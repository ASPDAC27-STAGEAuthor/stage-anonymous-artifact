namespace HardwareSim.Core;

/// <summary>Defines the supported validation severity values used by hardware simulation contracts.</summary>
public enum ValidationSeverity
{
    /// <summary>Provides diagnostic context without indicating a problem.</summary>
    Info,
    /// <summary>Identifies a recoverable concern that callers should review.</summary>
    Warning,
    /// <summary>Identifies a contract violation that prevents the requested operation.</summary>
    Error
}
/// <summary>Defines the supported validation level values used by hardware simulation contracts.</summary>
public enum ValidationLevel
{
    /// <summary>Selects the port direction value for the validation level contract.</summary>
    PortDirection = 1,
    /// <summary>Selects the signal type value for the validation level contract.</summary>
    SignalType = 2,
    /// <summary>Selects the data type precision value for the validation level contract.</summary>
    DataTypePrecision = 3,
    /// <summary>Selects the protocol value for the validation level contract.</summary>
    Protocol = 4,
    /// <summary>Selects the required port value for the validation level contract.</summary>
    RequiredPort = 5,
    /// <summary>Selects the graph connectivity value for the validation level contract.</summary>
    GraphConnectivity = 6,
    /// <summary>Selects the simulation readiness value for the validation level contract.</summary>
    SimulationReadiness = 7,
    /// <summary>Selects the physical routing readiness value for the validation level contract.</summary>
    PhysicalRoutingReadiness = 8
}

/// <summary>Represents validation issue data exchanged by hardware design and simulation workflows.</summary>
/// <param name="Severity">Provides the severity value carried by this contract.</param>
/// <param name="Level">Provides the level value carried by this contract.</param>
/// <param name="Code">Provides the code value carried by this contract.</param>
/// <param name="Message">Provides the message value carried by this contract.</param>
/// <param name="LinkId">Provides the link id value carried by this contract.</param>
/// <param name="ComponentId">Provides the component id value carried by this contract.</param>
/// <param name="SuggestedAdapter">Provides the suggested adapter value carried by this contract.</param>
/// <param name="AdapterSuggestion">Provides the adapter suggestion value carried by this contract.</param>
public sealed record ValidationIssue(
    ValidationSeverity Severity,
    ValidationLevel Level,
    string Code,
    string Message,
    string? LinkId = null,
    string? ComponentId = null,
    string? SuggestedAdapter = null,
    AdapterSuggestion? AdapterSuggestion = null);

/// <summary>Represents adapter suggestion data exchanged by hardware design and simulation workflows.</summary>
/// <param name="AdapterType">Provides the adapter type value carried by this contract.</param>
/// <param name="Reason">Provides the reason value carried by this contract.</param>
/// <param name="SourceComponentId">Provides the source component id value carried by this contract.</param>
/// <param name="SourcePortName">Provides the source port name value carried by this contract.</param>
/// <param name="DestinationComponentId">Provides the destination component id value carried by this contract.</param>
/// <param name="DestinationPortName">Provides the destination port name value carried by this contract.</param>
/// <param name="MismatchField">Provides the mismatch field value carried by this contract.</param>
/// <param name="SourceValue">Provides the source value carried by this contract.</param>
/// <param name="DestinationValue">Provides the destination value carried by this contract.</param>
public sealed record AdapterSuggestion(
    string AdapterType,
    string Reason,
    string SourceComponentId,
    string SourcePortName,
    string DestinationComponentId,
    string DestinationPortName,
    string MismatchField,
    string SourceValue,
    string DestinationValue);

/// <summary>Represents validation result data exchanged by hardware design and simulation workflows.</summary>
public sealed class ValidationResult
{
    /// <summary>Gets the ordered validation issues collected for the inspected model.</summary>
    public List<ValidationIssue> Issues { get; } = [];
    /// <summary>Gets whether any collected issue has error severity.</summary>
    public bool HasErrors => Issues.Any(i => i.Severity == ValidationSeverity.Error);

    /// <summary>Adds the supplied item to the enclosing collection or registry.</summary>
    public void Add(ValidationIssue issue) => Issues.Add(issue);
}

/// <summary>Represents hardware graph validator data exchanged by hardware design and simulation workflows.</summary>
public sealed class HardwareGraphValidator
{
    /// <summary>Validates the supplied graph and returns structured diagnostics.</summary>
    public ValidationResult Validate(HardwareGraph graph)
    {
        var result = new ValidationResult();
        CheckComponentAndLinkReferences(graph, result);
        CheckGroups(graph, result);
        CheckLinkCompatibility(graph, result);
        CheckRequiredPorts(graph, result);
        CheckConnectivity(graph, result);
        CheckSimulationReadiness(graph, result);
        CheckPhysicalRoutingReadiness(graph, result);
        return result;
    }

