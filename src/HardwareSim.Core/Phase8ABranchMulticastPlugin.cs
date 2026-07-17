namespace HardwareSim.Core;

/// <summary>Stable first-party contract for branch-local digital multicast.</summary>
public static class Phase8ABranchMulticastContract
{
    /// <summary>Stable branch multicast contract value for TypeId.</summary>
    public const string TypeId = "com.hardware-sim.first-party.collective.multicast";
    /// <summary>Stable branch multicast contract value for KernelId.</summary>
    public const string KernelId = "phase8a.collective.branch-multicast";
    /// <summary>Stable branch multicast contract value for InputPort.</summary>
    public const string InputPort = "in_packet";
    /// <summary>Stable branch multicast contract value for OutputPort.</summary>
    public const string OutputPort = "out_branch";
    /// <summary>Stable branch multicast contract value for MulticastOperation.</summary>
    public const string MulticastOperation = "multicast";
}

/// <summary>Creates the first-party branch multicast component descriptor.</summary>
public static class Phase8ABranchMulticastComponentPlugin
{
    /// <summary>Explicitly registers the Phase 8A branch multicast component.</summary>
    public static void Load(ComponentTypeRegistry registry)
    {
        if (registry is null) throw new ArgumentNullException(nameof(registry));
        FirstPartyComponentPlugins.RegisterOrThrow(registry, Create());
    }

    /// <summary>Creates the deterministic branch multicast descriptor.</summary>
    public static ComponentPluginDescriptor Create() => new(
        Phase8ABranchMulticastContract.TypeId,
        "Branch Multicast",
        "Collective",
        "1.0.0",
        [
            new ComponentPortSchema(Phase8ABranchMulticastContract.InputPort, PortDirection.Input, SignalType.Digital,
                HardwareDataType.Tensor, PrecisionKind.Any, PortProtocol.Packet, Required: true,
                Quantity: "parent_payload", Units: "bit"),
            new ComponentPortSchema(Phase8ABranchMulticastContract.OutputPort, PortDirection.Output, SignalType.Digital,
                HardwareDataType.Tensor, PrecisionKind.Any, PortProtocol.Packet, Required: true, MultiConnect: true,
                Quantity: "branch_clone", Units: "bit")
        ],
        [
            new ComponentParameterSchema(ComponentPluginRuntimeKeys.ProcessingLatencyCycles, "1", "cycles", 1, 1_000_000, false, "Branch replication service latency.", IntegerOnly: true),
            new ComponentParameterSchema("input_queue_depth", "8", "packets", 1, 65_536, false, "Maximum accepted parent packets.", IntegerOnly: true),
            new ComponentParameterSchema("output_queue_depth", "8", "packets", 1, 65_536, false, "Maximum engine-owned clone queue depth.", IntegerOnly: true),
            new ComponentParameterSchema("max_fanout", "1024", "consumers", 2, 65_536, false, "Maximum targets for one branch event.", IntegerOnly: true)
        ],
        NoopValidationProvider.Instance,
        NoopCompileProvider.Instance,
        Phase8ABranchMulticastRuntimeFactory.Instance,
        [new ComponentTraceDescriptor("phase8a.collective.branch_multicast", TraceEventType.Compute, "Branch-local clone and bits-conservation timeline")],
        [],
        UnityPresentationDescriptor: new UnityPresentationDescriptor("branch-multicast", "#0EA5E9", "MC", "Branch-local digital multicast", 94),
        SourceKind: ComponentPluginSourceKind.FirstParty,
        LegacyKind: null,
        ShowInPalette: false,
        RuntimeKernelFactory: Phase8ABranchMulticastKernelFactory.Instance);
}

internal sealed class Phase8ABranchMulticastRuntimeFactory : IComponentSimulationRuntimeFactory
{
    public static readonly Phase8ABranchMulticastRuntimeFactory Instance = new();

    public ComponentSimulationRuntimeDescriptor CreateRuntime(ComponentRuntimeFactoryContext context) =>
        Phase8ACollectivePluginRuntime.CreateDescriptor(
            context,
            Phase8ABranchMulticastKernelFactory.Instance.Descriptor,
            EnergyCategory.NoC);
}

internal sealed class Phase8ABranchMulticastKernelFactory : IComponentRuntimeKernelFactory
{
    public static readonly Phase8ABranchMulticastKernelFactory Instance = new();

    public ComponentRuntimeKernelDescriptor Descriptor { get; } = new()
    {
        KernelId = Phase8ABranchMulticastContract.KernelId,
        KernelVersion = "1.0.0",
        ContractSchemaId = "phase8a.collective.branch-multicast.config.v1",
        ImplementationHash = ComponentExecutionJson.ComputeSha256("phase8a-branch-multicast-runtime-v1\nbranch-local-clone\nexact-route\nexact-digital-payload\nstrict-typed-plan\nbuffered-parent-id-lifetime\nfail-closed-target-pipeline\ncurrent-next-backpressure"),
        SupportedOperationKinds = [Phase8ABranchMulticastContract.TypeId]
    };

    public IComponentRuntimeKernel CreateKernel(ComponentRuntimeKernelCreateContext context)
    {
        if (!string.Equals(context.Contract.OperationKind, Phase8ABranchMulticastContract.TypeId, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Branch multicast kernel operation/type mismatch.");
        return new Phase8ABranchMulticastKernel();
    }
}
