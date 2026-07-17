namespace HardwareSim.Core;

internal sealed record Phase8AActivationRouteDemand(
    string DemandId,
    string TensorTileId,
    string ProducerComponentId,
    string ConsumerComponentId,
    string RoutePathId,
    long Bits);

internal sealed class Phase8AActivationDistribution
{
    internal Phase8AActivationDistribution(
        string distributionId,
        string tensorTileId,
        string producerComponentId,
        long bits,
        IEnumerable<Phase8AActivationRouteDemand> demands,
        IEnumerable<string> branchComponentIds)
    {
        DistributionId = distributionId;
        TensorTileId = tensorTileId;
        ProducerComponentId = producerComponentId;
        Bits = bits;
        Demands = Array.AsReadOnly(demands.OrderBy(item => item.ConsumerComponentId, StringComparer.Ordinal)
            .ThenBy(item => item.DemandId, StringComparer.Ordinal).ToArray());
        BranchComponentIds = Array.AsReadOnly(branchComponentIds.ToArray());
    }

    internal string DistributionId { get; }
    internal string TensorTileId { get; }
    internal string ProducerComponentId { get; }
    internal long Bits { get; }
    internal IReadOnlyList<Phase8AActivationRouteDemand> Demands { get; }
    internal IReadOnlyList<string> BranchComponentIds { get; }
    internal bool IsMulticast => Demands.Count > 1;
}

internal static class Phase8AActivationDistributionPlanner
{
    internal static IReadOnlyList<Phase8AActivationDistribution> Plan(
        IEnumerable<Phase8AActivationRouteDemand> demands,
        Phase8ALogicalPathCatalog catalog)
    {
        if (demands is null) throw new ArgumentNullException(nameof(demands));
        if (catalog is null) throw new ArgumentNullException(nameof(catalog));
        var materialized = demands.ToArray();
        if (materialized.Any(item => item is null || string.IsNullOrWhiteSpace(item.DemandId) ||
                string.IsNullOrWhiteSpace(item.TensorTileId) || string.IsNullOrWhiteSpace(item.ProducerComponentId) ||
                string.IsNullOrWhiteSpace(item.ConsumerComponentId) || string.IsNullOrWhiteSpace(item.RoutePathId) || item.Bits <= 0) ||
            materialized.Select(item => item.DemandId).Distinct(StringComparer.Ordinal).Count() != materialized.Length)
            throw new ArgumentException("Activation route demands require unique ids, exact endpoints, path ids, and positive bits.", nameof(demands));

        var result = new List<Phase8AActivationDistribution>();
        foreach (var tileGroup in materialized
                     .GroupBy(item => (item.TensorTileId, item.ProducerComponentId, item.Bits))
                     .OrderBy(group => group.Key.TensorTileId, StringComparer.Ordinal)
                     .ThenBy(group => group.Key.ProducerComponentId, StringComparer.Ordinal)
                     .ThenBy(group => group.Key.Bits))
        {
            var consumerGroups = tileGroup.GroupBy(item => item.ConsumerComponentId, StringComparer.Ordinal)
                .OrderBy(group => group.Key, StringComparer.Ordinal).ToArray();
            var primary = consumerGroups.Select(group => group.OrderBy(item => item.DemandId, StringComparer.Ordinal).First()).ToArray();
            AddDistribution(result, primary, catalog);
            foreach (var duplicate in consumerGroups.SelectMany(group => group.OrderBy(item => item.DemandId, StringComparer.Ordinal).Skip(1)))
                AddDistribution(result, [duplicate], catalog);
        }
        return Array.AsReadOnly(result.OrderBy(item => item.DistributionId, StringComparer.Ordinal).ToArray());
    }

    private static void AddDistribution(
        List<Phase8AActivationDistribution> result,
        IReadOnlyList<Phase8AActivationRouteDemand> demands,
        Phase8ALogicalPathCatalog catalog)
    {
        var entries = demands.Select(demand =>
        {
            var entry = catalog.Find(demand.RoutePathId)
                ?? throw new InvalidOperationException($"Activation path '{demand.RoutePathId}' is absent from the exact catalog.");
            if (!string.Equals(entry.SourceComponentId, demand.ProducerComponentId, StringComparison.Ordinal) ||
                !string.Equals(entry.DestinationComponentId, demand.ConsumerComponentId, StringComparison.Ordinal))
                throw new InvalidOperationException($"Activation path '{demand.RoutePathId}' endpoints do not match demand '{demand.DemandId}'.");
            return entry;
        }).ToArray();
        var unionHops = entries.SelectMany(entry => entry.Hops).ToArray();
        if (demands.Count > 1 && unionHops.GroupBy(hop => hop.DestinationComponentId, StringComparer.Ordinal)
                .Any(group => group.Select(hop => hop.LogicalLinkId).Distinct(StringComparer.Ordinal).Count() > 1))
            throw new InvalidOperationException("Activation multicast routes must form a directed tree without reconvergence.");
        var branches = entries.SelectMany(entry => entry.Hops.Select((hop, index) => new
            {
                hop.SourceComponentId,
                hop.LogicalLinkId,
                Index = index
            }))
            .GroupBy(item => item.SourceComponentId, StringComparer.Ordinal)
            .Where(group => group.Select(item => item.LogicalLinkId).Distinct(StringComparer.Ordinal).Count() > 1)
            .OrderBy(group => group.Min(item => item.Index))
            .ThenBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => group.Key)
            .ToArray();
        if (demands.Count > 1 && branches.Length == 0)
            throw new InvalidOperationException("Activation multicast routes require at least one explicit branch component.");
        var first = demands.OrderBy(item => item.DemandId, StringComparer.Ordinal).First();
        result.Add(new Phase8AActivationDistribution(
            demands.Count > 1 ? "multicast:" + first.DemandId : first.DemandId,
            first.TensorTileId,
            first.ProducerComponentId,
            first.Bits,
            demands,
            branches));
    }
}
