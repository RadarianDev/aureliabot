using System.Globalization;
using System.Text.RegularExpressions;

public sealed class FormulaException(string message) : Exception(message);

public interface IFormulaScope
{
    double Read(string reference);
    IRandomSource Random { get; }
}

public sealed partial class Formula
{
    private static readonly HashSet<string> Keywords = ["and", "or", "not", "true", "false"];

    private static readonly Dictionary<string, (int Min, int Max)> Functions = new()
    {
        ["min"] = (1, int.MaxValue),
        ["max"] = (1, int.MaxValue),
        ["abs"] = (1, 1),
        ["round"] = (1, 2),
        ["floor"] = (1, 1),
        ["ceil"] = (1, 1),
        ["clamp"] = (3, 3),
        ["roll"] = (1, 2),
        ["chance"] = (1, 1),
    };

    private readonly Node _root;

    private Formula(string source, Node root, IReadOnlyList<string> references)
    {
        Source = source;
        _root = root;
        References = references;
    }

    public string Source { get; }

    public IReadOnlyList<string> References { get; }

    public static Formula? TryParse(string source, out string? error)
    {
        error = null;
        try
        {
            var parser = new Parser(Tokenize(source));
            var root = parser.ParseAll();
            var references = new List<string>();
            root.CollectReferences(references);
            return new Formula(source.Trim(), root, references.Distinct().ToList());
        }
        catch (FormulaException ex)
        {
            error = Texts.Get("formula.errors.invalid", ("formula", source.Trim()), ("error", ex.Message));
            return null;
        }
    }

    public double Evaluate(IFormulaScope scope)
    {
        var value = _root.Evaluate(scope);
        if (double.IsNaN(value) || double.IsInfinity(value))
            throw new FormulaException(Texts.Get("formula.errors.notANumber", ("formula", Source)));
        return value;
    }

    public bool IsTrue(IFormulaScope scope) => Evaluate(scope) != 0;

    public Formula Bind(string placeholder, string value)
    {
        var bound = Regex.Replace(Source, $@"(?<=\.){Regex.Escape(placeholder)}(?![A-Za-z0-9_])", value);
        return bound == Source ? this : TryParse(bound, out _) ?? this;
    }

    public override string ToString() => Source;

    public static double Truth(bool value) => value ? 1 : 0;

    private static List<Token> Tokenize(string text)
    {
        var tokens = new List<Token>();
        var i = 0;
        while (i < text.Length)
        {
            var c = text[i];
            if (char.IsWhiteSpace(c)) { i++; continue; }
            if (char.IsDigit(c) || (c == '.' && i + 1 < text.Length && char.IsDigit(text[i + 1])))
            {
                var match = NumberPattern().Match(text, i);
                tokens.Add(new Token(TokenKind.Number, match.Value));
                i += match.Length;
                continue;
            }
            if (char.IsLetter(c) || c == '_')
            {
                var match = ReferencePattern().Match(text, i);
                tokens.Add(new Token(TokenKind.Name, match.Value));
                i += match.Length;
                continue;
            }
            var two = i + 1 < text.Length ? text.Substring(i, 2) : "";
            if (two is "<=" or ">=" or "==" or "!=" or "&&" or "||")
            {
                tokens.Add(new Token(TokenKind.Symbol, two));
                i += 2;
                continue;
            }
            if ("+-*/%()<>,!".Contains(c))
            {
                tokens.Add(new Token(TokenKind.Symbol, c.ToString()));
                i++;
                continue;
            }
            if (c == '−')
            {
                tokens.Add(new Token(TokenKind.Symbol, "-"));
                i++;
                continue;
            }
            throw new FormulaException(Texts.Get("formula.errors.character", ("character", c.ToString())));
        }
        if (tokens.Count == 0) throw new FormulaException(Texts.Get("formula.errors.empty"));
        return tokens;
    }

    [GeneratedRegex(@"\d*\.?\d+(?:[eE][+-]?\d+)?")]
    private static partial Regex NumberPattern();

    [GeneratedRegex(@"[A-Za-z_][A-Za-z0-9_]*(?:\.[A-Za-z0-9_]+)*")]
    private static partial Regex ReferencePattern();

    private enum TokenKind { Number, Name, Symbol }

    private sealed record Token(TokenKind Kind, string Text);

    private sealed class Parser(List<Token> tokens)
    {
        private int _position;

        private Token? Peek => _position < tokens.Count ? tokens[_position] : null;

        private bool Accept(string symbol)
        {
            if (Peek is not { } token || token.Text != symbol || token.Kind == TokenKind.Number) return false;
            _position++;
            return true;
        }

        private void Expect(string symbol)
        {
            if (!Accept(symbol)) throw new FormulaException(Texts.Get("formula.errors.expected", ("expected", symbol), ("found", Peek?.Text ?? Texts.Get("formula.errors.end"))));
        }

        public Node ParseAll()
        {
            var node = Or();
            if (Peek is { } extra) throw new FormulaException(Texts.Get("formula.errors.unexpected", ("token", extra.Text)));
            return node;
        }

        private Node Or()
        {
            var node = And();
            while (Accept("or") || Accept("||")) node = new Binary("or", node, And());
            return node;
        }

        private Node And()
        {
            var node = Not();
            while (Accept("and") || Accept("&&")) node = new Binary("and", node, Not());
            return node;
        }

        private Node Not() => Accept("not") || Accept("!") ? new Unary("not", Not()) : Comparison();

        private Node Comparison()
        {
            var node = Sum();
            foreach (var op in new[] { "<=", ">=", "==", "!=", "<", ">" })
                if (Accept(op)) return new Binary(op, node, Sum());
            return node;
        }

