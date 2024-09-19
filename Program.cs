using System.Net;
using System.Net.Sockets;
using System.Diagnostics;
using dds_shared_lib;


namespace UdpRelayServer;

public class RelayServer
{
    public bool m_IsRunning { get; private set; }
    public List<IPEndPoint> m_ConnectedPlayers = new List<IPEndPoint>(); // IPEndPoints are used as player IDs
    public Dictionary<IPEndPoint, DateTime> m_PlayerLastActivity = new Dictionary<IPEndPoint, DateTime>();

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
                // Update the last activity time for the sender
                if (m_PlayerLastActivity.ContainsKey(result.RemoteEndPoint))
                {
                    m_PlayerLastActivity[result.RemoteEndPoint] = DateTime.Now;
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
        if (m_ConnectedPlayers.Contains(remoteEndPoint))
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
            m_ConnectedPlayers.Add(remoteEndPoint);
            m_PlayerLastActivity.Add(remoteEndPoint, DateTime.Now);
        }

        Console.WriteLine($"[INFO]: New player connected: {remoteEndPoint.Address}:{remoteEndPoint.Port}");
        GamePacket connectPacket = new GamePacket(GamePacket.OpCode.PlayerJoin); // Construct a new game packet to return

        // Add player ID + welcome message to the packet data
        using (MemoryStream ms = new MemoryStream())
        {
            byte[] welcomeMessage = System.Text.Encoding.UTF8.GetBytes($"(Server) [INFO]: You have successfully connected to the relay server!");
            ms.Write(welcomeMessage, 0, welcomeMessage.Length);
            connectPacket.m_Data = ms.ToArray();
        }

        await PacketManager.SendPacket(connectPacket, m_UdpServer, PROTOCOL_ID, remoteEndPoint);
    }

    // This does not send back a disconnect packet
    private void DisconnectPlayer(IPEndPoint remoteEndPoint)
    {
        if (!m_ConnectedPlayers.Contains(remoteEndPoint))
        {
            Console.WriteLine($"[ERROR]: Player not connected: {remoteEndPoint.Address}:{remoteEndPoint.Port}");
            return;
        }

        lock (_lock)
        {
            m_ConnectedPlayers.Remove(remoteEndPoint);
            m_PlayerLastActivity.Remove(remoteEndPoint);
        }
        Console.WriteLine($"[INFO]: Player disconnected: {remoteEndPoint.Address}:{remoteEndPoint.Port}");

        if (m_ConnectedPlayers.Count == 0)
        {
            Console.WriteLine("[INFO]: No players are connected, stopping the server...");
            StopRelayServer();
        }
        else
        {
            Console.WriteLine("[INFO]: Remaining Connected players:");
            foreach (var playerEndPoint in m_ConnectedPlayers)
            {
                Console.WriteLine($"[INFO]: Connected player: {playerEndPoint.Address}:{playerEndPoint.Port}");
            }
        }
    }

    private async Task MonitorPlayerTimeouts(TimeSpan timeoutInterval)
    {
        while (m_IsRunning)
        {
            await Task.Delay(1000); // Check every second
            List<IPEndPoint> timedOutPlayers = new List<IPEndPoint>();

            if (m_ConnectedPlayers.Count == 0)
            {
                return;
            }


            lock (_lock)
            {
                var now = DateTime.Now;

                foreach (IPEndPoint playerEndPoint in m_ConnectedPlayers)
                {
                    if (now - m_PlayerLastActivity[playerEndPoint] >= timeoutInterval)
                    {
                        timedOutPlayers.Add(playerEndPoint);
                    }
                }

                foreach (IPEndPoint playerEndPoint in timedOutPlayers)
                {
                    Console.WriteLine($"[INFO]: Player {playerEndPoint.ToString()} timed out, disconnecting...");
                    DisconnectPlayer(playerEndPoint);
                }
            }
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
