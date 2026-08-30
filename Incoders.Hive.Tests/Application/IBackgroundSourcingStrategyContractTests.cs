using Incoders.Hive.Application.Abstractions.Social.ContentGeneration;
using Incoders.Hive.Domain.Social.Clients;

namespace Incoders.Hive.Tests.Application;

/// <summary>
/// TDD tests for WU-3 step 3.1: Port contract purity.
/// Asserts that IBackgroundSourcingStrategy, IBackgroundStrategyResolver,
/// and BackgroundPlanRequest have no Infrastructure types in their signatures.
/// Refs: design §4; tasks 3.1.
/// </summary>
public sealed class IBackgroundSourcingStrategyContractTests
{
    // -----------------------------------------------------------------------
    // 3.1 RED: IBackgroundSourcingStrategy is a pure Application port
    // -----------------------------------------------------------------------

    [Fact]
    public void IBackgroundSourcingStrategy_ExistsAsPublicInterface()
    {
        var appAssembly = typeof(IBackgroundSourcingStrategy).Assembly;

        var type = appAssembly.GetExportedTypes()
            .FirstOrDefault(t => t.Name == "IBackgroundSourcingStrategy");

        Assert.NotNull(type);
        Assert.True(type!.IsInterface);
    }

    [Fact]
    public void IBackgroundSourcingStrategy_HasNoInfrastructureTypes()
    {
        var method = typeof(IBackgroundSourcingStrategy).GetMethod("Plan");
        Assert.NotNull(method);

        var allTypes = method!.GetParameters()
            .Select(p => p.ParameterType)
            .Append(method.ReturnType)
            .SelectMany(t => t.IsGenericType ? t.GetGenericArguments().Append(t) : new[] { t })
            .ToList();

        foreach (var type in allTypes)
        {
            Assert.DoesNotMatch(
                @"Infrastructure|Persistence|EfCore|Npgsql|Supabase|AspNetCore|Playwright|HttpClient",
                type.FullName ?? string.Empty);
        }
    }

    [Fact]
    public void IBackgroundSourcingStrategy_HasPolicyProperty()
    {
        var prop = typeof(IBackgroundSourcingStrategy).GetProperty("Policy");

        Assert.NotNull(prop);
        Assert.Equal(typeof(BackgroundSourcingPolicy), prop!.PropertyType);
    }

    // -----------------------------------------------------------------------
    // IBackgroundStrategyResolver is also a pure Application port
    // -----------------------------------------------------------------------

    [Fact]
    public void IBackgroundStrategyResolver_ExistsAsPublicInterface()
    {
        var appAssembly = typeof(IBackgroundStrategyResolver).Assembly;

        var type = appAssembly.GetExportedTypes()
            .FirstOrDefault(t => t.Name == "IBackgroundStrategyResolver");

        Assert.NotNull(type);
        Assert.True(type!.IsInterface);
    }

    [Fact]
    public void IBackgroundStrategyResolver_HasNoInfrastructureTypes()
    {
        var method = typeof(IBackgroundStrategyResolver).GetMethod("Resolve");
        Assert.NotNull(method);

        var allTypes = method!.GetParameters()
            .Select(p => p.ParameterType)
            .Append(method.ReturnType)
            .SelectMany(t => t.IsGenericType ? t.GetGenericArguments().Append(t) : new[] { t })
            .ToList();

        foreach (var type in allTypes)
        {
            Assert.DoesNotMatch(
                @"Infrastructure|Persistence|EfCore|Npgsql|Supabase|AspNetCore|Playwright|HttpClient",
                type.FullName ?? string.Empty);
        }
    }

    // -----------------------------------------------------------------------
    // BackgroundPlanRequest carries only Domain records (no provider types)
    // -----------------------------------------------------------------------

    [Fact]
    public void BackgroundPlanRequest_HasNoInfrastructureTypes()
    {
        var requestType = typeof(BackgroundPlanRequest);

        var propertyTypes = requestType.GetProperties()
            .Select(p => p.PropertyType)
            .SelectMany(t => t.IsGenericType ? t.GetGenericArguments().Append(t) : new[] { t })
            .ToList();

        foreach (var type in propertyTypes)
        {
            Assert.DoesNotMatch(
                @"Infrastructure|Persistence|EfCore|Npgsql|Supabase|AspNetCore|Playwright|HttpClient",
                type.FullName ?? string.Empty);
        }
    }
}
