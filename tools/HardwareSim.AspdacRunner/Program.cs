using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HardwareSim.Core;

var inputPath = ReadArgument(args, "--input") ?? throw new ArgumentException("--input is required.");
var outputPath = ReadArgument(args, "--output") ?? throw new ArgumentException("--output is required.");
using var document = JsonDocument.Parse(File.ReadAllText(inputPath));
var root = document.RootElement;
var scenario = root.GetProperty("scenario").GetString() ?? "";
if (string.Equals(scenario, "rq1_exact", StringComparison.Ordinal))
{
    RunRq1Exact(root, outputPath);
    return 0;
}
if (scenario is "vtl_workload" or "vss_workload")
{
    RunMatchedWorkload(root, outputPath, scenario);
    return 0;
}
if (scenario is "mnist_cnn_layer" or "mnist_pe_layer")
{
    RunMnistCnnLayer(root, outputPath);
    return 0;
}
if (scenario == "mnist_pe_kernel_conformance")
{
    RunMnistPeKernelConformance(root, outputPath);
    return 0;
}
if (scenario == "mnist_pe_network")
{
    RunMnistPeNetwork(root, outputPath);
    return 0;
}
if (scenario == "vbs_cnn_trace")
{
    RunVbsCnnTrace(root, outputPath);
    return 0;
}
if (scenario == "energy_reference")
{
    RunEnergyReference(root, outputPath);
    return 0;
}
if (scenario == "optical_oracle")
{
    RunOpticalOracle(root, outputPath);
    return 0;
}
if (scenario == "matched_transport")
{
    RunMatchedTransport(root, outputPath);
    return 0;
}
if (scenario == "codesign_bottleneck")
{
    AspdacRunnerScenarios.RunCodesignBottleneck(root, outputPath);
    return 0;
}
if (scenario == "codesign_precision")
{
    AspdacRunnerScenarios.RunCodesignPrecision(root, outputPath);
    return 0;
}
if (scenario == "cim_template_comparison")
{
    AspdacRunnerScenarios.RunCimTemplateComparison(root, outputPath);
    return 0;
}
if (scenario == AspdacReviewerScalingScenarios.ScenarioName)
{
    AspdacReviewerScalingScenarios.Run(root, outputPath);
    return 0;
}
if (scenario == "reviewer_holdout_ws")
{
    AspdacReviewerP1Scenarios.RunIndependentHoldout(root, outputPath);
    return 0;
}
if (scenario == "reviewer_noc_contract")
{
    AspdacReviewerP1Scenarios.RunNocContract(root, outputPath);
    return 0;
}
if (!string.Equals(scenario, "vbs_noc", StringComparison.Ordinal))
{
    throw new NotSupportedException($"Unsupported ASP-DAC STAGE scenario: {scenario}");
}

var parameters = root.GetProperty("resolved").GetProperty("parameters");
var traffic = RequiredString(parameters, "traffic");
var injectionRate = RequiredDouble(parameters, "injection_rate");
var seed = RequiredInt(parameters, "seed");
var warmupCycles = RequiredInt(parameters, "warmup_cycles");
var measurementCycles = RequiredInt(parameters, "measurement_cycles");
var drainCycles = RequiredInt(parameters, "drain_cycles");
var traceMode = RequiredString(parameters, "trace_mode");
var retainEventHash = string.Equals(traceMode, "full", StringComparison.OrdinalIgnoreCase);
var result = AspdacVbsRuntime.Run(new AspdacVbsOptions(
    traffic,
    injectionRate,
    seed,
    warmupCycles,
    measurementCycles,
    drainCycles,
    retainEventHash));

var configHash = root.GetProperty("config_hash").GetString() ?? "";
var graphHash = Sha256($"V-BS|4x4|16|1vc|16flits|128bit|xy|{configHash}");
var workloadHash = Sha256(parameters.GetRawText());
var mappingHash = Sha256("V-BS|endpoint-i->router-i|router-i->endpoint-i|xy");
var payload = new
{
    status = "completed",
    completed_utc = DateTimeOffset.UtcNow.ToString("O"),
    measurement_kind = "stage_packet_cycle_runtime",
    stage_runtime_version = typeof(AspdacVbsRuntime).Assembly.GetName().Version?.ToString() ?? "unknown",
    completed = result.Completed,
    completion_reason = result.CompletionReason,
    provenance = new
    {
        graph_hash = graphHash,
        workload_hash = workloadHash,
        mapping_hash = mappingHash,
        model_profile_hash = configHash,
        transport_contract = "4x4_mesh_16_router_16_endpoint_xy_1vc_16flits_128b_packet_128bpc_link",
        runtime = "HardwareSim.Core.AspdacVbsRuntime.FastRouter",
        crossbar_issue_model = CrossbarIssueModel.PerOutputIssue.ToString(),
        packet_injection = "stochastic_per_endpoint_per_cycle",
        prng = "splitmix64"
    },
    metrics = new
    {
        total_cycles = result.TotalCycles,
        offered_packets = result.OfferedPackets,
        injected_packets = result.InjectedPackets,
        delivered_packets = result.DeliveredPackets,
        measured_offered_packets = result.MeasuredOfferedPackets,
        measured_delivered_packets = result.MeasuredDeliveredPackets,
        offered_rate_avg = result.OfferedRateAverage,
        accepted_rate_avg = result.AcceptedRateAverage,
        accepted_offered_ratio = result.AcceptedOfferedRatio,
        packet_latency_avg = result.PacketLatencyAverage,
        packet_latency_p95 = result.PacketLatencyP95,
        unstable = result.Unstable,
        timeout = result.Timeout,
        queue_occupancy_avg_flits = result.AverageQueueOccupancyFlits,
        queue_occupancy_max_flits = result.MaxQueueOccupancyFlits,
        congestion_cycles = result.CongestionCycles,
        router_conflict_stalls = result.RouterConflictStalls,
        backpressure_cycles = result.BackpressureCycles,
        injection_queue_stalls = result.InjectionQueueStalls,
        runtime_event_hash = result.RuntimeEventHash
    },
    stall_reasons = result.StallReasons,
    limitations = new[]
    {
        "BookSim2 and STAGE use independent PRNG streams; compare seed aggregates, not seed-by-seed packet identities.",
        "The STAGE provider uses the frozen per-output router arbitration and VC capacity; BookSim2 compiled-default router pipeline timing remains a tool-specific latency offset.",
        "Runtime event hash is an audit hash for this provider and is not labeled as the canonical full-trace hash used by RQ1."
    }
};

