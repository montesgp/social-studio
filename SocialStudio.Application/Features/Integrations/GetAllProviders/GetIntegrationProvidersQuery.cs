using MediatR;
using SocialStudio.Application.Features.Integrations;

namespace SocialStudio.Application.Features.Integrations.GetAllProviders;

public sealed record GetIntegrationProvidersQuery : IRequest<IReadOnlyList<IntegrationProviderResponse>>;
