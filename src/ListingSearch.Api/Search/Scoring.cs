using ListingSearch.Api.Domain;
using Microsoft.Extensions.Options;

namespace ListingSearch.Api.Search;

/// <summary>Per-component scores (0–1) plus the combined relevance score (0–100).</summary>
public sealed record ScoreBreakdown(double? Budget, double Recency, double Total);

public interface IListingScorer
{
    ScoreBreakdown Score(Listing listing, SearchCriteria criteria, DateOnly today);
}

/// <summary>Tunable via the "Scoring" section of appsettings.json.</summary>
public sealed class ScoringOptions
{
    public double BudgetWeight { get; set; } = 0.6;
    public double RecencyWeight { get; set; } = 0.4;

    /// <summary>Relative distance from the target budget at which the budget score reaches 0.</summary>
    public double BudgetTolerance { get; set; } = 0.5;

    /// <summary>Multiplier on the distance when a listing is over budget (stretching hurts more than saving).</summary>
    public double OverBudgetPenalty { get; set; } = 1.5;

    /// <summary>Age in days at which the recency score has halved.</summary>
    public double RecencyHalfLifeDays { get; set; } = 30;

    public bool IsValid() =>
        BudgetWeight >= 0 && RecencyWeight >= 0 && BudgetWeight + RecencyWeight > 0 &&
        BudgetTolerance > 0 && OverBudgetPenalty >= 1 && RecencyHalfLifeDays > 0;
}

/// <summary>
/// Relevance = weighted average of how close the price is to targetBudget and how recently the
/// listing went live. See README "Scoring" for the rationale.
/// <list type="bullet">
/// <item>budget  = max(0, 1 - d / tolerance), d = |price - target| / target, d × penalty when over budget</item>
/// <item>recency = 0.5 ^ (ageDays / halfLife), future dates clamped to age 0</item>
/// <item>total   = 100 × (wB·budget + wR·recency) / (wB + wR); without a targetBudget, total = 100 × recency</item>
/// </list>
/// </summary>
public sealed class BudgetRecencyScorer(IOptions<ScoringOptions> options) : IListingScorer
{
    private readonly ScoringOptions _options = options.Value;

    public ScoreBreakdown Score(Listing listing, SearchCriteria criteria, DateOnly today)
    {
        var recency = RecencyScore(listing.ListedDate, today);
        var budget = criteria.TargetBudget is { } target ? BudgetScore(listing.Price, target) : (double?)null;

        var combined = budget is { } b
            ? (_options.BudgetWeight * b + _options.RecencyWeight * recency) / (_options.BudgetWeight + _options.RecencyWeight)
            : recency;

        // Rounded once here, and ranking sorts on this value, so listings that *display* the same
        // score are treated as tied and fall through to the deterministic tie-breakers.
        return new ScoreBreakdown(
            budget is null ? null : Math.Round(budget.Value, 3),
            Math.Round(recency, 3),
            Math.Round(combined * 100, 1));
    }

    internal double BudgetScore(decimal price, decimal target)
    {
        // Divide in double: decimal division overflows (throws) for tiny targets such as 1e-28,
        // whereas double just yields a huge distance, which correctly clamps the score to 0.
        var distance = (double)Math.Abs(price - target) / (double)target;
        if (price > target) distance *= _options.OverBudgetPenalty;
        return Math.Max(0, 1 - distance / _options.BudgetTolerance);
    }

    internal double RecencyScore(DateOnly listedDate, DateOnly today)
    {
        var ageDays = Math.Max(0, today.DayNumber - listedDate.DayNumber);
        return Math.Pow(0.5, ageDays / _options.RecencyHalfLifeDays);
    }
}
