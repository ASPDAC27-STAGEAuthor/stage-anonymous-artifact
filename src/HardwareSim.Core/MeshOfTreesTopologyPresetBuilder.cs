namespace HardwareSim.Core;

/// <summary>Builds the canonical Phase 8A mesh-of-trees topology from ordinary graph primitives.</summary>
public sealed class MeshOfTreesTopologyPresetBuilder : ITopologyPresetBuilder
{
    /// <summary>Identifies the stable production builder implementation.</summary>
    public const string BuilderId = "com.hardware-sim.topology-builder.mesh-of-trees.v1";
    /// <summary>Identifies the stable production builder contract version.</summary>
    public const string BuilderVersion = "1.0";
    /// <summary>Identifies generated MoT preset provenance.</summary>
    public const string ProvenanceSource = "generated-mot-preset";

    /// <inheritdoc />
    public string TopologyId => ReferenceMappingTopologyIds.MeshOfTreesV1;

    /// <inheritdoc />
    public TopologyBuildResult Build(TopologyPresetRequest request)
    {
        var issues = MeshOfTreesTopologyPresetValidator.Validate(request);
        if (issues.Any(issue => issue.Severity == ValidationSeverity.Error))
        {
            return new TopologyBuildResult(new HardwareGraph(), null, issues);
        }

        try
        {
            return MeshOfTreesTopologyGenerator.Generate(request, issues);
        }
        catch (OverflowException exception)
        {
            issues.Add(new TopologyBuildIssue(
                TopologyBuildIssueCodes.ArithmeticOverflow,
                ValidationSeverity.Error,
                "$",
                "Mesh-of-trees inventory or bandwidth arithmetic overflowed the supported integer range.",
                exception.Message));
            return new TopologyBuildResult(new HardwareGraph(), null, issues);
        }
    }
}

internal static class MeshOfTreesTopologyPresetValidator
{
    internal static List<TopologyBuildIssue> Validate(TopologyPresetRequest? request)
    {
        var issues = new List<TopologyBuildIssue>();
        if (request is null)
        {
            issues.Add(Issue(
                TopologyBuildIssueCodes.MissingRequest,
                "$",
                "A topology preset request is required."));
            return issues;
        }

        if (!string.Equals(request.TopologyId, ReferenceMappingTopologyIds.MeshOfTreesV1, StringComparison.Ordinal))
        {
            issues.Add(Issue(
                TopologyBuildIssueCodes.UnsupportedTopology,
                "$.topologyId",
                $"Mesh-of-trees builder does not support topology '{request.TopologyId}'."));
        }

        if (request.MeshRows <= 0 || request.MeshColumns <= 0)
        {
            issues.Add(Issue(
                TopologyBuildIssueCodes.InvalidMeshSize,
                "$.meshRows",
                "Mesh rows and columns must both be positive."));
        }

        if (request.ClusterSize < 2)
        {
            issues.Add(Issue(
                TopologyBuildIssueCodes.InvalidClusterSize,
                "$.clusterSize",
                "Canonical mesh-of-trees clusters require at least two processing elements."));
        }
        else if (!IsPowerOfTwo(request.ClusterSize))
        {
            issues.Add(Issue(
                TopologyBuildIssueCodes.NonPowerOfTwoClusterSize,
                "$.clusterSize",
                "Canonical mesh-of-trees clusters require a power-of-two processing-element count."));
        }

        if (request.WordBits <= 0 || request.LeafLaneCount <= 0)
        {
            issues.Add(Issue(
                TopologyBuildIssueCodes.InvalidBandwidthConfiguration,
                "$.wordBits",
                "Word width and leaf lane count must both be positive."));
        }

        if (!IsFinitePositive(request.LeafLinkDistance) ||
            !IsFinitePositive(request.TreeDistanceScale) ||
            !IsFinitePositive(request.MeshHopDistance) ||
            !IsFinitePositive(request.PlacementCellSizeMicrometers))
        {
            issues.Add(Issue(
                TopologyBuildIssueCodes.InvalidDistanceConfiguration,
                "$.leafLinkDistance",
                "Leaf distance, tree distance scale, mesh-hop distance, and placement cell size must be finite and positive."));
        }
        else if (IsPowerOfTwo(request.ClusterSize) && request.ClusterSize >= 2 &&
                 !IsFinitePositive(request.LeafLinkDistance * Math.Pow(request.TreeDistanceScale, Log2(request.ClusterSize))))
        {
            issues.Add(Issue(
                TopologyBuildIssueCodes.InvalidDistanceConfiguration,
                "$.treeDistanceScale",
                "The highest derived tree-link distance must remain finite and positive."));
        }

        if (request.RouterLatencyCycles < 0 || request.AdderLatencyCycles < 0)
        {
            issues.Add(Issue(
                TopologyBuildIssueCodes.InvalidLatencyConfiguration,
                "$.routerLatencyCycles",
                "Router and adder latency must be non-negative."));
        }

        try
        {
            _ = checked(request.MeshRows * request.MeshColumns);
            _ = checked(request.MeshRows * request.MeshColumns * request.ClusterSize);
            _ = checked(request.WordBits * request.LeafLaneCount * Math.Max(1, request.ClusterSize));
        }
        catch (OverflowException)
        {
            issues.Add(Issue(
                TopologyBuildIssueCodes.ArithmeticOverflow,
                "$",
                "Requested topology inventory or bandwidth exceeds the supported integer range."));
        }

        return issues;
    }

    private static bool IsPowerOfTwo(int value) => value > 0 && (value & (value - 1)) == 0;

    private static int Log2(int powerOfTwo)
    {
        var result = 0;
        for (var value = powerOfTwo; value > 1; value >>= 1) result++;
        return result;
    }

    private static bool IsFinitePositive(double value) =>
        value > 0 && !double.IsNaN(value) && !double.IsInfinity(value);

    private static TopologyBuildIssue Issue(string code, string location, string message) =>
        new(code, ValidationSeverity.Error, location, message);
}
