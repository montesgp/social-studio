using System.Text.Json;
using SocialStudio.Application.Abstractions.Social.ContentGeneration;
using SocialStudio.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;

namespace SocialStudio.Infrastructure.Social.ContentGeneration;

/// <summary>
/// Infrastructure adapter for <see cref="IClientAestheticDescriptorReader"/>.
/// Reads <c>DetectedBackgroundImages</c> text descriptors from the most recent completed
/// <c>ProfileAiAnalysis</c> for the given <c>(orgId, clientId)</c> pair.
///
/// <para>
/// No Apify call is made — this is a pure read over already-persisted AI analysis JSON
/// stored in <c>social.profile_analyses.ai_analysis_json</c> (PA-SEAM-01, PA-SEAM-03).
/// </para>
/// </summary>
public sealed class EfClientAestheticDescriptorReader : IClientAestheticDescriptorReader
{
    private static readonly JsonSerializerOptions JsonOpts =
        new(JsonSerializerDefaults.Web);

    private readonly AppIdentityDbContext _db;

    public EfClientAestheticDescriptorReader(AppIdentityDbContext db)
    {
        _db = db;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> GetDescriptorsAsync(
        Guid orgId,
        Guid clientId,
        CancellationToken ct)
    {
        // Select the most recently completed (Analyzed) analysis for this client.
        // In-progress, Failed, or other statuses are excluded (PA-SEAM-01d).
        var row = await _db.ProfileAnalyses
            .AsNoTracking()
            .Where(x =>
                x.OrganizationId == orgId &&
                x.ClientId == clientId &&
                x.Status == "Analyzed" &&
                x.AiAnalysisJson != null)
            .OrderByDescending(x => x.CompletedAtUtc)
            .Select(x => x.AiAnalysisJson)
            .FirstOrDefaultAsync(ct);

        if (row is null)
        {
            return [];
        }

        return DeserializeDescriptors(row);
    }

    // ── Private helpers ───────────────────────────────────────────────────

    private static IReadOnlyList<string> DeserializeDescriptors(string json)
    {
        try
        {
            var dto = JsonSerializer.Deserialize<AiAnalysisProjection>(json, JsonOpts);
            return dto?.DetectedBackgroundImages ?? [];
        }
        catch
        {
            // Malformed JSON — degrade gracefully; the pipeline falls back to Creative.
            return [];
        }
    }

    /// <summary>
    /// Minimal projection of the AI analysis JSON — only the field we need.
    /// Tolerant of missing keys (all properties nullable/optional).
    /// </summary>
    private sealed record AiAnalysisProjection(
        IReadOnlyList<string>? DetectedBackgroundImages = null);
}
