using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;

namespace HardwareSim.Core;

/// <summary>Identifies the evidence status of one normalized device field.</summary>
public enum NormalizedDeviceEvidenceStatus
{
    /// <summary>The value is reported directly by its source.</summary>
    Reported,
    /// <summary>The value is derived from identified inputs and a frozen formula.</summary>
    Derived,
    /// <summary>The value is interpolated inside an approved same-source range.</summary>
    Interpolated,
    /// <summary>The value is estimated by an explicit model.</summary>
    Estimated,
    /// <summary>The value is an explicit user override.</summary>
    UserOverride,
    /// <summary>No reliable value is available.</summary>
    Unknown
}

/// <summary>Identifies the calibration state carried by evidence.</summary>
public enum NormalizedDeviceCalibrationState
{
    /// <summary>The calibration state is not known.</summary>
    Unknown,
    /// <summary>The source or model is calibrated.</summary>
    Calibrated,
    /// <summary>The source or model is not calibrated.</summary>
    Uncalibrated,
    /// <summary>Calibration is not applicable to a specification or standard.</summary>
    NotApplicable,
    /// <summary>The profile contains both calibrated and uncalibrated fields.</summary>
    Mixed
}

/// <summary>Identifies whether a requested device fact is usable.</summary>
public enum NormalizedDeviceAvailability
{
    /// <summary>The requested fact is available.</summary>
    Available,
    /// <summary>The requested fact is unknown without preventing unrelated use.</summary>
    Unknown
}

/// <summary>Defines open capability identifiers used by the normalized profile contract.</summary>
public static class NormalizedDeviceCapabilityIds
{
    /// <summary>Physical footprint characterization.</summary>
    public const string Footprint = "device.footprint";
    /// <summary>Timing and throughput characterization.</summary>
    public const string Timing = "device.timing";
    /// <summary>Energy and power characterization.</summary>
    public const string Energy = "device.energy";
    /// <summary>Storage geometry and capacity characterization.</summary>
    public const string Storage = "device.storage";
    /// <summary>Precision and numeric format characterization.</summary>
    public const string Precision = "device.precision";
    /// <summary>Non-ideality and operating-point characterization.</summary>
    public const string NonIdeality = "device.non_ideality";
}

/// <summary>Describes one source input used to derive, interpolate, estimate, or override a field.</summary>
public sealed class NormalizedDeviceProvenanceInput
{
    internal NormalizedDeviceProvenanceInput(string name, JsonElement value, string units)
    {
        Name = name;
        Value = CloneOrNull(value);
        Units = units ?? "";
    }

    /// <summary>Gets the stable input name.</summary>
    public string Name { get; }
    /// <summary>Gets the exact scalar, Boolean, string, or null input value.</summary>
    public JsonElement Value { get; }
    /// <summary>Gets source units without pretending semantic equivalence.</summary>
    public string Units { get; }

    internal static JsonElement CloneOrNull(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Undefined) return value.Clone();
        using var document = JsonDocument.Parse("null");
        return document.RootElement.Clone();
    }
}

/// <summary>Preserves evidence, source hashes, method, range, and uncertainty for one field.</summary>
public sealed class NormalizedDeviceFieldProvenance
{
    internal NormalizedDeviceFieldProvenance(
        NormalizedDeviceEvidenceStatus status,
        string evidenceType,
        string evidenceGrade,
        NormalizedDeviceCalibrationState calibrationState,
        IReadOnlyList<string> sourceIds,
        IReadOnlyList<string> sourceRecordIds,
        IReadOnlyList<string> sourceRecordHashes,
        string methodId,
        string modelVersion,
        string formula,
        IReadOnlyList<NormalizedDeviceProvenanceInput> inputs,
        string applicableRange,
        string uncertainty,
        string evidenceLocator,
        string notes)
    {
        Status = status;
        EvidenceType = evidenceType;
        EvidenceGrade = evidenceGrade;
        CalibrationState = calibrationState;
        SourceIds = Freeze(sourceIds);
        SourceRecordIds = Freeze(sourceRecordIds);
        SourceRecordHashes = Freeze(sourceRecordHashes);
        MethodId = methodId;
        ModelVersion = modelVersion;
        Formula = formula;
        Inputs = new ReadOnlyCollection<NormalizedDeviceProvenanceInput>(inputs.ToArray());
        ApplicableRange = applicableRange;
        Uncertainty = uncertainty;
        EvidenceLocator = evidenceLocator;
        Notes = notes;
    }

