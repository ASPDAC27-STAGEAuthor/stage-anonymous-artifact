using System.Collections.ObjectModel;

namespace HardwareSim.Core;

/// <summary>Typed numeric artifacts for a two-layer tiled MLP reference.</summary>
public sealed class Phase8ATiledMlpRequest
{
    /// <summary>Creates immutable row-major MLP artifacts.</summary>
    public Phase8ATiledMlpRequest(
        IEnumerable<double>? input,
        IEnumerable<double>? weights1,
        IEnumerable<double>? bias1,
        IEnumerable<double>? weights2,
        IEnumerable<double>? bias2,
        int inputExtent,
        int hiddenExtent,
        int outputExtent,
        int layer1NPartitionExtent,
        int layer2KPartitionExtent,
        string inputDType = "fp8",
        string weightDType = "fp8",
        string accumulateDType = "fp16",
        string outputDType = "fp8")
    {
        Input = Array.AsReadOnly((input ?? []).ToArray());
        Weights1 = Array.AsReadOnly((weights1 ?? []).ToArray());
        Bias1 = Array.AsReadOnly((bias1 ?? []).ToArray());
        Weights2 = Array.AsReadOnly((weights2 ?? []).ToArray());
        Bias2 = Array.AsReadOnly((bias2 ?? []).ToArray());
        InputExtent = inputExtent;
        HiddenExtent = hiddenExtent;
        OutputExtent = outputExtent;
        Layer1NPartitionExtent = layer1NPartitionExtent;
        Layer2KPartitionExtent = layer2KPartitionExtent;
        InputDType = DigitalNumericFormats.NormalizeDType(inputDType);
        WeightDType = DigitalNumericFormats.NormalizeDType(weightDType);
        AccumulateDType = DigitalNumericFormats.NormalizeDType(accumulateDType);
        OutputDType = DigitalNumericFormats.NormalizeDType(outputDType);
    }
    /// <summary>Gets 1xK input values.</summary>
    public IReadOnlyList<double> Input { get; }
    /// <summary>Gets KxN layer-1 weights.</summary>
    public IReadOnlyList<double> Weights1 { get; }
    /// <summary>Gets layer-1 bias.</summary>
    public IReadOnlyList<double> Bias1 { get; }
    /// <summary>Gets NxO layer-2 weights.</summary>
    public IReadOnlyList<double> Weights2 { get; }
    /// <summary>Gets layer-2 bias.</summary>
    public IReadOnlyList<double> Bias2 { get; }
    /// <summary>Gets input K extent.</summary>
    public int InputExtent { get; }
    /// <summary>Gets hidden N extent.</summary>
    public int HiddenExtent { get; }
    /// <summary>Gets output extent.</summary>
    public int OutputExtent { get; }
    /// <summary>Gets layer-1 N tile extent.</summary>
    public int Layer1NPartitionExtent { get; }
    /// <summary>Gets layer-2 K tile extent.</summary>
    public int Layer2KPartitionExtent { get; }
    /// <summary>Gets input dtype.</summary>
    public string InputDType { get; }
    /// <summary>Gets weight dtype.</summary>
    public string WeightDType { get; }
    /// <summary>Gets accumulate dtype.</summary>
    public string AccumulateDType { get; }
    /// <summary>Gets output dtype.</summary>
    public string OutputDType { get; }
}

/// <summary>Exact tiled reference result and partition provenance.</summary>
public sealed record Phase8ATiledMlpResult(
    IReadOnlyList<double> Output,
    string OutputHash,
    int Layer1NPartitionCount,
    int Layer2KPartitionCount,
    IReadOnlyList<string> StageProvenance);

