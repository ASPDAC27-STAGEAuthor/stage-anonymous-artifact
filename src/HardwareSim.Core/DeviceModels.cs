using System.Globalization;
using System.Text.Json;

namespace HardwareSim.Core;

/// <summary>Defines the supported imported model type values used by hardware simulation contracts.</summary>
public enum ImportedModelType
{
    /// <summary>Selects the latency value for the imported model type contract.</summary>
    Latency,
    /// <summary>Selects the energy value for the imported model type contract.</summary>
    Energy,
    /// <summary>Selects the area value for the imported model type contract.</summary>
    Area,
    /// <summary>Selects the power value for the imported model type contract.</summary>
    Power,
    /// <summary>Selects the noise value for the imported model type contract.</summary>
    Noise,
    /// <summary>Selects the loss value for the imported model type contract.</summary>
    Loss,
    /// <summary>Selects the bandwidth value for the imported model type contract.</summary>
    Bandwidth,
    /// <summary>Selects the thermal value for the imported model type contract.</summary>
    Thermal,
    /// <summary>Selects the precision error value for the imported model type contract.</summary>
    PrecisionError
}

/// <summary>Represents imported device model data exchanged by hardware design and simulation workflows.</summary>
public sealed class ImportedDeviceModel
{
    /// <summary>Gets or sets the id value carried by the enclosing imported device model contract.</summary>
    public string Id { get; set; } = "";
    /// <summary>Gets or sets the model type value carried by the enclosing imported device model contract.</summary>
    public ImportedModelType ModelType { get; set; } = ImportedModelType.Energy;
    /// <summary>Gets or sets the values collection carried by the enclosing imported device model contract.</summary>
    public Dictionary<string, double> Values { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Gets or sets the metadata collection carried by the enclosing imported device model contract.</summary>
    public Dictionary<string, string> Metadata { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>Provides device model importer operations for hardware design and simulation workflows.</summary>
public static class DeviceModelImporter
{
    /// <summary>Creates json from the supplied external representation.</summary>
    public static ImportedDeviceModel FromJson(string json)
    {
        return JsonSerializer.Deserialize<ImportedDeviceModel>(json, HardwareGraphJson.Options)
            ?? throw new InvalidOperationException("Device model JSON was empty or invalid.");
    }

    /// <summary>Creates csv from the supplied external representation.</summary>
    public static IReadOnlyList<ImportedDeviceModel> FromCsv(string csv)
    {
        var lines = csv.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length <= 1)
        {
            return [];
        }

        var models = new Dictionary<string, ImportedDeviceModel>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in lines.Skip(1))
        {
            var parts = line.Split(',').Select(p => p.Trim()).ToArray();
            if (parts.Length < 4)
            {
                continue;
            }

            var id = parts[0];
            if (!Enum.TryParse<ImportedModelType>(parts[1], ignoreCase: true, out var modelType))
            {
                modelType = ImportedModelType.Energy;
            }

            if (!double.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            {
                continue;
            }

            if (!models.TryGetValue(id, out var model))
            {
                model = new ImportedDeviceModel { Id = id, ModelType = modelType };
                models[id] = model;
            }

            model.Values[parts[2]] = value;
        }

        return models.Values.ToList();
    }

    /// <summary>Creates yaml from the supplied external representation.</summary>
    public static IReadOnlyList<ImportedDeviceModel> FromYaml(string yaml)
    {
        var models = new List<ImportedDeviceModel>();
        ImportedDeviceModel? current = null;
        string? section = null;

        foreach (var rawLine in yaml.Split(["\r\n", "\n"], StringSplitOptions.None))
        {
            var line = StripComment(rawLine).Trim();
            if (string.IsNullOrWhiteSpace(line) || line == "---")
            {
                continue;
            }

            if (line.StartsWith("- ", StringComparison.Ordinal))
            {
                AddIfValid(models, current);
                current = new ImportedDeviceModel();
                section = null;
                line = line[2..].Trim();
                if (line.Length == 0)
                {
                    continue;
                }
            }

            current ??= new ImportedDeviceModel();

            if (line.EndsWith(":", StringComparison.Ordinal))
            {
                section = NormalizeYamlKey(line[..^1]);
                continue;
            }

            var separator = line.IndexOf(':');
            if (separator < 0)
            {
                continue;
            }

            var key = line[..separator].Trim();
            var value = Unquote(line[(separator + 1)..].Trim());
            if (string.Equals(section, "values", StringComparison.OrdinalIgnoreCase))
            {
                if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var numeric))
                {
                    current.Values[key] = numeric;
                }

                continue;
            }

            if (string.Equals(section, "metadata", StringComparison.OrdinalIgnoreCase))
            {
                current.Metadata[key] = value;
                continue;
            }

            ApplyYamlScalar(current, key, value);
        }

        AddIfValid(models, current);
        return models;
    }

