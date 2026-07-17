using System.Collections.ObjectModel;
using System.Globalization;

namespace HardwareSim.Core;

#pragma warning disable CS1591 // Public contract names carry stable schema, metric, unit, and provenance semantics.

/// <summary>Stable identities for the Phase 8A topology-cost-aware policy boundary.</summary>
public static class Phase8ATopologyCostAwarePolicyContract
{
    public const string CurrentConfigVersion = "1.1";
    public const string CurrentProfileVersion = "1.2";
    public const string CurrentBudgetVersion = "1.0";
    public const string DeterministicEnumerationMode = "semantic-depth-first-v1";
    public const string StaticCostProfileId = "phase8a-topology-static-cost-profile-v1.2";
    public const string PathCostModelId = "branch-aware-activation-plus-independent-other-catalog-latency-serialization-medium-conversion-congestion-v1";
    public const string ImbalanceModelId = "static-share-capacity-and-bandwidth-service-ppm-v1";
    public const string NoEnergyModelId = "none-not-estimated";
    public const string PartitionSignatureHashAlgorithm = "sha256/phase8a-lowering-partition-signature/v1";
    public const string PartitionPortfolioHashAlgorithm = "sha256/phase8a-topology-cost-partition-portfolio/v1";
}

/// <summary>Versioned non-negative weights for independently reproducible score contributions.</summary>
public sealed record Phase8ATopologyCostWeights(
    decimal AnalyticalLatencyWeight,
    decimal BitHopWeight,
    decimal DistanceTrafficWeight,
    decimal PredictedPeakBufferWeight,
    decimal PeImbalanceWeight,
    decimal StorageImbalanceWeight,
    decimal LinkImbalanceWeight,
    decimal LinkHotspotWeight,
    decimal ElectricalMetalBitHopWeight = 0.0000000005m,
    decimal OpticalWaveguideBitHopWeight = 0.00000000025m,
    decimal ThermalControlBitHopWeight = 0.000000001m,
    decimal ConversionTransitionWeight = 0.01m,
    decimal StaticServiceCongestionWeight = 0.001m)
{
    public static Phase8ATopologyCostWeights CreateDefault() => new(
        1m, 0.000001m, 0.000000001m, 0.000001m,
        0.001m, 0.001m, 0.001m, 0.000001m,
        0.0000000005m, 0.00000000025m, 0.000000001m, 0.01m, 0.001m);
}

/// <summary>Explicit versioned policy configuration. The default policy never uses the seed as randomness.</summary>
public sealed record Phase8ATopologyCostPolicyConfig(
    string SchemaVersion,
    string PolicyId,
    string EnumerationModeId,
    Phase8ATopologyCostWeights Weights)
{
    public static Phase8ATopologyCostPolicyConfig CreateDefault() => new(
        Phase8ATopologyCostAwarePolicyContract.CurrentConfigVersion,
        ReferenceMappingPolicyIds.TopologyCostAwareV1,
        Phase8ATopologyCostAwarePolicyContract.DeterministicEnumerationMode,
        Phase8ATopologyCostWeights.CreateDefault());
}

/// <summary>Versioned analytical profile. Energy is deliberately absent from this static mapper profile.</summary>
public sealed record Phase8ATopologyCostProfile(
    string SchemaVersion,
    string ProfileId,
    int WordBits,
    string PathCostModelId,
    string ImbalanceModelId,
    string EnergyAccountingModeId)
{
    public static Phase8ATopologyCostProfile CreateDefault() => new(
        Phase8ATopologyCostAwarePolicyContract.CurrentProfileVersion,
        Phase8ATopologyCostAwarePolicyContract.StaticCostProfileId,
        32,
        Phase8ATopologyCostAwarePolicyContract.PathCostModelId,
        Phase8ATopologyCostAwarePolicyContract.ImbalanceModelId,
        Phase8ATopologyCostAwarePolicyContract.NoEnergyModelId);
}

/// <summary>Versioned hard search budget. MaxSearchNodes counts every attempted target, operand-path, collective-target, and contributor-path branch expansion.</summary>
public sealed record Phase8ATopologyCostSearchBudget(
    string SchemaVersion,
    long MaxSearchNodes,
    int MaxRetainedCandidates)
{
    public static Phase8ATopologyCostSearchBudget CreateDefault(long maxSearchNodes = 100_000, int maxRetainedCandidates = 16) =>
        new(Phase8ATopologyCostAwarePolicyContract.CurrentBudgetVersion, maxSearchNodes, maxRetainedCandidates);

    public static Phase8ATopologyCostSearchBudget CreateFromProblem(
        Phase8AMappingProblem problem,
        long? maxSearchNodes = null,
        int maxRetainedCandidates = 16)
    {
        if (problem is null) throw new ArgumentNullException(nameof(problem));
        var nodes = maxSearchNodes ?? problem.PolicyBudget.MaxSearchNodes;
        if (nodes <= 0 || nodes > problem.PolicyBudget.MaxSearchNodes) throw new ArgumentOutOfRangeException(nameof(maxSearchNodes));
        if (maxRetainedCandidates <= 0) throw new ArgumentOutOfRangeException(nameof(maxRetainedCandidates));
        return new Phase8ATopologyCostSearchBudget(
            Phase8ATopologyCostAwarePolicyContract.CurrentBudgetVersion,
            nodes,
            (int)Math.Min(nodes, maxRetainedCandidates));
    }
}

/// <summary>Freezes the immutable mapping problem and every explicit policy authority.</summary>
public sealed class Phase8ATopologyCostPolicyRequest
{
    public Phase8ATopologyCostPolicyRequest(
        Phase8AMappingProblem? problem,
        Phase8ATopologyCostPolicyConfig? config,
        Phase8ATopologyCostProfile? profile,
        int seed,
        Phase8ATopologyCostSearchBudget? budget)
    {
        Problem = problem;
        Config = config;
        Profile = profile;
        Seed = seed;
        Budget = budget;
        FrozenProblemCanonicalHash = problem?.CanonicalHash ?? "";
    }

    public Phase8AMappingProblem? Problem { get; }
    public Phase8ATopologyCostPolicyConfig? Config { get; }
    public Phase8ATopologyCostProfile? Profile { get; }
    public int Seed { get; }
    public Phase8ATopologyCostSearchBudget? Budget { get; }
    public string FrozenProblemCanonicalHash { get; }
}

/// <summary>Core-owned digest of every compiled component profile bound to explicit semantic ordering.</summary>
public sealed record Phase8ATopologyProfileBindingHash(string AlgorithmId, string Hash);

/// <summary>Computes the public versioned aggregate profile binding; callers cannot inject an opaque profile hash.</summary>
public static class Phase8ATopologyProfileBindingHasher
{
    public const string CurrentAlgorithm = "sha256/phase8a-topology-profile-binding/v1";

    public static Phase8ATopologyProfileBindingHash Compute(Phase8AMappingProblem problem)
    {
        if (problem is null) throw new ArgumentNullException(nameof(problem));
        return Compute(problem.CapabilityAuthority.Snapshot, problem.NormalizedInput.ComponentOrdinals);
    }

    public static Phase8ATopologyProfileBindingHash Compute(
        CapabilitySnapshot snapshot,
        IReadOnlyList<Phase8ASemanticComponentOrdinal> semanticComponentOrdinals)
    {
        if (snapshot is null) throw new ArgumentNullException(nameof(snapshot));
        if (semanticComponentOrdinals is null) throw new ArgumentNullException(nameof(semanticComponentOrdinals));
        var semantic = semanticComponentOrdinals.ToDictionary(item => item.ComponentId, StringComparer.Ordinal);
        var components = snapshot.Components.Select(component =>
        {
            semantic.TryGetValue(component.ComponentId, out var binding);
            return new
            {
                RoleRank = RoleRank(binding?.RoleId),
                RoleId = binding?.RoleId ?? "unclassified",
                Ordinal = binding?.Ordinal ?? int.MaxValue,
                component.ComponentId,
                component.ProfileId,
                component.ProfileHash,
                component.DynamicEnergyPicojoules,
                component.FootprintAreaUm2,
                component.PhysicalFootprintHash,
                DeviceProfileHashes = component.DeviceProfileHashes.OrderBy(pair => pair.Key, StringComparer.Ordinal)
            };
        }).OrderBy(item => item.RoleRank)
          .ThenBy(item => item.RoleId, StringComparer.Ordinal)
          .ThenBy(item => item.Ordinal)
          .ThenBy(item => item.ComponentId, StringComparer.Ordinal)
          .ToArray();
        return new Phase8ATopologyProfileBindingHash(CurrentAlgorithm, Phase8AMappingAuthorityHasher.Hash(new
        {
            algorithm = CurrentAlgorithm,
            components
        }));
    }

    private static int RoleRank(string? roleId) => roleId switch
    {
        Phase8AMappingSemanticRoles.Ingress => 0,
        Phase8AMappingSemanticRoles.Compute => 1,
        Phase8AMappingSemanticRoles.Collective => 2,
        _ => 3
    };
}

/// <summary>Core canonical hashes for versioned policy inputs.</summary>
public static class Phase8ATopologyCostPolicyHasher
{
    public static string ComputeConfigHash(Phase8ATopologyCostPolicyConfig config) => Phase8AMappingAuthorityHasher.Hash(new
    {
        algorithm = "sha256/phase8a-topology-cost-policy-config/v1",
        config.SchemaVersion,
        config.PolicyId,
        config.EnumerationModeId,
        config.Weights
    });

    public static string ComputeProfileConfigHash(Phase8ATopologyCostProfile profile) => Phase8AMappingAuthorityHasher.Hash(new
    {
        algorithm = "sha256/phase8a-topology-cost-profile-config/v1",
        profile
    });

    public static string ComputeBudgetHash(Phase8ATopologyCostSearchBudget budget) => Phase8AMappingAuthorityHasher.Hash(new
    {
        algorithm = "sha256/phase8a-topology-cost-search-budget/v1",
        budget
    });
}

