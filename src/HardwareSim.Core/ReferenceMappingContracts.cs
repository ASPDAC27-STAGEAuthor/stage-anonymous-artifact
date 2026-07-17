using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace HardwareSim.Core;

/// <summary>Defines the stable topology preset identities accepted by reference mapping requests.</summary>
public static class ReferenceMappingTopologyIds
{
    /// <summary>Identifies the Phase 8A mesh-of-trees topology preset.</summary>
    public const string MeshOfTreesV1 = "com.hardware-sim.topology.mesh-of-trees.v1";

    /// <summary>Identifies the Phase 8A flat two-dimensional mesh topology preset.</summary>
    public const string Flat2DMeshV1 = "com.hardware-sim.topology.flat-2d-mesh.v1";
}

/// <summary>Defines the stable mapping policy identities accepted by reference mapping requests.</summary>
public static class ReferenceMappingPolicyIds
{
    /// <summary>Identifies the deterministic MoT policy that preserves the Python reference first-fit intent.</summary>
    public const string MotReferenceFirstFitV1 = "mot-reference-first-fit-v1";

    /// <summary>Identifies the deterministic topology-cost-aware mapping policy.</summary>
    public const string TopologyCostAwareV1 = "topology-cost-aware-v1";
}

/// <summary>Represents the row and column dimensions of a topology root mesh.</summary>
public sealed class ReferenceMappingMeshSize
{
    /// <summary>Initializes an immutable mesh-size value.</summary>
    /// <param name="rows">Number of mesh rows.</param>
    /// <param name="columns">Number of mesh columns.</param>
    public ReferenceMappingMeshSize(int rows, int columns)
    {
        Rows = rows;
        Columns = columns;
    }

    /// <summary>Gets the number of mesh rows.</summary>
    public int Rows { get; }

    /// <summary>Gets the number of mesh columns.</summary>
    public int Columns { get; }
}

/// <summary>Represents the row and column dimensions of a PE compute and weight tile.</summary>
public sealed class ReferenceMappingComputeSize
{
    /// <summary>Initializes an immutable compute-size value.</summary>
    /// <param name="rows">Number of compute rows.</param>
    /// <param name="columns">Number of compute columns.</param>
    public ReferenceMappingComputeSize(int rows, int columns)
    {
        Rows = rows;
        Columns = columns;
    }

    /// <summary>Initializes a square compute-size value from the supported scalar shorthand.</summary>
    /// <param name="size">Number of rows and columns.</param>
    public ReferenceMappingComputeSize(int size)
        : this(size, size)
    {
    }

    /// <summary>Gets the number of compute rows.</summary>
    public int Rows { get; }

    /// <summary>Gets the number of compute columns.</summary>
    public int Columns { get; }
}

/// <summary>Represents normalized matrix geometry for one workload operation.</summary>
public sealed class ReferenceMappingOperationGeometry
{
    /// <summary>Initializes immutable M, K, and N dimensions.</summary>
    /// <param name="m">Number of output rows.</param>
    /// <param name="k">Reduction dimension.</param>
    /// <param name="n">Number of output columns.</param>
    public ReferenceMappingOperationGeometry(int m, int k, int n)
    {
        M = m;
        K = k;
        N = n;
    }

    /// <summary>Gets the M dimension.</summary>
    public int M { get; }

    /// <summary>Gets the K dimension.</summary>
    public int K { get; }

    /// <summary>Gets the N dimension.</summary>
    public int N { get; }
}

/// <summary>Represents one immutable tensor identity and either inline values or an artifact hash.</summary>
public sealed class ReferenceMappingTensorArtifact
{
    /// <summary>Initializes an immutable tensor artifact.</summary>
    /// <param name="tensorId">Stable tensor identity.</param>
    /// <param name="values">Optional inline tensor values in canonical element order.</param>
    /// <param name="artifactHash">Optional content-addressed tensor artifact hash.</param>
    public ReferenceMappingTensorArtifact(
        string tensorId,
        IEnumerable<double>? values = null,
        string? artifactHash = null)
    {
        TensorId = NormalizeIdentifier(tensorId);
        Values = Array.AsReadOnly((values ?? []).ToArray());
        ArtifactHash = string.IsNullOrWhiteSpace(artifactHash) ? null : artifactHash.Trim();
    }

