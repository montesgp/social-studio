namespace Incoders.Hive.Infrastructure.Identity;

public interface IJwtTokenService
{
    /// <summary>
    /// Issues an access+refresh token pair for the given user. Roles are embedded as
    /// <c>role</c> claims so the bearer middleware can resolve them via <c>ClaimsPrincipal.IsInRole</c>.
    /// </summary>
    TokenPair Issue(ApplicationUser user, IEnumerable<string> roles);

    Guid? ConsumeRefreshToken(string refreshToken);
}
