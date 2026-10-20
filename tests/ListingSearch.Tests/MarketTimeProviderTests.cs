using ListingSearch.Api.Search;

namespace ListingSearch.Tests;

public class MarketTimeProviderTests
{
    [Fact]
    public void LocalTime_IsInTheMarketTimeZone()
    {
        var newYork = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
        var clock = new MarketTimeProvider(newYork);

        Assert.Same(newYork, clock.LocalTimeZone);
        Assert.Equal(newYork.GetUtcOffset(clock.GetUtcNow()), clock.GetLocalNow().Offset);
    }
}
