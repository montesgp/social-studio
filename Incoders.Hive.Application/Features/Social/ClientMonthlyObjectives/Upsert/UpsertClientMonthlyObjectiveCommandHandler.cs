using Incoders.Hive.Application.Abstractions;
using Incoders.Hive.Application.Abstractions.Social;
using Incoders.Hive.Application.Features.Social.ClientMonthlyObjectives.GetByClientAndMonth;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Social.ClientMonthlyObjectives;
using MediatR;

namespace Incoders.Hive.Application.Features.Social.ClientMonthlyObjectives.Upsert;

public sealed class UpsertClientMonthlyObjectiveCommandHandler
    : IRequestHandler<UpsertClientMonthlyObjectiveCommand, Result<ClientMonthlyObjectiveResponse>>
{
    private readonly IClientMonthlyObjectiveRepository _objectives;
    private readonly IClock _clock;

    public UpsertClientMonthlyObjectiveCommandHandler(
        IClientMonthlyObjectiveRepository objectives,
        IClock clock)
    {
        _objectives = objectives;
        _clock = clock;
    }

    public async Task<Result<ClientMonthlyObjectiveResponse>> Handle(
        UpsertClientMonthlyObjectiveCommand request,
        CancellationToken cancellationToken)
    {
        var existing = await _objectives.GetByClientAndMonthAsync(
            request.ClientId, request.OrganizationId, request.Year, request.Month, cancellationToken);

        if (existing is null)
        {
            // CREATE path
            var createResult = ClientMonthlyObjective.Create(
                ClientMonthlyObjectiveId.New(),
                request.ClientId,
                request.OrganizationId,
                request.Year,
                request.Month,
                request.Objectives,
                request.Kpis,
                _clock.UtcNow);

            if (createResult.IsFailure)
            {
                return Result.Failure<ClientMonthlyObjectiveResponse>(createResult.Error);
            }

            _objectives.Add(createResult.Value);
            await _objectives.SaveChangesAsync(cancellationToken);

            return Result.Success(ClientMonthlyObjectiveResponse.FromDomain(createResult.Value));
        }

        // UPDATE path
        var updateResult = existing.Update(request.Objectives, request.Kpis, _clock.UtcNow);

        if (updateResult.IsFailure)
        {
            return Result.Failure<ClientMonthlyObjectiveResponse>(updateResult.Error);
        }

        _objectives.Update(existing);
        await _objectives.SaveChangesAsync(cancellationToken);

        return Result.Success(ClientMonthlyObjectiveResponse.FromDomain(existing));
    }
}
