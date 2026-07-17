using System.Collections.ObjectModel;

namespace HardwareSim.Core;

#pragma warning disable CS1591 // Stable public contract names are documented at their owning type boundaries.

/// <summary>Binds one immutable authority digest to its versioned algorithm.</summary>
public sealed record Phase8AProposalHashBinding(string Algorithm, string Hash);

/// <summary>Captures an explicitly initialized, immutable project dirty-state and persisted revision authority.</summary>
public sealed class Phase8AProjectDirtySnapshot
{
    private Phase8AProjectDirtySnapshot(
        string revisionAuthorityId,
        long revision,
        bool hardwareGraphDirty,
        bool workloadGraphDirty,
        bool mappingDirty,
        bool placementDirty,
        bool routingDirty,
        bool modelBindingDirty,
        bool simulationGraphDirty,
        bool traceDirty,
        bool visualMetadataDirty)
    {
        RevisionAuthorityId = revisionAuthorityId;
        Revision = revision;
        HardwareGraphDirty = hardwareGraphDirty;
        WorkloadGraphDirty = workloadGraphDirty;
        MappingDirty = mappingDirty;
        PlacementDirty = placementDirty;
        RoutingDirty = routingDirty;
        ModelBindingDirty = modelBindingDirty;
        SimulationGraphDirty = simulationGraphDirty;
        TraceDirty = traceDirty;
        VisualMetadataDirty = visualMetadataDirty;
    }

    /// <summary>Gets the non-empty identity of the persisted revision authority.</summary>
    public string RevisionAuthorityId { get; }
    /// <summary>Gets the persisted mapping revision.</summary>
    public long Revision { get; }
    /// <summary>Gets whether the hardware graph is dirty.</summary>
    public bool HardwareGraphDirty { get; }
    /// <summary>Gets whether the workload graph is dirty.</summary>
    public bool WorkloadGraphDirty { get; }
    /// <summary>Gets whether the mapping is dirty.</summary>
    public bool MappingDirty { get; }
    /// <summary>Gets whether placement is dirty.</summary>
    public bool PlacementDirty { get; }
    /// <summary>Gets whether routing is dirty.</summary>
    public bool RoutingDirty { get; }
    /// <summary>Gets whether model bindings are dirty.</summary>
    public bool ModelBindingDirty { get; }
    /// <summary>Gets whether the compiled simulation graph is dirty.</summary>
    public bool SimulationGraphDirty { get; }
    /// <summary>Gets whether trace output is dirty.</summary>
    public bool TraceDirty { get; }
    /// <summary>Gets whether visual-only metadata is dirty.</summary>
    public bool VisualMetadataDirty { get; }

    /// <summary>Gets whether any semantic input or derived compile state prevents proposal creation or apply.</summary>
    public bool HasBlockingDirtyState =>
        HardwareGraphDirty || WorkloadGraphDirty || MappingDirty || PlacementDirty ||
        RoutingDirty || ModelBindingDirty || SimulationGraphDirty;

    /// <summary>Copies a live dirty state only when paired with an explicit persisted revision authority.</summary>
    internal static Phase8AProjectDirtySnapshot Capture(
        ProjectDirtyState dirtyState,
        long revision,
        string revisionAuthorityId)
    {
        if (dirtyState is null) throw new ArgumentNullException(nameof(dirtyState));
        if (revision < 0) throw new ArgumentOutOfRangeException(nameof(revision));
        if (string.IsNullOrWhiteSpace(revisionAuthorityId))
            throw new ArgumentException("A persisted revision authority identity is required.", nameof(revisionAuthorityId));
        return new Phase8AProjectDirtySnapshot(
            revisionAuthorityId.Trim(), revision,
            dirtyState.HardwareGraphDirty, dirtyState.WorkloadGraphDirty,
            dirtyState.MappingDirty, dirtyState.PlacementDirty,
            dirtyState.RoutingDirty, dirtyState.ModelBindingDirty,
            dirtyState.SimulationGraphDirty, dirtyState.TraceDirty,
            dirtyState.VisualMetadataDirty);
    }
}

/// <summary>Core-owned live authority for project dirty flags and the persisted mapping revision.</summary>
public sealed class Phase8AProjectRevisionAuthority
{
    private readonly ProjectDirtyState _dirtyState;
    private readonly Func<long> _readPersistedRevision;

    private Phase8AProjectRevisionAuthority(
        ProjectDirtyState dirtyState,
        Func<long> readPersistedRevision,
        string authorityId)
    {
        _dirtyState = dirtyState;
        _readPersistedRevision = readPersistedRevision;
        AuthorityId = authorityId;
    }

    /// <summary>Gets the stable identity of the persisted revision source.</summary>
    public string AuthorityId { get; }

    /// <summary>Creates a live authority; the revision callback is re-read for every proposal and Apply rebuild.</summary>
    public static Phase8AProjectRevisionAuthority Create(
        ProjectDirtyState dirtyState,
        Func<long> readPersistedRevision,
        string authorityId)
    {
        if (dirtyState is null) throw new ArgumentNullException(nameof(dirtyState));
        if (readPersistedRevision is null) throw new ArgumentNullException(nameof(readPersistedRevision));
        if (string.IsNullOrWhiteSpace(authorityId))
            throw new ArgumentException("A persisted revision authority identity is required.", nameof(authorityId));
        if (readPersistedRevision() < 0)
            throw new ArgumentOutOfRangeException(nameof(readPersistedRevision),
                "The persisted project revision cannot be negative.");
        return new Phase8AProjectRevisionAuthority(
            dirtyState, readPersistedRevision, authorityId.Trim());
    }

    internal Phase8AProjectDirtySnapshot Capture()
    {
        var revision = _readPersistedRevision();
        if (revision < 0)
            throw new InvalidOperationException("The persisted project revision cannot be negative.");
        return Phase8AProjectDirtySnapshot.Capture(
            _dirtyState, revision, AuthorityId);
    }
}

/// <summary>Freezes every authority that must still match when a Phase 8A proposal is applied.</summary>
public sealed class Phase8AMappingBaseSnapshot
{
    internal Phase8AMappingBaseSnapshot(
        Phase8AProposalHashBinding normalizedInput,
        Phase8AProposalHashBinding workload,
        Phase8AProposalHashBinding mapping,
        Phase8AProposalHashBinding lowering,
        Phase8AProposalHashBinding topologyGraph,
        Phase8AProposalHashBinding topologyManifest,
        Phase8AProposalHashBinding placement,
        Phase8AProposalHashBinding route,
        Phase8AProposalHashBinding capability,
        Phase8AProposalHashBinding logicalPathCatalog,
        Phase8AProposalHashBinding mappingProblem,
        Phase8AProposalHashBinding problemBudget,
        Phase8AProposalHashBinding policyConfig,
        Phase8AProposalHashBinding costProfileConfig,
        Phase8AProposalHashBinding policySearchBudget,
        Phase8AProposalHashBinding policyResult,
        Phase8AProposalHashBinding candidate,
        Phase8AProposalHashBinding profileBinding,
        string revisionAuthorityId,
        long revision,
        string canonicalHash)
    {
        NormalizedInput = normalizedInput;
        Workload = workload;
        Mapping = mapping;
        Lowering = lowering;
        TopologyGraph = topologyGraph;
        TopologyManifest = topologyManifest;
        Placement = placement;
        Route = route;
        Capability = capability;
        LogicalPathCatalog = logicalPathCatalog;
        MappingProblem = mappingProblem;
        ProblemBudget = problemBudget;
        PolicyConfig = policyConfig;
        CostProfileConfig = costProfileConfig;
        PolicySearchBudget = policySearchBudget;
        PolicyResult = policyResult;
        Candidate = candidate;
        ProfileBinding = profileBinding;
        RevisionAuthorityId = revisionAuthorityId;
        Revision = revision;
        CanonicalHash = canonicalHash;
    }

    /// <summary>Gets the normalized topology-aware input binding.</summary>
    public Phase8AProposalHashBinding NormalizedInput { get; }
    /// <summary>Gets the normalized workload binding.</summary>
    public Phase8AProposalHashBinding Workload { get; }
    /// <summary>Gets the current Mapping 2.0 binding.</summary>
    public Phase8AProposalHashBinding Mapping { get; }
    /// <summary>Gets the complete lowering-authority binding.</summary>
    public Phase8AProposalHashBinding Lowering { get; }
    /// <summary>Gets the actual topology graph binding.</summary>
    public Phase8AProposalHashBinding TopologyGraph { get; }
    /// <summary>Gets the typed topology-manifest binding.</summary>
    public Phase8AProposalHashBinding TopologyManifest { get; }
    /// <summary>Gets the physical placement binding.</summary>
    public Phase8AProposalHashBinding Placement { get; }
    /// <summary>Gets the physical route-set binding.</summary>
    public Phase8AProposalHashBinding Route { get; }
    /// <summary>Gets the discovered capability-content binding.</summary>
    public Phase8AProposalHashBinding Capability { get; }
    /// <summary>Gets the exact logical-path catalog binding.</summary>
    public Phase8AProposalHashBinding LogicalPathCatalog { get; }
    /// <summary>Gets the rebuilt mapping-problem binding.</summary>
    public Phase8AProposalHashBinding MappingProblem { get; }
    /// <summary>Gets the problem-surface budget binding.</summary>
    public Phase8AProposalHashBinding ProblemBudget { get; }
    /// <summary>Gets the topology-cost policy configuration binding.</summary>
    public Phase8AProposalHashBinding PolicyConfig { get; }
    /// <summary>Gets the topology-cost profile configuration binding.</summary>
    public Phase8AProposalHashBinding CostProfileConfig { get; }
    /// <summary>Gets the topology-cost search budget binding.</summary>
    public Phase8AProposalHashBinding PolicySearchBudget { get; }
    /// <summary>Gets the complete policy-result binding.</summary>
    public Phase8AProposalHashBinding PolicyResult { get; }
    /// <summary>Gets the selected policy candidate binding.</summary>
    public Phase8AProposalHashBinding Candidate { get; }
    /// <summary>Gets the aggregate semantic compiled-profile binding.</summary>
    public Phase8AProposalHashBinding ProfileBinding { get; }
    /// <summary>Gets the persisted revision authority identity.</summary>
    public string RevisionAuthorityId { get; }
    /// <summary>Gets the exact persisted revision.</summary>
    public long Revision { get; }
    /// <summary>Gets the versioned snapshot self hash.</summary>
    public string CanonicalHash { get; }

