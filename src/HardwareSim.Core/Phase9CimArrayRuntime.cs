using System.Collections.ObjectModel;
using System.Globalization;

#pragma warning disable CS1591

namespace HardwareSim.Core;

public enum Phase9CimOperationKind { Mvm, Read, Write, Calibration }

public sealed record Phase9CimOperationCharacterization(
    Phase9CimOperationKind Operation,
    int BaseLatencyCycles,
    double EnergyPicojoulesPerUnit,
    string EnergyUnit,
    int ParallelUnitsPerCycle,
    string ProfileHash,
    IReadOnlyList<string> SourceRecordIds,
    string ModelVersion,
    string Formula,
    string Uncertainty,
    NormalizedDeviceEvidenceStatus EvidenceStatus = NormalizedDeviceEvidenceStatus.Derived);

/// <summary>Array storage geometry projected onto the shared bit-addressed StorageMap.</summary>
public sealed record Phase9CimArrayLayout(
    string LayoutId,
    int Banks,
    int Rows,
    int Columns,
    int CellBits,
    int LogicalWeightBits,
    int BitSliceCount,
    string Encoding,
    bool Signed,
    bool RuntimeWriteAllowed,
    long CapacityBits,
    string LayoutHash)
{
    public int PhysicalBitsPerLogicalWeight => checked((int)Math.Ceiling(LogicalWeightBits / (double)CellBits) * CellBits);
    public long LogicalWeightCapacity => CapacityBits / PhysicalBitsPerLogicalWeight;

    public static Phase9CimArrayLayout Create(string layoutId, int banks, int rows, int columns, int cellBits, int logicalWeightBits, string encoding, bool signed, bool runtimeWriteAllowed = true, int? bitSliceCount = null)
    {
        if (string.IsNullOrWhiteSpace(layoutId)) throw new ArgumentException("Layout id is required.", nameof(layoutId));
        if (banks <= 0 || rows <= 0 || columns <= 0 || cellBits <= 0 || logicalWeightBits <= 0) throw new ArgumentOutOfRangeException(nameof(rows));
        var slices = bitSliceCount ?? checked((int)Math.Ceiling(logicalWeightBits / (double)cellBits));
        if (slices <= 0 || slices * cellBits < logicalWeightBits) throw new ArgumentOutOfRangeException(nameof(bitSliceCount));
        var capacity = checked((long)banks * rows * columns * cellBits);
        var hash = ComponentTemplateJson.StableHash(new { layoutId, banks, rows, columns, cellBits, logicalWeightBits, slices, encoding, signed, runtimeWriteAllowed, capacity });
        return new(layoutId.Trim(), banks, rows, columns, cellBits, logicalWeightBits, slices, encoding ?? "", signed, runtimeWriteAllowed, capacity, hash);
    }

    public static Phase9CimArrayLayout FromTemplate(TemplateStorageLayout layout, int logicalWeightBits)
    {
        if (layout is null) throw new ArgumentNullException(nameof(layout));
        var slices = layout.BitSlices.Count > 0 ? layout.BitSlices.Count : checked((int)Math.Ceiling(logicalWeightBits / (double)layout.CellBits));
        return Create(layout.Id, layout.Banks, layout.Rows, layout.Columns, layout.CellBits, logicalWeightBits, layout.Encoding, layout.Signed, layout.RuntimeWriteAllowed, slices);
    }
}

public sealed record Phase9CimRuntimeIssue(string Code, string Path, string Message);

public sealed class Phase9CimOperationResult
{
    internal Phase9CimOperationResult(bool success, Phase9CimOperationKind operation, IEnumerable<double>? values, int latencyCycles, double energyPicojoules, double utilization, IEnumerable<Phase9CimRuntimeIssue>? issues, IEnumerable<string>? traceFacts, StorageMapResult? storageResult, Phase9CimOperationCharacterization? characterization)
    {
        IsSuccess = success;
        Operation = operation;
        Values = new ReadOnlyCollection<double>((values ?? []).ToArray());
        LatencyCycles = latencyCycles;
        EnergyPicojoules = energyPicojoules;
        Utilization = utilization;
        Issues = new ReadOnlyCollection<Phase9CimRuntimeIssue>((issues ?? []).ToArray());
        TraceFacts = new ReadOnlyCollection<string>((traceFacts ?? []).ToArray());
        StorageResult = storageResult;
        Characterization = characterization;
    }
    public bool IsSuccess { get; }
    public Phase9CimOperationKind Operation { get; }
    public IReadOnlyList<double> Values { get; }
    public int LatencyCycles { get; }
    public double EnergyPicojoules { get; }
    public double Utilization { get; }
    public IReadOnlyList<Phase9CimRuntimeIssue> Issues { get; }
    public IReadOnlyList<string> TraceFacts { get; }
    public StorageMapResult? StorageResult { get; }
    public Phase9CimOperationCharacterization? Characterization { get; }
}

