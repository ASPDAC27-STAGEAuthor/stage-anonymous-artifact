using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HardwareSim.Core;

internal static class AspdacRunnerScenarios
{
    internal static void RunCodesignBottleneck(JsonElement root, string outputPath)
    {
        var parameters = root.GetProperty("resolved").GetProperty("parameters");
        var result = AspdacCodesignRuntime.Run(ReadCodesignOptions(parameters));
        WriteOutput(outputPath, new
        {
            status = "completed",
            completed_utc = DateTimeOffset.UtcNow.ToString("O"),
            measurement_kind = "stage_codesign_cycle_service_runtime",
            provenance = new
            {
                graph_hash = result.GraphHash,
                workload_hash = result.WorkloadHash,
                mapping_hash = result.MappingHash,
                model_hash = result.ModelHash,
                model_profile_hash = root.GetProperty("config_hash").GetString(),
                runtime = "HardwareSim.Core.AspdacCodesignRuntime",
                paired_axis = RequiredString(parameters, "axis"),
                paired_axis_value = RequiredInt(parameters, "axis_value"),
                invariant_hash_contract = "graph/workload/mapping/model unchanged within every one-axis sweep"
            },
            metrics = result,
            limitations = new[]
            {
                "STAGE-only cycle-service evidence; it is not a cross-tool accuracy claim.",
                "Critical-path attribution sums to total cycles while concurrent non-limiting service demand is reported separately."
            }
        });
    }

    internal static void RunCodesignPrecision(JsonElement root, string outputPath)
    {
        var parameters = root.GetProperty("resolved").GetProperty("parameters");
        var result = AspdacCodesignRuntime.RunPrecision(new AspdacPrecisionOptions(
            RequiredString(parameters, "precision"),
            RequiredInt(parameters, "bits_per_element"),
            RequiredInt(parameters, "conversion_passes"),
            RequiredLong(parameters, "element_count"),
            ReadCodesignOptions(parameters)));
        WriteOutput(outputPath, new
        {
            status = "completed",
            completed_utc = DateTimeOffset.UtcNow.ToString("O"),
            measurement_kind = "stage_precision_packet_cycle_runtime",
            provenance = new
            {
                workload_hash = result.WorkloadHash,
                mapping_hash = result.MappingHash,
                base_model_hash = result.BaseModelHash,
                precision_model_hash = result.PrecisionModelHash,
                model_profile_hash = root.GetProperty("config_hash").GetString(),
                runtime = "HardwareSim.Core.AspdacCodesignRuntime.RunPrecision",
                conversion_contract = "Phase7C precision-converter 1-cycle service, 0.02 pJ/bit"
            },
            metrics = result,
            limitations = new[] { "Precision sweep keeps element count and mapping fixed; it does not change PE arithmetic throughput." }
        });
    }

    internal static void RunCimTemplateComparison(JsonElement root, string outputPath)
    {
        var parameters = root.GetProperty("resolved").GetProperty("parameters");
        var device = RequiredString(parameters, "device");
        var mode = RequiredString(parameters, "mode");
        var seed = RequiredInt(parameters, "seed");
        var repeat = RequiredInt(parameters, "repeat");
        var operationCount = RequiredLong(parameters, "operation_count");
        var catalogPath = Path.GetFullPath(RequiredString(parameters, "catalog_path"));
        var package = Phase9LiteratureDeviceProfileNormalizer.Normalize(Phase7CLiteratureCharacterizationCatalog.Load(catalogPath));
        var digital = string.Equals(device, "digital", StringComparison.Ordinal);
        var template = digital
            ? ComponentTemplateExamples.PeArray32x32Fp8SramSynthetic()
            : Phase9CimTemplateFactory.Create(package);
        IReadOnlyDictionary<string, string>? overrides = digital ? null : new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [Phase9CimTemplateFactory.ExecutionModeParameter] = mode == "functional_exact" ? "functional" : "characterization",
            [Phase9CimTemplateFactory.SeedParameter] = seed.ToString(System.Globalization.CultureInfo.InvariantCulture),
            [Phase9CimTemplateFactory.EffectSetParameter] = mode == "functional_exact" ? "none" : "adc_resolution_equivalent_read_noise"
        };
        var kernel = new ComponentKernelTestRunner().Run(template, ComponentTypeRegistry.CreateDefault(), overrides, seed: seed);
        if (!kernel.IsSuccess || kernel.Profile is null || kernel.Simulation is null)
            throw new InvalidOperationException("Template production-kernel run failed: " + string.Join("; ", kernel.Issues.Select(issue => issue.Code + ":" + issue.Message)));

        const int macsPerInvocation = 1024;
        const int instances = 16;
        var invocations = (operationCount + macsPerInvocation - 1) / macsPerInvocation;
        var waves = (invocations + instances - 1) / instances;
        var invocationLatency = Math.Max(1, kernel.Profile.OperationLatency + kernel.Profile.PipelineLatency);
        var workloadCycles = checked(waves * invocationLatency);
        var schedulerHash = CimScheduleHash(waves, instances, invocationLatency, invocations);

