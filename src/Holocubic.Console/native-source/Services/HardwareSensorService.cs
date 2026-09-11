using System.Security.Principal;
using System.Runtime.InteropServices;
using Clocteck.CubicCenter.Core;
using Clocteck.CubicCenter.Models;
using LibreHardwareMonitor.Hardware;

namespace Clocteck.CubicCenter.Services;

public sealed class HardwareSensorService : IDisposable, ISensorProvider
{
    private readonly object _sync = new();
    private readonly AppLog _log;
    private readonly bool _elevated;
    private readonly IReadOnlyList<IVendorSensorProvider> _vendorProviders = [new AsusSensorReader()];
    private Computer? _computer;
    private string _status = "硬件传感器尚未初始化";
    private bool _disposed;

    public HardwareSensorService(AppLog log)
    {
        _log = log;
        _elevated = IsProcessElevated();
        try
        {
            _computer = new Computer
            {
                IsCpuEnabled = true,
                IsGpuEnabled = true,
                IsMemoryEnabled = true,
                IsMotherboardEnabled = true,
                IsControllerEnabled = true,
                IsPowerMonitorEnabled = true,
                IsStorageEnabled = true,
                IsNetworkEnabled = true,
                IsBatteryEnabled = true,
            };
            _computer.Open();
            _status = _elevated
                ? "LibreHardwareMonitor 已启动（管理员权限）"
                : "LibreHardwareMonitor 已启动；部分温度和风扇可能需要管理员权限";
            _log.Info("硬件传感器", _status);
        }
        catch (Exception error)
        {
            _status = "LibreHardwareMonitor 启动失败：" + error.Message;
            _computer?.Close();
            _computer = null;
            _log.Warn("硬件传感器", _status);
        }
    }

    public string ProviderId => "librehardwaremonitor";
    public bool IsAvailable { get { lock (_sync) return _computer is not null && !_disposed; } }
    public string ProviderStatus { get { lock (_sync) return _status; } }

