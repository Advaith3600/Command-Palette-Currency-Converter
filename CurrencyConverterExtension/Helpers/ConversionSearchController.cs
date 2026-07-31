using CurrencyConverterExtension.Converter;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace CurrencyConverterExtension.Helpers;

/// <summary>
/// Shared debounce + convert pipeline for the main converter and Today's rates search boxes.
/// </summary>
internal sealed partial class ConversionSearchController : IDisposable
{
    private readonly SettingsManager _settings;
    private readonly AliasManager _aliasManager;
    private readonly PinnedConversionManager _pinManager;
    private readonly CurrencyConverter _converter;
    private readonly Func<ParsedQuery, CancellationToken, Task<IListItem[]>> _buildItemsAsync;
    private readonly Action<IListItem[]> _setItems;
    private readonly Action<bool> _setLoading;
    private readonly Action<int> _raiseItemsChanged;

    private CancellationTokenSource? _debounceCts;
    private CancellationTokenSource? _conversionCts;

    internal ConversionSearchController(
        SettingsManager settings,
        AliasManager aliasManager,
        PinnedConversionManager pinManager,
        CurrencyConverter converter,
        Func<ParsedQuery, CancellationToken, Task<IListItem[]>> buildItemsAsync,
        Action<IListItem[]> setItems,
        Action<bool> setLoading,
        Action<int> raiseItemsChanged)
    {
        _settings = settings;
        _aliasManager = aliasManager;
        _pinManager = pinManager;
        _converter = converter;
        _buildItemsAsync = buildItemsAsync;
        _setItems = setItems;
        _setLoading = setLoading;
        _raiseItemsChanged = raiseItemsChanged;
    }

    internal void CancelPendingWork()
    {
        CancellationTokenSource? previousDebounce = Interlocked.Exchange(ref _debounceCts, null);
        previousDebounce?.Cancel();
        previousDebounce?.Dispose();

        CancellationTokenSource? previousConversion = Interlocked.Exchange(ref _conversionCts, null);
        previousConversion?.Cancel();
        previousConversion?.Dispose();
    }

    internal async Task DebounceAndConvertAsync(string search)
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

        await ConvertNowAsync(search).ConfigureAwait(false);
    }

    internal async Task ConvertNowAsync(string search)
    {
        if (string.IsNullOrEmpty(search))
        {
            _setLoading(false);
            _setItems([]);
            _raiseItemsChanged(0);
            return;
        }

        CancellationTokenSource conversionCts = new();
        CancellationTokenSource? previousConversion = Interlocked.Exchange(ref _conversionCts, conversionCts);
        previousConversion?.Cancel();
        previousConversion?.Dispose();
        CancellationToken ct = conversionCts.Token;

        _setLoading(true);
        _raiseItemsChanged(search.Length);

        try
        {
            await _aliasManager.EnsureInitializedAsync().ConfigureAwait(false);
            await _pinManager.EnsureInitializedAsync().ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();

            try
            {
                _converter.ValidateConversionAPI();
            }
            catch (Exception ex)
            {
                _setItems(
                [
                    new ListItem(new OpenUrlCommand(CurrencyConverterExtensionPage.GithubReadmeURL))
                    {
                        Title = ex.Message,
                        Subtitle = "Press enter or click to see how to fix this issue",
                        Icon = IconManager.WarningIcon,
                    }
                ]);
                return;
            }

            QueryParseResult parseResult = QueryParser.Parse(search, _settings.DecimalSeparator);
            IListItem[] items = parseResult.Status switch
            {
                QueryParseStatus.NoMatch => [],
                QueryParseStatus.InvalidExpression =>
                [
                    new ListItem(new NoOpCommand())
                    {
                        Title = "Invalid expression provided",
                        Subtitle = "Please check your mathematical expression",
                        Icon = IconManager.WarningIcon,
                    }
                ],
                QueryParseStatus.Success => await _buildItemsAsync(parseResult.Query!.Value, ct).ConfigureAwait(false),
                _ => [],
            };
            _setItems(items);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception)
        {
            _setItems(
            [
                new ListItem(new NoOpCommand())
                {
                    Title = "Something went wrong while converting currencies",
                    Subtitle = "Please try again",
                    Icon = IconManager.WarningIcon,
                }
            ]);
        }
        finally
        {
            if (!ct.IsCancellationRequested)
            {
                _setLoading(false);
                _raiseItemsChanged(search.Length);
            }
        }
    }

    public void Dispose() => CancelPendingWork();
}