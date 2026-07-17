using System.Collections.ObjectModel;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace HardwareSim.Core;

/// <summary>Defines stable Phase 8 optical route analysis issue codes.</summary>
public static class OpticalRouteIssueCodes
{
    /// <summary>Identifies an optical logical link that has no explicit Phase 6B route.</summary>
    public const string MissingExplicitRoute = "p8_optical_route_missing";
    /// <summary>Identifies a logical link that resolves to more than one explicit route.</summary>
    public const string AmbiguousExplicitRoute = "p8_optical_route_ambiguous";
    /// <summary>Identifies an explicit optical route whose logical link cannot be resolved.</summary>
    public const string MissingLogicalLink = "p8_optical_route_missing_logical_link";
    /// <summary>Identifies an optical-domain link whose explicit route is not an optical waveguide.</summary>
    public const string WrongMedium = "p8_optical_route_wrong_medium";
    /// <summary>Identifies an explicit optical route with fewer than two path points.</summary>
    public const string PathTooShort = "p8_optical_route_path_too_short";
    /// <summary>Identifies a zero-length optical route segment.</summary>
    public const string ZeroLengthSegment = "p8_optical_route_zero_length_segment";
    /// <summary>Identifies an optical route segment that is not horizontal or vertical.</summary>
    public const string NonManhattanSegment = "p8_optical_route_non_manhattan_segment";
    /// <summary>Identifies a 180-degree reversal instead of a valid 90-degree bend.</summary>
    public const string UTurn = "p8_optical_route_u_turn";
    /// <summary>Identifies a non-finite, negative, or unnamed device-loss term.</summary>
    public const string InvalidDeviceLoss = "p8_optical_route_invalid_device_loss";
}

/// <summary>Represents a typed device insertion-loss term supplied by plugin compilation.</summary>
/// <param name="Name">Stable loss contribution name, such as splitter or MRR insertion.</param>
/// <param name="Loss">Non-negative insertion loss.</param>
/// <param name="Provenance">Source and characterization status of the value.</param>
/// <param name="SourceId">Plugin component or characterized profile identifier.</param>
public sealed record OpticalDeviceLossContribution(
    string Name,
    Decibels Loss,
    OpticalQuantityProvenance Provenance,
    string SourceId = "");

/// <summary>Captures immutable geometry derived from one explicit Phase 6B optical route.</summary>
public sealed class OpticalRouteSnapshot
{
    /// <summary>Defines the current optical route snapshot schema.</summary>
    public const string CurrentSchemaVersion = "1.0";
    /// <summary>Gets the snapshot schema version.</summary>
    public string SchemaVersion { get; init; } = CurrentSchemaVersion;
    /// <summary>Gets the resolved logical link identifier.</summary>
    public string LinkId { get; init; } = "";
    /// <summary>Gets the structured routing layer id.</summary>
    public string LayerId { get; init; } = "";
    /// <summary>Gets the explicit route medium.</summary>
    public RoutingMedium Medium { get; init; } = RoutingMedium.OpticalWaveguide;
    /// <summary>Gets the unit of the frozen path coordinates.</summary>
    public PhysicalRoutePointUnit PathUnit { get; init; } = PhysicalRoutePointUnit.Micrometers;
    /// <summary>Gets the ordered explicit route path.</summary>
    public IReadOnlyList<PhysicalPoint> Path { get; init; } = [];
    /// <summary>Gets exact Manhattan path length in micrometers.</summary>
    public double LengthMicrometers { get; init; }
    /// <summary>Gets exact Manhattan path length in millimeters.</summary>
    public double LengthMillimeters { get; init; }
    /// <summary>Gets the number of valid 90-degree direction changes.</summary>
    public int NinetyDegreeBendCount { get; init; }
    /// <summary>Gets the number of unique same-layer optical path crossings.</summary>
    public int CrossingCount { get; init; }
    /// <summary>Gets the explicit coupler count; zero while RoutePath has no coupler marker.</summary>
    public int CouplerCount { get; init; }
    /// <summary>Gets the lowercase SHA-256 hash of canonical route identity and coordinates.</summary>
    public string RouteHash { get; init; } = "";
    /// <summary>Gets provenance for every route-derived quantity.</summary>
    public IReadOnlyDictionary<string, OpticalQuantityProvenance> Provenance { get; init; } =
        new ReadOnlyDictionary<string, OpticalQuantityProvenance>(new Dictionary<string, OpticalQuantityProvenance>());
}

