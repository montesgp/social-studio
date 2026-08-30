using Incoders.Hive.Domain.Social.ContentPlans;

namespace Incoders.Hive.Application.Abstractions.Social;

public interface IContentPlanRepository
{
    Task<ContentPlan?> GetByClientAndPeriodAsync(
        Guid organizationId,
        Guid clientId,
        Period period,
        CancellationToken ct);

    Task<IReadOnlyList<ContentPlan>> ListByClientAsync(
        Guid organizationId,
        Guid clientId,
        CancellationToken ct);

    Task UpsertAsync(ContentPlan plan, CancellationToken ct);

    Task LinkSlotPackageAsync(
        Guid planId,
        CalendarSlotId slotId,
        Guid packageId,
        CancellationToken ct);
}
