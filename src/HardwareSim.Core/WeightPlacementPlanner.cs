using System.Collections.ObjectModel;
using System.Text.Json;

namespace HardwareSim.Core;

/// <summary>Defines stable planned weight-lifecycle event identifiers.</summary>
public static class Phase8AWeightLifecycleKinds
{
    /// <summary>Plans transfer of a weight artifact to the declared preload port.</summary>
    public const string Preload = "weight-preload";
    /// <summary>Plans a storage write without mutating mapping-time written ranges.</summary>
    public const string Write = "weight-write";
    /// <summary>Plans the explicit commit required before compute visibility.</summary>
    public const string Commit = "weight-commit";
    /// <summary>Records a later consumer reusing an already resident exact tile.</summary>
    public const string Reuse = "weight-reuse";
}

/// <summary>Defines stable hard-failure codes for Phase 8A weight placement.</summary>
public static class Phase8AWeightPlacementIssueCodes
{
    /// <summary>A required request, assignment, tile, or explicit use was absent or inconsistent.</summary>
    public const string InvalidInput = "MappingCapacityWeightPlacementInputInvalid";
    /// <summary>An artifact hash or exact reuse identity was invalid.</summary>
    public const string InvalidArtifact = "MappingCapacityWeightArtifactInvalid";
    /// <summary>A component storage selector was ambiguous.</summary>
    public const string AmbiguousStorageSelector = "MappingCapacityStorageSelectorAmbiguous";
    /// <summary>No declared storage resource satisfied all hard constraints.</summary>
    public const string StorageUnavailable = "MappingCapacityWeightStorageUnavailable";
    /// <summary>An exact resident tile was requested again from storage that forbids reuse.</summary>
    public const string ReuseNotSupported = "MappingCapacityWeightReuseNotSupported";
    /// <summary>A declared resident-slot limit was exhausted.</summary>
    public const string ResidentSlotsExceeded = "MappingCapacityResidentSlotsExceeded";
    /// <summary>No aligned contiguous range could hold the exact weight tile.</summary>
    public const string StorageCapacityExceeded = "MappingCapacityWeightStorageExceeded";
    /// <summary>An initial storage map was inconsistent with static mapping-stage planning.</summary>
    public const string InitialStorageStateInvalid = "MappingCapacityInitialStorageStateInvalid";
    /// <summary>Checked size or address arithmetic overflowed.</summary>
    public const string ArithmeticOverflow = "MappingCapacityWeightArithmeticOverflow";
}

/// <summary>Builds the one Core-owned exact resident identity shared by placement, mapping, and apply.</summary>
public static class Phase8AWeightResidencyKey
{
    /// <summary>Gets the canonical algorithm identifier used to hash inline weight values when no artifact hash is supplied.</summary>
    public const string InlineArtifactHashAlgorithm = "sha256/phase8a-inline-weight-artifact/v1";

    /// <summary>Resolves the normalized artifact-hash input, preferring a declared value and otherwise hashing inline values canonically.</summary>
    /// <param name="artifact">Weight artifact containing either a declared artifact-hash value or non-empty inline values.</param>
    /// <returns>The trimmed lowercase declared value, or the canonical SHA-256 digest computed from the inline values.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="artifact"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Neither a declared artifact digest nor inline values are present.</exception>
    public static string ResolveArtifactHash(ReferenceMappingTensorArtifact artifact)
    {
        if (artifact is null) throw new ArgumentNullException(nameof(artifact));
        if (!string.IsNullOrWhiteSpace(artifact.ArtifactHash)) return artifact.ArtifactHash.Trim().ToLowerInvariant();
        if (artifact.Values.Count == 0) throw new ArgumentException("An exact weight artifact hash or inline payload is required.", nameof(artifact));
        var json = JsonSerializer.Serialize(new { algorithm = InlineArtifactHashAlgorithm, values = artifact.Values }, HardwareGraphJson.Options);
        return ComponentExecutionJson.ComputeSha256(ComponentExecutionJson.CanonicalizeJson(json));
    }

    /// <summary>Computes the exact resident identity from an artifact and one lowered weight-tile range, padded shape, and precision.</summary>
    /// <param name="artifact">Weight artifact whose declared digest or inline values define the content identity.</param>
    /// <param name="tile">Lowered weight tile supplying the exact K/N ranges, padded shape, and precision identity.</param>
    /// <returns>The canonical resident identity shared by placement, mapping, and apply.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="artifact"/> or <paramref name="tile"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The artifact has neither a declared hash nor inline values, or its resolved hash is not a 64-character hexadecimal SHA-256 digest.</exception>
    public static string Compute(ReferenceMappingTensorArtifact artifact, Phase8ALoweredOperandTile tile) =>
        Compute(ResolveArtifactHash(artifact), tile);

