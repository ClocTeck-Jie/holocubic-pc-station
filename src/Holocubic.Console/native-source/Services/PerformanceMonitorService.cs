using Clocteck.CubicCenter.Core;
using Clocteck.CubicCenter.Models;

namespace Clocteck.CubicCenter.Services;

public sealed class PerformanceMonitorService : IDisposable
{
    private readonly object _sync = new();
    private readonly HardwareSensorService _lhm;
    private readonly ElevatedSensorClient _elevated;
    private readonly HwInfoSharedMemoryProvider _hwInfo = new();
    private readonly PdhPerformanceProvider _pdh = new();
    private readonly WmiHardwareInfoProvider _wmi;
    private readonly SystemStatsService _nativeFallback = new();
    private readonly PerformanceMonitorSettings _settings;
    private readonly AppLog _log;
    private readonly Dictionary<string, double> _smoothed = new(StringComparer.Ordinal);
    private PerformanceMonitorSnapshot? _cached;
    private DateTimeOffset _lastSample = DateTimeOffset.MinValue;
    private SystemStats? _lastNative;
    private bool _disposed;

    public PerformanceMonitorService(AppLog log, PerformanceMonitorSettings? settings = null)
    {
        _log = log;
        _settings = settings ?? new PerformanceMonitorSettings();
        _settings.SampleIntervalMs = Math.Clamp(_settings.SampleIntervalMs, 500, 5000);
        _settings.SmoothingFactor = Math.Clamp(_settings.SmoothingFactor, 0.05, 1);
        _lhm = new HardwareSensorService(log);
        _elevated = new ElevatedSensorClient(log);
        _wmi = new WmiHardwareInfoProvider(log);
        log.Info("PC 监控", _pdh.Status);
    }

    public PerformanceMonitorSnapshot GetSnapshot(bool force = false)
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var now = DateTimeOffset.Now;
            if (!force && _cached is not null && (now - _lastSample).TotalMilliseconds < _settings.SampleIntervalMs)
                return _cached;

            var native = _nativeFallback.GetSnapshot();
            var pdh = _pdh.Read();
            var localLhm = _lhm.GetSnapshot();
            var needElevated = !localLhm.Elevated &&
                               (localLhm.CpuTemperatureC is null || localLhm.CpuPackagePowerW is null);
            var elevatedResponse = _elevated.ReadSnapshot(_settings.EnableElevatedSensorHost && needElevated);
            var elevatedLhm = elevatedResponse?.Ok == true ? elevatedResponse.Snapshot : null;
            var lhm = MergeSensors(elevatedLhm, localLhm);
            var hwInfo = _settings.EnableHwInfoSharedMemory ? _hwInfo.Read() : null;
            lhm = MergeSensors(hwInfo, lhm);
            var hardware = _wmi.Snapshot;
            var totalMemory = hardware.MemoryTotalBytes ?? native.MemoryTotalBytes;
            var availableMemory = pdh.MemoryAvailableBytes.HasValue
                ? (ulong)Math.Clamp(pdh.MemoryAvailableBytes.Value, 0d, (double)totalMemory)
                : totalMemory > native.MemoryUsedBytes ? totalMemory - native.MemoryUsedBytes : 0;
            var usedMemory = totalMemory - availableMemory;
            var cpuPercent = pdh.CpuPercent ?? native.CpuPercent;
            var (fallbackDown, fallbackUp) = NativeNetworkRates(native);
            var networkDown = pdh.NetworkReceiveBytesPerSecond ?? fallbackDown;
            var networkUp = pdh.NetworkSendBytesPerSecond ?? fallbackUp;

