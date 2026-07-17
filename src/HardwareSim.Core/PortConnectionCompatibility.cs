namespace HardwareSim.Core;

/// <summary>Describes one non-blocking contract diagnostic for a directly connectable port pair.</summary>
/// <param name="Code">Stable diagnostic code aligned with graph validation where applicable.</param>
/// <param name="Message">Human-readable diagnostic text.</param>
public sealed record PortConnectionDiagnostic(string Code, string Message);

/// <summary>Reports direct-link eligibility plus deterministic target-selection preference.</summary>
/// <param name="IsCompatible">Whether graph validation permits a direct link without a direction, signal-domain, or protocol error.</param>
/// <param name="Code">Stable compatibility or rejection code.</param>
/// <param name="Reason">Human-readable compatibility or rejection reason.</param>
/// <param name="PreferencePenalty">Non-negative rank penalty used only after direct-link eligibility is established.</param>
/// <param name="Diagnostics">Warning-only data, precision, quantity, or unit differences.</param>
public sealed record PortConnectionEvaluation(
    bool IsCompatible,
    string Code,
    string Reason,
    int PreferencePenalty,
    IReadOnlyList<PortConnectionDiagnostic> Diagnostics);

/// <summary>Supplies deterministic UI ordering metadata for one target port candidate.</summary>
/// <param name="Port">Candidate input or bidirectional port.</param>
/// <param name="PortOrder">Stable order of the port in its owning component contract.</param>
/// <param name="AnchorY">Optional canvas-space Y coordinate of the port anchor.</param>
/// <param name="StableName">Optional stable owner-qualified name used before the local port name as a final tie-break.</param>
public sealed record PortConnectionTargetCandidate(
    PortView Port,
    int PortOrder,
    double? AnchorY = null,
    string StableName = "");

/// <summary>Contains the selected direct-link target or a deterministic rejection summary.</summary>
/// <param name="Target">Selected compatible target, or null when no direct-link target exists.</param>
/// <param name="Evaluation">Compatibility evaluation for the selected target, or null when selection failed before evaluation.</param>
/// <param name="Reason">Selection detail or deterministic rejection summary.</param>
public sealed record PortConnectionSelection(
    PortView? Target,
    PortConnectionEvaluation? Evaluation,
    string Reason)
{
    /// <summary>Gets whether a compatible target was selected.</summary>
    public bool IsSuccess => Target is not null && Evaluation?.IsCompatible == true;
}

/// <summary>
/// Evaluates direct port links using the graph validator's error boundary and deterministically selects UI targets.
/// Direction, signal domain, and protocol mismatches reject a target. Data type, precision, quantity, and units
/// remain warning-only and influence preference so the UI does not hide links that Core validation permits.
/// </summary>
public static class PortConnectionCompatibility
{
    /// <summary>Evaluates one source/target pair without component-type special cases.</summary>
    public static PortConnectionEvaluation Evaluate(PortView? source, PortView? target)
    {
        if (source is null)
        {
            return Rejected("source_port_missing", "Connection source port is missing.");
        }

        if (target is null)
        {
            return Rejected("target_port_missing", "Connection target port is missing.");
        }

        if (!CanDrive(source.Direction))
        {
            return Rejected(
                "invalid_port_direction",
                $"Source port '{source.Name}' has direction '{source.Direction}' and cannot drive a link.");
        }

        if (!CanReceive(target.Direction))
        {
            return Rejected(
                "invalid_port_direction",
                $"Target port '{target.Name}' has direction '{target.Direction}' and cannot receive a link.");
        }

        if (source.SignalType != target.SignalType)
        {
            return Rejected(
                "signal_type_mismatch",
                $"Source signal domain '{source.SignalType}' does not match target domain '{target.SignalType}'.");
        }

        if (source.Protocol != target.Protocol)
        {
            return Rejected(
                "protocol_mismatch",
                $"Source protocol '{source.Protocol}' does not match target protocol '{target.Protocol}'.");
        }

        var diagnostics = new List<PortConnectionDiagnostic>();
        var preferencePenalty = 0;

        if (source.DataType != target.DataType)
        {
            preferencePenalty += 100;
            diagnostics.Add(new PortConnectionDiagnostic(
                "data_type_mismatch",
                $"Source data type '{source.DataType}' does not match target data type '{target.DataType}'."));
        }

        if (source.Precision != target.Precision)
        {
            if (source.Precision == PrecisionKind.Any || target.Precision == PrecisionKind.Any)
            {
                // Validator treats Any as compatible, but an exact precision contract wins deterministic selection.
                preferencePenalty += 1;
            }
            else
            {
                preferencePenalty += 50;
                diagnostics.Add(new PortConnectionDiagnostic(
                    "precision_mismatch",
                    $"Source precision '{source.Precision}' does not match target precision '{target.Precision}'."));
            }
        }

        AddQuantityPreference(source.Quantity, target.Quantity, diagnostics, ref preferencePenalty);
        AddUnitPreference(source.Units, target.Units, diagnostics, ref preferencePenalty);

        var reason = diagnostics.Count == 0
            ? "Direct link is compatible."
            : "Direct link is compatible with warning-only contract differences: " +
              string.Join("; ", diagnostics.Select(diagnostic => diagnostic.Message));
        return new PortConnectionEvaluation(
            true,
            diagnostics.Count == 0 ? "direct_link_compatible" : "direct_link_compatible_with_warnings",
            reason,
            preferencePenalty,
            diagnostics.AsReadOnly());
    }

