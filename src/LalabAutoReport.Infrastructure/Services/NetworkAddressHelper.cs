using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace LalabAutoReport.Infrastructure.Services;

public record NetworkAdapterCandidate(
    string Name,
    string Description,
    NetworkInterfaceType InterfaceType,
    OperationalStatus Status,
    bool HasGateway,
    IReadOnlyList<IPAddress> IPv4Addresses
);

public static class NetworkAddressHelper
{
    private static readonly string[] VirtualKeywords = new[]
    {
        "virtual", "vethernet", "wsl", "hyper-v", "vmware", "virtualbox",
        "vbox", "tap", "npcap", "bluetooth", "pseudo", "loopback", "teredo", "container"
    };

    public static bool IsVirtualOrExcluded(string name, string description)
    {
        var n = (name ?? string.Empty).ToLowerInvariant();
        var d = (description ?? string.Empty).ToLowerInvariant();

        return VirtualKeywords.Any(kw => n.Contains(kw) || d.Contains(kw));
    }

    public static string GetLocalIpAddress()
    {
        try
        {
            var adapters = NetworkInterface.GetAllNetworkInterfaces()
                .Select(nic =>
                {
                    try
                    {
                        var ipProps = nic.GetIPProperties();
                        var hasGateway = ipProps.GatewayAddresses.Any(g =>
                            g.Address != null &&
                            g.Address.AddressFamily == AddressFamily.InterNetwork &&
                            !IPAddress.IsLoopback(g.Address) &&
                            !g.Address.Equals(IPAddress.Any));

                        var ipv4s = ipProps.UnicastAddresses
                            .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork &&
                                        !IPAddress.IsLoopback(a.Address) &&
                                        !a.Address.Equals(IPAddress.Any))
                            .Select(a => a.Address)
                            .ToList();

                        return new NetworkAdapterCandidate(
                            nic.Name,
                            nic.Description,
                            nic.NetworkInterfaceType,
                            nic.OperationalStatus,
                            hasGateway,
                            ipv4s
                        );
                    }
                    catch
                    {
                        return null;
                    }
                })
                .Where(a => a != null)
                .Cast<NetworkAdapterCandidate>()
                .ToList();

            return SelectBestIpAddress(adapters);
        }
        catch
        {
            return "127.0.0.1";
        }
    }

    public static string SelectBestIpAddress(IEnumerable<NetworkAdapterCandidate> candidates)
    {
        var active = candidates
            .Where(c => c.Status == OperationalStatus.Up && c.InterfaceType != NetworkInterfaceType.Loopback)
            .ToList();

        // 1. Physical (not virtual) with IPv4 Gateway and Ethernet/Wi-Fi
        var bestWithGateway = active
            .Where(c => !IsVirtualOrExcluded(c.Name, c.Description) &&
                        c.HasGateway &&
                        (c.InterfaceType == NetworkInterfaceType.Ethernet || c.InterfaceType == NetworkInterfaceType.Wireless80211) &&
                        c.IPv4Addresses.Count > 0)
            .FirstOrDefault();

        if (bestWithGateway != null)
        {
            return bestWithGateway.IPv4Addresses[0].ToString();
        }

        // 2. Physical (not virtual) with IPv4 Gateway (any network interface type)
        var anyPhysicalWithGateway = active
            .Where(c => !IsVirtualOrExcluded(c.Name, c.Description) &&
                        c.HasGateway &&
                        c.IPv4Addresses.Count > 0)
            .FirstOrDefault();

        if (anyPhysicalWithGateway != null)
        {
            return anyPhysicalWithGateway.IPv4Addresses[0].ToString();
        }

        // 3. Physical (not virtual) Ethernet / Wi-Fi even without detected gateway (e.g. static IP or isolated switch)
        var physicalWithoutGateway = active
            .Where(c => !IsVirtualOrExcluded(c.Name, c.Description) &&
                        (c.InterfaceType == NetworkInterfaceType.Ethernet || c.InterfaceType == NetworkInterfaceType.Wireless80211) &&
                        c.IPv4Addresses.Count > 0)
            .FirstOrDefault();

        if (physicalWithoutGateway != null)
        {
            return physicalWithoutGateway.IPv4Addresses[0].ToString();
        }

        // 4. Any physical interface with IPv4
        var anyPhysical = active
            .Where(c => !IsVirtualOrExcluded(c.Name, c.Description) &&
                        c.IPv4Addresses.Count > 0)
            .FirstOrDefault();

        if (anyPhysical != null)
        {
            return anyPhysical.IPv4Addresses[0].ToString();
        }

        // 5. Fallback: Any interface with gateway
        var fallbackWithGateway = active
            .Where(c => c.HasGateway && c.IPv4Addresses.Count > 0)
            .FirstOrDefault();

        if (fallbackWithGateway != null)
        {
            return fallbackWithGateway.IPv4Addresses[0].ToString();
        }

        // 6. Fallback: Any active interface with IPv4
        var anyActive = active
            .Where(c => c.IPv4Addresses.Count > 0)
            .FirstOrDefault();

        if (anyActive != null)
        {
            return anyActive.IPv4Addresses[0].ToString();
        }

        return "127.0.0.1";
    }
}
