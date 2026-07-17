using System.Collections.ObjectModel;
using System.Text.Json.Nodes;

namespace HardwareSim.Core;

/// <summary>Names the Phase 8A meaning of communication-route fields without changing the WorkloadMapping 2.0 wire schema.</summary>
public static class Phase8ACommunicationRouteSemantics
{
    /// <summary>States that <see cref="CommunicationConsumerRoute.RoutePathId"/> is an immutable logical-path catalog entry id.</summary>
    public const string RoutePathIdMeaning = "phase8a-logical-path-catalog-entry-id";

    /// <summary>States that <see cref="CommunicationConsumerRoute.LinkIds"/> is the exact ordered logical-hop list stored by that catalog entry.</summary>
    public const string LinkIdsMeaning = "ordered-directed-logical-hop-ids";
}

/// <summary>Requests one named logical path between two exact component identities.</summary>
public sealed class Phase8ALogicalPathRequest
{
    /// <summary>Creates a logical-path request without interpreting component ids or names.</summary>
    public Phase8ALogicalPathRequest(string pathId, string sourceComponentId, string destinationComponentId)
    {
        PathId = pathId?.Trim() ?? "";
        SourceComponentId = sourceComponentId?.Trim() ?? "";
        DestinationComponentId = destinationComponentId?.Trim() ?? "";
    }

    /// <summary>Gets the stable catalog entry identity.</summary>
    public string PathId { get; }

    /// <summary>Gets the exact source component identity.</summary>
    public string SourceComponentId { get; }

    /// <summary>Gets the exact destination component identity.</summary>
    public string DestinationComponentId { get; }
}

/// <summary>Supplies one explicit custom-topology logical path; no path search is performed for this contract.</summary>
public sealed class Phase8AExplicitLogicalPath
{
    /// <summary>Creates an explicit immutable logical-path declaration.</summary>
    public Phase8AExplicitLogicalPath(
        string pathId,
        string sourceComponentId,
        string destinationComponentId,
        string algorithmId,
        IEnumerable<string>? directedLinkIds)
    {
        PathId = pathId?.Trim() ?? "";
        SourceComponentId = sourceComponentId?.Trim() ?? "";
        DestinationComponentId = destinationComponentId?.Trim() ?? "";
        AlgorithmId = algorithmId?.Trim() ?? "";
        DirectedLinkIds = Array.AsReadOnly((directedLinkIds ?? []).Select(id => id?.Trim() ?? "").ToArray());
    }

    /// <summary>Gets the stable catalog entry identity.</summary>
    public string PathId { get; }

    /// <summary>Gets the exact source component identity.</summary>
    public string SourceComponentId { get; }

    /// <summary>Gets the exact destination component identity.</summary>
    public string DestinationComponentId { get; }

    /// <summary>Gets the versioned algorithm or explicit-policy identity.</summary>
    public string AlgorithmId { get; }

    /// <summary>Gets the exact directed logical links in traversal order.</summary>
    public IReadOnlyList<string> DirectedLinkIds { get; }
}

/// <summary>Captures one immutable per-hop logical, endpoint-domain, and physical-route cost snapshot.</summary>
public sealed class Phase8APhysicalHopCostSnapshot
{
    internal Phase8APhysicalHopCostSnapshot(
        string logicalLinkId,
        string sourceComponentId,
        string sourcePortName,
        string destinationComponentId,
        string destinationPortName,
        int bandwidthBitsPerCycle,
        int latencyCycles,
        string routeType,
        RoutingMedium medium,
        double physicalLength,
        double routeGeometryLengthMicrometers,
        int bendCount,
        string signalDomain,
        string clockDomain,
        string physicalRouteHash)
    {
        LogicalLinkId = logicalLinkId;
        SourceComponentId = sourceComponentId;
        SourcePortName = sourcePortName;
        DestinationComponentId = destinationComponentId;
        DestinationPortName = destinationPortName;
        BandwidthBitsPerCycle = bandwidthBitsPerCycle;
        LatencyCycles = latencyCycles;
        RouteType = routeType;
        Medium = medium;
        PhysicalLength = physicalLength;
        RouteGeometryLengthMicrometers = routeGeometryLengthMicrometers;
        BendCount = bendCount;
        SignalDomain = signalDomain;
        ClockDomain = clockDomain;
        PhysicalRouteHash = physicalRouteHash;
    }

    /// <summary>Gets the exact logical link identity.</summary>
    public string LogicalLinkId { get; }

    /// <summary>Gets the exact source component identity.</summary>
    public string SourceComponentId { get; }

    /// <summary>Gets the exact source port name.</summary>
    public string SourcePortName { get; }

    /// <summary>Gets the exact destination component identity.</summary>
    public string DestinationComponentId { get; }

    /// <summary>Gets the exact destination port name.</summary>
    public string DestinationPortName { get; }

    /// <summary>Gets the logical link bandwidth in bits per cycle.</summary>
    public int BandwidthBitsPerCycle { get; }

    /// <summary>Gets the logical link latency in cycles.</summary>
    public int LatencyCycles { get; }

    /// <summary>Gets the exact legacy route-type field snapshot.</summary>
    public string RouteType { get; }

    /// <summary>Gets the explicit structured physical routing medium.</summary>
    public RoutingMedium Medium { get; }

    /// <summary>Gets the exact logical-link physical-length field used by transport cost models.</summary>
    public double PhysicalLength { get; }

    /// <summary>Gets the measured explicit RoutePath geometry length in micrometers.</summary>
    public double RouteGeometryLengthMicrometers { get; }

    /// <summary>Gets the exact explicit RoutePath bend count.</summary>
    public int BendCount { get; }

    /// <summary>Gets the matching signal, payload, precision, and protocol endpoint domain.</summary>
    public string SignalDomain { get; }

    /// <summary>Gets the matching endpoint clock domain.</summary>
    public string ClockDomain { get; }

    /// <summary>Gets the canonical hash of the exact physical route fields and points.</summary>
    public string PhysicalRouteHash { get; }
}

/// <summary>Represents one immutable logical-path catalog entry with ordered hop snapshots.</summary>
public sealed class Phase8ALogicalPathCatalogEntry
{
    internal Phase8ALogicalPathCatalogEntry(
        string pathId,
        string sourceComponentId,
        string destinationComponentId,
        string algorithmId,
        IEnumerable<Phase8APhysicalHopCostSnapshot> hops,
        string canonicalHashAlgorithm,
        string canonicalHash)
    {
        PathId = pathId;
        SourceComponentId = sourceComponentId;
        DestinationComponentId = destinationComponentId;
        AlgorithmId = algorithmId;
        Hops = Array.AsReadOnly(hops.ToArray());
        DirectedLinkIds = Array.AsReadOnly(Hops.Select(hop => hop.LogicalLinkId).ToArray());
        CanonicalHashAlgorithm = canonicalHashAlgorithm;
        CanonicalHash = canonicalHash;
    }

    /// <summary>Gets the stable catalog entry identity referenced by CommunicationConsumerRoute.RoutePathId.</summary>
    public string PathId { get; }

    /// <summary>Gets the exact source component identity.</summary>
    public string SourceComponentId { get; }

    /// <summary>Gets the exact destination component identity.</summary>
    public string DestinationComponentId { get; }

    /// <summary>Gets the versioned path-construction algorithm identity.</summary>
    public string AlgorithmId { get; }

    /// <summary>Gets immutable hop snapshots in exact traversal order.</summary>
    public IReadOnlyList<Phase8APhysicalHopCostSnapshot> Hops { get; }

    /// <summary>Gets ordered logical hop ids with the WorkloadMapping 2.0 CommunicationConsumerRoute.LinkIds semantics.</summary>
    public IReadOnlyList<string> DirectedLinkIds { get; }

    /// <summary>Gets the versioned path-entry canonical hash algorithm.</summary>
    public string CanonicalHashAlgorithm { get; }

    /// <summary>Gets the lowercase SHA-256 path-entry digest.</summary>
    public string CanonicalHash { get; }
}

/// <summary>Contains immutable logical paths bound to exact topology, placement, and physical-route base hashes.</summary>
public sealed class Phase8ALogicalPathCatalog
{
    /// <summary>Defines the initial logical-path catalog contract version.</summary>
    public const string CurrentSchemaVersion = "1.0";

