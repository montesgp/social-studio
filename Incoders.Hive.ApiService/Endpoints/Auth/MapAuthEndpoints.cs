using Incoders.Hive.Infrastructure.Identity;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

namespace Incoders.Hive.ApiService.Endpoints.Auth;

public static class AuthEndpointsExtensions
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth")
            .WithTags("Auth")
            .AllowAnonymous();

        group.MapPost("/register", RegisterAsync)
            .WithName("AuthRegister")
            .WithSummary("Register a new user and return access/refresh tokens")
            .Produces<TokenResponse>(StatusCodes.Status200OK)
            .ProducesValidationProblem();

        group.MapPost("/login", LoginAsync)
            .WithName("AuthLogin")
            .WithSummary("Password login")
            .Produces<TokenResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        group.MapPost("/refresh", RefreshAsync)
            .WithName("AuthRefresh")
            .WithSummary("Rotate access + refresh tokens using a refresh token")
            .Produces<TokenResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        group.MapPost("/confirm-email", ConfirmEmailAsync)
            .WithName("AuthConfirmEmail")
            .WithSummary("Confirm an email using the token issued at registration")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapPost("/resend-confirmation-email", ResendConfirmationAsync)
            .WithName("AuthResendConfirmation")
            .WithSummary("Regenerate and dispatch an email-confirmation token")
            .Produces(StatusCodes.Status204NoContent);

        group.MapPost("/forgot-password", ForgotPasswordAsync)
            .WithName("AuthForgotPassword")
            .WithSummary("Dispatch a password reset token")
            .Produces(StatusCodes.Status204NoContent);

        group.MapPost("/reset-password", ResetPasswordAsync)
            .WithName("AuthResetPassword")
            .WithSummary("Consume a reset token to set a new password")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        return app;
    }

    private static async Task<IResult> RegisterAsync(
        [FromBody] RegisterRequest body,
        UserManager<ApplicationUser> users,
        IJwtTokenService jwt,
        ILogger<ApplicationUser> logger,
        CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        var user = new ApplicationUser
        {
            UserName = body.Email,
            Email = body.Email,
            DisplayName = body.DisplayName,
        };

        var create = await users.CreateAsync(user, body.Password);
        if (!create.Succeeded)
        {
            return BuildIdentityProblem(create);
        }

        // New self-registered users default to organization-scoped admin. Platform sysadmin is reserved
        // for seeded accounts and explicit elevation.
        var roleAssign = await users.AddToRoleAsync(user, IdentityRoles.Admin);
        if (!roleAssign.Succeeded)
        {
            return BuildIdentityProblem(roleAssign);
        }

        var confirmationToken = await users.GenerateEmailConfirmationTokenAsync(user);
        logger.LogInformation("[DEV] Email confirmation token for {Email}: {Token}", user.Email, confirmationToken);

        var roles = await users.GetRolesAsync(user);
        var tokens = jwt.Issue(user, roles);
        return TypedResults.Ok(ToResponse(tokens));
    }

    private static async Task<IResult> LoginAsync(
        [FromBody] LoginRequest body,
        UserManager<ApplicationUser> users,
        IJwtTokenService jwt)
    {
        var user = await users.FindByEmailAsync(body.Email);
        if (user is null || !await users.CheckPasswordAsync(user, body.Password))
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Invalid credentials");
        }

        var roles = await users.GetRolesAsync(user);
        var tokens = jwt.Issue(user, roles);
        return TypedResults.Ok(ToResponse(tokens));
    }

    private static async Task<IResult> RefreshAsync(
        [FromBody] RefreshRequest body,
        UserManager<ApplicationUser> users,
        IJwtTokenService jwt)
    {
        var userId = jwt.ConsumeRefreshToken(body.RefreshToken);
        if (userId is null)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Invalid refresh token");
        }

        var user = await users.FindByIdAsync(userId.Value.ToString());
        if (user is null)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Unknown user");
        }

        var roles = await users.GetRolesAsync(user);
        var tokens = jwt.Issue(user, roles);
        return TypedResults.Ok(ToResponse(tokens));
    }

    private static async Task<IResult> ConfirmEmailAsync(
        [FromBody] ConfirmEmailRequest body,
        UserManager<ApplicationUser> users)
    {
        var user = await users.FindByEmailAsync(body.Email);
        if (user is null)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Unknown email");
        }

        var result = await users.ConfirmEmailAsync(user, body.Token);
        return result.Succeeded ? TypedResults.NoContent() : BuildIdentityProblem(result);
    }

    private static async Task<IResult> ResendConfirmationAsync(
        [FromBody] ResendConfirmationEmailRequest body,
        UserManager<ApplicationUser> users,
        ILogger<ApplicationUser> logger)
    {
        var user = await users.FindByEmailAsync(body.Email);
        if (user is not null && !await users.IsEmailConfirmedAsync(user))
        {
            var token = await users.GenerateEmailConfirmationTokenAsync(user);
            logger.LogInformation("[DEV] Resent email confirmation token for {Email}: {Token}", user.Email, token);
        }

        return TypedResults.NoContent();
    }

    private static async Task<IResult> ForgotPasswordAsync(
        [FromBody] ForgotPasswordRequest body,
        UserManager<ApplicationUser> users,
        ILogger<ApplicationUser> logger)
    {
        var user = await users.FindByEmailAsync(body.Email);
        if (user is not null)
        {
            var token = await users.GeneratePasswordResetTokenAsync(user);
            logger.LogInformation("[DEV] Password reset token for {Email}: {Token}", user.Email, token);
        }

        return TypedResults.NoContent();
    }

    private static async Task<IResult> ResetPasswordAsync(
        [FromBody] ResetPasswordRequest body,
        UserManager<ApplicationUser> users)
    {
        var user = await users.FindByEmailAsync(body.Email);
        if (user is null)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Unknown email");
        }

        var result = await users.ResetPasswordAsync(user, body.Token, body.NewPassword);
        return result.Succeeded ? TypedResults.NoContent() : BuildIdentityProblem(result);
    }

    private static TokenResponse ToResponse(TokenPair tokens) =>
        new(tokens.AccessToken, tokens.RefreshToken, tokens.AccessTokenExpiresAtUtc);

    private static IResult BuildIdentityProblem(IdentityResult result)
    {
        var errors = result.Errors.ToDictionary(e => e.Code, e => new[] { e.Description });
        return TypedResults.ValidationProblem(errors);
    }
}
