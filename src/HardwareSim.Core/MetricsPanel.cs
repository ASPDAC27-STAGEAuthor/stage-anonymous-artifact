using System.Globalization;
using System.Text;

namespace HardwareSim.Core;

/// <summary>Identifies one stable metrics table exposed by the replay metrics panel.</summary>
public enum MetricsTableKind
{
    /// <summary>Global run-level metrics.</summary>
    Global,
    /// <summary>Per-component metrics.</summary>
    Component,
    /// <summary>Per-link metrics.</summary>
    Link
}

/// <summary>Identifies the requested metrics table sort direction.</summary>
public enum MetricsSortDirection
{
    /// <summary>Sort from low to high or A to Z.</summary>
    Ascending,
    /// <summary>Sort from high to low or Z to A.</summary>
    Descending
}

/// <summary>Describes one stable metrics table column.</summary>
/// <param name="Name">Stable machine-readable column name.</param>
/// <param name="Unit">Stable column unit, or an empty string for identifiers.</param>
/// <param name="Description">Human-readable column meaning.</param>
public sealed record MetricsColumn(string Name, string Unit, string Description);

/// <summary>Represents one rendered metrics cell with a display value and optional numeric sort value.</summary>
/// <param name="Value">Stable display and CSV value.</param>
/// <param name="NumericValue">Optional numeric value used for numeric sorting.</param>
public sealed record MetricsCell(string Value, double? NumericValue = null);

/// <summary>Represents one row in a stable metrics panel table.</summary>
/// <param name="RowId">Stable row identifier.</param>
/// <param name="Cells">Cells aligned with the owning table columns.</param>
public sealed record MetricsTableRow(string RowId, IReadOnlyList<MetricsCell> Cells);

/// <summary>Represents one stable metrics panel table with sortable rows.</summary>
public sealed class MetricsPanelTable
{
    /// <summary>Gets the metrics panel schema version that produced this table.</summary>
    public string SchemaVersion { get; init; } = MetricsPanelBuilder.SchemaVersion;
    /// <summary>Gets the table kind.</summary>
    public MetricsTableKind Kind { get; init; }
    /// <summary>Gets the stable table columns in display and CSV order.</summary>
    public IReadOnlyList<MetricsColumn> Columns { get; init; } = Array.Empty<MetricsColumn>();
    /// <summary>Gets the table rows aligned with <see cref="Columns" />.</summary>
    public IReadOnlyList<MetricsTableRow> Rows { get; init; } = Array.Empty<MetricsTableRow>();
    /// <summary>Gets the column name used for the current ordering, or an empty string for natural order.</summary>
    public string SortedByColumn { get; init; } = "";
    /// <summary>Gets the sort direction used for the current ordering.</summary>
    public MetricsSortDirection SortDirection { get; init; } = MetricsSortDirection.Ascending;

    /// <summary>Returns a new table ordered by the requested stable column name.</summary>
    public MetricsPanelTable SortBy(string columnName, MetricsSortDirection direction)
    {
        if (string.IsNullOrWhiteSpace(columnName))
        {
            throw new ArgumentException("Metrics sort column is required.", nameof(columnName));
        }

        var columnIndex = -1;
        for (var index = 0; index < Columns.Count; index++)
        {
            if (string.Equals(Columns[index].Name, columnName, StringComparison.OrdinalIgnoreCase))
            {
                columnIndex = index;
                break;
            }
        }

        if (columnIndex < 0)
        {
            throw new ArgumentException($"Metrics column not found: {columnName}.", nameof(columnName));
        }

        var numeric = Rows.Any(row => row.Cells.Count > columnIndex && row.Cells[columnIndex].NumericValue.HasValue);
        IEnumerable<MetricsTableRow> ordered;
        if (numeric)
        {
            ordered = direction == MetricsSortDirection.Descending
                ? Rows.OrderByDescending(row => NumericSortValue(row, columnIndex)).ThenBy(row => row.RowId, StringComparer.Ordinal)
                : Rows.OrderBy(row => NumericSortValue(row, columnIndex)).ThenBy(row => row.RowId, StringComparer.Ordinal);
        }
        else
        {
            ordered = direction == MetricsSortDirection.Descending
                ? Rows.OrderByDescending(row => TextSortValue(row, columnIndex), StringComparer.Ordinal).ThenBy(row => row.RowId, StringComparer.Ordinal)
                : Rows.OrderBy(row => TextSortValue(row, columnIndex), StringComparer.Ordinal).ThenBy(row => row.RowId, StringComparer.Ordinal);
        }

        return new MetricsPanelTable
        {
            SchemaVersion = SchemaVersion,
            Kind = Kind,
            Columns = Columns,
            Rows = ordered.ToList(),
            SortedByColumn = Columns[columnIndex].Name,
            SortDirection = direction
        };
    }