    /// <summary>Gets the field evidence status.</summary>
    public NormalizedDeviceEvidenceStatus Status { get; }
    /// <summary>Gets the source evidence type.</summary>
    public string EvidenceType { get; }
    /// <summary>Gets the source evidence grade.</summary>
    public string EvidenceGrade { get; }
    /// <summary>Gets the field calibration state.</summary>
    public NormalizedDeviceCalibrationState CalibrationState { get; }
    /// <summary>Gets stable source identifiers.</summary>
    public IReadOnlyList<string> SourceIds { get; }
    /// <summary>Gets collision-safe import record identifiers.</summary>
    public IReadOnlyList<string> SourceRecordIds { get; }
    /// <summary>Gets original source-row hashes.</summary>
    public IReadOnlyList<string> SourceRecordHashes { get; }
    /// <summary>Gets the provenance method.</summary>
    public string MethodId { get; }
    /// <summary>Gets the method or model version.</summary>
    public string ModelVersion { get; }
    /// <summary>Gets the exact formula or identity rule.</summary>
    public string Formula { get; }
    /// <summary>Gets explicit method inputs.</summary>
    public IReadOnlyList<NormalizedDeviceProvenanceInput> Inputs { get; }
    /// <summary>Gets the approved applicability range.</summary>
    public string ApplicableRange { get; }
    /// <summary>Gets quantified or qualitative uncertainty.</summary>
    public string Uncertainty { get; }
    /// <summary>Gets the precise evidence locator.</summary>
    public string EvidenceLocator { get; }
    /// <summary>Gets usage limitations and source notes.</summary>
    public string Notes { get; }

    private static IReadOnlyList<string> Freeze(IEnumerable<string> values) =>
        new ReadOnlyCollection<string>(values.Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal).ToArray());
}
/// <summary>Describes one sparse, canonical-unit device field and its provenance.</summary>
public sealed class NormalizedDeviceField
{
    internal NormalizedDeviceField(string key, string name, string mode, JsonElement value, string canonicalUnits, string validRange, string interpolationPolicy, string extrapolationPolicy, NormalizedDeviceFieldProvenance provenance)
    {
        Key = key;
        Name = name;
        Mode = mode;
        Value = NormalizedDeviceProvenanceInput.CloneOrNull(value);
        CanonicalUnits = canonicalUnits;
        ValidRange = validRange;
        InterpolationPolicy = interpolationPolicy;
        ExtrapolationPolicy = extrapolationPolicy;
        Provenance = provenance;
    }

    /// <summary>Gets the stable record-discriminating field key.</summary>
    public string Key { get; }
    /// <summary>Gets the semantic field name.</summary>
    public string Name { get; }
    /// <summary>Gets the exact operating mode or precision label.</summary>
    public string Mode { get; }
    /// <summary>Gets the canonical scalar, Boolean, string, or null value.</summary>
    public JsonElement Value { get; }
    /// <summary>Gets canonical units; semantic energy denominators remain distinct.</summary>
    public string CanonicalUnits { get; }
    /// <summary>Gets the valid range.</summary>
    public string ValidRange { get; }
    /// <summary>Gets the interpolation policy.</summary>
    public string InterpolationPolicy { get; }
    /// <summary>Gets the extrapolation policy.</summary>
    public string ExtrapolationPolicy { get; }
    /// <summary>Gets complete per-field provenance.</summary>
    public NormalizedDeviceFieldProvenance Provenance { get; }
    /// <summary>Gets whether the field has a usable non-null value.</summary>
    public bool HasValue => Value.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined && Provenance.Status != NormalizedDeviceEvidenceStatus.Unknown;
}

