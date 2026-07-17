using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace HardwareSim.Core;

#pragma warning disable CS1591

public sealed record AspdacOpticalOracleResult(
    double RouteLossDb,
    double ReceivedPowerDbm,
    double ExactCaseMarginDb,
    double MatchedSystemMarginDb,
    int LogicalBits,
    int EncodedBits,
    int ServiceCycles,
    IReadOnlyList<string> ChannelIds,
    IReadOnlyList<double> WavelengthsNanometers,
    IReadOnlyList<int> CapacitySweep,
    string BerStatus,
    string CanonicalTraceSha256);

public sealed record AspdacTransportOptions(
    string WorkloadId,
    string TransportMode,
    long PacketCount,
    int FlowCount,
    int ChannelCapacity,
    int QueueDepth,
    int PayloadBits,
    string WorkloadHash,
    string MappingHash,
    string ComputeHash,
    string MemoryHash,
    string EndpointHash,
    string TransportHash);

public sealed record AspdacTransportResult(
    string WorkloadId,
    string TransportMode,
    long PacketCount,
    int FlowCount,
    int ChannelCapacity,
    int PayloadBits,
    int EncodedBitsPerPacket,
    int ServiceCyclesPerPacket,
    long TotalCycles,
    double MeanPacketLatencyCycles,
    long ConflictCount,
    long BackpressureEvents,
    long LogicalBits,
    long EncodedBits,
    double RouteLossDb,
    double ReceivedPowerDbm,
    double ReceiverMarginDb,
    double SerdesEnergyPj,
    double ConversionEnergyPj,
    double TuningEnergyPj,
    double LaserEnergyPj,
    double LinkEnergyPj,
    double EndpointEnergyPj,
    double TotalTransportEnergyPj,
    string BerStatus,
    string WorkloadHash,
    string MappingHash,
    string ComputeHash,
    string MemoryHash,
    string EndpointHash,
    string TransportHash,
    string CanonicalTraceSha256);

/// <summary>
/// Purpose-built ASP-DAC transport experiment runtime. It preserves the approved
/// Phase 8 device contracts while supporting the paper's one-to-eight-channel sweep.
/// </summary>
public static class AspdacTransportRuntime
{
    public const int LogicalPayloadBits = 128;
    public const int EncodedPayloadBits = 132;
    public const int LinkWidthBits = 128;
    public const double ElectricalLinkEnergyPerBitPj = ComponentDefaults.LinkEnergyPerBitPJ;
    public const double SerializerEnergyPerBitPj = 0.01;
    public const double ConverterEnergyPerBitPj = 0.04;
    public const double OpticalLinkEnergyPerBitPj = 0.001;
    public const double DetectorEnergyPerBitPj = 0.01;
    public const double LaserElectricalPowerMilliwatts = 10.0;
    public const double CycleTimePicoseconds = 1000.0;

    public static AspdacOpticalOracleResult RunOracle()
    {
        var loss = BuildMatchedLossBudget();
        var exactPower = OpticalPowerBudgetModel.Evaluate(new Dbm(0), loss.TotalLoss, new Dbm(-3));
        var matchedPower = OpticalPowerBudgetModel.Evaluate(new Dbm(0), loss.TotalLoss, new Dbm(-18));
        var channels = Enumerable.Range(0, 8).Select(index => $"ch{index}").ToArray();
        var wavelengths = Enumerable.Range(0, 8).Select(index => 1550.0 + index).ToArray();
        var capacities = new[] { 1, 2, 4, 8 };
        var canonical = string.Join("|",
            loss.TotalLoss.Value.ToString("R", CultureInfo.InvariantCulture),
            exactPower.ReceivedPower.Value.ToString("R", CultureInfo.InvariantCulture),
            exactPower.Margin.Value.ToString("R", CultureInfo.InvariantCulture),
            matchedPower.Margin.Value.ToString("R", CultureInfo.InvariantCulture),
            LogicalPayloadBits,
            EncodedPayloadBits,
            2,
            string.Join(",", channels),
            string.Join(",", wavelengths.Select(value => value.ToString("R", CultureInfo.InvariantCulture))),
            string.Join(",", capacities),
            "BER not modeled");

        return new AspdacOpticalOracleResult(
            loss.TotalLoss.Value,
            exactPower.ReceivedPower.Value,
            exactPower.Margin.Value,
            matchedPower.Margin.Value,
            LogicalPayloadBits,
            EncodedPayloadBits,
            2,
            channels,
            wavelengths,
            capacities,
            "BER not modeled",
            Sha256(canonical));
    }

    public static AspdacTransportResult Run(AspdacTransportOptions options)
    {
        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }
        Validate(options);

