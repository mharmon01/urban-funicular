namespace ListingSearch.Api.Search;

/// <summary>
/// Raw query-string input. Everything is bound as a string on purpose so that malformed values
/// (e.g. <c>minPrice=abc</c>) get a field-specific validation message from
/// <see cref="SearchQueryValidator"/> instead of the framework's generic binding failure.
/// </summary>
public sealed record SearchQuery(
    string? MinPrice,
    string? MaxPrice,
    string? MinBedrooms,
    string? City,
    string? Keyword,
    string? TargetBudget,
    string? Page,
    string? PageSize);

/// <summary>Validated, strongly-typed search input. Only produced by <see cref="SearchQueryValidator"/>.</summary>
public sealed record SearchCriteria
{
    public decimal? MinPrice { get; init; }
    public decimal? MaxPrice { get; init; }
    public int? MinBedrooms { get; init; }
    public string? City { get; init; }
    public string? Keyword { get; init; }
    public decimal? TargetBudget { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = SearchQueryValidator.DefaultPageSize;
}
