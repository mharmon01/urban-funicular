using ListingSearch.Api.Search;
using static ListingSearch.Tests.TestListings;

namespace ListingSearch.Tests;

public class ListingSearchServiceTests
{
    /// <summary>Result keys in ranked order — use when the order is what's being tested.</summary>
    private static IEnumerable<string> Ids(Result<SearchResponse> result) =>
        Assert.IsType<SearchResponse>(result.Value).Items.Select(i => $"{i.Source}:{i.Id}");

    /// <summary>Result keys as a set — use when only membership matters (filter tests).</summary>
    private static HashSet<string> IdSet(Result<SearchResponse> result) => [.. Ids(result)];

    private static HashSet<string> Set(params string[] ids) => [.. ids];

    // ---- Filtering ----

    [Fact]
    public void NoFilters_ReturnsEverything()
    {
        var result = Service(Create("1"), Create("2"), Create("3")).Search(Criteria());

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Value!.TotalCount);
    }

    [Fact]
    public void PriceBounds_AreInclusive()
    {
        var service = Service(Create("low", price: 100), Create("mid", price: 200), Create("high", price: 300));

        var result = service.Search(Criteria(minPrice: 200, maxPrice: 300));

        Assert.Equal(Set("MLS_A:mid", "MLS_A:high"), IdSet(result));
    }

    [Fact]
    public void MinBedrooms_IsInclusive()
    {
        var service = Service(Create("two", bedrooms: 2), Create("three", bedrooms: 3), Create("four", bedrooms: 4));

        Assert.Equal(2, service.Search(Criteria(minBedrooms: 3)).Value!.TotalCount);
    }

    [Fact]
    public void City_IsCaseAndWhitespaceInsensitive()
    {
        var service = Service(Create("1", city: "Reston"), Create("2", city: " reston "), Create("3", city: "Vienna"));

        Assert.Equal(2, service.Search(Criteria(city: "RESTON")).Value!.TotalCount);
    }

    [Fact]
    public void Keyword_IsCaseInsensitiveAndRequiresAllTerms()
    {
        var service = Service(
            Create("pool", description: "Townhome with community POOL and garage."),
            Create("garage", description: "Detached garage."),
            Create("neither", description: "Cozy starter home."));

        Assert.Equal(Set("MLS_A:pool", "MLS_A:garage"), IdSet(service.Search(Criteria(keyword: "garage"))));
        Assert.Equal(Set("MLS_A:pool"), IdSet(service.Search(Criteria(keyword: "pool GARAGE"))));
    }

    [Fact]
    public void Filters_CombineWithAnd()
    {
        var service = Service(
            Create("match", price: 400_000, bedrooms: 3, city: "Reston", description: "pets welcome"),
            Create("wrongCity", price: 400_000, bedrooms: 3, city: "Vienna", description: "pets welcome"),
            Create("tooFewBeds", price: 400_000, bedrooms: 1, city: "Reston", description: "pets welcome"),
            Create("noKeyword", price: 400_000, bedrooms: 3, city: "Reston", description: "quiet street"));

        var result = service.Search(Criteria(maxPrice: 450_000, minBedrooms: 2, city: "reston", keyword: "pets"));

        Assert.Equal(Set("MLS_A:match"), IdSet(result));
    }

    [Fact]
    public void SameIdFromDifferentSources_AreDistinctListings()
    {
        var result = Service(Create("1", source: "MLS_A"), Create("1", source: "MLS_B")).Search(Criteria());

        Assert.Equal(2, result.Value!.TotalCount);
    }

    // ---- No matches (unknown-city rejection is a validator concern: see SearchQueryValidatorTests) ----

    [Fact]
    public void NoMatchingListings_ReturnsEmptySuccessfulPage()
    {
        var result = Service(Create("1", price: 100), Create("2", price: 200)).Search(Criteria(minPrice: 1_000_000));

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!.Items);
        Assert.Equal(0, result.Value.TotalCount);
        Assert.Equal(0, result.Value.TotalPages);
        Assert.Equal(1, result.Value.Page);
    }

    [Fact]
    public void KnownCityWithNoMatchesForOtherFilters_IsAnEmptyResultNotAnError()
    {
        var result = Service(Create("1", city: "Reston", bedrooms: 2)).Search(Criteria(city: "Reston", minBedrooms: 5));

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!.Items);
    }

    [Fact]
    public void EmptyDataset_ReturnsEmptyPage()
    {
        var result = Service().Search(Criteria());

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value!.TotalCount);
    }

    // ---- Ranking ----

    [Fact]
    public void Ranking_PrefersPriceCloserToTargetBudget()
    {
        var service = Service(
            Create("far", price: 300_000),
            Create("close", price: 495_000),
            Create("over", price: 560_000));

        Assert.Equal(["MLS_A:close", "MLS_A:over", "MLS_A:far"], Ids(service.Search(Criteria(targetBudget: 500_000))));
    }

    [Fact]
    public void Ranking_WithoutTargetBudget_PrefersNewerListings()
    {
        var service = Service(
            Create("old", listedDate: "2026-07-01"),
            Create("new", listedDate: "2026-09-04"),
            Create("mid", listedDate: "2026-08-15"));

        Assert.Equal(["MLS_A:new", "MLS_A:mid", "MLS_A:old"], Ids(service.Search(Criteria())));
    }

    [Fact]
    public void TiedScores_BreakByNewerListingFirst()
    {
        // Years-old listings both round to a score of 0.0, so they tie even though their dates differ.
        var service = Service(
            Create("older", price: 100, listedDate: "2020-01-01"),
            Create("newer", price: 200, listedDate: "2020-01-02"));

        var result = service.Search(Criteria());

        Assert.All(result.Value!.Items, i => Assert.Equal(0.0, i.Score));
        Assert.Equal(["MLS_A:newer", "MLS_A:older"], Ids(result));
    }

    [Fact]
    public void TiedScores_SameDate_BreakByLowerPriceFirst()
    {
        // Without a targetBudget, price doesn't affect the score: same date → exact tie.
        var service = Service(
            Create("pricier", price: 500_000, listedDate: "2026-09-03"),
            Create("cheaper", price: 450_000, listedDate: "2026-09-03"));

        var result = service.Search(Criteria());

        Assert.Equal(result.Value!.Items[0].Score, result.Value.Items[1].Score);
        Assert.Equal(["MLS_A:cheaper", "MLS_A:pricier"], Ids(result));
    }

    [Fact]
    public void Ranking_UsesDisplayedScore_SoVisuallyTiedListingsFallToTieBreakers()
    {
        // Raw scores 99.973 (cheaper, under budget) vs 99.980 (pricier, over budget) both display as 100.0.
        // Ranking on the displayed value makes them a tie, so the lower price wins; ranking on the raw
        // value would put the pricier one first, contradicting the scores the user can see.
        var service = Service(
            Create("cheaper", price: 449_900, listedDate: Today.ToString("O")),
            Create("pricier", price: 450_050, listedDate: Today.ToString("O")));

        var result = service.Search(Criteria(targetBudget: 450_000));

        Assert.All(result.Value!.Items, i => Assert.Equal(100.0, i.Score));
        Assert.Equal(["MLS_A:cheaper", "MLS_A:pricier"], Ids(result));
    }

    [Fact]
    public void TiedScores_FullyIdentical_BreakBySourceThenId()
    {
        var service = Service(
            Create("2", source: "MLS_B"),
            Create("2", source: "MLS_A"),
            Create("1", source: "MLS_B"),
            Create("1", source: "MLS_A"));

        Assert.Equal(["MLS_A:1", "MLS_A:2", "MLS_B:1", "MLS_B:2"], Ids(service.Search(Criteria())));
    }

    [Fact]
    public void Ranking_IsIndependentOfInputOrder()
    {
        var listings = Enumerable.Range(0, 20)
            .Select(i => Create($"{i}", source: i % 2 == 0 ? "MLS_A" : "MLS_B", price: 400_000 + (i % 3) * 10_000,
                listedDate: $"2026-08-{10 + i % 4:00}"))
            .ToArray();

        var forward = Ids(Service(listings).Search(Criteria(targetBudget: 410_000, pageSize: 50)));
        var reversed = Ids(Service(listings.Reverse().ToArray()).Search(Criteria(targetBudget: 410_000, pageSize: 50)));

        Assert.Equal(forward, reversed);
    }

    // ---- Pagination ----

    private static ListingSearchService FiveListingsNewestFirst() => Service(
        Create("1", listedDate: "2026-09-05"),
        Create("2", listedDate: "2026-09-04"),
        Create("3", listedDate: "2026-09-03"),
        Create("4", listedDate: "2026-09-02"),
        Create("5", listedDate: "2026-09-01"));

    [Fact]
    public void Pagination_ReturnsRequestedPageAndMetadata()
    {
        var result = FiveListingsNewestFirst().Search(Criteria(page: 2, pageSize: 2));

        Assert.Equal(["MLS_A:3", "MLS_A:4"], Ids(result));
        Assert.Equal(new { Page = 2, PageSize = 2, TotalCount = 5, TotalPages = 3 },
            new { result.Value!.Page, result.Value.PageSize, result.Value.TotalCount, result.Value.TotalPages });
    }

    [Fact]
    public void Pagination_LastPageMayBePartial()
    {
        Assert.Equal(["MLS_A:5"], Ids(FiveListingsNewestFirst().Search(Criteria(page: 3, pageSize: 2))));
    }

    [Fact]
    public void Pagination_ExactMultipleOfPageSize_HasNoTrailingEmptyPage()
    {
        var result = FiveListingsNewestFirst().Search(Criteria(page: 1, pageSize: 5));

        Assert.Equal(1, result.Value!.TotalPages);
        Assert.Equal(5, result.Value.Items.Count);
        Assert.False(FiveListingsNewestFirst().Search(Criteria(page: 2, pageSize: 5)).IsSuccess);
    }

    [Fact]
    public void Pagination_PageBeyondLast_IsAnError()
    {
        var result = FiveListingsNewestFirst().Search(Criteria(page: 4, pageSize: 2));

        Assert.False(result.IsSuccess);
        Assert.Contains("3 page(s)", Assert.Single(result.Errors["page"]));
    }

    [Fact]
    public void Pagination_PageTwoOfEmptyResult_IsAnError()
    {
        Assert.False(Service().Search(Criteria(page: 2)).IsSuccess);
    }

    [Fact]
    public void Pagination_PagesTogetherCoverAllResultsExactlyOnce()
    {
        var service = FiveListingsNewestFirst();
        var all = Ids(service.Search(Criteria(pageSize: 50)));

        var paged = Enumerable.Range(1, 3).SelectMany(p => Ids(service.Search(Criteria(page: p, pageSize: 2))));

        Assert.Equal(all, paged);
    }

    // ---- Supporting endpoint ----

    [Fact]
    public void GetCities_IsDistinctCaseInsensitiveAndSorted()
    {
        var service = Service(Create("1", city: "Vienna"), Create("2", city: "Reston"), Create("3", city: " vienna "));

        Assert.Equal(["Reston", "Vienna"], service.GetCities());
    }

    [Fact]
    public void GetCities_PrefersMostCommonSpelling_RegardlessOfOrder()
    {
        var service = Service(Create("1", city: "reston"), Create("2", city: "Reston"), Create("3", city: "Reston"));

        Assert.Equal(["Reston"], service.GetCities());
    }

    [Fact]
    public void GetCities_TiedSpellings_PreferCapitalised()
    {
        Assert.Equal(["Reston"], Service(Create("1", city: "reston"), Create("2", city: "Reston")).GetCities());
    }

    // ---- "Today" ----

    [Fact]
    public void Recency_UsesMarketTimeZoneForToday()
    {
        // 02:00 UTC on Sep 6 is still the evening of Sep 5 in New York, so a listing dated Sep 5 is brand new.
        var clock = new Microsoft.Extensions.Time.Testing.FakeTimeProvider(new DateTimeOffset(2026, 9, 6, 2, 0, 0, TimeSpan.Zero));
        clock.SetLocalTimeZone(TimeZoneInfo.FindSystemTimeZoneById("America/New_York"));
        var service = new ListingSearchService(
            new Api.Data.InMemoryListingRepository([Create("1", listedDate: "2026-09-05")]), Scorer(), clock);

        var item = Assert.Single(service.Search(Criteria()).Value!.Items);

        Assert.Equal(1.0, item.ScoreBreakdown.Recency);
    }
}
