using Clocteck.CubicCenter.Models;

namespace Clocteck.CubicCenter.Core;

public sealed class AppLog
{
    private readonly object _sync = new();
    private readonly List<LogEntry> _entries = [];

    public event EventHandler<LogEntry>? EntryAdded;

    public IReadOnlyList<LogEntry> Snapshot()
    {
        lock (_sync)
        {
            return _entries.ToArray();
        }
    }

    public void Info(string source, string message) => Add("info", source, message);
    public void Warn(string source, string message) => Add("warning", source, message);
    public void Error(string source, string message) => Add("error", source, message);

    private void Add(string level, string source, string message)
    {
        var entry = new LogEntry(DateTimeOffset.Now, level, source, Redact(message));
        lock (_sync)
        {
            _entries.Add(entry);
            if (_entries.Count > 600) _entries.RemoveRange(0, 100);
        }
        EntryAdded?.Invoke(this, entry);
    }

    private static string Redact(string message)
    {
        var user = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return string.IsNullOrWhiteSpace(user) ? message : message.Replace(user, "%USERPROFILE%", StringComparison.OrdinalIgnoreCase);
    }
}
