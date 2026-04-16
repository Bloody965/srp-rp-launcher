using Microsoft.AspNetCore.Mvc;
using ApocalypseLauncher.API.Services;

namespace ApocalypseLauncher.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ServerController : ControllerBase
{
    private readonly ILogger<ServerController> _logger;
    private readonly IConfiguration _configuration;
    private readonly MinecraftServerService _minecraftService;

    public ServerController(ILogger<ServerController> logger, IConfiguration configuration, MinecraftServerService minecraftService)
    {
        _logger = logger;
        _configuration = configuration;
        _minecraftService = minecraftService;
    }

    [HttpGet("status")]
    public async Task<ActionResult<ServerStatusResponse>> GetStatus()
    {
        try
        {
            // Получаем адрес Minecraft сервера из конфигурации
            var serverAddress = _configuration["MinecraftServer:Address"] ?? "localhost";
            var serverPort = int.Parse(_configuration["MinecraftServer:Port"] ?? "25565");

            // Получаем информацию о сервере
            var serverInfo = await _minecraftService.GetServerInfoAsync(serverAddress, serverPort);

            return Ok(new ServerStatusResponse
            {
                IsOnline = serverInfo.IsOnline,
                PlayersOnline = serverInfo.PlayersOnline,
                MaxPlayers = serverInfo.MaxPlayers,
                ServerVersion = serverInfo.ServerVersion,
                Motd = serverInfo.Motd
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
}

public class ServerStatusResponse
{
    public bool IsOnline { get; set; }
    public int PlayersOnline { get; set; }
    public int MaxPlayers { get; set; }
    public string ServerVersion { get; set; } = "";
    public string Motd { get; set; } = "";
}
