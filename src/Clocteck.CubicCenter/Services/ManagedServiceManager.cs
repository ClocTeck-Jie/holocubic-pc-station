using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Sockets;
using Clocteck.CubicCenter.Core;
using Clocteck.CubicCenter.Models;

namespace Clocteck.CubicCenter.Services;

public sealed class ManagedServiceManager : IAsyncDisposable
{
    private readonly AppSettings _settings;
    private readonly BuiltinApiServer _builtIn;
    private readonly AppLog _log;
    private readonly Func<Task> _saveSettings;
    private readonly ConcurrentDictionary<string, Process> _processes = new();
    private readonly CancellationTokenSource _monitorCancellation = new();
    private readonly Task _monitorTask;

    public event EventHandler<IReadOnlyList<WorkerSnapshot>>? StateChanged;

    public ManagedServiceManager(AppSettings settings, BuiltinApiServer builtIn, AppLog log, Func<Task> saveSettings)
    {
        _settings = settings;
        _builtIn = builtIn;
        _log = log;
        _saveSettings = saveSettings;
        _monitorTask = MonitorAsync(_monitorCancellation.Token);
    }

    public async Task StartAutoServicesAsync()
    {
        foreach (var worker in _settings.Workers.Where(worker => worker.AutoStart && !worker.BuiltIn && !string.IsNullOrWhiteSpace(worker.Executable)))
        {
            await StartAsync(worker.Id);
        }
        await PublishAsync();
    }

    public async Task ConfigureAsync(string id, string executable, string? arguments = null)
    {
        var worker = Find(id);
        worker.Executable = executable;
        worker.WorkingDirectory = Path.GetDirectoryName(executable) ?? string.Empty;
        if (arguments is not null) worker.Arguments = arguments;
        await _saveSettings();
        _log.Info("服务", $"已配置 {worker.Name}：{Path.GetFileName(executable)}");
        await PublishAsync();
    }

    public async Task SetAutoStartAsync(string id, bool enabled)
    {
        var worker = Find(id);
        worker.AutoStart = enabled;
        await _saveSettings();
        await PublishAsync();
    }

    public async Task StartAsync(string id)
    {
        var worker = Find(id);
        if (worker.BuiltIn)
        {
            await _builtIn.StartAsync(worker.Id);
            await PublishAsync();
            return;
        }
        if (_processes.TryGetValue(id, out var running) && !running.HasExited) return;
        if (string.IsNullOrWhiteSpace(worker.Executable) || !File.Exists(worker.Executable))
        {
            _log.Warn("服务", $"{worker.Name} 尚未配置可执行文件");
            await PublishAsync();
            return;
        }
        if (worker.Port > 0 && await IsPortOpenAsync(worker.Port, 250))
        {
            _log.Warn("服务", $"端口 {worker.Port} 已被其他程序占用，未启动 {worker.Name}");
            await PublishAsync();
            return;
        }

        try
        {
            var startInfo = CreateStartInfo(worker);
            var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
            process.OutputDataReceived += (_, eventArgs) => { if (!string.IsNullOrWhiteSpace(eventArgs.Data)) _log.Info(worker.Name, eventArgs.Data); };
            process.ErrorDataReceived += (_, eventArgs) => { if (!string.IsNullOrWhiteSpace(eventArgs.Data)) _log.Warn(worker.Name, eventArgs.Data); };
            process.Exited += async (_, _) =>
            {
                _processes.TryRemove(worker.Id, out _);
                _log.Warn("服务", $"{worker.Name} 已退出，退出码 {SafeExitCode(process)}");
                await PublishAsync();
                process.Dispose();
            };
            if (!process.Start()) throw new InvalidOperationException("Process.Start 返回 false");
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            _processes[id] = process;
            _log.Info("服务", $"已启动 {worker.Name}，PID {process.Id}");
        }
        catch (Exception error)
        {
            _log.Error("服务", $"启动 {worker.Name} 失败：{error.Message}");
        }
        await PublishAsync();
    }

    public async Task StopAsync(string id)
    {
        var worker = Find(id);
        if (worker.BuiltIn)
        {
            await _builtIn.StopAsync(worker.Id);
            await PublishAsync();
            return;
        }
        if (!_processes.TryRemove(id, out var process))
        {
            await PublishAsync();
            return;
        }
        try
        {
            if (!process.HasExited)
            {
                if (!process.CloseMainWindow() || !process.WaitForExit(1800)) process.Kill(true);
                await process.WaitForExitAsync();
            }
            _log.Info("服务", $"已停止 {worker.Name}");
        }
        catch (Exception error)
        {
            _log.Warn("服务", $"停止 {worker.Name} 时发生错误：{error.Message}");
        }
        finally
        {
            process.Dispose();
        }
        await PublishAsync();
    }