/// <summary>Groups profile fields into open subcontracts.</summary>
public sealed class NormalizedDeviceSubcontracts
{
    internal NormalizedDeviceSubcontracts(IReadOnlyList<string> footprint, IReadOnlyList<string> timing, IReadOnlyList<string> energy, IReadOnlyList<string> storage, IReadOnlyList<string> precision, IReadOnlyList<string> nonIdeality)
    {
        FootprintFieldKeys = Freeze(footprint);
        TimingFieldKeys = Freeze(timing);
        EnergyFieldKeys = Freeze(energy);
        StorageFieldKeys = Freeze(storage);
        PrecisionFieldKeys = Freeze(precision);
        NonIdealityFieldKeys = Freeze(nonIdeality);
    }

    /// <summary>Gets footprint field keys.</summary>
    public IReadOnlyList<string> FootprintFieldKeys { get; }
    /// <summary>Gets timing field keys.</summary>
    public IReadOnlyList<string> TimingFieldKeys { get; }
    /// <summary>Gets energy and power field keys.</summary>
    public IReadOnlyList<string> EnergyFieldKeys { get; }
    /// <summary>Gets storage field keys.</summary>
    public IReadOnlyList<string> StorageFieldKeys { get; }
    /// <summary>Gets precision field keys.</summary>
    public IReadOnlyList<string> PrecisionFieldKeys { get; }
    /// <summary>Gets non-ideality field keys.</summary>
    public IReadOnlyList<string> NonIdealityFieldKeys { get; }

    private static IReadOnlyList<string> Freeze(IEnumerable<string> values) =>
        new ReadOnlyCollection<string>(values.Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal).ToArray());
}

/// <summary>Describes a structured missing-capability or missing-field issue.</summary>
public sealed class NormalizedDeviceProfileIssue
{
    internal NormalizedDeviceProfileIssue(string code, string path, string message, string requestedCapability)
    {
        Code = code;
        Path = path;
        Message = message;
        RequestedCapability = requestedCapability;
    }

    /// <summary>Gets the stable issue code.</summary>
    public string Code { get; }
    /// <summary>Gets the profile path.</summary>
    public string Path { get; }
    /// <summary>Gets the human-readable issue message.</summary>
    public string Message { get; }
    /// <summary>Gets the requested capability or field.</summary>
    public string RequestedCapability { get; }
}

/// <summary>Returns an available field set or an explicit unknown result.</summary>
public sealed class NormalizedDeviceResolution
{
    internal NormalizedDeviceResolution(NormalizedDeviceAvailability availability, IReadOnlyList<NormalizedDeviceField> fields, NormalizedDeviceProfileIssue? issue)
    {
        Availability = availability;
        Fields = new ReadOnlyCollection<NormalizedDeviceField>(fields.ToArray());
        Issue = issue;
    }

    /// <summary>Gets whether the request is available or unknown.</summary>
    public NormalizedDeviceAvailability Availability { get; }
    /// <summary>Gets matching usable fields.</summary>
    public IReadOnlyList<NormalizedDeviceField> Fields { get; }
    /// <summary>Gets the structured unknown issue, if any.</summary>
    public NormalizedDeviceProfileIssue? Issue { get; }
}

/// <summary>Represents one sparse device profile with canonical units and complete per-field evidence.</summary>
public sealed class NormalizedDeviceProfile
{
    /// <summary>The current normalized device profile schema.</summary>
    public const string CurrentSchemaVersion = "normalized-device-profile-1.0";

