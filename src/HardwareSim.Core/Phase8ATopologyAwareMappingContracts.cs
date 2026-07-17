using System.Collections.ObjectModel;
using System.Text.Json;

namespace HardwareSim.Core;

/// <summary>Defines explicit semantic roles used by the generic topology-aware mapping contract.</summary>
public static class Phase8AMappingSemanticRoles
{
    /// <summary>Component role for a workload compute target.</summary>
    public const string Compute = "compute";
    /// <summary>Component role for an externally supplied operand ingress.</summary>
    public const string Ingress = "ingress";
    /// <summary>Component role for an explicit numerical collective target.</summary>
    public const string Collective = "collective";
    /// <summary>Input port role for grouped partial vectors.</summary>
    public const string PartialVector = "partial_vector";
    /// <summary>Input port role for offset-aware tensor assembly.</summary>
    public const string AssemblyInput = "assembly_input";
    /// <summary>Output port role for computed or collected tensor values.</summary>
    public const string Result = "result";
}

/// <summary>Binds one normalized operation to exact activation, weight, and output tensors and a semantic order.</summary>
/// <param name="OperationId">Exact normalized operation identity.</param>
/// <param name="OperationOrdinal">Caller-authoritative semantic operation order.</param>
/// <param name="ActivationTensorId">Exact activation tensor identity.</param>
/// <param name="WeightTensorId">Exact weight tensor identity.</param>
/// <param name="OutputTensorId">Exact output tensor identity.</param>
public sealed record Phase8AOperationTensorBinding(
    string OperationId,
    int OperationOrdinal,
    string ActivationTensorId,
    string WeightTensorId,
    string OutputTensorId);

/// <summary>Identifies an exact actual-graph operand producer endpoint.</summary>
/// <param name="OperationId">Exact normalized operation identity.</param>
/// <param name="OperandRoleId">Exact activation or weight role.</param>
/// <param name="ProducerComponentId">Exact actual-graph producer component identity.</param>
/// <param name="ProducerHardwarePortName">Exact actual-graph output port name.</param>
public sealed record Phase8AOperandIngressBinding(
    string OperationId,
    string OperandRoleId,
    string ProducerComponentId,
    string ProducerHardwarePortName);

/// <summary>Supplies generic semantic role and order for one actual-graph component.</summary>
/// <param name="ComponentId">Exact actual-graph component identity.</param>
/// <param name="RoleId">Explicit generic semantic role.</param>
/// <param name="Ordinal">Caller-authoritative semantic order within that role.</param>
/// <param name="CoordinateRow">Optional typed row used only after explicit role selection.</param>
/// <param name="CoordinateColumn">Optional typed column used only after explicit role selection.</param>
public sealed record Phase8ASemanticComponentOrdinal(
    string ComponentId,
    string RoleId,
    int Ordinal,
    int? CoordinateRow = null,
    int? CoordinateColumn = null);

/// <summary>Supplies caller-authoritative semantic order for one exact logical-path catalog entry.</summary>
/// <param name="PathId">Exact logical-path catalog identity.</param>
/// <param name="Ordinal">Stable semantic path order.</param>
public sealed record Phase8ASemanticPathOrdinal(string PathId, int Ordinal);

/// <summary>Supplies caller-authoritative semantic order for one exact compiled storage selector.</summary>
public sealed record Phase8AStorageSelectorOrdinal(string ComponentId, string ResourceId, int Ordinal);

/// <summary>Represents generic normalized workload and topology input without preset mesh restrictions.</summary>
public sealed class Phase8ATopologyAwareMappingInput
{
    /// <summary>Creates a defensive generic topology-aware input.</summary>
    /// <param name="workload">Normalized workload operations and tensor artifacts.</param>
    /// <param name="topologyIdentity">Exact custom or preset topology identity.</param>
    /// <param name="policyId">Exact versioned mapping policy identity.</param>
    /// <param name="seed">Explicit deterministic seed.</param>
    /// <param name="operationBindings">Exact operation tensor roles and semantic order.</param>
    /// <param name="operandIngressBindings">Exact actual-graph activation and weight ingress endpoints.</param>
    /// <param name="componentOrdinals">Explicit generic component roles and semantic order.</param>
    /// <param name="pathOrdinals">Explicit logical-path semantic order.</param>
    /// <param name="storageSelectorOrdinals">Explicit compiled storage-selector semantic order.</param>
    public Phase8ATopologyAwareMappingInput(
        ReferenceMappingWorkload workload,
        string topologyIdentity,
        string policyId,
        int seed,
        IEnumerable<Phase8AOperationTensorBinding>? operationBindings,
        IEnumerable<Phase8AOperandIngressBinding>? operandIngressBindings,
        IEnumerable<Phase8ASemanticComponentOrdinal>? componentOrdinals,
        IEnumerable<Phase8ASemanticPathOrdinal>? pathOrdinals,
        IEnumerable<Phase8AStorageSelectorOrdinal>? storageSelectorOrdinals = null)
    {
        Workload = workload ?? throw new ArgumentNullException(nameof(workload));
        TopologyIdentity = topologyIdentity?.Trim() ?? "";
        PolicyId = policyId?.Trim() ?? "";
        Seed = seed;
        OperationBindings = Freeze(operationBindings, item => $"{item.OperationOrdinal:D10}\u001f{item.OperationId}", nameof(operationBindings));
        OperandIngressBindings = Freeze(operandIngressBindings, item => $"{item.OperationId}\u001f{item.OperandRoleId}", nameof(operandIngressBindings));
        ComponentOrdinals = Freeze(componentOrdinals, item => $"{item.RoleId}\u001f{item.Ordinal:D10}\u001f{item.ComponentId}", nameof(componentOrdinals));
        PathOrdinals = Freeze(pathOrdinals, item => $"{item.Ordinal:D10}\u001f{item.PathId}", nameof(pathOrdinals));
        StorageSelectorOrdinals = Freeze(storageSelectorOrdinals, item => $"{item.Ordinal:D10}\u001f{item.ComponentId}\u001f{item.ResourceId}", nameof(storageSelectorOrdinals));
    }

