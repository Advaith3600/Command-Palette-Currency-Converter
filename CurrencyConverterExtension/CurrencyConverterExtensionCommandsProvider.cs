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

        _aliasManager.InitializeAsync().Wait();
        _commands = [
            new CommandItem(new CurrencyConverterExtensionPage(_settingsManager)) { 
                Title = DisplayName, 
                Icon = Icon, 
                Subtitle = "Convert real and crypto currencies.",
            },
            new CommandItem(new CurrencyConverterAliasPage(_aliasManager)) {
                Title = DisplayName,
                Icon = Icon,
                Subtitle = "Update currency aliases."
            },
        ];
    }

    public override ICommandItem[] TopLevelCommands()
    {
        return _commands;
    }

}
