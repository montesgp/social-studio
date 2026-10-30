using System.Reflection;
using DomainProfileAnalysis = SocialStudio.Domain.Social.ProfileAnalysis.ProfileAnalysis;
using SocialStudio.Domain.Social.SingleAnalyzer;

namespace SocialStudio.Tests.Domain.Social.ProfileAnalysis;

/// <summary>
/// Architecture isolation tests (PA-REG-01).
/// Asserts that Domain.Social.ProfileAnalysis and Domain.Social.SingleAnalyzer
/// have ZERO cross-namespace references — they must remain completely isolated aggregates.
/// </summary>
public sealed class ProfileAnalysisArchitectureTests
{
    private const string ProfileAnalysisNamespace = "SocialStudio.Domain.Social.ProfileAnalysis";
    private const string SingleAnalyzerNamespace = "SocialStudio.Domain.Social.SingleAnalyzer";

    private static readonly Assembly DomainAssembly =
        typeof(DomainProfileAnalysis).Assembly;

    [Fact]
    public void ProfileAnalysis_DomainTypes_DoNotReferenceAnyTypeFromSingleAnalyzerNamespace()
    {
        // Get all types declared in the ProfileAnalysis namespace
        var profileAnalysisTypes = DomainAssembly.GetTypes()
            .Where(t => t.Namespace?.StartsWith(ProfileAnalysisNamespace, StringComparison.Ordinal) == true)
            .ToList();

        Assert.NotEmpty(profileAnalysisTypes);

        var violations = new List<string>();

        foreach (var type in profileAnalysisTypes)
        {
            // Check fields
            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.NonPublic |
                                                  BindingFlags.Instance | BindingFlags.Static))
            {
                if (IsInSingleAnalyzerNamespace(field.FieldType))
                    violations.Add($"{type.FullName}.{field.Name}: field type {field.FieldType.FullName}");
            }

            // Check properties
            foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.NonPublic |
                                                    BindingFlags.Instance | BindingFlags.Static))
            {
                if (IsInSingleAnalyzerNamespace(prop.PropertyType))
                    violations.Add($"{type.FullName}.{prop.Name}: property type {prop.PropertyType.FullName}");
            }

            // Check method parameters and return types
            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic |
                                                   BindingFlags.Instance | BindingFlags.Static))
            {
                if (IsInSingleAnalyzerNamespace(method.ReturnType))
                    violations.Add($"{type.FullName}.{method.Name}: return type {method.ReturnType.FullName}");

                foreach (var param in method.GetParameters())
                {
                    if (IsInSingleAnalyzerNamespace(param.ParameterType))
                        violations.Add($"{type.FullName}.{method.Name}({param.Name}): param type {param.ParameterType.FullName}");
                }
            }
        }

        Assert.True(violations.Count == 0,
            $"ProfileAnalysis domain types reference SingleAnalyzer namespace (PA-REG-01 violation):\n" +
            string.Join("\n", violations));
    }

    [Fact]
    public void SingleAnalyzer_DomainTypes_DoNotReferenceAnyTypeFromProfileAnalysisNamespace()
    {
        var singleAnalyzerTypes = DomainAssembly.GetTypes()
            .Where(t => t.Namespace?.StartsWith(SingleAnalyzerNamespace, StringComparison.Ordinal) == true)
            .ToList();

        Assert.NotEmpty(singleAnalyzerTypes);

        var violations = new List<string>();

        foreach (var type in singleAnalyzerTypes)
        {
            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.NonPublic |
                                                  BindingFlags.Instance | BindingFlags.Static))
            {
                if (IsInProfileAnalysisNamespace(field.FieldType))
                    violations.Add($"{type.FullName}.{field.Name}: field type {field.FieldType.FullName}");
            }

            foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.NonPublic |
                                                    BindingFlags.Instance | BindingFlags.Static))
            {
                if (IsInProfileAnalysisNamespace(prop.PropertyType))
                    violations.Add($"{type.FullName}.{prop.Name}: property type {prop.PropertyType.FullName}");
            }

            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic |
                                                   BindingFlags.Instance | BindingFlags.Static))
            {
                if (IsInProfileAnalysisNamespace(method.ReturnType))
                    violations.Add($"{type.FullName}.{method.Name}: return type {method.ReturnType.FullName}");

                foreach (var param in method.GetParameters())
                {
                    if (IsInProfileAnalysisNamespace(param.ParameterType))
                        violations.Add($"{type.FullName}.{method.Name}({param.Name}): param type {param.ParameterType.FullName}");
                }
            }
        }

        Assert.True(violations.Count == 0,
            $"SingleAnalyzer domain types reference ProfileAnalysis namespace (PA-REG-01 violation):\n" +
            string.Join("\n", violations));
    }

    private static bool IsInSingleAnalyzerNamespace(Type type) =>
        type.Namespace?.StartsWith(SingleAnalyzerNamespace, StringComparison.Ordinal) == true;

    private static bool IsInProfileAnalysisNamespace(Type type) =>
        type.Namespace?.StartsWith(ProfileAnalysisNamespace, StringComparison.Ordinal) == true;
}
