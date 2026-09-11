using System.Runtime.InteropServices;
using Clocteck.CubicCenter.Models;

namespace Clocteck.CubicCenter.Services;

internal sealed class PdhPerformanceProvider : IDisposable, ISensorProvider
{
    private const uint PdhFmtDouble = 0x00000200;
    private const uint ErrorSuccess = 0;
    private const uint PdhMoreData = 0x800007D2;
    private readonly object _sync = new();
    private readonly List<CounterHandle> _cpuCores = [];
    private readonly List<CounterHandle> _networkReceive = [];
    private readonly List<CounterHandle> _networkSend = [];
    private readonly List<CounterHandle> _physicalDiskRead = [];
    private readonly List<CounterHandle> _physicalDiskWrite = [];
    private IntPtr _query;
    private IntPtr _cpuTotal;
    private IntPtr _memoryAvailable;
    private IntPtr _diskRead;
    private IntPtr _diskWrite;
    private bool _disposed;
    private int _addedCounters;
    private int _failedCounters;
    private string _initializationStatus = "PDH 尚未初始化";

    public PdhPerformanceProvider()
    {
        try
        {
            Check(PdhOpenQueryW(null, IntPtr.Zero, out _query), "PdhOpenQuery");
            _cpuTotal = TryAdd(@"\Processor(_Total)\% Processor Time");
            _memoryAvailable = TryAdd(@"\Memory\Available Bytes");
            _diskRead = TryAdd(@"\PhysicalDisk(_Total)\Disk Read Bytes/sec");
            _diskWrite = TryAdd(@"\PhysicalDisk(_Total)\Disk Write Bytes/sec");
            AddExpanded(@"\Processor(*)\% Processor Time", _cpuCores, ExtractInstance);
            AddExpanded(@"\Network Interface(*)\Bytes Received/sec", _networkReceive, ExtractInstance);
            AddExpanded(@"\Network Interface(*)\Bytes Sent/sec", _networkSend, ExtractInstance);
            AddExpanded(@"\PhysicalDisk(*)\Disk Read Bytes/sec", _physicalDiskRead, ExtractInstance);
            AddExpanded(@"\PhysicalDisk(*)\Disk Write Bytes/sec", _physicalDiskWrite, ExtractInstance);
            Available = _addedCounters > 0;
            _initializationStatus = Available
                ? $"PDH 已初始化：{_cpuCores.Count} 个处理器实例，{Math.Max(_networkReceive.Count, _networkSend.Count)} 个网络实例，{Math.Max(_physicalDiskRead.Count, _physicalDiskWrite.Count)} 个磁盘实例" +
                  (_failedCounters > 0 ? $"；{_failedCounters} 项计数器不可用" : "")
                : "PDH 没有可用的计数器";
            Status = _initializationStatus;
            // Rate counters need a first sample. A transient collection failure can recover on Read.
            if (Available) _ = PdhCollectQueryData(_query);
        }
        catch (Exception error)
        {
            Available = false;
            Status = "PDH 初始化失败：" + error.Message;
            Dispose();
        }
    }

    public bool Available { get; private set; }
    public string Status { get; private set; } = "PDH 尚未初始化";
    public string ProviderId => "pdh";
    public bool IsAvailable => Available;
    public string ProviderStatus => Status;

    public PdhPerformanceSnapshot Read()
    {
        lock (_sync)
        {
            var now = DateTimeOffset.Now;
            if (_query == IntPtr.Zero || _disposed) return PdhPerformanceSnapshot.Empty(Status, now);
            if (!Available) return CreateSnapshot(now, false);
            try
            {
                Check(PdhCollectQueryData(_query), "PdhCollectQueryData");
                Status = _initializationStatus;
                return CreateSnapshot(now, true);
            }
            catch (Exception error)
            {
                Status = "PDH 采样失败：" + error.Message;
                return CreateSnapshot(now, false);
            }
        }
    }

