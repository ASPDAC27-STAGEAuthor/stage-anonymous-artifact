using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;

namespace HardwareSim.Core;

/// <summary>Selects the semantic MatMul partition axes.</summary>
public enum Phase8AMatMulPartitionKind
{
    /// <summary>Partitions K and reduces partial sums for the same output range.</summary>
    K,
    /// <summary>Partitions N and concatenates disjoint output-column ranges.</summary>
    N,
    /// <summary>Partitions K and N, reducing each N shard before concatenation.</summary>
    Hybrid
}

/// <summary>Defines stable Phase 8A lowered tensor role identifiers.</summary>
public static class Phase8ATensorRoleIds
{
    /// <summary>MatMul activation operand.</summary>
    public const string Activation = "activation";
    /// <summary>MatMul weight operand.</summary>
    public const string Weight = "weight";
    /// <summary>One compute issue's partial output.</summary>
    public const string PartialOutput = "partial-output";
    /// <summary>One K-reduced output shard.</summary>
    public const string ReducedOutput = "reduced-output";
    /// <summary>One N-assembled final output tile.</summary>
    public const string FinalOutput = "final-output";
}

/// <summary>Defines stable typed collective intent identifiers.</summary>
public static class Phase8ACollectiveIntentKinds
{
    /// <summary>Element-wise grouped vector sum for identical output ranges.</summary>
    public const string Sum = "sum";
    /// <summary>Offset-aware concatenation for disjoint N ranges.</summary>
    public const string Concat = "concat";
}

/// <summary>Defines stable tensor-lowering diagnostic codes.</summary>
public static class Phase8ATensorLoweringIssueCodes
{
    /// <summary>A required identifier or capability was absent.</summary>
    public const string MissingInput = "MatMulLoweringInputMissing";
    /// <summary>A tensor dimension or tile extent was non-positive.</summary>
    public const string InvalidShape = "MatMulLoweringShapeInvalid";
    /// <summary>A pure K or N policy requested the other partition axis.</summary>
    public const string PartitionSemanticMismatch = "MatMulPartitionSemanticMismatch";
    /// <summary>The requested tile exceeds the compiled shape contract.</summary>
    public const string TileShapeUnsupported = "MatMulTileShapeUnsupported";
    /// <summary>A required compiled shape contract was absent or invalid.</summary>
    public const string ShapeContractMissing = "MatMulShapeContractMissing";
    /// <summary>A non-digital or unsupported precision was requested.</summary>
    public const string PrecisionUnsupported = "MatMulPrecisionUnsupported";
    /// <summary>No storage capability can hold the padded weight tile.</summary>
    public const string CapacityExceeded = "MatMulCapacityExceeded";
    /// <summary>64-bit arithmetic or bounded plan materialization overflowed.</summary>
    public const string ArithmeticOverflow = "MatMulLoweringArithmeticOverflow";
    /// <summary>Generated ranges failed exact M, K, or N coverage.</summary>
    public const string CoverageInvalid = "MatMulLoweringCoverageInvalid";
}

/// <summary>Requests immutable lowering of X[M,K] x W[K,N] into Mapping 2.0-ready tiles.</summary>
public sealed class Phase8AMatMulLoweringRequest
{
    /// <summary>Creates a MatMul lowering request.</summary>
    public Phase8AMatMulLoweringRequest(
        string operationId,
        string activationTensorId,
        string weightTensorId,
        string outputTensorId,
        long m,
        long k,
        long n,
        long tileM,
        long tileK,
        long tileN,
        PrecisionKind activationPrecision,
        PrecisionKind weightPrecision,
        PrecisionKind outputPrecision,
        Phase8AMatMulPartitionKind partitionKind,
        ComponentCapabilitySnapshot capability)
    {
        OperationId = operationId?.Trim() ?? "";
        ActivationTensorId = activationTensorId?.Trim() ?? "";
        WeightTensorId = weightTensorId?.Trim() ?? "";
        OutputTensorId = outputTensorId?.Trim() ?? "";
        M = m;
        K = k;
        N = n;
        TileM = tileM;
        TileK = tileK;
        TileN = tileN;
        ActivationPrecision = activationPrecision;
        WeightPrecision = weightPrecision;
        OutputPrecision = outputPrecision;
        PartitionKind = partitionKind;
        Capability = capability ?? throw new ArgumentNullException(nameof(capability));
    }

