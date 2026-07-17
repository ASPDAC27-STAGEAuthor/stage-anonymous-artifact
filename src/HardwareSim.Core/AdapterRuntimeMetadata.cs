namespace HardwareSim.Core;

/// <summary>Provides adapter runtime metadata operations for hardware design and simulation workflows.</summary>
public static class AdapterRuntimeMetadata
{
    /// <summary>Parameter key storing adapter latency in cycles.</summary>
    public const string LatencyCyclesKey = "adapter_latency_cycles";
    /// <summary>Parameter key storing adapter energy per bit in picojoules.</summary>
    public const string EnergyPicojoulesPerBitKey = "adapter_energy_pj_per_bit";
    /// <summary>Parameter key indicating that adapter energy should also contribute to optical energy totals.</summary>
    public const string OpticalEnergyContributionKey = "adapter_contributes_optical_energy";
    /// <summary>Parameter key indicating that adapter energy should contribute to conversion energy totals.</summary>
    public const string ConversionEnergyContributionKey = "adapter_contributes_conversion_energy";
    /// <summary>Parameter key marking a descriptor as adapter-runtime-capable.</summary>
    public const string RuntimeEnabledKey = "adapter_runtime_enabled";

    private static readonly Lazy<IReadOnlyDictionary<string, ComponentPluginDescriptor>> DefaultDescriptors = new(() =>
        ComponentTypeRegistry.CreateDefault()
            .GetPlugins()
            .ToDictionary(plugin => plugin.TypeId, plugin => plugin, StringComparer.OrdinalIgnoreCase));

    /// <summary>Creates parameter schema entries used by first-party adapter-like plugin descriptors.</summary>
    public static IReadOnlyList<ComponentParameterSchema> AdapterParameterSchemas(
        int latencyCycles,
        double energyPicojoulesPerBit,
        bool contributesOpticalEnergy,
        bool includeConversionAliases = false)
    {
        var schemas = new List<ComponentParameterSchema>
        {
            new(RuntimeEnabledKey, "true", "bool", Description: "Marks this descriptor as adapter-runtime-capable."),
            new(LatencyCyclesKey, latencyCycles.ToString(System.Globalization.CultureInfo.InvariantCulture), "cycles", 0, 1024, false, "Adapter processing latency."),
            new(EnergyPicojoulesPerBitKey, energyPicojoulesPerBit.ToString(System.Globalization.CultureInfo.InvariantCulture), "pJ/bit", 0, 1000, false, "Adapter energy per transferred bit."),
            new(ComponentPluginRuntimeKeys.ProcessingLatencyCycles, latencyCycles.ToString(System.Globalization.CultureInfo.InvariantCulture), "cycles", 0, 1024, false, "Plugin runtime latency exported for the cycle simulator."),
            new(ComponentPluginRuntimeKeys.EnergyPicojoulesPerBit, energyPicojoulesPerBit.ToString(System.Globalization.CultureInfo.InvariantCulture), "pJ/bit", 0, 1000, false, "Plugin runtime energy per transferred bit."),
            new(ComponentPluginRuntimeKeys.EnergyPicojoulesPerPacket, "0", "pJ", 0, 1000, false, "Plugin runtime per-packet energy."),
            new(OpticalEnergyContributionKey, contributesOpticalEnergy ? "true" : "false", "bool", Description: "Whether conversion energy also contributes to optical energy totals."),
            new(ConversionEnergyContributionKey, "true", "bool", Description: "Whether runtime energy contributes to conversion energy totals.")
        };

        if (includeConversionAliases)
        {
            schemas.Add(new ComponentParameterSchema("conversion_latency_cycles", latencyCycles.ToString(System.Globalization.CultureInfo.InvariantCulture), "cycles", 0, 1024, false, "Precision conversion latency."));
            schemas.Add(new ComponentParameterSchema("conversion_energy_pj_per_bit", energyPicojoulesPerBit.ToString(System.Globalization.CultureInfo.InvariantCulture), "pJ/bit", 0, 1000, false, "Precision conversion energy per transferred bit."));
        }

        return schemas;
    }

