using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using Sonarr.MetadataProxy.Mapping;
using Sonarr.MetadataProxy.Options;
using Sonarr.MetadataProxy.Reverse;

namespace Sonarr.MetadataProxy.Controllers;

[ApiController]
[Route("api/overrides")]
[EnableCors(OverridesController.CorsPolicyName)]
public sealed class OverridesController : ControllerBase
{
    public const string CorsPolicyName = "override-ui";

    private readonly MappingStore _mapping;
    private readonly ProxyOptions _options;
    private readonly ITvdbToTmdbResolver _tvdbToTmdb;
    private readonly ITvdbToTvmazeResolver _tvdbToTvmaze;
    private readonly ITvdbToAnidbResolver _tvdbToAnidb;
    private readonly ILogger<OverridesController> _logger;

    public OverridesController(
        MappingStore mapping,
        ProxyOptions options,
        ITvdbToTmdbResolver tvdbToTmdb,
        ITvdbToTvmazeResolver tvdbToTvmaze,
        ITvdbToAnidbResolver tvdbToAnidb,
        ILogger<OverridesController> logger)
    {
        _mapping = mapping;
        _options = options;
        _tvdbToTmdb = tvdbToTmdb;
        _tvdbToTvmaze = tvdbToTvmaze;
        _tvdbToAnidb = tvdbToAnidb;
        _logger = logger;
    }

    public sealed record OverrideDto(int TvdbId, string Source, int? TmdbId, int? AniListId, int? MalId, int? TvmazeId, int? AnidbId);

    public sealed record OverrideRequest(int TvdbId, string Source, int? TmdbId, string? Title, int? Year, int? TvmazeId, int? AnidbId);

    public sealed record SearchSourceDto(string Source);

    public sealed record SearchSourceRequest(string? Source);

    public sealed record ProviderDto(string Id, string Label, bool Configured);

    [HttpGet("providers")]
    public IActionResult Providers()
    {
        var providers = new List<ProviderDto>
        {
            new("tmdb", "TMDB", _options.HasTmdbAuth),
            new("tvdb", "TVDB", true),
            new("anilist", "AniList", true),
            new("mal", "MAL", true),
            new("tvmaze", "TVMaze", true),
            new("anidb", "AniDB", _options.HasAnidbClient)
        };

        return Ok(new { providers = providers.Select(p => (object)p).ToList() });
    }

    [HttpGet("searchsource")]
    public IActionResult GetSearchSource()
    {
        return Ok(new SearchSourceDto(_mapping.GetDefaultSearchSource()));
    }

    [HttpPost("searchsource")]
    public IActionResult SetSearchSource([FromBody] SearchSourceRequest request)
    {
        try
        {
            _mapping.SetDefaultSearchSource(request.Source ?? "");
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }

        _logger.LogInformation("Default search source set to '{Source}'.", _mapping.GetDefaultSearchSource());
        return Ok(new SearchSourceDto(_mapping.GetDefaultSearchSource()));
    }

    [HttpGet]
    public IActionResult List()
    {
        var result = _mapping.AllOverrides()
            .Select(kv => new OverrideDto(
                kv.Key,
                kv.Value,
                _mapping.TryResolveSeriesTmdb(kv.Key),
                _mapping.TryGetAniListIdByTvdb(kv.Key),
                _mapping.TryGetMalIdByTvdb(kv.Key),
                _mapping.TryGetTvmazeIdByTvdb(kv.Key),
                _mapping.TryGetAnidbIdByTvdb(kv.Key)));
        return Ok(result);
    }