/// <summary>Provides the typed route and loss profile consumed by Phase 8 optical runtime code.</summary>
public sealed class OpticalLinkRuntimeProfile
{
    /// <summary>Defines the current optical link runtime profile schema.</summary>
    public const string CurrentSchemaVersion = "1.0";
    /// <summary>Gets the profile schema version.</summary>
    public string SchemaVersion { get; init; } = CurrentSchemaVersion;
    /// <summary>Gets the immutable explicit-route geometry snapshot.</summary>
    public OpticalRouteSnapshot Route { get; init; } = new();
    /// <summary>Gets the selected typed waveguide material.</summary>
    public OpticalWaveguideMaterial WaveguideMaterial { get; init; } = OpticalWaveguideMaterial.SiliconNitride;
    /// <summary>Gets the propagation coefficient in decibels per millimeter.</summary>
    public double PropagationLossDbPerMillimeter { get; init; } = OpticalLossDefaults.SiliconNitrideDbPerMillimeter;
    /// <summary>Gets every route and device loss term in deterministic order.</summary>
    public IReadOnlyList<OpticalLossContribution> LossContributions { get; init; } = [];
    /// <summary>Gets the exact sum of all loss terms.</summary>
    public Decibels TotalLoss { get; init; }
}

/// <summary>Combines an exact Phase 6C route resource with one wavelength channel.</summary>
public sealed record WavelengthResourceKey
{
    /// <summary>Initializes one exact wavelength-scoped routing resource key.</summary>
    /// <param name="routeResource">Existing Phase 6C edge, direction, layer, and medium identity.</param>
    /// <param name="channelId">Stable non-empty wavelength channel identifier.</param>
    /// <param name="wavelength">Positive physical wavelength.</param>
    public WavelengthResourceKey(RouteResourceKey routeResource, string channelId, Nanometers wavelength)
    {
        RouteResource = routeResource ?? throw new ArgumentNullException(nameof(routeResource));
        if (string.IsNullOrWhiteSpace(channelId))
        {
            throw new ArgumentException("Wavelength channel id is required.", nameof(channelId));
        }

        ChannelId = channelId.Trim();
        Wavelength = wavelength;
    }

    /// <summary>Gets the underlying exact Phase 6C routing resource.</summary>
    public RouteResourceKey RouteResource { get; }
    /// <summary>Gets the stable wavelength channel id.</summary>
    public string ChannelId { get; }
    /// <summary>Gets the channel wavelength.</summary>
    public Nanometers Wavelength { get; }
    /// <summary>Gets a deterministic report and trace identifier.</summary>
    public string ResourceId =>
        $"{RouteResource.EdgeId};direction={RouteResource.Direction};layer={RouteResource.Layer};medium={RouteResource.Medium};channel={ChannelId};wavelength_nm={Wavelength.Value.ToString("R", CultureInfo.InvariantCulture)}";
}

/// <summary>Returns compiled optical link profiles and structured diagnostics without mutating input.</summary>
public sealed class OpticalRouteAnalysisResult
{
    internal OpticalRouteAnalysisResult(
        IReadOnlyDictionary<string, OpticalLinkRuntimeProfile> profilesByLinkId,
        IReadOnlyList<CompilationIssue> issues)
    {
        ProfilesByLinkId = profilesByLinkId;
        Issues = issues;
    }