/// <summary>Complete immutable provenance for one verified scored candidate.</summary>
public sealed record Phase8ATopologyCostCandidateProvenance(
    string ProblemCanonicalHash,
    Phase8AMappingBaseHashes BaseHashes,
    string PolicyConfigHash,
    string CostProfileConfigHash,
    string SearchBudgetHash,
    string ProfileBindingHashAlgorithm,
    string ProfileBindingHash,
    int Seed,
    string TopologyHash,
    string RouteHash,
    string SemanticOrderingHash);

/// <summary>One globally verified, scored mapping candidate.</summary>
public sealed class Phase8ATopologyCostCandidate
{
    internal Phase8ATopologyCostCandidate(
        Phase8AMappingCandidateDraft draft,
        Phase8ACandidateVerificationResult verification,
        MappingCandidate descriptor,
        decimal totalScore,
        string candidateHash,
        Phase8ATopologyCostCandidateProvenance provenance)
    {
        Draft = draft;
        Verification = verification;
        Descriptor = descriptor;
        TotalScore = totalScore;
        CandidateHash = candidateHash;
        Provenance = provenance;
    }

    public Phase8AMappingCandidateDraft Draft { get; }
    public Phase8ACandidateVerificationResult Verification { get; }
    public MappingCandidate Descriptor { get; }
    public IReadOnlyList<MappingScoreItem> ScoreBreakdown => Descriptor.ScoreBreakdown;
    public decimal TotalScore { get; }
    public string CandidateHash { get; }
    public Phase8ATopologyCostCandidateProvenance Provenance { get; }
}

public static class Phase8ATopologyCostPolicyIssueCodes
{
    public const string InvalidRequest = "Phase8ATopologyCostPolicyRequestInvalid";
    public const string InvalidConfig = "Phase8ATopologyCostPolicyConfigInvalid";
    public const string InvalidProfile = "Phase8ATopologyCostProfileInvalid";
    public const string InvalidBudget = "Phase8ATopologyCostBudgetInvalid";
    public const string SeedMismatch = "Phase8ATopologyCostSeedMismatch";
    public const string ProblemHashMismatch = "Phase8ATopologyCostProblemHashMismatch";
    public const string ArithmeticOverflow = "Phase8ATopologyCostArithmeticOverflow";
    public const string MalformedProblem = "Phase8ATopologyCostProblemMalformed";
    public const string IncompleteSearch = "Phase8ATopologyCostSearchIncomplete";
    public const string OtherVerifierRejection = "Phase8ACandidateOtherVerifierRejection";
    public const string InvalidPortfolio = "Phase8ATopologyCostPortfolioInvalid";
    public const string PortfolioAuthorityMismatch = "Phase8ATopologyCostPortfolioAuthorityMismatch";
    public const string PartitionVariantInvalid = "Phase8ATopologyCostPartitionVariantInvalid";
    public const string PartitionVariantSetInvalid = "Phase8ATopologyCostPartitionVariantSetInvalid";
}

public static class Phase8ATopologyCostSearchStatuses
{
    public const string Success = "complete-success";
    public const string Infeasible = "complete-infeasible";
    public const string IncompleteBudgetExhausted = "incomplete-policy-budget-exhausted";
    public const string IncompleteProblemSurface = "incomplete-problem-surface";
    public const string Invalid = "invalid-input";
    public const string ArithmeticOverflow = "arithmetic-overflow";
}

public sealed record Phase8ATopologyCostPolicyIssue(
    string Code,
    ValidationSeverity Severity,
    string Location,
    string Message,
    string? RelatedId = null,
    long Count = 1);

/// <summary>Deterministic bounded-search result. Budget exhaustion is never reported as infeasibility.</summary>
public sealed class Phase8ATopologyCostPolicyResult
{
    internal Phase8ATopologyCostPolicyResult(
        string statusId,
        bool isCompleteSearch,
        long visitedSearchNodes,
        long completeDrafts,
        long verifiedDrafts,
        long rejectedDrafts,
        IEnumerable<Phase8ATopologyCostCandidate> candidates,
        IEnumerable<Phase8ATopologyCostPolicyIssue> issues,
        string policyConfigHash,
        string costProfileConfigHash,
        string searchBudgetHash,
        Phase8ATopologyProfileBindingHash? profileBinding)
    {
        StatusId = statusId;
        IsCompleteSearch = isCompleteSearch;
        VisitedSearchNodes = visitedSearchNodes;
        CompleteDrafts = completeDrafts;
        VerifiedDrafts = verifiedDrafts;
        RejectedDrafts = rejectedDrafts;
        Candidates = Array.AsReadOnly(candidates.OrderBy(item => item.TotalScore)
            .ThenBy(item => item.Descriptor.TieBreakKey, StringComparer.Ordinal)
            .ThenBy(item => item.CandidateHash, StringComparer.Ordinal).ToArray());
        Issues = Array.AsReadOnly(issues.OrderBy(item => item.Location, StringComparer.Ordinal)
            .ThenBy(item => item.Code, StringComparer.Ordinal).ToArray());
        PolicyConfigHash = policyConfigHash;
        CostProfileConfigHash = costProfileConfigHash;
        SearchBudgetHash = searchBudgetHash;
        ProfileBinding = profileBinding;
    }

    public string StatusId { get; }
    public bool IsCompleteSearch { get; }
    public long VisitedSearchNodes { get; }
    public long CompleteDrafts { get; }
    public long VerifiedDrafts { get; }
    public long RejectedDrafts { get; }
    public IReadOnlyList<Phase8ATopologyCostCandidate> Candidates { get; }
    public Phase8ATopologyCostCandidate? BestCandidate => Candidates.FirstOrDefault();
    public IReadOnlyList<Phase8ATopologyCostPolicyIssue> Issues { get; }
    public string PolicyConfigHash { get; }
    public string CostProfileConfigHash { get; }
    public string SearchBudgetHash { get; }
    public Phase8ATopologyProfileBindingHash? ProfileBinding { get; }
}

/// <summary>Stable Core-derived partition identities used by the topology-cost portfolio.</summary>
public static class Phase8APartitionVariantKindIds
{
    public const string K = "k-partition";
    public const string N = "n-partition";
    public const string Hybrid = "hybrid-k-n-partition";

    internal static int Order(string kindId) => kindId switch
    {
        K => 0,
        N => 1,
        Hybrid => 2,
        _ => int.MaxValue
    };
}

/// <summary>One exact axis interval in a Core-derived partition signature.</summary>
public sealed record Phase8APartitionAxisInterval(long Offset, long Extent);

/// <summary>Core-derived partition intervals for one workload operation.</summary>
public sealed class Phase8APartitionOperationSignature
{
    internal Phase8APartitionOperationSignature(
        string operationId,
        string loweringSemanticHash,
        IEnumerable<Phase8APartitionAxisInterval> kIntervals,
        IEnumerable<Phase8APartitionAxisInterval> nIntervals)
    {
        OperationId = operationId;
        LoweringSemanticHash = loweringSemanticHash;
        KIntervals = Array.AsReadOnly(kIntervals.OrderBy(item => item.Offset).ThenBy(item => item.Extent).ToArray());
        NIntervals = Array.AsReadOnly(nIntervals.OrderBy(item => item.Offset).ThenBy(item => item.Extent).ToArray());
    }

    public string OperationId { get; }
    public string LoweringSemanticHash { get; }
    public IReadOnlyList<Phase8APartitionAxisInterval> KIntervals { get; }
    public IReadOnlyList<Phase8APartitionAxisInterval> NIntervals { get; }
}

/// <summary>Core-owned partition signature derived from actual lowering intervals, never a caller label.</summary>
public sealed class Phase8APartitionVariantAuthority
{
    internal Phase8APartitionVariantAuthority(
        string kindId,
        string signatureHashAlgorithm,
        string signatureHash,
        IEnumerable<Phase8APartitionOperationSignature> operations)
    {
        KindId = kindId;
        SignatureHashAlgorithm = signatureHashAlgorithm;
        SignatureHash = signatureHash;
        Operations = Array.AsReadOnly(operations.OrderBy(item => item.OperationId, StringComparer.Ordinal).ToArray());
    }

    public string KindId { get; }
    public string SignatureHashAlgorithm { get; }
    public string SignatureHash { get; }
    public IReadOnlyList<Phase8APartitionOperationSignature> Operations { get; }
}

/// <summary>Freezes exactly three K, N, and hybrid policy requests for fair deterministic comparison.</summary>
public sealed class Phase8ATopologyCostPortfolioRequest
{
    public Phase8ATopologyCostPortfolioRequest(IEnumerable<Phase8ATopologyCostPolicyRequest?>? variants)
    {
        Variants = Array.AsReadOnly((variants ?? []).ToArray());
    }

    public IReadOnlyList<Phase8ATopologyCostPolicyRequest?> Variants { get; }
}

/// <summary>One partition variant and its exact request, problem, and policy-result authorities.</summary>
public sealed class Phase8ATopologyCostPortfolioVariant
{
    internal Phase8ATopologyCostPortfolioVariant(
        Phase8APartitionVariantAuthority partition,
        Phase8ATopologyCostPolicyRequest request,
        Phase8ATopologyCostPolicyResult result)
    {
        Partition = partition;
        Request = request;
        Result = result;
    }

    public Phase8APartitionVariantAuthority Partition { get; }
    public Phase8ATopologyCostPolicyRequest Request { get; }
    public Phase8AMappingProblem Problem => Request.Problem!;
    public Phase8ATopologyCostPolicyResult Result { get; }
}

/// <summary>One globally ranked candidate retaining its exact partition-variant authority.</summary>
public sealed class Phase8ATopologyCostPortfolioCandidate
{
    internal Phase8ATopologyCostPortfolioCandidate(
        int globalRank,
        Phase8ATopologyCostPortfolioVariant variant,
        Phase8ATopologyCostCandidate candidate)
    {
        GlobalRank = globalRank;
        Variant = variant;
        Candidate = candidate;
    }

    public int GlobalRank { get; }
    public Phase8ATopologyCostPortfolioVariant Variant { get; }
    public Phase8ATopologyCostCandidate Candidate { get; }
}

public static class Phase8ATopologyCostPortfolioStatuses
{
    public const string Success = "complete-success";
    public const string Infeasible = "complete-infeasible";
    public const string Incomplete = "incomplete-variant-search";
    public const string Invalid = "invalid-input";
    public const string VariantFailure = "variant-search-failed";
}

