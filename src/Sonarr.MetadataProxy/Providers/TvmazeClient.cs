using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Sonarr.MetadataProxy.Models.Tvmaze;

namespace Sonarr.MetadataProxy.Providers;

public sealed class TvmazeClient : ITvmazeApi
{
    private const string Endpoint = "https://api.tvmaze.com";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _http;
    private readonly TvmazeRateLimiter _rateLimiter;
    private readonly ILogger<TvmazeClient> _logger;

    public TvmazeClient(HttpClient http, TvmazeRateLimiter rateLimiter, ILogger<TvmazeClient> logger)
    {
        _http = http;
        _rateLimiter = rateLimiter;
        _logger = logger;
    }

    public async Task<IReadOnlyList<TvmazeShow>> SearchShowsAsync(string query, CancellationToken cancellationToken)
    {
        var url = $"{Endpoint}/search/shows?q={Uri.EscapeDataString(query)}";
        var hits = await GetAsync<List<TvmazeSearchHit>>(url, cancellationToken, allowNotFound: true).ConfigureAwait(false);
        if (hits is null)
        {
            return Array.Empty<TvmazeShow>();
        }

        return hits
            .Where(hit => hit.Show is not null)
            .Select(hit => hit.Show!)
            .ToList();
    }

    public async Task<TvmazeShow?> GetShowAsync(int tvmazeId, CancellationToken cancellationToken)
    {
        var url = $"{Endpoint}/shows/{tvmazeId}?embed[]=seasons&embed[]=cast";
        return await GetAsync<TvmazeShow>(url, cancellationToken, allowNotFound: true).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<TvmazeEpisode>> GetEpisodesAsync(int tvmazeId, CancellationToken cancellationToken)
    {
        var url = $"{Endpoint}/shows/{tvmazeId}/episodes";
        var episodes = await GetAsync<List<TvmazeEpisode>>(url, cancellationToken, allowNotFound: true).ConfigureAwait(false);
        return episodes ?? new List<TvmazeEpisode>();
    }

    public async Task<TvmazeShow?> FindByImdbAsync(string imdbId, CancellationToken cancellationToken)
    {
        var url = $"{Endpoint}/lookup/shows?imdb={Uri.EscapeDataString(imdbId)}";
        return await GetAsync<TvmazeShow>(url, cancellationToken, allowNotFound: true).ConfigureAwait(false);
    }

    public async Task<TvmazeShow?> FindByThetvdbAsync(int tvdbId, CancellationToken cancellationToken)
    {
        var url = $"{Endpoint}/lookup/shows?thetvdb={tvdbId}";
        return await GetAsync<TvmazeShow>(url, cancellationToken, allowNotFound: true).ConfigureAwait(false);
    }

    private async Task<T?> GetAsync<T>(string url, CancellationToken cancellationToken, bool allowNotFound)
    {
        await _rateLimiter.WaitAsync(cancellationToken).ConfigureAwait(false);

        const int maxRetries = 3;
        var attempt = 0;

        while (true)
        {
            try
            {
                using var response = await _http.GetAsync(url, cancellationToken).ConfigureAwait(false);

                if (response.StatusCode == System.Net.HttpStatusCode.NotFound && allowNotFound)
                {
                    _logger.LogInformation("TVMaze reported not found for '{Url}'.", url);
                    return default;
                }

                if (!response.IsSuccessStatusCode)
                {
                    var status = (int)response.StatusCode;
                    _logger.LogWarning("TVMaze returned {Status} for '{Url}'.", status, url);

                    var isRetryable = status is 429 or >= 500;
                    if (isRetryable && attempt < maxRetries)
                    {
                        attempt++;
                        var delay = TimeSpan.FromMilliseconds(300 * Math.Pow(2, attempt - 1));
                        _logger.LogInformation("Retrying TVMaze request in {Delay}ms (attempt {Attempt}/{MaxRetries}).", delay.TotalMilliseconds, attempt, maxRetries);
                        await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                        continue;
                    }

                    throw new TvmazeApiException($"TVMaze API error {status}.");
                }

                return await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken).ConfigureAwait(false);
            }
            catch (TvmazeApiException)
            {
                throw;
            }
            catch (HttpRequestException ex)
            {
                throw new TvmazeApiException("TVMaze request failed.", ex);
            }
            catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TvmazeApiException("TVMaze request timed out.", ex);
            }
            catch (JsonException ex)
            {
                throw new TvmazeApiException("TVMaze response could not be parsed.", ex);
            }
        }
    }
}