    /// <summary>Gets the source operation id.</summary>
    public string OperationId { get; }
    /// <summary>Gets the activation tensor id.</summary>
    public string ActivationTensorId { get; }
    /// <summary>Gets the weight tensor id.</summary>
    public string WeightTensorId { get; }
    /// <summary>Gets the output tensor id.</summary>
    public string OutputTensorId { get; }
    /// <summary>Gets M.</summary>
    public long M { get; }
    /// <summary>Gets K.</summary>
    public long K { get; }
    /// <summary>Gets N.</summary>
    public long N { get; }
    /// <summary>Gets the requested physical M tile extent.</summary>
    public long TileM { get; }
    /// <summary>Gets the requested physical K tile extent.</summary>
    public long TileK { get; }
    /// <summary>Gets the requested physical N tile extent.</summary>
    public long TileN { get; }
    /// <summary>Gets activation precision.</summary>
    public PrecisionKind ActivationPrecision { get; }
    /// <summary>Gets weight precision.</summary>
    public PrecisionKind WeightPrecision { get; }
    /// <summary>Gets output precision.</summary>
    public PrecisionKind OutputPrecision { get; }
    /// <summary>Gets partition semantics.</summary>
    public Phase8AMatMulPartitionKind PartitionKind { get; }
    /// <summary>Gets the immutable target capability used only for hard feasibility.</summary>
    public ComponentCapabilitySnapshot Capability { get; }
}

/// <summary>Records explicit tail padding and final crop provenance.</summary>
/// <param name="MHighPadding">Padded rows after the valid M range.</param>
/// <param name="KHighPadding">Padded reduction elements after the valid K range.</param>
/// <param name="NHighPadding">Padded columns after the valid N range.</param>
/// <param name="CropRequired">Whether padded output elements must be removed.</param>
/// <param name="CropStageId">Stable stage at which cropping becomes visible.</param>
public sealed record Phase8APaddingCropProvenance(
    long MHighPadding,
    long KHighPadding,
    long NHighPadding,
    bool CropRequired,
    string CropStageId);

/// <summary>Represents one immutable MatMul operand tile.</summary>
public sealed class Phase8ALoweredOperandTile
{
    internal Phase8ALoweredOperandTile(
        string tileId, string tensorId, string roleId, string precisionId,
        MappingIndexRange mRange, MappingIndexRange kRange, MappingIndexRange nRange,
        MappingShape validShape, MappingShape paddedShape, Phase8APaddingCropProvenance padding)
    {
        TileId = tileId;
        TensorId = tensorId;
        RoleId = roleId;
        PrecisionId = precisionId;
        MRange = mRange;
        KRange = kRange;
        NRange = nRange;
        ValidShape = validShape;
        PaddedShape = paddedShape;
        Padding = padding;
    }

    /// <summary>Gets the stable tile id.</summary>
    public string TileId { get; }
    /// <summary>Gets the source tensor id.</summary>
    public string TensorId { get; }
    /// <summary>Gets activation or weight role.</summary>
    public string RoleId { get; }
    /// <summary>Gets the exact precision id.</summary>
    public string PrecisionId { get; }
    /// <summary>Gets the M range, or an empty range when the operand has no M axis.</summary>
    public MappingIndexRange MRange { get; }
    /// <summary>Gets the K range.</summary>
    public MappingIndexRange KRange { get; }
    /// <summary>Gets the N range, or an empty range when the operand has no N axis.</summary>
    public MappingIndexRange NRange { get; }
    /// <summary>Gets the unpadded operand shape.</summary>
    public MappingShape ValidShape { get; }
    /// <summary>Gets the physical padded operand shape.</summary>
    public MappingShape PaddedShape { get; }
    /// <summary>Gets padding provenance.</summary>
    public Phase8APaddingCropProvenance Padding { get; }
}

