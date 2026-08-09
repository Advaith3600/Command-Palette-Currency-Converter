using CurrencyConverterExtension.Helpers;
using System;

namespace CurrencyConverterExtension.Tests;

public class PinnedDockBandRefreshTests
{
    [Fact]
    public void ShouldKeepPreviousItems_FailedRefreshWithGoodCurrent_ReturnsTrue()
    {
        Assert.True(PinnedDockBandManager.ShouldKeepPreviousItems(
            allSucceeded: false,
            currentHasSuccessfulItems: true));
    }

    [Fact]
    public void ShouldKeepPreviousItems_SuccessfulRefresh_ReturnsFalse()
    {
        Assert.False(PinnedDockBandManager.ShouldKeepPreviousItems(
            allSucceeded: true,
            currentHasSuccessfulItems: true));
    }

    [Fact]
    public void ShouldKeepPreviousItems_FailedRefreshWithEmptyCurrent_ReturnsFalse()
    {
        Assert.False(PinnedDockBandManager.ShouldKeepPreviousItems(
            allSucceeded: false,
            currentHasSuccessfulItems: false));
    }

    [Theory]
    [InlineData(1, 5)]
    [InlineData(2, 15)]
    [InlineData(3, 30)]
    [InlineData(4, 60)]
    [InlineData(5, 120)]
    [InlineData(6, 300)]
    [InlineData(7, 300)]
    [InlineData(0, 5)]
    [InlineData(-1, 5)]
    public void NextRetryDelay_ReturnsExpectedSeconds(int attempt, int expectedSeconds)
    {
        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), PinnedDockBandManager.NextRetryDelay(attempt));
    }
}
