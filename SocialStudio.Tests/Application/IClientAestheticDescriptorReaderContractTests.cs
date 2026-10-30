using SocialStudio.Application.Abstractions.Social.ContentGeneration;

namespace SocialStudio.Tests.Application;

/// <summary>
/// TDD tests for WU-5 step 5.1: IClientAestheticDescriptorReader port contract purity.
/// Asserts the interface exists in Application, has no Infrastructure types,
/// and exposes exactly the expected async descriptor-read signature.
/// Refs: design §4.4; tasks 5.1; PA-SEAM-01, PA-SEAM-03.
/// </summary>
public sealed class IClientAestheticDescriptorReaderContractTests
{
    // -----------------------------------------------------------------------
    // 5.1a — IClientAestheticDescriptorReader exists in Application assembly
    // -----------------------------------------------------------------------

    [Fact]
    public void IClientAestheticDescriptorReader_ExistsAsPublicInterfaceInApplicationAssembly()
    {
        var appAssembly = typeof(IBackgroundSourcingStrategy).Assembly;

        var type = appAssembly.GetExportedTypes()
            .FirstOrDefault(t => t.Name == "IClientAestheticDescriptorReader");

        Assert.NotNull(type);
        Assert.True(type!.IsInterface);
    }

    // -----------------------------------------------------------------------
    // 5.1b — no Infrastructure types in signature (PA-SEAM-03: no IApifyGateway)
    // -----------------------------------------------------------------------

    [Fact]
    public void IClientAestheticDescriptorReader_HasNoInfrastructureTypes()
    {
        var iface = typeof(IClientAestheticDescriptorReader);

        var method = iface.GetMethod("GetDescriptorsAsync");
        Assert.NotNull(method);

        var allTypes = method!.GetParameters()
            .Select(p => p.ParameterType)
            .Append(method.ReturnType)
            .SelectMany(t => t.IsGenericType ? t.GetGenericArguments().Append(t) : new[] { t })
            .ToList();

        foreach (var type in allTypes)
        {
            Assert.DoesNotMatch(
                @"Infrastructure|Persistence|EfCore|Npgsql|Supabase|AspNetCore|Playwright|HttpClient|Apify",
                type.FullName ?? string.Empty);
        }
    }

    // -----------------------------------------------------------------------
    // 5.1c — signature: GetDescriptorsAsync(Guid orgId, Guid clientId, CancellationToken ct)
    // -----------------------------------------------------------------------

    [Fact]
    public void IClientAestheticDescriptorReader_HasExpectedMethodSignature()
    {
        var iface = typeof(IClientAestheticDescriptorReader);
        var method = iface.GetMethod("GetDescriptorsAsync");

        Assert.NotNull(method);

        var parameters = method!.GetParameters();
        Assert.Equal(3, parameters.Length);
        Assert.Equal(typeof(Guid), parameters[0].ParameterType);
        Assert.Equal(typeof(Guid), parameters[1].ParameterType);
        Assert.Equal(typeof(CancellationToken), parameters[2].ParameterType);

        // Return type: Task<IReadOnlyList<string>>
        Assert.True(method.ReturnType.IsGenericType);
        var taskArg = method.ReturnType.GetGenericArguments()[0];
        Assert.True(taskArg.IsGenericType);
        var listArg = taskArg.GetGenericArguments()[0];
        Assert.Equal(typeof(string), listArg);
    }
}
