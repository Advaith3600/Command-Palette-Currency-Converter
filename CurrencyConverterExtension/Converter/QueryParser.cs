using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace CurrencyConverterExtension.Converter;

internal enum QueryParseStatus
{
    NoMatch,
    InvalidExpression,
    Success,
}

internal readonly record struct CurrencyAmount(string Currency, decimal Amount);

/// <summary>
/// A parsed query. For a single-currency query <see cref="Amount"/> and <see cref="FromCurrency"/>
/// describe it fully. A mixed query ("200 aed + 20 usd") lists its parts in <see cref="Components"/>;
/// <see cref="FromCurrency"/> is then the first part's currency and <see cref="Amount"/> is unused.
/// A part with an empty currency is an untagged amount and means the local currency.
/// </summary>
internal readonly record struct ParsedQuery(decimal Amount, string FromCurrency, string ToCurrency)
{
    public IReadOnlyList<CurrencyAmount> Components { get; init; } = Array.Empty<CurrencyAmount>();

    public bool IsMixed => Components is { Count: > 1 };
}

internal readonly record struct QueryParseResult(QueryParseStatus Status, ParsedQuery? Query)
{
    /// <summary>
    /// The query with every ambiguous separator ("1,234", "1.500") read the other way. Set only
    /// in system-default mode, where the region is a guess rather than the user's choice.
    /// </summary>
    public ParsedQuery? Alternative { get; init; }

    /// <summary>How a malformed number was guessed ("Read "1.234.56" as 1,234.56"), to show under the result.</summary>
    public string? Note { get; init; }

    /// <summary>What is wrong with an <see cref="QueryParseStatus.InvalidExpression"/>, when known.</summary>
    public string? Problem { get; init; }

    public static QueryParseResult NoMatch() => new(QueryParseStatus.NoMatch, null);
    public static QueryParseResult InvalidExpression(string? problem = null) => new(QueryParseStatus.InvalidExpression, null) { Problem = problem };
    public static QueryParseResult Success(ParsedQuery query) => new(QueryParseStatus.Success, query);
}

/// <summary>
/// Parses "&lt;amount expression&gt; [to|in] [currency]" queries. Each number may carry a
/// magnitude unit (see <see cref="NumberUnits"/>) and each operand may be tagged with a
/// currency, either before ("$100") or after ("100 usd") it.
/// </summary>
internal static class QueryParser
{
    private const int MaxSearchLength = 128;

    public static QueryParseResult Parse(string search, int decimalSeparatorMode)
    {
        string trimmed = search.Trim();
        if (trimmed.Length == 0 || trimmed.Length > MaxSearchLength)
        {
            return QueryParseResult.NoMatch();
        }

        // "100 usd =" and "100 usd to eur?" read like questions to a calculator.
        string normalized = Normalize(trimmed).TrimEnd('=', '?', ' ');
        if (normalized.Length == 0)
        {
            return QueryParseResult.NoMatch();
        }

        char preferredDecimal = GetDecimalSeparator(decimalSeparatorMode);
        QueryParseResult result = Parse(normalized, preferredDecimal, out bool ambiguous);

        if (ambiguous && decimalSeparatorMode == 0 && result.Status == QueryParseStatus.Success)
        {
            QueryParseResult alternative = Parse(normalized, preferredDecimal == '.' ? ',' : '.', out _);
            if (alternative.Status == QueryParseStatus.Success)
            {
                result = result with { Alternative = alternative.Query };
            }
        }

        return result;
    }

    private static QueryParseResult Parse(string normalized, char preferredDecimal, out bool ambiguous)
    {
        ambiguous = false;
        try
        {
            List<Token>? tokens = Tokenize(normalized, preferredDecimal, ref ambiguous);
            if (tokens is null || !tokens.Any(t => t.Kind == TokenKind.Number))
            {
                return QueryParseResult.NoMatch();
            }

            // Reported only now so stray characters elsewhere still mean NoMatch.
            if (tokens.FirstOrDefault(t => t.Problem is not null).Problem is { } problem)
            {
                return QueryParseResult.InvalidExpression(problem);
            }

            (Node expression, string toCurrency) = new Parser(tokens).ParseQuery();
            string[] notes = [.. tokens.Select(t => t.Note).OfType<string>()];
            return QueryParseResult.Success(Evaluate(expression, toCurrency)) with
            {
                Note = notes.Length == 0 ? null : string.Join("; ", notes),
            };
        }
        catch (ParseFailure failure)
        {
            return new QueryParseResult(failure.Status, null) { Problem = failure.Problem };
        }
        catch (DivideByZeroException)
        {
            return QueryParseResult.InvalidExpression("Can't divide by zero");
        }
        catch (OverflowException)
        {
            return QueryParseResult.InvalidExpression("The result is too large");
        }
        catch (FormatException)
        {
            return QueryParseResult.InvalidExpression();
        }
    }

