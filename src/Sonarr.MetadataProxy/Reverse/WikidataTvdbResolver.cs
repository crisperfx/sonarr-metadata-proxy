using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Text.Json;
using Sonarr.MetadataProxy.Models.Tmdb;
using Sonarr.MetadataProxy.Providers;

namespace Sonarr.MetadataProxy.Reverse;

public interface ITvdbToTmdbResolver
{
    Task<int?> ResolveTmdbIdAsync(int tvdbId, string? title, int? year, CancellationToken cancellationToken);
}

public sealed class WikidataTvdbResolver : ITvdbToTmdbResolver
{
    private readonly ConcurrentDictionary<int, int?> _cache = new();
    private readonly ILogger<WikidataTvdbResolver> _logger;
    private readonly HttpClient _http;
    private readonly ITmdbApi _tmdb;

    public WikidataTvdbResolver(ITmdbApi tmdb, ILogger<WikidataTvdbResolver> logger)
    {
        _logger = logger;
        _tmdb = tmdb;
        _http = new HttpClient
        {
            BaseAddress = new Uri("https://query.wikidata.org/sparql"),
            Timeout = TimeSpan.FromSeconds(15)
        };
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/sparql-results+json"));
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("SonarrMetadataProxy/0.1 (metadata mapping lookups)");
    }

    public async Task<int?> ResolveTmdbIdAsync(int tvdbId, string? title, int? year, CancellationToken cancellationToken)
    {
        if (_cache.TryGetValue(tvdbId, out var cached))
        {
            return cached;
        }

        int? result = null;
        try
        {
            var sparql = "SELECT ?tmdbId WHERE { " +
                         $"?item wdt:P12196 \"{tvdbId}\" . " +
                         "?item wdt:P4985 ?tmdbId . }";

            using var request = new HttpRequestMessage(HttpMethod.Get,
                "?query=" + Uri.EscapeDataString(sparql));
            using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Wikidata reverse lookup for TVDB {TvdbId} returned {(int)StatusCode}.",
                    tvdbId,
                    (int)response.StatusCode);
                return null;
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            result = ParseTmdbId(body);
            _logger.LogInformation(
                result.HasValue
                    ? "Wikidata mapping found: TVDB {TvdbId} -> TMDB {TmdbId}."
                    : "Wikidata mapping unavailable for TVDB {TvdbId}.",
                tvdbId,
                result);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Wikidata reverse lookup failed for TVDB {TvdbId}.", tvdbId);
        }

        // Validate that the resolved ID actually exists on TMDB
        if (result is > 0)
        {
            try
            {
                var details = await _tmdb.GetTvDetailsAsync(result.Value, cancellationToken).ConfigureAwait(false);
                if (details is null)
                {
                    _logger.LogWarning("Wikidata resolved TMDB ID {TmdbId} for TVDB {TvdbId} does not exist on TMDB (likely a numeric collision).", result, tvdbId);
                    result = null;
                }
            }
            catch (TmdbApiException ex)
            {
                _logger.LogWarning("TMDB validation failed for Wikidata resolved ID {TmdbId} for TVDB {TvdbId}: {Message}", result, tvdbId, ex.Message);
                result = null;
            }
        }

        _cache[tvdbId] = result;
        return result;
    }

    private static int? ParseTmdbId(string sparqlJson)
    {
        using var document = JsonDocument.Parse(sparqlJson);
        if (!document.RootElement.TryGetProperty("results", out var results))
        {
            return null;
        }

        foreach (var binding in results.GetProperty("bindings").EnumerateArray())
        {
            if (binding.TryGetProperty("tmdbId", out var node) &&
                node.TryGetProperty("value", out var value) &&
                int.TryParse(value.GetString(), out var tmdbId))
            {
                return tmdbId;
            }
        }

        return null;
    }
}