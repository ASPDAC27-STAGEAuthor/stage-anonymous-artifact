using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;

namespace HardwareSim.Core;

/// <summary>Machine-readable outcome of one deterministic component-kernel scenario.</summary>
public sealed class ComponentKernelTestRunResult
{
    /// <summary>Gets structured compile, runtime, and comparison diagnostics.</summary>
    public IReadOnlyList<ComponentTemplateIssue> Issues { get; init; } = [];
    /// <summary>Gets the compiled profile used by the production engine.</summary>
    public CompiledComponentProfile? Profile { get; init; }
    /// <summary>Gets the provider-generated deterministic scenario.</summary>
    public ComponentKernelTestScenario? Scenario { get; init; }
    /// <summary>Gets the production cycle-engine result.</summary>
    public SimulationResult? Simulation { get; init; }
    /// <summary>Gets cycle-qualified events emitted for the component under test.</summary>
    public IReadOnlyList<ComponentKernelTestTimelineEvent> Timeline { get; init; } = [];
    /// <summary>Gets the frozen runtime-kernel registry hash.</summary>
    public string RuntimeKernelRegistryHash { get; init; } = "";
    /// <summary>Gets the compiled profile hash.</summary>
    public string ProfileHash { get; init; } = "";
    /// <summary>Gets the exact execution-contract hash.</summary>
    public string ExecutionContractHash { get; init; } = "";
    /// <summary>Gets the canonical deterministic input hash.</summary>
    public string InputHash { get; init; } = "";
    /// <summary>Gets the independent reference output hash.</summary>
    public string ExpectedOutputHash { get; init; } = "";
    /// <summary>Gets the observed production output hash.</summary>
    public string ActualOutputHash { get; init; } = "";
    /// <summary>Gets the canonical production trace hash.</summary>
    public string TraceHash { get; init; } = "";
    /// <summary>Gets kernel-specific machine-readable artifacts such as separate activation, weight, and reference hashes.</summary>
    public IReadOnlyDictionary<string, string> Artifacts { get; init; } = new ReadOnlyDictionary<string, string>(new Dictionary<string, string>());
    /// <summary>Gets whether strict compile, production execution, timing, and output comparison all passed.</summary>
    public bool IsSuccess =>
        Profile is not null &&
        Scenario is not null &&
        Simulation?.Completed == true &&
        Issues.All(issue => issue.Severity is not ComponentTemplateIssueSeverity.Error and not ComponentTemplateIssueSeverity.Fatal) &&
        !string.IsNullOrWhiteSpace(ExpectedOutputHash) &&
        string.Equals(ExpectedOutputHash, ActualOutputHash, StringComparison.Ordinal);
}

/// <summary>Runs registered kernel scenarios through strict template compilation and the production cycle engine.</summary>
public sealed class ComponentKernelTestRunner
{
    private const string ComponentId = "component_under_test";
    private const string SinkId = "component_test_sink";