    internal NormalizedDeviceProfile(string profileId, string displayName, string deviceFamily, string technology, string operatingCorner, IReadOnlyList<string> modesAndPrecisions, IReadOnlyList<string> capabilities, IReadOnlyList<string> missingCapabilities, IReadOnlyList<string> missingFields, IReadOnlyList<string> sourceIds, IReadOnlyList<string> sourceRecordIds, IReadOnlyList<string> sourceRecordHashes, IReadOnlyList<string> evidenceTypes, IReadOnlyList<string> evidenceGrades, NormalizedDeviceCalibrationState calibrationState, IReadOnlyList<NormalizedDeviceField> fields, NormalizedDeviceSubcontracts subcontracts)
    {
        SchemaVersion = CurrentSchemaVersion;
        ProfileId = profileId;
        DisplayName = displayName;
        DeviceFamily = deviceFamily;
        Technology = technology;
        OperatingCorner = operatingCorner;
        ModesAndPrecisions = Freeze(modesAndPrecisions);
        Capabilities = Freeze(capabilities);
        MissingCapabilities = Freeze(missingCapabilities);
        MissingFields = Freeze(missingFields);
        SourceIds = Freeze(sourceIds);
        SourceRecordIds = Freeze(sourceRecordIds);
        SourceRecordHashes = Freeze(sourceRecordHashes);
        EvidenceTypes = Freeze(evidenceTypes);
        EvidenceGrades = Freeze(evidenceGrades);
        CalibrationState = calibrationState;
        Fields = new ReadOnlyCollection<NormalizedDeviceField>(fields.OrderBy(field => field.Key, StringComparer.Ordinal).ToArray());
        Subcontracts = subcontracts;
        ProfileHash = ComputeSemanticHash(this);
    }

    /// <summary>Gets the profile schema version.</summary>
    public string SchemaVersion { get; }
    /// <summary>Gets the stable profile identifier.</summary>
    public string ProfileId { get; }
    /// <summary>Gets display-only metadata excluded from the profile hash.</summary>
    public string DisplayName { get; }
    /// <summary>Gets the open device-family descriptor.</summary>
    public string DeviceFamily { get; }
    /// <summary>Gets the technology or process descriptor.</summary>
    public string Technology { get; }
    /// <summary>Gets the operating corner and technical context.</summary>
    public string OperatingCorner { get; }
    /// <summary>Gets exact modes and precision labels.</summary>
    public IReadOnlyList<string> ModesAndPrecisions { get; }
    /// <summary>Gets available open capabilities.</summary>
    public IReadOnlyList<string> Capabilities { get; }
    /// <summary>Gets high-level capabilities that remain unknown.</summary>
    public IReadOnlyList<string> MissingCapabilities { get; }
    /// <summary>Gets explicitly unknown fields without zero filling.</summary>
    public IReadOnlyList<string> MissingFields { get; }
    /// <summary>Gets source identifiers.</summary>
    public IReadOnlyList<string> SourceIds { get; }
    /// <summary>Gets collision-safe record identifiers.</summary>
    public IReadOnlyList<string> SourceRecordIds { get; }
    /// <summary>Gets original source record hashes.</summary>
    public IReadOnlyList<string> SourceRecordHashes { get; }
    /// <summary>Gets evidence types present in the profile.</summary>
    public IReadOnlyList<string> EvidenceTypes { get; }
    /// <summary>Gets evidence grades present in the profile.</summary>
    public IReadOnlyList<string> EvidenceGrades { get; }
    /// <summary>Gets the aggregate calibration state.</summary>
    public NormalizedDeviceCalibrationState CalibrationState { get; }
    /// <summary>Gets every sparse field, including explicit unknown gaps.</summary>
    public IReadOnlyList<NormalizedDeviceField> Fields { get; }
    /// <summary>Gets footprint, timing, energy, storage, precision, and non-ideality projections.</summary>
    public NormalizedDeviceSubcontracts Subcontracts { get; }
    /// <summary>Gets the deterministic semantic profile hash.</summary>
    public string ProfileHash { get; }
    /// <summary>Resolves one high-level capability without blocking unrelated simulation.</summary>
    public NormalizedDeviceResolution ResolveCapability(string capabilityId)
    {
        if (Capabilities.Contains(capabilityId, StringComparer.Ordinal))
        {
            var keys = CapabilityFieldKeys(Subcontracts, capabilityId);
            return new(NormalizedDeviceAvailability.Available, Fields.Where(field => keys.Contains(field.Key, StringComparer.Ordinal) && field.HasValue).ToArray(), null);
        }

        return new(NormalizedDeviceAvailability.Unknown, [], new NormalizedDeviceProfileIssue(
            "DeviceCapabilityUnknown",
            $"$.profiles[{ProfileId}].missingCapabilities",
            $"Profile '{ProfileId}' does not provide requested capability '{capabilityId}'. Unrelated capabilities remain usable.",
            capabilityId));
    }

