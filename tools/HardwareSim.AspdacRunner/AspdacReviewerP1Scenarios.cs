using System.Text.Json;
using HardwareSim.Core;

internal static class AspdacReviewerP1Scenarios
{
    private static readonly HashSet<string> HoldoutParameterAllowlist = new(StringComparer.Ordinal)
    {
        "case_id", "M", "N", "K", "precision_bits", "repeat", "seed"
    };

    private static readonly HashSet<string> NocParameterAllowlist = new(StringComparer.Ordinal)
    {
        "case_id", "repeat", "seed"
    };

    internal static void RunIndependentHoldout(JsonElement root, string outputPath)
    {
        var parameters = root.GetProperty("resolved").GetProperty("parameters");
        ValidateAllowlist(parameters, HoldoutParameterAllowlist);
        var frozen = AspdacReviewerHoldoutRuntime.GetCase(RequiredString(parameters, "case_id"));
        var input = new AspdacReviewerHoldoutInput(
            frozen.CaseId,
            RequiredLong(parameters, "M"),
            RequiredLong(parameters, "N"),
            RequiredLong(parameters, "K"),
            RequiredInt(parameters, "precision_bits"));
        if (input != frozen)
            throw new InvalidDataException($"Resolved shape for '{input.CaseId}' does not match the frozen reviewer hold-out matrix.");

        var repeat = RequiredInt(parameters, "repeat");
        var seed = RequiredInt(parameters, "seed");
        if (repeat is not (0 or 1) || seed != 40)
            throw new InvalidDataException("Reviewer hold-out requires repeat 0 or 1 and seed 40.");
        var result = AspdacReviewerHoldoutRuntime.Run(input);
        WriteOutput(outputPath, new
        {
            status = "completed",
            scenario = "reviewer_holdout_ws",
            completed_utc = DateTimeOffset.UtcNow.ToString("O"),
            candidate_id = OptionalString(root, "candidate_id", $"{input.CaseId}-r{repeat}"),
            config_hash = OptionalString(root, "config_hash", "not_wired"),
            evidence = new
            {
                independence = "Exact",
                repeat_hash = result.RepeatEvidenceLabel,
                timing = result.TimingEvidenceLabel,
                accesses = "Numerical only when counter definitions are paired; otherwise Trend"
            },
            provenance = new
            {
                lowering = "stage-reviewer-holdout-ws/v1",
                input_allowlist = HoldoutParameterAllowlist.OrderBy(value => value, StringComparer.Ordinal).ToArray(),
                input_sha256 = result.InputSha256,
                repeat_excluded_from_identity = true,
                seed_excluded_from_identity = true
            },
            resolved = new
            {
                parameters = new
                {
                    case_id = input.CaseId,
                    input.M,
                    input.N,
                    input.K,
                    precision_bits = input.PrecisionBits,
                    repeat,
                    seed
                },
                architecture = new
                {
                    array_rows = AspdacReviewerHoldoutRuntime.ArrayRows,
                    array_columns = AspdacReviewerHoldoutRuntime.ArrayColumns,
                    macs_per_pe_per_cycle = 1,
                    dataflow = "weight_stationary",
                    memory_words_per_stream_per_cycle = AspdacReviewerHoldoutRuntime.MemoryWordsPerCycle,
                    cold_start_prefetch_words = AspdacReviewerHoldoutRuntime.ColdStartPrefetchWords
                }
            },
            metrics = new
            {
                expected_macs = result.ExpectedMacs,
                row_tiles = result.RowTiles,
                column_tiles = result.ColumnTiles,
                wavefront_cycles = result.WavefrontCycles,
                memory_service_cycles = result.MemoryServiceCycles,
                warm_cycles = result.WarmCycles,
                prefetch_cycles = result.PrefetchCycles,
                total_cycles = result.TotalCycles,
                memory_stall_cycles = result.MemoryStallCycles,
                utilization_percent = result.UtilizationPercent,
                canonical_trace_sha256 = result.CanonicalTraceSha256,
                accesses = new
                {
                    sram_ifmap_reads = result.Accesses.SramIfmapReads,
                    sram_filter_reads = result.Accesses.SramFilterReads,
                    sram_ofmap_writes = result.Accesses.SramOfmapWrites,
                    dram_ifmap_reads = result.Accesses.DramIfmapReads,
                    dram_filter_reads = result.Accesses.DramFilterReads,
                    dram_ofmap_writes = result.Accesses.DramOfmapWrites
                }
            },
            limitations = new[]
            {
                "Timing is Trend evidence because native tiling and memory arbitration are not expected to match another simulator cycle-for-cycle.",
                "Access counts become Numerical evidence only under an explicitly shared counter definition.",
                "This runner consumes only high-level shape fields and the frozen public 4x4 WS contract."
            }
        });
    }

