using System.Collections.Concurrent;
using System.Net;

namespace TelegramGroupsAdmin.Core.Http;

/// <summary>
/// Exceptions to <see cref="PublicAddressPolicy"/> for the public-url HttpClient: an endpoint the
/// policy refuses is still connected when the allowance says so. The production registration is
/// <see cref="NoFetchAllowance"/>, which allows nothing; the only way to relax it is to register
/// another implementation in DI, which test hosts do for their loopback WireMock ports. There is
/// deliberately no configuration key, environment variable or settings page behind this.
/// </summary>
public interface IPublicUrlFetchAllowance
{
    /// <summary>True when a connection to <paramref name="address"/>:<paramref name="port"/> is allowed despite the policy.</summary>
    bool IsAllowed(IPAddress address, int port);
}

/// <summary>Allows nothing; the production allowance.</summary>
public sealed class NoFetchAllowance : IPublicUrlFetchAllowance
{
    public bool IsAllowed(IPAddress address, int port) => false;
}

/// <summary>
/// Allows loopback addresses on an explicit set of ports — for test hosts that serve fixtures from
/// a local WireMock server. Internal so that only the test assemblies named in Core's
/// InternalsVisibleTo can register it; production composition cannot see it.
/// </summary>
internal sealed class LoopbackPortAllowance : IPublicUrlFetchAllowance
{
    private readonly ConcurrentDictionary<int, byte> _ports = new();

    public LoopbackPortAllowance(params int[] ports)
    {
        foreach (var port in ports) AllowPort(port);
    }

    /// <summary>Opens one more loopback port (a test that starts its WireMock host after the app).</summary>
    public void AllowPort(int port) => _ports[port] = 0;

    public bool IsAllowed(IPAddress address, int port)
    {
        if (!_ports.ContainsKey(port)) return false;

        var effective = address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
        return IPAddress.IsLoopback(effective);
    }
}
