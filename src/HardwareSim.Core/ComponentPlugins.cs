using System.Collections.ObjectModel;

namespace HardwareSim.Core;

/// <summary>Identifies the trust boundary for a component plugin descriptor.</summary>
public enum ComponentPluginSourceKind
{
    /// <summary>The plugin is shipped by the simulator and loaded explicitly by first-party code.</summary>
    FirstParty,
    /// <summary>The plugin is external to the simulator and is disabled unless policy explicitly permits it.</summary>
    External
}

/// <summary>Defines stable runtime parameter keys emitted by plugin compilation.</summary>
public static class ComponentPluginRuntimeKeys
{
    /// <summary>Parameter key storing the stable plugin type id on compiled components.</summary>
    public const string TypeId = "plugin_type_id";
    /// <summary>Parameter key storing runtime latency supplied by a plugin runtime factory.</summary>
    public const string ProcessingLatencyCycles = "plugin_runtime_latency_cycles";
    /// <summary>Parameter key storing the runtime energy category supplied by a plugin runtime factory.</summary>
    public const string EnergyCategory = "plugin_energy_category";
    /// <summary>Parameter key storing plugin trace descriptor names.</summary>
    public const string TraceDescriptors = "plugin_trace_descriptors";
    /// <summary>Parameter key storing plugin metric descriptor names.</summary>
    public const string MetricDescriptors = "plugin_metric_descriptors";
    /// <summary>Parameter key storing plugin per-packet energy in picojoules.</summary>
    public const string EnergyPicojoulesPerPacket = "plugin_energy_pj_per_packet";
    /// <summary>Parameter key requesting the approved legacy runtime compatibility path instead of an exact plugin execution contract.</summary>
    public const string LegacyRuntimeCompatibility = "plugin_legacy_runtime_compatibility";
    /// <summary>Parameter key storing plugin per-bit energy in picojoules.</summary>
    public const string EnergyPicojoulesPerBit = "plugin_energy_pj_per_bit";
}

/// <summary>Maps legacy built-in component kinds to stable first-party type ids.</summary>
public static class ComponentTypeIds
{
    private static readonly IReadOnlyDictionary<ComponentKind, string> BuiltInTypeIds = new Dictionary<ComponentKind, string>
    {
        [ComponentKind.ProcessingElement] = "com.hardware-sim.builtin.processing-element",
        [ComponentKind.Router] = "com.hardware-sim.builtin.router",
        [ComponentKind.Buffer] = "com.hardware-sim.builtin.buffer",
        [ComponentKind.Memory] = "com.hardware-sim.builtin.memory",
        [ComponentKind.LinkEndpoint] = "com.hardware-sim.builtin.link-endpoint",
        [ComponentKind.ReductionUnit] = "com.hardware-sim.builtin.reduction-unit",
        [ComponentKind.SoftmaxUnit] = "com.hardware-sim.builtin.softmax-unit",
        [ComponentKind.WorkloadSource] = "com.hardware-sim.builtin.workload-source",
        [ComponentKind.WorkloadSink] = "com.hardware-sim.builtin.workload-sink",
        [ComponentKind.Adapter] = "com.hardware-sim.builtin.adapter",
        [ComponentKind.PrecisionConverter] = "com.hardware-sim.builtin.precision-converter",
        [ComponentKind.Quantizer] = "com.hardware-sim.builtin.quantizer",
        [ComponentKind.Dequantizer] = "com.hardware-sim.builtin.dequantizer",
        [ComponentKind.OpticalLink] = "com.hardware-sim.first-party.optical.link",
        [ComponentKind.Laser] = "com.hardware-sim.first-party.optical.laser",
        [ComponentKind.MrrRouter] = "com.hardware-sim.first-party.optical.mrr-router",
        [ComponentKind.MziSwitch] = "com.hardware-sim.first-party.optical.mzi-switch",
        [ComponentKind.Splitter] = "com.hardware-sim.first-party.optical.splitter",
        [ComponentKind.Combiner] = "com.hardware-sim.first-party.optical.combiner",
        [ComponentKind.Photodetector] = "com.hardware-sim.first-party.optical.photodetector",
        [ComponentKind.Modulator] = "com.hardware-sim.first-party.optical.modulator",
        [ComponentKind.WdmMux] = "com.hardware-sim.first-party.optical.wdm-mux",
        [ComponentKind.WdmDemux] = "com.hardware-sim.first-party.optical.wdm-demux",
        [ComponentKind.EoConverter] = "com.hardware-sim.first-party.optical.eo-converter",
        [ComponentKind.OeConverter] = "com.hardware-sim.first-party.optical.oe-converter",
        [ComponentKind.ReRamCrossbar] = "com.hardware-sim.first-party.cim.reram-crossbar",
        [ComponentKind.FeFetCrossbar] = "com.hardware-sim.first-party.cim.fefet-crossbar",
        [ComponentKind.Adc] = "com.hardware-sim.first-party.cim.adc",
        [ComponentKind.Dac] = "com.hardware-sim.first-party.cim.dac",
        [ComponentKind.AnalogAccumulator] = "com.hardware-sim.first-party.cim.analog-accumulator",
        [ComponentKind.SenseAmplifier] = "com.hardware-sim.first-party.cim.sense-amplifier",
        [ComponentKind.WriteDriver] = "com.hardware-sim.first-party.cim.write-driver",
        [ComponentKind.Macro] = "com.hardware-sim.builtin.macro",
        [ComponentKind.Custom] = "com.hardware-sim.builtin.custom"
    };

    private static readonly IReadOnlyDictionary<string, ComponentKind> KindByTypeId = BuiltInTypeIds
        .ToDictionary(pair => pair.Value, pair => pair.Key, StringComparer.OrdinalIgnoreCase);

    /// <summary>Returns the stable first-party type id for a legacy built-in component kind.</summary>
    public static string BuiltIn(ComponentKind kind) => BuiltInTypeIds.TryGetValue(kind, out var typeId)
        ? typeId
        : BuiltInTypeIds[ComponentKind.Custom];

    /// <summary>Normalizes a stable type id for registry lookups.</summary>
    public static string Normalize(string? typeId) => string.IsNullOrWhiteSpace(typeId) ? "" : typeId.Trim();

    /// <summary>Returns the effective stable type id for a design-time component.</summary>
    public static string EffectiveTypeId(HardwareComponent component) => !string.IsNullOrWhiteSpace(component.TypeId)
        ? Normalize(component.TypeId)
        : BuiltIn(component.Type);

    /// <summary>Returns the effective stable type id for a compiled component.</summary>
    public static string EffectiveTypeId(SimComponentDef component) => !string.IsNullOrWhiteSpace(component.TypeId)
        ? Normalize(component.TypeId)
        : BuiltIn(component.Type);

    /// <summary>Attempts to resolve a stable type id back to a legacy built-in kind.</summary>
    public static bool TryGetBuiltInKind(string? typeId, out ComponentKind kind) =>
        KindByTypeId.TryGetValue(Normalize(typeId), out kind);

    /// <summary>Gets whether the supplied type id belongs to a built-in or first-party legacy bridge kind.</summary>
    public static bool IsBuiltInTypeId(string? typeId) => TryGetBuiltInKind(typeId, out _);

