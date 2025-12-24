using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.Storage;

namespace CurrencyConverterExtension.Helpers
{
    internal class AliasManager
    {
        // Require at least one character; allow letters, numbers, currency symbols, and underscores.
        public const string KeyRegex = @"[\p{L}\p{Sc}_]*";

        private const string AliasFileName = "currency_alias.json";
        private Dictionary<string, string> aliases;

        public AliasManager()
        {
            aliases = new Dictionary<string, string>();
        }

        public bool ValidateKeyFormat(string key) => !string.IsNullOrWhiteSpace(key) && Regex.Match(key, KeyRegex).Success;

        public async Task InitializeAsync()
        {
            await EnsureAliasFileExistsAsync();
            await LoadAliasesAsync();
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
            foreach (var key in jsonObject.Keys)
            {
                aliases[key] = jsonObject[key].GetString();
            }
        }

        public bool HasAlias(string currencyCode) => aliases.ContainsKey(currencyCode);

        public string? GetAlias(string currencyCode)
        {
            if (aliases.TryGetValue(currencyCode, out string alias))
            {
                return alias;
            }
            return null;
        }

        public Dictionary<string, string> GetAllAliases() => aliases;

        public async Task SetAliasAsync(string currencyCode, string alias)
        {
            aliases[currencyCode] = alias;
            await SaveAliasesAsync();
        }

        public async Task RemoveAliasAsync(string currencyCode)
        {
            if (aliases.Remove(currencyCode))
            {
                await SaveAliasesAsync();
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
            foreach (var kvp in aliases.OrderBy(k => k.Key))
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
            await LoadAliasesAsync();
        }

        public async Task<string> ExportAliasesAsync()
        {
            string fileName = $"currency_alias_export_{DateTime.UtcNow:yyyyMMddHHmmss}.json";
            StorageFolder targetFolder = ApplicationData.Current.LocalFolder;
            StorageFile targetFile = await targetFolder.CreateFileAsync(fileName, CreationCollisionOption.ReplaceExisting);
            await FileIO.WriteTextAsync(targetFile, GetAliasesJson());
            return targetFile.Path;
        }
    }
}
