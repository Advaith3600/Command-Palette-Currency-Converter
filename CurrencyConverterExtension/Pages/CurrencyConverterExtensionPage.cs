// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Linq;
using System.Globalization;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using CurrencyConverterExtension.Helpers;
using Microsoft.CommandPalette.Extensions;
using CurrencyConverterExtension.Converter;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace CurrencyConverterExtension;

internal sealed partial class CurrencyConverterExtensionPage : DynamicListPage, IDisposable
{
    internal readonly SettingsManager _settings;
    internal readonly CurrencyConverter _converter;

    internal const string GithubReadmeURL = "https://github.com/Advaith3600/Command-Palette-Currency-Converter?tab=readme-ov-file";

    public CurrencyConverterExtensionPage(SettingsManager settings)
    {
        Icon = IconManager.Icon;
        Title = "Currency Converter";
        Name = "Open";

        _settings = settings;
        _converter = new(_settings);
    }

    public override IListItem[] GetItems()
    {
        string query = SearchText;
        if (query.Length == 0)
            return [];

        IsLoading = true;
        try
        {
            _converter.ValidateConversionAPI();
        }
        catch (Exception ex)
        {
            return [
                new ListItem(new OpenUrlCommand(GithubReadmeURL))
                {
                    Title = ex.Message,
                    Subtitle = "Press enter or click to see how to fix this issue",
                    Icon = IconManager.WarningIcon,
                }
            ];
        }

        var results = ParseQuery(query)
            .Where(x => x != null)
            .GroupBy(r => new { r.Title, r.Subtitle })
            .Select(g => g.First());

        IsLoading = false;
        return [.. results];
    }

    private List<ListItem> ParseQuery(string search)
    {
        NumberFormatInfo formatter = GetNumberFormatInfo();
        string decimalSeparator = Regex.Escape(formatter.CurrencyDecimalSeparator);
        string groupSeparator = Regex.Escape(formatter.CurrencyGroupSeparator);

        string amountPattern = $@"(?<amount>(?:\d+|\s+|{decimalSeparator}|{groupSeparator}|[+\-*/()])+)";
        string fromPattern = @"(?<from>[\p{L}\p{Sc}_]*)";
        string toPattern = @"(?<to>[\p{L}\p{Sc}_]*)";

        string pattern = $@"^\s*(?:(?:{amountPattern}\s*{fromPattern})|(?:{fromPattern}\s*{amountPattern}))\s*(?:to|in)?\s*{toPattern}\s*$";
        Match match = Regex.Match(search.Trim(), pattern);

        if (!match.Success)
        {
            return [];
        }

        decimal amountToConvert;
        try
        {
            amountToConvert = CalculateEngine.Evaluate(match.Groups["amount"].Value.Replace(formatter.CurrencyGroupSeparator, ""), GetNumberFormatInfo());
        }
        catch (Exception)
        {
            return [
                new ListItem(new NoOpCommand())
                {
                    Title = "Invalid expression provided",
                    Subtitle = "Please check your mathematical expression",
                    Icon = IconManager.WarningIcon,
                }
            ];
        }

        string fromCurrency = match.Groups["from"].Value.Trim().ToLower();
        string toCurrency = string.IsNullOrEmpty(match.Groups["to"].Value.Trim()) ? "" : match.Groups["to"].Value.Trim().ToLower();

        return _converter.GetConversionResults(amountToConvert, fromCurrency, toCurrency);
    }

    private NumberFormatInfo GetNumberFormatInfo()
    {
        NumberFormatInfo nfi = new();

        switch (_settings.DecimalSeparator)
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

    public override void UpdateSearchText(string oldSearch, string newSearch)
    {
        if (oldSearch != newSearch)
            RaiseItemsChanged();
    }

    public void Dispose()
    {
        _converter.Dispose();
    }
}
