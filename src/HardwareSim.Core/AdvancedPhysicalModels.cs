using System.Collections.ObjectModel;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace HardwareSim.Core;

/// <summary>Identifies the safe physical model family used by Phase 7 characterized imports.</summary>
public enum PhysicalModelKind
{
    /// <summary>Represents a fixed scalar output.</summary>
    Constant,
    /// <summary>Represents a parsed safe expression over whitelisted scalar inputs.</summary>
    SafeExpression,
    /// <summary>Represents a one-dimensional lookup table.</summary>
    LookupTable1D,
    /// <summary>Represents a two-dimensional lookup table.</summary>
    LookupTable2D
}

/// <summary>Defines the supported interpolation modes for characterized lookup tables.</summary>
public enum LookupInterpolationMode
{
    /// <summary>Uses the closest table point without interpolation.</summary>
    Nearest,
    /// <summary>Uses linear interpolation over a one-dimensional axis.</summary>
    Linear,
    /// <summary>Uses bilinear interpolation over a two-dimensional grid.</summary>
    Bilinear
}

/// <summary>Describes one named model input and its canonical units.</summary>
/// <param name="Name">Provides the input variable name accepted by the model.</param>
/// <param name="Units">Provides the units used for the input value.</param>
public sealed record PhysicalModelVariable(string Name, string Units);

/// <summary>Represents the scalar result returned by a characterized physical model evaluation.</summary>
/// <param name="ModelId">Provides the model id that produced the value.</param>
/// <param name="Quantity">Provides the output quantity name.</param>
/// <param name="Units">Provides the output units.</param>
/// <param name="Value">Provides the evaluated scalar value.</param>
public sealed record PhysicalModelEvaluation(string ModelId, string Quantity, string Units, double Value);

/// <summary>Defines a safe, strongly typed physical model that can evaluate constants, expressions, and lookup tables.</summary>
public sealed class PhysicalModelDefinition
{
    private const int MaxExpressionLength = 256;
    private const int MaxExpressionDepth = 32;
    private const int MaxExpressionNodeCount = 128;

    private PhysicalModelDefinition()
    {
    }

    /// <summary>Gets the stable model identifier.</summary>
    public string Id { get; private init; } = "";

    /// <summary>Gets the model family used for evaluation.</summary>
    public PhysicalModelKind Kind { get; private init; }

    /// <summary>Gets the ordered input variable schema with units.</summary>
    public IReadOnlyList<PhysicalModelVariable> Inputs { get; private init; } = [];

    /// <summary>Gets the output quantity name, such as energy or latency.</summary>
    public string OutputQuantity { get; private init; } = "";

    /// <summary>Gets the output units, such as pJ or cycles.</summary>
    public string OutputUnits { get; private init; } = "";

    /// <summary>Gets the scalar value for constant models.</summary>
    public double? ConstantValue { get; private init; }

    /// <summary>Gets the parsed safe expression source for expression models.</summary>
    public string? Expression { get; private init; }

    /// <summary>Gets the interpolation mode used by lookup table models.</summary>
    public LookupInterpolationMode Interpolation { get; private init; } = LookupInterpolationMode.Nearest;

    /// <summary>Gets the x axis values for lookup table models.</summary>
    public IReadOnlyList<double> XAxis { get; private init; } = [];

    /// <summary>Gets the y axis values for two-dimensional lookup table models.</summary>
    public IReadOnlyList<double> YAxis { get; private init; } = [];

    /// <summary>Gets lookup table values; 2D values are row-major by y axis, then x axis.</summary>
    public IReadOnlyList<double> Values { get; private init; } = [];

    /// <summary>Creates a fixed scalar physical model.</summary>
    public static PhysicalModelDefinition Constant(string id, string outputQuantity, string outputUnits, double value) =>
        new()
        {
            Id = RequireText(id, nameof(id)),
            Kind = PhysicalModelKind.Constant,
            OutputQuantity = RequireText(outputQuantity, nameof(outputQuantity)),
            OutputUnits = RequireText(outputUnits, nameof(outputUnits)),
            ConstantValue = RequireFinite(value, nameof(value))
        };

    /// <summary>Creates a safe expression model over explicitly whitelisted inputs.</summary>
    public static PhysicalModelDefinition SafeExpression(
        string id,
        string outputQuantity,
        string outputUnits,
        string expression,
        IEnumerable<PhysicalModelVariable> inputs)
    {
        var copiedInputs = CopyInputs(inputs);
        if (copiedInputs.Count == 0)
        {
            throw new InvalidOperationException("Expression models require at least one input variable.");
        }

        var expressionText = RequireText(expression, nameof(expression));
        if (expressionText.Length > MaxExpressionLength)
        {
            throw new InvalidOperationException($"Expression length must be at most {MaxExpressionLength} characters.");
        }

        return new PhysicalModelDefinition
        {
            Id = RequireText(id, nameof(id)),
            Kind = PhysicalModelKind.SafeExpression,
            Inputs = copiedInputs,
            OutputQuantity = RequireText(outputQuantity, nameof(outputQuantity)),
            OutputUnits = RequireText(outputUnits, nameof(outputUnits)),
            Expression = expressionText
        };
    }
    /// <summary>Creates a one-dimensional lookup table model.</summary>
    public static PhysicalModelDefinition LookupTable1D(
        string id,
        string outputQuantity,
        string outputUnits,
        PhysicalModelVariable input,
        IReadOnlyList<double> axis,
        IReadOnlyList<double> values,
        LookupInterpolationMode interpolation)
    {
        if (interpolation == LookupInterpolationMode.Bilinear)
        {
            throw new InvalidOperationException("1D lookup tables support nearest or linear interpolation only.");
        }

        var copiedAxis = CopyAxis(axis, nameof(axis));
        if (interpolation == LookupInterpolationMode.Linear && copiedAxis.Count < 2)
        {
            throw new InvalidOperationException("Linear interpolation requires at least two axis points.");
        }

        return new PhysicalModelDefinition
        {
            Id = RequireText(id, nameof(id)),
            Kind = PhysicalModelKind.LookupTable1D,
            Inputs = CopyInputs([input]),
            OutputQuantity = RequireText(outputQuantity, nameof(outputQuantity)),
            OutputUnits = RequireText(outputUnits, nameof(outputUnits)),
            Interpolation = interpolation,
            XAxis = copiedAxis,
            Values = CopyValues(values, copiedAxis.Count, nameof(values))
        };
    }

    /// <summary>Creates a two-dimensional lookup table model.</summary>
    public static PhysicalModelDefinition LookupTable2D(
        string id,
        string outputQuantity,
        string outputUnits,
        PhysicalModelVariable xInput,
        PhysicalModelVariable yInput,
        IReadOnlyList<double> xAxis,
        IReadOnlyList<double> yAxis,
        IReadOnlyList<double> values,
        LookupInterpolationMode interpolation)
    {
        if (interpolation == LookupInterpolationMode.Linear)
        {
            throw new InvalidOperationException("2D lookup tables support nearest or bilinear interpolation only.");
        }

        var copiedXAxis = CopyAxis(xAxis, nameof(xAxis));
        var copiedYAxis = CopyAxis(yAxis, nameof(yAxis));
        if (interpolation == LookupInterpolationMode.Bilinear && (copiedXAxis.Count < 2 || copiedYAxis.Count < 2))
        {
            throw new InvalidOperationException("Bilinear interpolation requires at least two points on each axis.");
        }

        return new PhysicalModelDefinition
        {
            Id = RequireText(id, nameof(id)),
            Kind = PhysicalModelKind.LookupTable2D,
            Inputs = CopyInputs([xInput, yInput]),
            OutputQuantity = RequireText(outputQuantity, nameof(outputQuantity)),
            OutputUnits = RequireText(outputUnits, nameof(outputUnits)),
            Interpolation = interpolation,
            XAxis = copiedXAxis,
            YAxis = copiedYAxis,
            Values = CopyValues(values, checked(copiedXAxis.Count * copiedYAxis.Count), nameof(values))
        };
    }

    /// <summary>Evaluates the model for the supplied input values.</summary>
    public PhysicalModelEvaluation Evaluate(IReadOnlyDictionary<string, double> variables)
    {
        if (variables is null)
        {
            throw new ArgumentNullException(nameof(variables));
        }

        var value = Kind switch
        {
            PhysicalModelKind.Constant => ConstantValue ?? throw new InvalidOperationException("Constant model has no scalar value."),
            PhysicalModelKind.SafeExpression => SafeExpressionEvaluator.Evaluate(
                Expression ?? throw new InvalidOperationException("Expression model has no expression."),
                variables,
                Inputs.Select(input => input.Name)),
            PhysicalModelKind.LookupTable1D => EvaluateLookup1D(ReadVariable(variables, Inputs[0].Name)),
            PhysicalModelKind.LookupTable2D => EvaluateLookup2D(ReadVariable(variables, Inputs[0].Name), ReadVariable(variables, Inputs[1].Name)),
            _ => throw new InvalidOperationException($"Unsupported model kind '{Kind}'.")
        };

        return new PhysicalModelEvaluation(Id, OutputQuantity, OutputUnits, RequireFinite(value, "evaluation result"));
    }

    private double EvaluateLookup1D(double x) => Interpolation switch
    {
        LookupInterpolationMode.Nearest => Values[NearestIndex(XAxis, x)],
        LookupInterpolationMode.Linear => InterpolateLinear(XAxis, Values, x),
        _ => throw new InvalidOperationException($"Unsupported 1D interpolation mode '{Interpolation}'.")
    };

    private double EvaluateLookup2D(double x, double y)
    {
        return Interpolation switch
        {
            LookupInterpolationMode.Nearest => Value2D(NearestIndex(XAxis, x), NearestIndex(YAxis, y)),
            LookupInterpolationMode.Bilinear => InterpolateBilinear(x, y),
            _ => throw new InvalidOperationException($"Unsupported 2D interpolation mode '{Interpolation}'.")
        };
    }

