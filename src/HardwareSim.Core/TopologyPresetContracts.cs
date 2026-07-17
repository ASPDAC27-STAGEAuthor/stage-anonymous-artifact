using System.Collections.ObjectModel;

namespace HardwareSim.Core;

/// <summary>Defines stable semantic roles for components emitted by topology preset builders.</summary>
public enum TopologyPresetComponentRole
{
    /// <summary>Identifies a workload processing endpoint.</summary>
    ProcessingElement,
    /// <summary>Identifies a router in a cluster-local distribution tree.</summary>
    TreeRouter,
    /// <summary>Identifies a cluster-local numerical reduction unit.</summary>
    TreeReductionUnit,
    /// <summary>Identifies a router attached to a topology root mesh.</summary>
    MeshRouter
}

/// <summary>Defines stable semantic roles for logical links emitted by topology preset builders.</summary>
public enum TopologyPresetLinkRole
{
    /// <summary>Carries activation or operand distribution traffic.</summary>
    ActivationDistribution,
    /// <summary>Carries partial sums toward an explicit reduction or collection target.</summary>
    PartialSumReturn,
    /// <summary>Carries traffic between neighboring root-mesh routers.</summary>
    MeshTransport
}

/// <summary>Defines the structural scope of one generated logical link.</summary>
public enum TopologyPresetLinkScope
{
    /// <summary>Connects a topology leaf and its immediate parent.</summary>
    Leaf,
    /// <summary>Connects two internal topology levels.</summary>
    Tree,
    /// <summary>Connects a cluster-local topology to its root-mesh attachment.</summary>
    Attachment,
    /// <summary>Connects neighboring root-mesh routers.</summary>
    Mesh
}

/// <summary>Represents one immutable row and column coordinate in a generated topology.</summary>
/// <param name="Row">Zero-based row coordinate.</param>
/// <param name="Column">Zero-based column coordinate.</param>
public sealed record TopologyPresetCoordinate(int Row, int Column);

/// <summary>Represents the immutable shared input accepted by topology preset builders.</summary>
public sealed class TopologyPresetRequest
{
    /// <summary>Initializes a topology preset request with explicit topology and physical-model parameters.</summary>
    /// <param name="topologyId">Stable topology preset identity.</param>
    /// <param name="meshRows">Number of root-mesh rows.</param>
    /// <param name="meshColumns">Number of root-mesh columns.</param>
    /// <param name="clusterSize">Number of processing elements per cluster.</param>
    /// <param name="wordBits">Number of bits carried by one topology word.</param>
    /// <param name="leafLaneCount">Lane count on each leaf edge.</param>
    /// <param name="leafLinkDistance">Typed distance of a level-zero leaf edge.</param>
    /// <param name="treeDistanceScale">Multiplicative typed-distance scale per tree level.</param>
    /// <param name="meshHopDistance">Typed distance between adjacent root-mesh routers.</param>
    /// <param name="routerLatencyCycles">Router latency attached to generated router components.</param>
    /// <param name="adderLatencyCycles">Adder latency attached to generated reduction components.</param>
    /// <param name="placementCellSizeMicrometers">Physical placement cell size used by the generated floorplan.</param>
    public TopologyPresetRequest(
        string topologyId,
        int meshRows,
        int meshColumns,
        int clusterSize,
        int wordBits = 32,
        int leafLaneCount = 1,
        double leafLinkDistance = 1.0,
        double treeDistanceScale = 1.4142135623730951,
        double meshHopDistance = 8.0,
        int routerLatencyCycles = 1,
        int adderLatencyCycles = 1,
        double placementCellSizeMicrometers = 100.0)
    {
        TopologyId = topologyId?.Trim() ?? "";
        MeshRows = meshRows;
        MeshColumns = meshColumns;
        ClusterSize = clusterSize;
        WordBits = wordBits;
        LeafLaneCount = leafLaneCount;
        LeafLinkDistance = leafLinkDistance;
        TreeDistanceScale = treeDistanceScale;
        MeshHopDistance = meshHopDistance;
        RouterLatencyCycles = routerLatencyCycles;
        AdderLatencyCycles = adderLatencyCycles;
        PlacementCellSizeMicrometers = placementCellSizeMicrometers;
    }

