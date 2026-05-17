using Incoders.Hive.Domain.Common;
using MediatR;

namespace Incoders.Hive.Application.Features.Social.Integrations;

public sealed record GetSocialDefaultAiProviderQuery(Guid OrganizationId)
    : IRequest<Result<SocialDefaultAiProviderResponse>>;

public sealed record SocialDefaultAiProviderResponse(
    string ProviderKey,
    string ProviderDisplayName,
    string? Model,
    bool IsReadOnly)
{
    public static SocialDefaultAiProviderResponse FromResolved(Abstractions.Integrations.ResolvedIntegration resolved) =>
        new(resolved.ProviderKey, resolved.ProviderDisplayName, resolved.SelectedAiModel, resolved.IsReadOnly);
}