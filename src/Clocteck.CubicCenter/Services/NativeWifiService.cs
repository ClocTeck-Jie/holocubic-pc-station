using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Xml;
using Clocteck.CubicCenter.Core;
using Clocteck.CubicCenter.Models;

namespace Clocteck.CubicCenter.Services;

public sealed class NativeWifiService : IDisposable
{
    private readonly AppLog _log;
    private readonly IntPtr _clientHandle;
    private bool _disposed;

    public NativeWifiService(AppLog log)
    {
        _log = log;
        var result = WlanOpenHandle(2, IntPtr.Zero, out _, out _clientHandle);
        if (result != 0)
        {
            throw new InvalidOperationException($"无法打开 Windows WLAN 服务，错误代码 {result}。请确认 WLAN AutoConfig 服务正在运行。");
        }
    }

    public Task<IReadOnlyList<WifiNetwork>> ScanAsync(CancellationToken cancellationToken = default) => Task.Run(async () =>
    {
        ThrowIfDisposed();
        var interfaces = EnumerateInterfaces();
        foreach (var item in interfaces)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var interfaceId = item.Id;
            _ = WlanScan(_clientHandle, ref interfaceId, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        }

        await Task.Delay(1800, cancellationToken);
        var networks = new List<WifiNetwork>();
        foreach (var item in interfaces)
        {
            cancellationToken.ThrowIfCancellationRequested();
            networks.AddRange(ReadAvailableNetworks(item));
        }

        return (IReadOnlyList<WifiNetwork>)networks
            .Where(network => !string.IsNullOrWhiteSpace(network.Ssid))
            .GroupBy(network => (network.InterfaceId, network.Ssid), new InterfaceSsidComparer())
            .Select(group => group.OrderByDescending(network => network.SignalQuality).First())
            .OrderByDescending(network => network.SignalQuality)
            .ThenBy(network => network.Ssid, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }, cancellationToken);

    public Task<WifiConnection?> GetCurrentConnectionAsync(CancellationToken cancellationToken = default) => Task.Run(() =>
    {
        ThrowIfDisposed();
        foreach (var item in EnumerateInterfaces())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var connection = ReadCurrentConnection(item);
            if (connection is not null) return connection;
        }
        return null;
    }, cancellationToken);

