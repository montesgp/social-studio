using System.Text;
using SocialStudio.Application.Abstractions.Social.ContentGeneration;
using SocialStudio.Domain.Social.ContentGeneration;

namespace SocialStudio.Application.Features.Social.ContentGeneration.Ideas;

/// <summary>
/// Grounds ideas in what the client DELIBERATELY DECIDED: the authored content strategy, its pillars,
/// and the current monthly objective.
///
/// Strict by design. When nobody has authored a strategy this returns null, the source contributes
/// nothing, and the chain falls through — which is exactly how a hybrid plan turns referential for a
/// client who has not defined one. Loosening this to "use whatever exists" would collapse the
/// distinction with <see cref="ClientKnowledgeIdeaGrounding"/> and both sources would say the same
/// thing twice.
/// </summary>
public sealed class ClientStrategyIdeaGrounding : IClientIdeaGrounding
{
    public const string Key = "client-strategy";

    private readonly IClientBrandSnapshotLoader _snapshots;

    public ClientStrategyIdeaGrounding(IClientBrandSnapshotLoader snapshots)
    {
        ArgumentNullException.ThrowIfNull(snapshots);
        _snapshots = snapshots;
    }

    public string GroundingKey => Key;

    public async Task<string?> BuildAsync(Guid organizationId, Guid clientId, CancellationToken ct)
    {
        var snapshot = await _snapshots.LoadAsync(organizationId, clientId, ct);

        var strategyText = snapshot.ContentStrategy?.StrategyText;
        var pillars = snapshot.Strategy?.ContentPillars ?? [];

        var hasAuthoredStrategy = !string.IsNullOrWhiteSpace(strategyText) || pillars.Count > 0;

        if (!hasAuthoredStrategy)
        {
            return null;
        }

        var builder = new StringBuilder();

        Append(builder, "Estrategia de contenido", strategyText);
        AppendList(builder, "Pilares de contenido", pillars);
        Append(builder, "Audiencia objetivo", snapshot.Strategy?.TargetAudience);
        Append(builder, "Frecuencia de publicación", snapshot.Strategy?.PostingFrequency);
        Append(builder, "Objetivo del mes", snapshot.MonthlyObjective?.Objectives);
        AppendList(builder, "KPIs", snapshot.MonthlyObjective?.Kpis);
        Append(builder, "Sector", snapshot.Client?.SectorOrNiche);

        return builder.ToString();
    }

    internal static void Append(StringBuilder builder, string label, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            builder.Append(label).Append(": ").AppendLine(value.Trim());
        }
    }

    internal static void AppendList(StringBuilder builder, string label, IReadOnlyList<string>? values)
    {
        var present = (values ?? []).Where(v => !string.IsNullOrWhiteSpace(v)).ToList();

        if (present.Count > 0)
        {
            builder.Append(label).Append(": ").AppendLine(string.Join(", ", present));
        }
    }
}

/// <summary>
/// Grounds ideas in EVERYTHING we know about the client — who they are, their niche, their voice, and
/// the accounts they look up to — with no requirement that anyone authored a strategy.
///
/// Permissive by design: this is the source that keeps producing for a client who never filled in a
/// plan. It gives up only when we know effectively nothing, because asking a model to invent topics
/// for an unknown client burns tokens to produce generic filler that nobody will publish.
/// </summary>
public sealed class ClientKnowledgeIdeaGrounding : IClientIdeaGrounding
{
    public const string Key = "ai-agent";

    private readonly IClientBrandSnapshotLoader _snapshots;

    public ClientKnowledgeIdeaGrounding(IClientBrandSnapshotLoader snapshots)
    {
        ArgumentNullException.ThrowIfNull(snapshots);
        _snapshots = snapshots;
    }

    public string GroundingKey => Key;

    public async Task<string?> BuildAsync(Guid organizationId, Guid clientId, CancellationToken ct)
    {
        var snapshot = await _snapshots.LoadAsync(organizationId, clientId, ct);

        var builder = new StringBuilder();

        ClientStrategyIdeaGrounding.Append(builder, "Cliente", snapshot.Client?.Name);
        ClientStrategyIdeaGrounding.Append(builder, "Descripción", snapshot.Client?.Description);
        ClientStrategyIdeaGrounding.Append(builder, "Sector o nicho", snapshot.Client?.SectorOrNiche);
        ClientStrategyIdeaGrounding.Append(builder, "Voz y tono", snapshot.Brand?.VoiceAndTone);
        ClientStrategyIdeaGrounding.Append(builder, "Notas de marca", snapshot.Brand?.Notes);
        ClientStrategyIdeaGrounding.AppendList(builder, "Pilares de contenido", snapshot.Strategy?.ContentPillars);
        ClientStrategyIdeaGrounding.Append(builder, "Audiencia objetivo", snapshot.Strategy?.TargetAudience);
        ClientStrategyIdeaGrounding.Append(builder, "Objetivo del mes", snapshot.MonthlyObjective?.Objectives);
        ClientStrategyIdeaGrounding.Append(builder, "Estrategia de contenido", snapshot.ContentStrategy?.StrategyText);
        AppendAccounts(builder, "Cuentas referentes", snapshot.ReferentAccounts);
        AppendAccounts(builder, "Competidores", snapshot.CompetitorAccounts);

        // A snapshot with every field inferred means the client record is effectively blank.
        return builder.Length == 0 ? null : builder.ToString();
    }

    private static void AppendAccounts(
        StringBuilder builder,
        string label,
        IReadOnlyList<ClientAccountSnapshot>? accounts)
    {
        var handles = (accounts ?? [])
            .Select(account => account.Handle ?? account.ProfileUrl)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToList();

        if (handles.Count > 0)
        {
            builder.Append(label).Append(": ").AppendLine(string.Join(", ", handles));
        }
    }
}