    /// <summary>Gets whether the supplied type id belongs to a first-party Optical or CIM plugin package.</summary>
    public static bool IsFirstPartyExtensionTypeId(string? typeId)
    {
        var normalized = Normalize(typeId);
        return normalized.StartsWith("com.hardware-sim.first-party.optical.", StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith("com.hardware-sim.first-party.cim.", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Gets whether the legacy kind is backed by a first-party Optical or CIM plugin package.</summary>
    public static bool IsFirstPartyExtensionKind(ComponentKind kind) => IsFirstPartyExtensionTypeId(BuiltIn(kind));
}
/// <summary>Describes one component port exposed by a plugin component type.</summary>
/// <param name="Name">Stable port name used in graph links.</param>
/// <param name="Direction">Direction allowed for this port.</param>
/// <param name="SignalType">Signal domain carried by this port.</param>
/// <param name="DataType">Data contract carried by this port.</param>
/// <param name="Precision">Precision contract carried by this port.</param>
/// <param name="Protocol">Transport protocol carried by this port.</param>
/// <param name="BandwidthBitsPerCycle">Nominal bandwidth in bits per cycle.</param>
/// <param name="LatencyCycles">Nominal port-local latency in cycles.</param>
/// <param name="Required">Whether this port must be connected before compilation.</param>
/// <param name="MultiConnect">Whether multiple links may connect to this port.</param>
/// <param name="Quantity">Stable physical or logical quantity carried by the port.</param>
/// <param name="Units">Canonical units for the carried quantity, or an empty string when unitless.</param>
public sealed record ComponentPortSchema(
    string Name,
    PortDirection Direction,
    SignalType SignalType = SignalType.Digital,
    HardwareDataType DataType = HardwareDataType.Packet,
    PrecisionKind Precision = PrecisionKind.Any,
    PortProtocol Protocol = PortProtocol.Packet,
    int BandwidthBitsPerCycle = ComponentDefaults.LinkBandwidthBitsPerCycle,
    int LatencyCycles = 0,
    bool Required = false,
    bool MultiConnect = false,
    string Quantity = "",
    string Units = "");

/// <summary>Describes one configurable parameter exposed by a plugin component type.</summary>
/// <param name="Name">Stable parameter key persisted in HardwareGraph parameters.</param>
/// <param name="DefaultValue">Default value serialized as a deterministic invariant string.</param>
/// <param name="Units">Physical units for the value, or an empty string for unitless values.</param>
/// <param name="Minimum">Inclusive numeric lower bound when the parameter is numeric.</param>
/// <param name="Maximum">Inclusive numeric upper bound when the parameter is numeric.</param>
/// <param name="Required">Whether the parameter must be present.</param>
/// <param name="Description">Human-readable parameter description.</param>
/// <param name="AllowedValues">Optional finite set of canonical string values.</param>
/// <param name="IntegerOnly">Whether a numeric value must be integral.</param>
public sealed record ComponentParameterSchema(
    string Name,
    string DefaultValue,
    string Units = "",
    double? Minimum = null,
    double? Maximum = null,
    bool Required = false,
    string Description = "",
    IReadOnlyList<string>? AllowedValues = null,
    bool IntegerOnly = false);

/// <summary>Describes one trace event kind emitted or enriched by a plugin runtime.</summary>
/// <param name="Name">Stable trace descriptor name.</param>
/// <param name="EventType">Trace event type used by the runtime.</param>
/// <param name="Description">Human-readable trace descriptor description.</param>
public sealed record ComponentTraceDescriptor(string Name, TraceEventType EventType, string Description);

/// <summary>Describes one metric emitted or enriched by a plugin runtime.</summary>
/// <param name="Name">Stable metric descriptor name.</param>
/// <param name="Units">Metric units.</param>
/// <param name="Category">System-level energy category associated with the metric when relevant.</param>
/// <param name="Description">Human-readable metric descriptor description.</param>
public sealed record ComponentMetricDescriptor(string Name, string Units, EnergyCategory Category, string Description);

/// <summary>Describes a primitive block exported for the future Phase 7C template palette.</summary>
/// <param name="PrimitiveId">Stable primitive block id.</param>
/// <param name="DisplayName">Display name shown by template tooling.</param>
/// <param name="Category">Template palette category.</param>
/// <param name="PortSchemas">Ports available inside a component template.</param>
/// <param name="ParameterSchemas">Parameters available inside a component template.</param>
public sealed record ComponentTemplatePrimitiveDescriptor(
    string PrimitiveId,
    string DisplayName,
    string Category,
    IReadOnlyList<ComponentPortSchema> PortSchemas,
    IReadOnlyList<ComponentParameterSchema> ParameterSchemas);

/// <summary>Describes a compiled profile factory exported for future template collapse.</summary>
/// <param name="FactoryId">Stable profile factory id.</param>
/// <param name="ProfileKind">Profile kind produced by the factory.</param>
/// <param name="Version">Factory contract version.</param>
/// <param name="Description">Human-readable factory description.</param>
public sealed record CompiledProfileFactoryDescriptor(
    string FactoryId,
    string ProfileKind,
    string Version,
    string Description);

/// <summary>Describes Unity-facing presentation metadata without depending on Unity assemblies.</summary>
/// <param name="Glyph">Stable glyph or icon key.</param>
/// <param name="ColorHex">Presentation color encoded as #RRGGBB.</param>
/// <param name="Abbreviation">Short node label.</param>
/// <param name="Summary">Short UI summary.</param>
/// <param name="SortOrder">Deterministic palette sort order.</param>
public sealed record UnityPresentationDescriptor(
    string Glyph,
    string ColorHex,
    string Abbreviation,
    string Summary,
    int SortOrder);

/// <summary>Represents one structured diagnostic emitted by plugin registration or providers.</summary>
/// <param name="Code">Stable machine-readable issue code.</param>
/// <param name="Severity">Issue severity.</param>
/// <param name="Location">JSON-style location or registry location.</param>
/// <param name="Message">Human-readable diagnostic message.</param>
/// <param name="RelatedId">Optional related plugin, component, or descriptor id.</param>
public sealed record ComponentPluginIssue(
    string Code,
    ValidationSeverity Severity,
    string Location,
    string Message,
    string? RelatedId = null);

/// <summary>Context passed to plugin validation providers.</summary>
/// <param name="Plugin">Plugin descriptor being validated.</param>
/// <param name="Component">Component instance being validated.</param>
/// <param name="Graph">Graph containing the component.</param>
public sealed record ComponentValidationContext(
    ComponentPluginDescriptor Plugin,
    HardwareComponent Component,
    HardwareGraph Graph);

/// <summary>Context passed to plugin compile providers.</summary>
/// <param name="Plugin">Plugin descriptor being compiled.</param>
/// <param name="Component">Component instance being compiled.</param>
/// <param name="Graph">Graph containing the component.</param>
public sealed record ComponentCompileContext(
    ComponentPluginDescriptor Plugin,
    HardwareComponent Component,
    HardwareGraph Graph);

/// <summary>Context passed to plugin runtime factories.</summary>
/// <param name="Plugin">Plugin descriptor owning the runtime.</param>
/// <param name="Component">Compiled component instance.</param>
/// <param name="Graph">Compiled simulation graph containing the component.</param>
public sealed record ComponentRuntimeFactoryContext(
    ComponentPluginDescriptor Plugin,
    SimComponentDef Component,
    HardwareSimulationGraph Graph);

/// <summary>Result returned by a plugin compile provider.</summary>
public sealed class ComponentCompileProviderResult
{
    /// <summary>Gets compile diagnostics emitted by the provider.</summary>
    public IReadOnlyList<ComponentPluginIssue> Issues { get; init; } = [];
    /// <summary>Gets deterministic compile-time parameters supplied by the provider.</summary>
    public IReadOnlyDictionary<string, string> Parameters { get; init; } =
        new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
    /// <summary>Gets whether no error-severity issue was emitted.</summary>
    public bool IsSuccess => Issues.All(issue => issue.Severity != ValidationSeverity.Error);

    /// <summary>Creates a successful empty compile provider result.</summary>
    public static ComponentCompileProviderResult Empty() => new();
}

/// <summary>Describes the deterministic runtime behavior exported by a plugin runtime factory.</summary>
public sealed class ComponentSimulationRuntimeDescriptor
{
    /// <summary>Gets the component processing latency in cycles.</summary>
    public int ProcessingLatencyCycles { get; init; }
    /// <summary>Gets the energy category used for trace provenance and metrics.</summary>
    public EnergyCategory EnergyCategory { get; init; } = EnergyCategory.NoC;
    /// <summary>Gets the trace descriptors active for this runtime.</summary>
    public IReadOnlyList<ComponentTraceDescriptor> TraceDescriptors { get; init; } = [];
    /// <summary>Gets the metric descriptors active for this runtime.</summary>
    public IReadOnlyList<ComponentMetricDescriptor> MetricDescriptors { get; init; } = [];
    /// <summary>Gets deterministic runtime parameters to merge into the compiled component.</summary>
    public IReadOnlyDictionary<string, string> Parameters { get; init; } =
        new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
    /// <summary>Gets the stable runtime kernel id when this descriptor binds an execution contract.</summary>
    public string KernelId { get; init; } = "";
    /// <summary>Gets the exact runtime kernel version when this descriptor binds an execution contract.</summary>
    public string KernelVersion { get; init; } = "";
    /// <summary>Gets the exact kernel configuration schema id.</summary>
    public string ContractSchemaId { get; init; } = "";
    /// <summary>Gets recursively canonicalized kernel configuration JSON.</summary>
    public string CanonicalKernelConfiguration { get; init; } = "{}";
    /// <summary>Gets the exact deterministic runtime kernel implementation hash.</summary>
    public string KernelImplementationHash { get; init; } = "";
}

/// <summary>Provides plugin-specific validation for component instances.</summary>
public interface IComponentValidationProvider
{
    /// <summary>Validates a plugin component instance and returns structured diagnostics.</summary>
    IReadOnlyList<ComponentPluginIssue> Validate(ComponentValidationContext context);
}

/// <summary>Provides plugin-specific compile-time metadata for component instances.</summary>
public interface IComponentCompileProvider
{
    /// <summary>Compiles plugin component metadata without mutating the source graph.</summary>
    ComponentCompileProviderResult Compile(ComponentCompileContext context);
}

/// <summary>Builds deterministic runtime descriptors for plugin component instances.</summary>
public interface IComponentSimulationRuntimeFactory
{
    /// <summary>Creates a runtime descriptor for a compiled plugin component instance.</summary>
    ComponentSimulationRuntimeDescriptor CreateRuntime(ComponentRuntimeFactoryContext context);
}

/// <summary>Defines one component plugin contract registered with the PluginManager.</summary>
/// <param name="TypeId">Stable component type id, independent of ComponentKind enum values.</param>
/// <param name="DisplayName">Human-readable component name.</param>
/// <param name="Category">Palette and registry category.</param>
/// <param name="Version">Plugin contract version.</param>
/// <param name="Ports">Port schema exposed by this type.</param>
/// <param name="Parameters">Parameter schema exposed by this type.</param>
/// <param name="ValidationProvider">Provider for instance validation.</param>
/// <param name="CompileProvider">Provider for compile-time metadata.</param>
/// <param name="SimulationRuntimeFactory">Factory for deterministic runtime descriptors.</param>
/// <param name="TraceDescriptors">Trace descriptors emitted or enriched by the runtime.</param>
/// <param name="MetricDescriptors">Metric descriptors emitted or enriched by the runtime.</param>
/// <param name="PrimitiveDescriptor">Optional Phase 7C primitive block descriptor.</param>
/// <param name="CompiledProfileFactoryDescriptor">Optional Phase 7C compiled profile factory descriptor.</param>
/// <param name="UnityPresentationDescriptor">Optional Unity presentation metadata, with no Unity dependency.</param>
/// <param name="SourceKind">Plugin source trust boundary.</param>
/// <param name="LegacyKind">Optional built-in enum bridge used only by first-party legacy components.</param>
/// <param name="ShowInPalette">Whether default editor palettes should include this descriptor.</param>
/// <param name="RuntimeKernelFactory">Optional exact component runtime kernel factory registered through this plugin.</param>
/// <param name="KernelTestScenarioProvider">Optional deterministic test-scenario provider for the exact runtime kernel.</param>
/// <param name="LegacyAliases">Optional historical display aliases retained for import and UI migration.</param>
/// <param name="DeviceContract">Optional open Phase 9 device capability and primitive contract.</param>
public sealed record ComponentPluginDescriptor(
    string TypeId,
    string DisplayName,
    string Category,
    string Version,
    IReadOnlyList<ComponentPortSchema> Ports,
    IReadOnlyList<ComponentParameterSchema> Parameters,
    IComponentValidationProvider ValidationProvider,
    IComponentCompileProvider CompileProvider,
    IComponentSimulationRuntimeFactory SimulationRuntimeFactory,
    IReadOnlyList<ComponentTraceDescriptor> TraceDescriptors,
    IReadOnlyList<ComponentMetricDescriptor> MetricDescriptors,
    ComponentTemplatePrimitiveDescriptor? PrimitiveDescriptor = null,
    CompiledProfileFactoryDescriptor? CompiledProfileFactoryDescriptor = null,
    UnityPresentationDescriptor? UnityPresentationDescriptor = null,
    ComponentPluginSourceKind SourceKind = ComponentPluginSourceKind.FirstParty,
    ComponentKind? LegacyKind = null,
    bool ShowInPalette = true,
    IComponentRuntimeKernelFactory? RuntimeKernelFactory = null,
    IComponentKernelTestScenarioProvider? KernelTestScenarioProvider = null,
    IReadOnlyList<string>? LegacyAliases = null,
    Phase9PrimitiveContract? DeviceContract = null);

/// <summary>Controls PluginManager registration policy.</summary>
public sealed class ComponentPluginManagerOptions
{
    /// <summary>Gets or sets whether external plugins may be registered.</summary>
    public bool AllowExternalPlugins { get; init; }
}

/// <summary>Represents a registered plugin and its deterministic order.</summary>
/// <param name="Plugin">Registered plugin descriptor.</param>
/// <param name="RegistrationOrder">Monotonic order assigned by the manager.</param>
public sealed record ComponentPluginRegistration(ComponentPluginDescriptor Plugin, int RegistrationOrder);

/// <summary>Represents the result of a plugin register or unregister operation.</summary>
public sealed class ComponentPluginRegistrationResult
{
    /// <summary>Gets the action attempted by the manager.</summary>
    public string Action { get; init; } = "";
    /// <summary>Gets the plugin related to the operation when available.</summary>
    public ComponentPluginDescriptor? Plugin { get; init; }
    /// <summary>Gets structured diagnostics emitted by the operation.</summary>
    public IReadOnlyList<ComponentPluginIssue> Issues { get; init; } = [];
    /// <summary>Gets whether the operation completed without error-severity diagnostics.</summary>
    public bool IsSuccess => Issues.All(issue => issue.Severity != ValidationSeverity.Error);

    /// <summary>Builds a successful result for the supplied action.</summary>
    public static ComponentPluginRegistrationResult Succeeded(string action, ComponentPluginDescriptor? plugin = null) => new()
    {
        Action = action,
        Plugin = plugin
    };

    /// <summary>Builds a failed result for the supplied action and diagnostics.</summary>
    public static ComponentPluginRegistrationResult Failed(string action, ComponentPluginDescriptor? plugin, IReadOnlyList<ComponentPluginIssue> issues) => new()
    {
        Action = action,
        Plugin = plugin,
        Issues = issues
    };
}

/// <summary>Registers component plugins without scanning or executing external assemblies.</summary>
public sealed class ComponentPluginManager
{
    private readonly ComponentPluginManagerOptions _options;
    private readonly Dictionary<string, ComponentPluginRegistration> _registrations = new(StringComparer.OrdinalIgnoreCase);
    private int _nextRegistrationOrder;

    /// <summary>Initializes a new plugin manager with optional registration policy.</summary>
    public ComponentPluginManager(ComponentPluginManagerOptions? options = null)
    {
        _options = options ?? new ComponentPluginManagerOptions();
    }

    /// <summary>Registers a plugin descriptor if policy and schema validation pass.</summary>
    public ComponentPluginRegistrationResult Register(ComponentPluginDescriptor plugin)
    {
        if (plugin is null)
        {
            throw new ArgumentNullException(nameof(plugin));
        }

        var issues = ValidateDescriptor(plugin).ToList();
        var normalizedTypeId = NormalizeTypeId(plugin.TypeId);
        if (plugin.SourceKind == ComponentPluginSourceKind.External && !_options.AllowExternalPlugins)
        {
            issues.Add(new ComponentPluginIssue(
                "ExternalPluginDisabled",
                ValidationSeverity.Error,
                "$.sourceKind",
                "External component plugins are disabled by default and must be explicitly allowed by policy.",
                plugin.TypeId));
        }

        if (!string.IsNullOrWhiteSpace(normalizedTypeId) && _registrations.TryGetValue(normalizedTypeId, out var existing))
        {
            var sameVersion = string.Equals(existing.Plugin.Version, plugin.Version, StringComparison.OrdinalIgnoreCase);
            issues.Add(new ComponentPluginIssue(
                sameVersion ? "PluginDuplicateTypeId" : "PluginVersionConflict",
                ValidationSeverity.Error,
                "$.typeId",
                sameVersion
                    ? $"Plugin type id '{plugin.TypeId}' version '{plugin.Version}' is already registered."
                    : $"Plugin type id '{plugin.TypeId}' is already registered with version '{existing.Plugin.Version}', not '{plugin.Version}'.",
                plugin.TypeId));
        }

        if (issues.Any(issue => issue.Severity == ValidationSeverity.Error))
        {
            return ComponentPluginRegistrationResult.Failed("register", plugin, issues);
        }

        _registrations[normalizedTypeId] = new ComponentPluginRegistration(plugin, _nextRegistrationOrder++);
        return ComponentPluginRegistrationResult.Succeeded("register", plugin);
    }

    /// <summary>Unregisters an existing plugin by stable type id.</summary>
    public ComponentPluginRegistrationResult Unregister(string typeId)
    {
        var normalizedTypeId = NormalizeTypeId(typeId);
        if (string.IsNullOrWhiteSpace(normalizedTypeId) || !_registrations.TryGetValue(normalizedTypeId, out var existing))
        {
            return ComponentPluginRegistrationResult.Failed(
                "unregister",
                null,
                [new ComponentPluginIssue(
                    "PluginNotRegistered",
                    ValidationSeverity.Error,
                    "$.typeId",
                    $"Plugin type id '{typeId}' is not registered.",
                    typeId)]);
        }

        _registrations.Remove(normalizedTypeId);
        return ComponentPluginRegistrationResult.Succeeded("unregister", existing.Plugin);
    }

    /// <summary>Finds a plugin by stable type id.</summary>
    public ComponentPluginDescriptor? GetPlugin(string typeId)
    {
        var normalizedTypeId = NormalizeTypeId(typeId);
        return _registrations.TryGetValue(normalizedTypeId, out var registration) ? registration.Plugin : null;
    }

    /// <summary>Returns registered plugins in deterministic registration order.</summary>
    public IReadOnlyList<ComponentPluginDescriptor> GetPlugins() => _registrations.Values
        .OrderBy(registration => registration.RegistrationOrder)
        .ThenBy(registration => registration.Plugin.TypeId, StringComparer.OrdinalIgnoreCase)
        .Select(registration => registration.Plugin)
        .ToList();

    /// <summary>Returns registered plugins in a category using deterministic registration order.</summary>
    public IReadOnlyList<ComponentPluginDescriptor> GetByCategory(string category) => _registrations.Values
        .Where(registration => string.Equals(registration.Plugin.Category, category, StringComparison.OrdinalIgnoreCase))
        .OrderBy(registration => registration.RegistrationOrder)
        .ThenBy(registration => registration.Plugin.TypeId, StringComparer.OrdinalIgnoreCase)
        .Select(registration => registration.Plugin)
        .ToList();

    private static IReadOnlyList<ComponentPluginIssue> ValidateDescriptor(ComponentPluginDescriptor plugin)
    {
        var issues = new List<ComponentPluginIssue>();
        RequireText(issues, plugin.TypeId, "PluginTypeIdRequired", "$.typeId", "Plugin type id is required.", plugin.TypeId);
        RequireText(issues, plugin.DisplayName, "PluginDisplayNameRequired", "$.displayName", "Plugin display name is required.", plugin.TypeId);
        RequireText(issues, plugin.Category, "PluginCategoryRequired", "$.category", "Plugin category is required.", plugin.TypeId);
        RequireText(issues, plugin.Version, "PluginVersionRequired", "$.version", "Plugin version is required.", plugin.TypeId);

        if (plugin.ValidationProvider is null)
        {
            issues.Add(new ComponentPluginIssue("PluginValidationProviderRequired", ValidationSeverity.Error, "$.validationProvider", "Validation provider is required.", plugin.TypeId));
        }

        if (plugin.CompileProvider is null)
        {
            issues.Add(new ComponentPluginIssue("PluginCompileProviderRequired", ValidationSeverity.Error, "$.compileProvider", "Compile provider is required.", plugin.TypeId));
        }

        if (plugin.SimulationRuntimeFactory is null)
        {
            issues.Add(new ComponentPluginIssue("PluginRuntimeFactoryRequired", ValidationSeverity.Error, "$.simulationRuntimeFactory", "Simulation runtime factory is required.", plugin.TypeId));
        }

        ValidatePorts(plugin, issues);
        ValidateParameters(plugin, issues);
        ValidateDescriptors(plugin, issues);
        return issues;
    }

    private static void ValidatePorts(ComponentPluginDescriptor plugin, List<ComponentPluginIssue> issues)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var port in plugin.Ports ?? [])
        {
            if (string.IsNullOrWhiteSpace(port.Name))
            {
                issues.Add(new ComponentPluginIssue("PluginPortNameRequired", ValidationSeverity.Error, "$.ports", "Every plugin port requires a stable name.", plugin.TypeId));
                continue;
            }

            if (!seen.Add(port.Name.Trim()))
            {
                issues.Add(new ComponentPluginIssue("PluginDuplicatePortName", ValidationSeverity.Error, "$.ports", $"Plugin port '{port.Name}' is duplicated.", plugin.TypeId));
            }

            if (port.BandwidthBitsPerCycle <= 0)
            {
                issues.Add(new ComponentPluginIssue("PluginPortBandwidthInvalid", ValidationSeverity.Error, "$.ports", $"Plugin port '{port.Name}' bandwidth must be positive.", plugin.TypeId));
            }

            if (port.LatencyCycles < 0)
            {
                issues.Add(new ComponentPluginIssue("PluginPortLatencyInvalid", ValidationSeverity.Error, "$.ports", $"Plugin port '{port.Name}' latency must be non-negative.", plugin.TypeId));
            }
        }
    }

    private static void ValidateParameters(ComponentPluginDescriptor plugin, List<ComponentPluginIssue> issues)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var parameter in plugin.Parameters ?? [])
        {
            if (string.IsNullOrWhiteSpace(parameter.Name))
            {
                issues.Add(new ComponentPluginIssue("PluginParameterNameRequired", ValidationSeverity.Error, "$.parameters", "Every plugin parameter requires a stable name.", plugin.TypeId));
                continue;
            }

            if (!seen.Add(parameter.Name.Trim()))
            {
                issues.Add(new ComponentPluginIssue("PluginDuplicateParameterName", ValidationSeverity.Error, "$.parameters", $"Plugin parameter '{parameter.Name}' is duplicated.", plugin.TypeId));
            }

            if (parameter.Minimum.HasValue && parameter.Maximum.HasValue && parameter.Minimum.Value > parameter.Maximum.Value)
            {
                issues.Add(new ComponentPluginIssue("PluginParameterRangeInvalid", ValidationSeverity.Error, "$.parameters", $"Plugin parameter '{parameter.Name}' has minimum greater than maximum.", plugin.TypeId));
            }
        }
    }

    private static void ValidateDescriptors(ComponentPluginDescriptor plugin, List<ComponentPluginIssue> issues)
    {
        if (plugin.PrimitiveDescriptor is not null)
        {
            RequireText(issues, plugin.PrimitiveDescriptor.PrimitiveId, "PluginPrimitiveIdRequired", "$.primitiveDescriptor.primitiveId", "Primitive descriptor id is required.", plugin.TypeId);
            RequireText(issues, plugin.PrimitiveDescriptor.DisplayName, "PluginPrimitiveNameRequired", "$.primitiveDescriptor.displayName", "Primitive descriptor display name is required.", plugin.TypeId);
        }

        if (plugin.DeviceContract is not null)
        {
            var deviceValidation = Phase9PrimitiveContractValidator.ValidateContract(plugin.DeviceContract);
            issues.AddRange(deviceValidation.Issues.Select(issue => new ComponentPluginIssue(issue.Code, issue.Severity, "$.deviceContract" + issue.Path.TrimStart('$'), issue.Message, plugin.TypeId)));
            if (!plugin.Ports.SequenceEqual(plugin.DeviceContract.Ports))
            {
                issues.Add(new ComponentPluginIssue("PrimitivePluginPortSchemaMismatch", ValidationSeverity.Error, "$.deviceContract.ports", "Device contract ports must exactly match plugin ports.", plugin.TypeId));
            }
            if (!plugin.Parameters.SequenceEqual(plugin.DeviceContract.Parameters))
            {
                issues.Add(new ComponentPluginIssue("PrimitivePluginParameterSchemaMismatch", ValidationSeverity.Error, "$.deviceContract.parameters", "Device contract parameters must exactly match plugin parameters.", plugin.TypeId));
            }
        }
        if (plugin.CompiledProfileFactoryDescriptor is not null)
        {
            RequireText(issues, plugin.CompiledProfileFactoryDescriptor.FactoryId, "PluginProfileFactoryIdRequired", "$.compiledProfileFactoryDescriptor.factoryId", "Compiled profile factory id is required.", plugin.TypeId);
            RequireText(issues, plugin.CompiledProfileFactoryDescriptor.Version, "PluginProfileFactoryVersionRequired", "$.compiledProfileFactoryDescriptor.version", "Compiled profile factory version is required.", plugin.TypeId);
        }
    }

    private static void RequireText(List<ComponentPluginIssue> issues, string value, string code, string location, string message, string? relatedId)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            issues.Add(new ComponentPluginIssue(code, ValidationSeverity.Error, location, message, relatedId));
        }
    }

    private static string NormalizeTypeId(string? typeId) => string.IsNullOrWhiteSpace(typeId) ? "" : typeId.Trim();
}