    public HardwareSensorSnapshot GetSnapshot()
    {
        lock (_sync)
        {
            if (_computer is null || _disposed)
            {
                return Empty(_status);
            }

            try
            {
                var readings = new List<SensorReading>();
                var rawReadings = new List<RawSensorReading>();
                var now = DateTimeOffset.Now;
                var source = _elevated ? "librehardwaremonitor-elevated" : ProviderId;
                foreach (var hardware in _computer.Hardware)
                {
                    Collect(hardware, readings, rawReadings, source, now);
                }

                var cpu = readings.Where(reading => IsCpu(reading.HardwareType)).ToArray();
                var cpuTemperatures = cpu.Where(reading => !Contains(reading.SensorName, "distance") &&
                    !Contains(reading.SensorName, "tjmax") && !Contains(reading.SensorName, "tj max")).ToArray();
                var gpu = SelectGpu(readings);
                var allFans = readings.Where(reading => reading.SensorType == SensorType.Fan && reading.Value >= 0).ToArray();
                var vendor = ReadVendorSensors();
                if (vendor.Available)
                {
                    AddVendorReading(rawReadings, "cpu-temperature", "CPU Temperature", "Temperature", "C", vendor.CpuTemperatureC, now);
                    AddVendorReading(rawReadings, "cpu-fan", "CPU Fan", "Fan", "RPM", vendor.CpuFanRpm, now);
                    AddVendorReading(rawReadings, "gpu-fan", "GPU Fan", "Fan", "RPM", vendor.GpuFanRpm, now);
                }

                var cpuName = HardwareNameFormatter.FormatCpu(readings.FirstOrDefault(reading => IsCpu(reading.HardwareType))?.HardwareName);
                var gpuName = gpu.Length == 0 ? null : HardwareNameFormatter.FormatGpu(gpu[0].HardwareName);
                var cpuClock = AverageCoreClocks(cpu) ?? ReadWindowsCpuClockMhz();
                var cpuTemperature = Pick(cpuTemperatures, SensorType.Temperature, TemperatureScore, 1, 150) ?? vendor.CpuTemperatureC;
                var cpuVoltage = Pick(cpu.Where(reading => !Contains(reading.SensorName, "vid") &&
                    (Contains(reading.SensorName, "core") || Contains(reading.SensorName, "cpu"))), SensorType.Voltage, CpuVoltageScore, 0.01, 5);
                var cpuPower = Pick(cpu.Where(reading => Contains(reading.SensorName, "package") || Contains(reading.SensorName, "ppt") ||
                    reading.SensorName.Equals("CPU Power", StringComparison.OrdinalIgnoreCase)), SensorType.Power, CpuPowerScore, 0, 1000);
                var cpuFan = PickFan(allFans, "cpu") ?? vendor.CpuFanRpm;
                var cpuTemperatureMax = Maximum(cpuTemperatures, SensorType.Temperature, 1, 150);
                var cpuCoreAverageTemperature = PickNamed(cpuTemperatures, SensorType.Temperature, "core average", 1, 150)
                    ?? AverageNamed(cpuTemperatures.Where(reading => !Contains(reading.SensorName, "max")), SensorType.Temperature, "core", 1, 150);
                var cpuCoresPower = PickNamed(cpu, SensorType.Power, "cores", 0.01, 1000);
                var cpuVid = PickNamed(cpu, SensorType.Voltage, "vid", 0.01, 5);
                var cpuBusClock = PickNamed(cpu, SensorType.Clock, "bus", 1);
                var gpuUsage = Pick(gpu.Where(reading => !Contains(reading.SensorName, "memory") &&
                    !Contains(reading.SensorName, "video") && !Contains(reading.SensorName, "copy")), SensorType.Load, GpuLoadScore, 0, 100);
                var gpuClock = Pick(gpu.Where(reading => !Contains(reading.SensorName, "memory") && !Contains(reading.SensorName, "vram")), SensorType.Clock, GpuClockScore, 1);
                var gpuTemperature = Pick(gpu, SensorType.Temperature, GpuTemperatureScore, 1, 150);
                var gpuFan = PickFan(gpu, "gpu") ?? vendor.GpuFanRpm;
                var gpuHotspot = PickNamedAny(gpu.Where(reading => !Contains(reading.SensorName, "memory") && !Contains(reading.SensorName, "vram")),
                    SensorType.Temperature, ["hot spot", "hotspot", "junction"], 1, 160);
                var gpuMemoryTemperature = PickNamedAny(gpu, SensorType.Temperature, ["memory junction", "memory", "vram"], 1, 160);
                var gpuPower = Pick(gpu, SensorType.Power, GpuPowerScore, 0.01, 1500);
                var gpuVoltage = Pick(gpu, SensorType.Voltage, GpuVoltageScore, 0.01, 5);
                var gpuMemoryClock = PickNamedAny(gpu, SensorType.Clock, ["memory", "vram"], 1);
                var gpuVramUsed = PickAnyMatching(gpu, [SensorType.SmallData, SensorType.Data],
                    reading => (Contains(reading.SensorName, "memory used") || Contains(reading.SensorName, "vram used")) &&
                               !Contains(reading.SensorName, "shared"), GpuVramUsedScore, 0);
                var gpuVramTotal = PickAnyMatching(gpu, [SensorType.SmallData, SensorType.Data],
                    reading => (Contains(reading.SensorName, "memory total") || Contains(reading.SensorName, "vram total")) &&
                               !Contains(reading.SensorName, "shared"), GpuVramTotalScore, 0);
                var motherboard = readings.Where(reading => IsBoard(reading.HardwareType)).ToArray();
                var storage = readings.Where(reading => IsStorage(reading.HardwareType)).ToArray();
                var motherboardTemperature = Pick(motherboard, SensorType.Temperature, MotherboardTemperatureScore, 1, 150);
                var vrmTemperature = PickNamedAny(motherboard, SensorType.Temperature, ["vrm", "mos", "vcore"], 1, 170);
                var diskTemperature = Pick(storage, SensorType.Temperature, DiskTemperatureScore, 1, 150);
                var diskHealth = Pick(storage.Where(reading => Contains(reading.SensorName, "remaining life") ||
                    Contains(reading.SensorName, "health")), SensorType.Level, DiskHealthScore, 0, 100);
                var chassisFan = PickFan(allFans, "chassis") ?? PickFan(allFans, "system");
                var pump = PickFan(allFans, "pump") ?? PickFan(allFans, "aio");

                // Unlabelled motherboard headers remain in Readings for manual binding.
                // A chassis/GPU fan must not silently become the CPU fan.
                var available = readings.Count > 0 || vendor.Available;
                var detail = available
                    ? $"已识别 {rawReadings.Count} 个传感器，{rawReadings.Count(reading => reading.Value.HasValue)} 项有读数" + (vendor.Available ? "；厂商传感器可用" : string.Empty)
                    : "没有读取到硬件传感器" + (_elevated ? string.Empty : "，请以管理员身份运行");

                return new HardwareSensorSnapshot(
                    available,
                    _elevated,
                    detail,
                    cpuName,
                    gpuName,
                    cpuClock,
                    cpuTemperature,
                    cpuVoltage,
                    cpuPower,
                    cpuFan,
                    gpuUsage,
                    gpuClock,
                    gpuTemperature,
                    gpuFan,
                    rawReadings.Count,
                    now,
                    cpuTemperatureMax,
                    cpuCoreAverageTemperature,
                    cpuCoresPower,
                    cpuVid,
                    cpuBusClock,
                    gpuHotspot,
                    gpuMemoryTemperature,
                    gpuPower,
                    gpuVoltage,
                    gpuMemoryClock,
                    gpuVramUsed,
                    gpuVramTotal,
                    motherboardTemperature,
                    vrmTemperature,
                    diskTemperature,
                    diskHealth,
                    chassisFan,
                    pump,
                    rawReadings,
                    gpu.FirstOrDefault()?.HardwareId);
            }
            catch (Exception error)
            {
                _status = "读取硬件传感器失败：" + error.Message;
                _log.Warn("硬件传感器", _status);
                return Empty(_status);
            }
        }
    }

