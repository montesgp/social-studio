namespace Incoders.Hive.Application.Features.Integrations;

public sealed record IntegrationSetupChecklistItemResponse(
    string Key,
    string Label,
    bool IsRequired,
    bool IsCompleted,
    string Requirement);