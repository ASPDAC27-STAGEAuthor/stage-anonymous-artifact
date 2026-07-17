namespace HardwareSim.Core;

/// <summary>Represents copy paste result data exchanged by hardware design and simulation workflows.</summary>
public sealed class CopyPasteResult
{
    /// <summary>Gets the id map collection carried by the enclosing copy paste result contract.</summary>
    public Dictionary<string, string> IdMap { get; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Gets the new component ids collection carried by the enclosing copy paste result contract.</summary>
    public List<string> NewComponentIds { get; } = [];
    /// <summary>Gets the new link ids collection carried by the enclosing copy paste result contract.</summary>
    public List<string> NewLinkIds { get; } = [];
    /// <summary>Gets the new group ids created while remapping copied group membership.</summary>
    public List<string> NewGroupIds { get; } = [];
    /// <summary>Gets external link ids skipped because external links are opt-in.</summary>
    public List<string> SkippedExternalLinkIds { get; } = [];
    /// <summary>Gets copied link ids rejected by validation.</summary>
    public Dictionary<string, string> RejectedLinkReasons { get; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>Summarizes a macro creation and collapse operation.</summary>
/// <param name="MacroId">Provides the macro definition id.</param>
/// <param name="InstanceId">Provides the created macro instance component id.</param>
/// <param name="InternalComponentIds">Provides component ids captured inside the definition.</param>
/// <param name="ExternalPortNames">Provides instance port names mapped to internal ports.</param>
/// <param name="RewiredLinkIds">Provides top-level external links rewired through the instance ports.</param>
public sealed record MacroCreateResult(
    string MacroId,
    string InstanceId,
    IReadOnlyList<string> InternalComponentIds,
    IReadOnlyList<string> ExternalPortNames,
    IReadOnlyList<string> RewiredLinkIds);

/// <summary>Summarizes expanding a macro instance back into editable top-level components.</summary>
/// <param name="MacroId">Provides the referenced macro definition id.</param>
/// <param name="InstanceId">Provides the expanded macro instance id.</param>
/// <param name="RestoredComponentIds">Provides restored top-level component ids.</param>
/// <param name="RestoredLinkIds">Provides restored internal link ids.</param>
/// <param name="RewiredLinkIds">Provides external links rewired back to internal ports.</param>
public sealed record MacroExpandResult(
    string MacroId,
    string InstanceId,
    IReadOnlyList<string> RestoredComponentIds,
    IReadOnlyList<string> RestoredLinkIds,
    IReadOnlyList<string> RewiredLinkIds);

/// <summary>Represents validation issues for editable macro external port mappings.</summary>
/// <param name="IsValid">Indicates whether the mapping set can be used for compile expansion.</param>
/// <param name="Issues">Provides validation issue text suitable for editor display.</param>
public sealed record MacroMappingValidationResult(bool IsValid, IReadOnlyList<string> Issues);

/// <summary>Represents the reusable macro library JSON envelope.</summary>
public sealed class MacroLibrary
{
    /// <summary>Defines the canonical reusable macro library schema version.</summary>
    public const string CurrentSchemaVersion = "1.0";
    /// <summary>Gets or sets the schema version for this library envelope.</summary>
    [System.Text.Json.Serialization.JsonPropertyName("schema_version")]
    public string SchemaVersion { get; set; } = CurrentSchemaVersion;
    /// <summary>Gets or sets reusable system-level macro definitions.</summary>
    public List<MacroComponent> Macros { get; set; } = [];
    /// <summary>Gets or sets unknown JSON properties preserved for forward-compatible roundtrips.</summary>
    [System.Text.Json.Serialization.JsonExtensionData]
    public Dictionary<string, System.Text.Json.JsonElement> ExtensionData { get; set; } = new(StringComparer.Ordinal);
}
/// <summary>Provides stable default visual metadata for Phase 5 group rendering.</summary>
public static class VisualGroupDefaults
{
    /// <summary>Gets the default semi-transparent fill used by Unity group rectangles.</summary>
    public const string Fill = "rgba(56,132,255,0.14)";
    /// <summary>Gets the default dashed-border color used by Unity group rectangles.</summary>
    public const string Border = "rgba(125,190,255,0.86)";
    /// <summary>Gets the default border style used by Unity group rectangles.</summary>
    public const string BorderStyle = "dashed";
    /// <summary>Gets the stable visual-only collapse semantics marker.</summary>
    public const string CollapseSemantics = "visual_only";

    /// <summary>Creates a fresh metadata dictionary with the Phase 5 visual defaults.</summary>
    public static Dictionary<string, string> CreateMetadata() => new(StringComparer.OrdinalIgnoreCase)
    {
        ["fill"] = Fill,
        ["border"] = Border,
        ["border_style"] = BorderStyle,
        ["label_visible"] = "true",
        ["collapse_semantics"] = CollapseSemantics
    };

    /// <summary>Applies missing Phase 5 group visual defaults without overwriting explicit metadata.</summary>
    public static void ApplyTo(VisualGroup group)
    {
        group.VisualMetadata ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in CreateMetadata())
        {
            group.VisualMetadata.TryAdd(pair.Key, pair.Value);
        }
    }
}

/// <summary>Provides group macro tools operations for hardware design and simulation workflows.</summary>
public static class GroupMacroTools
{
    /// <summary>Creates group from the supplied inputs.</summary>
    public static VisualGroup CreateGroup(HardwareGraph graph, string groupId, string name, IEnumerable<string> componentIds)
    {
        var ids = componentIds.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        foreach (var id in ids)
        {
            if (graph.FindComponent(id) is null)
            {
                throw new InvalidOperationException($"Cannot group missing component '{id}'.");
            }
        }

        EnsureNoPartialGroupOverlap(graph.Groups, groupId, ids);
        var group = new VisualGroup { Id = groupId, Name = name, ComponentIds = ids, VisualMetadata = VisualGroupDefaults.CreateMetadata() };
        graph.Groups.Add(group);
        return group;
    }

    internal static void EnsureNoPartialGroupOverlap(IEnumerable<VisualGroup> groups, string groupId, IReadOnlyCollection<string> componentIds)
    {
        var newSet = NormalizedGroupSet(componentIds);
        if (newSet.Count == 0)
        {
            return;
        }

        foreach (var group in groups)
        {
            if (string.Equals(group.Id, groupId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (IsDuplicateGroupMembership(newSet, group.ComponentIds))
            {
                throw new InvalidOperationException(
                    $"Group '{groupId}' duplicates group '{group.Id}' with the same member set. Groups must be unique, disjoint, or fully nested.");
            }

            if (!IsPartialGroupOverlap(newSet, group.ComponentIds))
            {
                continue;
            }

            var overlap = GroupOverlapMembers(newSet, group.ComponentIds);
            throw new InvalidOperationException(
                $"Group '{groupId}' partially overlaps group '{group.Id}' on member(s): {string.Join(", ", overlap)}. Groups must be disjoint or fully nested.");
        }
    }

    internal static bool IsPartialGroupOverlap(IReadOnlyCollection<string> leftIds, IReadOnlyCollection<string> rightIds)
    {
        var left = NormalizedGroupSet(leftIds);
        var right = NormalizedGroupSet(rightIds);
        if (left.Count == 0 || right.Count == 0)
        {
            return false;
        }

        var overlapCount = left.Count(right.Contains);
        return overlapCount > 0 && overlapCount < left.Count && overlapCount < right.Count;
    }

    internal static bool IsDuplicateGroupMembership(IReadOnlyCollection<string> leftIds, IReadOnlyCollection<string> rightIds)
    {
        var left = NormalizedGroupSet(leftIds);
        var right = NormalizedGroupSet(rightIds);
        return left.Count > 0 && left.SetEquals(right);
    }

    internal static IReadOnlyList<string> GroupOverlapMembers(IReadOnlyCollection<string> leftIds, IReadOnlyCollection<string> rightIds)
    {
        var right = NormalizedGroupSet(rightIds);
        return NormalizedGroupSet(leftIds)
            .Where(right.Contains)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToList();
    }

    private static HashSet<string> NormalizedGroupSet(IEnumerable<string> ids) => ids
        .Where(id => !string.IsNullOrWhiteSpace(id))
        .Select(id => id.Trim())
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>Clones selected components and their internal links using a deterministic suffix and position offset.</summary>
    public static CopyPasteResult CopyPasteComponents(
        HardwareGraph graph,
        IEnumerable<string> componentIds,
        string suffix,
        int offsetX,
        int offsetY,
        bool includeExternalLinks = false)
    {
        var selected = componentIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var result = new CopyPasteResult();
        var sourceComponents = graph.Components
            .Where(component => selected.Contains(component.Id))
            .OrderBy(component => component.Id, StringComparer.Ordinal)
            .ToList();

        foreach (var component in sourceComponents)
        {
            var cloneId = UniqueComponentId(graph, component.Id + suffix);
            result.IdMap[component.Id] = cloneId;
            var clone = CloneComponent(component, cloneId, suffix, offsetX, offsetY, result.IdMap);
            graph.Components.Add(clone);
            result.NewComponentIds.Add(clone.Id);
            CopyPlacement(graph, component.Id, clone.Id, offsetX, offsetY);
        }

        foreach (var group in graph.Groups.ToList())
        {
            var copiedMembers = group.ComponentIds
                .Where(id => result.IdMap.ContainsKey(id))
                .Select(id => result.IdMap[id])
                .ToList();
            if (copiedMembers.Count == 0)
            {
                continue;
            }

            var newGroup = new VisualGroup
            {
                Id = UniqueGroupId(graph, group.Id + suffix),
                Name = group.Name + " Copy",
                ComponentIds = copiedMembers,
                Collapsed = group.Collapsed,
                VisualMetadata = new Dictionary<string, string>(group.VisualMetadata, StringComparer.OrdinalIgnoreCase)
            };
            VisualGroupDefaults.ApplyTo(newGroup);
            graph.Groups.Add(newGroup);
            result.NewGroupIds.Add(newGroup.Id);
        }

        var linksToConsider = graph.Links
            .Where(link => selected.Contains(link.Source.ComponentId) || selected.Contains(link.Destination.ComponentId))
            .OrderBy(link => link.Id, StringComparer.Ordinal)
            .ToList();
        foreach (var link in linksToConsider)
        {
            var sourceCopied = result.IdMap.TryGetValue(link.Source.ComponentId, out var newSourceId);
            var destinationCopied = result.IdMap.TryGetValue(link.Destination.ComponentId, out var newDestinationId);
            var internalLink = sourceCopied && destinationCopied;
            if (!internalLink && !includeExternalLinks)
            {
                result.SkippedExternalLinkIds.Add(link.Id);
                continue;
            }

            var clone = CloneLink(
                link,
                UniqueLinkId(graph, link.Id + suffix),
                new PortRef(sourceCopied ? newSourceId! : link.Source.ComponentId, link.Source.PortName),
                new PortRef(destinationCopied ? newDestinationId! : link.Destination.ComponentId, link.Destination.PortName),
                result.IdMap);
            var beforeErrors = ValidationErrorKeys(graph);
            graph.Links.Add(clone);
            var errors = ValidationErrorKeys(graph)
                .Where(error => !beforeErrors.Contains(error))
                .ToList();
            if (errors.Count > 0)
            {
                graph.Links.Remove(clone);
                result.RejectedLinkReasons[link.Id] = string.Join(" | ", errors);
                continue;
            }

            result.NewLinkIds.Add(clone.Id);
            if (internalLink)
            {
                CopyRoute(graph, link.Id, clone.Id, offsetX, offsetY);
            }
        }

        return result;
    }

    /// <summary>Creates a reusable macro definition from selected top-level components without modifying the top-level graph.</summary>
    public static MacroComponent CreateMacro(HardwareGraph graph, string macroId, string name, IEnumerable<string> componentIds)
    {
        if (graph.Macros.Any(macro => string.Equals(macro.Id, macroId, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"Macro id '{macroId}' already exists.");
        }

        var selected = NormalizeSelection(graph, componentIds);
        var macro = new MacroComponent
        {
            Id = macroId.Trim(),
            Name = string.IsNullOrWhiteSpace(name) ? macroId.Trim() : name.Trim()
        };
        var idMap = selected.ToDictionary(id => id, id => id, StringComparer.OrdinalIgnoreCase);
        macro.InternalComponents = graph.Components
            .Where(component => selected.Contains(component.Id))
            .OrderBy(component => component.Id, StringComparer.Ordinal)
            .Select(component => CloneComponentForMacro(component, component.Id, 0, 0, idMap))
            .ToList();
        macro.InternalLinks = graph.Links
            .Where(link => selected.Contains(link.Source.ComponentId) && selected.Contains(link.Destination.ComponentId))
            .OrderBy(link => link.Id, StringComparer.Ordinal)
            .Select(link => CloneLink(link, link.Id, link.Source, link.Destination, idMap))
            .ToList();
        macro.InternalGroups = CaptureInternalGroups(graph, selected, idMap);
        foreach (var pair in BuildExternalPortMappings(graph, selected))
        {
            macro.ExternalPortMappings[pair.Key] = pair.Value;
        }

        var validation = ValidateMacroMappings(macro);
        if (!validation.IsValid)
        {
            throw new InvalidOperationException($"Macro '{macro.Id}' has invalid external mappings: {string.Join("; ", validation.Issues)}");
        }

        graph.Macros.Add(macro);
        return macro;
    }

    /// <summary>Creates a macro definition and folds the selected top-level subgraph into one macro instance component.</summary>
    public static MacroCreateResult CollapseSelectionToMacro(
        HardwareGraph graph,
        string macroId,
        string name,
        IEnumerable<string> componentIds,
        string instanceId)
    {
        var selected = NormalizeSelection(graph, componentIds);
        var selectedComponents = graph.Components.Where(component => selected.Contains(component.Id)).ToList();
        var minX = selectedComponents.Min(component => component.Position.X);
        var minY = selectedComponents.Min(component => component.Position.Y);
        var macro = CreateMacro(graph, macroId, name, selected);
        var instanceComponentId = UniqueComponentId(graph, string.IsNullOrWhiteSpace(instanceId) ? macro.Id + "_instance" : instanceId.Trim());
        var instance = new HardwareComponent
        {
            Id = instanceComponentId,
            Name = macro.Name,
            Type = ComponentKind.Macro,
            Position = new GridPosition(minX, minY),
            Ports = CreateMacroInstancePorts(macro),
            Parameters = { ["macro_ref"] = macro.Id },
            VisualStyle = { ["macro_view"] = "collapsed_subgraph" }
        };
        var endpointToExternalPort = macro.ExternalPortMappings
            .ToDictionary(pair => EndpointKey(pair.Value), pair => pair.Key, StringComparer.OrdinalIgnoreCase);
        var removedInternalLinkIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var rewiredLinkIds = new List<string>();
        foreach (var link in graph.Links.ToList())
        {
            var sourceSelected = selected.Contains(link.Source.ComponentId);
            var destinationSelected = selected.Contains(link.Destination.ComponentId);
            if (sourceSelected && destinationSelected)
            {
                graph.Links.Remove(link);
                removedInternalLinkIds.Add(link.Id);
                continue;
            }

            if (!sourceSelected && !destinationSelected)
            {
                continue;
            }

            if (sourceSelected)
            {
                link.Source = new PortRef(instanceComponentId, endpointToExternalPort[EndpointKey(link.Source)]);
            }
            else
            {
                link.Destination = new PortRef(instanceComponentId, endpointToExternalPort[EndpointKey(link.Destination)]);
            }

            rewiredLinkIds.Add(link.Id);
        }

        graph.Components.RemoveAll(component => selected.Contains(component.Id));
        graph.Components.Add(instance);
        ReplaceSelectedGroupMembersWithInstance(graph, selected, instanceComponentId);
        CollapsePlacement(graph, selected, instanceComponentId, minX, minY);
        graph.Routing?.Routes.RemoveAll(route => removedInternalLinkIds.Contains(route.LinkId));
        return new MacroCreateResult(
            macro.Id,
            instance.Id,
            macro.InternalComponents.Select(component => component.Id).ToList(),
            macro.ExternalPortMappings.Keys.OrderBy(key => key, StringComparer.OrdinalIgnoreCase).ToList(),
            rewiredLinkIds.OrderBy(id => id, StringComparer.Ordinal).ToList());
    }

    /// <summary>Expands a macro instance into editable top-level components while preserving the reusable definition.</summary>
    public static MacroExpandResult ExpandMacroInstance(HardwareGraph graph, string instanceId)
    {
        var instance = graph.FindComponent(instanceId) ?? throw new InvalidOperationException($"Component '{instanceId}' does not exist.");
        if (instance.Type != ComponentKind.Macro)
        {
            throw new InvalidOperationException($"Component '{instanceId}' is not a macro instance.");
        }

        if (!instance.Parameters.TryGetValue("macro_ref", out var macroRef) || string.IsNullOrWhiteSpace(macroRef))
        {
            throw new InvalidOperationException($"Macro instance '{instance.Id}' requires macro_ref.");
        }

        var definition = graph.Macros.SingleOrDefault(macro => string.Equals(macro.Id, macroRef, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Macro definition '{macroRef}' does not exist.");
        var validation = ValidateMacroMappings(definition);
        if (!validation.IsValid)
        {
            throw new InvalidOperationException($"Macro '{definition.Id}' has invalid external mappings: {string.Join("; ", validation.Issues)}");
        }

        var idMap = BuildMacroExpansionIdMap(graph, instance.Id, definition);
        var minInternalX = definition.InternalComponents.Min(component => component.Position.X);
        var minInternalY = definition.InternalComponents.Min(component => component.Position.Y);
        var offsetX = instance.Position.X - minInternalX;
        var offsetY = instance.Position.Y - minInternalY;
        var restoredComponents = definition.InternalComponents
            .OrderBy(component => component.Id, StringComparer.Ordinal)
            .Select(component => CloneComponentForMacro(component, idMap[component.Id], offsetX, offsetY, idMap))
            .ToList();
        var restoredLinks = new List<HardwareLink>();
        foreach (var link in definition.InternalLinks.OrderBy(link => link.Id, StringComparer.Ordinal))
        {
            restoredLinks.Add(CloneLink(
                link,
                UniqueLinkId(graph, link.Id),
                new PortRef(idMap[link.Source.ComponentId], link.Source.PortName),
                new PortRef(idMap[link.Destination.ComponentId], link.Destination.PortName),
                idMap));
        }

        var rewired = new List<string>();
        foreach (var link in graph.Links)
        {
            if (string.Equals(link.Source.ComponentId, instance.Id, StringComparison.OrdinalIgnoreCase))
            {
                var internalEndpoint = definition.ExternalPortMappings[link.Source.PortName];
                link.Source = new PortRef(idMap[internalEndpoint.ComponentId], internalEndpoint.PortName);
                rewired.Add(link.Id);
            }

            if (string.Equals(link.Destination.ComponentId, instance.Id, StringComparison.OrdinalIgnoreCase))
            {
                var internalEndpoint = definition.ExternalPortMappings[link.Destination.PortName];
                link.Destination = new PortRef(idMap[internalEndpoint.ComponentId], internalEndpoint.PortName);
                rewired.Add(link.Id);
            }
        }

        graph.Components.Remove(instance);
        graph.Components.AddRange(restoredComponents);
        graph.Links.AddRange(restoredLinks);
        ReplaceInstanceGroupMembersWithExpanded(
            graph,
            instance.Id,
            restoredComponents.Select(component => component.Id),
            definition.InternalGroups,
            idMap);
        ExpandPlacement(graph, instance.Id, restoredComponents);
        return new MacroExpandResult(
            definition.Id,
            instance.Id,
            restoredComponents.Select(component => component.Id).ToList(),
            restoredLinks.Select(link => link.Id).ToList(),
            rewired.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(id => id, StringComparer.Ordinal).ToList());
    }

    /// <summary>Validates that external macro port mappings point to real internal ports.</summary>
    public static MacroMappingValidationResult ValidateMacroMappings(MacroComponent macro)
    {
        var issues = new List<string>();
        if (!string.Equals(macro.SchemaVersion, MacroComponent.CurrentSchemaVersion, StringComparison.Ordinal))
        {
            issues.Add($"Unsupported macro schema_version '{macro.SchemaVersion}'.");
        }

        if (macro.InternalComponents.Count == 0)
        {
            issues.Add("Macro requires at least one internal component.");
        }

        var externalNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var mapping in macro.ExternalPortMappings.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(mapping.Key))
            {
                issues.Add("External port mapping name is required.");
                continue;
            }

            if (!externalNames.Add(mapping.Key))
            {
                issues.Add($"External port mapping '{mapping.Key}' is duplicated.");
            }

            var component = macro.InternalComponents.FirstOrDefault(candidate =>
                string.Equals(candidate.Id, mapping.Value.ComponentId, StringComparison.OrdinalIgnoreCase));
            if (component?.FindPort(mapping.Value.PortName) is null)
            {
                issues.Add($"External port '{mapping.Key}' references missing internal port '{mapping.Value.ComponentId}.{mapping.Value.PortName}'.");
            }
        }

        return new MacroMappingValidationResult(issues.Count == 0, issues);
    }

    /// <summary>Sets one external port mapping after validating the target internal port.</summary>
    public static MacroMappingValidationResult SetMacroExternalPortMapping(
        HardwareGraph graph,
        string macroId,
        string externalPortName,
        PortRef internalEndpoint)
    {
        var macro = graph.Macros.FirstOrDefault(candidate => string.Equals(candidate.Id, macroId, StringComparison.OrdinalIgnoreCase));
        if (macro is null)
        {
            return new MacroMappingValidationResult(false, [$"Macro '{macroId}' does not exist."]);
        }

        if (string.IsNullOrWhiteSpace(externalPortName))
        {
            return new MacroMappingValidationResult(false, ["External port name is required."]);
        }

        var component = macro.InternalComponents.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, internalEndpoint.ComponentId, StringComparison.OrdinalIgnoreCase));
        if (component?.FindPort(internalEndpoint.PortName) is null)
        {
            return new MacroMappingValidationResult(false, [$"External port '{externalPortName}' references missing internal port '{internalEndpoint.ComponentId}.{internalEndpoint.PortName}'."]);
        }

        macro.ExternalPortMappings[externalPortName.Trim()] = new PortRef(internalEndpoint.ComponentId, internalEndpoint.PortName);
        foreach (var instance in graph.Components.Where(componentInstance => componentInstance.Type == ComponentKind.Macro &&
                     componentInstance.Parameters.TryGetValue("macro_ref", out var macroRef) &&
                     string.Equals(macroRef, macro.Id, StringComparison.OrdinalIgnoreCase)))
        {
            if (instance.Ports.All(port => !string.Equals(port.Name, externalPortName, StringComparison.OrdinalIgnoreCase)))
            {
                instance.Ports.Add(ClonePortAs(component.FindPort(internalEndpoint.PortName)!, externalPortName.Trim()));
            }
        }

        return ValidateMacroMappings(macro);
    }

    /// <summary>Serializes the supplied macro definitions to the reusable Phase 5 macro library envelope.</summary>
    public static string SerializeMacroLibrary(IEnumerable<MacroComponent> macros)
    {
        var library = new MacroLibrary
        {
            Macros = macros
                .OrderBy(macro => macro.Id, StringComparer.Ordinal)
                .Select(CloneMacroDefinition)
                .ToList()
        };
        return System.Text.Json.JsonSerializer.Serialize(library, HardwareGraphJson.Options);
    }

    /// <summary>Deserializes a reusable macro library envelope and validates the minimum version contract.</summary>
    public static MacroLibrary DeserializeMacroLibrary(string json)
    {
        var library = System.Text.Json.JsonSerializer.Deserialize<MacroLibrary>(json, HardwareGraphJson.Options)
            ?? throw new HardwareGraphSerializationException([
                new HardwareGraphSerializationIssue("InvalidMacroLibrary", "error", "$", "Macro library JSON did not contain an object.")]);
        library.Macros ??= [];
        library.ExtensionData ??= new Dictionary<string, System.Text.Json.JsonElement>(StringComparer.Ordinal);
        if (!string.Equals(library.SchemaVersion, MacroLibrary.CurrentSchemaVersion, StringComparison.Ordinal))
        {
            throw new HardwareGraphSerializationException([
                new HardwareGraphSerializationIssue(
                    "UnsupportedMacroLibraryVersion",
                    "error",
                    "$.schema_version",
                    $"Macro library requires schema {MacroLibrary.CurrentSchemaVersion}, but received '{library.SchemaVersion}'.")]);
        }

        var graph = new HardwareGraph { Macros = library.Macros };
        HardwareGraphJson.NormalizeGraph(graph);
        library.Macros = graph.Macros;
        foreach (var macro in library.Macros)
        {
            var validation = ValidateMacroMappings(macro);
            if (!validation.IsValid)
            {
                throw new HardwareGraphSerializationException(validation.Issues
                    .Select(issue => new HardwareGraphSerializationIssue("InvalidMacroLibrary", "error", "$.macros", issue))
                    .ToArray());
            }
        }

        return library;
    }

    /// <summary>Imports macro definitions from a reusable macro library envelope.</summary>
    public static int ImportMacroLibrary(HardwareGraph graph, string json, bool overwriteExisting = false)
    {
        var library = DeserializeMacroLibrary(json);
        var imported = 0;
        foreach (var macro in library.Macros)
        {
            var existing = graph.Macros.FirstOrDefault(candidate => string.Equals(candidate.Id, macro.Id, StringComparison.OrdinalIgnoreCase));
            if (existing is not null && !overwriteExisting)
            {
                continue;
            }

            if (existing is not null)
            {
                graph.Macros.Remove(existing);
            }

            graph.Macros.Add(CloneMacroDefinition(macro));
            imported++;
        }

        return imported;
    }
    private static HashSet<string> ValidationErrorKeys(HardwareGraph graph) =>
        new HardwareGraphValidator()
            .Validate(graph)
            .Issues
            .Where(issue => issue.Severity == ValidationSeverity.Error)
            .Select(issue => $"{issue.Code}: {issue.Message}")
            .ToHashSet(StringComparer.Ordinal);
    private static HardwareComponent CloneComponent(
        HardwareComponent component,
        string cloneId,
        string suffix,
        int offsetX,
        int offsetY,
        IReadOnlyDictionary<string, string> idMap) => new()
    {
        Id = cloneId,
        Name = component.Name + " Copy",
        Type = component.Type,
        Position = new GridPosition(component.Position.X + offsetX, component.Position.Y + offsetY),
        Ports = component.Ports.Select(ClonePort).ToList(),
        Parameters = RemapDictionary(component.Parameters, idMap),
        ModelRef = component.ModelRef,
        LatencyModel = component.LatencyModel,
        EnergyModel = component.EnergyModel,
        AreaModel = component.AreaModel,
        VisualStyle = RemapDictionary(component.VisualStyle, idMap),
        InternalState = RemapDictionary(component.InternalState, idMap),
        ExtensionData = new Dictionary<string, System.Text.Json.JsonElement>(component.ExtensionData, StringComparer.Ordinal)
    };

    private static HardwareComponent CloneComponentForMacro(
        HardwareComponent component,
        string cloneId,
        int offsetX,
        int offsetY,
        IReadOnlyDictionary<string, string> idMap) => new()
    {
        Id = cloneId,
        Name = component.Name,
        Type = component.Type,
        Position = new GridPosition(component.Position.X + offsetX, component.Position.Y + offsetY),
        Ports = component.Ports.Select(ClonePort).ToList(),
        Parameters = RemapDictionary(component.Parameters, idMap),
        ModelRef = component.ModelRef,
        LatencyModel = component.LatencyModel,
        EnergyModel = component.EnergyModel,
        AreaModel = component.AreaModel,
        VisualStyle = RemapDictionary(component.VisualStyle, idMap),
        InternalState = RemapDictionary(component.InternalState, idMap),
        ExtensionData = new Dictionary<string, System.Text.Json.JsonElement>(component.ExtensionData, StringComparer.Ordinal)
    };

    private static MacroComponent CloneMacroDefinition(MacroComponent macro)
    {
        var idMap = macro.InternalComponents.ToDictionary(component => component.Id, component => component.Id, StringComparer.OrdinalIgnoreCase);
        return new MacroComponent
        {
            SchemaVersion = macro.SchemaVersion,
            Id = macro.Id,
            Name = macro.Name,
            InternalComponents = macro.InternalComponents.Select(component => CloneComponentForMacro(component, component.Id, 0, 0, idMap)).ToList(),
            InternalLinks = macro.InternalLinks.Select(link => CloneLink(link, link.Id, link.Source, link.Destination, idMap)).ToList(),
            InternalGroups = macro.InternalGroups.Select(group => CloneGroupForMacro(group, idMap)).ToList(),
            ExternalPortMappings = macro.ExternalPortMappings.ToDictionary(
                pair => pair.Key,
                pair => new PortRef(pair.Value.ComponentId, pair.Value.PortName),
                StringComparer.OrdinalIgnoreCase),
            ExtensionData = new Dictionary<string, System.Text.Json.JsonElement>(macro.ExtensionData, StringComparer.Ordinal)
        };
    }
    private static List<VisualGroup> CaptureInternalGroups(
        HardwareGraph graph,
        HashSet<string> selected,
        IReadOnlyDictionary<string, string> idMap) => graph.Groups
        .Where(group => group.ComponentIds.Count > 0 && group.ComponentIds.All(selected.Contains))
        .OrderBy(group => group.Id, StringComparer.Ordinal)
        .Select(group => CloneGroupForMacro(group, idMap))
        .ToList();

    private static VisualGroup CloneGroupForMacro(
        VisualGroup group,
        IReadOnlyDictionary<string, string> idMap,
        string? groupId = null)
    {
        var visualMetadata = group.VisualMetadata is null
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(group.VisualMetadata, StringComparer.OrdinalIgnoreCase);
        var extensionData = group.ExtensionData is null
            ? new Dictionary<string, System.Text.Json.JsonElement>(StringComparer.Ordinal)
            : new Dictionary<string, System.Text.Json.JsonElement>(group.ExtensionData, StringComparer.Ordinal);
        var clone = new VisualGroup
        {
            Id = string.IsNullOrWhiteSpace(groupId) ? group.Id : groupId,
            Name = group.Name,
            ComponentIds = group.ComponentIds
                .Where(id => idMap.ContainsKey(id))
                .Select(id => idMap[id])
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList(),
            Collapsed = group.Collapsed,
            VisualMetadata = visualMetadata,
            ExtensionData = extensionData
        };
        VisualGroupDefaults.ApplyTo(clone);
        return clone;
    }

    private static HardwarePort ClonePort(HardwarePort port) => new()
    {
        Name = port.Name,
        Direction = port.Direction,
        SignalType = port.SignalType,
        DataType = port.DataType,
        Precision = port.Precision,
        Protocol = port.Protocol,
        BandwidthBitsPerCycle = port.BandwidthBitsPerCycle,
        LatencyCycles = port.LatencyCycles,
        ClockDomain = port.ClockDomain,
        Required = port.Required,
        MultiConnect = port.MultiConnect,
        ExtensionData = new Dictionary<string, System.Text.Json.JsonElement>(port.ExtensionData, StringComparer.Ordinal)
    };

    private static HardwarePort ClonePortAs(HardwarePort port, string name) => new()
    {
        Name = name,
        Direction = port.Direction,
        SignalType = port.SignalType,
        DataType = port.DataType,
        Precision = port.Precision,
        Protocol = port.Protocol,
        BandwidthBitsPerCycle = port.BandwidthBitsPerCycle,
        LatencyCycles = port.LatencyCycles,
        ClockDomain = port.ClockDomain,
        Required = port.Required,
        MultiConnect = port.MultiConnect,
        ExtensionData = new Dictionary<string, System.Text.Json.JsonElement>(port.ExtensionData, StringComparer.Ordinal)
    };
    private static HardwareLink CloneLink(
        HardwareLink link,
        string cloneId,
        PortRef source,
        PortRef destination,
        IReadOnlyDictionary<string, string> idMap) => new()
    {
        Id = cloneId,
        Source = source,
        Destination = destination,
        ModelRef = link.ModelRef,
        BandwidthBitsPerCycle = link.BandwidthBitsPerCycle,
        LatencyCycles = link.LatencyCycles,
        EnergyPerBit = link.EnergyPerBit,
        PhysicalLength = link.PhysicalLength,
        RouteType = link.RouteType,
        Parameters = RemapDictionary(link.Parameters, idMap),
        ExtensionData = new Dictionary<string, System.Text.Json.JsonElement>(link.ExtensionData, StringComparer.Ordinal)
    };

    private static Dictionary<string, string> RemapDictionary(
        IDictionary<string, string>? values,
        IReadOnlyDictionary<string, string> idMap)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in values ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase))
        {
            result[pair.Key] = RemapReferenceValue(pair.Value, idMap);
        }

        return result;
    }

