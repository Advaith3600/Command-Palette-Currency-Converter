using CurrencyConverterExtension.Converter;
using CurrencyConverterExtension.Helpers;
using System.Globalization;

namespace CurrencyConverterExtension.Tests;

public class LanguageAndRegionTests
{
    // The shipped defaults these rules must still respect.
    private static AliasManager DefaultAliases() => new(new Dictionary<string, string>
    {
        ["$"] = "usd",
        ["¥"] = "jpy",
        ["kr"] = "sek",
        ["peso"] = "mxn",
        ["pesos"] = "mxn",
        ["dollar"] = "usd",
        ["rupee"] = "inr",
        ["₨"] = "lkr",
        ["₱"] = "php",
    });

    // ---- Regional symbols and words ----

    [Theory]
    [InlineData("$", "MXN", "mxn")]
    [InlineData("$", "CAD", "cad")]
    [InlineData("$", "ARS", "ars")]
    [InlineData("$", "USD", "usd")]
    [InlineData("$", "INR", "usd")]
    [InlineData("dollar", "AUD", "aud")]
    [InlineData("dollar", "MXN", "usd")]
    [InlineData("pesos", "PHP", "php")]
    [InlineData("pesos", "INR", "mxn")]
    [InlineData("¥", "CNY", "cny")]
    [InlineData("¥", "USD", "jpy")]
    [InlineData("元", "TWD", "twd")]
    [InlineData("元", "USD", "cny")]
    [InlineData("kr", "NOK", "nok")]
    [InlineData("rs", "PKR", "pkr")]
    [InlineData("rs", "USD", "inr")]
    [InlineData("rupees", "NPR", "npr")]
    [InlineData("pesos", "₱", "php")]
    public void Resolve_SharedSymbols_FollowLocalCurrency(string typed, string local, string expected)
    {
        Assert.Equal(expected, CurrencyNames.Resolve(typed, DefaultAliases(), local));
    }

    [Fact]
    public void Resolve_CustomisedSharedSymbol_KeepsUserAlias()
    {
        AliasManager aliases = new(new Dictionary<string, string> { ["$"] = "eur" });

        Assert.Equal("eur", CurrencyNames.Resolve("$", aliases, "MXN"));
    }

    // ---- Built-in words ----

    [Theory]
    [InlineData("рублей", "rub")]
    [InlineData("Руб", "rub")]
    [InlineData("долларов", "usd")]
    [InlineData("евро", "eur")]
    [InlineData("грн", "uah")]
    [InlineData("тенге", "kzt")]
    [InlineData("dólares", "usd")]
    [InlineData("reais", "brl")]
    [InlineData("piso", "php")]
    [InlineData("Franken", "chf")]
    [InlineData("रुपये", "inr")]
    [InlineData("人民币", "cny")]
    [InlineData("美元", "usd")]
    [InlineData("新台幣", "twd")]
    [InlineData("ドル", "usd")]
    [InlineData("人民元", "cny")]
    [InlineData("원", "krw")]
    [InlineData("달러", "usd")]
    [InlineData("usd", "usd")]
    [InlineData("xyz", "xyz")]
    public void Resolve_BuiltInWords(string typed, string expected)
    {
        Assert.Equal(expected, CurrencyNames.Resolve(typed, DefaultAliases(), "USD"));
    }

    [Theory]
    [InlineData("달러를", "usd")]
    [InlineData("원으로", "krw")]
    [InlineData("원화로", "krw")]
    [InlineData("유로", "eur")]
    [InlineData("유로를", "eur")]
    [InlineData("엔화에서", "jpy")]
    public void Resolve_KoreanParticles_AreStripped(string typed, string expected)
    {
        Assert.Equal(expected, CurrencyNames.Resolve(typed, DefaultAliases(), "KRW"));
    }

    [Fact]
    public void Resolve_UserAlias_BeatsBuiltInWord()
    {
        AliasManager aliases = new(new Dictionary<string, string> { ["евро"] = "usd" });

        Assert.Equal("usd", CurrencyNames.Resolve("евро", aliases, "RUB"));
    }

    // ---- Words for "to" ----