    /// <summary>Gets the normalized workload.</summary>
    public ReferenceMappingWorkload Workload { get; }
    /// <summary>Gets the exact custom or preset topology identity.</summary>
    public string TopologyIdentity { get; }
    /// <summary>Gets the exact policy identity.</summary>
    public string PolicyId { get; }
    /// <summary>Gets the explicit deterministic seed.</summary>
    public int Seed { get; }
    /// <summary>Gets exact tensor roles in semantic operation order.</summary>
    public IReadOnlyList<Phase8AOperationTensorBinding> OperationBindings { get; }
    /// <summary>Gets exact operand ingress endpoints.</summary>
    public IReadOnlyList<Phase8AOperandIngressBinding> OperandIngressBindings { get; }
    /// <summary>Gets explicit generic component roles and order.</summary>
    public IReadOnlyList<Phase8ASemanticComponentOrdinal> ComponentOrdinals { get; }
    /// <summary>Gets explicit logical-path order.</summary>
    public IReadOnlyList<Phase8ASemanticPathOrdinal> PathOrdinals { get; }
    /// <summary>Gets explicit compiled-storage selector order.</summary>
    public IReadOnlyList<Phase8AStorageSelectorOrdinal> StorageSelectorOrdinals { get; }

    private static IReadOnlyList<T> Freeze<T>(IEnumerable<T>? source, Func<T, string> key, string parameterName)
        where T : class
    {
        var values = (source ?? []).ToArray();
        if (values.Any(item => item is null))
        {
            throw new ArgumentException("Topology-aware mapping collections cannot contain null entries.", parameterName);
        }
        return Array.AsReadOnly(values.OrderBy(key, StringComparer.Ordinal).ToArray());
    }
}

/// <summary>Explicitly binds one compiled capability port to one actual HardwareGraph port.</summary>
/// <param name="ComponentId">Exact component identity shared by capability and graph.</param>
/// <param name="CapabilityPortId">Exact compiled capability port identity.</param>
/// <param name="HardwarePortName">Exact actual-graph port name.</param>
public sealed record Phase8ACapabilityPortBinding(
    string ComponentId,
    string CapabilityPortId,
    string HardwarePortName);

/// <summary>Provides a Core-discovered capability snapshot and exact actual-graph port bindings.</summary>
public sealed class Phase8ACapabilityAuthority
{
    /// <summary>Defines the complete capability-content hash projection.</summary>
    public const string ContentHashAlgorithm = "sha256/phase8a-capability-authority-content/v1";

    internal Phase8ACapabilityAuthority(
        CapabilitySnapshot snapshot,
        string contentHash,
        IEnumerable<Phase8ACapabilityPortBinding>? portBindings,
        string pluginRegistryHash,
        string runtimeKernelRegistryHash,
        string compiledSourceGraphHash)
    {
        Snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
        ContentHash = contentHash?.Trim() ?? "";
        PortBindings = Array.AsReadOnly((portBindings ?? [])
            .OrderBy(item => item.ComponentId, StringComparer.Ordinal)
            .ThenBy(item => item.CapabilityPortId, StringComparer.Ordinal)
            .ToArray());
        PluginRegistryHash = pluginRegistryHash?.Trim() ?? "";
        RuntimeKernelRegistryHash = runtimeKernelRegistryHash?.Trim() ?? "";
        CompiledSourceGraphHash = compiledSourceGraphHash?.Trim() ?? "";
    }

    /// <summary>Gets the frozen capability snapshot.</summary>
    public CapabilitySnapshot Snapshot { get; }
    /// <summary>Gets the Core-defined complete content hash algorithm.</summary>
    public string ContentHashAlgorithmId => ContentHashAlgorithm;
    /// <summary>Gets the Core-computed complete capability content digest.</summary>
    public string ContentHash { get; }
    /// <summary>Gets exact actual-graph port bindings.</summary>
    public IReadOnlyList<Phase8ACapabilityPortBinding> PortBindings { get; }
    /// <summary>Gets the exact plugin registry hash used by Core discovery.</summary>
    public string PluginRegistryHash { get; }
    /// <summary>Gets the exact runtime-kernel registry hash used by Core discovery.</summary>
    public string RuntimeKernelRegistryHash { get; }
    /// <summary>Gets the compiled simulation graph source hash used by Core discovery.</summary>
    public string CompiledSourceGraphHash { get; }
}

/// <summary>Provides an expected full semantic hash for one immutable lowering plan.</summary>
public sealed class Phase8ALoweringAuthority
{
    /// <summary>Defines the complete public lowering semantic hash projection.</summary>
    public const string SemanticHashAlgorithm = "sha256/phase8a-lowering-authority-semantic/v1";

    /// <summary>Creates one lowering authority envelope.</summary>
    /// <param name="plan">Immutable lowering plan.</param>
    /// <param name="semanticHashAlgorithm">Expected semantic hash algorithm.</param>
    /// <param name="semanticHash">Expected full semantic digest.</param>
    public Phase8ALoweringAuthority(
        Phase8AMatMulLoweringPlan plan,
        string semanticHashAlgorithm,
        string semanticHash)
    {
        Plan = plan ?? throw new ArgumentNullException(nameof(plan));
        SemanticHashAlgorithmId = semanticHashAlgorithm?.Trim() ?? "";
        SemanticHash = semanticHash?.Trim() ?? "";
    }

    /// <summary>Gets the immutable lowering plan.</summary>
    public Phase8AMatMulLoweringPlan Plan { get; }
    /// <summary>Gets the declared semantic hash algorithm.</summary>
    public string SemanticHashAlgorithmId { get; }
    /// <summary>Gets the declared full semantic digest.</summary>
    public string SemanticHash { get; }
}

/// <summary>Defines explicit bounded enumeration limits.</summary>
public sealed record Phase8AMappingPolicyBudget(
    int MaxTargetOptionsPerTile,
    int MaxPathsPerEndpointPair,
    int MaxCollectiveTargetsPerIntent,
    long MaxSearchNodes)
{
    /// <summary>Creates conservative deterministic defaults.</summary>
    public static Phase8AMappingPolicyBudget CreateDefault() => new(256, 64, 64, 100_000);
}

/// <summary>Reports whether bounded problem-surface enumeration retained all target/path options; it does not report downstream policy DFS exhaustion.</summary>
/// <param name="VisitedOptions">Number of hard-feasible options visited before retention.</param>
/// <param name="RetainedOptions">Number of options retained in the immutable problem.</param>
/// <param name="IsCompleteSearch">Whether no option was truncated by policy bounds.</param>
/// <param name="TruncatedReasonId">Stable truncation reason, or an empty string.</param>
public sealed record Phase8AMappingSearchStatus(
    long VisitedOptions,
    long RetainedOptions,
    bool IsCompleteSearch,
    string TruncatedReasonId);

