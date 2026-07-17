using System.Collections.ObjectModel;

namespace HardwareSim.Core;

/// <summary>Stable Phase 8A Unity workbench view identities.</summary>
public static class Phase8AUnityWorkbenchViewIds
{
    /// <summary>Cluster hierarchy view.</summary>
    public const string Clusters = "clusters";
    /// <summary>Weight placement and residency view.</summary>
    public const string Weights = "weights";
    /// <summary>Activation multicast view.</summary>
    public const string Multicast = "multicast";
    /// <summary>Partial-sum collective view.</summary>
    public const string PartialSums = "partial_sums";
    /// <summary>Offset assembly view.</summary>
    public const string Assembly = "assembly";
    /// <summary>Layer and tile assignment view.</summary>
    public const string Layers = "layers";
    /// <summary>Manual override diff view.</summary>
    public const string Overrides = "overrides";
    /// <summary>Phase 8A runtime replay view.</summary>
    public const string Replay = "replay";
}

/// <summary>Stable workload choices exposed by the Phase 8A Core workbench.</summary>
public static class Phase8AUnityWorkbenchWorkloadIds
{
    /// <summary>Canonical numeric 1x8 to 1x16 to 1x4 MLP.</summary>
    public const string CanonicalMlp = "phase8a.mlp.1x8x16x4";
    /// <summary>Scaled numeric 1x64 to 1x128 to 1x32 MLP.</summary>
    public const string ScaledMlp = "phase8a.mlp.1x64x128x32";
    /// <summary>Paper-profile QKV projection tile scenario.</summary>
    public const string PaperQkvProjection = "phase8a.paper.qkv-projection-tile";
}

/// <summary>One Core-owned workload selector entry for Unity.</summary>
public sealed record Phase8AUnityWorkbenchWorkloadChoice(string WorkloadId, string DisplayName);

/// <summary>Core-projected candidate score row for Unity.</summary>
public sealed record Phase8AUnityCandidateScoreRow(
    string CandidateId,
    string PolicyId,
    decimal TotalScore,
    int IssueCount,
    bool Selected,
    string TopologyHash,
    string RouteHash,
    IReadOnlyList<MappingScoreItem> ScoreBreakdown);

/// <summary>Core-projected hierarchy, mapping, collective, or replay detail row.</summary>
public sealed record Phase8AUnityWorkbenchDetailRow(
    string CategoryId,
    string PrimaryId,
    string Summary,
    string Provenance);

/// <summary>Immutable Unity-facing Phase 8A workbench state.</summary>
public sealed class Phase8AUnityWorkbenchSnapshot
{
    internal Phase8AUnityWorkbenchSnapshot(
        string topologyId,
        string topologyHash,
        string resolvedMappingHash,
        string dcLayoutHash,
        string activationTreeHash,
        string reductionPlanHash,
        string selectedWorkloadId,
        int deterministicSeed,
        string selectedCandidateId,
        string selectedViewId,
        string decisionStatus,
        string statusMessage,
        IReadOnlyList<Phase8AUnityCandidateScoreRow> candidates,
        IReadOnlyList<Phase8AUnityWorkbenchDetailRow> details)
    {
        TopologyId = topologyId;
        TopologyHash = topologyHash;
        ResolvedMappingHash = resolvedMappingHash;
        DcLayoutHash = dcLayoutHash;
        ActivationTreeHash = activationTreeHash;
        ReductionPlanHash = reductionPlanHash;
        SelectedWorkloadId = selectedWorkloadId;
        DeterministicSeed = deterministicSeed;
        SelectedCandidateId = selectedCandidateId;
        SelectedViewId = selectedViewId;
        DecisionStatus = decisionStatus;
        StatusMessage = statusMessage;
        Candidates = candidates;
        Details = details;
    }

