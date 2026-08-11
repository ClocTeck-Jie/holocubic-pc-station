using System.Text.Json;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using Clocteck.CubicCenter.Models;
using Clocteck.CubicCenter.Services;
using Microsoft.Win32;

namespace Clocteck.CubicCenter.Core;

public sealed class AppController : IAsyncDisposable
{
    private readonly PortableSettingsStore _store = new();
    private readonly AppLog _log = new();
    private readonly SystemStatsService _stats = new();
    private readonly CancellationTokenSource _lifetime = new();
    private AppSettings _settings = new();
    private NativeWifiService? _wifi;
    private DeviceDiscoveryService? _discovery;
    private BuiltinApiServer? _builtIn;
    private ManagedServiceManager? _services;
    private HoloPcMonitorConfigurator? _holoMonitor;
    private HolopetConfigurator? _holopet;
    private SmtcMusicConfigurator? _smtcMusic;
    private StoreServerClient? _storeServer;
    private GitHubStoreInstaller? _githubStoreInstaller;
    private DeviceApiClient? _deviceApi;
    private SerialMonitorService? _serial;
    private Task? _statusTask;
    private Task? _serialStatusTask;
    private Task? _deviceAppServiceTask;
    private string? _holoMonitorConfigKey;
    private readonly SemaphoreSlim _holoMonitorConfigLock = new(1, 1);
    private string? _lastHolopetConfigKey;
    private string? _lastSmtcMusicConfigKey;
    private string? _lastDesktopMirrorConfigKey;
    private string? _lastObservedDeviceAppKey;
    private string? _currentUiLanguage;
    private readonly HashSet<string> _manuallyStoppedServices = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _servicePolicyLock = new();
    private readonly SemaphoreSlim _wifiSerialHandshakeLock = new(1, 1);
    private TaskCompletionSource<bool>? _wifiSerialReadySignal;
    private string? _wifiSerialScanRequestId;
    private string? _wifiSerialProvisionRequestId;

    public event EventHandler<string>? BrowserRequested;
    public event EventHandler? ExitRequested;

    public Func<string, object?, Task>? SendEventAsync { get; set; }

    public async Task InitializeAsync()
    {
        _settings = await _store.LoadAsync();
        ConfigureBundledWorkers();
        foreach (var device in _settings.Devices) device.Online = false;
        _discovery = new DeviceDiscoveryService(_log);
        _holoMonitor = new HoloPcMonitorConfigurator(_log);
        _holopet = new HolopetConfigurator(_log);
        _smtcMusic = new SmtcMusicConfigurator(_log);
        _storeServer = new StoreServerClient(_log);
        _githubStoreInstaller = new GitHubStoreInstaller(_log);
        _deviceApi = new DeviceApiClient(_log);
        _serial = new SerialMonitorService();
        _serial.StatusChanged += (_, snapshot) => QueueEvent("serial.status", snapshot);
        _serial.TextReceived += (_, chunk) => QueueEvent("serial.data", chunk);
        _serial.ProtocolReceived += (_, message) => HandleSerialProtocol(message);
        try
        {
            _wifi = new NativeWifiService(_log);
            _log.Info("Wi-Fi", "Windows WLAN 服务已就绪");
        }
        catch (Exception error)
        {
            _log.Error("Wi-Fi", error.Message);
        }

        _builtIn = new BuiltinApiServer(_log, _stats);
        _services = new ManagedServiceManager(_settings, _builtIn, _log, SaveSettingsAsync);
        _services.StateChanged += (_, workers) => QueueEvent("services.state", workers);
        _log.EntryAdded += (_, entry) => QueueEvent("log.entry", entry);
        await _services.StartAutoServicesAsync();
        _statusTask = StatusLoopAsync(_lifetime.Token);
        _serialStatusTask = SerialStatusLoopAsync(_lifetime.Token);
        _deviceAppServiceTask = DeviceAppServiceLoopAsync(_lifetime.Token);
        _ = RefreshKnownDevicesAsync();
        _log.Info("应用", "Clocteck Cubic Center 0.1.1 已启动");
    }

    public async Task HandleCommandAsync(string json)
    {
        try
        {
            var command = JsonSerializer.Deserialize<BridgeCommand>(json, JsonOptions) ?? new BridgeCommand();
            switch (command.Action)
            {
                case "app.bootstrap":
                    await SendBootstrapAsync();
                    break;
                case "wifi.serial.scan":
                    await ScanWifiOverSerialAsync(GetString(command, "port"), GetInt(command, "baud", 115200));
                    break;
                case "wifi.serial.provision":
                    await ProvisionWifiOverSerialAsync(RequireString(command, "ssid"), GetString(command, "pwd") ?? string.Empty);
                    break;
                case "device.discover":
                    _ = DiscoverDeviceAsync();
                    break;
                case "device.openControl":
                    await OpenControlPageAsync(GetString(command, "ip"));
                    break;
                case "device.connectIp":
                    await ConnectDeviceByIpAsync(RequireString(command, "ip"));
                    break;
                case "device.select":
                    await SelectDeviceAsync(RequireString(command, "ip"));
                    break;
                case "device.remove":
                    await RemoveDeviceAsync(RequireString(command, "ip"));
                    break;
                case "device.control.refresh":
                    await LoadDeviceControlAsync(GetString(command, "ip"));
                    break;
                case "device.web.open":
                    OpenDeviceWebPage(RequireString(command, "path"), GetString(command, "ip"));
                    break;
                case "device.app.launch":
                    await LaunchDeviceAppAsync(command);
                    break;
                case "device.app.exit":
                    await ExitDeviceAppAsync();
                    break;
                case "device.store.load":
                    await LoadDeviceStoreAsync();
                    break;
                case "device.store.description.open":
                    OpenStoreDescriptionPage(RequireString(command, "url"));
                    break;
                case "device.store.pc.download":
                    await DownloadPcStoreAppAsync(command);
                    break;
                case "device.store.pc.install":
                    await InstallCachedPcStoreAppAsync(command);
                    break;
                case "device.store.install":
                    await InstallDeviceAppAsync(command);
                    break;
                case "device.store.uninstall":
                    await UninstallDeviceAppAsync(RequireString(command, "id"));
                    break;
                case "device.settings.save":
                    await SaveDeviceSettingsAsync(command);
                    break;
                case "device.language.sync":
                    await SyncDeviceLanguageAsync(RequireString(command, "language"));
                    break;
                case "device.display.wake":
                    await WakeDeviceAsync();
                    break;
                case "device.alarm.test":
                    await AlarmTestAsync();
                    break;
                case "device.alarm.stop":
                    await AlarmStopAsync();
                    break;
                case "device.firmware.check":
                    await CheckFirmwareAsync();
                    break;
                case "device.firmware.update":
                    await StartFirmwareUpdateAsync();
                    break;
                case "device.fs.list":
                    await ListDeviceFilesAsync(GetString(command, "path"));
                    break;
                case "device.fs.preview":
                    await PreviewDeviceFileAsync(RequireString(command, "path"));
                    break;
                case "device.fs.upload.pick":
                    await PickAndUploadDeviceFilesAsync(GetString(command, "path"), GetString(command, "mediaMode"));
                    break;
                case "device.fs.download":
                    await DownloadDeviceFileAsync(RequireString(command, "path"), GetString(command, "name"));
                    break;
                case "device.fs.delete":
                    await DeleteDeviceFileAsync(RequireString(command, "path"), GetString(command, "parent"));
                    break;
                case "device.fs.rename":
                    await RenameDevicePathAsync(RequireString(command, "path"), RequireString(command, "newPath"), GetString(command, "parent"));
                    break;
                case "device.fs.mkdir":
                    await CreateDeviceDirectoryAsync(RequireString(command, "path"), GetString(command, "parent"));
                    break;
                case "device.fs.paste":
                    await PasteDevicePathAsync(
                        RequireString(command, "sourcePath"),
                        RequireString(command, "destinationPath"),
                        GetBool(command, "isDirectory"),
                        GetBool(command, "move"),
                        GetString(command, "parent"));
                    break;
                case "device.speed.test":
                    await RunDeviceSpeedTestAsync(command);
                    break;
                case "device.latency.test":
                    await RunDeviceLatencyTestAsync(command);
                    break;
                case "device.lua.read":
                    await ReadLuaCodeAsync(GetString(command, "path"));
                    break;
                case "device.lua.save":
                    await SaveLuaCodeAsync(GetString(command, "path"), RequireString(command, "code"), false);
                    break;
                case "device.lua.run":
                    await SaveLuaCodeAsync(GetString(command, "path"), RequireString(command, "code"), true);
                    break;
                case "serial.refresh":
                    await QueueEventAsync("serial.status", RequireSerial().Refresh());
                    break;
                case "serial.connect":
                    await ConnectSerialAsync(RequireString(command, "port"), GetInt(command, "baud", 115200));
                    break;
                case "serial.disconnect":
                    await DisconnectSerialAsync();
                    break;
                case "holoMonitor.configure":
                    _ = ConfigureHoloMonitorAsync(GetString(command, "ip"), true);
                    break;
                case "services.refresh":
                    await QueueEventAsync("services.state", await RequireServices().SnapshotAsync());
                    break;
                case "services.start":
                    await StartManagedServiceAsync(RequireString(command, "id"));
                    break;
                case "services.stop":
                    await StopManagedServiceAsync(RequireString(command, "id"));
                    break;
                case "services.configure":
                    await ConfigureWorkerAsync(RequireString(command, "id"));
                    break;
                case "services.autoStart":
                    await RequireServices().SetAutoStartAsync(RequireString(command, "id"), GetBool(command, "enabled"));
                    break;
                case "desktopMirror.settings.save":
                    await SaveDesktopMirrorSettingsAsync(command);
                    break;
                case "app.exit":
                    ExitRequested?.Invoke(this, EventArgs.Empty);
                    break;
                default:
                    _log.Warn("界面", $"未知命令：{command.Action}");
                    break;
            }
        }
        catch (Exception error)
        {
            _log.Error("命令", error.Message);
            await QueueEventAsync("app.error", new { message = error.Message });
        }
    }

    private async Task SendBootstrapAsync()
    {
        WifiConnection? wifiConnection = null;
        if (_wifi is not null)
        {
            try { wifiConnection = await _wifi.GetCurrentConnectionAsync(_lifetime.Token); } catch { }
        }
        var connection = ComputerNetworkService.Resolve(wifiConnection, SelectedDeviceIp);
        await QueueEventAsync("app.bootstrap", new
        {
            version = "0.1.1",
            wifiAvailable = _wifi is not null,
            wifi = connection,
            devices = _settings.Devices.OrderByDescending(device => device.LastSeen),
            selectedDeviceIp = SelectedDeviceIp,
            services = _services is null ? [] : await _services.SnapshotAsync(),
            stats = _stats.GetSnapshot(),
            hardware = _builtIn?.GetHardwareSnapshot(),
            desktopMirror = _settings.DesktopMirror,
            logs = _log.Snapshot(),
            serial = _serial?.Snapshot(),
        });
    }

