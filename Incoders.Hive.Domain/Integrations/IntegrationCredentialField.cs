namespace Incoders.Hive.Domain.Integrations;

public sealed record IntegrationCredentialField(
    string Key,
    string Label,
    bool IsSecret,
    bool IsRequired,
    string? Placeholder = null,
    string? HelpText = null);