/// <summary>Locks one operation tile to an exact target, port, and storage selector.</summary>
/// <param name="OperationTileId">Exact lowered operation tile.</param>
/// <param name="TargetComponentId">Exact target component.</param>
/// <param name="TargetPortId">Exact target weight port.</param>
/// <param name="StorageResourceId">Exact storage resource.</param>
public sealed record Phase8AManualTargetConstraint(
    string OperationTileId,
    string TargetComponentId,
    string TargetPortId,
    string StorageResourceId);

/// <summary>Locks one operation operand to one exact path.</summary>
/// <param name="OperationTileId">Exact lowered operation tile.</param>
/// <param name="OperandRoleId">Exact activation or weight role.</param>
/// <param name="PathId">Exact logical-path catalog identity.</param>
public sealed record Phase8AManualOperandPathConstraint(
    string OperationTileId,
    string OperandRoleId,
    string PathId);

/// <summary>Locks one collective intent to one exact target and input port.</summary>
/// <param name="CollectiveIntentId">Exact collective intent.</param>
/// <param name="TargetComponentId">Exact collective component.</param>
/// <param name="InputPortId">Exact collective input port.</param>
public sealed record Phase8AManualCollectiveConstraint(
    string CollectiveIntentId,
    string TargetComponentId,
    string InputPortId);

/// <summary>Freezes one occupied storage interval inherited from Mapping 2.0.</summary>
/// <param name="PlacementId">Exact base placement identity.</param>
/// <param name="TileId">Exact resident tile identity.</param>
/// <param name="AddressBits">Base bit address.</param>
/// <param name="SizeBits">Occupied bit extent.</param>
/// <param name="ReuseKey">Exact resident reuse identity, or an empty string.</param>
public sealed record Phase8AStorageOccupiedInterval(
    string PlacementId,
    string TileId,
    long AddressBits,
    long SizeBits,
    string ReuseKey);

/// <summary>Describes an exact global storage ledger selector and its frozen base occupancy.</summary>
public sealed class Phase8AStorageSelectorState
{
    internal Phase8AStorageSelectorState(
        string componentId,
        string resourceId,
        string levelId,
        int storageOrdinal,
        long capacityBits,
        long alignmentBits,
        long allocationGranularityBits,
        int residentSlots,
        bool supportsReuse,
        IEnumerable<Phase8AStorageOccupiedInterval> occupied)
    {
        ComponentId = componentId;
        ResourceId = resourceId;
        LevelId = levelId;
        StorageOrdinal = storageOrdinal;
        CapacityBits = capacityBits;
        AlignmentBits = alignmentBits;
        AllocationGranularityBits = allocationGranularityBits;
        ResidentSlots = residentSlots;
        SupportsReuse = supportsReuse;
        BaseOccupiedIntervals = Array.AsReadOnly(occupied.OrderBy(item => item.AddressBits).ThenBy(item => item.PlacementId, StringComparer.Ordinal).ToArray());
    }

    /// <summary>Gets the exact component identity.</summary>
    public string ComponentId { get; }
    /// <summary>Gets the exact storage resource identity.</summary>
    public string ResourceId { get; }
    /// <summary>Gets the exact storage-level selector.</summary>
    public string LevelId { get; }
    /// <summary>Gets caller-authoritative semantic storage order.</summary>
    public int StorageOrdinal { get; }
    /// <summary>Gets total capacity in bits.</summary>
    public long CapacityBits { get; }
    /// <summary>Gets allocation alignment in bits.</summary>
    public long AlignmentBits { get; }
    /// <summary>Gets allocation granularity in bits.</summary>
    public long AllocationGranularityBits { get; }
    /// <summary>Gets total resident slots.</summary>
    public int ResidentSlots { get; }
    /// <summary>Gets whether exact resident reuse is supported.</summary>
    public bool SupportsReuse { get; }
    /// <summary>Gets immutable base occupied intervals.</summary>
    public IReadOnlyList<Phase8AStorageOccupiedInterval> BaseOccupiedIntervals { get; }
}

/// <summary>Identifies one exact typed endpoint in the actual graph and compiled capability surface.</summary>
public sealed record Phase8AExactRouteEndpoint(
    string ComponentId,
    string HardwarePortName,
    string CapabilityPortId,
    string SemanticRoleId,
    int ComponentOrdinal);

/// <summary>Binds one exact producer-port/consumer-port pair to one validated catalog path.</summary>
public sealed record Phase8AExactRoutePair(
    Phase8AExactRouteEndpoint Source,
    Phase8AExactRouteEndpoint Destination,
    string PathId,
    int PathOrdinal,
    long TotalLatencyCycles,
    double TotalPhysicalLengthMicrometers);

/// <summary>Provides exact endpoint-pair route lookup without component-only reachability shortcuts.</summary>
public sealed class Phase8AExactRouteMatrix
{
    internal Phase8AExactRouteMatrix(IEnumerable<Phase8AExactRoutePair> pairs)
    {
        Pairs = Array.AsReadOnly(pairs
            .OrderBy(item => item.Source.ComponentOrdinal)
            .ThenBy(item => item.Destination.ComponentOrdinal)
            .ThenBy(item => item.PathOrdinal)
            .ToArray());
    }

    /// <summary>Gets every exact route pair in semantic order.</summary>
    public IReadOnlyList<Phase8AExactRoutePair> Pairs { get; }

    /// <summary>Finds exact paths between actual graph ports.</summary>
    public IReadOnlyList<Phase8AExactRoutePair> Find(
        string sourceComponentId,
        string sourceHardwarePortName,
        string destinationComponentId,
        string destinationHardwarePortName) => Array.AsReadOnly(Pairs.Where(item =>
            string.Equals(item.Source.ComponentId, sourceComponentId, StringComparison.Ordinal) &&
            string.Equals(item.Source.HardwarePortName, sourceHardwarePortName, StringComparison.Ordinal) &&
            string.Equals(item.Destination.ComponentId, destinationComponentId, StringComparison.Ordinal) &&
            string.Equals(item.Destination.HardwarePortName, destinationHardwarePortName, StringComparison.Ordinal)).ToArray());
}

