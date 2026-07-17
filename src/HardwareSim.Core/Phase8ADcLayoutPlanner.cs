using System.Text.Json;

namespace HardwareSim.Core;

/// <summary>Builds deterministic D/C placement and reduction groups without creating runtime state.</summary>
public static class Phase8ADcLayoutPlanner
{
    private const int CanonicalPeRows = 32;
    private const int CanonicalPeColumns = 32;
    private const int MaximumOperationTiles = 1_000_000;

    /// <summary>Resolves one all-or-nothing canonical layout.</summary>
    public static Phase8ADcLayoutResult Plan(Phase8ADcLayoutRequest? request)
    {
        var issues = Validate(request);
        if (issues.Count > 0) return Failure(issues);

        try
        {
            var normalized = request! with { };
            var kTileCount = checked(normalized.K / normalized.PeRows);
            var nShardCount = checked(normalized.N / normalized.PeColumns);
            var divisionTileCount = checked(normalized.WeightRowDivisionSize / normalized.PeRows);
            var divisionCount = checked(normalized.K / normalized.WeightRowDivisionSize);
            var operationTileCount = checked(kTileCount * nShardCount);
            var requiredClusterCount = CeilingDiv(operationTileCount, normalized.ClusterSize);
            var clusterCount = normalized.ClusterCount ?? requiredClusterCount;
            normalized = normalized with { ClusterCount = clusterCount };

            if ((long)clusterCount * normalized.ClusterSize < operationTileCount)
                return Failure(new Phase8ADcLayoutIssue(
                    Phase8ADcLayoutIssueCodes.CapacityExceeded,
                    "$.clusterCount",
                    "The declared cluster inventory cannot hold every operation tile exactly once."));

            var nShards = Enumerable.Range(0, nShardCount)
                .Select(index => new Phase8ADcNShard(
                    NShardId(index),
                    index,
                    new MappingIndexRange(checked((long)index * normalized.PeColumns), normalized.PeColumns),
                    new MappingIndexRange(checked((long)index * normalized.PeColumns), normalized.PeColumns)))
                .ToArray();
            var occupancy = Enumerable.Range(0, clusterCount)
                .Select(_ => new bool[normalized.ClusterSize])
                .ToArray();
            var assignmentIdsByCluster = Enumerable.Range(0, clusterCount)
                .Select(_ => new List<string>())
                .ToArray();
            var divisions = new List<Phase8ADcDivision>();
            var blocks = new List<Phase8ADcBlock>();
            var fragments = new List<Phase8ADcClusterFragment>();
            var assignments = new List<Phase8ADcPeAssignment>();
            var localGroups = new List<Phase8ADcLocalReductionGroup>();

            for (var divisionIndex = 0; divisionIndex < divisionCount; divisionIndex++)
            {
                var divisionKTileStart = checked(divisionIndex * divisionTileCount);
                var divisionKOffset = checked((long)divisionKTileStart * normalized.PeRows);
                var divisionKRange = new MappingIndexRange(divisionKOffset, normalized.WeightRowDivisionSize);
                var divisionBlockIds = new List<string>();

                foreach (var shard in nShards)
                {
                    var blockId = BlockId(divisionIndex, shard.NShardIndex);
                    divisionBlockIds.Add(blockId);
                    var blockFragmentIds = new List<string>();
                    var remainingTiles = divisionTileCount;
                    var nextKTileWithinDivision = 0;
                    var fragmentIndex = 0;

                    while (remainingTiles > 0)
                    {
                        if (!TryReserveLowestContiguous(
                                occupancy,
                                remainingTiles,
                                out var clusterIndex,
                                out var peStartOrdinal,
                                out var peCount))
                        {
                            return Failure(new Phase8ADcLayoutIssue(
                                Phase8ADcLayoutIssueCodes.CapacityExceeded,
                                "$.allocation",
                                $"No contiguous cluster segment remains for division {divisionIndex}, N shard {shard.NShardIndex}, fragment {fragmentIndex}."));
                        }

                        var fragmentId = FragmentId(divisionIndex, shard.NShardIndex, fragmentIndex);
                        var localGroupId = LocalGroupId(divisionIndex, shard.NShardIndex, fragmentIndex, clusterIndex);
                        var fragmentAssignmentIds = new List<string>();
                        var fragmentKOffset = checked(divisionKOffset + (long)nextKTileWithinDivision * normalized.PeRows);
                        var fragmentKRange = new MappingIndexRange(fragmentKOffset, checked((long)peCount * normalized.PeRows));

                        for (var localIndex = 0; localIndex < peCount; localIndex++)
                        {
                            var kTileWithinDivision = checked(nextKTileWithinDivision + localIndex);
                            var globalKTileIndex = checked(divisionKTileStart + kTileWithinDivision);
                            var peOrdinal = checked(peStartOrdinal + localIndex);
                            var assignmentId = AssignmentId(divisionIndex, shard.NShardIndex, globalKTileIndex);
                            var kRange = new MappingIndexRange(
                                checked((long)globalKTileIndex * normalized.PeRows),
                                normalized.PeRows);
                            var assignment = new Phase8ADcPeAssignment(
                                assignmentId,
                                OperationTileId(normalized.OperationId, globalKTileIndex, shard.NShardIndex),
                                blockId,
                                fragmentId,
                                ActivationTileId(normalized.OperationId, globalKTileIndex),
                                WeightTileId(normalized.OperationId, globalKTileIndex, shard.NShardIndex),
                                PartialResultId(normalized.OperationId, globalKTileIndex, shard.NShardIndex),
                                divisionIndex,
                                shard.NShardIndex,
                                globalKTileIndex,
                                kTileWithinDivision,
                                kRange,
                                shard.NRange,
                                clusterIndex,
                                peOrdinal);
                            assignments.Add(assignment);
                            fragmentAssignmentIds.Add(assignmentId);
                            assignmentIdsByCluster[clusterIndex].Add(assignmentId);
                        }

                        blockFragmentIds.Add(fragmentId);
                        fragments.Add(new Phase8ADcClusterFragment(
                            fragmentId,
                            blockId,
                            divisionIndex,
                            shard.NShardIndex,
                            fragmentIndex,
                            clusterIndex,
                            peStartOrdinal,
                            peCount,
                            nextKTileWithinDivision,
                            fragmentKRange,
                            localGroupId,
                            fragmentAssignmentIds));
                        localGroups.Add(new Phase8ADcLocalReductionGroup(
                            localGroupId,
                            LocalGroupKey(normalized.OperationId, divisionIndex, shard.NShardIndex, fragmentIndex, clusterIndex),
                            divisionIndex,
                            shard.NShardIndex,
                            fragmentIndex,
                            clusterIndex,
                            fragmentKRange,
                            shard.NRange,
                            fragmentAssignmentIds));

                        nextKTileWithinDivision = checked(nextKTileWithinDivision + peCount);
                        remainingTiles -= peCount;
                        fragmentIndex++;
                    }

                    blocks.Add(new Phase8ADcBlock(
                        blockId,
                        divisionIndex,
                        shard.NShardIndex,
                        divisionKRange,
                        shard.NRange,
                        blockFragmentIds));
                }

                divisions.Add(new Phase8ADcDivision(
                    DivisionId(divisionIndex),
                    divisionIndex,
                    divisionKRange,
                    divisionKTileStart,
                    divisionTileCount,
                    divisionBlockIds));
            }

            var activationDeliveries = assignments
                .GroupBy(assignment => (assignment.GlobalKTileIndex, assignment.ClusterIndex))
                .OrderBy(group => group.Key.GlobalKTileIndex)
                .ThenBy(group => group.Key.ClusterIndex)
                .Select(group =>
                {
                    var ordered = group.OrderBy(item => item.PeOrdinal).ToArray();
                    var first = ordered[0];
                    return new Phase8ADcActivationClusterDelivery(
                        ActivationDeliveryId(group.Key.GlobalKTileIndex, group.Key.ClusterIndex),
                        first.ActivationTileId,
                        group.Key.GlobalKTileIndex,
                        first.KRange,
                        group.Key.ClusterIndex,
                        ordered.Select(item => item.AssignmentId),
                        ordered.Select(item => item.PeOrdinal));
                })
                .ToArray();
            var meshGroups = nShards.Select(shard =>
            {
                var contributors = localGroups
                    .Where(group => group.NShardIndex == shard.NShardIndex)
                    .OrderBy(group => group.KRange.Offset)
                    .ThenBy(group => group.KRange.Extent)
                    .ThenBy(group => group.ClusterIndex)
                    .ThenBy(group => group.FragmentIndex)
                    .Select(group => group.GroupId)
                    .ToArray();
                return new Phase8ADcMeshReductionGroup(
                    MeshGroupId(shard.NShardIndex),
                    MeshGroupKey(normalized.OperationId, shard.NShardIndex),
                    shard.NShardIndex,
                    shard.NRange,
                    contributors);
            }).ToArray();
            var assemblyShards = nShards.Select(shard => new Phase8ADcFinalAssemblyShard(
                AssemblyShardId(shard.NShardIndex),
                shard.NShardIndex,
                shard.NRange,
                MeshGroupId(shard.NShardIndex))).ToArray();
            var occupancies = Enumerable.Range(0, clusterCount)
                .Select(clusterIndex => new Phase8ADcClusterOccupancy(
                    clusterIndex,
                    Bitset(occupancy[clusterIndex]),
                    assignmentIdsByCluster[clusterIndex]))
                .ToArray();
            var localAdds = localGroups.Sum(group => group.AddOperationCount);
            var meshAdds = meshGroups.Sum(group => group.AddOperationCount);
            var capacity = checked(clusterCount * normalized.ClusterSize);
            var summary = new Phase8ADcLayoutSummary(
                divisionCount,
                kTileCount,
                nShardCount,
                clusterCount,
                capacity,
                assignments.Count,
                checked(capacity - assignments.Count),
                assignments.Count,
                localGroups.Count,
                localAdds,
                meshGroups.Length,
                meshAdds,
                assemblyShards.Length);

            var invariantIssues = ValidateResolved(
                normalized,
                divisions,
                nShards,
                blocks,
                fragments,
                assignments,
                activationDeliveries,
                localGroups,
                meshGroups,
                assemblyShards,
                occupancies,
                summary);
            if (invariantIssues.Count > 0) return Failure(invariantIssues);

            var canonicalHash = ComputeHash(
                normalized,
                divisions,
                nShards,
                blocks,
                fragments,
                assignments,
                activationDeliveries,
                localGroups,
                meshGroups,
                assemblyShards,
                occupancies,
                summary);
            return new Phase8ADcLayoutResult(new Phase8ADcLayoutPlan(
                normalized,
                divisions,
                nShards,
                blocks,
                fragments,
                assignments,
                activationDeliveries,
                localGroups,
                meshGroups,
                assemblyShards,
                occupancies,
                summary,
                canonicalHash), []);
        }
        catch (OverflowException)
        {
            return Failure(new Phase8ADcLayoutIssue(
                Phase8ADcLayoutIssueCodes.ArithmeticOverflow,
                "$",
                "D/C tile, capacity, range, or conservation arithmetic exceeded the supported range."));
        }
    }