    /// <summary>
    /// The separator that wins when a number reads either way ("1,234"). Everything else is
    /// read from the number's shape, so the region is only a tiebreaker.
    /// </summary>
    internal static char GetDecimalSeparator(int decimalSeparatorMode) => decimalSeparatorMode switch
    {
        1 => '.',
        2 => ',',
        _ => CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator == "," ? ',' : '.',
    };

    /// <summary>
    /// Folds characters that other keyboards, IMEs and pasted text produce into the ASCII
    /// the tokenizer reads. One char in, one char out.
    /// </summary>
    private static string Normalize(string s)
    {
        StringBuilder? sb = null;
        for (int i = 0; i < s.Length; i++)
        {
            char normalized = Normalize(s[i]);
            if (normalized != s[i])
            {
                sb ??= new StringBuilder(s);
                sb[i] = normalized;
            }
        }

        return sb?.ToString() ?? s;
    }

    private static char Normalize(char c) => c switch
    {
        >= '！' and <= '～' => (char)(c - 0xFEE0), // full-width forms from CJK IMEs ("１００", "＄")
        '−' or '‒' or '–' or '﹣' => '-', // minus sign, figure and en dashes
        '×' or '⋅' => '*', // × ⋅
        '÷' or '∕' => '/', // ÷ ∕
        '٫' => '.', // Arabic decimal separator
        '٬' => ',', // Arabic thousands separator
        '’' or 'ʼ' => '\'', // Swiss thousands apostrophes
        _ when c > '9' && char.IsDigit(c) => (char)('0' + CharUnicodeInfo.GetDecimalDigitValue(c)), // ١٢٣, ۱۲۳, १२३
        _ => c,
    };

    // ==================== Tokenizer ====================

    private enum TokenKind
    {
        Number,
        Operator,
        OpenParen,
        CloseParen,
        Word,
    }

    /// <summary>
    /// <see cref="Plain"/> numbers are bare digit runs ("123"), which a stray space may have split.
    /// <see cref="Note"/> records a guess; <see cref="Problem"/> a number that could not be read.
    /// </summary>
    private readonly record struct Token(
        TokenKind Kind,
        decimal Value = 0m,
        char Operator = '\0',
        string Text = "",
        bool Plain = false,
        string? Note = null,
        string? Problem = null,
        bool Connector = false);

    /// <summary>Returns null when the text contains characters that cannot be part of a query.</summary>
    private static List<Token>? Tokenize(string s, char preferredDecimal, ref bool ambiguous)
    {
        List<Token>? tokens = ReadTokens(s, preferredDecimal, ref ambiguous);
        return tokens is null ? null : JoinSplitNumbers(ReadXAsTimes(DropDanglingConnectors(FixKeyboardLayout(tokens))));
    }

    // Words for "to" across languages; arrows and CJK connectors become "to" as well.
    private static readonly HashSet<string> ToKeywords = new(StringComparer.Ordinal)
    {
        "to", "in", "into", "в", "во", "a", "en", "para", "em", "nach",
    };

    // Chinese and Japanese write a conversion without spaces ("100美元换人民币", "100ドルを円に"), so
    // these split a word run. Connectors mean "to"; fillers ("how much") are dropped. None of them
    // appears inside a currency name.
    private static readonly (string Text, bool Connector)[] CjkSplitters =
    [
        .. new[] { "兑换成", "转换成", "换算成", "兑换", "换成", "转换", "转成", "兑成", "换算", "等于", "换", "兑", "转", "到", "是", "から", "って", "を", "は", "に", "で", "へ" }
            .Select(t => (t, true)),
        .. new[] { "多少钱", "多少", "把", "吗", "いくら", "ですか", "何" }.Select(t => (t, false)),
    ];

    private static readonly (string Text, bool Connector)[] CjkSplittersByLength = [.. CjkSplitters.OrderByDescending(s => s.Text.Length)];

    /// <summary>Splits a word run at CJK connectors and fillers into currency words and "to" keywords.</summary>
    private static void AddWordTokens(List<Token> tokens, string word)
    {
        int segmentStart = 0;
        int p = 0;
        while (p < word.Length)
        {
            (string Text, bool Connector) splitter = CjkSplittersByLength.FirstOrDefault(
                c => p + c.Text.Length <= word.Length && string.CompareOrdinal(word, p, c.Text, 0, c.Text.Length) == 0);
            if (splitter.Text is null)
            {
                p++;
                continue;
            }

            if (p > segmentStart)
            {
                tokens.Add(new Token(TokenKind.Word, Text: word[segmentStart..p]));
            }

            if (splitter.Connector)
            {
                tokens.Add(new Token(TokenKind.Word, Text: "to", Connector: true));
            }

            p += splitter.Text.Length;
            segmentStart = p;
        }

        if (word.Length > segmentStart)
        {
            tokens.Add(new Token(TokenKind.Word, Text: word[segmentStart..]));
        }
    }

