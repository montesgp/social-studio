using System.IdentityModel.Tokens.Jwt;
using Incoders.Hive.Infrastructure.Identity;
using Incoders.Hive.Tests.Common;
using Microsoft.Extensions.Options;

namespace Incoders.Hive.Tests.Application.Auth;

public class JwtTokenServiceTests
{
    private static readonly DateTime Now = new(2026, 4, 24, 12, 0, 0, DateTimeKind.Utc);

    private static JwtTokenService CreateService(out RefreshTokenStore store, out TestClock clock)
    {
        var opts = Options.Create(new JwtOptions
        {
            Issuer = "test",
            Audience = "test",
            Secret = "unit-test-secret-unit-test-secret",
            AccessTokenMinutes = 15,
            RefreshTokenDays = 7,
        });
        clock = new TestClock(Now);
        store = new RefreshTokenStore();
        return new JwtTokenService(opts, clock, store);
    }

    [Fact]
    public void Issue_ProducesSignedJwtWithExpectedClaims()
    {
        var service = CreateService(out _, out _);
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            Email = "ada@example.com",
            UserName = "ada@example.com",
        };

        var tokens = service.Issue(user, Array.Empty<string>());
        Assert.False(string.IsNullOrWhiteSpace(tokens.AccessToken));
        Assert.False(string.IsNullOrWhiteSpace(tokens.RefreshToken));
        Assert.Equal(Now.AddMinutes(15), tokens.AccessTokenExpiresAtUtc);

        var parsed = new JwtSecurityTokenHandler().ReadJwtToken(tokens.AccessToken);
        Assert.Equal(user.Id.ToString(), parsed.Subject);
        Assert.Contains(parsed.Claims, c => c.Type == JwtRegisteredClaimNames.Email && c.Value == "ada@example.com");
    }

    [Fact]
    public void ConsumeRefreshToken_Works_ThenIsSingleUse()
    {
        var service = CreateService(out _, out _);
        var user = new ApplicationUser { Id = Guid.NewGuid(), Email = "x@y" };
        var tokens = service.Issue(user, Array.Empty<string>());

        Assert.Equal(user.Id, service.ConsumeRefreshToken(tokens.RefreshToken));
        Assert.Null(service.ConsumeRefreshToken(tokens.RefreshToken));
    }

    [Fact]
    public void ConsumeRefreshToken_AfterExpiry_ReturnsNull()
    {
        var service = CreateService(out _, out var clock);
        var user = new ApplicationUser { Id = Guid.NewGuid(), Email = "x@y" };
        var tokens = service.Issue(user, Array.Empty<string>());

        clock.UtcNow = Now.AddDays(8);

        Assert.Null(service.ConsumeRefreshToken(tokens.RefreshToken));
    }
}