/// <summary>Describes one hard-feasible compute target without allocating a candidate address.</summary>
public sealed record Phase8AMappingTargetOption
{
    /// <summary>Creates one target option and snapshots both caller-owned path collections.</summary>
    public Phase8AMappingTargetOption(
        int targetOrdinal,
        int storageOrdinal,
        string targetComponentId,
        string activationPortId,
        string weightPortId,
        string resultPortId,
        string storageResourceId,
        string storageLevelId,
        long requiredWeightBits,
        string weightReuseKey,
        IReadOnlyList<string>? activationPathIds,
        IReadOnlyList<string>? weightPathIds)
    {
        TargetOrdinal = targetOrdinal;
        StorageOrdinal = storageOrdinal;
        TargetComponentId = targetComponentId;
        ActivationPortId = activationPortId;
        WeightPortId = weightPortId;
        ResultPortId = resultPortId;
        StorageResourceId = storageResourceId;
        StorageLevelId = storageLevelId;
        RequiredWeightBits = requiredWeightBits;
        WeightReuseKey = weightReuseKey;
        ActivationPathIds = FreezePathIds(activationPathIds);
        WeightPathIds = FreezePathIds(weightPathIds);
    }

    /// <summary>Gets the caller-authoritative semantic target ordinal.</summary>
    public int TargetOrdinal { get; }
    /// <summary>Gets the caller-authoritative semantic storage ordinal.</summary>
    public int StorageOrdinal { get; }
    /// <summary>Gets the exact target component identity.</summary>
    public string TargetComponentId { get; }
    /// <summary>Gets the exact target activation capability port.</summary>
    public string ActivationPortId { get; }
    /// <summary>Gets the exact target weight capability port.</summary>
    public string WeightPortId { get; }
    /// <summary>Gets the exact target result capability port.</summary>
    public string ResultPortId { get; }
    /// <summary>Gets the exact target storage resource.</summary>
    public string StorageResourceId { get; }
    /// <summary>Gets the exact target storage level.</summary>
    public string StorageLevelId { get; }
    /// <summary>Gets the required resident weight extent in bits.</summary>
    public long RequiredWeightBits { get; }
    /// <summary>Gets the exact weight-residency reuse identity.</summary>
    public string WeightReuseKey { get; }
    /// <summary>Gets immutable exact activation path identities.</summary>
    public IReadOnlyList<string> ActivationPathIds { get; }
    /// <summary>Gets immutable exact weight path identities.</summary>
    public IReadOnlyList<string> WeightPathIds { get; }

    private static IReadOnlyList<string> FreezePathIds(IEnumerable<string>? source) =>
        Array.AsReadOnly((source ?? []).ToArray());
}

/// <summary>Contains one lowered compute tile and its bounded hard-feasible target surface.</summary>
public sealed class Phase8AMappingOperationProblem
{
    internal Phase8AMappingOperationProblem(
        int operationOrdinal,
        Phase8ALoweredOperationTile tile,
        Phase8ALoweredOperandTile activation,
        Phase8ALoweredOperandTile weight,
        Phase8ALoweredOutputTile output,
        IEnumerable<Phase8AMappingTargetOption> targetOptions)
    {
        OperationOrdinal = operationOrdinal;
        Tile = tile;
        Activation = activation;
        Weight = weight;
        Output = output;
        TargetOptions = Array.AsReadOnly(targetOptions
            .OrderBy(item => item.TargetOrdinal)
            .ThenBy(item => item.StorageOrdinal)
            .ToArray());
    }

    /// <summary>Gets the semantic workload-operation order.</summary>
    public int OperationOrdinal { get; }
    /// <summary>Gets the exact lowering compute tile.</summary>
    public Phase8ALoweredOperationTile Tile { get; }
    /// <summary>Gets the exact activation tile.</summary>
    public Phase8ALoweredOperandTile Activation { get; }
    /// <summary>Gets the exact weight tile.</summary>
    public Phase8ALoweredOperandTile Weight { get; }
    /// <summary>Gets the exact partial-output tile.</summary>
    public Phase8ALoweredOutputTile Output { get; }
    /// <summary>Gets bounded hard-feasible targets in semantic order.</summary>
    public IReadOnlyList<Phase8AMappingTargetOption> TargetOptions { get; }
}

/// <summary>Describes one exact collective target and required semantic ports.</summary>
public sealed record Phase8AMappingCollectiveTargetOption(
    int TargetOrdinal,
    string TargetComponentId,
    string InputPortId,
    string ResultPortId);

/// <summary>Contains one validated lowered collective and its bounded explicit target surface.</summary>
public sealed class Phase8AMappingCollectiveRequirement
{
    internal Phase8AMappingCollectiveRequirement(
        int operationOrdinal,
        Phase8ALoweredCollectiveIntent intent,
        string requiredOperationKindId,
        string requiredInputRoleId,
        IEnumerable<Phase8AMappingCollectiveTargetOption> targetOptions)
    {
        OperationOrdinal = operationOrdinal;
        Intent = intent;
        RequiredOperationKindId = requiredOperationKindId;
        RequiredInputRoleId = requiredInputRoleId;
        TargetOptions = Array.AsReadOnly(targetOptions.OrderBy(item => item.TargetOrdinal).ToArray());
    }

    /// <summary>Gets the semantic workload-operation order.</summary>
    public int OperationOrdinal { get; }
    /// <summary>Gets the exact lowering collective intent.</summary>
    public Phase8ALoweredCollectiveIntent Intent { get; }
    /// <summary>Gets the exact collective capability operation kind.</summary>
    public string RequiredOperationKindId { get; }
    /// <summary>Gets the exact required compiled input semantic role.</summary>
    public string RequiredInputRoleId { get; }
    /// <summary>Gets bounded explicit targets in semantic order.</summary>
    public IReadOnlyList<Phase8AMappingCollectiveTargetOption> TargetOptions { get; }
}

/// <summary>Selects one complete compute target and both exact operand routes.</summary>
public sealed record Phase8AOperationCandidateSelection(
    string OperationTileId,
    string TargetComponentId,
    string ActivationPortId,
    string WeightPortId,
    string ResultPortId,
    string StorageResourceId,
    string ActivationPathId,
    string WeightPathId);

/// <summary>Selects one exact contributor-to-collective route.</summary>
public sealed record Phase8ACollectiveContributorRoute(string ContributorTileId, string PathId);

/// <summary>Selects one collective target and exact routes for every contributor.</summary>
public sealed record Phase8ACollectiveCandidateSelection
{
    /// <summary>Creates one untrusted collective selection with a defensive contributor-route snapshot.</summary>
    public Phase8ACollectiveCandidateSelection(
        string collectiveIntentId,
        string targetComponentId,
        string inputPortId,
        string resultPortId,
        IEnumerable<Phase8ACollectiveContributorRoute>? contributorRoutes)
        : this(collectiveIntentId, targetComponentId, inputPortId, resultPortId, contributorRoutes, inheritedMalformed: false)
    {
    }

