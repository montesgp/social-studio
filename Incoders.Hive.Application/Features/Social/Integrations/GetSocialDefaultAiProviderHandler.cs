using Incoders.Hive.Application.Abstractions.Integrations;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Integrations;
using MediatR;

namespace Incoders.Hive.Application.Features.Social.Integrations;

public sealed class GetSocialDefaultAiProviderHandler
    : IRequestHandler<GetSocialDefaultAiProviderQuery, Result<SocialDefaultAiProviderResponse>>
{
    private readonly IIntegrationResolver _resolver;

    public GetSocialDefaultAiProviderHandler(IIntegrationResolver resolver)
    {
        _resolver = resolver;
    }

    public async Task<Result<SocialDefaultAiProviderResponse>> Handle(
        GetSocialDefaultAiProviderQuery request,
        CancellationToken cancellationToken)
    {
        if (request.OrganizationId == Guid.Empty)
        {
            return Result.Failure<SocialDefaultAiProviderResponse>(IntegrationErrors.OrganizationRequired);
        }

        var resolved = await _resolver.ResolveDefaultAiProviderAsync(request.OrganizationId, cancellationToken);
        if (resolved.IsFailure)
        {
            return Result.Failure<SocialDefaultAiProviderResponse>(resolved.Error);
        }

        return Result.Success(SocialDefaultAiProviderResponse.FromResolved(resolved.Value));
    }
}