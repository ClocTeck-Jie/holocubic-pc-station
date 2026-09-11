using System.Text.Json;

namespace Clocteck.CubicCenter.Services;

public sealed class CodexStateReader
{
    public object ReadState()
    {
        try
        {
            var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex", "sessions");
            if (!Directory.Exists(root)) return Offline("Codex session目录不存在");
            var latest = new DirectoryInfo(root).EnumerateFiles("*.jsonl", SearchOption.AllDirectories)
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .FirstOrDefault();
            if (latest is null) return Offline("尚未发现 Codex session");

            var line = ReadLastLine(latest.FullName);
            string eventType = "session";
            string? timestamp = null;
            if (!string.IsNullOrWhiteSpace(line))
            {
                using var document = JsonDocument.Parse(line);
                var rootElement = document.RootElement;
                eventType = TryString(rootElement, "type") ?? TryString(rootElement, "event") ?? "session";
                timestamp = TryString(rootElement, "timestamp") ?? TryString(rootElement, "time");
            }

            var age = DateTime.UtcNow - latest.LastWriteTimeUtc;
            return new
            {
                online = true,
                status = age < TimeSpan.FromSeconds(15) ? "working" : "idle",
                event_type = eventType,
                updated_at = timestamp ?? latest.LastWriteTimeUtc.ToString("O"),
                session_file = latest.Name,
                age_seconds = Math.Max(0, (int)age.TotalSeconds),
                privacy = "仅返回事件类型和时间，不返回提示词、回答或工具内容",
            };
        }
        catch (Exception error)
        {
            return Offline(error.Message);
        }
    }

    private static object Offline(string reason) => new { online = false, status = "offline", reason, updated_at = DateTimeOffset.Now };

    private static string ReadLastLine(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (stream.Length == 0) return string.Empty;
        var length = (int)Math.Min(stream.Length, 128 * 1024);
        stream.Seek(-length, SeekOrigin.End);
        var buffer = new byte[length];
        _ = stream.Read(buffer, 0, length);
        var text = System.Text.Encoding.UTF8.GetString(buffer);
        return text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? string.Empty;
    }

    private static string? TryString(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) ? value.ToString() : null;
}