    private Phase8ACollectiveCandidateSelection(
        string collectiveIntentId,
        string targetComponentId,
        string inputPortId,
        string resultPortId,
        IEnumerable<Phase8ACollectiveContributorRoute>? contributorRoutes,
        bool inheritedMalformed)
    {
        CollectiveIntentId = collectiveIntentId;
        TargetComponentId = targetComponentId;
        InputPortId = inputPortId;
        ResultPortId = resultPortId;
        var suppliedRoutes = contributorRoutes?.ToArray();
        HasMalformedContributorRoutes = inheritedMalformed || suppliedRoutes is null || suppliedRoutes.Any(item => item is null);
        ContributorRoutes = Array.AsReadOnly((suppliedRoutes ?? [])
            .Where(item => item is not null)
            .ToArray()!);
    }

    internal Phase8ACollectiveCandidateSelection DefensiveCopy() => new(
        CollectiveIntentId,
        TargetComponentId,
        InputPortId,
        ResultPortId,
        ContributorRoutes,
        HasMalformedContributorRoutes);

    /// <summary>Gets the exact collective intent identity.</summary>
    public string CollectiveIntentId { get; }
    /// <summary>Gets the exact selected collective component.</summary>
    public string TargetComponentId { get; }
    /// <summary>Gets the exact selected collective input port.</summary>
    public string InputPortId { get; }
    /// <summary>Gets the exact selected collective result port.</summary>
    public string ResultPortId { get; }
    /// <summary>Gets an immutable defensive contributor-route snapshot.</summary>
    public IReadOnlyList<Phase8ACollectiveContributorRoute> ContributorRoutes { get; }
    /// <summary>Gets whether the caller supplied a null collection or null contributor route.</summary>
    public bool HasMalformedContributorRoutes { get; }
}

/// <summary>Represents an untrusted candidate draft that must pass complete verification.</summary>
public sealed class Phase8AMappingCandidateDraft
{
    /// <summary>Creates a defensive candidate draft.</summary>
    public Phase8AMappingCandidateDraft(
        IEnumerable<Phase8AOperationCandidateSelection>? operationSelections,
        IEnumerable<Phase8ACollectiveCandidateSelection>? collectiveSelections)
    {
        var operations = (operationSelections ?? []).ToArray();
        var collectives = (collectiveSelections ?? []).ToArray();
        HasMalformedElements = operations.Any(item => item is null) || collectives.Any(item => item is null) ||
            collectives.Any(item => item?.HasMalformedContributorRoutes == true);
        OperationSelections = Array.AsReadOnly(operations.Where(item => item is not null).OrderBy(item => item.OperationTileId, StringComparer.Ordinal).ToArray()!);
        CollectiveSelections = Array.AsReadOnly(collectives
            .Where(item => item is not null)
            .Select(item => item.DefensiveCopy())
            .OrderBy(item => item.CollectiveIntentId, StringComparer.Ordinal)
            .ToArray());
    }

    /// <summary>Gets whether the caller supplied null top-level selections or malformed contributor routes.</summary>
    public bool HasMalformedElements { get; }
    /// <summary>Gets untrusted compute selections.</summary>
    public IReadOnlyList<Phase8AOperationCandidateSelection> OperationSelections { get; }
    /// <summary>Gets untrusted collective selections.</summary>
    public IReadOnlyList<Phase8ACollectiveCandidateSelection> CollectiveSelections { get; }
}

/// <summary>Records one verifier-owned global storage allocation.</summary>
public sealed record Phase8AStorageAllocationDecision(
    string OperationTileId,
    string ComponentId,
    string ResourceId,
    long AddressBits,
    long SizeBits,
    string ReuseKey,
    bool ReusedExistingAllocation);

/// <summary>Describes one deterministic candidate-verification failure.</summary>
public sealed record Phase8ACandidateVerificationIssue(string Code, string Location, string Message, string? RelatedId = null);

/// <summary>Contains candidate-verifier decisions or deterministic hard failures.</summary>
public sealed class Phase8ACandidateVerificationResult
{
    internal Phase8ACandidateVerificationResult(
        IEnumerable<Phase8AStorageAllocationDecision> allocations,
        IEnumerable<Phase8ACandidateVerificationIssue> issues)
    {
        Allocations = Array.AsReadOnly(allocations.OrderBy(item => item.ComponentId, StringComparer.Ordinal)
            .ThenBy(item => item.ResourceId, StringComparer.Ordinal)
            .ThenBy(item => item.AddressBits)
            .ToArray());
        Issues = Array.AsReadOnly(issues.OrderBy(item => item.Location, StringComparer.Ordinal)
            .ThenBy(item => item.Code, StringComparer.Ordinal)
            .ToArray());
    }

    /// <summary>Gets verifier-owned storage decisions.</summary>
    public IReadOnlyList<Phase8AStorageAllocationDecision> Allocations { get; }
    /// <summary>Gets deterministic hard failures.</summary>
    public IReadOnlyList<Phase8ACandidateVerificationIssue> Issues { get; }
    /// <summary>Gets whether the candidate is complete and hard-feasible.</summary>
    public bool IsSuccess => Issues.Count == 0;
}
/// <summary>Contains a defensively frozen actual custom graph and its exact attached typed manifest.</summary>
public sealed class Phase8ACustomTopologyAuthority
{
    private readonly string _graphJson;
    internal Phase8ACustomTopologyAuthority(HardwareGraph graph, TopologyManifest manifest)
    {
        _graphJson = HardwareGraphJson.Serialize(graph);
        Manifest = manifest;
    }

    /// <summary>Gets the complete custom topology manifest.</summary>
    public TopologyManifest Manifest { get; }
    /// <summary>Gets a raw JSON defensive clone that preserves physical stale edits exactly.</summary>
    public HardwareGraph CloneActualGraph() => JsonSerializer.Deserialize<HardwareGraph>(_graphJson, HardwareGraphJson.Options)
        ?? throw new InvalidOperationException("Frozen custom HardwareGraph JSON deserialized to null.");
}

/// <summary>Returns a public custom-topology authority or structured validation diagnostics.</summary>
public sealed class Phase8ACustomTopologyAuthorityResult
{
    internal Phase8ACustomTopologyAuthorityResult(Phase8ACustomTopologyAuthority? authority, IEnumerable<TopologyBuildIssue> issues)
    {
        Authority = authority;
        Issues = Array.AsReadOnly(issues.ToArray());
    }
    /// <summary>Gets the defensively frozen custom authority when validation succeeds.</summary>
    public Phase8ACustomTopologyAuthority? Authority { get; }
    /// <summary>Gets deterministic structured custom-authority diagnostics.</summary>
    public IReadOnlyList<TopologyBuildIssue> Issues { get; }
    /// <summary>Gets whether authority creation completed without error diagnostics.</summary>
    public bool IsSuccess => Authority is not null && Issues.All(item => item.Severity != ValidationSeverity.Error);
}

