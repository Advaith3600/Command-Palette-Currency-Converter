// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using CurrencyConverterExtension.Converter;
using CurrencyConverterExtension.Helpers;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace CurrencyConverterExtension;

internal sealed partial class CurrencyConverterExtensionPage : DynamicListPage, IDisposable
{
    internal readonly SettingsManager _settings;
    internal readonly CurrencyConverter _converter;
    internal readonly AliasManager _aliasManager;
    internal readonly PinnedConversionManager _pinManager;

    internal const string GithubReadmeURL = "https://github.com/Advaith3600/Command-Palette-Currency-Converter?tab=readme-ov-file";

    private readonly IListItem _todaysRatesItem;
    private readonly IListItem _aliasItem;
    private readonly ConversionSearchController _search;
    private IListItem[] _items = [];
    private string? _lastRequestedSearch;
    private int _suppressSearchUpdate;

    public CurrencyConverterExtensionPage(
        SettingsManager settings,
        AliasManager aliasManager,
        PinnedConversionManager pinManager,
        CurrencyConverter converter,
        CommandItem todaysRatesCommand,
        CommandItem aliasCommand,
        string id = "CurrencyConverterExtensionPage")
    {
        Id = id;
        Icon = IconManager.Icon;
        Title = "Currency Converter";
        Name = "Convert";
        ShowDetails = true;
        EmptyContent = new ListItem(new NoOpCommand())
        {
            Title = "No conversion results",
            Subtitle = "Try a query like 100 USD to EUR",
            Icon = IconManager.Icon,
        };

        _settings = settings;
        _aliasManager = aliasManager;
        _pinManager = pinManager;
        _converter = converter;

        _todaysRatesItem = new ListItem(todaysRatesCommand.Command!)
        {
            Title = todaysRatesCommand.Title,
            Subtitle = todaysRatesCommand.Subtitle,
            Icon = todaysRatesCommand.Icon ?? Icon,
        };
        _aliasItem = new ListItem(aliasCommand.Command!)
        {
            Title = aliasCommand.Title,
            Subtitle = aliasCommand.Subtitle,
            Icon = aliasCommand.Icon ?? Icon,
        };

        _search = new ConversionSearchController(
            settings,
            aliasManager,
            pinManager,
            converter,
            BuildConversionItemsAsync,
            items => _items = items,
            loading => IsLoading = loading,
            RaiseItemsChanged);
    }

    /// <summary>
    /// Sets <see cref="DynamicListPage.SearchText"/> without invoking <see cref="UpdateSearchText"/>
    /// (the SDK setter always calls UpdateSearchText).
    /// </summary>
    private void SetSearchTextWithoutConverting(string query)
    {
        Interlocked.Exchange(ref _suppressSearchUpdate, 1);
        try
        {
            SearchText = query;
            OnPropertyChanged(nameof(SearchText));
        }
        finally
        {
            Interlocked.Exchange(ref _suppressSearchUpdate, 0);
        }
    }

    /// <summary>
    /// Pre-fills the search box when the home-page fallback matches a conversion query.
    /// </summary>
    internal void ApplyFallbackQuery(string query)
    {
        if (SearchText == query)
        {
            return;
        }

        _search.CancelPendingWork();
        SetSearchTextWithoutConverting(query);
        // Leave _lastRequestedSearch null so GetItems starts conversion when the page is shown.
        _lastRequestedSearch = null;
        _items = [];
        IsLoading = false;
    }

    internal void ClearFallbackQuery()
    {
        if (string.IsNullOrEmpty(SearchText) && _lastRequestedSearch is null)
        {
            return;
        }

        _search.CancelPendingWork();
        SetSearchTextWithoutConverting(string.Empty);
        _lastRequestedSearch = null;
        _items = [];
        IsLoading = false;
    }

    public override IListItem[] GetItems()
    {
        if (SearchText.Length == 0)
        {
            return FallbackItems();
        }

        if (_lastRequestedSearch != SearchText)
        {
            _lastRequestedSearch = SearchText;
            _ = _search.DebounceAndConvertAsync(SearchText);
        }

        return _items;
    }

