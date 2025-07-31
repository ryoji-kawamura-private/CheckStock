using AngleSharp;
using Discord;
using Discord.WebSocket;
using PuppeteerSharp;
using System;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Threading;
using System.Threading.Tasks;

namespace CheckStock
{
	class Program
	{
		private static DiscordSettings DiscordSettings { get; } = (DiscordSettings)ConfigurationManager.GetSection("DiscordSettings");
		private static PollingUrlSettings PollingUrlSettings { get; } = (PollingUrlSettings)ConfigurationManager.GetSection("PollingUrlSettings");
		private static DiscordSocketClient DiscordClient { get; set; }
		private static SocketGuildUser[] GuildUsers { get; set; }
		private static SemaphoreSlim Semaphore { get; } = new SemaphoreSlim(3); // 同時実行数を3に設定
		private static string TempProfilePathPrefix { get; } = Path.Combine(Path.GetTempPath(), "puppeteer");

		static async Task Main(string[] args)
		{
			DiscordClient = await GetDiscordClientAsync();
			DiscordClient.Ready += OnReadyAsync;
			await Task.Delay(-1);
		}

		private static async Task<DiscordSocketClient> GetDiscordClientAsync()
		{
			var config = new DiscordSocketConfig
			{
				GatewayIntents = GatewayIntents.Guilds | GatewayIntents.GuildMessages | GatewayIntents.GuildMessageTyping | GatewayIntents.GuildMembers
			};
			var discordClient = new DiscordSocketClient(config);
			discordClient.Log += Log;
			// Botトークンをここに設定
			var botToken = DiscordSettings.BotToken;

			await discordClient.LoginAsync(TokenType.Bot, botToken);
			await discordClient.StartAsync();
			return discordClient;
		}

		private static async Task OnReadyAsync()
		{
			var guildId = DiscordSettings.GuildId;
			var guild = DiscordClient.GetGuild(guildId);

			if (guild == null)
			{
				Console.WriteLine($"Discord Guild with ID {guildId} not found.");
				throw new Exception($"Discord Guild with ID {guildId} not found.");
			}

			await guild.DownloadUsersAsync();

			var userIds = DiscordSettings.UserIds.GetElements();
			GuildUsers = userIds.Select(userId => guild.GetUser(userId.Value)).Where(user => user != null).ToArray();

			if (!GuildUsers.Any())
			{
				Console.WriteLine("Discord User not found.");
				throw new Exception("Discord User not found.");
			}

			KillChromeProcesses();
			var tempRoot = Path.GetTempPath(); // 例: C:\Users\ユーザー名\AppData\Local\Temp\
			DeleteMatchingDirectories(TempProfilePathPrefix);


			foreach (UrlElement urlElement in PollingUrlSettings.Urls)
			{
				_ = CheckStockAsync(urlElement.Url, urlElement.Selector, urlElement.ExcludeWord, urlElement.IncludeWord);
			}
		}

		private static async Task CheckStockAsync(string url, string selector, string excludeWord, string includeWord)
		{
			while (true)
			{
				await Semaphore.WaitAsync(); // セマフォでリソースを確保
				var sendedDiscord = false;

				var tempProfileDir = Path.Combine(TempProfilePathPrefix, Guid.NewGuid().ToString());

				using (var Browser = await Puppeteer.LaunchAsync(new LaunchOptions { Headless = true, ExecutablePath = ConfigurationManager.AppSettings["ChromePath"], Args = new[] { "--disable-blink-features=AutomationControlled" }, UserDataDir = tempProfileDir }))
				{
					Console.WriteLine(url);
					try
					{
						using (var page = (await Browser.PagesAsync()).First())
						{
							try
							{
								await page.DeleteCookieAsync();
								var userAgent = await page.EvaluateExpressionAsync<string>("navigator.userAgent");
								userAgent = userAgent.Replace("Headless", "");
								await page.SetUserAgentAsync(userAgent);
								var response = await page.GoToAsync(url, new NavigationOptions
								{
									Timeout = 60000, // 1分
								});
								var headers = response.Headers;
								headers["content-type"] = "utf-8";
								await Task.Delay(1500);
								var content = await page.GetContentAsync();
								await page.CloseAsync();
								using (var context = BrowsingContext.New(AngleSharp.Configuration.Default))
								using (var document = await context.OpenAsync(req => req.Content(content).Header("Content-Type", "text/html; charset=utf-8")))
								{
									var elements = document.QuerySelectorAll(selector);
									if (elements.Any())
									{
										var currentValue = elements.ElementAt(0).InnerHtml;
										if (!string.IsNullOrEmpty(excludeWord) && (!currentValue.Contains(excludeWord)) || (!string.IsNullOrEmpty(includeWord) && currentValue.IndexOf(includeWord, StringComparison.OrdinalIgnoreCase) >= 0))
										{
											Log($"CurrentValue:{currentValue}");
											await SendDiscord(url);
											sendedDiscord = true;
										}
									}
									else
									{
										Console.WriteLine($"Url:{url} Selector:{selector} エレメントが見つかりません");
									}
								}
							}
							finally
							{
								await page.CloseAsync();
							}
						}
					}
					catch (Exception ex)
					{
						Console.WriteLine("リクエストエラー:" + url + "\r\n" + ex);
						continue;
					}
					finally
					{
						await Browser.CloseAsync();
						try
						{
							if (Directory.Exists(tempProfileDir))
								Directory.Delete(tempProfileDir, true);
						}
						catch (Exception ex)
						{
							Log($"一時ディレクトリ削除失敗: {tempProfileDir}, {ex.Message}");
						}
						Semaphore.Release();
					}
				}
				if (sendedDiscord)
				{
					await Task.Delay(300000);
				}
			}
		}