    /// <summary>Gets the stable topology preset identity.</summary>
    public string TopologyId { get; }
    /// <summary>Gets the number of root-mesh rows.</summary>
    public int MeshRows { get; }
    /// <summary>Gets the number of root-mesh columns.</summary>
    public int MeshColumns { get; }
    /// <summary>Gets the number of processing elements per cluster.</summary>
    public int ClusterSize { get; }
    /// <summary>Gets the number of bits carried by one topology word.</summary>
    public int WordBits { get; }
    /// <summary>Gets the lane count on each leaf edge.</summary>
    public int LeafLaneCount { get; }
    /// <summary>Gets the typed distance of a level-zero leaf edge.</summary>
    public double LeafLinkDistance { get; }
    /// <summary>Gets the multiplicative typed-distance scale per tree level.</summary>
    public double TreeDistanceScale { get; }
    /// <summary>Gets the typed distance between adjacent root-mesh routers.</summary>
    public double MeshHopDistance { get; }
    /// <summary>Gets the generated router latency in cycles.</summary>
    public int RouterLatencyCycles { get; }
    /// <summary>Gets the generated reduction adder latency in cycles.</summary>
    public int AdderLatencyCycles { get; }
    /// <summary>Gets the generated physical placement cell size in micrometers.</summary>
    public double PlacementCellSizeMicrometers { get; }
    /// <summary>Gets the checked root-mesh component count.</summary>
    public long ClusterCount => checked((long)MeshRows * MeshColumns);
    /// <summary>Gets the checked processing-element count implied by the shared request.</summary>
    public long TotalProcessingElements => checked(ClusterCount * ClusterSize);
}

/// <summary>Describes immutable provenance for one generated topology manifest.</summary>
public sealed class TopologyPresetProvenance
{
    /// <summary>Initializes typed topology build provenance.</summary>
    /// <param name="builderId">Stable builder identity.</param>
    /// <param name="builderVersion">Stable builder implementation contract version.</param>
    /// <param name="source">Stable provenance source category.</param>
    /// <param name="requestHashAlgorithm">Canonical request hash algorithm.</param>
    /// <param name="requestHash">Canonical request hash.</param>
    public TopologyPresetProvenance(
        string builderId,
        string builderVersion,
        string source,
        string requestHashAlgorithm,
        string requestHash)
    {
        BuilderId = builderId?.Trim() ?? "";
        BuilderVersion = builderVersion?.Trim() ?? "";
        Source = source?.Trim() ?? "";
        RequestHashAlgorithm = requestHashAlgorithm?.Trim() ?? "";
        RequestHash = requestHash?.Trim() ?? "";
    }

    /// <summary>Gets the stable builder identity.</summary>
    public string BuilderId { get; }
    /// <summary>Gets the builder implementation contract version.</summary>
    public string BuilderVersion { get; }
    /// <summary>Gets the stable provenance source category.</summary>
    public string Source { get; }
    /// <summary>Gets the canonical request hash algorithm.</summary>
    public string RequestHashAlgorithm { get; }
    /// <summary>Gets the canonical request hash.</summary>
    public string RequestHash { get; }
}

