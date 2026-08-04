using Clocteck.CubicCenter.Core;
using Clocteck.CubicCenter.Models;

namespace Clocteck.CubicCenter.Services;

public sealed class ProvisioningCoordinator
{
    private readonly NativeWifiService _wifi;
    private readonly DeviceDiscoveryService _discovery;
    private readonly AppLog _log;
    private readonly AppSettings _settings;
    private readonly Func<Task> _saveSettings;
    private CancellationTokenSource? _sessionCancellation;
    private WifiConnection? _previousConnection;
    private WifiNetwork? _deviceNetwork;
    private Task? _monitorTask;

    public ProvisioningSnapshot Current { get; private set; } = new("idle", "尚未开始配网", 0);

    public event EventHandler<ProvisioningSnapshot>? StatusChanged;
    public event EventHandler<string>? OpenBrowserRequested;
    public event EventHandler<DeviceInfo>? DeviceFound;

    public ProvisioningCoordinator(
        NativeWifiService wifi,
        DeviceDiscoveryService discovery,
        AppLog log,
        AppSettings settings,
        Func<Task> saveSettings)
    {
        _wifi = wifi;
        _discovery = discovery;
        _log = log;
        _settings = settings;
        _saveSettings = saveSettings;
    }

    public async Task<IReadOnlyList<WifiNetwork>> ScanDeviceNetworksAsync(CancellationToken cancellationToken = default)
    {
        Publish("scanning", "正在扫描设备热点…", 8, canCancel: true);
        var all = await _wifi.ScanAsync(cancellationToken);
        var matches = all.Where(network => IsDeviceAp(network.Ssid)).ToArray();
        Publish(matches.Length > 0 ? "ready" : "not-found",
            matches.Length > 0 ? $"找到 {matches.Length} 个设备热点" : "没有发现设备热点，请确认设备处于配网模式",
            matches.Length > 0 ? 15 : 0,
            canCancel: false);
        return matches;
    }