    private static double NumericSortValue(MetricsTableRow row, int columnIndex) =>
        row.Cells.Count > columnIndex ? row.Cells[columnIndex].NumericValue ?? 0 : 0;

    private static string TextSortValue(MetricsTableRow row, int columnIndex) =>
        row.Cells.Count > columnIndex ? row.Cells[columnIndex].Value : "";
}

/// <summary>Represents all stable metrics tables shown by the replay metrics panel.</summary>
/// <param name="SchemaVersion">Metrics panel schema version.</param>
/// <param name="Global">Global metrics table.</param>
/// <param name="Components">Component metrics table.</param>
/// <param name="Links">Link metrics table.</param>
/// <param name="Summary">Compact run summary.</param>
public sealed record MetricsPanelSnapshot(
    string SchemaVersion,
    MetricsPanelTable Global,
    MetricsPanelTable Components,
    MetricsPanelTable Links,
    string Summary);

/// <summary>Builds stable metrics panel tables from simulation metrics.</summary>
public static class MetricsPanelBuilder
{
    /// <summary>Defines the stable metrics panel and CSV schema version.</summary>
    public const string SchemaVersion = "metrics-panel-1.0";

    /// <summary>Builds global, component, and link tables from the supplied metrics.</summary>
    public static MetricsPanelSnapshot Build(SimulationMetrics? metrics)
    {
        metrics ??= new SimulationMetrics();
        var global = BuildGlobalTable(metrics.Global);
        var components = BuildComponentTable(metrics.Components.Values);
        var links = BuildLinkTable(metrics.Links.Values, metrics.Global.TotalCycles);
        var summary = $"schema={SchemaVersion} globalRows={global.Rows.Count} componentRows={components.Rows.Count} linkRows={links.Rows.Count}";
        return new MetricsPanelSnapshot(SchemaVersion, global, components, links, summary);
    }

    private static MetricsPanelTable BuildGlobalTable(GlobalMetrics metrics)
    {
        var rows = new List<MetricsTableRow>
        {
            GlobalRow("total_cycles", metrics.TotalCycles, "cycles"),
            GlobalRow("packets_injected", metrics.PacketsInjected, "packets"),
            GlobalRow("packets_delivered", metrics.PacketsDelivered, "packets"),
            GlobalRow("flits_injected", metrics.FlitsInjected, "flits"),
            GlobalRow("flits_delivered", metrics.FlitsDelivered, "flits"),
            GlobalRow("total_energy_pj", metrics.TotalEnergy, "pJ"),
            GlobalRow("compute_energy_pj", metrics.ComputeEnergy, "pJ"),
            GlobalRow("noc_energy_pj", metrics.NoCEnergy, "pJ"),
            GlobalRow("conversion_energy_pj", metrics.ConversionEnergy, "pJ"),
            GlobalRow("optical_energy_pj", metrics.OpticalEnergy, "pJ"),
            GlobalRow("total_area_um2", metrics.TotalAreaUm2.Value, "um2"),
            GlobalRow("average_utilization_ratio", metrics.AverageUtilization, "ratio"),
            GlobalRow("area_weighted_utilization_ratio", metrics.AreaWeightedUtilization, "ratio"),
            GlobalRow("pe_only_utilization_ratio", metrics.PeOnlyUtilization, "ratio"),
            GlobalRow("router_only_utilization_ratio", metrics.RouterOnlyUtilization, "ratio"),
            GlobalRow("average_throughput_packets_per_cycle", metrics.AverageThroughputPacketsPerCycle, "packets/cycle")
        };

        foreach (var pair in metrics.NamedMetrics.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            rows.Add(GlobalRow(
                "named_metric:" + pair.Key,
                pair.Value.Value,
                pair.Value.Units));
        }

        return new MetricsPanelTable
        {
            Kind = MetricsTableKind.Global,
            Columns =
            [
                new MetricsColumn("metric", "", "Stable global metric name."),
                new MetricsColumn("value", "mixed", "Metric value."),
                new MetricsColumn("unit", "", "Metric unit.")
            ],
            Rows = rows
        };
    }

