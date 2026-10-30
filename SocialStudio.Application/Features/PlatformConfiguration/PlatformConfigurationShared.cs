using System.Text.Json;
using SocialStudio.Domain.Common;

namespace SocialStudio.Application.Features.PlatformConfiguration;

internal static class PlatformConfigurationJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
}

internal static class PlatformConfigurationErrors
{
    public static readonly Error UnknownEmailTemplate = Error.NotFound(
        "platform_configuration.email_template_unknown",
        "platform_configuration.errors.email_template_unknown");

    public static readonly Error EmailTemplateContentRequired = Error.Validation(
        "platform_configuration.email_template_content_required",
        "platform_configuration.errors.email_template_content_required");

    public static readonly Error SmtpConfigurationInvalid = Error.Validation(
        "platform_configuration.smtp_invalid",
        "platform_configuration.errors.smtp_invalid");
}
