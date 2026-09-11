using Microsoft.Win32;
using Clocteck.CubicCenter.Models;

namespace Clocteck.CubicCenter.Services;

public static class PawnIoDetector
{
    public const string DownloadUrl = "https://github.com/namazso/PawnIO.Setup/releases/latest";

    public static PawnIoStatus Detect()
    {
        try
        {
            using var service = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\PawnIO");
            if (service is null)
                return new(false, "未检测到 PawnIO；CPU 温度、功率和主板风扇可能不可用", null, null, DateTimeOffset.Now);

            var rawPath = service.GetValue("ImagePath")?.ToString()?.Trim().Trim('"');
            var path = ExpandDriverPath(rawPath);
            string? version = null;
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
            {
                version = System.Diagnostics.FileVersionInfo.GetVersionInfo(path).FileVersion;
            }
            return new(true, "PawnIO 已安装", version, path, DateTimeOffset.Now);
        }
        catch (Exception error)
        {
            return new(false, "检测 PawnIO 失败：" + error.Message, null, null, DateTimeOffset.Now);
        }
    }

    private static string? ExpandDriverPath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var path = Environment.ExpandEnvironmentVariables(value);
        if (path.StartsWith(@"\SystemRoot\", StringComparison.OrdinalIgnoreCase))
            path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), path[12..]);
        else if (path.StartsWith(@"System32\", StringComparison.OrdinalIgnoreCase))
            path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), path);
        return path;
    }
}
