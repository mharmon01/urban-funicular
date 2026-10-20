using ListingSearch.Api.Domain;

namespace ListingSearch.Api.Data;

public interface IListingRepository
{
    IReadOnlyList<Listing> GetAll();
}
