using System.Globalization;

namespace HardwareSim.Core;

#pragma warning disable CS1591

public sealed class ComponentTemplateResolvedParameters
{
    public ComponentTemplateResolvedParameters(Dictionary<string, string> values, Dictionary<string, string> defaults)
    {
        Values = values;
        Defaults = defaults;
    }

    public Dictionary<string, string> Values { get; }
    public Dictionary<string, string> Defaults { get; }
    public int ArrayRows { get; init; }
    public int ArrayCols { get; init; }
    public string InputDType { get; init; } = "fp8";
    public string WeightDType { get; init; } = "fp8";
    public string OutputDType { get; init; } = "fp8";
    public string AccumulateDType { get; init; } = "fp16";
    public int CellBits { get; init; }
    public int AdcBits { get; init; }
    public int DacBits { get; init; }
    public int InputQueueDepth { get; init; }
    public int OutputQueueDepth { get; init; }
    public int IssueInterval { get; init; }
    public int MacsPerCycle { get; init; }
    public int PipelineLatency { get; init; }
}

public sealed class ComponentTemplateParameterResolution
{
    public ComponentTemplateParameterResolution(ComponentTemplate template, ComponentTemplateResolvedParameters parameters, IReadOnlyList<ComponentTemplateIssue> issues)
    {
        Template = template;
        Parameters = parameters;
        Issues = issues;
    }

    public ComponentTemplate Template { get; }
    public ComponentTemplateResolvedParameters Parameters { get; }
    public IReadOnlyList<ComponentTemplateIssue> Issues { get; }
    public bool HasBlockingIssues => Issues.Any(i => i.Severity is ComponentTemplateIssueSeverity.Error or ComponentTemplateIssueSeverity.Fatal);
}

public sealed class ComponentTemplateParameterResolver
{
    public ComponentTemplateParameterResolution Resolve(ComponentTemplate template, IReadOnlyDictionary<string, string>? instanceOverrides = null)
    {
        var resolved = ComponentTemplateJson.Clone(template);
        var issues = new List<ComponentTemplateIssue>();
        var defaults = resolved.Parameters.ToDictionary(p => p.Name, p => p.DefaultValue, StringComparer.OrdinalIgnoreCase);
        var values = new Dictionary<string, string>(defaults, StringComparer.OrdinalIgnoreCase);
        foreach (var pair in (instanceOverrides ?? new Dictionary<string, string>()).OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            var schema = resolved.Parameters.FirstOrDefault(p => string.Equals(p.Name, pair.Key, StringComparison.OrdinalIgnoreCase));
            if (schema is null)
            {
                issues.Add(new("TemplateParameterUnknown", ComponentTemplateIssueSeverity.Error, "$.instance_overrides", $"Override parameter '{pair.Key}' is not declared by the template.", pair.Key));
                continue;
            }

            if (!ParameterValid(schema, pair.Value, out var message))
            {
                issues.Add(new("TemplateParameterInvalid", ComponentTemplateIssueSeverity.Error, $"$.instance_overrides.{pair.Key}", message, pair.Key));
                continue;
            }

            values[pair.Key] = pair.Value;
        }

        foreach (var required in resolved.Parameters.Where(p => p.Required && string.IsNullOrWhiteSpace(values.GetValueOrDefault(p.Name))))
        {
            issues.Add(new("TemplateParameterRequired", ComponentTemplateIssueSeverity.Error, $"$.parameters[{required.Name}]", $"Required template parameter '{required.Name}' has no default or override.", required.Name));
        }

        var rows = PositiveInt(values, "array_rows", DefaultRows(resolved), issues);
        var cols = PositiveInt(values, "array_cols", DefaultCols(resolved), issues);
        var inputDType = DType(values, "input_dtype", InputOperand(resolved)?.DType ?? "fp8");
        var weightDType = DType(values, "weight_dtype", StoredOperand(resolved)?.DType ?? inputDType);
        var outputDType = DType(values, "output_dtype", OutputOperand(resolved)?.DType ?? inputDType);
        var accumulateDType = DType(values, "accumulate_dtype", resolved.OperationContract.AccumulateDType);
        var cellBits = PositiveInt(values, "cell_bits", resolved.StorageLayouts.FirstOrDefault()?.CellBits ?? 1, issues);
        var adcBits = NonNegativeInt(values, "adc_bits", 0, issues);
        var dacBits = NonNegativeInt(values, "dac_bits", 0, issues);
        var inputQueue = PositiveInt(values, "input_queue_depth", Math.Max(1, resolved.TimingContract.InputQueueDepth), issues);
        var outputQueue = PositiveInt(values, "output_queue_depth", Math.Max(1, resolved.TimingContract.OutputQueueDepth), issues);
        var issueInterval = PositiveInt(values, "issue_interval_override", Math.Max(1, resolved.TimingContract.IssueInterval), issues);
        var macsPerCycle = PositiveInt(values, "macs_per_cycle", Math.Max(1, rows * cols), issues);
        var pipelineLatency = NonNegativeInt(values, "pipeline_latency", Math.Max(0, resolved.TimingContract.PipelineLatency), issues);

        var parameters = new ComponentTemplateResolvedParameters(values, defaults)
        {
            ArrayRows = rows,
            ArrayCols = cols,
            InputDType = inputDType,
            WeightDType = weightDType,
            OutputDType = outputDType,
            AccumulateDType = accumulateDType,
            CellBits = cellBits,
            AdcBits = adcBits,
            DacBits = dacBits,
            InputQueueDepth = inputQueue,
            OutputQueueDepth = outputQueue,
            IssueInterval = issueInterval,
            MacsPerCycle = macsPerCycle,
            PipelineLatency = pipelineLatency
        };

        if (resolved.TargetKind == ComponentTemplateTargetKind.ProcessingElement)
        {
            ApplyResolvedParameters(resolved, parameters);
        }
        return new ComponentTemplateParameterResolution(resolved, parameters, issues);
    }