    /// <summary>Gets the stable tensor identity.</summary>
    public string TensorId { get; }

    /// <summary>Gets a defensive read-only copy of inline tensor values.</summary>
    public IReadOnlyList<double> Values { get; }

    /// <summary>Gets the optional normalized tensor artifact hash.</summary>
    public string? ArtifactHash { get; }

    private static string NormalizeIdentifier(string? value) => value?.Trim() ?? "";
}

/// <summary>Represents one immutable normalized workload operation used for reference mapping.</summary>
public sealed class ReferenceMappingWorkloadOperation
{
    /// <summary>Initializes a normalized workload operation.</summary>
    /// <param name="operationId">Stable operation identity.</param>
    /// <param name="operationTypeId">Stable operation semantic identity.</param>
    /// <param name="geometry">M, K, and N operation geometry.</param>
    /// <param name="precision">Concrete operation precision.</param>
    /// <param name="tensors">Tensor identities and their inline values or artifact hashes.</param>
    public ReferenceMappingWorkloadOperation(
        string operationId,
        string operationTypeId,
        ReferenceMappingOperationGeometry geometry,
        PrecisionKind precision,
        IEnumerable<ReferenceMappingTensorArtifact>? tensors = null)
    {
        OperationId = NormalizeIdentifier(operationId);
        OperationTypeId = NormalizeIdentifier(operationTypeId);
        Geometry = geometry ?? throw new ArgumentNullException(nameof(geometry));
        Precision = precision;
        Tensors = FreezeAndSortTensors(tensors);
    }

    /// <summary>Gets the stable operation identity.</summary>
    public string OperationId { get; }

    /// <summary>Gets the stable operation semantic identity.</summary>
    public string OperationTypeId { get; }

    /// <summary>Gets immutable M, K, and N operation geometry.</summary>
    public ReferenceMappingOperationGeometry Geometry { get; }

    /// <summary>Gets the concrete operation precision.</summary>
    public PrecisionKind Precision { get; }

    /// <summary>Gets tensor artifacts in normalized identity order.</summary>
    public IReadOnlyList<ReferenceMappingTensorArtifact> Tensors { get; }

    private static IReadOnlyList<ReferenceMappingTensorArtifact> FreezeAndSortTensors(
        IEnumerable<ReferenceMappingTensorArtifact>? tensors)
    {
        var supplied = (tensors ?? []).ToArray();
        if (supplied.Any(item => item is null))
        {
            throw new ArgumentException("Tensor artifacts cannot contain null entries.", nameof(tensors));
        }

        return Array.AsReadOnly(supplied
            .OrderBy(item => item.TensorId, StringComparer.Ordinal)
            .ToArray());
    }

    private static string NormalizeIdentifier(string? value) => value?.Trim() ?? "";
}

/// <summary>Represents an immutable normalized workload used by a reference mapping request.</summary>
public sealed class ReferenceMappingWorkload
{
    /// <summary>Initializes a normalized workload and freezes its operation order.</summary>
    /// <param name="workloadId">Stable workload identity.</param>
    /// <param name="operations">Normalized workload operations.</param>
    public ReferenceMappingWorkload(
        string workloadId,
        IEnumerable<ReferenceMappingWorkloadOperation>? operations = null)
    {
        WorkloadId = NormalizeIdentifier(workloadId);
        Operations = FreezeAndSortOperations(operations);
    }

    /// <summary>Gets the stable workload identity.</summary>
    public string WorkloadId { get; }

    /// <summary>Gets operations in normalized identity order.</summary>
    public IReadOnlyList<ReferenceMappingWorkloadOperation> Operations { get; }

