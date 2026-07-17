using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;

namespace HardwareSim.Core;

/// <summary>Stable first-party contract for deterministic grouped vector-sum collectives.</summary>
public static class Phase8AGroupedVectorSumContract
{
    /// <summary>Stable plugin type id.</summary>
    public const string TypeId = "com.hardware-sim.first-party.collective.grouped-vector-sum";
    /// <summary>Stable runtime kernel id.</summary>
    public const string KernelId = "phase8a.collective.grouped-vector-sum";
    /// <summary>Input port carrying partial vectors.</summary>
    public const string InputPort = "in_partial";
    /// <summary>Output port carrying the reduced vector.</summary>
    public const string OutputPort = "out_sum";
    /// <summary>Required collective operation metadata key.</summary>
    public const string OperationKind = "phase8a.collective.operation";
    /// <summary>Required group identity metadata key.</summary>
    public const string GroupKey = "phase8a.collective.group_key";
    /// <summary>Required ordered contributor-list metadata key.</summary>
    public const string ExpectedContributors = "phase8a.collective.expected_contributors";
    /// <summary>Required contributor identity metadata key.</summary>
    public const string ContributorId = "phase8a.collective.contributor_id";
    /// <summary>Required output M-offset metadata key.</summary>
    public const string OutputMOffset = "phase8a.collective.output_m_offset";
    /// <summary>Required output M-extent metadata key.</summary>
    public const string OutputMExtent = "phase8a.collective.output_m_extent";
    /// <summary>Required output N-offset metadata key.</summary>
    public const string OutputNOffset = "phase8a.collective.output_n_offset";
    /// <summary>Required output N-extent metadata key.</summary>
    public const string OutputNExtent = "phase8a.collective.output_n_extent";
    /// <summary>Required payload dtype metadata key.</summary>
    public const string DType = "phase8a.collective.dtype";
    /// <summary>Metadata value identifying the only supported operation.</summary>
    public const string SumOperation = "sum";
}

/// <summary>Creates the first-party grouped vector-sum component descriptor.</summary>
public static class Phase8AGroupedVectorSumComponentPlugin
{
    /// <summary>Registers this first-party Phase 8A grouped vector Sum descriptor.</summary>
    public static void Load(ComponentTypeRegistry registry)
    {
        if (registry is null) throw new ArgumentNullException(nameof(registry));
        FirstPartyComponentPlugins.RegisterOrThrow(registry, Create());
    }

    /// <summary>Creates a deterministic grouped vector-sum plugin descriptor.</summary>
    public static ComponentPluginDescriptor Create()
    {
        var parameters = new List<ComponentParameterSchema>
        {
            new(ComponentPluginRuntimeKeys.ProcessingLatencyCycles, "1", "cycles", 1, 1_000_000, false, "Fixed vector-sum service latency.", IntegerOnly: true),
            new("input_queue_depth", "8", "packets", 1, 65_536, false, "Maximum buffered contributors.", IntegerOnly: true),
            new("output_queue_depth", "4", "packets", 1, 65_536, false, "Maximum engine-owned output queue depth.", IntegerOnly: true),
            new("missing_contributor_timeout_cycles", "64", "cycles", 1, 1_000_000, false, "Cycles after first arrival before an incomplete group fails.", IntegerOnly: true),
            new("max_vector_elements", "1048576", "elements", 1, 1_048_576, false, "Maximum vector element count.", IntegerOnly: true)
        };
        var traces = new List<ComponentTraceDescriptor>
        {
            new("phase8a.collective.grouped_vector_sum", TraceEventType.Compute, "Exact grouped vector-sum current/next runtime")
        };
        return new ComponentPluginDescriptor(
            Phase8AGroupedVectorSumContract.TypeId,
            "Grouped Vector Sum",
            "Collective",
            "1.0.0",
            [
                new ComponentPortSchema(Phase8AGroupedVectorSumContract.InputPort, PortDirection.Input, SignalType.Digital,
                    HardwareDataType.Tensor, PrecisionKind.Any, PortProtocol.Packet, Required: true, MultiConnect: true,
                    Quantity: "partial_vector", Units: "element"),
                new ComponentPortSchema(Phase8AGroupedVectorSumContract.OutputPort, PortDirection.Output, SignalType.Digital,
                    HardwareDataType.Tensor, PrecisionKind.Any, PortProtocol.Packet, Required: true,
                    Quantity: "reduced_vector", Units: "element")
            ],
            parameters,
            NoopValidationProvider.Instance,
            NoopCompileProvider.Instance,
            Phase8AGroupedVectorSumRuntimeFactory.Instance,
            traces,
            [],
            UnityPresentationDescriptor: new UnityPresentationDescriptor("grouped-vector-sum", "#2563EB", "GVS", "Ordered partial-vector sum", 95),
            SourceKind: ComponentPluginSourceKind.FirstParty,
            LegacyKind: null,
            ShowInPalette: false,
            RuntimeKernelFactory: Phase8AGroupedVectorSumKernelFactory.Instance);
    }
}

