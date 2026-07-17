namespace HardwareSim.Core;

/// <summary>Represents the result of generating a persisted trace store for Unity replay.</summary>
/// <param name="Succeeded">Provides whether trace generation completed and persisted a store.</param>
/// <param name="StoreDirectory">Provides the output store directory requested by the caller.</param>
/// <param name="Manifest">Provides the persisted trace manifest when generation succeeds.</param>
/// <param name="SimulationResult">Provides the simulation result used to populate runtime metrics and analysis.</param>
/// <param name="Graph">Provides the graph supplied by the Unity editor.</param>
/// <param name="Errors">Provides compile diagnostics when generation fails before simulation.</param>
/// <param name="ErrorMessage">Provides a stable user-facing failure summary.</param>
public sealed record UnityTraceGenerationResult(
    bool Succeeded,
    string StoreDirectory,
    PersistedTraceManifest? Manifest,
    SimulationResult? SimulationResult,
    HardwareGraph? Graph,
    IReadOnlyList<CompilationIssue> Errors,
    string ErrorMessage);

/// <summary>Provides Unity-facing persisted trace generation without exposing engine invocation in Unity runtime scripts.</summary>
public sealed class UnityTraceGenerationAdapter
{
    /// <summary>Compiles the supplied graph, runs the default cycle simulation, and writes a FullCycleTrace store.</summary>
    public UnityTraceGenerationResult GenerateFullCycleTraceStore(
        HardwareGraph graph,
        string storeDirectory,
        SimulationOptions? options = null,
        IReadOnlyDictionary<string, string>? config = null)
    {
        if (graph is null)
        {
            throw new ArgumentNullException(nameof(graph));
        }

        if (string.IsNullOrWhiteSpace(storeDirectory))
        {
            throw new ArgumentException("Trace store directory is required.", nameof(storeDirectory));
        }

        var componentRegistry = ComponentTypeRegistry.CreateDefault();
        var compilation = new SimulationGraphCompiler().CompileHardware(graph, componentRegistry: componentRegistry);
        if (!compilation.IsSuccess)
        {
            var message = string.Join("; ", compilation.Errors.Select(error => $"{error.Code}:{error.Message}"));
            return new UnityTraceGenerationResult(false, storeDirectory, null, null, graph, compilation.Errors, message);
        }

        var frozenKernels = componentRegistry.FreezeRuntimeKernels();
        if (!frozenKernels.IsSuccess || frozenKernels.Snapshot is null)
        {
            var errors = frozenKernels.Issues
                .Where(issue => issue.Severity == ValidationSeverity.Error)
                .Select(issue => new CompilationIssue(
                    issue.Code,
                    issue.Severity,
                    "$.runtime_kernel_registry",
                    issue.Message,
                    issue.RelatedId))
                .ToList();
            var message = string.Join("; ", errors.Select(error => $"{error.Code}:{error.Message}"));
            return new UnityTraceGenerationResult(false, storeDirectory, null, null, graph, errors, message);
        }

        options ??= new SimulationOptions();
        var result = new CycleSimulationEngine(frozenKernels.Snapshot).Run(compilation.Graph!, options);
        var runtimeErrors = result.Issues
            .Where(issue => string.Equals(issue.Severity, "error", StringComparison.OrdinalIgnoreCase))
            .Select(issue => new CompilationIssue(
                issue.Code,
                ValidationSeverity.Error,
                $"$.simulation.cycles[{issue.Cycle}]",
                issue.Message,
                string.IsNullOrWhiteSpace(issue.ComponentId) ? null : issue.ComponentId))
            .ToList();
        if (!result.Completed || runtimeErrors.Count > 0)
        {
            if (runtimeErrors.Count == 0)
            {
                runtimeErrors.Add(new CompilationIssue(
                    "UnityTraceSimulationIncomplete",
                    ValidationSeverity.Error,
                    "$.simulation",
                    string.IsNullOrWhiteSpace(result.CompletionReason)
                        ? "Simulation did not reach a normal completion state."
                        : result.CompletionReason));
            }

            var message = string.Join("; ", runtimeErrors.Select(error => $"{error.Code}:{error.Message}"));
            return new UnityTraceGenerationResult(false, storeDirectory, null, result, graph, runtimeErrors, message);
        }

        var traceConfig = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["source"] = "unity-generate-trace",
            ["max_cycles"] = options.MaxCycles.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["execution_mode"] = options.ExecutionMode.ToString(),
            ["graph_components"] = graph.Components.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["graph_links"] = graph.Links.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["runtime_kernel_registry_hash"] = frozenKernels.Snapshot.ContentHash
        };

        if (config is not null)
        {
            foreach (var pair in config)
            {
                traceConfig[pair.Key] = pair.Value ?? string.Empty;
            }
        }

        var manifest = PersistedTraceStore.Write(result, storeDirectory, new TraceStorageOptions
        {
            TraceLevel = TraceLevel.FullCycleTrace,
            Seed = options.DeterministicSeed,
            Config = traceConfig
        });

        return new UnityTraceGenerationResult(true, storeDirectory, manifest, result, graph, Array.Empty<CompilationIssue>(), string.Empty);
    }
}
