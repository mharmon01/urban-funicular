using System.Globalization;
using ListingSearch.Api.Search;
using Microsoft.Extensions.Primitives;

namespace ListingSearch.Tests;

public class SearchQueryValidatorTests
{
    private static SearchQuery Query(
        string? minPrice = null, string? maxPrice = null, string? minBedrooms = null, string? city = null,
        string? keyword = null, string? targetBudget = null, string? page = null, string? pageSize = null) =>
        new(minPrice, maxPrice, minBedrooms, city, keyword, targetBudget, page, pageSize);

    [Fact]
    public void EmptyQuery_UsesDefaults()
    {
        var result = SearchQueryValidator.Validate(Query());

        Assert.True(result.IsSuccess);
        Assert.Equal(TestListings.Criteria(), result.Value);
    }

    [Fact]
    public void BlankValues_AreTreatedAsNotProvided()
    {
        var result = SearchQueryValidator.Validate(Query(minPrice: "", maxPrice: "  ", city: " ", keyword: "", page: "", pageSize: " "));

        Assert.True(result.IsSuccess);
        Assert.Equal(TestListings.Criteria(), result.Value);
    }

    [Fact]
    public void ValidValues_AreParsedAndTrimmed()
    {
        var result = SearchQueryValidator.Validate(Query("100000", "600000.50", "2", "  Reston ", " pool ", "500000", "3", "25"));

        Assert.True(result.IsSuccess);
        Assert.Equal(
            TestListings.Criteria(100_000, 600_000.50m, 2, "Reston", "pool", 500_000, 3, 25),
            result.Value);
    }

    [Fact]
    public void MinPriceGreaterThanMaxPrice_ReportsErrorOnBothFields()
    {
        var result = SearchQueryValidator.Validate(Query(minPrice: "600000", maxPrice: "500000"));

        Assert.False(result.IsSuccess);
        Assert.Contains("minPrice", result.Errors.Keys);
        Assert.Contains("maxPrice", result.Errors.Keys);
    }

    [Fact]
    public void MinPriceEqualToMaxPrice_IsValid()
    {
        Assert.True(SearchQueryValidator.Validate(Query(minPrice: "500000", maxPrice: "500000")).IsSuccess);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("51")]
    public void PageSizeOutOfRange_IsRejected(string pageSize)
    {
        var result = SearchQueryValidator.Validate(Query(pageSize: pageSize));

        Assert.False(result.IsSuccess);
        Assert.Contains("pageSize", result.Errors.Keys);
    }