    /// <summary>Computes the exact resident identity from an artifact digest and one lowered weight-tile range, padded shape, and precision.</summary>
    /// <param name="artifactHash">SHA-256 artifact digest; surrounding whitespace and hexadecimal letter casing are normalized.</param>
    /// <param name="tile">Lowered weight tile supplying the exact K/N ranges, padded shape, and precision identity.</param>
    /// <returns>The canonical resident identity shared by placement, mapping, and apply.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="tile"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="artifactHash"/> is not a 64-character hexadecimal SHA-256 digest.</exception>
    public static string Compute(string artifactHash, Phase8ALoweredOperandTile tile)
    {
        if (tile is null) throw new ArgumentNullException(nameof(tile));
        var normalizedHash = artifactHash?.Trim().ToLowerInvariant() ?? "";
        if (normalizedHash.Length != 64 || normalizedHash.Any(character => !(character is >= '0' and <= '9' or >= 'a' and <= 'f')))
            throw new ArgumentException("Weight artifact identity must be a lowercase SHA-256 digest.", nameof(artifactHash));
        return string.Join("|",
            "artifact=" + normalizedHash,
            $"k={tile.KRange.Offset}:{tile.KRange.Extent}",
            $"n={tile.NRange.Offset}:{tile.NRange.Extent}",
            "shape=" + string.Join("x", tile.PaddedShape.Dimensions),
            "dtype=" + tile.PrecisionId);
    }

    /// <summary>Computes one exact physical residency identity without collapsing replicas at different selectors or addresses.</summary>
    public static string ComputePlacementId(
        string componentId,
        string resourceId,
        long addressBits,
        long sizeBits,
        string reuseKey)
    {
        if (string.IsNullOrWhiteSpace(componentId))
            throw new ArgumentException("A storage component identity is required.", nameof(componentId));
        if (string.IsNullOrWhiteSpace(resourceId))
            throw new ArgumentException("A storage resource identity is required.", nameof(resourceId));
        if (addressBits < 0) throw new ArgumentOutOfRangeException(nameof(addressBits));
        if (sizeBits <= 0) throw new ArgumentOutOfRangeException(nameof(sizeBits));
        if (string.IsNullOrWhiteSpace(reuseKey))
            throw new ArgumentException("An exact weight residency key is required.", nameof(reuseKey));
        return "weight-placement:" + ComponentExecutionJson.ComputeSha256(string.Join("\u001f",
            componentId,
            resourceId,
            addressBits.ToString(System.Globalization.CultureInfo.InvariantCulture),
            sizeBits.ToString(System.Globalization.CultureInfo.InvariantCulture),
            reuseKey));
    }
}

/// <summary>Explicitly binds one weight artifact to the lowered operation-tile consumers that use it.</summary>
public sealed class Phase8AWeightArtifactUse
{
    /// <summary>Creates one immutable explicit artifact use.</summary>
    public Phase8AWeightArtifactUse(
        string operationId,
        string tensorId,
        string artifactHash,
        string precisionId,
        IEnumerable<string>? consumerOperationTileIds)
    {
        OperationId = operationId?.Trim() ?? "";
        TensorId = tensorId?.Trim() ?? "";
        ArtifactHash = artifactHash?.Trim().ToLowerInvariant() ?? "";
        PrecisionId = precisionId?.Trim() ?? "";
        ConsumerOperationTileIds = new ReadOnlyCollection<string>((consumerOperationTileIds ?? [])
            .Select(value => value?.Trim() ?? "")
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToList());
    }

    /// <summary>Gets the source operation identity.</summary>
    public string OperationId { get; }
    /// <summary>Gets the source weight tensor identity.</summary>
    public string TensorId { get; }
    /// <summary>Gets the exact lowercase SHA-256 artifact hash.</summary>
    public string ArtifactHash { get; }
    /// <summary>Gets the exact digital precision identity.</summary>
    public string PrecisionId { get; }
    /// <summary>Gets explicitly declared lowered operation-tile consumers.</summary>
    public IReadOnlyList<string> ConsumerOperationTileIds { get; }
}

/// <summary>Requests deterministic static weight placement over cloned StorageMap state.</summary>
public sealed class Phase8AWeightPlacementRequest
{
    private readonly IReadOnlyDictionary<string, StorageMap> _initialStorageMaps;

    /// <summary>Creates one immutable weight-placement request.</summary>
    public Phase8AWeightPlacementRequest(
        CapabilitySnapshot capabilitySnapshot,
        IEnumerable<OperationTileAssignment>? assignments,
        IEnumerable<Phase8ALoweredOperandTile>? weightTiles,
        IEnumerable<Phase8AWeightArtifactUse>? artifactUses,
        IReadOnlyDictionary<string, StorageMap>? initialStorageMaps = null)
    {
        CapabilitySnapshot = capabilitySnapshot ?? throw new ArgumentNullException(nameof(capabilitySnapshot));
        Assignments = new ReadOnlyCollection<OperationTileAssignment>((assignments ?? [])
            .OrderBy(item => item.AssignmentId, StringComparer.Ordinal)
            .ToList());
        WeightTiles = new ReadOnlyCollection<Phase8ALoweredOperandTile>((weightTiles ?? [])
            .OrderBy(item => item.TileId, StringComparer.Ordinal)
            .ToList());
        ArtifactUses = new ReadOnlyCollection<Phase8AWeightArtifactUse>((artifactUses ?? [])
            .OrderBy(item => item.OperationId, StringComparer.Ordinal)
            .ThenBy(item => item.TensorId, StringComparer.Ordinal)
            .ThenBy(item => item.ArtifactHash, StringComparer.Ordinal)
            .ToList());

        var maps = new SortedDictionary<string, StorageMap>(StringComparer.Ordinal);
        foreach (var pair in initialStorageMaps ?? new Dictionary<string, StorageMap>())
        {
            maps[pair.Key] = pair.Value?.Clone() ?? throw new ArgumentException("Initial storage maps cannot contain null values.", nameof(initialStorageMaps));
        }
        _initialStorageMaps = new ReadOnlyDictionary<string, StorageMap>(maps);
    }