    private static void ApplyResolvedParameters(ComponentTemplate template, ComponentTemplateResolvedParameters p)
    {
        var input = InputOperand(template);
        if (input is not null) { input.Shape = [1, p.ArrayRows]; input.DType = p.InputDType; }
        var weight = StoredOperand(template);
        if (weight is not null) { weight.Shape = [p.ArrayRows, p.ArrayCols]; weight.DType = p.WeightDType; }
        var output = OutputOperand(template);
        if (output is not null) { output.Shape = [1, p.ArrayCols]; output.DType = p.OutputDType; }
        template.OperationContract.MultiplyDType = string.Equals(p.InputDType, p.WeightDType, StringComparison.OrdinalIgnoreCase) ? p.InputDType : p.InputDType + "x" + p.WeightDType;
        template.OperationContract.AccumulateDType = p.AccumulateDType;
        template.OperationContract.OutputDType = p.OutputDType;

        foreach (var port in template.ExternalPorts)
        {
            if (port.Direction == PortDirection.Input && port.DataType != HardwareDataType.Config)
            {
                var isWeight = port.Name.Contains("weight", StringComparison.OrdinalIgnoreCase);
                port.Shape = isWeight ? [p.ArrayRows, p.ArrayCols] : [1, p.ArrayRows];
                port.Precision = PrecisionFromDType(isWeight ? p.WeightDType : p.InputDType);
                port.BandwidthBitsPerCycle = isWeight
                    ? Math.Max(1, p.ArrayRows * p.ArrayCols * ComponentTemplateValidator.DTypeBits(p.WeightDType))
                    : Math.Max(1, p.ArrayRows * ComponentTemplateValidator.DTypeBits(p.InputDType));
            }
            else if (port.Direction == PortDirection.Output)
            {
                port.Shape = [1, p.ArrayCols];
                port.Precision = PrecisionFromDType(p.OutputDType);
                port.BandwidthBitsPerCycle = Math.Max(1, p.ArrayCols * ComponentTemplateValidator.DTypeBits(p.OutputDType));
            }
        }

        foreach (var storage in template.StorageLayouts)
        {
            storage.CellBits = p.CellBits;
            storage.BitSlices = BuildBitSlices(ComponentTemplateValidator.DTypeBits(p.WeightDType), p.CellBits);
        }

        template.TimingContract.InputQueueDepth = p.InputQueueDepth;
        template.TimingContract.OutputQueueDepth = p.OutputQueueDepth;
        template.TimingContract.IssueInterval = p.IssueInterval;
        template.TimingContract.PipelineLatency = p.PipelineLatency;

        foreach (var block in template.InternalBlocks)
        {
            block.Parameters["array_rows"] = p.ArrayRows.ToString(CultureInfo.InvariantCulture);
            block.Parameters["array_cols"] = p.ArrayCols.ToString(CultureInfo.InvariantCulture);
            block.Parameters["macs_per_cycle"] = p.MacsPerCycle.ToString(CultureInfo.InvariantCulture);
            foreach (var port in block.Ports)
            {
                if (port.DataType == HardwareDataType.Config) continue;
                if (port.Name.Contains("weight", StringComparison.OrdinalIgnoreCase))
                {
                    port.Shape = [p.ArrayRows, p.ArrayCols];
                    port.Precision = PrecisionFromDType(p.WeightDType);
                    port.WidthBits = Math.Max(1, ComponentTemplateValidator.DTypeBits(p.WeightDType) * p.ArrayCols);
                }
                else if (port.Name.Contains("result", StringComparison.OrdinalIgnoreCase) || port.Name.Contains("psum", StringComparison.OrdinalIgnoreCase) || port.Direction == PortDirection.Output)
                {
                    port.Shape = [1, p.ArrayCols];
                    port.Precision = PrecisionFromDType(p.OutputDType);
                    port.WidthBits = Math.Max(1, ComponentTemplateValidator.DTypeBits(p.OutputDType) * p.ArrayCols);
                }
                else
                {
                    port.Shape = [1, p.ArrayRows];
                    port.Precision = PrecisionFromDType(p.InputDType);
                    port.WidthBits = Math.Max(1, ComponentTemplateValidator.DTypeBits(p.InputDType) * p.ArrayRows);
                }
            }
        }
    }

    private static List<StorageBitSlice> BuildBitSlices(int dtypeBits, int cellBits)
    {
        var slices = new List<StorageBitSlice>();
        var safeCellBits = Math.Max(1, cellBits);
        for (var bit = 0; bit < dtypeBits; bit += safeCellBits)
        {
            slices.Add(new StorageBitSlice { LogicalBitStart = bit, BitCount = Math.Min(safeCellBits, dtypeBits - bit), CellBitStart = 0 });
        }
        return slices;
    }

    private static TemplateOperandContract? InputOperand(ComponentTemplate template) => template.OperationContract.InputOperands.FirstOrDefault();
    private static TemplateOperandContract? StoredOperand(ComponentTemplate template) => template.OperationContract.StoredOperands.FirstOrDefault();
    private static TemplateOperandContract? OutputOperand(ComponentTemplate template) => template.OperationContract.OutputOperands.FirstOrDefault();
    private static int DefaultRows(ComponentTemplate template) => Math.Max(1, InputOperand(template)?.Shape.Skip(1).FirstOrDefault() ?? StoredOperand(template)?.Shape.FirstOrDefault() ?? 1);
    private static int DefaultCols(ComponentTemplate template) => Math.Max(1, OutputOperand(template)?.Shape.Skip(1).FirstOrDefault() ?? StoredOperand(template)?.Shape.Skip(1).FirstOrDefault() ?? 1);
    private static string DType(IReadOnlyDictionary<string, string> values, string key, string fallback) => values.TryGetValue(key, out var raw) && !string.IsNullOrWhiteSpace(raw) ? raw.Trim().ToLowerInvariant() : fallback.Trim().ToLowerInvariant();

