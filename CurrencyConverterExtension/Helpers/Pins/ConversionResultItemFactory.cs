using CurrencyConverterExtension.Commands;
using CurrencyConverterExtension.Converter;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using System;
using System.Globalization;

namespace CurrencyConverterExtension.Helpers;

internal enum ConversionPinAction
{
    /// <summary>Enter copies; pin/unpin is available from the context menu (Ctrl+Enter).</summary>
    Secondary,

    /// <summary>Enter pins when unpinned; copy is available from the context menu.</summary>
    Primary,
}

internal static class ConversionResultItemFactory
{
    /// <summary>
    /// Builds a list item for a conversion result with pin/unpin actions kept in sync
    /// across the main converter and Today's rates pages.
    /// </summary>
    /// <param name="treatAsPinned">
    /// When true, always render as pinned (e.g. rows loaded from the pins list),
    /// including failed conversions so Unpin remains available.
    /// </param>
    internal static ListItem Create(
        ConversionOutcome outcome,
        PinnedConversionManager pinManager,
        Action onPinsChanged,
        ConversionPinAction pinAction = ConversionPinAction.Secondary,
        bool treatAsPinned = false)
    {
        PinnedConversion pin = new(outcome.Amount, outcome.FromCurrency, outcome.ToCurrency);
        string fromCode = string.IsNullOrEmpty(outcome.FromCurrency)
            ? string.Empty
            : outcome.FromCurrency.ToUpperInvariant();
        string toCode = string.IsNullOrEmpty(outcome.ToCurrency)
            ? string.Empty
            : outcome.ToCurrency.ToUpperInvariant();
        bool isPinned = treatAsPinned || pinManager.Contains(pin);

        if (!outcome.IsSuccess)
        {
            if (!isPinned)
            {
                return outcome.Item;
            }

            return CreatePinnedFailureItem(outcome, pinManager, pin, fromCode, toCode, onPinsChanged);
        }

        if (isPinned)
        {
            UnpinConversionCommand unpinCommand = new(pinManager, pin);
            unpinCommand.ItemsChanged += onPinsChanged;

            return new ListItem(CurrencyConverter.CreateCopyCommand(outcome.ToFormatted))
            {
                Title = outcome.Item.Title,
                Subtitle = string.Empty,
                Icon = IconManager.Icon,
                Details = CurrencyConverter.CreateConversionDetails(
                    outcome.Amount.ToString("N", CultureInfo.CurrentCulture),
                    fromCode,
                    outcome.ToFormatted,
                    toCode,
                    outcome.Rate,
                    outcome.RateUpdatedAt,
                    "Pinned"),
                Tags =
                [
                    new Tag("Pinned"),
                    new Tag(fromCode),
                    new Tag(toCode),
                ],
                MoreCommands =
                [
                    new CommandContextItem(unpinCommand)
                ],
            };
        }

        PinConversionCommand pinCommand = new(pinManager, pin);
        pinCommand.ItemsChanged += onPinsChanged;
        CopyTextCommand copyCommand = CurrencyConverter.CreateCopyCommand(outcome.ToFormatted);

        if (pinAction == ConversionPinAction.Primary)
        {
            return new ListItem(pinCommand)
            {
                Title = outcome.Item.Title,
                Subtitle = "Press Enter to pin this conversion",
                Icon = IconManager.Icon,
                Details = outcome.Item.Details,
                Tags = outcome.Item.Tags,
                MoreCommands =
                [
                    new CommandContextItem(copyCommand)
                ],
            };
        }

        return new ListItem(copyCommand)
        {
            Title = outcome.Item.Title,
            Subtitle = string.Empty,
            Icon = IconManager.Icon,
            Details = outcome.Item.Details,
            Tags = outcome.Item.Tags,
            MoreCommands =
            [
                new CommandContextItem(pinCommand)
            ],
        };
    }

    /// <summary>Placeholder row for a pin that produced no conversion outcomes.</summary>
    internal static ListItem CreatePinnedPlaceholder(
        PinnedConversion pin,
        PinnedConversionManager pinManager,
        Action onPinsChanged)
    {
        string fromCode = pin.FromCurrency.ToUpperInvariant();
        string toCode = pin.ToCurrency.ToUpperInvariant();
        string title = pin.ToDisplayLabel();

        UnpinConversionCommand unpinCommand = new(pinManager, pin);
        unpinCommand.ItemsChanged += onPinsChanged;

        return new ListItem(new NoOpCommand())
        {
            Title = title,
            Subtitle = "Unable to convert this pinned pair",
            Icon = IconManager.WarningIcon,
            Tags =
            [
                new Tag("Pinned"),
                new Tag(fromCode),
                new Tag(toCode),
            ],
            MoreCommands =
            [
                new CommandContextItem(unpinCommand)
            ],
        };
    }

    private static ListItem CreatePinnedFailureItem(
        ConversionOutcome outcome,
        PinnedConversionManager pinManager,
        PinnedConversion pin,
        string fromCode,
        string toCode,
        Action onPinsChanged)
    {
        UnpinConversionCommand unpinCommand = new(pinManager, pin);
        unpinCommand.ItemsChanged += onPinsChanged;

        ListItem item = outcome.Item;
        item.Tags =
        [
            new Tag("Pinned"),
            ..(string.IsNullOrEmpty(fromCode) ? [] : new Tag[] { new(fromCode) }),
            ..(string.IsNullOrEmpty(toCode) ? [] : new Tag[] { new(toCode) }),
        ];
        item.MoreCommands =
        [
            new CommandContextItem(unpinCommand)
        ];
        return item;
    }
}