/// <summary>Creates components from registered plugin descriptors without central enum expansion.</summary>
public sealed class ComponentTypeRegistry
{
    private readonly ComponentPluginManager _plugins;

    /// <summary>Initializes a new component type registry over the supplied plugin manager.</summary>
    public ComponentTypeRegistry(ComponentPluginManager? pluginManager = null)
    {
        _plugins = pluginManager ?? new ComponentPluginManager();
    }

    /// <summary>Gets the underlying plugin manager.</summary>
    public ComponentPluginManager PluginManager => _plugins;

    /// <summary>Creates the default registry and explicitly loads first-party plugin packages.</summary>
    public static ComponentTypeRegistry CreateDefault(bool includeSamplePlugin = false)
    {
        var registry = new ComponentTypeRegistry();
        FirstPartyComponentPlugins.Load(registry);
        if (includeSamplePlugin)
        {
            registry.Register(Phase7BSampleComponentPlugin.Create());
        }

        return registry;
    }

    /// <summary>Registers one component plugin descriptor.</summary>
    public ComponentPluginRegistrationResult Register(ComponentPluginDescriptor plugin) => _plugins.Register(plugin);

    /// <summary>Unregisters one component plugin descriptor by type id.</summary>
    public ComponentPluginRegistrationResult Unregister(string typeId) => _plugins.Unregister(typeId);

