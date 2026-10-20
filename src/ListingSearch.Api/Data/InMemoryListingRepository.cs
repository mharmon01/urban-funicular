using System.Text.Json;
using ListingSearch.Api.Domain;

namespace ListingSearch.Api.Data;

/// <summary>
/// Holds listings in memory. The data is loaded once and never mutated, so it is safe to share
/// as a singleton across concurrent requests.
/// </summary>
public sealed class InMemoryListingRepository(IEnumerable<Listing> listings) : IListingRepository
{
    private readonly IReadOnlyList<Listing> _listings = listings.ToList();

    public IReadOnlyList<Listing> GetAll() => _listings;

    public static InMemoryListingRepository LoadFromJsonFile(string path)
    {
        using var stream = File.OpenRead(path);
        var listings = JsonSerializer.Deserialize<List<Listing>>(stream, JsonSerializerOptions.Web)
            ?? throw new InvalidDataException($"Listing file '{path}' contained no data.");
        return new InMemoryListingRepository(listings);
    }
}
