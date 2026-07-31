using CurrencyConverterExtension.Helpers;
using Microsoft.CommandPalette.Extensions.Toolkit;
using System;
using System.Threading.Tasks;

namespace CurrencyConverterExtension.Commands;

internal sealed partial class PinConversionCommand : InvokableCommand
{
    internal readonly PinnedConversionManager _pinManager;
    internal readonly PinnedConversion _pin;

    public event Action? ItemsChanged;

    internal PinConversionCommand(PinnedConversionManager pinManager, PinnedConversion pin)
    {
        _pinManager = pinManager;
        _pin = pin;

        Name = "Pin";
        Icon = new IconInfo("\uE718");
    }

    public override CommandResult Invoke()
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await _pinManager.EnsureInitializedAsync().ConfigureAwait(false);
                await _pinManager.AddPinAsync(_pin).ConfigureAwait(false);
                ItemsChanged?.Invoke();
                new ToastStatusMessage(
                    $"Pinned {_pin.Amount.ToString("N", System.Globalization.CultureInfo.CurrentCulture)} {_pin.FromCurrency.ToUpperInvariant()} to {_pin.ToCurrency.ToUpperInvariant()}").Show();
            }
            catch (Exception ex)
            {
                new ToastStatusMessage($"Failed to pin conversion: {ex.Message}").Show();
            }
        });

        return CommandResult.KeepOpen();
    }
}