    /// <summary>Compiles and executes one exact registered scenario without target-kind-specific dispatch.</summary>
    public ComponentKernelTestRunResult Run(
        ComponentTemplate template,
        ComponentTypeRegistry componentRegistry,
        IReadOnlyDictionary<string, string>? instanceOverrides = null,
        IReadOnlyDictionary<string, CharacterizedProfileSnapshot>? externalSnapshots = null,
        int seed = 42)
    {
        if (template is null) throw new ArgumentNullException(nameof(template));
        if (componentRegistry is null) throw new ArgumentNullException(nameof(componentRegistry));

        var issues = new List<ComponentTemplateIssue>();
        var frozen = componentRegistry.FreezeRuntimeKernels();
        issues.AddRange(frozen.Issues.Select(FromPluginIssue));
        if (!frozen.IsSuccess || frozen.Snapshot is null)
        {
            return Result(issues, registryHash: frozen.Snapshot?.ContentHash ?? "");
        }

        var snapshot = frozen.Snapshot;
        var compile = new ComponentTemplateCompiler().Compile(template, instanceOverrides, externalSnapshots, snapshot);
        issues.AddRange(compile.Issues);
        if (!compile.IsSuccess || compile.Profile?.ExecutionContract is null)
        {
            return Result(issues, profile: compile.Profile, registryHash: snapshot.ContentHash);
        }

        var profile = compile.Profile;
        var contract = profile.ExecutionContract;
        var resolution = snapshot.ResolveExact(contract.KernelId, contract.KernelVersion, contract.ContractSchemaId);
        issues.AddRange(resolution.Issues.Select(FromPluginIssue));
        if (!resolution.IsSuccess || resolution.Registration is null)
        {
            return Result(issues, profile, registryHash: snapshot.ContentHash);
        }

        var provider = resolution.Registration.TestScenarioProvider;
        if (provider is null)
        {
            issues.Add(new(
                ComponentExecutionIssueCodes.KernelTestScenarioMissing,
                ComponentTemplateIssueSeverity.Error,
                "$.execution_contract.kernel_id",
                $"Runtime kernel '{contract.KernelId}' has no registered deterministic test scenario provider.",
                contract.KernelId));
            return Result(issues, profile, registryHash: snapshot.ContentHash);
        }

        ComponentKernelTestScenario scenario;
        try
        {
            scenario = provider.CreateScenario(contract, seed);
        }
        catch (Exception exception)
        {
            issues.Add(ScenarioIssue("$.scenario", $"Kernel test scenario provider failed: {exception.Message}", contract.KernelId));
            return Result(issues, profile, registryHash: snapshot.ContentHash);
        }

        issues.AddRange(ValidateScenario(scenario, contract));
        var inputHash = ComputeInputHash(scenario);
        if (HasBlocking(issues))
        {
            return Result(issues, profile, scenario, snapshot.ContentHash, inputHash);
        }

        var library = new ComponentTemplateLibrary();
        library.AddOrReplace(template);
        var graph = BuildScenarioGraph(template, contract, scenario, instanceOverrides);
        var graphCompile = new SimulationGraphCompiler().CompileHardware(
            graph,
            componentRegistry: componentRegistry,
            componentTemplateLibrary: library);
        issues.AddRange(graphCompile.Errors.Select(FromCompilationIssue));
        issues.AddRange(graphCompile.Warnings.Select(FromCompilationIssue));
        if (!graphCompile.IsSuccess || graphCompile.Graph is null)
        {
            return Result(issues, profile, scenario, snapshot.ContentHash, inputHash);
        }

        var compiledGraph = graphCompile.Graph;
        profile = compiledGraph.CompiledComponentProfiles[ComponentId];
        var simulation = new CycleSimulationEngine(snapshot).Run(compiledGraph, new SimulationOptions
        {
            MaxCycles = scenario.MaxCycles,
            DefaultInjectCount = 0,
            DeterministicSeed = scenario.Seed
        });
        var timeline = simulation.Trace.Cycles
            .SelectMany(cycle => cycle.Events
                .Where(traceEvent => string.Equals(traceEvent.ComponentId, ComponentId, StringComparison.Ordinal))
                .Select(traceEvent => new ComponentKernelTestTimelineEvent(cycle.Cycle, traceEvent)))
            .ToList()
            .AsReadOnly();

        if (!simulation.Completed || simulation.Issues.Any(issue => string.Equals(issue.Severity, "error", StringComparison.OrdinalIgnoreCase)))
        {
            issues.Add(new(
                ComponentExecutionIssueCodes.KernelTestRuntimeFailed,
                ComponentTemplateIssueSeverity.Error,
                "$.simulation",
                simulation.Completed
                    ? string.Join("; ", simulation.Issues.Select(issue => $"{issue.Code}:{issue.Message}"))
                    : simulation.CompletionReason,
                contract.KernelId));
        }

        ComponentKernelTestEvaluationResult evaluation;
        try
        {
            evaluation = provider.EvaluateScenario(scenario, new ComponentKernelTestObservation
            {
                Profile = profile,
                Simulation = simulation,
                ComponentEvents = timeline
            });
        }
        catch (Exception exception)
        {
            issues.Add(ScenarioIssue("$.evaluation", $"Kernel test scenario evaluator failed: {exception.Message}", contract.KernelId));
            return Result(issues, profile, scenario, snapshot.ContentHash, inputHash, simulation, timeline);
        }

        issues.AddRange(evaluation.Issues);
        if (!string.Equals(evaluation.ExpectedOutputHash, evaluation.ActualOutputHash, StringComparison.Ordinal) &&
            !issues.Any(issue => issue.Code == ComponentExecutionIssueCodes.KernelTestOutputMismatch))
        {
            issues.Add(new(
                ComponentExecutionIssueCodes.KernelTestOutputMismatch,
                ComponentTemplateIssueSeverity.Error,
                "$.simulation.delivered_packets",
                "Observed component output does not match the independent scenario reference.",
                contract.KernelId));
        }

        return Result(
            issues,
            profile,
            scenario,
            snapshot.ContentHash,
            inputHash,
            simulation,
            timeline,
            evaluation.ExpectedOutputHash,
            evaluation.ActualOutputHash,
            evaluation.Artifacts);
    }

