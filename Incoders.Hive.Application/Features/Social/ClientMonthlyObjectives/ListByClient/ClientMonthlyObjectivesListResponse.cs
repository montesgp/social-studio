using Incoders.Hive.Application.Features.Social.ClientMonthlyObjectives.GetByClientAndMonth;

namespace Incoders.Hive.Application.Features.Social.ClientMonthlyObjectives.ListByClient;

public sealed record ClientMonthlyObjectivesListResponse(IReadOnlyList<ClientMonthlyObjectiveResponse> Items);
