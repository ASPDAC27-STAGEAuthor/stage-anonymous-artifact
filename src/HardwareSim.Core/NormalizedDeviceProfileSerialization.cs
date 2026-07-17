using System.Text.Json;

namespace HardwareSim.Core;

/// <summary>Serializes and validates versioned normalized device profile packages.</summary>
public static class NormalizedDeviceProfilePackageJson
{
    /// <summary>Serializes a package with stable ordering and explicit hashes.</summary>
    public static string Serialize(NormalizedDeviceProfilePackage package)
    {
        if (package is null) throw new ArgumentNullException(nameof(package));
        return JsonSerializer.Serialize(ToDto(package), HardwareGraphJson.Options);
    }

    /// <summary>Deserializes a package and rejects schema or semantic hash drift.</summary>
    public static NormalizedDeviceProfilePackage Deserialize(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) throw new ArgumentException("Normalized device profile package JSON is required.", nameof(json));
        var dto = JsonSerializer.Deserialize<PackageDto>(json, HardwareGraphJson.Options)
            ?? throw new InvalidDataException("Normalized device profile package JSON did not contain an object.");
        if (!string.Equals(dto.SchemaVersion, NormalizedDeviceProfilePackage.CurrentSchemaVersion, StringComparison.Ordinal))
            throw new InvalidDataException($"Unsupported normalized device package schema '{dto.SchemaVersion}'.");

