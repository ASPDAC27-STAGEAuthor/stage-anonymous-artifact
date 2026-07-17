using System.Collections.ObjectModel;
using System.Globalization;


#pragma warning disable CS1591 // Phase 8 public quantity and contract names carry their unit and schema semantics.
namespace HardwareSim.Core;

/// <summary>Stable first-party Phase 8 optical component type identifiers.</summary>
public static class Phase8OpticalTypeIds
{
    public const string Link = "com.hardware-sim.first-party.optical.link";
    public const string Laser = "com.hardware-sim.first-party.optical.laser";
    public const string MrrRouter = "com.hardware-sim.first-party.optical.mrr-router";
    public const string MziSwitch = "com.hardware-sim.first-party.optical.mzi-switch";
    public const string Splitter = "com.hardware-sim.first-party.optical.splitter";
    public const string Combiner = "com.hardware-sim.first-party.optical.combiner";
    public const string Photodetector = "com.hardware-sim.first-party.optical.photodetector";
    public const string Modulator = "com.hardware-sim.first-party.optical.modulator";
    public const string WdmMux = "com.hardware-sim.first-party.optical.wdm-mux";
    public const string WdmDemux = "com.hardware-sim.first-party.optical.wdm-demux";
    public const string EoConverter = "com.hardware-sim.first-party.optical.eo-converter";
    public const string OeConverter = "com.hardware-sim.first-party.optical.oe-converter";

    public static IReadOnlyList<string> All { get; } =
    [
        Link, Laser, MrrRouter, MziSwitch, Splitter, Combiner,
        Photodetector, Modulator, WdmMux, WdmDemux, EoConverter, OeConverter
    ];
}

/// <summary>Canonical Phase 8 optical component parameter and compiled-resource keys.</summary>
public static class Phase8OpticalParameterKeys
{
    public const string WavelengthNanometers = "wavelength_nm";
    public const string ChannelId = "channel_id";
    public const string OpticalPowerDbm = "optical_power_dbm";
    public const string WaveguideMaterial = "waveguide_material";
    public const string PropagationLossDbPerMillimeter = "propagation_loss_db_per_mm";
    public const string BendLossDb = "bend_loss_db";
    public const string CrossingLossDb = "crossing_loss_db";
    public const string CouplerLossDb = "coupler_loss_db";
    public const string InsertionLossDb = "insertion_loss_db";
    public const string BranchLossDb = "branch_loss_db";
    public const string CrosstalkDb = "crosstalk_db";
    public const string DynamicEnergyPicojoulesPerBit = "dynamic_energy_pj_per_bit";
    public const string ConversionEnergyPicojoulesPerBit = "conversion_energy_pj_per_bit";
    public const string ElectricalPowerMilliwatts = "electrical_power_mw";
    public const string LatencyCycles = "latency_cycles";
    public const string StartupLatencyCycles = "startup_latency_cycles";
    public const string ReceiverSensitivityDbm = "receiver_sensitivity_dbm";
    public const string ResponsivityAmperesPerWatt = "responsivity_a_per_w";
    public const string SignalToNoiseThresholdDb = "snr_threshold_db";
    public const string BerModel = "ber_model";
    public const string SwitchState = "switch_state";
    public const string TuningVoltageVolts = "tuning_voltage_v";
    public const string TuningPowerMilliwatts = "tuning_power_mw";
    public const string TuningLatencyCycles = "tuning_latency_cycles";
    public const string TemperatureCelsius = "temperature_c";
    public const string ReferenceTemperatureCelsius = "reference_temperature_c";
    public const string ReferenceVoltageVolts = "reference_voltage_v";
    public const string NominalResonanceNanometers = "nominal_resonance_nm";
    public const string ResonanceOffsetNanometers = "resonance_offset_nm";
    public const string PassbandNanometers = "passband_nm";
    public const string ThermalDriftNanometersPerCelsius = "thermal_drift_nm_per_c";
    public const string VoltageDriftNanometersPerVolt = "voltage_drift_nm_per_v";
    public const string ExtinctionRatioDb = "extinction_ratio_db";
    public const string BiasVoltageVolts = "bias_voltage_v";
    public const string SplitRatio = "split_ratio";
    public const string QueueDepth = "queue_depth";
    public const string AllocationMode = "allocation_mode";
    public const string ChannelTableNanometers = "channel_table_nm";
    public const string ChannelToleranceNanometers = "channel_tolerance_nm";
    public const string ChannelCount = "channel_count";
    public const string UnmatchedPolicy = "unmatched_policy";
    public const string OutputPrecision = "output_precision";
    public const string ModelVersion = "phase8_optical_model_version";
    public const string TypeId = "phase8_optical_type_id";
    public const string ProvenancePrefix = "phase8_provenance.";
    public const string LegacyDisplayAlias = "legacy_display_alias";
    public const string LegacyCompatibility = "phase8_legacy_compatibility";
    public const string ClockPeriodPicoseconds = "clock_period_ps";
}