    internal IEnumerable<(string Name, Phase8AProposalHashBinding Binding)> Bindings()
    {
        yield return ("normalizedInput", NormalizedInput);
        yield return ("workload", Workload);
        yield return ("mapping", Mapping);
        yield return ("lowering", Lowering);
        yield return ("topologyGraph", TopologyGraph);
        yield return ("topologyManifest", TopologyManifest);
        yield return ("placement", Placement);
        yield return ("route", Route);
        yield return ("capability", Capability);
        yield return ("logicalPathCatalog", LogicalPathCatalog);
        yield return ("mappingProblem", MappingProblem);
        yield return ("problemBudget", ProblemBudget);
        yield return ("policyConfig", PolicyConfig);
        yield return ("costProfileConfig", CostProfileConfig);
        yield return ("policySearchBudget", PolicySearchBudget);
        yield return ("policyResult", PolicyResult);
        yield return ("candidate", Candidate);
        yield return ("profileBinding", ProfileBinding);
    }
}

/// <summary>Packages actual Core mapping-problem authorities with one real topology-cost policy result.</summary>
public sealed class Phase8AProposalApplyContextRequest
{
    /// <summary>Creates a request whose mapping problem will be rebuilt by Core.</summary>
    public Phase8AProposalApplyContextRequest(
        Phase8AMappingProblemRequest problemRequest,
        Phase8ATopologyCostPolicyConfig policyConfig,
        Phase8ATopologyCostProfile costProfile,
        Phase8ATopologyCostSearchBudget policySearchBudget,
        Phase8ATopologyCostPolicyResult policyResult,
        Phase8AProjectRevisionAuthority projectAuthority,
        bool acceptIncompleteBestSoFar = false)
    {
        ProblemRequest = problemRequest ?? throw new ArgumentNullException(nameof(problemRequest));
        PolicyConfig = policyConfig ?? throw new ArgumentNullException(nameof(policyConfig));
        CostProfile = costProfile ?? throw new ArgumentNullException(nameof(costProfile));
        PolicySearchBudget = policySearchBudget ?? throw new ArgumentNullException(nameof(policySearchBudget));
        PolicyResult = policyResult ?? throw new ArgumentNullException(nameof(policyResult));
        ProjectAuthority = projectAuthority ?? throw new ArgumentNullException(nameof(projectAuthority));
        AcceptIncompleteBestSoFar = acceptIncompleteBestSoFar;
    }

    /// <summary>Gets the actual graph, mapping, lowering, discovery, manifest, catalog, budget, and manual authorities.</summary>
    public Phase8AMappingProblemRequest ProblemRequest { get; }
    /// <summary>Gets the exact policy configuration used to produce the result.</summary>
    public Phase8ATopologyCostPolicyConfig PolicyConfig { get; }
    /// <summary>Gets the exact analytical cost profile used to produce the result.</summary>
    public Phase8ATopologyCostProfile CostProfile { get; }
    /// <summary>Gets the exact downstream policy search budget.</summary>
    public Phase8ATopologyCostSearchBudget PolicySearchBudget { get; }
    /// <summary>Gets the real Core policy search result.</summary>
    public Phase8ATopologyCostPolicyResult PolicyResult { get; }
    /// <summary>Gets the live Core-owned dirty and persisted-revision authority.</summary>
    public Phase8AProjectRevisionAuthority ProjectAuthority { get; }
    /// <summary>Gets whether an explicitly disclosed best-so-far candidate from an incomplete search may be proposed.</summary>
    public bool AcceptIncompleteBestSoFar { get; }
}

/// <summary>Contains either one rebuilt proposal/apply context or deterministic authority issues.</summary>
public sealed class Phase8AProposalApplyContextResult
{
    internal Phase8AProposalApplyContextResult(
        Phase8AProposalApplyContext? context,
        IEnumerable<WorkloadMappingV2Issue> issues)
    {
        Context = context;
        Issues = Array.AsReadOnly(issues
            .OrderBy(item => item.Location, StringComparer.Ordinal)
            .ThenBy(item => item.Code, StringComparer.Ordinal)
            .ThenBy(item => item.RelatedId, StringComparer.Ordinal)
            .ToArray());
    }

    /// <summary>Gets the rebuilt context when every authority and policy binding is valid.</summary>
    public Phase8AProposalApplyContext? Context { get; }
    /// <summary>Gets deterministic context-construction issues.</summary>
    public IReadOnlyList<WorkloadMappingV2Issue> Issues { get; }
    /// <summary>Gets whether a complete context was built.</summary>
    public bool IsSuccess => Context is not null && Issues.All(item => item.Severity != ValidationSeverity.Error);
}

/// <summary>Represents a Core-rebuilt point-in-time authority context; callers cannot supply a base snapshot.</summary>
public sealed class Phase8AProposalApplyContext
{
    internal Phase8AProposalApplyContext(
        Phase8AProposalApplyContextRequest request,
        Phase8AMappingProblem problem,
        Phase8ATopologyCostCandidate selectedCandidate,
        Phase8AMappingBaseSnapshot snapshot,
        Phase8AProjectDirtySnapshot projectState)
    {
        Request = request;
        Problem = problem;
        SelectedCandidate = selectedCandidate;
        BaseSnapshot = snapshot;
        ProjectState = projectState;
    }

    /// <summary>Gets the actual request retained for an independent rebuild at Apply.</summary>
    public Phase8AProposalApplyContextRequest Request { get; }
    /// <summary>Gets the Core-rebuilt immutable mapping problem.</summary>
    public Phase8AMappingProblem Problem { get; }
    /// <summary>Gets the best verified candidate selected from the real policy result.</summary>
    public Phase8ATopologyCostCandidate SelectedCandidate { get; }
    /// <summary>Gets the Core-owned complete base snapshot.</summary>
    public Phase8AMappingBaseSnapshot BaseSnapshot { get; }
    /// <summary>Gets the initialized project dirty/revision snapshot.</summary>
    public Phase8AProjectDirtySnapshot ProjectState { get; }
}

/// <summary>Defines stable mapping proposal and apply issue identities.</summary>
public static class Phase8AMappingProposalIssueCodes
{
    public const string ProblemBuildFailed = "Phase8AProposalProblemBuildFailed";
    public const string PolicyResultInvalid = "Phase8AProposalPolicyResultInvalid";
    public const string IncompleteBestSoFarNotAccepted = "Phase8AProposalIncompleteBestSoFarNotAccepted";
    public const string InputDirty = "Phase8AProposalInputDirty";
    public const string RevisionStale = "Phase8AProposalRevisionStale";
    public const string NormalizedInputStale = "Phase8AProposalNormalizedInputStale";
    public const string WorkloadStale = "Phase8AProposalWorkloadStale";
    public const string MappingStale = "Phase8AProposalMappingStale";
    public const string LoweringStale = "Phase8AProposalLoweringStale";
    public const string TopologyGraphStale = "Phase8AProposalTopologyGraphStale";
    public const string TopologyManifestStale = "Phase8AProposalTopologyManifestStale";
    public const string PlacementStale = "Phase8AProposalPlacementStale";
    public const string RouteStale = "Phase8AProposalRouteStale";
    public const string CapabilityStale = "Phase8AProposalCapabilityStale";
    public const string PathCatalogStale = "Phase8AProposalPathCatalogStale";
    public const string MappingProblemStale = "Phase8AProposalMappingProblemStale";
    public const string ProblemBudgetStale = "Phase8AProposalProblemBudgetStale";
    public const string PolicyConfigStale = "Phase8AProposalPolicyConfigStale";
    public const string CostProfileStale = "Phase8AProposalCostProfileStale";
    public const string PolicySearchBudgetStale = "Phase8AProposalPolicySearchBudgetStale";
    public const string PolicyResultStale = "Phase8AProposalPolicyResultStale";
    public const string CandidateStale = "Phase8AProposalCandidateStale";
    public const string ProfileBindingStale = "Phase8AProposalProfileBindingStale";
    public const string ProposedMappingInvalid = "Phase8AProposalMappingInvalid";
    public const string ProposalHashMismatch = "Phase8AProposalHashMismatch";
    public const string ManualDiffMismatch = "Phase8AProposalManualDiffMismatch";
}

/// <summary>Represents one immutable proposal produced only from a valid Core authority context.</summary>
public sealed class Phase8AMappingProposal
{
    /// <summary>Defines the current proposal contract.</summary>
    public const string CurrentSchemaVersion = "1.0";

