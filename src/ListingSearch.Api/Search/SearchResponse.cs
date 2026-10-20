namespace ListingSearch.Api.Search;

public sealed record ListingResult(
    string Id,
    string Source,
    string Address,
    string City,
    string State,
    string Zip,
    decimal Price,
    int Bedrooms,
    double Bathrooms,
    int Sqft,
    DateOnly ListedDate,
    string Status,
    string Description,
    double Score,
    ScoreBreakdown ScoreBreakdown);

public sealed record SearchResponse(
    IReadOnlyList<ListingResult> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages);
