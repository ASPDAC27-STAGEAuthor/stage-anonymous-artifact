namespace HardwareSim.Core;

internal sealed record Phase8AMatMulScenarioPlan(
    Phase8AMatMulScenarioRequest Request,
    int MeshRows,
    int MeshColumns,
    HardwareGraph TopologyGraph,
    TopologyManifest TopologyManifest,
    Phase8AMatMulLoweringPlan Lowering,
    Phase8ADcLayoutPlan DcLayout,
    Phase8AActivationTreePlan ActivationTree,
    Phase8AHierarchicalReductionPlan HierarchicalReduction,
    IReadOnlyList<OperationTileAssignment> Assignments,
    Phase8AWeightPlacementPlan WeightPlacement,
    MappingCandidate Candidate,
    CapabilitySnapshot ReferenceCapabilities,
    IReadOnlyList<double> Input,
    IReadOnlyList<double> Weights,
    string InputHash,
    string WeightHash,
    string MappingAuthorityHash);

internal static class Phase8AMatMulScenarioPlanner
{
    public static (Phase8AMatMulScenarioPlan? Plan, IReadOnlyList<Phase8AMatMulScenarioIssue> Issues) Plan(
        Phase8AMatMulScenarioRequest request)
    {
        var resolved = Phase8AMatMulResolvedAuthorityResolver.Resolve(request);
        if (!resolved.IsSuccess || resolved.Bundle is null)
            return (null, resolved.Issues);

        try
        {
            var authority = resolved.Bundle;
            var (input, weights) = GenerateValues(request);
            return (new Phase8AMatMulScenarioPlan(
                authority.Request,
                authority.MeshRows,
                authority.MeshColumns,
                authority.TopologyGraphAuthority,
                authority.TopologyManifest,
                authority.Lowering,
                authority.DcLayout,
                authority.ActivationTree,
                authority.HierarchicalReduction,
                authority.Assignments,
                authority.WeightPlacement,
                authority.Candidate,
                authority.Capabilities,
                input,
                weights,
                authority.InputArtifactHash,
                authority.WeightArtifactHash,
                authority.ResolvedMappingHash), []);
        }
        catch (Exception exception) when (exception is OverflowException or InvalidOperationException or ArgumentException)
        {
            return (null, [new Phase8AMatMulScenarioIssue("Phase8AScenarioValueGenerationFailed", "$", exception.Message)]);
        }
    }

    private static (IReadOnlyList<double> Input, IReadOnlyList<double> Weights) GenerateValues(
        Phase8AMatMulScenarioRequest request)
    {
        var random = new StableRandom(unchecked((uint)request.Seed));
        var input = new double[request.K];
        for (var tile = 0; tile < request.K / request.PeRows; tile++)
        {
            var selected = new HashSet<int>();
            while (selected.Count < Math.Min(8, request.PeRows)) selected.Add(random.Next(request.PeRows));
            foreach (var index in selected) input[tile * request.PeRows + index] = random.Next(2) == 0 ? -1d : 1d;
        }
        var weights = new double[checked(request.K * request.N)];
        for (var index = 0; index < weights.Length; index++) weights[index] = random.Next(3) - 1;
        return (Array.AsReadOnly(input), Array.AsReadOnly(weights));
    }

    private sealed class StableRandom
    {
        private uint state;
        public StableRandom(uint seed) => state = seed == 0 ? 0x6d2b79f5u : seed;
        public int Next(int exclusiveMaximum)
        {
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            return (int)(state % (uint)exclusiveMaximum);
        }
    }
}