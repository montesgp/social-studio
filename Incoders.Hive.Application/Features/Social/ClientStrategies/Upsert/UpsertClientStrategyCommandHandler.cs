using Incoders.Hive.Application.Abstractions;
using Incoders.Hive.Application.Abstractions.Social;
using Incoders.Hive.Application.Features.Social.ClientStrategies.GetByClientId;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Social.ClientStrategies;
using MediatR;

namespace Incoders.Hive.Application.Features.Social.ClientStrategies.Upsert;

public sealed class UpsertClientStrategyCommandHandler
    : IRequestHandler<UpsertClientStrategyCommand, Result<ClientStrategyResponse>>
{
    private readonly IClientStrategyRepository _strategies;
    private readonly IClock _clock;

    public UpsertClientStrategyCommandHandler(
        IClientStrategyRepository strategies,
        IClock clock)
    {
        _strategies = strategies;
        _clock = clock;
    }

    public async Task<Result<ClientStrategyResponse>> Handle(
        UpsertClientStrategyCommand request,
        CancellationToken cancellationToken)
    {
        var existing = await _strategies.GetByClientIdAsync(
            request.ClientId, request.OrganizationId, cancellationToken);

        if (existing is null)
        {
            // CREATE path
            var createResult = ClientStrategy.Create(
                ClientStrategyId.New(),
                request.ClientId,
                request.OrganizationId,
                request.ContentPillars,
                request.ToneKeywords,
                request.TargetAudience,
                request.PostingFrequency,
                request.Notes,
                _clock.UtcNow);

            if (createResult.IsFailure)
            {
                return Result.Failure<ClientStrategyResponse>(createResult.Error);
            }

            _strategies.Add(createResult.Value);
            await _strategies.SaveChangesAsync(cancellationToken);

            return Result.Success(ClientStrategyResponse.FromDomain(createResult.Value));
        }

        // UPDATE path
        var updateResult = existing.Update(
            request.ContentPillars,
            request.ToneKeywords,
            request.TargetAudience,
            request.PostingFrequency,
            request.Notes,
            _clock.UtcNow);

        if (updateResult.IsFailure)
        {
            return Result.Failure<ClientStrategyResponse>(updateResult.Error);
        }

        _strategies.Update(existing);
        await _strategies.SaveChangesAsync(cancellationToken);

        return Result.Success(ClientStrategyResponse.FromDomain(existing));
    }
}