    private static IReadOnlyList<ComponentTemplateIssue> ValidateScenario(ComponentKernelTestScenario scenario, CompiledComponentExecutionContract contract)
    {
        var issues = new List<ComponentTemplateIssue>();
        if (scenario is null)
        {
            return [ScenarioIssue("$.scenario", "Kernel test scenario provider returned null.", contract.KernelId)];
        }
        if (string.IsNullOrWhiteSpace(scenario.ScenarioId)) issues.Add(ScenarioIssue("$.scenario.scenario_id", "Scenario id is required.", contract.KernelId));
        if (scenario.MaxCycles <= 0) issues.Add(ScenarioIssue("$.scenario.max_cycles", "Scenario max cycles must be positive.", contract.KernelId));
        if (scenario.Inputs.Count == 0) issues.Add(ScenarioIssue("$.scenario.inputs", "At least one input transaction is required.", contract.KernelId));
        if (scenario.OutputPortNames.Count == 0) issues.Add(ScenarioIssue("$.scenario.output_port_names", "At least one observed output port is required.", contract.KernelId));

        var transactionIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var input in scenario.Inputs)
        {
            if (string.IsNullOrWhiteSpace(input.TransactionId) || !transactionIds.Add(input.TransactionId))
            {
                issues.Add(ScenarioIssue("$.scenario.inputs", $"Input transaction id '{input.TransactionId}' is missing or duplicated.", input.TransactionId));
            }
            var port = contract.Ports.FirstOrDefault(candidate => candidate.Direction == PortDirection.Input && string.Equals(candidate.Name, input.InputPortName, StringComparison.Ordinal));
            if (port is null) issues.Add(ScenarioIssue("$.scenario.inputs", $"Input port '{input.InputPortName}' is not declared by the compiled contract.", input.InputPortName));
            if (input.InjectionCycle < 0) issues.Add(ScenarioIssue("$.scenario.inputs", "Input injection cycle cannot be negative.", input.TransactionId));
            if (string.IsNullOrWhiteSpace(input.Packet.Id)) issues.Add(ScenarioIssue("$.scenario.inputs", "Input packet id is required.", input.TransactionId));
            if (input.Packet.Bits <= 0) issues.Add(ScenarioIssue("$.scenario.inputs", "Input packet bits must be positive.", input.TransactionId));
        }

