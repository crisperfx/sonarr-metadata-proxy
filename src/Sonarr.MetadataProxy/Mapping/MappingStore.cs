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
    private readonly Dictionary<int, int> _aniListByTvdb = new();
    private readonly Dictionary<int, int> _malByTvdb = new();
    private readonly Dictionary<int, int> _tvdbByMal = new();
    private readonly Dictionary<int, int> _tvdbByAniList = new();
    private readonly Dictionary<int, int> _tvmazeByTvdb = new();
    private readonly Dictionary<int, int> _tvdbByTvmaze = new();
    private readonly Dictionary<int, int> _anidbByTvdb = new();
    private readonly Dictionary<int, int> _tvdbByAnidb = new();
    private string _defaultSearchSource = "";

    public const string SourceTmdb = "tmdb";
    public const string SourceTvdb = "tvdb";
    public const string SourceAniList = "anilist";
    public const string SourceMal = "mal";
    public const string SourceTvmaze = "tvmaze";
    public const string SourceAnidb = "anidb";

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

    public int? TryGetAniListIdByTvdb(int tvdbId)
    {
        lock (_sync)
        {
            return _aniListByTvdb.TryGetValue(tvdbId, out var anilistId) ? anilistId : null;
        }
    }

    public void RegisterAniListId(int tvdbId, int anilistId)
    {
        if (anilistId <= 0)
        {
            return;
        }

        lock (_sync)
        {
            if (_aniListByTvdb.ContainsKey(tvdbId))
            {
                return;
            }

            _aniListByTvdb[tvdbId] = anilistId;
            Save();
        }
    }

    public int? TryGetMalIdByTvdb(int tvdbId)
    {
        lock (_sync)
        {
            return _malByTvdb.TryGetValue(tvdbId, out var malId) ? malId : null;
        }
    }

    public void RegisterMalId(int tvdbId, int malId)
    {
        if (malId <= 0)
        {
            return;
        }

        lock (_sync)
        {
            var changed = false;

            // Always keep the minimum MAL ID for a TVDB ID
            if (!_malByTvdb.ContainsKey(tvdbId) || malId < _malByTvdb[tvdbId])
            {
                var oldMalId = _malByTvdb.GetValueOrDefault(tvdbId, 0);
                _malByTvdb[tvdbId] = malId;
                changed = true;

                // Update reverse mapping for new MAL ID
                if (!_tvdbByMal.ContainsKey(malId))
                {
                    _tvdbByMal[malId] = tvdbId;
                    changed = true;
                }

                // If we replaced an old MAL ID, clean up reverse mapping for old one
                // (only if no other TVDB ID maps to it)
                if (oldMalId > 0 && oldMalId != malId)
                {
                    if (_tvdbByMal.TryGetValue(oldMalId, out var mappedTvdbId) && mappedTvdbId == tvdbId)
                    {
                        _tvdbByMal.Remove(oldMalId);
                    }
                }
            }

            if (changed)
            {
                Save();
            }
        }
    }

    public int? TryGetTvdbByMalId(int malId)
    {
        lock (_sync)
        {
            return _tvdbByMal.TryGetValue(malId, out var tvdbId) ? tvdbId : null;
        }
    }

    public int? TryGetTvdbByAniListId(int anilistId)
    {
        lock (_sync)
        {
            return _tvdbByAniList.TryGetValue(anilistId, out var tvdbId) ? tvdbId : null;
        }
    }

    public int? TryGetTvmazeIdByTvdb(int tvdbId)
    {
        lock (_sync)
        {
            return _tvmazeByTvdb.TryGetValue(tvdbId, out var tvmazeId) ? tvmazeId : null;
        }
    }

    public int? TryGetTvdbByTvmazeId(int tvmazeId)
    {
        lock (_sync)
        {
            return _tvdbByTvmaze.TryGetValue(tvmazeId, out var tvdbId) ? tvdbId : null;
        }
    }

    public void RegisterTvmazeId(int tvdbId, int tvmazeId)
    {
        if (tvmazeId <= 0)
        {
            return;
        }

        lock (_sync)
        {
            var changed = false;

            if (!_tvmazeByTvdb.ContainsKey(tvdbId))
            {
                _tvmazeByTvdb[tvdbId] = tvmazeId;
                changed = true;
            }

            if (!_tvdbByTvmaze.ContainsKey(tvmazeId))
            {
                _tvdbByTvmaze[tvmazeId] = tvdbId;
                changed = true;
            }

            if (changed)
            {
                Save();
            }
        }
    }

    public int? TryGetAnidbIdByTvdb(int tvdbId)
    {
        lock (_sync)
        {
            return _anidbByTvdb.TryGetValue(tvdbId, out var anidbId) ? anidbId : null;
        }
    }

    public int? TryGetTvdbByAnidbId(int anidbId)
    {
        lock (_sync)
        {
            return _tvdbByAnidb.TryGetValue(anidbId, out var tvdbId) ? tvdbId : null;
        }
    }

    public void RegisterAnidbId(int tvdbId, int anidbId)
    {
        if (anidbId <= 0)
        {
            return;
        }

        lock (_sync)
        {
            var changed = false;

            if (!_anidbByTvdb.ContainsKey(tvdbId))
            {
                _anidbByTvdb[tvdbId] = anidbId;
                changed = true;
            }

            if (!_tvdbByAnidb.ContainsKey(anidbId))
            {
                _tvdbByAnidb[anidbId] = tvdbId;
                changed = true;
            }

            if (changed)
            {
                Save();
            }
        }
    }

    public void RegisterIds(int tvdbId, int? tmdbId = null, int? malId = null, int? anilistId = null)
    {
        if (tvdbId <= 0)
        {
            return;
        }

        lock (_sync)
        {
            var changed = false;

            if (tmdbId.HasValue && !SyntheticIds.IsSyntheticSeries(tvdbId) && !_seriesReal.ContainsKey(tvdbId))
            {
                _seriesReal[tvdbId] = tmdbId.Value;
                changed = true;
            }

            if (malId.HasValue && malId.Value > 0)
            {
                if (!_malByTvdb.ContainsKey(tvdbId))
                {
                    _malByTvdb[tvdbId] = malId.Value;
                    changed = true;
                }
                if (!_tvdbByMal.ContainsKey(malId.Value))
                {
                    _tvdbByMal[malId.Value] = tvdbId;
                    changed = true;
                }
            }

            if (anilistId.HasValue && anilistId.Value > 0)
            {
                if (!_aniListByTvdb.ContainsKey(tvdbId))
                {
                    _aniListByTvdb[tvdbId] = anilistId.Value;
                    changed = true;
                }
                if (!_tvdbByAniList.ContainsKey(anilistId.Value))
                {
                    _tvdbByAniList[anilistId.Value] = tvdbId;
                    changed = true;
                }
            }

            if (changed)
            {
                Save();
            }
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
        if (source is not (SourceTmdb or SourceTvdb or SourceAniList or SourceMal or SourceTvmaze or SourceAnidb))
        {
            throw new ArgumentException("Source must be 'tmdb', 'tvdb', 'anilist', 'mal', 'tvmaze' or 'anidb'.", nameof(source));
        }

        if (tvdbId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(tvdbId), "tvdbId must be positive");
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
            var removedOverride = _overrides.Remove(tvdbId);
            var removedMapping = _seriesReal.Remove(tvdbId);
            var removedAniList = _aniListByTvdb.Remove(tvdbId);
            var removedMal = _malByTvdb.Remove(tvdbId);
            var removedTvmaze = _tvmazeByTvdb.Remove(tvdbId);
            var removedAnidb = _anidbByTvdb.Remove(tvdbId);

            var orphanedTvmazeTvdbIds = _tvdbByTvmaze
                .Where(kvp => kvp.Value == tvdbId)
                .Select(kvp => kvp.Key)
                .ToList();
            foreach (var orphanedTvmazeId in orphanedTvmazeTvdbIds)
            {
                _tvdbByTvmaze.Remove(orphanedTvmazeId);
            }

            var orphanedAnidbIds = _tvdbByAnidb
                .Where(kvp => kvp.Value == tvdbId)
                .Select(kvp => kvp.Key)
                .ToList();
            foreach (var orphanedAnidbId in orphanedAnidbIds)
            {
                _tvdbByAnidb.Remove(orphanedAnidbId);
            }

            if (!removedOverride && !removedMapping && !removedAniList && !removedMal && !removedTvmaze && !removedAnidb
                && orphanedTvmazeTvdbIds.Count == 0 && orphanedAnidbIds.Count == 0)
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
        if (normalized is not "" and not SourceTmdb and not SourceTvdb and not SourceAniList and not SourceMal and not SourceTvmaze and not SourceAnidb)
        {
            throw new ArgumentException("Search source must be '', 'tmdb', 'tvdb', 'anilist', 'mal', 'tvmaze' or 'anidb'.", nameof(source));
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
                    _aniListByTvdb.Clear();
                    _malByTvdb.Clear();
                    _tvdbByMal.Clear();
                    _tvdbByAniList.Clear();
                    _tvmazeByTvdb.Clear();
                    _tvdbByTvmaze.Clear();
                    _anidbByTvdb.Clear();
                    _tvdbByAnidb.Clear();

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

                    foreach (var (key, value) in persisted.AniListByTvdb)
                    {
                        _aniListByTvdb[key] = value;
                    }

                    foreach (var (key, value) in persisted.MalByTvdb)
                    {
                        _malByTvdb[key] = value;
                    }

                    foreach (var (key, value) in persisted.TvdbByMal)
                    {
                        _tvdbByMal[key] = value;
                    }

                    foreach (var (key, value) in persisted.TvdbByAniList)
                    {
                        _tvdbByAniList[key] = value;
                    }

                    foreach (var (key, value) in persisted.TvmazeByTvdb)
                    {
                        _tvmazeByTvdb[key] = value;
                    }

                    foreach (var (key, value) in persisted.TvdbByTvmaze)
                    {
                        _tvdbByTvmaze[key] = value;
                    }

                    foreach (var (key, value) in persisted.AnidbByTvdb)
                    {
                        _anidbByTvdb[key] = value;
                    }

                    foreach (var (key, value) in persisted.TvdbByAnidb)
                    {
                        _tvdbByAnidb[key] = value;
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
                AniListByTvdb = new Dictionary<int, int>(_aniListByTvdb),
                MalByTvdb = new Dictionary<int, int>(_malByTvdb),
                TvdbByMal = new Dictionary<int, int>(_tvdbByMal),
                TvdbByAniList = new Dictionary<int, int>(_tvdbByAniList),
                TvmazeByTvdb = new Dictionary<int, int>(_tvmazeByTvdb),
                TvdbByTvmaze = new Dictionary<int, int>(_tvdbByTvmaze),
                AnidbByTvdb = new Dictionary<int, int>(_anidbByTvdb),
                TvdbByAnidb = new Dictionary<int, int>(_tvdbByAnidb),
                DefaultSearchSource = _defaultSearchSource
            };

            var json = JsonSerializer.Serialize(persisted, new JsonSerializerOptions { WriteIndented = true });
            var temp = _filePath + ".tmp";
            File.WriteAllText(temp, json);
            UnixPermissions.PrivateFile(temp);
            File.Move(temp, _filePath, true);
            UnixPermissions.PrivateFile(_filePath);
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
        public Dictionary<int, int> AniListByTvdb { get; set; } = new();
        public Dictionary<int, int> MalByTvdb { get; set; } = new();
        public Dictionary<int, int> TvdbByMal { get; set; } = new();
        public Dictionary<int, int> TvdbByAniList { get; set; } = new();
        public Dictionary<int, int> TvmazeByTvdb { get; set; } = new();
        public Dictionary<int, int> TvdbByTvmaze { get; set; } = new();
        public Dictionary<int, int> AnidbByTvdb { get; set; } = new();
        public Dictionary<int, int> TvdbByAnidb { get; set; } = new();
        public string DefaultSearchSource { get; set; } = "";
    }
}