/// <summary>Independent tiled numeric reference for paper-scale MLP artifacts.</summary>
public static class Phase8ATiledMlpReference
{
    /// <summary>Evaluates N-partition/Concat then K-partition/Sum with exact digital encodings.</summary>
    public static Phase8ATiledMlpResult Evaluate(Phase8ATiledMlpRequest request)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));
        Validate(request);
        var hidden = new double[request.HiddenExtent];
        var provenance = new List<string>();
        var nPartitions = request.HiddenExtent / request.Layer1NPartitionExtent;
        for (var partition = 0; partition < nPartitions; partition++)
        {
            var offset = partition * request.Layer1NPartitionExtent;
            var tileWeights = Enumerable.Range(0, request.InputExtent)
                .SelectMany(row => request.Weights1.Skip(row * request.HiddenExtent + offset).Take(request.Layer1NPartitionExtent)).ToArray();
            var tile = DigitalVmmReferenceEvaluator.Evaluate(
                request.Input, tileWeights, request.InputExtent, request.Layer1NPartitionExtent,
                request.InputDType, request.WeightDType, request.AccumulateDType, request.OutputDType);
            Array.Copy(tile.ToArray(), 0, hidden, offset, tile.Count);
            provenance.Add($"layer1:n_partition={partition};offset={offset};extent={tile.Count};op=concat");
        }
        for (var index = 0; index < hidden.Length; index++)
            hidden[index] = Math.Max(0, DigitalNumericFormats.Quantize(hidden[index] + request.Bias1[index], request.OutputDType).Value);

        var output = new double[request.OutputExtent];
        var kPartitions = request.HiddenExtent / request.Layer2KPartitionExtent;
        for (var partition = 0; partition < kPartitions; partition++)
        {
            var rowOffset = partition * request.Layer2KPartitionExtent;
            var tileInput = hidden.Skip(rowOffset).Take(request.Layer2KPartitionExtent).ToArray();
            var tileWeights = request.Weights2.Skip(rowOffset * request.OutputExtent)
                .Take(request.Layer2KPartitionExtent * request.OutputExtent).ToArray();
            var partial = DigitalVmmReferenceEvaluator.Evaluate(
                tileInput, tileWeights, request.Layer2KPartitionExtent, request.OutputExtent,
                request.OutputDType, request.WeightDType, request.AccumulateDType, request.OutputDType);
            for (var index = 0; index < output.Length; index++) output[index] += partial[index];
            provenance.Add($"layer2:k_partition={partition};offset={rowOffset};extent={request.Layer2KPartitionExtent};op=sum");
        }
        for (var index = 0; index < output.Length; index++)
            output[index] = DigitalNumericFormats.Quantize(output[index] + request.Bias2[index], request.OutputDType).Value;
        return new Phase8ATiledMlpResult(
            Array.AsReadOnly(output),
            DigitalNumericFormats.HashEncodedValues("phase8a-paper-mlp-output", request.OutputDType, 1, request.OutputExtent, output),
            nPartitions,
            kPartitions,
            new ReadOnlyCollection<string>(provenance));
    }

    private static void Validate(Phase8ATiledMlpRequest request)
    {
        if (request.InputExtent <= 0 || request.HiddenExtent <= 0 || request.OutputExtent <= 0 ||
            request.Layer1NPartitionExtent <= 0 || request.Layer2KPartitionExtent <= 0 ||
            request.HiddenExtent % request.Layer1NPartitionExtent != 0 || request.HiddenExtent % request.Layer2KPartitionExtent != 0)
            throw new ArgumentException("Positive dimensions and exact N/K partition divisibility are required.", nameof(request));
        var expectedWeights1 = (long)request.InputExtent * request.HiddenExtent;
        var expectedWeights2 = (long)request.HiddenExtent * request.OutputExtent;
        if (expectedWeights1 > int.MaxValue || expectedWeights2 > int.MaxValue ||
            request.Input.Count != request.InputExtent || request.Weights1.Count != expectedWeights1 ||
            request.Bias1.Count != request.HiddenExtent || request.Weights2.Count != expectedWeights2 ||
            request.Bias2.Count != request.OutputExtent ||
            request.Input.Concat(request.Weights1).Concat(request.Bias1).Concat(request.Weights2).Concat(request.Bias2).Any(value => !double.IsFinite(value)))
            throw new ArgumentException("MLP artifact shapes and finite numeric values must exactly match declared dimensions.", nameof(request));
    }
}
