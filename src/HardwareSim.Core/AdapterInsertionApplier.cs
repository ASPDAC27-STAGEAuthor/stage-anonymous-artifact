namespace HardwareSim.Core;

/// <summary>Defines the supported adapter insertion apply status values used by hardware simulation contracts.</summary>
public enum AdapterInsertionApplyStatus
{
    /// <summary>Selects the applied value for the adapter insertion apply status contract.</summary>
    Applied,
    /// <summary>Selects the no candidate value for the adapter insertion apply status contract.</summary>
    NoCandidate,
    /// <summary>Selects the step not found value for the adapter insertion apply status contract.</summary>
    StepNotFound,
    /// <summary>Selects the link not found value for the adapter insertion apply status contract.</summary>
    LinkNotFound,
    /// <summary>Selects the port not found value for the adapter insertion apply status contract.</summary>
    PortNotFound,
    /// <summary>Selects the unsupported conflict value for the adapter insertion apply status contract.</summary>
    UnsupportedConflict
}

/// <summary>Represents adapter insertion apply result data exchanged by hardware design and simulation workflows.</summary>
/// <param name="Graph">Provides the graph value carried by this contract.</param>
/// <param name="Status">Provides the status value carried by this contract.</param>
/// <param name="Message">Provides the message value carried by this contract.</param>
/// <param name="AppliedStep">Provides the applied step value carried by this contract.</param>
/// <param name="AdapterComponentId">Provides the adapter component id value carried by this contract.</param>
public sealed record AdapterInsertionApplyResult(
    HardwareGraph Graph,
    AdapterInsertionApplyStatus Status,
    string Message,
    AdapterInsertionStep? AppliedStep = null,
    string? AdapterComponentId = null);

/// <summary>Provides adapter insertion applier operations for hardware design and simulation workflows.</summary>
public static class AdapterInsertionApplier
{
    /// <summary>Applies the supplied plan or metadata to the target graph or model.</summary>
    public static AdapterInsertionApplyResult Apply(HardwareGraph graph, AdapterInsertionPlan plan)
    {
        if (plan.Steps.Count == 0)
        {
            return new AdapterInsertionApplyResult(Clone(graph), AdapterInsertionApplyStatus.NoCandidate,
                "No adapter insertion steps are available to apply.");
        }

        if (plan.Steps.Count != 1)
        {
            return new AdapterInsertionApplyResult(Clone(graph), AdapterInsertionApplyStatus.UnsupportedConflict,
                "Automatic multi-step adapter application is not supported; select exactly one plan step explicitly.");
        }

        return ApplySingleStep(graph, plan.Steps[0]);
    }

    /// <summary>Applies single step to the supplied graph or model.</summary>
    public static AdapterInsertionApplyResult ApplySingleStep(HardwareGraph graph, AdapterInsertionPlan plan, string stepId)
    {
        var step = plan.Steps.SingleOrDefault(s => string.Equals(s.StepId, stepId, StringComparison.OrdinalIgnoreCase));
        return step is null
            ? new AdapterInsertionApplyResult(Clone(graph), AdapterInsertionApplyStatus.StepNotFound,
                $"Adapter insertion step '{stepId}' was not found.")
            : ApplySingleStep(graph, step);
    }

