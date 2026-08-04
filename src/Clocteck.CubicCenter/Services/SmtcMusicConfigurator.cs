using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Clocteck.CubicCenter.Core;
using Clocteck.CubicCenter.Models;

namespace Clocteck.CubicCenter.Services;

public sealed class SmtcMusicConfigurator
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

    public SmtcMusicConfigurator(AppLog log) => _log = log;

    public async Task<SmtcMusicConfigResult> ConfigureAsync(
        string deviceHost,
        string? routeBase,
        int port,
        CancellationToken cancellationToken)
    {
        var route = NormalizeRoute(routeBase);
        try
        {
            var deviceIp = await ResolveDeviceIpv4Async(deviceHost, cancellationToken);
            if (deviceIp is null) return Fail("无法解析设备 IP。", deviceHost, port, route);
            var computerIp = ResolveLocalRouteAddress(deviceIp);
            if (computerIp is null) return Fail("没有找到能访问设备的电脑 IPv4 地址。", deviceIp.ToString(), port, route);

            var configUri = new Uri($"http://{deviceIp}{route}/api/config");
            using var currentResponse = await _client.GetAsync(configUri, cancellationToken);
            if (currentResponse.StatusCode == HttpStatusCode.NotFound)
            {
                return Fail("设备尚未注册 SMTC Music 配置接口，请确认应用已经启动。", deviceIp.ToString(), port, route);
            }
            currentResponse.EnsureSuccessStatusCode();
            using var currentDocument = JsonDocument.Parse(await currentResponse.Content.ReadAsStringAsync(cancellationToken));
            var current = currentDocument.RootElement;

            var payload = JsonSerializer.Serialize(new
            {
                host = computerIp.ToString(),
                port,
                poll_ms = ReadInt(current, "poll_ms", 1000),
                timeout_ms = ReadInt(current, "timeout_ms", 2500),
                status_path = ReadText(current, "status_path", "/status"),
                control_path = ReadText(current, "control_path", "/control"),
                serial_log = ReadBool(current, "serial_log", true),
            });
            _log.Info("SMTC Music", $"同步桥接地址 http://{computerIp}:{port}");
            using var saveResponse = await _client.PostAsync(
                configUri,
                new StringContent(payload, Encoding.UTF8, "application/json"),
                cancellationToken);
            var saveText = await saveResponse.Content.ReadAsStringAsync(cancellationToken);
            if (!saveResponse.IsSuccessStatusCode)
            {
                return Fail("设备保存 SMTC Music 配置失败：" + TryReadError(saveText), deviceIp.ToString(), port, route);
            }
            using var savedDocument = JsonDocument.Parse(saveText);
            if (!Matches(savedDocument.RootElement, computerIp.ToString(), port))
            {
                return Fail("SMTC Music 返回成功，但配置校验不一致。", deviceIp.ToString(), port, route);
            }

            await Task.Delay(250, cancellationToken);
            using var verifyResponse = await _client.GetAsync(configUri, cancellationToken);
            verifyResponse.EnsureSuccessStatusCode();
            using var verifyDocument = JsonDocument.Parse(await verifyResponse.Content.ReadAsStringAsync(cancellationToken));
            if (!Matches(verifyDocument.RootElement, computerIp.ToString(), port))
            {
                return Fail("SMTC Music 配置已提交，但重新读取验证失败。", deviceIp.ToString(), port, route);
            }

            var bridgeUrl = $"http://{computerIp}:{port}";
            var message = $"SMTC Music 已连接 {bridgeUrl}";
            _log.Info("SMTC Music", message);
            return new SmtcMusicConfigResult(true, message, deviceIp.ToString(), computerIp.ToString(), port, route, bridgeUrl);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception error)
        {
            var message = "SMTC Music 自动配置失败：" + error.Message;
            _log.Warn("SMTC Music", message);
            return Fail(message, deviceHost, port, route);
        }
    }

    private static bool Matches(JsonElement config, string host, int port) =>
        config.TryGetProperty("ok", out var ok) && ok.ValueKind == JsonValueKind.True &&
        config.TryGetProperty("host", out var hostNode) && hostNode.GetString() == host &&
        config.TryGetProperty("port", out var portNode) && portNode.TryGetInt32(out var value) && value == port;

    private static int ReadInt(JsonElement source, string name, int fallback) =>
        source.TryGetProperty(name, out var node) && node.TryGetInt32(out var value) ? value : fallback;

    private static string ReadText(JsonElement source, string name, string fallback) =>
        source.TryGetProperty(name, out var node) && node.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(node.GetString())
            ? node.GetString()!
            : fallback;

    private static bool ReadBool(JsonElement source, string name, bool fallback) =>
        source.TryGetProperty(name, out var node) ? node.ValueKind == JsonValueKind.True : fallback;

    private static async Task<IPAddress?> ResolveDeviceIpv4Async(string host, CancellationToken cancellationToken)
    {
        if (IPAddress.TryParse(host, out var parsed) && parsed.AddressFamily == AddressFamily.InterNetwork) return parsed;
        try
        {
            return (await Dns.GetHostAddressesAsync(host, cancellationToken))
                .FirstOrDefault(address => address.AddressFamily == AddressFamily.InterNetwork);
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

    private static string NormalizeRoute(string? route) => string.IsNullOrWhiteSpace(route)
        ? "/smtc_music"
        : "/" + route.Trim().Trim('/');

    private static string TryReadError(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.TryGetProperty("error", out var error)) return error.ToString();
            if (document.RootElement.TryGetProperty("message", out var message)) return message.ToString();
        }
        catch (JsonException) { }
        return string.IsNullOrWhiteSpace(json) ? "HTTP 请求失败" : json.Trim();
    }

    private static SmtcMusicConfigResult Fail(string message, string? deviceIp, int port, string route) =>
        new(false, message, deviceIp, null, port, route);
}