    /// <summary>Gets the selected topology preset id.</summary>
    public string TopologyId { get; }
    /// <summary>Gets the exact topology manifest hash.</summary>
    public string TopologyHash { get; }
    /// <summary>Gets the single resolved mapping authority hash loaded by the scenario path.</summary>
    public string ResolvedMappingHash { get; }
    /// <summary>Gets the resolved D/C layout authority hash.</summary>
    public string DcLayoutHash { get; }
    /// <summary>Gets the shared-prefix activation-tree authority hash.</summary>
    public string ActivationTreeHash { get; }
    /// <summary>Gets the hierarchical reduction and assembly authority hash.</summary>
    public string ReductionPlanHash { get; }
    /// <summary>Gets the selected stable Core workload id.</summary>
    public string SelectedWorkloadId { get; }
    /// <summary>Gets the selected deterministic mapping/simulation seed.</summary>
    public int DeterministicSeed { get; }
    /// <summary>Gets the selected immutable candidate id.</summary>
    public string SelectedCandidateId { get; }
    /// <summary>Gets the selected detail view id.</summary>
    public string SelectedViewId { get; }
    /// <summary>Gets the latest proposal decision status.</summary>
    public string DecisionStatus { get; }
    /// <summary>Gets a user-facing status derived from Core results.</summary>
    public string StatusMessage { get; }
    /// <summary>Gets candidate score rows in deterministic rank order.</summary>
    public IReadOnlyList<Phase8AUnityCandidateScoreRow> Candidates { get; }
    /// <summary>Gets rows for the selected detail view.</summary>
    public IReadOnlyList<Phase8AUnityWorkbenchDetailRow> Details { get; }
}

/// <summary>
/// Stateful Core adapter for the Phase 8A Unity workbench. It projects existing topology,
/// mapping, proposal, and replay contracts; Unity owns no parallel mapping truth.
/// </summary>
public sealed class Phase8AUnityWorkbenchAdapter
{
    private static readonly IReadOnlyList<Phase8AUnityWorkbenchWorkloadChoice> WorkloadChoices =
        new ReadOnlyCollection<Phase8AUnityWorkbenchWorkloadChoice>(
        [
            new(Phase8AUnityWorkbenchWorkloadIds.CanonicalMlp, "Canonical MLP 1×8 → 1×16 → 1×4"),
            new(Phase8AUnityWorkbenchWorkloadIds.ScaledMlp, "Scaled MLP 1×64 → 1×128 → 1×32"),
            new(Phase8AUnityWorkbenchWorkloadIds.PaperQkvProjection, "Paper QKV projection tile")
        ]);
    private readonly TopologyPresetBuilderRegistry registry;
    private TopologyManifest? topology;
    private WorkloadMappingV2? mapping;
    private Phase8ADcLayoutPlan? dcLayout;
    private Phase8AActivationTreePlan? activationTree;
    private Phase8AHierarchicalReductionPlan? hierarchicalReduction;
    private string resolvedMappingHash = "";
    private IReadOnlyList<MappingCandidate> candidates = [];
    private string selectedCandidateId = "";
    private Phase8AMappingProposal? proposal;
    private Phase8AProposalApplyContext? applyContext;
    private IReadOnlyList<(long Cycle, TraceEvent Event)> replayEvents = [];
    private string selectedWorkloadId = Phase8AUnityWorkbenchWorkloadIds.CanonicalMlp;
    private int deterministicSeed = 808;
    private string decisionStatus = "none";
    private string statusMessage = "No topology preview loaded.";

    /// <summary>Creates an adapter using the registered first-party topology builders.</summary>
    public Phase8AUnityWorkbenchAdapter(TopologyPresetBuilderRegistry? registry = null) =>
        this.registry = registry ?? TopologyPresetBuilderRegistry.CreateDefault();

    /// <summary>Gets topology ids from the Core registry rather than a Unity switch.</summary>
    public IReadOnlyList<string> TopologyIds => registry.TopologyIds;

    /// <summary>Gets immutable workload selector entries from Core.</summary>
    public IReadOnlyList<Phase8AUnityWorkbenchWorkloadChoice> Workloads => WorkloadChoices;

    /// <summary>Selects a stable Core workload identity and explicit deterministic seed.</summary>
    public bool SelectWorkload(string workloadId, int seed)
    {
        var normalized = workloadId?.Trim() ?? "";
        if (WorkloadChoices.All(choice => !string.Equals(choice.WorkloadId, normalized, StringComparison.Ordinal))) return false;
        selectedWorkloadId = normalized;
        deterministicSeed = seed;
        statusMessage = $"Workload selected: {selectedWorkloadId}; seed={deterministicSeed}. Load or compile its Mapping 2.x authority to populate candidates and mapping views.";
        return true;
    }

