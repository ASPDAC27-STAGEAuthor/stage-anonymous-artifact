namespace HardwareSim.Core;

/// <summary>Represents palette item data exchanged by hardware design and simulation workflows.</summary>
/// <param name="Kind">Provides the kind value carried by this contract.</param>
/// <param name="DisplayName">Provides the display name value carried by this contract.</param>
/// <param name="Description">Provides the description value carried by this contract.</param>
public sealed record PaletteItem(ComponentKind Kind, string DisplayName, string Description)
{
    /// <summary>Gets the stable registry type id used to create this palette item.</summary>
    public string TypeId { get; init; } = ComponentTypeIds.BuiltIn(Kind);
    /// <summary>Gets the palette category supplied by the plugin descriptor.</summary>
    public string Category { get; init; } = "Core";
    /// <summary>Gets the Unity-independent glyph key supplied by the plugin descriptor.</summary>
    public string Glyph { get; init; } = "component";
    /// <summary>Gets the Unity-independent color supplied by the plugin descriptor.</summary>
    public string ColorHex { get; init; } = "#8A8F98";
    /// <summary>Gets the short display abbreviation supplied by the plugin descriptor.</summary>
    public string Abbreviation { get; init; } = "CMP";
    /// <summary>Gets the deterministic palette sort order supplied by the plugin descriptor.</summary>
    public int SortOrder { get; init; } = int.MaxValue;
}
/// <summary>Summarizes the components and links removed by an editor delete command.</summary>
/// <param name="ComponentCount">Counts removed components.</param>
/// <param name="LinkCount">Counts removed links, including links removed because their component endpoint was deleted.</param>
public sealed record EditorDeleteResult(int ComponentCount, int LinkCount);

/// <summary>Defines explicit member handling for group deletion.</summary>
public enum GroupDeleteMode
{
    /// <summary>Deletes only the visual group and preserves every member component and link.</summary>
    GroupOnly,
    /// <summary>Deletes the visual group, member components, and links attached to those members.</summary>
    GroupAndMembers
}

/// <summary>Defines explicit member handling for group copy operations.</summary>
public enum GroupCopyMode
{
    /// <summary>Copies only the visual group shell and keeps it attached to the same member ids.</summary>
    GroupOnly,
    /// <summary>Requests copying member components through the Phase 5 copy/paste remapping workflow.</summary>
    GroupWithMembers
}

/// <summary>Previews a group edit that requires user confirmation before members are affected.</summary>
/// <param name="GroupId">Provides the group identifier.</param>
/// <param name="Action">Provides the requested action.</param>
/// <param name="MemberBehavior">Provides the explicit member behavior.</param>
/// <param name="RequiresConfirmation">Indicates whether UI confirmation is required.</param>
/// <param name="MemberIds">Provides the affected member ids.</param>
public sealed record GroupEditPreview(string GroupId, string Action, string MemberBehavior, bool RequiresConfirmation, IReadOnlyList<string> MemberIds);

/// <summary>Summarizes member movement performed by one undoable group drag command.</summary>
/// <param name="GroupId">Provides the group identifier.</param>
/// <param name="MovedComponentIds">Provides moved component ids.</param>
/// <param name="DeltaX">Provides the X grid delta.</param>
/// <param name="DeltaY">Provides the Y grid delta.</param>
public sealed record GroupMoveResult(string GroupId, IReadOnlyList<string> MovedComponentIds, int DeltaX, int DeltaY);

/// <summary>Summarizes a confirmed group delete command.</summary>
/// <param name="GroupId">Provides the group identifier.</param>
/// <param name="Mode">Provides the explicit delete mode.</param>
/// <param name="DeletedComponents">Counts deleted member components.</param>
/// <param name="DeletedLinks">Counts deleted links.</param>
public sealed record GroupDeleteResult(string GroupId, GroupDeleteMode Mode, int DeletedComponents, int DeletedLinks);

/// <summary>Summarizes a confirmed group copy command.</summary>
/// <param name="SourceGroupId">Provides the original group identifier.</param>
/// <param name="NewGroupId">Provides the copied group identifier.</param>
/// <param name="Mode">Provides the explicit copy mode.</param>
/// <param name="CopiedComponentIds">Provides copied member component ids when the mode copies members.</param>
public sealed record GroupCopyResult(string SourceGroupId, string NewGroupId, GroupCopyMode Mode, IReadOnlyList<string> CopiedComponentIds);

