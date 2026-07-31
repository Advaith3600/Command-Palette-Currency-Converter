// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using CurrencyConverterExtension.Commands;
using CurrencyConverterExtension.Converter;
using CurrencyConverterExtension.Helpers;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using System;

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
    private readonly PinnedDockBandManager _dockBandManager;
    private readonly CurrencyConverterTodaysRatesPage _todaysRatesPage;
    private readonly CurrencyConverterExtensionPage _mainPage;
    private readonly CurrencyConverterExtensionPage _fallbackPage;
    private bool _disposed;

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

        _todaysRatesPage = new(
            _settingsManager,
            _aliasManager,
            _pinManager,
            _converter);

        _todaysRatesCommand = new CommandItem(_todaysRatesPage)
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

        _mainPage = new(
            _settingsManager,
            _aliasManager,
            _pinManager,
            _converter,
            _todaysRatesCommand,
            _aliasCommand);

        _mainCommand = new CommandItem(_mainPage)
        {
            Title = DisplayName,
            Icon = Icon,
            Subtitle = "Convert real and crypto currencies.",
            MoreCommands = [
                new CommandContextItem(Settings.SettingsPage)
            ]
        };

        // Separate page instance so home-page fallback typing does not mutate the top-level page.
        _fallbackPage = new(
            _settingsManager,
            _aliasManager,
            _pinManager,
            _converter,
            _todaysRatesCommand,
            _aliasCommand,
            "CurrencyConverterExtensionPage.Fallback");
        _fallbackItem = new CurrencyConverterFallbackItem(_fallbackPage, _settingsManager);

        // PinsChanged alone drives dock refresh; RatesRefreshed would double-fetch after pin changes.
        _dockBandManager = new PinnedDockBandManager(
            _converter,
            _pinManager,
            _aliasManager,
            Icon,
            _todaysRatesCommand.Command!);
        _pinManager.PinsChanged += OnPinsChanged;

        _commands = [_mainCommand];
    }

    private void OnPinsChanged()
    {
        _dockBandManager.MarkDirty();
        _ = _dockBandManager.RefreshAsync();
    }

    public override ICommandItem[] TopLevelCommands() => _commands;

    public override IFallbackCommandItem[] FallbackCommands() => [_fallbackItem];

    public override ICommandItem[]? GetDockBands() => _dockBandManager.GetDockBands();

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

        if (_dockBandManager.Band?.Command?.Id == id)
        {
            return _dockBandManager.Band;
        }

        return null;
    }

    public override void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _pinManager.PinsChanged -= OnPinsChanged;
        _dockBandManager.Dispose();
        _todaysRatesPage.Dispose();
        _mainPage.Dispose();
        _fallbackPage.Dispose();
        _converter.Dispose();
        base.Dispose();
        GC.SuppressFinalize(this);
    }
}
