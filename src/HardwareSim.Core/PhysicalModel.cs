using System.Text.Json;
using System.Text.Json.Serialization;

#pragma warning disable CS1591

namespace HardwareSim.Core;

/// <summary>Represents physical point data exchanged by hardware design and simulation workflows.</summary>
/// <param name="X">Provides the x value carried by this contract.</param>
/// <param name="Y">Provides the y value carried by this contract.</param>
public sealed record PhysicalPoint(double X, double Y);

/// <summary>Defines supported physical routing media used by explicit route paths.</summary>
public enum RoutingMedium
{
    /// <summary>Uses electrical metal interconnect.</summary>
    ElectricalMetal,
    /// <summary>Uses an optical waveguide path.</summary>
    OpticalWaveguide,
    /// <summary>Uses a thermal control routing path.</summary>
    ThermalControl
}

/// <summary>Defines units for physical route path points.</summary>
public enum PhysicalRoutePointUnit
{
    /// <summary>Route path coordinates are expressed in micrometers.</summary>
    Micrometers
}

/// <summary>Defines the logical target kind carried by a physical route.</summary>
public enum PhysicalRouteTargetKind
{
    /// <summary>The route binds to one resolved logical hardware link id.</summary>
    LogicalLink,
    /// <summary>The route binds to one resolved logical collection target id.</summary>
    CollectionTarget
}

/// <summary>Represents a structured physical routing layer identifier.</summary>
public sealed class RoutingLayerId
{
    /// <summary>Gets or sets the routing layer stack family, such as M or WG.</summary>
    public string Stack { get; set; } = "M";
    /// <summary>Gets or sets the routing layer index inside the stack.</summary>
    public int Index { get; set; } = 3;
    /// <summary>Gets or sets optional layer purpose metadata.</summary>
    public string? Purpose { get; set; }
    /// <summary>Gets the compact deterministic layer id.</summary>
    [JsonIgnore]
    public string Id => Index > 0 ? $"{Stack}{Index}" : Stack;

    /// <summary>Creates a metal routing layer id.</summary>
    public static RoutingLayerId Metal(int index, string? purpose = null) => new() { Stack = "M", Index = index, Purpose = purpose };

    /// <summary>Parses a legacy compact layer id into structured layer fields.</summary>
    public static RoutingLayerId Parse(string? value)
    {
        var text = string.IsNullOrWhiteSpace(value) ? "M3" : value.Trim();
        var digitStart = 0;
        while (digitStart < text.Length && !char.IsDigit(text[digitStart]))
        {
            digitStart++;
        }

        var stack = digitStart > 0 ? text[..digitStart] : "M";
        var suffix = digitStart < text.Length ? text[digitStart..] : "";
        var index = int.TryParse(suffix, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : 0;
        if (index <= 0 && string.Equals(stack, "M", StringComparison.OrdinalIgnoreCase))
        {
            index = 3;
        }

        return new RoutingLayerId { Stack = string.IsNullOrWhiteSpace(stack) ? "M" : stack, Index = index };
    }

    /// <inheritdoc />
    public override string ToString() => Id;
}

/// <summary>Represents one placed component footprint in physical placement grid cells.</summary>
public sealed class PhysicalCellPlacement
{
    /// <summary>Gets or sets the top grid row occupied by the component footprint.</summary>
    public int Row { get; set; }
    /// <summary>Gets or sets the left grid column occupied by the component footprint.</summary>
    public int Col { get; set; }
    /// <summary>Gets or sets the footprint width in placement grid cells.</summary>
    public int WidthCells { get; set; } = 1;
    /// <summary>Gets or sets the footprint height in placement grid cells.</summary>
    public int HeightCells { get; set; } = 1;
    /// <summary>Gets or sets the physical layer carrying this footprint.</summary>
    public string Layer { get; set; } = "M0";
    /// <summary>Gets or sets the resolved continuous footprint hash.</summary>
    public string PhysicalFootprintHash { get; set; } = "";
    /// <summary>Gets or sets the true physical width, excluding placement quantization slack.</summary>
    public double PhysicalWidthUm { get; set; }
    /// <summary>Gets or sets the true physical height, excluding placement quantization slack.</summary>
    public double PhysicalHeightUm { get; set; }
    public double KeepoutLeftUm { get; set; }
    public double KeepoutRightUm { get; set; }
    public double KeepoutTopUm { get; set; }
    public double KeepoutBottomUm { get; set; }
    public double QuantizationSlackWidthUm { get; set; }
    public double QuantizationSlackHeightUm { get; set; }
    public PhysicalFootprintScope PhysicalScope { get; set; } = PhysicalFootprintScope.Core;
    public PhysicalFootprintSourceKind PhysicalSourceKind { get; set; } = PhysicalFootprintSourceKind.Unknown;
    public NormalizedDeviceEvidenceStatus PhysicalEvidenceStatus { get; set; } = NormalizedDeviceEvidenceStatus.Unknown;
    public string PhysicalUncertainty { get; set; } = "";
    public PhysicalPlacementDisplayMode DisplayMode { get; set; } = PhysicalPlacementDisplayMode.PhysicalScale;
    public bool NonProportionalRendering { get; set; }
    public bool RotationAllowed { get; set; }
    public bool Rotated { get; set; }
    public Dictionary<string, PhysicalPortAnchor> PortAnchors { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    internal IEnumerable<(int Row, int Col)> OccupiedCells()
    {
        for (var row = Row; row < Row + HeightCells; row++)
        {
            for (var col = Col; col < Col + WidthCells; col++)
            {
                yield return (row, col);
            }
        }
    }
}

/// <summary>Represents a structured placement validation issue.</summary>
public sealed record PlacementIssue(string Code, string Severity, string ComponentId, string Message);

/// <summary>Reports placement validation issues without clamping or guessing missing positions.</summary>
public sealed class PlacementReport
{
    /// <summary>Gets the ordered placement issues collected during validation.</summary>
    public List<PlacementIssue> Issues { get; } = [];
    /// <summary>Gets component ids that are present in the graph but missing explicit physical placement.</summary>
    public IReadOnlyList<string> UnplacedComponentIds => Issues
        .Where(issue => string.Equals(issue.Code, PhysicalPlacement.UnplacedIssueCode, StringComparison.Ordinal))
        .Select(issue => issue.ComponentId)
        .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
        .ToList();
    /// <summary>Gets whether validation found any placement error.</summary>
    public bool HasErrors => Issues.Any(issue => string.Equals(issue.Severity, "error", StringComparison.OrdinalIgnoreCase));
}

/// <summary>Summarizes a placement edit attempt.</summary>
public sealed record PlacementEditResult(bool IsSuccess, IReadOnlyList<PlacementIssue> Issues)
{
    /// <summary>Gets the reusable successful placement edit result.</summary>
    public static PlacementEditResult Success { get; } = new(true, []);
}

/// <summary>Represents physical placement data exchanged by hardware design and simulation workflows.</summary>
public sealed class PhysicalPlacement
{
    /// <summary>Defines the current physical placement schema version.</summary>
    public const string CurrentSchemaVersion = "1.0";
    /// <summary>Identifies placement edits rejected because a footprint exceeds grid bounds.</summary>
    public const string OutOfBoundsIssueCode = "placement_out_of_bounds";
    /// <summary>Identifies placement edits rejected because two footprints overlap.</summary>
    public const string OverlapIssueCode = "placement_overlap";
    /// <summary>Identifies graph components that have no explicit physical placement.</summary>
    public const string UnplacedIssueCode = "placement_unplaced_component";

