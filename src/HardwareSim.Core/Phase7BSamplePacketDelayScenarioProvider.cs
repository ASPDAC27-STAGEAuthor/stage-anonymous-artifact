using System.Globalization;
using System.Text.Json;

namespace HardwareSim.Core;

internal sealed class Phase7BSamplePacketDelayScenarioProvider : IComponentKernelTestScenarioProvider
{
    public static readonly Phase7BSamplePacketDelayScenarioProvider Instance = new();

    public ComponentKernelTestScenarioProviderDescriptor Descriptor { get; } = new()
    {
        KernelId = Phase7BSamplePacketDelayKernelFactory.KernelId,
        KernelVersion = "1.0.0",
        ContractSchemaId = Phase7BSamplePacketDelayKernelFactory.SchemaId,
        ProviderVersion = "1.0.0"
    };

    public ComponentKernelTestScenario CreateScenario(CompiledComponentExecutionContract contract, int seed)
    {
        var packet = new Packet
        {
            Id = $"phase7b-delay-{seed.ToString(CultureInfo.InvariantCulture)}",
            PacketType = PacketType.Activation,
            NumElements = 4,
            BitWidth = 16,
            Bits = 64,
            Precision = PrecisionKind.FP16,
            Values =
            [
                ((seed & 15) - 8) / 4.0,
                (((seed >> 4) & 15) - 8) / 4.0,
                1.25,
                -0.5
            ]
        };
        var canonicalPacket = CanonicalPacket(packet);
        return new ComponentKernelTestScenario
        {
            ScenarioId = "phase7b.sample.packet_delay.identity",
            Seed = seed,
            MaxCycles = Math.Max(16, contract.Timing.OperationLatencyCycles + 16),
            CanonicalInputJson = ComponentExecutionJson.CanonicalizeJson(JsonSerializer.Serialize(canonicalPacket, HardwareGraphJson.Options)),
            CanonicalExpectationJson = ComponentExecutionJson.CanonicalizeJson(JsonSerializer.Serialize(new
            {
                Behavior = "identity_packet_delay",
                ExpectedPacket = canonicalPacket,
                OperationLatencyCycles = contract.Timing.OperationLatencyCycles
            }, HardwareGraphJson.Options)),
            Inputs =
            [
                new ComponentKernelTestInputTransaction
                {
                    TransactionId = "packet",
                    InputPortName = "in_packet",
                    InjectionCycle = 0,
                    Packet = packet
                }
            ],
            OutputPortNames = ["out_packet"]
        };
    }

    public ComponentKernelTestEvaluationResult EvaluateScenario(ComponentKernelTestScenario scenario, ComponentKernelTestObservation observation)
    {
        var issues = new List<ComponentTemplateIssue>();
        var expectedPacket = scenario.Inputs.Single().Packet;
        var actualPacket = observation.Simulation.DeliveredPackets.SingleOrDefault();
        var expectedHash = HashPacket(expectedPacket);
        var actualHash = actualPacket is null ? "missing" : HashPacket(actualPacket);
        if (actualPacket is null || !string.Equals(expectedHash, actualHash, StringComparison.Ordinal))
        {
            issues.Add(new(
                ComponentExecutionIssueCodes.KernelTestOutputMismatch,
                ComponentTemplateIssueSeverity.Error,
                "$.simulation.delivered_packets",
                "Packet-delay output did not preserve the deterministic packet payload and identity.",
                Phase7BSamplePacketDelayKernelFactory.KernelId));
        }

        var issueEvents = observation.ComponentEvents
            .Where(item => item.Event.Detail?.Contains("issue_cycle=", StringComparison.Ordinal) == true)
            .ToList();
        var outputEvents = observation.ComponentEvents
            .Where(item => item.Event.Detail?.Contains("output_cycle=", StringComparison.Ordinal) == true)
            .ToList();
        if (issueEvents.Count != 1 || outputEvents.Count != 1)
        {
            issues.Add(TimingIssue($"Expected one issue and one output event, observed {issueEvents.Count} issue and {outputEvents.Count} output events."));
        }
        else
        {
            var expectedOutputCycle = issueEvents[0].Cycle + Math.Max(1, observation.Profile.ExecutionContract!.Timing.OperationLatencyCycles) - 1;
            if (outputEvents[0].Cycle != expectedOutputCycle)
            {
                issues.Add(TimingIssue($"Packet-delay output cycle {outputEvents[0].Cycle} does not match compiled expectation {expectedOutputCycle}."));
            }
        }

        return new ComponentKernelTestEvaluationResult
        {
            Issues = issues.AsReadOnly(),
            ExpectedOutputHash = expectedHash,
            ActualOutputHash = actualHash
        };
    }

    private static ComponentTemplateIssue TimingIssue(string message) => new(
        ComponentExecutionIssueCodes.KernelTestTimingMismatch,
        ComponentTemplateIssueSeverity.Error,
        "$.simulation.timeline",
        message,
        Phase7BSamplePacketDelayKernelFactory.KernelId);

    private static string HashPacket(Packet packet) => ComponentExecutionJson.ComputeSha256(
        ComponentExecutionJson.CanonicalizeJson(JsonSerializer.Serialize(CanonicalPacket(packet), HardwareGraphJson.Options)));

    private static object CanonicalPacket(Packet packet) => new
    {
        packet.Id,
        packet.PacketType,
        packet.NumElements,
        packet.BitWidth,
        packet.Bits,
        packet.Precision,
        Values = packet.Values.ToList()
    };
}
