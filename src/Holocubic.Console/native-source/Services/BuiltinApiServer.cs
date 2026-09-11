using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Channels;
using Clocteck.CubicCenter.Core;
using Clocteck.CubicCenter.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Clocteck.CubicCenter.Services;

public sealed class BuiltinApiServer : IAsyncDisposable
{
    private static readonly IReadOnlyDictionary<string, int> ServicePorts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
    {
        ["codex-core"] = 8788,
        ["holopet"] = 17321,
        ["pc-monitor"] = 17322,
    };

    private readonly AppLog _log;
    private PerformanceMonitorService? _monitor;
    private readonly PerformanceMonitorSettings _monitorSettings;
    private PerformanceMonitorService Monitor => _monitor ??= new PerformanceMonitorService(_log, _monitorSettings);
    private readonly CodexStateReader _codex = new();
    private readonly ConcurrentDictionary<Guid, Channel<string>> _eventClients = new();
    private readonly ConcurrentDictionary<string, WebApplication> _applications = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, string> _messages = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private string _lastHolopetEvent = JsonSerializer.Serialize(new
    {
        state = "idle",
        @event = "BridgeStart",
        source = "cubic-center",
        project = "",
        tool = "",
        model = "",
        effort = "",
    });
    private bool _disposed;

    public HardwareSensorSnapshot GetHardwareSnapshot() => Monitor.GetHardwareSnapshot();

    public PerformanceMonitorSnapshot GetMonitorSnapshot() => Monitor.GetSnapshot();
    public void ConfigureMonitorBindings(Dictionary<string, string>? bindings)
    {
        if (_monitor is not null) _monitor.ConfigureBindings(bindings);
        else _monitorSettings.Bindings = SensorMapping.ValidateBindings(bindings);
    }
    public HardwareCompatibilityStatus GetHardwareCompatibilityStatus() => Monitor.GetCompatibilityStatus();
    public Task<bool> StartElevatedSensorHostAsync() => Monitor.StartElevatedHostAsync();
    public void SetHwInfoEnabled(bool enabled) => Monitor.SetHwInfoEnabled(enabled);
    public HardwareDiagnosticBundle CreateHardwareDiagnosticBundle(IReadOnlyList<LogEntry> logs) => Monitor.CreateDiagnosticBundle(logs);
    public bool IsRunning(string serviceId) => _applications.ContainsKey(serviceId);
    public string GetStatusMessage(string serviceId) => _messages.TryGetValue(serviceId, out var message) ? message : "已停止";

    public BuiltinApiServer(AppLog log, PerformanceMonitorSettings? monitorSettings = null)
    {
        _log = log;
        _monitorSettings = monitorSettings ?? new PerformanceMonitorSettings();
    }

    public async Task StartAsync(string serviceId, CancellationToken cancellationToken = default)
    {
        serviceId = NormalizeServiceId(serviceId);
        if (!ServicePorts.TryGetValue(serviceId, out var port)) throw new KeyNotFoundException($"未知内置服务：{serviceId}");

        await _lifecycle.WaitAsync(cancellationToken);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_applications.ContainsKey(serviceId)) return;

