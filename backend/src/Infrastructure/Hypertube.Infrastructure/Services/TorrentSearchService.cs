using Hypertube.Application.Movies.DTOs;
using Hypertube.Application.Movies.Services;
using Hypertube.Infrastructure.ExternalServices;
using Microsoft.Extensions.Logging;

namespace Hypertube.Infrastructure.Services;

public class TorrentSearchService : ITorrentSearchService
{
    private readonly YtsService _ytsService;
    private readonly PirateBayService _pirateBayService;
    private readonly IMovieMetadataService _metadataService;
    private readonly ILogger<TorrentSearchService> _logger;

    public TorrentSearchService(
        YtsService ytsService,
        PirateBayService pirateBayService,
        IMovieMetadataService metadataService,
        ILogger<TorrentSearchService> logger)
    {
        _ytsService = ytsService;
        _pirateBayService = pirateBayService;
        _metadataService = metadataService;
        _logger = logger;
    }

    public async Task<(List<TorrentSearchResultDto> Results, int TotalCount)> SearchAsync(string query, int page = 1, int limit = 20, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Searching torrents for query: {Query}", query);

        // Search both sources in parallel (fetch more to allow for filtering/deduplication)
        var ytsTask = _ytsService.SearchAsync(query, 1, 100, cancellationToken);
        var tpbTask = _pirateBayService.SearchAsync(query, 1, 100, cancellationToken);

        await Task.WhenAll(ytsTask, tpbTask);

        var ytsResults = await ytsTask;
        var tpbResults = await tpbTask;

        // Enrich TPB results with metadata
        await EnrichResultsWithMetadata(tpbResults, cancellationToken);

        // Merge results
        var allResults = new List<TorrentSearchResultDto>();
        allResults.AddRange(ytsResults);
        allResults.AddRange(tpbResults);

        // Deduplicate by IMDb ID and title
        var deduplicatedResults = DeduplicateResults(allResults);

        // Get total count before pagination
        var totalCount = deduplicatedResults.Count;

        // Sort by seeds descending, then rating, then paginate
        var sortedResults = deduplicatedResults
            .OrderByDescending(r => r.Seeds ?? 0)
            .ThenByDescending(r => r.Rating ?? 0)
            .Skip((page - 1) * limit)
            .Take(limit)
            .ToList();

        _logger.LogInformation(
            "Found {Count} results from YTS and {TpbCount} from TPB, returning {PageCount} of {Total} after deduplication",
            ytsResults.Count,
            tpbResults.Count,
            sortedResults.Count,
            totalCount
        );

        return (sortedResults, totalCount);
    }

    public async Task<(List<TorrentSearchResultDto> Results, int TotalCount)> GetPopularAsync(int page = 1, int limit = 20, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Getting popular torrents");

        // Get popular from both sources in parallel (fetch more to allow for filtering/deduplication)
        var ytsTask = _ytsService.GetPopularAsync(1, 100, cancellationToken);
        var tpbTask = _pirateBayService.GetPopularAsync(1, 100, cancellationToken);

        await Task.WhenAll(ytsTask, tpbTask);

        var ytsResults = await ytsTask;
        var tpbResults = await tpbTask;

        // Enrich TPB results with metadata
        await EnrichResultsWithMetadata(tpbResults, cancellationToken);

        // Merge results
        var allResults = new List<TorrentSearchResultDto>();
        allResults.AddRange(ytsResults);
        allResults.AddRange(tpbResults);

        // Deduplicate
        var deduplicatedResults = DeduplicateResults(allResults);

        // Get total count before pagination
        var totalCount = deduplicatedResults.Count;

        // Sort by rating and seeds, then paginate
        var sortedResults = deduplicatedResults
            .OrderByDescending(r => r.Rating ?? 0)
            .ThenByDescending(r => r.Seeds ?? 0)
            .Skip((page - 1) * limit)
            .Take(limit)
            .ToList();

        _logger.LogInformation(
            "Found {YtsCount} popular from YTS and {TpbCount} from TPB, returning {PageCount} of {Total}",
            ytsResults.Count,
            tpbResults.Count,
            sortedResults.Count,
            totalCount
        );

        return (sortedResults, totalCount);
    }

