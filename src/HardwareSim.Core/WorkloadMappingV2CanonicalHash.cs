using System.Text.Json.Nodes;

namespace HardwareSim.Core;

/// <summary>Contains a WorkloadMapping 2.0 canonical projection and SHA-256 digest.</summary>
/// <param name="Algorithm">Canonical projection and hash algorithm identifier.</param>
/// <param name="Hash">Lowercase SHA-256 digest.</param>
/// <param name="CanonicalJson">Exact canonical JSON hashed to produce the digest.</param>
public sealed record WorkloadMappingV2CanonicalHash(string Algorithm, string Hash, string CanonicalJson);

/// <summary>Computes deterministic semantic hashes for immutable WorkloadMapping 2.0 documents.</summary>
public static class WorkloadMappingV2CanonicalHasher
{
    /// <summary>Computes a canonical semantic projection and lowercase SHA-256 digest.</summary>
    /// <param name="mapping">Immutable mapping to hash.</param>
    /// <returns>The exact algorithm, digest, and canonical JSON.</returns>
    public static WorkloadMappingV2CanonicalHash Compute(WorkloadMappingV2 mapping)
    {
        if (mapping is null)
        {
            throw new ArgumentNullException(nameof(mapping));
        }

        var unhashed = mapping.WithCanonicalHash("");
        var node = JsonNode.Parse(WorkloadMappingV2Json.SerializeRaw(unhashed)) as JsonObject
            ?? throw new InvalidOperationException("WorkloadMapping 2.0 canonical projection must be a JSON object.");
        node.Remove("canonicalHash");
        RemoveHumanDiagnosticText(node);
        var canonicalJson = ComponentExecutionJson.CanonicalizeJson(node.ToJsonString());
        return new WorkloadMappingV2CanonicalHash(
            WorkloadMappingV2.CurrentCanonicalHashAlgorithm,
            ComponentExecutionJson.ComputeSha256(canonicalJson),
            canonicalJson);
    }

    private static void RemoveHumanDiagnosticText(JsonObject root)
    {
        if (root["candidate"] is not JsonObject candidate || candidate["issues"] is not JsonArray issues)
        {
            return;
        }

        foreach (var issue in issues.OfType<JsonObject>())
        {
            issue.Remove("message");
            issue.Remove("suggestion");
        }
    }
}