    public string GetDiagnosticReport()
    {
        lock (_sync)
        {
            if (_computer is null || _disposed) return _status;
            try
            {
                foreach (var hardware in _computer.Hardware) UpdateTree(hardware);
                return _computer.GetReport();
            }
            catch (Exception error)
            {
                return "LibreHardwareMonitor 诊断报告生成失败：" + error;
            }
        }
    }

    private VendorSensorSnapshot ReadVendorSensors()
    {
        var snapshots = _vendorProviders.Where(provider => provider.IsAvailable).Select(provider =>
        {
            try { return provider.Read(); }
            catch (Exception error)
            {
                _log.Warn("硬件传感器", $"厂商数据源 {provider.ProviderId} 读取失败：{error.Message}");
                return new VendorSensorSnapshot(false, null, null, null);
            }
        }).Where(value => value.Available).ToArray();
        return new(
            snapshots.Length > 0,
            snapshots.Select(value => value.CpuTemperatureC).FirstOrDefault(value => value.HasValue),
            snapshots.Select(value => value.CpuFanRpm).FirstOrDefault(value => value.HasValue),
            snapshots.Select(value => value.GpuFanRpm).FirstOrDefault(value => value.HasValue));
    }

    private static void UpdateTree(IHardware hardware)
    {
        hardware.Update();
        foreach (var subHardware in hardware.SubHardware) UpdateTree(subHardware);
    }

    private static void Collect(IHardware hardware, ICollection<SensorReading> readings,
        ICollection<RawSensorReading> rawReadings, string source, DateTimeOffset timestamp)
    {
        // One unsupported device should not suppress every other device's readings.
        var updated = true;
        try { hardware.Update(); }
        catch { updated = false; }
        foreach (var sensor in hardware.Sensors)
        {
            double? value = updated && sensor.Value is float sample && float.IsFinite(sample)
                ? sensor.SensorType == SensorType.Data ? (double)sample * 1024 : sample
                : null;
            rawReadings.Add(new RawSensorReading(
                "lhm:" + sensor.Identifier,
                hardware.Identifier.ToString(),
                hardware.Name,
                hardware.HardwareType.ToString(),
                sensor.Name,
                sensor.SensorType.ToString(),
                value,
                UnitFor(sensor.SensorType),
                source,
                timestamp));
            if (!value.HasValue) continue;
            readings.Add(new SensorReading(
                hardware.Identifier.ToString(),
                hardware.HardwareType.ToString(),
                hardware.Name,
                sensor.SensorType,
                sensor.Name,
                value.Value));
        }

        foreach (var subHardware in hardware.SubHardware)
        {
            Collect(subHardware, readings, rawReadings, source, timestamp);
        }
    }

    private static string UnitFor(SensorType type) => type.ToString() switch
    {
        "Temperature" => "C", "Clock" => "MHz", "Frequency" => "Hz", "Fan" => "RPM",
        "Voltage" => "V", "Current" => "A", "Power" => "W",
        "Load" or "Control" or "Level" or "Humidity" => "%",
        "Data" or "SmallData" => "MB", "Throughput" => "B/s", "Flow" => "L/h",
        "TimeSpan" => "s", "Timing" => "ns", "Energy" => "mWh", "Noise" => "dBA",
        "Conductivity" => "µS/cm", _ => string.Empty
    };

