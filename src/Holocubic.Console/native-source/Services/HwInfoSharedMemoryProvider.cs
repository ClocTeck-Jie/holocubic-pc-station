using System.IO.MemoryMappedFiles;
using System.Text;
using Clocteck.CubicCenter.Models;

namespace Clocteck.CubicCenter.Services;

internal sealed class HwInfoSharedMemoryProvider : ISensorProvider
{
    private const string MappingName = @"Global\HWiNFO_SENS_SM2";
    private const uint Signature = 0x53695748; // "HWiS"
    private static readonly Encoding StrictUtf8 = new UTF8Encoding(false, true);
    private readonly object _sync = new();
    private string _status = "HWiNFO Shared Memory 尚未连接";
    private bool _available;

    public string ProviderId => "hwinfo-shm";
    public bool IsAvailable { get { lock (_sync) return _available; } }
    public string ProviderStatus { get { lock (_sync) return _status; } }

    public HardwareSensorSnapshot Read()
    {
        lock (_sync)
        {
            try
            {
                using var mapping = MemoryMappedFile.OpenExisting(MappingName, MemoryMappedFileRights.Read);
                using var view = mapping.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
                if (view.Capacity < 44) throw new InvalidDataException("HWiNFO 共享内存头不完整");
                var signature = view.ReadUInt32(0);
                if (signature != Signature) throw new InvalidDataException($"共享内存签名不匹配：0x{signature:X8}");
                var version = view.ReadUInt32(4);
                var revision = view.ReadUInt32(8);
                if (version != 1) throw new InvalidDataException($"不支持的 HWiNFO 共享内存版本：{version}");
                var pollTime = view.ReadInt64(12);
                var sensorOffset = view.ReadUInt32(20);
                var sensorSize = view.ReadUInt32(24);
                var sensorCount = view.ReadUInt32(28);
                var readingOffset = view.ReadUInt32(32);
                var readingSize = view.ReadUInt32(36);
                var readingCount = view.ReadUInt32(40);
                if (sensorSize < 264 || readingSize < 316 || sensorCount > 4096 || readingCount > 65536)
                    throw new InvalidDataException("HWiNFO 共享内存结构尺寸无效");
                ValidateSection(view.Capacity, sensorOffset, sensorSize, sensorCount);
                ValidateSection(view.Capacity, readingOffset, readingSize, readingCount);
                var sensorEnd = (long)sensorOffset + (long)sensorSize * sensorCount;
                var readingEnd = (long)readingOffset + (long)readingSize * readingCount;
                if (sensorCount > 0 && readingCount > 0 && sensorOffset < readingEnd && readingOffset < sensorEnd)
                    throw new InvalidDataException("HWiNFO 共享内存区段重叠");
                var pollingPeriod = revision >= 1 && view.Capacity >= 48 && sensorOffset >= 48 && readingOffset >= 48
                    ? view.ReadUInt32(44) : 2000;
                var timestamp = DateTimeOffset.FromUnixTimeSeconds(pollTime);
                var now = DateTimeOffset.Now;
                var maxAgeSeconds = Math.Clamp(pollingPeriod / 1000d * 3, 30, 300);
                var stale = pollTime <= 0 || (now - timestamp).TotalSeconds > maxAgeSeconds || timestamp > now.AddMinutes(5);

                var sensors = new Dictionary<uint, Sensor>();
                for (uint index = 0; index < sensorCount; index++)
                {
                    var position = (long)sensorOffset + (long)index * sensorSize;
                    var id = view.ReadUInt32(position);
                    var instance = view.ReadUInt32(position + 4);
                    var user = ReadText(view, position + 136, 128);
                    var original = ReadText(view, position + 8, 128);
                    sensors[index] = new($"hwinfo:{id:X8}:{instance:X8}", original,
                        string.IsNullOrWhiteSpace(user) ? original : user);
                }

                var readings = new List<Reading>((int)Math.Min(readingCount, 8192));
                var rawReadings = new List<RawSensorReading>((int)Math.Min(readingCount, 8192));
                for (uint index = 0; index < readingCount; index++)
                {
                    var position = (long)readingOffset + (long)index * readingSize;
                    var readingType = view.ReadUInt32(position);
                    var sensorIndex = view.ReadUInt32(position + 4);
                    var readingId = view.ReadUInt32(position + 8);
                    if (!sensors.TryGetValue(sensorIndex, out var sensor))
                        throw new InvalidDataException("HWiNFO 读数引用了不存在的硬件");
                    var user = ReadText(view, position + 140, 128);
                    var original = ReadText(view, position + 12, 128);
                    var label = string.IsNullOrWhiteSpace(user) ? original : user;
                    var unit = ReadText(view, position + 268, 16);
                    var value = view.ReadDouble(position + 284);
                    var normalized = Normalize(unit, value);
                    double? sample = stale || !double.IsFinite(normalized.Value) ? null : normalized.Value;
                    rawReadings.Add(new($"{sensor.Id}:{readingId:X8}", sensor.Id, sensor.DisplayName,
                        HardwareTypeFor(sensor.OriginalName), label, SensorTypeFor(readingType, normalized.Unit),
                        sample, normalized.Unit, ProviderId, timestamp));
                    if (sample.HasValue)
                        readings.Add(new(sensor.Id, sensor.OriginalName, sensor.DisplayName,
                            string.IsNullOrWhiteSpace(original) ? label : original, normalized.Unit, sample.Value));
                }
                // Reject a changing layout/poll rather than publish a mixture of two samples.
                if (view.ReadUInt32(0) != Signature || view.ReadInt64(12) != pollTime ||
                    view.ReadUInt32(20) != sensorOffset || view.ReadUInt32(24) != sensorSize ||
                    view.ReadUInt32(28) != sensorCount || view.ReadUInt32(32) != readingOffset ||
                    view.ReadUInt32(36) != readingSize || view.ReadUInt32(40) != readingCount)
                    return Unavailable("HWiNFO 正在更新传感器，请等待下一次采样");
                if (stale) return Unavailable("HWiNFO 读数已过期，请检查 Shared Memory 是否仍在更新", rawReadings, timestamp);

                var cpu = readings.Where(IsCpu).ToArray();
                var cpuTemperatures = cpu.Where(reading => !Contains(reading.Label, "distance") &&
                    !Contains(reading.Label, "tjmax") && !Contains(reading.Label, "tj max")).ToArray();
                var gpu = readings.Where(IsGpu).GroupBy(reading => reading.HardwareId)
                    .OrderByDescending(group => GpuHardwareScore(group.First().Sensor))
                    .ThenBy(group => group.Key, StringComparer.Ordinal)
                    .Select(group => group.ToArray()).FirstOrDefault() ?? [];
                var fans = readings.Where(reading => IsUnit(reading, "RPM")).ToArray();
                _available = readings.Count > 0;
                _status = _available ? $"HWiNFO Shared Memory 已连接：{readings.Count}/{rawReadings.Count} 项有读数" : "HWiNFO Shared Memory 没有有效读数";
                return new HardwareSensorSnapshot(
                    _available,
                    false,
                    _status,
                    cpu.FirstOrDefault()?.DisplayName,
                    gpu.FirstOrDefault()?.DisplayName,
                    Pick(cpu, reading => IsClock(reading) && !Contains(reading.Label, "bus") && !Contains(reading.Label, "uncore"), CpuClockScore, 1, 10000),
                    Pick(cpuTemperatures, IsTemperature, CpuTemperatureScore, 1, 150),
                    Pick(cpu, IsVoltage, CpuVoltageScore, 0.01, 5),
                    Pick(cpu, IsPower, CpuPowerScore, 0.01, 1500),
                    Pick(fans, reading => Contains(reading.Label, "cpu") || Contains(reading.Label, "processor"), CpuFanScore, 0, 30000),
                    Pick(gpu, IsPercent, GpuUsageScore, 0, 100),
                    Pick(gpu, reading => IsClock(reading) && !Contains(reading.Label, "memory") && !Contains(reading.Label, "vram"), GpuClockScore, 1, 10000),
                    Pick(gpu, IsTemperature, GpuTemperatureScore, 1, 160),
                    Pick(gpu, reading => IsUnit(reading, "RPM"), GpuFanScore, 0, 30000),
                    rawReadings.Count,
                    timestamp,
                    CpuTemperatureMaxC: cpuTemperatures.Where(IsTemperature).Where(reading => reading.Value >= 1 && reading.Value <= 150)
                        .Select(reading => (double?)reading.Value).Max(),
                    CpuCoreAverageTemperatureC: Pick(cpuTemperatures, reading => IsTemperature(reading) && Contains(reading.Label, "core average"), CpuTemperatureScore, 1, 150),
                    CpuCoresPowerW: Pick(cpu, reading => IsPower(reading) &&
                        (Contains(reading.Label, "cores power") || Contains(reading.Label, "core power")), CpuCoresPowerScore, 0.01, 1500),
                    GpuHotspotTemperatureC: Pick(gpu, reading => IsTemperature(reading) &&
                        !Contains(reading.Label, "memory") && !Contains(reading.Label, "vram") &&
                        (Contains(reading.Label, "hot spot") || Contains(reading.Label, "hotspot") || Contains(reading.Label, "junction")), GpuHotspotScore, 1, 170),
                    GpuMemoryTemperatureC: Pick(gpu, reading => IsTemperature(reading) &&
                        (Contains(reading.Label, "memory") || Contains(reading.Label, "vram")), GpuMemoryTemperatureScore, 1, 170),
                    GpuPowerW: Pick(gpu, IsPower, GpuPowerScore, 0.01, 2000),
                    GpuVoltageV: Pick(gpu, IsVoltage, GpuVoltageScore, 0.01, 5),
                    GpuMemoryClockMhz: Pick(gpu, reading => IsClock(reading) &&
                        (Contains(reading.Label, "memory") || Contains(reading.Label, "vram")), GpuMemoryClockScore, 1, 30000),
                    GpuVramUsedMb: Pick(gpu, reading => IsMemoryAmount(reading) && !Contains(reading.Label, "shared") && Contains(reading.Label, "used") &&
                        (Contains(reading.Label, "memory") || Contains(reading.Label, "vram")), GpuVramScore, 0),
                    GpuVramTotalMb: Pick(gpu, reading => IsMemoryAmount(reading) && !Contains(reading.Label, "shared") &&
                        (Contains(reading.Label, "total") || Contains(reading.Label, "capacity")) &&
                        (Contains(reading.Label, "memory") || Contains(reading.Label, "vram")), GpuVramScore, 0),
                    MotherboardTemperatureC: Pick(readings, reading => IsTemperature(reading) &&
                        (Contains(reading.Text, "motherboard") || Contains(reading.Text, "system")), BoardTemperatureScore, 1, 160),
                    VrmTemperatureC: Pick(readings, reading => IsTemperature(reading) &&
                        (Contains(reading.Text, "vrm") || Contains(reading.Text, "mos")), VrmTemperatureScore, 1, 180),
                    DiskTemperatureC: Pick(readings, reading => IsTemperature(reading) &&
                        (Contains(reading.Sensor, "drive") || Contains(reading.Sensor, "ssd") || Contains(reading.Sensor, "nvme")), DiskTemperatureScore, 1, 150),
                    ChassisFanRpm: Pick(fans, reading => Contains(reading.Label, "chassis") || Contains(reading.Label, "system fan"), ChassisFanScore, 0, 30000),
                    PumpRpm: Pick(fans, reading => Contains(reading.Label, "pump"), PumpScore, 0, 30000),
                    Readings: rawReadings,
                    GpuHardwareId: gpu.FirstOrDefault()?.HardwareId);
            }
            catch (FileNotFoundException)
            {
                return Unavailable("HWiNFO 未运行或未启用 Shared Memory Support");
            }
            catch (Exception error)
            {
                return Unavailable("HWiNFO Shared Memory 读取失败：" + error.Message);
            }
        }
    }