    /// <summary>Builds a topology preview through the shared Core preset registry.</summary>
    public TopologyBuildResult PreviewTopology(TopologyPresetRequest request)
    {
        var result = registry.Build(request);
        topology = result.TopologyManifest;
        statusMessage = result.IsSuccess && topology is not null
            ? $"Topology ready: components={topology.Components.Count}, links={topology.Links.Count}, hash={Short(topology.CanonicalHash)}; workload={selectedWorkloadId}; seed={deterministicSeed}."
            : "Topology preview failed: " + string.Join("; ", result.Issues.Select(issue => issue.Code + ": " + issue.Message));
        return result;
    }

    /// <summary>Loads a verified Mapping 2.x document for all mapping detail views.</summary>
    public void LoadMapping(WorkloadMappingV2 value)
    {
        if (value is null) throw new ArgumentNullException(nameof(value));
        WorkloadMappingV2ImportResult imported;
        try
        {
            imported = WorkloadMappingV2.FromJson(value.ToJson());
        }
        catch (System.Text.Json.JsonException exception)
        {
            throw new ArgumentException("The mapping is not a valid topology-aware WorkloadMappingV2 authority.", nameof(value), exception);
        }
        if (!imported.CanCompileTopologyAware || imported.Mapping is null)
            throw new ArgumentException("The mapping is not a verified topology-aware WorkloadMappingV2 authority: " +
                string.Join("; ", imported.Issues.Select(issue => issue.Code + ": " + issue.Message)), nameof(value));
        mapping = imported.Mapping;
        dcLayout = null;
        activationTree = null;
        hierarchicalReduction = null;
        resolvedMappingHash = "";
        LoadCandidates([mapping.Candidate], mapping.Candidate.CandidateId);
        statusMessage = $"Mapping loaded: assignments={mapping.OperationTileAssignments.Count}, flows={mapping.CommunicationFlows.Count}, collectives={mapping.CollectivePlans.Count}.";
    }

    /// <summary>Loads one complete generated scenario without recomputing D/C relationships in Unity.</summary>
    public void LoadScenario(Phase8AMatMulScenarioBundle value)
    {
        if (value is null) throw new ArgumentNullException(nameof(value));
        if (value.DcLayout.CanonicalHash != value.ActivationTree.LayoutHash ||
            value.DcLayout.CanonicalHash != value.HierarchicalReduction.LayoutHash ||
            value.TopologyAuthorityGraph.Parameters.GetValueOrDefault("topology_manifest_hash", value.ActivationTree.TopologyManifestHash) !=
                value.ActivationTree.TopologyManifestHash ||
            value.ActivationTree.TopologyManifestHash != value.HierarchicalReduction.TopologyManifestHash)
            throw new ArgumentException("The scenario contains stale or mismatched D/C authorities.", nameof(value));
        LoadMapping(value.Mapping);
        dcLayout = value.DcLayout;
        activationTree = value.ActivationTree;
        hierarchicalReduction = value.HierarchicalReduction;
        resolvedMappingHash = value.ResolvedMappingHash;
        deterministicSeed = value.Request.Seed;
        selectedWorkloadId = $"phase8a.matmul.1x{value.Request.K}x{value.Request.N}";
        statusMessage = $"Resolved scenario loaded: D={value.Request.WeightRowDivisionSize}, C={value.Request.ClusterSize}, layout={Short(dcLayout.CanonicalHash)}, activation={Short(activationTree.CanonicalHash)}, reduction={Short(hierarchicalReduction.CanonicalHash)}.";
    }

