using System.Collections.ObjectModel;
using System.Text.Json.Nodes;

namespace HardwareSim.Core;

/// <summary>Represents one immutable ordered logical path through a typed topology manifest.</summary>
public sealed class TopologyLogicalPath
{
    /// <summary>Defines the current logical-path contract version.</summary>
    public const string CurrentSchemaVersion = "1.0";

    internal TopologyLogicalPath(
        string algorithmId,
        string sourceComponentId,
        string destinationComponentId,
        IEnumerable<string> directedLinkIds,
        string canonicalHashAlgorithm,
        string canonicalHash)
    {
        AlgorithmId = algorithmId;
        SourceComponentId = sourceComponentId;
        DestinationComponentId = destinationComponentId;
        DirectedLinkIds = new ReadOnlyCollection<string>(directedLinkIds.ToArray());
        CanonicalHashAlgorithm = canonicalHashAlgorithm;
        CanonicalHash = canonicalHash;
    }

    /// <summary>Gets the logical-path schema version.</summary>
    public string SchemaVersion => CurrentSchemaVersion;

    /// <summary>Gets the versioned path-planning algorithm identity.</summary>
    public string AlgorithmId { get; }

    /// <summary>Gets the typed source component identity.</summary>
    public string SourceComponentId { get; }

    /// <summary>Gets the typed destination component identity.</summary>
    public string DestinationComponentId { get; }

    /// <summary>Gets the directed logical link identities in exact traversal order.</summary>
    public IReadOnlyList<string> DirectedLinkIds { get; }

    /// <summary>Gets the versioned canonical logical-path hash algorithm.</summary>
    public string CanonicalHashAlgorithm { get; }

    /// <summary>Gets the lowercase canonical logical-path SHA-256 digest.</summary>
    public string CanonicalHash { get; }
}

/// <summary>Contains either one immutable logical path or deterministic structured diagnostics.</summary>
public sealed class TopologyLogicalPathPlanResult
{
    internal TopologyLogicalPathPlanResult(TopologyLogicalPath? path, IEnumerable<TopologyBuildIssue> issues)
    {
        Path = path;
        Issues = new ReadOnlyCollection<TopologyBuildIssue>(issues.ToArray());
    }

    /// <summary>Gets the planned logical path when planning succeeds.</summary>
    public TopologyLogicalPath? Path { get; }

    /// <summary>Gets deterministic typed path-planning diagnostics.</summary>
    public IReadOnlyList<TopologyBuildIssue> Issues { get; }

    /// <summary>Gets whether a path was produced without error diagnostics.</summary>
    public bool IsSuccess => Path is not null && Issues.All(issue => issue.Severity != ValidationSeverity.Error);
}

/// <summary>Plans deterministic logical XY paths exclusively from typed manifest coordinates and links.</summary>
public static class TopologyLogicalPathPlanner
{
    /// <summary>Identifies the deterministic column-before-row logical XY policy.</summary>
    public const string DeterministicXyAlgorithmId = "deterministic-logical-xy-column-then-row-v1";

    /// <summary>Identifies the canonical logical-path hash projection.</summary>
    public const string PathHashAlgorithm = "sha256/topology-logical-path-1.0/xy-column-then-row-v1";