    [Theory]
    [InlineData("1")]
    [InlineData("50")]
    public void PageSizeAtBoundary_IsAccepted(string pageSize)
    {
        Assert.True(SearchQueryValidator.Validate(Query(pageSize: pageSize)).IsSuccess);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-3")]
    public void PageLessThanOne_IsRejected(string page)
    {
        Assert.Contains("page", SearchQueryValidator.Validate(Query(page: page)).Errors.Keys);
    }

    [Theory]
    [InlineData("minPrice", "-1")]
    [InlineData("maxPrice", "-1")]
    [InlineData("minBedrooms", "-1")]
    [InlineData("targetBudget", "0")]
    [InlineData("targetBudget", "-100")]
    public void NegativeOrZeroValues_AreRejected(string field, string value)
    {
        var query = field switch
        {
            "minPrice" => Query(minPrice: value),
            "maxPrice" => Query(maxPrice: value),
            "minBedrooms" => Query(minBedrooms: value),
            _ => Query(targetBudget: value),
        };

        Assert.Contains(field, SearchQueryValidator.Validate(query).Errors.Keys);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("1,000")]
    [InlineData("1e6")]
    [InlineData("$500")]
    public void NonNumericPrice_IsRejectedWithFieldMessage(string value)
    {
        var result = SearchQueryValidator.Validate(Query(minPrice: value));

        Assert.False(result.IsSuccess);
        Assert.Contains("must be a number", Assert.Single(result.Errors["minPrice"]));
    }

    [Theory]
    [InlineData("2.5")]
    [InlineData("two")]
    public void NonIntegerBedrooms_IsRejected(string value)
    {
        Assert.Contains("minBedrooms", SearchQueryValidator.Validate(Query(minBedrooms: value)).Errors.Keys);
    }

    [Fact]
    public void OverlongKeyword_IsRejected()
    {
        var keyword = new string('a', SearchQueryValidator.MaxKeywordLength + 1);

        Assert.Contains("keyword", SearchQueryValidator.Validate(Query(keyword: keyword)).Errors.Keys);
    }

    [Fact]
    public void MultipleProblems_AreAllReported()
    {
        var result = SearchQueryValidator.Validate(Query(minPrice: "abc", minBedrooms: "-1", page: "0", pageSize: "0"));

        Assert.Equal(["minBedrooms", "minPrice", "page", "pageSize"], result.Errors.Keys.Order());
    }

    // ---- Out-of-range numbers ----

    [Theory]
    [InlineData("minPrice", "99999999999999999999999999999999")]
    [InlineData("pageSize", "99999999999")]
    [InlineData("minBedrooms", "-99999999999")]
    public void HugeNumbers_AreReportedAsTooLargeNotAsNonNumeric(string field, string value)
    {
        var query = field switch
        {
            "minPrice" => Query(minPrice: value),
            "pageSize" => Query(pageSize: value),
            _ => Query(minBedrooms: value),
        };

        Assert.Contains("is too large", Assert.Single(SearchQueryValidator.Validate(query).Errors[field]));
    }

    [Fact]
    public void ErrorMessages_UseInvariantNumberFormatting()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE"); // would print 600000,5
            var result = SearchQueryValidator.Validate(Query(minPrice: "600000.5", maxPrice: "500000"));

            Assert.Contains("(600000.5)", result.Errors["minPrice"][0]);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    // ---- Unknown / repeated parameters ----

    private static Dictionary<string, StringValues> Sent(params (string Name, string[] Values)[] parameters) =>
        parameters.ToDictionary(p => p.Name, p => new StringValues(p.Values));

    [Fact]
    public void UnknownParameter_IsRejected()
    {
        var result = SearchQueryValidator.Validate(Query(), Sent(("max_price", ["1"])));

        Assert.Contains("Unknown parameter 'max_price'", Assert.Single(result.Errors["max_price"]));
    }

    [Fact]
    public void SupportedParameters_AreAcceptedCaseInsensitively()
    {
        var sent = Sent(("MINPRICE", ["1"]), ("pagesize", ["5"]), ("city", ["Reston"]));

        Assert.True(SearchQueryValidator.Validate(Query(minPrice: "1", pageSize: "5", city: "Reston"), sent).IsSuccess);
    }

    [Fact]
    public void RepeatedParameter_IsRejectedUnderItsCanonicalName()
    {
        // The framework binds repeated values joined ("1,2"); that must not surface as a parse error.
        var result = SearchQueryValidator.Validate(Query(minPrice: "1,2"), Sent(("MinPrice", ["1", "2"])));

        Assert.Equal(["minPrice"], result.Errors.Keys);
        Assert.Contains("given 2 times", Assert.Single(result.Errors["minPrice"]));
    }

    [Fact]
    public void RepeatedParameter_WithAnEmptyValue_IsStillRejected()
    {
        // The framework would drop the empty value and silently use "Reston".
        var result = SearchQueryValidator.Validate(Query(city: "Reston"), Sent(("city", ["", "Reston"])), knownCities: KnownCities);

        Assert.Equal(["city"], result.Errors.Keys);
    }

    // ---- City must exist in the data ----

    private static readonly string[] KnownCities = ["Reston", "Vienna"];

    [Fact]
    public void UnknownCity_IsRejectedWithListOfKnownCities()
    {
        var result = SearchQueryValidator.Validate(Query(city: "Atlantis"), knownCities: KnownCities);

        var message = Assert.Single(result.Errors["city"]);
        Assert.Contains("'Atlantis'", message);
        Assert.Contains("Reston, Vienna", message);
    }

    [Fact]
    public void KnownCity_IsMatchedCaseAndWhitespaceInsensitively()
    {
        Assert.True(SearchQueryValidator.Validate(Query(city: "  vIeNnA "), knownCities: KnownCities).IsSuccess);
    }

    [Fact]
    public void UnknownCity_IsReportedTogetherWithOtherErrors()
    {
        var result = SearchQueryValidator.Validate(
            Query(minPrice: "600000", maxPrice: "500000", city: "Atlantis"), knownCities: KnownCities);

        Assert.Equal(["city", "maxPrice", "minPrice"], result.Errors.Keys.Order());
    }
}
