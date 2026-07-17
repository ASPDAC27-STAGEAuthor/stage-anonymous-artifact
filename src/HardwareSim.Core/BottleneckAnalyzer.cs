namespace HardwareSim.Core;

/// <summary>Provides bottleneck analyzer operations for hardware design and simulation workflows.</summary>
public static class BottleneckAnalyzer
{
    /// <summary>Defines the stable structured bottleneck analyzer schema version.</summary>
    public const string SchemaVersion = "bottleneck-analyzer-1.0";

    /// <summary>Gets the exact bottleneck types supported by the Phase 4 analyzer.</summary>
    public static IReadOnlyList<string> SupportedTypes { get; } =
    [
        "Compute",
        "Memory",
        "Network",
        "Buffer",
        "MappingImbalance",
        "SynchronizationDependency",
        "PrecisionConversion",
        "EOOEConversion",
        "OpticalWavelength",
        "OpticalPowerBudget",
        "CIM_ADCDAC",
        "CIM_WriteUpdate"
    ];

    private static readonly IReadOnlyDictionary<string, int> TypeRank = SupportedTypes
        .Select((type, index) => new { type, index })
        .ToDictionary(item => item.type, item => item.index, StringComparer.Ordinal);

    /// <summary>Analyzes the supplied metrics and returns structured findings.</summary>
    public static BottleneckReport Analyze(SimulationMetrics metrics)
    {
        if (metrics is null)
        {
            throw new ArgumentNullException(nameof(metrics));
        }

        var topFindings = BuildTopFindings(metrics);
        var congestedLink = metrics.Links.Values
            .OrderByDescending(l => l.CongestionCycles)
            .ThenByDescending(l => l.TotalBitsTransferred)
            .FirstOrDefault(l => l.CongestionCycles > 0);
        var suggestions = BuildSuggestions(metrics, congestedLink);

        if (congestedLink is not null)
        {
            return new BottleneckReport
            {
                AnalyzerSchemaVersion = SchemaVersion,
                MainBottleneck = $"Link {congestedLink.LinkId} experienced {congestedLink.CongestionCycles} congested cycles.",
                Cause = "At least one upstream component tried to send while the link was unavailable due to in-flight traffic, router arbitration conflict, or downstream resource backpressure.",
                SuggestedFix = suggestions.FirstOrDefault()?.Recommendation ??
                               "Increase link bandwidth, reduce link latency, add buffering, or route traffic through an alternate path.",
                TopFindings = topFindings,
                Suggestions = suggestions
            };
        }

        var stalledComponent = metrics.Components.Values
            .OrderByDescending(c => c.StallCycles)
            .FirstOrDefault(c => c.StallCycles > 0);

        if (stalledComponent is not null)
        {
            var dominantReason = stalledComponent.StallCyclesByReason
                .OrderByDescending(kvp => kvp.Value)
                .FirstOrDefault();
            var reasonText = dominantReason.Value > 0
                ? $" Dominant reason: {dominantReason.Key} ({dominantReason.Value} cycles)."
                : "";
            return new BottleneckReport
            {
                AnalyzerSchemaVersion = SchemaVersion,
                MainBottleneck = $"Component {stalledComponent.ComponentId} stalled for {stalledComponent.StallCycles} cycles.",
                Cause = $"The component could not forward ready work because a dependency, route, or output link was unavailable.{reasonText}",
                SuggestedFix = suggestions.FirstOrDefault()?.Recommendation ??
                               "Inspect the component route, required ports, and downstream bandwidth.",
                TopFindings = topFindings,
                Suggestions = suggestions
            };
        }

        var hottestLink = metrics.Links.Values.OrderByDescending(l => l.TotalBitsTransferred).FirstOrDefault();
        if (hottestLink is not null && hottestLink.TotalBitsTransferred > 0)
        {
            return new BottleneckReport
            {
                AnalyzerSchemaVersion = SchemaVersion,
                MainBottleneck = $"Highest traffic link: {hottestLink.LinkId} transferred {hottestLink.TotalBitsTransferred} bits.",
                Cause = "No congestion was detected, but this link carries the most traffic in the trace.",
                SuggestedFix = "Use this link as the first inspection point when scaling workload size or adding parallel traffic.",
                TopFindings = topFindings,
                Suggestions =
                [
                    new(
                        "inspect_high_traffic_link",
                        hottestLink.LinkId,
                        $"Link transferred {hottestLink.TotalBitsTransferred} bits without congestion.",
                        "Use this link as the first inspection point when scaling workload size or adding parallel traffic.")
                ]
            };
        }

        if (topFindings.Count > 0)
        {
            var first = topFindings[0];
            return new BottleneckReport
            {
                AnalyzerSchemaVersion = SchemaVersion,
                MainBottleneck = $"{first.Type} bottleneck at {first.Location}.",
                Cause = first.Cause,
                SuggestedFix = first.Suggestion,
                TopFindings = topFindings,
                Suggestions = suggestions
            };
        }

        return new BottleneckReport { AnalyzerSchemaVersion = SchemaVersion };
    }

