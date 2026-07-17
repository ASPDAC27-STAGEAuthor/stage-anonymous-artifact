using System.Collections.ObjectModel;
using System.Globalization;

#pragma warning disable CS1591

namespace HardwareSim.Core;

public enum Phase9ConverterKind { Adc, Dac }
public enum Phase9QuantizationRounding { NearestEven, TowardZero, AwayFromZero }
public enum Phase9ClippingPolicy { Saturate, Error }

public sealed record Phase9ConverterOperatingPoint(
    int PrecisionBits,
    int LatencyCycles,
    double EnergyPicojoulesPerSample,
    double ThroughputSamplesPerCycle,
    double MinimumAnalogValue,
    double MaximumAnalogValue,
    Phase9QuantizationRounding Rounding,
    Phase9ClippingPolicy Clipping,
    string ProfileHash,
    IReadOnlyList<string> SourceRecordIds,
    string EvidenceType,
    NormalizedDeviceEvidenceStatus EvidenceStatus,
    string ModelVersion,
    string Formula,
    string Uncertainty,
    string OperatingPointHash)
{
    public static Phase9ConverterOperatingPoint Create(int precisionBits, int latencyCycles, double energyPicojoulesPerSample,
        double throughputSamplesPerCycle, double minimumAnalogValue, double maximumAnalogValue,
        Phase9QuantizationRounding rounding, Phase9ClippingPolicy clipping, string profileHash,
        IEnumerable<string>? sourceRecordIds, string evidenceType, NormalizedDeviceEvidenceStatus evidenceStatus,
        string modelVersion, string formula, string uncertainty)
    {
        if (precisionBits <= 0 || precisionBits > 30) throw new ArgumentOutOfRangeException(nameof(precisionBits));
        if (latencyCycles < 0 || !double.IsFinite(energyPicojoulesPerSample) || energyPicojoulesPerSample < 0 ||
            !double.IsFinite(throughputSamplesPerCycle) || throughputSamplesPerCycle <= 0 ||
            !double.IsFinite(minimumAnalogValue) || !double.IsFinite(maximumAnalogValue) || minimumAnalogValue >= maximumAnalogValue)
            throw new ArgumentOutOfRangeException(nameof(latencyCycles));
        var records = (sourceRecordIds ?? []).Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal).ToArray();
        var hash = ComponentTemplateJson.StableHash(new { precisionBits, latencyCycles, energyPicojoulesPerSample, throughputSamplesPerCycle,
            minimumAnalogValue, maximumAnalogValue, rounding, clipping, profileHash, records, evidenceType, evidenceStatus, modelVersion, formula, uncertainty });
        return new(precisionBits, latencyCycles, energyPicojoulesPerSample, throughputSamplesPerCycle, minimumAnalogValue, maximumAnalogValue,
            rounding, clipping, profileHash ?? "", records, evidenceType ?? "", evidenceStatus, modelVersion ?? "", formula ?? "", uncertainty ?? "", hash);
    }
}

public sealed class Phase9ConverterProfile
{
    public Phase9ConverterProfile(string profileId, Phase9ConverterKind kind, IEnumerable<Phase9ConverterOperatingPoint> operatingPoints, string sweepId = "", bool interpolationAllowed = false)
    {
        ProfileId = string.IsNullOrWhiteSpace(profileId) ? throw new ArgumentException("Profile id is required.", nameof(profileId)) : profileId.Trim();
        Kind = kind;
        var points = (operatingPoints ?? []).OrderBy(value => value.PrecisionBits).ToArray();
        if (points.Length == 0 || points.Select(value => value.PrecisionBits).Distinct().Count() != points.Length) throw new ArgumentException("Converter profile requires unique discrete precision points.", nameof(operatingPoints));
        OperatingPoints = new ReadOnlyCollection<Phase9ConverterOperatingPoint>(points);
        SweepId = sweepId ?? "";
        InterpolationAllowed = interpolationAllowed;
        ProfileHash = ComponentTemplateJson.StableHash(new { ProfileId, Kind, Points = points.Select(value => value.OperatingPointHash), SweepId, InterpolationAllowed });
    }
    public string ProfileId { get; }
    public Phase9ConverterKind Kind { get; }
    public IReadOnlyList<Phase9ConverterOperatingPoint> OperatingPoints { get; }
    public string SweepId { get; }
    public bool InterpolationAllowed { get; }
    public string ProfileHash { get; }
    public Phase9ConverterOperatingPoint? Find(int precisionBits) => OperatingPoints.FirstOrDefault(value => value.PrecisionBits == precisionBits);