    private async Task ScanWifiOverSerialAsync(string? preferredPort, int baudRate)
    {
        var serial = RequireSerial();
        var snapshot = serial.Refresh();
        if (!snapshot.Connected)
        {
            var port = !string.IsNullOrWhiteSpace(preferredPort) &&
                       snapshot.Ports.Contains(preferredPort, StringComparer.OrdinalIgnoreCase)
                ? preferredPort
                : snapshot.Ports.FirstOrDefault();
            if (string.IsNullOrWhiteSpace(port))
            {
                throw new InvalidOperationException("没有发现可用的设备串口。");
            }
            baudRate = Math.Clamp(baudRate, 1200, 3_000_000);
            await QueueEventAsync("wifi.serial.status", new
            {
                status = "waiting",
                message = $"正在自动连接串口 {port}",
            });
            snapshot = serial.Connect(port, baudRate);
            _log.Info("串口配网", $"扫描 Wi-Fi 前已自动连接 {port} @ {baudRate}");
            await QueueEventAsync("serial.status", snapshot);
        }
        await EnsureWifiGuideSerialReadyAsync();
        var id = Guid.NewGuid().ToString("N");
        _wifiSerialScanRequestId = id;
        serial.SendLine("@CUBIC_WIFI/1 " + JsonSerializer.Serialize(new { cmd = "scan", id }));
        await QueueEventAsync("wifi.serial.status", new { id, status = "sent", message = "正在请求设备扫描 Wi-Fi" });
    }

    private async Task ProvisionWifiOverSerialAsync(string ssid, string password)
    {
        ssid = ssid.Trim();
        if (ssid.Length is < 1 or > 64) throw new ArgumentException("Wi-Fi 名称长度无效。");
        if (password.Length > 128) throw new ArgumentException("Wi-Fi 密码长度无效。");
        var serial = RequireSerial();
        await EnsureWifiGuideSerialReadyAsync();
        var id = Guid.NewGuid().ToString("N");
        _wifiSerialProvisionRequestId = id;
        serial.SendLine("@CUBIC_WIFI/1 " + JsonSerializer.Serialize(new { cmd = "provision", id, ssid, pwd = password }));
        _log.Info("串口配网", $"已发送 Wi-Fi 凭据：{ssid}（密码已隐藏）");
        await QueueEventAsync("wifi.serial.status", new { id, status = "sent", ssid, message = "已通过串口发送，等待设备连接" });
    }

