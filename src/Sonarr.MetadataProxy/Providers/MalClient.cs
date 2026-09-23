using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Sonarr.MetadataProxy.Models.Mal;

namespace Sonarr.MetadataProxy.Providers;

public sealed class MalClient : IMalApi
{
    private const string Endpoint = "https://api.tenrai.org/v1";
    private const int SearchLimit = 20;

    private readonly HttpClient _http;
    private readonly ILogger<MalClient> _logger;
    private readonly TenraiRateLimiter _rateLimiter;

    public MalClient(HttpClient http, ILogger<MalClient> logger, TenraiRateLimiter rateLimiter)
    {
        _http = http;
        _logger = logger;
        _rateLimiter = rateLimiter;
    }

    public async Task<IReadOnlyList<MalAnime>> SearchAsync(string query, CancellationToken cancellationToken)
    {
        var url = $"{Endpoint}/anime/search?q={Uri.EscapeDataString(query)}&limit={SearchLimit}&type=tv&type=tv_special";
        return await ExecuteAsync<IReadOnlyList<MalAnime>>(url, data => data.EnumerateArray().Select(ParseAnime).ToList(), cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<MalAnime?> GetByIdAsync(int malId, CancellationToken cancellationToken)
    {
        var results = await ExecuteAsync<List<MalAnime>>(
            $"{Endpoint}/anime/{malId}",
            data => data.ValueKind == JsonValueKind.Object ? new List<MalAnime> { ParseAnime(data) } : new List<MalAnime>(),
            cancellationToken,
            allowNotFound: true).ConfigureAwait(false);

        return results?.FirstOrDefault();
    }

    public async Task<MalPictures?> GetPicturesAsync(int malId, CancellationToken cancellationToken)
    {
        return await ExecuteAsync<MalPictures?>(
            $"{Endpoint}/anime/{malId}/pictures",
            data =>
            {
                var pictures = new MalPictures();
                if (data.ValueKind != JsonValueKind.Array)
                {
                    return pictures;
                }

                foreach (var item in data.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    if (item.TryGetProperty("url", out var urlProp) && urlProp.ValueKind == JsonValueKind.String)
                    {
                        var url = urlProp.GetString();
                        if (!string.IsNullOrWhiteSpace(url))
                        {
                            pictures.Posters.Add(url);
                        }
                    }
                }

                return pictures;
            },
            cancellationToken,
            allowNotFound: true).ConfigureAwait(false);
    }

    private async Task<T> ExecuteAsync<T>(
        string url,
        Func<JsonElement, T> extract,
        CancellationToken cancellationToken,
        bool allowNotFound = false)
    {
        await _rateLimiter.WaitAsync(cancellationToken).ConfigureAwait(false);

        const int maxRetries = 3;
        var baseDelay = TimeSpan.FromSeconds(1);
        var attempt = 0;

        while (true)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Add("Accept", "application/json");

                using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
                var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

                if (response.StatusCode == HttpStatusCode.NotFound && allowNotFound)
                {
                    _logger.LogInformation("MAL (Tenrai) reported not found for '{Url}'.", url);
                    return default!;
                }

                if (!response.IsSuccessStatusCode)
                {
                    var status = (int)response.StatusCode;
                    _logger.LogWarning("MAL (Tenrai) returned {Status}: {Body}", status, Truncate(body));

                    var isRetryable = status == 502 || status == 503 || status == 504;
                    if (isRetryable && attempt < maxRetries)
                    {
                        attempt++;
                        var delay = TimeSpan.FromTicks(baseDelay.Ticks * (1L << (attempt - 1)));
                        _logger.LogInformation("Retrying Tenrai request in {Delay}s (attempt {Attempt}/{MaxRetries}).", delay.TotalSeconds, attempt, maxRetries);
                        await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                        continue;
                    }

                    throw new MalApiException($"Tenrai API error {status}.");
                }

                using var document = JsonDocument.Parse(body);
                return document.RootElement.TryGetProperty("data", out var data) ? extract(data) : default!;
            }
            catch (MalApiException)
            {
                throw;
            }
            catch (HttpRequestException ex)
            {
                throw new MalApiException("Tenrai request failed.", ex);
            }
            catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                throw new MalApiException("Tenrai request timed out.", ex);
            }
            catch (JsonException ex)
            {
                throw new MalApiException("Tenrai response could not be parsed.", ex);
            }
        }
    }

    private MalAnime ParseAnime(JsonElement element)
    {
        var aired = element.TryGetProperty("aired", out var airedNode) && airedNode.ValueKind == JsonValueKind.Object
            ? airedNode
            : default;

        return new MalAnime
        {
            Id = GetNullableInt(element, "id") ?? GetNullableInt(element, "mal_id") ?? 0,
            Title = GetString(element, "title"),
            TitleEnglish = GetString(element, "title_english"),
            TitleJapanese = GetString(element, "title_japanese"),
            Synonyms = GetStringList(element, "title_synonyms") ?? GetStringList(element, "synonyms") ?? new List<string>(),
            Episodes = GetNullableInt(element, "episodes"),
            DurationMinutes = ParseDuration(GetString(element, "duration")),
            Status = GetString(element, "status"),
            FirstAirDate = ParseAiredDate(aired, "from"),
            LastAirDate = ParseAiredDate(aired, "to"),
            Score = GetNullableDouble(element, "score"),
            ScoreCount = GetNullableInt(element, "scored_by") ?? GetNullableInt(element, "score_count"),
            Synopsis = GetString(element, "synopsis") ?? GetString(element, "description"),
            PosterUrl = GetPosterUrl(element),
            Genres = GetNameList(element, "genres"),
            Studio = GetNameList(element, "studios").FirstOrDefault(),
            Type = GetString(element, "type")
        };
    }

    private static int? ParseDuration(string? duration)
    {
        if (string.IsNullOrWhiteSpace(duration) || duration.Equals("Unknown", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var hours = Regex.Match(duration, @"(\d+)\s*(?:hours?|hrs?|h)\b", RegexOptions.IgnoreCase);
        var minutes = Regex.Match(duration, @"(\d+)\s*min", RegexOptions.IgnoreCase);

        var total = 0;
        if (hours.Success && int.TryParse(hours.Groups[1].Value, out var h))
        {
            total += h * 60;
        }

        if (minutes.Success && int.TryParse(minutes.Groups[1].Value, out var m))
        {
            total += m;
        }

        return total > 0 ? total : null;
    }

    private static string? ParseAiredDate(JsonElement aired, string property)
    {
        if (aired.ValueKind != JsonValueKind.Object || !aired.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var text = value.GetString();
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        if (DateTimeOffset.TryParse(text, out var dateTime))
        {
            return dateTime.ToString("yyyy-MM-dd");
        }

        return text.Length >= 10 ? text[..10] : null;
    }

    private static string? GetPosterUrl(JsonElement element)
    {
        if (element.TryGetProperty("poster", out var poster) && poster.ValueKind == JsonValueKind.String)
        {
            return poster.GetString();
        }
        if (!element.TryGetProperty("images", out var images) || images.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (!images.TryGetProperty("jpg", out var jpg) || jpg.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return GetString(jpg, "large_image_url") ?? GetString(jpg, "image_url");
    }

    private static List<string> GetNameList(JsonElement element, string property)
    {
        var result = new List<string>();
        if (!element.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.Array)
        {
            return result;
        }

        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.Object && GetString(item, "name") is { } name)
            {
                result.Add(name);
            }
        }

        return result;
    }

    private static int GetInt(JsonElement element, string property)
    {
        return GetNullableInt(element, property) ?? 0;
    }

    private static int? GetNullableInt(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.Number)
        {
            return null;
        }

        return value.TryGetInt32(out var parsed) ? parsed : null;
    }

    private static double? GetNullableDouble(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.Number)
        {
            return null;
        }

        return value.TryGetDouble(out var parsed) ? parsed : null;
    }

    private static string? GetString(JsonElement element, string property)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var text = value.GetString();
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    private static List<string> GetStringList(JsonElement element, string property)
    {
        var result = new List<string>();
        if (!element.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.Array)
        {
            return result;
        }

        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String && item.GetString() is { Length: > 0 } text)
            {
                result.Add(text);
            }
        }

        return result;
    }

    private static string Truncate(string value, int maxLength = 200)
    {
        return value.Length <= maxLength ? value : value[..maxLength] + "...";
    }
}

public class TenraiRateLimiter
{
    private static readonly TimeSpan MinInterval = TimeSpan.FromMilliseconds(333);
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(1);
    private const int MaxRequestsPerWindow = 60;

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Queue<DateTime> _requestedAt = new();
    private DateTime _lastRequest = DateTime.MinValue;

    public virtual async Task WaitAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var now = DateTime.UtcNow;
            while (_requestedAt.Count > 0 && now - _requestedAt.Peek() >= Window)
            {
                _requestedAt.Dequeue();
            }

            var delay = TimeSpan.Zero;
            if (_requestedAt.Count >= MaxRequestsPerWindow)
            {
                delay = Window - (now - _requestedAt.Peek());
            }

            if (_lastRequest != DateTime.MinValue)
            {
                var sinceLast = now - _lastRequest;
                if (sinceLast < MinInterval)
                {
                    var gap = MinInterval - sinceLast;
                    if (gap > delay)
                    {
                        delay = gap;
                    }
                }
            }

            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }

            _requestedAt.Enqueue(DateTime.UtcNow);
            _lastRequest = DateTime.UtcNow;
        }
        finally
        {
            _gate.Release();
        }
    }
}