using Incoders.Hive.ApiService.Endpoints.Common;
using Incoders.Hive.Application.Features.PlatformConfiguration.EmailTemplates;
using Incoders.Hive.Application.Features.PlatformConfiguration.Integrations;
using Incoders.Hive.Infrastructure.Identity;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Incoders.Hive.ApiService.Endpoints.PlatformConfiguration;

public static class PlatformConfigurationEndpointsExtensions
{
    public static IEndpointRouteBuilder MapPlatformConfigurationEndpoints(this IEndpointRouteBuilder app)
    {
        var emailTemplates = app.MapGroup("/api/platform/email-templates")
            .WithTags("Platform Email Templates")
            .RequireAuthorization(IdentityServicesExtensions.Policies.PlatformAdmin);

        emailTemplates.MapGet("/", ListEmailTemplatesAsync)
            .WithName("ListPlatformEmailTemplates")
            .WithSummary("List platform email templates for authentication and notifications")
            .Produces<IReadOnlyList<EmailTemplateResponse>>(StatusCodes.Status200OK);

        emailTemplates.MapPut("/{key}", UpsertEmailTemplateAsync)
            .WithName("UpsertPlatformEmailTemplate")
            .WithSummary("Create or update a platform email template")
            .Produces<EmailTemplateResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);

        var platformIntegrations = app.MapGroup("/api/platform/integrations")
            .WithTags("Platform Integrations")
            .RequireAuthorization(IdentityServicesExtensions.Policies.PlatformAdmin);

        platformIntegrations.MapGet("/", ListPlatformIntegrationsAsync)
            .WithName("ListPlatformIntegrations")
            .WithSummary("List platform-owned integration configurations")
            .Produces<IReadOnlyList<PlatformIntegrationSummaryResponse>>(StatusCodes.Status200OK);

        platformIntegrations.MapGet("/email-smtp", GetEmailSmtpAsync)
            .WithName("GetEmailSmtpPlatformIntegration")
            .WithSummary("Get the platform SMTP email configuration")
            .Produces<EmailSmtpConfigurationResponse>(StatusCodes.Status200OK);

        platformIntegrations.MapPut("/email-smtp", UpsertEmailSmtpAsync)
            .WithName("UpsertEmailSmtpPlatformIntegration")
            .WithSummary("Create or update the platform SMTP email configuration")
            .Produces<EmailSmtpConfigurationResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        return app;
    }

    private static async Task<IResult> ListEmailTemplatesAsync(
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new GetEmailTemplatesQuery(), cancellationToken);
        return result.Match(items => (IResult)TypedResults.Ok(items));
    }

    private static async Task<IResult> UpsertEmailTemplateAsync(
        string key,
        [FromBody] UpsertEmailTemplateRequest body,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(
            new UpsertEmailTemplateCommand(key, body.Subject, body.HtmlBody, body.TextBody),
            cancellationToken);
        return result.Match(template => (IResult)TypedResults.Ok(template));
    }

    private static async Task<IResult> ListPlatformIntegrationsAsync(
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new GetPlatformIntegrationsQuery(), cancellationToken);
        return result.Match(items => (IResult)TypedResults.Ok(items));
    }

    private static async Task<IResult> GetEmailSmtpAsync(
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new GetEmailSmtpConfigurationQuery(), cancellationToken);
        return result.Match(config => (IResult)TypedResults.Ok(config));
    }

    private static async Task<IResult> UpsertEmailSmtpAsync(
        [FromBody] UpsertEmailSmtpConfigurationRequest body,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(
            new UpsertEmailSmtpConfigurationCommand(
                body.Enabled,
                body.Host,
                body.Port,
                body.UseSsl,
                body.Username,
                body.Password,
                body.FromEmail,
                body.FromName),
            cancellationToken);
        return result.Match(config => (IResult)TypedResults.Ok(config));
    }
}

public sealed record UpsertEmailTemplateRequest(string Subject, string HtmlBody, string? TextBody);

public sealed record UpsertEmailSmtpConfigurationRequest(
    bool Enabled,
    string Host,
    int Port,
    bool UseSsl,
    string Username,
    string? Password,
    string FromEmail,
    string FromName);