    private static void ApplyYamlScalar(ImportedDeviceModel model, string key, string value)
    {
        switch (NormalizeYamlKey(key))
        {
            case "id":
                model.Id = value;
                break;
            case "modeltype":
            case "model_type":
                model.ModelType = Enum.TryParse<ImportedModelType>(value, ignoreCase: true, out var modelType)
                    ? modelType
                    : ImportedModelType.Energy;
                break;
        }
    }

    private static void AddIfValid(List<ImportedDeviceModel> models, ImportedDeviceModel? model)
    {
        if (model is not null && !string.IsNullOrWhiteSpace(model.Id))
        {
            models.Add(model);
        }
    }

    private static string NormalizeYamlKey(string key) =>
        key.Trim().Replace("-", "_", StringComparison.Ordinal).ToLowerInvariant();

    private static string StripComment(string line)
    {
        var commentIndex = line.IndexOf('#');
        return commentIndex < 0 ? line : line[..commentIndex];
    }

    private static string Unquote(string value)
    {
        if (value.Length >= 2 &&
            ((value[0] == '"' && value[^1] == '"') || (value[0] == '\'' && value[^1] == '\'')))
        {
            return value[1..^1];
        }

        return value;
    }
}

/// <summary>Represents device model registry data exchanged by hardware design and simulation workflows.</summary>
public sealed class DeviceModelRegistry
{
    private readonly Dictionary<string, ImportedDeviceModel> _models = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, CharacterizedPhysicalModel> _physicalModels = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Adds the supplied item to the enclosing collection or registry.</summary>
    public void Add(ImportedDeviceModel model)
    {
        if (string.IsNullOrWhiteSpace(model.Id))
        {
            throw new InvalidOperationException("Imported model id is required.");
        }

        _models[model.Id] = model;
    }

    /// <summary>Adds a Phase 7 characterized physical model to the registry.</summary>
    public void Add(CharacterizedPhysicalModel model)
    {
        if (string.IsNullOrWhiteSpace(model.Id))
        {
            throw new InvalidOperationException("Physical model id is required.");
        }

        _physicalModels[model.Id] = model;
    }

    /// <summary>Finds the item matching the supplied identifier when it is registered.</summary>
    public ImportedDeviceModel? Find(string? modelId)
    {
        return modelId is not null && _models.TryGetValue(modelId, out var model) ? model : null;
    }

    /// <summary>Finds a Phase 7 characterized physical model when it is registered.</summary>
    public CharacterizedPhysicalModel? FindPhysical(string? modelId)
    {
        return modelId is not null && _physicalModels.TryGetValue(modelId, out var model) ? model : null;
    }
}
/// <summary>Represents optical link parameters data exchanged by hardware design and simulation workflows.</summary>
public sealed class OpticalLinkParameters
{
    /// <summary>Gets or sets the length millimeters value carried by the enclosing optical link parameters contract.</summary>
    public double LengthMillimeters { get; set; }
    /// <summary>Gets or sets the bend count value carried by the enclosing optical link parameters contract.</summary>
    public int BendCount { get; set; }
    /// <summary>Gets or sets the crossing count value carried by the enclosing optical link parameters contract.</summary>
    public int CrossingCount { get; set; }
    /// <summary>Gets or sets the splitter count value carried by the enclosing optical link parameters contract.</summary>
    public int SplitterCount { get; set; }
    /// <summary>Gets or sets the waveguide loss db per millimeter value carried by the enclosing optical link parameters contract.</summary>
    public double WaveguideLossDbPerMillimeter { get; set; } = 0.5;
    /// <summary>Gets or sets the bend loss db value carried by the enclosing optical link parameters contract.</summary>
    public double BendLossDb { get; set; } = 0.02;
    /// <summary>Gets or sets the crossing loss db value carried by the enclosing optical link parameters contract.</summary>
    public double CrossingLossDb { get; set; } = 0.05;
    /// <summary>Gets or sets the coupler loss db value carried by the enclosing optical link parameters contract.</summary>
    public double CouplerLossDb { get; set; } = 1.0;
    /// <summary>Gets or sets the device insertion loss db value carried by the enclosing optical link parameters contract.</summary>
    public double DeviceInsertionLossDb { get; set; } = 1.0;
    /// <summary>Gets or sets the splitter loss db value carried by the enclosing optical link parameters contract.</summary>
    public double SplitterLossDb { get; set; } = 3.0;
    /// <summary>Gets or sets the laser energy per bit value carried by the enclosing optical link parameters contract.</summary>
    public double LaserEnergyPerBit { get; set; } = 0.2;
    /// <summary>Gets or sets the modulator energy per bit value carried by the enclosing optical link parameters contract.</summary>
    public double ModulatorEnergyPerBit { get; set; } = 0.05;
    /// <summary>Gets or sets the receiver energy per bit value carried by the enclosing optical link parameters contract.</summary>
    public double ReceiverEnergyPerBit { get; set; } = 0.08;
    /// <summary>Gets or sets the thermal tuning energy per bit value carried by the enclosing optical link parameters contract.</summary>
    public double ThermalTuningEnergyPerBit { get; set; } = 0.02;
}

