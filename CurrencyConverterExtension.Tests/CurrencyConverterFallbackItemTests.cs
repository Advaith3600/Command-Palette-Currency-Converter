using CurrencyConverterExtension.Commands;

namespace CurrencyConverterExtension.Tests;

public class CurrencyConverterFallbackItemTests
{
    [Fact]
    public void FormatOpenConverterTitle_PutsTrimmedQueryInQuotes()
    {
        string title = CurrencyConverterFallbackItem.FormatOpenConverterTitle("  300 cny  ");

        Assert.Equal("Convert \"300 cny\" with Currency Converter", title);
    }

    [Fact]
    public void ResolveFallbackFailureTitle_WhenSuppressed_UsesOpenConverterTitle()
    {
        string title = CurrencyConverterFallbackItem.ResolveFallbackFailureTitle(
            suppressWarnings: true,
            query: "  300 cny  ",
            errorTitle: "Unable to reach the conversion service");

        Assert.Equal("Convert \"300 cny\" with Currency Converter", title);
        Assert.DoesNotContain("Unable to reach", title, StringComparison.Ordinal);
    }

    [Fact]
    public void ResolveFallbackFailureTitle_WhenNotSuppressed_UsesErrorTitle()
    {
        string title = CurrencyConverterFallbackItem.ResolveFallbackFailureTitle(
            suppressWarnings: false,
            query: "300 cny",
            errorTitle: "Unable to reach the conversion service");

        Assert.Equal("Unable to reach the conversion service", title);
    }

    [Fact]
    public void ResolveFallbackFailureTitle_WhenNotSuppressedAndNoError_UsesGenericMessage()
    {
        string title = CurrencyConverterFallbackItem.ResolveFallbackFailureTitle(
            suppressWarnings: false,
            query: "300 cny",
            errorTitle: null);

        Assert.Equal("Something went wrong while converting currencies", title);
    }
}
