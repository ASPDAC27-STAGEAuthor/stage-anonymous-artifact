using System.Text.Json;
using System.Text.Json.Nodes;

namespace HardwareSim.Core;

/// <summary>Defines one executable HardwareGraph schema migration step.</summary>
public interface IHardwareGraphMigration
{
    /// <summary>Gets the source schema version accepted by this migration.</summary>
    string FromVersion { get; }
    /// <summary>Gets the target schema version produced by this migration.</summary>
    string ToVersion { get; }
    /// <summary>Transforms the supplied JSON object into the target schema version.</summary>
    JsonObject Migrate(JsonObject source);
}

/// <summary>Represents one applied schema migration edge.</summary>
/// <param name="FromVersion">Provides the source version value carried by this contract.</param>
/// <param name="ToVersion">Provides the target version value carried by this contract.</param>
public sealed record HardwareGraphMigrationStep(string FromVersion, string ToVersion);

/// <summary>Contains the migrated JSON document or structured migration issues.</summary>
public sealed class HardwareGraphMigrationChainResult
{
    /// <summary>Gets whether migration completed without issues.</summary>
    public bool IsSuccess => Document is not null && Issues.Count == 0;
    /// <summary>Gets the migrated JSON document when migration succeeds.</summary>
    public JsonObject? Document { get; init; }
    /// <summary>Gets structured migration issues when migration fails.</summary>
    public IReadOnlyList<HardwareGraphSerializationIssue> Issues { get; init; } = [];
    /// <summary>Gets the applied migration edges in execution order.</summary>
    public IReadOnlyList<HardwareGraphMigrationStep> MigrationPath { get; init; } = [];
}

/// <summary>Contains the current HardwareGraph produced by import or structured import issues.</summary>
public sealed class HardwareGraphImportResult
{
    /// <summary>Gets whether import produced a current-schema HardwareGraph.</summary>
    public bool IsSuccess => Graph is not null && Issues.Count == 0;
    /// <summary>Gets the imported HardwareGraph when import succeeds.</summary>
    public HardwareGraph? Graph { get; init; }
    /// <summary>Gets structured import or migration issues when import fails.</summary>
    public IReadOnlyList<HardwareGraphSerializationIssue> Issues { get; init; } = [];
    /// <summary>Gets the applied migration edges in execution order.</summary>
    public IReadOnlyList<HardwareGraphMigrationStep> MigrationPath { get; init; } = [];
    /// <summary>Gets the source schema version detected from the imported document.</summary>
    public string SourceVersion { get; init; } = "";
}

/// <summary>Provides explicit HardwareGraph schema import and migration operations.</summary>
public static class HardwareGraphSchemaMigrator
{
    private static readonly IReadOnlyList<IHardwareGraphMigration> BuiltInMigrations =
    [
        new LegacyHardwareGraph010To100Migration()
    ];

    /// <summary>Imports a JSON document into the current HardwareGraph schema or returns structured issues.</summary>
    public static HardwareGraphImportResult ImportToCurrent(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return FailedImport("InvalidJson", "$", "HardwareGraph JSON is empty.");
        }

        JsonObject root;
        try
        {
            var node = JsonNode.Parse(json);
            if (node is not JsonObject parsed)
            {
                return FailedImport("InvalidJson", "$", "HardwareGraph JSON did not contain an object.");
            }

            root = parsed;
        }
        catch (JsonException exception)
        {
            return FailedImport("InvalidJson", exception.Path ?? "$", exception.Message);
        }

        var version = ReadVersion(root);
        if (string.IsNullOrWhiteSpace(version))
        {
            return LooksLikeUnityEditorSnapshot(root)
                ? FailedImport(
                    "UnsupportedUnitySnapshotImport",
                    "$",
                    "Unity editor snapshot JSON is not a lossless HardwareGraph project file. Export or save the HardwareGraph JSON instead.")
                : FailedImport(
                    "MissingSchemaVersion",
                    "$.schema_version",
                    "HardwareGraph schema_version is required unless an explicit legacy importer recognizes the document.");
        }