/// <summary>Summarizes macro library import through the editor undo flow.</summary>
/// <param name="ImportedCount">Counts imported macro definitions.</param>
public sealed record MacroLibraryImportResult(int ImportedCount);
/// <summary>Provides the hardware editor service for hardware design and simulation workflows.</summary>
public sealed class HardwareEditor
{
    private readonly Stack<string> _undo = new();
    private readonly Stack<string> _redo = new();
    private readonly HardwareGraphValidator _validator = new();
    private readonly ComponentTypeRegistry _componentRegistry;

    /// <summary>Gets or sets the graph value carried by the enclosing hardware editor contract.</summary>
    public HardwareGraph Graph { get; private set; }
    /// <summary>Gets the shared invalidation state updated by graph edits and compilation.</summary>
    public ProjectDirtyState DirtyState { get; }
    /// <summary>Gets the component registry used by this editor instance.</summary>
    public ComponentTypeRegistry ComponentRegistry => _componentRegistry;
    /// <summary>Gets the registry-backed palette for this editor instance.</summary>
    public IReadOnlyList<PaletteItem> Palette => _componentRegistry.PaletteItems();

    /// <summary>Initializes a new hardware editor instance from the supplied state.</summary>
    public HardwareEditor(HardwareGraph? graph = null, ProjectDirtyState? dirtyState = null, ComponentTypeRegistry? componentRegistry = null)
    {
        Graph = graph ?? new HardwareGraph();
        DirtyState = dirtyState ?? new ProjectDirtyState();
        _componentRegistry = componentRegistry ?? ComponentTypeRegistry.CreateDefault();
    }

    /// <summary>Gets the mvp palette collection carried by the enclosing hardware editor contract.</summary>
    public static IReadOnlyList<PaletteItem> MvpPalette { get; } = ComponentTypeRegistry.CreateDefault().PaletteItems()
        .Where(item => string.Equals(item.Category, "Core", StringComparison.OrdinalIgnoreCase))
        .ToList();

    /// <summary>Adds component to the current model.</summary>
    public HardwareComponent AddComponent(ComponentKind kind, string id, GridPosition position, string? name = null) =>
        AddComponent(ComponentTypeIds.BuiltIn(kind), id, position, name);

    /// <summary>Adds a component by stable plugin type id to the current model.</summary>
    public HardwareComponent AddComponent(string typeId, string id, GridPosition position, string? name = null)
    {
        if (Graph.FindComponent(id) is not null)
        {
            throw new InvalidOperationException($"Component id '{id}' already exists.");
        }

        SaveUndoSnapshot();
        var component = _componentRegistry.CreateComponent(typeId, id, position, name);
        Graph.Components.Add(component);
        DirtyState.MarkHardwareGraphChanged();
        _redo.Clear();
        return component;
    }

    /// <summary>Adds a template-backed component using shell ports from the template semantic IR.</summary>
    public HardwareComponent AddTemplateComponent(ComponentTemplate template, string id, GridPosition position, IReadOnlyDictionary<string, string>? overrides = null, string? name = null)
    {
        if (template is null)
        {
            throw new ArgumentNullException(nameof(template));
        }

        if (Graph.FindComponent(id) is not null)
        {
            throw new InvalidOperationException($"Component id '{id}' already exists.");
        }

        SaveUndoSnapshot();
        var component = _componentRegistry.CreateComponent(ComponentTypeIds.BuiltIn(ToComponentKind(template.TargetKind)), id, position, name ?? template.DisplayName);
        component.TemplateRef = new ComponentTemplateInstanceRef
        {
            TemplateId = template.TemplateId,
            Version = template.Version,
            ParameterOverrides = overrides is null
                ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>(overrides, StringComparer.OrdinalIgnoreCase)
        };
        component.Ports = template.ExternalPorts
            .OrderBy(port => port.Name, StringComparer.Ordinal)
            .Select(ToHardwarePort)
            .ToList();
        Graph.Components.Add(component);
        DirtyState.MarkHardwareGraphChanged();
        _redo.Clear();
        return component;
    }

    private static ComponentKind ToComponentKind(ComponentTemplateTargetKind targetKind) => targetKind switch
    {
        ComponentTemplateTargetKind.ProcessingElement => ComponentKind.ProcessingElement,
        ComponentTemplateTargetKind.Router => ComponentKind.Router,
        ComponentTemplateTargetKind.Memory => ComponentKind.Memory,
        ComponentTemplateTargetKind.Buffer => ComponentKind.Buffer,
        ComponentTemplateTargetKind.Link => ComponentKind.LinkEndpoint,
        _ => ComponentKind.Custom
    };