    /// <summary>Loads immutable Core candidate descriptors and an optional selected id.</summary>
    public void LoadCandidates(IEnumerable<MappingCandidate>? values, string? selectedId = null)
    {
        var materialized = (values ?? []).ToArray();
        if (materialized.Any(value => value is null) ||
            materialized.Any(value => string.IsNullOrWhiteSpace(value.CandidateId)) ||
            materialized.Select(value => value.CandidateId).Distinct(StringComparer.Ordinal).Count() != materialized.Length)
            throw new ArgumentException("Candidate rows must be non-null and have unique non-empty stable ids.", nameof(values));
        try
        {
            candidates = new ReadOnlyCollection<MappingCandidate>(materialized
                .OrderBy(TotalScore)
                .ThenBy(value => value.TieBreakKey, StringComparer.Ordinal)
                .ThenBy(value => value.CandidateId, StringComparer.Ordinal)
                .ToArray());
        }
        catch (OverflowException exception)
        {
            throw new ArgumentException("Candidate weighted scores exceed supported decimal bounds.", nameof(values), exception);
        }
        selectedCandidateId = selectedId?.Trim() ?? "";
        if (selectedCandidateId.Length == 0 || candidates.All(value => value.CandidateId != selectedCandidateId))
            selectedCandidateId = candidates.FirstOrDefault()?.CandidateId ?? "";
    }

    /// <summary>Selects one loaded candidate by stable id without mutating mapping authorities.</summary>
    public bool SelectCandidate(string candidateId)
    {
        var normalized = candidateId?.Trim() ?? "";
        if (candidates.All(value => value.CandidateId != normalized)) return false;
        selectedCandidateId = normalized;
        statusMessage = "Candidate selected: " + normalized;
        return true;
    }

    /// <summary>Loads one real proposal and its optional current-authority apply context.</summary>
    public void LoadProposal(Phase8AMappingProposal value, Phase8AProposalApplyContext? currentContext = null)
    {
        proposal = value ?? throw new ArgumentNullException(nameof(value));
        applyContext = currentContext;
        mapping = proposal.ProposedMapping;
        LoadCandidates([proposal.SourceCandidate.Descriptor], proposal.SourceCandidate.Descriptor.CandidateId);
        decisionStatus = "pending";
        statusMessage = currentContext is null
            ? "Proposal loaded read-only; current apply authority is not loaded."
            : "Proposal ready for strict Apply/Cancel decision.";
    }

    /// <summary>Strictly applies the loaded proposal through the existing Core applier.</summary>
    public Phase8AMappingProposalDecision? ApplyProposal()
    {
        if (proposal is null || applyContext is null)
        {
            decisionStatus = "rejected";
            statusMessage = "Apply rejected: a real proposal and current authority context are required.";
            return null;
        }
        if (proposal.SourceCandidate.Descriptor.CandidateId != selectedCandidateId)
        {
            decisionStatus = "rejected";
            statusMessage = "Apply rejected: the selected candidate is not the loaded proposal candidate.";
            return null;
        }
        var decision = Phase8AMappingProposalApplier.Apply(proposal, applyContext);
        decisionStatus = decision.DecisionId;
        statusMessage = decision.IsApplied
            ? "Proposal applied; mapping/simulation/trace are invalidated by Core."
            : "Proposal rejected: " + string.Join("; ", decision.Issues.Select(issue => issue.Code + ": " + issue.Message));
        if (decision.Mapping is not null) mapping = decision.Mapping;
        return decision;
    }

    /// <summary>Cancels the loaded proposal through the existing no-mutation Core contract.</summary>
    public Phase8AMappingProposalDecision? CancelProposal()
    {
        if (proposal is null)
        {
            decisionStatus = "none";
            statusMessage = "Cancel ignored: no proposal is loaded.";
            return null;
        }
        var decision = Phase8AMappingProposalApplier.Cancel(proposal);
        decisionStatus = decision.DecisionId;
        statusMessage = "Proposal cancelled without changing project authority.";
        return decision;
    }