    /// <summary>Gets one registered component plugin by type id.</summary>
    public ComponentPluginDescriptor? GetPlugin(string typeId) => _plugins.GetPlugin(typeId);

    /// <summary>Returns registered plugin descriptors in deterministic registration order.</summary>
    public IReadOnlyList<ComponentPluginDescriptor> GetPlugins() => _plugins.GetPlugins();

    /// <summary>Freezes runtime kernel factories from the underlying PluginManager into a deterministic snapshot.</summary>
    public ComponentRuntimeKernelRegistryBuildResult FreezeRuntimeKernels() => ComponentRuntimeKernelRegistrySnapshot.Build(_plugins);

    /// <summary>Returns palette items for descriptors marked visible.</summary>
    public IReadOnlyList<PaletteItem> PaletteItems() => _plugins.GetPlugins()
        .Where(plugin => plugin.ShowInPalette)
        .OrderBy(plugin => plugin.UnityPresentationDescriptor?.SortOrder ?? int.MaxValue)
        .ThenBy(plugin => plugin.DisplayName, StringComparer.OrdinalIgnoreCase)
        .Select(plugin => new PaletteItem(plugin.LegacyKind ?? ComponentKind.Custom, plugin.DisplayName, plugin.UnityPresentationDescriptor?.Summary ?? plugin.Category)
        {
            TypeId = plugin.TypeId,
            Category = plugin.Category,
            Glyph = plugin.UnityPresentationDescriptor?.Glyph ?? "component",
            ColorHex = plugin.UnityPresentationDescriptor?.ColorHex ?? "#8A8F98",
            Abbreviation = plugin.UnityPresentationDescriptor?.Abbreviation ?? Abbreviate(plugin.DisplayName),
            SortOrder = plugin.UnityPresentationDescriptor?.SortOrder ?? int.MaxValue
        })
        .ToList();

