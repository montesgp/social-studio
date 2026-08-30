using System.Collections;
using System.Reflection;
using Incoders.Hive.Application.Features.Social.NicheDiscovery.Analytics;

namespace Incoders.Hive.Tests.Architecture;

/// <summary>
/// WU-10 (guard b, design §3): every public property of every type in
/// <c>…NicheDiscovery.Semantics</c> is <c>string</c>, <c>bool</c>, an <c>enum</c>, or a collection of
/// those — no numeric type anywhere. The AI cannot emit a number because the contract has nowhere to
/// put one.
///
/// Written by reflection over the <c>NicheDiscovery.Semantics</c> NAMESPACE STRING — not a direct
/// <c>typeof</c> reference to any real Semantics type — so this file compiles and is vacuously green
/// (zero types found, zero violations) before task 10.3 lands the real types. It MUST be re-run after
/// 10.3 to confirm it turns MEANINGFULLY green (the real types are found and still carry zero
/// violations), not just "no types found".
/// </summary>
public sealed class NicheDiscoverySemanticsNoNumericGuardTests
{
    [Fact]
    public void SemanticsTypes_HaveNoNumericPublicProperty()
    {
        // Any already-existing Application type anchors the assembly reference without naming a
        // Semantics type directly.
        var applicationAssembly = typeof(DeterministicMetricCalculator).Assembly;

        var semanticsTypes = applicationAssembly.GetTypes()
            .Where(t => t.Namespace is not null && t.Namespace.Contains("NicheDiscovery.Semantics", StringComparison.Ordinal))
            .ToList();

        var violations = new List<string>();

        foreach (var type in semanticsTypes)
        {
            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                if (!IsAllowed(property.PropertyType, []))
                {
                    violations.Add($"{type.Name}.{property.Name} ({property.PropertyType.Name})");
                }
            }
        }

        Assert.Empty(violations);
    }

    /// <summary>
    /// A property type is allowed when it is <c>string</c>/<c>bool</c>/an <c>enum</c>, a collection of
    /// those, OR another type declared in the SAME <c>NicheDiscovery.Semantics</c> namespace whose OWN
    /// properties recursively satisfy this same rule (e.g. an outcome wrapper nesting the annotation
    /// contract). Numeric leakage is still caught however deep it is nested; <paramref name="visiting"/>
    /// guards against infinite recursion on a self-referential type.
    /// </summary>
    private static bool IsAllowed(Type type, HashSet<Type> visiting)
    {
        var underlying = Nullable.GetUnderlyingType(type) ?? type;

        if (underlying == typeof(string) || underlying == typeof(bool) || underlying.IsEnum)
        {
            return true;
        }

        if (typeof(IEnumerable).IsAssignableFrom(underlying) && underlying != typeof(string))
        {
            var elementType = underlying.IsGenericType ? underlying.GetGenericArguments().FirstOrDefault() : null;
            return elementType is null || IsAllowed(elementType, visiting);
        }

        if (underlying.Namespace is not null
            && underlying.Namespace.Contains("NicheDiscovery.Semantics", StringComparison.Ordinal)
            && visiting.Add(underlying))
        {
            return underlying.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .All(p => IsAllowed(p.PropertyType, visiting));
        }

        return false;
    }
}
