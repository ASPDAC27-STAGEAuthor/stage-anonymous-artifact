namespace HardwareSim.Core;

/// <summary>Represents adapter insertion plan options data exchanged by hardware design and simulation workflows.</summary>
public sealed class AdapterInsertionPlanOptions
{
    /// <summary>Gets or sets the adapter id prefix value carried by the enclosing adapter insertion plan options contract.</summary>
    public string AdapterIdPrefix { get; set; } = "planned_adapter";
    /// <summary>Gets or sets whether planning reports proposed adapters without modifying the graph.</summary>
    public bool DryRun { get; set; } = true;
}

/// <summary>Represents adapter insertion plan data exchanged by hardware design and simulation workflows.</summary>
/// <param name="DryRun">Provides the dry run value carried by this contract.</param>
/// <param name="Summary">Provides the summary value carried by this contract.</param>
/// <param name="Steps">Provides the steps value carried by this contract.</param>
public sealed record AdapterInsertionPlan(
    bool DryRun,
    string Summary,
    List<AdapterInsertionStep> Steps);

/// <summary>Represents adapter insertion step data exchanged by hardware design and simulation workflows.</summary>
/// <param name="StepId">Provides the step id value carried by this contract.</param>
/// <param name="LinkId">Provides the link id value carried by this contract.</param>
/// <param name="AdapterType">Provides the adapter type value carried by this contract.</param>
/// <param name="ProposedComponentKind">Provides the proposed component kind value carried by this contract.</param>
/// <param name="ProposedAdapterComponentId">Provides the proposed adapter component id value carried by this contract.</param>
/// <param name="SourceComponentId">Provides the source component id value carried by this contract.</param>
/// <param name="SourcePortName">Provides the source port name value carried by this contract.</param>
/// <param name="DestinationComponentId">Provides the destination component id value carried by this contract.</param>
/// <param name="DestinationPortName">Provides the destination port name value carried by this contract.</param>
/// <param name="MismatchField">Provides the mismatch field value carried by this contract.</param>
/// <param name="SourceValue">Provides the source value carried by this contract.</param>
/// <param name="DestinationValue">Provides the destination value carried by this contract.</param>
/// <param name="ValidationCode">Provides the validation code value carried by this contract.</param>
/// <param name="Severity">Provides the severity value carried by this contract.</param>
/// <param name="DryRun">Provides the dry run value carried by this contract.</param>
/// <param name="Action">Provides the action value carried by this contract.</param>
public sealed record AdapterInsertionStep(
    string StepId,
    string LinkId,
    string AdapterType,
    ComponentKind ProposedComponentKind,
    string ProposedAdapterComponentId,
    string SourceComponentId,
    string SourcePortName,
    string DestinationComponentId,
    string DestinationPortName,
    string MismatchField,
    string SourceValue,
    string DestinationValue,
    string ValidationCode,
    ValidationSeverity Severity,
    bool DryRun,
    string Action);

/// <summary>Provides adapter insertion planner operations for hardware design and simulation workflows.</summary>
public static class AdapterInsertionPlanner
{
    /// <summary>Builds a deterministic plan from the supplied validation findings without mutating the graph.</summary>
    public static AdapterInsertionPlan Plan(ValidationResult validation, AdapterInsertionPlanOptions? options = null)
    {
        options ??= new AdapterInsertionPlanOptions();
        var adapterIdPrefix = string.IsNullOrWhiteSpace(options.AdapterIdPrefix)
            ? "planned_adapter"
            : options.AdapterIdPrefix.Trim();

        var suggestions = validation.Issues
            .Where(i => i.AdapterSuggestion is not null)
            .OrderBy(i => i.LinkId ?? "", StringComparer.OrdinalIgnoreCase)
            .ThenBy(i => i.AdapterSuggestion!.MismatchField, StringComparer.OrdinalIgnoreCase)
            .ThenBy(i => i.AdapterSuggestion!.AdapterType, StringComparer.OrdinalIgnoreCase)
            .ThenBy(i => i.Code, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var steps = new List<AdapterInsertionStep>();
        for (var index = 0; index < suggestions.Count; index++)
        {
            var issue = suggestions[index];
            var suggestion = issue.AdapterSuggestion!;
            var adapterKind = ComponentKindFor(suggestion.AdapterType);
            var stepId = $"adapter_plan_step_{index:000}";
            var adapterId = $"{adapterIdPrefix}_{index:000}_{NormalizeIdPart(suggestion.AdapterType)}";
            var linkId = issue.LinkId ??
                $"{suggestion.SourceComponentId}_{suggestion.SourcePortName}_to_{suggestion.DestinationComponentId}_{suggestion.DestinationPortName}";

            steps.Add(new AdapterInsertionStep(
                stepId,
                linkId,
                suggestion.AdapterType,
                adapterKind,
                adapterId,
                suggestion.SourceComponentId,
                suggestion.SourcePortName,
                suggestion.DestinationComponentId,
                suggestion.DestinationPortName,
                suggestion.MismatchField,
                suggestion.SourceValue,
                suggestion.DestinationValue,
                issue.Code,
                issue.Severity,
                options.DryRun,
                options.DryRun
                    ? $"Dry-run: propose inserting {suggestion.AdapterType} between {suggestion.SourceComponentId}.{suggestion.SourcePortName} and {suggestion.DestinationComponentId}.{suggestion.DestinationPortName}."
                    : $"Plan: insert {suggestion.AdapterType} between {suggestion.SourceComponentId}.{suggestion.SourcePortName} and {suggestion.DestinationComponentId}.{suggestion.DestinationPortName}."));
        }

        var summary = steps.Count == 0
            ? "No adapter insertion steps proposed."
            : $"Proposed {steps.Count} dry-run adapter insertion step(s): {string.Join(", ", steps.GroupBy(s => s.AdapterType).OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase).Select(g => $"{g.Key}={g.Count()}"))}.";

        return new AdapterInsertionPlan(options.DryRun, summary, steps);
    }

    private static ComponentKind ComponentKindFor(string adapterType) => adapterType.Trim().ToUpperInvariant() switch
    {
        "QUANTIZER" => ComponentKind.Quantizer,
        "DEQUANTIZER" => ComponentKind.Dequantizer,
        "PRECISION CONVERTER" => ComponentKind.PrecisionConverter,
        "E/O CONVERTER" => ComponentKind.EoConverter,
        "O/E RECEIVER" => ComponentKind.OeConverter,
        "ADC" => ComponentKind.Adc,
        "DAC" => ComponentKind.Dac,
        "PROTOCOL ADAPTER" => ComponentKind.Adapter,
        _ => ComponentKind.Adapter
    };

    private static string NormalizeIdPart(string value)
    {
        var chars = value.Trim().ToLowerInvariant()
            .Select(c => char.IsLetterOrDigit(c) ? c : '_')
            .ToArray();
        var normalized = new string(chars);
        while (normalized.Contains("__", StringComparison.Ordinal))
        {
            normalized = normalized.Replace("__", "_", StringComparison.Ordinal);
        }

        return normalized.Trim('_');
    }
}
