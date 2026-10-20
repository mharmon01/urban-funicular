using ListingSearch.Api.Domain;

namespace ListingSearch.Api.Search;

/// <summary>Filter predicates. All bounds are inclusive; all text comparisons are case-insensitive.</summary>
public static class ListingFilters
{
    public static bool Matches(Listing listing, SearchCriteria criteria) =>
        (criteria.MinPrice is null || listing.Price >= criteria.MinPrice) &&
        (criteria.MaxPrice is null || listing.Price <= criteria.MaxPrice) &&
        (criteria.MinBedrooms is null || listing.Bedrooms >= criteria.MinBedrooms) &&
        (criteria.City is null || CityEquals(listing.City, criteria.City)) &&
        (criteria.Keyword is null || DescriptionMatches(listing.Description, criteria.Keyword));

    /// <summary>Feeds disagree on casing/whitespace, so cities are compared trimmed and case-insensitively.</summary>
    public static bool CityEquals(string listingCity, string city) =>
        string.Equals(listingCity.Trim(), city.Trim(), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Every whitespace-separated term must appear in the description (AND semantics, substring match,
    /// so "pet" matches "pets"). Known limitation: no negation awareness — "pets" also matches "No pets".
    /// </summary>
    public static bool DescriptionMatches(string description, string keyword) =>
        keyword.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .All(term => description.Contains(term, StringComparison.OrdinalIgnoreCase));
}