    /// <summary>Creates a hardware component from a registered descriptor and default schema.</summary>
    public HardwareComponent CreateComponent(string typeId, string id, GridPosition position, string? name = null)
    {
        var normalizedTypeId = ComponentTypeIds.Normalize(typeId);
        var plugin = GetPlugin(normalizedTypeId) ?? throw new InvalidOperationException($"Component plugin type id '{typeId}' is not registered.");
        var isLegacyBridge = plugin.LegacyKind.HasValue && !ComponentTypeIds.IsFirstPartyExtensionTypeId(plugin.TypeId);
        var component = new HardwareComponent
        {
            Id = id,
            Name = string.IsNullOrWhiteSpace(name) ? plugin.DisplayName : name.Trim(),
            Type = plugin.LegacyKind ?? ComponentKind.Custom,
            TypeId = isLegacyBridge ? "" : plugin.TypeId,
            Position = position,
            Ports = plugin.Ports.Select(ToHardwarePort).ToList(),
            Parameters = plugin.Parameters.ToDictionary(parameter => parameter.Name, parameter => parameter.DefaultValue, StringComparer.OrdinalIgnoreCase)
        };

        if (!isLegacyBridge)
        {
            component.VisualStyle[ComponentPluginRuntimeKeys.TypeId] = plugin.TypeId;
            if (plugin.UnityPresentationDescriptor is not null)
            {
                component.VisualStyle["plugin_glyph"] = plugin.UnityPresentationDescriptor.Glyph;
                component.VisualStyle["plugin_color"] = plugin.UnityPresentationDescriptor.ColorHex;
            }
        }

        return component;
    }

