// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Linq;
using System.Threading;
using System.Globalization;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using CurrencyConverterExtension.Helpers;
using Microsoft.CommandPalette.Extensions;
using CurrencyConverterExtension.Converter;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace CurrencyConverterExtension;

internal sealed partial class CurrencyConverterExtensionPage : DynamicListPage, IDisposable
{
    private string _searchText = "";
    internal readonly SettingsManager _settings;
    internal readonly CurrencyConverter _converter;
    
    internal const string GithubReadmeURL = "https://github.com/Advaith3600/Command-Palette-Currency-Converter?tab=readme-ov-file";

    public CurrencyConverterExtensionPage(SettingsManager settings, AliasManager aliasManager)
    {
        Icon = IconManager.Icon;
        Title = "Currency Converter";
        Name = "Convert";

        _settings = settings;
        _converter = new(_settings, aliasManager);
    }

    public override string SearchText 
    { 
        get => _searchText; 
        set 
        { 
            if (_searchText == value) return; 
            var old = _searchText; 
            _searchText = value; 
            UpdateSearchText(old, value); 
        } 
    }

    public override IListItem[] GetItems()
    {
        if (SearchText.Length == 0)
            return FallbackItems();

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

        var results = ParseQuery(SearchText)
            .Where(x => x != null)
            .GroupBy(r => new { r.Title, r.Subtitle })
            .Select(g => g.First());

        IsLoading = false;
        return [.. results];
    }

    private AnonymousCommand UpdateSearchCommand(string text)
    {
        return new AnonymousCommand(() =>
         {
             SearchText = text;
         })
        {
            Name = "Use",
            Result = CommandResult.KeepOpen()
        };
    }

    private IListItem[] FallbackItems()
    {
        return [
            new ListItem(new OpenUrlCommand(GithubReadmeURL))
            {
                Title = "Start typing to convert currencies",
                Subtitle = "Few examples are listed below",
                Icon = IconManager.Icon,
            },
            new ListItem(UpdateSearchCommand("100 USD to INR"))
            {
                Title = "100 USD to INR",
                Subtitle = "Convert 100 US Dollars to Indian Rupees",
                Icon = IconManager.Icon,
                MoreCommands = [
                    new CommandContextItem(new CopyTextCommand("100 USD to INR"))
                ]
            },
            new ListItem(UpdateSearchCommand("$100 to €"))
            {
                Title = "$100 to €",
                Subtitle = "Convert 100 US Dollars to Euros",
                Icon = IconManager.Icon,
                MoreCommands = [
                    new CommandContextItem(new CopyTextCommand("100 USD to INR"))
                ]
            },
            new ListItem(UpdateSearchCommand("₽100"))
            {
                Title = "₽100",
                Subtitle = "Convert 100 Russian Rubles",
                Icon = IconManager.Icon,
                MoreCommands = [
                    new CommandContextItem(new CopyTextCommand("100 USD to INR"))
                ]
            },
        ];
    }

    private List<ListItem> ParseQuery(string search)
    {
        NumberFormatInfo formatter = GetNumberFormatInfo();
        string decimalSeparator = Regex.Escape(formatter.CurrencyDecimalSeparator);
        string groupSeparator = Regex.Escape(formatter.CurrencyGroupSeparator);

        string amountPattern = $@"(?<amount>(?:\d+|\s+|{decimalSeparator}|{groupSeparator}|[+\-*/()])+)";
        string fromPattern = $@"(?<from>{AliasManager.KeyRegex})";
        string toPattern = $@"(?<to>{AliasManager.KeyRegex})";

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
        {
            DebounceSearch(newSearch.Length);
        }
    }

    private CancellationTokenSource? _debounceCts;

    private void DebounceSearch(int searchLength)
    {
        // Cancel any ongoing debounce task
        _debounceCts?.Cancel();
        _debounceCts = new CancellationTokenSource();

        var token = _debounceCts.Token;

        Task.Delay(300, token) // 300ms debounce delay
            .ContinueWith(t =>
            {
                if (!t.IsCanceled)
                {
                    // Trigger the items update after debounce delay
                    RaiseItemsChanged(searchLength);
                }
            }, TaskScheduler.Default);
    }

    public void Dispose()
    {
        _converter.Dispose();
    }
}