    private static int PositiveInt(IReadOnlyDictionary<string, string> values, string key, int fallback, List<ComponentTemplateIssue> issues)
    {
        if (!values.TryGetValue(key, out var raw) || string.IsNullOrWhiteSpace(raw)) return Math.Max(1, fallback);
        if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) && value > 0) return value;
        issues.Add(new("TemplateParameterInvalid", ComponentTemplateIssueSeverity.Error, "$.parameters[" + key + "]", $"Parameter '{key}' must be a positive integer.", key));
        return Math.Max(1, fallback);
    }

    private static int NonNegativeInt(IReadOnlyDictionary<string, string> values, string key, int fallback, List<ComponentTemplateIssue> issues)
    {
        if (!values.TryGetValue(key, out var raw) || string.IsNullOrWhiteSpace(raw)) return Math.Max(0, fallback);
        if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) && value >= 0) return value;
        issues.Add(new("TemplateParameterInvalid", ComponentTemplateIssueSeverity.Error, "$.parameters[" + key + "]", $"Parameter '{key}' must be a non-negative integer.", key));
        return Math.Max(0, fallback);
    }

    private static bool ParameterValid(TemplateParameter schema, string value, out string message)
    {
        message = "";
        if (schema.ValueKind == TemplateParameterValueKind.Enum && !schema.AllowedValues.Any(v => string.Equals(v, value, StringComparison.OrdinalIgnoreCase))) { message = $"Parameter '{schema.Name}' value '{value}' is not allowed."; return false; }
        if (schema.ValueKind is TemplateParameterValueKind.Integer or TemplateParameterValueKind.Number)
        {
            if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) || double.IsNaN(parsed) || double.IsInfinity(parsed)) { message = $"Parameter '{schema.Name}' must be numeric."; return false; }
            if (schema.ValueKind == TemplateParameterValueKind.Integer && Math.Abs(parsed - Math.Round(parsed)) > 0) { message = $"Parameter '{schema.Name}' must be an integer."; return false; }
            if ((schema.Minimum.HasValue && parsed < schema.Minimum.Value) || (schema.Maximum.HasValue && parsed > schema.Maximum.Value)) { message = $"Parameter '{schema.Name}' is outside its valid range."; return false; }
        }
        if (schema.ValueKind == TemplateParameterValueKind.Boolean && !bool.TryParse(value, out _)) { message = $"Parameter '{schema.Name}' must be boolean."; return false; }
        return true;
    }

    private static PrecisionKind PrecisionFromDType(string dtype)
    {
        var normalized = (dtype ?? "").Trim().ToLowerInvariant();
        return normalized switch
        {
            "fp8" or "fp8e4m3" => PrecisionKind.FP8_E4M3,
            "fp16" or "bf16" => PrecisionKind.FP16,
            "fp32" => PrecisionKind.FP32,
            _ => PrecisionKind.Any
        };
    }
}

