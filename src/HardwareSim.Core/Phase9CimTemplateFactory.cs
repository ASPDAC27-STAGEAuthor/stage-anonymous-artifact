#pragma warning disable CS1591

using System.Globalization;
using System.Text.Json;

namespace HardwareSim.Core;

/// <summary>Builds the Phase 9 literature-bound CIM PE without introducing a central CIM component class.</summary>
public static class Phase9CimTemplateFactory
{
    public const string TemplateId = "CIM_PE_ReRAM_32x32_FP8_Literature";
    public const string ArrayProfileParameter = "device_profile_id";
    public const string ArrayProfileHashParameter = "device_profile_hash";
    public const string AdcProfileParameter = "adc_profile_id";
    public const string AdcProfileHashParameter = "adc_profile_hash";
    public const string ExecutionModeParameter = "phase9_execution_mode";
    public const string SeedParameter = "phase9_nonideal_seed";
    public const string EffectSetParameter = "phase9_effect_set";

    public static ComponentTemplate Create(
        NormalizedDeviceProfilePackage package,
        string arrayProfileId = "isscc_reram_96mb_22nm_2026",
        string adcProfileId = "adc_flash_sar_8b_180nm_2026")
    {
        if (package is null) throw new ArgumentNullException(nameof(package));
        var arrayProfile = RequireProfile(package, arrayProfileId);
        var adcProfile = RequireProfile(package, adcProfileId);
        var arrayEnergy = RequireNumber(arrayProfile, "energy_pj_per_mac_assuming_2ops");
        var arrayArea = OptionalNumber(arrayProfile, "array_area_um2");
        var adcEnergy = RequireNumber(adcProfile, "energy_pj_per_sample");
        var adcArea = RequireNumber(adcProfile, "area_um2");

        var template = ComponentTemplateExamples.PeArray32x32Fp8ReramLikeSynthetic();
        template.TemplateId = TemplateId;
        template.DisplayName = "CIM PE ReRAM 32x32 FP8 (Literature Exact Point)";
        template.Version = "1.0.0";
        template.Lifecycle = ComponentTemplateLifecycleState.Published;
        template.CompiledProfile = null;
        template.Provenance = new ComponentTemplateProvenance
        {
            Source = $"normalized-device-profile:{arrayProfile.ProfileId}+{adcProfile.ProfileId}",
            Author = "HardwareSim Phase9",
            ToolVersion = "phase9-cim-template-v1",
            DependencyHashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["normalized_device_package"] = package.PackageHash,
                ["array_device_profile"] = arrayProfile.ProfileHash,
                ["adc_device_profile"] = adcProfile.ProfileHash,
                ["phase9_physical_footprint_authoritative"] = "true",
                ["phase9_literature_exact_point"] = "true"
            },
            Warnings =
            [
                "Functional execution is exact for the declared 32x32 FP8 shell contract.",
                "Literature values are used only at their reported operating points; no cross-source interpolation is performed.",
                "The macro footprint is a derived internal floorplan envelope, not reported ReRAM macro geometry.",
                "Unbound DAC and peripheral terms remain explicitly model-derived and are not literature truth."
            ]
        };

        var executionBinding = template.ExecutionBinding
            ?? throw new InvalidOperationException("CIM template requires an execution binding.");
        executionBinding.KernelId = Phase9CimVmmKernelFactory.KernelId;
        executionBinding.KernelVersionRequirement = "1.x";
        executionBinding.ContractSchemaId = CoreDigitalVmmKernelFactory.SchemaId;
        executionBinding.OperationKind = "digital_vmm";
        executionBinding.ConfigurationBindings["execution_mode"] = "resolved." + ExecutionModeParameter;
        executionBinding.ConfigurationBindings["nonideal_seed"] = "resolved." + SeedParameter;
        executionBinding.ConfigurationBindings["effect_set"] = "resolved." + EffectSetParameter;
        executionBinding.ConfigurationBindings["device_profile_id"] = "resolved." + ArrayProfileParameter;
        executionBinding.ConfigurationBindings["device_profile_hash"] = "resolved." + ArrayProfileHashParameter;
        executionBinding.ConfigurationBindings["adc_profile_id"] = "resolved." + AdcProfileParameter;
        executionBinding.ConfigurationBindings["adc_profile_hash"] = "resolved." + AdcProfileHashParameter;