    private HardwareSensorSnapshot Unavailable(string message, IReadOnlyList<RawSensorReading>? readings = null,
        DateTimeOffset? timestamp = null)
    {
        _available = false;
        _status = message;
        return new(false, false, message, null, null, null, null, null, null, null, null, null, null, null,
            readings?.Count ?? 0, timestamp ?? DateTimeOffset.Now, Readings: readings);
    }

    private static void ValidateSection(long capacity, uint offset, uint size, uint count)
    {
        if (offset < 44 || offset > capacity || (long)size * count > capacity - offset)
            throw new InvalidDataException("HWiNFO 共享内存区段超出边界");
    }

    private static (string Unit, double Value) Normalize(string unit, double value)
    {
        var normalized = unit.Trim();
        // Both providers express MB as 2^20 bytes. Keep summary and catalog units identical.
        return normalized switch
        {
            "°C" or "C" => ("C", value), "°F" or "F" => ("C", (value - 32) / 1.8),
            "GHz" => ("MHz", value * 1000), "kHz" => ("MHz", value / 1000), "Hz" => ("MHz", value / 1_000_000),
            "GB" or "GiB" => ("MB", value * 1024), "TB" or "TiB" => ("MB", value * 1024 * 1024),
            "MiB" => ("MB", value), "KB" or "KiB" => ("MB", value / 1024),
            "B" or "Bytes" => ("MB", value / 1024 / 1024),
            "KB/s" or "kB/s" or "KiB/s" => ("B/s", value * 1024),
            "MB/s" or "MiB/s" => ("B/s", value * 1024 * 1024),
            "GB/s" or "GiB/s" => ("B/s", value * 1024 * 1024 * 1024),
            "mV" => ("V", value / 1000), "mW" => ("W", value / 1000), "kW" => ("W", value * 1000),
            "mA" => ("A", value / 1000), "rpm" => ("RPM", value),
            _ => (normalized, value)
        };
    }

