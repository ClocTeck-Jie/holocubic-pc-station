using System.Globalization;
using Clocteck.CubicCenter.Models;

namespace Clocteck.CubicCenter.Services;

public static class SensorMapping
{
    public sealed record Definition(string Key, string Label, string Unit, string WireLabel,
        CanonicalSensorId CanonicalId, string CanonicalKey, Func<PerformanceMonitorSnapshot, double?> Read);
    private static double? Canonical(PerformanceMonitorSnapshot s, CanonicalSensorId id) =>
        s.Sensors.FirstOrDefault(v => v.Id == (byte)id)?.Value;
    public static readonly IReadOnlyList<Definition> Definitions =
    [
        new("cpu_usage", "CPU 使用率", "%", "CPU Usage", CanonicalSensorId.CpuUsage, "cpu.usage", s => Canonical(s, CanonicalSensorId.CpuUsage)),
        new("cpu_temp", "CPU 温度", "C", "CPU Temperature", CanonicalSensorId.CpuTemperature, "cpu.temperature", s => s.HardwareSensors.CpuTemperatureC),
        new("cpu_temp_max", "CPU 最高温度", "C", "CPU Maximum Temperature", CanonicalSensorId.CpuTemperatureMax, "cpu.temperature.max", s => s.HardwareSensors.CpuTemperatureMaxC),
        new("cpu_core_average_temp", "CPU 核心平均温度", "C", "CPU Core Average Temperature", CanonicalSensorId.CpuCoreAverageTemperature, "cpu.temperature.average", s => s.HardwareSensors.CpuCoreAverageTemperatureC),
        new("cpu_clock", "CPU 频率", "MHz", "CPU Frequency", CanonicalSensorId.CpuClock, "cpu.clock", s => s.HardwareSensors.CpuClockMhz),
        new("cpu_voltage", "CPU 电压", "V", "CPU Voltage", CanonicalSensorId.CpuVoltage, "cpu.voltage", s => s.HardwareSensors.CpuVoltageV),
        new("cpu_power", "CPU 封装功耗", "W", "CPU Package Power", CanonicalSensorId.CpuPower, "cpu.power", s => s.HardwareSensors.CpuPackagePowerW),
        new("cpu_cores_power", "CPU 核心功耗", "W", "CPU Cores Power", CanonicalSensorId.CpuCoresPower, "cpu.cores.power", s => s.HardwareSensors.CpuCoresPowerW),
        new("cpu_vid", "CPU VID", "V", "CPU VID", CanonicalSensorId.CpuVid, "cpu.vid", s => s.HardwareSensors.CpuVidV),
        new("cpu_bus_clock", "CPU 总线频率", "MHz", "CPU Bus Frequency", CanonicalSensorId.CpuBusClock, "cpu.bus.clock", s => s.HardwareSensors.CpuBusClockMhz),
        new("cpu_fan", "CPU 风扇", "RPM", "CPU Fan", CanonicalSensorId.CpuFan, "cpu.fan", s => s.HardwareSensors.CpuFanRpm),
        new("gpu_usage", "GPU 使用率", "%", "GPU Usage", CanonicalSensorId.GpuUsage, "gpu.usage", s => s.HardwareSensors.GpuUsagePercent),
        new("gpu_temp", "GPU 温度", "C", "GPU Temperature", CanonicalSensorId.GpuTemperature, "gpu.temperature", s => s.HardwareSensors.GpuTemperatureC),
        new("gpu_hotspot", "GPU 热点温度", "C", "GPU Hotspot", CanonicalSensorId.GpuHotspot, "gpu.hotspot", s => s.HardwareSensors.GpuHotspotTemperatureC),
        new("gpu_memory_temp", "GPU 显存温度", "C", "GPU Memory Temperature", CanonicalSensorId.GpuMemoryTemperature, "gpu.memory.temperature", s => s.HardwareSensors.GpuMemoryTemperatureC),
        new("gpu_clock", "GPU 频率", "MHz", "GPU Frequency", CanonicalSensorId.GpuClock, "gpu.clock", s => s.HardwareSensors.GpuClockMhz),
        new("gpu_memory_clock", "GPU 显存频率", "MHz", "GPU Memory Frequency", CanonicalSensorId.GpuMemoryClock, "gpu.memory.clock", s => s.HardwareSensors.GpuMemoryClockMhz),
        new("gpu_power", "GPU 功耗", "W", "GPU Power", CanonicalSensorId.GpuPower, "gpu.power", s => s.HardwareSensors.GpuPowerW),
        new("gpu_voltage", "GPU 电压", "V", "GPU Voltage", CanonicalSensorId.GpuVoltage, "gpu.voltage", s => s.HardwareSensors.GpuVoltageV),
        new("gpu_vram_used", "已用显存", "MB", "GPU VRAM Used", CanonicalSensorId.GpuVramUsed, "gpu.vram.used", s => s.HardwareSensors.GpuVramUsedMb),
        new("gpu_vram_total", "显存总量", "MB", "GPU VRAM Total", CanonicalSensorId.GpuVramTotal, "gpu.vram.total", s => s.HardwareSensors.GpuVramTotalMb),
        new("gpu_fan", "GPU 风扇", "RPM", "GPU Fan", CanonicalSensorId.GpuFan, "gpu.fan", s => s.HardwareSensors.GpuFanRpm),
        new("memory_usage", "内存使用率", "%", "Memory Usage", CanonicalSensorId.RamUsage, "ram.usage", s => Canonical(s, CanonicalSensorId.RamUsage)),
        new("memory_used", "已用内存", "MB", "Used Memory", CanonicalSensorId.RamUsed, "ram.used", s => s.System.MemoryUsedBytes / 1048576d),
        new("memory_total", "内存总量", "MB", "Total Memory", CanonicalSensorId.RamTotal, "ram.total", s => s.System.MemoryTotalBytes / 1048576d),
        new("network_download", "网络下载", "B/s", "Network Download 1", CanonicalSensorId.NetworkDownload, "network.download", s => Canonical(s, CanonicalSensorId.NetworkDownload)),
        new("network_upload", "网络上传", "B/s", "Network Upload 1", CanonicalSensorId.NetworkUpload, "network.upload", s => Canonical(s, CanonicalSensorId.NetworkUpload)),
        new("disk_read", "磁盘读取", "B/s", "Disk Read", CanonicalSensorId.DiskRead, "disk.read", s => Canonical(s, CanonicalSensorId.DiskRead)),
        new("disk_write", "磁盘写入", "B/s", "Disk Write", CanonicalSensorId.DiskWrite, "disk.write", s => Canonical(s, CanonicalSensorId.DiskWrite)),
        new("disk_temp", "磁盘温度", "C", "Disk Temperature", CanonicalSensorId.DiskTemperature, "disk.temperature", s => s.HardwareSensors.DiskTemperatureC),
        new("disk_health", "磁盘健康度", "%", "Disk Health", CanonicalSensorId.DiskHealth, "disk.health", s => s.HardwareSensors.DiskHealthPercent),
        new("motherboard_temp", "主板温度", "C", "Motherboard Temperature", CanonicalSensorId.MotherboardTemperature, "motherboard.temperature", s => s.HardwareSensors.MotherboardTemperatureC),
        new("vrm_temp", "VRM 温度", "C", "VRM Temperature", CanonicalSensorId.VrmTemperature, "vrm.temperature", s => s.HardwareSensors.VrmTemperatureC),
        new("chassis_fan", "机箱风扇", "RPM", "Chassis Fan", CanonicalSensorId.ChassisFan, "chassis.fan", s => s.HardwareSensors.ChassisFanRpm),
        new("pump", "水泵转速", "RPM", "Pump Speed", CanonicalSensorId.Pump, "pump", s => s.HardwareSensors.PumpRpm),
    ];