    internal Phase8AMappingProposal(
        string proposalId,
        Phase8AMappingBaseSnapshot baseSnapshot,
        Phase8ATopologyCostCandidate sourceCandidate,
        WorkloadMappingV2 proposedMapping,
        IReadOnlyList<MappingManualDiffItem> manualDiff,
        string policySearchStatusId,
        bool policySearchComplete,
        bool acceptedIncompleteBestSoFar,
        string candidateEnvelopeHash,
        string canonicalHash)
    {
        ProposalId = proposalId;
        BaseSnapshot = baseSnapshot;
        SourceCandidate = sourceCandidate;
        ProposedMapping = proposedMapping;
        ManualDiff = Array.AsReadOnly(manualDiff.ToArray());
        PolicySearchStatusId = policySearchStatusId;
        PolicySearchComplete = policySearchComplete;
        AcceptedIncompleteBestSoFar = acceptedIncompleteBestSoFar;
        CandidateEnvelopeHash = candidateEnvelopeHash;
        CanonicalHash = canonicalHash;
    }

    public string SchemaVersion => CurrentSchemaVersion;
    public string ProposalId { get; }
    public Phase8AMappingBaseSnapshot BaseSnapshot { get; }
    public Phase8ATopologyCostCandidate SourceCandidate { get; }
    public WorkloadMappingV2 ProposedMapping { get; }
    public IReadOnlyList<MappingManualDiffItem> ManualDiff { get; }
    public string PolicySearchStatusId { get; }
    public bool PolicySearchComplete { get; }
    public bool AcceptedIncompleteBestSoFar { get; }
    public string CandidateEnvelopeHash { get; }
    public string CanonicalHash { get; }
}

/// <summary>Contains either one complete immutable proposal or deterministic construction issues.</summary>
public sealed class Phase8AMappingProposalBuildResult
{
    internal Phase8AMappingProposalBuildResult(
        Phase8AMappingProposal? proposal,
        IEnumerable<WorkloadMappingV2Issue> issues)
    {
        Proposal = proposal;
        Issues = Array.AsReadOnly(issues
            .OrderBy(item => item.Location, StringComparer.Ordinal)
            .ThenBy(item => item.Code, StringComparer.Ordinal)
            .ThenBy(item => item.RelatedId, StringComparer.Ordinal)
            .ToArray());
    }

    public Phase8AMappingProposal? Proposal { get; }
    public IReadOnlyList<WorkloadMappingV2Issue> Issues { get; }
    public bool IsSuccess => Proposal is not null && Issues.All(item => item.Severity != ValidationSeverity.Error);
}

/// <summary>Describes apply-only invalidations without mutating a live ProjectDirtyState.</summary>
public sealed record Phase8AMappingApplyInvalidation(
    long PreviousRevision,
    long NextRevision,
    bool HardwareGraphDirty,
    bool WorkloadGraphDirty,
    bool MappingDirty,
    bool SimulationGraphDirty,
    bool TraceDirty);

/// <summary>Defines stable proposal decision identities.</summary>
public static class Phase8AMappingProposalDecisionIds
{
    public const string Applied = "applied";
    public const string Rejected = "rejected";
    public const string Cancelled = "cancelled";
}

/// <summary>Contains an immutable apply, reject, or cancel decision.</summary>
public sealed class Phase8AMappingProposalDecision
{
    internal Phase8AMappingProposalDecision(
        string decisionId,
        WorkloadMappingV2? mapping,
        Phase8AMappingApplyInvalidation? invalidation,
        IEnumerable<WorkloadMappingV2Issue> issues)
    {
        DecisionId = decisionId;
        Mapping = mapping;
        Invalidation = invalidation;
        Issues = Array.AsReadOnly(issues
            .OrderBy(item => item.Location, StringComparer.Ordinal)
            .ThenBy(item => item.Code, StringComparer.Ordinal)
            .ThenBy(item => item.RelatedId, StringComparer.Ordinal)
            .ToArray());
    }

    public string DecisionId { get; }
    public WorkloadMappingV2? Mapping { get; }
    public Phase8AMappingApplyInvalidation? Invalidation { get; }
    public IReadOnlyList<WorkloadMappingV2Issue> Issues { get; }
    public bool IsApplied => DecisionId == Phase8AMappingProposalDecisionIds.Applied && Mapping is not null && Invalidation is not null;
    public bool IsCancelled => DecisionId == Phase8AMappingProposalDecisionIds.Cancelled && Mapping is null && Invalidation is null;
}

/// <summary>Builds Core-owned proposal/apply contexts from actual authorities and real policy results.</summary>
public static class Phase8AProposalApplyContextFactory
{
    /// <summary>Rebuilds and validates the mapping problem, policy result, candidate, and complete base snapshot.</summary>
    public static Phase8AProposalApplyContextResult Create(Phase8AProposalApplyContextRequest request)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));
        return Build(request);
    }

    internal static Phase8AProposalApplyContextResult Rebuild(Phase8AProposalApplyContext context)
    {
        if (context is null) throw new ArgumentNullException(nameof(context));
        return Build(context.Request);
    }

    private static Phase8AProposalApplyContextResult Build(Phase8AProposalApplyContextRequest request)
    {
        var issues = new List<WorkloadMappingV2Issue>();
        Phase8AProjectDirtySnapshot projectState;
        try
        {
            projectState = request.ProjectAuthority.Capture();
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or ArgumentException or OverflowException)
        {
            issues.Add(Error(Phase8AMappingProposalIssueCodes.RevisionStale,
                "$.projectAuthority.revision",
                "The live persisted project revision authority could not be read: " + exception.Message));
            return new Phase8AProposalApplyContextResult(null, issues);
        }
        var built = Phase8AMappingProblemBuilder.Build(request.ProblemRequest);
        foreach (var issue in built.Issues.Where(item => item.Severity == ValidationSeverity.Error))
            issues.Add(Error(Phase8AMappingProposalIssueCodes.ProblemBuildFailed,
                "$.problem" + issue.Location.TrimStart('$'),
                issue.Code + ": " + issue.Message, issue.RelatedId));

        if (built.Problem is null || issues.Count != 0)
            return new Phase8AProposalApplyContextResult(null, issues);

        var problem = built.Problem;
        var candidate = ValidatePolicyResult(problem, request, issues);
        if (candidate is null || issues.Any(item => item.Severity == ValidationSeverity.Error))
            return new Phase8AProposalApplyContextResult(null, issues);

        var snapshot = Phase8AMappingProposalAuthorityHasher.CreateSnapshot(
            problem, request.PolicyConfig, request.CostProfile,
            request.PolicySearchBudget, request.PolicyResult,
            candidate, projectState);
        return new Phase8AProposalApplyContextResult(
            new Phase8AProposalApplyContext(
                request, problem, candidate, snapshot, projectState), issues);
    }

    private static Phase8ATopologyCostCandidate? ValidatePolicyResult(
        Phase8AMappingProblem problem,
        Phase8AProposalApplyContextRequest request,
        List<WorkloadMappingV2Issue> issues)
    {
        var result = request.PolicyResult;
        var candidate = result.BestCandidate;
        if (candidate is null || result.Issues.Any(item => item.Severity == ValidationSeverity.Error) ||
            result.Candidates.Count == 0)
        {
            issues.Add(Error(Phase8AMappingProposalIssueCodes.PolicyResultInvalid,
                "$.policyResult", "A real policy result with at least one verified candidate and no errors is required."));
            return null;
        }

        if (!result.IsCompleteSearch && !request.AcceptIncompleteBestSoFar)
            issues.Add(Error(Phase8AMappingProposalIssueCodes.IncompleteBestSoFarNotAccepted,
                "$.policyResult.isCompleteSearch",
                "An incomplete policy result requires explicit best-so-far acceptance."));

        if (!result.IsCompleteSearch &&
            result.StatusId is not Phase8ATopologyCostSearchStatuses.IncompleteBudgetExhausted and
                not Phase8ATopologyCostSearchStatuses.IncompleteProblemSurface)
            issues.Add(Error(Phase8AMappingProposalIssueCodes.PolicyResultInvalid,
                "$.policyResult.statusId",
                "Only explicitly disclosed incomplete policy statuses may provide best-so-far candidates."));

        var configHash = Phase8ATopologyCostPolicyHasher.ComputeConfigHash(request.PolicyConfig);
        var profileConfigHash = Phase8ATopologyCostPolicyHasher.ComputeProfileConfigHash(request.CostProfile);
        var budgetHash = Phase8ATopologyCostPolicyHasher.ComputeBudgetHash(request.PolicySearchBudget);
        var profileBinding = Phase8ATopologyProfileBindingHasher.Compute(problem);
        var provenance = candidate.Provenance;
        if (result.PolicyConfigHash != configHash ||
            result.CostProfileConfigHash != profileConfigHash ||
            result.SearchBudgetHash != budgetHash ||
            result.ProfileBinding is null ||
            result.ProfileBinding.AlgorithmId != profileBinding.AlgorithmId ||
            result.ProfileBinding.Hash != profileBinding.Hash ||
            provenance.ProblemCanonicalHash != problem.CanonicalHash ||
            provenance.BaseHashes != problem.BaseHashes ||
            provenance.PolicyConfigHash != configHash ||
            provenance.CostProfileConfigHash != profileConfigHash ||
            provenance.SearchBudgetHash != budgetHash ||
            provenance.ProfileBindingHashAlgorithm != profileBinding.AlgorithmId ||
            provenance.ProfileBindingHash != profileBinding.Hash ||
            provenance.Seed != problem.NormalizedInput.Seed ||
            provenance.TopologyHash != problem.BaseHashes.TopologyGraphHash ||
            provenance.RouteHash != problem.BaseHashes.RouteHash ||
            provenance.SemanticOrderingHash != problem.SemanticOrderingHash ||
            candidate.Descriptor.PolicyId != request.PolicyConfig.PolicyId ||
            candidate.Descriptor.PolicyConfigHash != configHash ||
            candidate.Descriptor.TopologyHash != problem.BaseHashes.TopologyGraphHash ||
            candidate.Descriptor.RouteHash != problem.BaseHashes.RouteHash ||
            candidate.Descriptor.ProfileHash != problem.BaseHashes.CapabilityContentHash)
            issues.Add(Error(Phase8AMappingProposalIssueCodes.PolicyResultInvalid,
                "$.policyResult.provenance",
                "Policy result, selected candidate, problem, config, budget, capability, profile, topology, route, seed, and semantic ordering must bind exactly."));

        var verification = Phase8AMappingCandidateVerifier.Verify(problem, candidate.Draft);
        if (!verification.IsSuccess ||
            !verification.Allocations.SequenceEqual(candidate.Verification.Allocations) ||
            candidate.Verification.Issues.Count != 0)
            issues.Add(Error(Phase8AMappingProposalIssueCodes.PolicyResultInvalid,
                "$.policyResult.bestCandidate.verification",
                "The selected policy draft did not reproduce the verifier-owned hard-feasible allocation decisions."));

        var expectedCandidateHash = Phase8AMappingProposalAuthorityHasher.ComputeSourceCandidateHash(
            candidate, result.StatusId, result.IsCompleteSearch,
            problem.SearchStatus.TruncatedReasonId);
        if (expectedCandidateHash != candidate.CandidateHash ||
            candidate.Descriptor.CandidateId != "phase8a-tca-" + candidate.CandidateHash[..24] ||
            candidate.TotalScore != candidate.ScoreBreakdown.Aggregate(0m, (sum, item) => checked(sum + item.WeightedValue)))
            issues.Add(Error(Phase8AMappingProposalIssueCodes.PolicyResultInvalid,
                "$.policyResult.bestCandidate.candidateHash",
                "The selected policy candidate hash, identity, or total score is inconsistent with its complete semantic content."));
        return candidate;
    }

    private static WorkloadMappingV2Issue Error(
        string code, string location, string message, string? relatedId = null) =>
        new(code, ValidationSeverity.Error, location, message, relatedId);
}