    private static string SensorTypeFor(uint type, string unit) => type switch
    {
        1 => "Temperature", 2 => "Voltage", 3 => "Fan", 4 => "Current", 5 => "Power", 6 => "Clock", 7 => "Load",
        _ => unit switch
        {
            "C" => "Temperature", "V" => "Voltage", "RPM" => "Fan", "A" => "Current", "W" => "Power",
            "MHz" => "Clock", "%" => "Load", "MB" => "SmallData", "B/s" => "Throughput", _ => "Other"
        }
    };

    private static string HardwareTypeFor(string sensor)
    {
        if (Contains(sensor, "gpu") || Contains(sensor, "graphics"))
            return Contains(sensor, "nvidia") || Contains(sensor, "geforce") ? "GpuNvidia"
                : Contains(sensor, "amd") || Contains(sensor, "radeon") ? "GpuAmd"
                : Contains(sensor, "intel") ? "GpuIntel" : "Gpu";
        if (Contains(sensor, "cpu") || Contains(sensor, "processor")) return "Cpu";
        if (Contains(sensor, "drive") || Contains(sensor, "ssd") || Contains(sensor, "nvme") || Contains(sensor, "hdd")) return "Storage";
        if (Contains(sensor, "memory") || Contains(sensor, "dimm")) return "Memory";
        if (Contains(sensor, "network") || Contains(sensor, "ethernet") || Contains(sensor, "wi-fi")) return "Network";
        if (Contains(sensor, "battery")) return "Battery";
        if (Contains(sensor, "motherboard") || Contains(sensor, "super i/o") || Contains(sensor, "asus") ||
            Contains(sensor, "asrock") || Contains(sensor, "gigabyte") || Contains(sensor, "msi") ||
            Contains(sensor, "nuvoton") || Contains(sensor, "ite ")) return "Motherboard";
        return "Other";
    }