    private double InterpolateBilinear(double x, double y)
    {
        var x0 = LowerBracket(XAxis, x);
        var y0 = LowerBracket(YAxis, y);
        var x1 = x0 + 1;
        var y1 = y0 + 1;
        var tx = (x - XAxis[x0]) / (XAxis[x1] - XAxis[x0]);
        var ty = (y - YAxis[y0]) / (YAxis[y1] - YAxis[y0]);
        var q11 = Value2D(x0, y0);
        var q21 = Value2D(x1, y0);
        var q12 = Value2D(x0, y1);
        var q22 = Value2D(x1, y1);
        return (q11 * (1.0 - tx) * (1.0 - ty)) +
               (q21 * tx * (1.0 - ty)) +
               (q12 * (1.0 - tx) * ty) +
               (q22 * tx * ty);
    }

    private double Value2D(int xIndex, int yIndex) => Values[checked((yIndex * XAxis.Count) + xIndex)];
    private static double InterpolateLinear(IReadOnlyList<double> axis, IReadOnlyList<double> values, double x)
    {
        var lower = LowerBracket(axis, x);
        var upper = lower + 1;
        var t = (x - axis[lower]) / (axis[upper] - axis[lower]);
        return values[lower] + ((values[upper] - values[lower]) * t);
    }

    private static int LowerBracket(IReadOnlyList<double> axis, double value)
    {
        EnsureWithinAxis(axis, value);
        if (axis.Count < 2)
        {
            throw new InvalidOperationException("Interpolation requires at least two axis values.");
        }

        if (value.Equals(axis[^1]))
        {
            return axis.Count - 2;
        }

        for (var index = 0; index < axis.Count - 1; index++)
        {
            if (value <= axis[index + 1])
            {
                return index;
            }
        }

        return axis.Count - 2;
    }

    private static int NearestIndex(IReadOnlyList<double> axis, double value)
    {
        EnsureWithinAxis(axis, value);
        var bestIndex = 0;
        var bestDistance = Math.Abs(value - axis[0]);
        for (var index = 1; index < axis.Count; index++)
        {
            var distance = Math.Abs(value - axis[index]);
            if (distance < bestDistance)
            {
                bestIndex = index;
                bestDistance = distance;
            }
        }

        return bestIndex;
    }

    private static void EnsureWithinAxis(IReadOnlyList<double> axis, double value)
    {
        RequireFinite(value, "lookup input");
        if (value < axis[0] || value > axis[^1])
        {
            throw new InvalidOperationException($"Lookup input {value.ToString(CultureInfo.InvariantCulture)} is outside axis range [{axis[0].ToString(CultureInfo.InvariantCulture)}, {axis[^1].ToString(CultureInfo.InvariantCulture)}].");
        }
    }

    private static double ReadVariable(IReadOnlyDictionary<string, double> variables, string name)
    {
        if (!variables.TryGetValue(name, out var value))
        {
            throw new InvalidOperationException($"Missing model input variable '{name}'.");
        }

        return RequireFinite(value, name);
    }

    private static IReadOnlyList<PhysicalModelVariable> CopyInputs(IEnumerable<PhysicalModelVariable> inputs)
    {
        if (inputs is null)
        {
            throw new ArgumentNullException(nameof(inputs));
        }

        var copied = inputs.Select(input => input ?? throw new InvalidOperationException("Model input cannot be null.")).ToList();
        foreach (var input in copied)
        {
            RequireText(input.Name, nameof(input.Name));
            RequireText(input.Units, nameof(input.Units));
        }

        var duplicate = copied
            .GroupBy(input => input.Name, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new InvalidOperationException($"Duplicate model input variable '{duplicate.Key}'.");
        }

        return copied.AsReadOnly();
    }

    private static IReadOnlyList<double> CopyAxis(IReadOnlyList<double> axis, string name)
    {
        if (axis is null)
        {
            throw new ArgumentNullException(name);
        }

        if (axis.Count == 0)
        {
            throw new InvalidOperationException($"{name} must contain at least one point.");
        }

        var copied = axis.Select((value, index) => RequireFinite(value, $"{name}[{index}]")).ToList();
        for (var index = 1; index < copied.Count; index++)
        {
            if (copied[index] <= copied[index - 1])
            {
                throw new InvalidOperationException($"{name} must be strictly increasing without duplicate points.");
            }
        }

        return copied.AsReadOnly();
    }

    private static IReadOnlyList<double> CopyValues(IReadOnlyList<double> values, int expectedCount, string name)
    {
        if (values is null)
        {
            throw new ArgumentNullException(name);
        }

        if (values.Count != expectedCount)
        {
            throw new InvalidOperationException($"{name} must contain exactly {expectedCount.ToString(CultureInfo.InvariantCulture)} point(s).");
        }

        return values.Select((value, index) => RequireFinite(value, $"{name}[{index}]")).ToList().AsReadOnly();
    }

    private static string RequireText(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"{name} is required.");
        }

        return value.Trim();
    }

    private static double RequireFinite(double value, string name)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            throw new InvalidOperationException($"{name} must be finite.");
        }

        return value;
    }
    private static class SafeExpressionEvaluator
    {
        public static double Evaluate(string expression, IReadOnlyDictionary<string, double> variables, IEnumerable<string> allowedVariables)
        {
            var allowed = allowedVariables.ToHashSet(StringComparer.Ordinal);
            var parser = new Parser(expression, variables, allowed);
            return parser.Parse();
        }

        private sealed class Parser
        {
            private readonly string _expression;
            private readonly IReadOnlyDictionary<string, double> _variables;
            private readonly HashSet<string> _allowedVariables;
            private int _position;
            private int _nodeCount;

            public Parser(string expression, IReadOnlyDictionary<string, double> variables, HashSet<string> allowedVariables)
            {
                _expression = expression;
                _variables = variables;
                _allowedVariables = allowedVariables;
            }

            public double Parse()
            {
                if (_expression.Length > MaxExpressionLength)
                {
                    throw new InvalidOperationException($"Expression length must be at most {MaxExpressionLength} characters.");
                }

                var value = ParseExpression(0);
                SkipWhitespace();
                if (_position != _expression.Length)
                {
                    throw new InvalidOperationException($"Unexpected token at position {_position.ToString(CultureInfo.InvariantCulture)}.");
                }

                return RequireFinite(value, "expression result");
            }

            private double ParseExpression(int depth)
            {
                EnsureDepth(depth);
                var value = ParseTerm(depth + 1);
                while (true)
                {
                    if (Match('+'))
                    {
                        CountNode();
                        value += ParseTerm(depth + 1);
                    }
                    else if (Match('-'))
                    {
                        CountNode();
                        value -= ParseTerm(depth + 1);
                    }
                    else
                    {
                        return value;
                    }
                }
            }

            private double ParseTerm(int depth)
            {
                EnsureDepth(depth);
                var value = ParseUnary(depth + 1);
                while (true)
                {
                    if (Match('*'))
                    {
                        CountNode();
                        value *= ParseUnary(depth + 1);
                    }
                    else if (Match('/'))
                    {
                        CountNode();
                        var divisor = ParseUnary(depth + 1);
                        if (divisor == 0)
                        {
                            throw new InvalidOperationException("Expression division by zero is not allowed.");
                        }

                        value /= divisor;
                    }
                    else
                    {
                        return value;
                    }
                }
            }

            private double ParseUnary(int depth)
            {
                EnsureDepth(depth);
                if (Match('+'))
                {
                    CountNode();
                    return ParseUnary(depth + 1);
                }

                if (Match('-'))
                {
                    CountNode();
                    return -ParseUnary(depth + 1);
                }

                return ParsePrimary(depth + 1);
            }

            private double ParsePrimary(int depth)
            {
                EnsureDepth(depth);
                SkipWhitespace();
                if (Match('('))
                {
                    var value = ParseExpression(depth + 1);
                    Expect(')');
                    return value;
                }

                if (_position < _expression.Length && (char.IsLetter(_expression[_position]) || _expression[_position] == '_'))
                {
                    return ParseIdentifier(depth + 1);
                }

                return ParseNumber();
            }

            private double ParseIdentifier(int depth)
            {
                var start = _position;
                _position++;
                while (_position < _expression.Length && (char.IsLetterOrDigit(_expression[_position]) || _expression[_position] == '_'))
                {
                    _position++;
                }

                var identifier = _expression[start.._position];
                SkipWhitespace();
                if (Match('('))
                {
                    return ParseFunction(identifier, depth + 1);
                }

                CountNode();
                if (!_allowedVariables.Contains(identifier))
                {
                    throw new InvalidOperationException($"Expression variable '{identifier}' is not allowed.");
                }

                if (!_variables.TryGetValue(identifier, out var value))
                {
                    throw new InvalidOperationException($"Expression variable '{identifier}' is missing.");
                }

                return RequireFinite(value, identifier);
            }

            private double ParseFunction(string name, int depth)
            {
                EnsureDepth(depth);
                var args = new List<double>();
                SkipWhitespace();
                if (!Match(')'))
                {
                    do
                    {
                        args.Add(ParseExpression(depth + 1));
                        SkipWhitespace();
                    }
                    while (Match(','));
                    Expect(')');
                }

                CountNode();
                return name switch
                {
                    "abs" => Function1(name, args, Math.Abs),
                    "sqrt" => Function1(name, args, value => value < 0 ? throw new InvalidOperationException("sqrt requires a non-negative argument.") : Math.Sqrt(value)),
                    "min" => Function2(name, args, Math.Min),
                    "max" => Function2(name, args, Math.Max),
                    "pow" => Function2(name, args, Math.Pow),
                    _ => throw new InvalidOperationException($"Expression function '{name}' is not allowed.")
                };
            }

            private static double Function1(string name, IReadOnlyList<double> args, Func<double, double> function)
            {
                RequireArgCount(name, args, 1);
                return RequireFinite(function(args[0]), name);
            }

            private static double Function2(string name, IReadOnlyList<double> args, Func<double, double, double> function)
            {
                RequireArgCount(name, args, 2);
                return RequireFinite(function(args[0], args[1]), name);
            }

            private static void RequireArgCount(string name, IReadOnlyList<double> args, int count)
            {
                if (args.Count != count)
                {
                    throw new InvalidOperationException($"Function '{name}' requires {count.ToString(CultureInfo.InvariantCulture)} argument(s).");
                }
            }

            private double ParseNumber()
            {
                SkipWhitespace();
                var start = _position;
                var sawDigit = false;
                while (_position < _expression.Length && (char.IsDigit(_expression[_position]) || _expression[_position] == '.'))
                {
                    sawDigit |= char.IsDigit(_expression[_position]);
                    _position++;
                }

                if (_position < _expression.Length && (_expression[_position] == 'e' || _expression[_position] == 'E'))
                {
                    var exponentStart = _position;
                    _position++;
                    if (_position < _expression.Length && (_expression[_position] == '+' || _expression[_position] == '-'))
                    {
                        _position++;
                    }

                    var exponentDigitStart = _position;
                    while (_position < _expression.Length && char.IsDigit(_expression[_position]))
                    {
                        _position++;
                    }

                    if (exponentDigitStart == _position)
                    {
                        _position = exponentStart;
                    }
                }

                if (!sawDigit)
                {
                    throw new InvalidOperationException($"Expected number or variable at position {_position.ToString(CultureInfo.InvariantCulture)}.");
                }

                var raw = _expression[start.._position];
                if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                {
                    throw new InvalidOperationException($"Invalid number literal '{raw}'.");
                }

                CountNode();
                return RequireFinite(value, raw);
            }

            private bool Match(char expected)
            {
                SkipWhitespace();
                if (_position >= _expression.Length || _expression[_position] != expected)
                {
                    return false;
                }

                _position++;
                return true;
            }

            private void Expect(char expected)
            {
                if (!Match(expected))
                {
                    throw new InvalidOperationException($"Expected '{expected}' at position {_position.ToString(CultureInfo.InvariantCulture)}.");
                }
            }

            private void SkipWhitespace()
            {
                while (_position < _expression.Length && char.IsWhiteSpace(_expression[_position]))
                {
                    _position++;
                }
            }

            private static void EnsureDepth(int depth)
            {
                if (depth > MaxExpressionDepth)
                {
                    throw new InvalidOperationException($"Expression depth must be at most {MaxExpressionDepth.ToString(CultureInfo.InvariantCulture)}.");
                }
            }

            private void CountNode()
            {
                _nodeCount++;
                if (_nodeCount > MaxExpressionNodeCount)
                {
                    throw new InvalidOperationException($"Expression complexity must be at most {MaxExpressionNodeCount.ToString(CultureInfo.InvariantCulture)} nodes.");
                }
            }
        }
    }
}
/// <summary>Defines stable Phase 7 issue severities for model import, evaluation, and binding.</summary>
public enum PhysicalModelIssueSeverity
{
    /// <summary>Indicates an issue that prevents using the model.</summary>
    Error,
    /// <summary>Indicates a non-fatal condition that should be surfaced to the compiler/user.</summary>
    Warning,
    /// <summary>Indicates informational model provenance.</summary>
    Info
}