/// <summary>Describes one immutable component in a generated topology manifest.</summary>
public sealed class TopologyManifestComponent
{
    /// <summary>Initializes one immutable topology component descriptor.</summary>
    /// <param name="componentId">Stable generated component identity.</param>
    /// <param name="role">Typed component role.</param>
    /// <param name="coordinate">Explicit generated placement coordinate.</param>
    /// <param name="meshCoordinate">Optional root-mesh coordinate.</param>
    /// <param name="clusterIndex">Optional canonical row-major cluster index.</param>
    /// <param name="level">Topology level measured upward from processing leaves.</param>
    /// <param name="parentComponentId">Optional typed hierarchy parent identity.</param>
    /// <param name="childComponentIds">Typed hierarchy child identities.</param>
    /// <param name="attachmentComponentId">Optional paired or attached component identity.</param>
    public TopologyManifestComponent(
        string componentId,
        TopologyPresetComponentRole role,
        TopologyPresetCoordinate coordinate,
        TopologyPresetCoordinate? meshCoordinate,
        int? clusterIndex,
        int level,
        string? parentComponentId,
        IEnumerable<string>? childComponentIds,
        string? attachmentComponentId)
    {
        ComponentId = componentId?.Trim() ?? "";
        Role = role;
        Coordinate = coordinate ?? throw new ArgumentNullException(nameof(coordinate));
        MeshCoordinate = meshCoordinate;
        ClusterIndex = clusterIndex;
        Level = level;
        ParentComponentId = NormalizeOptional(parentComponentId);
        ChildComponentIds = Array.AsReadOnly((childComponentIds ?? [])
            .Select(id => id?.Trim() ?? "")
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray());
        AttachmentComponentId = NormalizeOptional(attachmentComponentId);
    }

    /// <summary>Gets the stable generated component identity.</summary>
    public string ComponentId { get; }
    /// <summary>Gets the typed component role.</summary>
    public TopologyPresetComponentRole Role { get; }
    /// <summary>Gets the explicit generated placement coordinate.</summary>
    public TopologyPresetCoordinate Coordinate { get; }
    /// <summary>Gets the optional root-mesh coordinate.</summary>
    public TopologyPresetCoordinate? MeshCoordinate { get; }
    /// <summary>Gets the optional canonical row-major cluster index.</summary>
    public int? ClusterIndex { get; }
    /// <summary>Gets the topology level measured upward from processing leaves.</summary>
    public int Level { get; }
    /// <summary>Gets the optional typed hierarchy parent identity.</summary>
    public string? ParentComponentId { get; }
    /// <summary>Gets a defensive immutable child identity collection.</summary>
    public IReadOnlyList<string> ChildComponentIds { get; }
    /// <summary>Gets the optional paired or attached component identity.</summary>
    public string? AttachmentComponentId { get; }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>Describes one immutable logical link in a generated topology manifest.</summary>
public sealed class TopologyManifestLink
{
    /// <summary>Initializes one immutable topology link descriptor.</summary>
    /// <param name="linkId">Stable logical link identity and physical route identity.</param>
    /// <param name="role">Typed traffic role.</param>
    /// <param name="scope">Typed structural scope.</param>
    /// <param name="sourceComponentId">Stable source component identity.</param>
    /// <param name="destinationComponentId">Stable destination component identity.</param>
    /// <param name="clusterIndex">Optional canonical row-major cluster index.</param>
    /// <param name="level">Tree level used for lane and typed-distance scaling.</param>
    /// <param name="laneCount">Exact generated lane count.</param>
    /// <param name="bandwidthBitsPerCycle">Exact generated bandwidth in bits per cycle.</param>
    /// <param name="distance">Exact typed topology distance.</param>
    public TopologyManifestLink(
        string linkId,
        TopologyPresetLinkRole role,
        TopologyPresetLinkScope scope,
        string sourceComponentId,
        string destinationComponentId,
        int? clusterIndex,
        int level,
        int laneCount,
        int bandwidthBitsPerCycle,
        double distance)
    {
        LinkId = linkId?.Trim() ?? "";
        Role = role;
        Scope = scope;
        SourceComponentId = sourceComponentId?.Trim() ?? "";
        DestinationComponentId = destinationComponentId?.Trim() ?? "";
        ClusterIndex = clusterIndex;
        Level = level;
        LaneCount = laneCount;
        BandwidthBitsPerCycle = bandwidthBitsPerCycle;
        Distance = distance;
    }

