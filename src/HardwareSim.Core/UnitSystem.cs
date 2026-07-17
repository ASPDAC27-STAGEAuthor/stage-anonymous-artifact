namespace HardwareSim.Core;

/// <summary>Provides canonical physical-unit conversions used by compiler and simulation contracts.</summary>
public static class UnitSystem
{
    /// <summary>Converts millimeters to micrometers.</summary>
    public static double MmToUm(double millimeters) => millimeters * 1_000.0;

    /// <summary>Converts micrometers to millimeters.</summary>
    public static double UmToMm(double micrometers) => micrometers / 1_000.0;

    /// <summary>Converts nanoseconds to picoseconds.</summary>
    public static double NsToPs(double nanoseconds) => nanoseconds * 1_000.0;

    /// <summary>Converts picoseconds to nanoseconds.</summary>
    public static double PsToNs(double picoseconds) => picoseconds / 1_000.0;

    /// <summary>Converts joules to picojoules.</summary>
    public static double JToPJ(double joules) => joules * 1_000_000_000_000.0;

    /// <summary>Converts picojoules to joules.</summary>
    public static double PJToJ(double picojoules) => picojoules / 1_000_000_000_000.0;

    /// <summary>Converts watts to milliwatts.</summary>
    public static double WToMW(double watts) => watts * 1_000.0;

    /// <summary>Converts milliwatts to watts.</summary>
    public static double MWToW(double milliwatts) => milliwatts / 1_000.0;

    /// <summary>Converts gigabits per second to bits transferred per clock cycle.</summary>
    public static double GbpsToBitsPerCycle(double gigabitsPerSecond, double clockFrequencyGHz)
    {
        ValidateClockFrequency(clockFrequencyGHz);
        return gigabitsPerSecond / clockFrequencyGHz;
    }

    /// <summary>Converts gigabytes per second to bits transferred per clock cycle.</summary>
    public static double GBpsToBitsPerCycle(double gigabytesPerSecond, double clockFrequencyGHz)
    {
        ValidateClockFrequency(clockFrequencyGHz);
        return gigabytesPerSecond * 8.0 / clockFrequencyGHz;
    }

    /// <summary>Adds logarithmic gain or loss terms expressed in decibels.</summary>
    public static double DbSum(params double[] lossesDb) => lossesDb.Sum();

    /// <summary>Converts optical or electrical power from dBm to milliwatts.</summary>
    public static double DbmToMw(double dbm) => Math.Pow(10.0, dbm / 10.0);

    /// <summary>Converts positive power in milliwatts to dBm.</summary>
    public static double MwToDbm(double milliwatts)
    {
        if (milliwatts <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(milliwatts), "Optical power must be greater than zero milliwatts.");
        }

        return 10.0 * Math.Log10(milliwatts);
    }

    internal static void ValidateClockFrequency(double clockFrequencyGHz)
    {
        if (clockFrequencyGHz <= 0 || double.IsNaN(clockFrequencyGHz) || double.IsInfinity(clockFrequencyGHz))
        {
            throw new ArgumentOutOfRangeException(nameof(clockFrequencyGHz), "Clock frequency must be finite and greater than zero.");
        }
    }
}

/// <summary>Stores a clock frequency and derives period and cycle-time conversions from it.</summary>
public sealed class ClockConfig
{
    private double _clockFrequencyGHz = 1.0;

    /// <summary>Gets or sets the positive clock frequency in gigahertz.</summary>
    public double ClockFrequencyGHz
    {
        get => _clockFrequencyGHz;
        set
        {
            UnitSystem.ValidateClockFrequency(value);
            _clockFrequencyGHz = value;
        }
    }

    /// <summary>Gets the clock period in picoseconds.</summary>
    public double ClockPeriodPs => 1_000.0 / ClockFrequencyGHz;

    /// <summary>Converts an integral cycle count to elapsed picoseconds.</summary>
    public double CyclesToPs(long cycles) => cycles * ClockPeriodPs;

    /// <summary>Converts elapsed picoseconds to the smallest covering integral cycle count.</summary>
    public long PsToCycles(double picoseconds)
    {
        if (picoseconds < 0 || double.IsNaN(picoseconds) || double.IsInfinity(picoseconds))
        {
            throw new ArgumentOutOfRangeException(nameof(picoseconds), "Physical time must be finite and non-negative.");
        }

        return checked((long)Math.Ceiling(picoseconds / ClockPeriodPs));
    }
}
