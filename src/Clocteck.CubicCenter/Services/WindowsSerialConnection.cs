using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Clocteck.CubicCenter.Services;

internal sealed class WindowsSerialConnection : IDisposable
{
    private const uint GenericRead = 0x80000000;
    private const uint GenericWrite = 0x40000000;
    private const uint OpenExisting = 3;
    private const uint FileAttributeNormal = 0x00000080;
    private const uint FileFlagOverlapped = 0x40000000;

    private readonly object _writeSync = new();
    private readonly CancellationTokenSource _readCancellation = new();
    private readonly SafeFileHandle _handle;
    private readonly FileStream _stream;
    private Task? _readTask;
    private bool _disposed;

    public WindowsSerialConnection(string portName, int baudRate)
    {
        PortName = portName;
        BaudRate = baudRate;

        var devicePath = portName.StartsWith("\\\\.\\", StringComparison.Ordinal)
            ? portName
            : "\\\\.\\" + portName;
        _handle = CreateFile(
            devicePath,
            GenericRead | GenericWrite,
            0,
            IntPtr.Zero,
            OpenExisting,
            FileAttributeNormal | FileFlagOverlapped,
            IntPtr.Zero);
        if (_handle.IsInvalid)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"无法打开串口 {portName}");
        }

        try
        {
            ConfigurePort(_handle, baudRate);
            _stream = new FileStream(_handle, FileAccess.ReadWrite, 4096, isAsync: true);
        }
        catch
        {
            _handle.Dispose();
            throw;
        }
    }

    public string PortName { get; }
    public int BaudRate { get; }
    public bool IsOpen => !_disposed && !_handle.IsInvalid && !_handle.IsClosed;

    public event EventHandler<SerialBytesReceivedEventArgs>? BytesReceived;
    public event EventHandler<SerialConnectionErrorEventArgs>? ErrorReceived;

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _readTask ??= ReadLoopAsync();
    }

    public void Write(ReadOnlySpan<byte> bytes)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        lock (_writeSync)
        {
            _stream.Write(bytes);
            _stream.Flush();
        }
    }

    private async Task ReadLoopAsync()
    {
        var buffer = new byte[4096];
        try
        {
            while (!_readCancellation.IsCancellationRequested)
            {
                var count = await _stream.ReadAsync(buffer, _readCancellation.Token).ConfigureAwait(false);
                if (count == 0)
                {
                    await Task.Delay(10, _readCancellation.Token).ConfigureAwait(false);
                    continue;
                }
                BytesReceived?.Invoke(this, new SerialBytesReceivedEventArgs(buffer[..count].ToArray()));
            }
        }
        catch (OperationCanceledException) when (_readCancellation.IsCancellationRequested)
        {
        }
        catch (ObjectDisposedException) when (_readCancellation.IsCancellationRequested)
        {
        }
        catch (Exception error)
        {
            ErrorReceived?.Invoke(this, new SerialConnectionErrorEventArgs(error));
        }
    }

    private static void ConfigurePort(SafeFileHandle handle, int baudRate)
    {
        var dcb = new Dcb { Length = (uint)Marshal.SizeOf<Dcb>() };
        if (!GetCommState(handle, ref dcb))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "无法读取串口配置");
        }

        dcb.BaudRate = (uint)baudRate;
        dcb.Flags = 0x00000001; // fBinary; DTR and RTS disabled in one SetCommState call.
        dcb.ByteSize = 8;
        dcb.Parity = 0;
        dcb.StopBits = 0;
        dcb.XonChar = 0x11;
        dcb.XoffChar = 0x13;
        if (!SetCommState(handle, ref dcb))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "无法应用串口配置");
        }

        var timeouts = new CommTimeouts
        {
            ReadIntervalTimeout = 1,
            ReadTotalTimeoutMultiplier = 0,
            ReadTotalTimeoutConstant = 50,
            WriteTotalTimeoutMultiplier = 0,
            WriteTotalTimeoutConstant = 500,
        };
        if (!SetCommTimeouts(handle, ref timeouts))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "无法设置串口超时");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _readCancellation.Cancel();
        try { _stream.Dispose(); } catch { }
        _readCancellation.Dispose();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Dcb
    {
        public uint Length;
        public uint BaudRate;
        public uint Flags;
        public ushort Reserved;
        public ushort XonLimit;
        public ushort XoffLimit;
        public byte ByteSize;
        public byte Parity;
        public byte StopBits;
        public byte XonChar;
        public byte XoffChar;
        public byte ErrorChar;
        public byte EofChar;
        public byte EventChar;
        public ushort Reserved1;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CommTimeouts
    {
        public uint ReadIntervalTimeout;
        public uint ReadTotalTimeoutMultiplier;
        public uint ReadTotalTimeoutConstant;
        public uint WriteTotalTimeoutMultiplier;
        public uint WriteTotalTimeoutConstant;
    }

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
    private static extern bool GetCommState(SafeFileHandle file, ref Dcb dcb);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetCommState(SafeFileHandle file, ref Dcb dcb);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetCommTimeouts(SafeFileHandle file, ref CommTimeouts timeouts);
}

internal sealed class SerialBytesReceivedEventArgs(byte[] bytes) : EventArgs
{
    public byte[] Bytes { get; } = bytes;
}

internal sealed class SerialConnectionErrorEventArgs(Exception error) : EventArgs
{
    public Exception Error { get; } = error;
}