    /// <summary>Gets the stable logical link and physical route identity.</summary>
    public string LinkId { get; }
    /// <summary>Gets the typed traffic role.</summary>
    public TopologyPresetLinkRole Role { get; }
    /// <summary>Gets the typed structural scope.</summary>
    public TopologyPresetLinkScope Scope { get; }
    /// <summary>Gets the stable source component identity.</summary>
    public string SourceComponentId { get; }
    /// <summary>Gets the stable destination component identity.</summary>
    public string DestinationComponentId { get; }
    /// <summary>Gets the optional canonical row-major cluster index.</summary>
    public int? ClusterIndex { get; }
    /// <summary>Gets the tree level used for lane and typed-distance scaling.</summary>
    public int Level { get; }
    /// <summary>Gets the exact generated lane count.</summary>
    public int LaneCount { get; }
    /// <summary>Gets the exact generated bandwidth in bits per cycle.</summary>
    public int BandwidthBitsPerCycle { get; }
    /// <summary>Gets the exact typed topology distance.</summary>
    public double Distance { get; }
}

/// <summary>Represents one immutable canonical topology manifest.</summary>
public sealed class TopologyManifest
{
    internal TopologyManifest(
        string schemaVersion,
        TopologyPresetRequest request,
        IEnumerable<TopologyManifestComponent> components,
        IEnumerable<TopologyManifestLink> links,
        TopologyPresetProvenance provenance,
        string topologyGraphHashAlgorithm,
        string topologyGraphHash,
        string placementHashAlgorithm,
        string placementHash,
        string routeHashAlgorithm,
        string routeHash,
        string canonicalHashAlgorithm,
        string canonicalHash)
    {
        SchemaVersion = schemaVersion;
        Request = request;
        Components = Array.AsReadOnly(components.OrderBy(item => item.ComponentId, StringComparer.Ordinal).ToArray());
        Links = Array.AsReadOnly(links.OrderBy(item => item.LinkId, StringComparer.Ordinal).ToArray());
        Provenance = provenance;
        TopologyGraphHashAlgorithm = topologyGraphHashAlgorithm;
        TopologyGraphHash = topologyGraphHash;
        PlacementHashAlgorithm = placementHashAlgorithm;
        PlacementHash = placementHash;
        RouteHashAlgorithm = routeHashAlgorithm;
        RouteHash = routeHash;
        CanonicalHashAlgorithm = canonicalHashAlgorithm;
        CanonicalHash = canonicalHash;
    }