    internal Phase8ALogicalPathCatalog(
        string topologyGraphHashAlgorithm,
        string topologyGraphHash,
        string placementHashAlgorithm,
        string placementHash,
        string routeHashAlgorithm,
        string routeHash,
        string topologyManifestHashAlgorithm,
        string topologyManifestHash,
        IEnumerable<Phase8ALogicalPathCatalogEntry> entries,
        string canonicalHashAlgorithm,
        string canonicalHash)
    {
        TopologyGraphHashAlgorithm = topologyGraphHashAlgorithm;
        TopologyGraphHash = topologyGraphHash;
        PlacementHashAlgorithm = placementHashAlgorithm;
        PlacementHash = placementHash;
        RouteHashAlgorithm = routeHashAlgorithm;
        RouteHash = routeHash;
        TopologyManifestHashAlgorithm = topologyManifestHashAlgorithm;
        TopologyManifestHash = topologyManifestHash;
        Entries = Array.AsReadOnly(entries.OrderBy(entry => entry.PathId, StringComparer.Ordinal).ToArray());
        CanonicalHashAlgorithm = canonicalHashAlgorithm;
        CanonicalHash = canonicalHash;
    }

    /// <summary>Gets the logical-path catalog schema version.</summary>
    public string SchemaVersion => CurrentSchemaVersion;

    /// <summary>Gets the semantic topology hash algorithm captured at catalog creation.</summary>
    public string TopologyGraphHashAlgorithm { get; }

    /// <summary>Gets the semantic topology hash captured at catalog creation.</summary>
    public string TopologyGraphHash { get; }

    /// <summary>Gets the physical placement hash algorithm captured at catalog creation.</summary>
    public string PlacementHashAlgorithm { get; }

    /// <summary>Gets the physical placement hash captured at catalog creation.</summary>
    public string PlacementHash { get; }

    /// <summary>Gets the physical routing hash algorithm captured at catalog creation.</summary>
    public string RouteHashAlgorithm { get; }

    /// <summary>Gets the physical routing hash captured at catalog creation.</summary>
    public string RouteHash { get; }

    /// <summary>Gets the canonical typed-topology manifest hash algorithm captured at catalog creation.</summary>
    public string TopologyManifestHashAlgorithm { get; }

    /// <summary>Gets the canonical typed-topology manifest hash captured at catalog creation.</summary>
    public string TopologyManifestHash { get; }

    /// <summary>Gets immutable entries in stable path-id order.</summary>
    public IReadOnlyList<Phase8ALogicalPathCatalogEntry> Entries { get; }

    /// <summary>Gets the versioned catalog canonical hash algorithm.</summary>
    public string CanonicalHashAlgorithm { get; }

    /// <summary>Gets the lowercase SHA-256 catalog digest.</summary>
    public string CanonicalHash { get; }

    /// <summary>Resolves one exact logical catalog identity without case or name inference.</summary>
    public Phase8ALogicalPathCatalogEntry? Find(string pathId) =>
        Entries.SingleOrDefault(entry => string.Equals(entry.PathId, pathId, StringComparison.Ordinal));

    /// <summary>Resolves and verifies one WorkloadMapping 2.0 consumer binding against exact catalog semantics.</summary>
    public bool TryResolve(CommunicationConsumerRoute route, out Phase8ALogicalPathCatalogEntry? entry)
    {
        if (route is null)
        {
            entry = null;
            return false;
        }

        entry = Find(route.RoutePathId);
        return entry is not null &&
               string.Equals(route.ConsumerComponentId, entry.DestinationComponentId, StringComparison.Ordinal) &&
               entry.DirectedLinkIds.SequenceEqual(route.LinkIds, StringComparer.Ordinal);
    }
}

/// <summary>Describes one deterministic logical-path catalog build or validation failure.</summary>
/// <param name="Code">Stable machine-readable issue code.</param>
/// <param name="Severity">Typed issue severity.</param>
/// <param name="Location">JSON-style contract location.</param>
/// <param name="Message">Human-readable issue message.</param>
public sealed record Phase8APathCatalogIssue(string Code, ValidationSeverity Severity, string Location, string Message);

/// <summary>Defines stable issue codes for strict Phase 8A logical and physical path validation.</summary>
public static class Phase8APathCatalogIssueCodes
{
    /// <summary>Identifies a missing or malformed path declaration.</summary>
    public const string InvalidPath = "Phase8AInvalidLogicalPath";
    /// <summary>Identifies duplicate stable path identities.</summary>
    public const string DuplicatePath = "Phase8ADuplicateLogicalPath";
    /// <summary>Identifies a path endpoint that does not resolve exactly.</summary>
    public const string InvalidEndpoint = "Phase8AInvalidPathEndpoint";
    /// <summary>Identifies a missing logical link.</summary>
    public const string MissingLogicalLink = "Phase8AMissingLogicalLink";
    /// <summary>Identifies a duplicate logical link identity.</summary>
    public const string DuplicateLogicalLink = "Phase8ADuplicateLogicalLink";
    /// <summary>Identifies a repeated logical link inside one path.</summary>
    public const string DuplicatePathHop = "Phase8ADuplicatePathHop";
    /// <summary>Identifies ordered hops that do not form the declared endpoint path.</summary>
    public const string DiscontinuousPath = "Phase8ADiscontinuousLogicalPath";
    /// <summary>Identifies a missing explicit physical route.</summary>
    public const string MissingPhysicalRoute = "Phase8AMissingPhysicalRoute";
    /// <summary>Identifies duplicate explicit physical routes for one logical link.</summary>
    public const string DuplicatePhysicalRoute = "Phase8ADuplicatePhysicalRoute";
    /// <summary>Identifies an invalid explicit physical route.</summary>
    public const string InvalidPhysicalRoute = "Phase8AInvalidPhysicalRoute";
    /// <summary>Identifies logical, manifest, port, or physical-route endpoint disagreement.</summary>
    public const string EndpointMismatch = "Phase8APathEndpointMismatch";
    /// <summary>Identifies incompatible signal or clock domains at a logical hop boundary.</summary>
    public const string DomainMismatch = "Phase8APathDomainMismatch";
    /// <summary>Identifies a topology graph hash that changed after catalog construction.</summary>
    public const string TopologyHashStale = "Phase8APathTopologyHashStale";
    /// <summary>Identifies a placement hash that changed after catalog construction.</summary>
    public const string PlacementHashStale = "Phase8APathPlacementHashStale";
    /// <summary>Identifies a physical route hash that changed after catalog construction.</summary>
    public const string RouteHashStale = "Phase8APathRouteHashStale";
    /// <summary>Identifies invalid or changed typed topology-manifest content.</summary>
    public const string ManifestHashStale = "Phase8APathManifestHashStale";
    /// <summary>Identifies a malformed typed topology manifest supplied to the path builder.</summary>
    public const string InvalidManifest = "Phase8AInvalidTopologyManifest";
    /// <summary>Identifies a cycle in typed parent, child, or attachment hierarchy metadata.</summary>
    public const string HierarchyCycle = "Phase8ATopologyHierarchyCycle";
    /// <summary>Identifies a logical-link PhysicalLength snapshot change.</summary>
    public const string PhysicalLengthStale = "Phase8APathPhysicalLengthStale";
    /// <summary>Identifies a logical-link RouteType snapshot change or medium disagreement.</summary>
    public const string RouteTypeStale = "Phase8APathRouteTypeStale";
    /// <summary>Identifies a path-entry canonical hash mismatch.</summary>
    public const string PathHashMismatch = "Phase8APathHashMismatch";
    /// <summary>Identifies an aggregate catalog canonical hash mismatch.</summary>
    public const string CatalogHashMismatch = "Phase8APathCatalogHashMismatch";
    /// <summary>Identifies a path request unsupported by the selected typed topology helper.</summary>
    public const string UnsupportedTypedPath = "Phase8AUnsupportedTypedPath";
}

/// <summary>Contains either one immutable logical-path catalog or deterministic structured issues.</summary>
public sealed class Phase8ALogicalPathCatalogBuildResult
{
    internal Phase8ALogicalPathCatalogBuildResult(Phase8ALogicalPathCatalog? catalog, IEnumerable<Phase8APathCatalogIssue> issues)
    {
        Catalog = catalog;
        Issues = Array.AsReadOnly(issues.ToArray());
    }

    /// <summary>Gets the immutable catalog when strict construction succeeds.</summary>
    public Phase8ALogicalPathCatalog? Catalog { get; }

    /// <summary>Gets deterministic structured build issues.</summary>
    public IReadOnlyList<Phase8APathCatalogIssue> Issues { get; }

    /// <summary>Gets whether a catalog was built without error issues.</summary>
    public bool IsSuccess => Catalog is not null && Issues.All(issue => issue.Severity != ValidationSeverity.Error);
}

/// <summary>Builds and validates strict precomputed logical/physical path catalogs without fallback path search.</summary>
public static class Phase8ALogicalPathCatalogBuilder
{
    /// <summary>Identifies the canonical path-entry hash projection.</summary>
    public const string PathHashAlgorithm = "sha256/phase8a-logical-path-entry-1.0/v2";