        var constrainedElectrical = string.Equals(options.TransportMode, "electrical_contended", StringComparison.Ordinal);
        var optical = options.TransportMode is "optical_conflict_free" or "optical_contended";
        var conflictFree = string.Equals(options.TransportMode, "optical_conflict_free", StringComparison.Ordinal);
        var encodedBitsPerPacket = optical ? Encode64B66B(options.PayloadBits) : options.PayloadBits;
        var serviceCycles = checked((encodedBitsPerPacket + LinkWidthBits - 1) / LinkWidthBits);
        var channelCount = optical || constrainedElectrical ? options.ChannelCapacity : 8;
        var queues = Enumerable.Range(0, options.FlowCount)
            .Select(_ => new Queue<PacketTicket>(options.QueueDepth))
            .ToArray();
        var remainingPerFlow = Distribute(options.PacketCount, options.FlowCount);
        var channels = Enumerable.Range(0, channelCount).Select(_ => new ServiceChannel()).ToArray();

        var cycle = 0L;
        var injected = 0L;
        var completed = 0L;
        var latencySum = 0.0;
        var conflicts = 0L;
        var backpressure = 0L;
        var nextPacketId = 0L;
        var roundRobin = 0;
        using var trace = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        while (completed < options.PacketCount)
        {
            for (var channelIndex = 0; channelIndex < channels.Length; channelIndex++)
            {
                var channel = channels[channelIndex];
                if (channel.Ticket is null)
                {
                    continue;
                }

                channel.RemainingCycles--;
                if (channel.RemainingCycles == 0)
                {
                    var ticket = channel.Ticket.Value;
                    completed++;
                    latencySum += cycle - ticket.InjectionCycle;
                    AppendTrace(trace, $"complete|{cycle}|{channelIndex}|{ticket.PacketId}\n");
                    channel.Ticket = null;
                }
            }

            for (var flow = 0; flow < options.FlowCount; flow++)
            {
                if (remainingPerFlow[flow] == 0)
                {
                    continue;
                }

                if (queues[flow].Count >= options.QueueDepth)
                {
                    backpressure++;
                    continue;
                }

                var ticket = new PacketTicket(nextPacketId++, flow, cycle);
                queues[flow].Enqueue(ticket);
                remainingPerFlow[flow]--;
                injected++;
                AppendTrace(trace, $"inject|{cycle}|{flow}|{ticket.PacketId}\n");
            }

            if (conflictFree)
            {
                for (var channelIndex = 0; channelIndex < channels.Length; channelIndex++)
                {
                    if (channels[channelIndex].Ticket is not null)
                    {
                        continue;
                    }

                    var flow = channelIndex % options.FlowCount;
                    if (queues[flow].Count > 0)
                    {
                        Issue(queues, channels[channelIndex], flow, channelIndex, cycle, serviceCycles, trace);
                    }
                }
            }
            else
            {
                var requests = Enumerable.Range(0, options.FlowCount).Count(flow => queues[flow].Count > 0);
                var grants = 0;
                for (var channelIndex = 0; channelIndex < channels.Length; channelIndex++)
                {
                    if (channels[channelIndex].Ticket is not null)
                    {
                        continue;
                    }

                    var flow = FindNextRequest(queues, roundRobin);
                    if (flow < 0)
                    {
                        continue;
                    }

                    Issue(queues, channels[channelIndex], flow, channelIndex, cycle, serviceCycles, trace);
                    roundRobin = (flow + 1) % options.FlowCount;
                    grants++;
                }

                if (optical)
                {
                    conflicts += Math.Max(0, requests - grants);
                }
            }

            cycle++;
            if (cycle > checked(options.PacketCount * Math.Max(8, serviceCycles * options.FlowCount)))
            {
                throw new InvalidOperationException("Transport runtime exceeded its deterministic progress bound.");
            }
        }

        if (injected != options.PacketCount)
        {
            throw new InvalidOperationException($"Injected {injected} packets but expected {options.PacketCount}.");
        }

        var logicalBits = checked(options.PacketCount * options.PayloadBits);
        var encodedBits = checked(options.PacketCount * (long)encodedBitsPerPacket);
        var loss = BuildMatchedLossBudget();
        var power = OpticalPowerBudgetModel.Evaluate(new Dbm(0), loss.TotalLoss, new Dbm(-18));
        var serdes = optical ? encodedBits * SerializerEnergyPerBitPj * 2.0 : 0.0;
        var conversion = optical ? encodedBits * ConverterEnergyPerBitPj * 2.0 : 0.0;
        var tuning = 0.0;
        var laser = optical ? cycle * LaserElectricalPowerMilliwatts * CycleTimePicoseconds / 1000.0 : 0.0;
        var link = optical ? encodedBits * OpticalLinkEnergyPerBitPj : logicalBits * ElectricalLinkEnergyPerBitPj;
        var endpoint = optical ? encodedBits * DetectorEnergyPerBitPj : 0.0;
        var total = serdes + conversion + tuning + laser + link + endpoint;
        AppendTrace(trace, $"summary|{cycle}|{completed}|{conflicts}|{backpressure}\n");
        var traceHash = ToLowerHex(trace.GetHashAndReset());