    /// <summary>Gets or sets the placement schema version.</summary>
    [JsonPropertyName("schema_version")]
    public string SchemaVersion { get; set; } = CurrentSchemaVersion;
    /// <summary>Gets or sets the number of rows in the physical placement grid; zero means unbounded.</summary>
    public int Rows { get; set; }
    /// <summary>Gets or sets the number of columns in the physical placement grid; zero means unbounded.</summary>
    public int Cols { get; set; }
    /// <summary>Gets or sets the physical width of one placement cell in micrometers.</summary>
    public double CellWidthMicrometers { get; set; } = 100;
    /// <summary>Gets or sets the physical height of one placement cell in micrometers.</summary>
    public double CellHeightMicrometers { get; set; } = 100;
    /// <summary>Gets or sets the physical coordinate of grid row zero and column zero.</summary>
    public PhysicalPoint Origin { get; set; } = new(0, 0);
    /// <summary>Gets or sets the default physical layer for placement footprints.</summary>
    public string Layer { get; set; } = "M0";
    /// <summary>Gets or sets the placement rendering mode.</summary>
    public PhysicalPlacementDisplayMode DisplayMode { get; set; } = PhysicalPlacementDisplayMode.PhysicalScale;
    /// <summary>Gets or sets the readable minimum cell span used only in non-proportional mode.</summary>
    public int ReadableMinimumCells { get; set; } = 2;
    /// <summary>Gets or sets editor and floorplan metadata that does not change placement geometry.</summary>
    public Dictionary<string, string> FloorplanMetadata { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Gets or sets the legacy square grid cell size; setting it updates width and height together.</summary>
    public double GridCellMicrometers
    {
        get => Math.Abs(CellWidthMicrometers - CellHeightMicrometers) < 0.000000001
            ? CellWidthMicrometers
            : Math.Max(CellWidthMicrometers, CellHeightMicrometers);
        set
        {
            CellWidthMicrometers = value;
            CellHeightMicrometers = value;
        }
    }

    /// <summary>Gets or sets explicit component footprints keyed by component id.</summary>
    public Dictionary<string, PhysicalCellPlacement> ComponentCells { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Gets or sets legacy explicit component positions keyed by component id.</summary>
    public Dictionary<string, PhysicalPoint> ComponentPositions { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Gets or sets unknown JSON properties preserved for forward-compatible roundtrips.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement> ExtensionData { get; set; } = new(StringComparer.Ordinal);

    /// <summary>Attempts to place or replace one component footprint on the physical grid.</summary>
    public PlacementEditResult PlaceComponent(string componentId, int row, int col, int widthCells = 1, int heightCells = 1, string? layer = null)
    {
        if (string.IsNullOrWhiteSpace(componentId))
        {
            throw new ArgumentException("Component id is required.", nameof(componentId));
        }

        var normalizedId = componentId.Trim();
        var placement = new PhysicalCellPlacement
        {
            Row = row,
            Col = col,
            WidthCells = widthCells,
            HeightCells = heightCells,
            Layer = string.IsNullOrWhiteSpace(layer) ? Layer : layer!.Trim()
        };
        if (ComponentCells.TryGetValue(normalizedId, out var existing) && !string.IsNullOrWhiteSpace(existing.PhysicalFootprintHash))
        {
            CopyPhysicalMetadata(existing, placement);
        }
        var issues = ValidateCandidate(normalizedId, placement).ToList();
        if (issues.Count > 0)
        {
            return new PlacementEditResult(false, issues);
        }

        ComponentCells[normalizedId] = placement;
        ComponentPositions[normalizedId] = PositionForCell(placement);
        return PlacementEditResult.Success;
    }

    /// <summary>Projects a resolved continuous footprint into placement cells without changing physical truth.</summary>
    public PlacementEditResult PlaceResolvedFootprint(
        string componentId,
        int row,
        int col,
        PhysicalFootprint footprint,
        string? layer = null,
        bool rotate = false,
        IReadOnlyDictionary<string, PhysicalPortAnchor>? portAnchors = null)
    {
        if (footprint is null) throw new ArgumentNullException(nameof(footprint));
        if (!footprint.IsKnown)
        {
            return new PlacementEditResult(false, [new PlacementIssue("placement_unknown_footprint", "error", componentId, "Unknown physical footprint cannot be silently placed as 1x1.")]);
        }
        if (rotate && !footprint.RotationAllowed)
        {
            return new PlacementEditResult(false, [new PlacementIssue("placement_rotation_forbidden", "error", componentId, "The resolved footprint does not permit rotation.")]);
        }
        var effective = rotate
            ? PhysicalFootprint.Create(footprint.Scope, footprint.AreaUm2, footprint.HeightUm, footprint.WidthUm, footprint.SourceKind, footprint.EvidenceStatus,
                footprint.SourceRecordIds, footprint.EvidenceType, footprint.MethodId + ":rotated", footprint.ModelVersion, footprint.Formula,
                footprint.Uncertainty, footprint.ValidContext, footprint.RotationAllowed, footprint.KeepoutBottomUm, footprint.KeepoutTopUm, footprint.KeepoutLeftUm, footprint.KeepoutRightUm)
            : footprint;
        var projection = PhysicalFootprintResolver.Quantize(effective, CellWidthMicrometers, CellHeightMicrometers, DisplayMode, ReadableMinimumCells);
        var candidate = new PhysicalCellPlacement
        {
            Row = row,
            Col = col,
            WidthCells = projection.WidthCells,
            HeightCells = projection.HeightCells,
            Layer = string.IsNullOrWhiteSpace(layer) ? Layer : layer!.Trim(),
            PhysicalFootprintHash = footprint.FootprintHash,
            PhysicalWidthUm = effective.WidthUm!.Value,
            PhysicalHeightUm = effective.HeightUm!.Value,
            KeepoutLeftUm = effective.KeepoutLeftUm,
            KeepoutRightUm = effective.KeepoutRightUm,
            KeepoutTopUm = effective.KeepoutTopUm,
            KeepoutBottomUm = effective.KeepoutBottomUm,
            QuantizationSlackWidthUm = projection.QuantizationSlackWidthUm,
            QuantizationSlackHeightUm = projection.QuantizationSlackHeightUm,
            PhysicalScope = effective.Scope,
            PhysicalSourceKind = effective.SourceKind,
            PhysicalEvidenceStatus = effective.EvidenceStatus,
            PhysicalUncertainty = effective.Uncertainty,
            DisplayMode = projection.DisplayMode,
            NonProportionalRendering = projection.NonProportionalRendering,
            RotationAllowed = effective.RotationAllowed,
            Rotated = rotate,
            PortAnchors = portAnchors is null
                ? new Dictionary<string, PhysicalPortAnchor>(StringComparer.OrdinalIgnoreCase)
                : portAnchors.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase)
        };
        var normalizedId = componentId?.Trim() ?? "";
        var issues = ValidateCandidate(normalizedId, candidate).ToList();
        if (issues.Count > 0) return new PlacementEditResult(false, issues);
        ComponentCells[normalizedId] = candidate;
        ComponentPositions[normalizedId] = PositionForCell(candidate);
        return PlacementEditResult.Success;
    }

    /// <summary>Resolves edge-aware physical anchors for a link when either endpoint has Phase 9 footprint authority.</summary>
    public bool TryResolveLinkAnchors(string sourceComponentId, string sourcePortName, string destinationComponentId, string destinationPortName, out PhysicalPoint sourceAnchor, out PhysicalPoint destinationAnchor)
    {
        sourceAnchor = new PhysicalPoint(0, 0);
        destinationAnchor = new PhysicalPoint(0, 0);
        if (!ComponentCells.TryGetValue(sourceComponentId, out var source) || !ComponentCells.TryGetValue(destinationComponentId, out var destination)) return false;
        if (string.IsNullOrWhiteSpace(source.PhysicalFootprintHash) && string.IsNullOrWhiteSpace(destination.PhysicalFootprintHash) && source.PortAnchors.Count == 0 && destination.PortAnchors.Count == 0) return false;
        var sourceOrigin = PositionForCell(source);
        var destinationOrigin = PositionForCell(destination);
        var sourceWidth = source.PhysicalWidthUm > 0 ? source.PhysicalWidthUm : source.WidthCells * CellWidthMicrometers;
        var sourceHeight = source.PhysicalHeightUm > 0 ? source.PhysicalHeightUm : source.HeightCells * CellHeightMicrometers;
        var destinationWidth = destination.PhysicalWidthUm > 0 ? destination.PhysicalWidthUm : destination.WidthCells * CellWidthMicrometers;
        var destinationHeight = destination.PhysicalHeightUm > 0 ? destination.PhysicalHeightUm : destination.HeightCells * CellHeightMicrometers;
        var sourceDeviceOrigin = new PhysicalPoint(sourceOrigin.X + source.KeepoutLeftUm, sourceOrigin.Y + source.KeepoutTopUm);
        var destinationDeviceOrigin = new PhysicalPoint(destinationOrigin.X + destination.KeepoutLeftUm, destinationOrigin.Y + destination.KeepoutTopUm);
        if (TryExplicitAnchor(source, sourcePortName, sourceDeviceOrigin, sourceWidth, sourceHeight, out var explicitSource)) sourceAnchor = explicitSource;
        if (TryExplicitAnchor(destination, destinationPortName, destinationDeviceOrigin, destinationWidth, destinationHeight, out var explicitDestination)) destinationAnchor = explicitDestination;
        var sourceCenter = new PhysicalPoint(sourceDeviceOrigin.X + sourceWidth / 2, sourceDeviceOrigin.Y + sourceHeight / 2);
        var destinationCenter = new PhysicalPoint(destinationDeviceOrigin.X + destinationWidth / 2, destinationDeviceOrigin.Y + destinationHeight / 2);
        var horizontal = Math.Abs(destinationCenter.X - sourceCenter.X) >= Math.Abs(destinationCenter.Y - sourceCenter.Y);
        if (sourceAnchor == new PhysicalPoint(0, 0))
            sourceAnchor = horizontal
                ? new PhysicalPoint(destinationCenter.X >= sourceCenter.X ? sourceDeviceOrigin.X + sourceWidth : sourceDeviceOrigin.X, sourceCenter.Y)
                : new PhysicalPoint(sourceCenter.X, destinationCenter.Y >= sourceCenter.Y ? sourceDeviceOrigin.Y + sourceHeight : sourceDeviceOrigin.Y);
        if (destinationAnchor == new PhysicalPoint(0, 0))
            destinationAnchor = horizontal
                ? new PhysicalPoint(destinationCenter.X >= sourceCenter.X ? destinationDeviceOrigin.X : destinationDeviceOrigin.X + destinationWidth, destinationCenter.Y)
                : new PhysicalPoint(destinationCenter.X, destinationCenter.Y >= sourceCenter.Y ? destinationDeviceOrigin.Y : destinationDeviceOrigin.Y + destinationHeight);
        return true;
    }
    /// <summary>Attempts to move an existing component footprint while preserving its size.</summary>
    public PlacementEditResult MoveComponent(string componentId, int row, int col, string? layer = null)
    {
        if (!ComponentCells.TryGetValue(componentId, out var existing))
        {
            return PlaceComponent(componentId, row, col, layer: layer);
        }

        return PlaceComponent(componentId, row, col, existing.WidthCells, existing.HeightCells, layer ?? existing.Layer);
    }

    /// <summary>Removes any explicit physical placement for the supplied component id.</summary>
    public bool RemoveComponent(string componentId)
    {
        var removedCell = ComponentCells.Remove(componentId);
        var removedPosition = ComponentPositions.Remove(componentId);
        return removedCell || removedPosition;
    }

    /// <summary>Attempts to retrieve an explicit cell footprint for a component.</summary>
    public bool TryGetCell(string componentId, out PhysicalCellPlacement placement) =>
        ComponentCells.TryGetValue(componentId, out placement!);

    /// <summary>Attempts to retrieve an explicit physical coordinate for a component.</summary>
    public bool TryGetPhysicalPosition(string componentId, out PhysicalPoint position)
    {
        if (ComponentCells.TryGetValue(componentId, out var cell))
        {
            position = PositionForCell(cell);
            return true;
        }

        if (ComponentPositions.TryGetValue(componentId, out position!))
        {
            return true;
        }

        position = new PhysicalPoint(0, 0);
        return false;
    }

    /// <summary>Returns an explicit physical position or the legacy schematic fallback used by pre-6A callers.</summary>
    public PhysicalPoint PositionFor(HardwareComponent component)
    {
        if (TryGetPhysicalPosition(component.Id, out var position))
        {
            return position;
        }

        return new PhysicalPoint(component.Position.X * GridCellMicrometers, component.Position.Y * GridCellMicrometers);
    }

    /// <summary>Attempts to calculate physical Manhattan distance between two explicitly placed components.</summary>
    public bool TryGetManhattanDistanceMicrometers(string firstComponentId, string secondComponentId, out double distanceMicrometers)
    {
        if (!TryGetPhysicalPosition(firstComponentId, out var first) || !TryGetPhysicalPosition(secondComponentId, out var second))
        {
            distanceMicrometers = 0;
            return false;
        }

        distanceMicrometers = Manhattan(first, second);
        return true;
    }

    /// <summary>Calculates physical Manhattan distance between two explicitly placed components.</summary>
    public double ManhattanDistanceMicrometers(string firstComponentId, string secondComponentId) =>
        TryGetManhattanDistanceMicrometers(firstComponentId, secondComponentId, out var distance)
            ? distance
            : throw new InvalidOperationException($"Both components must have explicit physical placement: '{firstComponentId}', '{secondComponentId}'.");

    /// <summary>Validates explicit placement bounds, overlap, and missing component placements.</summary>
    public PlacementReport Validate(IEnumerable<HardwareComponent> components)
    {
        var report = new PlacementReport();
        var componentIds = components.Select(component => component.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in ComponentCells.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
        {
            report.Issues.AddRange(ValidateCandidate(pair.Key, pair.Value, includeExistingOverlap: false));
        }

        var placedCells = ComponentCells.ToList();
        for (var i = 0; i < placedCells.Count; i++)
        {
            for (var j = i + 1; j < placedCells.Count; j++)
            {
                if (Overlaps(placedCells[i].Value, placedCells[j].Value))
                {
                    report.Issues.Add(new PlacementIssue(
                        OverlapIssueCode,
                        "error",
                        placedCells[j].Key,
                        $"Component '{placedCells[j].Key}' overlaps component '{placedCells[i].Key}' on layer '{placedCells[j].Value.Layer}'."));
                }
            }
        }

        foreach (var componentId in componentIds.OrderBy(id => id, StringComparer.OrdinalIgnoreCase))
        {
            if (!ComponentCells.ContainsKey(componentId) && !ComponentPositions.ContainsKey(componentId))
            {
                report.Issues.Add(new PlacementIssue(
                    UnplacedIssueCode,
                    "warning",
                    componentId,
                    $"Component '{componentId}' has no explicit physical placement."));
            }
        }

        return report;
    }

    internal void Normalize()
    {
        SchemaVersion = string.IsNullOrWhiteSpace(SchemaVersion) ? CurrentSchemaVersion : SchemaVersion;
        Rows = Math.Max(0, Rows);
        Cols = Math.Max(0, Cols);
        CellWidthMicrometers = CellWidthMicrometers <= 0 ? 100 : CellWidthMicrometers;
        CellHeightMicrometers = CellHeightMicrometers <= 0 ? CellWidthMicrometers : CellHeightMicrometers;
        Origin ??= new PhysicalPoint(0, 0);
        Layer = string.IsNullOrWhiteSpace(Layer) ? "M0" : Layer;
        ReadableMinimumCells = Math.Max(1, ReadableMinimumCells);
        FloorplanMetadata ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        ComponentCells ??= new Dictionary<string, PhysicalCellPlacement>(StringComparer.OrdinalIgnoreCase);
        ComponentPositions ??= new Dictionary<string, PhysicalPoint>(StringComparer.OrdinalIgnoreCase);
        ExtensionData ??= new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var pair in ComponentCells.ToList())
        {
            var cell = pair.Value;
            cell.WidthCells = Math.Max(1, cell.WidthCells);
            cell.HeightCells = Math.Max(1, cell.HeightCells);
            cell.Layer = string.IsNullOrWhiteSpace(cell.Layer) ? Layer : cell.Layer;
            cell.PortAnchors ??= new Dictionary<string, PhysicalPortAnchor>(StringComparer.OrdinalIgnoreCase);
            ComponentPositions[pair.Key] = PositionForCell(cell);
        }
    }

    private PhysicalPoint PositionForCell(PhysicalCellPlacement cell) =>
        new(Origin.X + cell.Col * CellWidthMicrometers, Origin.Y + cell.Row * CellHeightMicrometers);

    private IEnumerable<PlacementIssue> ValidateCandidate(string componentId, PhysicalCellPlacement candidate, bool includeExistingOverlap = true)
    {
        if (candidate.WidthCells <= 0 || candidate.HeightCells <= 0)
        {
            yield return new PlacementIssue("placement_invalid_footprint", "error", componentId, "Placement footprint must be at least one cell wide and high.");
        }

        if (!string.IsNullOrWhiteSpace(candidate.PhysicalFootprintHash))
        {
            var minimumWidth = Math.Max(1, checked((int)Math.Ceiling((candidate.PhysicalWidthUm + candidate.KeepoutLeftUm + candidate.KeepoutRightUm) / CellWidthMicrometers)));
            var minimumHeight = Math.Max(1, checked((int)Math.Ceiling((candidate.PhysicalHeightUm + candidate.KeepoutTopUm + candidate.KeepoutBottomUm) / CellHeightMicrometers)));
            if (candidate.WidthCells < minimumWidth || candidate.HeightCells < minimumHeight)
            {
                yield return new PlacementIssue("placement_below_physical_minimum", "error", componentId,
                    $"Component '{componentId}' cannot shrink below resolved physical minimum {minimumWidth}x{minimumHeight} cells.");
            }
            if (candidate.NonProportionalRendering && candidate.DisplayMode != PhysicalPlacementDisplayMode.ReadableMinimum)
            {
                yield return new PlacementIssue("placement_rendering_mode_invalid", "error", componentId, "Non-proportional rendering must be explicitly marked ReadableMinimum.");
            }
        }
        if (candidate.Row < 0 || candidate.Col < 0 ||
            Rows > 0 && candidate.Row + Math.Max(1, candidate.HeightCells) > Rows ||
            Cols > 0 && candidate.Col + Math.Max(1, candidate.WidthCells) > Cols)
        {
            yield return new PlacementIssue(
                OutOfBoundsIssueCode,
                "error",
                componentId,
                $"Component '{componentId}' placement row={candidate.Row}, col={candidate.Col}, size={candidate.WidthCells}x{candidate.HeightCells} exceeds grid rows={Rows}, cols={Cols}; placement is rejected, not clamped.");
        }

        if (!includeExistingOverlap)
        {
            yield break;
        }

        foreach (var pair in ComponentCells)
        {
            if (string.Equals(pair.Key, componentId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (Overlaps(candidate, pair.Value))
            {
                yield return new PlacementIssue(
                    OverlapIssueCode,
                    "error",
                    componentId,
                    $"Component '{componentId}' overlaps component '{pair.Key}' on layer '{candidate.Layer}'.");
            }
        }
    }

    private void CopyPhysicalMetadata(PhysicalCellPlacement source, PhysicalCellPlacement target)
    {
        target.PhysicalFootprintHash = source.PhysicalFootprintHash;
        target.PhysicalWidthUm = source.PhysicalWidthUm;
        target.PhysicalHeightUm = source.PhysicalHeightUm;
        target.KeepoutLeftUm = source.KeepoutLeftUm;
        target.KeepoutRightUm = source.KeepoutRightUm;
        target.KeepoutTopUm = source.KeepoutTopUm;
        target.KeepoutBottomUm = source.KeepoutBottomUm;
        target.QuantizationSlackWidthUm = target.WidthCells * CellWidthMicrometers - source.PhysicalWidthUm - source.KeepoutLeftUm - source.KeepoutRightUm;
        target.QuantizationSlackHeightUm = target.HeightCells * CellHeightMicrometers - source.PhysicalHeightUm - source.KeepoutTopUm - source.KeepoutBottomUm;
        target.PhysicalScope = source.PhysicalScope;
        target.PhysicalSourceKind = source.PhysicalSourceKind;
        target.PhysicalEvidenceStatus = source.PhysicalEvidenceStatus;
        target.PhysicalUncertainty = source.PhysicalUncertainty;
        target.DisplayMode = source.DisplayMode;
        target.NonProportionalRendering = source.NonProportionalRendering;
        target.RotationAllowed = source.RotationAllowed;
        target.Rotated = source.Rotated;
        target.PortAnchors = source.PortAnchors.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
    }

    private static bool TryExplicitAnchor(PhysicalCellPlacement placement, string portName, PhysicalPoint origin, double width, double height, out PhysicalPoint anchor)
    {
        anchor = new PhysicalPoint(0, 0);
        if (!placement.PortAnchors.TryGetValue(portName ?? "", out var declared)) return false;
        anchor = declared.Kind switch
        {
            PhysicalPortAnchorKind.Center => new PhysicalPoint(origin.X + width / 2, origin.Y + height / 2),
            PhysicalPortAnchorKind.LeftEdge => new PhysicalPoint(origin.X, origin.Y + Math.Max(0, Math.Min(height, declared.OffsetYUm))),
            PhysicalPortAnchorKind.RightEdge => new PhysicalPoint(origin.X + width, origin.Y + Math.Max(0, Math.Min(height, declared.OffsetYUm))),
            PhysicalPortAnchorKind.TopEdge => new PhysicalPoint(origin.X + Math.Max(0, Math.Min(width, declared.OffsetXUm)), origin.Y),
            PhysicalPortAnchorKind.BottomEdge => new PhysicalPoint(origin.X + Math.Max(0, Math.Min(width, declared.OffsetXUm)), origin.Y + height),
            _ => new PhysicalPoint(origin.X + declared.OffsetXUm, origin.Y + declared.OffsetYUm)
        };
        if (!double.IsFinite(anchor.X) || !double.IsFinite(anchor.Y) || anchor.X < origin.X - 1e-9 || anchor.X > origin.X + width + 1e-9 || anchor.Y < origin.Y - 1e-9 || anchor.Y > origin.Y + height + 1e-9)
            throw new InvalidOperationException($"Physical port anchor '{portName}' lies outside the resolved footprint.");
        return true;
    }
    private static bool Overlaps(PhysicalCellPlacement left, PhysicalCellPlacement right)
    {
        if (!string.Equals(left.Layer, right.Layer, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return left.Row < right.Row + right.HeightCells &&
               left.Row + left.HeightCells > right.Row &&
               left.Col < right.Col + right.WidthCells &&
               left.Col + left.WidthCells > right.Col;
    }

    internal static double Manhattan(PhysicalPoint a, PhysicalPoint b) => Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y);
}
/// <summary>Represents physical route data exchanged by hardware design and simulation workflows.</summary>
public sealed class PhysicalRoute
{
    /// <summary>Defines the current physical route schema version.</summary>
    public const string CurrentSchemaVersion = "1.0";
    /// <summary>Gets or sets the physical route schema version.</summary>
    [JsonPropertyName("schema_version")]
    public string SchemaVersion { get; set; } = CurrentSchemaVersion;
    /// <summary>Gets or sets the logical link id value carried by the enclosing physical route contract.</summary>
    public string LinkId { get; set; } = "";
    /// <summary>Gets or sets the resolved logical target kind for this route.</summary>
    public PhysicalRouteTargetKind TargetKind { get; set; } = PhysicalRouteTargetKind.LogicalLink;
    /// <summary>Gets or sets the structured routing medium used as the explicit route medium truth.</summary>
    public RoutingMedium Medium { get; set; } = RoutingMedium.ElectricalMetal;
    /// <summary>Gets or sets the structured routing layer id used by route analysis and UI rendering.</summary>
    public RoutingLayerId LayerId { get; set; } = RoutingLayerId.Metal(3);
    /// <summary>Gets or sets the unit used by route path coordinates.</summary>
    public PhysicalRoutePointUnit PathUnit { get; set; } = PhysicalRoutePointUnit.Micrometers;
    /// <summary>Gets or sets the legacy route type string, mapped to Medium for compatibility.</summary>
    [JsonPropertyOrder(-1)]
    public string RouteType
    {
        get => MediumToLegacyRouteType(Medium);
        set => Medium = ParseMedium(value);
    }
    /// <summary>Gets or sets the legacy compact layer id, mapped to LayerId for compatibility.</summary>
    [JsonPropertyOrder(-1)]
    public string Layer
    {
        get => LayerId.ToString();
        set => LayerId = RoutingLayerId.Parse(value);
    }
    /// <summary>Gets or sets the explicit physical route path.</summary>
    public List<PhysicalPoint> Path { get; set; } = [];
    /// <summary>Gets or sets unknown JSON properties preserved for forward-compatible roundtrips.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement> ExtensionData { get; set; } = new(StringComparer.Ordinal);

    /// <summary>Parses a legacy route type value into the structured routing medium contract.</summary>
    public static RoutingMedium ParseMedium(string? value)
    {
        var normalized = (value ?? "").Trim().Replace('-', '_').Replace(' ', '_').ToLowerInvariant();
        return normalized switch
        {
            "electrical" or "electrical_metal" or "metal" => RoutingMedium.ElectricalMetal,
            "optical" or "optical_waveguide" or "waveguide" => RoutingMedium.OpticalWaveguide,
            "thermal" or "thermal_control" => RoutingMedium.ThermalControl,
            _ when Enum.TryParse<RoutingMedium>(value, ignoreCase: true, out var parsed) => parsed,
            _ => RoutingMedium.ElectricalMetal
        };
    }

    /// <summary>Maps a structured routing medium to the legacy route type value used by older link contracts.</summary>
    public static string MediumToLegacyRouteType(RoutingMedium medium) => medium switch
    {
        RoutingMedium.OpticalWaveguide => "optical",
        RoutingMedium.ThermalControl => "thermal_control",
        _ => "electrical"
    };

    internal void Normalize()
    {
        SchemaVersion = string.IsNullOrWhiteSpace(SchemaVersion) ? CurrentSchemaVersion : SchemaVersion;
        LinkId = LinkId?.Trim() ?? "";
        LayerId ??= RoutingLayerId.Metal(3);
        Path ??= [];
        ExtensionData ??= new Dictionary<string, JsonElement>(StringComparer.Ordinal);
    }
}

/// <summary>Represents physical routing data exchanged by hardware design and simulation workflows.</summary>
public sealed class PhysicalRouting
{
    /// <summary>Defines the current physical routing schema version.</summary>
    public const string CurrentSchemaVersion = "1.0";
    /// <summary>Gets or sets the physical routing schema version.</summary>
    [JsonPropertyName("schema_version")]
    public string SchemaVersion { get; set; } = CurrentSchemaVersion;
    /// <summary>Gets or sets the routes collection carried by the enclosing physical routing contract.</summary>
    public List<PhysicalRoute> Routes { get; set; } = [];
    /// <summary>Gets or sets unknown JSON properties preserved for forward-compatible roundtrips.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement> ExtensionData { get; set; } = new(StringComparer.Ordinal);

    /// <summary>Finds route when it exists.</summary>
    public PhysicalRoute? FindRoute(string linkId) =>
        Routes.FirstOrDefault(r => string.Equals(r.LinkId, linkId, StringComparison.OrdinalIgnoreCase));

    internal void Normalize()
    {
        SchemaVersion = string.IsNullOrWhiteSpace(SchemaVersion) ? CurrentSchemaVersion : SchemaVersion;
        Routes ??= [];
        ExtensionData ??= new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var route in Routes)
        {
            route.Normalize();
        }
    }
}

/// <summary>Represents link model parameters data exchanged by hardware design and simulation workflows.</summary>
public sealed class LinkModelParameters
{
    /// <summary>Gets or sets link base latency in cycles before serialization and physical propagation.</summary>
    public int BaseLatencyCycles { get; set; } = 1;
    /// <summary>Gets or sets propagation delay in picoseconds per physical micrometer.</summary>
    public double DelayPsPerMicrometer { get; set; } = 1.0;
    /// <summary>Gets or sets additional congestion latency in cycles.</summary>
    public int CongestionLatencyCycles { get; set; }
    /// <summary>Gets or sets base link energy per bit in picojoules.</summary>
    public double BaseEnergyPerBit { get; set; } = ComponentDefaults.LinkEnergyPerBitPJ;
    /// <summary>Gets or sets distance-dependent energy per bit per micrometer.</summary>
    public double EnergyPerBitPerMicrometer { get; set; } = 0.0001;
    /// <summary>Gets or sets the route resource analysis cell size in micrometers.</summary>
    public double RoutingResourceCellSizeMicrometers { get; set; } = 100;
    /// <summary>Gets or sets default exact route resource capacity.</summary>
    public int RoutingResourceDefaultCapacity { get; set; } = 1;
    /// <summary>Gets or sets electrical route resource capacity.</summary>
    public int RoutingResourceElectricalCapacity { get; set; } = 1;
    /// <summary>Gets or sets optical route resource capacity.</summary>
    public int RoutingResourceOpticalCapacity { get; set; } = 1;
    /// <summary>Gets or sets thermal route resource capacity.</summary>
    public int RoutingResourceThermalCapacity { get; set; } = 1;
    /// <summary>Gets or sets dynamic congestion penalty cycles per over-capacity link.</summary>
    public int RoutingCongestionPenaltyCyclesPerOverCapacityLink { get; set; } = 1;
    /// <summary>Gets or sets the cap applied to dynamic routing congestion penalty cycles.</summary>
    public int RoutingCongestionMaxPenaltyCycles { get; set; } = 16;
    /// <summary>Gets the unit carried by dynamic routing congestion penalty values.</summary>
    public string RoutingCongestionPenaltyUnits => "cycles";
    /// <summary>Gets the deterministic dynamic routing congestion penalty formula label.</summary>
    public string RoutingCongestionPenaltyFormula => "sum(over_capacity_by * routing_congestion_penalty_cycles_per_over_capacity_link) capped by routing_congestion_max_penalty_cycles";

    /// <summary>Creates link model parameters from graph-level string parameters.</summary>
    public static LinkModelParameters FromGraphParameters(IReadOnlyDictionary<string, string>? parameters)
    {
        var model = new LinkModelParameters();
        if (parameters is null)
        {
            return model;
        }

        model.BaseLatencyCycles = ReadInt(parameters, "link_base_latency_cycles", model.BaseLatencyCycles);
        model.DelayPsPerMicrometer = ReadDouble(parameters, "delay_ps_per_um", ReadDouble(parameters, "delay_ps_per_micrometer", model.DelayPsPerMicrometer));
        model.CongestionLatencyCycles = ReadInt(parameters, "link_congestion_latency_cycles", model.CongestionLatencyCycles);
        model.BaseEnergyPerBit = ReadDouble(parameters, "link_base_energy_per_bit_pj", model.BaseEnergyPerBit);
        model.EnergyPerBitPerMicrometer = ReadDouble(parameters, "energy_per_bit_per_um", ReadDouble(parameters, "energy_per_bit_per_micrometer", model.EnergyPerBitPerMicrometer));
        model.RoutingResourceCellSizeMicrometers = ReadDouble(parameters, "routing_resource_cell_size_um", model.RoutingResourceCellSizeMicrometers);
        model.RoutingResourceDefaultCapacity = ReadInt(parameters, "routing_resource_capacity", model.RoutingResourceDefaultCapacity);
        model.RoutingResourceElectricalCapacity = ReadInt(parameters, "routing_resource_capacity_electrical", model.RoutingResourceDefaultCapacity);
        model.RoutingResourceOpticalCapacity = ReadInt(parameters, "routing_resource_capacity_optical", model.RoutingResourceDefaultCapacity);
        model.RoutingResourceThermalCapacity = ReadInt(parameters, "routing_resource_capacity_thermal", model.RoutingResourceDefaultCapacity);
        model.RoutingCongestionPenaltyCyclesPerOverCapacityLink = ReadInt(parameters, "routing_congestion_penalty_cycles_per_over_capacity_link", ReadInt(parameters, "routing_congestion_penalty_cycles", model.RoutingCongestionPenaltyCyclesPerOverCapacityLink));
        model.RoutingCongestionMaxPenaltyCycles = ReadInt(parameters, "routing_congestion_max_penalty_cycles", model.RoutingCongestionMaxPenaltyCycles);
        return model;
    }

    /// <summary>Creates exact route resource capacity profile from graph-level model parameters.</summary>
    public RouteResourceCapacityProfile ToRouteResourceCapacityProfile() => new()
    {
        DefaultCapacity = Math.Max(1, RoutingResourceDefaultCapacity),
        ElectricalMetalCapacity = Math.Max(1, RoutingResourceElectricalCapacity),
        OpticalWaveguideCapacity = Math.Max(1, RoutingResourceOpticalCapacity),
        ThermalControlCapacity = Math.Max(1, RoutingResourceThermalCapacity)
    };

    private static int ReadInt(IReadOnlyDictionary<string, string> parameters, string key, int fallback) =>
        parameters.TryGetValue(key, out var raw) && int.TryParse(raw, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var value)
            ? value
            : fallback;

    private static double ReadDouble(IReadOnlyDictionary<string, string> parameters, string key, double fallback) =>
        parameters.TryGetValue(key, out var raw) && double.TryParse(raw, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value)
            ? value
            : fallback;
}

/// <summary>Represents exact physical link timing and energy calculation output.</summary>
/// <param name="DistanceMicrometers">Provides the physical distance in micrometers.</param>
/// <param name="Bits">Provides the transferred payload size in bits.</param>
/// <param name="BandwidthBitsPerCycle">Provides the link bandwidth in bits per cycle.</param>
/// <param name="BaseLatencyCycles">Provides base link latency in cycles.</param>
/// <param name="SerializationCycles">Provides serialization service time in cycles.</param>
/// <param name="PropagationDelayPs">Provides propagation delay in picoseconds.</param>
/// <param name="PropagationCycles">Provides propagation delay rounded to cycles.</param>
/// <param name="CongestionLatencyCycles">Provides congestion latency in cycles.</param>
/// <param name="LinkLatencyCycles">Provides link latency excluding serialization.</param>
/// <param name="TotalLatencyCycles">Provides total latency including serialization.</param>
/// <param name="EnergyPerBitPJ">Provides energy per transferred bit in picojoules.</param>
/// <param name="TotalEnergyPJ">Provides total transferred energy in picojoules.</param>
public sealed record PhysicalLinkTimingBreakdown(
    double DistanceMicrometers,
    long Bits,
    int BandwidthBitsPerCycle,
    int BaseLatencyCycles,
    int SerializationCycles,
    double PropagationDelayPs,
    int PropagationCycles,
    int CongestionLatencyCycles,
    int LinkLatencyCycles,
    int TotalLatencyCycles,
    double EnergyPerBitPJ,
    double TotalEnergyPJ);

/// <summary>Summarizes application of physical link models to a graph.</summary>
public sealed class PhysicalLinkModelApplyReport
{
    /// <summary>Gets non-fatal issues encountered while deriving physical link models.</summary>
    public List<PlacementIssue> Issues { get; } = [];
    /// <summary>Gets or sets the number of links updated with physical model values.</summary>
    public int AppliedLinkCount { get; set; }
    /// <summary>Gets or sets exact routing congestion analysis for this compile.</summary>
    public RouteResourceCongestionReport? RoutingCongestion { get; set; }
    /// <summary>Gets structured routing congestion warnings backed by exact resource evidence.</summary>
    public List<RoutingCongestionWarning> RoutingWarnings { get; } = [];
    /// <summary>Gets a compact summary of applied physical link updates.</summary>
    public string Summary => $"physical_links={AppliedLinkCount};issues={Issues.Count};routing_congested_resources={RoutingCongestion?.CongestedResourceCount ?? 0}";
}

/// <summary>Provides deterministic physical link timing and energy calculations.</summary>
public static class PhysicalLinkModel
{
    /// <summary>Calculates link timing and energy from distance, packet size, bandwidth, and clock configuration.</summary>
    public static PhysicalLinkTimingBreakdown Calculate(
        double distanceMicrometers,
        long bits,
        int bandwidthBitsPerCycle,
        LinkModelParameters? model = null,
        ClockConfig? clock = null,
        int additionalCongestionLatencyCycles = 0)
    {
        if (distanceMicrometers < 0 || double.IsNaN(distanceMicrometers) || double.IsInfinity(distanceMicrometers))
        {
            throw new ArgumentOutOfRangeException(nameof(distanceMicrometers), "Distance must be finite and non-negative micrometers.");
        }

        model ??= new LinkModelParameters();
        clock ??= new ClockConfig();
        var safeBits = Math.Max(0, bits);
        var safeBandwidth = Math.Max(1, bandwidthBitsPerCycle);
        var serializationCycles = checked((int)Math.Ceiling(safeBits / (double)safeBandwidth));
        var propagationDelayPs = distanceMicrometers * model.DelayPsPerMicrometer;
        var propagationCycles = checked((int)clock.PsToCycles(propagationDelayPs));
        var baseLatency = Math.Max(0, model.BaseLatencyCycles);
        var congestion = Math.Max(0, model.CongestionLatencyCycles) + Math.Max(0, additionalCongestionLatencyCycles);
        var linkLatency = baseLatency + propagationCycles + congestion;
        var totalLatency = baseLatency + serializationCycles + propagationCycles + congestion;
        var energyPerBit = Math.Max(0, model.BaseEnergyPerBit) + distanceMicrometers * Math.Max(0, model.EnergyPerBitPerMicrometer);
        return new PhysicalLinkTimingBreakdown(
            distanceMicrometers,
            safeBits,
            safeBandwidth,
            baseLatency,
            serializationCycles,
            propagationDelayPs,
            propagationCycles,
            congestion,
            linkLatency,
            totalLatency,
            energyPerBit,
            safeBits * energyPerBit);
    }

    internal static PhysicalLinkModelApplyReport ApplyToGraph(
        HardwareGraph graph,
        PhysicalPlacement? placement = null,
        PhysicalRouting? routing = null,
        LinkModelParameters? linkModel = null,
        ClockConfig? clock = null,
        LegacyPreparationReport? legacyReport = null)
    {
        var report = new PhysicalLinkModelApplyReport();
        placement ??= graph.Placement;
        routing ??= graph.Routing;
        if (placement is null && routing is null)
        {
            return report;
        }

        placement?.Normalize();
        routing?.Normalize();
        linkModel ??= LinkModelParameters.FromGraphParameters(graph.Parameters);
        clock ??= new ClockConfig();
        var congestedResourcesByLink = new Dictionary<string, List<RouteResourceOccupancy>>(StringComparer.OrdinalIgnoreCase);
        if (routing is not null)
        {
            report.RoutingCongestion = RouteResourceAnalyzer.Analyze(
                routing,
                linkModel.RoutingResourceCellSizeMicrometers,
                linkModel.RoutingResourceDefaultCapacity,
                linkModel.ToRouteResourceCapacityProfile());
            congestedResourcesByLink = CongestedResourcesByLink(report.RoutingCongestion);
            report.RoutingWarnings.AddRange(CreateRoutingCongestionWarnings(report.RoutingCongestion));
        }

        foreach (var link in graph.Links.OrderBy(link => link.Id, StringComparer.Ordinal))
        {
            var source = graph.FindComponent(link.Source.ComponentId);
            var destination = graph.FindComponent(link.Destination.ComponentId);
            if (source is null || destination is null)
            {
                continue;
            }

            var route = routing?.FindRoute(link.Id);
            double length;
            string routeType;
            RoutingMedium? routeMedium = null;
            PhysicalRouteMetricResult? routeMetrics = null;
            PhysicalPoint? resolvedSourceAnchor = null;
            PhysicalPoint? resolvedDestinationAnchor = null;
            var sourceAnchor = new PhysicalPoint(0, 0);
            var destinationAnchor = new PhysicalPoint(0, 0);
            var hasResolvedAnchors = placement is not null && placement.TryResolveLinkAnchors(
                source.Id, link.Source.PortName, destination.Id, link.Destination.PortName,
                out sourceAnchor, out destinationAnchor);
            if (hasResolvedAnchors)
            {
                resolvedSourceAnchor = sourceAnchor;
                resolvedDestinationAnchor = destinationAnchor;
            }
            if (route is not null)
            {
                routeMetrics = PhysicalRouteMetrics.Analyze(route.Path);
                length = routeMetrics.LengthMicrometers;
                if (hasResolvedAnchors)
                {
                    length += route.Path.Count == 0
                        ? PhysicalPlacement.Manhattan(sourceAnchor, destinationAnchor)
                        : PhysicalPlacement.Manhattan(sourceAnchor, route.Path[0]) + PhysicalPlacement.Manhattan(route.Path[^1], destinationAnchor);
                }
                routeMedium = route.Medium;
                routeType = PhysicalRoute.MediumToLegacyRouteType(route.Medium);
            }
            else if (hasResolvedAnchors)
            {
                length = PhysicalPlacement.Manhattan(sourceAnchor, destinationAnchor);
                routeType = "footprint_anchor_manhattan";
            }
            else if (placement is not null &&
                     placement.TryGetPhysicalPosition(source.Id, out var sourcePosition) &&
                     placement.TryGetPhysicalPosition(destination.Id, out var destinationPosition))
            {
                length = PhysicalPlacement.Manhattan(sourcePosition, destinationPosition);
                routeType = "placement_manhattan";
            }
            else
            {
                report.Issues.Add(new PlacementIssue(
                    PhysicalPlacement.UnplacedIssueCode,
                    "warning",
                    link.Id,
                    $"Link '{link.Id}' cannot derive physical length because one or both endpoints have no explicit placement."));
                continue;
            }

            var congestedResources = route is not null && congestedResourcesByLink.TryGetValue(link.Id, out var resources)
                ? resources
                : [];
            var routingCongestionPenalty = DynamicCongestionPenaltyCycles(congestedResources, linkModel);
            var timing = Calculate(length, bits: 0, Math.Max(1, link.BandwidthBitsPerCycle), linkModel, clock, routingCongestionPenalty);
            link.PhysicalLength = length;
            link.RouteType = routeType;
            link.LatencyCycles = timing.LinkLatencyCycles;
            link.EnergyPerBit = timing.EnergyPerBitPJ;
            link.Parameters["physical_distance_um"] = length.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture);
            if (resolvedSourceAnchor is not null && resolvedDestinationAnchor is not null)
            {
                link.Parameters["physical_source_anchor_um"] = $"{resolvedSourceAnchor.X.ToString("R", System.Globalization.CultureInfo.InvariantCulture)},{resolvedSourceAnchor.Y.ToString("R", System.Globalization.CultureInfo.InvariantCulture)}";
                link.Parameters["physical_destination_anchor_um"] = $"{resolvedDestinationAnchor.X.ToString("R", System.Globalization.CultureInfo.InvariantCulture)},{resolvedDestinationAnchor.Y.ToString("R", System.Globalization.CultureInfo.InvariantCulture)}";
                link.Parameters["physical_anchor_source"] = "resolved footprint edge or explicit port anchor";
            }
            link.Parameters["delay_ps_per_um"] = linkModel.DelayPsPerMicrometer.ToString(System.Globalization.CultureInfo.InvariantCulture);
            link.Parameters["propagation_delay_ps"] = timing.PropagationDelayPs.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture);
            link.Parameters["propagation_cycles"] = timing.PropagationCycles.ToString(System.Globalization.CultureInfo.InvariantCulture);
            link.Parameters["physical_latency_formula"] = "base+serialization+propagation+congestion";
            link.Parameters["physical_route_strategy"] = route is not null ? (hasResolvedAnchors ? "explicit_route_path_with_footprint_anchors" : "explicit_route_path") : (hasResolvedAnchors ? "footprint_anchor_manhattan" : "placement_manhattan_fallback");
            link.Parameters["link_latency_cycles_excluding_serialization"] = timing.LinkLatencyCycles.ToString(System.Globalization.CultureInfo.InvariantCulture);
            link.Parameters["routing_congestion_penalty_cycles"] = routingCongestionPenalty.ToString(System.Globalization.CultureInfo.InvariantCulture);
            link.Parameters["routing_congestion_penalty_units"] = linkModel.RoutingCongestionPenaltyUnits;
            link.Parameters["routing_congestion_penalty_formula"] = linkModel.RoutingCongestionPenaltyFormula;
            link.Parameters["routing_congestion_penalty_cap_cycles"] = Math.Max(0, linkModel.RoutingCongestionMaxPenaltyCycles).ToString(System.Globalization.CultureInfo.InvariantCulture);
            link.Parameters["routing_congestion_penalty_source"] = "edge/direction/layer/medium route resource occupancy";
            link.Parameters["routing_congestion_resource_evidence"] = FormatResourceEvidence(congestedResources);
            if (route is not null)
            {
                link.Parameters["routing_medium"] = route.Medium.ToString();
                link.Parameters["routing_layer_id"] = route.LayerId.ToString();
                link.Parameters["route_path_unit"] = route.PathUnit.ToString();
                link.Parameters["route_bend_count"] = routeMetrics!.BendCount.ToString(System.Globalization.CultureInfo.InvariantCulture);
                link.Parameters["bend_count"] = routeMetrics.BendCount.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }

            if (routeMedium == RoutingMedium.OpticalWaveguide)
            {
                var optical = OpticalModel.Estimate(new OpticalLinkParameters
                {
                    LengthMillimeters = length / 1000.0,
                    BendCount = routeMetrics?.BendCount ?? 0,
                    CrossingCount = link.Parameters.TryGetValue("crossing_count", out var crossings) && int.TryParse(crossings, out var crossingCount) ? crossingCount : 0,
                    SplitterCount = link.Parameters.TryGetValue("splitter_count", out var splitters) && int.TryParse(splitters, out var splitterCount) ? splitterCount : 0
                });
                link.Parameters["optical_loss_db"] = optical.LossDb.ToString(System.Globalization.CultureInfo.InvariantCulture);
                link.EnergyPerBit += optical.EnergyPerBit;
            }

            report.AppliedLinkCount++;
        }

        legacyReport?.Notes.Add("Applied physical placement/routing link latency and energy models with ps/um propagation units and exact route-resource congestion penalties.");
        return report;
    }

    private static Dictionary<string, List<RouteResourceOccupancy>> CongestedResourcesByLink(RouteResourceCongestionReport report)
    {
        var result = new Dictionary<string, List<RouteResourceOccupancy>>(StringComparer.OrdinalIgnoreCase);
        foreach (var resource in report.Resources.Where(resource => resource.IsCongested))
        {
            foreach (var linkId in resource.LinkIds)
            {
                if (!result.TryGetValue(linkId, out var resources))
                {
                    resources = [];
                    result[linkId] = resources;
                }

                resources.Add(resource);
            }
        }

        return result;
    }

    private static int DynamicCongestionPenaltyCycles(IReadOnlyList<RouteResourceOccupancy> resources, LinkModelParameters model)
    {
        var perOverCapacity = Math.Max(0, model.RoutingCongestionPenaltyCyclesPerOverCapacityLink);
        var uncapped = resources.Sum(resource => resource.OverCapacityBy * perOverCapacity);
        return Math.Min(uncapped, Math.Max(0, model.RoutingCongestionMaxPenaltyCycles));
    }

    private static IEnumerable<RoutingCongestionWarning> CreateRoutingCongestionWarnings(RouteResourceCongestionReport report)
    {
        foreach (var resource in report.Resources.Where(resource => resource.IsCongested))
        {
            yield return new RoutingCongestionWarning
            {
                Message = $"Routing resource {resource.EdgeId} {resource.Direction} {resource.Layer}/{resource.Medium} is over capacity by {resource.OverCapacityBy}.",
                Evidence = resource.Evidence,
                LinkIds = resource.LinkIds.ToList(),
                Suggestions =
                [
                    new("change_layer", $"Move one of {LinkList(resource.LinkIds)} to another layer on edge {resource.EdgeId}.", resource.Evidence),
                    new("change_path", $"Reroute one of {LinkList(resource.LinkIds)} around edge {resource.EdgeId}.", resource.Evidence),
                    new("increase_capacity", $"Increase {resource.CapacitySource} for {resource.Layer}/{resource.Medium} above {resource.Capacity}.", resource.Evidence),
                    new("change_medium", $"Move one of {LinkList(resource.LinkIds)} to a different routing medium if the connected ports allow it.", resource.Evidence)
                ]
            };
        }
    }

    private static string FormatResourceEvidence(IReadOnlyList<RouteResourceOccupancy> resources) =>
        resources.Count == 0 ? "none" : string.Join(" | ", resources.Select(resource => resource.Evidence));

    private static string LinkList(IReadOnlyList<string> linkIds) =>
        linkIds.Count == 0 ? "-" : string.Join(",", linkIds);

    private static double RouteLength(IReadOnlyList<PhysicalPoint> path)
    {
        if (path.Count < 2)
        {
            return 0;
        }

        var total = 0.0;
        for (var i = 1; i < path.Count; i++)
        {
            total += PhysicalPlacement.Manhattan(path[i - 1], path[i]);
        }

        return total;
    }
}
internal sealed class LegacyPreparationReport
{
    public List<string> Notes { get; } = [];
}

internal sealed class LegacyPreparationResult
{
    public HardwareGraph Graph { get; init; } = new();
    public LegacyPreparationReport Report { get; init; } = new();
    public WorkloadSchedule? Schedule { get; init; }
}

internal static class LegacyWorkloadGraphPreparation
{
    public static LegacyPreparationResult Prepare(
        HardwareGraph logicalGraph,
        WorkloadGraph? workload = null,
        WorkloadMapping? mapping = null,
        PhysicalPlacement? placement = null,
        PhysicalRouting? routing = null,
        LinkModelParameters? linkModel = null,
        DeviceModelRegistry? modelRegistry = null) =>
        PrepareInternal(logicalGraph, workload, mapping, placement, routing, linkModel, modelRegistry, insertPrecisionAdapters: false);

    public static LegacyPreparationResult PrepareWithPrecisionAdapters(
        HardwareGraph logicalGraph,
        WorkloadGraph? workload = null,
        WorkloadMapping? mapping = null,
        PhysicalPlacement? placement = null,
        PhysicalRouting? routing = null,
        LinkModelParameters? linkModel = null,
        DeviceModelRegistry? modelRegistry = null) =>
        PrepareInternal(logicalGraph, workload, mapping, placement, routing, linkModel, modelRegistry, insertPrecisionAdapters: true);

    private static LegacyPreparationResult PrepareInternal(
        HardwareGraph logicalGraph,
        WorkloadGraph? workload,
        WorkloadMapping? mapping,
        PhysicalPlacement? placement,
        PhysicalRouting? routing,
        LinkModelParameters? linkModel,
        DeviceModelRegistry? modelRegistry,
        bool insertPrecisionAdapters)
    {
        var graph = HardwareGraphJson.Deserialize(HardwareGraphJson.Serialize(logicalGraph));
        var report = new LegacyPreparationReport();
        WorkloadSchedule? schedule = null;
        linkModel ??= new LinkModelParameters();

        ApplyWorkloadToSources(graph, workload, mapping, report);
        if (insertPrecisionAdapters)
        {
            InsertPrecisionAdapters(graph, workload, mapping, report);
        }

        if (workload is not null && mapping is not null)
        {
            schedule = new WorkloadScheduler().BuildSchedule(workload, mapping, graph);
            ApplyScheduleToComponents(graph, schedule, report);
        }
        PhysicalLinkModel.ApplyToGraph(graph, placement ?? graph.Placement, routing ?? graph.Routing, linkModel, new ClockConfig(), report);
        ApplyImportedModels(graph, modelRegistry, report);
        ApplyAdvancedComponentEstimates(graph, workload, report);

        return new LegacyPreparationResult { Graph = graph, Report = report, Schedule = schedule };
    }

    private static void ApplyScheduleToComponents(HardwareGraph graph, WorkloadSchedule schedule, LegacyPreparationReport report)
    {
        foreach (var byComponent in schedule.Operations.GroupBy(o => o.ComponentId, StringComparer.OrdinalIgnoreCase))
        {
            var component = graph.FindComponent(byComponent.Key);
            if (component is null)
            {
                continue;
            }

            component.Parameters["scheduled_operations"] = string.Join(",", byComponent.Select(o => o.OperationId));
            component.Parameters["scheduled_active_cycles"] = byComponent.Sum(o => o.EndCycle - o.StartCycle).ToString();
        }

        report.Notes.Add($"Built workload schedule with {schedule.Operations.Count} operation(s) over {schedule.TotalCycles} cycles.");
    }

    private static void ApplyWorkloadToSources(HardwareGraph graph, WorkloadGraph? workload, WorkloadMapping? mapping, LegacyPreparationReport report)
    {
        if (workload is null || workload.Operations.Count == 0)
        {
            return;
        }

        var orderedOperations = workload.TopologicalOrder();
        var firstOperation = orderedOperations[0];
        var source = graph.Components.FirstOrDefault(c => c.Type == ComponentKind.WorkloadSource);
        if (source is null)
        {
            report.Notes.Add("Workload was provided but no WorkloadSource component exists.");
            return;
        }

        source.Parameters["workload_id"] = workload.Id;
        source.Parameters["operation_id"] = firstOperation.Id;
        source.Parameters["precision"] = firstOperation.Precision.ToString();
        source.Parameters["packet_bits"] = orderedOperations.Max(o => PrecisionModel.PacketBitsFor(o)).ToString();
        source.Parameters["packet_count"] = orderedOperations.Sum(o => PrecisionModel.PacketCountFor(o)).ToString();
        foreach (var port in source.Ports.Where(p => p.Direction is PortDirection.Output or PortDirection.Bidirectional))
        {
            if (port.Precision == PrecisionKind.Any)
            {
                port.Precision = firstOperation.Precision;
            }
        }

        var mappedComponent = mapping?.ComponentFor(firstOperation.Id);
        if (!string.IsNullOrWhiteSpace(mappedComponent))
        {
            source.Parameters["mapping_target"] = mappedComponent;
        }

        foreach (var component in graph.Components.Where(c => c.Type == ComponentKind.ProcessingElement))
        {
            foreach (var port in component.Ports)
            {
                if (port.Precision == PrecisionKind.Any)
                {
                    port.Precision = firstOperation.Precision;
                }
            }
        }

        foreach (var entry in mapping?.Entries ?? [])
        {
            var component = graph.FindComponent(entry.ComponentId);
            if (component is not null)
            {
                component.Parameters["mapped_operations"] =
                    component.Parameters.TryGetValue("mapped_operations", out var existing) && !string.IsNullOrWhiteSpace(existing)
                        ? $"{existing},{entry.OperationId}"
                        : entry.OperationId;
            }
        }

        report.Notes.Add($"Mapped workload '{workload.Id}' as {source.Parameters["packet_count"]} packets with max packet size {source.Parameters["packet_bits"]} bits.");
    }

    private static void InsertPrecisionAdapters(
        HardwareGraph graph,
        WorkloadGraph? workload,
        WorkloadMapping? mapping,
        LegacyPreparationReport report)
    {
        if (workload is null || mapping is null || workload.Operations.Count == 0)
        {
            return;
        }

        var firstOperation = workload.TopologicalOrder()[0];
        var targetComponentId = mapping.ComponentFor(firstOperation.Id);
        if (string.IsNullOrWhiteSpace(targetComponentId))
        {
            return;
        }

        var source = graph.Components.FirstOrDefault(c => c.Type == ComponentKind.WorkloadSource);
        var target = graph.FindComponent(targetComponentId);
        if (source is null || target is null || target.Type != ComponentKind.ProcessingElement)
        {
            return;
        }

        var sourceOut = source.Ports.FirstOrDefault(p => p.Direction is PortDirection.Output or PortDirection.Bidirectional);
        var targetIn = target.Ports.FirstOrDefault(p => p.Direction is PortDirection.Input or PortDirection.Bidirectional);
        if (sourceOut is null || targetIn is null)
        {
            return;
        }

        var sourcePrecision = sourceOut.Precision == PrecisionKind.Any ? firstOperation.Precision : sourceOut.Precision;
        var targetPrecision = targetIn.Precision;
        if (targetPrecision == PrecisionKind.Any || sourcePrecision == targetPrecision)
        {
            return;
        }

        var directLink = graph.Links.FirstOrDefault(l =>
            string.Equals(l.Source.ComponentId, source.Id, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(l.Destination.ComponentId, target.Id, StringComparison.OrdinalIgnoreCase));
        if (directLink is null)
        {
            return;
        }

        var adapterKind = PrecisionModel.BitsPerElement(sourcePrecision) > PrecisionModel.BitsPerElement(targetPrecision)
            ? ComponentKind.Quantizer
            : ComponentKind.Dequantizer;
        var adapterId = UniqueId(graph, $"{source.Id}_{target.Id}_{adapterKind.ToString().ToLowerInvariant()}");
        var adapter = new HardwareComponent
        {
            Id = adapterId,
            Name = $"{sourcePrecision} to {targetPrecision} {adapterKind}",
            Type = adapterKind,
            Position = new GridPosition((source.Position.X + target.Position.X) / 2, (source.Position.Y + target.Position.Y) / 2),
            Ports =
            [
                new HardwarePort
                {
                    Name = "in",
                    Direction = PortDirection.Input,
                    Required = true,
                    Precision = sourcePrecision,
                    SignalType = sourceOut.SignalType,
                    DataType = sourceOut.DataType,
                    Protocol = sourceOut.Protocol
                },
                new HardwarePort
                {
                    Name = "out",
                    Direction = PortDirection.Output,
                    Required = true,
                    Precision = targetPrecision,
                    SignalType = targetIn.SignalType,
                    DataType = targetIn.DataType,
                    Protocol = targetIn.Protocol
                }
            ],
            Parameters =
            {
                ["source_precision"] = sourcePrecision.ToString(),
                ["target_precision"] = targetPrecision.ToString(),
                ["conversion_latency_cycles"] = "1",
                ["conversion_energy_pj_per_bit"] = "0.02",
                ["inserted_by_compiler"] = "precision_adapter"
            }
        };

        graph.Components.Add(adapter);
        graph.Links.Remove(directLink);
        graph.Links.Add(CloneLink(directLink, $"{directLink.Id}_to_{adapterId}", directLink.Source, new PortRef(adapterId, "in")));
        graph.Links.Add(CloneLink(directLink, $"{adapterId}_to_{directLink.Destination.ComponentId}", new PortRef(adapterId, "out"), directLink.Destination));
        report.Notes.Add($"Inserted {adapterKind} '{adapterId}' between '{source.Id}' and '{target.Id}' for {sourcePrecision}->{targetPrecision}.");
    }

    private static HardwareLink CloneLink(HardwareLink template, string id, PortRef source, PortRef destination) => new()
    {
        Id = id,
        Source = source,
        Destination = destination,
        BandwidthBitsPerCycle = template.BandwidthBitsPerCycle,
        LatencyCycles = template.LatencyCycles,
        EnergyPerBit = template.EnergyPerBit,
        RouteType = template.RouteType,
        PhysicalLength = template.PhysicalLength,
        ModelRef = template.ModelRef,
        Parameters = new Dictionary<string, string>(template.Parameters, StringComparer.OrdinalIgnoreCase)
    };

    private static string UniqueId(HardwareGraph graph, string baseId)
    {
        var id = baseId;
        var index = 1;
        while (graph.FindComponent(id) is not null)
        {
            id = $"{baseId}_{index++}";
        }

        return id;
    }

    private static void ApplyImportedModels(HardwareGraph graph, DeviceModelRegistry? modelRegistry, LegacyPreparationReport report)
    {
        if (modelRegistry is null)
        {
            return;
        }

        var applied = 0;
        foreach (var component in graph.Components)
        {
            var model = modelRegistry.Find(component.ModelRef);
            if (model is null)
            {
                continue;
            }

            DeviceModelBinding.ApplyToComponent(component, model);
            applied++;
        }

        foreach (var link in graph.Links)
        {
            var model = modelRegistry.Find(link.ModelRef);
            if (model is null)
            {
                continue;
            }

            DeviceModelBinding.ApplyToLink(link, model);
            applied++;
        }

        report.Notes.Add($"Applied {applied} imported device model binding(s).");
    }

    private static void ApplyAdvancedComponentEstimates(HardwareGraph graph, WorkloadGraph? workload, LegacyPreparationReport report)
    {
        var operationCount = workload?.Operations.Sum(o => Math.Max(1, o.OutputShape.ElementCount)) ?? 1;
        var cimComponents = graph.Components
            .Where(component => FirstPartyCimComponentPlugins.IsCrossbarTypeId(ComponentTypeIds.EffectiveTypeId(component)))
            .ToList();
        foreach (var component in cimComponents)
        {
            var estimate = CimModel.Estimate(new CimCrossbarParameters
            {
                Rows = component.GetIntParameter("rows", 128),
                Columns = component.GetIntParameter("columns", 128),
                AdcBits = component.GetIntParameter("adc_bits", 6),
                NoiseStandardDeviation = component.GetDoubleParameter("noise_stddev", 0),
                DeviceVariationStandardDeviation = component.GetDoubleParameter("device_variation_stddev", 0),
                MacEnergyPicojoules = component.GetDoubleParameter("mac_energy_pj", 0.02),
                AdcEnergyPicojoulesPerConversion = component.GetDoubleParameter("adc_energy_pj", 1.0),
                DacEnergyPicojoulesPerConversion = component.GetDoubleParameter("dac_energy_pj", 0.5),
                ReadLatencyCycles = component.GetDoubleParameter("read_latency_cycles", 2)
            }, operationCount);

            component.Parameters["cim_compute_energy_pj"] = estimate.ComputeEnergyPicojoules.ToString(System.Globalization.CultureInfo.InvariantCulture);
            component.Parameters["cim_conversion_energy_pj"] = estimate.ConversionEnergyPicojoules.ToString(System.Globalization.CultureInfo.InvariantCulture);
            component.Parameters["cim_read_latency_cycles"] = estimate.ReadLatencyCycles.ToString(System.Globalization.CultureInfo.InvariantCulture);
            component.Parameters["cim_quantization_step"] = estimate.QuantizationStep.ToString(System.Globalization.CultureInfo.InvariantCulture);
            component.Parameters["cim_noise_stddev"] = estimate.NoiseStandardDeviation.ToString(System.Globalization.CultureInfo.InvariantCulture);
            component.Parameters["cim_device_variation_stddev"] = estimate.DeviceVariationStandardDeviation.ToString(System.Globalization.CultureInfo.InvariantCulture);
            component.Parameters["cim_error_stddev"] = estimate.ErrorStandardDeviation.ToString(System.Globalization.CultureInfo.InvariantCulture);
            component.Parameters["cim_effective_precision_bits"] = estimate.EffectivePrecisionBits.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        if (cimComponents.Count > 0)
        {
            report.Notes.Add($"Applied CIM estimates to {cimComponents.Count} component(s).");
        }
    }

    private static double Manhattan(PhysicalPoint a, PhysicalPoint b) => Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y);

    private static double RouteLength(IReadOnlyList<PhysicalPoint> path)
    {
        if (path.Count < 2)
        {
            return 0;
        }

        var total = 0.0;
        for (var i = 1; i < path.Count; i++)
        {
            total += Manhattan(path[i - 1], path[i]);
        }

        return total;
    }
}
