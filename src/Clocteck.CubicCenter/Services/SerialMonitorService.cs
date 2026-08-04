using System.IO.Ports;
using System.Text;

namespace Clocteck.CubicCenter.Services;

public sealed class SerialMonitorService : IDisposable
{
    private readonly object _sync = new();
    private SerialPort? _port;
    private string? _lastError;
    private long _receivedBytes;
    private DateTimeOffset? _connectedAt;

    public event EventHandler<SerialMonitorSnapshot>? StatusChanged;
    public event EventHandler<SerialTextChunk>? TextReceived;

    public SerialMonitorSnapshot Snapshot()
    {
        lock (_sync)
        {
            return new SerialMonitorSnapshot(
                GetPorts(),
                _port?.PortName,
                _port?.BaudRate ?? 115200,
                _port?.IsOpen == true,
                _lastError,
                _receivedBytes,
                _connectedAt);
        }
    }

    public SerialMonitorSnapshot Refresh()
    {
        var snapshot = Snapshot();
        StatusChanged?.Invoke(this, snapshot);
        return snapshot;
    }

    public SerialMonitorSnapshot Connect(string portName, int baudRate)
    {
        portName = (portName ?? string.Empty).Trim();
        if (!GetPorts().Contains(portName, StringComparer.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"串口不存在：{portName}");
        }
        if (baudRate is < 1200 or > 3_000_000)
        {
            throw new ArgumentOutOfRangeException(nameof(baudRate), "波特率超出支持范围。");
        }

        lock (_sync)
        {
            CloseLocked();
            var port = new SerialPort(portName, baudRate, Parity.None, 8, StopBits.One)
            {
                Encoding = new UTF8Encoding(false, false),
                DtrEnable = false,
                RtsEnable = false,
                ReadTimeout = 500,
                WriteTimeout = 500,
            };
            port.DataReceived += OnDataReceived;
            port.ErrorReceived += OnErrorReceived;
            try
            {
                port.Open();
                _port = port;
                _lastError = null;
                _receivedBytes = 0;
                _connectedAt = DateTimeOffset.Now;
            }
            catch
            {
                port.DataReceived -= OnDataReceived;
                port.ErrorReceived -= OnErrorReceived;
                port.Dispose();
                throw;
            }
        }

        return PublishStatus();
    }

    public SerialMonitorSnapshot Disconnect()
    {
        lock (_sync)
        {
            CloseLocked();
            _lastError = null;
        }
        return PublishStatus();
    }

    private void OnDataReceived(object sender, SerialDataReceivedEventArgs args)
    {
        string text;
        long receivedBytes;
        lock (_sync)
        {
            if (_port is null || !_port.IsOpen) return;
            try
            {
                var pendingBytes = _port.BytesToRead;
                text = _port.ReadExisting();
                _receivedBytes += pendingBytes;
                receivedBytes = _receivedBytes;
            }
            catch (Exception error)
            {
                _lastError = error.Message;
                PublishStatus();
                return;
            }
        }
        if (!string.IsNullOrEmpty(text)) TextReceived?.Invoke(this, new SerialTextChunk(DateTimeOffset.Now, text, receivedBytes));
    }

    private void OnErrorReceived(object sender, SerialErrorReceivedEventArgs args)
    {
        lock (_sync) _lastError = args.EventType.ToString();
        PublishStatus();
    }

    private SerialMonitorSnapshot PublishStatus()
    {
        var snapshot = Snapshot();
        StatusChanged?.Invoke(this, snapshot);
        return snapshot;
    }

    private void CloseLocked()
    {
        if (_port is null) return;
        _port.DataReceived -= OnDataReceived;
        _port.ErrorReceived -= OnErrorReceived;
        try { if (_port.IsOpen) _port.Close(); } catch { }
        _port.Dispose();
        _port = null;
        _connectedAt = null;
    }

    private static string[] GetPorts() => SerialPort.GetPortNames()
        .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    public void Dispose()
    {
        lock (_sync) CloseLocked();
    }
}

public sealed record SerialMonitorSnapshot(
    IReadOnlyList<string> Ports,
    string? ConnectedPort,
    int BaudRate,
    bool Connected,
    string? Error,
    long ReceivedBytes,
    DateTimeOffset? ConnectedAt);

public sealed record SerialTextChunk(DateTimeOffset Time, string Text, long ReceivedBytes);
