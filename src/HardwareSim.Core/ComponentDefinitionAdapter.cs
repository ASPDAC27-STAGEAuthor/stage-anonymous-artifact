using System.Globalization;

namespace HardwareSim.Core;

#pragma warning disable CS1591

public sealed record ComponentTemplateSummaryView(
    string TemplateId,
    string Version,
    string DisplayName,
    ComponentTemplateTargetKind TargetKind,
    ComponentTemplateLifecycleState Lifecycle,
    bool SyntheticProfile,
    string Category);

public sealed record ComponentTemplatePortView(
    string Name,
    PortDirection Direction,
    SignalType SignalType,
    HardwareDataType DataType,
    PrecisionKind Precision,
    PortProtocol Protocol,
    string ShellBinding);

public sealed record ComponentTemplateParameterView(
    string Name,
    TemplateParameterValueKind ValueKind,
    string DefaultValue,
    string Units,
    string AllowedValues,
    bool Required);

public sealed record ComponentTemplateBlockView(
    string Id,
    string DisplayName,
    string BlockKind,
    InternalBlockLayer Layer,
    string TraceStage,
    double EnergyPicojoules,
    double AreaUm2,
    IReadOnlyList<string> MappedStructuralBlockIds,
    IReadOnlyList<string> PortNames,
    IReadOnlyList<ComponentTemplateInternalPortView> Ports);

public sealed record ComponentTemplateInternalPortView(
    string Name,
    PortDirection Direction,
    SignalType SignalType,
    HardwareDataType DataType,
    PrecisionKind Precision,
    PortProtocol Protocol,
    int WidthBits);

public sealed record ComponentTemplateConnectionView(
    string Id,
    string Source,
    string Target,
    string PayloadType,
    string Shape,
    PrecisionKind Precision,
    double RatePerCycle,
    int LatencyCycles,
    int BandwidthBitsPerCycle,
    TemplateBackpressureBehavior BackpressureBehavior);

public sealed record ComponentTemplateStorageView(
    string Id,
    string LogicalName,
    int Banks,
    int Rows,
    int Columns,
    int CellBits,
    string Encoding,
    long CapacityBits);

public sealed record ComponentTemplateTimingView(
    int OperationLatency,
    int PipelineLatency,
    int IssueInterval,
    int InputQueueDepth,
    int OutputQueueDepth,
    TemplateCollectionTargetPolicy DefaultResponseTargetPolicy,
    bool CanAcceptWhileBusy,
    string OutputBackpressureBehavior);

public sealed record ComponentTemplateOperationView(
    string OperationName,
    string Equation,
    string MultiplyDType,
    string AccumulateDType,
    string OutputDType,
    string QuantizationMode,
    bool Saturation,
    IReadOnlyList<string> InputOperands,
    IReadOnlyList<string> StoredOperands,
    IReadOnlyList<string> OutputOperands);

public sealed record ComponentTemplateProfileBindingView(
    string BindingId,
    string BlockId,
    string ProfileId,
    string ProfileHash,
    string OutputQuantity,
    string Units,
    double? ExactPointValue,
    string EvidenceStatus,
    string ValidRange,
    string Uncertainty,
    string Source,
    bool Synthetic);

public sealed record ComponentTemplateFootprintView(
    bool IsKnown,
    string Scope,
    double? AreaUm2,
    double? WidthUm,
    double? HeightUm,
    string SourceKind,
    string EvidenceStatus,
    string Uncertainty,
    string ValidContext,
    string FootprintHash);
public sealed record ComponentTemplateCompiledProfileView(
    string TemplateId,
    string TemplateVersion,
    string ProfileHash,
    int OperationLatency,
    int PipelineLatency,
    int IssueInterval,
    int InputQueueDepth,
    int OutputQueueDepth,
    TemplateCollectionTargetPolicy DefaultResponseTargetPolicy,
    double TotalEnergyPicojoules,
    double TotalAreaUm2,
    IReadOnlyDictionary<string, double> EnergyBreakdown,
    IReadOnlyList<string> TraceDescriptors,
    IReadOnlyList<string> InternalDrilldownStages,
    bool SyntheticProfile,
    IReadOnlyDictionary<string, string>? DerivedMetrics = null,
    IReadOnlyList<string>? SupportedOperations = null,
    IReadOnlyDictionary<string, string>? ShapeContract = null,
    IReadOnlyDictionary<string, long>? Capacity = null,
    IReadOnlyDictionary<string, string>? ProfileSnapshotHashes = null,
    ComponentTemplateFootprintView? PhysicalFootprint = null,
    IReadOnlyList<string>? EvidenceGaps = null);

public sealed record ComponentTemplateDefinitionSnapshot(
    IReadOnlyList<ComponentTemplateSummaryView> Templates,
    ComponentTemplateSummaryView? Selected,
    IReadOnlyList<string> ViewTabs,
    IReadOnlyList<ComponentTemplatePortView> Ports,
    IReadOnlyList<ComponentTemplateParameterView> Parameters,
    IReadOnlyList<ComponentTemplateBlockView> Blocks,
    IReadOnlyList<ComponentTemplateConnectionView> Connections,
    IReadOnlyDictionary<string, GridPosition> SymbolLayout,
    IReadOnlyDictionary<string, GridPosition> DataflowLayout,
    IReadOnlyDictionary<string, GridPosition> StructuralLayout,
    IReadOnlyList<ComponentTemplateStorageView> StorageLayouts,
    ComponentTemplateTimingView? Timing,
    ComponentTemplateOperationView? Operation,
    ComponentTemplateCompiledProfileView? CompiledProfile,
    IReadOnlyList<ComponentTemplateIssue> ValidationIssues,
    IReadOnlyList<ComponentTemplateProfileBindingView> ProfileBindings,
    IReadOnlyList<string> EvidenceWarnings);

public sealed record ComponentTemplateImpactChange(string Name, string Before, string After);

public sealed record ComponentTemplateCompileView(
    bool IsSuccess,
    ComponentTemplateCompiledProfileView? Profile,
    IReadOnlyList<ComponentTemplateIssue> Issues,
    IReadOnlyDictionary<string, string>? DerivedMetrics = null,
    IReadOnlyList<ComponentTemplateImpactChange>? ImpactChanges = null);

public sealed record ComponentTemplateEditResult(
    bool IsSuccess,
    ComponentTemplateDefinitionSnapshot Snapshot,
    IReadOnlyList<ComponentTemplateIssue> Issues,
    string Message,
    IReadOnlyList<ComponentTemplateImpactChange>? ImpactChanges = null);

public sealed record ComponentTemplateTestDataflowView(
    bool IsSuccess,
    string CompletionReason,
    string ProfileHash,
    long TotalCycles,
    long PacketsDelivered,
    double TotalEnergyPicojoules,
    double TotalAreaUm2,
    IReadOnlyList<string> TraceEvidence,
    IReadOnlyList<ComponentTemplateIssue> Issues,
    IReadOnlyDictionary<string, string>? ReferenceArtifacts = null);

public sealed record ComponentTemplatePlacementOption(
    string SlotLabel,
    string TemplateId,
    string Version,
    string DisplayName,
    ComponentTemplateTargetKind TargetKind,
    ComponentTemplateLifecycleState Lifecycle,
    bool SyntheticProfile,
    bool Placeable,
    string Summary);

public sealed class ComponentDefinitionAdapter
{
    private const string StructuralPortViewName = "Structural/Port View";
    private readonly ComponentTemplateLibrary library;
    private readonly ComponentTypeRegistry componentRegistry;
    private readonly NormalizedDeviceProfilePackage? phase9Profiles;
    private readonly Dictionary<string, ComponentTemplate> impactBaselineTemplates = new(StringComparer.OrdinalIgnoreCase);
    private string selectedTemplateId = "";
    private string selectedVersion = "";

    public ComponentDefinitionAdapter(
        ComponentTemplateLibrary? library = null,
        ComponentTypeRegistry? componentRegistry = null,
        NormalizedDeviceProfilePackage? phase9Profiles = null)
    {
        this.library = library ?? new ComponentTemplateLibrary();
        this.componentRegistry = componentRegistry ?? ComponentTypeRegistry.CreateDefault();
        this.phase9Profiles = phase9Profiles;
    }

    private ComponentTemplateCompileResult CompileTemplate(ComponentTemplate template, IReadOnlyDictionary<string, string>? overrides = null)
    {
        var frozen = componentRegistry.FreezeRuntimeKernels();
        if (!frozen.IsSuccess || frozen.Snapshot is null)
        {
            return ComponentTemplateCompileResult.Failure(frozen.Issues.Select(issue => new ComponentTemplateIssue(
                issue.Code,
                issue.Severity == ValidationSeverity.Error ? ComponentTemplateIssueSeverity.Error : ComponentTemplateIssueSeverity.Warning,
                issue.Location,
                issue.Message,
                issue.RelatedId)).ToList());
        }
        return new ComponentTemplateCompiler().Compile(template, overrides, kernelRegistry: frozen.Snapshot);
    }

    public ComponentTemplateLibrary Library => library;

    public static ComponentDefinitionAdapter CreateWithMvpExamples()
    {
        var adapter = new ComponentDefinitionAdapter(CreateMvpTemplateLibrary());
        adapter.SelectTemplate("PE_Array_32x32_FP8_SRAM_Synthetic", "1.0.0");
        return adapter;
    }

    public static ComponentDefinitionAdapter CreateWithPhase9Examples(string literatureCatalogPath)
    {
        var catalog = Phase7CLiteratureCharacterizationCatalog.Load(literatureCatalogPath);
        if (!catalog.IsValid)
        {
            throw new InvalidDataException("Phase 9 literature catalog is invalid: " +
                string.Join("; ", catalog.Issues.Select(issue => issue.Code + ":" + issue.Message)));
        }

        var profiles = Phase9LiteratureDeviceProfileNormalizer.Normalize(catalog);
        var adapter = new ComponentDefinitionAdapter(CreateMvpTemplateLibrary(profiles), phase9Profiles: profiles);
        if (!adapter.SelectTemplate(Phase9CimTemplateFactory.TemplateId, "1.0.0"))
        {
            throw new InvalidOperationException("Phase 9 literature CIM template was not registered.");
        }

        return adapter;
    }

    public static ComponentTemplateLibrary CreateMvpTemplateLibrary(NormalizedDeviceProfilePackage? phase9Profiles = null)
    {
        var library = new ComponentTemplateLibrary();
        library.AddOrReplace(ComponentTemplateExamples.PeArray32x32Fp8SramSynthetic());
        library.AddOrReplace(ComponentTemplateExamples.PeArray32x32Fp8ReramLikeSynthetic());
        if (phase9Profiles is not null)
            library.AddOrReplace(Phase9CimTemplateFactory.Create(phase9Profiles));
        return library;
    }

