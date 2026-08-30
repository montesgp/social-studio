using System.Reflection;
using Incoders.Hive.Application.Abstractions.Social.ContentGeneration;

namespace Incoders.Hive.Tests.Application;

/// <summary>
/// B3 WU-2 TDD contract test for <see cref="IPptxExportAdapter"/>.
///
/// <para>
/// Architecture gate: the Application port MUST NOT reference any Infrastructure
/// or provider-specific types (<c>DocumentFormat.OpenXml</c>, <c>Playwright</c>,
/// <c>AspNetCore</c>, EF Core, Npgsql, etc.). Only Domain records and BCL types
/// are allowed in parameter and return type signatures.
/// </para>
///
/// Mirrors the pattern established by <c>IVisualDesignerContractTests.Port_HasNoInfrastructureTypes</c>.
/// </summary>
public sealed class IPptxExportAdapterContractTests
{
    [Fact]
    public void Port_HasNoInfrastructureTypes()
    {
        // GIVEN the Application assembly
        var appAssembly = typeof(IPptxExportAdapter).Assembly;

        // WHEN its exported types are inspected
        var portType = appAssembly.GetExportedTypes()
            .FirstOrDefault(t => t.Name == "IPptxExportAdapter");

        // THEN IPptxExportAdapter is present as a public interface
        Assert.NotNull(portType);
        Assert.True(portType!.IsInterface);

        // AND its method signatures contain no Infrastructure/provider types
        var methods = portType.GetMethods(BindingFlags.Public | BindingFlags.Instance);
        foreach (var method in methods)
        {
            var allTypes = method.GetParameters()
                .Select(p => p.ParameterType)
                .Append(method.ReturnType)
                .SelectMany(t => t.IsGenericType ? t.GetGenericArguments().Append(t) : new[] { t })
                .ToList();

            foreach (var type in allTypes)
            {
                Assert.DoesNotMatch(
                    @"Infrastructure|Persistence|EfCore|Npgsql|AspNetCore|Playwright|DocumentFormat",
                    type.FullName ?? string.Empty);
            }
        }
    }
}