/// <summary>Creates proposals by projecting a selected verified policy candidate into a complete Mapping 2.0 value.</summary>
public static class Phase8AMappingProposalFactory
{
    /// <summary>Creates a deterministic proposal without modifying any authored authority.</summary>
    public static Phase8AMappingProposalBuildResult Create(
        string proposalId,
        Phase8AProposalApplyContext context)
    {
        if (context is null) throw new ArgumentNullException(nameof(context));
        var issues = new List<WorkloadMappingV2Issue>();
        var rebuilt = Phase8AProposalApplyContextFactory.Rebuild(context);
        if (!rebuilt.IsSuccess || rebuilt.Context is null)
            return new Phase8AMappingProposalBuildResult(null, rebuilt.Issues);
        context = rebuilt.Context;
        if (string.IsNullOrWhiteSpace(proposalId))
            issues.Add(Error(Phase8AMappingProposalIssueCodes.ProposalHashMismatch,
                "$.proposalId", "A stable non-empty proposal identity is required."));
        AddDirtyIssues(context.ProjectState, issues);
        if (issues.Any(item => item.Severity == ValidationSeverity.Error))
            return new Phase8AMappingProposalBuildResult(null, issues);

        WorkloadMappingV2 proposed;
        IReadOnlyList<MappingManualDiffItem> manualDiff;
        try
        {
            (proposed, manualDiff) = Phase8AMappingProjection.Project(
                context.Problem, context.SelectedCandidate);
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or ArgumentException or
                KeyNotFoundException or OverflowException)
        {
            issues.Add(Error(Phase8AMappingProposalIssueCodes.ProposedMappingInvalid,
                "$.proposedMapping", "Core projection failed: " + exception.Message));
            return new Phase8AMappingProposalBuildResult(null, issues);
        }

        foreach (var issue in WorkloadMappingV2Validator.Validate(proposed, true)
                     .Where(item => item.Severity == ValidationSeverity.Error))
            issues.Add(Error(Phase8AMappingProposalIssueCodes.ProposedMappingInvalid,
                "$.proposedMapping" + issue.Location.TrimStart('$'),
                issue.Code + ": " + issue.Message, issue.RelatedId));

        var computed = WorkloadMappingV2CanonicalHasher.Compute(proposed);
        if (computed.Hash != proposed.CanonicalHash ||
            computed.Algorithm != proposed.CanonicalHashAlgorithm)
            issues.Add(Error(Phase8AMappingProposalIssueCodes.ProposedMappingInvalid,
                "$.proposedMapping.canonicalHash",
                "The projected Mapping 2.0 canonical hash does not match its semantic content."));
        if (issues.Any(item => item.Severity == ValidationSeverity.Error))
            return new Phase8AMappingProposalBuildResult(null, issues);

        var candidateEnvelopeHash = Phase8AMappingProposalAuthorityHasher.ComputeCandidateEnvelopeHash(
            context.SelectedCandidate, proposed, context.Problem.CanonicalHash);
        var unhashed = new Phase8AMappingProposal(
            proposalId.Trim(), context.BaseSnapshot, context.SelectedCandidate,
            proposed, manualDiff, context.Request.PolicyResult.StatusId,
            context.Request.PolicyResult.IsCompleteSearch,
            context.Request.AcceptIncompleteBestSoFar,
            candidateEnvelopeHash, "");
        var proposal = new Phase8AMappingProposal(
            unhashed.ProposalId, unhashed.BaseSnapshot, unhashed.SourceCandidate,
            unhashed.ProposedMapping, unhashed.ManualDiff,
            unhashed.PolicySearchStatusId, unhashed.PolicySearchComplete,
            unhashed.AcceptedIncompleteBestSoFar, unhashed.CandidateEnvelopeHash,
            Phase8AMappingProposalAuthorityHasher.ComputeProposalHash(unhashed));
        return new Phase8AMappingProposalBuildResult(proposal, issues);
    }

    internal static void AddDirtyIssues(
        Phase8AProjectDirtySnapshot state,
        List<WorkloadMappingV2Issue> issues)
    {
        var values = new (string Name, bool Value)[]
        {
            (nameof(state.HardwareGraphDirty), state.HardwareGraphDirty),
            (nameof(state.WorkloadGraphDirty), state.WorkloadGraphDirty),
            (nameof(state.MappingDirty), state.MappingDirty),
            (nameof(state.PlacementDirty), state.PlacementDirty),
            (nameof(state.RoutingDirty), state.RoutingDirty),
            (nameof(state.ModelBindingDirty), state.ModelBindingDirty),
            (nameof(state.SimulationGraphDirty), state.SimulationGraphDirty)
        };
        foreach (var item in values.Where(item => item.Value))
            issues.Add(Error(Phase8AMappingProposalIssueCodes.InputDirty,
                "$.projectState." + item.Name,
                "Proposal creation and Apply require a clean semantic authority snapshot.",
                item.Name));
    }

    private static WorkloadMappingV2Issue Error(
        string code, string location, string message, string? relatedId = null) =>
        new(code, ValidationSeverity.Error, location, message, relatedId);
}