/// <summary>Immutable deterministic comparison of real K, N, and hybrid mapping searches.</summary>
public sealed class Phase8ATopologyCostPortfolioResult
{
    internal Phase8ATopologyCostPortfolioResult(
        string statusId,
        bool isCompleteComparison,
        IEnumerable<Phase8ATopologyCostPortfolioVariant> variants,
        IEnumerable<Phase8ATopologyCostPolicyIssue> issues,
        string canonicalHash)
    {
        StatusId = statusId;
        IsCompleteComparison = isCompleteComparison;
        Variants = Array.AsReadOnly(variants.OrderBy(item => Phase8APartitionVariantKindIds.Order(item.Partition.KindId)).ToArray());
        Issues = Array.AsReadOnly(issues.OrderBy(item => item.Location, StringComparer.Ordinal).ThenBy(item => item.Code, StringComparer.Ordinal).ToArray());
        var ordered = Variants.SelectMany(variant => variant.Result.Candidates.Select(candidate => (Variant: variant, Candidate: candidate)))
            .OrderBy(item => item.Candidate.TotalScore)
            .ThenBy(item => item.Candidate.Descriptor.TieBreakKey, StringComparer.Ordinal)
            .ThenBy(item => Phase8APartitionVariantKindIds.Order(item.Variant.Partition.KindId))
            .ThenBy(item => item.Candidate.CandidateHash, StringComparer.Ordinal)
            .ToArray();
        RankedCandidates = Array.AsReadOnly(ordered.Select((item, index) =>
            new Phase8ATopologyCostPortfolioCandidate(index, item.Variant, item.Candidate)).ToArray());
        CanonicalHash = canonicalHash;
    }

    public string StatusId { get; }
    public bool IsCompleteComparison { get; }
    public IReadOnlyList<Phase8ATopologyCostPortfolioVariant> Variants { get; }
    public IReadOnlyList<Phase8ATopologyCostPortfolioCandidate> RankedCandidates { get; }
    public Phase8ATopologyCostPortfolioCandidate? BestCandidate => RankedCandidates.FirstOrDefault();
    public IReadOnlyList<Phase8ATopologyCostPolicyIssue> Issues { get; }
    public string CanonicalHashAlgorithm => Phase8ATopologyCostAwarePolicyContract.PartitionPortfolioHashAlgorithm;
    public string CanonicalHash { get; }
}

internal static class Phase8ATopologyCostMath
{
    internal static decimal ShareRange(IEnumerable<decimal> values)
    {
        var array = values.ToArray();
        if (array.Any(value => value < 0)) throw new ArgumentOutOfRangeException(nameof(values));
        var total = array.Aggregate(0m, (sum, value) => checked(sum + value));
        if (array.Length <= 1 || total == 0) return 0;
        var shares = array.Select(value => checked((value / total) * 1_000_000m));
        return Range(shares);
    }

    internal static decimal Range(IEnumerable<decimal> values)
    {
        var array = values.ToArray();
        return array.Length <= 1 ? 0 : checked(array.Max() - array.Min());
    }
}
/// <summary>Deterministic bounded topology/capacity/cost-aware mapping search.</summary>
public static class Phase8ATopologyCostAwarePolicy
{
    public static Phase8ATopologyCostPolicyResult Search(Phase8ATopologyCostPolicyRequest? request)
    {
        var validation = Validate(request);
        if (validation.Count != 0)
            return Empty(Phase8ATopologyCostSearchStatuses.Invalid, validation);

        var problem = request!.Problem!;
        var config = request.Config!;
        var profile = request.Profile!;
        var budget = request.Budget!;
        var configHash = Phase8ATopologyCostPolicyHasher.ComputeConfigHash(config);
        var profileConfigHash = Phase8ATopologyCostPolicyHasher.ComputeProfileConfigHash(profile);
        var budgetHash = Phase8ATopologyCostPolicyHasher.ComputeBudgetHash(budget);
        var profileBinding = Phase8ATopologyProfileBindingHasher.Compute(problem);
        var provenance = new Phase8ATopologyCostCandidateProvenance(
            problem.CanonicalHash,
            problem.BaseHashes,
            configHash,
            profileConfigHash,
            budgetHash,
            profileBinding.AlgorithmId,
            profileBinding.Hash,
            request.Seed,
            problem.BaseHashes.TopologyGraphHash,
            problem.BaseHashes.RouteHash,
            problem.SemanticOrderingHash);
        try
        {
            return new SearchEngine(problem, config, profile, budget, provenance, profileBinding).Run();
        }
        catch (OverflowException)
        {
            return Empty(Phase8ATopologyCostSearchStatuses.ArithmeticOverflow,
                [Issue(Phase8ATopologyCostPolicyIssueCodes.ArithmeticOverflow, "$", "Topology-cost arithmetic exceeded Decimal or Int64.")],
                configHash, profileConfigHash, budgetHash, profileBinding);
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException or KeyNotFoundException)
        {
            return Empty(Phase8ATopologyCostSearchStatuses.Invalid,
                [Issue(Phase8ATopologyCostPolicyIssueCodes.MalformedProblem, "$", "Frozen mapping problem is structurally inconsistent: " + exception.Message)],
                configHash, profileConfigHash, budgetHash, profileBinding);
        }
    }

    public static Phase8ATopologyCostPortfolioResult ComparePartitionVariants(Phase8ATopologyCostPortfolioRequest? request)
    {
        var issues = new List<Phase8ATopologyCostPolicyIssue>();
        if (request is null)
        {
            issues.Add(Issue(Phase8ATopologyCostPolicyIssueCodes.InvalidPortfolio, "$", "A K/N/hybrid portfolio request is required."));
            return EmptyPortfolio(issues);
        }
        if (request.Variants.Count != 3)
            issues.Add(Issue(Phase8ATopologyCostPolicyIssueCodes.PartitionVariantSetInvalid, "$.variants", "Exactly three real K, N, and hybrid policy requests are required."));

        var validated = new List<(Phase8ATopologyCostPolicyRequest Request, Phase8APartitionVariantAuthority Partition)>();
        for (var index = 0; index < request.Variants.Count; index++)
        {
            var variant = request.Variants[index];
            if (variant is null)
            {
                issues.Add(Issue(Phase8ATopologyCostPolicyIssueCodes.InvalidPortfolio, $"$.variants[{index}]", "A non-null policy request is required."));
                continue;
            }
            foreach (var issue in Validate(variant))
                issues.Add(RebasePortfolioIssue(issue, index));
            if (!TryDerivePartitionAuthority(variant.Problem, out var partition, out var reason))
                issues.Add(Issue(Phase8ATopologyCostPolicyIssueCodes.PartitionVariantInvalid, $"$.variants[{index}].problem.loweringAuthorities", reason));
            else
                validated.Add((variant, partition!));
        }

        if (validated.Count == 3)
        {
            var requiredKinds = new[]
            {
                Phase8APartitionVariantKindIds.K,
                Phase8APartitionVariantKindIds.N,
                Phase8APartitionVariantKindIds.Hybrid
            };
            var actualKinds = validated.Select(item => item.Partition.KindId).OrderBy(Phase8APartitionVariantKindIds.Order).ToArray();
            if (!actualKinds.SequenceEqual(requiredKinds))
                issues.Add(Issue(Phase8ATopologyCostPolicyIssueCodes.PartitionVariantSetInvalid, "$.variants", "The portfolio must contain exactly one Core-derived K, N, and hybrid lowering variant."));

            if (validated.Select(item => PortfolioComparableAuthorityHash(item.Request.Problem!)).Distinct(StringComparer.Ordinal).Count() != 1)
                issues.Add(Issue(Phase8ATopologyCostPolicyIssueCodes.PortfolioAuthorityMismatch, "$.variants", "All variants must bind the same normalized workload, base mapping, topology, placement, routes, logical-path catalog, capabilities, and problem budget."));
            if (validated.Select(item => Phase8ATopologyCostPolicyHasher.ComputeConfigHash(item.Request.Config!)).Distinct(StringComparer.Ordinal).Count() != 1)
                issues.Add(Issue(Phase8ATopologyCostPolicyIssueCodes.PortfolioAuthorityMismatch, "$.variants.config", "All variants must use the exact same versioned policy configuration and weights."));
            if (validated.Select(item => Phase8ATopologyCostPolicyHasher.ComputeProfileConfigHash(item.Request.Profile!)).Distinct(StringComparer.Ordinal).Count() != 1)
                issues.Add(Issue(Phase8ATopologyCostPolicyIssueCodes.PortfolioAuthorityMismatch, "$.variants.profile", "All variants must use the exact same analytical profile."));
            if (validated.Select(item => Phase8ATopologyCostPolicyHasher.ComputeBudgetHash(item.Request.Budget!)).Distinct(StringComparer.Ordinal).Count() != 1)
                issues.Add(Issue(Phase8ATopologyCostPolicyIssueCodes.PortfolioAuthorityMismatch, "$.variants.budget", "All variants must use the exact same search budget."));
            if (validated.Select(item => item.Request.Seed).Distinct().Count() != 1)
                issues.Add(Issue(Phase8ATopologyCostPolicyIssueCodes.PortfolioAuthorityMismatch, "$.variants.seed", "All variants must use the exact same explicit seed."));
        }

        if (issues.Count != 0)
            return EmptyPortfolio(issues);

        var variants = validated.Select(item => new Phase8ATopologyCostPortfolioVariant(
            item.Partition, item.Request, Search(item.Request))).ToArray();
        var executionFailed = variants.Any(item => item.Result.StatusId is Phase8ATopologyCostSearchStatuses.Invalid or Phase8ATopologyCostSearchStatuses.ArithmeticOverflow);
        var complete = !executionFailed && variants.All(item => item.Result.IsCompleteSearch);
        var status = executionFailed
            ? Phase8ATopologyCostPortfolioStatuses.VariantFailure
            : !complete
                ? Phase8ATopologyCostPortfolioStatuses.Incomplete
                : variants.Any(item => item.Result.Candidates.Count != 0)
                    ? Phase8ATopologyCostPortfolioStatuses.Success
                    : Phase8ATopologyCostPortfolioStatuses.Infeasible;
        return new Phase8ATopologyCostPortfolioResult(
            status, complete, variants, [], ComputePortfolioHash(status, complete, variants, []));
    }