		private static async Task SendDiscord(string url)
		{
			try
			{
				foreach (var guildUser in GuildUsers)
				{
					// ユーザーにDMを送信
					await guildUser.SendMessageAsync(url);
					Console.WriteLine("Discord Message sent!");
				}
			}
			catch (Exception ex)
			{
				Console.WriteLine("Discord Message sent failed!");
				Console.WriteLine(ex);
			}
		}

		private static Task Log(LogMessage msg)
		{
			Console.WriteLine(msg.ToString());
			return Task.CompletedTask;
		}

		private static void Log(string msg)
		{
			const string directoryPath = @".\Logs\";
			try
			{
				// メッセージにタイムスタンプを追加
				var logEntry = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " " + msg + Environment.NewLine;

				// コンソールにも出力
				Console.WriteLine(logEntry);

				// ディレクトリが存在しない場合は作成
				if (!Directory.Exists(directoryPath))
				{
					Directory.CreateDirectory(directoryPath);
				}

				// ファイル名を生成
				var fileName = Path.Combine(directoryPath, DateTime.Now.ToString("yyyyMMdd") + ".log");

				// ファイルにメッセージを追記
				File.AppendAllText(fileName, logEntry);
			}
			catch (Exception ex)
			{
				// 例外処理（ログの書き込みに失敗した場合）
				Console.Error.WriteLine("Logging failed: " + ex.Message);
			}
		}

		private static void KillChromeProcesses()
		{

			foreach (var proc in Process.GetProcessesByName("chrome"))
			{
				try
				{
					// コマンドライン引数にプロファイルフォルダが含まれているか
					var cmd = GetCommandLine(proc);  // ※下述 API or ManagementObjectSearcher などで取得
					if (cmd != null && cmd.IndexOf(TempProfilePathPrefix, StringComparison.OrdinalIgnoreCase) >= 0)
					{
						proc.Kill();
						proc.WaitForExit();
						Console.WriteLine($"プロセス終了: {proc.ProcessName} (PID: {proc.Id})");
					}
				}
				catch (Exception ex)
				{
					Log($"取得失敗/終了失敗: PID={proc.Id}, {ex.Message}");
				}
			}
		}

		private static string GetCommandLine(Process process)
		{
			try
			{
				using (var searcher = new ManagementObjectSearcher(
					$"SELECT CommandLine FROM Win32_Process WHERE ProcessId = {process.Id}"))
				using (var results = searcher.Get())
				{
					return results.Cast<ManagementObject>()
						.Select(mo => mo["CommandLine"]?.ToString())
						.FirstOrDefault();
				}
			}
			catch
			{
				return null;
			}
		}
		private static void DeleteMatchingDirectories(string startsWith)
		{
			string parentDir = Path.GetDirectoryName(startsWith);
			if (string.IsNullOrWhiteSpace(parentDir) || !Directory.Exists(parentDir))
			{
				Console.WriteLine($"親ディレクトリが存在しません: {parentDir}");
				return;
			}

			foreach (var dir in Directory.GetDirectories(parentDir))
			{
				if (dir.StartsWith(startsWith, StringComparison.OrdinalIgnoreCase))
				{
					try
					{
						Directory.Delete(dir, true);
						Console.WriteLine($"削除: {dir}");
					}
					catch (Exception ex)
					{
						Console.WriteLine($"削除失敗: {dir} - {ex.Message}");
					}
				}
			}
		}
	}
}