        template.Parameters.AddRange(
        [
            StringParameter(ArrayProfileParameter, arrayProfile.ProfileId),
            StringParameter(ArrayProfileHashParameter, arrayProfile.ProfileHash),
            StringParameter(AdcProfileParameter, adcProfile.ProfileId),
            StringParameter(AdcProfileHashParameter, adcProfile.ProfileHash),
            EnumParameter(ExecutionModeParameter, "functional", ["functional", "characterization", "estimated"]),
            IntegerParameter(SeedParameter, "9", 0, int.MaxValue),
            StringParameter(EffectSetParameter, "none")
        ]);

        foreach (var port in template.ExternalPorts)
        {
            port.BandwidthBitsPerCycle = port.Name switch
            {
                "in_activation" => 256,
                "in_weight" => 8192,
                "out_result" => 256,
                _ => 128
            };
        }

        var array = RequireBlock(template, "compute_core");
        array.DisplayName = "ReRAM Array";
        array.BlockKind = "ReRAMArray";
        array.TypeId = ComponentTypeIds.BuiltIn(ComponentKind.ReRamCrossbar);
        array.ProfileBindingIds = ["phase9_array_profile"];
        array.EnergyPicojoules = arrayEnergy.Value.GetDouble() * 32 * 32;
        array.AreaUm2 = arrayArea?.Value.GetDouble() ?? 0;
        MarkMetric(array, "energy", arrayEnergy, "reported-point-derived-per-operation");
        MarkMetric(array, "area", arrayArea, arrayArea is null ? "unknown-not-zero" : "reported");

        var adc = RequireBlock(template, "adc_like");
        adc.DisplayName = "ADC";
        adc.BlockKind = "ADC";
        adc.TypeId = ComponentTypeIds.BuiltIn(ComponentKind.Adc);
        adc.ProfileBindingIds = ["phase9_adc_profile"];
        adc.EnergyPicojoules = adcEnergy.Value.GetDouble() * 32;
        adc.AreaUm2 = adcArea.Value.GetDouble();
        MarkMetric(adc, "energy", adcEnergy, "derived-from-reported-power-and-sample-rate");
        MarkMetric(adc, "area", adcArea, "reported-area-estimated-geometry");

        SetPrimitive(template, "dac_like", "DAC", ComponentTypeIds.BuiltIn(ComponentKind.Dac), "estimated-model");
        SetPrimitive(template, "accumulator", "AnalogAccumulator", ComponentTypeIds.BuiltIn(ComponentKind.AnalogAccumulator), "model-derived");
        SetPrimitive(template, "decoder", "RowColumnDecoder", Phase9CimPrimitivePlugins.DecoderTypeId, "model-derived");
        SetPrimitive(template, "controller", "ArrayController", Phase9CimPrimitivePlugins.ControllerTypeId, "model-derived");