    /// <summary>Resolves one semantic field and optional mode.</summary>
    public NormalizedDeviceResolution ResolveField(string fieldName, string? mode = null)
    {
        var matching = Fields.Where(field => string.Equals(field.Name, fieldName, StringComparison.Ordinal))
            .Where(field => string.IsNullOrWhiteSpace(mode) || string.Equals(field.Mode, mode, StringComparison.Ordinal)).ToArray();
        var available = matching.Where(field => field.HasValue).ToArray();
        if (available.Length > 0) return new(NormalizedDeviceAvailability.Available, available, null);

        var code = matching.Length > 0 ? "DeviceFieldUnknown" : "DeviceFieldMissing";
        return new(NormalizedDeviceAvailability.Unknown, [], new NormalizedDeviceProfileIssue(
            code,
            $"$.profiles[{ProfileId}].fields",
            $"Profile '{ProfileId}' does not provide a usable value for field '{fieldName}'{(string.IsNullOrWhiteSpace(mode) ? "" : $" in mode '{mode}'")}. Unrelated fields remain usable.",
            fieldName));
    }

    internal static string ComputeSemanticHash(NormalizedDeviceProfile profile) =>
        ComponentTemplateJson.StableHash(new
        {
            profile.SchemaVersion,
            profile.ProfileId,
            profile.DeviceFamily,
            profile.Technology,
            profile.OperatingCorner,
            ModesAndPrecisions = profile.ModesAndPrecisions.OrderBy(value => value, StringComparer.Ordinal),
            Capabilities = profile.Capabilities.OrderBy(value => value, StringComparer.Ordinal),
            MissingCapabilities = profile.MissingCapabilities.OrderBy(value => value, StringComparer.Ordinal),
            MissingFields = profile.MissingFields.OrderBy(value => value, StringComparer.Ordinal),
            SourceIds = profile.SourceIds.OrderBy(value => value, StringComparer.Ordinal),
            SourceRecordIds = profile.SourceRecordIds.OrderBy(value => value, StringComparer.Ordinal),
            SourceRecordHashes = profile.SourceRecordHashes.OrderBy(value => value, StringComparer.Ordinal),
            EvidenceTypes = profile.EvidenceTypes.OrderBy(value => value, StringComparer.Ordinal),
            EvidenceGrades = profile.EvidenceGrades.OrderBy(value => value, StringComparer.Ordinal),
            profile.CalibrationState,
            Fields = profile.Fields.OrderBy(field => field.Key, StringComparer.Ordinal).Select(field => new
            {
                field.Key,
                field.Name,
                field.Mode,
                Value = CanonicalValue(field.Value),
                field.CanonicalUnits,
                field.ValidRange,
                field.InterpolationPolicy,
                field.ExtrapolationPolicy,
                field.Provenance.Status,
                field.Provenance.EvidenceType,
                field.Provenance.EvidenceGrade,
                field.Provenance.CalibrationState,
                SourceIds = field.Provenance.SourceIds.OrderBy(value => value, StringComparer.Ordinal),
                SourceRecordIds = field.Provenance.SourceRecordIds.OrderBy(value => value, StringComparer.Ordinal),
                SourceRecordHashes = field.Provenance.SourceRecordHashes.OrderBy(value => value, StringComparer.Ordinal),
                field.Provenance.MethodId,
                field.Provenance.ModelVersion,
                field.Provenance.Formula,
                Inputs = field.Provenance.Inputs.OrderBy(input => input.Name, StringComparer.Ordinal).Select(input => new { input.Name, Value = CanonicalValue(input.Value), input.Units }),
                field.Provenance.ApplicableRange,
                field.Provenance.Uncertainty,
                field.Provenance.EvidenceLocator,
                field.Provenance.Notes
            }),
            Footprint = profile.Subcontracts.FootprintFieldKeys,
            Timing = profile.Subcontracts.TimingFieldKeys,
            Energy = profile.Subcontracts.EnergyFieldKeys,
            Storage = profile.Subcontracts.StorageFieldKeys,
            Precision = profile.Subcontracts.PrecisionFieldKeys,
            NonIdeality = profile.Subcontracts.NonIdealityFieldKeys
        });

