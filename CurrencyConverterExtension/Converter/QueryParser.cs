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
    public static QueryParseResult NoMatch() => new(QueryParseStatus.NoMatch, null);
    public static QueryParseResult InvalidExpression() => new(QueryParseStatus.InvalidExpression, null);
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

        try
        {
            List<Token>? tokens = Tokenize(trimmed, GetNumberFormatInfo(decimalSeparatorMode));
            if (tokens is null || !tokens.Any(t => t.Kind == TokenKind.Number))
            {
                return QueryParseResult.NoMatch();
            }

            // Reported only now so stray characters elsewhere still mean NoMatch.
            if (tokens.Any(t => t.Invalid))
            {
                return QueryParseResult.InvalidExpression();
            }

            (Node expression, string toCurrency) = new Parser(tokens).ParseQuery();
            return QueryParseResult.Success(Evaluate(expression, toCurrency));
        }
        catch (ParseFailure failure)
        {
            return new QueryParseResult(failure.Status, null);
        }
        catch (Exception e) when (e is FormatException or OverflowException or DivideByZeroException)
        {
            return QueryParseResult.InvalidExpression();
        }
    }

    internal static NumberFormatInfo GetNumberFormatInfo(int decimalSeparatorMode)
    {
        NumberFormatInfo nfi = new();

        switch (decimalSeparatorMode)
        {
            case 1:
                nfi.CurrencyDecimalSeparator = ".";
                nfi.CurrencyGroupSeparator = ",";
                break;
            case 2:
                nfi.CurrencyDecimalSeparator = ",";
                nfi.CurrencyGroupSeparator = ".";
                break;
            default:
                // Clone so callers cannot mutate the live culture instance.
                nfi = (NumberFormatInfo)CultureInfo.CurrentCulture.NumberFormat.Clone();
                break;
        }

        return nfi;
    }

    // ==================== Tokenizer ====================

    private enum TokenKind
    {
        Number,
        Operator,
        OpenParen,
        CloseParen,
        Word,
    }

    private readonly record struct Token(TokenKind Kind, decimal Value = 0m, char Operator = '\0', string Text = "", bool Invalid = false);

    /// <summary>Returns null when the text contains characters that cannot be part of a query.</summary>
    private static List<Token>? Tokenize(string s, NumberFormatInfo formatter)
    {
        string decimalSeparator = formatter.CurrencyDecimalSeparator;
        string groupSeparator = formatter.CurrencyGroupSeparator;
        List<Token> tokens = [];
        int i = 0;

        while (i < s.Length)
        {
            char c = s[i];

            if (char.IsWhiteSpace(c))
            {
                i++;
            }
            else if (IsDigit(c))
            {
                decimal? value = ReadNumber(s, ref i, decimalSeparator, groupSeparator);
                tokens.Add(new Token(TokenKind.Number, value ?? 0m, Invalid: value is null));
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
            else if (IsWordChar(c))
            {
                int start = i;
                while (i < s.Length && IsWordChar(s[i]))
                {
                    i++;
                }

                tokens.Add(new Token(TokenKind.Word, Text: s[start..i].ToLowerInvariant()));
            }
            else
            {
                return null;
            }
        }

        return tokens;
    }

    /// <summary>
    /// Reads digits, separators and whitespace-split digit groups ("1 000"), then any
    /// magnitude units ("5k", "3 Mio.", "5万亿"). Null when the number is malformed or overflows.
    /// </summary>
    private static decimal? ReadNumber(string s, ref int i, string decimalSeparator, string groupSeparator)
    {
        StringBuilder digits = new();
        while (i < s.Length)
        {
            if (IsDigit(s[i]))
            {
                digits.Append(s[i]);
                i++;
            }
            else if (MatchesAt(s, i, decimalSeparator))
            {
                digits.Append('.');
                i += decimalSeparator.Length;
            }
            else if (MatchesAt(s, i, groupSeparator))
            {
                i += groupSeparator.Length;
            }
            else if (char.IsWhiteSpace(s[i]))
            {
                int next = SkipWhitespace(s, i);
                if (next >= s.Length || !IsDigit(s[next]))
                {
                    break;
                }

                i = next;
            }
            else
            {
                break;
            }
        }

        bool valid = decimal.TryParse(digits.ToString(), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out decimal value);

        while (true)
        {
            int next = SkipWhitespace(s, i);
            if (!NumberUnits.TryMatch(s, next, out decimal multiplier, out int length))
            {
                return valid ? value : null;
            }

            i = next + length;
            if (valid)
            {
                try
                {
                    value *= multiplier;
                }
                catch (OverflowException)
                {
                    valid = false;
                }
            }
        }
    }

    private static int SkipWhitespace(string s, int i)
    {
        while (i < s.Length && char.IsWhiteSpace(s[i]))
        {
            i++;
        }

        return i;
    }

    private static bool MatchesAt(string s, int index, string value) =>
        value.Length > 0
        && index + value.Length <= s.Length
        && string.CompareOrdinal(s, index, value, 0, value.Length) == 0;

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

    private sealed class ParseFailure(QueryParseStatus status) : Exception
    {
        public QueryParseStatus Status { get; } = status;
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
                if (!Peek(TokenKind.CloseParen))
                {
                    throw new ParseFailure(QueryParseStatus.InvalidExpression);
                }

                _pos++;
                return inner;
            }

            throw new ParseFailure(QueryParseStatus.InvalidExpression);
        }

        private bool IsOperandStart(int index) =>
            index < tokens.Count
            && tokens[index] is { Kind: TokenKind.Number or TokenKind.OpenParen }
                or { Kind: TokenKind.Operator, Operator: '+' or '-' };

        private bool IsKeywordBeforeWord(int index) =>
            index + 1 < tokens.Count
            && tokens[index] is { Kind: TokenKind.Word, Text: "to" or "in" }
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
                    throw new ParseFailure(QueryParseStatus.InvalidExpression);
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
                    _ => throw new ParseFailure(QueryParseStatus.InvalidExpression),
                };
            default:
                throw new ParseFailure(QueryParseStatus.InvalidExpression);
        }
    }
}