    public Phase9ConverterOperatingPoint Interpolate(int precisionBits)
    {
        var exact = Find(precisionBits);
        if (exact is not null) return exact;
        if (!InterpolationAllowed || string.IsNullOrWhiteSpace(SweepId)) throw new InvalidOperationException("Only an explicitly normalized same-source sweep may interpolate converter points.");
        var lower = OperatingPoints.LastOrDefault(value => value.PrecisionBits < precisionBits);
        var upper = OperatingPoints.FirstOrDefault(value => value.PrecisionBits > precisionBits);
        if (lower is null || upper is null || !string.Equals(lower.ProfileHash, upper.ProfileHash, StringComparison.Ordinal) ||
            !lower.SourceRecordIds.Intersect(upper.SourceRecordIds, StringComparer.Ordinal).Any())
            throw new InvalidOperationException("Interpolation requires bracketing points from the same profile and source sweep.");
        var ratio = (precisionBits - lower.PrecisionBits) / (double)(upper.PrecisionBits - lower.PrecisionBits);
        return Phase9ConverterOperatingPoint.Create(precisionBits,
            checked((int)Math.Round(Lerp(lower.LatencyCycles, upper.LatencyCycles, ratio), MidpointRounding.ToEven)),
            Lerp(lower.EnergyPicojoulesPerSample, upper.EnergyPicojoulesPerSample, ratio),
            Lerp(lower.ThroughputSamplesPerCycle, upper.ThroughputSamplesPerCycle, ratio),
            Lerp(lower.MinimumAnalogValue, upper.MinimumAnalogValue, ratio), Lerp(lower.MaximumAnalogValue, upper.MaximumAnalogValue, ratio),
            lower.Rounding, lower.Clipping, lower.ProfileHash, lower.SourceRecordIds.Concat(upper.SourceRecordIds),
            lower.EvidenceType, NormalizedDeviceEvidenceStatus.Interpolated, "same-source-sweep-interpolation-v1",
            $"linear interpolation in {SweepId}", Combine(lower.Uncertainty, upper.Uncertainty));
    }
    private static double Lerp(double left, double right, double ratio) => left + (right - left) * ratio;
    private static string Combine(string left, string right) => string.Equals(left, right, StringComparison.Ordinal) ? left : left + "; " + right;
}

public sealed record Phase9ConverterIssue(string Code, string Path, string Message);

public sealed record Phase9ConverterResult(
    bool IsSuccess,
    Phase9ConverterKind Kind,
    int PrecisionBits,
    double Input,
    int? DigitalCode,
    double? AnalogValue,
    bool Clipped,
    long AcceptCycle,
    long ReadyCycle,
    double EnergyPicojoules,
    string OperatingPointHash,
    IReadOnlyList<Phase9ConverterIssue> Issues,
    string ResultHash);

/// <summary>Stateful converter runtime with discrete declared precision, throughput, latency, quantization, and clipping.</summary>
public sealed class Phase9ConverterRuntime
{
    private long nextAcceptCycle;
    public Phase9ConverterRuntime(Phase9ConverterProfile profile) => Profile = profile ?? throw new ArgumentNullException(nameof(profile));
    public Phase9ConverterProfile Profile { get; }
    public long NextAcceptCycle => nextAcceptCycle;

