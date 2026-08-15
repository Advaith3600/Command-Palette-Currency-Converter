// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using CurrencyConverterExtension.Converter;
using CurrencyConverterExtension.Helpers;
using Microsoft.CommandPalette.Extensions.Toolkit;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace CurrencyConverterExtension.Commands;

internal sealed partial class CurrencyConverterFallbackItem : FallbackCommandItem, IDisposable
{
    private const string FallbackId = "CurrencyConverter.Fallback.Convert";
    private const string FallbackDisplayTitle = "Convert with Currency Converter";
    private const string ConverterSubtitle = "Currency Converter";
    private const int DebounceMilliseconds = 300;

    // Stable Id so CmdPal can disable or pin this fallback if the host honors it.
    public override string Id => FallbackId;

    private readonly CurrencyConverterExtensionPage _page;
    private readonly SettingsManager _settings;
    private readonly AliasManager _aliasManager;
    private readonly CurrencyConverter _converter;
    private readonly NoOpCommand _hiddenCommand = new() { Name = string.Empty };
    private readonly CommandContextItem _openConverterContextItem;

    private CancellationTokenSource? _debounceCts;
    private CancellationTokenSource? _conversionCts;
    private bool _disposed;

    internal CurrencyConverterFallbackItem(
        CurrencyConverterExtensionPage page,
        SettingsManager settings,
        AliasManager aliasManager,
        CurrencyConverter converter)
        : base(page, FallbackDisplayTitle, FallbackId)
    {
        _page = page;
        _settings = settings;
        _aliasManager = aliasManager;
        _converter = converter;
        _openConverterContextItem = new CommandContextItem(_page)
        {
            Title = "Open Currency Converter",
        };
        ApplyHidden();
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

        string trimmed = query.Trim();
        _page.ApplyFallbackQuery(trimmed);

        if (parseResult.Status != QueryParseStatus.Success || parseResult.Query is null)
        {
            CancelPendingWork();
            ShowOpenConverter(trimmed);
            return;
        }

        ParsedQuery parsed = parseResult.Query.Value;
        FallbackConversionPair? pair = FallbackConversionSelector.TrySelect(
            parsed,
            _settings.LocalCurrency,
            _settings.Currencies,
            _aliasManager);

        if (pair is null && _aliasManager.IsInitialized)
        {
            CancelPendingWork();
            ShowOpenConverter(trimmed);
            return;
        }

        if (pair is { } selected
            && _converter.TryConvertFromCache(parsed.Amount, selected.FromCurrency, selected.ToCurrency, out ConversionOutcome? cached)
            && cached is { IsSuccess: true })
        {
            CancelPendingWork();
            ApplyOutcome(cached);
            return;
        }

        if (pair is { } placeholder)
        {
            SetPlaceholder(parsed.Amount, placeholder);
        }
        else
        {
            ShowOpenConverter(trimmed);
        }

        _ = ConvertAfterDebounceAsync(trimmed, parsed);
    }

