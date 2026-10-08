using System.Globalization;

namespace Yorehold.Rules;

/// <summary>
/// A rule written as arithmetic: "die == 20 ? 2 : total >= dc ? 1 : 0". Numbers, names the caller
/// gives values to ("total", "dc", "self.level"), + - * / %, comparisons, &amp;&amp; || !, a ? b : c and
/// min, max, floor, ceil, round, abs and clamp. True is 1 and false is 0. It reads nothing but the
/// names it is handed and always ends, so a ruleset from anywhere is safe to run.
/// </summary>
public sealed class Formula
{
    private const int LongestText = 2000;
    private const int Deepest = 40;
    private static readonly string[] Functions = { "min", "max", "floor", "ceil", "round", "abs", "clamp" };

    private abstract record Node;
    private sealed record Number(double Value) : Node;
    private sealed record Name(string Id) : Node;
    private sealed record Unary(char Op, Node Of) : Node;
    private sealed record Binary(string Op, Node Left, Node Right) : Node;
    private sealed record Choice(Node If, Node Then, Node Else) : Node;
    private sealed record Call(string Function, List<Node> Arguments) : Node;

    private readonly Node _root;

    public string Text { get; }
    /// <summary>The names it reads, so a loader can refuse one nobody will give a value to.</summary>
    public IReadOnlyList<string> Names { get; }

    private Formula(string text, Node root, List<string> names)
    {
        Text = text;
        _root = root;
        Names = names;
    }

    /// <summary>Null, with why, when the text isn't a formula.</summary>
    public static Formula? Parse(string text, out string error)
    {
        error = "";
        if (text.Length == 0 || text.Length > LongestText)
        {
            error = $"is a formula of 1 to {LongestText} characters";
            return null;
        }
        var parser = new Parser(text);
        try
        {
            Node root = parser.Expression(0);
            parser.SkipSpace();
            if (parser.Position < text.Length)
            {
                throw new FormatException($"unexpected \"{text[parser.Position]}\" at {parser.Position + 1}");
            }
            return new Formula(text, root, parser.Names);
        }
        catch (FormatException problem)
        {
            error = problem.Message;
            return null;
        }
    }

    /// <summary>The value, with each name looked up through values; a name it doesn't know is 0.</summary>
    public double Evaluate(Func<string, double?> values)
    {
        return Value(_root, values);
    }

    public int Whole(Func<string, double?> values)
    {
        double value = Evaluate(values);
        return double.IsNaN(value) ? 0 : (int)Math.Clamp(Math.Floor(value), int.MinValue, int.MaxValue);
    }

    public override string ToString() => Text;

    private static double Value(Node node, Func<string, double?> values)
    {
        switch (node)
        {
            case Number number:
                return number.Value;
            case Name name:
                return values(name.Id) ?? 0;
            case Unary unary:
            {
                double of = Value(unary.Of, values);
                return unary.Op == '-' ? -of : of == 0 ? 1 : 0;
            }
            case Choice choice:
                return Value(choice.If, values) != 0 ? Value(choice.Then, values) : Value(choice.Else, values);
            case Call call:
            {
                List<double> a = call.Arguments.Select(argument => Value(argument, values)).ToList();
                return call.Function switch
                {
                    "min" => a.Min(),
                    "max" => a.Max(),
                    "floor" => Math.Floor(a[0]),
                    "ceil" => Math.Ceiling(a[0]),
                    "round" => Math.Round(a[0], MidpointRounding.AwayFromZero),
                    "abs" => Math.Abs(a[0]),
                    _ => Math.Clamp(a[0], Math.Min(a[1], a[2]), Math.Max(a[1], a[2])),
                };
            }
            case Binary binary:
            {
                double left = Value(binary.Left, values);
                // the right side of && and || is only looked at when it matters
                if (binary.Op == "&&")
                {
                    return left != 0 && Value(binary.Right, values) != 0 ? 1 : 0;
                }
                if (binary.Op == "||")
                {
                    return left != 0 || Value(binary.Right, values) != 0 ? 1 : 0;
                }
                double right = Value(binary.Right, values);
                return binary.Op switch
                {
                    "+" => left + right,
                    "-" => left - right,
                    "*" => left * right,
                    // dividing by nothing is nothing, so a broken rule can't stop a fight
                    "/" => right == 0 ? 0 : left / right,
                    "%" => right == 0 ? 0 : left % right,
                    "==" => left == right ? 1 : 0,
                    "!=" => left != right ? 1 : 0,
                    ">=" => left >= right ? 1 : 0,
                    "<=" => left <= right ? 1 : 0,
                    ">" => left > right ? 1 : 0,
                    _ => left < right ? 1 : 0,
                };
            }
        }
        return 0;
    }

    private sealed class Parser
    {
        private readonly string _text;

        public int Position { get; private set; }
        public List<string> Names { get; } = new();