    private async Task EnrichResultsWithMetadata(List<TorrentSearchResultDto> results, CancellationToken cancellationToken)
    {
        // Process in parallel but limit concurrency to avoid rate limits
        var options = new ParallelOptions { MaxDegreeOfParallelism = 5, CancellationToken = cancellationToken };

        await Parallel.ForEachAsync(results, options, async (result, ct) =>
        {
            // Only enrich if missing critical metadata
            if (string.IsNullOrEmpty(result.CoverImageUrl) || string.IsNullOrEmpty(result.Summary))
            {
                try
                {
                    MovieMetadataDto? metadata = null;

                    // Strategy 1: Try IMDb ID first (most accurate)
                    if (!string.IsNullOrEmpty(result.ImdbId))
                    {
                        _logger.LogInformation("Enriching by IMDb ID: {ImdbId} for {Title}", result.ImdbId, result.Title);

                        // Cast to TmdbService to access GetMetadataByImdbIdAsync
                        if (_metadataService is TmdbService tmdbService)
                        {
                            metadata = await tmdbService.GetMetadataByImdbIdAsync(result.ImdbId, ct);
                        }
                    }

                    // Strategy 2: Fallback to title + year search
                    if (metadata == null)
                    {
                        _logger.LogInformation("Enriching by title+year: {Title} ({Year})", result.Title, result.Year);
                        metadata = await _metadataService.GetMetadataAsync(result.Title, result.Year, ct);
                    }

                    if (metadata != null)
                    {
                        result.ImdbId = metadata.ImdbId ?? result.ImdbId;
                        result.CoverImageUrl = metadata.PosterUrl;
                        result.Summary = metadata.Summary;
                        result.Rating = metadata.Rating ?? result.Rating;
                        result.Genre = metadata.Genre;
                        result.Director = metadata.Director;
                        result.Cast = metadata.Cast != null && metadata.Cast.Count > 0 ? string.Join(", ", metadata.Cast) : null;
                        result.Duration = metadata.Runtime ?? result.Duration;

                        // Update title/year if we got better info
                        if (!string.IsNullOrEmpty(metadata.Title)) result.Title = metadata.Title;
                        if (metadata.Year.HasValue) result.Year = metadata.Year;

                        _logger.LogInformation("Successfully enriched: {Title}", result.Title);
                    }
                    else
                    {
                        _logger.LogWarning("No metadata found for {Title}", result.Title);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to enrich metadata for {Title}", result.Title);
                }
            }
        });
    }

    private List<TorrentSearchResultDto> DeduplicateResults(List<TorrentSearchResultDto> results)
    {
        // Group by IMDb ID (if available) or title+year
        var groups = results.GroupBy(r =>
        {
            if (!string.IsNullOrEmpty(r.ImdbId))
                return $"imdb_{r.ImdbId}"; // Group all qualities together
            return $"title_{r.Title}_{r.Year}";
        });

        // Take best result from each group
        var deduplicated = new List<TorrentSearchResultDto>();

        foreach (var group in groups)
        {
            // Prefer YTS source for base metadata, but if not available use any with metadata
            var bestMetadata = group.FirstOrDefault(r => r.Source == "YTS") 
                             ?? group.FirstOrDefault(r => !string.IsNullOrEmpty(r.CoverImageUrl))
                             ?? group.First();

            // Create a merged result (we want to keep the best metadata but maybe list available qualities?)
            // For now, just return one entry per movie to avoid duplicates in the grid
            // The details page will fetch all torrents for this movie anyway
            deduplicated.Add(bestMetadata);
        }

        return deduplicated;
    }
}