    /// <summary>Gets the exact frozen capability snapshot.</summary>
    public CapabilitySnapshot CapabilitySnapshot { get; }
    /// <summary>Gets assignments in stable assignment-id order.</summary>
    public IReadOnlyList<OperationTileAssignment> Assignments { get; }
    /// <summary>Gets lowered weight tiles in stable tile-id order.</summary>
    public IReadOnlyList<Phase8ALoweredOperandTile> WeightTiles { get; }
    /// <summary>Gets explicit artifact uses in canonical order.</summary>
    public IReadOnlyList<Phase8AWeightArtifactUse> ArtifactUses { get; }

    internal IReadOnlyDictionary<string, StorageMap> CloneInitialStorageMaps() =>
        new ReadOnlyDictionary<string, StorageMap>(_initialStorageMaps.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.Clone(),
            StringComparer.Ordinal));
}

/// <summary>Records one planned lifecycle step; it never represents an already executed write.</summary>
public sealed class Phase8AWeightLifecycleEvent
{
    /// <summary>Creates one immutable planned lifecycle step with explicit transaction provenance.</summary>
    public Phase8AWeightLifecycleEvent(
        int sequence,
        string stepId,
        string kindId,
        string placementId,
        string consumerOperationTileId,
        string targetComponentId,
        string storageResourceId,
        string storageLevelId,
        string preloadPortId,
        long addressBits,
        long payloadBits,
        long allocatedBits,
        string commitModeId,
        string reuseKey,
        IEnumerable<string>? dependencyStepIds)
    {
        Sequence = sequence;
        StepId = stepId;
        KindId = kindId;
        PlacementId = placementId;
        ConsumerOperationTileId = consumerOperationTileId;
        TargetComponentId = targetComponentId;
        StorageResourceId = storageResourceId;
        StorageLevelId = storageLevelId;
        PreloadPortId = preloadPortId;
        AddressBits = addressBits;
        PayloadBits = payloadBits;
        AllocatedBits = allocatedBits;
        CommitModeId = commitModeId;
        ReuseKey = reuseKey;
        DependencyStepIds = new ReadOnlyCollection<string>((dependencyStepIds ?? []).ToList());
    }

    /// <summary>Gets stable zero-based lifecycle order.</summary>
    public int Sequence { get; }
    /// <summary>Gets the stable step identity used by later dependencies.</summary>
    public string StepId { get; }
    /// <summary>Gets the stable lifecycle event identity.</summary>
    public string KindId { get; }
    /// <summary>Gets the related immutable operand placement identity.</summary>
    public string PlacementId { get; }
    /// <summary>Gets the consumer causing this planned step.</summary>
    public string ConsumerOperationTileId { get; }
    /// <summary>Gets the exact target component.</summary>
    public string TargetComponentId { get; }
    /// <summary>Gets the exact selected storage resource.</summary>
    public string StorageResourceId { get; }
    /// <summary>Gets the exact selected storage level.</summary>
    public string StorageLevelId { get; }
    /// <summary>Gets the exact declared preload port.</summary>
    public string PreloadPortId { get; }
    /// <summary>Gets the lowest-fit allocation address in bits.</summary>
    public long AddressBits { get; }
    /// <summary>Gets actual tensor payload bits before allocation rounding.</summary>
    public long PayloadBits { get; }
    /// <summary>Gets allocated bits after storage granularity rounding.</summary>
    public long AllocatedBits { get; }
    /// <summary>Gets the exact declared commit contract.</summary>
    public string CommitModeId { get; }
    /// <summary>Gets exact artifact/range/shape/dtype resident identity.</summary>
    public string ReuseKey { get; }
    /// <summary>Gets immutable prerequisite step identities.</summary>
    public IReadOnlyList<string> DependencyStepIds { get; }
}

/// <summary>Binds one explicit consumer to its selected placement and preload contract.</summary>
/// <param name="ConsumerOperationTileId">Lowered operation-tile consumer identity.</param>
/// <param name="PlacementId">Selected operand placement identity.</param>
/// <param name="StorageResourceId">Selected capability resource identity.</param>
/// <param name="PreloadPortId">Selected declared preload port.</param>
public sealed record Phase8AWeightPlacementBinding(
    string ConsumerOperationTileId,
    string PlacementId,
    string StorageResourceId,
    string PreloadPortId);

/// <summary>Contains immutable planned weight placements, lifecycle, and defensive StorageMap snapshots.</summary>
public sealed class Phase8AWeightPlacementPlan
{
    private readonly SortedDictionary<string, StorageMapSnapshot> _storageMaps;

