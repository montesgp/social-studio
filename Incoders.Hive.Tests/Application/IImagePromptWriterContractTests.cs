using System.Reflection;
using Incoders.Hive.Application.Abstractions.Social.ContentGeneration;
using Incoders.Hive.Domain.Common;

namespace Incoders.Hive.Tests.Application;

/// <summary>
/// WU-1 RED contract tests: IImagePromptWriter is a pure Application port — no provider types
/// in any signature. ImagePromptRequest carries only Guid, int, string, and ClientAestheticProfile.
/// Refs: design §3.2; tasks 1.2.
/// </summary>
public sealed class IImagePromptWriterContractTests
{
    private static readonly Assembly AppAssembly = typeof(IImagePromptWriter).Assembly;

    private static readonly string[] ForbiddenNamespacePatterns =
    [
        "OpenAI", "Azure.AI", "Anthropic", "Infrastructure", "Persistence",
        "EfCore", "Npgsql", "Supabase", "AspNetCore", "Playwright", "System.Net.Http"
    ];

    // -----------------------------------------------------------------------
    // IImagePromptWriter exists as a public interface
    // -----------------------------------------------------------------------

    [Fact]
    public void IImagePromptWriter_ExistsAsPublicInterface()
    {
        var type = AppAssembly.GetExportedTypes()
            .FirstOrDefault(t => t.Name == "IImagePromptWriter");

        Assert.NotNull(type);
        Assert.True(type!.IsInterface);
    }

    // -----------------------------------------------------------------------
    // WriteAsync(ImagePromptRequest, CancellationToken) returns Task<Result<string>>
    // -----------------------------------------------------------------------

    [Fact]
    public void IImagePromptWriter_WriteAsync_HasCorrectReturnType()
    {
        var method = typeof(IImagePromptWriter).GetMethod("WriteAsync");
        Assert.NotNull(method);

        var returnType = method!.ReturnType;
        Assert.True(returnType.IsGenericType, "Return type must be generic Task<...>");
        Assert.Equal(typeof(Task<>), returnType.GetGenericTypeDefinition());

        var innerType = returnType.GetGenericArguments()[0];
        Assert.True(innerType.IsGenericType);
        Assert.Equal(typeof(Result<>), innerType.GetGenericTypeDefinition());
        Assert.Equal(typeof(string), innerType.GetGenericArguments()[0]);
    }

    [Fact]
    public void IImagePromptWriter_WriteAsync_HasNoInfrastructureTypes()
    {
        var method = typeof(IImagePromptWriter).GetMethod("WriteAsync");
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
                    $"Forbidden pattern '{pattern}' in type '{fullName}'");
            }
        }
    }

    // -----------------------------------------------------------------------
    // ImagePromptRequest carries only safe types (Guid, int, string, ClientAestheticProfile)
    // -----------------------------------------------------------------------

    [Fact]
    public void ImagePromptRequest_ExistsAsRecord()
    {
        var type = AppAssembly.GetExportedTypes()
            .FirstOrDefault(t => t.Name == "ImagePromptRequest");

        Assert.NotNull(type);
        Assert.True(type!.IsSealed);
    }

    [Fact]
    public void ImagePromptRequest_HasNoInfrastructureTypes()
    {
        var requestType = AppAssembly.GetExportedTypes()
            .FirstOrDefault(t => t.Name == "ImagePromptRequest");

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
                    $"Forbidden pattern '{pattern}' in property type '{fullName}'");
            }
        }
    }

    [Fact]
    public void ImagePromptRequest_HasExpectedProperties()
    {
        var requestType = AppAssembly.GetExportedTypes()
            .FirstOrDefault(t => t.Name == "ImagePromptRequest");
        Assert.NotNull(requestType);

        var props = requestType!.GetProperties().Select(p => p.Name).ToHashSet();
        Assert.Contains("OrganizationId", props);
        Assert.Contains("ClientId", props);
        Assert.Contains("SlideIndex", props);
        Assert.Contains("SlideCopySummary", props);
        Assert.Contains("Aesthetic", props);
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
