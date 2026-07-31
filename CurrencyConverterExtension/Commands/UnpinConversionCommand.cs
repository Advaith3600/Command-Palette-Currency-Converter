using CurrencyConverterExtension.Helpers;
using Microsoft.CommandPalette.Extensions.Toolkit;
using System;
using System.Globalization;
using System.Threading.Tasks;

namespace CurrencyConverterExtension.Commands;

internal sealed partial class UnpinConversionCommand : InvokableCommand
{
    internal readonly PinnedConversionManager _pinManager;
    internal readonly PinnedConversion _pin;

    public event Action? ItemsChanged;

    internal UnpinConversionCommand(PinnedConversionManager pinManager, PinnedConversion pin)
    {
        _pinManager = pinManager;
        _pin = pin;

        Name = "Unpin";
        Icon = new IconInfo("\uE8BB");
    }

    public override CommandResult Invoke()
    {
        string label =
            $"{_pin.Amount.ToString("N", CultureInfo.CurrentCulture)} {_pin.FromCurrency.ToUpperInvariant()} to {_pin.ToCurrency.ToUpperInvariant()}";

        return CommandResult.Confirm(new()
        {
            PrimaryCommand = new AnonymousCommand(
            () =>
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await _pinManager.EnsureInitializedAsync().ConfigureAwait(false);
                        await _pinManager.RemovePinAsync(_pin).ConfigureAwait(false);
                        ItemsChanged?.Invoke();
                        new ToastStatusMessage("Pinned conversion removed").Show();
                    }
                    catch (Exception ex)
                    {
                        new ToastStatusMessage($"Failed to unpin conversion: {ex.Message}").Show();
                    }
                });
            })
            {
                Name = "Confirm",
                Result = CommandResult.KeepOpen(),
            },
            Title = "Remove this pinned conversion?",
            Description = $"You are about to unpin '{label}'",
        });
    }
}
