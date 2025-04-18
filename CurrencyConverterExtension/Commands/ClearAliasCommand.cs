using CurrencyConverterExtension.Helpers;
using Microsoft.CommandPalette.Extensions.Toolkit;
using System;

namespace CurrencyConverterExtension.Commands
{
    internal sealed partial class ClearAliasCommand : InvokableCommand
    {
        internal readonly AliasManager _aliasManager;
        internal readonly string _aliasKey;

        public event Action ItemsChanged;

        internal ClearAliasCommand(AliasManager aliasManager, string aliasKey)
        {
            _aliasManager = aliasManager;
            _aliasKey = aliasKey;

            Name = "Remove";
            Icon = new IconInfo("\uE8BB");
        }

        public override CommandResult Invoke()
        {
            return CommandResult.Confirm(new()
            {
                PrimaryCommand = new AnonymousCommand(
                () =>
                {
                    _aliasManager.RemoveAliasAsync(_aliasKey).Wait();
                    ItemsChanged?.Invoke();
                    ToastStatusMessage t = new("The alias was deleted");
                    t.Show();
                })
                {
                    Name = "Confirm",
                    Result = CommandResult.KeepOpen(),
                },
                Title = "Are you sure you want to remove this alias?",
                Description = $"You are about to remove the alias '{_aliasKey}' => '{_aliasManager.GetAlias(_aliasKey)}'",
            });
        }
    }
}
