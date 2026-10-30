using SocialStudio.ApiService.Endpoints.Common;
using SocialStudio.Application.Abstractions.Social.ContentGeneration;
using SocialStudio.Application.Features.PlatformConfiguration.ContentGenerationAudit;
using SocialStudio.Application.Features.PlatformConfiguration.EmailTemplates;
using SocialStudio.Application.Features.PlatformConfiguration.Integrations;
using SocialStudio.Infrastructure.Identity;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace SocialStudio.ApiService.Endpoints.PlatformConfiguration;

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

        // Change 3 (job 8757e85a-6acd-49b4-af11-a2d19fbcd3e1 incident): read-only audit timeline for
        // content-generation jobs. Same PlatformAdmin (sysadmin) policy as the rest of this group —
        // this surfaces per-slide provider telemetry and asset state across ALL organizations, which
        // must never be reachable by a normal tenant user or a caller scoped to one org.
        var contentGenerationJobs = app.MapGroup("/api/platform/content-generation-jobs")
            .WithTags("Platform Content Generation Audit")
            .RequireAuthorization(IdentityServicesExtensions.Policies.PlatformAdmin);

        contentGenerationJobs.MapGet("/", ListContentGenerationJobsAsync)
            .WithName("ListPlatformContentGenerationJobs")
            .WithSummary("List recent content-generation jobs across all organizations (bounded, paginated)")
            .Produces<ContentGenerationJobListPage>(StatusCodes.Status200OK);

        contentGenerationJobs.MapGet("/{jobId:guid}/timeline", GetContentGenerationJobTimelineAsync)
            .WithName("GetPlatformContentGenerationJobTimeline")
            .WithSummary("Get the chronological audit timeline for one content-generation job")
            .Produces<ContentGenerationJobTimeline>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

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

    private static async Task<IResult> ListContentGenerationJobsAsync(
        ISender sender,
        CancellationToken cancellationToken,
        int page = 1,
        int pageSize = 25)
    {
        var result = await sender.Send(new ListContentGenerationJobsQuery(page, pageSize), cancellationToken);
        return result.Match(items => (IResult)TypedResults.Ok(items));
    }

    private static async Task<IResult> GetContentGenerationJobTimelineAsync(
        Guid jobId,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new GetContentGenerationJobTimelineQuery(jobId), cancellationToken);
        return result.Match(timeline => (IResult)TypedResults.Ok(timeline));
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
