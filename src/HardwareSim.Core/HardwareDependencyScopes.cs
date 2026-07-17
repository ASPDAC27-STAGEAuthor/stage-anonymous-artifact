namespace HardwareSim.Core;

/// <summary>Stable link dependency scopes used by static graph validation.</summary>
public static class HardwareDependencyScopes
{
    /// <summary>Gets the hardware-link parameter carrying a dependency scope.</summary>
    public const string Parameter = "dependency_scope";

    /// <summary>Marks a queued, stateful capability output that re-enters its owning transport component.</summary>
    public const string StatefulCapabilityReinjectionV1 = "stateful-capability-reinjection/v1";
}
