namespace HardwareSim.Core;

/// <summary>Defines the supported adapter insertion chain status values used by hardware simulation contracts.</summary>
public enum AdapterInsertionChainStatus
{
    /// <summary>Selects the ready value for the adapter insertion chain status contract.</summary>
    Ready,
    /// <summary>Selects the no candidate value for the adapter insertion chain status contract.</summary>
    NoCandidate
}

/// <summary>Represents adapter insertion chain plan data exchanged by hardware design and simulation workflows.</summary>
/// <param name="LinkId">Provides the link id value carried by this contract.</param>
/// <param name="Status">Provides the status value carried by this contract.</param>
/// <param name="DryRun">Provides the dry run value carried by this contract.</param>
/// <param name="Summary">Provides the summary value carried by this contract.</param>
/// <param name="Steps">Provides the steps value carried by this contract.</param>
public sealed record AdapterInsertionChainPlan(
    string LinkId,
    AdapterInsertionChainStatus Status,
    bool DryRun,
    string Summary,
    List<AdapterInsertionStep> Steps);

/// <summary>Defines the supported adapter insertion chain apply status values used by hardware simulation contracts.</summary>
public enum AdapterInsertionChainApplyStatus
{
    /// <summary>Selects the applied value for the adapter insertion chain apply status contract.</summary>
    Applied,
    /// <summary>Selects the no candidate value for the adapter insertion chain apply status contract.</summary>
    NoCandidate,
    /// <summary>Selects the link not found value for the adapter insertion chain apply status contract.</summary>
    LinkNotFound,
    /// <summary>Selects the port not found value for the adapter insertion chain apply status contract.</summary>
    PortNotFound
}

/// <summary>Represents adapter insertion chain apply result data exchanged by hardware design and simulation workflows.</summary>
/// <param name="Graph">Provides the graph value carried by this contract.</param>
/// <param name="Status">Provides the status value carried by this contract.</param>
/// <param name="Message">Provides the message value carried by this contract.</param>
/// <param name="ChainPlan">Provides the chain plan value carried by this contract.</param>
/// <param name="AdapterComponentIds">Provides the adapter component ids value carried by this contract.</param>
public sealed record AdapterInsertionChainApplyResult(
    HardwareGraph Graph,
    AdapterInsertionChainApplyStatus Status,
    string Message,
    AdapterInsertionChainPlan ChainPlan,
    List<string> AdapterComponentIds);

/// <summary>Defines the supported adapter insertion multi chain apply status values used by hardware simulation contracts.</summary>
public enum AdapterInsertionMultiChainApplyStatus
{
    /// <summary>Selects the applied value for the adapter insertion multi chain apply status contract.</summary>
    Applied,
    /// <summary>Selects the no candidate value for the adapter insertion multi chain apply status contract.</summary>
    NoCandidate,
    /// <summary>Selects the partial value for the adapter insertion multi chain apply status contract.</summary>
    Partial
}

/// <summary>Represents adapter insertion multi chain link result data exchanged by hardware design and simulation workflows.</summary>
/// <param name="LinkId">Provides the link id value carried by this contract.</param>
/// <param name="Status">Provides the status value carried by this contract.</param>
/// <param name="Message">Provides the message value carried by this contract.</param>
/// <param name="StepIds">Provides the step ids value carried by this contract.</param>
/// <param name="AdapterComponentIds">Provides the adapter component ids value carried by this contract.</param>
public sealed record AdapterInsertionMultiChainLinkResult(
    string LinkId,
    AdapterInsertionChainApplyStatus Status,
    string Message,
    IReadOnlyList<string> StepIds,
    IReadOnlyList<string> AdapterComponentIds);

/// <summary>Represents adapter insertion multi chain apply result data exchanged by hardware design and simulation workflows.</summary>
/// <param name="Graph">Provides the graph value carried by this contract.</param>
/// <param name="Status">Provides the status value carried by this contract.</param>
/// <param name="Message">Provides the message value carried by this contract.</param>
/// <param name="AppliedLinkCount">Provides the applied link count value carried by this contract.</param>
/// <param name="SkippedLinkCount">Provides the skipped link count value carried by this contract.</param>
/// <param name="LinkResults">Provides the link results value carried by this contract.</param>
public sealed record AdapterInsertionMultiChainApplyResult(
    HardwareGraph Graph,
    AdapterInsertionMultiChainApplyStatus Status,
    string Message,
    int AppliedLinkCount,
    int SkippedLinkCount,
    IReadOnlyList<AdapterInsertionMultiChainLinkResult> LinkResults);

