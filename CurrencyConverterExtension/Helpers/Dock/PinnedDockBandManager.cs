using CurrencyConverterExtension.Converter;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using System.Threading;
using System.Threading.Tasks;

namespace CurrencyConverterExtension.Helpers;

internal sealed partial class PinnedDockBandManager : IDisposable
{
    private static readonly TimeSpan ResumeNetworkSettleDelay = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan PeriodicRefreshInterval = TimeSpan.FromHours(1);

    // Seconds between failed-refresh retries: 5s → 15s → 30s → 60s → 2m → 5m (cap).
    private static readonly int[] RetryDelaySeconds = [5, 15, 30, 60, 120, 300];

    private readonly CurrencyConverter _converter;
    private readonly PinnedConversionManager _pinManager;
    private readonly AliasManager _aliasManager;
    private readonly IconInfo _icon;
    private readonly ICommand _openConverterCommand;
    private readonly DockPinItemFactory _itemFactory;
    private readonly object _publishGate = new();

    private WrappedDockItem? _pinnedDockBand;
    private PeriodicTimer? _dockRefreshTimer;
    private CancellationTokenSource? _dockMonitorCts;
    private CancellationTokenSource? _refreshCts;
    private CancellationTokenSource? _retryCts;
    private int _retryAttempt;
    private int _dockMonitorStarted;
    private int _dirty = 1; // Start dirty so the first GetDockBands loads pins/rates.
    private int _disposed;

    internal PinnedDockBandManager(
        CurrencyConverter converter,
        PinnedConversionManager pinManager,
        AliasManager aliasManager,
        IconInfo icon,
        ICommand openConverterCommand)
    {
        _converter = converter;
        _pinManager = pinManager;
        _aliasManager = aliasManager;
        _icon = icon;
        _openConverterCommand = openConverterCommand;
        _itemFactory = new DockPinItemFactory(converter, pinManager, icon, RefreshGroupAsync);
    }

    internal ICommandItem? Band => _pinnedDockBand;

    /// <summary>
    /// When a refresh fails, keep the on-screen band if it already shows real conversions.
    /// </summary>
    internal static bool ShouldKeepPreviousItems(bool allSucceeded, bool currentHasSuccessfulItems) =>
        !allSucceeded && currentHasSuccessfulItems;

    /// <summary>
    /// Exponential-ish backoff for failed dock refreshes. <paramref name="attempt"/> is 1-based.
    /// </summary>
    internal static TimeSpan NextRetryDelay(int attempt)
    {
        int index = Math.Clamp(attempt - 1, 0, RetryDelaySeconds.Length - 1);
        return TimeSpan.FromSeconds(RetryDelaySeconds[index]);
    }

