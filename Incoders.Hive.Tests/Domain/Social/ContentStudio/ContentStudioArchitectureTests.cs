using System.Reflection;
using Incoders.Hive.Domain.Social.ContentStudio;

namespace Incoders.Hive.Tests.Domain.Social.ContentStudio;

/// <summary>
/// Architecture isolation tests (CS-10). ContentStudio domain types MUST reference no type from the
/// SingleAnalyzer or ContentGeneration namespaces — only soft Guids cross the boundary, so the editorial
/// surface stays source-agnostic and the generation pipeline stays immutable.
/// </summary>
public sealed class ContentStudioArchitectureTests
{
    private const string ContentStudioNamespace = "Incoders.Hive.Domain.Social.ContentStudio";
    private const string SingleAnalyzerNamespace = "Incoders.Hive.Domain.Social.SingleAnalyzer";
    private const string ContentGenerationNamespace = "Incoders.Hive.Domain.Social.ContentGeneration";

    private static readonly Assembly DomainAssembly = typeof(ContentItem).Assembly;

    [Fact]
    public void ContentStudio_DomainTypes_DoNotReferenceSingleAnalyzerOrContentGeneration()
    {
        var types = DomainAssembly.GetTypes()
            .Where(t => t.Namespace?.StartsWith(ContentStudioNamespace, StringComparison.Ordinal) == true)
            .ToList();

        Assert.NotEmpty(types);

        var violations = new List<string>();

        foreach (var type in types)
        {
            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.NonPublic |
                                                  BindingFlags.Instance | BindingFlags.Static))
            {
                if (IsForbidden(field.FieldType))
                    violations.Add($"{type.FullName}.{field.Name}: field type {field.FieldType.FullName}");
            }

            foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.NonPublic |
                                                    BindingFlags.Instance | BindingFlags.Static))
            {
                if (IsForbidden(prop.PropertyType))
                    violations.Add($"{type.FullName}.{prop.Name}: property type {prop.PropertyType.FullName}");
            }

            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic |
                                                   BindingFlags.Instance | BindingFlags.Static))
            {
                if (IsForbidden(method.ReturnType))
                    violations.Add($"{type.FullName}.{method.Name}: return type {method.ReturnType.FullName}");

                foreach (var param in method.GetParameters())
                {
                    if (IsForbidden(param.ParameterType))
                        violations.Add($"{type.FullName}.{method.Name}({param.Name}): param type {param.ParameterType.FullName}");
                }
            }
        }

        Assert.True(violations.Count == 0,
            "ContentStudio domain types reference a forbidden namespace (CS-10 violation):\n" +
            string.Join("\n", violations));
    }

    private static bool IsForbidden(Type type)
    {
        if (type.IsGenericType)
        {
            foreach (var argument in type.GetGenericArguments())
            {
                if (IsForbidden(argument))
                    return true;
            }
        }

        return type.Namespace is not null &&
               (type.Namespace.StartsWith(SingleAnalyzerNamespace, StringComparison.Ordinal) ||
                type.Namespace.StartsWith(ContentGenerationNamespace, StringComparison.Ordinal));
    }
}