/// <summary>Provides adapter insertion chain planner operations for hardware design and simulation workflows.</summary>
public static class AdapterInsertionChainPlanner
{
    /// <summary>Builds a deterministic for link plan without applying unrelated changes.</summary>
    public static AdapterInsertionChainPlan PlanForLink(AdapterInsertionPlan plan, string linkId)
    {
        var linkSteps = plan.Steps
            .Where(s => string.Equals(s.LinkId, linkId, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (linkSteps.Count == 0)
        {
            return new AdapterInsertionChainPlan(linkId, AdapterInsertionChainStatus.NoCandidate, plan.DryRun,
                $"No adapter insertion steps found for link '{linkId}'.", []);
        }

        var ordered = linkSteps
            .GroupBy(ChainCategory, StringComparer.OrdinalIgnoreCase)
            .Select(g => PickRepresentativeStep(g.ToList()))
            .OrderBy(ChainOrder)
            .ThenBy(s => s.StepId, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new AdapterInsertionChainPlan(linkId, AdapterInsertionChainStatus.Ready, plan.DryRun,
            $"Planned same-link adapter chain for '{linkId}': {string.Join(" -> ", ordered.Select(s => s.AdapterType))}.",
            ordered);
    }

    private static AdapterInsertionStep PickRepresentativeStep(IReadOnlyList<AdapterInsertionStep> steps)
    {
        if (steps.Any(s => s.AdapterType == "Protocol Adapter"))
        {
            return steps
                .OrderBy(s => s.MismatchField == "protocol" ? 0 : 1)
                .ThenBy(s => s.StepId, StringComparer.OrdinalIgnoreCase)
                .First();
        }

        return steps.OrderBy(s => s.StepId, StringComparer.OrdinalIgnoreCase).First();
    }

    private static string ChainCategory(AdapterInsertionStep step)
    {
        if (IsSignalAdapter(step.AdapterType))
        {
            return "signal";
        }

        if (IsPrecisionAdapter(step.AdapterType))
        {
            return "precision";
        }

        return "protocol";
    }

    private static int ChainOrder(AdapterInsertionStep step)
    {
        if (IsSignalAdapter(step.AdapterType))
        {
            return 0;
        }

        if (IsPrecisionAdapter(step.AdapterType))
        {
            return 1;
        }

        return 2;
    }

    internal static bool IsSignalAdapter(string adapterType) =>
        adapterType is "E/O Converter" or "O/E Receiver" or "ADC" or "DAC";

    internal static bool IsPrecisionAdapter(string adapterType) =>
        adapterType is "Quantizer" or "Dequantizer" or "Precision Converter";
}

/// <summary>Provides adapter insertion chain applier operations for hardware design and simulation workflows.</summary>
public static class AdapterInsertionChainApplier
{
    /// <summary>Applies the supplied plan or metadata to the target graph or model.</summary>
    public static AdapterInsertionChainApplyResult Apply(HardwareGraph graph, AdapterInsertionChainPlan chainPlan)
    {
        var cloned = HardwareGraphJson.Deserialize(HardwareGraphJson.Serialize(graph));
        return ApplyOnClone(cloned, chainPlan);
    }

    /// <summary>Applies all non conflicting to the supplied graph or model.</summary>
    public static AdapterInsertionMultiChainApplyResult ApplyAllNonConflicting(HardwareGraph graph, AdapterInsertionPlan plan)
    {
        var cloned = HardwareGraphJson.Deserialize(HardwareGraphJson.Serialize(graph));
        var linkIds = plan.Steps
            .Select(s => s.LinkId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (linkIds.Count == 0)
        {
            return new AdapterInsertionMultiChainApplyResult(
                cloned,
                AdapterInsertionMultiChainApplyStatus.NoCandidate,
                "No adapter insertion chains are available to apply.",
                0,
                0,
                []);
        }

        var linkResults = new List<AdapterInsertionMultiChainLinkResult>();
        foreach (var linkId in linkIds)
        {
            var chainPlan = AdapterInsertionChainPlanner.PlanForLink(plan, linkId);
            var result = ApplyOnClone(cloned, chainPlan);
            linkResults.Add(new AdapterInsertionMultiChainLinkResult(
                linkId,
                result.Status,
                result.Message,
                chainPlan.Steps.Select(s => s.StepId).ToList(),
                result.AdapterComponentIds));
        }

        var applied = linkResults.Count(r => r.Status == AdapterInsertionChainApplyStatus.Applied);
        var skipped = linkResults.Count - applied;
        var status = skipped == 0
            ? AdapterInsertionMultiChainApplyStatus.Applied
            : AdapterInsertionMultiChainApplyStatus.Partial;

        return new AdapterInsertionMultiChainApplyResult(
            cloned,
            status,
            $"Applied {applied} of {linkResults.Count} same-link adapter chain(s); skipped {skipped}.",
            applied,
            skipped,
            linkResults);
    }

    private static AdapterInsertionChainApplyResult ApplyOnClone(HardwareGraph cloned, AdapterInsertionChainPlan chainPlan)
    {
        if (chainPlan.Steps.Count == 0)
        {
            return new AdapterInsertionChainApplyResult(cloned, AdapterInsertionChainApplyStatus.NoCandidate,
                "No same-link adapter chain steps are available to apply.", chainPlan, []);
        }

        var targetLink = cloned.Links.FirstOrDefault(l => string.Equals(l.Id, chainPlan.LinkId, StringComparison.OrdinalIgnoreCase));
        if (targetLink is null)
        {
            return new AdapterInsertionChainApplyResult(cloned, AdapterInsertionChainApplyStatus.LinkNotFound,
                $"Link '{chainPlan.LinkId}' was not found.", chainPlan, []);
        }

        var sourceComponent = cloned.FindComponent(targetLink.Source.ComponentId);
        var destinationComponent = cloned.FindComponent(targetLink.Destination.ComponentId);
        var sourcePort = cloned.FindPort(targetLink.Source);
        var destinationPort = cloned.FindPort(targetLink.Destination);
        if (sourceComponent is null || destinationComponent is null || sourcePort is null || destinationPort is null)
        {
            return new AdapterInsertionChainApplyResult(cloned, AdapterInsertionChainApplyStatus.PortNotFound,
                $"Link '{chainPlan.LinkId}' cannot be applied because one endpoint component or port is missing.",
                chainPlan,
                []);
        }

        cloned.Links.Remove(targetLink);
        var adapterIds = new List<string>();
        var currentRef = targetLink.Source;
        var currentContract = PortContract.From(sourcePort);

        for (var index = 0; index < chainPlan.Steps.Count; index++)
        {
            var step = chainPlan.Steps[index];
            var nextContract = NextContract(currentContract, destinationPort, step);
            var adapterId = UniqueComponentId(cloned, step.ProposedAdapterComponentId);
            adapterIds.Add(adapterId);

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
                    currentContract.ToPort("in", PortDirection.Input),
                    nextContract.ToPort("out", PortDirection.Output)
                ],
                Parameters =
                {
                    ["inserted_by"] = "adapter_insertion_chain_applier",
                    ["plan_step_id"] = step.StepId,
                    ["chain_link_id"] = chainPlan.LinkId,
                    ["chain_index"] = index.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["adapter_type"] = step.AdapterType,
                    ["mismatch_field"] = step.MismatchField,
                    ["source_value"] = step.SourceValue,
                    ["destination_value"] = step.DestinationValue
                }
            };
            AdapterRuntimeMetadata.Apply(adapter, step);

            cloned.Components.Add(adapter);
            cloned.Links.Add(CloneLink(targetLink, UniqueLinkId(cloned, $"{currentRef.ComponentId}_to_{adapterId}"), currentRef, new PortRef(adapterId, "in")));
            currentRef = new PortRef(adapterId, "out");
            currentContract = nextContract;
        }

        cloned.Links.Add(CloneLink(targetLink, UniqueLinkId(cloned, $"{currentRef.ComponentId}_to_{targetLink.Destination.ComponentId}"), currentRef, targetLink.Destination));

        return new AdapterInsertionChainApplyResult(cloned, AdapterInsertionChainApplyStatus.Applied,
            $"Applied {adapterIds.Count} adapter(s) on link '{chainPlan.LinkId}'.",
            chainPlan,
            adapterIds);
    }

    private static PortContract NextContract(PortContract current, HardwarePort destinationPort, AdapterInsertionStep step)
    {
        if (AdapterInsertionChainPlanner.IsSignalAdapter(step.AdapterType))
        {
            return current with { SignalType = destinationPort.SignalType };
        }

        if (AdapterInsertionChainPlanner.IsPrecisionAdapter(step.AdapterType))
        {
            return current with { Precision = destinationPort.Precision };
        }

        return current with
        {
            DataType = destinationPort.DataType,
            Protocol = destinationPort.Protocol
        };
    }

    private sealed record PortContract(
        SignalType SignalType,
        HardwareDataType DataType,
        PrecisionKind Precision,
        PortProtocol Protocol,
        int BandwidthBitsPerCycle,
        int LatencyCycles,
        string ClockDomain)
    {
        public static PortContract From(HardwarePort port) =>
            new(port.SignalType, port.DataType, port.Precision, port.Protocol, port.BandwidthBitsPerCycle, port.LatencyCycles, port.ClockDomain);

        public HardwarePort ToPort(string name, PortDirection direction) => new()
        {
            Name = name,
            Direction = direction,
            SignalType = SignalType,
            DataType = DataType,
            Precision = Precision,
            Protocol = Protocol,
            BandwidthBitsPerCycle = BandwidthBitsPerCycle,
            LatencyCycles = LatencyCycles,
            ClockDomain = ClockDomain,
            Required = true
        };
    }

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
