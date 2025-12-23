using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using CurrencyConverterExtension.Commands;
using CurrencyConverterExtension.Helpers;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace CurrencyConverterExtension;

internal sealed partial class CurrencyConverterAliasPage : ListPage
{
    internal readonly AliasManager _aliasManager;

    public CurrencyConverterAliasPage(AliasManager aliasManager)
    {
        Icon = IconManager.Icon;
        Title = "Currency Converter";
        Name = "Alias";

        _aliasManager = aliasManager;
    }

    public override IListItem[] GetItems()
    {
        // TODO: When GoToPage is implemented in CommandResult, use it to navigate to the CreateAliasPage
        // https://github.com/microsoft/PowerToys/issues/38338
        // ListItem createAlias = new(new OpenCreateAliasPageCommand());

        List<IListItem> items = new();

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
                        await _aliasManager.ResetToDefaultAsync().ConfigureAwait(false);
                        RaiseItemsChanged();
                        new ToastStatusMessage("Aliases reset to default.").Show();
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
                string path = await _aliasManager.ExportAliasesAsync().ConfigureAwait(false);
                new ToastStatusMessage($"Aliases exported to {path}").Show();
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
            .Select(kvp => {
                ClearAliasCommand command = new(_aliasManager, kvp.Key);
                command.ItemsChanged += OnAliasClear;
                return new ListItem(new NoOpCommand())
                {
                    Title = $"{kvp.Key} => {kvp.Value}",
                    Icon = IconManager.Icon,
                    MoreCommands = [
                        new CommandContextItem(command)
                    ]
                };
            }));

        return [.. items];
    }

    private void OnAliasClear() => RaiseItemsChanged();
}