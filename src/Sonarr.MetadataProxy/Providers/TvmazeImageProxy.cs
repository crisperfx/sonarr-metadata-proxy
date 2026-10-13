using System.Collections.Concurrent;
using System.Net.Http;
using System.Text;
using Sonarr.MetadataProxy.Options;

namespace Sonarr.MetadataProxy.Providers;

public sealed class TvmazeImageProxy
{
    private readonly HttpClient _http;
    private readonly ProxyOptions _options;
    private readonly ILogger<TvmazeImageProxy> _logger;
    private readonly TimeSpan _cacheTtl;
    private readonly ConcurrentDictionary<string, CachedImage> _cache = new();

    private sealed record CachedImage(DateTimeOffset StoredAt, byte[] Bytes, string ContentType);

    public TvmazeImageProxy(HttpClient http, ProxyOptions options, ILogger<TvmazeImageProxy> logger)
    {
        _http = http;
        _options = options;
        _logger = logger;
        _cacheTtl = TimeSpan.FromMinutes(options.CacheTtlMinutes);
    }

    public async Task<IResult> GetImageAsync(string pictureUrl, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(pictureUrl)) return Results.NotFound();

        if (_cache.TryGetValue(pictureUrl, out var cached) && DateTimeOffset.UtcNow - cached.StoredAt < _cacheTtl)
        {
            return Results.File(cached.Bytes, cached.ContentType, enableRangeProcessing: true);
        }

        try
        {
            using var response = await _http.GetAsync(pictureUrl, ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Tvmaze image proxy: {RemoteStatus} for {Url}", (int)response.StatusCode, pictureUrl);
                return Results.StatusCode((int)response.StatusCode);
            }

            var bytes = await response.Content.ReadAsByteArrayAsync(ct);
            var contentType = response.Content.Headers.ContentType?.ToString() ?? "application/octet-stream";
            _cache[pictureUrl] = new CachedImage(DateTimeOffset.UtcNow, bytes, contentType);

            return Results.File(bytes, contentType, enableRangeProcessing: true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Tvmaze image proxy failed for {Url}", pictureUrl);
            return Results.StatusCode(503);
        }
    }
}