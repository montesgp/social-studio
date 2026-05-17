using Incoders.Hive.Domain.Integrations;

namespace Incoders.Hive.Application.Features.Integrations;

public sealed record IntegrationCredentialFieldResponse(
    string Key,
    string Label,
    bool IsSecret,
    bool IsRequired,
    string? Placeholder,
    string? HelpText)
{
    public static IntegrationCredentialFieldResponse FromDomain(IntegrationCredentialField field) =>
        new(field.Key, field.Label, field.IsSecret, field.IsRequired, field.Placeholder, field.HelpText);
}