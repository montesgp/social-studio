using Incoders.Hive.Domain.Social.ProfileAnalysis;

namespace Incoders.Hive.Application.Abstractions.Social;

/// <summary>
/// Repository port for <see cref="ProfileAnalysis"/> aggregate persistence.
/// All queries are scoped to <paramref name="organizationId"/> — the org-scoping invariant
/// mirrors the single-analyzer repository pattern.
/// Implemented exclusively in Infrastructure (EF / Postgres). No provider types leak here.
/// </summary>
public interface IProfileAnalysisRepository
{
    Task AddAsync(
        ProfileAnalysis analysis,
        CancellationToken cancellationToken = default);

    Task<ProfileAnalysis?> GetByIdAsync(
        Guid analysisId,
        Guid organizationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns a paged, newest-first list of analyses scoped to <paramref name="organizationId"/>.
    /// </summary>
    Task<ProfileAnalysisHistoryPage> GetByOrganizationAsync(
        Guid organizationId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task UpdateAsync(
        ProfileAnalysis analysis,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Hard-deletes the analysis. Returns <c>true</c> when deleted; <c>false</c> when not found
    /// or not owned by the organization.
    /// </summary>
    Task<bool> DeleteAsync(
        Guid analysisId,
        Guid organizationId,
        CancellationToken cancellationToken = default);
}

/// <summary>A single row in the profile-analysis history list.</summary>
public sealed record ProfileAnalysisHistoryItem(
    Guid Id,
    Guid OrganizationId,
    Guid? ClientId,
    Guid? OwnAccountId,
    string? ClientName,
    string ProfileUrlOrHandle,
    string Status,
    string? FailureCode,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    DateTime? CompletedAtUtc);

/// <summary>Paged list result for <see cref="IProfileAnalysisRepository.GetByOrganizationAsync"/>.</summary>
public sealed record ProfileAnalysisHistoryPage(
    IReadOnlyList<ProfileAnalysisHistoryItem> Items,
    int TotalCount,
    int Page,
    int PageSize);
