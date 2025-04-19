using System.Linq;
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
        //ListItem createAlias = new(new OpenCreateAliasPageCommand());

        return [
            //createAlias, 
            .._aliasManager
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
                })
        ];
    }

    private void OnAliasClear() => RaiseItemsChanged();
}