    private static HardwarePort ToHardwarePort(TemplateExternalPort port) => new()
    {
        Name = port.Name,
        Direction = port.Direction,
        SignalType = port.SignalType,
        DataType = port.DataType,
        Precision = port.Precision,
        Protocol = port.Protocol,
        BandwidthBitsPerCycle = port.BandwidthBitsPerCycle,
        Required = false
    };
    /// <summary>Moves a component, records an undo snapshot, and invalidates compiled results.</summary>
    public void MoveComponent(string componentId, GridPosition newPosition)
    {
        var component = Graph.FindComponent(componentId) ?? throw new InvalidOperationException($"Component '{componentId}' does not exist.");
        SaveUndoSnapshot();
        component.Position = newPosition;
        DirtyState.MarkHardwareGraphChanged();
        _redo.Clear();
    }

    /// <summary>Places a component on the explicit physical floorplan without changing schematic coordinates.</summary>
    public PlacementEditResult PlacePhysicalComponent(string componentId, int row, int col, int widthCells = 1, int heightCells = 1, string? layer = null)
    {
        _ = RequireComponent(componentId);
        SaveUndoSnapshot();
        Graph.Placement ??= new PhysicalPlacement();
        var result = Graph.Placement.PlaceComponent(componentId, row, col, widthCells, heightCells, layer);
        if (result.IsSuccess)
        {
            DirtyState.MarkPlacementChanged();
            _redo.Clear();
            return result;
        }

        Graph = HardwareGraphJson.Deserialize(_undo.Pop());
        return result;
    }

    /// <summary>Moves an existing physical placement footprint while preserving its size.</summary>
    public PlacementEditResult MovePhysicalComponent(string componentId, int row, int col, string? layer = null)
    {
        _ = RequireComponent(componentId);
        SaveUndoSnapshot();
        Graph.Placement ??= new PhysicalPlacement();
        var result = Graph.Placement.MoveComponent(componentId, row, col, layer);
        if (result.IsSuccess)
        {
            DirtyState.MarkPlacementChanged();
            _redo.Clear();
            return result;
        }

        Graph = HardwareGraphJson.Deserialize(_undo.Pop());
        return result;
    }

    /// <summary>Removes the explicit physical placement for a component.</summary>
    public bool RemovePhysicalPlacement(string componentId)
    {
        _ = RequireComponent(componentId);
        if (Graph.Placement is null)
        {
            return false;
        }

        SaveUndoSnapshot();
        var removed = Graph.Placement.RemoveComponent(componentId);
        if (removed)
        {
            DirtyState.MarkPlacementChanged();
            _redo.Clear();
        }
        else
        {
            _undo.Pop();
        }

        return removed;
    }
    /// <summary>Creates an explicit physical route through an undoable graph edit.</summary>
    public PhysicalRoute CreatePhysicalRoute(
        string linkId,
        IEnumerable<PhysicalPoint> path,
        RoutingLayerId? layerId = null,
        RoutingMedium medium = RoutingMedium.ElectricalMetal,
        PhysicalRouteTargetKind targetKind = PhysicalRouteTargetKind.LogicalLink)
    {
        if (targetKind == PhysicalRouteTargetKind.LogicalLink)
        {
            _ = RequireLink(linkId);
        }

        SaveUndoSnapshot();
        Graph.Routing ??= new PhysicalRouting();
        var editor = new PhysicalRouteEditor(Graph.Routing);
        var route = editor.CreateStructuredRoute(linkId, path, layerId, medium, targetKind);
        DirtyState.MarkRoutingChanged();
        _redo.Clear();
        return route;
    }

    /// <summary>Deletes an explicit physical route through an undoable graph edit.</summary>
    public bool DeletePhysicalRoute(string linkId)
    {
        if (Graph.Routing?.FindRoute(linkId) is null)
        {
            return false;
        }

        SaveUndoSnapshot();
        new PhysicalRouteEditor(Graph.Routing).DeleteRoute(linkId);
        DirtyState.MarkRoutingChanged();
        _redo.Clear();
        return true;
    }

    /// <summary>Appends a point to an explicit route through an undoable graph edit.</summary>
    public void AppendPhysicalRoutePoint(string linkId, PhysicalPoint point)
    {
        var editor = RequirePhysicalRouteEditor(linkId);
        SaveUndoSnapshot();
        editor.AppendPoint(linkId, point);
        DirtyState.MarkRoutingChanged();
        _redo.Clear();
    }

    /// <summary>Inserts a point into an explicit route through an undoable graph edit.</summary>
    public void InsertPhysicalRoutePoint(string linkId, int index, PhysicalPoint point)
    {
        var editor = RequirePhysicalRouteEditor(linkId);
        SaveUndoSnapshot();
        editor.InsertPoint(linkId, index, point);
        DirtyState.MarkRoutingChanged();
        _redo.Clear();
    }

