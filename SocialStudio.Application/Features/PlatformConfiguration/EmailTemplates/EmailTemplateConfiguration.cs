using System.Text.Json;
using SocialStudio.Application.Abstractions;
using SocialStudio.Application.Features.PlatformConfiguration;
using SocialStudio.Domain.Common;
using SocialStudio.Domain.SystemSettings;
using MediatR;

namespace SocialStudio.Application.Features.PlatformConfiguration.EmailTemplates;

public sealed record EmailTemplateResponse(
    string Key,
    string DisplayName,
    string Description,
    string Subject,
    string HtmlBody,
    string? TextBody,
    IReadOnlyList<string> Variables,
    bool IsConfigured,
    DateTime? UpdatedAtUtc);

public sealed record GetEmailTemplatesQuery()
    : IRequest<Result<IReadOnlyList<EmailTemplateResponse>>>;

public sealed record UpsertEmailTemplateCommand(
    string Key,
    string Subject,
    string HtmlBody,
    string? TextBody)
    : IRequest<Result<EmailTemplateResponse>>;

public sealed class GetEmailTemplatesHandler
    : IRequestHandler<GetEmailTemplatesQuery, Result<IReadOnlyList<EmailTemplateResponse>>>
{
    private readonly ISystemSettingRepository _settings;

    public GetEmailTemplatesHandler(ISystemSettingRepository settings)
    {
        _settings = settings;
    }

    public async Task<Result<IReadOnlyList<EmailTemplateResponse>>> Handle(
        GetEmailTemplatesQuery request,
        CancellationToken cancellationToken)
    {
        var items = new List<EmailTemplateResponse>();
        foreach (var definition in EmailTemplateDefinitions.All)
        {
            var setting = await _settings.GetByKeyAsync(
                definition.SettingKey,
                SystemSettingScope.Global,
                null,
                null,
                cancellationToken);

            items.Add(EmailTemplateMapper.ToResponse(definition, setting));
        }

        return Result.Success<IReadOnlyList<EmailTemplateResponse>>(items);
    }
}

public sealed class UpsertEmailTemplateHandler
    : IRequestHandler<UpsertEmailTemplateCommand, Result<EmailTemplateResponse>>
{
    private readonly ISystemSettingRepository _settings;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;

    public UpsertEmailTemplateHandler(
        ISystemSettingRepository settings,
        IUnitOfWork unitOfWork,
        IClock clock)
    {
        _settings = settings;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<Result<EmailTemplateResponse>> Handle(
        UpsertEmailTemplateCommand request,
        CancellationToken cancellationToken)
    {
        var definition = EmailTemplateDefinitions.Find(request.Key);
        if (definition is null)
        {
            return Result.Failure<EmailTemplateResponse>(PlatformConfigurationErrors.UnknownEmailTemplate);
        }

        var subject = request.Subject?.Trim() ?? string.Empty;
        var htmlBody = request.HtmlBody?.Trim() ?? string.Empty;
        if (subject.Length == 0 || htmlBody.Length == 0)
        {
            return Result.Failure<EmailTemplateResponse>(PlatformConfigurationErrors.EmailTemplateContentRequired);
        }

        var payload = JsonSerializer.Serialize(
            new EmailTemplatePayload(subject, htmlBody, NormalizeOptional(request.TextBody)),
            PlatformConfigurationJson.Options);

        var existing = await _settings.GetByKeyAsync(
            definition.SettingKey,
            SystemSettingScope.Global,
            null,
            null,
            cancellationToken);

        if (existing is null)
        {
            var created = SystemSetting.Create(
                SystemSettingId.New(),
                definition.SettingKey,
                payload,
                SystemSettingScope.Global,
                null,
                null,
                _clock.UtcNow);

            if (created.IsFailure)
            {
                return Result.Failure<EmailTemplateResponse>(created.Error);
            }

            await _settings.AddAsync(created.Value, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return Result.Success(EmailTemplateMapper.ToResponse(definition, created.Value));
        }

        var updated = existing.Update(payload, SystemSettingScope.Global, null, null, _clock.UtcNow);
        if (updated.IsFailure)
        {
            return Result.Failure<EmailTemplateResponse>(updated.Error);
        }

        _settings.Update(existing);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(EmailTemplateMapper.ToResponse(definition, existing));
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

internal sealed record EmailTemplateDefinition(
    string Key,
    string SettingKey,
    string DisplayName,
    string Description,
    string DefaultSubject,
    string DefaultHtmlBody,
    IReadOnlyList<string> Variables);

internal sealed record EmailTemplatePayload(string Subject, string HtmlBody, string? TextBody);

internal static class EmailTemplateDefinitions
{
    public static readonly IReadOnlyList<EmailTemplateDefinition> All =
    [
        new(
            "auth.magic-link",
            "email_templates.auth.magic_link",
            "Magic Link",
            "Passwordless sign-in email.",
            "Your Social Studio sign-in link",
            "<p>Hello {{userEmail}},</p><p>Use this link to sign in:</p><p><a href=\"{{magicLinkUrl}}\">Sign in</a></p>",
            ["{{appName}}", "{{userEmail}}", "{{magicLinkUrl}}", "{{token}}"]),
        new(
            "auth.password-reset",
            "email_templates.auth.password_reset",
            "Renew Password",
            "Password reset email for the authentication flow.",
            "Reset your Social Studio password",
            "<p>Hello {{userEmail}},</p><p>Use this link to reset your password:</p><p><a href=\"{{resetPasswordUrl}}\">Reset password</a></p>",
            ["{{appName}}", "{{userEmail}}", "{{resetPasswordUrl}}", "{{token}}"]),
        new(
            "auth.confirm-email",
            "email_templates.auth.confirm_email",
            "Confirm Email",
            "Email confirmation message for newly registered users.",
            "Confirm your Social Studio email",
            "<p>Hello {{userEmail}},</p><p>Please confirm your email:</p><p><a href=\"{{confirmEmailUrl}}\">Confirm email</a></p>",
            ["{{appName}}", "{{userEmail}}", "{{confirmEmailUrl}}", "{{token}}"]),
        new(
            "auth.invite-user",
            "email_templates.auth.invite_user",
            "User Invitation",
            "Invitation email for users added by an administrator.",
            "You have been invited to Social Studio",
            "<p>Hello {{userEmail}},</p><p>You were invited to join {{organizationName}}.</p><p><a href=\"{{inviteUrl}}\">Accept invitation</a></p>",
            ["{{appName}}", "{{userEmail}}", "{{organizationName}}", "{{inviteUrl}}", "{{token}}"])
    ];

    public static EmailTemplateDefinition? Find(string key) =>
        All.FirstOrDefault(x => string.Equals(x.Key, key?.Trim(), StringComparison.OrdinalIgnoreCase));
}

internal static class EmailTemplateMapper
{
    public static EmailTemplateResponse ToResponse(EmailTemplateDefinition definition, SystemSetting? setting)
    {
        var payload = Deserialize(setting?.Value);
        return new EmailTemplateResponse(
            definition.Key,
            definition.DisplayName,
            definition.Description,
            payload?.Subject ?? definition.DefaultSubject,
            payload?.HtmlBody ?? definition.DefaultHtmlBody,
            payload?.TextBody,
            definition.Variables,
            setting is not null,
            setting?.UpdatedAtUtc);
    }

    private static EmailTemplatePayload? Deserialize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<EmailTemplatePayload>(value, PlatformConfigurationJson.Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