    /// <summary>Identifies the canonical aggregate catalog hash projection.</summary>
    public const string CatalogHashAlgorithm = "sha256/phase8a-logical-path-catalog-1.0/v2";

    /// <summary>Identifies the typed mesh-of-trees hierarchy plus column-before-row mesh policy.</summary>
    public const string MeshOfTreesAlgorithmId = "typed-mesh-of-trees-hierarchy-xy-column-then-row-v1";

    /// <summary>Builds flat-mesh paths by explicitly consuming the typed deterministic XY planner result for every request.</summary>
    public static Phase8ALogicalPathCatalogBuildResult BuildFlatMesh(
        HardwareGraph graph,
        TopologyManifest manifest,
        IEnumerable<Phase8ALogicalPathRequest>? requests)
    {
        if (graph is null) throw new ArgumentNullException(nameof(graph));
        if (manifest is null) throw new ArgumentNullException(nameof(manifest));
        var issues = new List<Phase8APathCatalogIssue>();
        if (!string.Equals(manifest.Request.TopologyId, ReferenceMappingTopologyIds.Flat2DMeshV1, StringComparison.Ordinal))
        {
            issues.Add(Issue(Phase8APathCatalogIssueCodes.UnsupportedTypedPath, "$.topologyId", "Flat-mesh catalog construction requires the typed flat-2d-mesh manifest."));
        }

        var explicitPaths = new List<Phase8AExplicitLogicalPath>();
        foreach (var request in StableRequests(requests, issues))
        {
            var planned = TopologyLogicalPathPlanner.PlanDeterministicXy(manifest, request.SourceComponentId, request.DestinationComponentId);
            if (!planned.IsSuccess)
            {
                issues.AddRange(planned.Issues.Select(item => Issue(
                    Phase8APathCatalogIssueCodes.UnsupportedTypedPath,
                    "$.paths[" + request.PathId + "]",
                    item.Code + ": " + item.Message)));
                continue;
            }

            explicitPaths.Add(new Phase8AExplicitLogicalPath(
                request.PathId,
                request.SourceComponentId,
                request.DestinationComponentId,
                planned.Path!.AlgorithmId,
                planned.Path.DirectedLinkIds));
        }

        return BuildExplicitCore(graph, manifest, explicitPaths, issues);
    }

    /// <summary>Builds mesh-of-trees paths only from typed hierarchy, attachment, role, scope, and mesh-coordinate metadata.</summary>
    public static Phase8ALogicalPathCatalogBuildResult BuildMeshOfTrees(
        HardwareGraph graph,
        TopologyManifest manifest,
        IEnumerable<Phase8ALogicalPathRequest>? requests)
    {
        if (graph is null) throw new ArgumentNullException(nameof(graph));
        if (manifest is null) throw new ArgumentNullException(nameof(manifest));
        var issues = new List<Phase8APathCatalogIssue>();
        if (!string.Equals(manifest.Request.TopologyId, ReferenceMappingTopologyIds.MeshOfTreesV1, StringComparison.Ordinal))
        {
            issues.Add(Issue(Phase8APathCatalogIssueCodes.UnsupportedTypedPath, "$.topologyId", "Mesh-of-trees catalog construction requires the typed mesh-of-trees manifest."));
        }

        var explicitPaths = new List<Phase8AExplicitLogicalPath>();
        foreach (var request in StableRequests(requests, issues))
        {
            var planned = PlanMeshOfTrees(manifest, request, issues);
            if (planned is not null)
            {
                explicitPaths.Add(planned);
            }
        }

        return BuildExplicitCore(graph, manifest, explicitPaths, issues);
    }

    /// <summary>Builds a custom-topology catalog exclusively from caller-supplied explicit ordered entries.</summary>
    public static Phase8ALogicalPathCatalogBuildResult BuildExplicit(
        HardwareGraph graph,
        TopologyManifest manifest,
        IEnumerable<Phase8AExplicitLogicalPath>? explicitPaths)
    {
        if (graph is null) throw new ArgumentNullException(nameof(graph));
        if (manifest is null) throw new ArgumentNullException(nameof(manifest));
        return BuildExplicitCore(graph, manifest, explicitPaths ?? [], []);
    }

    /// <summary>Validates an existing catalog against exact current graph, manifest, route, cost, endpoint, and domain state.</summary>
    public static IReadOnlyList<Phase8APathCatalogIssue> Validate(
        Phase8ALogicalPathCatalog catalog,
        HardwareGraph graph,
        TopologyManifest manifest)
    {
        if (catalog is null) throw new ArgumentNullException(nameof(catalog));
        if (graph is null) throw new ArgumentNullException(nameof(graph));
        if (manifest is null) throw new ArgumentNullException(nameof(manifest));
        var issues = new List<Phase8APathCatalogIssue>();
        ValidateBaseHashes(graph, manifest, catalog, issues);

        var selfHash = ComputeCatalogHash(catalog);
        if (!string.Equals(selfHash, catalog.CanonicalHash, StringComparison.Ordinal))
        {
            issues.Add(Issue(Phase8APathCatalogIssueCodes.CatalogHashMismatch, "$.canonicalHash", "The stored catalog canonical hash does not match its immutable entries."));
        }

        foreach (var entry in catalog.Entries)
        {
            var before = issues.Count;
            var current = SnapshotPath(
                graph,
                manifest,
                new Phase8AExplicitLogicalPath(entry.PathId, entry.SourceComponentId, entry.DestinationComponentId, entry.AlgorithmId, entry.DirectedLinkIds),
                issues);
            if (current is null || issues.Skip(before).Any(issue => issue.Severity == ValidationSeverity.Error))
            {
                continue;
            }

            CompareEntry(entry, current, issues);
        }

        return Array.AsReadOnly(issues.OrderBy(issue => issue.Location, StringComparer.Ordinal).ThenBy(issue => issue.Code, StringComparer.Ordinal).ToArray());
    }

    private static Phase8ALogicalPathCatalogBuildResult BuildExplicitCore(
        HardwareGraph graph,
        TopologyManifest manifest,
        IEnumerable<Phase8AExplicitLogicalPath> explicitPaths,
        IEnumerable<Phase8APathCatalogIssue> preliminaryIssues)
    {
        var issues = preliminaryIssues.ToList();
        ValidateBaseHashes(graph, manifest, null, issues);
        var paths = explicitPaths.ToArray();
        if (paths.Length == 0)
        {
            issues.Add(Issue(Phase8APathCatalogIssueCodes.InvalidPath, "$.paths", "At least one explicit logical path is required."));
        }

        foreach (var duplicate in paths.Where(path => path is not null).GroupBy(path => path.PathId, StringComparer.Ordinal).Where(group => group.Count() > 1))
        {
            issues.Add(Issue(Phase8APathCatalogIssueCodes.DuplicatePath, "$.paths[" + duplicate.Key + "]", "Every logical path id must be unique."));
        }

        var entries = new List<Phase8ALogicalPathCatalogEntry>();
        foreach (var path in paths.Where(path => path is not null).OrderBy(path => path.PathId, StringComparer.Ordinal))
        {
            var entry = SnapshotPath(graph, manifest, path, issues);
            if (entry is not null)
            {
                entries.Add(entry);
            }
        }

        if (issues.Any(issue => issue.Severity == ValidationSeverity.Error))
        {
            return new Phase8ALogicalPathCatalogBuildResult(null, StableIssues(issues));
        }

        var unhashed = new Phase8ALogicalPathCatalog(
            manifest.TopologyGraphHashAlgorithm,
            manifest.TopologyGraphHash,
            manifest.PlacementHashAlgorithm,
            manifest.PlacementHash,
            manifest.RouteHashAlgorithm,
            manifest.RouteHash,
            manifest.CanonicalHashAlgorithm,
            manifest.CanonicalHash,
            entries,
            CatalogHashAlgorithm,
            "");
        var catalog = new Phase8ALogicalPathCatalog(
            unhashed.TopologyGraphHashAlgorithm,
            unhashed.TopologyGraphHash,
            unhashed.PlacementHashAlgorithm,
            unhashed.PlacementHash,
            unhashed.RouteHashAlgorithm,
            unhashed.RouteHash,
            unhashed.TopologyManifestHashAlgorithm,
            unhashed.TopologyManifestHash,
            unhashed.Entries,
            CatalogHashAlgorithm,
            ComputeCatalogHash(unhashed));
        return new Phase8ALogicalPathCatalogBuildResult(catalog, StableIssues(issues));
    }