    /// <summary>Gets compiled profiles keyed by logical link id.</summary>
    public IReadOnlyDictionary<string, OpticalLinkRuntimeProfile> ProfilesByLinkId { get; }
    /// <summary>Gets structured route and loss diagnostics.</summary>
    public IReadOnlyList<CompilationIssue> Issues { get; }
    /// <summary>Gets whether all required optical links produced profiles.</summary>
    public bool IsSuccess => Issues.All(issue => issue.Severity != ValidationSeverity.Error);

    /// <summary>Finds a compiled profile by case-insensitive logical link id.</summary>
    public OpticalLinkRuntimeProfile? FindProfile(string linkId) =>
        ProfilesByLinkId.TryGetValue(linkId, out var profile) ? profile : null;
}

/// <summary>Compiles exact RoutePath geometry into Phase 8 optical runtime profiles.</summary>
public static class Phase8OpticalRouteAnalyzer
{
    private const double CoordinateTolerance = 0.000000001;
    private const string FirstPartyOpticalTypeIdPrefix = "com.hardware-sim.first-party.optical.";

    /// <summary>Determines whether a link enters the strict Phase 8 route contract without legacy enum, signal-domain, or raw RouteType inference.</summary>
    public static bool RequiresOpticalRoute(HardwareGraph graph, HardwareLink link)
    {
        if (graph is null) throw new ArgumentNullException(nameof(graph));
        if (link is null) throw new ArgumentNullException(nameof(link));

        var hasExplicitOpticalRoute = graph.Routing?.Routes.Any(route =>
            route.TargetKind == PhysicalRouteTargetKind.LogicalLink &&
            route.Medium == RoutingMedium.OpticalWaveguide &&
            string.Equals(route.LinkId, link.Id, StringComparison.OrdinalIgnoreCase)) == true;
        if (hasExplicitOpticalRoute)
        {
            return true;
        }

        // Compatibility boundary: approved enum-only, SignalType-only, and raw RouteType optical
        // graphs keep their legacy runtime. Registry-created Phase 8 components persist explicit TypeId,
        // and only links attached to their optical-domain ports require a waveguide RoutePath.
        var sourcePort = graph.FindPort(link.Source);
        var destinationPort = graph.FindPort(link.Destination);
        var sourceComponent = graph.FindComponent(link.Source.ComponentId);
        var destinationComponent = graph.FindComponent(link.Destination.ComponentId);
        return IsExplicitFirstPartyOpticalComponent(sourceComponent) && sourcePort?.SignalType == SignalType.Optical ||
            IsExplicitFirstPartyOpticalComponent(destinationComponent) && destinationPort?.SignalType == SignalType.Optical;
    }

    /// <summary>Analyzes every optical-domain link and returns profiles ready for SimLinkDef.</summary>
    public static OpticalRouteAnalysisResult AnalyzeGraph(
        HardwareGraph graph,
        IReadOnlyDictionary<string, OpticalWaveguideMaterial>? materialsByLinkId = null,
        IReadOnlyDictionary<string, IReadOnlyList<OpticalDeviceLossContribution>>? deviceContributionsByLinkId = null)
    {
        if (graph is null) throw new ArgumentNullException(nameof(graph));

        var profiles = new Dictionary<string, OpticalLinkRuntimeProfile>(StringComparer.OrdinalIgnoreCase);
        var issues = new List<CompilationIssue>();
        var effectiveMaterials = materialsByLinkId ?? ResolveWaveguideMaterials(graph, issues);
        foreach (var route in (graph.Routing?.Routes ?? [])
                     .Where(route => route.TargetKind == PhysicalRouteTargetKind.LogicalLink && route.Medium == RoutingMedium.OpticalWaveguide)
                     .OrderBy(route => route.LinkId, StringComparer.Ordinal))
        {
            if (!graph.Links.Any(link => string.Equals(link.Id, route.LinkId, StringComparison.OrdinalIgnoreCase)))
            {
                issues.Add(Error(
                    OpticalRouteIssueCodes.MissingLogicalLink,
                    route.LinkId,
                    $"Explicit optical route '{route.LinkId}' does not resolve to a logical hardware link.",
                    "Bind the route to an existing logical link id."));
            }
        }

        foreach (var link in graph.Links
                     .Where(link => RequiresOpticalRoute(graph, link))
                     .OrderBy(link => link.Id, StringComparer.Ordinal))
        {
            var material = FindMaterial(effectiveMaterials, link.Id);
            var deviceContributions = FindValue(deviceContributionsByLinkId, link.Id) ?? [];
            var linkResult = AnalyzeLink(graph, link, material, deviceContributions);
            issues.AddRange(linkResult.Issues);
            var profile = linkResult.FindProfile(link.Id);
            if (profile is not null)
            {
                profiles[link.Id] = profile;
            }
        }

        return Result(profiles, issues);
    }

