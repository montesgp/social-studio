using MediatR;
using Incoders.Hive.Application.Features.Integrations;

namespace Incoders.Hive.Application.Features.Integrations.GetAllProviders;

public sealed record GetIntegrationProvidersQuery : IRequest<IReadOnlyList<IntegrationProviderResponse>>;
