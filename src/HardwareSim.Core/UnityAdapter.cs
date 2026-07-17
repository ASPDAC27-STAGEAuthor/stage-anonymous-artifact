namespace HardwareSim.Core;

/// <summary>Represents palette item view data exchanged by hardware design and simulation workflows.</summary>
/// <param name="Kind">Provides the kind value carried by this contract.</param>
/// <param name="DisplayName">Provides the display name value carried by this contract.</param>
/// <param name="Description">Provides the description value carried by this contract.</param>
public sealed record PaletteItemView(ComponentKind Kind, string DisplayName, string Description)
{
    /// <summary>Gets the stable registry type id used for palette creation.</summary>
    public string TypeId { get; init; } = ComponentTypeIds.BuiltIn(Kind);
    /// <summary>Gets the plugin category for grouping or filtering.</summary>
    public string Category { get; init; } = "Core";
    /// <summary>Gets the Unity-independent glyph key.</summary>
    public string Glyph { get; init; } = "component";
    /// <summary>Gets the Unity-independent color encoded as #RRGGBB.</summary>
    public string ColorHex { get; init; } = "#8A8F98";
    /// <summary>Gets the short display abbreviation.</summary>
    public string Abbreviation { get; init; } = "CMP";
    /// <summary>Gets the deterministic palette sort order.</summary>
    public int SortOrder { get; init; } = int.MaxValue;
}
/// <summary>Represents port view data exchanged by hardware design and simulation workflows.</summary>
/// <param name="Name">Provides the name value carried by this contract.</param>
/// <param name="Direction">Provides the direction value carried by this contract.</param>
/// <param name="SignalType">Provides the signal type value carried by this contract.</param>
/// <param name="DataType">Provides the data type value carried by this contract.</param>
/// <param name="Precision">Provides the precision value carried by this contract.</param>
/// <param name="Protocol">Provides the protocol value carried by this contract.</param>
public sealed record PortView(string Name, PortDirection Direction, SignalType SignalType, HardwareDataType DataType, PrecisionKind Precision, PortProtocol Protocol)
{
    /// <summary>Gets the semantic quantity carried by the port, or an empty string when unspecified.</summary>
    public string Quantity { get; init; } = "";
    /// <summary>Gets the canonical unit contract carried by the port, or an empty string when unitless or unspecified.</summary>
    public string Units { get; init; } = "";
    /// <summary>Gets the declared port bandwidth in bits per cycle.</summary>
    public int BandwidthBitsPerCycle { get; init; }
    /// <summary>Gets the component-local port latency in cycles.</summary>
    public int LatencyCycles { get; init; }
    /// <summary>Gets the clock domain associated with the port.</summary>
    public string ClockDomain { get; init; } = "default";
    /// <summary>Gets whether compilation requires the port to be connected.</summary>
    public bool Required { get; init; }
    /// <summary>Gets whether the port permits multiple links.</summary>
    public bool MultiConnect { get; init; }
}
/// <summary>Represents component view data exchanged by hardware design and simulation workflows.</summary>
/// <param name="Id">Provides the id value carried by this contract.</param>
/// <param name="Name">Provides the name value carried by this contract.</param>
/// <param name="Type">Provides the type value carried by this contract.</param>
/// <param name="X">Provides the x value carried by this contract.</param>
/// <param name="Y">Provides the y value carried by this contract.</param>
/// <param name="Ports">Provides the ports value carried by this contract.</param>
/// <param name="Parameters">Provides component parameters currently persisted in the authoritative graph.</param>
/// <param name="ModelRef">Provides the optional model binding persisted with the component.</param>
/// <param name="VisualStyle">Provides the visual metadata persisted with the component, never affecting simulation semantics.</param>
public sealed record ComponentView(
    string Id,
    string Name,
    ComponentKind Type,
    int X,
    int Y,
    IReadOnlyList<PortView> Ports,
    IReadOnlyDictionary<string, string> Parameters,
    string? ModelRef,
    IReadOnlyDictionary<string, string>? VisualStyle)
{
    /// <summary>Gets the stable registry type id for plugin-backed components.</summary>
    public string TypeId { get; init; } = "";
    /// <summary>Gets the registry-provided display name without relying on the mutable instance name.</summary>
    public string PresentationDisplayName { get; init; } = "";
    /// <summary>Gets the registry-provided glyph key used by descriptor-driven Unity rendering.</summary>
    public string PresentationGlyph { get; init; } = "component";
    /// <summary>Gets the registry-provided accent color encoded as #RRGGBB.</summary>
    public string PresentationColorHex { get; init; } = "#8A8F98";
    /// <summary>Gets the registry-provided short label used on compact graph cards.</summary>
    public string PresentationAbbreviation { get; init; } = "CMP";
    /// <summary>Gets the registry-provided category used for presentation-only layout decisions.</summary>
    public string PresentationCategory { get; init; } = "Core";
    /// <summary>Gets the optional ComponentTemplate id bound to this graph component.</summary>
    public string? TemplateId { get; init; }
    /// <summary>Gets the optional ComponentTemplate version bound to this graph component.</summary>
    public string? TemplateVersion { get; init; }
    /// <summary>Gets the latest compiled profile hash recorded on the template reference, when available.</summary>
    public string? CompiledProfileHash { get; init; }
};

/// <summary>Represents link view data exchanged by hardware design and simulation workflows.</summary>
/// <param name="Id">Provides the id value carried by this contract.</param>
/// <param name="SourceComponentId">Provides the source component id value carried by this contract.</param>
/// <param name="SourcePort">Provides the source port value carried by this contract.</param>
/// <param name="DestinationComponentId">Provides the destination component id value carried by this contract.</param>
/// <param name="DestinationPort">Provides the destination port value carried by this contract.</param>
/// <param name="BandwidthBitsPerCycle">Provides the bandwidth value persisted with the link.</param>
/// <param name="LatencyCycles">Provides the latency value persisted with the link.</param>
/// <param name="EnergyPerBit">Provides the energy-per-bit value persisted with the link.</param>
/// <param name="ModelRef">Provides the optional model binding persisted with the link.</param>
/// <param name="Parameters">Provides link parameters currently persisted in the authoritative graph.</param>
public sealed record LinkView(
    string Id,
    string SourceComponentId,
    string SourcePort,
    string DestinationComponentId,
    string DestinationPort,
    int BandwidthBitsPerCycle,
    int LatencyCycles,
    double EnergyPerBit,
    string? ModelRef,
    IReadOnlyDictionary<string, string> Parameters);
/// <summary>Represents visual group view data exchanged by hardware design and simulation workflows.</summary>
/// <param name="Id">Provides the group identifier.</param>
/// <param name="Name">Provides the display name.</param>
/// <param name="ComponentIds">Provides member component ids.</param>
/// <param name="Collapsed">Indicates whether the group is visually collapsed.</param>
/// <param name="VisualMetadata">Provides visual-only group metadata.</param>
public sealed record GroupView(
    string Id,
    string Name,
    IReadOnlyList<string> ComponentIds,
    bool Collapsed,
    IReadOnlyDictionary<string, string> VisualMetadata);
/// <summary>Represents reusable macro definition view data exchanged by hardware design and simulation workflows.</summary>
/// <param name="Id">Provides the macro definition id.</param>
/// <param name="Name">Provides the display name.</param>
/// <param name="SchemaVersion">Provides the macro schema version.</param>
/// <param name="InternalComponentIds">Provides ids captured inside the macro definition.</param>
/// <param name="ExternalPortMappings">Provides external port names mapped to internal component ports.</param>
public sealed record MacroView(
    string Id,
    string Name,
    string SchemaVersion,
    IReadOnlyList<string> InternalComponentIds,
    IReadOnlyDictionary<string, string> ExternalPortMappings);/// <summary>Represents inspector field data exchanged by hardware design and simulation workflows.</summary>
/// <param name="Name">Provides the name value carried by this contract.</param>
/// <param name="Value">Provides the displayed field value carried by this contract.</param>
public sealed record InspectorField(string Name, string Value);
/// <summary>Represents inspector view data exchanged by hardware design and simulation workflows.</summary>
/// <param name="TargetId">Provides the target id value carried by this contract.</param>
/// <param name="Fields">Provides the fields value carried by this contract.</param>
public sealed record InspectorView(string TargetId, IReadOnlyList<InspectorField> Fields);
/// <summary>Represents one component in the physical placement editor view.</summary>
public sealed record PlacementComponentView(
    string ComponentId,
    int Row,
    int Col,
    int WidthCells,
    int HeightCells,
    string Layer,
    double XMicrometers,
    double YMicrometers,
    double? PhysicalWidthUm = null,
    double? PhysicalHeightUm = null,
    double? PhysicalAreaUm2 = null,
    string FootprintScope = "",
    string FootprintSourceKind = "",
    string FootprintEvidenceStatus = "Unknown",
    string FootprintUncertainty = "",
    string FootprintHash = "",
    PhysicalPlacementDisplayMode DisplayMode = PhysicalPlacementDisplayMode.PhysicalScale,
    bool NonProportionalRendering = false);
/// <summary>Represents physical placement editor snapshot data exchanged by hardware design and simulation workflows.</summary>
public sealed record PlacementViewSnapshot(
    int Rows,
    int Cols,
    double CellWidthMicrometers,
    double CellHeightMicrometers,
    string Layer,
    IReadOnlyList<PlacementComponentView> Components,
    IReadOnlyList<PlacementIssue> Issues,
    string Summary);
/// <summary>Represents a physical Manhattan distance query result for editor overlays.</summary>
public sealed record PlacementDistanceView(bool IsAvailable, string SourceComponentId, string DestinationComponentId, double DistanceMicrometers, string Message);
/// <summary>Represents editor snapshot data exchanged by hardware design and simulation workflows.</summary>
/// <param name="Palette">Provides the palette value carried by this contract.</param>
/// <param name="Components">Provides the components value carried by this contract.</param>
/// <param name="Links">Provides the links value carried by this contract.</param>
/// <param name="Inspector">Provides the inspector value carried by this contract.</param>
/// <param name="Groups">Provides visual group view data.</param>
/// <param name="Macros">Provides reusable macro definition view data.</param>
public sealed record EditorSnapshot(IReadOnlyList<PaletteItemView> Palette, IReadOnlyList<ComponentView> Components, IReadOnlyList<LinkView> Links, IReadOnlyList<GroupView> Groups, IReadOnlyList<MacroView> Macros, InspectorView? Inspector)
{
    /// <summary>Gets every registry-visible palette entry, including extension categories hidden from the legacy Palette projection.</summary>
    public IReadOnlyList<PaletteItemView> RegistryPalette { get; init; } = Palette;
}

/// <summary>Provides the hardware editor adapter service for hardware design and simulation workflows.</summary>
public sealed class HardwareEditorAdapter
{
    private HardwareEditor _editor;
    private readonly ComponentTemplateLibrary _componentTemplateLibrary;
    private string? _selectedComponentId;
    private string? _selectedLinkId;

    /// <summary>Initializes a new hardware editor adapter instance from the supplied state.</summary>
    public HardwareEditorAdapter(HardwareEditor? editor = null, ComponentTemplateLibrary? componentTemplateLibrary = null)
    {
        _editor = editor ?? new HardwareEditor();
        _componentTemplateLibrary = componentTemplateLibrary ?? ComponentDefinitionAdapter.CreateMvpTemplateLibrary();
    }

    /// <summary>Creates a Unity adapter whose template palette includes the Phase 9 literature-bound CIM profile.</summary>
    public static HardwareEditorAdapter CreateWithPhase9Examples(string literatureCatalogPath, HardwareEditor? editor = null)
    {
        var definitions = ComponentDefinitionAdapter.CreateWithPhase9Examples(literatureCatalogPath);
        return new HardwareEditorAdapter(editor, definitions.Library);
    }

