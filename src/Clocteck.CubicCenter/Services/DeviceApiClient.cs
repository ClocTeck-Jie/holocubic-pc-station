using System.Buffers;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Clocteck.CubicCenter.Core;

namespace Clocteck.CubicCenter.Services;

public sealed class DeviceApiClient : IDisposable
{
    public const string SettingsPath = "/sd/apps/settings.json";
    public const string DefaultStoreServer = "https://cubic.clocteck.com";

    private readonly AppLog _log;
    private readonly HttpClient _client = new(new SocketsHttpHandler
    {
        ConnectTimeout = TimeSpan.FromSeconds(3),
        AllowAutoRedirect = false,
        MaxConnectionsPerServer = 8,
    })
    {
        Timeout = TimeSpan.FromSeconds(20),
    };
    private readonly HttpClient _uploadClient = new(new SocketsHttpHandler
    {
        ConnectTimeout = TimeSpan.FromSeconds(5),
        AllowAutoRedirect = false,
        MaxConnectionsPerServer = 1,
    })
    {
        Timeout = TimeSpan.FromMinutes(5),
    };

    public DeviceApiClient(AppLog log) => _log = log;

    public async Task<DeviceControlSnapshot> GetControlSnapshotAsync(string ip, CancellationToken cancellationToken)
    {
        var state = await GetJsonAsync(ip, "/api/system/state", cancellationToken);
        var settings = await ReadSettingsAsync(ip, cancellationToken);
        var display = await TryGetJsonAsync(ip, "/display/api/info", cancellationToken);
        var schedule = await TryGetJsonAsync(ip, "/display-schedule/api/info", cancellationToken);
        return new DeviceControlSnapshot(ip, state, settings, display, schedule);
    }

    public Task<JsonElement> GetStateAsync(string ip, CancellationToken cancellationToken) =>
        GetJsonAsync(ip, "/api/system/state", cancellationToken);

    public Task<JsonElement> GetCatalogAsync(string ip, CancellationToken cancellationToken)
    {
        var path = "/api/system/apps/catalog?server=" + Uri.EscapeDataString(DefaultStoreServer) + "&channel=stable";
        return GetJsonAsync(ip, path, cancellationToken);
    }

    public Task<JsonElement> LaunchAppAsync(string ip, string appId, CancellationToken cancellationToken) =>
        SendJsonAsync(ip, HttpMethod.Post, "/api/system/launch?id=" + Uri.EscapeDataString(appId), null, cancellationToken);

    public Task<JsonElement> ExitAppAsync(string ip, CancellationToken cancellationToken) =>
        SendJsonAsync(ip, HttpMethod.Post, "/api/system/exit", null, cancellationToken);

    public Task<JsonElement> WakeAsync(string ip, CancellationToken cancellationToken) =>
        SendJsonAsync(ip, HttpMethod.Post, "/display/api/wake", null, cancellationToken);

    public Task<JsonElement> ListFilesAsync(string ip, string path, CancellationToken cancellationToken) =>
        GetJsonAsync(ip, "/api/system/fs/list?path=" + Uri.EscapeDataString(NormalizeFsPath(path)), cancellationToken);

