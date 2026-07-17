using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;

namespace HardwareSim.Core;

/// <summary>Defines stable typed resource kinds consumed by Phase 8A capability discovery.</summary>
public static class Phase8ACapabilityResourceKinds
{
    /// <summary>Declares one complete mapping-visible storage capability as canonical JSON.</summary>
    public const string MappingStorageCapability = "mapping.storage-capability.v1";
}

/// <summary>Defines stable shape keys derived from semantic execution-contract ports.</summary>
public static class Phase8ACapabilityShapeKeys
{
    /// <summary>Maximum MatMul M extent accepted by one operation issue.</summary>
    public const string MatMulMaximumM = "matmul.maximum_m";
    /// <summary>Maximum MatMul K extent accepted by one operation issue.</summary>
    public const string MatMulMaximumK = "matmul.maximum_k";
    /// <summary>Maximum MatMul N extent accepted by one operation issue.</summary>
    public const string MatMulMaximumN = "matmul.maximum_n";
}

/// <summary>Provides stable capability-discovery issue codes.</summary>
public static class Phase8ACapabilityIssueCodes
{
    /// <summary>A required frozen input hash was absent.</summary>
    public const string MissingFrozenHash = "CapabilityFrozenHashMissing";
    /// <summary>The supplied runtime registry hash differs from the compiled graph.</summary>
    public const string RuntimeRegistryHashMismatch = "CapabilityRuntimeRegistryHashMismatch";
    /// <summary>A compiled profile lacks its exact semantic identity.</summary>
    public const string ProfileIdentityMissing = "CapabilityProfileIdentityMissing";
    /// <summary>A mapping-visible component lacks an execution contract.</summary>
    public const string ExecutionContractMissing = "CapabilityExecutionContractMissing";
    /// <summary>Profile and component execution contracts disagree.</summary>
    public const string ExecutionContractMismatch = "CapabilityExecutionContractMismatch";
    /// <summary>An execution contract is incomplete or not frozen.</summary>
    public const string ExecutionContractInvalid = "CapabilityExecutionContractInvalid";
    /// <summary>A compiled component lacks an explicit stable type id.</summary>
    public const string StableTypeIdMissing = "CapabilityStableTypeIdMissing";
    /// <summary>A declared execution port is absent or differs in the compiled graph.</summary>
    public const string PortContractMismatch = "CapabilityPortContractMismatch";
    /// <summary>A typed storage capability resource is malformed.</summary>
    public const string StorageContractInvalid = "CapabilityStorageContractInvalid";
    /// <summary>A weight-accepting component does not declare mapping-visible storage.</summary>
    public const string WeightStorageContractMissing = "CapabilityWeightStorageContractMissing";
    /// <summary>No mapping-visible compiled capability was discovered.</summary>
    public const string EmptyDiscovery = "CapabilityDiscoveryEmpty";
    /// <summary>Capability arithmetic exceeded the supported 64-bit range.</summary>
    public const string ArithmeticOverflow = "CapabilityArithmeticOverflow";
}

/// <summary>Freezes the hashes and already-compiled hardware graph used for capability discovery.</summary>
public sealed class Phase8ACapabilityDiscoveryRequest
{
    /// <summary>Creates a discovery request after macro expansion and template/profile compilation.</summary>
    public Phase8ACapabilityDiscoveryRequest(
        HardwareSimulationGraph graph,
        string pluginRegistryHash,
        string runtimeKernelRegistryHash,
        string topologyGraphHash,
        string placementHash,
        string routeHash)
    {
        Graph = graph ?? throw new ArgumentNullException(nameof(graph));
        PluginRegistryHash = pluginRegistryHash?.Trim() ?? "";
        RuntimeKernelRegistryHash = runtimeKernelRegistryHash?.Trim() ?? "";
        TopologyGraphHash = topologyGraphHash?.Trim() ?? "";
        PlacementHash = placementHash?.Trim() ?? "";
        RouteHash = routeHash?.Trim() ?? "";
    }

