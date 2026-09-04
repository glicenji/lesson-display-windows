using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace LessonDisplay.Server.Mdns;

public static class LocalNetwork
{
    /// <summary>
    /// Best-effort "what LAN IP would other computers use to reach me" —
    /// the classic trick of connecting a UDP socket (no packet actually
    /// sent) and reading back which local address the OS routed it through.
    /// Falls back to scanning interfaces if that's ever unavailable.
    /// </summary>
    public static IPAddress? GetPrimaryIPv4()
    {
        try
        {
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            socket.Connect("8.8.8.8", 65530);
            if (socket.LocalEndPoint is IPEndPoint ep && !IPAddress.IsLoopback(ep.Address))
                return ep.Address;
        }
        catch
        {
            // fall through to the interface scan below
        }

        try
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .Where(ni => ni.OperationalStatus == OperationalStatus.Up
                             && ni.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .SelectMany(ni => ni.GetIPProperties().UnicastAddresses)
                .Select(a => a.Address)
                .FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork);
        }
        catch
        {
            return null;
        }
    }
}
