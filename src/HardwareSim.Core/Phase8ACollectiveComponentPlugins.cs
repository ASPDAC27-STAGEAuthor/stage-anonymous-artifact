namespace HardwareSim.Core;

/// <summary>Explicit first-party Phase 8A collective plugin package.</summary>
public static class Phase8ACollectiveComponentPlugins
{
    /// <summary>Registers multicast, grouped vector sum, and tensor assembly without changing legacy registry defaults.</summary>
    public static void Load(ComponentTypeRegistry registry)
    {
        if (registry is null) throw new ArgumentNullException(nameof(registry));
        Phase8ABranchMulticastComponentPlugin.Load(registry);
        Phase8AGroupedVectorSumComponentPlugin.Load(registry);
        Phase8ATensorAssemblyComponentPlugin.Load(registry);
        Phase8ATensorSliceComponentPlugin.Load(registry);
    }
}