        public Parser(string text)
        {
            _text = text;
        }

        public void SkipSpace()
        {
            while (Position < _text.Length && char.IsWhiteSpace(_text[Position]))
            {
                Position++;
            }
        }

        private bool Take(string symbol)
        {
            SkipSpace();
            if (string.CompareOrdinal(_text, Position, symbol, 0, symbol.Length) != 0)
            {
                return false;
            }
            Position += symbol.Length;
            return true;
        }

        private void Need(string symbol)
        {
            if (!Take(symbol))
            {
                throw new FormatException($"expected \"{symbol}\" at {Position + 1}");
            }
        }

        public Node Expression(int depth)
        {
            if (depth > Deepest)
            {
                throw new FormatException("is nested too deep");
            }
            Node condition = Or(depth);
            if (!Take("?"))
            {
                return condition;
            }
            Node then = Expression(depth + 1);
            Need(":");
            return new Choice(condition, then, Expression(depth + 1));
        }

        private Node Or(int depth)
        {
            Node left = And(depth);
            while (Take("||"))
            {
                left = new Binary("||", left, And(depth));
            }
            return left;
        }

        private Node And(int depth)
        {
            Node left = Compare(depth);
            while (Take("&&"))
            {
                left = new Binary("&&", left, Compare(depth));
            }
            return left;
        }

        private Node Compare(int depth)
        {
            Node left = Sum(depth);
            foreach (string op in new[] { "==", "!=", ">=", "<=", ">", "<" })
            {
                if (Take(op))
                {
                    return new Binary(op, left, Sum(depth));
                }
            }
            return left;
        }

        private Node Sum(int depth)
        {
            Node left = Product(depth);
            while (true)
            {
                if (Take("+"))
                {
                    left = new Binary("+", left, Product(depth));
                }
                else if (Take("-"))
                {
                    left = new Binary("-", left, Product(depth));
                }
                else
                {
                    return left;
                }
            }
        }

        private Node Product(int depth)
        {
            Node left = Sign(depth);
            while (true)
            {
                if (Take("*"))
                {
                    left = new Binary("*", left, Sign(depth));
                }
                else if (Take("/"))
                {
                    left = new Binary("/", left, Sign(depth));
                }
                else if (Take("%"))
                {
                    left = new Binary("%", left, Sign(depth));
                }
                else
                {
                    return left;
                }
            }
        }

        private Node Sign(int depth)
        {
            if (depth > Deepest)
            {
                throw new FormatException("is nested too deep");
            }
            if (Take("-"))
            {
                return new Unary('-', Sign(depth + 1));
            }
            // "!" but not the start of "!="
            SkipSpace();
            if (Position < _text.Length && _text[Position] == '!' && !(Position + 1 < _text.Length && _text[Position + 1] == '='))
            {
                Position++;
                return new Unary('!', Sign(depth + 1));
            }
            return Primary(depth);
        }

        private Node Primary(int depth)
        {
            SkipSpace();
            if (Take("("))
            {
                Node inside = Expression(depth + 1);
                Need(")");
                return inside;
            }
            int start = Position;
            if (Position < _text.Length && char.IsAsciiDigit(_text[Position]))
            {
                while (Position < _text.Length && (char.IsAsciiDigit(_text[Position]) || _text[Position] == '.'))
                {
                    Position++;
                }
                if (!double.TryParse(_text.AsSpan(start, Position - start), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out double number))
                {
                    throw new FormatException($"\"{_text[start..Position]}\" isn't a number");
                }
                return new Number(number);
            }
            if (Position < _text.Length && (char.IsAsciiLetter(_text[Position]) || _text[Position] == '_'))
            {
                while (Position < _text.Length && (char.IsAsciiLetterOrDigit(_text[Position]) || _text[Position] is '_' or '.'))
                {
                    Position++;
                }
                string name = _text[start..Position];
                if (!Take("("))
                {
                    if (name is "true" or "false")
                    {
                        return new Number(name == "true" ? 1 : 0);
                    }
                    if (!Names.Contains(name))
                    {
                        Names.Add(name);
                    }
                    return new Name(name);
                }
                if (!Functions.Contains(name))
                {
                    throw new FormatException($"unknown function \"{name}\"");
                }
                var arguments = new List<Node>();
                do
                {
                    arguments.Add(Expression(depth + 1));
                }
                while (Take(","));
                Need(")");
                int needed = name is "min" or "max" ? -1 : name == "clamp" ? 3 : 1;
                if (needed < 0 ? arguments.Count > 16 : arguments.Count != needed)
                {
                    throw new FormatException($"{name}() takes {(needed < 0 ? "1 to 16 values" : needed == 1 ? "one value" : "three values")}");
                }
                return new Call(name, arguments);
            }
            throw new FormatException(Position >= _text.Length ? "ends too soon" : $"unexpected \"{_text[Position]}\" at {Position + 1}");
        }
    }
}