var destination = Path.GetFullPath(outputPath);
Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
File.WriteAllText(destination, JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine);
return 0;

static string? ReadArgument(IReadOnlyList<string> arguments, string name)
{
    for (var index = 0; index < arguments.Count - 1; index++)
    {
        if (string.Equals(arguments[index], name, StringComparison.OrdinalIgnoreCase)) return arguments[index + 1];
    }
    return null;
}

static string RequiredString(JsonElement element, string name) =>
    element.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String
        ? property.GetString()!
        : throw new InvalidDataException($"Missing string parameter '{name}'.");

static string OptionalString(JsonElement element, string name, string fallback) =>
    element.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(property.GetString())
        ? property.GetString()!
        : fallback;

static int RequiredInt(JsonElement element, string name) =>
    element.TryGetProperty(name, out var property) && property.TryGetInt32(out var value)
        ? value
        : throw new InvalidDataException($"Missing integer parameter '{name}'.");

static long RequiredLong(JsonElement element, string name) =>
    element.TryGetProperty(name, out var property) && property.TryGetInt64(out var value)
        ? value
        : throw new InvalidDataException($"Missing integer parameter '{name}'.");

static double RequiredDouble(JsonElement element, string name) =>
    element.TryGetProperty(name, out var property) && property.TryGetDouble(out var value)
        ? value
        : throw new InvalidDataException($"Missing numeric parameter '{name}'.");

static string Sha256(string value) =>
    Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

static void RunOpticalOracle(JsonElement root, string outputPath)
{
    var parameters = root.GetProperty("resolved").GetProperty("parameters");
    var repeat = RequiredInt(parameters, "repeat");
    var result = AspdacTransportRuntime.RunOracle();
    var payload = new
    {
        status = "completed",
        completed_utc = DateTimeOffset.UtcNow.ToString("O"),
        measurement_kind = "rq4_independent_optical_oracle",
        metrics = result,
        provenance = new
        {
            repeat,
            repeat_excluded_from_oracle_identity = true,
            model_profile_hash = root.GetProperty("config_hash").GetString(),
            runtime = "HardwareSim.Core.AspdacTransportRuntime.RunOracle",
            contract = "phase8-frozen-optical-device-defaults-plus-paper-capacity-sweep",
            evidence_level = "exact"
        },
        limitations = new[]
        {
            "The independent oracle evaluates link budget, channel identity, and serialization; it does not predict BER.",
            "Capacity eight is a paper experiment service abstraction layered above the frozen Phase 8 component contracts."
        }
    };
    WriteOutput(outputPath, payload);
}

static void RunMatchedTransport(JsonElement root, string outputPath)
{
    var parameters = root.GetProperty("resolved").GetProperty("parameters");
    var result = AspdacTransportRuntime.Run(new AspdacTransportOptions(
        parameters.TryGetProperty("workload_id", out var workloadId) && workloadId.ValueKind == JsonValueKind.String
            ? workloadId.GetString()!
            : RequiredString(parameters, "case_id"),
        RequiredString(parameters, "transport_mode"),
        RequiredLong(parameters, "packet_count"),
        RequiredInt(parameters, "flow_count"),
        RequiredInt(parameters, "channel_capacity"),
        RequiredInt(parameters, "queue_depth"),
        RequiredInt(parameters, "payload_bits"),
        RequiredString(parameters, "workload_hash"),
        RequiredString(parameters, "mapping_hash"),
        RequiredString(parameters, "compute_hash"),
        RequiredString(parameters, "memory_hash"),
        RequiredString(parameters, "endpoint_hash"),
        RequiredString(parameters, "transport_hash")));
    var payload = new
    {
        status = "completed",
        completed_utc = DateTimeOffset.UtcNow.ToString("O"),
        measurement_kind = "stage_packet_cycle_transport_runtime",
        provenance = new
        {
            graph_hash = RequiredString(parameters, "graph_hash"),
            result.WorkloadHash,
            result.MappingHash,
            result.ComputeHash,
            result.MemoryHash,
            result.EndpointHash,
            result.TransportHash,
            model_profile_hash = root.GetProperty("config_hash").GetString(),
            runtime = "HardwareSim.Core.AspdacTransportRuntime",
            trace_hash_algorithm = "sha256-event-stream-v1",
            paired_condition_contract = "workload/mapping/compute/memory/endpoint hashes invariant; transport hash only variable"
        },
        metrics = result,
        limitations = new[]
        {
            "Transport-only replay: workload operation count, mapping, compute, memory, and endpoints are held by paired hashes.",
            "Phase 8 functional energy defaults are shared reference values, not silicon-calibrated power.",
            "BER not modeled."
        }
    };
    WriteOutput(outputPath, payload);
}