        if (string.Equals(version, HardwareGraph.CurrentSchemaVersion, StringComparison.Ordinal) &&
            root.ContainsKey("schema_version"))
        {
            var current = HardwareGraphJson.DeserializeCurrentSchemaJson(json);
            return current.IsSuccess && current.Graph is not null
                ? new HardwareGraphImportResult
                {
                    Graph = current.Graph,
                    SourceVersion = version
                }
                : new HardwareGraphImportResult
                {
                    Issues = current.Issues,
                    SourceVersion = version
                };
        }

        var majorIssue = ValidateKnownMajor(version);
        if (majorIssue is not null && !string.Equals(version, "0.1.0", StringComparison.Ordinal))
        {
            return new HardwareGraphImportResult
            {
                Issues = [majorIssue],
                SourceVersion = version
            };
        }

        var migrated = Migrate(root, HardwareGraph.CurrentSchemaVersion, BuiltInMigrations);
        if (!migrated.IsSuccess || migrated.Document is null)
        {
            return new HardwareGraphImportResult
            {
                Issues = migrated.Issues,
                MigrationPath = migrated.MigrationPath,
                SourceVersion = version
            };
        }

        var migratedJson = migrated.Document.ToJsonString(HardwareGraphJson.Options);
        var result = HardwareGraphJson.DeserializeCurrentSchemaJson(migratedJson);
        return result.IsSuccess && result.Graph is not null
            ? new HardwareGraphImportResult
            {
                Graph = result.Graph,
                MigrationPath = migrated.MigrationPath,
                SourceVersion = version
            }
            : new HardwareGraphImportResult
            {
                Issues = result.Issues,
                MigrationPath = migrated.MigrationPath,
                SourceVersion = version
            };
    }

    /// <summary>Runs a migration chain from the document version to the requested target version.</summary>
    public static HardwareGraphMigrationChainResult Migrate(
        JsonObject source,
        string targetVersion,
        IEnumerable<IHardwareGraphMigration> migrations)
    {
        if (source is null)
        {
            return FailedChain("InvalidJson", "$", "Migration source document is required.");
        }

        if (string.IsNullOrWhiteSpace(targetVersion))
        {
            return FailedChain("InvalidSchemaVersion", "$.schema_version", "Migration target version is required.");
        }

        var currentVersion = ReadVersion(source);
        if (string.IsNullOrWhiteSpace(currentVersion))
        {
            return FailedChain("MissingSchemaVersion", "$.schema_version", "Migration source document must declare a schema version.");
        }

        var document = CloneObject(source);
        var path = new List<HardwareGraphMigrationStep>();
        var migrationList = migrations.ToList();
        var visited = new HashSet<string>(StringComparer.Ordinal) { currentVersion };

        while (!string.Equals(currentVersion, targetVersion, StringComparison.Ordinal))
        {
            var migration = migrationList
                .Where(candidate => string.Equals(candidate.FromVersion, currentVersion, StringComparison.Ordinal))
                .OrderBy(candidate => candidate.ToVersion, StringComparer.Ordinal)
                .FirstOrDefault();
            if (migration is null)
            {
                return new HardwareGraphMigrationChainResult
                {
                    Issues =
                    [
                        new HardwareGraphSerializationIssue(
                            "MigrationPathNotFound",
                            "error",
                            "$.schema_version",
                            $"No HardwareGraph migration path from '{currentVersion}' to '{targetVersion}'.")
                    ],
                    MigrationPath = path
                };
            }

            document = migration.Migrate(document);
            currentVersion = ReadVersion(document);
            path.Add(new HardwareGraphMigrationStep(migration.FromVersion, migration.ToVersion));
            if (string.IsNullOrWhiteSpace(currentVersion))
            {
                return new HardwareGraphMigrationChainResult
                {
                    Issues =
                    [
                        new HardwareGraphSerializationIssue(
                            "MigrationDroppedSchemaVersion",
                            "error",
                            "$.schema_version",
                            $"Migration {migration.FromVersion}->{migration.ToVersion} did not produce a schema version.")
                    ],
                    MigrationPath = path
                };
            }

            if (!string.Equals(currentVersion, migration.ToVersion, StringComparison.Ordinal))
            {
                return new HardwareGraphMigrationChainResult
                {
                    Issues =
                    [
                        new HardwareGraphSerializationIssue(
                            "MigrationVersionMismatch",
                            "error",
                            "$.schema_version",
                            $"Migration {migration.FromVersion}->{migration.ToVersion} produced '{currentVersion}'.")
                    ],
                    MigrationPath = path
                };
            }

            if (!visited.Add(currentVersion))
            {
                return new HardwareGraphMigrationChainResult
                {
                    Issues =
                    [
                        new HardwareGraphSerializationIssue(
                            "MigrationCycleDetected",
                            "error",
                            "$.schema_version",
                            $"Migration chain revisited schema version '{currentVersion}'.")
                    ],
                    MigrationPath = path
                };
            }
        }

        return new HardwareGraphMigrationChainResult
        {
            Document = document,
            MigrationPath = path
        };
    }

    private static HardwareGraphSerializationIssue? ValidateKnownMajor(string version)
    {
        var majorText = version.Split('.', 2)[0];
        if (!int.TryParse(majorText, out var major))
        {
            return new HardwareGraphSerializationIssue(
                "InvalidSchemaVersion",
                "error",
                "$.schema_version",
                $"HardwareGraph schema version '{version}' is not valid.");
        }

        if (major is not 0 and not 1)
        {
            return new HardwareGraphSerializationIssue(
                "UnsupportedSchemaVersion",
                "error",
                "$.schema_version",
                $"HardwareGraph schema major version '{major}' is not supported; supported import majors are 0 and 1.");
        }

        return null;
    }

    private static string ReadVersion(JsonObject document)
    {
        return ReadString(document, "schema_version") ??
               ReadString(document, "schemaVersion") ??
               "";
    }

    private static string? ReadString(JsonObject document, string key)
    {
        return document.TryGetPropertyValue(key, out var node) && node is JsonValue value &&
               value.TryGetValue<string>(out var text)
            ? text
            : null;
    }

    private static bool LooksLikeUnityEditorSnapshot(JsonObject root)
    {
        if (root.ContainsKey("palette") || root.ContainsKey("inspector"))
        {
            return true;
        }

        if (root["components"] is not JsonArray components || components.Count == 0)
        {
            return false;
        }

        return components.OfType<JsonObject>().Any(component =>
            component.ContainsKey("x") &&
            component.ContainsKey("y") &&
            !component.ContainsKey("position"));
    }

    private static HardwareGraphImportResult FailedImport(string code, string location, string message) =>
        new()
        {
            Issues =
            [
                new HardwareGraphSerializationIssue(code, "error", location, message)
            ]
        };

    private static HardwareGraphMigrationChainResult FailedChain(string code, string location, string message) =>
        new()
        {
            Issues =
            [
                new HardwareGraphSerializationIssue(code, "error", location, message)
            ]
        };

    private static JsonObject CloneObject(JsonObject source) =>
        JsonNode.Parse(source.ToJsonString())!.AsObject();

    private sealed class LegacyHardwareGraph010To100Migration : IHardwareGraphMigration
    {
        public string FromVersion => "0.1.0";
        public string ToVersion => HardwareGraph.CurrentSchemaVersion;

        public JsonObject Migrate(JsonObject source)
        {
            var migrated = CloneObject(source);
            NormalizeLegacyNames(migrated);
            migrated["schema_version"] = HardwareGraph.CurrentSchemaVersion;
            migrated.Remove("schemaVersion");
            migrated["groups"] ??= new JsonArray();
            migrated["macros"] ??= new JsonArray();
            return migrated;
        }

        private static void NormalizeLegacyNames(JsonNode? node)
        {
            if (node is JsonObject obj)
            {
                Rename(obj, "modelRef", "model_ref");
                Rename(obj, "latencyModel", "latency_model");
                Rename(obj, "energyModel", "energy_model");
                Rename(obj, "areaModel", "area_model");
                Rename(obj, "schemaVersion", "schema_version");
                foreach (var child in obj.Select(pair => pair.Value).ToList())
                {
                    NormalizeLegacyNames(child);
                }
            }
            else if (node is JsonArray array)
            {
                foreach (var child in array.ToList())
                {
                    NormalizeLegacyNames(child);
                }
            }
        }

        private static void Rename(JsonObject obj, string oldName, string newName)
        {
            if (!obj.TryGetPropertyValue(oldName, out var value) || obj.ContainsKey(newName))
            {
                return;
            }

            obj.Remove(oldName);
            obj[newName] = value;
        }
    }
}
