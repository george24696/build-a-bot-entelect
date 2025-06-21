using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using BuildABot2025.Models;
using BuildABot2025.Services;

public class Program
{
    public static async Task Main(string[] args)
    {
        // Load config
        var builder = new ConfigurationBuilder().AddJsonFile("appsettings.json", optional: false);
        var configuration = builder.Build();

        // Get IP and port
        var ip = Environment.GetEnvironmentVariable("RUNNER_IPV4") ?? configuration["RunnerIP"];
        if (!ip.StartsWith("http://")) ip = $"http://{ip}";
        var port = configuration["RunnerPort"];
        var url = $"{ip}:{port}/bothub";

        // Get nickname and token
        var nickname = Environment.GetEnvironmentVariable("BOT_NICKNAME") ?? configuration["BotNickname"];
        var token = Environment.GetEnvironmentVariable("Token") ?? Environment.GetEnvironmentVariable("REGISTRATION_TOKEN");

        // Setup connection
        var connection = new HubConnectionBuilder()
            .WithUrl(url)
            .ConfigureLogging(logging => logging.SetMinimumLevel(LogLevel.Debug))
            .WithAutomaticReconnect()
            .Build();

        var botService = new BotService();
        BotCommand? botCommand = new BotCommand();

        // Register event handlers
        connection.On<Guid>("Registered", id => botService.SetBotId(id));

        connection.On<string>("Disconnect", async reason =>
        {
            Console.WriteLine($"Server sent disconnect: {reason}");
            await connection.StopAsync();
        });

        connection.On<GameState>("GameState", gamestate =>
        {
            botCommand = botService.ProcessState(gamestate);
        });

        // Handle disconnection
        connection.Closed += (error) =>
        {
            Console.WriteLine($"Connection closed: {error?.Message}");
            return Task.CompletedTask;
        };

        // Start connection
        await connection.StartAsync();
        Console.WriteLine("Connected to Runner");

        // Register the bot
        await connection.InvokeAsync("Register", token, nickname);

        // Main game loop
        while (connection.State == HubConnectionState.Connected)
        {
            if (botCommand == null || (int)botCommand.Action is < 1 or > 5)
                continue;

            await connection.SendAsync("BotCommand", botCommand);
            botCommand = null;
        }
    }
}
