using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using HardwareSim.Core;

internal static class AspdacReviewerScalingScenarios
{
    internal const string ScenarioName = "reviewer_scalability";

    internal static void Run(JsonElement root, string outputPath)
    {
        using var process = Process.GetCurrentProcess();
        var processStartupSeconds = Math.Max(
            0d,
            (DateTime.UtcNow - process.StartTime.ToUniversalTime()).TotalSeconds);
        var parameters = root.GetProperty("resolved").GetProperty("parameters");
        var meshDimension = RequiredInt(parameters, "mesh_dimension");
        var packetCount = RequiredLong(parameters, "packet_count");
        var seed = RequiredInt(parameters, "seed");
        var repeat = OptionalInt(parameters, "repeat", 0);
        var traceModeText = RequiredString(parameters, "trace_mode");
        var traceMode = ParseTraceMode(traceModeText);
        var injectionRate = OptionalDouble(parameters, "injection_rate", 0.02d);
        var inputBufferDepth = OptionalInt(parameters, "input_buffer_depth", 16);
        var maxCycles = OptionalLong(parameters, "max_cycles");
        var compressTrace = OptionalBool(parameters, "compress_trace", true);

        var destination = Path.GetFullPath(outputPath);
        var resultDirectory = Path.GetDirectoryName(destination)
            ?? throw new InvalidOperationException("Reviewer scaling output path has no parent directory.");
        var traceDirectory = traceMode == AspdacReviewerTraceMode.FullProvenance
            ? Path.Combine(resultDirectory, Path.GetFileNameWithoutExtension(destination) + ".trace")
            : null;

        var result = AspdacReviewerScalingRuntime.Run(new AspdacReviewerScalingOptions(
            meshDimension,
            packetCount,
            seed,
            traceMode,
            traceDirectory,
            injectionRate,
            inputBufferDepth,
            maxCycles,
            compressTrace));

        var simulationSeconds = result.Timings.SimulationWallSeconds;
        var payload = new
        {
            status = result.Completed ? "completed" : "timeout",
            reason = result.CompletionReason,
            completed_utc = DateTimeOffset.UtcNow.ToString("O"),
            measurement_kind = "stage_reviewer_scaling_runtime",
            scenario = ScenarioName,
            candidate_id = OptionalString(root, "candidate_id"),
            config_hash = OptionalString(root, "config_hash"),
            parameters = new
            {
                mesh_dimension = meshDimension,
                packet_count = packetCount,
                seed,
                repeat,
                trace_mode = traceModeText,
                injection_rate = injectionRate,
                input_buffer_depth = inputBufferDepth,
                max_cycles = maxCycles,
                compress_trace = compressTrace
            },
            metrics = new
            {
                graph_build_seconds = result.Timings.GraphBuildSeconds,
                graph_validation_seconds = result.Timings.GraphValidationSeconds,
                compile_seconds = result.Timings.CompileSeconds,
                simulation_wall_seconds = result.Timings.SimulationWallSeconds,
                simulation_core_seconds = result.Timings.SimulationCoreSeconds,
                trace_persist_seconds = result.Timings.TracePersistSeconds,
                trace_compression_seconds = result.Timings.TraceCompressionSeconds,
                scenario_wall_seconds = result.Timings.ScenarioWallSeconds,
                process_startup_seconds = processStartupSeconds,
                process_startup_measurement = "process_start_to_scaling_scenario_entry",
                simulated_cycles = result.SimulatedCycles,
                simulated_cycles_per_second = Rate(result.SimulatedCycles, simulationSeconds),
                completed_packets_per_second = Rate(result.CompletedPackets, simulationSeconds),
                events_per_second = Rate(result.EventCount, simulationSeconds),
                requested_packets = result.RequestedPackets,
                injected_packets = result.InjectedPackets,
                completed_packets = result.CompletedPackets,
                event_count = result.EventCount,
                router_conflict_events = result.RouterConflictEvents,
                backpressure_events = result.BackpressureEvents,
                injection_blocked_events = result.InjectionBlockedEvents,
                peak_working_set_bytes = (long?)null,
                peak_working_set_measurement = "external_manager_required",
                in_process_peak_working_set_bytes_advisory = result.PeakWorkingSetBytes,
                peak_managed_bytes = result.PeakManagedBytes,
                raw_trace_bytes = result.RawTraceBytes,
                compressed_trace_bytes = result.CompressedTraceBytes,
                bytes_per_event = result.EventCount == 0 ? 0d : result.RawTraceBytes / (double)result.EventCount,
                compiled_component_count = result.CompiledComponentCount,
                compiled_link_count = result.CompiledLinkCount,
                graph_validation_issue_count = result.GraphValidationIssueCount
            },
            hashes = new
            {
                topology_canonical_sha256 = result.TopologyCanonicalHash,
                compiled_source_graph_sha256 = result.CompiledSourceGraphHash,
                canonical_delivery_sha256 = result.CanonicalDeliveryHash,
                raw_trace_sha256 = result.RawTraceSha256,
                compressed_trace_sha256 = result.CompressedTraceSha256
            },
            artifacts = new
            {
                raw_trace_path = result.RawTracePath,
                compressed_trace_path = result.CompressedTracePath
            },
            provenance = new
            {
                runtime = "HardwareSim.Core.AspdacReviewerScalingRuntime",
                runtime_version = typeof(AspdacReviewerScalingRuntime).Assembly.GetName().Version?.ToString() ?? "unknown",
                runner_version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown",
                evidence_scope = "specialized_compiled_graph_packet_cycle_scaling_path",
                consumes_compiled_graph = true,
                traffic = "uniform_random_exact_packet_count",
                routing = "deterministic_xy",
                packet_bits = 128,
                flits_per_packet = 1,
                virtual_channels = 1,
                arbitration = "deterministic_per_output_round_robin",
                graph_builder = Flat2DMeshTopologyPresetBuilder.BuilderId,
                graph_builder_version = Flat2DMeshTopologyPresetBuilder.BuilderVersion,
                os = RuntimeInformation.OSDescription,
                architecture = RuntimeInformation.OSArchitecture.ToString(),
                process_architecture = RuntimeInformation.ProcessArchitecture.ToString(),
                dotnet_runtime = RuntimeInformation.FrameworkDescription,
                cpu = Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER") ?? "unknown",
                available_managed_memory_bytes = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes,
                command = Environment.CommandLine,
                git_commit = OptionalString(root, "git_commit")
            },
            limitations = new[]
            {
                "Process startup and peak child working set are intentionally not inferred in-process; the config-driven parent manager must measure both around the child process.",
                "The runtime consumes a compiled production flat-mesh graph but is a specialized packet-cycle scaling path, not a throughput claim for every STAGE DSE engine or workload lowering path.",
                "Every generated physical route is bound to the compiler explicit-topology-transport contract so bidirectional mesh edges are stateful packet routes rather than combinational dependencies.",
                "The topology builder has no WorkloadSource/WorkloadSink because this specialized exact-count path injects at compiled routers; those two simulation-readiness diagnostics are recorded but are the only validation exemptions.",
                "This experiment measures simulator execution cost; it does not establish prediction accuracy or parity with a specialist simulator."
            }
        };

        Directory.CreateDirectory(resultDirectory);
        File.WriteAllText(
            destination,
            JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine);
    }