    /// <summary>Applies single step to the supplied graph or model.</summary>
    public static AdapterInsertionApplyResult ApplySingleStep(HardwareGraph graph, AdapterInsertionStep step)
    {
        var cloned = Clone(graph);
        var targetLink = cloned.Links.FirstOrDefault(l => string.Equals(l.Id, step.LinkId, StringComparison.OrdinalIgnoreCase));
        if (targetLink is null)
        {
            return new AdapterInsertionApplyResult(cloned, AdapterInsertionApplyStatus.LinkNotFound,
                $"Link '{step.LinkId}' was not found.", step);
        }

        var sourceComponent = cloned.FindComponent(targetLink.Source.ComponentId);
        var destinationComponent = cloned.FindComponent(targetLink.Destination.ComponentId);
        var sourcePort = cloned.FindPort(targetLink.Source);
        var destinationPort = cloned.FindPort(targetLink.Destination);
        if (sourceComponent is null || destinationComponent is null || sourcePort is null || destinationPort is null)
        {
            return new AdapterInsertionApplyResult(cloned, AdapterInsertionApplyStatus.PortNotFound,
                $"Link '{step.LinkId}' cannot be applied because one endpoint component or port is missing.", step);
        }

        var adapterId = UniqueComponentId(cloned, step.ProposedAdapterComponentId);
        var adapter = new HardwareComponent
        {
            Id = adapterId,
            Name = $"{step.AdapterType} for {step.SourceValue}->{step.DestinationValue}",
            Type = step.ProposedComponentKind,
            Position = new GridPosition(
                (sourceComponent.Position.X + destinationComponent.Position.X) / 2,
                (sourceComponent.Position.Y + destinationComponent.Position.Y) / 2),
            Ports =
            [
                CopyEndpointPort(sourcePort, "in", PortDirection.Input),
                CopyEndpointPort(destinationPort, "out", PortDirection.Output)
            ],
            Parameters =
            {
                ["inserted_by"] = "adapter_insertion_applier",
                ["plan_step_id"] = step.StepId,
                ["adapter_type"] = step.AdapterType,
                ["mismatch_field"] = step.MismatchField,
                ["source_value"] = step.SourceValue,
                ["destination_value"] = step.DestinationValue
            }
        };
        AdapterRuntimeMetadata.Apply(adapter, step);

        cloned.Components.Add(adapter);
        cloned.Links.Remove(targetLink);
        cloned.Links.Add(CloneLink(targetLink, UniqueLinkId(cloned, $"{targetLink.Id}_to_{adapterId}"), targetLink.Source, new PortRef(adapterId, "in")));
        cloned.Links.Add(CloneLink(targetLink, UniqueLinkId(cloned, $"{adapterId}_to_{targetLink.Destination.ComponentId}"), new PortRef(adapterId, "out"), targetLink.Destination));

        return new AdapterInsertionApplyResult(cloned, AdapterInsertionApplyStatus.Applied,
            $"Applied adapter insertion step '{step.StepId}' by inserting '{adapterId}' on link '{step.LinkId}'.",
            step,
            adapterId);
    }

    private static HardwareGraph Clone(HardwareGraph graph) =>
        HardwareGraphJson.Deserialize(HardwareGraphJson.Serialize(graph));

    private static HardwarePort CopyEndpointPort(HardwarePort template, string name, PortDirection direction) => new()
    {
        Name = name,
        Direction = direction,
        SignalType = template.SignalType,
        DataType = template.DataType,
        Precision = template.Precision,
        Protocol = template.Protocol,
        BandwidthBitsPerCycle = template.BandwidthBitsPerCycle,
        LatencyCycles = template.LatencyCycles,
        ClockDomain = template.ClockDomain,
        Required = true,
        MultiConnect = false
    };

    private static HardwareLink CloneLink(HardwareLink template, string id, PortRef source, PortRef destination) => new()
    {
        Id = id,
        Source = source,
        Destination = destination,
        BandwidthBitsPerCycle = template.BandwidthBitsPerCycle,
        LatencyCycles = template.LatencyCycles,
        EnergyPerBit = template.EnergyPerBit,
        PhysicalLength = template.PhysicalLength,
        RouteType = template.RouteType,
        ModelRef = template.ModelRef,
        Parameters = new Dictionary<string, string>(template.Parameters, StringComparer.OrdinalIgnoreCase)
    };

    private static string UniqueComponentId(HardwareGraph graph, string preferredId)
    {
        var id = string.IsNullOrWhiteSpace(preferredId) ? "inserted_adapter" : preferredId;
        var baseId = id;
        var index = 1;
        while (graph.FindComponent(id) is not null)
        {
            id = $"{baseId}_{index++}";
        }

        return id;
    }

    private static string UniqueLinkId(HardwareGraph graph, string preferredId)
    {
        var id = preferredId;
        var baseId = id;
        var index = 1;
        while (graph.Links.Any(l => string.Equals(l.Id, id, StringComparison.OrdinalIgnoreCase)))
        {
            id = $"{baseId}_{index++}";
        }

        return id;
    }
}
