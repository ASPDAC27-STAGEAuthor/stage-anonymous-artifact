using System.Text.Json;
using System.Text.Json.Nodes;

namespace HardwareSim.Core;

/// <summary>Computes stable request, logical topology, placement, route, and aggregate manifest hashes.</summary>
public static class TopologyPresetCanonicalizer
{
    /// <summary>Identifies the canonical topology preset request hash projection.</summary>
    public const string RequestHashAlgorithm = "sha256/topology-preset-request-1.0/v1";
    /// <summary>Identifies the logical topology graph hash projection.</summary>
    public const string TopologyGraphHashAlgorithm = "sha256/topology-graph-1.0/v1";
    /// <summary>Identifies the explicit physical placement hash projection.</summary>
    public const string PlacementHashAlgorithm = "sha256/topology-placement-1.0/v1";
    /// <summary>Identifies the explicit physical route hash projection.</summary>
    public const string RouteHashAlgorithm = "sha256/topology-routes-1.0/v1";
    /// <summary>Identifies the aggregate typed topology manifest hash projection.</summary>
    public const string ManifestHashAlgorithm = "sha256/topology-manifest-1.0/v1";

    /// <summary>Computes the canonical hash of one immutable topology preset request.</summary>
    /// <param name="request">Topology preset request to hash.</param>
    /// <returns>The exact canonical projection and lowercase SHA-256 digest.</returns>
    public static TopologyCanonicalHash ComputeRequest(TopologyPresetRequest request)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        return Hash(RequestHashAlgorithm, RequestNode(request));
    }

    /// <summary>Computes a semantic hash over components, ports, logical links, and non-visual graph configuration.</summary>
    /// <param name="graph">Hardware graph to hash.</param>
    /// <returns>The exact canonical projection and lowercase SHA-256 digest.</returns>
    public static TopologyCanonicalHash ComputeTopologyGraph(HardwareGraph graph)
    {
        if (graph is null)
        {
            throw new ArgumentNullException(nameof(graph));
        }

        return Hash(TopologyGraphHashAlgorithm, TopologyGraphNode(graph));
    }

    /// <summary>Computes a semantic hash over explicit physical placement only.</summary>
    /// <param name="placement">Physical placement to hash, or null when absent.</param>
    /// <returns>The exact canonical projection and lowercase SHA-256 digest.</returns>
    public static TopologyCanonicalHash ComputePlacement(PhysicalPlacement? placement) =>
        Hash(PlacementHashAlgorithm, PlacementNode(placement));

    /// <summary>Computes a semantic hash over explicit physical RoutePath data only.</summary>
    /// <param name="routing">Physical routing to hash, or null when absent.</param>
    /// <returns>The exact canonical projection and lowercase SHA-256 digest.</returns>
    public static TopologyCanonicalHash ComputeRouting(PhysicalRouting? routing) =>
        Hash(RouteHashAlgorithm, RoutingNode(routing));

    /// <summary>Computes a route hash over explicit physical RoutePath data and logical-link physical route fields.</summary>
    /// <param name="graph">Hardware graph carrying logical links and explicit physical routing.</param>
    /// <returns>The exact canonical projection and lowercase SHA-256 digest.</returns>
    public static TopologyCanonicalHash ComputeRouting(HardwareGraph graph)
    {
        if (graph is null)
        {
            throw new ArgumentNullException(nameof(graph));
        }

        return Hash(RouteHashAlgorithm, RoutingNode(graph));
    }

    /// <summary>Computes the aggregate canonical hash of one immutable typed topology manifest.</summary>
    /// <param name="manifest">Typed topology manifest to hash.</param>
    /// <returns>The exact canonical projection and lowercase SHA-256 digest.</returns>
    public static TopologyCanonicalHash ComputeManifest(TopologyManifest manifest)
    {
        if (manifest is null)
        {
            throw new ArgumentNullException(nameof(manifest));
        }

        return Hash(ManifestHashAlgorithm, ManifestNode(manifest, includeCanonicalHash: false));
    }

    internal static TopologyManifest CreateManifest(
        TopologyPresetRequest request,
        HardwareGraph graph,
        IEnumerable<TopologyManifestComponent> components,
        IEnumerable<TopologyManifestLink> links,
        string builderId,
        string builderVersion,
        string source)
    {
        var requestHash = ComputeRequest(request);
        var topologyHash = ComputeTopologyGraph(graph);
        var placementHash = ComputePlacement(graph.Placement);
        var routeHash = ComputeRouting(graph);
        var provenance = new TopologyPresetProvenance(
            builderId,
            builderVersion,
            source,
            requestHash.Algorithm,
            requestHash.Hash);
        var unhashed = new TopologyManifest(
            TopologyManifest.CurrentSchemaVersion,
            request,
            components,
            links,
            provenance,
            topologyHash.Algorithm,
            topologyHash.Hash,
            placementHash.Algorithm,
            placementHash.Hash,
            routeHash.Algorithm,
            routeHash.Hash,
            ManifestHashAlgorithm,
            "");
        var aggregate = ComputeManifest(unhashed);
        return new TopologyManifest(
            unhashed.SchemaVersion,
            unhashed.Request,
            unhashed.Components,
            unhashed.Links,
            unhashed.Provenance,
            unhashed.TopologyGraphHashAlgorithm,
            unhashed.TopologyGraphHash,
            unhashed.PlacementHashAlgorithm,
            unhashed.PlacementHash,
            unhashed.RouteHashAlgorithm,
            unhashed.RouteHash,
            aggregate.Algorithm,
            aggregate.Hash);
    }

    internal static JsonObject ManifestNode(TopologyManifest manifest, bool includeCanonicalHash)
    {
        var root = new JsonObject
        {
            ["schemaVersion"] = manifest.SchemaVersion,
            ["request"] = RequestNode(manifest.Request),
            ["components"] = ArrayNode(manifest.Components, ManifestComponentNode),
            ["links"] = ArrayNode(manifest.Links, ManifestLinkNode),
            ["provenance"] = new JsonObject
            {
                ["builderId"] = manifest.Provenance.BuilderId,
                ["builderVersion"] = manifest.Provenance.BuilderVersion,
                ["source"] = manifest.Provenance.Source,
                ["requestHashAlgorithm"] = manifest.Provenance.RequestHashAlgorithm,
                ["requestHash"] = manifest.Provenance.RequestHash
            },
            ["topologyGraphHashAlgorithm"] = manifest.TopologyGraphHashAlgorithm,
            ["topologyGraphHash"] = manifest.TopologyGraphHash,
            ["placementHashAlgorithm"] = manifest.PlacementHashAlgorithm,
            ["placementHash"] = manifest.PlacementHash,
            ["routeHashAlgorithm"] = manifest.RouteHashAlgorithm,
            ["routeHash"] = manifest.RouteHash,
            ["canonicalHashAlgorithm"] = manifest.CanonicalHashAlgorithm
        };
        if (includeCanonicalHash)
        {
            root["canonicalHash"] = manifest.CanonicalHash;
        }

        return root;
    }

    private static TopologyCanonicalHash Hash(string algorithm, JsonNode node)
    {
        var canonicalJson = ComponentExecutionJson.CanonicalizeJson(node.ToJsonString());
        return new TopologyCanonicalHash(
            algorithm,
            ComponentExecutionJson.ComputeSha256(canonicalJson),
            canonicalJson);
    }

    private static JsonObject RequestNode(TopologyPresetRequest request) => new()
    {
        ["topologyId"] = request.TopologyId,
        ["meshRows"] = request.MeshRows,
        ["meshColumns"] = request.MeshColumns,
        ["clusterSize"] = request.ClusterSize,
        ["wordBits"] = request.WordBits,
        ["leafLaneCount"] = request.LeafLaneCount,
        ["leafLinkDistance"] = request.LeafLinkDistance,
        ["treeDistanceScale"] = request.TreeDistanceScale,
        ["meshHopDistance"] = request.MeshHopDistance,
        ["routerLatencyCycles"] = request.RouterLatencyCycles,
        ["adderLatencyCycles"] = request.AdderLatencyCycles,
        ["placementCellSizeMicrometers"] = request.PlacementCellSizeMicrometers
    };

    private static JsonObject TopologyGraphNode(HardwareGraph graph) => new()
    {
        ["schemaVersion"] = graph.SchemaVersion,
        ["components"] = ArrayNode(graph.Components.OrderBy(item => item.Id, StringComparer.Ordinal), ComponentNode),
        ["links"] = ArrayNode(graph.Links.OrderBy(item => item.Id, StringComparer.Ordinal), LinkNode),
        ["macros"] = ArrayNode(graph.Macros.OrderBy(item => item.Id, StringComparer.Ordinal), MacroNode),
        ["parameters"] = StringMap(graph.Parameters),
        ["extensionData"] = ExtensionMap(graph.ExtensionData, TopologyManifestJson.ExtensionPropertyName)
    };

    private static JsonObject ComponentNode(HardwareComponent component) => new()
    {
        ["id"] = component.Id,
        ["type"] = component.Type.ToString(),
        ["typeId"] = component.TypeId,
        ["templateRef"] = component.TemplateRef is null ? null : new JsonObject
        {
            ["templateId"] = component.TemplateRef.TemplateId,
            ["version"] = component.TemplateRef.Version,
            ["parameterOverrides"] = StringMap(component.TemplateRef.ParameterOverrides),
            ["compiledProfileHash"] = component.TemplateRef.CompiledProfileHash
        },
        ["ports"] = ArrayNode(component.Ports.OrderBy(item => item.Name, StringComparer.Ordinal), PortNode),
        ["parameters"] = StringMap(component.Parameters),
        ["modelRef"] = component.ModelRef,
        ["latencyModel"] = component.LatencyModel,
        ["energyModel"] = component.EnergyModel,
        ["areaModel"] = component.AreaModel,
        ["extensionData"] = ExtensionMap(component.ExtensionData)
    };

    private static JsonObject PortNode(HardwarePort port) => new()
    {
        ["name"] = port.Name,
        ["direction"] = port.Direction.ToString(),
        ["signalType"] = port.SignalType.ToString(),
        ["dataType"] = port.DataType.ToString(),
        ["precision"] = port.Precision.ToString(),
        ["protocol"] = port.Protocol.ToString(),
        ["bandwidthBitsPerCycle"] = port.BandwidthBitsPerCycle,
        ["latencyCycles"] = port.LatencyCycles,
        ["clockDomain"] = port.ClockDomain,
        ["required"] = port.Required,
        ["multiConnect"] = port.MultiConnect,
        ["extensionData"] = ExtensionMap(port.ExtensionData)
    };

    private static JsonObject LinkNode(HardwareLink link) => new()
    {
        ["id"] = link.Id,
        ["source"] = new JsonObject { ["componentId"] = link.Source.ComponentId, ["portName"] = link.Source.PortName },
        ["destination"] = new JsonObject { ["componentId"] = link.Destination.ComponentId, ["portName"] = link.Destination.PortName },
        ["modelRef"] = link.ModelRef,
        ["bandwidthBitsPerCycle"] = link.BandwidthBitsPerCycle,
        ["latencyCycles"] = link.LatencyCycles,
        ["energyPerBit"] = link.EnergyPerBit,
        ["parameters"] = StringMap(link.Parameters),
        ["extensionData"] = ExtensionMap(link.ExtensionData)
    };

    private static JsonObject MacroNode(MacroComponent macro) => new()
    {
        ["schemaVersion"] = macro.SchemaVersion,
        ["id"] = macro.Id,
        ["components"] = ArrayNode(macro.InternalComponents.OrderBy(item => item.Id, StringComparer.Ordinal), ComponentNode),
        ["links"] = ArrayNode(macro.InternalLinks.OrderBy(item => item.Id, StringComparer.Ordinal), LinkNode),
        ["externalPortMappings"] = PortRefMap(macro.ExternalPortMappings),
        ["extensionData"] = ExtensionMap(macro.ExtensionData)
    };

    private static JsonObject PlacementNode(PhysicalPlacement? placement)
    {
        if (placement is null)
        {
            return new JsonObject { ["placement"] = null };
        }

        var cells = new JsonObject();
        foreach (var pair in placement.ComponentCells.OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            cells[pair.Key] = new JsonObject
            {
                ["row"] = pair.Value.Row,
                ["column"] = pair.Value.Col,
                ["widthCells"] = pair.Value.WidthCells,
                ["heightCells"] = pair.Value.HeightCells,
                ["layer"] = pair.Value.Layer
            };
        }

        var positions = new JsonObject();
        foreach (var pair in placement.ComponentPositions.OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            positions[pair.Key] = PointNode(pair.Value);
        }

        return new JsonObject
        {
            ["schemaVersion"] = placement.SchemaVersion,
            ["rows"] = placement.Rows,
            ["columns"] = placement.Cols,
            ["cellWidthMicrometers"] = placement.CellWidthMicrometers,
            ["cellHeightMicrometers"] = placement.CellHeightMicrometers,
            ["origin"] = PointNode(placement.Origin),
            ["layer"] = placement.Layer,
            ["componentCells"] = cells,
            ["componentPositions"] = positions
        };
    }

    private static JsonObject RoutingNode(PhysicalRouting? routing)
    {
        if (routing is null)
        {
            return new JsonObject { ["routing"] = null };
        }

        return new JsonObject
        {
            ["schemaVersion"] = routing.SchemaVersion,
            ["routes"] = ArrayNode(routing.Routes.OrderBy(item => item.LinkId, StringComparer.Ordinal), route => new JsonObject
            {
                ["schemaVersion"] = route.SchemaVersion,
                ["linkId"] = route.LinkId,
                ["targetKind"] = route.TargetKind.ToString(),
                ["medium"] = route.Medium.ToString(),
                ["layerStack"] = route.LayerId.Stack,
                ["layerIndex"] = route.LayerId.Index,
                ["layerPurpose"] = route.LayerId.Purpose,
                ["pathUnit"] = route.PathUnit.ToString(),
                ["path"] = ArrayNode(route.Path, PointNode)
            })
        };
    }

    private static JsonObject RoutingNode(HardwareGraph graph) => new()
    {
        ["logicalLinkPhysicalRoutes"] = ArrayNode(
            graph.Links.OrderBy(item => item.Id, StringComparer.Ordinal),
            link => new JsonObject
            {
                ["linkId"] = link.Id,
                ["physicalLength"] = link.PhysicalLength,
                ["routeType"] = link.RouteType
            }),
        ["physicalRouting"] = RoutingNode(graph.Routing)
    };

    private static JsonObject ManifestComponentNode(TopologyManifestComponent component) => new()
    {
        ["componentId"] = component.ComponentId,
        ["role"] = component.Role.ToString(),
        ["coordinate"] = CoordinateNode(component.Coordinate),
        ["meshCoordinate"] = component.MeshCoordinate is null ? null : CoordinateNode(component.MeshCoordinate),
        ["clusterIndex"] = NullableIntNode(component.ClusterIndex),
        ["level"] = component.Level,
        ["parentComponentId"] = component.ParentComponentId,
        ["childComponentIds"] = ArrayNode(component.ChildComponentIds, id => JsonValue.Create(id)),
        ["attachmentComponentId"] = component.AttachmentComponentId
    };

    private static JsonObject ManifestLinkNode(TopologyManifestLink link) => new()
    {
        ["linkId"] = link.LinkId,
        ["role"] = link.Role.ToString(),
        ["scope"] = link.Scope.ToString(),
        ["sourceComponentId"] = link.SourceComponentId,
        ["destinationComponentId"] = link.DestinationComponentId,
        ["clusterIndex"] = NullableIntNode(link.ClusterIndex),
        ["level"] = link.Level,
        ["laneCount"] = link.LaneCount,
        ["bandwidthBitsPerCycle"] = link.BandwidthBitsPerCycle,
        ["distance"] = link.Distance
    };

    private static JsonObject CoordinateNode(TopologyPresetCoordinate coordinate) => new()
    {
        ["row"] = coordinate.Row,
        ["column"] = coordinate.Column
    };

    private static JsonObject PointNode(PhysicalPoint point) => new()
    {
        ["x"] = point.X,
        ["y"] = point.Y
    };

    private static JsonNode? NullableIntNode(int? value) =>
        value.HasValue ? JsonValue.Create(value.Value) : null;

    private static JsonObject StringMap(IReadOnlyDictionary<string, string>? values)
    {
        var node = new JsonObject();
        foreach (var pair in (values ?? new Dictionary<string, string>()).OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            node[pair.Key] = pair.Value;
        }

        return node;
    }

    private static JsonObject PortRefMap(IReadOnlyDictionary<string, PortRef>? values)
    {
        var node = new JsonObject();
        foreach (var pair in (values ?? new Dictionary<string, PortRef>()).OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            node[pair.Key] = new JsonObject
            {
                ["componentId"] = pair.Value.ComponentId,
                ["portName"] = pair.Value.PortName
            };
        }

        return node;
    }

    private static JsonObject ExtensionMap(IReadOnlyDictionary<string, JsonElement>? values, string? excludedKey = null)
    {
        var node = new JsonObject();
        foreach (var pair in (values ?? new Dictionary<string, JsonElement>()).OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            if (string.Equals(pair.Key, excludedKey, StringComparison.Ordinal))
            {
                continue;
            }

            node[pair.Key] = JsonNode.Parse(pair.Value.GetRawText());
        }

        return node;
    }

    private static JsonArray ArrayNode<T>(IEnumerable<T> values, Func<T, JsonNode?> selector)
    {
        var result = new JsonArray();
        foreach (var value in values)
        {
            result.Add(selector(value));
        }

        return result;
    }
}