    [Theory]
    [InlineData("100 usd в rub", "usd", "rub")]
    [InlineData("100 долларов в рублях", "долларов", "рублях")]
    [InlineData("100 usd во eur", "usd", "eur")]
    [InlineData("100 usd a mxn", "usd", "mxn")]
    [InlineData("100 dólares en pesos", "dólares", "pesos")]
    [InlineData("50 reais em dólares", "reais", "dólares")]
    [InlineData("100 usd para brl", "usd", "brl")]
    [InlineData("100 usd nach eur", "usd", "eur")]
    [InlineData("100 usd → eur", "usd", "eur")]
    [InlineData("100 usd -> eur", "usd", "eur")]
    [InlineData("100 usd => eur", "usd", "eur")]
    [InlineData("100usd→eur", "usd", "eur")]
    public void Parse_ToWords_InManyLanguages(string search, string from, string to)
    {
        QueryParseResult result = QueryParser.Parse(search, decimalSeparatorMode: 1);

        Assert.Equal(QueryParseStatus.Success, result.Status);
        Assert.Equal(from, result.Query!.Value.FromCurrency);
        Assert.Equal(to, result.Query.Value.ToCurrency);
    }

    // ---- Chinese, Japanese and Korean ----

    [Theory]
    [InlineData("100美元换人民币", "100", "美元", "人民币")]
    [InlineData("100美元兑换人民币", "100", "美元", "人民币")]
    [InlineData("100美元换成人民币", "100", "美元", "人民币")]
    [InlineData("100美元是多少人民币", "100", "美元", "人民币")]
    [InlineData("100美元等于多少人民币", "100", "美元", "人民币")]
    [InlineData("把100美元换成人民币", "100", "美元", "人民币")]
    [InlineData("100美元", "100", "美元", "")]
    [InlineData("100块", "100", "块", "")]
    [InlineData("5百万日元", "5000000", "日元", "")]
    [InlineData("100ドルを円に", "100", "ドル", "円")]
    [InlineData("100ドルは何円", "100", "ドル", "円")]
    [InlineData("100ドルは何円ですか", "100", "ドル", "円")]
    [InlineData("１００ドルを円に", "100", "ドル", "円")]
    [InlineData("10万円", "100000", "円", "")]
    [InlineData("100달러를 원으로", "100", "달러를", "원으로")]
    [InlineData("10만원", "100000", "원", "")]
    [InlineData("1억원 달러", "100000000", "원", "달러")]
    [InlineData("3백만원", "3000000", "원", "")]
    public void Parse_CjkQueries(string search, string amount, string from, string to)
    {
        QueryParseResult result = QueryParser.Parse(search, decimalSeparatorMode: 1);

        Assert.True(result.Status == QueryParseStatus.Success, $"{search}: {result.Status} {result.Problem}");
        Assert.Equal(decimal.Parse(amount, CultureInfo.InvariantCulture), result.Query!.Value.Amount);
        Assert.Equal(from, result.Query.Value.FromCurrency);
        Assert.Equal(to, result.Query.Value.ToCurrency);
    }

    // ---- Abbreviation dots ----

    [Theory]
    [InlineData("Rs. 500", "rs")]
    [InlineData("Rs.500", "rs")]
    [InlineData("500 руб.", "руб")]
    [InlineData("500 usd.", "usd")]
    public void Parse_AbbreviationDot_AfterCurrency(string search, string from)
    {
        QueryParseResult result = QueryParser.Parse(search, decimalSeparatorMode: 1);

        Assert.Equal(QueryParseStatus.Success, result.Status);
        Assert.Equal(500m, result.Query!.Value.Amount);
        Assert.Equal(from, result.Query.Value.FromCurrency);
    }

    [Fact]
    public void Parse_DotBetweenWords_IsNotAQuery()
    {
        Assert.Equal(QueryParseStatus.NoMatch, QueryParser.Parse("google.com 5", decimalSeparatorMode: 1).Status);
    }

    // ---- Units ----