    internal Phase8AWeightPlacementPlan(
        IEnumerable<OperandPlacement> placements,
        IEnumerable<Phase8AWeightLifecycleEvent> lifecycleEvents,
        IEnumerable<Phase8AWeightPlacementBinding> bindings,
        IReadOnlyDictionary<string, StorageMap> storageMaps,
        string canonicalHash)
    {
        Placements = new ReadOnlyCollection<OperandPlacement>(placements.OrderBy(item => item.PlacementId, StringComparer.Ordinal).ToList());
        LifecycleEvents = new ReadOnlyCollection<Phase8AWeightLifecycleEvent>(lifecycleEvents.OrderBy(item => item.Sequence).ToList());
        Bindings = new ReadOnlyCollection<Phase8AWeightPlacementBinding>(bindings.OrderBy(item => item.ConsumerOperationTileId, StringComparer.Ordinal).ToList());
        _storageMaps = new SortedDictionary<string, StorageMapSnapshot>(StringComparer.Ordinal);
        foreach (var pair in storageMaps.OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            _storageMaps[pair.Key] = CloneSnapshot(pair.Value.ToSnapshot());
        }
        CanonicalHash = canonicalHash;
    }

    /// <summary>Gets unique exact-tile placements in canonical identity order.</summary>
    public IReadOnlyList<OperandPlacement> Placements { get; }
    /// <summary>Gets planned Preload, Write, Commit, and Reuse steps in exact order.</summary>
    public IReadOnlyList<Phase8AWeightLifecycleEvent> LifecycleEvents { get; }
    /// <summary>Gets explicit consumer-to-placement bindings.</summary>
    public IReadOnlyList<Phase8AWeightPlacementBinding> Bindings { get; }
    /// <summary>Gets defensive storage snapshots; mapping-stage planned writes do not appear as written ranges.</summary>
    public IReadOnlyDictionary<string, StorageMapSnapshot> StorageMaps =>
        new ReadOnlyDictionary<string, StorageMapSnapshot>(_storageMaps.ToDictionary(
            pair => pair.Key,
            pair => CloneSnapshot(pair.Value),
            StringComparer.Ordinal));
    /// <summary>Gets the deterministic semantic SHA-256 plan hash.</summary>
    public string CanonicalHash { get; }

    private static StorageMapSnapshot CloneSnapshot(StorageMapSnapshot source) => new()
    {
        StorageId = source.StorageId,
        CapacityBits = source.CapacityBits,
        Allocations = source.Allocations.Select(item => new StorageAllocationSnapshot
        {
            TileId = item.TileId,
            BaseAddressBits = item.BaseAddressBits,
            SizeBits = item.SizeBits
        }).ToList(),
        WrittenRanges = source.WrittenRanges.Select(item => new StorageWrittenRangeSnapshot
        {
            TileId = item.TileId,
            BaseAddressBits = item.BaseAddressBits,
            SizeBits = item.SizeBits,
            Provenance = item.Provenance
        }).ToList()
    };
}

/// <summary>Returns a complete weight-placement plan or deterministic structured hard failures.</summary>
public sealed class Phase8AWeightPlacementResult
{
    internal Phase8AWeightPlacementResult(Phase8AWeightPlacementPlan? plan, IEnumerable<WorkloadMappingV2Issue> issues)
    {
        Plan = plan;
        Issues = new ReadOnlyCollection<WorkloadMappingV2Issue>(issues
            .OrderBy(item => item.Location, StringComparer.Ordinal)
            .ThenBy(item => item.Code, StringComparer.Ordinal)
            .ThenBy(item => item.RelatedId, StringComparer.Ordinal)
            .ToList());
    }

    /// <summary>Gets the complete plan on success.</summary>
    public Phase8AWeightPlacementPlan? Plan { get; }
    /// <summary>Gets immutable structured failures.</summary>
    public IReadOnlyList<WorkloadMappingV2Issue> Issues { get; }
    /// <summary>Gets whether a complete all-or-nothing plan was produced.</summary>
    public bool IsSuccess => Plan is not null && Issues.All(issue => issue.Severity != ValidationSeverity.Error);
}

/// <summary>Plans deterministic resident weight placement using declared capabilities and cloned StorageMaps.</summary>
public static class Phase8AWeightPlacementPlanner
{
    /// <summary>Creates the unambiguous StorageMap identity for one component resource selector.</summary>
    public static string StorageMapId(string componentId, string resourceId) =>
        $"{componentId?.Trim() ?? ""}::{resourceId?.Trim() ?? ""}";

    /// <summary>Plans exact weight allocations and lifecycle without executing any storage writes.</summary>
    public static Phase8AWeightPlacementResult Plan(Phase8AWeightPlacementRequest request)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        var issues = ValidateInput(request);
        if (issues.Count > 0)
        {
            return Failure(issues);
        }

        try
        {
            var assignments = request.Assignments.ToDictionary(item => item.AssignmentId, StringComparer.Ordinal);
            var tiles = request.WeightTiles.ToDictionary(item => item.TileId, StringComparer.Ordinal);
            var components = request.CapabilitySnapshot.Components.ToDictionary(item => item.ComponentId, StringComparer.Ordinal);
            var workingMaps = request.CloneInitialStorageMaps().ToDictionary(pair => pair.Key, pair => pair.Value.Clone(), StringComparer.Ordinal);
            var placements = new List<OperandPlacement>();
            var lifecycle = new List<Phase8AWeightLifecycleEvent>();
            var bindings = new List<Phase8AWeightPlacementBinding>();
            var resident = new Dictionary<string, ResidentPlacement>(StringComparer.Ordinal);
            var sequence = 0;
            var reuseDemand = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var declaredUse in request.ArtifactUses)
            {
                foreach (var declaredConsumerId in declaredUse.ConsumerOperationTileIds)
                {
                    var declaredAssignment = assignments[declaredConsumerId];
                    var declaredWeightTileId = declaredAssignment.OperandTileIds.Single(tileId =>
                        tiles.TryGetValue(tileId, out var candidate) &&
                        string.Equals(candidate.RoleId, Phase8ATensorRoleIds.Weight, StringComparison.Ordinal));
                    var declaredKey = declaredAssignment.TargetComponentId + "\u001f" + Phase8AWeightResidencyKey.Compute(declaredUse.ArtifactHash, tiles[declaredWeightTileId]);
                    reuseDemand[declaredKey] = reuseDemand.TryGetValue(declaredKey, out var count) ? count + 1 : 1;
                }
            }

