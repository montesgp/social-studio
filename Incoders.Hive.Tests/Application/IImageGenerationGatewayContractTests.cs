using System.Reflection;
using Incoders.Hive.Application.Abstractions.Ai;
using Incoders.Hive.Domain.Common;

namespace Incoders.Hive.Tests.Application;

/// <summary>
/// WU-1 RED contract tests: IImageGenerationGateway and IImageModelClient are pure Application
/// ports — no OpenAI, HttpClient, Supabase, or EF types in any signature.
/// All request/result records contain only primitives, byte[], and Application-layer types.
/// Mirrors IVisualDesignerContractTests and IBackgroundSourcingStrategyContractTests pattern.
/// Refs: IMG-01, IMG-08; design §3.1, §6; tasks 1.1.
/// </summary>
public sealed class IImageGenerationGatewayContractTests
{
    private static readonly Assembly AppAssembly = typeof(IImageGenerationGateway).Assembly;

    private static readonly string[] ForbiddenNamespacePatterns =
    [
        "OpenAI", "Azure.AI", "Anthropic", "Infrastructure", "Persistence",
        "EfCore", "Npgsql", "Supabase", "AspNetCore", "Playwright", "System.Net.Http"
    ];

    // -----------------------------------------------------------------------
    // IImageGenerationGateway exists as a public interface
    // -----------------------------------------------------------------------

    [Fact]
    public void IImageGenerationGateway_ExistsAsPublicInterface()
    {
        var type = AppAssembly.GetExportedTypes()
            .FirstOrDefault(t => t.Name == "IImageGenerationGateway");

        Assert.NotNull(type);
        Assert.True(type!.IsInterface);
    }

    // -----------------------------------------------------------------------
    // IMG-08: No provider types in IImageGenerationGateway.GenerateAsync signature
    // -----------------------------------------------------------------------

    [Fact]
    public void IImageGenerationGateway_GenerateAsync_HasNoInfrastructureTypes()
    {
        var method = typeof(IImageGenerationGateway).GetMethod("GenerateAsync");
        Assert.NotNull(method);

        AssertNoForbiddenTypes(method!);
    }

    // -----------------------------------------------------------------------
    // IImageGenerationGateway mirrors IImageModelClient (same GenerateAsync signature)
    // -----------------------------------------------------------------------

    [Fact]
    public void IImageModelClient_ExistsAsPublicInterface()
    {
        var type = AppAssembly.GetExportedTypes()
            .FirstOrDefault(t => t.Name == "IImageModelClient");

        Assert.NotNull(type);
        Assert.True(type!.IsInterface);
    }

    [Fact]
    public void IImageModelClient_GenerateAsync_HasNoInfrastructureTypes()
    {
        var method = typeof(IImageModelClient).GetMethod("GenerateAsync");
        Assert.NotNull(method);

        AssertNoForbiddenTypes(method!);
    }

    [Fact]
    public void IImageModelClient_GenerateAsync_MirrorsGatewaySignature()
    {
        var gatewayMethod = typeof(IImageGenerationGateway).GetMethod("GenerateAsync");
        var clientMethod = typeof(IImageModelClient).GetMethod("GenerateAsync");

        Assert.NotNull(gatewayMethod);
        Assert.NotNull(clientMethod);

        // Same return type
        Assert.Equal(gatewayMethod!.ReturnType, clientMethod!.ReturnType);

        // Same parameter types
        var gatewayParams = gatewayMethod.GetParameters().Select(p => p.ParameterType).ToArray();
        var clientParams = clientMethod.GetParameters().Select(p => p.ParameterType).ToArray();
        Assert.Equal(gatewayParams, clientParams);
    }

    // -----------------------------------------------------------------------
    // Request/result records: only primitives, byte[], and Application types
    // -----------------------------------------------------------------------

    [Fact]
    public void ImageGenerationRequest_HasNoInfrastructureTypes()
    {
        var requestType = AppAssembly.GetExportedTypes()
            .FirstOrDefault(t => t.Name == "ImageGenerationRequest");

        Assert.NotNull(requestType);
        AssertRecordPropertiesHaveNoForbiddenTypes(requestType!);
    }

    [Fact]
    public void ImageGenerationResult_HasNoInfrastructureTypes()
    {
        var resultType = AppAssembly.GetExportedTypes()
            .FirstOrDefault(t => t.Name == "ImageGenerationResult");

        Assert.NotNull(resultType);
        AssertRecordPropertiesHaveNoForbiddenTypes(resultType!);
    }

    [Fact]
    public void ImageInitReference_ExistsAndHasNoInfrastructureTypes()
    {
        var type = AppAssembly.GetExportedTypes()
            .FirstOrDefault(t => t.Name == "ImageInitReference");

        Assert.NotNull(type);
        AssertRecordPropertiesHaveNoForbiddenTypes(type!);
    }

    [Fact]
    public void ImageUsage_ExistsAndHasNoInfrastructureTypes()
    {
        var type = AppAssembly.GetExportedTypes()
            .FirstOrDefault(t => t.Name == "ImageUsage");

        Assert.NotNull(type);
        AssertRecordPropertiesHaveNoForbiddenTypes(type!);
    }

    // -----------------------------------------------------------------------
    // GenerateAsync returns Task<Result<ImageGenerationResult>>
    // -----------------------------------------------------------------------

