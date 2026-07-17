using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;

namespace HardwareSim.Core;

/// <summary>Classifies whether a compiler request produced a simulation graph.</summary>
public enum CompilationStatus
{
    /// <summary>Compilation completed and returned a graph.</summary>
    Success,
    /// <summary>Compilation rejected the input and returned structured errors.</summary>
    Failure
}

/// <summary>Describes a compiler diagnostic without requiring callers to parse exception text.</summary>
/// <param name="Code">Stable machine-readable diagnostic code.</param>
/// <param name="Severity">Diagnostic severity.</param>
/// <param name="Location">JSON-style location of the invalid input.</param>
/// <param name="Message">Human-readable explanation.</param>
/// <param name="RelatedId">Optional component, port, link, model, or macro identifier.</param>
/// <param name="Suggestion">Optional corrective action.</param>
public sealed record CompilationIssue(
    string Code,
    ValidationSeverity Severity,
    string Location,
    string Message,
    string? RelatedId = null,
    string? Suggestion = null);

/// <summary>Returns either a compiled graph or structured diagnostics for an expected input error.</summary>
public sealed class CompilationResult<TGraph> where TGraph : class
{
    private CompilationResult(
        CompilationStatus status,
        TGraph? graph,
        IReadOnlyList<CompilationIssue> errors,
        IReadOnlyList<CompilationIssue> warnings,
        IReadOnlyList<string> notes)
    {
        Status = status;
        Graph = graph;
        Errors = errors;
        Warnings = warnings;
        Notes = notes;
    }

    /// <summary>Gets the overall compilation outcome.</summary>
    public CompilationStatus Status { get; }
    /// <summary>Gets whether compilation produced a graph.</summary>
    public bool IsSuccess => Status == CompilationStatus.Success;
    /// <summary>Gets the compiled graph, or <see langword="null"/> after failure.</summary>
    public TGraph? Graph { get; }
    /// <summary>Gets fatal diagnostics that prevented graph creation.</summary>
    public IReadOnlyList<CompilationIssue> Errors { get; }
    /// <summary>Gets non-fatal diagnostics emitted during compilation.</summary>
    public IReadOnlyList<CompilationIssue> Warnings { get; }
    /// <summary>Gets informational compiler notes such as expansion and graph counts.</summary>
    public IReadOnlyList<string> Notes { get; }

    /// <summary>Creates a successful result containing the compiled graph and optional diagnostics.</summary>
    public static CompilationResult<TGraph> Succeeded(
        TGraph graph,
        IEnumerable<CompilationIssue>? warnings = null,
        IEnumerable<string>? notes = null) =>
        new(
            CompilationStatus.Success,
            graph,
            [],
            (warnings ?? []).ToList(),
            (notes ?? []).ToList());

    /// <summary>Creates a failed result with no graph and one or more structured errors.</summary>
    public static CompilationResult<TGraph> Failed(
        IEnumerable<CompilationIssue> errors,
        IEnumerable<CompilationIssue>? warnings = null,
        IEnumerable<string>? notes = null) =>
        new(
            CompilationStatus.Failure,
            null,
            errors.ToList(),
            (warnings ?? []).ToList(),
            (notes ?? []).ToList());
}

/// <summary>Compiles design-time HardwareGraph 1.0 data into an immutable hardware simulation graph.</summary>
public sealed class SimulationGraphCompiler
{
    /// <summary>Compiles hardware, workload, and mapping data into an executable simulation graph without mutating inputs.</summary>
    public CompilationResult<ExecutableSimulationGraph> CompileExecutable(
        HardwareGraph hardwareGraph,
        WorkloadGraph workload,
        WorkloadMapping mapping,
        DeviceModelRegistry? modelRegistry = null,
        SimulationConfig? simulationConfig = null,
        TraceConfig? traceConfig = null,
        PacketizationMode packetizationMode = PacketizationMode.CoarsePacketMode,
        ComponentTemplateLibrary? componentTemplateLibrary = null)
    {
        if (hardwareGraph is null)
        {
            throw new ArgumentNullException(nameof(hardwareGraph));
        }
        if (workload is null)
        {
            throw new ArgumentNullException(nameof(workload));
        }
        if (mapping is null)
        {
            throw new ArgumentNullException(nameof(mapping));
        }

        var hardwareBefore = HardwareGraphJson.Serialize(hardwareGraph);
        var workloadBefore = workload.ToJson();
        var mappingBefore = mapping.ToJson();
        var errors = new List<CompilationIssue>();
        var notes = new List<string>();

        var workloadValidation = workload.Validate();
        errors.AddRange(workloadValidation.Issues
            .Where(issue => issue.Severity == ValidationSeverity.Error)
            .Select(issue => new CompilationIssue(issue.Code, issue.Severity, issue.Location, issue.Message, issue.RelatedId)));
        var mappingValidation = mapping.Validate(workload, hardwareGraph);
        errors.AddRange(mappingValidation.Issues.Where(issue => issue.Severity == ValidationSeverity.Error));

        var hardwareResult = CompileHardware(
            hardwareGraph,
            modelRegistry,
            simulationConfig,
            traceConfig,
            componentTemplateLibrary: componentTemplateLibrary);
        if (!hardwareResult.IsSuccess)
        {
            errors.AddRange(hardwareResult.Errors);
        }

        if (packetizationMode == PacketizationMode.FlitLevelMode)
        {
            notes.Add("FlitLevelMode emits explicit flits and is validated by the executable runtime before simulation.");
        }

        if (errors.Count > 0 || hardwareResult.Graph is null)
        {
            return CompilationResult<ExecutableSimulationGraph>.Failed(errors, hardwareResult.Warnings, hardwareResult.Notes.Concat(notes));
        }

        var schedule = new WorkloadScheduler().BuildSchedule(workload, mapping, hardwareGraph);
        var tiles = BuildTensorTiles(workload, mapping);
        var messages = BuildInitialMessages(schedule, mapping, tiles);
        var packets = BuildInitialPackets(schedule, mapping, tiles, messages, hardwareResult.Graph, packetizationMode);
        var flits = packetizationMode == PacketizationMode.FlitLevelMode ? BuildInitialFlits(packets, hardwareResult.Graph.SimulationConfig.FlitSizeBits) : new List<Flit>();
        var storageMaps = BuildStorageMapSnapshots(hardwareResult.Graph, tiles, mapping, errors);
        ApplyStorageAllocationsToTiles(tiles, storageMaps);
        if (errors.Count > 0)
        {
            return CompilationResult<ExecutableSimulationGraph>.Failed(errors, hardwareResult.Warnings, hardwareResult.Notes.Concat(notes));
        }
        var executable = new ExecutableSimulationGraph
        {
            HardwareGraph = hardwareResult.Graph,
            Schedule = schedule,
            TensorTiles = tiles.AsReadOnly(),
            InitialMessages = messages.AsReadOnly(),
            InitialPackets = packets.AsReadOnly(),
            InitialFlits = flits.AsReadOnly(),
            Mapping = WorkloadMapping.FromJson(mappingBefore),
            StorageMaps = storageMaps,
            PacketizationMode = packetizationMode,
            TransportSemantics = TransportSemanticsContract.Clone(hardwareResult.Graph.SimulationConfig.TransportSemantics),
            WorkloadMappingProvenance = new WorkloadMappingProvenance
            {
                WorkloadSchemaVersion = workload.SchemaVersion,
                WorkloadHash = ComputeSha256(workloadBefore),
                MappingSchemaVersion = mapping.SchemaVersion,
                MappingHash = ComputeSha256(mappingBefore),
                Note = "workload-aware compile"
            }
        };

        if (!string.Equals(hardwareBefore, HardwareGraphJson.Serialize(hardwareGraph), StringComparison.Ordinal) ||
            !string.Equals(workloadBefore, workload.ToJson(), StringComparison.Ordinal) ||
            !string.Equals(mappingBefore, mapping.ToJson(), StringComparison.Ordinal))
        {
            errors.Add(new CompilationIssue(
                "CompilerInputMutationError",
                ValidationSeverity.Error,
                "$",
                "Workload-aware compiler mutated one or more input graphs."));
            return CompilationResult<ExecutableSimulationGraph>.Failed(errors, hardwareResult.Warnings, hardwareResult.Notes.Concat(notes));
        }

        notes.Add($"Built executable graph with {schedule.Operations.Count} scheduled operation(s), {tiles.Count} tensor tile(s), {messages.Count} message(s), and {packets.Count} packet(s).");
        return CompilationResult<ExecutableSimulationGraph>.Succeeded(executable, hardwareResult.Warnings, hardwareResult.Notes.Concat(notes));
    }
    /// <summary>Expands fully-defined macros, validates the design, binds models, and builds a hardware-only simulation graph.</summary>
    public CompilationResult<HardwareSimulationGraph> CompileHardware(
        HardwareGraph hardwareGraph,
        DeviceModelRegistry? modelRegistry = null,
        SimulationConfig? simulationConfig = null,
        TraceConfig? traceConfig = null,
        ProjectDirtyState? dirtyState = null,
        ComponentTypeRegistry? componentRegistry = null,
        ComponentTemplateLibrary? componentTemplateLibrary = null)
    {
        if (hardwareGraph is null)
        {
            throw new ArgumentNullException(nameof(hardwareGraph));
        }
        var errors = new List<CompilationIssue>();
        var warnings = new List<CompilationIssue>();
        var notes = new List<string>();
        var effectiveSimulationConfig = simulationConfig ?? new SimulationConfig();
        var effectiveComponentRegistry = componentRegistry ?? ComponentTypeRegistry.CreateDefault();

        ValidateSchema(hardwareGraph, errors);
        var expandedGraph = errors.Count == 0
            ? ExpandMacros(hardwareGraph, errors, notes)
            : hardwareGraph;
        ValidateComponentIds(expandedGraph, errors);
        ValidatePortsAndLinks(expandedGraph, errors);
        ValidateModels(expandedGraph, modelRegistry, errors);
        ValidateDependencies(expandedGraph, errors);
        ValidateTransportSemantics(effectiveSimulationConfig.TransportSemantics, errors);
        ValidatePluginComponents(expandedGraph, effectiveComponentRegistry, errors, warnings);

        if (errors.Count > 0)
        {
            dirtyState?.MarkCompilationFailed();
            return CompilationResult<HardwareSimulationGraph>.Failed(errors, warnings, notes);
        }

        var compileGraph = HardwareGraphJson.Deserialize(HardwareGraphJson.Serialize(expandedGraph));
        if (compileGraph.Placement is not null || compileGraph.Routing is not null)
        {
            var physicalReport = PhysicalLinkModel.ApplyToGraph(
                compileGraph,
                compileGraph.Placement,
                compileGraph.Routing,
                LinkModelParameters.FromGraphParameters(compileGraph.Parameters),
                effectiveSimulationConfig.Clock);
            notes.Add(physicalReport.Summary);
            warnings.AddRange(physicalReport.Issues.Select(issue => new CompilationIssue(
                issue.Code,
                ToValidationSeverity(issue.Severity),
                "$.placement",
                issue.Message,
                issue.ComponentId)));
            warnings.AddRange(physicalReport.RoutingWarnings.Select(warning => new CompilationIssue(
                warning.Code,
                ToValidationSeverity(warning.Severity),
                "$.routing.routes",
                $"{warning.Message} Evidence: {warning.Evidence}",
                string.Join(",", warning.LinkIds),
                string.Join(" | ", warning.Suggestions.Select(suggestion =>
                    $"{suggestion.Kind}: {suggestion.Message}; evidence={suggestion.Evidence}")))));
        }

        var physicalModelSnapshots = PhysicalModelCompilerSnapshotBuilder.Build(compileGraph, modelRegistry);
        warnings.AddRange(physicalModelSnapshots.Warnings);
        errors.AddRange(physicalModelSnapshots.Errors);
        if (errors.Count > 0)
        {
            dirtyState?.MarkCompilationFailed();
            return CompilationResult<HardwareSimulationGraph>.Failed(errors, warnings, notes);
        }
        var opticalRouteAnalysis = Phase8OpticalRouteAnalyzer.AnalyzeGraph(compileGraph);
        foreach (var issue in opticalRouteAnalysis.Issues)
        {
            if (issue.Severity == ValidationSeverity.Error)
            {
                errors.Add(issue);
            }
            else
            {
                warnings.Add(issue);
            }
        }
        if (errors.Count > 0)
        {
            dirtyState?.MarkCompilationFailed();
            return CompilationResult<HardwareSimulationGraph>.Failed(errors, warnings, notes);
        }
        if (opticalRouteAnalysis.ProfilesByLinkId.Count > 0)
        {
            notes.Add($"Compiled {opticalRouteAnalysis.ProfilesByLinkId.Count} explicit Phase 8 optical route profile(s).");
        }
        var pluginCompileParameters = BuildPluginCompileParameters(compileGraph, effectiveComponentRegistry, errors, warnings);
        if (errors.Count > 0)
        {
            dirtyState?.MarkCompilationFailed();
            return CompilationResult<HardwareSimulationGraph>.Failed(errors, warnings, notes);
        }
        var pluginTypeIds = BuildPluginTypeIds(compileGraph, effectiveComponentRegistry);
        var kernelRegistryResult = effectiveComponentRegistry.FreezeRuntimeKernels();
        AddPluginIssues(kernelRegistryResult.Issues, errors, warnings, "$.runtime_kernel_registry");
        if (!kernelRegistryResult.IsSuccess || kernelRegistryResult.Snapshot is null)
        {
            dirtyState?.MarkCompilationFailed();
            return CompilationResult<HardwareSimulationGraph>.Failed(errors, warnings, notes);
        }
        var runtimeKernelRegistry = kernelRegistryResult.Snapshot;
        var templateCompileArtifacts = BuildTemplateCompileArtifacts(
            compileGraph,
            componentTemplateLibrary,
            physicalModelSnapshots.ProfileSnapshots,
            runtimeKernelRegistry,
            errors,
            warnings);
        if (errors.Count > 0)
        {
            dirtyState?.MarkCompilationFailed();
            return CompilationResult<HardwareSimulationGraph>.Failed(errors, warnings, notes);
        }
        var ports = compileGraph.Components
            .OrderBy(component => component.Id, StringComparer.Ordinal)
            .SelectMany(component => component.Ports
                .OrderBy(port => port.Name, StringComparer.Ordinal)
                .Select(port => BuildPort(component.Id, port)))
            .ToList();
        var portIds = ports.ToDictionary(
            port => BuildPortKey(port.ComponentId, port.Name),
            port => port.Id,
            StringComparer.OrdinalIgnoreCase);
        var components = compileGraph.Components
            .OrderBy(component => component.Id, StringComparer.Ordinal)
            .Select(component => BuildComponent(
                component,
                ports,
                modelRegistry,
                templateCompileArtifacts.ParametersByComponentId.GetValueOrDefault(component.Id),
                pluginCompileParameters.GetValueOrDefault(component.Id),
                pluginTypeIds.GetValueOrDefault(component.Id),
                templateCompileArtifacts.ProfilesByComponentId.GetValueOrDefault(component.Id)?.ExecutionContract))
            .ToList();
        var links = compileGraph.Links
            .OrderBy(link => link.Id, StringComparer.Ordinal)
            .Select(link => BuildLink(link, portIds, opticalRouteAnalysis.FindProfile(link.Id)))
            .ToList();
        var energyModels = BuildEnergyModels(compileGraph, modelRegistry);
        var latencyModels = BuildLatencyModels(compileGraph, modelRegistry);
        var areaModels = BuildAreaModels(compileGraph, modelRegistry);

        var sourceJson = HardwareGraphJson.Serialize(hardwareGraph);
        var graph = new HardwareSimulationGraph
        {
            Components = components.AsReadOnly(),
            Ports = ports.AsReadOnly(),
            Links = links.AsReadOnly(),
            RoutingTables = new ReadOnlyDictionary<string, RoutingTable>(new Dictionary<string, RoutingTable>()),
            EnergyModels = new ReadOnlyDictionary<string, EnergyModel>(energyModels),
            LatencyModels = new ReadOnlyDictionary<string, LatencyModel>(latencyModels),
            AreaModels = new ReadOnlyDictionary<string, AreaModel>(areaModels),
            ModelBindingSnapshots = physicalModelSnapshots.BindingSnapshots,
            CharacterizedProfiles = physicalModelSnapshots.ProfileSnapshots,
            CompiledComponentProfiles = templateCompileArtifacts.ProfilesByComponentId,
            SimulationConfig = effectiveSimulationConfig,
            TraceConfig = traceConfig ?? new TraceConfig(),
            Provenance = new SimulationGraphProvenance
            {
                SourceSchemaVersion = hardwareGraph.SchemaVersion,
                SourceGraphHash = ComputeSha256(sourceJson),
                CompilerVersion = "1.0",
                ComponentRuntimeKernelRegistryHash = runtimeKernelRegistry.ContentHash
            }
        };

        graph = ApplyPluginRuntimeDescriptors(graph, effectiveComponentRegistry, runtimeKernelRegistry, errors, warnings);
        if (errors.Count > 0)
        {
            dirtyState?.MarkCompilationFailed();
            return CompilationResult<HardwareSimulationGraph>.Failed(errors, warnings, notes);
        }

        notes.Add($"Compiled {components.Count} component(s), {ports.Count} port(s), and {links.Count} link(s).");
        dirtyState?.MarkCompilationSucceeded();
        return CompilationResult<HardwareSimulationGraph>.Succeeded(graph, warnings, notes);
    }

