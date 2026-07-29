using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.Storage;

namespace CurrencyConverterExtension.Helpers
{
    internal class AliasManager
    {
        // Character class for currency/alias tokens in queries and forms.
        // Uses * (zero or more) so QueryParser can treat "to" as optional.
        // Match is intentionally unanchored — keys may appear as prefixes within larger strings.
        public const string KeyRegex = @"[\p{L}\p{Sc}_]*";

        private const string ValidationKeyRegex = @"^[\p{L}\p{Sc}_]+$";

        private const string AliasFileName = "currency_alias.json";
        private readonly object _gate = new();
        private readonly object _initGate = new();
        private Dictionary<string, string> aliases;
        private bool _initialized;
        private Task? _initTask;

        public bool IsInitialized => _initialized;

        public AliasManager()
        {
            aliases = new Dictionary<string, string>();
        }

        internal AliasManager(Dictionary<string, string> aliases)
        {
            this.aliases = aliases.ToDictionary(
                kvp => NormalizeKey(kvp.Key),
                kvp => kvp.Value,
                StringComparer.Ordinal);
            _initialized = true;
        }

        public bool ValidateKeyFormat(string key) => !string.IsNullOrWhiteSpace(key) && Regex.IsMatch(key, ValidationKeyRegex);

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
                await InitializeAsync().ConfigureAwait(false);
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

        public async Task InitializeAsync()
        {
            await EnsureAliasFileExistsAsync().ConfigureAwait(false);
            await LoadAliasesAsync().ConfigureAwait(false);
        }

        private async Task EnsureAliasFileExistsAsync()
        {
            StorageFolder roamingFolder = ApplicationData.Current.RoamingFolder;
            StorageFile aliasFile = await roamingFolder.TryGetItemAsync(AliasFileName) as StorageFile;

            if (aliasFile == null)
            {
                // Place the included alias file to the roaming folder
                StorageFile defaultAliasFile = await StorageFile.GetFileFromApplicationUriAsync(new Uri("ms-appx:///alias.default.json"));
                await defaultAliasFile.CopyAsync(roamingFolder, AliasFileName, NameCollisionOption.ReplaceExisting);
            }
        }

        private async Task LoadAliasesAsync()
        {
            StorageFolder roamingFolder = ApplicationData.Current.RoamingFolder;
            StorageFile aliasFile = await roamingFolder.GetFileAsync(AliasFileName);
            string jsonText = await FileIO.ReadTextAsync(aliasFile);

            JsonObject jsonObject = JsonObject.Parse(jsonText);
            lock (_gate)
            {
                aliases.Clear();
                foreach (var key in jsonObject.Keys)
                {
                    aliases[NormalizeKey(key)] = jsonObject[key].GetString();
                }
            }
        }

        public bool HasAlias(string currencyCode)
        {
            lock (_gate)
            {
                return aliases.ContainsKey(NormalizeKey(currencyCode));
            }
        }

        public string? GetAlias(string currencyCode)
        {
            lock (_gate)
            {
                if (aliases.TryGetValue(NormalizeKey(currencyCode), out string alias))
                {
                    return alias;
                }
            }

            return null;
        }

        public Dictionary<string, string> GetAllAliases()
        {
            lock (_gate)
            {
                return new Dictionary<string, string>(aliases);
            }
        }

        public async Task SetAliasAsync(string currencyCode, string alias)
        {
            lock (_gate)
            {
                aliases[NormalizeKey(currencyCode)] = alias;
            }

            await SaveAliasesAsync().ConfigureAwait(false);
        }

        public async Task RemoveAliasAsync(string currencyCode)
        {
            bool removed;
            lock (_gate)
            {
                removed = aliases.Remove(NormalizeKey(currencyCode));
            }

            if (removed)
            {
                await SaveAliasesAsync().ConfigureAwait(false);
            }
        }

        private async Task SaveAliasesAsync()
        {
            StorageFolder roamingFolder = ApplicationData.Current.RoamingFolder;
            StorageFile aliasFile = await roamingFolder.CreateFileAsync(AliasFileName, CreationCollisionOption.ReplaceExisting);

            string jsonText = GetAliasesJson();
            await FileIO.WriteTextAsync(aliasFile, jsonText);
        }

        private string GetAliasesJson()
        {
            JsonObject jsonObject = new JsonObject();
            List<KeyValuePair<string, string>> ordered;
            lock (_gate)
            {
                ordered = aliases.OrderBy(k => k.Key).ToList();
            }

            foreach (var kvp in ordered)
            {
                jsonObject[kvp.Key] = JsonValue.CreateStringValue(kvp.Value);
            }

            return jsonObject.Stringify();
        }

        public async Task ResetToDefaultAsync()
        {
            StorageFolder roamingFolder = ApplicationData.Current.RoamingFolder;
            StorageFile defaultAliasFile = await StorageFile.GetFileFromApplicationUriAsync(new Uri("ms-appx:///alias.default.json"));
            await defaultAliasFile.CopyAsync(roamingFolder, AliasFileName, NameCollisionOption.ReplaceExisting);
            await LoadAliasesAsync().ConfigureAwait(false);
        }

        public async Task<string> ExportAliasesAsync()
        {
            string fileName = $"currency_alias_export_{DateTime.UtcNow:yyyyMMddHHmmss}.json";
            string pathUser = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string pathDownload = Path.Combine(pathUser, "Downloads");
            Directory.CreateDirectory(pathDownload);
            string filePath = Path.Combine(pathDownload, fileName);
            await File.WriteAllTextAsync(filePath, GetAliasesJson()).ConfigureAwait(false);
            return filePath;
        }

        private static string NormalizeKey(string key) => key.ToLowerInvariant();
    }
}
