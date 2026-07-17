using System.Globalization;
using HardwareSim.Core;

internal static class CimEgressAccountingTests
{
    public static IReadOnlyList<TestCase> All =>
    [
        new("P10-CIM-ENERGY-001 digital and CIM templates share complete output-stage accounting", SharedOutputStageAccountingIsExact, "paper")
    ];

    private static void SharedOutputStageAccountingIsExact()
    {
        var root = FindRepositoryRoot();
        var catalogPath = Path.Combine(root, "data", "characterization", "phase7c-literature-catalog-v1.json");
        var package = Phase9LiteratureDeviceProfileNormalizer.Normalize(Phase7CLiteratureCharacterizationCatalog.Load(catalogPath));
        var digital = ComponentTemplateExamples.PeArray32x32Fp8SramSynthetic();
        var cim = Phase9CimTemplateFactory.Create(package);

        long OutputBits(ComponentTemplate template)
        {
            var raw = template.CompiledProfile?.DerivedMetrics.GetValueOrDefault("output_bits") ?? "";
            TestAssert.True(long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value), "compiled output_bits metric");
            return value;
        }

        var digitalBits = OutputBits(digital);
        var cimBits = OutputBits(cim);
        var digitalEgress = ComponentTemplateEnergyModels.SharedOutputStageEnergyPicojoules(digitalBits);
        var cimEgress = ComponentTemplateEnergyModels.SharedOutputStageEnergyPicojoules(cimBits);

        TestAssert.Equal(256L, digitalBits, "digital output bits per invocation");
        TestAssert.Equal(digitalBits, cimBits, "matched shell output bits");
        TestAssert.Near(0.128, digitalEgress, 0, "digital output-stage energy");
        TestAssert.Near(digitalEgress, cimEgress, 0, "shared output-stage energy");
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "STAGE.sln"))) return current.FullName;
            current = current.Parent;
        }
        throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
