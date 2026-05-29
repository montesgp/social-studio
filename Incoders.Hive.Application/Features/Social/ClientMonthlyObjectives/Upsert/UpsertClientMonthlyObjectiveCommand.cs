using Incoders.Hive.Application.Features.Social.ClientMonthlyObjectives.GetByClientAndMonth;
using Incoders.Hive.Domain.Common;
using MediatR;

namespace Incoders.Hive.Application.Features.Social.ClientMonthlyObjectives.Upsert;

public sealed record UpsertClientMonthlyObjectiveCommand(
    Guid ClientId,
    Guid OrganizationId,
    int Year,
    int Month,
    string Objectives,
    IReadOnlyList<string>? Kpis) : IRequest<Result<ClientMonthlyObjectiveResponse>>;
