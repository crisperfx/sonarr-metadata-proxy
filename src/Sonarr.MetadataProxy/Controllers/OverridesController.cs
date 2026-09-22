using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using Sonarr.MetadataProxy.Mapping;
using Sonarr.MetadataProxy.Reverse;

namespace Sonarr.MetadataProxy.Controllers;

[ApiController]
[Route("api/overrides")]
[EnableCors(OverridesController.CorsPolicyName)]
public sealed class OverridesController : ControllerBase
{
    public const string CorsPolicyName = "override-ui";

    private readonly MappingStore _mapping;
    private readonly ITvdbToTmdbResolver _tvdbToTmdb;
    private readonly ILogger<OverridesController> _logger;

    public OverridesController(
        MappingStore mapping,
        ITvdbToTmdbResolver tvdbToTmdb,
        ILogger<OverridesController> logger)
    {
        _mapping = mapping;
        _tvdbToTmdb = tvdbToTmdb;
        _logger = logger;
    }

    public sealed record OverrideDto(int TvdbId, string Source, int? TmdbId);

    public sealed record OverrideRequest(int TvdbId, string Source, int? TmdbId, string? Title, int? Year);

    public sealed record SearchSourceDto(string Source);

    public sealed record SearchSourceRequest(string? Source);

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
            .Select(kv => new OverrideDto(kv.Key, kv.Value, _mapping.TryResolveSeriesTmdb(kv.Key)));
        return Ok(result);
    }

    [HttpPost]
    public async Task<IActionResult> Set([FromBody] OverrideRequest request)
    {
        if (request.TvdbId <= 0 || SyntheticIds.IsSyntheticSeries(request.TvdbId))
        {
            return BadRequest(new { error = "tvdbId must be a real (non-synthetic) TVDB id" });
        }

        if (request.Source is not (MappingStore.SourceTmdb or MappingStore.SourceTvdb or MappingStore.SourceAniList))
        {
            return BadRequest(new { error = "source must be 'tmdb', 'tvdb' or 'anilist'" });
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

        _mapping.SetOverride(request.TvdbId, request.Source);
        _logger.LogInformation("Override set for TVDB id {TvdbId} -> {Source}.", request.TvdbId, request.Source);
        return Ok(new OverrideDto(request.TvdbId, request.Source, _mapping.TryResolveSeriesTmdb(request.TvdbId)));
    }

    [HttpDelete("{tvdbId:int}")]
    public IActionResult Remove(int tvdbId)
    {
        if (!_mapping.RemoveOverride(tvdbId))
        {
            return NotFound();
        }

        _logger.LogInformation("Override removed for TVDB id {TvdbId}.", tvdbId);
        return NoContent();
    }
}