    /// <summary>Applies the supplied plan or metadata to the target graph or model.</summary>
    public static void Apply(HardwareComponent component, AdapterInsertionStep step)
    {
        component.Parameters["source_signal_type"] = step.MismatchField == "signal_type" ? step.SourceValue : "";
        component.Parameters["target_signal_type"] = step.MismatchField == "signal_type" ? step.DestinationValue : "";
        component.Parameters["source_precision"] = step.MismatchField == "precision" ? step.SourceValue : "";
        component.Parameters["target_precision"] = step.MismatchField == "precision" ? step.DestinationValue : "";
        component.Parameters["source_data_type"] = step.MismatchField == "data_type" ? step.SourceValue : "";
        component.Parameters["target_data_type"] = step.MismatchField == "data_type" ? step.DestinationValue : "";
        component.Parameters["source_protocol"] = step.MismatchField == "protocol" ? step.SourceValue : "";
        component.Parameters["target_protocol"] = step.MismatchField == "protocol" ? step.DestinationValue : "";
        component.Parameters[LatencyCyclesKey] = LatencyCycles(component).ToString(System.Globalization.CultureInfo.InvariantCulture);
        component.Parameters[EnergyPicojoulesPerBitKey] = EnergyPicojoulesPerBit(component).ToString(System.Globalization.CultureInfo.InvariantCulture);
        component.Parameters[ComponentPluginRuntimeKeys.ProcessingLatencyCycles] = component.Parameters[LatencyCyclesKey];
        component.Parameters[ComponentPluginRuntimeKeys.EnergyPicojoulesPerBit] = component.Parameters[EnergyPicojoulesPerBitKey];
        component.Parameters[ComponentPluginRuntimeKeys.EnergyPicojoulesPerPacket] = component.Parameters.GetValueOrDefault(ComponentPluginRuntimeKeys.EnergyPicojoulesPerPacket, "0");
        component.Parameters[OpticalEnergyContributionKey] = ContributesToOpticalEnergy(component).ToString();
        component.Parameters[ConversionEnergyContributionKey] = ContributesToConversionEnergy(component).ToString();

        if (component.Type is ComponentKind.Quantizer or ComponentKind.Dequantizer or ComponentKind.PrecisionConverter)
        {
            component.Parameters["conversion_latency_cycles"] = component.Parameters[LatencyCyclesKey];
            component.Parameters["conversion_energy_pj_per_bit"] = component.Parameters[EnergyPicojoulesPerBitKey];
        }
    }

    /// <summary>Gets whether this component participates in adapter runtime accounting.</summary>
    public static bool IsAdapterRuntimeComponent(SimComponentDef component) =>
        component.Parameters.ContainsKey(RuntimeEnabledKey) ||
        component.Parameters.ContainsKey(EnergyPicojoulesPerBitKey) ||
        DescriptorDefault(ComponentTypeIds.EffectiveTypeId(component), RuntimeEnabledKey) is not null;

    /// <summary>Gets adapter latency for a design-time component from descriptor defaults and overrides.</summary>
    public static int LatencyCycles(HardwareComponent component) => ReadInt(
        component.Parameters,
        LatencyCyclesKey,
        DescriptorDefaultInt(ComponentTypeIds.EffectiveTypeId(component), LatencyCyclesKey, 1));

    /// <summary>Gets adapter latency for a compiled component from descriptor defaults and overrides.</summary>
    public static int LatencyCycles(SimComponentDef component) => ReadInt(
        component.Parameters,
        LatencyCyclesKey,
        DescriptorDefaultInt(ComponentTypeIds.EffectiveTypeId(component), LatencyCyclesKey, 1));