/// <summary>Strictly applies or cancels an immutable proposal without automatic graph overwrite.</summary>
public static class Phase8AMappingProposalApplier
{
    /// <summary>Rebuilds current authorities, rejects every stale binding independently, and returns a new mapping only on success.</summary>
    public static Phase8AMappingProposalDecision Apply(
        Phase8AMappingProposal proposal,
        Phase8AProposalApplyContext currentContext)
    {
        if (proposal is null) throw new ArgumentNullException(nameof(proposal));
        if (currentContext is null) throw new ArgumentNullException(nameof(currentContext));

        var issues = new List<WorkloadMappingV2Issue>();
        var rebuilt = Phase8AProposalApplyContextFactory.Rebuild(currentContext);
        if (!rebuilt.IsSuccess || rebuilt.Context is null)
        {
            issues.AddRange(rebuilt.Issues);
            return Reject(issues);
        }

        var current = rebuilt.Context;
        Phase8AMappingProposalFactory.AddDirtyIssues(current.ProjectState, issues);
        CompareSnapshot(proposal.BaseSnapshot, current.BaseSnapshot, issues);
        var proposalHash = Phase8AMappingProposalAuthorityHasher.ComputeProposalHash(proposal);
        var candidateEnvelope = Phase8AMappingProposalAuthorityHasher.ComputeCandidateEnvelopeHash(
            proposal.SourceCandidate, proposal.ProposedMapping,
            current.Problem.CanonicalHash);
        if (proposal.CanonicalHash != proposalHash ||
            proposal.CandidateEnvelopeHash != candidateEnvelope)
            issues.Add(Error(Phase8AMappingProposalIssueCodes.ProposalHashMismatch,
                "$.canonicalHash",
                "Proposal or candidate envelope semantic content changed after creation."));

        var verification = Phase8AMappingCandidateVerifier.Verify(
            current.Problem, proposal.SourceCandidate.Draft);
        if (!verification.IsSuccess ||
            !verification.Allocations.SequenceEqual(proposal.SourceCandidate.Verification.Allocations))
            issues.Add(Error(Phase8AMappingProposalIssueCodes.CandidateStale,
                "$.sourceCandidate.verification",
                "The proposal candidate no longer reproduces hard-feasible verifier allocations."));

        try
        {
            var regenerated = Phase8AMappingProjection.Project(
                current.Problem, proposal.SourceCandidate);
            if (regenerated.Mapping.CanonicalHash != proposal.ProposedMapping.CanonicalHash ||
                !regenerated.ManualDiff.SequenceEqual(proposal.ManualDiff) ||
                !proposal.ManualDiff.SequenceEqual(proposal.ProposedMapping.Candidate.ManualDiff))
                issues.Add(Error(Phase8AMappingProposalIssueCodes.ManualDiffMismatch,
                    "$.manualDiff",
                    "Core projection, stable-ID manual diff, and proposed mapping no longer agree exactly."));
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or ArgumentException or
                KeyNotFoundException or OverflowException)
        {
            issues.Add(Error(Phase8AMappingProposalIssueCodes.ProposedMappingInvalid,
                "$.proposedMapping",
                "Independent Apply projection failed: " + exception.Message));
        }

        foreach (var issue in WorkloadMappingV2Validator.Validate(
                     proposal.ProposedMapping, true)
                 .Where(item => item.Severity == ValidationSeverity.Error))
            issues.Add(Error(Phase8AMappingProposalIssueCodes.ProposedMappingInvalid,
                "$.proposedMapping" + issue.Location.TrimStart('$'),
                issue.Code + ": " + issue.Message, issue.RelatedId));
        var mappingHash = WorkloadMappingV2CanonicalHasher.Compute(proposal.ProposedMapping);
        if (mappingHash.Hash != proposal.ProposedMapping.CanonicalHash ||
            mappingHash.Algorithm != proposal.ProposedMapping.CanonicalHashAlgorithm)
            issues.Add(Error(Phase8AMappingProposalIssueCodes.ProposedMappingInvalid,
                "$.proposedMapping.canonicalHash",
                "The proposed Mapping 2.0 stored hash does not match its semantic content."));
        if (issues.Any(item => item.Severity == ValidationSeverity.Error))
            return Reject(issues);

        var import = WorkloadMappingV2Json.ImportToCurrent(
            WorkloadMappingV2Json.Serialize(proposal.ProposedMapping));
        if (!import.IsSuccess || import.Mapping is null)
            return Reject(import.Issues.Concat([
                Error(Phase8AMappingProposalIssueCodes.ProposedMappingInvalid,
                    "$.proposedMapping",
                    "The applied mapping failed a strict immutable serialization round trip.")
            ]));

        if (current.ProjectState.Revision == long.MaxValue)
            return Reject([
                Error(Phase8AMappingProposalIssueCodes.RevisionStale,
                    "$.projectAuthority.revision",
                    "The persisted project revision cannot be advanced without overflow.")
            ]);
        var effects = new Phase8AMappingApplyInvalidation(
            current.ProjectState.Revision,
            current.ProjectState.Revision + 1,
            false, false, true, true, true);
        return new Phase8AMappingProposalDecision(
            Phase8AMappingProposalDecisionIds.Applied,
            import.Mapping, effects, []);
    }

    /// <summary>Cancels a proposal without reading, rebuilding, invalidating, or changing any project authority.</summary>
    public static Phase8AMappingProposalDecision Cancel(Phase8AMappingProposal proposal)
    {
        if (proposal is null) throw new ArgumentNullException(nameof(proposal));
        return new Phase8AMappingProposalDecision(
            Phase8AMappingProposalDecisionIds.Cancelled, null, null, []);
    }

    private static void CompareSnapshot(
        Phase8AMappingBaseSnapshot expected,
        Phase8AMappingBaseSnapshot actual,
        List<WorkloadMappingV2Issue> issues)
    {
        var codes = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["normalizedInput"] = Phase8AMappingProposalIssueCodes.NormalizedInputStale,
            ["workload"] = Phase8AMappingProposalIssueCodes.WorkloadStale,
            ["mapping"] = Phase8AMappingProposalIssueCodes.MappingStale,
            ["lowering"] = Phase8AMappingProposalIssueCodes.LoweringStale,
            ["topologyGraph"] = Phase8AMappingProposalIssueCodes.TopologyGraphStale,
            ["topologyManifest"] = Phase8AMappingProposalIssueCodes.TopologyManifestStale,
            ["placement"] = Phase8AMappingProposalIssueCodes.PlacementStale,
            ["route"] = Phase8AMappingProposalIssueCodes.RouteStale,
            ["capability"] = Phase8AMappingProposalIssueCodes.CapabilityStale,
            ["logicalPathCatalog"] = Phase8AMappingProposalIssueCodes.PathCatalogStale,
            ["mappingProblem"] = Phase8AMappingProposalIssueCodes.MappingProblemStale,
            ["problemBudget"] = Phase8AMappingProposalIssueCodes.ProblemBudgetStale,
            ["policyConfig"] = Phase8AMappingProposalIssueCodes.PolicyConfigStale,
            ["costProfileConfig"] = Phase8AMappingProposalIssueCodes.CostProfileStale,
            ["policySearchBudget"] = Phase8AMappingProposalIssueCodes.PolicySearchBudgetStale,
            ["policyResult"] = Phase8AMappingProposalIssueCodes.PolicyResultStale,
            ["candidate"] = Phase8AMappingProposalIssueCodes.CandidateStale,
            ["profileBinding"] = Phase8AMappingProposalIssueCodes.ProfileBindingStale
        };
        var current = actual.Bindings().ToDictionary(
            item => item.Name, item => item.Binding, StringComparer.Ordinal);
        foreach (var item in expected.Bindings())
            if (item.Binding != current[item.Name])
                issues.Add(Error(codes[item.Name],
                    "$.baseSnapshot." + item.Name,
                    "The current Core-rebuilt authority differs from the exact proposal base.",
                    item.Name));

        if (expected.Revision != actual.Revision ||
            expected.RevisionAuthorityId != actual.RevisionAuthorityId)
            issues.Add(Error(Phase8AMappingProposalIssueCodes.RevisionStale,
                "$.baseSnapshot.revision",
                "The persisted revision or its authority identity changed after proposal creation."));
    }

    private static Phase8AMappingProposalDecision Reject(
        IEnumerable<WorkloadMappingV2Issue> issues) =>
        new(Phase8AMappingProposalDecisionIds.Rejected, null, null, issues);

    private static WorkloadMappingV2Issue Error(
        string code, string location, string message, string? relatedId = null) =>
        new(code, ValidationSeverity.Error, location, message, relatedId);
}

internal static class Phase8AMappingProposalAuthorityHasher
{
    public const string SnapshotAlgorithm = "sha256/phase8a-mapping-proposal-base-snapshot/v2";
    public const string ProblemBudgetAlgorithm = "sha256/phase8a-mapping-problem-budget/v1";
    public const string PolicyConfigAlgorithm = "sha256/phase8a-topology-cost-policy-config/v1";
    public const string CostProfileAlgorithm = "sha256/phase8a-topology-cost-profile-config/v1";
    public const string PolicySearchBudgetAlgorithm = "sha256/phase8a-topology-cost-search-budget/v1";
    public const string PolicyResultAlgorithm = "sha256/phase8a-topology-cost-policy-result/v1";
    public const string CandidateBindingAlgorithm = "sha256/phase8a-topology-cost-candidate-binding/v1";
    public const string CandidateEnvelopeAlgorithm = "sha256/phase8a-mapping-proposal-candidate-envelope/v2";
    public const string ProposalAlgorithm = "sha256/phase8a-mapping-proposal/v2";
    public const string NormalizedInputAlgorithm = "sha256/phase8a-topology-aware-normalized-input/v1";
    public const string WorkloadAlgorithm = "sha256/phase8a-normalized-workload/v1";
    public const string LoweringAlgorithm = "sha256/phase8a-lowering-authority-set/v1";

