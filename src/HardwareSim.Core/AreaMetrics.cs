using System.Globalization;

namespace HardwareSim.Core;

/// <summary>Defines how editor groups participate in physical-area metrics.</summary>
public enum GroupAreaPolicy
{
    /// <summary>Groups are visual containers and contribute no additional area.</summary>
    VisualOnlyNoArea
}

/// <summary>Defines how macro instances participate in physical-area metrics.</summary>
public enum MacroAreaPolicy
{
    /// <summary>Compile macros first, then count each expanded physical leaf exactly once.</summary>
    ExpandedPhysicalLeaves
}

/// <summary>Identifies the resolved source of a component area value.</summary>
public enum AreaValueSource
{
    /// <summary>The built-in component-kind default.</summary>
    Default,
    /// <summary>An explicit component area_um2 parameter.</summary>
    ComponentOverride,
    /// <summary>A compiled imported area model.</summary>
    ImportedModel
}

/// <summary>One physical component's resolved area contribution.</summary>
/// <param name="ComponentId">Stable compiled component identifier.</param>
/// <param name="ComponentKind">Physical component kind.</param>
/// <param name="AreaUm2">Strongly typed area contribution.</param>
/// <param name="Source">Resolved value source.</param>
public sealed record ComponentAreaMetric(
    string ComponentId,
    ComponentKind ComponentKind,
    SquareMicrometers AreaUm2,
    AreaValueSource Source);

/// <summary>Contains exact component and total physical-area metrics.</summary>
public sealed class AreaAggregationResult
{
    internal AreaAggregationResult(IReadOnlyList<ComponentAreaMetric> components, SquareMicrometers totalAreaUm2)
    {
        Components = components;
        TotalAreaUm2 = totalAreaUm2;
    }

    /// <summary>Gets physical leaf contributions in stable component-id order.</summary>
    public IReadOnlyList<ComponentAreaMetric> Components { get; }

    /// <summary>Gets the sum of all physical leaf contributions.</summary>
    public SquareMicrometers TotalAreaUm2 { get; }

    /// <summary>Gets the non-counting visual-group policy.</summary>
    public GroupAreaPolicy GroupPolicy => GroupAreaPolicy.VisualOnlyNoArea;

    /// <summary>Gets the expanded-physical-leaf macro policy.</summary>
    public MacroAreaPolicy MacroPolicy => MacroAreaPolicy.ExpandedPhysicalLeaves;
}

/// <summary>Resolves and aggregates physical area from a compiled hardware graph.</summary>
public static class AreaAggregation
{
    /// <summary>Aggregates defaults, explicit overrides, and imported models without group or macro double counting.</summary>
    public static AreaAggregationResult Aggregate(HardwareSimulationGraph graph)
    {
        if (graph is null)
        {
            throw new ArgumentNullException(nameof(graph));
        }

        var components = new List<ComponentAreaMetric>();
        var total = SquareMicrometers.Zero;
        foreach (var component in graph.Components.OrderBy(item => item.Id, StringComparer.Ordinal))
        {
            if (!IsPhysical(component.Type))
            {
                continue;
            }

            var resolved = ResolveArea(graph, component);
            components.Add(new ComponentAreaMetric(component.Id, component.Type, resolved.Area, resolved.Source));
            total += resolved.Area;
        }

        return new AreaAggregationResult(components.AsReadOnly(), total);
    }

    private static bool IsPhysical(ComponentKind kind) =>
        kind is ComponentKind.ProcessingElement or ComponentKind.Router or ComponentKind.Buffer or ComponentKind.Memory;

    private static (SquareMicrometers Area, AreaValueSource Source) ResolveArea(
        HardwareSimulationGraph graph,
        SimComponentDef component)
    {
        if (component.Parameters.TryGetValue("area_um2", out var rawArea))
        {
            if (!double.TryParse(rawArea, NumberStyles.Float, CultureInfo.InvariantCulture, out var area))
            {
                throw new FormatException($"Component '{component.Id}' area_um2 must be a finite non-negative number.");
            }

            return (new SquareMicrometers(area), AreaValueSource.ComponentOverride);
        }

        AreaModel? model = null;
        if (!string.IsNullOrWhiteSpace(component.AreaModelId) &&
            graph.AreaModels.TryGetValue(component.AreaModelId, out var modelById))
        {
            model = modelById;
        }
        else if (graph.AreaModels.TryGetValue(component.Id, out var modelByComponent))
        {
            model = modelByComponent;
        }

        return model is null
            ? (ComponentDefaults.DefaultArea(component.Type), AreaValueSource.Default)
            : (new SquareMicrometers(model.AreaUm2), AreaValueSource.ImportedModel);
    }
}
