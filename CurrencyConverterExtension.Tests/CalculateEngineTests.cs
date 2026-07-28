using System.Globalization;
using CurrencyConverterExtension.Converter;

namespace CurrencyConverterExtension.Tests;

public class CalculateEngineTests
{
    private static NumberFormatInfo DotFormatter()
    {
        return new NumberFormatInfo
        {
            CurrencyDecimalSeparator = ".",
            CurrencyGroupSeparator = ",",
        };
    }

    private static NumberFormatInfo CommaFormatter()
    {
        return new NumberFormatInfo
        {
            CurrencyDecimalSeparator = ",",
            CurrencyGroupSeparator = ".",
        };
    }

    [Fact]
    public void Evaluate_RespectsMultiplicationPrecedence()
    {
        decimal result = CalculateEngine.Evaluate("2 + 3 * 4", DotFormatter());
        Assert.Equal(14m, result);
    }

    [Fact]
    public void Evaluate_RespectsParentheses()
    {
        decimal result = CalculateEngine.Evaluate("(2 + 3) * 4", DotFormatter());
        Assert.Equal(20m, result);
    }

    [Fact]
    public void Evaluate_SupportsChainedOperations()
    {
        decimal result = CalculateEngine.Evaluate("100 + 20 - 5 * 2", DotFormatter());
        Assert.Equal(110m, result);
    }

    [Fact]
    public void Evaluate_ParsesDotDecimalSeparator()
    {
        decimal result = CalculateEngine.Evaluate("10.5 + 1.5", DotFormatter());
        Assert.Equal(12m, result);
    }

    [Fact]
    public void Evaluate_ParsesCommaDecimalSeparator()
    {
        decimal result = CalculateEngine.Evaluate("10,5 + 1,5", CommaFormatter());
        Assert.Equal(12m, result);
    }

    [Fact]
    public void Evaluate_ThrowsOnDivideByZero()
    {
        Assert.Throws<DivideByZeroException>(() => CalculateEngine.Evaluate("10 / 0", DotFormatter()));
    }

    [Fact]
    public void Evaluate_ThrowsOnUnbalancedParenthesis()
    {
        Assert.ThrowsAny<Exception>(() => CalculateEngine.Evaluate("(10 + 2", DotFormatter()));
    }
}