    private void HandleSerialProtocol(SerialProtocolMessage message)
    {
        try
        {
            using var doc = JsonDocument.Parse(message.Json);
            var root = doc.RootElement;
            var status = root.TryGetProperty("status", out var statusNode) ? statusNode.GetString() : null;
            var protocolApp = root.TryGetProperty("app", out var appNode) ? appNode.GetString() : null;
            var responseId = root.TryGetProperty("id", out var idNode) ? idNode.GetString() : null;
            if (string.Equals(status, "ready", StringComparison.OrdinalIgnoreCase) &&
                (string.Equals(protocolApp, "serial_wifi_setup", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(protocolApp, "wifi_guide", StringComparison.OrdinalIgnoreCase)))
            {
                _wifiSerialReadySignal?.TrySetResult(true);
                QueueEvent("wifi.serial.status", root.Clone());
                return;
            }

            var networks = default(JsonElement);
            var isScanResult = string.Equals(status, "scan_result", StringComparison.OrdinalIgnoreCase) &&
                               root.TryGetProperty("networks", out networks);
            var isWifiGuide = string.IsNullOrWhiteSpace(protocolApp) ||
                              string.Equals(protocolApp, "serial_wifi_setup", StringComparison.OrdinalIgnoreCase) ||
                              string.Equals(protocolApp, "wifi_guide", StringComparison.OrdinalIgnoreCase);
            var matchesScan = (!string.IsNullOrWhiteSpace(responseId) &&
                               string.Equals(responseId, _wifiSerialScanRequestId, StringComparison.Ordinal)) ||
                              (isScanResult && isWifiGuide);
            var matchesProvision = !string.IsNullOrWhiteSpace(responseId) &&
                                   string.Equals(responseId, _wifiSerialProvisionRequestId, StringComparison.Ordinal);
            if (!matchesScan && !matchesProvision) return;

            if (matchesScan && isScanResult)
            {
                QueueEvent("wifi.networks", new
                {
                    networks = networks.Clone(),
                    currentSsid = root.TryGetProperty("current_ssid", out var currentSsid) ? currentSsid.GetString() : null,
                });
                var networkCount = networks.ValueKind == JsonValueKind.Array ? networks.GetArrayLength() : 0;
                _log.Info("串口配网", $"已同步设备扫描到的 {networkCount} 个 Wi-Fi 热点");
            }
            if (string.Equals(status, "scan_result", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(status, "success", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(status, "error", StringComparison.OrdinalIgnoreCase))
            {
                if (matchesScan) _wifiSerialScanRequestId = null;
                if (matchesProvision) _wifiSerialProvisionRequestId = null;
            }
            QueueEvent("wifi.serial.status", root.Clone());
        }
        catch (JsonException error)
        {
            _log.Warn("串口配网", "忽略无效协议帧：" + error.Message);
        }
    }

    private async Task DiscoverDeviceAsync()
    {
        if (_discovery is null) return;
        await QueueEventAsync("device.discovery", new { status = "working", message = "正在扫描局域网中的 Clocteck Cubic 设备" });

        try
        {
            foreach (var saved in _settings.Devices) saved.Online = false;
            var found = new List<DeviceInfo>();

            var knownAddresses = _settings.Devices
                .Select(device => device.IpAddress)
                .Where(address => !string.IsNullOrWhiteSpace(address))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            foreach (var address in knownAddresses)
            {
                await QueueEventAsync("device.discovery", new
                {
                    status = "working",
                    message = $"正在搜索 {address}"
                });
                var knownDevice = await _discovery.ProbeAsync(address, _lifetime.Token, 1400);
                if (knownDevice is not null) found.Add(knownDevice);
            }

            await QueueEventAsync("device.discovery", new
            {
                status = "working",
                message = "正在读取局域网邻居设备"
            });
            found.AddRange(await _discovery.ScanLocalSubnetsAsync(
                _lifetime.Token,
                address => QueueEvent("device.discovery", new
                {
                    status = "working",
                    message = $"正在搜索 {address}"
                })));
            var devices = found
                .GroupBy(device => device.IpAddress, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToArray();

            foreach (var device in devices) UpsertDevice(device, false);
            if (devices.Length > 0 && string.IsNullOrWhiteSpace(SelectedDeviceIp))
            {
                SelectDevice(devices[0].IpAddress);
            }
            await SaveSettingsAsync();
            await SendDeviceListAsync();

            if (devices.Length == 0)
            {
                await QueueEventAsync("device.discovery", new { status = "not-found", message = "没有发现设备，可手动输入设备 IP 连接" });
                return;
            }

            await QueueEventAsync("device.discovery", new { status = "success", message = $"发现 {devices.Length} 台设备" });
            var selected = SelectedDeviceIp;
            if (!string.IsNullOrWhiteSpace(selected)) _ = SynchronizeCurrentDeviceAppAsync(selected, false);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            // Application is closing.
        }
        catch (Exception error)
        {
            _log.Error("设备发现", error.Message);
            await QueueEventAsync("device.discovery", new { status = "error", message = error.Message });
        }
    }

    private async Task RefreshKnownDevicesAsync()
    {
        if (_discovery is null || _settings.Devices.Count == 0) return;
        try
        {
            var tasks = _settings.Devices
                .Select(device => _discovery.ProbeAsync(device.IpAddress, _lifetime.Token, 1800))
                .ToArray();
            var found = (await Task.WhenAll(tasks)).Where(device => device is not null).Cast<DeviceInfo>().ToArray();
            foreach (var device in found) UpsertDevice(device, false);
            await SaveSettingsAsync();
            await SendDeviceListAsync();
            if (found.Any(device => device.IpAddress.Equals(SelectedDeviceIp, StringComparison.OrdinalIgnoreCase)))
                _ = SynchronizeCurrentDeviceAppAsync(SelectedDeviceIp, false);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            // Application is closing.
        }
        catch (Exception error)
        {
            _log.Warn("设备发现", "刷新已保存设备失败：" + error.Message);
        }
    }

    private async Task ConfigureHoloMonitorAsync(string? deviceHost, bool force)
    {
        if (_holoMonitor is null) return;
        deviceHost = string.IsNullOrWhiteSpace(deviceHost) ? SelectedDeviceIp : deviceHost;
        if (string.IsNullOrWhiteSpace(deviceHost))
        {
            await QueueEventAsync("holoMonitor.config", new { status = "error", message = "请先选择一台设备" });
            return;
        }

        var monitorPort = _settings.Workers.FirstOrDefault(worker => worker.Id == "pc-monitor")?.Port ?? 17322;
        var hardware = _builtIn?.GetHardwareSnapshot();
        var routeIp = ComputerNetworkService.Resolve(null, deviceHost)?.Ipv4Address ?? string.Empty;
        var configKey = $"{deviceHost}|{routeIp}|{monitorPort}|{hardware?.CpuName}|{hardware?.GpuName}";
        await _holoMonitorConfigLock.WaitAsync(_lifetime.Token);
        try
        {
            if (!force && string.Equals(_holoMonitorConfigKey, configKey, StringComparison.OrdinalIgnoreCase)) return;
            await QueueEventAsync("holoMonitor.config", new
            {
                status = "working",
                message = "正在识别当前电脑 IP并配置 Holo PC Monitor…",
            });
            var result = await _holoMonitor.ConfigureAsync(
                deviceHost,
                monitorPort,
                hardware?.CpuName,
                hardware?.GpuName,
                _lifetime.Token);
            if (result.Ok) _holoMonitorConfigKey = configKey;
            await QueueEventAsync("holoMonitor.config", new
            {
                status = result.Ok ? "success" : "error",
                result.Message,
                result.DeviceIp,
                result.ComputerIp,
                result.Port,
                result.Path,
                result.DataUrl,
            });
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            // Application is closing.
        }
        finally
        {
            _holoMonitorConfigLock.Release();
        }
    }

    private async Task EnsureCompanionForSnapshotAsync(
        string ip,
        JsonElement state,
        JsonElement settings,
        bool forceConfiguration,
        string? requestedWeatherAddress = null,
        string? requestedLanguage = null)
    {
        var appId = CurrentAppId(state);
        var appKey = string.IsNullOrWhiteSpace(appId) ? $"{ip}:launcher" : $"{ip}:{appId}";
        var appChanged = !string.Equals(_lastObservedDeviceAppKey, appKey, StringComparison.OrdinalIgnoreCase);
        if (appChanged)
        {
            _lastObservedDeviceAppKey = appKey;
            var newService = CompanionServiceForApp(appId);
            if (newService is not null)
            {
                lock (_servicePolicyLock) _manuallyStoppedServices.Remove(newService);
            }
        }

        var serviceId = CompanionServiceForApp(appId);
        if (serviceId is null) return;
        await EnsureCompanionServiceStartedAsync(appId!, appChanged || forceConfiguration);
        lock (_servicePolicyLock)
        {
            if (_manuallyStoppedServices.Contains(serviceId)) return;
        }

        if (serviceId == "pc-monitor")
        {
            await ConfigureHoloMonitorAsync(ip, false);
            return;
        }

        if (serviceId == "desktop-mirror")
        {
            await ConfigureDesktopMirrorAsync(ip, forceConfiguration);
            return;
        }

        if (serviceId == "smtc-music" && _smtcMusic is not null)
        {
            var smtcRoute = ReadJsonText(state, "current_route_base") ?? "/smtc_music";
            var smtcPort = _settings.Workers.FirstOrDefault(worker => worker.Id == "smtc-music")?.Port ?? 17865;
            var smtcConfigKey = $"{ip}|{smtcRoute}|{smtcPort}";
            if (!forceConfiguration && string.Equals(_lastSmtcMusicConfigKey, smtcConfigKey, StringComparison.Ordinal)) return;
            var smtcResult = await _smtcMusic.ConfigureAsync(ip, smtcRoute, smtcPort, _lifetime.Token);
            if (smtcResult.Ok) _lastSmtcMusicConfigKey = smtcConfigKey;
            await QueueEventAsync("smtcMusic.config", new
            {
                status = smtcResult.Ok ? "success" : "error",
                smtcResult.Message,
                smtcResult.DeviceIp,
                smtcResult.ComputerIp,
                smtcResult.Port,
                smtcResult.Route,
                smtcResult.BridgeUrl,
            });
            return;
        }

        if (serviceId != "holopet" || _holopet is null) return;
        var language = NormalizeLanguage(requestedLanguage) ?? _currentUiLanguage ??
                       NormalizeLanguage(ReadJsonText(settings, "language", "locale", "lang")) ?? "zh-CN";
        var weatherAddress = string.IsNullOrWhiteSpace(requestedWeatherAddress)
            ? ReadJsonText(settings, "weather_address", "weatherAddress")
            : requestedWeatherAddress.Trim();
        var route = ReadJsonText(state, "current_route_base") ?? "/holopet";
        var port = _settings.Workers.FirstOrDefault(worker => worker.Id == "holopet")?.Port ?? 17321;
        var configKey = $"{ip}|{route}|{port}|{language}|{weatherAddress}";
        if (!forceConfiguration && string.Equals(_lastHolopetConfigKey, configKey, StringComparison.Ordinal)) return;

        var updates = new Dictionary<string, object?> { ["language"] = language };
        if (!string.IsNullOrWhiteSpace(weatherAddress)) updates["weather_address"] = weatherAddress;
        await RequireDeviceApi().SaveSettingsAsync(ip, updates, _lifetime.Token);

        var result = await _holopet.ConfigureAsync(ip, route, port, _lifetime.Token);
        if (result.Ok) _lastHolopetConfigKey = configKey;
        await QueueEventAsync("holopet.config", new
        {
            status = result.Ok ? "success" : "error",
            result.Message,
            result.DeviceIp,
            result.ComputerIp,
            result.Port,
            result.Route,
            result.EventUrl,
            weatherAddress,
            language,
        });
    }

    private async Task EnsureCompanionServiceStartedAsync(string appId, bool explicitActivation)
    {
        var serviceId = CompanionServiceForApp(appId);
        if (serviceId is null) return;
        lock (_servicePolicyLock)
        {
            if (explicitActivation) _manuallyStoppedServices.Remove(serviceId);
            else if (_manuallyStoppedServices.Contains(serviceId)) return;
        }
        await RequireServices().StartAsync(serviceId);
    }

    private async Task StartManagedServiceAsync(string serviceId)
    {
        lock (_servicePolicyLock) _manuallyStoppedServices.Remove(serviceId);
        await RequireServices().StartAsync(serviceId);
    }

    private async Task StopManagedServiceAsync(string serviceId)
    {
        lock (_servicePolicyLock) _manuallyStoppedServices.Add(serviceId);
        if (serviceId.Equals("holopet", StringComparison.OrdinalIgnoreCase)) _lastHolopetConfigKey = null;
        if (serviceId.Equals("smtc-music", StringComparison.OrdinalIgnoreCase)) _lastSmtcMusicConfigKey = null;
        if (serviceId.Equals("pc-monitor", StringComparison.OrdinalIgnoreCase)) _holoMonitorConfigKey = null;
        if (serviceId.Equals("desktop-mirror", StringComparison.OrdinalIgnoreCase)) _lastDesktopMirrorConfigKey = null;
        await RequireServices().StopAsync(serviceId);
    }

    private async Task ConfigureDesktopMirrorAsync(string deviceIp, bool force)
    {
        var computerIp = ComputerNetworkService.Resolve(null, deviceIp)?.Ipv4Address;
        var port = _settings.Workers.FirstOrDefault(worker => worker.Id == "desktop-mirror")?.Port ?? 8787;
        if (string.IsNullOrWhiteSpace(computerIp))
        {
            await QueueEventAsync("desktopMirror.config", new
            {
                status = "error",
                message = "找不到可访问设备的电脑 IPv4 地址",
                deviceIp,
                port,
            });
            return;
        }

        var configKey = $"{deviceIp}|{computerIp}|{port}";
        if (!force && string.Equals(_lastDesktopMirrorConfigKey, configKey, StringComparison.Ordinal)) return;

        var payload = System.Text.Json.JsonSerializer.Serialize(new
        {
            url = $"ws://{computerIp}:{port}",
            ws_buffer = 32768,
            overlay = false,
            serial_debug = true,
            serial_debug_every = 1,
            serial_debug_interval_ms = 5000,
        });
        try
        {
            await RequireDeviceApi().UploadFileAsync(
                deviceIp,
                "/sd/apps/desktop_mirror/config.json",
                new System.Text.UTF8Encoding(false).GetBytes(payload),
                "application/json; charset=utf-8",
                _lifetime.Token);
            _lastDesktopMirrorConfigKey = configKey;
            _log.Info("桌面投屏", $"已通过 FS 同步设备配置 ws://{computerIp}:{port}");
            await QueueEventAsync("desktopMirror.config", new
            {
                status = "success",
                message = "投屏配置已通过 FS 同步",
                deviceIp,
                computerIp,
                port,
                url = $"ws://{computerIp}:{port}",
            });
        }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException)
        {
            _log.Warn("桌面投屏", $"FS 同步投屏配置失败：{error.Message}");
            await QueueEventAsync("desktopMirror.config", new
            {
                status = "error",
                message = error.Message,
                deviceIp,
                computerIp,
                port,
            });
        }
    }

    private async Task SynchronizeCurrentDeviceAppAsync(string? ip, bool force)
    {
        if (string.IsNullOrWhiteSpace(ip) || _deviceApi is null) return;
        try
        {
            var state = await _deviceApi.GetStateAsync(ip, _lifetime.Token);
            var saved = _settings.Devices.FirstOrDefault(device => device.IpAddress.Equals(ip, StringComparison.OrdinalIgnoreCase));
            if (saved is not null)
            {
                saved.Online = true;
                saved.LastSeen = DateTimeOffset.Now;
                UpdateDeviceRuntime(saved, state);
                await SendDeviceListAsync();
            }

            var currentAppId = CurrentAppId(state);
            var appKey = $"{ip}:{currentAppId ?? "launcher"}";
            if (!force &&
                string.Equals(_lastObservedDeviceAppKey, appKey, StringComparison.OrdinalIgnoreCase) &&
                CompanionServiceForApp(currentAppId) is null)
            {
                return;
            }
            var snapshot = await _deviceApi.GetControlSnapshotAsync(ip, _lifetime.Token);
            await EnsureCompanionForSnapshotAsync(ip, snapshot.State, snapshot.Settings, force);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException)
        {
            _log.Warn("应用服务", $"检查设备 {ip} 当前应用失败：{error.Message}");
        }
    }

    private async Task DeviceAppServiceLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await SynchronizeCurrentDeviceAppAsync(SelectedDeviceIp, false);
                await Task.Delay(4000, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception error)
            {
                _log.Warn("应用服务", error.Message);
                await Task.Delay(4000, cancellationToken);
            }
        }
    }

    private static string? CompanionServiceForApp(string? appId) => appId?.Trim().ToLowerInvariant() switch
    {
        "holopet" or "holo_pet" or "clawd_monitor" => "holopet",
        "holo_pc_monitor" or "holo-pc-monitor" => "pc-monitor",
        "codex_buddy" or "codex-buddy" => "codex-core",
        "desktop_mirror" or "desktop-mirror" => "desktop-mirror",
        "smtc_music" or "smtc-music" or "holocubic-smtc-music" => "smtc-music",
        _ => null,
    };

    private void ConfigureBundledWorkers()
    {
        var mirror = _settings.Workers.FirstOrDefault(item => item.Id.Equals("desktop-mirror", StringComparison.OrdinalIgnoreCase));
        if (mirror is not null)
        {
            var mirrorDirectory = Path.Combine(AppContext.BaseDirectory, "resources", "CompanionServices", "desktop-mirror");
            var bundledPython = Path.Combine(mirrorDirectory, "python", "python.exe");
            mirror.Executable = File.Exists(bundledPython) ? bundledPython : "python.exe";
            var mirrorSettings = _settings.DesktopMirror ?? new DesktopMirrorSettings();
            var source = mirrorSettings.Source is "virtual-monitor" or "region" ? mirrorSettings.Source : "screen";
            var args = $"\"{Path.Combine(mirrorDirectory, "desktop_mirror_server.py")}\" --host 0.0.0.0 --port 8787 --source {source} --monitor {Math.Clamp(mirrorSettings.Monitor, 1, 16)} --fps {Math.Clamp(mirrorSettings.Fps, 1, 30)} --quality {Math.Clamp(mirrorSettings.Quality, 1, 95)} --width 320 --height 240 --fit {NormalizeMirrorFit(mirrorSettings.Fit)}";
            if (source == "virtual-monitor" && !string.IsNullOrWhiteSpace(mirrorSettings.MonitorResolution)) args += $" --monitor-resolution {mirrorSettings.MonitorResolution}";
            if (source == "region" && !string.IsNullOrWhiteSpace(mirrorSettings.Region)) args += $" --region {mirrorSettings.Region}";
            mirror.Arguments = args;
            mirror.WorkingDirectory = mirrorDirectory;
            mirror.Port = 8787;
            // desktop-mirror is a WebSocket server, so the manager uses the listening port as its health check.
            mirror.HealthPath = string.Empty;
            mirror.BuiltIn = false;
        }

        var worker = _settings.Workers.FirstOrDefault(item => item.Id.Equals("smtc-music", StringComparison.OrdinalIgnoreCase));
        if (worker is null) return;
        var serviceDirectory = Path.Combine(AppContext.BaseDirectory, "resources", "CompanionServices", "smtc");
        worker.Executable = Path.Combine(AppContext.BaseDirectory, "resources", "CompanionServices", "node", "node.exe");
        worker.Arguments = $"\"{Path.Combine(serviceDirectory, "smtc-bridge.js")}\"";
        worker.WorkingDirectory = serviceDirectory;
        worker.Port = 17865;
        worker.HealthPath = "/health";
        worker.AutoStart = false;
        worker.BuiltIn = false;
    }

    private static string NormalizeMirrorFit(string? fit) => fit is "contain" or "cover" or "stretch" ? fit : "stretch";

    private async Task SaveDesktopMirrorSettingsAsync(BridgeCommand command)
    {
        var source = GetString(command, "source") ?? "screen";
        if (source is not ("screen" or "virtual-monitor" or "region")) throw new ArgumentException("无效的投屏来源");
        var settings = _settings.DesktopMirror ??= new DesktopMirrorSettings();
        settings.Source = source;
        settings.Monitor = Math.Clamp(GetInt(command, "monitor", settings.Monitor), 1, 16);
        settings.MonitorResolution = (GetString(command, "monitorResolution") ?? settings.MonitorResolution ?? "").Trim();
        settings.Region = (GetString(command, "region") ?? settings.Region ?? "").Trim();
        settings.Fit = NormalizeMirrorFit(GetString(command, "fit") ?? settings.Fit);
        settings.Fps = Math.Clamp(GetInt(command, "fps", settings.Fps), 1, 30);
        settings.Quality = Math.Clamp(GetInt(command, "quality", settings.Quality), 1, 95);
        if (source == "virtual-monitor" && !System.Text.RegularExpressions.Regex.IsMatch(settings.MonitorResolution, @"^\d{2,5}x\d{2,5}$", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            throw new ArgumentException("虚拟副屏分辨率格式应为 WIDTHxHEIGHT，例如 640x480");
        if (source == "region" && !System.Text.RegularExpressions.Regex.IsMatch(settings.Region, @"^-?\d+,-?\d+,\d+,\d+$"))
            throw new ArgumentException("区域格式应为 x,y,width,height");
        await _store.SaveAsync(_settings);
        ConfigureBundledWorkers();
        _lastDesktopMirrorConfigKey = null;
        await RequireServices().StopAsync("desktop-mirror");
        await RequireServices().StartAsync("desktop-mirror");
        await QueueEventAsync("desktopMirror.settings.saved", new { status = "success", settings });
        await SendBootstrapAsync();
    }

    private static string? CurrentAppId(JsonElement state)
    {
        return state.TryGetProperty("current_app", out var current) && current.ValueKind == JsonValueKind.Object &&
               current.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String
            ? id.GetString()
            : null;
    }

    private static string? ReadJsonText(JsonElement source, params string[] names)
    {
        if (source.ValueKind != JsonValueKind.Object) return null;
        foreach (var name in names)
        {
            if (!source.TryGetProperty(name, out var value)) continue;
            var text = value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();
            if (!string.IsNullOrWhiteSpace(text)) return text.Trim();
        }
        return null;
    }

    private static string? NormalizeLanguage(string? language) => language?.Trim() switch
    {
        "zh-CN" => "zh-CN",
        "zh-TW" => "zh-TW",
        "en" => "en",
        "ja" => "ja",
        _ => null,
    };

    private async Task ConnectDeviceByIpAsync(string input)
    {
        if (_discovery is null) return;
        if (!IPAddress.TryParse(input.Trim(), out var address) || address.AddressFamily != AddressFamily.InterNetwork)
        {
            await QueueEventAsync("device.discovery", new { status = "error", message = "请输入有效的 IPv4 地址，例如 192.168.0.188" });
            return;
        }

        var ip = address.ToString();
        await QueueEventAsync("device.discovery", new { status = "working", message = $"正在连接 {ip}" });
        var device = await _discovery.ProbeAsync(ip, _lifetime.Token, 3500);
        if (device is null)
        {
            await QueueEventAsync("device.discovery", new { status = "not-found", message = $"{ip} 没有响应 Clocteck 设备接口" });
            return;
        }

        await RegisterDeviceAsync(device, true, true);
        await QueueEventAsync("device.discovery", new { status = "success", message = $"已连接设备 {ip}" });
    }

    private async Task RegisterDeviceAsync(DeviceInfo device, bool select, bool configureMonitor)
    {
        UpsertDevice(device, select);
        await SaveSettingsAsync();
        await SendDeviceListAsync();
        await QueueEventAsync("device.found", device);
        if (configureMonitor) await SynchronizeCurrentDeviceAppAsync(device.IpAddress, false);
    }

    private async Task RegisterProvisionedDeviceAsync(DeviceInfo device)
    {
        await RegisterDeviceAsync(device, true, true);
        await OpenControlPageAsync(device.IpAddress);
    }

    private void UpsertDevice(DeviceInfo device, bool select)
    {
        var saved = _settings.Devices.FirstOrDefault(item =>
            item.IpAddress.Equals(device.IpAddress, StringComparison.OrdinalIgnoreCase));
        if (saved is null)
        {
            saved = new SavedDevice { IpAddress = device.IpAddress };
            _settings.Devices.Add(saved);
        }
        saved.Name = "Clocteck Cubic";
        saved.DeviceId = device.DeviceId ?? saved.DeviceId;
        saved.LastSeen = DateTimeOffset.Now;
        saved.Online = true;
        if (!string.IsNullOrWhiteSpace(device.RawState))
        {
            try
            {
                using var document = JsonDocument.Parse(device.RawState);
                UpdateDeviceRuntime(saved, document.RootElement);
            }
            catch (JsonException) { }
        }
        if (select) SelectDevice(device.IpAddress);
    }

    private async Task SelectDeviceAsync(string input)
    {
        if (!IPAddress.TryParse(input.Trim(), out var address) || address.AddressFamily != AddressFamily.InterNetwork)
        {
            throw new ArgumentException("设备 IP 无效。");
        }
        var ip = address.ToString();
        if (_settings.Devices.All(device => !device.IpAddress.Equals(ip, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("请先连接该 IP 对应的设备。");
        }
        SelectDevice(ip);
        await SaveSettingsAsync();
        await SendDeviceListAsync();
    }

    private void SelectDevice(string ip)
    {
        _settings.SelectedDeviceIp = ip;
        _settings.LastDeviceIp = ip;
    }

    private async Task RemoveDeviceAsync(string input)
    {
        var ip = input.Trim();
        _settings.Devices.RemoveAll(device => device.IpAddress.Equals(ip, StringComparison.OrdinalIgnoreCase));
        if (string.Equals(SelectedDeviceIp, ip, StringComparison.OrdinalIgnoreCase))
        {
            var replacement = _settings.Devices.OrderByDescending(device => device.LastSeen).FirstOrDefault()?.IpAddress;
            _settings.SelectedDeviceIp = replacement;
            _settings.LastDeviceIp = replacement;
        }
        await SaveSettingsAsync();
        await SendDeviceListAsync();
    }

    private async Task OpenControlPageAsync(string? requestedIp)
    {
        if (!string.IsNullOrWhiteSpace(requestedIp)) await SelectDeviceAsync(requestedIp);
        var ip = RequireSelectedDeviceIp();
        await QueueEventAsync("device.control.open", new { ip });
        await LoadDeviceControlAsync(ip);
    }

    private async Task LoadDeviceControlAsync(string? requestedIp)
    {
        var ip = string.IsNullOrWhiteSpace(requestedIp) ? RequireSelectedDeviceIp() : requestedIp;
        if (_deviceApi is null) return;
        try
        {
            await QueueEventAsync("device.control.status", new { status = "working", message = $"正在读取 {ip}" });
            var snapshot = await _deviceApi.GetControlSnapshotAsync(ip, _lifetime.Token);
            var saved = _settings.Devices.FirstOrDefault(device => device.IpAddress.Equals(ip, StringComparison.OrdinalIgnoreCase));
            if (saved is not null)
            {
                saved.Online = true;
                saved.LastSeen = DateTimeOffset.Now;
                UpdateDeviceRuntime(saved, snapshot.State);
                await SaveSettingsAsync();
            }
            await QueueEventAsync("device.control", snapshot);
            await SendDeviceListAsync();
            await EnsureCompanionForSnapshotAsync(ip, snapshot.State, snapshot.Settings, false);
        }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException)
        {
            var saved = _settings.Devices.FirstOrDefault(device => device.IpAddress.Equals(ip, StringComparison.OrdinalIgnoreCase));
            if (saved is not null) saved.Online = false;
            await QueueEventAsync("device.control.status", new { status = "error", message = $"无法连接 {ip}：{error.Message}" });
            await SendDeviceListAsync();
        }
    }

    private void OpenDeviceWebPage(string path, string? requestedIp)
    {
        var ip = string.IsNullOrWhiteSpace(requestedIp) ? RequireSelectedDeviceIp() : requestedIp;
        path = path.Trim();
        if (!path.StartsWith('/') || path.StartsWith("//", StringComparison.Ordinal) || path.Contains('\r') || path.Contains('\n'))
        {
            throw new ArgumentException("设备页面路径无效。");
        }
        BrowserRequested?.Invoke(this, $"http://{ip}{path}");
    }

    private async Task LaunchDeviceAppAsync(BridgeCommand command)
    {
        var appId = RequireString(command, "id");
        var ip = RequireSelectedDeviceIp();
        var requestedLanguage = NormalizeLanguage(GetString(command, "language"));
        var requestedWeatherAddress = GetString(command, "weatherAddress")?.Trim();
        if (requestedLanguage is not null) _currentUiLanguage = requestedLanguage;
        await EnsureCompanionServiceStartedAsync(appId, true);
        if (CompanionServiceForApp(appId) == "desktop-mirror")
        {
            await ConfigureDesktopMirrorAsync(ip, true);
        }
        await QueueEventAsync("device.app.starting", new { id = appId });
        try { await RequireDeviceApi().WakeAsync(ip, _lifetime.Token); }
        catch (HttpRequestException) { }
        var result = await RequireDeviceApi().LaunchAppAsync(ip, appId, _lifetime.Token);
        var readiness = await WaitForDeviceAppPageAsync(ip, appId);
        await QueueEventAsync("device.action", new
        {
            action = "launch",
            ok = true,
            id = appId,
            controlReady = readiness.Ready,
            route = readiness.Route,
            result,
        });
        var state = await RequireDeviceApi().GetStateAsync(ip, _lifetime.Token);
        var settings = (await RequireDeviceApi().GetControlSnapshotAsync(ip, _lifetime.Token)).Settings;
        await EnsureCompanionForSnapshotAsync(ip, state, settings, true, requestedWeatherAddress, requestedLanguage);
        await LoadDeviceControlAsync(ip);
    }

    private async Task<(bool Ready, string? Route)> WaitForDeviceAppPageAsync(string ip, string appId)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(9);
        string? route = null;
        var confirmed = false;
        while (DateTimeOffset.UtcNow < deadline && !_lifetime.IsCancellationRequested)
        {
            try
            {
                var state = await RequireDeviceApi().GetStateAsync(ip, _lifetime.Token);
                var currentId = state.TryGetProperty("current_app", out var current) && current.ValueKind == JsonValueKind.Object &&
                                current.TryGetProperty("id", out var id)
                    ? id.GetString()
                    : null;
                var matchesRequestedApp = string.Equals(currentId, appId, StringComparison.OrdinalIgnoreCase);
                if (matchesRequestedApp && !confirmed)
                {
                    confirmed = true;
                    await QueueEventAsync("device.app.confirmed", new { id = appId, state });
                }
                if (matchesRequestedApp &&
                    state.TryGetProperty("current_route_base", out var routeNode) &&
                    routeNode.ValueKind == JsonValueKind.String)
                {
                    route = routeNode.GetString();
                    if (!string.IsNullOrWhiteSpace(route) &&
                        await RequireDeviceApi().IsPageReadyAsync(ip, route, TimeSpan.FromMilliseconds(1300), _lifetime.Token))
                    {
                        return (true, route);
                    }
                }
            }
            catch (HttpRequestException) { }
            catch (TaskCanceledException) when (!_lifetime.IsCancellationRequested) { }
            await Task.Delay(600, _lifetime.Token);
        }
        return (false, route);
    }

    private async Task ExitDeviceAppAsync()
    {
        var ip = RequireSelectedDeviceIp();
        var result = await RequireDeviceApi().ExitAppAsync(ip, _lifetime.Token);
        await QueueEventAsync("device.action", new { action = "exit", ok = true, result });
        await Task.Delay(350, _lifetime.Token);
        await LoadDeviceControlAsync(ip);
    }

    private async Task LoadDeviceStoreAsync()
    {
        var ip = RequireSelectedDeviceIp();
        await QueueEventAsync("device.store.status", new
        {
            status = "working",
            message = $"正在同时读取服务器目录和 {ip} 的应用信息"
        });

        var catalogTask = RequireStoreServer().GetCatalogAsync(_lifetime.Token);
        var snapshotTask = RequireDeviceApi().GetControlSnapshotAsync(ip, _lifetime.Token);
        await Task.WhenAll(catalogTask, snapshotTask);
        var catalog = await catalogTask;
        var snapshot = await snapshotTask;

        var saved = _settings.Devices.FirstOrDefault(device =>
            device.IpAddress.Equals(ip, StringComparison.OrdinalIgnoreCase));
        if (saved is not null)
        {
            saved.Online = true;
            saved.LastSeen = DateTimeOffset.Now;
            UpdateDeviceRuntime(saved, snapshot.State);
            await SaveSettingsAsync();
        }

        await QueueEventAsync("device.store", new { ip, catalog, snapshot });
        await SendPcStoreCacheAsync();
    }

    private void OpenStoreDescriptionPage(string url)
    {
        if (!StoreServerClient.IsTrustedStoreUri(url, "/apps/", out var uri))
        {
            throw new ArgumentException("应用介绍地址无效。");
        }
        BrowserRequested?.Invoke(this, uri.AbsoluteUri);
    }

    private async Task InstallDeviceAppAsync(BridgeCommand command)
    {
        var ip = RequireSelectedDeviceIp();
        var id = RequireString(command, "id");
        var result = await RequireDeviceApi().InstallAppAsync(
            ip,
            RequireString(command, "manifestUrl"),
            id,
            GetString(command, "name") ?? id,
            _lifetime.Token);
        await QueueEventAsync("device.store.status", new { status = "success", message = $"已提交安装：{id}", result });
        await LoadDeviceControlAsync(ip);
    }

    private async Task DownloadPcStoreAppAsync(BridgeCommand command)
    {
        var id = RequireString(command, "id");
        try
        {
            var result = await RequireGitHubStoreInstaller().DownloadAsync(
                id,
                RequireString(command, "version"),
                progress => QueueEventAsync("device.store.pc-progress", progress),
                _lifetime.Token);
            await QueueEventAsync("device.store.status", new { status = "done", message = $"已下载到电脑：{id} {result.Version}", result });
            await SendPcStoreCacheAsync();
        }
        catch (Exception error) when (error is HttpRequestException or IOException or InvalidOperationException or ArgumentException or TaskCanceledException)
        {
            _log.Warn("应用商店", $"PC 下载 {id} 失败：{error.Message}");
            await QueueEventAsync("device.store.pc-progress", new
            {
                appId = id,
                status = "error",
                phase = "download",
                percent = 0,
                completed = 0,
                total = 0,
                message = $"下载失败：{error.Message}",
            });
        }
    }

    private async Task InstallCachedPcStoreAppAsync(BridgeCommand command)
    {
        var ip = RequireSelectedDeviceIp();
        var id = RequireString(command, "id");
        try
        {
            var state = await RequireDeviceApi().GetStateAsync(ip, _lifetime.Token);
            if (string.Equals(CurrentAppId(state), id, StringComparison.OrdinalIgnoreCase))
            {
                await RequireDeviceApi().ExitAppAsync(ip, _lifetime.Token);
                await Task.Delay(350, _lifetime.Token);
            }

            var result = await RequireGitHubStoreInstaller().InstallCachedAsync(
                id,
                RequireString(command, "version"),
                GetString(command, "transport") ?? "fs",
                ip,
                RequireDeviceApi(),
                progress => QueueEventAsync("device.store.pc-progress", progress),
                _lifetime.Token);
            await QueueEventAsync("device.store.status", new { status = "done", message = $"安装完成：{id} {result.Version}", result });
            await LoadDeviceControlAsync(ip);
        }
        catch (Exception error) when (error is HttpRequestException or IOException or InvalidOperationException or ArgumentException or TaskCanceledException)
        {
            _log.Warn("应用商店", $"安装到设备 {id} 失败：{error.Message}");
            await QueueEventAsync("device.store.pc-progress", new
            {
                appId = id,
                status = "error",
                phase = "install",
                percent = 0,
                completed = 0,
                total = 0,
                message = $"安装失败：{error.Message}",
            });
        }
    }

    private Task SendPcStoreCacheAsync() =>
        QueueEventAsync("device.store.pc-cache", new { packages = RequireGitHubStoreInstaller().GetCachedPackages() });

    private async Task UninstallDeviceAppAsync(string appId)
    {
        var ip = RequireSelectedDeviceIp();
        var result = await RequireDeviceApi().UninstallAppAsync(ip, appId, _lifetime.Token);
        await QueueEventAsync("device.store.status", new { status = "success", message = $"已卸载：{appId}", result });
        await LoadDeviceControlAsync(ip);
    }

    private async Task SaveDeviceSettingsAsync(BridgeCommand command)
    {
        var ip = RequireSelectedDeviceIp();
        var result = await RequireDeviceApi().SaveSettingsAsync(ip, command.Payload, _lifetime.Token);
        await QueueEventAsync("device.settings.saved", new { ok = true, message = "设备设置已保存", result });
        await LoadDeviceControlAsync(ip);
    }

    private async Task SyncDeviceLanguageAsync(string language)
    {
        language = language.Trim();
        if (language is not ("zh-CN" or "en" or "ja" or "zh-TW"))
        {
            throw new ArgumentException("不支持的界面语言。");
        }
        _currentUiLanguage = language;
        var ip = RequireSelectedDeviceIp();
        var result = await RequireDeviceApi().SaveSettingsAsync(ip,
            new Dictionary<string, object?> { ["language"] = language }, _lifetime.Token);
        await QueueEventAsync("device.language.synced", new { ip, language, result });
    }

    private async Task WakeDeviceAsync()
    {
        var ip = RequireSelectedDeviceIp();
        var result = await RequireDeviceApi().WakeAsync(ip, _lifetime.Token);
        await QueueEventAsync("device.action", new { action = "wake", ok = true, result });
    }

    private async Task AlarmTestAsync()
    {
        var result = await RequireDeviceApi().TestAlarmAsync(RequireSelectedDeviceIp(), _lifetime.Token);
        await QueueEventAsync("device.action", new { action = "alarm-test", ok = true, result });
    }

    private async Task AlarmStopAsync()
    {
        var result = await RequireDeviceApi().StopAlarmAsync(RequireSelectedDeviceIp(), _lifetime.Token);
        await QueueEventAsync("device.action", new { action = "alarm-stop", ok = true, result });
    }

    private async Task CheckFirmwareAsync()
    {
        var result = await RequireDeviceApi().CheckFirmwareAsync(RequireSelectedDeviceIp(), _lifetime.Token);
        await QueueEventAsync("device.firmware", new { action = "check", result });
    }

    private async Task StartFirmwareUpdateAsync()
    {
        var result = await RequireDeviceApi().StartFirmwareUpdateAsync(RequireSelectedDeviceIp(), _lifetime.Token);
        await QueueEventAsync("device.firmware", new { action = "update", result });
    }

    private async Task ListDeviceFilesAsync(string? requestedPath)
    {
        var ip = RequireSelectedDeviceIp();
        var path = NormalizeDeviceDirectory(requestedPath);
        await QueueEventAsync("device.fs.status", new { status = "working", messageKey = "正在读取设备文件" });
        var result = await RequireDeviceApi().ListFilesAsync(ip, path, _lifetime.Token);
        await QueueEventAsync("device.fs.list", new { ip, path, result });
    }

    private async Task PreviewDeviceFileAsync(string path)
    {
        var bytes = await RequireDeviceApi().ReadFileAsync(RequireSelectedDeviceIp(), path, 4 * 1024 * 1024, _lifetime.Token);
        var extension = Path.GetExtension(path).ToLowerInvariant();
        if (ImageMimeTypes.TryGetValue(extension, out var mime))
        {
            await QueueEventAsync("device.fs.preview", new
            {
                path,
                kind = "image",
                content = $"data:{mime};base64,{Convert.ToBase64String(bytes)}",
                size = bytes.Length,
            });
            return;
        }
        if (TextFileExtensions.Contains(extension))
        {
            await QueueEventAsync("device.fs.preview", new
            {
                path,
                kind = "text",
                content = new System.Text.UTF8Encoding(false, false).GetString(bytes),
                size = bytes.Length,
            });
            return;
        }
        await QueueEventAsync("device.fs.preview", new { path, kind = "binary", size = bytes.Length });
    }

    private async Task PickAndUploadDeviceFilesAsync(string? requestedDirectory, string? requestedMediaMode)
    {
        var directory = NormalizeDeviceDirectory(requestedDirectory);
        var mediaMode = requestedMediaMode is "crop" or "fit" ? requestedMediaMode : "original";
        var transformMedia = IsDisplayMediaDirectory(directory) && mediaMode != "original";
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "选择要上传到设备的文件",
            Filter = "媒体与常用文件|*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.webp;*.mp3;*.lrc;*.lua;*.json;*.txt;*.html;*.css;*.js|所有文件|*.*",
            Multiselect = true,
            CheckFileExists = true,
        };
        if (dialog.ShowDialog() != true) return;

        var ip = RequireSelectedDeviceIp();
        foreach (var localPath in dialog.FileNames)
        {
            var info = new FileInfo(localPath);
            if (info.Length > 64L * 1024 * 1024) throw new InvalidOperationException($"{info.Name} 超过 64 MB 上传上限。");
            var devicePath = directory.TrimEnd('/') + "/" + info.Name;
            if (transformMedia && MediaPreparationService.CanTransform(localPath))
            {
                await QueueEventAsync("device.fs.status", new { status = "working", messageKey = "正在处理 {0} 为 320×240", args = new[] { info.Name } });
                var prepared = await MediaPreparationService.PrepareAsync(localPath, mediaMode, _lifetime.Token);
                if (prepared.Content.LongLength > 64L * 1024 * 1024) throw new InvalidOperationException($"{info.Name} 处理后超过 64 MB 上传上限。");
                await RequireDeviceApi().UploadFileAsync(ip, devicePath, prepared.Content, prepared.ContentType, _lifetime.Token);
            }
            else
            {
                await QueueEventAsync("device.fs.status", new { status = "working", messageKey = "正在上传 {0}", args = new[] { info.Name } });
                await RequireDeviceApi().UploadLocalFileAsync(ip, devicePath, localPath, _lifetime.Token);
            }
        }
        var resultMessage = transformMedia ? "已上传 {0} 个文件，图片已处理为 320×240" : "已上传 {0} 个文件";
        await QueueEventAsync("device.fs.status", new { status = "success", messageKey = resultMessage, args = new[] { dialog.FileNames.Length.ToString() } });
        await ListDeviceFilesAsync(directory);
    }

    private static bool IsDisplayMediaDirectory(string directory) =>
        directory.Equals("/sd/images", StringComparison.OrdinalIgnoreCase) ||
        directory.StartsWith("/sd/images/", StringComparison.OrdinalIgnoreCase) ||
        directory.Equals("/sd/gifs", StringComparison.OrdinalIgnoreCase) ||
        directory.StartsWith("/sd/gifs/", StringComparison.OrdinalIgnoreCase);

    private async Task DownloadDeviceFileAsync(string path, string? suggestedName)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "保存设备文件",
            FileName = string.IsNullOrWhiteSpace(suggestedName) ? Path.GetFileName(path) : suggestedName,
            Filter = "所有文件|*.*",
            AddExtension = false,
        };
        if (dialog.ShowDialog() != true) return;
        await QueueEventAsync("device.fs.status", new { status = "working", messageKey = "正在下载设备文件" });
        var bytes = await RequireDeviceApi().ReadFileAsync(RequireSelectedDeviceIp(), path, 64 * 1024 * 1024, _lifetime.Token);
        await File.WriteAllBytesAsync(dialog.FileName, bytes, _lifetime.Token);
        await QueueEventAsync("device.fs.status", new { status = "success", messageKey = "文件已保存到电脑" });
    }

