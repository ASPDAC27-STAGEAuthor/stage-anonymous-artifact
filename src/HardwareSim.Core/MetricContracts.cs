namespace HardwareSim.Core;

#pragma warning disable CS1591 // Public member names carry their explicit metric unit or category.

/// <summary>Energy measured in picojoules.</summary>
public readonly record struct Picojoules
{
    public Picojoules(double value)
    {
        MetricUnitGuard.NonNegativeFinite(value, nameof(value));
        Value = value;
    }

    public double Value { get; }

    public static Picojoules Zero => new(0);

    public static Picojoules operator +(Picojoules left, Picojoules right) => new(left.Value + right.Value);
}

/// <summary>Energy rate measured in picojoules per transferred bit.</summary>
public readonly record struct PicojoulesPerBit
{
    public PicojoulesPerBit(double value)
    {
        MetricUnitGuard.NonNegativeFinite(value, nameof(value));
        Value = value;
    }

    public double Value { get; }

    public static Picojoules operator *(PicojoulesPerBit rate, BitCount bits) => new(rate.Value * bits.Value);
}

/// <summary>Energy rate measured in picojoules per MAC operation.</summary>
public readonly record struct PicojoulesPerMac
{
    public PicojoulesPerMac(double value)
    {
        MetricUnitGuard.NonNegativeFinite(value, nameof(value));
        Value = value;
    }

    public double Value { get; }

    public static Picojoules operator *(PicojoulesPerMac rate, MacCount macs) => new(rate.Value * macs.Value);
}

/// <summary>Physical area measured in square micrometers.</summary>
public readonly record struct SquareMicrometers
{
    public SquareMicrometers(double value)
    {
        MetricUnitGuard.NonNegativeFinite(value, nameof(value));
        Value = value;
    }

    public double Value { get; }

    public static SquareMicrometers Zero => new(0);

    public static SquareMicrometers operator +(SquareMicrometers left, SquareMicrometers right) => new(left.Value + right.Value);
}

/// <summary>Non-negative transferred or serviced bit count.</summary>
public readonly record struct BitCount
{
    public BitCount(long value)
    {
        MetricUnitGuard.NonNegative(value, nameof(value));
        Value = value;
    }

    public long Value { get; }

    public static BitCount Zero => new(0);
}

/// <summary>Non-negative MAC operation count.</summary>
public readonly record struct MacCount
{
    public MacCount(long value)
    {
        MetricUnitGuard.NonNegative(value, nameof(value));
        Value = value;
    }

    public long Value { get; }

    public static MacCount Zero => new(0);
}

/// <summary>Non-negative cycle count.</summary>
public readonly record struct CycleCount
{
    public CycleCount(long value)
    {
        MetricUnitGuard.NonNegative(value, nameof(value));
        Value = value;
    }

    public long Value { get; }

    public static CycleCount Zero => new(0);
}

/// <summary>Physical meaning of an energy contribution.</summary>
public enum EnergyKind
{
    Dynamic,
    Static,
    Leakage,
    Conversion,
    Tuning,
    Calibration
}

/// <summary>System-level destination category for energy aggregation.</summary>
public enum EnergyCategory
{
    Compute,
    Memory,
    NoC,
    Conversion,
    Optical,
    Cim,
    Leakage
}

/// <summary>Mutually exclusive per-cycle component activity state.</summary>
public enum ComponentActivityState
{
    Active,
    Idle,
    Stall
}

/// <summary>Energy split by physical contribution kind.</summary>
public sealed class EnergyBreakdown
{
    public Picojoules Dynamic { get; set; }
    public Picojoules Static { get; set; }
    public Picojoules Leakage { get; set; }
    public Picojoules Conversion { get; set; }
    public Picojoules Tuning { get; set; }
    public Picojoules Calibration { get; set; }

    public Picojoules TotalPJ => Dynamic + Static + Leakage + Conversion + Tuning + Calibration;

    public Picojoules this[EnergyKind kind]
    {
        get => kind switch
        {
            EnergyKind.Dynamic => Dynamic,
            EnergyKind.Static => Static,
            EnergyKind.Leakage => Leakage,
            EnergyKind.Conversion => Conversion,
            EnergyKind.Tuning => Tuning,
            EnergyKind.Calibration => Calibration,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
        };
        set
        {
            switch (kind)
            {
                case EnergyKind.Dynamic: Dynamic = value; break;
                case EnergyKind.Static: Static = value; break;
                case EnergyKind.Leakage: Leakage = value; break;
                case EnergyKind.Conversion: Conversion = value; break;
                case EnergyKind.Tuning: Tuning = value; break;
                case EnergyKind.Calibration: Calibration = value; break;
                default: throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
            }
        }
    }
}

/// <summary>Energy split by system-level aggregation category.</summary>
public sealed class EnergyCategoryBreakdown
{
    public Picojoules Compute { get; set; }
    public Picojoules Memory { get; set; }
    public Picojoules NoC { get; set; }
    public Picojoules Conversion { get; set; }
    public Picojoules Optical { get; set; }
    public Picojoules Cim { get; set; }
    public Picojoules Leakage { get; set; }

    public Picojoules TotalPJ => Compute + Memory + NoC + Conversion + Optical + Cim + Leakage;

    public Picojoules this[EnergyCategory category]
    {
        get => category switch
        {
            EnergyCategory.Compute => Compute,
            EnergyCategory.Memory => Memory,
            EnergyCategory.NoC => NoC,
            EnergyCategory.Conversion => Conversion,
            EnergyCategory.Optical => Optical,
            EnergyCategory.Cim => Cim,
            EnergyCategory.Leakage => Leakage,
            _ => throw new ArgumentOutOfRangeException(nameof(category), category, null)
        };
        set
        {
            switch (category)
            {
                case EnergyCategory.Compute: Compute = value; break;
                case EnergyCategory.Memory: Memory = value; break;
                case EnergyCategory.NoC: NoC = value; break;
                case EnergyCategory.Conversion: Conversion = value; break;
                case EnergyCategory.Optical: Optical = value; break;
                case EnergyCategory.Cim: Cim = value; break;
                case EnergyCategory.Leakage: Leakage = value; break;
                default: throw new ArgumentOutOfRangeException(nameof(category), category, null);
            }
        }
    }
}

internal static class MetricUnitGuard
{
    public static void NonNegativeFinite(double value, string parameterName)
    {
        if (double.IsNaN(value) || double.IsInfinity(value) || value < 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "Metric units must be finite and non-negative.");
        }
    }

    public static void NonNegative(long value, string parameterName)
    {
        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "Metric units must be non-negative.");
        }
    }
}
