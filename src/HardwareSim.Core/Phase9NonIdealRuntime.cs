using System.Collections.ObjectModel;

#pragma warning disable CS1591

namespace HardwareSim.Core;

public enum Phase9NonIdealTier { Functional, CharacterizationDriven, Estimated }

[Flags]
public enum Phase9NonIdealEffect
{
    None = 0,
    ConductanceQuantization = 1 << 0,
    DeviceToDeviceVariation = 1 << 1,
    CycleToCycleVariation = 1 << 2,
    ReadNoise = 1 << 3,
    WriteNoise = 1 << 4,
    StuckAtFault = 1 << 5,
    IrDrop = 1 << 6,
    AdcQuantization = 1 << 7,
    DacQuantization = 1 << 8,
    Saturation = 1 << 9,
    Drift = 1 << 10
}

/// <summary>One profile-supported or explicitly estimated non-ideal effect.</summary>
public sealed record Phase9NonIdealEffectModel(
    Phase9NonIdealEffect Effect,
    double ParameterA,
    double ParameterB,
    NormalizedDeviceEvidenceStatus EvidenceStatus,
    string ProfileHash,
    IReadOnlyList<string> SourceRecordIds,
    string ModelVersion,
    string Formula,
    string ValidRange,
    string Uncertainty,
    string ModelHash)
{
    public static Phase9NonIdealEffectModel Create(Phase9NonIdealEffect effect, double parameterA, double parameterB,
        NormalizedDeviceEvidenceStatus evidenceStatus, string profileHash, IEnumerable<string>? sourceRecordIds,
        string modelVersion, string formula, string validRange, string uncertainty)
    {
        if (effect is Phase9NonIdealEffect.None || !IsSingle(effect)) throw new ArgumentException("Effect model must identify one effect.", nameof(effect));
        if (!double.IsFinite(parameterA) || !double.IsFinite(parameterB)) throw new ArgumentOutOfRangeException(nameof(parameterA));
        var records = (sourceRecordIds ?? []).Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal).ToArray();
        var hash = ComponentTemplateJson.StableHash(new { effect, parameterA, parameterB, evidenceStatus, profileHash, records, modelVersion, formula, validRange, uncertainty });
        return new(effect, parameterA, parameterB, evidenceStatus, profileHash ?? "", records, modelVersion ?? "", formula ?? "", validRange ?? "", uncertainty ?? "", hash);
    }
    private static bool IsSingle(Phase9NonIdealEffect effect) => ((int)effect & ((int)effect - 1)) == 0;
}

public sealed record Phase9NonIdealIssue(string Code, string Path, string Message);

public sealed class Phase9NonIdealResult
{
    internal Phase9NonIdealResult(bool success, IEnumerable<double> values, Phase9NonIdealTier tier, long seed,
        Phase9NonIdealEffect effects, bool estimatedResearchResult, IEnumerable<Phase9NonIdealIssue>? issues, string resultHash)
    {
        IsSuccess = success;
        Values = new ReadOnlyCollection<double>((values ?? []).ToArray());
        Tier = tier;
        Seed = seed;
        Effects = effects;
        EstimatedResearchResult = estimatedResearchResult;
        Issues = new ReadOnlyCollection<Phase9NonIdealIssue>((issues ?? []).ToArray());
        ResultHash = resultHash;
    }
    public bool IsSuccess { get; }
    public IReadOnlyList<double> Values { get; }
    public Phase9NonIdealTier Tier { get; }
    public long Seed { get; }
    public Phase9NonIdealEffect Effects { get; }
    public bool EstimatedResearchResult { get; }
    public IReadOnlyList<Phase9NonIdealIssue> Issues { get; }
    public string ResultHash { get; }
}

