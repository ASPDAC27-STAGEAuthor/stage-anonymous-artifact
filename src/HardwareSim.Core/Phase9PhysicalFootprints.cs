using System.Globalization;
using System.Text.Json;

#pragma warning disable CS1591

namespace HardwareSim.Core;

/// <summary>Physical hierarchy at which a footprint is valid.</summary>
public enum PhysicalFootprintScope { Cell, Array, Core, Macro, Chip, Package }

/// <summary>How footprint geometry was resolved.</summary>
public enum PhysicalFootprintSourceKind
{
    ReportedDimensions,
    ReportedAreaAndAspectRatio,
    ReportedAreaEstimatedGeometry,
    DerivedCellArray,
    TemplateFloorplan,
    EstimatedModel,
    Unknown
}

/// <summary>Placement visualization mode; readable minimum is explicitly non-proportional.</summary>
public enum PhysicalPlacementDisplayMode { PhysicalScale, ReadableMinimum }

/// <summary>Anchor location for a physical component port.</summary>
public enum PhysicalPortAnchorKind { Center, LeftEdge, RightEdge, TopEdge, BottomEdge, Explicit }

/// <summary>One optional explicit port anchor in micrometers relative to the physical device origin.</summary>
public sealed class PhysicalPortAnchor
{
    public PhysicalPortAnchorKind Kind { get; set; } = PhysicalPortAnchorKind.Explicit;
    public double OffsetXUm { get; set; }
    public double OffsetYUm { get; set; }
}


/// <summary>Resolved continuous geometry and complete evidence for one physical node.</summary>
public sealed record PhysicalFootprint(
    string SchemaVersion,
    PhysicalFootprintScope Scope,
    double? AreaUm2,
    double? WidthUm,
    double? HeightUm,
    double? AspectRatio,
    double KeepoutLeftUm,
    double KeepoutRightUm,
    double KeepoutTopUm,
    double KeepoutBottomUm,
    PhysicalFootprintSourceKind SourceKind,
    NormalizedDeviceEvidenceStatus EvidenceStatus,
    IReadOnlyList<string> SourceRecordIds,
    string EvidenceType,
    string MethodId,
    string ModelVersion,
    string Formula,
    string Uncertainty,
    string ValidContext,
    bool RotationAllowed,
    string FootprintHash)
{
    public const string CurrentSchemaVersion = "physical-footprint-1.0";
    public bool IsKnown => AreaUm2.HasValue && WidthUm.HasValue && HeightUm.HasValue;
    public double EnvelopeWidthUm => (WidthUm ?? 0) + KeepoutLeftUm + KeepoutRightUm;
    public double EnvelopeHeightUm => (HeightUm ?? 0) + KeepoutTopUm + KeepoutBottomUm;

    public static PhysicalFootprint Create(
        PhysicalFootprintScope scope,
        double? areaUm2,
        double? widthUm,
        double? heightUm,
        PhysicalFootprintSourceKind sourceKind,
        NormalizedDeviceEvidenceStatus evidenceStatus,
        IEnumerable<string>? sourceRecordIds,
        string evidenceType,
        string methodId,
        string modelVersion,
        string formula,
        string uncertainty,
        string validContext,
        bool rotationAllowed = true,
        double keepoutLeftUm = 0,
        double keepoutRightUm = 0,
        double keepoutTopUm = 0,
        double keepoutBottomUm = 0)
    {
        Validate(areaUm2, widthUm, heightUm, keepoutLeftUm, keepoutRightUm, keepoutTopUm, keepoutBottomUm);
        var records = (sourceRecordIds ?? []).Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal).ToArray();
        double? aspect = widthUm.HasValue && heightUm.HasValue && heightUm.Value > 0 ? widthUm.Value / heightUm.Value : null;
        var hash = ComponentTemplateJson.StableHash(new
        {
            SchemaVersion = CurrentSchemaVersion, scope, areaUm2, widthUm, heightUm, aspect,
            keepoutLeftUm, keepoutRightUm, keepoutTopUm, keepoutBottomUm, sourceKind, evidenceStatus,
            SourceRecordIds = records, evidenceType, methodId, modelVersion, formula, uncertainty, validContext, rotationAllowed
        });
        return new(CurrentSchemaVersion, scope, areaUm2, widthUm, heightUm, aspect,
            keepoutLeftUm, keepoutRightUm, keepoutTopUm, keepoutBottomUm, sourceKind, evidenceStatus,
            records, evidenceType ?? "", methodId ?? "", modelVersion ?? "", formula ?? "", uncertainty ?? "", validContext ?? "", rotationAllowed, hash);
    }

    public static PhysicalFootprint Unknown(PhysicalFootprintScope scope, string context, IEnumerable<string>? sourceRecordIds = null) =>
        Create(scope, null, null, null, PhysicalFootprintSourceKind.Unknown, NormalizedDeviceEvidenceStatus.Unknown,
            sourceRecordIds, "unknown", "explicit-unknown-footprint-v1", "1.0.0", "unknown", "unknown; no reliable geometry", context, false);

    private static void Validate(double? area, double? width, double? height, params double[] keepouts)
    {
        foreach (var value in new[] { area, width, height })
            if (value.HasValue && (!double.IsFinite(value.Value) || value.Value <= 0)) throw new ArgumentOutOfRangeException(nameof(area), "Known footprint dimensions and area must be finite and positive.");
        if (width.HasValue != height.HasValue) throw new ArgumentException("Width and height must be known or unknown together.");
        if (area.HasValue != width.HasValue) throw new ArgumentException("Known footprint area and geometry must be resolved together.");
        if (keepouts.Any(value => !double.IsFinite(value) || value < 0)) throw new ArgumentOutOfRangeException(nameof(keepouts), "Keepouts must be finite and nonnegative.");
    }
}