    private static string RemapReferenceValue(string value, IReadOnlyDictionary<string, string> idMap)
    {
        if (idMap.TryGetValue(value, out var remapped))
        {
            return remapped;
        }

        var parts = value.Split(',').Select(part => part.Trim()).ToArray();
        if (parts.Length > 1 && parts.All(part => idMap.ContainsKey(part) || !string.IsNullOrWhiteSpace(part)))
        {
            var changed = false;
            for (var index = 0; index < parts.Length; index++)
            {
                if (idMap.TryGetValue(parts[index], out var partRemap))
                {
                    parts[index] = partRemap;
                    changed = true;
                }
            }

            if (changed)
            {
                return string.Join(",", parts);
            }
        }

        return value;
    }

    private static void CopyPlacement(HardwareGraph graph, string sourceId, string cloneId, int offsetX, int offsetY)
    {
        var placement = graph.Placement;
        if (placement?.ComponentPositions.TryGetValue(sourceId, out var point) != true)
        {
            return;
        }

        var cell = placement.GridCellMicrometers;
        placement.ComponentPositions[cloneId] = new PhysicalPoint(point!.X + offsetX * cell, point.Y + offsetY * cell);
    }

    private static void CopyRoute(HardwareGraph graph, string sourceLinkId, string cloneLinkId, int offsetX, int offsetY)
    {
        var route = graph.Routing?.FindRoute(sourceLinkId);
        if (route is null || graph.Placement is null)
        {
            return;
        }

        var cell = graph.Placement.GridCellMicrometers;
        var clone = new PhysicalRoute
        {
            LinkId = cloneLinkId,
            SchemaVersion = route.SchemaVersion,
            RouteType = route.RouteType,
            TargetKind = route.TargetKind,
            Medium = route.Medium,
            Layer = route.Layer,
            LayerId = new RoutingLayerId { Stack = route.LayerId.Stack, Index = route.LayerId.Index, Purpose = route.LayerId.Purpose },
            PathUnit = route.PathUnit,
            Path = route.Path.Select(point => new PhysicalPoint(point.X + offsetX * cell, point.Y + offsetY * cell)).ToList(),
            ExtensionData = new Dictionary<string, System.Text.Json.JsonElement>(route.ExtensionData, StringComparer.Ordinal)
        };
        graph.Routing!.Routes.Add(clone);
    }