    public static Phase8AMappingBaseSnapshot CreateSnapshot(
        Phase8AMappingProblem problem,
        Phase8ATopologyCostPolicyConfig config,
        Phase8ATopologyCostProfile profile,
        Phase8ATopologyCostSearchBudget policyBudget,
        Phase8ATopologyCostPolicyResult result,
        Phase8ATopologyCostCandidate candidate,
        Phase8AProjectDirtySnapshot state)
    {
        var mappingHash = WorkloadMappingV2CanonicalHasher.Compute(problem.BaseMapping);
        var normalized = new Phase8AProposalHashBinding(
            NormalizedInputAlgorithm, problem.BaseHashes.NormalizedInputHash);
        var workload = new Phase8AProposalHashBinding(
            WorkloadAlgorithm,
            Phase8AMappingAuthorityHasher.ComputeWorkloadHash(problem.NormalizedInput.Workload));
        var mapping = new Phase8AProposalHashBinding(mappingHash.Algorithm, mappingHash.Hash);
        var lowering = new Phase8AProposalHashBinding(
            LoweringAlgorithm, problem.BaseHashes.LoweringHash);
        var topology = new Phase8AProposalHashBinding(
            problem.TopologyManifest.TopologyGraphHashAlgorithm,
            problem.BaseHashes.TopologyGraphHash);
        var manifest = new Phase8AProposalHashBinding(
            problem.TopologyManifest.CanonicalHashAlgorithm,
            problem.BaseHashes.TopologyManifestHash);
        var placement = new Phase8AProposalHashBinding(
            problem.TopologyManifest.PlacementHashAlgorithm,
            problem.BaseHashes.PlacementHash);
        var route = new Phase8AProposalHashBinding(
            problem.TopologyManifest.RouteHashAlgorithm,
            problem.BaseHashes.RouteHash);
        var capability = new Phase8AProposalHashBinding(
            Phase8ACapabilityAuthority.ContentHashAlgorithm,
            problem.BaseHashes.CapabilityContentHash);
        var catalog = new Phase8AProposalHashBinding(
            problem.LogicalPathCatalog.CanonicalHashAlgorithm,
            problem.BaseHashes.LogicalPathCatalogHash);
        var mappingProblem = new Phase8AProposalHashBinding(
            problem.CanonicalHashAlgorithm, problem.CanonicalHash);
        var problemBudget = new Phase8AProposalHashBinding(
            ProblemBudgetAlgorithm,
            Phase8AMappingAuthorityHasher.Hash(new
            {
                algorithm = ProblemBudgetAlgorithm,
                problem.PolicyBudget
            }));
        var policyConfig = new Phase8AProposalHashBinding(
            PolicyConfigAlgorithm,
            Phase8ATopologyCostPolicyHasher.ComputeConfigHash(config));
        var costProfile = new Phase8AProposalHashBinding(
            CostProfileAlgorithm,
            Phase8ATopologyCostPolicyHasher.ComputeProfileConfigHash(profile));
        var searchBudget = new Phase8AProposalHashBinding(
            PolicySearchBudgetAlgorithm,
            Phase8ATopologyCostPolicyHasher.ComputeBudgetHash(policyBudget));
        var policyResult = new Phase8AProposalHashBinding(
            PolicyResultAlgorithm, ComputePolicyResultHash(result));
        var candidateBinding = new Phase8AProposalHashBinding(
            CandidateBindingAlgorithm, candidate.CandidateHash);
        var profileBinding = new Phase8AProposalHashBinding(
            result.ProfileBinding!.AlgorithmId, result.ProfileBinding.Hash);
        var self = Phase8AMappingAuthorityHasher.Hash(new
        {
            algorithm = SnapshotAlgorithm,
            normalized, workload, mapping, lowering, topology, manifest,
            placement, route, capability, catalog, mappingProblem,
            problemBudget, policyConfig, costProfile, searchBudget,
            policyResult, candidateBinding, profileBinding,
            state.RevisionAuthorityId, state.Revision
        });
        return new Phase8AMappingBaseSnapshot(
            normalized, workload, mapping, lowering, topology, manifest,
            placement, route, capability, catalog, mappingProblem,
            problemBudget, policyConfig, costProfile, searchBudget,
            policyResult, candidateBinding, profileBinding,
            state.RevisionAuthorityId, state.Revision, self);
    }

    public static string ComputePolicyResultHash(Phase8ATopologyCostPolicyResult result) =>
        Phase8AMappingAuthorityHasher.Hash(new
        {
            algorithm = PolicyResultAlgorithm,
            result.StatusId, result.IsCompleteSearch,
            result.VisitedSearchNodes, result.CompleteDrafts,
            result.VerifiedDrafts, result.RejectedDrafts,
            candidates = result.Candidates.Select(item => new
            {
                item.CandidateHash, item.TotalScore,
                item.Descriptor.CandidateId, item.Descriptor.TieBreakKey,
                issues = item.Descriptor.Issues.Select(issue => new
                {
                    issue.Code, issue.Severity, issue.Location, issue.RelatedId
                })
            }),
            issues = result.Issues.Select(issue => new
            {
                issue.Code, issue.Severity, issue.Location, issue.RelatedId
            }),
            result.PolicyConfigHash, result.CostProfileConfigHash,
            result.SearchBudgetHash, profileBinding = result.ProfileBinding
        });

    public static string ComputeSourceCandidateHash(
        Phase8ATopologyCostCandidate candidate,
        string statusId,
        bool isCompleteSearch,
        string problemTruncation)
    {
        var hash = Phase8AMappingAuthorityHasher.Hash(new
        {
            algorithm = "sha256/phase8a-topology-cost-candidate/v1",
            provenance = candidate.Provenance,
            operations = candidate.Draft.OperationSelections,
            collectives = candidate.Draft.CollectiveSelections,
            allocations = candidate.Verification.Allocations,
            Items = candidate.ScoreBreakdown,
            Total = candidate.TotalScore,
            tieBreak = candidate.Descriptor.TieBreakKey
        });
        var incomplete = candidate.Descriptor.Issues.Where(issue =>
            issue.Code == Phase8ATopologyCostPolicyIssueCodes.IncompleteSearch).ToArray();
        var rejectionDisclosures = candidate.Descriptor.Issues.Where(issue =>
            issue.Code != Phase8ATopologyCostPolicyIssueCodes.IncompleteSearch).ToArray();
        if (rejectionDisclosures.Length != 0)
            hash = DisclosureHash(
                hash, "verifier-rejection-summary-v1", rejectionDisclosures);
        if (isCompleteSearch)
            return incomplete.Length == 0 ? hash : "";
        if (incomplete.Length != 1 ||
            incomplete[0].Message !=
            "This candidate was retained from an incomplete search and is not a proven complete optimum. Status=" +
            statusId + "; problemTruncation=" + problemTruncation + ".")
            return "";
        return DisclosureHash(
            hash, "incomplete-search-disclosure-v1",
            rejectionDisclosures.Concat(incomplete).ToArray());
    }

    private static string DisclosureHash(
        string candidateHash,
        string disclosureId,
        IReadOnlyList<WorkloadMappingV2Issue> issues) =>
        Phase8AMappingAuthorityHasher.Hash(new
        {
            algorithm = "sha256/phase8a-topology-cost-candidate-disclosure/v1",
            CandidateHash = candidateHash,
            disclosureId,
            issues
        });

    public static string ComputeCandidateEnvelopeHash(
        Phase8ATopologyCostCandidate candidate,
        WorkloadMappingV2 mapping,
        string problemHash) =>
        Phase8AMappingAuthorityHasher.Hash(new
        {
            algorithm = CandidateEnvelopeAlgorithm,
            problemHash, candidate.CandidateHash, candidate.Provenance,
            operations = candidate.Draft.OperationSelections,
            collectives = candidate.Draft.CollectiveSelections,
            allocations = candidate.Verification.Allocations,
            descriptor = new
            {
                candidate.Descriptor.CandidateId,
                candidate.Descriptor.PolicyId,
                candidate.Descriptor.PolicyConfigHash,
                issues = candidate.Descriptor.Issues.Select(issue => new
                {
                    issue.Code, issue.Severity, issue.Location, issue.RelatedId
                }),
                candidate.Descriptor.ScoreBreakdown,
                candidate.Descriptor.TopologyHash,
                candidate.Descriptor.RouteHash,
                candidate.Descriptor.ProfileHash,
                candidate.Descriptor.TieBreakKey
            },
            candidate.TotalScore,
            mappingSemanticHash = WorkloadMappingV2CanonicalHasher.Compute(mapping).Hash,
            manualDiff = mapping.Candidate.ManualDiff
        });

    public static string ComputeProposalHash(Phase8AMappingProposal proposal) =>
        Phase8AMappingAuthorityHasher.Hash(new
        {
            algorithm = ProposalAlgorithm,
            proposal.SchemaVersion, proposal.ProposalId,
            baseSnapshotHash = proposal.BaseSnapshot.CanonicalHash,
            proposal.SourceCandidate.CandidateHash,
            proposedMappingHash = proposal.ProposedMapping.CanonicalHash,
            proposal.ManualDiff, proposal.PolicySearchStatusId,
            proposal.PolicySearchComplete,
            proposal.AcceptedIncompleteBestSoFar,
            proposal.CandidateEnvelopeHash
        });
}

internal static class Phase8AMappingProjection
{
    private const string CompilerVersion = "phase8a-mapping-proposal-compiler-v1";

    public static (WorkloadMappingV2 Mapping, IReadOnlyList<MappingManualDiffItem> ManualDiff) Project(
        Phase8AMappingProblem problem,
        Phase8ATopologyCostCandidate candidate)
    {
        var verification = Phase8AMappingCandidateVerifier.Verify(problem, candidate.Draft);
        if (!verification.IsSuccess ||
            !verification.Allocations.SequenceEqual(candidate.Verification.Allocations))
            throw new InvalidOperationException("Candidate verification decisions are not reproducible.");

        var selections = candidate.Draft.OperationSelections.ToDictionary(
            item => item.OperationTileId, StringComparer.Ordinal);
        var assignments = problem.Operations.Select(operation =>
        {
            var selection = selections[operation.Tile.OperationTileId];
            return operation.Tile.CreateAssignment(
                operation.Tile.OperationTileId,
                selection.TargetComponentId,
                selection.WeightPortId);
        }).ToArray();
        var placements = BuildPlacements(problem, candidate, selections);
        var flows = BuildFlows(problem, candidate, selections);
        var collectives = BuildCollectives(problem, candidate);
        var first = CreateMapping(
            problem, candidate, assignments, placements, flows, collectives,
            CopyCandidate(candidate.Descriptor, []), "");
        var manualDiff = Phase8AManualDiffProjector.Compute(problem, candidate, first);
        var semantic = CreateMapping(
            problem, candidate, assignments, placements, flows, collectives,
            CopyCandidate(candidate.Descriptor, manualDiff), "");
        var hash = WorkloadMappingV2CanonicalHasher.Compute(semantic);
        return (semantic.WithCanonicalHash(hash.Hash),
            Array.AsReadOnly(manualDiff.ToArray()));
    }