    private AnonymousCommand UpdateSearchCommand(string text)
    {
        return new AnonymousCommand(() =>
         {
             SetSearchTextWithoutConverting(text);
             _lastRequestedSearch = text;
             _ = _search.DebounceAndConvertAsync(text);
         })
        {
            Name = "Use",
            Result = CommandResult.KeepOpen()
        };
    }

    private IListItem[] FallbackItems()
    {
        return [
            _todaysRatesItem,
            _aliasItem,
            new ListItem(UpdateSearchCommand("100 USD to INR"))
            {
                Title = "100 USD to INR",
                Subtitle = "Convert 100 US Dollars to Indian Rupees",
                Icon = IconManager.Icon,
                MoreCommands = [
                    new CommandContextItem(new CopyTextCommand("100 USD to INR") {
                        Result = CommandResult.ShowToast(new ToastArgs()
                        {
                            Message = "Copied to clipboard",
                            Result = CommandResult.KeepOpen()
                        })
                    })
                ]
            },
            new ListItem(UpdateSearchCommand("$100 to €"))
            {
                Title = "$100 to €",
                Subtitle = "Convert 100 US Dollars to Euros",
                Icon = IconManager.Icon,
                MoreCommands = [
                    new CommandContextItem(new CopyTextCommand("$100 to €") {
                        Result = CommandResult.ShowToast(new ToastArgs()
                        {
                            Message = "Copied to clipboard",
                            Result = CommandResult.KeepOpen()
                        })
                    })
                ]
            },
            new ListItem(UpdateSearchCommand("₽100"))
            {
                Title = "₽100",
                Subtitle = "Convert 100 Russian Rubles",
                Icon = IconManager.Icon,
                MoreCommands = [
                    new CommandContextItem(new CopyTextCommand("₽100") {
                        Result = CommandResult.ShowToast(new ToastArgs()
                        {
                            Message = "Copied to clipboard",
                            Result = CommandResult.KeepOpen()
                        })
                    })
                ]
            },
            new ListItem(new OpenUrlCommand("https://cdn.jsdelivr.net/npm/@fawazahmed0/currency-api@latest/v1/currencies.json"))
            {
                Title = "All available currencies",
                Subtitle = "Opens the full currencies list (JSON)",
                Icon = IconManager.Icon,
            },
            new ListItem(_settings.Settings.SettingsPage)
            {
                Title = "Open settings",
                Subtitle = "Local currency, quick conversion currencies, API, and more",
                Icon = IconManager.Icon,
            },
        ];
    }

    private async Task<IListItem[]> BuildConversionItemsAsync(ParsedQuery query, CancellationToken cancellationToken)
    {
        List<ConversionOutcome> outcomes = await _converter.GetConversionOutcomesAsync(
            query.Amount,
            query.FromCurrency,
            query.ToCurrency,
            cancellationToken).ConfigureAwait(false);

        return [.. outcomes
            .GroupBy(o => new { o.Item.Title, o.Item.Subtitle })
            .Select(g => g.First())
            .Select(o => ConversionResultItemFactory.Create(o, _pinManager, OnPinned))];
    }

    private void OnPinned()
    {
        if (string.IsNullOrEmpty(SearchText))
        {
            return;
        }

        // Skip debounce so the pinned/unpinned state updates immediately.
        _ = _search.ConvertNowAsync(SearchText);
    }

    public override void UpdateSearchText(string oldSearch, string newSearch)
    {
        if (_suppressSearchUpdate != 0)
        {
            return;
        }

        if (oldSearch != newSearch)
        {
            IsLoading = !string.IsNullOrEmpty(newSearch);
            _lastRequestedSearch = newSearch;
            _ = _search.DebounceAndConvertAsync(newSearch);
        }
    }

    public void Dispose() => _search.Dispose();
}