    private async Task DeleteDeviceFileAsync(string path, string? parent)
    {
        await RequireDeviceApi().DeleteFileAsync(RequireSelectedDeviceIp(), path, _lifetime.Token);
        await QueueEventAsync("device.fs.status", new { status = "success", messageKey = "设备文件已删除" });
        await ListDeviceFilesAsync(parent);
    }

    private async Task RenameDevicePathAsync(string path, string newPath, string? parent)
    {
        await QueueEventAsync("device.fs.status", new { status = "working", messageKey = "正在重命名设备项目" });
        await RequireDeviceApi().RenamePathAsync(RequireSelectedDeviceIp(), path, newPath, _lifetime.Token);
        await QueueEventAsync("device.fs.status", new { status = "success", messageKey = "设备项目已重命名" });
        await ListDeviceFilesAsync(parent);
    }

    private async Task CreateDeviceDirectoryAsync(string path, string? parent)
    {
        await QueueEventAsync("device.fs.status", new { status = "working", messageKey = "正在创建设备文件夹" });
        await RequireDeviceApi().CreateDirectoryAsync(RequireSelectedDeviceIp(), path, _lifetime.Token);
        await QueueEventAsync("device.fs.status", new { status = "success", messageKey = "设备文件夹已创建" });
        await ListDeviceFilesAsync(parent);
    }

