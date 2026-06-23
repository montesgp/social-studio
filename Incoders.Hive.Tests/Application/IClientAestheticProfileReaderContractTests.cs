using Incoders.Hive.Application.Abstractions.Social.ContentGeneration;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Shared;

namespace Incoders.Hive.Tests.Application;

/// <summary>
/// TDD contract tests for WU-2 step 2.3: IClientAestheticProfileReader port purity.
/// Asserts the interface exists in Application, has no Infrastructure/EF/Npgsql types in
/// its signature, and exposes exactly GetProfileAsync(orgId, clientId, ct) → ClientAestheticProfile?.
/// Refs: design §3.2 (OQ-6); tasks 2.3; CAG-CG-05.
/// </summary>
public sealed class IClientAestheticProfileReaderContractTests
{
    // ── 2.3a — Interface exists as public in Application assembly ────────────

    [Fact]
    public void IClientAestheticProfileReader_ExistsAsPublicInterfaceInApplicationAssembly()
    {
        var appAssembly = typeof(IClientAestheticProfileReader).Assembly;

        var type = appAssembly.GetExportedTypes()
            .FirstOrDefault(t => t.Name == "IClientAestheticProfileReader");

        Assert.NotNull(type);
        Assert.True(type!.IsInterface);
    }

    // ── 2.3b — No Infrastructure types in signature (architecture gate) ──────

    [Fact]
    public void IClientAestheticProfileReader_HasNoInfrastructureTypesInSignature()
    {
        var iface = typeof(IClientAestheticProfileReader);
        var method = iface.GetMethod("GetProfileAsync");
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

    // ── 2.3c — Signature: GetProfileAsync(Guid orgId, Guid clientId, CancellationToken ct) → Task<ClientAestheticProfile?> ──

    [Fact]
    public void IClientAestheticProfileReader_HasExpectedMethodSignature()
    {
        var iface = typeof(IClientAestheticProfileReader);
        var method = iface.GetMethod("GetProfileAsync");

        Assert.NotNull(method);

        var parameters = method!.GetParameters();
        Assert.Equal(3, parameters.Length);
        Assert.Equal(typeof(Guid), parameters[0].ParameterType);
        Assert.Equal(typeof(Guid), parameters[1].ParameterType);
        Assert.Equal(typeof(CancellationToken), parameters[2].ParameterType);

        // Return type: Task<ClientAestheticProfile?>
        Assert.True(method.ReturnType.IsGenericType);
        var taskArg = method.ReturnType.GetGenericArguments()[0];

        // ClientAestheticProfile? is Nullable<ClientAestheticProfile> for value types,
        // but since it is a reference type (record class), the nullable annotation does not
        // change the CLR type — Task<ClientAestheticProfile> and Task<ClientAestheticProfile?>
        // are the same at runtime.
        Assert.Equal(typeof(ClientAestheticProfile), taskArg);
    }

    // ── 2.3d — Old interface (IClientAestheticDescriptorReader) is NOT the reader anymore ──
    // The new port replaces it; the old interface may still exist for back-compat until
    // callers are migrated, but the contract test pins the new name.

    [Fact]
    public void IClientAestheticProfileReader_IsDistinctFromOldDescriptorReaderInterface()
    {
        var newIface = typeof(IClientAestheticProfileReader);
        Assert.NotEqual("IClientAestheticDescriptorReader", newIface.Name);
        Assert.Equal("IClientAestheticProfileReader", newIface.Name);
    }
}
