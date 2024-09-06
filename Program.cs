using System;
using System.Net;
using System.Net.Sockets;

public class UdpRelayServer
{
    public void StartRelayServer(int port)
    {
        UdpClient udpServer = new UdpClient(port);
        // Temporarily not setting blocking to false to avoid exception
        //udpServer.Client.Blocking = false;
        Console.WriteLine($"Relay server started on port: {port}");

        IPEndPoint clientEndPoint = new IPEndPoint(IPAddress.Any, 0);
        while (true)
        {
            byte[] receivedData = udpServer.Receive(ref clientEndPoint);
            udpServer.Send(receivedData, receivedData.Length, clientEndPoint);

            string receivedMessage = System.Text.Encoding.UTF8.GetString(receivedData);
            Console.WriteLine($"Received message from: {clientEndPoint.Address} on port: {clientEndPoint.Port}");
            Console.WriteLine($"Received message: {receivedMessage}");

            byte[] responseMessage = System.Text.Encoding.UTF8.GetBytes("Message received client! (This is server)");
            udpServer.Send(responseMessage, responseMessage.Length, clientEndPoint);
        }
    }

    public static void Main()
    {
        int port = 13439;

        Console.WriteLine("Starting relay server...");
        UdpRelayServer relayServer = new UdpRelayServer();
        relayServer.StartRelayServer(port);
    }
}
