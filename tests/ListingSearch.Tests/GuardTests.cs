using ListingSearch.Api.Data;
using ListingSearch.Api.Search;

namespace ListingSearch.Tests;

/// <summary>Defensive checks that protect the app from bad configuration, bad data files and misuse.</summary>
public class GuardTests
{
    [Fact]
    public void DefaultScoringOptions_AreValid()
    {
        Assert.True(new ScoringOptions().IsValid());
    }

    public static TheoryData<ScoringOptions> InvalidScoringOptions => new()
    {
        new ScoringOptions { BudgetWeight = -0.1 },
        new ScoringOptions { RecencyWeight = -0.1 },
        new ScoringOptions { BudgetWeight = 0, RecencyWeight = 0 },
        new ScoringOptions { BudgetTolerance = 0 },
        new ScoringOptions { OverBudgetPenalty = 0.9 },
        new ScoringOptions { RecencyHalfLifeDays = 0 },
    };

    [Theory]
    [MemberData(nameof(InvalidScoringOptions))]
    public void InvalidScoringOptions_AreRejected(ScoringOptions options)
    {
        Assert.False(options.IsValid());
    }

    [Fact]
    public void ResultFailure_RequiresAtLeastOneError()
    {
        Assert.Throws<ArgumentException>(() => Result<int>.Failure(new Dictionary<string, string[]>()));
    }

    [Fact]
    public void Repository_LoadsListingsFromJson()
    {
        var path = WriteTempFile("""[{ "id": "A1", "source": "MLS_A", "address": "1 Main St", "city": "Reston", "listedDate": "2026-09-01" }]""");

        var listing = Assert.Single(InMemoryListingRepository.LoadFromJsonFile(path).GetAll());

        Assert.Equal(("MLS_A", "A1", new DateOnly(2026, 9, 1)), (listing.Source, listing.Id, listing.ListedDate));
    }

    [Fact]
    public void Repository_RejectsAFileContainingNull()
    {
        Assert.Throws<InvalidDataException>(() => InMemoryListingRepository.LoadFromJsonFile(WriteTempFile("null")));
    }

    private static string WriteTempFile(string content)
    {
        var path = Path.GetTempFileName();
        File.WriteAllText(path, content);
        return path;
    }
}