public sealed class ComponentTemplateDerivedProfile
{
    public Dictionary<string, string> Metrics { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, long> Capacity { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, double> EnergyPicojoules { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, double> AreaUm2 { get; } = new(StringComparer.OrdinalIgnoreCase);
    public int OperationLatency { get; init; }
    public int PipelineLatency { get; init; }
    public int IssueInterval { get; init; }
    public int InputQueueDepth { get; init; }
    public int OutputQueueDepth { get; init; }
}

public static class ComponentTemplateEnergyModels
{
    public const string SharedOutputStageModelId = "component-template-output-bit-v1";
    public const double SharedOutputStagePicojoulesPerBit = 0.0005;

    public static double SharedOutputStageEnergyPicojoules(long outputBits)
    {
        if (outputBits < 0) throw new ArgumentOutOfRangeException(nameof(outputBits));
        return outputBits * SharedOutputStagePicojoulesPerBit;
    }
}

public sealed class ComponentTemplateDerivedProfileCalculator
{
    public ComponentTemplateDerivedProfile Calculate(ComponentTemplate template, ComponentTemplateResolvedParameters p, IReadOnlyList<CharacterizedProfileSnapshot> snapshots, List<ComponentTemplateIssue> issues)
    {
        if (template.TargetKind != ComponentTemplateTargetKind.ProcessingElement)
        {
            return CalculateGeneric(template, p);
        }

        var storage = template.StorageLayouts.FirstOrDefault(layout => string.Equals(layout.LogicalName, "weight", StringComparison.OrdinalIgnoreCase)) ?? template.StorageLayouts.FirstOrDefault();
        var storageCapacityBits = storage?.CapacityBits ?? 0;
        var inputBits = ComponentTemplateValidator.DTypeBits(p.InputDType);
        var weightBits = ComponentTemplateValidator.DTypeBits(p.WeightDType);
        var outputBitsPerValue = ComponentTemplateValidator.DTypeBits(p.OutputDType);
        var accumulateBits = ComponentTemplateValidator.DTypeBits(p.AccumulateDType);
        var activationValues = p.ArrayRows;
        var weightValues = p.ArrayRows * p.ArrayCols;
        var outputValues = p.ArrayCols;
        var activationBits = activationValues * inputBits;
        var weightRequiredBits = weightValues * weightBits;
        var outputBits = outputValues * outputBitsPerValue;
        var maxWeightValues = weightBits <= 0 ? 0 : storageCapacityBits / weightBits;
        var maxWeightMatrices = weightRequiredBits <= 0 ? 0 : storageCapacityBits / weightRequiredBits;
        var storageUtilization = storageCapacityBits <= 0 ? 0 : weightRequiredBits / (double)storageCapacityBits;
        var macCount = weightValues;
        var macsPerCycle = Math.Max(1, p.MacsPerCycle);
        var computeCycles = (int)Math.Max(1, Math.Ceiling(macCount / (double)macsPerCycle));
        var baselineRows = PositiveDefault(p.Defaults, "array_rows", p.ArrayRows);
        var baselineCols = PositiveDefault(p.Defaults, "array_cols", p.ArrayCols);
        var baselineMacs = Math.Max(1, baselineRows * baselineCols);
        var baselineMacsPerCycle = PositiveDefault(p.Defaults, "macs_per_cycle", baselineMacs);
        var baselineComputeCycles = (int)Math.Max(1, Math.Ceiling(baselineMacs / (double)baselineMacsPerCycle));
        var baselinePipeline = NonNegativeDefault(p.Defaults, "pipeline_latency", template.TimingContract.PipelineLatency);
        var baselineAdc = NonNegativeDefault(p.Defaults, "adc_bits", 0);
        var baselineDac = NonNegativeDefault(p.Defaults, "dac_bits", 0);
        var adcLatency = PrecisionLatencyCycles(p.AdcBits, true);
        var dacLatency = PrecisionLatencyCycles(p.DacBits, false);
        var baselineAdcLatency = PrecisionLatencyCycles(baselineAdc, true);
        var baselineDacLatency = PrecisionLatencyCycles(baselineDac, false);
        var operationLatency = Math.Max(1, template.TimingContract.OperationLatency + (computeCycles - baselineComputeCycles) + (p.PipelineLatency - baselinePipeline) + (adcLatency - baselineAdcLatency) + (dacLatency - baselineDacLatency));
        var reramLike = storage?.Encoding.Contains("reram", StringComparison.OrdinalIgnoreCase) == true || template.TemplateId.Contains("reram", StringComparison.OrdinalIgnoreCase);
        var cellsPerWeight = Math.Max(1, (int)Math.Ceiling(weightBits / (double)Math.Max(1, p.CellBits)));
        var computeEnergy = macCount * ComputeEnergyPerMac(p.InputDType, p.WeightDType, p.AccumulateDType);
        var weightReadEnergy = reramLike ? weightValues * cellsPerWeight * ReRamReadEnergyPerCell(p.CellBits) : weightRequiredBits * 0.002;
        var activationReadEnergy = activationBits * 0.001;
        var readEnergy = weightReadEnergy + activationReadEnergy;
        var writeEnergy = outputBits * 0.004;
        var weightWriteEnergy = weightRequiredBits * 0.004;
        var adcEnergy = outputValues * PrecisionEnergy(p.AdcBits, true);
        var dacEnergy = activationValues * PrecisionEnergy(p.DacBits, false);
        var controllerEnergy = 0.2;
        var accumulatorEnergy = outputValues * 0.01;
        var egressEnergy = ComponentTemplateEnergyModels.SharedOutputStageEnergyPicojoules(outputBits);
        var declaredStructuralBlocks = template.InternalBlocks
            .Where(block => block.Layer == InternalBlockLayer.Structural)
            .OrderBy(block => block.Id, StringComparer.Ordinal)
            .ToList();
        var declaredStructuralEnergy = declaredStructuralBlocks.Sum(block => block.EnergyPicojoules);
        var declaredStructuralArea = declaredStructuralBlocks.Sum(block => block.AreaUm2);
        var totalDynamic = computeEnergy + readEnergy + writeEnergy + adcEnergy + dacEnergy + controllerEnergy + accumulatorEnergy + egressEnergy + declaredStructuralEnergy;
        var physicalCells = storage is null ? 0 : Math.Max(0, storage.Banks) * Math.Max(0, storage.Rows - storage.ReservedRows) * Math.Max(0, storage.Columns - storage.ReservedColumns);
        var storageArea = reramLike ? physicalCells * 0.02 : 1400;

        var profile = new ComponentTemplateDerivedProfile
        {
            OperationLatency = operationLatency,
            PipelineLatency = p.PipelineLatency,
            IssueInterval = p.IssueInterval,
            InputQueueDepth = p.InputQueueDepth,
            OutputQueueDepth = p.OutputQueueDepth
        };

        profile.Capacity[storage?.Id ?? "weight_store_0"] = storageCapacityBits;
        profile.EnergyPicojoules["compute_dynamic_energy"] = computeEnergy;
        profile.EnergyPicojoules["read_energy"] = readEnergy;
        profile.EnergyPicojoules["write_energy"] = writeEnergy;
        profile.EnergyPicojoules["adc_energy"] = adcEnergy;
        profile.EnergyPicojoules["dac_energy"] = dacEnergy;
        profile.EnergyPicojoules["controller_energy"] = controllerEnergy;
        profile.EnergyPicojoules["buffer_energy"] = accumulatorEnergy + egressEnergy;
        profile.EnergyPicojoules["leakage_energy_per_cycle"] = 0;
        foreach (var block in declaredStructuralBlocks.Where(block => block.EnergyPicojoules > 0))
        {
            profile.EnergyPicojoules["declared_block:" + block.Id] = block.EnergyPicojoules;
        }
        profile.AreaUm2["storage_area"] = storageArea;
        profile.AreaUm2["compute_area"] = reramLike ? 6000 : 5000;
        profile.AreaUm2["adc_area"] = PrecisionArea(p.AdcBits, true);
        profile.AreaUm2["dac_area"] = PrecisionArea(p.DacBits, false);
        profile.AreaUm2["controller_area"] = 600;
        profile.AreaUm2["buffer_accumulator_egress_area"] = 1400;
        foreach (var block in declaredStructuralBlocks.Where(block => block.AreaUm2 > 0))
        {
            profile.AreaUm2["declared_block:" + block.Id] = block.AreaUm2;
        }

        Add(profile, "array_rows", p.ArrayRows);
        Add(profile, "array_cols", p.ArrayCols);
        Add(profile, "input_dtype", p.InputDType);
        Add(profile, "weight_dtype", p.WeightDType);
        Add(profile, "output_dtype", p.OutputDType);
        Add(profile, "accumulate_dtype", p.AccumulateDType);
        Add(profile, "input_dtype_bits", inputBits);
        Add(profile, "weight_dtype_bits", weightBits);
        Add(profile, "output_dtype_bits", outputBitsPerValue);
        Add(profile, "accumulate_dtype_bits", accumulateBits);
        Add(profile, "activation_values", activationValues);
        Add(profile, "weight_values", weightValues);
        Add(profile, "output_values", outputValues);
        Add(profile, "activation_bits", activationBits);
        Add(profile, "weight_required_bits", weightRequiredBits);
        Add(profile, "output_bits", outputBits);
        Add(profile, "storage_capacity_bits", storageCapacityBits);
        Add(profile, "max_weight_values", maxWeightValues);
        Add(profile, "max_weight_matrices", maxWeightMatrices);
        Add(profile, "storage_utilization", storageUtilization);
        Add(profile, "mac_count", macCount);
        Add(profile, "macs_per_cycle", macsPerCycle);
        Add(profile, "compute_cycles", computeCycles);
        Add(profile, "compute_energy_pj", computeEnergy);
        Add(profile, "read_energy_pj", readEnergy);
        Add(profile, "write_energy_pj", writeEnergy);
        Add(profile, "weight_write_energy_pj", weightWriteEnergy);
        Add(profile, "adc_energy_pj", adcEnergy);
        Add(profile, "dac_energy_pj", dacEnergy);
        Add(profile, "controller_energy_pj", controllerEnergy);
        Add(profile, "accumulator_energy_pj", accumulatorEnergy);
        Add(profile, "declared_structural_energy_pj", declaredStructuralEnergy);
        Add(profile, "declared_structural_area_um2", declaredStructuralArea);
        foreach (var block in declaredStructuralBlocks)
        {
            Add(profile, "declared_block_energy_pj." + block.Id, block.EnergyPicojoules);
            Add(profile, "declared_block_area_um2." + block.Id, block.AreaUm2);
        }
        Add(profile, "total_dynamic_energy_pj", totalDynamic);
        Add(profile, "total_area_um2", profile.AreaUm2.Values.Sum());
        Add(profile, "operation_latency", operationLatency);
        Add(profile, "pipeline_latency", p.PipelineLatency);
        Add(profile, "issue_interval", p.IssueInterval);
        Add(profile, "input_queue_depth", p.InputQueueDepth);
        Add(profile, "output_queue_depth", p.OutputQueueDepth);
        Add(profile, "cells_per_weight", cellsPerWeight);
        Add(profile, "packing_efficiency", weightBits / (double)(cellsPerWeight * Math.Max(1, p.CellBits)));
        Add(profile, "adc_latency_cycles", adcLatency);
        Add(profile, "dac_latency_cycles", dacLatency);
        var literatureExactPoint = template.Provenance.DependencyHashes.TryGetValue("phase9_literature_exact_point", out var exactPoint) &&
            string.Equals(exactPoint, "true", StringComparison.OrdinalIgnoreCase);
        Add(profile, "synthetic_profile_only", literatureExactPoint ? "false" : "true");

        if (storageCapacityBits > 0 && weightRequiredBits > storageCapacityBits)
        {
            issues.Add(new("TemplateStorageCapacityExceeded", ComponentTemplateIssueSeverity.Error, "$.storage_layouts[" + (storage?.Id ?? "weight_store_0") + "]", $"Resolved weight operand requires {weightRequiredBits} bits but storage capacity is {storageCapacityBits} bits.", storage?.Id ?? "weight_store_0"));
        }

        return profile;
    }

    private static ComponentTemplateDerivedProfile CalculateGeneric(ComponentTemplate template, ComponentTemplateResolvedParameters parameters)
    {
        var profile = new ComponentTemplateDerivedProfile
        {
            OperationLatency = Math.Max(1, template.TimingContract.OperationLatency),
            PipelineLatency = Math.Max(0, template.TimingContract.PipelineLatency),
            IssueInterval = Math.Max(1, template.TimingContract.IssueInterval),
            InputQueueDepth = Math.Max(1, template.TimingContract.InputQueueDepth),
            OutputQueueDepth = Math.Max(1, template.TimingContract.OutputQueueDepth)
        };

        foreach (var storage in template.StorageLayouts.OrderBy(item => item.Id, StringComparer.Ordinal))
        {
            profile.Capacity[storage.Id] = storage.CapacityBits;
        }
        foreach (var block in template.InternalBlocks.OrderBy(item => item.Id, StringComparer.Ordinal))
        {
            if (block.EnergyPicojoules > 0) profile.EnergyPicojoules["block:" + block.Id] = block.EnergyPicojoules;
            if (block.AreaUm2 > 0) profile.AreaUm2["block:" + block.Id] = block.AreaUm2;
        }

        Add(profile, "operation_latency", profile.OperationLatency);
        Add(profile, "pipeline_latency", profile.PipelineLatency);
        Add(profile, "issue_interval", profile.IssueInterval);
        Add(profile, "input_queue_depth", profile.InputQueueDepth);
        Add(profile, "output_queue_depth", profile.OutputQueueDepth);
        Add(profile, "resolved_parameter_count", parameters.Values.Count);
        return profile;
    }

    private static void Add(ComponentTemplateDerivedProfile profile, string key, int value) => profile.Metrics[key] = value.ToString(CultureInfo.InvariantCulture);
    private static void Add(ComponentTemplateDerivedProfile profile, string key, long value) => profile.Metrics[key] = value.ToString(CultureInfo.InvariantCulture);
    private static void Add(ComponentTemplateDerivedProfile profile, string key, double value) => profile.Metrics[key] = value.ToString("0.######", CultureInfo.InvariantCulture);
    private static void Add(ComponentTemplateDerivedProfile profile, string key, string value) => profile.Metrics[key] = value;
    private static int PositiveDefault(IReadOnlyDictionary<string, string> defaults, string key, int fallback) => defaults.TryGetValue(key, out var raw) && int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) && value > 0 ? value : Math.Max(1, fallback);
    private static int NonNegativeDefault(IReadOnlyDictionary<string, string> defaults, string key, int fallback) => defaults.TryGetValue(key, out var raw) && int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) && value >= 0 ? value : Math.Max(0, fallback);

    private static double ComputeEnergyPerMac(string input, string weight, string accumulate)
    {
        if (input.Equals("fp8", StringComparison.OrdinalIgnoreCase) && weight.Equals("fp8", StringComparison.OrdinalIgnoreCase) && accumulate.Equals("fp16", StringComparison.OrdinalIgnoreCase)) return 0.002;
        if (input.Equals("fp16", StringComparison.OrdinalIgnoreCase) && weight.Equals("fp16", StringComparison.OrdinalIgnoreCase) && accumulate.Equals("fp32", StringComparison.OrdinalIgnoreCase)) return 0.008;
        var inputBits = ComponentTemplateValidator.DTypeBits(input);
        var weightBits = ComponentTemplateValidator.DTypeBits(weight);
        var accumulateBits = ComponentTemplateValidator.DTypeBits(accumulate);
        return 0.002 * Math.Max(1, inputBits / 8.0) * Math.Max(1, weightBits / 8.0) * Math.Max(1, accumulateBits / 16.0);
    }

    private static double ReRamReadEnergyPerCell(int cellBits) => cellBits switch { <= 1 => 0.003, 2 => 0.004, 3 or 4 => 0.006, _ => 0.006 * cellBits / 4.0 };
    private static double PrecisionEnergy(int bits, bool adc) => bits <= 0 ? 0 : Interpolate(bits, adc ? new (int Bits, double Value)[] { (4, 0.2), (6, 0.45), (8, 1.0) } : new (int Bits, double Value)[] { (4, 0.1), (6, 0.25), (8, 0.6) });
    private static int PrecisionLatencyCycles(int bits, bool adc) => bits <= 0 ? 0 : (int)Math.Ceiling(Interpolate(bits, adc ? new (int Bits, double Value)[] { (4, 500.0), (6, 800.0), (8, 1200.0) } : new (int Bits, double Value)[] { (4, 300.0), (6, 500.0), (8, 800.0) }) / 1000.0);
    private static double PrecisionArea(int bits, bool adc) => bits <= 0 ? 0 : Interpolate(bits, adc ? new (int Bits, double Value)[] { (4, 200.0), (6, 350.0), (8, 600.0) } : new (int Bits, double Value)[] { (4, 150.0), (6, 280.0), (8, 500.0) });

    private static double Interpolate(int bits, (int Bits, double Value)[] table)
    {
        var ordered = table.OrderBy(item => item.Bits).ToArray();
        if (bits <= ordered[0].Bits) return ordered[0].Value * bits / ordered[0].Bits;
        for (var i = 1; i < ordered.Length; i++)
        {
            if (bits <= ordered[i].Bits)
            {
                var previous = ordered[i - 1];
                var current = ordered[i];
                var ratio = (bits - previous.Bits) / (double)(current.Bits - previous.Bits);
                return previous.Value + (current.Value - previous.Value) * ratio;
            }
        }

        var last = ordered[^1];
        return last.Value * bits / last.Bits;
    }
}

public sealed class ComponentTemplateSemanticAnalyzer
{
    public ComponentTemplateValidationResult Analyze(ComponentTemplate template)
    {
        var result = new ComponentTemplateValidationResult();
        if (template.TargetKind != ComponentTemplateTargetKind.ProcessingElement) return result;

        RequirePath(template, result, InternalBlockLayer.Dataflow, ["ingress", "inputbuffer", "computecore", "accumulator", "egress"], "TemplatePeDataflowPathMissing", "$.internal_blocks[dataflow]");
        RequirePath(template, result, InternalBlockLayer.Dataflow, ["weightstore", "computecore"], "TemplatePeWeightDataflowPathMissing", "$.internal_connections");
        RequirePath(template, result, InternalBlockLayer.Structural, ["weightstore", "computecore"], "TemplatePeWeightPathMissing", "$.internal_connections");
        RequirePath(template, result, InternalBlockLayer.Structural, ["computecore", "accumulator", "egress"], "TemplatePeResultPathMissing", "$.internal_connections");

        if (!template.ExternalPorts.Any(port => port.Direction == PortDirection.Input && port.DataType != HardwareDataType.Config && Role(template, port.ShellBlockId) == "ingress"))
        {
            result.Add(Error("TemplatePeIngressShellMissing", "$.external_ports", "ProcessingElement template requires an activation input shell bound to ingress."));
        }

        if (!template.ExternalPorts.Any(port => port.Direction == PortDirection.Input && port.DataType != HardwareDataType.Config && Role(template, port.ShellBlockId) == "weightstore"))
        {
            result.Add(Error("TemplatePeWeightShellMissing", "$.external_ports", "ProcessingElement template requires a weight input shell bound to WeightStore."));
        }

        if (!template.ExternalPorts.Any(port => port.Direction == PortDirection.Output && Role(template, port.ShellBlockId) == "egress"))
        {
            result.Add(Error("TemplatePeEgressShellMissing", "$.external_ports", "ProcessingElement template requires an output shell bound to egress."));
        }

        ValidatePeRoleEdges(template, result);
        ValidatePeControlBindings(template, result);
        ValidatePeControlTopology(template, result);
        ValidateAnalogConversionTopology(template, result);
        return result;
    }