    private static IReadOnlyList<OperandPlacement> BuildPlacements(
        Phase8AMappingProblem problem,
        Phase8ATopologyCostCandidate candidate,
        IReadOnlyDictionary<string, Phase8AOperationCandidateSelection> selections)
    {
        var operations = problem.Operations.ToDictionary(
            item => item.Tile.OperationTileId, StringComparer.Ordinal);
        var groups = candidate.Verification.Allocations
            .GroupBy(item => new
            {
                item.ComponentId, item.ResourceId, item.AddressBits,
                item.SizeBits, item.ReuseKey
            })
            .OrderBy(group => group.Key.ComponentId, StringComparer.Ordinal)
            .ThenBy(group => group.Key.ResourceId, StringComparer.Ordinal)
            .ThenBy(group => group.Key.AddressBits)
            .ThenBy(group => group.Key.ReuseKey, StringComparer.Ordinal);
        var result = new List<OperandPlacement>();
        foreach (var group in groups)
        {
            var allocation = group.OrderBy(item => item.OperationTileId, StringComparer.Ordinal).First();
            var operation = operations[allocation.OperationTileId];
            var option = operation.TargetOptions.Single(item =>
                item.TargetComponentId == allocation.ComponentId &&
                item.StorageResourceId == allocation.ResourceId &&
                item.TargetComponentId == selections[allocation.OperationTileId].TargetComponentId);
            var selector = problem.StorageSelectors.Single(item =>
                item.ComponentId == allocation.ComponentId &&
                item.ResourceId == allocation.ResourceId);
            var storage = problem.CapabilityAuthority.Snapshot.Components
                .Single(item => item.ComponentId == allocation.ComponentId)
                .StorageCapabilities.Single(item => item.ResourceId == allocation.ResourceId);
            var expectedResidencyKey = Phase8AWeightResidencyKey.Compute(
                problem.NormalizedInput.Workload.Operations
                    .Single(item => item.OperationId == operation.Tile.OperationId)
                    .Tensors.Single(item => item.TensorId == operation.Weight.TensorId),
                operation.Weight);
            if (allocation.ReuseKey != expectedResidencyKey)
                throw new InvalidOperationException(
                    "Verifier allocation did not preserve the Core-owned exact weight residency key.");
            if (group.All(item => item.ReusedExistingAllocation))
            {
                var existing = problem.BaseMapping.OperandPlacements.SingleOrDefault(item =>
                    item.StorageComponentId == allocation.ComponentId &&
                    item.StorageLevelId == option.StorageLevelId &&
                    item.AddressBits == allocation.AddressBits &&
                    item.SizeBits >= allocation.SizeBits &&
                    item.ReuseGroupId == allocation.ReuseKey);
                if (existing is null)
                    throw new InvalidOperationException(
                        "A verifier-reused base residency did not resolve to one exact current placement.");
                result.Add(existing);
                continue;
            }
            var placementId = Phase8AWeightResidencyKey.ComputePlacementId(
                allocation.ComponentId, allocation.ResourceId,
                allocation.AddressBits, allocation.SizeBits,
                allocation.ReuseKey);
            result.Add(new OperandPlacement(
                placementId,
                operation.Tile.OperationId, operation.Weight.TensorId,
                operation.Weight.TileId, Phase8ATensorRoleIds.Weight,
                allocation.ComponentId, option.StorageLevelId,
                allocation.AddressBits, allocation.SizeBits,
                selector.AlignmentBits, "resident",
                storage.CommitModeId == "none" ? "preload-write" : "preload-write-commit",
                "mapping-apply", "inference-end", allocation.ReuseKey,
                storage.CommitModeId != "none"));
        }
        return Array.AsReadOnly(result.ToArray());
    }

    private static IReadOnlyList<CommunicationFlow> BuildFlows(
        Phase8AMappingProblem problem,
        Phase8ATopologyCostCandidate candidate,
        IReadOnlyDictionary<string, Phase8AOperationCandidateSelection> selections)
    {
        var result = new List<CommunicationFlow>();
        var produced = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var operation in problem.Operations)
        {
            var selection = selections[operation.Tile.OperationTileId];
            var weightIngress = problem.NormalizedInput.OperandIngressBindings.Single(item =>
                item.OperationId == operation.Tile.OperationId &&
                item.OperandRoleId == Phase8ATensorRoleIds.Weight);
            var allocation = candidate.Verification.Allocations.Single(item =>
                item.OperationTileId == operation.Tile.OperationTileId);
            if (!allocation.ReusedExistingAllocation)
                result.Add(Flow(
                    "flow:weight:" + operation.Tile.OperationTileId,
                    weightIngress.ProducerComponentId,
                    selection.TargetComponentId, operation.Weight.TileId,
                    TensorBits(operation.Weight.PaddedShape, operation.Weight.PrecisionId),
                    selection.WeightPathId, problem.LogicalPathCatalog));
            produced[operation.Output.TileId] = selection.TargetComponentId;
        }
        foreach (var distribution in BuildActivationDistributions(problem, selections))
            result.Add(ActivationFlow(distribution, problem.LogicalPathCatalog));

        var collectiveSelections = candidate.Draft.CollectiveSelections.ToDictionary(
            item => item.CollectiveIntentId, StringComparer.Ordinal);
        foreach (var collective in problem.Collectives)
        {
            var selection = collectiveSelections[collective.Intent.IntentId];
            foreach (var contributor in collective.Intent.ContributorTileIds)
            {
                var route = selection.ContributorRoutes.Single(item =>
                    item.ContributorTileId == contributor);
                result.Add(Flow(
                    "flow:collective:" + collective.Intent.IntentId + ":" + contributor,
                    produced[contributor], selection.TargetComponentId,
                    contributor, OutputBits(problem, contributor),
                    route.PathId, problem.LogicalPathCatalog));
            }
            produced[collective.Intent.ResultTileId] = selection.TargetComponentId;
        }
        return Array.AsReadOnly(result.ToArray());
    }

    internal static IReadOnlyList<Phase8AActivationDistribution> BuildActivationDistributions(
        Phase8AMappingProblem problem,
        IReadOnlyDictionary<string, Phase8AOperationCandidateSelection> selections)
    {
        var demands = problem.Operations.Select(operation =>
        {
            var selection = selections[operation.Tile.OperationTileId];
            var ingress = problem.NormalizedInput.OperandIngressBindings.Single(item =>
                item.OperationId == operation.Tile.OperationId &&
                item.OperandRoleId == Phase8ATensorRoleIds.Activation);
            return new Phase8AActivationRouteDemand(
                operation.Tile.OperationTileId,
                operation.Activation.TileId,
                ingress.ProducerComponentId,
                selection.TargetComponentId,
                selection.ActivationPathId,
                TensorBits(operation.Activation.PaddedShape, operation.Activation.PrecisionId));
        });
        return Phase8AActivationDistributionPlanner.Plan(
            demands, problem.LogicalPathCatalog);
    }

    private static CommunicationFlow ActivationFlow(
        Phase8AActivationDistribution distribution,
        Phase8ALogicalPathCatalog catalog)
    {
        var routes = distribution.Demands.Select(demand =>
        {
            var path = catalog.Find(demand.RoutePathId)
                ?? throw new InvalidOperationException("Selected activation path is absent from the exact catalog.");
            return new CommunicationConsumerRoute(demand.ConsumerComponentId, path.PathId, path.DirectedLinkIds);
        }).ToArray();
        return new CommunicationFlow(
            "flow:activation:" + distribution.DistributionId,
            distribution.ProducerComponentId,
            distribution.Demands.Select(item => item.ConsumerComponentId).ToArray(),
            distribution.TensorTileId,
            distribution.Bits,
            distribution.IsMulticast ? Phase8ACommunicationFlowKinds.Multicast : Phase8ACommunicationFlowKinds.Unicast,
            distribution.BranchComponentIds,
            routes);
    }

    private static CommunicationFlow Flow(
        string flowId, string producer, string consumer, string tileId,
        long bits, string pathId, Phase8ALogicalPathCatalog catalog)
    {
        var path = catalog.Find(pathId)
            ?? throw new InvalidOperationException("Selected route path is absent from the exact catalog.");
        if (path.SourceComponentId != producer ||
            path.DestinationComponentId != consumer)
            throw new InvalidOperationException(
                "Selected route path endpoints do not match the projected flow.");
        return new CommunicationFlow(
            flowId, producer, [consumer], tileId, bits, Phase8ACommunicationFlowKinds.Unicast, [],
            [new CommunicationConsumerRoute(consumer, pathId, path.DirectedLinkIds)]);
    }

    private static IReadOnlyList<CollectivePlan> BuildCollectives(
        Phase8AMappingProblem problem,
        Phase8ATopologyCostCandidate candidate)
    {
        var selections = candidate.Draft.CollectiveSelections.ToDictionary(
            item => item.CollectiveIntentId, StringComparer.Ordinal);
        return Array.AsReadOnly(problem.Collectives.Select(item =>
        {
            var selection = selections[item.Intent.IntentId];
            return new CollectivePlan(
                item.Intent.IntentId, item.Intent.KindId,
                item.Intent.ContributorTileIds, selection.TargetComponentId,
                item.Intent.ResultTileId, "stable-contributor-order-v1",
                item.Intent.PrecisionId, item.Intent.GroupKey,
                new CollectiveErrorBehavior("error", "error", "error", "error", "error"));
        }).ToArray());
    }

    private static WorkloadMappingV2 CreateMapping(
        Phase8AMappingProblem problem,
        Phase8ATopologyCostCandidate candidate,
        IReadOnlyList<OperationTileAssignment> assignments,
        IReadOnlyList<OperandPlacement> placements,
        IReadOnlyList<CommunicationFlow> flows,
        IReadOnlyList<CollectivePlan> collectives,
        MappingCandidate descriptor,
        string canonicalHash) =>
        new(
            WorkloadMappingV2.CurrentSchemaVersion,
            "phase8a-" + candidate.CandidateHash[..24],
            WorkloadMappingV2Modes.TopologyAware,
            problem.CapabilityAuthority.Snapshot,
            assignments, placements, flows, collectives, descriptor,
            new WorkloadMappingV2Provenance(
                Phase8AMappingAuthorityHasher.ComputeWorkloadHash(
                    problem.NormalizedInput.Workload),
                problem.BaseHashes.NormalizedInputHash,
                CompilerVersion, problem.NormalizedInput.Seed),
            null, WorkloadMappingV2.CurrentCanonicalHashAlgorithm,
            canonicalHash);

    private static MappingCandidate CopyCandidate(
        MappingCandidate candidate,
        IReadOnlyList<MappingManualDiffItem> manualDiff) =>
        new(
            candidate.CandidateId, candidate.PolicyId,
            candidate.PolicyConfigHash, candidate.Issues,
            candidate.ScoreBreakdown, candidate.TopologyHash,
            candidate.RouteHash, candidate.ProfileHash,
            candidate.TieBreakKey, manualDiff);

    private static long OutputBits(Phase8AMappingProblem problem, string tileId)
    {
        var output = problem.LoweringAuthorities
            .SelectMany(item => item.Plan.OutputTiles)
            .Single(item => item.TileId == tileId);
        return TensorBits(output.PaddedShape, output.PrecisionId);
    }

    private static long TensorBits(MappingShape shape, string precisionId) =>
        checked(shape.Dimensions.Aggregate(
                    1L, (total, dimension) => checked(total * dimension)) *
                PrecisionBits(precisionId));

    private static int PrecisionBits(string precisionId) => precisionId switch
    {
        nameof(PrecisionKind.FP32) or nameof(PrecisionKind.TF32) or nameof(PrecisionKind.INT32) => 32,
        nameof(PrecisionKind.FP16) or nameof(PrecisionKind.BF16) or nameof(PrecisionKind.INT16) => 16,
        nameof(PrecisionKind.FP8_E4M3) or nameof(PrecisionKind.FP8_E5M2) or nameof(PrecisionKind.INT8) => 8,
        nameof(PrecisionKind.INT4) => 4,
        nameof(PrecisionKind.INT2) => 2,
        nameof(PrecisionKind.Binary) => 1,
        _ => throw new InvalidOperationException(
            "Projection requires a concrete digital precision.")
    };
}

