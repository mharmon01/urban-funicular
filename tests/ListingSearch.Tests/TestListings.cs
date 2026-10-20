using ListingSearch.Api.Data;
using ListingSearch.Api.Domain;
using ListingSearch.Api.Search;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace ListingSearch.Tests;

internal static class TestListings
{
    /// <summary>Fixed "today" so recency scores are deterministic.</summary>
    public static readonly DateOnly Today = new(2026, 9, 5);

    public static FakeTimeProvider Clock() => new(new DateTimeOffset(Today.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero));

    public static Listing Create(
        string id,
        string source = "MLS_A",
        decimal price = 500_000,
        int bedrooms = 3,
        string city = "Springfield",
        string listedDate = "2026-09-01",
        string description = "",
        string status = "active") => new()
        {
            Id = id,
            Source = source,
            Address = $"{id} Test St",
            City = city,
            State = "VA",
            Zip = "22150",
            Price = price,
            Bedrooms = bedrooms,
            Bathrooms = 2,
            Sqft = 1500,
            ListedDate = DateOnly.Parse(listedDate),
            Status = status,
            Description = description,
        };

    public static ListingSearchService Service(params Listing[] listings) =>
        new(new InMemoryListingRepository(listings), Scorer(), Clock());

    public static BudgetRecencyScorer Scorer(ScoringOptions? options = null) =>
        new(Options.Create(options ?? new ScoringOptions()));

    public static SearchCriteria Criteria(
        decimal? minPrice = null, decimal? maxPrice = null, int? minBedrooms = null, string? city = null,
        string? keyword = null, decimal? targetBudget = null, int page = 1, int pageSize = 10) => new()
        {
            MinPrice = minPrice,
            MaxPrice = maxPrice,
            MinBedrooms = minBedrooms,
            City = city,
            Keyword = keyword,
            TargetBudget = targetBudget,
            Page = page,
            PageSize = pageSize,
        };
}
