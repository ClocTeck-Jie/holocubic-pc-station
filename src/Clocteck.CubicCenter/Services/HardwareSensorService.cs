using System.Security.Principal;
using System.Runtime.InteropServices;
using Clocteck.CubicCenter.Core;
using Clocteck.CubicCenter.Models;
using LibreHardwareMonitor.Hardware;

namespace Clocteck.CubicCenter.Services;

public sealed class HardwareSensorService : IDisposable
{
    private readonly object _sync = new();
    private readonly AppLog _log;
    private readonly bool _elevated;
    private readonly AsusSensorReader _asus = new();
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
                foreach (var hardware in _computer.Hardware)
                {
                    Collect(hardware, readings);
                }

                var cpu = readings.Where(reading => IsCpu(reading.HardwareType)).ToArray();
                var gpu = SelectGpu(readings);
                var allFans = readings.Where(reading => reading.SensorType == SensorType.Fan && reading.Value > 0).ToArray();
                var asus = _asus.Read();

                var cpuName = HardwareNameFormatter.FormatCpu(readings.FirstOrDefault(reading => IsCpu(reading.HardwareType))?.HardwareName);
                var gpuName = gpu.Length == 0 ? null : HardwareNameFormatter.FormatGpu(gpu[0].HardwareName);
                var cpuClock = AverageCoreClocks(cpu) ?? ReadWindowsCpuClockMhz();
                var cpuTemperature = Pick(cpu, SensorType.Temperature, TemperatureScore, 1, 150) ?? asus.CpuTemperatureC;
                var cpuVoltage = Pick(cpu, SensorType.Voltage, CpuVoltageScore, 0.01, 5);
                var cpuPower = Pick(cpu, SensorType.Power, CpuPowerScore, 0.01, 1000);
                var cpuFan = PickFan(allFans, "cpu") ?? asus.CpuFanRpm;
                var gpuUsage = Pick(gpu, SensorType.Load, GpuLoadScore, 0, 100);
                var gpuClock = Pick(gpu, SensorType.Clock, GpuClockScore, 1);
                var gpuTemperature = Pick(gpu, SensorType.Temperature, GpuTemperatureScore, 1, 150);
                var gpuFan = PickFan(gpu.Where(reading => reading.SensorType == SensorType.Fan && reading.Value > 0), "gpu") ?? asus.GpuFanRpm;

                // Many motherboards expose fan headers under Super I/O with names such as
                // "Fan #1". If neither CPU nor GPU is named, keep the first valid fan as
                // the app's generic fan fallback instead of inventing a zero value.
                cpuFan ??= allFans.OrderByDescending(reading => FanScore(reading, "cpu")).FirstOrDefault()?.Value;

                var available = readings.Count > 0;
                var detail = available
                    ? $"已读取 {readings.Count} 个有效传感器" + (asus.Available ? "；ASUS ACPI 可用" : string.Empty)
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
                    readings.Count,
                    DateTimeOffset.Now);
            }
            catch (Exception error)
            {
                _status = "读取硬件传感器失败：" + error.Message;
                _log.Warn("硬件传感器", _status);
                return Empty(_status);
            }
        }
    }

    private static void Collect(IHardware hardware, ICollection<SensorReading> readings)
    {
        hardware.Update();
        foreach (var sensor in hardware.Sensors)
        {
            if (sensor.Value is not float value || float.IsNaN(value) || float.IsInfinity(value)) continue;
            readings.Add(new SensorReading(
                hardware.HardwareType.ToString(),
                hardware.Name,
                sensor.SensorType,
                sensor.Name,
                value));
        }

        foreach (var subHardware in hardware.SubHardware)
        {
            Collect(subHardware, readings);
        }
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
        .GroupBy(reading => (reading.HardwareType, reading.HardwareName))
        .OrderByDescending(group => GpuHardwareScore(group.Key.HardwareType, group.Key.HardwareName))
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

    private static double? PickFan(IEnumerable<SensorReading> readings, string owner) => readings
        .Where(reading => reading.SensorType == SensorType.Fan && reading.Value > 0 &&
            (Contains(reading.SensorName, owner) || Contains(reading.HardwareName, owner)))
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
            _asus.Dispose();
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
        string HardwareType,
        string HardwareName,
        SensorType SensorType,
        string SensorName,
        float Value);
}