            foreach (var use in request.ArtifactUses)
            {
                foreach (var consumerId in use.ConsumerOperationTileIds)
                {
                    var assignment = assignments[consumerId];
                    var weightTileId = assignment.OperandTileIds.Single(tileId =>
                        tiles.TryGetValue(tileId, out var candidate) &&
                        string.Equals(candidate.RoleId, Phase8ATensorRoleIds.Weight, StringComparison.Ordinal));
                    var tile = tiles[weightTileId];
                    var reuseKey = Phase8AWeightResidencyKey.Compute(use.ArtifactHash, tile);
                    var residentKey = assignment.TargetComponentId + "\u001f" + reuseKey;
                    var requiresReuse = reuseDemand[residentKey] > 1;
                    if (resident.TryGetValue(residentKey, out var existing))
                    {
                        if (!existing.Storage.SupportsReuse)
                        {
                            return Failure(Issue(Phase8AWeightPlacementIssueCodes.ReuseNotSupported, "$.artifactUses",
                                $"Storage resource '{existing.Storage.ResourceId}' does not permit reuse of exact resident tile '{tile.TileId}'.", consumerId));
                        }
                        bindings.Add(new Phase8AWeightPlacementBinding(consumerId, existing.Placement.PlacementId, existing.Storage.ResourceId, existing.Storage.PreloadPortId));
                        lifecycle.Add(CreateLifecycle(sequence++, existing.Placement.PlacementId + ":reuse:" + ComponentExecutionJson.ComputeSha256(consumerId),
                            Phase8AWeightLifecycleKinds.Reuse, existing.Placement, consumerId, existing.Storage, existing.PayloadBits,
                            reuseKey, [existing.ReadyStepId]));
                        continue;
                    }

                    var component = components[assignment.TargetComponentId];
                    var rawBits = RawSizeBits(tile);
                    var hardCandidates = component.StorageCapabilities
                        .Where(storage =>
                            storage.SupportedOperandRoleIds.Contains(Phase8ATensorRoleIds.Weight, StringComparer.Ordinal) &&
                            storage.SupportedPrecisionIds.Contains(tile.PrecisionId, StringComparer.Ordinal) &&
                            !string.IsNullOrWhiteSpace(storage.PreloadPortId) &&
                            component.Ports.Any(port => string.Equals(port.PortId, storage.PreloadPortId, StringComparison.Ordinal) &&
                                                        string.Equals(port.DirectionId, "input", StringComparison.Ordinal) &&
                                                        string.Equals(port.SemanticRoleId, Phase8ATensorRoleIds.Weight, StringComparison.Ordinal) &&
                                                        string.Equals(port.DataTypeId, HardwareDataType.Tensor.ToString(), StringComparison.Ordinal) &&
                                                        string.Equals(port.PrecisionId, tile.PrecisionId, StringComparison.Ordinal)) &&
                            storage.CapacityBits >= rawBits &&
                            storage.AlignmentBits > 0 &&
                            storage.AllocationGranularityBits > 0 &&
                            storage.ResidentSlots > 0 &&
                            storage.WriteBandwidthBitsPerCycle > 0 &&
                            !string.IsNullOrWhiteSpace(storage.CommitModeId) &&
                            !string.IsNullOrWhiteSpace(storage.SourceContractHash))
                        .ToArray();
                    if (requiresReuse && hardCandidates.Length > 0 && hardCandidates.All(storage => !storage.SupportsReuse))
                    {
                        return Failure(Issue(Phase8AWeightPlacementIssueCodes.ReuseNotSupported, "$.capabilitySnapshot.components.storageCapabilities",
                            $"Component '{component.ComponentId}' has no eligible storage resource that permits the declared exact-tile reuse.", consumerId));
                    }
                    var candidates = hardCandidates
                        .Where(storage => !requiresReuse || storage.SupportsReuse)
                        .OrderBy(storage => storage.StorageLevelId, StringComparer.Ordinal)
                        .ThenBy(storage => storage.ResourceId, StringComparer.Ordinal)
                        .ThenBy(storage => storage.SourceContractHash, StringComparer.Ordinal)
                        .ToArray();

                    if (candidates.Length == 0)
                    {
                        return Failure(Issue(Phase8AWeightPlacementIssueCodes.StorageUnavailable, "$.capabilitySnapshot.components.storageCapabilities",
                            $"Component '{component.ComponentId}' has no declared weight storage satisfying role, precision, port, capacity, alignment, granularity, and commit constraints.", consumerId));
                    }

                    SelectedStorage? selected = null;
                    var sawSlotFailure = false;
                    var sawCapacityFailure = false;
                    foreach (var storage in candidates)
                    {
                        var mapId = StorageMapId(component.ComponentId, storage.ResourceId);
                        var currentMap = workingMaps.TryGetValue(mapId, out var supplied)
                            ? supplied
                            : new StorageMap(mapId, storage.CapacityBits);
                        if (currentMap.CapacityBits != storage.CapacityBits ||
                            !string.Equals(currentMap.StorageId, mapId, StringComparison.Ordinal) ||
                            currentMap.WrittenRanges.Count != 0)
                        {
                            return Failure(Issue(Phase8AWeightPlacementIssueCodes.InitialStorageStateInvalid, "$.initialStorageMaps",
                                $"StorageMap '{mapId}' must match declared capacity and identity and contain no executed written ranges during mapping.", consumerId));
                        }
                        if (currentMap.Allocations.Count >= storage.ResidentSlots)
                        {
                            sawSlotFailure = true;
                            continue;
                        }

                        var allocationBits = AlignUp(rawBits, storage.AllocationGranularityBits);
                        if (allocationBits > storage.CapacityBits)
                        {
                            sawCapacityFailure = true;
                            continue;
                        }

                        var trial = currentMap.Clone();
                        var allocationId = "resident:" + ComponentExecutionJson.ComputeSha256(reuseKey);
                        var allocation = trial.Allocate(allocationId, allocationBits, storage.AlignmentBits);
                        if (!allocation.IsSuccess)
                        {
                            sawCapacityFailure = true;
                            continue;
                        }

                        selected = new SelectedStorage(storage, allocation.AddressBits!.Value, allocationBits);
                        workingMaps[mapId] = trial;
                        break;
                    }

                    if (selected is null)
                    {
                        var code = sawCapacityFailure
                            ? Phase8AWeightPlacementIssueCodes.StorageCapacityExceeded
                            : sawSlotFailure
                                ? Phase8AWeightPlacementIssueCodes.ResidentSlotsExceeded
                                : Phase8AWeightPlacementIssueCodes.StorageUnavailable;
                        return Failure(Issue(code, "$.capabilitySnapshot.components.storageCapabilities",
                            $"No eligible storage selector on component '{component.ComponentId}' can reserve the exact resident weight tile.", consumerId));
                    }

                    var placementId = Phase8AWeightResidencyKey.ComputePlacementId(
                        component.ComponentId, selected.Storage.ResourceId,
                        selected.AddressBits, selected.AllocationBits, reuseKey);
                    var commitRequired = !string.Equals(selected.Storage.CommitModeId, "none", StringComparison.OrdinalIgnoreCase);
                    var placement = new OperandPlacement(
                        placementId,
                        use.OperationId,
                        use.TensorId,
                        tile.TileId,
                        Phase8ATensorRoleIds.Weight,
                        component.ComponentId,
                        selected.Storage.StorageLevelId,
                        selected.AddressBits,
                        selected.AllocationBits,
                        selected.Storage.AlignmentBits,
                        "resident",
                        commitRequired ? "preload-write-commit" : "preload-write",
                        "mapping-plan",
                        "last-explicit-consumer",
                        reuseKey,
                        commitRequired);
                    placements.Add(placement);
                    bindings.Add(new Phase8AWeightPlacementBinding(consumerId, placementId, selected.Storage.ResourceId, selected.Storage.PreloadPortId));
                    var preloadStepId = placementId + ":preload";
                    var writeStepId = placementId + ":write";
                    var commitStepId = placementId + ":commit";
                    lifecycle.Add(CreateLifecycle(sequence++, preloadStepId, Phase8AWeightLifecycleKinds.Preload,
                        placement, consumerId, selected.Storage, rawBits, reuseKey, []));
                    lifecycle.Add(CreateLifecycle(sequence++, writeStepId, Phase8AWeightLifecycleKinds.Write,
                        placement, consumerId, selected.Storage, rawBits, reuseKey, [preloadStepId]));
                    var readyStepId = writeStepId;
                    if (commitRequired)
                    {
                        lifecycle.Add(CreateLifecycle(sequence++, commitStepId, Phase8AWeightLifecycleKinds.Commit,
                            placement, consumerId, selected.Storage, rawBits, reuseKey, [writeStepId]));
                        readyStepId = commitStepId;
                    }
                    resident[residentKey] = new ResidentPlacement(placement, selected.Storage, rawBits, readyStepId);
                }
            }