    [HttpPost]
    public async Task<IActionResult> Set([FromBody] OverrideRequest request)
    {
        if (request.TvdbId <= 0)
        {
            return BadRequest(new { error = "tvdbId must be positive" });
        }

        var isSynthetic = SyntheticIds.IsSyntheticSeries(request.TvdbId);

        if (request.Source is not (MappingStore.SourceTmdb or MappingStore.SourceTvdb or MappingStore.SourceAniList or MappingStore.SourceMal or MappingStore.SourceTvmaze or MappingStore.SourceAnidb))
        {
            return BadRequest(new { error = "source must be 'tmdb', 'tvdb', 'anilist', 'mal', 'tvmaze' or 'anidb'" });
        }

        if (isSynthetic && request.Source == MappingStore.SourceTvdb)
        {
            _logger.LogWarning("TVDB override requested for synthetic TVDB id {TvdbId}; TVDB passthrough will not work (no real TVDB mapping).", request.TvdbId);
        }

        if (request.Source == MappingStore.SourceTmdb && request.TmdbId is > 0)
        {
            _mapping.RegisterSeries(request.TvdbId, request.TmdbId.Value);
        }
        else if (request.Source == MappingStore.SourceTmdb)
        {
            var resolved = await _tvdbToTmdb.ResolveTmdbIdAsync(
                request.TvdbId, request.Title, request.Year, CancellationToken.None);
            if (resolved is > 0)
            {
                _mapping.RegisterSeries(request.TvdbId, resolved.Value);
            }
            else
            {
                _logger.LogWarning(
                    "Override source tmdb requested for TVDB id {TvdbId} (title: '{Title}') but no TMDB mapping is known yet. "
                    + "It will fall back to TVDB until a mapping is recorded.",
                    request.TvdbId, request.Title);
            }
        }

        if (request.Source == MappingStore.SourceTvmaze && request.TvmazeId is > 0)
        {
            _mapping.RegisterTvmazeId(request.TvdbId, request.TvmazeId.Value);
        }
        else if (request.Source == MappingStore.SourceTvmaze)
        {
            var resolved = await _tvdbToTvmaze.ResolveTvmazeIdAsync(
                request.TvdbId, request.Title, request.Year, CancellationToken.None);
            if (resolved is > 0)
            {
                _mapping.RegisterTvmazeId(request.TvdbId, resolved.Value);
            }
            else
            {
                _logger.LogWarning(
                    "Override source tvmaze requested for TVDB id {TvdbId} (title: '{Title}') but no TVMaze mapping is known yet. "
                    + "It will fall back to TVDB until a mapping is recorded.",
                    request.TvdbId, request.Title);
            }
        }

        if (request.Source == MappingStore.SourceAnidb)
        {
            var anidbId = request.AnidbId;
            if (!anidbId.HasValue)
            {
                anidbId = SyntheticIds.TryDecomposeAnidbSeries(request.TvdbId);
            }
            if (!anidbId.HasValue)
            {
                anidbId = _mapping.TryGetAnidbIdByTvdb(request.TvdbId);
            }
            if (!anidbId.HasValue)
            {
                var resolved = await _tvdbToAnidb.ResolveAnidbIdAsync(
                    request.TvdbId, request.Title, request.Year, CancellationToken.None);
                if (resolved is > 0)
                {
                    anidbId = resolved;
                }
            }

            if (anidbId is > 0)
            {
                _mapping.RegisterAnidbId(request.TvdbId, anidbId.Value);
            }
            else
            {
                _logger.LogWarning(
                    "Override source anidb requested for TVDB id {TvdbId} (title: '{Title}') but no AniDB id is known yet. "
                    + "It will fall back to TVDB until a mapping is recorded.",
                    request.TvdbId, request.Title);
            }
        }

        _mapping.SetOverride(request.TvdbId, request.Source);
        _logger.LogInformation("Override set for TVDB id {TvdbId} -> {Source}.", request.TvdbId, request.Source);
        return Ok(new OverrideDto(
            request.TvdbId,
            request.Source,
            _mapping.TryResolveSeriesTmdb(request.TvdbId),
            _mapping.TryGetAniListIdByTvdb(request.TvdbId),
            _mapping.TryGetMalIdByTvdb(request.TvdbId),
            _mapping.TryGetTvmazeIdByTvdb(request.TvdbId),
            _mapping.TryGetAnidbIdByTvdb(request.TvdbId)));
    }

    [HttpDelete("{tvdbId:int}")]
    public IActionResult Remove(int tvdbId)
    {
        if (!_mapping.RemoveOverride(tvdbId))
        {
            return NotFound();
        }

        _logger.LogInformation("Override and associated mappings removed for TVDB id {TvdbId}.", tvdbId);
        return NoContent();
    }
}