    private static List<Phase8ADcLayoutIssue> Validate(Phase8ADcLayoutRequest? request)
    {
        var issues = new List<Phase8ADcLayoutIssue>();
        if (request is null)
        {
            issues.Add(new(Phase8ADcLayoutIssueCodes.RequestMissing, "$", "A D/C layout request is required."));
            return issues;
        }

        if (string.IsNullOrWhiteSpace(request.OperationId))
            issues.Add(new(Phase8ADcLayoutIssueCodes.OperationIdInvalid, "$.operationId", "OperationId must be non-empty."));
        if (request.M <= 0 || request.K <= 0 || request.N <= 0 || request.PeRows <= 0 || request.PeColumns <= 0)
            issues.Add(new(Phase8ADcLayoutIssueCodes.ShapeInvalid, "$.shape", "M, K, N, and PE dimensions must be positive."));
        if (request.M > 0 && request.M != 1)
            issues.Add(new(Phase8ADcLayoutIssueCodes.UnsupportedMExtent, "$.m", "The current D/C scenario supports exactly M=1."));
        if (request.PeRows > 0 && request.PeColumns > 0 &&
            (request.PeRows != CanonicalPeRows || request.PeColumns != CanonicalPeColumns))
            issues.Add(new(Phase8ADcLayoutIssueCodes.ComputeTileUnsupported, "$.pe", "The canonical D/C planner requires a 32x32 PE compute tile."));
        if (request.K > 0 && request.N > 0 && request.PeRows > 0 && request.PeColumns > 0 &&
            (request.K % request.PeRows != 0 || request.N % request.PeColumns != 0))
            issues.Add(new(Phase8ADcLayoutIssueCodes.TailUnsupported, "$.shape", "K and N tails require an explicit future padding mode; canonical mode rejects them."));
        if (request.WeightRowDivisionSize <= 0)
            issues.Add(new(Phase8ADcLayoutIssueCodes.DivisionInvalid, "$.weightRowDivisionSize", "D must be positive."));
        else if (request.K > 0 && request.WeightRowDivisionSize > request.K)
            issues.Add(new(Phase8ADcLayoutIssueCodes.DivisionExceedsK, "$.weightRowDivisionSize", "D must not exceed K."));
        else if (request.PeRows > 0 && request.K > 0 &&
                 (request.WeightRowDivisionSize % request.PeRows != 0 || request.K % request.WeightRowDivisionSize != 0))
            issues.Add(new(Phase8ADcLayoutIssueCodes.DivisionInvalid, "$.weightRowDivisionSize", "Canonical mode requires D to be divisible by 32 and to divide K exactly."));
        if (request.ClusterSize < 2 || !IsPowerOfTwo(request.ClusterSize))
            issues.Add(new(Phase8ADcLayoutIssueCodes.ClusterInvalid, "$.clusterSize", "Canonical MoT cluster size C must be a power of two and at least two."));
        if (request.ClusterCount is <= 0)
            issues.Add(new(Phase8ADcLayoutIssueCodes.ClusterCountInvalid, "$.clusterCount", "An explicit cluster count must be positive."));

        if (issues.Count > 0) return issues;
        try
        {
            var operationTiles = checked((long)(request.K / request.PeRows) * (request.N / request.PeColumns));
            if (operationTiles > MaximumOperationTiles)
                issues.Add(new(Phase8ADcLayoutIssueCodes.LayoutTooLarge, "$.shape", $"The pure planner limit is {MaximumOperationTiles} operation tiles."));
            if (request.ClusterCount is int clusterCount && checked((long)clusterCount * request.ClusterSize) < operationTiles)
                issues.Add(new(Phase8ADcLayoutIssueCodes.CapacityExceeded, "$.clusterCount", "The declared cluster inventory cannot hold every operation tile exactly once."));
        }
        catch (OverflowException)
        {
            issues.Add(new(Phase8ADcLayoutIssueCodes.ArithmeticOverflow, "$", "D/C request arithmetic exceeded the supported range."));
        }
        return issues;
    }