    private static MetricsPanelTable BuildComponentTable(IEnumerable<ComponentMetrics> components)
    {
        var rows = components
            .OrderBy(component => component.ComponentId, StringComparer.Ordinal)
            .Select(component => new MetricsTableRow(component.ComponentId,
            [
                Text(component.ComponentId),
                Number(component.ActiveCycles),
                Number(component.IdleCycles),
                Number(component.StallCycles),
                Number(component.Utilization),
                Number(component.InputTrafficBits),
                Number(component.OutputTrafficBits),
                Number(component.AverageOccupancyBits),
                Number(component.PeakOccupancyBits),
                Number(component.MemoryUsedBits),
                Number(component.MemoryCapacityBits),
                Number(component.Energy),
                Number(component.AreaUm2.Value),
                Text(DominantStallReason(component))
            ]))
            .ToList();

        return new MetricsPanelTable
        {
            Kind = MetricsTableKind.Component,
            Columns =
            [
                new MetricsColumn("component_id", "", "Stable component identifier."),
                new MetricsColumn("active_cycles", "cycles", "Cycles classified as active."),
                new MetricsColumn("idle_cycles", "cycles", "Cycles classified as idle."),
                new MetricsColumn("stall_cycles", "cycles", "Cycles classified as stalled."),
                new MetricsColumn("utilization_ratio", "ratio", "Active cycles divided by classified cycles."),
                new MetricsColumn("input_traffic_bits", "bits", "Bits received by the component."),
                new MetricsColumn("output_traffic_bits", "bits", "Bits emitted by the component."),
                new MetricsColumn("average_occupancy_bits", "bits", "Mean sampled bit occupancy."),
                new MetricsColumn("peak_occupancy_bits", "bits", "Peak sampled bit occupancy."),
                new MetricsColumn("memory_used_bits", "bits", "Final memory usage in bits."),
                new MetricsColumn("memory_capacity_bits", "bits", "Configured memory capacity in bits."),
                new MetricsColumn("energy_pj", "pJ", "Component energy in picojoules."),
                new MetricsColumn("area_um2", "um2", "Component physical area in square micrometers."),
                new MetricsColumn("dominant_stall_reason", "enum", "Highest-count stall reason.")
            ],
            Rows = rows
        };
    }