/// <summary>Stateful array runtime that executes allocation, write, read, calibration, and MVM operations.</summary>
public sealed class Phase9CimArrayRuntime
{
    private readonly Dictionary<Phase9CimOperationKind, Phase9CimOperationCharacterization> characterization;
    private readonly Dictionary<long, double> storedValues = [];
    private readonly Dictionary<string, TileShape> tileShapes = new(StringComparer.OrdinalIgnoreCase);
    private long calibrationVersion;

    public Phase9CimArrayRuntime(Phase9CimArrayLayout layout, IEnumerable<Phase9CimOperationCharacterization> operationCharacterization)
    {
        Layout = layout ?? throw new ArgumentNullException(nameof(layout));
        characterization = (operationCharacterization ?? []).ToDictionary(value => value.Operation);
        Storage = new StorageMap(layout.LayoutId, layout.CapacityBits);
        RuntimeHash = ComponentTemplateJson.StableHash(new
        {
            layout.LayoutHash,
            Characterization = characterization.Values.OrderBy(value => value.Operation).Select(value => new
            {
                value.Operation, value.BaseLatencyCycles, value.EnergyPicojoulesPerUnit, value.EnergyUnit,
                value.ParallelUnitsPerCycle, value.ProfileHash, SourceRecordIds = value.SourceRecordIds.OrderBy(item => item, StringComparer.Ordinal),
                value.ModelVersion, value.Formula, value.Uncertainty
            })
        });
    }

    public Phase9CimArrayLayout Layout { get; }
    public StorageMap Storage { get; }
    public string RuntimeHash { get; }
    public long CalibrationVersion => calibrationVersion;

    public StorageMapResult AllocateWeightTile(string tileId, int logicalRows, int logicalColumns, long? preferredAddressBits = null)
    {
        if (logicalRows <= 0 || logicalColumns <= 0) return StorageMapResult.Failure("StorageCapacityError", "Logical tile rows and columns must be positive.", Storage.StorageId, tileId, preferredAddressBits);
        long count;
        long bits;
        try
        {
            count = checked((long)logicalRows * logicalColumns);
            bits = checked(count * Layout.PhysicalBitsPerLogicalWeight);
        }
        catch (OverflowException)
        {
            return StorageMapResult.Failure("StorageCapacityError", "Logical tile capacity overflowed Int64.", Storage.StorageId, tileId, preferredAddressBits);
        }
        var allocation = Storage.Allocate(tileId, bits, Layout.PhysicalBitsPerLogicalWeight, preferredAddressBits);
        if (allocation.IsSuccess) tileShapes[tileId] = new(logicalRows, logicalColumns, allocation.AddressBits!.Value, count);
        return allocation;
    }