    private static IReadOnlyList<ReferenceMappingWorkloadOperation> FreezeAndSortOperations(
        IEnumerable<ReferenceMappingWorkloadOperation>? operations)
    {
        var supplied = (operations ?? []).ToArray();
        if (supplied.Any(item => item is null))
        {
            throw new ArgumentException("Workload operations cannot contain null entries.", nameof(operations));
        }

        return Array.AsReadOnly(supplied
            .OrderBy(item => item.OperationId, StringComparer.Ordinal)
            .ThenBy(item => item.OperationTypeId, StringComparer.Ordinal)
            .ToArray());
    }

    private static string NormalizeIdentifier(string? value) => value?.Trim() ?? "";
}

/// <summary>Represents the immutable, unambiguous input contract for Phase 8A reference mapping.</summary>
public sealed class ReferenceMappingRequest
{
    /// <summary>Initializes an immutable reference mapping request.</summary>
    /// <param name="workload">Normalized workload and tensor IR.</param>
    /// <param name="topologyId">Stable topology preset identity.</param>
    /// <param name="clusterSize">Number of PEs per MoT cluster, or one for the flat mesh canonical profile.</param>
    /// <param name="meshSize">Rows and columns of the cluster-root or PE-attached router mesh.</param>
    /// <param name="computeSize">Rows and columns of the PE compute and weight tile.</param>
    /// <param name="rowDivisionSize">Number of K rows covered by one division.</param>
    /// <param name="policyId">Stable mapping policy identity and version.</param>
    /// <param name="seed">Explicit deterministic provenance seed.</param>
    public ReferenceMappingRequest(
        ReferenceMappingWorkload workload,
        string topologyId,
        int clusterSize,
        ReferenceMappingMeshSize meshSize,
        ReferenceMappingComputeSize computeSize,
        int rowDivisionSize,
        string policyId,
        int seed)
    {
        Workload = workload ?? throw new ArgumentNullException(nameof(workload));
        TopologyId = NormalizeIdentifier(topologyId);
        ClusterSize = clusterSize;
        MeshSize = meshSize ?? throw new ArgumentNullException(nameof(meshSize));
        ComputeSize = computeSize ?? throw new ArgumentNullException(nameof(computeSize));
        RowDivisionSize = rowDivisionSize;
        PolicyId = NormalizeIdentifier(policyId);
        Seed = seed;
    }

    /// <summary>Gets the immutable normalized workload and tensor IR.</summary>
    public ReferenceMappingWorkload Workload { get; }

    /// <summary>Gets the stable topology preset identity.</summary>
    public string TopologyId { get; }

    /// <summary>Gets the number of PEs in each MoT cluster, or one for a canonical flat mesh.</summary>
    public int ClusterSize { get; }

    /// <summary>Gets the root mesh dimensions.</summary>
    public ReferenceMappingMeshSize MeshSize { get; }

    /// <summary>Gets the PE compute and weight tile dimensions.</summary>
    public ReferenceMappingComputeSize ComputeSize { get; }

    /// <summary>Gets the number of K rows covered by one division.</summary>
    public int RowDivisionSize { get; }

    /// <summary>Gets the stable mapping policy identity and version.</summary>
    public string PolicyId { get; }

    /// <summary>Gets the explicit deterministic provenance seed.</summary>
    public int Seed { get; }

    /// <summary>Gets the canonical number of mesh roots or routers.</summary>
    public long ClusterCount => checked((long)MeshSize.Rows * MeshSize.Columns);

    /// <summary>Gets the canonical total PE count for the selected topology, or zero for an unsupported topology.</summary>
    public long TotalProcessingElements =>
        string.Equals(TopologyId, ReferenceMappingTopologyIds.MeshOfTreesV1, StringComparison.Ordinal)
            ? checked(ClusterCount * ClusterSize)
            : string.Equals(TopologyId, ReferenceMappingTopologyIds.Flat2DMeshV1, StringComparison.Ordinal)
                ? ClusterCount
                : 0;

    /// <summary>Validates all canonical Phase 8A reference mapping constraints.</summary>
    public ReferenceMappingValidationResult Validate() => ReferenceMappingRequestValidator.Validate(this);

