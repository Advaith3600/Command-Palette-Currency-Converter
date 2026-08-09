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

    private readonly IListItem _aliasItem;
    private readonly ConversionSearchController _search;
    private readonly object _pinFallbackGate = new();
    private IListItem[] _items = [];
    private string? _lastRequestedSearch;
    private int _suppressSearchUpdate;
    private CancellationTokenSource? _pinFallbackCts;
    private int _pinFallbackVersion;
    private int _pinFallbackInFlight;
    private int _pinFallbackLoaded;
    private int _pinSlotCount;
    private bool _disposed;

    public CurrencyConverterExtensionPage(
        SettingsManager settings,
        AliasManager aliasManager,
        PinnedConversionManager pinManager,
        CurrencyConverter converter,
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

        _pinManager.PinsChanged += OnPinsChangedExternally;
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

        CancelPinFallback();
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
        IsLoading = false;
        _ = LoadPinnedFallbackAsync();
    }

    public override IListItem[] GetItems()
    {
        if (SearchText.Length == 0)
        {
            if (_pinFallbackLoaded == 0 && _pinFallbackInFlight == 0)
            {
                _ = LoadPinnedFallbackAsync();
            }

            return _items.Length == 0
                ? BuildNavItems(includeExampleConversions: !(_pinManager.IsInitialized && _pinManager.GetAllPins().Count > 0))
                : _items;
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
             CancelPinFallback();
             SetSearchTextWithoutConverting(text);
             _lastRequestedSearch = text;
             _ = _search.DebounceAndConvertAsync(text);
         })
        {
            Name = "Use",
            Result = CommandResult.KeepOpen()
        };
    }

    private IListItem[] BuildNavItems(bool includeExampleConversions)
    {
        List<IListItem> items = [_aliasItem];

        if (includeExampleConversions)
        {
            items.Add(new ListItem(UpdateSearchCommand("100 USD to INR"))
            {
                Title = "100 USD to INR",
                Subtitle = "Convert 100 US Dollars to Indian Rupees",
                Icon = IconManager.Icon,
                MoreCommands =
                [
                    new CommandContextItem(new CopyTextCommand("100 USD to INR")
                    {
                        Result = CommandResult.ShowToast(new ToastArgs()
                        {
                            Message = "Copied to clipboard",
                            Result = CommandResult.KeepOpen()
                        })
                    })
                ]
            });
            items.Add(new ListItem(UpdateSearchCommand("$100 to €"))
            {
                Title = "$100 to €",
                Subtitle = "Convert 100 US Dollars to Euros",
                Icon = IconManager.Icon,
                MoreCommands =
                [
                    new CommandContextItem(new CopyTextCommand("$100 to €")
                    {
                        Result = CommandResult.ShowToast(new ToastArgs()
                        {
                            Message = "Copied to clipboard",
                            Result = CommandResult.KeepOpen()
                        })
                    })
                ]
            });
            items.Add(new ListItem(UpdateSearchCommand("₽100"))
            {
                Title = "₽100",
                Subtitle = "Convert 100 Russian Rubles",
                Icon = IconManager.Icon,
                MoreCommands =
                [
                    new CommandContextItem(new CopyTextCommand("₽100")
                    {
                        Result = CommandResult.ShowToast(new ToastArgs()
                        {
                            Message = "Copied to clipboard",
                            Result = CommandResult.KeepOpen()
                        })
                    })
                ]
            });
        }

        items.Add(new ListItem(new OpenUrlCommand(_converter.GetHelperLink()))
        {
            Title = "All available currencies",
            Subtitle = "Opens the currency list for the selected API",
            Icon = IconManager.Icon,
        });
        items.Add(new ListItem(_settings.Settings.SettingsPage)
        {
            Title = "Open settings",
            Subtitle = "Local currency, quick conversion currencies, API, and more",
            Icon = IconManager.Icon,
        });

        return [.. items];
    }

    private void OnPinsChangedExternally()
    {
        if (string.IsNullOrEmpty(SearchText))
        {
            _ = LoadPinnedFallbackAsync();
        }
    }

    private void CancelPinFallback()
    {
        CancellationTokenSource? previous = Interlocked.Exchange(ref _pinFallbackCts, null);
        previous?.Cancel();
        previous?.Dispose();
        Interlocked.Exchange(ref _pinFallbackInFlight, 0);
        Interlocked.Exchange(ref _pinFallbackLoaded, 0);
        Interlocked.Exchange(ref _pinSlotCount, 0);
    }

    private async Task LoadPinnedFallbackAsync()
    {
        CancellationTokenSource cts = new();
        CancellationTokenSource? previous = Interlocked.Exchange(ref _pinFallbackCts, cts);
        previous?.Cancel();
        previous?.Dispose();

        CancellationToken ct = cts.Token;
        int version = Interlocked.Increment(ref _pinFallbackVersion);
        Interlocked.Exchange(ref _pinFallbackInFlight, 1);
        Interlocked.Exchange(ref _pinFallbackLoaded, 0);

        // Show nav immediately while pins initialize / rates resolve.
        // Omit example queries if we already know the user has pins.
        bool includeExamples = !(_pinManager.IsInitialized && _pinManager.GetAllPins().Count > 0);
        IListItem[] navItems = BuildNavItems(includeExamples);
        if (_items.Length == 0)
        {
            _items = navItems;
            RaiseItemsChanged();
        }

        try
        {
            await _pinManager.EnsureInitializedAsync().ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();

            if (version != _pinFallbackVersion || !string.IsNullOrEmpty(SearchText))
            {
                return;
            }

            List<PinnedConversion> pins = _pinManager.GetAllPins();
            IListItem[] pinItems = [.. pins.Select(pin =>
                ConversionResultItemFactory.CreatePinnedLoadingItem(pin, _pinManager, OnPinned))];

            // Hide the three sample conversions once the user has any pins.
            navItems = BuildNavItems(includeExampleConversions: pins.Count == 0);

            Interlocked.Exchange(ref _pinSlotCount, pinItems.Length);
            _items = [.. pinItems, .. navItems];
            RaiseItemsChanged();

            if (pinItems.Length == 0)
            {
                return;
            }

            Task[] resolveTasks = new Task[pins.Count];
            for (int i = 0; i < pins.Count; i++)
            {
                int index = i;
                PinnedConversion pin = pins[i];
                resolveTasks[i] = ResolvePinnedSlotAsync(index, pin, version, ct);
            }

            await Task.WhenAll(resolveTasks).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception)
        {
            if (version != _pinFallbackVersion || !string.IsNullOrEmpty(SearchText))
            {
                return;
            }

            // Keep whatever pin/nav rows we already painted; mark unresolved slots as failed.
            ReplaceUnresolvedPinSlotsWithFailed(version);
        }
        finally
        {
            if (version == _pinFallbackVersion)
            {
                Interlocked.Exchange(ref _pinFallbackInFlight, 0);
                Interlocked.Exchange(ref _pinFallbackLoaded, 1);
            }
        }
    }

    private async Task ResolvePinnedSlotAsync(
        int index,
        PinnedConversion pin,
        int version,
        CancellationToken cancellationToken)
    {
        try
        {
            List<ConversionOutcome> outcomes = await _converter.GetConversionOutcomesAsync(
                pin.Amount,
                pin.FromCurrency,
                pin.ToCurrency,
                cancellationToken).ConfigureAwait(false);

            cancellationToken.ThrowIfCancellationRequested();

            ConversionOutcome? success = outcomes.FirstOrDefault(o => o.IsSuccess);
            IListItem resolved = success is null
                ? ConversionResultItemFactory.CreatePinnedLoadFailedItem(pin, _pinManager, OnPinned)
                : ConversionResultItemFactory.Create(success, _pinManager, OnPinned, treatAsPinned: true);

            ReplacePinSlot(index, resolved, version);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            ReplacePinSlot(
                index,
                ConversionResultItemFactory.CreatePinnedLoadFailedItem(pin, _pinManager, OnPinned),
                version);
        }
    }

    private void ReplacePinSlot(int index, IListItem item, int version)
    {
        lock (_pinFallbackGate)
        {
            if (version != _pinFallbackVersion || !string.IsNullOrEmpty(SearchText))
            {
                return;
            }

            if (index < 0 || index >= _pinSlotCount || index >= _items.Length)
            {
                return;
            }

            IListItem[] next = [.. _items];
            next[index] = item;
            _items = next;
        }

        RaiseItemsChanged();
    }

    private void ReplaceUnresolvedPinSlotsWithFailed(int version)
    {
        lock (_pinFallbackGate)
        {
            if (version != _pinFallbackVersion || !string.IsNullOrEmpty(SearchText))
            {
                return;
            }

            int pinCount = _pinSlotCount;
            if (pinCount == 0 || _items.Length < pinCount)
            {
                return;
            }

            List<PinnedConversion> pins = _pinManager.GetAllPins();
            IListItem[] next = [.. _items];
            for (int i = 0; i < pinCount && i < pins.Count; i++)
            {
                // Only rewrite rows that still look like loading placeholders.
                if (string.Equals(next[i].Subtitle, ConversionResultItemFactory.PinnedLoadingSubtitle, StringComparison.Ordinal))
                {
                    next[i] = ConversionResultItemFactory.CreatePinnedLoadFailedItem(
                        pins[i],
                        _pinManager,
                        OnPinned);
                }
            }

            _items = next;
        }

        RaiseItemsChanged();
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
        // Empty listing reloads via PinsChanged; avoid a second concurrent LoadPinnedFallbackAsync.
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

        if (oldSearch == newSearch)
        {
            return;
        }

        if (string.IsNullOrEmpty(newSearch))
        {
            _search.CancelPendingWork();
            IsLoading = false;
            _lastRequestedSearch = newSearch;
            _ = LoadPinnedFallbackAsync();
            return;
        }

        CancelPinFallback();
        IsLoading = true;
        _lastRequestedSearch = newSearch;
        _ = _search.DebounceAndConvertAsync(newSearch);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _pinManager.PinsChanged -= OnPinsChangedExternally;
        CancelPinFallback();
        _search.Dispose();
    }
}