/// <summary>Persists typed topology manifests inside HardwareGraph extension data without hidden runtime state.</summary>
public static class TopologyManifestJson
{
    /// <summary>Defines the HardwareGraph extension property carrying the typed topology manifest.</summary>
    public const string ExtensionPropertyName = "topology_manifest";

    /// <summary>Serializes one immutable typed topology manifest.</summary>
    /// <param name="manifest">Manifest to serialize.</param>
    /// <returns>Stable JSON including the aggregate canonical hash.</returns>
    public static string Serialize(TopologyManifest manifest)
    {
        if (manifest is null)
        {
            throw new ArgumentNullException(nameof(manifest));
        }

        return TopologyPresetCanonicalizer.ManifestNode(manifest, includeCanonicalHash: true)
            .ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>Returns a defensive graph copy with the typed manifest embedded as extension data.</summary>
    /// <param name="graph">Generated topology graph.</param>
    /// <param name="manifest">Typed topology manifest to persist.</param>
    /// <returns>A deep graph copy carrying the manifest.</returns>
    public static HardwareGraph AttachToGraph(HardwareGraph graph, TopologyManifest manifest)
    {
        if (graph is null)
        {
            throw new ArgumentNullException(nameof(graph));
        }

        if (manifest is null)
        {
            throw new ArgumentNullException(nameof(manifest));
        }

        var copy = HardwareGraphJson.Deserialize(HardwareGraphJson.Serialize(graph));
        using var document = JsonDocument.Parse(Serialize(manifest));
        copy.ExtensionData[ExtensionPropertyName] = document.RootElement.Clone();
        return copy;
    }

    /// <summary>Reads and validates a typed topology manifest from HardwareGraph extension data.</summary>
    /// <param name="graph">Loaded or edited hardware graph.</param>
    /// <returns>The typed manifest plus hash and staleness diagnostics.</returns>
    public static TopologyManifestReadResult ReadFromGraph(HardwareGraph graph)
    {
        if (graph is null)
        {
            throw new ArgumentNullException(nameof(graph));
        }

        if (!graph.ExtensionData.TryGetValue(ExtensionPropertyName, out var element))
        {
            return new TopologyManifestReadResult(null,
            [
                new TopologyBuildIssue(
                    TopologyBuildIssueCodes.MissingManifest,
                    ValidationSeverity.Error,
                    "$." + ExtensionPropertyName,
                    "HardwareGraph does not contain a typed topology manifest.")
            ]);
        }

        var parsed = Parse(element);
        if (parsed.Manifest is null)
        {
            return parsed;
        }

        var issues = parsed.Issues.ToList();
        AddStalenessIssue(
            issues,
            TopologyBuildIssueCodes.TopologyGraphChanged,
            "$.topologyGraphHash",
            parsed.Manifest.TopologyGraphHash,
            TopologyPresetCanonicalizer.ComputeTopologyGraph(graph).Hash,
            "Logical components, ports, or links changed after preset generation.");
        AddStalenessIssue(
            issues,
            TopologyBuildIssueCodes.PlacementChanged,
            "$.placementHash",
            parsed.Manifest.PlacementHash,
            TopologyPresetCanonicalizer.ComputePlacement(graph.Placement).Hash,
            "Physical placement changed after preset generation; geometry-dependent mapping is stale.");
        AddStalenessIssue(
            issues,
            TopologyBuildIssueCodes.RouteChanged,
            "$.routeHash",
            parsed.Manifest.RouteHash,
            TopologyPresetCanonicalizer.ComputeRouting(graph).Hash,
            "Physical RoutePath data changed after preset generation; route-dependent mapping is stale.");
        return new TopologyManifestReadResult(parsed.Manifest, issues);
    }

    /// <summary>Parses and validates standalone typed topology manifest JSON.</summary>
    /// <param name="json">Topology manifest JSON.</param>
    /// <returns>The typed manifest or structured parse and hash issues.</returns>
    public static TopologyManifestReadResult TryDeserialize(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Invalid("$", "Topology manifest JSON is required.");
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            return Parse(document.RootElement);
        }
        catch (JsonException exception)
        {
            return Invalid(exception.Path ?? "$", exception.Message);
        }
    }