    private PdhPerformanceSnapshot CreateSnapshot(DateTimeOffset now, bool collected)
    {
        double? Sample(IntPtr handle) => collected ? ReadValue(handle) : null;
        var readings = new List<RawSensorReading>();
        var cores = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        var cpu = ClampNullablePercent(Sample(_cpuTotal));
        var memory = NonNegative(Sample(_memoryAvailable));
        var diskRead = NonNegative(Sample(_diskRead));
        var diskWrite = NonNegative(Sample(_diskWrite));
        readings.Add(Reading("Cpu", "_Total", "usage", "CPU Total", "CPU Usage", "Load", cpu, "%", now));
        readings.Add(Reading("Memory", "system", "available", "System Memory", "Available Memory", "SmallData", memory / (1024 * 1024), "MB", now));
        readings.Add(Reading("Storage", "_Total", "read", "All Physical Disks", "Disk Read", "Throughput", diskRead, "B/s", now));
        readings.Add(Reading("Storage", "_Total", "write", "All Physical Disks", "Disk Write", "Throughput", diskWrite, "B/s", now));
        foreach (var counter in _cpuCores)
        {
            var value = ClampNullablePercent(Sample(counter.Handle));
            if (value.HasValue) cores[counter.Instance] = value.Value;
            readings.Add(Reading("Cpu", counter.Instance, "usage", "CPU " + counter.Instance, "CPU Core Usage", "Load", value, "%", now));
        }
        double? SampleInstances(IEnumerable<CounterHandle> counters, string hardwareType, string metric, string name)
        {
            var values = new List<double?>();
            foreach (var counter in counters)
            {
                var value = NonNegative(Sample(counter.Handle));
                values.Add(value);
                readings.Add(Reading(hardwareType, counter.Instance, metric, counter.Instance, name, "Throughput", value, "B/s", now));
            }
            return Sum(values);
        }
        var receive = SampleInstances(_networkReceive, "Network", "receive", "Received Data Rate");
        var send = SampleInstances(_networkSend, "Network", "send", "Sent Data Rate");
        var perDiskRead = SampleInstances(_physicalDiskRead, "Storage", "read", "Disk Read");
        var perDiskWrite = SampleInstances(_physicalDiskWrite, "Storage", "write", "Disk Write");
        return new(collected, Status, cpu, memory, diskRead ?? perDiskRead, diskWrite ?? perDiskWrite,
            receive, send, cores, now, readings);
    }

    private static RawSensorReading Reading(string hardwareType, string instance, string metric,
        string hardwareName, string name, string sensorType, double? value, string unit, DateTimeOffset timestamp)
    {
        var hardwareId = $"pdh:{hardwareType}:{Uri.EscapeDataString(instance)}";
        return new($"{hardwareId}:{metric}", hardwareId, hardwareName, hardwareType, name, sensorType,
            value, unit, "pdh", timestamp);
    }

    private IntPtr TryAdd(string path)
    {
        try
        {
            var status = PdhAddEnglishCounterW(_query, path, IntPtr.Zero, out var counter);
            if (status == ErrorSuccess && counter != IntPtr.Zero)
            {
                _addedCounters++;
                return counter;
            }
        }
        catch { }
        _failedCounters++;
        return IntPtr.Zero;
    }

    private void AddExpanded(string wildcard, ICollection<CounterHandle> destination, Func<string, string> instanceSelector)
    {
        try
        {
            foreach (var path in Expand(wildcard))
            {
                var instance = instanceSelector(path);
                if (instance.Equals("_Total", StringComparison.OrdinalIgnoreCase)) continue;
                destination.Add(new(TryAdd(path), instance));
            }
        }
        catch { _failedCounters++; }
    }