    private static AspdacReviewerTraceMode ParseTraceMode(string value) => value.Trim().ToLowerInvariant() switch
    {
        "metrics_only" or "metrics-only" or "off" => AspdacReviewerTraceMode.MetricsOnly,
        "full" or "full_provenance" or "full-provenance" => AspdacReviewerTraceMode.FullProvenance,
        _ => throw new InvalidDataException($"Unsupported reviewer trace mode '{value}'.")
    };

    private static double Rate(long count, double seconds) => seconds <= 0d ? 0d : count / seconds;

    private static string RequiredString(JsonElement parameters, string name) =>
        parameters.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()!
            : throw new InvalidDataException($"Reviewer scaling parameter '{name}' must be a non-empty string.");

    private static int RequiredInt(JsonElement parameters, string name) =>
        parameters.TryGetProperty(name, out var value) && value.TryGetInt32(out var parsed)
            ? parsed
            : throw new InvalidDataException($"Reviewer scaling parameter '{name}' must be an Int32.");

    private static long RequiredLong(JsonElement parameters, string name) =>
        parameters.TryGetProperty(name, out var value) && value.TryGetInt64(out var parsed)
            ? parsed
            : throw new InvalidDataException($"Reviewer scaling parameter '{name}' must be an Int64.");

    private static int OptionalInt(JsonElement parameters, string name, int fallback) =>
        parameters.TryGetProperty(name, out var value) && value.TryGetInt32(out var parsed) ? parsed : fallback;

    private static long? OptionalLong(JsonElement parameters, string name) =>
        parameters.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null && value.TryGetInt64(out var parsed)
            ? parsed
            : null;

    private static double OptionalDouble(JsonElement parameters, string name, double fallback) =>
        parameters.TryGetProperty(name, out var value) && value.TryGetDouble(out var parsed) ? parsed : fallback;

    private static bool OptionalBool(JsonElement parameters, string name, bool fallback) =>
        parameters.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : fallback;

    private static string? OptionalString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
