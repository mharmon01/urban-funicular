using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ListingSearch.Api.Search;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace ListingSearch.Tests;

/// <summary>End-to-end over HTTP against the real sample_listings.json, with a fixed clock.</summary>
public class SearchApiTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client = factory
        .WithWebHostBuilder(b => b.ConfigureTestServices(s => s.AddSingleton<TimeProvider>(TestListings.Clock())))
        .CreateClient();

    private async Task<SearchResponse> SearchOk(string query)
    {
        var response = await _client.GetAsync($"/api/listings/search{query}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<SearchResponse>(JsonSerializerOptions.Web))!;
    }

    private async Task<ValidationProblemDetails> SearchBadRequest(string query)
    {
        var response = await _client.GetAsync($"/api/listings/search{query}");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        return (await response.Content.ReadFromJsonAsync<ValidationProblemDetails>(JsonSerializerOptions.Web))!;
    }

    [Fact]
    public async Task NoParameters_ReturnsFirstPageOfAllListings()
    {
        var result = await SearchOk("");

        Assert.Equal(12, result.TotalCount);
        Assert.Equal(10, result.Items.Count);
        Assert.Equal(2, result.TotalPages);
    }

    [Fact]
    public async Task TargetBudget_RanksExactPriceMatchFirst()
    {
        var result = await SearchOk("?city=springfield&targetBudget=450000");

        Assert.Equal(4, result.TotalCount);
        Assert.Equal(("MLS_A", "A1"), (result.Items[0].Source, result.Items[0].Id));
        Assert.Equal(1.0, result.Items[0].ScoreBreakdown.Budget);
        Assert.True(result.Items.Zip(result.Items.Skip(1)).All(p => p.First.Score >= p.Second.Score));
    }

    [Fact]
    public async Task NoMatches_Returns200WithEmptyItems()
    {
        var result = await SearchOk("?city=Reston&minBedrooms=10");

        Assert.Empty(result.Items);
        Assert.Equal(0, result.TotalPages);
    }

    [Theory]
    [InlineData("?minPrice=600000&maxPrice=500000", "minPrice")]
    [InlineData("?pageSize=0", "pageSize")]
    [InlineData("?minPrice=abc", "minPrice")]
    [InlineData("?city=Atlantis", "city")]
    [InlineData("?page=99", "page")]
    [InlineData("?max_price=1", "max_price")]
    [InlineData("?minPrice=99999999999999999999999999999999", "minPrice")]
    [InlineData("?minPrice=1&minPrice=2", "minPrice")]
    [InlineData("?city=&city=Reston", "city")]
    [InlineData("?PAGE=1&page=1", "page")]
    public async Task InvalidInput_Returns400ProblemWithFieldError(string query, string field)
    {
        var problem = await SearchBadRequest(query);

        Assert.Contains(field, problem.Errors.Keys);
    }

    [Fact]
    public async Task DataDependentAndShapeErrors_AreReportedTogether()
    {
        var problem = await SearchBadRequest("?minPrice=600000&maxPrice=500000&city=Atlantis");

        Assert.Equal(["city", "maxPrice", "minPrice"], problem.Errors.Keys.Order());
    }

    [Fact]
    public async Task TinyTargetBudget_IsHandledNotA500()
    {
        // Regression: this used to overflow decimal division in the scorer.
        var result = await SearchOk("?targetBudget=0.0000000000000000000000000001");

        Assert.All(result.Items, i => Assert.Equal(0.0, i.ScoreBreakdown.Budget));
    }

    [Fact]
    public async Task ParameterNames_AreCaseInsensitive()
    {
        Assert.Equal(4, (await SearchOk("?CITY=Springfield")).TotalCount);
    }

    [Fact]
    public async Task Cities_ReturnsDistinctSortedCities()
    {
        var cities = await _client.GetFromJsonAsync<string[]>("/api/listings/cities") ?? [];

        Assert.Equal(["Chantilly", "Fairfax", "Manassas", "Reston", "Springfield", "Vienna"], cities);
    }
}