/// <summary>Quantized placement projection without modifying continuous truth.</summary>
public sealed record PhysicalFootprintPlacementProjection(
    PhysicalFootprint Footprint,
    int WidthCells,
    int HeightCells,
    double QuantizationSlackWidthUm,
    double QuantizationSlackHeightUm,
    double CellWidthUm,
    double CellHeightUm,
    PhysicalPlacementDisplayMode DisplayMode,
    bool NonProportionalRendering);

/// <summary>Frozen footprint resolution priority for literature profiles and template floorplans.</summary>
public static class PhysicalFootprintResolver
{
    public static PhysicalFootprint ResolveProfile(NormalizedDeviceProfile profile, PhysicalFootprintScope? requestedScope = null, int? rows = null, int? columns = null, int banks = 1, int bitSlices = 1)
    {
        if (profile is null) throw new ArgumentNullException(nameof(profile));
        var fields = profile.Fields.Where(field => field.HasValue).ToArray();
        var areaField = SelectArea(fields, requestedScope);
        if (areaField is null) return PhysicalFootprint.Unknown(requestedScope ?? InferScope(fields), profile.OperatingCorner, profile.SourceRecordIds);
        var area = Number(areaField);
        var scope = requestedScope ?? InferScope(areaField.Name);

        if (TryReportedDimensions(areaField, out var width, out var height))
        {
            return PhysicalFootprint.Create(scope, area, width, height, PhysicalFootprintSourceKind.ReportedDimensions,
                areaField.Provenance.Status, areaField.Provenance.SourceRecordIds, areaField.Provenance.EvidenceType,
                "reported-dimensions-envelope-v1", areaField.Provenance.ModelVersion, "width × height; area consistency checked",
                areaField.Provenance.Uncertainty, profile.OperatingCorner);
        }

        var aspectField = fields.FirstOrDefault(field => field.Name.Contains("aspect_ratio", StringComparison.OrdinalIgnoreCase));
        if (aspectField is not null)
        {
            var ratio = Number(aspectField);
            width = Math.Sqrt(area * ratio);
            height = Math.Sqrt(area / ratio);
            return PhysicalFootprint.Create(scope, area, width, height, PhysicalFootprintSourceKind.ReportedAreaAndAspectRatio,
                NormalizedDeviceEvidenceStatus.Derived, areaField.Provenance.SourceRecordIds.Concat(aspectField.Provenance.SourceRecordIds),
                areaField.Provenance.EvidenceType, "area-aspect-envelope-v1", "1.0.0", "width=sqrt(area*ratio); height=sqrt(area/ratio)",
                CombineUncertainty(areaField, aspectField), profile.OperatingCorner);
        }

        if (scope == PhysicalFootprintScope.Cell && rows.GetValueOrDefault() > 0 && columns.GetValueOrDefault() > 0)
        {
            var count = checked((long)rows!.Value * columns!.Value * Math.Max(1, banks) * Math.Max(1, bitSlices));
            var arrayArea = area * count;
            var side = Math.Sqrt(arrayArea);
            return PhysicalFootprint.Create(PhysicalFootprintScope.Array, arrayArea, side, side, PhysicalFootprintSourceKind.DerivedCellArray,
                NormalizedDeviceEvidenceStatus.Derived, areaField.Provenance.SourceRecordIds, areaField.Provenance.EvidenceType,
                "cell-array-envelope-v1", "1.0.0", "array_area=cell_area*rows*columns*banks*bit_slices; square geometry estimated",
                areaField.Provenance.Uncertainty, profile.OperatingCorner);
        }

        var square = Math.Sqrt(area);
        var geometryStatus = NormalizedDeviceEvidenceStatus.Estimated;
        return PhysicalFootprint.Create(scope, area, square, square, PhysicalFootprintSourceKind.ReportedAreaEstimatedGeometry,
            geometryStatus, areaField.Provenance.SourceRecordIds, areaField.Provenance.EvidenceType,
            "reported-area-square-envelope-v1", "1.0.0", "width=sqrt(area); height=sqrt(area); geometry only is estimated",
            AddGeometryUncertainty(areaField.Provenance.Uncertainty), profile.OperatingCorner);
    }

