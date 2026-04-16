using System.Net.Sockets;
using System.Text;

namespace ApocalypseLauncher.API.Services;

public class MinecraftServerService
{
    public async Task<MinecraftServerInfo> GetServerInfoAsync(string address, int port)
    {
        try
        {
            using var client = new TcpClient();
            var connectTask = client.ConnectAsync(address, port);
            var timeoutTask = Task.Delay(5000);

            var completedTask = await Task.WhenAny(connectTask, timeoutTask);

            if (completedTask == timeoutTask || !client.Connected)
            {
                return new MinecraftServerInfo { IsOnline = false };
            }

            using var stream = client.GetStream();

            // Отправляем Handshake пакет
            var handshake = CreateHandshakePacket(address, port);
            await stream.WriteAsync(handshake);

            // Отправляем Status Request пакет
            var statusRequest = new byte[] { 0x01, 0x00 };
            await stream.WriteAsync(statusRequest);

            // Читаем ответ
            var response = await ReadResponseAsync(stream);

            if (response != null)
            {
                var serverInfo = ParseServerResponse(response);
                serverInfo.IsOnline = true;
                return serverInfo;
            }

            return new MinecraftServerInfo { IsOnline = false };
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MinecraftServerService] Error: {ex.Message}");
            return new MinecraftServerInfo { IsOnline = false };
        }
    }

    private byte[] CreateHandshakePacket(string address, int port)
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);

        // Packet ID (0x00 для handshake)
        WriteVarInt(writer, 0x00);
        // Protocol version (-1 для status)
        WriteVarInt(writer, -1);
        // Server address
        WriteVarInt(writer, address.Length);
        writer.Write(Encoding.UTF8.GetBytes(address));
        // Server port
        writer.Write((byte)(port >> 8));
        writer.Write((byte)(port & 0xFF));
        // Next state (1 для status)
        WriteVarInt(writer, 1);

        var data = ms.ToArray();
        var packet = new byte[data.Length + GetVarIntSize(data.Length)];

        using var packetMs = new MemoryStream(packet);
        using var packetWriter = new BinaryWriter(packetMs);
        WriteVarInt(packetWriter, data.Length);
        packetWriter.Write(data);

        return packet;
    }

    private async Task<string?> ReadResponseAsync(NetworkStream stream)
    {
        try
        {
            // Читаем длину пакета
            var length = await ReadVarIntAsync(stream);
            if (length <= 0 || length > 32767) return null;

            // Читаем packet ID
            var packetId = await ReadVarIntAsync(stream);
            if (packetId != 0x00) return null;

            // Читаем длину JSON строки
            var jsonLength = await ReadVarIntAsync(stream);
            if (jsonLength <= 0 || jsonLength > 32767) return null;

            // Читаем JSON
            var buffer = new byte[jsonLength];
            var totalRead = 0;
            while (totalRead < jsonLength)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(totalRead, jsonLength - totalRead));
                if (read == 0) return null;
                totalRead += read;
            }

            return Encoding.UTF8.GetString(buffer);
        }
        catch
        {
            return null;
        }
    }

    private MinecraftServerInfo ParseServerResponse(string json)
    {
        try
        {
            var doc = System.Text.Json.JsonDocument.Parse(json);
            var root = doc.RootElement;

            var info = new MinecraftServerInfo { IsOnline = true };

            if (root.TryGetProperty("players", out var players))
            {
                if (players.TryGetProperty("online", out var online))
                    info.PlayersOnline = online.GetInt32();

                if (players.TryGetProperty("max", out var max))
                    info.MaxPlayers = max.GetInt32();
            }

            if (root.TryGetProperty("version", out var version))
            {
                if (version.TryGetProperty("name", out var name))
                    info.ServerVersion = name.GetString() ?? "";
            }

            if (root.TryGetProperty("description", out var description))
            {
                if (description.ValueKind == System.Text.Json.JsonValueKind.String)
                {
                    info.Motd = description.GetString() ?? "";
                }
                else if (description.TryGetProperty("text", out var text))
                {
                    info.Motd = text.GetString() ?? "";
                }
            }

            return info;
        }
        catch
        {
            return new MinecraftServerInfo { IsOnline = true };
        }
    }

    private void WriteVarInt(BinaryWriter writer, int value)
    {
        while ((value & -128) != 0)
        {
            writer.Write((byte)(value & 127 | 128));
            value = (int)((uint)value >> 7);
        }
        writer.Write((byte)value);
    }

    private async Task<int> ReadVarIntAsync(NetworkStream stream)
    {
        int numRead = 0;
        int result = 0;
        byte read;
        do
        {
            var buffer = new byte[1];
            var bytesRead = await stream.ReadAsync(buffer);
            if (bytesRead == 0) return -1;

            read = buffer[0];
            int value = read & 0x7F;
            result |= value << (7 * numRead);

            numRead++;
            if (numRead > 5) return -1;
        } while ((read & 0x80) != 0);

        return result;
    }

    private int GetVarIntSize(int value)
    {
        int size = 0;
        while ((value & -128) != 0)
        {
            size++;
            value = (int)((uint)value >> 7);
        }
        return size + 1;
    }
}

public class MinecraftServerInfo
{
    public bool IsOnline { get; set; }
    public int PlayersOnline { get; set; }
    public int MaxPlayers { get; set; } = 100;
    public string ServerVersion { get; set; } = "1.20.1";
    public string Motd { get; set; } = "";
}
