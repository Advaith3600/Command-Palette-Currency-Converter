using CurrencyConverterExtension.Converter;
using CurrencyConverterExtension.Helpers;
using System.Globalization;

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
    public void Parse_AmountOnly_LeavesCurrenciesEmpty()
    {
        QueryParseResult result = QueryParser.Parse("300", decimalSeparatorMode: 1);

        Assert.Equal(QueryParseStatus.Success, result.Status);
        Assert.Equal(300m, result.Query!.Value.Amount);
        Assert.Equal("", result.Query.Value.FromCurrency);
        Assert.Equal("", result.Query.Value.ToCurrency);
    }

    [Fact]
    public void Parse_MathAmountOnly_EvaluatesExpression()
    {
        QueryParseResult result = QueryParser.Parse("10*30", decimalSeparatorMode: 1);

        Assert.Equal(QueryParseStatus.Success, result.Status);
        Assert.Equal(300m, result.Query!.Value.Amount);
        Assert.Equal("", result.Query.Value.FromCurrency);
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
    public void Parse_UnrelatedText_ReturnsNoMatch()
    {
        QueryParseResult result = QueryParser.Parse("sdkjaf", decimalSeparatorMode: 1);

        Assert.Equal(QueryParseStatus.NoMatch, result.Status);
    }

    [Fact]
    public void Parse_LongDigitRunWithJunk_ReturnsNoMatchQuickly()
    {
        // Formerly catastrophic backtracking: ~9s at 24 digits under the old nested-quantifier pattern.
        string search = new string('1', 40) + "!";
        var sw = System.Diagnostics.Stopwatch.StartNew();
        QueryParseResult result = QueryParser.Parse(search, decimalSeparatorMode: 1);
        sw.Stop();

        Assert.Equal(QueryParseStatus.NoMatch, result.Status);
        Assert.True(sw.ElapsedMilliseconds < 500, $"Parse took {sw.ElapsedMilliseconds}ms");
    }

    [Fact]
    public void Parse_OverMaxLength_ReturnsNoMatch()
    {
        string search = new string('1', 200) + " usd to inr";
        QueryParseResult result = QueryParser.Parse(search, decimalSeparatorMode: 1);
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
        Assert.True(AliasManager.ValidateKeyFormat(currencyToken), "Test precondition failed");

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
        Assert.False(AliasManager.ValidateKeyFormat(currencyToken));

        string search = $"100 {currencyToken} to inr";
        QueryParseResult result = QueryParser.Parse(search, decimalSeparatorMode: 1);
        Assert.Equal(QueryParseStatus.NoMatch, result.Status);
    }

    [Fact]
    public void Parse_SystemDefault_EnUS_ParsesDotDecimal()
    {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");

        QueryParseResult result = QueryParser.Parse("10.5 usd to inr", decimalSeparatorMode: 0);

        Assert.Equal(QueryParseStatus.Success, result.Status);
        Assert.Equal(10.5m, result.Query!.Value.Amount);
    }

    [Fact]
    public void Parse_SystemDefault_DeDE_ParsesCommaDecimal()
    {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");

        QueryParseResult result = QueryParser.Parse("10,5 usd to inr", decimalSeparatorMode: 0);

        Assert.Equal(QueryParseStatus.Success, result.Status);
        Assert.Equal(10.5m, result.Query!.Value.Amount);
    }

    [Fact]
    public void Parse_SystemDefault_DeDE_DotTreatedAsGroupSeparator()
    {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");

        QueryParseResult result = QueryParser.Parse("1.000 usd to inr", decimalSeparatorMode: 0);

        Assert.Equal(QueryParseStatus.Success, result.Status);
        Assert.Equal(1000m, result.Query!.Value.Amount);
    }

    // ---- Expression evaluation (formerly CalculateEngine) ----

    [Theory]
    [InlineData("2 + 3 * 4", 14)]
    [InlineData("(2 + 3) * 4", 20)]
    [InlineData("100 + 20 - 5 * 2", 110)]
    [InlineData("10.5 + 1.5", 12)]
    [InlineData("-100 + 20", -80)]
    [InlineData("10*-5", -50)]
    [InlineData("-(1+2)", -3)]
    [InlineData("-((2+3)*4)", -20)]
    [InlineData("10 + -(3+2)", 5)]
    public void Parse_Arithmetic_Evaluates(string search, decimal expected)
    {
        QueryParseResult result = QueryParser.Parse(search, decimalSeparatorMode: 1);

        Assert.Equal(QueryParseStatus.Success, result.Status);
        Assert.Equal(expected, result.Query!.Value.Amount);
    }

    [Fact]
    public void Parse_CommaDecimalMode_EvaluatesArithmetic()
    {
        QueryParseResult result = QueryParser.Parse("10,5 + 1,5", decimalSeparatorMode: 2);

        Assert.Equal(QueryParseStatus.Success, result.Status);
        Assert.Equal(12m, result.Query!.Value.Amount);
    }

    [Theory]
    [InlineData("(10 + 2 usd")]
    [InlineData("10 + usd")]
    [InlineData("10 +")]
    public void Parse_MalformedArithmetic_ReturnsInvalidExpression(string search)
    {
        Assert.Equal(QueryParseStatus.InvalidExpression, QueryParser.Parse(search, decimalSeparatorMode: 1).Status);
    }

    // ---- Magnitude units ----

    [Theory]
    [InlineData("5k usd to eur", 5_000, "usd", "eur")]
    [InlineData("1.5M jpy", 1_500_000, "jpy", "")]
    [InlineData("2b ¥ in usd", 2_000_000_000, "¥", "usd")]
    [InlineData("5k¥", 5_000, "¥", "")]
    [InlineData("3 thousand usd", 3_000, "usd", "")]
    [InlineData("5 mil usd", 5_000_000, "usd", "")]
    [InlineData("2k+500 usd", 2_500, "usd", "")]
    [InlineData("$1.2bn to eur", 1_200_000_000, "$", "eur")]
    [InlineData("5万円 to usd", 50_000, "円", "usd")]
    [InlineData("3億 jpy", 300_000_000, "jpy", "")]
    [InlineData("1兆円", 1_000_000_000_000, "円", "")]
    [InlineData("5万亿 cny", 5_000_000_000_000, "cny", "")]
    [InlineData("5만원", 50_000, "원", "")]
    [InlineData("5 lakh inr", 500_000, "inr", "")]
    [InlineData("2 Cr inr", 20_000_000, "inr", "")]
    [InlineData("5 करोड़ inr", 50_000_000, "inr", "")]
    [InlineData("5 करोड़ inr", 50_000_000, "inr", "")]
    [InlineData("3 Mio. eur", 3_000_000, "eur", "")]
    [InlineData("4 Mrd eur", 4_000_000_000, "eur", "")]
    [InlineData("3 Mio. € in usd", 3_000_000, "€", "usd")]
    [InlineData("2 crore inr", 20_000_000, "inr", "")]
    [InlineData("2 млн руб", 2_000_000, "руб", "")]
    [InlineData("3 ألف aed", 3_000, "aed", "")]
    public void Parse_MagnitudeUnits_ScaleAmount(string search, decimal amount, string from, string to)
    {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");

        QueryParseResult result = QueryParser.Parse(search, decimalSeparatorMode: 1);

        Assert.Equal(QueryParseStatus.Success, result.Status);
        Assert.Equal(amount, result.Query!.Value.Amount);
        Assert.Equal(from, result.Query.Value.FromCurrency);
        Assert.Equal(to, result.Query.Value.ToCurrency);
    }

    [Theory]
    [InlineData("5mxn", "mxn")]
    [InlineData("5bnd", "bnd")]
    [InlineData("5mnt", "mnt")]
    [InlineData("5crc", "crc")]
    [InlineData("5km", "km")]
    [InlineData("5l", "l")]
    public void Parse_GluedCurrencyCodes_AreNotUnits(string search, string from)
    {
        QueryParseResult result = QueryParser.Parse(search, decimalSeparatorMode: 1);

        Assert.Equal(QueryParseStatus.Success, result.Status);
        Assert.Equal(5m, result.Query!.Value.Amount);
        Assert.Equal(from, result.Query.Value.FromCurrency);
    }

    [Theory]
    [InlineData("100 usd inr", 100, "usd", "inr")]
    [InlineData("usd 100 eur", 100, "usd", "eur")]
    [InlineData("100 + 5 usd", 105, "usd", "")]
    [InlineData("10 / 2 usd", 5, "usd", "")]
    [InlineData("(1 + 2) usd to eur", 3, "usd", "eur")]
    [InlineData("1 000 usd", 1_000, "usd", "")]
    [InlineData("100 to inr", 100, "", "inr")]
    public void Parse_SingleCurrencyQueries_KeepScalarMeaning(string search, decimal amount, string from, string to)
    {
        QueryParseResult result = QueryParser.Parse(search, decimalSeparatorMode: 1);

        Assert.Equal(QueryParseStatus.Success, result.Status);
        Assert.False(result.Query!.Value.IsMixed);
        Assert.Equal(amount, result.Query.Value.Amount);
        Assert.Equal(from, result.Query.Value.FromCurrency);
        Assert.Equal(to, result.Query.Value.ToCurrency);
    }

    [Fact]
    public void Parse_UnitOverflow_ReturnsInvalidExpression()
    {
        QueryParseResult result = QueryParser.Parse("100000000000000000000000兆 usd", decimalSeparatorMode: 1);

        Assert.Equal(QueryParseStatus.InvalidExpression, result.Status);
    }

    [Fact]
    public void Parse_TurkishCulture_MatchesUnitsAndCodesInvariantly()
    {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");

        QueryParseResult units = QueryParser.Parse("5 MIO EUR", decimalSeparatorMode: 1);
        QueryParseResult code = QueryParser.Parse("5 INR", decimalSeparatorMode: 1);

        Assert.Equal(5_000_000m, units.Query!.Value.Amount);
        Assert.Equal("eur", units.Query.Value.FromCurrency);
        Assert.Equal("inr", code.Query!.Value.FromCurrency);
    }

    // ---- Mixed currencies ----

    [Fact]
    public void Parse_MixedCurrencies_ReturnsComponentsAndTarget()
    {
        QueryParseResult result = QueryParser.Parse("200 aed + 20 usd to eur", decimalSeparatorMode: 1);

        Assert.Equal(QueryParseStatus.Success, result.Status);
        ParsedQuery query = result.Query!.Value;
        Assert.True(query.IsMixed);
        Assert.Equal("aed", query.FromCurrency);
        Assert.Equal("eur", query.ToCurrency);
        Assert.Equal([new CurrencyAmount("aed", 200m), new CurrencyAmount("usd", 20m)], query.Components);
    }

    [Theory]
    [InlineData("20 usd - 5 eur", "usd", 20, "eur", -5)]
    [InlineData("2 * 10 usd + 5 eur", "usd", 20, "eur", 5)]
    [InlineData("$100 + €50", "$", 100, "€", 50)]
    [InlineData("(10 usd + 5 eur) * 2", "usd", 20, "eur", 10)]
    [InlineData("10 usd + 5 eur + 5 usd", "usd", 15, "eur", 5)]
    public void Parse_MixedArithmetic_CombinesPerCurrency(string search, string c1, decimal a1, string c2, decimal a2)
    {
        QueryParseResult result = QueryParser.Parse(search, decimalSeparatorMode: 1);

        Assert.Equal(QueryParseStatus.Success, result.Status);
        Assert.Equal([new CurrencyAmount(c1, a1), new CurrencyAmount(c2, a2)], result.Query!.Value.Components);
    }

    [Theory]
    [InlineData("10 usd * 5 eur")]
    [InlineData("10 eur / 2 usd")]
    public void Parse_AmbiguousMixedArithmetic_ReturnsInvalidExpression(string search)
    {
        Assert.Equal(QueryParseStatus.InvalidExpression, QueryParser.Parse(search, decimalSeparatorMode: 1).Status);
    }

    [Theory]
    [InlineData("en-US", "5 mil usd", 5_000_000)]
    [InlineData("es-ES", "5 mil usd", 5_000)]
    [InlineData("en-US", "2 bn eur", 2_000_000_000)]
    [InlineData("fr-FR", "2 Bn eur", 2_000_000_000_000)]
    public void Parse_LanguageDependentUnits_FollowCurrentCulture(string culture, string search, decimal amount)
    {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);

        QueryParseResult result = QueryParser.Parse(search, decimalSeparatorMode: 1);

        Assert.Equal(amount, result.Query!.Value.Amount);
    }

    [Theory]
    [InlineData("200 aed + 5 + 20 usd", "aed", 200, "", 5, "usd", 20)]
    [InlineData("5 + 200 aed + 20 usd", "", 5, "aed", 200, "usd", 20)]
    [InlineData("200 aed + 20 usd - 2 * 3", "aed", 200, "usd", 20, "", -6)]
    public void Parse_MixedWithBareAmount_KeepsItAsLocalCurrencyPart(
        string search, string c1, decimal a1, string c2, decimal a2, string c3, decimal a3)
    {
        QueryParseResult result = QueryParser.Parse(search, decimalSeparatorMode: 1);

        Assert.Equal(QueryParseStatus.Success, result.Status);
        Assert.Equal(
            [new CurrencyAmount(c1, a1), new CurrencyAmount(c2, a2), new CurrencyAmount(c3, a3)],
            result.Query!.Value.Components);
    }

    [Fact]
    public void Parse_MixedWithBracketedSum_TagsTheWholeBracket()
    {
        QueryParseResult result = QueryParser.Parse("200 aed + (5 + 20) usd", decimalSeparatorMode: 1);

        Assert.Equal([new CurrencyAmount("aed", 200m), new CurrencyAmount("usd", 25m)], result.Query!.Value.Components);
    }
}