/// <summary>Deterministic composable non-ideal runtime with explicit F/C/E evidence tiers.</summary>
public sealed class Phase9NonIdealRuntime
{
    private readonly IReadOnlyDictionary<Phase9NonIdealEffect, Phase9NonIdealEffectModel> models;
    public Phase9NonIdealRuntime(Phase9NonIdealTier tier, long seed, IEnumerable<Phase9NonIdealEffectModel>? effectModels = null)
    {
        Tier = tier;
        Seed = seed;
        models = (effectModels ?? []).ToDictionary(model => model.Effect);
        EffectSet = tier == Phase9NonIdealTier.Functional ? Phase9NonIdealEffect.None : models.Keys.Aggregate(Phase9NonIdealEffect.None, (current, effect) => current | effect);
        RuntimeHash = ComponentTemplateJson.StableHash(new { Tier, Seed, Models = models.Values.OrderBy(value => value.Effect).Select(value => value.ModelHash) });
    }
    public Phase9NonIdealTier Tier { get; }
    public long Seed { get; }
    public Phase9NonIdealEffect EffectSet { get; }
    public string RuntimeHash { get; }

    public Phase9NonIdealResult Apply(IReadOnlyList<double> idealValues, int activeDevices, long calibrationVersion = 0)
    {
        if (idealValues is null || idealValues.Count == 0 || idealValues.Any(value => !double.IsFinite(value))) return Failure("NonIdealInputInvalid", "$.ideal_values", "Ideal values must be a non-empty finite vector.");
        if (activeDevices <= 0) return Failure("NonIdealActiveDeviceCountInvalid", "$.active_devices", "Active device count must be positive.");
        if (Tier == Phase9NonIdealTier.Functional)
        {
            var exact = idealValues.ToArray();
            return new(true, exact, Tier, Seed, Phase9NonIdealEffect.None, false, [], Hash(exact, Phase9NonIdealEffect.None));
        }
        foreach (var model in models.Values)
        {
            if (Tier == Phase9NonIdealTier.CharacterizationDriven && model.EvidenceStatus is NormalizedDeviceEvidenceStatus.Estimated or NormalizedDeviceEvidenceStatus.Unknown or NormalizedDeviceEvidenceStatus.UserOverride)
                return Failure("NonIdealCharacterizationEvidenceInvalid", $"$.effects[{model.Effect}]", "Characterization-driven mode only enables reported, derived, or approved interpolated evidence.");
            if (Tier == Phase9NonIdealTier.Estimated && model.EvidenceStatus == NormalizedDeviceEvidenceStatus.Unknown)
                return Failure("NonIdealEstimatedModelMissing", $"$.effects[{model.Effect}]", "Estimated mode requires an explicit prior/model and uncertainty instead of unknown values.");
            if (string.IsNullOrWhiteSpace(model.ModelVersion) || string.IsNullOrWhiteSpace(model.Formula) || string.IsNullOrWhiteSpace(model.Uncertainty))
                return Failure("NonIdealProvenanceIncomplete", $"$.effects[{model.Effect}]", "Every effect requires model version, formula, and uncertainty.");
        }
        var values = idealValues.ToArray();
        var rng = new DeterministicNormal(Seed);
        foreach (var model in models.Values.OrderBy(value => value.Effect)) ApplyEffect(values, model, rng, activeDevices, calibrationVersion);
        return new(true, values, Tier, Seed, EffectSet, Tier == Phase9NonIdealTier.Estimated, [], Hash(values, EffectSet));
    }

