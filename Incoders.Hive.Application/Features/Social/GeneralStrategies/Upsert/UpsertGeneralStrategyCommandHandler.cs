using Incoders.Hive.Application.Abstractions;
using Incoders.Hive.Application.Abstractions.Social;
using Incoders.Hive.Application.Features.Social.GeneralStrategies.GetByClientId;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Social.GeneralStrategies;
using MediatR;

namespace Incoders.Hive.Application.Features.Social.GeneralStrategies.Upsert;

public sealed class UpsertGeneralStrategyCommandHandler
    : IRequestHandler<UpsertGeneralStrategyCommand, Result<GeneralStrategyResponse>>
{
    private readonly IGeneralStrategyRepository _strategies;
    private readonly IClock _clock;

    public UpsertGeneralStrategyCommandHandler(
        IGeneralStrategyRepository strategies,
        IClock clock)
    {
        _strategies = strategies;
        _clock = clock;
    }

    public async Task<Result<GeneralStrategyResponse>> Handle(
        UpsertGeneralStrategyCommand request,
        CancellationToken cancellationToken)
    {
        var existing = await _strategies.GetByClientIdAsync(
            request.ClientId, request.OrganizationId, cancellationToken);

        if (existing is null)
        {
            // CREATE path
            var createResult = GeneralStrategy.Create(
                GeneralStrategyId.New(),
                request.ClientId,
                request.OrganizationId,
                request.ContentPillars,
                request.TargetAudience,
                request.PostingFrequency,
                request.Notes,
                _clock.UtcNow);

            if (createResult.IsFailure)
            {
                return Result.Failure<GeneralStrategyResponse>(createResult.Error);
            }

            _strategies.Add(createResult.Value);
            await _strategies.SaveChangesAsync(cancellationToken);

            return Result.Success(GeneralStrategyResponse.FromDomain(createResult.Value));
        }

        // UPDATE path
        var updateResult = existing.Update(
            request.ContentPillars,
            request.TargetAudience,
            request.PostingFrequency,
            request.Notes,
            _clock.UtcNow);

        if (updateResult.IsFailure)
        {
            return Result.Failure<GeneralStrategyResponse>(updateResult.Error);
        }

        _strategies.Update(existing);
        await _strategies.SaveChangesAsync(cancellationToken);

        return Result.Success(GeneralStrategyResponse.FromDomain(existing));
    }
}
