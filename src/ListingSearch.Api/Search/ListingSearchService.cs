using ListingSearch.Api.Data;
using ListingSearch.Api.Domain;
using static System.FormattableString;

namespace ListingSearch.Api.Search;

/// <summary>
/// Runs a search as an explicit pipeline: filter → score → rank → paginate.
/// Each stage is a small, separately testable step, so new requirements slot in as a stage
/// (e.g. cross-feed de-duplication — see README "Extending").
/// Expects criteria already validated by <see cref="SearchQueryValidator"/> (including the city check).
/// </summary>
public sealed class ListingSearchService(IListingRepository repository, IListingScorer scorer, TimeProvider timeProvider)
{
    /// <summary>
    /// Distinct cities, compared case- and whitespace-insensitively. When feeds spell a city differently,
    /// the most common spelling is shown (ties → ordinal order, which puts "Reston" before "reston").
    /// </summary>
    public IReadOnlyList<string> GetCities() =>
        repository.GetAll()
            .Select(l => l.City.Trim())
            .GroupBy(c => c, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.GroupBy(c => c, StringComparer.Ordinal)
                .OrderByDescending(spelling => spelling.Count())
                .ThenBy(spelling => spelling.Key, StringComparer.Ordinal)
                .First().Key)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();

    public Result<SearchResponse> Search(SearchCriteria criteria)
    {
        var matches = repository.GetAll().Where(l => ListingFilters.Matches(l, criteria));
        var ranked = Rank(matches, criteria);

        var totalCount = ranked.Count;
        var totalPages = (int)Math.Ceiling(totalCount / (double)criteria.PageSize);

        // Page 1 of an empty result is a valid "no matches" answer; anything past the last page is a client error.
        if (criteria.Page > Math.Max(totalPages, 1))
        {
            return Result<SearchResponse>.Failure("page",
                Invariant($"page {criteria.Page} is out of range; this search has {totalPages} page(s)."));
        }

        var items = ranked
            .Skip((criteria.Page - 1) * criteria.PageSize)
            .Take(criteria.PageSize)
            .Select(s => ToResult(s.Listing, s.Score))
            .ToList();

        return Result<SearchResponse>.Success(
            new SearchResponse(items, criteria.Page, criteria.PageSize, totalCount, totalPages));
    }

    /// <summary>
    /// Highest score first. Ties are broken deterministically — newer listing, then lower price,
    /// then (source, id) — so the same query always yields the same order and pages never overlap
    /// or skip listings.
    /// </summary>
    private List<(Listing Listing, ScoreBreakdown Score)> Rank(IEnumerable<Listing> listings, SearchCriteria criteria)
    {
        // Listing dates are calendar dates in the market's time zone, so "today" must be too
        // (TimeProvider.LocalTimeZone is configured as the market zone; see MarketTimeProvider).
        var today = DateOnly.FromDateTime(timeProvider.GetLocalNow().DateTime);
        return listings
            .Select(l => (Listing: l, Score: scorer.Score(l, criteria, today)))
            .OrderByDescending(s => s.Score.Total)
            .ThenByDescending(s => s.Listing.ListedDate)
            .ThenBy(s => s.Listing.Price)
            .ThenBy(s => s.Listing.Source, StringComparer.Ordinal)
            .ThenBy(s => s.Listing.Id, StringComparer.Ordinal)
            .ToList();
    }

    private static ListingResult ToResult(Listing l, ScoreBreakdown score) => new(
        l.Id, l.Source, l.Address, l.City, l.State, l.Zip, l.Price, l.Bedrooms, l.Bathrooms, l.Sqft,
        l.ListedDate, l.Status, l.Description, score.Total, score);
}