    public Task ConnectAsync(WifiNetwork network, CancellationToken cancellationToken = default) => Task.Run(() =>
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();

        var profileName = network.Ssid;
        if (!network.HasProfile)
        {
            if (network.SecurityEnabled)
            {
                throw new InvalidOperationException($"Windows 中没有“{network.Ssid}”的已保存配置。请先通过 Windows 连接一次该网络。");
            }
            CreateOpenProfile(network.InterfaceId, network.Ssid);
        }

        var profilePointer = Marshal.StringToHGlobalUni(profileName);
        try
        {
            var parameters = new WlanConnectionParameters
            {
                ConnectionMode = WlanConnectionMode.Profile,
                Profile = profilePointer,
                Dot11BssType = Dot11BssType.Any,
            };
            var interfaceId = network.InterfaceId;
            var result = WlanConnect(_clientHandle, ref interfaceId, ref parameters, IntPtr.Zero);
            if (result != 0) throw new InvalidOperationException($"请求连接“{network.Ssid}”失败，WLAN错误代码 {result}。");
            _log.Info("Wi-Fi", $"已向 Windows 请求连接 {network.Ssid}");
        }
        finally
        {
            Marshal.FreeHGlobal(profilePointer);
        }
    }, cancellationToken);

    public Task ConnectProfileAsync(Guid interfaceId, string profileName, CancellationToken cancellationToken = default) => Task.Run(() =>
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        var profilePointer = Marshal.StringToHGlobalUni(profileName);
        try
        {
            var parameters = new WlanConnectionParameters
            {
                ConnectionMode = WlanConnectionMode.Profile,
                Profile = profilePointer,
                Dot11BssType = Dot11BssType.Any,
            };
            var result = WlanConnect(_clientHandle, ref interfaceId, ref parameters, IntPtr.Zero);
            if (result != 0) throw new InvalidOperationException($"恢复 Wi-Fi“{profileName}”失败，WLAN错误代码 {result}。");
            _log.Info("Wi-Fi", $"正在恢复原网络 {profileName}");
        }
        finally
        {
            Marshal.FreeHGlobal(profilePointer);
        }
    }, cancellationToken);

    public Task DisconnectAsync(Guid interfaceId) => Task.Run(() =>
    {
        ThrowIfDisposed();
        var result = WlanDisconnect(_clientHandle, ref interfaceId, IntPtr.Zero);
        if (result != 0) throw new InvalidOperationException($"断开设备热点失败，WLAN错误代码 {result}。");
    });

    private IReadOnlyList<WlanInterface> EnumerateInterfaces()
    {
        var result = WlanEnumInterfaces(_clientHandle, IntPtr.Zero, out var listPointer);
        if (result != 0) throw new InvalidOperationException($"枚举无线网卡失败，WLAN错误代码 {result}。");
        try
        {
            var count = Marshal.ReadInt32(listPointer);
            var current = IntPtr.Add(listPointer, 8);
            var size = Marshal.SizeOf<WlanInterfaceInfo>();
            var items = new List<WlanInterface>(count);
            for (var index = 0; index < count; index++)
            {
                var native = Marshal.PtrToStructure<WlanInterfaceInfo>(current);
                items.Add(new WlanInterface(native.InterfaceGuid, native.Description));
                current = IntPtr.Add(current, size);
            }
            return items;
        }
        finally
        {
            WlanFreeMemory(listPointer);
        }
    }

    private IReadOnlyList<WifiNetwork> ReadAvailableNetworks(WlanInterface item)
    {
        var interfaceId = item.Id;
        var result = WlanGetAvailableNetworkList(_clientHandle, ref interfaceId, 0, IntPtr.Zero, out var listPointer);
        if (result != 0) return [];
        try
        {
            var count = Marshal.ReadInt32(listPointer);
            var current = IntPtr.Add(listPointer, 8);
            var size = Marshal.SizeOf<WlanAvailableNetwork>();
            var items = new List<WifiNetwork>(count);
            for (var index = 0; index < count; index++)
            {
                var native = Marshal.PtrToStructure<WlanAvailableNetwork>(current);
                var ssid = native.Ssid.ToText();
                items.Add(new WifiNetwork(
                    ssid,
                    (int)Math.Min(native.SignalQuality, 100),
                    native.SecurityEnabled,
                    !string.IsNullOrWhiteSpace(native.ProfileName),
                    item.Id,
                    item.Name));
                current = IntPtr.Add(current, size);
            }
            return items;
        }
        finally
        {
            WlanFreeMemory(listPointer);
        }
    }

    private WifiConnection? ReadCurrentConnection(WlanInterface item)
    {
        var interfaceId = item.Id;
        var result = WlanQueryInterface(
            _clientHandle,
            ref interfaceId,
            WlanIntfOpcode.CurrentConnection,
            IntPtr.Zero,
            out _,
            out var dataPointer,
            out _);
        if (result != 0) return null;

        try
        {
            var native = Marshal.PtrToStructure<WlanConnectionAttributes>(dataPointer);
            if (native.InterfaceState != WlanInterfaceState.Connected) return null;
            var network = FindNetworkInterface(item.Id);
            var ip = network?.GetIPProperties().UnicastAddresses
                .FirstOrDefault(address => address.Address.AddressFamily == AddressFamily.InterNetwork)?.Address.ToString();
            var gateway = network?.GetIPProperties().GatewayAddresses
                .FirstOrDefault(address => address.Address.AddressFamily == AddressFamily.InterNetwork)?.Address.ToString();
            var bssid = native.Association.Bssid is { Length: 6 }
                ? string.Join(":", native.Association.Bssid.Select(value => value.ToString("X2")))
                : string.Empty;
            return new WifiConnection(
                native.Association.Ssid.ToText(),
                native.ProfileName,
                (int)native.Association.SignalQuality,
                bssid,
                item.Id,
                item.Name,
                ip,
                gateway);
        }
        finally
        {
            WlanFreeMemory(dataPointer);
        }
    }

    private void CreateOpenProfile(Guid interfaceId, string ssid)
    {
        var escaped = SecurityElementEscape(ssid);
        var hex = Convert.ToHexString(Encoding.UTF8.GetBytes(ssid));
        var xml = $"""
                  <?xml version="1.0"?>
                  <WLANProfile xmlns="http://www.microsoft.com/networking/WLAN/profile/v1">
                    <name>{escaped}</name>
                    <SSIDConfig><SSID><hex>{hex}</hex><name>{escaped}</name></SSID></SSIDConfig>
                    <connectionType>ESS</connectionType>
                    <connectionMode>manual</connectionMode>
                    <MSM><security><authEncryption><authentication>open</authentication><encryption>none</encryption><useOneX>false</useOneX></authEncryption></security></MSM>
                  </WLANProfile>
                  """;
        var result = WlanSetProfile(_clientHandle, ref interfaceId, 0, xml, null, true, IntPtr.Zero, out var reason);
        if (result != 0) throw new InvalidOperationException($"创建临时热点配置失败，WLAN错误代码 {result}，原因 {reason}。");
    }

    private static NetworkInterface? FindNetworkInterface(Guid id) =>
        NetworkInterface.GetAllNetworkInterfaces().FirstOrDefault(item =>
            Guid.TryParse(item.Id.Trim('{', '}'), out var networkId) && networkId == id);

    private static string SecurityElementEscape(string value)
    {
        var document = new XmlDocument();
        var element = document.CreateElement("value");
        element.InnerText = value;
        return element.InnerXml;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _ = WlanCloseHandle(_clientHandle, IntPtr.Zero);
    }

    private sealed class InterfaceSsidComparer : IEqualityComparer<(Guid InterfaceId, string Ssid)>
    {
        public bool Equals((Guid InterfaceId, string Ssid) x, (Guid InterfaceId, string Ssid) y) =>
            x.InterfaceId == y.InterfaceId && string.Equals(x.Ssid, y.Ssid, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((Guid InterfaceId, string Ssid) obj) => HashCode.Combine(obj.InterfaceId, StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Ssid));
    }

    private sealed record WlanInterface(Guid Id, string Name);

    private enum WlanInterfaceState { NotReady, Connected, AdHocNetworkFormed, Disconnecting, Disconnected, Associating, Discovering, Authenticating }
    private enum WlanConnectionMode { Profile, TemporaryProfile, DiscoverySecure, DiscoveryUnsecure, Auto, Invalid }
    private enum Dot11BssType : uint { Infrastructure = 1, Independent = 2, Any = 3 }
    private enum WlanIntfOpcode { AutoconfStart = 0, AutoconfEnabled = 1, BackgroundScanEnabled = 2, MediaStreamingMode = 3, RadioState = 4, BssType = 5, InterfaceState = 6, CurrentConnection = 7 }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WlanInterfaceInfo
    {
        public Guid InterfaceGuid;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Description;
        public WlanInterfaceState State;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Dot11Ssid
    {
        public uint Length;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)] public byte[] Bytes;

        public readonly string ToText()
        {
            if (Bytes is null || Length == 0) return string.Empty;
            return Encoding.UTF8.GetString(Bytes, 0, (int)Math.Min(Length, 32)).TrimEnd('\0');
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WlanAvailableNetwork
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string ProfileName;
        public Dot11Ssid Ssid;
        public Dot11BssType BssType;
        public uint NumberOfBssids;
        [MarshalAs(UnmanagedType.Bool)] public bool NetworkConnectable;
        public uint NotConnectableReason;
        public uint NumberOfPhyTypes;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)] public uint[] PhyTypes;
        [MarshalAs(UnmanagedType.Bool)] public bool MorePhyTypes;
        public uint SignalQuality;
        [MarshalAs(UnmanagedType.Bool)] public bool SecurityEnabled;
        public uint DefaultAuthAlgorithm;
        public uint DefaultCipherAlgorithm;
        public uint Flags;
        public uint Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WlanAssociationAttributes
    {
        public Dot11Ssid Ssid;
        public Dot11BssType BssType;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 6)] public byte[] Bssid;
        public uint PhyType;
        public uint PhyIndex;
        public uint SignalQuality;
        public uint RxRate;
        public uint TxRate;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WlanSecurityAttributes
    {
        [MarshalAs(UnmanagedType.Bool)] public bool SecurityEnabled;
        [MarshalAs(UnmanagedType.Bool)] public bool OneXEnabled;
        public uint AuthAlgorithm;
        public uint CipherAlgorithm;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WlanConnectionAttributes
    {
        public WlanInterfaceState InterfaceState;
        public WlanConnectionMode ConnectionMode;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string ProfileName;
        public WlanAssociationAttributes Association;
        public WlanSecurityAttributes Security;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WlanConnectionParameters
    {
        public WlanConnectionMode ConnectionMode;
        public IntPtr Profile;
        public IntPtr Dot11Ssid;
        public IntPtr DesiredBssidList;
        public Dot11BssType Dot11BssType;
        public uint Flags;
    }

    [DllImport("wlanapi.dll")]
    private static extern uint WlanOpenHandle(uint clientVersion, IntPtr reserved, out uint negotiatedVersion, out IntPtr clientHandle);

    [DllImport("wlanapi.dll")]
    private static extern uint WlanCloseHandle(IntPtr clientHandle, IntPtr reserved);

    [DllImport("wlanapi.dll")]
    private static extern uint WlanEnumInterfaces(IntPtr clientHandle, IntPtr reserved, out IntPtr interfaceList);

    [DllImport("wlanapi.dll")]
    private static extern uint WlanScan(IntPtr clientHandle, ref Guid interfaceGuid, IntPtr dot11Ssid, IntPtr ieData, IntPtr reserved);

    [DllImport("wlanapi.dll")]
    private static extern uint WlanGetAvailableNetworkList(IntPtr clientHandle, ref Guid interfaceGuid, uint flags, IntPtr reserved, out IntPtr availableNetworkList);

    [DllImport("wlanapi.dll")]
    private static extern uint WlanQueryInterface(IntPtr clientHandle, ref Guid interfaceGuid, WlanIntfOpcode opcode, IntPtr reserved, out uint dataSize, out IntPtr data, out uint opcodeValueType);

    [DllImport("wlanapi.dll")]
    private static extern uint WlanConnect(IntPtr clientHandle, ref Guid interfaceGuid, ref WlanConnectionParameters connectionParameters, IntPtr reserved);

    [DllImport("wlanapi.dll")]
    private static extern uint WlanDisconnect(IntPtr clientHandle, ref Guid interfaceGuid, IntPtr reserved);

    [DllImport("wlanapi.dll", CharSet = CharSet.Unicode)]
    private static extern uint WlanSetProfile(IntPtr clientHandle, ref Guid interfaceGuid, uint flags, string profileXml, string? allUserProfileSecurity, bool overwrite, IntPtr reserved, out uint reasonCode);

    [DllImport("wlanapi.dll")]
    private static extern void WlanFreeMemory(IntPtr memory);
}
