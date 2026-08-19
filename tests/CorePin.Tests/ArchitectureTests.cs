using System.Reflection;
using CorePin.Core.Topology;

namespace CorePin.Tests;

public static class ArchitectureTests
{
    /// Which CorePin.Core module may use which — the matrix the dependency test enforces.
    private static readonly Dictionary<string, string[]> Allowed = new(StringComparer.Ordinal)
    {
        ["Primitives"] = [],
        ["Time"] = [],
        ["Paths"] = [],
        ["Diagnostics"] = ["Primitives", "Time"],
        ["Topology"] = ["Primitives"],
        ["Layout"] = ["Primitives", "Topology"],
        ["Rules"] = ["Primitives"],
        ["Platform"] = ["Primitives", "Topology"],
        ["Configuration"] = ["Primitives", "Time", "Rules", "Diagnostics"],
        ["Engine"] = ["Primitives", "Time", "Rules", "Platform", "Diagnostics"],
        ["ViewModel"] = ["Primitives", "Rules", "Configuration", "Engine", "Diagnostics"],
    };

    private const string CoreNamespace = "CorePin.Core";

    private static Assembly CoreAssembly => typeof(ClusterBuilder).Assembly;

    public static void Test_CoreHasNoDeclaredPInvoke()
    {
        var offenders = new List<string>();
        foreach (var type in CoreAssembly.GetTypes())
        {
            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic
                                                   | BindingFlags.Instance | BindingFlags.Static
                                                   | BindingFlags.DeclaredOnly))
            {
                if (method.Attributes.HasFlag(MethodAttributes.PinvokeImpl))
                    offenders.Add($"{type.FullName}.{method.Name}");
            }
        }

        Assert.Equal(string.Empty, string.Join(", ", offenders),
            "CorePin.Core must not contain a declared P/Invoke");
    }

    public static void Test_ModuleDependenciesMatch_2_5()
    {
        var violations = new List<string>();

        foreach (var type in CoreAssembly.GetTypes())
        {
            string? module = ModuleOf(type);
            if (module is null) continue;                       // not inside CorePin.Core.*

            if (!Allowed.TryGetValue(module, out var allowed))
            {
                violations.Add($"unknown module '{module}' ({type.FullName})");
                continue;
            }

            foreach (var referenced in ReferencedTypes(type))
            {
                string? other = ModuleOf(referenced);
                if (other is null || other == module) continue;
                if (!Allowed.ContainsKey(other))
                {
                    violations.Add($"{type.FullName} -> unknown module '{other}'");
                    continue;
                }
                if (!allowed.Contains(other, StringComparer.Ordinal))
                    violations.Add($"{module} must not use {other} ({type.FullName} -> {referenced.FullName})");
            }
        }

        Assert.Equal(string.Empty, string.Join(" | ", violations.Distinct(StringComparer.Ordinal)),
            "module dependencies must match the Allowed matrix");
    }

    /// Namespaces below CorePin.Core.<Module> belong to that module. A type directly in
    /// CorePin.Core has no module and is reported as unknown.
    private static string? ModuleOf(Type type)
    {
        string? ns = type.Namespace;
        if (ns is null || !ns.StartsWith(CoreNamespace, StringComparison.Ordinal)) return null;
        if (ns.Length == CoreNamespace.Length) return "<root>";
        if (ns[CoreNamespace.Length] != '.') return null;       // e.g. CorePin.CoreSomething

        string rest = ns[(CoreNamespace.Length + 1)..];
        int dot = rest.IndexOf('.', StringComparison.Ordinal);
        return dot < 0 ? rest : rest[..dot];
    }

    private static IEnumerable<Type> ReferencedTypes(Type type)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic
                                   | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

        if (type.BaseType is { } baseType)
            foreach (var t in Flatten(baseType)) yield return t;

        foreach (var field in type.GetFields(flags))
            foreach (var t in Flatten(field.FieldType)) yield return t;

        foreach (var property in type.GetProperties(flags))
            foreach (var t in Flatten(property.PropertyType)) yield return t;

        foreach (var method in type.GetMethods(flags))
        {
            foreach (var t in Flatten(method.ReturnType)) yield return t;
            foreach (var parameter in method.GetParameters())
                foreach (var t in Flatten(parameter.ParameterType)) yield return t;
        }

        foreach (var constructor in type.GetConstructors(flags))
            foreach (var parameter in constructor.GetParameters())
                foreach (var t in Flatten(parameter.ParameterType)) yield return t;
    }

    private static IEnumerable<Type> Flatten(Type type)
    {
        if (type.GetElementType() is { } element)
        {
            foreach (var t in Flatten(element)) yield return t;
            yield break;
        }

        yield return type;

        if (type.IsGenericType)
            foreach (var argument in type.GetGenericArguments())
                foreach (var t in Flatten(argument)) yield return t;
    }
}
