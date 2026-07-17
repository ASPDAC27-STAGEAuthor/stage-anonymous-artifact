namespace HardwareSim.Core;

/// <summary>Stable first-party contract for offset-aware tensor assembly.</summary>
public static class Phase8ATensorAssemblyContract
{
    /// <summary>Stable plugin type id.</summary>
    public const string TypeId = "com.hardware-sim.first-party.collective.tensor-assembly";
    /// <summary>Stable runtime kernel id.</summary>
    public const string KernelId = "phase8a.collective.tensor-assembly";
    /// <summary>Input port carrying non-overlapping tensor slices.</summary>
    public const string InputPort = "in_slice";
    /// <summary>Output port carrying the assembled tensor.</summary>
    public const string OutputPort = "out_tensor";
    /// <summary>Only supported collective operation.</summary>
    public const string ConcatOperation = "concat";
}

/// <summary>Creates the first-party offset-aware tensor assembly descriptor.</summary>
public static class Phase8ATensorAssemblyComponentPlugin
{
    /// <summary>Explicitly registers the Phase 8A tensor assembly component.</summary>
    public static void Load(ComponentTypeRegistry registry)
    {
        if (registry is null) throw new ArgumentNullException(nameof(registry));
        FirstPartyComponentPlugins.RegisterOrThrow(registry, Create());
    }

    /// <summary>Creates the deterministic tensor assembly descriptor.</summary>
    public static ComponentPluginDescriptor Create() => new(
        Phase8ATensorAssemblyContract.TypeId,
        "Tensor Assembly",
        "Collective",
        "1.0.0",
        [
            new ComponentPortSchema(Phase8ATensorAssemblyContract.InputPort, PortDirection.Input, SignalType.Digital,
                HardwareDataType.Tensor, PrecisionKind.Any, PortProtocol.Packet, Required: true, MultiConnect: true,
                Quantity: "tensor_slice", Units: "element"),
            new ComponentPortSchema(Phase8ATensorAssemblyContract.OutputPort, PortDirection.Output, SignalType.Digital,
                HardwareDataType.Tensor, PrecisionKind.Any, PortProtocol.Packet, Required: true,
                Quantity: "assembled_tensor", Units: "element")
        ],
        [
            new ComponentParameterSchema(ComponentPluginRuntimeKeys.ProcessingLatencyCycles, "1", "cycles", 1, 1_000_000, false, "Fixed tensor assembly service latency.", IntegerOnly: true),
            new ComponentParameterSchema("input_queue_depth", "16", "packets", 1, 65_536, false, "Maximum buffered tensor slices.", IntegerOnly: true),
            new ComponentParameterSchema("output_queue_depth", "4", "packets", 1, 65_536, false, "Maximum engine-owned assembled outputs.", IntegerOnly: true),
            new ComponentParameterSchema("missing_contributor_timeout_cycles", "64", "cycles", 1, 1_000_000, false, "Cycles after first arrival before an incomplete assembly fails.", IntegerOnly: true),
            new ComponentParameterSchema("max_tensor_elements", "1048576", "elements", 1, 1_048_576, false, "Maximum assembled tensor size.", IntegerOnly: true)
        ],
        NoopValidationProvider.Instance,
        NoopCompileProvider.Instance,
        Phase8ATensorAssemblyRuntimeFactory.Instance,
        [new ComponentTraceDescriptor("phase8a.collective.tensor_assembly", TraceEventType.Compute, "Exact offset-aware tensor assembly timeline")],
        [],
        UnityPresentationDescriptor: new UnityPresentationDescriptor("tensor-assembly", "#7C3AED", "ASM", "Offset-aware tensor assembly", 96),
        SourceKind: ComponentPluginSourceKind.FirstParty,
        LegacyKind: null,
        ShowInPalette: false,
        RuntimeKernelFactory: Phase8ATensorAssemblyKernelFactory.Instance);
}

internal sealed class Phase8ATensorAssemblyRuntimeFactory : IComponentSimulationRuntimeFactory
{
    public static readonly Phase8ATensorAssemblyRuntimeFactory Instance = new();

    public ComponentSimulationRuntimeDescriptor CreateRuntime(ComponentRuntimeFactoryContext context) =>
        Phase8ACollectivePluginRuntime.CreateDescriptor(
            context,
            Phase8ATensorAssemblyKernelFactory.Instance.Descriptor,
            EnergyCategory.Compute);
}

internal sealed class Phase8ATensorAssemblyKernelFactory : IComponentRuntimeKernelFactory
{
    public static readonly Phase8ATensorAssemblyKernelFactory Instance = new();

    public ComponentRuntimeKernelDescriptor Descriptor { get; } = new()
    {
        KernelId = Phase8ATensorAssemblyContract.KernelId,
        KernelVersion = "1.0.0",
        ContractSchemaId = "phase8a.collective.tensor-assembly.config.v1",
        ImplementationHash = ComponentExecutionJson.ComputeSha256("phase8a-tensor-assembly-runtime-v1\noffset-aware-concat\ncoverage-errors\nstrict-json-metadata\nstrict-stage-output-routing\ncurrent-next-backpressure"),
        SupportedOperationKinds = [Phase8ATensorAssemblyContract.TypeId]
    };

    public IComponentRuntimeKernel CreateKernel(ComponentRuntimeKernelCreateContext context)
    {
        if (!string.Equals(context.Contract.OperationKind, Phase8ATensorAssemblyContract.TypeId, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Tensor assembly kernel operation/type mismatch.");
        return new Phase8ATensorAssemblyKernel();
    }
}