    private static List<BottleneckFinding> BuildTopFindings(SimulationMetrics metrics)
    {
        var candidates = new List<BottleneckCandidate>();
        AddCompute(candidates, metrics);
        AddMemory(candidates, metrics);
        AddNetwork(candidates, metrics);
        AddBuffer(candidates, metrics);
        AddMappingImbalance(candidates, metrics);
        AddSynchronizationDependency(candidates, metrics);
        AddPrecisionConversion(candidates, metrics);
        AddEoOeConversion(candidates, metrics);
        AddOpticalWavelength(candidates, metrics);
        AddOpticalPowerBudget(candidates, metrics);
        AddCimAdcDac(candidates, metrics);
        AddCimWriteUpdate(candidates, metrics);

        return candidates
            .Where(candidate => candidate.Score > 0)
            .GroupBy(candidate => $"{candidate.Finding.Type}:{candidate.Finding.Location}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(candidate => candidate.Score).First())
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => TypeOrder(candidate.Finding.Type))
            .ThenBy(candidate => candidate.Finding.Location, StringComparer.Ordinal)
            .Take(3)
            .Select(candidate => candidate.Finding)
            .ToList();
    }

    private static void AddCompute(List<BottleneckCandidate> candidates, SimulationMetrics metrics)
    {
        var component = metrics.Components.Values
            .OrderByDescending(component => component.ActiveCycles + component.Energy)
            .ThenBy(component => component.ComponentId, StringComparer.Ordinal)
            .FirstOrDefault(component => component.ActiveCycles > 0 || component.Energy > 0);
        if (component is null)
        {
            return;
        }

        var score = component.ActiveCycles + component.Energy;
        var evidence = $"component={component.ComponentId};active_cycles={component.ActiveCycles};energy_pj={Format(component.Energy)}";
        candidates.Add(Create(
            "Compute",
            component.ComponentId,
            evidence,
            score,
            $"Compute-bound component consumed {component.ActiveCycles} active cycles.",
            "The component is spending measurable cycles or energy on compute work.",
            "consider increasing compute parallelism or reducing per-packet compute work"));
    }

    private static void AddMemory(List<BottleneckCandidate> candidates, SimulationMetrics metrics)
    {
        var item = metrics.Components.Values
            .Select(component => new
            {
                Component = component,
                MemoryBusy = component.StallCyclesByReason.GetValueOrDefault(StallReason.MemoryBusy),
                Outstanding = Math.Max(0, component.MemoryRequestsIssued - component.MemoryRequestsCompleted),
                ServicedBits = component.ReadBitsServiced + component.WriteBitsServiced
            })
            .Select(item => new { item.Component, Score = item.MemoryBusy * 20 + item.Outstanding * 10 + item.ServicedBits / 256.0, item.MemoryBusy, item.Outstanding, item.ServicedBits })
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.Component.ComponentId, StringComparer.Ordinal)
            .FirstOrDefault(item => item.Score > 0);
        if (item is null)
        {
            return;
        }