    /// <summary>Moves a point in an explicit route through an undoable graph edit.</summary>
    public void MovePhysicalRoutePoint(string linkId, int index, PhysicalPoint point)
    {
        var editor = RequirePhysicalRouteEditor(linkId);
        SaveUndoSnapshot();
        editor.MovePoint(linkId, index, point);
        DirtyState.MarkRoutingChanged();
        _redo.Clear();
    }

    /// <summary>Deletes a point from an explicit route through an undoable graph edit.</summary>
    public void DeletePhysicalRoutePoint(string linkId, int index)
    {
        var editor = RequirePhysicalRouteEditor(linkId);
        SaveUndoSnapshot();
        editor.DeletePoint(linkId, index);
        DirtyState.MarkRoutingChanged();
        _redo.Clear();
    }
    /// <summary>Removes component from the current model.</summary>
    public void DeleteComponent(string componentId)
    {
        var component = Graph.FindComponent(componentId) ?? throw new InvalidOperationException($"Component '{componentId}' does not exist.");
        SaveUndoSnapshot();
        Graph.Components.Remove(component);
        Graph.Links.RemoveAll(l => l.Source.ComponentId == componentId || l.Destination.ComponentId == componentId);
        foreach (var group in Graph.Groups)
        {
            group.ComponentIds.Remove(componentId);
        }
        DirtyState.MarkHardwareGraphChanged();
        _redo.Clear();
    }

    /// <summary>Removes selected components and links in one undoable graph edit.</summary>
    public EditorDeleteResult DeleteSelection(IEnumerable<string> componentIds, IEnumerable<string> linkIds)
    {
        var componentSet = componentIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var linkSet = linkIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var componentsToRemove = Graph.Components
            .Where(component => componentSet.Contains(component.Id))
            .ToList();
        var linksToRemove = Graph.Links
            .Where(link =>
                linkSet.Contains(link.Id) ||
                componentSet.Contains(link.Source.ComponentId) ||
                componentSet.Contains(link.Destination.ComponentId))
            .ToList();

        if (componentsToRemove.Count == 0 && linksToRemove.Count == 0)
        {
            return new EditorDeleteResult(0, 0);
        }

        SaveUndoSnapshot();
        foreach (var link in linksToRemove)
        {
            Graph.Links.Remove(link);
        }

        foreach (var component in componentsToRemove)
        {
            Graph.Components.Remove(component);
            foreach (var group in Graph.Groups)
            {
                group.ComponentIds.Remove(component.Id);
            }
        }

        DirtyState.MarkHardwareGraphChanged();
        _redo.Clear();
        return new EditorDeleteResult(componentsToRemove.Count, linksToRemove.Count);
    }

