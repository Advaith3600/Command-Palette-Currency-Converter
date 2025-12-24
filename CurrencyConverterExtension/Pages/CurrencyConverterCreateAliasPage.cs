using CurrencyConverterExtension.Forms;
using CurrencyConverterExtension.Helpers;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace CurrencyConverterExtension;

internal sealed partial class CurrencyConverterCreateAliasPage : ContentPage
{
    internal readonly AliasManager _aliasManager;
    internal readonly CreateAliasForm _createAliasForm;

    public CurrencyConverterCreateAliasPage(AliasManager aliasManager)
    {
        Id = "CurrencyConverterCreateAliasPage";
        Icon = IconManager.Icon;
        Title = "Currency Converter";
        Name = "Create a new Alias";

        _aliasManager = aliasManager;
        _createAliasForm = new(aliasManager);
    }

    public override IContent[] GetContent()
    {
        return [
            _createAliasForm
        ];
    }
}