        var evidence = $"component={item.Component.ComponentId};memory_busy_cycles={item.MemoryBusy};outstanding_memory_requests={item.Outstanding};serviced_bits={item.ServicedBits}";
        candidates.Add(Create(
            "Memory",
            item.Component.ComponentId,
            evidence,
            item.Score,
            $"Memory path pressure score {Format(item.Score)}.",
            "Memory service or bank availability is limiting progress.",
            "increase memory banks, ports, or rebalance memory traffic"));
    }

    private static void AddNetwork(List<BottleneckCandidate> candidates, SimulationMetrics metrics)
    {
        var link = metrics.Links.Values
            .Select(link => new
            {
                Link = link,
                Score = link.CongestionCycles * 100 + link.BackpressureCycles * 50 + link.BusyCycles * 2 + link.TotalBitsTransferred / 512.0
            })
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.Link.LinkId, StringComparer.Ordinal)
            .FirstOrDefault(item => item.Score > 0);
        if (link is null)
        {
            return;
        }

        var evidence = $"link={link.Link.LinkId};congestion_cycles={link.Link.CongestionCycles};backpressure_cycles={link.Link.BackpressureCycles};total_bits_transferred={link.Link.TotalBitsTransferred}";
        candidates.Add(Create(
            "Network",
            link.Link.LinkId,
            evidence,
            link.Score,
            $"Network pressure score {Format(link.Score)}.",
            "The link is carrying hot traffic, backpressure, or congestion.",
            "increase link bandwidth, reduce latency, or add an alternate route"));
    }

    private static void AddBuffer(List<BottleneckCandidate> candidates, SimulationMetrics metrics)
    {
        var item = metrics.Components.Values
            .Select(component => new
            {
                Component = component,
                OutputFull = component.StallCyclesByReason.GetValueOrDefault(StallReason.OutputBufferFull),
                Score = component.StallCyclesByReason.GetValueOrDefault(StallReason.OutputBufferFull) * 20 +
                        component.FlitsStalled * 10 +
                        component.PeakOccupancyBits / 64.0 +
                        component.AverageOccupancyBits / 64.0
            })
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.Component.ComponentId, StringComparer.Ordinal)
            .FirstOrDefault(item => item.Score > 0);
        if (item is null)
        {
            return;
        }

        var evidence = $"component={item.Component.ComponentId};output_buffer_full_cycles={item.OutputFull};peak_occupancy_bits={item.Component.PeakOccupancyBits};flits_stalled={item.Component.FlitsStalled}";
        candidates.Add(Create(
            "Buffer",
            item.Component.ComponentId,
            evidence,
            item.Score,
            $"Buffer pressure score {Format(item.Score)}.",
            "Queue capacity or local buffering is limiting forward progress.",
            "increase queue capacity or reduce upstream burst pressure"));
    }

    private static void AddMappingImbalance(List<BottleneckCandidate> candidates, SimulationMetrics metrics)
    {
        var components = metrics.Components.Values.Where(component => component.ActiveCycles > 0 || component.IdleCycles > 0 || component.StallCycles > 0).ToList();
        if (components.Count < 2)
        {
            return;
        }

        var average = components.Average(component => component.ActiveCycles);
        var hottest = components.OrderByDescending(component => component.ActiveCycles).ThenBy(component => component.ComponentId, StringComparer.Ordinal).First();
        var spread = hottest.ActiveCycles - average;
        if (spread <= 0)
        {
            return;
        }

        var evidence = $"component={hottest.ComponentId};active_cycles={hottest.ActiveCycles};average_active_cycles={Format(average)};component_count={components.Count}";
        var score = spread * 4;
        candidates.Add(Create(
            "MappingImbalance",
            hottest.ComponentId,
            evidence,
            score,
            $"Active-cycle imbalance {Format(spread)} cycles above average.",
            "Work is concentrated on one component while peers are less active.",
            "rebalance operation mapping or split work across more components"));
    }

    private static void AddSynchronizationDependency(List<BottleneckCandidate> candidates, SimulationMetrics metrics)
    {
        AddStallReasonCandidate(
            candidates,
            metrics,
            StallReason.DependencyNotReady,
            "SynchronizationDependency",
            "Dependency readiness stalls delayed execution.",
            "review schedule dependencies or move dependent operations closer together");
    }

    private static void AddPrecisionConversion(List<BottleneckCandidate> candidates, SimulationMetrics metrics)
    {
        var item = metrics.Components.Values
            .Select(component => new
            {
                Component = component,
                ConverterBusy = component.StallCyclesByReason.GetValueOrDefault(StallReason.PrecisionConverterBusy),
                Score = component.StallCyclesByReason.GetValueOrDefault(StallReason.PrecisionConverterBusy) * 20 + component.EnergyBreakdown.Conversion.Value + metrics.Global.ConversionEnergy
            })
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.Component.ComponentId, StringComparer.Ordinal)
            .FirstOrDefault(item => item.Score > 0);
        if (item is null && metrics.Global.ConversionEnergy <= 0)
        {
            return;
        }

        var location = item?.Component.ComponentId ?? "global";
        var converterBusy = item?.ConverterBusy ?? 0;
        var score = item?.Score ?? metrics.Global.ConversionEnergy;
        var evidence = $"location={location};precision_converter_busy_cycles={converterBusy};conversion_energy_pj={Format(metrics.Global.ConversionEnergy)}";
        candidates.Add(Create(
            "PrecisionConversion",
            location,
            evidence,
            score,
            $"Precision conversion pressure score {Format(score)}.",
            "Precision conversion work or converter contention is visible in metrics.",
            "scale converter capacity or reduce precision-conversion traffic"));
    }

    private static void AddEoOeConversion(List<BottleneckCandidate> candidates, SimulationMetrics metrics)
    {
        var component = metrics.Components.Values
            .Where(component => IsEoOe(component.ComponentId))
            .OrderByDescending(component => component.ActiveCycles + component.Energy)
            .ThenBy(component => component.ComponentId, StringComparer.Ordinal)
            .FirstOrDefault(component => component.ActiveCycles > 0 || component.Energy > 0);
        if (component is null && (metrics.Global.OpticalEnergy <= 0 || metrics.Global.ConversionEnergy <= 0))
        {
            return;
        }

        var location = component?.ComponentId ?? "global";
        var active = component?.ActiveCycles ?? 0;
        var energy = component?.Energy ?? (metrics.Global.OpticalEnergy + metrics.Global.ConversionEnergy);
        var score = (active + energy) * 2;
        var evidence = $"location={location};eo_oe_active_cycles={active};conversion_energy_pj={Format(metrics.Global.ConversionEnergy)};optical_energy_pj={Format(metrics.Global.OpticalEnergy)}";
        candidates.Add(Create(
            "EOOEConversion",
            location,
            evidence,
            score,
            $"E/O or O/E conversion pressure score {Format(score)}.",
            "Electrical-optical conversion is consuming visible cycles or energy.",
            "reduce conversion crossings or add E/O O/E adapter capacity"));
    }

    private static void AddOpticalWavelength(List<BottleneckCandidate> candidates, SimulationMetrics metrics)
    {
        AddStallReasonCandidate(
            candidates,
            metrics,
            StallReason.OpticalChannelUnavailable,
            "OpticalWavelength",
            "Optical channel sharing stalled traffic.",
            "split contending flows across wavelengths or channels");
    }

    private static void AddOpticalPowerBudget(List<BottleneckCandidate> candidates, SimulationMetrics metrics)
    {
        var linkEnergy = metrics.Links.Values
            .Where(link => IsOptical(link.LinkId))
            .OrderByDescending(link => link.Energy)
            .ThenBy(link => link.LinkId, StringComparer.Ordinal)
            .FirstOrDefault();
        var linkScore = linkEnergy?.Energy ?? 0;
        var score = metrics.Global.OpticalEnergy + linkScore;
        if (score <= 0)
        {
            return;
        }

        var location = linkEnergy?.LinkId ?? "global";
        var evidence = $"location={location};optical_energy_pj={Format(metrics.Global.OpticalEnergy)};link_energy_pj={Format(linkScore)}";
        candidates.Add(Create(
            "OpticalPowerBudget",
            location,
            evidence,
            score,
            $"Optical energy pressure score {Format(score)}.",
            "Optical transfer energy indicates possible power budget pressure.",
            "reduce optical fanout, shorten routes, or add optical power budget"));
    }

    private static void AddCimAdcDac(List<BottleneckCandidate> candidates, SimulationMetrics metrics)
    {
        var component = metrics.Components.Values
            .Where(component => IsCim(component.ComponentId))
            .OrderByDescending(component => component.Energy + component.EnergyBreakdown.Conversion.Value)
            .ThenBy(component => component.ComponentId, StringComparer.Ordinal)
            .FirstOrDefault(component => component.Energy > 0 || component.EnergyBreakdown.Conversion.Value > 0);
        var cimEnergy = metrics.Global.EnergyByCategory.Cim.Value;
        if (component is null && cimEnergy <= 0)
        {
            return;
        }

        var location = component?.ComponentId ?? "global";
        var componentEnergy = component?.Energy ?? 0;
        var score = componentEnergy + cimEnergy;
        var evidence = $"location={location};cim_energy_pj={Format(cimEnergy)};component_energy_pj={Format(componentEnergy)}";
        candidates.Add(Create(
            "CIM_ADCDAC",
            location,
            evidence,
            score,
            $"CIM ADC/DAC energy pressure score {Format(score)}.",
            "CIM analog-digital conversion energy is visible in the metric breakdown.",
            "reduce ADC/DAC precision pressure or increase CIM conversion parallelism"));
    }

    private static void AddCimWriteUpdate(List<BottleneckCandidate> candidates, SimulationMetrics metrics)
    {
        var component = metrics.Components.Values
            .Where(component => IsCim(component.ComponentId) || component.WriteBitsServiced > 0)
            .OrderByDescending(component => component.WriteBitsServiced + component.MemoryRequestsIssued)
            .ThenBy(component => component.ComponentId, StringComparer.Ordinal)
            .FirstOrDefault(component => component.WriteBitsServiced > 0 || component.MemoryRequestsIssued > 0);
        if (component is null)
        {
            return;
        }

        var score = component.WriteBitsServiced / 128.0 + component.MemoryRequestsIssued;
        var evidence = $"component={component.ComponentId};write_bits_serviced={component.WriteBitsServiced};memory_requests_issued={component.MemoryRequestsIssued}";
        candidates.Add(Create(
            "CIM_WriteUpdate",
            component.ComponentId,
            evidence,
            score,
            $"CIM write/update pressure score {Format(score)}.",
            "CIM write/update traffic is consuming memory update bandwidth.",
            "batch write updates or add CIM write bandwidth"));
    }

    private static void AddStallReasonCandidate(
        List<BottleneckCandidate> candidates,
        SimulationMetrics metrics,
        StallReason reason,
        string type,
        string cause,
        string action)
    {
        var item = metrics.Components.Values
            .Select(component => new { Component = component, Cycles = component.StallCyclesByReason.GetValueOrDefault(reason) })
            .OrderByDescending(item => item.Cycles)
            .ThenBy(item => item.Component.ComponentId, StringComparer.Ordinal)
            .FirstOrDefault(item => item.Cycles > 0);
        if (item is null)
        {
            return;
        }

        var evidence = $"component={item.Component.ComponentId};stall_reason={reason};stall_cycles={item.Cycles}";
        candidates.Add(Create(
            type,
            item.Component.ComponentId,
            evidence,
            item.Cycles * 20,
            $"{reason} accounted for {item.Cycles} stall cycles.",
            cause,
            action));
    }

    private static BottleneckCandidate Create(
        string type,
        string location,
        string evidence,
        double score,
        string impact,
        string cause,
        string action)
    {
        var confidence = Math.Clamp(score / Math.Max(1.0, score + 10.0), 0.0, 1.0);
        return new BottleneckCandidate(
            score,
            new BottleneckFinding
            {
                Type = type,
                Location = location,
                Evidence = evidence,
                Impact = impact,
                Confidence = confidence,
                Cause = cause,
                Suggestion = $"{evidence}. {action}."
            });
    }

    private static List<BottleneckSuggestion> BuildSuggestions(SimulationMetrics metrics, LinkMetrics? congestedLink)
    {
        var suggestions = new List<BottleneckSuggestion>();
        if (congestedLink is not null)
        {
            suggestions.Add(new(
                "increase_link_bandwidth",
                congestedLink.LinkId,
                $"Link recorded {congestedLink.CongestionCycles} congestion cycles and {congestedLink.TotalBitsTransferred} transferred bits.",
                "Increase link bandwidth, reduce link latency, or route traffic through an alternate path."));
        }

        foreach (var component in metrics.Components.Values.OrderBy(c => c.ComponentId, StringComparer.Ordinal))
        {
            foreach (var (reason, cycles) in component.StallCyclesByReason.Where(kvp => kvp.Value > 0).OrderByDescending(kvp => kvp.Value))
            {
                suggestions.Add(SuggestionFor(component.ComponentId, reason, cycles));
            }
        }

        return suggestions
            .GroupBy(s => $"{s.Code}:{s.TargetId}", StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderBy(s => s.Code, StringComparer.Ordinal)
            .ThenBy(s => s.TargetId, StringComparer.Ordinal)
            .ToList();
    }

    private static BottleneckSuggestion SuggestionFor(string componentId, StallReason reason, long cycles) =>
        reason switch
        {
            StallReason.OutputBufferFull => new(
                "increase_output_queue_capacity",
                componentId,
                $"Component recorded {cycles} OutputBufferFull stall cycles.",
                "Increase queue_capacity on this component or reduce upstream injection pressure."),
            StallReason.LinkBusy => new(
                "increase_downstream_link_bandwidth",
                componentId,
                $"Component recorded {cycles} LinkBusy stall cycles.",
                "Increase downstream link bandwidth, reduce link latency, or add an alternate route."),
            StallReason.RouterConflict => new(
                "adjust_routing_or_arbitration_policy",
                componentId,
                $"Component recorded {cycles} RouterConflict stall cycles.",
                "Review arbitration_policy/routing_policy, add path diversity, or split contending flows."),
            StallReason.MemoryBusy => new(
                "increase_memory_parallelism",
                componentId,
                $"Component recorded {cycles} MemoryBusy stall cycles.",
                "Increase memory_ports/max_concurrent_accesses, add memory_banks, or rebalance memory traffic."),
            StallReason.OpticalChannelUnavailable => new(
                "split_optical_channel",
                componentId,
                $"Component recorded {cycles} OpticalChannelUnavailable stall cycles.",
                "Assign contending optical links to different optical_channel values or add wavelength/channel capacity."),
            StallReason.NoRoute => new(
                "add_route_or_mapping",
                componentId,
                $"Component recorded {cycles} NoRoute stall cycles.",
                "Add a valid route, update mapping, or ensure the destination is reachable."),
            StallReason.DependencyNotReady => new(
                "rebalance_schedule_dependencies",
                componentId,
                $"Component recorded {cycles} DependencyNotReady stall cycles.",
                "Review workload schedule dependencies or map dependent operations closer together."),
            StallReason.PrecisionConverterBusy => new(
                "scale_precision_conversion",
                componentId,
                $"Component recorded {cycles} PrecisionConverterBusy stall cycles.",
                "Add converter capacity or reduce precision-conversion pressure on this path."),
            _ => new(
                "inspect_component_stalls",
                componentId,
                $"Component recorded {cycles} {reason} stall cycles.",
                "Inspect component configuration, routing, and downstream resources.")
        };

    private static bool IsEoOe(string value) =>
        value.Contains("oe", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("eo", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("optical_electrical", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("electrical_optical", StringComparison.OrdinalIgnoreCase);

    private static bool IsOptical(string value) => value.Contains("optical", StringComparison.OrdinalIgnoreCase);

    private static bool IsCim(string value) => value.Contains("cim", StringComparison.OrdinalIgnoreCase);

    private static int TypeOrder(string type) => TypeRank.TryGetValue(type, out var index) ? index : int.MaxValue;

    private static string Format(double value) => value.ToString("G17", System.Globalization.CultureInfo.InvariantCulture);

    private sealed record BottleneckCandidate(double Score, BottleneckFinding Finding);
}