    internal static void RunNocContract(JsonElement root, string outputPath)
    {
        var parameters = root.GetProperty("resolved").GetProperty("parameters");
        ValidateAllowlist(parameters, NocParameterAllowlist);
        var item = AspdacReviewerNocContractRuntime.GetCase(RequiredString(parameters, "case_id"));
        var repeat = RequiredInt(parameters, "repeat");
        var seed = RequiredInt(parameters, "seed");
        if (repeat is not (0 or 1) || seed != 40)
            throw new InvalidDataException("Reviewer NoC contracts require repeat 0 or 1 and seed 40.");

        var result = AspdacReviewerNocContractRuntime.Run(item);
        WriteOutput(outputPath, new
        {
            status = result.Status,
            scenario = "reviewer_noc_contract",
            completed_utc = DateTimeOffset.UtcNow.ToString("O"),
            candidate_id = OptionalString(root, "candidate_id", $"{item.CaseId}-r{repeat}"),
            config_hash = OptionalString(root, "config_hash", "not_wired"),
            evidence = new { contract = result.Status == "not_supported" ? "not_supported" : "Exact", oracle = "Exact artifact", repeat_hash = "Exact", comparison_permission = "contract_only" },
            resolved = new
            {
                parameters = new { case_id = item.CaseId, repeat, seed },
                contract = new
                {
                    item.Scenario,
                    item.PacketBits,
                    item.FlitBits,
                    item.VirtualChannels,
                    item.VcDepthFlits,
                    item.InputCount,
                    selected_virtual_channel = item.SelectedVirtualChannel,
                    downstream_release_cycle = item.DownstreamReleaseCycle
                }
            },
            metrics = new
            {
                completed = result.Completed,
                delivered_all_packets = result.DeliveredAllPackets,
                flits_per_packet = result.FlitsPerPacket,
                oracle_matched = result.OracleMatched,
                canonical_timeline_sha256 = result.CanonicalTimelineSha256,
                oracle_timeline_sha256 = result.OracleTimelineSha256,
                oracle_sha256 = result.Oracle.CanonicalSha256,
                stage_support_reason = result.StageSupportReason,
                packet_moments = result.ObservedMoments.Select(moment => new
                {
                    moment.PacketId,
                    requested_injection_cycle = moment.RequestedInjectionCycle,
                    source_visible_cycle = moment.SourceVisibleCycle,
                    router_tail_arrival_cycle = moment.RouterTailArrivalCycle,
                    router_visible_cycle = moment.RouterVisibleCycle,
                    grant_cycle = moment.GrantCycle,
                    delivery_cycle = moment.DeliveryCycle
                }).ToArray()
            },
            timeline = result.Timeline.Select(entry => new
            {
                entry.Sequence,
                entry.Cycle,
                entry.Phase,
                event_type = entry.EventType,
                packet_id = entry.PacketId,
                flit_id = entry.FlitId,
                flit_index = entry.FlitIndex,
                total_flits = entry.TotalFlits,
                component_id = entry.ComponentId,
                input_port = entry.InputPort,
                virtual_channel = entry.VirtualChannel,
                output_port = entry.OutputPort,
                link_id = entry.LinkId,
                occupancy_before = entry.OccupancyBefore,
                occupancy_after = entry.OccupancyAfter,
                entry.Ready,
                entry.Valid,
                entry.Granted,
                serialization_bits = entry.SerializationBits,
                arrival_cycle = entry.ArrivalCycle,
                tail_complete = entry.TailComplete,
                committed_visible = entry.CommittedVisible,
                entry.Delivered,
                buffer_released = entry.BufferReleased,
                entry.Reason,
                evidence_label = entry.EvidenceLabel
            }).ToArray(),
            oracle_timeline = result.OracleTimeline.Select(entry => new
            {
                entry.Sequence,
                entry.Cycle,
                entry.Phase,
                event_type = entry.EventType,
                packet_id = entry.PacketId,
                flit_id = entry.FlitId,
                flit_index = entry.FlitIndex,
                total_flits = entry.TotalFlits,
                component_id = entry.ComponentId,
                input_port = entry.InputPort,
                virtual_channel = entry.VirtualChannel,
                output_port = entry.OutputPort,
                link_id = entry.LinkId,
                occupancy_before = entry.OccupancyBefore,
                occupancy_after = entry.OccupancyAfter,
                entry.Ready,
                entry.Valid,
                entry.Granted,
                serialization_bits = entry.SerializationBits,
                arrival_cycle = entry.ArrivalCycle,
                tail_complete = entry.TailComplete,
                committed_visible = entry.CommittedVisible,
                entry.Delivered,
                buffer_released = entry.BufferReleased,
                entry.Reason,
                evidence_label = "Independent oracle artifact"
            }).ToArray(),
            feature_boundary = result.FeatureBoundary.Select(boundary => new
            {
                feature_id = boundary.FeatureId,
                feature_group = boundary.FeatureGroup,
                status = boundary.Status,
                modeled_semantics = boundary.ModeledSemantics,
                evidence_label = boundary.EvidenceLabel,
                comparison_permission = boundary.ComparisonPermission
            }).ToArray(),
            limitations = new[]
            {
                "The contract is tail-complete store-and-forward with whole-packet atomic VC admission.",
                "Physical inputs use round-robin; VC selection is explicit or lowest-nonempty priority, not VC round-robin.",
                "No separate RC, VA, SA, crossbar-traversal, or credit-return pipeline stage is modeled."
            }
        });
    }