/// <summary>Declares exact actual-graph port endpoints for one custom logical link.</summary>
public sealed record Phase8ACustomTopologyLinkEndpoint(
    string LinkId,
    string SourceComponentId,
    string SourcePortName,
    string DestinationComponentId,
    string DestinationPortName);

/// <summary>Creates graph-attached custom topology authority without applying mesh or cluster feasibility rules.</summary>
public static class Phase8ACustomTopologyAuthorityFactory
{
    /// <summary>Validates and attaches explicit typed custom-topology authority to an actual graph.</summary>
    public static Phase8ACustomTopologyAuthorityResult CreateAndAttach(
        HardwareGraph? actualGraph,
        TopologyPresetRequest? explicitRequest,
        IEnumerable<TopologyManifestComponent>? components,
        IEnumerable<TopologyManifestLink>? links,
        IEnumerable<Phase8ACustomTopologyLinkEndpoint>? exactLinkEndpoints)
    {
        try
        {
            return CreateAndAttachCore(actualGraph, explicitRequest, components, links, exactLinkEndpoints);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or ArgumentException or
            KeyNotFoundException or OverflowException or NullReferenceException or NotSupportedException or
            HardwareGraphSerializationException)
        {
            return Failure("$", "Malformed custom topology authority was rejected structurally: " + exception.Message);
        }
    }

    private static Phase8ACustomTopologyAuthorityResult CreateAndAttachCore(
        HardwareGraph? actualGraph,
        TopologyPresetRequest? explicitRequest,
        IEnumerable<TopologyManifestComponent>? components,
        IEnumerable<TopologyManifestLink>? links,
        IEnumerable<Phase8ACustomTopologyLinkEndpoint>? exactLinkEndpoints)
    {
        var issues = new List<TopologyBuildIssue>();
        if (actualGraph is null)
        {
            return Failure("$.actualGraph", "An actual HardwareGraph is required.");
        }

        ValidateExplicitRequest(explicitRequest, issues);
        if (components is null)
        {
            issues.Add(Error("$.components", "An explicit custom manifest component collection is required."));
        }
        if (links is null)
        {
            issues.Add(Error("$.links", "An explicit custom manifest link collection is required."));
        }
        if (exactLinkEndpoints is null)
        {
            issues.Add(Error("$.exactLinkEndpoints", "An explicit custom exact-link endpoint collection is required."));
        }

        var componentArray = (components ?? []).ToArray();
        var linkArray = (links ?? []).ToArray();
        var endpointArray = (exactLinkEndpoints ?? []).ToArray();
        ValidateGraphShape(actualGraph, issues);
        ValidateManifestShape(componentArray, linkArray, endpointArray, issues);
        if (issues.Count != 0)
        {
            return new Phase8ACustomTopologyAuthorityResult(null, issues);
        }

        var graphComponentIds = actualGraph.Components.Select(item => item.Id).ToArray();
        if (graphComponentIds.Any(string.IsNullOrWhiteSpace) ||
            graphComponentIds.Distinct(StringComparer.Ordinal).Count() != graphComponentIds.Length)
        {
            issues.Add(Error("$.actualGraph.components", "Actual custom graph component ids must be non-empty and unique."));
        }
        var manifestComponentIds = componentArray.Select(item => item.ComponentId).ToArray();
        if (manifestComponentIds.Any(string.IsNullOrWhiteSpace) ||
            manifestComponentIds.Distinct(StringComparer.Ordinal).Count() != manifestComponentIds.Length ||
            !manifestComponentIds.ToHashSet(StringComparer.Ordinal).SetEquals(graphComponentIds))
        {
            issues.Add(Error("$.components", "Custom manifest components must exactly and uniquely cover actual graph components."));
        }

        var graphLinkIds = actualGraph.Links.Select(item => item.Id).ToArray();
        var manifestLinkIds = linkArray.Select(item => item.LinkId).ToArray();
        var endpointLinkIds = endpointArray.Select(item => item.LinkId).ToArray();
        if (graphLinkIds.Any(string.IsNullOrWhiteSpace) ||
            graphLinkIds.Distinct(StringComparer.Ordinal).Count() != graphLinkIds.Length)
        {
            issues.Add(Error("$.actualGraph.links", "Actual custom graph link ids must be non-empty and unique."));
        }
        if (manifestLinkIds.Any(string.IsNullOrWhiteSpace) ||
            manifestLinkIds.Distinct(StringComparer.Ordinal).Count() != manifestLinkIds.Length ||
            !manifestLinkIds.ToHashSet(StringComparer.Ordinal).SetEquals(graphLinkIds) ||
            endpointLinkIds.Any(string.IsNullOrWhiteSpace) ||
            endpointLinkIds.Distinct(StringComparer.Ordinal).Count() != endpointLinkIds.Length ||
            !endpointLinkIds.ToHashSet(StringComparer.Ordinal).SetEquals(graphLinkIds))
        {
            issues.Add(Error("$.links", "Custom manifest links and exact port-endpoint descriptors must uniquely cover every actual graph link."));
        }
        if (issues.Count != 0)
        {
            return new Phase8ACustomTopologyAuthorityResult(null, issues);
        }

        var graphComponents = actualGraph.Components.ToDictionary(item => item.Id, StringComparer.Ordinal);
        var manifestLinks = linkArray.ToDictionary(item => item.LinkId, StringComparer.Ordinal);
        var endpoints = endpointArray.ToDictionary(item => item.LinkId, StringComparer.Ordinal);
        foreach (var actual in actualGraph.Links)
        {
            var item = manifestLinks[actual.Id];
            var endpoint = endpoints[actual.Id];
            if (!HasExactPort(graphComponents, actual.Source.ComponentId, actual.Source.PortName) ||
                !HasExactPort(graphComponents, actual.Destination.ComponentId, actual.Destination.PortName))
            {
                issues.Add(Error("$.actualGraph.links", "Every custom graph link endpoint must resolve to one exact actual-graph component port."));
                continue;
            }
            if (!string.Equals(item.SourceComponentId, actual.Source.ComponentId, StringComparison.Ordinal) ||
                !string.Equals(item.DestinationComponentId, actual.Destination.ComponentId, StringComparison.Ordinal) ||
                item.BandwidthBitsPerCycle != actual.BandwidthBitsPerCycle ||
                !string.Equals(endpoint.SourceComponentId, actual.Source.ComponentId, StringComparison.Ordinal) ||
                !string.Equals(endpoint.SourcePortName, actual.Source.PortName, StringComparison.Ordinal) ||
                !string.Equals(endpoint.DestinationComponentId, actual.Destination.ComponentId, StringComparison.Ordinal) ||
                !string.Equals(endpoint.DestinationPortName, actual.Destination.PortName, StringComparison.Ordinal))
            {
                issues.Add(Error("$.links", "Custom manifest link and exact typed source/destination ports must match the actual graph."));
            }
        }
        if (issues.Count != 0)
        {
            return new Phase8ACustomTopologyAuthorityResult(null, issues);
        }

        var cloneJson = HardwareGraphJson.Serialize(actualGraph);
        var clone = JsonSerializer.Deserialize<HardwareGraph>(cloneJson, HardwareGraphJson.Options);
        if (clone is null)
        {
            return Failure("$.actualGraph", "Actual graph defensive clone failed.");
        }
        var manifest = TopologyPresetCanonicalizer.CreateManifest(explicitRequest!, clone, componentArray, linkArray,
            "phase8a-custom-topology-authority", "1.0", "phase8a-public-custom-authority");
        var attached = TopologyManifestJson.AttachToGraph(clone, manifest);
        var read = TopologyManifestJson.ReadFromGraph(attached);
        if (!read.IsSuccess || read.Manifest is null)
        {
            return new Phase8ACustomTopologyAuthorityResult(null, read.Issues);
        }
        return new Phase8ACustomTopologyAuthorityResult(new Phase8ACustomTopologyAuthority(attached, read.Manifest), read.Issues);
    }