    public IReadOnlyList<string> Phase9ArrayProfileIds() =>
        phase9Profiles is null
            ? []
            : phase9Profiles.Profiles
                .Where(profile => profile.ResolveField("energy_pj_per_mac_assuming_2ops").Fields.Any(field => field.HasValue))
                .Select(profile => profile.ProfileId)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToList();

    public IReadOnlyList<string> Phase9AdcProfileIds() =>
        phase9Profiles is null
            ? []
            : phase9Profiles.Profiles
                .Where(profile => profile.ResolveField("energy_pj_per_sample").Fields.Any(field => field.HasValue) &&
                    profile.ResolveField("area_um2").Fields.Any(field => field.HasValue))
                .Select(profile => profile.ProfileId)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToList();

    public ComponentTemplateEditResult SwitchPhase9Profiles(string arrayProfileId, string adcProfileId)
    {
        if (phase9Profiles is null)
        {
            return EditFailure("Phase 9 profile package is unavailable.",
                new("Phase9ProfilePackageMissing", ComponentTemplateIssueSeverity.Error, "$.profile_bindings",
                    "Load the verified literature catalog before switching profiles."));
        }

        try
        {
            var replacement = Phase9CimTemplateFactory.Create(phase9Profiles, arrayProfileId, adcProfileId);
            library.AddOrReplace(replacement);
            selectedTemplateId = replacement.TemplateId;
            selectedVersion = replacement.Version;
            return new ComponentTemplateEditResult(true, Snapshot(), [],
                "Phase 9 device profiles switched; compiled profile, footprint, and provenance were recomputed.");
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or KeyNotFoundException)
        {
            return EditFailure("Phase 9 profile switch failed.",
                new("Phase9ProfileSwitchInvalid", ComponentTemplateIssueSeverity.Error, "$.profile_bindings", ex.Message));
        }
    }
    public IReadOnlyList<ComponentTemplateIssue> AddOrReplace(ComponentTemplate template)
    {
        var validation = new ComponentTemplateValidator().Validate(template);
        library.AddOrReplace(template);
        if (string.IsNullOrWhiteSpace(selectedTemplateId))
        {
            selectedTemplateId = template.TemplateId;
            selectedVersion = template.Version;
        }

        return validation.Issues;
    }

    public bool SelectTemplate(string templateId, string version)
    {
        var found = library.Find(templateId, version);
        if (found is null)
        {
            return false;
        }

        selectedTemplateId = found.TemplateId;
        selectedVersion = found.Version;
        return true;
    }

    public ComponentTemplateDefinitionSnapshot Snapshot()
    {
        var templates = library.Templates;
        var selected = SelectedTemplate(templates);
        var validation = selected is null ? [] : new ComponentTemplateValidator().Validate(selected).Issues;
        var compiled = selected?.CompiledProfile ?? (selected is null ? null : CompileTemplate(selected).Profile);
        return new ComponentTemplateDefinitionSnapshot(
            templates.Select(Summary).ToList(),
            selected is null ? null : Summary(selected),
            selected is null ? [] : selected.Views.OrderBy(view => view.Kind).Select(view => ViewLabel(view.Kind)).ToList(),
            selected is null ? [] : selected.ExternalPorts.OrderBy(port => port.Name, StringComparer.Ordinal).Select(Port).ToList(),
            selected is null ? [] : selected.Parameters.OrderBy(parameter => parameter.Name, StringComparer.Ordinal).Select(Parameter).ToList(),
            selected is null ? [] : selected.InternalBlocks.OrderBy(block => block.Layer).ThenBy(block => block.Id, StringComparer.Ordinal).Select(block => Block(selected, block)).ToList(),
            selected is null ? [] : selected.InternalConnections.OrderBy(connection => connection.Id, StringComparer.Ordinal).Select(Connection).ToList(),
            selected is null ? new Dictionary<string, GridPosition>(StringComparer.OrdinalIgnoreCase) : ViewLayout(selected, TemplateViewKind.Symbol),
            selected is null ? new Dictionary<string, GridPosition>(StringComparer.OrdinalIgnoreCase) : ViewLayout(selected, TemplateViewKind.Dataflow),
            selected is null ? new Dictionary<string, GridPosition>(StringComparer.OrdinalIgnoreCase) : ViewLayout(selected, TemplateViewKind.StructuralPort),
            selected is null ? [] : selected.StorageLayouts.OrderBy(layout => layout.Id, StringComparer.Ordinal).Select(Storage).ToList(),
            selected is null ? null : Timing(selected.TimingContract),
            selected is null ? null : Operation(selected.OperationContract),
            compiled is null ? null : Profile(compiled),
            validation,
            selected is null ? [] : selected.ProfileBindings.OrderBy(binding => binding.BindingId, StringComparer.Ordinal).Select(ProfileBinding).ToList(),
            selected is null ? [] : selected.Provenance.Warnings.OrderBy(value => value, StringComparer.Ordinal).ToList());
    }

    public ComponentTemplateCompileView CompileSelected(IReadOnlyDictionary<string, string>? overrides = null)
    {
        var selected = SelectedTemplate(library.Templates);
        if (selected is null)
        {
            return new ComponentTemplateCompileView(false, null, [new ComponentTemplateIssue("TemplateSelectionMissing", ComponentTemplateIssueSeverity.Error, "$", "No ComponentTemplate is selected.")]);
        }

        var pending = NormalizeOverrides(overrides);
        var result = CompileTemplate(selected, pending);
        var impact = BuildImpactChanges(selected, pending, result.Profile, result.DerivedMetrics);
        return new ComponentTemplateCompileView(result.IsSuccess, result.Profile is null ? null : Profile(result.Profile), result.Issues, result.DerivedMetrics, impact);
    }

    public ComponentTemplateEditResult CompileAndStoreSelected(IReadOnlyDictionary<string, string>? overrides = null)
    {
        var selected = SelectedTemplate(library.Templates);
        if (selected is null)
        {
            return EditFailure("No ComponentTemplate is selected.", new("TemplateSelectionMissing", ComponentTemplateIssueSeverity.Error, "$", "No ComponentTemplate is selected."));
        }

        var pending = NormalizeOverrides(overrides);

        var result = CompileTemplate(selected, pending);
        var impact = BuildImpactChanges(selected, pending, result.Profile, result.DerivedMetrics);
        if (!result.IsSuccess || result.Profile is null)
        {
            return new ComponentTemplateEditResult(false, Snapshot(), result.Issues, "compile failed", impact);
        }

        var applyIssues = ApplyParameterDefaults(selected, pending);
        if (applyIssues.Any(issue => issue.Severity is ComponentTemplateIssueSeverity.Error or ComponentTemplateIssueSeverity.Fatal))
        {
            return new ComponentTemplateEditResult(false, Snapshot(), applyIssues, "compile failed", impact);
        }

        var stored = CompileTemplate(selected);
        if (!stored.IsSuccess || stored.Profile is null)
        {
            return new ComponentTemplateEditResult(false, Snapshot(), stored.Issues, "compile failed", impact);
        }

        selected.CompiledProfile = stored.Profile;
        selected.Provenance.CompileHash = stored.Profile.ProfileHash;
        selected.Lifecycle = ComponentTemplateLifecycleState.Compiled;
        ClearImpactBaseline(selected);
        library.AddOrReplace(selected);
        SelectTemplate(selected.TemplateId, selected.Version);
        return new ComponentTemplateEditResult(true, Snapshot(), stored.Issues, "compile success", impact);
    }
    public ComponentTemplateEditResult CreateCustomProcessingElement()
    {
        var template = ComponentTemplateExamples.PeArray32x32Fp8SramSynthetic();
        var suffix = NextCustomSuffix("Custom_PE_Template_");
        template.TemplateId = "Custom_PE_Template_" + suffix;
        template.DisplayName = "Custom PE Template " + suffix;
        template.Version = "0.1.0";
        template.Lifecycle = ComponentTemplateLifecycleState.Draft;
        template.CompiledProfile = null;
        template.Provenance.Source = "unity.component_definition.custom";
        template.Provenance.Author = "Component Definition UI";
        template.Provenance.CompileHash = "";
        template.Provenance.Warnings = ["Synthetic profile only; custom draft must be validated and compiled before runtime."];
        library.AddOrReplace(template);
        SelectTemplate(template.TemplateId, template.Version);
        return new ComponentTemplateEditResult(true, Snapshot(), [], "created draft custom PE template");
    }

    public ComponentTemplateEditResult DuplicateSelectedAsCustom()
    {
        var selected = SelectedTemplate(library.Templates);
        if (selected is null)
        {
            return EditFailure("No ComponentTemplate is selected.", new("TemplateSelectionMissing", ComponentTemplateIssueSeverity.Error, "$", "No ComponentTemplate is selected."));
        }

        var suffix = NextCustomSuffix(SafeId(selected.TemplateId) + "_Custom_");
        selected.TemplateId = SafeId(selected.TemplateId) + "_Custom_" + suffix;
        selected.DisplayName = selected.DisplayName + " Custom " + suffix;
        selected.Version = "0.1.0";
        selected.Lifecycle = ComponentTemplateLifecycleState.Draft;
        selected.CompiledProfile = null;
        selected.Provenance.Source = "unity.component_definition.duplicate";
        selected.Provenance.Author = "Component Definition UI";
        selected.Provenance.CompileHash = "";
        selected.Provenance.Warnings = ["Synthetic profile only; duplicated custom draft must be validated and compiled before runtime."];
        library.AddOrReplace(selected);
        SelectTemplate(selected.TemplateId, selected.Version);
        return new ComponentTemplateEditResult(true, Snapshot(), [], "duplicated selected template as custom draft");
    }

    public ComponentTemplateEditResult ValidateSelected()
    {
        var selected = SelectedTemplate(library.Templates);
        if (selected is null)
        {
            return EditFailure("No ComponentTemplate is selected.", new("TemplateSelectionMissing", ComponentTemplateIssueSeverity.Error, "$", "No ComponentTemplate is selected."));
        }

        var validation = new ComponentTemplateValidator().Validate(selected);
        if (!validation.HasBlockingIssues)
        {
            selected.Lifecycle = ComponentTemplateLifecycleState.Validated;
            selected.CompiledProfile = null;
            selected.Provenance.CompileHash = "";
            library.AddOrReplace(selected);
            SelectTemplate(selected.TemplateId, selected.Version);
        }

        return new ComponentTemplateEditResult(!validation.HasBlockingIssues, Snapshot(), validation.Issues, validation.HasBlockingIssues ? "validation failed" : "validation success");
    }

