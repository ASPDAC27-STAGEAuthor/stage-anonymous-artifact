using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace HardwareSim.Core;

/// <summary>Represents a stable hash over a canonical simulation trace document.</summary>
public sealed class CanonicalTraceHash
{
    /// <summary>Gets the hash algorithm used to produce the digest.</summary>
    public string Algorithm { get; init; } = CanonicalTraceHasher.Algorithm;
    /// <summary>Gets the trace schema version included in the canonical hash document.</summary>
    public string TraceSchemaVersion { get; init; } = CanonicalTraceHasher.TraceSchemaVersion;
    /// <summary>Gets the deterministic seed included in the canonical hash document.</summary>
    public int Seed { get; init; }
    /// <summary>Gets the stable configuration values included in the canonical hash document.</summary>
    public IReadOnlyDictionary<string, string> Config { get; init; } = new Dictionary<string, string>();
    /// <summary>Gets the hexadecimal digest of the canonical hash document.</summary>
    public string Hash { get; init; } = "";
    /// <summary>Gets the canonical JSON document used as the hash input.</summary>
    public string CanonicalJson { get; init; } = "";
}

/// <summary>Builds canonical trace serialization and SHA-256 hashes for regression tests.</summary>
public static class CanonicalTraceHasher
{
    /// <summary>Defines the trace hash algorithm identifier stored with every digest.</summary>
    public const string Algorithm = "SHA-256";
    /// <summary>Defines the canonical trace schema version used by Phase 1C golden tests.</summary>
    public const string TraceSchemaVersion = "1.0";

    /// <summary>Computes a stable trace hash from trace events, config, and deterministic seed.</summary>
    public static CanonicalTraceHash Compute(
        SimulationTrace trace,
        IReadOnlyDictionary<string, string>? config = null,
        int seed = 0)
    {
        if (trace is null)
        {
            throw new ArgumentNullException(nameof(trace));
        }

        var stableConfig = StableConfig(config ?? new Dictionary<string, string>());
        var canonical = BuildCanonicalDocument(trace, stableConfig, seed).ToJsonString(new JsonSerializerOptions
        {
            WriteIndented = false
        });

        return new CanonicalTraceHash
        {
            Algorithm = Algorithm,
            TraceSchemaVersion = TraceSchemaVersion,
            Seed = seed,
            Config = stableConfig,
            Hash = ComputeSha256(canonical),
            CanonicalJson = canonical
        };
    }

    private static JsonObject BuildCanonicalDocument(
        SimulationTrace trace,
        IReadOnlyDictionary<string, string> config,
        int seed)
    {
        var configNode = new JsonObject();
        foreach (var pair in config.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            configNode[pair.Key] = pair.Value;
        }

        var cycles = new JsonArray();
        foreach (var cycle in trace.Cycles.OrderBy(cycle => cycle.Cycle))
        {
            var events = new JsonArray();
            foreach (var item in cycle.Events)
            {
                var evt = new JsonObject
                {
                    ["type"] = item.Type.ToString(),
                    ["bits"] = item.Bits
                };
                AddIfPresent(evt, "packet_id", item.PacketId);
                AddIfPresent(evt, "component_id", item.ComponentId);
                AddIfPresent(evt, "link_id", item.LinkId);
                AddIfPresent(evt, "source", item.Source);
                AddIfPresent(evt, "destination", item.Destination);
                AddIfPresent(evt, "detail", item.Detail);
                events.Add(evt);
            }

            cycles.Add(new JsonObject
            {
                ["cycle"] = cycle.Cycle,
                ["events"] = events
            });
        }

        return new JsonObject
        {
            ["algorithm"] = Algorithm,
            ["trace_schema_version"] = TraceSchemaVersion,
            ["seed"] = seed,
            ["config"] = configNode,
            ["trace"] = new JsonObject
            {
                ["cycles"] = cycles
            }
        };
    }

    private static IReadOnlyDictionary<string, string> StableConfig(IReadOnlyDictionary<string, string> config)
    {
        var stable = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in config)
        {
            if (IsUnstableConfigEntry(pair.Key, pair.Value))
            {
                continue;
            }

            stable[pair.Key] = pair.Value ?? "";
        }

        return stable;
    }

    private static bool IsUnstableConfigEntry(string key, string? value)
    {
        var normalizedKey = key.Replace("_", "", StringComparison.Ordinal)
            .Replace("-", "", StringComparison.Ordinal)
            .ToLowerInvariant();
        if (normalizedKey.Contains("timestamp", StringComparison.Ordinal) ||
            normalizedKey.Contains("createdat", StringComparison.Ordinal) ||
            normalizedKey.Contains("updatedat", StringComparison.Ordinal) ||
            normalizedKey.Contains("generatedat", StringComparison.Ordinal) ||
            normalizedKey.Contains("absolutepath", StringComparison.Ordinal) ||
            normalizedKey.Contains("outputpath", StringComparison.Ordinal) ||
            normalizedKey.Contains("workingdirectory", StringComparison.Ordinal))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(value) && (Path.IsPathRooted(value) || DateTimeOffset.TryParse(value, out _)))
        {
            return true;
        }

        return false;
    }

    private static void AddIfPresent(JsonObject target, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            target[key] = value;
        }
    }

    private static string ComputeSha256(string value)
    {
        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(value));
        var builder = new StringBuilder(hash.Length * 2);
        foreach (var item in hash)
        {
            builder.Append(item.ToString("x2", System.Globalization.CultureInfo.InvariantCulture));
        }

        return builder.ToString();
    }
}
