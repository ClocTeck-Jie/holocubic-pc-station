using System.Text.Json.Serialization;

namespace Clocteck.CubicCenter.Models;

public sealed class AppSettings
{
    public List<string> DeviceApAliases { get; set; } = ["clocteck-cubic", "clocteck_cubic"];
    public string? LastDeviceIp { get; set; }
    public string? SelectedDeviceIp { get; set; }
    public List<SavedDevice> Devices { get; set; } = [];
    public bool CloseToTray { get; set; } = true;
    public List<WorkerSettings> Workers { get; set; } = WorkerSettings.CreateDefaults();
    public DesktopMirrorSettings DesktopMirror { get; set; } = new();
}

public sealed class DesktopMirrorSettings
{
    public string Source { get; set; } = "screen";
    public int Monitor { get; set; } = 1;
    public string MonitorResolution { get; set; } = "";
    public string Region { get; set; } = "";
    public string Fit { get; set; } = "stretch";
    public int Fps { get; set; } = 8;
    public int Quality { get; set; } = 65;
}

public sealed class SavedDevice
{
    public required string IpAddress { get; set; }
    public string Name { get; set; } = "Clocteck Cubic";
    public string? DeviceId { get; set; }
    public DateTimeOffset LastSeen { get; set; } = DateTimeOffset.Now;
    public bool Online { get; set; }
    public string? CurrentAppId { get; set; }
    public string? CurrentAppName { get; set; }
    public int? WifiRssi { get; set; }
}

public sealed class WorkerSettings
{
    public required string Id { get; set; }
    public required string Name { get; set; }
    public required string Description { get; set; }
    public string Executable { get; set; } = string.Empty;
    public string Arguments { get; set; } = string.Empty;
    public string WorkingDirectory { get; set; } = string.Empty;
    public int Port { get; set; }
    public string HealthPath { get; set; } = string.Empty;
    public bool AutoStart { get; set; }
    public bool BuiltIn { get; set; }

    public static List<WorkerSettings> CreateDefaults() =>
    [
        new() { Id = "codex-core", Name = "Codex Buddy", Description = "读取 Codex 状态并提供 8788 兼容接口", Port = 8788, HealthPath = "/health", AutoStart = false, BuiltIn = true },
        new() { Id = "pc-monitor", Name = "电脑监控", Description = "提供 CPU、内存和网络状态接口", Port = 17322, HealthPath = "/health", AutoStart = false, BuiltIn = true },
        new() { Id = "desktop-mirror", Name = "桌面投屏", Description = "管理 320×240 WebSocket 投屏程序", Port = 8787, AutoStart = false },
        new() { Id = "holopet", Name = "Holopet", Description = "接收 Codex Hook并提供 17321 SSE接口", Port = 17321, HealthPath = "/health", AutoStart = false, BuiltIn = true },
        new() { Id = "smtc-music", Name = "SMTC Music", Description = "读取 Windows 媒体状态并提供歌词、封面与播放控制", Port = 17865, HealthPath = "/health", AutoStart = false },
    ];
}

public sealed record WifiNetwork(
    string Ssid,
    int SignalQuality,
    bool SecurityEnabled,
    bool HasProfile,
    Guid InterfaceId,
    string InterfaceName);

public sealed record WifiConnection(
    string Ssid,
    string ProfileName,
    int SignalQuality,
    string Bssid,
    Guid InterfaceId,
    string InterfaceName,
    string? Ipv4Address,
    string? Gateway);

public sealed record ComputerNetworkConnection(
    string DisplayName,
    string ConnectionType,
    string InterfaceName,
    string? Ssid,
    int? SignalQuality,
    string? Ipv4Address,
    string? Gateway);

public sealed record DeviceInfo(
    string Hostname,
    string IpAddress,
    string ControlUrl,
    string? DeviceId,
    string? RawState);

public sealed record ProvisioningSnapshot(
    string Stage,
    string Message,
    int Progress,
    string? PreviousSsid = null,
    string? DeviceSsid = null,
    string? SetupUrl = null,
    string? ControlUrl = null,
    bool CanCancel = false,
    bool CanForceComplete = false);

public sealed record WorkerSnapshot(
    string Id,
    string Name,
    string Description,
    string Status,
    int Port,
    int? ProcessId,
    bool AutoStart,
    bool BuiltIn,
    bool Configured,
    string Executable,
    string Message);

public sealed record SystemStats(
    double CpuPercent,
    ulong MemoryUsedBytes,
    ulong MemoryTotalBytes,
    long NetworkReceivedBytes,
    long NetworkSentBytes,
    long UptimeSeconds,
    DateTimeOffset Timestamp);

public sealed record HardwareSensorSnapshot(
    bool Available,
    bool Elevated,
    string Status,
    string? CpuName,
    string? GpuName,
    double? CpuClockMhz,
    double? CpuTemperatureC,
    double? CpuVoltageV,
    double? CpuPackagePowerW,
    double? CpuFanRpm,
    double? GpuUsagePercent,
    double? GpuClockMhz,
    double? GpuTemperatureC,
    double? GpuFanRpm,
    int SensorCount,
    DateTimeOffset Timestamp);

public sealed record HoloMonitorConfigResult(
    bool Ok,
    string Message,
    string? DeviceIp,
    string? ComputerIp,
    int Port,
    string Path,
    string? DataUrl = null);

public sealed record HolopetConfigResult(
    bool Ok,
    string Message,
    string? DeviceIp,
    string? ComputerIp,
    int Port,
    string Route,
    string? EventUrl = null);

public sealed record SmtcMusicConfigResult(
    bool Ok,
    string Message,
    string? DeviceIp,
    string? ComputerIp,
    int Port,
    string Route,
    string? BridgeUrl = null);

public sealed record LogEntry(DateTimeOffset Time, string Level, string Source, string Message);

public sealed class BridgeCommand
{
    [JsonPropertyName("action")]
    public string Action { get; set; } = string.Empty;

    [JsonPropertyName("payload")]
    public Dictionary<string, object?> Payload { get; set; } = [];
}
