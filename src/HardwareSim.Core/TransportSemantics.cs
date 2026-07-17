namespace HardwareSim.Core;

/// <summary>Defines the flow-control modes frozen by Transport Semantics 1.0.</summary>
public enum FlowControlMode
{
    /// <summary>Ready/valid flow control where an unavailable downstream endpoint keeps the same item valid upstream.</summary>
    BlockingReadyValid
}

/// <summary>Represents one transport semantics validation diagnostic.</summary>
/// <param name="Code">Stable machine-readable issue code.</param>
/// <param name="Severity">Diagnostic severity.</param>
/// <param name="Location">JSON-style location of the invalid field.</param>
/// <param name="Message">Human-readable diagnostic message.</param>
public sealed record TransportSemanticsValidationIssue(
    string Code,
    ValidationSeverity Severity,
    string Location,
    string Message);

/// <summary>Contains Transport Semantics 1.0 validation diagnostics.</summary>
public sealed class TransportSemanticsValidationResult
{
    /// <summary>Gets validation issues.</summary>
    public List<TransportSemanticsValidationIssue> Issues { get; } = [];
    /// <summary>Gets whether validation found no errors.</summary>
    public bool IsSuccess => Issues.All(issue => issue.Severity != ValidationSeverity.Error);
}

/// <summary>Serializable snapshot of the Transport Semantics contract used by compiled graphs.</summary>
public sealed class TransportSemanticsSnapshot
{
    /// <summary>Gets or sets the frozen transport semantics version.</summary>
    public string Version { get; init; } = TransportSemanticsContract.CurrentVersion;
    /// <summary>Gets or sets the logical packetization mode.</summary>
    public PacketizationMode PacketizationMode { get; init; } = PacketizationMode.CoarsePacketMode;
    /// <summary>Gets or sets the flow-control mode.</summary>
    public FlowControlMode FlowControlMode { get; init; } = FlowControlMode.BlockingReadyValid;
    /// <summary>Gets or sets the router crossbar issue model.</summary>
    public CrossbarIssueModel CrossbarIssueModel { get; init; } = CrossbarIssueModel.PerOutputIssue;
    /// <summary>Gets or sets the canonical serialization rule.</summary>
    public string SerializationRule { get; init; } = TransportSemanticsContract.SerializationRule;
    /// <summary>Gets or sets frozen machine-readable stall reason codes.</summary>
    public List<string> StallReasonCodes { get; init; } = TransportSemanticsContract.FrozenStallReasonCodes.ToList();
    /// <summary>Gets or sets frozen machine-readable transport and address error codes.</summary>
    public List<string> ErrorCodes { get; init; } = TransportSemanticsContract.FrozenErrorCodes.ToList();
}

/// <summary>Exposes the versioned Transport Semantics 1.0 contract to compilers, runtimes, tests, and schemas.</summary>
public static class TransportSemanticsContract
{
    /// <summary>Current frozen transport semantics version.</summary>
    public const string CurrentVersion = "1.0";
    /// <summary>Canonical documentation title for the frozen contract.</summary>
    public const string DocumentTitle = "Transport Semantics 1.0";
    /// <summary>Canonical coarse serialization formula.</summary>
    public const string SerializationRule = "cycles = ceil(bits / bandwidth_bits_per_cycle)";
    /// <summary>Stable code emitted for ready/valid downstream capacity stalls.</summary>
    public const string DownstreamFullStallCode = "downstream_full";
    /// <summary>Stable code emitted for same-output router arbitration conflicts.</summary>
    public const string RouterConflictStallCode = "router_conflict";

    /// <summary>Frozen stall reason codes; display text may be localized, but codes are stable.</summary>
    public static IReadOnlyList<string> FrozenStallReasonCodes { get; } =
    [
        DownstreamFullStallCode,
        RouterConflictStallCode,
        "memory_busy",
        "link_busy",
        "input_empty",
        "no_route"
    ];

