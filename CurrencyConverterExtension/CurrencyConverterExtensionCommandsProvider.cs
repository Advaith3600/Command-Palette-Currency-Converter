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
    private readonly SettingsManager _settingsManager = new();
    private readonly AliasManager _aliasManager = new();

    public CurrencyConverterExtensionCommandsProvider()
    {
        DisplayName = "Currency Converter";
        Icon = IconHelpers.FromRelativePath("Assets\\StoreLogo.png");
        Settings = _settingsManager.Settings;

        // Kick off alias loading without blocking the COM constructor (WinRT async).
        _ = _aliasManager.EnsureInitializedAsync();

        _commands = [
            new CommandItem(new CurrencyConverterExtensionPage(_settingsManager, _aliasManager)) {
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

}