    private static Phase8ATopologyCostPolicyIssue RebasePortfolioIssue(Phase8ATopologyCostPolicyIssue issue, int index)
    {
        var suffix = issue.Location == "$" ? "" : issue.Location.StartsWith("$", StringComparison.Ordinal) ? issue.Location[1..] : "." + issue.Location;
        return new Phase8ATopologyCostPolicyIssue(issue.Code, issue.Severity, $"$.variants[{index}]" + suffix, issue.Message, issue.RelatedId, issue.Count);
    }

    private static Phase8ATopologyCostPortfolioResult EmptyPortfolio(IEnumerable<Phase8ATopologyCostPolicyIssue> issues)
    {
        var frozen = issues.ToArray();
        return new Phase8ATopologyCostPortfolioResult(
            Phase8ATopologyCostPortfolioStatuses.Invalid, false, [], frozen,
            ComputePortfolioHash(Phase8ATopologyCostPortfolioStatuses.Invalid, false, [], frozen));
    }

    private static string PortfolioComparableAuthorityHash(Phase8AMappingProblem problem) => Phase8AMappingAuthorityHasher.Hash(new
    {
        algorithm = "sha256/phase8a-topology-cost-portfolio-comparable-authority/v1",
        workloadHash = Phase8AMappingAuthorityHasher.ComputeWorkloadHash(problem.NormalizedInput.Workload),
        problem.BaseHashes.NormalizedInputHash,
        problem.BaseHashes.BaseMappingHash,
        problem.BaseHashes.CapabilityContentHash,
        problem.BaseHashes.TopologyGraphHash,
        problem.BaseHashes.PlacementHash,
        problem.BaseHashes.RouteHash,
        problem.BaseHashes.TopologyManifestHash,
        problem.BaseHashes.LogicalPathCatalogHash,
        problem.PolicyBudget,
        problem.ManualTargetConstraints,
        problem.ManualOperandPathConstraints,
        problem.ManualCollectiveConstraints
    });

    private static bool TryDerivePartitionAuthority(
        Phase8AMappingProblem? problem,
        out Phase8APartitionVariantAuthority? partition,
        out string reason)
    {
        partition = null;
        reason = "The partition authority is malformed.";
        if (problem is null || problem.LoweringAuthorities.Count == 0)
            return false;

        var signatures = new List<Phase8APartitionOperationSignature>();
        var kinds = new List<string>();
        foreach (var lowering in problem.LoweringAuthorities)
        {
            var tiles = lowering.Plan.OperationTiles;
            if (tiles.Count == 0)
            {
                reason = "Every lowering must expose non-empty operation tiles.";
                return false;
            }
            var kIntervals = tiles.Select(item => new Phase8APartitionAxisInterval(item.KRange.Offset, item.KRange.Extent))
                .Distinct().OrderBy(item => item.Offset).ThenBy(item => item.Extent).ToArray();
            var nIntervals = tiles.Select(item => new Phase8APartitionAxisInterval(item.NRange.Offset, item.NRange.Extent))
                .Distinct().OrderBy(item => item.Offset).ThenBy(item => item.Extent).ToArray();
            var kind = kIntervals.Length > 1 && nIntervals.Length > 1
                ? Phase8APartitionVariantKindIds.Hybrid
                : kIntervals.Length > 1
                    ? Phase8APartitionVariantKindIds.K
                    : nIntervals.Length > 1
                        ? Phase8APartitionVariantKindIds.N
                        : "";
            if (kind.Length == 0)
            {
                reason = "An unpartitioned lowering cannot impersonate a K, N, or hybrid portfolio variant.";
                return false;
            }
            kinds.Add(kind);
            signatures.Add(new Phase8APartitionOperationSignature(
                lowering.Plan.OperationId, lowering.SemanticHash, kIntervals, nIntervals));
        }
        if (kinds.Distinct(StringComparer.Ordinal).Count() != 1)
        {
            reason = "Every operation in one portfolio problem must use the same Core-derived partition axes.";
            return false;
        }

        var kindId = kinds[0];
        var ordered = signatures.OrderBy(item => item.OperationId, StringComparer.Ordinal).ToArray();
        var hash = Phase8AMappingAuthorityHasher.Hash(new
        {
            algorithm = Phase8ATopologyCostAwarePolicyContract.PartitionSignatureHashAlgorithm,
            kindId,
            operations = ordered.Select(item => new
            {
                item.OperationId,
                item.LoweringSemanticHash,
                k = item.KIntervals,
                n = item.NIntervals
            })
        });
        partition = new Phase8APartitionVariantAuthority(
            kindId, Phase8ATopologyCostAwarePolicyContract.PartitionSignatureHashAlgorithm, hash, ordered);
        reason = "";
        return true;
    }

    private static string ComputePortfolioHash(
        string status,
        bool complete,
        IEnumerable<Phase8ATopologyCostPortfolioVariant> variants,
        IEnumerable<Phase8ATopologyCostPolicyIssue> issues)
    {
        var ordered = variants.OrderBy(item => Phase8APartitionVariantKindIds.Order(item.Partition.KindId)).ToArray();
        var rankedCandidateHashes = ordered.SelectMany(variant => variant.Result.Candidates.Select(candidate => (Variant: variant, Candidate: candidate)))
            .OrderBy(item => item.Candidate.TotalScore)
            .ThenBy(item => item.Candidate.Descriptor.TieBreakKey, StringComparer.Ordinal)
            .ThenBy(item => Phase8APartitionVariantKindIds.Order(item.Variant.Partition.KindId))
            .ThenBy(item => item.Candidate.CandidateHash, StringComparer.Ordinal)
            .Select(item => item.Candidate.CandidateHash)
            .ToArray();
        return Phase8AMappingAuthorityHasher.Hash(new
        {
            algorithm = Phase8ATopologyCostAwarePolicyContract.PartitionPortfolioHashAlgorithm,
            status,
            complete,
            variants = ordered.Select(item => new
            {
                item.Partition.KindId,
                item.Partition.SignatureHashAlgorithm,
                item.Partition.SignatureHash,
                problemCanonicalHash = item.Problem.CanonicalHash,
                item.Result.StatusId,
                item.Result.IsCompleteSearch,
                item.Result.VisitedSearchNodes,
                item.Result.CompleteDrafts,
                item.Result.VerifiedDrafts,
                item.Result.RejectedDrafts,
                item.Result.PolicyConfigHash,
                item.Result.CostProfileConfigHash,
                item.Result.SearchBudgetHash,
                profileBinding = item.Result.ProfileBinding,
                candidateHashes = item.Result.Candidates.Select(candidate => candidate.CandidateHash),
                resultIssues = item.Result.Issues
            }),
            rankedCandidateHashes,
            issues = issues.OrderBy(item => item.Location, StringComparer.Ordinal).ThenBy(item => item.Code, StringComparer.Ordinal)
        });
    }

    private static List<Phase8ATopologyCostPolicyIssue> Validate(Phase8ATopologyCostPolicyRequest? request)
    {
        var issues = new List<Phase8ATopologyCostPolicyIssue>();
        if (request is null) return [Issue(Phase8ATopologyCostPolicyIssueCodes.InvalidRequest, "$", "A policy request is required.")];
        if (request.Problem is null || request.Config is null || request.Profile is null || request.Budget is null)
            return [Issue(Phase8ATopologyCostPolicyIssueCodes.InvalidRequest, "$", "Problem, config, profile, and budget are required.")];
        var problem = request.Problem;
        var config = request.Config;
        var profile = request.Profile;
        var budget = request.Budget;
        if (string.IsNullOrWhiteSpace(problem.CanonicalHash) || !string.Equals(problem.CanonicalHash, request.FrozenProblemCanonicalHash, StringComparison.Ordinal))
            issues.Add(Issue(Phase8ATopologyCostPolicyIssueCodes.ProblemHashMismatch, "$.problem.canonicalHash", "The frozen problem canonical hash changed."));
        if (config.SchemaVersion != Phase8ATopologyCostAwarePolicyContract.CurrentConfigVersion ||
            config.PolicyId != ReferenceMappingPolicyIds.TopologyCostAwareV1 ||
            config.PolicyId != problem.NormalizedInput.PolicyId ||
            config.EnumerationModeId != Phase8ATopologyCostAwarePolicyContract.DeterministicEnumerationMode ||
            config.Weights is null || Weights(config.Weights).Any(value => value < 0) || Weights(config.Weights).All(value => value == 0))
            issues.Add(Issue(Phase8ATopologyCostPolicyIssueCodes.InvalidConfig, "$.config", "Only the versioned deterministic topology-cost policy with non-negative, non-zero aggregate weights is supported."));
        if (profile.SchemaVersion != Phase8ATopologyCostAwarePolicyContract.CurrentProfileVersion ||
            profile.ProfileId != Phase8ATopologyCostAwarePolicyContract.StaticCostProfileId || profile.WordBits <= 0 ||
            profile.PathCostModelId != Phase8ATopologyCostAwarePolicyContract.PathCostModelId ||
            profile.ImbalanceModelId != Phase8ATopologyCostAwarePolicyContract.ImbalanceModelId ||
            profile.EnergyAccountingModeId != Phase8ATopologyCostAwarePolicyContract.NoEnergyModelId)
            issues.Add(Issue(Phase8ATopologyCostPolicyIssueCodes.InvalidProfile, "$.profile", "The profile must use the versioned static path/imbalance model and explicitly state that energy is not estimated."));
        if (budget.SchemaVersion != Phase8ATopologyCostAwarePolicyContract.CurrentBudgetVersion || budget.MaxSearchNodes <= 0 ||
            budget.MaxSearchNodes > problem.PolicyBudget.MaxSearchNodes || budget.MaxRetainedCandidates <= 0 || budget.MaxRetainedCandidates > budget.MaxSearchNodes)
            issues.Add(Issue(Phase8ATopologyCostPolicyIssueCodes.InvalidBudget, "$.budget", "The versioned policy budget must be positive and cannot exceed the problem-authorized MaxSearchNodes."));
        if (request.Seed != problem.NormalizedInput.Seed)
            issues.Add(Issue(Phase8ATopologyCostPolicyIssueCodes.SeedMismatch, "$.seed", "The explicit provenance seed must equal the frozen normalized-input seed; deterministic enumeration does not use it as randomness."));
        return issues;
    }