/// <summary>Describes a structured Phase 7 physical model diagnostic.</summary>
/// <param name="Code">Provides a stable machine-readable code.</param>
/// <param name="Severity">Provides the issue severity.</param>
/// <param name="Location">Provides a JSONPath-like location.</param>
/// <param name="Message">Provides a human-readable message.</param>
/// <param name="RelatedId">Provides an optional model, target, or parameter identifier.</param>
public sealed record PhysicalModelIssue(
    string Code,
    PhysicalModelIssueSeverity Severity,
    string Location,
    string Message,
    string? RelatedId = null);

/// <summary>Represents one input value with explicit units supplied at compile time.</summary>
/// <param name="Value">Provides the numeric value.</param>
/// <param name="Units">Provides the value units.</param>
public sealed record PhysicalModelInputValue(double Value, string Units);

/// <summary>Represents a closed numeric range and its units.</summary>
/// <param name="Minimum">Provides the inclusive minimum.</param>
/// <param name="Maximum">Provides the inclusive maximum.</param>
/// <param name="Units">Provides the range units.</param>
public sealed record PhysicalModelRange(double Minimum, double Maximum, string Units);

/// <summary>Defines how out-of-range model inputs are handled.</summary>
public enum PhysicalModelRangePolicy
{
    /// <summary>Out-of-range inputs generate warnings.</summary>
    Warning,
    /// <summary>Out-of-range inputs fail evaluation or compilation.</summary>
    Error
}

/// <summary>Stores provenance and calibration metadata required for Phase 7 physical models.</summary>
public sealed class PhysicalModelMetadata
{
    /// <summary>Gets or sets the required source identifier or citation handle.</summary>
    public string Source { get; set; } = "";
    /// <summary>Gets or sets the optional citation text.</summary>
    public string Citation { get; set; } = "";
    /// <summary>Gets or sets the optional source or model version.</summary>
    public string Version { get; set; } = "";
    /// <summary>Gets or sets whether this model is calibrated against measured or trusted data.</summary>
    public bool Calibrated { get; set; }
    /// <summary>Gets or sets free-form notes preserved in profile snapshots.</summary>
    public string Notes { get; set; } = "";
    /// <summary>Gets or sets valid input ranges keyed by model input name.</summary>
    public Dictionary<string, PhysicalModelRange> ValidRanges { get; set; } = new(StringComparer.Ordinal);
    /// <summary>Gets or sets the out-of-range policy.</summary>
    public PhysicalModelRangePolicy RangePolicy { get; set; } = PhysicalModelRangePolicy.Warning;
}

/// <summary>Contains either a physical model evaluation or structured diagnostics.</summary>
public sealed class PhysicalModelEvaluationResult
{
    private PhysicalModelEvaluationResult(
        PhysicalModelEvaluation? evaluation,
        IReadOnlyList<PhysicalModelIssue> errors,
        IReadOnlyList<PhysicalModelIssue> warnings)
    {
        Evaluation = evaluation;
        Errors = errors;
        Warnings = warnings;
    }

    /// <summary>Gets whether evaluation succeeded without errors.</summary>
    public bool IsSuccess => Evaluation is not null && Errors.Count == 0;
    /// <summary>Gets the evaluated value when successful.</summary>
    public PhysicalModelEvaluation? Evaluation { get; }
    /// <summary>Gets fatal structured diagnostics.</summary>
    public IReadOnlyList<PhysicalModelIssue> Errors { get; }
    /// <summary>Gets non-fatal structured diagnostics.</summary>
    public IReadOnlyList<PhysicalModelIssue> Warnings { get; }

    /// <summary>Creates a successful evaluation result.</summary>
    public static PhysicalModelEvaluationResult Succeeded(
        PhysicalModelEvaluation evaluation,
        IEnumerable<PhysicalModelIssue>? warnings = null) =>
        new(evaluation, [], (warnings ?? []).ToList());

    /// <summary>Creates a failed evaluation result.</summary>
    public static PhysicalModelEvaluationResult Failed(
        IEnumerable<PhysicalModelIssue> errors,
        IEnumerable<PhysicalModelIssue>? warnings = null) =>
        new(null, errors.ToList(), (warnings ?? []).ToList());
}

/// <summary>Provides canonical Phase 7 unit conversion and dimension validation.</summary>
public static class PhysicalUnitConverter
{
    private static readonly Dictionary<string, string> Dimensions = new(StringComparer.OrdinalIgnoreCase)
    {
        [""] = "dimensionless",
        ["count"] = "dimensionless",
        ["value"] = "dimensionless",
        ["bool"] = "boolean",
        ["enum"] = "text",
        ["text"] = "text",
        ["bit"] = "bit",
        ["bits"] = "bit",
        ["bits/cell"] = "bit_per_cell",
        ["bits/s"] = "bit_rate",
        ["cells"] = "cell_count",
        ["cells/weight"] = "cells_per_weight",
        ["chips"] = "chip_count",
        ["weights"] = "weight_count",
        ["cycle"] = "cycle",
        ["cycles"] = "cycle",
        ["nm"] = "length",
        ["um"] = "length",
        ["mm"] = "length",
        ["um2"] = "area",
        ["mm2"] = "area",
        ["um2/cell"] = "area_per_cell",
        ["ps"] = "time",
        ["ns"] = "time",
        ["s"] = "time",
        ["pJ"] = "energy",
        ["J"] = "energy",
        ["pJ/access"] = "energy_per_access",
        ["pJ/event"] = "energy_per_event",
        ["pJ/sample"] = "energy_per_sample",
        ["pJ/conv-step"] = "energy_per_conversion_step",
        ["fJ/conv-step"] = "energy_per_conversion_step",
        ["pJ/op"] = "energy_per_operation",
        ["pJ/MAC"] = "energy_per_mac",
        ["pJ/FLOP"] = "energy_per_flop",
        ["mW"] = "power",
        ["W"] = "power",
        ["pJ/bit"] = "energy_per_bit",
        ["J/bit"] = "energy_per_bit",
        ["pJ/bit/um"] = "energy_per_bit_per_length",
        ["cycles/bit"] = "cycle_per_bit",
        ["V"] = "voltage",
        ["mV"] = "voltage",
        ["uA"] = "current",
        ["mA"] = "current",
        ["A"] = "current",
        ["ohm"] = "resistance",
        ["kohm"] = "resistance",
        ["Mohm"] = "resistance",
        ["uS"] = "conductance",
        ["mS"] = "conductance",
        ["Hz"] = "frequency",
        ["kHz"] = "frequency",
        ["MHz"] = "frequency",
        ["GHz"] = "frequency",
        ["samples/s"] = "sample_rate",
        ["kS/s"] = "sample_rate",
        ["MS/s"] = "sample_rate",
        ["GS/s"] = "sample_rate",
        ["GOPS"] = "operation_rate",
        ["TOPS/W"] = "energy_efficiency",
        ["TFLOPS/W"] = "energy_efficiency",
        ["TOPS/mm2"] = "compute_density",
        ["TFLOPS/mm2"] = "compute_density",
        ["LSB"] = "converter_code",
        ["dB"] = "log_ratio",
        ["%"] = "ratio_percent",
        ["°C"] = "temperature"
    };

