using System.Text.Json;
using Incoders.Hive.Application.Abstractions;
using Incoders.Hive.Application.Abstractions.Integrations;
using Incoders.Hive.Application.Features.PlatformConfiguration;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.SystemSettings;
using MediatR;

namespace Incoders.Hive.Application.Features.PlatformConfiguration.Integrations;

public sealed record PlatformIntegrationSummaryResponse(
    string Key,
    string DisplayName,
    string Description,
    string Category,
    bool Enabled,
    bool IsConfigured,
    DateTime? UpdatedAtUtc);

public sealed record EmailSmtpConfigurationResponse(
    string Key,
    string DisplayName,
    bool Enabled,
    string Host,
    int Port,
    bool UseSsl,
    string Username,
    string FromEmail,
    string FromName,
    bool PasswordConfigured,
    DateTime? UpdatedAtUtc);

public sealed record GetPlatformIntegrationsQuery()
    : IRequest<Result<IReadOnlyList<PlatformIntegrationSummaryResponse>>>;

public sealed record GetEmailSmtpConfigurationQuery()
    : IRequest<Result<EmailSmtpConfigurationResponse>>;

public sealed record UpsertEmailSmtpConfigurationCommand(
    bool Enabled,
    string Host,
    int Port,
    bool UseSsl,
    string Username,
    string? Password,
    string FromEmail,
    string FromName)
    : IRequest<Result<EmailSmtpConfigurationResponse>>;

public sealed class GetPlatformIntegrationsHandler
    : IRequestHandler<GetPlatformIntegrationsQuery, Result<IReadOnlyList<PlatformIntegrationSummaryResponse>>>
{
    private readonly ISystemSettingRepository _settings;

    public GetPlatformIntegrationsHandler(ISystemSettingRepository settings)
    {
        _settings = settings;
    }

    public async Task<Result<IReadOnlyList<PlatformIntegrationSummaryResponse>>> Handle(
        GetPlatformIntegrationsQuery request,
        CancellationToken cancellationToken)
    {
        var smtp = await PlatformIntegrationSettings.GetGlobalAsync(
            _settings,
            PlatformIntegrationSettings.EmailSmtpKey,
            cancellationToken);

        var smtpPayload = EmailSmtpMapper.Deserialize(smtp?.Value);

        IReadOnlyList<PlatformIntegrationSummaryResponse> items =
        [
            new(
                "email-smtp",
                "Email SMTP",
                "Platform email delivery service used by authentication and system notifications.",
                "Messaging",
                smtpPayload?.Enabled ?? false,
                smtp is not null,
                smtp?.UpdatedAtUtc),
            new(
                "whatsapp",
                "WhatsApp",
                "Reserved platform messaging integration for future notification flows.",
                "Messaging",
                false,
                false,
                null)
        ];

        return Result.Success(items);
    }
}

public sealed class GetEmailSmtpConfigurationHandler
    : IRequestHandler<GetEmailSmtpConfigurationQuery, Result<EmailSmtpConfigurationResponse>>
{
    private readonly ISystemSettingRepository _settings;

    public GetEmailSmtpConfigurationHandler(ISystemSettingRepository settings)
    {
        _settings = settings;
    }

    public async Task<Result<EmailSmtpConfigurationResponse>> Handle(
        GetEmailSmtpConfigurationQuery request,
        CancellationToken cancellationToken)
    {
        var setting = await PlatformIntegrationSettings.GetGlobalAsync(
            _settings,
            PlatformIntegrationSettings.EmailSmtpKey,
            cancellationToken);

        return Result.Success(EmailSmtpMapper.ToResponse(setting));
    }
}

