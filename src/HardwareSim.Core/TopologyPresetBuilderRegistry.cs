namespace HardwareSim.Core;

/// <summary>Contains a topology preset builder resolution plus structured registry diagnostics.</summary>
public sealed class TopologyPresetBuilderResolution
{
    internal TopologyPresetBuilderResolution(
        ITopologyPresetBuilder? builder,
        IEnumerable<TopologyBuildIssue> issues)
    {
        Builder = builder;
        Issues = Array.AsReadOnly(issues.ToArray());
    }

    /// <summary>Gets the resolved builder when one stable topology id matched.</summary>
    public ITopologyPresetBuilder? Builder { get; }
    /// <summary>Gets immutable structured registry diagnostics.</summary>
    public IReadOnlyList<TopologyBuildIssue> Issues { get; }
    /// <summary>Gets whether one builder resolved without an error diagnostic.</summary>
    public bool IsSuccess => Builder is not null && Issues.All(issue => issue.Severity != ValidationSeverity.Error);
}

/// <summary>Resolves topology preset builders by stable id without a topology-specific switch.</summary>
public sealed class TopologyPresetBuilderRegistry
{
    private readonly IReadOnlyDictionary<string, ITopologyPresetBuilder> _builders;

    /// <summary>Creates the first-party topology preset registry without topology-specific dispatch switches.</summary>
    /// <returns>An immutable registry containing the MoT and ordinary flat two-dimensional mesh builders.</returns>
    public static TopologyPresetBuilderRegistry CreateDefault() => new(
    [
        new Flat2DMeshTopologyPresetBuilder(),
        new MeshOfTreesTopologyPresetBuilder()
    ]);

    /// <summary>Initializes an immutable registry snapshot from explicitly supplied builders.</summary>
    /// <param name="builders">Topology preset builders to register.</param>
    public TopologyPresetBuilderRegistry(IEnumerable<ITopologyPresetBuilder>? builders)
    {
        var supplied = (builders ?? []).Where(builder => builder is not null).ToArray();
        var issues = new List<TopologyBuildIssue>();
        foreach (var invalid in supplied.Where(builder => string.IsNullOrWhiteSpace(builder.TopologyId)))
        {
            issues.Add(new TopologyBuildIssue(
                TopologyBuildIssueCodes.InvalidBuilderRegistration,
                ValidationSeverity.Error,
                "$.builders",
                $"Topology preset builder '{invalid.GetType().FullName}' has no stable topology id."));
        }

        var valid = supplied.Where(builder => !string.IsNullOrWhiteSpace(builder.TopologyId)).ToArray();
        var dictionary = new Dictionary<string, ITopologyPresetBuilder>(StringComparer.Ordinal);
        foreach (var group in valid
                     .GroupBy(builder => builder.TopologyId.Trim(), StringComparer.Ordinal)
                     .OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            if (group.Count() != 1)
            {
                issues.Add(new TopologyBuildIssue(
                    TopologyBuildIssueCodes.DuplicateBuilderRegistration,
                    ValidationSeverity.Error,
                    "$.builders[" + group.Key + "]",
                    $"Topology id '{group.Key}' is registered by {group.Count()} builders; ambiguous ids are not resolved."));
                continue;
            }

            dictionary.Add(group.Key, group.Single());
        }

        _builders = dictionary;
        Issues = Array.AsReadOnly(issues
            .OrderBy(issue => issue.Location, StringComparer.Ordinal)
            .ThenBy(issue => issue.Code, StringComparer.Ordinal)
            .ToArray());
        TopologyIds = Array.AsReadOnly(dictionary.Keys.OrderBy(id => id, StringComparer.Ordinal).ToArray());
    }

    /// <summary>Gets registered stable topology ids in deterministic ordinal order.</summary>
    public IReadOnlyList<string> TopologyIds { get; }
    /// <summary>Gets immutable builder registration diagnostics.</summary>
    public IReadOnlyList<TopologyBuildIssue> Issues { get; }

    /// <summary>Resolves one builder by exact stable topology id.</summary>
    /// <param name="topologyId">Stable topology preset identity.</param>
    /// <returns>The matching builder or structured unsupported and registration diagnostics.</returns>
    public TopologyPresetBuilderResolution Resolve(string topologyId)
    {
        var normalized = topologyId?.Trim() ?? "";
        if (_builders.TryGetValue(normalized, out var builder))
        {
            return new TopologyPresetBuilderResolution(builder, Array.Empty<TopologyBuildIssue>());
        }

        var registrationLocation = "$.builders[" + normalized + "]";
        var issues = Issues
            .Where(issue => string.Equals(issue.Location, registrationLocation, StringComparison.Ordinal))
            .ToList();
        issues.Add(new TopologyBuildIssue(
            TopologyBuildIssueCodes.UnsupportedTopology,
            ValidationSeverity.Error,
            "$.topologyId",
            $"No topology preset builder is registered for stable id '{normalized}'."));
        return new TopologyPresetBuilderResolution(null, issues);
    }

    /// <summary>Attempts to resolve one builder without producing diagnostics.</summary>
    /// <param name="topologyId">Stable topology preset identity.</param>
    /// <param name="builder">Resolved builder when this method returns true.</param>
    /// <returns>True only when one exact stable id is registered.</returns>
    public bool TryResolve(string topologyId, out ITopologyPresetBuilder? builder) =>
        _builders.TryGetValue(topologyId?.Trim() ?? "", out builder);

    /// <summary>Dispatches a build request through the exact registered topology id.</summary>
    /// <param name="request">Immutable shared topology preset request.</param>
    /// <returns>A generated topology or structured missing and unsupported diagnostics.</returns>
    public TopologyBuildResult Build(TopologyPresetRequest request)
    {
        if (request is null)
        {
            return new TopologyBuildResult(new HardwareGraph(), null,
            [
                new TopologyBuildIssue(
                    TopologyBuildIssueCodes.MissingRequest,
                    ValidationSeverity.Error,
                    "$",
                    "A topology preset request is required.")
            ]);
        }

        var resolution = Resolve(request.TopologyId);
        return resolution.IsSuccess
            ? resolution.Builder!.Build(request)
            : new TopologyBuildResult(new HardwareGraph(), null, resolution.Issues);
    }
}