    private static int GpuHardwareScore(string sensor)
    {
        var score = HardwareTypeFor(sensor) switch { "GpuNvidia" => 400, "GpuAmd" => 300, "GpuIntel" => 200, _ => 100 };
        if (Contains(sensor, "RTX") || Contains(sensor, "GTX") || Contains(sensor, "RX ") || Contains(sensor, "Arc")) score += 100;
        if (Contains(sensor, "D3D") || Contains(sensor, "DXGI")) score -= 50;
        return score;
    }

    private static string ReadText(MemoryMappedViewAccessor view, long offset, int length)
    {
        var data = new byte[length];
        view.ReadArray(offset, data, 0, length);
        var end = Array.IndexOf(data, (byte)0);
        if (end < 0) end = data.Length;
        try { return StrictUtf8.GetString(data, 0, end).Trim(); }
        // Older SHM layouts use ANSI; its degree sign is a single byte (0xB0).
        catch (DecoderFallbackException) { return Encoding.Latin1.GetString(data, 0, end).Trim(); }
    }

    private static double? Pick(IEnumerable<Reading> source, Func<Reading, bool> predicate, Func<Reading, int> score,
        double min = double.NegativeInfinity, double max = double.PositiveInfinity) => source
        .Where(reading => predicate(reading) && reading.Value >= min && reading.Value <= max)
        .OrderByDescending(score)
        .Select(reading => (double?)Math.Round(reading.Value, 3))
        .FirstOrDefault();

