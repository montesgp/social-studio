using SocialStudio.Domain.Common;
using MediatR;

namespace SocialStudio.Application.Features.Social.ContentGeneration.GeneratePackage;

/// <summary>
/// Command to start a content package generation job for a completed analysis.
/// Validated by <see cref="GenerateContentPackageCommandValidator"/>.
/// </summary>
public sealed record GenerateContentPackageCommand(
    Guid OrganizationId,
    Guid? ClientId,
    Guid AnalysisId,
    IReadOnlyList<string> RequestedFormats,
    Guid? CorrelationId,
    string? RegenerationObjection = null) : IRequest<Result<GenerateContentPackageResponse>>;

/// <summary>
/// Response returned on successful command processing (202 Accepted).
/// </summary>
public sealed record GenerateContentPackageResponse(
    Guid JobId,
    string StatusUrl);