    /// <summary>Computes the SHA-256 hash and canonical JSON for a valid normalized request.</summary>
    public ReferenceMappingCanonicalHash ComputeCanonicalHash() => ReferenceMappingRequestCanonicalizer.Compute(this);

    private static string NormalizeIdentifier(string? value) => value?.Trim() ?? "";
}

/// <summary>Describes one structured reference mapping request validation diagnostic.</summary>
/// <param name="Code">Stable machine-readable issue code.</param>
/// <param name="Severity">Diagnostic severity.</param>
/// <param name="Location">JSON-style location of the invalid request element.</param>
/// <param name="Message">Human-readable diagnostic message.</param>
/// <param name="RelatedId">Optional workload, operation, or tensor identity.</param>
public sealed record ReferenceMappingValidationIssue(
    string Code,
    ValidationSeverity Severity,
    string Location,
    string Message,
    string? RelatedId = null);

/// <summary>Contains immutable structured validation diagnostics for a reference mapping request.</summary>
public sealed class ReferenceMappingValidationResult
{
    internal ReferenceMappingValidationResult(IEnumerable<ReferenceMappingValidationIssue> issues)
    {
        Issues = Array.AsReadOnly(issues.ToArray());
    }

    /// <summary>Gets validation diagnostics in deterministic request order.</summary>
    public IReadOnlyList<ReferenceMappingValidationIssue> Issues { get; }

    /// <summary>Gets whether validation found no error diagnostics.</summary>
    public bool IsSuccess => Issues.All(issue => issue.Severity != ValidationSeverity.Error);
}

/// <summary>Defines stable issue codes emitted by reference mapping request validation.</summary>
public static class ReferenceMappingValidationIssueCodes
{
    /// <summary>Identifies a missing request.</summary>
    public const string MissingRequest = "MissingReferenceMappingRequest";
    /// <summary>Identifies an unsupported topology preset.</summary>
    public const string UnsupportedTopology = "UnsupportedReferenceMappingTopology";
    /// <summary>Identifies an invalid MoT or flat-mesh cluster size.</summary>
    public const string InvalidClusterSize = "InvalidReferenceMappingClusterSize";
    /// <summary>Identifies a non-power-of-two MoT cluster size.</summary>
    public const string NonPowerOfTwoClusterSize = "NonPowerOfTwoReferenceMappingClusterSize";
    /// <summary>Identifies invalid mesh dimensions.</summary>
    public const string InvalidMeshSize = "InvalidReferenceMappingMeshSize";
    /// <summary>Identifies invalid compute dimensions.</summary>
    public const string InvalidComputeSize = "InvalidReferenceMappingComputeSize";
    /// <summary>Identifies a non-square reference-compatible compute tile.</summary>
    public const string NonSquareComputeSize = "NonSquareReferenceMappingComputeSize";
    /// <summary>Identifies an invalid K row division size.</summary>
    public const string InvalidRowDivisionSize = "InvalidReferenceMappingRowDivisionSize";
    /// <summary>Identifies a row division that is not aligned to compute rows.</summary>
    public const string MisalignedRowDivisionSize = "MisalignedReferenceMappingRowDivisionSize";
    /// <summary>Identifies an unsupported mapping policy.</summary>
    public const string UnsupportedPolicy = "UnsupportedReferenceMappingPolicy";
    /// <summary>Identifies a missing workload identity.</summary>
    public const string MissingWorkloadId = "MissingReferenceMappingWorkloadId";
    /// <summary>Identifies a workload without operations.</summary>
    public const string MissingOperations = "MissingReferenceMappingOperations";
    /// <summary>Identifies a missing operation identity.</summary>
    public const string MissingOperationId = "MissingReferenceMappingOperationId";
    /// <summary>Identifies duplicate operation identities.</summary>
    public const string DuplicateOperationId = "DuplicateReferenceMappingOperationId";
    /// <summary>Identifies a missing operation semantic identity.</summary>
    public const string MissingOperationType = "MissingReferenceMappingOperationType";
    /// <summary>Identifies non-positive M, K, or N operation geometry.</summary>
    public const string InvalidOperationGeometry = "InvalidReferenceMappingOperationGeometry";
    /// <summary>Identifies a non-concrete or unsupported operation precision.</summary>
    public const string InvalidPrecision = "InvalidReferenceMappingPrecision";
    /// <summary>Identifies an operation without tensor identities.</summary>
    public const string MissingTensors = "MissingReferenceMappingTensors";
    /// <summary>Identifies a missing tensor identity.</summary>
    public const string MissingTensorId = "MissingReferenceMappingTensorId";
    /// <summary>Identifies duplicate tensor identities within one operation.</summary>
    public const string DuplicateTensorId = "DuplicateReferenceMappingTensorId";
    /// <summary>Identifies a tensor without inline values or an artifact hash.</summary>
    public const string MissingTensorPayload = "MissingReferenceMappingTensorPayload";
    /// <summary>Identifies a non-finite inline tensor value.</summary>
    public const string NonFiniteTensorValue = "NonFiniteReferenceMappingTensorValue";
    /// <summary>Identifies a K range that is not completely covered by row divisions.</summary>
    public const string IncompleteKCoverage = "IncompleteReferenceMappingKCoverage";
    /// <summary>Identifies a derived cluster or PE count that exceeds Int64 capacity.</summary>
    public const string CapacityOverflow = "ReferenceMappingCapacityOverflow";
}