    private static IEnumerable<decimal> Weights(Phase8ATopologyCostWeights value)
    {
        yield return value.AnalyticalLatencyWeight;
        yield return value.BitHopWeight;
        yield return value.DistanceTrafficWeight;
        yield return value.PredictedPeakBufferWeight;
        yield return value.PeImbalanceWeight;
        yield return value.StorageImbalanceWeight;
        yield return value.LinkImbalanceWeight;
        yield return value.LinkHotspotWeight;
        yield return value.ElectricalMetalBitHopWeight;
        yield return value.OpticalWaveguideBitHopWeight;
        yield return value.ThermalControlBitHopWeight;
        yield return value.ConversionTransitionWeight;
        yield return value.StaticServiceCongestionWeight;
    }

    private static Phase8ATopologyCostPolicyResult Empty(
        string status,
        IEnumerable<Phase8ATopologyCostPolicyIssue> issues,
        string configHash = "",
        string profileHash = "",
        string budgetHash = "",
        Phase8ATopologyProfileBindingHash? binding = null) =>
        new(status, false, 0, 0, 0, 0, [], issues, configHash, profileHash, budgetHash, binding);

    private static Phase8ATopologyCostPolicyIssue Issue(string code, string location, string message, string? relatedId = null) =>
        new(code, ValidationSeverity.Error, location, message, relatedId);

    private sealed class SearchEngine
    {
        private readonly Phase8AMappingProblem _problem;
        private readonly Phase8ATopologyCostPolicyConfig _config;
        private readonly Phase8ATopologyCostProfile _profile;
        private readonly Phase8ATopologyCostSearchBudget _budget;
        private readonly Phase8ATopologyCostCandidateProvenance _provenance;
        private readonly Phase8ATopologyProfileBindingHash _profileBinding;
        private readonly IReadOnlyList<Phase8AMappingOperationProblem> _operations;
        private readonly IReadOnlyList<Phase8AMappingCollectiveRequirement> _collectives;
        private readonly Dictionary<string, int> _pathOrdinals;
        private readonly List<Phase8AOperationCandidateSelection> _operationSelections = [];
        private readonly List<Phase8ACollectiveCandidateSelection> _collectiveSelections = [];
        private readonly Dictionary<string, (string ComponentId, string ResultPortId)> _produced = new(StringComparer.Ordinal);
        private readonly List<Phase8ATopologyCostCandidate> _retained = [];
        private long _visited;
        private long _completeDrafts;
        private long _verifiedDrafts;
        private long _rejectedDrafts;
        private readonly SortedDictionary<string, long> _verifierRejectionCounts = new(StringComparer.Ordinal);
        private bool _budgetExhausted;

        public SearchEngine(
            Phase8AMappingProblem problem,
            Phase8ATopologyCostPolicyConfig config,
            Phase8ATopologyCostProfile profile,
            Phase8ATopologyCostSearchBudget budget,
            Phase8ATopologyCostCandidateProvenance provenance,
            Phase8ATopologyProfileBindingHash profileBinding)
        {
            _problem = problem;
            _config = config;
            _profile = profile;
            _budget = budget;
            _provenance = provenance;
            _profileBinding = profileBinding;
            _operations = problem.Operations.OrderBy(item => item.OperationOrdinal)
                .ThenBy(item => item.Tile.MRange.Offset).ThenBy(item => item.Tile.KRange.Offset).ThenBy(item => item.Tile.NRange.Offset).ToArray();
            _collectives = problem.Collectives.OrderBy(item => item.OperationOrdinal)
                .ThenBy(item => item.Intent.StageOrder).ThenBy(item => item.Intent.MRange.Offset).ThenBy(item => item.Intent.NRange.Offset).ToArray();
            _pathOrdinals = problem.NormalizedInput.PathOrdinals.ToDictionary(item => item.PathId, item => item.Ordinal, StringComparer.Ordinal);
        }

        public Phase8ATopologyCostPolicyResult Run()
        {
            EnumerateOperations(0);
            var complete = !_budgetExhausted && _problem.SearchStatus.IsCompleteSearch;
            var status = _budgetExhausted
                ? Phase8ATopologyCostSearchStatuses.IncompleteBudgetExhausted
                : !_problem.SearchStatus.IsCompleteSearch
                    ? Phase8ATopologyCostSearchStatuses.IncompleteProblemSurface
                    : _verifiedDrafts == 0
                        ? Phase8ATopologyCostSearchStatuses.Infeasible
                        : Phase8ATopologyCostSearchStatuses.Success;
            var incomplete = !complete && (status == Phase8ATopologyCostSearchStatuses.IncompleteBudgetExhausted || status == Phase8ATopologyCostSearchStatuses.IncompleteProblemSurface);
            var resultIssues = VerifierRejectionSummaries(status).ToList();
            var candidates = _retained.ToArray();
            if (resultIssues.Count != 0 && candidates.Length != 0)
            {
                var warnings = resultIssues.Select(item => new WorkloadMappingV2Issue(
                    item.Code,
                    ValidationSeverity.Warning,
                    "$.candidate.searchRejections",
                    "Bounded search rejected " + item.Count.ToString(CultureInfo.InvariantCulture) + " verifier issue occurrence(s) with code " + item.Code + "."))
                    .ToArray();
                candidates = candidates.Select(candidate => WithCandidateIssues(candidate, warnings, "verifier-rejection-summary-v1")).ToArray();
            }
            if (incomplete)
            {
                resultIssues.Add(new Phase8ATopologyCostPolicyIssue(
                    Phase8ATopologyCostPolicyIssueCodes.IncompleteSearch,
                    ValidationSeverity.Warning,
                    "$.search",
                    "Candidate ranking covers only an incomplete search surface and must not be presented as a complete optimum. Status=" + status + "; problemTruncation=" + _problem.SearchStatus.TruncatedReasonId + "."));
                candidates = candidates.Select(candidate => WithIncompleteDisclosure(candidate, status)).ToArray();
            }
            return new Phase8ATopologyCostPolicyResult(status, complete, _visited, _completeDrafts, _verifiedDrafts, _rejectedDrafts,
                candidates, resultIssues, _provenance.PolicyConfigHash, _provenance.CostProfileConfigHash, _provenance.SearchBudgetHash, _profileBinding);
        }

        private IReadOnlyList<Phase8ATopologyCostPolicyIssue> VerifierRejectionSummaries(string status)
        {
            var severity = status == Phase8ATopologyCostSearchStatuses.Infeasible ? ValidationSeverity.Error : ValidationSeverity.Warning;
            return _verifierRejectionCounts.Select(item => new Phase8ATopologyCostPolicyIssue(
                item.Key,
                severity,
                "$.candidateVerifier.rejections",
                "Candidate verifier reported " + item.Value.ToString(CultureInfo.InvariantCulture) + " rejected issue occurrence(s) with code " + item.Key + ".",
                null,
                item.Value)).ToArray();
        }

        private Phase8ATopologyCostCandidate WithIncompleteDisclosure(Phase8ATopologyCostCandidate candidate, string status)
        {
            var warning = new WorkloadMappingV2Issue(
                Phase8ATopologyCostPolicyIssueCodes.IncompleteSearch,
                ValidationSeverity.Warning,
                "$.candidate",
                "This candidate was retained from an incomplete search and is not a proven complete optimum. Status=" + status + "; problemTruncation=" + _problem.SearchStatus.TruncatedReasonId + ".");
            return WithCandidateIssues(candidate, [warning], "incomplete-search-disclosure-v1");
        }

        private static Phase8ATopologyCostCandidate WithCandidateIssues(
            Phase8ATopologyCostCandidate candidate,
            IEnumerable<WorkloadMappingV2Issue> additions,
            string disclosureId)
        {
            var issues = candidate.Descriptor.Issues.Concat(additions).ToArray();
            if (issues.Length == candidate.Descriptor.Issues.Count) return candidate;
            var disclosedHash = Phase8AMappingAuthorityHasher.Hash(new
            {
                algorithm = "sha256/phase8a-topology-cost-candidate-disclosure/v1",
                candidate.CandidateHash,
                disclosureId,
                issues
            });
            var descriptor = new MappingCandidate(
                "phase8a-tca-" + disclosedHash[..24],
                candidate.Descriptor.PolicyId,
                candidate.Descriptor.PolicyConfigHash,
                issues,
                candidate.ScoreBreakdown,
                candidate.Descriptor.TopologyHash,
                candidate.Descriptor.RouteHash,
                candidate.Descriptor.ProfileHash,
                candidate.Descriptor.TieBreakKey,
                candidate.Descriptor.ManualDiff);
            return new Phase8ATopologyCostCandidate(candidate.Draft, candidate.Verification, descriptor,
                candidate.TotalScore, disclosedHash, candidate.Provenance);
        }

        private static string BoundedVerifierCode(string code) => code switch
        {
            Phase8ACandidateVerificationIssueCodes.CoverageInvalid => Phase8ACandidateVerificationIssueCodes.CoverageInvalid,
            Phase8ACandidateVerificationIssueCodes.SelectionInvalid => Phase8ACandidateVerificationIssueCodes.SelectionInvalid,
            Phase8ACandidateVerificationIssueCodes.ManualConstraintViolated => Phase8ACandidateVerificationIssueCodes.ManualConstraintViolated,
            Phase8ACandidateVerificationIssueCodes.RouteInvalid => Phase8ACandidateVerificationIssueCodes.RouteInvalid,
            Phase8ACandidateVerificationIssueCodes.CapacityExceeded => Phase8ACandidateVerificationIssueCodes.CapacityExceeded,
            Phase8ACandidateVerificationIssueCodes.ResidentSlotsExceeded => Phase8ACandidateVerificationIssueCodes.ResidentSlotsExceeded,
            _ => Phase8ATopologyCostPolicyIssueCodes.OtherVerifierRejection
        };