/// <summary>Represents one immutable partial, reduced, or final output tile.</summary>
public sealed class Phase8ALoweredOutputTile
{
    internal Phase8ALoweredOutputTile(
        string tileId, string tensorId, string roleId, string precisionId,
        MappingIndexRange mRange, MappingIndexRange nRange,
        MappingShape validShape, MappingShape paddedShape, Phase8APaddingCropProvenance padding)
    {
        TileId = tileId;
        TensorId = tensorId;
        RoleId = roleId;
        PrecisionId = precisionId;
        MRange = mRange;
        NRange = nRange;
        ValidShape = validShape;
        PaddedShape = paddedShape;
        Padding = padding;
    }

    /// <summary>Gets the stable tile id.</summary>
    public string TileId { get; }
    /// <summary>Gets the output tensor id.</summary>
    public string TensorId { get; }
    /// <summary>Gets partial, reduced, or final role.</summary>
    public string RoleId { get; }
    /// <summary>Gets the exact output precision id.</summary>
    public string PrecisionId { get; }
    /// <summary>Gets the valid M range.</summary>
    public MappingIndexRange MRange { get; }
    /// <summary>Gets the valid N range.</summary>
    public MappingIndexRange NRange { get; }
    /// <summary>Gets the valid output shape.</summary>
    public MappingShape ValidShape { get; }
    /// <summary>Gets the padded compute shape.</summary>
    public MappingShape PaddedShape { get; }
    /// <summary>Gets padding and crop provenance.</summary>
    public Phase8APaddingCropProvenance Padding { get; }
}

/// <summary>Represents one immutable compute issue with OperationTileAssignment-ready ranges.</summary>
public sealed class Phase8ALoweredOperationTile
{
    internal Phase8ALoweredOperationTile(
        string operationTileId, string operationId, string activationTileId, string weightTileId,
        string outputTileId, MappingIndexRange mRange, MappingIndexRange kRange, MappingIndexRange nRange,
        MappingShape validShape, MappingShape paddedShape, Phase8AMatMulPartitionKind partitionKind)
    {
        OperationTileId = operationTileId;
        OperationId = operationId;
        ActivationTileId = activationTileId;
        WeightTileId = weightTileId;
        OutputTileId = outputTileId;
        MRange = mRange;
        KRange = kRange;
        NRange = nRange;
        ValidShape = validShape;
        PaddedShape = paddedShape;
        PartitionKind = partitionKind;
    }

    /// <summary>Gets the stable operation-tile id.</summary>
    public string OperationTileId { get; }
    /// <summary>Gets the source operation id.</summary>
    public string OperationId { get; }
    /// <summary>Gets the activation operand tile id.</summary>
    public string ActivationTileId { get; }
    /// <summary>Gets the weight operand tile id.</summary>
    public string WeightTileId { get; }
    /// <summary>Gets the partial output tile id.</summary>
    public string OutputTileId { get; }
    /// <summary>Gets the exact M range.</summary>
    public MappingIndexRange MRange { get; }
    /// <summary>Gets the exact K range.</summary>
    public MappingIndexRange KRange { get; }
    /// <summary>Gets the exact N range.</summary>
    public MappingIndexRange NRange { get; }
    /// <summary>Gets the valid [M,N] output shape.</summary>
    public MappingShape ValidShape { get; }
    /// <summary>Gets the physical [M,K,N] compute shape.</summary>
    public MappingShape PaddedShape { get; }
    /// <summary>Gets the selected partition semantics.</summary>
    public Phase8AMatMulPartitionKind PartitionKind { get; }

    /// <summary>Creates a Mapping 2.0 assignment after a mapper selects a concrete target.</summary>
    public OperationTileAssignment CreateAssignment(string assignmentId, string targetComponentId, string targetPortId) => new(
        assignmentId, OperationId, OutputTileId, targetComponentId, targetPortId,
        [ActivationTileId, WeightTileId], MRange, KRange, NRange, ValidShape, PaddedShape,
        PartitionKind.ToString().ToLowerInvariant());
}