internal static class Phase8AManualDiffProjector
{
    public static IReadOnlyList<MappingManualDiffItem> Compute(
        Phase8AMappingProblem problem,
        Phase8ATopologyCostCandidate candidate,
        WorkloadMappingV2 proposed)
    {
        var result = new List<MappingManualDiffItem>();
        var current = problem.BaseMapping;
        foreach (var manual in problem.ManualTargetConstraints)
        {
            var operation = problem.FindOperation(manual.OperationTileId)
                ?? throw new InvalidOperationException(
                    "Validated manual target subject disappeared.");
            var selected = proposed.OperationTileAssignments.Single(item =>
                item.AssignmentId == manual.OperationTileId);
            var beforeAssignment = current.OperationTileAssignments.SingleOrDefault(item =>
                item.AssignmentId == manual.OperationTileId);
            Add(result,
                "$.operationTileAssignments[assignmentId=" +
                Escape(manual.OperationTileId) + "].targetComponentId",
                beforeAssignment?.TargetComponentId,
                selected.TargetComponentId, "manual-target-constraint");
            Add(result,
                "$.operationTileAssignments[assignmentId=" +
                Escape(manual.OperationTileId) + "].targetPortId",
                beforeAssignment?.TargetPortId,
                selected.TargetPortId, "manual-target-constraint");

            var artifact = problem.NormalizedInput.Workload.Operations
                .Single(item => item.OperationId == operation.Tile.OperationId)
                .Tensors.Single(item => item.TensorId == operation.Weight.TensorId);
            var residencyKey = Phase8AWeightResidencyKey.Compute(
                artifact, operation.Weight);
            var selector = problem.StorageSelectors.Single(item =>
                item.ComponentId == manual.TargetComponentId &&
                item.ResourceId == manual.StorageResourceId);
            var allocation = proposed.OperandPlacements.Single(item =>
                item.StorageComponentId == manual.TargetComponentId &&
                item.StorageLevelId == selector.LevelId &&
                item.ReuseGroupId == residencyKey);
            var beforePlacement = current.OperandPlacements.SingleOrDefault(item =>
                item.PlacementId == allocation.PlacementId);
            Add(result,
                "$.operandPlacements[placementId=" +
                Escape(allocation.PlacementId) + "].storageComponentId",
                beforePlacement?.StorageComponentId,
                allocation.StorageComponentId, "manual-target-constraint");
            Add(result,
                "$.operandPlacements[placementId=" +
                Escape(allocation.PlacementId) + "].storageLevelId",
                beforePlacement?.StorageLevelId,
                allocation.StorageLevelId, "manual-target-constraint");
        }

        var candidateSelections = candidate.Draft.OperationSelections.ToDictionary(
            item => item.OperationTileId, StringComparer.Ordinal);
        var activationDistributions = Phase8AMappingProjection.BuildActivationDistributions(
            problem, candidateSelections);
        foreach (var manual in problem.ManualOperandPathConstraints)
        {
            string flowId;
            CommunicationFlow? flow;
            CommunicationConsumerRoute route;
            if (manual.OperandRoleId == Phase8ATensorRoleIds.Activation)
            {
                var distribution = activationDistributions.Single(item =>
                    item.Demands.Any(demand => demand.DemandId == manual.OperationTileId));
                var demand = distribution.Demands.Single(item =>
                    item.DemandId == manual.OperationTileId);
                flowId = "flow:activation:" + distribution.DistributionId;
                flow = proposed.CommunicationFlows.SingleOrDefault(item =>
                    item.FlowId == flowId);
                route = flow?.ConsumerRoutes.SingleOrDefault(item =>
                    item.ConsumerComponentId == demand.ConsumerComponentId &&
                    item.RoutePathId == demand.RoutePathId)!;
            }
            else
            {
                flowId = "flow:weight:" + manual.OperationTileId;
                flow = proposed.CommunicationFlows.SingleOrDefault(item =>
                    item.FlowId == flowId);
                if (flow is null)
                {
                    var allocation = candidate.Verification.Allocations.Single(item =>
                        item.OperationTileId == manual.OperationTileId);
                    if (allocation.ReusedExistingAllocation)
                        continue;
                }
                route = flow?.ConsumerRoutes.SingleOrDefault()!;
            }
            if (flow is null || route is null)
                throw new InvalidOperationException(
                    "A validated manual operand path did not project to one exact communication flow route.");
            var beforeRoute = current.CommunicationFlows
                .SingleOrDefault(item => item.FlowId == flowId)?
                .ConsumerRoutes.SingleOrDefault(item =>
                    item.ConsumerComponentId == route.ConsumerComponentId);
            Add(result,
                "$.communicationFlows[flowId=" + Escape(flowId) +
                "].consumerRoutes[consumerComponentId=" +
                Escape(route.ConsumerComponentId) + "].routePathId",
                beforeRoute?.RoutePathId, route.RoutePathId,
                "manual-path-constraint");
        }

        foreach (var manual in problem.ManualCollectiveConstraints)
        {
            var plan = proposed.CollectivePlans.Single(item =>
                item.CollectiveId == manual.CollectiveIntentId);
            var before = current.CollectivePlans.SingleOrDefault(item =>
                item.CollectiveId == manual.CollectiveIntentId);
            Add(result,
                "$.collectivePlans[collectiveId=" +
                Escape(plan.CollectiveId) + "].targetComponentId",
                before?.TargetComponentId, plan.TargetComponentId,
                "manual-collective-constraint");
        }

        return Array.AsReadOnly(result
            .Distinct()
            .OrderBy(item => item.Path, StringComparer.Ordinal)
            .ThenBy(item => item.ReasonCode, StringComparer.Ordinal)
            .ToArray());
    }

    private static void Add(
        List<MappingManualDiffItem> result,
        string path, string? before, string after, string reason)
    {
        var actualBefore = before ?? "<missing>";
        if (actualBefore != after)
            result.Add(new MappingManualDiffItem(
                path, actualBefore, after, reason));
    }

    private static string Escape(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("]", "\\]", StringComparison.Ordinal)
            .Replace("=", "\\=", StringComparison.Ordinal);
}