    [Theory]
    [InlineData("pt-BR", "5 mil reais", "5000")]
    [InlineData("es-MX", "5 mil pesos", "5000")]
    [InlineData("en-US", "5 mil usd", "5000000")]
    [InlineData("es-MX", "5 mil millones", "5000000000")]
    [InlineData("pt-BR", "2 milhões", "2000000")]
    [InlineData("pt-BR", "3 bilhões", "3000000000")]
    [InlineData("it-IT", "5 mila euro", "5000")]
    [InlineData("it-IT", "2 milioni", "2000000")]
    [InlineData("de-DE", "1,5 Milliarden", "1500000000")]
    [InlineData("de-DE", "3 Tausend", "3000")]
    [InlineData("ru-RU", "5к руб", "5000")]
    [InlineData("ru-RU", "3 миллиона", "3000000")]
    [InlineData("ru-RU", "2 ляма", "2000000")]
    [InlineData("ru-RU", "10 тысяч рублей", "10000")]
    [InlineData("ru-RU", "1,5 миллиарда", "1500000000")]
    public void Parse_LongUnitWords(string region, string search, string expected)
    {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(region);

        QueryParseResult result = QueryParser.Parse(search, decimalSeparatorMode: 0);

        Assert.True(result.Status == QueryParseStatus.Success, $"{search}: {result.Status} {result.Problem}");
        Assert.Equal(decimal.Parse(expected, CultureInfo.InvariantCulture), result.Query!.Value.Amount);
    }

    // ---- Russian keyboard slips ----

    [Theory]
    [InlineData("12ю5 usd", "12.5", "Read \"12ю5\" as 12.5")]
    [InlineData("12б5 usd", "12.5", "Read \"12б5\" as 12.5")]
    [InlineData("1 234ю50", "1234.5", "Read \"1 234ю50\" as 1,234.5")]
    public void Parse_RussianLayoutSeparators_AreLabelled(string search, string expected, string note)
    {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");

        QueryParseResult result = QueryParser.Parse(search, decimalSeparatorMode: 0);

        Assert.Equal(decimal.Parse(expected, CultureInfo.InvariantCulture), result.Query!.Value.Amount);
        Assert.Equal(note, result.Note);
    }

    [Theory]
    [InlineData("100 гыв", "usd", "", "Read \"гыв\" as USD")]
    [InlineData("100 гыв в кги", "usd", "rub", "Read \"гыв\" as USD; Read \"кги\" as RUB")]
    [InlineData("100 угк", "eur", "", "Read \"угк\" as EUR")]
    [InlineData("100 ГЫВ", "usd", "", "Read \"гыв\" as USD")]
    public void Parse_WrongLayoutCodes_AreLabelled(string search, string from, string to, string note)
    {
        QueryParseResult result = QueryParser.Parse(search, decimalSeparatorMode: 1);

        Assert.Equal(QueryParseStatus.Success, result.Status);
        Assert.Equal(from, result.Query!.Value.FromCurrency);
        Assert.Equal(to, result.Query.Value.ToCurrency);
        Assert.Equal(note, result.Note);
    }

    [Theory]
    [InlineData("100 руб", "руб")]
    [InlineData("100 грн", "грн")]
    [InlineData("5 тыс", "")]
    public void Parse_RealRussianWords_AreNotRetyped(string search, string from)
    {
        QueryParseResult result = QueryParser.Parse(search, decimalSeparatorMode: 1);

        Assert.Equal(QueryParseStatus.Success, result.Status);
        Assert.Equal(from, result.Query!.Value.FromCurrency);
        Assert.Null(result.Note);
    }

    // ---- Numerals written in CJK characters ----

    [Theory]
    [InlineData("三千五百", "3500", "")]
    [InlineData("一百二十三", "123", "")]
    [InlineData("十二", "12", "")]
    [InlineData("二十", "20", "")]
    [InlineData("两千零五", "2005", "")]
    [InlineData("一万", "10000", "")]
    [InlineData("十万", "100000", "")]
    [InlineData("一亿五千万", "150000000", "")]
    [InlineData("一万亿", "1000000000000", "")]
    [InlineData("三点五", "3.5", "")]
    [InlineData("三点五万", "35000", "")]
    [InlineData("二〇二四", "2024", "")]
    [InlineData("壹仟贰佰叁拾肆", "1234", "")]
    [InlineData("貳萬", "20000", "")]
    [InlineData("壱万円", "10000", "円")]
    [InlineData("三千円", "3000", "円")]
    [InlineData("五百美元", "500", "美元")]
    [InlineData("五百美元换人民币", "500", "美元")]
    [InlineData("美元三千", "3000", "美元")]
    [InlineData("三千 usd", "3000", "usd")]
    [InlineData("삼만원", "30000", "원")]
    [InlineData("만원", "10000", "원")]
    [InlineData("오원", "5", "원")]
    [InlineData("이백 달러", "200", "달러")]
    [InlineData("삼만원을", "30000", "원을")]
    [InlineData("일억 오천만원", "150000000", "원")]
    public void Parse_CjkNumerals(string search, string amount, string from)
    {
        QueryParseResult result = QueryParser.Parse(search, decimalSeparatorMode: 1);

        Assert.True(result.Status == QueryParseStatus.Success, $"{search}: {result.Status} {result.Problem}");
        Assert.Equal(decimal.Parse(amount, CultureInfo.InvariantCulture), result.Query!.Value.Amount);
        Assert.Equal(from, result.Query.Value.FromCurrency);
    }