/// <summary>Represents a typed immutable Sum or Concat intent emitted by lowering.</summary>
public sealed class Phase8ALoweredCollectiveIntent
{
    internal Phase8ALoweredCollectiveIntent(
        string intentId, string kindId, string groupKey, int stageOrder,
        IReadOnlyList<string> contributorTileIds, string resultTileId,
        MappingIndexRange mRange, MappingIndexRange nRange, string precisionId)
    {
        IntentId = intentId;
        KindId = kindId;
        GroupKey = groupKey;
        StageOrder = stageOrder;
        ContributorTileIds = new ReadOnlyCollection<string>(contributorTileIds.ToList());
        ResultTileId = resultTileId;
        MRange = mRange;
        NRange = nRange;
        PrecisionId = precisionId;
    }

    /// <summary>Gets the stable intent id.</summary>
    public string IntentId { get; }
    /// <summary>Gets sum or concat.</summary>
    public string KindId { get; }
    /// <summary>Gets the isolation key for contributors.</summary>
    public string GroupKey { get; }
    /// <summary>Gets ordering stage; Sum precedes Concat.</summary>
    public int StageOrder { get; }
    /// <summary>Gets contributors in deterministic numeric order.</summary>
    public IReadOnlyList<string> ContributorTileIds { get; }
    /// <summary>Gets the result tile id.</summary>
    public string ResultTileId { get; }
    /// <summary>Gets the covered M range.</summary>
    public MappingIndexRange MRange { get; }
    /// <summary>Gets the covered N range.</summary>
    public MappingIndexRange NRange { get; }
    /// <summary>Gets accumulation or assembly precision.</summary>
    public string PrecisionId { get; }
}

/// <summary>Contains a deterministic immutable MatMul lowering plan.</summary>
public sealed class Phase8AMatMulLoweringPlan
{
    internal Phase8AMatMulLoweringPlan(
        string operationId,
        IReadOnlyList<Phase8ALoweredOperandTile> operands,
        IReadOnlyList<Phase8ALoweredOutputTile> outputs,
        IReadOnlyList<Phase8ALoweredOperationTile> operations,
        IReadOnlyList<Phase8ALoweredCollectiveIntent> collectives,
        IReadOnlyList<string> finalOutputTileIds,
        string canonicalHash)
    {
        OperationId = operationId;
        OperandTiles = new ReadOnlyCollection<Phase8ALoweredOperandTile>(operands.OrderBy(item => item.TileId, StringComparer.Ordinal).ToList());
        OutputTiles = new ReadOnlyCollection<Phase8ALoweredOutputTile>(outputs.OrderBy(item => item.TileId, StringComparer.Ordinal).ToList());
        OperationTiles = new ReadOnlyCollection<Phase8ALoweredOperationTile>(operations.OrderBy(item => item.OperationTileId, StringComparer.Ordinal).ToList());
        CollectiveIntents = new ReadOnlyCollection<Phase8ALoweredCollectiveIntent>(collectives.OrderBy(item => item.StageOrder).ThenBy(item => item.IntentId, StringComparer.Ordinal).ToList());
        FinalOutputTileIds = new ReadOnlyCollection<string>(finalOutputTileIds.ToList());
        CanonicalHash = canonicalHash;
    }

    /// <summary>Gets the source operation id.</summary>
    public string OperationId { get; }
    /// <summary>Gets deduplicated activation and weight tiles.</summary>
    public IReadOnlyList<Phase8ALoweredOperandTile> OperandTiles { get; }
    /// <summary>Gets partial, reduced, and assembled output tiles.</summary>
    public IReadOnlyList<Phase8ALoweredOutputTile> OutputTiles { get; }
    /// <summary>Gets concrete compute issues.</summary>
    public IReadOnlyList<Phase8ALoweredOperationTile> OperationTiles { get; }
    /// <summary>Gets typed collectives in execution-stage order.</summary>
    public IReadOnlyList<Phase8ALoweredCollectiveIntent> CollectiveIntents { get; }
    /// <summary>Gets final cropped output tiles in increasing M order.</summary>
    public IReadOnlyList<string> FinalOutputTileIds { get; }
    /// <summary>Gets the exact deterministic semantic plan hash.</summary>
    public string CanonicalHash { get; }
}

