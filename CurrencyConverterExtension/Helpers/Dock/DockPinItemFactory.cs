using CurrencyConverterExtension.Commands;
using CurrencyConverterExtension.Converter;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace CurrencyConverterExtension.Helpers;

internal sealed class DockPinItemFactory
{
    private static readonly IconInfo UpdatedIcon = new("\uE823"); // Recent (clock)

    private readonly CurrencyConverter _converter;
    private readonly PinnedConversionManager _pinManager;
    private readonly IconInfo _icon;
    private readonly Func<PinnedConversion, Task<bool>> _refreshGroupAsync;

    internal DockPinItemFactory(
        CurrencyConverter converter,
        PinnedConversionManager pinManager,
        IconInfo icon,
        Func<PinnedConversion, Task<bool>> refreshGroupAsync)
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

    internal async Task<IListItem> CreatePinnedDockItemAsync(PinnedConversion pin, CancellationToken cancellationToken = default)
    {
        string pairLabel = pin.ToDisplayLabel();
        string commandId = CreateDockPinCommandId(pin);

        try
        {
            List<ConversionOutcome> outcomes = await _converter.GetConversionOutcomesAsync(
                pin.Amount,
                pin.FromCurrency,
                pin.ToCurrency,
                cancellationToken).ConfigureAwait(false);

            ConversionOutcome? success = outcomes.FirstOrDefault(o => o.IsSuccess);
            if (success is null)
            {
                return CreateDockPlaceholderItem(pairLabel, commandId, pin);
            }

            CopyTextCommand copyCommand = CurrencyConverter.CreateCopyCommand(success.ToFormatted);
            copyCommand.Id = commandId;

            string updatedAt = CurrencyConverter.FormatRateUpdatedAt(success.RateUpdatedAt);
            List<IContextItem> moreCommands = BuildDockContextItems(pin);
            if (updatedAt != "—")
            {
                // Dock has no details pane; surface last-updated time in the context menu.
                moreCommands.Add(new CommandContextItem(new NoOpCommand
                {
                    Name = $"Updated {updatedAt}",
                    Icon = UpdatedIcon,
                }));
            }

            return new ListItem(copyCommand)
            {
                Title = $"{success.ToFormatted} {success.ToCurrency.ToUpperInvariant()}",
                Subtitle = pairLabel,
                Icon = CurrencyIconManager.For(pin.ToCurrency),
                MoreCommands = [.. moreCommands],
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return CreateDockPlaceholderItem(pairLabel, commandId, pin);
        }
    }

    private ListItem CreateDockPlaceholderItem(string pairLabel, string commandId, PinnedConversion pin) =>
        new(new NoOpCommand { Id = commandId })
        {
            Title = pairLabel,
            Subtitle = string.Empty,
            Icon = CurrencyIconManager.For(pin.ToCurrency),
            MoreCommands = [.. BuildDockContextItems(pin)],
        };

    private List<IContextItem> BuildDockContextItems(PinnedConversion pin) =>
    [
        new CommandContextItem(new RefreshPinnedDockCommand(pin, _refreshGroupAsync)),
        new CommandContextItem(new UnpinConversionCommand(_pinManager, pin)),
    ];

    private static string CreateDockPinCommandId(PinnedConversion pin) =>
        $"CurrencyConverter.Dock.Pin.{pin.Amount.ToString(CultureInfo.InvariantCulture)}.{pin.FromCurrency}.{pin.ToCurrency}";
}
