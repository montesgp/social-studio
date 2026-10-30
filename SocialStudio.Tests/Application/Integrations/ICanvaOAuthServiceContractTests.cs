using System.Reflection;
using SocialStudio.Application.Abstractions.Integrations;

namespace SocialStudio.Tests.Application.Integrations;

/// <summary>
/// WU-5 contract test: <see cref="ICanvaOAuthService"/> must not expose any
/// Infrastructure or provider types in its public method signatures.
///
/// Mirrors the pattern established by <see cref="ICanvaUserTokenRepositoryContractTests"/>.
/// </summary>
public sealed class ICanvaOAuthServiceContractTests
{
    [Fact]
    public void Port_HasNoInfrastructureTypes()
    {
        var appAssembly = typeof(ICanvaOAuthService).Assembly;

        var portType = appAssembly.GetExportedTypes()
            .FirstOrDefault(t => t.Name == "ICanvaOAuthService");

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
}
