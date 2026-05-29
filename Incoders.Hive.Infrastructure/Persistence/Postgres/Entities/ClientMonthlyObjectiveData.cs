namespace Incoders.Hive.Infrastructure.Persistence.Postgres.Entities;

/// <summary>
/// EF Core persistence entity for social.client_monthly_objectives.
/// No annotations — all configuration done via fluent API in AppIdentityDbContext.
/// No navigation properties — sibling aggregate pattern.
/// </summary>
public sealed class ClientMonthlyObjectiveData
{
    public Guid Id { get; set; }

    public Guid ClientId { get; set; }

    public Guid OrganizationId { get; set; }

    public int Year { get; set; }

    public int Month { get; set; }

    public string Objectives { get; set; } = string.Empty;

    public List<string> Kpis { get; set; } = new();

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }
}
