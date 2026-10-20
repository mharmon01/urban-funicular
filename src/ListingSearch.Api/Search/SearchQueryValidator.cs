using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Primitives;
using static System.FormattableString;

namespace ListingSearch.Api.Search;

/// <summary>
/// Parses and validates raw query input into <see cref="SearchCriteria"/>. Pure: anything it needs
/// from the outside world (which parameters were sent, which cities exist) is passed in.
/// All errors are collected rather than stopping at the first, so the UI can show every problem at once.
/// The one rule that can't live here is "page past the last page", which depends on the filtered
/// result count and is checked by <see cref="ListingSearchService"/>.
/// </summary>
public static partial class SearchQueryValidator
{
    public const int DefaultPageSize = 10;
    public const int MaxPageSize = 50;
    public const int MaxKeywordLength = 200;

    public static readonly IReadOnlyList<string> SupportedParameters =
        ["minPrice", "maxPrice", "minBedrooms", "city", "keyword", "targetBudget", "page", "pageSize"];

    // Deliberately excludes thousands separators and exponents: "1,000" is ambiguous across cultures.
    private const NumberStyles DecimalStyle =
        NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite |
        NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint;

    /// <param name="query">Raw values bound from the query string.</param>
    /// <param name="parameters">
    /// The query string as sent (name → values; ASP.NET Core merges names case-insensitively).
    /// Unrecognised names (e.g. a typo like <c>max_price</c>) are rejected, because silently ignoring
    /// them would return unfiltered — i.e. wrong — data. Repeated names (<c>minPrice=1&amp;minPrice=2</c>)
    /// are rejected because there's no right answer: the framework would join them as "1,2" (and drop
    /// empty ones), producing either a confusing parse error or a silently-chosen value.
    /// </param>
    /// <param name="knownCities">
    /// Cities present in the data. A city outside this set is reported as an error (almost certainly a
    /// typo) rather than producing an empty result. Pass null to skip the check.
    /// </param>
    public static Result<SearchCriteria> Validate(
        SearchQuery query,
        IEnumerable<KeyValuePair<string, StringValues>>? parameters = null,
        IReadOnlyCollection<string>? knownCities = null)
    {
        var errors = new Dictionary<string, List<string>>();
        void Error(string field, string message)
        {
            if (!errors.TryGetValue(field, out var list)) errors[field] = list = [];
            list.Add(message);
        }

        var repeated = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, values) in parameters ?? [])
        {
            if (!SupportedParameters.Contains(name, StringComparer.OrdinalIgnoreCase))
                Error(name, $"Unknown parameter '{name}'. Supported parameters: {string.Join(", ", SupportedParameters)}.");
            else if (values.Count > 1)
            {
                repeated.Add(name);
                Error(SupportedParameters.First(p => p.Equals(name, StringComparison.OrdinalIgnoreCase)),
                    Invariant($"{name} was given {values.Count} times; specify it once."));
            }
        }

        // A repeated parameter is already an error; parsing its joined value would only add noise.
        string? Single(string name, string? raw) => repeated.Contains(name) ? null : raw;

        var minPrice = ParseDecimal(Single("minPrice", query.MinPrice), "minPrice", Error);
        var maxPrice = ParseDecimal(Single("maxPrice", query.MaxPrice), "maxPrice", Error);
        var targetBudget = ParseDecimal(Single("targetBudget", query.TargetBudget), "targetBudget", Error);
        var minBedrooms = ParseInt(Single("minBedrooms", query.MinBedrooms), "minBedrooms", Error);
        var page = ParseInt(Single("page", query.Page), "page", Error);
        var pageSize = ParseInt(Single("pageSize", query.PageSize), "pageSize", Error);

        if (minPrice < 0) Error("minPrice", "minPrice cannot be negative.");
        if (maxPrice < 0) Error("maxPrice", "maxPrice cannot be negative.");
        if (minPrice > maxPrice)
        {
            Error("minPrice", Invariant($"minPrice ({minPrice}) cannot be greater than maxPrice ({maxPrice})."));
            Error("maxPrice", Invariant($"maxPrice ({maxPrice}) cannot be less than minPrice ({minPrice})."));
        }
        if (targetBudget <= 0) Error("targetBudget", "targetBudget must be greater than zero.");
        if (minBedrooms < 0) Error("minBedrooms", "minBedrooms cannot be negative.");
        if (page < 1) Error("page", "page must be 1 or greater.");
        if (pageSize is < 1 or > MaxPageSize) Error("pageSize", Invariant($"pageSize must be between 1 and {MaxPageSize}."));

        var keyword = Normalize(Single("keyword", query.Keyword));
        if (keyword?.Length > MaxKeywordLength) Error("keyword", Invariant($"keyword cannot exceed {MaxKeywordLength} characters."));

        var city = Normalize(Single("city", query.City));
        if (city is not null && knownCities is not null && !knownCities.Any(known => ListingFilters.CityEquals(known, city)))
            Error("city", $"No listings exist in city '{city}'. Known cities: {string.Join(", ", knownCities)}.");

        if (errors.Count > 0)
            return Result<SearchCriteria>.Failure(errors.ToDictionary(e => e.Key, e => e.Value.ToArray()));

        return Result<SearchCriteria>.Success(new SearchCriteria
        {
            MinPrice = minPrice,
            MaxPrice = maxPrice,
            MinBedrooms = minBedrooms,
            City = city,
            Keyword = keyword,
            TargetBudget = targetBudget,
            Page = page ?? 1,
            PageSize = pageSize ?? DefaultPageSize,
        });
    }

    /// <summary>Blank input means "not provided" — an empty form field shouldn't be an error.</summary>
    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static decimal? ParseDecimal(string? raw, string field, Action<string, string> error)
    {
        if (Normalize(raw) is not { } value) return null;
        if (decimal.TryParse(value, DecimalStyle, CultureInfo.InvariantCulture, out var result)) return result;
        error(field, DecimalPattern().IsMatch(value)
            ? $"{field} is too large (got '{value}')."
            : $"{field} must be a number (got '{value}').");
        return null;
    }

    private static int? ParseInt(string? raw, string field, Action<string, string> error)
    {
        if (Normalize(raw) is not { } value) return null;
        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result)) return result;
        error(field, IntegerPattern().IsMatch(value)
            ? $"{field} is too large (got '{value}')."
            : $"{field} must be a whole number (got '{value}').");
        return null;
    }

    // Well-formed numbers that still failed to parse can only have overflowed.
    [GeneratedRegex(@"^[+-]?(\d+\.?\d*|\.\d+)$")]
    private static partial Regex DecimalPattern();

    [GeneratedRegex(@"^[+-]?\d+$")]
    private static partial Regex IntegerPattern();
}