    /// <summary>Gets the completed hardware compilation output.</summary>
    public HardwareSimulationGraph Graph { get; }
    /// <summary>Gets the frozen component/plugin registry hash.</summary>
    public string PluginRegistryHash { get; }
    /// <summary>Gets the frozen runtime-kernel registry hash.</summary>
    public string RuntimeKernelRegistryHash { get; }
    /// <summary>Gets the frozen topology graph hash.</summary>
    public string TopologyGraphHash { get; }
    /// <summary>Gets the frozen placement hash.</summary>
    public string PlacementHash { get; }
    /// <summary>Gets the frozen route-set hash.</summary>
    public string RouteHash { get; }
}

/// <summary>Returns an immutable Mapping 2.0 capability snapshot or structured hard failures.</summary>
public sealed class Phase8ACapabilityDiscoveryResult
{
    internal Phase8ACapabilityDiscoveryResult(CapabilitySnapshot? snapshot, IEnumerable<WorkloadMappingV2Issue> issues)
    {
        Snapshot = snapshot;
        Issues = new ReadOnlyCollection<WorkloadMappingV2Issue>(issues.ToList());
    }

    /// <summary>Gets the frozen snapshot when discovery completed without errors.</summary>
    public CapabilitySnapshot? Snapshot { get; }
    /// <summary>Gets deterministic structured diagnostics.</summary>
    public IReadOnlyList<WorkloadMappingV2Issue> Issues { get; }
    /// <summary>Gets whether a usable snapshot was produced.</summary>
    public bool IsSuccess => Snapshot is not null && Issues.All(issue => issue.Severity != ValidationSeverity.Error);
}

/// <summary>Discovers mapping capabilities only from compiled profiles, execution contracts, ports, and resources.</summary>
public static class Phase8ACapabilityDiscovery
{
    private const string StorageRoleWeight = "weight";
    private const string RoleActivation = "activation";
    private const string RoleResult = "result";

    private sealed record DiscoveredCapabilityPorts(
        IReadOnlyList<CapabilityPortSnapshot> Snapshots,
        IReadOnlyDictionary<string, CapabilityPortSnapshot> ByDeclaredName);

