using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text.Json;
using Clocteck.CubicCenter.Core;
using Clocteck.CubicCenter.Models;

namespace Clocteck.CubicCenter.Services;

public sealed class HolopetConfigurator
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

    public HolopetConfigurator(AppLog log) => _log = log;

    public async Task<HolopetConfigResult> ConfigureAsync(
        string deviceHost,
        string? routeBase,
        int port,
        CancellationToken cancellationToken)
    {
        const string eventPath = "/events";
        var route = NormalizeRoute(routeBase);
        try
        {
            var deviceIp = await ResolveDeviceIpv4Async(deviceHost, cancellationToken);
            if (deviceIp is null) return Fail("无法解析设备 IP。", deviceHost, port, route);
            var computerIp = ResolveLocalRouteAddress(deviceIp);
            if (computerIp is null) return Fail("没有找到能访问设备的电脑 IPv4 地址。", deviceIp.ToString(), port, route);

            var stateUri = new Uri($"http://{deviceIp}{route}/api/state");
            using var stateResponse = await _client.GetAsync(stateUri, cancellationToken);
            if (stateResponse.StatusCode == HttpStatusCode.NotFound)
            {
                return Fail("设备尚未注册 Holopet 配置接口，请确认应用已经启动。", deviceIp.ToString(), port, route);
            }
            stateResponse.EnsureSuccessStatusCode();

            var query = BuildQuery(new Dictionary<string, string>
            {
                ["host"] = computerIp.ToString(),
                ["port"] = port.ToString(),
                ["path"] = eventPath,
            });
            var saveUri = new Uri($"http://{deviceIp}{route}/api/save?{query}");
            _log.Info("Holopet", $"同步桥接地址 http://{computerIp}:{port}{eventPath}");
            using var saveResponse = await _client.GetAsync(saveUri, cancellationToken);
            var saveText = await saveResponse.Content.ReadAsStringAsync(cancellationToken);
            if (!saveResponse.IsSuccessStatusCode)
            {
                return Fail("设备保存 Holopet 配置失败：" + TryReadError(saveText), deviceIp.ToString(), port, route);
            }

            using var savedDocument = JsonDocument.Parse(saveText);
            if (!Matches(savedDocument.RootElement, computerIp.ToString(), port, eventPath))
            {
                return Fail("Holopet 返回成功，但配置校验不一致。", deviceIp.ToString(), port, route);
            }

            await Task.Delay(300, cancellationToken);
            using var verifyResponse = await _client.GetAsync(stateUri, cancellationToken);
            verifyResponse.EnsureSuccessStatusCode();
            using var verifyDocument = JsonDocument.Parse(await verifyResponse.Content.ReadAsStringAsync(cancellationToken));
            if (!Matches(verifyDocument.RootElement, computerIp.ToString(), port, eventPath))
            {
                return Fail("Holopet 配置已提交，但重新读取验证失败。", deviceIp.ToString(), port, route);
            }

            var eventUrl = $"http://{computerIp}:{port}{eventPath}";
            var message = $"Holopet 已连接 {eventUrl}";
            _log.Info("Holopet", message);
            return new HolopetConfigResult(true, message, deviceIp.ToString(), computerIp.ToString(), port, route, eventUrl);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception error)
        {
            var message = "Holopet 自动配置失败：" + error.Message;
            _log.Warn("Holopet", message);
            return Fail(message, deviceHost, port, route);
        }
    }

    private static bool Matches(JsonElement state, string host, int port, string path)
    {
        return state.TryGetProperty("ok", out var ok) && ok.ValueKind == JsonValueKind.True &&
               state.TryGetProperty("host", out var hostNode) && hostNode.GetString() == host &&
               state.TryGetProperty("port", out var portNode) && portNode.TryGetInt32(out var value) && value == port &&
               state.TryGetProperty("path", out var pathNode) && NormalizePath(pathNode.GetString()) == path;
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

    private static string NormalizeRoute(string? route) => string.IsNullOrWhiteSpace(route)
        ? "/holopet"
        : "/" + route.Trim().Trim('/');

    private static string NormalizePath(string? path) => string.IsNullOrWhiteSpace(path) ? "/events" : path.StartsWith('/') ? path : "/" + path;
    private static string BuildQuery(IReadOnlyDictionary<string, string> values) => string.Join("&", values.Select(pair =>
        $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"));

    private static string TryReadError(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.TryGetProperty("error", out var error)) return error.ToString();
            if (document.RootElement.TryGetProperty("message", out var message)) return message.ToString();
        }
        catch { }
        return "HTTP请求失败";
    }

    private static HolopetConfigResult Fail(string message, string? deviceIp, int port, string route) =>
        new(false, message, deviceIp, null, port, route);
}