    /// <summary>Defines the current topology manifest schema version.</summary>
    public const string CurrentSchemaVersion = "1.0";
    /// <summary>Gets the topology manifest schema version.</summary>
    public string SchemaVersion { get; }
    /// <summary>Gets the immutable normalized topology preset request.</summary>
    public TopologyPresetRequest Request { get; }
    /// <summary>Gets immutable component descriptors in stable identity order.</summary>
    public IReadOnlyList<TopologyManifestComponent> Components { get; }
    /// <summary>Gets immutable logical link descriptors in stable identity order.</summary>
    public IReadOnlyList<TopologyManifestLink> Links { get; }
    /// <summary>Gets typed immutable build provenance.</summary>
    public TopologyPresetProvenance Provenance { get; }
    /// <summary>Gets the semantic topology graph hash algorithm.</summary>
    public string TopologyGraphHashAlgorithm { get; }
    /// <summary>Gets the semantic topology graph hash over components, ports, and logical links.</summary>
    public string TopologyGraphHash { get; }
    /// <summary>Gets the explicit physical placement hash algorithm.</summary>
    public string PlacementHashAlgorithm { get; }
    /// <summary>Gets the explicit physical placement hash.</summary>
    public string PlacementHash { get; }
    /// <summary>Gets the explicit physical route hash algorithm.</summary>
    public string RouteHashAlgorithm { get; }
    /// <summary>Gets the explicit physical route hash.</summary>
    public string RouteHash { get; }
    /// <summary>Gets the canonical topology manifest hash algorithm.</summary>
    public string CanonicalHashAlgorithm { get; }
    /// <summary>Gets the canonical topology manifest hash.</summary>
    public string CanonicalHash { get; }
}

/// <summary>Represents one structured topology build or persistence diagnostic.</summary>
/// <param name="Code">Stable machine-readable issue code.</param>
/// <param name="Severity">Typed diagnostic severity.</param>
/// <param name="Location">JSON-style contract location.</param>
/// <param name="Message">Human-readable diagnostic message.</param>
/// <param name="Suggestion">Optional repair suggestion.</param>
public sealed record TopologyBuildIssue(
    string Code,
    ValidationSeverity Severity,
    string Location,
    string Message,
    string? Suggestion = null);

/// <summary>Defines stable issue codes emitted by topology preset contracts and builders.</summary>
public static class TopologyBuildIssueCodes
{
    /// <summary>Identifies a missing topology request.</summary>
    public const string MissingRequest = "MissingTopologyPresetRequest";
    /// <summary>Identifies a request for a topology not implemented by the selected builder.</summary>
    public const string UnsupportedTopology = "UnsupportedTopologyPreset";
    /// <summary>Identifies an invalid topology preset builder registration.</summary>
    public const string InvalidBuilderRegistration = "InvalidTopologyPresetBuilderRegistration";
    /// <summary>Identifies duplicate stable topology ids in a builder registry.</summary>
    public const string DuplicateBuilderRegistration = "DuplicateTopologyPresetBuilderRegistration";
    /// <summary>Identifies invalid root-mesh dimensions.</summary>
    public const string InvalidMeshSize = "InvalidTopologyMeshSize";
    /// <summary>Identifies an invalid cluster size.</summary>
    public const string InvalidClusterSize = "InvalidTopologyClusterSize";
    /// <summary>Identifies a cluster size that is not a power of two where required.</summary>
    public const string NonPowerOfTwoClusterSize = "NonPowerOfTwoTopologyClusterSize";
    /// <summary>Identifies invalid word or lane configuration.</summary>
    public const string InvalidBandwidthConfiguration = "InvalidTopologyBandwidthConfiguration";
    /// <summary>Identifies invalid typed-distance configuration.</summary>
    public const string InvalidDistanceConfiguration = "InvalidTopologyDistanceConfiguration";
    /// <summary>Identifies invalid latency configuration.</summary>
    public const string InvalidLatencyConfiguration = "InvalidTopologyLatencyConfiguration";
    /// <summary>Identifies an inventory or bandwidth arithmetic overflow.</summary>
    public const string ArithmeticOverflow = "TopologyArithmeticOverflow";
    /// <summary>Identifies an invalid or incomplete generated explicit physical route.</summary>
    public const string InvalidPhysicalRoute = "InvalidTopologyPhysicalRoute";
    /// <summary>Identifies missing persisted typed topology metadata.</summary>
    public const string MissingManifest = "MissingTopologyManifest";
    /// <summary>Identifies malformed persisted typed topology metadata.</summary>
    public const string InvalidManifest = "InvalidTopologyManifest";
    /// <summary>Identifies an unsupported persisted topology manifest version.</summary>
    public const string UnsupportedManifestVersion = "UnsupportedTopologyManifestVersion";
    /// <summary>Identifies a canonical topology manifest hash mismatch.</summary>
    public const string ManifestHashMismatch = "TopologyManifestHashMismatch";
    /// <summary>Identifies a logical topology graph edit made after the manifest was generated.</summary>
    public const string TopologyGraphChanged = "TopologyGraphChanged";
    /// <summary>Identifies a physical placement edit made after the manifest was generated.</summary>
    public const string PlacementChanged = "TopologyPlacementChanged";
    /// <summary>Identifies a physical route edit made after the manifest was generated.</summary>
    public const string RouteChanged = "TopologyRouteChanged";
}

/// <summary>Represents a canonical JSON projection and SHA-256 digest.</summary>
/// <param name="Algorithm">Stable canonical projection and hash algorithm identity.</param>
/// <param name="Hash">Lowercase SHA-256 digest.</param>
/// <param name="CanonicalJson">Exact canonical JSON hashed to produce the digest.</param>
public sealed record TopologyCanonicalHash(string Algorithm, string Hash, string CanonicalJson);

/// <summary>Contains either one immutable topology manifest or structured persistence issues.</summary>
public sealed class TopologyManifestReadResult
{
    internal TopologyManifestReadResult(TopologyManifest? manifest, IEnumerable<TopologyBuildIssue> issues)
    {
        Manifest = manifest;
        Issues = Array.AsReadOnly(issues.ToArray());
    }