    /// <summary>Gets the authoritative HardwareGraph edited by the Unity-facing adapter.</summary>
    public HardwareGraph Graph => _editor.Graph;
    /// <summary>Gets the shared dirty-state tracker for editing, compilation, and simulation.</summary>
    public ProjectDirtyState DirtyState => _editor.DirtyState;
    /// <summary>Gets the number of processing-cluster groups collapsed during the most recent project load.</summary>
    public int LastDefaultCollapsedGroupCount { get; private set; }

    /// <summary>Adds component to the current model.</summary>
    public string AddComponent(ComponentKind kind, string id, int x, int y)
    {
        return _editor.AddComponent(kind, id, new GridPosition(x, y)).Id;
    }
    /// <summary>Adds a component by stable plugin type id through the Core editor.</summary>
    public string AddComponent(string typeId, string id, int x, int y)
    {
        return _editor.AddComponent(typeId, id, new GridPosition(x, y)).Id;
    }

    /// <summary>Gets placement choices for published or compiled ComponentTemplate targets.</summary>
    public IReadOnlyList<ComponentTemplatePlacementOption> GetTemplatePlacementOptions(ComponentTemplateTargetKind targetKind) =>
        new ComponentDefinitionAdapter(_componentTemplateLibrary).PlacementOptions(targetKind);

    /// <summary>Adds the Phase 7C MVP template-backed ProcessingElement through the Core editor.</summary>
    public string AddTemplateProcessingElement(string id, int x, int y, IReadOnlyDictionary<string, string>? overrides = null) =>
        AddTemplateProcessingElement(id, x, y, "PE_Array_32x32_FP8_SRAM_Synthetic", "1.0.0", overrides);

    /// <summary>Adds a selected Phase 7C ProcessingElement ComponentTemplate through the Core editor.</summary>
    public string AddTemplateProcessingElement(string id, int x, int y, string templateId, string version, IReadOnlyDictionary<string, string>? overrides = null)
    {
        var template = _componentTemplateLibrary.Find(templateId, version)
            ?? throw new KeyNotFoundException($"ComponentTemplate '{templateId}' version '{version}' is unavailable.");
        var component = _editor.AddTemplateComponent(template, id, new GridPosition(x, y), overrides, template.DisplayName);
        if (string.Equals(template.TemplateId, Phase9CimTemplateFactory.TemplateId, StringComparison.Ordinal))
        {
            PopulatePhase9AuthoringParameters(component, template, overrides);
        }
        return component.Id;
    }
    private static void PopulatePhase9AuthoringParameters(
        HardwareComponent component,
        ComponentTemplate template,
        IReadOnlyDictionary<string, string>? overrides)
    {
        foreach (var parameter in template.Parameters)
        {
            component.Parameters[parameter.Name] = overrides is not null && overrides.TryGetValue(parameter.Name, out var value)
                ? value
                : parameter.DefaultValue;
        }

        var profile = template.CompiledProfile;
        var storage = template.StorageLayouts.FirstOrDefault();
        component.Parameters["phase9_capability"] = string.Join(",", profile?.SupportedOperations ?? []);
        component.Parameters["phase9_array"] = storage is null
            ? "unknown"
            : $"{storage.Rows}x{storage.Columns};cell_bits={storage.CellBits};encoding={storage.Encoding}";
        component.Parameters["phase9_precision"] = template.OperationContract.MultiplyDType + " multiply / " +
            template.OperationContract.AccumulateDType + " accumulate / " + template.OperationContract.OutputDType + " output";
        component.Parameters["phase9_storage"] = storage is null
            ? "unknown"
            : $"capacity_bits={storage.CapacityBits};runtime_write={storage.RuntimeWriteAllowed.ToString().ToLowerInvariant()}";
        component.Parameters["phase9_mode"] = component.Parameters.GetValueOrDefault(Phase9CimTemplateFactory.ExecutionModeParameter, "functional");
        component.Parameters["phase9_profile"] = string.Join(",", template.ProfileBindings.Select(binding => binding.ProfileId).OrderBy(value => value, StringComparer.Ordinal));
        component.Parameters["phase9_valid_range"] = "literature exact operating points only; no cross-source interpolation or extrapolation";
        component.Parameters["phase9_evidence"] = "normalized per-field provenance; inspect profile binding and footprint badges";
        component.Parameters["phase9_evidence_gaps"] = string.Join(" | ", template.Provenance.Warnings);
        if (profile?.PhysicalFootprint is { } footprint)
        {
            component.Parameters["physical_footprint_hash"] = footprint.FootprintHash;
            component.Parameters["physical_footprint_scope"] = footprint.Scope.ToString();
            component.Parameters["physical_footprint_source_kind"] = footprint.SourceKind.ToString();
            component.Parameters["physical_footprint_evidence_status"] = footprint.EvidenceStatus.ToString();
            component.Parameters["physical_footprint_uncertainty"] = footprint.Uncertainty;
            if (footprint.IsKnown)
            {
                component.Parameters["physical_width_um"] = footprint.WidthUm!.Value.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
                component.Parameters["physical_height_um"] = footprint.HeightUm!.Value.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
                component.Parameters["area_um2"] = footprint.AreaUm2!.Value.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
            }
        }
    }
    /// <summary>Moves a component to Unity grid coordinates through the Core editor.</summary>
    public void MoveComponent(string componentId, int x, int y) => _editor.MoveComponent(componentId, new GridPosition(x, y));
    /// <summary>Attempts to place a component on the physical grid without changing schematic coordinates.</summary>
    public bool TryPlaceComponent(string componentId, int row, int col, out string error, int widthCells = 1, int heightCells = 1, string? layer = null)
    {
        var result = _editor.PlacePhysicalComponent(componentId, row, col, widthCells, heightCells, layer);
        error = result.IsSuccess ? "" : FormatPlacementErrors(result.Issues);
        return result.IsSuccess;
    }

    /// <summary>Attempts to move an existing physical placement footprint.</summary>
    public bool TryMovePhysicalComponent(string componentId, int row, int col, out string error, string? layer = null)
    {
        var result = _editor.MovePhysicalComponent(componentId, row, col, layer);
        error = result.IsSuccess ? "" : FormatPlacementErrors(result.Issues);
        return result.IsSuccess;
    }

    /// <summary>Attempts to remove a physical placement footprint.</summary>
    public bool TryRemovePlacement(string componentId, out string error)
    {
        try
        {
            var removed = _editor.RemovePhysicalPlacement(componentId);
            error = removed ? "" : $"Component '{componentId}' has no explicit physical placement.";
            return removed;
        }
        catch (InvalidOperationException ex)
        {
            error = ex.Message;
            return false;
        }
    }

    /// <summary>Removes component from the current model.</summary>
    public void DeleteComponent(string componentId) => _editor.DeleteComponent(componentId);
    /// <summary>Removes selected components and links through one Core editor command.</summary>
    public EditorDeleteResult DeleteSelection(IEnumerable<string> componentIds, IEnumerable<string> linkIds) =>
        _editor.DeleteSelection(componentIds, linkIds);
    /// <summary>Creates a visual group through the Core editor.</summary>
    public string CreateGroup(string groupId, string name, IEnumerable<string> componentIds) =>
        _editor.CreateGroup(groupId, name, componentIds).Id;
    /// <summary>Moves every member of a group as one undoable command.</summary>
    public GroupMoveResult MoveGroupBy(string groupId, int deltaX, int deltaY) =>
        _editor.MoveGroupBy(groupId, deltaX, deltaY);
    /// <summary>Sets a group's visual-only collapsed state.</summary>
    public void SetGroupCollapsed(string groupId, bool collapsed) => _editor.SetGroupCollapsed(groupId, collapsed);
    /// <summary>Previews a group delete operation for UI confirmation.</summary>
    public GroupEditPreview PreviewGroupDelete(string groupId, GroupDeleteMode mode) => _editor.PreviewGroupDelete(groupId, mode);
    /// <summary>Deletes a group with explicit member behavior after UI confirmation.</summary>
    public GroupDeleteResult DeleteGroup(string groupId, GroupDeleteMode mode, bool confirmed) => _editor.DeleteGroup(groupId, mode, confirmed);
    /// <summary>Previews a group copy operation for UI confirmation.</summary>
    public GroupEditPreview PreviewGroupCopy(string groupId, GroupCopyMode mode) => _editor.PreviewGroupCopy(groupId, mode);
    /// <summary>Copies a group with explicit member behavior after UI confirmation.</summary>
    public GroupCopyResult CopyGroup(string groupId, string newGroupId, GroupCopyMode mode, bool confirmed) =>
        _editor.CopyGroup(groupId, newGroupId, mode, confirmed);
    /// <summary>Copies selected components through the Phase 5 remapper.</summary>
    public CopyPasteResult CopyPasteComponents(
        IEnumerable<string> componentIds,
        string suffix,
        int offsetX,
        int offsetY,
        bool includeExternalLinks = false) =>
        _editor.CopyPasteComponents(componentIds, suffix, offsetX, offsetY, includeExternalLinks);
    /// <summary>Creates a reusable macro and folds selected components into one instance.</summary>
    public MacroCreateResult CreateMacroFromSelection(string macroId, string name, IEnumerable<string> componentIds, string instanceId) =>
        _editor.CreateMacroFromSelection(macroId, name, componentIds, instanceId);
    /// <summary>Expands a selected macro instance back into top-level components.</summary>
    public MacroExpandResult ExpandMacroInstance(string instanceId) => _editor.ExpandMacroInstance(instanceId);
    /// <summary>Attempts to update a macro external port mapping and returns validation issues.</summary>
    public MacroMappingValidationResult SetMacroExternalPortMapping(string macroId, string externalPortName, PortRef internalEndpoint) =>
        _editor.SetMacroExternalPortMapping(macroId, externalPortName, internalEndpoint);
    /// <summary>Serializes reusable macro definitions through the Phase 5 library envelope.</summary>
    public string SaveMacroLibraryJson(IEnumerable<string>? macroIds = null) => _editor.SaveMacroLibraryJson(macroIds);
    /// <summary>Imports reusable macro definitions through the Phase 5 library envelope.</summary>
    public MacroLibraryImportResult LoadMacroLibraryJson(string json, bool overwriteExisting = false) =>
        _editor.LoadMacroLibraryJson(json, overwriteExisting);    /// <summary>Selects one component and clears any selected link.</summary>
    public void SelectComponent(string componentId) { _selectedComponentId = componentId; _selectedLinkId = null; }
    /// <summary>Selects one link and clears any selected component.</summary>
    public void SelectLink(string linkId) { _selectedLinkId = linkId; _selectedComponentId = null; }
    /// <summary>Clears the adapter selection used to build inspector snapshots.</summary>
    public void ClearSelection() { _selectedComponentId = null; _selectedLinkId = null; }
    /// <summary>Delegates graph undo to the Core editor.</summary>
    public void Undo() => _editor.Undo();
    /// <summary>Delegates graph redo to the Core editor.</summary>
    public void Redo() => _editor.Redo();
    /// <summary>Serializes the complete editor graph through the production HardwareGraph serializer.</summary>
    public string SaveProjectJson() => _editor.SaveProjectJson();

    /// <summary>Replaces editor state with a graph loaded through the production HardwareGraph serializer.</summary>
    public void LoadProjectJson(string json)
    {
        _editor = HardwareEditor.LoadProjectJson(json);
        LastDefaultCollapsedGroupCount = 0;
        foreach (var group in _editor.Graph.Groups.Where(IsProcessingClusterGroup))
        {
            VisualGroupDefaults.ApplyTo(group);
            group.Collapsed = true;
            LastDefaultCollapsedGroupCount++;
        }

        _selectedComponentId = null;
        _selectedLinkId = null;
    }

