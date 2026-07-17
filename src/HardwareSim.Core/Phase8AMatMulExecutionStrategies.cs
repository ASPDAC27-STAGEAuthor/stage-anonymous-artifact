namespace HardwareSim.Core;

/// <summary>Stable execution-strategy identities for generated Phase 8A MatMul scenarios.</summary>
public static class Phase8ATopologyExecutionStrategies
{
    /// <summary>Retains the pre-correction source-tile and collector-overlay runtime for explicit compatibility.</summary>
    public const string LegacyOverlayV2 = "com.hardware-sim.strategy.legacy-overlay.v2";

    /// <summary>Executes row-replicated activation slicing and tree-node in-network reduction on canonical MoT.</summary>
    public const string MeshOfTreesRowReplicatedInrV1 = "com.hardware-sim.strategy.mesh-of-trees.row-replicated-inr.v1";

    internal const string RouterBranchCapabilityParameter = "phase8a_router_branch_capability";
    internal const string RouterBranchCapabilityValue = "explicit-multicast/v1";
}

/// <summary>Stable result-landing modes for topology execution strategies.</summary>
public static class Phase8AOutputLandingModes
{
    /// <summary>Delivers one result shard from every cluster root.</summary>
    public const string ClusterLocalShardsV1 = "cluster-local-shards/v1";

    /// <summary>Delivers reduced result roots from one or more topology-resolved egress routers.</summary>
    public const string TopologyEgressShardsV1 = "topology-egress-shards/v1";

    /// <summary>Routes result shards to one cluster for exact offset assembly.</summary>
    public const string CentralOffsetAssemblyV1 = "central-offset-assembly/v1";
}

internal interface IPhase8AMatMulRuntimeStrategy
{
    string StrategyId { get; }

    (Phase8AMatMulExecutableScenario? Scenario, IReadOnlyList<Phase8AMatMulScenarioIssue> Issues) Build(
        Phase8AMatMulScenarioPlan plan);
}

internal static class Phase8AMatMulRuntimeStrategyRegistry
{
    private static readonly IReadOnlyDictionary<string, IPhase8AMatMulRuntimeStrategy> Strategies =
        new Dictionary<string, IPhase8AMatMulRuntimeStrategy>(StringComparer.Ordinal)
        {
            [Phase8ATopologyExecutionStrategies.LegacyOverlayV2] = Phase8ALegacyOverlayRuntimeStrategy.Instance,
            [Phase8ATopologyExecutionStrategies.MeshOfTreesRowReplicatedInrV1] = Phase8AMoTRowReplicatedInrRuntimeStrategy.Instance
        };

    public static (Phase8AMatMulExecutableScenario? Scenario, IReadOnlyList<Phase8AMatMulScenarioIssue> Issues) Build(
        Phase8AMatMulScenarioPlan plan)
    {
        if (!Strategies.TryGetValue(plan.Request.TopologyExecutionStrategyId, out var strategy))
        {
            return (null,
            [
                new Phase8AMatMulScenarioIssue(
                    "Phase8AScenarioExecutionStrategyUnsupported",
                    "$.topologyExecutionStrategyId",
                    $"Execution strategy '{plan.Request.TopologyExecutionStrategyId}' is not registered.")
            ]);
        }

        return strategy.Build(plan);
    }
}

internal sealed class Phase8ALegacyOverlayRuntimeStrategy : IPhase8AMatMulRuntimeStrategy
{
    public static readonly Phase8ALegacyOverlayRuntimeStrategy Instance = new();

    public string StrategyId => Phase8ATopologyExecutionStrategies.LegacyOverlayV2;

    public (Phase8AMatMulExecutableScenario? Scenario, IReadOnlyList<Phase8AMatMulScenarioIssue> Issues) Build(
        Phase8AMatMulScenarioPlan plan) => Phase8AMatMulResolvedRuntimeBuilder.BuildLegacy(plan);
}

internal sealed class Phase8AMoTRowReplicatedInrRuntimeStrategy : IPhase8AMatMulRuntimeStrategy
{
    public static readonly Phase8AMoTRowReplicatedInrRuntimeStrategy Instance = new();

    public string StrategyId => Phase8ATopologyExecutionStrategies.MeshOfTreesRowReplicatedInrV1;

    public (Phase8AMatMulExecutableScenario? Scenario, IReadOnlyList<Phase8AMatMulScenarioIssue> Issues) Build(
        Phase8AMatMulScenarioPlan plan) => Phase8AMoTInrRuntimeBuilder.Build(plan);
}
