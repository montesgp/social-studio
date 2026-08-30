using Incoders.Hive.Domain.Common;
using MediatR;

namespace Incoders.Hive.Application.Features.Social.ProfileAnalyzer.Analyze;

/// <summary>
/// Initiates a new profile analysis run. Exactly one source path must be supplied:
/// - Client path: (ClientId + OwnAccountId) — analyzes the client's own Instagram account.
/// - URL path: ProfileUrl — analyzes any public Instagram profile by URL.
/// Violating the mutual-exclusion rule returns a validation error (PA-03).
/// </summary>
public sealed record AnalyzeProfileCommand(
    Guid OrganizationId,
    Guid UserId,
    Guid? ClientId,
    Guid? OwnAccountId,
    string? ProfileUrl) : IRequest<Result<AnalyzeProfileStatusResponse>>;

