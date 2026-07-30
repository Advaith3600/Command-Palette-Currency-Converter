// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using CurrencyConverterExtension.Helpers;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace CurrencyConverterExtension;

public partial class CurrencyConverterExtensionCommandsProvider : CommandProvider
{
    private readonly ICommandItem[] _commands;
    private readonly CommandItem _todaysRatesCommand;
    private readonly CommandItem _aliasCommand;
    private readonly SettingsManager _settingsManager = new();
    private readonly AliasManager _aliasManager = new();
    private readonly PinnedConversionManager _pinManager = new();

    public CurrencyConverterExtensionCommandsProvider()
    {
        DisplayName = "Currency Converter";
        Icon = IconHelpers.FromRelativePath("Assets\\StoreLogo.png");
        Settings = _settingsManager.Settings;

        // Kick off alias / pin loading without blocking the COM constructor (WinRT async).
        _ = _aliasManager.EnsureInitializedAsync();
        _ = _pinManager.EnsureInitializedAsync();

        _todaysRatesCommand = new CommandItem(
            new CurrencyConverterTodaysRatesPage(_settingsManager, _aliasManager, _pinManager))
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

        _commands = [
            new CommandItem(new CurrencyConverterExtensionPage(
                _settingsManager,
                _aliasManager,
                _todaysRatesCommand,
                _aliasCommand))
            {
                Title = DisplayName,
                Icon = Icon,
                Subtitle = "Convert real and crypto currencies.",
                MoreCommands = [
                    new CommandContextItem(Settings.SettingsPage)
                ]
            },
        ];
    }

    public override ICommandItem[] TopLevelCommands()
    {
        return _commands;
    }

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

        foreach (ICommandItem item in _commands)
        {
            if (item.Command?.Id == id)
            {
                return item;
            }
        }

        return null;
    }
}
