using System.Text.Json;

internal sealed record TestCase(string Name, Action Body, string? Group = null, bool ExplicitOnly = false)
{
    public static implicit operator TestCase((string Name, Action Body) value) => new(value.Name, value.Body);
}

internal sealed record TestCaseResult(string Name, string Status, string? Error, long DurationMilliseconds);

internal static class TestRunner
{
    public static int Run(IEnumerable<TestCase> testCases, string[] args)
    {
        var filter = ReadArgument(args, "--filter");
        var group = ReadArgument(args, "--group");
        var resultsPath = ReadArgument(args, "--results");
        var selected = Select(testCases, filter, group);
        var results = new List<TestCaseResult>();

        foreach (var test in selected)
        {
            var started = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                test.Body();
                started.Stop();
                results.Add(new TestCaseResult(test.Name, "PASS", null, started.ElapsedMilliseconds));
                Console.WriteLine($"PASS {test.Name}");
            }
            catch (Exception exception)
            {
                started.Stop();
                results.Add(new TestCaseResult(test.Name, "FAIL", exception.ToString(), started.ElapsedMilliseconds));
                Console.WriteLine($"FAIL {test.Name}");
                Console.WriteLine(exception);
            }
        }

        if (!string.IsNullOrWhiteSpace(resultsPath))
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(resultsPath));
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(
                resultsPath,
                JsonSerializer.Serialize(
                    new
                    {
                        schema_version = "1.0",
                        filter,
                        group,
                        total = results.Count,
                        passed = results.Count(result => result.Status == "PASS"),
                        failed = results.Count(result => result.Status == "FAIL"),
                        results
                    },
                    new JsonSerializerOptions { WriteIndented = true }));
        }

        if (selected.Count == 0)
        {
            Console.Error.WriteLine($"No tests matched filter '{filter}' and group '{group}'.");
            return 2;
        }

        return results.Any(result => result.Status == "FAIL") ? 1 : 0;
    }

    internal static IReadOnlyList<TestCase> Select(IEnumerable<TestCase> testCases, string? filter, string? group)
    {
        return testCases
            .Where(test => !test.ExplicitOnly ||
                (!string.IsNullOrWhiteSpace(group) &&
                 !string.IsNullOrWhiteSpace(test.Group) &&
                 NormalizeGroup(test.Group) == NormalizeGroup(group)))
            .Where(test => string.IsNullOrWhiteSpace(group) || MatchesGroup(test, group))
            .Where(test => string.IsNullOrWhiteSpace(filter) || test.Name.Contains(filter, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private static bool MatchesGroup(TestCase test, string group)
    {
        var normalized = NormalizeGroup(group);
        if (!string.IsNullOrWhiteSpace(test.Group))
        {
            return NormalizeGroup(test.Group) == normalized;
        }

        return normalized switch
        {
            "golden" => test.Name.StartsWith("P1C-GOLD-", StringComparison.OrdinalIgnoreCase),
            "phase1c" => test.Name.StartsWith("P1C-", StringComparison.OrdinalIgnoreCase),
            "phase3b" => test.Name.StartsWith("P3B-", StringComparison.OrdinalIgnoreCase),
            "phase3c" => test.Name.StartsWith("P3C-", StringComparison.OrdinalIgnoreCase),
            "phase3d" => test.Name.StartsWith("P3D-", StringComparison.OrdinalIgnoreCase),
            "phase3e" => test.Name.StartsWith("P3E-", StringComparison.OrdinalIgnoreCase),
            "phase4" => test.Name.StartsWith("P4-", StringComparison.OrdinalIgnoreCase),
            "phase5" => test.Name.StartsWith("P5-", StringComparison.OrdinalIgnoreCase),
            "phase6a" => test.Name.StartsWith("P6A-", StringComparison.OrdinalIgnoreCase),
            "phase6b" => test.Name.StartsWith("P6B-", StringComparison.OrdinalIgnoreCase),
            "phase6c" => test.Name.StartsWith("P6C-", StringComparison.OrdinalIgnoreCase),
            "phase7" => test.Name.StartsWith("P7-", StringComparison.OrdinalIgnoreCase),
            "phase7b" => test.Name.StartsWith("P7B-", StringComparison.OrdinalIgnoreCase),
            "phase7c" => test.Name.StartsWith("P7C-", StringComparison.OrdinalIgnoreCase),
            "phase9" => test.Name.StartsWith("P9-", StringComparison.OrdinalIgnoreCase),
            "phase10" => test.Name.StartsWith("P10-", StringComparison.OrdinalIgnoreCase),
            "phase8" => test.Name.StartsWith("P8-", StringComparison.OrdinalIgnoreCase),
            "phase8a" => test.Name.StartsWith("P8A-", StringComparison.OrdinalIgnoreCase),
            _ => false
        };
    }

    private static string NormalizeGroup(string group) => group.Trim().ToLowerInvariant();

    private static string? ReadArgument(IReadOnlyList<string> args, string name)
    {
        for (var index = 0; index < args.Count - 1; index++)
        {
            if (string.Equals(args[index], name, StringComparison.OrdinalIgnoreCase))
            {
                return args[index + 1];
            }
        }

        return null;
    }
}

internal static class TestAssert
{
    public static void True(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    public static void Equal<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{message} Expected: {expected}; actual: {actual}.");
        }
    }

    public static void Near(double expected, double actual, double tolerance, string message)
    {
        if (Math.Abs(expected - actual) > tolerance)
        {
            throw new InvalidOperationException($"{message} Expected: {expected}; actual: {actual}; tolerance: {tolerance}.");
        }
    }

    public static void Throws<TException>(Action action, string message)
        where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException($"{message} Expected {typeof(TException).Name}; actual {exception.GetType().Name}.", exception);
        }

        throw new InvalidOperationException($"{message} Expected {typeof(TException).Name}; no exception was thrown.");
    }
}