    private async Task PasteDevicePathAsync(
        string sourcePath,
        string destinationPath,
        bool isDirectory,
        bool move,
        string? parent)
    {
        await QueueEventAsync("device.fs.status", new
        {
            status = "working",
            messageKey = move ? "正在移动设备项目" : "正在复制设备项目",
        });
        if (move)
        {
            await RequireDeviceApi().RenamePathAsync(RequireSelectedDeviceIp(), sourcePath, destinationPath, _lifetime.Token);
        }
        else
        {
            await RequireDeviceApi().CopyPathAsync(RequireSelectedDeviceIp(), sourcePath, destinationPath, isDirectory, _lifetime.Token);
        }
        await QueueEventAsync("device.fs.status", new
        {
            status = "success",
            messageKey = move ? "设备项目已移动" : "设备项目已复制",
        });
        await ListDeviceFilesAsync(parent);
    }

    private async Task RunDeviceSpeedTestAsync(BridgeCommand command)
    {
        var requestedIps = GetStringList(command, "ips")
            .Where(ip => IPAddress.TryParse(ip, out _))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (requestedIps.Length == 0) requestedIps = [RequireSelectedDeviceIp()];

        var transport = (GetString(command, "transport") ?? "both").Trim().ToLowerInvariant();
        var layout = (GetString(command, "layout") ?? "both").Trim().ToLowerInvariant();
        var direction = (GetString(command, "direction") ?? "both").Trim().ToLowerInvariant();
        var baseDirectory = NormalizeDeviceDirectory(GetString(command, "path") ?? "/sd");
        var rounds = Math.Clamp(GetInt(command, "rounds", 2), 1, 20);
        var continuousKb = Math.Clamp(GetInt(command, "continuousKb", 1024), 1, 64 * 1024);
        var fragmentKb = Math.Clamp(GetInt(command, "fragmentKb", 4), 1, 4096);
        var fragmentCount = Math.Clamp(GetInt(command, "fragmentCount", 64), 1, 2000);
        var devtoolsChunkKb = Math.Clamp(GetInt(command, "devtoolsChunkKb", 64), 4, 256);
        var maxParallel = Math.Clamp(GetInt(command, "parallel", Math.Min(4, requestedIps.Length)), 1, 8);
        var transports = transport switch
        {
            "fs" => new[] { "fs" },
            "devtools" => new[] { "devtools" },
            "ram" => new[] { "ram" },
            _ => new[] { "fs", "devtools" },
        };
        var layouts = layout switch
        {
            "continuous" => new[] { "continuous" },
            "fragments" => new[] { "fragments" },
            _ => new[] { "continuous", "fragments" },
        };
        var directions = direction switch
        {
            "upload" => new[] { "upload" },
            "download" => new[] { "download" },
            _ => new[] { "upload", "download" },
        };

        var tempRoot = Path.Combine(Path.GetTempPath(), "clocteck-pcapp-speed", DateTimeOffset.Now.ToString("yyyyMMddHHmmssfff"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            var filesByLayout = CreateSpeedTestFiles(tempRoot, baseDirectory, layouts, continuousKb, fragmentKb, fragmentCount);
            var totalScenarios = requestedIps.Length * transports.Length * layouts.Length * directions.Length * rounds;
            var completedScenarios = 0;
            var progressLock = new object();

            await QueueEventAsync("device.speed.status", new
            {
                status = "working",
                reset = true,
                progress = 0,
                completed = 0,
                total = totalScenarios,
                activeDevices = requestedIps.Length,
                message = $"开始测速：{requestedIps.Length} 台设备，并发 {maxParallel}",
            });

            using var semaphore = new SemaphoreSlim(maxParallel, maxParallel);
            var tasks = requestedIps.Select(async ip =>
            {
                await semaphore.WaitAsync(_lifetime.Token);
                try
                {
                    await RunDeviceSpeedTestForDeviceAsync(
                        ip,
                        baseDirectory,
                        transports,
                        layouts,
                        directions,
                        rounds,
                        devtoolsChunkKb * 1024,
                        filesByLayout,
                        () =>
                        {
                            int completed;
                            lock (progressLock) completed = ++completedScenarios;
                            return QueueEventAsync("device.speed.status", new
                            {
                                status = "working",
                                progress = Math.Round(completed * 100d / Math.Max(1, totalScenarios), 1),
                                completed,
                                total = totalScenarios,
                                message = $"测速进度 {completed}/{totalScenarios}",
                            });
                        },
                        _lifetime.Token);
                }
                catch (Exception error)
                {
                    await QueueEventAsync("device.speed.result", new
                    {
                        ip,
                        error = error.Message,
                        time = DateTimeOffset.Now,
                    });
                    _log.Warn("网速测试", $"{ip} 测速失败：{error.Message}");
                }
                finally
                {
                    semaphore.Release();
                }
            }).ToArray();

            await Task.WhenAll(tasks);
            await QueueEventAsync("device.speed.status", new
            {
                status = "success",
                done = true,
                progress = 100,
                completed = completedScenarios,
                total = totalScenarios,
                message = $"网速测试完成：{requestedIps.Length} 台设备",
            });
            _log.Info("网速测试", $"多设备测速完成：{string.Join(", ", requestedIps)}");
        }
        finally
        {
            try { Directory.Delete(tempRoot, true); } catch { }
        }
    }

    private async Task RunDeviceSpeedTestForDeviceAsync(
        string ip,
        string baseDirectory,
        IReadOnlyList<string> transports,
        IReadOnlyList<string> layouts,
        IReadOnlyList<string> directions,
        int rounds,
        int devtoolsChunkBytes,
        IReadOnlyDictionary<string, List<SpeedTestFile>> filesByLayout,
        Func<Task> scenarioCompletedAsync,
        CancellationToken cancellationToken)
    {
        var api = RequireDeviceApi();
        var mode = "";
        int? rssi = null;
        try
        {
            var state = await api.GetStateAsync(ip, cancellationToken);
            if (state.TryGetProperty("wifi", out var wifi) && wifi.ValueKind == JsonValueKind.Object)
            {
                if (wifi.TryGetProperty("mode", out var modeNode)) mode = modeNode.GetString() ?? "";
                if (wifi.TryGetProperty("sta_rssi", out var rssiNode) && rssiNode.TryGetInt32(out var parsedRssi)) rssi = parsedRssi;
            }
        }
        catch { }

        if (!baseDirectory.Equals("/sd", StringComparison.OrdinalIgnoreCase))
        {
            try { await api.CreateDirectoryAsync(ip, baseDirectory, cancellationToken); } catch { }
        }

        if (directions.Contains("download") && !directions.Contains("upload") && transports.Any(item => item != "ram"))
        {
            foreach (var selectedLayout in layouts)
            {
                await EnsureSpeedFilesOnDeviceAsync(ip, filesByLayout[selectedLayout], cancellationToken);
            }
        }

        foreach (var selectedTransport in transports)
        {
            foreach (var selectedLayout in layouts)
            {
                var files = filesByLayout[selectedLayout];
                foreach (var selectedDirection in directions)
                {
                    for (var round = 1; round <= rounds; round++)
                    {
                        await QueueEventAsync("device.speed.status", new
                        {
                            status = "working",
                            phase = selectedDirection,
                            ip,
                            transport = selectedTransport,
                            layout = selectedLayout,
                            direction = selectedDirection,
                            round,
                            rounds,
                            message = $"{ip} {SpeedDirectionLabel(selectedDirection)} {SpeedTransportLabel(selectedTransport)} / {SpeedLayoutLabel(selectedLayout)} {round}/{rounds}",
                        });
                        var result = selectedTransport == "ram"
                            ? await RunRamSpeedScenarioAsync(ip, selectedDirection, files, devtoolsChunkBytes, cancellationToken)
                            : selectedDirection == "upload"
                                ? await UploadSpeedScenarioAsync(ip, selectedTransport, files, devtoolsChunkBytes, cancellationToken)
                                : await DownloadSpeedScenarioAsync(ip, selectedTransport, files, devtoolsChunkBytes, cancellationToken);
                        await QueueEventAsync("device.speed.result", new
                        {
                            ip,
                            mode,
                            rssi,
                            direction = selectedDirection,
                            directionLabel = SpeedDirectionLabel(selectedDirection),
                            transport = selectedTransport,
                            transportLabel = SpeedTransportLabel(selectedTransport),
                            layout = selectedLayout,
                            layoutLabel = SpeedLayoutLabel(selectedLayout),
                            round,
                            rounds,
                            fileCount = files.Count,
                            bytes = result.Bytes,
                            milliseconds = Math.Round(result.Elapsed.TotalMilliseconds, 1),
                            kbps = Math.Round(result.Bytes / 1024d / Math.Max(0.001, result.Elapsed.TotalSeconds), 1),
                            time = DateTimeOffset.Now,
                        });
                        await scenarioCompletedAsync();
                    }
                }
            }
        }
    }

    private async Task EnsureSpeedFilesOnDeviceAsync(
        string ip,
        IReadOnlyList<SpeedTestFile> files,
        CancellationToken cancellationToken)
    {
        foreach (var item in files)
        {
            await RequireDeviceApi().UploadLocalFileAsync(ip, item.DevicePath, item.LocalPath, cancellationToken);
        }
    }

    private async Task<(long Bytes, TimeSpan Elapsed)> RunRamSpeedScenarioAsync(
        string ip,
        string direction,
        IReadOnlyList<SpeedTestFile> files,
        int chunkBytes,
        CancellationToken cancellationToken)
    {
        var totalBytes = files.Sum(item => (long)item.Size);
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var api = RequireDeviceApi();
        var transferred = direction == "upload"
            ? await api.UploadRamBenchmarkAsync(ip, checked((int)totalBytes), chunkBytes, cancellationToken)
            : await api.DownloadRamBenchmarkAsync(ip, checked((int)totalBytes), chunkBytes, cancellationToken);
        stopwatch.Stop();
        return (transferred, stopwatch.Elapsed);
    }

    private async Task<(long Bytes, TimeSpan Elapsed)> UploadSpeedScenarioAsync(
        string ip,
        string transport,
        IReadOnlyList<SpeedTestFile> files,
        int devtoolsChunkBytes,
        CancellationToken cancellationToken)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        long total = 0;
        foreach (var item in files)
        {
            if (transport == "devtools")
            {
                await RequireDeviceApi().UploadLocalFileViaDevToolsAsync(ip, item.DevicePath, item.LocalPath, null, cancellationToken);
            }
            else
            {
                await RequireDeviceApi().UploadLocalFileAsync(ip, item.DevicePath, item.LocalPath, cancellationToken);
            }
            total += item.Size;
        }
        stopwatch.Stop();
        return (total, stopwatch.Elapsed);
    }

