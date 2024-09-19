using System.Net;
using System.Net.Sockets;
using System.Diagnostics;
using dds_shared_lib;


namespace UdpRelayServer;

public class RelayServer
{
    public bool m_IsRunning { get; private set; }
    public Dictionary<string, IPEndPoint> m_ConnectedPlayers = new Dictionary<string, IPEndPoint>(); // Hashed IDs are used as player IDs - NOTE: IPEndPoints themselves can cause equality issues
    public Dictionary<string, DateTime> m_PlayerLastActivity = new Dictionary<string, DateTime>();

    private const uint MAX_PLAYERS = 4;
    private const uint PROTOCOL_ID = 13439;  // SET CUSTOM PROTOCOL ID HERE
    private readonly object _lock = new object();
#nullable enable
    private UdpClient? m_UdpServer;


    public void StartRelayServer(ushort port)
    {
        Console.WriteLine("Starting relay server...");
        try
        {
            m_UdpServer = new UdpClient(port);
        }
        catch (Exception e)
        {
            Console.WriteLine($"[ERROR]: Failed to start relay server: {e.Message}");
            return;
        }

        Console.WriteLine($"[INFO]: Relay server started on port: {port}");
        m_IsRunning = true;
        m_UdpServer.Client.Blocking = false;

        Console.WriteLine("[INFO]: Will now begin listening for messages...");
        _ = ListenForMessages(); // Start listening without blocking
        _ = MonitorPlayerTimeouts(TimeSpan.FromSeconds(10)); // Start monitoring player timeouts
    }

    private async Task ListenForMessages()
    {
        Debug.Assert(m_UdpServer != null, "[ERROR]: UdpServer is null, did you call StartRelayServer?");

        while (m_IsRunning)
        {
            try
            {
                // Receive packet and return if null
                UdpReceiveResult result = await m_UdpServer.ReceiveAsync();
                Packet? packet = PacketManager.GetPacketFromData(result.Buffer, PROTOCOL_ID);
                if (packet == null)
                {  // NOTE: A wrong protocol ID will cause this to fail
                    Console.WriteLine($"[ERROR]: Failed to get packet from data: {result.Buffer}");
                    return;
                }

#if DEBUG
                Console.WriteLine($"[DEBUG]: Received packet: {packet.m_PacketType.ToString()}");
                Console.WriteLine($"[DEBUG]: Received packet data: {packet.m_Data.Length} bytes");
#endif
                lock (_lock)
                {
                    string senderId = GetHashedId(result.RemoteEndPoint);
                    // Update the last activity time for the sender
                    if (m_PlayerLastActivity.ContainsKey(senderId))
                    {
                        m_PlayerLastActivity[senderId] = DateTime.UtcNow;
                    }
                }

                switch (packet.m_PacketType)
                {
                    case Packet.PacketType.GamePacket:
                        await HandleGamePacket(packet as GamePacket, result.RemoteEndPoint);
                        break;
                    // TODO:
                    // case Packet.PacketType.PlayerPacket:
                    //     await HandlePlayerPacket(packet as PlayerPacket, remoteEndPoint);
                    //     break;
                    default:
                        Console.WriteLine($"[ERROR]: Unknown packet type: {packet.m_PacketType}");
                        break;
                }
            }
            catch (SocketException e)
            {
                Console.WriteLine($"[ERROR]: Failed to receive message: {e.Message}");
                await Task.Delay(100); // Add a small delay to prevent tight loop on error
            }
            catch (Exception e)
            {
                Console.WriteLine($"[ERROR]: Failed to receive message: {e.Message}");
                await Task.Delay(100); // Add a small delay to prevent tight loop on error
            }
        }
    }

    private async Task HandleGamePacket(GamePacket packet, IPEndPoint remoteEndPoint)
    {

        switch (packet.m_OpCode)
        {
            case GamePacket.OpCode.PlayerJoin:
                await ConnectNewPlayer(remoteEndPoint);
                break;
            case GamePacket.OpCode.PlayerLeave:
                DisconnectPlayer(remoteEndPoint);
                break;
            default:
                Console.WriteLine($"[ERROR]: Unknown game packet op code: {packet.m_OpCode}");
                break;
        }
    }

