using System.Security.Claims;
using SocialStudio.Application.Abstractions;
using SocialStudio.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;

namespace SocialStudio.Tests.ApiService;

/// <summary>
/// Change 3 (job 8757e85a-6acd-49b4-af11-a2d19fbcd3e1 incident): the sysadmin-facing
/// content-generation audit timeline endpoints must be gated by the SAME PlatformAdmin policy as
/// the rest of <c>/api/platform/integrations</c> — this surfaces per-slide provider telemetry and
/// asset state across ALL organizations, which must never be reachable by a normal tenant user.
/// </summary>
public sealed class ContentGenerationAuditEndpointTests
{
    [Fact]
    public void ContentGenerationAuditEndpointsRequirePlatformAdminPolicy()
    {
        var source = ReadRepositoryFile(
            "SocialStudio.ApiService",
            "Endpoints",
            "PlatformConfiguration",
            "MapPlatformConfigurationEndpoints.cs");

        Assert.Contains("app.MapGroup(\"/api/platform/content-generation-jobs\")", source);

        // The group declaration and its RequireAuthorization call must be adjacent so the policy
        // actually applies to /api/platform/content-generation-jobs and not some other group.
        var groupIndex = source.IndexOf(
            "app.MapGroup(\"/api/platform/content-generation-jobs\")",
            StringComparison.Ordinal);
        var nextGroupIndex = source.IndexOf("app.MapGroup(", groupIndex + 1, StringComparison.Ordinal);
        var searchWindow = nextGroupIndex > 0
            ? source[groupIndex..nextGroupIndex]
            : source[groupIndex..];

        Assert.Contains(
            ".RequireAuthorization(IdentityServicesExtensions.Policies.PlatformAdmin)",
            searchWindow);
    }

    [Fact]
    public async Task PlatformAdminHandler_NonSysadminCaller_DoesNotSucceedRequirement()
    {
        var handler = new PlatformAdminHandler(new FakeCurrentUser(isAuthenticated: true, isPlatformAdmin: false));
        var requirement = new PlatformAdminRequirement();
        var context = new AuthorizationHandlerContext(
            [requirement],
            new ClaimsPrincipal(new ClaimsIdentity()),
            resource: null);

        await handler.HandleAsync(context);

        Assert.False(context.HasSucceeded);
    }

    [Fact]
    public async Task PlatformAdminHandler_UnauthenticatedCaller_DoesNotSucceedRequirement()
    {
        var handler = new PlatformAdminHandler(new FakeCurrentUser(isAuthenticated: false, isPlatformAdmin: true));
        var requirement = new PlatformAdminRequirement();
        var context = new AuthorizationHandlerContext(
            [requirement],
            new ClaimsPrincipal(new ClaimsIdentity()),
            resource: null);

        await handler.HandleAsync(context);

        Assert.False(context.HasSucceeded);
    }

    [Fact]
    public async Task PlatformAdminHandler_SysadminCaller_SucceedsRequirement()
    {
        var handler = new PlatformAdminHandler(new FakeCurrentUser(isAuthenticated: true, isPlatformAdmin: true));
        var requirement = new PlatformAdminRequirement();
        var context = new AuthorizationHandlerContext(
            [requirement],
            new ClaimsPrincipal(new ClaimsIdentity()),
            resource: null);

        await handler.HandleAsync(context);

        Assert.True(context.HasSucceeded);
    }

    private static string ReadRepositoryFile(params string[] relativePathParts)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);

        while (current is not null)
        {
            var candidate = Path.Combine([current.FullName, .. relativePathParts]);
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }

            current = current.Parent;
        }

        throw new FileNotFoundException(
            $"Could not locate repository file {Path.Combine(relativePathParts)} from {AppContext.BaseDirectory}.");
    }

    private sealed class FakeCurrentUser(bool isAuthenticated, bool isPlatformAdmin) : ICurrentUser
    {
        public bool IsAuthenticated => isAuthenticated;
        public Guid? UserId => isAuthenticated ? Guid.NewGuid() : null;
        public string? Email => isAuthenticated ? "user@example.com" : null;
        public bool IsPlatformAdmin => isPlatformAdmin;
    }
}
