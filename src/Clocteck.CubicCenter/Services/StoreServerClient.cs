using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;
using Clocteck.CubicCenter.Core;

namespace Clocteck.CubicCenter.Services;

public sealed class StoreServerClient : IDisposable
{
    public const string DefaultServer = "https://cubic.clocteck.com";
    private static readonly Uri CatalogUri = new($"{DefaultServer}/v1/apps/catalog?channel=stable");

    private readonly AppLog _log;
    private readonly HttpClient _client = new(new SocketsHttpHandler
    {
        ConnectTimeout = TimeSpan.FromSeconds(8),
        AllowAutoRedirect = false,
        MaxConnectionsPerServer = 8,
        AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
    })
    {
        Timeout = TimeSpan.FromSeconds(30),
    };

    public StoreServerClient(AppLog log)
    {
        _log = log;
        _client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("ClocteckCubicCenter", "0.1.0"));
        _client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<JsonElement> GetCatalogAsync(CancellationToken cancellationToken)
    {
        _log.Info("应用商店", "电脑正在直接读取官方应用目录");
        using var response = await _client.GetAsync(CatalogUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var catalog = await JsonNode.ParseAsync(stream, cancellationToken: cancellationToken) as JsonObject;
        if (catalog?["items"] is not JsonArray items)
        {
            throw new InvalidOperationException("官方应用目录格式无效。");
        }

        foreach (var item in items.OfType<JsonObject>())
        {
            var appId = item["app_id"]?.GetValue<string>()?.Trim();
            if (string.IsNullOrWhiteSpace(appId)) continue;
            item["description_json_url"] ??= $"{DefaultServer}/v1/apps/{Uri.EscapeDataString(appId)}/description.json?channel=stable";
            item["description_page_url"] ??= $"{DefaultServer}/apps/{Uri.EscapeDataString(appId)}?channel=stable";
        }

        return JsonSerializer.SerializeToElement(catalog);
    }

    public static bool IsTrustedStoreUri(string? value, string requiredPathPrefix, out Uri uri)
    {
        if (Uri.TryCreate(value, UriKind.Absolute, out var candidate) &&
            candidate.Scheme == Uri.UriSchemeHttps &&
            candidate.Host.Equals("cubic.clocteck.com", StringComparison.OrdinalIgnoreCase) &&
            candidate.AbsolutePath.StartsWith(requiredPathPrefix, StringComparison.Ordinal))
        {
            uri = candidate;
            return true;
        }

        uri = null!;
        return false;
    }

    public void Dispose() => _client.Dispose();
}