public sealed class UpsertEmailSmtpConfigurationHandler
    : IRequestHandler<UpsertEmailSmtpConfigurationCommand, Result<EmailSmtpConfigurationResponse>>
{
    private readonly ISystemSettingRepository _settings;
    private readonly IIntegrationSecretProtector _secretProtector;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;

    public UpsertEmailSmtpConfigurationHandler(
        ISystemSettingRepository settings,
        IIntegrationSecretProtector secretProtector,
        IUnitOfWork unitOfWork,
        IClock clock)
    {
        _settings = settings;
        _secretProtector = secretProtector;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<Result<EmailSmtpConfigurationResponse>> Handle(
        UpsertEmailSmtpConfigurationCommand request,
        CancellationToken cancellationToken)
    {
        if (!IsValid(request))
        {
            return Result.Failure<EmailSmtpConfigurationResponse>(PlatformConfigurationErrors.SmtpConfigurationInvalid);
        }

        var existing = await PlatformIntegrationSettings.GetGlobalAsync(
            _settings,
            PlatformIntegrationSettings.EmailSmtpKey,
            cancellationToken);
        var existingPayload = EmailSmtpMapper.Deserialize(existing?.Value);
        var protectedPassword = string.IsNullOrWhiteSpace(request.Password)
            ? existingPayload?.ProtectedPassword
            : _secretProtector.Protect(request.Password.Trim());

        var payload = new EmailSmtpConfigurationPayload(
            request.Enabled,
            request.Host.Trim(),
            request.Port,
            request.UseSsl,
            request.Username.Trim(),
            protectedPassword,
            request.FromEmail.Trim(),
            request.FromName.Trim());
        var serialized = JsonSerializer.Serialize(payload, PlatformConfigurationJson.Options);

        if (existing is null)
        {
            var created = SystemSetting.Create(
                SystemSettingId.New(),
                PlatformIntegrationSettings.EmailSmtpKey,
                serialized,
                SystemSettingScope.Global,
                null,
                null,
                _clock.UtcNow);

            if (created.IsFailure)
            {
                return Result.Failure<EmailSmtpConfigurationResponse>(created.Error);
            }

            await _settings.AddAsync(created.Value, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return Result.Success(EmailSmtpMapper.ToResponse(created.Value));
        }

        var updated = existing.Update(serialized, SystemSettingScope.Global, null, null, _clock.UtcNow);
        if (updated.IsFailure)
        {
            return Result.Failure<EmailSmtpConfigurationResponse>(updated.Error);
        }

        _settings.Update(existing);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(EmailSmtpMapper.ToResponse(existing));
    }

    private static bool IsValid(UpsertEmailSmtpConfigurationCommand request)
    {
        if (request.Port is < 1 or > 65_535)
        {
            return false;
        }

        if (!request.Enabled)
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(request.Host)
               && !string.IsNullOrWhiteSpace(request.FromEmail)
               && request.FromEmail.Contains('@', StringComparison.Ordinal);
    }
}

internal sealed record EmailSmtpConfigurationPayload(
    bool Enabled,
    string Host,
    int Port,
    bool UseSsl,
    string Username,
    string? ProtectedPassword,
    string FromEmail,
    string FromName);

internal static class PlatformIntegrationSettings
{
    public const string EmailSmtpKey = "platform_integrations.email.smtp";

    public static Task<SystemSetting?> GetGlobalAsync(
        ISystemSettingRepository settings,
        string key,
        CancellationToken cancellationToken) =>
        settings.GetByKeyAsync(key, SystemSettingScope.Global, null, null, cancellationToken);
}

internal static class EmailSmtpMapper
{
    public static EmailSmtpConfigurationResponse ToResponse(SystemSetting? setting)
    {
        var payload = Deserialize(setting?.Value);
        return new EmailSmtpConfigurationResponse(
            "email-smtp",
            "Email SMTP",
            payload?.Enabled ?? false,
            payload?.Host ?? string.Empty,
            payload?.Port ?? 587,
            payload?.UseSsl ?? true,
            payload?.Username ?? string.Empty,
            payload?.FromEmail ?? string.Empty,
            payload?.FromName ?? "Incoders Hive",
            !string.IsNullOrWhiteSpace(payload?.ProtectedPassword),
            setting?.UpdatedAtUtc);
    }

    public static EmailSmtpConfigurationPayload? Deserialize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<EmailSmtpConfigurationPayload>(value, PlatformConfigurationJson.Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