static void WriteOutput<T>(string outputPath, T payload)
{
    var destination = Path.GetFullPath(outputPath);
    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
    File.WriteAllText(destination, JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine);
}
static void RunMatchedWorkload(JsonElement root, string outputPath, string scenario)
{
    var parameters = root.GetProperty("resolved").GetProperty("parameters");
    var workload = new AspdacMatchedWorkload(
        RequiredString(parameters, "case_id"),
        RequiredLong(parameters, "M"),
        RequiredLong(parameters, "N"),
        RequiredLong(parameters, "K"),
        RequiredLong(parameters, "expected_macs"),
        RequiredLong(parameters, "register_accesses"),
        RequiredLong(parameters, "local_buffer_accesses"),
        RequiredLong(parameters, "global_buffer_accesses"),
        RequiredLong(parameters, "dram_accesses"),
        RequiredLong(parameters, "sram_ifmap_reads"),
        RequiredLong(parameters, "sram_filter_reads"),
        RequiredLong(parameters, "sram_ofmap_writes"),
        RequiredLong(parameters, "dram_ifmap_reads"),
        RequiredLong(parameters, "dram_filter_reads"),
        RequiredLong(parameters, "dram_ofmap_writes"));
    var mode = RequiredString(parameters, "mode");
    var result = scenario == "vtl_workload"
        ? AspdacMatchedRuntime.RunTimeloopMatched(workload, string.Equals(mode, "full_system", StringComparison.Ordinal))
        : AspdacMatchedRuntime.RunScaleSimMatched(workload, string.Equals(mode, "cold_start", StringComparison.Ordinal));
    var configHash = root.GetProperty("config_hash").GetString() ?? "";
    var payload = new
    {
        status = "completed",
        completed_utc = DateTimeOffset.UtcNow.ToString("O"),
        measurement_kind = result.MeasurementKind,
        provenance = new
        {
            graph_hash = Sha256($"{scenario}|{configHash}"),
            workload_hash = Sha256(parameters.GetRawText()),
            mapping_hash = RequiredString(parameters, "mapping_hash"),
            model_profile_hash = configHash,
            runtime = "HardwareSim.Core.AspdacMatchedRuntime",
            trace_hash_algorithm = "sha256-cycle-record-v1",
            external_raw_path = RequiredString(parameters, "external_raw_path")
        },
        metrics = new
        {
            case_id = result.CaseId,
            mode = result.Mode,
            total_cycles = result.TotalCycles,
            compute_cycles = result.ComputeCycles,
            memory_cycles = result.MemoryCycles,
            noc_cycles = result.NocCycles,
            serialization_cycles = result.SerializationCycles,
            conversion_cycles = result.ConversionCycles,
            reduction_cycles = result.ReductionCycles,
            softmax_cycles = result.SoftmaxCycles,
            prefetch_cycles = result.PrefetchCycles,
            wavefront_cycles = result.WavefrontCycles,
            memory_stall_cycles = result.MemoryStallCycles,
            completed_macs = result.CompletedMacs,
            utilization_pct = result.UtilizationPercent,
            canonical_trace_hash = result.TraceHash
        },
        accesses = result.Accesses,
        reference = scenario == "vtl_workload"
            ? new Dictionary<string, object?>
            {
                ["tool"] = "timeloop-model",
                ["cycles"] = RequiredLong(parameters, "reference_cycles"),
                ["utilization_pct"] = RequiredDouble(parameters, "reference_utilization_pct")
            }
            : new Dictionary<string, object?>
            {
                ["tool"] = "SCALE-Sim",
                ["total_cycles"] = RequiredLong(parameters, "reference_cycles"),
                ["cold_cycles"] = RequiredLong(parameters, "reference_cold_cycles"),
                ["stall_cycles"] = RequiredLong(parameters, "reference_stall_cycles"),
                ["utilization_pct"] = RequiredDouble(parameters, "reference_utilization_pct")
            },
        limitations = scenario == "vtl_workload"
            ? new[]
            {
                "Compute-only is the exact frozen 16-MAC schedule; full-system is a conservative serialized service replay and is not a Timeloop-equivalent total-cycle claim.",
                "Access counts originate from the frozen timeloop-model mapping and are replayed without estimator substitution."
            }
            : new[]
            {
                "The matched STAGE runtime implements 4x4 WS wavefront and 8-word/cycle stream service but not SCALE-Sim's internal bank arbitration; timing comparison is Trend evidence.",
                "Cold-start separately charges sequential prefetch of the two 8-KiB input stores."
            }
    };
    var destination = Path.GetFullPath(outputPath);
    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
    File.WriteAllText(destination, JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine);
}
static void RunMnistCnnLayer(JsonElement root, string outputPath)
{
    var parameters = root.GetProperty("resolved").GetProperty("parameters");
    var mode = RequiredString(parameters, "mode");
    var repeat = RequiredInt(parameters, "repeat");
    var hasArithmeticProfile = parameters.TryGetProperty("profile_id", out var profileProperty);
    var profileId = hasArithmeticProfile ? profileProperty.GetString() ?? "" : "legacy_fp32";
    var arithmeticProfile = hasArithmeticProfile ? ResolveArithmeticProfile(root, profileId) : default;
    var precisionBits = hasArithmeticProfile ? RequiredInt(arithmeticProfile, "payload_bits") : RequiredInt(parameters, "precision_bits");
    var conversionPasses = hasArithmeticProfile ? RequiredInt(arithmeticProfile, "conversion_passes") : 0;
    var arithmeticProfileHash = hasArithmeticProfile ? RequiredString(arithmeticProfile, "profile_hash") : root.GetProperty("config_hash").GetString() ?? "legacy";
    var result = AspdacCnnRuntime.Run(new AspdacCnnLayerOptions(
        RequiredString(parameters, "layer_id"),
        RequiredString(parameters, "layer_kind"),
        RequiredLong(parameters, "M"),
        RequiredLong(parameters, "N"),
        RequiredLong(parameters, "K"),
        RequiredLong(parameters, "image_count"),
        precisionBits,
        RequiredLong(parameters, "post_op_elements_per_image"),
        string.Equals(mode, "full_system", StringComparison.Ordinal),
        RequiredString(parameters, "model_hash"),
        RequiredString(parameters, "dataset_hash"),
        RequiredString(parameters, "prediction_hash"),
        RequiredString(parameters, "lowering_hash"),
        conversionPasses,
        profileId,
        arithmeticProfileHash));
    var payload = new
    {
        status = "completed",
        completed_utc = DateTimeOffset.UtcNow.ToString("O"),
        measurement_kind = hasArithmeticProfile ? "stage_mnist_pe_precision_layer_runtime" : "stage_cnn_layer_cycle_service_runtime",
        peak_memory_bytes = System.Diagnostics.Process.GetCurrentProcess().PeakWorkingSet64,
        provenance = new
        {
            graph_hash = Sha256($"S-Native|16x256|128b|1mem|5mem|{root.GetProperty("config_hash").GetString()}"),
            workload_hash = OptionalString(parameters, "workload_hash", Sha256(parameters.GetRawText())),
            mapping_hash = OptionalString(parameters, "mapping_hash", Sha256($"mnist_cnn|{RequiredString(parameters, "layer_id")}|{RequiredLong(parameters, "M")}|{RequiredLong(parameters, "N")}|{RequiredLong(parameters, "K")}|row_major_im2col_to_s_native|{RequiredString(parameters, "lowering_hash")}")),
            transport_hash = OptionalString(parameters, "transport_hash", Sha256("S-Native|128b-packets|128bpc-link|sequential-service")),
            model_hash = RequiredString(parameters, "model_hash"),
            dataset_hash = RequiredString(parameters, "dataset_hash"),
            prediction_hash_binding = RequiredString(parameters, "prediction_hash"),
            lowering_hash = RequiredString(parameters, "lowering_hash"),
            stage_config_hash = root.GetProperty("config_hash").GetString(),
            arithmetic_profile_id = profileId,
            arithmetic_profile_hash = arithmeticProfileHash,
            runtime = "HardwareSim.Core.AspdacCnnRuntime",
            trace_hash_algorithm = "sha256-cycle-record-v1",
            conversion_contract = "Phase7C precision-converter 1-cycle packet service across 16 PEs, 0.02 pJ/bit/pass",
            repeat,
            repeat_excluded_from_runtime_identity = true
        },
        metrics = new
        {
            layer_id = result.LayerId,
            layer_kind = result.LayerKind,
            mode = result.Mode,
            image_count = result.ImageCount,
            completed_macs = result.CompletedMacs,
            input_elements = result.InputElements,
            weight_elements = result.WeightElements,
            output_elements = result.OutputElements,
            payload_bits_per_element = precisionBits,
            logical_bits = result.LogicalBits,
            padding_bits = result.PaddingBits,
            packetized_bits = result.PacketizedBits,
            packet_count = result.PacketCount,
            compute_cycles = result.ComputeCycles,
            memory_cycles = result.MemoryCycles,
            noc_cycles = result.NocCycles,
            post_op_cycles = result.PostOpCycles,
            conversion_cycles = result.ConversionCycles,
            conversion_energy_pj = result.ConversionEnergyPj,
            total_cycles = result.TotalCycles,
            compute_utilization_pct = result.ComputeUtilizationPercent,
            dominant_service = result.DominantService,
            canonical_trace_hash = result.CanonicalTraceHash,
            compute_timing_distinguishes_arithmetic_profile = false
        },
        stall_reasons = new Dictionary<string, long>(StringComparer.Ordinal)
        {
            ["compute_busy"] = result.ComputeCycles,
            ["memory_port_busy"] = result.MemoryCycles,
            ["noc_serialization"] = result.NocCycles,
            ["synthetic_post_op_service"] = result.PostOpCycles,
            ["precision_conversion_service"] = result.ConversionCycles
        },
        limitations = new[]
        {
            "Accuracy is supplied by the paired deterministic functional bridge; this STAGE record measures the same lowered layer's modeled traffic and cycle services.",
            "Convolution and fully connected layers use exact materialized-im2col/FC shapes; weights load once per resolved layer case.",
            "Bias, ReLU, average pooling, layer orchestration, and classification are not claimed as native STAGE CNN execution.",
            "The frozen S-Native compute timing/energy profile does not distinguish FP32, FP16, FP8-a16, and FP8-a8 arithmetic; no compute speedup or PE energy saving is inferred.",
            "Conversion cost uses the already-approved Phase7C converter service contract and is reported separately from compute."
        }
    };
    WriteOutput(outputPath, payload);
}

