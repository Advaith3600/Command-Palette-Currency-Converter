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
    private IListItem[] _items = [];
    private CancellationTokenSource? _debounceCts;
    private CancellationTokenSource? _conversionCts;
    private string? _lastRequestedSearch;

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

        CancelPendingWork();
        SearchText = query;
        OnPropertyChanged(nameof(SearchText));
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

        CancelPendingWork();
        SearchText = string.Empty;
        OnPropertyChanged(nameof(SearchText));
        _lastRequestedSearch = null;
        _items = [];
        IsLoading = false;
    }

    private void CancelPendingWork()
    {
        CancellationTokenSource? previousDebounce = Interlocked.Exchange(ref _debounceCts, null);
        previousDebounce?.Cancel();
        previousDebounce?.Dispose();

        CancellationTokenSource? previousConversion = Interlocked.Exchange(ref _conversionCts, null);
        previousConversion?.Cancel();
        previousConversion?.Dispose();
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
            _ = DebounceAndConvertAsync(SearchText);
        }

        return _items;
    }

    private AnonymousCommand UpdateSearchCommand(string text)
    {
        return new AnonymousCommand(() =>
         {
             SearchText = text;
             OnPropertyChanged(nameof(SearchText));
             _lastRequestedSearch = text;
             _ = DebounceAndConvertAsync(text);
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

    private async Task<List<IListItem>> ParseQueryAsync(string search, CancellationToken cancellationToken)
    {
        var parseResult = QueryParser.Parse(search, _settings.DecimalSeparator);

        return parseResult.Status switch
        {
            QueryParseStatus.NoMatch => [],
            QueryParseStatus.InvalidExpression => [
                new ListItem(new NoOpCommand())
                {
                    Title = "Invalid expression provided",
                    Subtitle = "Please check your mathematical expression",
                    Icon = IconManager.WarningIcon,
                }
            ],
            QueryParseStatus.Success => await BuildConversionItemsAsync(parseResult.Query!.Value, cancellationToken).ConfigureAwait(false),
            _ => [],
        };
    }

    private async Task<List<IListItem>> BuildConversionItemsAsync(ParsedQuery query, CancellationToken cancellationToken)
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
        _ = ConvertNowAsync(SearchText);
    }

    public override void UpdateSearchText(string oldSearch, string newSearch)
    {
        if (oldSearch != newSearch)
        {
            _lastRequestedSearch = newSearch;
            _ = DebounceAndConvertAsync(newSearch);
        }
    }

    private async Task DebounceAndConvertAsync(string search)
    {
        CancellationTokenSource debounceCts = new();
        CancellationTokenSource? previousDebounce = Interlocked.Exchange(ref _debounceCts, debounceCts);
        previousDebounce?.Cancel();
        previousDebounce?.Dispose();

        try
        {
            await Task.Delay(300, debounceCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        await ConvertNowAsync(search).ConfigureAwait(false);
    }

    private async Task ConvertNowAsync(string search)
    {
        if (string.IsNullOrEmpty(search))
        {
            IsLoading = false;
            _items = [];
            RaiseItemsChanged(0);
            return;
        }

        CancellationTokenSource conversionCts = new();
        CancellationTokenSource? previousConversion = Interlocked.Exchange(ref _conversionCts, conversionCts);
        previousConversion?.Cancel();
        previousConversion?.Dispose();
        CancellationToken ct = conversionCts.Token;

        IsLoading = true;
        RaiseItemsChanged(search.Length);

        try
        {
            await _aliasManager.EnsureInitializedAsync().ConfigureAwait(false);
            await _pinManager.EnsureInitializedAsync().ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();

            try
            {
                _converter.ValidateConversionAPI();
            }
            catch (Exception ex)
            {
                _items =
                [
                    new ListItem(new OpenUrlCommand(GithubReadmeURL))
                    {
                        Title = ex.Message,
                        Subtitle = "Press enter or click to see how to fix this issue",
                        Icon = IconManager.WarningIcon,
                    }
                ];
                return;
            }

            var results = await ParseQueryAsync(search, ct).ConfigureAwait(false);
            _items = [.. results];
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception)
        {
            _items =
            [
                new ListItem(new NoOpCommand())
                {
                    Title = "Something went wrong while converting currencies",
                    Subtitle = "Please try again",
                    Icon = IconManager.WarningIcon,
                }
            ];
        }
        finally
        {
            if (!ct.IsCancellationRequested)
            {
                IsLoading = false;
                RaiseItemsChanged(search.Length);
            }
        }
    }

    public void Dispose()
    {
        CancelPendingWork();
    }
}