    /// <summary>Analyzes one optical link using only explicit geometry and typed model inputs.</summary>
    public static OpticalRouteAnalysisResult AnalyzeLink(
        HardwareGraph graph,
        HardwareLink link,
        OpticalWaveguideMaterial material = OpticalWaveguideMaterial.SiliconNitride,
        IReadOnlyList<OpticalDeviceLossContribution>? deviceContributions = null)
    {
        if (graph is null) throw new ArgumentNullException(nameof(graph));
        if (link is null) throw new ArgumentNullException(nameof(link));

        deviceContributions ??= [];
        var profiles = new Dictionary<string, OpticalLinkRuntimeProfile>(StringComparer.OrdinalIgnoreCase);
        var issues = new List<CompilationIssue>();
        var routes = (graph.Routing?.Routes ?? [])
            .Where(route =>
                route.TargetKind == PhysicalRouteTargetKind.LogicalLink &&
                string.Equals(route.LinkId, link.Id, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (routes.Count == 0)
        {
            issues.Add(Error(
                OpticalRouteIssueCodes.MissingExplicitRoute,
                link.Id,
                $"Optical link '{link.Id}' requires an explicit Phase 6B RoutePath; link length and bend parameters are not accepted as fallback.",
                "Author a LogicalLink PhysicalRoute with OpticalWaveguide medium and at least two path points."));
            return Result(profiles, issues);
        }

        if (routes.Count > 1)
        {
            issues.Add(Error(
                OpticalRouteIssueCodes.AmbiguousExplicitRoute,
                link.Id,
                $"Optical link '{link.Id}' resolves to {routes.Count} explicit logical routes; route-derived loss requires exactly one truth source.",
                "Keep exactly one LogicalLink PhysicalRoute for this link."));
            return Result(profiles, issues);
        }

        var route = routes[0];
        if (route.Medium != RoutingMedium.OpticalWaveguide)
        {
            issues.Add(Error(
                OpticalRouteIssueCodes.WrongMedium,
                link.Id,
                $"Optical link '{link.Id}' route medium is '{route.Medium}', not '{RoutingMedium.OpticalWaveguide}'.",
                "Set the explicit route medium to OpticalWaveguide."));
            return Result(profiles, issues);
        }

        var bendCount = ValidatePath(route, issues);
        ValidateDeviceContributions(link.Id, deviceContributions, issues);
        if (issues.Any(issue => issue.Severity == ValidationSeverity.Error))
        {
            return Result(profiles, issues);
        }

        var lengthMicrometers = PhysicalRouteMetrics.CalculateLengthMicrometers(route.Path);
        var lengthMillimeters = UnitSystem.UmToMm(lengthMicrometers);
        var crossingCount = CountCrossings(route, graph.Routing?.Routes ?? []);
        const int couplerCount = 0;
        var routeHash = ComputeRouteHash(route);
        var routeProvenance = new Dictionary<string, OpticalQuantityProvenance>(StringComparer.Ordinal)
        {
            ["length"] = new("RoutePathDerived", $"route_id={link.Id};path_unit=um;point_count={route.Path.Count};route_hash={routeHash}"),
            ["bend_count"] = new("RoutePathDerived", $"route_id={link.Id};definition=90_degree_direction_change;route_hash={routeHash}"),
            ["crossing_count"] = new("TopologyDerived", $"route_id={link.Id};definition=unique_same_layer_orthogonal_intersection;shared_endpoints=excluded;route_hash={routeHash}"),
            ["coupler_count"] = new("TopologyDerived", $"route_id={link.Id};count=0;reason=no_explicit_route_or_port_coupler_marker;route_hash={routeHash}"),
            ["route_hash"] = new("RoutePathDerived", "algorithm=sha256;canonical_schema=phase8-optical-route-v1")
        };
        var snapshot = new OpticalRouteSnapshot
        {
            LinkId = link.Id,
            LayerId = route.LayerId?.ToString() ?? route.Layer,
            Medium = route.Medium,
            PathUnit = route.PathUnit,
            Path = route.Path.Select(point => new PhysicalPoint(point.X, point.Y)).ToList().AsReadOnly(),
            LengthMicrometers = lengthMicrometers,
            LengthMillimeters = lengthMillimeters,
            NinetyDegreeBendCount = bendCount,
            CrossingCount = crossingCount,
            CouplerCount = couplerCount,
            RouteHash = routeHash,
            Provenance = new ReadOnlyDictionary<string, OpticalQuantityProvenance>(routeProvenance)
        };

        var typedDeviceContributions = deviceContributions
            .OrderBy(contribution => contribution.Name, StringComparer.Ordinal)
            .ThenBy(contribution => contribution.SourceId, StringComparer.Ordinal)
            .Select(contribution => new OpticalLossContribution(
                contribution.Name,
                contribution.Loss,
                contribution.Provenance,
                contribution.SourceId))
            .ToList();
        var budget = OpticalLossModel.Calculate(
            lengthMillimeters,
            material,
            bendCount,
            crossingCount,
            couplerCount,
            typedDeviceContributions);

        profiles[link.Id] = new OpticalLinkRuntimeProfile
        {
            Route = snapshot,
            WaveguideMaterial = material,
            PropagationLossDbPerMillimeter = OpticalLossDefaults.PropagationDbPerMillimeter(material),
            LossContributions = budget.Contributions,
            TotalLoss = budget.TotalLoss
        };
        return Result(profiles, issues);
    }

    private static int ValidatePath(PhysicalRoute route, List<CompilationIssue> issues)
    {
        if (route.Path.Count < 2)
        {
            issues.Add(Error(
                OpticalRouteIssueCodes.PathTooShort,
                route.LinkId,
                $"Optical route '{route.LinkId}' must contain at least two explicit path points.",
                "Add a complete ordered RoutePath; compiler length fallback is forbidden."));
            return 0;
        }

        var directions = new (int X, int Y)?[route.Path.Count - 1];
        for (var index = 1; index < route.Path.Count; index++)
        {
            var previous = route.Path[index - 1];
            var current = route.Path[index];
            var dx = current.X - previous.X;
            var dy = current.Y - previous.Y;
            if (Same(dx, 0) && Same(dy, 0))
            {
                issues.Add(Error(
                    OpticalRouteIssueCodes.ZeroLengthSegment,
                    route.LinkId,
                    $"Optical route '{route.LinkId}' segment {index - 1}->{index} has zero length.",
                    "Remove the duplicate consecutive route point."));
                continue;
            }

            if (!Same(dx, 0) && !Same(dy, 0))
            {
                issues.Add(Error(
                    OpticalRouteIssueCodes.NonManhattanSegment,
                    route.LinkId,
                    $"Optical route '{route.LinkId}' segment {index - 1}->{index} is not horizontal or vertical; non-90-degree geometry is unsupported.",
                    "Replace the segment with explicit Manhattan path points."));
                continue;
            }

            directions[index - 1] = (Math.Sign(dx), Math.Sign(dy));
        }

        var bendCount = 0;
        for (var index = 1; index < directions.Length; index++)
        {
            var previous = directions[index - 1];
            var current = directions[index];
            if (previous is null || current is null || previous.Value == current.Value)
            {
                continue;
            }

            if (previous.Value.X == -current.Value.X && previous.Value.Y == -current.Value.Y)
            {
                issues.Add(Error(
                    OpticalRouteIssueCodes.UTurn,
                    route.LinkId,
                    $"Optical route '{route.LinkId}' reverses direction by 180 degrees at path point {index}; only straight segments and explicit 90-degree bends are supported.",
                    "Replace the U-turn with a valid non-reversing Manhattan route."));
                continue;
            }

            bendCount++;
        }

        return bendCount;
    }

    private static void ValidateDeviceContributions(
        string linkId,
        IReadOnlyList<OpticalDeviceLossContribution> contributions,
        List<CompilationIssue> issues)
    {
        for (var index = 0; index < contributions.Count; index++)
        {
            var contribution = contributions[index];
            if (string.IsNullOrWhiteSpace(contribution.Name) || contribution.Loss.Value < 0)
            {
                issues.Add(Error(
                    OpticalRouteIssueCodes.InvalidDeviceLoss,
                    linkId,
                    $"Optical device loss contribution {index} for link '{linkId}' must have a stable name and finite non-negative dB value.",
                    "Supply a typed plugin/profile loss contribution with explicit provenance."));
            }
        }
    }

    private static int CountCrossings(PhysicalRoute target, IReadOnlyList<PhysicalRoute> routes)
    {
        var targetSegments = Segments(target.Path);
        var crossingKeys = new HashSet<string>(StringComparer.Ordinal);

        for (var first = 0; first < targetSegments.Count; first++)
        {
            for (var second = first + 2; second < targetSegments.Count; second++)
            {
                AddCrossing(targetSegments[first], targetSegments[second], crossingKeys);
            }
        }

        foreach (var other in routes
                     .Where(route =>
                         !ReferenceEquals(route, target) &&
                         route.Medium == RoutingMedium.OpticalWaveguide &&
                         string.Equals(Layer(route), Layer(target), StringComparison.OrdinalIgnoreCase))
                     .OrderBy(route => route.LinkId, StringComparer.Ordinal))
        {
            foreach (var targetSegment in targetSegments)
            {
                foreach (var otherSegment in Segments(other.Path))
                {
                    AddCrossing(targetSegment, otherSegment, crossingKeys);
                }
            }
        }

        return crossingKeys.Count;
    }

    private static void AddCrossing(
        RouteSegment first,
        RouteSegment second,
        HashSet<string> crossingKeys)
    {
        if (!TryOrthogonalIntersection(first, second, out var intersection) ||
            IsSegmentEndpoint(first, intersection) ||
            IsSegmentEndpoint(second, intersection))
        {
            return;
        }

        crossingKeys.Add($"{Format(intersection.X)},{Format(intersection.Y)}");
    }

    private static bool TryOrthogonalIntersection(RouteSegment first, RouteSegment second, out PhysicalPoint intersection)
    {
        intersection = new PhysicalPoint(0, 0);
        if (first.IsHorizontal == second.IsHorizontal)
        {
            return false;
        }

        var horizontal = first.IsHorizontal ? first : second;
        var vertical = first.IsHorizontal ? second : first;
        var x = vertical.Start.X;
        var y = horizontal.Start.Y;
        if (!Between(x, horizontal.Start.X, horizontal.End.X) || !Between(y, vertical.Start.Y, vertical.End.Y))
        {
            return false;
        }

        intersection = new PhysicalPoint(x, y);
        return true;
    }

    private static List<RouteSegment> Segments(IReadOnlyList<PhysicalPoint> path)
    {
        var segments = new List<RouteSegment>();
        for (var index = 1; index < path.Count; index++)
        {
            var start = path[index - 1];
            var end = path[index];
            var horizontal = Same(start.Y, end.Y) && !Same(start.X, end.X);
            var vertical = Same(start.X, end.X) && !Same(start.Y, end.Y);
            if (horizontal || vertical)
            {
                segments.Add(new RouteSegment(start, end, horizontal));
            }
        }

        return segments;
    }

    private static bool IsSegmentEndpoint(RouteSegment segment, PhysicalPoint point) =>
        SamePoint(segment.Start, point) || SamePoint(segment.End, point);

    private static bool IsExplicitFirstPartyOpticalComponent(HardwareComponent? component) =>
        ComponentTypeIds.Normalize(component?.TypeId).StartsWith(FirstPartyOpticalTypeIdPrefix, StringComparison.OrdinalIgnoreCase);

    private static string ComputeRouteHash(PhysicalRoute route)
    {
        var builder = new StringBuilder();
        builder.Append("schema=phase8-optical-route-v1\n");
        builder.Append("link_id=").Append(route.LinkId).Append('\n');
        builder.Append("target_kind=").Append(route.TargetKind).Append('\n');
        builder.Append("medium=").Append(route.Medium).Append('\n');
        builder.Append("layer=").Append(Layer(route)).Append('\n');
        builder.Append("path_unit=").Append(route.PathUnit).Append('\n');
        for (var index = 0; index < route.Path.Count; index++)
        {
            builder.Append("path[").Append(index).Append("]=")
                .Append(Format(route.Path[index].X)).Append(',')
                .Append(Format(route.Path[index].Y)).Append('\n');
        }

        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(builder.ToString()));
        return string.Concat(hash.Select(value => value.ToString("x2", CultureInfo.InvariantCulture)));
    }

    private static CompilationIssue Error(string code, string linkId, string message, string suggestion) => new(
        code,
        ValidationSeverity.Error,
        $"$.routing.routes[link_id='{linkId}']",
        message,
        linkId,
        suggestion);

    private static OpticalRouteAnalysisResult Result(
        Dictionary<string, OpticalLinkRuntimeProfile> profiles,
        List<CompilationIssue> issues) => new(
            new ReadOnlyDictionary<string, OpticalLinkRuntimeProfile>(profiles),
            issues.AsReadOnly());

    private static TValue? FindValue<TValue>(IReadOnlyDictionary<string, TValue>? values, string key) where TValue : class
    {
        if (values is null)
        {
            return null;
        }

        if (values.TryGetValue(key, out var exact))
        {
            return exact;
        }

        return values.FirstOrDefault(pair => string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase)).Value;
    }
    private static IReadOnlyDictionary<string, OpticalWaveguideMaterial> ResolveWaveguideMaterials(
        HardwareGraph graph,
        ICollection<CompilationIssue> issues)
    {
        var result = new Dictionary<string, OpticalWaveguideMaterial>(StringComparer.OrdinalIgnoreCase);
        foreach (var link in graph.Links
                     .Where(link => RequiresOpticalRoute(graph, link))
                     .OrderBy(link => link.Id, StringComparer.Ordinal))
        {
            var candidates = new[] { link.Source.ComponentId, link.Destination.ComponentId }
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(graph.FindComponent)
                .Where(component =>
                    component is not null &&
                    string.Equals(
                        ComponentTypeIds.Normalize(component.TypeId),
                        Phase8OpticalTypeIds.Link,
                        StringComparison.OrdinalIgnoreCase))
                .Cast<HardwareComponent>()
                .Where(component => component.Parameters.ContainsKey(Phase8OpticalParameterKeys.WaveguideMaterial))
                .Select(component => new
                {
                    Component = component,
                    Raw = component.Parameters[Phase8OpticalParameterKeys.WaveguideMaterial]
                })
                .ToList();

            var parsed = new List<(string ComponentId, OpticalWaveguideMaterial Material)>();
            foreach (var candidate in candidates)
            {
                if (!TryParseWaveguideMaterial(candidate.Raw, out var material))
                {
                    issues.Add(new CompilationIssue(
                        "P8OpticalWaveguideMaterialInvalid",
                        ValidationSeverity.Error,
                        $"$.components[{candidate.Component.Id}].parameters.{Phase8OpticalParameterKeys.WaveguideMaterial}",
                        $"Optical Link '{candidate.Component.Id}' has unsupported waveguide material '{candidate.Raw}'. Expected SiN or Si.",
                        candidate.Component.Id));
                    continue;
                }

                parsed.Add((candidate.Component.Id, material));
            }

            var distinct = parsed.Select(item => item.Material).Distinct().ToList();
            if (distinct.Count > 1)
            {
                issues.Add(new CompilationIssue(
                    "P8OpticalWaveguideMaterialConflict",
                    ValidationSeverity.Error,
                    $"$.links[{link.Id}]",
                    $"Optical route '{link.Id}' is adjacent to Optical Link components with conflicting waveguide materials: {string.Join(", ", parsed.Select(item => $"{item.ComponentId}={item.Material}").OrderBy(value => value, StringComparer.Ordinal))}.",
                    link.Id));
                continue;
            }

            if (distinct.Count == 1)
            {
                result[link.Id] = distinct[0];
            }
        }

        return new ReadOnlyDictionary<string, OpticalWaveguideMaterial>(result);
    }

