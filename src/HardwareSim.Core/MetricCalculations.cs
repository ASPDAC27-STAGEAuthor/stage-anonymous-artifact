
namespace HardwareSim.Core;

/// <summary>Provides exact, strongly typed component energy equations.</summary>
public static class EnergyFormula
{
    /// <summary>Computes processing-element dynamic energy as total MACs times energy per MAC.</summary>
    public static Picojoules ProcessingElement(MacCount totalMacs, PicojoulesPerMac energyPerMac) =>
        energyPerMac * totalMacs;

    /// <summary>Computes router dynamic energy as total routed bits times energy per bit.</summary>
    public static Picojoules Router(BitCount totalBits, PicojoulesPerBit energyPerBit) =>
        energyPerBit * totalBits;

    /// <summary>Computes link dynamic energy as total transferred bits times energy per bit.</summary>
    public static Picojoules Link(BitCount totalBits, PicojoulesPerBit energyPerBit) =>
        energyPerBit * totalBits;

    /// <summary>Computes buffer dynamic energy from separately metered read and write bits.</summary>
    public static Picojoules Buffer(
        BitCount readBits,
        PicojoulesPerBit readEnergyPerBit,
        BitCount writeBits,
        PicojoulesPerBit writeEnergyPerBit) =>
        readEnergyPerBit * readBits + writeEnergyPerBit * writeBits;

    /// <summary>Computes memory dynamic energy from separately metered read and write bits.</summary>
    public static Picojoules Memory(
        BitCount readBits,
        PicojoulesPerBit readEnergyPerBit,
        BitCount writeBits,
        PicojoulesPerBit writeEnergyPerBit) =>
        readEnergyPerBit * readBits + writeEnergyPerBit * writeBits;
}

/// <summary>One metered energy contribution with both physical-kind and system-category provenance.</summary>
/// <param name="ComponentId">Stable originating component identifier.</param>
/// <param name="Kind">Physical contribution kind.</param>
/// <param name="Category">System-level aggregation category.</param>
/// <param name="Amount">Strongly typed energy amount.</param>
public sealed record EnergyContribution(string ComponentId, EnergyKind Kind, EnergyCategory Category, Picojoules Amount);

/// <summary>Holds energy aggregated through the two required classification views.</summary>
public sealed class AggregatedEnergy
{
    internal AggregatedEnergy(EnergyBreakdown byKind, EnergyCategoryBreakdown byCategory)
    {
        ByKind = byKind;
        ByCategory = byCategory;
    }

    /// <summary>Gets energy classified by physical contribution kind.</summary>
    public EnergyBreakdown ByKind { get; }

    /// <summary>Gets energy classified by system-level category.</summary>
    public EnergyCategoryBreakdown ByCategory { get; }

    /// <summary>Gets the canonical total in picojoules.</summary>
    public Picojoules TotalPJ => ByKind.TotalPJ;

    /// <summary>Returns whether both classification views have the same total within the explicit tolerance.</summary>
    public bool IsConsistent(double tolerancePJ = EnergyAggregation.DefaultTolerancePJ)
    {
        MetricUnitGuard.NonNegativeFinite(tolerancePJ, nameof(tolerancePJ));
        return Math.Abs(ByKind.TotalPJ.Value - ByCategory.TotalPJ.Value) <= tolerancePJ;
    }
}

/// <summary>Aggregates deterministic, provenance-carrying energy contributions.</summary>
public static class EnergyAggregation
{
    /// <summary>Default tolerance used only for floating-point total consistency checks.</summary>
    public const double DefaultTolerancePJ = 1e-9;

    /// <summary>Aggregates every contribution into both complete classification views.</summary>
    public static AggregatedEnergy Aggregate(IEnumerable<EnergyContribution> contributions)
    {
        if (contributions is null)
        {
            throw new ArgumentNullException(nameof(contributions));
        }

        var byKind = new EnergyBreakdown();
        var byCategory = new EnergyCategoryBreakdown();
        foreach (var contribution in contributions
                     .OrderBy(item => item.ComponentId, StringComparer.Ordinal)
                     .ThenBy(item => item.Kind)
                     .ThenBy(item => item.Category)
                     .ThenBy(item => item.Amount.Value))
        {
            byKind[contribution.Kind] = byKind[contribution.Kind] + contribution.Amount;
            byCategory[contribution.Category] = byCategory[contribution.Category] + contribution.Amount;
        }

        return new AggregatedEnergy(byKind, byCategory);
    }
}