        var profiles = (dto.Profiles ?? []).Select(FromDto).ToArray();
        var package = new NormalizedDeviceProfilePackage(dto.PackageId, dto.PackageVersion, dto.SourceCatalogId, dto.SourceCatalogHash, profiles);
        if (!string.Equals(package.PackageHash, dto.PackageHash, StringComparison.Ordinal))
            throw new InvalidDataException("Normalized device profile package hash does not match semantic content.");
        return package;
    }

    private static NormalizedDeviceProfile FromDto(ProfileDto dto)
    {
        if (!string.Equals(dto.SchemaVersion, NormalizedDeviceProfile.CurrentSchemaVersion, StringComparison.Ordinal))
            throw new InvalidDataException($"Unsupported normalized device profile schema '{dto.SchemaVersion}'.");

        var fields = (dto.Fields ?? []).Select(field => new NormalizedDeviceField(
            field.Key,
            field.Name,
            field.Mode,
            field.Value,
            string.IsNullOrWhiteSpace(field.CanonicalUnits) ? "" : PhysicalUnitConverter.Canonicalize(field.CanonicalUnits),
            field.ValidRange,
            field.InterpolationPolicy,
            field.ExtrapolationPolicy,
            new NormalizedDeviceFieldProvenance(
                field.Provenance.Status,
                field.Provenance.EvidenceType,
                field.Provenance.EvidenceGrade,
                field.Provenance.CalibrationState,
                field.Provenance.SourceIds ?? [],
                field.Provenance.SourceRecordIds ?? [],
                field.Provenance.SourceRecordHashes ?? [],
                field.Provenance.MethodId,
                field.Provenance.ModelVersion,
                field.Provenance.Formula,
                (field.Provenance.Inputs ?? []).Select(input => new NormalizedDeviceProvenanceInput(input.Name, input.Value, input.Units)).ToArray(),
                field.Provenance.ApplicableRange,
                field.Provenance.Uncertainty,
                field.Provenance.EvidenceLocator,
                field.Provenance.Notes))).ToArray();
        var subcontracts = dto.Subcontracts ?? new SubcontractsDto();
        var profile = new NormalizedDeviceProfile(
            dto.ProfileId,
            dto.DisplayName,
            dto.DeviceFamily,
            dto.Technology,
            dto.OperatingCorner,
            dto.ModesAndPrecisions ?? [],
            dto.Capabilities ?? [],
            dto.MissingCapabilities ?? [],
            dto.MissingFields ?? [],
            dto.SourceIds ?? [],
            dto.SourceRecordIds ?? [],
            dto.SourceRecordHashes ?? [],
            dto.EvidenceTypes ?? [],
            dto.EvidenceGrades ?? [],
            dto.CalibrationState,
            fields,
            new NormalizedDeviceSubcontracts(
                subcontracts.FootprintFieldKeys ?? [],
                subcontracts.TimingFieldKeys ?? [],
                subcontracts.EnergyFieldKeys ?? [],
                subcontracts.StorageFieldKeys ?? [],
                subcontracts.PrecisionFieldKeys ?? [],
                subcontracts.NonIdealityFieldKeys ?? []));
        if (!string.Equals(profile.ProfileHash, dto.ProfileHash, StringComparison.Ordinal))
            throw new InvalidDataException($"Normalized device profile '{dto.ProfileId}' hash does not match semantic content.");
        return profile;
    }
    private static PackageDto ToDto(NormalizedDeviceProfilePackage package) => new()
    {
        SchemaVersion = package.SchemaVersion,
        PackageId = package.PackageId,
        PackageVersion = package.PackageVersion,
        SourceCatalogId = package.SourceCatalogId,
        SourceCatalogHash = package.SourceCatalogHash,
        PackageHash = package.PackageHash,
        Profiles = package.Profiles.Select(profile => new ProfileDto
        {
            SchemaVersion = profile.SchemaVersion,
            ProfileId = profile.ProfileId,
            DisplayName = profile.DisplayName,
            DeviceFamily = profile.DeviceFamily,
            Technology = profile.Technology,
            OperatingCorner = profile.OperatingCorner,
            ModesAndPrecisions = profile.ModesAndPrecisions.ToArray(),
            Capabilities = profile.Capabilities.ToArray(),
            MissingCapabilities = profile.MissingCapabilities.ToArray(),
            MissingFields = profile.MissingFields.ToArray(),
            SourceIds = profile.SourceIds.ToArray(),
            SourceRecordIds = profile.SourceRecordIds.ToArray(),
            SourceRecordHashes = profile.SourceRecordHashes.ToArray(),
            EvidenceTypes = profile.EvidenceTypes.ToArray(),
            EvidenceGrades = profile.EvidenceGrades.ToArray(),
            CalibrationState = profile.CalibrationState,
            ProfileHash = profile.ProfileHash,
            Fields = profile.Fields.Select(field => new FieldDto
            {
                Key = field.Key,
                Name = field.Name,
                Mode = field.Mode,
                Value = field.Value.Clone(),
                CanonicalUnits = field.CanonicalUnits,
                ValidRange = field.ValidRange,
                InterpolationPolicy = field.InterpolationPolicy,
                ExtrapolationPolicy = field.ExtrapolationPolicy,
                Provenance = new ProvenanceDto
                {
                    Status = field.Provenance.Status,
                    EvidenceType = field.Provenance.EvidenceType,
                    EvidenceGrade = field.Provenance.EvidenceGrade,
                    CalibrationState = field.Provenance.CalibrationState,
                    SourceIds = field.Provenance.SourceIds.ToArray(),
                    SourceRecordIds = field.Provenance.SourceRecordIds.ToArray(),
                    SourceRecordHashes = field.Provenance.SourceRecordHashes.ToArray(),
                    MethodId = field.Provenance.MethodId,
                    ModelVersion = field.Provenance.ModelVersion,
                    Formula = field.Provenance.Formula,
                    Inputs = field.Provenance.Inputs.Select(input => new InputDto { Name = input.Name, Value = input.Value.Clone(), Units = input.Units }).ToArray(),
                    ApplicableRange = field.Provenance.ApplicableRange,
                    Uncertainty = field.Provenance.Uncertainty,
                    EvidenceLocator = field.Provenance.EvidenceLocator,
                    Notes = field.Provenance.Notes
                }
            }).ToArray(),
            Subcontracts = new SubcontractsDto
            {
                FootprintFieldKeys = profile.Subcontracts.FootprintFieldKeys.ToArray(),
                TimingFieldKeys = profile.Subcontracts.TimingFieldKeys.ToArray(),
                EnergyFieldKeys = profile.Subcontracts.EnergyFieldKeys.ToArray(),
                StorageFieldKeys = profile.Subcontracts.StorageFieldKeys.ToArray(),
                PrecisionFieldKeys = profile.Subcontracts.PrecisionFieldKeys.ToArray(),
                NonIdealityFieldKeys = profile.Subcontracts.NonIdealityFieldKeys.ToArray()
            }
        }).ToArray()
    };

    private sealed class PackageDto
    {
        public string SchemaVersion { get; set; } = "";
        public string PackageId { get; set; } = "";
        public string PackageVersion { get; set; } = "";
        public string SourceCatalogId { get; set; } = "";
        public string SourceCatalogHash { get; set; } = "";
        public string PackageHash { get; set; } = "";
        public ProfileDto[]? Profiles { get; set; }
    }

    private sealed class ProfileDto
    {
        public string SchemaVersion { get; set; } = "";
        public string ProfileId { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string DeviceFamily { get; set; } = "";
        public string Technology { get; set; } = "";
        public string OperatingCorner { get; set; } = "";
        public string[]? ModesAndPrecisions { get; set; }
        public string[]? Capabilities { get; set; }
        public string[]? MissingCapabilities { get; set; }
        public string[]? MissingFields { get; set; }
        public string[]? SourceIds { get; set; }
        public string[]? SourceRecordIds { get; set; }
        public string[]? SourceRecordHashes { get; set; }
        public string[]? EvidenceTypes { get; set; }
        public string[]? EvidenceGrades { get; set; }
        public NormalizedDeviceCalibrationState CalibrationState { get; set; }
        public string ProfileHash { get; set; } = "";
        public FieldDto[]? Fields { get; set; }
        public SubcontractsDto? Subcontracts { get; set; }
    }
    private sealed class FieldDto
    {
        public string Key { get; set; } = "";
        public string Name { get; set; } = "";
        public string Mode { get; set; } = "";
        public JsonElement Value { get; set; }
        public string CanonicalUnits { get; set; } = "";
        public string ValidRange { get; set; } = "";
        public string InterpolationPolicy { get; set; } = "";
        public string ExtrapolationPolicy { get; set; } = "";
        public ProvenanceDto Provenance { get; set; } = new();
    }

    private sealed class ProvenanceDto
    {
        public NormalizedDeviceEvidenceStatus Status { get; set; }
        public string EvidenceType { get; set; } = "";
        public string EvidenceGrade { get; set; } = "";
        public NormalizedDeviceCalibrationState CalibrationState { get; set; }
        public string[]? SourceIds { get; set; }
        public string[]? SourceRecordIds { get; set; }
        public string[]? SourceRecordHashes { get; set; }
        public string MethodId { get; set; } = "";
        public string ModelVersion { get; set; } = "";
        public string Formula { get; set; } = "";
        public InputDto[]? Inputs { get; set; }
        public string ApplicableRange { get; set; } = "";
        public string Uncertainty { get; set; } = "";
        public string EvidenceLocator { get; set; } = "";
        public string Notes { get; set; } = "";
    }

    private sealed class InputDto
    {
        public string Name { get; set; } = "";
        public JsonElement Value { get; set; }
        public string Units { get; set; } = "";
    }

    private sealed class SubcontractsDto
    {
        public string[]? FootprintFieldKeys { get; set; }
        public string[]? TimingFieldKeys { get; set; }
        public string[]? EnergyFieldKeys { get; set; }
        public string[]? StorageFieldKeys { get; set; }
        public string[]? PrecisionFieldKeys { get; set; }
        public string[]? NonIdealityFieldKeys { get; set; }
    }
}