    public Phase9CimOperationResult Write(string tileId, long logicalWeightOffset, IReadOnlyList<double> values, string provenance)
    {
        if (!Layout.RuntimeWriteAllowed) return Failure(Phase9CimOperationKind.Write, "CimRuntimeWriteForbidden", "$.operation.write", "Runtime writes are disabled by the storage layout.");
        if (!TryCharacterization(Phase9CimOperationKind.Write, out var model, out var missing)) return missing!;
        if (!tileShapes.TryGetValue(tileId, out var shape)) return Failure(Phase9CimOperationKind.Write, "CimMissingAllocation", "$.tile_id", $"Weight tile '{tileId}' is not allocated.");
        if (values is null || values.Count == 0) return Failure(Phase9CimOperationKind.Write, "CimInvalidWrite", "$.values", "Write requires at least one logical weight.");
        if (logicalWeightOffset < 0 || logicalWeightOffset + values.Count > shape.WeightCount) return Failure(Phase9CimOperationKind.Write, "CimInvalidWrite", "$.logical_weight_offset", "Write exceeds the allocated logical tile range.");
        if (values.Any(value => !double.IsFinite(value))) return Failure(Phase9CimOperationKind.Write, "CimInvalidWrite", "$.values", "Weights must be finite.");
        var address = checked(shape.BaseAddressBits + logicalWeightOffset * Layout.PhysicalBitsPerLogicalWeight);
        var size = checked((long)values.Count * Layout.PhysicalBitsPerLogicalWeight);
        var storage = Storage.Write(address, size, provenance + ";layout_hash=" + Layout.LayoutHash);
        if (!storage.IsSuccess) return StorageFailure(Phase9CimOperationKind.Write, storage);
        for (var index = 0; index < values.Count; index++) storedValues[address + index * Layout.PhysicalBitsPerLogicalWeight] = values[index];
        return Success(Phase9CimOperationKind.Write, [], model, values.Count, shape.WeightCount, storage,
            $"write;tile={tileId};logical_offset={logicalWeightOffset};logical_weights={values.Count};physical_bits={size};cell_bits={Layout.CellBits};bit_slices={Layout.BitSliceCount}");
    }

    public Phase9CimOperationResult Read(string tileId, long logicalWeightOffset, int logicalWeightCount)
    {
        if (!TryCharacterization(Phase9CimOperationKind.Read, out var model, out var missing)) return missing!;
        if (!tileShapes.TryGetValue(tileId, out var shape)) return Failure(Phase9CimOperationKind.Read, "CimMissingAllocation", "$.tile_id", $"Weight tile '{tileId}' is not allocated.");
        if (logicalWeightOffset < 0 || logicalWeightCount <= 0 || logicalWeightOffset + logicalWeightCount > shape.WeightCount) return Failure(Phase9CimOperationKind.Read, "CimMissingAddress", "$.logical_weight_offset", "Read exceeds the allocated logical tile range.");
        var address = checked(shape.BaseAddressBits + logicalWeightOffset * Layout.PhysicalBitsPerLogicalWeight);
        var size = checked((long)logicalWeightCount * Layout.PhysicalBitsPerLogicalWeight);
        var storage = Storage.Read(address, size);
        if (!storage.IsSuccess) return StorageFailure(Phase9CimOperationKind.Read, storage);
        var values = new double[logicalWeightCount];
        for (var index = 0; index < logicalWeightCount; index++)
        {
            var valueAddress = address + index * Layout.PhysicalBitsPerLogicalWeight;
            if (!storedValues.TryGetValue(valueAddress, out values[index])) return Failure(Phase9CimOperationKind.Read, "CimMissingAddress", "$.storage", $"Logical weight at bit address {valueAddress} has not been written.", storage);
        }
        return Success(Phase9CimOperationKind.Read, values, model, logicalWeightCount, shape.WeightCount, storage,
            $"read;tile={tileId};logical_offset={logicalWeightOffset};logical_weights={logicalWeightCount};physical_bits={size}");
    }