internal sealed class Phase8AGroupedVectorSumRuntimeFactory : IComponentSimulationRuntimeFactory
{
    public static readonly Phase8AGroupedVectorSumRuntimeFactory Instance = new();

    public ComponentSimulationRuntimeDescriptor CreateRuntime(ComponentRuntimeFactoryContext context)
    {
        var parameters = context.Plugin.Parameters.ToDictionary(
            schema => schema.Name,
            schema => context.Component.Parameters.GetValueOrDefault(schema.Name, schema.DefaultValue),
            StringComparer.OrdinalIgnoreCase);
        var descriptor = Phase8AGroupedVectorSumKernelFactory.Instance.Descriptor;
        return new ComponentSimulationRuntimeDescriptor
        {
            ProcessingLatencyCycles = ReadInt(parameters, ComponentPluginRuntimeKeys.ProcessingLatencyCycles, 1),
            EnergyCategory = EnergyCategory.Compute,
            TraceDescriptors = context.Plugin.TraceDescriptors,
            MetricDescriptors = context.Plugin.MetricDescriptors,
            Parameters = new ReadOnlyDictionary<string, string>(parameters),
            KernelId = descriptor.KernelId,
            KernelVersion = descriptor.KernelVersion,
            ContractSchemaId = descriptor.ContractSchemaId,
            CanonicalKernelConfiguration = ComponentExecutionJson.CanonicalizeJson(JsonSerializer.Serialize(
                parameters.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                    .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
                HardwareGraphJson.Options)),
            KernelImplementationHash = descriptor.ImplementationHash
        };
    }

    private static int ReadInt(IReadOnlyDictionary<string, string> values, string key, int fallback) =>
        values.TryGetValue(key, out var raw) && int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? Math.Max(0, value)
            : fallback;
}

internal sealed class Phase8AGroupedVectorSumKernelFactory : IComponentRuntimeKernelFactory
{
    public static readonly Phase8AGroupedVectorSumKernelFactory Instance = new();

    public ComponentRuntimeKernelDescriptor Descriptor { get; } = new()
    {
        KernelId = Phase8AGroupedVectorSumContract.KernelId,
        KernelVersion = "1.0.0",
        ContractSchemaId = "phase8a.collective.grouped-vector-sum.config.v1",
        ImplementationHash = ComponentExecutionJson.ComputeSha256("phase8a-grouped-vector-sum-runtime-v1\nordered-contributors\nstrict-json-metadata\nstrict-stage-output-routing\ncurrent-next-backpressure"),
        SupportedOperationKinds = [Phase8AGroupedVectorSumContract.TypeId]
    };

    public IComponentRuntimeKernel CreateKernel(ComponentRuntimeKernelCreateContext context)
    {
        if (!string.Equals(context.Contract.OperationKind, Phase8AGroupedVectorSumContract.TypeId, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Grouped vector-sum kernel operation/type mismatch.");
        return new Phase8AGroupedVectorSumKernel();
    }
}
