using CurrencyConverterExtension.Commands;
using CurrencyConverterExtension.Converter;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace CurrencyConverterExtension.Helpers;

internal sealed class DockPinItemFactory
{
    private readonly CurrencyConverter _converter;
    private readonly PinnedConversionManager _pinManager;
    private readonly IconInfo _icon;
    private readonly Func<PinnedConversion, Task> _refreshGroupAsync;

    internal DockPinItemFactory(
        CurrencyConverter converter,
        PinnedConversionManager pinManager,
        IconInfo icon,
        Func<PinnedConversion, Task> refreshGroupAsync)
    {
        _converter = converter;
        _pinManager = pinManager;
        _icon = icon;
        _refreshGroupAsync = refreshGroupAsync;
    }

    internal IListItem CreateEmptyPinsPlaceholder(ICommand todaysRatesCommand) =>
        new ListItem(todaysRatesCommand)
        {
            Title = "Pin conversions",
            Subtitle = "Pin a conversion from Currency Converter or Today's rates",
            Icon = _icon,
        };

    internal async Task<IListItem> CreatePinnedDockItemAsync(PinnedConversion pin)
    {
        string from = pin.FromCurrency.ToUpperInvariant();
        string to = pin.ToCurrency.ToUpperInvariant();
        string amount = pin.Amount.ToString("N", CultureInfo.CurrentCulture);
        string pairLabel = $"{amount} {from} → {to}";
        string commandId = CreateDockPinCommandId(pin);

        try
        {
            List<ConversionOutcome> outcomes = await _converter.GetConversionOutcomesAsync(
                pin.Amount,
                pin.FromCurrency,
                pin.ToCurrency).ConfigureAwait(false);

            ConversionOutcome? success = outcomes.FirstOrDefault(o => o.IsSuccess);
            if (success is null)
            {
                return CreateDockPlaceholderItem(pairLabel, commandId, pin);
            }

            CopyTextCommand copyCommand = CurrencyConverter.CreateCopyCommand(success.ToFormatted);
            copyCommand.Id = commandId;

            RefreshPinnedDockCommand refreshCommand = new(pin, _refreshGroupAsync);
            UnpinConversionCommand unpinCommand = new(_pinManager, pin);

            string updatedAt = CurrencyConverter.FormatRateUpdatedAt(success.RateUpdatedAt);
            List<IContextItem> moreCommands =
            [
                new CommandContextItem(refreshCommand),
                new CommandContextItem(unpinCommand),
            ];
            if (updatedAt != "—")
            {
                // Dock has no details pane; surface last-updated time in the context menu.
                moreCommands.Add(new CommandContextItem(new NoOpCommand
                {
                    Name = $"Updated {updatedAt}",
                    Icon = new IconInfo("\uE823"), // Recent (clock)
                }));
            }

            return new ListItem(copyCommand)
            {
                Title = $"{success.ToFormatted} {success.ToCurrency.ToUpperInvariant()}",
                Subtitle = pairLabel,
                Icon = _icon,
                MoreCommands = [.. moreCommands],
            };
        }
        catch (Exception)
        {
            return CreateDockPlaceholderItem(pairLabel, commandId, pin);
        }
    }

    private ListItem CreateDockPlaceholderItem(string pairLabel, string commandId, PinnedConversion pin)
    {
        RefreshPinnedDockCommand refreshCommand = new(pin, _refreshGroupAsync);
        UnpinConversionCommand unpinCommand = new(_pinManager, pin);

        return new ListItem(new NoOpCommand { Id = commandId })
        {
            Title = pairLabel,
            Subtitle = string.Empty,
            Icon = _icon,
            MoreCommands =
            [
                new CommandContextItem(refreshCommand),
                new CommandContextItem(unpinCommand),
            ],
        };
    }

    private static string CreateDockPinCommandId(PinnedConversion pin) =>
        $"CurrencyConverter.Dock.Pin.{pin.Amount.ToString(CultureInfo.InvariantCulture)}.{pin.FromCurrency}.{pin.ToCurrency}";
}