/// <summary>Optical model provenance source names frozen by the Phase 8 contract.</summary>
public static class OpticalProvenanceSources
{
    public const string Phase8ContractDefault = "Phase8ContractDefault";
    public const string RoutePathDerived = "RoutePathDerived";
    public const string TopologyDerived = "TopologyDerived";
    public const string CharacterizedProfileSnapshot = "CharacterizedProfileSnapshot";
    public const string PluginInstanceOverride = "PluginInstanceOverride";
    public const string SyntheticFunctionalDefault = "SyntheticFunctionalDefault";
}

/// <summary>Identifies the source and limitation of a typed optical quantity.</summary>
public sealed record OpticalQuantityProvenance(string Source, string Detail, bool IsSynthetic = false);

/// <summary>A finite logarithmic ratio in decibels.</summary>
public readonly record struct Decibels
{
    public Decibels(double value)
    {
        Value = OpticalNumeric.RequireFinite(value, nameof(value));
    }

    public double Value { get; }

    public static Decibels operator +(Decibels left, Decibels right) => new(left.Value + right.Value);
    public static Decibels operator -(Decibels left, Decibels right) => new(left.Value - right.Value);
    public override string ToString() => Value.ToString("R", CultureInfo.InvariantCulture) + " dB";
}

/// <summary>A finite absolute power level in dBm.</summary>
public readonly record struct Dbm
{
    public Dbm(double value)
    {
        Value = OpticalNumeric.RequireFinite(value, nameof(value));
    }

    public double Value { get; }
    public Milliwatts ToMilliwatts() => new(UnitSystem.DbmToMw(Value));
    public static Dbm FromMilliwatts(Milliwatts power) => new(UnitSystem.MwToDbm(power.Value));
    public static Dbm operator -(Dbm power, Decibels loss) => new(power.Value - loss.Value);
    public override string ToString() => Value.ToString("R", CultureInfo.InvariantCulture) + " dBm";
}

/// <summary>A positive wavelength in nanometers.</summary>
public readonly record struct Nanometers
{
    public Nanometers(double value)
    {
        Value = OpticalNumeric.RequirePositive(value, nameof(value));
    }

    public double Value { get; }
    public override string ToString() => Value.ToString("R", CultureInfo.InvariantCulture) + " nm";
}

/// <summary>A finite, non-negative power in milliwatts.</summary>
public readonly record struct Milliwatts
{
    public Milliwatts(double value)
    {
        Value = OpticalNumeric.RequireNonNegative(value, nameof(value));
    }

    public double Value { get; }
    public Dbm ToDbm() => Dbm.FromMilliwatts(this);
    public override string ToString() => Value.ToString("R", CultureInfo.InvariantCulture) + " mW";
}

/// <summary>Typed optical state carried alongside a packet through Phase 8 runtime kernels.</summary>
public sealed record OpticalPacketState
{
    private string _channelId = "ch0";

    public Nanometers Wavelength { get; init; } = new(1550.0);
    public string ChannelId
    {
        get => _channelId;
        init
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("Optical channel id is required.", nameof(value));
            }

            _channelId = value.Trim();
        }
    }
    public Dbm OpticalPower { get; init; } = new(0.0);
    public Decibels AccumulatedLoss { get; init; } = new(0.0);
    public Decibels AccumulatedCrosstalk { get; init; } = new(0.0);
    public Decibels? SignalToNoiseRatio { get; init; }

    public OpticalPacketState ApplyLoss(Decibels loss)
    {
        if (loss.Value < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(loss), "Optical path loss must be non-negative.");
        }

        return this with
        {
            OpticalPower = OpticalPower - loss,
            AccumulatedLoss = AccumulatedLoss + loss
        };
    }
}

