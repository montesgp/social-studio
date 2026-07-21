using System.Reflection;
using Incoders.Hive.Domain.Social.Publications;

namespace Incoders.Hive.Tests.Domain.Social.Publications;

/// <summary>
/// Architecture-style guard (PUB-06): the publication axis is orthogonal to Content Studio by
/// construction — it must never reference any <c>Incoders.Hive.Domain.Social.ContentStudio</c> type.
/// Mirrors the CS orthogonality discipline via a reflection-based type-reference check (no source-text
/// parsing dependency needed at the assembly level).
/// </summary>
public sealed class PublicationOrthogonalityTests
{
    [Fact]
    public void PublicationsNamespace_ReferencesNoContentStudioType()
    {
        var assembly = typeof(Publication).Assembly;
        var publicationTypes = assembly.GetTypes()
            .Where(t => t.Namespace == "Incoders.Hive.Domain.Social.Publications")
            .ToList();

        Assert.NotEmpty(publicationTypes);

        foreach (var type in publicationTypes)
        {
            AssertNoContentStudioReference(type);
        }
    }

    private static void AssertNoContentStudioReference(Type type)
    {
        const string forbiddenNamespace = "Incoders.Hive.Domain.Social.ContentStudio";

        foreach (var member in type.GetMembers(
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static
            | BindingFlags.DeclaredOnly))
        {
            var referencedTypes = member switch
            {
                PropertyInfo p => [p.PropertyType],
                FieldInfo f => new[] { f.FieldType },
                MethodInfo m => m.GetParameters().Select(x => x.ParameterType)
                    .Append(m.ReturnType)
                    .ToArray(),
                ConstructorInfo c => c.GetParameters().Select(x => x.ParameterType).ToArray(),
                _ => Array.Empty<Type>(),
            };

            foreach (var referenced in referencedTypes)
            {
                Assert.False(
                    referenced.Namespace?.StartsWith(forbiddenNamespace, StringComparison.Ordinal) == true,
                    $"{type.FullName}.{member.Name} references forbidden type {referenced.FullName}.");
            }
        }
    }
}