    public static Dictionary<string, string> ValidateBindings(Dictionary<string, string>? bindings, IReadOnlyList<RawSensorReading>? readings = null)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (bindings is null) return result;
        if (bindings.Count > Definitions.Count) throw new ArgumentException("传感器映射数量无效");
        foreach (var (key, id) in bindings)
        {
            var definition = Definitions.FirstOrDefault(d => d.Key == key) ?? throw new ArgumentException("未知设备监控字段：" + key);
            if (string.IsNullOrEmpty(id)) continue;
            if (id.Length > 1024 || id.Any(char.IsControl)) throw new ArgumentException("传感器标识无效");
            if (id == "auto:" + key) continue;
            if (id.StartsWith("auto:", StringComparison.Ordinal)) throw new ArgumentException("自动汇总只能用于对应字段");
            var reading = readings?.FirstOrDefault(r => r.Id == id);
            if (reading is not null && reading.Unit != definition.Unit) throw new ArgumentException("传感器单位与设备字段不匹配：" + key);
            result[key] = id;
        }
        return result;
    }

    public static PerformanceMonitorSnapshot Apply(PerformanceMonitorSnapshot snapshot, PerformanceMonitorSettings settings)
    {
        var catalog = (snapshot.Readings ?? snapshot.HardwareSensors.Readings ?? []).ToList();
        var mappings = new List<SensorMappingValue>();
        var values = snapshot.Sensors.Where(s => s.Id == (byte)CanonicalSensorId.CpuCoreUsage).ToList();
        foreach (var definition in Definitions)
        {
            var baseline = definition.Read(snapshot);
            if (baseline.HasValue && !double.IsFinite(baseline.Value)) baseline = null;
            var existing = snapshot.Sensors.FirstOrDefault(s => s.Id == (byte)definition.CanonicalId);
            var source = existing?.Source is "pdh" or "windows-native" or "pdh+wmi" or "wmi" ? existing.Source : "automatic";
            var automatic = new RawSensorReading("auto:" + definition.Key, "automatic", "自动汇总", "Summary", definition.Label,
                definition.Unit, baseline, definition.Unit, source, snapshot.Timestamp);
            catalog.Add(automatic);
            var manual = settings.Bindings.TryGetValue(definition.Key, out var sensorId);
            var selected = manual ? catalog.FirstOrDefault(r => r.Id == sensorId) : automatic;
            // Each provider invalidates stale samples using its actual polling interval.
            var valid = selected is not null && selected.Unit == definition.Unit && selected.Value.HasValue &&
                double.IsFinite(selected.Value.Value);
            var value = valid ? selected!.Value : null;
            var status = !manual ? value.HasValue ? "automatic" : "unavailable" : selected is null ? "missing" : selected.Unit != definition.Unit ? "unit-mismatch" : value.HasValue ? "mapped" : "unavailable";
            mappings.Add(new(definition.Key, definition.Label, definition.Unit, manual ? sensorId : null, value, selected?.Source ?? "unavailable", status));
            if (value.HasValue)
            {
                // The existing canonical RAM protocol is bytes; mapping controls use MB.
                var canonicalValue = definition.Key is "memory_used" or "memory_total" ? value.Value * 1048576d : value.Value;
                var canonicalUnit = definition.Key is "memory_used" or "memory_total" ? "B" : definition.Unit;
                values.Add(new((byte)definition.CanonicalId, definition.CanonicalKey, canonicalValue, canonicalUnit, selected!.Source, selected.Timestamp, manual ? sensorId : null));
            }
        }
        var byKey = mappings.ToDictionary(m => m.Key);
        double? V(string key) => byKey[key].Value;
        var h = snapshot.HardwareSensors;
        h = h with
        {
            CpuClockMhz = V("cpu_clock"), CpuTemperatureC = V("cpu_temp"), CpuTemperatureMaxC = V("cpu_temp_max"),
            CpuCoreAverageTemperatureC = V("cpu_core_average_temp"), CpuVoltageV = V("cpu_voltage"), CpuPackagePowerW = V("cpu_power"),
            CpuCoresPowerW = V("cpu_cores_power"), CpuVidV = V("cpu_vid"), CpuBusClockMhz = V("cpu_bus_clock"), CpuFanRpm = V("cpu_fan"),
            GpuUsagePercent = V("gpu_usage"), GpuClockMhz = V("gpu_clock"), GpuTemperatureC = V("gpu_temp"), GpuFanRpm = V("gpu_fan"),
            GpuHotspotTemperatureC = V("gpu_hotspot"), GpuMemoryTemperatureC = V("gpu_memory_temp"), GpuPowerW = V("gpu_power"),
            GpuVoltageV = V("gpu_voltage"), GpuMemoryClockMhz = V("gpu_memory_clock"), GpuVramUsedMb = V("gpu_vram_used"),
            GpuVramTotalMb = V("gpu_vram_total"), MotherboardTemperatureC = V("motherboard_temp"), VrmTemperatureC = V("vrm_temp"),
            DiskTemperatureC = V("disk_temp"), DiskHealthPercent = V("disk_health"), ChassisFanRpm = V("chassis_fan"), PumpRpm = V("pump"),
            Readings = catalog,
        };
        // Explicit mappings must not retain the name of a different, automatically selected GPU.
        var gpuOrigins = mappings.Where(m => m.Key.StartsWith("gpu_", StringComparison.Ordinal) && m.Value.HasValue)
            .Select(m => m.SensorId is null ? h.GpuHardwareId : catalog.FirstOrDefault(r => r.Id == m.SensorId)?.HardwareId)
            .Where(id => id is not null).Distinct().ToArray();
        if (gpuOrigins.Length == 1)
        {
            var selectedGpu = catalog.FirstOrDefault(r => r.HardwareId == gpuOrigins[0] && r.HardwareType.StartsWith("Gpu", StringComparison.OrdinalIgnoreCase));
            if (selectedGpu is not null) h = h with { GpuName = HardwareNameFormatter.FormatGpu(selectedGpu.HardwareName), GpuHardwareId = selectedGpu.HardwareId };
        }
        else if (gpuOrigins.Length > 1) h = h with { GpuName = "Multiple GPUs", GpuHardwareId = null };
        return snapshot with { Hardware = snapshot.Hardware with { GpuName = h.GpuName, GpuShortName = h.GpuName },
            HardwareSensors = h, Sensors = values, Readings = catalog, Mappings = mappings,
            Settings = new PerformanceMonitorSettings { SampleIntervalMs = settings.SampleIntervalMs, SmoothingFactor = settings.SmoothingFactor,
                EnableElevatedSensorHost = settings.EnableElevatedSensorHost, EnableHwInfoSharedMemory = settings.EnableHwInfoSharedMemory,
                Bindings = new(settings.Bindings, StringComparer.Ordinal) } };
    }

    public static string WireValue(Definition definition, SensorMappingValue mapping)
    {
        var unit = definition.Unit;
        var value = mapping.Value;
        if (definition.Key.StartsWith("network_", StringComparison.Ordinal)) { value /= 1024; unit = "KB/s"; }
        else if (definition.Key is "disk_read" or "disk_write") { value /= 1048576; unit = "MB/s"; }
        var format = unit is "RPM" or "MHz" or "MB" ? "F0" : unit == "V" ? "F3" : unit is "KB/s" or "MB/s" ? "F2" : "F1";
        // Send an explicit unavailable marker so receivers can clear a disconnected sensor.
        return definition.Key + "|" + definition.WireLabel + " " + (value.HasValue ? value.Value.ToString(format, CultureInfo.InvariantCulture) : "--") + " " + unit;
    }
}
