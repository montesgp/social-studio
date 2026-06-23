using Incoders.Hive.Application.Abstractions.Social;
using System.Reflection;

namespace Incoders.Hive.Tests.Application;

/// <summary>
/// TDD contract tests for WU-2 step 2.5: IProfileAnalysisRepository freshness/debounce methods.
/// Asserts the two new method signatures are present on the existing interface and contain
/// no Infrastructure types.
/// Refs: design §3.4; tasks 2.5; CAG-PA-04, CAG-PA-05.
/// </summary>
public sealed class IProfileAnalysisRepositoryFreshnessTests
{
    // ── 2.5a — GetLatestAnalyzedCompletedAtAsync exists on the interface ─────

    [Fact]
    public void IProfileAnalysisRepository_HasGetLatestAnalyzedCompletedAtAsync()
    {
        var iface = typeof(IProfileAnalysisRepository);

        var method = iface.GetMethod(
            "GetLatestAnalyzedCompletedAtAsync",
            [typeof(Guid), typeof(Guid), typeof(CancellationToken)]);

        Assert.NotNull(method);
    }

    // ── 2.5b — GetLatestAnalyzedCompletedAtAsync returns Task<DateTime?> ────

    [Fact]
    public void IProfileAnalysisRepository_GetLatestAnalyzedCompletedAtAsync_ReturnsTaskNullableDateTime()
    {
        var iface = typeof(IProfileAnalysisRepository);

        var method = iface.GetMethod(
            "GetLatestAnalyzedCompletedAtAsync",
            [typeof(Guid), typeof(Guid), typeof(CancellationToken)]);

        Assert.NotNull(method);
        Assert.True(method!.ReturnType.IsGenericType, "Return type must be generic (Task<T>)");

        var taskArg = method.ReturnType.GetGenericArguments()[0];
        // DateTime? → Nullable<DateTime>
        Assert.True(taskArg.IsGenericType, "Task arg must be Nullable<DateTime>");
        Assert.Equal(typeof(Nullable<DateTime>), taskArg);
    }

    // ── 2.5c — HasNonTerminalAnalysisForClientAsync exists on the interface ──

    [Fact]
    public void IProfileAnalysisRepository_HasNonTerminalAnalysisForClientAsync_Exists()
    {
        var iface = typeof(IProfileAnalysisRepository);

        var method = iface.GetMethod(
            "HasNonTerminalAnalysisForClientAsync",
            [typeof(Guid), typeof(Guid), typeof(CancellationToken)]);

        Assert.NotNull(method);
    }

    // ── 2.5d — HasNonTerminalAnalysisForClientAsync returns Task<bool> ───────

    [Fact]
    public void IProfileAnalysisRepository_HasNonTerminalAnalysisForClientAsync_ReturnsTaskBool()
    {
        var iface = typeof(IProfileAnalysisRepository);

        var method = iface.GetMethod(
            "HasNonTerminalAnalysisForClientAsync",
            [typeof(Guid), typeof(Guid), typeof(CancellationToken)]);

        Assert.NotNull(method);
        Assert.True(method!.ReturnType.IsGenericType, "Return type must be generic (Task<T>)");

        var taskArg = method.ReturnType.GetGenericArguments()[0];
        Assert.Equal(typeof(bool), taskArg);
    }

    // ── 2.5e — No Infrastructure types in either new method signature ─────────

    [Fact]
    public void IProfileAnalysisRepository_FreshnessMethods_HaveNoInfrastructureTypes()
    {
        var iface = typeof(IProfileAnalysisRepository);

        var methods = new[]
        {
            iface.GetMethod("GetLatestAnalyzedCompletedAtAsync", [typeof(Guid), typeof(Guid), typeof(CancellationToken)]),
            iface.GetMethod("HasNonTerminalAnalysisForClientAsync", [typeof(Guid), typeof(Guid), typeof(CancellationToken)])
        };

        foreach (var method in methods)
        {
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
    }
}