    /// <summary>"100ドルを円に": a connector with no currency after it, or doubled, says nothing.</summary>
    private static List<Token> DropDanglingConnectors(List<Token> tokens)
    {
        List<Token> kept = [];
        for (int k = 0; k < tokens.Count; k++)
        {
            bool dangling = tokens[k].Connector
                && (k + 1 == tokens.Count
                    || tokens[k + 1].Kind != TokenKind.Word
                    || tokens[k + 1].Connector
                    || ToKeywords.Contains(tokens[k + 1].Text)
                    || (kept.Count > 0 && kept[^1].Kind == TokenKind.Word && ToKeywords.Contains(kept[^1].Text)));
            if (!dangling)
            {
                kept.Add(tokens[k]);
            }
        }

        return kept;
    }

    // Russian and Ukrainian ЙЦУКЕН keys and the Latin letters on the same keys.
    private const string CyrillicKeys = "йцукенгшщзфывапролдячсмитьі";
    private const string LatinKeys = "qwertyuiopasdfghjklzxcvbnms";

    /// <summary>
    /// "гыв" is "usd" typed with the keyboard still on Russian. Rewritten only when the word is
    /// not a currency name itself and the Latin reading is a known code; the guess gets a note.
    /// </summary>
    private static List<Token> FixKeyboardLayout(List<Token> tokens)
    {
        for (int k = 0; k < tokens.Count; k++)
        {
            Token token = tokens[k];
            if (token.Kind != TokenKind.Word
                || token.Connector
                || token.Text.Length is < 3 or > 4
                || CurrencyNames.IsBuiltInName(token.Text)
                || !token.Text.All(c => CyrillicKeys.Contains(c)))
            {
                continue;
            }

            string latin = string.Concat(token.Text.Select(c => LatinKeys[CyrillicKeys.IndexOf(c)]));
            if (CurrencyNames.IsKnownCode(latin))
            {
                tokens[k] = token with { Text = latin, Note = $"Read \"{token.Text}\" as {latin.ToUpperInvariant()}" };
            }
        }

        return tokens;
    }

    /// <summary>"5x100" and "3 x 20 usd": an "x" between two operands multiplies.</summary>
    private static List<Token> ReadXAsTimes(List<Token> tokens)
    {
        for (int k = 1; k + 1 < tokens.Count; k++)
        {
            if (tokens[k] is { Kind: TokenKind.Word, Text: "x" }
                && tokens[k - 1].Kind is TokenKind.Number or TokenKind.CloseParen
                && tokens[k + 1].Kind is TokenKind.Number or TokenKind.OpenParen)
            {
                tokens[k] = new Token(TokenKind.Operator, Operator: '*');
            }
        }

        return tokens;
    }

    /// <summary>"1 23": two bare digit runs in a row are one number with a stray space, never valid otherwise.</summary>
    private static List<Token> JoinSplitNumbers(List<Token> tokens)
    {
        List<Token> joined = [];
        foreach (Token token in tokens)
        {
            if (token.Plain && joined.Count > 0 && joined[^1] is { Plain: true } previous)
            {
                string text = $"{previous.Text} {token.Text}";
                joined[^1] = decimal.TryParse(text.Replace(" ", string.Empty, StringComparison.Ordinal), NumberStyles.None, CultureInfo.InvariantCulture, out decimal value)
                    ? previous with { Value = value, Text = text, Note = ReadAsNote(text, value) }
                    : previous with { Text = text, Problem = CouldNotReadProblem(text, "the number is too large") };
                continue;
            }

            joined.Add(token);
        }

        return joined;
    }