    private static HashSet<string> NormalizeSelection(HardwareGraph graph, IEnumerable<string> componentIds)
    {
        var selected = componentIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (selected.Count < 2)
        {
            throw new InvalidOperationException("Macro requires at least two selected non-macro components.");
        }

        foreach (var id in selected.OrderBy(id => id, StringComparer.Ordinal))
        {
            var component = graph.FindComponent(id);
            if (component is null)
            {
                throw new InvalidOperationException($"Cannot create macro from missing component '{id}'.");
            }

            if (component.Type == ComponentKind.Macro)
            {
                throw new InvalidOperationException($"Cannot create macro from macro instance '{id}'. Expand it before creating a new macro.");
            }
        }

        return selected;
    }

    private static Dictionary<string, PortRef> BuildExternalPortMappings(HardwareGraph graph, HashSet<string> selected)
    {
        var mappings = new Dictionary<string, PortRef>(StringComparer.OrdinalIgnoreCase);
        var endpointToName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var link in graph.Links.OrderBy(link => link.Id, StringComparer.Ordinal))
        {
            var sourceSelected = selected.Contains(link.Source.ComponentId);
            var destinationSelected = selected.Contains(link.Destination.ComponentId);
            if (sourceSelected == destinationSelected)
            {
                continue;
            }

            var endpoint = sourceSelected ? link.Source : link.Destination;
            var endpointKey = EndpointKey(endpoint);
            if (endpointToName.ContainsKey(endpointKey))
            {
                continue;
            }

            var externalName = UniqueExternalPortName(endpoint.PortName, endpoint.ComponentId, usedNames);
            endpointToName[endpointKey] = externalName;
            usedNames.Add(externalName);
            mappings[externalName] = new PortRef(endpoint.ComponentId, endpoint.PortName);
        }

