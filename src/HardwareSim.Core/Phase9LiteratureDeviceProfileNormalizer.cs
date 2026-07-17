using System.Globalization;
using System.Text.Json;

namespace HardwareSim.Core;

/// <summary>Normalizes the verified Phase 7C catalog into sparse Phase 9 device profiles.</summary>
public static class Phase9LiteratureDeviceProfileNormalizer
{
    private static readonly string[] RequiredCapabilityVocabulary =
    [
        NormalizedDeviceCapabilityIds.Footprint,
        NormalizedDeviceCapabilityIds.Timing,
        NormalizedDeviceCapabilityIds.Energy,
        NormalizedDeviceCapabilityIds.Storage,
        NormalizedDeviceCapabilityIds.Precision,
        NormalizedDeviceCapabilityIds.NonIdeality
    ];

    /// <summary>Creates a versioned package without modifying source records or workbook hashes.</summary>
    public static NormalizedDeviceProfilePackage Normalize(Phase7CLiteratureCharacterizationCatalog catalog)
    {
        if (catalog is null) throw new ArgumentNullException(nameof(catalog));
        if (!catalog.IsValid) throw new InvalidOperationException("Cannot normalize an invalid literature catalog.");

        var profiles = catalog.Records.GroupBy(record => record.ProfileId, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => CreateProfile(group.Key, group.ToArray())).ToArray();
        var catalogHash = ComponentTemplateJson.StableHash(new
        {
            catalog.CatalogId,
            catalog.SourceWorkbookSha256,
            RecordIds = catalog.Records.Select(record => record.RecordId).OrderBy(value => value, StringComparer.Ordinal),
            SourceRecordHashes = catalog.Records.Select(record => record.SourceRecordSha256).OrderBy(value => value, StringComparer.Ordinal)
        });
        return new("phase9.real-literature.normalized-device-profiles.v1", "1.0.0", catalog.CatalogId, catalogHash, profiles);
    }

    internal static IReadOnlyList<string> ClassifyCapabilities(string fieldName)
    {
        var value = fieldName.ToLowerInvariant();
        var capabilities = new HashSet<string>(StringComparer.Ordinal);
        if (ContainsAny(value, "area", "width", "height", "footprint", "pitch")) capabilities.Add(NormalizedDeviceCapabilityIds.Footprint);
        if (ContainsAny(value, "latency", "delay", "period", "sample_rate", "frequency", "throughput", "bandwidth", "clock")) capabilities.Add(NormalizedDeviceCapabilityIds.Timing);
        if (ContainsAny(value, "energy", "power", "efficiency")) capabilities.Add(NormalizedDeviceCapabilityIds.Energy);
        if (ContainsAny(value, "rows", "columns", "array_rows", "array_cols", "capacity", "physical_cells", "cells_per", "buffer_bits", "cell_bits")) capabilities.Add(NormalizedDeviceCapabilityIds.Storage);
        if (ContainsAny(value, "bits", "dtype", "format", "precision", "mantissa", "fraction", "exponent", "enob", "signed")) capabilities.Add(NormalizedDeviceCapabilityIds.Precision);
        if (ContainsAny(value, "conductance", "resistance", "voltage", "variation", "noise", "dnl", "inl", "sndr", "sfdr", "retention", "programming_success", "temperature")) capabilities.Add(NormalizedDeviceCapabilityIds.NonIdeality);
        return capabilities.OrderBy(capability => capability, StringComparer.Ordinal).ToArray();
    }

    private static NormalizedDeviceProfile CreateProfile(string profileId, IReadOnlyList<Phase7CLiteratureRecord> records)
    {
        var fields = records.Select(CreateField).OrderBy(field => field.Key, StringComparer.Ordinal).ToArray();
        var capabilityFields = RequiredCapabilityVocabulary.ToDictionary(
            capability => capability,
            capability => fields.Where(field => ClassifyCapabilities(field.Name).Contains(capability, StringComparer.Ordinal)).Select(field => field.Key).ToArray(),
            StringComparer.Ordinal);
        var fieldsByKey = fields.ToDictionary(field => field.Key, StringComparer.Ordinal);
        var capabilities = capabilityFields.Where(pair => pair.Value.Any(key => fieldsByKey[key].HasValue)).Select(pair => pair.Key).ToArray();
        var missingCapabilities = RequiredCapabilityVocabulary.Except(capabilities, StringComparer.Ordinal).ToArray();
        var processFields = fields.Where(field => field.HasValue && (field.Name == "process_node_nm" || field.Name == "process_node")).ToArray();
        var technology = processFields.Length == 0
            ? records.Select(record => record.TechnicalContext).FirstOrDefault() ?? "unknown"
            : string.Join(" | ", processFields.Select(field => DisplayValue(field.Value) + (string.IsNullOrWhiteSpace(field.CanonicalUnits) ? "" : " " + field.CanonicalUnits)).Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal));
        var calibrationStates = fields.Select(field => field.Provenance.CalibrationState).Distinct().ToArray();
        var calibrationState = calibrationStates.Length == 1 ? calibrationStates[0] : NormalizedDeviceCalibrationState.Mixed;
        var subcontracts = new NormalizedDeviceSubcontracts(
            capabilityFields[NormalizedDeviceCapabilityIds.Footprint],
            capabilityFields[NormalizedDeviceCapabilityIds.Timing],
            capabilityFields[NormalizedDeviceCapabilityIds.Energy],
            capabilityFields[NormalizedDeviceCapabilityIds.Storage],
            capabilityFields[NormalizedDeviceCapabilityIds.Precision],
            capabilityFields[NormalizedDeviceCapabilityIds.NonIdeality]);