    /// <summary>Frozen structured transport and address error codes.</summary>
    public static IReadOnlyList<string> FrozenErrorCodes { get; } =
    [
        "FlitSequenceError",
        "VirtualChannelError",
        "NoRoute",
        "BufferCapacityOverflow",
        "MemoryAddressError",
        "StorageCapacityError",
        "StorageOverlapError",
        "MemoryRequestError"
    ];

    /// <summary>Creates a fresh default Transport Semantics 1.0 snapshot.</summary>
    public static TransportSemanticsSnapshot CreateDefault() => new();

    /// <summary>Creates a deep copy of a Transport Semantics snapshot.</summary>
    public static TransportSemanticsSnapshot Clone(TransportSemanticsSnapshot snapshot) => new()
    {
        Version = snapshot.Version,
        PacketizationMode = snapshot.PacketizationMode,
        FlowControlMode = snapshot.FlowControlMode,
        CrossbarIssueModel = snapshot.CrossbarIssueModel,
        SerializationRule = snapshot.SerializationRule,
        StallReasonCodes = snapshot.StallReasonCodes.ToList(),
        ErrorCodes = snapshot.ErrorCodes.ToList()
    };

    /// <summary>Validates that a snapshot names the frozen Transport Semantics 1.0 contract and supported modes.</summary>
    public static TransportSemanticsValidationResult Validate(TransportSemanticsSnapshot? snapshot)
    {
        var result = new TransportSemanticsValidationResult();
        if (snapshot is null)
        {
            Add(result, "MissingTransportSemantics", "$.simulationConfig.transportSemantics", "Transport semantics are required.");
            return result;
        }

        if (!string.Equals(snapshot.Version, CurrentVersion, StringComparison.Ordinal))
        {
            Add(result, "UnsupportedTransportSemanticsVersion", "$.simulationConfig.transportSemantics.version", $"Transport Semantics version '{snapshot.Version}' is not supported; expected {CurrentVersion}.");
        }
        if (!Enum.IsDefined(typeof(PacketizationMode), snapshot.PacketizationMode))
        {
            Add(result, "UnsupportedPacketizationMode", "$.simulationConfig.transportSemantics.packetizationMode", $"Packetization mode '{snapshot.PacketizationMode}' is not supported.");
        }
        if (!Enum.IsDefined(typeof(FlowControlMode), snapshot.FlowControlMode))
        {
            Add(result, "UnsupportedFlowControlMode", "$.simulationConfig.transportSemantics.flowControlMode", $"Flow-control mode '{snapshot.FlowControlMode}' is not supported.");
        }
        if (!Enum.IsDefined(typeof(CrossbarIssueModel), snapshot.CrossbarIssueModel))
        {
            Add(result, "UnsupportedCrossbarIssueModel", "$.simulationConfig.transportSemantics.crossbarIssueModel", $"Crossbar issue model '{snapshot.CrossbarIssueModel}' is not supported.");
        }
        if (!string.Equals(snapshot.SerializationRule, SerializationRule, StringComparison.Ordinal))
        {
            Add(result, "UnsupportedSerializationRule", "$.simulationConfig.transportSemantics.serializationRule", $"Serialization rule must be '{SerializationRule}'.");
        }
        foreach (var code in FrozenStallReasonCodes)
        {
            if (!snapshot.StallReasonCodes.Contains(code, StringComparer.Ordinal))
            {
                Add(result, "MissingFrozenStallReasonCode", "$.simulationConfig.transportSemantics.stallReasonCodes", $"Frozen stall code '{code}' is required.");
            }
        }
        foreach (var code in FrozenErrorCodes)
        {
            if (!snapshot.ErrorCodes.Contains(code, StringComparer.Ordinal))
            {
                Add(result, "MissingFrozenErrorCode", "$.simulationConfig.transportSemantics.errorCodes", $"Frozen error code '{code}' is required.");
            }
        }

        return result;
    }

    private static void Add(TransportSemanticsValidationResult result, string code, string location, string message) =>
        result.Issues.Add(new TransportSemanticsValidationIssue(code, ValidationSeverity.Error, location, message));
}