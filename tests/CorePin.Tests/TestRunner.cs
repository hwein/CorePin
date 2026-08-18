using System.Reflection;
using System.Text.Json;

namespace CorePin.Tests;

public static class TestRunner
{
    public static int Main(string[] args)
    {
        string? filter = args.Length > 0 ? args[0] : null;

        List<(Type Type, MethodInfo Method)> tests;
        try
        {
            tests = Discover(filter);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("RUNNER ERROR  " + ex);
            return 2;
        }

        // A filter that matches nothing is a runner error, not a green run: a typo in the
        // filter would otherwise be indistinguishable from "everything passed".
        // Without a filter an empty run stays exit code 0.
        if (filter is not null && tests.Count == 0)
        {
            Console.Error.WriteLine($"RUNNER ERROR  filter '{filter}' matched no test");
            return 2;
        }

        int passed = 0;
        var failures = new List<string>();

        foreach (var (type, method) in tests)
        {
            string name = $"{type.Name}.{method.Name}";
            try
            {
                method.Invoke(null, null);
                Console.WriteLine($"PASS  {name}");
                passed++;
            }
            catch (TargetInvocationException ex)
            {
                var inner = ex.InnerException ?? ex;
                Console.WriteLine($"FAIL  {name}");
                Console.WriteLine("      " + inner.Message.Replace("\n", "\n      ", StringComparison.Ordinal));
                failures.Add(name);
            }
        }

        Console.WriteLine($"{passed} passed, {failures.Count} failed, {tests.Count} total");
        foreach (var name in failures) Console.WriteLine("FAILED  " + name);
        return failures.Count == 0 ? 0 : 1;
    }

    private static List<(Type, MethodInfo)> Discover(string? filter)
    {
        var result = new List<(Type, MethodInfo)>();
        foreach (var type in Assembly.GetExecutingAssembly().GetTypes()
                     .Where(t => t.Name.EndsWith("Tests", StringComparison.Ordinal))
                     .OrderBy(t => t.Name, StringComparer.Ordinal))
        {
            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Static)
                         .Where(m => m.Name.StartsWith("Test_", StringComparison.Ordinal)
                                     && m.ReturnType == typeof(void)
                                     && m.GetParameters().Length == 0)
                         .OrderBy(m => m.Name, StringComparer.Ordinal))
            {
                string name = $"{type.Name}.{method.Name}";
                if (filter is null || name.Contains(filter, StringComparison.OrdinalIgnoreCase))
                    result.Add((type, method));
            }
        }
        return result;
    }
}

public sealed class AssertionException(string message) : Exception(message);

public static class Assert
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    public static void True(bool condition, string because)
    {
        if (!condition) throw new AssertionException(because);
    }

    public static void Equal<T>(T expected, T actual, string because)
    {
        if (EqualityComparer<T>.Default.Equals(expected, actual)) return;
        throw new AssertionException(
            $"{because}\nexpected: {ToJson(expected)}\nactual:   {ToJson(actual)}");
    }

    public static void Throws<TException>(Action action, string because) where TException : Exception
    {
        try { action(); }
        catch (TException) { return; }
        catch (Exception ex)
        {
            throw new AssertionException($"{because}\nexpected: {typeof(TException).Name}\nactual:   {ex.GetType().Name}");
        }
        throw new AssertionException($"{because}\nexpected: {typeof(TException).Name}\nactual:   no exception");
    }

    private static string ToJson<T>(T value)
    {
        try { return JsonSerializer.Serialize(value, Json); }
        catch (NotSupportedException) { return value?.ToString() ?? "null"; }
    }
}

/// Self-deleting temporary directory for tests that touch the file system.
public sealed class TempDir : IDisposable
{
    public TempDir()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "corepin-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public void Dispose()
    {
        // A leftover directory under %TEMP% is not worth failing a test over: the log file
        // handle may still be held by a writer thread that outlived its Dispose timeout.
        try { Directory.Delete(Path, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}