    private static List<TensorTile> BuildTensorTiles(WorkloadGraph workload, WorkloadMapping mapping)
    {
        var consumersByProducer = workload.Ops
            .SelectMany(operation => operation.DependencyIds.Select(dependency => new { Producer = dependency, Consumer = operation.Id }))
            .GroupBy(edge => edge.Producer, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Select(edge => edge.Consumer).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(id => id, StringComparer.Ordinal).ToList(), StringComparer.OrdinalIgnoreCase);
        var placements = mapping.Placements.ToDictionary(placement => placement.TensorId + "\n" + placement.TileId, placement => placement, StringComparer.OrdinalIgnoreCase);
        var tiles = new List<TensorTile>();
        foreach (var operation in workload.TopologicalOrder())
        {
            var globalShape = operation.OutputShape.Dimensions.Count > 0 ? operation.OutputShape.Dimensions : operation.TensorShape;
            var tensorId = $"{operation.Id}_out";
            var tileId = $"{tensorId}_tile_0";
            placements.TryGetValue(tensorId + "\n" + tileId, out var placement);
            tiles.Add(new TensorTile
            {
                TensorId = tensorId,
                TileId = tileId,
                TensorName = tensorId,
                GlobalShape = globalShape.ToList(),
                TileShape = globalShape.ToList(),
                TileOffset = globalShape.Select(_ => 0).ToList(),
                Precision = operation.Precision,
                ProducerOpId = operation.Id,
                ConsumerOpIds = consumersByProducer.TryGetValue(operation.Id, out var consumers) ? consumers : [],
                StorageLocation = placement?.StorageComponentId ?? "",
                StorageId = placement?.StorageComponentId ?? "",
                SizeBits = Math.Max(1, globalShape.Aggregate(1L, (acc, dim) => acc * Math.Max(0, dim)) * (PrecisionModel.TryGetDigitalBitWidth(operation.Precision, out var tileBitWidth) ? tileBitWidth : 0)),
                AddressHint = placement?.AddressHint ?? ""
            });
        }

        return tiles;
    }

    private static List<Message> BuildInitialMessages(WorkloadSchedule schedule, WorkloadMapping mapping, IReadOnlyList<TensorTile> tiles)
    {
        var messages = new List<Message>();
        foreach (var operation in schedule.Operations.OrderBy(operation => operation.StartCycle).ThenBy(operation => operation.OperationId, StringComparer.Ordinal))
        {
            var tile = tiles.First(item => string.Equals(item.ProducerOpId, operation.OperationId, StringComparison.OrdinalIgnoreCase));
            var entry = mapping.EntryFor(operation.OperationId);
            messages.Add(new Message
            {
                Id = $"msg_{operation.OperationId}",
                SourceComponentId = "workload",
                DestinationComponentId = operation.ComponentId,
                WorkloadOpId = operation.OperationId,
                TensorId = tile.TensorId,
                TileId = tile.TileId,
                Metadata =
                {
                    ["start_cycle"] = operation.StartCycle.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["target_port"] = entry?.TargetPort ?? ""
                }
            });
        }

        return messages;
    }