    private static void ValidatePeRoleEdges(ComponentTemplate template, ComponentTemplateValidationResult result)
    {
        var allowedData = new HashSet<string>(StringComparer.Ordinal)
        {
            "ingress->inputbuffer",
            "inputbuffer->computecore",
            "inputbuffer->dac",
            "weightstore->computecore",
            "dac->computecore",
            "computecore->adc",
            "adc->accumulator",
            "computecore->accumulator",
            "accumulator->egress"
        };
        var allowedControl = new HashSet<string>(StringComparer.Ordinal)
        {
            "controller->ingress",
            "controller->inputbuffer",
            "controller->weightstore",
            "controller->computecore",
            "controller->accumulator",
            "controller->egress",
            "controller->decoder",
            "controller->dac",
            "controller->adc",
            "decoder->computecore"
        };
        var blocks = template.InternalBlocks.ToDictionary(block => block.Id, StringComparer.OrdinalIgnoreCase);
        foreach (var connection in template.InternalConnections)
        {
            if (!blocks.TryGetValue(connection.SourceBlockId, out var source) ||
                !blocks.TryGetValue(connection.TargetBlockId, out var target) ||
                source.Layer != target.Layer)
            {
                continue;
            }

            var sourceRole = Role(source);
            var targetRole = Role(target);
            if (string.IsNullOrWhiteSpace(sourceRole) || string.IsNullOrWhiteSpace(targetRole)) continue;

            var edge = sourceRole + "->" + targetRole;
            var control = IsControlConnection(template, connection);
            if (!(control ? allowedControl : allowedData).Contains(edge))
            {
                result.Add(Error(
                    "TemplatePeIllegalSemanticEdge",
                    "$.internal_connections[" + connection.Id + "]",
                    $"ProcessingElement {(control ? "control" : "data")} edge '{edge}' is not permitted by the PE semantic topology.",
                    connection.Id));
            }
        }
    }

