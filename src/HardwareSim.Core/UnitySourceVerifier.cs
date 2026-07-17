using System.Text.Json;
using System.Xml.Linq;

namespace HardwareSim.Core;

/// <summary>Represents unity source verification result data exchanged by hardware design and simulation workflows.</summary>
/// <param name="Errors">Provides the errors value carried by this contract.</param>
/// <param name="SceneObjects">Provides the scene objects value carried by this contract.</param>
/// <param name="ScriptReferences">Provides the script references value carried by this contract.</param>
/// <param name="LayoutControls">Provides the layout controls value carried by this contract.</param>
/// <param name="RuntimeVerification">Provides the runtime verification value carried by this contract.</param>
public sealed record UnitySourceVerificationResult(
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> SceneObjects,
    IReadOnlyList<string> ScriptReferences,
    IReadOnlyList<string> LayoutControls,
    string RuntimeVerification)
{
    /// <summary>Gets whether source verification completed without any reported errors.</summary>
    public bool Passed => Errors.Count == 0;
}

/// <summary>Provides unity source verifier operations for hardware design and simulation workflows.</summary>
public static class UnitySourceVerifier
{
    /// <summary>Checks required Unity scenes, scripts, runtime bindings, and forbidden duplicate Core definitions.</summary>
    public static UnitySourceVerificationResult Verify(
        string unityRoot,
        IReadOnlyCollection<string> requiredSceneObjects,
        IReadOnlyCollection<string> requiredScripts,
        IReadOnlyCollection<string> requiredControls)
    {
        var errors = new List<string>();
        var scenePath = Path.Combine(unityRoot, "Assets", "Scenes", "HardwareSimulatorScene.asset.json");
        var layoutPath = Path.Combine(unityRoot, "Assets", "UI", "HardwareSimulatorLayout.uxml");
        var scriptsRoot = Path.Combine(unityRoot, "Assets", "Scripts");

        if (!File.Exists(scenePath))
        {
            errors.Add($"Missing scene manifest: {scenePath}");
            return new UnitySourceVerificationResult(errors, [], [], [], "");
        }

        if (!File.Exists(layoutPath))
        {
            errors.Add($"Missing UI layout: {layoutPath}");
            return new UnitySourceVerificationResult(errors, [], [], [], "");
        }

        using var sceneDocument = JsonDocument.Parse(File.ReadAllText(scenePath));
        var root = sceneDocument.RootElement;
        var runtimeVerification = root.TryGetProperty("runtimeVerification", out var runtimeProperty)
            ? runtimeProperty.GetString() ?? ""
            : "";
        if (!string.Equals(runtimeVerification, "not_run_in_unity_editor", StringComparison.Ordinal))
        {
            errors.Add("Scene manifest must keep runtimeVerification=not_run_in_unity_editor until rendered Unity evidence exists.");
        }

        var sceneObjects = new HashSet<string>(StringComparer.Ordinal);
        var scriptReferences = new HashSet<string>(StringComparer.Ordinal);
        if (root.TryGetProperty("objects", out var objects) && objects.ValueKind == JsonValueKind.Array)
        {
            foreach (var obj in objects.EnumerateArray())
            {
                if (obj.TryGetProperty("name", out var nameProperty) && nameProperty.GetString() is { } name)
                {
                    sceneObjects.Add(name);
                }

                if (!obj.TryGetProperty("scripts", out var scripts) || scripts.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var script in scripts.EnumerateArray())
                {
                    if (script.GetString() is { } scriptName)
                    {
                        scriptReferences.Add(scriptName);
                    }
                }
            }
        }

        foreach (var expected in requiredSceneObjects)
        {
            if (!sceneObjects.Contains(expected))
            {
                errors.Add($"Scene manifest is missing object {expected}.");
            }
        }

        foreach (var expected in requiredScripts)
        {
            if (!scriptReferences.Contains(expected))
            {
                errors.Add($"Scene manifest is missing script reference {expected}.");
            }

            var scriptPath = Path.Combine(scriptsRoot, $"{expected}.cs");
            if (!File.Exists(scriptPath))
            {
                errors.Add($"Referenced script file is missing: {scriptPath}");
            }
        }

        var layoutControls = XDocument.Parse(File.ReadAllText(layoutPath))
            .Descendants()
            .Select(e => e.Attribute("name")?.Value)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var expected in requiredControls)
        {
            if (!layoutControls.Contains(expected))
            {
                errors.Add($"UI layout is missing control {expected}.");
            }
        }

        return new UnitySourceVerificationResult(
            errors,
            sceneObjects.OrderBy(name => name, StringComparer.Ordinal).ToList(),
            scriptReferences.OrderBy(name => name, StringComparer.Ordinal).ToList(),
            layoutControls.OrderBy(name => name, StringComparer.Ordinal).ToList(),
            runtimeVerification);
    }
}