    /// <summary>Creates a hardware component from a legacy built-in kind through the same registry path.</summary>
    public HardwareComponent CreateBuiltIn(ComponentKind kind, string id, GridPosition position, string? name = null) =>
        CreateComponent(ComponentTypeIds.BuiltIn(kind), id, position, name);

    private static HardwarePort ToHardwarePort(ComponentPortSchema port)
    {
        var result = new HardwarePort
        {
            Name = port.Name,
            Direction = port.Direction,
            SignalType = port.SignalType,
            DataType = port.DataType,
            Precision = port.Precision,
            Protocol = port.Protocol,
            BandwidthBitsPerCycle = port.BandwidthBitsPerCycle,
            LatencyCycles = port.LatencyCycles,
            Required = port.Required,
            MultiConnect = port.MultiConnect
        };

        if (!string.IsNullOrWhiteSpace(port.Quantity))
            result.ExtensionData["quantity"] = System.Text.Json.JsonSerializer.SerializeToElement(port.Quantity);
        if (!string.IsNullOrWhiteSpace(port.Units))
            result.ExtensionData["units"] = System.Text.Json.JsonSerializer.SerializeToElement(port.Units);
        return result;
    }

    private static string Abbreviate(string displayName)
    {
        var letters = new string((displayName ?? "")
            .Where(char.IsLetterOrDigit)
            .Take(3)
            .Select(char.ToUpperInvariant)
            .ToArray());
        return string.IsNullOrWhiteSpace(letters) ? "CMP" : letters;
    }
}

/// <summary>Loads all simulator-shipped first-party component plugin descriptors explicitly.</summary>
public static class FirstPartyComponentPlugins
{
    /// <summary>Loads core, Phase 8A collective, Optical, SerDes, and CIM first-party descriptors into the supplied registry.</summary>
    public static void Load(ComponentTypeRegistry registry)
    {
        if (registry is null)
        {
            throw new ArgumentNullException(nameof(registry));
        }

        foreach (var plugin in CoreBuiltIns()) RegisterOrThrow(registry, plugin);
        Phase8ACollectiveComponentPlugins.Load(registry);
        Phase8AElementwiseComponentPlugins.Load(registry);
        FirstPartyOpticalComponentPlugins.Load(registry);
        FirstPartySerDesComponentPlugins.Load(registry);
        FirstPartyCimComponentPlugins.Load(registry);
    }

    private static IEnumerable<ComponentPluginDescriptor> CoreBuiltIns()
    {
        yield return BuiltIn(ComponentKind.WorkloadSource, "Workload Source", "Core", "source", "#24A148", "SRC", "Injects packets", 10, showInPalette: true);
        yield return BuiltIn(ComponentKind.Memory, "Memory / Global Buffer", "Core", "memory", "#8A3FFC", "MEM", "Large storage", 20, showInPalette: true);
        yield return BuiltIn(ComponentKind.Router, "Router", "Core", "router", "#198038", "RTR", "Packet routing", 30, showInPalette: true);
        yield return BuiltIn(ComponentKind.Buffer, "Buffer", "Core", "buffer", "#B28600", "BUF", "Data buffering", 40, showInPalette: true);
        yield return BuiltIn(ComponentKind.ProcessingElement, "PE", "Core", "processing-element", "#0F62FE", "PE", "MAC operations", 50, showInPalette: true) with
        {
            RuntimeKernelFactory = CoreDigitalVmmKernelFactory.Instance,
            KernelTestScenarioProvider = CoreDigitalVmmScenarioProvider.Instance
        };
        yield return BuiltIn(ComponentKind.ReductionUnit, "Reduction Unit", "Core", "reduction", "#1192E8", "RED", "Partial sum reduction", 60, showInPalette: true);
        yield return BuiltIn(ComponentKind.SoftmaxUnit, "Softmax Unit", "Core", "softmax", "#D02670", "SMX", "Softmax computation", 70, showInPalette: true);
        yield return BuiltIn(ComponentKind.LinkEndpoint, "Link", "Core", "link-endpoint", "#697077", "LNK", "Explicit link endpoint", 80, showInPalette: true);
        yield return BuiltIn(ComponentKind.WorkloadSink, "Workload Sink", "Core", "sink", "#DA1E28", "SNK", "Absorbs packets", 90, showInPalette: true);
        yield return BuiltIn(ComponentKind.Adapter, "Protocol Adapter", "Adapter", "adapter", "#6F6F6F", "ADP", "Protocol adaptation", 200, showInPalette: false, parameters: AdapterRuntimeMetadata.AdapterParameterSchemas(1, 0.005, contributesOpticalEnergy: false));
        yield return BuiltIn(ComponentKind.PrecisionConverter, "Precision Converter", "Adapter", "precision-converter", "#6F6F6F", "PCV", "Precision conversion", 201, showInPalette: false, parameters: AdapterRuntimeMetadata.AdapterParameterSchemas(1, 0.02, contributesOpticalEnergy: false, includeConversionAliases: true));
        yield return BuiltIn(ComponentKind.Quantizer, "Quantizer", "Adapter", "quantizer", "#6F6F6F", "QNT", "Quantization", 202, showInPalette: false, parameters: AdapterRuntimeMetadata.AdapterParameterSchemas(1, 0.02, contributesOpticalEnergy: false, includeConversionAliases: true));
        yield return BuiltIn(ComponentKind.Dequantizer, "Dequantizer", "Adapter", "dequantizer", "#6F6F6F", "DQT", "Dequantization", 203, showInPalette: false, parameters: AdapterRuntimeMetadata.AdapterParameterSchemas(1, 0.02, contributesOpticalEnergy: false, includeConversionAliases: true));
        yield return BuiltIn(ComponentKind.Macro, "Macro", "Template", "macro", "#8D8D8D", "MAC", "Reusable macro", 900, showInPalette: false);
    }

