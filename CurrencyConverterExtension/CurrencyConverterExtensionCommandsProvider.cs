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
    private readonly CommandItem _aliasCommand;
    private readonly CommandItem _mainCommand;
    private readonly CurrencyConverterFallbackItem _fallbackItem;
    private readonly SettingsManager _settingsManager = new();
    private readonly AliasManager _aliasManager = new();
    private readonly PinnedConversionManager _pinManager = new();
    private readonly CurrencyConverter _converter;
    private readonly PinnedDockBandManager _dockBandManager;
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
        // One shared converter so main, fallback, and dock share the rate cache.
        _converter = new CurrencyConverter(_settingsManager, _aliasManager);

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
            _aliasCommand,
            "CurrencyConverterExtensionPage.Fallback");
        _fallbackItem = new CurrencyConverterFallbackItem(_fallbackPage, _settingsManager);

        // PinsChanged alone drives dock refresh.
        _dockBandManager = new PinnedDockBandManager(
            _converter,
            _pinManager,
            _aliasManager,
            Icon,
            _mainCommand.Command!);
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
        if (_aliasCommand.Command?.Id == id)
        {
            return CreatePinnedPageDockItem(_aliasCommand);
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

    /// <summary>
    /// Wrap a nested list page as a single-button dock band so Pin to Dock opens
    /// the page instead of expanding <see cref="IListPage.GetItems"/> as buttons.
    /// Dock renders each <see cref="ListItem"/>'s Title/Subtitle, so set them on
    /// the item (<see cref="WrappedDockItem.Subtitle"/> alone only covers Pin to Home).
    /// </summary>
    private WrappedDockItem CreatePinnedPageDockItem(CommandItem source)
    {
        ICommand command = source.Command!;
        string title = source.Title ?? command.Name;

        return new WrappedDockItem(
            [
                new ListItem(command)
                {
                    Title = title,
                    Subtitle = source.Subtitle,
                    Icon = source.Icon ?? Icon,
                },
            ],
            command.Id,
            title)
        {
            Subtitle = source.Subtitle,
            Icon = source.Icon ?? Icon,
        };
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
        _mainPage.Dispose();
        _fallbackPage.Dispose();
        _converter.Dispose();
        base.Dispose();
        GC.SuppressFinalize(this);
    }
}