    /// <summary>Loads completed simulation trace events for Phase 8A replay inspection.</summary>
    public void LoadReplay(SimulationResult result)
    {
        if (result is null) throw new ArgumentNullException(nameof(result));
        if (!result.Completed) throw new InvalidOperationException("Completed simulation evidence is required for Phase 8A replay.");
        replayEvents = new ReadOnlyCollection<(long, TraceEvent)>(result.Trace.Cycles
            .SelectMany(cycle => cycle.Events.Select(trace => (cycle.Cycle, trace)))
            .Where(item => IsPhase8AReplayEvent(item.trace))
            .OrderBy(item => item.Cycle).ThenBy(item => item.trace.ComponentId, StringComparer.Ordinal)
            .ThenBy(item => item.trace.PacketId, StringComparer.Ordinal).ToArray());
        statusMessage = $"Replay loaded: Phase 8A events={replayEvents.Count}, trace={Short(result.TraceHash?.Hash ?? "")}.";
    }

    /// <summary>Projects the selected Core-owned view.</summary>
    public Phase8AUnityWorkbenchSnapshot Snapshot(string selectedViewId)
    {
        var view = NormalizeView(selectedViewId);
        return new Phase8AUnityWorkbenchSnapshot(
            topology?.Request.TopologyId ?? (dcLayout is null ? "" : ReferenceMappingTopologyIds.MeshOfTreesV1),
            topology?.CanonicalHash ?? activationTree?.TopologyManifestHash ?? "",
            resolvedMappingHash,
            dcLayout?.CanonicalHash ?? "",
            activationTree?.CanonicalHash ?? "",
            hierarchicalReduction?.CanonicalHash ?? "",
            selectedWorkloadId,
            deterministicSeed,
            selectedCandidateId,
            view,
            decisionStatus,
            statusMessage,
            CandidateRows(),
            Details(view));
    }

    private IReadOnlyList<Phase8AUnityCandidateScoreRow> CandidateRows() =>
        new ReadOnlyCollection<Phase8AUnityCandidateScoreRow>(candidates.Select(candidate => new Phase8AUnityCandidateScoreRow(
            candidate.CandidateId,
            candidate.PolicyId,
            TotalScore(candidate),
            candidate.Issues.Count,
            candidate.CandidateId == selectedCandidateId,
            candidate.TopologyHash,
            candidate.RouteHash,
            candidate.ScoreBreakdown)).ToArray());

    private IReadOnlyList<Phase8AUnityWorkbenchDetailRow> Details(string view) => new ReadOnlyCollection<Phase8AUnityWorkbenchDetailRow>((view switch
    {
        Phase8AUnityWorkbenchViewIds.Clusters => ClusterRows(),
        Phase8AUnityWorkbenchViewIds.Weights => WeightRows(),
        Phase8AUnityWorkbenchViewIds.Multicast => MulticastRows(),
        Phase8AUnityWorkbenchViewIds.PartialSums => PartialSumRows(),
        Phase8AUnityWorkbenchViewIds.Assembly => AssemblyRows(),
        Phase8AUnityWorkbenchViewIds.Layers => LayerRows(),
        Phase8AUnityWorkbenchViewIds.Overrides => OverrideRows(),
        Phase8AUnityWorkbenchViewIds.Replay => ReplayRows(),
        _ => []
    }).ToArray());

    private IEnumerable<Phase8AUnityWorkbenchDetailRow> ClusterRows()
    {
        if (dcLayout is not null)
        {
            return dcLayout.ClusterOccupancies.Select(cluster => new Phase8AUnityWorkbenchDetailRow(
                Phase8AUnityWorkbenchViewIds.Clusters,
                "cluster-" + cluster.ClusterIndex,
                $"PE used={cluster.AssignmentIds.Count}/{dcLayout.Request.ClusterSize}; activations={dcLayout.ActivationDeliveries.Count(item => item.ClusterIndex == cluster.ClusterIndex)}; local groups={dcLayout.LocalReductionGroups.Count(item => item.ClusterIndex == cluster.ClusterIndex)}",
                $"layout={Short(dcLayout.CanonicalHash)}; occupancy={cluster.UtilizationBitset}"));
        }
        return topology?.Components
            .Where(component => component.ClusterIndex.HasValue)
            .GroupBy(component => component.ClusterIndex!.Value).OrderBy(group => group.Key)
            .Select(group => new Phase8AUnityWorkbenchDetailRow(
                Phase8AUnityWorkbenchViewIds.Clusters,
                "cluster-" + group.Key,
                string.Join(", ", group.GroupBy(component => component.Role).OrderBy(item => item.Key)
                    .Select(item => item.Key + "=" + item.Count())),
                "topology=" + Short(topology!.CanonicalHash))) ?? [];
    }