    private static readonly Dictionary<string, string> UnitAliases = new(StringComparer.Ordinal)
    {
        ["um^2"] = "um2",
        ["mm^2"] = "mm2",
        ["um^2/cell"] = "um2/cell",
        ["TOPS/mm^2"] = "TOPS/mm2",
        ["TFLOPS/mm^2"] = "TFLOPS/mm2"
    };

    private static readonly Dictionary<string, double> ScaleToBase = new(StringComparer.Ordinal)
    {
        ["nm"] = 0.001d,
        ["um"] = 1d,
        ["mm"] = 1_000d,
        ["um2"] = 1d,
        ["mm2"] = 1_000_000d,
        ["ps"] = 1d,
        ["ns"] = 1_000d,
        ["s"] = 1_000_000_000_000d,
        ["pJ"] = 1d,
        ["J"] = 1_000_000_000_000d,
        ["mW"] = 1d,
        ["W"] = 1_000d,
        ["pJ/bit"] = 1d,
        ["J/bit"] = 1_000_000_000_000d,
        ["fJ/conv-step"] = 0.001d,
        ["pJ/conv-step"] = 1d,
        ["mV"] = 0.001d,
        ["V"] = 1d,
        ["uA"] = 1d,
        ["mA"] = 1_000d,
        ["A"] = 1_000_000d,
        ["ohm"] = 1d,
        ["kohm"] = 1_000d,
        ["Mohm"] = 1_000_000d,
        ["uS"] = 1d,
        ["mS"] = 1_000d,
        ["Hz"] = 1d,
        ["kHz"] = 1_000d,
        ["MHz"] = 1_000_000d,
        ["GHz"] = 1_000_000_000d,
        ["samples/s"] = 1d,
        ["kS/s"] = 1_000d,
        ["MS/s"] = 1_000_000d,
        ["GS/s"] = 1_000_000_000d
    };

    /// <summary>Converts a finite value between compatible units.</summary>
    public static double Convert(double value, string fromUnits, string toUnits)
    {
        RequireFinite(value, nameof(value));
        var from = Normalize(fromUnits);
        var to = Normalize(toUnits);
        if (!Dimensions.TryGetValue(from, out var fromDimension))
        {
            throw new InvalidOperationException($"Unsupported source units '{fromUnits}'.");
        }

        if (!Dimensions.TryGetValue(to, out var toDimension))
        {
            throw new InvalidOperationException($"Unsupported destination units '{toUnits}'.");
        }

        if (!string.Equals(fromDimension, toDimension, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Cannot convert from '{fromUnits}' to incompatible units '{toUnits}'.");
        }

        if (string.Equals(from, to, StringComparison.Ordinal))
        {
            return value;
        }

        if (ScaleToBase.TryGetValue(from, out var fromScale) && ScaleToBase.TryGetValue(to, out var toScale))
        {
            return value * fromScale / toScale;
        }

        return value;
    }

    /// <summary>Returns the canonical spelling for a supported unit without changing its value.</summary>
    public static string Canonicalize(string units)
    {
        var normalized = Normalize(units);
        if (!Dimensions.ContainsKey(normalized))
        {
            throw new InvalidOperationException($"Unsupported units '{units}'.");
        }

        return normalized;
    }

    /// <summary>Returns whether two units have the same canonical dimension.</summary>
    public static bool IsCompatible(string fromUnits, string toUnits)
    {
        var from = Normalize(fromUnits);
        var to = Normalize(toUnits);
        return Dimensions.TryGetValue(from, out var fromDimension) &&
               Dimensions.TryGetValue(to, out var toDimension) &&
               string.Equals(fromDimension, toDimension, StringComparison.Ordinal);
    }

    private static string Normalize(string units)
    {
        var value = (units ?? "").Trim();
        if (UnitAliases.TryGetValue(value, out var directAlias))
        {
            return directAlias;
        }

        var compatibility = value
            .Normalize(NormalizationForm.FormKC)
            .Replace('μ', 'u')
            .Replace('µ', 'u');
        if (UnitAliases.TryGetValue(compatibility, out var compatibilityAlias))
        {
            return compatibilityAlias;
        }

        return Dimensions.Keys.FirstOrDefault(key => string.Equals(key, compatibility, StringComparison.OrdinalIgnoreCase))
            ?? compatibility;
    }

    private static void RequireFinite(double value, string name)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            throw new InvalidOperationException($"{name} must be finite.");
        }
    }
}
/// <summary>Combines a strongly typed physical model with required provenance metadata.</summary>
public sealed class CharacterizedPhysicalModel
{
    /// <summary>Initializes a characterized physical model.</summary>
    public CharacterizedPhysicalModel(PhysicalModelDefinition definition, PhysicalModelMetadata metadata)
    {
        Definition = definition ?? throw new ArgumentNullException(nameof(definition));
        Metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
        var metadataIssues = ValidateMetadata(metadata).ToList();
        if (metadataIssues.Any(issue => issue.Severity == PhysicalModelIssueSeverity.Error))
        {
            throw new InvalidOperationException(string.Join("; ", metadataIssues.Select(issue => issue.Message)));
        }
    }

    /// <summary>Gets the stable model id.</summary>
    public string Id => Definition.Id;
    /// <summary>Gets the strongly typed model definition.</summary>
    public PhysicalModelDefinition Definition { get; }
    /// <summary>Gets source, version, calibration, range, and notes metadata.</summary>
    public PhysicalModelMetadata Metadata { get; }

    /// <summary>Evaluates with unit conversion, range checks, and structured diagnostics.</summary>
    public PhysicalModelEvaluationResult TryEvaluate(IReadOnlyDictionary<string, PhysicalModelInputValue> inputs)
    {
        var errors = new List<PhysicalModelIssue>();
        var warnings = ValidateMetadata(Metadata).ToList();
        var normalized = new Dictionary<string, double>(StringComparer.Ordinal);

        if (!Metadata.Calibrated)
        {
            warnings.Add(new PhysicalModelIssue(
                "ModelUncalibratedWarning",
                PhysicalModelIssueSeverity.Warning,
                "$.metadata.calibrated",
                $"Model '{Id}' is not marked calibrated.",
                Id));
        }

        foreach (var input in Definition.Inputs)
        {
            if (!inputs.TryGetValue(input.Name, out var supplied))
            {
                errors.Add(new PhysicalModelIssue(
                    "ModelInputMissing",
                    PhysicalModelIssueSeverity.Error,
                    $"$.inputs.{input.Name}",
                    $"Model input '{input.Name}' is required.",
                    input.Name));
                continue;
            }

            try
            {
                var converted = PhysicalUnitConverter.Convert(supplied.Value, supplied.Units, input.Units);
                normalized[input.Name] = converted;
                CheckRange(input.Name, converted, warnings, errors);
            }
            catch (InvalidOperationException exception)
            {
                errors.Add(new PhysicalModelIssue(
                    "ModelUnitIncompatible",
                    PhysicalModelIssueSeverity.Error,
                    $"$.inputs.{input.Name}.units",
                    exception.Message,
                    input.Name));
            }
        }

        if (errors.Count > 0)
        {
            return PhysicalModelEvaluationResult.Failed(errors, warnings.Where(issue => issue.Severity == PhysicalModelIssueSeverity.Warning));
        }

        try
        {
            return PhysicalModelEvaluationResult.Succeeded(
                Definition.Evaluate(normalized),
                warnings.Where(issue => issue.Severity == PhysicalModelIssueSeverity.Warning));
        }
        catch (InvalidOperationException exception)
        {
            return PhysicalModelEvaluationResult.Failed(
                [MapEvaluationException(exception, Id)],
                warnings.Where(issue => issue.Severity == PhysicalModelIssueSeverity.Warning));
        }
    }

    private void CheckRange(string inputName, double value, List<PhysicalModelIssue> warnings, List<PhysicalModelIssue> errors)
    {
        if (!Metadata.ValidRanges.TryGetValue(inputName, out var range))
        {
            return;
        }

        double minimum;
        double maximum;
        try
        {
            var inputUnits = Definition.Inputs.Single(input => string.Equals(input.Name, inputName, StringComparison.Ordinal)).Units;
            minimum = PhysicalUnitConverter.Convert(range.Minimum, range.Units, inputUnits);
            maximum = PhysicalUnitConverter.Convert(range.Maximum, range.Units, inputUnits);
        }
        catch (InvalidOperationException exception)
        {
            errors.Add(new PhysicalModelIssue(
                "ModelValidRangeUnitError",
                PhysicalModelIssueSeverity.Error,
                $"$.metadata.validRanges.{inputName}",
                exception.Message,
                inputName));
            return;
        }

        if (value >= minimum && value <= maximum)
        {
            return;
        }

        var issue = new PhysicalModelIssue(
            "ModelInputOutOfRange",
            Metadata.RangePolicy == PhysicalModelRangePolicy.Error ? PhysicalModelIssueSeverity.Error : PhysicalModelIssueSeverity.Warning,
            $"$.inputs.{inputName}",
            $"Input '{inputName}' value {value.ToString(CultureInfo.InvariantCulture)} is outside valid range [{minimum.ToString(CultureInfo.InvariantCulture)}, {maximum.ToString(CultureInfo.InvariantCulture)}].",
            inputName);
        if (issue.Severity == PhysicalModelIssueSeverity.Error)
        {
            errors.Add(issue);
        }
        else
        {
            warnings.Add(issue);
        }
    }