            lhm = lhm with
            {
                CpuName = FirstText(hardware.CpuShortName, lhm.CpuName),
                GpuName = FirstText(lhm.GpuName, hardware.GpuShortName),
            };
            hardware = hardware with
            {
                CpuName = FirstText(hardware.CpuName, lhm.CpuName, Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER")),
                CpuShortName = FirstText(hardware.CpuShortName, lhm.CpuName),
                GpuName = FirstText(lhm.GpuName, hardware.GpuName),
                GpuShortName = FirstText(lhm.GpuName, hardware.GpuShortName),
            };

            var values = new List<CanonicalSensorValue>();
            Add(values, CanonicalSensorId.CpuUsage, "cpu.usage", Smooth("cpu.usage", cpuPercent), "%", pdh.CpuPercent.HasValue ? "pdh" : "windows-native", now);
            Add(values, CanonicalSensorId.CpuTemperature, "cpu.temperature", lhm.CpuTemperatureC, "C", SourceFor(hwInfo, elevatedLhm, localLhm, value => value.CpuTemperatureC), now);
            Add(values, CanonicalSensorId.CpuTemperatureMax, "cpu.temperature.max", lhm.CpuTemperatureMaxC, "C", SourceFor(hwInfo, elevatedLhm, localLhm, value => value.CpuTemperatureMaxC), now);
            Add(values, CanonicalSensorId.CpuPower, "cpu.power", lhm.CpuPackagePowerW, "W", SourceFor(hwInfo, elevatedLhm, localLhm, value => value.CpuPackagePowerW), now);
            Add(values, CanonicalSensorId.CpuClock, "cpu.clock", lhm.CpuClockMhz, "MHz", SourceFor(hwInfo, elevatedLhm, localLhm, value => value.CpuClockMhz), now);
            Add(values, CanonicalSensorId.CpuVoltage, "cpu.voltage", lhm.CpuVoltageV, "V", SourceFor(hwInfo, elevatedLhm, localLhm, value => value.CpuVoltageV), now);
            Add(values, CanonicalSensorId.CpuFan, "cpu.fan", lhm.CpuFanRpm, "RPM", SourceFor(hwInfo, elevatedLhm, localLhm, value => value.CpuFanRpm), now);
            foreach (var core in pdh.CpuCoreUsagePercent.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
                Add(values, CanonicalSensorId.CpuCoreUsage, "cpu.core.usage", Smooth("cpu.core." + core.Key, core.Value), "%", "pdh", now, core.Key);

            Add(values, CanonicalSensorId.GpuUsage, "gpu.usage", SmoothNullable("gpu.usage", lhm.GpuUsagePercent), "%", SourceFor(hwInfo, elevatedLhm, localLhm, value => value.GpuUsagePercent), now);
            Add(values, CanonicalSensorId.GpuTemperature, "gpu.temperature", lhm.GpuTemperatureC, "C", SourceFor(hwInfo, elevatedLhm, localLhm, value => value.GpuTemperatureC), now);
            Add(values, CanonicalSensorId.GpuHotspot, "gpu.hotspot", lhm.GpuHotspotTemperatureC, "C", SourceFor(hwInfo, elevatedLhm, localLhm, value => value.GpuHotspotTemperatureC), now);
            Add(values, CanonicalSensorId.GpuPower, "gpu.power", lhm.GpuPowerW, "W", SourceFor(hwInfo, elevatedLhm, localLhm, value => value.GpuPowerW), now);
            Add(values, CanonicalSensorId.GpuClock, "gpu.clock", lhm.GpuClockMhz, "MHz", SourceFor(hwInfo, elevatedLhm, localLhm, value => value.GpuClockMhz), now);
            Add(values, CanonicalSensorId.GpuMemoryClock, "gpu.memory.clock", lhm.GpuMemoryClockMhz, "MHz", SourceFor(hwInfo, elevatedLhm, localLhm, value => value.GpuMemoryClockMhz), now);
            Add(values, CanonicalSensorId.GpuVramUsed, "gpu.vram.used", lhm.GpuVramUsedMb, "MB", SourceFor(hwInfo, elevatedLhm, localLhm, value => value.GpuVramUsedMb), now);
            Add(values, CanonicalSensorId.GpuFan, "gpu.fan", lhm.GpuFanRpm, "RPM", SourceFor(hwInfo, elevatedLhm, localLhm, value => value.GpuFanRpm), now);
            Add(values, CanonicalSensorId.GpuVoltage, "gpu.voltage", lhm.GpuVoltageV, "V", SourceFor(hwInfo, elevatedLhm, localLhm, value => value.GpuVoltageV), now);

            var memoryPercent = totalMemory == 0 ? 0 : usedMemory * 100d / totalMemory;
            Add(values, CanonicalSensorId.RamUsage, "ram.usage", Smooth("ram.usage", memoryPercent), "%", pdh.MemoryAvailableBytes.HasValue ? "pdh" : "windows-native", now);
            Add(values, CanonicalSensorId.RamUsed, "ram.used", usedMemory, "B", pdh.MemoryAvailableBytes.HasValue ? "pdh+wmi" : "windows-native", now);
            Add(values, CanonicalSensorId.RamTotal, "ram.total", totalMemory, "B", hardware.MemoryTotalBytes.HasValue ? "wmi" : "windows-native", now);
            Add(values, CanonicalSensorId.NetworkDownload, "network.download", Smooth("network.download", networkDown), "B/s", pdh.NetworkReceiveBytesPerSecond.HasValue ? "pdh" : "windows-native", now);
            Add(values, CanonicalSensorId.NetworkUpload, "network.upload", Smooth("network.upload", networkUp), "B/s", pdh.NetworkSendBytesPerSecond.HasValue ? "pdh" : "windows-native", now);
            Add(values, CanonicalSensorId.DiskRead, "disk.read", SmoothNullable("disk.read", pdh.DiskReadBytesPerSecond), "B/s", "pdh", now);
            Add(values, CanonicalSensorId.DiskWrite, "disk.write", SmoothNullable("disk.write", pdh.DiskWriteBytesPerSecond), "B/s", "pdh", now);
            Add(values, CanonicalSensorId.DiskTemperature, "disk.temperature", lhm.DiskTemperatureC, "C", SourceFor(hwInfo, elevatedLhm, localLhm, value => value.DiskTemperatureC), now);

            var system = new SystemStats(
                Math.Round(cpuPercent, 1),
                usedMemory,
                totalMemory,
                native.NetworkReceivedBytes,
                native.NetworkSentBytes,
                native.UptimeSeconds,
                now);
            var providers = new List<SensorProviderStatus>
            {
                Provider(_lhm, "LibreHardwareMonitor", true, now),
                Provider(_pdh, "Windows PDH", true, now),
                Provider(_wmi, "Windows WMI", true, now),
                new("elevated-lhm", "管理员传感器 Host", _settings.EnableElevatedSensorHost, _elevated.Available,
                    ElevatedSensorHost.CurrentProcessIsElevated() ? "主程序已具有管理员权限" : _elevated.Status, now),
                new("hwinfo-shm", "HWiNFO Shared Memory", _settings.EnableHwInfoSharedMemory, _hwInfo.IsAvailable,
                    _settings.EnableHwInfoSharedMemory ? _hwInfo.ProviderStatus : "默认关闭；仅在用户已安装并合法授权 HWiNFO 时使用", now),
            };

            _lastNative = native;
            _lastSample = now;
            var readings = MergeReadings(hwInfo?.Readings, elevatedLhm?.Readings, localLhm.Readings, pdh.Readings);
            lhm = lhm with { Readings = readings, SensorCount = readings.Count };
            _cached = SensorMapping.Apply(new PerformanceMonitorSnapshot(now, _settings.SampleIntervalMs, hardware, lhm, system, values, providers,
                Readings: readings, Compatibility: GetCompatibilityStatus()), _settings);
            return _cached;
        }
    }