    private static TopologyManifestReadResult Parse(JsonElement root)
    {
        try
        {
            if (root.ValueKind != JsonValueKind.Object)
            {
                return Invalid("$", "Topology manifest must be a JSON object.");
            }

            var schemaVersion = RequiredString(root, "schemaVersion");
            if (!string.Equals(schemaVersion, TopologyManifest.CurrentSchemaVersion, StringComparison.Ordinal))
            {
                return new TopologyManifestReadResult(null,
                [
                    new TopologyBuildIssue(
                        TopologyBuildIssueCodes.UnsupportedManifestVersion,
                        ValidationSeverity.Error,
                        "$.schemaVersion",
                        $"Topology manifest version '{schemaVersion}' is unsupported; expected {TopologyManifest.CurrentSchemaVersion}.")
                ]);
            }

            var requestElement = root.GetProperty("request");
            var request = new TopologyPresetRequest(
                RequiredString(requestElement, "topologyId"),
                RequiredInt(requestElement, "meshRows"),
                RequiredInt(requestElement, "meshColumns"),
                RequiredInt(requestElement, "clusterSize"),
                RequiredInt(requestElement, "wordBits"),
                RequiredInt(requestElement, "leafLaneCount"),
                RequiredDouble(requestElement, "leafLinkDistance"),
                RequiredDouble(requestElement, "treeDistanceScale"),
                RequiredDouble(requestElement, "meshHopDistance"),
                RequiredInt(requestElement, "routerLatencyCycles"),
                RequiredInt(requestElement, "adderLatencyCycles"),
                RequiredDouble(requestElement, "placementCellSizeMicrometers"));
            var components = root.GetProperty("components").EnumerateArray().Select(ParseComponent).ToArray();
            var links = root.GetProperty("links").EnumerateArray().Select(ParseLink).ToArray();
            var provenanceElement = root.GetProperty("provenance");
            var provenance = new TopologyPresetProvenance(
                RequiredString(provenanceElement, "builderId"),
                RequiredString(provenanceElement, "builderVersion"),
                RequiredString(provenanceElement, "source"),
                RequiredString(provenanceElement, "requestHashAlgorithm"),
                RequiredString(provenanceElement, "requestHash"));
            var manifest = new TopologyManifest(
                schemaVersion,
                request,
                components,
                links,
                provenance,
                RequiredString(root, "topologyGraphHashAlgorithm"),
                RequiredString(root, "topologyGraphHash"),
                RequiredString(root, "placementHashAlgorithm"),
                RequiredString(root, "placementHash"),
                RequiredString(root, "routeHashAlgorithm"),
                RequiredString(root, "routeHash"),
                RequiredString(root, "canonicalHashAlgorithm"),
                RequiredString(root, "canonicalHash"));
            var issues = ValidateTypedManifest(manifest);
            var expected = TopologyPresetCanonicalizer.ComputeManifest(manifest);
            if (!string.Equals(manifest.CanonicalHashAlgorithm, expected.Algorithm, StringComparison.Ordinal) ||
                !string.Equals(manifest.CanonicalHash, expected.Hash, StringComparison.Ordinal))
            {
                issues.Add(new TopologyBuildIssue(
                    TopologyBuildIssueCodes.ManifestHashMismatch,
                    ValidationSeverity.Error,
                    "$.canonicalHash",
                    "Persisted topology manifest canonical hash does not match its typed content."));
            }

            return new TopologyManifestReadResult(manifest, issues);
        }
        catch (Exception exception) when (exception is KeyNotFoundException or InvalidOperationException or FormatException or OverflowException)
        {
            return Invalid("$", exception.Message);
        }
    }