    private static void ValidatePeControlBindings(ComponentTemplate template, ComponentTemplateValidationResult result)
    {
        foreach (var block in template.InternalBlocks.Where(block => block.Layer == InternalBlockLayer.Structural && !block.Abstract))
        {
            foreach (var port in block.Ports.Where(port => port.SignalType == SignalType.Control))
            {
                if (port.Direction is PortDirection.Input or PortDirection.Bidirectional)
                {
                    var shellBound = template.ExternalPorts.Any(external =>
                        external.Direction is PortDirection.Input or PortDirection.Bidirectional &&
                        external.SignalType == SignalType.Control &&
                        string.Equals(external.ShellBlockId, block.Id, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(external.ShellPortName, port.Name, StringComparison.OrdinalIgnoreCase));
                    var internallyBound = template.InternalConnections.Any(connection =>
                        string.Equals(connection.TargetBlockId, block.Id, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(connection.TargetPortName, port.Name, StringComparison.OrdinalIgnoreCase) &&
                        IsControlConnection(template, connection));
                    if (!shellBound && !internallyBound)
                    {
                        result.Add(Error(
                            "TemplatePeControlInputUnbound",
                            $"$.internal_blocks[{block.Id}].ports[{port.Name}]",
                            $"Control input '{block.Id}.{port.Name}' must bind to the external control shell or an internal control connection.",
                            block.Id + "." + port.Name));
                    }
                }

                if (port.Direction is PortDirection.Output or PortDirection.Bidirectional)
                {
                    var shellBound = template.ExternalPorts.Any(external =>
                        external.Direction is PortDirection.Output or PortDirection.Bidirectional &&
                        external.SignalType == SignalType.Control &&
                        string.Equals(external.ShellBlockId, block.Id, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(external.ShellPortName, port.Name, StringComparison.OrdinalIgnoreCase));
                    var internallyBound = template.InternalConnections.Any(connection =>
                        string.Equals(connection.SourceBlockId, block.Id, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(connection.SourcePortName, port.Name, StringComparison.OrdinalIgnoreCase) &&
                        IsControlConnection(template, connection));
                    if (!shellBound && !internallyBound)
                    {
                        result.Add(Error(
                            "TemplatePeControlOutputUnbound",
                            $"$.internal_blocks[{block.Id}].ports[{port.Name}]",
                            $"Control output '{block.Id}.{port.Name}' must drive an external control shell or an internal control connection.",
                            block.Id + "." + port.Name));
                    }
                }
            }
        }
    }

    private static void ValidatePeControlTopology(ComponentTemplate template, ComponentTemplateValidationResult result)
    {
        var controllers = BlocksForRole(template, InternalBlockLayer.Structural, "controller");
        if (controllers.Count == 0)
        {
            result.Add(Error("TemplatePeControllerMissing", "$.internal_blocks[structural]", "ProcessingElement requires a structural Controller."));
            return;
        }

        if (!template.ExternalPorts.Any(port =>
                port.Direction is PortDirection.Input or PortDirection.Bidirectional &&
                port.SignalType == SignalType.Control &&
                Role(template, port.ShellBlockId) == "controller"))
        {
            result.Add(Error("TemplatePeControllerShellMissing", "$.external_ports", "ProcessingElement requires an external control shell bound to Controller."));
        }

        var requiredTargets = new List<string> { "ingress", "inputbuffer", "weightstore", "computecore", "accumulator", "egress" };
        if (RequiresAnalogConversion(template)) requiredTargets.AddRange(["decoder", "dac", "adc"]);
        foreach (var role in requiredTargets)
        {
            var targets = BlocksForRole(template, InternalBlockLayer.Structural, role);
            if (targets.Count == 0 || !Reachable(template, controllers, targets, InternalBlockLayer.Structural, controlPlane: true))
            {
                result.Add(Error(
                    "TemplatePeControlPathMissing",
                    "$.internal_connections",
                    $"ProcessingElement Controller requires a reachable control path to {role}.",
                    "controller->" + role));
            }
        }
    }

    private static void ValidateAnalogConversionTopology(ComponentTemplate template, ComponentTemplateValidationResult result)
    {
        if (!RequiresAnalogConversion(template)) return;

        RequirePath(template, result, InternalBlockLayer.Structural, ["inputbuffer", "dac", "computecore"], "TemplatePeDacPathMissing", "$.internal_connections");
        RequirePath(template, result, InternalBlockLayer.Structural, ["computecore", "adc", "accumulator"], "TemplatePeAdcPathMissing", "$.internal_connections");
        RequireControlPath(template, result, "controller", "decoder", "TemplatePeDecoderControlPathMissing");
        RequireControlPath(template, result, "decoder", "computecore", "TemplatePeArrayControlPathMissing");

        var blocks = template.InternalBlocks.ToDictionary(block => block.Id, StringComparer.OrdinalIgnoreCase);
        foreach (var connection in template.InternalConnections)
        {
            if (!blocks.TryGetValue(connection.SourceBlockId, out var source) ||
                !blocks.TryGetValue(connection.TargetBlockId, out var target) ||
                source.Layer != InternalBlockLayer.Structural ||
                target.Layer != InternalBlockLayer.Structural ||
                IsControlConnection(template, connection))
            {
                continue;
            }

            var edge = Role(source) + "->" + Role(target);
            if (edge is "inputbuffer->computecore" or "computecore->accumulator")
            {
                result.Add(Error(
                    "TemplatePeAnalogConversionBypass",
                    "$.internal_connections[" + connection.Id + "]",
                    $"Analog-array PE data edge '{edge}' bypasses a required DAC/ADC conversion stage.",
                    connection.Id));
            }
        }
    }

    private static void RequirePath(ComponentTemplate template, ComponentTemplateValidationResult result, InternalBlockLayer layer, IReadOnlyList<string> roles, string code, string location)
    {
        for (var i = 0; i < roles.Count - 1; i++)
        {
            var source = BlocksForRole(template, layer, roles[i]);
            var target = BlocksForRole(template, layer, roles[i + 1]);
            if (source.Count == 0 || target.Count == 0 || !Reachable(template, source, target, layer, controlPlane: false))
            {
                result.Add(Error(code, location, $"ProcessingElement requires reachable {roles[i]} -> {roles[i + 1]} data path.", roles[i] + "->" + roles[i + 1]));
            }
        }
    }

    private static void RequireControlPath(ComponentTemplate template, ComponentTemplateValidationResult result, string sourceRole, string targetRole, string code)
    {
        var source = BlocksForRole(template, InternalBlockLayer.Structural, sourceRole);
        var target = BlocksForRole(template, InternalBlockLayer.Structural, targetRole);
        if (source.Count == 0 || target.Count == 0 || !Reachable(template, source, target, InternalBlockLayer.Structural, controlPlane: true))
        {
            result.Add(Error(code, "$.internal_connections", $"ProcessingElement requires reachable {sourceRole} -> {targetRole} control path.", sourceRole + "->" + targetRole));
        }
    }

    private static bool Reachable(
        ComponentTemplate template,
        IReadOnlyCollection<string> sourceIds,
        IReadOnlyCollection<string> targetIds,
        InternalBlockLayer layer,
        bool controlPlane)
    {
        var layerBlocks = template.InternalBlocks.Where(block => block.Layer == layer).Select(block => block.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var edges = template.InternalConnections
            .Where(connection =>
                layerBlocks.Contains(connection.SourceBlockId) &&
                layerBlocks.Contains(connection.TargetBlockId) &&
                IsControlConnection(template, connection) == controlPlane)
            .GroupBy(connection => connection.SourceBlockId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Select(connection => connection.TargetBlockId).Distinct(StringComparer.OrdinalIgnoreCase).ToList(), StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<string>(sourceIds);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (!visited.Add(current)) continue;
            if (targetIds.Contains(current, StringComparer.OrdinalIgnoreCase)) return true;
            if (!edges.TryGetValue(current, out var next)) continue;
            foreach (var id in next) queue.Enqueue(id);
        }
        return false;
    }

    private static bool IsControlConnection(ComponentTemplate template, InternalConnection connection)
    {
        var source = template.InternalBlocks.FirstOrDefault(block => string.Equals(block.Id, connection.SourceBlockId, StringComparison.OrdinalIgnoreCase))?
            .Ports.FirstOrDefault(port => string.Equals(port.Name, connection.SourcePortName, StringComparison.OrdinalIgnoreCase));
        var target = template.InternalBlocks.FirstOrDefault(block => string.Equals(block.Id, connection.TargetBlockId, StringComparison.OrdinalIgnoreCase))?
            .Ports.FirstOrDefault(port => string.Equals(port.Name, connection.TargetPortName, StringComparison.OrdinalIgnoreCase));
        if (source is not null || target is not null)
        {
            return source?.SignalType == SignalType.Control || target?.SignalType == SignalType.Control;
        }

        return string.Equals(connection.PayloadType, HardwareDataType.Config.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    private static bool RequiresAnalogConversion(ComponentTemplate template) =>
        template.InternalBlocks
            .Where(block => block.Layer == InternalBlockLayer.Structural && Role(block) == "computecore")
            .Any(block =>
                block.Ports.Any(port => port.SignalType == SignalType.Analog) ||
                block.BlockKind.Contains("reram", StringComparison.OrdinalIgnoreCase) ||
                block.BlockKind.Contains("analog", StringComparison.OrdinalIgnoreCase));

    private static List<string> BlocksForRole(ComponentTemplate template, InternalBlockLayer layer, string role) =>
        template.InternalBlocks.Where(block => block.Layer == layer && Role(block) == role).Select(block => block.Id).ToList();

    private static string Role(ComponentTemplate template, string blockId)
    {
        var block = template.InternalBlocks.FirstOrDefault(item => string.Equals(item.Id, blockId, StringComparison.OrdinalIgnoreCase));
        return block is null ? "" : Role(block);
    }

    private static string Role(InternalBlock block)
    {
        var text = (block.Id + " " + block.BlockKind + " " + block.DisplayName).Replace("_", "").Replace("-", "").ToLowerInvariant();
        if (text.Contains("inputbuffer")) return "inputbuffer";
        if (text.Contains("weightstore")) return "weightstore";
        if (text.Contains("controller")) return "controller";
        if (text.Contains("decoder")) return "decoder";
        if (text.Contains("adc")) return "adc";
        if (text.Contains("dac")) return "dac";
        if (text.Contains("computecore") || text.Contains("compute") || text.Contains("array")) return "computecore";
        if (text.Contains("accumulator") || text.Contains("accumulate")) return "accumulator";
        if (text.Contains("ingress")) return "ingress";
        if (text.Contains("egress")) return "egress";
        return "";
    }

    private static ComponentTemplateIssue Error(string code, string location, string message, string? relatedId = null) =>
        new(code, ComponentTemplateIssueSeverity.Error, location, message, relatedId);
}
