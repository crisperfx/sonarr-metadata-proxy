using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Sonarr.MetadataProxy.Models.Tmdb;
using Sonarr.MetadataProxy.Options;

namespace Sonarr.MetadataProxy.Providers;

public sealed class TmdbClient : ITmdbApi
{
    private readonly ProxyOptions _options;
    private readonly ILogger<TmdbClient> _logger;
    private readonly HttpClient _http;
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    public TmdbClient(ProxyOptions options, ILogger<TmdbClient> logger)
    {
        _options = options;
        _logger = logger;

        var handler = new RetryingHandler
        {
            InnerHandler = new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli
            }
        };

        _http = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.themoviedb.org/3/"),
            Timeout = TimeSpan.FromSeconds(30)
        };

        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("SonarrMetadataProxy/0.1");

        if (!_options.HasTmdbAuth)
        {
            _logger.LogWarning(
                "No TMDB credentials configured (TMDB_API_KEY or TMDB_API_TOKEN). TMDB metadata calls will fail and fall back to TVDB when ENABLE_TVDB_FALLBACK=true.");
        }
        else if (!string.IsNullOrWhiteSpace(_options.TmdbApiToken))
        {
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _options.TmdbApiToken);
        }
    }

    public async Task<List<TmdbTvSearchResult>> SearchTvAsync(string query, CancellationToken cancellationToken)
    {
        ThrowIfUnauthorized();

        var parameters = $"language={Uri.EscapeDataString(_options.TmdBLanguage)}" +
                         $"&query={Uri.EscapeDataString(query)}";

        var response = await GetAsync<TmdbTvSearchResponse>($"search/tv?{parameters}", cancellationToken).ConfigureAwait(false);
        return response.Results;
    }

    public async Task<List<TmdbTvSearchResult>> FindByImdbAsync(string imdbId, CancellationToken cancellationToken)
    {
        ThrowIfUnauthorized();

        var parameters = "language=" + Uri.EscapeDataString(_options.TmdBLanguage) +
                         "&external_source=imdb_id";

        var response = await GetAsync<TmdbFindResponse>($"find/{Uri.EscapeDataString(imdbId)}?{parameters}", cancellationToken).ConfigureAwait(false);
        return response.TvResults;
    }

    public async Task<TmdbTvDetails> GetTvDetailsAsync(int tmdbId, CancellationToken cancellationToken)
    {
        ThrowIfUnauthorized();

        var parameters = "language=" + Uri.EscapeDataString(_options.TmdBLanguage) +
                         "&append_to_response=external_ids,credits,content_ratings";

        return await GetAsync<TmdbTvDetails>($"tv/{tmdbId}?{parameters}", cancellationToken).ConfigureAwait(false);
    }

    public async Task<List<TmdbEpisode>> GetSeasonEpisodesAsync(int tmdbId, int seasonNumber, CancellationToken cancellationToken)
    {
        ThrowIfUnauthorized();

        var parameters = "language=" + Uri.EscapeDataString(_options.TmdBLanguage);
        var response = await GetAsync<TmdbSeasonResponse>($"tv/{tmdbId}/season/{seasonNumber}?{parameters}", cancellationToken).ConfigureAwait(false);
        return response.Episodes;
    }

    private async Task<T> GetAsync<T>(string requestUri, CancellationToken cancellationToken)
    {
        var uri = requestUri;
        if (string.IsNullOrWhiteSpace(_options.TmdbApiToken))
        {
            uri += (uri.Contains('?') ? "&" : "?") + "api_key=" + Uri.EscapeDataString(_options.TmdbApiKey ?? string.Empty);
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, uri);

        try
        {
            using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                throw new TmdbApiException(
                    $"TMDB API returned {(int)response.StatusCode} for {requestUri}. {Truncate(body, 200)}",
                    (int)response.StatusCode);
            }

            var result = JsonSerializer.Deserialize<T>(body, _jsonOptions);
            if (result is null)
            {
                throw new TmdbApiException($"TMDB API returned an empty payload for {requestUri}.", 0);
            }

            return result;
        }
        catch (TmdbApiException)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new TmdbApiException($"Failed to reach TMDB API for {requestUri}.", ex);
        }
    }

    private void ThrowIfUnauthorized()
    {
        if (_options.HasTmdbAuth)
        {
            return;
        }

        throw new TmdbApiException(
            "TMDB credentials are not configured. Set TMDB_API_KEY or TMDB_API_TOKEN.",
            0);
    }

    private static string Truncate(string value, int maxLength)
    {
        return value.Length <= maxLength ? value : value.Substring(0, maxLength);
    }

    private sealed class RetryingHandler : DelegatingHandler
    {
        private const int MaxAttempts = 3;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            for (var attempt = 1; ; attempt++)
            {
                var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);

                if (response.IsSuccessStatusCode || attempt >= MaxAttempts)
                {
                    return response;
                }

                if ((int)response.StatusCode is not (429 or >= 500))
                {
                    return response;
                }

                var delay = TimeSpan.FromMilliseconds(300 * Math.Pow(2, attempt - 1));
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
        }
    }
}