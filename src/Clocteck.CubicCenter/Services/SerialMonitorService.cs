using System.IO.Ports;
using System.Text;

namespace Clocteck.CubicCenter.Services;

public sealed class SerialMonitorService : IDisposable
{
    private readonly object _sync = new();
    private WindowsSerialConnection? _connection;
    private Decoder _decoder = new UTF8Encoding(false, false).GetDecoder();
    private string? _lastError;
    private long _receivedBytes;
    private DateTimeOffset? _connectedAt;
    private string _lineBuffer = string.Empty;

    public event EventHandler<SerialMonitorSnapshot>? StatusChanged;
    public event EventHandler<SerialTextChunk>? TextReceived;
    public event EventHandler<SerialProtocolMessage>? ProtocolReceived;

    public SerialMonitorSnapshot Snapshot()
    {
        lock (_sync)
        {
            return new SerialMonitorSnapshot(
                GetPorts(),
                _connection?.PortName,
                _connection?.BaudRate ?? 115200,
                _connection?.IsOpen == true,
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

    public SerialMonitorSnapshot Connect(string portName, int baudRate) =>
        ConnectCore(portName, baudRate, forceReconnect: false);

    public SerialMonitorSnapshot Reconnect(string portName, int baudRate) =>
        ConnectCore(portName, baudRate, forceReconnect: true);

    private SerialMonitorSnapshot ConnectCore(string portName, int baudRate, bool forceReconnect)
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
            if (!forceReconnect &&
                _connection?.IsOpen == true &&
                string.Equals(_connection.PortName, portName, StringComparison.OrdinalIgnoreCase) &&
                _connection.BaudRate == baudRate)
            {
                // Keep the existing USB CDC session alive. Closing and reopening an
                // ESP32-S3 native serial port can toggle its control-line state and
                // cause USB_UART_CHIP_RESET on some Windows drivers.
                return Snapshot();
            }

            CloseLocked();
            var connection = new WindowsSerialConnection(portName, baudRate);
            connection.BytesReceived += OnBytesReceived;
            connection.ErrorReceived += OnErrorReceived;
            try
            {
                _connection = connection;
                _lastError = null;
                _receivedBytes = 0;
                _connectedAt = DateTimeOffset.Now;
                _lineBuffer = string.Empty;
                _decoder = new UTF8Encoding(false, false).GetDecoder();
                connection.Start();
            }
            catch
            {
                _connection = null;
                connection.BytesReceived -= OnBytesReceived;
                connection.ErrorReceived -= OnErrorReceived;
                connection.Dispose();
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

    public void SendLine(string line)
    {
        line ??= string.Empty;
        lock (_sync)
        {
            if (_connection is null || !_connection.IsOpen) throw new InvalidOperationException("串口尚未连接。");
            var text = line.EndsWith("\n", StringComparison.Ordinal) ? line : line + "\n";
            _connection.Write(Encoding.UTF8.GetBytes(text));
        }
    }

    private void OnBytesReceived(object? sender, SerialBytesReceivedEventArgs args)
    {
        string text;
        long receivedBytes;
        lock (_sync)
        {
            if (!ReferenceEquals(sender, _connection) || _connection?.IsOpen != true) return;
            try
            {
                var chars = new char[Encoding.UTF8.GetMaxCharCount(args.Bytes.Length)];
                _decoder.Convert(args.Bytes, chars, flush: false, out _, out var charsUsed, out _);
                text = new string(chars, 0, charsUsed);
                _receivedBytes += args.Bytes.Length;
                receivedBytes = _receivedBytes;
            }
            catch (Exception error)
            {
                _lastError = error.Message;
                PublishStatus();
                return;
            }
        }
        if (!string.IsNullOrEmpty(text))
        {
            TextReceived?.Invoke(this, new SerialTextChunk(DateTimeOffset.Now, text, receivedBytes));
            ParseProtocolLines(text);
        }
    }

    private void ParseProtocolLines(string text)
    {
        List<string> lines;
        lock (_sync)
        {
            _lineBuffer += text;
            lines = _lineBuffer.Split('\n').ToList();
            _lineBuffer = lines[^1];
            lines.RemoveAt(lines.Count - 1);
        }
        foreach (var line in lines)
        {
            var trimmed = line.TrimEnd('\r');
            const string prefix = "@CUBIC_WIFI/1 ";
            var prefixIndex = trimmed.IndexOf(prefix, StringComparison.Ordinal);
            if (prefixIndex >= 0)
            {
                ProtocolReceived?.Invoke(this, new SerialProtocolMessage(
                    DateTimeOffset.Now,
                    trimmed[(prefixIndex + prefix.Length)..]));
            }
        }
    }

    private void OnErrorReceived(object? sender, SerialConnectionErrorEventArgs args)
    {
        lock (_sync)
        {
            if (!ReferenceEquals(sender, _connection)) return;
            _lastError = args.Error.Message;
        }
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
        if (_connection is null) return;
        var connection = _connection;
        _connection = null;
        connection.BytesReceived -= OnBytesReceived;
        connection.ErrorReceived -= OnErrorReceived;
        connection.Dispose();
        _connectedAt = null;
        _lineBuffer = string.Empty;
        _decoder = new UTF8Encoding(false, false).GetDecoder();
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
public sealed record SerialProtocolMessage(DateTimeOffset Time, string Json);
