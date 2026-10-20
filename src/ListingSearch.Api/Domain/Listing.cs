namespace ListingSearch.Api.Domain;

/// <summary>
/// A property listing as ingested from an MLS feed. <see cref="Id"/> is only unique within a
/// <see cref="Source"/>, so (Source, Id) is the identity of a listing.
/// </summary>
public sealed record Listing
{
    public required string Id { get; init; }
    public required string Source { get; init; }
    public required string Address { get; init; }
    public required string City { get; init; }
    public string State { get; init; } = "";
    public string Zip { get; init; } = "";
    public decimal Price { get; init; }
    public int Bedrooms { get; init; }
    public double Bathrooms { get; init; }
    public int Sqft { get; init; }
    public double Latitude { get; init; }
    public double Longitude { get; init; }
    public DateOnly ListedDate { get; init; }
    public string Status { get; init; } = "";
    public string Description { get; init; } = "";
}