    /// <summary>Plans a column-first then row logical path between typed mesh routers or their attached processing elements.</summary>
    /// <param name="manifest">Typed topology manifest containing endpoint attachments, router coordinates, and directed links.</param>
    /// <param name="sourceComponentId">Source MeshRouter or attached ProcessingElement identity.</param>
    /// <param name="destinationComponentId">Destination MeshRouter or attached ProcessingElement identity.</param>
    /// <returns>An immutable ordered logical path or structured diagnostics.</returns>
    public static TopologyLogicalPathPlanResult PlanDeterministicXy(
        TopologyManifest manifest,
        string sourceComponentId,
        string destinationComponentId)
    {
        if (manifest is null)
        {
            throw new ArgumentNullException(nameof(manifest));
        }

        var sourceId = sourceComponentId?.Trim() ?? "";
        var destinationId = destinationComponentId?.Trim() ?? "";
        var byId = manifest.Components
            .GroupBy(component => component.ComponentId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
        if (!TryResolveEndpoint(byId, sourceId, out var source, out var sourceIssue))
        {
            return new TopologyLogicalPathPlanResult(null, [sourceIssue!]);
        }

        if (!TryResolveEndpoint(byId, destinationId, out var destination, out var destinationIssue))
        {
            return new TopologyLogicalPathPlanResult(null, [destinationIssue!]);
        }

        if (string.Equals(sourceId, destinationId, StringComparison.Ordinal))
        {
            var localHash = ComputeHash(manifest, sourceId, destinationId, []);
            return new TopologyLogicalPathPlanResult(
                new TopologyLogicalPath(
                    DeterministicXyAlgorithmId,
                    sourceId,
                    destinationId,
                    [],
                    PathHashAlgorithm,
                    localHash),
                []);
        }

        var directedLinkIds = new List<string>();
        var sourceRouterResult = ResolveEndpointRouter(manifest, byId, source!, isSource: true, directedLinkIds);
        if (sourceRouterResult.Issue is not null)
        {
            return new TopologyLogicalPathPlanResult(null, [sourceRouterResult.Issue]);
        }

        var destinationRouterResult = ResolveEndpointRouter(manifest, byId, destination!, isSource: false, null);
        if (destinationRouterResult.Issue is not null)
        {
            return new TopologyLogicalPathPlanResult(null, [destinationRouterResult.Issue]);
        }

        var sourceRouter = sourceRouterResult.Router!;
        var destinationRouter = destinationRouterResult.Router!;
        var routers = manifest.Components
            .Where(component => component.Role == TopologyPresetComponentRole.MeshRouter)
            .ToArray();
        var byCoordinate = routers
            .Where(component => component.MeshCoordinate is not null)
            .GroupBy(component => (component.MeshCoordinate!.Row, component.MeshCoordinate.Column))
            .ToDictionary(group => group.Key, group => group.ToArray());
        if (byCoordinate.Any(pair => pair.Value.Length != 1))
        {
            return Failure(
                "TopologyLogicalPathCoordinateAmbiguous",
                "$.components",
                "Every typed mesh coordinate used for logical XY routing must identify exactly one MeshRouter.");
        }

        var current = sourceRouter;
        while (current.MeshCoordinate!.Column != destinationRouter.MeshCoordinate!.Column)
        {
            var nextColumn = current.MeshCoordinate.Column + Math.Sign(destinationRouter.MeshCoordinate.Column - current.MeshCoordinate.Column);
            var nextResult = AppendHop(
                manifest,
                byCoordinate,
                current,
                (current.MeshCoordinate.Row, nextColumn),
                directedLinkIds);
            if (nextResult.Issue is not null)
            {
                return new TopologyLogicalPathPlanResult(null, [nextResult.Issue]);
            }

            current = nextResult.Component!;
        }

        while (current.MeshCoordinate!.Row != destinationRouter.MeshCoordinate!.Row)
        {
            var nextRow = current.MeshCoordinate.Row + Math.Sign(destinationRouter.MeshCoordinate.Row - current.MeshCoordinate.Row);
            var nextResult = AppendHop(
                manifest,
                byCoordinate,
                current,
                (nextRow, current.MeshCoordinate.Column),
                directedLinkIds);
            if (nextResult.Issue is not null)
            {
                return new TopologyLogicalPathPlanResult(null, [nextResult.Issue]);
            }

            current = nextResult.Component!;
        }

        if (destination!.Role == TopologyPresetComponentRole.ProcessingElement)
        {
            var attachmentIssue = AppendAttachmentLink(
                manifest,
                destinationRouter,
                destination,
                TopologyPresetLinkRole.ActivationDistribution,
                directedLinkIds);
            if (attachmentIssue is not null)
            {
                return new TopologyLogicalPathPlanResult(null, [attachmentIssue]);
            }
        }

        var hash = ComputeHash(manifest, sourceId, destinationId, directedLinkIds);
        return new TopologyLogicalPathPlanResult(
            new TopologyLogicalPath(
                DeterministicXyAlgorithmId,
                sourceId,
                destinationId,
                directedLinkIds,
                PathHashAlgorithm,
                hash),
            []);
    }

    private static bool TryResolveEndpoint(
        IReadOnlyDictionary<string, TopologyManifestComponent[]> byId,
        string componentId,
        out TopologyManifestComponent? component,
        out TopologyBuildIssue? issue)
    {
        component = null;
        issue = null;
        if (!byId.TryGetValue(componentId, out var matches) || matches.Length != 1)
        {
            issue = Issue(
                "TopologyLogicalPathEndpointInvalid",
                "$.components",
                $"Logical XY path endpoint '{componentId}' must resolve exactly one typed component.");
            return false;
        }

        component = matches[0];
        if (component.Role is not TopologyPresetComponentRole.MeshRouter and not TopologyPresetComponentRole.ProcessingElement)
        {
            issue = Issue(
                "TopologyLogicalPathEndpointRoleUnsupported",
                "$.components",
                $"Logical XY path endpoint '{componentId}' must be a MeshRouter or an attached ProcessingElement.");
            component = null;
            return false;
        }

        return true;
    }

    private static (TopologyManifestComponent? Router, TopologyBuildIssue? Issue) ResolveEndpointRouter(
        TopologyManifest manifest,
        IReadOnlyDictionary<string, TopologyManifestComponent[]> byId,
        TopologyManifestComponent endpoint,
        bool isSource,
        ICollection<string>? directedLinkIds)
    {
        if (endpoint.Role == TopologyPresetComponentRole.MeshRouter)
        {
            if (endpoint.MeshCoordinate is null)
            {
                return (null, Issue(
                    "TopologyLogicalPathCoordinateMissing",
                    "$.components",
                    $"MeshRouter endpoint '{endpoint.ComponentId}' requires a typed mesh coordinate."));
            }

            return (endpoint, null);
        }

        var attachmentId = endpoint.AttachmentComponentId ?? "";
        if (!byId.TryGetValue(attachmentId, out var attachmentMatches) ||
            attachmentMatches.Length != 1 ||
            attachmentMatches[0].Role != TopologyPresetComponentRole.MeshRouter ||
            attachmentMatches[0].MeshCoordinate is null ||
            endpoint.MeshCoordinate is null ||
            attachmentMatches[0].MeshCoordinate != endpoint.MeshCoordinate)
        {
            return (null, Issue(
                "TopologyLogicalPathAttachmentInvalid",
                "$.components",
                $"ProcessingElement endpoint '{endpoint.ComponentId}' must name exactly one co-located typed MeshRouter attachment."));
        }

        var router = attachmentMatches[0];
        if (isSource)
        {
            var attachmentIssue = AppendAttachmentLink(
                manifest,
                endpoint,
                router,
                TopologyPresetLinkRole.PartialSumReturn,
                directedLinkIds!);
            if (attachmentIssue is not null)
            {
                return (null, attachmentIssue);
            }
        }

        return (router, null);
    }

    private static TopologyBuildIssue? AppendAttachmentLink(
        TopologyManifest manifest,
        TopologyManifestComponent source,
        TopologyManifestComponent destination,
        TopologyPresetLinkRole expectedRole,
        ICollection<string> directedLinkIds)
    {
        var matches = manifest.Links
            .Where(link =>
                link.Scope == TopologyPresetLinkScope.Attachment &&
                link.Role == expectedRole &&
                string.Equals(link.SourceComponentId, source.ComponentId, StringComparison.Ordinal) &&
                string.Equals(link.DestinationComponentId, destination.ComponentId, StringComparison.Ordinal))
            .ToArray();
        if (matches.Length != 1)
        {
            return Issue(
                "TopologyLogicalPathAttachmentLinkInvalid",
                "$.links",
                $"Typed attachment hop '{source.ComponentId}' to '{destination.ComponentId}' must resolve exactly one directed {expectedRole} link.");
        }

        directedLinkIds.Add(matches[0].LinkId);
        return null;
    }

    private static (TopologyManifestComponent? Component, TopologyBuildIssue? Issue) AppendHop(
        TopologyManifest manifest,
        IReadOnlyDictionary<(int Row, int Column), TopologyManifestComponent[]> byCoordinate,
        TopologyManifestComponent current,
        (int Row, int Column) nextCoordinate,
        ICollection<string> directedLinkIds)
    {
        if (!byCoordinate.TryGetValue(nextCoordinate, out var nextMatches) || nextMatches.Length != 1)
        {
            return (null, Issue(
                "TopologyLogicalPathCoordinateUnreachable",
                "$.components",
                $"Typed mesh coordinate ({nextCoordinate.Row},{nextCoordinate.Column}) does not resolve exactly one MeshRouter."));
        }

        var next = nextMatches[0];
        var links = manifest.Links
            .Where(link =>
                link.Role == TopologyPresetLinkRole.MeshTransport &&
                link.Scope == TopologyPresetLinkScope.Mesh &&
                string.Equals(link.SourceComponentId, current.ComponentId, StringComparison.Ordinal) &&
                string.Equals(link.DestinationComponentId, next.ComponentId, StringComparison.Ordinal))
            .ToArray();
        if (links.Length != 1)
        {
            return (null, Issue(
                "TopologyLogicalPathDirectedLinkInvalid",
                "$.links",
                $"Typed mesh hop '{current.ComponentId}' to '{next.ComponentId}' must resolve exactly one directed MeshTransport link."));
        }

        directedLinkIds.Add(links[0].LinkId);
        return (next, null);
    }

    private static string ComputeHash(
        TopologyManifest manifest,
        string sourceComponentId,
        string destinationComponentId,
        IEnumerable<string> directedLinkIds)
    {
        var links = new JsonArray();
        foreach (var linkId in directedLinkIds)
        {
            links.Add(linkId);
        }

        var node = new JsonObject
        {
            ["schemaVersion"] = TopologyLogicalPath.CurrentSchemaVersion,
            ["algorithmId"] = DeterministicXyAlgorithmId,
            ["topologyManifestHashAlgorithm"] = manifest.CanonicalHashAlgorithm,
            ["topologyManifestHash"] = manifest.CanonicalHash,
            ["sourceComponentId"] = sourceComponentId,
            ["destinationComponentId"] = destinationComponentId,
            ["directedLinkIds"] = links
        };
        var canonicalJson = ComponentExecutionJson.CanonicalizeJson(node.ToJsonString());
        return ComponentExecutionJson.ComputeSha256(canonicalJson);
    }

    private static TopologyLogicalPathPlanResult Failure(string code, string location, string message) =>
        new(null, [Issue(code, location, message)]);

    private static TopologyBuildIssue Issue(string code, string location, string message) =>
        new(code, ValidationSeverity.Error, location, message);
}