    internal static ComponentPluginDescriptor BuiltIn(
        ComponentKind kind,
        string displayName,
        string category,
        string glyph,
        string colorHex,
        string abbreviation,
        string summary,
        int sortOrder,
        bool showInPalette,
        IReadOnlyList<ComponentParameterSchema>? parameters = null,
        EnergyCategory energyCategory = EnergyCategory.NoC,
        bool exposeTemplateDescriptors = false)
    {
        var typeId = ComponentTypeIds.BuiltIn(kind);
        var effectiveParameters = parameters ?? ParametersFor(kind);
        return new ComponentPluginDescriptor(
            typeId,
            displayName,
            category,
            "1.0.0",
            PortsFor(kind),
            effectiveParameters,
            NoopValidationProvider.Instance,
            NoopCompileProvider.Instance,
            DefaultRuntimeFactory.Instance,
            [new ComponentTraceDescriptor($"{typeId}.compute", TraceEventType.Compute, $"{displayName} compute/runtime event")],
            [new ComponentMetricDescriptor($"{typeId}.energy", "pJ", energyCategory, $"{displayName} energy")],
            PrimitiveDescriptor: exposeTemplateDescriptors
                ? new ComponentTemplatePrimitiveDescriptor($"{typeId}.primitive", $"{displayName} Primitive", category, PortsFor(kind), effectiveParameters)
                : null,
            CompiledProfileFactoryDescriptor: exposeTemplateDescriptors
                ? new CompiledProfileFactoryDescriptor($"{typeId}.profile-factory", "compiled-profile", "1.0.0", $"Produces compiled profiles for {displayName}.")
                : null,
            UnityPresentationDescriptor: new UnityPresentationDescriptor(glyph, colorHex, abbreviation, summary, sortOrder),
            SourceKind: ComponentPluginSourceKind.FirstParty,
            LegacyKind: kind,
            ShowInPalette: showInPalette);
    }

    internal static IReadOnlyList<ComponentParameterSchema> ParametersFor(ComponentKind kind) => ComponentDefaults.For(kind)
        .Select(pair => new ComponentParameterSchema(pair.Key, pair.Value))
        .ToList();

    internal static IReadOnlyList<ComponentPortSchema> PortsFor(ComponentKind kind) => PortSchemas.TryGetValue(kind, out var ports)
        ? ports
        : DefaultInOutPorts;

    internal static void RegisterOrThrow(ComponentTypeRegistry registry, ComponentPluginDescriptor plugin)
    {
        var result = registry.Register(plugin);
        if (!result.IsSuccess)
        {
            throw new InvalidOperationException(string.Join(Environment.NewLine, result.Issues.Select(issue => $"{issue.Code}: {issue.Message}")));
        }
    }

    internal static ComponentPortSchema In(string name = "in", bool multiConnect = false, SignalType signal = SignalType.Digital, HardwareDataType dataType = HardwareDataType.Packet, PrecisionKind precision = PrecisionKind.Any, PortProtocol protocol = PortProtocol.Packet) =>
        new(name, PortDirection.Input, signal, dataType, precision, protocol, Required: true, MultiConnect: multiConnect);

    internal static ComponentPortSchema Out(string name = "out", bool multiConnect = false, SignalType signal = SignalType.Digital, HardwareDataType dataType = HardwareDataType.Packet, PrecisionKind precision = PrecisionKind.Any, PortProtocol protocol = PortProtocol.Packet) =>
        new(name, PortDirection.Output, signal, dataType, precision, protocol, Required: true, MultiConnect: multiConnect);

    private static readonly IReadOnlyList<ComponentPortSchema> DefaultInOutPorts = [In(), Out()];

    private static readonly IReadOnlyDictionary<ComponentKind, IReadOnlyList<ComponentPortSchema>> PortSchemas = new Dictionary<ComponentKind, IReadOnlyList<ComponentPortSchema>>
    {
        [ComponentKind.WorkloadSource] =
        [
            Out(),
            new ComponentPortSchema(
                "control_out",
                PortDirection.Output,
                SignalType.Control,
                HardwareDataType.Config,
                PrecisionKind.Any,
                PortProtocol.RequestResponse,
                Required: false,
                MultiConnect: true,
                Quantity: "control_command",
                Units: "configuration")
        ],
        [ComponentKind.WorkloadSink] = [In()],
        [ComponentKind.Router] = [In(multiConnect: true), Out(multiConnect: true)],
        [ComponentKind.Buffer] = [In(), Out()],
        [ComponentKind.Memory] = [In(multiConnect: true), Out(multiConnect: true)],
        [ComponentKind.ProcessingElement] = [In(), Out()],
        [ComponentKind.ReductionUnit] = [In(multiConnect: true), Out()],
        [ComponentKind.SoftmaxUnit] = [In(), Out()],
        [ComponentKind.LinkEndpoint] = [In(), Out()],
        [ComponentKind.Adapter] = [In(), Out()],
        [ComponentKind.PrecisionConverter] = [In(), Out()],
        [ComponentKind.Quantizer] = [In(), Out()],
        [ComponentKind.Dequantizer] = [In(), Out()],
        [ComponentKind.Macro] = [In(multiConnect: true), Out(multiConnect: true)]
    };
}

/// <summary>First-party Optical component plugin package registration path.</summary>
public static class FirstPartyOpticalComponentPlugins
{
    /// <summary>Gets the assembly location that owns the first-party Optical plugin package.</summary>
    public static string AssemblyName => typeof(FirstPartyOpticalComponentPlugins).Assembly.GetName().Name ?? "HardwareSim.Core";

    /// <summary>Loads first-party Optical descriptors into the supplied registry.</summary>
    public static void Load(ComponentTypeRegistry registry)
    {
        foreach (var plugin in Descriptors()) FirstPartyComponentPlugins.RegisterOrThrow(registry, plugin);
    }

    /// <summary>Returns first-party Optical descriptors without mutating a registry.</summary>
    public static IReadOnlyList<ComponentPluginDescriptor> Descriptors() =>
        Phase8OpticalPluginContracts.Descriptors();
}

/// <summary>First-party CIM component plugin package registration path.</summary>
public static class FirstPartyCimComponentPlugins
{
    /// <summary>Gets the assembly location that owns the first-party CIM plugin package.</summary>
    public static string AssemblyName => typeof(FirstPartyCimComponentPlugins).Assembly.GetName().Name ?? "HardwareSim.Core";