    internal static IEnumerable<PhysicalModelIssue> ValidateMetadata(PhysicalModelMetadata metadata)
    {
        if (string.IsNullOrWhiteSpace(metadata.Source))
        {
            yield return new PhysicalModelIssue(
                "ModelSourceRequired",
                PhysicalModelIssueSeverity.Error,
                "$.metadata.source",
                "Physical model metadata source is required.");
        }

        foreach (var (name, range) in metadata.ValidRanges)
        {
            if (double.IsNaN(range.Minimum) || double.IsNaN(range.Maximum) || double.IsInfinity(range.Minimum) || double.IsInfinity(range.Maximum))
            {
                yield return new PhysicalModelIssue(
                    "ModelValidRangeNonFinite",
                    PhysicalModelIssueSeverity.Error,
                    $"$.metadata.validRanges.{name}",
                    $"Valid range for '{name}' must be finite.",
                    name);
            }

            if (range.Maximum < range.Minimum)
            {
                yield return new PhysicalModelIssue(
                    "ModelValidRangeInvalid",
                    PhysicalModelIssueSeverity.Error,
                    $"$.metadata.validRanges.{name}",
                    $"Valid range for '{name}' has maximum below minimum.",
                    name);
            }
        }
    }

    internal static PhysicalModelIssue MapEvaluationException(InvalidOperationException exception, string modelId)
    {
        var message = exception.Message;
        var code = message switch
        {
            var item when item.Contains("not allowed", StringComparison.OrdinalIgnoreCase) => "ModelExpressionRejected",
            var item when item.Contains("complexity", StringComparison.OrdinalIgnoreCase) => "ModelExpressionTooComplex",
            var item when item.Contains("depth", StringComparison.OrdinalIgnoreCase) => "ModelExpressionTooDeep",
            var item when item.Contains("length", StringComparison.OrdinalIgnoreCase) => "ModelExpressionTooLong",
            var item when item.Contains("outside axis range", StringComparison.OrdinalIgnoreCase) => "ModelLookupOutOfRange",
            var item when item.Contains("finite", StringComparison.OrdinalIgnoreCase) => "ModelNonFiniteValue",
            _ => "ModelEvaluationError"
        };
        return new PhysicalModelIssue(code, PhysicalModelIssueSeverity.Error, "$", message, modelId);
    }
}
/// <summary>Contains physical models imported from JSON or CSV plus structured diagnostics.</summary>
public sealed class PhysicalModelImportResult
{
    private PhysicalModelImportResult(
        IReadOnlyList<CharacterizedPhysicalModel> models,
        IReadOnlyList<PhysicalModelIssue> errors,
        IReadOnlyList<PhysicalModelIssue> warnings)
    {
        Models = models;
        Errors = errors;
        Warnings = warnings;
    }

    /// <summary>Gets whether import succeeded without errors.</summary>
    public bool IsSuccess => Errors.Count == 0;
    /// <summary>Gets imported models.</summary>
    public IReadOnlyList<CharacterizedPhysicalModel> Models { get; }
    /// <summary>Gets fatal import diagnostics.</summary>
    public IReadOnlyList<PhysicalModelIssue> Errors { get; }
    /// <summary>Gets non-fatal import diagnostics.</summary>
    public IReadOnlyList<PhysicalModelIssue> Warnings { get; }

    /// <summary>Creates a successful import result.</summary>
    public static PhysicalModelImportResult Succeeded(
        IReadOnlyList<CharacterizedPhysicalModel> models,
        IEnumerable<PhysicalModelIssue>? warnings = null) =>
        new(models, [], (warnings ?? []).ToList());

    /// <summary>Creates a failed import result.</summary>
    public static PhysicalModelImportResult Failed(
        IEnumerable<PhysicalModelIssue> errors,
        IEnumerable<PhysicalModelIssue>? warnings = null) =>
        new([], errors.ToList(), (warnings ?? []).ToList());
}

/// <summary>Imports Phase 7 physical models from formal JSON and CSV formats.</summary>
public static class PhysicalModelImporter
{
    private static readonly string[] CsvRequiredColumns =
    [
        "model_id",
        "kind",
        "output_quantity",
        "output_units",
        "input_x",
        "input_x_units",
        "input_y",
        "input_y_units",
        "x",
        "y",
        "value",
        "interpolation",
        "expression",
        "source",
        "version",
        "citation",
        "calibrated",
        "notes"
    ];