    [Theory]
    [InlineData("3万5千", "35000")]
    [InlineData("1万5000円", "15000")]
    [InlineData("1万500円", "10500")]
    [InlineData("1억 5천만원", "150000000")]
    [InlineData("1억 5000만원", "150000000")]
    [InlineData("3千5百", "3500")]
    [InlineData("1.5万", "15000")]
    [InlineData("5万亿", "5000000000000")]
    public void Parse_LatinDigitsWithCjkUnits_AddUp(string search, string amount)
    {
        QueryParseResult result = QueryParser.Parse(search, decimalSeparatorMode: 1);

        Assert.True(result.Status == QueryParseStatus.Success, $"{search}: {result.Status} {result.Problem}");
        Assert.Equal(decimal.Parse(amount, CultureInfo.InvariantCulture), result.Query!.Value.Amount);
        Assert.Null(result.Note);
    }

    [Theory]
    [InlineData("一万五", "15000")]
    [InlineData("两万三", "23000")]
    [InlineData("三千五", "3500")]
    [InlineData("一百二", "120")]
    public void Parse_ColloquialTrailingDigit_IsScaledAndLabelled(string search, string amount)
    {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");

        QueryParseResult result = QueryParser.Parse(search, decimalSeparatorMode: 1);

        Assert.Equal(decimal.Parse(amount, CultureInfo.InvariantCulture), result.Query!.Value.Amount);
        Assert.StartsWith($"Read \"{search}\" as ", result.Note);
    }

    [Theory]
    [InlineData("三星")]
    [InlineData("一起")]
    [InlineData("一緒に")]
    [InlineData("千葉")]
    [InlineData("参加")]
    [InlineData("일본")]
    [InlineData("이번")]
    [InlineData("구글")]
    [InlineData("사과")]
    public void Parse_WordsStartingWithNumerals_AreNotNumbers(string search)
    {
        Assert.Equal(QueryParseStatus.NoMatch, QueryParser.Parse(search, decimalSeparatorMode: 1).Status);
    }

    [Fact]
    public void Parse_SpaceAfterBigUnit_NeedsAUnitToContinue()
    {
        QueryParseResult result = QueryParser.Parse("100万 5 usd", decimalSeparatorMode: 1);

        Assert.NotEqual(1_000_005m, result.Query?.Amount);
    }

    // ---- Other digit scripts (decimal digits Windows already knows) ----

    [Theory]
    [InlineData("๑๒๓ thb", "123")]      // Thai
    [InlineData("১২৩ bdt", "123")]      // Bengali
    [InlineData("௧௨௩ inr", "123")]      // Tamil
    [InlineData("૧૨૩ inr", "123")]      // Gujarati
    [InlineData("១២៣ khr", "123")]      // Khmer
    [InlineData("၁၂၃ mmk", "123")]      // Myanmar
    [InlineData("໑໒໓ lak", "123")]      // Lao
    [InlineData("੧੨੩ inr", "123")]      // Gurmukhi
    public void Parse_OtherDigitScripts(string search, string amount)
    {
        QueryParseResult result = QueryParser.Parse(search, decimalSeparatorMode: 1);

        Assert.Equal(QueryParseStatus.Success, result.Status);
        Assert.Equal(decimal.Parse(amount, CultureInfo.InvariantCulture), result.Query!.Value.Amount);
    }
}
