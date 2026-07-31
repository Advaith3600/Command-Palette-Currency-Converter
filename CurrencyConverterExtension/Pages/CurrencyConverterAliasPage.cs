using CurrencyConverterExtension.Commands;
using CurrencyConverterExtension.Helpers;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace CurrencyConverterExtension;

internal sealed partial class CurrencyConverterAliasPage : ListPage
{
    internal readonly AliasManager _aliasManager;

    public CurrencyConverterAliasPage(AliasManager aliasManager)
    {
        Id = "CurrencyConverterAliasPage";
        Icon = IconManager.Icon;
        Title = "Currency Converter";
        Name = "Alias";

        _aliasManager = aliasManager;
    }

    public override IListItem[] GetItems()
    {
        if (!_aliasManager.IsInitialized)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await _aliasManager.EnsureInitializedAsync().ConfigureAwait(false);
                    RaiseItemsChanged();
                }
                catch (Exception ex)
                {
                    new ToastStatusMessage($"Failed to load aliases: {ex.Message}").Show();
                }
            });

            return
            [
                new ListItem(new NoOpCommand())
                {
                    Title = "Loading aliases…",
                    Subtitle = "Please wait",
                    Icon = Icon,
                }
            ];
        }

        List<IListItem> items = new();

        items.Add(new ListItem(new CurrencyConverterCreateAliasPage(_aliasManager))
        {
            Title = "Create a new currency alias.",
            Subtitle = "Add your own alias to make the conversions faster",
            Icon = Icon,
        });

        items.Add(new ListItem(new AnonymousCommand(() => { })
        {
            Name = "Reset aliases",
            Result = CommandResult.Confirm(new()
            {
                Title = "Reset aliases to default?",
                Description = "This will restore the built-in alias list and remove any custom entries.",
                PrimaryCommand = new AnonymousCommand(() =>
                {
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await _aliasManager.EnsureInitializedAsync().ConfigureAwait(false);
                            await _aliasManager.ResetToDefaultAsync().ConfigureAwait(false);
                            RaiseItemsChanged();
                            new ToastStatusMessage("Aliases reset to default.").Show();
                        }
                        catch (Exception ex)
                        {
                            new ToastStatusMessage($"Failed to reset aliases: {ex.Message}").Show();
                        }
                    });
                })
                {
                    Name = "Reset",
                    Result = CommandResult.KeepOpen(),
                }
            })
        })
        {
            Title = "Reset aliases to default",
            Subtitle = "Restore the built-in alias file",
            Icon = IconManager.WarningIcon,
        });

        items.Add(new ListItem(new AnonymousCommand(() =>
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await _aliasManager.EnsureInitializedAsync().ConfigureAwait(false);
                    string path = await _aliasManager.ExportAliasesAsync().ConfigureAwait(false);
                    new ToastStatusMessage($"Aliases exported to {path}").Show();
                }
                catch (Exception ex)
                {
                    new ToastStatusMessage($"Failed to export aliases: {ex.Message}").Show();
                }
            });
        })
        {
            Name = "Export aliases",
            Result = CommandResult.KeepOpen()
        })
        {
            Title = "Export aliases",
            Subtitle = "Save current aliases to a JSON file",
            Icon = IconManager.Icon,
        });

        items.AddRange(_aliasManager
            .GetAllAliases()
            .Select(kvp =>
            {
                ClearAliasCommand command = new(_aliasManager, kvp.Key);
                command.ItemsChanged += OnAliasClear;
                return new ListItem(new NoOpCommand())
                {
                    Title = $"{kvp.Key} ⇒ {kvp.Value}",
                    Icon = IconManager.Icon,
                    Tags =
                    [
                        new Tag(kvp.Key),
                        new Tag(kvp.Value.ToUpperInvariant()),
                    ],
                    MoreCommands = [
                        new CommandContextItem(command)
                    ]
                };
            }));

        return [.. items];
    }

    private void OnAliasClear() => RaiseItemsChanged();
}
