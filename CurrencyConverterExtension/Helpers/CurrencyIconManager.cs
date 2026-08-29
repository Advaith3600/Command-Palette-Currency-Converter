using Microsoft.CommandPalette.Extensions.Toolkit;
using System;
using System.Collections.Generic;
using System.IO;

namespace CurrencyConverterExtension.Helpers
{
    /// <summary>
    /// Resolves currency codes to packaged icons: crypto WebPs first, then fiat flags
    /// named by ISO currency code, then the converter logo. No runtime network or maps.
    /// </summary>
    internal static class CurrencyIconManager
    {
        private const string FlagsFolder = "Assets\\Flags";
        private const string CryptoFolder = "Assets\\Crypto";

        private static readonly object Gate = new();
        private static readonly Dictionary<string, IconInfo> IconCache = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Returns a crypto or flag <see cref="IconInfo"/> for the currency code,
        /// or the app logo when no packaged asset exists.
        /// </summary>
        public static IconInfo For(string? currencyCode)
        {
            if (string.IsNullOrWhiteSpace(currencyCode))
            {
                return IconManager.Icon;
            }

            string key = NormalizeKey(currencyCode);
            lock (Gate)
            {
                if (IconCache.TryGetValue(key, out IconInfo? cached))
                {
                    return cached;
                }

                IconInfo icon = ResolveIcon(key);
                IconCache[key] = icon;
                return icon;
            }
        }

        internal static string ToRelativeCryptoPath(string currencyCode) =>
            Path.Combine(CryptoFolder, $"{NormalizeKey(currencyCode)}.webp");

        internal static string ToRelativeFlagPath(string currencyCode) =>
            Path.Combine(FlagsFolder, $"{NormalizeKey(currencyCode)}.webp");

        private static IconInfo ResolveIcon(string currencyCode)
        {
            string cryptoRelative = ToRelativeCryptoPath(currencyCode);
            if (AssetExists(cryptoRelative))
            {
                return IconHelpers.FromRelativePath(cryptoRelative);
            }

            string flagRelative = ToRelativeFlagPath(currencyCode);
            if (AssetExists(flagRelative))
            {
                return IconHelpers.FromRelativePath(flagRelative);
            }

            return IconManager.Icon;
        }

        private static bool AssetExists(string relativePath)
        {
            string fullPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, relativePath);
            return File.Exists(fullPath);
        }

        private static string NormalizeKey(string key) => key.Trim().ToLowerInvariant();
    }
}