    /// <summary>Imports one model or an array of models from JSON.</summary>
    public static PhysicalModelImportResult FromJson(string json)
    {
        var errors = new List<PhysicalModelIssue>();
        var warnings = new List<PhysicalModelIssue>();
        var models = new List<CharacterizedPhysicalModel>();
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind == JsonValueKind.Array)
            {
                var index = 0;
                foreach (var element in document.RootElement.EnumerateArray())
                {
                    AddJsonModel(element, $"$[{index.ToString(CultureInfo.InvariantCulture)}]", models, errors, warnings);
                    index++;
                }
            }
            else if (document.RootElement.ValueKind == JsonValueKind.Object)
            {
                AddJsonModel(document.RootElement, "$", models, errors, warnings);
            }
            else
            {
                errors.Add(Error("ModelJsonRootInvalid", "$", "Physical model JSON root must be an object or array."));
            }
        }
        catch (JsonException exception)
        {
            errors.Add(Error("ModelJsonInvalid", exception.Path ?? "$", exception.Message));
        }

        return errors.Count == 0
            ? PhysicalModelImportResult.Succeeded(models, warnings)
            : PhysicalModelImportResult.Failed(errors, warnings);
    }

    /// <summary>Imports models from the Phase 7 CSV table format.</summary>
    public static PhysicalModelImportResult FromCsv(string csv)
    {
        var errors = new List<PhysicalModelIssue>();
        var warnings = new List<PhysicalModelIssue>();
        var models = new List<CharacterizedPhysicalModel>();
        var lines = csv.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length < 2)
        {
            return PhysicalModelImportResult.Failed([Error("ModelCsvEmpty", "$", "CSV must include a header and at least one data row.")]);
        }

        var headers = SplitCsvLine(lines[0]);
        var indexByHeader = headers
            .Select((header, index) => new { Header = header.Trim(), Index = index })
            .ToDictionary(item => item.Header, item => item.Index, StringComparer.OrdinalIgnoreCase);
        foreach (var required in CsvRequiredColumns)
        {
            if (!indexByHeader.ContainsKey(required))
            {
                errors.Add(Error("ModelCsvMissingColumn", "$.header", $"CSV is missing required column '{required}'.", required));
            }
        }

        if (errors.Count > 0)
        {
            return PhysicalModelImportResult.Failed(errors);
        }

        var rows = new List<Dictionary<string, string>>();
        for (var lineIndex = 1; lineIndex < lines.Length; lineIndex++)
        {
            var values = SplitCsvLine(lines[lineIndex]);
            if (values.Count != headers.Count)
            {
                errors.Add(Error("ModelCsvColumnCount", $"$[{lineIndex.ToString(CultureInfo.InvariantCulture)}]", "CSV row column count does not match the header."));
                continue;
            }

            var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var index = 0; index < headers.Count; index++)
            {
                row[headers[index]] = values[index].Trim();
            }

            rows.Add(row);
        }

        foreach (var group in rows.GroupBy(row => row["model_id"], StringComparer.OrdinalIgnoreCase))
        {
            AddCsvModel(group.Key, group.ToList(), models, errors, warnings);
        }

        return errors.Count == 0
            ? PhysicalModelImportResult.Succeeded(models, warnings)
            : PhysicalModelImportResult.Failed(errors, warnings);
    }

    private static void AddJsonModel(
        JsonElement element,
        string location,
        List<CharacterizedPhysicalModel> models,
        List<PhysicalModelIssue> errors,
        List<PhysicalModelIssue> warnings)
    {
        try
        {
            var id = RequiredString(element, "id", location, errors);
            var kind = RequiredString(element, "kind", location, errors);
            var outputQuantity = RequiredString(element, "outputQuantity", location, errors);
            var outputUnits = RequiredString(element, "outputUnits", location, errors);
            var metadata = ParseMetadata(element.TryGetProperty("metadata", out var metadataElement) ? metadataElement : default, $"{location}.metadata", errors);
            if (errors.Any(issue => issue.Location.StartsWith(location, StringComparison.Ordinal)))
            {
                return;
            }

            var definition = kind.ToLowerInvariant() switch
            {
                "constant" => PhysicalModelDefinition.Constant(id, outputQuantity, outputUnits, RequiredDouble(element, "value", location, errors)),
                "safeexpression" or "expression" => PhysicalModelDefinition.SafeExpression(
                    id,
                    outputQuantity,
                    outputUnits,
                    RequiredString(element, "expression", location, errors),
                    ParseInputs(element, location, errors)),
                "lookuptable1d" or "lut1d" => PhysicalModelDefinition.LookupTable1D(
                    id,
                    outputQuantity,
                    outputUnits,
                    ParseInputs(element, location, errors).Single(),
                    ParseDoubleArray(element, "xAxis", location, errors),
                    ParseDoubleArray(element, "values", location, errors),
                    ParseInterpolation(element, location, errors, LookupInterpolationMode.Linear)),
                "lookuptable2d" or "lut2d" => CreateJson2DDefinition(element, id, outputQuantity, outputUnits, location, errors),
                _ => throw new InvalidOperationException($"Unsupported model kind '{kind}'.")
            };

            var model = new CharacterizedPhysicalModel(definition, metadata);
            warnings.AddRange(CharacterizedPhysicalModel.ValidateMetadata(metadata)
                .Where(issue => issue.Severity == PhysicalModelIssueSeverity.Warning));
            models.Add(model);
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
        {
            errors.Add(Error("ModelJsonImportError", location, exception.Message));
        }
    }

    private static PhysicalModelDefinition CreateJson2DDefinition(
        JsonElement element,
        string id,
        string outputQuantity,
        string outputUnits,
        string location,
        List<PhysicalModelIssue> errors)
    {
        var inputs = ParseInputs(element, location, errors);
        if (inputs.Count != 2)
        {
            throw new InvalidOperationException("2D lookup table models require exactly two inputs.");
        }

        return PhysicalModelDefinition.LookupTable2D(
            id,
            outputQuantity,
            outputUnits,
            inputs[0],
            inputs[1],
            ParseDoubleArray(element, "xAxis", location, errors),
            ParseDoubleArray(element, "yAxis", location, errors),
            ParseDoubleArray(element, "values", location, errors),
            ParseInterpolation(element, location, errors, LookupInterpolationMode.Bilinear));
    }

    private static void AddCsvModel(
        string modelId,
        IReadOnlyList<Dictionary<string, string>> rows,
        List<CharacterizedPhysicalModel> models,
        List<PhysicalModelIssue> errors,
        List<PhysicalModelIssue> warnings)
    {
        if (string.IsNullOrWhiteSpace(modelId))
        {
            errors.Add(Error("ModelCsvMissingId", "$", "CSV model_id is required."));
            return;
        }

        try
        {
            var first = rows[0];
            var kind = first["kind"].Trim().ToLowerInvariant();
            var outputQuantity = RequireCsvText(first, "output_quantity", modelId);
            var outputUnits = RequireCsvText(first, "output_units", modelId);
            var metadata = new PhysicalModelMetadata
            {
                Source = first["source"],
                Version = first["version"],
                Citation = first["citation"],
                Notes = first["notes"],
                Calibrated = bool.TryParse(first["calibrated"], out var calibrated) && calibrated
            };

            PhysicalModelDefinition definition = kind switch
            {
                "constant" => PhysicalModelDefinition.Constant(modelId, outputQuantity, outputUnits, ParseCsvDouble(first, "value", modelId)),
                "safeexpression" or "expression" => PhysicalModelDefinition.SafeExpression(
                    modelId,
                    outputQuantity,
                    outputUnits,
                    RequireCsvText(first, "expression", modelId),
                    CsvInputs(first, requireY: false)),
                "lookuptable1d" or "lut1d" => Csv1D(modelId, outputQuantity, outputUnits, rows),
                "lookuptable2d" or "lut2d" => Csv2D(modelId, outputQuantity, outputUnits, rows),
                _ => throw new InvalidOperationException($"Unsupported CSV model kind '{first["kind"]}'.")
            };

            var model = new CharacterizedPhysicalModel(definition, metadata);
            if (!model.Metadata.Calibrated)
            {
                warnings.Add(new PhysicalModelIssue("ModelUncalibratedWarning", PhysicalModelIssueSeverity.Warning, "$.metadata.calibrated", $"Model '{modelId}' is not marked calibrated.", modelId));
            }

            models.Add(model);
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
        {
            errors.Add(Error("ModelCsvImportError", $"$.models.{modelId}", exception.Message, modelId));
        }
    }

    private static PhysicalModelDefinition Csv1D(string modelId, string outputQuantity, string outputUnits, IReadOnlyList<Dictionary<string, string>> rows)
    {
        var first = rows[0];
        var axis = rows.Select(row => ParseCsvDouble(row, "x", modelId)).ToList();
        var values = rows.Select(row => ParseCsvDouble(row, "value", modelId)).ToList();
        var interpolation = ParseCsvInterpolation(first, LookupInterpolationMode.Linear);
        return PhysicalModelDefinition.LookupTable1D(
            modelId,
            outputQuantity,
            outputUnits,
            CsvInputs(first, requireY: false).Single(),
            axis,
            values,
            interpolation);
    }

    private static PhysicalModelDefinition Csv2D(string modelId, string outputQuantity, string outputUnits, IReadOnlyList<Dictionary<string, string>> rows)
    {
        var first = rows[0];
        var xAxis = rows.Select(row => ParseCsvDouble(row, "x", modelId)).Distinct().OrderBy(value => value).ToList();
        var yAxis = rows.Select(row => ParseCsvDouble(row, "y", modelId)).Distinct().OrderBy(value => value).ToList();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var byPoint = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            var x = ParseCsvDouble(row, "x", modelId);
            var y = ParseCsvDouble(row, "y", modelId);
            var key = $"{x.ToString(CultureInfo.InvariantCulture)}|{y.ToString(CultureInfo.InvariantCulture)}";
            if (!seen.Add(key))
            {
                throw new InvalidOperationException($"Duplicate 2D LUT point ({x.ToString(CultureInfo.InvariantCulture)}, {y.ToString(CultureInfo.InvariantCulture)}).");
            }

            byPoint[key] = ParseCsvDouble(row, "value", modelId);
        }

        var values = new List<double>();
        foreach (var y in yAxis)
        {
            foreach (var x in xAxis)
            {
                var key = $"{x.ToString(CultureInfo.InvariantCulture)}|{y.ToString(CultureInfo.InvariantCulture)}";
                if (!byPoint.TryGetValue(key, out var value))
                {
                    throw new InvalidOperationException($"2D LUT missing point ({x.ToString(CultureInfo.InvariantCulture)}, {y.ToString(CultureInfo.InvariantCulture)}).");
                }

                values.Add(value);
            }
        }

        return PhysicalModelDefinition.LookupTable2D(
            modelId,
            outputQuantity,
            outputUnits,
            CsvInputs(first, requireY: true)[0],
            CsvInputs(first, requireY: true)[1],
            xAxis,
            yAxis,
            values,
            ParseCsvInterpolation(first, LookupInterpolationMode.Bilinear));
    }

    private static IReadOnlyList<PhysicalModelVariable> CsvInputs(Dictionary<string, string> row, bool requireY)
    {
        var result = new List<PhysicalModelVariable>
        {
            new(RequireCsvText(row, "input_x", row["model_id"]), RequireCsvText(row, "input_x_units", row["model_id"]))
        };
        if (requireY || !string.IsNullOrWhiteSpace(row["input_y"]))
        {
            result.Add(new PhysicalModelVariable(RequireCsvText(row, "input_y", row["model_id"]), RequireCsvText(row, "input_y_units", row["model_id"])));
        }

        return result;
    }

    private static PhysicalModelMetadata ParseMetadata(JsonElement element, string location, List<PhysicalModelIssue> errors)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            errors.Add(Error("ModelMetadataMissing", location, "Physical model metadata object is required."));
            return new PhysicalModelMetadata();
        }

        var metadata = new PhysicalModelMetadata
        {
            Source = OptionalString(element, "source"),
            Citation = OptionalString(element, "citation"),
            Version = OptionalString(element, "version"),
            Notes = OptionalString(element, "notes"),
            Calibrated = OptionalBool(element, "calibrated"),
            RangePolicy = OptionalString(element, "rangePolicy").Equals("error", StringComparison.OrdinalIgnoreCase)
                ? PhysicalModelRangePolicy.Error
                : PhysicalModelRangePolicy.Warning
        };

        if (element.TryGetProperty("validRanges", out var ranges) && ranges.ValueKind == JsonValueKind.Object)
        {
            foreach (var range in ranges.EnumerateObject())
            {
                metadata.ValidRanges[range.Name] = new PhysicalModelRange(
                    RequiredDouble(range.Value, "minimum", $"{location}.validRanges.{range.Name}", errors),
                    RequiredDouble(range.Value, "maximum", $"{location}.validRanges.{range.Name}", errors),
                    RequiredString(range.Value, "units", $"{location}.validRanges.{range.Name}", errors));
            }
        }

        foreach (var issue in CharacterizedPhysicalModel.ValidateMetadata(metadata))
        {
            if (issue.Severity == PhysicalModelIssueSeverity.Error)
            {
                errors.Add(issue);
            }
        }

        return metadata;
    }

    private static IReadOnlyList<PhysicalModelVariable> ParseInputs(JsonElement element, string location, List<PhysicalModelIssue> errors)
    {
        if (!element.TryGetProperty("inputs", out var inputs) || inputs.ValueKind != JsonValueKind.Array)
        {
            errors.Add(Error("ModelInputsMissing", $"{location}.inputs", "Model inputs array is required."));
            return [];
        }

        var result = new List<PhysicalModelVariable>();
        var index = 0;
        foreach (var input in inputs.EnumerateArray())
        {
            result.Add(new PhysicalModelVariable(
                RequiredString(input, "name", $"{location}.inputs[{index.ToString(CultureInfo.InvariantCulture)}]", errors),
                RequiredString(input, "units", $"{location}.inputs[{index.ToString(CultureInfo.InvariantCulture)}]", errors)));
            index++;
        }

        return result;
    }

    private static IReadOnlyList<double> ParseDoubleArray(JsonElement element, string name, string location, List<PhysicalModelIssue> errors)
    {
        if (!element.TryGetProperty(name, out var array) || array.ValueKind != JsonValueKind.Array)
        {
            errors.Add(Error("ModelArrayMissing", $"{location}.{name}", $"Array '{name}' is required."));
            return [];
        }

        return array.EnumerateArray().Select((item, index) => ReadFiniteDouble(item, $"{location}.{name}[{index.ToString(CultureInfo.InvariantCulture)}]")).ToList();
    }

    private static double RequiredDouble(JsonElement element, string name, string location, List<PhysicalModelIssue> errors)
    {
        if (!element.TryGetProperty(name, out var value))
        {
            errors.Add(Error("ModelPropertyMissing", $"{location}.{name}", $"Property '{name}' is required."));
            return 0;
        }

        return ReadFiniteDouble(value, $"{location}.{name}");
    }

    private static double ReadFiniteDouble(JsonElement element, string location)
    {
        if (element.ValueKind != JsonValueKind.Number || !element.TryGetDouble(out var value) || double.IsNaN(value) || double.IsInfinity(value))
        {
            throw new InvalidOperationException($"Numeric value at '{location}' must be finite.");
        }

        return value;
    }

    private static string RequiredString(JsonElement element, string name, string location, List<PhysicalModelIssue> errors)
    {
        if (!element.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
        {
            errors.Add(Error("ModelPropertyMissing", $"{location}.{name}", $"Property '{name}' is required."));
            return "";
        }

        return value.GetString()!.Trim();
    }

    private static string OptionalString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";

    private static bool OptionalBool(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

    private static LookupInterpolationMode ParseInterpolation(JsonElement element, string location, List<PhysicalModelIssue> errors, LookupInterpolationMode fallback)
    {
        if (!element.TryGetProperty("interpolation", out var value) || value.ValueKind != JsonValueKind.String)
        {
            return fallback;
        }

        return Enum.TryParse<LookupInterpolationMode>(value.GetString(), ignoreCase: true, out var parsed)
            ? parsed
            : throw new InvalidOperationException($"Unsupported interpolation '{value.GetString()}' at {location}.interpolation.");
    }

    private static LookupInterpolationMode ParseCsvInterpolation(Dictionary<string, string> row, LookupInterpolationMode fallback) =>
        string.IsNullOrWhiteSpace(row["interpolation"])
            ? fallback
            : Enum.TryParse<LookupInterpolationMode>(row["interpolation"], ignoreCase: true, out var parsed)
                ? parsed
                : throw new InvalidOperationException($"Unsupported interpolation '{row["interpolation"]}'.");

    private static string RequireCsvText(Dictionary<string, string> row, string column, string modelId)
    {
        if (!row.TryGetValue(column, out var value) || string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"CSV model '{modelId}' requires column '{column}'.");
        }

        return value.Trim();
    }

    private static double ParseCsvDouble(Dictionary<string, string> row, string column, string modelId)
    {
        var raw = RequireCsvText(row, column, modelId);
        if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || double.IsNaN(value) || double.IsInfinity(value))
        {
            throw new InvalidOperationException($"CSV model '{modelId}' column '{column}' must be finite numeric data.");
        }

        return value;
    }

    private static List<string> SplitCsvLine(string line)
    {
        var result = new List<string>();
        var builder = new StringBuilder();
        var inQuotes = false;
        for (var index = 0; index < line.Length; index++)
        {
            var current = line[index];
            if (current == '"')
            {
                if (inQuotes && index + 1 < line.Length && line[index + 1] == '"')
                {
                    builder.Append('"');
                    index++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (current == ',' && !inQuotes)
            {
                result.Add(builder.ToString());
                builder.Clear();
            }
            else
            {
                builder.Append(current);
            }
        }

        result.Add(builder.ToString());
        return result;
    }

    private static PhysicalModelIssue Error(string code, string location, string message, string? relatedId = null) =>
        new(code, PhysicalModelIssueSeverity.Error, location, message, relatedId);
}
/// <summary>Identifies whether a physical model is bound to a component or link.</summary>
public enum ModelBindingTargetKind
{
    /// <summary>Model target is a component.</summary>
    Component,
    /// <summary>Model target is a link.</summary>
    Link
}

/// <summary>Records the exact physical model binding consumed by the compiler.</summary>
public sealed class ModelBindingSnapshot
{
    /// <summary>Gets or sets the deterministic snapshot id.</summary>
    public string Id { get; init; } = "";
    /// <summary>Gets or sets the target kind.</summary>
    public ModelBindingTargetKind TargetKind { get; init; }
    /// <summary>Gets or sets the component or link id.</summary>
    public string TargetId { get; init; } = "";
    /// <summary>Gets or sets the physical model id.</summary>
    public string ModelId { get; init; } = "";
    /// <summary>Gets or sets the model version used by this compile.</summary>
    public string ModelVersion { get; init; } = "";
    /// <summary>Gets or sets the output quantity.</summary>
    public string OutputQuantity { get; init; } = "";
    /// <summary>Gets or sets the output units.</summary>
    public string OutputUnits { get; init; } = "";
    /// <summary>Gets or sets the required source metadata.</summary>
    public string Source { get; init; } = "";
    /// <summary>Gets or sets citation metadata.</summary>
    public string Citation { get; init; } = "";
    /// <summary>Gets or sets notes metadata.</summary>
    public string Notes { get; init; } = "";
    /// <summary>Gets or sets whether the model was calibrated.</summary>
    public bool Calibrated { get; init; }
    /// <summary>Gets or sets the deterministic model definition hash.</summary>
    public string ModelHash { get; init; } = "";
    /// <summary>Gets or sets compile-time parameters converted to model input units.</summary>
    public IReadOnlyDictionary<string, double> Parameters { get; init; } =
        new ReadOnlyDictionary<string, double>(new Dictionary<string, double>());
    /// <summary>Gets or sets units for compile-time parameters.</summary>
    public IReadOnlyDictionary<string, string> ParameterUnits { get; init; } =
        new ReadOnlyDictionary<string, string>(new Dictionary<string, string>());
    /// <summary>Gets or sets valid ranges carried into provenance.</summary>
    public IReadOnlyDictionary<string, PhysicalModelRange> ValidRanges { get; init; } =
        new ReadOnlyDictionary<string, PhysicalModelRange>(new Dictionary<string, PhysicalModelRange>());
}

/// <summary>Stores a runtime-independent characterized profile snapshot for later template/compiler phases.</summary>
public sealed class CharacterizedProfileSnapshot
{
    /// <summary>Gets or sets the deterministic profile id.</summary>
    public string Id { get; init; } = "";
    /// <summary>Gets or sets the target kind.</summary>
    public ModelBindingTargetKind TargetKind { get; init; }
    /// <summary>Gets or sets the component or link id.</summary>
    public string TargetId { get; init; } = "";
    /// <summary>Gets or sets the source model id.</summary>
    public string ModelId { get; init; } = "";
    /// <summary>Gets or sets the output quantity.</summary>
    public string OutputQuantity { get; init; } = "";
    /// <summary>Gets or sets the output units.</summary>
    public string Units { get; init; } = "";
    /// <summary>Gets or sets the evaluated compile-time scalar value.</summary>
    public double Value { get; init; }
    /// <summary>Gets or sets the model source.</summary>
    public string Source { get; init; } = "";
    /// <summary>Gets or sets the model version.</summary>
    public string Version { get; init; } = "";
    /// <summary>Gets or sets whether the profile came from calibrated model data.</summary>
    public bool Calibrated { get; init; }
    /// <summary>Gets or sets valid ranges copied into the snapshot.</summary>
    public IReadOnlyDictionary<string, PhysicalModelRange> ValidRanges { get; init; } =
        new ReadOnlyDictionary<string, PhysicalModelRange>(new Dictionary<string, PhysicalModelRange>());
    /// <summary>Gets or sets compile-time parameters copied into the snapshot.</summary>
    public IReadOnlyDictionary<string, double> CompileTimeParameters { get; init; } =
        new ReadOnlyDictionary<string, double>(new Dictionary<string, double>());
    /// <summary>Gets or sets the deterministic profile hash.</summary>
    public string Hash { get; init; } = "";
}

/// <summary>Provides traceable bind, override, and unbind operations for Phase 7 models.</summary>
public static class PhysicalModelBinding
{
    /// <summary>Binds a model id to a component and records the bind action in parameters.</summary>
    public static void BindComponent(HardwareComponent component, string modelId)
    {
        component.ModelRef = RequireModelId(modelId);
        component.Parameters["model_binding_action"] = "bind";
        component.Parameters["model_binding_target"] = "component";
    }

    /// <summary>Binds a model id to a link and records the bind action in parameters.</summary>
    public static void BindLink(HardwareLink link, string modelId)
    {
        link.ModelRef = RequireModelId(modelId);
        link.Parameters["model_binding_action"] = "bind";
        link.Parameters["model_binding_target"] = "link";
    }

    /// <summary>Unbinds a component model reference and records the unbind action.</summary>
    public static void UnbindComponent(HardwareComponent component)
    {
        component.ModelRef = null;
        component.Parameters["model_binding_action"] = "unbind";
        component.Parameters.Remove("model_ref");
    }

    /// <summary>Unbinds a link model reference and records the unbind action.</summary>
    public static void UnbindLink(HardwareLink link)
    {
        link.ModelRef = null;
        link.Parameters["model_binding_action"] = "unbind";
        link.Parameters.Remove("model_ref");
    }

    /// <summary>Overrides one model parameter value using explicit units.</summary>
    public static void OverrideParameter(IDictionary<string, string> parameters, string name, double value, string units)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new InvalidOperationException("Override parameter name is required.");
        }

        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            throw new InvalidOperationException("Override parameter value must be finite.");
        }

        parameters[$"model_param_{name.Trim()}"] = value.ToString(CultureInfo.InvariantCulture);
        parameters[$"model_param_{name.Trim()}_units"] = units.Trim();
        parameters["model_binding_action"] = "override";
    }

    private static string RequireModelId(string modelId)
    {
        if (string.IsNullOrWhiteSpace(modelId))
        {
            throw new InvalidOperationException("Model id is required for binding.");
        }

        return modelId.Trim();
    }
}