        var structural = template.InternalBlocks.Where(block => block.Layer == InternalBlockLayer.Structural).ToArray();
        double Energy(params string[] ids) => structural.Where(block => ids.Contains(block.Id, StringComparer.Ordinal)).Sum(block => block.EnergyPicojoules);
        var perInvocationArray = Energy("compute_core", "sense_amp");
        var perInvocationAdc = Energy("adc_like");
        var perInvocationDac = Energy("dac_like");
        var perInvocationControl = Energy("decoder", "controller");
        var perInvocationBuffer = Energy("input_buffer", "weight_store", "accumulator");
        var classified = new HashSet<string>(new[] { "compute_core", "sense_amp", "adc_like", "dac_like", "decoder", "controller", "input_buffer", "weight_store", "accumulator", "egress" }, StringComparer.Ordinal);
        var perInvocationOther = structural.Where(block => !classified.Contains(block.Id)).Sum(block => block.EnergyPicojoules);
        var knownPerInvocation = perInvocationArray + perInvocationAdc + perInvocationDac + perInvocationControl + perInvocationBuffer + perInvocationOther;
        if (!kernel.Profile.DerivedMetrics.TryGetValue("output_bits", out var outputBitsRaw) ||
            !long.TryParse(outputBitsRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var outputBitsPerInvocation) ||
            outputBitsPerInvocation <= 0)
            throw new InvalidOperationException("Compiled template lacks a positive output_bits metric for shared egress accounting.");
        var perInvocationEgress = ComponentTemplateEnergyModels.SharedOutputStageEnergyPicojoules(outputBitsPerInvocation);
        var completePerInvocation = knownPerInvocation + perInvocationEgress;

