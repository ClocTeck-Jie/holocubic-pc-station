using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Clocteck.CubicCenter.Models;

namespace Clocteck.CubicCenter.Services;

public static class ComputerNetworkService
{
    public static ComputerNetworkConnection? Resolve(WifiConnection? wifi, string? deviceIp)
    {
        var routeAddress = ResolveRouteAddress(deviceIp) ?? ResolveRouteAddress("1.1.1.1");
        var adapters = NetworkInterface.GetAllNetworkInterfaces()
            .Where(adapter => adapter.OperationalStatus == OperationalStatus.Up &&
                              adapter.NetworkInterfaceType is not NetworkInterfaceType.Loopback and not NetworkInterfaceType.Tunnel)
            .Select(adapter => new
            {
                Adapter = adapter,
                Properties = adapter.GetIPProperties(),
                Address = adapter.GetIPProperties().UnicastAddresses
                    .Select(item => item.Address)
                    .FirstOrDefault(address => IsUsableIpv4(address) && (routeAddress is null || address.Equals(routeAddress))),
                FallbackAddress = adapter.GetIPProperties().UnicastAddresses
                    .Select(item => item.Address)
                    .FirstOrDefault(IsUsableIpv4),
            })
            .Where(item => item.FallbackAddress is not null)
            .OrderByDescending(item => item.Address is not null)
            .ThenByDescending(item => item.Properties.GatewayAddresses.Any(gateway => IsUsableIpv4(gateway.Address)))
            .ThenByDescending(item => IsEthernet(item.Adapter.NetworkInterfaceType))
            .ThenByDescending(item => item.Adapter.Speed)
            .ToArray();

        var selected = adapters.FirstOrDefault();
        if (selected is null)
        {
            return wifi is null ? null : FromWifi(wifi);
        }

        var selectedAddress = selected.Address ?? selected.FallbackAddress;
        var isWireless = selected.Adapter.NetworkInterfaceType == NetworkInterfaceType.Wireless80211;
        if (isWireless && wifi is not null &&
            (string.IsNullOrWhiteSpace(wifi.Ipv4Address) || wifi.Ipv4Address == selectedAddress?.ToString()))
        {
            return FromWifi(wifi);
        }

        var gateway = selected.Properties.GatewayAddresses
            .Select(item => item.Address)
            .FirstOrDefault(IsUsableIpv4)?.ToString();
        var type = isWireless ? "Wi-Fi" : IsEthernet(selected.Adapter.NetworkInterfaceType) ? "Ethernet" : "Network";
        return new ComputerNetworkConnection(
            selected.Adapter.Name,
            type,
            selected.Adapter.Description,
            null,
            null,
            selectedAddress?.ToString(),
            gateway);
    }

    private static ComputerNetworkConnection FromWifi(WifiConnection wifi) => new(
        wifi.Ssid,
        "Wi-Fi",
        wifi.InterfaceName,
        wifi.Ssid,
        wifi.SignalQuality,
        wifi.Ipv4Address,
        wifi.Gateway);

    private static IPAddress? ResolveRouteAddress(string? destination)
    {
        if (!IPAddress.TryParse(destination, out var target) || target.AddressFamily != AddressFamily.InterNetwork) return null;
        try
        {
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            socket.Connect(new IPEndPoint(target, 9));
            return (socket.LocalEndPoint as IPEndPoint)?.Address;
        }
        catch (SocketException)
        {
            return null;
        }
    }

    private static bool IsUsableIpv4(IPAddress? address)
    {
        if (address is null || address.AddressFamily != AddressFamily.InterNetwork || IPAddress.IsLoopback(address)) return false;
        var bytes = address.GetAddressBytes();
        return !(bytes[0] == 169 && bytes[1] == 254) && !address.Equals(IPAddress.Any);
    }

    private static bool IsEthernet(NetworkInterfaceType type) => type is
        NetworkInterfaceType.Ethernet or
        NetworkInterfaceType.Ethernet3Megabit or
        NetworkInterfaceType.FastEthernetFx or
        NetworkInterfaceType.FastEthernetT or
        NetworkInterfaceType.GigabitEthernet;
}
