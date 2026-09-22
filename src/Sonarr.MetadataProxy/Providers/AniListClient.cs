using System.Net.Http.Json;
using System.Text.Json;
using Sonarr.MetadataProxy.Models.AniList;

namespace Sonarr.MetadataProxy.Providers;

public sealed class AniListClient : IAniListApi
{
    private const string Endpoint = "https://graphql.anilist.co";
    private const int MaxConcurrentRequests = 4;

    private readonly HttpClient _http;
    private readonly ILogger<AniListClient> _logger;
    private readonly SemaphoreSlim _gate = new(MaxConcurrentRequests, MaxConcurrentRequests);

    public AniListClient(HttpClient http, ILogger<AniListClient> logger)
    {
        _http = http;
        _logger = logger;
    }

    public async Task<IReadOnlyList<AniListMedia>> SearchAsync(string query, CancellationToken cancellationToken)
    {
        var variables = new { term = query, perPage = 20 };
        return await ExecuteAsync(
            "query ($term: String, $perPage: Int) { Page(page: 1, perPage: $perPage) { media(search: $term, type: ANIME) { "
            + MediaFields
            + " } } }",
            variables,
            data => data.GetProperty("Page").GetProperty("media").EnumerateArray().Select(ParseMedia).ToList(),
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<AniListMedia?> GetByIdAsync(int anilistId, CancellationToken cancellationToken)
    {
        var variables = new { id = anilistId };
        var results = await ExecuteAsync(
            "query ($id: Int) { Media(id: $id, type: ANIME) { " + MediaFields + " } }",
            variables,
            data => data.TryGetProperty("Media", out var media) && media.ValueKind == JsonValueKind.Object
                ? new List<AniListMedia> { ParseMedia(media) }
                : new List<AniListMedia>(),
            cancellationToken).ConfigureAwait(false);

        return results.FirstOrDefault();
    }

    private const string MediaFields =
        "id idMal title { romaji english native } synonyms format episodes duration status "
        + "startDate { year month day } endDate { year month day } seasonYear averageScore meanScore "
        + "description(asHtml: false) coverImage { extraLarge large medium } bannerImage genres "
        + "countryOfOrigin studios(isMain: true) { nodes { name } }";

    private async Task<TResult> ExecuteAsync<TResult>(
        string query,
        object variables,
        Func<JsonElement, TResult> extract,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint);
            request.Headers.Add("Accept", "application/json");
            request.Content = JsonContent.Create(new { query, variables });

            using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("AniList GraphQL returned {Status}: {Body}", (int)response.StatusCode, Truncate(body));
                throw new AniListApiException($"AniList API error {response.StatusCode}.");
            }

            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            if (root.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Array && errors.GetArrayLength() > 0)
            {
                var message = errors[0].TryGetProperty("message", out var err) ? err.GetString() : null;
                _logger.LogWarning("AniList GraphQL error: {Message}", message);
                throw new AniListApiException("AniList GraphQL error: " + message);
            }

            return extract(root.TryGetProperty("data", out var data) ? data : root);
        }
        catch (AniListApiException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new AniListApiException("AniList request failed.", ex);
        }
        finally
        {
            _gate.Release();
        }
    }

    private AniListMedia ParseMedia(JsonElement element)
    {
        var firstAir = ParseDate(element, "startDate");
        var lastAir = ParseDate(element, "endDate");

        var title = element.TryGetProperty("title", out var titleNode) && titleNode.ValueKind == JsonValueKind.Object
            ? titleNode
            : default;

        var romaji = GetString(title, "romaji");
        var english = GetString(title, "english");
        var native = GetString(title, "native");

        var synonyms = GetStringList(element, "synonyms");
        var genres = GetStringList(element, "genres");

        var studio = GetStudio(element);

        var alternativeTitles = BuildAlternativeTitles(english, romaji, native, synonyms);

        return new AniListMedia
        {
            Id = GetInt(element, "id"),
            IdMal = GetNullableInt(element, "idMal"),
            TitleRomaji = romaji,
            TitleEnglish = english,
            TitleNative = native,
            Synonyms = synonyms,
            Episodes = GetNullableInt(element, "episodes"),
            DurationMinutes = GetNullableInt(element, "duration"),
            Status = GetString(element, "status"),
            FirstAirDate = firstAir,
            LastAirDate = lastAir,
            AverageScore = GetNullableInt(element, "averageScore") ?? 0d,
            Description = GetString(element, "description"),
            PosterUrl = GetCoverImage(element),
            BannerUrl = GetString(element, "bannerImage"),
            Genres = genres,
            CountryOfOrigin = GetString(element, "countryOfOrigin"),
            Studio = studio,
            AlternativeTitles = alternativeTitles
        };
    }

    private static List<string> BuildAlternativeTitles(string? english, string? romaji, string? native, List<string> synonyms)
    {
        var result = new List<string>();
        foreach (var title in new[] { english, romaji, native })
        {
            if (!string.IsNullOrWhiteSpace(title) && !result.Contains(title))
            {
                result.Add(title);
            }
        }

        foreach (var synonym in synonyms)
        {
            if (!string.IsNullOrWhiteSpace(synonym) && !result.Contains(synonym))
            {
                result.Add(synonym);
            }
        }

        return result;
    }

    private static string? GetStudio(JsonElement element)
    {
        if (!element.TryGetProperty("studios", out var studios) || studios.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (var node in GetNodes(studios))
        {
            if (GetString(node, "name") is { } name)
            {
                return name;
            }
        }

        return null;
    }

    private static IEnumerable<JsonElement> GetNodes(JsonElement parent)
    {
        if (!parent.TryGetProperty("nodes", out var nodes) || nodes.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<JsonElement>();
        }

        return nodes.EnumerateArray().ToList();
    }

    private static string? GetCoverImage(JsonElement element)
    {
        if (!element.TryGetProperty("coverImage", out var cover) || cover.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return GetString(cover, "large") ?? GetString(cover, "medium") ?? GetString(cover, "extraLarge");
    }

    private static string? ParseDate(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var node) || node.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var year = GetNullableInt(node, "year");
        if (year is not > 0)
        {
            return null;
        }

        var month = GetNullableInt(node, "month") ?? 0;
        var day = GetNullableInt(node, "day") ?? 0;

        if (month is < 1 or > 12 || day < 1 || day > 31)
        {
            return year.Value.ToString("D4");
        }

        var date = new DateTime(year.Value, month, Math.Min(day, 28));
        return date.ToString("yyyy-MM-dd");
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