    /// <summary>Gets adapter energy per bit for a design-time component from descriptor defaults and overrides.</summary>
    public static double EnergyPicojoulesPerBit(HardwareComponent component) => ReadDouble(
        component.Parameters,
        EnergyPicojoulesPerBitKey,
        DescriptorDefaultDouble(ComponentTypeIds.EffectiveTypeId(component), EnergyPicojoulesPerBitKey, 0.01));

    /// <summary>Gets adapter energy per bit for a compiled component from descriptor defaults and overrides.</summary>
    public static double EnergyPicojoulesPerBit(SimComponentDef component) => ReadDouble(
        component.Parameters,
        EnergyPicojoulesPerBitKey,
        DescriptorDefaultDouble(ComponentTypeIds.EffectiveTypeId(component), EnergyPicojoulesPerBitKey, 0.01));

    /// <summary>Gets whether adapter runtime energy should also increment optical totals.</summary>
    public static bool ContributesToOpticalEnergy(HardwareComponent component) => ReadBool(
        component.Parameters,
        OpticalEnergyContributionKey,
        DescriptorDefaultBool(ComponentTypeIds.EffectiveTypeId(component), OpticalEnergyContributionKey, false));

    /// <summary>Gets whether adapter runtime energy should also increment optical totals.</summary>
    public static bool ContributesToOpticalEnergy(SimComponentDef component) => ReadBool(
        component.Parameters,
        OpticalEnergyContributionKey,
        DescriptorDefaultBool(ComponentTypeIds.EffectiveTypeId(component), OpticalEnergyContributionKey, false));

    /// <summary>Gets whether adapter runtime energy should increment conversion totals.</summary>
    public static bool ContributesToConversionEnergy(HardwareComponent component) => ReadBool(
        component.Parameters,
        ConversionEnergyContributionKey,
        DescriptorDefaultBool(ComponentTypeIds.EffectiveTypeId(component), ConversionEnergyContributionKey, false));

    /// <summary>Gets whether adapter runtime energy should increment conversion totals.</summary>
    public static bool ContributesToConversionEnergy(SimComponentDef component) => ReadBool(
        component.Parameters,
        ConversionEnergyContributionKey,
        DescriptorDefaultBool(ComponentTypeIds.EffectiveTypeId(component), ConversionEnergyContributionKey, false));

    private static string? DescriptorDefault(string typeId, string key) =>
        DefaultDescriptors.Value.TryGetValue(ComponentTypeIds.Normalize(typeId), out var plugin)
            ? plugin.Parameters.FirstOrDefault(parameter => string.Equals(parameter.Name, key, StringComparison.OrdinalIgnoreCase))?.DefaultValue
            : null;

    private static int DescriptorDefaultInt(string typeId, string key, int fallback) =>
        int.TryParse(DescriptorDefault(typeId, key), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : fallback;

    private static double DescriptorDefaultDouble(string typeId, string key, double fallback) =>
        double.TryParse(DescriptorDefault(typeId, key), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : fallback;

    private static bool DescriptorDefaultBool(string typeId, string key, bool fallback) =>
        bool.TryParse(DescriptorDefault(typeId, key), out var parsed) ? parsed : fallback;

    private static int ReadInt(IReadOnlyDictionary<string, string> parameters, string key, int fallback) =>
        parameters.TryGetValue(key, out var raw) && int.TryParse(raw, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var parsed)
            ? Math.Max(0, parsed)
            : fallback;

    private static double ReadDouble(IReadOnlyDictionary<string, string> parameters, string key, double fallback) =>
        parameters.TryGetValue(key, out var raw) && double.TryParse(raw, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var parsed)
            ? Math.Max(0, parsed)
            : fallback;

    private static bool ReadBool(IReadOnlyDictionary<string, string> parameters, string key, bool fallback) =>
        parameters.TryGetValue(key, out var raw) && bool.TryParse(raw, out var parsed) ? parsed : fallback;
}
