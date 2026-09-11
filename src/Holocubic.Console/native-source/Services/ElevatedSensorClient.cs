using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using Clocteck.CubicCenter.Core;
using Clocteck.CubicCenter.Models;

namespace Clocteck.CubicCenter.Services;

public sealed class ElevatedSensorClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly object _sync = new();
    private readonly SemaphoreSlim _launchGate = new(1, 1);
    private readonly AppLog _log;
    private DateTimeOffset _lastLaunchAttempt = DateTimeOffset.MinValue;
    private string? _lastConnectionError;
    private string _status = "管理员传感器尚未连接";
    private bool _available;

    public ElevatedSensorClient(AppLog log) => _log = log;
    public bool Available { get { lock (_sync) return _available; } }
    public string Status { get { lock (_sync) return _status; } }

    public ElevatedSensorResponse? ReadSnapshot(bool autoStart)
    {
        var response = Send("snapshot", 180);
        if (response is not null)
        {
            SetStatus(response.Ok, response.Message);
            return response;
        }
        SetStatus(false, "管理员传感器未连接");
        if (autoStart) _ = EnsureStartedAsync(false);
        return null;
    }

    public ElevatedSensorResponse? ReadDiagnostics() => Send("diagnostics", 1200);

    public async Task<bool> EnsureStartedAsync(bool force)
    {
        if (ElevatedSensorHost.CurrentProcessIsElevated())
        {
            SetStatus(true, "主程序已具有管理员权限");
            return true;
        }

        await _launchGate.WaitAsync();
        try
        {
            // Automatic detection and the UI button can arrive at the same time. Always
            // reuse an existing host before applying retry throttling or launching another.
            var existing = Send("snapshot", 500);
            if (existing?.Ok == true)
            {
                SetStatus(true, existing.Message);
                return true;
            }

            lock (_sync)
            {
                if (!force && DateTimeOffset.Now - _lastLaunchAttempt < TimeSpan.FromMinutes(5)) return false;
                _lastLaunchAttempt = DateTimeOffset.Now;
                _status = "正在请求管理员传感器权限";
            }

            var executable = Environment.ProcessPath ?? throw new InvalidOperationException("无法确定 PCAPP 程序路径");
            var start = new ProcessStartInfo
            {
                FileName = executable,
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden,
            };
            start.ArgumentList.Add("--sensor-host");
            start.ArgumentList.Add("--parent-pid");
            start.ArgumentList.Add(Environment.ProcessId.ToString());
            var clientSid = WindowsIdentity.GetCurrent().User?.Value;
            if (!string.IsNullOrWhiteSpace(clientSid))
            {
                start.ArgumentList.Add("--client-sid");
                start.ArgumentList.Add(clientSid);
            }
            using var process = Process.Start(start) ?? throw new InvalidOperationException("无法启动管理员传感器进程");

            // UAC confirmation and hardware enumeration can take several seconds on
            // Ryzen mobile systems. Three seconds was too short and encouraged retries.
            for (var attempt = 0; attempt < 60; attempt++)
            {
                await Task.Delay(250);
                var response = Send("snapshot", 400);
                if (response?.Ok == true)
                {
                    SetStatus(true, response.Message);
                    _log.Info("硬件传感器", "管理员传感器 Host 已连接");
                    return true;
                }
            }

            var detail = process.HasExited
                ? $"管理员传感器进程已退出，退出码 {process.ExitCode}"
                : "管理员传感器启动后 15 秒仍未响应";
            var connectionError = LastConnectionError();
            if (!string.IsNullOrWhiteSpace(connectionError)) detail += "；管道错误：" + connectionError;
            SetStatus(false, detail);
            _log.Warn("硬件传感器", detail);
        }
        catch (System.ComponentModel.Win32Exception error) when (error.NativeErrorCode == 1223)
        {
            SetStatus(false, "用户取消了管理员权限请求");
            _log.Warn("硬件传感器", "用户取消了管理员传感器权限请求");
        }
        catch (Exception error)
        {
            var detail = "管理员传感器启动失败：" + error.Message;
            SetStatus(false, detail);
            _log.Warn("硬件传感器", detail);
        }
        finally
        {
            _launchGate.Release();
        }
        return false;
    }

    private ElevatedSensorResponse? Send(string command, int timeoutMilliseconds)
    {
        try
        {
            using var pipe = new NamedPipeClientStream(".", ElevatedSensorHost.PipeName, PipeDirection.InOut, PipeOptions.None);
            pipe.Connect(timeoutMilliseconds);
            using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 1024, true) { AutoFlush = true };
            using var reader = new StreamReader(pipe, Encoding.UTF8, false, 4096, true);
            writer.WriteLine(command);
            var json = reader.ReadLine();
            var response = string.IsNullOrWhiteSpace(json) ? null : JsonSerializer.Deserialize<ElevatedSensorResponse>(json, JsonOptions);
            lock (_sync) _lastConnectionError = null;
            return response;
        }
        catch (Exception error)
        {
            lock (_sync) _lastConnectionError = error.Message;
            return null;
        }
    }

    private string? LastConnectionError()
    {
        lock (_sync) return _lastConnectionError;
    }

    private void SetStatus(bool available, string status)
    {
        lock (_sync)
        {
            _available = available;
            _status = status;
        }
    }
}