    /// <summary>
    /// Selects from plain PortView candidates using enumeration order and stable port name after contract preference.
    /// </summary>
    public static PortConnectionSelection SelectTarget(
        PortView? source,
        IEnumerable<PortView>? candidates,
        double? pointerY = null)
    {
        var projected = (candidates ?? Enumerable.Empty<PortView>())
            .Select((port, index) => new PortConnectionTargetCandidate(port, index, null, port?.Name ?? ""))
            .ToList();
        return SelectTarget(source, projected, pointerY);
    }

    /// <summary>
    /// Selects a compatible target by exact-contract preference, pointer proximity, declared port order, and stable name.
    /// </summary>
    public static PortConnectionSelection SelectTarget(
        PortView? source,
        IEnumerable<PortConnectionTargetCandidate>? candidates,
        double? pointerY = null)
    {
        if (source is null)
        {
            return new PortConnectionSelection(null, null, "Connection source port is missing.");
        }

        if (!CanDrive(source.Direction))
        {
            var evaluation = Rejected(
                "invalid_port_direction",
                $"Source port '{source.Name}' has direction '{source.Direction}' and cannot drive a link.");
            return new PortConnectionSelection(null, evaluation, evaluation.Reason);
        }

        var materialized = (candidates ?? Enumerable.Empty<PortConnectionTargetCandidate>())
            .Where(candidate => candidate is not null)
            .Select((candidate, index) => new EvaluatedCandidate(
                candidate,
                Evaluate(source, candidate.Port),
                index))
            .ToList();

        if (materialized.Count == 0)
        {
            return new PortConnectionSelection(null, null, "No target ports were supplied.");
        }

        var selected = materialized
            .Where(item => item.Evaluation.IsCompatible)
            .OrderBy(item => item.Evaluation.PreferencePenalty)
            .ThenBy(item => PointerRank(pointerY, item.Candidate.AnchorY))
            .ThenBy(item => PointerDistance(pointerY, item.Candidate.AnchorY))
            .ThenBy(item => item.Candidate.PortOrder)
            .ThenBy(item => StableKey(item.Candidate), StringComparer.Ordinal)
            .ThenBy(item => item.Candidate.Port.Name, StringComparer.Ordinal)
            .ThenBy(item => item.OriginalIndex)
            .FirstOrDefault();

        if (selected is not null)
        {
            return new PortConnectionSelection(
                selected.Candidate.Port,
                selected.Evaluation,
                $"Selected compatible target '{selected.Candidate.Port.Name}'. {selected.Evaluation.Reason}");
        }

        var rejectionSummary = string.Join(
            "; ",
            materialized
                .OrderBy(item => item.Candidate.PortOrder)
                .ThenBy(item => StableKey(item.Candidate), StringComparer.Ordinal)
                .ThenBy(item => item.Candidate.Port.Name, StringComparer.Ordinal)
                .ThenBy(item => item.OriginalIndex)
                .Select(item =>
                    $"{item.Candidate.Port.Name}: {item.Evaluation.Code} ({item.Evaluation.Reason})"));
        return new PortConnectionSelection(
            null,
            null,
            "No directly compatible target port was found. " + rejectionSummary);
    }

