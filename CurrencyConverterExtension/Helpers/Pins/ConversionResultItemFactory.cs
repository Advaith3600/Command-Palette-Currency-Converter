using CurrencyConverterExtension.Commands;
using CurrencyConverterExtension.Converter;
using Microsoft.CommandPalette.Extensions.Toolkit;
using System;
using System.Globalization;

namespace CurrencyConverterExtension.Helpers;

internal static class ConversionResultItemFactory
{
    internal const string PinnedLoadingSubtitle = "Loading…";
    internal const string PinnedLoadFailedSubtitle = "Loading failed";

    /// <summary>
    /// Builds a list item for a conversion result with pin/unpin actions kept in sync
    /// on the main converter (search results and pinned empty listing).
    /// Enter copies; pin/unpin is available from the context menu.
    /// </summary>
    /// <param name="treatAsPinned">
    /// When true, always render as pinned (e.g. rows loaded from the pins list),
    /// including failed conversions so Unpin remains available.
    /// </param>
    internal static ListItem Create(
        ConversionOutcome outcome,
        PinnedConversionManager pinManager,
        Action onPinsChanged,
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
                Icon = CurrencyIconManager.For(toCode),
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

        return new ListItem(copyCommand)
        {
            Title = outcome.Item.Title,
            Subtitle = string.Empty,
            Icon = CurrencyIconManager.For(toCode),
            Details = outcome.Item.Details,
            Tags = outcome.Item.Tags,
            MoreCommands =
            [
                new CommandContextItem(pinCommand)
            ],
        };
    }

    /// <summary>Row shown while a pinned conversion's rate is still loading.</summary>
    internal static ListItem CreatePinnedLoadingItem(
        PinnedConversion pin,
        PinnedConversionManager pinManager,
        Action onPinsChanged) =>
        CreatePinnedStatusItem(pin, pinManager, onPinsChanged, PinnedLoadingSubtitle, IconManager.Icon);

    /// <summary>Row shown when a pinned conversion failed to load (HTTP/cache error).</summary>
    internal static ListItem CreatePinnedLoadFailedItem(
        PinnedConversion pin,
        PinnedConversionManager pinManager,
        Action onPinsChanged) =>
        CreatePinnedStatusItem(pin, pinManager, onPinsChanged, PinnedLoadFailedSubtitle, IconManager.WarningIcon);

    private static ListItem CreatePinnedStatusItem(
        PinnedConversion pin,
        PinnedConversionManager pinManager,
        Action onPinsChanged,
        string subtitle,
        IconInfo icon)
    {
        string fromCode = pin.FromCurrency.ToUpperInvariant();
        string toCode = pin.ToCurrency.ToUpperInvariant();

        UnpinConversionCommand unpinCommand = new(pinManager, pin);
        unpinCommand.ItemsChanged += onPinsChanged;

        return new ListItem(new NoOpCommand())
        {
            Title = pin.ToDisplayLabel(),
            Subtitle = subtitle,
            Icon = icon,
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