        return mappings;
    }

    private static string UniqueExternalPortName(string portName, string componentId, HashSet<string> usedNames)
    {
        var baseName = string.IsNullOrWhiteSpace(portName) ? "port" : portName.Trim();
        if (!usedNames.Contains(baseName))
        {
            return baseName;
        }

        baseName = SafeIdentifier($"{componentId}_{portName}");
        var name = baseName;
        var index = 1;
        while (usedNames.Contains(name))
        {
            name = $"{baseName}_{index++}";
        }

        return name;
    }

    private static List<HardwarePort> CreateMacroInstancePorts(MacroComponent macro)
    {
        var ports = new List<HardwarePort>();
        foreach (var mapping in macro.ExternalPortMappings.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
        {
            var component = macro.InternalComponents.First(component => string.Equals(component.Id, mapping.Value.ComponentId, StringComparison.OrdinalIgnoreCase));
            var internalPort = component.FindPort(mapping.Value.PortName)!;
            ports.Add(ClonePortAs(internalPort, mapping.Key));
        }

        return ports;
    }

    private static void ReplaceSelectedGroupMembersWithInstance(HardwareGraph graph, HashSet<string> selected, string instanceId)
    {
        foreach (var group in graph.Groups.ToList())
        {
            var selectedMembers = group.ComponentIds.Count(id => selected.Contains(id));
            if (selectedMembers == 0)
            {
                continue;
            }

            if (selectedMembers == group.ComponentIds.Count)
            {
                graph.Groups.Remove(group);
                continue;
            }

            group.ComponentIds.RemoveAll(id => selected.Contains(id));
            if (!group.ComponentIds.Contains(instanceId, StringComparer.OrdinalIgnoreCase))
            {
                group.ComponentIds.Add(instanceId);
            }
        }
    }

    private static void ReplaceInstanceGroupMembersWithExpanded(
        HardwareGraph graph,
        string instanceId,
        IEnumerable<string> expandedIds,
        IReadOnlyList<VisualGroup> internalGroups,
        IReadOnlyDictionary<string, string> idMap)
    {
        var expanded = expandedIds.ToList();
        foreach (var group in graph.Groups)
        {
            var removed = group.ComponentIds.RemoveAll(id => string.Equals(id, instanceId, StringComparison.OrdinalIgnoreCase)) > 0;
            if (!removed)
            {
                continue;
            }

            foreach (var id in expanded)
            {
                if (!group.ComponentIds.Contains(id, StringComparer.OrdinalIgnoreCase))
                {
                    group.ComponentIds.Add(id);
                }
            }

            group.Collapsed = false;
            VisualGroupDefaults.ApplyTo(group);
            group.VisualMetadata["collapse_semantics"] = VisualGroupDefaults.CollapseSemantics;
        }

        foreach (var internalGroup in internalGroups.OrderBy(group => group.ComponentIds.Count).ThenBy(group => group.Id, StringComparer.Ordinal))
        {
            var restoredGroup = CloneGroupForMacro(internalGroup, idMap, UniqueGroupId(graph, internalGroup.Id));
            restoredGroup.Collapsed = false;
            VisualGroupDefaults.ApplyTo(restoredGroup);
            restoredGroup.VisualMetadata["collapse_semantics"] = VisualGroupDefaults.CollapseSemantics;
            if (restoredGroup.ComponentIds.Count == 0 ||
                graph.Groups.Any(group => IsDuplicateGroupMembership(group.ComponentIds, restoredGroup.ComponentIds)))
            {
                continue;
            }

            EnsureNoPartialGroupOverlap(graph.Groups, restoredGroup.Id, restoredGroup.ComponentIds);
            graph.Groups.Add(restoredGroup);
        }
    }
    private static void CollapsePlacement(HardwareGraph graph, HashSet<string> selected, string instanceId, int gridX, int gridY)
    {
        var placement = graph.Placement;
        if (placement is null)
        {
            return;
        }

        foreach (var id in selected)
        {
            placement.ComponentPositions.Remove(id);
        }

        var cell = placement.GridCellMicrometers;
        placement.ComponentPositions[instanceId] = new PhysicalPoint(gridX * cell, gridY * cell);
    }

    private static void ExpandPlacement(HardwareGraph graph, string instanceId, IEnumerable<HardwareComponent> restoredComponents)
    {
        var placement = graph.Placement;
        if (placement is null)
        {
            return;
        }

        placement.ComponentPositions.Remove(instanceId);
        var cell = placement.GridCellMicrometers;
        foreach (var component in restoredComponents)
        {
            placement.ComponentPositions[component.Id] = new PhysicalPoint(component.Position.X * cell, component.Position.Y * cell);
        }
    }

    private static Dictionary<string, string> BuildMacroExpansionIdMap(HardwareGraph graph, string instanceId, MacroComponent definition)
    {
        var used = graph.Components
            .Where(component => !string.Equals(component.Id, instanceId, StringComparison.OrdinalIgnoreCase))
            .Select(component => component.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var idMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var component in definition.InternalComponents.OrderBy(component => component.Id, StringComparer.Ordinal))
        {
            var id = component.Id;
            if (used.Contains(id))
            {
                id = SafeIdentifier($"{instanceId}_{component.Id}");
                var index = 1;
                var candidate = id;
                while (used.Contains(candidate))
                {
                    candidate = $"{id}_{index++}";
                }

                id = candidate;
            }

            used.Add(id);
            idMap[component.Id] = id;
        }

        return idMap;
    }

    private static string EndpointKey(PortRef endpoint) => $"{endpoint.ComponentId}.{endpoint.PortName}";

    private static string SafeIdentifier(string value)
    {
        var chars = value.Select(ch => char.IsLetterOrDigit(ch) || ch == '_' ? ch : '_').ToArray();
        return new string(chars);
    }
    private static string UniqueComponentId(HardwareGraph graph, string baseId)
    {
        var id = baseId;
        var index = 1;
        while (graph.FindComponent(id) is not null)
        {
            id = $"{baseId}_{index++}";
        }

        return id;
    }

    private static string UniqueLinkId(HardwareGraph graph, string baseId)
    {
        var id = baseId;
        var index = 1;
        while (graph.Links.Any(link => string.Equals(link.Id, id, StringComparison.OrdinalIgnoreCase)))
        {
            id = $"{baseId}_{index++}";
        }

        return id;
    }

    private static string UniqueGroupId(HardwareGraph graph, string baseId)
    {
        var id = baseId;
        var index = 1;
        while (graph.Groups.Any(group => string.Equals(group.Id, id, StringComparison.OrdinalIgnoreCase)))
        {
            id = $"{baseId}_{index++}";
        }

        return id;
    }
}
