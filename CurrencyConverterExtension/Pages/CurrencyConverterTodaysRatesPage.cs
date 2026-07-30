// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using CurrencyConverterExtension.Commands;
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

internal sealed partial class CurrencyConverterTodaysRatesPage : DynamicListPage, IDisposable
{
    internal readonly SettingsManager _settings;
    internal readonly CurrencyConverter _converter;
    internal readonly AliasManager _aliasManager;
    internal readonly PinnedConversionManager _pinManager;

    private IListItem[] _items = [];
    private CancellationTokenSource? _debounceCts;
    private CancellationTokenSource? _conversionCts;
    private CancellationTokenSource? _defaultCts;
    private int _defaultLoadInFlight;

    public CurrencyConverterTodaysRatesPage(
        SettingsManager settings,
        AliasManager aliasManager,
        PinnedConversionManager pinManager)
    {
        Id = "CurrencyConverterTodaysRatesPage";
        Icon = IconManager.Icon;
        Title = "Today's rates";
        Name = "Today's rates";
        ShowDetails = true;

        _settings = settings;
        _aliasManager = aliasManager;
        _pinManager = pinManager;
        _converter = new(_settings, aliasManager);
    }

    public override IListItem[] GetItems()
    {
        if (SearchText.Length == 0)
        {
            if (_items.Length == 0 && Volatile.Read(ref _defaultLoadInFlight) == 0)
            {
                _ = LoadDefaultViewAsync();
            }

            return _items.Length == 0 ? [CreateLoadingItem()] : _items;
        }

        return _items;
    }

    public override void UpdateSearchText(string oldSearch, string newSearch)
    {
        if (oldSearch == newSearch)
        {
            return;
        }

        if (string.IsNullOrEmpty(newSearch))
        {
            _ = LoadDefaultViewAsync();
            return;
        }

        _ = DebounceAndConvertAsync(newSearch);
    }

    private void CancelSearchWork()
    {
        CancellationTokenSource? previousDebounce = Interlocked.Exchange(ref _debounceCts, null);
        previousDebounce?.Cancel();
        previousDebounce?.Dispose();

        CancellationTokenSource? previousConversion = Interlocked.Exchange(ref _conversionCts, null);
        previousConversion?.Cancel();
        previousConversion?.Dispose();
    }

    private async Task LoadDefaultViewAsync()
    {
        CancelSearchWork();

        CancellationTokenSource defaultCts = new();
        CancellationTokenSource? previous = Interlocked.Exchange(ref _defaultCts, defaultCts);
        previous?.Cancel();
        previous?.Dispose();
        CancellationToken ct = defaultCts.Token;

        Interlocked.Exchange(ref _defaultLoadInFlight, 1);
        _items = [CreateLoadingItem()];
        IsLoading = true;
        RaiseItemsChanged(0);

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
                List<IListItem> errorItems = [];
                if (_pinManager.GetAllPins().Count == 0)
                {
                    errorItems.Add(CreateHintItem());
                }

                errorItems.Add(new ListItem(new OpenUrlCommand(CurrencyConverterExtensionPage.GithubReadmeURL))
                {
                    Title = ex.Message,
                    Subtitle = "Press enter or click to see how to fix this issue",
                    Icon = IconManager.WarningIcon,
                });
                _items = [.. errorItems];
                return;
            }

            List<IListItem> items = [];
            List<PinnedConversion> pins = _pinManager.GetAllPins();

            foreach (PinnedConversion pin in pins)
            {
                ct.ThrowIfCancellationRequested();
                List<ConversionOutcome> outcomes = await _converter.GetConversionOutcomesAsync(
                    pin.Amount,
                    pin.FromCurrency,
                    pin.ToCurrency,
                    ct).ConfigureAwait(false);

                foreach (ConversionOutcome outcome in outcomes)
                {
                    items.Add(CreatePinnedItem(outcome, pin));
                }
            }

            if (pins.Count == 0)
            {
                items.Add(CreateHintItem());
            }

            string local = _settings.LocalCurrency.Trim();
            string[] otherCurrencies = [.. _settings.Currencies
                .Select(c => c.Trim())
                .Where(c => c.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)];

            bool hasConvertibleOther = otherCurrencies.Any(c =>
                !string.Equals(c, local, StringComparison.OrdinalIgnoreCase));

            if (!hasConvertibleOther)
            {
                items.Add(new ListItem(_settings.Settings.SettingsPage)
                {
                    Title = "No quick rates available",
                    Subtitle = "Local currency matches your other currencies. Press Enter to open Settings and add a different currency.",
                    Icon = IconManager.WarningIcon,
                });
            }
            else
            {
                foreach (string currency in otherCurrencies)
                {
                    if (string.Equals(currency, local, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    ct.ThrowIfCancellationRequested();
                    List<ConversionOutcome> outcomes = await _converter.GetConversionOutcomesAsync(
                        1m,
                        local,
                        currency,
                        ct).ConfigureAwait(false);

                    foreach (ConversionOutcome outcome in outcomes)
                    {
                        items.Add(outcome.Item);
                    }
                }
            }

            _items = [.. items];
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception)
        {
            List<IListItem> errorItems = [];
            if (_pinManager.IsInitialized && _pinManager.GetAllPins().Count == 0)
            {
                errorItems.Add(CreateHintItem());
            }

            errorItems.Add(new ListItem(new NoOpCommand())
            {
                Title = "Something went wrong while loading today's rates",
                Subtitle = "Please try again",
                Icon = IconManager.WarningIcon,
            });
            _items = [.. errorItems];
        }
        finally
        {
            if (!ct.IsCancellationRequested)
            {
                Interlocked.Exchange(ref _defaultLoadInFlight, 0);
                IsLoading = false;
                RaiseItemsChanged(0);
            }
        }
    }