    private static TopologyManifestComponent ParseComponent(JsonElement element)
    {
        var coordinate = ParseCoordinate(element.GetProperty("coordinate"));
        TopologyPresetCoordinate? meshCoordinate = null;
        if (element.TryGetProperty("meshCoordinate", out var meshElement) && meshElement.ValueKind != JsonValueKind.Null)
        {
            meshCoordinate = ParseCoordinate(meshElement);
        }

        int? clusterIndex = element.TryGetProperty("clusterIndex", out var clusterElement) && clusterElement.ValueKind != JsonValueKind.Null
            ? clusterElement.GetInt32()
            : null;
        return new TopologyManifestComponent(
            RequiredString(element, "componentId"),
            RequiredEnum<TopologyPresetComponentRole>(element, "role"),
            coordinate,
            meshCoordinate,
            clusterIndex,
            RequiredInt(element, "level"),
            OptionalString(element, "parentComponentId"),
            element.GetProperty("childComponentIds").EnumerateArray().Select(item => item.GetString() ?? ""),
            OptionalString(element, "attachmentComponentId"));
    }

    private static TopologyManifestLink ParseLink(JsonElement element)
    {
        int? clusterIndex = element.TryGetProperty("clusterIndex", out var clusterElement) && clusterElement.ValueKind != JsonValueKind.Null
            ? clusterElement.GetInt32()
            : null;
        return new TopologyManifestLink(
            RequiredString(element, "linkId"),
            RequiredEnum<TopologyPresetLinkRole>(element, "role"),
            RequiredEnum<TopologyPresetLinkScope>(element, "scope"),
            RequiredString(element, "sourceComponentId"),
            RequiredString(element, "destinationComponentId"),
            clusterIndex,
            RequiredInt(element, "level"),
            RequiredInt(element, "laneCount"),
            RequiredInt(element, "bandwidthBitsPerCycle"),
            RequiredDouble(element, "distance"));
    }