/// <summary>Defines Phase 7 external executable/plugin model security policy.</summary>
public sealed class PhysicalModelSecurityPolicy
{
    /// <summary>Gets whether external executable or plugin models are allowed.</summary>
    public bool AllowExternalExecutableModels { get; init; }

    /// <summary>Validates a request to use an external executable/plugin model.</summary>
    public IReadOnlyList<PhysicalModelIssue> ValidateExternalModelRequest(string modelId)
    {
        if (AllowExternalExecutableModels)
        {
            return [];
        }

        return
        [
            new PhysicalModelIssue(
                "ExternalModelDisabled",
                PhysicalModelIssueSeverity.Error,
                "$.externalModel",
                "External executable/plugin physical models are disabled by default in Phase 7.",
                modelId)
        ];
    }
}

/// <summary>Contains model binding and profile snapshots emitted by the Phase 7 compiler path.</summary>
public sealed class PhysicalModelCompileSnapshotResult
{
    /// <summary>Gets or sets binding snapshots by deterministic id.</summary>
    public IReadOnlyDictionary<string, ModelBindingSnapshot> BindingSnapshots { get; init; } =
        new ReadOnlyDictionary<string, ModelBindingSnapshot>(new Dictionary<string, ModelBindingSnapshot>());
    /// <summary>Gets or sets profile snapshots by deterministic id.</summary>
    public IReadOnlyDictionary<string, CharacterizedProfileSnapshot> ProfileSnapshots { get; init; } =
        new ReadOnlyDictionary<string, CharacterizedProfileSnapshot>(new Dictionary<string, CharacterizedProfileSnapshot>());
    /// <summary>Gets or sets fatal compile diagnostics.</summary>
    public IReadOnlyList<CompilationIssue> Errors { get; init; } = [];
    /// <summary>Gets or sets non-fatal compile diagnostics.</summary>
    public IReadOnlyList<CompilationIssue> Warnings { get; init; } = [];
}