    /// <summary>Compiles the current graph through the production compiler and updates shared dirty state.</summary>
    public CompilationResult<HardwareSimulationGraph> CompileHardware(
        DeviceModelRegistry? modelRegistry = null,
        SimulationConfig? simulationConfig = null,
        TraceConfig? traceConfig = null) =>
        new SimulationGraphCompiler().CompileHardware(
            _editor.Graph,
            modelRegistry: modelRegistry,
            simulationConfig: simulationConfig,
            traceConfig: traceConfig,
            dirtyState: _editor.DirtyState,
            componentTemplateLibrary: DefaultComponentTemplateLibraryIfNeeded());

    /// <summary>Attempts to create a validated link and returns an editor-safe error message on failure.</summary>
    public bool TryConnect(string linkId, PortRef source, PortRef destination, out string error)
    {
        try
        {
            _editor.Connect(linkId, source, destination);
            error = "";
            return true;
        }
        catch (InvalidOperationException ex)
        {
            error = ex.Message;
            return false;
        }
    }

    /// <summary>Attempts to update a component model binding and returns an editor-safe error message on failure.</summary>
    public bool TrySetComponentModelRef(string componentId, string? modelRef, out string error) =>
        TryEdit(() => _editor.SetComponentModelRef(componentId, modelRef), out error);

    /// <summary>Attempts to add or replace a component parameter and returns an editor-safe error message on failure.</summary>
    public bool TrySetComponentParameter(string componentId, string key, string value, out string error) =>
        TryEdit(() => _editor.SetComponentParameter(componentId, key, value), out error);

   /// <summary>Attempts to remove a component parameter and returns an editor-safe error message on failure.</summary>
   public bool TryRemoveComponentParameter(string componentId, string key, out string error) =>
       TryEdit(() => _editor.RemoveComponentParameter(componentId, key), out error);

    /// <summary>Attempts to add or replace a component visual-style entry without affecting simulation semantics.</summary>
    public bool TrySetComponentVisualStyle(string componentId, string key, string value, out string error) =>
        TryEdit(() => _editor.SetComponentVisualStyle(componentId, key, value), out error);

    /// <summary>Attempts to remove a component visual-style entry without affecting simulation semantics.</summary>
    public bool TryRemoveComponentVisualStyle(string componentId, string key, out string error) =>
        TryEdit(() => _editor.RemoveComponentVisualStyle(componentId, key), out error);

    /// <summary>Attempts to update a link bandwidth and returns an editor-safe error message on failure.</summary>
    public bool TrySetLinkBandwidth(string linkId, int bandwidthBitsPerCycle, out string error) =>
        TryEdit(() => _editor.SetLinkBandwidth(linkId, bandwidthBitsPerCycle), out error);

    /// <summary>Attempts to update a link latency and returns an editor-safe error message on failure.</summary>
    public bool TrySetLinkLatency(string linkId, int latencyCycles, out string error) =>
        TryEdit(() => _editor.SetLinkLatency(linkId, latencyCycles), out error);

    /// <summary>Attempts to update a link energy-per-bit value and returns an editor-safe error message on failure.</summary>
    public bool TrySetLinkEnergyPerBit(string linkId, double energyPerBit, out string error) =>
        TryEdit(() => _editor.SetLinkEnergyPerBit(linkId, energyPerBit), out error);

    /// <summary>Attempts to update a link model binding and returns an editor-safe error message on failure.</summary>
    public bool TrySetLinkModelRef(string linkId, string? modelRef, out string error) =>
        TryEdit(() => _editor.SetLinkModelRef(linkId, modelRef), out error);

    /// <summary>Attempts to add or replace a link parameter and returns an editor-safe error message on failure.</summary>
    public bool TrySetLinkParameter(string linkId, string key, string value, out string error) =>
        TryEdit(() => _editor.SetLinkParameter(linkId, key, value), out error);

    /// <summary>Attempts to remove a link parameter and returns an editor-safe error message on failure.</summary>
    public bool TryRemoveLinkParameter(string linkId, string key, out string error) =>
        TryEdit(() => _editor.RemoveLinkParameter(linkId, key), out error);

    /// <summary>Projects the physical placement floorplan into immutable Unity view data.</summary>
    public PlacementViewSnapshot PlacementSnapshot()
    {
        var placement = _editor.Graph.Placement ?? new PhysicalPlacement();
        var report = _editor.Graph.Placement is null
            ? new PlacementReport()
            : placement.Validate(_editor.Graph.Components);
        var components = new List<PlacementComponentView>();
        foreach (var component in _editor.Graph.Components.OrderBy(component => component.Id, StringComparer.OrdinalIgnoreCase))
        {
            placement.TryGetCell(component.Id, out var cell);
            placement.TryGetPhysicalPosition(component.Id, out var point);
            var displayMode = cell is null
                ? PhysicalPlacementDisplayMode.ReadableMinimum
                : PhysicalPlacementDisplayMode.PhysicalScale;
            components.Add(new PlacementComponentView(
                component.Id,
                cell?.Row ?? -1,
                cell?.Col ?? -1,
                cell?.WidthCells ?? 0,
                cell?.HeightCells ?? 0,
                cell?.Layer ?? placement.Layer,
                point.X,
                point.Y,
                ParameterDouble(component, "physical_width_um"),
                ParameterDouble(component, "physical_height_um"),
                ParameterDouble(component, "area_um2"),
                ParameterText(component, "physical_footprint_scope"),
                ParameterText(component, "physical_footprint_source_kind"),
                ParameterText(component, "physical_footprint_evidence_status", "Unknown"),
                ParameterText(component, "physical_footprint_uncertainty"),
                ParameterText(component, "physical_footprint_hash"),
                displayMode,
                displayMode == PhysicalPlacementDisplayMode.ReadableMinimum));
        }

        return new PlacementViewSnapshot(
            placement.Rows,
            placement.Cols,
            placement.CellWidthMicrometers,
            placement.CellHeightMicrometers,
            placement.Layer,
            components,
            report.Issues.ToList(),
            $"placement_components={components.Count};issues={report.Issues.Count}");
    }

    private static double? ParameterDouble(HardwareComponent component, string key) =>
        component.Parameters.TryGetValue(key, out var raw) &&
        double.TryParse(raw, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value) &&
        double.IsFinite(value)
            ? value
            : null;

    private static string ParameterText(HardwareComponent component, string key, string fallback = "") =>
        component.Parameters.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : fallback;
    /// <summary>Projects explicit physical routing using the graph's typed congestion capacity contract.</summary>
    public PhysicalRouteEditorSnapshot RoutingSnapshot()
    {
        var linkModel = LinkModelParameters.FromGraphParameters(_editor.Graph.Parameters);
        return new PhysicalRouteEditorAdapter(_editor.Graph.Routing ?? new PhysicalRouting()).Snapshot(
            linkModel.RoutingResourceCellSizeMicrometers,
            linkModel.RoutingResourceDefaultCapacity,
            linkModel.ToRouteResourceCapacityProfile());
    }

    /// <summary>Projects explicit physical routing with an explicit analysis-cell size and uniform capacity.</summary>
    public PhysicalRouteEditorSnapshot RoutingSnapshot(double cellSizeMicrometers) =>
        RoutingSnapshot(cellSizeMicrometers, 1);

    /// <summary>Projects explicit physical routing with an explicit analysis-cell size and uniform capacity.</summary>
    public PhysicalRouteEditorSnapshot RoutingSnapshot(double cellSizeMicrometers, int defaultCellCapacity) =>
        new PhysicalRouteEditorAdapter(_editor.Graph.Routing ?? new PhysicalRouting()).Snapshot(cellSizeMicrometers, defaultCellCapacity);

    /// <summary>Attempts to create an explicit route through the Core editor.</summary>
    public bool TryCreatePhysicalRoute(string linkId, IEnumerable<PhysicalPoint> path, RoutingLayerId? layerId, RoutingMedium medium, out string error) =>
        TryEdit(() => _editor.CreatePhysicalRoute(linkId, path, layerId, medium), out error);

    /// <summary>Attempts to delete an explicit route through the Core editor.</summary>
    public bool TryDeletePhysicalRoute(string linkId, out string error)
    {
        try
        {
            var deleted = _editor.DeletePhysicalRoute(linkId);
            error = deleted ? "" : $"Route for link '{linkId}' does not exist.";
            return deleted;
        }
        catch (InvalidOperationException ex)
        {
            error = ex.Message;
            return false;
        }
    }

    /// <summary>Attempts to append a point to an explicit route through the Core editor.</summary>
    public bool TryAppendPhysicalRoutePoint(string linkId, PhysicalPoint point, out string error) =>
        TryEdit(() => _editor.AppendPhysicalRoutePoint(linkId, point), out error);

    /// <summary>Attempts to insert a point into an explicit route through the Core editor.</summary>
    public bool TryInsertPhysicalRoutePoint(string linkId, int index, PhysicalPoint point, out string error) =>
        TryEdit(() => _editor.InsertPhysicalRoutePoint(linkId, index, point), out error);

    /// <summary>Attempts to move a point in an explicit route through the Core editor.</summary>
    public bool TryMovePhysicalRoutePoint(string linkId, int index, PhysicalPoint point, out string error) =>
        TryEdit(() => _editor.MovePhysicalRoutePoint(linkId, index, point), out error);

    /// <summary>Attempts to remove a point from an explicit route through the Core editor.</summary>
    public bool TryDeletePhysicalRoutePoint(string linkId, int index, out string error) =>
        TryEdit(() => _editor.DeletePhysicalRoutePoint(linkId, index), out error);
    /// <summary>Queries exact physical Manhattan distance between two explicitly placed components.</summary>
    public PlacementDistanceView TryGetPlacementDistance(string sourceComponentId, string destinationComponentId)
    {
        if (_editor.Graph.Placement is not null &&
            _editor.Graph.Placement.TryGetManhattanDistanceMicrometers(sourceComponentId, destinationComponentId, out var distance))
        {
            return new PlacementDistanceView(true, sourceComponentId, destinationComponentId, distance, "ok");
        }

        return new PlacementDistanceView(false, sourceComponentId, destinationComponentId, 0,
            "Both components must have explicit physical placement.");
    }