    public async Task<byte[]> ReadFileAsync(
        string ip,
        string path,
        int maxBytes,
        CancellationToken cancellationToken)
    {
        path = NormalizeFsPath(path);
        maxBytes = Math.Clamp(maxBytes, 1, 64 * 1024 * 1024);
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            BuildUri(ip, "/api/system/fs/file?path=" + Uri.EscapeDataString(path)));
        request.Headers.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue { NoStore = true };
        using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var text = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException($"HTTP {(int)response.StatusCode}：{TryReadError(text) ?? response.ReasonPhrase}", null, response.StatusCode);
        }
        if (response.Content.Headers.ContentLength is long length && length > maxBytes)
        {
            throw new InvalidOperationException($"文件超过 {maxBytes / 1024 / 1024} MB 读取上限。");
        }

        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var output = new MemoryStream();
        var buffer = new byte[64 * 1024];
        while (true)
        {
            var count = await input.ReadAsync(buffer, cancellationToken);
            if (count <= 0) break;
            if (output.Length + count > maxBytes) throw new InvalidOperationException($"文件超过 {maxBytes / 1024 / 1024} MB 读取上限。");
            output.Write(buffer, 0, count);
        }
        return output.ToArray();
    }

    public async Task<byte[]> ReadFileViaDevToolsAsync(
        string ip,
        string path,
        int maxBytes,
        int chunkSize,
        CancellationToken cancellationToken)
    {
        path = NormalizeFsPath(path);
        maxBytes = Math.Clamp(maxBytes, 1, 64 * 1024 * 1024);
        chunkSize = Math.Clamp(chunkSize, 1024, 256 * 1024);
        using var output = new MemoryStream();
        var offset = 0;
        while (output.Length < maxBytes)
        {
            var size = Math.Min(chunkSize, maxBytes - (int)output.Length);
            var route = "/devtools/api/read?path=" + Uri.EscapeDataString(path) +
                        "&offset=" + offset.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                        "&size=" + size.ToString(System.Globalization.CultureInfo.InvariantCulture);
            using var request = new HttpRequestMessage(HttpMethod.Get, BuildUri(ip, route));
            request.Headers.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue { NoStore = true };
            using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var text = await response.Content.ReadAsStringAsync(cancellationToken);
                var detail = TryReadError(text) ?? response.ReasonPhrase ?? "DevTools read failed";
                throw new HttpRequestException($"HTTP {(int)response.StatusCode}: {detail}", null, response.StatusCode);
            }

            await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
            var buffer = ArrayPool<byte>.Shared.Rent(64 * 1024);
            var chunkBytes = 0;
            try
            {
                while (true)
                {
                    var count = await input.ReadAsync(buffer.AsMemory(0, Math.Min(buffer.Length, maxBytes - (int)output.Length)), cancellationToken);
                    if (count <= 0) break;
                    output.Write(buffer, 0, count);
                    chunkBytes += count;
                    if (output.Length >= maxBytes) break;
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
            if (chunkBytes <= 0) break;
            offset += chunkBytes;
            if (chunkBytes < size) break;
        }
        return output.ToArray();
    }

    public Task<JsonElement> UploadFileAsync(
        string ip,
        string path,
        ReadOnlyMemory<byte> content,
        string contentType,
        CancellationToken cancellationToken)
    {
        var body = new ByteArrayContent(content.ToArray());
        body.Headers.ContentType = System.Net.Http.Headers.MediaTypeHeaderValue.Parse(
            string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType);
        return SendJsonAsync(ip, HttpMethod.Put,
            "/api/system/fs/upload?path=" + Uri.EscapeDataString(NormalizeFsPath(path)), body, cancellationToken);
    }

    public async Task<JsonElement> UploadLocalFileAsync(
        string ip,
        string devicePath,
        string localPath,
        CancellationToken cancellationToken)
        => await UploadLocalFileAsync(ip, devicePath, localPath, null, cancellationToken);

    public async Task<JsonElement> UploadLocalFileAsync(
        string ip,
        string devicePath,
        string localPath,
        Func<long, long, Task>? progressAsync,
        CancellationToken cancellationToken)
    {
        using var body = new ProgressFileContent(localPath, progressAsync, cancellationToken);
        body.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
        return await SendUploadJsonAsync(ip, HttpMethod.Put,
            "/api/system/fs/upload?path=" + Uri.EscapeDataString(NormalizeFsPath(devicePath)), body, cancellationToken);
    }

    public async Task UploadLocalFileViaDevToolsAsync(
        string ip,
        string devicePath,
        string localPath,
        Func<long, long, Task>? progressAsync,
        CancellationToken cancellationToken)
    {
        devicePath = NormalizeFsPath(devicePath);
        var total = new FileInfo(localPath).Length;
        const int chunkSize = 48 * 1024;
        await using var input = new FileStream(localPath, FileMode.Open, FileAccess.Read, FileShare.Read, chunkSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var buffer = ArrayPool<byte>.Shared.Rent(chunkSize);
        long offset = 0;
        try
        {
            do
            {
                var count = total == 0 ? 0 : await input.ReadAsync(buffer.AsMemory(0, chunkSize), cancellationToken);
                if (total > 0 && count <= 0) throw new EndOfStreamException("读取电脑缓存文件时提前结束。");
                using var body = new ByteArrayContent(buffer, 0, count);
                body.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
                var path = "/devtools/api/upload?path=" + Uri.EscapeDataString(devicePath) +
                           "&offset=" + offset.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                           "&total=" + total.ToString(System.Globalization.CultureInfo.InvariantCulture);
                var result = await SendUploadJsonAsync(ip, HttpMethod.Put, path, body, cancellationToken);
                var expectedNext = offset + count;
                var next = result.TryGetProperty("next_offset", out var nextNode) && nextNode.TryGetInt64(out var returnedNext)
                    ? returnedNext
                    : expectedNext;
                if (next != expectedNext) throw new InvalidOperationException($"DevTools 返回了无效上传偏移：应为 {expectedNext}，实际为 {next}。");
                offset = next;
                if (progressAsync is not null) await progressAsync(offset, total);
                if (total == 0) break;
            }
            while (offset < total);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    public async Task<long> UploadRamBenchmarkAsync(
        string ip,
        int totalBytes,
        int chunkSize,
        CancellationToken cancellationToken)
    {
        totalBytes = Math.Clamp(totalBytes, 1, 64 * 1024 * 1024);
        chunkSize = Math.Clamp(chunkSize, 1024, 256 * 1024);
        var buffer = new byte[chunkSize];
        long completed = 0;
        while (completed < totalBytes)
        {
            var count = (int)Math.Min(chunkSize, totalBytes - completed);
            using var body = new ByteArrayContent(buffer, 0, count);
            body.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
            var path = "/devtools/api/bench/upload?size=" + count.ToString(System.Globalization.CultureInfo.InvariantCulture);
            await SendUploadJsonAsync(ip, HttpMethod.Post, path, body, cancellationToken);
            completed += count;
        }
        return completed;
    }

    public async Task<long> DownloadRamBenchmarkAsync(
        string ip,
        int totalBytes,
        int chunkSize,
        CancellationToken cancellationToken)
    {
        totalBytes = Math.Clamp(totalBytes, 1, 64 * 1024 * 1024);
        chunkSize = Math.Clamp(chunkSize, 1024, 256 * 1024);
        long completed = 0;
        while (completed < totalBytes)
        {
            var count = (int)Math.Min(chunkSize, totalBytes - completed);
            var path = "/devtools/api/bench/download?size=" + count.ToString(System.Globalization.CultureInfo.InvariantCulture);
            using var response = await _client.GetAsync(BuildUri(ip, path), HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var text = await response.Content.ReadAsStringAsync(cancellationToken);
                throw new HttpRequestException($"HTTP {(int)response.StatusCode}：{TryReadError(text) ?? response.ReasonPhrase ?? "RAM benchmark download failed"}", null, response.StatusCode);
            }
            await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
            var buffer = ArrayPool<byte>.Shared.Rent(64 * 1024);
            var chunkRead = 0;
            try
            {
                while (chunkRead < count)
                {
                    var read = await input.ReadAsync(buffer.AsMemory(0, Math.Min(buffer.Length, count - chunkRead)), cancellationToken);
                    if (read <= 0) break;
                    chunkRead += read;
                }
            }
            finally { ArrayPool<byte>.Shared.Return(buffer); }
            if (chunkRead != count)
            {
                throw new InvalidOperationException($"RAM benchmark download size mismatch: expected {count}, received {chunkRead}");
            }
            completed += chunkRead;
        }
        return completed;
    }

    public async Task<long> EchoRamBenchmarkAsync(
        string ip,
        int totalBytes,
        int chunkSize,
        CancellationToken cancellationToken)
    {
        totalBytes = Math.Clamp(totalBytes, 1, 64 * 1024 * 1024);
        chunkSize = Math.Clamp(chunkSize, 1024, 256 * 1024);
        var buffer = new byte[chunkSize];
        long completed = 0;
        while (completed < totalBytes)
        {
            var count = (int)Math.Min(chunkSize, totalBytes - completed);
            using var body = new ByteArrayContent(buffer, 0, count);
            body.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
            var path = "/devtools/api/bench/echo";
            using var request = new HttpRequestMessage(HttpMethod.Post, BuildUri(ip, path)) { Content = body };
            request.Headers.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue { NoStore = true };
            using var response = await _uploadClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode) throw new HttpRequestException($"HTTP {(int)response.StatusCode}：RAM benchmark echo failed", null, response.StatusCode);
            var echoed = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            if (echoed.Length != count) throw new InvalidOperationException("RAM benchmark echo size mismatch");
            completed += count;
        }
        return completed;
    }

    public Task<JsonElement> DeleteFileAsync(string ip, string path, CancellationToken cancellationToken) =>
        SendJsonAsync(ip, HttpMethod.Delete,
            "/api/system/fs/remove?path=" + Uri.EscapeDataString(NormalizeFsPath(path)), null, cancellationToken);

    public Task<JsonElement> RenamePathAsync(
        string ip,
        string path,
        string newPath,
        CancellationToken cancellationToken) =>
        SendJsonAsync(ip, HttpMethod.Post,
            "/devtools/api/rename?path=" + Uri.EscapeDataString(NormalizeFsPath(path)) +
            "&new_path=" + Uri.EscapeDataString(NormalizeFsPath(newPath)), null, cancellationToken);

    public Task<JsonElement> CreateDirectoryAsync(string ip, string path, CancellationToken cancellationToken) =>
        SendJsonAsync(ip, HttpMethod.Post,
            "/devtools/api/mkdir?path=" + Uri.EscapeDataString(NormalizeFsPath(path)), null, cancellationToken);

    public async Task CopyPathAsync(
        string ip,
        string sourcePath,
        string destinationPath,
        bool isDirectory,
        CancellationToken cancellationToken)
    {
        sourcePath = NormalizeFsPath(sourcePath);
        destinationPath = NormalizeFsPath(destinationPath);
        if (sourcePath.Equals(destinationPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("源路径和目标路径不能相同。");
        }
        if (isDirectory && destinationPath.StartsWith(sourcePath.TrimEnd('/') + "/", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("不能把文件夹复制到自身内部。");
        }
        if (!isDirectory)
        {
            var bytes = await ReadFileAsync(ip, sourcePath, 64 * 1024 * 1024, cancellationToken);
            await UploadFileAsync(ip, destinationPath, bytes, "application/octet-stream", cancellationToken);
            return;
        }

        var pending = new Queue<(string Source, string Destination, int Depth)>();
        pending.Enqueue((sourcePath, destinationPath, 0));
        var entriesSeen = 0;
        while (pending.Count > 0)
        {
            var current = pending.Dequeue();
            if (current.Depth > 32) throw new InvalidOperationException("文件夹层级超过 32 层复制上限。");
            await CreateDirectoryAsync(ip, current.Destination, cancellationToken);
            var listing = await ListFilesAsync(ip, current.Source, cancellationToken);
            var entries = listing.TryGetProperty("entries", out var entriesNode) && entriesNode.ValueKind == JsonValueKind.Array
                ? entriesNode.EnumerateArray().ToArray()
                : [];
            foreach (var entry in entries)
            {
                if (++entriesSeen > 4096) throw new InvalidOperationException("复制项目超过 4096 个安全上限。");
                var name = entry.TryGetProperty("name", out var nameNode) ? nameNode.GetString() : null;
                if (string.IsNullOrWhiteSpace(name)) continue;
                var childSource = current.Source.TrimEnd('/') + "/" + name;
                var childDestination = current.Destination.TrimEnd('/') + "/" + name;
                var childIsDirectory = entry.TryGetProperty("is_dir", out var directoryNode) && directoryNode.ValueKind == JsonValueKind.True;
                if (childIsDirectory)
                {
                    pending.Enqueue((childSource, childDestination, current.Depth + 1));
                }
                else
                {
                    var bytes = await ReadFileAsync(ip, childSource, 64 * 1024 * 1024, cancellationToken);
                    await UploadFileAsync(ip, childDestination, bytes, "application/octet-stream", cancellationToken);
                }
            }
        }
    }

    public async Task<bool> IsPageReadyAsync(
        string ip,
        string path,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        path = path.Trim();
        if (!path.StartsWith('/') || path.StartsWith("//", StringComparison.Ordinal)) return false;
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, BuildUri(ip, path));
            request.Headers.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue { NoStore = true };
            using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeoutSource.Token);
            return response.IsSuccessStatusCode;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return false; }
        catch (HttpRequestException) { return false; }
    }

    public Task<JsonElement> TestAlarmAsync(string ip, CancellationToken cancellationToken) =>
        SendJsonAsync(ip, HttpMethod.Post, "/display-schedule/api/alarm/test", null, cancellationToken);

    public Task<JsonElement> StopAlarmAsync(string ip, CancellationToken cancellationToken) =>
        SendJsonAsync(ip, HttpMethod.Post, "/display-schedule/api/alarm/stop", null, cancellationToken);

    public Task<JsonElement> CheckFirmwareAsync(string ip, CancellationToken cancellationToken) =>
        SendJsonAsync(ip, HttpMethod.Get, "/api/system/firmware/check", null, cancellationToken);

    public Task<JsonElement> StartFirmwareUpdateAsync(string ip, CancellationToken cancellationToken) =>
        SendJsonAsync(ip, HttpMethod.Post, "/api/system/firmware/update", null, cancellationToken);

    public Task<JsonElement> SetBrightnessAsync(string ip, int value, CancellationToken cancellationToken)
    {
        value = Math.Clamp(value, 1, 100);
        return SendJsonAsync(ip, HttpMethod.Post, $"/display/api/brightness?value={value}", null, cancellationToken);
    }

    public Task<JsonElement> SetAutoSleepAsync(string ip, int seconds, CancellationToken cancellationToken)
    {
        var enabled = seconds > 0;
        var normalized = Math.Clamp(enabled ? seconds : 1800, 60, 86400);
        return SendJsonAsync(ip, HttpMethod.Post,
            $"/display/api/sleep?enabled={(enabled ? "true" : "false")}&seconds={normalized}", null, cancellationToken);
    }

    public async Task<JsonElement> SaveSettingsAsync(
        string ip,
        IReadOnlyDictionary<string, object?> updates,
        CancellationToken cancellationToken)
    {
        var settings = await ReadSettingsNodeAsync(ip, cancellationToken);
        var oldAddress = ReadText(settings, "weather_address", "weatherAddress");

        foreach (var (key, raw) in updates)
        {
            if (!AllowedSettingKeys.Contains(key)) continue;
            settings[key] = ToJsonNode(raw);
        }

        var newAddress = ReadText(settings, "weather_address", "weatherAddress");
        if (!string.Equals(oldAddress, newAddress, StringComparison.Ordinal))
        {
            settings.Remove("weather_location_address");
            settings.Remove("weather_location_raw");
            settings.Remove("weather_location_id");
            settings.Remove("weather_city");
        }
        settings["saved_at"] = DateTimeOffset.Now.ToString("O");

        var body = settings.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        var saved = await SendJsonAsync(
            ip,
            HttpMethod.Put,
            "/api/system/fs/upload?path=" + Uri.EscapeDataString(SettingsPath),
            new StringContent(body, Encoding.UTF8, "application/json"),
            cancellationToken);

        if (updates.TryGetValue("brightness", out var brightnessRaw) && TryInt(brightnessRaw, out var brightness))
        {
            try { await SetBrightnessAsync(ip, brightness, cancellationToken); }
            catch (Exception error) { _log.Warn("设备设置", "亮度设置已保存，但显示服务暂未响应：" + error.Message); }
        }

        if (updates.TryGetValue("auto_sleep_seconds", out var secondsRaw) && TryInt(secondsRaw, out var seconds))
        {
            var enabled = updates.TryGetValue("auto_sleep_enabled", out var enabledRaw) && TryBool(enabledRaw);
            try { await SetAutoSleepAsync(ip, enabled ? seconds : 0, cancellationToken); }
            catch (Exception error) { _log.Warn("设备设置", "息屏设置已保存，但显示服务暂未响应：" + error.Message); }
        }

        if (updates.Keys.Any(ScheduleSettingKeys.Contains))
        {
            try
            {
                var scheduleBody = new JsonObject();
                foreach (var key in ScheduleSettingKeys)
                {
                    if (settings.TryGetPropertyValue(key, out var value)) scheduleBody[key] = value?.DeepClone();
                }
                await SendJsonAsync(ip, HttpMethod.Post, "/display-schedule/api/settings",
                    new StringContent(scheduleBody.ToJsonString(), Encoding.UTF8, "application/json"), cancellationToken);
            }
            catch (Exception error)
            {
                _log.Warn("设备设置", "息屏与闹钟设置已保存，但服务暂未响应：" + error.Message);
            }
        }

        return saved;
    }

    public Task<JsonElement> InstallAppAsync(
        string ip,
        string manifestUrl,
        string appId,
        string name,
        CancellationToken cancellationToken)
    {
        var body = JsonSerializer.Serialize(new { manifest_url = manifestUrl, app_id = appId, name });
        return SendJsonAsync(ip, HttpMethod.Post, "/api/system/apps",
            new StringContent(body, Encoding.UTF8, "application/json"), cancellationToken);
    }

    public Task<JsonElement> UninstallAppAsync(string ip, string appId, CancellationToken cancellationToken) =>
        SendJsonAsync(ip, HttpMethod.Delete, "/api/system/apps?id=" + Uri.EscapeDataString(appId), null, cancellationToken);

    public Task<JsonElement> RescanAppsAsync(string ip, CancellationToken cancellationToken) =>
        SendJsonAsync(ip, HttpMethod.Post, "/devtools/api/apps/rescan", null, cancellationToken);

    private async Task<JsonElement> ReadSettingsAsync(string ip, CancellationToken cancellationToken)
    {
        var node = await ReadSettingsNodeAsync(ip, cancellationToken);
        return JsonSerializer.SerializeToElement(node);
    }

    private async Task<JsonObject> ReadSettingsNodeAsync(string ip, CancellationToken cancellationToken)
    {
        try
        {
            var path = "/devtools/api/read?path=" + Uri.EscapeDataString(SettingsPath) + "&offset=0&size=65536";
            using var response = await _client.GetAsync(BuildUri(ip, path), cancellationToken);
            response.EnsureSuccessStatusCode();
            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            var text = Encoding.UTF8.GetString(bytes);
            return JsonNode.Parse(text) as JsonObject ?? [];
        }
        catch (HttpRequestException error)
        {
            _log.Warn("设备设置", "暂时无法读取 settings.json：" + error.Message);
            return [];
        }
        catch (JsonException error)
        {
            _log.Warn("设备设置", "settings.json 格式无效，将使用空设置：" + error.Message);
            return [];
        }
    }

    private Task<JsonElement> GetJsonAsync(string ip, string path, CancellationToken cancellationToken) =>
        SendJsonAsync(ip, HttpMethod.Get, path, null, cancellationToken);

    private async Task<JsonElement?> TryGetJsonAsync(string ip, string path, CancellationToken cancellationToken)
    {
        try { return await GetJsonAsync(ip, path, cancellationToken); }
        catch (HttpRequestException) { return null; }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested) { return null; }
    }

    private async Task<JsonElement> SendJsonAsync(
        string ip,
        HttpMethod method,
        string path,
        HttpContent? content,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, BuildUri(ip, path)) { Content = content };
        request.Headers.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue { NoStore = true };
        using var response = await _client.SendAsync(request, cancellationToken);
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var detail = TryReadError(text) ?? response.ReasonPhrase ?? "设备接口请求失败";
            throw new HttpRequestException($"HTTP {(int)response.StatusCode}：{detail}", null, response.StatusCode);
        }

        if (string.IsNullOrWhiteSpace(text)) return JsonSerializer.SerializeToElement(new { ok = true });
        try
        {
            using var document = JsonDocument.Parse(text);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return JsonSerializer.SerializeToElement(new { ok = true, text });
        }
    }

    private async Task<JsonElement> SendUploadJsonAsync(
        string ip,
        HttpMethod method,
        string path,
        HttpContent content,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, BuildUri(ip, path)) { Content = content };
        request.Headers.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue { NoStore = true };
        using var response = await _uploadClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var detail = TryReadError(text) ?? response.ReasonPhrase ?? "设备上传接口请求失败";
            throw new HttpRequestException($"HTTP {(int)response.StatusCode}：{detail}", null, response.StatusCode);
        }
        if (string.IsNullOrWhiteSpace(text)) return JsonSerializer.SerializeToElement(new { ok = true });
        try
        {
            using var document = JsonDocument.Parse(text);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return JsonSerializer.SerializeToElement(new { ok = true, text });
        }
    }

    private static Uri BuildUri(string ip, string path)
    {
        if (!IPAddress.TryParse(ip, out var address) || address.AddressFamily != AddressFamily.InterNetwork)
        {
            throw new ArgumentException("设备地址必须是 IPv4 地址。", nameof(ip));
        }
        if (!path.StartsWith('/')) throw new ArgumentException("设备 API 路径无效。", nameof(path));
        return new Uri($"http://{address}{path}");
    }

    private static string NormalizeFsPath(string path)
    {
        path = (path ?? string.Empty).Trim().Replace('\\', '/');
        if (!path.Equals("/sd", StringComparison.Ordinal) && !path.StartsWith("/sd/", StringComparison.Ordinal))
        {
            throw new ArgumentException("设备文件路径必须位于 /sd。", nameof(path));
        }
        if (path.Split('/', StringSplitOptions.RemoveEmptyEntries).Any(part => part is "." or ".."))
        {
            throw new ArgumentException("设备文件路径不能包含相对目录。", nameof(path));
        }
        return path.TrimEnd('/') is { Length: > 0 } normalized ? normalized : "/sd";
    }

    public void Dispose()
    {
        _client.Dispose();
        _uploadClient.Dispose();
    }

    private sealed class ProgressFileContent : HttpContent
    {
        private readonly string _path;
        private readonly long _length;
        private readonly Func<long, long, Task>? _progressAsync;
        private readonly CancellationToken _cancellationToken;

        public ProgressFileContent(string path, Func<long, long, Task>? progressAsync, CancellationToken cancellationToken)
        {
            _path = path;
            _length = new FileInfo(path).Length;
            _progressAsync = progressAsync;
            _cancellationToken = cancellationToken;
        }

        protected override bool TryComputeLength(out long length)
        {
            length = _length;
            return true;
        }

        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            await using var input = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            var buffer = ArrayPool<byte>.Shared.Rent(64 * 1024);
            long completed = 0;
            long lastReported = 0;
            try
            {
                while (true)
                {
                    var count = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), _cancellationToken);
                    if (count <= 0) break;
                    await stream.WriteAsync(buffer.AsMemory(0, count), _cancellationToken);
                    completed += count;
                    if (_progressAsync is not null && (completed == _length || completed - lastReported >= 64 * 1024))
                    {
                        lastReported = completed;
                        await _progressAsync(completed, _length);
                    }
                }
                if (_progressAsync is not null && completed != lastReported) await _progressAsync(completed, _length);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }
    }

    private static JsonNode? ToJsonNode(object? raw)
    {
        if (raw is null) return null;
        if (raw is JsonElement element) return JsonNode.Parse(element.GetRawText());
        return JsonSerializer.SerializeToNode(raw);
    }

    private static string ReadText(JsonObject settings, params string[] names)
    {
        foreach (var name in names)
        {
            if (settings[name] is JsonValue value && value.TryGetValue<string>(out var text)) return text ?? string.Empty;
        }
        return string.Empty;
    }

    private static bool TryInt(object? raw, out int value)
    {
        if (raw is JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Number) return element.TryGetInt32(out value);
            return int.TryParse(element.ToString(), out value);
        }
        return int.TryParse(raw?.ToString(), out value);
    }

    private static bool TryBool(object? raw)
    {
        if (raw is JsonElement element)
        {
            return element.ValueKind == JsonValueKind.True ||
                   (element.ValueKind == JsonValueKind.String && bool.TryParse(element.GetString(), out var result) && result);
        }
        return bool.TryParse(raw?.ToString(), out var parsed) && parsed;
    }

    private static string? TryReadError(string text)
    {
        try
        {
            using var document = JsonDocument.Parse(text);
            if (document.RootElement.TryGetProperty("error", out var error)) return error.ToString();
            if (document.RootElement.TryGetProperty("message", out var message)) return message.ToString();
        }
        catch (JsonException) { }
        return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }

    private static readonly HashSet<string> AllowedSettingKeys = new(StringComparer.Ordinal)
    {
        "timezone",
        "weather_address",
        "language",
        "ap_enabled",
        "autostart_enabled",
        "autostart_app_id",
        "brightness",
        "auto_sleep_enabled",
        "auto_sleep_seconds",
        "scheduled_sleep_enabled",
        "scheduled_sleep_mode",
        "scheduled_sleep_hour",
        "scheduled_sleep_minute",
        "scheduled_wake_hour",
        "scheduled_wake_minute",
        "alarm_sound",
        "alarms",
    };

    private static readonly HashSet<string> ScheduleSettingKeys = new(StringComparer.Ordinal)
    {
        "auto_sleep_enabled",
        "auto_sleep_seconds",
        "scheduled_sleep_enabled",
        "scheduled_sleep_mode",
        "scheduled_sleep_hour",
        "scheduled_sleep_minute",
        "scheduled_wake_hour",
        "scheduled_wake_minute",
        "alarm_sound",
        "alarms",
    };
}

public sealed record DeviceControlSnapshot(
    string Ip,
    JsonElement State,
    JsonElement Settings,
    JsonElement? Display,
    JsonElement? Schedule);