    private static List<Token>? ReadTokens(string s, char preferredDecimal, ref bool ambiguous)
    {
        List<Token> tokens = [];
        int i = 0;

        while (i < s.Length)
        {
            char c = s[i];

            if (char.IsWhiteSpace(c))
            {
                i++;
            }
            else if (IsDigit(c) || (IsSeparator(c) && i + 1 < s.Length && IsDigit(s[i + 1])))
            {
                tokens.Add(ReadNumber(s, ref i, preferredDecimal, ref ambiguous));
            }
            else if (c == '→' || (c is '-' or '=' && i + 1 < s.Length && s[i + 1] == '>'))
            {
                // "100 usd → eur", "100 usd -> eur".
                tokens.Add(new Token(TokenKind.Word, Text: "to", Connector: true));
                i += c == '→' ? 1 : 2;
            }
            else if (c is '+' or '-' or '*' or '/')
            {
                tokens.Add(new Token(TokenKind.Operator, Operator: c));
                i++;
            }
            else if (c == '(')
            {
                tokens.Add(new Token(TokenKind.OpenParen));
                i++;
            }
            else if (c == ')')
            {
                tokens.Add(new Token(TokenKind.CloseParen));
                i++;
            }
            else if (CjkNumerals.IsNumeral(c) && TryReadCjkNumeral(s, ref i) is { } numeral)
            {
                tokens.Add(numeral);
            }
            else if (IsWordChar(c))
            {
                // A Han numeral ends the word: "美元三千". Hangul is left alone, since Korean
                // spaces its words and "이" is also a particle ("원이").
                int start = i;
                while (i < s.Length && IsWordChar(s[i]) && !(i > start && IsHan(s[i]) && StartsCjkNumeral(s, i)))
                {
                    i++;
                }

                AddWordTokens(tokens, s[start..i].ToLowerInvariant());

                // "Rs. 500", "Rs.500", "500 руб.": an abbreviation dot. Not before a letter, so
                // "google.com" still isn't a query.
                if (i < s.Length && s[i] == '.' && (i + 1 == s.Length || char.IsWhiteSpace(s[i + 1]) || IsDigit(s[i + 1])))
                {
                    i++;
                }
            }
            else
            {
                return null;
            }
        }

        return tokens;
    }

    /// <summary>
    /// Reads digit groups split by ".", ",", spaces or apostrophes ("1 000", "1'000"), then any
    /// magnitude units ("5k", "3 Mio.", "5万亿"). Slips like "12..5" and "12,-" are repaired;
    /// malformed grouping is guessed with a note, or reported as the token's problem.
    /// </summary>
    private static Token ReadNumber(string s, ref int i, char preferredDecimal, ref bool ambiguous)
    {
        // groups[k] and groups[k + 1] are split by separators[k]; ' ' stands for any space or apostrophe.
        List<string> groups = [];
        List<char> separators = [];
        int numberStart = i;
        bool slipped = false;
        int start = i;
        while (i < s.Length)
        {
            if (IsDigit(s[i]))
            {
                i++;
                continue;
            }

            int next = -1;

            // On a Russian or Ukrainian layout the "." and "," keys type "ю" and "б" ("12ю5").
            bool layoutSlip = char.ToLowerInvariant(s[i]) is 'ю' or 'б';
            char separator = char.ToLowerInvariant(s[i]) switch
            {
                MiddleDot or 'ю' => '.',
                'б' => ',',
                char other => other,
            };

            // "12..5": a doubled separator is a slip, not two separators.
            int last = i;
            while (IsSeparator(separator) && last + 1 < s.Length && s[last + 1] == s[i])
            {
                last++;
            }

            if (IsSeparator(separator) && last + 1 < s.Length && IsDigit(s[last + 1]))
            {
                next = last + 1;
                slipped |= layoutSlip;
            }
            else if (separator == '\'' || char.IsWhiteSpace(separator))
            {
                // Spaces and apostrophes only ever group thousands, so they need three digits
                // after them; otherwise "100 5" would merge two numbers.
                separator = ' ';
                int digitsStart = s[i] == '\'' ? i + 1 : SkipWhitespace(s, i);
                if (i > start && IsThreeDigitGroup(s, digitsStart))
                {
                    next = digitsStart;
                }
            }

            if (next < 0)
            {
                break;
            }

            groups.Add(s[start..i]);
            separators.Add(separator);
            start = i = next;
        }

        groups.Add(s[start..i]);
        string digitsText = s[numberStart..i];
        bool plain = separators.Count == 0;

        // "12.", "100," and the "no cents" price "12,-": a separator with nothing after it.
        if (i < s.Length && (IsSeparator(s[i]) || s[i] == MiddleDot))
        {
            int end = i + 1 < s.Length && s[i + 1] == '-' ? i + 2 : i + 1;
            if (end == s.Length || char.IsWhiteSpace(s[end]) || IsWordChar(s[end]))
            {
                i = end;
                plain = false;
            }
        }

        string? digits = JoinDigits(groups, separators, preferredDecimal, ref ambiguous, out bool guessed, out string? reason);
        decimal value = 0m;
        if (digits is not null && !decimal.TryParse(digits, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value))
        {
            reason = "the number is too large";
        }

        string? note = (guessed || slipped) && reason is null ? ReadAsNote(digitsText, value) : null;

        // "3万5千", "1万5000円", "1억 5천만": units that add up, not just multiply.
        if (reason is null && i < s.Length && CjkNumerals.IsUnit(s[i]))
        {
            try
            {
                if (CjkNumerals.TryRead(s, i, value, out decimal compound, out int end, out bool colloquial))
                {
                    value = compound;
                    note ??= colloquial ? ReadAsNote(s[numberStart..end], value) : null;
                    i = end;
                    plain = false;
                }
            }
            catch (OverflowException)
            {
                reason = "the number is too large";
            }
        }

        while (true)
        {
            int next = SkipWhitespace(s, i);
            if (!NumberUnits.TryMatch(s, next, out decimal multiplier, out int length))
            {
                return reason is null
                    ? new Token(TokenKind.Number, value, Text: digitsText, Plain: plain, Note: note)
                    : new Token(TokenKind.Number, Text: digitsText, Problem: CouldNotReadProblem(s[numberStart..i], reason));
            }

            i = next + length;
            plain = false;
            if (reason is null)
            {
                try
                {
                    value *= multiplier;
                }
                catch (OverflowException)
                {
                    reason = "the number is too large";
                }
            }
        }
    }