    public static PhysicalFootprint ResolveTemplateFloorplan(ComponentTemplate template, double pitchUm = 10, double packingOverheadFraction = 0.10)
    {
        if (template is null) throw new ArgumentNullException(nameof(template));
        if (!double.IsFinite(pitchUm) || pitchUm <= 0) throw new ArgumentOutOfRangeException(nameof(pitchUm));
        if (!double.IsFinite(packingOverheadFraction) || packingOverheadFraction < 0) throw new ArgumentOutOfRangeException(nameof(packingOverheadFraction));
        var structural = template.InternalBlocks.Where(block => block.Layer == InternalBlockLayer.Structural && block.AreaUm2 > 0).ToArray();
        if (structural.Length == 0) return PhysicalFootprint.Unknown(PhysicalFootprintScope.Macro, template.TemplateId);
        var layout = template.Views.FirstOrDefault(view => view.Kind == TemplateViewKind.StructuralPort)?.Layout ?? new Dictionary<string, GridPosition>();
        var maxX = 0d;
        var maxY = 0d;
        var minX = double.MaxValue;
        var minY = double.MaxValue;
        foreach (var block in structural)
        {
            var position = layout.TryGetValue(block.Id, out var value) ? value : new GridPosition(Array.IndexOf(structural, block), 0);
            var side = Math.Sqrt(block.AreaUm2);
            var x = position.X * pitchUm;
            var y = position.Y * pitchUm;
            minX = Math.Min(minX, x);
            minY = Math.Min(minY, y);
            maxX = Math.Max(maxX, x + side);
            maxY = Math.Max(maxY, y + side);
        }
        var width = (maxX - minX) * (1 + packingOverheadFraction);
        var height = (maxY - minY) * (1 + packingOverheadFraction);
        var boundingArea = width * height;
        var internalArea = structural.Sum(block => block.AreaUm2);
        if (boundingArea + 1e-9 < internalArea) throw new InvalidOperationException("Template floorplan bounding area cannot be smaller than additive internal physical area.");
        return PhysicalFootprint.Create(PhysicalFootprintScope.Macro, boundingArea, width, height,
            PhysicalFootprintSourceKind.TemplateFloorplan, NormalizedDeviceEvidenceStatus.Derived,
            template.ProfileBindings.SelectMany(binding => binding.Snapshot is null ? Array.Empty<string>() : new[] { binding.Snapshot.Hash }),
            "template_floorplan", "template-floorplan-bounding-envelope-v1", "1.0.0",
            "bounding envelope of positioned internal footprints × explicit packing overhead; internal areas counted once",
            $"packing overhead fraction={packingOverheadFraction.ToString("R", CultureInfo.InvariantCulture)}", template.TemplateId);
    }

