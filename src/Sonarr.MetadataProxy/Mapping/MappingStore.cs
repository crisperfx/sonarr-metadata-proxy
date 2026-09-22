using System.Text.Json;
using Sonarr.MetadataProxy.Options;

namespace Sonarr.MetadataProxy.Mapping;

public sealed class MappingStore
{
    private readonly string _filePath;
    private readonly ILogger<MappingStore> _logger;
    private readonly object _sync = new();

    private readonly Dictionary<int, int> _seriesReal = new();
    private readonly Dictionary<string, int> _episodes = new();
    private readonly Dictionary<int, int> _nextEpisodeSequence = new();
    private readonly Dictionary<int, string> _overrides = new();
    private string _defaultSearchSource = "";

    public const string SourceTmdb = "tmdb";
    public const string SourceTvdb = "tvdb";
    public const string SourceAniList = "anilist";

    public MappingStore(ProxyOptions options, ILogger<MappingStore> logger)
    {
        var directory = Path.Combine(options.DataDir, "mappings");
        _filePath = Path.Combine(directory, "mappings.json");
        _logger = logger;
        MigrateLegacyFile(options.DataDir);
        Load();
    }

    private void MigrateLegacyFile(string dataDir)
    {
        var legacy = Path.Combine(dataDir, "mappings.json");
        if (!File.Exists(legacy) || File.Exists(_filePath))
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
            File.Move(legacy, _filePath);
            _logger.LogInformation("Migrated mapping store from {Legacy} to {Path}.", legacy, _filePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not migrate legacy mapping store from {Legacy}.", legacy);
        }
    }

    public int? TryResolveSeriesTmdb(int tvdbId)
    {
        lock (_sync)
        {
            return _seriesReal.TryGetValue(tvdbId, out var tmdbId) ? tmdbId : null;
        }
    }

    public void RegisterSeries(int tvdbId, int tmdbId)
    {
        if (SyntheticIds.IsSyntheticSeries(tvdbId))
        {
            return;
        }

        lock (_sync)
        {
            if (!_seriesReal.ContainsKey(tvdbId))
            {
                _seriesReal[tvdbId] = tmdbId;
                Save();
            }
        }
    }

    public int EpisodeTvdbId(int seriesTvdbId, int season, int episode)
    {
        var key = $"{seriesTvdbId}:{season}:{episode}";
        lock (_sync)
        {
            if (_episodes.TryGetValue(key, out var existing))
            {
                return existing;
            }

            var sequence = _nextEpisodeSequence.GetValueOrDefault(seriesTvdbId, 0);
            var assigned = SyntheticIds.MinEpisodeSynthetic + sequence;
            _episodes[key] = assigned;
            _nextEpisodeSequence[seriesTvdbId] = sequence + 1;
            Save();
            return assigned;
        }
    }

    public string? GetOverride(int tvdbId)
    {
        lock (_sync)
        {
            return _overrides.TryGetValue(tvdbId, out var source) ? source : null;
        }
    }

    public void SetOverride(int tvdbId, string source)
    {
        if (source != SourceTmdb && source != SourceTvdb)
        {
            throw new ArgumentException("Source must be 'tmdb' or 'tvdb'.", nameof(source));
        }

        if (tvdbId <= 0 || SyntheticIds.IsSyntheticSeries(tvdbId))
        {
            throw new ArgumentOutOfRangeException(nameof(tvdbId), "Overrides only apply to real (non-synthetic) TVDB ids.");
        }

        lock (_sync)
        {
            _overrides[tvdbId] = source;
            Save();
        }
    }

    public bool RemoveOverride(int tvdbId)
    {
        lock (_sync)
        {
            if (!_overrides.Remove(tvdbId))
            {
                return false;
            }

            Save();
            return true;
        }
    }

    public IReadOnlyDictionary<int, string> AllOverrides()
    {
        lock (_sync)
        {
            return new Dictionary<int, string>(_overrides);
        }
    }

    public string GetDefaultSearchSource()
    {
        lock (_sync)
        {
            return _defaultSearchSource;
        }
    }

    public void SetDefaultSearchSource(string source)
    {
        var normalized = (source ?? string.Empty).Trim().ToLowerInvariant();
        if (normalized is not "" and not SourceTmdb and not SourceTvdb and not SourceAniList)
        {
            throw new ArgumentException("Search source must be '', 'tmdb', 'tvdb' or 'anilist'.", nameof(source));
        }

        lock (_sync)
        {
            if (_defaultSearchSource == normalized)
            {
                return;
            }

            _defaultSearchSource = normalized;
            Save();
        }
    }

    private void Load()
    {
        if (!File.Exists(_filePath))
        {
            _logger.LogInformation("No existing mapping store at {Path}. Starting empty.", _filePath);
            return;
        }

        try
        {
            var json = File.ReadAllText(_filePath);
            var persisted = JsonSerializer.Deserialize<PersistedState>(json);
            if (persisted is not null)
            {
                lock (_sync)
                {
                    _seriesReal.Clear();
                    _episodes.Clear();
                    _nextEpisodeSequence.Clear();
                    _overrides.Clear();
                    _defaultSearchSource = persisted.DefaultSearchSource ?? "";

                    foreach (var (key, value) in persisted.SeriesReal)
                    {
                        _seriesReal[key] = value;
                    }

                    foreach (var (key, value) in persisted.Episodes)
                    {
                        _episodes[key] = value;
                    }

                    foreach (var (key, value) in persisted.EpisodeSequences)
                    {
                        _nextEpisodeSequence[key] = value;
                    }

                    foreach (var (key, value) in persisted.Overrides)
                    {
                        _overrides[key] = value;
                    }
                }

                _logger.LogInformation(
                    "Loaded mapping store with {Series} series and {Episodes} episode mappings.",
                    _seriesReal.Count,
                    _episodes.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load mapping store from {Path}. Starting empty.", _filePath);
        }
    }

    private void Save()
    {
        try
        {
            var directory = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var persisted = new PersistedState
            {
                SeriesReal = new Dictionary<int, int>(_seriesReal),
                Episodes = new Dictionary<string, int>(_episodes),
                EpisodeSequences = new Dictionary<int, int>(_nextEpisodeSequence),
                Overrides = new Dictionary<int, string>(_overrides),
                DefaultSearchSource = _defaultSearchSource
            };

            var json = JsonSerializer.Serialize(persisted, new JsonSerializerOptions { WriteIndented = true });
            var temp = _filePath + ".tmp";
            File.WriteAllText(temp, json);
            File.Move(temp, _filePath, true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist mapping store to {Path}.", _filePath);
        }
    }

    private sealed class PersistedState
    {
        public Dictionary<int, int> SeriesReal { get; set; } = new();
        public Dictionary<string, int> Episodes { get; set; } = new();
        public Dictionary<int, int> EpisodeSequences { get; set; } = new();
        public Dictionary<int, string> Overrides { get; set; } = new();
        public string DefaultSearchSource { get; set; } = "";
    }
}