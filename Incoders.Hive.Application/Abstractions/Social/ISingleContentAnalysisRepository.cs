using Incoders.Hive.Domain.Social.SingleAnalyzer;

namespace Incoders.Hive.Application.Abstractions.Social;

public interface ISingleContentAnalysisRepository
{
    Task AddAsync(
        SingleContentAnalysisRecord analysis,
        CancellationToken cancellationToken = default);

    Task<SingleContentAnalysisRecord?> GetByIdAsync(
        Guid analysisId,
        Guid organizationId,
        CancellationToken cancellationToken = default);

    Task UpdateStatusAsync(
        Guid analysisId,
        Guid organizationId,
        SingleContentAnalysisStatus status,
        DateTime updatedAtUtc,
        CancellationToken cancellationToken = default,
        string? failureCode = null,
        string? failureMessageKey = null);

    Task UpdateAsync(
        SingleContentAnalysisRecord analysis,
        CancellationToken cancellationToken = default);
}

public sealed record SingleContentAnalysisRecord(
    Guid Id,
    Guid OrganizationId,
    Guid? ClientId,
    Guid UserId,
    string SourceUrl,
    string? NormalizedUrl,
    string? SourcePlatformKey,
    SingleContentAnalysisStatus Status,
    string? ContentSnapshotJson,
    string? AnalysisJson,
    string? ProviderDiagnosticsJson,
    string? FailureCode,
    string? FailureMessageKey,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    DateTime? CompletedAtUtc);