/// <summary>Validates the canonical semantic constraints of Phase 8A reference mapping requests.</summary>
public static class ReferenceMappingRequestValidator
{
    /// <summary>Validates the supplied request and returns deterministic structured diagnostics.</summary>
    /// <param name="request">Reference mapping request to validate.</param>
    /// <returns>Immutable validation result.</returns>
    public static ReferenceMappingValidationResult Validate(ReferenceMappingRequest? request)
    {
        var issues = new List<ReferenceMappingValidationIssue>();
        if (request is null)
        {
            Add(issues, ReferenceMappingValidationIssueCodes.MissingRequest, "$", "ReferenceMappingRequest is required.");
            return new ReferenceMappingValidationResult(issues);
        }

        var isMot = string.Equals(request.TopologyId, ReferenceMappingTopologyIds.MeshOfTreesV1, StringComparison.Ordinal);
        var isFlat = string.Equals(request.TopologyId, ReferenceMappingTopologyIds.Flat2DMeshV1, StringComparison.Ordinal);
        if (!isMot && !isFlat)
        {
            Add(issues, ReferenceMappingValidationIssueCodes.UnsupportedTopology, "$.topology_id",
                $"Topology '{request.TopologyId}' is not a supported reference mapping topology.");
        }

        if (isMot)
        {
            if (request.ClusterSize < 2)
            {
                Add(issues, ReferenceMappingValidationIssueCodes.InvalidClusterSize, "$.cluster_size",
                    "Mesh-of-trees cluster_size must be at least two.");
            }
            else if (!IsPowerOfTwo(request.ClusterSize))
            {
                Add(issues, ReferenceMappingValidationIssueCodes.NonPowerOfTwoClusterSize, "$.cluster_size",
                    "Reference-compatible mesh-of-trees cluster_size must be a power of two.");
            }
        }
        else if (isFlat && request.ClusterSize != 1)
        {
            Add(issues, ReferenceMappingValidationIssueCodes.InvalidClusterSize, "$.cluster_size",
                "The canonical flat mesh profile requires cluster_size equal to one.");
        }

        if (request.MeshSize.Rows <= 0 || request.MeshSize.Columns <= 0)
        {
            Add(issues, ReferenceMappingValidationIssueCodes.InvalidMeshSize, "$.mesh_size",
                "mesh_size rows and columns must both be positive.");
        }

        if (request.ComputeSize.Rows <= 0 || request.ComputeSize.Columns <= 0)
        {
            Add(issues, ReferenceMappingValidationIssueCodes.InvalidComputeSize, "$.compute_size",
                "compute_size rows and columns must both be positive.");
        }
        else if (request.ComputeSize.Rows != request.ComputeSize.Columns)
        {
            Add(issues, ReferenceMappingValidationIssueCodes.NonSquareComputeSize, "$.compute_size",
                "Reference-compatible PE compute_size must be square.");
        }

        if (request.RowDivisionSize <= 0)
        {
            Add(issues, ReferenceMappingValidationIssueCodes.InvalidRowDivisionSize, "$.row_division_size",
                "row_division_size must be positive.");
        }
        else if (request.ComputeSize.Rows > 0 && request.RowDivisionSize % request.ComputeSize.Rows != 0)
        {
            Add(issues, ReferenceMappingValidationIssueCodes.MisalignedRowDivisionSize, "$.row_division_size",
                "row_division_size must be divisible by compute_size.rows.");
        }

        if (!string.Equals(request.PolicyId, ReferenceMappingPolicyIds.MotReferenceFirstFitV1, StringComparison.Ordinal) &&
            !string.Equals(request.PolicyId, ReferenceMappingPolicyIds.TopologyCostAwareV1, StringComparison.Ordinal))
        {
            Add(issues, ReferenceMappingValidationIssueCodes.UnsupportedPolicy, "$.policy_id",
                $"Policy '{request.PolicyId}' is not a supported reference mapping policy.");
        }

        ValidateWorkload(request, issues);
        ValidateDerivedCapacity(request, isMot, issues);
        return new ReferenceMappingValidationResult(issues);
    }