    private IEnumerable<Phase8AUnityWorkbenchDetailRow> WeightRows() => mapping?.OperandPlacements
        .Where(item => item.OperandRoleId == Phase8ATensorRoleIds.Weight)
        .Select(item => new Phase8AUnityWorkbenchDetailRow(
            Phase8AUnityWorkbenchViewIds.Weights, item.PlacementId,
            $"{item.TileId} -> {item.StorageComponentId}/{item.StorageLevelId} @ {item.AddressBits} bits; size={item.SizeBits}",
            $"residency={item.ResidencyModeId}; load={item.LoadModeId}; commit={item.CommitRequired}")) ?? [];

    private IEnumerable<Phase8AUnityWorkbenchDetailRow> MulticastRows()
    {
        if (activationTree is not null)
        {
            return activationTree.Trees.Select(tree => new Phase8AUnityWorkbenchDetailRow(
                Phase8AUnityWorkbenchViewIds.Multicast,
                tree.TreeId,
                $"{tree.ActivationTileId}: clusters={tree.Clusters.Count}; targets={tree.Targets.Count}; branches={tree.BranchPoints.Count}; edges={tree.Edges.Count}",
                $"activation={Short(activationTree.CanonicalHash)}; bits={tree.Bits}"));
        }
        return mapping?.CommunicationFlows
            .Where(item => item.FlowKindId is Phase8ACommunicationFlowKinds.Multicast or Phase8ACommunicationFlowKinds.Broadcast)
            .Select(item => new Phase8AUnityWorkbenchDetailRow(
                Phase8AUnityWorkbenchViewIds.Multicast, item.FlowId,
                $"{item.ProducerComponentId} -> {string.Join(",", item.ConsumerComponentIds)}; bits={item.Bits}; branches={item.BranchComponentIds.Count}",
                "routes=" + string.Join(",", item.ConsumerRoutes.Select(route => route.RoutePathId)))) ?? [];
    }

    private IEnumerable<Phase8AUnityWorkbenchDetailRow> PartialSumRows()
    {
        if (hierarchicalReduction is not null)
        {
            var local = hierarchicalReduction.LocalGroups.Select(item => new Phase8AUnityWorkbenchDetailRow(
                Phase8AUnityWorkbenchViewIds.PartialSums,
                item.GroupId,
                $"local {item.Mode}: contributors={item.Contributors.Count}; adds={item.AddOperationCount}; target={item.TargetComponentId}",
                $"group={item.GroupKey}; reduction={Short(hierarchicalReduction.CanonicalHash)}"));
            var global = hierarchicalReduction.GlobalGroups.Select(item => new Phase8AUnityWorkbenchDetailRow(
                Phase8AUnityWorkbenchViewIds.PartialSums,
                item.GroupId,
                $"mesh {item.Mode}: contributors={item.Contributors.Count}; adds={item.AddOperationCount}; target={item.CollectionMeshRouterComponentId}",
                $"group={item.GroupKey}; reduction={Short(hierarchicalReduction.CanonicalHash)}"));
            return local.Concat(global);
        }
        return mapping?.CollectivePlans
            .Where(item => item.CollectiveKindId == Phase8ACollectiveIntentKinds.Sum)
            .Select(item => new Phase8AUnityWorkbenchDetailRow(
                Phase8AUnityWorkbenchViewIds.PartialSums,
                item.CollectiveId,
                $"{item.CollectiveKindId}: contributors={item.ContributorIds.Count} -> {item.TargetComponentId}/{item.OutputTileId}",
                $"group={item.GroupKey}; order={item.OrderPolicyId}")) ?? [];
    }

