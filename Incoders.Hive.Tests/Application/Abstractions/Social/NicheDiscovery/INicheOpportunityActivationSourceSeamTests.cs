using System.Reflection;
using Incoders.Hive.Application.Abstractions.Social.NicheDiscovery;
using Incoders.Hive.Domain.Social.NicheDiscovery.Taxonomy;

namespace Incoders.Hive.Tests.Application.Abstractions.Social.NicheDiscovery;

/// <summary>
/// ND-17: the Content Generator seam is NAMED, not built. This change must not implement it and must
/// not introduce a reference from NicheDiscovery to a ContentGeneration type, in either direction.
/// </summary>
public sealed class INicheOpportunityActivationSourceSeamTests
{
    [Fact]
    public void GetActivatablePatternsAsync_HasTheExactShapeFromDesignSection12()
    {
        var method = typeof(INicheOpportunityActivationSource).GetMethod("GetActivatablePatternsAsync");

        Assert.NotNull(method);
        Assert.Equal(typeof(Task<IReadOnlyList<ActivatableContentPattern>>), method!.ReturnType);

        var parameters = method.GetParameters();
        Assert.Equal(typeof(Guid), parameters[0].ParameterType);
        Assert.Equal(typeof(Guid), parameters[1].ParameterType);
        Assert.Equal(typeof(CancellationToken), parameters[2].ParameterType);
    }

    [Fact]
    public void ActivatableContentPattern_HasTheExactShapeFromDesignSection12()
    {
        var properties = typeof(ActivatableContentPattern).GetProperties();

        Assert.Contains(properties, p => p.Name == "Pattern" && p.PropertyType == typeof(ContentPatternKind));
        Assert.Contains(properties, p => p.Name == "TopicLabels" && p.PropertyType == typeof(IReadOnlyList<string>));
        Assert.Contains(properties, p => p.Name == "TargetMarketCode" && p.PropertyType == typeof(string));
        Assert.Contains(properties, p => p.Name == "SubNicheLabel" && p.PropertyType == typeof(string));
        Assert.Contains(properties, p => p.Name == "OpportunityScore" && p.PropertyType == typeof(double));
        Assert.Contains(properties, p => p.Name == "ConfidenceScore" && p.PropertyType == typeof(double));
    }

    [Fact]
    public void NoTypeUnderApplicationAbstractionsNicheDiscovery_ReferencesAContentGenerationType()
    {
        var assembly = typeof(INicheOpportunityActivationSource).Assembly;
        var offenders = new List<string>();

        var nicheDiscoveryTypes = assembly.GetTypes()
            .Where(t => t.Namespace is not null &&
                        t.Namespace.StartsWith(
                            "Incoders.Hive.Application.Abstractions.Social.NicheDiscovery",
                            StringComparison.Ordinal));

        foreach (var type in nicheDiscoveryTypes)
        {
            foreach (var member in GetReferencedTypes(type))
            {
                if (member.Namespace is not null &&
                    member.Namespace.Contains("ContentGeneration", StringComparison.Ordinal))
                {
                    offenders.Add($"{type.FullName} references {member.FullName}");
                }
            }
        }

        Assert.True(offenders.Count == 0, "NicheDiscovery must not reference ContentGeneration (ND-17):\n" + string.Join('\n', offenders));
    }

    private static IEnumerable<Type> GetReferencedTypes(Type type)
    {
        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
        {
            yield return property.PropertyType;
        }

        foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
        {
            yield return method.ReturnType;
            foreach (var parameter in method.GetParameters())
            {
                yield return parameter.ParameterType;
            }
        }
    }
}