    [Fact]
    public void IImageGenerationGateway_GenerateAsync_ReturnsTaskOfResultOfImageGenerationResult()
    {
        var method = typeof(IImageGenerationGateway).GetMethod("GenerateAsync");
        Assert.NotNull(method);

        var returnType = method!.ReturnType;
        Assert.True(returnType.IsGenericType, "Return type must be generic Task<...>");
        Assert.Equal(typeof(Task<>), returnType.GetGenericTypeDefinition());

        var innerType = returnType.GetGenericArguments()[0];
        Assert.True(innerType.IsGenericType);
        Assert.Equal(typeof(Result<>), innerType.GetGenericTypeDefinition());

        var resultType = AppAssembly.GetExportedTypes()
            .FirstOrDefault(t => t.Name == "ImageGenerationResult");
        Assert.Equal(resultType, innerType.GetGenericArguments()[0]);
    }

    // -----------------------------------------------------------------------
    // WU-4 (CAC-IMG-01/04): IImageGenerationGateway.DescribeAsync + ImageModelCapabilities
    // -----------------------------------------------------------------------

    [Fact]
    public void IImageGenerationGateway_DescribeAsync_Exists()
    {
        var method = typeof(IImageGenerationGateway).GetMethod("DescribeAsync",
            [typeof(Guid), typeof(CancellationToken)]);

        Assert.NotNull(method);
    }

    [Fact]
    public void IImageGenerationGateway_DescribeAsync_ReturnsTaskOfResultOfImageModelCapabilities()
    {
        var method = typeof(IImageGenerationGateway).GetMethod("DescribeAsync",
            [typeof(Guid), typeof(CancellationToken)]);

        Assert.NotNull(method);

        var returnType = method!.ReturnType;
        Assert.True(returnType.IsGenericType, "DescribeAsync return type must be generic Task<...>");
        Assert.Equal(typeof(Task<>), returnType.GetGenericTypeDefinition());

        var innerType = returnType.GetGenericArguments()[0];
        Assert.True(innerType.IsGenericType, "Inner type must be generic Result<...>");
        Assert.Equal(typeof(Result<>), innerType.GetGenericTypeDefinition());

        var capsType = AppAssembly.GetExportedTypes()
            .FirstOrDefault(t => t.Name == "ImageModelCapabilities");
        Assert.NotNull(capsType);
        Assert.Equal(capsType, innerType.GetGenericArguments()[0]);
    }

    [Fact]
    public void IImageGenerationGateway_DescribeAsync_HasNoInfrastructureTypes()
    {
        var method = typeof(IImageGenerationGateway).GetMethod("DescribeAsync",
            [typeof(Guid), typeof(CancellationToken)]);

        Assert.NotNull(method);
        AssertNoForbiddenTypes(method!);
    }

    [Fact]
    public void ImageModelCapabilities_ExistsAndHasNoInfrastructureTypes()
    {
        var capsType = AppAssembly.GetExportedTypes()
            .FirstOrDefault(t => t.Name == "ImageModelCapabilities");

        Assert.NotNull(capsType);
        AssertRecordPropertiesHaveNoForbiddenTypes(capsType!);
    }

    [Fact]
    public void ImageModelCapabilities_HasSupportsImageConditioningBoolProperty()
    {
        var capsType = AppAssembly.GetExportedTypes()
            .FirstOrDefault(t => t.Name == "ImageModelCapabilities");

        Assert.NotNull(capsType);

        var prop = capsType!.GetProperty("SupportsImageConditioning");
        Assert.NotNull(prop);
        Assert.Equal(typeof(bool), prop!.PropertyType);
    }

    [Fact]
    public void ImageModelCapabilities_HasMaxConditioningImagesIntProperty()
    {
        var capsType = AppAssembly.GetExportedTypes()
            .FirstOrDefault(t => t.Name == "ImageModelCapabilities");

        Assert.NotNull(capsType);

        var prop = capsType!.GetProperty("MaxConditioningImages");
        Assert.NotNull(prop);
        Assert.Equal(typeof(int), prop!.PropertyType);
    }

    // -----------------------------------------------------------------------
    // WU-4 (CAC-IMG-01): IImageModelClient.Capabilities property
    // -----------------------------------------------------------------------

    [Fact]
    public void IImageModelClient_HasCapabilitiesProperty()
    {
        var prop = typeof(IImageModelClient).GetProperty("Capabilities");

        Assert.NotNull(prop);

        var capsType = AppAssembly.GetExportedTypes()
            .FirstOrDefault(t => t.Name == "ImageModelCapabilities");
        Assert.NotNull(capsType);
        Assert.Equal(capsType, prop!.PropertyType);
    }

    // -----------------------------------------------------------------------
    // WU-4 (CAC-IMG-01): ImageGenerationRequest.ReferenceImages
    // -----------------------------------------------------------------------

    [Fact]
    public void ImageGenerationRequest_HasNullableReferenceImagesProperty()
    {
        var requestType = AppAssembly.GetExportedTypes()
            .FirstOrDefault(t => t.Name == "ImageGenerationRequest");

        Assert.NotNull(requestType);

        var prop = requestType!.GetProperty("ReferenceImages");
        Assert.NotNull(prop);

        // Must be IReadOnlyList<ImageInitReference>? (nullable)
        Assert.True(prop!.PropertyType.IsGenericType
            || prop.PropertyType.GetGenericTypeDefinition() == typeof(IReadOnlyList<>),
            "ReferenceImages must be a generic IReadOnlyList<> type");

        AssertRecordPropertiesHaveNoForbiddenTypes(requestType!);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static void AssertNoForbiddenTypes(MethodInfo method)
    {
        var allTypes = method.GetParameters()
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
                    $"Forbidden type pattern '{pattern}' found in '{fullName}'");
            }
        }
    }

    private static void AssertRecordPropertiesHaveNoForbiddenTypes(Type recordType)
    {
        var propertyTypes = recordType.GetProperties()
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
                    $"Forbidden type pattern '{pattern}' found in property type '{fullName}' of {recordType.Name}");
            }
        }
    }

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
