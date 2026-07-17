using System.Text.Json;

namespace HardwareSim.Core;

internal sealed class Phase8AStaticWeightDigitalVmmKernelFactory : IComponentRuntimeKernelFactory, IComponentExecutionContractCompiler
{
    public const string KernelId = "phase8a.core.digital.vmm.static-weight";
    public const string PluginTypeId = "com.hardware-sim.phase8a.static-weight-vmm-kernel";
    public const string InitialWeightValuesParameter = "phase8a_initial_weight_values_json";
    public const string InitialWeightIdParameter = "phase8a_initial_weight_id";
    private const string InitialWeightValuesConfiguration = "initial_weight_values_json";
    private const string InitialWeightIdConfiguration = "initial_weight_id";
    public static readonly Phase8AStaticWeightDigitalVmmKernelFactory Instance = new();

    public ComponentRuntimeKernelDescriptor Descriptor { get; } = new()
    {
        KernelId = KernelId,
        KernelVersion = "1.0.0",
        ContractSchemaId = CoreDigitalVmmKernelFactory.SchemaId,
        ImplementationHash = ComponentExecutionJson.ComputeSha256(
            "phase8a-static-weight-digital-vmm-v1\ninitial-committed-weights\nno-runtime-weight-packet\ncore-digital-vmm-delegate"),
        SupportedOperationKinds = ["digital_vmm"]
    };

    public static void Configure(ComponentTypeRegistry registry, ComponentTemplate template)
    {
        var basePlugin = registry.GetPlugin(ComponentTypeIds.BuiltIn(ComponentKind.ProcessingElement))
            ?? throw new InvalidOperationException("Built-in processing-element plugin is unavailable.");
        var registration = registry.Register(basePlugin with
        {
            TypeId = PluginTypeId,
            DisplayName = "Phase 8A Static Weight VMM Kernel",
            Version = "1.0.0",
            LegacyKind = null,
            ShowInPalette = false,
            RuntimeKernelFactory = Instance,
            KernelTestScenarioProvider = null,
            LegacyAliases = []
        });
        if (!registration.IsSuccess)
            throw new InvalidOperationException(string.Join("; ", registration.Issues.Select(issue => issue.Message)));

        template.Parameters.Add(new TemplateParameter
        {
            Name = InitialWeightValuesParameter,
            ValueKind = TemplateParameterValueKind.String,
            DefaultValue = "[]",
            Description = "Scenario-local row-major weights committed before cycle zero."
        });
        template.Parameters.Add(new TemplateParameter
        {
            Name = InitialWeightIdParameter,
            ValueKind = TemplateParameterValueKind.String,
            DefaultValue = "static-weight",
            Description = "Stable provenance id for weights committed before cycle zero."
        });
        var binding = template.ExecutionBinding
            ?? throw new InvalidOperationException("The PE template has no execution binding.");
        binding.KernelId = KernelId;
        binding.KernelVersionRequirement = "1.x";
        binding.ContractSchemaId = CoreDigitalVmmKernelFactory.SchemaId;
        binding.ConfigurationBindings[InitialWeightValuesConfiguration] = "resolved." + InitialWeightValuesParameter;
        binding.ConfigurationBindings[InitialWeightIdConfiguration] = "resolved." + InitialWeightIdParameter;
    }

    public ComponentExecutionContractCompileResult CompileExecutionContract(ComponentExecutionContractCompileContext context) =>
        CoreDigitalVmmKernelFactory.Instance.CompileExecutionContract(context);

    public IComponentRuntimeKernel CreateKernel(ComponentRuntimeKernelCreateContext context)
    {
        var values = JsonSerializer.Deserialize<Dictionary<string, string>>(
            context.Contract.KernelConfiguration.CanonicalJson,
            HardwareGraphJson.Options) ?? throw new InvalidOperationException("Static-weight VMM configuration is missing.");
        var weights = JsonSerializer.Deserialize<double[]>(
            values.GetValueOrDefault(InitialWeightValuesConfiguration, "[]"),
            HardwareGraphJson.Options) ?? throw new InvalidOperationException("Static-weight VMM initial weights are missing.");
        var configuration = CoreDigitalVmmConfiguration.FromContract(context.Contract);
        if (weights.Length != configuration.WeightValueCount)
            throw new InvalidOperationException($"Static-weight VMM requires {configuration.WeightValueCount} values but received {weights.Length}.");
        var weightId = values.GetValueOrDefault(InitialWeightIdConfiguration, "").Trim();
        if (weightId.Length == 0)
            throw new InvalidOperationException("Static-weight VMM requires a stable initial weight id.");
        return new Phase8AStaticWeightDigitalVmmKernel(configuration, weights, weightId);
    }
}

internal sealed class Phase8AStaticWeightDigitalVmmKernel : IPhaseSafeComponentRuntimeKernel
{
    private readonly CoreDigitalVmmKernel inner;
    private readonly CoreDigitalVmmConfiguration configuration;
    private readonly double[] weights;
    private readonly string weightId;

    public Phase8AStaticWeightDigitalVmmKernel(
        CoreDigitalVmmConfiguration configuration,
        IReadOnlyList<double> weights,
        string weightId)
    {
        this.configuration = configuration;
        this.weights = weights.ToArray();
        this.weightId = weightId;
        inner = new CoreDigitalVmmKernel(configuration);
    }

    public IComponentRuntimeKernelState CreateInitialState(CompiledComponentExecutionContract contract)
    {
        var state = (CoreDigitalVmmState)inner.CreateInitialState(contract);
        state.CommittedWeights = weights
            .Select(value => DigitalNumericFormats.Quantize(value, configuration.WeightDType).Value)
            .ToArray();
        state.WeightPacketId = weightId;
        state.WeightVersion = 1;
        return state;
    }

    public bool CanAccept(ComponentRuntimeKernelCycleContext context, IComponentRuntimeKernelState current, string inputPortName) =>
        inner.CanAccept(context, current, inputPortName);

    public ComponentRuntimeKernelInputResult SampleInput(
        ComponentRuntimeKernelCycleContext context,
        IComponentRuntimeKernelState current,
        IComponentRuntimeKernelState next,
        ComponentRuntimeKernelInput input) => inner.SampleInput(context, current, next, input);

    public ComponentRuntimeKernelAdvanceResult Advance(
        ComponentRuntimeKernelCycleContext context,
        IComponentRuntimeKernelState current,
        IComponentRuntimeKernelState next,
        bool outputQueueAvailable) => inner.Advance(context, current, next, outputQueueAvailable);
}