    private static void ApplyStorageAllocationsToTiles(
        IReadOnlyList<TensorTile> tiles,
        IReadOnlyDictionary<string, StorageMapSnapshot> storageMaps)
    {
        var tileById = tiles.ToDictionary(tile => tile.TileId, StringComparer.OrdinalIgnoreCase);
        foreach (var map in storageMaps.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            foreach (var allocation in map.Value.Allocations.OrderBy(item => item.BaseAddressBits).ThenBy(item => item.TileId, StringComparer.Ordinal))
            {
                if (!tileById.TryGetValue(allocation.TileId, out var tile))
                {
                    continue;
                }

                tile.StorageId = map.Key;
                tile.StorageLocation = map.Key;
                tile.BaseAddressBits = allocation.BaseAddressBits;
                tile.SizeBits = allocation.SizeBits;
            }
        }
    }

    private static Dictionary<string, StorageMapSnapshot> BuildStorageMapSnapshots(
        HardwareSimulationGraph hardwareGraph,
        IReadOnlyList<TensorTile> tiles,
        WorkloadMapping mapping,
        List<CompilationIssue> errors)
    {
        var maps = new Dictionary<string, StorageMap>(StringComparer.OrdinalIgnoreCase);
        var placements = mapping.Placements
            .Where(placement => !string.IsNullOrWhiteSpace(placement.StorageComponentId))
            .OrderBy(placement => placement.StorageComponentId, StringComparer.Ordinal)
            .ThenBy(placement => placement.TensorId, StringComparer.Ordinal)
            .ThenBy(placement => placement.TileId, StringComparer.Ordinal)
            .ToList();

        foreach (var placement in placements)
        {
            var storage = hardwareGraph.FindComponent(placement.StorageComponentId);
            if (storage is null)
            {
                errors.Add(new CompilationIssue(
                    "StorageMapError",
                    ValidationSeverity.Error,
                    "$.mapping.placements",
                    $"Storage placement references missing component '{placement.StorageComponentId}'.",
                    placement.StorageComponentId));
                continue;
            }
            if (storage.Type is not (ComponentKind.Memory or ComponentKind.Buffer))
            {
                errors.Add(new CompilationIssue(
                    "StorageMapError",
                    ValidationSeverity.Error,
                    "$.mapping.placements",
                    $"Storage placement component '{storage.Id}' must be Memory or Buffer for executable StorageMap snapshots.",
                    storage.Id));
                continue;
            }

            var tile = tiles.FirstOrDefault(item =>
                (string.IsNullOrWhiteSpace(placement.TileId) || string.Equals(item.TileId, placement.TileId, StringComparison.OrdinalIgnoreCase)) &&
                (string.IsNullOrWhiteSpace(placement.TensorId) || string.Equals(item.TensorId, placement.TensorId, StringComparison.OrdinalIgnoreCase)));
            if (tile is null)
            {
                errors.Add(new CompilationIssue(
                    "StorageMapError",
                    ValidationSeverity.Error,
                    "$.mapping.placements",
                    $"Storage placement for tensor '{placement.TensorId}' tile '{placement.TileId}' did not match a compiled tensor tile.",
                    placement.TensorId));
                continue;
            }

            if (!maps.TryGetValue(storage.Id, out var map))
            {
                maps[storage.Id] = map = new StorageMap(storage.Id, StorageCapacityFor(storage));
            }

            var preferred = ParseAddressHint(tile.AddressHint, storage.Id, tile.TileId, errors);
            if (errors.Count > 0)
            {
                continue;
            }

            var allocation = map.Allocate(tile.TileId, Math.Max(1, tile.TotalBits), ComponentDefaults.MemoryLineSizeBits, preferred);
            if (!allocation.IsSuccess)
            {
                errors.Add(new CompilationIssue(
                    allocation.Code,
                    ValidationSeverity.Error,
                    "$.mapping.placements",
                    allocation.Message,
                    tile.TileId));
            }
        }

        return maps
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .ToDictionary(pair => pair.Key, pair => pair.Value.ToSnapshot(), StringComparer.OrdinalIgnoreCase);
    }

    private static long StorageCapacityFor(SimComponentDef storage) => storage.Type switch
    {
        ComponentKind.Buffer => storage.GetIntParameter("capacity_bits", ComponentDefaults.BufferCapacityBits),
        ComponentKind.Memory => storage.GetIntParameter("capacity_bits", storage.GetIntParameter("memory_capacity_bits", (int)ComponentDefaults.MemoryCapacityBits)),
        _ => ComponentDefaults.MemoryCapacityBits
    };

    private static long? ParseAddressHint(string addressHint, string storageId, string tileId, List<CompilationIssue> errors)
    {
        if (string.IsNullOrWhiteSpace(addressHint))
        {
            return null;
        }
        if (!long.TryParse(addressHint, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var parsed) || parsed < 0)
        {
            errors.Add(new CompilationIssue(
                "StorageCapacityError",
                ValidationSeverity.Error,
                "$.mapping.placements.address_hint",
                $"AddressHint '{addressHint}' for tile '{tileId}' in storage '{storageId}' must be a nonnegative bit address.",
                tileId));
            return null;
        }
        return parsed;
    }

    private static List<Packet> BuildInitialPackets(
        WorkloadSchedule schedule,
        WorkloadMapping mapping,
        IReadOnlyList<TensorTile> tiles,
        IReadOnlyList<Message> messages,
        HardwareSimulationGraph hardwareGraph,
        PacketizationMode packetizationMode)
    {
        var packets = new List<Packet>();
        foreach (var operation in schedule.Operations.OrderBy(operation => operation.StartCycle).ThenBy(operation => operation.OperationId, StringComparer.Ordinal))
        {
            var tile = tiles.First(item => string.Equals(item.ProducerOpId, operation.OperationId, StringComparison.OrdinalIgnoreCase));
            var message = messages.First(item => string.Equals(item.WorkloadOpId, operation.OperationId, StringComparison.OrdinalIgnoreCase));
            var entry = mapping.EntryFor(operation.OperationId);
            var bitWidth = PrecisionModel.TryGetDigitalBitWidth(tile.Precision, out var width) ? width : 0;
            for (var index = 0; index < operation.PacketCount; index++)
            {
                var packetId = $"pkt_{operation.OperationId}_{index:000}";
                var routePath = RoutePathFor(operation.ComponentId, mapping, hardwareGraph);
                packets.Add(new Packet
                {
                    Id = packetId,
                    PacketType = PacketTypeFor(operation.Type),
                    NumElements = bitWidth <= 0 ? 0 : Math.Max(1, operation.PacketBits / bitWidth),
                    BitWidth = bitWidth,
                    Bits = operation.PacketBits,
                    Precision = tile.Precision,
                    SourceComponentId = routePath.FirstOrDefault() ?? "workload",
                    DestinationComponentId = operation.ComponentId,
                    SourcePort = "out",
                    DestinationPort = entry?.TargetPort ?? "",
                    WorkloadOpId = operation.OperationId,
                    TensorId = tile.TensorId,
                    TileId = tile.TileId,
                    RoutePath = routePath,
                    InjectionCycle = operation.StartCycle,
                    CreatedCycle = operation.StartCycle,
                    CurrentComponentId = routePath.FirstOrDefault() ?? "workload",
                    Metadata =
                    {
                        ["message_id"] = message.Id,
                        ["packetization_mode"] = packetizationMode.ToString()
                    }
                });
                message.PacketIds.Add(packetId);
            }
        }

        return packets;
    }

    private static List<Flit> BuildInitialFlits(IReadOnlyList<Packet> packets, int flitSizeBits)
    {
        if (flitSizeBits <= 0)
        {
            throw new InvalidOperationException("SimulationConfig.FlitSizeBits must be positive.");
        }

        var flits = new List<Flit>();
        foreach (var packet in packets.OrderBy(packet => packet.Id, StringComparer.Ordinal))
        {
            flits.AddRange(FlitPacketizer.Packetize(packet, flitSizeBits));
        }

        return flits;
    }

    private static PacketType PacketTypeFor(OpType type) => type switch
    {
        OpType.Attention_QK => PacketType.AttentionScore,
        OpType.Softmax => PacketType.SoftmaxResult,
        OpType.Attention_V => PacketType.Activation,
        _ => PacketType.Activation
    };

    private static List<string> RoutePathFor(string componentId, WorkloadMapping mapping, HardwareSimulationGraph hardwareGraph)
    {
        var hinted = mapping.RouteHints.FirstOrDefault(route => route.PreferredPath.Contains(componentId, StringComparer.OrdinalIgnoreCase));
        if (hinted is not null && hinted.PreferredPath.Count > 0)
        {
            return hinted.PreferredPath.ToList();
        }

        var source = hardwareGraph.Components.FirstOrDefault(component => component.Type == ComponentKind.WorkloadSource)?.Id ?? hardwareGraph.Components.FirstOrDefault()?.Id ?? "workload";
        return string.Equals(source, componentId, StringComparison.OrdinalIgnoreCase) ? [componentId] : [source, componentId];
    }

    private static HardwareGraph ExpandMacros(
        HardwareGraph source,
        List<CompilationIssue> errors,
        List<string> notes)
    {
        var expanded = HardwareGraphJson.Deserialize(HardwareGraphJson.Serialize(source));
        var instances = expanded.Components
            .Where(component => component.Type == ComponentKind.Macro)
            .ToList();
        if (instances.Count == 0)
        {
            return expanded;
        }

        var definitions = expanded.Macros
            .GroupBy(macro => macro.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);

        foreach (var instance in instances)
        {
            if (!TryResolveMacroDefinition(instance, definitions, errors, out var definition))
            {
                continue;
            }

            if (!ValidateMacroDefinition(instance, definition!, expanded, errors))
            {
                continue;
            }

            ExpandMacroInstance(expanded, instance, definition!);
            notes.Add($"Expanded macro instance '{instance.Id}' from definition '{definition!.Id}'.");
        }

        return expanded;
    }

    private static bool TryResolveMacroDefinition(
        HardwareComponent instance,
        IReadOnlyDictionary<string, List<MacroComponent>> definitions,
        List<CompilationIssue> errors,
        out MacroComponent? definition)
    {
        definition = null;
        if (!instance.Parameters.TryGetValue("macro_ref", out var macroRef) || string.IsNullOrWhiteSpace(macroRef))
        {
            errors.Add(new CompilationIssue(
                "MacroExpansionError",
                ValidationSeverity.Error,
                $"$.components[{instance.Id}].parameters.macro_ref",
                $"Macro instance '{instance.Id}' requires a non-empty macro_ref parameter.",
                instance.Id));
            return false;
        }

        if (!definitions.TryGetValue(macroRef, out var matches) || matches.Count == 0)
        {
            errors.Add(new CompilationIssue(
                "MacroNotFoundError",
                ValidationSeverity.Error,
                $"$.components[{instance.Id}].parameters.macro_ref",
                $"Macro instance '{instance.Id}' references missing definition '{macroRef}'.",
                instance.Id));
            return false;
        }

        if (matches.Count > 1)
        {
            errors.Add(new CompilationIssue(
                "MacroExpansionError",
                ValidationSeverity.Error,
                "$.macros",
                $"Macro definition id '{macroRef}' is not unique.",
                macroRef));
            return false;
        }

        definition = matches[0];
        return true;
    }

