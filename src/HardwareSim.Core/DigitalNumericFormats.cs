using System.Globalization;

namespace HardwareSim.Core;

/// <summary>One finite value rounded to an exact supported digital encoding.</summary>
/// <param name="Value">Decoded finite value represented by the encoding.</param>
/// <param name="EncodedBits">Unsigned payload bits in the low bits.</param>
/// <param name="BitWidth">Encoding width.</param>
public readonly record struct DigitalNumericValue(double Value, ulong EncodedBits, int BitWidth);

/// <summary>Deterministic finite-number encoders used by digital component kernels and independent references.</summary>
public static class DigitalNumericFormats
{
    private static readonly (ushort Bits, double Value)[] PositiveHalfValues = BuildPositiveHalfValues();
    private static readonly (byte Bits, double Value)[] PositiveFp8Values = BuildPositiveFp8Values();

    /// <summary>Normalizes one supported dtype token.</summary>
    public static string NormalizeDType(string dtype)
    {
        var normalized = (dtype ?? "").Trim().Replace("_", "", StringComparison.Ordinal).Replace("-", "", StringComparison.Ordinal).ToLowerInvariant();
        return normalized switch
        {
            "fp8" or "fp8e4m3" or "e4m3" => "fp8",
            "fp16" or "binary16" or "half" => "fp16",
            "fp32" or "binary32" or "single" => "fp32",
            _ => throw new ArgumentOutOfRangeException(nameof(dtype), dtype, "Supported digital dtypes are fp8 E4M3FN, fp16, and fp32.")
        };
    }

    /// <summary>Gets the exact digital width of one supported dtype.</summary>
    public static int BitWidth(string dtype) => NormalizeDType(dtype) switch
    {
        "fp8" => 8,
        "fp16" => 16,
        _ => 32
    };

    /// <summary>Gets the matching graph precision enum.</summary>
    public static PrecisionKind Precision(string dtype) => NormalizeDType(dtype) switch
    {
        "fp8" => PrecisionKind.FP8_E4M3,
        "fp16" => PrecisionKind.FP16,
        _ => PrecisionKind.FP32
    };

    /// <summary>Rounds a finite value to the requested dtype using nearest-even and finite saturation.</summary>
    public static DigitalNumericValue Quantize(double value, string dtype)
    {
        if (!double.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, "Digital kernel inputs must be finite.");
        }