    private static void ValidateWorkload(
        ReferenceMappingRequest request,
        List<ReferenceMappingValidationIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(request.Workload.WorkloadId))
        {
            Add(issues, ReferenceMappingValidationIssueCodes.MissingWorkloadId, "$.workload.workload_id",
                "A stable workload identity is required.");
        }

        if (request.Workload.Operations.Count == 0)
        {
            Add(issues, ReferenceMappingValidationIssueCodes.MissingOperations, "$.workload.operations",
                "At least one normalized workload operation is required.");
            return;
        }

        var operationIds = new HashSet<string>(StringComparer.Ordinal);
        for (var operationIndex = 0; operationIndex < request.Workload.Operations.Count; operationIndex++)
        {
            var operation = request.Workload.Operations[operationIndex];
            var operationPath = $"$.workload.operations[{operationIndex}]";
            if (string.IsNullOrWhiteSpace(operation.OperationId))
            {
                Add(issues, ReferenceMappingValidationIssueCodes.MissingOperationId, operationPath + ".operation_id",
                    "A stable operation identity is required.");
            }
            else if (!operationIds.Add(operation.OperationId))
            {
                Add(issues, ReferenceMappingValidationIssueCodes.DuplicateOperationId, operationPath + ".operation_id",
                    $"Operation identity '{operation.OperationId}' is duplicated.", operation.OperationId);
            }

            if (string.IsNullOrWhiteSpace(operation.OperationTypeId))
            {
                Add(issues, ReferenceMappingValidationIssueCodes.MissingOperationType, operationPath + ".operation",
                    "A stable operation semantic identity is required.", operation.OperationId);
            }

            if (operation.Geometry.M <= 0 || operation.Geometry.K <= 0 || operation.Geometry.N <= 0)
            {
                Add(issues, ReferenceMappingValidationIssueCodes.InvalidOperationGeometry, operationPath + ".geometry",
                    "Operation M, K, and N dimensions must all be positive.", operation.OperationId);
            }

            if (!Enum.IsDefined(typeof(PrecisionKind), operation.Precision) ||
                operation.Precision is PrecisionKind.Any or PrecisionKind.Analog)
            {
                Add(issues, ReferenceMappingValidationIssueCodes.InvalidPrecision, operationPath + ".precision",
                    "Operation precision must be a concrete digital PrecisionKind for Phase 8A.", operation.OperationId);
            }

            if (request.RowDivisionSize > 0 && operation.Geometry.K > 0 &&
                operation.Geometry.K % request.RowDivisionSize != 0)
            {
                Add(issues, ReferenceMappingValidationIssueCodes.IncompleteKCoverage, operationPath + ".geometry.k",
                    $"Operation K={operation.Geometry.K} is not completely covered by row_division_size={request.RowDivisionSize}.",
                    operation.OperationId);
            }

            ValidateTensors(operation, operationIndex, issues);
        }
    }

    private static void ValidateTensors(
        ReferenceMappingWorkloadOperation operation,
        int operationIndex,
        List<ReferenceMappingValidationIssue> issues)
    {
        var operationPath = $"$.workload.operations[{operationIndex}]";
        if (operation.Tensors.Count == 0)
        {
            Add(issues, ReferenceMappingValidationIssueCodes.MissingTensors, operationPath + ".tensors",
                "At least one tensor identity is required.", operation.OperationId);
            return;
        }

        var tensorIds = new HashSet<string>(StringComparer.Ordinal);
        for (var tensorIndex = 0; tensorIndex < operation.Tensors.Count; tensorIndex++)
        {
            var tensor = operation.Tensors[tensorIndex];
            var tensorPath = operationPath + $".tensors[{tensorIndex}]";
            if (string.IsNullOrWhiteSpace(tensor.TensorId))
            {
                Add(issues, ReferenceMappingValidationIssueCodes.MissingTensorId, tensorPath + ".tensor_id",
                    "A stable tensor identity is required.", operation.OperationId);
            }
            else if (!tensorIds.Add(tensor.TensorId))
            {
                Add(issues, ReferenceMappingValidationIssueCodes.DuplicateTensorId, tensorPath + ".tensor_id",
                    $"Tensor identity '{tensor.TensorId}' is duplicated within operation '{operation.OperationId}'.",
                    tensor.TensorId);
            }

            if (tensor.Values.Count == 0 && string.IsNullOrWhiteSpace(tensor.ArtifactHash))
            {
                Add(issues, ReferenceMappingValidationIssueCodes.MissingTensorPayload, tensorPath,
                    "A tensor requires inline values or an artifact hash.", tensor.TensorId);
            }

            for (var valueIndex = 0; valueIndex < tensor.Values.Count; valueIndex++)
            {
                var value = tensor.Values[valueIndex];
                if (double.IsNaN(value) || double.IsInfinity(value))
                {
                    Add(issues, ReferenceMappingValidationIssueCodes.NonFiniteTensorValue,
                        tensorPath + $".values[{valueIndex}]", "Inline tensor values must be finite.", tensor.TensorId);
                }
            }
        }
    }

    private static void ValidateDerivedCapacity(
        ReferenceMappingRequest request,
        bool isMot,
        List<ReferenceMappingValidationIssue> issues)
    {
        if (request.MeshSize.Rows <= 0 || request.MeshSize.Columns <= 0 || request.ClusterSize <= 0)
        {
            return;
        }

        try
        {
            var clusterCount = checked((long)request.MeshSize.Rows * request.MeshSize.Columns);
            if (isMot)
            {
                _ = checked(clusterCount * request.ClusterSize);
            }
        }
        catch (OverflowException)
        {
            Add(issues, ReferenceMappingValidationIssueCodes.CapacityOverflow, "$.mesh_size",
                "Derived cluster_count or total_pe exceeds Int64 capacity.");
        }
    }

    private static bool IsPowerOfTwo(int value) => value > 0 && (value & (value - 1)) == 0;

    private static void Add(
        List<ReferenceMappingValidationIssue> issues,
        string code,
        string location,
        string message,
        string? relatedId = null) =>
        issues.Add(new ReferenceMappingValidationIssue(code, ValidationSeverity.Error, location, message, relatedId));
}

