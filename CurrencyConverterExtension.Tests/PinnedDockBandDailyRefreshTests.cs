using CurrencyConverterExtension.Helpers;
using System;

namespace CurrencyConverterExtension.Tests;

public class PinnedDockBandDailyRefreshTests
{
    [Fact]
    public void NeedsDailyRefresh_NullLastDay_ReturnsTrue()
    {
        DateOnly today = new(2026, 7, 31);
        Assert.True(PinnedDockBandManager.NeedsDailyRefresh(null, today));
    }

    [Fact]
    public void NeedsDailyRefresh_SameDay_ReturnsFalse()
    {
        DateOnly today = new(2026, 7, 31);
        Assert.False(PinnedDockBandManager.NeedsDailyRefresh(today, today));
    }

    [Fact]
    public void NeedsDailyRefresh_DifferentDay_ReturnsTrue()
    {
        DateOnly yesterday = new(2026, 7, 30);
        DateOnly today = new(2026, 7, 31);
        Assert.True(PinnedDockBandManager.NeedsDailyRefresh(yesterday, today));
    }

    [Fact]
    public void NeedsDailyRefresh_FutureStampVsToday_ReturnsTrue()
    {
        // Clock/TZ moved backward: stamped day is "ahead" of local today.
        DateOnly stamped = new(2026, 8, 1);
        DateOnly today = new(2026, 7, 31);
        Assert.True(PinnedDockBandManager.NeedsDailyRefresh(stamped, today));
    }
}
