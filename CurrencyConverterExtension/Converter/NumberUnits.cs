using System;
using System.Globalization;
using System.Linq;

namespace CurrencyConverterExtension.Converter;

/// <summary>
/// Magnitude units typed after a number ("5k", "3 Mio.", "5万"). Windows/.NET/ICU only
/// format CLDR compact numbers and never parse them, so the table is maintained here.
/// Entries follow CLDR short/long compact decimal forms, minus en-IN "L" (default alias
/// for HNL). Units whose meaning differs by language ("mil", "bn") use the English value
/// unless the current culture's language overrides it. Long words cover the common singular and
/// plural forms people type ("5 millones", "3 миллиона").
/// </summary>
internal static class NumberUnits
{
    private readonly record struct Unit(
        string Text,
        decimal Multiplier,
        bool Standalone,
        string[]? OverrideLanguages = null,
        decimal OverrideMultiplier = 0m);

    private const decimal Hundred = 100m;
    private const decimal Thousand = 1_000m;
    private const decimal TenThousand = 10_000m;
    private const decimal Lakh = 100_000m;
    private const decimal Million = 1_000_000m;
    private const decimal Crore = 10_000_000m;
    private const decimal HundredMillion = 100_000_000m;
    private const decimal Billion = 1_000_000_000m;
    private const decimal Trillion = 1_000_000_000_000m;

    // Standalone units are single CJK/Hangul characters that may be followed directly by
    // other letters ("5万円"). The rest must not be followed by a letter, so glued codes
    // like "5mxn" or "5bnd" stay currencies.
    private static readonly Unit[] Units =
    [
        // English
        new("k", Thousand, false),
        new("thousand", Thousand, false),
        new("m", Million, false),
        new("mn", Million, false),
        new("million", Million, false),
        new("mil", Million, false, ["es", "pt"], Thousand), // Spanish and Portuguese "mil" is a thousand
        new("b", Billion, false),
        new("bn", Billion, false, ["fr"], Trillion), // French "Bn" is a long-scale billion
        new("billion", Billion, false),
        new("t", Trillion, false),
        new("tn", Trillion, false),
        new("trillion", Trillion, false),

        // Indian numbering
        new("lac", Lakh, false),
        new("lacs", Lakh, false),
        new("lakh", Lakh, false),
        new("lakhs", Lakh, false),
        new("cr", Crore, false),
        new("crore", Crore, false),
        new("crores", Crore, false),
        new("हज़ार", Thousand, false), // हज़ार (decomposed)
        new("हज़ार", Thousand, false), // हज़ार (precomposed)
        new("लाख", Lakh, false), // लाख
        new("करोड़", Crore, false), // करोड़ (decomposed)
        new("करोड़", Crore, false), // करोड़ (precomposed)

        // German / Italian / Dutch / French / Portuguese (long scale: Bio. = 1e12)
        new("tsd", Thousand, false),
        new("mio", Million, false),
        new("mln", Million, false),
        new("mrd", Billion, false),
        new("mld", Billion, false),
        new("md", Billion, false),
        new("bio", Trillion, false),
        new("mi", Million, false),
        new("bi", Billion, false),
        new("tri", Trillion, false),
        new("tausend", Thousand, false),
        new("millionen", Million, false),
        new("milliarde", Billion, false),
        new("milliarden", Billion, false),
        new("mila", Thousand, false),
        new("milione", Million, false),
        new("milioni", Million, false),
        new("miliardo", Billion, false),
        new("miliardi", Billion, false),

        // Spanish / Portuguese ("5 mil millones" multiplies through to 5e9)
        new("millón", Million, false),
        new("millon", Million, false),
        new("millones", Million, false),
        new("milhão", Million, false),
        new("milhao", Million, false),
        new("milhões", Million, false),
        new("milhoes", Million, false),
        new("bilhão", Billion, false), // Brazilian short scale
        new("bilhao", Billion, false),
        new("bilhões", Billion, false),
        new("bilhoes", Billion, false),

        // Russian / Ukrainian
        new("тыс", Thousand, false),
        new("тис", Thousand, false),
        new("млн", Million, false),
        new("млрд", Billion, false),
        new("трлн", Trillion, false),
        new("к", Thousand, false), // "5к", Cyrillic k
        new("тысяча", Thousand, false),
        new("тысячи", Thousand, false),
        new("тысяч", Thousand, false),
        new("тисяча", Thousand, false),
        new("тисячі", Thousand, false),
        new("тисяч", Thousand, false),
        new("миллион", Million, false),
        new("миллиона", Million, false),
        new("миллионов", Million, false),
        new("мільйон", Million, false),
        new("мільйона", Million, false),
        new("мільйонів", Million, false),
        new("лям", Million, false), // slang
        new("ляма", Million, false),
        new("лямов", Million, false),
        new("миллиард", Billion, false),
        new("миллиарда", Billion, false),
        new("миллиардов", Billion, false),
        new("мільярд", Billion, false),
        new("мільярда", Billion, false),
        new("мільярдів", Billion, false),

        // Arabic
        new("ألف", Thousand, false),
        new("مليون", Million, false),
        new("مليار", Billion, false),
        new("ترليون", Trillion, false),
        new("تريليون", Trillion, false),

        // Chinese / Japanese
        new("百", Hundred, true),
        new("千", Thousand, true),
        new("万", TenThousand, true),
        new("萬", TenThousand, true),
        new("亿", HundredMillion, true),
        new("億", HundredMillion, true),
        new("兆", Trillion, true),

        // Korean
        new("백", Hundred, true),
        new("천", Thousand, true),
        new("만", TenThousand, true),
        new("억", HundredMillion, true),
        new("조", Trillion, true),
    ];

    // Longest first so "mn" wins over "m" and "млрд" over "млн".
    private static readonly Unit[] ByLength = [.. Units.OrderByDescending(u => u.Text.Length)];

    /// <summary>
    /// Matches a unit at <paramref name="index"/>. <paramref name="length"/> includes an
    /// optional trailing abbreviation dot ("Mio.", "тыс.").
    /// </summary>
    internal static bool TryMatch(string s, int index, out decimal multiplier, out int length)
    {
        foreach (Unit unit in ByLength)
        {
            int end = index + unit.Text.Length;
            if (end > s.Length
                || string.Compare(s, index, unit.Text, 0, unit.Text.Length, StringComparison.OrdinalIgnoreCase) != 0)
            {
                continue;
            }

            if (!unit.Standalone)
            {
                if (end < s.Length && IsWordChar(s[end]))
                {
                    continue;
                }

                if (end < s.Length && s[end] == '.')
                {
                    end++;
                }
            }

            multiplier = unit.OverrideLanguages is { } languages
                && languages.Contains(CultureInfo.CurrentCulture.TwoLetterISOLanguageName, StringComparer.Ordinal)
                    ? unit.OverrideMultiplier
                    : unit.Multiplier;
            length = end - index;
            return true;
        }

        multiplier = 0m;
        length = 0;
        return false;
    }

    private static bool IsWordChar(char c) =>
        char.IsLetter(c)
        || CharUnicodeInfo.GetUnicodeCategory(c) is UnicodeCategory.NonSpacingMark
            or UnicodeCategory.SpacingCombiningMark
            or UnicodeCategory.EnclosingMark;
}