    /// <summary>Loads first-party CIM descriptors into the supplied registry.</summary>
    public static void Load(ComponentTypeRegistry registry)
    {
        foreach (var plugin in Descriptors()) FirstPartyComponentPlugins.RegisterOrThrow(registry, plugin);
    }

    /// <summary>Returns first-party CIM descriptors without mutating a registry.</summary>
    public static IReadOnlyList<ComponentPluginDescriptor> Descriptors() =>
        Phase9CimPrimitivePlugins.Descriptors();

    /// <summary>Gets whether the supplied stable type id is one of the first-party CIM crossbar primitives.</summary>
    public static bool IsCrossbarTypeId(string typeId)
    {
        var normalized = ComponentTypeIds.Normalize(typeId);
        return string.Equals(normalized, ComponentTypeIds.BuiltIn(ComponentKind.ReRamCrossbar), StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalized, ComponentTypeIds.BuiltIn(ComponentKind.FeFetCrossbar), StringComparison.OrdinalIgnoreCase);
    }

    private static ComponentPluginDescriptor Cim(ComponentKind kind, string displayName, string glyph, string abbreviation, int sortOrder, EnergyCategory category = EnergyCategory.Cim)
    {
        var adapterParameters = category == EnergyCategory.Conversion
            ? AdapterRuntimeMetadata.AdapterParameterSchemas(2, 0.03, contributesOpticalEnergy: false)
            : null;
        return FirstPartyComponentPlugins.BuiltIn(
            kind,
            displayName,
            "CIM",
            glyph,
            "#8A3800",
            abbreviation,
            "First-party CIM primitive",
            sortOrder,
            showInPalette: false,
            parameters: adapterParameters,
            energyCategory: category,
            exposeTemplateDescriptors: true);
    }
}

/// <summary>Provides a first-party sample plugin used to validate the end-to-end plugin path.</summary>
public static class Phase7BSampleComponentPlugin
{
    /// <summary>Stable type id for the Phase 7B sample plugin component.</summary>
    public const string TypeId = "com.hardware-sim.samples.phase7b.packet-delay";

    /// <summary>Creates the sample plugin descriptor without touching ComponentKind.</summary>
    public static ComponentPluginDescriptor Create() => new(
        TypeId,
        "Phase 7B Packet Delay",
        "Samples",
        "1.0.0",
        [FirstPartyComponentPlugins.In(), FirstPartyComponentPlugins.Out()],
        [
            new ComponentParameterSchema("processing_latency_cycles", "2", "cycles", 0, 64, true, "Sample plugin processing latency."),
            new ComponentParameterSchema(ComponentPluginRuntimeKeys.EnergyPicojoulesPerPacket, "0.25", "pJ", 0, 1000, false, "Sample plugin packet energy."),
            new ComponentParameterSchema(ComponentPluginRuntimeKeys.EnergyPicojoulesPerBit, "0.001", "pJ/bit", 0, 1, false, "Sample plugin bit energy.")
        ],
        NoopValidationProvider.Instance,
        SampleCompileProvider.Instance,
        SampleRuntimeFactory.Instance,
        [new ComponentTraceDescriptor("phase7b.sample.compute", TraceEventType.Compute, "Sample plugin compute event")],
        [new ComponentMetricDescriptor("phase7b.sample.energy", "pJ", EnergyCategory.NoC, "Sample plugin energy")],
        PrimitiveDescriptor: new ComponentTemplatePrimitiveDescriptor(
            "phase7b.sample.packet-delay.primitive",
            "Packet Delay Primitive",
            "Samples",
            [FirstPartyComponentPlugins.In(), FirstPartyComponentPlugins.Out()],
            [new ComponentParameterSchema("processing_latency_cycles", "2", "cycles", 0, 64)]),
        CompiledProfileFactoryDescriptor: new CompiledProfileFactoryDescriptor(
            "phase7b.sample.packet-delay.profile-factory",
            "compiled-profile",
            "1.0.0",
            "Produces sample compiled packet-delay profiles."),
        UnityPresentationDescriptor: new UnityPresentationDescriptor("sample-delay", "#005D5D", "DLY", "Sample plugin", 120),
        SourceKind: ComponentPluginSourceKind.FirstParty,
        LegacyKind: null,
        ShowInPalette: true,
        RuntimeKernelFactory: Phase7BSamplePacketDelayKernelFactory.Instance,
        KernelTestScenarioProvider: Phase7BSamplePacketDelayScenarioProvider.Instance);

    private sealed class SampleCompileProvider : IComponentCompileProvider
    {
        public static readonly SampleCompileProvider Instance = new();

        public ComponentCompileProviderResult Compile(ComponentCompileContext context) => new()
        {
            Parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["phase7b_sample_compile"] = "true",
                [ComponentPluginRuntimeKeys.TypeId] = context.Plugin.TypeId
            }
        };
    }

    private sealed class SampleRuntimeFactory : IComponentSimulationRuntimeFactory
    {
        public static readonly SampleRuntimeFactory Instance = new();

        public ComponentSimulationRuntimeDescriptor CreateRuntime(ComponentRuntimeFactoryContext context) => new()
        {
            ProcessingLatencyCycles = Math.Max(0, context.Component.GetIntParameter("processing_latency_cycles", 2)),
            EnergyCategory = EnergyCategory.NoC,
            TraceDescriptors = context.Plugin.TraceDescriptors,
            MetricDescriptors = context.Plugin.MetricDescriptors,
            Parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["phase7b_sample_runtime"] = "true"
            }
        };
    }
}

internal sealed class NoopValidationProvider : IComponentValidationProvider
{
    public static readonly NoopValidationProvider Instance = new();

    public IReadOnlyList<ComponentPluginIssue> Validate(ComponentValidationContext context) => [];
}

internal sealed class NoopCompileProvider : IComponentCompileProvider
{
    public static readonly NoopCompileProvider Instance = new();

    public ComponentCompileProviderResult Compile(ComponentCompileContext context) => ComponentCompileProviderResult.Empty();
}

internal sealed class DefaultRuntimeFactory : IComponentSimulationRuntimeFactory
{
    public static readonly DefaultRuntimeFactory Instance = new();

    public ComponentSimulationRuntimeDescriptor CreateRuntime(ComponentRuntimeFactoryContext context)
    {
        var parameters = context.Plugin.Parameters.ToDictionary(
            parameter => parameter.Name,
            parameter => context.Component.Parameters.GetValueOrDefault(parameter.Name, parameter.DefaultValue),
            StringComparer.OrdinalIgnoreCase);
        var latency = ReadInt(parameters, ComponentPluginRuntimeKeys.ProcessingLatencyCycles,
            ReadInt(parameters, AdapterRuntimeMetadata.LatencyCyclesKey, 0));
        return new ComponentSimulationRuntimeDescriptor
        {
            ProcessingLatencyCycles = latency,
            EnergyCategory = context.Plugin.MetricDescriptors.FirstOrDefault()?.Category ?? EnergyCategory.NoC,
            TraceDescriptors = context.Plugin.TraceDescriptors,
            MetricDescriptors = context.Plugin.MetricDescriptors,
            Parameters = parameters
        };
    }

    private static int ReadInt(IReadOnlyDictionary<string, string> parameters, string key, int fallback) =>
        parameters.TryGetValue(key, out var raw) && int.TryParse(raw, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var parsed)
            ? Math.Max(0, parsed)
            : fallback;
}