    private static Phase8ALogicalPathCatalogEntry? SnapshotPath(
        HardwareGraph graph,
        TopologyManifest manifest,
        Phase8AExplicitLogicalPath path,
        List<Phase8APathCatalogIssue> issues)
    {
        var location = "$.paths[" + path.PathId + "]";
        var startIssueCount = issues.Count;
        if (string.IsNullOrWhiteSpace(path.PathId) || string.IsNullOrWhiteSpace(path.SourceComponentId) ||
            string.IsNullOrWhiteSpace(path.DestinationComponentId) || string.IsNullOrWhiteSpace(path.AlgorithmId))
        {
            issues.Add(Issue(Phase8APathCatalogIssueCodes.InvalidPath, location, "Path id, endpoints, and algorithm id must be non-empty."));
        }

        if (path.DirectedLinkIds.Any(string.IsNullOrWhiteSpace))
        {
            issues.Add(Issue(Phase8APathCatalogIssueCodes.InvalidPath, location + ".directedLinkIds", "Logical hop ids must be non-empty."));
        }

        foreach (var duplicate in path.DirectedLinkIds.GroupBy(id => id, StringComparer.Ordinal).Where(group => group.Count() > 1))
        {
            issues.Add(Issue(Phase8APathCatalogIssueCodes.DuplicatePathHop, location + ".directedLinkIds", "Logical link '" + duplicate.Key + "' appears more than once in one path."));
        }

        var hops = new List<Phase8APhysicalHopCostSnapshot>();
        foreach (var linkId in path.DirectedLinkIds)
        {
            var hop = SnapshotHop(graph, manifest, linkId, location, issues);
            if (hop is not null)
            {
                hops.Add(hop);
            }
        }

        ValidateContinuity(path, hops, location, issues);
        if (issues.Skip(startIssueCount).Any(issue => issue.Severity == ValidationSeverity.Error))
        {
            return null;
        }

        var hash = ComputePathHash(manifest, path, hops);
        return new Phase8ALogicalPathCatalogEntry(
            path.PathId,
            path.SourceComponentId,
            path.DestinationComponentId,
            path.AlgorithmId,
            hops,
            PathHashAlgorithm,
            hash);
    }