    internal static bool HasSuccessfulConversionItems(IListItem[]? items) =>
        items is not null && items.Any(static i => i.Command is CopyTextCommand);

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
        }

        if (Volatile.Read(ref _dirty) != 0)
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

        CancelPendingRetry();

        CancellationTokenSource refreshCts = new();
        CancellationTokenSource? previous = Interlocked.Exchange(ref _refreshCts, refreshCts);
        previous?.Cancel();
        previous?.Dispose();
        CancellationToken ct = refreshCts.Token;

        bool scheduleRetry = false;
        bool hasPins = false;

        try
        {
            await _pinManager.EnsureInitializedAsync().ConfigureAwait(false);
            await _aliasManager.EnsureInitializedAsync().ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();

            hasPins = _pinManager.GetAllPins().Count > 0;
            SyncBackgroundMonitor(hasPins);

            (IListItem[] items, bool allSucceeded) = await BuildDockBandItemsAsync(ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();

            lock (_publishGate)
            {
                if (ct.IsCancellationRequested || !ReferenceEquals(Volatile.Read(ref _refreshCts), refreshCts))
                {
                    return;
                }

                bool keepPrevious = ShouldKeepPreviousItems(
                    allSucceeded,
                    HasSuccessfulConversionItems(_pinnedDockBand.Items));

                if (!keepPrevious)
                {
                    _pinnedDockBand.Items = items;
                }

                if (allSucceeded)
                {
                    Interlocked.Exchange(ref _dirty, 0);
                    Interlocked.Exchange(ref _retryAttempt, 0);
                }
                else
                {
                    Interlocked.Exchange(ref _dirty, 1);
                    scheduleRetry = hasPins;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Newer refresh or dispose.
        }
        catch (Exception)
        {
            // Keep whatever items are currently shown.
            Interlocked.Exchange(ref _dirty, 1);
            scheduleRetry = hasPins;
        }
        finally
        {
            if (ReferenceEquals(Volatile.Read(ref _refreshCts), refreshCts))
            {
                Interlocked.CompareExchange(ref _refreshCts, null, refreshCts);
                refreshCts.Dispose();
            }

            if (scheduleRetry && Volatile.Read(ref _disposed) == 0)
            {
                ScheduleRetry();
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

        CancelPendingRetry();

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
            return ([_itemFactory.CreateEmptyPinsPlaceholder(_openConverterCommand)], true);
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

    /// <summary>
    /// Background refresh (hourly / resume / network) only runs while there are pinned conversions.
    /// </summary>
    private void SyncBackgroundMonitor(bool hasPins)
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        if (hasPins)
        {
            EnsureMonitor();
        }
        else
        {
            StopBackgroundMonitor();
        }
    }

    private void EnsureMonitor()
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        if (Interlocked.Exchange(ref _dockMonitorStarted, 1) != 0)
        {
            return;
        }

        _dockMonitorCts = new CancellationTokenSource();
        PeriodicTimer timer = new(PeriodicRefreshInterval);
        _dockRefreshTimer = timer;
        _ = RunPeriodicRefreshAsync(timer, _dockMonitorCts.Token);

        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        NetworkChange.NetworkAvailabilityChanged += OnNetworkAvailabilityChanged;
    }

    private void StopBackgroundMonitor()
    {
        if (Interlocked.CompareExchange(ref _dockMonitorStarted, 0, 1) != 1)
        {
            return;
        }

        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        NetworkChange.NetworkAvailabilityChanged -= OnNetworkAvailabilityChanged;

        CancellationTokenSource? monitorCts = Interlocked.Exchange(ref _dockMonitorCts, null);
        monitorCts?.Cancel();
        monitorCts?.Dispose();

        PeriodicTimer? timer = Interlocked.Exchange(ref _dockRefreshTimer, null);
        timer?.Dispose();

        CancelPendingRetry();
    }

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Resume)
        {
            _ = RefreshAfterResumeAsync();
        }
    }

    private void OnNetworkAvailabilityChanged(object? sender, NetworkAvailabilityEventArgs e)
    {
        if (!e.IsAvailable || Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        MarkDirty();
        _ = RefreshAsync();
    }

    private async Task RefreshAfterResumeAsync()
    {
        CancellationToken cancellationToken = _dockMonitorCts?.Token ?? CancellationToken.None;

        try
        {
            await Task.Delay(ResumeNetworkSettleDelay, cancellationToken).ConfigureAwait(false);

            if (Volatile.Read(ref _disposed) != 0)
            {
                return;
            }

            MarkDirty();
            await RefreshAsync().ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Extension process shutting down.
        }
    }

    private async Task RunPeriodicRefreshAsync(PeriodicTimer timer, CancellationToken cancellationToken)
    {
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                MarkDirty();
                await RefreshAsync().ConfigureAwait(false);
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

    private void ScheduleRetry()
    {
        if (Volatile.Read(ref _disposed) != 0
            || _pinnedDockBand is null
            || _pinManager.GetAllPins().Count == 0)
        {
            return;
        }

        int attempt = Interlocked.Increment(ref _retryAttempt);
        TimeSpan delay = NextRetryDelay(attempt);

        CancellationTokenSource retryCts = new();
        CancellationTokenSource? previous = Interlocked.Exchange(ref _retryCts, retryCts);
        previous?.Cancel();
        previous?.Dispose();

        _ = RunScheduledRetryAsync(delay, retryCts);
    }

    private async Task RunScheduledRetryAsync(TimeSpan delay, CancellationTokenSource retryCts)
    {
        try
        {
            await Task.Delay(delay, retryCts.Token).ConfigureAwait(false);

            if (Volatile.Read(ref _disposed) != 0)
            {
                return;
            }

            MarkDirty();
            await RefreshAsync().ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Newer refresh/retry or dispose.
        }
        finally
        {
            if (ReferenceEquals(Volatile.Read(ref _retryCts), retryCts))
            {
                Interlocked.CompareExchange(ref _retryCts, null, retryCts);
                retryCts.Dispose();
            }
        }
    }

    private void CancelPendingRetry()
    {
        CancellationTokenSource? retryCts = Interlocked.Exchange(ref _retryCts, null);
        if (retryCts is null)
        {
            return;
        }

        retryCts.Cancel();
        retryCts.Dispose();
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        StopBackgroundMonitor();

        CancellationTokenSource? refreshCts = Interlocked.Exchange(ref _refreshCts, null);
        refreshCts?.Cancel();
        refreshCts?.Dispose();
    }
}