    /// <summary>
    /// "三千五百", "삼만원": a numeral written in CJK characters. Null when it is really the start
    /// of a word ("三星", "일본"), so ordinary text never becomes a number.
    /// </summary>
    private static Token? TryReadCjkNumeral(string s, ref int i)
    {
        try
        {
            if (!CjkNumerals.TryRead(s, i, null, out decimal value, out int end, out bool colloquial) || !EndsCjkNumeral(s, end))
            {
                return null;
            }

            string text = s[i..end];
            i = end;
            return new Token(TokenKind.Number, value, Text: text, Note: colloquial ? ReadAsNote(text, value) : null);
        }
        catch (OverflowException)
        {
            int end = i;
            while (end < s.Length && CjkNumerals.IsNumeral(s[end]))
            {
                end++;
            }

            string text = s[i..end];
            i = end;
            return new Token(TokenKind.Number, Text: text, Problem: CouldNotReadProblem(text, "the number is too large"));
        }
    }

    private static bool StartsCjkNumeral(string s, int i)
    {
        try
        {
            return CjkNumerals.TryRead(s, i, null, out _, out int end, out _) && EndsCjkNumeral(s, end);
        }
        catch (OverflowException)
        {
            return true;
        }
    }

    /// <summary>
    /// A CJK numeral counts only when what follows is not more of the same word: the end, a space,
    /// another script, a currency name ("円", "원을") or a connector ("换").
    /// </summary>
    private static bool EndsCjkNumeral(string s, int end) =>
        end == s.Length
        || !IsCjkLetter(s[end])
        || CurrencyNames.StartsWithName(s, end)
        || CjkSplitters.Any(c => end + c.Text.Length <= s.Length && string.CompareOrdinal(s, end, c.Text, 0, c.Text.Length) == 0);

    private static bool IsHan(char c) => c is >= '一' and <= '鿿' or >= '㐀' and <= '䶿' or >= '豈' and <= '﫿';

    private static bool IsCjkLetter(char c) => IsHan(c) || c is >= '぀' and <= 'ヿ' or >= '가' and <= '힣';

    private static string ReadAsNote(string text, decimal value) =>
        $"Read \"{text}\" as {value.ToString("#,0.############", CultureInfo.CurrentCulture)}";

    private static string CouldNotReadProblem(string text, string reason) => $"Couldn't read \"{text}\": {reason}";