    private static void CheckComponentAndLinkReferences(HardwareGraph graph, ValidationResult result)
    {
        foreach (var duplicate in graph.Components.GroupBy(c => c.Id).Where(g => g.Count() > 1))
        {
            result.Add(new(ValidationSeverity.Error, ValidationLevel.GraphConnectivity, "duplicate_component_id",
                $"Component id '{duplicate.Key}' is used more than once.", ComponentId: duplicate.Key));
        }

        foreach (var link in graph.Links)
        {
            if (graph.FindComponent(link.Source.ComponentId) is null)
            {
                result.Add(new(ValidationSeverity.Error, ValidationLevel.GraphConnectivity, "missing_source_component",
                    $"Link '{link.Id}' source component '{link.Source.ComponentId}' does not exist.", LinkId: link.Id));
            }

            if (graph.FindComponent(link.Destination.ComponentId) is null)
            {
                result.Add(new(ValidationSeverity.Error, ValidationLevel.GraphConnectivity, "missing_destination_component",
                    $"Link '{link.Id}' destination component '{link.Destination.ComponentId}' does not exist.", LinkId: link.Id));
            }

            if (graph.FindPort(link.Source) is null)
            {
                result.Add(new(ValidationSeverity.Error, ValidationLevel.GraphConnectivity, "missing_source_port",
                    $"Link '{link.Id}' source port '{link.Source.ComponentId}.{link.Source.PortName}' does not exist.", LinkId: link.Id));
            }

            if (graph.FindPort(link.Destination) is null)
            {
                result.Add(new(ValidationSeverity.Error, ValidationLevel.GraphConnectivity, "missing_destination_port",
                    $"Link '{link.Id}' destination port '{link.Destination.ComponentId}.{link.Destination.PortName}' does not exist.", LinkId: link.Id));
            }
        }
    }

