using CurrencyConverterExtension.Helpers;
using Microsoft.CommandPalette.Extensions.Toolkit;
using System;
using System.Threading.Tasks;

namespace CurrencyConverterExtension.Commands;

internal sealed partial class RefreshPinnedDockCommand : InvokableCommand
{
    private readonly PinnedConversion _pin;
    private readonly Func<PinnedConversion, Task> _refreshGroupAsync;

    internal RefreshPinnedDockCommand(PinnedConversion pin, Func<PinnedConversion, Task> refreshGroupAsync)
    {
        _pin = pin;
        _refreshGroupAsync = refreshGroupAsync;

        Name = "Refresh";
        Icon = new IconInfo("\uE72C");
    }

    public override CommandResult Invoke()
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await _refreshGroupAsync(_pin).ConfigureAwait(false);
                new ToastStatusMessage(
                    $"Refreshed rates for {_pin.FromCurrency.ToUpperInvariant()}").Show();
            }
            catch (Exception ex)
            {
                new ToastStatusMessage($"Failed to refresh: {ex.Message}").Show();
            }
        });

        return CommandResult.KeepOpen();
    }
}