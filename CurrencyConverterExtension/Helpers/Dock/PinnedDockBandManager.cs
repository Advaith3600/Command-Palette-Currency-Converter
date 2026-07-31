using CurrencyConverterExtension.Converter;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace CurrencyConverterExtension.Helpers;

internal sealed class PinnedDockBandManager
{
    private readonly CurrencyConverter _converter;
    private readonly PinnedConversionManager _pinManager;
    private readonly AliasManager _aliasManager;
    private readonly IconInfo _icon;
    private readonly ICommand _todaysRatesCommand;
    private readonly DockPinItemFactory _itemFactory;

    private WrappedDockItem? _pinnedDockBand;
    private int _dockRefreshVersion;
    private DateOnly? _lastDockRatesDay;
    private PeriodicTimer? _dockDayTimer;
    private CancellationTokenSource? _dockDayCts;
    private int _dockDayMonitorStarted;

    internal PinnedDockBandManager(
        CurrencyConverter converter,
        PinnedConversionManager pinManager,
        AliasManager aliasManager,
        IconInfo icon,
        ICommand todaysRatesCommand)
    {
        _converter = converter;
        _pinManager = pinManager;
        _aliasManager = aliasManager;
        _icon = icon;
        _todaysRatesCommand = todaysRatesCommand;
        _itemFactory = new DockPinItemFactory(converter, pinManager, icon, RefreshGroupAsync);
    }

    internal ICommandItem? Band => _pinnedDockBand;

    /// <summary>
    /// Returns true when dock rates should be refreshed for a new local calendar day
    /// (including when no successful refresh has been stamped yet).
    /// </summary>
    internal static bool NeedsDailyRefresh(DateOnly? lastRatesDay, DateOnly today) =>
        lastRatesDay != today;

    internal ICommandItem[] GetDockBands()
    {
        if (_pinnedDockBand is null)
        {
            _pinnedDockBand = new WrappedDockItem(
                [],
                "CurrencyConverter.Dock.Pinned",
                "Currency pins")
            {
                Icon = _icon,
            };
            EnsureDayMonitor();
        }

        // Pins and rates load async; update Items when ready (and on later GetDockBands calls).
        _ = RefreshAsync();
        return [_pinnedDockBand];
    }

    internal async Task RefreshAsync()
    {
        // Band may not exist yet if the host has not asked for dock bands.
        if (_pinnedDockBand is null)
        {
            return;
        }

        int version = Interlocked.Increment(ref _dockRefreshVersion);

        try
        {
            await _pinManager.EnsureInitializedAsync().ConfigureAwait(false);
            await _aliasManager.EnsureInitializedAsync().ConfigureAwait(false);

            IListItem[] items = await BuildDockBandItemsAsync().ConfigureAwait(false);

            // A newer refresh started while we were loading — drop this result.
            if (version != Volatile.Read(ref _dockRefreshVersion))
            {
                return;
            }

            _pinnedDockBand.Items = items;
            _lastDockRatesDay = DateOnly.FromDateTime(DateTime.Now);
        }
        catch (Exception)
        {
            // Keep whatever items are currently shown; do not stamp the day.
        }
    }

    internal async Task RefreshGroupAsync(PinnedConversion pin)
    {
        if (_pinnedDockBand is null)
        {
            return;
        }

        int version = Interlocked.Increment(ref _dockRefreshVersion);

        try
        {
            await _pinManager.EnsureInitializedAsync().ConfigureAwait(false);
            await _aliasManager.EnsureInitializedAsync().ConfigureAwait(false);

            _converter.InvalidateCacheForFromCurrency(pin.FromCurrency);

            List<PinnedConversion> groupPins = [.. _pinManager.GetAllPins()
                .Where(p => string.Equals(p.FromCurrency, pin.FromCurrency, StringComparison.OrdinalIgnoreCase))];

            if (groupPins.Count == 0)
            {
                return;
            }

            IListItem[] refreshed = await PinnedConversionFetchHelper.FetchGroupedByFromCurrencyAsync(
                groupPins,
                _itemFactory.CreatePinnedDockItemAsync).ConfigureAwait(false);

            if (version != Volatile.Read(ref _dockRefreshVersion))
            {
                return;
            }

            Dictionary<string, IListItem> byId = [];
            foreach (IListItem item in refreshed)
            {
                string? id = item.Command?.Id;
                if (!string.IsNullOrEmpty(id))
                {
                    byId[id] = item;
                }
            }

            IListItem[] current = _pinnedDockBand.Items ?? [];
            IListItem[] merged = new IListItem[current.Length];
            for (int i = 0; i < current.Length; i++)
            {
                string? id = current[i].Command?.Id;
                merged[i] = id is not null && byId.TryGetValue(id, out IListItem? updated)
                    ? updated
                    : current[i];
            }

            _pinnedDockBand.Items = merged;
            _lastDockRatesDay = DateOnly.FromDateTime(DateTime.Now);
        }
        catch (Exception)
        {
            // Keep whatever items are currently shown for this group.
        }
    }

    private async Task<IListItem[]> BuildDockBandItemsAsync()
    {
        List<PinnedConversion> pins = _pinManager.GetAllPins();
        if (pins.Count == 0)
        {
            return [_itemFactory.CreateEmptyPinsPlaceholder(_todaysRatesCommand)];
        }

        return await PinnedConversionFetchHelper.FetchGroupedByFromCurrencyAsync(
            pins,
            _itemFactory.CreatePinnedDockItemAsync).ConfigureAwait(false);
    }

    private void EnsureDayMonitor()
    {
        if (Interlocked.Exchange(ref _dockDayMonitorStarted, 1) != 0)
        {
            return;
        }

        _dockDayCts = new CancellationTokenSource();
        _dockDayTimer = new PeriodicTimer(TimeSpan.FromHours(1));
        _ = RunDockDayMonitorAsync(_dockDayCts.Token);

        SystemEvents.PowerModeChanged += OnPowerModeChanged;
    }

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Resume)
        {
            _ = CheckAndRefreshForNewDayAsync();
        }
    }

    private async Task RunDockDayMonitorAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (_dockDayTimer is not null &&
                   await _dockDayTimer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                await CheckAndRefreshForNewDayAsync().ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Extension process shutting down.
        }
    }

    private async Task CheckAndRefreshForNewDayAsync()
    {
        DateOnly today = DateOnly.FromDateTime(DateTime.Now);
        if (!NeedsDailyRefresh(_lastDockRatesDay, today))
        {
            return;
        }

        _converter.InvalidateCacheFromPreviousDays(today);
        await RefreshAsync().ConfigureAwait(false);
    }
}