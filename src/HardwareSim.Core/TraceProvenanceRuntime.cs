namespace HardwareSim.Core;

public sealed partial class CycleSimulationEngine
{
    private static readonly Lazy<ComponentTypeRegistry> TraceComponentRegistry = new(() => ComponentTypeRegistry.CreateDefault());

    private static void EnrichTraceProvenance(
        HardwareSimulationGraph graph,
        CycleTraceRecord record,
        IReadOnlyDictionary<string, Packet> packetCatalog)
    {
        for (var index = 0; index < record.Events.Count; index++)
        {
            var traceEvent = record.Events[index];
            if (traceEvent.Provenance is not null ||
                string.IsNullOrWhiteSpace(traceEvent.PacketId) ||
                !packetCatalog.TryGetValue(traceEvent.PacketId, out var packet) ||
                string.IsNullOrWhiteSpace(packet.WorkloadOpId) ||
                string.IsNullOrWhiteSpace(packet.TensorId) ||
                string.IsNullOrWhiteSpace(packet.TileId))
            {
                continue;
            }

            record.Events[index] = TraceProvenanceFactory.Attach(
                traceEvent,
                packet,
                ParseTraceStallReason(traceEvent),
                TraceEnergyCategory(graph, traceEvent));
        }
    }

    private static StallReason? ParseTraceStallReason(TraceEvent traceEvent)
    {
        if (Enum.TryParse<StallReason>(traceEvent.StallReason, true, out var direct))
        {
            return direct;
        }

        const string marker = "stall_reason=";
        var detail = traceEvent.Detail ?? "";
        var start = detail.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (start < 0)
        {
            return null;
        }

        start += marker.Length;
        var token = new string(detail
            .Skip(start)
            .TakeWhile(character => char.IsLetterOrDigit(character) || character == '_')
            .ToArray());
        return Enum.TryParse<StallReason>(token, true, out var parsed) ? parsed : null;
    }

    private static EnergyCategory TraceEnergyCategory(
        HardwareSimulationGraph graph,
        TraceEvent traceEvent)
    {
        var link = string.IsNullOrWhiteSpace(traceEvent.LinkId)
            ? null
            : graph.Links.FirstOrDefault(item => string.Equals(item.Id, traceEvent.LinkId, StringComparison.OrdinalIgnoreCase));
        if (link is not null)
        {
            return string.Equals(link.RouteType, "optical", StringComparison.OrdinalIgnoreCase)
                ? EnergyCategory.Optical
                : EnergyCategory.NoC;
        }

        var component = string.IsNullOrWhiteSpace(traceEvent.ComponentId)
            ? null
            : graph.FindComponent(traceEvent.ComponentId);
        if (component is null)
        {
            return EnergyCategory.NoC;
        }

        if (component.Parameters.TryGetValue(ComponentPluginRuntimeKeys.EnergyCategory, out var rawCategory) &&
            Enum.TryParse<EnergyCategory>(rawCategory, ignoreCase: true, out var pluginCategory))
        {
            return pluginCategory;
        }

        var plugin = TraceComponentRegistry.Value.GetPlugin(ComponentTypeIds.EffectiveTypeId(component));
        if (plugin is not null && ComponentTypeIds.IsFirstPartyExtensionTypeId(plugin.TypeId))
        {
            return plugin.MetricDescriptors.FirstOrDefault()?.Category ?? EnergyCategory.NoC;
        }

        return component.Type switch
        {
            ComponentKind.ProcessingElement or ComponentKind.ReductionUnit or ComponentKind.SoftmaxUnit =>
                EnergyCategory.Compute,
            ComponentKind.Buffer or ComponentKind.Memory =>
                EnergyCategory.Memory,
            ComponentKind.Adapter or ComponentKind.PrecisionConverter or ComponentKind.Quantizer or ComponentKind.Dequantizer =>
                EnergyCategory.Conversion,
            _ => EnergyCategory.NoC
        };
    }
}