    private async Task ConnectNewPlayer(IPEndPoint remoteEndPoint)
    {
        string playerId = GetHashedId(remoteEndPoint);
        if (m_ConnectedPlayers.ContainsKey(playerId))
        {
            Console.WriteLine($"[ERROR]: Player already connected: {remoteEndPoint.Address}:{remoteEndPoint.Port}");
            return;
        }
        if (m_ConnectedPlayers.Count >= MAX_PLAYERS)
        {
            Console.WriteLine($"[ERROR]: Maximum number of players reached: {MAX_PLAYERS}");
            return;
        }

        lock (_lock)
        {
            m_ConnectedPlayers.Add(playerId, remoteEndPoint);
            m_PlayerLastActivity.Add(playerId, DateTime.UtcNow);
        }

        Console.WriteLine($"[INFO]: New player connected: {remoteEndPoint.Address}:{remoteEndPoint.Port}");
        GamePacket connectPacket = new GamePacket(GamePacket.OpCode.PlayerJoin); // Construct a new game packet to return

        // Add player ID + welcome message to the packet data
        using (MemoryStream ms = new MemoryStream())
        {
            byte[] playerIdBytes = System.Text.Encoding.UTF8.GetBytes(playerId);
            byte[] playerIdLengthBytes = BitConverter.GetBytes(playerIdBytes.Length);
            byte[] welcomeMessage = System.Text.Encoding.UTF8.GetBytes($"(Server) [INFO]: You have successfully connected to the relay server!");

            ms.Write(playerIdLengthBytes, 0, playerIdLengthBytes.Length);
            ms.Write(playerIdBytes, 0, playerIdBytes.Length);
            ms.Write(welcomeMessage, 0, welcomeMessage.Length);
            connectPacket.m_Data = ms.ToArray();
        }

        await PacketManager.SendPacket(connectPacket, m_UdpServer, PROTOCOL_ID, remoteEndPoint);
    }

    // This does not send back a disconnect packet
    private void DisconnectPlayer(string playerId)
    {
        if (!m_ConnectedPlayers.ContainsKey(playerId))
        {
            Console.WriteLine($"[ERROR]: Player not connected: {playerId}");
            return;
        }

        lock (_lock)
        {
            m_ConnectedPlayers.Remove(playerId);
            m_PlayerLastActivity.Remove(playerId);
        }
        Console.WriteLine($"[INFO]: Player disconnected: {playerId}");

        if (m_ConnectedPlayers.Count == 0)
        {
            Console.WriteLine("[INFO]: No players are connected, stopping the server...");
            StopRelayServer();
        }
        else
        {
            Console.WriteLine("[INFO]: Remaining Connected players:");
            foreach (var kvp in m_ConnectedPlayers)
            {
                Console.WriteLine($"[INFO]: Connected player: {kvp.Key} - {kvp.Value}");
            }
        }
    }

    private void DisconnectPlayer(IPEndPoint remoteEndPoint)
    {
        string playerId = GetHashedId(remoteEndPoint);
        DisconnectPlayer(playerId);
    }

    private async Task MonitorPlayerTimeouts(TimeSpan timeoutInterval)
    {
        while (m_IsRunning)
        {
            await Task.Delay(1000); // Check every second
            List<string> timedOutPlayers = new List<string>();

            lock (_lock)
            {
                var now = DateTime.UtcNow;

                foreach (var playerId in m_ConnectedPlayers.Keys)
                {
                    if (now - m_PlayerLastActivity[playerId] >= timeoutInterval)
                    {
                        timedOutPlayers.Add(playerId);
                    }
                }

            }

            foreach (var playerId in timedOutPlayers)
            {
                Console.WriteLine($"[INFO]: Player {playerId} timed out, disconnecting...");
                DisconnectPlayer(playerId);
            }
        }
    }

    // Input: IPEndPoint
    // Output: Hashed ID as a string
    private string GetHashedId(IPEndPoint endPoint)
    {
        using (var sha256 = System.Security.Cryptography.SHA256.Create())
        {
            byte[] hashBytes = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(endPoint.ToString()));
            return Convert.ToBase64String(hashBytes);
        }
    }

    public void StopRelayServer()
    {
        m_IsRunning = false;
        m_UdpServer?.Close();
        Console.WriteLine("[INFO]: Relay server stopped.");
    }

    public static void Main()
    {
        ushort port = 13439;
        RelayServer relayServer = new RelayServer();

        relayServer.StartRelayServer(port);

        // Keep the server running until Enter is pressed
        Console.WriteLine("Press Enter to stop the server...");
        Console.ReadLine();
        relayServer.StopRelayServer();
    }

    //public void SendMessageToPlayer(int playerId, string message)
}
