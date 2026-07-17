namespace HardwareSim.Core;

internal static class Phase8AMatMulRuntimeIds
{
    public const string ActivationSource = "scenario-source-activation";
    public const string WeightSource = "scenario-source-weight";
    public const string Sink = "scenario-output-sink";
    public const string Assembly = "scenario-final-offset-assembly";
    public static string ActivationSourceLink(int clusterIndex) => "scenario.link.activation-source.c" + clusterIndex.ToString("D4", System.Globalization.CultureInfo.InvariantCulture);
    public const string AssemblyInputLink = "scenario.link.assembly.in";
    public const string AssemblyOutputLink = "scenario.link.assembly-sink";

    public static string Short(string value) => ComponentExecutionJson.ComputeSha256(value)[..12];
    public static string BranchComponent(string anchor) => "scenario-mcast-" + Short(anchor);
    public static string BranchInputLink(string anchor) => "scenario.link.mcast.in." + Short(anchor);
    public static string BranchOutputLink(string anchor, string authorityLink) => "scenario.link.mcast.out." + Short(anchor) + "." + Short(authorityLink);
    public static string LocalCollector(string group) => "scenario-local-sum-" + Short(group);
    public static string LocalInputLink(string group) => "scenario.link.local.in." + Short(group);
    public static string LocalOutputLink(string group) => "scenario.link.local.out." + Short(group);
    public static string GlobalCollector(string group) => "scenario-global-sum-" + Short(group);
    public static string GlobalInputLink(string group) => "scenario.link.global.in." + Short(group);
    public static string GlobalOutputLink(string group) => "scenario.link.global.out." + Short(group);
    public static string WeightPort(string assignment) => "out-" + Short(assignment);
    public static string WeightLink(string assignment) => "scenario.link.weight." + Short(assignment);
    public static string WeightFlow(string assignment) => "scenario.flow.weight." + Short(assignment);
    public static string PartialFlow(string assignment) => "scenario.flow.psum." + Short(assignment);
    public static string LocalOutputFlow(string group) => "scenario.flow.local-output." + Short(group);
    public static string GlobalOutputFlow(string group) => "scenario.flow.global-output." + Short(group);
    public static string AssemblyOutputFlow() => "scenario.flow.assembly-output";
    public static string ActivationSourceFlow(int k) => "scenario.flow.activation-source.k" + k;
    public static string BranchFlow(int k, string component) => "scenario.flow.activation-branch.k" + k + "." + Short(component);
    public static string Path(string flow) => "scenario.path." + flow;
    public static string ActivationPath(int k, string source, string destination) =>
        "scenario.path.activation.k" + k + "." + Short(source) + "." + Short(destination);
}