    /// <summary>Gets the immutable topology manifest when parsing succeeds.</summary>
    public TopologyManifest? Manifest { get; }
    /// <summary>Gets deterministic structured persistence issues.</summary>
    public IReadOnlyList<TopologyBuildIssue> Issues { get; }
    /// <summary>Gets whether a manifest was read without error diagnostics.</summary>
    public bool IsSuccess => Manifest is not null && Issues.All(issue => issue.Severity != ValidationSeverity.Error);
}

/// <summary>Contains a defensively frozen generated topology graph, manifest, and build diagnostics.</summary>
public sealed class TopologyBuildResult
{
    private readonly string _hardwareGraphJson;

    internal TopologyBuildResult(HardwareGraph graph, TopologyManifest? manifest, IEnumerable<TopologyBuildIssue> issues)
    {
        _hardwareGraphJson = HardwareGraphJson.Serialize(graph ?? throw new ArgumentNullException(nameof(graph)));
        TopologyManifest = manifest;
        Issues = Array.AsReadOnly(issues.ToArray());
    }

    /// <summary>Gets a fresh defensive graph copy suitable for explicit user editing.</summary>
    public HardwareGraph HardwareGraph => HardwareGraphJson.Deserialize(_hardwareGraphJson);
    /// <summary>Gets a fresh defensive macro-library copy; canonical presets currently return an empty collection.</summary>
    public IReadOnlyList<MacroComponent> MacroLibrary => HardwareGraph.Macros.AsReadOnly();
    /// <summary>Gets a fresh defensive physical placement copy when the build emitted placement.</summary>
    public PhysicalPlacement? PhysicalPlacement => HardwareGraph.Placement;
    /// <summary>Gets a fresh defensive physical routing copy when the build emitted routing.</summary>
    public PhysicalRouting? PhysicalRouting => HardwareGraph.Routing;
    /// <summary>Gets the immutable typed topology manifest when build validation succeeded.</summary>
    public TopologyManifest? TopologyManifest { get; }
    /// <summary>Gets immutable structured build diagnostics.</summary>
    public IReadOnlyList<TopologyBuildIssue> Issues { get; }
    /// <summary>Gets whether the builder emitted a manifest without error diagnostics.</summary>
    public bool IsSuccess => TopologyManifest is not null && Issues.All(issue => issue.Severity != ValidationSeverity.Error);
    /// <summary>Gets the exact frozen HardwareGraph JSON carried by this result.</summary>
    public string CanonicalGraphJson => _hardwareGraphJson;
}

/// <summary>Defines the shared production contract implemented by topology preset builders.</summary>
public interface ITopologyPresetBuilder
{
    /// <summary>Gets the stable topology preset identity accepted by this builder.</summary>
    string TopologyId { get; }
    /// <summary>Builds a defensively frozen topology result from an immutable request.</summary>
    /// <param name="request">Shared topology preset request.</param>
    /// <returns>The generated graph, physical artifacts, manifest, and structured issues.</returns>
    TopologyBuildResult Build(TopologyPresetRequest request);
}