    public static PhysicalFootprintPlacementProjection Quantize(PhysicalFootprint footprint, double cellWidthUm, double cellHeightUm, PhysicalPlacementDisplayMode displayMode = PhysicalPlacementDisplayMode.PhysicalScale, int readableMinimumCells = 2)
    {
        if (!footprint.IsKnown) throw new InvalidOperationException("Unknown footprint cannot be projected to occupied cells.");
        if (!double.IsFinite(cellWidthUm) || cellWidthUm <= 0 || !double.IsFinite(cellHeightUm) || cellHeightUm <= 0) throw new ArgumentOutOfRangeException(nameof(cellWidthUm));
        var physicalWidth = Math.Max(1, checked((int)Math.Ceiling(footprint.EnvelopeWidthUm / cellWidthUm)));
        var physicalHeight = Math.Max(1, checked((int)Math.Ceiling(footprint.EnvelopeHeightUm / cellHeightUm)));
        var width = displayMode == PhysicalPlacementDisplayMode.ReadableMinimum ? Math.Max(physicalWidth, Math.Max(1, readableMinimumCells)) : physicalWidth;
        var height = displayMode == PhysicalPlacementDisplayMode.ReadableMinimum ? Math.Max(physicalHeight, Math.Max(1, readableMinimumCells)) : physicalHeight;
        return new(footprint, width, height, width * cellWidthUm - footprint.EnvelopeWidthUm, height * cellHeightUm - footprint.EnvelopeHeightUm,
            cellWidthUm, cellHeightUm, displayMode, displayMode == PhysicalPlacementDisplayMode.ReadableMinimum);
    }

    public static double SuggestCellResolutionUm(IEnumerable<PhysicalFootprint> footprints, int targetCellsAcrossMedian = 8)
    {
        var spans = footprints.Where(value => value.IsKnown).SelectMany(value => new[] { value.EnvelopeWidthUm, value.EnvelopeHeightUm }).OrderBy(value => value).ToArray();
        if (spans.Length == 0) return 100;
        var median = spans[spans.Length / 2];
        return Math.Max(median / Math.Max(1, targetCellsAcrossMedian), 0.001);
    }

    private static NormalizedDeviceField? SelectArea(IEnumerable<NormalizedDeviceField> fields, PhysicalFootprintScope? requestedScope)
    {
        var candidates = fields.Where(field => string.Equals(field.CanonicalUnits, "um2", StringComparison.Ordinal) || string.Equals(field.CanonicalUnits, "um2/cell", StringComparison.Ordinal)).ToArray();
        if (requestedScope.HasValue)
        {
            var exact = candidates.FirstOrDefault(field => InferScope(field.Name) == requestedScope.Value);
            if (exact is not null) return exact;
        }
        return candidates.OrderBy(field => AreaPriority(field.Name)).ThenBy(field => field.Key, StringComparer.Ordinal).FirstOrDefault();
    }

