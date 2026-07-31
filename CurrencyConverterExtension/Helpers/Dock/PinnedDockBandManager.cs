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

internal sealed partial class PinnedDockBandManager : IDisposable
{
    private readonly CurrencyConverter _converter;
    private readonly PinnedConversionManager _pinManager;
    private readonly AliasManager _aliasManager;
    private readonly IconInfo _icon;
    private readonly ICommand _todaysRatesCommand;
    private readonly DockPinItemFactory _itemFactory;
    private readonly object _publishGate = new();

    private WrappedDockItem? _pinnedDockBand;
    private int _lastDockRatesDayNumber; // 0 = never stamped; otherwise DateOnly.DayNumber
    private PeriodicTimer? _dockDayTimer;
    private CancellationTokenSource? _dockDayCts;
    private CancellationTokenSource? _refreshCts;
    private int _dockDayMonitorStarted;
    private int _dirty = 1; // Start dirty so the first GetDockBands loads pins/rates.
    private int _disposed;

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

    internal void MarkDirty() => Interlocked.Exchange(ref _dirty, 1);

    internal ICommandItem[] GetDockBands()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

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

        DateOnly today = DateOnly.FromDateTime(DateTime.Now);
        bool needsDay = NeedsDailyRefresh(ReadLastRatesDay(), today);
        bool dirty = Volatile.Read(ref _dirty) != 0;

        if (dirty || needsDay)
        {
            _ = RefreshAsync();
        }

