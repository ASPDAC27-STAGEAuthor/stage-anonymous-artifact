using System.Collections.ObjectModel;

namespace HardwareSim.Core;

/// <summary>Frozen paper-workload controls and explicit numerical-model boundaries.</summary>
public sealed class Phase8APaperWorkloadProfile
{
    /// <summary>Creates a validated paper-workload profile.</summary>
    public Phase8APaperWorkloadProfile(
        int batch,
        int sequenceLength,
        int queryHeadCount,
        int keyValueHeadCount,
        int headDimension,
        int peTileRows,
        int peTileColumns,
        string projectionModel,
        string attentionMultiplierModel,
        string softmaxModel,
        IEnumerable<string>? weightResidencyModes)
    {
        if (batch <= 0 || sequenceLength <= 0 || queryHeadCount <= 0 || keyValueHeadCount <= 0 ||
            headDimension <= 0 || peTileRows <= 0 || peTileColumns <= 0)
            throw new ArgumentOutOfRangeException(nameof(batch), "Paper workload dimensions and PE tile extents must be positive.");
        if (queryHeadCount % keyValueHeadCount != 0)
            throw new ArgumentException("Query heads must be divisible by key/value heads.", nameof(queryHeadCount));
        if (string.IsNullOrWhiteSpace(projectionModel) || string.IsNullOrWhiteSpace(attentionMultiplierModel) || string.IsNullOrWhiteSpace(softmaxModel))
            throw new ArgumentException("Projection, attention multiplier, and softmax modeling status must be explicit.");
        var modes = (weightResidencyModes ?? []).Select(value => value?.Trim() ?? "")
            .Where(value => value.Length > 0).Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal).ToArray();
        if (!modes.SequenceEqual(new[] { "amortized", "cold", "steady_state" }, StringComparer.Ordinal))
            throw new ArgumentException("Weight residency modes must contain exactly cold, steady_state, and amortized.", nameof(weightResidencyModes));

        Batch = batch;
        SequenceLength = sequenceLength;
        QueryHeadCount = queryHeadCount;
        KeyValueHeadCount = keyValueHeadCount;
        HeadDimension = headDimension;
        PeTileRows = peTileRows;
        PeTileColumns = peTileColumns;
        ProjectionModel = projectionModel.Trim();
        AttentionMultiplierModel = attentionMultiplierModel.Trim();
        SoftmaxModel = softmaxModel.Trim();
        WeightResidencyModes = Array.AsReadOnly(modes);
    }

    /// <summary>Gets batch size.</summary>
    public int Batch { get; }
    /// <summary>Gets sequence length.</summary>
    public int SequenceLength { get; }
    /// <summary>Gets query-head count.</summary>
    public int QueryHeadCount { get; }
    /// <summary>Gets key/value-head count.</summary>
    public int KeyValueHeadCount { get; }
    /// <summary>Gets per-head dimension.</summary>
    public int HeadDimension { get; }
    /// <summary>Gets PE tile row extent.</summary>
    public int PeTileRows { get; }
    /// <summary>Gets PE tile column extent.</summary>
    public int PeTileColumns { get; }
    /// <summary>Gets the Q/K/V projection modeling status.</summary>
    public string ProjectionModel { get; }
    /// <summary>Gets the attention multiplier modeling status.</summary>
    public string AttentionMultiplierModel { get; }
    /// <summary>Gets the softmax modeling status.</summary>
    public string SoftmaxModel { get; }
    /// <summary>Gets the required cold, steady-state, and amortized weight modes.</summary>
    public IReadOnlyList<string> WeightResidencyModes { get; }

    /// <summary>Returns the approved LLaMA-1B communication-study controls.</summary>
    public static Phase8APaperWorkloadProfile CanonicalLlama1B() => new(
        1, 2048, 32, 8, 64, 32, 32,
        "mapped_traffic_runtime",
        "timing_only",
        "timing_only",
        ["cold", "steady_state", "amortized"]);

    /// <summary>Returns stable common controls suitable for factorial drift validation.</summary>
    public IReadOnlyDictionary<string, string> ToFrozenControls() =>
        new ReadOnlyDictionary<string, string>(new SortedDictionary<string, string>(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["batch"] = Batch.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["sequence_length"] = SequenceLength.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["query_head_count"] = QueryHeadCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["key_value_head_count"] = KeyValueHeadCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["head_dimension"] = HeadDimension.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["pe_tile"] = $"{PeTileRows}x{PeTileColumns}",
            ["projection_model"] = ProjectionModel,
            ["attention_multiplier_model"] = AttentionMultiplierModel,
            ["softmax_model"] = SoftmaxModel,
            ["weight_residency_modes"] = string.Join(",", WeightResidencyModes)
        }, StringComparer.Ordinal));
}