        return new NormalizedDeviceProfile(
            profileId,
            profileId,
            string.Join("+", records.Select(record => record.Component).Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal)),
            technology,
            string.Join(" | ", records.Select(record => record.TechnicalContext).Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal)),
            records.Select(record => record.Mode).ToArray(),
            capabilities,
            missingCapabilities,
            fields.Where(field => !field.HasValue).Select(field => field.Name + "|" + field.Mode).ToArray(),
            records.Select(record => record.SourceId).ToArray(),
            records.Select(record => record.RecordId).ToArray(),
            records.Select(record => record.SourceRecordSha256).ToArray(),
            records.Select(record => record.EvidenceType).ToArray(),
            records.Select(record => record.EvidenceGrade).ToArray(),
            calibrationState,
            fields,
            subcontracts);
    }
    private static NormalizedDeviceField CreateField(Phase7CLiteratureRecord record)
    {
        var status = record.DataOrigin switch
        {
            "reported" => NormalizedDeviceEvidenceStatus.Reported,
            "unit_converted" => NormalizedDeviceEvidenceStatus.Derived,
            "derived_from_reported" => NormalizedDeviceEvidenceStatus.Derived,
            "interpolated" => NormalizedDeviceEvidenceStatus.Interpolated,
            "estimated" => NormalizedDeviceEvidenceStatus.Estimated,
            "user_override" => NormalizedDeviceEvidenceStatus.UserOverride,
            "gap" => NormalizedDeviceEvidenceStatus.Unknown,
            _ => throw new InvalidOperationException($"Unsupported data origin '{record.DataOrigin}' for record '{record.RecordId}'.")
        };
        var calibrationState = record.EvidenceType.Contains("standard", StringComparison.OrdinalIgnoreCase) || record.EvidenceType.Contains("specification", StringComparison.OrdinalIgnoreCase)
            ? NormalizedDeviceCalibrationState.NotApplicable
            : record.Calibrated ? NormalizedDeviceCalibrationState.Calibrated : NormalizedDeviceCalibrationState.Uncalibrated;
        var inputs = new List<NormalizedDeviceProvenanceInput>();
        AddInput(inputs, "reported_value_a", record.ReportedValueA, record.ReportedUnitsA);
        AddInput(inputs, "auxiliary_value_b", record.AuxiliaryValueB, record.AuxiliaryUnitsB);
        AddInput(inputs, "auxiliary_value_c", record.AuxiliaryValueC, record.AuxiliaryUnitsC);
        var methodId = record.DataOrigin switch
        {
            "reported" => "literature-reported-exact-point",
            "unit_converted" => "canonical-unit-conversion-v1",
            "derived_from_reported" => "literature-derived-formula-v1",
            "interpolated" => "same-source-interpolation-v1",
            "estimated" => "explicit-estimation-model-v1",
            "user_override" => "user-override-v1",
            _ => "explicit-unknown-gap-v1"
        };
        var formula = string.IsNullOrWhiteSpace(record.ConversionFormula)
            ? status == NormalizedDeviceEvidenceStatus.Reported ? "identity" : "not_available"
            : record.ConversionFormula;
        var uncertainty = status == NormalizedDeviceEvidenceStatus.Unknown
            ? "unknown; no reliable value in the approved source record"
            : string.IsNullOrWhiteSpace(record.Notes) ? $"not quantified; evidence grade {record.EvidenceGrade}" : record.Notes;
        var canonicalUnits = string.IsNullOrWhiteSpace(record.NormalizedUnits) ? "" : PhysicalUnitConverter.Canonicalize(record.NormalizedUnits);
        var provenance = new NormalizedDeviceFieldProvenance(
            status,
            record.EvidenceType,
            record.EvidenceGrade,
            calibrationState,
            [record.SourceId],
            [record.RecordId],
            [record.SourceRecordSha256],
            methodId,
            record.ProfileVersion,
            formula,
            inputs,
            record.ValidRange,
            uncertainty,
            record.Doi + "#" + record.EvidenceLocator,
            record.Notes);
        return new(
            record.Field + "|" + record.Mode + "|" + record.RecordId,
            record.Field,
            record.Mode,
            record.NormalizedValue,
            canonicalUnits,
            record.ValidRange,
            record.InterpolationMethod,
            record.ExtrapolationPolicy,
            provenance);
    }

    private static void AddInput(List<NormalizedDeviceProvenanceInput> inputs, string name, JsonElement value, string units)
    {
        if (value.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined) inputs.Add(new(name, value, units));
    }

    private static bool ContainsAny(string value, params string[] tokens) => tokens.Any(value.Contains);

    private static string DisplayValue(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Number when value.TryGetInt64(out var integer) => integer.ToString(CultureInfo.InvariantCulture),
        JsonValueKind.Number => value.GetDouble().ToString("R", CultureInfo.InvariantCulture),
        JsonValueKind.String => value.GetString() ?? "",
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        _ => "unknown"
    };
}