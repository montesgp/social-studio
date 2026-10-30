using System.Text.Json;
using SocialStudio.Application.Abstractions;
using SocialStudio.Domain.SystemSettings;

namespace SocialStudio.Infrastructure.PlatformConfiguration;

public sealed class PlatformEmailTemplateSeeder
{
    private const string LogoUrl = "https://www.incoders.com.ar/assets/imgs/logotypes/incoders-logo-complete-dark.png";
    private const string WebsiteUrl = "https://www.incoders.com.ar/";
    private const string ProductName = "Social Studio";
    private const string UserEmailPlaceholder = "{{userEmail}}";
    private const string AppNamePlaceholder = "{{appName}}";
    private const string OrganizationNamePlaceholder = "{{organizationName}}";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static readonly IReadOnlyList<TemplateSeed> Seeds =
    [
        new(
            "email_templates.auth.confirm_email",
            "Confirmá tu email en {{appName}}",
            BuildHtml(
                title: "Confirmá tu correo",
                messageHtml: $"Ya casi terminamos de activar tu cuenta en <strong>{AppNamePlaceholder}</strong>. Confirmá tu email para habilitar el acceso a todos los flujos de Social Studio.",
                buttonLabel: "Confirmar email",
                actionUrlPlaceholder: "{{confirmEmailUrl}}",
                safetyNote: "Si no creaste esta cuenta, podés ignorar este correo."),
            BuildText(
                headline: $"Hola {UserEmailPlaceholder}, confirmá tu correo para activar tu acceso a {AppNamePlaceholder}.",
                actionLabel: "Enlace de confirmación:",
                actionUrlPlaceholder: "{{confirmEmailUrl}}",
                safetyNote: "Si no creaste esta cuenta, podés ignorar este correo.")
        ),
        new(
            "email_templates.auth.password_reset",
            "Restablecé tu contraseña en {{appName}}",
            BuildHtml(
                title: "Restablecé tu contraseña",
                messageHtml: $"Recibimos una solicitud para restablecer la contraseña de tu cuenta en <strong>{AppNamePlaceholder}</strong>.",
                buttonLabel: "Crear nueva contraseña",
                actionUrlPlaceholder: "{{resetPasswordUrl}}",
                safetyNote: "Si no solicitaste este cambio, ignorá este correo y tu contraseña seguirá igual."),
            BuildText(
                headline: $"Hola {UserEmailPlaceholder}, recibimos una solicitud para restablecer tu contraseña en {AppNamePlaceholder}.",
                actionLabel: "Enlace para crear una nueva contraseña:",
                actionUrlPlaceholder: "{{resetPasswordUrl}}",
                safetyNote: "Si no solicitaste este cambio, ignorá este correo y tu contraseña seguirá igual.")
        ),
        new(
            "email_templates.auth.magic_link",
            "Acceso seguro a {{appName}}",
            BuildHtml(
                title: "Ingresá sin contraseña",
                messageHtml: $"Usá este acceso seguro para ingresar a <strong>{AppNamePlaceholder}</strong> sin escribir tu contraseña.",
                buttonLabel: "Ingresar ahora",
                actionUrlPlaceholder: "{{magicLinkUrl}}",
                safetyNote: "Si no solicitaste este acceso, podés ignorar este correo."),
            BuildText(
                headline: $"Hola {UserEmailPlaceholder}, usá este enlace para ingresar a {AppNamePlaceholder} sin contraseña.",
                actionLabel: "Enlace de acceso seguro:",
                actionUrlPlaceholder: "{{magicLinkUrl}}",
                safetyNote: "Si no solicitaste este acceso, podés ignorar este correo.")
        ),
        new(
            "email_templates.auth.invite_user",
            "Te invitaron a {{organizationName}} en {{appName}}",
            BuildHtml(
                title: "Tenés una invitación",
                messageHtml: $"Te invitaron a unirte a <strong>{OrganizationNamePlaceholder}</strong> en <strong>{AppNamePlaceholder}</strong>.",
                buttonLabel: "Aceptar invitación",
                actionUrlPlaceholder: "{{inviteUrl}}",
                safetyNote: "Si no esperabas esta invitación, podés ignorar este correo."),
            BuildText(
                headline: $"Hola {UserEmailPlaceholder}, te invitaron a {OrganizationNamePlaceholder} en {AppNamePlaceholder}.",
                actionLabel: "Enlace para aceptar la invitación:",
                actionUrlPlaceholder: "{{inviteUrl}}",
                safetyNote: "Si no esperabas esta invitación, podés ignorar este correo.")
        ),
    ];

    private readonly ISystemSettingRepository _settings;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;