    private static bool TryParseWaveguideMaterial(string? raw, out OpticalWaveguideMaterial material)
    {
        var normalized = (raw ?? "").Trim()
            .Replace("-", "", StringComparison.Ordinal)
            .Replace("_", "", StringComparison.Ordinal)
            .Replace(" ", "", StringComparison.Ordinal);
        if (string.Equals(normalized, "sin", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalized, "siliconnitride", StringComparison.OrdinalIgnoreCase))
        {
            material = OpticalWaveguideMaterial.SiliconNitride;
            return true;
        }
        if (string.Equals(normalized, "si", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalized, "silicon", StringComparison.OrdinalIgnoreCase))
        {
            material = OpticalWaveguideMaterial.Silicon;
            return true;
        }

        material = default;
        return false;
    }


    private static OpticalWaveguideMaterial FindMaterial(
        IReadOnlyDictionary<string, OpticalWaveguideMaterial>? materials,
        string linkId)
    {
        if (materials is null)
        {
            return OpticalWaveguideMaterial.SiliconNitride;
        }

        if (materials.TryGetValue(linkId, out var exact))
        {
            return exact;
        }

        foreach (var pair in materials)
        {
            if (string.Equals(pair.Key, linkId, StringComparison.OrdinalIgnoreCase))
            {
                return pair.Value;
            }
        }

        return OpticalWaveguideMaterial.SiliconNitride;
    }

    private static string Layer(PhysicalRoute route) => route.LayerId?.ToString() ?? route.Layer;

    private static bool Between(double value, double first, double second) =>
        value >= Math.Min(first, second) - CoordinateTolerance &&
        value <= Math.Max(first, second) + CoordinateTolerance;

    private static bool SamePoint(PhysicalPoint first, PhysicalPoint second) =>
        Same(first.X, second.X) && Same(first.Y, second.Y);

    private static bool Same(double first, double second) => Math.Abs(first - second) <= CoordinateTolerance;

    private static string Format(double value) =>
        Same(value, 0) ? "0" : value.ToString("R", CultureInfo.InvariantCulture);

    private sealed record RouteSegment(PhysicalPoint Start, PhysicalPoint End, bool IsHorizontal);
}