/// <summary>Builds compiler snapshots for Phase 7 physical model bindings.</summary>
public static class PhysicalModelCompilerSnapshotBuilder
{
    /// <summary>Builds binding and characterized profile snapshots without mutating the graph.</summary>
    public static PhysicalModelCompileSnapshotResult Build(HardwareGraph graph, DeviceModelRegistry? registry)
    {
        var bindings = new Dictionary<string, ModelBindingSnapshot>(StringComparer.Ordinal);
        var profiles = new Dictionary<string, CharacterizedProfileSnapshot>(StringComparer.Ordinal);
        var errors = new List<CompilationIssue>();
        var warnings = new List<CompilationIssue>();
        if (registry is null)
        {
            return new PhysicalModelCompileSnapshotResult
            {
                BindingSnapshots = new ReadOnlyDictionary<string, ModelBindingSnapshot>(bindings),
                ProfileSnapshots = new ReadOnlyDictionary<string, CharacterizedProfileSnapshot>(profiles)
            };
        }

        foreach (var component in graph.Components.Where(component => !string.IsNullOrWhiteSpace(component.ModelRef)))
        {
            var model = registry.FindPhysical(component.ModelRef);
            if (model is not null)
            {
                AddTarget(ModelBindingTargetKind.Component, component.Id, component.Parameters, model, bindings, profiles, errors, warnings);
            }
        }

        foreach (var link in graph.Links.Where(link => !string.IsNullOrWhiteSpace(link.ModelRef)))
        {
            var model = registry.FindPhysical(link.ModelRef);
            if (model is not null)
            {
                AddTarget(ModelBindingTargetKind.Link, link.Id, link.Parameters, model, bindings, profiles, errors, warnings);
            }
        }

        return new PhysicalModelCompileSnapshotResult
        {
            BindingSnapshots = new ReadOnlyDictionary<string, ModelBindingSnapshot>(bindings),
            ProfileSnapshots = new ReadOnlyDictionary<string, CharacterizedProfileSnapshot>(profiles),
            Errors = errors,
            Warnings = warnings
        };
    }

    private static void AddTarget(
        ModelBindingTargetKind targetKind,
        string targetId,
        IReadOnlyDictionary<string, string> parameters,
        CharacterizedPhysicalModel model,
        Dictionary<string, ModelBindingSnapshot> bindings,
        Dictionary<string, CharacterizedProfileSnapshot> profiles,
        List<CompilationIssue> errors,
        List<CompilationIssue> warnings)
    {
        var suppliedInputs = ReadInputs(model, parameters, targetId, errors);
        if (errors.Count > 0)
        {
            return;
        }

        var evaluation = model.TryEvaluate(suppliedInputs);
        warnings.AddRange(evaluation.Warnings.Select(ToCompilationIssue));
        if (!evaluation.IsSuccess || evaluation.Evaluation is null)
        {
            errors.AddRange(evaluation.Errors.Select(ToCompilationIssue));
            return;
        }

        var canonicalParameters = CanonicalParameters(model, suppliedInputs);
        var parameterUnits = model.Definition.Inputs.ToDictionary(input => input.Name, input => input.Units, StringComparer.Ordinal);
        var bindingId = $"{targetKind.ToString().ToLowerInvariant()}:{targetId}:{model.Id}";
        var modelHash = StableHash(new
        {
            model.Id,
            model.Definition.Kind,
            model.Definition.OutputQuantity,
            model.Definition.OutputUnits,
            model.Definition.Expression,
            model.Definition.Interpolation,
            model.Definition.XAxis,
            model.Definition.YAxis,
            model.Definition.Values,
            model.Metadata.Source,
            model.Metadata.Version,
            model.Metadata.Calibrated
        });
        var binding = new ModelBindingSnapshot
        {
            Id = bindingId,
            TargetKind = targetKind,
            TargetId = targetId,
            ModelId = model.Id,
            ModelVersion = model.Metadata.Version,
            OutputQuantity = model.Definition.OutputQuantity,
            OutputUnits = model.Definition.OutputUnits,
            Source = model.Metadata.Source,
            Citation = model.Metadata.Citation,
            Notes = model.Metadata.Notes,
            Calibrated = model.Metadata.Calibrated,
            ModelHash = modelHash,
            Parameters = new ReadOnlyDictionary<string, double>(canonicalParameters),
            ParameterUnits = new ReadOnlyDictionary<string, string>(parameterUnits),
            ValidRanges = new ReadOnlyDictionary<string, PhysicalModelRange>(new Dictionary<string, PhysicalModelRange>(model.Metadata.ValidRanges, StringComparer.Ordinal))
        };
        bindings[binding.Id] = binding;

        var profileHash = StableHash(new
        {
            binding.Id,
            binding.ModelHash,
            binding.Parameters,
            evaluation.Evaluation.Value,
            evaluation.Evaluation.Units
        });
        profiles[$"profile:{binding.Id}"] = new CharacterizedProfileSnapshot
        {
            Id = $"profile:{binding.Id}",
            TargetKind = targetKind,
            TargetId = targetId,
            ModelId = model.Id,
            OutputQuantity = evaluation.Evaluation.Quantity,
            Units = evaluation.Evaluation.Units,
            Value = evaluation.Evaluation.Value,
            Source = model.Metadata.Source,
            Version = model.Metadata.Version,
            Calibrated = model.Metadata.Calibrated,
            ValidRanges = binding.ValidRanges,
            CompileTimeParameters = binding.Parameters,
            Hash = profileHash
        };
    }

    private static Dictionary<string, PhysicalModelInputValue> ReadInputs(
        CharacterizedPhysicalModel model,
        IReadOnlyDictionary<string, string> parameters,
        string targetId,
        List<CompilationIssue> errors)
    {
        var result = new Dictionary<string, PhysicalModelInputValue>(StringComparer.Ordinal);
        foreach (var input in model.Definition.Inputs)
        {
            var valueKey = $"model_param_{input.Name}";
            var unitsKey = $"model_param_{input.Name}_units";
            if (!parameters.TryGetValue(valueKey, out var rawValue) && !parameters.TryGetValue(input.Name, out rawValue))
            {
                errors.Add(new CompilationIssue(
                    "ModelParameterMissing",
                    ValidationSeverity.Error,
                    $"$.parameters.{valueKey}",
                    $"Target '{targetId}' bound to model '{model.Id}' is missing compile-time parameter '{input.Name}'.",
                    targetId));
                continue;
            }

            if (!double.TryParse(rawValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || double.IsNaN(value) || double.IsInfinity(value))
            {
                errors.Add(new CompilationIssue(
                    "ModelParameterInvalid",
                    ValidationSeverity.Error,
                    $"$.parameters.{valueKey}",
                    $"Target '{targetId}' model parameter '{input.Name}' must be finite numeric data.",
                    targetId));
                continue;
            }

            var units = parameters.TryGetValue(unitsKey, out var rawUnits) ? rawUnits : input.Units;
            result[input.Name] = new PhysicalModelInputValue(value, units);
        }

        return result;
    }

    private static Dictionary<string, double> CanonicalParameters(CharacterizedPhysicalModel model, IReadOnlyDictionary<string, PhysicalModelInputValue> supplied)
    {
        var result = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var input in model.Definition.Inputs)
        {
            var value = supplied[input.Name];
            result[input.Name] = PhysicalUnitConverter.Convert(value.Value, value.Units, input.Units);
        }

        return result;
    }

    private static CompilationIssue ToCompilationIssue(PhysicalModelIssue issue) =>
        new(
            issue.Code,
            issue.Severity == PhysicalModelIssueSeverity.Error ? ValidationSeverity.Error : ValidationSeverity.Warning,
            issue.Location,
            issue.Message,
            issue.RelatedId);

    private static string StableHash(object value)
    {
        var json = JsonSerializer.Serialize(value, HardwareGraphJson.Options);
        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(json));
        var builder = new StringBuilder(hash.Length * 2);
        foreach (var item in hash)
        {
            builder.Append(item.ToString("x2", CultureInfo.InvariantCulture));
        }

        return builder.ToString();
    }
}