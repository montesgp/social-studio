using System.Reflection;
using SocialStudio.Application.Abstractions.Social.ContentGeneration;
using SocialStudio.Domain.Social.ContentGeneration;

namespace SocialStudio.Tests.Application;

/// <summary>
/// WU-1 RED contract tests: IClientAestheticImageBackgroundService is a pure Application port.
/// BuildCreativeBackgroundsAsync returns Task&lt;IReadOnlyDictionary&lt;int, BackgroundSelection.ForcedDefault&gt;&gt;.
/// ClientAestheticImageRequest contains only primitives, domain types, and ClientAestheticProfile.
/// Refs: design §3.3; tasks 1.3.
/// </summary>
public sealed class IClientAestheticImageBackgroundServiceContractTests
{
    private static readonly Assembly AppAssembly = typeof(IClientAestheticImageBackgroundService).Assembly;

    private static readonly string[] ForbiddenNamespacePatterns =
    [
        "OpenAI", "Azure.AI", "Anthropic", "Infrastructure", "Persistence",
        "EfCore", "Npgsql", "Supabase", "AspNetCore", "Playwright", "System.Net.Http"
    ];

    // -----------------------------------------------------------------------
    // IClientAestheticImageBackgroundService exists as a public interface
    // -----------------------------------------------------------------------

    [Fact]
    public void IClientAestheticImageBackgroundService_ExistsAsPublicInterface()
    {
        var type = AppAssembly.GetExportedTypes()
            .FirstOrDefault(t => t.Name == "IClientAestheticImageBackgroundService");

        Assert.NotNull(type);
        Assert.True(type!.IsInterface);
    }

    // -----------------------------------------------------------------------
    // BuildCreativeBackgroundsAsync returns correct type
    // -----------------------------------------------------------------------

    [Fact]
    public void BuildCreativeBackgroundsAsync_HasCorrectReturnType()
    {
        var method = typeof(IClientAestheticImageBackgroundService)
            .GetMethod("BuildCreativeBackgroundsAsync");
        Assert.NotNull(method);

        var returnType = method!.ReturnType;
        // Task<IReadOnlyDictionary<int, BackgroundSelection.ForcedDefault>>
        Assert.True(returnType.IsGenericType);
        Assert.Equal(typeof(Task<>), returnType.GetGenericTypeDefinition());

        var innerType = returnType.GetGenericArguments()[0];
        Assert.True(innerType.IsGenericType);
        Assert.Equal(typeof(IReadOnlyDictionary<,>), innerType.GetGenericTypeDefinition());

        var keyType = innerType.GetGenericArguments()[0];
        var valueType = innerType.GetGenericArguments()[1];
        Assert.Equal(typeof(int), keyType);
        Assert.Equal(typeof(BackgroundSelection.ForcedDefault), valueType);
    }

    [Fact]
    public void BuildCreativeBackgroundsAsync_HasNoInfrastructureTypes()
    {
        var method = typeof(IClientAestheticImageBackgroundService)
            .GetMethod("BuildCreativeBackgroundsAsync");
        Assert.NotNull(method);

        var allTypes = method!.GetParameters()
            .Select(p => p.ParameterType)
            .Append(method.ReturnType)
            .SelectMany(ExpandGenericArguments)
            .ToList();

        foreach (var type in allTypes)
        {
            var fullName = type.FullName ?? string.Empty;
            foreach (var pattern in ForbiddenNamespacePatterns)
            {
                Assert.False(
                    fullName.Contains(pattern, StringComparison.Ordinal),
                    $"Forbidden pattern '{pattern}' found in type '{fullName}'");
            }
        }
    }

    // -----------------------------------------------------------------------
    // ClientAestheticImageRequest exists and has only safe types
    // -----------------------------------------------------------------------

    [Fact]
    public void ClientAestheticImageRequest_ExistsAsRecord()
    {
        var type = AppAssembly.GetExportedTypes()
            .FirstOrDefault(t => t.Name == "ClientAestheticImageRequest");

        Assert.NotNull(type);
        Assert.True(type!.IsSealed);
    }

    [Fact]
    public void ClientAestheticImageRequest_HasNoInfrastructureTypes()
    {
        var requestType = AppAssembly.GetExportedTypes()
            .FirstOrDefault(t => t.Name == "ClientAestheticImageRequest");

        Assert.NotNull(requestType);

        var propertyTypes = requestType!.GetProperties()
            .Select(p => p.PropertyType)
            .SelectMany(ExpandGenericArguments)
            .ToList();

        foreach (var type in propertyTypes)
        {
            var fullName = type.FullName ?? string.Empty;
            foreach (var pattern in ForbiddenNamespacePatterns)
            {
                Assert.False(
                    fullName.Contains(pattern, StringComparison.Ordinal),
                    $"Forbidden pattern '{pattern}' found in property type '{fullName}' of ClientAestheticImageRequest");
            }
        }
    }

    [Fact]
    public void ClientAestheticImageRequest_HasExpectedProperties()
    {
        var requestType = AppAssembly.GetExportedTypes()
            .FirstOrDefault(t => t.Name == "ClientAestheticImageRequest");
        Assert.NotNull(requestType);

        var props = requestType!.GetProperties().Select(p => p.Name).ToHashSet();
        Assert.Contains("OrganizationId", props);
        Assert.Contains("ClientId", props);
        Assert.Contains("JobId", props);
        Assert.Contains("FormatResultId", props);
        Assert.Contains("FormatKey", props);
        Assert.Contains("SlideCount", props);
        Assert.Contains("CreativeSlideIndices", props);
        Assert.Contains("Aesthetic", props);
        Assert.Contains("PerSlideCopySummaries", props);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static IEnumerable<Type> ExpandGenericArguments(Type t)
    {
        if (t.IsGenericType)
        {
            foreach (var arg in t.GetGenericArguments())
            foreach (var inner in ExpandGenericArguments(arg))
                yield return inner;
        }

        yield return t;
    }
}
