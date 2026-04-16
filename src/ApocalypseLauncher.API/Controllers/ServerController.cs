using Microsoft.AspNetCore.Mvc;

namespace ApocalypseLauncher.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ServerController : ControllerBase
{
    private readonly ILogger<ServerController> _logger;
    private readonly IConfiguration _configuration;

    public ServerController(ILogger<ServerController> logger, IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
    }

    [HttpGet("status")]
    public async Task<ActionResult<ServerStatusResponse>> GetStatus()
    {
        try
        {
            // Получаем адрес Minecraft сервера из конфигурации
            var serverAddress = _configuration["MinecraftServer:Address"] ?? "localhost";
            var serverPort = int.Parse(_configuration["MinecraftServer:Port"] ?? "25565");

            // Проверяем доступность сервера (простая проверка TCP порта)
            bool isOnline = await CheckServerOnlineAsync(serverAddress, serverPort);

            // TODO: Реализовать получение реального количества игроков через Minecraft Server Query
            // Пока возвращаем заглушку
            int playersOnline = isOnline ? 0 : 0;
            int maxPlayers = 100;

            return Ok(new ServerStatusResponse
            {
                IsOnline = isOnline,
                PlayersOnline = playersOnline,
                MaxPlayers = maxPlayers,
                ServerVersion = "1.20.1 Forge",
                Motd = "SRP-RP Server"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking server status");
            return Ok(new ServerStatusResponse
            {
                IsOnline = false,
                PlayersOnline = 0,
                MaxPlayers = 100,
                ServerVersion = "1.20.1 Forge",
                Motd = "SRP-RP Server"
            });
        }
    }

    private async Task<bool> CheckServerOnlineAsync(string address, int port)
    {
        try
        {
            using var client = new System.Net.Sockets.TcpClient();
            var connectTask = client.ConnectAsync(address, port);
            var timeoutTask = Task.Delay(3000); // 3 секунды таймаут

            var completedTask = await Task.WhenAny(connectTask, timeoutTask);

            if (completedTask == connectTask && client.Connected)
            {
                return true;
            }

            return false;
        }
        catch
        {
            return false;
        }
    }
}

public class ServerStatusResponse
{
    public bool IsOnline { get; set; }
    public int PlayersOnline { get; set; }
    public int MaxPlayers { get; set; }
    public string ServerVersion { get; set; } = "";
    public string Motd { get; set; } = "";
}