        private void EnumerateOperations(int index)
        {
            if (_budgetExhausted) return;
            if (index == _operations.Count)
            {
                EnumerateCollectives(0);
                return;
            }
            var operation = _operations[index];
            foreach (var option in operation.TargetOptions.OrderBy(item => item.TargetOrdinal).ThenBy(item => item.StorageOrdinal))
            {
                if (!VisitNode()) return;
                foreach (var activationPath in StablePaths(option.ActivationPathIds))
                {
                    if (!VisitNode()) return;
                    foreach (var weightPath in StablePaths(option.WeightPathIds))
                    {
                        if (!VisitNode()) return;
                        var selection = new Phase8AOperationCandidateSelection(operation.Tile.OperationTileId, option.TargetComponentId,
                            option.ActivationPortId, option.WeightPortId, option.ResultPortId, option.StorageResourceId, activationPath, weightPath);
                        _operationSelections.Add(selection);
                        _produced[operation.Output.TileId] = (selection.TargetComponentId, selection.ResultPortId);
                        EnumerateOperations(index + 1);
                        _produced.Remove(operation.Output.TileId);
                        _operationSelections.RemoveAt(_operationSelections.Count - 1);
                        if (_budgetExhausted) return;
                    }
                }
            }
        }

        private void EnumerateCollectives(int index)
        {
            if (_budgetExhausted) return;
            if (index == _collectives.Count)
            {
                EvaluateCompleteDraft();
                return;
            }
            var collective = _collectives[index];
            foreach (var target in collective.TargetOptions.OrderBy(item => item.TargetOrdinal))
            {
                if (!VisitNode()) return;
                var choices = ContributorPathChoices(collective, target);
                if (choices is null) continue;
                EnumerateContributorRoutes(collective, target, choices, 0, [], index);
                if (_budgetExhausted) return;
            }
        }

        private void EnumerateContributorRoutes(
            Phase8AMappingCollectiveRequirement collective,
            Phase8AMappingCollectiveTargetOption target,
            IReadOnlyList<(string ContributorId, IReadOnlyList<string> Paths)> choices,
            int contributorIndex,
            List<Phase8ACollectiveContributorRoute> routes,
            int collectiveIndex)
        {
            if (_budgetExhausted) return;
            if (contributorIndex == choices.Count)
            {
                var selection = new Phase8ACollectiveCandidateSelection(collective.Intent.IntentId, target.TargetComponentId,
                    target.InputPortId, target.ResultPortId, Array.AsReadOnly(routes.ToArray()));
                _collectiveSelections.Add(selection);
                _produced[collective.Intent.ResultTileId] = (selection.TargetComponentId, selection.ResultPortId);
                EnumerateCollectives(collectiveIndex + 1);
                _produced.Remove(collective.Intent.ResultTileId);
                _collectiveSelections.RemoveAt(_collectiveSelections.Count - 1);
                return;
            }
            var choice = choices[contributorIndex];
            foreach (var path in choice.Paths)
            {
                if (!VisitNode()) return;
                routes.Add(new Phase8ACollectiveContributorRoute(choice.ContributorId, path));
                EnumerateContributorRoutes(collective, target, choices, contributorIndex + 1, routes, collectiveIndex);
                routes.RemoveAt(routes.Count - 1);
                if (_budgetExhausted) return;
            }
        }

        private IReadOnlyList<(string ContributorId, IReadOnlyList<string> Paths)>? ContributorPathChoices(
            Phase8AMappingCollectiveRequirement collective,
            Phase8AMappingCollectiveTargetOption target)
        {
            var destinationHardware = HardwarePortName(target.TargetComponentId, target.InputPortId);
            var result = new List<(string ContributorId, IReadOnlyList<string> Paths)>();
            foreach (var contributor in collective.Intent.ContributorTileIds)
            {
                if (!_produced.TryGetValue(contributor, out var producer)) return null;
                var sourceHardware = HardwarePortName(producer.ComponentId, producer.ResultPortId);
                var paths = _problem.RouteMatrix.Find(producer.ComponentId, sourceHardware, target.TargetComponentId, destinationHardware)
                    .Where(item => item.Source.CapabilityPortId == producer.ResultPortId && item.Destination.CapabilityPortId == target.InputPortId)
                    .OrderBy(item => item.PathOrdinal).Select(item => item.PathId).Distinct(StringComparer.Ordinal).ToArray();
                if (paths.Length == 0) return null;
                result.Add((contributor, Array.AsReadOnly(paths)));
            }
            return result;
        }

        private void EvaluateCompleteDraft()
        {
            _completeDrafts = checked(_completeDrafts + 1);
            var draft = new Phase8AMappingCandidateDraft(_operationSelections.ToArray(), _collectiveSelections.ToArray());
            var verification = Phase8AMappingCandidateVerifier.Verify(_problem, draft);
            if (!verification.IsSuccess)
            {
                _rejectedDrafts = checked(_rejectedDrafts + 1);
                foreach (var issue in verification.Issues)
                {
                    var code = BoundedVerifierCode(issue.Code);
                    _verifierRejectionCounts[code] = checked(_verifierRejectionCounts.GetValueOrDefault(code) + 1);
                }
                return;
            }
            _verifiedDrafts = checked(_verifiedDrafts + 1);
            var tieBreak = TieBreakKey(draft);
            var score = Score(_problem, draft, verification, _config, _profile);
            var candidateHash = Phase8AMappingAuthorityHasher.Hash(new
            {
                algorithm = "sha256/phase8a-topology-cost-candidate/v1",
                provenance = _provenance,
                operations = draft.OperationSelections,
                collectives = draft.CollectiveSelections,
                allocations = verification.Allocations,
                score.Items,
                score.Total,
                tieBreak
            });
            var descriptor = new MappingCandidate(
                "phase8a-tca-" + candidateHash[..24],
                _config.PolicyId,
                _provenance.PolicyConfigHash,
                [],
                score.Items,
                _problem.BaseHashes.TopologyGraphHash,
                _problem.BaseHashes.RouteHash,
                _problem.CapabilityAuthority.ContentHash,
                tieBreak,
                []);
            Retain(new Phase8ATopologyCostCandidate(draft, verification, descriptor, score.Total, candidateHash, _provenance));
        }

        private void Retain(Phase8ATopologyCostCandidate candidate)
        {
            _retained.Add(candidate);
            _retained.Sort((left, right) =>
            {
                var score = left.TotalScore.CompareTo(right.TotalScore);
                if (score != 0) return score;
                var tie = StringComparer.Ordinal.Compare(left.Descriptor.TieBreakKey, right.Descriptor.TieBreakKey);
                return tie != 0 ? tie : StringComparer.Ordinal.Compare(left.CandidateHash, right.CandidateHash);
            });
            if (_retained.Count > _budget.MaxRetainedCandidates) _retained.RemoveAt(_retained.Count - 1);
        }

        private string TieBreakKey(Phase8AMappingCandidateDraft draft)
        {
            var parts = new List<string>();
            foreach (var operation in _operations)
            {
                var selection = draft.OperationSelections.Single(item => item.OperationTileId == operation.Tile.OperationTileId);
                var option = operation.TargetOptions.Single(item => item.TargetComponentId == selection.TargetComponentId &&
                    item.ActivationPortId == selection.ActivationPortId && item.WeightPortId == selection.WeightPortId &&
                    item.ResultPortId == selection.ResultPortId && item.StorageResourceId == selection.StorageResourceId);
                parts.Add(string.Format(CultureInfo.InvariantCulture, "o:{0:D10}:{1:D10}:{2:D10}:{3:D10}",
                    option.TargetOrdinal, option.StorageOrdinal, _pathOrdinals[selection.ActivationPathId], _pathOrdinals[selection.WeightPathId]));
            }
            foreach (var collective in _collectives)
            {
                var selection = draft.CollectiveSelections.Single(item => item.CollectiveIntentId == collective.Intent.IntentId);
                var option = collective.TargetOptions.Single(item => item.TargetComponentId == selection.TargetComponentId && item.InputPortId == selection.InputPortId && item.ResultPortId == selection.ResultPortId);
                var routes = collective.Intent.ContributorTileIds.Select(id => selection.ContributorRoutes.Single(item => item.ContributorTileId == id).PathId)
                    .Select(id => _pathOrdinals[id].ToString("D10", CultureInfo.InvariantCulture));
                parts.Add("c:" + option.TargetOrdinal.ToString("D10", CultureInfo.InvariantCulture) + ":" + string.Join(":", routes));
            }
            return string.Join("|", parts);
        }

        private IReadOnlyList<string> StablePaths(IEnumerable<string> paths) => paths.OrderBy(path => _pathOrdinals[path]).ThenBy(path => path, StringComparer.Ordinal).ToArray();

        private string HardwarePortName(string componentId, string portId) => _problem.CapabilityAuthority.PortBindings.Single(item =>
            item.ComponentId == componentId && item.CapabilityPortId == portId).HardwarePortName;

        private bool VisitNode()
        {
            if (_visited >= _budget.MaxSearchNodes)
            {
                _budgetExhausted = true;
                return false;
            }
            _visited = checked(_visited + 1);
            return true;
        }
    }

    private sealed record ScoreResult(IReadOnlyList<MappingScoreItem> Items, decimal Total);