    private static Phase8APhysicalHopCostSnapshot? SnapshotHop(
        HardwareGraph graph,
        TopologyManifest manifest,
        string linkId,
        string pathLocation,
        List<Phase8APathCatalogIssue> issues)
    {
        var location = pathLocation + ".links[" + linkId + "]";
        var graphLinks = graph.Links.Where(link => string.Equals(link.Id, linkId, StringComparison.Ordinal)).ToArray();
        var manifestLinks = manifest.Links.Where(link => string.Equals(link.LinkId, linkId, StringComparison.Ordinal)).ToArray();
        if (graphLinks.Length == 0 || manifestLinks.Length == 0)
        {
            issues.Add(Issue(Phase8APathCatalogIssueCodes.MissingLogicalLink, location, "The ordered hop must resolve one graph link and one typed manifest link."));
            return null;
        }

        if (graphLinks.Length != 1 || manifestLinks.Length != 1)
        {
            issues.Add(Issue(Phase8APathCatalogIssueCodes.DuplicateLogicalLink, location, "The ordered hop must resolve exactly one graph link and one typed manifest link."));
            return null;
        }

        var link = graphLinks[0];
        var typedLink = manifestLinks[0];
        if (!string.Equals(link.Source.ComponentId, typedLink.SourceComponentId, StringComparison.Ordinal) ||
            !string.Equals(link.Destination.ComponentId, typedLink.DestinationComponentId, StringComparison.Ordinal))
        {
            issues.Add(Issue(Phase8APathCatalogIssueCodes.EndpointMismatch, location, "Graph and typed-manifest link endpoints must match exactly."));
        }

        var sourcePort = ResolvePort(graph, link.Source, PortDirection.Output, location + ".source", issues);
        var destinationPort = ResolvePort(graph, link.Destination, PortDirection.Input, location + ".destination", issues);
        var routes = (graph.Routing?.Routes ?? [])
            .Where(route => string.Equals(route.LinkId, linkId, StringComparison.Ordinal))
            .ToArray();
        if (routes.Length == 0)
        {
            issues.Add(Issue(Phase8APathCatalogIssueCodes.MissingPhysicalRoute, location, "The logical hop requires one explicit logical-link physical RoutePath."));
            return null;
        }

        if (routes.Length != 1)
        {
            issues.Add(Issue(Phase8APathCatalogIssueCodes.DuplicatePhysicalRoute, location, "The logical hop must have exactly one physical RoutePath identity across all target kinds."));
            return null;
        }

        var route = routes[0];
        var routeValid = ValidatePhysicalRoute(graph, link, route, location, issues);
        var expectedRouteType = PhysicalRoute.MediumToLegacyRouteType(route.Medium);
        if (!string.Equals(link.RouteType, expectedRouteType, StringComparison.Ordinal))
        {
            issues.Add(Issue(Phase8APathCatalogIssueCodes.RouteTypeStale, location + ".routeType", "Logical-link RouteType must exactly match the explicit structured route medium."));
        }

        if (sourcePort is null || destinationPort is null || !routeValid)
        {
            return null;
        }

        if (sourcePort.SignalType != destinationPort.SignalType ||
            sourcePort.DataType != destinationPort.DataType ||
            sourcePort.Precision != destinationPort.Precision ||
            sourcePort.Protocol != destinationPort.Protocol ||
            !string.Equals(sourcePort.ClockDomain, destinationPort.ClockDomain, StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(sourcePort.ClockDomain))
        {
            issues.Add(Issue(Phase8APathCatalogIssueCodes.DomainMismatch, location, "Source and destination ports must have exactly matching signal, payload, precision, protocol, and clock domains."));
            return null;
        }

        var metrics = PhysicalRouteMetrics.Analyze(route.Path);
        var signalDomain = string.Join("/", sourcePort.SignalType, sourcePort.DataType, sourcePort.Precision, sourcePort.Protocol);
        return new Phase8APhysicalHopCostSnapshot(
            link.Id,
            link.Source.ComponentId,
            link.Source.PortName,
            link.Destination.ComponentId,
            link.Destination.PortName,
            link.BandwidthBitsPerCycle,
            link.LatencyCycles,
            link.RouteType,
            route.Medium,
            link.PhysicalLength,
            metrics.LengthMicrometers,
            metrics.BendCount,
            signalDomain,
            sourcePort.ClockDomain,
            ComputePhysicalRouteHash(route));
    }

    private static HardwarePort? ResolvePort(
        HardwareGraph graph,
        PortRef reference,
        PortDirection requiredDirection,
        string location,
        List<Phase8APathCatalogIssue> issues)
    {
        var components = graph.Components.Where(component => string.Equals(component.Id, reference.ComponentId, StringComparison.Ordinal)).ToArray();
        if (components.Length != 1)
        {
            issues.Add(Issue(Phase8APathCatalogIssueCodes.EndpointMismatch, location, "A logical endpoint component must resolve exactly by ordinal identity."));
            return null;
        }

        var ports = components[0].Ports.Where(port => string.Equals(port.Name, reference.PortName, StringComparison.Ordinal)).ToArray();
        if (ports.Length != 1 || ports[0].Direction != requiredDirection && ports[0].Direction != PortDirection.Bidirectional)
        {
            issues.Add(Issue(Phase8APathCatalogIssueCodes.EndpointMismatch, location, "A logical endpoint port must resolve exactly with a compatible direction."));
            return null;
        }

        return ports[0];
    }

    private static bool ValidatePhysicalRoute(
        HardwareGraph graph,
        HardwareLink link,
        PhysicalRoute route,
        string location,
        List<Phase8APathCatalogIssue> issues)
    {
        var valid = true;
        void Invalid(string message)
        {
            valid = false;
            issues.Add(Issue(Phase8APathCatalogIssueCodes.InvalidPhysicalRoute, location + ".physicalRoute", message));
        }

        if (route.TargetKind != PhysicalRouteTargetKind.LogicalLink || !Enum.IsDefined(typeof(RoutingMedium), route.Medium) ||
            route.LayerId is null || string.IsNullOrWhiteSpace(route.LayerId.Stack) || route.LayerId.Index < 0 ||
            route.PathUnit != PhysicalRoutePointUnit.Micrometers || route.Path.Count < 2)
        {
            Invalid("A physical hop requires a logical-link target, valid medium/layer, micrometer units, and at least two points.");
        }

        if (link.BandwidthBitsPerCycle <= 0 || link.LatencyCycles < 0 || !double.IsFinite(link.PhysicalLength) || link.PhysicalLength <= 0)
        {
            Invalid("Logical-link bandwidth, latency, and PhysicalLength cost fields must be finite and valid.");
        }

        var placement = graph.Placement;
        for (var index = 0; index < route.Path.Count; index++)
        {
            var point = route.Path[index];
            if (!double.IsFinite(point.X) || !double.IsFinite(point.Y))
            {
                Invalid("Every physical route point must be finite.");
            }

            if (placement is not null && placement.Rows > 0 && placement.Cols > 0 &&
                (point.X < placement.Origin.X - 0.000000001 ||
                 point.X > placement.Origin.X + placement.Cols * placement.CellWidthMicrometers + 0.000000001 ||
                 point.Y < placement.Origin.Y - 0.000000001 ||
                 point.Y > placement.Origin.Y + placement.Rows * placement.CellHeightMicrometers + 0.000000001))
            {
                Invalid("Every physical route point must remain inside explicit placement bounds.");
            }

            if (index > 0)
            {
                var previous = route.Path[index - 1];
                var sameX = Same(previous.X, point.X);
                var sameY = Same(previous.Y, point.Y);
                if (sameX == sameY)
                {
                    Invalid("Every physical route segment must be non-zero and Manhattan-routed.");
                }
            }
        }

        if (placement is null || !placement.TryGetPhysicalPosition(link.Source.ComponentId, out var source) ||
            !placement.TryGetPhysicalPosition(link.Destination.ComponentId, out var destination) || route.Path.Count == 0 ||
            !SamePoint(route.Path[0], source) || !SamePoint(route.Path[^1], destination))
        {
            issues.Add(Issue(Phase8APathCatalogIssueCodes.EndpointMismatch, location + ".physicalRoute", "Physical RoutePath endpoints must exactly match explicit endpoint placement."));
            valid = false;
        }

        return valid;
    }

    private static void ValidateContinuity(
        Phase8AExplicitLogicalPath path,
        IReadOnlyList<Phase8APhysicalHopCostSnapshot> hops,
        string location,
        List<Phase8APathCatalogIssue> issues)
    {
        if (hops.Count == 0)
        {
            if (!string.Equals(path.SourceComponentId, path.DestinationComponentId, StringComparison.Ordinal))
            {
                issues.Add(Issue(Phase8APathCatalogIssueCodes.DiscontinuousPath, location, "Only a same-component path may contain zero logical hops."));
            }

            return;
        }

        if (!string.Equals(hops[0].SourceComponentId, path.SourceComponentId, StringComparison.Ordinal) ||
            !string.Equals(hops[^1].DestinationComponentId, path.DestinationComponentId, StringComparison.Ordinal))
        {
            issues.Add(Issue(Phase8APathCatalogIssueCodes.DiscontinuousPath, location, "First and last hop endpoints must match the declared path endpoints exactly."));
        }

        for (var index = 1; index < hops.Count; index++)
        {
            if (!string.Equals(hops[index - 1].DestinationComponentId, hops[index].SourceComponentId, StringComparison.Ordinal))
            {
                issues.Add(Issue(Phase8APathCatalogIssueCodes.DiscontinuousPath, location, "Ordered logical hops must be endpoint-contiguous."));
                break;
            }
        }
    }

    private static void ValidateBaseHashes(
        HardwareGraph graph,
        TopologyManifest manifest,
        Phase8ALogicalPathCatalog? catalog,
        List<Phase8APathCatalogIssue> issues)
    {
        ValidateManifestIntegrity(graph, manifest, catalog, issues);
        var topology = TopologyPresetCanonicalizer.ComputeTopologyGraph(graph);
        var placement = TopologyPresetCanonicalizer.ComputePlacement(graph.Placement);
        var routes = TopologyPresetCanonicalizer.ComputeRouting(graph);
        ValidateHash(manifest.TopologyGraphHashAlgorithm, manifest.TopologyGraphHash, topology, catalog?.TopologyGraphHashAlgorithm, catalog?.TopologyGraphHash,
            Phase8APathCatalogIssueCodes.TopologyHashStale, "$.topologyGraphHash", issues);
        ValidateHash(manifest.PlacementHashAlgorithm, manifest.PlacementHash, placement, catalog?.PlacementHashAlgorithm, catalog?.PlacementHash,
            Phase8APathCatalogIssueCodes.PlacementHashStale, "$.placementHash", issues);
        ValidateHash(manifest.RouteHashAlgorithm, manifest.RouteHash, routes, catalog?.RouteHashAlgorithm, catalog?.RouteHash,
            Phase8APathCatalogIssueCodes.RouteHashStale, "$.routeHash", issues);
    }

    private static void ValidateManifestIntegrity(
        HardwareGraph graph,
        TopologyManifest manifest,
        Phase8ALogicalPathCatalog? catalog,
        List<Phase8APathCatalogIssue> issues)
    {
        if (!string.Equals(manifest.SchemaVersion, TopologyManifest.CurrentSchemaVersion, StringComparison.Ordinal))
        {
            issues.Add(Issue(Phase8APathCatalogIssueCodes.InvalidManifest, "$.schemaVersion", "The typed topology manifest schema version is unsupported."));
        }

        var computedManifest = TopologyPresetCanonicalizer.ComputeManifest(manifest);
        if (!string.Equals(manifest.CanonicalHashAlgorithm, computedManifest.Algorithm, StringComparison.Ordinal) ||
            !string.Equals(manifest.CanonicalHash, computedManifest.Hash, StringComparison.Ordinal))
        {
            issues.Add(Issue(Phase8APathCatalogIssueCodes.ManifestHashStale, "$.canonicalHash", "The typed topology manifest canonical hash does not match its typed content."));
        }

        var requestHash = TopologyPresetCanonicalizer.ComputeRequest(manifest.Request);
        if (!string.Equals(manifest.Provenance.RequestHashAlgorithm, requestHash.Algorithm, StringComparison.Ordinal) ||
            !string.Equals(manifest.Provenance.RequestHash, requestHash.Hash, StringComparison.Ordinal))
        {
            issues.Add(Issue(Phase8APathCatalogIssueCodes.ManifestHashStale, "$.provenance.requestHash", "The typed topology manifest request hash does not match its normalized request."));
        }

        if (catalog is not null &&
            (!string.Equals(catalog.TopologyManifestHashAlgorithm, manifest.CanonicalHashAlgorithm, StringComparison.Ordinal) ||
             !string.Equals(catalog.TopologyManifestHash, manifest.CanonicalHash, StringComparison.Ordinal)))
        {
            issues.Add(Issue(Phase8APathCatalogIssueCodes.ManifestHashStale, "$.topologyManifestHash", "The typed topology manifest changed after catalog construction."));
        }

        var manifestComponents = manifest.Components
            .GroupBy(component => component.ComponentId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
        if (manifestComponents.Any(pair => string.IsNullOrWhiteSpace(pair.Key) || pair.Value.Length != 1))
        {
            issues.Add(Issue(Phase8APathCatalogIssueCodes.InvalidManifest, "$.components", "Typed manifest component identities must be non-empty and unique."));
        }

        var manifestComponentIds = manifestComponents.Keys.ToHashSet(StringComparer.Ordinal);
        if (manifest.Components.Any(component =>
                component.ChildComponentIds.Any(string.IsNullOrWhiteSpace) ||
                component.ChildComponentIds.Distinct(StringComparer.Ordinal).Count() != component.ChildComponentIds.Count ||
                component.ParentComponentId is not null && !manifestComponentIds.Contains(component.ParentComponentId) ||
                component.AttachmentComponentId is not null && !manifestComponentIds.Contains(component.AttachmentComponentId) ||
                component.ChildComponentIds.Any(childId => !manifestComponentIds.Contains(childId))))
        {
            issues.Add(Issue(Phase8APathCatalogIssueCodes.InvalidManifest, "$.components", "Typed hierarchy and attachment references must be non-empty, unique, and resolve manifest components."));
        }

        var manifestLinks = manifest.Links
            .GroupBy(link => link.LinkId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
        if (manifestLinks.Any(pair => string.IsNullOrWhiteSpace(pair.Key) || pair.Value.Length != 1) ||
            manifest.Links.Any(link =>
                !manifestComponentIds.Contains(link.SourceComponentId) ||
                !manifestComponentIds.Contains(link.DestinationComponentId) ||
                link.LaneCount <= 0 || link.BandwidthBitsPerCycle <= 0 || link.Level < 0 ||
                !double.IsFinite(link.Distance) || link.Distance <= 0))
        {
            issues.Add(Issue(Phase8APathCatalogIssueCodes.InvalidManifest, "$.links", "Typed manifest links must be unique, resolve endpoints, and carry valid lane, bandwidth, level, and distance metadata."));
        }

        if (manifest.Components.Any(component =>
                graph.Components.Count(candidate => string.Equals(candidate.Id, component.ComponentId, StringComparison.Ordinal)) != 1) ||
            manifest.Links.Any(typedLink =>
            {
                var graphMatches = graph.Links.Where(link => string.Equals(link.Id, typedLink.LinkId, StringComparison.Ordinal)).ToArray();
                return graphMatches.Length != 1 ||
                       !string.Equals(graphMatches[0].Source.ComponentId, typedLink.SourceComponentId, StringComparison.Ordinal) ||
                       !string.Equals(graphMatches[0].Destination.ComponentId, typedLink.DestinationComponentId, StringComparison.Ordinal) ||
                       graphMatches[0].BandwidthBitsPerCycle != typedLink.BandwidthBitsPerCycle;
            }))
        {
            issues.Add(Issue(Phase8APathCatalogIssueCodes.InvalidManifest, "$.topologyManifest", "Every typed manifest component and link must resolve exactly against the supplied graph, including directed endpoints and bandwidth."));
        }

        if (graph.ExtensionData.ContainsKey(TopologyManifestJson.ExtensionPropertyName))
        {
            var embedded = TopologyManifestJson.ReadFromGraph(graph);
            if (embedded.Manifest is null || embedded.Issues.Any(issue => issue.Severity == ValidationSeverity.Error))
            {
                issues.Add(Issue(Phase8APathCatalogIssueCodes.InvalidManifest, "$.topology_manifest", "The graph-embedded typed topology manifest is invalid."));
            }
            else if (!string.Equals(embedded.Manifest.CanonicalHashAlgorithm, manifest.CanonicalHashAlgorithm, StringComparison.Ordinal) ||
                     !string.Equals(embedded.Manifest.CanonicalHash, manifest.CanonicalHash, StringComparison.Ordinal))
            {
                issues.Add(Issue(Phase8APathCatalogIssueCodes.ManifestHashStale, "$.topology_manifest", "The supplied typed topology manifest does not match the graph-embedded manifest."));
            }
        }
    }

    private static void ValidateHash(
        string manifestAlgorithm,
        string manifestHash,
        TopologyCanonicalHash current,
        string? catalogAlgorithm,
        string? catalogHash,
        string code,
        string location,
        List<Phase8APathCatalogIssue> issues)
    {
        if (!string.Equals(manifestAlgorithm, current.Algorithm, StringComparison.Ordinal) ||
            !string.Equals(manifestHash, current.Hash, StringComparison.Ordinal) ||
            catalogAlgorithm is not null && (!string.Equals(catalogAlgorithm, manifestAlgorithm, StringComparison.Ordinal) ||
                                             !string.Equals(catalogHash, manifestHash, StringComparison.Ordinal)))
        {
            issues.Add(Issue(code, location, "The current graph, typed manifest, and catalog base hash must match exactly."));
        }
    }

    private static void CompareEntry(
        Phase8ALogicalPathCatalogEntry expected,
        Phase8ALogicalPathCatalogEntry current,
        List<Phase8APathCatalogIssue> issues)
    {
        var location = "$.paths[" + expected.PathId + "]";
        for (var index = 0; index < Math.Min(expected.Hops.Count, current.Hops.Count); index++)
        {
            if (!Same(expected.Hops[index].PhysicalLength, current.Hops[index].PhysicalLength))
            {
                issues.Add(Issue(Phase8APathCatalogIssueCodes.PhysicalLengthStale, location + ".hops[" + index + "]", "Logical-link PhysicalLength changed after catalog construction."));
            }

            if (!string.Equals(expected.Hops[index].RouteType, current.Hops[index].RouteType, StringComparison.Ordinal))
            {
                issues.Add(Issue(Phase8APathCatalogIssueCodes.RouteTypeStale, location + ".hops[" + index + "]", "Logical-link RouteType changed after catalog construction."));
            }
        }

        if (!string.Equals(expected.CanonicalHashAlgorithm, current.CanonicalHashAlgorithm, StringComparison.Ordinal) ||
            !string.Equals(expected.CanonicalHash, current.CanonicalHash, StringComparison.Ordinal))
        {
            issues.Add(Issue(Phase8APathCatalogIssueCodes.PathHashMismatch, location + ".canonicalHash", "Current logical or physical hop state does not match the stored path hash."));
        }
    }

    private static IReadOnlyList<Phase8ALogicalPathRequest> StableRequests(
        IEnumerable<Phase8ALogicalPathRequest>? requests,
        List<Phase8APathCatalogIssue> issues)
    {
        var result = (requests ?? []).Where(request => request is not null).ToArray();
        if (result.Length == 0)
        {
            issues.Add(Issue(Phase8APathCatalogIssueCodes.InvalidPath, "$.paths", "At least one logical path request is required."));
        }

        return result.OrderBy(request => request.PathId, StringComparer.Ordinal).ToArray();
    }

    private static Phase8AExplicitLogicalPath? PlanMeshOfTrees(
        TopologyManifest manifest,
        Phase8ALogicalPathRequest request,
        List<Phase8APathCatalogIssue> issues)
    {
        var components = manifest.Components
            .GroupBy(component => component.ComponentId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
        if (!components.TryGetValue(request.SourceComponentId, out var sourceMatches) || sourceMatches.Length != 1 ||
            !components.TryGetValue(request.DestinationComponentId, out var destinationMatches) || destinationMatches.Length != 1)
        {
            issues.Add(Issue(Phase8APathCatalogIssueCodes.InvalidEndpoint, "$.paths[" + request.PathId + "]", "MoT endpoints must resolve exactly in the typed manifest."));
            return null;
        }

        var source = sourceMatches[0];
        var destination = destinationMatches[0];
        if (source.Role is not TopologyPresetComponentRole.ProcessingElement and not TopologyPresetComponentRole.MeshRouter ||
            destination.Role is not TopologyPresetComponentRole.ProcessingElement and not TopologyPresetComponentRole.MeshRouter)
        {
            issues.Add(Issue(Phase8APathCatalogIssueCodes.UnsupportedTypedPath, "$.paths[" + request.PathId + "]", "MoT catalog endpoints must be ProcessingElement or MeshRouter roles."));
            return null;
        }

        if (!HasTypedMoTLocation(source) || !HasTypedMoTLocation(destination))
        {
            issues.Add(Issue(Phase8APathCatalogIssueCodes.UnsupportedTypedPath, "$.paths[" + request.PathId + "]", "MoT endpoints require non-null typed cluster and mesh-coordinate metadata."));
            return null;
        }

        if (string.Equals(source.ComponentId, destination.ComponentId, StringComparison.Ordinal))
        {
            return new Phase8AExplicitLogicalPath(request.PathId, source.ComponentId, destination.ComponentId, MeshOfTreesAlgorithmId, []);
        }

        var links = new List<string>();
        var sourceRoot = source.Role == TopologyPresetComponentRole.MeshRouter
            ? source
            : AppendPartialSumAscent(manifest, components, source, links, request.PathId, issues);
        var destinationRoot = destination.Role == TopologyPresetComponentRole.MeshRouter
            ? destination
            : ResolveClusterMeshRoot(components, destination, request.PathId, issues);
        if (sourceRoot is null || destinationRoot is null)
        {
            return null;
        }

        if (!AppendTypedMeshXy(manifest, sourceRoot, destinationRoot, links, request.PathId, issues))
        {
            return null;
        }

        if (destination.Role == TopologyPresetComponentRole.ProcessingElement &&
            !AppendActivationDescent(manifest, components, destinationRoot, destination, links, request.PathId, issues))
        {
            return null;
        }

        return new Phase8AExplicitLogicalPath(request.PathId, source.ComponentId, destination.ComponentId, MeshOfTreesAlgorithmId, links);
    }

    private static TopologyManifestComponent? AppendPartialSumAscent(
        TopologyManifest manifest,
        IReadOnlyDictionary<string, TopologyManifestComponent[]> components,
        TopologyManifestComponent source,
        ICollection<string> links,
        string pathId,
        List<Phase8APathCatalogIssue> issues)
    {
        var current = source;
        var nextId = source.AttachmentComponentId;
        var visited = new HashSet<string>(StringComparer.Ordinal) { source.ComponentId };
        var traversed = 0;
        while (!string.IsNullOrWhiteSpace(nextId))
        {
            if (traversed++ >= manifest.Components.Count || !visited.Add(nextId))
            {
                issues.Add(Issue(Phase8APathCatalogIssueCodes.HierarchyCycle, "$.paths[" + pathId + "]", "MoT partial-sum attachment hierarchy contains a cycle."));
                return null;
            }

            if (!TryUniqueComponent(components, nextId, out var parent) ||
                !IsValidPartialSumParent(current, parent!) ||
                !SameClusterCoordinate(source, parent!))
            {
                issues.Add(Issue(Phase8APathCatalogIssueCodes.UnsupportedTypedPath, "$.paths[" + pathId + "]", "MoT partial-sum ascent requires PE to TreeReductionUnit, TreeReductionUnit to TreeReductionUnit, and root TreeReductionUnit to MeshRouter only."));
                return null;
            }

            var reciprocal = parent!.Role == TopologyPresetComponentRole.MeshRouter
                ? string.Equals(parent.AttachmentComponentId, current.ComponentId, StringComparison.Ordinal)
                : parent.ChildComponentIds.Count(id => string.Equals(id, current.ComponentId, StringComparison.Ordinal)) == 1;
            if (!reciprocal)
            {
                issues.Add(Issue(Phase8APathCatalogIssueCodes.UnsupportedTypedPath, "$.paths[" + pathId + "]", "MoT partial-sum Parent/Children/Attachment metadata must be reciprocal and unique."));
                return null;
            }

            var scope = parent.Role == TopologyPresetComponentRole.MeshRouter
                ? TopologyPresetLinkScope.Attachment
                : current.Role == TopologyPresetComponentRole.ProcessingElement ? TopologyPresetLinkScope.Leaf : TopologyPresetLinkScope.Tree;
            if (!AppendExactTypedLink(manifest, current, parent, TopologyPresetLinkRole.PartialSumReturn, scope, links, pathId, issues))
            {
                return null;
            }

            if (parent.Role == TopologyPresetComponentRole.MeshRouter)
            {
                return parent;
            }

            current = parent;
            nextId = parent.ParentComponentId;
        }

        issues.Add(Issue(Phase8APathCatalogIssueCodes.UnsupportedTypedPath, "$.paths[" + pathId + "]", "MoT partial-sum hierarchy did not terminate at a typed MeshRouter."));
        return null;
    }
    private static TopologyManifestComponent? ResolveClusterMeshRoot(
        IReadOnlyDictionary<string, TopologyManifestComponent[]> components,
        TopologyManifestComponent endpoint,
        string pathId,
        List<Phase8APathCatalogIssue> issues)
    {
        var current = endpoint;
        var visited = new HashSet<string>(StringComparer.Ordinal) { endpoint.ComponentId };
        var traversed = 0;
        while (current.Role != TopologyPresetComponentRole.MeshRouter)
        {
            var nextId = current.ParentComponentId;
            if (traversed++ >= components.Count || string.IsNullOrWhiteSpace(nextId) || !visited.Add(nextId))
            {
                var code = string.IsNullOrWhiteSpace(nextId)
                    ? Phase8APathCatalogIssueCodes.UnsupportedTypedPath
                    : Phase8APathCatalogIssueCodes.HierarchyCycle;
                issues.Add(Issue(code, "$.paths[" + pathId + "]", "MoT activation parent hierarchy is incomplete or cyclic."));
                return null;
            }

            if (!TryUniqueComponent(components, nextId, out var parent) ||
                !IsValidActivationParent(current, parent!) ||
                !SameClusterCoordinate(endpoint, parent!) ||
                parent!.ChildComponentIds.Count(id => string.Equals(id, current.ComponentId, StringComparison.Ordinal)) != 1)
            {
                issues.Add(Issue(Phase8APathCatalogIssueCodes.UnsupportedTypedPath, "$.paths[" + pathId + "]", "MoT activation hierarchy requires PE to TreeRouter, TreeRouter to TreeRouter or MeshRouter, and reciprocal typed children only."));
                return null;
            }

            current = parent;
        }

        return current;
    }
    private static bool AppendActivationDescent(
        TopologyManifest manifest,
        IReadOnlyDictionary<string, TopologyManifestComponent[]> components,
        TopologyManifestComponent root,
        TopologyManifestComponent destination,
        ICollection<string> links,
        string pathId,
        List<Phase8APathCatalogIssue> issues)
    {
        var reverse = new List<TopologyManifestComponent> { destination };
        var current = destination;
        var visited = new HashSet<string>(StringComparer.Ordinal) { destination.ComponentId };
        var traversed = 0;
        while (!string.Equals(current.ComponentId, root.ComponentId, StringComparison.Ordinal))
        {
            var nextId = current.ParentComponentId;
            if (traversed++ >= manifest.Components.Count || string.IsNullOrWhiteSpace(nextId) || !visited.Add(nextId))
            {
                var code = string.IsNullOrWhiteSpace(nextId)
                    ? Phase8APathCatalogIssueCodes.UnsupportedTypedPath
                    : Phase8APathCatalogIssueCodes.HierarchyCycle;
                issues.Add(Issue(code, "$.paths[" + pathId + "]", "MoT activation descent parent hierarchy is incomplete or cyclic."));
                return false;
            }

            if (!TryUniqueComponent(components, nextId, out var parent) ||
                !IsValidActivationParent(current, parent!) ||
                !SameClusterCoordinate(destination, parent!))
            {
                issues.Add(Issue(Phase8APathCatalogIssueCodes.UnsupportedTypedPath, "$.paths[" + pathId + "]", "MoT activation descent requires only same-cluster TreeRouter parents terminating at MeshRouter."));
                return false;
            }

            if (parent!.ChildComponentIds.Count(id => string.Equals(id, current.ComponentId, StringComparison.Ordinal)) != 1)
            {
                issues.Add(Issue(Phase8APathCatalogIssueCodes.UnsupportedTypedPath, "$.paths[" + pathId + "]", "MoT typed parent and child metadata must be reciprocal and unique."));
                return false;
            }

            reverse.Add(parent);
            current = parent;
        }

        reverse.Reverse();
        for (var index = 1; index < reverse.Count; index++)
        {
            var parent = reverse[index - 1];
            var child = reverse[index];
            if (!IsValidActivationParent(child, parent))
            {
                issues.Add(Issue(Phase8APathCatalogIssueCodes.UnsupportedTypedPath, "$.paths[" + pathId + "]", "MoT activation descent contains a non-router internal node or a direct MeshRouter-to-PE shortcut."));
                return false;
            }

            var scope = parent.Role == TopologyPresetComponentRole.MeshRouter
                ? TopologyPresetLinkScope.Attachment
                : child.Role == TopologyPresetComponentRole.ProcessingElement ? TopologyPresetLinkScope.Leaf : TopologyPresetLinkScope.Tree;
            if (!AppendExactTypedLink(manifest, parent, child, TopologyPresetLinkRole.ActivationDistribution, scope, links, pathId, issues))
            {
                return false;
            }
        }

        return true;
    }
    private static bool AppendTypedMeshXy(
        TopologyManifest manifest,
        TopologyManifestComponent source,
        TopologyManifestComponent destination,
        ICollection<string> links,
        string pathId,
        List<Phase8APathCatalogIssue> issues)
    {
        if (source.MeshCoordinate is null || destination.MeshCoordinate is null)
        {
            issues.Add(Issue(Phase8APathCatalogIssueCodes.UnsupportedTypedPath, "$.paths[" + pathId + "]", "MoT mesh traversal requires explicit typed mesh coordinates."));
            return false;
        }

        var byCoordinate = manifest.Components
            .Where(component => component.Role == TopologyPresetComponentRole.MeshRouter && component.MeshCoordinate is not null)
            .GroupBy(component => (component.MeshCoordinate!.Row, component.MeshCoordinate.Column))
            .ToDictionary(group => group.Key, group => group.ToArray());
        var current = source;
        while (current.MeshCoordinate!.Column != destination.MeshCoordinate.Column)
        {
            var coordinate = (current.MeshCoordinate.Row, current.MeshCoordinate.Column + Math.Sign(destination.MeshCoordinate.Column - current.MeshCoordinate.Column));
            if (!TryUniqueCoordinate(byCoordinate, coordinate, out var next))
            {
                issues.Add(Issue(Phase8APathCatalogIssueCodes.UnsupportedTypedPath, "$.paths[" + pathId + "]", "The next typed MoT mesh coordinate must resolve exactly one MeshRouter."));
                return false;
            }

            if (!AppendExactTypedLink(manifest, current, next!, TopologyPresetLinkRole.MeshTransport, TopologyPresetLinkScope.Mesh, links, pathId, issues))
            {
                return false;
            }

            current = next!;
        }

        while (current.MeshCoordinate!.Row != destination.MeshCoordinate.Row)
        {
            var coordinate = (current.MeshCoordinate.Row + Math.Sign(destination.MeshCoordinate.Row - current.MeshCoordinate.Row), current.MeshCoordinate.Column);
            if (!TryUniqueCoordinate(byCoordinate, coordinate, out var next))
            {
                issues.Add(Issue(Phase8APathCatalogIssueCodes.UnsupportedTypedPath, "$.paths[" + pathId + "]", "The next typed MoT mesh coordinate must resolve exactly one MeshRouter."));
                return false;
            }

            if (!AppendExactTypedLink(manifest, current, next!, TopologyPresetLinkRole.MeshTransport, TopologyPresetLinkScope.Mesh, links, pathId, issues))
            {
                return false;
            }

            current = next!;
        }

        return true;
    }

    private static bool AppendExactTypedLink(
        TopologyManifest manifest,
        TopologyManifestComponent source,
        TopologyManifestComponent destination,
        TopologyPresetLinkRole role,
        TopologyPresetLinkScope scope,
        ICollection<string> links,
        string pathId,
        List<Phase8APathCatalogIssue> issues)
    {
        var matches = manifest.Links.Where(link =>
            link.Role == role && link.Scope == scope &&
            string.Equals(link.SourceComponentId, source.ComponentId, StringComparison.Ordinal) &&
            string.Equals(link.DestinationComponentId, destination.ComponentId, StringComparison.Ordinal)).ToArray();
        if (matches.Length != 1)
        {
            var code = matches.Length == 0
                ? Phase8APathCatalogIssueCodes.MissingLogicalLink
                : Phase8APathCatalogIssueCodes.DuplicateLogicalLink;
            issues.Add(Issue(code, "$.paths[" + pathId + "]", "Every typed MoT hop must resolve exactly one directed role/scope link."));
            return false;
        }

        links.Add(matches[0].LinkId);
        return true;
    }

    private static bool TryUniqueComponent(
        IReadOnlyDictionary<string, TopologyManifestComponent[]> components,
        string? componentId,
        out TopologyManifestComponent? component)
    {
        component = null;
        if (string.IsNullOrWhiteSpace(componentId) || !components.TryGetValue(componentId, out var matches) || matches.Length != 1)
        {
            return false;
        }

        component = matches[0];
        return true;
    }

    private static bool TryUniqueCoordinate(
        IReadOnlyDictionary<(int Row, int Column), TopologyManifestComponent[]> components,
        (int Row, int Column) coordinate,
        out TopologyManifestComponent? component)
    {
        component = null;
        if (!components.TryGetValue(coordinate, out var matches) || matches.Length != 1)
        {
            return false;
        }

        component = matches[0];
        return true;
    }

    private static bool HasTypedMoTLocation(TopologyManifestComponent component) =>
        component.ClusterIndex.HasValue && component.MeshCoordinate is not null;

    private static bool IsValidPartialSumParent(TopologyManifestComponent current, TopologyManifestComponent parent) =>
        current.Role switch
        {
            TopologyPresetComponentRole.ProcessingElement => parent.Role == TopologyPresetComponentRole.TreeReductionUnit,
            TopologyPresetComponentRole.TreeReductionUnit =>
                parent.Role is TopologyPresetComponentRole.TreeReductionUnit or TopologyPresetComponentRole.MeshRouter,
            _ => false
        };

    private static bool IsValidActivationParent(TopologyManifestComponent current, TopologyManifestComponent parent) =>
        current.Role switch
        {
            TopologyPresetComponentRole.ProcessingElement => parent.Role == TopologyPresetComponentRole.TreeRouter,
            TopologyPresetComponentRole.TreeRouter =>
                parent.Role is TopologyPresetComponentRole.TreeRouter or TopologyPresetComponentRole.MeshRouter,
            _ => false
        };

    private static bool SameClusterCoordinate(TopologyManifestComponent left, TopologyManifestComponent right) =>
        HasTypedMoTLocation(left) &&
        HasTypedMoTLocation(right) &&
        left.ClusterIndex == right.ClusterIndex &&
        left.MeshCoordinate == right.MeshCoordinate;

    private static string ComputePathHash(
        TopologyManifest manifest,
        Phase8AExplicitLogicalPath path,
        IEnumerable<Phase8APhysicalHopCostSnapshot> hops)
    {
        var node = new JsonObject
        {
            ["schemaVersion"] = Phase8ALogicalPathCatalog.CurrentSchemaVersion,
            ["algorithmId"] = path.AlgorithmId,
            ["topologyGraphHash"] = manifest.TopologyGraphHash,
            ["placementHash"] = manifest.PlacementHash,
            ["routeHash"] = manifest.RouteHash,
            ["topologyManifestHashAlgorithm"] = manifest.CanonicalHashAlgorithm,
            ["topologyManifestHash"] = manifest.CanonicalHash,
            ["pathId"] = path.PathId,
            ["sourceComponentId"] = path.SourceComponentId,
            ["destinationComponentId"] = path.DestinationComponentId,
            ["hops"] = HopArray(hops)
        };
        return Hash(node);
    }

    private static string ComputeCatalogHash(Phase8ALogicalPathCatalog catalog)
    {
        var entries = new JsonArray();
        foreach (var entry in catalog.Entries.OrderBy(item => item.PathId, StringComparer.Ordinal))
        {
            entries.Add(new JsonObject
            {
                ["pathId"] = entry.PathId,
                ["sourceComponentId"] = entry.SourceComponentId,
                ["destinationComponentId"] = entry.DestinationComponentId,
                ["algorithmId"] = entry.AlgorithmId,
                ["canonicalHashAlgorithm"] = entry.CanonicalHashAlgorithm,
                ["canonicalHash"] = entry.CanonicalHash,
                ["hops"] = HopArray(entry.Hops)
            });
        }

        return Hash(new JsonObject
        {
            ["schemaVersion"] = catalog.SchemaVersion,
            ["topologyGraphHashAlgorithm"] = catalog.TopologyGraphHashAlgorithm,
            ["topologyGraphHash"] = catalog.TopologyGraphHash,
            ["placementHashAlgorithm"] = catalog.PlacementHashAlgorithm,
            ["placementHash"] = catalog.PlacementHash,
            ["routeHashAlgorithm"] = catalog.RouteHashAlgorithm,
            ["routeHash"] = catalog.RouteHash,
            ["topologyManifestHashAlgorithm"] = catalog.TopologyManifestHashAlgorithm,
            ["topologyManifestHash"] = catalog.TopologyManifestHash,
            ["canonicalHashAlgorithm"] = catalog.CanonicalHashAlgorithm,
            ["entries"] = entries
        });
    }

    private static JsonArray HopArray(IEnumerable<Phase8APhysicalHopCostSnapshot> hops)
    {
        var result = new JsonArray();
        foreach (var hop in hops)
        {
            result.Add(new JsonObject
            {
                ["logicalLinkId"] = hop.LogicalLinkId,
                ["sourceComponentId"] = hop.SourceComponentId,
                ["sourcePortName"] = hop.SourcePortName,
                ["destinationComponentId"] = hop.DestinationComponentId,
                ["destinationPortName"] = hop.DestinationPortName,
                ["bandwidthBitsPerCycle"] = hop.BandwidthBitsPerCycle,
                ["latencyCycles"] = hop.LatencyCycles,
                ["routeType"] = hop.RouteType,
                ["medium"] = hop.Medium.ToString(),
                ["physicalLength"] = hop.PhysicalLength,
                ["routeGeometryLengthMicrometers"] = hop.RouteGeometryLengthMicrometers,
                ["bendCount"] = hop.BendCount,
                ["signalDomain"] = hop.SignalDomain,
                ["clockDomain"] = hop.ClockDomain,
                ["physicalRouteHash"] = hop.PhysicalRouteHash
            });
        }

        return result;
    }

    private static string ComputePhysicalRouteHash(PhysicalRoute route)
    {
        var points = new JsonArray();
        foreach (var point in route.Path)
        {
            points.Add(new JsonObject { ["x"] = point.X, ["y"] = point.Y });
        }

        return Hash(new JsonObject
        {
            ["linkId"] = route.LinkId,
            ["targetKind"] = route.TargetKind.ToString(),
            ["medium"] = route.Medium.ToString(),
            ["layerStack"] = route.LayerId.Stack,
            ["layerIndex"] = route.LayerId.Index,
            ["layerPurpose"] = route.LayerId.Purpose,
            ["pathUnit"] = route.PathUnit.ToString(),
            ["path"] = points
        });
    }

    private static string Hash(JsonNode node)
    {
        var canonical = ComponentExecutionJson.CanonicalizeJson(node.ToJsonString());
        return ComponentExecutionJson.ComputeSha256(canonical);
    }

    private static IReadOnlyList<Phase8APathCatalogIssue> StableIssues(IEnumerable<Phase8APathCatalogIssue> issues) =>
        Array.AsReadOnly(issues.OrderBy(issue => issue.Location, StringComparer.Ordinal).ThenBy(issue => issue.Code, StringComparer.Ordinal).ToArray());

    private static Phase8APathCatalogIssue Issue(string code, string location, string message) =>
        new(code, ValidationSeverity.Error, location, message);

    private static bool SamePoint(PhysicalPoint left, PhysicalPoint right) => Same(left.X, right.X) && Same(left.Y, right.Y);

    private static bool Same(double left, double right) => Math.Abs(left - right) < 0.000000001;
}