    /// <summary>
    /// Decides which separator, if any, is the decimal one, checks the digit grouping, and
    /// returns the number as invariant digits ("1234.5"). Grouping that fits no style is guessed
    /// (<paramref name="guessed"/>) when one fix is clearly most likely; otherwise null with a reason.
    /// </summary>
    private static string? JoinDigits(
        List<string> groups,
        List<char> separators,
        char preferredDecimal,
        ref bool ambiguous,
        out bool guessed,
        out string? reason)
    {
        guessed = false;
        reason = null;
        int dots = separators.Count(c => c == '.');
        int commas = separators.Count(c => c == ',');
        int last = separators.Count - 1;
        int decimalIndex = -1;

        if (dots > 0 && commas > 0)
        {
            // "1.234,56" and "1,234.56": the last separator is the decimal one.
            char decimalSeparator = separators.Last(IsSeparator);
            decimalIndex = separators.LastIndexOf(decimalSeparator);
            if ((decimalSeparator == '.' ? dots : commas) > 1 || decimalIndex != last)
            {
                reason = "dots and commas are mixed up";
                return null;
            }
        }
        else if (dots + commas == 1 && separators.FindIndex(IsSeparator) == last)
        {
            // A lone separator with a space after it ("1,234 567") groups, so only a last one gets here.
            string head = groups[last];
            string tail = groups[last + 1];

            if (separators.Contains(' ') || tail.Length != 3 || head.Length is 0 or > 3 || head == "0")
            {
                // "1 234,5", "12,50", "0,500", ",5", "1234,567": nobody groups digits like this.
                decimalIndex = last;
            }
            else
            {
                // "1,234" and "1.234" read either way; the region or setting decides.
                ambiguous = true;
                if (separators[last] == preferredDecimal)
                {
                    decimalIndex = last;
                }
            }
        }

        if (IsValidGrouping(groups, decimalIndex < 0 ? groups.Count : decimalIndex + 1))
        {
            return Join(groups, decimalIndex);
        }

        if (decimalIndex < 0 && separators[last] != ' ' && groups[^1].Length != 3 && IsValidGrouping(groups, groups.Count - 1))
        {
            // "1.234.56": well-grouped thousands, then a short last group that only fits as decimals.
            guessed = true;
            return Join(groups, last);
        }

        if (decimalIndex < 0 && groups[^1].Length == 3 && groups[0].Length > 0)
        {
            // "1,2345,678": it ends like a grouped whole number, so the middle is a typo.
            guessed = true;
            return string.Concat(groups);
        }

        reason = "the digit groups don't line up";
        return null;
    }