    public async Task<IReadOnlyList<WorkerSnapshot>> SnapshotAsync()
    {
        var snapshots = new List<WorkerSnapshot>();
        foreach (var worker in _settings.Workers)
        {
            var tracked = _processes.TryGetValue(worker.Id, out var process) && !process.HasExited;
            var listening = worker.Port > 0 && await IsPortOpenAsync(worker.Port, 180);
            string status;
            string message;
            if (worker.BuiltIn)
            {
                if (_builtIn.IsRunning(worker.Id))
                {
                    status = "running";
                    message = _builtIn.GetStatusMessage(worker.Id);
                }
                else if (listening)
                {
                    status = "external";
                    message = $"端口 {worker.Port} 已被外部程序占用";
                }
                else
                {
                    message = _builtIn.GetStatusMessage(worker.Id);
                    status = message.StartsWith("启动失败", StringComparison.Ordinal) ? "error" : "stopped";
                }
            }
            else if (tracked)
            {
                status = "running";
                message = listening || worker.Port == 0 ? "进程运行中" : "进程已启动，等待端口就绪";
            }
            else if (listening)
            {
                status = "external";
                message = $"端口 {worker.Port} 已被外部程序占用";
            }
            else if (string.IsNullOrWhiteSpace(worker.Executable))
            {
                status = "unconfigured";
                message = "请选择现有上位机程序";
            }
            else
            {
                status = "stopped";
                message = "已配置，当前未运行";
            }

            snapshots.Add(new WorkerSnapshot(
                worker.Id,
                worker.Name,
                worker.Description,
                status,
                worker.Port,
                tracked ? process!.Id : null,
                worker.AutoStart,
                worker.BuiltIn,
                worker.BuiltIn || (!string.IsNullOrWhiteSpace(worker.Executable) && File.Exists(worker.Executable)),
                worker.Executable,
                message));
        }
        return snapshots;
    }

    private async Task MonitorAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await PublishAsync();
                await Task.Delay(3000, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception error)
            {
                _log.Warn("服务监控", error.Message);
                await Task.Delay(3000, cancellationToken);
            }
        }
    }

    private async Task PublishAsync() => StateChanged?.Invoke(this, await SnapshotAsync());

    private WorkerSettings Find(string id) => _settings.Workers.FirstOrDefault(worker => worker.Id == id)
        ?? throw new KeyNotFoundException($"未知服务：{id}");

    private static ProcessStartInfo CreateStartInfo(WorkerSettings worker)
    {
        var extension = Path.GetExtension(worker.Executable).ToLowerInvariant();
        var executable = worker.Executable;
        var arguments = worker.Arguments;
        if (extension == ".py")
        {
            arguments = $"\"{worker.Executable}\" {arguments}".Trim();
            executable = "python.exe";
        }
        else if (extension == ".js")
        {
            arguments = $"\"{worker.Executable}\" {arguments}".Trim();
            executable = "node.exe";
        }
        else if (extension is ".cmd" or ".bat")
        {
            arguments = $"/c \"\"{worker.Executable}\" {arguments}\"";
            executable = "cmd.exe";
        }

        return new ProcessStartInfo
        {
            FileName = executable,
            Arguments = arguments,
            WorkingDirectory = string.IsNullOrWhiteSpace(worker.WorkingDirectory) ? Path.GetDirectoryName(worker.Executable)! : worker.WorkingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
    }

    private static async Task<bool> IsPortOpenAsync(int port, int timeoutMilliseconds)
    {
        using var client = new TcpClient();
        try
        {
            using var timeout = new CancellationTokenSource(timeoutMilliseconds);
            await client.ConnectAsync("127.0.0.1", port, timeout.Token);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static int SafeExitCode(Process process)
    {
        try { return process.ExitCode; } catch { return -1; }
    }

    public async ValueTask DisposeAsync()
    {
        _monitorCancellation.Cancel();
        try { await _monitorTask; } catch (OperationCanceledException) { }
        foreach (var id in _processes.Keys.ToArray()) await StopAsync(id);
        await _builtIn.DisposeAsync();
        _monitorCancellation.Dispose();
    }
}