    /// <summary>Builds an immutable Mapping 2.0 capability snapshot without inspecting component enum or display identity.</summary>
    public static Phase8ACapabilityDiscoveryResult Discover(Phase8ACapabilityDiscoveryRequest request)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));
        var issues = new List<WorkloadMappingV2Issue>();
        RequireHash(request.PluginRegistryHash, "$.pluginRegistryHash", issues);
        RequireHash(request.RuntimeKernelRegistryHash, "$.runtimeKernelRegistryHash", issues);
        RequireHash(request.TopologyGraphHash, "$.topologyGraphHash", issues);
        RequireHash(request.PlacementHash, "$.placementHash", issues);
        RequireHash(request.RouteHash, "$.routeHash", issues);

        var graph = request.Graph;
        if (!string.Equals(graph.Provenance.ComponentRuntimeKernelRegistryHash, request.RuntimeKernelRegistryHash, StringComparison.Ordinal))
        {
            issues.Add(Error(
                Phase8ACapabilityIssueCodes.RuntimeRegistryHashMismatch,
                "$.runtimeKernelRegistryHash",
                "The supplied runtime-kernel registry hash does not match the completed hardware compilation snapshot."));
        }

        var snapshots = new List<ComponentCapabilitySnapshot>();
        foreach (var component in graph.Components.OrderBy(item => item.Id, StringComparer.Ordinal))
        {
            graph.CompiledComponentProfiles.TryGetValue(component.Id, out var profile);
            var componentContract = component.ExecutionContract;
            var profileContract = profile?.ExecutionContract;
            if (profile is null && componentContract is null) continue;

            if (string.IsNullOrWhiteSpace(component.TypeId))
            {
                issues.Add(Error(Phase8ACapabilityIssueCodes.StableTypeIdMissing,
                    $"$.components[{component.Id}].typeId",
                    "Mapping-visible compiled components require an explicit stable type id; enum and display-name fallbacks are forbidden.", component.Id));
                continue;
            }

            if (profile is not null && (string.IsNullOrWhiteSpace(profile.TemplateId) || string.IsNullOrWhiteSpace(profile.ProfileHash)))
            {
                issues.Add(Error(Phase8ACapabilityIssueCodes.ProfileIdentityMissing,
                    $"$.compiledComponentProfiles[{component.Id}]",
                    "A compiled profile must carry exact template and profile identities.", component.Id));
                continue;
            }

            var contract = componentContract ?? profileContract;
            if (contract is null)
            {
                issues.Add(Error(Phase8ACapabilityIssueCodes.ExecutionContractMissing,
                    $"$.components[{component.Id}].executionContract",
                    "A compiled profile cannot be exposed to topology-aware mapping without an execution contract.", component.Id));
                continue;
            }

            if ((componentContract is not null && !ValidContractIdentity(componentContract)) ||
                (profileContract is not null && !ValidContractIdentity(profileContract)))
            {
                issues.Add(Error(Phase8ACapabilityIssueCodes.ExecutionContractInvalid,
                    $"$.components[{component.Id}].executionContract",
                    "Execution contract identity, provenance, and self hash must exactly match its semantic content.", component.Id));
                continue;
            }
            if (componentContract is not null && profileContract is not null &&
                !string.Equals(componentContract.ContractHash, profileContract.ContractHash, StringComparison.Ordinal))
            {
                issues.Add(Error(Phase8ACapabilityIssueCodes.ExecutionContractMismatch,
                    $"$.components[{component.Id}].executionContract",
                    "Component and compiled-profile execution contract hashes differ.", component.Id));
                continue;
            }
            if (!string.Equals(contract.Provenance.RegistrySnapshotHash, request.RuntimeKernelRegistryHash, StringComparison.Ordinal))
            {
                issues.Add(Error(Phase8ACapabilityIssueCodes.ExecutionContractInvalid,
                    $"$.components[{component.Id}].executionContract",
                    "Execution contract frozen registry provenance does not match the requested runtime registry.", component.Id));
                continue;
            }

            var issueCount = issues.Count;
            var discoveredPorts = DiscoverPorts(graph, component, contract, issues);
            var ports = discoveredPorts.Snapshots;
            var shapeContracts = DiscoverShapes(profile, contract, component.Id, issues);
            var storage = DiscoverStorage(contract, discoveredPorts.ByDeclaredName, component.Id, issues);
            if (contract.Ports.Any(port => port.Direction == PortDirection.Input && string.Equals(port.SemanticRole, StorageRoleWeight, StringComparison.Ordinal)) && storage.Count == 0)
            {
                issues.Add(Error(Phase8ACapabilityIssueCodes.WeightStorageContractMissing,
                    $"$.components[{component.Id}].executionContract.resources",
                    "A component with a semantic weight input must declare a complete mapping.storage-capability.v1 resource.", component.Id));
            }
            if (issues.Skip(issueCount).Any(issue => issue.Severity == ValidationSeverity.Error)) continue;

            try
            {
                var operationKinds = (profile?.SupportedOperations ?? [])
                    .Append(contract.OperationKind)
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(value => value, StringComparer.Ordinal)
                    .ToList();
                var precisions = contract.Ports
                    .Where(port => port.Precision != PrecisionKind.Any)
                    .Select(port => port.Precision.ToString())
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(value => value, StringComparer.Ordinal)
                    .ToList();
                var capacity = checked(storage.Sum(item => item.CapacityBits));
                var bandwidth = ports.Count == 0 ? 0 : ports.Max(port => port.BandwidthBitsPerCycle);
                var domain = ports.Select(port => port.DomainId).Distinct(StringComparer.Ordinal).ToList();
                snapshots.Add(new ComponentCapabilitySnapshot(
                    component.Id,
                    component.TypeId,
                    profile?.TemplateId ?? "direct-execution",
                    profile?.Provenance.GetValueOrDefault("semantic_hash", "") ?? "",
                    profile is null ? contract.ContractSchemaId : $"{profile.TemplateId}@{profile.TemplateVersion}",
                    profile?.ProfileHash ?? contract.ContractHash,
                    contract.KernelId,
                    contract.Provenance.KernelImplementationHash,
                    operationKinds,
                    shapeContracts,
                    precisions,
                    capacity,
                    contract.Timing.OperationLatencyCycles,
                    bandwidth,
                    ports,
                    domain.Count == 1 ? domain[0] : "mixed",
                    storage,
                    dynamicEnergyPicojoules: profile?.EnergyPicojoules.Count > 0 ? profile.TotalEnergyPicojoules : null,
                    footprintAreaUm2: profile?.PhysicalFootprint?.AreaUm2,
                    footprintWidthUm: profile?.PhysicalFootprint?.WidthUm,
                    footprintHeightUm: profile?.PhysicalFootprint?.HeightUm,
                    physicalFootprintHash: profile?.PhysicalFootprint?.FootprintHash ?? "",
                    physicalFootprintScope: profile?.PhysicalFootprint?.Scope.ToString() ?? "",
                    physicalFootprintSourceKind: profile?.PhysicalFootprint?.SourceKind.ToString() ?? "",
                    physicalFootprintUncertainty: profile?.PhysicalFootprint?.Uncertainty ?? "",
                    deviceProfileHashes: contract.Provenance.ProfileSnapshotHashes));
            }
            catch (OverflowException)
            {
                issues.Add(Error(Phase8ACapabilityIssueCodes.ArithmeticOverflow,
                    $"$.components[{component.Id}]", "Capability capacity arithmetic exceeded Int64.", component.Id));
            }
        }

        foreach (var duplicatePortId in snapshots.SelectMany(snapshot => snapshot.Ports)
                     .GroupBy(port => port.PortId, StringComparer.Ordinal)
                     .Where(group => group.Count() > 1)
                     .Select(group => group.Key)
                     .OrderBy(value => value, StringComparer.Ordinal))
        {
            issues.Add(Error(
                Phase8ACapabilityIssueCodes.PortContractMismatch,
                $"$.components.ports[id={duplicatePortId}]",
                "Compiled capability PortIds must be globally unique across the frozen snapshot.",
                duplicatePortId));
        }
        if (snapshots.Count == 0)
        {
            issues.Add(Error(Phase8ACapabilityIssueCodes.EmptyDiscovery, "$.components",
                "No mapping-visible capability could be discovered from compiled contracts."));
        }
        if (issues.Any(issue => issue.Severity == ValidationSeverity.Error))
            return new Phase8ACapabilityDiscoveryResult(null, issues);

        var registryHash = Hash(new
        {
            algorithm = "sha256/phase8a-registry-freeze/v1",
            plugin = request.PluginRegistryHash,
            runtime = request.RuntimeKernelRegistryHash
        });
        var snapshotId = Hash(new
        {
            algorithm = "sha256/phase8a-capability-snapshot/v1",
            compiledGraph = graph.Provenance.SourceGraphHash,
            topology = request.TopologyGraphHash,
            placement = request.PlacementHash,
            route = request.RouteHash,
            registry = registryHash,
            components = snapshots.OrderBy(item => item.ComponentId, StringComparer.Ordinal)
        });
        return new Phase8ACapabilityDiscoveryResult(
            new CapabilitySnapshot(snapshotId, request.TopologyGraphHash, request.PlacementHash, request.RouteHash, registryHash, snapshots),
            issues);
    }

    private static DiscoveredCapabilityPorts DiscoverPorts(
        HardwareSimulationGraph graph,
        SimComponentDef component,
        CompiledComponentExecutionContract contract,
        List<WorkloadMappingV2Issue> issues)
    {
        var result = new List<CapabilityPortSnapshot>();
        var byDeclaredName = new SortedDictionary<string, CapabilityPortSnapshot>(StringComparer.Ordinal);
        var duplicateDeclaredNames = contract.Ports
            .GroupBy(port => port.Name, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var duplicateName in duplicateDeclaredNames.OrderBy(value => value, StringComparer.Ordinal))
        {
            issues.Add(Error(
                Phase8ACapabilityIssueCodes.ExecutionContractInvalid,
                $"$.components[{component.Id}].executionContract.ports[name={duplicateName}]",
                "Execution-contract declared port names must be unique.",
                component.Id));
        }

        var discoveredPortIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var declared in contract.Ports.OrderBy(port => port.Name, StringComparer.Ordinal))
        {
            if (string.IsNullOrWhiteSpace(declared.Name))
            {
                issues.Add(Error(
                    Phase8ACapabilityIssueCodes.ExecutionContractInvalid,
                    $"$.components[{component.Id}].executionContract.ports",
                    "Execution-contract declared port names must be non-empty.",
                    component.Id));
                continue;
            }
            if (duplicateDeclaredNames.Contains(declared.Name)) continue;

            var compiledMatches = graph.Ports.Where(port =>
                    string.Equals(port.ComponentId, component.Id, StringComparison.Ordinal) &&
                    string.Equals(port.Name, declared.Name, StringComparison.Ordinal))
                .Take(2)
                .ToList();
            var compiled = compiledMatches.Count == 1 ? compiledMatches[0] : null;
            var integralBandwidth = double.IsFinite(declared.BandwidthBitsPerCycle) &&
                declared.BandwidthBitsPerCycle > 0 &&
                declared.BandwidthBitsPerCycle == Math.Truncate(declared.BandwidthBitsPerCycle) &&
                declared.BandwidthBitsPerCycle <= long.MaxValue;
            if (compiled is null || !integralBandwidth || compiled.Direction != declared.Direction ||
                compiled.SignalType != declared.SignalType || compiled.DataType != declared.DataType ||
                compiled.Protocol != declared.Protocol || compiled.BandwidthBitsPerCycle != declared.BandwidthBitsPerCycle ||
                (declared.Precision != PrecisionKind.Any && compiled.Precision != declared.Precision) ||
                string.IsNullOrWhiteSpace(compiled.Id) || !discoveredPortIds.Add(compiled.Id))
            {
                issues.Add(Error(Phase8ACapabilityIssueCodes.PortContractMismatch,
                    $"$.components[{component.Id}].ports[{declared.Name}]",
                    "Execution-contract port must map one-to-one to a uniquely identified typed compiled SimulationGraph port.", component.Id));
                continue;
            }

            var snapshot = new CapabilityPortSnapshot(
                compiled.Id,
                declared.Direction.ToString().ToLowerInvariant(),
                declared.Protocol.ToString().ToLowerInvariant(),
                $"{declared.SignalType.ToString().ToLowerInvariant()}/clock:{compiled.ClockDomain}",
                checked((long)declared.BandwidthBitsPerCycle),
                declared.SemanticRole ?? "",
                compiled.DataType.ToString(),
                compiled.Precision.ToString());
            result.Add(snapshot);
            byDeclaredName.Add(declared.Name, snapshot);
        }

        return new DiscoveredCapabilityPorts(
            new ReadOnlyCollection<CapabilityPortSnapshot>(result),
            new ReadOnlyDictionary<string, CapabilityPortSnapshot>(byDeclaredName));
    }

    private static IReadOnlyDictionary<string, string> DiscoverShapes(
        CompiledComponentProfile? profile,
        CompiledComponentExecutionContract contract,
        string componentId,
        List<WorkloadMappingV2Issue> issues)
    {
        var result = new SortedDictionary<string, string>(profile?.ShapeContract ?? new Dictionary<string, string>(), StringComparer.Ordinal);
        foreach (var roleGroup in contract.Ports
                     .Where(port => !string.IsNullOrWhiteSpace(port.SemanticRole))
                     .GroupBy(port => port.SemanticRole, StringComparer.Ordinal)
                     .OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            var declaredPorts = roleGroup.OrderBy(port => port.Name, StringComparer.Ordinal).ToArray();
            if (declaredPorts.Length == 1)
            {
                result[$"port.{roleGroup.Key}"] = string.Join("x", declaredPorts[0].Shape);
                continue;
            }

            foreach (var declaredPort in declaredPorts)
                result[$"port.{roleGroup.Key}@{declaredPort.Name}"] = string.Join("x", declaredPort.Shape);
        }

        var markerKeys = new[]
        {
            Phase8ACapabilityShapeKeys.MatMulMaximumM,
            Phase8ACapabilityShapeKeys.MatMulMaximumK,
            Phase8ACapabilityShapeKeys.MatMulMaximumN
        };
        var markerCount = markerKeys.Count(result.ContainsKey);
        var activations = contract.Ports.Where(port => string.Equals(port.SemanticRole, RoleActivation, StringComparison.Ordinal)).ToList();
        var weights = contract.Ports.Where(port => string.Equals(port.SemanticRole, StorageRoleWeight, StringComparison.Ordinal)).ToList();
        var outputs = contract.Ports.Where(port => string.Equals(port.SemanticRole, RoleResult, StringComparison.Ordinal)).ToList();
        var completeRoleCounts = activations.Count == 1 && weights.Count == 1 && outputs.Count == 1;

        if (markerCount == 0 && !completeRoleCounts)
            return new ReadOnlyDictionary<string, string>(result);
        if (markerCount != 0 && (markerCount != markerKeys.Length || !completeRoleCounts))
        {
            issues.Add(Error(Phase8ACapabilityIssueCodes.ExecutionContractInvalid,
                $"$.components[{componentId}].executionContract.ports",
                "Explicit MatMul shape intent requires all three matmul.maximum_m/k/n markers and exactly one activation, weight, and result semantic port.", componentId));
            return new ReadOnlyDictionary<string, string>(result);
        }

        var activation = activations[0];
        var weight = weights[0];
        var output = outputs[0];
        if (activation.Direction != PortDirection.Input || weight.Direction != PortDirection.Input || output.Direction != PortDirection.Output)
        {
            issues.Add(Error(Phase8ACapabilityIssueCodes.ExecutionContractInvalid,
                $"$.components[{componentId}].executionContract.ports",
                "MatMul activation and weight semantic ports must be inputs and the result semantic port must be an output.", componentId));
            return new ReadOnlyDictionary<string, string>(result);
        }
        if (activation.Shape.Count != 2 || weight.Shape.Count != 2 || output.Shape.Count != 2 ||
            activation.Shape.Any(value => value <= 0) || weight.Shape.Any(value => value <= 0) || output.Shape.Any(value => value <= 0) ||
            activation.Shape[1] != weight.Shape[0] || output.Shape[0] != activation.Shape[0] || output.Shape[1] != weight.Shape[1])
        {
            issues.Add(Error(Phase8ACapabilityIssueCodes.ExecutionContractInvalid,
                $"$.components[{componentId}].executionContract.ports",
                "Activation, weight, and result semantic port shapes must form X[M,K] x W[K,N] -> Y[M,N].", componentId));
            return new ReadOnlyDictionary<string, string>(result);
        }
        result[Phase8ACapabilityShapeKeys.MatMulMaximumM] = activation.Shape[0].ToString(CultureInfo.InvariantCulture);
        result[Phase8ACapabilityShapeKeys.MatMulMaximumK] = weight.Shape[0].ToString(CultureInfo.InvariantCulture);
        result[Phase8ACapabilityShapeKeys.MatMulMaximumN] = weight.Shape[1].ToString(CultureInfo.InvariantCulture);
        return new ReadOnlyDictionary<string, string>(result);
    }

    private static List<ComponentStorageCapabilitySnapshot> DiscoverStorage(
        CompiledComponentExecutionContract contract,
        IReadOnlyDictionary<string, CapabilityPortSnapshot> portsByDeclaredName,
        string componentId,
        List<WorkloadMappingV2Issue> issues)
    {
        var result = new List<ComponentStorageCapabilitySnapshot>();
        foreach (var entry in contract.Resources
                     .Where(resource => string.Equals(resource.ResourceKind, Phase8ACapabilityResourceKinds.MappingStorageCapability, StringComparison.Ordinal))
                     .OrderBy(resource => resource.Name, StringComparer.Ordinal))
        {
            try
            {
                using var document = JsonDocument.Parse(entry.CanonicalValue);
                var root = document.RootElement;
                var resourceId = Text(root, "resourceId");
                var level = Text(root, "storageLevelId");
                var roles = Strings(root, "supportedOperandRoleIds");
                var precisions = Strings(root, "supportedPrecisionIds");
                var preloadRole = Text(root, "preloadPortSemanticRole");
                var matchingDeclaredPorts = string.IsNullOrWhiteSpace(preloadRole)
                    ? new List<CompiledComponentPortContract>()
                    : contract.Ports
                        .Where(port => string.Equals(port.SemanticRole, preloadRole, StringComparison.Ordinal))
                        .ToList();
                CapabilityPortSnapshot? preloadPort = null;
                if (matchingDeclaredPorts.Count == 1 &&
                    matchingDeclaredPorts[0].Direction == PortDirection.Input &&
                    portsByDeclaredName.TryGetValue(matchingDeclaredPorts[0].Name, out var matchedSnapshot) &&
                    string.Equals(matchedSnapshot.DirectionId, "input", StringComparison.Ordinal) &&
                    string.Equals(matchedSnapshot.SemanticRoleId, preloadRole, StringComparison.Ordinal))
                {
                    preloadPort = matchedSnapshot;
                }

                var capacity = Integer(root, "capacityBits");
                var alignment = Integer(root, "alignmentBits");
                var granularity = Integer(root, "allocationGranularityBits");
                var slots = Integer(root, "residentSlots");
                var readBandwidth = Integer(root, "readBandwidthBitsPerCycle");
                var writeBandwidth = Integer(root, "writeBandwidthBitsPerCycle");
                var readLatency = Integer(root, "readLatencyCycles");
                var writeLatency = Integer(root, "writeLatencyCycles");
                var commit = Text(root, "commitModeId");
                var streaming = Boolean(root, "supportsStreaming");
                var reuse = Boolean(root, "supportsReuse");
                if (string.IsNullOrWhiteSpace(resourceId) || string.IsNullOrWhiteSpace(level) || roles.Count == 0 || precisions.Count == 0 ||
                    preloadPort is null || capacity <= 0 || alignment <= 0 || granularity <= 0 || slots <= 0 ||
                    readBandwidth <= 0 || writeBandwidth <= 0 || readLatency < 0 || writeLatency < 0 || string.IsNullOrWhiteSpace(commit))
                    throw new JsonException("Storage capability contains missing or out-of-range fields, or its non-empty preload semantic role does not resolve exactly one input port.");
                result.Add(new ComponentStorageCapabilitySnapshot(
                    resourceId, level, roles, precisions, capacity, alignment, granularity, checked((int)slots),
                    preloadPort.PortId, readBandwidth, writeBandwidth, readLatency, writeLatency, commit,
                    streaming, reuse, contract.ContractHash));
            }
            catch (Exception exception) when (exception is JsonException or KeyNotFoundException or FormatException or InvalidOperationException or OverflowException)
            {
                issues.Add(Error(Phase8ACapabilityIssueCodes.StorageContractInvalid,
                    $"$.components[{componentId}].executionContract.resources[{entry.Name}]",
                    $"Typed storage capability is invalid: {exception.Message}", componentId));
            }
        }
        return result;
    }

    private static bool ValidContractIdentity(CompiledComponentExecutionContract contract)
    {
        if (string.IsNullOrWhiteSpace(contract.KernelId) ||
            string.IsNullOrWhiteSpace(contract.KernelVersion) ||
            string.IsNullOrWhiteSpace(contract.ContractSchemaId) ||
            string.IsNullOrWhiteSpace(contract.OperationKind) ||
            string.IsNullOrWhiteSpace(contract.ContractHash) ||
            contract.Provenance is null ||
            string.IsNullOrWhiteSpace(contract.Provenance.KernelImplementationHash) ||
            string.IsNullOrWhiteSpace(contract.Provenance.RegistrySnapshotHash))
            return false;

        try
        {
            var cloneJson = JsonSerializer.Serialize(contract, HardwareGraphJson.Options);
            var clone = JsonSerializer.Deserialize<CompiledComponentExecutionContract>(cloneJson, HardwareGraphJson.Options);
            return clone is not null && string.Equals(
                contract.ContractHash,
                ComponentExecutionJson.ComputeContractHash(clone),
                StringComparison.Ordinal);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or NotSupportedException)
        {
            return false;
        }
    }

    private static void RequireHash(string value, string location, List<WorkloadMappingV2Issue> issues)
    {
        if (string.IsNullOrWhiteSpace(value))
            issues.Add(Error(Phase8ACapabilityIssueCodes.MissingFrozenHash, location, "A frozen hash is required before capability discovery."));
    }

    private static string Hash(object value) => ComponentExecutionJson.ComputeSha256(
        ComponentExecutionJson.CanonicalizeJson(JsonSerializer.Serialize(value, HardwareGraphJson.Options)));

    private static string Text(JsonElement root, string name) => root.GetProperty(name).GetString() ?? "";
    private static long Integer(JsonElement root, string name) => root.GetProperty(name).GetInt64();
    private static bool Boolean(JsonElement root, string name) => root.GetProperty(name).GetBoolean();
    private static IReadOnlyList<string> Strings(JsonElement root, string name) => root.GetProperty(name).EnumerateArray()
        .Select(item => item.GetString() ?? "").Where(item => !string.IsNullOrWhiteSpace(item)).OrderBy(item => item, StringComparer.Ordinal).ToList();

    private static WorkloadMappingV2Issue Error(string code, string location, string message, string? relatedId = null) =>
        new(code, ValidationSeverity.Error, location, message, relatedId);
}