    private async Task ConvertAfterDebounceAsync(string query, ParsedQuery parsed)
    {
        CancellationTokenSource debounceCts = new();
        CancellationTokenSource? previousDebounce = Interlocked.Exchange(ref _debounceCts, debounceCts);
        previousDebounce?.Cancel();
        previousDebounce?.Dispose();

        try
        {
            await Task.Delay(DebounceMilliseconds, debounceCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        await ConvertNowAsync(query, parsed).ConfigureAwait(false);
    }

    private async Task ConvertNowAsync(string query, ParsedQuery parsed)
    {
        CancellationTokenSource conversionCts = new();
        CancellationTokenSource? previousConversion = Interlocked.Exchange(ref _conversionCts, conversionCts);
        previousConversion?.Cancel();
        previousConversion?.Dispose();
        CancellationToken ct = conversionCts.Token;

        try
        {
            await _aliasManager.EnsureInitializedAsync().ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();

            FallbackConversionPair? pair = FallbackConversionSelector.TrySelect(
                parsed,
                _settings.LocalCurrency,
                _settings.Currencies,
                _aliasManager);
            ct.ThrowIfCancellationRequested();

            if (pair is null)
            {
                ShowOpenConverter(query);
                return;
            }

            FallbackConversionPair selected = pair.Value;

            try
            {
                _converter.ValidateConversionAPI();
            }
            catch (Exception ex)
            {
                ApplyFailure(query, ex.Message);
                return;
            }

            List<ConversionOutcome> outcomes = await _converter.GetConversionOutcomesAsync(
                parsed.Amount,
                selected.FromCurrency,
                selected.ToCurrency,
                ct).ConfigureAwait(false);

            ct.ThrowIfCancellationRequested();

            ConversionOutcome? success = outcomes.FirstOrDefault(o => o.IsSuccess);
            if (success is not null)
            {
                ApplyOutcome(success);
                return;
            }

            ApplyFailure(query, outcomes.FirstOrDefault()?.Item.Title);
        }
        catch (OperationCanceledException)
        {
            if (!ct.IsCancellationRequested)
            {
                ApplyFailure(query, null);
            }
        }
        catch (Exception)
        {
            if (!ct.IsCancellationRequested)
            {
                ApplyFailure(query, null);
            }
        }
    }

    private void ApplyOutcome(ConversionOutcome outcome)
    {
        Command = CurrencyConverter.CreateCopyCommand(outcome.ToFormatted);
        Title = outcome.Item.Title;
        Subtitle = ConverterSubtitle;
        Icon = outcome.Item.Icon ?? CurrencyIconManager.For(outcome.ToCurrency);
        MoreCommands = [_openConverterContextItem];
    }

    private void SetPlaceholder(decimal amount, FallbackConversionPair pair)
    {
        ShowPage();
        string fromFormatted = amount.ToString("N", CultureInfo.CurrentCulture);
        Title = $"{fromFormatted} {pair.FromCurrency.ToUpperInvariant()} → …";
        Subtitle = ConverterSubtitle;
        Icon = CurrencyIconManager.For(pair.ToCurrency);
        MoreCommands = [];
    }

    private void ApplyFailure(string query, string? errorTitle)
    {
        ShowPage();
        Title = ResolveFallbackFailureTitle(_settings.SuppressFallbackWarnings, query, errorTitle);
        Subtitle = ConverterSubtitle;
        Icon = IconManager.Icon;
        MoreCommands = [];
    }

    private void ShowOpenConverter(string query)
    {
        ShowPage();
        Title = FormatOpenConverterTitle(query);
        Subtitle = ConverterSubtitle;
        Icon = IconManager.Icon;
        MoreCommands = [];
    }

    internal static string FormatOpenConverterTitle(string query)
    {
        string trimmed = query.Trim();
        return $"Convert \"{trimmed}\" with Currency Converter";
    }

    internal static string ResolveFallbackFailureTitle(bool suppressWarnings, string query, string? errorTitle)
    {
        if (suppressWarnings)
        {
            return FormatOpenConverterTitle(query);
        }

        return string.IsNullOrWhiteSpace(errorTitle)
            ? "Something went wrong while converting currencies"
            : errorTitle;
    }

    private void Hide()
    {
        CancelPendingWork();
        ApplyHidden();
    }

    private void ApplyHidden()
    {
        // Global results rank this fallback like a top-level command. An empty
        // Title still surfaces the page Name ("Convert"), so swap to a no-op.
        Command = _hiddenCommand;
        Title = string.Empty;
        Subtitle = string.Empty;
        Icon = IconManager.Icon;
        MoreCommands = [];
        _page.ClearFallbackQuery();
    }

    private void ShowPage()
    {
        Command = _page;
    }

    private void CancelPendingWork()
    {
        CancellationTokenSource? previousDebounce = Interlocked.Exchange(ref _debounceCts, null);
        previousDebounce?.Cancel();
        previousDebounce?.Dispose();

        CancellationTokenSource? previousConversion = Interlocked.Exchange(ref _conversionCts, null);
        previousConversion?.Cancel();
        previousConversion?.Dispose();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        CancelPendingWork();
        GC.SuppressFinalize(this);
    }
}
