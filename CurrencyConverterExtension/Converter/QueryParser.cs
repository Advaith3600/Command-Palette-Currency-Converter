using CurrencyConverterExtension.Helpers;
using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace CurrencyConverterExtension.Converter;

internal enum QueryParseStatus
{
    NoMatch,
    InvalidExpression,
    Success,
}

internal readonly record struct ParsedQuery(decimal Amount, string FromCurrency, string ToCurrency);

internal readonly record struct QueryParseResult(QueryParseStatus Status, ParsedQuery? Query)
{
    public static QueryParseResult NoMatch() => new(QueryParseStatus.NoMatch, null);
    public static QueryParseResult InvalidExpression() => new(QueryParseStatus.InvalidExpression, null);
    public static QueryParseResult Success(ParsedQuery query) => new(QueryParseStatus.Success, query);
}

internal static class QueryParser
{
    public static QueryParseResult Parse(string search, int decimalSeparatorMode)
    {
        NumberFormatInfo formatter = GetNumberFormatInfo(decimalSeparatorMode);
        string decimalSeparator = Regex.Escape(formatter.CurrencyDecimalSeparator);
        string groupSeparator = Regex.Escape(formatter.CurrencyGroupSeparator);

        string amountPattern = $@"(?<amount>(?:\d+|\s+|{decimalSeparator}|{groupSeparator}|[+\-*/()])+)";
        string fromPattern = $@"(?<from>{AliasManager.KeyRegex})";
        string toPattern = $@"(?<to>{AliasManager.KeyRegex})";

        string pattern = $@"^\s*(?:(?:{amountPattern}\s*{fromPattern})|(?:{fromPattern}\s*{amountPattern}))\s*(?:to|in)?\s*{toPattern}\s*$";
        Match match = Regex.Match(search.Trim(), pattern);

        if (!match.Success)
        {
            return QueryParseResult.NoMatch();
        }

        decimal amountToConvert;
        try
        {
            amountToConvert = CalculateEngine.Evaluate(
                match.Groups["amount"].Value.Replace(formatter.CurrencyGroupSeparator, ""),
                GetNumberFormatInfo(decimalSeparatorMode));
        }
        catch (Exception)
        {
            return QueryParseResult.InvalidExpression();
        }

        string fromCurrency = match.Groups["from"].Value.Trim().ToLowerInvariant();
        string toCurrency = string.IsNullOrEmpty(match.Groups["to"].Value.Trim())
            ? ""
            : match.Groups["to"].Value.Trim().ToLowerInvariant();

        return QueryParseResult.Success(new ParsedQuery(amountToConvert, fromCurrency, toCurrency));
    }

    internal static NumberFormatInfo GetNumberFormatInfo(int decimalSeparatorMode)
    {
        NumberFormatInfo nfi = new();

        switch (decimalSeparatorMode)
        {
            case 1:
                nfi.CurrencyDecimalSeparator = ".";
                nfi.CurrencyGroupSeparator = ",";
                break;
            case 2:
                nfi.CurrencyDecimalSeparator = ",";
                nfi.CurrencyGroupSeparator = ".";
                break;
            default:
                nfi = CultureInfo.CurrentCulture.NumberFormat;
                break;
        }

        return nfi;
    }
}