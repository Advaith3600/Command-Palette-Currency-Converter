using System.Collections.Generic;

namespace CurrencyConverterExtension.Converter;

/// <summary>
/// Reads Chinese, Japanese and Korean numerals: "三千五百", "一万五" (15,000), "两千零五",
/// banknote forms ("壹仟贰佰"), Korean "삼만", and Latin digits mixed with units ("3万5千",
/// "1万5000円", "1억 5천만"). Units below 10,000 add inside a section; 万/億/兆 (만/억/조)
/// close a section, and a bigger one multiplies everything before it ("一万亿" = 10¹²).
/// </summary>
internal static class CjkNumerals
{
    private static readonly Dictionary<char, int> Digits = Build(
        ("〇零영공", 0),
        ("一壹壱일", 1),
        ("二贰貳弐两兩이", 2),
        ("三叁參参삼", 3),
        ("四肆사", 4),
        ("五伍오", 5),
        ("六陆陸육륙", 6),
        ("七柒칠", 7),
        ("八捌팔", 8),
        ("九玖구", 9));

    private static readonly Dictionary<char, int> SmallUnits = Build(
        ("十拾십", 10),
        ("百佰백", 100),
        ("千仟천", 1000));

    private static readonly Dictionary<char, decimal> BigUnits = new()
    {
        ['万'] = 10_000m, ['萬'] = 10_000m, ['만'] = 10_000m,
        ['亿'] = 100_000_000m, ['億'] = 100_000_000m, ['억'] = 100_000_000m,
        ['兆'] = 1_000_000_000_000m, ['조'] = 1_000_000_000_000m,
    };

    private const string Points = "点點점";

    private static Dictionary<char, int> Build(params (string Chars, int Value)[] groups)
    {
        Dictionary<char, int> map = [];
        foreach ((string chars, int value) in groups)
        {
            foreach (char c in chars)
            {
                map[c] = value;
            }
        }

        return map;
    }

    internal static bool IsNumeral(char c) => Digits.ContainsKey(c) || IsUnit(c);

    internal static bool IsUnit(char c) => SmallUnits.ContainsKey(c) || BigUnits.ContainsKey(c);

    /// <summary>
    /// Reads a numeral starting at <paramref name="index"/>. With <paramref name="initial"/>, a
    /// Latin amount was just read ("3" of "3万5千") and a unit must follow. <paramref name="colloquial"/>
    /// is set when a trailing digit was scaled ("一万五" is 15,000, not 10,005). Throws
    /// <see cref="System.OverflowException"/> for amounts too large for a decimal.
    /// </summary>
    internal static bool TryRead(string s, int index, decimal? initial, out decimal value, out int end, out bool colloquial)
    {
        decimal total = 0m;
        decimal section = 0m;
        decimal largestBig = 0m;
        decimal? digits = initial;
        bool numeralDigits = false;
        int digitCount = 0;
        decimal unitBeforeDigits = 0m;
        bool afterBig = false;
        bool read = false;
        int p = index;
        colloquial = false;

        while (p < s.Length)
        {
            char c = s[p];

            if (Digits.TryGetValue(c, out int digit) && !(digits is not null && !numeralDigits))
            {
                digits = ((digits ?? 0m) * 10m) + digit;
                numeralDigits = true;
                digitCount++;
                p++;
            }
            else if (c is >= '0' and <= '9' && digits is null && (read || initial is not null))
            {
                // "3万5千", "1万5000円": Latin digits after a unit.
                int start = p;
                while (p < s.Length && (s[p] is >= '0' and <= '9' || (s[p] == '.' && p + 1 < s.Length && s[p + 1] is >= '0' and <= '9')))
                {
                    p++;
                }

                digits = decimal.Parse(s[start..p], System.Globalization.CultureInfo.InvariantCulture);
                numeralDigits = false;
                digitCount = 0;
            }
            else if (SmallUnits.TryGetValue(c, out int small))
            {
                section += (digits ?? 1m) * small;
                unitBeforeDigits = small;
                ResetDigits();
                p++;
            }
            else if (BigUnits.TryGetValue(c, out decimal big))
            {
                // A bare unit is one of it ("万", "만원"), unless it scales what came before ("5万亿").
                decimal chunk = section + (digits ?? 0m);
                if (chunk == 0m && total == 0m)
                {
                    chunk = 1m;
                }

                total = big > largestBig ? (total + chunk) * big : total + (chunk * big);
                largestBig = big > largestBig ? big : largestBig;
                section = 0m;
                unitBeforeDigits = big;
                ResetDigits();
                afterBig = true;
                p++;
                read = true;
                continue;
            }
            else if (Points.IndexOf(c) >= 0 && p + 1 < s.Length && Digits.ContainsKey(s[p + 1]))
            {
                // "三点五" is 3.5.
                decimal scale = 0.1m;
                decimal fraction = 0m;
                p++;
                while (p < s.Length && Digits.TryGetValue(s[p], out int fractionDigit))
                {
                    fraction += fractionDigit * scale;
                    scale /= 10m;
                    p++;
                }

                digits = (digits ?? 0m) + fraction;
                digitCount = 0;
            }
            else if (afterBig && char.IsWhiteSpace(c) && StartsUnitGroup(s, SkipSpaces(s, p)))
            {
                // Korean spaces each big group: "1억 5천만".
                p = SkipSpaces(s, p);
                afterBig = false;
                continue;
            }
            else
            {
                break;
            }

            afterBig = false;
            read = true;
        }

        if (!read || (initial is not null && p == index))
        {
            value = 0m;
            end = index;
            return false;
        }

        if (digits is { } trailing)
        {
            if (numeralDigits && digitCount == 1 && unitBeforeDigits >= 100m)
            {
                // "一万五", "三千五", "一百二": the digit counts in the next lower unit.
                trailing *= unitBeforeDigits / 10m;
                colloquial = true;
            }

            section += trailing;
        }

        value = total + section;
        end = p;
        return true;

        void ResetDigits()
        {
            digits = null;
            numeralDigits = false;
            digitCount = 0;
        }
    }

    // After a space, only a group that ends in a unit continues the number ("5천만"), so
    // "100万 5 usd" stays two numbers.
    private static bool StartsUnitGroup(string s, int p)
    {
        int start = p;
        while (p < s.Length && (s[p] is >= '0' and <= '9' || Digits.ContainsKey(s[p])))
        {
            p++;
        }

        return p > start && p < s.Length && IsUnit(s[p]);
    }

    private static int SkipSpaces(string s, int p)
    {
        while (p < s.Length && char.IsWhiteSpace(s[p]))
        {
            p++;
        }

        return p;
    }
}
