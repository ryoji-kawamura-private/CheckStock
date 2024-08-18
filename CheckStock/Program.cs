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
using System.Threading.Tasks;

namespace CheckStock
{
	class Program
	{
		private static DiscordSettings DiscordSettings { get; } = (DiscordSettings)ConfigurationManager.GetSection("DiscordSettings");
		private static PollingUrlSettings PollingUrlSettings { get; } = (PollingUrlSettings)ConfigurationManager.GetSection("PollingUrlSettings");
		private static DiscordSocketClient DiscordClient { get; set; }
		private static SocketGuildUser[] GuildUsers { get; set; }


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

			foreach (UrlElement urlElement in PollingUrlSettings.Urls)
			{
				_ = CheckStockAsync(urlElement.Url, urlElement.CssSelector);
			}
		}

		private static async Task CheckStockAsync(string url, string cssSelector)
		{

			var currentValue = default(string);
			var previousValue = default(string);

			while (true)
			{
				Console.WriteLine(url);
				try
				{
					using (var browser = await Puppeteer.LaunchAsync(new LaunchOptions { Headless = true, ExecutablePath = ConfigurationManager.AppSettings["ChromePath"] }))
					{
						using (var page = await browser.NewPageAsync())
						{
							await page.GoToAsync(url);
							await Task.Delay(4000);
							var content = await page.GetContentAsync();
							using (var context = BrowsingContext.New(AngleSharp.Configuration.Default))
							using (var document = await context.OpenAsync(req => req.Content(content)))
							{
								// 特定の要素を取得する
								var elements = document.QuerySelectorAll(cssSelector); // クラス名に応じて変更
								if (elements.Any())
									currentValue = elements.ElementAt(0).InnerHtml;
							}
							await page.CloseAsync();
						}
						await browser.CloseAsync();
					}
				}
				catch (Exception ex)
				{
					Console.WriteLine("リクエストエラー" + ex + "\r\n" + url);
					currentValue = ex.Message;
					continue;
				}
				finally
				{

					if (previousValue != null && currentValue != previousValue)
						await SendDiscord(url);
					previousValue = currentValue;

					var seed = Environment.TickCount;
					var rnd = new Random(seed++);
					await Task.Delay(rnd.Next(1000, 11000));
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
	}
}