    private async Task<(long Bytes, TimeSpan Elapsed)> DownloadSpeedScenarioAsync(
        string ip,
        string transport,
        IReadOnlyList<SpeedTestFile> files,
        int devtoolsChunkBytes,
        CancellationToken cancellationToken)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        long total = 0;
        foreach (var item in files)
        {
            var bytes = transport == "devtools"
                ? await RequireDeviceApi().ReadFileViaDevToolsAsync(ip, item.DevicePath, item.Size, devtoolsChunkBytes, cancellationToken)
                : await RequireDeviceApi().ReadFileAsync(ip, item.DevicePath, item.Size, cancellationToken);
            if (bytes.Length != item.Size)
            {
                throw new InvalidOperationException($"{SpeedTransportLabel(transport)} 设备传电脑大小不一致：{item.DevicePath}，期望 {item.Size}，实际 {bytes.Length}");
            }
            total += bytes.Length;
        }
        stopwatch.Stop();
        return (total, stopwatch.Elapsed);
    }

    private static Dictionary<string, List<SpeedTestFile>> CreateSpeedTestFiles(
        string tempRoot,
        string baseDirectory,
        IReadOnlyList<string> layouts,
        int continuousKb,
        int fragmentKb,
        int fragmentCount)
    {
        var result = new Dictionary<string, List<SpeedTestFile>>(StringComparer.OrdinalIgnoreCase);
        var prefix = baseDirectory.TrimEnd('/') + "/pcapp_speed_";
        if (layouts.Contains("continuous"))
        {
            var size = checked(continuousKb * 1024);
            var local = Path.Combine(tempRoot, $"continuous_{continuousKb}k.bin");
            WritePatternFile(local, size, 17);
            result["continuous"] = [new SpeedTestFile(local, $"{prefix}continuous_{continuousKb}k.bin", size)];
        }
        if (layouts.Contains("fragments"))
        {
            var size = checked(fragmentKb * 1024);
            var files = new List<SpeedTestFile>(fragmentCount);
            var fragmentRoot = Path.Combine(tempRoot, "fragments");
            Directory.CreateDirectory(fragmentRoot);
            for (var index = 0; index < fragmentCount; index++)
            {
                var name = $"fragment_{index + 1:D4}_{fragmentKb}k.bin";
                var local = Path.Combine(fragmentRoot, name);
                WritePatternFile(local, size, index + 31);
                files.Add(new SpeedTestFile(local, $"{prefix}{name}", size));
            }
            result["fragments"] = files;
        }
        return result;
    }

    private static void WritePatternFile(string path, int size, int seed)
    {
        var bytes = new byte[size];
        for (var index = 0; index < bytes.Length; index++)
        {
            bytes[index] = (byte)((index * 31 + seed) % 251);
        }
        File.WriteAllBytes(path, bytes);
    }

    private static string SpeedTransportLabel(string transport) =>
        transport.Equals("devtools", StringComparison.OrdinalIgnoreCase) ? "DevTools" :
        transport.Equals("ram", StringComparison.OrdinalIgnoreCase) ? "RAM 内存" : "FS";

    private static string SpeedLayoutLabel(string layout) =>
        layout.Equals("fragments", StringComparison.OrdinalIgnoreCase) ? "碎片文件" : "连续文件";

    private static string SpeedDirectionLabel(string direction) =>
        direction.Equals("upload", StringComparison.OrdinalIgnoreCase) ? "电脑传设备" : "设备传电脑";

    private async Task RunDeviceLatencyTestAsync(BridgeCommand command)
    {
        var requestedIps = GetStringList(command, "ips")
            .Where(ip => IPAddress.TryParse(ip, out _))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (requestedIps.Length == 0) requestedIps = [RequireSelectedDeviceIp()];

        var count = Math.Clamp(GetInt(command, "count", 10), 1, 100);
        var maxParallel = Math.Clamp(GetInt(command, "parallel", Math.Min(4, requestedIps.Length)), 1, 8);
        var totalSamples = requestedIps.Length * count;
        var completedSamples = 0;
        var progressLock = new object();

        await QueueEventAsync("device.speed.status", new
        {
            status = "working",
            reset = true,
            progress = 0,
            completed = 0,
            total = totalSamples,
            activeDevices = requestedIps.Length,
            message = $"开始延迟测试：{requestedIps.Length} 台设备，每台 {count} 次，并发 {maxParallel}",
        });

        Func<Task> sampleCompletedAsync = () =>
        {
            int completed;
            lock (progressLock) completed = ++completedSamples;
            return QueueEventAsync("device.speed.status", new
            {
                status = "working",
                progress = Math.Round(completed * 100d / Math.Max(1, totalSamples), 1),
                completed,
                total = totalSamples,
                message = $"延迟测试进度 {completed}/{totalSamples}",
            });
        };

        using var semaphore = new SemaphoreSlim(maxParallel, maxParallel);
        var tasks = requestedIps.Select(async ip =>
        {
            await semaphore.WaitAsync(_lifetime.Token);
            try
            {
                await RunLatencyTestForDeviceAsync(ip, count, sampleCompletedAsync, _lifetime.Token);
            }
            catch (Exception error)
            {
                await QueueEventAsync("device.speed.result", new
                {
                    category = "latency",
                    ip,
                    direction = "latency",
                    directionLabel = "延迟测试",
                    transport = "api",
                    transportLabel = "System API",
                    layout = "state",
                    layoutLabel = "/api/system/state",
                    samples = count,
                    failed = count,
                    failureRate = 100,
                    error = error.Message,
                    time = DateTimeOffset.Now,
                });
                _log.Warn("延迟测试", $"{ip} 延迟测试失败：{error.Message}");
            }
            finally
            {
                semaphore.Release();
            }
        }).ToArray();

        await Task.WhenAll(tasks);
        await QueueEventAsync("device.speed.status", new
        {
            status = "success",
            done = true,
            progress = 100,
            completed = completedSamples,
            total = totalSamples,
            message = $"延迟测试完成：{requestedIps.Length} 台设备",
        });
    }

    private async Task RunLatencyTestForDeviceAsync(
        string ip,
        int count,
        Func<Task> sampleCompletedAsync,
        CancellationToken cancellationToken)
    {
        var api = RequireDeviceApi();
        var samples = new List<double>(count);
        var failed = 0;
        string? lastError = null;
        var mode = "";
        int? rssi = null;

        for (var index = 1; index <= count; index++)
        {
            await QueueEventAsync("device.speed.status", new
            {
                status = "working",
                phase = "latency",
                ip,
                direction = "latency",
                round = index,
                rounds = count,
                message = $"{ip} 延迟测试 {index}/{count}",
            });

            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(3));
                var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                var state = await api.GetStateAsync(ip, timeout.Token);
                stopwatch.Stop();
                samples.Add(stopwatch.Elapsed.TotalMilliseconds);
                if (state.TryGetProperty("wifi", out var wifi) && wifi.ValueKind == JsonValueKind.Object)
                {
                    if (wifi.TryGetProperty("mode", out var modeNode)) mode = modeNode.GetString() ?? "";
                    if (wifi.TryGetProperty("sta_rssi", out var rssiNode) && rssiNode.TryGetInt32(out var parsedRssi)) rssi = parsedRssi;
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                failed++;
                lastError = "请求超时";
            }
            catch (Exception error)
            {
                failed++;
                lastError = error.Message;
            }

            await sampleCompletedAsync();
        }

        if (samples.Count == 0)
        {
            await QueueEventAsync("device.speed.result", new
            {
                category = "latency",
                ip,
                direction = "latency",
                directionLabel = "延迟测试",
                transport = "api",
                transportLabel = "System API",
                layout = "state",
                layoutLabel = "/api/system/state",
                round = count,
                rounds = count,
                samples = count,
                okCount = 0,
                failed,
                failureRate = 100,
                milliseconds = 0,
                error = lastError ?? "全部请求失败",
                time = DateTimeOffset.Now,
            });
            return;
        }

        var ordered = samples.OrderBy(value => value).ToArray();
        var p95Index = Math.Clamp((int)Math.Ceiling(ordered.Length * 0.95d) - 1, 0, ordered.Length - 1);
        var jitter = samples.Count > 1
            ? samples.Zip(samples.Skip(1), (left, right) => Math.Abs(right - left)).Average()
            : 0;

        await QueueEventAsync("device.speed.result", new
        {
            category = "latency",
            ip,
            mode,
            rssi,
            direction = "latency",
            directionLabel = "延迟测试",
            transport = "api",
            transportLabel = "System API",
            layout = "state",
            layoutLabel = "/api/system/state",
            round = count,
            rounds = count,
            samples = count,
            okCount = samples.Count,
            failed,
            failureRate = Math.Round(failed * 100d / Math.Max(1, count), 1),
            minMs = Math.Round(samples.Min(), 1),
            avgMs = Math.Round(samples.Average(), 1),
            p95Ms = Math.Round(ordered[p95Index], 1),
            maxMs = Math.Round(samples.Max(), 1),
            jitterMs = Math.Round(jitter, 1),
            milliseconds = Math.Round(samples.Average(), 1),
            time = DateTimeOffset.Now,
        });
    }

    private async Task ReadLuaCodeAsync(string? requestedPath)
    {
        var path = NormalizeLuaPath(requestedPath);
        var bytes = await RequireDeviceApi().ReadFileAsync(RequireSelectedDeviceIp(), path, 1024 * 1024, _lifetime.Token);
        var code = new System.Text.UTF8Encoding(false, false).GetString(bytes);
        await QueueEventAsync("device.lua.code", new { path, code });
    }

    private async Task SaveLuaCodeAsync(string? requestedPath, string code, bool run)
    {
        var path = NormalizeLuaPath(requestedPath);
        var bytes = System.Text.Encoding.UTF8.GetBytes(code);
        if (bytes.Length > 1024 * 1024) throw new InvalidOperationException("Lua 文件超过 1 MB 保存上限。");
        await RequireDeviceApi().UploadFileAsync(RequireSelectedDeviceIp(), path, bytes, "text/plain; charset=utf-8", _lifetime.Token);
        if (run)
        {
            await RequireDeviceApi().LaunchAppAsync(RequireSelectedDeviceIp(), "devrun", _lifetime.Token);
        }
        await QueueEventAsync("device.lua.saved", new { path, run });
    }

    private async Task ConnectSerialAsync(string port, int baudRate)
    {
        var snapshot = RequireSerial().Connect(port, baudRate);
        _log.Info("串口", $"已连接 {port} @ {baudRate}");
        await QueueEventAsync("serial.status", snapshot);
    }

    private async Task DisconnectSerialAsync()
    {
        _wifiSerialReadySignal?.TrySetCanceled();
        _wifiSerialReadySignal = null;
        _wifiSerialScanRequestId = null;
        _wifiSerialProvisionRequestId = null;
        var snapshot = RequireSerial().Disconnect();
        _log.Info("串口", "串口已断开");
        await QueueEventAsync("serial.status", snapshot);
    }

    private async Task EnsureWifiGuideSerialReadyAsync()
    {
        await _wifiSerialHandshakeLock.WaitAsync(_lifetime.Token);
        try
        {
            var serial = RequireSerial();
            if (!serial.Snapshot().Connected)
            {
                throw new InvalidOperationException("请先连接设备串口。");
            }

            var ready = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _wifiSerialReadySignal = ready;
            var handshakeId = Guid.NewGuid().ToString("N");
            await QueueEventAsync("wifi.serial.status", new
            {
                status = "waiting",
                message = "串口已连接；若设备未响应，请操作设备打开 WiFi Setting Guide App",
            });

            var connection = serial.Snapshot();
            var portName = connection.ConnectedPort;
            var baudRate = connection.BaudRate;
            if (connection.ConnectedAt is { } connectedAt)
            {
                var settleDelay = TimeSpan.FromMilliseconds(2500) - (DateTimeOffset.Now - connectedAt);
                if (settleDelay > TimeSpan.Zero)
                {
                    await QueueEventAsync("wifi.serial.status", new
                    {
                        status = "waiting",
                        message = "正在等待设备串口稳定并重新就绪",
                    });
                    await Task.Delay(settleDelay, _lifetime.Token);
                }
            }

            for (var attempt = 1; attempt <= 30; attempt++)
            {
                try
                {
                    serial.SendLine("@CUBIC_WIFI/1 " + JsonSerializer.Serialize(new
                    {
                        cmd = "hello",
                        id = handshakeId,
                    }));
                }
                catch (Exception error) when (!string.IsNullOrWhiteSpace(portName))
                {
                    _log.Warn("串口配网", $"握手发送失败，等待 USB 串口恢复：{error.Message}");
                    await QueueEventAsync("wifi.serial.status", new
                    {
                        status = "waiting",
                        message = "设备串口正在重新连接，请稍候",
                    });
                    if (await TryRestoreWifiSerialAsync(serial, portName, baudRate))
                    {
                        await Task.Delay(2500, _lifetime.Token);
                    }
                }
                var completed = await Task.WhenAny(
                    ready.Task,
                    Task.Delay(800, _lifetime.Token));
                if (completed == ready.Task && await ready.Task)
                {
                    _log.Info("串口配网", $"设备串口已就绪（握手尝试 {attempt} 次）");
                    return;
                }
            }

            throw new TimeoutException("串口连接后设备未响应，请操作设备打开 WiFi Setting Guide App。");
        }
        finally
        {
            _wifiSerialReadySignal = null;
            _wifiSerialHandshakeLock.Release();
        }
    }

    private async Task<bool> TryRestoreWifiSerialAsync(SerialMonitorService serial, string portName, int baudRate)
    {
        for (var attempt = 1; attempt <= 20; attempt++)
        {
            _lifetime.Token.ThrowIfCancellationRequested();
            if (serial.Snapshot().Ports.Contains(portName, StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    serial.Reconnect(portName, baudRate);
                    _log.Info("串口配网", $"USB 串口 {portName} 已恢复");
                    return true;
                }
                catch (Exception error) when (attempt < 20)
                {
                    _log.Warn("串口配网", $"USB 串口恢复尝试 {attempt} 失败：{error.Message}");
                }
            }
            await Task.Delay(300, _lifetime.Token);
        }
        return false;
    }

    private static string NormalizeDeviceDirectory(string? path)
    {
        path = string.IsNullOrWhiteSpace(path) ? "/sd" : path.Trim().Replace('\\', '/');
        if (!path.Equals("/sd", StringComparison.Ordinal) && !path.StartsWith("/sd/", StringComparison.Ordinal))
        {
            throw new ArgumentException("设备目录必须位于 /sd。");
        }
        if (path.Split('/', StringSplitOptions.RemoveEmptyEntries).Any(part => part is "." or ".."))
        {
            throw new ArgumentException("设备目录不能包含相对路径。");
        }
        return path.TrimEnd('/') is { Length: > 0 } normalized ? normalized : "/sd";
    }

    private static string NormalizeLuaPath(string? path)
    {
        path = string.IsNullOrWhiteSpace(path) ? "/sd/apps/devrun/main.lua" : path.Trim().Replace('\\', '/');
        if (!path.StartsWith("/sd/", StringComparison.Ordinal)) throw new ArgumentException("Lua 文件必须位于 /sd。");
        if (!path.EndsWith(".lua", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Lua 编辑器只能保存 .lua 文件。");
        var separator = path.LastIndexOf('/');
        _ = NormalizeDeviceDirectory(path[..separator]);
        return path;
    }

    private string RequireSelectedDeviceIp() => SelectedDeviceIp ??
        throw new InvalidOperationException("请先选择或连接一台设备。");

    private DeviceApiClient RequireDeviceApi() => _deviceApi ??
        throw new InvalidOperationException("设备接口尚未初始化。");

    private StoreServerClient RequireStoreServer() => _storeServer ??
        throw new InvalidOperationException("应用商店客户端尚未初始化。");

    private GitHubStoreInstaller RequireGitHubStoreInstaller() => _githubStoreInstaller ??
        throw new InvalidOperationException("GitHub 应用安装器尚未初始化。");

    private SerialMonitorService RequireSerial() => _serial ??
        throw new InvalidOperationException("串口服务尚未初始化。");

    private Task SendDeviceListAsync() => QueueEventAsync("device.list", new
    {
        devices = _settings.Devices.OrderByDescending(device => device.LastSeen),
        selectedDeviceIp = SelectedDeviceIp,
    });

    private string? SelectedDeviceIp => _settings.SelectedDeviceIp ?? _settings.LastDeviceIp;

    private static void UpdateDeviceRuntime(SavedDevice saved, JsonElement state)
    {
        saved.CurrentAppId = null;
        saved.CurrentAppName = null;
        saved.WifiRssi = null;
        if (state.TryGetProperty("wifi", out var wifi) && wifi.ValueKind == JsonValueKind.Object &&
            wifi.TryGetProperty("sta_rssi", out var rssi) && rssi.TryGetInt32(out var value))
        {
            saved.WifiRssi = value;
        }
        if (!state.TryGetProperty("current_app", out var current) || current.ValueKind != JsonValueKind.Object) return;
        if (current.TryGetProperty("id", out var id)) saved.CurrentAppId = id.GetString();
        if (current.TryGetProperty("name", out var name)) saved.CurrentAppName = name.GetString();
        saved.CurrentAppName ??= saved.CurrentAppId;
    }

    private async Task ConfigureWorkerAsync(string id)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "选择上位机启动文件",
            Filter = "支持的程序|*.exe;*.cmd;*.bat;*.py;*.js|所有文件|*.*",
            CheckFileExists = true,
        };
        if (dialog.ShowDialog() == true) await RequireServices().ConfigureAsync(id, dialog.FileName);
    }

    private async Task StatusLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                WifiConnection? wifiConnection = null;
                if (_wifi is not null) wifiConnection = await _wifi.GetCurrentConnectionAsync(cancellationToken);
                var network = ComputerNetworkService.Resolve(wifiConnection, SelectedDeviceIp);
                await QueueEventAsync("system.status", new { wifi = network, stats = _stats.GetSnapshot(), time = DateTimeOffset.Now });
                await Task.Delay(2000, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception error)
            {
                _log.Warn("状态", error.Message);
                await Task.Delay(3000, cancellationToken);
            }
        }
    }

    private async Task SerialStatusLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                _serial?.Refresh();
                await Task.Delay(1000, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception error)
            {
                _log.Warn("串口", "刷新串口状态失败：" + error.Message);
                await Task.Delay(1000, cancellationToken);
            }
        }
    }

    private Task SaveSettingsAsync() => _store.SaveAsync(_settings);

    private ManagedServiceManager RequireServices() => _services ?? throw new InvalidOperationException("服务管理器尚未初始化。");

    private static string RequireString(BridgeCommand command, string name) => GetString(command, name)
        ?? throw new ArgumentException($"缺少参数 {name}");

    private static string? GetString(BridgeCommand command, string name)
    {
        if (!command.Payload.TryGetValue(name, out var raw) || raw is null) return null;
        return raw is JsonElement element ? element.ValueKind == JsonValueKind.String ? element.GetString() : element.ToString() : raw.ToString();
    }

    private static IReadOnlyList<string> GetStringList(BridgeCommand command, string name)
    {
        if (!command.Payload.TryGetValue(name, out var raw) || raw is null) return [];
        if (raw is JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Array)
            {
                var items = new List<string>();
                foreach (var item in element.EnumerateArray())
                {
                    var value = item.ValueKind == JsonValueKind.String ? item.GetString() : item.ToString();
                    if (!string.IsNullOrWhiteSpace(value)) items.Add(value.Trim());
                }
                return items;
            }
            var single = element.ValueKind == JsonValueKind.String ? element.GetString() : element.ToString();
            return string.IsNullOrWhiteSpace(single) ? [] : [single.Trim()];
        }
        if (raw is IEnumerable<object?> rawValues && raw is not string)
        {
            return rawValues
                .Select(item => item?.ToString())
                .Where(text => !string.IsNullOrWhiteSpace(text))
                .Select(text => text!.Trim())
                .ToArray();
        }
        var text = raw.ToString();
        return string.IsNullOrWhiteSpace(text) ? [] : [text.Trim()];
    }

    private static bool GetBool(BridgeCommand command, string name)
    {
        if (!command.Payload.TryGetValue(name, out var raw) || raw is null) return false;
        if (raw is JsonElement element) return element.ValueKind == JsonValueKind.True || (element.ValueKind == JsonValueKind.String && bool.TryParse(element.GetString(), out var parsed) && parsed);
        return Convert.ToBoolean(raw);
    }

    private static int GetInt(BridgeCommand command, string name, int fallback)
    {
        if (!command.Payload.TryGetValue(name, out var raw) || raw is null) return fallback;
        if (raw is JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out var number)) return number;
            return int.TryParse(element.ToString(), out number) ? number : fallback;
        }
        return int.TryParse(raw.ToString(), out var parsed) ? parsed : fallback;
    }

    private void QueueEvent(string type, object? payload) => _ = QueueEventAsync(type, payload);

    private Task QueueEventAsync(string type, object? payload) => SendEventAsync?.Invoke(type, payload) ?? Task.CompletedTask;

    private sealed record SpeedTestFile(string LocalPath, string DevicePath, int Size);

    public async ValueTask DisposeAsync()
    {
        _lifetime.Cancel();
        if (_statusTask is not null)
        {
            try { await _statusTask; } catch (OperationCanceledException) { }
        }
        if (_deviceAppServiceTask is not null)
        {
            try { await _deviceAppServiceTask; } catch (OperationCanceledException) { }
        }
        if (_serialStatusTask is not null)
        {
            try { await _serialStatusTask; } catch (OperationCanceledException) { }
        }
        if (_services is not null) await _services.DisposeAsync();
        _serial?.Dispose();
        _deviceApi?.Dispose();
        _githubStoreInstaller?.Dispose();
        _storeServer?.Dispose();
        _wifi?.Dispose();
        _holoMonitorConfigLock.Dispose();
        _lifetime.Dispose();
    }

    private static readonly Dictionary<string, string> ImageMimeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".gif"] = "image/gif",
        [".bmp"] = "image/bmp",
        [".webp"] = "image/webp",
    };

    private static readonly HashSet<string> TextFileExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".lua", ".txt", ".lrc", ".json", ".html", ".css", ".js", ".md", ".xml", ".csv", ".log", ".conf", ".ini",
    };

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
}