/// <summary>Waveguide material choices covered by the Phase 8 default loss table.</summary>
public enum OpticalWaveguideMaterial
{
    SiliconNitride,
    Silicon
}

/// <summary>Frozen exact loss defaults required by Phase 8.</summary>
public static class OpticalLossDefaults
{
    public const double SiliconNitrideDbPerMillimeter = 0.1;
    public const double SiliconDbPerMillimeter = 1.0;
    public const double NinetyDegreeBendDb = 0.01;
    public const double CrossingDb = 0.1;
    public const double CouplerDb = 0.5;
    public const double OneToTwoSplitterDb = 3.0;
    public const double MrrInsertionDb = 1.0;
    public const double MziInsertionDb = 1.0;

    public static double PropagationDbPerMillimeter(OpticalWaveguideMaterial material) => material switch
    {
        OpticalWaveguideMaterial.SiliconNitride => SiliconNitrideDbPerMillimeter,
        OpticalWaveguideMaterial.Silicon => SiliconDbPerMillimeter,
        _ => throw new ArgumentOutOfRangeException(nameof(material), material, null)
    };
}

/// <summary>One explainable additive contribution to an optical path loss budget.</summary>
public sealed record OpticalLossContribution(
    string Name,
    Decibels Loss,
    OpticalQuantityProvenance Provenance,
    string SourceId = "");

/// <summary>An immutable optical loss budget with an exact additive total.</summary>
public sealed class OpticalLossBudget
{
    public OpticalLossBudget(IEnumerable<OpticalLossContribution> contributions)
    {
        var snapshot = (contributions ?? throw new ArgumentNullException(nameof(contributions))).ToList();
        if (snapshot.Any(item => string.IsNullOrWhiteSpace(item.Name) || item.Loss.Value < 0))
        {
            throw new ArgumentException("Every optical loss contribution requires a name and non-negative finite loss.", nameof(contributions));
        }

        Contributions = new ReadOnlyCollection<OpticalLossContribution>(snapshot);
        TotalLoss = new Decibels(snapshot.Sum(item => item.Loss.Value));
    }

    public IReadOnlyList<OpticalLossContribution> Contributions { get; }
    public Decibels TotalLoss { get; }
}

/// <summary>Builds deterministic loss budgets from explicit route facts and device contributions.</summary>
public static class OpticalLossModel
{
    public static OpticalLossBudget Calculate(
        double lengthMillimeters,
        OpticalWaveguideMaterial material,
        int bendCount,
        int crossingCount,
        int couplerCount,
        IEnumerable<OpticalLossContribution>? deviceContributions = null)
    {
        OpticalNumeric.RequireNonNegative(lengthMillimeters, nameof(lengthMillimeters));
        OpticalNumeric.RequireNonNegative(bendCount, nameof(bendCount));
        OpticalNumeric.RequireNonNegative(crossingCount, nameof(crossingCount));
        OpticalNumeric.RequireNonNegative(couplerCount, nameof(couplerCount));

        var routePath = new OpticalQuantityProvenance(OpticalProvenanceSources.RoutePathDerived, "Explicit Phase 6B RoutePath geometry.");
        var topology = new OpticalQuantityProvenance(OpticalProvenanceSources.TopologyDerived, "Derived from explicit optical topology.");
        var contributions = new List<OpticalLossContribution>
        {
            new("waveguide", new Decibels(lengthMillimeters * OpticalLossDefaults.PropagationDbPerMillimeter(material)), routePath),
            new("90_degree_bends", new Decibels(bendCount * OpticalLossDefaults.NinetyDegreeBendDb), routePath),
            new("crossings", new Decibels(crossingCount * OpticalLossDefaults.CrossingDb), topology),
            new("couplers", new Decibels(couplerCount * OpticalLossDefaults.CouplerDb), topology)
        };

        if (deviceContributions is not null)
        {
            contributions.AddRange(deviceContributions);
        }

        return new OpticalLossBudget(contributions);
    }

    public static OpticalLossContribution DeviceInsertion(
        string name,
        int count,
        double lossDbPerDevice,
        string sourceId,
        OpticalQuantityProvenance provenance)
    {
        OpticalNumeric.RequireNonNegative(count, nameof(count));
        OpticalNumeric.RequireNonNegative(lossDbPerDevice, nameof(lossDbPerDevice));
        return new OpticalLossContribution(name, new Decibels(count * lossDbPerDevice), provenance, sourceId);
    }
}

