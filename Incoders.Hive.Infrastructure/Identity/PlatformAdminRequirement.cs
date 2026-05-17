using Incoders.Hive.Application.Abstractions;
using Microsoft.AspNetCore.Authorization;

namespace Incoders.Hive.Infrastructure.Identity;

/// <summary>
/// Authorization requirement that passes only when the caller is a platform-level sysadmin.
/// </summary>
public sealed class PlatformAdminRequirement : IAuthorizationRequirement { }

internal sealed class PlatformAdminHandler : AuthorizationHandler<PlatformAdminRequirement>
{
    private readonly ICurrentUser _currentUser;

    public PlatformAdminHandler(ICurrentUser currentUser)
    {
        _currentUser = currentUser;
    }

    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PlatformAdminRequirement requirement)
    {
        if (_currentUser.IsAuthenticated && _currentUser.IsPlatformAdmin)
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
