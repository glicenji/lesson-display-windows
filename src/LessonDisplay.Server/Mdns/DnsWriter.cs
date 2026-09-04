using System.Net;
using System.Text;

namespace LessonDisplay.Server.Mdns;

/// <summary>
/// Builds a minimal, hand-rolled mDNS "A record" announcement packet. We only
/// ever send unsolicited announcements (never parse incoming queries), which
/// keeps this to a small amount of code with no real room for a parser bug —
/// RFC 6762 section 8.3 explicitly allows a responder to announce its
/// records with QDCOUNT=0 the same way a response would look.
/// </summary>
public static class DnsWriter
{
    public static byte[] BuildAAnnouncement(string hostname, IPAddress address, int ttlSeconds = 120)
    {
        using var ms = new MemoryStream();

        void WriteUInt16(ushort v)
        {
            ms.WriteByte((byte)(v >> 8));
            ms.WriteByte((byte)v);
        }

        void WriteUInt32(uint v)
        {
            ms.WriteByte((byte)(v >> 24));
            ms.WriteByte((byte)(v >> 16));
            ms.WriteByte((byte)(v >> 8));
            ms.WriteByte((byte)v);
        }

        void WriteName(string name)
        {
            foreach (var label in name.TrimEnd('.').Split('.', StringSplitOptions.RemoveEmptyEntries))
            {
                var bytes = Encoding.ASCII.GetBytes(label);
                ms.WriteByte((byte)bytes.Length);
                ms.Write(bytes, 0, bytes.Length);
            }
            ms.WriteByte(0); // root label
        }

        // ---- Header ----
        WriteUInt16(0);      // ID (0 for multicast)
        WriteUInt16(0x8400); // Flags: QR=1 (response), AA=1 (authoritative)
        WriteUInt16(0);      // QDCOUNT
        WriteUInt16(1);      // ANCOUNT
        WriteUInt16(0);      // NSCOUNT
        WriteUInt16(0);      // ARCOUNT

        // ---- Answer: <hostname> A <address> ----
        WriteName(hostname);
        WriteUInt16(1); // TYPE = A
        WriteUInt16(1); // CLASS = IN
        WriteUInt32((uint)ttlSeconds);
        var ipBytes = address.GetAddressBytes(); // 4 bytes for IPv4
        WriteUInt16((ushort)ipBytes.Length);
        ms.Write(ipBytes, 0, ipBytes.Length);

        return ms.ToArray();
    }
}