    private static void ValidateExplicitRequest(TopologyPresetRequest? request, List<TopologyBuildIssue> issues)
    {
        if (request is null)
        {
            issues.Add(Error("$.explicitRequest", "A caller-supplied custom topology request is required."));
            return;
        }
        if (string.IsNullOrWhiteSpace(request.TopologyId))
        {
            issues.Add(Error("$.explicitRequest.topologyId", "A non-empty custom topology id is required."));
        }
        else if (string.Equals(request.TopologyId, ReferenceMappingTopologyIds.MeshOfTreesV1, StringComparison.Ordinal) ||
            string.Equals(request.TopologyId, Flat2DMeshTopologyPresetBuilder.TopologyPresetId, StringComparison.Ordinal))
        {
            issues.Add(Error("$.explicitRequest.topologyId", "Built-in topology ids are reserved for their canonical preset builders and cannot identify custom authority."));
        }
        if (request.MeshRows < 0 || request.MeshColumns < 0 || request.ClusterSize < 0)
        {
            issues.Add(Error("$.explicitRequest", "Custom mesh and cluster dimensions must be non-negative; zero is allowed as an explicit not-applicable value."));
        }
        if (request.WordBits <= 0 || request.LeafLaneCount <= 0)
        {
            issues.Add(Error("$.explicitRequest", "Custom topology word width and lane count must be positive."));
        }
        if (!PositiveFinite(request.LeafLinkDistance) || !PositiveFinite(request.TreeDistanceScale) ||
            !PositiveFinite(request.MeshHopDistance) || !PositiveFinite(request.PlacementCellSizeMicrometers))
        {
            issues.Add(Error("$.explicitRequest", "Custom topology physical parameters must be positive and finite."));
        }
        if (request.RouterLatencyCycles < 0 || request.AdderLatencyCycles < 0)
        {
            issues.Add(Error("$.explicitRequest", "Custom topology latencies must be non-negative."));
        }
    }

    private static void ValidateGraphShape(HardwareGraph graph, List<TopologyBuildIssue> issues)
    {
        if (graph.Components is null)
        {
            issues.Add(Error("$.actualGraph.components", "Actual custom graph components cannot be null."));
        }
        else
        {
            ValidateComponentCollection(graph.Components, "$.actualGraph.components", issues);
        }
        if (graph.Links is null)
        {
            issues.Add(Error("$.actualGraph.links", "Actual custom graph links cannot be null."));
        }
        else
        {
            ValidateLinkCollection(graph.Links, "$.actualGraph.links", issues);
        }
        if (graph.Groups is null || graph.Macros is null || graph.Parameters is null || graph.ExtensionData is null)
        {
            issues.Add(Error("$.actualGraph", "Actual custom graph collections cannot be null."));
        }
        if (graph.Groups is not null)
        {
            for (var index = 0; index < graph.Groups.Count; index++)
            {
                var group = graph.Groups[index];
                if (group is null || group.ComponentIds is null || group.VisualMetadata is null || group.ExtensionData is null)
                {
                    issues.Add(Error($"$.actualGraph.groups[{index}]", "Actual custom graph groups and their collections cannot be null."));
                }
            }
        }
        if (graph.Macros is not null)
        {
            for (var index = 0; index < graph.Macros.Count; index++)
            {
                var macro = graph.Macros[index];
                if (macro is null)
                {
                    issues.Add(Error($"$.actualGraph.macros[{index}]", "Actual custom graph macros cannot contain null entries."));
                    continue;
                }
                ValidateComponentCollection(macro.InternalComponents, $"$.actualGraph.macros[{index}].internalComponents", issues);
                ValidateLinkCollection(macro.InternalLinks, $"$.actualGraph.macros[{index}].internalLinks", issues);
                if (macro.InternalGroups is null || macro.ExternalPortMappings is null || macro.ExtensionData is null ||
                    (macro.ExternalPortMappings?.Any(item => item.Value is null) ?? false))
                {
                    issues.Add(Error($"$.actualGraph.macros[{index}]", "Actual custom graph macro collections and port references cannot be null."));
                }
            }
        }
        ValidatePhysicalArtifacts(graph.Placement, graph.Routing, issues);
    }

