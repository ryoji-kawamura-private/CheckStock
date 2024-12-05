using AngleSharp;
using AngleSharp.Html.Dom;
using AngleSharp.Html.Parser;
using Discord;
using Discord.WebSocket;
using PuppeteerSharp;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
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
		private static SemaphoreSlim Semaphore { get; } = new SemaphoreSlim(2); // 同時実行数を5に設定
		private static IBrowser Browser { get; set; }


		static async Task Main(string[] args)
		{
			Browser = await Puppeteer.LaunchAsync(new LaunchOptions { Headless = true, ExecutablePath = ConfigurationManager.AppSettings["ChromePath"], Args = new[] { "--disable-blink-features=AutomationControlled" }, });
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
				Console.WriteLine(url);
				try
				{
					using (var page = await Browser.NewPageAsync())
					{
						try
						{
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
									if (!string.IsNullOrEmpty(excludeWord) && (!currentValue.Contains(excludeWord)) || (!string.IsNullOrEmpty(includeWord) && currentValue.Contains(includeWord)))
									{
										Log($"CurrentValue:{currentValue}");
										await SendDiscord(url);
									}
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
					//await Browser.CloseAsync();
					//Browser = await Puppeteer.LaunchAsync(new LaunchOptions { Headless = false, ExecutablePath = ConfigurationManager.AppSettings["ChromePath"], Args = new[] { "--window-size=200,100" }, });
					Console.WriteLine("リクエストエラー" + "\r\n" + url + "\r\n" + ex);
					continue;
				}
				finally
				{
					Semaphore.Release();
					await Task.Delay(2000);
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
				// ディレクトリが存在しない場合は作成
				if (!Directory.Exists(directoryPath))
				{
					Directory.CreateDirectory(directoryPath);
				}

				// ファイル名を生成
				var fileName = Path.Combine(directoryPath, DateTime.Now.ToString("yyyyMMdd") + ".log");

				// メッセージにタイムスタンプを追加
				var logEntry = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " " + msg + Environment.NewLine;

				// ファイルにメッセージを追記
				File.AppendAllText(fileName, logEntry);

				// コンソールにも出力
				Console.WriteLine(logEntry);
			}
			catch (Exception ex)
			{
				// 例外処理（ログの書き込みに失敗した場合）
				Console.Error.WriteLine("Logging failed: " + ex.Message);
			}
		}
	}
}
