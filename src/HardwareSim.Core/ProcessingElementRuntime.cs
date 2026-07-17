namespace HardwareSim.Core;

/// <summary>Represents one PE state transition record for exact runtime tests and trace projection.</summary>
/// <param name="Cycle">Provides the cycle where the transition was recorded.</param>
/// <param name="StateBefore">Provides the state before the runtime action.</param>
/// <param name="StateAfter">Provides the state after the runtime action.</param>
/// <param name="RemainingMacs">Provides the remaining MAC count after the runtime action.</param>
public sealed record ProcessingElementStateRecord(long Cycle, ProcessingElementRuntimeState StateBefore, ProcessingElementRuntimeState StateAfter, long RemainingMacs);

/// <summary>Provides a deterministic Phase 3C processing element runtime state machine.</summary>
public sealed class ProcessingElementRuntime
{
    private readonly SimComponentDef component;
    private Packet? inputPacket;
    private Packet? outputPacket;
    private long remainingMacs;
    private long effectiveMacPerCycle;
    private long totalMacs;

    /// <summary>Initializes a processing element runtime for one compiled PE component.</summary>
    public ProcessingElementRuntime(SimComponentDef component)
    {
        this.component = component ?? throw new ArgumentNullException(nameof(component));
        State = ProcessingElementRuntimeState.WaitingInput;
    }

    /// <summary>Gets the committed PE runtime state.</summary>
    public ProcessingElementRuntimeState State { get; private set; }
    /// <summary>Gets the remaining MAC operations for the current packet.</summary>
    public long RemainingMacs => remainingMacs;
    /// <summary>Gets the effective throughput chosen for the current packet.</summary>
    public long EffectiveMacPerCycle => effectiveMacPerCycle;
    /// <summary>Gets exact state records emitted by this runtime.</summary>
    public List<ProcessingElementStateRecord> StateTrace { get; } = [];

    /// <summary>Attempts to accept a complete packet when the PE is waiting for input.</summary>
    public bool TryAcceptInput(Packet packet, long cycle)
    {
        if (State != ProcessingElementRuntimeState.WaitingInput)
        {
            return false;
        }
        inputPacket = PacketCatalog.ClonePacket(packet);
        totalMacs = ResolveTotalMacs(component, inputPacket);
        effectiveMacPerCycle = ResolveMacPerCycle(component, inputPacket.Precision);
        remainingMacs = totalMacs;
        State = ProcessingElementRuntimeState.Computing;
        StateTrace.Add(new ProcessingElementStateRecord(cycle, ProcessingElementRuntimeState.WaitingInput, State, remainingMacs));
        return true;
    }

    /// <summary>Advances one compute cycle when the PE is computing.</summary>
    public void Tick(long cycle)
    {
        var before = State;
        if (State == ProcessingElementRuntimeState.Computing)
        {
            remainingMacs = Math.Max(0, remainingMacs - effectiveMacPerCycle);
            if (remainingMacs == 0)
            {
                outputPacket = BuildOutputPacket(inputPacket!, cycle);
                State = ProcessingElementRuntimeState.WaitingOutput;
            }
        }
        StateTrace.Add(new ProcessingElementStateRecord(cycle, before, State, remainingMacs));
    }

    /// <summary>Attempts to emit the computed output packet when downstream capacity is available.</summary>
    public bool TryEmitOutput(bool downstreamAccepted, long cycle, out Packet? packet)
    {
        packet = null;
        if (State != ProcessingElementRuntimeState.WaitingOutput || outputPacket is null)
        {
            return false;
        }
        if (!downstreamAccepted)
        {
            StateTrace.Add(new ProcessingElementStateRecord(cycle, State, State, remainingMacs));
            return false;
        }

        packet = PacketCatalog.ClonePacket(outputPacket);
        inputPacket = null;
        outputPacket = null;
        remainingMacs = 0;
        totalMacs = 0;
        effectiveMacPerCycle = 0;
        var before = State;
        State = ProcessingElementRuntimeState.WaitingInput;
        StateTrace.Add(new ProcessingElementStateRecord(cycle, before, State, remainingMacs));
        return true;
    }

    /// <summary>Computes exact MAC latency cycles for a packet on a component.</summary>
    public static long ComputeCycles(SimComponentDef component, Packet packet)
    {
        var macs = ResolveTotalMacs(component, packet);
        var macPerCycle = ResolveMacPerCycle(component, packet.Precision);
        return Math.Max(1, (long)Math.Ceiling(macs / (double)macPerCycle));
    }

    private static long ResolveTotalMacs(SimComponentDef component, Packet packet)
    {
        if (packet.Metadata.TryGetValue("total_macs", out var packetMacsRaw) && long.TryParse(packetMacsRaw, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var packetMacs) && packetMacs > 0)
        {
            return packetMacs;
        }
        var componentMacs = component.GetIntParameter("total_macs", 0);
        if (componentMacs > 0)
        {
            return componentMacs;
        }
        return Math.Max(1, packet.NumElements);
    }

    private static long ResolveMacPerCycle(SimComponentDef component, PrecisionKind precision)
    {
        var precisionKey = "mac_per_cycle_" + precision.ToString().ToLowerInvariant();
        var precisionThroughput = component.GetIntParameter(precisionKey, 0);
        if (precisionThroughput > 0)
        {
            return precisionThroughput;
        }
        return Math.Max(1, component.GetIntParameter("mac_per_cycle", ComponentDefaults.ProcessingElementMacPerCycle));
    }

    private Packet BuildOutputPacket(Packet packet, long cycle)
    {
        var output = PacketCatalog.ClonePacket(packet);
        output.Id = string.IsNullOrWhiteSpace(packet.Id) ? $"{component.Id}_out" : $"{packet.Id}_pe_out";
        output.SourceComponentId = component.Id;
        output.CurrentComponentId = component.Id;
        output.CreatedCycle = cycle;
        output.Metadata["total_macs"] = totalMacs.ToString(System.Globalization.CultureInfo.InvariantCulture);
        output.Metadata["mac_per_cycle"] = effectiveMacPerCycle.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (component.Parameters.TryGetValue("output_precision", out var outputPrecisionRaw) && Enum.TryParse<PrecisionKind>(outputPrecisionRaw, ignoreCase: true, out var outputPrecision))
        {
            output.Precision = outputPrecision;
        }

        if (packet.Values.Count > 0 && packet.Values.Count % 2 == 0)
        {
            var accumulator = component.GetDoubleParameter("accumulator", 0);
            for (var index = 0; index < packet.Values.Count; index += 2)
            {
                accumulator += packet.Values[index] * packet.Values[index + 1];
            }
            output.Values = [accumulator];
        }
        else
        {
            output.Values = packet.Values.ToList();
        }

        return output;
    }
}