    /// <summary>Projects the current graph and selection into immutable Unity view data.</summary>
    public EditorSnapshot Snapshot()
    {
        return new EditorSnapshot(
            _editor.Palette
                .Select(PaletteItemViewFor)
                .Where(item => !string.Equals(item.Category, "Optical", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(item.Category, "Interface", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(item.Category, "CIM", StringComparison.OrdinalIgnoreCase))
                .ToList(),
            _editor.Graph.Components.Select(ComponentViewFor).ToList(),
            _editor.Graph.Links.Select(l => new LinkView(
                l.Id,
                l.Source.ComponentId,
                l.Source.PortName,
                l.Destination.ComponentId,
                l.Destination.PortName,
                l.BandwidthBitsPerCycle,
                l.LatencyCycles,
                l.EnergyPerBit,
                l.ModelRef,
                new Dictionary<string, string>(l.Parameters, StringComparer.OrdinalIgnoreCase))).ToList(),
            _editor.Graph.Groups.Select(group => new GroupView(
                group.Id,
                group.Name,
                group.ComponentIds.ToList(),
                group.Collapsed,
                GroupVisualMetadata(group))).ToList(),
            _editor.Graph.Macros.Select(macro => new MacroView(
                macro.Id,
                macro.Name,
                macro.SchemaVersion,
                macro.InternalComponents.Select(component => component.Id).ToList(),
                macro.ExternalPortMappings.ToDictionary(
                    pair => pair.Key,
                    pair => $"{pair.Value.ComponentId}.{pair.Value.PortName}",
                    StringComparer.OrdinalIgnoreCase))).ToList(),
            BuildInspector())
        {
            RegistryPalette = _editor.Palette.Select(PaletteItemViewFor).ToList()
        };
    }

    private ComponentView ComponentViewFor(HardwareComponent component)
    {
        var typeId = ComponentTypeIds.EffectiveTypeId(component);
        var paletteItem = _editor.Palette.FirstOrDefault(item =>
            string.Equals(ComponentTypeIds.Normalize(item.TypeId), ComponentTypeIds.Normalize(typeId), StringComparison.OrdinalIgnoreCase));
        return new ComponentView(
            component.Id,
            component.Name,
            component.Type,
            component.Position.X,
            component.Position.Y,
            component.Ports.Select(PortViewFor).ToList(),
            new Dictionary<string, string>(component.Parameters, StringComparer.OrdinalIgnoreCase),
            component.ModelRef,
            new Dictionary<string, string>(
                component.VisualStyle ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                StringComparer.OrdinalIgnoreCase))
        {
            TypeId = typeId,
            PresentationDisplayName = paletteItem?.DisplayName ?? component.Name,
            PresentationGlyph = paletteItem?.Glyph ?? "component",
            PresentationColorHex = paletteItem?.ColorHex ?? "#8A8F98",
            PresentationAbbreviation = paletteItem?.Abbreviation ?? "CMP",
            PresentationCategory = paletteItem?.Category ?? "Core",
            TemplateId = component.TemplateRef?.TemplateId,
            TemplateVersion = component.TemplateRef?.Version,
            CompiledProfileHash = component.TemplateRef?.CompiledProfileHash
        };
    }

    private static PaletteItemView PaletteItemViewFor(PaletteItem item) => new(item.Kind, item.DisplayName, item.Description)
    {
        TypeId = item.TypeId,
        Category = item.Category,
        Glyph = item.Glyph,
        ColorHex = item.ColorHex,
        Abbreviation = item.Abbreviation,
        SortOrder = item.SortOrder
    };

    private static PortView PortViewFor(HardwarePort port) =>
        new(port.Name, port.Direction, port.SignalType, port.DataType, port.Precision, port.Protocol)
        {
            Quantity = PortExtensionText(port, "quantity"),
            Units = PortExtensionText(port, "units"),
            BandwidthBitsPerCycle = port.BandwidthBitsPerCycle,
            LatencyCycles = port.LatencyCycles,
            ClockDomain = port.ClockDomain,
            Required = port.Required,
            MultiConnect = port.MultiConnect
        };

    private static string PortExtensionText(HardwarePort port, string key)
    {
        if (!port.ExtensionData.TryGetValue(key, out var value))
        {
            return "";
        }

        return value.ValueKind == System.Text.Json.JsonValueKind.String
            ? value.GetString() ?? ""
            : value.ToString();
    }

    private ComponentTemplateLibrary? DefaultComponentTemplateLibraryIfNeeded() => _editor.Graph.Components.Any(component => component.TemplateRef is not null)
        ? _componentTemplateLibrary
        : null;
    private static bool IsProcessingClusterGroup(VisualGroup group) =>
        group.VisualMetadata is not null &&
        group.VisualMetadata.TryGetValue("role", out var role) &&
        string.Equals(role, "processing-cluster", StringComparison.OrdinalIgnoreCase);
    private static IReadOnlyDictionary<string, string> GroupVisualMetadata(VisualGroup group)
    {
        var metadata = VisualGroupDefaults.CreateMetadata();
        foreach (var pair in group.VisualMetadata ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase))
        {
            metadata[pair.Key] = pair.Value;
        }

        return metadata;
    }
    private static string FormatPlacementErrors(IReadOnlyList<PlacementIssue> issues) =>
        issues.Count == 0
            ? "Placement edit was rejected."
            : string.Join("; ", issues.Select(issue => $"{issue.Code}:{issue.Message}"));

    private InspectorView? BuildInspector()
    {
        if (_selectedComponentId is not null && _editor.Graph.FindComponent(_selectedComponentId) is { } component)
        {
            var fields = new List<InspectorField>
            {
                new("id", component.Id),
                new("name", component.Name),
                new("type", component.Type.ToString()),
                new("position", $"{component.Position.X},{component.Position.Y}"),
                new("model_ref", component.ModelRef ?? "")
            };
            if (component.TemplateRef is not null)
            {
                fields.Add(new InspectorField("template_id", component.TemplateRef.TemplateId));
                fields.Add(new InspectorField("template_version", component.TemplateRef.Version));
                fields.Add(new InspectorField("compiled_profile_hash", component.TemplateRef.CompiledProfileHash ?? ""));
            }
            if (component.Parameters.ContainsKey("phase9_profile"))
            {
                fields.Add(new InspectorField("capability", ParameterText(component, "phase9_capability", "unknown")));
                fields.Add(new InspectorField("array", ParameterText(component, "phase9_array", "unknown")));
                fields.Add(new InspectorField("precision", ParameterText(component, "phase9_precision", "unknown")));
                fields.Add(new InspectorField("storage", ParameterText(component, "phase9_storage", "unknown")));
                fields.Add(new InspectorField("mode", ParameterText(component, "phase9_mode", "unknown")));
                fields.Add(new InspectorField("profile", ParameterText(component, "phase9_profile", "unknown")));
                fields.Add(new InspectorField("valid_range", ParameterText(component, "phase9_valid_range", "unknown")));
                fields.Add(new InspectorField("evidence", ParameterText(component, "phase9_evidence", "unknown")));
                fields.Add(new InspectorField("evidence_gaps", ParameterText(component, "phase9_evidence_gaps", "none")));
                fields.Add(new InspectorField("footprint",
                    ParameterText(component, "physical_width_um", "?") + "um x " +
                    ParameterText(component, "physical_height_um", "?") + "um; " +
                    ParameterText(component, "physical_footprint_evidence_status", "Unknown") + "/" +
                    ParameterText(component, "physical_footprint_source_kind", "Unknown")));
            }
            fields.AddRange(component.Parameters.Select(p => new InspectorField($"parameter:{p.Key}", p.Value)));
            return new InspectorView(component.Id, fields);
        }

        if (_selectedLinkId is not null && _editor.Graph.Links.FirstOrDefault(l => l.Id == _selectedLinkId) is { } link)
        {
            var fields = new List<InspectorField>
            {
                new("id", link.Id),
                new("source", $"{link.Source.ComponentId}.{link.Source.PortName}"),
                new("destination", $"{link.Destination.ComponentId}.{link.Destination.PortName}"),
                new("bandwidth_bits_per_cycle", link.BandwidthBitsPerCycle.ToString()),
                new("latency_cycles", link.LatencyCycles.ToString()),
                new("energy_per_bit", link.EnergyPerBit.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                new("model_ref", link.ModelRef ?? "")
            };
            fields.AddRange(link.Parameters.Select(p => new InspectorField($"parameter:{p.Key}", p.Value)));
            return new InspectorView(link.Id, fields);
        }

        return null;
    }

    private static bool TryEdit(Action edit, out string error)
    {
        try
        {
            edit();
            error = "";
            return true;
        }
        catch (ArgumentException ex)
        {
            error = ex.Message;
            return false;
        }
        catch (InvalidOperationException ex)
        {
            error = ex.Message;
            return false;
        }
    }
}

/// <summary>Represents replay snapshot data exchanged by hardware design and simulation workflows.</summary>
/// <param name="CurrentCycle">Provides the current cycle value carried by this contract.</param>
/// <param name="LastCycle">Provides the last cycle value carried by this contract.</param>
/// <param name="State">Provides the state value carried by this contract.</param>
/// <param name="Events">Provides the events value carried by this contract.</param>
/// <param name="Phase9Facts">Provides typed Phase 9 CIM facts classified independently from compatibility shell events.</param>
public sealed record ReplaySnapshot(
    long CurrentCycle,
    long LastCycle,
    ReplayState State,
    IReadOnlyList<TraceEvent> Events,
    IReadOnlyList<Phase9ReplayFactView>? Phase9Facts = null);

/// <summary>Stable Phase 9 replay categories shown independently from shell compatibility events.</summary>
public enum Phase9ReplayFactCategory
{
    /// <summary>Analog-domain operation or MVM activity.</summary>
    AnalogOperation,
    /// <summary>ADC or DAC conversion activity.</summary>
    Conversion,
    /// <summary>Array compute or read activity.</summary>
    ArrayCompute,
    /// <summary>Weight write, commit, or update activity.</summary>
    WriteUpdate,
    /// <summary>Enabled non-ideal effect activity.</summary>
    NonIdeal,
    /// <summary>Compatibility shell summary activity.</summary>
    ShellSummary
}

/// <summary>One typed Phase 9 replay fact projected from a trace event.</summary>
public sealed record Phase9ReplayFactView(
    Phase9ReplayFactCategory Category,
    string Label,
    string Detail,
    string ComponentId,
    string PacketId);
/// <summary>Represents analysis component view data exchanged by hardware design and simulation workflows.</summary>
/// <param name="ComponentId">Provides the component id value carried by this contract.</param>
/// <param name="TrafficBits">Provides the traffic bits value carried by this contract.</param>
/// <param name="ActiveCycles">Provides the active cycles value carried by this contract.</param>
/// <param name="StallCycles">Provides the stall cycles value carried by this contract.</param>
/// <param name="DominantStallReason">Provides the dominant stall reason value carried by this contract.</param>
/// <param name="Heat">Provides the heat value carried by this contract.</param>
public sealed record AnalysisComponentView(string ComponentId, long TrafficBits, long ActiveCycles, long StallCycles, string DominantStallReason, double Heat);
/// <summary>Represents analysis link view data exchanged by hardware design and simulation workflows.</summary>
/// <param name="LinkId">Provides the link id value carried by this contract.</param>
/// <param name="TransferredBits">Provides the transferred bits value carried by this contract.</param>
/// <param name="CongestionCycles">Provides the congestion cycles value carried by this contract.</param>
/// <param name="Heat">Provides the heat value carried by this contract.</param>
public sealed record AnalysisLinkView(string LinkId, long TransferredBits, long CongestionCycles, double Heat);
/// <summary>Represents analysis timeline bin view data exchanged by hardware design and simulation workflows.</summary>
/// <param name="StartCycle">Provides the start cycle value carried by this contract.</param>
/// <param name="EndCycle">Provides the end cycle value carried by this contract.</param>
/// <param name="EventCount">Provides the event count value carried by this contract.</param>
/// <param name="EventCountsByType">Provides the event counts by type value carried by this contract.</param>
public sealed record AnalysisTimelineBinView(long StartCycle, long EndCycle, int EventCount, IReadOnlyDictionary<TraceEventType, int> EventCountsByType);
/// <summary>Represents analysis adapter runtime view data exchanged by hardware design and simulation workflows.</summary>
/// <param name="ComponentId">Provides the component id value carried by this contract.</param>
/// <param name="ComponentType">Provides the component type value carried by this contract.</param>
/// <param name="AdapterType">Provides the adapter type value carried by this contract.</param>
/// <param name="MismatchField">Provides the mismatch field value carried by this contract.</param>
/// <param name="SourceValue">Provides the source value carried by this contract.</param>
/// <param name="DestinationValue">Provides the destination value carried by this contract.</param>
/// <param name="ActiveCycles">Provides the active cycles value carried by this contract.</param>
/// <param name="InputTrafficBits">Provides the input traffic bits value carried by this contract.</param>
/// <param name="OutputTrafficBits">Provides the output traffic bits value carried by this contract.</param>
/// <param name="Energy">Provides the energy value carried by this contract.</param>
/// <param name="PassThroughEventCount">Provides the pass through event count value carried by this contract.</param>
/// <param name="PrecisionConversionEventCount">Provides the precision conversion event count value carried by this contract.</param>
/// <param name="DisplayLabel">Provides the display label value carried by this contract.</param>
/// <param name="Detail">Provides the detail value carried by this contract.</param>
public sealed record AnalysisAdapterRuntimeView(
    string ComponentId,
    string ComponentType,
    string AdapterType,
    string MismatchField,
    string SourceValue,
    string DestinationValue,
    long ActiveCycles,
    long InputTrafficBits,
    long OutputTrafficBits,
    double Energy,
    int PassThroughEventCount,
    int PrecisionConversionEventCount,
    string DisplayLabel,
    string Detail);
/// <summary>Represents replay analysis view snapshot data exchanged by hardware design and simulation workflows.</summary>
/// <param name="Components">Provides the components value carried by this contract.</param>
/// <param name="Links">Provides the links value carried by this contract.</param>
/// <param name="Timeline">Provides the timeline value carried by this contract.</param>
/// <param name="AdapterRuntime">Provides the adapter runtime value carried by this contract.</param>
/// <param name="Summary">Provides the summary value carried by this contract.</param>
public sealed record ReplayAnalysisViewSnapshot(
    IReadOnlyList<AnalysisComponentView> Components,
    IReadOnlyList<AnalysisLinkView> Links,
    IReadOnlyList<AnalysisTimelineBinView> Timeline,
    IReadOnlyList<AnalysisAdapterRuntimeView> AdapterRuntime,
    string Summary);
/// <summary>Represents adapter runtime rollup data exchanged by hardware design and simulation workflows.</summary>
/// <param name="TotalRows">Provides the total rows value carried by this contract.</param>
/// <param name="PassThroughRows">Provides the pass through rows value carried by this contract.</param>
/// <param name="PrecisionConversionRows">Provides the precision conversion rows value carried by this contract.</param>
/// <param name="TotalInputTrafficBits">Provides the total input traffic bits value carried by this contract.</param>
/// <param name="TotalOutputTrafficBits">Provides the total output traffic bits value carried by this contract.</param>
/// <param name="TotalEnergy">Provides the total energy value carried by this contract.</param>
/// <param name="OpticalAdapterRows">Provides the optical adapter rows value carried by this contract.</param>
public sealed record AdapterRuntimeRollup(
    int TotalRows,
    int PassThroughRows,
    int PrecisionConversionRows,
    long TotalInputTrafficBits,
    long TotalOutputTrafficBits,
    double TotalEnergy,
    int OpticalAdapterRows);

/// <summary>Provides adapter runtime report renderer operations for hardware design and simulation workflows.</summary>
public static class AdapterRuntimeReportRenderer
{
    /// <summary>Aggregates adapter count, cycles, energy, and traffic across report rows.</summary>
    public static AdapterRuntimeRollup BuildRollup(IEnumerable<AnalysisAdapterRuntimeView> rows)
    {
        var rowList = rows.ToList();
        return new AdapterRuntimeRollup(
            rowList.Count,
            rowList.Count(row => row.PassThroughEventCount > 0),
            rowList.Count(row => row.PrecisionConversionEventCount > 0),
            rowList.Sum(row => row.InputTrafficBits),
            rowList.Sum(row => row.OutputTrafficBits),
            rowList.Sum(row => row.Energy),
            rowList.Count(row =>
                row.ComponentType.Contains("Oe", StringComparison.OrdinalIgnoreCase) ||
                row.ComponentType.Contains("Eo", StringComparison.OrdinalIgnoreCase) ||
                row.AdapterType.Contains("O/E", StringComparison.OrdinalIgnoreCase) ||
                row.AdapterType.Contains("E/O", StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>Builds and returns the render markdown text representation from the supplied inputs.</summary>
    public static string RenderMarkdown(IEnumerable<AnalysisAdapterRuntimeView> rows)
    {
        var rowList = rows.ToList();
        var rollup = BuildRollup(rowList);
        var lines = new List<string>
        {
            "# Adapter Runtime Report",
            "",
            $"- total_rows: {rollup.TotalRows}",
            $"- pass_through_rows: {rollup.PassThroughRows}",
            $"- precision_conversion_rows: {rollup.PrecisionConversionRows}",
            "",
            "## Rows"
        };

        lines.AddRange(rowList.Select(row => $"- {row.DisplayLabel}: {row.Detail}"));
        return string.Join(Environment.NewLine, lines) + Environment.NewLine;
    }

    /// <summary>Builds and returns the render overhead markdown text representation from the supplied inputs.</summary>
    public static string RenderOverheadMarkdown(IEnumerable<AnalysisAdapterRuntimeView> rows)
    {
        var rowList = rows.ToList();
        var rollup = BuildRollup(rowList);
        var highestEnergyRow = rowList
            .OrderByDescending(row => row.Energy)
            .ThenBy(row => row.ComponentId, StringComparer.Ordinal)
            .FirstOrDefault();
        var energy = rollup.TotalEnergy.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        var lines = new List<string>
        {
            "# Adapter Runtime Overhead Report",
            "",
            $"- total_energy_pj: {energy}",
            $"- total_input_traffic_bits: {rollup.TotalInputTrafficBits}",
            $"- total_output_traffic_bits: {rollup.TotalOutputTrafficBits}",
            $"- pass_through_rows: {rollup.PassThroughRows}",
            $"- precision_conversion_rows: {rollup.PrecisionConversionRows}",
            $"- optical_adapter_rows: {rollup.OpticalAdapterRows}",
            "",
            "## Highest Energy Adapter"
        };

        if (highestEnergyRow is null)
        {
            lines.Add("- none");
        }
        else
        {
            var rowEnergy = highestEnergyRow.Energy.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
            lines.Add($"- {highestEnergyRow.DisplayLabel}: energy_pj={rowEnergy}; {highestEnergyRow.Detail}");
        }

        return string.Join(Environment.NewLine, lines) + Environment.NewLine;
    }
}
/// <summary>Represents route grid cell view data exchanged by hardware design and simulation workflows.</summary>
/// <param name="X">Provides the x value carried by this contract.</param>
/// <param name="Y">Provides the y value carried by this contract.</param>
/// <param name="Layer">Provides the layer value carried by this contract.</param>
/// <param name="Occupancy">Provides the occupancy value carried by this contract.</param>
/// <param name="Capacity">Provides the capacity value carried by this contract.</param>
/// <param name="OverCapacityBy">Provides the over capacity by value carried by this contract.</param>
/// <param name="IsCongested">Provides the is congested value carried by this contract.</param>
/// <param name="LinkIds">Provides the link ids value carried by this contract.</param>
public sealed record RouteGridCellView(int X, int Y, string Layer, int Occupancy, int Capacity, int OverCapacityBy, bool IsCongested, IReadOnlyList<string> LinkIds);
/// <summary>Represents route grid congestion view snapshot data exchanged by hardware design and simulation workflows.</summary>
/// <param name="Cells">Provides the cells value carried by this contract.</param>
/// <param name="CongestedCells">Provides the congested cells value carried by this contract.</param>
/// <param name="Summary">Provides the summary value carried by this contract.</param>
public sealed record RouteGridCongestionViewSnapshot(
    IReadOnlyList<RouteGridCellView> Cells,
    IReadOnlyList<RouteGridCellView> CongestedCells,
    string Summary)
{
    /// <summary>Gets the physical size represented by one congestion-analysis cell.</summary>
    public double CellSizeMicrometers { get; init; } = 100;
}
/// <summary>Represents exact route resource view data exchanged by hardware design and simulation workflows.</summary>
public sealed record RouteResourceView(
    string EdgeId,
    RouteResourceDirection Direction,
    string Layer,
    RoutingMedium Medium,
    int Occupancy,
    int Capacity,
    int OverCapacityBy,
    bool IsCongested,
    IReadOnlyList<string> LinkIds,
    IReadOnlyList<string> OverCapacityLinkIds,
    string Evidence)
{
    /// <summary>Gets the exact analysis-grid X coordinate at the canonical edge start.</summary>
    public int EdgeStartX { get; init; }
    /// <summary>Gets the exact analysis-grid Y coordinate at the canonical edge start.</summary>
    public int EdgeStartY { get; init; }
    /// <summary>Gets the exact analysis-grid X coordinate at the canonical edge end.</summary>
    public int EdgeEndX { get; init; }
    /// <summary>Gets the exact analysis-grid Y coordinate at the canonical edge end.</summary>
    public int EdgeEndY { get; init; }
}
/// <summary>Represents exact route resource congestion view snapshot data exchanged by hardware design and simulation workflows.</summary>
public sealed record RouteResourceCongestionViewSnapshot(
    IReadOnlyList<RouteResourceView> Resources,
    IReadOnlyList<RouteResourceView> CongestedResources,
    string Summary)
{
    /// <summary>Gets the physical size represented by one exact-resource analysis cell.</summary>
    public double CellSizeMicrometers { get; init; } = 100;
}
/// <summary>Represents physical route point view data exchanged by hardware design and simulation workflows.</summary>
/// <param name="X">Provides the x value carried by this contract.</param>
/// <param name="Y">Provides the y value carried by this contract.</param>
public sealed record PhysicalRoutePointView(double X, double Y);
/// <summary>Represents physical route view data exchanged by hardware design and simulation workflows.</summary>
/// <param name="LinkId">Provides the link id value carried by this contract.</param>
/// <param name="Layer">Provides the legacy compact layer value carried by this contract.</param>
/// <param name="RouteType">Provides the legacy route type value carried by this contract.</param>
/// <param name="Medium">Provides the structured routing medium value carried by this contract.</param>
/// <param name="LayerId">Provides the structured routing layer id value carried by this contract.</param>
/// <param name="PathUnit">Provides the route point unit value carried by this contract.</param>
/// <param name="LengthMicrometers">Provides the explicit route path length in micrometers.</param>
/// <param name="BendCount">Provides the explicit route path bend count.</param>
/// <param name="Points">Provides the points value carried by this contract.</param>
public sealed record PhysicalRouteView(
    string LinkId,
    string Layer,
    string RouteType,
    RoutingMedium Medium,
    RoutingLayerId LayerId,
    PhysicalRoutePointUnit PathUnit,
    double LengthMicrometers,
    int BendCount,
    IReadOnlyList<PhysicalRoutePointView> Points);
/// <summary>Represents physical route editor snapshot data exchanged by hardware design and simulation workflows.</summary>
/// <param name="Routes">Provides the routes value carried by this contract.</param>
/// <param name="Congestion">Provides cell summary congestion used as a visual overview.</param>
/// <param name="ResourceCongestion">Provides exact edge, direction, layer, and medium resource congestion.</param>
/// <param name="Summary">Provides the summary value carried by this contract.</param>
public sealed record PhysicalRouteEditorSnapshot(
    IReadOnlyList<PhysicalRouteView> Routes,
    RouteGridCongestionViewSnapshot Congestion,
    RouteResourceCongestionViewSnapshot ResourceCongestion,
    string Summary);

/// <summary>Represents one floorplan-clipped route heatmap cell in placement-grid coordinates.</summary>
public sealed record RouteGridHeatmapCellView(
    int X,
    int Y,
    string Layer,
    RoutingMedium Medium,
    double LeftPlacementCells,
    double TopPlacementCells,
    double WidthPlacementCells,
    double HeightPlacementCells,
    int Occupancy,
    int Capacity,
    int OverCapacityBy,
    bool IsCongested,
    IReadOnlyList<string> LinkIds)
{
    /// <summary>Gets whether this view is an exact edge/direction/layer/medium congestion hotspot.</summary>
    public bool IsExactResourceHotspot { get; init; }
    /// <summary>Gets the exact route-resource edge id when this view is a hotspot.</summary>
    public string ResourceEdgeId { get; init; } = "";
    /// <summary>Gets the exact route-resource direction when this view is a hotspot.</summary>
    public RouteResourceDirection? ResourceDirection { get; init; }
    /// <summary>Gets the exact resource evidence used by reports and the Unity tooltip.</summary>
    public string Evidence { get; init; } = "";
}

/// <summary>Identifies the typed Route view bucket used for an aggregated placement-cell hotspot.</summary>
public enum RouteHotspotViewBucket
{
    /// <summary>Contains electrical-metal and control routing resources.</summary>
    ElectricalControl,
    /// <summary>Contains optical-waveguide routing resources.</summary>
    Optical
}

/// <summary>Represents exact route resources aggregated into one visible placement-grid cell.</summary>
/// <param name="Row">Provides the visible placement row.</param>
/// <param name="Col">Provides the visible placement column.</param>
/// <param name="ViewBucket">Provides the typed electrical/control or optical view bucket.</param>
/// <param name="Resources">Provides the complete, deterministically ordered exact resources assigned to the cell.</param>
/// <param name="LinkIds">Provides the distinct, deterministically ordered links represented by the cell.</param>
/// <param name="MaxOverCapacityBy">Provides the maximum exact-resource over-capacity amount without summing unrelated capacities.</param>
/// <param name="MaxUtilization">Provides the maximum exact-resource occupancy-to-capacity ratio.</param>
public sealed record RouteResourcePlacementHotspotCellView(
    int Row,
    int Col,
    RouteHotspotViewBucket ViewBucket,
    IReadOnlyList<RouteResourceView> Resources,
    IReadOnlyList<string> LinkIds,
    int MaxOverCapacityBy,
    double MaxUtilization);

/// <summary>Projects physical congestion cells onto a bounded placement floorplan.</summary>
public static class RouteGridHeatmapProjection
{
    private sealed record OwnedRouteResource(RouteResourceView Resource, int Row, int Col);

    /// <summary>
    /// Converts analysis-grid cells to placement-grid coordinates, filters by typed route medium,
    /// and clips cells that extend beyond the displayed floorplan.
    /// </summary>
    public static IReadOnlyList<RouteGridHeatmapCellView> Project(
        PhysicalRouteEditorSnapshot routing,
        PlacementViewSnapshot placement,
        int displayRows,
        int displayCols,
        bool showOptical)
    {
        var analysisCellMicrometers = Math.Max(1d, routing.ResourceCongestion.CellSizeMicrometers);
        var placementCellWidthMicrometers = placement.CellWidthMicrometers > 0d
            ? placement.CellWidthMicrometers
            : analysisCellMicrometers;
        var placementCellHeightMicrometers = placement.CellHeightMicrometers > 0d
            ? placement.CellHeightMicrometers
            : placementCellWidthMicrometers;
        var maxX = Math.Max(0, displayCols);
        var maxY = Math.Max(0, displayRows);
        var routesById = routing.Routes
            .GroupBy(route => route.LinkId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var projected = new List<RouteGridHeatmapCellView>();

        foreach (var cell in routing.Congestion.Cells)
        {
            var visibleRoutes = cell.LinkIds
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(linkId => routesById.TryGetValue(linkId, out var route) ? route : null)
                .Where(route => route is not null &&
                    (route.Medium == RoutingMedium.OpticalWaveguide) == showOptical)
                .Cast<PhysicalRouteView>()
                .GroupBy(route => route.Medium)
                .OrderBy(group => group.Key);

            foreach (var mediumGroup in visibleRoutes)
            {
                var unboundedLeft = cell.X * analysisCellMicrometers / placementCellWidthMicrometers;
                var unboundedRight = (cell.X + 1d) * analysisCellMicrometers / placementCellWidthMicrometers;
                var unboundedTop = cell.Y * analysisCellMicrometers / placementCellHeightMicrometers;
                var unboundedBottom = (cell.Y + 1d) * analysisCellMicrometers / placementCellHeightMicrometers;
                var left = Math.Max(0d, Math.Min(maxX, unboundedLeft));
                var right = Math.Max(0d, Math.Min(maxX, unboundedRight));
                var top = Math.Max(0d, Math.Min(maxY, unboundedTop));
                var bottom = Math.Max(0d, Math.Min(maxY, unboundedBottom));
                if (right <= left || bottom <= top)
                {
                    continue;
                }

                var linkIds = mediumGroup
                    .Select(route => route.LinkId)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(linkId => linkId, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                var occupancy = linkIds.Count;
                var capacity = Math.Max(1, cell.Capacity);
                var overCapacityBy = Math.Max(0, occupancy - capacity);
                projected.Add(new RouteGridHeatmapCellView(
                    cell.X,
                    cell.Y,
                    cell.Layer,
                    mediumGroup.Key,
                    left,
                    top,
                    right - left,
                    bottom - top,
                    occupancy,
                    capacity,
                    overCapacityBy,
                    overCapacityBy > 0,
                    linkIds));
            }
        }

        var horizontalScale = analysisCellMicrometers / placementCellWidthMicrometers;
        var verticalScale = analysisCellMicrometers / placementCellHeightMicrometers;
        foreach (var resource in routing.ResourceCongestion.CongestedResources
            .Where(resource => (resource.Medium == RoutingMedium.OpticalWaveguide) == showOptical))
        {
            // Fill a one-cell-wide analysis-grid corridor around the exact edge. Adjacent resource
            // edges meet at their cell centers without overlapping and artificially darkening cells.
            var horizontal = resource.EdgeStartY == resource.EdgeEndY;
            var unboundedLeft = horizontal
                ? (Math.Min(resource.EdgeStartX, resource.EdgeEndX) + 0.5d) * horizontalScale
                : resource.EdgeStartX * horizontalScale;
            var unboundedRight = horizontal
                ? (Math.Max(resource.EdgeStartX, resource.EdgeEndX) + 0.5d) * horizontalScale
                : (resource.EdgeStartX + 1d) * horizontalScale;
            var unboundedTop = horizontal
                ? resource.EdgeStartY * verticalScale
                : (Math.Min(resource.EdgeStartY, resource.EdgeEndY) + 0.5d) * verticalScale;
            var unboundedBottom = horizontal
                ? (resource.EdgeStartY + 1d) * verticalScale
                : (Math.Max(resource.EdgeStartY, resource.EdgeEndY) + 0.5d) * verticalScale;

            var left = Math.Max(0d, Math.Min(maxX, unboundedLeft));
            var right = Math.Max(0d, Math.Min(maxX, unboundedRight));
            var top = Math.Max(0d, Math.Min(maxY, unboundedTop));
            var bottom = Math.Max(0d, Math.Min(maxY, unboundedBottom));
            if (right <= left || bottom <= top)
            {
                continue;
            }

            projected.Add(new RouteGridHeatmapCellView(
                resource.EdgeStartX,
                resource.EdgeStartY,
                resource.Layer,
                resource.Medium,
                left,
                top,
                right - left,
                bottom - top,
                resource.Occupancy,
                resource.Capacity,
                resource.OverCapacityBy,
                resource.IsCongested,
                resource.LinkIds)
            {
                IsExactResourceHotspot = true,
                ResourceEdgeId = resource.EdgeId,
                ResourceDirection = resource.Direction,
                Evidence = resource.Evidence
            });
        }

        return projected
            .OrderBy(cell => cell.IsExactResourceHotspot)
            .ThenBy(cell => cell.IsCongested)
            .ThenBy(cell => cell.Medium)
            .ThenBy(cell => cell.Layer, StringComparer.Ordinal)
            .ThenBy(cell => cell.Y)
            .ThenBy(cell => cell.X)
            .ThenBy(cell => cell.ResourceEdgeId, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// Assigns each exact resource to one visible placement cell, then aggregates resources so
    /// every placement cell is rendered once without analysis-cell scaling or alpha overlap.
    /// </summary>
    public static IReadOnlyList<RouteResourcePlacementHotspotCellView> ProjectExactPlacementCells(
        PhysicalRouteEditorSnapshot routing,
        PlacementViewSnapshot placement,
        int displayRows,
        int displayCols,
        bool showOptical)
    {
        var rows = Math.Max(0, displayRows);
        var cols = Math.Max(0, displayCols);
        if (rows == 0 || cols == 0)
        {
            return Array.Empty<RouteResourcePlacementHotspotCellView>();
        }

        var analysisCellMicrometers = Math.Max(1d, routing.ResourceCongestion.CellSizeMicrometers);
        var placementCellWidthMicrometers = placement.CellWidthMicrometers > 0d
            ? placement.CellWidthMicrometers
            : analysisCellMicrometers;
        var placementCellHeightMicrometers = placement.CellHeightMicrometers > 0d
            ? placement.CellHeightMicrometers
            : placementCellWidthMicrometers;
        var owned = new List<OwnedRouteResource>();
        foreach (var resource in routing.ResourceCongestion.CongestedResources
            .Where(resource => (resource.Medium == RoutingMedium.OpticalWaveguide) == showOptical))
        {
            if (TryFindPlacementCellOwner(
                resource,
                analysisCellMicrometers,
                placementCellWidthMicrometers,
                placementCellHeightMicrometers,
                rows,
                cols,
                out var row,
                out var col))
            {
                owned.Add(new OwnedRouteResource(resource, row, col));
            }
        }

        var bucket = showOptical
            ? RouteHotspotViewBucket.Optical
            : RouteHotspotViewBucket.ElectricalControl;
        return owned
            .GroupBy(item => (item.Row, item.Col))
            .OrderBy(group => group.Key.Row)
            .ThenBy(group => group.Key.Col)
            .Select(group =>
            {
                var resources = group
                    .Select(item => item.Resource)
                    .OrderBy(resource => resource.Medium)
                    .ThenBy(resource => resource.Layer, StringComparer.Ordinal)
                    .ThenBy(resource => resource.EdgeStartY)
                    .ThenBy(resource => resource.EdgeStartX)
                    .ThenBy(resource => resource.EdgeEndY)
                    .ThenBy(resource => resource.EdgeEndX)
                    .ThenBy(resource => resource.Direction)
                    .ToList();
                var linkIds = resources
                    .SelectMany(resource => resource.LinkIds)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(linkId => linkId, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                return new RouteResourcePlacementHotspotCellView(
                    group.Key.Row,
                    group.Key.Col,
                    bucket,
                    resources,
                    linkIds,
                    resources.Max(resource => resource.OverCapacityBy),
                    resources.Max(resource => resource.Occupancy / (double)Math.Max(1, resource.Capacity)));
            })
            .ToList();
    }

    private static bool TryFindPlacementCellOwner(
        RouteResourceView resource,
        double analysisCellMicrometers,
        double placementCellWidthMicrometers,
        double placementCellHeightMicrometers,
        int rows,
        int cols,
        out int ownerRow,
        out int ownerCol)
    {
        ownerRow = -1;
        ownerCol = -1;
        var horizontal = resource.EdgeStartY == resource.EdgeEndY;
        var corridorLeft = horizontal
            ? (Math.Min(resource.EdgeStartX, resource.EdgeEndX) + 0.5d) * analysisCellMicrometers
            : resource.EdgeStartX * analysisCellMicrometers;
        var corridorRight = horizontal
            ? (Math.Max(resource.EdgeStartX, resource.EdgeEndX) + 0.5d) * analysisCellMicrometers
            : (resource.EdgeStartX + 1d) * analysisCellMicrometers;
        var corridorTop = horizontal
            ? resource.EdgeStartY * analysisCellMicrometers
            : (Math.Min(resource.EdgeStartY, resource.EdgeEndY) + 0.5d) * analysisCellMicrometers;
        var corridorBottom = horizontal
            ? (resource.EdgeStartY + 1d) * analysisCellMicrometers
            : (Math.Max(resource.EdgeStartY, resource.EdgeEndY) + 0.5d) * analysisCellMicrometers;

        var clippedLeft = Math.Max(0d, corridorLeft);
        var clippedRight = Math.Min(cols * placementCellWidthMicrometers, corridorRight);
        var clippedTop = Math.Max(0d, corridorTop);
        var clippedBottom = Math.Min(rows * placementCellHeightMicrometers, corridorBottom);
        if (clippedRight <= clippedLeft || clippedBottom <= clippedTop)
        {
            return false;
        }

        var firstCol = Math.Max(0, (int)Math.Floor(clippedLeft / placementCellWidthMicrometers));
        var lastCol = Math.Min(cols - 1, (int)Math.Ceiling(clippedRight / placementCellWidthMicrometers) - 1);
        var firstRow = Math.Max(0, (int)Math.Floor(clippedTop / placementCellHeightMicrometers));
        var lastRow = Math.Min(rows - 1, (int)Math.Ceiling(clippedBottom / placementCellHeightMicrometers) - 1);
        var midpointCol = (int)Math.Floor(((corridorLeft + corridorRight) * 0.5d) / placementCellWidthMicrometers);
        var midpointRow = (int)Math.Floor(((corridorTop + corridorBottom) * 0.5d) / placementCellHeightMicrometers);
        var bestArea = -1d;
        var bestContainsMidpoint = false;
        for (var row = firstRow; row <= lastRow; row++)
        {
            var cellTop = row * placementCellHeightMicrometers;
            var cellBottom = cellTop + placementCellHeightMicrometers;
            for (var col = firstCol; col <= lastCol; col++)
            {
                var cellLeft = col * placementCellWidthMicrometers;
                var cellRight = cellLeft + placementCellWidthMicrometers;
                var overlapWidth = Math.Max(0d, Math.Min(clippedRight, cellRight) - Math.Max(clippedLeft, cellLeft));
                var overlapHeight = Math.Max(0d, Math.Min(clippedBottom, cellBottom) - Math.Max(clippedTop, cellTop));
                var overlapArea = overlapWidth * overlapHeight;
                if (overlapArea <= 0d)
                {
                    continue;
                }

                var containsMidpoint = row == midpointRow && col == midpointCol;
                var winsByArea = overlapArea > bestArea + 0.000000001d;
                var tiesArea = Math.Abs(overlapArea - bestArea) <= 0.000000001d;
                var winsTie = tiesArea && containsMidpoint && !bestContainsMidpoint;
                if (!winsByArea && !winsTie)
                {
                    continue;
                }

                bestArea = overlapArea;
                bestContainsMidpoint = containsMidpoint;
                ownerRow = row;
                ownerCol = col;
            }
        }

        return ownerRow >= 0 && ownerCol >= 0;
    }
}

/// <summary>Classifies CIM replay facts without changing approved runtime trace events or their hashes.</summary>
public static class Phase9ReplayFactFormatter
{
    /// <summary>Returns a typed CIM fact, or null for unrelated events.</summary>
    public static Phase9ReplayFactView? Format(TraceEvent? traceEvent)
    {
        if (traceEvent is null || string.IsNullOrWhiteSpace(traceEvent.Detail))
        {
            return null;
        }

        var detail = traceEvent.Detail;
        var normalized = detail.ToLowerInvariant();
        Phase9ReplayFactCategory? category = normalized switch
        {
            var value when value.Contains("phase9.cim.nonideal", StringComparison.Ordinal) ||
                value.Contains("nonideal", StringComparison.Ordinal) ||
                value.Contains("non-ideal", StringComparison.Ordinal) => Phase9ReplayFactCategory.NonIdeal,
            var value when value.Contains("phase9.cim.conversion", StringComparison.Ordinal) ||
                value.Contains("conversion", StringComparison.Ordinal) ||
                value.Contains("adc", StringComparison.Ordinal) ||
                value.Contains("dac", StringComparison.Ordinal) => Phase9ReplayFactCategory.Conversion,
            var value when value.Contains("weight_commit", StringComparison.Ordinal) ||
                value.Contains("weight_accept", StringComparison.Ordinal) ||
                value.Contains("write", StringComparison.Ordinal) ||
                value.Contains("update", StringComparison.Ordinal) => Phase9ReplayFactCategory.WriteUpdate,
            var value when value.Contains("shell_summary", StringComparison.Ordinal) ||
                value.Contains("pe_shell_summary", StringComparison.Ordinal) => Phase9ReplayFactCategory.ShellSummary,
            var value when value.Contains("analog", StringComparison.Ordinal) ||
                value.Contains("phase9.cim.array", StringComparison.Ordinal) ||
                value.Contains("mvm", StringComparison.Ordinal) => Phase9ReplayFactCategory.AnalogOperation,
            var value when value.Contains("compute_issue", StringComparison.Ordinal) ||
                value.Contains("weight_read", StringComparison.Ordinal) ||
                value.Contains("array", StringComparison.Ordinal) => Phase9ReplayFactCategory.ArrayCompute,
            _ => null
        };
        if (!category.HasValue)
        {
            return null;
        }

        var label = category.Value switch
        {
            Phase9ReplayFactCategory.AnalogOperation => "analog operation",
            Phase9ReplayFactCategory.Conversion => "ADC/DAC conversion",
            Phase9ReplayFactCategory.ArrayCompute => "array compute",
            Phase9ReplayFactCategory.WriteUpdate => "write/update",
            Phase9ReplayFactCategory.NonIdeal => "non-ideal effect",
            _ => "shell summary"
        };
        return new Phase9ReplayFactView(
            category.Value,
            label,
            detail,
            traceEvent.ComponentId ?? "",
            traceEvent.PacketId ?? "");
    }

    /// <summary>Formats a compact user-facing row while retaining the typed category.</summary>
    public static string FormatRow(TraceEvent? traceEvent)
    {
        var fact = Format(traceEvent);
        return fact is null ? "" : fact.Label + ": " + fact.Detail;
    }
}
/// <summary>Formats typed optical replay facts for Unity without relying on Unity-only parsing code.</summary>
public static class OpticalReplayFactFormatter
{
    /// <summary>Returns a compact optical fact row, or an empty string for unrelated trace events.</summary>
    public static string Format(TraceEvent? traceEvent)
    {
        if (traceEvent is null || string.IsNullOrWhiteSpace(traceEvent.Detail))
        {
            return "";
        }

        var wavelength = Value(traceEvent, "wavelength_nm");
        var channel = Value(traceEvent, "channel_id") ?? Value(traceEvent, "channel");
        var power = Value(traceEvent, "optical_power_dbm");
        var loss = Value(traceEvent, "accumulated_loss_db") ?? Value(traceEvent, "total_loss_db");
        var crosstalk = Value(traceEvent, "accumulated_crosstalk_db") ?? Value(traceEvent, "max_crosstalk_db");
        var snr = Value(traceEvent, "snr_db");
        var sensitivity = Value(traceEvent, "receiver_sensitivity_dbm");
        var margin = Value(traceEvent, "receiver_margin_db");
        var detectorCurrent = Value(traceEvent, "detector_current_A");
        var logicalPacketId = Value(traceEvent, PacketTraceIdentity.LogicalPacketIdKey);
        var parentPacketId = Value(traceEvent, PacketTraceIdentity.ParentPacketIdKey);
        var stallReason = Value(traceEvent, "stall_reason");
        var busyLink = Value(traceEvent, "busy_link");
        var isOpticalStall = string.Equals(
            stallReason,
            StallReason.OpticalChannelUnavailable.ToString(),
            StringComparison.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(wavelength) &&
            string.IsNullOrWhiteSpace(channel) &&
            string.IsNullOrWhiteSpace(power) &&
            string.IsNullOrWhiteSpace(loss) &&
            string.IsNullOrWhiteSpace(crosstalk) &&
            string.IsNullOrWhiteSpace(snr) &&
            string.IsNullOrWhiteSpace(sensitivity) &&
            string.IsNullOrWhiteSpace(margin) &&
            string.IsNullOrWhiteSpace(detectorCurrent) &&
            !isOpticalStall)
        {
            return "";
        }

        var fields = new List<string>();
        if (!string.IsNullOrWhiteSpace(traceEvent.PacketId)) fields.Add("packet=" + traceEvent.PacketId);
        if (!string.IsNullOrWhiteSpace(logicalPacketId)) fields.Add("logical=" + logicalPacketId);
        if (!string.IsNullOrWhiteSpace(parentPacketId)) fields.Add("parent=" + parentPacketId);
        if (!string.IsNullOrWhiteSpace(channel)) fields.Add("channel=" + channel);
        if (!string.IsNullOrWhiteSpace(wavelength)) fields.Add("wavelength_nm=" + wavelength);
        if (!string.IsNullOrWhiteSpace(power)) fields.Add("power_dBm=" + power);
        if (!string.IsNullOrWhiteSpace(loss)) fields.Add("loss_dB=" + loss);
        if (!string.IsNullOrWhiteSpace(crosstalk)) fields.Add("crosstalk_dB=" + crosstalk);
        if (!string.IsNullOrWhiteSpace(snr)) fields.Add("snr_dB=" + snr);
        if (!string.IsNullOrWhiteSpace(sensitivity)) fields.Add("sensitivity_dBm=" + sensitivity);
        if (!string.IsNullOrWhiteSpace(margin)) fields.Add("margin_dB=" + margin);
        if (!string.IsNullOrWhiteSpace(detectorCurrent)) fields.Add("detector_current_A=" + detectorCurrent);
        if (isOpticalStall) fields.Add("stall_reason=" + stallReason);
        if (!string.IsNullOrWhiteSpace(busyLink)) fields.Add("busy_link=" + busyLink);
        return string.Join(",", fields);
    }

    private static string? Value(TraceEvent traceEvent, string key) =>
        PacketTraceIdentity.DetailValue(traceEvent.Detail, key);
}

/// <summary>Provides the trace replay adapter service for hardware design and simulation workflows.</summary>
public sealed class TraceReplayAdapter : IDisposable
{
    private readonly TraceReplayController _controller;

    /// <summary>Initializes a new trace replay adapter instance from the supplied state.</summary>
    public TraceReplayAdapter(SimulationTrace trace)
    {
        _controller = new TraceReplayController(trace);
    }

    /// <summary>Initializes a new trace replay adapter instance from a persisted trace store reader.</summary>
    public TraceReplayAdapter(PersistedTraceStoreReader traceStore)
    {
        _controller = new TraceReplayController(traceStore);
    }

    /// <summary>Loads a replay adapter from a persisted trace store directory.</summary>
    public static TraceReplayAdapter LoadFromStore(string directory) => new(PersistedTraceStore.Open(directory));

    /// <summary>Gets whether the replay source is persisted trace storage.</summary>
    public bool LoadedFromPersistentTrace => _controller.LoadedFromPersistentTrace;

    /// <summary>Gets the stable replay source kind label.</summary>
    public string SourceKind => _controller.SourceKind;

    /// <summary>Gets the persisted trace manifest when the adapter was loaded from storage.</summary>
    public PersistedTraceManifest? Manifest => _controller.Manifest;

    /// <summary>Starts replay through the underlying trace controller.</summary>
    public void Play() => _controller.Play();
    /// <summary>Pauses replay through the underlying trace controller.</summary>
    public void Pause() => _controller.Pause();
    /// <summary>Advances playing replay and pauses atomically when the final cycle is reached.</summary>
    public bool AdvanceIfPlaying(int cycles = 1) => _controller.TryAdvanceIfPlaying(cycles);
    /// <summary>Moves replay forward or backward by the signed cycle delta.</summary>
    public void Step() => _controller.StepForward();
    /// <summary>Moves replay directly to the requested bounded cycle.</summary>
    public void JumpTo(long cycle) => _controller.JumpTo(cycle);
    /// <summary>Applies or clears the packet filter used by replay snapshots.</summary>
    public void SelectPacket(string? packetId) => _controller.SelectPacket(packetId);
    /// <summary>Selects every physical packet in the supplied packet's logical transformation family.</summary>
    public void SelectLogicalPacket(string? packetId) => _controller.SelectLogicalPacket(packetId);
    /// <summary>Applies or clears the component filter used by replay snapshots.</summary>
    public void SelectComponent(string? componentId) => _controller.SelectComponent(componentId);
    /// <summary>Updates event filter using the supplied value.</summary>
    public void SetEventFilter(TraceEventType type, bool enabled) => _controller.SetEventTypeEnabled(type, enabled);
    /// <summary>Returns the selected packet path from the replay trace.</summary>
    public IReadOnlyList<ReplayPacketPathStep> PacketPath(string packetId) => _controller.PacketPathWithCycles(packetId);
    /// <summary>Returns the complete logical path across physical packet transformations.</summary>
    public IReadOnlyList<ReplayPacketPathStep> LogicalPacketPath(string packetId) => _controller.LogicalPacketPathWithCycles(packetId);

    /// <summary>Projects the current replay cursor, state, and filtered events into Unity view data.</summary>
    public ReplaySnapshot Snapshot()
    {
        var events = _controller.CurrentEvents();
        var phase9Facts = events
            .Select(Phase9ReplayFactFormatter.Format)
            .Where(fact => fact is not null)
            .Cast<Phase9ReplayFactView>()
            .ToList();
        return new ReplaySnapshot(_controller.CurrentCycle, _controller.LastCycle, _controller.State, events, phase9Facts);
    }

    /// <summary>Projects current replay cycle details used by Unity graph visualization.</summary>
    public ReplayCycleDetails CycleDetails() => _controller.CurrentCycleDetails();

    /// <summary>Releases persisted trace storage resources when this adapter owns them.</summary>
    public void Dispose() => _controller.Dispose();
}

/// <summary>Provides the replay analysis adapter service for hardware design and simulation workflows.</summary>
public sealed class ReplayAnalysisAdapter
{
    private readonly ReplayAnalysisSnapshot _analysis;

    /// <summary>Initializes a new replay analysis adapter instance from the supplied state.</summary>
    public ReplayAnalysisAdapter(ReplayAnalysisSnapshot analysis)
    {
        _analysis = analysis;
    }

    /// <summary>Creates simulation from the supplied external representation.</summary>
    public static ReplayAnalysisAdapter FromSimulation(SimulationResult result, int timelineBinSize = 10, HardwareGraph? graph = null) =>
        new(ReplayAnalysisBuilder.Build(result, timelineBinSize, graph));

    /// <summary>Projects replay heatmaps, timeline bins, and adapter statistics into sorted Unity view data.</summary>
    public ReplayAnalysisViewSnapshot Snapshot() =>
        new(
            _analysis.Components
                .OrderByDescending(c => c.Heat)
                .Select(c => new AnalysisComponentView(c.ComponentId, c.TrafficBits, c.ActiveCycles, c.StallCycles, c.DominantStallReason, c.Heat))
                .ToList(),
            _analysis.Links
                .OrderByDescending(l => l.Heat)
                .Select(l => new AnalysisLinkView(l.LinkId, l.TransferredBits, l.CongestionCycles, l.Heat))
                .ToList(),
            _analysis.Timeline
                .Select(t => new AnalysisTimelineBinView(t.StartCycle, t.EndCycle, t.EventCount, new Dictionary<TraceEventType, int>(t.EventCountsByType)))
                .ToList(),
            _analysis.AdapterRuntime
                .OrderByDescending(a => a.PassThroughEventCount + a.PrecisionConversionEventCount)
                .ThenBy(a => a.ComponentId, StringComparer.Ordinal)
                .Select(a => new AnalysisAdapterRuntimeView(
                    a.ComponentId,
                    a.ComponentType,
                    a.AdapterType,
                    a.MismatchField,
                    a.SourceValue,
                    a.DestinationValue,
                    a.ActiveCycles,
                    a.InputTrafficBits,
                    a.OutputTrafficBits,
                    a.Energy,
                    a.PassThroughEventCount,
                    a.PrecisionConversionEventCount,
                    FormatAdapterRuntimeLabel(a),
                    FormatAdapterRuntimeDetail(a)))
                .ToList(),
            $"components={_analysis.Components.Count} links={_analysis.Links.Count} timelineBins={_analysis.Timeline.Count} adapterRuntime={_analysis.AdapterRuntime.Count}");

    private static string FormatAdapterRuntimeLabel(AdapterRuntimeSummaryEntry entry) =>
        $"{entry.AdapterType} ({entry.ComponentId})";

    private static string FormatAdapterRuntimeDetail(AdapterRuntimeSummaryEntry entry)
    {
        var energy = entry.Energy.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        return $"{entry.MismatchField}:{entry.SourceValue}->{entry.DestinationValue}; " +
               $"traffic={entry.InputTrafficBits}->{entry.OutputTrafficBits} bits; " +
               $"energy_pj={energy}; " +
               $"events=pass:{entry.PassThroughEventCount},precision:{entry.PrecisionConversionEventCount}";
    }
}

/// <summary>Provides the route grid congestion adapter service for hardware design and simulation workflows.</summary>
public sealed class RouteGridCongestionAdapter
{
    private readonly RouteGridCongestionReport _report;

    /// <summary>Initializes a new route grid congestion adapter instance from the supplied state.</summary>
    public RouteGridCongestionAdapter(RouteGridCongestionReport report)
    {
        _report = report;
    }

    /// <summary>Creates routing from the supplied external representation.</summary>
    public static RouteGridCongestionAdapter FromRouting(
        PhysicalRouting routing,
        double cellSizeMicrometers = 100,
        int defaultCellCapacity = 1) =>
        new(RouteGridAnalyzer.Analyze(routing, cellSizeMicrometers, defaultCellCapacity));

    /// <summary>Projects analyzed route cells and the congested subset into Unity view data.</summary>
    public RouteGridCongestionViewSnapshot Snapshot()
    {
        var cells = _report.Cells
            .OrderByDescending(c => c.IsCongested)
            .ThenByDescending(c => c.OverCapacityBy)
            .ThenBy(c => c.Layer, StringComparer.Ordinal)
            .ThenBy(c => c.X)
            .ThenBy(c => c.Y)
            .Select(c => new RouteGridCellView(c.X, c.Y, c.Layer, c.Occupancy, c.Capacity, c.OverCapacityBy, c.IsCongested, c.LinkIds))
            .ToList();

        return new RouteGridCongestionViewSnapshot(
            cells,
            cells.Where(c => c.IsCongested).ToList(),
            $"routes={_report.RouteCount} cells={_report.CellCount} congested={_report.CongestedCellCount}")
        {
            CellSizeMicrometers = _report.CellSizeMicrometers
        };
    }
}

/// <summary>Provides the exact route resource congestion adapter service for hardware design and simulation workflows.</summary>
public sealed class RouteResourceCongestionAdapter
{
    private readonly RouteResourceCongestionReport _report;

    /// <summary>Initializes a new exact route resource congestion adapter instance from the supplied state.</summary>
    public RouteResourceCongestionAdapter(RouteResourceCongestionReport report)
    {
        _report = report;
    }

    /// <summary>Creates exact route resource analysis from the supplied routing data.</summary>
    public static RouteResourceCongestionAdapter FromRouting(
        PhysicalRouting routing,
        double cellSizeMicrometers = 100,
        int defaultResourceCapacity = 1,
        RouteResourceCapacityProfile? capacityProfile = null) =>
        new(RouteResourceAnalyzer.Analyze(routing, cellSizeMicrometers, defaultResourceCapacity, capacityProfile));

    /// <summary>Projects exact edge, direction, layer, and medium route resources into Unity view data.</summary>
    public RouteResourceCongestionViewSnapshot Snapshot()
    {
        var resources = _report.Resources
            .OrderByDescending(resource => resource.IsCongested)
            .ThenByDescending(resource => resource.OverCapacityBy)
            .ThenBy(resource => resource.EdgeId, StringComparer.Ordinal)
            .ThenBy(resource => resource.Direction)
            .ThenBy(resource => resource.Layer, StringComparer.Ordinal)
            .ThenBy(resource => resource.Medium)
            .Select(resource => new RouteResourceView(
                resource.EdgeId,
                resource.Direction,
                resource.Layer,
                resource.Medium,
                resource.Occupancy,
                resource.Capacity,
                resource.OverCapacityBy,
                resource.IsCongested,
                resource.LinkIds,
                resource.OverCapacityLinkIds,
                resource.Evidence)
            {
                EdgeStartX = resource.Key.EdgeStartX,
                EdgeStartY = resource.Key.EdgeStartY,
                EdgeEndX = resource.Key.EdgeEndX,
                EdgeEndY = resource.Key.EdgeEndY
            })
            .ToList();

        return new RouteResourceCongestionViewSnapshot(
            resources,
            resources.Where(resource => resource.IsCongested).ToList(),
            $"routes={_report.RouteCount} resources={_report.ResourceCount} congested_resources={_report.CongestedResourceCount}; exact=edge/direction/layer/medium; cell summary only for overview")
        {
            CellSizeMicrometers = _report.CellSizeMicrometers
        };
    }
}
/// <summary>Provides the physical route editor adapter service for hardware design and simulation workflows.</summary>
public sealed class PhysicalRouteEditorAdapter
{
    private readonly PhysicalRouteEditor _editor;

    /// <summary>Initializes a new physical route editor adapter instance from the supplied state.</summary>
    public PhysicalRouteEditorAdapter(PhysicalRouting? routing = null)
    {
        _editor = new PhysicalRouteEditor(routing);
    }

    /// <summary>Gets the routing value carried by the enclosing physical route editor adapter contract.</summary>
    public PhysicalRouting Routing => _editor.Routing;

    /// <summary>Creates route from the supplied inputs.</summary>
    public string CreateRoute(
        string linkId,
        string layer = "M3",
        string routeType = "electrical",
        IReadOnlyList<PhysicalRoutePointView>? points = null)
    {
        var route = _editor.CreateRoute(
            linkId,
            (points ?? []).Select(p => new PhysicalPoint(p.X, p.Y)),
            layer,
            routeType);
        return route.LinkId;
    }

    /// <summary>Removes route from the current model.</summary>
    public void DeleteRoute(string linkId) => _editor.DeleteRoute(linkId);
    /// <summary>Appends a Unity-supplied coordinate to the selected link route.</summary>
    public void AppendPoint(string linkId, double x, double y) => _editor.AppendPoint(linkId, new PhysicalPoint(x, y));
    /// <summary>Inserts a Unity-supplied coordinate at the requested route index.</summary>
    public void InsertPoint(string linkId, int index, double x, double y) => _editor.InsertPoint(linkId, index, new PhysicalPoint(x, y));
    /// <summary>Moves the indexed route waypoint to a Unity-supplied coordinate.</summary>
    public void MovePoint(string linkId, int index, double x, double y) => _editor.MovePoint(linkId, index, new PhysicalPoint(x, y));
    /// <summary>Removes point from the current model.</summary>
    public void DeletePoint(string linkId, int index) => _editor.DeletePoint(linkId, index);

    /// <summary>Projects routes and their current congestion analysis into Unity view data.</summary>
    public PhysicalRouteEditorSnapshot Snapshot(
        double cellSizeMicrometers = 100,
        int defaultCellCapacity = 1,
        RouteResourceCapacityProfile? capacityProfile = null)
    {
        var routes = _editor.Routing.Routes
            .OrderBy(r => r.LinkId, StringComparer.Ordinal)
            .Select(r =>
            {
                var metrics = PhysicalRouteMetrics.Analyze(r.Path);
                return new PhysicalRouteView(
                    r.LinkId,
                    r.Layer,
                    r.RouteType,
                    r.Medium,
                    r.LayerId,
                    r.PathUnit,
                    metrics.LengthMicrometers,
                    metrics.BendCount,
                    r.Path.Select(p => new PhysicalRoutePointView(p.X, p.Y)).ToList());
            })
            .ToList();
        var congestion = new RouteGridCongestionAdapter(
            _editor.AnalyzeCongestion(cellSizeMicrometers, defaultCellCapacity)).Snapshot();
        var resourceCongestion = RouteResourceCongestionAdapter
            .FromRouting(_editor.Routing, cellSizeMicrometers, defaultCellCapacity, capacityProfile)
            .Snapshot();

        return new PhysicalRouteEditorSnapshot(
            routes,
            congestion,
            resourceCongestion,
            $"routes={routes.Count} points={routes.Sum(r => r.Points.Count)} congested_resources={resourceCongestion.CongestedResources.Count} cells={congestion.Cells.Count}");
    }
}
