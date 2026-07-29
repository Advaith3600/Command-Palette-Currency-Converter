using CurrencyConverterExtension.Converter;
using CurrencyConverterExtension.Helpers;

namespace CurrencyConverterExtension.Tests;

public class QueryParserTests
{
    [Theory]
    [InlineData("100 usd to inr", 100, "usd", "inr")]
    [InlineData("usd 100 in eur", 100, "usd", "eur")]
    [InlineData("$100 to eur", 100, "$", "eur")]
    [InlineData("100\u20AC", 100, "\u20AC", "")]
    [InlineData("\u20BD100", 100, "\u20BD", "")]
    public void Parse_Success_ExtractsAmountAndCurrencies(string search, decimal amount, string from, string to)
    {
        QueryParseResult result = QueryParser.Parse(search, decimalSeparatorMode: 1);

        Assert.Equal(QueryParseStatus.Success, result.Status);
        Assert.NotNull(result.Query);
        Assert.Equal(amount, result.Query.Value.Amount);
        Assert.Equal(from, result.Query.Value.FromCurrency);
        Assert.Equal(to, result.Query.Value.ToCurrency);
    }

    [Fact]
    public void Parse_AmountOnlyWithTarget_LeavesToEmptyWhenNoTarget()
    {
        QueryParseResult result = QueryParser.Parse("100 usd", decimalSeparatorMode: 1);

        Assert.Equal(QueryParseStatus.Success, result.Status);
        Assert.Equal(100m, result.Query!.Value.Amount);
        Assert.Equal("usd", result.Query.Value.FromCurrency);
        Assert.Equal("", result.Query.Value.ToCurrency);
    }

    [Fact]
    public void Parse_MathInAmount_EvaluatesExpression()
    {
        QueryParseResult result = QueryParser.Parse("100+20 usd to inr", decimalSeparatorMode: 1);

        Assert.Equal(QueryParseStatus.Success, result.Status);
        Assert.Equal(120m, result.Query!.Value.Amount);
        Assert.Equal("usd", result.Query.Value.FromCurrency);
        Assert.Equal("inr", result.Query.Value.ToCurrency);
    }

    [Fact]
    public void Parse_InvalidExpression_ReturnsInvalidExpression()
    {
        QueryParseResult result = QueryParser.Parse("10 / 0 usd to inr", decimalSeparatorMode: 1);

        Assert.Equal(QueryParseStatus.InvalidExpression, result.Status);
    }

    [Fact]
    public void Parse_TrailingJunk_ReturnsNoMatch()
    {
        QueryParseResult result = QueryParser.Parse("100 usd to inr extra", decimalSeparatorMode: 1);

        Assert.Equal(QueryParseStatus.NoMatch, result.Status);
    }

    [Fact]
    public void Parse_DotDecimalMode_ParsesFractionalAmount()
    {
        QueryParseResult result = QueryParser.Parse("10.5 usd to inr", decimalSeparatorMode: 1);

        Assert.Equal(QueryParseStatus.Success, result.Status);
        Assert.Equal(10.5m, result.Query!.Value.Amount);
    }

    [Fact]
    public void Parse_CommaDecimalMode_ParsesFractionalAmount()
    {
        QueryParseResult result = QueryParser.Parse("10,5 usd to inr", decimalSeparatorMode: 2);

        Assert.Equal(QueryParseStatus.Success, result.Status);
        Assert.Equal(10.5m, result.Query!.Value.Amount);
    }

    [Theory]
    [InlineData("usd")]
    [InlineData("my_alias")]
    [InlineData("$")]
    [InlineData("\u20AC")]
    public void Parse_AgreementWithAliasValidation_ForValidKeys(string currencyToken)
    {
        var aliasManager = new AliasManager();
        Assert.True(aliasManager.ValidateKeyFormat(currencyToken), "Test precondition failed");

        // Keep formatting simple: integer amount + explicit "to" clause.
        string search = currencyToken == "\u20AC"
            ? $"100{currencyToken} to inr"
            : $"100 {currencyToken} to inr";

        QueryParseResult result = QueryParser.Parse(search, decimalSeparatorMode: 1);
        Assert.Equal(QueryParseStatus.Success, result.Status);
        Assert.Equal("inr", result.Query!.Value.ToCurrency);
        Assert.Equal(currencyToken.ToLowerInvariant(), result.Query.Value.FromCurrency);
    }

    [Theory]
    [InlineData("usd123")]
    [InlineData("!!!")]
    public void Parse_AgreementWithAliasValidation_ForInvalidKeys_ReturnsNoMatch(string currencyToken)
    {
        var aliasManager = new AliasManager();
        Assert.False(aliasManager.ValidateKeyFormat(currencyToken));

        string search = $"100 {currencyToken} to inr";
        QueryParseResult result = QueryParser.Parse(search, decimalSeparatorMode: 1);
        Assert.Equal(QueryParseStatus.NoMatch, result.Status);
    }
}