using ListingSearch.Api.Search;
using static ListingSearch.Tests.TestListings;

namespace ListingSearch.Tests;

public class BudgetRecencyScorerTests
{
    private readonly BudgetRecencyScorer _scorer = Scorer();

    [Fact]
    public void PriceEqualToTarget_ScoresFullBudget()
    {
        Assert.Equal(1.0, _scorer.BudgetScore(500_000, 500_000), 6);
    }

    [Fact]
    public void OverBudget_IsPenalisedMoreThanSameDistanceUnder()
    {
        // 10% away with tolerance 0.5: under → 1 - 0.1/0.5 = 0.8; over → 1 - 0.15/0.5 = 0.7
        Assert.Equal(0.8, _scorer.BudgetScore(450_000, 500_000), 6);
        Assert.Equal(0.7, _scorer.BudgetScore(550_000, 500_000), 6);
    }

    [Theory]
    [InlineData(100_000)]
    [InlineData(2_000_000)]
    public void FarFromTarget_ClampsToZero(int price)
    {
        Assert.Equal(0.0, _scorer.BudgetScore(price, 500_000));
    }

    [Fact]
    public void TinyTarget_ScoresZeroInsteadOfOverflowing()
    {
        // Regression: decimal division overflowed (HTTP 500) for the smallest positive decimal.
        Assert.Equal(0.0, _scorer.BudgetScore(610_000, 0.0000000000000000000000000001m));
    }

    [Fact]
    public void Recency_HalvesEveryHalfLife()
    {
        Assert.Equal(1.0, _scorer.RecencyScore(Today, Today), 6);
        Assert.Equal(0.5, _scorer.RecencyScore(Today.AddDays(-30), Today), 6);
        Assert.Equal(0.25, _scorer.RecencyScore(Today.AddDays(-60), Today), 6);
    }

    [Fact]
    public void FutureListedDate_IsClampedToToday()
    {
        Assert.Equal(1.0, _scorer.RecencyScore(Today.AddDays(10), Today), 6);
    }

    [Fact]
    public void WithTargetBudget_CombinesWeightedComponents()
    {
        var listing = Create("1", price: 500_000, listedDate: Today.AddDays(-30).ToString("O"));

        var score = _scorer.Score(listing, Criteria(targetBudget: 500_000), Today);

        // 0.6 × 1.0 + 0.4 × 0.5 = 0.8
        Assert.Equal(new ScoreBreakdown(1.0, 0.5, 80.0), score);
    }

    [Fact]
    public void WithoutTargetBudget_ScoreIsRecencyOnly()
    {
        var listing = Create("1", listedDate: Today.AddDays(-30).ToString("O"));

        var score = _scorer.Score(listing, Criteria(), Today);

        Assert.Equal(new ScoreBreakdown(null, 0.5, 50.0), score);
    }

    [Fact]
    public void Weights_AreConfigurable()
    {
        var budgetOnly = Scorer(new ScoringOptions { BudgetWeight = 1, RecencyWeight = 0 });
        var listing = Create("1", price: 450_000, listedDate: "2020-01-01");

        Assert.Equal(80.0, budgetOnly.Score(listing, Criteria(targetBudget: 500_000), Today).Total);
    }
}
