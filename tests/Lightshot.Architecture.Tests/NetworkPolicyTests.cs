using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Lightshot.TestSupport;
using Xunit;

namespace Lightshot.Architecture.Tests;

public class NetworkPolicyTests
{
    private const string AllowedNamespace = "Lightshot.Platform.Windows.Updates";

    [Fact]
    [Unit]
    public void OnlyUpdatesNamespaceUsesNetwork()
    {
        var assembliesToScan = new[]
        {
            typeof(Lightshot.Core.CoreMarker).Assembly,
            typeof(Lightshot.Rendering.RenderingMarker).Assembly,
            typeof(Lightshot.Platform.Windows.Updates.UpdateChecker).Assembly,
            typeof(Lightshot.App.Updates.UpdatePolicy).Assembly
        };

        bool foundNetworkInUpdates = false;
        var violations = new List<string>();

        foreach (var assembly in assembliesToScan)
        {
            foreach (var type in assembly.GetTypes())
            {
                bool isAllowed = type.Namespace != null &&
                    (type.Namespace == AllowedNamespace || type.Namespace.StartsWith(AllowedNamespace + ".", StringComparison.Ordinal));

                var networkRefs = GetReferencedNetworkTypes(type);
                if (networkRefs.Count > 0)
                {
                    if (isAllowed)
                    {
                        foundNetworkInUpdates = true;
                    }
                    else
                    {
                        violations.Add($"{type.FullName} references network type(s): {string.Join(", ", networkRefs)}");
                    }
                }
            }
        }

        // Gate 4: scanner must find at least one network reference inside the Updates namespace (proves scan works)
        Assert.True(foundNetworkInUpdates, $"Scanner must find at least one network reference inside {AllowedNamespace} to prove the scanner works.");

        // NetworkPolicy: no code outside Updates is permitted to reference network types
        Assert.Empty(violations);
    }

    private static HashSet<string> GetReferencedNetworkTypes(Type type)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);

        void CheckType(Type? t)
        {
            if (t == null) return;

            if (IsNetworkType(t))
            {
                result.Add(t.FullName ?? t.Name);
            }

            if (t.IsGenericType)
            {
                foreach (var arg in t.GetGenericArguments())
                {
                    CheckType(arg);
                }
            }

            if (t.HasElementType)
            {
                CheckType(t.GetElementType());
            }
        }

        CheckType(type.BaseType);
        foreach (var iface in type.GetInterfaces())
        {
            CheckType(iface);
        }

        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

        foreach (var field in type.GetFields(flags))
        {
            CheckType(field.FieldType);
        }

        foreach (var prop in type.GetProperties(flags))
        {
            CheckType(prop.PropertyType);
        }

        foreach (var method in type.GetMethods(flags))
        {
            CheckType(method.ReturnType);
            foreach (var param in method.GetParameters())
            {
                CheckType(param.ParameterType);
            }
            if (method.IsGenericMethod)
            {
                foreach (var arg in method.GetGenericArguments())
                {
                    CheckType(arg);
                }
            }

            try
            {
                var body = method.GetMethodBody();
                if (body != null)
                {
                    foreach (var local in body.LocalVariables)
                    {
                        CheckType(local.LocalType);
                    }
                }
            }
            catch
            {
                // Dynamic or platform-specific methods may throw
            }
        }

        foreach (var ctor in type.GetConstructors(flags))
        {
            foreach (var param in ctor.GetParameters())
            {
                CheckType(param.ParameterType);
            }

            try
            {
                var body = ctor.GetMethodBody();
                if (body != null)
                {
                    foreach (var local in body.LocalVariables)
                    {
                        CheckType(local.LocalType);
                    }
                }
            }
            catch
            {
            }
        }

        return result;
    }

    private static bool IsNetworkType(Type type)
    {
        string? ns = type.Namespace;
        if (string.IsNullOrEmpty(ns)) return false;

        return ns == "System.Net" || ns.StartsWith("System.Net.", StringComparison.Ordinal);
    }
}
