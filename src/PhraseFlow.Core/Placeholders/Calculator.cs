using System.Globalization;

namespace PhraseFlow.Core.Placeholders;

/// <summary>Small, safe arithmetic evaluator used by <c>{calc:…}</c>.</summary>
public static class Calculator
{
    public static double Evaluate(string expression)
    {
        var parser = new Parser(expression);
        var value = parser.ParseExpression();
        parser.SkipWhitespace();
        if (!parser.AtEnd)
        {
            throw new FormatException($"Unexpected '{parser.Current}' in expression.");
        }

        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            throw new FormatException("The expression does not produce a finite number.");
        }

        return value;
    }

    public static string Format(double value, string? format, IFormatProvider culture)
    {
        if (!string.IsNullOrWhiteSpace(format))
        {
            return value.ToString(format.Trim(), culture);
        }

        var rounded = Math.Round(value, 10);
        return rounded.ToString("0.##########", culture);
    }

    private sealed class Parser(string text)
    {
        private int _position;

        public bool AtEnd => _position >= text.Length;

        public char Current => text[_position];

        public void SkipWhitespace()
        {
            while (!AtEnd && char.IsWhiteSpace(Current))
            {
                _position++;
            }
        }

        private bool Accept(char c)
        {
            SkipWhitespace();
            if (!AtEnd && Current == c)
            {
                _position++;
                return true;
            }

            return false;
        }

        public double ParseExpression()
        {
            var value = ParseTerm();
            while (true)
            {
                if (Accept('+'))
                {
                    value += ParseTerm();
                }
                else if (Accept('-'))
                {
                    value -= ParseTerm();
                }
                else
                {
                    return value;
                }
            }
        }

        private double ParseTerm()
        {
            var value = ParsePower();
            while (true)
            {
                if (Accept('*') || Accept('×'))
                {
                    value *= ParsePower();
                }
                else if (Accept('/') || Accept('÷'))
                {
                    value /= ParsePower();
                }
                else if (Accept('%'))
                {
                    value %= ParsePower();
                }
                else
                {
                    return value;
                }
            }
        }

        private double ParsePower()
        {
            var value = ParseUnary();
            return Accept('^') ? Math.Pow(value, ParsePower()) : value;
        }

        private double ParseUnary()
        {
            if (Accept('-'))
            {
                return -ParseUnary();
            }

            return Accept('+') ? ParseUnary() : ParsePrimary();
        }

        private double ParsePrimary()
        {
            SkipWhitespace();
            if (AtEnd)
            {
                throw new FormatException("Unexpected end of expression.");
            }

            if (Accept('('))
            {
                var value = ParseExpression();
                if (!Accept(')'))
                {
                    throw new FormatException("Missing ')'.");
                }

                return value;
            }

            if (char.IsDigit(Current) || Current == '.')
            {
                var start = _position;
                while (!AtEnd && (char.IsDigit(Current) || Current == '.'))
                {
                    _position++;
                }

                if (!AtEnd && (Current == 'e' || Current == 'E') && _position + 1 < text.Length &&
                    (char.IsDigit(text[_position + 1]) || text[_position + 1] is '+' or '-'))
                {
                    _position += 2;
                    while (!AtEnd && char.IsDigit(Current))
                    {
                        _position++;
                    }
                }

                var number = text[start.._position];
                if (!double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
                {
                    throw new FormatException($"Invalid number '{number}'.");
                }

                return parsed;
            }

            if (char.IsLetter(Current))
            {
                var start = _position;
                while (!AtEnd && char.IsLetterOrDigit(Current))
                {
                    _position++;
                }

                var name = text[start.._position].ToLowerInvariant();
                switch (name)
                {
                    case "pi":
                        return Math.PI;
                    case "e":
                        return Math.E;
                }

                if (!Accept('('))
                {
                    throw new FormatException($"Unknown name '{name}'.");
                }

                var args = new List<double>();
                if (!Accept(')'))
                {
                    do
                    {
                        args.Add(ParseExpression());
                    }
                    while (Accept(',') || Accept(';'));

                    if (!Accept(')'))
                    {
                        throw new FormatException($"Missing ')' after {name}(.");
                    }
                }

                return CallFunction(name, args);
            }

            throw new FormatException($"Unexpected '{Current}' in expression.");
        }

        private static double CallFunction(string name, List<double> args)
        {
            double Arg(int index) => index < args.Count
                ? args[index]
                : throw new FormatException($"{name}() needs {index + 1} argument(s).");

            return name switch
            {
                "abs" => Math.Abs(Arg(0)),
                "round" => args.Count > 1
                    ? Math.Round(Arg(0), (int)Math.Clamp(Arg(1), 0, 15), MidpointRounding.AwayFromZero)
                    : Math.Round(Arg(0), MidpointRounding.AwayFromZero),
                "floor" => Math.Floor(Arg(0)),
                "ceil" or "ceiling" => Math.Ceiling(Arg(0)),
                "sqrt" => Math.Sqrt(Arg(0)),
                "pow" => Math.Pow(Arg(0), Arg(1)),
                "min" => args.Count > 0 ? args.Min() : throw new FormatException("min() needs arguments."),
                "max" => args.Count > 0 ? args.Max() : throw new FormatException("max() needs arguments."),
                "log" => Math.Log10(Arg(0)),
                "ln" => Math.Log(Arg(0)),
                _ => throw new FormatException($"Unknown function '{name}'."),
            };
        }
    }
}