        return new AspdacTransportResult(
            options.WorkloadId,
            options.TransportMode,
            options.PacketCount,
            options.FlowCount,
            channelCount,
            options.PayloadBits,
            encodedBitsPerPacket,
            serviceCycles,
            cycle,
            latencySum / options.PacketCount,
            conflicts,
            backpressure,
            logicalBits,
            encodedBits,
            optical ? loss.TotalLoss.Value : 0.0,
            optical ? power.ReceivedPower.Value : 0.0,
            optical ? power.Margin.Value : 0.0,
            serdes,
            conversion,
            tuning,
            laser,
            link,
            endpoint,
            total,
            optical ? "BER not modeled" : "not applicable",
            options.WorkloadHash,
            options.MappingHash,
            options.ComputeHash,
            options.MemoryHash,
            options.EndpointHash,
            options.TransportHash,
            traceHash);
    }

    private static OpticalLossBudget BuildMatchedLossBudget()
    {
        var phase8 = new OpticalQuantityProvenance(
            OpticalProvenanceSources.Phase8ContractDefault,
            "Frozen Phase 8 ASP-DAC optical oracle contract.");
        return OpticalLossModel.Calculate(
            1.0,
            OpticalWaveguideMaterial.SiliconNitride,
            1,
            0,
            0,
            new[]
            {
                OpticalLossModel.DeviceInsertion("splitter", 1, OpticalLossDefaults.OneToTwoSplitterDb, "rq4-splitter", phase8),
                OpticalLossModel.DeviceInsertion("mrr", 1, OpticalLossDefaults.MrrInsertionDb, "rq4-mrr", phase8)
            });
    }

    private static int Encode64B66B(int payloadBits)
    {
        var blocks = checked((payloadBits + 63) / 64);
        return checked(blocks * 66);
    }

    private static long[] Distribute(long packets, int flows)
    {
        var result = new long[flows];
        for (var flow = 0; flow < flows; flow++)
        {
            result[flow] = packets / flows + (flow < packets % flows ? 1 : 0);
        }
        return result;
    }

    private static int FindNextRequest(IReadOnlyList<Queue<PacketTicket>> queues, int start)
    {
        for (var offset = 0; offset < queues.Count; offset++)
        {
            var index = (start + offset) % queues.Count;
            if (queues[index].Count > 0)
            {
                return index;
            }
        }
        return -1;
    }

    private static void Issue(
        IReadOnlyList<Queue<PacketTicket>> queues,
        ServiceChannel channel,
        int flow,
        int channelIndex,
        long cycle,
        int serviceCycles,
        IncrementalHash trace)
    {
        var ticket = queues[flow].Dequeue();
        channel.Ticket = ticket;
        channel.RemainingCycles = serviceCycles;
        AppendTrace(trace, $"issue|{cycle}|{channelIndex}|{flow}|{ticket.PacketId}|{serviceCycles}\n");
    }

    private static void Validate(AspdacTransportOptions options)
    {
        if (options.TransportMode is not ("electrical" or "electrical_contended" or "optical_conflict_free" or "optical_contended"))
        {
            throw new ArgumentOutOfRangeException(nameof(options.TransportMode), options.TransportMode, "Unsupported transport mode.");
        }
        if (options.PacketCount <= 0 || options.FlowCount <= 0 || options.QueueDepth <= 0 || options.PayloadBits <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Packet count, flow count, queue depth, and payload bits must be positive.");
        }
        if (options.ChannelCapacity is not (1 or 2 or 4 or 8))
        {
            throw new ArgumentOutOfRangeException(nameof(options.ChannelCapacity), options.ChannelCapacity, "Capacity must be 1, 2, 4, or 8.");
        }
        if (string.Equals(options.TransportMode, "optical_conflict_free", StringComparison.Ordinal) && options.FlowCount > options.ChannelCapacity)
        {
            throw new ArgumentException("Conflict-free optical transport requires at least one channel per flow.", nameof(options));
        }
    }

    private static void AppendTrace(IncrementalHash trace, string value) =>
        trace.AppendData(Encoding.UTF8.GetBytes(value));

    private static string Sha256(string value)
    {
        using var sha = SHA256.Create();
        return ToLowerHex(sha.ComputeHash(Encoding.UTF8.GetBytes(value)));
    }

    private static string ToLowerHex(IEnumerable<byte> bytes) =>
        string.Concat(bytes.Select(value => value.ToString("x2", CultureInfo.InvariantCulture)));

    private readonly record struct PacketTicket(long PacketId, int Flow, long InjectionCycle);

    private sealed class ServiceChannel
    {
        public PacketTicket? Ticket { get; set; }
        public int RemainingCycles { get; set; }
    }
}

#pragma warning restore CS1591
