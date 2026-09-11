using System.Management;
using Clocteck.CubicCenter.Core;
using Clocteck.CubicCenter.Models;

namespace Clocteck.CubicCenter.Services;

internal sealed class WmiHardwareInfoProvider : ISensorProvider
{
    private readonly AppLog _log;
    private ComputerHardwareInfo _snapshot = Empty();

    public WmiHardwareInfoProvider(AppLog log)
    {
        _log = log;
        Refresh();
    }

    public bool Available { get; private set; }
    public string Status { get; private set; } = "WMI 尚未读取";
    public ComputerHardwareInfo Snapshot => _snapshot;
    public string ProviderId => "wmi";
    public bool IsAvailable => Available;
    public string ProviderStatus => Status;

    public void Refresh()
    {
        try
        {
            string? cpuName = null;
            int? cpuCores = null;
            int? cpuLogical = null;
            uint? cpuClock = null;
            using (var searcher = new ManagementObjectSearcher(
                       "SELECT Name,NumberOfCores,NumberOfLogicalProcessors,MaxClockSpeed FROM Win32_Processor"))
            {
                foreach (ManagementObject item in searcher.Get())
                {
                    cpuName ??= Text(item["Name"]);
                    cpuCores = Sum(cpuCores, Int32(item["NumberOfCores"]));
                    cpuLogical = Sum(cpuLogical, Int32(item["NumberOfLogicalProcessors"]));
                    cpuClock ??= UInt32(item["MaxClockSpeed"]);
                }
            }

            var gpuCandidates = new List<(string Name, ulong? Memory)>();
            using (var searcher = new ManagementObjectSearcher("SELECT Name,AdapterRAM FROM Win32_VideoController"))
            {
                foreach (ManagementObject item in searcher.Get())
                {
                    var name = Text(item["Name"]);
                    if (!string.IsNullOrWhiteSpace(name)) gpuCandidates.Add((name, UInt64(item["AdapterRAM"])));
                }
            }
            var gpu = gpuCandidates.OrderByDescending(candidate => GpuScore(candidate.Name)).FirstOrDefault();

            string? motherboard = null;
            using (var searcher = new ManagementObjectSearcher("SELECT Manufacturer,Product FROM Win32_BaseBoard"))
            {
                foreach (ManagementObject item in searcher.Get())
                {
                    motherboard = Join(Text(item["Manufacturer"]), Text(item["Product"]));
                    if (!string.IsNullOrWhiteSpace(motherboard)) break;
                }
            }

            ulong? totalMemory = null;
            using (var searcher = new ManagementObjectSearcher("SELECT TotalPhysicalMemory FROM Win32_ComputerSystem"))
            {
                foreach (ManagementObject item in searcher.Get())
                {
                    totalMemory = UInt64(item["TotalPhysicalMemory"]);
                    break;
                }
            }

            string? windowsName = null;
            string? windowsVersion = null;
            using (var searcher = new ManagementObjectSearcher("SELECT Caption,Version FROM Win32_OperatingSystem"))
            {
                foreach (ManagementObject item in searcher.Get())
                {
                    windowsName = Text(item["Caption"]);
                    windowsVersion = Text(item["Version"]);
                    break;
                }
            }

            var disks = new List<DiskHardwareInfo>();
            using (var searcher = new ManagementObjectSearcher("SELECT Model,Size FROM Win32_DiskDrive"))
            {
                foreach (ManagementObject item in searcher.Get())
                {
                    var model = Text(item["Model"]);
                    if (!string.IsNullOrWhiteSpace(model)) disks.Add(new(model, UInt64(item["Size"])));
                }
            }

            _snapshot = new ComputerHardwareInfo(
                cpuName,
                HardwareNameFormatter.FormatCpu(cpuName),
                cpuCores,
                cpuLogical,
                cpuClock,
                string.IsNullOrWhiteSpace(gpu.Name) ? null : gpu.Name,
                string.IsNullOrWhiteSpace(gpu.Name) ? null : HardwareNameFormatter.FormatGpu(gpu.Name),
                gpu.Memory,
                totalMemory,
                motherboard,
                windowsName,
                windowsVersion,
                disks,
                DateTimeOffset.Now);
            Available = !string.IsNullOrWhiteSpace(cpuName) || !string.IsNullOrWhiteSpace(gpu.Name);
            Status = Available ? $"WMI 已读取：{disks.Count} 块磁盘" : "WMI 未返回硬件信息";
            _log.Info("PC 监控", Status);
        }
        catch (Exception error) when (error is ManagementException or UnauthorizedAccessException or InvalidOperationException)
        {
            Available = false;
            Status = "WMI 读取失败：" + error.Message;
            _log.Warn("PC 监控", Status);
        }
    }

    private static ComputerHardwareInfo Empty() => new(
        null, null, null, null, null, null, null, null, null, null, null, null, [], DateTimeOffset.Now);

    private static string? Text(object? value) => value?.ToString()?.Trim() is { Length: > 0 } text ? text : null;
    private static int? Int32(object? value) => int.TryParse(value?.ToString(), out var parsed) ? parsed : null;
    private static uint? UInt32(object? value) => uint.TryParse(value?.ToString(), out var parsed) ? parsed : null;
    private static ulong? UInt64(object? value) => ulong.TryParse(value?.ToString(), out var parsed) ? parsed : null;
    private static int? Sum(int? left, int? right) => right.HasValue ? (left ?? 0) + right.Value : left;
    private static string? Join(string? left, string? right) => string.Join(' ', new[] { left, right }.Where(value => !string.IsNullOrWhiteSpace(value)));

    private static int GpuScore(string name)
    {
        var score = name.Contains("RTX", StringComparison.OrdinalIgnoreCase) || name.Contains("GTX", StringComparison.OrdinalIgnoreCase) ? 500
            : name.Contains("Radeon RX", StringComparison.OrdinalIgnoreCase) ? 450
            : name.Contains("Arc", StringComparison.OrdinalIgnoreCase) ? 400
            : 100;
        if (name.Contains("Microsoft", StringComparison.OrdinalIgnoreCase) || name.Contains("Remote", StringComparison.OrdinalIgnoreCase)) score -= 1000;
        return score;
    }
}
