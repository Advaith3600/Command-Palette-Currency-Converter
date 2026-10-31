using CurrencyConverterExtension.Helpers;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace CurrencyConverterExtension.Converter;

/// <summary>
/// Turns what people type ("$", "pesos", "рублей", "美元", "달러를") into currency codes.
/// The user's alias file wins, except for symbols and words shared by several countries,
/// which mean the local currency while the alias still has its shipped default. Built-in
/// words live here rather than in alias.default.json, which is only copied on first run.
/// </summary>
internal static class CurrencyNames
{
    private readonly record struct Regional(string Default, string[] LocalCodes);

    private static readonly string[] DollarCodes =
        ["usd", "cad", "aud", "nzd", "sgd", "hkd", "twd", "jmd", "ttd", "bsd", "bbd", "bzd", "bmd", "bnd", "fjd", "gyd", "kyd", "lrd", "nad", "sbd", "srd", "xcd"];

    private static readonly string[] PesoCodes = ["mxn", "php", "ars", "clp", "cop", "cup", "dop", "uyu"];

    private static readonly string[] RupeeCodes = ["inr", "pkr", "lkr", "npr", "mur", "scr"];

    private static readonly string[] YuanCodes = ["cny", "twd", "hkd", "mop"];

    // "$" is also the peso sign, so it covers the peso countries; "dollar" in Mexico is still USD.
    private static readonly Dictionary<string, Regional> RegionalNames = new(StringComparer.Ordinal)
    {
        ["$"] = new("usd", [.. DollarCodes, .. PesoCodes]),
        ["dollar"] = new("usd", DollarCodes),
        ["dollars"] = new("usd", DollarCodes),
        ["bucks"] = new("usd", DollarCodes),
        ["peso"] = new("mxn", PesoCodes),
        ["pesos"] = new("mxn", PesoCodes),
        ["¥"] = new("jpy", ["jpy", "cny"]),
        ["￥"] = new("jpy", ["jpy", "cny"]),
        ["元"] = new("cny", YuanCodes),
        ["块"] = new("cny", YuanCodes),
        ["kr"] = new("sek", ["sek", "nok", "dkk", "isk"]),
        ["₨"] = new("lkr", RupeeCodes),
        ["rs"] = new("inr", RupeeCodes),
        ["rupee"] = new("inr", RupeeCodes),
        ["rupees"] = new("inr", RupeeCodes),
        ["dinar"] = new("kwd", ["kwd", "bhd", "jod", "iqd", "rsd", "tnd", "dzd", "lyd"]),
        ["dirham"] = new("aed", ["aed", "mad"]),
    };

    private static readonly Dictionary<string, string> BuiltInNames = BuildBuiltInNames();

    private static Dictionary<string, string> BuildBuiltInNames()
    {
        Dictionary<string, string> names = new(StringComparer.Ordinal);
        void Add(string code, params string[] words)
        {
            foreach (string word in words)
            {
                names[word] = code;
            }
        }

        // Symbols missing from the shipped alias file.
        Add("kzt", "₸", "тенге", "тг");
        Add("try", "₺", "лира", "лиры", "лир", "lira", "lire");
        Add("gel", "₾");
        Add("bdt", "৳");

        // Russian / Ukrainian (case forms people type after a number or "в")
        Add("rub", "р", "руб", "рубль", "рубля", "рублей", "рубли", "рублях", "рублям", "₽");
        Add("usd", "доллар", "доллара", "долларов", "доллары", "долларах", "бакс", "бакса", "баксов", "баксы", "долар", "долари", "доларів");
        Add("eur", "евро", "євро");
        Add("cny", "юань", "юаня", "юаней", "юани", "юанях");
        Add("jpy", "иена", "иены", "иен", "йена", "йены", "йен");
        Add("gbp", "фунт", "фунта", "фунтов", "фунты");
        Add("uah", "грн", "гривна", "гривны", "гривен", "гривня", "гривні", "гривень");
        Add("chf", "франк", "франка", "франков");
        Add("pln", "злотый", "злотых");
        Add("btc", "биткоин", "биткоина", "биткоинов");

        // Spanish / Portuguese / Italian / German / French
        Add("usd", "dólar", "dólares", "dolar", "dolares", "dollaro", "dollari");
        Add("brl", "reais");
        Add("gbp", "libra", "libras", "sterlina", "sterline", "pfund", "livre", "livres");
        Add("chf", "franken", "franco", "franchi");
        Add("php", "piso");

        // Hindi
        Add("inr", "रुपये", "रुपया", "रुपए", "रु");
        Add("usd", "डॉलर");

        // Chinese (simplified and traditional)
        Add("cny", "人民币", "人民幣");
        Add("usd", "美元", "美金");
        Add("eur", "欧元", "歐元");
        Add("jpy", "日元", "日圆", "日圓", "日币", "日幣");
        Add("gbp", "英镑", "英鎊");
        Add("hkd", "港元", "港币", "港幣");
        Add("krw", "韩元", "韓元", "韩币", "韓幣");
        Add("twd", "台币", "台幣", "新台币", "新臺幣", "新台幣");
        Add("rub", "卢布", "盧布");
        Add("aud", "澳元", "澳币", "澳幣");
        Add("cad", "加元", "加币", "加幣");
        Add("chf", "瑞郎", "瑞士法郎");
        Add("sgd", "新加坡元", "新币", "新幣");
        Add("thb", "泰铢", "泰銖");
        Add("inr", "卢比", "印度卢比");
        Add("btc", "比特币", "比特幣");

        // Japanese
        Add("jpy", "円", "えん");
        Add("usd", "ドル", "米ドル");
        Add("eur", "ユーロ");
        Add("gbp", "ポンド");
        Add("cny", "人民元");
        Add("krw", "ウォン");
        Add("rub", "ルーブル");
        Add("aud", "豪ドル");
        Add("cad", "カナダドル");
        Add("hkd", "香港ドル");
        Add("chf", "スイスフラン");
        Add("inr", "ルピー");
        Add("btc", "ビットコイン");

        // Korean
        Add("krw", "원", "원화");
        Add("usd", "달러", "달러화", "불");
        Add("eur", "유로", "유로화");
        Add("jpy", "엔", "엔화");
        Add("cny", "위안", "위안화");
        Add("gbp", "파운드");
        Add("rub", "루블");
        Add("btc", "비트코인");

        return names;
    }

