using System.Diagnostics;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using Clocteck.CubicCenter.Core;
using Clocteck.CubicCenter.Models;

namespace Clocteck.CubicCenter.Services;

public static class ElevatedSensorHost
{
    public const string PipeName = "Clocteck.CubicCenter.SensorHost.v1";
    private const string HostMutexName = @"Local\Clocteck.CubicCenter.SensorHost.v1";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static bool IsHostCommand(IReadOnlyList<string> args) =>
        args.Any(value => value.Equals("--sensor-host", StringComparison.OrdinalIgnoreCase));

    public static async Task RunAsync(IReadOnlyList<string> args, CancellationToken cancellationToken = default)
    {
        var pipeName = ReadArgument(args, "--pipe-name") ?? PipeName;
        var mutexName = pipeName == PipeName ? HostMutexName : @"Local\" + pipeName.Replace('\\', '.');
        using var hostMutex = new Mutex(false, mutexName);
        var ownsMutex = false;
        try
        {
            try { ownsMutex = hostMutex.WaitOne(0); }
            catch (AbandonedMutexException) { ownsMutex = true; }

            // A concurrent automatic/manual request may already have started the host.
            // The new process should exit normally instead of crashing on the fixed pipe.
            if (!ownsMutex) return;

            await RunOwnedAsync(args, pipeName, cancellationToken);
        }
        finally
        {
            if (ownsMutex) hostMutex.ReleaseMutex();
        }
    }

    private static async Task RunOwnedAsync(IReadOnlyList<string> args, string pipeName, CancellationToken cancellationToken)
    {
        var parentPid = ReadParentPid(args);
        var clientSid = ReadArgument(args, "--client-sid");
        var log = new AppLog();
        using var sensors = new HardwareSensorService(log);
        var idleSince = DateTimeOffset.Now;

        while (!cancellationToken.IsCancellationRequested && ParentIsAlive(parentPid) && DateTimeOffset.Now - idleSince < TimeSpan.FromMinutes(2))
        {
            NamedPipeServerStream pipe;
            try
            {
                pipe = CreatePipe(pipeName, clientSid);
            }
            catch (IOException)
            {
                // An older host can briefly retain the pipe while shutting down. Avoid an
                // unhandled WPF exception/APPCRASH and let the parent retry cleanly.
                return;
            }

            await using (pipe)
            {
            using var wait = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            wait.CancelAfter(TimeSpan.FromSeconds(10));
            try
            {
                await pipe.WaitForConnectionAsync(wait.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                continue;
            }

            idleSince = DateTimeOffset.Now;
            using var reader = new StreamReader(pipe, Encoding.UTF8, false, 1024, true);
            await using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 1024, true) { AutoFlush = true };
            var command = (await reader.ReadLineAsync(cancellationToken))?.Trim().ToLowerInvariant();
            ElevatedSensorResponse response;
            try
            {
                response = command switch
                {
                    "diagnostics" => new(true, "管理员传感器诊断已生成", sensors.GetSnapshot(), sensors.GetDiagnosticReport(), PawnIoDetector.Detect()),
                    _ => new(true, "管理员传感器可用", sensors.GetSnapshot(), null, PawnIoDetector.Detect()),
                };
            }
            catch (Exception error)
            {
                response = new(false, error.Message, PawnIo: PawnIoDetector.Detect());
            }
            await writer.WriteLineAsync(JsonSerializer.Serialize(response, JsonOptions));
            }
        }
    }

    private static NamedPipeServerStream CreatePipe(string pipeName, string? clientSid)
    {
        var security = new PipeSecurity();
        security.SetAccessRuleProtection(true, false);
        var hostSid = WindowsIdentity.GetCurrent().User;
        if (hostSid is not null)
            security.AddAccessRule(new PipeAccessRule(hostSid, PipeAccessRights.FullControl, AccessControlType.Allow));
        if (!string.IsNullOrWhiteSpace(clientSid))
        {
            var sid = new SecurityIdentifier(clientSid);
            security.AddAccessRule(new PipeAccessRule(sid, PipeAccessRights.ReadWrite, AccessControlType.Allow));
        }

        return NamedPipeServerStreamAcl.Create(
            pipeName,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous,
            4096,
            4096,
            security,
            HandleInheritability.None,
            (PipeAccessRights)0);
    }

    private static int? ReadParentPid(IReadOnlyList<string> args)
    {
        for (var index = 0; index + 1 < args.Count; index++)
            if (args[index].Equals("--parent-pid", StringComparison.OrdinalIgnoreCase) && int.TryParse(args[index + 1], out var pid)) return pid;
        return null;
    }

    private static string? ReadArgument(IReadOnlyList<string> args, string name)
    {
        for (var index = 0; index + 1 < args.Count; index++)
            if (args[index].Equals(name, StringComparison.OrdinalIgnoreCase)) return args[index + 1];
        return null;
    }

    private static bool ParentIsAlive(int? pid)
    {
        if (!pid.HasValue) return true;
        try { return !Process.GetProcessById(pid.Value).HasExited; }
        catch { return false; }
    }

    public static bool CurrentProcessIsElevated()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch { return false; }
    }
}