/// <summary>Machine-readable warning emitted by Phase 8 optical models.</summary>
public sealed record OpticalModelWarning(
    string Code,
    ValidationSeverity Severity,
    string Message,
    IReadOnlyDictionary<string, string> Data,
    OpticalQuantityProvenance Provenance);

/// <summary>Stable Phase 8 optical warning identifiers.</summary>
public static class OpticalWarningCodes
{
    public const string ReceiverMarginBelowSensitivity = "P8ReceiverMarginBelowSensitivity";
    public const string BerNotModeled = "P8BerNotModeled";
}

/// <summary>Typed result of a laser-to-receiver optical power budget.</summary>
public sealed class OpticalPowerBudget
{
    public Dbm LaserOutput { get; init; }
    public Milliwatts LaserOutputMilliwatts { get; init; }
    public Decibels TotalLoss { get; init; }
    public Dbm ReceivedPower { get; init; }
    public Milliwatts ReceivedPowerMilliwatts { get; init; }
    public Dbm ReceiverSensitivity { get; init; }
    public Decibels Margin { get; init; }
    public IReadOnlyList<OpticalModelWarning> Warnings { get; init; } = [];
}

/// <summary>Evaluates exact Phase 8 optical receiver power and sensitivity margins.</summary>
public static class OpticalPowerBudgetModel
{
    public static OpticalPowerBudget Evaluate(
        Dbm laserOutput,
        Decibels totalLoss,
        Dbm receiverSensitivity,
        OpticalQuantityProvenance? provenance = null)
    {
        if (totalLoss.Value < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(totalLoss), "Total optical path loss must be non-negative.");
        }

        var receivedPower = laserOutput - totalLoss;
        var margin = new Decibels(receivedPower.Value - receiverSensitivity.Value);
        var effectiveProvenance = provenance ?? new OpticalQuantityProvenance(
            OpticalProvenanceSources.Phase8ContractDefault,
            "received_power_dBm = laser_output_dBm - total_loss_dB");
        var warnings = new List<OpticalModelWarning>();
        if (margin.Value < 0)
        {
            warnings.Add(new OpticalModelWarning(
                OpticalWarningCodes.ReceiverMarginBelowSensitivity,
                ValidationSeverity.Warning,
                $"Receiver power margin is {margin.Value.ToString("R", CultureInfo.InvariantCulture)} dB; received power is below sensitivity.",
                new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["laser_output_dbm"] = laserOutput.Value.ToString("R", CultureInfo.InvariantCulture),
                    ["total_loss_db"] = totalLoss.Value.ToString("R", CultureInfo.InvariantCulture),
                    ["received_power_dbm"] = receivedPower.Value.ToString("R", CultureInfo.InvariantCulture),
                    ["receiver_sensitivity_dbm"] = receiverSensitivity.Value.ToString("R", CultureInfo.InvariantCulture),
                    ["margin_db"] = margin.Value.ToString("R", CultureInfo.InvariantCulture)
                }),
                effectiveProvenance));
        }

        return new OpticalPowerBudget
        {
            LaserOutput = laserOutput,
            LaserOutputMilliwatts = new Milliwatts(UnitSystem.DbmToMw(laserOutput.Value)),
            TotalLoss = totalLoss,
            ReceivedPower = receivedPower,
            ReceivedPowerMilliwatts = new Milliwatts(UnitSystem.DbmToMw(receivedPower.Value)),
            ReceiverSensitivity = receiverSensitivity,
            Margin = margin,
            Warnings = new ReadOnlyCollection<OpticalModelWarning>(warnings)
        };
    }
}

/// <summary>Inputs to the deterministic Phase 8 MRR/MZI tuning model.</summary>
public sealed record OpticalTuningInput(
    Nanometers NominalResonance,
    double ResonanceOffsetNanometers,
    double TemperatureCelsius,
    double ReferenceTemperatureCelsius,
    double ThermalDriftNanometersPerCelsius,
    double VoltageVolts,
    double ReferenceVoltageVolts,
    double VoltageDriftNanometersPerVolt,
    Milliwatts TuningPower,
    int LatencyCycles);

/// <summary>Explainable tuning state sampled and committed by a Phase 8 optical runtime.</summary>
public sealed record OpticalTuningState(
    Nanometers NominalResonance,
    double ResonanceDriftNanometers,
    Nanometers EffectiveResonance,
    double VoltageVolts,
    Milliwatts TuningPower,
    int LatencyCycles,
    double TemperatureCelsius,
    OpticalQuantityProvenance Provenance);