        var outputNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var outputPortName in scenario.OutputPortNames)
        {
            if (!outputNames.Add(outputPortName)) issues.Add(ScenarioIssue("$.scenario.output_port_names", $"Output port '{outputPortName}' is duplicated.", outputPortName));
            if (!contract.Ports.Any(port => port.Direction == PortDirection.Output && string.Equals(port.Name, outputPortName, StringComparison.Ordinal)))
            {
                issues.Add(ScenarioIssue("$.scenario.output_port_names", $"Output port '{outputPortName}' is not declared by the compiled contract.", outputPortName));
            }
        }

        try
        {
            _ = ComponentExecutionJson.CanonicalizeJson(scenario.CanonicalInputJson);
            _ = ComponentExecutionJson.CanonicalizeJson(scenario.CanonicalExpectationJson);
        }
        catch (Exception exception)
        {
            issues.Add(ScenarioIssue("$.scenario", $"Scenario canonical JSON is invalid: {exception.Message}", contract.KernelId));
        }
        return issues.AsReadOnly();
    }

    private static HardwareGraph BuildScenarioGraph(
        ComponentTemplate template,
        CompiledComponentExecutionContract contract,
        ComponentKernelTestScenario scenario,
        IReadOnlyDictionary<string, string>? instanceOverrides)
    {
        var graph = new HardwareGraph();
        var component = new HardwareComponent
        {
            Id = ComponentId,
            Name = template.DisplayName,
            Type = ToComponentKind(template.TargetKind),
            Position = new GridPosition(2, 0),
            TemplateRef = new ComponentTemplateInstanceRef
            {
                TemplateId = template.TemplateId,
                Version = template.Version,
                ParameterOverrides = new Dictionary<string, string>(instanceOverrides ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase)
            },
            Ports = template.ExternalPorts.Select(ToHardwarePort).ToList()
        };
        graph.Components.Add(component);

        for (var index = 0; index < scenario.Inputs.Count; index++)
        {
            var input = scenario.Inputs[index];
            var inputPort = contract.Ports.Single(port => port.Direction == PortDirection.Input && string.Equals(port.Name, input.InputPortName, StringComparison.Ordinal));
            var sourceId = $"component_test_source_{index:D3}";
            graph.Components.Add(new HardwareComponent
            {
                Id = sourceId,
                Name = input.TransactionId,
                Type = ComponentKind.WorkloadSource,
                Position = new GridPosition(0, index),
                Ports = [ToSourcePort(inputPort)],
                Parameters = SourceParameters(input)
            });
            graph.Links.Add(new HardwareLink
            {
                Id = $"component_test_input_{index:D3}",
                Source = new PortRef(sourceId, "out"),
                Destination = new PortRef(ComponentId, input.InputPortName),
                LatencyCycles = 1,
                BandwidthBitsPerCycle = ToBandwidthBitsPerCycle(inputPort.BandwidthBitsPerCycle)
            });
        }

        var sink = new HardwareComponent
        {
            Id = SinkId,
            Name = "Component Test Sink",
            Type = ComponentKind.WorkloadSink,
            Position = new GridPosition(4, 0)
        };
        graph.Components.Add(sink);
        for (var index = 0; index < scenario.OutputPortNames.Count; index++)
        {
            var outputName = scenario.OutputPortNames[index];
            var outputPort = contract.Ports.Single(port => port.Direction == PortDirection.Output && string.Equals(port.Name, outputName, StringComparison.Ordinal));
            var sinkPortName = $"in_{index:D3}";
            sink.Ports.Add(ToSinkPort(outputPort, sinkPortName));
            graph.Links.Add(new HardwareLink
            {
                Id = $"component_test_output_{index:D3}",
                Source = new PortRef(ComponentId, outputName),
                Destination = new PortRef(SinkId, sinkPortName),
                LatencyCycles = 1,
                BandwidthBitsPerCycle = ToBandwidthBitsPerCycle(outputPort.BandwidthBitsPerCycle)
            });
        }
        return graph;
    }

    private static HardwarePort ToHardwarePort(TemplateExternalPort port) => new()
    {
        Name = port.Name,
        Direction = port.Direction,
        SignalType = port.SignalType,
        DataType = port.DataType,
        Precision = port.Precision,
        Protocol = port.Protocol,
        BandwidthBitsPerCycle = port.BandwidthBitsPerCycle,
        Required = port.Required,
        MultiConnect = false
    };

    private static HardwarePort ToSourcePort(CompiledComponentPortContract port) => new()
    {
        Name = "out",
        Direction = PortDirection.Output,
        SignalType = port.SignalType,
        DataType = port.DataType,
        Precision = port.Precision,
        Protocol = port.Protocol,
        BandwidthBitsPerCycle = ToBandwidthBitsPerCycle(port.BandwidthBitsPerCycle)
    };

    private static HardwarePort ToSinkPort(CompiledComponentPortContract port, string name) => new()
    {
        Name = name,
        Direction = PortDirection.Input,
        SignalType = port.SignalType,
        DataType = port.DataType,
        Precision = port.Precision,
        Protocol = port.Protocol,
        BandwidthBitsPerCycle = ToBandwidthBitsPerCycle(port.BandwidthBitsPerCycle)
    };

    private static int ToBandwidthBitsPerCycle(double value) =>
        !double.IsFinite(value) || value <= 0
            ? 1
            : value >= int.MaxValue
                ? int.MaxValue
                : Math.Max(1, (int)Math.Ceiling(value));

    private static Dictionary<string, string> SourceParameters(ComponentKernelTestInputTransaction input)
    {
        var packet = input.Packet;
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["packet_count"] = "1",
            ["packet_bits"] = packet.Bits.ToString(CultureInfo.InvariantCulture),
            ["packet_id"] = packet.Id,
            ["packet_type"] = packet.PacketType.ToString(),
            ["packet_num_elements"] = Math.Max(1, packet.NumElements).ToString(CultureInfo.InvariantCulture),
            ["packet_bit_width"] = Math.Max(1, packet.BitWidth).ToString(CultureInfo.InvariantCulture),
            ["packet_precision"] = packet.Precision.ToString(),
            ["payload_values"] = string.Join(",", packet.Values.Select(value => value.ToString("R", CultureInfo.InvariantCulture))),
            ["initial_injection_cycle"] = input.InjectionCycle.ToString(CultureInfo.InvariantCulture),
            ["source_port"] = "out",
            ["queue_capacity"] = "4"
        };
    }

    private static ComponentKind ToComponentKind(ComponentTemplateTargetKind targetKind) => targetKind switch
    {
        ComponentTemplateTargetKind.ProcessingElement => ComponentKind.ProcessingElement,
        ComponentTemplateTargetKind.Router => ComponentKind.Router,
        ComponentTemplateTargetKind.Memory => ComponentKind.Memory,
        ComponentTemplateTargetKind.Buffer => ComponentKind.Buffer,
        ComponentTemplateTargetKind.Link => ComponentKind.LinkEndpoint,
        _ => ComponentKind.Custom
    };

    private static string ComputeInputHash(ComponentKernelTestScenario scenario)
    {
        var canonical = ComponentExecutionJson.CanonicalizeJson(JsonSerializer.Serialize(
            scenario.Inputs.Select(input => new
            {
                input.TransactionId,
                input.InputPortName,
                input.InjectionCycle,
                Packet = input.Packet
            }),
            HardwareGraphJson.Options));
        return ComponentExecutionJson.ComputeSha256(canonical);
    }

    private static ComponentTemplateIssue FromPluginIssue(ComponentPluginIssue issue) => new(
        issue.Code,
        issue.Severity == ValidationSeverity.Error ? ComponentTemplateIssueSeverity.Error : ComponentTemplateIssueSeverity.Warning,
        issue.Location,
        issue.Message,
        issue.RelatedId);

    private static ComponentTemplateIssue FromCompilationIssue(CompilationIssue issue) => new(
        issue.Code,
        issue.Severity == ValidationSeverity.Error ? ComponentTemplateIssueSeverity.Error : ComponentTemplateIssueSeverity.Warning,
        issue.Location,
        issue.Message,
        issue.RelatedId,
        issue.Suggestion);

    private static ComponentTemplateIssue ScenarioIssue(string location, string message, string? relatedId) => new(
        ComponentExecutionIssueCodes.KernelTestScenarioInvalid,
        ComponentTemplateIssueSeverity.Error,
        location,
        message,
        relatedId);

    private static bool HasBlocking(IEnumerable<ComponentTemplateIssue> issues) =>
        issues.Any(issue => issue.Severity is ComponentTemplateIssueSeverity.Error or ComponentTemplateIssueSeverity.Fatal);

    private static ComponentKernelTestRunResult Result(
        IEnumerable<ComponentTemplateIssue> issues,
        CompiledComponentProfile? profile = null,
        ComponentKernelTestScenario? scenario = null,
        string registryHash = "",
        string inputHash = "",
        SimulationResult? simulation = null,
        IReadOnlyList<ComponentKernelTestTimelineEvent>? timeline = null,
        string expectedOutputHash = "",
        string actualOutputHash = "",
        IReadOnlyDictionary<string, string>? artifacts = null) => new()
        {
            Issues = new ReadOnlyCollection<ComponentTemplateIssue>(issues.ToList()),
            Profile = profile,
            Scenario = scenario,
            Simulation = simulation,
            Timeline = timeline ?? [],
            RuntimeKernelRegistryHash = registryHash,
            ProfileHash = profile?.ProfileHash ?? "",
            ExecutionContractHash = profile?.ExecutionContract?.ContractHash ?? "",
            InputHash = inputHash,
            ExpectedOutputHash = expectedOutputHash,
            ActualOutputHash = actualOutputHash,
            TraceHash = simulation?.TraceHash?.Hash ?? "",
            Artifacts = artifacts ?? new ReadOnlyDictionary<string, string>(new Dictionary<string, string>())
        };
}