    private static ScoreResult Score(
        Phase8AMappingProblem problem,
        Phase8AMappingCandidateDraft draft,
        Phase8ACandidateVerificationResult verification,
        Phase8ATopologyCostPolicyConfig config,
        Phase8ATopologyCostProfile profile)
    {
        var accumulator = new StaticCostAccumulator(problem, profile.WordBits);
        var allocationByTile = verification.Allocations.ToDictionary(item => item.OperationTileId, StringComparer.Ordinal);
        var outputTiles = problem.LoweringAuthorities.SelectMany(item => item.Plan.OutputTiles)
            .GroupBy(item => item.TileId, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.Single(), StringComparer.Ordinal);
        var activationDemands = new List<Phase8AActivationRouteDemand>();
        foreach (var operation in problem.Operations)
        {
            var selection = draft.OperationSelections.Single(item => item.OperationTileId == operation.Tile.OperationTileId);
            var activationBits = TensorBits(operation.Activation.PaddedShape, operation.Activation.PrecisionId);
            var activationIngress = problem.NormalizedInput.OperandIngressBindings.Single(item =>
                item.OperationId == operation.Tile.OperationId && item.OperandRoleId == Phase8ATensorRoleIds.Activation);
            activationDemands.Add(new Phase8AActivationRouteDemand(
                operation.Tile.OperationTileId,
                operation.Activation.TileId,
                activationIngress.ProducerComponentId,
                selection.TargetComponentId,
                selection.ActivationPathId,
                activationBits));
            if (!allocationByTile[operation.Tile.OperationTileId].ReusedExistingAllocation)
                accumulator.AddFlow(selection.WeightPathId, operation.TargetOptions.Single(item =>
                    item.TargetComponentId == selection.TargetComponentId && item.StorageResourceId == selection.StorageResourceId &&
                    item.WeightPortId == selection.WeightPortId).RequiredWeightBits, "weight", selection.TargetComponentId);
            accumulator.AddPeWork(selection.TargetComponentId, TensorElements(operation.Tile.PaddedShape));
        }
        foreach (var distribution in Phase8AActivationDistributionPlanner.Plan(activationDemands, problem.LogicalPathCatalog))
            accumulator.AddActivationDistribution(distribution);
        foreach (var collective in problem.Collectives)
        {
            var selection = draft.CollectiveSelections.Single(item => item.CollectiveIntentId == collective.Intent.IntentId);
            foreach (var contributor in selection.ContributorRoutes)
            {
                if (!outputTiles.TryGetValue(contributor.ContributorTileId, out var output))
                    throw new InvalidOperationException("Collective contributor output tile is absent from lowering authority.");
                var bits = TensorBits(output.PaddedShape, output.PrecisionId);
                accumulator.AddFlow(contributor.PathId, bits,
                    collective.Intent.KindId == Phase8ACollectiveIntentKinds.Sum ? "reduction" : "assembly",
                    selection.TargetComponentId);
            }
        }
        accumulator.AddStorage(verification);
        var values = accumulator.Finish();
        var weights = config.Weights;
        var items = new[]
        {
            Item("traffic.total.bits", values.TotalBits, 0m, "bit", "branch-aware-activation-plus-independent-other-static-demand/v1"),
            Item("traffic.total.words", values.TotalWords, 0m, "word-" + profile.WordBits, "branch-aware-activation-plus-independent-other-static-demand/v1"),
            Item("traffic.activation.bits", values.ActivationBits, 0m, "bit", "branch-aware-multicast-static-injection-demand/v1"),
            Item("traffic.weight-preload.bits", values.WeightBits, 0m, "bit", "independent-unicast-nonreused-weight-preload-upper-bound/v1"),
            Item("traffic.reduction.bits", values.ReductionBits, 0m, "bit", "independent-unicast-sum-contributor-demand-upper-bound/v1"),
            Item("traffic.assembly.bits", values.AssemblyBits, 0m, "bit", "independent-unicast-concat-contributor-demand-upper-bound/v1"),
            Item("path.hops", values.Hops, 0m, "hop", "branch-aware-activation-plus-independent-other-static-link-demand/v1"),
            Item("traffic.bit-hops", values.BitHops, weights.BitHopWeight, "bit-hop", "branch-aware-activation-plus-independent-other-static-link-demand/v1"),
            Item("traffic.word-hops", values.WordHops, 0m, "word-hop-" + profile.WordBits, "branch-aware-activation-plus-independent-other-static-link-demand/v1"),
            Item("traffic.medium.electrical-metal.bit-hops", values.ElectricalMetalBitHops, weights.ElectricalMetalBitHopWeight, "bit-hop", "analytical-static-branch-aware-activation-plus-independent-other-medium-bit-hop/v1"),
            Item("traffic.medium.optical-waveguide.bit-hops", values.OpticalWaveguideBitHops, weights.OpticalWaveguideBitHopWeight, "bit-hop", "analytical-static-branch-aware-activation-plus-independent-other-medium-bit-hop/v1"),
            Item("traffic.medium.thermal-control.bit-hops", values.ThermalControlBitHops, weights.ThermalControlBitHopWeight, "bit-hop", "analytical-static-branch-aware-activation-plus-independent-other-medium-bit-hop/v1"),
            Item("traffic.geometry.bit-micrometers", values.DistanceBitMicrometers, weights.DistanceTrafficWeight, "bit-um", "branch-aware-activation-plus-independent-other-physical-route-geometry/v1"),
            Item("conversion.medium-or-signal-domain.transitions", values.ConversionTransitions, weights.ConversionTransitionWeight, "transition", "analytical-static-adjacent-hop-medium-or-signal-domain-transition-proxy/v1"),
            Item("conversion.medium-or-signal-domain.bit-transitions", values.ConversionBitTransitions, 0m, "bit-transition", "analytical-static-adjacent-hop-medium-or-signal-domain-transition-proxy/v1"),
            Item("latency.analytical-path-service.cycles", values.AnalyticalLatencyCycles, weights.AnalyticalLatencyWeight, "cycle", Phase8ATopologyCostAwarePolicyContract.PathCostModelId),
            Item("congestion.static-link-service-hotspot.cycles", values.StaticServiceHotspotCycles, weights.StaticServiceCongestionWeight, "cycle", "analytical-static-branch-aware-activation-plus-independent-other-bandwidth-service-hotspot-proxy/v1"),
            Item("congestion.static-link-service-imbalance.ppm", values.StaticServiceImbalancePpm, 0m, "ppm-total-link-service", "analytical-static-branch-aware-activation-plus-independent-other-bandwidth-service-share-proxy/v1"),
            Item("buffer.static-fanin-upper-bound.bits", values.PredictedPeakBufferBits, weights.PredictedPeakBufferWeight, "bit", "branch-aware-activation-plus-independent-other-static-destination-fanin/v1"),
            Item("imbalance.pe-static-work.ppm", values.PeImbalancePpm, weights.PeImbalanceWeight, "ppm-total-static-macs", "semantic-compute-assignment-share/v1"),
            Item("imbalance.storage-utilization.ppm", values.StorageImbalancePpm, weights.StorageImbalanceWeight, "ppm-capacity", "verifier-storage-ledger-capacity/v1"),
            Item("imbalance.link-traffic.ppm", values.LinkImbalancePpm, weights.LinkImbalanceWeight, "ppm-total-link-traffic", "branch-aware-activation-plus-independent-other-logical-link-traffic-share/v1"),
            Item("hotspot.link-traffic.bits", values.LinkHotspotBits, weights.LinkHotspotWeight, "bit", "branch-aware-activation-plus-independent-other-logical-link-demand/v1")
        }.OrderBy(item => item.MetricId, StringComparer.Ordinal).ToArray();
        return new ScoreResult(Array.AsReadOnly(items), items.Aggregate(0m, (sum, item) => checked(sum + item.WeightedValue)));
    }

    private static MappingScoreItem Item(string id, decimal value, decimal weight, string unit, string source) =>
        new(id, value, weight, checked(value * weight), unit, source);

    private sealed class StaticCostAccumulator
    {
        private readonly Phase8AMappingProblem _problem;
        private readonly int _wordBits;
        private readonly Dictionary<string, decimal> _linkBits = new(StringComparer.Ordinal);
        private readonly Dictionary<string, decimal> _linkServiceCycles = new(StringComparer.Ordinal);
        private readonly Dictionary<string, decimal> _destinationBits = new(StringComparer.Ordinal);
        private readonly Dictionary<string, decimal> _peWork = new(StringComparer.Ordinal);
        private decimal _totalBits;
        private decimal _totalWords;
        private decimal _activationBits;
        private decimal _weightBits;
        private decimal _reductionBits;
        private decimal _assemblyBits;
        private decimal _hops;
        private decimal _bitHops;
        private decimal _wordHops;
        private decimal _distance;
        private decimal _latency;
        private decimal _electricalMetalBitHops;
        private decimal _opticalWaveguideBitHops;
        private decimal _thermalControlBitHops;
        private decimal _conversionTransitions;
        private decimal _conversionBitTransitions;
        private decimal _storageImbalance;

        public StaticCostAccumulator(Phase8AMappingProblem problem, int wordBits)
        {
            _problem = problem;
            _wordBits = wordBits;
            foreach (var id in problem.LogicalPathCatalog.Entries.SelectMany(item => item.Hops).Select(item => item.LogicalLinkId).Distinct(StringComparer.Ordinal))
            {
                _linkBits[id] = 0;
                _linkServiceCycles[id] = 0;
            }
            foreach (var id in problem.NormalizedInput.ComponentOrdinals.Where(item => item.RoleId == Phase8AMappingSemanticRoles.Compute).Select(item => item.ComponentId)) _peWork[id] = 0;
        }

