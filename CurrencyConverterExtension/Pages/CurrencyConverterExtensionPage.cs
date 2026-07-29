// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using CurrencyConverterExtension.Converter;
using CurrencyConverterExtension.Helpers;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace CurrencyConverterExtension;

internal sealed partial class CurrencyConverterExtensionPage : DynamicListPage, IDisposable
{
    internal readonly SettingsManager _settings;
    internal readonly CurrencyConverter _converter;
    internal readonly AliasManager _aliasManager;

    internal const string GithubReadmeURL = "https://github.com/Advaith3600/Command-Palette-Currency-Converter?tab=readme-ov-file";

    private IListItem[] _items = [];
    private CancellationTokenSource? _debounceCts;
    private CancellationTokenSource? _conversionCts;

    public CurrencyConverterExtensionPage(SettingsManager settings, AliasManager aliasManager)
    {
        Icon = IconManager.Icon;
        Title = "Currency Converter";
        Name = "Convert";

        _settings = settings;
        _aliasManager = aliasManager;
        _converter = new(_settings, aliasManager);
    }

    public override IListItem[] GetItems()
    {
        if (SearchText.Length == 0)
        {
            return FallbackItems();
        }

        return _items;
    }

    private AnonymousCommand UpdateSearchCommand(string text)
    {
        return new AnonymousCommand(() =>
         {
             SearchText = text;
         })
        {
            Name = "Use",
            Result = CommandResult.KeepOpen()
        };
    }

    private IListItem[] FallbackItems()
    {
        return [
            new ListItem(new CurrencyConverterAliasPage(_aliasManager)) {
                Title = "Manage currency aliases",
                Subtitle = "View, create and remove your aliases",
                Icon = Icon,
            },

            new ListItem(new OpenUrlCommand(GithubReadmeURL))
            {
                Title = "Start typing to convert currencies",
                Subtitle = "Few examples are listed below",
                Icon = IconManager.Icon,
            },
            new ListItem(UpdateSearchCommand("100 USD to INR"))
            {
                Title = "100 USD to INR",
                Subtitle = "Convert 100 US Dollars to Indian Rupees",
                Icon = IconManager.Icon,
                MoreCommands = [
                    new CommandContextItem(new CopyTextCommand("100 USD to INR") {
                        Result = CommandResult.ShowToast(new ToastArgs()
                        {
                            Message = "Copied to clipboard",
                            Result = CommandResult.KeepOpen()
                        })
                    })
                ]
            },
            new ListItem(UpdateSearchCommand("$100 to €"))
            {
                Title = "$100 to €",
                Subtitle = "Convert 100 US Dollars to Euros",
                Icon = IconManager.Icon,
                MoreCommands = [
                    new CommandContextItem(new CopyTextCommand("$100 to €") {
                        Result = CommandResult.ShowToast(new ToastArgs()
                        {
                            Message = "Copied to clipboard",
                            Result = CommandResult.KeepOpen()
                        })
                    })
                ]
            },
            new ListItem(UpdateSearchCommand("₽100"))
            {
                Title = "₽100",
                Subtitle = "Convert 100 Russian Rubles",
                Icon = IconManager.Icon,
                MoreCommands = [
                    new CommandContextItem(new CopyTextCommand("₽100") {
                        Result = CommandResult.ShowToast(new ToastArgs()
                        {
                            Message = "Copied to clipboard",
                            Result = CommandResult.KeepOpen()
                        })
                    })
                ]
            },
        ];
    }

    private async Task<List<ListItem>> ParseQueryAsync(string search, CancellationToken cancellationToken)
    {
        var parseResult = QueryParser.Parse(search, _settings.DecimalSeparator);

        return parseResult.Status switch
        {
            QueryParseStatus.NoMatch => [],
            QueryParseStatus.InvalidExpression => [
                new ListItem(new NoOpCommand())
                {
                    Title = "Invalid expression provided",
                    Subtitle = "Please check your mathematical expression",
                    Icon = IconManager.WarningIcon,
                }
            ],
            QueryParseStatus.Success => await _converter.GetConversionResultsAsync(
                parseResult.Query!.Value.Amount,
                parseResult.Query.Value.FromCurrency,
                parseResult.Query.Value.ToCurrency,
                cancellationToken).ConfigureAwait(false),
            _ => [],
        };
    }

    public override void UpdateSearchText(string oldSearch, string newSearch)
    {
        if (oldSearch != newSearch)
        {
            _ = DebounceAndConvertAsync(newSearch);
        }
    }

    private async Task DebounceAndConvertAsync(string search)
    {
        CancellationTokenSource debounceCts = new();
        CancellationTokenSource? previousDebounce = Interlocked.Exchange(ref _debounceCts, debounceCts);
        previousDebounce?.Cancel();
        previousDebounce?.Dispose();

        try
        {
            await Task.Delay(300, debounceCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (string.IsNullOrEmpty(search))
        {
            IsLoading = false;
            _items = [];
            RaiseItemsChanged(0);
            return;
        }

        CancellationTokenSource conversionCts = new();
        CancellationTokenSource? previousConversion = Interlocked.Exchange(ref _conversionCts, conversionCts);
        previousConversion?.Cancel();
        previousConversion?.Dispose();
        CancellationToken ct = conversionCts.Token;

        IsLoading = true;
        RaiseItemsChanged(search.Length);

        try
        {
            await _aliasManager.EnsureInitializedAsync().ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();

            try
            {
                _converter.ValidateConversionAPI();
            }
            catch (Exception ex)
            {
                _items =
                [
                    new ListItem(new OpenUrlCommand(GithubReadmeURL))
                    {
                        Title = ex.Message,
                        Subtitle = "Press enter or click to see how to fix this issue",
                        Icon = IconManager.WarningIcon,
                    }
                ];
                return;
            }

            var results = await ParseQueryAsync(search, ct).ConfigureAwait(false);
            _items = [.. results
                .GroupBy(r => new { r.Title, r.Subtitle })
                .Select(g => g.First())];
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception)
        {
            _items =
            [
                new ListItem(new NoOpCommand())
                {
                    Title = "Something went wrong while converting currencies",
                    Subtitle = "Please try again",
                    Icon = IconManager.WarningIcon,
                }
            ];
        }
        finally
        {
            if (!ct.IsCancellationRequested)
            {
                IsLoading = false;
                RaiseItemsChanged(search.Length);
            }
        }
    }

    public void Dispose()
    {
        _debounceCts?.Cancel();
        _debounceCts?.Dispose();
        _conversionCts?.Cancel();
        _conversionCts?.Dispose();
        _converter.Dispose();
    }
}