    public Phase9CimOperationResult Mvm(string tileId, IReadOnlyList<double> activation, Phase9NonIdealRuntime? nonIdealRuntime = null)
    {
        if (!TryCharacterization(Phase9CimOperationKind.Mvm, out var model, out var missing)) return missing!;
        if (!tileShapes.TryGetValue(tileId, out var shape)) return Failure(Phase9CimOperationKind.Mvm, "CimMissingAllocation", "$.tile_id", $"Weight tile '{tileId}' is not allocated.");
        if (shape.Rows > Layout.Rows || shape.Columns > Layout.Columns * Layout.Banks) return Failure(Phase9CimOperationKind.Mvm, "CimCapacityOverflow", "$.tile_shape", "Tile shape exceeds physical rows/columns/banks.");
        if (activation is null || activation.Count != shape.Rows || activation.Any(value => !double.IsFinite(value))) return Failure(Phase9CimOperationKind.Mvm, "CimMvmShapeMismatch", "$.activation", $"Activation must contain exactly {shape.Rows} finite values.");
        var fullRead = Read(tileId, 0, checked((int)shape.WeightCount));
        if (!fullRead.IsSuccess) return Failure(Phase9CimOperationKind.Mvm, fullRead.Issues[0].Code, fullRead.Issues[0].Path, fullRead.Issues[0].Message, fullRead.StorageResult);
        var weights = fullRead.Values;
        var output = new double[shape.Columns];
        for (var row = 0; row < shape.Rows; row++)
        for (var column = 0; column < shape.Columns; column++)
            output[column] += activation[row] * weights[row * shape.Columns + column];
        if (nonIdealRuntime is not null)
        {
            var transformed = nonIdealRuntime.Apply(output, checked(shape.Rows * shape.Columns), calibrationVersion);
            if (!transformed.IsSuccess) return Failure(Phase9CimOperationKind.Mvm, transformed.Issues[0].Code, transformed.Issues[0].Path, transformed.Issues[0].Message);
            output = transformed.Values.ToArray();
        }
        return Success(Phase9CimOperationKind.Mvm, output, model, checked(shape.Rows * shape.Columns), Layout.LogicalWeightCapacity, null,
            $"mvm;tile={tileId};rows={shape.Rows};columns={shape.Columns};cell_bits={Layout.CellBits};bit_slices={Layout.BitSliceCount};calibration_version={calibrationVersion}");
    }

    public Phase9CimOperationResult Calibrate(string methodId)
    {
        if (!TryCharacterization(Phase9CimOperationKind.Calibration, out var model, out var missing)) return missing!;
        if (string.IsNullOrWhiteSpace(methodId)) return Failure(Phase9CimOperationKind.Calibration, "CimCalibrationMethodRequired", "$.method_id", "Calibration method id is required.");
        calibrationVersion++;
        return Success(Phase9CimOperationKind.Calibration, [], model, 1, 1, null, $"calibration;method={methodId};version={calibrationVersion}");
    }

    private bool TryCharacterization(Phase9CimOperationKind operation, out Phase9CimOperationCharacterization model, out Phase9CimOperationResult? failure)
    {
        if (characterization.TryGetValue(operation, out model!))
        {
            failure = null;
            return true;
        }
        failure = Failure(operation, "CimOperationCharacterizationUnknown", "$.operation", $"No latency/energy characterization is available for operation '{operation}'.");
        return false;
    }

    private static Phase9CimOperationResult Success(Phase9CimOperationKind operation, IEnumerable<double> values, Phase9CimOperationCharacterization model, long units, long capacityUnits, StorageMapResult? storage, params string[] facts)
    {
        var parallelism = Math.Max(1, model.ParallelUnitsPerCycle);
        var latency = checked(model.BaseLatencyCycles + (int)Math.Ceiling(units / (double)parallelism));
        var energy = model.EnergyPicojoulesPerUnit * units;
        var utilization = capacityUnits <= 0 ? 0 : Math.Min(1, units / (double)capacityUnits);
        var trace = facts.Concat(new[]
        {
            $"latency_cycles={latency};formula=base+ceil(units/parallel_units);base={model.BaseLatencyCycles};parallel_units={parallelism}",
            $"energy_pj={energy.ToString("R", CultureInfo.InvariantCulture)};units={units};energy_unit={model.EnergyUnit};profile_hash={model.ProfileHash};model_version={model.ModelVersion}"
        });
        return new(true, operation, values, latency, energy, utilization, [], trace, storage, model);
    }

    private static Phase9CimOperationResult Failure(Phase9CimOperationKind operation, string code, string path, string message, StorageMapResult? storage = null) =>
        new(false, operation, [], 0, 0, 0, [new(code, path, message)], [], storage, null);
    private static Phase9CimOperationResult StorageFailure(Phase9CimOperationKind operation, StorageMapResult storage) => Failure(operation,
        storage.Code == "MemoryAddressError" ? "CimMissingAddress" : storage.Code == "StorageCapacityError" ? "CimCapacityOverflow" : "CimStorageError",
        "$.storage", storage.Message, storage);
    private sealed record TileShape(int Rows, int Columns, long BaseAddressBits, long WeightCount);
}
