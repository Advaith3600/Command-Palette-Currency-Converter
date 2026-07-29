using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace CurrencyConverterExtension.Converter;

public static class CalculateEngine
{
    private static bool HasPrecedence(char op1, char op2)
    {
        if (op2 == '(' || op2 == ')')
            return false;
        if ((op1 == '*' || op1 == '/') && (op2 == '+' || op2 == '-'))
            return false;
        else
            return true;
    }

    private static decimal ApplyOp(char op, decimal b, decimal a) => op switch
    {
        '+' => a + b,
        '-' => a - b,
        '*' => a * b,
        '/' when b != 0 => a / b,
        '/' => throw new DivideByZeroException("Cannot divide by zero"),
        _ => throw new ArgumentException("Invalid operator", nameof(op))
    };

    private static void ApplyTopOperator(Stack<decimal> values, Stack<char> ops)
    {
        if (values.Count < 2 || ops.Count == 0)
            throw new InvalidOperationException("Invalid expression");

        values.Push(ApplyOp(ops.Pop(), values.Pop(), values.Pop()));
    }

    public static decimal Evaluate(string expression, NumberFormatInfo formatter)
    {
        Stack<decimal> values = new Stack<decimal>();
        Stack<char> ops = new Stack<char>();
        bool expectOperand = true;
        decimal sign = 1;

        string separator = formatter.CurrencyDecimalSeparator;
        for (int i = 0; i < expression.Length; i++)
        {
            if (expression[i] == ' ')
                continue;

            if (expectOperand && (expression[i] == '+' || expression[i] == '-'))
            {
                if (expression[i] == '-')
                    sign = -sign;
                continue;
            }

            if (expression[i] >= '0' && expression[i] <= '9')
            {
                StringBuilder sbuf = new StringBuilder();
                while (i < expression.Length &&
                       ((expression[i] >= '0' && expression[i] <= '9') ||
                        MatchesSeparator(expression, i, separator) ||
                        char.IsWhiteSpace(expression[i])))
                {
                    if (!char.IsWhiteSpace(expression[i]))
                        sbuf.Append(expression[i]);
                    i += MatchesSeparator(expression, i, separator) ? separator.Length : 1;
                }

                values.Push(sign * decimal.Parse(sbuf.ToString(), NumberStyles.Currency, formatter));
                sign = 1;
                expectOperand = false;
                i--;
            }
            else if (expression[i] == '(')
            {
                ops.Push(expression[i]);
                expectOperand = true;
            }
            else if (expression[i] == ')')
            {
                while (ops.Count > 0 && ops.Peek() != '(')
                    ApplyTopOperator(values, ops);

                if (ops.Count == 0 || ops.Pop() != '(')
                    throw new InvalidOperationException("Invalid expression");

                expectOperand = false;
            }
            else if (expression[i] == '+' || expression[i] == '-' || expression[i] == '*' || expression[i] == '/')
            {
                while (ops.Count > 0 && HasPrecedence(expression[i], ops.Peek()))
                    ApplyTopOperator(values, ops);

                ops.Push(expression[i]);
                expectOperand = true;
            }
            else
            {
                throw new InvalidOperationException("Invalid expression");
            }
        }

        if (expectOperand && values.Count == 0)
            throw new InvalidOperationException("Invalid expression");

        while (ops.Count > 0)
            ApplyTopOperator(values, ops);

        if (values.Count != 1)
            throw new InvalidOperationException("Invalid expression");

        return values.Pop();
    }

    private static bool MatchesSeparator(string expression, int index, string separator) =>
        index + separator.Length <= expression.Length &&
        expression.Substring(index, separator.Length) == separator;
}