    private static IReadOnlyList<string> CapabilityFieldKeys(NormalizedDeviceSubcontracts subcontracts, string capabilityId) => capabilityId switch
    {
        NormalizedDeviceCapabilityIds.Footprint => subcontracts.FootprintFieldKeys,
        NormalizedDeviceCapabilityIds.Timing => subcontracts.TimingFieldKeys,
        NormalizedDeviceCapabilityIds.Energy => subcontracts.EnergyFieldKeys,
        NormalizedDeviceCapabilityIds.Storage => subcontracts.StorageFieldKeys,
        NormalizedDeviceCapabilityIds.Precision => subcontracts.PrecisionFieldKeys,
        NormalizedDeviceCapabilityIds.NonIdeality => subcontracts.NonIdealityFieldKeys,
        _ => []
    };

    private static string CanonicalValue(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Number when value.TryGetInt64(out var integer) => integer.ToString(CultureInfo.InvariantCulture),
        JsonValueKind.Number => value.GetDouble().ToString("R", CultureInfo.InvariantCulture),
        JsonValueKind.String => JsonSerializer.Serialize(value.GetString()),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        JsonValueKind.Null or JsonValueKind.Undefined => "null",
        _ => value.GetRawText()
    };

    private static IReadOnlyList<string> Freeze(IEnumerable<string> values) =>
        new ReadOnlyCollection<string>(values.Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal).ToArray());
}

/// <summary>Contains a versioned collection of normalized device profiles.</summary>
public sealed class NormalizedDeviceProfilePackage
{
    /// <summary>The current package schema.</summary>
    public const string CurrentSchemaVersion = "normalized-device-profile-package-1.0";

    internal NormalizedDeviceProfilePackage(string packageId, string packageVersion, string sourceCatalogId, string sourceCatalogHash, IReadOnlyList<NormalizedDeviceProfile> profiles)
    {
        SchemaVersion = CurrentSchemaVersion;
        PackageId = packageId;
        PackageVersion = packageVersion;
        SourceCatalogId = sourceCatalogId;
        SourceCatalogHash = sourceCatalogHash;
        Profiles = new ReadOnlyCollection<NormalizedDeviceProfile>(profiles.OrderBy(profile => profile.ProfileId, StringComparer.Ordinal).ToArray());
        PackageHash = ComponentTemplateJson.StableHash(new
        {
            SchemaVersion,
            PackageId,
            PackageVersion,
            SourceCatalogId,
            SourceCatalogHash,
            Profiles = Profiles.Select(profile => new { profile.ProfileId, profile.ProfileHash })
        });
    }

    /// <summary>Gets the package schema version.</summary>
    public string SchemaVersion { get; }
    /// <summary>Gets the package identifier.</summary>
    public string PackageId { get; }
    /// <summary>Gets the package version.</summary>
    public string PackageVersion { get; }
    /// <summary>Gets the source catalog identifier.</summary>
    public string SourceCatalogId { get; }
    /// <summary>Gets the source catalog semantic hash.</summary>
    public string SourceCatalogHash { get; }
    /// <summary>Gets normalized profiles in stable identifier order.</summary>
    public IReadOnlyList<NormalizedDeviceProfile> Profiles { get; }
    /// <summary>Gets the deterministic package hash.</summary>
    public string PackageHash { get; }
}