    /// <summary>Creates a visual group with Phase 5 rendering metadata without changing simulation semantics.</summary>
    public VisualGroup CreateGroup(string groupId, string name, IEnumerable<string> componentIds)
    {
        if (string.IsNullOrWhiteSpace(groupId))
        {
            throw new ArgumentException("Group id is required.", nameof(groupId));
        }

        if (Graph.Groups.Any(group => string.Equals(group.Id, groupId, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"Group id '{groupId}' already exists.");
        }

        var ids = componentIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (ids.Count == 0)
        {
            throw new InvalidOperationException("A group requires at least one member component.");
        }

        foreach (var id in ids)
        {
            _ = RequireComponent(id);
        }

        GroupMacroTools.EnsureNoPartialGroupOverlap(Graph.Groups, groupId, ids);
        SaveUndoSnapshot();
        var group = new VisualGroup
        {
            Id = groupId.Trim(),
            Name = string.IsNullOrWhiteSpace(name) ? groupId.Trim() : name.Trim(),
            ComponentIds = ids,
            VisualMetadata = VisualGroupDefaults.CreateMetadata()
        };
        Graph.Groups.Add(group);
        DirtyState.MarkVisualMetadataChanged();
        _redo.Clear();
        return group;
    }

    /// <summary>Moves every member of a group by one delta and records the operation as one undo command.</summary>
    public GroupMoveResult MoveGroupBy(string groupId, int deltaX, int deltaY)
    {
        var group = RequireGroup(groupId);
        var members = group.ComponentIds
            .Select(id => Graph.FindComponent(id))
            .Where(component => component is not null)
            .Cast<HardwareComponent>()
            .ToList();
        if (members.Count == 0 || deltaX == 0 && deltaY == 0)
        {
            return new GroupMoveResult(group.Id, members.Select(component => component.Id).ToList(), deltaX, deltaY);
        }

        SaveUndoSnapshot();
        foreach (var component in members)
        {
            component.Position = new GridPosition(component.Position.X + deltaX, component.Position.Y + deltaY);
        }

        DirtyState.MarkHardwareGraphChanged();
        _redo.Clear();
        return new GroupMoveResult(group.Id, members.Select(component => component.Id).ToList(), deltaX, deltaY);
    }

    /// <summary>Sets a group's collapsed visual state without changing simulation semantics.</summary>
    public void SetGroupCollapsed(string groupId, bool collapsed)
    {
        var group = RequireGroup(groupId);
        VisualGroupDefaults.ApplyTo(group);
        if (group.Collapsed == collapsed)
        {
            return;
        }

        SaveUndoSnapshot();
        group.Collapsed = collapsed;
        group.VisualMetadata["collapse_semantics"] = VisualGroupDefaults.CollapseSemantics;
        DirtyState.MarkVisualMetadataChanged();
        _redo.Clear();
    }

    /// <summary>Previews a group delete operation so UI can require explicit confirmation.</summary>
    public GroupEditPreview PreviewGroupDelete(string groupId, GroupDeleteMode mode)
    {
        var group = RequireGroup(groupId);
        var behavior = mode == GroupDeleteMode.GroupAndMembers ? "delete_members" : "keep_members";
        return new GroupEditPreview(group.Id, "delete", behavior, true, group.ComponentIds.ToList());
    }

    /// <summary>Deletes a group only after UI confirmation and with explicit member behavior.</summary>
    public GroupDeleteResult DeleteGroup(string groupId, GroupDeleteMode mode, bool confirmed)
    {
        var group = RequireGroup(groupId);
        if (!confirmed)
        {
            throw new InvalidOperationException($"Deleting group '{group.Id}' requires confirmation.");
        }

        SaveUndoSnapshot();
        var deletedComponents = 0;
        var deletedLinks = 0;
        if (mode == GroupDeleteMode.GroupAndMembers)
        {
            var memberSet = group.ComponentIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var linksToRemove = Graph.Links
                .Where(link => memberSet.Contains(link.Source.ComponentId) || memberSet.Contains(link.Destination.ComponentId))
                .ToList();
            foreach (var link in linksToRemove)
            {
                Graph.Links.Remove(link);
            }

            deletedLinks = linksToRemove.Count;
            var componentsToRemove = Graph.Components
                .Where(component => memberSet.Contains(component.Id))
                .ToList();
            foreach (var component in componentsToRemove)
            {
                Graph.Components.Remove(component);
            }

            deletedComponents = componentsToRemove.Count;
            foreach (var visualGroup in Graph.Groups)
            {
                visualGroup.ComponentIds.RemoveAll(id => memberSet.Contains(id));
            }
        }

        Graph.Groups.Remove(group);
        if (mode == GroupDeleteMode.GroupAndMembers)
        {
            DirtyState.MarkHardwareGraphChanged();
        }
        else
        {
            DirtyState.MarkVisualMetadataChanged();
        }

        _redo.Clear();
        return new GroupDeleteResult(group.Id, mode, deletedComponents, deletedLinks);
    }

    /// <summary>Previews a group copy operation so UI can require explicit confirmation.</summary>
    public GroupEditPreview PreviewGroupCopy(string groupId, GroupCopyMode mode)
    {
        var group = RequireGroup(groupId);
        var behavior = mode == GroupCopyMode.GroupWithMembers ? "copy_members" : "copy_group_shell";
        return new GroupEditPreview(group.Id, "copy", behavior, true, group.ComponentIds.ToList());
    }

    /// <summary>Copies a group shell after confirmation; member copy is handled by the Phase 5 copy/paste remapper.</summary>
    public GroupCopyResult CopyGroup(string groupId, string newGroupId, GroupCopyMode mode, bool confirmed)
    {
        var group = RequireGroup(groupId);
        if (!confirmed)
        {
            throw new InvalidOperationException($"Copying group '{group.Id}' requires confirmation.");
        }

        if (mode == GroupCopyMode.GroupWithMembers)
        {
            throw new InvalidOperationException("Copying group members must use the Phase 5 copy/paste remapping workflow.");
        }

        if (string.IsNullOrWhiteSpace(newGroupId))
        {
            throw new ArgumentException("New group id is required.", nameof(newGroupId));
        }

        if (Graph.Groups.Any(candidate => string.Equals(candidate.Id, newGroupId, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"Group id '{newGroupId}' already exists.");
        }

        GroupMacroTools.EnsureNoPartialGroupOverlap(Graph.Groups, newGroupId, group.ComponentIds);
        SaveUndoSnapshot();
        var copy = new VisualGroup
        {
            Id = newGroupId.Trim(),
            Name = group.Name + " Copy",
            ComponentIds = group.ComponentIds.ToList(),
            Collapsed = group.Collapsed,
            VisualMetadata = new Dictionary<string, string>(group.VisualMetadata, StringComparer.OrdinalIgnoreCase)
        };
        VisualGroupDefaults.ApplyTo(copy);
        Graph.Groups.Add(copy);
        DirtyState.MarkVisualMetadataChanged();
        _redo.Clear();
        return new GroupCopyResult(group.Id, copy.Id, mode, Array.Empty<string>());
    }
    /// <summary>Copies selected components through the Phase 5 remapper as one undoable graph edit.</summary>
    public CopyPasteResult CopyPasteComponents(
        IEnumerable<string> componentIds,
        string suffix,
        int offsetX,
        int offsetY,
        bool includeExternalLinks = false)
    {
        SaveUndoSnapshot();
        var result = GroupMacroTools.CopyPasteComponents(Graph, componentIds, suffix, offsetX, offsetY, includeExternalLinks);
        if (result.NewComponentIds.Count > 0 || result.NewLinkIds.Count > 0 || result.NewGroupIds.Count > 0)
        {
            DirtyState.MarkHardwareGraphChanged();
            _redo.Clear();
        }
        else
        {
            _undo.Pop();
        }

        return result;
    }
    /// <summary>Creates a reusable macro and replaces the selected top-level subgraph with one macro instance.</summary>
    public MacroCreateResult CreateMacroFromSelection(
        string macroId,
        string name,
        IEnumerable<string> componentIds,
        string instanceId)
    {
        SaveUndoSnapshot();
        var result = GroupMacroTools.CollapseSelectionToMacro(Graph, macroId, name, componentIds, instanceId);
        DirtyState.MarkHardwareGraphChanged();
        _redo.Clear();
        return result;
    }

    /// <summary>Expands a macro instance back into editable top-level components.</summary>
    public MacroExpandResult ExpandMacroInstance(string instanceId)
    {
        SaveUndoSnapshot();
        var result = GroupMacroTools.ExpandMacroInstance(Graph, instanceId);
        DirtyState.MarkHardwareGraphChanged();
        _redo.Clear();
        return result;
    }

    /// <summary>Updates one macro external port mapping after validating the target internal port.</summary>
    public MacroMappingValidationResult SetMacroExternalPortMapping(string macroId, string externalPortName, PortRef internalEndpoint)
    {
        SaveUndoSnapshot();
        var result = GroupMacroTools.SetMacroExternalPortMapping(Graph, macroId, externalPortName, internalEndpoint);
        if (result.IsValid)
        {
            DirtyState.MarkHardwareGraphChanged();
            _redo.Clear();
        }
        else
        {
            _undo.Pop();
        }

        return result;
    }

    /// <summary>Serializes selected or all macro definitions into the Phase 5 reusable macro library format.</summary>
    public string SaveMacroLibraryJson(IEnumerable<string>? macroIds = null)
    {
        var selected = macroIds?.Where(id => !string.IsNullOrWhiteSpace(id)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var macros = selected is null || selected.Count == 0
            ? Graph.Macros
            : Graph.Macros.Where(macro => selected.Contains(macro.Id));
        return GroupMacroTools.SerializeMacroLibrary(macros);
    }

    /// <summary>Imports reusable macro definitions through an undoable graph edit.</summary>
    public MacroLibraryImportResult LoadMacroLibraryJson(string json, bool overwriteExisting = false)
    {
        SaveUndoSnapshot();
        try
        {
            var imported = GroupMacroTools.ImportMacroLibrary(Graph, json, overwriteExisting);
            if (imported > 0)
            {
                DirtyState.MarkHardwareGraphChanged();
                _redo.Clear();
            }
            else
            {
                _undo.Pop();
            }

            return new MacroLibraryImportResult(imported);
        }
        catch
        {
            _undo.Pop();
            throw;
        }
    }
    /// <summary>Updates the component model binding through an undoable graph edit.</summary>
    public void SetComponentModelRef(string componentId, string? modelRef)
    {
        var component = RequireComponent(componentId);
        SaveUndoSnapshot();
        component.ModelRef = string.IsNullOrWhiteSpace(modelRef) ? null : modelRef.Trim();
        DirtyState.MarkHardwareGraphChanged();
        _redo.Clear();
    }

    /// <summary>Adds or replaces a component parameter through an undoable graph edit.</summary>
    public void SetComponentParameter(string componentId, string key, string value)
    {
        var component = RequireComponent(componentId);
        var normalizedKey = NormalizeParameterKey(key);
        SaveUndoSnapshot();
        component.Parameters[normalizedKey] = value ?? "";
        DirtyState.MarkHardwareGraphChanged();
        _redo.Clear();
    }

   /// <summary>Removes a component parameter through an undoable graph edit.</summary>
   public void RemoveComponentParameter(string componentId, string key)
   {
       var component = RequireComponent(componentId);
       var normalizedKey = NormalizeParameterKey(key);
       if (!component.Parameters.ContainsKey(normalizedKey))
       {
           throw new InvalidOperationException($"Component '{componentId}' does not contain parameter '{normalizedKey}'.");
       }

       SaveUndoSnapshot();
       component.Parameters.Remove(normalizedKey);
       DirtyState.MarkHardwareGraphChanged();
       _redo.Clear();
   }

    /// <summary>Adds or replaces a component visual-style entry through an undoable edit that does not affect simulation semantics.</summary>
    public void SetComponentVisualStyle(string componentId, string key, string value)
    {
        var component = RequireComponent(componentId);
        var normalizedKey = NormalizeParameterKey(key);
        SaveUndoSnapshot();
        component.VisualStyle ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        component.VisualStyle[normalizedKey] = value ?? "";
        DirtyState.MarkVisualMetadataChanged();
        _redo.Clear();
    }

    /// <summary>Removes a component visual-style entry through an undoable edit that does not affect simulation semantics.</summary>
    public void RemoveComponentVisualStyle(string componentId, string key)
    {
        var component = RequireComponent(componentId);
        var normalizedKey = NormalizeParameterKey(key);
        component.VisualStyle ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!component.VisualStyle.ContainsKey(normalizedKey))
        {
            throw new InvalidOperationException($"Component '{componentId}' does not contain visual style '{normalizedKey}'.");
        }

        SaveUndoSnapshot();
        component.VisualStyle.Remove(normalizedKey);
        DirtyState.MarkVisualMetadataChanged();
        _redo.Clear();
    }

    /// <summary>Validates and adds a link, recording undo history and graph invalidation.</summary>
    public HardwareLink Connect(string linkId, PortRef source, PortRef destination)
    {
        if (Graph.Links.Any(l => string.Equals(l.Id, linkId, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"Link id '{linkId}' already exists.");
        }

        var link = new HardwareLink { Id = linkId, Source = source, Destination = destination };
        Graph.Links.Add(link);
        var validation = _validator.Validate(Graph);
        if (validation.Issues.Any(i => i.LinkId == linkId && i.Severity == ValidationSeverity.Error))
        {
            Graph.Links.Remove(link);
            var errors = validation.Issues
                .Where(i => i.LinkId == linkId && i.Severity == ValidationSeverity.Error)
                .Select(i => i.Message);
            throw new InvalidOperationException(string.Join(Environment.NewLine, errors));
        }

        SaveUndoSnapshotBeforeLinkAdd(link);
        DirtyState.MarkHardwareGraphChanged();
        _redo.Clear();
        return link;
    }

    /// <summary>Removes a link while preserving the prior graph for undo.</summary>
    public void Disconnect(string linkId)
    {
        var link = Graph.Links.FirstOrDefault(l => string.Equals(l.Id, linkId, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Link '{linkId}' does not exist.");
        SaveUndoSnapshot();
        Graph.Links.Remove(link);
        DirtyState.MarkHardwareGraphChanged();
        _redo.Clear();
    }

    /// <summary>Updates a link bandwidth value through an undoable graph edit.</summary>
    public void SetLinkBandwidth(string linkId, int bandwidthBitsPerCycle)
    {
        if (bandwidthBitsPerCycle < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(bandwidthBitsPerCycle), "Bandwidth must be non-negative.");
        }

        var link = RequireLink(linkId);
        SaveUndoSnapshot();
        link.BandwidthBitsPerCycle = bandwidthBitsPerCycle;
        DirtyState.MarkHardwareGraphChanged();
        _redo.Clear();
    }

    /// <summary>Updates a link latency value through an undoable graph edit.</summary>
    public void SetLinkLatency(string linkId, int latencyCycles)
    {
        if (latencyCycles < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(latencyCycles), "Latency must be non-negative.");
        }

        var link = RequireLink(linkId);
        SaveUndoSnapshot();
        link.LatencyCycles = latencyCycles;
        DirtyState.MarkHardwareGraphChanged();
        _redo.Clear();
    }

    /// <summary>Updates a link energy-per-bit value through an undoable graph edit.</summary>
    public void SetLinkEnergyPerBit(string linkId, double energyPerBit)
    {
        if (double.IsNaN(energyPerBit) || double.IsInfinity(energyPerBit) || energyPerBit < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(energyPerBit), "Energy per bit must be a finite non-negative value.");
        }

        var link = RequireLink(linkId);
        SaveUndoSnapshot();
        link.EnergyPerBit = energyPerBit;
        DirtyState.MarkHardwareGraphChanged();
        _redo.Clear();
    }

    /// <summary>Updates a link model binding through an undoable graph edit.</summary>
    public void SetLinkModelRef(string linkId, string? modelRef)
    {
        var link = RequireLink(linkId);
        SaveUndoSnapshot();
        link.ModelRef = string.IsNullOrWhiteSpace(modelRef) ? null : modelRef.Trim();
        DirtyState.MarkHardwareGraphChanged();
        _redo.Clear();
    }

    /// <summary>Adds or replaces a link parameter through an undoable graph edit.</summary>
    public void SetLinkParameter(string linkId, string key, string value)
    {
        var link = RequireLink(linkId);
        var normalizedKey = NormalizeParameterKey(key);
        SaveUndoSnapshot();
        link.Parameters[normalizedKey] = value ?? "";
        DirtyState.MarkHardwareGraphChanged();
        _redo.Clear();
    }

    /// <summary>Removes a link parameter through an undoable graph edit.</summary>
    public void RemoveLinkParameter(string linkId, string key)
    {
        var link = RequireLink(linkId);
        var normalizedKey = NormalizeParameterKey(key);
        if (!link.Parameters.ContainsKey(normalizedKey))
        {
            throw new InvalidOperationException($"Link '{linkId}' does not contain parameter '{normalizedKey}'.");
        }

        SaveUndoSnapshot();
        link.Parameters.Remove(normalizedKey);
        DirtyState.MarkHardwareGraphChanged();
        _redo.Clear();
    }

    /// <summary>Gets whether an earlier graph snapshot is available.</summary>
    public bool CanUndo => _undo.Count > 0;
    /// <summary>Gets whether a previously undone graph snapshot is available.</summary>
    public bool CanRedo => _redo.Count > 0;

    /// <summary>Restores the preceding graph snapshot and invalidates compiled results.</summary>
    public void Undo()
    {
        if (!CanUndo)
        {
            return;
        }

        _redo.Push(HardwareGraphJson.Serialize(Graph));
        Graph = HardwareGraphJson.Deserialize(_undo.Pop());
        DirtyState.MarkHardwareGraphChanged();
    }

    /// <summary>Restores the next graph snapshot and invalidates compiled results.</summary>
    public void Redo()
    {
        if (!CanRedo)
        {
            return;
        }

        _undo.Push(HardwareGraphJson.Serialize(Graph));
        Graph = HardwareGraphJson.Deserialize(_redo.Pop());
        DirtyState.MarkHardwareGraphChanged();
    }

    /// <summary>Builds and returns the save project json text representation from the supplied inputs.</summary>
    public string SaveProjectJson() => HardwareGraphJson.Serialize(Graph);

    /// <summary>Deserializes a project with the production graph serializer and marks it for compilation.</summary>
    public static HardwareEditor LoadProjectJson(string json)
    {
        var editor = new HardwareEditor(HardwareGraphJson.Deserialize(json));
        editor.DirtyState.MarkHardwareGraphChanged();
        return editor;
    }

    private void SaveUndoSnapshot() => _undo.Push(HardwareGraphJson.Serialize(Graph));

    private void SaveUndoSnapshotBeforeLinkAdd(HardwareLink link)
    {
        Graph.Links.Remove(link);
        SaveUndoSnapshot();
        Graph.Links.Add(link);
    }

    private HardwareComponent RequireComponent(string componentId) =>
        Graph.FindComponent(componentId) ?? throw new InvalidOperationException($"Component '{componentId}' does not exist.");

    private HardwareLink RequireLink(string linkId) =>
        Graph.Links.FirstOrDefault(link => string.Equals(link.Id, linkId, StringComparison.OrdinalIgnoreCase))
        ?? throw new InvalidOperationException($"Link '{linkId}' does not exist.");

    private PhysicalRouteEditor RequirePhysicalRouteEditor(string linkId)
    {
        if (Graph.Routing is null || Graph.Routing.FindRoute(linkId) is null)
        {
            throw new InvalidOperationException($"Route for link '{linkId}' does not exist.");
        }

        return new PhysicalRouteEditor(Graph.Routing);
    }

    private VisualGroup RequireGroup(string groupId) =>
        Graph.Groups.FirstOrDefault(group => string.Equals(group.Id, groupId, StringComparison.OrdinalIgnoreCase))
        ?? throw new InvalidOperationException($"Group '{groupId}' does not exist.");
    private static string NormalizeParameterKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("Parameter key is required.", nameof(key));
        }

        return key.Trim();
    }

}