        var senseAmplifier = new InternalBlock
        {
            Id = "sense_amp",
            DisplayName = "Sense Amplifier",
            BlockKind = "SenseAmplifier",
            TypeId = ComponentTypeIds.BuiltIn(ComponentKind.SenseAmplifier),
            Layer = InternalBlockLayer.Structural,
            TraceStage = "sense_amplify",
            EnergyPicojoules = 0.35,
            AreaUm2 = 300,
            Ports =
            [
                new InternalPort { Name = "bitline_in", Direction = PortDirection.Input, SignalType = SignalType.Analog, DataType = HardwareDataType.Tensor, Precision = PrecisionKind.FP8_E4M3, Protocol = PortProtocol.Packet, WidthBits = 256, Shape = [1, 32] },
                new InternalPort { Name = "sense_out", Direction = PortDirection.Output, SignalType = SignalType.Analog, DataType = HardwareDataType.Tensor, Precision = PrecisionKind.FP8_E4M3, Protocol = PortProtocol.Packet, WidthBits = 256, Shape = [1, 32] },
                new InternalPort { Name = "enable", Direction = PortDirection.Input, SignalType = SignalType.Control, DataType = HardwareDataType.Config, Precision = PrecisionKind.Any, Protocol = PortProtocol.Packet, WidthBits = 32 }
            ]
        };
        MarkMetric(senseAmplifier, "energy", null, "model-derived");
        MarkMetric(senseAmplifier, "area", null, "model-derived");
        template.InternalBlocks.Add(senseAmplifier);
        var arrayToAdc = template.InternalConnections.Single(connection => connection.Id == "s_array_adc");
        arrayToAdc.TargetBlockId = senseAmplifier.Id;
        arrayToAdc.TargetPortName = "bitline_in";
        template.InternalConnections.Add(new InternalConnection
        {
            Id = "s_sense_adc", SourceBlockId = senseAmplifier.Id, SourcePortName = "sense_out",
            TargetBlockId = "adc_like", TargetPortName = "analog_placeholder", PayloadType = "tensor",
            Precision = PrecisionKind.FP8_E4M3, BandwidthBitsPerCycle = 256
        });
        template.InternalConnections.Add(new InternalConnection
        {
            Id = "s_ctrl_sense", SourceBlockId = "controller", SourcePortName = "issue",
            TargetBlockId = senseAmplifier.Id, TargetPortName = "enable", PayloadType = "config",
            Precision = PrecisionKind.Any, BandwidthBitsPerCycle = 32
        });
        var structuralView = template.Views.Single(view => view.Kind == TemplateViewKind.StructuralPort);
        structuralView.Layout[senseAmplifier.Id] = new GridPosition(11, 3);

        template.ProfileBindings =
        [
            Binding("phase9_array_profile", array.Id, arrayProfile, arrayEnergy),
            Binding("phase9_adc_profile", adc.Id, adcProfile, adcEnergy)
        ];