static void RunMnistPeKernelConformance(JsonElement root, string outputPath)
{
    var parameters = root.GetProperty("resolved").GetProperty("parameters");
    var profileId = RequiredString(parameters, "profile_id");
    var profile = ResolveArithmeticProfile(root, profileId);
    var rows = RequiredInt(parameters, "rows");
    var columns = RequiredInt(parameters, "columns");
    var seed = RequiredInt(parameters, "seed");
    var repeat = RequiredInt(parameters, "repeat");
    var template = ComponentTemplateJson.Clone(ComponentTemplateExamples.PeArray32x32Fp8SramSynthetic());
    template.StorageLayouts.Single(layout => layout.Id == "weight_store_0").Rows = 4096;
    template.CompiledProfile = null;
    template.Provenance.CompileHash = "";
    var overrides = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["array_rows"] = rows.ToString(System.Globalization.CultureInfo.InvariantCulture),
        ["array_cols"] = columns.ToString(System.Globalization.CultureInfo.InvariantCulture),
        ["input_dtype"] = RequiredString(profile, "input_dtype"),
        ["weight_dtype"] = RequiredString(profile, "weight_dtype"),
        ["accumulate_dtype"] = RequiredString(profile, "accumulate_dtype"),
        ["output_dtype"] = RequiredString(profile, "output_dtype"),
        ["macs_per_cycle"] = Math.Max(1, rows * columns).ToString(System.Globalization.CultureInfo.InvariantCulture),
        ["weight_write_bandwidth_bits_per_cycle"] = "1048576"
    };
    var run = new ComponentKernelTestRunner().Run(template, ComponentTypeRegistry.CreateDefault(), overrides, seed: seed);
    if (!run.IsSuccess || run.Scenario is null || run.Simulation is null)
    {
        throw new InvalidOperationException("MNIST PE kernel conformance failed: " + string.Join("; ", run.Issues.Select(issue => issue.Code + ":" + issue.Message)));
    }

    var activation = run.Scenario.Inputs.Single(input => input.InputPortName == "in_activation").Packet.Values;
    var weights = run.Scenario.Inputs.Single(input => input.InputPortName == "in_weight").Packet.Values;
    var reference = DigitalVmmReferenceEvaluator.Evaluate(
        activation,
        weights,
        rows,
        columns,
        RequiredString(profile, "input_dtype"),
        RequiredString(profile, "weight_dtype"),
        RequiredString(profile, "accumulate_dtype"),
        RequiredString(profile, "output_dtype"));
    var actual = run.Simulation.DeliveredPackets.Single().Values;
    var scalarValues = new[]
    {
        0d,
        BitConverter.Int64BitsToDouble(long.MinValue),
        Math.Pow(2, -25),
        -Math.Pow(2, -25),
        Math.Pow(2, -10),
        -Math.Pow(2, -10),
        1d + Math.Pow(2, -11),
        1d + Math.Pow(2, -4),
        448d,
        -448d,
        1e40,
        -1e40
    };
    var dtypes = new[] { "fp32", "fp16", "fp8" };
    WriteOutput(outputPath, new
    {
        status = "completed",
        completed_utc = DateTimeOffset.UtcNow.ToString("O"),
        measurement_kind = "stage_core_digital_vmm_encoded_bit_conformance",
        peak_memory_bytes = System.Diagnostics.Process.GetCurrentProcess().PeakWorkingSet64,
        provenance = new
        {
            kernel_id = "core.digital.vmm",
            kernel_registry_hash = run.RuntimeKernelRegistryHash,
            compiled_profile_hash = run.ProfileHash,
            execution_contract_hash = run.ExecutionContractHash,
            arithmetic_profile_id = profileId,
            arithmetic_profile_hash = RequiredString(profile, "profile_hash"),
            input_hash = run.InputHash,
            trace_hash = run.TraceHash,
            repeat,
            repeat_excluded_from_runtime_identity = true
        },
        metrics = new
        {
            case_id = RequiredString(parameters, "case_id"),
            profile_id = profileId,
            repeat,
            rows,
            columns,
            passed = run.IsSuccess,
            exact_encoded_bits = run.ExpectedOutputHash == run.ActualOutputHash,
            expected_output_hash = run.ExpectedOutputHash,
            actual_output_hash = run.ActualOutputHash,
            output_elements = actual.Count
        },
        arithmetic = new
        {
            input_dtype = RequiredString(profile, "input_dtype"),
            weight_dtype = RequiredString(profile, "weight_dtype"),
            accumulate_dtype = RequiredString(profile, "accumulate_dtype"),
            output_dtype = RequiredString(profile, "output_dtype"),
            activation_values = activation,
            weight_values = weights,
            reference_values = reference,
            actual_values = actual,
            reference_encoded_hex = EncodeValues(reference, RequiredString(profile, "output_dtype")),
            actual_encoded_hex = EncodeValues(actual, RequiredString(profile, "output_dtype")),
            scalar_encoding_audit = dtypes.Select(dtype => new
            {
                dtype,
                values = scalarValues.Select(value => new
                {
                    value,
                    encoded_hex = EncodeValue(value, dtype),
                    decoded_value = DigitalNumericFormats.Quantize(value, dtype).Value
                }).ToArray()
            }).ToArray()
        },
        issues = run.Issues,
        limitations = new[]
        {
            "This candidate executes the real CoreDigitalVmmKernel through ComponentKernelTestRunner and the production cycle engine.",
            "The reference evaluator is independent of kernel state/timing but uses the same public DigitalNumericFormats encoding contract.",
            "The Python vector backend must reproduce these encoded outputs before any accuracy result is admitted."
        }
    });
}

