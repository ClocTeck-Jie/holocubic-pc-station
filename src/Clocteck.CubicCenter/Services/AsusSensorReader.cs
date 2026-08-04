using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Clocteck.CubicCenter.Services;

// Read-only compatibility client for the public ASUS ATKACPI device interface.
// It never changes performance modes, fan curves, power limits or other firmware settings.
public sealed class AsusSensorReader : IDisposable
{
    private const uint GenericRead = 0x80000000;
    private const uint GenericWrite = 0x40000000;
    private const uint ShareRead = 0x00000001;
    private const uint ShareWrite = 0x00000002;
    private const uint OpenExisting = 3;
    private const uint IoctlAsusAcpi = 0x0022240C;
    private const uint MethodDeviceStatus = 0x53545344;
    private const uint CpuTemperatureId = 0x00120094;
    private const uint CpuFanId = 0x00110013;
    private const uint GpuFanId = 0x00110014;
    private readonly object _sync = new();
    private readonly SafeFileHandle _handle;

    public bool Available => !_handle.IsInvalid && !_handle.IsClosed;

    public AsusSensorReader()
    {
        _handle = CreateFile(
            @"\\.\ATKACPI",
            GenericRead | GenericWrite,
            ShareRead | ShareWrite,
            IntPtr.Zero,
            OpenExisting,
            0,
            IntPtr.Zero);
    }

    public AsusSensorSnapshot Read()
    {
        if (!Available) return new(false, null, null, null);
        lock (_sync)
        {
            var temperature = ReadDevice(CpuTemperatureId);
            var cpuFan = ReadFan(CpuFanId);
            var gpuFan = ReadFan(GpuFanId);
            return new(
                true,
                temperature is > 0 and < 130 ? temperature : null,
                cpuFan,
                gpuFan);
        }
    }

    private double? ReadFan(uint id)
    {
        var raw = ReadDevice(id);
        if (!raw.HasValue) return null;
        var hundredsOfRpm = raw.Value & 0xFFFF;
        return hundredsOfRpm is > 0 and <= 120 ? hundredsOfRpm * 100d : null;
    }

    private int? ReadDevice(uint id)
    {
        var input = new byte[16];
        BitConverter.GetBytes(MethodDeviceStatus).CopyTo(input, 0);
        BitConverter.GetBytes(8u).CopyTo(input, 4);
        BitConverter.GetBytes(id).CopyTo(input, 8);
        var output = new byte[16];
        if (!DeviceIoControl(_handle, IoctlAsusAcpi, input, (uint)input.Length, output, (uint)output.Length, out _, IntPtr.Zero)) return null;
        var value = BitConverter.ToInt32(output, 0) - 65536;
        return value >= 0 ? value : null;
    }

    public void Dispose() => _handle.Dispose();

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(
        string fileName,
        uint desiredAccess,
        uint shareMode,
        IntPtr securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        IntPtr templateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(
        SafeFileHandle device,
        uint controlCode,
        byte[] input,
        uint inputSize,
        byte[] output,
        uint outputSize,
        out uint bytesReturned,
        IntPtr overlapped);
}

public sealed record AsusSensorSnapshot(bool Available, double? CpuTemperatureC, double? CpuFanRpm, double? GpuFanRpm);
