using SocialStudio.Application.Abstractions;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace SocialStudio.Infrastructure.Identity;

/// <summary>
/// Wires ASP.NET Identity, JWT bearer authentication signed with a local HMAC key,
/// and authorization policies. The backend is the sole authority for authentication.
/// </summary>
public static class IdentityServicesExtensions
{
    /// <summary>Authorization policy names.</summary>
    public static class Policies
    {
        /// <summary>Requires the caller to hold the <see cref="IdentityRoles.Sysadmin"/> role.</summary>
        public const string PlatformAdmin = "PlatformAdmin";
    }

    public static IServiceCollection AddIdentityAuth(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            .Validate(o => !string.IsNullOrWhiteSpace(o.Issuer), "Authentication:Jwt:Issuer is required.")
            .Validate(o => !string.IsNullOrWhiteSpace(o.Audience), "Authentication:Jwt:Audience is required.")
            .Validate(o => !string.IsNullOrWhiteSpace(o.Secret) && Encoding.UTF8.GetByteCount(o.Secret) >= 32,
                "Authentication:Jwt:Secret must be at least 32 bytes (use a strong random value).")
            .ValidateOnStart();

        services.AddOptions<SysAdminSeederOptions>()
            .Bind(configuration.GetSection(SysAdminSeederOptions.SectionName));

        services
            .AddIdentityCore<ApplicationUser>(options =>
            {
                options.User.RequireUniqueEmail = true;
                options.Password.RequiredLength = 12;
                options.Password.RequireDigit = true;
                options.Password.RequireLowercase = true;
                options.Password.RequireUppercase = true;
                options.Password.RequireNonAlphanumeric = true;
                options.SignIn.RequireConfirmedEmail = false;
            })
            .AddRoles<ApplicationRole>()
            .AddRoleManager<RoleManager<ApplicationRole>>()
            .AddEntityFrameworkStores<AppIdentityDbContext>()
            .AddDefaultTokenProviders();

        services.AddSingleton<RefreshTokenStore>();
        services.AddScoped<IJwtTokenService, JwtTokenService>();
        services.AddScoped<RoleSeeder>();
        services.AddScoped<SysAdminUserSeeder>();

        services.Replace(ServiceDescriptor.Scoped<ICurrentUser, HttpContextCurrentUser>());
        services.AddScoped<IAuthorizationHandler, PlatformAdminHandler>();

        services
            .AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultScheme = JwtBearerDefaults.AuthenticationScheme;
            })
            .AddJwtBearer();

        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<JwtOptions>>((bearerOpts, jwtOpts) =>
            {
                var jwt = jwtOpts.Value;
                bearerOpts.MapInboundClaims = false;
                bearerOpts.RequireHttpsMetadata = false;
                bearerOpts.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = jwt.Issuer,
                    ValidateAudience = true,
                    ValidAudience = jwt.Audience,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Secret)),
                    NameClaimType = JwtRegisteredClaimNames.Sub,
                    RoleClaimType = ClaimTypes.Role,
                    ClockSkew = TimeSpan.FromSeconds(30),
                };
            });

        services.AddAuthorization(options =>
        {
            options.AddPolicy(Policies.PlatformAdmin, policy =>
                policy.AddRequirements(new PlatformAdminRequirement()));
        });

        return services;
    }
}
