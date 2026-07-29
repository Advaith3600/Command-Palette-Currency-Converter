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

    [Fact]
    public void Evaluate_SupportsUnaryMinus()
    {
        decimal result = CalculateEngine.Evaluate("-100 + 20", DotFormatter());
        Assert.Equal(-80m, result);
    }

    [Fact]
    public void Evaluate_SupportsUnaryMinusAfterOperator()
    {
        decimal result = CalculateEngine.Evaluate("10*-5", DotFormatter());
        Assert.Equal(-50m, result);
    }

    [Fact]
    public void Evaluate_SupportsUnaryMinusBeforeParentheses()
    {
        decimal result = CalculateEngine.Evaluate("-(1+2)", DotFormatter());
        Assert.Equal(-3m, result);
    }

    [Fact]
    public void Evaluate_SupportsUnaryMinusBeforeNestedParentheses()
    {
        decimal result = CalculateEngine.Evaluate("-((2+3)*4)", DotFormatter());
        Assert.Equal(-20m, result);
    }

    [Fact]
    public void Evaluate_SupportsUnaryMinusBeforeParenthesesInExpression()
    {
        decimal result = CalculateEngine.Evaluate("10 + -(3+2)", DotFormatter());
        Assert.Equal(5m, result);
    }

    [Fact]
    public void Evaluate_DeDECultureFormatter_ParsesCommaDecimal()
    {
        var nfi = CultureInfo.GetCultureInfo("de-DE").NumberFormat;
        decimal result = CalculateEngine.Evaluate("10,5 + 1,5", nfi);
        Assert.Equal(12m, result);
    }

    [Fact]
    public void Evaluate_DeDECultureFormatter_AfterGroupSeparatorStripped()
    {
        var nfi = CultureInfo.GetCultureInfo("de-DE").NumberFormat;
        // QueryParser strips group separators before calling Evaluate
        string expression = "1.000 + 500".Replace(nfi.CurrencyGroupSeparator, "");
        decimal result = CalculateEngine.Evaluate(expression, nfi);
        Assert.Equal(1500m, result);
    }
}