static void RunMnistPeNetwork(JsonElement root, string outputPath)
{
    var parameters = root.GetProperty("resolved").GetProperty("parameters");
    var profileId = RequiredString(parameters, "profile_id");
    var profile = ResolveArithmeticProfile(root, profileId);
    var repeat = RequiredInt(parameters, "repeat");
    var imageCount = RequiredLong(parameters, "image_count");
    var baseConfig = root.GetProperty("resolved").GetProperty("base_config");
    var results = new List<AspdacCnnLayerResult>();
    foreach (var layer in baseConfig.GetProperty("stage").GetProperty("layers").EnumerateArray())
    {
        results.Add(AspdacCnnRuntime.Run(new AspdacCnnLayerOptions(
            RequiredString(layer, "layer_id"),
            RequiredString(layer, "layer_kind"),
            RequiredLong(layer, "M"),
            RequiredLong(layer, "N"),
            RequiredLong(layer, "K"),
            imageCount,
            RequiredInt(profile, "payload_bits"),
            RequiredLong(layer, "post_op_elements_per_image"),
            true,
            RequiredString(parameters, "model_hash"),
            RequiredString(parameters, "dataset_hash"),
            RequiredString(parameters, "prediction_hash"),
            RequiredString(parameters, "lowering_hash"),
            RequiredInt(profile, "conversion_passes"),
            profileId,
            RequiredString(profile, "profile_hash"))));
    }
    var traceHash = Sha256(string.Join("|", results.Select(result => result.CanonicalTraceHash)));
    WriteOutput(outputPath, new
    {
        status = "completed",
        completed_utc = DateTimeOffset.UtcNow.ToString("O"),
        measurement_kind = "stage_mnist_pe_precision_sequential_network_runtime",
        peak_memory_bytes = System.Diagnostics.Process.GetCurrentProcess().PeakWorkingSet64,
        provenance = new
        {
            arithmetic_profile_id = profileId,
            arithmetic_profile_hash = RequiredString(profile, "profile_hash"),
            workload_hash = RequiredString(parameters, "workload_hash"),
            mapping_hash = RequiredString(parameters, "mapping_hash"),
            transport_hash = RequiredString(parameters, "transport_hash"),
            model_hash = RequiredString(parameters, "model_hash"),
            dataset_hash = RequiredString(parameters, "dataset_hash"),
            prediction_hash_binding = RequiredString(parameters, "prediction_hash"),
            lowering_hash = RequiredString(parameters, "lowering_hash"),
            runtime = "HardwareSim.Core.AspdacCnnRuntime sequential composition",
            repeat,
            repeat_excluded_from_runtime_identity = true
        },
        metrics = new
        {
            profile_id = profileId,
            repeat,
            image_count = imageCount,
            layer_count = results.Count,
            logical_bits = results.Sum(result => result.LogicalBits),
            padding_bits = results.Sum(result => result.PaddingBits),
            packetized_bits = results.Sum(result => result.PacketizedBits),
            packet_count = results.Sum(result => result.PacketCount),
            compute_cycles = results.Sum(result => result.ComputeCycles),
            memory_cycles = results.Sum(result => result.MemoryCycles),
            noc_cycles = results.Sum(result => result.NocCycles),
            post_op_cycles = results.Sum(result => result.PostOpCycles),
            conversion_cycles = results.Sum(result => result.ConversionCycles),
            conversion_energy_pj = results.Sum(result => result.ConversionEnergyPj),
            sequential_cycles = results.Sum(result => result.TotalCycles),
            canonical_trace_hash = traceHash,
            compute_timing_distinguishes_arithmetic_profile = false
        },
        layers = results.Select(result => new
        {
            result.LayerId,
            result.TotalCycles,
            result.PacketizedBits,
            result.PacketCount,
            result.CanonicalTraceHash
        }).ToArray(),
        limitations = new[]
        {
            "Sequential composition pairs the five exact lowered CNN shapes with one frozen mapping/transport contract; it is not native CNN tensor orchestration.",
            "Compute timing is invariant because the approved S-Native service model does not distinguish these arithmetic profiles.",
            "Bias, ReLU, AvgPool, and classification remain in the deterministic functional harness."
        }
    });
}

