using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Serialization;
using Clocteck.CubicCenter.Core;
using Clocteck.CubicCenter.Models;

namespace Clocteck.CubicCenter.Services;

public sealed class HoloPcMonitorConfigurator
{
    private readonly AppLog _log;
    private readonly HttpClient _client = new(new SocketsHttpHandler
    {
        ConnectTimeout = TimeSpan.FromSeconds(3),
        AllowAutoRedirect = true,
    })
    {
        Timeout = TimeSpan.FromSeconds(8),
    };

    public HoloPcMonitorConfigurator(AppLog log) => _log = log;

    public async Task<HoloMonitorConfigResult> ConfigureAsync(
        string deviceHost,
        int monitorPort,
        string? cpuName,
        string? gpuName,
        CancellationToken cancellationToken)
    {
        const string streamPath = "/sse";
        try
        {
            var deviceIp = await ResolveDeviceIpv4Async(deviceHost, cancellationToken);
            if (deviceIp is null)
            {
                return Fail("无法解析设备 IP，请先完成设备发现。", deviceHost, monitorPort, streamPath);
            }

            var computerIp = ResolveLocalRouteAddress(deviceIp);
            if (computerIp is null)
            {
                return Fail("没有找到能访问设备的电脑 IPv4 地址。", deviceIp.ToString(), monitorPort, streamPath);
            }

            _log.Info("Holo PC Monitor", $"读取设备 {deviceIp} 上的现有配置");
            var stateUri = new Uri($"http://{deviceIp}/holo_pc_monitor/api/state");
            using var stateResponse = await _client.GetAsync(stateUri, cancellationToken);
            if (stateResponse.StatusCode == HttpStatusCode.NotFound)
            {
                return Fail("设备没有注册 Holo PC Monitor配置接口，请先在设备上启动该 app。", deviceIp.ToString(), monitorPort, streamPath);
            }
            stateResponse.EnsureSuccessStatusCode();
            await using var stateStream = await stateResponse.Content.ReadAsStreamAsync(cancellationToken);
            var state = await JsonSerializer.DeserializeAsync<HoloMonitorDeviceState>(stateStream, JsonOptions, cancellationToken);
            if (state is null || !state.Ok)
            {
                return Fail("设备返回的 Holo PC Monitor配置无效。", deviceIp.ToString(), monitorPort, streamPath);
            }

            var desiredCpuName = string.IsNullOrWhiteSpace(cpuName)
                ? HardwareNameFormatter.FormatCpu(state.CpuName)
                : HardwareNameFormatter.FormatCpu(cpuName);
            var desiredGpuName = string.IsNullOrWhiteSpace(gpuName)
                ? HardwareNameFormatter.FormatGpu(state.GpuName)
                : HardwareNameFormatter.FormatGpu(gpuName);
            var query = new Dictionary<string, string>
            {
                ["host"] = computerIp.ToString(),
                ["port"] = monitorPort.ToString(),
                ["path"] = streamPath,
                ["layout"] = NormalizeLayout(state.Layout),
                ["cpu_name"] = desiredCpuName,
                ["gpu_name"] = desiredGpuName,
                ["accent_color"] = NormalizeAccent(state.AccentColor),
            };
            var dataUrl = $"http://{computerIp}:{monitorPort}{streamPath}";
            if (state.Host == computerIp.ToString() && state.Port == monitorPort &&
                NormalizePath(state.Path) == streamPath &&
                HardwareNameFormatter.FormatCpu(state.CpuName) == desiredCpuName &&
                HardwareNameFormatter.FormatGpu(state.GpuName) == desiredGpuName)
            {
                var unchangedMessage = $"配置已正确，无需重复写入：{dataUrl}";
                _log.Info("Holo PC Monitor", unchangedMessage);
                return new HoloMonitorConfigResult(true, unchangedMessage, deviceIp.ToString(), computerIp.ToString(), monitorPort, streamPath, dataUrl);
            }
            var saveUri = new Uri($"http://{deviceIp}/holo_pc_monitor/api/save?{BuildQuery(query)}");
            _log.Info("Holo PC Monitor", $"写入数据地址 http://{computerIp}:{monitorPort}{streamPath}");
            using var saveResponse = await _client.GetAsync(saveUri, cancellationToken);
            var saveText = await saveResponse.Content.ReadAsStringAsync(cancellationToken);
            if (!saveResponse.IsSuccessStatusCode)
            {
                var detail = TryReadError(saveText) ?? $"HTTP {(int)saveResponse.StatusCode}";
                return Fail("设备保存配置失败：" + detail, deviceIp.ToString(), monitorPort, streamPath);
            }

            var saved = JsonSerializer.Deserialize<HoloMonitorDeviceState>(saveText, JsonOptions);
            if (saved is null || !saved.Ok || saved.Host != computerIp.ToString() || saved.Port != monitorPort || NormalizePath(saved.Path) != streamPath)
            {
                return Fail("设备返回成功，但配置校验不一致。", deviceIp.ToString(), monitorPort, streamPath);
            }

            await Task.Delay(350, cancellationToken);
            using var verifyResponse = await _client.GetAsync(stateUri, cancellationToken);
            verifyResponse.EnsureSuccessStatusCode();
            var verifyText = await verifyResponse.Content.ReadAsStringAsync(cancellationToken);
            var verified = JsonSerializer.Deserialize<HoloMonitorDeviceState>(verifyText, JsonOptions);
            if (verified is null || verified.Host != computerIp.ToString() || verified.Port != monitorPort || NormalizePath(verified.Path) != streamPath)
            {
                return Fail("配置已经提交，但重新读取验证失败。", deviceIp.ToString(), monitorPort, streamPath);
            }

            var message = $"已自动配置为 {dataUrl}";
            _log.Info("Holo PC Monitor", message);
            return new HoloMonitorConfigResult(true, message, deviceIp.ToString(), computerIp.ToString(), monitorPort, streamPath, dataUrl);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (HttpRequestException error)
        {
            var message = error.StatusCode == HttpStatusCode.NotFound
                ? "设备没有 Holo PC Monitor配置接口，请先启动该 app。"
                : "访问设备配置接口失败：" + error.Message;
            _log.Warn("Holo PC Monitor", message);
            return Fail(message, deviceHost, monitorPort, streamPath);
        }
        catch (Exception error)
        {
            var message = "自动配置失败：" + error.Message;
            _log.Error("Holo PC Monitor", message);
            return Fail(message, deviceHost, monitorPort, streamPath);
        }
    }

    private static async Task<IPAddress?> ResolveDeviceIpv4Async(string host, CancellationToken cancellationToken)
    {
        if (IPAddress.TryParse(host, out var parsed) && parsed.AddressFamily == AddressFamily.InterNetwork) return parsed;
        try
        {
            return (await Dns.GetHostAddressesAsync(host, cancellationToken)).FirstOrDefault(address => address.AddressFamily == AddressFamily.InterNetwork);
        }
        catch (SocketException)
        {
            return null;
        }
    }

    private static IPAddress? ResolveLocalRouteAddress(IPAddress deviceIp)
    {
        try
        {
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            socket.Connect(new IPEndPoint(deviceIp, 9));
            return (socket.LocalEndPoint as IPEndPoint)?.Address;
        }
        catch (SocketException)
        {
            return null;
        }
    }

    private static string BuildQuery(IReadOnlyDictionary<string, string> values) => string.Join("&", values.Select(pair =>
        $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"));

    private static string NormalizeLayout(string? value) => value is "classic" or "hex" ? value : "dashboard";
    private static string NormalizePath(string? value) => string.IsNullOrWhiteSpace(value) ? "/sse" : value.StartsWith('/') ? value : "/" + value;

    private static string NormalizeAccent(string? value)
    {
        var raw = (value ?? "E7C21D").Trim().TrimStart('#');
        return raw.Length == 6 && raw.All(Uri.IsHexDigit) ? "#" + raw.ToUpperInvariant() : "#E7C21D";
    }

    private static string? TryReadError(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.TryGetProperty("error", out var error)) return error.ToString();
            if (document.RootElement.TryGetProperty("message", out var message)) return message.ToString();
        }
        catch { }
        return null;
    }

    private static HoloMonitorConfigResult Fail(string message, string? deviceIp, int port, string path) =>
        new(false, message, deviceIp, null, port, path);

    private sealed class HoloMonitorDeviceState
    {
        public bool Ok { get; set; }
        public string Host { get; set; } = string.Empty;
        public int Port { get; set; }
        public string Path { get; set; } = "/sse";
        public string Layout { get; set; } = "dashboard";
        [JsonPropertyName("cpu_name")]
        public string CpuName { get; set; } = "CPU";

        [JsonPropertyName("gpu_name")]
        public string GpuName { get; set; } = "GPU";

        [JsonPropertyName("accent_color")]
        public string AccentColor { get; set; } = "#E7C21D";
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
    };
}