    private static void ValidateAllowlist(JsonElement parameters, IReadOnlySet<string> allowlist)
    {
        if (parameters.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Resolved parameters must be a JSON object.");
        var unexpected = parameters.EnumerateObject().Select(property => property.Name)
            .Where(name => !allowlist.Contains(name)).OrderBy(name => name, StringComparer.Ordinal).ToArray();
        if (unexpected.Length > 0)
            throw new InvalidDataException($"Unexpected reviewer parameter(s): {string.Join(", ", unexpected)}.");
    }

    private static string RequiredString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()!
            : throw new InvalidDataException($"Missing string parameter '{name}'.");
    private static int RequiredInt(JsonElement element, string name) =>
        element.TryGetProperty(name, out var property) && property.TryGetInt32(out var value)
            ? value
            : throw new InvalidDataException($"Missing integer parameter '{name}'.");
    private static long RequiredLong(JsonElement element, string name) =>
        element.TryGetProperty(name, out var property) && property.TryGetInt64(out var value)
            ? value
            : throw new InvalidDataException($"Missing integer parameter '{name}'.");
    private static string OptionalString(JsonElement element, string name, string fallback) =>
        element.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString() ?? fallback
            : fallback;

    private static void WriteOutput<T>(string outputPath, T payload)
    {
        var destination = Path.GetFullPath(outputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.WriteAllText(destination, JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine);
    }
}