        return [_pinnedDockBand];
    }

    internal async Task RefreshAsync()
    {
        if (_pinnedDockBand is null || Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        CancellationTokenSource refreshCts = new();
        CancellationTokenSource? previous = Interlocked.Exchange(ref _refreshCts, refreshCts);
        previous?.Cancel();
        previous?.Dispose();
        CancellationToken ct = refreshCts.Token;

        try
        {
            await _pinManager.EnsureInitializedAsync().ConfigureAwait(false);
            await _aliasManager.EnsureInitializedAsync().ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();

            (IListItem[] items, bool allSucceeded) = await BuildDockBandItemsAsync(ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();

            lock (_publishGate)
            {
                if (ct.IsCancellationRequested || !ReferenceEquals(Volatile.Read(ref _refreshCts), refreshCts))
                {
                    return;
                }

                _pinnedDockBand.Items = items;
                Interlocked.Exchange(ref _dirty, 0);

                // Only stamp the calendar day after a fully successful full-band refresh
                // (or when there are no pins). Never stamp on placeholder-only failure.
                if (allSucceeded)
                {
                    WriteLastRatesDay(DateOnly.FromDateTime(DateTime.Now));
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Newer refresh or dispose.
        }
        catch (Exception)
        {
            // Keep whatever items are currently shown; do not stamp the day.
        }
        finally
        {
            if (ReferenceEquals(Volatile.Read(ref _refreshCts), refreshCts))
            {
                Interlocked.CompareExchange(ref _refreshCts, null, refreshCts);
                refreshCts.Dispose();
            }
        }
    }

    /// <summary>
    /// Refreshes all dock pins that share <paramref name="pin"/>'s from-currency.
    /// Returns true when the group was updated on screen.
    /// </summary>
    internal async Task<bool> RefreshGroupAsync(PinnedConversion pin)
    {
        if (_pinnedDockBand is null || Volatile.Read(ref _disposed) != 0)
        {
            return false;
        }

        CancellationTokenSource refreshCts = new();
        CancellationTokenSource? previous = Interlocked.Exchange(ref _refreshCts, refreshCts);
        previous?.Cancel();
        previous?.Dispose();
        CancellationToken ct = refreshCts.Token;

        try
        {
            await _pinManager.EnsureInitializedAsync().ConfigureAwait(false);
            await _aliasManager.EnsureInitializedAsync().ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();

            _converter.InvalidateCacheForFromCurrency(pin.FromCurrency);

            List<PinnedConversion> groupPins = [.. _pinManager.GetAllPins()
                .Where(p => string.Equals(p.FromCurrency, pin.FromCurrency, StringComparison.OrdinalIgnoreCase))];

            if (groupPins.Count == 0)
            {
                return false;
            }

            IListItem[] refreshed = await Task.WhenAll(
                groupPins.Select(p => _itemFactory.CreatePinnedDockItemAsync(p, ct))).ConfigureAwait(false);

            ct.ThrowIfCancellationRequested();

            Dictionary<string, IListItem> byId = [];
            foreach (IListItem item in refreshed)
            {
                string? id = item.Command?.Id;
                if (!string.IsNullOrEmpty(id))
                {
                    byId[id] = item;
                }
            }

            lock (_publishGate)
            {
                if (ct.IsCancellationRequested || !ReferenceEquals(Volatile.Read(ref _refreshCts), refreshCts))
                {
                    return false;
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
                // Do not stamp _lastDockRatesDay — this is only one from-currency group.
            }

            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (Exception)
        {
            // Keep whatever items are currently shown for this group.
            return false;
        }
        finally
        {
            if (ReferenceEquals(Volatile.Read(ref _refreshCts), refreshCts))
            {
                Interlocked.CompareExchange(ref _refreshCts, null, refreshCts);
                refreshCts.Dispose();
            }
        }
    }

    private async Task<(IListItem[] Items, bool AllSucceeded)> BuildDockBandItemsAsync(CancellationToken cancellationToken)
    {
        List<PinnedConversion> pins = _pinManager.GetAllPins();
        if (pins.Count == 0)
        {
            return ([_itemFactory.CreateEmptyPinsPlaceholder(_todaysRatesCommand)], true);
        }

        (IListItem Item, bool Succeeded)[] results = await Task.WhenAll(
            pins.Select(async pin =>
            {
                IListItem item = await _itemFactory.CreatePinnedDockItemAsync(pin, cancellationToken).ConfigureAwait(false);
                bool succeeded = item.Command is CopyTextCommand;
                return (item, succeeded);
            })).ConfigureAwait(false);

        IListItem[] items = [.. results.Select(r => r.Item)];
        bool allSucceeded = results.All(r => r.Succeeded);
        return (items, allSucceeded);
    }

    private DateOnly? ReadLastRatesDay()
    {
        int dayNumber = _lastDockRatesDayNumber;
        return dayNumber == 0 ? null : DateOnly.FromDayNumber(dayNumber);
    }

    private void WriteLastRatesDay(DateOnly day) =>
        _lastDockRatesDayNumber = day.DayNumber;

    private void EnsureDayMonitor()
    {
        if (Interlocked.Exchange(ref _dockDayMonitorStarted, 1) != 0)
        {
            return;
        }

        _dockDayCts = new CancellationTokenSource();
        PeriodicTimer timer = new(TimeSpan.FromHours(1));
        _dockDayTimer = timer;
        _ = RunDockDayMonitorAsync(timer, _dockDayCts.Token);

        SystemEvents.PowerModeChanged += OnPowerModeChanged;
    }

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Resume)
        {
            _ = CheckAndRefreshForNewDayAsync();
        }
    }

    private async Task RunDockDayMonitorAsync(PeriodicTimer timer, CancellationToken cancellationToken)
    {
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                await CheckAndRefreshForNewDayAsync().ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Extension process shutting down.
        }
        catch (ObjectDisposedException)
        {
            // Timer disposed during shutdown.
        }
    }

    private async Task CheckAndRefreshForNewDayAsync()
    {
        DateOnly today = DateOnly.FromDateTime(DateTime.Now);
        if (!NeedsDailyRefresh(ReadLastRatesDay(), today))
        {
            return;
        }

        _converter.InvalidateCacheFromPreviousDays(today);
        MarkDirty();
        await RefreshAsync().ConfigureAwait(false);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        if (_dockDayMonitorStarted != 0)
        {
            SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        }

        CancellationTokenSource? dayCts = Interlocked.Exchange(ref _dockDayCts, null);
        dayCts?.Cancel();
        dayCts?.Dispose();

        PeriodicTimer? timer = Interlocked.Exchange(ref _dockDayTimer, null);
        timer?.Dispose();

        CancellationTokenSource? refreshCts = Interlocked.Exchange(ref _refreshCts, null);
        refreshCts?.Cancel();
        refreshCts?.Dispose();

        Interlocked.Exchange(ref _dockDayMonitorStarted, 0);
    }
}