/// <summary>Returns a lowering plan or structured hard failures.</summary>
public sealed class Phase8AMatMulLoweringResult
{
    internal Phase8AMatMulLoweringResult(Phase8AMatMulLoweringPlan? plan, IEnumerable<WorkloadMappingV2Issue> issues)
    {
        Plan = plan;
        Issues = new ReadOnlyCollection<WorkloadMappingV2Issue>(issues.ToList());
    }

    /// <summary>Gets the immutable plan after success.</summary>
    public Phase8AMatMulLoweringPlan? Plan { get; }
    /// <summary>Gets deterministic structured diagnostics.</summary>
    public IReadOnlyList<WorkloadMappingV2Issue> Issues { get; }
    /// <summary>Gets whether lowering produced a complete plan.</summary>
    public bool IsSuccess => Plan is not null && Issues.All(issue => issue.Severity != ValidationSeverity.Error);
}

/// <summary>Lowers MatMul into exact M/K/N ranges without silent tail truncation.</summary>
public static class Phase8ATensorLowerer
{
    private const long MaximumMaterializedOperationTiles = 1_000_000;

    /// <summary>Lowers a validated request into immutable operand, output, assignment, and collective IR.</summary>
    public static Phase8AMatMulLoweringResult LowerMatMul(Phase8AMatMulLoweringRequest request)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));
        var issues = Validate(request);
        if (issues.Count > 0) return new Phase8AMatMulLoweringResult(null, issues);

        try
        {
            var mCount = CeilingDiv(request.M, request.TileM);
            var kCount = CeilingDiv(request.K, request.TileK);
            var nCount = CeilingDiv(request.N, request.TileN);
            var count = checked(checked(mCount * kCount) * nCount);
            if (count > MaximumMaterializedOperationTiles)
                return Failure(Phase8ATensorLoweringIssueCodes.ArithmeticOverflow, "$.tiles",
                    $"Lowering would materialize {count} operation tiles, exceeding the deterministic bound {MaximumMaterializedOperationTiles}.");

            var operands = new Dictionary<string, Phase8ALoweredOperandTile>(StringComparer.Ordinal);
            var outputs = new Dictionary<string, Phase8ALoweredOutputTile>(StringComparer.Ordinal);
            var operations = new List<Phase8ALoweredOperationTile>();
            var contributors = new Dictionary<(long M, long N), List<string>>();
            var activationPrecision = request.ActivationPrecision.ToString();
            var weightPrecision = request.WeightPrecision.ToString();
            var outputPrecision = request.OutputPrecision.ToString();

            for (long mi = 0; mi < mCount; mi++)
            {
                var m = Range(mi, request.TileM, request.M);
                for (long ni = 0; ni < nCount; ni++)
                {
                    var n = Range(ni, request.TileN, request.N);
                    var group = contributors[(mi, ni)] = [];
                    for (long ki = 0; ki < kCount; ki++)
                    {
                        var k = Range(ki, request.TileK, request.K);
                        var activationId = $"{request.OperationId}:x:m{mi}:k{ki}";
                        var weightId = $"{request.OperationId}:w:k{ki}:n{ni}";
                        var partialId = $"{request.OperationId}:y:partial:m{mi}:k{ki}:n{ni}";
                        operands.TryAdd(activationId, new Phase8ALoweredOperandTile(
                            activationId, request.ActivationTensorId, Phase8ATensorRoleIds.Activation, activationPrecision,
                            m, k, new MappingIndexRange(0, 0), Shape(m.Extent, k.Extent), Shape(request.TileM, request.TileK),
                            Padding(request.TileM - m.Extent, request.TileK - k.Extent, 0, false, "none")));
                        operands.TryAdd(weightId, new Phase8ALoweredOperandTile(
                            weightId, request.WeightTensorId, Phase8ATensorRoleIds.Weight, weightPrecision,
                            new MappingIndexRange(0, 0), k, n, Shape(k.Extent, n.Extent), Shape(request.TileK, request.TileN),
                            Padding(0, request.TileK - k.Extent, request.TileN - n.Extent, false, "none")));
                        outputs.Add(partialId, Output(
                            partialId, request.OutputTensorId, Phase8ATensorRoleIds.PartialOutput, outputPrecision,
                            m, n, request.TileM, request.TileN, "final-output-crop"));
                        operations.Add(new Phase8ALoweredOperationTile(
                            $"{request.OperationId}:issue:m{mi}:k{ki}:n{ni}", request.OperationId,
                            activationId, weightId, partialId, m, k, n,
                            Shape(m.Extent, n.Extent), Shape(request.TileM, request.TileK, request.TileN), request.PartitionKind));
                        group.Add(partialId);
                    }
                }
            }

            var collectives = new List<Phase8ALoweredCollectiveIntent>();
            var nShardResults = new Dictionary<(long M, long N), string>();
            for (long mi = 0; mi < mCount; mi++)
            {
                var m = Range(mi, request.TileM, request.M);
                for (long ni = 0; ni < nCount; ni++)
                {
                    var n = Range(ni, request.TileN, request.N);
                    var inputs = contributors[(mi, ni)];
                    if (inputs.Count == 1)
                    {
                        nShardResults[(mi, ni)] = inputs[0];
                        continue;
                    }
                    var reducedId = $"{request.OperationId}:y:reduced:m{mi}:n{ni}";
                    outputs.Add(reducedId, Output(reducedId, request.OutputTensorId, Phase8ATensorRoleIds.ReducedOutput,
                        outputPrecision, m, n, request.TileM, request.TileN, "final-output-crop"));
                    collectives.Add(new Phase8ALoweredCollectiveIntent(
                        $"{request.OperationId}:sum:m{mi}:n{ni}", Phase8ACollectiveIntentKinds.Sum,
                        $"{request.OperationId}|m={m.Offset}:{m.Extent}|n={n.Offset}:{n.Extent}", 0,
                        inputs, reducedId, m, n, outputPrecision));
                    nShardResults[(mi, ni)] = reducedId;
                }
            }

            var finals = new List<string>();
            for (long mi = 0; mi < mCount; mi++)
            {
                var m = Range(mi, request.TileM, request.M);
                var inputs = Enumerable.Range(0, checked((int)nCount)).Select(ni => nShardResults[(mi, ni)]).ToList();
                if (inputs.Count == 1)
                {
                    finals.Add(inputs[0]);
                    continue;
                }
                var finalId = $"{request.OperationId}:y:final:m{mi}";
                outputs.Add(finalId, new Phase8ALoweredOutputTile(
                    finalId, request.OutputTensorId, Phase8ATensorRoleIds.FinalOutput, outputPrecision,
                    m, new MappingIndexRange(0, request.N), Shape(m.Extent, request.N), Shape(request.TileM, request.N),
                    Padding(request.TileM - m.Extent, 0, 0, request.TileM != m.Extent, "final-output-crop")));
                collectives.Add(new Phase8ALoweredCollectiveIntent(
                    $"{request.OperationId}:concat:m{mi}", Phase8ACollectiveIntentKinds.Concat,
                    $"{request.OperationId}|m={m.Offset}:{m.Extent}|n=0:{request.N}", 1,
                    inputs, finalId, m, new MappingIndexRange(0, request.N), outputPrecision));
                finals.Add(finalId);
            }

            if (!CoverageIsExact(request, operations))
                return Failure(Phase8ATensorLoweringIssueCodes.CoverageInvalid, "$.operationTiles",
                    "Generated operation ranges do not cover the complete M/K/N domain exactly.");

            var semantic = new
            {
                algorithm = "sha256/phase8a-matmul-lowering/v1",
                request.OperationId,
                shape = new[] { request.M, request.K, request.N },
                tile = new[] { request.TileM, request.TileK, request.TileN },
                partition = request.PartitionKind.ToString(),
                capability = request.Capability.ProfileHash,
                operands = operands.Values.OrderBy(item => item.TileId, StringComparer.Ordinal),
                outputs = outputs.Values.OrderBy(item => item.TileId, StringComparer.Ordinal),
                operations = operations.OrderBy(item => item.OperationTileId, StringComparer.Ordinal),
                collectives = collectives.OrderBy(item => item.StageOrder).ThenBy(item => item.IntentId, StringComparer.Ordinal),
                finals
            };
            var hash = ComponentExecutionJson.ComputeSha256(ComponentExecutionJson.CanonicalizeJson(
                JsonSerializer.Serialize(semantic, HardwareGraphJson.Options)));
            return new Phase8AMatMulLoweringResult(new Phase8AMatMulLoweringPlan(
                request.OperationId, operands.Values.ToList(), outputs.Values.ToList(), operations, collectives, finals, hash), []);
        }
        catch (OverflowException)
        {
            return Failure(Phase8ATensorLoweringIssueCodes.ArithmeticOverflow, "$.shape",
                "MatMul lowering arithmetic exceeded the supported 64-bit range.");
        }
    }

    private static List<WorkloadMappingV2Issue> Validate(Phase8AMatMulLoweringRequest request)
    {
        var issues = new List<WorkloadMappingV2Issue>();
        if (new[] { request.OperationId, request.ActivationTensorId, request.WeightTensorId, request.OutputTensorId }
            .Any(string.IsNullOrWhiteSpace))
            issues.Add(Error(Phase8ATensorLoweringIssueCodes.MissingInput, "$.identifiers", "Operation and tensor identifiers are required."));
        if (request.M <= 0 || request.K <= 0 || request.N <= 0 || request.TileM <= 0 || request.TileK <= 0 || request.TileN <= 0)
            issues.Add(Error(Phase8ATensorLoweringIssueCodes.InvalidShape, "$.shape", "M, K, N, and tile extents must be positive."));
        if (request.PartitionKind == Phase8AMatMulPartitionKind.K && request.TileN < request.N)
            issues.Add(Error(Phase8ATensorLoweringIssueCodes.PartitionSemanticMismatch, "$.tileN", "K partition cannot also shard N; select Hybrid."));
        if (request.PartitionKind == Phase8AMatMulPartitionKind.N && request.TileK < request.K)
            issues.Add(Error(Phase8ATensorLoweringIssueCodes.PartitionSemanticMismatch, "$.tileK", "N partition cannot also shard K; select Hybrid."));

        var maxM = ShapeLimit(request.Capability, Phase8ACapabilityShapeKeys.MatMulMaximumM, issues);
        var maxK = ShapeLimit(request.Capability, Phase8ACapabilityShapeKeys.MatMulMaximumK, issues);
        var maxN = ShapeLimit(request.Capability, Phase8ACapabilityShapeKeys.MatMulMaximumN, issues);
        if (maxM.HasValue && request.TileM > maxM.Value || maxK.HasValue && request.TileK > maxK.Value || maxN.HasValue && request.TileN > maxN.Value)
            issues.Add(Error(Phase8ATensorLoweringIssueCodes.TileShapeUnsupported, "$.tileShape",
                "Requested physical tile extents exceed the compiled semantic port shape contract.", request.Capability.ComponentId));

        var precisions = new[] { request.ActivationPrecision, request.WeightPrecision, request.OutputPrecision };
        foreach (var precision in precisions.Distinct())
        {
            if (BitWidth(precision) is null || !request.Capability.PrecisionIds.Contains(precision.ToString(), StringComparer.Ordinal))
                issues.Add(Error(Phase8ATensorLoweringIssueCodes.PrecisionUnsupported, "$.precision",
                    $"Precision '{precision}' is not a concrete digital precision exposed by the compiled capability.", request.Capability.ComponentId));
        }

        var weightBits = BitWidth(request.WeightPrecision);
        if (weightBits.HasValue && request.TileK > 0 && request.TileN > 0)
        {
            try
            {
                var required = checked(checked(request.TileK * request.TileN) * weightBits.Value);
                var compatible = request.Capability.StorageCapabilities
                    .Where(storage => storage.SupportedOperandRoleIds.Contains(Phase8ATensorRoleIds.Weight, StringComparer.Ordinal) &&
                                      storage.SupportedPrecisionIds.Contains(request.WeightPrecision.ToString(), StringComparer.Ordinal))
                    .ToList();
                if (compatible.Count == 0 || compatible.Max(storage => storage.CapacityBits) < required)
                    issues.Add(Error(Phase8ATensorLoweringIssueCodes.CapacityExceeded, "$.capability.storageCapabilities",
                        $"No declared weight storage can hold the padded {request.TileK}x{request.TileN} tile ({required} bits).",
                        request.Capability.ComponentId));
            }
            catch (OverflowException)
            {
                issues.Add(Error(Phase8ATensorLoweringIssueCodes.ArithmeticOverflow, "$.tileShape",
                    "Padded weight tile size exceeds Int64.", request.Capability.ComponentId));
            }
        }
        return issues;
    }

    private static long? ShapeLimit(ComponentCapabilitySnapshot capability, string key, List<WorkloadMappingV2Issue> issues)
    {
        if (capability.ShapeContracts.TryGetValue(key, out var raw) &&
            long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) && value > 0) return value;
        issues.Add(Error(Phase8ATensorLoweringIssueCodes.ShapeContractMissing, $"$.capability.shapeContracts.{key}",
            $"Compiled capability is missing positive shape contract '{key}'.", capability.ComponentId));
        return null;
    }

    private static int? BitWidth(PrecisionKind precision) => precision switch
    {
        PrecisionKind.FP32 or PrecisionKind.TF32 or PrecisionKind.INT32 => 32,
        PrecisionKind.FP16 or PrecisionKind.BF16 or PrecisionKind.INT16 => 16,
        PrecisionKind.FP8_E4M3 or PrecisionKind.FP8_E5M2 or PrecisionKind.INT8 => 8,
        PrecisionKind.INT4 => 4,
        PrecisionKind.INT2 => 2,
        PrecisionKind.Binary => 1,
        _ => null
    };

    private static MappingIndexRange Range(long index, long tile, long total)
    {
        var offset = checked(index * tile);
        return new MappingIndexRange(offset, Math.Min(tile, checked(total - offset)));
    }

    private static long CeilingDiv(long value, long divisor) => checked(1 + (value - 1) / divisor);
    private static MappingShape Shape(params long[] dimensions) => new(dimensions);
    private static Phase8APaddingCropProvenance Padding(long m, long k, long n, bool crop, string stage) => new(m, k, n, crop, stage);

    private static Phase8ALoweredOutputTile Output(
        string id, string tensorId, string role, string precision, MappingIndexRange m, MappingIndexRange n,
        long tileM, long tileN, string cropStage) => new(
            id, tensorId, role, precision, m, n, Shape(m.Extent, n.Extent), Shape(tileM, tileN),
            Padding(tileM - m.Extent, 0, tileN - n.Extent, tileM != m.Extent || tileN != n.Extent, cropStage));

    private static bool CoverageIsExact(Phase8AMatMulLoweringRequest request, IReadOnlyList<Phase8ALoweredOperationTile> operations)
    {
        var m = operations.Select(item => item.MRange).Distinct().OrderBy(item => item.Offset).ToList();
        var k = operations.Select(item => item.KRange).Distinct().OrderBy(item => item.Offset).ToList();
        var n = operations.Select(item => item.NRange).Distinct().OrderBy(item => item.Offset).ToList();
        return Covers(m, request.M) && Covers(k, request.K) && Covers(n, request.N);
    }

    private static bool Covers(IReadOnlyList<MappingIndexRange> ranges, long total)
    {
        long cursor = 0;
        foreach (var range in ranges)
        {
            if (range.Offset != cursor || range.Extent <= 0) return false;
            cursor = checked(cursor + range.Extent);
        }
        return cursor == total;
    }

    private static Phase8AMatMulLoweringResult Failure(string code, string location, string message) =>
        new(null, [Error(code, location, message)]);
    private static WorkloadMappingV2Issue Error(string code, string location, string message, string? relatedId = null) =>
        new(code, ValidationSeverity.Error, location, message, relatedId);
}