/// <summary>Represents optical link estimate data exchanged by hardware design and simulation workflows.</summary>
/// <param name="LossDb">Provides the loss db value carried by this contract.</param>
/// <param name="EnergyPerBit">Provides the energy per bit value carried by this contract.</param>
public sealed record OpticalLinkEstimate(double LossDb, double EnergyPerBit);

/// <summary>Provides optical model operations for hardware design and simulation workflows.</summary>
public static class OpticalModel
{
    /// <summary>Estimates physical cost from the supplied device or simulation parameters.</summary>
    public static OpticalLinkEstimate Estimate(OpticalLinkParameters parameters)
    {
        var loss =
            parameters.WaveguideLossDbPerMillimeter * parameters.LengthMillimeters +
            parameters.BendLossDb * parameters.BendCount +
            parameters.CrossingLossDb * parameters.CrossingCount +
            parameters.CouplerLossDb +
            parameters.DeviceInsertionLossDb +
            parameters.SplitterLossDb * parameters.SplitterCount;

        var energy =
            parameters.LaserEnergyPerBit +
            parameters.ModulatorEnergyPerBit +
            parameters.ReceiverEnergyPerBit +
            parameters.ThermalTuningEnergyPerBit;

        return new OpticalLinkEstimate(loss, energy);
    }
}

/// <summary>Represents cim crossbar parameters data exchanged by hardware design and simulation workflows.</summary>
public sealed class CimCrossbarParameters
{
    /// <summary>Gets or sets the rows value carried by the enclosing cim crossbar parameters contract.</summary>
    public int Rows { get; set; } = 128;
    /// <summary>Gets or sets the columns value carried by the enclosing cim crossbar parameters contract.</summary>
    public int Columns { get; set; } = 128;
    /// <summary>Gets or sets the adc bits value carried by the enclosing cim crossbar parameters contract.</summary>
    public int AdcBits { get; set; } = 6;
    /// <summary>Gets or sets the noise standard deviation value carried by the enclosing cim crossbar parameters contract.</summary>
    public double NoiseStandardDeviation { get; set; }
    /// <summary>Gets or sets the device variation standard deviation value carried by the enclosing cim crossbar parameters contract.</summary>
    public double DeviceVariationStandardDeviation { get; set; }
    /// <summary>Gets or sets the mac energy picojoules value carried by the enclosing cim crossbar parameters contract.</summary>
    public double MacEnergyPicojoules { get; set; } = 0.02;
    /// <summary>Gets or sets the adc energy picojoules per conversion value carried by the enclosing cim crossbar parameters contract.</summary>
    public double AdcEnergyPicojoulesPerConversion { get; set; } = 1.0;
    /// <summary>Gets or sets the dac energy picojoules per conversion value carried by the enclosing cim crossbar parameters contract.</summary>
    public double DacEnergyPicojoulesPerConversion { get; set; } = 0.5;
    /// <summary>Gets or sets the read latency cycles value carried by the enclosing cim crossbar parameters contract.</summary>
    public double ReadLatencyCycles { get; set; } = 2;
    /// <summary>Gets or sets the write energy picojoules per cell value carried by the enclosing cim crossbar parameters contract.</summary>
    public double WriteEnergyPicojoulesPerCell { get; set; } = 0.01;
}