    private static void ValidateComponentCollection(
        IEnumerable<HardwareComponent>? components,
        string location,
        List<TopologyBuildIssue> issues)
    {
        if (components is null)
        {
            issues.Add(Error(location, "Hardware component collection cannot be null."));
            return;
        }
        var index = 0;
        foreach (var component in components)
        {
            var itemLocation = $"{location}[{index}]";
            if (component is null)
            {
                issues.Add(Error(itemLocation, "Hardware component collection cannot contain null entries."));
                index++;
                continue;
            }
            if (component.Ports is null)
            {
                issues.Add(Error(itemLocation + ".ports", "Hardware component ports cannot be null."));
            }
            else
            {
                for (var portIndex = 0; portIndex < component.Ports.Count; portIndex++)
                {
                    var port = component.Ports[portIndex];
                    if (port is null || string.IsNullOrWhiteSpace(port.Name) || port.ExtensionData is null)
                    {
                        issues.Add(Error($"{itemLocation}.ports[{portIndex}]", "Hardware ports must be non-null, named, and structurally complete."));
                    }
                }
                if (component.Ports.Where(item => item is not null).Select(item => item.Name).Distinct(StringComparer.Ordinal).Count() !=
                    component.Ports.Count(item => item is not null))
                {
                    issues.Add(Error(itemLocation + ".ports", "Hardware port names must be unique within a component."));
                }
            }
            if (component.Parameters is null || component.VisualStyle is null || component.InternalState is null || component.ExtensionData is null ||
                (component.TemplateRef is not null && component.TemplateRef.ParameterOverrides is null))
            {
                issues.Add(Error(itemLocation, "Hardware component collections cannot be null."));
            }
            index++;
        }
    }

    private static void ValidateLinkCollection(
        IEnumerable<HardwareLink>? links,
        string location,
        List<TopologyBuildIssue> issues)
    {
        if (links is null)
        {
            issues.Add(Error(location, "Hardware link collection cannot be null."));
            return;
        }
        var index = 0;
        foreach (var link in links)
        {
            var itemLocation = $"{location}[{index}]";
            if (link is null)
            {
                issues.Add(Error(itemLocation, "Hardware link collection cannot contain null entries."));
                index++;
                continue;
            }
            if (link.Source is null || link.Destination is null ||
                string.IsNullOrWhiteSpace(link.Source?.ComponentId) || string.IsNullOrWhiteSpace(link.Source?.PortName) ||
                string.IsNullOrWhiteSpace(link.Destination?.ComponentId) || string.IsNullOrWhiteSpace(link.Destination?.PortName))
            {
                issues.Add(Error(itemLocation, "Hardware link source and destination must be complete non-null port references."));
            }
            if (link.Parameters is null || link.ExtensionData is null || !double.IsFinite(link.EnergyPerBit) || !double.IsFinite(link.PhysicalLength))
            {
                issues.Add(Error(itemLocation, "Hardware link collections and physical values must be structurally valid."));
            }
            index++;
        }
    }

    private static void ValidatePhysicalArtifacts(
        PhysicalPlacement? placement,
        PhysicalRouting? routing,
        List<TopologyBuildIssue> issues)
    {
        if (placement is null)
        {
            issues.Add(Error("$.actualGraph.placement", "Custom topology authority requires explicit physical placement."));
        }
        else
        {
            if (placement.Origin is null || placement.ComponentCells is null || placement.ComponentPositions is null ||
                placement.FloorplanMetadata is null || placement.ExtensionData is null ||
                (placement.ComponentCells?.Any(item => item.Value is null) ?? false) ||
                (placement.ComponentPositions?.Any(item => item.Value is null) ?? false) ||
                !double.IsFinite(placement.CellWidthMicrometers) || !double.IsFinite(placement.CellHeightMicrometers))
            {
                issues.Add(Error("$.actualGraph.placement", "Custom physical placement and all of its collections and coordinates must be structurally valid."));
            }
        }
        if (routing is null)
        {
            issues.Add(Error("$.actualGraph.routing", "Custom topology authority requires explicit physical routing."));
        }
        else if (routing.Routes is null || routing.ExtensionData is null)
        {
            issues.Add(Error("$.actualGraph.routing", "Custom physical routing collections cannot be null."));
        }
        else
        {
            for (var index = 0; index < routing.Routes.Count; index++)
            {
                var route = routing.Routes[index];
                if (route is null || route.LayerId is null || route.Path is null || route.ExtensionData is null ||
                    (route.Path?.Any(point => point is null || !double.IsFinite(point.X) || !double.IsFinite(point.Y)) ?? false))
                {
                    issues.Add(Error($"$.actualGraph.routing.routes[{index}]", "Custom physical routes, layers, paths, and points must be structurally valid."));
                }
            }
        }
    }

    private static void ValidateManifestShape(
        IReadOnlyList<TopologyManifestComponent> components,
        IReadOnlyList<TopologyManifestLink> links,
        IReadOnlyList<Phase8ACustomTopologyLinkEndpoint> endpoints,
        List<TopologyBuildIssue> issues)
    {
        for (var index = 0; index < components.Count; index++)
        {
            var component = components[index];
            if (component is null || component.Coordinate is null || component.ChildComponentIds is null ||
                (component.ChildComponentIds?.Any(item => item is null) ?? false))
            {
                issues.Add(Error($"$.components[{index}]", "Custom manifest component descriptors cannot be null or structurally incomplete."));
            }
        }
        for (var index = 0; index < links.Count; index++)
        {
            var link = links[index];
            if (link is null || string.IsNullOrWhiteSpace(link.LinkId) || string.IsNullOrWhiteSpace(link.SourceComponentId) ||
                string.IsNullOrWhiteSpace(link.DestinationComponentId))
            {
                issues.Add(Error($"$.links[{index}]", "Custom manifest link descriptors cannot be null or structurally incomplete."));
            }
        }
        for (var index = 0; index < endpoints.Count; index++)
        {
            var endpoint = endpoints[index];
            if (endpoint is null || string.IsNullOrWhiteSpace(endpoint.LinkId) || string.IsNullOrWhiteSpace(endpoint.SourceComponentId) ||
                string.IsNullOrWhiteSpace(endpoint.SourcePortName) || string.IsNullOrWhiteSpace(endpoint.DestinationComponentId) ||
                string.IsNullOrWhiteSpace(endpoint.DestinationPortName))
            {
                issues.Add(Error($"$.exactLinkEndpoints[{index}]", "Custom exact-link endpoint descriptors cannot be null or structurally incomplete."));
            }
        }
    }

    private static bool HasExactPort(
        IReadOnlyDictionary<string, HardwareComponent> components,
        string componentId,
        string portName) =>
        components.TryGetValue(componentId, out var component) &&
        component.Ports.Any(port => string.Equals(port.Name, portName, StringComparison.Ordinal));

    private static bool PositiveFinite(double value) => double.IsFinite(value) && value > 0;

    private static TopologyBuildIssue Error(string location, string message) => new(
        TopologyBuildIssueCodes.InvalidManifest, ValidationSeverity.Error, location, message);
    private static Phase8ACustomTopologyAuthorityResult Failure(string location, string message) => new(null, [Error(location, message)]);
}