    public HardwareSensorSnapshot GetHardwareSnapshot() => GetSnapshot().HardwareSensors;

    public HardwareCompatibilityStatus GetCompatibilityStatus()
    {
        var now = DateTimeOffset.Now;
        return new(
            ElevatedSensorHost.CurrentProcessIsElevated(),
            _settings.EnableElevatedSensorHost,
            _elevated.Available,
            _elevated.Status,
            PawnIoDetector.Detect(),
            _settings.EnableHwInfoSharedMemory,
            _hwInfo.IsAvailable,
            _settings.EnableHwInfoSharedMemory ? _hwInfo.ProviderStatus : "未启用",
            now);
    }

    public Task<bool> StartElevatedHostAsync() => _elevated.EnsureStartedAsync(true);

    public void ConfigureBindings(Dictionary<string, string>? bindings)
    {
        lock (_sync)
        {
            _settings.Bindings = SensorMapping.ValidateBindings(bindings, _cached?.Readings);
            _cached = null;
        }
    }

    public void SetHwInfoEnabled(bool enabled)
    {
        lock (_sync)
        {
            _settings.EnableHwInfoSharedMemory = enabled;
            _cached = null;
        }
    }

    public HardwareDiagnosticBundle CreateDiagnosticBundle(IReadOnlyList<LogEntry> logs)
    {
        var snapshot = GetSnapshot(true);
        var elevatedReport = _elevated.ReadDiagnostics();
        return new(
            DateTimeOffset.Now,
            GetCompatibilityStatus(),
            snapshot,
            _lhm.GetDiagnosticReport(),
            elevatedReport?.LibreHardwareMonitorReport,
            ReadSensorHostErrorLog(),
            logs);
    }