    private static void CheckGroups(HardwareGraph graph, ValidationResult result)
    {
        foreach (var duplicate in graph.Groups
                     .Where(group => !string.IsNullOrWhiteSpace(group.Id))
                     .GroupBy(group => group.Id, StringComparer.OrdinalIgnoreCase)
                     .Where(group => group.Count() > 1))
        {
            result.Add(new(ValidationSeverity.Error, ValidationLevel.GraphConnectivity, "duplicate_group_id",
                $"Group id '{duplicate.Key}' is used more than once."));
        }

        foreach (var group in graph.Groups)
        {
            if (group.ComponentIds.Count == 0)
            {
                result.Add(new(ValidationSeverity.Warning, ValidationLevel.GraphConnectivity, "empty_group",
                    $"Group '{group.Id}' has no members."));
            }

            foreach (var duplicate in group.ComponentIds
                         .GroupBy(id => id, StringComparer.OrdinalIgnoreCase)
                         .Where(ids => ids.Count() > 1))
            {
                result.Add(new(ValidationSeverity.Error, ValidationLevel.GraphConnectivity, "duplicate_group_member",
                    $"Group '{group.Id}' contains member '{duplicate.Key}' more than once.",
                    ComponentId: duplicate.Key));
            }

            foreach (var memberId in group.ComponentIds.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (graph.FindComponent(memberId) is null)
                {
                    result.Add(new(ValidationSeverity.Error, ValidationLevel.GraphConnectivity, "missing_group_member",
                        $"Group '{group.Id}' references missing component '{memberId}'.",
                        ComponentId: memberId));
                }
            }
        }


        for (var leftIndex = 0; leftIndex < graph.Groups.Count; leftIndex++)
        {
            for (var rightIndex = leftIndex + 1; rightIndex < graph.Groups.Count; rightIndex++)
            {
                var left = graph.Groups[leftIndex];
                var right = graph.Groups[rightIndex];
                if (GroupMacroTools.IsDuplicateGroupMembership(left.ComponentIds, right.ComponentIds))
                {
                    result.Add(new(ValidationSeverity.Error, ValidationLevel.GraphConnectivity, "duplicate_group_membership",
                        $"Group '{left.Id}' duplicates group '{right.Id}' with the same member set. Groups must be unique, disjoint, or fully nested.",
                        ComponentId: left.ComponentIds.FirstOrDefault()));
                    continue;
                }

                if (!GroupMacroTools.IsPartialGroupOverlap(left.ComponentIds, right.ComponentIds))
                {
                    continue;
                }

                var overlap = GroupMacroTools.GroupOverlapMembers(left.ComponentIds, right.ComponentIds);
                result.Add(new(ValidationSeverity.Error, ValidationLevel.GraphConnectivity, "partial_group_overlap",
                    $"Group '{left.Id}' partially overlaps group '{right.Id}' on member(s): {string.Join(", ", overlap)}. Groups must be disjoint or fully nested.",
                    ComponentId: overlap.FirstOrDefault()));
            }
        }


    }
    private static void CheckLinkCompatibility(HardwareGraph graph, ValidationResult result)
    {
        foreach (var link in graph.Links)
        {
            var source = graph.FindPort(link.Source);
            var destination = graph.FindPort(link.Destination);
            if (source is null || destination is null)
            {
                continue;
            }

            if (!CanDrive(source.Direction) || !CanReceive(destination.Direction))
            {
                result.Add(new(ValidationSeverity.Error, ValidationLevel.PortDirection, "invalid_port_direction",
                    $"Link '{link.Id}' must connect output/bidirectional to input/bidirectional, but connects {source.Direction} to {destination.Direction}.",
                    LinkId: link.Id));
            }

            if (source.SignalType != destination.SignalType)
            {
                var adapter = SuggestSignalAdapter(source.SignalType, destination.SignalType);
                result.Add(new(ValidationSeverity.Error, ValidationLevel.SignalType, "signal_type_mismatch",
                    $"Link '{link.Id}' connects {source.SignalType} to {destination.SignalType}.",
                    LinkId: link.Id,
                    SuggestedAdapter: adapter,
                    AdapterSuggestion: BuildAdapterSuggestion(link, adapter, "signal_type", source.SignalType, destination.SignalType)));
            }

            if (source.DataType != destination.DataType)
            {
                const string adapter = "Protocol Adapter";
                result.Add(new(ValidationSeverity.Warning, ValidationLevel.DataTypePrecision, "data_type_mismatch",
                    $"Link '{link.Id}' connects {source.DataType} to {destination.DataType}.",
                    LinkId: link.Id,
                    SuggestedAdapter: adapter,
                    AdapterSuggestion: BuildAdapterSuggestion(link, adapter, "data_type", source.DataType, destination.DataType)));
            }

            if (source.Precision != PrecisionKind.Any &&
                destination.Precision != PrecisionKind.Any &&
                source.Precision != destination.Precision)
            {
                var adapter = SuggestPrecisionAdapter(source.Precision, destination.Precision);
                result.Add(new(ValidationSeverity.Warning, ValidationLevel.DataTypePrecision, "precision_mismatch",
                    $"Link '{link.Id}' connects {source.Precision} to {destination.Precision}.",
                    LinkId: link.Id,
                    SuggestedAdapter: adapter,
                    AdapterSuggestion: BuildAdapterSuggestion(link, adapter, "precision", source.Precision, destination.Precision)));
                if (IsLargePrecisionSpan(source.Precision, destination.Precision))
                {
                    result.Add(new(ValidationSeverity.Warning, ValidationLevel.DataTypePrecision, "precision_large_span",
                        $"Link '{link.Id}' converts {source.Precision} to {destination.Precision} across a large bit-width span; use an explicit {adapter} with reviewed scale and zero_point. Multi-step adapter chains are suggestions until explicitly applied.",
                        LinkId: link.Id,
                        SuggestedAdapter: adapter,
                        AdapterSuggestion: BuildAdapterSuggestion(link, adapter, "precision", source.Precision, destination.Precision)));
                }
            }

            if (source.Protocol != destination.Protocol)
            {
                const string adapter = "Protocol Adapter";
                result.Add(new(ValidationSeverity.Error, ValidationLevel.Protocol, "protocol_mismatch",
                    $"Link '{link.Id}' connects {source.Protocol} to {destination.Protocol}.",
                    LinkId: link.Id,
                    SuggestedAdapter: adapter,
                    AdapterSuggestion: BuildAdapterSuggestion(link, adapter, "protocol", source.Protocol, destination.Protocol)));
            }
        }

        foreach (var inputGroup in graph.Links.GroupBy(l => $"{l.Destination.ComponentId}.{l.Destination.PortName}"))
        {
            var destination = graph.FindPort(inputGroup.First().Destination);
            if (destination is not null && destination.Direction == PortDirection.Input && !destination.MultiConnect && inputGroup.Count() > 1)
            {
                result.Add(new(ValidationSeverity.Error, ValidationLevel.PortDirection, "input_port_already_connected",
                    $"Input port '{inputGroup.Key}' has {inputGroup.Count()} incoming links but does not allow multi-connect.",
                    ComponentId: inputGroup.First().Destination.ComponentId));
            }
        }
    }

    private static void CheckRequiredPorts(HardwareGraph graph, ValidationResult result)
    {
        foreach (var component in graph.Components)
        {
            foreach (var port in component.Ports.Where(p => p.Required))
            {
                var connected = graph.Links.Any(l =>
                    l.Source.ComponentId == component.Id && l.Source.PortName == port.Name ||
                    l.Destination.ComponentId == component.Id && l.Destination.PortName == port.Name);

                if (!connected)
                {
                    result.Add(new(ValidationSeverity.Error, ValidationLevel.RequiredPort, "required_port_unconnected",
                        $"Required port '{component.Id}.{port.Name}' is not connected.", ComponentId: component.Id));
                }
            }
        }
    }

