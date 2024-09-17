using System;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Collections.Generic;
using System.IO;
using dds_shared_lib;

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
        Packet? packet = PacketManager.GetPacketFromData(data, PROTOCOL_ID);
        if (packet == null)
        {  // NOTE: A wrong protocol ID will cause this to fail
            Console.WriteLine($"[ERROR]: Failed to get packet from data: {data}");
            return;
        }

        switch (packet.m_PacketType)
        {
            case Packet.PacketType.GamePacket:
                await HandleGamePacket(packet as GamePacket, remoteEndPoint);
                break;
            // TODO:
            // case Packet.PacketType.PlayerPacket:
            //     await HandlePlayerPacket(packet as PlayerPacket, remoteEndPoint);
            //     break;
            default:
                Console.WriteLine($"[ERROR]: Unknown packet type: {packet.m_PacketType}");
                break;
        }

        // TODO: Process incoming data
    }

    private async Task HandleGamePacket(GamePacket packet, IPEndPoint remoteEndPoint)
    {
        Console.WriteLine($"[INFO]: Received game packet: {packet.m_OpCode.ToString()}");

        switch (packet.m_OpCode)
        {
            case GamePacket.OpCode.PlayerJoin:
                await ConnectNewPlayer(remoteEndPoint);
                break;
            // TODO:
            // case GamePacket.OpCode.PlayerLeave:
            //     await DisconnectPlayer(remoteEndPoint);
            //     break;
            default:
                Console.WriteLine($"[ERROR]: Unknown game packet op code: {packet.m_OpCode}");
                break;
        }
    }

    public async Task ConnectNewPlayer(IPEndPoint remoteEndPoint)
    {
        if (m_ConnectedPlayers.ContainsValue(remoteEndPoint))
        {
            Console.WriteLine($"[ERROR]: Player already connected: {remoteEndPoint.Address}:{remoteEndPoint.Port}");
            return;
        }

        byte newPlayerId;
        byte[] welcomeMessage = System.Text.Encoding.UTF8.GetBytes($"(Server) [INFO]: You have successfully connected to the relay server!");
        lock (_lock)
        {
            newPlayerId = nextPlayerId++;
            m_ConnectedPlayers.Add(newPlayerId, remoteEndPoint);
        }

        Console.WriteLine($"[INFO]: New player connected: {remoteEndPoint.Address}:{remoteEndPoint.Port} (Player ID: {newPlayerId})");
        GamePacket connectPacket = new GamePacket(GamePacket.OpCode.PlayerJoin); // Construct a new game packet to return

        // Add player ID + welcome message to the packet data
        using (MemoryStream ms = new MemoryStream())
        {
            ms.WriteByte(newPlayerId);
            ms.Write(welcomeMessage, 0, welcomeMessage.Length);
            connectPacket.m_Data = ms.ToArray();
        }

        await PacketManager.SendPacket(connectPacket, m_UdpServer, PROTOCOL_ID, remoteEndPoint);
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
