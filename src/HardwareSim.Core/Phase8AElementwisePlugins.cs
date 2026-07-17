namespace HardwareSim.Core;

/// <summary>Stable first-party BiasAdd contract.</summary>
public static class Phase8ABiasAddContract
{
    /// <summary>Stable Phase 8A contract value for TypeId.</summary>
    public const string TypeId = "com.hardware-sim.first-party.elementwise.bias-add";
    /// <summary>Stable Phase 8A contract value for KernelId.</summary>
    public const string KernelId = "phase8a.elementwise.bias-add";
    /// <summary>Stable Phase 8A contract value for TensorInputPort.</summary>
    public const string TensorInputPort = "in_tensor";
    /// <summary>Stable Phase 8A contract value for BiasInputPort.</summary>
    public const string BiasInputPort = "in_bias";
    /// <summary>Stable Phase 8A contract value for OutputPort.</summary>
    public const string OutputPort = "out_tensor";
}

/// <summary>Stable first-party ReLU contract.</summary>
public static class Phase8AReluContract
{
    /// <summary>Stable Phase 8A contract value for TypeId.</summary>
    public const string TypeId = "com.hardware-sim.first-party.elementwise.relu";
    /// <summary>Stable Phase 8A contract value for KernelId.</summary>
    public const string KernelId = "phase8a.elementwise.relu";
    /// <summary>Stable Phase 8A contract value for InputPort.</summary>
    public const string InputPort = "in_tensor";
    /// <summary>Stable Phase 8A contract value for OutputPort.</summary>
    public const string OutputPort = "out_tensor";
}

/// <summary>Registers Phase 8A elementwise execution plugins.</summary>
public static class Phase8AElementwiseComponentPlugins
{
    /// <summary>Loads BiasAdd and ReLU into the supplied registry.</summary>
    public static void Load(ComponentTypeRegistry registry)
    {
        if (registry is null) throw new ArgumentNullException(nameof(registry));
        FirstPartyComponentPlugins.RegisterOrThrow(registry, BiasAdd());
        FirstPartyComponentPlugins.RegisterOrThrow(registry, Relu());
    }

    private static ComponentPluginDescriptor BiasAdd() => new(
        Phase8ABiasAddContract.TypeId, "Bias Add", "Elementwise", "1.0.0",
        [
            Port(Phase8ABiasAddContract.TensorInputPort, PortDirection.Input, "input_tensor"),
            Port(Phase8ABiasAddContract.BiasInputPort, PortDirection.Input, "resident_bias"),
            Port(Phase8ABiasAddContract.OutputPort, PortDirection.Output, "biased_tensor")
        ],
        Parameters(16), NoopValidationProvider.Instance, NoopCompileProvider.Instance,
        new RuntimeFactory(Phase8ABiasAddKernelFactory.Instance),
        [new ComponentTraceDescriptor("phase8a.elementwise.bias_add", TraceEventType.Compute, "Bias preload, reuse, and vector-add timeline")], [],
        UnityPresentationDescriptor: new UnityPresentationDescriptor("bias-add", "#D97706", "B+", "Reusable vector bias add", 97),
        SourceKind: ComponentPluginSourceKind.FirstParty, LegacyKind: null, ShowInPalette: false,
        RuntimeKernelFactory: Phase8ABiasAddKernelFactory.Instance);

    private static ComponentPluginDescriptor Relu() => new(
        Phase8AReluContract.TypeId, "ReLU", "Elementwise", "1.0.0",
        [Port(Phase8AReluContract.InputPort, PortDirection.Input, "input_tensor"), Port(Phase8AReluContract.OutputPort, PortDirection.Output, "rectified_tensor")],
        Parameters(16), NoopValidationProvider.Instance, NoopCompileProvider.Instance,
        new RuntimeFactory(Phase8AReluKernelFactory.Instance),
        [new ComponentTraceDescriptor("phase8a.elementwise.relu", TraceEventType.Compute, "Deterministic elementwise ReLU timeline")], [],
        UnityPresentationDescriptor: new UnityPresentationDescriptor("relu", "#EA580C", "R", "Elementwise ReLU", 98),
        SourceKind: ComponentPluginSourceKind.FirstParty, LegacyKind: null, ShowInPalette: false,
        RuntimeKernelFactory: Phase8AReluKernelFactory.Instance);

    private static ComponentPortSchema Port(string name, PortDirection direction, string quantity) => new(
        name, direction, SignalType.Digital, HardwareDataType.Tensor, PrecisionKind.Any, PortProtocol.Packet,
        Required: true, MultiConnect: direction == PortDirection.Input, Quantity: quantity, Units: "element");

    private static IReadOnlyList<ComponentParameterSchema> Parameters(int defaultDepth) =>
    [
        new(ComponentPluginRuntimeKeys.ProcessingLatencyCycles, "1", "cycles", 1, 1_000_000, false, "Fixed elementwise service latency.", IntegerOnly: true),
        new("input_queue_depth", defaultDepth.ToString(System.Globalization.CultureInfo.InvariantCulture), "packets", 1, 65_536, false, "Maximum buffered input packets.", IntegerOnly: true),
        new("output_queue_depth", "8", "packets", 1, 65_536, false, "Maximum engine-owned outputs.", IntegerOnly: true)
    ];

    private sealed class RuntimeFactory : IComponentSimulationRuntimeFactory
    {
        private readonly IComponentRuntimeKernelFactory kernel;
        public RuntimeFactory(IComponentRuntimeKernelFactory kernel) => this.kernel = kernel;
        public ComponentSimulationRuntimeDescriptor CreateRuntime(ComponentRuntimeFactoryContext context) =>
            Phase8ACollectivePluginRuntime.CreateDescriptor(context, kernel.Descriptor, EnergyCategory.Compute);
    }
}

internal sealed class Phase8ABiasAddKernelFactory : IComponentRuntimeKernelFactory
{
    public static readonly Phase8ABiasAddKernelFactory Instance = new();
    public ComponentRuntimeKernelDescriptor Descriptor { get; } = new()
    {
        KernelId = Phase8ABiasAddContract.KernelId,
        KernelVersion = "1.0.0",
        ContractSchemaId = "phase8a.elementwise.bias-add.config.v1",
        ImplementationHash = ComponentExecutionJson.ComputeSha256("phase8a-bias-add-v1\nresident-bias-reuse\nvalidated-precision\nstrict-stage-output-routing\nbounded-output-backpressure"),
        SupportedOperationKinds = [Phase8ABiasAddContract.TypeId]
    };
    public IComponentRuntimeKernel CreateKernel(ComponentRuntimeKernelCreateContext context) => new Phase8ABiasAddKernel();
}

internal sealed class Phase8AReluKernelFactory : IComponentRuntimeKernelFactory
{
    public static readonly Phase8AReluKernelFactory Instance = new();
    public ComponentRuntimeKernelDescriptor Descriptor { get; } = new()
    {
        KernelId = Phase8AReluContract.KernelId,
        KernelVersion = "1.0.0",
        ContractSchemaId = "phase8a.elementwise.relu.config.v1",
        ImplementationHash = ComponentExecutionJson.ComputeSha256("phase8a-relu-v1\nexact-elementwise\nvalidated-digital-payload\nstrict-stage-output-routing\nbounded-output-backpressure"),
        SupportedOperationKinds = [Phase8AReluContract.TypeId]
    };
    public IComponentRuntimeKernel CreateKernel(ComponentRuntimeKernelCreateContext context) => new Phase8AReluKernel();
}
