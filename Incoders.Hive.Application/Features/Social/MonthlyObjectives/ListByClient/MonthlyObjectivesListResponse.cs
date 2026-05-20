using Incoders.Hive.Application.Features.Social.MonthlyObjectives.GetByClientAndMonth;

namespace Incoders.Hive.Application.Features.Social.MonthlyObjectives.ListByClient;

public sealed record MonthlyObjectivesListResponse(IReadOnlyList<MonthlyObjectiveResponse> Items);