    public PlatformEmailTemplateSeeder(
        ISystemSettingRepository settings,
        IUnitOfWork unitOfWork,
        IClock clock)
    {
        _settings = settings;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        var hasChanges = false;

        foreach (var seed in Seeds)
        {
            var existing = await _settings.GetByKeyAsync(
                seed.SettingKey,
                SystemSettingScope.Global,
                null,
                null,
                cancellationToken);

            if (existing is not null)
            {
                continue;
            }

            var payload = JsonSerializer.Serialize(
                new EmailTemplateSeedPayload(seed.Subject, seed.HtmlBody, seed.TextBody),
                JsonOptions);

            var createResult = SystemSetting.Create(
                SystemSettingId.New(),
                seed.SettingKey,
                payload,
                SystemSettingScope.Global,
                null,
                null,
                _clock.UtcNow);

            if (createResult.IsFailure)
            {
                throw new InvalidOperationException(
                    $"Failed to seed platform email template '{seed.SettingKey}': {createResult.Error.Code}");
            }

            await _settings.AddAsync(createResult.Value, cancellationToken);
            hasChanges = true;
        }

        if (hasChanges)
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
    }

    private static string BuildHtml(
        string title,
        string messageHtml,
        string buttonLabel,
        string actionUrlPlaceholder,
        string safetyNote)
    {
        return $"""
<!DOCTYPE html>
<html lang="es">
  <head>
    <meta charset="UTF-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1.0" />
    <title>{title}</title>
  </head>
  <body style="margin:0;padding:0;background-color:#f3f6fa;font-family:Arial,Helvetica,sans-serif;color:#1f2937;">
    <table role="presentation" width="100%" cellspacing="0" cellpadding="0" style="background-color:#f3f6fa;margin:0;padding:24px 0;">
      <tr>
        <td align="center">
          <table role="presentation" width="100%" cellspacing="0" cellpadding="0" style="max-width:620px;background:#ffffff;border-radius:18px;overflow:hidden;box-shadow:0 12px 30px rgba(15,23,42,0.1);">
            <tr>
              <td align="center" style="padding:30px 24px 18px 24px;background:linear-gradient(145deg,#f9fdff 0%,#eefcff 100%);">
                <img
                  src="{LogoUrl}"
                  alt="Incoders"
                  width="220"
                  style="display:block;max-width:220px;width:100%;height:auto;border:0;"
                />
              </td>
            </tr>

            <tr>
              <td style="padding:18px 36px 10px 36px;text-align:center;">
                <h1 style="margin:0;font-size:28px;line-height:36px;color:#1f2937;font-weight:800;">
                  {title}
                </h1>
              </td>
            </tr>

            <tr>
              <td style="padding:4px 36px 0 36px;text-align:center;">
                <p style="margin:0;font-size:16px;line-height:26px;color:#4b5563;">
                  Hola <strong style="color:#111827;">{UserEmailPlaceholder}</strong>,
                </p>
              </td>
            </tr>

            <tr>
              <td style="padding:10px 36px 0 36px;text-align:center;">
                <p style="margin:0;font-size:16px;line-height:26px;color:#4b5563;">
                  {messageHtml}
                </p>
              </td>
            </tr>

            <tr>
              <td align="center" style="padding:30px 36px 20px 36px;">
                <a
                  href="{actionUrlPlaceholder}"
                  style="display:inline-block;background-color:#00d9ff;color:#06202b;text-decoration:none;font-size:16px;font-weight:800;padding:14px 28px;border-radius:12px;"
                >
                  {buttonLabel}
                </a>
              </td>
            </tr>

            <tr>
              <td style="padding:0 36px 12px 36px;">
                <p style="margin:0;font-size:14px;line-height:24px;color:#6b7280;text-align:center;">
                  {safetyNote}
                </p>
              </td>
            </tr>

            <tr>
              <td style="padding:0 36px 26px 36px;">
                <p style="margin:0;font-size:13px;line-height:22px;word-break:break-all;color:#0f6f83;text-align:center;">
                  {actionUrlPlaceholder}
                </p>
              </td>
            </tr>

            <tr>
              <td style="padding:20px 36px;background-color:#f8fafc;border-top:1px solid #e5e7eb;text-align:center;">
                <p style="margin:0;font-size:12px;line-height:20px;color:#9ca3af;">
                  {ProductName} · automatización y operaciones de contenido social
                </p>
                <p style="margin:6px 0 0 0;font-size:12px;line-height:20px;color:#9ca3af;">
                  <a href="{WebsiteUrl}" style="color:#0f6f83;text-decoration:none;">www.incoders.com.ar</a>
                </p>
              </td>
            </tr>
          </table>
        </td>
      </tr>
    </table>
  </body>
</html>
""";
    }

    private static string BuildText(
        string headline,
        string actionLabel,
        string actionUrlPlaceholder,
        string safetyNote)
    {
        return $"""
{headline}

{actionLabel}
{actionUrlPlaceholder}

{safetyNote}

Si el botón no funciona, copiá y pegá ese enlace en tu navegador.

{ProductName}
{WebsiteUrl}
""";
    }

    private sealed record TemplateSeed(
        string SettingKey,
        string Subject,
        string HtmlBody,
        string? TextBody);

    private sealed record EmailTemplateSeedPayload(
        string Subject,
        string HtmlBody,
        string? TextBody);
}
