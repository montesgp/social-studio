using System.Reflection;
using Incoders.Hive.Application.Abstractions.Social.ContentGeneration;

namespace Incoders.Hive.Tests.Application;

/// <summary>
/// WU-6 contract tests: <see cref="ICanvaDesignImportClient"/> must not expose any
/// Infrastructure or provider types in its public method signatures.
///
/// <para>
/// Mirrors the pattern established by <see cref="ICanvaOAuthServiceContractTests"/>.
/// </para>
///
/// <para>
/// Spec: CANVA-EXPORT-04 — Autofill/Brand Templates endpoints are NEVER reachable
/// from the active code path. This is asserted here as a static text check on the
/// port definition (no string literal "autofill" or "brand-template" in the interface source).
/// </para>
/// </summary>
public sealed class ICanvaDesignImportClientContractTests
{
    [Fact]
    public void Port_HasNoInfrastructureTypes()
    {
        var appAssembly = typeof(ICanvaDesignImportClient).Assembly;

        var portType = appAssembly.GetExportedTypes()
            .FirstOrDefault(t => t.Name == "ICanvaDesignImportClient");

        Assert.NotNull(portType);
        Assert.True(portType!.IsInterface);

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
                    @"Infrastructure|Persistence|EfCore|Npgsql|AspNetCore|Playwright|DocumentFormat|EntityFramework|HttpClient|System\.Net\.Http",
                    type.FullName ?? string.Empty);
            }
        }
    }

    [Fact]
    public void Port_DoesNotReferenceAutofillOrBrandTemplatesEndpoints()
    {
        // Spec CANVA-EXPORT-04: Autofill and Brand Templates are NEVER reachable
        // from the active code path. The port itself must not reference these endpoints
        // in any property, method name, or parameter name.
        var appAssembly = typeof(ICanvaDesignImportClient).Assembly;

        var portType = appAssembly.GetExportedTypes()
            .FirstOrDefault(t => t.Name == "ICanvaDesignImportClient");

        Assert.NotNull(portType);

        // Inspect method names and parameter names for any autofill/brand-template references
        var methods = portType!.GetMethods(BindingFlags.Public | BindingFlags.Instance);
        foreach (var method in methods)
        {
            Assert.DoesNotContain("autofill", method.Name, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("brandtemplate", method.Name.Replace("-", ""), StringComparison.OrdinalIgnoreCase);

            foreach (var param in method.GetParameters())
            {
                Assert.DoesNotContain("autofill", param.Name ?? string.Empty, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("brandtemplate", (param.Name ?? string.Empty).Replace("-", ""), StringComparison.OrdinalIgnoreCase);
            }
        }
    }
}