    // Korean particles glued to a word: "달러를", "원으로". Longest first.
    private static readonly string[] KoreanParticles = ["으로", "에서", "로", "를", "을", "은", "는", "이", "가", "에", "와", "과", "도"];

    private static readonly string[] CryptoCodes =
        ["aave", "ada", "ape", "atom", "avax", "bch", "bnb", "btc", "dai", "doge", "dot", "etc", "eth", "fil", "icp", "link", "ltc", "mana", "matic", "mkr", "near", "sand", "shib", "sol", "sui", "trx", "uni", "usdc", "usdt", "xlm", "xmr", "xrp"];

    private static readonly Lazy<HashSet<string>> KnownCodes = new(() =>
    {
        HashSet<string> codes = new(CryptoCodes, StringComparer.Ordinal);
        foreach (CultureInfo culture in CultureInfo.GetCultures(CultureTypes.SpecificCultures))
        {
            try
            {
                codes.Add(new RegionInfo(culture.Name).ISOCurrencySymbol.ToLowerInvariant());
            }
            catch (ArgumentException)
            {
                // Some specific cultures have no region.
            }
        }

        return codes;
    });

    /// <summary>A currency code Windows knows for some region, or a packaged crypto code.</summary>
    internal static bool IsKnownCode(string code) => KnownCodes.Value.Contains(code);

    internal static bool IsBuiltInName(string word) => BuiltInNames.ContainsKey(word) || RegionalNames.ContainsKey(word);

    private static readonly string[] AllNames = [.. BuiltInNames.Keys.Concat(RegionalNames.Keys).OrderByDescending(n => n.Length)];

    /// <summary>A built-in currency name starts at <paramref name="index"/> ("三千円", "삼만원을").</summary>
    internal static bool StartsWithName(string s, int index) =>
        AllNames.Any(n => index + n.Length <= s.Length && string.Compare(s, index, n, 0, n.Length, StringComparison.OrdinalIgnoreCase) == 0);

    /// <summary>
    /// Resolves a typed currency to a lowercase code. Unknown text comes back lowercased, so
    /// plain codes ("usd") pass through.
    /// </summary>
    internal static string Resolve(string? currency, AliasManager aliases, string localCurrency)
    {
        if (string.IsNullOrWhiteSpace(currency))
        {
            return string.Empty;
        }

        string key = currency.Trim().ToLowerInvariant();
        if (TryResolve(key, aliases, localCurrency, out string code))
        {
            return code;
        }

        // "달러를" → "달러": only when the stem is a name we know, since "유로" ends in a particle too.
        if (key.Length > 1 && IsHangul(key[^1]))
        {
            foreach (string particle in KoreanParticles)
            {
                if (key.Length > particle.Length
                    && key.EndsWith(particle, StringComparison.Ordinal)
                    && TryResolve(key[..^particle.Length], aliases, localCurrency, out code))
                {
                    return code;
                }
            }
        }

        return key;
    }

    private static bool TryResolve(string key, AliasManager aliases, string localCurrency, out string code)
    {
        string? alias = aliases.GetAlias(key)?.ToLowerInvariant();

        if (RegionalNames.TryGetValue(key, out Regional regional) && (alias is null || alias == regional.Default))
        {
            string local = ResolveLocal(localCurrency, aliases);
            code = regional.LocalCodes.Contains(local, StringComparer.Ordinal) ? local : regional.Default;
            return true;
        }

        if (alias is not null)
        {
            code = alias;
            return true;
        }

        return BuiltInNames.TryGetValue(key, out code!);
    }

    private static string ResolveLocal(string localCurrency, AliasManager aliases)
    {
        string key = localCurrency.Trim().ToLowerInvariant();
        return aliases.GetAlias(key)?.ToLowerInvariant() ?? key;
    }

    private static bool IsHangul(char c) => c is >= '가' and <= '힣';
}
