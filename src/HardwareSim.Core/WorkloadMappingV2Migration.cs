using System.Text.Json;

namespace HardwareSim.Core;

/// <summary>Records one applied WorkloadMapping schema migration edge.</summary>
/// <param name="FromVersion">Source schema version.</param>
/// <param name="ToVersion">Target schema version.</param>
public sealed record WorkloadMappingV2MigrationStep(string FromVersion, string ToVersion);

/// <summary>Provides explicit WorkloadMapping import and migration into immutable schema 2.0.</summary>
public static class WorkloadMappingV2Migrator
{
    /// <summary>Imports a current mapping or migrates a recognized legacy mapping.</summary>
    /// <param name="json">Mapping JSON to import.</param>
    /// <returns>The imported mapping, applied migration path, and structured issues.</returns>
    public static WorkloadMappingV2ImportResult ImportToCurrent(string json) => WorkloadMappingV2Json.ImportToCurrent(json);

    internal static WorkloadMappingV2ImportResult MigrateLegacyJson(string json)
    {
        WorkloadMapping legacy;
        try
        {
            legacy = WorkloadMappingJson.Deserialize(json);
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException or ArgumentException)
        {
            return new WorkloadMappingV2ImportResult
            {
                SourceVersion = "1.0",
                Issues =
                [
                    new WorkloadMappingV2Issue(
                        "InvalidLegacyMappingDocument",
                        ValidationSeverity.Error,
                        exception is JsonException jsonException ? jsonException.Path ?? "$" : "$",
                        exception.Message)
                ]
            };
        }

        if (!string.Equals(legacy.SchemaVersion, "1.0", StringComparison.Ordinal))
        {
            return new WorkloadMappingV2ImportResult
            {
                SourceVersion = legacy.SchemaVersion,
                Issues =
                [
                    new WorkloadMappingV2Issue(
                        "MigrationSourceVersionMismatch",
                        ValidationSeverity.Error,
                        "$.schema_version",
                        $"Expected legacy WorkloadMapping schema 1.0, but received '{legacy.SchemaVersion}'.")
                ]
            };
        }

        var normalizedLegacyJson = legacy.ToJson();
        var legacyHash = ComponentExecutionJson.ComputeSha256(ComponentExecutionJson.CanonicalizeJson(normalizedLegacyJson));
        var legacySnapshot = new LegacyWorkloadMappingSnapshot(
            legacy.SchemaVersion,
            legacy.Entries.Select(entry => new LegacyMappingEntrySnapshot(
                entry.WorkloadOpId,
                entry.TargetComponentId,
                entry.TargetPort,
                entry.ScheduleHints)).ToList(),
            legacy.Placements.Select(placement => new LegacyTensorPlacementSnapshot(
                placement.TensorId,
                placement.TileId,
                placement.StorageComponentId,
                placement.StorageLevel,
                placement.AddressHint)).ToList(),
            legacy.RouteHints.Select(route => new LegacyRouteHintSnapshot(
                route.LinkId,
                route.PreferredPath,
                route.Priority)).ToList());

        var warnings = new[]
        {
            new WorkloadMappingV2Issue(
                "LegacyMappingRequiresCapabilityFreeze",
                ValidationSeverity.Warning,
                "$.capabilitySnapshot",
                "WorkloadMapping 1.0 did not contain a compiled capability snapshot; migration preserved the legacy document without inventing one."),
            new WorkloadMappingV2Issue(
                "LegacyOperandSemanticsUnresolved",
                ValidationSeverity.Warning,
                "$.operationTileAssignments",
                "WorkloadMapping 1.0 did not encode M/K/N ranges, operand roles, or partition semantics."),
            new WorkloadMappingV2Issue(
                "LegacyRouteHintNotExecutable",
                ValidationSeverity.Warning,
                "$.communicationFlows",
                "Legacy route hints were preserved verbatim and were not promoted to executable CommunicationFlow bindings.")
        };

        var mapping = new WorkloadMappingV2(
            WorkloadMappingV2.CurrentSchemaVersion,
            $"legacy-{legacyHash[..16]}",
            WorkloadMappingV2Modes.LegacyCompatibility,
            WorkloadMappingV2.EmptyCapabilitySnapshot(),
            [],
            [],
            [],
            [],
            new MappingCandidate(
                "legacy-compatibility",
                WorkloadMappingV2Modes.LegacyCompatibility,
                legacyHash,
                warnings,
                [],
                "",
                "",
                "",
                legacyHash,
                []),
            new WorkloadMappingV2Provenance(legacyHash, legacyHash, "mapping-v2-migrator-1.0", 0),
            legacySnapshot,
            WorkloadMappingV2.CurrentCanonicalHashAlgorithm,
            "");
        var canonicalHash = WorkloadMappingV2CanonicalHasher.Compute(mapping).Hash;
        var migrated = mapping.WithCanonicalHash(canonicalHash);

        return new WorkloadMappingV2ImportResult
        {
            Mapping = migrated,
            SourceVersion = "1.0",
            MigrationPath = [new WorkloadMappingV2MigrationStep("1.0", WorkloadMappingV2.CurrentSchemaVersion)],
            Issues = MappingV2Freeze.List(warnings)
        };
    }
}