    private static int AreaPriority(string name) => name switch
    {
        "area_um2" => 0,
        "array_area_um2" => 1,
        "core_area_um2" or "core_total_area_um2" => 2,
        "chip_total_area_um2" => 3,
        "area_um2_per_cell" => 4,
        _ => 10
    };
    private static PhysicalFootprintScope InferScope(IEnumerable<NormalizedDeviceField> fields) => InferScope(fields.FirstOrDefault(field => field.CanonicalUnits.StartsWith("um2", StringComparison.Ordinal))?.Name ?? "");
    private static PhysicalFootprintScope InferScope(string name)
    {
        var normalized = name.ToLowerInvariant();
        if (normalized.Contains("per_cell") || normalized.Contains("cell_area")) return PhysicalFootprintScope.Cell;
        if (normalized.Contains("array")) return PhysicalFootprintScope.Array;
        if (normalized.Contains("chip")) return PhysicalFootprintScope.Chip;
        if (normalized.Contains("macro")) return PhysicalFootprintScope.Macro;
        return PhysicalFootprintScope.Core;
    }
    private static double Number(NormalizedDeviceField field) => field.Value.ValueKind == JsonValueKind.Number ? field.Value.GetDouble() : throw new InvalidDataException($"Footprint field '{field.Key}' must be numeric.");
    private static bool TryReportedDimensions(NormalizedDeviceField area, out double width, out double height)
    {
        width = height = 0;
        if (!area.Provenance.Formula.Contains("width", StringComparison.OrdinalIgnoreCase) || !area.Provenance.Formula.Contains("height", StringComparison.OrdinalIgnoreCase)) return false;
        var inputs = area.Provenance.Inputs.Where(input => input.Value.ValueKind == JsonValueKind.Number).ToArray();
        if (inputs.Length < 2) return false;
        try
        {
            width = PhysicalUnitConverter.Convert(inputs[0].Value.GetDouble(), inputs[0].Units, "um");
            height = PhysicalUnitConverter.Convert(inputs[1].Value.GetDouble(), inputs[1].Units, "um");
            return width > 0 && height > 0 && Math.Abs(width * height - Number(area)) <= Math.Max(1e-6, Number(area) * 1e-6);
        }
        catch (ArgumentException) { return false; }
    }
    private static string CombineUncertainty(params NormalizedDeviceField[] fields) => string.Join("; ", fields.Select(field => field.Provenance.Uncertainty).Distinct(StringComparer.Ordinal));
    private static string AddGeometryUncertainty(string uncertainty) => (uncertainty ?? "") + "; square envelope is estimated and not reported geometry";
}

/// <summary>Applies a profile binding while invalidating all dependent authorities.</summary>
public static class Phase9ProfileBindingAuthority
{
    public static void Apply(HardwareComponent component, NormalizedDeviceProfile profile, PhysicalFootprint footprint, ProjectDirtyState dirtyState)
    {
        if (component is null || profile is null || footprint is null || dirtyState is null) throw new ArgumentNullException();
        var previousProfileHash = component.Parameters.GetValueOrDefault(Phase9DeviceRuntimeKeys.ProfileHash, "");
        var previousFootprintHash = component.Parameters.GetValueOrDefault("physical_footprint_hash", "");
        component.Parameters[Phase9DeviceRuntimeKeys.ProfileId] = profile.ProfileId;
        component.Parameters[Phase9DeviceRuntimeKeys.ProfileHash] = profile.ProfileHash;
        component.Parameters[Phase9DeviceRuntimeKeys.DeviceFamily] = profile.DeviceFamily;
        component.Parameters[Phase9DeviceRuntimeKeys.OperatingCorner] = profile.OperatingCorner;
        component.Parameters["physical_footprint_hash"] = footprint.FootprintHash;
        if (!string.Equals(previousProfileHash, profile.ProfileHash, StringComparison.Ordinal) || !string.Equals(previousFootprintHash, footprint.FootprintHash, StringComparison.Ordinal))
        {
            dirtyState.MarkModelBindingChanged();
            dirtyState.MarkMappingChanged();
            dirtyState.MarkPlacementChanged();
            dirtyState.MarkRoutingChanged();
        }
    }
}
