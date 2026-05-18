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

public enum SingleContentAnalysisStatus
{
    Requested = 1,
    ResolvingContent = 2,
    ContentResolved = 3,
    ContentResolvedPartial = 4,
    Analyzing = 5,
    Analyzed = 6,
    Failed = 7,
}