    private ListItem CreateLoadingItem() =>
        new(new NoOpCommand())
        {
            Title = "Loading today's rates…",
            Subtitle = "Please wait",
            Icon = Icon,
        };

    private async Task DebounceAndConvertAsync(string search)
    {
        CancellationTokenSource? previousDefault = Interlocked.Exchange(ref _defaultCts, null);
        previousDefault?.Cancel();
        previousDefault?.Dispose();
        Interlocked.Exchange(ref _defaultLoadInFlight, 0);

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

        if (string.IsNullOrEmpty(search))
        {
            await LoadDefaultViewAsync().ConfigureAwait(false);
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
                    new ListItem(new OpenUrlCommand(CurrencyConverterExtensionPage.GithubReadmeURL))
                    {
                        Title = ex.Message,
                        Subtitle = "Press enter or click to see how to fix this issue",
                        Icon = IconManager.WarningIcon,
                    }
                ];
                return;
            }

            var parseResult = QueryParser.Parse(search, _settings.DecimalSeparator);
            _items = parseResult.Status switch
            {
                QueryParseStatus.NoMatch => [],
                QueryParseStatus.InvalidExpression =>
                [
                    new ListItem(new NoOpCommand())
                    {
                        Title = "Invalid expression provided",
                        Subtitle = "Please check your mathematical expression",
                        Icon = IconManager.WarningIcon,
                    }
                ],
                QueryParseStatus.Success => await BuildSearchResultItemsAsync(parseResult.Query!.Value, ct).ConfigureAwait(false),
                _ => [],
            };
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

    private async Task<IListItem[]> BuildSearchResultItemsAsync(ParsedQuery query, CancellationToken cancellationToken)
    {
        List<ConversionOutcome> outcomes = await _converter.GetConversionOutcomesAsync(
            query.Amount,
            query.FromCurrency,
            query.ToCurrency,
            cancellationToken).ConfigureAwait(false);

        return [.. outcomes
            .GroupBy(o => new { o.Item.Title, o.Item.Subtitle })
            .Select(g => g.First())
            .Select(CreateSearchResultItem)];
    }

    private IListItem CreateSearchResultItem(ConversionOutcome outcome)
    {
        if (!outcome.IsSuccess)
        {
            return outcome.Item;
        }

        PinnedConversion pin = new(outcome.Amount, outcome.FromCurrency, outcome.ToCurrency);
        PinConversionCommand pinCommand = new(_pinManager, pin);
        pinCommand.ItemsChanged += OnPinned;

        return new ListItem(pinCommand)
        {
            Title = outcome.Item.Title,
            Subtitle = "Press Enter to pin this conversion",
            Icon = IconManager.Icon,
            Details = outcome.Item.Details,
            MoreCommands =
            [
                new CommandContextItem(CurrencyConverter.CreateCopyCommand(outcome.ToFormatted))
            ]
        };
    }

    private ListItem CreatePinnedItem(ConversionOutcome outcome, PinnedConversion pin)
    {
        if (!outcome.IsSuccess)
        {
            return outcome.Item;
        }

        UnpinConversionCommand unpinCommand = new(_pinManager, pin);
        unpinCommand.ItemsChanged += OnPinned;

        return new ListItem(CurrencyConverter.CreateCopyCommand(outcome.ToFormatted))
        {
            Title = outcome.Item.Title,
            Subtitle = $"Pinned · {outcome.Item.Subtitle}",
            Icon = IconManager.Icon,
            Details = outcome.Item.Details,
            MoreCommands =
            [
                new CommandContextItem(unpinCommand)
            ]
        };
    }

    private static ListItem CreateHintItem() =>
        new(new NoOpCommand())
        {
            Title = "Pin custom conversions",
            Subtitle = "Search a conversion like 34 BTC to AED, then press Enter to pin it here",
            Icon = IconManager.Icon,
        };

    private void OnPinned()
    {
        SearchText = string.Empty;
        OnPropertyChanged(nameof(SearchText));
        _ = LoadDefaultViewAsync();
    }

    public void Dispose()
    {
        _debounceCts?.Cancel();
        _debounceCts?.Dispose();
        _conversionCts?.Cancel();
        _conversionCts?.Dispose();
        _defaultCts?.Cancel();
        _defaultCts?.Dispose();
        _converter.Dispose();
    }
}
