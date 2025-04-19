using Microsoft.CommandPalette.Extensions.Toolkit;

namespace CurrencyConverterExtension.Commands
{
    internal sealed partial class OpenCreateAliasPageCommand : InvokableCommand
    {
        internal OpenCreateAliasPageCommand()
        {
            Name = "Create a new alias";
            Icon = new IconInfo("\uE710");
        }

        public override CommandResult Invoke()
        {
            return CommandResult.GoToPage(new()
            {
                PageId = "CurrencyConverterCreateAliasPage",
            });
        }
    }
}
