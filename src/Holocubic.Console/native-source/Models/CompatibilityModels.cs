namespace Clocteck.CubicCenter.Models;

public sealed record PawnIoStatus(
    bool Installed,
    string Status,
    string? Version,
    string? DriverPath,
    DateTimeOffset CheckedAt);

public sealed record HardwareCompatibilityStatus(
    bool MainProcessElevated,
    bool ElevatedHostEnabled,
    bool ElevatedHostAvailable,
    string ElevatedHostStatus,
    PawnIoStatus PawnIo,
    bool HwInfoEnabled,
    bool HwInfoAvailable,
    string HwInfoStatus,
    DateTimeOffset Timestamp);

public sealed record HardwareDiagnosticBundle(
    DateTimeOffset CreatedAt,
    HardwareCompatibilityStatus Compatibility,
    PerformanceMonitorSnapshot Snapshot,
    string LocalLibreHardwareMonitorReport,
    string? ElevatedLibreHardwareMonitorReport,
    string SensorHostErrorLog,
    IReadOnlyList<LogEntry> Logs);

public sealed record ElevatedSensorResponse(
    bool Ok,
    string Message,
    HardwareSensorSnapshot? Snapshot = null,
    string? LibreHardwareMonitorReport = null,
    PawnIoStatus? PawnIo = null);

public sealed record DiagnosticFileInfo(
    string Path,
    bool Exists,
    long? Size,
    string? Version,
    string? Sha256);

public sealed record DiagnosticHttpResult(
    string Url,
    bool Ok,
    int? StatusCode,
    string Body,
    string? Error,
    long ElapsedMilliseconds);

public sealed record SmtcDiagnosticBundle(
    DateTimeOffset CreatedAt,
    string PcAppVersion,
    string WindowsVersion,
    WorkerSnapshot? Worker,
    DiagnosticFileInfo BridgeBinary,
    DiagnosticFileInfo DeviceFont,
    DiagnosticHttpResult Health,
    DiagnosticHttpResult MediaStatus,
    DiagnosticHttpResult? DeviceConfig,
    string BridgeLogTail,
    IReadOnlyList<LogEntry> RecentLogs);