            var hash = ComputeHash(placements, lifecycle, bindings, workingMaps);
            return new Phase8AWeightPlacementResult(new Phase8AWeightPlacementPlan(placements, lifecycle, bindings, workingMaps, hash), []);
        }
        catch (OverflowException)
        {
            return Failure(Issue(Phase8AWeightPlacementIssueCodes.ArithmeticOverflow, "$.weightTiles",
                "Checked weight size, granularity, or address arithmetic overflowed."));
        }
    }

    private static List<WorkloadMappingV2Issue> ValidateInput(Phase8AWeightPlacementRequest request)
    {
        var issues = new List<WorkloadMappingV2Issue>();
        if (request.Assignments.Count == 0 || request.WeightTiles.Count == 0 || request.ArtifactUses.Count == 0)
        {
            issues.Add(Issue(Phase8AWeightPlacementIssueCodes.InvalidInput, "$",
                "Weight placement requires assignments, lowered weight tiles, and explicit artifact uses."));
        }
        foreach (var pair in request.CloneInitialStorageMaps())
        {
            if (pair.Value.WrittenRanges.Count != 0)
            {
                issues.Add(Issue(Phase8AWeightPlacementIssueCodes.InitialStorageStateInvalid, "$.initialStorageMaps",
                    $"Initial StorageMap '{pair.Key}' contains executed written ranges; static mapping must remain planned-only.", pair.Key));
            }
        }
        var assignmentIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var assignment in request.Assignments)
        {
            if (string.IsNullOrWhiteSpace(assignment.AssignmentId) || !assignmentIds.Add(assignment.AssignmentId))
            {
                issues.Add(Issue(Phase8AWeightPlacementIssueCodes.InvalidInput, "$.assignments", "Assignment identities must be non-empty and unique.", assignment.AssignmentId));
            }
        }

        var tiles = new Dictionary<string, Phase8ALoweredOperandTile>(StringComparer.Ordinal);
        foreach (var tile in request.WeightTiles)
        {
            if (string.IsNullOrWhiteSpace(tile.TileId) || !tiles.TryAdd(tile.TileId, tile) ||
                !string.Equals(tile.RoleId, Phase8ATensorRoleIds.Weight, StringComparison.Ordinal))
            {
                issues.Add(Issue(Phase8AWeightPlacementIssueCodes.InvalidInput, "$.weightTiles", "Weight tiles must have non-empty unique ids and the typed weight role.", tile.TileId));
            }
        }

        var componentIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var component in request.CapabilitySnapshot.Components)
        {
            if (string.IsNullOrWhiteSpace(component.ComponentId) || !componentIds.Add(component.ComponentId))
            {
                issues.Add(Issue(Phase8AWeightPlacementIssueCodes.InvalidInput, "$.capabilitySnapshot.components",
                    "Capability component identities must be non-empty and unique.", component.ComponentId));
            }

            var levels = new HashSet<string>(StringComparer.Ordinal);
            var resources = new HashSet<string>(StringComparer.Ordinal);
            foreach (var storage in component.StorageCapabilities)
            {
                if (string.IsNullOrWhiteSpace(storage.StorageLevelId) || !levels.Add(storage.StorageLevelId) ||
                    string.IsNullOrWhiteSpace(storage.ResourceId) || !resources.Add(storage.ResourceId))
                {
                    issues.Add(Issue(Phase8AWeightPlacementIssueCodes.AmbiguousStorageSelector,
                        "$.capabilitySnapshot.components.storageCapabilities",
                        $"Component '{component.ComponentId}' requires unique non-empty storage level and resource selectors.", component.ComponentId));
                }
                if (storage.CapacityBits <= 0 || storage.AlignmentBits <= 0 || storage.AllocationGranularityBits <= 0 ||
                    storage.ResidentSlots <= 0 || storage.ReadBandwidthBitsPerCycle <= 0 || storage.WriteBandwidthBitsPerCycle <= 0 ||
                    storage.SupportedOperandRoleIds.Count == 0 || storage.SupportedPrecisionIds.Count == 0 ||
                    string.IsNullOrWhiteSpace(storage.PreloadPortId) || string.IsNullOrWhiteSpace(storage.CommitModeId) ||
                    string.IsNullOrWhiteSpace(storage.SourceContractHash) ||
                    !component.Ports.Any(port => string.Equals(port.PortId, storage.PreloadPortId, StringComparison.Ordinal) &&
                                                string.Equals(port.DirectionId, "input", StringComparison.Ordinal) &&
                                                string.Equals(port.SemanticRoleId, Phase8ATensorRoleIds.Weight, StringComparison.Ordinal) &&
                                                string.Equals(port.DataTypeId, HardwareDataType.Tensor.ToString(), StringComparison.Ordinal) &&
                                                storage.SupportedPrecisionIds.Contains(port.PrecisionId, StringComparer.Ordinal)))
                {
                    issues.Add(Issue(Phase8AWeightPlacementIssueCodes.InvalidInput,
                        "$.capabilitySnapshot.components.storageCapabilities",
                        $"Storage resource '{storage.ResourceId}' requires positive capacity/alignment/granularity/slots/read-write bandwidth, roles, and precisions plus declared preload port, commit mode, and source hash.", storage.ResourceId));
                }
            }
        }

        var coveredConsumers = new HashSet<string>(StringComparer.Ordinal);
        var assignmentsById = request.Assignments
            .GroupBy(item => item.AssignmentId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        foreach (var use in request.ArtifactUses)
        {
            if (string.IsNullOrWhiteSpace(use.OperationId) || string.IsNullOrWhiteSpace(use.TensorId) ||
                string.IsNullOrWhiteSpace(use.PrecisionId) || !IsLowerSha256(use.ArtifactHash) ||
                use.ConsumerOperationTileIds.Count == 0)
            {
                issues.Add(Issue(Phase8AWeightPlacementIssueCodes.InvalidArtifact, "$.artifactUses",
                    "Every explicit weight use requires operation, tensor, concrete precision, lowercase SHA-256 artifact hash, and at least one consumer.", use.TensorId));
                continue;
            }
            foreach (var consumerId in use.ConsumerOperationTileIds)
            {
                if (!coveredConsumers.Add(consumerId) || !assignmentsById.TryGetValue(consumerId, out var assignment))
                {
                    issues.Add(Issue(Phase8AWeightPlacementIssueCodes.InvalidInput, "$.artifactUses.consumerOperationTileIds",
                        $"Consumer '{consumerId}' must exist and be covered exactly once.", consumerId));
                    continue;
                }

                var typedWeightTiles = assignment.OperandTileIds
                    .Where(id => tiles.TryGetValue(id, out var tile) &&
                                 string.Equals(tile.RoleId, Phase8ATensorRoleIds.Weight, StringComparison.Ordinal))
                    .ToArray();
                var matchingWeightTiles = typedWeightTiles
                    .Where(id => tiles.TryGetValue(id, out var tile) &&
                                 string.Equals(tile.TensorId, use.TensorId, StringComparison.Ordinal) &&
                                 string.Equals(tile.PrecisionId, use.PrecisionId, StringComparison.Ordinal))
                    .ToArray();
                if (!string.Equals(assignment.OperationId, use.OperationId, StringComparison.Ordinal) ||
                    typedWeightTiles.Length != 1 || matchingWeightTiles.Length != 1)
                {
                    issues.Add(Issue(Phase8AWeightPlacementIssueCodes.InvalidInput, "$.artifactUses",
                        $"Consumer '{consumerId}' must bind exactly one matching typed weight tile for the explicit operation/tensor/precision use.", consumerId));
                }
            }
        }

        foreach (var assignment in request.Assignments)
        {
            if (!coveredConsumers.Contains(assignment.AssignmentId))
            {
                issues.Add(Issue(Phase8AWeightPlacementIssueCodes.InvalidInput, "$.artifactUses",
                    $"Assignment '{assignment.AssignmentId}' has no explicit weight artifact use.", assignment.AssignmentId));
            }
            if (!request.CapabilitySnapshot.Components.Any(component =>
                    string.Equals(component.ComponentId, assignment.TargetComponentId, StringComparison.Ordinal)))
            {
                issues.Add(Issue(Phase8AWeightPlacementIssueCodes.InvalidInput, "$.assignments",
                    $"Assignment target '{assignment.TargetComponentId}' is absent from the capability snapshot.", assignment.AssignmentId));
            }
        }

        return issues;
    }

    private static long RawSizeBits(Phase8ALoweredOperandTile tile)
    {
        if (!Enum.TryParse<PrecisionKind>(tile.PrecisionId, false, out var precision) ||
            !PrecisionModel.TryGetDigitalBitWidth(precision, out var bitWidth))
        {
            throw new OverflowException();
        }

        var elements = 1L;
        foreach (var dimension in tile.PaddedShape.Dimensions)
        {
            if (dimension <= 0)
            {
                throw new OverflowException();
            }
            elements = checked(elements * dimension);
        }
        return checked(elements * bitWidth);
    }


    private static long AlignUp(long value, long alignment)
    {
        var remainder = value % alignment;
        return remainder == 0 ? value : checked(value + alignment - remainder);
    }
    private static Phase8AWeightLifecycleEvent CreateLifecycle(
        int sequence,
        string stepId,
        string kindId,
        OperandPlacement placement,
        string consumerId,
        ComponentStorageCapabilitySnapshot storage,
        long payloadBits,
        string reuseKey,
        IEnumerable<string> dependencies) => new(
            sequence,
            stepId,
            kindId,
            placement.PlacementId,
            consumerId,
            placement.StorageComponentId,
            storage.ResourceId,
            storage.StorageLevelId,
            storage.PreloadPortId,
            placement.AddressBits,
            payloadBits,
            placement.SizeBits,
            storage.CommitModeId,
            reuseKey,
            dependencies);
    private static string ComputeHash(
        IReadOnlyList<OperandPlacement> placements,
        IReadOnlyList<Phase8AWeightLifecycleEvent> lifecycle,
        IReadOnlyList<Phase8AWeightPlacementBinding> bindings,
        IReadOnlyDictionary<string, StorageMap> maps)
    {
        var json = JsonSerializer.Serialize(new
        {
            algorithm = "sha256/phase8a-weight-placement/v1",
            placements = placements.OrderBy(item => item.PlacementId, StringComparer.Ordinal),
            lifecycle = lifecycle.OrderBy(item => item.Sequence),
            bindings = bindings.OrderBy(item => item.ConsumerOperationTileId, StringComparer.Ordinal),
            storageMaps = maps.OrderBy(item => item.Key, StringComparer.Ordinal).Select(item => new
            {
                id = item.Key,
                snapshot = item.Value.ToSnapshot()
            })
        }, HardwareGraphJson.Options);
        return ComponentExecutionJson.ComputeSha256(ComponentExecutionJson.CanonicalizeJson(json));
    }

    private static bool IsLowerSha256(string value) =>
        value.Length == 64 && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static Phase8AWeightPlacementResult Failure(params WorkloadMappingV2Issue[] issues) =>
        new(null, issues);

    private static Phase8AWeightPlacementResult Failure(IEnumerable<WorkloadMappingV2Issue> issues) =>
        new(null, issues);

    private static WorkloadMappingV2Issue Issue(string code, string location, string message, string? relatedId = null) =>
        new(code, ValidationSeverity.Error, location, message, relatedId);

    private sealed record SelectedStorage(
        ComponentStorageCapabilitySnapshot Storage,
        long AddressBits,
        long AllocationBits);

    private sealed record ResidentPlacement(
        OperandPlacement Placement,
        ComponentStorageCapabilitySnapshot Storage,
        long PayloadBits,
        string ReadyStepId);
}
