using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Clocteck.CubicCenter.Core;

namespace Clocteck.CubicCenter.Services;

public sealed class GitHubStoreInstaller : IDisposable
{
    private const int MaxFiles = 4096;
    private const long MaxPackageBytes = 64L * 1024 * 1024;
    private readonly AppLog _log;
    private readonly string _cacheRoot;
    private readonly HttpClient _client = new(new SocketsHttpHandler
    {
        ConnectTimeout = TimeSpan.FromSeconds(10),
        AllowAutoRedirect = true,
        MaxConnectionsPerServer = 8,
        AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
    })
    {
        Timeout = TimeSpan.FromSeconds(45),
    };

    public GitHubStoreInstaller(AppLog log)
    {
        _log = log;
        _cacheRoot = Path.Combine(AppContext.BaseDirectory, "data", "store-cache");
        Directory.CreateDirectory(_cacheRoot);
        _client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("ClocteckCubicCenter", "0.1.1"));
    }

    public IReadOnlyList<CachedStorePackage> GetCachedPackages()
    {
        if (!Directory.Exists(_cacheRoot)) return [];
        var packages = new List<CachedStorePackage>();
        foreach (var metadataPath in Directory.EnumerateFiles(_cacheRoot, "package.json", SearchOption.AllDirectories))
        {
            if (metadataPath.StartsWith(Path.Combine(_cacheRoot, ".staging") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) continue;
            try
            {
                var metadata = JsonSerializer.Deserialize<CachedPackageMetadata>(File.ReadAllText(metadataPath, Encoding.UTF8));
                if (metadata is null || !CacheFilesAreValid(Path.GetDirectoryName(metadataPath)!, metadata)) continue;
                packages.Add(new(metadata.AppId, metadata.Version, metadata.RepositoryUrl, metadata.Files.Count, metadata.TotalBytes));
            }
            catch { }
        }
        return packages;
    }

    public async Task<GitHubStoreDownloadResult> DownloadAsync(
        string appId,
        string expectedVersion,
        Func<GitHubStoreProgress, Task> reportAsync,
        CancellationToken cancellationToken)
    {
        appId = ValidateAppId(appId);
        expectedVersion = ValidateVersion(expectedVersion);
        if (string.IsNullOrWhiteSpace(expectedVersion)) throw new InvalidOperationException("商店没有返回可校验的应用版本，不能使用 PC 下载。");
        if (appId.Equals("launcher", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("启动器包含附属服务，请使用“设备下载”完成原子更新。");

        var source = ResolveSource(appId);
        var targetRoot = CacheDirectory(appId, expectedVersion);
        var stagingParent = Path.Combine(_cacheRoot, ".staging");
        Directory.CreateDirectory(stagingParent);
        var tempRoot = Path.Combine(stagingParent, appId + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            await reportAsync(new(appId, "working", "download", 0, 0, 0, "正在读取服务器文件清单"));
            var manifest = await GetServerManifestAsync(appId, cancellationToken);
            if (!manifest.AppId.Equals(appId, StringComparison.Ordinal) || !manifest.Version.Equals(expectedVersion, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"商店清单版本已变化：页面为 {expectedVersion}，服务器为 {manifest.Version}，请刷新应用商店。");
            if (manifest.Files.Count is 0 or > MaxFiles || manifest.TotalBytes <= 0 || manifest.TotalBytes > MaxPackageBytes)
                throw new InvalidOperationException("应用文件数量或总大小超过 PC 安装安全限制。");

            var appInfo = manifest.Files.FirstOrDefault(file => file.Path.Equals("app.info", StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException("服务器清单缺少 app.info。");
            var appInfoBytes = await DownloadFileAsync(source, appInfo, cancellationToken);
            var githubVersion = ReadAppInfoVersion(appInfoBytes);
            if (!githubVersion.Equals(expectedVersion, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"GitHub 版本为 {githubVersion}，商店版本为 {expectedVersion}，为防止降级或错版已停止。请改用“设备下载”。");

            await WriteLocalFileAsync(tempRoot, appInfo.Path, appInfoBytes, cancellationToken);
            long downloaded = appInfoBytes.LongLength;
            await reportAsync(new(appId, "working", "download", TransferPercent(downloaded, manifest.TotalBytes), downloaded, manifest.TotalBytes,
                $"正在从 GitHub 下载 {source.Repository}"));

            using var downloadGate = new SemaphoreSlim(6, 6);
            using var reportGate = new SemaphoreSlim(1, 1);
            var remaining = manifest.Files.Where(file => !file.Path.Equals("app.info", StringComparison.OrdinalIgnoreCase)).ToArray();
            await Task.WhenAll(remaining.Select(async file =>
            {
                await downloadGate.WaitAsync(cancellationToken);
                try
                {
                    var bytes = await DownloadFileAsync(source, file, cancellationToken);
                    await WriteLocalFileAsync(tempRoot, file.Path, bytes, cancellationToken);
                    var completed = Interlocked.Add(ref downloaded, bytes.LongLength);
                    await reportGate.WaitAsync(cancellationToken);
                    try
                    {
                        await reportAsync(new(appId, "working", "download", TransferPercent(completed, manifest.TotalBytes), completed, manifest.TotalBytes,
                            $"正在从 GitHub 下载 {file.Path}"));
                    }
                    finally { reportGate.Release(); }
                }
                finally { downloadGate.Release(); }
            }));

            if (downloaded != manifest.TotalBytes)
                throw new InvalidOperationException($"GitHub 文件总大小校验失败：应为 {manifest.TotalBytes}，实际为 {downloaded}。");

            var metadata = new CachedPackageMetadata(appId, expectedVersion, source.RepositoryUrl, manifest.TotalBytes, manifest.Files.ToList());
            await File.WriteAllTextAsync(Path.Combine(tempRoot, "package.json"), JsonSerializer.Serialize(metadata), Encoding.UTF8, cancellationToken);
            if (Directory.Exists(targetRoot)) Directory.Delete(targetRoot, true);
            Directory.CreateDirectory(Path.GetDirectoryName(targetRoot)!);
            Directory.Move(tempRoot, targetRoot);

            await reportAsync(new(appId, "success", "downloaded", 100, downloaded, manifest.TotalBytes,
                $"{appId} {expectedVersion} 已下载到电脑，点击“安装到设备”继续"));
            _log.Info("应用商店", $"{appId} {expectedVersion} 已从 GitHub 下载到电脑缓存");
            return new(appId, expectedVersion, source.RepositoryUrl, manifest.Files.Count, manifest.TotalBytes, targetRoot);
        }
        finally
        {
            try { if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, true); } catch { }
        }
    }

    public async Task<GitHubStoreInstallResult> InstallCachedAsync(
        string appId,
        string expectedVersion,
        string transferMethod,
        string deviceIp,
        DeviceApiClient device,
        Func<GitHubStoreProgress, Task> reportAsync,
        CancellationToken cancellationToken)
    {
        appId = ValidateAppId(appId);
        expectedVersion = ValidateVersion(expectedVersion);
        transferMethod = NormalizeTransferMethod(transferMethod);
        var localRoot = CacheDirectory(appId, expectedVersion);
        var metadata = await ReadCachedMetadataAsync(localRoot, cancellationToken);
        if (!metadata.AppId.Equals(appId, StringComparison.Ordinal) || !metadata.Version.Equals(expectedVersion, StringComparison.OrdinalIgnoreCase) ||
            !CacheFilesAreValid(localRoot, metadata))
        {
            throw new InvalidOperationException("电脑缓存不完整或版本不匹配，请重新下载。");
        }

        var appInfo = metadata.Files.FirstOrDefault(file => file.Path.Equals("app.info", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("电脑缓存缺少 app.info，请重新下载。");
        var regularFiles = metadata.Files.Where(file =>
            !file.Path.Equals("app.info", StringComparison.OrdinalIgnoreCase) &&
            !file.Path.Equals("main.lua", StringComparison.OrdinalIgnoreCase)).ToList();
        var mainFile = metadata.Files.FirstOrDefault(file => file.Path.Equals("main.lua", StringComparison.OrdinalIgnoreCase));
        if (mainFile is not null) regularFiles.Add(mainFile);
        regularFiles.Add(appInfo);

        long uploaded = 0;
        var transferLabel = transferMethod == "devtools" ? "DevTools 分块" : "固件 FS";
        await reportAsync(new(appId, "working", "install", 0, 0, metadata.TotalBytes, $"准备通过{transferLabel}安装到设备"));
        if (transferMethod == "devtools")
        {
            await EnsureDevToolsDirectoriesAsync(device, deviceIp, appId, metadata.Files, cancellationToken);
        }
        foreach (var file in regularFiles)
        {
            var completedBeforeFile = uploaded;
            await UploadWithFirmwareFsAsync(
                device,
                deviceIp,
                appId,
                localRoot,
                file,
                transferMethod,
                async current => await reportAsync(new(
                    appId,
                    "working",
                    "install",
                    TransferPercent(completedBeforeFile + current, metadata.TotalBytes),
                    completedBeforeFile + current,
                    metadata.TotalBytes,
                    $"正在通过{transferLabel}上传 {file.Path}")),
                cancellationToken);
            uploaded += file.Size;
        }

        var installedInfo = await device.ReadFileAsync(deviceIp, $"/sd/apps/{appId}/app.info", 64 * 1024, cancellationToken);
        var installedVersion = ReadAppInfoVersion(installedInfo);
        if (!installedVersion.Equals(expectedVersion, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"设备端版本校验失败：应为 {expectedVersion}，实际为 {installedVersion}。");

        await reportAsync(new(appId, "working", "install", 100, uploaded, metadata.TotalBytes, "正在刷新设备应用列表"));
        JsonElement rescanResult;
        try
        {
            rescanResult = await device.RescanAppsAsync(deviceIp, cancellationToken);
        }
        catch (HttpRequestException error)
        {
            throw new InvalidOperationException("DevTools 应用刷新接口不可用，请将设备 DevTools 更新到 1.0.3 或更高版本。", error);
        }
        if (!rescanResult.TryGetProperty("rescanned", out var rescannedNode) || rescannedNode.ValueKind != JsonValueKind.True)
        {
            throw new InvalidOperationException("DevTools 未确认应用列表刷新成功。");
        }

        await reportAsync(new(appId, "working", "install", 100, uploaded, metadata.TotalBytes, "正在确认应用注册结果"));
        var visible = false;
        for (var attempt = 0; attempt < 12 && !visible; attempt++)
        {
            if (attempt > 0) await Task.Delay(500, cancellationToken);
            try
            {
                visible = DeviceStateContainsApp(await device.GetStateAsync(deviceIp, cancellationToken), appId, expectedVersion);
            }
            catch (HttpRequestException) when (attempt < 5) { }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested && attempt < 5) { }
        }
        if (!visible) throw new InvalidOperationException("DevTools 已完成应用重扫，但系统应用列表中仍未找到该版本。请检查 app.info 的名称、入口和版本格式。");

        await reportAsync(new(appId, "success", "installed", 100, uploaded, metadata.TotalBytes,
            $"{appId} {expectedVersion} 已安装到设备"));
        _log.Info("应用商店", $"{appId} {expectedVersion} 已从电脑缓存通过{transferLabel}安装到 {deviceIp}");
        return new(appId, expectedVersion, metadata.RepositoryUrl, metadata.Files.Count, metadata.TotalBytes);
    }

    private async Task<ServerPackageManifest> GetServerManifestAsync(string appId, CancellationToken cancellationToken)
    {
        var uri = new Uri($"{StoreServerClient.DefaultServer}/v1/apps/{Uri.EscapeDataString(appId)}/description.json?channel=stable");
        using var response = await _client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = document.RootElement;
        var returnedId = root.TryGetProperty("app_id", out var idNode) ? idNode.GetString() ?? string.Empty : string.Empty;
        var version = root.TryGetProperty("version", out var versionNode) ? versionNode.GetString() ?? string.Empty : string.Empty;
        var files = new List<ServerPackageFile>();
        if (root.TryGetProperty("files", out var fileNodes) && fileNodes.ValueKind == JsonValueKind.Array)
        {
            foreach (var node in fileNodes.EnumerateArray())
            {
                var path = node.TryGetProperty("path", out var pathNode) ? NormalizeRelativePath(pathNode.GetString()) : null;
                var size = node.TryGetProperty("size", out var sizeNode) && sizeNode.TryGetInt64(out var parsedSize) ? parsedSize : -1;
                if (path is null || size < 0 || size > MaxPackageBytes) throw new InvalidOperationException("服务器文件清单包含无效路径或大小。");
                files.Add(new(path, size));
            }
        }
        return new(returnedId, version, files, files.Sum(file => file.Size));
    }

    private async Task<byte[]> DownloadFileAsync(GitHubSource source, ServerPackageFile file, CancellationToken cancellationToken)
    {
        var uri = BuildRawUri(source, file.Path);
        Exception? lastError = null;
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                using var response = await _client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                if (response.StatusCode == HttpStatusCode.NotFound)
                    throw new InvalidOperationException($"GitHub 原仓库缺少 {file.Path}，请改用“设备下载”。");
                response.EnsureSuccessStatusCode();
                if (response.Content.Headers.ContentLength is long headerSize && headerSize != file.Size)
                    throw new InvalidOperationException($"GitHub 文件大小不匹配：{file.Path}，商店 {file.Size}，GitHub {headerSize}。");
                var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
                if (bytes.LongLength != file.Size)
                    throw new InvalidOperationException($"GitHub 文件大小不匹配：{file.Path}，商店 {file.Size}，GitHub {bytes.LongLength}。");
                return bytes;
            }
            catch (InvalidOperationException) { throw; }
            catch (Exception error) when (error is HttpRequestException or IOException or TaskCanceledException)
            {
                lastError = error;
                if (attempt < 3) await Task.Delay(350 * attempt, cancellationToken);
            }
        }
        throw new InvalidOperationException($"连续三次读取 GitHub 文件失败：{file.Path}。请检查电脑能否访问 GitHub，或改用“设备下载”。", lastError);
    }

    private static async Task WriteLocalFileAsync(string root, string relativePath, byte[] bytes, CancellationToken cancellationToken)
    {
        var fullPath = SafeLocalPath(root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        await File.WriteAllBytesAsync(fullPath, bytes, cancellationToken);
    }

    private static async Task UploadWithFirmwareFsAsync(
        DeviceApiClient device,
        string deviceIp,
        string appId,
        string localRoot,
        ServerPackageFile file,
        string transferMethod,
        Func<long, Task> reportAsync,
        CancellationToken cancellationToken)
    {
        Exception? lastError = null;
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                var devicePath = $"/sd/apps/{appId}/{file.Path}";
                var localPath = SafeLocalPath(localRoot, file.Path);
                if (transferMethod == "devtools")
                {
                    await device.UploadLocalFileViaDevToolsAsync(deviceIp, devicePath, localPath, (completed, _) => reportAsync(completed), cancellationToken);
                }
                else
                {
                    await device.UploadLocalFileAsync(deviceIp, devicePath, localPath, (completed, _) => reportAsync(completed), cancellationToken);
                }
                return;
            }
            catch (Exception error) when (error is HttpRequestException or IOException or TaskCanceledException)
            {
                lastError = error;
                if (attempt < 3) await Task.Delay(500 * attempt, cancellationToken);
            }
        }
        throw new InvalidOperationException($"连续三次上传 {file.Path} 失败：{lastError?.Message}", lastError);
    }

    private static string ReadAppInfoVersion(byte[] bytes)
    {
        var text = Encoding.UTF8.GetString(bytes);
        var match = Regex.Match(text, @"(?im)^\s*version\s*=\s*([^\r\n#;]+)");
        var version = match.Success ? match.Groups[1].Value.Trim().Trim('"', '\'') : string.Empty;
        if (string.IsNullOrWhiteSpace(version)) throw new InvalidOperationException("GitHub app.info 没有有效版本号。");
        return version;
    }

    private static string ValidateAppId(string value)
    {
        value = value.Trim();
        if (!Regex.IsMatch(value, @"^[A-Za-z0-9._-]{1,80}$")) throw new ArgumentException("应用 ID 不适合用于 GitHub 下载。");
        return value;
    }

    private static string ValidateVersion(string value)
    {
        value = (value ?? string.Empty).Trim();
        if (!Regex.IsMatch(value, @"^[A-Za-z0-9._-]{1,80}$")) throw new ArgumentException("应用版本号无效。");
        return value;
    }

    private static string NormalizeTransferMethod(string value)
    {
        value = (value ?? string.Empty).Trim().ToLowerInvariant();
        return value is "devtools" ? "devtools" : "fs";
    }

    private string CacheDirectory(string appId, string version)
    {
        var path = Path.GetFullPath(Path.Combine(_cacheRoot, ValidateAppId(appId), ValidateVersion(version)));
        var root = Path.GetFullPath(_cacheRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("应用缓存路径无效。");
        return path;
    }

    private static async Task<CachedPackageMetadata> ReadCachedMetadataAsync(string localRoot, CancellationToken cancellationToken)
    {
        var metadataPath = Path.Combine(localRoot, "package.json");
        if (!File.Exists(metadataPath)) throw new InvalidOperationException("尚未下载到电脑，请先点击下载。");
        await using var stream = File.OpenRead(metadataPath);
        return await JsonSerializer.DeserializeAsync<CachedPackageMetadata>(stream, cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("电脑缓存清单无效，请重新下载。");
    }

    private static bool CacheFilesAreValid(string localRoot, CachedPackageMetadata metadata)
    {
        if (metadata.Files.Count is 0 or > MaxFiles || metadata.TotalBytes <= 0 || metadata.Files.Sum(file => file.Size) != metadata.TotalBytes) return false;
        foreach (var file in metadata.Files)
        {
            try
            {
                var path = SafeLocalPath(localRoot, file.Path);
                if (!File.Exists(path) || new FileInfo(path).Length != file.Size) return false;
            }
            catch { return false; }
        }
        return true;
    }

    private static bool DeviceStateContainsApp(JsonElement state, string appId, string expectedVersion)
    {
        foreach (var property in new[] { "installed_apps", "apps" })
        {
            if (!state.TryGetProperty(property, out var apps) || apps.ValueKind != JsonValueKind.Array) continue;
            foreach (var item in apps.EnumerateArray())
            {
                var id = item.TryGetProperty("id", out var idNode) ? idNode.GetString() : null;
                if (!string.Equals(id, appId, StringComparison.OrdinalIgnoreCase)) continue;
                var version = item.TryGetProperty("version", out var versionNode) ? versionNode.GetString() : null;
                return string.IsNullOrWhiteSpace(version) || string.Equals(version, expectedVersion, StringComparison.OrdinalIgnoreCase);
            }
        }
        return false;
    }

    private static async Task EnsureDevToolsDirectoriesAsync(
        DeviceApiClient device,
        string deviceIp,
        string appId,
        IReadOnlyList<ServerPackageFile> files,
        CancellationToken cancellationToken)
    {
        var root = $"/sd/apps/{appId}";
        var directories = new HashSet<string>(StringComparer.Ordinal) { root };
        foreach (var file in files)
        {
            var parts = file.Path.Split('/', StringSplitOptions.RemoveEmptyEntries);
            for (var depth = 1; depth < parts.Length; depth++)
            {
                directories.Add(root + "/" + string.Join('/', parts.Take(depth)));
            }
        }
        foreach (var directory in directories.OrderBy(path => path.Count(character => character == '/')))
        {
            try
            {
                await device.ListFilesAsync(deviceIp, directory, cancellationToken);
            }
            catch (HttpRequestException error) when (error.StatusCode == HttpStatusCode.NotFound)
            {
                await device.CreateDirectoryAsync(deviceIp, directory, cancellationToken);
            }
        }
    }

    private static string? NormalizeRelativePath(string? value)
    {
        value = value?.Trim().Replace('\\', '/');
        if (string.IsNullOrWhiteSpace(value) || value.StartsWith('/') || value.Contains(':')) return null;
        var segments = value.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments.Length == 0 || segments.Any(segment => segment is "." or "..") ? null : string.Join('/', segments);
    }

    private static string SafeLocalPath(string root, string relativePath)
    {
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var path = Path.GetFullPath(Path.Combine(fullRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!path.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("应用文件路径越过临时目录。");
        return path;
    }

    private static Uri BuildRawUri(GitHubSource source, string relativePath)
    {
        var path = string.Join('/', new[] { source.PackagePrefix, relativePath }.Where(value => !string.IsNullOrWhiteSpace(value))
            .SelectMany(value => value.Split('/', StringSplitOptions.RemoveEmptyEntries))
            .Select(Uri.EscapeDataString));
        return new Uri($"https://raw.githubusercontent.com/{source.Repository}/{Uri.EscapeDataString(source.Branch)}/{path}");
    }

    private static GitHubSource ResolveSource(string appId)
    {
        if (StandaloneSources.TryGetValue(appId, out var source)) return source;
        return new("clocteck/holocubic-apps", "main", $"{appId}/package");
    }

    private static int TransferPercent(long completed, long total) => total <= 0 ? 0 : (int)Math.Clamp(completed * 100 / total, 0, 100);

    public void Dispose() => _client.Dispose();

    private static readonly Dictionary<string, GitHubSource> StandaloneSources = new(StringComparer.OrdinalIgnoreCase)
    {
        ["hidpad"] = new("clocteck/hidpad", "main", "package"),
        ["holo-retro-go"] = new("clocteck/holo-retro-go", "main", "package"),
        ["holocubic-airplay-service"] = new("sunfang1cn/holocubic-airplay-service", "main", "package"),
        ["holocubic-lua-claw"] = new("clocteck/holocubic-lua-claw", "main", "package"),
        ["holocubic-smtc-music"] = new("Brownlzy/holocubic-smtc-music", "master", "package"),
        ["holopet"] = new("MadLongTom/holopet", "main", "package"),
        ["time-calendar-weather-memo"] = new("clocteck/time-calendar-weather-memo", "main", "package"),
    };

    private sealed record GitHubSource(string Repository, string Branch, string PackagePrefix)
    {
        public string RepositoryUrl => "https://github.com/" + Repository;
    }
    private sealed record ServerPackageFile(string Path, long Size);
    private sealed record ServerPackageManifest(string AppId, string Version, IReadOnlyList<ServerPackageFile> Files, long TotalBytes);
    private sealed record CachedPackageMetadata(string AppId, string Version, string RepositoryUrl, long TotalBytes, List<ServerPackageFile> Files);
}

public sealed record GitHubStoreProgress(
    string AppId,
    string Status,
    string Phase,
    int Percent,
    long Completed,
    long Total,
    string Message);

public sealed record GitHubStoreInstallResult(
    string AppId,
    string Version,
    string RepositoryUrl,
    int FileCount,
    long TotalBytes);

public sealed record GitHubStoreDownloadResult(
    string AppId,
    string Version,
    string RepositoryUrl,
    int FileCount,
    long TotalBytes,
    string CachePath);

public sealed record CachedStorePackage(
    string AppId,
    string Version,
    string RepositoryUrl,
    int FileCount,
    long TotalBytes);
