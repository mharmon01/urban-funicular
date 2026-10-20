namespace ListingSearch.Api.Search;

/// <summary>
/// System clock whose "local" time zone is the listings' market rather than the server's, so
/// "today" for recency matches the calendar the listedDate values were written in — regardless of
/// whether the server runs in UTC or somewhere else.
/// </summary>
public sealed class MarketTimeProvider(TimeZoneInfo marketTimeZone) : TimeProvider
{
    public override TimeZoneInfo LocalTimeZone => marketTimeZone;
}
