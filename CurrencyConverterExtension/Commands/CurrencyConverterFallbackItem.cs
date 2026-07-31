// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using CurrencyConverterExtension.Converter;
using CurrencyConverterExtension.Helpers;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace CurrencyConverterExtension.Commands;

internal sealed partial class CurrencyConverterFallbackItem : FallbackCommandItem
{
    private const string FallbackId = "CurrencyConverter.Fallback.Convert";

    private readonly CurrencyConverterExtensionPage _page;
    private readonly SettingsManager _settings;

    internal CurrencyConverterFallbackItem(CurrencyConverterExtensionPage page, SettingsManager settings)
        : base(page, "Convert with Currency Converter", FallbackId)
    {
        _page = page;
        _settings = settings;
        Title = string.Empty;
        Subtitle = string.Empty;
        Icon = IconManager.Icon;
    }

    public override void UpdateQuery(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            Hide();
            return;
        }

        QueryParseResult parseResult = QueryParser.Parse(query, _settings.DecimalSeparator);
        if (parseResult.Status == QueryParseStatus.NoMatch)
        {
            Hide();
            return;
        }

        Title = "Convert with Currency Converter";
        Subtitle = query.Trim();
        Icon = IconManager.Icon;
        _page.ApplyFallbackQuery(query.Trim());
    }

    private void Hide()
    {
        Title = string.Empty;
        Subtitle = string.Empty;
        _page.ClearFallbackQuery();
    }
}