    private static List<TopologyBuildIssue> ValidateTypedManifest(TopologyManifest manifest)
    {
        var issues = new List<TopologyBuildIssue>();
        var request = manifest.Request;
        var requestRangesValid = !string.IsNullOrWhiteSpace(request.TopologyId) &&
                                 request.MeshRows >= 0 && request.MeshColumns >= 0 && request.ClusterSize >= 0 &&
                                 request.WordBits > 0 && request.LeafLaneCount > 0 &&
                                 PositiveFinite(request.LeafLinkDistance) && PositiveFinite(request.TreeDistanceScale) &&
                                 PositiveFinite(request.MeshHopDistance) && PositiveFinite(request.PlacementCellSizeMicrometers) &&
                                 request.RouterLatencyCycles >= 0 && request.AdderLatencyCycles >= 0;
        try
        {
            _ = request.ClusterCount;
            _ = request.TotalProcessingElements;
            _ = checked(request.WordBits * request.LeafLaneCount);
        }
        catch (OverflowException)
        {
            requestRangesValid = false;
        }
        if (!requestRangesValid)
        {
            issues.Add(new TopologyBuildIssue(
                TopologyBuildIssueCodes.InvalidManifest,
                ValidationSeverity.Error,
                "$.request",
                "Topology request dimensions must be non-negative; word, lane, distance, scale, and placement values must be finite and positive; latency values must be non-negative; derived capacities must not overflow."));
        }

        if (manifest.Components.Count == 0 ||
            manifest.Components.Any(item => string.IsNullOrWhiteSpace(item.ComponentId)) ||
            manifest.Components.GroupBy(item => item.ComponentId, StringComparer.Ordinal).Any(group => group.Count() > 1))
        {
            issues.Add(new TopologyBuildIssue(
                TopologyBuildIssueCodes.InvalidManifest,
                ValidationSeverity.Error,
                "$.components",
                "Topology manifest component identities must be non-empty and unique."));
        }

        var componentIds = manifest.Components.Select(item => item.ComponentId).ToHashSet(StringComparer.Ordinal);
        if (manifest.Components.Any(item =>
                item.Coordinate.Row < 0 || item.Coordinate.Column < 0 ||
                item.MeshCoordinate is { Row: < 0 } or { Column: < 0 } ||
                item.ClusterIndex < 0 || item.Level < 0 ||
                item.ChildComponentIds.Any(childId =>
                    string.IsNullOrWhiteSpace(childId) || !componentIds.Contains(childId)) ||
                item.ChildComponentIds.Distinct(StringComparer.Ordinal).Count() != item.ChildComponentIds.Count))
        {
            issues.Add(new TopologyBuildIssue(
                TopologyBuildIssueCodes.InvalidManifest,
                ValidationSeverity.Error,
                "$.components",
                "Topology component coordinates, cluster indexes, and levels must be non-negative; declared child identities must be unique and resolve exactly."));
        }

        if (manifest.Links.Any(item => string.IsNullOrWhiteSpace(item.LinkId)) ||
            manifest.Links.GroupBy(item => item.LinkId, StringComparer.Ordinal).Any(group => group.Count() > 1) ||
            manifest.Links.Any(item =>
                !componentIds.Contains(item.SourceComponentId) ||
                !componentIds.Contains(item.DestinationComponentId) ||
                item.ClusterIndex < 0 || item.Level < 0 || item.LaneCount <= 0 ||
                item.BandwidthBitsPerCycle <= 0 || !PositiveFinite(item.Distance)))
        {
            issues.Add(new TopologyBuildIssue(
                TopologyBuildIssueCodes.InvalidManifest,
                ValidationSeverity.Error,
                "$.links",
                "Topology links must have unique identities, resolved endpoints, non-negative cluster and level metadata, positive lanes and bandwidth, and finite positive distance."));
        }

        var requestHash = TopologyPresetCanonicalizer.ComputeRequest(manifest.Request);
        if (!string.Equals(manifest.Provenance.RequestHashAlgorithm, requestHash.Algorithm, StringComparison.Ordinal) ||
            !string.Equals(manifest.Provenance.RequestHash, requestHash.Hash, StringComparison.Ordinal))
        {
            issues.Add(new TopologyBuildIssue(
                TopologyBuildIssueCodes.ManifestHashMismatch,
                ValidationSeverity.Error,
                "$.provenance.requestHash",
                "Persisted topology request hash does not match the normalized request."));
        }

        return issues;
    }