        private Node Sum()
        {
            var node = Term();
            while (true)
            {
                if (Accept("+")) node = new Binary("+", node, Term());
                else if (Accept("-")) node = new Binary("-", node, Term());
                else return node;
            }
        }

        private Node Term()
        {
            var node = Signed();
            while (true)
            {
                if (Accept("*")) node = new Binary("*", node, Signed());
                else if (Accept("/")) node = new Binary("/", node, Signed());
                else if (Accept("%")) node = new Binary("%", node, Signed());
                else return node;
            }
        }

        private Node Signed() => Accept("-") ? new Unary("-", Signed()) : Accept("+") ? Signed() : Atom();

        private Node Atom()
        {
            if (Peek is not { } token) throw new FormulaException(Texts.Get("formula.errors.unfinished"));
            _position++;
            if (token.Kind == TokenKind.Number)
                return new Number(double.Parse(token.Text, NumberStyles.Float, CultureInfo.InvariantCulture));
            if (token.Text == "(")
            {
                var inner = Or();
                Expect(")");
                return inner;
            }
            if (token.Kind != TokenKind.Name) throw new FormulaException(Texts.Get("formula.errors.unexpected", ("token", token.Text)));
            if (token.Text == "true") return new Number(1);
            if (token.Text == "false") return new Number(0);
            if (Keywords.Contains(token.Text)) throw new FormulaException(Texts.Get("formula.errors.unexpected", ("token", token.Text)));
            if (Accept("("))
            {
                if (!Functions.TryGetValue(token.Text, out var arity))
                    throw new FormulaException(Texts.Get("formula.errors.unknownFunction", ("name", token.Text), ("functions", string.Join(", ", Functions.Keys))));
                var args = new List<Node>();
                if (!Accept(")"))
                {
                    do args.Add(Or()); while (Accept(","));
                    Expect(")");
                }
                if (args.Count < arity.Min || args.Count > arity.Max)
                    throw new FormulaException(Texts.Get("formula.errors.arguments", ("name", token.Text)));
                return new Call(token.Text, args);
            }
            return new Reference(token.Text);
        }
    }

    private abstract record Node
    {
        public abstract double Evaluate(IFormulaScope scope);

        public virtual void CollectReferences(List<string> into) { }
    }

    private sealed record Number(double Value) : Node
    {
        public override double Evaluate(IFormulaScope scope) => Value;
    }

    private sealed record Reference(string Name) : Node
    {
        public override double Evaluate(IFormulaScope scope) => scope.Read(Name);

        public override void CollectReferences(List<string> into) => into.Add(Name);
    }

    private sealed record Unary(string Op, Node Operand) : Node
    {
        public override double Evaluate(IFormulaScope scope) => Op == "-" ? -Operand.Evaluate(scope) : Truth(Operand.Evaluate(scope) == 0);

        public override void CollectReferences(List<string> into) => Operand.CollectReferences(into);
    }

    private sealed record Binary(string Op, Node Left, Node Right) : Node
    {
        public override double Evaluate(IFormulaScope scope)
        {
            if (Op == "and") return Truth(Left.Evaluate(scope) != 0 && Right.Evaluate(scope) != 0);
            if (Op == "or") return Truth(Left.Evaluate(scope) != 0 || Right.Evaluate(scope) != 0);
            var l = Left.Evaluate(scope);
            var r = Right.Evaluate(scope);
            return Op switch
            {
                "+" => l + r,
                "-" => l - r,
                "*" => l * r,
                "/" => r == 0 ? throw new FormulaException(Texts.Get("formula.errors.divideByZero")) : l / r,
                "%" => r == 0 ? throw new FormulaException(Texts.Get("formula.errors.divideByZero")) : l % r,
                "<" => Truth(l < r),
                "<=" => Truth(l <= r),
                ">" => Truth(l > r),
                ">=" => Truth(l >= r),
                "==" => Truth(Math.Abs(l - r) < 1e-9),
                _ => Truth(Math.Abs(l - r) >= 1e-9),
            };
        }

        public override void CollectReferences(List<string> into)
        {
            Left.CollectReferences(into);
            Right.CollectReferences(into);
        }
    }

    private sealed record Call(string Name, IReadOnlyList<Node> Args) : Node
    {
        public override double Evaluate(IFormulaScope scope)
        {
            var values = Args.Select(a => a.Evaluate(scope)).ToList();
            return Name switch
            {
                "min" => values.Min(),
                "max" => values.Max(),
                "abs" => Math.Abs(values[0]),
                "round" => Math.Round(values[0], values.Count > 1 ? (int)Math.Clamp(values[1], 0, 10) : 0, MidpointRounding.AwayFromZero),
                "floor" => Math.Floor(values[0]),
                "ceil" => Math.Ceiling(values[0]),
                "clamp" => Math.Clamp(values[0], Math.Min(values[1], values[2]), Math.Max(values[1], values[2])),
                "roll" => Roll(scope, values),
                _ => Truth(scope.Random.NextDouble() < values[0]),
            };
        }

        private static double Roll(IFormulaScope scope, List<double> values)
        {
            var (count, sides) = values.Count == 2 ? ((int)values[0], (int)values[1]) : (1, (int)values[0]);
            if (count < 0 || count > 1000 || sides < 1 || sides > 100000)
                throw new FormulaException(Texts.Get("formula.errors.roll"));
            var total = 0;
            for (var i = 0; i < count; i++) total += scope.Random.RollDie(sides);
            return total;
        }

        public override void CollectReferences(List<string> into)
        {
            foreach (var arg in Args) arg.CollectReferences(into);
        }
    }
}
