using Incoders.Hive.Application.Abstractions;
using Incoders.Hive.Application.Abstractions.Social;
using Incoders.Hive.Application.Features.Social.MonthlyObjectives.GetByClientAndMonth;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Social.MonthlyObjectives;
using MediatR;

namespace Incoders.Hive.Application.Features.Social.MonthlyObjectives.Upsert;

public sealed class UpsertMonthlyObjectiveCommandHandler
    : IRequestHandler<UpsertMonthlyObjectiveCommand, Result<MonthlyObjectiveResponse>>
{
    private readonly IMonthlyObjectiveRepository _objectives;
    private readonly IClock _clock;

    public UpsertMonthlyObjectiveCommandHandler(
        IMonthlyObjectiveRepository objectives,
        IClock clock)
    {
        _objectives = objectives;
        _clock = clock;
    }

    public async Task<Result<MonthlyObjectiveResponse>> Handle(
        UpsertMonthlyObjectiveCommand request,
        CancellationToken cancellationToken)
    {
        var existing = await _objectives.GetByClientAndMonthAsync(
            request.ClientId, request.OrganizationId, request.Year, request.Month, cancellationToken);

        if (existing is null)
        {
            // CREATE path
            var createResult = MonthlyObjective.Create(
                MonthlyObjectiveId.New(),
                request.ClientId,
                request.OrganizationId,
                request.Year,
                request.Month,
                request.Objectives,
                request.Kpis,
                _clock.UtcNow);

            if (createResult.IsFailure)
            {
                return Result.Failure<MonthlyObjectiveResponse>(createResult.Error);
            }

            _objectives.Add(createResult.Value);
            await _objectives.SaveChangesAsync(cancellationToken);

            return Result.Success(MonthlyObjectiveResponse.FromDomain(createResult.Value));
        }

        // UPDATE path
        var updateResult = existing.Update(request.Objectives, request.Kpis, _clock.UtcNow);

        if (updateResult.IsFailure)
        {
            return Result.Failure<MonthlyObjectiveResponse>(updateResult.Error);
        }

        _objectives.Update(existing);
        await _objectives.SaveChangesAsync(cancellationToken);

        return Result.Success(MonthlyObjectiveResponse.FromDomain(existing));
    }
}
