using ListingSearch.Api.Search;
using Microsoft.AspNetCore.Http.HttpResults;

namespace ListingSearch.Api.Endpoints;

public static class ListingEndpoints
{
    public static IEndpointRouteBuilder MapListingEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/listings");
        group.MapGet("/search", Search);
        group.MapGet("/cities", (ListingSearchService service) => TypedResults.Ok(service.GetCities()));
        return app;
    }

    /// <summary>200 with a page of ranked results, or 400 ValidationProblem (RFC 7807) with per-field errors.</summary>
    private static Results<Ok<SearchResponse>, ValidationProblem> Search(
        [AsParameters] SearchQuery query, HttpRequest request, ListingSearchService service)
    {
        var criteria = SearchQueryValidator.Validate(query, request.Query, service.GetCities());
        if (!criteria.IsSuccess) return TypedResults.ValidationProblem(criteria.Errors);

        var result = service.Search(criteria.Value!);
        return result.IsSuccess
            ? TypedResults.Ok(result.Value!)
            : TypedResults.ValidationProblem(result.Errors);
    }
}