    private static void AddVendorReading(ICollection<RawSensorReading> readings, string id, string name,
        string sensorType, string unit, double? value, DateTimeOffset timestamp)
    {
        if (!value.HasValue || !double.IsFinite(value.Value)) return;
        readings.Add(new("asus:" + id, "asus:atkacpi", "ASUS ATK ACPI", "Motherboard", name,
            sensorType, value, unit, "asus-atkacpi", timestamp));
    }

    private static double? AverageCoreClocks(IEnumerable<SensorReading> readings)
    {
        var clocks = readings
            .Where(reading => reading.SensorType == SensorType.Clock && reading.Value > 1 &&
                (Contains(reading.SensorName, "core") || Contains(reading.SensorName, "average")))
            .Where(reading => !Contains(reading.SensorName, "bus") && !Contains(reading.SensorName, "uncore"))
            .Select(reading => (double)reading.Value)
            .ToArray();
        if (clocks.Length > 0) return Math.Round(clocks.Average(), 1);
        return Pick(readings, SensorType.Clock, CpuClockScore, 1);
    }

    private static SensorReading[] SelectGpu(IEnumerable<SensorReading> readings) => readings
        .Where(reading => IsGpu(reading.HardwareType))
        .GroupBy(reading => reading.HardwareId)
        .OrderByDescending(group => GpuHardwareScore(group.First().HardwareType, group.First().HardwareName))
        .ThenBy(group => group.Key, StringComparer.Ordinal)
        .Select(group => group.ToArray())
        .FirstOrDefault() ?? [];

    private static int GpuHardwareScore(string type, string name)
    {
        var score = type.Equals("GpuNvidia", StringComparison.OrdinalIgnoreCase) ? 400
            : type.Equals("GpuAmd", StringComparison.OrdinalIgnoreCase) ? 300
            : type.Equals("GpuIntel", StringComparison.OrdinalIgnoreCase) ? 200
            : 100;
        if (Contains(name, "RTX") || Contains(name, "GTX") || Contains(name, "RX ") || Contains(name, "Arc")) score += 100;
        return score;
    }