    private IEnumerable<Phase8AUnityWorkbenchDetailRow> AssemblyRows()
    {
        if (hierarchicalReduction is not null)
        {
            return hierarchicalReduction.FinalAssembly.Shards.Select(item => new Phase8AUnityWorkbenchDetailRow(
                Phase8AUnityWorkbenchViewIds.Assembly,
                item.ShardId,
                $"N[{item.NRange.Offset}..{item.NRange.Offset + item.NRange.Extent}) <- {item.SourceResultId}",
                $"assembly={hierarchicalReduction.FinalAssembly.AssemblyRequirementId}; reduction={Short(hierarchicalReduction.CanonicalHash)}"));
        }
        return mapping?.CollectivePlans
            .Where(item => item.CollectiveKindId == Phase8ACollectiveIntentKinds.Concat)
            .Select(item => new Phase8AUnityWorkbenchDetailRow(
                Phase8AUnityWorkbenchViewIds.Assembly,
                item.CollectiveId,
                $"{item.CollectiveKindId}: contributors={item.ContributorIds.Count} -> {item.TargetComponentId}/{item.OutputTileId}",
                $"group={item.GroupKey}; order={item.OrderPolicyId}")) ?? [];
    }

    private IEnumerable<Phase8AUnityWorkbenchDetailRow> LayerRows() => mapping?.OperationTileAssignments
        .GroupBy(item => item.OperationId, StringComparer.Ordinal).OrderBy(group => group.Key, StringComparer.Ordinal)
        .Select(group => new Phase8AUnityWorkbenchDetailRow(
            Phase8AUnityWorkbenchViewIds.Layers, group.Key,
            $"tiles={group.Count()}; targets={string.Join(",", group.Select(item => item.TargetComponentId).Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal))}",
            "partitions=" + string.Join(",", group.Select(item => item.PartitionPolicyId).Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal)))) ?? [];

    private IEnumerable<Phase8AUnityWorkbenchDetailRow> OverrideRows() => (proposal?.ManualDiff ?? mapping?.Candidate.ManualDiff ?? [])
        .Select(item => new Phase8AUnityWorkbenchDetailRow(
            Phase8AUnityWorkbenchViewIds.Overrides, item.Path,
            item.BeforeValue + " -> " + item.AfterValue,
            "reason=" + item.ReasonCode));

    private IEnumerable<Phase8AUnityWorkbenchDetailRow> ReplayRows() => replayEvents.Select(item =>
        new Phase8AUnityWorkbenchDetailRow(
            Phase8AUnityWorkbenchViewIds.Replay,
            item.Event.PacketId ?? item.Event.ComponentId ?? "cycle-" + item.Cycle,
            $"cycle={item.Cycle}; component={item.Event.ComponentId}; event={item.Event.Type}",
            item.Event.Detail ?? ""));

    private static bool IsPhase8AReplayEvent(TraceEvent item)
    {
        var detail = item.Detail ?? "";
        return detail.Contains("multicast", StringComparison.OrdinalIgnoreCase) ||
            detail.Contains("grouped_vector_sum", StringComparison.OrdinalIgnoreCase) ||
            detail.Contains("tensor_assembly", StringComparison.OrdinalIgnoreCase) ||
            detail.Contains("bias_add", StringComparison.OrdinalIgnoreCase) ||
            detail.Contains("relu", StringComparison.OrdinalIgnoreCase) ||
            detail.Contains("kernel=core.digital.vmm", StringComparison.OrdinalIgnoreCase);
    }

    private static decimal TotalScore(MappingCandidate candidate) => candidate.ScoreBreakdown.Aggregate(0m, (sum, item) => checked(sum + item.WeightedValue));
    private static string Short(string value) => string.IsNullOrWhiteSpace(value) ? "--" : value[..Math.Min(12, value.Length)];
    private static string NormalizeView(string value) => value switch
    {
        Phase8AUnityWorkbenchViewIds.Weights => value,
        Phase8AUnityWorkbenchViewIds.Multicast => value,
        Phase8AUnityWorkbenchViewIds.PartialSums => value,
        Phase8AUnityWorkbenchViewIds.Assembly => value,
        Phase8AUnityWorkbenchViewIds.Layers => value,
        Phase8AUnityWorkbenchViewIds.Overrides => value,
        Phase8AUnityWorkbenchViewIds.Replay => value,
        _ => Phase8AUnityWorkbenchViewIds.Clusters
    };
}