    public async Task BeginAsync(string? requestedSsid = null)
    {
        CancelSessionOnly();
        _sessionCancellation = new CancellationTokenSource();
        var cancellationToken = _sessionCancellation.Token;

        try
        {
            _previousConnection = await _wifi.GetCurrentConnectionAsync(cancellationToken);
            var previousSsid = _previousConnection?.Ssid;
            _log.Info("配网", string.IsNullOrWhiteSpace(previousSsid) ? "配网前电脑没有连接 Wi-Fi" : $"已记录原 Wi-Fi：{previousSsid}");

            Publish("scanning", "正在扫描 clocteck-cubic 设备热点…", 10, previousSsid, canCancel: true);
            var networks = await _wifi.ScanAsync(cancellationToken);
            _deviceNetwork = networks
                .Where(network => IsDeviceAp(network.Ssid))
                .Where(network => string.IsNullOrWhiteSpace(requestedSsid) || network.Ssid.Equals(requestedSsid, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(network => network.SignalQuality)
                .FirstOrDefault();
            if (_deviceNetwork is null)
            {
                throw new InvalidOperationException("没有发现 clocteck-cubic 设备热点。请让设备进入配网模式后重试。");
            }

            Publish("connecting-ap", $"正在连接 {_deviceNetwork.Ssid}…", 24, previousSsid, _deviceNetwork.Ssid, canCancel: true);
            await _wifi.ConnectAsync(_deviceNetwork, cancellationToken);
            var connected = await WaitForConnectionAsync(
                connection => connection?.Ssid.Equals(_deviceNetwork.Ssid, StringComparison.OrdinalIgnoreCase) == true,
                TimeSpan.FromSeconds(25),
                cancellationToken);
            if (connected is null) throw new TimeoutException("Windows 没有在25秒内连接设备热点。");

            var setupUrl = $"http://{connected.Gateway ?? "192.168.18.1"}/";
            Publish("provisioning", "已连接设备热点，请在配网页面选择目标 Wi-Fi", 42, previousSsid, _deviceNetwork.Ssid, setupUrl, canCancel: true, canForceComplete: true);
            _log.Info("配网", $"设备配网页面：{setupUrl}");
            OpenBrowserRequested?.Invoke(this, setupUrl);

            _monitorTask = MonitorDeviceApSafeAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            Publish("cancelled", "配网操作已取消", 0);
        }
        catch (Exception error)
        {
            _log.Error("配网", error.Message);
            Publish("error", error.Message, 0, _previousConnection?.Ssid, _deviceNetwork?.Ssid);
        }
    }

    public async Task ForceCompleteAsync()
    {
        if (_deviceNetwork is null) return;
        try
        {
            Publish("leaving-ap", "正在断开设备热点并恢复电脑网络…", 55, _previousConnection?.Ssid, _deviceNetwork.Ssid, canCancel: true);
            await _wifi.DisconnectAsync(_deviceNetwork.InterfaceId);
            await RestoreAndDiscoverAsync(_sessionCancellation?.Token ?? CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
            Publish("cancelled", "配网操作已取消", 0);
        }
        catch (Exception error)
        {
            _log.Error("配网", error.Message);
            Publish("error", error.Message, 0, _previousConnection?.Ssid, _deviceNetwork.Ssid);
        }
    }

    public async Task CancelAndRestoreAsync()
    {
        CancelSessionOnly();
        if (_previousConnection is null)
        {
            Publish("cancelled", "已取消；配网前没有可恢复的 Wi-Fi", 0);
            return;
        }

        try
        {
            Publish("restoring", $"正在恢复 {_previousConnection.Ssid}…", 30, _previousConnection.Ssid);
            await _wifi.ConnectProfileAsync(_previousConnection.InterfaceId, _previousConnection.ProfileName);
            await WaitForConnectionAsync(
                connection => connection?.Ssid.Equals(_previousConnection.Ssid, StringComparison.OrdinalIgnoreCase) == true,
                TimeSpan.FromSeconds(25),
                CancellationToken.None);
            Publish("cancelled", $"已恢复 {_previousConnection.Ssid}", 0, _previousConnection.Ssid);
        }
        catch (Exception error)
        {
            _log.Error("配网恢复", error.Message);
            Publish("error", $"配网已取消，但恢复原 Wi-Fi 失败：{error.Message}", 0, _previousConnection.Ssid);
        }
    }

    private async Task MonitorDeviceApAsync(CancellationToken cancellationToken)
    {
        if (_deviceNetwork is null) return;
        await Task.Delay(TimeSpan.FromSeconds(8), cancellationToken);
        var disconnectedCount = 0;
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromMinutes(20);
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = await _wifi.GetCurrentConnectionAsync(cancellationToken);
            if (current?.Ssid.Equals(_deviceNetwork.Ssid, StringComparison.OrdinalIgnoreCase) == true)
            {
                disconnectedCount = 0;
            }
            else
            {
                disconnectedCount++;
                if (disconnectedCount >= 3)
                {
                    _log.Info("配网", "检测到设备热点已经退出，开始恢复电脑网络");
                    await RestoreAndDiscoverAsync(cancellationToken);
                    return;
                }
            }
            await Task.Delay(1800, cancellationToken);
        }

        Publish("provisioning", "仍在等待设备退出配网热点；可点击“已完成配置”手动继续", 45,
            _previousConnection?.Ssid, _deviceNetwork.Ssid, Current.SetupUrl, canCancel: true, canForceComplete: true);
    }

    private async Task MonitorDeviceApSafeAsync(CancellationToken cancellationToken)
    {
        try
        {
            await MonitorDeviceApAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // The user cancelled or started a new provisioning session.
        }
        catch (Exception error)
        {
            _log.Error("配网监控", error.Message);
            Publish("error", error.Message, 0, _previousConnection?.Ssid, _deviceNetwork?.Ssid);
        }
    }

    private async Task RestoreAndDiscoverAsync(CancellationToken cancellationToken)
    {
        if (_previousConnection is not null && !IsDeviceAp(_previousConnection.Ssid))
        {
            Publish("restoring", $"正在重新连接 {_previousConnection.Ssid}…", 62, _previousConnection.Ssid, _deviceNetwork?.Ssid, canCancel: true);
            await _wifi.ConnectProfileAsync(_previousConnection.InterfaceId, _previousConnection.ProfileName, cancellationToken);
            var restored = await WaitForConnectionAsync(
                connection => connection?.Ssid.Equals(_previousConnection.Ssid, StringComparison.OrdinalIgnoreCase) == true,
                TimeSpan.FromSeconds(35),
                cancellationToken);
            if (restored is null) throw new TimeoutException($"无法自动恢复 {_previousConnection.Ssid}，请在 Windows 网络菜单中手动连接。");
        }
        else
        {
            Publish("restoring", "请让电脑连接设备所加入的目标 Wi-Fi…", 62, deviceSsid: _deviceNetwork?.Ssid, canCancel: true);
            var connected = await WaitForConnectionAsync(connection => connection is not null && !IsDeviceAp(connection.Ssid), TimeSpan.FromMinutes(2), cancellationToken);
            if (connected is null) throw new TimeoutException("电脑尚未连接目标 Wi-Fi。");
        }

        Publish("discovering", $"正在发现 {_settings.DeviceHostname}…", 78, _previousConnection?.Ssid, _deviceNetwork?.Ssid, canCancel: true);
        var knownIps = _settings.Devices.Select(device => device.IpAddress).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var byName = await _discovery.FindAsync(_settings.DeviceHostname, null, TimeSpan.FromSeconds(12), cancellationToken);
        Publish("subnet-scan", "正在扫描本地网络并区分多台设备…", 86, _previousConnection?.Ssid, _deviceNetwork?.Ssid, canCancel: true);
        var discovered = await _discovery.ScanLocalSubnetsAsync(cancellationToken);
        var device = discovered.FirstOrDefault(item => !knownIps.Contains(item.IpAddress)) ??
                     byName ??
                     discovered.FirstOrDefault();
        if (device is null)
        {
            throw new TimeoutException("电脑已恢复网络，但没有找到设备。请检查配网密码、访客网络隔离，或手动输入设备 IP。");
        }

        _settings.LastDeviceIp = device.IpAddress;
        _settings.SelectedDeviceIp = device.IpAddress;
        await _saveSettings();
        Publish("complete", $"配网成功，设备地址 {device.IpAddress}", 100, _previousConnection?.Ssid, _deviceNetwork?.Ssid, controlUrl: device.ControlUrl);
        DeviceFound?.Invoke(this, device);
    }

    private async Task<WifiConnection?> WaitForConnectionAsync(Func<WifiConnection?, bool> predicate, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = await _wifi.GetCurrentConnectionAsync(cancellationToken);
            if (predicate(current)) return current;
            await Task.Delay(1000, cancellationToken);
        }
        return null;
    }

    private bool IsDeviceAp(string ssid) => _settings.DeviceApAliases.Any(alias =>
        ssid.Equals(alias, StringComparison.OrdinalIgnoreCase) || ssid.StartsWith(alias + "-", StringComparison.OrdinalIgnoreCase));

    private void CancelSessionOnly()
    {
        if (_sessionCancellation is null) return;
        _sessionCancellation.Cancel();
        _sessionCancellation.Dispose();
        _sessionCancellation = null;
    }

    private void Publish(
        string stage,
        string message,
        int progress,
        string? previousSsid = null,
        string? deviceSsid = null,
        string? setupUrl = null,
        string? controlUrl = null,
        bool canCancel = false,
        bool canForceComplete = false)
    {
        Current = new ProvisioningSnapshot(stage, message, progress, previousSsid, deviceSsid, setupUrl, controlUrl, canCancel, canForceComplete);
        StatusChanged?.Invoke(this, Current);
    }
}
