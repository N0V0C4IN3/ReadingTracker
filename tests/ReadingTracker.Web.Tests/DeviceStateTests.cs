using ReadingTracker.Web.Services;

namespace ReadingTracker.Web.Tests;

public class DeviceStateTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 20, 21, 0, 0, TimeSpan.Zero);

    private static Device DeviceUsed(DateTimeOffset? lastUsedAt) =>
        new(Guid.NewGuid(), "Kindle", Now.AddDays(-30), lastUsedAt);

    [Fact]
    public void A_device_never_used_says_so()
    {
        var state = DeviceState.Of(DeviceUsed(null), Now);

        Assert.Equal(DeviceStateKind.NeverUsed, state.Kind);
        Assert.Equal("Never used", state.Label);
    }

    [Fact]
    public void A_device_used_within_the_day_is_syncing()
    {
        var state = DeviceState.Of(DeviceUsed(Now.AddHours(-23)), Now);

        Assert.Equal(DeviceStateKind.Syncing, state.Kind);
        Assert.Equal("Syncing", state.Label);
    }

    [Theory]
    [InlineData(25, "Quiet 1 day")]
    [InlineData(9 * 24 + 6, "Quiet 9 days")]
    [InlineData(60 * 24, "Quiet 60 days")]
    public void A_device_not_used_for_a_day_or_more_is_quiet_for_that_many_days(int hoursAgo, string label)
    {
        var state = DeviceState.Of(DeviceUsed(Now.AddHours(-hoursAgo)), Now);

        Assert.Equal(DeviceStateKind.Quiet, state.Kind);
        Assert.Equal(label, state.Label);
    }

    [Fact]
    public void A_clock_slightly_ahead_of_the_gateway_still_counts_as_syncing()
    {
        var state = DeviceState.Of(DeviceUsed(Now.AddMinutes(2)), Now);

        Assert.Equal(DeviceStateKind.Syncing, state.Kind);
    }
}
