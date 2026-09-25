using System.IO.Compression;
using System.Text;
using Sonarr.MetadataProxy.Models.Anidb;
using Sonarr.MetadataProxy.Options;

namespace Sonarr.MetadataProxy.Services;

public sealed record AnidbTitleEntry(int Aid, int Type, string Language, string Title);

public sealed record AnidbTitleHit(int Aid, string Title);

public static class AnidbTitleType
{
    public const int Main = 1;
    public const int Synonym = 2;
    public const int Short = 3;
    public const int Official = 4;
}

/// <summary>
/// Downloads and indexes the AniDB daily title dump (anime-titles.dat.gz).
/// The dump is fetched at most once per day; matching prefers official/main
/// titles and is used to turn a free-text query into AniDB ids.
/// </summary>
public sealed class AnidbTitleList
{
    private const string TitleDumpUrl = "https://anidb.net/api/anime-titles.dat.gz";

    private readonly HttpClient _http;
    private readonly string _filePath;
    private readonly ILogger<AnidbTitleList> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private List<AnidbTitleEntry> _entries = new();
    private Dictionary<int, string> _mainTitles = new();
    private bool _loaded;

    public AnidbTitleList(ProxyOptions options, HttpClient http, ILogger<AnidbTitleList> logger)
    {
        _http = http;
        _logger = logger;
        _filePath = Path.Combine(options.DataDir, "anime-titles", "anime-titles.dat.gz");
    }

    public bool HasIndex
    {
        get
        {
            lock (_entries)
            {
                return _loaded;
            }
        }
    }

    public async Task EnsureLoadedAsync(CancellationToken cancellationToken)
    {
        if (HasIndex)
        {
            return;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (HasIndex)
            {
                return;
            }

            var fileExists = File.Exists(_filePath);
            var fileIsFresh = fileExists && File.GetLastWriteTimeUtc(_filePath).Date == DateTime.UtcNow.Date;

            if (fileExists && !fileIsFresh)
            {
                await RefreshAsync(cancellationToken).ConfigureAwait(false);
            }

            if (fileExists)
            {
                LoadFromFile();
                return;
            }

            _logger.LogWarning("AniDB title dump not available yet; fetching '{Url}'.", TitleDumpUrl);
            await RefreshAsync(cancellationToken).ConfigureAwait(false);
            if (File.Exists(_filePath))
            {
                LoadFromFile();
                return;
            }

            _loaded = true;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        try
        {
            var directory = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var temp = _filePath + ".part";
            using (var response = await _http.GetAsync(TitleDumpUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false))
            {
                if (!response.IsSuccessStatusCode)
                {
                    throw new AnidbApiException($"AniDB title dump returned {(int)response.StatusCode}.");
                }

                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                await using var file = File.Create(temp);
                await stream.CopyToAsync(file, cancellationToken).ConfigureAwait(false);
            }

            File.Move(temp, _filePath, true);
            _logger.LogInformation("AniDB title dump downloaded to {Path}.", _filePath);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "AniDB title dump download failed: {Message}", ex.Message);
        }
    }

    private void LoadFromFile()
    {
        try
        {
            var entries = new List<AnidbTitleEntry>();
            using var compressed = File.OpenRead(_filePath);
            using var gzip = new GZipStream(compressed, CompressionMode.Decompress);
            using var reader = new StreamReader(gzip, Encoding.UTF8);

            string? line;
            while ((line = reader.ReadLine()) is not null)
            {
                var parsed = ParseLine(line);
                if (parsed is not null)
                {
                    entries.Add(parsed);
                }
            }

            lock (_entries)
            {
                _entries = entries;
                _mainTitles = BuildMainTitles(entries);
                _loaded = true;
            }

            _logger.LogInformation("AniDB title index loaded: {Count} entries.", entries.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not parse AniDB title dump at {Path}.", _filePath);
            lock (_entries)
            {
                _loaded = true;
            }
        }
    }

    private static Dictionary<int, string> BuildMainTitles(List<AnidbTitleEntry> entries)
    {
        var result = new Dictionary<int, string>();
        foreach (var entry in entries)
        {
            if (entry.Type is not (AnidbTitleType.Main or AnidbTitleType.Official))
            {
                continue;
            }

            if (!result.ContainsKey(entry.Aid) && !string.IsNullOrWhiteSpace(entry.Title))
            {
                result[entry.Aid] = entry.Title.Trim();
            }
        }

        return result;
    }

    public static AnidbTitleEntry? ParseLine(string line)
    {
        var parts = line.Split('|');
        if (parts.Length < 4)
        {
            return null;
        }

        if (!int.TryParse(parts[0], out var aid) || aid <= 0)
        {
            return null;
        }

        if (!int.TryParse(parts[1], out var type))
        {
            return null;
        }

        var language = parts[2];
        var title = string.Join('|', parts.Skip(3)).Trim();
        if (title.Length == 0)
        {
            return null;
        }

        return new AnidbTitleEntry(aid, type, language, title);
    }

    public IReadOnlyList<AnidbTitleHit> Search(string query)
    {
        var normalized = Normalize(query);
        if (normalized.Length == 0)
        {
            return Array.Empty<AnidbTitleHit>();
        }

        List<AnidbTitleEntry> snapshot;
        lock (_entries)
        {
            snapshot = _entries;
        }

        // Only search main/official titles (type 1, 4) — matches AniDB web search for TV series
        var filtered = snapshot.Where(e => e.Type is AnidbTitleType.Main or AnidbTitleType.Official).ToList();

        var bestByAid = new Dictionary<int, (int Score, string Title)>();
        foreach (var entry in filtered)
        {
            var title = Normalize(entry.Title);
            if (title.Length == 0)
            {
                continue;
            }

            var score = Score(title, normalized);
            if (score <= 0)
            {
                continue;
            }

            if (bestByAid.TryGetValue(entry.Aid, out var current) && current.Score >= score)
            {
                continue;
            }

            bestByAid[entry.Aid] = (score, entry.Title.Trim());
        }

        var result = bestByAid
            .Select(kv => new AnidbTitleHit(kv.Key, MainTitle(kv.Key) ?? kv.Value.Title))
            .OrderByDescending(hit => bestByAid[hit.Aid].Score)
            .ThenBy(hit => hit.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return result;
    }

    private static int Score(string title, string normalizedQuery)
    {
        if (string.Equals(title, normalizedQuery, StringComparison.Ordinal))
        {
            return 100;
        }

        if (title.StartsWith(normalizedQuery, StringComparison.Ordinal))
        {
            return 40;
        }

        return title.Contains(normalizedQuery, StringComparison.Ordinal) ? 10 : 0;
    }

    private string? MainTitle(int aid)
    {
        lock (_entries)
        {
            return _mainTitles.TryGetValue(aid, out var title) ? title : null;
        }
    }

    private static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return string.Join(" ",
            value.ToLowerInvariant()
                .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }
}