    public ComponentTemplateEditResult SaveSelectedDraft(IReadOnlyDictionary<string, string>? overrides = null)
    {
        var selected = SelectedTemplate(library.Templates);
        if (selected is null)
        {
            return EditFailure("No ComponentTemplate is selected.", new("TemplateSelectionMissing", ComponentTemplateIssueSeverity.Error, "$", "No ComponentTemplate is selected."));
        }

        var pending = NormalizeOverrides(overrides);
        var preview = CompileTemplate(selected, pending);
        var impact = BuildImpactChanges(selected, pending, preview.Profile, preview.DerivedMetrics);
        var applyIssues = ApplyParameterDefaults(selected, pending);
        if (applyIssues.Any(issue => issue.Severity is ComponentTemplateIssueSeverity.Error or ComponentTemplateIssueSeverity.Fatal))
        {
            return new ComponentTemplateEditResult(false, Snapshot(), applyIssues, "draft save failed", impact);
        }

        if (selected.Lifecycle is ComponentTemplateLifecycleState.Published or ComponentTemplateLifecycleState.Compiled)
        {
            selected.Lifecycle = ComponentTemplateLifecycleState.Draft;
            selected.CompiledProfile = null;
            selected.Provenance.CompileHash = "";
        }

        ClearImpactBaseline(selected);
        library.AddOrReplace(selected);
        SelectTemplate(selected.TemplateId, selected.Version);
        return new ComponentTemplateEditResult(true, Snapshot(), new ComponentTemplateValidator().Validate(selected).Issues, "draft saved", impact);
    }
    public ComponentTemplateEditResult RenameSelectedTemplate(string displayName)
    {
        var selected = SelectedTemplate(library.Templates);
        if (selected is null)
        {
            return EditFailure("No ComponentTemplate is selected.", new("TemplateSelectionMissing", ComponentTemplateIssueSeverity.Error, "$", "No ComponentTemplate is selected."));
        }

        var trimmed = displayName?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return EditFailure("Template display name is required.", new("TemplateDisplayNameMissing", ComponentTemplateIssueSeverity.Error, "$.display_name", "Template display name is required.", selected.TemplateId));
        }

