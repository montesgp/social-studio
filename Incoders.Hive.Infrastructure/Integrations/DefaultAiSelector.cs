using Incoders.Hive.Application.Abstractions.ContentAnalysis;
using Incoders.Hive.Application.Abstractions.Integrations;
using Incoders.Hive.Domain.Common;

namespace Incoders.Hive.Infrastructure.Integrations;

/// <summary>
/// Infrastructure adapter that implements <see cref="IDefaultAiSelector"/> by delegating
/// to the existing <see cref="IIntegrationResolver.ResolveDefaultAiProviderAsync"/> method.
/// The selection rule (is_default_ai first → last UpdatedAtUtc fallback) is owned by the resolver;
/// this adapter is a thin mapping layer to keep the Application port free of Infrastructure types.
/// </summary>
internal sealed class DefaultAiSelector : IDefaultAiSelector
{
    private readonly IIntegrationResolver _resolver;

    public DefaultAiSelector(IIntegrationResolver resolver)
    {
        _resolver = resolver;
    }

    public async Task<Result<DefaultAiSelection>> SelectAsync(
        Guid organizationId,
        CancellationToken ct = default)
    {
        var resolved = await _resolver.ResolveDefaultAiProviderAsync(organizationId, ct);
        if (resolved.IsFailure)
        {
            return Result.Failure<DefaultAiSelection>(resolved.Error);
        }

        return Result.Success(new DefaultAiSelection(
            resolved.Value.ProviderKey,
            resolved.Value.SelectedAiModel));
    }
}