    private static void ApplyEffect(double[] values, Phase9NonIdealEffectModel model, DeterministicNormal rng, int activeDevices, long calibrationVersion)
    {
        switch (model.Effect)
        {
            case Phase9NonIdealEffect.ConductanceQuantization:
            case Phase9NonIdealEffect.AdcQuantization:
            case Phase9NonIdealEffect.DacQuantization:
                if (model.ParameterA <= 0) throw new InvalidOperationException($"{model.Effect} quantization step must be positive.");
                for (var index = 0; index < values.Length; index++) values[index] = Math.Round(values[index] / model.ParameterA, MidpointRounding.ToEven) * model.ParameterA;
                break;
            case Phase9NonIdealEffect.DeviceToDeviceVariation:
                for (var index = 0; index < values.Length; index++) values[index] *= 1 + rng.NextGaussian() * model.ParameterA / Math.Sqrt(Math.Max(1, activeDevices));
                break;
            case Phase9NonIdealEffect.CycleToCycleVariation:
            case Phase9NonIdealEffect.ReadNoise:
            case Phase9NonIdealEffect.WriteNoise:
                for (var index = 0; index < values.Length; index++) values[index] += rng.NextGaussian() * model.ParameterA;
                break;
            case Phase9NonIdealEffect.StuckAtFault:
                for (var index = 0; index < values.Length; index++) if (rng.NextUnit() < model.ParameterA) values[index] = model.ParameterB;
                break;
            case Phase9NonIdealEffect.IrDrop:
                for (var index = 0; index < values.Length; index++) values[index] *= Math.Max(0, 1 - model.ParameterA * Math.Sqrt(activeDevices));
                break;
            case Phase9NonIdealEffect.Saturation:
                for (var index = 0; index < values.Length; index++) values[index] = Math.Max(model.ParameterA, Math.Min(model.ParameterB, values[index]));
                break;
            case Phase9NonIdealEffect.Drift:
                for (var index = 0; index < values.Length; index++) values[index] *= Math.Exp(-model.ParameterA * Math.Max(0, calibrationVersion));
                break;
        }
    }

    private Phase9NonIdealResult Failure(string code, string path, string message) => new(false, [], Tier, Seed, EffectSet, Tier == Phase9NonIdealTier.Estimated,
        [new(code, path, message)], ComponentTemplateJson.StableHash(new { RuntimeHash, code, path, message }));
    private string Hash(IReadOnlyList<double> values, Phase9NonIdealEffect effects) => ComponentTemplateJson.StableHash(new { RuntimeHash, values, Tier, Seed, effects });

    private sealed class DeterministicNormal
    {
        private ulong state;
        private bool hasSpare;
        private double spare;
        public DeterministicNormal(long seed) => state = unchecked((ulong)seed) ^ 0x9E3779B97F4A7C15UL;
        public double NextUnit()
        {
            state ^= state >> 12;
            state ^= state << 25;
            state ^= state >> 27;
            var value = state * 2685821657736338717UL;
            return (value >> 11) * (1.0 / 9007199254740992.0);
        }
        public double NextGaussian()
        {
            if (hasSpare) { hasSpare = false; return spare; }
            var u1 = Math.Max(NextUnit(), 1e-16);
            var u2 = NextUnit();
            var radius = Math.Sqrt(-2 * Math.Log(u1));
            var angle = 2 * Math.PI * u2;
            spare = radius * Math.Sin(angle);
            hasSpare = true;
            return radius * Math.Cos(angle);
        }
    }
}

public sealed record Phase9NonIdealStatistics(int SampleCount, double Mean, double StandardDeviation, double ConfidenceInterval95Low, double ConfidenceInterval95High, string EffectSet, string StatisticsHash);

public static class Phase9NonIdealStatisticsRunner
{
    public static Phase9NonIdealStatistics Run(IEnumerable<long> seeds, IReadOnlyList<double> idealValues, int activeDevices, Func<long, Phase9NonIdealRuntime> factory)
    {
        var outputs = seeds.Select(seed =>
        {
            var result = factory(seed).Apply(idealValues, activeDevices);
            if (!result.IsSuccess) throw new InvalidOperationException(result.Issues[0].Message);
            return result.Values.Average();
        }).ToArray();
        if (outputs.Length < 2) throw new ArgumentException("Statistics require at least two seeds.", nameof(seeds));
        var mean = outputs.Average();
        var variance = outputs.Sum(value => (value - mean) * (value - mean)) / (outputs.Length - 1);
        var standardDeviation = Math.Sqrt(variance);
        var halfWidth = 1.96 * standardDeviation / Math.Sqrt(outputs.Length);
        var effectSet = factory(0).EffectSet.ToString();
        var hash = ComponentTemplateJson.StableHash(new { outputs, mean, standardDeviation, halfWidth, effectSet });
        return new(outputs.Length, mean, standardDeviation, mean - halfWidth, mean + halfWidth, effectSet, hash);
    }
}