/// <summary>Represents cim estimate data exchanged by hardware design and simulation workflows.</summary>
/// <param name="ComputeEnergyPicojoules">Provides the compute energy picojoules value carried by this contract.</param>
/// <param name="ConversionEnergyPicojoules">Provides the conversion energy picojoules value carried by this contract.</param>
/// <param name="ReadLatencyCycles">Provides the read latency cycles value carried by this contract.</param>
/// <param name="QuantizationStep">Provides the quantization step value carried by this contract.</param>
/// <param name="NoiseStandardDeviation">Provides the noise standard deviation value carried by this contract.</param>
/// <param name="DeviceVariationStandardDeviation">Provides the device variation standard deviation value carried by this contract.</param>
/// <param name="ErrorStandardDeviation">Provides the error standard deviation value carried by this contract.</param>
/// <param name="EffectivePrecisionBits">Provides the effective precision bits value carried by this contract.</param>
public sealed record CimEstimate(
    double ComputeEnergyPicojoules,
    double ConversionEnergyPicojoules,
    double ReadLatencyCycles,
    double QuantizationStep,
    double NoiseStandardDeviation,
    double DeviceVariationStandardDeviation,
    double ErrorStandardDeviation,
    double EffectivePrecisionBits);

/// <summary>Provides cim model operations for hardware design and simulation workflows.</summary>
public static class CimModel
{
    /// <summary>Estimates physical cost from the supplied device or simulation parameters.</summary>
    public static CimEstimate Estimate(CimCrossbarParameters parameters, long operations)
    {
        var macEnergy = operations * parameters.MacEnergyPicojoules;
        var conversions = Math.Ceiling((double)operations / Math.Max(1, parameters.Columns));
        var conversionEnergy = conversions * (parameters.AdcEnergyPicojoulesPerConversion + parameters.DacEnergyPicojoulesPerConversion);
        var adcLevels = Math.Max(2, Math.Pow(2, Math.Max(1, parameters.AdcBits)));
        var quantizationStep = 1.0 / (adcLevels - 1.0);
        var quantizationErrorStdDev = quantizationStep / Math.Sqrt(12.0);
        var noiseStdDev = Math.Max(0, parameters.NoiseStandardDeviation);
        var variationStdDev = Math.Max(0, parameters.DeviceVariationStandardDeviation);
        var errorStdDev = Math.Sqrt(
            (quantizationErrorStdDev * quantizationErrorStdDev) +
            (noiseStdDev * noiseStdDev) +
            (variationStdDev * variationStdDev));
        var effectivePrecisionBits = Math.Min(
            Math.Max(1, parameters.AdcBits),
            Math.Max(0, -Math.Log(Math.Max(errorStdDev, 1e-12), 2)));

        return new CimEstimate(
            macEnergy,
            conversionEnergy,
            parameters.ReadLatencyCycles,
            quantizationStep,
            noiseStdDev,
            variationStdDev,
            errorStdDev,
            effectivePrecisionBits);
    }
}

/// <summary>Provides device model binding operations for hardware design and simulation workflows.</summary>
public static class DeviceModelBinding
{
    /// <summary>Applies to component to the supplied graph or model.</summary>
    public static void ApplyToComponent(HardwareComponent component, ImportedDeviceModel model)
    {
        component.Parameters[$"model_{model.ModelType.ToString().ToLowerInvariant()}_ref"] = model.Id;
        foreach (var (key, value) in model.Values)
        {
            component.Parameters[$"model_{key}"] = value.ToString(CultureInfo.InvariantCulture);
        }

        if (model.Values.TryGetValue("area_mm2", out var area))
        {
            component.AreaModel = $"{area.ToString(CultureInfo.InvariantCulture)} mm2";
        }

        if (model.Values.TryGetValue("latency_cycles", out var latency))
        {
            component.LatencyModel = $"{latency.ToString(CultureInfo.InvariantCulture)} cycles";
        }

        if (model.Values.TryGetValue("energy_pj", out var energy))
        {
            component.EnergyModel = $"{energy.ToString(CultureInfo.InvariantCulture)} pJ";
        }
    }

    /// <summary>Applies to link to the supplied graph or model.</summary>
    public static void ApplyToLink(HardwareLink link, ImportedDeviceModel model)
    {
        link.Parameters[$"model_{model.ModelType.ToString().ToLowerInvariant()}_ref"] = model.Id;
        foreach (var (key, value) in model.Values)
        {
            link.Parameters[$"model_{key}"] = value.ToString(CultureInfo.InvariantCulture);
        }

        if (model.Values.TryGetValue("energy_per_bit", out var energyPerBit))
        {
            link.EnergyPerBit = energyPerBit;
        }

        if (model.Values.TryGetValue("latency_cycles", out var latencyCycles))
        {
            link.LatencyCycles = Math.Max(1, (int)Math.Ceiling(latencyCycles));
        }

        if (model.Values.TryGetValue("insertion_loss_db", out var lossDb))
        {
            link.Parameters["optical_loss_db"] = lossDb.ToString(CultureInfo.InvariantCulture);
        }
    }
}