        public void AddActivationDistribution(Phase8AActivationDistribution distribution)
        {
            if (!distribution.IsMulticast)
            {
                var demand = distribution.Demands.Single();
                AddFlow(demand.RoutePathId, distribution.Bits, "activation", demand.ConsumerComponentId);
                return;
            }
            if (distribution.BranchComponentIds.Count == 0)
                throw new InvalidOperationException("A multicast activation distribution requires an explicit branch component.");

            var paths = distribution.Demands.Select(demand =>
                _problem.LogicalPathCatalog.Find(demand.RoutePathId)
                ?? throw new InvalidOperationException("Selected multicast activation path is absent from the frozen catalog.")).ToArray();
            var bitValue = checked((decimal)distribution.Bits);
            var words = checked((decimal)CeilDiv(distribution.Bits, _wordBits));
            _totalBits = checked(_totalBits + bitValue);
            _totalWords = checked(_totalWords + words);
            _activationBits = checked(_activationBits + bitValue);
            foreach (var demand in distribution.Demands)
                _destinationBits[demand.ConsumerComponentId] = checked(_destinationBits.GetValueOrDefault(demand.ConsumerComponentId) + bitValue);

            var uniqueHops = paths.SelectMany(path => path.Hops)
                .GroupBy(hop => hop.LogicalLinkId, StringComparer.Ordinal)
                .Select(group => group.First())
                .OrderBy(hop => hop.LogicalLinkId, StringComparer.Ordinal)
                .ToArray();
            _hops = checked(_hops + uniqueHops.Length);
            _bitHops = checked(_bitHops + bitValue * uniqueHops.Length);
            _wordHops = checked(_wordHops + words * uniqueHops.Length);
            foreach (var hop in uniqueHops)
            {
                if (hop.BandwidthBitsPerCycle <= 0 || !double.IsFinite(hop.RouteGeometryLengthMicrometers) || hop.RouteGeometryLengthMicrometers < 0 ||
                    !Enum.IsDefined(typeof(RoutingMedium), hop.Medium))
                    throw new InvalidOperationException("Frozen multicast path carries invalid bandwidth, medium, or geometry.");
                var geometry = decimal.Parse(hop.RouteGeometryLengthMicrometers.ToString("R", CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture);
                var serviceCycles = checked((decimal)CeilDiv(distribution.Bits, hop.BandwidthBitsPerCycle));
                _distance = checked(_distance + bitValue * geometry);
                _latency = checked(_latency + hop.LatencyCycles + serviceCycles);
                _linkBits[hop.LogicalLinkId] = checked(_linkBits.GetValueOrDefault(hop.LogicalLinkId) + bitValue);
                _linkServiceCycles[hop.LogicalLinkId] = checked(_linkServiceCycles.GetValueOrDefault(hop.LogicalLinkId) + serviceCycles);
                switch (hop.Medium)
                {
                    case RoutingMedium.ElectricalMetal:
                        _electricalMetalBitHops = checked(_electricalMetalBitHops + bitValue);
                        break;
                    case RoutingMedium.OpticalWaveguide:
                        _opticalWaveguideBitHops = checked(_opticalWaveguideBitHops + bitValue);
                        break;
                    case RoutingMedium.ThermalControl:
                        _thermalControlBitHops = checked(_thermalControlBitHops + bitValue);
                        break;
                    default:
                        throw new InvalidOperationException("Frozen multicast path carries an unsupported routing medium.");
                }
            }

            var transitions = new HashSet<(string PreviousLinkId, string LinkId)>();
            foreach (var path in paths)
            {
                Phase8APhysicalHopCostSnapshot? previous = null;
                foreach (var hop in path.Hops)
                {
                    if (previous is not null &&
                        (previous.Medium != hop.Medium || !string.Equals(previous.SignalDomain, hop.SignalDomain, StringComparison.Ordinal)) &&
                        transitions.Add((previous.LogicalLinkId, hop.LogicalLinkId)))
                    {
                        _conversionTransitions = checked(_conversionTransitions + 1);
                        _conversionBitTransitions = checked(_conversionBitTransitions + bitValue);
                    }
                    previous = hop;
                }
            }
        }

        public void AddFlow(string pathId, long bits, string category, string destinationComponentId)
        {
            if (bits <= 0) throw new InvalidOperationException("Selected flow payload bits must be positive.");
            var path = _problem.LogicalPathCatalog.Find(pathId) ?? throw new InvalidOperationException("Selected path is absent from the frozen catalog.");
            var bitValue = checked((decimal)bits);
            var words = checked((decimal)CeilDiv(bits, _wordBits));
            _totalBits = checked(_totalBits + bitValue);
            _totalWords = checked(_totalWords + words);
            switch (category)
            {
                case "activation": _activationBits = checked(_activationBits + bitValue); break;
                case "weight": _weightBits = checked(_weightBits + bitValue); break;
                case "reduction": _reductionBits = checked(_reductionBits + bitValue); break;
                case "assembly": _assemblyBits = checked(_assemblyBits + bitValue); break;
                default: throw new InvalidOperationException("Unknown static traffic category.");
            }
            _destinationBits[destinationComponentId] = checked(_destinationBits.GetValueOrDefault(destinationComponentId) + bitValue);
            _hops = checked(_hops + path.Hops.Count);
            _bitHops = checked(_bitHops + bitValue * path.Hops.Count);
            _wordHops = checked(_wordHops + words * path.Hops.Count);
            Phase8APhysicalHopCostSnapshot? previous = null;
            foreach (var hop in path.Hops)
            {
                if (hop.BandwidthBitsPerCycle <= 0 || !double.IsFinite(hop.RouteGeometryLengthMicrometers) || hop.RouteGeometryLengthMicrometers < 0 ||
                    !Enum.IsDefined(typeof(RoutingMedium), hop.Medium))
                    throw new InvalidOperationException("Frozen logical path carries invalid bandwidth, medium, or geometry.");
                var geometry = decimal.Parse(hop.RouteGeometryLengthMicrometers.ToString("R", CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture);
                var serviceCycles = checked((decimal)CeilDiv(bits, hop.BandwidthBitsPerCycle));
                _distance = checked(_distance + bitValue * geometry);
                _latency = checked(_latency + hop.LatencyCycles + serviceCycles);
                _linkBits[hop.LogicalLinkId] = checked(_linkBits.GetValueOrDefault(hop.LogicalLinkId) + bitValue);
                _linkServiceCycles[hop.LogicalLinkId] = checked(_linkServiceCycles.GetValueOrDefault(hop.LogicalLinkId) + serviceCycles);
                switch (hop.Medium)
                {
                    case RoutingMedium.ElectricalMetal:
                        _electricalMetalBitHops = checked(_electricalMetalBitHops + bitValue);
                        break;
                    case RoutingMedium.OpticalWaveguide:
                        _opticalWaveguideBitHops = checked(_opticalWaveguideBitHops + bitValue);
                        break;
                    case RoutingMedium.ThermalControl:
                        _thermalControlBitHops = checked(_thermalControlBitHops + bitValue);
                        break;
                    default:
                        throw new InvalidOperationException("Frozen logical path carries an unsupported routing medium.");
                }
                if (previous is not null &&
                    (previous.Medium != hop.Medium || !string.Equals(previous.SignalDomain, hop.SignalDomain, StringComparison.Ordinal)))
                {
                    _conversionTransitions = checked(_conversionTransitions + 1);
                    _conversionBitTransitions = checked(_conversionBitTransitions + bitValue);
                }
                previous = hop;
            }
        }

        public void AddPeWork(string componentId, long macs) => _peWork[componentId] = checked(_peWork.GetValueOrDefault(componentId) + macs);

        public void AddStorage(Phase8ACandidateVerificationResult verification)
        {
            var utilization = new List<decimal>();
            foreach (var selector in _problem.StorageSelectors)
            {
                var used = selector.BaseOccupiedIntervals.Aggregate(0L, (sum, item) => checked(sum + item.SizeBits));
                used = verification.Allocations.Where(item => item.ComponentId == selector.ComponentId && item.ResourceId == selector.ResourceId && !item.ReusedExistingAllocation)
                    .Aggregate(used, (sum, item) => checked(sum + item.SizeBits));
                utilization.Add(checked((decimal)used * 1_000_000m / selector.CapacityBits));
            }
            _storageImbalance = Phase8ATopologyCostMath.Range(utilization);
        }

        public CostValues Finish() => new(
            _totalBits,
            _totalWords,
            _activationBits,
            _weightBits,
            _reductionBits,
            _assemblyBits,
            _hops,
            _bitHops,
            _wordHops,
            _electricalMetalBitHops,
            _opticalWaveguideBitHops,
            _thermalControlBitHops,
            _distance,
            _conversionTransitions,
            _conversionBitTransitions,
            _latency,
            _linkServiceCycles.Count == 0 ? 0 : _linkServiceCycles.Values.Max(),
            Phase8ATopologyCostMath.ShareRange(_linkServiceCycles.Values),
            _destinationBits.Count == 0 ? 0 : _destinationBits.Values.Max(),
            Phase8ATopologyCostMath.ShareRange(_peWork.Values),
            _storageImbalance,
            Phase8ATopologyCostMath.ShareRange(_linkBits.Values),
            _linkBits.Count == 0 ? 0 : _linkBits.Values.Max());


    }

    private sealed record CostValues(
        decimal TotalBits,
        decimal TotalWords,
        decimal ActivationBits,
        decimal WeightBits,
        decimal ReductionBits,
        decimal AssemblyBits,
        decimal Hops,
        decimal BitHops,
        decimal WordHops,
        decimal ElectricalMetalBitHops,
        decimal OpticalWaveguideBitHops,
        decimal ThermalControlBitHops,
        decimal DistanceBitMicrometers,
        decimal ConversionTransitions,
        decimal ConversionBitTransitions,
        decimal AnalyticalLatencyCycles,
        decimal StaticServiceHotspotCycles,
        decimal StaticServiceImbalancePpm,
        decimal PredictedPeakBufferBits,
        decimal PeImbalancePpm,
        decimal StorageImbalancePpm,
        decimal LinkImbalancePpm,
        decimal LinkHotspotBits);

    private static long TensorBits(MappingShape shape, string precisionId) => checked(TensorElements(shape) * PrecisionBits(precisionId));

    private static long TensorElements(MappingShape shape) => shape.Dimensions.Aggregate(1L, (value, dimension) => checked(value * dimension));

    private static int PrecisionBits(string precisionId) => precisionId switch
    {
        nameof(PrecisionKind.FP32) or nameof(PrecisionKind.TF32) or nameof(PrecisionKind.INT32) => 32,
        nameof(PrecisionKind.FP16) or nameof(PrecisionKind.BF16) or nameof(PrecisionKind.INT16) => 16,
        nameof(PrecisionKind.FP8_E4M3) or nameof(PrecisionKind.FP8_E5M2) or nameof(PrecisionKind.INT8) => 8,
        nameof(PrecisionKind.INT4) => 4,
        nameof(PrecisionKind.INT2) => 2,
        nameof(PrecisionKind.Binary) => 1,
        _ => throw new InvalidOperationException("Static policy requires a concrete digital precision.")
    };

    private static long CeilDiv(long value, long divisor)
    {
        if (value < 0 || divisor <= 0) throw new ArgumentOutOfRangeException(nameof(value));
        return checked(value / divisor + (value % divisor == 0 ? 0 : 1));
    }
}
