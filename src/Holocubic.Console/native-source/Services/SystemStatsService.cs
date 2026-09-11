using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using Clocteck.CubicCenter.Models;

namespace Clocteck.CubicCenter.Services;

public sealed class SystemStatsService
{
    private readonly object _sync = new();
    private ulong _lastIdle;
    private ulong _lastKernel;
    private ulong _lastUser;

    public SystemStats GetSnapshot()
    {
        lock (_sync)
        {
            var cpu = ReadCpuPercent();
            var memory = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
            _ = GlobalMemoryStatusEx(ref memory);
            long received = 0;
            long sent = 0;
            foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces().Where(adapter => adapter.OperationalStatus == OperationalStatus.Up))
            {
                try
                {
                    var stats = adapter.GetIPv4Statistics();
                    received += stats.BytesReceived;
                    sent += stats.BytesSent;
                }
                catch
                {
                    // A virtual adapter may disappear while being queried.
                }
            }

            return new SystemStats(
                Math.Round(cpu, 1),
                memory.TotalPhysical - memory.AvailablePhysical,
                memory.TotalPhysical,
                received,
                sent,
                Environment.TickCount64 / 1000,
                DateTimeOffset.Now);
        }
    }

    private double ReadCpuPercent()
    {
        if (!GetSystemTimes(out var idle, out var kernel, out var user)) return 0;
        var idleValue = idle.ToUInt64();
        var kernelValue = kernel.ToUInt64();
        var userValue = user.ToUInt64();
        if (_lastKernel == 0)
        {
            _lastIdle = idleValue;
            _lastKernel = kernelValue;
            _lastUser = userValue;
            return 0;
        }
        var idleDelta = idleValue - _lastIdle;
        var totalDelta = kernelValue - _lastKernel + userValue - _lastUser;
        _lastIdle = idleValue;
        _lastKernel = kernelValue;
        _lastUser = userValue;
        return totalDelta == 0 ? 0 : Math.Clamp((totalDelta - idleDelta) * 100d / totalDelta, 0, 100);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileTime
    {
        public uint Low;
        public uint High;
        public readonly ulong ToUInt64() => ((ulong)High << 32) | Low;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MemoryStatus
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhysical;
        public ulong AvailablePhysical;
        public ulong TotalPageFile;
        public ulong AvailablePageFile;
        public ulong TotalVirtual;
        public ulong AvailableVirtual;
        public ulong AvailableExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(out FileTime idleTime, out FileTime kernelTime, out FileTime userTime);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatus buffer);
}
