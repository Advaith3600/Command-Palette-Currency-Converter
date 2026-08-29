// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.

using CurrencyConverterExtension.Converter;
using CurrencyConverterExtension.Helpers;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace CurrencyConverterExtension.Commands;

internal sealed partial class CurrencyConverterFallbackItem : FallbackCommandItem, IDisposable
{
    internal const string FallbackId = "CurrencyConverter.Fallback.Convert";
    private const string FallbackDisplayTitle = "Convert with Currency Converter";
    private const string ConverterSubtitle = "Currency Converter";
    // CmdPal still calls UpdateQuery on essentially every keystroke (~50ms).
    // Debounce only the network path; cache hits stay immediate.
    private const int DebounceMilliseconds = 300;

    // Stable Id so CmdPal can match "Include in global results" after Command swaps.
    public override string Id => FallbackId;

    private readonly CurrencyConverterExtensionPage _page;
    private readonly SettingsManager _settings;
    private readonly AliasManager _aliasManager;
    private readonly CurrencyConverter _converter;
    private readonly NoOpCommand _hiddenCommand = new() { Name = string.Empty, Id = FallbackId };
    private readonly CommandContextItem _openConverterContextItem;

    private CancellationTokenSource? _conversionCts;
    private int _queryVersion;
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

        ConvertNow(trimmed, parsed);
    }

    private void ConvertNow(string query, ParsedQuery parsed)
    {
        int version = Interlocked.Increment(ref _queryVersion);
        CancellationTokenSource conversionCts = new();
        CancellationTokenSource? previousConversion = Interlocked.Exchange(ref _conversionCts, conversionCts);
        previousConversion?.Cancel();
        previousConversion?.Dispose();
        CancellationToken ct = conversionCts.Token;

        try
        {
            WaitFor(_aliasManager.EnsureInitializedAsync());
            if (IsSuperseded(version, ct))
            {
                return;
            }

            FallbackConversionPair? pair = FallbackConversionSelector.TrySelect(
                parsed,
                _settings.LocalCurrency,
                _settings.Currencies,
                _aliasManager);
            if (IsSuperseded(version, ct))
            {
                return;
            }

            if (pair is null)
            {
                ShowOpenConverter(query);
                return;
            }

            FallbackConversionPair selected = pair.Value;
            if (TryApplyCached(parsed.Amount, selected))
            {
                return;
            }

            WaitFor(Task.Delay(DebounceMilliseconds, ct));
            if (IsSuperseded(version, ct))
            {
                return;
            }

            if (TryApplyCached(parsed.Amount, selected))
            {
                return;
            }

            try
            {
                _converter.ValidateConversionAPI();
            }
            catch (Exception ex)
            {
                if (!IsSuperseded(version, ct))
                {
                    ApplyFailure(query, ex.Message);
                }

                return;
            }

            List<ConversionOutcome> outcomes = WaitFor(_converter.GetConversionOutcomesAsync(
                parsed.Amount,
                selected.FromCurrency,
                selected.ToCurrency,
                ct));
            if (IsSuperseded(version, ct))
            {
                return;
            }

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
            if (!IsSuperseded(version, ct))
            {
                ApplyFailure(query, null);
            }
        }
        catch (Exception)
        {
            if (!IsSuperseded(version, ct))
            {
                ApplyFailure(query, null);
            }
        }
    }

    private bool TryApplyCached(decimal amount, FallbackConversionPair pair)
    {
        if (_converter.TryConvertFromCache(amount, pair.FromCurrency, pair.ToCurrency, out ConversionOutcome? cached)
            && cached is { IsSuccess: true })
        {
            ApplyOutcome(cached);
            return true;
        }

        return false;
    }

    private bool IsSuperseded(int version, CancellationToken ct) =>
        ct.IsCancellationRequested || version != Volatile.Read(ref _queryVersion);

    private static void WaitFor(Task task) => task.GetAwaiter().GetResult();

    private static T WaitFor<T>(Task<T> task) => task.GetAwaiter().GetResult();

    private void ApplyOutcome(ConversionOutcome outcome)
    {
        AssignCommand(CurrencyConverter.CreateCopyCommand(outcome.ToFormatted));
        Title = outcome.Item.Title;
        Subtitle = ConverterSubtitle;
        Icon = outcome.Item.Icon ?? CurrencyIconManager.For(outcome.ToCurrency);
        MoreCommands = [_openConverterContextItem];
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
        AssignCommand(_page);
    }

    /// <summary>
    /// CmdPal's "Include in global results" looks up <see cref="ICommand.Id"/>.
    /// Replacing Command with a new copy/page instance that has a different Id
    /// drops the row into the Fallbacks section even when that setting is on.
    /// </summary>
    private void AssignCommand(ICommand command)
    {
        if (command is Command toolkitCommand)
        {
            toolkitCommand.Id = FallbackId;
        }

        Command = command;
    }

    private void CancelPendingWork()
    {
        Interlocked.Increment(ref _queryVersion);
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
