using CurrencyConverterExtension.Helpers;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using System;

namespace CurrencyConverterExtension.Tests;

public class PinnedDockBandRefreshTests
{
    [Fact]
    public void MergeDockBandItems_EmptyCurrent_ReturnsIncoming()
    {
        IListItem[] incoming =
        [
            CreateSuccess("pin-a", "1 USD"),
            CreatePlaceholder("pin-b"),
        ];

        IListItem[] merged = PinnedDockBandManager.MergeDockBandItems(null, incoming);

        Assert.Same(incoming, merged);
    }

    [Fact]
    public void MergeDockBandItems_PrefersNewSuccessOverPrevious()
    {
        IListItem[] current = [CreateSuccess("pin-a", "old")];
        IListItem[] incoming = [CreateSuccess("pin-a", "new")];

        IListItem[] merged = PinnedDockBandManager.MergeDockBandItems(current, incoming);

        Assert.Same(incoming[0], merged[0]);
        Assert.Equal("new", merged[0].Title);
    }

    [Fact]
    public void MergeDockBandItems_KeepsPreviousSuccessWhenIncomingIsPlaceholder()
    {
        IListItem previous = CreateSuccess("pin-a", "1.00 EUR");
        IListItem[] current = [previous];
        IListItem[] incoming = [CreatePlaceholder("pin-a")];

        IListItem[] merged = PinnedDockBandManager.MergeDockBandItems(current, incoming);

        Assert.Same(previous, merged[0]);
        Assert.Equal("1.00 EUR", merged[0].Title);
    }

    [Fact]
    public void MergeDockBandItems_MixedSuccessAndFailure_MergesPerPin()
    {
        IListItem previousA = CreateSuccess("pin-a", "old-a");
        IListItem previousB = CreateSuccess("pin-b", "old-b");
        IListItem[] current = [previousA, previousB];

        IListItem newA = CreateSuccess("pin-a", "new-a");
        IListItem failedB = CreatePlaceholder("pin-b");
        IListItem[] incoming = [newA, failedB];

        IListItem[] merged = PinnedDockBandManager.MergeDockBandItems(current, incoming);

        Assert.Same(newA, merged[0]);
        Assert.Same(previousB, merged[1]);
    }

    [Fact]
    public void MergeDockBandItems_NewPinWithNoPrevious_UsesPlaceholder()
    {
        IListItem[] current = [CreateSuccess("pin-a", "1 USD")];
        IListItem placeholder = CreatePlaceholder("pin-c");
        IListItem[] incoming = [CreateSuccess("pin-a", "2 USD"), placeholder];

        IListItem[] merged = PinnedDockBandManager.MergeDockBandItems(current, incoming);

        Assert.Equal(2, merged.Length);
        Assert.Equal("2 USD", merged[0].Title);
        Assert.Same(placeholder, merged[1]);
    }

    [Fact]
    public void IsSuccessfulConversionItem_CopyTextCommand_ReturnsTrue()
    {
        Assert.True(PinnedDockBandManager.IsSuccessfulConversionItem(CreateSuccess("pin-a", "1")));
    }

    [Fact]
    public void IsSuccessfulConversionItem_Placeholder_ReturnsFalse()
    {
        Assert.False(PinnedDockBandManager.IsSuccessfulConversionItem(CreatePlaceholder("pin-a")));
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

    private static ListItem CreateSuccess(string commandId, string title)
    {
        CopyTextCommand command = new(title) { Id = commandId };
        return new ListItem(command) { Title = title };
    }

    private static ListItem CreatePlaceholder(string commandId) =>
        new(new NoOpCommand { Id = commandId })
        {
            Title = "placeholder",
        };
}
