namespace Clocteck.CubicCenter.Models;

public enum CanonicalSensorId : byte
{
    CpuUsage = 0x01,
    CpuTemperature = 0x02,
    CpuPower = 0x03,
    CpuClock = 0x04,
    CpuTemperatureMax = 0x05,
    CpuVoltage = 0x06,
    CpuFan = 0x07,
    CpuCoreUsage = 0x08,
    CpuCoreAverageTemperature = 0x09,
    CpuCoresPower = 0x0A,
    CpuVid = 0x0B,
    CpuBusClock = 0x0C,

    GpuUsage = 0x10,
    GpuTemperature = 0x11,
    GpuHotspot = 0x12,
    GpuPower = 0x13,
    GpuClock = 0x14,
    GpuVramUsed = 0x15,
    GpuMemoryClock = 0x16,
    GpuFan = 0x17,
    GpuVoltage = 0x18,
    GpuMemoryTemperature = 0x19,
    GpuVramTotal = 0x1A,

    RamUsage = 0x20,
    RamUsed = 0x21,
    RamTotal = 0x22,

    NetworkDownload = 0x30,
    NetworkUpload = 0x31,

    DiskRead = 0x40,
    DiskWrite = 0x41,
    DiskTemperature = 0x42,
    DiskHealth = 0x43,
    MotherboardTemperature = 0x50,
    VrmTemperature = 0x51,
    ChassisFan = 0x52,
    Pump = 0x53,
}

public sealed record CanonicalSensorValue(
    byte Id,
    string Key,
    double Value,
    string Unit,
    string Source,
    DateTimeOffset Timestamp,
    string? Instance = null);

public sealed record SensorProviderStatus(
    string Id,
    string Name,
    bool Enabled,
    bool Available,
    string Status,
    DateTimeOffset Timestamp);

public sealed record ComputerHardwareInfo(
    string? CpuName,
    string? CpuShortName,
    int? CpuCores,
    int? CpuLogicalProcessors,
    uint? CpuMaxClockMhz,
    string? GpuName,
    string? GpuShortName,
    ulong? GpuMemoryBytes,
    ulong? MemoryTotalBytes,
    string? Motherboard,
    string? WindowsName,
    string? WindowsVersion,
    IReadOnlyList<DiskHardwareInfo> Disks,
    DateTimeOffset Timestamp);

public sealed record DiskHardwareInfo(string Model, ulong? SizeBytes);

public sealed record PerformanceMonitorSnapshot(
    DateTimeOffset Timestamp,
    int SampleIntervalMs,
    ComputerHardwareInfo Hardware,
    HardwareSensorSnapshot HardwareSensors,
    SystemStats System,
    IReadOnlyList<CanonicalSensorValue> Sensors,
    IReadOnlyList<SensorProviderStatus> Providers,
    IReadOnlyList<RawSensorReading>? Readings = null,
    IReadOnlyList<SensorMappingValue>? Mappings = null,
    PerformanceMonitorSettings? Settings = null,
    HardwareCompatibilityStatus? Compatibility = null);

public sealed record SensorMappingValue(string Key, string Label, string Unit, string? SensorId,
    double? Value, string Source, string Status);

public sealed class PerformanceMonitorSettings
{
    public int SampleIntervalMs { get; set; } = 1000;
    public double SmoothingFactor { get; set; } = 0.3;
    public string SensorProvider { get; set; } = "automatic";
    public bool EnableElevatedSensorHost { get; set; } = true;
    public bool EnableHwInfoSharedMemory { get; set; }
    public Dictionary<string, string> Bindings { get; set; } = new(StringComparer.Ordinal);
}