    private static double? ReadWindowsCpuClockMhz()
    {
        var count = Environment.ProcessorCount;
        var itemSize = Marshal.SizeOf<ProcessorPowerInformation>();
        var buffer = Marshal.AllocHGlobal(itemSize * count);
        try
        {
            if (CallNtPowerInformation(11, IntPtr.Zero, 0, buffer, itemSize * count) != 0) return null;
            var clocks = new List<uint>(count);
            for (var index = 0; index < count; index++)
            {
                var item = Marshal.PtrToStructure<ProcessorPowerInformation>(buffer + index * itemSize);
                if (item.CurrentMhz > 0) clocks.Add(item.CurrentMhz);
            }
            return clocks.Count == 0 ? null : Math.Round(clocks.Average(value => (double)value));
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static double? Pick(
        IEnumerable<SensorReading> readings,
        SensorType type,
        Func<SensorReading, int> score,
        double min = double.NegativeInfinity,
        double max = double.PositiveInfinity) => readings
            .Where(reading => reading.SensorType == type && reading.Value >= min && reading.Value <= max)
            .OrderByDescending(score)
        .Select(reading => (double?)Math.Round(reading.Value, 2))
        .FirstOrDefault();

    private static double? PickAny(
        IEnumerable<SensorReading> readings,
        IReadOnlyCollection<SensorType> types,
        Func<SensorReading, int> score,
        double min = double.NegativeInfinity,
        double max = double.PositiveInfinity) => readings
        .Where(reading => types.Contains(reading.SensorType) && reading.Value >= min && reading.Value <= max)
        .OrderByDescending(score)
        .Select(reading => (double?)Math.Round(reading.Value, 2))
        .FirstOrDefault();

    private static double? PickNamed(
        IEnumerable<SensorReading> readings,
        SensorType type,
        string token,
        double min = double.NegativeInfinity,
        double max = double.PositiveInfinity) => readings
        .Where(reading => reading.SensorType == type && Contains(reading.SensorName, token) && reading.Value >= min && reading.Value <= max)
        .OrderByDescending(reading => reading.Value)
        .Select(reading => (double?)Math.Round(reading.Value, 2))
        .FirstOrDefault();

    private static double? PickNamedAny(
        IEnumerable<SensorReading> readings,
        SensorType type,
        IReadOnlyCollection<string> tokens,
        double min = double.NegativeInfinity,
        double max = double.PositiveInfinity) => readings
        .Where(reading => reading.SensorType == type && tokens.Any(token => Contains(reading.SensorName, token)) &&
                          reading.Value >= min && reading.Value <= max)
        .OrderByDescending(reading => tokens.Select((token, index) => Contains(reading.SensorName, token) ? tokens.Count - index : 0).Max())
        .Select(reading => (double?)Math.Round(reading.Value, 2))
        .FirstOrDefault();

    private static double? PickAnyNamed(
        IEnumerable<SensorReading> readings,
        IReadOnlyCollection<SensorType> types,
        IReadOnlyCollection<string> tokens,
        double min = double.NegativeInfinity,
        double max = double.PositiveInfinity) => readings
        .Where(reading => types.Contains(reading.SensorType) && tokens.Any(token => Contains(reading.SensorName, token)) &&
                          reading.Value >= min && reading.Value <= max)
        .OrderByDescending(reading => tokens.Select((token, index) => Contains(reading.SensorName, token) ? tokens.Count - index : 0).Max())
        .Select(reading => (double?)Math.Round(reading.Value, 2))
        .FirstOrDefault();

    private static double? PickAnyMatching(
        IEnumerable<SensorReading> readings,
        IReadOnlyCollection<SensorType> types,
        Func<SensorReading, bool> predicate,
        Func<SensorReading, int> score,
        double min = double.NegativeInfinity,
        double max = double.PositiveInfinity) => readings
        .Where(reading => types.Contains(reading.SensorType) && predicate(reading) && reading.Value >= min && reading.Value <= max)
        .OrderByDescending(score)
        .Select(reading => (double?)Math.Round(reading.Value, 2))
        .FirstOrDefault();

    private static double? AverageNamed(
        IEnumerable<SensorReading> readings,
        SensorType type,
        string token,
        double min,
        double max)
    {
        var values = readings.Where(reading => reading.SensorType == type && Contains(reading.SensorName, token) && reading.Value >= min && reading.Value <= max)
            .Select(reading => (double)reading.Value).ToArray();
        return values.Length == 0 ? null : Math.Round(values.Average(), 2);
    }

    private static double? Maximum(IEnumerable<SensorReading> readings, SensorType type, double min, double max)
    {
        var values = readings.Where(reading => reading.SensorType == type && reading.Value >= min && reading.Value <= max)
            .Select(reading => (double)reading.Value).ToArray();
        return values.Length == 0 ? null : Math.Round(values.Max(), 2);
    }

    private static double? PickFan(IEnumerable<SensorReading> readings, string owner) => readings
        .Where(reading => reading.SensorType == SensorType.Fan && reading.Value >= 0 &&
            (Contains(reading.SensorName, owner) || Contains(reading.HardwareName, owner) ||
                owner == "gpu" && IsGpu(reading.HardwareType)))
        .OrderByDescending(reading => FanScore(reading, owner))
        .Select(reading => (double?)Math.Round(reading.Value))
        .FirstOrDefault();

    private static int TemperatureScore(SensorReading reading) =>
        Score(reading.SensorName, ("package", 100), ("tctl", 95), ("tdie", 94), ("average", 90), ("max", 80), ("core", 60));

    private static int GpuTemperatureScore(SensorReading reading) =>
        Score(reading.SensorName, ("core", 100), ("gpu", 90), ("hot spot", 70), ("hotspot", 70), ("memory", 40));

    private static int CpuVoltageScore(SensorReading reading) =>
        Score(reading.SensorName, ("core", 100), ("vcore", 100), ("vid", 80), ("soc", 40));

    private static int CpuPowerScore(SensorReading reading) =>
        Score(reading.SensorName, ("package", 100), ("cpu", 90), ("ppt", 80), ("cores", 60));

    private static int CpuClockScore(SensorReading reading) =>
        Score(reading.SensorName, ("average", 100), ("core", 90), ("cpu", 80));

    private static int GpuLoadScore(SensorReading reading) =>
        Score(reading.SensorName, ("gpu core", 100), ("d3d 3d", 95), ("3d", 90), ("core", 80), ("gpu", 70), ("memory", 30));

    private static int GpuClockScore(SensorReading reading) =>
        Score(reading.SensorName, ("gpu core", 100), ("core", 90), ("graphics", 85), ("memory", 40));

    private static int GpuHotspotScore(SensorReading reading) =>
        Score(reading.SensorName, ("hot spot", 120), ("hotspot", 120), ("junction", 110), ("core", 1));

    private static int GpuMemoryTemperatureScore(SensorReading reading) =>
        Score(reading.SensorName, ("memory junction", 120), ("memory", 110), ("vram", 100), ("hot", 1));

    private static int GpuPowerScore(SensorReading reading) =>
        Score(reading.SensorName, ("gpu package", 120), ("total", 110), ("board", 100), ("gpu", 90), ("core", 60));

    private static int GpuVoltageScore(SensorReading reading) =>
        Score(reading.SensorName, ("gpu core", 120), ("core", 110), ("gpu", 100), ("memory", 40));

    private static int GpuMemoryClockScore(SensorReading reading) =>
        Score(reading.SensorName, ("memory", 120), ("vram", 110), ("core", 1));

    private static int GpuVramUsedScore(SensorReading reading) =>
        reading.SensorName.Equals("GPU Memory Used", StringComparison.OrdinalIgnoreCase) ? 500
        : Score(reading.SensorName, ("vram used", 450), ("dedicated memory used", 400), ("memory used", 300));

    private static int GpuVramTotalScore(SensorReading reading) =>
        reading.SensorName.Equals("GPU Memory Total", StringComparison.OrdinalIgnoreCase) ? 500
        : Score(reading.SensorName, ("vram total", 450), ("dedicated memory total", 400), ("memory total", 300));

    private static int MotherboardTemperatureScore(SensorReading reading) =>
        Score(reading.SensorName, ("motherboard", 120), ("system", 110), ("chipset", 100), ("pch", 90), ("temperature #1", 40));

    private static int VrmTemperatureScore(SensorReading reading) =>
        Score(reading.SensorName, ("vrm", 130), ("mos", 120), ("vcore", 110), ("power", 50));

    private static int DiskTemperatureScore(SensorReading reading) =>
        Score(reading.SensorName, ("controller", 120), ("composite", 115), ("temperature", 100));

    private static int DiskHealthScore(SensorReading reading) =>
        Score(reading.SensorName, ("remaining life", 120), ("health", 110), ("life", 100));

    private static int FanScore(SensorReading reading, string owner) =>
        Score(reading.SensorName + " " + reading.HardwareName, (owner, 100), ("fan #1", 60), ("fan", 40));

    private static int Score(string text, params (string Token, int Score)[] rules)
    {
        foreach (var rule in rules)
        {
            if (Contains(text, rule.Token)) return rule.Score;
        }
        return 1;
    }

    private static bool Contains(string text, string value) => text.Contains(value, StringComparison.OrdinalIgnoreCase);
    private static bool IsCpu(string value) => value.Equals("Cpu", StringComparison.OrdinalIgnoreCase);
    private static bool IsGpu(string value) => value.StartsWith("Gpu", StringComparison.OrdinalIgnoreCase);
    private static bool IsBoard(string value) => value is "Motherboard" or "SuperIO" or "EmbeddedController";
    private static bool IsStorage(string value) => value.Equals("Storage", StringComparison.OrdinalIgnoreCase);

    private HardwareSensorSnapshot Empty(string status) => new(
        false, _elevated, status, null, null, null, null, null, null, null, null, null, null, null, 0, DateTimeOffset.Now);

    private static bool IsProcessElevated()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
            _computer?.Close();
            _computer = null;
            foreach (var provider in _vendorProviders) provider.Dispose();
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessorPowerInformation
    {
        public uint Number;
        public uint MaxMhz;
        public uint CurrentMhz;
        public uint MhzLimit;
        public uint MaxIdleState;
        public uint CurrentIdleState;
    }

    [DllImport("powrprof.dll")]
    private static extern uint CallNtPowerInformation(
        int informationLevel,
        IntPtr inputBuffer,
        int inputBufferSize,
        IntPtr outputBuffer,
        int outputBufferSize);

    private sealed record SensorReading(
        string HardwareId,
        string HardwareType,
        string HardwareName,
        SensorType SensorType,
        string SensorName,
        double Value);
}