    private static IReadOnlyList<Phase8ADcLayoutIssue> ValidateResolved(
        Phase8ADcLayoutRequest request,
        IReadOnlyList<Phase8ADcDivision> divisions,
        IReadOnlyList<Phase8ADcNShard> nShards,
        IReadOnlyList<Phase8ADcBlock> blocks,
        IReadOnlyList<Phase8ADcClusterFragment> fragments,
        IReadOnlyList<Phase8ADcPeAssignment> assignments,
        IReadOnlyList<Phase8ADcActivationClusterDelivery> activationDeliveries,
        IReadOnlyList<Phase8ADcLocalReductionGroup> localGroups,
        IReadOnlyList<Phase8ADcMeshReductionGroup> meshGroups,
        IReadOnlyList<Phase8ADcFinalAssemblyShard> assemblyShards,
        IReadOnlyList<Phase8ADcClusterOccupancy> occupancies,
        Phase8ADcLayoutSummary summary)
    {
        var failures = new List<string>();
        var expectedAssignmentCount = checked((request.K / request.PeRows) * (request.N / request.PeColumns));
        if (assignments.Count != expectedAssignmentCount || assignments.Select(item => item.AssignmentId).Distinct(StringComparer.Ordinal).Count() != assignments.Count)
            failures.Add("Every K-by-N operation tile must have one unique PE assignment.");
        if (assignments.GroupBy(item => (item.ClusterIndex, item.PeOrdinal)).Any(group => group.Count() != 1))
            failures.Add("Each physical PE slot must hold at most one resident weight tile.");
        if (assignments.GroupBy(item => (item.GlobalKTileIndex, item.NShardIndex)).Any(group => group.Count() != 1))
            failures.Add("Every logical K tile and N shard pair must be covered exactly once.");
        if (blocks.Count != divisions.Count * nShards.Count || fragments.Select(item => item.FragmentId).Distinct(StringComparer.Ordinal).Count() != fragments.Count)
            failures.Add("Every division and N shard must resolve one block with unique fragments.");

        var assignmentIds = assignments.Select(item => item.AssignmentId).ToHashSet(StringComparer.Ordinal);
        var assignmentById = assignments
            .GroupBy(item => item.AssignmentId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var localContributorIds = localGroups.SelectMany(group => group.ContributorAssignmentIds).ToArray();
        if (localContributorIds.Length != assignments.Count ||
            localContributorIds.Distinct(StringComparer.Ordinal).Count() != assignments.Count ||
            !assignmentIds.SetEquals(localContributorIds))
            failures.Add("Every PE partial must enter exactly one local reduction group.");
        if (localGroups.Any(group => group.ContributorAssignmentIds.Any(id =>
                !assignmentById.TryGetValue(id, out var assignment) || assignment.NShardIndex != group.NShardIndex)))
            failures.Add("A local reduction group must never mix N ranges.");

        var localIds = localGroups.Select(group => group.GroupId).ToHashSet(StringComparer.Ordinal);
        var localById = localGroups
            .GroupBy(group => group.GroupId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var meshContributorIds = meshGroups.SelectMany(group => group.ContributorLocalGroupIds).ToArray();
        if (meshContributorIds.Length != localGroups.Count ||
            meshContributorIds.Distinct(StringComparer.Ordinal).Count() != localGroups.Count ||
            !localIds.SetEquals(meshContributorIds))
            failures.Add("Every local result must enter exactly one mesh N group.");
        if (meshGroups.Any(group => group.ContributorLocalGroupIds.Any(id =>
                !localById.TryGetValue(id, out var local) || local.NShardIndex != group.NShardIndex)))
            failures.Add("A mesh reduction group must never mix N ranges.");

        var activationTargets = activationDeliveries.SelectMany(delivery => delivery.TargetAssignmentIds).ToArray();
        if (activationTargets.Length != assignments.Count ||
            activationTargets.Distinct(StringComparer.Ordinal).Count() != assignments.Count ||
            !assignmentIds.SetEquals(activationTargets))
            failures.Add("Every PE assignment must consume exactly one cluster-local activation delivery.");
        if (activationDeliveries.Any(delivery => delivery.TargetAssignmentIds.Count != delivery.TargetPeOrdinals.Count ||
                                                 delivery.TargetPeOrdinals.Distinct().Count() != delivery.TargetPeOrdinals.Count ||
                                                 delivery.TargetAssignmentIds.Any(id =>
                                                     !assignmentById.TryGetValue(id, out var assignment) ||
                                                     assignment.GlobalKTileIndex != delivery.GlobalKTileIndex ||
                                                     assignment.ClusterIndex != delivery.ClusterIndex)))
            failures.Add("Activation target identities and PE ordinals must be exact and duplicate-free within a cluster.");

        var sortedAssembly = assemblyShards.OrderBy(item => item.NRange.Offset).ToArray();
        long nextNOffset = 0;
        foreach (var shard in sortedAssembly)
        {
            if (shard.NRange.Offset != nextNOffset || shard.NRange.Extent <= 0) failures.Add("Final N shards contain an overlap or gap.");
            nextNOffset = checked(shard.NRange.Offset + shard.NRange.Extent);
        }
        if (nextNOffset != request.N || assemblyShards.Count != nShards.Count) failures.Add("Final N assembly does not cover Y exactly once.");
        if (summary.LocalAddOperationCount + summary.MeshAddOperationCount != summary.PePartialCount - summary.FinalOutputShardCount)
            failures.Add("Local plus mesh add operations must conserve PE partials into final N shards.");
        if (occupancies.Count != request.ClusterCount || occupancies.Sum(item => item.AssignmentIds.Count) != assignments.Count ||
            occupancies.Any(occupancy => occupancy.UtilizationBitset.Length != request.ClusterSize ||
                                         occupancy.AssignmentIds.Any(id =>
                                             !assignmentById.TryGetValue(id, out var assignment) ||
                                             assignment.ClusterIndex != occupancy.ClusterIndex)))
            failures.Add("Cluster occupancy must cover every assignment and every declared cluster exactly.");

        return failures.Distinct(StringComparer.Ordinal)
            .Select(message => new Phase8ADcLayoutIssue(Phase8ADcLayoutIssueCodes.InternalInvariant, "$.resolvedPlan", message))
            .ToArray();
    }

    private static bool TryReserveLowestContiguous(
        IReadOnlyList<bool[]> occupancy,
        int requestedCount,
        out int clusterIndex,
        out int peStartOrdinal,
        out int peCount)
    {
        clusterIndex = -1;
        peStartOrdinal = -1;
        peCount = 0;
        for (var candidateCluster = 0; candidateCluster < occupancy.Count; candidateCluster++)
        {
            var slots = occupancy[candidateCluster];
            var start = Array.FindIndex(slots, used => !used);
            if (start < 0) continue;
            var available = 0;
            while (start + available < slots.Length && !slots[start + available]) available++;
            if (available == 0) continue;
            var reserved = Math.Min(requestedCount, available);
            for (var ordinal = start; ordinal < start + reserved; ordinal++) slots[ordinal] = true;
            clusterIndex = candidateCluster;
            peStartOrdinal = start;
            peCount = reserved;
            return true;
        }
        return false;
    }

    private static string ComputeHash(
        Phase8ADcLayoutRequest request,
        IReadOnlyList<Phase8ADcDivision> divisions,
        IReadOnlyList<Phase8ADcNShard> nShards,
        IReadOnlyList<Phase8ADcBlock> blocks,
        IReadOnlyList<Phase8ADcClusterFragment> fragments,
        IReadOnlyList<Phase8ADcPeAssignment> assignments,
        IReadOnlyList<Phase8ADcActivationClusterDelivery> activationDeliveries,
        IReadOnlyList<Phase8ADcLocalReductionGroup> localGroups,
        IReadOnlyList<Phase8ADcMeshReductionGroup> meshGroups,
        IReadOnlyList<Phase8ADcFinalAssemblyShard> assemblyShards,
        IReadOnlyList<Phase8ADcClusterOccupancy> occupancies,
        Phase8ADcLayoutSummary summary)
    {
        var json = JsonSerializer.Serialize(new
        {
            algorithm = Phase8ADcLayoutPlan.CanonicalHashAlgorithm,
            request,
            divisions,
            nShards,
            blocks,
            fragments,
            assignments,
            activationDeliveries,
            localGroups,
            meshGroups,
            assemblyShards,
            occupancies,
            summary
        }, HardwareGraphJson.Options);
        return ComponentExecutionJson.ComputeSha256(ComponentExecutionJson.CanonicalizeJson(json));
    }

    private static string DivisionId(int division) => $"division:d{division:D4}";
    private static string NShardId(int nShard) => $"n-shard:n{nShard:D4}";
    private static string BlockId(int division, int nShard) => $"block:d{division:D4}:n{nShard:D4}";
    private static string FragmentId(int division, int nShard, int fragment) => $"fragment:d{division:D4}:n{nShard:D4}:f{fragment:D4}";
    private static string AssignmentId(int division, int nShard, int globalKTile) => $"assignment:d{division:D4}:n{nShard:D4}:k{globalKTile:D4}";
    private static string OperationTileId(string operationId, int globalKTile, int nShard) => $"{operationId}:issue:m0:k{globalKTile}:n{nShard}";
    private static string ActivationTileId(string operationId, int globalKTile) => $"{operationId}:x:m0:k{globalKTile}";
    private static string WeightTileId(string operationId, int globalKTile, int nShard) => $"{operationId}:w:k{globalKTile}:n{nShard}";
    private static string PartialResultId(string operationId, int globalKTile, int nShard) => $"{operationId}:y:partial:m0:k{globalKTile}:n{nShard}";
    private static string ActivationDeliveryId(int globalKTile, int cluster) => $"activation-delivery:k{globalKTile:D4}:c{cluster:D4}";
    private static string LocalGroupId(int division, int nShard, int fragment, int cluster) => $"local:d{division:D4}:n{nShard:D4}:f{fragment:D4}:c{cluster:D4}";
    private static string MeshGroupId(int nShard) => $"mesh:n{nShard:D4}";
    private static string AssemblyShardId(int nShard) => $"assembly:n{nShard:D4}";
    private static string LocalGroupKey(string operationId, int division, int nShard, int fragment, int cluster) =>
        $"{operationId}|m0|n{nShard}|d{division}|f{fragment}|c{cluster}";
    private static string MeshGroupKey(string operationId, int nShard) => $"{operationId}|m0|n{nShard}";
    private static string Bitset(IEnumerable<bool> values) => new(values.Select(value => value ? '1' : '0').ToArray());
    private static int CeilingDiv(int value, int divisor) => checked(1 + (value - 1) / divisor);
    private static bool IsPowerOfTwo(int value) => value > 0 && (value & (value - 1)) == 0;
    private static Phase8ADcLayoutResult Failure(params Phase8ADcLayoutIssue[] issues) => new(null, issues);
    private static Phase8ADcLayoutResult Failure(IEnumerable<Phase8ADcLayoutIssue> issues) => new(null, issues);
}
