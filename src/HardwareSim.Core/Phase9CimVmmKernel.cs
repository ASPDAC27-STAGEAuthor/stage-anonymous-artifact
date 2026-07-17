#pragma warning disable CS1591

using System.Globalization;
using System.Text.Json;

namespace HardwareSim.Core;

/// <summary>Phase 9 CIM shell compiler over the production exact VMM runtime.</summary>
internal sealed class Phase9CimVmmKernelFactory : IComponentRuntimeKernelFactory, IComponentExecutionContractCompiler
{
    public const string KernelId = "phase9.cim.vmm";
    public static readonly Phase9CimVmmKernelFactory Instance = new();

    public ComponentRuntimeKernelDescriptor Descriptor { get; } = new()
    {
        KernelId = KernelId,
        KernelVersion = "1.0.0",
        ContractSchemaId = CoreDigitalVmmKernelFactory.SchemaId,
        ImplementationHash = ComponentExecutionJson.ComputeSha256(
            "phase9-cim-vmm-v1\nexact-core-vmm-delegate\ntyped-storage-capability\ndevice-profile-footprint-provenance"),
        SupportedOperationKinds = ["digital_vmm"]
    };

    public IComponentRuntimeKernel CreateKernel(ComponentRuntimeKernelCreateContext context) =>
        new CoreDigitalVmmKernel(CoreDigitalVmmConfiguration.FromContract(context.Contract));

    public ComponentExecutionContractCompileResult CompileExecutionContract(ComponentExecutionContractCompileContext context)
    {
        var result = CoreDigitalVmmKernelFactory.Instance.CompileExecutionContract(context);
        if (result.Contract is null) return result;
        var contract = result.Contract;
        var values = context.ConfigurationValues;
        var capacity = Integer(values, "storage_capacity_bits");
        var readBandwidth = Integer(values, "macs_per_cycle") * 8;
        var writeBandwidth = Integer(values, "weight_write_bandwidth_bits_per_cycle");
        var writeLatency = Integer(values, "weight_write_latency_cycles", allowZero: true);
        var precision = context.Template.ExternalPorts.Single(port => port.Name == "in_weight").Precision.ToString();
        var storage = new
        {
            resourceId = context.Template.StorageLayouts.Single(layout => string.Equals(layout.LogicalName, "weight", StringComparison.OrdinalIgnoreCase)).Id,
            storageLevelId = "cim-array-local",
            supportedOperandRoleIds = new[] { "weight" },
            supportedPrecisionIds = new[] { precision },
            preloadPortSemanticRole = "weight",
            capacityBits = capacity,
            alignmentBits = 8,
            allocationGranularityBits = 8,
            residentSlots = 1,
            readBandwidthBitsPerCycle = Math.Max(1, readBandwidth),
            writeBandwidthBitsPerCycle = Math.Max(1, writeBandwidth),
            readLatencyCycles = Math.Max(0, contract.Timing.PipelineLatencyCycles),
            writeLatencyCycles = writeLatency,
            commitModeId = "write-then-commit-v1",
            supportsStreaming = false,
            supportsReuse = true
        };
        contract.Resources.Add(new CompiledComponentResourceEntry
        {
            Name = "phase9_cim_weight_storage",
            ResourceKind = Phase8ACapabilityResourceKinds.MappingStorageCapability,
            Units = "canonical-json",
            CanonicalValue = ComponentExecutionJson.CanonicalizeJson(JsonSerializer.Serialize(storage, HardwareGraphJson.Options)),
            ValueType = "object"
        });
        contract.Resources.AddRange(context.ProfileSnapshots.OrderBy(snapshot => snapshot.Id, StringComparer.Ordinal).Select(snapshot => new CompiledComponentResourceEntry
        {
            Name = "device_profile:" + snapshot.Id,
            ResourceKind = "device_profile_snapshot",
            Units = snapshot.Units,
            CanonicalValue = snapshot.Hash,
            ValueType = "sha256"
        }));
        contract.TraceDescriptors.AddRange(
        [
            new ComponentTraceDescriptor("phase9.cim.array", TraceEventType.Compute, "CIM array compute and write timeline"),
            new ComponentTraceDescriptor("phase9.cim.conversion", TraceEventType.Compute, "DAC/ADC conversion timeline"),
            new ComponentTraceDescriptor("phase9.cim.nonideal", TraceEventType.Compute, "Enabled non-ideal effect summary")
        ]);
        contract.MetricDescriptors.AddRange(
        [
            new ComponentMetricDescriptor("phase9.cim.array_energy", "pJ", EnergyCategory.Compute, "Array operation energy"),
            new ComponentMetricDescriptor("phase9.cim.adc_energy", "pJ", EnergyCategory.Compute, "ADC conversion energy"),
            new ComponentMetricDescriptor("phase9.cim.dac_energy", "pJ", EnergyCategory.Compute, "DAC conversion energy")
        ]);
        contract.Provenance.FunctionalIdealOnly = !values.TryGetValue("execution_mode", out var mode) || string.Equals(mode, "functional", StringComparison.OrdinalIgnoreCase);
        return new ComponentExecutionContractCompileResult { Contract = contract, Issues = result.Issues };
    }

    private static long Integer(IReadOnlyDictionary<string, string> values, string key, bool allowZero = false)
    {
        if (values.TryGetValue(key, out var raw) && long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) && (allowZero ? value >= 0 : value > 0))
            return value;
        throw new InvalidOperationException($"Phase 9 CIM configuration '{key}' is invalid.");
    }
}

internal sealed class Phase9CimVmmScenarioProvider : IComponentKernelTestScenarioProvider
{
    public static readonly Phase9CimVmmScenarioProvider Instance = new();

    public ComponentKernelTestScenarioProviderDescriptor Descriptor { get; } = new()
    {
        KernelId = Phase9CimVmmKernelFactory.KernelId,
        KernelVersion = "1.0.0",
        ContractSchemaId = CoreDigitalVmmKernelFactory.SchemaId,
        ProviderVersion = "1.0.0"
    };

    public ComponentKernelTestScenario CreateScenario(CompiledComponentExecutionContract contract, int seed) =>
        CoreDigitalVmmScenarioProvider.Instance.CreateScenario(contract, seed);

    public ComponentKernelTestEvaluationResult EvaluateScenario(ComponentKernelTestScenario scenario, ComponentKernelTestObservation observation) =>
        CoreDigitalVmmScenarioProvider.Instance.EvaluateScenario(scenario, observation);
}
