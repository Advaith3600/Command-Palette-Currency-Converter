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
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace CurrencyConverterExtension;

public partial class CurrencyConverterExtensionCommandsProvider : CommandProvider
{
    private readonly ICommandItem[] _commands;
    private readonly CommandItem _todaysRatesCommand;
    private readonly CommandItem _aliasCommand;
    private readonly CommandItem _mainCommand;
    private readonly CurrencyConverterFallbackItem _fallbackItem;
    private readonly SettingsManager _settingsManager = new();
    private readonly AliasManager _aliasManager = new();
    private readonly PinnedConversionManager _pinManager = new();
    private readonly CurrencyConverter _converter;
    private WrappedDockItem? _pinnedDockBand;
    private int _dockRefreshVersion;

    public CurrencyConverterExtensionCommandsProvider()
    {
        DisplayName = "Currency Converter";
        Icon = IconHelpers.FromRelativePath("Assets\\StoreLogo.png");
        Settings = _settingsManager.Settings;

        // Kick off alias / pin loading without blocking the COM constructor (WinRT async).
        _ = _aliasManager.EnsureInitializedAsync();
        _ = _pinManager.EnsureInitializedAsync();
        // One shared converter so main, fallback, today's rates, and dock share the rate cache.
        _converter = new CurrencyConverter(_settingsManager, _aliasManager);

        CurrencyConverterTodaysRatesPage todaysRatesPage = new(
            _settingsManager,
            _aliasManager,
            _pinManager,
            _converter);
        // Pin/unpin and today's-rates reload both notify; RefreshDockBandAsync coalesces concurrent calls.
        todaysRatesPage.RatesRefreshed += OnDockDataChanged;
        _pinManager.PinsChanged += OnDockDataChanged;

        _todaysRatesCommand = new CommandItem(todaysRatesPage)
        {
            Title = "Today's rates",
            Subtitle = "1 local currency to your other currencies, plus pinned conversions",
            Icon = Icon,
        };

        _aliasCommand = new CommandItem(new CurrencyConverterAliasPage(_aliasManager))
        {
            Title = "Manage currency aliases",
            Subtitle = "View, create and remove your aliases",
            Icon = Icon,
        };

        CurrencyConverterExtensionPage mainPage = new(
            _settingsManager,
            _aliasManager,
            _converter,
            _todaysRatesCommand,
            _aliasCommand);

        _mainCommand = new CommandItem(mainPage)
        {
            Title = DisplayName,
            Icon = Icon,
            Subtitle = "Convert real and crypto currencies.",
            MoreCommands = [
                new CommandContextItem(Settings.SettingsPage)
            ]
        };

        // Separate page instance so home-page fallback typing does not mutate the top-level page.
        CurrencyConverterExtensionPage fallbackPage = new(
            _settingsManager,
            _aliasManager,
            _converter,
            _todaysRatesCommand,
            _aliasCommand,
            "CurrencyConverterExtensionPage.Fallback");
        _fallbackItem = new CurrencyConverterFallbackItem(fallbackPage, _settingsManager);

        _commands = [_mainCommand];
    }

    public override ICommandItem[] TopLevelCommands() => _commands;

    public override IFallbackCommandItem[] FallbackCommands() => [_fallbackItem];

    public override ICommandItem[]? GetDockBands()
    {
        _pinnedDockBand ??= new WrappedDockItem(
            [],
            "CurrencyConverter.Dock.Pinned",
            "Currency pins")
        {
            Icon = Icon,
        };

        // Pins and rates load async; update Items when ready (and on later GetDockBands calls).
        _ = RefreshDockBandAsync();
        return [_pinnedDockBand];
    }

    private void OnDockDataChanged() => _ = RefreshDockBandAsync();

    private async Task RefreshDockBandAsync()
    {
        // Band may not exist yet if the host has not asked for dock bands.
        if (_pinnedDockBand is null)
        {
            return;
        }

        int version = Interlocked.Increment(ref _dockRefreshVersion);

        try
        {
            await _pinManager.EnsureInitializedAsync().ConfigureAwait(false);
            await _aliasManager.EnsureInitializedAsync().ConfigureAwait(false);

            IListItem[] items = await BuildDockBandItemsAsync().ConfigureAwait(false);

            // A newer refresh started while we were loading — drop this result.
            if (version != Volatile.Read(ref _dockRefreshVersion))
            {
                return;
            }

            _pinnedDockBand.Items = items;
        }
        catch (Exception)
        {
            // Keep whatever items are currently shown.
        }
    }

    private async Task<IListItem[]> BuildDockBandItemsAsync()
    {
        List<PinnedConversion> pins = _pinManager.GetAllPins();
        if (pins.Count == 0)
        {
            return
            [
                new ListItem(_todaysRatesCommand.Command!)
                {
                    Title = "Pin conversions",
                    Subtitle = "Open Today's rates to pin pairs for the Dock",
                    Icon = Icon,
                }
            ];
        }

        List<IListItem> items = [];
        foreach (PinnedConversion pin in pins)
        {
            items.Add(await CreatePinnedDockItemAsync(pin).ConfigureAwait(false));
        }

        return [.. items];
    }

    private async Task<IListItem> CreatePinnedDockItemAsync(PinnedConversion pin)
    {
        string from = pin.FromCurrency.ToUpperInvariant();
        string to = pin.ToCurrency.ToUpperInvariant();
        string amount = pin.Amount.ToString(CultureInfo.CurrentCulture);
        string pairLabel = $"{amount} {from} → {to}";
        string commandId = $"CurrencyConverter.Dock.Pin.{pin.Amount.ToString(CultureInfo.InvariantCulture)}.{pin.FromCurrency}.{pin.ToCurrency}";

        try
        {
            List<ConversionOutcome> outcomes = await _converter.GetConversionOutcomesAsync(
                pin.Amount,
                pin.FromCurrency,
                pin.ToCurrency).ConfigureAwait(false);

            ConversionOutcome? success = outcomes.FirstOrDefault(o => o.IsSuccess);
            if (success is null)
            {
                return CreateDockPlaceholderItem(pairLabel, commandId);
            }

            CopyTextCommand copyCommand = CurrencyConverter.CreateCopyCommand(success.ToFormatted);
            copyCommand.Id = commandId;

            return new ListItem(copyCommand)
            {
                Title = $"{success.ToFormatted} {success.ToCurrency.ToUpperInvariant()}",
                Subtitle = pairLabel,
                Icon = Icon,
                Details = success.Item.Details,
                Tags =
                [
                    new Tag(from),
                    new Tag(to),
                    new Tag("Pinned"),
                ],
            };
        }
        catch (Exception)
        {
            return CreateDockPlaceholderItem(pairLabel, commandId);
        }
    }

    private ListItem CreateDockPlaceholderItem(string pairLabel, string commandId) =>
        new(new NoOpCommand { Id = commandId })
        {
            Title = pairLabel,
            Subtitle = string.Empty,
            Icon = Icon,
        };

    public override ICommandItem? GetCommandItem(string id)
    {
        if (_todaysRatesCommand.Command?.Id == id)
        {
            return _todaysRatesCommand;
        }

        if (_aliasCommand.Command?.Id == id)
        {
            return _aliasCommand;
        }

        if (_mainCommand.Command?.Id == id)
        {
            return _mainCommand;
        }

        if (_pinnedDockBand?.Command?.Id == id)
        {
            return _pinnedDockBand;
        }

        return null;
    }
}