    private static string ReadSensorHostErrorLog()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "data", "sensor-host-error.log");
            return File.Exists(path) ? File.ReadAllText(path) : "管理员 Sensor Host 没有记录未处理异常";
        }
        catch (Exception error)
        {
            return "读取管理员 Sensor Host 错误日志失败：" + error.Message;
        }
    }

    private (double Down, double Up) NativeNetworkRates(SystemStats current)
    {
        if (_lastNative is null) return (0, 0);
        var seconds = Math.Max(0.1, (current.Timestamp - _lastNative.Timestamp).TotalSeconds);
        return (
            Math.Max(0, current.NetworkReceivedBytes - _lastNative.NetworkReceivedBytes) / seconds,
            Math.Max(0, current.NetworkSentBytes - _lastNative.NetworkSentBytes) / seconds);
    }

    private double Smooth(string key, double value)
    {
        if (!_smoothed.TryGetValue(key, out var previous))
        {
            _smoothed[key] = value;
            return value;
        }
        var next = previous * (1 - _settings.SmoothingFactor) + value * _settings.SmoothingFactor;
        _smoothed[key] = next;
        return next;
    }

    private double? SmoothNullable(string key, double? value) => value.HasValue ? Smooth(key, value.Value) : null;

    private static void Add(
        ICollection<CanonicalSensorValue> target,
        CanonicalSensorId id,
        string key,
        double? value,
        string unit,
        string source,
        DateTimeOffset timestamp,
        string? instance = null)
    {
        if (!value.HasValue || double.IsNaN(value.Value) || double.IsInfinity(value.Value)) return;
        target.Add(new((byte)id, key, Math.Round(value.Value, 3), unit, source, timestamp, instance));
    }

    private static SensorProviderStatus Provider(ISensorProvider provider, string name, bool enabled, DateTimeOffset now) =>
        new(provider.ProviderId, name, enabled, provider.IsAvailable, provider.ProviderStatus, now);

    private static HardwareSensorSnapshot MergeSensors(HardwareSensorSnapshot? preferred, HardwareSensorSnapshot fallback)
    {
        if (preferred?.Available != true) return fallback;
        var sameGpu = preferred.GpuHardwareId is not null && preferred.GpuHardwareId == fallback.GpuHardwareId;
        var gpu = preferred.GpuHardwareId is not null ? preferred : fallback;
        return fallback with
        {
            Available = true,
            Elevated = preferred.Elevated || fallback.Elevated,
            Status = preferred.Status + "；" + fallback.Status,
            CpuName = preferred.CpuName ?? fallback.CpuName,
            GpuName = gpu.GpuName,
            GpuHardwareId = gpu.GpuHardwareId,
            CpuClockMhz = preferred.CpuClockMhz ?? fallback.CpuClockMhz,
            CpuTemperatureC = preferred.CpuTemperatureC ?? fallback.CpuTemperatureC,
            CpuVoltageV = preferred.CpuVoltageV ?? fallback.CpuVoltageV,
            CpuPackagePowerW = preferred.CpuPackagePowerW ?? fallback.CpuPackagePowerW,
            CpuFanRpm = preferred.CpuFanRpm ?? fallback.CpuFanRpm,
            GpuUsagePercent = gpu.GpuUsagePercent ?? (sameGpu ? fallback.GpuUsagePercent : null),
            GpuClockMhz = gpu.GpuClockMhz ?? (sameGpu ? fallback.GpuClockMhz : null),
            GpuTemperatureC = gpu.GpuTemperatureC ?? (sameGpu ? fallback.GpuTemperatureC : null),
            GpuFanRpm = gpu.GpuFanRpm ?? (sameGpu ? fallback.GpuFanRpm : null),
            Timestamp = DateTimeOffset.Now,
            CpuTemperatureMaxC = preferred.CpuTemperatureMaxC ?? fallback.CpuTemperatureMaxC,
            CpuCoreAverageTemperatureC = preferred.CpuCoreAverageTemperatureC ?? fallback.CpuCoreAverageTemperatureC,
            CpuCoresPowerW = preferred.CpuCoresPowerW ?? fallback.CpuCoresPowerW,
            CpuVidV = preferred.CpuVidV ?? fallback.CpuVidV,
            CpuBusClockMhz = preferred.CpuBusClockMhz ?? fallback.CpuBusClockMhz,
            GpuHotspotTemperatureC = gpu.GpuHotspotTemperatureC ?? (sameGpu ? fallback.GpuHotspotTemperatureC : null),
            GpuMemoryTemperatureC = gpu.GpuMemoryTemperatureC ?? (sameGpu ? fallback.GpuMemoryTemperatureC : null),
            GpuPowerW = gpu.GpuPowerW ?? (sameGpu ? fallback.GpuPowerW : null),
            GpuVoltageV = gpu.GpuVoltageV ?? (sameGpu ? fallback.GpuVoltageV : null),
            GpuMemoryClockMhz = gpu.GpuMemoryClockMhz ?? (sameGpu ? fallback.GpuMemoryClockMhz : null),
            GpuVramUsedMb = gpu.GpuVramUsedMb ?? (sameGpu ? fallback.GpuVramUsedMb : null),
            GpuVramTotalMb = gpu.GpuVramTotalMb ?? (sameGpu ? fallback.GpuVramTotalMb : null),
            MotherboardTemperatureC = preferred.MotherboardTemperatureC ?? fallback.MotherboardTemperatureC,
            VrmTemperatureC = preferred.VrmTemperatureC ?? fallback.VrmTemperatureC,
            DiskTemperatureC = preferred.DiskTemperatureC ?? fallback.DiskTemperatureC,
            DiskHealthPercent = preferred.DiskHealthPercent ?? fallback.DiskHealthPercent,
            ChassisFanRpm = preferred.ChassisFanRpm ?? fallback.ChassisFanRpm,
            PumpRpm = preferred.PumpRpm ?? fallback.PumpRpm,
        };
    }

    private static IReadOnlyList<RawSensorReading> MergeReadings(params IReadOnlyList<RawSensorReading>?[] sources)
    {
        var result = new Dictionary<string, RawSensorReading>(StringComparer.Ordinal);
        foreach (var readings in sources)
        foreach (var reading in readings ?? [])
        {
            if (!result.TryGetValue(reading.Id, out var previous) ||
                ((!previous.Value.HasValue || !double.IsFinite(previous.Value.Value)) && reading.Value.HasValue && double.IsFinite(reading.Value.Value)))
                result[reading.Id] = reading;
        }
        return result.Values.ToArray();
    }

    private static string SourceFor(
        HardwareSensorSnapshot? hwInfo,
        HardwareSensorSnapshot? elevated,
        HardwareSensorSnapshot local,
        Func<HardwareSensorSnapshot, double?> selector)
    {
        if (hwInfo?.Available == true && selector(hwInfo).HasValue) return "hwinfo-shm";
        if (elevated?.Available == true && selector(elevated).HasValue) return "librehardwaremonitor-elevated";
        return selector(local).HasValue ? "librehardwaremonitor" : "unavailable";
    }

    private static string? FirstText(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
            _pdh.Dispose();
            _lhm.Dispose();
        }
    }
}