        return NormalizeDType(dtype) switch
        {
            "fp8" => QuantizeFp8(value),
            "fp16" => QuantizeHalf(value),
            _ => QuantizeSingle(value)
        };
    }

    /// <summary>Returns a stable SHA-256 artifact over exact encoded values and shape.</summary>
    public static string HashEncodedValues(string name, string dtype, int rows, int columns, IReadOnlyList<double> values)
    {
        var normalized = NormalizeDType(dtype);
        var width = BitWidth(normalized);
        var digits = width / 4;
        var encoded = values.Select(value => Quantize(value, normalized).EncodedBits.ToString($"x{digits}", CultureInfo.InvariantCulture)).ToList();
        return ComponentExecutionJson.ComputeSha256(ComponentExecutionJson.CanonicalizeJson(System.Text.Json.JsonSerializer.Serialize(new
        {
            Name = name,
            DType = normalized,
            Shape = new[] { rows, columns },
            EncodedValues = encoded
        }, HardwareGraphJson.Options)));
    }

    private static DigitalNumericValue QuantizeFp8(double value)
    {
        var negative = IsNegative(value);
        var magnitude = Math.Abs(value);
        var selected = SelectNearest(PositiveFp8Values, magnitude);
        var bits = (byte)(selected.Bits | (negative ? 0x80 : 0));
        var decoded = negative ? -selected.Value : selected.Value;
        if (selected.Value == 0 && negative) decoded = -0.0;
        return new DigitalNumericValue(decoded, bits, 8);
    }

    private static DigitalNumericValue QuantizeHalf(double value)
    {
        var negative = IsNegative(value);
        var magnitude = Math.Abs(value);
        var selected = SelectNearest(PositiveHalfValues, magnitude);
        var bits = (ushort)(selected.Bits | (negative ? 0x8000 : 0));
        var decoded = negative ? -selected.Value : selected.Value;
        if (selected.Value == 0 && negative) decoded = -0.0;
        return new DigitalNumericValue(decoded, bits, 16);
    }

    private static DigitalNumericValue QuantizeSingle(double value)
    {
        var clamped = Math.Max(-float.MaxValue, Math.Min(float.MaxValue, value));
        var rounded = (float)clamped;
        var bits = unchecked((uint)BitConverter.SingleToInt32Bits(rounded));
        return new DigitalNumericValue(rounded, bits, 32);
    }

    private static (TBits Bits, double Value) SelectNearest<TBits>((TBits Bits, double Value)[] values, double magnitude)
        where TBits : struct, IConvertible
    {
        if (magnitude <= 0) return values[0];
        if (magnitude >= values[^1].Value) return values[^1];
        var low = 0;
        var high = values.Length - 1;
        while (low + 1 < high)
        {
            var middle = low + (high - low) / 2;
            if (values[middle].Value <= magnitude) low = middle;
            else high = middle;
        }

        var lowerDistance = magnitude - values[low].Value;
        var upperDistance = values[high].Value - magnitude;
        if (lowerDistance < upperDistance) return values[low];
        if (upperDistance < lowerDistance) return values[high];
        var lowerBits = values[low].Bits.ToUInt64(CultureInfo.InvariantCulture);
        return (lowerBits & 1UL) == 0 ? values[low] : values[high];
    }

    private static (ushort Bits, double Value)[] BuildPositiveHalfValues()
    {
        var values = new (ushort Bits, double Value)[0x7c00];
        for (var bits = 0; bits < values.Length; bits++) values[bits] = ((ushort)bits, DecodePositiveHalf((ushort)bits));
        return values;
    }

    private static double DecodePositiveHalf(ushort bits)
    {
        var exponent = (bits >> 10) & 0x1f;
        var mantissa = bits & 0x03ff;
        if (exponent == 0) return mantissa == 0 ? 0 : mantissa * Math.Pow(2, -24);
        return (1.0 + mantissa / 1024.0) * Math.Pow(2, exponent - 15);
    }

    private static (byte Bits, double Value)[] BuildPositiveFp8Values()
    {
        var values = new (byte Bits, double Value)[0x7f];
        for (var bits = 0; bits < values.Length; bits++) values[bits] = ((byte)bits, DecodePositiveFp8((byte)bits));
        return values;
    }

    private static double DecodePositiveFp8(byte bits)
    {
        var exponent = (bits >> 3) & 0x0f;
        var mantissa = bits & 0x07;
        if (exponent == 0) return mantissa == 0 ? 0 : mantissa * Math.Pow(2, -9);
        return (1.0 + mantissa / 8.0) * Math.Pow(2, exponent - 7);
    }

    private static bool IsNegative(double value) => (unchecked((ulong)BitConverter.DoubleToInt64Bits(value)) & 0x8000000000000000UL) != 0;
}

/// <summary>Independent row-major digital VMM reference used by registered PE test scenarios.</summary>
public static class DigitalVmmReferenceEvaluator
{
    /// <summary>Computes Y = X @ W with operand, per-accumulate, and output quantization.</summary>
    public static IReadOnlyList<double> Evaluate(
        IReadOnlyList<double> activation,
        IReadOnlyList<double> weights,
        int rows,
        int columns,
        string inputDType,
        string weightDType,
        string accumulateDType,
        string outputDType)
    {
        if (activation.Count != rows) throw new ArgumentException($"Activation length {activation.Count} does not match rows {rows}.", nameof(activation));
        if (weights.Count != checked(rows * columns)) throw new ArgumentException($"Weight length {weights.Count} does not match shape {rows}x{columns}.", nameof(weights));
        var quantizedActivation = activation.Select(value => DigitalNumericFormats.Quantize(value, inputDType).Value).ToArray();
        var quantizedWeights = weights.Select(value => DigitalNumericFormats.Quantize(value, weightDType).Value).ToArray();
        var output = new double[columns];
        for (var column = 0; column < columns; column++)
        {
            var accumulator = DigitalNumericFormats.Quantize(0, accumulateDType).Value;
            for (var row = 0; row < rows; row++)
            {
                accumulator = DigitalNumericFormats.Quantize(
                    accumulator + quantizedActivation[row] * quantizedWeights[row * columns + column],
                    accumulateDType).Value;
            }
            output[column] = DigitalNumericFormats.Quantize(accumulator, outputDType).Value;
        }
        return output;
    }
}