/// <summary>Evaluates deterministic voltage/temperature resonance drift and static tuning energy.</summary>
public static class OpticalTuningModel
{
    public static OpticalTuningState Evaluate(
        OpticalTuningInput input,
        OpticalQuantityProvenance? provenance = null)
    {
        if (input is null)
        {
            throw new ArgumentNullException(nameof(input));
        }
        OpticalNumeric.RequireFinite(input.ResonanceOffsetNanometers, nameof(input.ResonanceOffsetNanometers));
        OpticalNumeric.RequireFinite(input.TemperatureCelsius, nameof(input.TemperatureCelsius));
        OpticalNumeric.RequireFinite(input.ReferenceTemperatureCelsius, nameof(input.ReferenceTemperatureCelsius));
        OpticalNumeric.RequireFinite(input.ThermalDriftNanometersPerCelsius, nameof(input.ThermalDriftNanometersPerCelsius));
        OpticalNumeric.RequireFinite(input.VoltageVolts, nameof(input.VoltageVolts));
        OpticalNumeric.RequireFinite(input.ReferenceVoltageVolts, nameof(input.ReferenceVoltageVolts));
        OpticalNumeric.RequireFinite(input.VoltageDriftNanometersPerVolt, nameof(input.VoltageDriftNanometersPerVolt));
        OpticalNumeric.RequireNonNegative(input.LatencyCycles, nameof(input.LatencyCycles));

        var drift = input.ResonanceOffsetNanometers
            + input.ThermalDriftNanometersPerCelsius * (input.TemperatureCelsius - input.ReferenceTemperatureCelsius)
            + input.VoltageDriftNanometersPerVolt * (input.VoltageVolts - input.ReferenceVoltageVolts);
        var effective = new Nanometers(input.NominalResonance.Value + drift);
        return new OpticalTuningState(
            input.NominalResonance,
            drift,
            effective,
            input.VoltageVolts,
            input.TuningPower,
            input.LatencyCycles,
            input.TemperatureCelsius,
            provenance ?? new OpticalQuantityProvenance(
                OpticalProvenanceSources.SyntheticFunctionalDefault,
                "Functional tuning drift only; not silicon characterization.",
                IsSynthetic: true));
    }

    public static double CalculateTuningEnergyPicojoules(Milliwatts tuningPower, long activeCycles, double clockPeriodPicoseconds)
    {
        OpticalNumeric.RequireNonNegative(activeCycles, nameof(activeCycles));
        OpticalNumeric.RequirePositive(clockPeriodPicoseconds, nameof(clockPeriodPicoseconds));
        return tuningPower.Value * activeCycles * clockPeriodPicoseconds / 1_000.0;
    }
}

/// <summary>Creates stable boundary warnings for unsupported BER prediction.</summary>
public static class OpticalBoundaryWarnings
{
    public static OpticalModelWarning BerNotModeled(string sourceId) => new(
        OpticalWarningCodes.BerNotModeled,
        ValidationSeverity.Warning,
        "BER not modeled",
        new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["source_id"] = sourceId ?? "",
            ["ber_value_available"] = "false"
        }),
        new OpticalQuantityProvenance(
            OpticalProvenanceSources.SyntheticFunctionalDefault,
            "Phase 8 checks receiver sensitivity and optional SNR thresholds but does not predict BER.",
            IsSynthetic: true));
}

internal static class OpticalNumeric
{
    public static double RequireFinite(double value, string parameterName)
    {
        if (!double.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(parameterName, "Optical quantity must be finite.");
        }

        return value;
    }

    public static double RequirePositive(double value, string parameterName)
    {
        RequireFinite(value, parameterName);
        if (value <= 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, "Optical quantity must be greater than zero.");
        }

        return value;
    }

    public static double RequireNonNegative(double value, string parameterName)
    {
        RequireFinite(value, parameterName);
        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, "Optical quantity must be non-negative.");
        }

        return value;
    }

    public static int RequireNonNegative(int value, string parameterName)
    {
        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, "Optical count must be non-negative.");
        }

        return value;
    }

    public static long RequireNonNegative(long value, string parameterName)
    {
        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, "Optical count must be non-negative.");
        }

        return value;
    }
}