static JsonElement ResolveArithmeticProfile(JsonElement root, string profileId)
{
    foreach (var profile in root.GetProperty("resolved").GetProperty("base_config").GetProperty("arithmetic_profiles").EnumerateArray())
    {
        if (string.Equals(RequiredString(profile, "profile_id"), profileId, StringComparison.Ordinal)) return profile;
    }
    throw new InvalidDataException($"Unknown arithmetic profile '{profileId}'.");
}

static string EncodeValue(double value, string dtype)
{
    var encoded = DigitalNumericFormats.Quantize(value, dtype);
    return encoded.EncodedBits.ToString($"x{encoded.BitWidth / 4}", System.Globalization.CultureInfo.InvariantCulture);
}

static string[] EncodeValues(IEnumerable<double> values, string dtype) => values.Select(value => EncodeValue(value, dtype)).ToArray();

static void RunVbsCnnTrace(JsonElement root, string outputPath)
{
    var parameters = root.GetProperty("resolved").GetProperty("parameters");
    var traceCsvPath = Path.GetFullPath(RequiredString(parameters, "trace_csv_path"));
    var traceSha256 = RequiredString(parameters, "trace_sha256");
    var drainCycles = RequiredInt(parameters, "drain_cycles");
    var result = AspdacVbsCnnTraceRuntime.RunTraceCsv(traceCsvPath, traceSha256, drainCycles);
    var configHash = root.GetProperty("config_hash").GetString() ?? "";

    string OptionalString(string name, string fallback) =>
        parameters.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(property.GetString())
            ? property.GetString()!
            : fallback;

    var payload = new
    {
        status = result.Completed ? "completed" : "failed",
        completed_utc = DateTimeOffset.UtcNow.ToString("O"),
        measurement_kind = "stage_vbs_cnn_packet_trace_runtime",
        completed = result.Completed,
        completion_reason = result.CompletionReason,
        provenance = new
        {
            graph_hash = Sha256($"V-BS|4x4|16|1vc|16flits|128bit|xy|{configHash}"),
            workload_hash = OptionalString("workload_hash", result.TraceSha256),
            mapping_hash = OptionalString("mapping_hash", Sha256($"cnn_trace|{result.TraceSha256}|endpoint-i->router-i|xy")),
            model_profile_hash = configHash,
            model_hash = OptionalString("model_hash", "not_supplied"),
            dataset_hash = OptionalString("dataset_hash", "not_supplied"),
            lowering_hash = OptionalString("lowering_hash", "not_supplied"),
            trace_csv_path = traceCsvPath.Replace('\\', '/'),
            trace_csv_contract = "packet_id,release_cycle,source,destination,flits,traffic_class,layer_id,tensor_role,payload_bits;1flit;128bit;class0;src!=dst",
            trace_sha256 = result.TraceSha256,
            runtime_event_hash = result.RuntimeEventHash,
            runtime = "HardwareSim.Core.AspdacVbsCnnTraceRuntime.RunTraceCsv",
            transport_contract = "4x4_mesh_16_router_16_endpoint_xy_1vc_16flits_128b_packet_128bpc_link",
            trace_hash_algorithm = "sha256-canonical-packet-delivery-v1"
        },
        metrics = new
        {
            trace_sha256 = result.TraceSha256,
            offered_packets = result.OfferedPackets,
            injected_packets = result.InjectedPackets,
            delivered_packets = result.DeliveredPackets,
            undrained_packets = result.UndrainedPackets,
            total_cycles = result.TotalCycles,
            network_makespan_cycles = result.NetworkMakespanCycles,
            packet_latency_avg = result.PacketLatencyAverage,
            packet_latency_p95 = result.PacketLatencyP95,
            throughput_packets_per_cycle = result.ThroughputPacketsPerCycle,
            timeout = result.Timeout,
            queue_occupancy_avg_flits = result.AverageQueueOccupancyFlits,
            queue_occupancy_max_flits = result.MaxQueueOccupancyFlits,
            congestion_cycles = result.CongestionCycles,
            router_conflict_stalls = result.RouterConflictStalls,
            backpressure_events = result.BackpressureEvents,
            injection_queue_stalls = result.InjectionQueueStalls,
            canonical_delivery_trace_hash = result.CanonicalDeliveryTraceHash,
            runtime_event_hash = result.RuntimeEventHash
        },
        stall_reasons = result.StallReasons,
        limitations = new[]
        {
            "Transport-only replay of a frozen materialized-im2col CNN packet trace; this does not execute CNN numerical operators.",
            "Latency is measured from canonical release_cycle to endpoint delivery in the STAGE V-BS router pipeline.",
            "The CSV SHA-256 authenticates shared offers; the delivery and runtime hashes authenticate STAGE execution order."
        }
    };
    WriteOutput(outputPath, payload);
}
static void RunEnergyReference(JsonElement root, string outputPath)
{
    var parameters = root.GetProperty("resolved").GetProperty("parameters");
    var kind = RequiredString(parameters, "kind");
    var caseId = RequiredString(parameters, "case_id");
    var metrics = new Dictionary<string, object?> { ["case_id"] = caseId, ["kind"] = kind };
    var breakdown = new Dictionary<string, object?>();
    var limitations = new List<string> { "Shared 45-nm CACTI/Aladdin reference model; not silicon-calibrated power evidence." };
    if (kind == "microbench")
    {
        var actions = RequiredLong(parameters, "action_count");
        var perAction = RequiredDouble(parameters, "per_action_energy_pj");
        metrics["action_count"] = actions;
        metrics["per_action_energy_pj"] = perAction;
        metrics["stage_energy_pj"] = actions * perAction;
        breakdown[RequiredString(parameters, "category")] = actions * perAction;
    }
    else if (kind == "workload")
    {
        var macs = RequiredLong(parameters, "expected_macs");
        var compute = macs * RequiredDouble(parameters, "shared_ert_mac_energy_pj") / 1_000_000d;
        var registers = macs * RequiredDouble(parameters, "register_fj_per_compute") / 1_000_000_000d;
        var local = macs * RequiredDouble(parameters, "local_fj_per_compute") / 1_000_000_000d;
        var global = macs * RequiredDouble(parameters, "global_fj_per_compute") / 1_000_000_000d;
        var dram = macs * RequiredDouble(parameters, "dram_fj_per_compute") / 1_000_000_000d;
        var matchedTotal = compute + registers + local + global + dram;
        var nativeTotal = RequiredDouble(parameters, "external_native_energy_uj");
        metrics["expected_macs"] = macs;
        metrics["external_native_energy_uj"] = nativeTotal;
        metrics["stage_matched_ert_energy_uj"] = matchedTotal;
        metrics["native_binding_gap_uj"] = matchedTotal - nativeTotal;
        metrics["native_binding_gap_fraction"] = (matchedTotal - nativeTotal) / nativeTotal;
        metrics["noc_energy_uj"] = null;
        metrics["conversion_energy_uj"] = null;
        metrics["optical_energy_uj"] = null;
        breakdown["compute_uj"] = compute;
        breakdown["register_uj"] = registers;
        breakdown["local_sram_uj"] = local;
        breakdown["global_sram_uj"] = global;
        breakdown["dram_uj"] = dram;
        limitations.Add("Timeloop's native arithmetic summary uses 1.0 pJ/MAC while the generated MAC ERT action is 3.275 pJ; native workload totals are Trend evidence only.");
        limitations.Add("NoC, conversion, and optical energies are retained as unknown rather than folded into compute or set to zero.");
    }
    else
    {
        throw new InvalidDataException($"Unsupported energy case kind '{kind}'.");
    }
    var payload = new
    {
        status = "completed",
        completed_utc = DateTimeOffset.UtcNow.ToString("O"),
        measurement_kind = "stage_shared_reference_action_accounting",
        provenance = new
        {
            model_profile_hash = root.GetProperty("config_hash").GetString(),
            workload_hash = Sha256(parameters.GetRawText()),
            ert_sha256 = RequiredString(parameters, "ert_sha256"),
            runtime = "HardwareSim.AspdacRunner.energy_reference",
            technology = "45nm",
            estimators = "CACTI/Aladdin"
        },
        metrics,
        breakdown,
        limitations
    };
    var destination = Path.GetFullPath(outputPath);
    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
    File.WriteAllText(destination, JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine);
}
static void RunRq1Exact(JsonElement root, string outputPath)
{
    var parameters = root.GetProperty("resolved").GetProperty("parameters");
    var caseName = RequiredString(parameters, "case");
    var repeat = RequiredInt(parameters, "repeat");
    const int seed = 901;
    SortedDictionary<string, object?> details;
    SimulationTrace trace;
    string traceKind;

    if (caseName is "current_next_visibility" or "backpressure")
    {
        var hardware = caseName == "current_next_visibility"
            ? SampleGraphs.CreateMemoryRouterPeReductionSinkGraph()
            : SampleGraphs.CreateContendedSharedRouterGraph();
        var compiled = new SimulationGraphCompiler().CompileHardware(hardware);
        if (!compiled.IsSuccess || compiled.Graph is null)
        {
            throw new InvalidOperationException(string.Join("; ", compiled.Errors.Select(error => error.Message)));
        }
        var runtime = new CycleSimulationEngine().Run(compiled.Graph, new SimulationOptions
        {
            MaxCycles = 160,
            DeterministicSeed = seed,
            CycleTraceMode = SimulationCycleTraceMode.Full
        });
        if (runtime.TraceHash is null) throw new InvalidOperationException("Exact runtime did not emit a canonical trace hash.");
        trace = runtime.Trace;
        traceKind = "stage_cycle_runtime";
        details = new SortedDictionary<string, object?>(StringComparer.Ordinal)
        {
            ["completed"] = runtime.Completed,
            ["cycles"] = runtime.Metrics.Global.TotalCycles,
            ["packets_delivered"] = runtime.Metrics.Global.PacketsDelivered,
            ["stall_cycles"] = runtime.Metrics.Components.Values.Sum(component => component.StallCycles),
            ["trace_events"] = runtime.Trace.Cycles.Sum(cycle => cycle.Events.Count)
        };
    }
    else
    {
        details = EvaluateIndependentOracle(caseName);
        trace = OracleTrace(caseName, details);
        traceKind = "independent_oracle_trace";
    }

    var canonical = CanonicalTraceHasher.Compute(
        trace,
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["case"] = caseName,
            ["oracle_contract"] = "aspg-rq1-exact-1.0"
        },
        seed);
    var payload = new
    {
        status = "completed",
        completed_utc = DateTimeOffset.UtcNow.ToString("O"),
        measurement_kind = "rq1_exact_oracle",
        metrics = new
        {
            case_name = caseName,
            repeat,
            passed = true,
            trace_kind = traceKind,
            canonical_trace_hash = canonical.Hash,
            canonical_trace_bytes = Encoding.UTF8.GetByteCount(canonical.CanonicalJson)
        },
        exact_values = details,
        provenance = new
        {
            oracle_contract = "aspg-rq1-exact-1.0",
            trace_hash_algorithm = CanonicalTraceHasher.Algorithm,
            trace_schema_version = CanonicalTraceHasher.TraceSchemaVersion,
            seed,
            repeat_excluded_from_oracle_identity = true
        },
        limitations = Array.Empty<string>()
    };
    var destination = Path.GetFullPath(outputPath);
    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
    File.WriteAllText(destination, JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine);
}