    private static void CheckConnectivity(HardwareGraph graph, ValidationResult result)
    {
        foreach (var component in graph.Components)
        {
            var linked = graph.Links.Any(l => l.Source.ComponentId == component.Id || l.Destination.ComponentId == component.Id);
            if (!linked)
            {
                result.Add(new(ValidationSeverity.Warning, ValidationLevel.GraphConnectivity, "isolated_component",
                    $"Component '{component.Id}' is isolated.", ComponentId: component.Id));
            }
        }
    }

    private static void CheckSimulationReadiness(HardwareGraph graph, ValidationResult result)
    {
        if (!graph.Components.Any(c => c.Type == ComponentKind.WorkloadSource))
        {
            result.Add(new(ValidationSeverity.Error, ValidationLevel.SimulationReadiness, "missing_workload_source",
                "Simulation requires at least one WorkloadSource component."));
        }

        if (!graph.Components.Any(c => c.Type == ComponentKind.WorkloadSink))
        {
            result.Add(new(ValidationSeverity.Error, ValidationLevel.SimulationReadiness, "missing_workload_sink",
                "Simulation requires at least one WorkloadSink component."));
        }
    }

    private static void CheckPhysicalRoutingReadiness(HardwareGraph graph, ValidationResult result)
    {
        foreach (var link in graph.Links.Where(l => l.RouteType != "logical" && l.PhysicalLength <= 0))
        {
            result.Add(new(ValidationSeverity.Warning, ValidationLevel.PhysicalRoutingReadiness, "missing_physical_length",
                $"Routed link '{link.Id}' has route type '{link.RouteType}' but no physical length.", LinkId: link.Id));
        }
    }

    private static bool CanDrive(PortDirection direction) => direction is PortDirection.Output or PortDirection.Bidirectional;
    private static bool CanReceive(PortDirection direction) => direction is PortDirection.Input or PortDirection.Bidirectional;

    private static string SuggestSignalAdapter(SignalType source, SignalType destination) => (source, destination) switch
    {
        (SignalType.Optical, SignalType.Digital) => "O/E Receiver",
        (SignalType.Digital, SignalType.Optical) => "E/O Converter",
        (SignalType.Analog, SignalType.Digital) => "ADC",
        (SignalType.Digital, SignalType.Analog) => "DAC",
        _ => "Protocol Adapter"
    };

    private static string SuggestPrecisionAdapter(PrecisionKind source, PrecisionKind destination)
    {
        if (IsFloatingPrecision(source) && IsIntegerPrecision(destination))
        {
            return "Quantizer";
        }

        if (IsIntegerPrecision(source) && IsFloatingPrecision(destination))
        {
            return "Dequantizer";
        }

        return "Precision Converter";
    }

    private static bool IsFloatingPrecision(PrecisionKind precision) => precision is
        PrecisionKind.FP32 or PrecisionKind.FP16 or PrecisionKind.BF16 or PrecisionKind.TF32 or PrecisionKind.FP8_E4M3 or PrecisionKind.FP8_E5M2;

    private static bool IsIntegerPrecision(PrecisionKind precision) => precision is
        PrecisionKind.INT32 or PrecisionKind.INT16 or PrecisionKind.INT8 or PrecisionKind.INT4 or PrecisionKind.INT2 or PrecisionKind.Binary;

    private static bool IsLargePrecisionSpan(PrecisionKind source, PrecisionKind destination)
    {
        if (!PrecisionModel.TryGetDigitalBitWidth(source, out var sourceBits) ||
            !PrecisionModel.TryGetDigitalBitWidth(destination, out var destinationBits))
        {
            return false;
        }

        var high = Math.Max(sourceBits, destinationBits);
        var low = Math.Max(1, Math.Min(sourceBits, destinationBits));
        return high / low >= 8;
    }

    private static AdapterSuggestion BuildAdapterSuggestion<T>(
        HardwareLink link,
        string adapter,
        string mismatchField,
        T sourceValue,
        T destinationValue) =>
        new(
            adapter,
            $"{mismatchField} mismatch between {link.Source.ComponentId}.{link.Source.PortName} and {link.Destination.ComponentId}.{link.Destination.PortName}",
            link.Source.ComponentId,
            link.Source.PortName,
            link.Destination.ComponentId,
            link.Destination.PortName,
            mismatchField,
            sourceValue?.ToString() ?? "",
            destinationValue?.ToString() ?? "");
}
