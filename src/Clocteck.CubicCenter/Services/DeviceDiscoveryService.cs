using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Text.Json;
using Clocteck.CubicCenter.Core;
using Clocteck.CubicCenter.Models;

namespace Clocteck.CubicCenter.Services;

public sealed class DeviceDiscoveryService
{
    private readonly AppLog _log;
    private readonly HttpClient _client = new(new SocketsHttpHandler
    {
        ConnectTimeout = TimeSpan.FromSeconds(2),
        AllowAutoRedirect = true,
        MaxConnectionsPerServer = 8,
    })
    {
        Timeout = TimeSpan.FromSeconds(3),
    };

    public DeviceDiscoveryService(AppLog log) => _log = log;

    public async Task<DeviceInfo?> ScanLocalSubnetAsync(CancellationToken cancellationToken)
    {
        return (await ScanLocalSubnetsAsync(cancellationToken)).FirstOrDefault();
    }

    public async Task<IReadOnlyList<DeviceInfo>> ScanLocalSubnetsAsync(
        CancellationToken cancellationToken,
        Action<string>? probingAddress = null)
    {
        var candidates = await GetNeighborCandidatesAsync(cancellationToken);
        if (candidates.Length == 0) return [];

        _log.Info("设备发现", $"从 Windows 邻居表读取到 {candidates.Length} 个活跃地址，仅检查这些地址");
        using var semaphore = new SemaphoreSlim(12);
        var tasks = candidates.Select(async candidate =>
        {
            await semaphore.WaitAsync(cancellationToken);
            try
            {
                probingAddress?.Invoke(candidate.Address);
                var ping = await TryPingAsync(candidate.Address, cancellationToken);
                var name = await TryResolveNameAsync(candidate.Address, cancellationToken);
                if (!string.IsNullOrWhiteSpace(name))
                {
                    _log.Info("设备发现", $"邻居 {candidate.Address} · {name} · Ping {(ping ? "成功" : "无响应")}");
                }
                return await ProbeAsync(candidate.Address, cancellationToken, ping ? 900 : 1250);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return null;
            }
            finally
            {
                semaphore.Release();
            }
        }).ToArray();

        var results = await Task.WhenAll(tasks);
        return results
            .Where(device => device is not null)
            .Cast<DeviceInfo>()
            .GroupBy(device => device.IpAddress, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(device => ParseIpv4(device.IpAddress))
            .ToArray();
    }

    public async Task<DeviceInfo?> ProbeAsync(string host, CancellationToken cancellationToken, int timeoutMilliseconds = 2500)
    {
        if (string.IsNullOrWhiteSpace(host)) return null;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(timeoutMilliseconds);
        foreach (var path in new[] { "/api/system/state", "/" })
        {
            try
            {
                using var response = await _client.GetAsync($"http://{host}{path}", timeout.Token);
                if (!response.IsSuccessStatusCode) continue;
                var text = await response.Content.ReadAsStringAsync(timeout.Token);
                if (!LooksLikeClocteck(path, text)) continue;
                var ip = await ResolveIpv4Async(host, timeout.Token) ?? host;
                var deviceId = TryReadDeviceId(text);
                var info = new DeviceInfo("Clocteck Cubic", ip, $"http://{ip}/", deviceId, path.StartsWith("/api/") ? text : null);
                _log.Info("设备发现", $"发现设备 {ip}");
                return info;
            }
            catch (Exception error) when (error is HttpRequestException or TaskCanceledException or SocketException)
            {
                // Try the next address or wait for the next discovery pass.
            }
        }
        return null;
    }

    private static bool LooksLikeClocteck(string path, string text)
    {
        if (path.StartsWith("/api/"))
        {
            return text.TrimStart().StartsWith('{') &&
                   (text.Contains("wifi", StringComparison.OrdinalIgnoreCase) ||
                    text.Contains("system", StringComparison.OrdinalIgnoreCase) ||
                    text.Contains("firmware", StringComparison.OrdinalIgnoreCase));
        }
        return text.Contains("clocteck", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("holocubic", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("/api/system", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<string?> ResolveIpv4Async(string host, CancellationToken cancellationToken)
    {
        if (IPAddress.TryParse(host, out var parsed)) return parsed.ToString();
        try
        {
            return (await Dns.GetHostAddressesAsync(host, cancellationToken))
                .FirstOrDefault(address => address.AddressFamily == AddressFamily.InterNetwork)?.ToString();
        }
        catch
        {
            return null;
        }
    }

    private static string? TryReadDeviceId(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return FindString(document.RootElement, "device_id") ??
                   FindString(document.RootElement, "chip_id") ??
                   FindString(document.RootElement, "mac");
        }
        catch
        {
            return null;
        }
    }

    private static string? FindString(JsonElement element, string name)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) return property.Value.ToString();
                var nested = FindString(property.Value, name);
                if (nested is not null) return nested;
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                var nested = FindString(item, name);
                if (nested is not null) return nested;
            }
        }
        return null;
    }

    private static async Task<NeighborCandidate[]> GetNeighborCandidatesAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "arp.exe",
                    Arguments = "-a",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                },
            };
            if (!process.Start()) return [];
            var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            var output = await outputTask;
            return Regex.Matches(output, @"(?<![\d.])(?:\d{1,3}\.){3}\d{1,3}(?![\d.])")
                .Select(match => match.Value)
                .Where(IsNeighborCandidate)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(address => new NeighborCandidate(address))
                .OrderBy(candidate => ParseIpv4(candidate.Address))
                .ToArray();
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            return [];
        }
    }

    private static async Task<bool> TryPingAsync(string address, CancellationToken cancellationToken)
    {
        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(address, 350).WaitAsync(cancellationToken);
            return reply.Status == IPStatus.Success;
        }
        catch
        {
            return false;
        }
    }

    private static async Task<string?> TryResolveNameAsync(string address, CancellationToken cancellationToken)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(450);
            var entry = await Dns.GetHostEntryAsync(address, timeout.Token);
            return string.IsNullOrWhiteSpace(entry.HostName) || entry.HostName == address ? null : entry.HostName;
        }
        catch
        {
            return null;
        }
    }

    private static bool IsNeighborCandidate(string value)
    {
        if (!IPAddress.TryParse(value, out var address) || address.AddressFamily != AddressFamily.InterNetwork || IPAddress.IsLoopback(address)) return false;
        var bytes = address.GetAddressBytes();
        if (bytes[3] is 0 or 255 || bytes[0] >= 224 || bytes[0] == 169) return false;
        return bytes[0] == 10 || bytes[0] == 192 && bytes[1] == 168 || bytes[0] == 172 && bytes[1] is >= 16 and <= 31;
    }

    private static uint ToUInt32(IReadOnlyList<byte> bytes) =>
        ((uint)bytes[0] << 24) | ((uint)bytes[1] << 16) | ((uint)bytes[2] << 8) | bytes[3];

    private static uint ParseIpv4(string value) => IPAddress.TryParse(value, out var parsed)
        ? ToUInt32(parsed.GetAddressBytes())
        : uint.MaxValue;

    private sealed record NeighborCandidate(string Address);
}
