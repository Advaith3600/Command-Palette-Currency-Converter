using CurrencyConverterExtension.Helpers;
using Microsoft.CommandPalette.Extensions.Toolkit;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace CurrencyConverterExtension.Commands;

internal sealed partial class RefreshPinnedDockCommand : InvokableCommand
{
    private static readonly IconInfo RefreshIcon = new("\uE72C");
    private static int _refreshInFlight;

    private readonly PinnedConversion _pin;
    private readonly Func<PinnedConversion, Task<bool>> _refreshGroupAsync;

    internal RefreshPinnedDockCommand(PinnedConversion pin, Func<PinnedConversion, Task<bool>> refreshGroupAsync)
    {
        _pin = pin;
        _refreshGroupAsync = refreshGroupAsync;

        Name = "Refresh";
        Icon = RefreshIcon;
    }

    public override CommandResult Invoke()
    {
        if (Interlocked.CompareExchange(ref _refreshInFlight, 1, 0) != 0)
        {
            return CommandResult.KeepOpen();
        }

        _ = InvokeRefreshAsync();
        return CommandResult.KeepOpen();
    }

    private async Task InvokeRefreshAsync()
    {
        try
        {
            bool refreshed = await _refreshGroupAsync(_pin).ConfigureAwait(false);
            if (refreshed)
            {
                new ToastStatusMessage(
                    $"Refreshed rates for {_pin.FromCurrency.ToUpperInvariant()}").Show();
            }
            else
            {
                new ToastStatusMessage(
                    $"Could not refresh rates for {_pin.FromCurrency.ToUpperInvariant()}").Show();
            }
        }
        catch (Exception ex)
        {
            new ToastStatusMessage($"Failed to refresh: {ex.Message}").Show();
        }
        finally
        {
            Interlocked.Exchange(ref _refreshInFlight, 0);
        }
    }
}