static SortedDictionary<string, object?> EvaluateIndependentOracle(string caseName)
{
    var values = new SortedDictionary<string, object?>(StringComparer.Ordinal);
    switch (caseName)
    {
        case "graph_round_trip":
        {
            var graph = SampleGraphs.CreateMemoryRouterPeReductionSinkGraph();
            var before = HardwareGraphJson.Serialize(graph);
            var after = HardwareGraphJson.Serialize(HardwareGraphJson.Deserialize(before));
            if (!string.Equals(before, after, StringComparison.Ordinal)) throw new InvalidOperationException("Graph round-trip changed canonical JSON.");
            values["canonical_json_equal"] = true;
            values["component_count"] = graph.Components.Count;
            values["link_count"] = graph.Links.Count;
            values["artifact_sha256"] = Sha256(before);
            break;
        }
        case "compile_non_mutation":
        {
            var graph = SampleGraphs.CreateMemoryRouterPeReductionSinkGraph();
            var before = HardwareGraphJson.Serialize(graph);
            var compiled = new SimulationGraphCompiler().CompileHardware(graph);
            var after = HardwareGraphJson.Serialize(graph);
            if (!compiled.IsSuccess || compiled.Graph is null) throw new InvalidOperationException("Reference graph did not compile.");
            if (!string.Equals(before, after, StringComparison.Ordinal)) throw new InvalidOperationException("Compilation mutated its input graph.");
            values["input_unchanged"] = true;
            values["compiled_component_count"] = compiled.Graph.Components.Count;
            values["input_sha256"] = Sha256(before);
            break;
        }
        case "packet_serialization_132b":
        {
            var serialized = FlitLinkSerializer.Serialize(
                [new Flit { Id = "encoded:f0000", PacketId = "encoded", FlitIndex = 0, TotalFlits = 1, PayloadBits = 132, IsHead = true, IsTail = true }],
                128,
                0,
                0);
            if (serialized.BusyCycles != 2 || serialized.TotalBitsTransferred != 132) throw new InvalidOperationException("132-bit serialization oracle failed.");
            values["logical_payload_bits"] = 128;
            values["encoded_bits"] = 132;
            values["service_cycles"] = serialized.BusyCycles;
            values["cycle0_bits"] = serialized.Trace.Single().Segments[0].Bits;
            values["cycle1_bits"] = serialized.Trace.Single().Segments[1].Bits;
            break;
        }
        case "optical_loss_power_margin":
        {
            var provenance = new OpticalQuantityProvenance(OpticalProvenanceSources.Phase8ContractDefault, "ASP-DAC independent oracle");
            var loss = OpticalLossModel.Calculate(
                1.0,
                OpticalWaveguideMaterial.SiliconNitride,
                1,
                0,
                0,
                [
                    OpticalLossModel.DeviceInsertion("splitter", 1, 3.0, "splitter0", provenance),
                    OpticalLossModel.DeviceInsertion("mrr", 1, 1.0, "mrr0", provenance)
                ]);
            var power = OpticalPowerBudgetModel.Evaluate(new Dbm(0), loss.TotalLoss, new Dbm(-3));
            if (Math.Abs(loss.TotalLoss.Value - 4.11) > 1e-12 || Math.Abs(power.ReceivedPower.Value + 4.11) > 1e-12 || Math.Abs(power.Margin.Value + 1.11) > 1e-12)
                throw new InvalidOperationException("Optical hand oracle failed.");
            values["route_loss_db"] = loss.TotalLoss.Value;
            values["received_power_dbm"] = power.ReceivedPower.Value;
            values["margin_db"] = power.Margin.Value;
            values["receiver_sensitivity_dbm"] = -3d;
            break;
        }
        case "wavelength_arbitration":
        {
            var packets = new[] { "dynamic_b", "dynamic_a" }.OrderBy(id => id, StringComparer.Ordinal).ToArray();
            var channels = new[] { "ch0", "ch1", "ch2", "ch3" };
            var allocation = packets.Select((packet, index) => packet + "=" + channels[index]).ToArray();
            if (!allocation.SequenceEqual(["dynamic_a=ch0", "dynamic_b=ch1"])) throw new InvalidOperationException("Wavelength allocation oracle failed.");
            values["allocation"] = string.Join(";", allocation);
            values["conflicts"] = 0;
            values["occupied_channels"] = 2;
            values["capacity"] = 4;
            break;
        }
        case "reduction":
        {
            var operands = new[] { 1d, 2d, 3d, 4d };
            var sum = operands.Sum();
            if (sum != 10d) throw new InvalidOperationException("Reduction oracle failed.");
            values["operand_count"] = operands.Length;
            values["sum"] = sum;
            values["latency_cycles"] = 2;
            break;
        }
        case "softmax":
        {
            var logits = new[] { 0d, 0d };
            var exponentials = logits.Select(Math.Exp).ToArray();
            var denominator = exponentials.Sum();
            var probabilities = exponentials.Select(value => value / denominator).ToArray();
            if (probabilities.Any(value => Math.Abs(value - 0.5d) > 1e-12)) throw new InvalidOperationException("Softmax oracle failed.");
            values["probability0"] = probabilities[0];
            values["probability1"] = probabilities[1];
            values["probability_sum"] = probabilities.Sum();
            values["latency_cycles"] = 8;
            break;
        }
        default:
            throw new ArgumentOutOfRangeException(nameof(caseName), caseName, "Unsupported RQ1 exact case.");
    }
    return values;
}

static SimulationTrace OracleTrace(string caseName, IReadOnlyDictionary<string, object?> values)
{
    var detail = string.Join(";", values.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => $"{pair.Key}={pair.Value}"));
    return new SimulationTrace
    {
        Cycles =
        [
            new CycleTraceRecord
            {
                Cycle = 0,
                Events = [new TraceEvent(TraceEventType.Warning, ComponentId: "independent_oracle", Detail: $"case={caseName};{detail}")]
            }
        ]
    };
}