    private static string[] Expand(string wildcard)
    {
        uint size = 0;
        var result = PdhExpandWildCardPathW(null, wildcard, null, ref size, 0);
        if (result != PdhMoreData && result != ErrorSuccess) return [];
        // MULTI_SZ contains embedded NULs: StringBuilder marshaling can truncate it after the first path.
        for (var attempt = 0; attempt < 3 && size > 0 && size <= 2_000_000; attempt++)
        {
            var buffer = new char[checked((int)size)];
            result = PdhExpandWildCardPathW(null, wildcard, buffer, ref size, 0);
            if (result == PdhMoreData) continue; // The instance set can change between calls.
            if (result != ErrorSuccess) return [];
            return new string(buffer, 0, (int)Math.Min(size, (uint)buffer.Length))
                .Split('\0', StringSplitOptions.RemoveEmptyEntries);
        }
        return [];
    }

    private static string ExtractInstance(string path)
    {
        var open = path.IndexOf('(');
        var close = open >= 0 ? path.LastIndexOf(")\\", StringComparison.Ordinal) : -1;
        return open >= 0 && close > open ? path[(open + 1)..close] : path;
    }

    private static double? ReadValue(IntPtr counter)
    {
        if (counter == IntPtr.Zero || PdhGetFormattedCounterValue(counter, PdhFmtDouble, out _, out var value) != ErrorSuccess) return null;
        if (value.Status > 1 || double.IsNaN(value.DoubleValue) || double.IsInfinity(value.DoubleValue)) return null;
        return value.DoubleValue;
    }

    private static double? Sum(IEnumerable<double?> values)
    {
        double? sum = null;
        foreach (var value in values)
            if (value.HasValue) sum = (sum ?? 0) + value.Value;
        return sum;
    }

    private static double ClampPercent(double value) => Math.Clamp(value, 0, 100);
    private static double? ClampNullablePercent(double? value) => value.HasValue ? ClampPercent(value.Value) : null;
    private static double? NonNegative(double? value) => value.HasValue && value.Value >= 0 ? value.Value : null;

    private static void Check(uint status, string operation)
    {
        if (status != ErrorSuccess) throw new InvalidOperationException($"{operation} 失败：0x{status:X8}");
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
            if (_query != IntPtr.Zero) _ = PdhCloseQuery(_query);
            _query = IntPtr.Zero;
            Available = false;
        }
    }

    private sealed record CounterHandle(IntPtr Handle, string Instance);

    [StructLayout(LayoutKind.Explicit)]
    private struct PdhFormattedCounterValue
    {
        [FieldOffset(0)] public uint Status;
        [FieldOffset(8)] public double DoubleValue;
    }

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern uint PdhOpenQueryW(string? dataSource, IntPtr userData, out IntPtr query);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern uint PdhAddEnglishCounterW(IntPtr query, string fullCounterPath, IntPtr userData, out IntPtr counter);

    [DllImport("pdh.dll")]
    private static extern uint PdhCollectQueryData(IntPtr query);

    [DllImport("pdh.dll")]
    private static extern uint PdhGetFormattedCounterValue(IntPtr counter, uint format, out uint type, out PdhFormattedCounterValue value);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern uint PdhExpandWildCardPathW(string? dataSource, string wildcardPath,
        [Out, MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.U2)] char[]? expandedPathList,
        ref uint pathListLength, uint flags);

    [DllImport("pdh.dll")]
    private static extern uint PdhCloseQuery(IntPtr query);
}

internal sealed record PdhPerformanceSnapshot(
    bool Available,
    string Status,
    double? CpuPercent,
    double? MemoryAvailableBytes,
    double? DiskReadBytesPerSecond,
    double? DiskWriteBytesPerSecond,
    double? NetworkReceiveBytesPerSecond,
    double? NetworkSendBytesPerSecond,
    IReadOnlyDictionary<string, double> CpuCoreUsagePercent,
    DateTimeOffset Timestamp,
    IReadOnlyList<RawSensorReading>? Readings = null)
{
    public static PdhPerformanceSnapshot Empty(string status, DateTimeOffset timestamp) =>
        new(false, status, null, null, null, null, null, null, new Dictionary<string, double>(), timestamp);
}