        var ideal = new[] { 0.125, -0.5, 0.75, 1.0, -0.25, 0.375, 0.625, -0.875 };
        Phase9NonIdealResult nonIdeal;
        string nonIdealEvidence;
        if (mode == "functional_exact" || digital)
        {
            nonIdeal = new Phase9NonIdealRuntime(Phase9NonIdealTier.Functional, seed).Apply(ideal, macsPerInvocation);
            nonIdealEvidence = "functional exact; no non-ideal effects";
        }
        else
        {
            var adcProfile = package.Profiles.Single(profile => profile.ProfileId == "adc_flash_sar_8b_180nm_2026");
            var sigma = 1.0 / (255.0 * Math.Sqrt(12.0));
            var model = Phase9NonIdealEffectModel.Create(
                Phase9NonIdealEffect.ReadNoise,
                sigma,
                0,
                NormalizedDeviceEvidenceStatus.Derived,
                adcProfile.ProfileHash,
                adcProfile.SourceIds,
                "phase9-adc-resolution-equivalent-noise-v1",
                "sigma = one 8-bit LSB / sqrt(12); y = x + normal(0,sigma)",
                "reported 8-bit ADC operating point only",
                "quantization-equivalent sigma; not measured ReRAM device noise");
            nonIdeal = new Phase9NonIdealRuntime(Phase9NonIdealTier.CharacterizationDriven, seed, [model]).Apply(ideal, macsPerInvocation);
            nonIdealEvidence = "derived from literature-bound 8-bit ADC resolution; not measured device-noise calibration";
        }
        if (!nonIdeal.IsSuccess) throw new InvalidOperationException(string.Join("; ", nonIdeal.Issues.Select(issue => issue.Message)));
        var rmse = Math.Sqrt(nonIdeal.Values.Zip(ideal, (actual, expected) => (actual - expected) * (actual - expected)).Average());
        var footprint = kernel.Profile.PhysicalFootprint;
        var energy = new
        {
            array_pj = perInvocationArray * invocations,
            adc_pj = perInvocationAdc * invocations,
            dac_pj = perInvocationDac * invocations,
            decoder_controller_pj = perInvocationControl * invocations,
            buffer_accumulator_pj = perInvocationBuffer * invocations,
            egress_pj = perInvocationEgress * invocations,
            other_known_pj = perInvocationOther * invocations,
            known_total_pj = knownPerInvocation * invocations,
            known_energy_per_operation_pj = knownPerInvocation / macsPerInvocation,
            egress_energy_per_operation_pj = perInvocationEgress / macsPerInvocation,
            complete_total_pj = completePerInvocation * invocations,
            complete_energy_per_operation_pj = completePerInvocation / macsPerInvocation,
            unknown_terms = Array.Empty<string>()
        };
        var traceHash = Sha256(string.Join("|", kernel.TraceHash, schedulerHash, nonIdeal.ResultHash, operationCount, device, mode));
        WriteOutput(outputPath, new
        {
            status = "completed",
            completed_utc = DateTimeOffset.UtcNow.ToString("O"),
            measurement_kind = "stage_digital_cim_template_cycle_runtime",
            provenance = new
            {
                template_id = template.TemplateId,
                template_version = template.Version,
                workload_hash = RequiredString(parameters, "workload_hash"),
                mapping_hash = RequiredString(parameters, "mapping_hash"),
                template_profile_hash = kernel.ProfileHash,
                kernel.ExecutionContractHash,
                kernel.RuntimeKernelRegistryHash,
                production_kernel_trace_hash = kernel.TraceHash,
                workload_scheduler_hash = schedulerHash,
                array_profile_hash = digital ? "synthetic-phase7c-digital" : template.ProfileBindings.Single(binding => binding.BindingId == "phase9_array_profile").Snapshot!.Hash,
                adc_profile_hash = digital ? "not-applicable" : template.ProfileBindings.Single(binding => binding.BindingId == "phase9_adc_profile").Snapshot!.Hash,
                digital_profile_boundary = digital ? "Phase7C synthetic digital PE template" : "not-applicable",
                cim_profile_boundary = digital ? "not-applicable" : "literature exact-point profiles plus explicitly model-derived peripherals",
                egress_energy_model_id = ComponentTemplateEnergyModels.SharedOutputStageModelId,
                egress_energy_pj_per_bit = ComponentTemplateEnergyModels.SharedOutputStagePicojoulesPerBit,
                egress_output_bits_per_invocation = outputBitsPerInvocation,
                nonideal_evidence = nonIdealEvidence,
                profile_warnings = template.Provenance.Warnings,
                repeat_excluded_from_identity = true
            },
            metrics = new
            {
                device,
                mode,
                seed,
                repeat,
                operation_count = operationCount,
                macs_per_invocation = macsPerInvocation,
                invocation_count = invocations,
                template_instances = instances,
                invocation_latency_cycles = invocationLatency,
                total_cycles = workloadCycles,
                energy,
                output_rmse = rmse,
                output_error_result_hash = nonIdeal.ResultHash,
                footprint_area_um2 = footprint?.AreaUm2,
                footprint_width_um = footprint?.WidthUm,
                footprint_height_um = footprint?.HeightUm,
                footprint_complete = footprint?.IsKnown == true,
                known_area_subtotal_um2 = kernel.Profile.TotalAreaUm2,
                canonical_trace_sha256 = traceHash
            },
            limitations = new[]
            {
                "STAGE-only template comparison; the Phase 7C digital template is synthetic and the CIM macro is literature-bound only at declared exact points.",
                "Both templates use the same modeled output-stage cost of 0.0005 pJ/bit; complete totals are model comparisons rather than silicon measurements.",
                "Characterization-driven variation uses ADC-resolution-equivalent derived sigma, not measured ReRAM device-noise calibration."
            }
        });
    }

    private static AspdacCodesignOptions ReadCodesignOptions(JsonElement parameters) => new(
        RequiredString(parameters, "workload_id"),
        RequiredLong(parameters, "mac_count"),
        RequiredLong(parameters, "packet_count"),
        RequiredLong(parameters, "memory_requests"),
        RequiredInt(parameters, "macs_per_pe_per_cycle"),
        RequiredInt(parameters, "link_bits_per_cycle"),
        RequiredInt(parameters, "memory_ports"),
        RequiredInt(parameters, "queue_depth"),
        RequiredString(parameters, "graph_hash"),
        RequiredString(parameters, "workload_hash"),
        RequiredString(parameters, "mapping_hash"),
        RequiredString(parameters, "model_hash"));

    private static string CimScheduleHash(long waves, int instances, int invocationLatency, long invocations)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var remaining = invocations;
        for (var wave = 0L; wave < waves; wave++)
        {
            var active = (int)Math.Min(instances, remaining);
            for (var cycle = 0; cycle < invocationLatency; cycle++)
                hash.AppendData(Encoding.UTF8.GetBytes($"{wave}|{cycle}|{active}\n"));
            remaining -= active;
        }
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private static string Sha256(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private static string RequiredString(JsonElement element, string name) => element.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String ? property.GetString()! : throw new InvalidDataException($"Missing string parameter '{name}'.");
    private static int RequiredInt(JsonElement element, string name) => element.TryGetProperty(name, out var property) && property.TryGetInt32(out var value) ? value : throw new InvalidDataException($"Missing integer parameter '{name}'.");
    private static long RequiredLong(JsonElement element, string name) => element.TryGetProperty(name, out var property) && property.TryGetInt64(out var value) ? value : throw new InvalidDataException($"Missing integer parameter '{name}'.");

    private static void WriteOutput<T>(string outputPath, T payload)
    {
        var destination = Path.GetFullPath(outputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.WriteAllText(destination, JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine);
    }
}