    /// <summary>
    /// The first <paramref name="integerGroups"/> groups form a whole number: 1-3 leading digits,
    /// then groups of three, allowing Indian two-digit groups before the last three ("1,00,000").
    /// </summary>
    private static bool IsValidGrouping(List<string> groups, int integerGroups)
    {
        if (integerGroups > 1 && groups[0].Length is 0 or > 3)
        {
            return false;
        }

        for (int k = 1; k < integerGroups; k++)
        {
            bool valid = k == integerGroups - 1
                ? groups[k].Length == 3
                : groups[k].Length is 2 or 3;
            if (!valid)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Invariant digits, with the group after <paramref name="decimalIndex"/> as the fraction.</summary>
    private static string Join(List<string> groups, int decimalIndex) =>
        decimalIndex < 0
            ? string.Concat(groups)
            : $"{string.Concat(groups.Take(decimalIndex + 1))}.{groups[^1]}";

    private static bool IsThreeDigitGroup(string s, int index) =>
        index + 3 <= s.Length
        && IsDigit(s[index]) && IsDigit(s[index + 1]) && IsDigit(s[index + 2])
        && (index + 3 == s.Length || !IsDigit(s[index + 3]));

    private static int SkipWhitespace(string s, int i)
    {
        while (i < s.Length && char.IsWhiteSpace(s[i]))
        {
            i++;
        }

        return i;
    }

    // A decimal point in UK handwriting and Catalan ("12·50"); only read inside numbers.
    private const char MiddleDot = '·';

    private static bool IsSeparator(char c) => c is '.' or ',';

    private static bool IsDigit(char c) => c is >= '0' and <= '9';

    // Same token class as AliasManager keys, plus combining marks for scripts like Devanagari.
    private static bool IsWordChar(char c) =>
        c == '_'
        || char.IsLetter(c)
        || CharUnicodeInfo.GetUnicodeCategory(c) is UnicodeCategory.CurrencySymbol
            or UnicodeCategory.NonSpacingMark
            or UnicodeCategory.SpacingCombiningMark
            or UnicodeCategory.EnclosingMark;

    // ==================== Parser ====================

    private abstract record Node;

    private sealed record NumberNode(decimal Value) : Node;

    private sealed record NegateNode(Node Operand) : Node;

    private sealed record BinaryNode(char Operator, Node Left, Node Right) : Node;

    private sealed record TagNode(string Currency, Node Operand) : Node;

    private sealed class ParseFailure(QueryParseStatus status, string? problem = null) : Exception
    {
        public QueryParseStatus Status { get; } = status;

        public string? Problem { get; } = problem;
    }

    /// <summary>
    /// Recursive descent: expr = term (+|- term)*, term = unary (*|/ unary)*,
    /// unary = (+|-) unary | [word] primary [word], primary = number | ( expr ).
    /// Malformed arithmetic is <see cref="QueryParseStatus.InvalidExpression"/>; leftover
    /// words or numbers mean the text is not a conversion query (<see cref="QueryParseStatus.NoMatch"/>).
    /// </summary>
    private sealed class Parser(List<Token> tokens)
    {
        private int _pos;

        public (Node Expression, string ToCurrency) ParseQuery()
        {
            Node expression = ParseExpression();

            string toCurrency = string.Empty;
            if (IsKeywordBeforeWord(_pos))
            {
                _pos++;
            }

            if (Peek(TokenKind.Word))
            {
                toCurrency = tokens[_pos++].Text;
            }

            if (Peek(TokenKind.CloseParen))
            {
                throw new ParseFailure(QueryParseStatus.InvalidExpression, "Unexpected \")\"");
            }

            if (_pos != tokens.Count)
            {
                throw new ParseFailure(QueryParseStatus.NoMatch);
            }

            return (expression, toCurrency);
        }

        private Node ParseExpression()
        {
            Node left = ParseTerm();
            while (PeekOperator('+', '-'))
            {
                char op = tokens[_pos++].Operator;
                left = new BinaryNode(op, left, ParseTerm());
            }

            return left;
        }

        private Node ParseTerm()
        {
            Node left = ParseUnary();
            while (PeekOperator('*', '/'))
            {
                char op = tokens[_pos++].Operator;
                left = new BinaryNode(op, left, ParseUnary());
            }

            return left;
        }

        private Node ParseUnary(bool allowPostfix = true)
        {
            if (PeekOperator('+', '-'))
            {
                char op = tokens[_pos++].Operator;
                Node operand = ParseUnary(allowPostfix);
                return op == '-' ? new NegateNode(operand) : operand;
            }

            // Prefix currency: "$100", "usd 100", "€(1+2)". A word after it is the
            // target ("usd 100 eur"), not a second tag.
            if (Peek(TokenKind.Word) && IsOperandStart(_pos + 1))
            {
                string currency = tokens[_pos++].Text;
                return new TagNode(currency, ParseUnary(allowPostfix: false));
            }

            Node primary = ParsePrimary();

            // Postfix currency: "100 usd". Leave "to"/"in" + word for the target.
            if (allowPostfix && Peek(TokenKind.Word) && !IsKeywordBeforeWord(_pos))
            {
                return new TagNode(tokens[_pos++].Text, primary);
            }

            return primary;
        }

        private Node ParsePrimary()
        {
            if (Peek(TokenKind.Number))
            {
                return new NumberNode(tokens[_pos++].Value);
            }

            if (Peek(TokenKind.OpenParen))
            {
                _pos++;
                Node inner = ParseExpression();
                if (Peek(TokenKind.CloseParen))
                {
                    _pos++;
                }
                else if (_pos < tokens.Count && !IsKeywordBeforeWord(_pos))
                {
                    throw new ParseFailure(QueryParseStatus.InvalidExpression, "Missing \")\"");
                }

                // Otherwise the bracket closes itself at the end, like a calculator: "(10 + 2 usd to eur".
                return inner;
            }

            throw new ParseFailure(QueryParseStatus.InvalidExpression, MissingNumberProblem());
        }

        private string MissingNumberProblem() =>
            Peek(TokenKind.CloseParen) ? "Unexpected \")\""
            : _pos > 0 && tokens[_pos - 1].Kind == TokenKind.Operator ? $"Missing a number after \"{tokens[_pos - 1].Operator}\""
            : "Missing a number";

        private bool IsOperandStart(int index) =>
            index < tokens.Count
            && tokens[index] is { Kind: TokenKind.Number or TokenKind.OpenParen }
                or { Kind: TokenKind.Operator, Operator: '+' or '-' };

        private bool IsKeywordBeforeWord(int index) =>
            index + 1 < tokens.Count
            && tokens[index].Kind == TokenKind.Word
            && ToKeywords.Contains(tokens[index].Text)
            && tokens[index + 1].Kind == TokenKind.Word;

        private bool Peek(TokenKind kind) => _pos < tokens.Count && tokens[_pos].Kind == kind;

        private bool PeekOperator(char a, char b) =>
            _pos < tokens.Count
            && tokens[_pos].Kind == TokenKind.Operator
            && (tokens[_pos].Operator == a || tokens[_pos].Operator == b);
    }

    // ==================== Evaluation ====================

    private static ParsedQuery Evaluate(Node expression, string toCurrency)
    {
        List<string> currencies = [];
        CollectCurrencies(expression, currencies);

        // One currency (or none): it applies to the whole amount, so "10 / 2 usd" and
        // "100 + 5 usd" keep their scalar meaning.
        if (currencies.Count <= 1)
        {
            decimal amount = EvaluateScalar(expression);
            string from = currencies.Count == 1 ? currencies[0] : string.Empty;
            return new ParsedQuery(amount, from, toCurrency)
            {
                Components = from.Length == 0 ? Array.Empty<CurrencyAmount>() : new[] { new CurrencyAmount(from, amount) },
            };
        }

        Money money = EvaluateMoney(expression);
        List<CurrencyAmount> components = [.. money.Parts.Select(p => new CurrencyAmount(p.Key, p.Value))];

        // "200 aed + 5 + 20 usd": the bare 5 is in the local currency, kept where it was typed.
        // "200 aed + (5 + 20) usd" never gets here, since the bracket is tagged as a whole.
        if (money.HasUntagged)
        {
            components.Insert(Math.Min(money.UntaggedIndex, components.Count), new CurrencyAmount(string.Empty, money.Untagged));
        }

        return new ParsedQuery(0m, components[0].Currency, toCurrency) { Components = components };
    }

    private static void CollectCurrencies(Node node, List<string> currencies)
    {
        switch (node)
        {
            case TagNode tag:
                if (!currencies.Contains(tag.Currency, StringComparer.Ordinal))
                {
                    currencies.Add(tag.Currency);
                }

                CollectCurrencies(tag.Operand, currencies);
                break;
            case NegateNode negate:
                CollectCurrencies(negate.Operand, currencies);
                break;
            case BinaryNode binary:
                CollectCurrencies(binary.Left, currencies);
                CollectCurrencies(binary.Right, currencies);
                break;
        }
    }

    private static decimal EvaluateScalar(Node node) => node switch
    {
        NumberNode number => number.Value,
        TagNode tag => EvaluateScalar(tag.Operand),
        NegateNode negate => -EvaluateScalar(negate.Operand),
        BinaryNode binary => Apply(binary.Operator, EvaluateScalar(binary.Left), EvaluateScalar(binary.Right)),
        _ => throw new ParseFailure(QueryParseStatus.InvalidExpression),
    };

    private static decimal Apply(char op, decimal left, decimal right) => op switch
    {
        '+' => left + right,
        '-' => left - right,
        '*' => left * right,
        '/' => left / right,
        _ => throw new ParseFailure(QueryParseStatus.InvalidExpression),
    };

    /// <summary>
    /// A sum of currency amounts plus an untagged part. Only scalars may multiply or
    /// divide money; tagging an already-tagged value is rejected.
    /// </summary>
    private sealed class Money
    {
        public decimal Untagged;
        public bool HasUntagged;

        // How many parts preceded the first untagged amount, to list it in typed order.
        public int UntaggedIndex;
        public List<KeyValuePair<string, decimal>> Parts = [];

        public bool IsScalar => Parts.Count == 0;

        public Money Scale(decimal factor, bool divide = false)
        {
            Money result = new()
            {
                Untagged = divide ? Untagged / factor : Untagged * factor,
                HasUntagged = HasUntagged,
                UntaggedIndex = UntaggedIndex,
            };
            foreach (KeyValuePair<string, decimal> part in Parts)
            {
                result.Parts.Add(new(part.Key, divide ? part.Value / factor : part.Value * factor));
            }

            return result;
        }

        public Money Add(Money other, decimal sign)
        {
            Money result = Scale(1m);
            if (!HasUntagged && other.HasUntagged)
            {
                result.UntaggedIndex = Parts.Count + other.UntaggedIndex;
            }

            result.Untagged += sign * other.Untagged;
            result.HasUntagged |= other.HasUntagged;
            foreach (KeyValuePair<string, decimal> part in other.Parts)
            {
                int index = result.Parts.FindIndex(p => p.Key == part.Key);
                if (index >= 0)
                {
                    result.Parts[index] = new(part.Key, result.Parts[index].Value + (sign * part.Value));
                }
                else
                {
                    result.Parts.Add(new(part.Key, sign * part.Value));
                }
            }

            return result;
        }
    }

    private static Money EvaluateMoney(Node node)
    {
        switch (node)
        {
            case NumberNode number:
                return new Money { Untagged = number.Value, HasUntagged = true };
            case TagNode tag:
                Money operand = EvaluateMoney(tag.Operand);
                if (!operand.IsScalar)
                {
                    throw new ParseFailure(QueryParseStatus.InvalidExpression, "An amount can only have one currency");
                }

                return new Money { Parts = [new(tag.Currency, operand.Untagged)] };
            case NegateNode negate:
                return EvaluateMoney(negate.Operand).Scale(-1m);
            case BinaryNode binary:
                Money left = EvaluateMoney(binary.Left);
                Money right = EvaluateMoney(binary.Right);
                return binary.Operator switch
                {
                    '+' => left.Add(right, 1m),
                    '-' => left.Add(right, -1m),
                    '*' when left.IsScalar => right.Scale(left.Untagged),
                    '*' when right.IsScalar => left.Scale(right.Untagged),
                    '/' when right.IsScalar => left.Scale(right.Untagged, divide: true),
                    _ => throw new ParseFailure(QueryParseStatus.InvalidExpression, "Money can only be multiplied or divided by plain numbers"),
                };
            default:
                throw new ParseFailure(QueryParseStatus.InvalidExpression);
        }
    }
}