        var frozen = ComponentTypeRegistry.CreateDefault().FreezeRuntimeKernels();
        if (!frozen.IsSuccess || frozen.Snapshot is null)
            throw new InvalidOperationException(string.Join("; ", frozen.Issues.Select(issue => issue.Message)));
        var compiled = new ComponentTemplateCompiler().Compile(template, kernelRegistry: frozen.Snapshot);
        if (!compiled.IsSuccess || compiled.Profile is null)
            throw new InvalidOperationException(string.Join("; ", compiled.Issues.Select(issue => $"{issue.Code}:{issue.Message}")));
        template.CompiledProfile = compiled.Profile;
        template.Provenance.CompileHash = compiled.Profile.ProfileHash;
        return template;
    }

    private static NormalizedDeviceProfile RequireProfile(NormalizedDeviceProfilePackage package, string id) =>
        package.Profiles.SingleOrDefault(profile => string.Equals(profile.ProfileId, id, StringComparison.Ordinal))
        ?? throw new KeyNotFoundException($"Normalized device profile '{id}' is unavailable.");

    private static NormalizedDeviceField RequireNumber(NormalizedDeviceProfile profile, string fieldName)
    {
        var resolution = profile.ResolveField(fieldName);
        var field = resolution.Fields.OrderBy(value => value.Key, StringComparer.Ordinal).FirstOrDefault()
            ?? throw new InvalidOperationException($"Profile '{profile.ProfileId}' lacks required exact-point field '{fieldName}'.");
        if (!field.Value.TryGetDouble(out var value) || !double.IsFinite(value) || value <= 0)
            throw new InvalidOperationException($"Profile field '{field.Key}' must be finite and positive.");
        return field;
    }

    private static NormalizedDeviceField? OptionalNumber(NormalizedDeviceProfile profile, string fieldName)
    {
        var field = profile.ResolveField(fieldName).Fields.OrderBy(value => value.Key, StringComparer.Ordinal).FirstOrDefault();
        if (field is null) return null;
        if (!field.Value.TryGetDouble(out var value) || !double.IsFinite(value) || value <= 0)
            throw new InvalidOperationException($"Profile field '{field.Key}' must be finite and positive.");
        return field;
    }

    private static InternalBlock RequireBlock(ComponentTemplate template, string id) =>
        template.InternalBlocks.Single(block => string.Equals(block.Id, id, StringComparison.Ordinal));

    private static void SetPrimitive(ComponentTemplate template, string id, string kind, string typeId, string evidence)
    {
        var block = RequireBlock(template, id);
        block.DisplayName = kind;
        block.BlockKind = kind;
        block.TypeId = typeId;
        MarkMetric(block, "energy", null, evidence);
        MarkMetric(block, "area", null, evidence);
    }

    private static void MarkMetric(InternalBlock block, string quantity, NormalizedDeviceField? field, string status)
    {
        block.ExtensionData[$"phase9_{quantity}_evidence"] = JsonSerializer.SerializeToElement(new
        {
            status,
            profile_field = field?.Key ?? "",
            source_record_ids = field?.Provenance.SourceRecordIds ?? [],
            uncertainty = field?.Provenance.Uncertainty ?? "unknown"
        }, HardwareGraphJson.Options);
    }

    private static TemplateProfileBinding Binding(string id, string blockId, NormalizedDeviceProfile profile, NormalizedDeviceField field)
    {
        var binding = new TemplateProfileBinding
        {
            BindingId = id,
            BlockId = blockId,
            ProfileId = profile.ProfileId,
            ModelId = "normalized-device-exact-point-v1",
            Synthetic = false,
            Snapshot = new CharacterizedProfileSnapshot
            {
                Id = profile.ProfileId,
                TargetKind = ModelBindingTargetKind.Component,
                TargetId = blockId,
                ModelId = "normalized-device-exact-point-v1",
                OutputQuantity = field.Name,
                Units = field.CanonicalUnits,
                Value = field.Value.GetDouble(),
                Source = string.Join(",", profile.SourceIds),
                Version = "1.0.0",
                Calibrated = profile.CalibrationState == NormalizedDeviceCalibrationState.Calibrated,
                Hash = profile.ProfileHash
            }
        };
        binding.ExtensionData["phase9_provenance"] = JsonSerializer.SerializeToElement(new
        {
            evidence_status = field.Provenance.Status.ToString(),
            evidence_type = field.Provenance.EvidenceType,
            valid_range = field.ValidRange,
            applicable_range = field.Provenance.ApplicableRange,
            interpolation_policy = field.InterpolationPolicy,
            extrapolation_policy = field.ExtrapolationPolicy,
            uncertainty = field.Provenance.Uncertainty,
            source_record_ids = field.Provenance.SourceRecordIds,
            evidence_locator = field.Provenance.EvidenceLocator,
            notes = field.Provenance.Notes
        }, HardwareGraphJson.Options);
        return binding;
    }
    private static TemplateParameter StringParameter(string name, string value) => new()
    {
        Name = name, ValueKind = TemplateParameterValueKind.String, DefaultValue = value, Required = true
    };

    private static TemplateParameter EnumParameter(string name, string value, List<string> allowed) => new()
    {
        Name = name, ValueKind = TemplateParameterValueKind.Enum, DefaultValue = value, AllowedValues = allowed, Required = true
    };

    private static TemplateParameter IntegerParameter(string name, string value, int minimum, int maximum) => new()
    {
        Name = name, ValueKind = TemplateParameterValueKind.Integer, DefaultValue = value,
        Minimum = minimum, Maximum = maximum, Units = "count", Required = true
    };
}
