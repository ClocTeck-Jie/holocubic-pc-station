using System.Text.Json;
using System.Text.Encodings.Web;
using Clocteck.CubicCenter.Models;

namespace Clocteck.CubicCenter.Core;

public sealed class PortableSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly string _dataDirectory;
    private readonly string _settingsPath;

    public PortableSettingsStore()
    {
        _dataDirectory = Path.Combine(AppContext.BaseDirectory, "data");
        _settingsPath = Path.Combine(_dataDirectory, "settings.json");
    }

    public string DataDirectory => _dataDirectory;

    public async Task<AppSettings> LoadAsync()
    {
        Directory.CreateDirectory(_dataDirectory);
        if (!File.Exists(_settingsPath))
        {
            var defaults = new AppSettings();
            await SaveAsync(defaults);
            return defaults;
        }

        try
        {
            await using var stream = File.OpenRead(_settingsPath);
            var settings = await JsonSerializer.DeserializeAsync<AppSettings>(stream, JsonOptions) ?? new AppSettings();
            settings.Devices ??= [];
            settings.Workers ??= [];
            if (string.IsNullOrWhiteSpace(settings.SelectedDeviceIp) && !string.IsNullOrWhiteSpace(settings.LastDeviceIp))
            {
                settings.SelectedDeviceIp = settings.LastDeviceIp;
            }
            if (!string.IsNullOrWhiteSpace(settings.SelectedDeviceIp) &&
                settings.Devices.All(device => !device.IpAddress.Equals(settings.SelectedDeviceIp, StringComparison.OrdinalIgnoreCase)))
            {
                settings.Devices.Add(new SavedDevice
                {
                    IpAddress = settings.SelectedDeviceIp,
                    Online = false,
                });
            }
            MergeDefaultWorkers(settings);
            return settings;
        }
        catch
        {
            var backup = _settingsPath + ".invalid-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
            File.Copy(_settingsPath, backup, true);
            var defaults = new AppSettings();
            await SaveAsync(defaults);
            return defaults;
        }
    }

    public async Task SaveAsync(AppSettings settings)
    {
        Directory.CreateDirectory(_dataDirectory);
        var temp = _settingsPath + ".tmp";
        await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(settings, JsonOptions));
        File.Move(temp, _settingsPath, true);
    }

    private static void MergeDefaultWorkers(AppSettings settings)
    {
        foreach (var worker in WorkerSettings.CreateDefaults())
        {
            if (settings.Workers.All(existing => existing.Id != worker.Id))
            {
                settings.Workers.Add(worker);
            }
        }
    }
}
