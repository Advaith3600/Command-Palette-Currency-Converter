using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.Storage;

namespace CurrencyConverterExtension.Helpers;

#pragma warning disable CA1001 // SemaphoreSlim lives for extension process lifetime
internal class PinnedConversionManager
{
    private const string PinFileName = "pinned_conversions.json";
    private readonly object _gate = new();
    private readonly object _initGate = new();
    private readonly SemaphoreSlim _saveLock = new(1, 1);
#pragma warning restore CA1001
    private List<PinnedConversion> _pins = [];
    private bool _initialized;
    private Task? _initTask;

    public bool IsInitialized => _initialized;

    public event Action? PinsChanged;

    public PinnedConversionManager()
    {
    }

    internal PinnedConversionManager(IEnumerable<PinnedConversion> pins)
    {
        _pins = NormalizeAndDedupe(pins);
        _initialized = true;
    }

    public Task EnsureInitializedAsync()
    {
        if (_initialized)
        {
            return Task.CompletedTask;
        }

        lock (_initGate)
        {
            return _initTask ??= InitializeCoreAsync();
        }
    }

    private async Task InitializeCoreAsync()
    {
        try
        {
            await LoadPinsAsync().ConfigureAwait(false);
            _initialized = true;
        }
        catch
        {
            lock (_initGate)
            {
                _initTask = null;
            }

            throw;
        }
    }

    private async Task LoadPinsAsync()
    {
        StorageFolder roamingFolder = ApplicationData.Current.RoamingFolder;
        StorageFile? pinFile = await roamingFolder.TryGetItemAsync(PinFileName) as StorageFile;

        if (pinFile == null)
        {
            lock (_gate)
            {
                _pins = [];
            }

            return;
        }

        string jsonText = await FileIO.ReadTextAsync(pinFile);
        List<PinnedConversion> loaded = ParsePinsJson(jsonText);
        lock (_gate)
        {
            _pins = loaded;
        }
    }

    internal static List<PinnedConversion> ParsePinsJson(string jsonText)
    {
        if (string.IsNullOrWhiteSpace(jsonText))
        {
            return [];
        }

        JsonArray array = JsonArray.Parse(jsonText);
        List<PinnedConversion> pins = [];

        foreach (IJsonValue value in array)
        {
            if (value.ValueType != JsonValueType.Object)
            {
                continue;
            }

            JsonObject obj = value.GetObject();
            if (!obj.ContainsKey("amount") || !obj.ContainsKey("from") || !obj.ContainsKey("to"))
            {
                continue;
            }

            double amountNumber = obj["amount"].GetNumber();
            string from = obj["from"].GetString();
            string to = obj["to"].GetString();

            if (string.IsNullOrWhiteSpace(from) || string.IsNullOrWhiteSpace(to))
            {
                continue;
            }

            pins.Add(new PinnedConversion(
                Convert.ToDecimal(amountNumber, CultureInfo.InvariantCulture),
                from.Trim().ToLowerInvariant(),
                to.Trim().ToLowerInvariant()));
        }

        return NormalizeAndDedupe(pins);
    }

    public List<PinnedConversion> GetAllPins()
    {
        lock (_gate)
        {
            return [.. _pins];
        }
    }

    public bool Contains(PinnedConversion pin)
    {
        lock (_gate)
        {
            return _pins.Contains(pin);
        }
    }

    public async Task AddPinAsync(PinnedConversion pin)
    {
        await _saveLock.WaitAsync().ConfigureAwait(false);
        try
        {
            PinnedConversion normalized = Normalize(pin);
            bool changed;
            lock (_gate)
            {
                if (_pins.Contains(normalized))
                {
                    changed = false;
                }
                else
                {
                    _pins.Add(normalized);
                    changed = true;
                }
            }

            if (changed)
            {
                await SavePinsAsync().ConfigureAwait(false);
                PinsChanged?.Invoke();
            }
        }
        finally
        {
            _saveLock.Release();
        }
    }

    public async Task RemovePinAsync(PinnedConversion pin)
    {
        await _saveLock.WaitAsync().ConfigureAwait(false);
        try
        {
            PinnedConversion normalized = Normalize(pin);
            bool removed;
            lock (_gate)
            {
                removed = _pins.Remove(normalized);
            }

            if (removed)
            {
                await SavePinsAsync().ConfigureAwait(false);
                PinsChanged?.Invoke();
            }
        }
        finally
        {
            _saveLock.Release();
        }
    }

    private async Task SavePinsAsync()
    {
        StorageFolder roamingFolder = ApplicationData.Current.RoamingFolder;
        StorageFile pinFile = await roamingFolder.CreateFileAsync(PinFileName, CreationCollisionOption.ReplaceExisting);
        await FileIO.WriteTextAsync(pinFile, GetPinsJson());
    }

    internal string GetPinsJson()
    {
        JsonArray array = [];
        List<PinnedConversion> snapshot;
        lock (_gate)
        {
            snapshot = [.. _pins];
        }

        foreach (PinnedConversion pin in snapshot)
        {
            JsonObject obj = new()
            {
                ["amount"] = JsonValue.CreateNumberValue(Convert.ToDouble(pin.Amount, CultureInfo.InvariantCulture)),
                ["from"] = JsonValue.CreateStringValue(pin.FromCurrency),
                ["to"] = JsonValue.CreateStringValue(pin.ToCurrency),
            };
            array.Add(obj);
        }

        return array.Stringify();
    }

    private static PinnedConversion Normalize(PinnedConversion pin) =>
        new(
            pin.Amount,
            pin.FromCurrency.Trim().ToLowerInvariant(),
            pin.ToCurrency.Trim().ToLowerInvariant());

    private static List<PinnedConversion> NormalizeAndDedupe(IEnumerable<PinnedConversion> pins) =>
        [.. pins.Select(Normalize).Distinct()];
}