            WebApplication? app = null;
            try
            {
                var options = new WebApplicationOptions
                {
                    Args = [],
                    ApplicationName = typeof(BuiltinApiServer).Assembly.FullName,
                    ContentRootPath = AppContext.BaseDirectory,
                    EnvironmentName = Environments.Production,
                };
                var builder = WebApplication.CreateBuilder(options);
                builder.Logging.ClearProviders();
                builder.WebHost.ConfigureKestrel(server => server.ListenAnyIP(port));
                app = builder.Build();
                app.Use(async (context, next) =>
                {
                    context.Response.Headers.AccessControlAllowOrigin = "*";
                    await next();
                });
                MapRoutes(app, serviceId);
                await app.StartAsync(cancellationToken);
                _applications[serviceId] = app;
                _messages[serviceId] = $"运行中：{port}";
                _log.Info(ServiceDisplayName(serviceId), $"服务已启动，监听 0.0.0.0:{port}");
            }
            catch (Exception error)
            {
                if (app is not null)
                {
                    try { await app.DisposeAsync(); } catch { }
                }
                _messages[serviceId] = "启动失败：" + error.Message;
                _log.Error(ServiceDisplayName(serviceId), _messages[serviceId]);
            }
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    public async Task StopAsync(string serviceId, CancellationToken cancellationToken = default)
    {
        serviceId = NormalizeServiceId(serviceId);
        await _lifecycle.WaitAsync(cancellationToken);
        try
        {
            if (!_applications.TryRemove(serviceId, out var app))
            {
                _messages[serviceId] = "已停止";
                return;
            }
            try
            {
                await app.StopAsync(TimeSpan.FromSeconds(3));
                await app.DisposeAsync();
                _messages[serviceId] = "已停止";
                _log.Info(ServiceDisplayName(serviceId), "服务已停止");
            }
            catch (Exception error)
            {
                _messages[serviceId] = "停止异常：" + error.Message;
                _log.Warn(ServiceDisplayName(serviceId), _messages[serviceId]);
            }
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    private void MapRoutes(WebApplication app, string serviceId)
    {
        app.MapGet("/health", () => Results.Json(new
        {
            ok = true,
            service = ServiceWireName(serviceId),
            version = "0.1.1",
            time = DateTimeOffset.Now,
        }));

        if (serviceId == "pc-monitor")
        {
            app.MapGet("/api/stats", () => Results.Json(Monitor.GetSnapshot().System));
            app.MapGet("/api/sensors", () => Results.Json(Monitor.GetSnapshot().HardwareSensors));
            app.MapGet("/api/monitor", () => Results.Json(Monitor.GetSnapshot()));
            app.MapGet("/api/canonical", () => Results.Json(Monitor.GetSnapshot().Sensors));
            app.MapGet("/api/catalog", () => Results.Json(Monitor.GetSnapshot().Readings));
            app.MapGet("/api/providers", () => Results.Json(Monitor.GetSnapshot().Providers));
            app.MapGet("/api/compatibility", () => Results.Json(Monitor.GetCompatibilityStatus()));
            app.MapGet("/sse", StreamPcMonitorAsync);
            app.MapGet("/", () => Results.Json(new
            {
                name = "Clocteck Cubic Center",
                service = "pc-monitor",
                architecture = "LHM + elevated LHM + PDH + WMI + optional HWiNFO -> Sensor Normalizer",
                endpoints = new[] { "/health", "/api/stats", "/api/sensors", "/api/monitor", "/api/canonical", "/api/catalog", "/api/providers", "/api/compatibility", "/sse" },
            }));
            return;
        }

        if (serviceId == "holopet")
        {
            app.MapGet("/status", () => Results.Content(_lastHolopetEvent, "application/json; charset=utf-8"));
            app.MapPost("/event", PublishHolopetEventAsync);
            app.MapGet("/events", StreamEventsAsync);
            app.MapGet("/", () => Results.Json(new
            {
                name = "Clocteck Cubic Center",
                service = "holopet",
                clients = _eventClients.Count,
                endpoints = new[] { "/health", "/status", "/events", "/event" },
            }));
            return;
        }

        app.MapGet("/state", () => Results.Json(_codex.ReadState()));
        app.MapGet("/status", () => Results.Json(_codex.ReadState()));
        app.MapPost("/permission", async (HttpRequest request) =>
        {
            using var document = await JsonDocument.ParseAsync(request.Body);
            return Results.Json(new { ok = true, recorded = true, applied = false, note = "当前接口只记录选择，不能代替 Codex 客户端授权" });
        });
        app.MapGet("/", () => Results.Json(new
        {
            name = "Clocteck Cubic Center",
            service = "codex-buddy",
            endpoints = new[] { "/health", "/state", "/status", "/permission" },
        }));
    }

    private async Task<IResult> PublishHolopetEventAsync(HttpRequest request)
    {
        using var reader = new StreamReader(request.Body);
        var payload = await reader.ReadToEndAsync();
        if (string.IsNullOrWhiteSpace(payload)) payload = "{}";
        try
        {
            using var document = JsonDocument.Parse(payload);
            if (document.RootElement.ValueKind == JsonValueKind.Object) _lastHolopetEvent = payload;
        }
        catch (JsonException) { }
        foreach (var channel in _eventClients.Values) channel.Writer.TryWrite(payload);
        return Results.Json(new { ok = true, clients = _eventClients.Count });
    }

    private async Task StreamPcMonitorAsync(HttpContext context)
    {
        try
        {
            context.Response.StatusCode = StatusCodes.Status200OK;
            context.Response.Headers.CacheControl = "no-cache";
            context.Response.Headers.Connection = "keep-alive";
            context.Response.ContentType = "text/event-stream; charset=utf-8";

            _log.Info("电脑监控", $"设备已连接 /sse，来源 {context.Connection.RemoteIpAddress}");
            await context.Response.WriteAsync(": connected\n\n", context.RequestAborted);
            await context.Response.Body.FlushAsync(context.RequestAborted);

            while (!context.RequestAborted.IsCancellationRequested)
            {
                await Task.Delay(1000, context.RequestAborted);
                var snapshot = Monitor.GetSnapshot();
                var hardware = snapshot.HardwareSensors;
                var mappings = (snapshot.Mappings ?? []).ToDictionary(m => m.Key);
                var entries = SensorMapping.Definitions.Select(definition => SensorMapping.WireValue(definition, mappings[definition.Key])).ToList();
                var cpuName = SanitizeMetricText(hardware.CpuName ?? Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER") ?? "Windows CPU");
                entries.Add($"cpu_name|CPU Name {cpuName}");
                entries.Add($"gpu_name|GPU Name {SanitizeMetricText(hardware.GpuName ?? "--")}");
                var usedMb = mappings["memory_used"].Value;
                var totalMb = mappings["memory_total"].Value;
                var freeMb = usedMb.HasValue && totalMb.HasValue ? Math.Max(0, totalMb.Value - usedMb.Value).ToString("F0", System.Globalization.CultureInfo.InvariantCulture) : "--";
                entries.Add($"memory_free|Free Memory {freeMb} MB");
                await context.Response.WriteAsync($"data: {string.Join("{|}", entries)}\n\n", context.RequestAborted);
                await context.Response.Body.FlushAsync(context.RequestAborted);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            _log.Error("电脑监控", "/sse 数据流异常：" + error.Message);
        }
    }

    private async Task StreamEventsAsync(HttpContext context)
    {
        context.Response.StatusCode = StatusCodes.Status200OK;
        context.Response.Headers.CacheControl = "no-cache";
        context.Response.Headers.Connection = "keep-alive";
        context.Response.ContentType = "text/event-stream; charset=utf-8";
        var id = Guid.NewGuid();
        var channel = Channel.CreateBounded<string>(new BoundedChannelOptions(64)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false,
        });
        _eventClients[id] = channel;
        _log.Info("Holopet", $"SSE设备已连接，当前 {_eventClients.Count} 台");
        try
        {
            await context.Response.WriteAsync(": clawd connected\n\n", context.RequestAborted);
            await context.Response.WriteAsync($"data: {_lastHolopetEvent.Replace("\r", string.Empty).Replace("\n", "\ndata: ")}\n\n", context.RequestAborted);
            await context.Response.Body.FlushAsync(context.RequestAborted);
            await foreach (var message in channel.Reader.ReadAllAsync(context.RequestAborted))
            {
                var safe = message.Replace("\r", string.Empty).Replace("\n", "\ndata: ");
                await context.Response.WriteAsync($"data: {safe}\n\n", context.RequestAborted);
                await context.Response.Body.FlushAsync(context.RequestAborted);
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            _eventClients.TryRemove(id, out _);
        }
    }

    private static string SanitizeMetricText(string value) => value
        .Replace("{|}", " ", StringComparison.Ordinal)
        .Replace('|', ' ')
        .Replace('\r', ' ')
        .Replace('\n', ' ')
        .Trim();

    private static void AddMetric(List<string> entries, string key, string label, double? value, string format, string unit)
    {
        if (!value.HasValue || double.IsNaN(value.Value) || double.IsInfinity(value.Value)) return;
        entries.Add($"{key}|{label} {value.Value.ToString(format, System.Globalization.CultureInfo.InvariantCulture)} {unit}");
    }

    private static double Sensor(PerformanceMonitorSnapshot snapshot, CanonicalSensorId id) =>
        SensorNullable(snapshot, id) ?? 0;

    private static double? SensorNullable(PerformanceMonitorSnapshot snapshot, CanonicalSensorId id) => snapshot.Sensors
        .FirstOrDefault(sensor => sensor.Id == (byte)id && string.IsNullOrWhiteSpace(sensor.Instance))?.Value;

    private static string NormalizeServiceId(string serviceId) => serviceId.Trim().ToLowerInvariant();
    private static string ServiceDisplayName(string serviceId) => serviceId switch
    {
        "holopet" => "Holopet",
        "pc-monitor" => "电脑监控",
        "codex-core" => "Codex Buddy",
        _ => serviceId,
    };
    private static string ServiceWireName(string serviceId) => serviceId == "codex-core" ? "codex-buddy" : serviceId;

    public async ValueTask DisposeAsync()
    {
        await _lifecycle.WaitAsync();
        try
        {
            if (_disposed) return;
            _disposed = true;
            foreach (var pair in _applications.ToArray())
            {
                if (!_applications.TryRemove(pair.Key, out var app)) continue;
                try
                {
                    await app.StopAsync(TimeSpan.FromSeconds(3));
                    await app.DisposeAsync();
                }
                catch { }
                _messages[pair.Key] = "已停止";
            }
            _monitor?.Dispose();
        }
        finally
        {
            _lifecycle.Release();
            _lifecycle.Dispose();
        }
    }
}