    private static bool IsCpu(Reading reading) => Contains(reading.Sensor, "cpu") ||
        (Contains(reading.Sensor, "processor") && !Contains(reading.Sensor, "gpu"));
    private static bool IsGpu(Reading reading) => Contains(reading.Sensor, "gpu") || Contains(reading.Sensor, "graphics");
    private static bool IsTemperature(Reading reading) => IsUnit(reading, "C") || reading.Unit.Contains("°C", StringComparison.OrdinalIgnoreCase);
    private static bool IsPower(Reading reading) => IsUnit(reading, "W");
    private static bool IsVoltage(Reading reading) => IsUnit(reading, "V");
    private static bool IsPercent(Reading reading) => IsUnit(reading, "%");
    private static bool IsClock(Reading reading) => IsUnit(reading, "MHz");
    private static bool IsMemoryAmount(Reading reading) => IsUnit(reading, "MB") || IsUnit(reading, "GB");
    private static bool IsUnit(Reading reading, string unit) => reading.Unit.Equals(unit, StringComparison.OrdinalIgnoreCase);
    private static bool Contains(string value, string token) => value.Contains(token, StringComparison.OrdinalIgnoreCase);
    private static int Score(Reading reading, params (string Token, int Score)[] rules)
    {
        foreach (var rule in rules) if (Contains(reading.Text, rule.Token)) return rule.Score;
        return 1;
    }

    private static int CpuTemperatureScore(Reading r) => Score(r, ("tctl/tdie", 200), ("cpu package", 190), ("cpu die", 180), ("core average", 150), ("core max", 140));
    private static int CpuClockScore(Reading r) => Score(r, ("average effective clock", 230), ("average", 220), ("core", 200));
    private static int CpuMaxTemperatureScore(Reading r) => Score(r, ("core max", 220), ("cpu package", 200), ("tctl/tdie", 190), ("cpu die", 180));
    private static int CpuPowerScore(Reading r) => Score(r, ("cpu package power", 220), ("package power", 210), ("cpu ppt", 200), ("cpu total", 180));
    private static int CpuCoresPowerScore(Reading r) => Score(r, ("cpu cores power", 220), ("cores power", 210), ("core power", 180));
    private static int CpuVoltageScore(Reading r) => Score(r, ("core vid", 210), ("vcore", 200), ("core voltage", 190), ("cpu", 100));
    private static int CpuFanScore(Reading r) => Score(r, ("cpu fan", 220), ("processor fan", 210), ("fan", 100));
    private static int GpuUsageScore(Reading r) => Score(r, ("gpu core load", 220), ("gpu utilization", 210), ("gpu usage", 200), ("d3d", 120));
    private static int GpuTemperatureScore(Reading r) => Score(r, ("gpu temperature", 220), ("gpu core", 210), ("hot spot", 150), ("memory", 100));
    private static int GpuHotspotScore(Reading r) => Score(r, ("hot spot", 230), ("hotspot", 230), ("junction", 220));
    private static int GpuMemoryTemperatureScore(Reading r) => Score(r, ("memory junction", 230), ("gpu memory", 220), ("vram", 210));
    private static int GpuPowerScore(Reading r) => Score(r, ("gpu power", 230), ("total graphics power", 225), ("board power", 220), ("gpu chip power", 180));
    private static int GpuVoltageScore(Reading r) => Score(r, ("gpu core voltage", 230), ("gpu voltage", 220), ("core voltage", 180));
    private static int GpuClockScore(Reading r) => Score(r, ("gpu clock", 230), ("graphics clock", 220), ("core clock", 210), ("memory", 100));
    private static int GpuMemoryClockScore(Reading r) => Score(r, ("memory clock", 230), ("vram clock", 220));
    private static int GpuVramScore(Reading r) => Score(r, ("dedicated gpu memory used", 230), ("gpu memory used", 220), ("vram used", 210));
    private static int GpuFanScore(Reading r) => Score(r, ("gpu fan", 230), ("fan", 100));
    private static int BoardTemperatureScore(Reading r) => Score(r, ("motherboard", 220), ("system", 180), ("chipset", 160));
    private static int VrmTemperatureScore(Reading r) => Score(r, ("vrm", 230), ("mos", 220));
    private static int DiskTemperatureScore(Reading r) => Score(r, ("drive temperature", 220), ("composite", 210), ("controller", 200));
    private static int ChassisFanScore(Reading r) => Score(r, ("chassis", 220), ("system fan", 210));
    private static int PumpScore(Reading r) => Score(r, ("water pump", 230), ("aio pump", 220), ("pump", 200));

    private sealed record Sensor(string Id, string OriginalName, string DisplayName);

    private sealed record Reading(string HardwareId, string Sensor, string DisplayName, string Label, string Unit, double Value)
    {
        public string Text => Sensor + " " + Label;
    }
}