    private static bool PositiveFinite(double value) => double.IsFinite(value) && value > 0;

    private static void AddStalenessIssue(
        List<TopologyBuildIssue> issues,
        string code,
        string location,
        string expected,
        string actual,
        string message)
    {
        if (!string.Equals(expected, actual, StringComparison.Ordinal))
        {
            issues.Add(new TopologyBuildIssue(code, ValidationSeverity.Warning, location, message));
        }
    }

    private static TopologyManifestReadResult Invalid(string location, string message) => new(null,
    [
        new TopologyBuildIssue(
            TopologyBuildIssueCodes.InvalidManifest,
            ValidationSeverity.Error,
            location,
            message)
    ]);

    private static TopologyPresetCoordinate ParseCoordinate(JsonElement element) => new(
        RequiredInt(element, "row"),
        RequiredInt(element, "column"));

    private static string RequiredString(JsonElement element, string propertyName)
    {
        var value = element.GetProperty(propertyName).GetString();
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new FormatException($"Property '{propertyName}' must be a non-empty string.");
        }

        return value.Trim();
    }

    private static string? OptionalString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind != JsonValueKind.Null
            ? value.GetString()
            : null;

    private static int RequiredInt(JsonElement element, string propertyName) => element.GetProperty(propertyName).GetInt32();
    private static double RequiredDouble(JsonElement element, string propertyName) => element.GetProperty(propertyName).GetDouble();

    private static T RequiredEnum<T>(JsonElement element, string propertyName) where T : struct, Enum
    {
        var raw = RequiredString(element, propertyName);
        if (!Enum.TryParse<T>(raw, ignoreCase: false, out var value) || !Enum.IsDefined(typeof(T), value))
        {
            throw new FormatException($"Property '{propertyName}' contains unsupported {typeof(T).Name} value '{raw}'.");
        }

        return value;
    }
}