    private static MetricsPanelTable BuildLinkTable(IEnumerable<LinkMetrics> links, long globalCycles)
    {
        var rows = links
            .OrderBy(link => link.LinkId, StringComparer.Ordinal)
            .Select(link => new MetricsTableRow(link.LinkId,
            [
                Text(link.LinkId),
                Number(link.TotalBitsTransferred),
                Number(link.BusyCycles),
                Number(link.TotalCycles),
                Number(link.CongestionCycles),
                Number(link.BackpressureCycles),
                Number(link.FlitsTransferred),
                Number(link.SerializationBitsSent),
                Number(link.Utilization(link.TotalCycles > 0 ? link.TotalCycles : globalCycles)),
                Number(link.Energy)
            ]))
            .ToList();

        return new MetricsPanelTable
        {
            Kind = MetricsTableKind.Link,
            Columns =
            [
                new MetricsColumn("link_id", "", "Stable link identifier."),
                new MetricsColumn("total_bits_transferred", "bits", "Bits transferred across the link."),
                new MetricsColumn("busy_cycles", "cycles", "Cycles with link activity."),
                new MetricsColumn("total_cycles", "cycles", "Sampled link cycles."),
                new MetricsColumn("congestion_cycles", "cycles", "Cycles where the link was congested."),
                new MetricsColumn("backpressure_cycles", "cycles", "Cycles blocked by downstream flow control."),
                new MetricsColumn("flits_transferred", "flits", "Flits transferred across the link."),
                new MetricsColumn("serialization_bits_sent", "bits", "Bits emitted by the serializer."),
                new MetricsColumn("utilization_ratio", "ratio", "Busy cycles divided by sampled cycles."),
                new MetricsColumn("energy_pj", "pJ", "Link energy in picojoules.")
            ],
            Rows = rows
        };
    }

    private static MetricsTableRow GlobalRow(string name, long value, string unit) => new(name,
    [
        Text(name),
        Number(value),
        Text(unit)
    ]);

    private static MetricsTableRow GlobalRow(string name, double value, string unit) => new(name,
    [
        Text(name),
        Number(value),
        Text(unit)
    ]);

    private static MetricsCell Text(string value) => new(value ?? "");

    private static MetricsCell Number(long value) => new(value.ToString(CultureInfo.InvariantCulture), value);

    private static MetricsCell Number(double value) => new(value.ToString("G17", CultureInfo.InvariantCulture), value);

    private static string DominantStallReason(ComponentMetrics component)
    {
        var dominant = component.StallCyclesByReason
            .OrderByDescending(pair => pair.Value)
            .ThenBy(pair => pair.Key.ToString(), StringComparer.Ordinal)
            .FirstOrDefault();
        return dominant.Value > 0 ? dominant.Key.ToString() : "";
    }
}

/// <summary>Exports metrics panel tables to stable CSV text.</summary>
public static class MetricsPanelCsvExporter
{
    /// <summary>Exports one metrics panel table with schema and unit metadata.</summary>
    public static string ExportTable(MetricsPanelTable table)
    {
        if (table is null)
        {
            throw new ArgumentNullException(nameof(table));
        }

        var lines = new List<string>
        {
            $"# schema_version,{Escape(table.SchemaVersion)}",
            $"# table,{Escape(TableName(table.Kind))}",
            $"# units,{string.Join(",", table.Columns.Select(column => Escape(column.Unit)))}",
            string.Join(",", table.Columns.Select(column => Escape(column.Name)))
        };
        lines.AddRange(table.Rows.Select(row => string.Join(",", row.Cells.Select(cell => Escape(cell.Value)))));
        return string.Join("\n", lines) + "\n";
    }

    /// <summary>Exports all metrics panel tables in global, component, then link order.</summary>
    public static string ExportAll(MetricsPanelSnapshot snapshot)
    {
        if (snapshot is null)
        {
            throw new ArgumentNullException(nameof(snapshot));
        }

        return ExportTable(snapshot.Global) + "\n" + ExportTable(snapshot.Components) + "\n" + ExportTable(snapshot.Links);
    }

    private static string TableName(MetricsTableKind kind) => kind switch
    {
        MetricsTableKind.Global => "global",
        MetricsTableKind.Component => "component",
        MetricsTableKind.Link => "link",
        _ => kind.ToString().ToLowerInvariant()
    };

    private static string Escape(string value)
    {
        value ??= "";
        if (value.Contains("\"", StringComparison.Ordinal) ||
            value.Contains(",", StringComparison.Ordinal) ||
            value.Contains("\n", StringComparison.Ordinal) ||
            value.Contains("\r", StringComparison.Ordinal))
        {
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        return value;
    }
}