    public Phase9ConverterResult Convert(double input, int precisionBits, long cycle, bool allowInterpolation = false)
    {
        if (!double.IsFinite(input)) return Failure(input, precisionBits, cycle, "ConverterInputInvalid", "$.input", "Converter input must be finite.");
        var point = Profile.Find(precisionBits);
        if (point is null && allowInterpolation)
        {
            try { point = Profile.Interpolate(precisionBits); }
            catch (InvalidOperationException exception) { return Failure(input, precisionBits, cycle, "ConverterInterpolationForbidden", "$.precision_bits", exception.Message); }
        }
        if (point is null) return Failure(input, precisionBits, cycle, "ConverterPrecisionUnsupported", "$.precision_bits", $"Profile '{Profile.ProfileId}' does not declare {precisionBits}-bit conversion.");
        if (cycle < nextAcceptCycle) return Failure(input, precisionBits, cycle, "ConverterThroughputBusy", "$.cycle", $"Converter cannot accept before cycle {nextAcceptCycle}.");
        var interval = Math.Max(1L, checked((long)Math.Ceiling(1 / point.ThroughputSamplesPerCycle)));
        nextAcceptCycle = checked(cycle + interval);
        bool clipped;
        int? code;
        double? analog;
        var levels = (1L << precisionBits) - 1;
        if (Profile.Kind == Phase9ConverterKind.Adc)
        {
            clipped = input < point.MinimumAnalogValue || input > point.MaximumAnalogValue;
            if (clipped && point.Clipping == Phase9ClippingPolicy.Error) return Failure(input, precisionBits, cycle, "ConverterRangeError", "$.input", "Input lies outside declared analog range.");
            var bounded = Math.Max(point.MinimumAnalogValue, Math.Min(point.MaximumAnalogValue, input));
            var raw = (bounded - point.MinimumAnalogValue) / (point.MaximumAnalogValue - point.MinimumAnalogValue) * levels;
            code = checked((int)Round(raw, point.Rounding));
            analog = point.MinimumAnalogValue + code.Value / (double)levels * (point.MaximumAnalogValue - point.MinimumAnalogValue);
        }
        else
        {
            var inputCode = Round(input, point.Rounding);
            clipped = inputCode < 0 || inputCode > levels;
            if (clipped && point.Clipping == Phase9ClippingPolicy.Error) return Failure(input, precisionBits, cycle, "ConverterRangeError", "$.input", "DAC code lies outside declared digital range.");
            inputCode = Math.Max(0, Math.Min(levels, inputCode));
            code = checked((int)inputCode);
            analog = point.MinimumAnalogValue + code.Value / (double)levels * (point.MaximumAnalogValue - point.MinimumAnalogValue);
        }
        var ready = checked(cycle + point.LatencyCycles);
        var hash = ComponentTemplateJson.StableHash(new { Profile.ProfileHash, point.OperatingPointHash, input, code, analog, clipped, cycle, ready });
        return new(true, Profile.Kind, precisionBits, input, code, analog, clipped, cycle, ready, point.EnergyPicojoulesPerSample, point.OperatingPointHash, [], hash);
    }

    private Phase9ConverterResult Failure(double input, int precision, long cycle, string code, string path, string message)
    {
        var issues = new[] { new Phase9ConverterIssue(code, path, message) };
        return new(false, Profile.Kind, precision, input, null, null, false, cycle, cycle, 0, "", issues,
            ComponentTemplateJson.StableHash(new { Profile.ProfileHash, input, precision, cycle, code, message }));
    }
    private static long Round(double value, Phase9QuantizationRounding mode) => mode switch
    {
        Phase9QuantizationRounding.TowardZero => checked((long)Math.Truncate(value)),
        Phase9QuantizationRounding.AwayFromZero => checked((long)Math.Round(value, MidpointRounding.AwayFromZero)),
        _ => checked((long)Math.Round(value, MidpointRounding.ToEven))
    };
}
