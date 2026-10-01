using System.Net;
using System.Net.Sockets;

namespace TelegramGroupsAdmin.Core.Http;

/// <summary>
/// Decides whether an IP address is a public Internet address the server may connect to on a
/// user's behalf. Everything loopback, private, link-local (including cloud metadata),
/// unspecified, carrier-grade NAT, multicast, discard, reserved or documentation-only is refused, in its
/// IPv4 form, its native IPv6 form, and every IPv6 form that embeds an IPv4 address
/// (IPv4-mapped, IPv4-compatible, NAT64 and 6to4). This is the rule the public-url HttpClient
/// applies to the address it actually connects to, so it holds for DNS answers and redirect hops.
/// </summary>
public static class PublicAddressPolicy
{
    private sealed record BlockedRange(IPAddress Network, int PrefixLength, string Reason)
    {
        private readonly byte[] _network = Network.GetAddressBytes();

        public bool Contains(ReadOnlySpan<byte> address)
        {
            if (address.Length != _network.Length) return false;

            var fullBytes = PrefixLength / 8;
            for (var i = 0; i < fullBytes; i++)
            {
                if (address[i] != _network[i]) return false;
            }

            var remainingBits = PrefixLength % 8;
            if (remainingBits == 0) return true;

            var mask = (byte)(0xFF << (8 - remainingBits));
            return (address[fullBytes] & mask) == (_network[fullBytes] & mask);
        }
    }

    private static readonly BlockedRange[] BlockedV4 =
    [
        new(IPAddress.Parse("0.0.0.0"), 8, "unspecified / this-network (0.0.0.0/8)"),
        new(IPAddress.Parse("10.0.0.0"), 8, "private (10.0.0.0/8)"),
        new(IPAddress.Parse("100.64.0.0"), 10, "carrier-grade NAT (100.64.0.0/10)"),
        new(IPAddress.Parse("127.0.0.0"), 8, "loopback (127.0.0.0/8)"),
        new(IPAddress.Parse("169.254.0.0"), 16, "link-local (169.254.0.0/16)"),
        new(IPAddress.Parse("172.16.0.0"), 12, "private (172.16.0.0/12)"),
        new(IPAddress.Parse("192.0.0.0"), 24, "reserved IETF protocol assignments (192.0.0.0/24)"),
        new(IPAddress.Parse("192.0.2.0"), 24, "documentation (192.0.2.0/24)"),
        new(IPAddress.Parse("192.168.0.0"), 16, "private (192.168.0.0/16)"),
        new(IPAddress.Parse("198.18.0.0"), 15, "benchmarking (198.18.0.0/15)"),
        new(IPAddress.Parse("198.51.100.0"), 24, "documentation (198.51.100.0/24)"),
        new(IPAddress.Parse("203.0.113.0"), 24, "documentation (203.0.113.0/24)"),
        new(IPAddress.Parse("224.0.0.0"), 4, "multicast (224.0.0.0/4)"),
        new(IPAddress.Parse("240.0.0.0"), 4, "reserved / broadcast (240.0.0.0/4)"),
    ];

    private static readonly BlockedRange[] BlockedV6 =
    [
        new(IPAddress.Parse("::"), 128, "unspecified (::)"),
        new(IPAddress.Parse("::1"), 128, "loopback (::1)"),
        new(IPAddress.Parse("100::"), 64, "discard-only (100::/64)"),
        // Local-use NAT64 (RFC 8215): the translator is on the local network by definition, so the
        // whole /48 is refused rather than judging the embedded IPv4.
        new(IPAddress.Parse("64:ff9b:1::"), 48, "local-use NAT64 (64:ff9b:1::/48)"),
        new(IPAddress.Parse("fc00::"), 7, "unique-local (fc00::/7)"),
        new(IPAddress.Parse("fe80::"), 10, "link-local (fe80::/10)"),
        new(IPAddress.Parse("fec0::"), 10, "site-local (fec0::/10)"),
        new(IPAddress.Parse("ff00::"), 8, "multicast (ff00::/8)"),
        new(IPAddress.Parse("2001:db8::"), 32, "documentation (2001:db8::/32)"),
    ];

    /// <summary>IPv6 prefixes that carry an IPv4 address; the embedded address is what gets judged.</summary>
    private static readonly (BlockedRange Prefix, int V4Offset)[] EmbeddedV4 =
    [
        (new BlockedRange(IPAddress.Parse("::ffff:0:0"), 96, "IPv4-mapped"), 12),
        (new BlockedRange(IPAddress.Parse("64:ff9b::"), 96, "NAT64"), 12),
        (new BlockedRange(IPAddress.Parse("2002::"), 16, "6to4"), 2),
        // IPv4-compatible (::a.b.c.d, deprecated): the ::/96 prefix also covers :: and ::1, which
        // are judged as themselves first.
        (new BlockedRange(IPAddress.Parse("::"), 96, "IPv4-compatible"), 12),
    ];

    /// <summary>
    /// Returns true when <paramref name="address"/> is a public Internet address. When it is not,
    /// <paramref name="reason"/> names the range that refused it (for logs, never for users).
    /// </summary>
    public static bool IsPublic(IPAddress address, out string? reason)
    {
        ArgumentNullException.ThrowIfNull(address);

        switch (address.AddressFamily)
        {
            case AddressFamily.InterNetwork:
                return IsPublicV4(address.GetAddressBytes(), out reason);

            case AddressFamily.InterNetworkV6:
            {
                var bytes = address.GetAddressBytes();
                foreach (var range in BlockedV6)
                {
                    if (range.Contains(bytes))
                    {
                        reason = range.Reason;
                        return false;
                    }
                }

                foreach (var (prefix, v4Offset) in EmbeddedV4)
                {
                    if (prefix.Contains(bytes))
                    {
                        if (!IsPublicV4(bytes.AsSpan(v4Offset, 4), out var embeddedReason))
                        {
                            reason = $"{embeddedReason} embedded in {prefix.Reason} IPv6";
                            return false;
                        }

                        reason = null;
                        return true;
                    }
                }

                reason = null;
                return true;
            }

            default:
                reason = $"unsupported address family {address.AddressFamily}";
                return false;
        }
    }

    private static bool IsPublicV4(ReadOnlySpan<byte> bytes, out string? reason)
    {
        foreach (var range in BlockedV4)
        {
            if (range.Contains(bytes))
            {
                reason = range.Reason;
                return false;
            }
        }

        reason = null;
        return true;
    }
}