    private static bool ValidateMacroDefinition(
        HardwareComponent instance,
        MacroComponent definition,
        HardwareGraph graph,
        List<CompilationIssue> errors)
    {
        var initialErrorCount = errors.Count;
        if (!string.Equals(definition.SchemaVersion, MacroComponent.CurrentSchemaVersion, StringComparison.Ordinal))
        {
            AddMacroError(errors, instance, definition, $"Macro schema_version '{definition.SchemaVersion}' is not supported.");
        }

        if (definition.InternalComponents.Count == 0)
        {
            AddMacroError(errors, instance, definition, "The referenced definition has no internal components.");
            return false;
        }

        var internalIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var component in definition.InternalComponents)
        {
            if (string.IsNullOrWhiteSpace(component.Id))
            {
                AddMacroError(errors, instance, definition, "Every internal component requires a non-empty id.");
                continue;
            }

            if (!internalIds.Add(component.Id))
            {
                AddMacroError(errors, instance, definition, $"Internal component id '{component.Id}' is not unique.");
            }

            if (component.Type == ComponentKind.Macro)
            {
                AddMacroError(errors, instance, definition, $"Nested macro component '{component.Id}' is outside the Phase 0 contract.");
            }
        }

        foreach (var link in definition.InternalLinks)
        {
            ValidateMacroEndpoint(instance, definition, link.Source, $"internal link '{link.Id}' source", errors);
            ValidateMacroEndpoint(instance, definition, link.Destination, $"internal link '{link.Id}' destination", errors);
        }

        foreach (var mapping in definition.ExternalPortMappings)
        {
            ValidateMacroEndpoint(instance, definition, mapping.Value, $"external mapping '{mapping.Key}'", errors);
        }

        foreach (var port in instance.Ports)
        {
            var connected = graph.Links.Any(link =>
                Matches(link.Source, instance.Id, port.Name) ||
                Matches(link.Destination, instance.Id, port.Name));
            if (port.Required && !connected)
            {
                AddMacroError(errors, instance, definition, $"Required instance port '{port.Name}' is not connected.");
            }

            if (connected && !definition.ExternalPortMappings.ContainsKey(port.Name))
            {
                AddMacroError(errors, instance, definition, $"Connected instance port '{port.Name}' has no external port mapping.");
            }
        }

        foreach (var endpoint in graph.Links
                     .SelectMany(link => new[] { link.Source, link.Destination })
                     .Where(endpoint => string.Equals(endpoint.ComponentId, instance.Id, StringComparison.OrdinalIgnoreCase)))
        {
            if (instance.FindPort(endpoint.PortName) is null)
            {
                AddMacroError(errors, instance, definition, $"Link endpoint references missing instance port '{endpoint.PortName}'.");
            }
        }