    /// <summary>Attempts to select a target and returns a concise UI-ready reason.</summary>
    public static bool TrySelectTarget(
        PortView? source,
        IReadOnlyList<PortView>? candidates,
        out PortView? target,
        out string reason,
        double? pointerY = null)
    {
        var selection = SelectTarget(source, candidates, pointerY);
        target = selection.Target;
        reason = selection.Reason;
        return selection.IsSuccess;
    }

    private static void AddQuantityPreference(
        string sourceQuantity,
        string targetQuantity,
        List<PortConnectionDiagnostic> diagnostics,
        ref int preferencePenalty)
    {
        var source = Normalize(sourceQuantity);
        var target = Normalize(targetQuantity);
        if (string.Equals(source, target, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (source.Length == 0 || target.Length == 0)
        {
            preferencePenalty += 2;
            diagnostics.Add(new PortConnectionDiagnostic(
                "quantity_unspecified",
                $"Quantity contract is incomplete ('{source}' -> '{target}')."));
            return;
        }

        preferencePenalty += 10;
        diagnostics.Add(new PortConnectionDiagnostic(
            "quantity_mismatch",
            $"Source quantity '{source}' does not match target quantity '{target}'."));
    }

    private static void AddUnitPreference(
        string sourceUnits,
        string targetUnits,
        List<PortConnectionDiagnostic> diagnostics,
        ref int preferencePenalty)
    {
        var source = Normalize(sourceUnits);
        var target = Normalize(targetUnits);
        if (string.Equals(source, target, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (source.Length == 0 || target.Length == 0)
        {
            preferencePenalty += 4;
            diagnostics.Add(new PortConnectionDiagnostic(
                "unit_unspecified",
                $"Unit contract is incomplete ('{source}' -> '{target}')."));
            return;
        }

        if (PhysicalUnitConverter.IsCompatible(source, target))
        {
            preferencePenalty += 1;
            diagnostics.Add(new PortConnectionDiagnostic(
                "unit_conversion",
                $"Source units '{source}' and target units '{target}' require a compatible unit conversion."));
            return;
        }

        preferencePenalty += 20;
        diagnostics.Add(new PortConnectionDiagnostic(
            "unit_mismatch",
            $"Source units '{source}' are not known to be compatible with target units '{target}'."));
    }

    private static PortConnectionEvaluation Rejected(string code, string reason) =>
        new(false, code, reason, int.MaxValue, Array.Empty<PortConnectionDiagnostic>());

    private static bool CanDrive(PortDirection direction) =>
        direction is PortDirection.Output or PortDirection.Bidirectional;

    private static bool CanReceive(PortDirection direction) =>
        direction is PortDirection.Input or PortDirection.Bidirectional;

    private static int PointerRank(double? pointerY, double? anchorY) =>
        IsFinite(pointerY) && IsFinite(anchorY) ? 0 : 1;

    private static double PointerDistance(double? pointerY, double? anchorY) =>
        IsFinite(pointerY) && IsFinite(anchorY)
            ? Math.Abs(pointerY!.Value - anchorY!.Value)
            : double.MaxValue;

    private static bool IsFinite(double? value) =>
        value.HasValue && !double.IsNaN(value.Value) && !double.IsInfinity(value.Value);

    private static string StableKey(PortConnectionTargetCandidate candidate) =>
        string.IsNullOrWhiteSpace(candidate.StableName)
            ? candidate.Port.Name
            : candidate.StableName.Trim();

    private static string Normalize(string? value) => value?.Trim() ?? "";

    private sealed record EvaluatedCandidate(
        PortConnectionTargetCandidate Candidate,
        PortConnectionEvaluation Evaluation,
        int OriginalIndex);
}