        CaptureImpactBaseline(selected);
        selected.DisplayName = trimmed;
        MarkEditedDraft(selected);
        library.AddOrReplace(selected);
        SelectTemplate(selected.TemplateId, selected.Version);
        return new ComponentTemplateEditResult(true, Snapshot(), new ComponentTemplateValidator().Validate(selected).Issues, "template name updated");
    }

    public ComponentTemplateEditResult PublishSelected()
    {
        var selected = SelectedTemplate(library.Templates);
        if (selected is null)
        {
            return EditFailure("No ComponentTemplate is selected.", new("TemplateSelectionMissing", ComponentTemplateIssueSeverity.Error, "$", "No ComponentTemplate is selected."));
        }

        if (selected.CompiledProfile is null)
        {
            var compile = CompileTemplate(selected);
            if (!compile.IsSuccess || compile.Profile is null)
            {
                return new ComponentTemplateEditResult(false, Snapshot(), compile.Issues, "publish requires successful compile");
            }

            selected.CompiledProfile = compile.Profile;
            selected.Provenance.CompileHash = compile.Profile.ProfileHash;
        }

        selected.Lifecycle = ComponentTemplateLifecycleState.Published;
        library.AddOrReplace(selected);
        SelectTemplate(selected.TemplateId, selected.Version);
        return new ComponentTemplateEditResult(true, Snapshot(), [], "published");
    }

    public ComponentTemplateEditResult UpdateSelectedParameterDefault(string name, string value)
    {
        var selected = SelectedTemplate(library.Templates);
        if (selected is null)
        {
            return EditFailure("No ComponentTemplate is selected.", new("TemplateSelectionMissing", ComponentTemplateIssueSeverity.Error, "$", "No ComponentTemplate is selected."));
        }

        var parameter = selected.Parameters.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
        if (parameter is null)
        {
            return EditFailure("Parameter not found.", new("TemplateParameterMissing", ComponentTemplateIssueSeverity.Error, "$.parameters", $"Parameter '{name}' was not found.", name));
        }

        if (!TemplateParameterValueIsValid(parameter, value, out var message))
        {
            return EditFailure(message, new("TemplateParameterInvalid", ComponentTemplateIssueSeverity.Error, "$.parameters[" + parameter.Name + "].default_value", message, parameter.Name));
        }

        CaptureImpactBaseline(selected);
        parameter.DefaultValue = value;
        MarkEditedDraft(selected);
        library.AddOrReplace(selected);
        SelectTemplate(selected.TemplateId, selected.Version);
        return new ComponentTemplateEditResult(true, Snapshot(), new ComponentTemplateValidator().Validate(selected).Issues, "parameter default updated");
    }

    public ComponentTemplateEditResult AddSelectedBlock(string layerName, string blockKind)
    {
        var selected = SelectedTemplate(library.Templates);
        if (selected is null)
        {
            return EditFailure("No ComponentTemplate is selected.", new("TemplateSelectionMissing", ComponentTemplateIssueSeverity.Error, "$", "No ComponentTemplate is selected."));
        }

        var layer = string.Equals(layerName, "Dataflow", StringComparison.OrdinalIgnoreCase)
            ? InternalBlockLayer.Dataflow
            : InternalBlockLayer.Structural;
        var normalizedKind = string.IsNullOrWhiteSpace(blockKind) ? (layer == InternalBlockLayer.Dataflow ? "Stage" : "ComputeCore") : blockKind.Trim();
        var idPrefix = SafeId(normalizedKind).ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(idPrefix))
        {
            idPrefix = layer == InternalBlockLayer.Dataflow ? "stage" : "block";
        }

        var id = NextInternalBlockId(selected, idPrefix);
        var block = new InternalBlock
        {
            Id = id,
            DisplayName = HumanizeBlockKind(normalizedKind),
            BlockKind = normalizedKind,
            TypeId = "component_template.internal." + idPrefix,
            Layer = layer,
            Abstract = layer == InternalBlockLayer.Dataflow,
            TraceStage = idPrefix + "_stage",
            EnergyPicojoules = layer == InternalBlockLayer.Structural ? 0.1 : 0,
            AreaUm2 = layer == InternalBlockLayer.Structural ? 100 : 0,
            Ports = [
                new InternalPort { Name = "in", Direction = PortDirection.Input, SignalType = SignalType.Digital, DataType = HardwareDataType.Tensor, Precision = PrecisionKind.FP8_E4M3, Protocol = PortProtocol.Packet, WidthBits = 256 },
                new InternalPort { Name = "out", Direction = PortDirection.Output, SignalType = SignalType.Digital, DataType = HardwareDataType.Tensor, Precision = PrecisionKind.FP8_E4M3, Protocol = PortProtocol.Packet, WidthBits = 256 }
            ]
        };

        CaptureImpactBaseline(selected);
        selected.InternalBlocks.Add(block);
        var viewKind = layer == InternalBlockLayer.Dataflow ? TemplateViewKind.Dataflow : TemplateViewKind.StructuralPort;
        var layout = EnsureView(selected, viewKind).Layout;
        layout[id] = NextLayoutPosition(selected, layer, viewKind);
        MarkEditedDraft(selected);
        library.AddOrReplace(selected);
        SelectTemplate(selected.TemplateId, selected.Version);
        return new ComponentTemplateEditResult(true, Snapshot(), new ComponentTemplateValidator().Validate(selected).Issues, "block added");
    }

    public ComponentTemplateEditResult DeleteSelectedBlock(string blockId)
    {
        var selected = SelectedTemplate(library.Templates);
        if (selected is null)
        {
            return EditFailure("No ComponentTemplate is selected.", new("TemplateSelectionMissing", ComponentTemplateIssueSeverity.Error, "$", "No ComponentTemplate is selected."));
        }

        var block = selected.InternalBlocks.FirstOrDefault(b => string.Equals(b.Id, blockId, StringComparison.OrdinalIgnoreCase));
        if (block is null)
        {
            return EditFailure("Block not found.", new("TemplateInternalBlockMissing", ComponentTemplateIssueSeverity.Error, "$.internal_blocks", $"Internal block '{blockId}' was not found.", blockId));
        }

        CaptureImpactBaseline(selected);
        selected.InternalBlocks.Remove(block);
        selected.InternalConnections.RemoveAll(connection => string.Equals(connection.SourceBlockId, blockId, StringComparison.OrdinalIgnoreCase) || string.Equals(connection.TargetBlockId, blockId, StringComparison.OrdinalIgnoreCase));
        foreach (var item in selected.InternalBlocks)
        {
            item.MappedStructuralBlockIds.RemoveAll(id => string.Equals(id, blockId, StringComparison.OrdinalIgnoreCase));
        }

        foreach (var view in selected.Views)
        {
            view.Layout.Remove(blockId);
        }

        MarkEditedDraft(selected);
        library.AddOrReplace(selected);
        SelectTemplate(selected.TemplateId, selected.Version);
        return new ComponentTemplateEditResult(true, Snapshot(), new ComponentTemplateValidator().Validate(selected).Issues, "block deleted");
    }

    public ComponentTemplateEditResult UpdateSelectedBlockLayout(string viewLabel, string blockId, int x, int y)
    {
        var selected = SelectedTemplate(library.Templates);
        if (selected is null)
        {
            return EditFailure("No ComponentTemplate is selected.", new("TemplateSelectionMissing", ComponentTemplateIssueSeverity.Error, "$", "No ComponentTemplate is selected."));
        }

        var block = selected.InternalBlocks.FirstOrDefault(b => string.Equals(b.Id, blockId, StringComparison.OrdinalIgnoreCase));
        if (block is null)
        {
            return EditFailure("Block not found.", new("TemplateInternalBlockMissing", ComponentTemplateIssueSeverity.Error, "$.internal_blocks", $"Internal block '{blockId}' was not found.", blockId));
        }

        var kind = string.Equals(viewLabel, StructuralPortViewName, StringComparison.OrdinalIgnoreCase) || block.Layer == InternalBlockLayer.Structural
            ? TemplateViewKind.StructuralPort
            : TemplateViewKind.Dataflow;
        EnsureView(selected, kind).Layout[blockId] = new GridPosition(x, y);
        library.AddOrReplace(selected);
        SelectTemplate(selected.TemplateId, selected.Version);
        return new ComponentTemplateEditResult(true, Snapshot(), new ComponentTemplateValidator().Validate(selected).Issues, "layout updated");
    }

    public ComponentTemplateEditResult UpdateSelectedSymbolLayout(string itemId, int x, int y)
    {
        var selected = SelectedTemplate(library.Templates);
        if (selected is null)
        {
            return EditFailure("No ComponentTemplate is selected.", new("TemplateSelectionMissing", ComponentTemplateIssueSeverity.Error, "$", "No ComponentTemplate is selected."));
        }

        if (string.IsNullOrWhiteSpace(itemId))
        {
            return EditFailure("Symbol item missing.", new("TemplateSymbolItemMissing", ComponentTemplateIssueSeverity.Error, "$.views[symbol].layout", "Symbol layout item id is required."));
        }

        var id = itemId.Trim();
        var known = string.Equals(id, "symbol_shell", StringComparison.OrdinalIgnoreCase)
            || selected.ExternalPorts.Any(port => string.Equals(port.Name, id, StringComparison.OrdinalIgnoreCase));
        if (!known)
        {
            return EditFailure("Symbol item not found.", new("TemplateSymbolItemMissing", ComponentTemplateIssueSeverity.Error, "$.views[symbol].layout", $"Symbol item '{id}' was not found.", id));
        }

        EnsureView(selected, TemplateViewKind.Symbol).Layout[id] = new GridPosition(x, y);
        library.AddOrReplace(selected);
        SelectTemplate(selected.TemplateId, selected.Version);
        return new ComponentTemplateEditResult(true, Snapshot(), new ComponentTemplateValidator().Validate(selected).Issues, "symbol layout updated");
    }
    public ComponentTemplateEditResult AddSelectedConnection(string viewLabel, string sourceBlockId, string targetBlockId)
    {
        var selected = SelectedTemplate(library.Templates);
        if (selected is null)
        {
            return EditFailure("No ComponentTemplate is selected.", new("TemplateSelectionMissing", ComponentTemplateIssueSeverity.Error, "$", "No ComponentTemplate is selected."));
        }

        var source = selected.InternalBlocks.FirstOrDefault(b => string.Equals(b.Id, sourceBlockId, StringComparison.OrdinalIgnoreCase));
        var target = selected.InternalBlocks.FirstOrDefault(b => string.Equals(b.Id, targetBlockId, StringComparison.OrdinalIgnoreCase));
        var sourcePort = source?.Ports.FirstOrDefault(p => p.Direction is PortDirection.Output or PortDirection.Bidirectional);
        var targetPort = sourcePort is null
            ? null
            : target?.Ports.FirstOrDefault(p => p.Direction is PortDirection.Input or PortDirection.Bidirectional && p.SignalType == sourcePort.SignalType && p.Protocol == sourcePort.Protocol)
                ?? target?.Ports.FirstOrDefault(p => p.Direction is PortDirection.Input or PortDirection.Bidirectional);
        return AddSelectedConnection(viewLabel, sourceBlockId, sourcePort?.Name ?? string.Empty, targetBlockId, targetPort?.Name ?? string.Empty);
    }

    public ComponentTemplateEditResult AddSelectedConnection(string viewLabel, string sourceBlockId, string sourcePortName, string targetBlockId, string targetPortName)
    {
        var selected = SelectedTemplate(library.Templates);
        if (selected is null)
        {
            return EditFailure("No ComponentTemplate is selected.", new("TemplateSelectionMissing", ComponentTemplateIssueSeverity.Error, "$", "No ComponentTemplate is selected."));
        }

        var source = selected.InternalBlocks.FirstOrDefault(b => string.Equals(b.Id, sourceBlockId, StringComparison.OrdinalIgnoreCase));
        var target = selected.InternalBlocks.FirstOrDefault(b => string.Equals(b.Id, targetBlockId, StringComparison.OrdinalIgnoreCase));
        if (source is null || target is null)
        {
            return EditFailure("Connection endpoint block not found.", new("TemplateConnectionEndpointMissing", ComponentTemplateIssueSeverity.Error, "$.internal_connections", "Connection source or target block was not found.", sourceBlockId + "->" + targetBlockId));
        }

        if (source.Layer != target.Layer)
        {
            return EditFailure("Connection endpoints must be in the same ComponentTemplate view layer.", new("TemplateConnectionLayerMismatch", ComponentTemplateIssueSeverity.Error, "$.internal_connections", "Connection endpoints must both be dataflow blocks or both be structural blocks.", source.Id + "->" + target.Id));
        }

        var sourcePort = source.Ports.FirstOrDefault(p => string.Equals(p.Name, sourcePortName, StringComparison.OrdinalIgnoreCase) && p.Direction is PortDirection.Output or PortDirection.Bidirectional);
        var targetPort = target.Ports.FirstOrDefault(p => string.Equals(p.Name, targetPortName, StringComparison.OrdinalIgnoreCase) && p.Direction is PortDirection.Input or PortDirection.Bidirectional);
        if (sourcePort is null || targetPort is null)
        {
            return EditFailure("Connection requires an output port and a compatible input port.", new("TemplateConnectionPortMissing", ComponentTemplateIssueSeverity.Error, "$.internal_connections", "Connection requires an output port and a compatible input port.", source.Id + "." + sourcePortName + "->" + target.Id + "." + targetPortName));
        }

        if (sourcePort.SignalType != targetPort.SignalType || sourcePort.Protocol != targetPort.Protocol)
        {
            return EditFailure("Connection endpoints have incompatible domain/protocol.", new("TemplateConnectionDomainMismatch", ComponentTemplateIssueSeverity.Error, "$.internal_connections", "Connection endpoints have incompatible domain/protocol.", source.Id + "." + sourcePort.Name + "->" + target.Id + "." + targetPort.Name));
        }

        var duplicate = selected.InternalConnections.Any(connection =>
            string.Equals(connection.SourceBlockId, source.Id, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(connection.SourcePortName, sourcePort.Name, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(connection.TargetBlockId, target.Id, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(connection.TargetPortName, targetPort.Name, StringComparison.OrdinalIgnoreCase));
        if (duplicate)
        {
            return EditFailure("Connection already exists.", new("TemplateConnectionDuplicate", ComponentTemplateIssueSeverity.Warning, "$.internal_connections", "Connection already exists.", source.Id + "." + sourcePort.Name + "->" + target.Id + "." + targetPort.Name));
        }

        var prefix = source.Layer == InternalBlockLayer.Dataflow ? "df_custom" : "s_custom";
        CaptureImpactBaseline(selected);
        selected.InternalConnections.Add(new InternalConnection
        {
            Id = NextInternalConnectionId(selected, prefix),
            SourceBlockId = source.Id,
            SourcePortName = sourcePort.Name,
            TargetBlockId = target.Id,
            TargetPortName = targetPort.Name,
            PayloadType = sourcePort.DataType.ToString().ToLowerInvariant(),
            Shape = sourcePort.Shape.ToList(),
            Precision = sourcePort.Precision,
            BandwidthBitsPerCycle = sourcePort.WidthBits,
            BackpressureBehavior = TemplateBackpressureBehavior.Propagate
        });

        MarkEditedDraft(selected);
        library.AddOrReplace(selected);
        SelectTemplate(selected.TemplateId, selected.Version);
        return new ComponentTemplateEditResult(true, Snapshot(), new ComponentTemplateValidator().Validate(selected).Issues, "connection added");
    }

    public ComponentTemplateEditResult DeleteSelectedConnection(string connectionId)
    {
        var selected = SelectedTemplate(library.Templates);
        if (selected is null)
        {
            return EditFailure("No ComponentTemplate is selected.", new("TemplateSelectionMissing", ComponentTemplateIssueSeverity.Error, "$", "No ComponentTemplate is selected."));
        }

        if (!selected.InternalConnections.Any(connection => string.Equals(connection.Id, connectionId, StringComparison.OrdinalIgnoreCase)))
        {
            return EditFailure("Connection not found.", new("TemplateConnectionMissing", ComponentTemplateIssueSeverity.Error, "$.internal_connections", $"Connection '{connectionId}' was not found.", connectionId));
        }

        CaptureImpactBaseline(selected);
        selected.InternalConnections.RemoveAll(connection => string.Equals(connection.Id, connectionId, StringComparison.OrdinalIgnoreCase));

        MarkEditedDraft(selected);
        library.AddOrReplace(selected);
        SelectTemplate(selected.TemplateId, selected.Version);
        return new ComponentTemplateEditResult(true, Snapshot(), new ComponentTemplateValidator().Validate(selected).Issues, "connection deleted");
    }

    public ComponentTemplateEditResult UpdateSelectedConnection(string connectionId, string payloadType, string shape, string precision, string ratePerCycle, string latencyCycles, string bandwidthBitsPerCycle, string backpressureBehavior)
    {
        var selected = SelectedTemplate(library.Templates);
        if (selected is null)
        {
            return EditFailure("No ComponentTemplate is selected.", new("TemplateSelectionMissing", ComponentTemplateIssueSeverity.Error, "$", "No ComponentTemplate is selected."));
        }

        var connection = selected.InternalConnections.FirstOrDefault(item => string.Equals(item.Id, connectionId, StringComparison.OrdinalIgnoreCase));
        if (connection is null)
        {
            return EditFailure("Connection not found.", new("TemplateConnectionMissing", ComponentTemplateIssueSeverity.Error, "$.internal_connections", $"Connection '{connectionId}' was not found.", connectionId));
        }

        if (!Enum.TryParse<PrecisionKind>(precision, ignoreCase: true, out var parsedPrecision))
        {
            return EditFailure("Connection precision is invalid.", new("TemplateConnectionPrecisionInvalid", ComponentTemplateIssueSeverity.Error, "$.internal_connections[" + connection.Id + "].precision", "Connection precision is invalid.", connection.Id));
        }

        if (!TryNonNegativeDouble(ratePerCycle, out var parsedRate) || parsedRate <= 0)
        {
            return EditFailure("Connection rate must be positive.", new("TemplateConnectionRateInvalid", ComponentTemplateIssueSeverity.Error, "$.internal_connections[" + connection.Id + "].rate", "Connection rate must be positive.", connection.Id));
        }

        if (!TryNonNegativeInt(latencyCycles, out var parsedLatency))
        {
            return EditFailure("Connection latency must be a non-negative integer.", new("TemplateConnectionLatencyInvalid", ComponentTemplateIssueSeverity.Error, "$.internal_connections[" + connection.Id + "].latency", "Connection latency must be a non-negative integer.", connection.Id));
        }

        if (!TryPositiveInt(bandwidthBitsPerCycle, out var parsedBandwidth))
        {
            return EditFailure("Connection bandwidth must be a positive integer.", new("TemplateConnectionBandwidthInvalid", ComponentTemplateIssueSeverity.Error, "$.internal_connections[" + connection.Id + "].bandwidth", "Connection bandwidth must be a positive integer.", connection.Id));
        }

        if (!Enum.TryParse<TemplateBackpressureBehavior>(backpressureBehavior, ignoreCase: true, out var parsedBackpressure))
        {
            return EditFailure("Connection backpressure behavior is invalid.", new("TemplateConnectionBackpressureInvalid", ComponentTemplateIssueSeverity.Error, "$.internal_connections[" + connection.Id + "].backpressure", "Connection backpressure behavior is invalid.", connection.Id));
        }

        CaptureImpactBaseline(selected);
        connection.PayloadType = string.IsNullOrWhiteSpace(payloadType) ? "packet" : payloadType.Trim();
        connection.Shape = ParseShape(shape);
        connection.Precision = parsedPrecision;
        connection.RatePerCycle = parsedRate;
        connection.LatencyCycles = parsedLatency;
        connection.BandwidthBitsPerCycle = parsedBandwidth;
        connection.BackpressureBehavior = parsedBackpressure;
        MarkEditedDraft(selected);
        library.AddOrReplace(selected);
        SelectTemplate(selected.TemplateId, selected.Version);
        return new ComponentTemplateEditResult(true, Snapshot(), new ComponentTemplateValidator().Validate(selected).Issues, "connection updated");
    }

    public ComponentTemplateEditResult UpdateSelectedPort(string blockId, string portName, string newName, string direction, string signalType, string dataType, string precision, string protocol, string widthBits)
    {
        var selected = SelectedTemplate(library.Templates);
        if (selected is null)
        {
            return EditFailure("No ComponentTemplate is selected.", new("TemplateSelectionMissing", ComponentTemplateIssueSeverity.Error, "$", "No ComponentTemplate is selected."));
        }

        var block = selected.InternalBlocks.FirstOrDefault(item => string.Equals(item.Id, blockId, StringComparison.OrdinalIgnoreCase));
        var port = block?.Ports.FirstOrDefault(item => string.Equals(item.Name, portName, StringComparison.OrdinalIgnoreCase));
        if (block is null || port is null)
        {
            return EditFailure("Internal port not found.", new("TemplateInternalPortMissing", ComponentTemplateIssueSeverity.Error, "$.internal_blocks", "Internal port was not found.", blockId + "." + portName));
        }

        var trimmedName = string.IsNullOrWhiteSpace(newName) ? port.Name : SafeId(newName.Trim());
        if (string.IsNullOrWhiteSpace(trimmedName))
        {
            return EditFailure("Port name is invalid.", new("TemplateInternalPortNameInvalid", ComponentTemplateIssueSeverity.Error, "$.internal_blocks[" + block.Id + "].ports", "Port name is invalid.", block.Id));
        }

        if (block.Ports.Any(item => !ReferenceEquals(item, port) && string.Equals(item.Name, trimmedName, StringComparison.OrdinalIgnoreCase)))
        {
            return EditFailure("Port name already exists on this block.", new("TemplateInternalPortDuplicate", ComponentTemplateIssueSeverity.Error, "$.internal_blocks[" + block.Id + "].ports", "Port name already exists on this block.", block.Id + "." + trimmedName));
        }

        if (!Enum.TryParse<PortDirection>(direction, ignoreCase: true, out var parsedDirection) ||
            !Enum.TryParse<SignalType>(signalType, ignoreCase: true, out var parsedSignal) ||
            !Enum.TryParse<HardwareDataType>(dataType, ignoreCase: true, out var parsedDataType) ||
            !Enum.TryParse<PrecisionKind>(precision, ignoreCase: true, out var parsedPrecision) ||
            !Enum.TryParse<PortProtocol>(protocol, ignoreCase: true, out var parsedProtocol))
        {
            return EditFailure("Port enum value is invalid.", new("TemplateInternalPortEnumInvalid", ComponentTemplateIssueSeverity.Error, "$.internal_blocks[" + block.Id + "].ports", "Port enum value is invalid.", block.Id + "." + port.Name));
        }

        if (!TryPositiveInt(widthBits, out var parsedWidth))
        {
            return EditFailure("Port width must be a positive integer.", new("TemplateInternalPortWidthInvalid", ComponentTemplateIssueSeverity.Error, "$.internal_blocks[" + block.Id + "].ports", "Port width must be a positive integer.", block.Id + "." + port.Name));
        }

        CaptureImpactBaseline(selected);
        var previousName = port.Name;
        port.Name = trimmedName;
        port.Direction = parsedDirection;
        port.SignalType = parsedSignal;
        port.DataType = parsedDataType;
        port.Precision = parsedPrecision;
        port.Protocol = parsedProtocol;
        port.WidthBits = parsedWidth;
        if (!string.Equals(previousName, trimmedName, StringComparison.OrdinalIgnoreCase))
        {
            foreach (var connection in selected.InternalConnections)
            {
                if (string.Equals(connection.SourceBlockId, block.Id, StringComparison.OrdinalIgnoreCase) && string.Equals(connection.SourcePortName, previousName, StringComparison.OrdinalIgnoreCase)) connection.SourcePortName = trimmedName;
                if (string.Equals(connection.TargetBlockId, block.Id, StringComparison.OrdinalIgnoreCase) && string.Equals(connection.TargetPortName, previousName, StringComparison.OrdinalIgnoreCase)) connection.TargetPortName = trimmedName;
            }
        }

        MarkEditedDraft(selected);
        library.AddOrReplace(selected);
        SelectTemplate(selected.TemplateId, selected.Version);
        return new ComponentTemplateEditResult(true, Snapshot(), new ComponentTemplateValidator().Validate(selected).Issues, "port updated");
    }

    public ComponentTemplateEditResult UpdateSelectedDataflowMapping(string dataflowBlockId, string structuralBlockIds)
    {
        var selected = SelectedTemplate(library.Templates);
        if (selected is null)
        {
            return EditFailure("No ComponentTemplate is selected.", new("TemplateSelectionMissing", ComponentTemplateIssueSeverity.Error, "$", "No ComponentTemplate is selected."));
        }

        var dataflow = selected.InternalBlocks.FirstOrDefault(item => string.Equals(item.Id, dataflowBlockId, StringComparison.OrdinalIgnoreCase) && item.Layer == InternalBlockLayer.Dataflow);
        if (dataflow is null)
        {
            return EditFailure("Dataflow block not found.", new("TemplateDataflowBlockMissing", ComponentTemplateIssueSeverity.Error, "$.internal_blocks", "Dataflow block was not found.", dataflowBlockId));
        }

        var ids = (structuralBlockIds ?? string.Empty)
            .Split(new[] { ',', ';', ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(item => item.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var structural = selected.InternalBlocks.Where(item => item.Layer == InternalBlockLayer.Structural).Select(item => item.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missing = ids.FirstOrDefault(id => !structural.Contains(id));
        if (missing is not null)
        {
            return EditFailure("Mapped structural block not found.", new("TemplateMappingTargetMissing", ComponentTemplateIssueSeverity.Error, "$.internal_blocks[" + dataflow.Id + "].mapped_structural_block_ids", "Mapped structural block was not found.", missing));
        }

        CaptureImpactBaseline(selected);
        dataflow.MappedStructuralBlockIds.Clear();
        dataflow.MappedStructuralBlockIds.AddRange(ids);
        MarkEditedDraft(selected);
        library.AddOrReplace(selected);
        SelectTemplate(selected.TemplateId, selected.Version);
        return new ComponentTemplateEditResult(true, Snapshot(), new ComponentTemplateValidator().Validate(selected).Issues, "mapping updated");
    }

    public ComponentTemplateEditResult UpdateSelectedBlock(string blockId, string displayName, string blockKind, string traceStage, string energyPicojoules, string areaUm2)
    {
        var selected = SelectedTemplate(library.Templates);
        if (selected is null)
        {
            return EditFailure("No ComponentTemplate is selected.", new("TemplateSelectionMissing", ComponentTemplateIssueSeverity.Error, "$", "No ComponentTemplate is selected."));
        }

        var block = selected.InternalBlocks.FirstOrDefault(b => string.Equals(b.Id, blockId, StringComparison.OrdinalIgnoreCase));
        if (block is null)
        {
            return EditFailure("Block not found.", new("TemplateInternalBlockMissing", ComponentTemplateIssueSeverity.Error, "$.internal_blocks", $"Internal block '{blockId}' was not found.", blockId));
        }

        if (!TryNonNegativeDouble(energyPicojoules, out var energy))
        {
            return EditFailure("Energy must be a non-negative number.", new("TemplateInternalBlockEnergyInvalid", ComponentTemplateIssueSeverity.Error, "$.internal_blocks[" + block.Id + "].energy", "Energy must be a non-negative number.", block.Id));
        }

        if (!TryNonNegativeDouble(areaUm2, out var area))
        {
            return EditFailure("Area must be a non-negative number.", new("TemplateInternalBlockAreaInvalid", ComponentTemplateIssueSeverity.Error, "$.internal_blocks[" + block.Id + "].area", "Area must be a non-negative number.", block.Id));
        }

        var mappedStructuralBlocks = MappedStructuralBlocks(selected, block);
        var effectiveMetrics = EffectiveBlockMetrics(selected, block);
        var metricChanged = Math.Abs(effectiveMetrics.EnergyPicojoules - energy) > 0.000000000001 || Math.Abs(effectiveMetrics.AreaUm2 - area) > 0.000000000001;
        if (block.Layer == InternalBlockLayer.Dataflow && metricChanged && mappedStructuralBlocks.Count == 0)
        {
            return EditFailure("Dataflow metrics require a mapped structural block.", new("TemplateDataflowMetricMappingMissing", ComponentTemplateIssueSeverity.Error, "$.internal_blocks[" + block.Id + "].mapped_structural_block_ids", "Dataflow energy and area are projections of mapped structural blocks; map this block before editing physical metrics.", block.Id));
        }
        if (block.Layer == InternalBlockLayer.Dataflow && metricChanged && mappedStructuralBlocks.Count > 1)
        {
            return EditFailure("Dataflow metric edit is ambiguous across multiple structural blocks.", new("TemplateDataflowMetricMappingAmbiguous", ComponentTemplateIssueSeverity.Error, "$.internal_blocks[" + block.Id + "].mapped_structural_block_ids", "Edit each mapped structural block separately when a dataflow block maps to multiple physical blocks.", block.Id));
        }

        CaptureImpactBaseline(selected);
        block.DisplayName = string.IsNullOrWhiteSpace(displayName) ? block.DisplayName : displayName.Trim();
        block.BlockKind = string.IsNullOrWhiteSpace(blockKind) ? block.BlockKind : blockKind.Trim();
        block.TraceStage = traceStage.Trim();
        if (block.Layer == InternalBlockLayer.Dataflow)
        {
            block.EnergyPicojoules = 0;
            block.AreaUm2 = 0;
            if (mappedStructuralBlocks.Count == 1)
            {
                mappedStructuralBlocks[0].EnergyPicojoules = energy;
                mappedStructuralBlocks[0].AreaUm2 = area;
            }
        }
        else
        {
            block.EnergyPicojoules = energy;
            block.AreaUm2 = area;
        }
        MarkEditedDraft(selected);
        library.AddOrReplace(selected);
        SelectTemplate(selected.TemplateId, selected.Version);
        return new ComponentTemplateEditResult(true, Snapshot(), new ComponentTemplateValidator().Validate(selected).Issues, "block updated");
    }

    public ComponentTemplateEditResult UpdateSelectedStorage(string storageId, string rows, string columns, string cellBits, string encoding)
    {
        var selected = SelectedTemplate(library.Templates);
        if (selected is null)
        {
            return EditFailure("No ComponentTemplate is selected.", new("TemplateSelectionMissing", ComponentTemplateIssueSeverity.Error, "$", "No ComponentTemplate is selected."));
        }

        var layout = selected.StorageLayouts.FirstOrDefault(s => string.Equals(s.Id, storageId, StringComparison.OrdinalIgnoreCase));
        if (layout is null)
        {
            return EditFailure("Storage layout not found.", new("TemplateStorageMissing", ComponentTemplateIssueSeverity.Error, "$.storage_layouts", $"Storage layout '{storageId}' was not found.", storageId));
        }

        if (!TryPositiveInt(rows, out var parsedRows) || !TryPositiveInt(columns, out var parsedColumns) || !TryPositiveInt(cellBits, out var parsedCellBits))
        {
            return EditFailure("Rows, columns, and cell_bits must be positive integers.", new("TemplateStorageLayoutInvalid", ComponentTemplateIssueSeverity.Error, "$.storage_layouts[" + layout.Id + "]", "Rows, columns, and cell_bits must be positive integers.", layout.Id));
        }

        CaptureImpactBaseline(selected);
        layout.Rows = parsedRows;
        layout.Columns = parsedColumns;
        layout.CellBits = parsedCellBits;
        layout.Encoding = string.IsNullOrWhiteSpace(encoding) ? layout.Encoding : encoding.Trim();
        SetParameterDefaultIfPresent(selected, "cell_bits", parsedCellBits.ToString(CultureInfo.InvariantCulture));
        MarkEditedDraft(selected);
        library.AddOrReplace(selected);
        SelectTemplate(selected.TemplateId, selected.Version);
        return new ComponentTemplateEditResult(true, Snapshot(), new ComponentTemplateValidator().Validate(selected).Issues, "storage updated");
    }

    public ComponentTemplateEditResult UpdateSelectedTiming(string operationLatency, string pipelineLatency, string issueInterval, string inputQueueDepth, string outputQueueDepth)
    {
        var selected = SelectedTemplate(library.Templates);
        if (selected is null)
        {
            return EditFailure("No ComponentTemplate is selected.", new("TemplateSelectionMissing", ComponentTemplateIssueSeverity.Error, "$", "No ComponentTemplate is selected."));
        }

        if (!TryNonNegativeInt(operationLatency, out var op) || !TryNonNegativeInt(pipelineLatency, out var pipe) || !TryPositiveInt(issueInterval, out var issue) || !TryNonNegativeInt(inputQueueDepth, out var inputDepth) || !TryNonNegativeInt(outputQueueDepth, out var outputDepth))
        {
            return EditFailure("Timing fields must be numeric; issue interval must be positive.", new("TemplateTimingInvalid", ComponentTemplateIssueSeverity.Error, "$.timing_contract", "Timing fields must be numeric; issue interval must be positive."));
        }

        CaptureImpactBaseline(selected);
        selected.TimingContract.OperationLatency = op;
        selected.TimingContract.PipelineLatency = pipe;
        selected.TimingContract.IssueInterval = issue;
        selected.TimingContract.InputQueueDepth = inputDepth;
        selected.TimingContract.OutputQueueDepth = outputDepth;
        SetParameterDefaultIfPresent(selected, "pipeline_latency", pipe.ToString(CultureInfo.InvariantCulture));
        SetParameterDefaultIfPresent(selected, "issue_interval_override", issue.ToString(CultureInfo.InvariantCulture));
        SetParameterDefaultIfPresent(selected, "input_queue_depth", inputDepth.ToString(CultureInfo.InvariantCulture));
        SetParameterDefaultIfPresent(selected, "output_queue_depth", outputDepth.ToString(CultureInfo.InvariantCulture));
        MarkEditedDraft(selected);
        library.AddOrReplace(selected);
        SelectTemplate(selected.TemplateId, selected.Version);
        return new ComponentTemplateEditResult(true, Snapshot(), new ComponentTemplateValidator().Validate(selected).Issues, "timing updated");
    }

    public ComponentTemplateTestDataflowView TestSelectedDataflow(IReadOnlyDictionary<string, string>? overrides = null)
    {
        var selected = SelectedTemplate(library.Templates);
        if (selected is null)
        {
            return TestFailure("No ComponentTemplate is selected.", new("TemplateSelectionMissing", ComponentTemplateIssueSeverity.Error, "$", "No ComponentTemplate is selected."));
        }

        var run = new ComponentKernelTestRunner().Run(selected, componentRegistry, overrides, seed: 7);
        var simulation = run.Simulation;
        var profile = run.Profile;
        var evidence = run.Timeline
            .Select(item => $"cycle={item.Cycle};event={item.Event.Type};{item.Event.Detail ?? string.Empty}")
            .Distinct(StringComparer.Ordinal)
            .OrderBy(item => item, StringComparer.Ordinal)
            .ToList();
        var artifacts = run.Artifacts.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);

        void AddArtifact(string name, string value)
        {
            if (!string.IsNullOrWhiteSpace(value)) artifacts[name] = value;
        }

        AddArtifact("profile_hash", run.ProfileHash);
        AddArtifact("execution_contract_hash", run.ExecutionContractHash);
        AddArtifact("runtime_kernel_registry_hash", run.RuntimeKernelRegistryHash);
        AddArtifact("input_hash", run.InputHash);
        AddArtifact("reference_output_hash", run.ExpectedOutputHash);
        AddArtifact("actual_output_hash", run.ActualOutputHash);
        AddArtifact("trace_hash", run.TraceHash);
        evidence.AddRange(artifacts.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => $"artifact_{pair.Key}={pair.Value}"));

        var blockingMessages = run.Issues
            .Where(issue => issue.Severity is ComponentTemplateIssueSeverity.Error or ComponentTemplateIssueSeverity.Fatal)
            .Select(issue => $"{issue.Code}: {issue.Message}")
            .ToList();
        var completionReason = run.IsSuccess
            ? simulation?.CompletionReason ?? "Component kernel scenario completed."
            : blockingMessages.Count > 0
                ? string.Join("; ", blockingMessages)
                : simulation?.CompletionReason ?? "Component kernel scenario failed.";

        return new ComponentTemplateTestDataflowView(
            run.IsSuccess,
            completionReason,
            run.ProfileHash,
            simulation?.Metrics.Global.TotalCycles ?? 0,
            simulation?.Metrics.Global.PacketsDelivered ?? 0,
            profile?.TotalEnergyPicojoules ?? 0,
            profile?.TotalAreaUm2 ?? 0,
            evidence,
            run.Issues,
            artifacts);
    }

    public ComponentTemplate? FindTemplate(string templateId, string version) => library.Find(templateId, version);

    public IReadOnlyList<ComponentTemplatePlacementOption> PlacementOptions(ComponentTemplateTargetKind targetKind)
    {
        var candidates = library.Templates
            .Where(template => template.TargetKind == targetKind)
            .OrderBy(template => DefaultTemplateSortKey(template), StringComparer.Ordinal)
            .ThenBy(template => template.TemplateId, StringComparer.Ordinal)
            .ThenBy(template => template.Version, StringComparer.Ordinal)
            .ToList();
        var customIndex = 1;
        var defaultAssigned = false;
        var options = new List<ComponentTemplatePlacementOption>();
        foreach (var template in candidates)
        {
            var isDefault = !defaultAssigned && template.TemplateId.Contains("SRAM", StringComparison.OrdinalIgnoreCase);
            if (!isDefault && !defaultAssigned)
            {
                isDefault = true;
            }

            var slot = isDefault ? "Default" : $"Custom {customIndex++}";
            defaultAssigned |= isDefault;
            var placeable = template.Lifecycle is ComponentTemplateLifecycleState.Compiled or ComponentTemplateLifecycleState.Published;
            var summary = PlacementSummary(template);
            options.Add(new ComponentTemplatePlacementOption(
                slot,
                template.TemplateId,
                template.Version,
                template.DisplayName,
                template.TargetKind,
                template.Lifecycle,
                Summary(template).SyntheticProfile,
                placeable,
                summary));
        }

        return options;
    }

    public string ExportPackageJson(string packageId, string packageVersion, IEnumerable<(string TemplateId, string Version)> selectedTemplates) =>
        ComponentTemplatePackageJson.Serialize(library.ExportPackage(packageId, packageVersion, selectedTemplates));

    public IReadOnlyList<ComponentTemplateIssue> ImportPackageJson(string json, ComponentTypeRegistry? plugins = null)
    {
        var package = ComponentTemplatePackageJson.Deserialize(json);
        return library.ImportPackage(package, plugins);
    }

    private static Dictionary<string, string> NormalizeOverrides(IReadOnlyDictionary<string, string>? overrides) =>
        overrides is null
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : overrides.Where(pair => !string.IsNullOrWhiteSpace(pair.Key)).ToDictionary(pair => pair.Key, pair => pair.Value ?? string.Empty, StringComparer.OrdinalIgnoreCase);

    private static IReadOnlyList<ComponentTemplateIssue> ApplyParameterDefaults(ComponentTemplate selected, IReadOnlyDictionary<string, string> overrides)
    {
        var issues = new List<ComponentTemplateIssue>();
        foreach (var pair in overrides.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            var parameter = selected.Parameters.FirstOrDefault(p => string.Equals(p.Name, pair.Key, StringComparison.OrdinalIgnoreCase));
            if (parameter is null)
            {
                issues.Add(new("TemplateParameterMissing", ComponentTemplateIssueSeverity.Error, "$.parameters", $"Parameter '{pair.Key}' was not found.", pair.Key));
                continue;
            }

            if (!TemplateParameterValueIsValid(parameter, pair.Value, out var message))
            {
                issues.Add(new("TemplateParameterInvalid", ComponentTemplateIssueSeverity.Error, "$.parameters[" + parameter.Name + "].default_value", message, parameter.Name));
                continue;
            }

            parameter.DefaultValue = pair.Value;
        }

        if (overrides.Count > 0)
        {
            selected.CompiledProfile = null;
            selected.Provenance.CompileHash = "";
        }

        return issues;
    }

    private void CaptureImpactBaseline(ComponentTemplate template)
    {
        var key = ImpactBaselineKey(template);
        if (!impactBaselineTemplates.ContainsKey(key))
        {
            impactBaselineTemplates[key] = ComponentTemplateJson.Clone(template);
        }
    }

    private void ClearImpactBaseline(ComponentTemplate template) => impactBaselineTemplates.Remove(ImpactBaselineKey(template));

    private static string ImpactBaselineKey(ComponentTemplate template) => template.TemplateId + "\u001f" + template.Version;

    private IReadOnlyList<ComponentTemplateImpactChange> BuildImpactChanges(ComponentTemplate selected, IReadOnlyDictionary<string, string> overrides, CompiledComponentProfile? targetProfile = null, IReadOnlyDictionary<string, string>? targetDerived = null)
    {
        impactBaselineTemplates.TryGetValue(ImpactBaselineKey(selected), out var baselineTemplate);
        if (baselineTemplate is null && overrides.Count == 0)
        {
            return [];
        }

        var baselineSource = baselineTemplate ?? selected;
        var baseline = CompileTemplate(baselineSource);
        var target = targetProfile is null && targetDerived is null ? CompileTemplate(selected, overrides) : null;
        var effectiveTargetProfile = targetProfile ?? target?.Profile;
        var targetMetrics = effectiveTargetProfile?.DerivedMetrics ?? targetDerived ?? target?.DerivedMetrics ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var baselineMetrics = baseline.Profile?.DerivedMetrics ?? baseline.DerivedMetrics;
        var changes = new List<ComponentTemplateImpactChange>();
        foreach (var pair in overrides.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            var before = "";
            if (baseline.Profile is not null && baseline.Profile.InstanceOverrides.TryGetValue(pair.Key, out var baselineValue))
            {
                before = baselineValue;
            }
            else
            {
                before = baselineSource.Parameters.FirstOrDefault(parameter => string.Equals(parameter.Name, pair.Key, StringComparison.OrdinalIgnoreCase))?.DefaultValue ?? "";
            }

            if (!string.Equals(before, pair.Value, StringComparison.OrdinalIgnoreCase))
            {
                changes.Add(new ComponentTemplateImpactChange(pair.Key, before, pair.Value));
            }
        }

        var overridden = overrides.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var key in baselineMetrics.Keys.Concat(targetMetrics.Keys).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(key => key, StringComparer.Ordinal))
        {
            if (overridden.Contains(key)) continue;
            baselineMetrics.TryGetValue(key, out var before);
            targetMetrics.TryGetValue(key, out var after);
            if (!string.Equals(before ?? "", after ?? "", StringComparison.Ordinal))
            {
                changes.Add(new ComponentTemplateImpactChange(key, before ?? "", after ?? ""));
            }
        }

        if (baseline.Profile is not null && effectiveTargetProfile is not null)
        {
            AddLongMapImpactChanges(changes, "capacity.", baseline.Profile.Capacity, effectiveTargetProfile.Capacity);
            AddDoubleMapImpactChanges(changes, "bandwidth_bits_per_cycle.", baseline.Profile.BandwidthBitsPerCycle, effectiveTargetProfile.BandwidthBitsPerCycle);
            AddDoubleMapImpactChanges(changes, "energy_pj.", baseline.Profile.EnergyPicojoules, effectiveTargetProfile.EnergyPicojoules);
            AddDoubleMapImpactChanges(changes, "area_um2.", baseline.Profile.AreaUm2, effectiveTargetProfile.AreaUm2);
        }

        var targetHash = effectiveTargetProfile?.ProfileHash ?? "";
        var baselineHash = baseline.Profile?.ProfileHash ?? "";
        if (!string.IsNullOrWhiteSpace(targetHash) && !string.Equals(baselineHash, targetHash, StringComparison.Ordinal))
        {
            changes.Add(new ComponentTemplateImpactChange("profile_hash", baselineHash, targetHash));
        }

        return changes;
    }

    private static void AddLongMapImpactChanges(List<ComponentTemplateImpactChange> changes, string prefix, IReadOnlyDictionary<string, long> baseline, IReadOnlyDictionary<string, long> target)
    {
        foreach (var key in baseline.Keys.Concat(target.Keys).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(key => key, StringComparer.Ordinal))
        {
            var hasBefore = baseline.TryGetValue(key, out var before);
            var hasAfter = target.TryGetValue(key, out var after);
            if (hasBefore != hasAfter || before != after)
            {
                changes.Add(new ComponentTemplateImpactChange(prefix + key, hasBefore ? before.ToString(CultureInfo.InvariantCulture) : "", hasAfter ? after.ToString(CultureInfo.InvariantCulture) : ""));
            }
        }
    }

    private static void AddDoubleMapImpactChanges(List<ComponentTemplateImpactChange> changes, string prefix, IReadOnlyDictionary<string, double> baseline, IReadOnlyDictionary<string, double> target)
    {
        foreach (var key in baseline.Keys.Concat(target.Keys).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(key => key, StringComparer.Ordinal))
        {
            var hasBefore = baseline.TryGetValue(key, out var before);
            var hasAfter = target.TryGetValue(key, out var after);
            if (hasBefore != hasAfter || before != after)
            {
                changes.Add(new ComponentTemplateImpactChange(prefix + key, hasBefore ? before.ToString("0.######", CultureInfo.InvariantCulture) : "", hasAfter ? after.ToString("0.######", CultureInfo.InvariantCulture) : ""));
            }
        }
    }
    private ComponentTemplate? SelectedTemplate(IReadOnlyList<ComponentTemplate> templates)
    {
        if (!string.IsNullOrWhiteSpace(selectedTemplateId))
        {
            var selected = templates.FirstOrDefault(template =>
                string.Equals(template.TemplateId, selectedTemplateId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(template.Version, selectedVersion, StringComparison.OrdinalIgnoreCase));
            if (selected is not null)
            {
                return selected;
            }
        }

        var first = templates.OrderBy(template => template.TemplateId, StringComparer.Ordinal).ThenBy(template => template.Version, StringComparer.Ordinal).FirstOrDefault();
        if (first is not null)
        {
            selectedTemplateId = first.TemplateId;
            selectedVersion = first.Version;
        }

        return first;
    }

    private ComponentTemplateEditResult EditFailure(string message, ComponentTemplateIssue issue) => new(false, Snapshot(), [issue], message);

    private static ComponentTemplateTestDataflowView TestFailure(string message, ComponentTemplateIssue issue) => new(false, message, "", 0, 0, 0, 0, [], [issue]);

    private static ComponentTemplateSummaryView Summary(ComponentTemplate template) => new(
        template.TemplateId,
        template.Version,
        template.DisplayName,
        template.TargetKind,
        template.Lifecycle,
        template.ProfileBindings.Any(binding => binding.Synthetic) || template.Provenance.Warnings.Any(warning => warning.Contains("Synthetic", StringComparison.OrdinalIgnoreCase)),
        template.Category);

    private static ComponentTemplatePortView Port(TemplateExternalPort port) => new(
        port.Name,
        port.Direction,
        port.SignalType,
        port.DataType,
        port.Precision,
        port.Protocol,
        $"{port.ShellBlockId}.{port.ShellPortName}");

    private static ComponentTemplateParameterView Parameter(TemplateParameter parameter) => new(
        parameter.Name,
        parameter.ValueKind,
        parameter.DefaultValue,
        parameter.Units,
        string.Join(",", parameter.AllowedValues.OrderBy(value => value, StringComparer.Ordinal)),
        parameter.Required);

    private static ComponentTemplateBlockView Block(ComponentTemplate template, InternalBlock block)
    {
        var metrics = EffectiveBlockMetrics(template, block);
        return new ComponentTemplateBlockView(
            block.Id,
            block.DisplayName,
            block.BlockKind,
            block.Layer,
            block.TraceStage,
            metrics.EnergyPicojoules,
            metrics.AreaUm2,
            block.MappedStructuralBlockIds.OrderBy(id => id, StringComparer.Ordinal).ToList(),
            block.Ports.OrderBy(port => port.Name, StringComparer.Ordinal).Select(port => port.Name).ToList(),
            block.Ports.OrderBy(port => port.Name, StringComparer.Ordinal).Select(Port).ToList());
    }

    private static (double EnergyPicojoules, double AreaUm2) EffectiveBlockMetrics(ComponentTemplate template, InternalBlock block)
    {
        var mapped = MappedStructuralBlocks(template, block);
        return block.Layer == InternalBlockLayer.Dataflow && mapped.Count > 0
            ? (mapped.Sum(item => item.EnergyPicojoules), mapped.Sum(item => item.AreaUm2))
            : (block.EnergyPicojoules, block.AreaUm2);
    }

    private static List<InternalBlock> MappedStructuralBlocks(ComponentTemplate template, InternalBlock block)
    {
        if (block.Layer != InternalBlockLayer.Dataflow || block.MappedStructuralBlockIds.Count == 0)
        {
            return [];
        }

        var mappedIds = block.MappedStructuralBlockIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return template.InternalBlocks
            .Where(item => item.Layer == InternalBlockLayer.Structural && mappedIds.Contains(item.Id))
            .OrderBy(item => item.Id, StringComparer.Ordinal)
            .ToList();
    }

    private static ComponentTemplateInternalPortView Port(InternalPort port) => new(
        port.Name,
        port.Direction,
        port.SignalType,
        port.DataType,
        port.Precision,
        port.Protocol,
        port.WidthBits);

    private static ComponentTemplateConnectionView Connection(InternalConnection connection) => new(
        connection.Id,
        $"{connection.SourceBlockId}.{connection.SourcePortName}",
        $"{connection.TargetBlockId}.{connection.TargetPortName}",
        connection.PayloadType,
        connection.Shape.Count == 0 ? "scalar" : string.Join("x", connection.Shape),
        connection.Precision,
        connection.RatePerCycle,
        connection.LatencyCycles,
        connection.BandwidthBitsPerCycle,
        connection.BackpressureBehavior);

    private static IReadOnlyDictionary<string, GridPosition> ViewLayout(ComponentTemplate template, TemplateViewKind kind) =>
        template.Views.FirstOrDefault(view => view.Kind == kind)?.Layout.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase) ?? new Dictionary<string, GridPosition>(StringComparer.OrdinalIgnoreCase);

    private static TemplateView EnsureView(ComponentTemplate template, TemplateViewKind kind)
    {
        var view = template.Views.FirstOrDefault(item => item.Kind == kind);
        if (view is not null)
        {
            return view;
        }

        view = new TemplateView { Kind = kind };
        template.Views.Add(view);
        return view;
    }

    private static GridPosition NextLayoutPosition(ComponentTemplate template, InternalBlockLayer layer, TemplateViewKind kind)
    {
        var view = EnsureView(template, kind);
        var ids = template.InternalBlocks.Where(block => block.Layer == layer).Select(block => block.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var used = view.Layout.Where(pair => ids.Contains(pair.Key)).Select(pair => pair.Value).ToList();
        var next = used.Count == 0 ? 0 : used.Max(position => position.X + position.Y * 4) + 1;
        return new GridPosition(next % 4, next / 4);
    }

    private static string NextInternalBlockId(ComponentTemplate template, string prefix)
    {
        var existing = template.InternalBlocks.Select(block => block.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        for (var i = 1; i < 1000; i++)
        {
            var candidate = prefix + "_" + i.ToString(CultureInfo.InvariantCulture);
            if (!existing.Contains(candidate))
            {
                return candidate;
            }
        }

        return prefix + "_" + DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
    }

    private static string NextInternalConnectionId(ComponentTemplate template, string prefix)
    {
        var existing = template.InternalConnections.Select(connection => connection.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        for (var i = 1; i < 1000; i++)
        {
            var candidate = prefix + "_" + i.ToString(CultureInfo.InvariantCulture);
            if (!existing.Contains(candidate))
            {
                return candidate;
            }
        }

        return prefix + "_overflow";
    }
    private static string HumanizeBlockKind(string kind)
    {
        if (string.IsNullOrWhiteSpace(kind))
        {
            return "Internal Block";
        }

        var chars = kind.Replace('_', ' ').Replace('-', ' ').ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            if (i == 0 || chars[i - 1] == ' ')
            {
                chars[i] = char.ToUpperInvariant(chars[i]);
            }
        }

        return new string(chars);
    }

    private static ComponentTemplateStorageView Storage(TemplateStorageLayout layout) => new(
        layout.Id,
        layout.LogicalName,
        layout.Banks,
        layout.Rows,
        layout.Columns,
        layout.CellBits,
        layout.Encoding,
        layout.CapacityBits);

    private static ComponentTemplateTimingView Timing(TemplateTimingContract timing) => new(
        timing.OperationLatency,
        timing.PipelineLatency,
        timing.IssueInterval,
        timing.InputQueueDepth,
        timing.OutputQueueDepth,
        timing.DefaultResponseTargetPolicy,
        timing.CanAcceptWhileBusy,
        timing.OutputBackpressureBehavior);

    private static ComponentTemplateOperationView Operation(TemplateOperationContract operation) => new(
        operation.OperationName,
        operation.Equation,
        operation.MultiplyDType,
        operation.AccumulateDType,
        operation.OutputDType,
        operation.Quantization.Mode,
        operation.Quantization.Saturation,
        operation.InputOperands.Select(OperandSummary).ToList(),
        operation.StoredOperands.Select(OperandSummary).ToList(),
        operation.OutputOperands.Select(OperandSummary).ToList());

    private static ComponentTemplateProfileBindingView ProfileBinding(TemplateProfileBinding binding)
    {
        binding.ExtensionData.TryGetValue("phase9_provenance", out var provenance);
        var snapshot = binding.Snapshot;
        return new ComponentTemplateProfileBindingView(
            binding.BindingId,
            binding.BlockId,
            binding.ProfileId,
            snapshot?.Hash ?? "",
            snapshot?.OutputQuantity ?? "",
            snapshot?.Units ?? "",
            snapshot?.Value,
            JsonProperty(provenance, "evidence_status", binding.Synthetic ? "Estimated" : "Unknown"),
            JsonProperty(provenance, "valid_range", "exact characterized point only"),
            JsonProperty(provenance, "uncertainty", "unknown"),
            snapshot?.Source ?? "",
            binding.Synthetic);
    }

    private static string JsonProperty(System.Text.Json.JsonElement element, string name, string fallback)
    {
        if (element.ValueKind == System.Text.Json.JsonValueKind.Object &&
            element.TryGetProperty(name, out var value))
        {
            return value.ValueKind == System.Text.Json.JsonValueKind.String
                ? value.GetString() ?? fallback
                : value.ToString();
        }

        return fallback;
    }

    private static IReadOnlyList<string> ProfileEvidenceGaps(CompiledComponentProfile profile)
    {
        var gaps = new List<string>();
        if (profile.PhysicalFootprint is null || !profile.PhysicalFootprint.IsKnown)
            gaps.Add("physical footprint is unknown");
        if (profile.ExecutionContract is null || profile.ExecutionContract.Provenance.ProfileSnapshotHashes.Count == 0)
            gaps.Add("no normalized device profile snapshot is frozen into the runtime contract");
        if (profile.Provenance.TryGetValue("synthetic_profile", out var synthetic) &&
            string.Equals(synthetic, "true", StringComparison.OrdinalIgnoreCase))
            gaps.Add("synthetic characterization is active");
        return gaps;
    }
    private static ComponentTemplateFootprintView? Footprint(PhysicalFootprint? footprint) =>
        footprint is null ? null : new ComponentTemplateFootprintView(
            footprint.IsKnown,
            footprint.Scope.ToString(),
            footprint.AreaUm2,
            footprint.WidthUm,
            footprint.HeightUm,
            footprint.SourceKind.ToString(),
            footprint.EvidenceStatus.ToString(),
            footprint.Uncertainty,
            footprint.ValidContext,
            footprint.FootprintHash);
    private static ComponentTemplateCompiledProfileView Profile(CompiledComponentProfile profile) => new(
        profile.TemplateId,
        profile.TemplateVersion,
        profile.ProfileHash,
        profile.OperationLatency,
        profile.PipelineLatency,
        profile.IssueInterval,
        profile.InputQueueDepth,
        profile.OutputQueueDepth,
        profile.DefaultResponseTargetPolicy,
        profile.TotalEnergyPicojoules,
        profile.TotalAreaUm2,
        new Dictionary<string, double>(profile.EnergyPicojoules, StringComparer.OrdinalIgnoreCase),
        profile.TraceDescriptors.ToList(),
        profile.InternalDrilldownStages.ToList(),
        profile.Provenance.TryGetValue("synthetic_profile", out var synthetic) && string.Equals(synthetic, "true", StringComparison.OrdinalIgnoreCase),
        new Dictionary<string, string>(profile.DerivedMetrics, StringComparer.OrdinalIgnoreCase),
        profile.SupportedOperations.ToList(),
        new Dictionary<string, string>(profile.ShapeContract, StringComparer.OrdinalIgnoreCase),
        new Dictionary<string, long>(profile.Capacity, StringComparer.OrdinalIgnoreCase),
        profile.ExecutionContract is null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : new Dictionary<string, string>(profile.ExecutionContract.Provenance.ProfileSnapshotHashes, StringComparer.Ordinal),
        Footprint(profile.PhysicalFootprint),
        ProfileEvidenceGaps(profile));

    private static string OperandSummary(TemplateOperandContract operand)
    {
        var shape = operand.Shape.Count == 0 ? "scalar" : string.Join("x", operand.Shape);
        var storage = string.IsNullOrWhiteSpace(operand.StorageRef) ? "" : " @ " + operand.StorageRef;
        return operand.Name + ": " + shape + " " + operand.DType + storage;
    }

    private static void SetParameterDefaultIfPresent(ComponentTemplate template, string name, string value)
    {
        var parameter = template.Parameters.FirstOrDefault(item => string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase));
        if (parameter is not null)
        {
            parameter.DefaultValue = value;
        }
    }
    private static void MarkEditedDraft(ComponentTemplate template)
    {
        template.Lifecycle = ComponentTemplateLifecycleState.Draft;
        template.CompiledProfile = null;
        template.Provenance.CompileHash = "";
    }

    private static bool TemplateParameterValueIsValid(TemplateParameter parameter, string value, out string message)
    {
        message = "";
        if (parameter.ValueKind == TemplateParameterValueKind.Enum && !parameter.AllowedValues.Any(allowed => string.Equals(allowed, value, StringComparison.OrdinalIgnoreCase)))
        {
            message = "Value must be one of: " + string.Join(", ", parameter.AllowedValues);
            return false;
        }

        if (parameter.ValueKind is TemplateParameterValueKind.Integer or TemplateParameterValueKind.Number)
        {
            if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) || double.IsNaN(parsed) || double.IsInfinity(parsed))
            {
                message = "Value must be numeric.";
                return false;
            }

            if (parameter.ValueKind == TemplateParameterValueKind.Integer && Math.Abs(parsed - Math.Round(parsed)) > 0)
            {
                message = "Value must be an integer.";
                return false;
            }

            if ((parameter.Minimum.HasValue && parsed < parameter.Minimum.Value) || (parameter.Maximum.HasValue && parsed > parameter.Maximum.Value))
            {
                message = "Value is outside the parameter range.";
                return false;
            }
        }

        if (parameter.ValueKind == TemplateParameterValueKind.Boolean && !bool.TryParse(value, out _))
        {
            message = "Value must be true or false.";
            return false;
        }

        return true;
    }

    private static bool TryPositiveInt(string raw, out int value) => int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out value) && value > 0;

    private static bool TryNonNegativeInt(string raw, out int value) => int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out value) && value >= 0;

    private static bool TryNonNegativeDouble(string raw, out double value) => double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && !double.IsNaN(value) && !double.IsInfinity(value) && value >= 0;

    private static List<int> ParseShape(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw) || raw.Equals("scalar", StringComparison.OrdinalIgnoreCase))
        {
            return [];
        }

        return raw
            .Split(new[] { 'x', 'X', ',', ';', ' ', '[', ']' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(item => int.TryParse(item.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? Math.Max(1, value) : 1)
            .ToList();
    }

    private string NextCustomSuffix(string prefix)
    {
        var existing = library.Templates.Select(template => template.TemplateId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        for (var i = 1; i < 10000; i++)
        {
            if (!existing.Contains(prefix + i.ToString(CultureInfo.InvariantCulture)))
            {
                return i.ToString(CultureInfo.InvariantCulture);
            }
        }

        return DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
    }

    private static string SafeId(string value)
    {
        var chars = value.Select(ch => char.IsLetterOrDigit(ch) ? ch : '_').ToArray();
        return new string(chars).Trim('_');
    }

    private static string ViewLabel(TemplateViewKind kind) => kind switch
    {
        TemplateViewKind.Symbol => "Symbol View",
        TemplateViewKind.Dataflow => "Dataflow View",
        TemplateViewKind.StructuralPort => "Structural/Port View",
        TemplateViewKind.ModelProfile => "Model/Profile View",
        TemplateViewKind.Storage => "Storage View",
        TemplateViewKind.CompiledProfile => "Compiled Profile View",
        _ => kind.ToString()
    };

    private static string DefaultTemplateSortKey(ComponentTemplate template) =>
        template.TemplateId.Contains("SRAM", StringComparison.OrdinalIgnoreCase) ? "0" : "1";

    private string PlacementSummary(ComponentTemplate template)
    {
        var profile = template.CompiledProfile ?? CompileTemplate(template).Profile;
        var storage = template.StorageLayouts.FirstOrDefault();
        var shape = profile is null
            ? "shape=pending"
            : string.Join("; ", profile.ShapeContract.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => pair.Key + "=" + pair.Value));
        var storageText = storage is null
            ? "storage=none"
            : $"storage={storage.Rows}x{storage.Columns} cell_bits={storage.CellBits} {storage.Encoding}";
        var synthetic = Summary(template).SyntheticProfile ? "synthetic" : "profiled";
        return $"{synthetic}; {shape}; {storageText}";
    }
}