/// <summary>Represents the canonical serialization and SHA-256 digest of a valid reference mapping request.</summary>
public sealed class ReferenceMappingCanonicalHash
{
    internal ReferenceMappingCanonicalHash(string hash, string canonicalJson)
    {
        Hash = hash;
        CanonicalJson = canonicalJson;
    }

    /// <summary>Gets the canonical hash algorithm identifier.</summary>
    public string Algorithm => ReferenceMappingRequestCanonicalizer.Algorithm;

    /// <summary>Gets the lowercase hexadecimal SHA-256 digest.</summary>
    public string Hash { get; }

    /// <summary>Gets the normalized canonical JSON document hashed by this result.</summary>
    public string CanonicalJson { get; }
}

/// <summary>Builds normalized canonical JSON and SHA-256 hashes for reference mapping requests.</summary>
public static class ReferenceMappingRequestCanonicalizer
{
    /// <summary>Defines the canonical request hash algorithm.</summary>
    public const string Algorithm = "sha256/reference-mapping-request-1.0/v1";

    /// <summary>Computes canonical JSON and a stable SHA-256 digest for a valid request.</summary>
    /// <param name="request">Immutable normalized request.</param>
    /// <returns>Canonical JSON and digest.</returns>
    /// <exception cref="ArgumentNullException">The request is null.</exception>
    /// <exception cref="InvalidOperationException">The request fails structured validation.</exception>
    public static ReferenceMappingCanonicalHash Compute(ReferenceMappingRequest request)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }
        var validation = ReferenceMappingRequestValidator.Validate(request);
        if (!validation.IsSuccess)
        {
            var issue = validation.Issues.First(item => item.Severity == ValidationSeverity.Error);
            throw new InvalidOperationException(
                $"ReferenceMappingRequest must be valid before canonical hashing: {issue.Code} at {issue.Location}.");
        }

        var canonicalJson = BuildCanonicalDocument(request).ToJsonString(new JsonSerializerOptions
        {
            WriteIndented = false
        });
        return new ReferenceMappingCanonicalHash(ComputeSha256(canonicalJson), canonicalJson);
    }

    private static JsonObject BuildCanonicalDocument(ReferenceMappingRequest request)
    {
        var operations = new JsonArray();
        foreach (var operation in request.Workload.Operations)
        {
            var tensors = new JsonArray();
            foreach (var tensor in operation.Tensors)
            {
                var values = new JsonArray();
                foreach (var value in tensor.Values)
                {
                    values.Add(JsonValue.Create(value));
                }

                tensors.Add(new JsonObject
                {
                    ["tensor_id"] = tensor.TensorId,
                    ["values"] = values,
                    ["artifact_hash"] = tensor.ArtifactHash
                });
            }

            operations.Add(new JsonObject
            {
                ["operation_id"] = operation.OperationId,
                ["operation"] = operation.OperationTypeId,
                ["geometry"] = new JsonObject
                {
                    ["m"] = operation.Geometry.M,
                    ["k"] = operation.Geometry.K,
                    ["n"] = operation.Geometry.N
                },
                ["precision"] = operation.Precision.ToString(),
                ["tensors"] = tensors
            });
        }

        return new JsonObject
        {
            ["workload"] = new JsonObject
            {
                ["workload_id"] = request.Workload.WorkloadId,
                ["operations"] = operations
            },
            ["topology_id"] = request.TopologyId,
            ["cluster_size"] = request.ClusterSize,
            ["mesh_size"] = new JsonObject
            {
                ["rows"] = request.MeshSize.Rows,
                ["columns"] = request.MeshSize.Columns
            },
            ["compute_size"] = new JsonObject
            {
                ["rows"] = request.ComputeSize.Rows,
                ["columns"] = request.ComputeSize.Columns
            },
            ["row_division_size"] = request.RowDivisionSize,
            ["policy_id"] = request.PolicyId,
            ["seed"] = request.Seed
        };
    }

    private static string ComputeSha256(string value)
    {
        using var sha256 = SHA256.Create();
        var digest = sha256.ComputeHash(Encoding.UTF8.GetBytes(value));
        var builder = new StringBuilder(digest.Length * 2);
        foreach (var item in digest)
        {
            builder.Append(item.ToString("x2", CultureInfo.InvariantCulture));
        }

        return builder.ToString();
    }
}
