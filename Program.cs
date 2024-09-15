using System;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using System.Diagnostics;
using Game.Networking;

public class RelayServer
{
    public bool m_IsRunning { get; private set; }
    public byte nextPlayerId { get; private set; } = 0;
    public Dictionary<ushort, IPEndPoint> m_ConnectedPlayers = new Dictionary<ushort, IPEndPoint>();

    private const uint PROTOCOL_ID = 13439; // SET CUSTOM PROTOCOL ID HERE
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
    }

    private async Task ListenForMessages()
    {
        Debug.Assert(m_UdpServer != null, "[ERROR]: UdpServer is null, did you call StartRelayServer?");

        while (m_IsRunning)
        {
            try
            {
                UdpReceiveResult result = await m_UdpServer.ReceiveAsync();
                await HandleIncomingMessage(result.Buffer, result.RemoteEndPoint);
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

    private async Task HandleIncomingMessage(byte[] data, IPEndPoint remoteEndPoint)
    {
        // NOTE: this shouldn't be done like this because it's completely vulnerable to DDoS like attacks
        // TODO: implement a game connect packet instead

        if (!m_ConnectedPlayers.ContainsValue(remoteEndPoint))
        {
            await ConnectNewPlayer(remoteEndPoint);
        }
        // TODO: Process incoming data
    }

    public async Task ConnectNewPlayer(IPEndPoint remoteEndPoint)
    {
        Console.WriteLine($"[INFO]: New player connected: {remoteEndPoint.Address}:{remoteEndPoint.Port}");
        byte newPlayerId;
        lock (_lock)
        {
            newPlayerId = nextPlayerId++;
            m_ConnectedPlayers.Add(newPlayerId, remoteEndPoint);
        }

        // TODO:
        // m_UdpServer?.Send(new byte[] { newPlayerId }, 1, m_ConnectedPlayers[newPlayerId]);
        // GamePacket connectPacket = new GamePacket(GamePacket.OpCode.PlayerJoin);
        // connectPacket.m_Data = new byte[] { newPlayerId };
        // await SendPacket(m_UdpServer, connectPacket);

        byte[] welcomeMessage = System.Text.Encoding.UTF8.GetBytes($"(Server) [INFO]: You have successfully connected to the relay server!");
        await m_UdpServer?.SendAsync(welcomeMessage, welcomeMessage.Length, m_ConnectedPlayers[newPlayerId]);
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

    // TODO: Implement a shared packet handler for all clients and servers
    // - This should include SendPacket and GetPacketFromData methods
}

// IPEndPoint clientEndPoint = new IPEndPoint(IPAddress.Any, 0);
// while (true)
// {
//     byte[] receivedData = udpServer.Receive(ref clientEndPoint);
//
//     string receivedMessage = System.Text.Encoding.UTF8.GetString(receivedData);
//     Console.WriteLine($"Received message from: {clientEndPoint.Address} on port: {clientEndPoint.Port}");
//     Console.WriteLine($"Received message: {receivedMessage}");
//
//     byte[] responseMessage = System.Text.Encoding.UTF8.GetBytes("Message received client! (This is server)");
//     udpServer.Send(responseMessage, responseMessage.Length, clientEndPoint);
// }