        return errors.Count == initialErrorCount;
    }

    private static void ValidateMacroEndpoint(
        HardwareComponent instance,
        MacroComponent definition,
        PortRef endpoint,
        string role,
        List<CompilationIssue> errors)
    {
        var component = definition.InternalComponents.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, endpoint.ComponentId, StringComparison.OrdinalIgnoreCase));
        if (component?.FindPort(endpoint.PortName) is null)
        {
            AddMacroError(
                errors,
                instance,
                definition,
                $"The {role} references missing internal port '{endpoint.ComponentId}.{endpoint.PortName}'.");
        }
    }

    private static void AddMacroError(
        List<CompilationIssue> errors,
        HardwareComponent instance,
        MacroComponent definition,
        string message) =>
        errors.Add(new CompilationIssue(
            "MacroExpansionError",
            ValidationSeverity.Error,
            $"$.macros[{definition.Id}]",
            $"Macro instance '{instance.Id}' cannot expand definition '{definition.Id}': {message}",
            instance.Id));

    private static void ExpandMacroInstance(
        HardwareGraph graph,
        HardwareComponent instance,
        MacroComponent definition)
    {
        var instanceDefinition = HardwareGraphJson.Deserialize(
            HardwareGraphJson.Serialize(new HardwareGraph { Macros = [definition] })).Macros.Single();
        var prefix = DeterministicMacroPrefix(graph, instance, instanceDefinition);
        foreach (var link in graph.Links)
        {
            link.Source = RewriteMacroEndpoint(link.Source, instance, instanceDefinition, prefix);
            link.Destination = RewriteMacroEndpoint(link.Destination, instance, instanceDefinition, prefix);
        }

        foreach (var component in instanceDefinition.InternalComponents)
        {
            component.Id = prefix + component.Id;
            graph.Components.Add(component);
        }

        foreach (var link in instanceDefinition.InternalLinks)
        {
            link.Id = prefix + link.Id;
            link.Source = new PortRef(prefix + link.Source.ComponentId, link.Source.PortName);
            link.Destination = new PortRef(prefix + link.Destination.ComponentId, link.Destination.PortName);
            graph.Links.Add(link);
        }

        graph.Components.Remove(instance);
    }

    private static string DeterministicMacroPrefix(
        HardwareGraph graph,
        HardwareComponent instance,
        MacroComponent definition)
    {
        var componentIds = graph.Components
            .Where(component => !string.Equals(component.Id, instance.Id, StringComparison.OrdinalIgnoreCase))
            .Select(component => component.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var linkIds = graph.Links.Select(link => link.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var attempt = 0;
        while (true)
        {
            var prefix = attempt == 0 ? $"{instance.Id}::" : $"{instance.Id}::{attempt}::";
            var componentConflict = definition.InternalComponents.Any(component => componentIds.Contains(prefix + component.Id));
            var linkConflict = definition.InternalLinks.Any(link => linkIds.Contains(prefix + link.Id));
            if (!componentConflict && !linkConflict)
            {
                return prefix;
            }

            attempt++;
        }
    }

    private static PortRef RewriteMacroEndpoint(
        PortRef endpoint,
        HardwareComponent instance,
        MacroComponent definition,
        string prefix)
    {
        if (!string.Equals(endpoint.ComponentId, instance.Id, StringComparison.OrdinalIgnoreCase))
        {
            return endpoint;
        }

        var internalEndpoint = definition.ExternalPortMappings[endpoint.PortName];
        return new PortRef(prefix + internalEndpoint.ComponentId, internalEndpoint.PortName);
    }

    private static void ValidateTransportSemantics(TransportSemanticsSnapshot semantics, List<CompilationIssue> errors)
    {
        foreach (var issue in TransportSemanticsContract.Validate(semantics).Issues.Where(issue => issue.Severity == ValidationSeverity.Error))
        {
            errors.Add(new CompilationIssue(issue.Code, issue.Severity, issue.Location, issue.Message));
        }
    }

    private static void ValidateSchema(HardwareGraph graph, List<CompilationIssue> errors)
    {
        if (!string.Equals(graph.SchemaVersion, HardwareGraph.CurrentSchemaVersion, StringComparison.Ordinal))
        {
            errors.Add(new CompilationIssue(
                "UnsupportedSchemaVersion",
                ValidationSeverity.Error,
                "$.schema_version",
                $"Compiler requires HardwareGraph schema {HardwareGraph.CurrentSchemaVersion}, but received '{graph.SchemaVersion}'.",
                graph.SchemaVersion));
        }
    }

    private static void ValidateComponentIds(HardwareGraph graph, List<CompilationIssue> errors)
    {
        foreach (var component in graph.Components.Where(component => string.IsNullOrWhiteSpace(component.Id)))
        {
            errors.Add(new CompilationIssue(
                "MissingComponentId",
                ValidationSeverity.Error,
                "$.components",
                "Every component requires a non-empty id."));
        }

        foreach (var duplicate in graph.Components
                     .Where(component => !string.IsNullOrWhiteSpace(component.Id))
                     .GroupBy(component => component.Id, StringComparer.OrdinalIgnoreCase)
                     .Where(group => group.Count() > 1))
        {
            errors.Add(new CompilationIssue(
                "DuplicateComponentId",
                ValidationSeverity.Error,
                "$.components",
                $"Component id '{duplicate.Key}' is not unique.",
                duplicate.Key));
        }
    }

    private static void ValidatePortsAndLinks(HardwareGraph graph, List<CompilationIssue> errors)
    {
        foreach (var component in graph.Components)
        {
            foreach (var duplicate in component.Ports
                         .GroupBy(port => port.Name, StringComparer.OrdinalIgnoreCase)
                         .Where(group => group.Count() > 1))
            {
                errors.Add(new CompilationIssue(
                    "DuplicatePortId",
                    ValidationSeverity.Error,
                    $"$.components[{component.Id}].ports",
                    $"Port name '{duplicate.Key}' is not unique on component '{component.Id}'.",
                    BuildPortKey(component.Id, duplicate.Key)));
            }

            foreach (var port in component.Ports.Where(port => port.Required))
            {
                var connected = graph.Links.Any(link =>
                    Matches(link.Source, component.Id, port.Name) ||
                    Matches(link.Destination, component.Id, port.Name));
                if (!connected)
                {
                    errors.Add(new CompilationIssue(
                        "PortConnectionError",
                        ValidationSeverity.Error,
                        $"$.components[{component.Id}].ports[{port.Name}]",
                        $"Required port '{component.Id}.{port.Name}' is not connected.",
                        BuildPortKey(component.Id, port.Name),
                        "Connect the required port or mark it optional."));
                }
            }
        }

        foreach (var link in graph.Links)
        {
            ValidateEndpoint(graph, link, link.Source, isSource: true, errors);
            ValidateEndpoint(graph, link, link.Destination, isSource: false, errors);
        }
    }

    private static void ValidateEndpoint(
        HardwareGraph graph,
        HardwareLink link,
        PortRef endpoint,
        bool isSource,
        List<CompilationIssue> errors)
    {
        var component = graph.FindComponent(endpoint.ComponentId);
        var port = component?.FindPort(endpoint.PortName);
        if (port is null)
        {
            errors.Add(new CompilationIssue(
                "PortConnectionError",
                ValidationSeverity.Error,
                $"$.links[{link.Id}].{(isSource ? "source" : "destination")}",
                $"Link '{link.Id}' references missing port '{endpoint.ComponentId}.{endpoint.PortName}'.",
                link.Id));
            return;
        }

        var validDirection = isSource
            ? port.Direction is PortDirection.Output or PortDirection.Bidirectional
            : port.Direction is PortDirection.Input or PortDirection.Bidirectional;
        if (!validDirection)
        {
            errors.Add(new CompilationIssue(
                "PortConnectionError",
                ValidationSeverity.Error,
                $"$.links[{link.Id}].{(isSource ? "source" : "destination")}",
                $"Port '{endpoint.ComponentId}.{endpoint.PortName}' has direction '{port.Direction}' and cannot be used as a link {(isSource ? "source" : "destination") }.",
                link.Id));
        }
    }

    private static void ValidateModels(
        HardwareGraph graph,
        DeviceModelRegistry? modelRegistry,
        List<CompilationIssue> errors)
    {
        foreach (var component in graph.Components.Where(component => !string.IsNullOrWhiteSpace(component.ModelRef)))
        {
            if (modelRegistry?.Find(component.ModelRef) is null && modelRegistry?.FindPhysical(component.ModelRef) is null)
            {
                errors.Add(new CompilationIssue(
                    "ModelNotFoundError",
                    ValidationSeverity.Error,
                    $"$.components[{component.Id}].model_ref",
                    $"Component '{component.Id}' references model '{component.ModelRef}', but that model is not registered.",
                    component.Id));
            }
        }

        foreach (var link in graph.Links.Where(link => !string.IsNullOrWhiteSpace(link.ModelRef)))
        {
            if (modelRegistry?.Find(link.ModelRef) is null && modelRegistry?.FindPhysical(link.ModelRef) is null)
            {
                errors.Add(new CompilationIssue(
                    "ModelNotFoundError",
                    ValidationSeverity.Error,
                    $"$.links[{link.Id}].model_ref",
                    $"Link '{link.Id}' references model '{link.ModelRef}', but that model is not registered.",
                    link.Id));
            }
        }
    }

    private static void ValidateDependencies(HardwareGraph graph, List<CompilationIssue> errors)
    {
        var adjacency = graph.Components.ToDictionary(
            component => component.Id,
            _ => new List<string>(),
            StringComparer.OrdinalIgnoreCase);
        foreach (var link in graph.Links)
        {
            // Stateful capability output becomes visible through a queued current/next
            // boundary. It remains executable, but is not a combinational dependency.
            if (IsExplicitStatefulCapabilityReinjection(link))
            {
                continue;
            }
            // A typed topology transport edge is a packet route, not a combinational
            // component dependency. Opposite activation/partial-sum trees therefore
            // form a legal stateful network cycle while ordinary graph cycles remain errors.
            if (IsExplicitTopologyTransport(graph, link))
            {
                continue;
            }
            if (adjacency.TryGetValue(link.Source.ComponentId, out var destinations) &&
                adjacency.ContainsKey(link.Destination.ComponentId))
            {
                destinations.Add(link.Destination.ComponentId);
            }
        }

        var states = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var stack = new List<string>();
        foreach (var componentId in adjacency.Keys.OrderBy(id => id, StringComparer.Ordinal))
        {
            if (FindCycle(componentId, adjacency, states, stack, out var cycle))
            {
                errors.Add(new CompilationIssue(
                    "CyclicDependencyError",
                    ValidationSeverity.Error,
                    "$.links",
                    $"Component dependency cycle detected: {string.Join(" -> ", cycle)}.",
                    cycle[0],
                    "Break the directed cycle or represent feedback through an explicit stateful component contract."));
                return;
            }
        }
    }

    private static bool IsExplicitStatefulCapabilityReinjection(HardwareLink link) =>
        string.Equals(
            link.Parameters.GetValueOrDefault(HardwareDependencyScopes.Parameter, ""),
            HardwareDependencyScopes.StatefulCapabilityReinjectionV1,
            StringComparison.Ordinal);
    private static bool IsExplicitTopologyTransport(HardwareGraph graph, HardwareLink link)
    {
        if (!string.Equals(
                graph.Parameters.GetValueOrDefault("physical_route_contract", ""),
                "explicit-per-logical-link",
                StringComparison.Ordinal))
        {
            return false;
        }

        var role = link.Parameters.GetValueOrDefault("topology_role", "");
        var supportedRole =
            string.Equals(role, TopologyPresetLinkRole.ActivationDistribution.ToString(), StringComparison.Ordinal) ||
            string.Equals(role, TopologyPresetLinkRole.PartialSumReturn.ToString(), StringComparison.Ordinal) ||
            string.Equals(role, TopologyPresetLinkRole.MeshTransport.ToString(), StringComparison.Ordinal);
        return supportedRole &&
               !string.IsNullOrWhiteSpace(link.Parameters.GetValueOrDefault("physical_route_id", ""));
    }

    private static bool FindCycle(
        string componentId,
        IReadOnlyDictionary<string, List<string>> adjacency,
        Dictionary<string, int> states,
        List<string> stack,
        out IReadOnlyList<string> cycle)
    {
        if (states.TryGetValue(componentId, out var state))
        {
            if (state == 1)
            {
                var start = stack.FindIndex(id => string.Equals(id, componentId, StringComparison.OrdinalIgnoreCase));
                cycle = stack.Skip(start).Append(componentId).ToList();
                return true;
            }

            cycle = [];
            return false;
        }

        states[componentId] = 1;
        stack.Add(componentId);
        foreach (var destination in adjacency[componentId].OrderBy(id => id, StringComparer.Ordinal))
        {
            if (FindCycle(destination, adjacency, states, stack, out cycle))
            {
                return true;
            }
        }

        stack.RemoveAt(stack.Count - 1);
        states[componentId] = 2;
        cycle = [];
        return false;
    }

    private static void ValidatePluginComponents(
        HardwareGraph graph,
        ComponentTypeRegistry componentRegistry,
        List<CompilationIssue> errors,
        List<CompilationIssue> warnings)
    {
        foreach (var component in graph.Components.Where(ShouldUsePluginPath).OrderBy(component => component.Id, StringComparer.Ordinal))
        {
            var typeId = ResolvePluginTypeId(component);
            var plugin = componentRegistry.GetPlugin(typeId);
            if (plugin is null)
            {
                errors.Add(new CompilationIssue(
                    "PluginNotRegistered",
                    ValidationSeverity.Error,
                    $"$.components[{component.Id}].type_id",
                    $"Component '{component.Id}' references plugin type id '{typeId}', but that plugin is not registered.",
                    component.Id));
                continue;
            }

            try
            {
                AddPluginIssues(
                    plugin.ValidationProvider.Validate(new ComponentValidationContext(plugin, component, graph)),
                    errors,
                    warnings,
                    $"$.components[{component.Id}]");
            }
            catch (Exception exception)
            {
                errors.Add(new CompilationIssue(
                    "PluginValidationProviderError",
                    ValidationSeverity.Error,
                    $"$.components[{component.Id}].type_id",
                    $"Plugin validation provider for '{typeId}' failed: {exception.Message}",
                    component.Id));
            }
        }
    }

    private sealed class TemplateCompileArtifacts
    {
        public TemplateCompileArtifacts(
            IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> parametersByComponentId,
            IReadOnlyDictionary<string, CompiledComponentProfile> profilesByComponentId)
        {
            ParametersByComponentId = parametersByComponentId;
            ProfilesByComponentId = profilesByComponentId;
        }

        public IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> ParametersByComponentId { get; }
        public IReadOnlyDictionary<string, CompiledComponentProfile> ProfilesByComponentId { get; }
    }

    private static TemplateCompileArtifacts BuildTemplateCompileArtifacts(
        HardwareGraph graph,
        ComponentTemplateLibrary? componentTemplateLibrary,
        IReadOnlyDictionary<string, CharacterizedProfileSnapshot> characterizedProfileSnapshots,
        ComponentRuntimeKernelRegistrySnapshot runtimeKernelRegistry,
        List<CompilationIssue> errors,
        List<CompilationIssue> warnings)
    {
        var parameterSets = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        var profiles = new Dictionary<string, CompiledComponentProfile>(StringComparer.OrdinalIgnoreCase);
        var compiler = new ComponentTemplateCompiler();

        foreach (var component in graph.Components.Where(component => component.TemplateRef is not null).OrderBy(component => component.Id, StringComparer.Ordinal))
        {
            var reference = component.TemplateRef!;
            if (string.IsNullOrWhiteSpace(reference.TemplateId) || string.IsNullOrWhiteSpace(reference.Version))
            {
                errors.Add(new CompilationIssue(
                    "TemplateReferenceIncomplete",
                    ValidationSeverity.Error,
                    $"$.components[{component.Id}].template_ref",
                    "ComponentTemplate reference requires both template_id and version.",
                    component.Id));
                continue;
            }

            if (componentTemplateLibrary is null)
            {
                errors.Add(new CompilationIssue(
                    "TemplateLibraryMissing",
                    ValidationSeverity.Error,
                    $"$.components[{component.Id}].template_ref",
                    "ComponentTemplate reference cannot be compiled without a ComponentTemplateLibrary.",
                    component.Id));
                continue;
            }

            var template = componentTemplateLibrary.Find(reference.TemplateId, reference.Version);
            if (template is null)
            {
                errors.Add(new CompilationIssue(
                    "TemplateNotFound",
                    ValidationSeverity.Error,
                    $"$.components[{component.Id}].template_ref",
                    $"ComponentTemplate {reference.TemplateId} version {reference.Version} was not found in the supplied library.",
                    component.Id));
                continue;
            }

            if (template.Lifecycle is ComponentTemplateLifecycleState.Draft or ComponentTemplateLifecycleState.Validated or ComponentTemplateLifecycleState.BrokenDependency)
            {
                errors.Add(new CompilationIssue(
                    "TemplateLifecycleNotRuntimeReady",
                    ValidationSeverity.Error,
                    $"$.components[{component.Id}].template_ref",
                    $"ComponentTemplate {template.TemplateId} version {template.Version} is {template.Lifecycle}; Draft, Validated, and BrokenDependency templates cannot enter SimulationGraph.",
                    component.Id));
                continue;
            }

            if (template.Lifecycle == ComponentTemplateLifecycleState.Deprecated)
            {
                warnings.Add(new CompilationIssue(
                    "TemplateDeprecated",
                    ValidationSeverity.Warning,
                    $"$.components[{component.Id}].template_ref",
                    $"ComponentTemplate {template.TemplateId} version {template.Version} is Deprecated.",
                    component.Id));
            }

            var componentErrorCount = errors.Count;
            ValidateTemplateInstancePorts(component, template, errors);
            if (errors.Count > componentErrorCount)
            {
                continue;
            }

            var compile = compiler.Compile(template, reference.ParameterOverrides, characterizedProfileSnapshots, runtimeKernelRegistry);
            AddTemplateCompileIssues(compile.Issues, component.Id, errors, warnings);
            if (!compile.IsSuccess || compile.Profile is null)
            {
                continue;
            }

            reference.CompiledProfileHash = compile.Profile.ProfileHash;
            profiles[component.Id] = compile.Profile;
            parameterSets[component.Id] = new ReadOnlyDictionary<string, string>(BuildTemplateRuntimeParameters(compile.Profile));
        }

        return new TemplateCompileArtifacts(
            new ReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>(parameterSets),
            new ReadOnlyDictionary<string, CompiledComponentProfile>(profiles));
    }

    private static void ValidateTemplateInstancePorts(
        HardwareComponent component,
        ComponentTemplate template,
        List<CompilationIssue> errors)
    {
        var ports = component.Ports.ToDictionary(port => port.Name, StringComparer.OrdinalIgnoreCase);
        foreach (var externalPort in template.ExternalPorts.OrderBy(port => port.Name, StringComparer.Ordinal))
        {
            if (!ports.TryGetValue(externalPort.Name, out var shellPort))
            {
                if (externalPort.Required)
                {
                    errors.Add(new CompilationIssue(
                        "TemplateExternalPortMissing",
                        ValidationSeverity.Error,
                        $"$.components[{component.Id}].ports",
                        $"Component {component.Id} is missing required template external port {externalPort.Name}.",
                        component.Id));
                }

                continue;
            }

            if (shellPort.Direction != externalPort.Direction)
            {
                errors.Add(new CompilationIssue(
                    "TemplateExternalPortDirectionMismatch",
                    ValidationSeverity.Error,
                    $"$.components[{component.Id}].ports[{externalPort.Name}]",
                    $"Component port {externalPort.Name} direction {shellPort.Direction} does not match template direction {externalPort.Direction}.",
                    component.Id));
            }

            if (shellPort.SignalType != externalPort.SignalType || shellPort.DataType != externalPort.DataType)
            {
                errors.Add(new CompilationIssue(
                    "TemplateExternalPortTypeMismatch",
                    ValidationSeverity.Error,
                    $"$.components[{component.Id}].ports[{externalPort.Name}]",
                    $"Component port {externalPort.Name} type does not match template external port contract.",
                    component.Id));
            }

            if (externalPort.Precision != PrecisionKind.Any && shellPort.Precision != externalPort.Precision)
            {
                errors.Add(new CompilationIssue(
                    "TemplateExternalPortPrecisionMismatch",
                    ValidationSeverity.Error,
                    $"$.components[{component.Id}].ports[{externalPort.Name}]",
                    $"Component port {externalPort.Name} precision {shellPort.Precision} does not match template precision {externalPort.Precision}.",
                    component.Id));
            }
        }
    }

    private static void AddTemplateCompileIssues(
        IEnumerable<ComponentTemplateIssue> templateIssues,
        string componentId,
        List<CompilationIssue> errors,
        List<CompilationIssue> warnings)
    {
        foreach (var issue in templateIssues)
        {
            var severity = ToValidationSeverity(issue.Severity);
            var suffix = issue.Location.TrimStart("$".ToCharArray());
            var compilationIssue = new CompilationIssue(
                issue.Code,
                severity,
                $"$.components[{componentId}].template_ref{suffix}",
                issue.Message,
                componentId,
                issue.Suggestion);
            if (severity == ValidationSeverity.Error)
            {
                errors.Add(compilationIssue);
            }
            else
            {
                warnings.Add(compilationIssue);
            }
        }
    }

    private static Dictionary<string, string> BuildTemplateRuntimeParameters(CompiledComponentProfile profile)
    {
        var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [ComponentTemplateRuntimeKeys.TemplateId] = profile.TemplateId,
            [ComponentTemplateRuntimeKeys.TemplateVersion] = profile.TemplateVersion,
            [ComponentTemplateRuntimeKeys.CompiledProfileHash] = profile.ProfileHash,
            [ComponentTemplateRuntimeKeys.OperationLatency] = profile.OperationLatency.ToString(System.Globalization.CultureInfo.InvariantCulture),
            [ComponentTemplateRuntimeKeys.PipelineLatency] = profile.PipelineLatency.ToString(System.Globalization.CultureInfo.InvariantCulture),
            [ComponentTemplateRuntimeKeys.IssueInterval] = profile.IssueInterval.ToString(System.Globalization.CultureInfo.InvariantCulture),
            [ComponentTemplateRuntimeKeys.InputQueueDepth] = profile.InputQueueDepth.ToString(System.Globalization.CultureInfo.InvariantCulture),
            [ComponentTemplateRuntimeKeys.OutputQueueDepth] = profile.OutputQueueDepth.ToString(System.Globalization.CultureInfo.InvariantCulture),
            [ComponentTemplateRuntimeKeys.DefaultResponseTargetPolicy] = profile.DefaultResponseTargetPolicy.ToString(),
            [ComponentTemplateRuntimeKeys.TraceDescriptors] = string.Join(",", profile.TraceDescriptors.OrderBy(descriptor => descriptor, StringComparer.Ordinal)),
            [ComponentTemplateRuntimeKeys.InternalDrilldownStages] = string.Join(",", profile.InternalDrilldownStages.OrderBy(stage => stage, StringComparer.Ordinal)),
            [ComponentTemplateRuntimeKeys.EnergyTotalPicojoules] = profile.TotalEnergyPicojoules.ToString(System.Globalization.CultureInfo.InvariantCulture),
            [ComponentTemplateRuntimeKeys.EnergyBreakdownPicojoules] = FormatTemplateRuntimeBreakdown(profile.EnergyPicojoules),
            [ComponentTemplateRuntimeKeys.AreaTotalUm2] = profile.TotalAreaUm2.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["compute_latency_cycles"] = profile.OperationLatency.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["queue_capacity"] = Math.Max(profile.InputQueueDepth, profile.OutputQueueDepth).ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["pipeline_latency_cycles"] = profile.PipelineLatency.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["issue_interval_cycles"] = profile.IssueInterval.ToString(System.Globalization.CultureInfo.InvariantCulture)
        };

        if (profile.PhysicalFootprint is { } footprint)
        {
            parameters["physical_footprint_hash"] = footprint.FootprintHash;
            parameters["physical_footprint_scope"] = footprint.Scope.ToString();
            parameters["physical_footprint_source_kind"] = footprint.SourceKind.ToString();
            parameters["physical_footprint_evidence_status"] = footprint.EvidenceStatus.ToString();
            parameters["physical_footprint_uncertainty"] = footprint.Uncertainty;
            if (footprint.IsKnown)
            {
                parameters["physical_width_um"] = footprint.WidthUm!.Value.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
                parameters["physical_height_um"] = footprint.HeightUm!.Value.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
                parameters["area_um2"] = footprint.AreaUm2!.Value.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
            }
        }
        if (profile.ExecutionContract is not null)
        {
            parameters[ComponentTemplateRuntimeKeys.ExecutionKernelId] = profile.ExecutionContract.KernelId;
            parameters[ComponentTemplateRuntimeKeys.ExecutionKernelVersion] = profile.ExecutionContract.KernelVersion;
            parameters[ComponentTemplateRuntimeKeys.ExecutionContractSchemaId] = profile.ExecutionContract.ContractSchemaId;
            parameters[ComponentTemplateRuntimeKeys.ExecutionContractHash] = profile.ExecutionContract.ContractHash;
            parameters[ComponentTemplateRuntimeKeys.RuntimeKernelRegistryHash] = profile.ExecutionContract.Provenance.RegistrySnapshotHash;
        }

        if (profile.ShapeContract.TryGetValue("result", out var resultShape))
        {
            if (resultShape.Contains("fp8", StringComparison.OrdinalIgnoreCase))
            {
                parameters["output_precision"] = PrecisionKind.FP8_E4M3.ToString();
            }
            else if (resultShape.Contains("fp16", StringComparison.OrdinalIgnoreCase))
            {
                parameters["output_precision"] = PrecisionKind.FP16.ToString();
            }
            else if (resultShape.Contains("fp32", StringComparison.OrdinalIgnoreCase))
            {
                parameters["output_precision"] = PrecisionKind.FP32.ToString();
            }
        }

        return parameters;
    }

    private static string FormatTemplateRuntimeBreakdown(IReadOnlyDictionary<string, double> values) =>
        string.Join("|", values
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => $"{pair.Key}:{pair.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)}"));

    private static ValidationSeverity ToValidationSeverity(ComponentTemplateIssueSeverity severity) => severity switch
    {
        ComponentTemplateIssueSeverity.Error or ComponentTemplateIssueSeverity.Fatal => ValidationSeverity.Error,
        ComponentTemplateIssueSeverity.Info => ValidationSeverity.Info,
        _ => ValidationSeverity.Warning
    };

    private static IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> BuildPluginCompileParameters(
        HardwareGraph graph,
        ComponentTypeRegistry componentRegistry,
        List<CompilationIssue> errors,
        List<CompilationIssue> warnings)
    {
        var result = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var component in graph.Components.Where(ShouldUsePluginPath).OrderBy(component => component.Id, StringComparer.Ordinal))
        {
            var typeId = ResolvePluginTypeId(component);
            var plugin = componentRegistry.GetPlugin(typeId);
            if (plugin is null)
            {
                continue;
            }

            try
            {
                var compile = plugin.CompileProvider.Compile(new ComponentCompileContext(plugin, component, graph));
                AddPluginIssues(compile.Issues, errors, warnings, $"$.components[{component.Id}].compileProvider");
                if (compile.IsSuccess)
                {
                    result[component.Id] = new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(compile.Parameters, StringComparer.OrdinalIgnoreCase));
                }
            }
            catch (Exception exception)
            {
                errors.Add(new CompilationIssue(
                    "PluginCompileProviderError",
                    ValidationSeverity.Error,
                    $"$.components[{component.Id}].type_id",
                    $"Plugin compile provider for '{typeId}' failed: {exception.Message}",
                    component.Id));
            }
        }

        return result;
    }

    private static IReadOnlyDictionary<string, string> BuildPluginTypeIds(HardwareGraph graph, ComponentTypeRegistry componentRegistry)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var component in graph.Components.Where(ShouldUsePluginPath).OrderBy(component => component.Id, StringComparer.Ordinal))
        {
            var typeId = ResolvePluginTypeId(component);
            var plugin = componentRegistry.GetPlugin(typeId);
            if (plugin is not null)
            {
                result[component.Id] = plugin.TypeId;
            }
        }

        return result;
    }

    private static bool ShouldUsePluginPath(HardwareComponent component) =>
        !string.IsNullOrWhiteSpace(component.TypeId) || ComponentTypeIds.IsFirstPartyExtensionKind(component.Type);

    private static string ResolvePluginTypeId(HardwareComponent component) => !string.IsNullOrWhiteSpace(component.TypeId)
        ? ComponentTypeIds.Normalize(component.TypeId)
        : ComponentTypeIds.BuiltIn(component.Type);

    private static HardwareSimulationGraph ApplyPluginRuntimeDescriptors(
        HardwareSimulationGraph graph,
        ComponentTypeRegistry componentRegistry,
        ComponentRuntimeKernelRegistrySnapshot runtimeKernelRegistry,
        List<CompilationIssue> errors,
        List<CompilationIssue> warnings)
    {
        _ = warnings;
        var components = new List<SimComponentDef>();
        foreach (var component in graph.Components.OrderBy(component => component.Id, StringComparer.Ordinal))
        {
            if (string.IsNullOrWhiteSpace(component.TypeId))
            {
                components.Add(component);
                continue;
            }

            var typeId = ComponentTypeIds.Normalize(component.TypeId);
            var plugin = componentRegistry.GetPlugin(typeId);
            if (plugin is null)
            {
                components.Add(component);
                continue;
            }

            try
            {
                var runtime = plugin.SimulationRuntimeFactory.CreateRuntime(new ComponentRuntimeFactoryContext(plugin, component, graph));
                var parameters = new Dictionary<string, string>(component.Parameters, StringComparer.OrdinalIgnoreCase)
                {
                    [ComponentPluginRuntimeKeys.TypeId] = plugin.TypeId,
                    [ComponentPluginRuntimeKeys.ProcessingLatencyCycles] = Math.Max(0, runtime.ProcessingLatencyCycles).ToString(System.Globalization.CultureInfo.InvariantCulture),
                    [ComponentPluginRuntimeKeys.EnergyCategory] = runtime.EnergyCategory.ToString(),
                    [ComponentPluginRuntimeKeys.TraceDescriptors] = string.Join(",", runtime.TraceDescriptors.Select(descriptor => descriptor.Name).OrderBy(name => name, StringComparer.Ordinal)),
                    [ComponentPluginRuntimeKeys.MetricDescriptors] = string.Join(",", runtime.MetricDescriptors.Select(descriptor => descriptor.Name).OrderBy(name => name, StringComparer.Ordinal))
                };
                foreach (var pair in runtime.Parameters)
                {
                    parameters[pair.Key] = pair.Value;
                }

                var executionContract = component.ExecutionContract;
                var legacyCompatibility = parameters.TryGetValue(ComponentPluginRuntimeKeys.LegacyRuntimeCompatibility, out var compatibility) &&
                    bool.TryParse(compatibility, out var useLegacyCompatibility) &&
                    useLegacyCompatibility;
                if (executionContract is null && !string.IsNullOrWhiteSpace(runtime.KernelId) && !legacyCompatibility)
                {
                    executionContract = BuildDirectPluginExecutionContract(component, plugin, runtime, parameters, runtimeKernelRegistry);
                }
                components.Add(CopyComponentWithParameters(component, parameters, executionContract));
            }
            catch (Exception exception)
            {
                errors.Add(new CompilationIssue(
                    "PluginRuntimeFactoryError",
                    ValidationSeverity.Error,
                    $"$.components[{component.Id}].type_id",
                    $"Plugin runtime factory for '{typeId}' failed: {exception.Message}",
                    component.Id));
                components.Add(component);
            }
        }

        return new HardwareSimulationGraph
        {
            SchemaVersion = graph.SchemaVersion,
            Components = components.AsReadOnly(),
            Ports = graph.Ports,
            Links = graph.Links,
            RoutingTables = graph.RoutingTables,
            EnergyModels = graph.EnergyModels,
            LatencyModels = graph.LatencyModels,
            AreaModels = graph.AreaModels,
            ModelBindingSnapshots = graph.ModelBindingSnapshots,
            CharacterizedProfiles = graph.CharacterizedProfiles,
            CompiledComponentProfiles = graph.CompiledComponentProfiles,
            SimulationConfig = graph.SimulationConfig,
            TraceConfig = graph.TraceConfig,
            Provenance = graph.Provenance
        };
    }

    private static CompiledComponentExecutionContract BuildDirectPluginExecutionContract(
        SimComponentDef component,
        ComponentPluginDescriptor plugin,
        ComponentSimulationRuntimeDescriptor runtime,
        IReadOnlyDictionary<string, string> parameters,
        ComponentRuntimeKernelRegistrySnapshot runtimeKernelRegistry)
    {
        if (string.IsNullOrWhiteSpace(runtime.KernelVersion) ||
            string.IsNullOrWhiteSpace(runtime.ContractSchemaId) ||
            string.IsNullOrWhiteSpace(runtime.KernelImplementationHash))
        {
            throw new InvalidOperationException(
                $"Plugin '{plugin.TypeId}' runtime kernel identity must include exact version, contract schema id, and implementation hash.");
        }

        var resolution = runtimeKernelRegistry.ResolveExact(runtime.KernelId, runtime.KernelVersion, runtime.ContractSchemaId);
        if (!resolution.IsSuccess || resolution.Registration is null)
        {
            var details = string.Join("; ", resolution.Issues.Select(issue => $"{issue.Code}: {issue.Message}"));
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(details)
                    ? $"Runtime kernel '{runtime.KernelId}' could not be resolved exactly."
                    : details);
        }

        var descriptor = resolution.Registration.Descriptor;
        if (!string.Equals(descriptor.ImplementationHash, runtime.KernelImplementationHash, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Runtime kernel '{runtime.KernelId}' implementation hash '{runtime.KernelImplementationHash}' does not match registered hash '{descriptor.ImplementationHash}'.");
        }
        if (!string.Equals(resolution.Registration.PluginTypeId, plugin.TypeId, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Runtime kernel '{runtime.KernelId}' is registered by '{resolution.Registration.PluginTypeId}', not '{plugin.TypeId}'.");
        }
        if (descriptor.SupportedOperationKinds.Count > 0 &&
            !descriptor.SupportedOperationKinds.Contains(plugin.TypeId, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Runtime kernel '{runtime.KernelId}' does not declare operation kind '{plugin.TypeId}'.");
        }

        var contract = new CompiledComponentExecutionContract
        {
            KernelId = descriptor.KernelId,
            KernelVersion = descriptor.KernelVersion,
            ContractSchemaId = descriptor.ContractSchemaId,
            OperationKind = plugin.TypeId,
            Ports = plugin.Ports
                .OrderBy(port => port.Name, StringComparer.Ordinal)
                .Select(port => new CompiledComponentPortContract
                {
                    Name = port.Name,
                    Direction = port.Direction,
                    SignalType = port.SignalType,
                    DataType = port.DataType,
                    Precision = port.Precision,
                    Protocol = port.Protocol,
                    SemanticRole = string.IsNullOrWhiteSpace(port.Quantity) ? port.Name : port.Quantity,
                    Required = port.Required,
                    MultiConnect = port.MultiConnect,
                    BandwidthBitsPerCycle = port.BandwidthBitsPerCycle,
                    LatencyCycles = port.LatencyCycles
                })
                .ToList(),
            Timing = new CompiledComponentTimingContract
            {
                OperationLatencyCycles = Math.Max(0, runtime.ProcessingLatencyCycles),
                PipelineLatencyCycles = 0,
                IssueIntervalCycles = PositiveInt(parameters, "issue_interval_cycles", 1),
                FixedServiceLatencyCycles = Math.Max(0, runtime.ProcessingLatencyCycles),
                RuntimeDependentStallAllowed = true
            },
            Queues = new CompiledComponentQueueContract
            {
                InputDepth = PositiveInt(parameters, "input_queue_depth", 4),
                OutputDepth = PositiveInt(parameters, "output_queue_depth", 4)
            },
            Resources = plugin.Parameters
                .Where(parameter => !string.IsNullOrWhiteSpace(parameter.Units))
                .OrderBy(parameter => parameter.Name, StringComparer.Ordinal)
                .Select(parameter =>
                {
                    var value = parameters.GetValueOrDefault(parameter.Name, parameter.DefaultValue);
                    return new CompiledComponentResourceEntry
                    {
                        Name = parameter.Name,
                        ResourceKind = "plugin_parameter",
                        Units = parameter.Units,
                        CanonicalValue = value,
                        ValueType = InvariantValueType(value)
                    };
                })
                .ToList(),
            KernelConfiguration = CanonicalComponentKernelConfiguration.Create(
                runtime.ContractSchemaId,
                runtime.CanonicalKernelConfiguration),
            TraceDescriptors = runtime.TraceDescriptors
                .OrderBy(descriptor => descriptor.Name, StringComparer.Ordinal)
                .ToList(),
            MetricDescriptors = runtime.MetricDescriptors
                .OrderBy(descriptor => descriptor.Name, StringComparer.Ordinal)
                .ToList(),
            Provenance = new CompiledComponentExecutionProvenance
            {
                KernelImplementationHash = descriptor.ImplementationHash,
                RegistrySnapshotHash = runtimeKernelRegistry.ContentHash,
                SyntheticProfileOnly = true,
                FunctionalIdealOnly = true
            }
        };
        contract.RefreshContractHash();
        return contract;
    }

    private static int PositiveInt(IReadOnlyDictionary<string, string> parameters, string key, int fallback) =>
        parameters.TryGetValue(key, out var value) &&
        int.TryParse(value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var parsed) &&
        parsed > 0
            ? parsed
            : fallback;

    private static string InvariantValueType(string value) =>
        bool.TryParse(value, out _) ? "boolean" :
        long.TryParse(value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out _) ? "integer" :
        double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _) ? "number" :
        "string";

    private static SimComponentDef CopyComponentWithParameters(
        SimComponentDef component,
        IReadOnlyDictionary<string, string> parameters,
        CompiledComponentExecutionContract? executionContract = null) => new()
    {
        Id = component.Id,
        Name = component.Name,
        Type = component.Type,
        TypeId = component.TypeId,
        Position = component.Position,
        PortIds = component.PortIds,
        Parameters = ReadOnlyCopy(parameters),
        ModelRef = component.ModelRef,
        LatencyModelId = component.LatencyModelId,
        EnergyModelId = component.EnergyModelId,
        AreaModelId = component.AreaModelId,
        ExecutionContract = executionContract ?? component.ExecutionContract
    };

    private static void AddPluginIssues(
        IEnumerable<ComponentPluginIssue> pluginIssues,
        List<CompilationIssue> errors,
        List<CompilationIssue> warnings,
        string locationPrefix)
    {
        foreach (var issue in pluginIssues)
        {
            var compilationIssue = new CompilationIssue(
                issue.Code,
                issue.Severity,
                string.IsNullOrWhiteSpace(issue.Location) ? locationPrefix : $"{locationPrefix}.{issue.Location.TrimStart('$', '.')}",
                issue.Message,
                issue.RelatedId);
            if (issue.Severity == ValidationSeverity.Error)
            {
                errors.Add(compilationIssue);
            }
            else
            {
                warnings.Add(compilationIssue);
            }
        }
    }

    private static SimPortDef BuildPort(string componentId, HardwarePort port) => new()
    {
        Id = BuildPortKey(componentId, port.Name),
        ComponentId = componentId,
        Name = port.Name,
        Direction = port.Direction,
        SignalType = port.SignalType,
        DataType = port.DataType,
        Precision = port.Precision,
        Protocol = port.Protocol,
        BandwidthBitsPerCycle = port.BandwidthBitsPerCycle,
        LatencyCycles = port.LatencyCycles,
        ClockDomain = port.ClockDomain,
        Required = port.Required,
        MultiConnect = port.MultiConnect
    };

    private static SimComponentDef BuildComponent(
        HardwareComponent component,
        IReadOnlyList<SimPortDef> ports,
        DeviceModelRegistry? modelRegistry,
        IReadOnlyDictionary<string, string>? templateCompileParameters,
        IReadOnlyDictionary<string, string>? pluginCompileParameters,
        string? pluginTypeId,
        CompiledComponentExecutionContract? executionContract)
    {
        var importedModel = modelRegistry?.Find(component.ModelRef);
        var parameters = new Dictionary<string, string>(component.Parameters, StringComparer.OrdinalIgnoreCase);
        if (templateCompileParameters is not null)
        {
            foreach (var pair in templateCompileParameters)
            {
                parameters[pair.Key] = pair.Value;
            }
        }

        if (pluginCompileParameters is not null)
        {
            foreach (var pair in pluginCompileParameters)
            {
                parameters[pair.Key] = pair.Value;
            }
        }

        return new SimComponentDef
        {
            Id = component.Id,
            Name = component.Name,
            Type = component.Type,
            TypeId = pluginTypeId ?? component.TypeId ?? "",
            Position = component.Position,
            PortIds = ports
                .Where(port => string.Equals(port.ComponentId, component.Id, StringComparison.OrdinalIgnoreCase))
                .Select(port => port.Id)
                .ToList()
                .AsReadOnly(),
            Parameters = ReadOnlyCopy(parameters),
            ModelRef = component.ModelRef,
            LatencyModelId = component.LatencyModel ??
                (importedModel?.ModelType == ImportedModelType.Latency ? importedModel.Id : null),
            EnergyModelId = component.EnergyModel ??
                (importedModel?.ModelType == ImportedModelType.Energy ? importedModel.Id : null),
            AreaModelId = component.AreaModel ??
                (importedModel?.ModelType == ImportedModelType.Area ? importedModel.Id : null),
            ExecutionContract = executionContract
        };
    }


    private static SimLinkDef BuildLink(
        HardwareLink link,
        IReadOnlyDictionary<string, string> portIds,
        OpticalLinkRuntimeProfile? opticalProfile) => new()
    {
        Id = link.Id,
        SourcePortId = portIds[BuildPortKey(link.Source.ComponentId, link.Source.PortName)],
        DestinationPortId = portIds[BuildPortKey(link.Destination.ComponentId, link.Destination.PortName)],
        Source = link.Source,
        Destination = link.Destination,
        ModelRef = link.ModelRef,
        BandwidthBitsPerCycle = link.BandwidthBitsPerCycle,
        LatencyCycles = link.LatencyCycles,
        EnergyPerBitPJ = link.EnergyPerBit,
        PhysicalLengthUm = link.PhysicalLength,
        RouteType = link.RouteType,
        OpticalRoute = opticalProfile?.Route,
        OpticalProfile = opticalProfile,
        Parameters = ReadOnlyCopy(link.Parameters)
    };

    private static Dictionary<string, EnergyModel> BuildEnergyModels(
        HardwareGraph graph,
        DeviceModelRegistry? modelRegistry)
    {
        var result = new Dictionary<string, EnergyModel>(StringComparer.OrdinalIgnoreCase);
        foreach (var component in graph.Components.Where(component => !string.IsNullOrWhiteSpace(component.EnergyModel)))
        {
            result[component.Id] = new EnergyModel { Id = component.Id, Source = component.EnergyModel! };
        }

        foreach (var component in graph.Components.Where(component => !string.IsNullOrWhiteSpace(component.ModelRef)))
        {
            var model = modelRegistry?.Find(component.ModelRef);
            if (model?.ModelType != ImportedModelType.Energy)
            {
                continue;
            }

            result[model.Id] = new EnergyModel
            {
                Id = model.Id,
                EnergyPerOperationPJ = ReadModelValue(model, "energy_pj", "energy_per_operation_pj"),
                EnergyPerBitPJ = ReadModelValue(model, "energy_per_bit", "energy_per_bit_pj"),
                Source = model.Metadata.GetValueOrDefault("source", model.Id)
            };
        }

        foreach (var link in graph.Links)
        {
            result[link.Id] = new EnergyModel
            {
                Id = link.Id,
                EnergyPerBitPJ = link.EnergyPerBit,
                Source = modelRegistry?.Find(link.ModelRef)?.Id ?? "hardware_graph"
            };
        }

        return result;
    }

    private static Dictionary<string, LatencyModel> BuildLatencyModels(
        HardwareGraph graph,
        DeviceModelRegistry? modelRegistry)
    {
        var result = new Dictionary<string, LatencyModel>(StringComparer.OrdinalIgnoreCase);
        foreach (var component in graph.Components.Where(component => !string.IsNullOrWhiteSpace(component.LatencyModel)))
        {
            result[component.Id] = new LatencyModel { Id = component.Id, Source = component.LatencyModel! };
        }

        foreach (var component in graph.Components.Where(component => !string.IsNullOrWhiteSpace(component.ModelRef)))
        {
            var model = modelRegistry?.Find(component.ModelRef);
            if (model?.ModelType != ImportedModelType.Latency)
            {
                continue;
            }

            result[model.Id] = new LatencyModel
            {
                Id = model.Id,
                LatencyCycles = checked((int)Math.Ceiling(ReadModelValue(model, "latency_cycles"))),
                Source = model.Metadata.GetValueOrDefault("source", model.Id)
            };
        }

        foreach (var link in graph.Links)
        {
            result[link.Id] = new LatencyModel
            {
                Id = link.Id,
                LatencyCycles = link.LatencyCycles,
                Source = modelRegistry?.Find(link.ModelRef)?.Id ?? "hardware_graph"
            };
        }

        return result;
    }

    private static Dictionary<string, AreaModel> BuildAreaModels(
        HardwareGraph graph,
        DeviceModelRegistry? modelRegistry)
    {
        var result = new Dictionary<string, AreaModel>(StringComparer.OrdinalIgnoreCase);
        foreach (var component in graph.Components.Where(component => !string.IsNullOrWhiteSpace(component.AreaModel)))
        {
            result[component.Id] = new AreaModel
            {
                Id = component.Id,
                Source = modelRegistry?.Find(component.ModelRef)?.Id ?? component.AreaModel!
            };
        }

        foreach (var component in graph.Components.Where(component => !string.IsNullOrWhiteSpace(component.ModelRef)))
        {
            var model = modelRegistry?.Find(component.ModelRef);
            if (model?.ModelType != ImportedModelType.Area)
            {
                continue;
            }

            var areaUm2 = model.Values.TryGetValue("area_um2", out var directArea)
                ? directArea
                : ReadModelValue(model, "area_mm2") * 1_000_000.0;
            result[model.Id] = new AreaModel
            {
                Id = model.Id,
                AreaUm2 = areaUm2,
                Source = model.Metadata.GetValueOrDefault("source", model.Id)
            };
        }

        return result;
    }

    private static ReadOnlyDictionary<string, string> ReadOnlyCopy(IReadOnlyDictionary<string, string> source) =>
        new(new Dictionary<string, string>(source, StringComparer.OrdinalIgnoreCase));

    private static double ReadModelValue(ImportedDeviceModel model, params string[] names)
    {
        foreach (var name in names)
        {
            if (model.Values.TryGetValue(name, out var value))
            {
                return value;
            }
        }

        return 0;
    }

    private static bool Matches(PortRef endpoint, string componentId, string portName) =>
        string.Equals(endpoint.ComponentId, componentId, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(endpoint.PortName, portName, StringComparison.OrdinalIgnoreCase);

    private static string BuildPortKey(string componentId, string portName) => $"{componentId}.{portName}";

    private static ValidationSeverity ToValidationSeverity(string severity) =>
        severity.Equals("error", StringComparison.OrdinalIgnoreCase)
            ? ValidationSeverity.Error
            : severity.Equals("info", StringComparison.OrdinalIgnoreCase)
                ? ValidationSeverity.Info
                : ValidationSeverity.Warning;

    private static string ComputeSha256(string value)
    {
        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(value));
        var builder = new StringBuilder(hash.Length * 2);
        foreach (var item in hash)
        {
            builder.Append(item.ToString("x2", System.Globalization.CultureInfo.InvariantCulture));
        }

        return builder.ToString();
    }
}
