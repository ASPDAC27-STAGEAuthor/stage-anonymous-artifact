using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;

namespace HardwareSim.Core;

internal static class Phase8ACollectivePluginRuntime
{
    public static ComponentSimulationRuntimeDescriptor CreateDescriptor(
        ComponentRuntimeFactoryContext context,
        ComponentRuntimeKernelDescriptor kernel,
        EnergyCategory energyCategory)
    {
        var parameters = context.Plugin.Parameters.ToDictionary(
            schema => schema.Name,
            schema => context.Component.Parameters.GetValueOrDefault(schema.Name, schema.DefaultValue),
            StringComparer.OrdinalIgnoreCase);
        return new ComponentSimulationRuntimeDescriptor
        {
            ProcessingLatencyCycles = ReadParameterInt(parameters, ComponentPluginRuntimeKeys.ProcessingLatencyCycles, 1),
            EnergyCategory = energyCategory,
            TraceDescriptors = context.Plugin.TraceDescriptors,
            MetricDescriptors = context.Plugin.MetricDescriptors,
            Parameters = new ReadOnlyDictionary<string, string>(parameters),
            KernelId = kernel.KernelId,
            KernelVersion = kernel.KernelVersion,
            ContractSchemaId = kernel.ContractSchemaId,
            CanonicalKernelConfiguration = ComponentExecutionJson.CanonicalizeJson(JsonSerializer.Serialize(
                parameters.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                    .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
                HardwareGraphJson.Options)),
            KernelImplementationHash = kernel.ImplementationHash
        };
    }

    private static int ReadParameterInt(IReadOnlyDictionary<string, string> values, string key, int fallback) =>
        values.TryGetValue(key, out var raw) && int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? Math.Max(0, value)
            : fallback;

    public static int ReadResourceInt(CompiledComponentExecutionContract contract, string key, int fallback) =>
        int.TryParse(contract.Resources.FirstOrDefault(resource => string.Equals(resource.Name, key, StringComparison.OrdinalIgnoreCase))?.CanonicalValue,
            NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : fallback;
}
