using Incoders.Hive.Domain.Common;
using MediatR;

namespace Incoders.Hive.Application.Features.Social.ClientAestheticProfileAdmin.UpdateBackgroundPattern;

/// <summary>
/// client-analysis-data admin panel: patches the <c>BackgroundPattern</c> (per-slide
/// "Photo"/"FlatColor"/"Decorator" classification) on the client's latest Analyzed profile
/// analysis. Values are supplied as raw strings so the validator can produce a stable,
/// localizable error for unrecognized tokens instead of a deserialization exception.
/// </summary>
public sealed record UpdateClientAestheticProfileBackgroundPatternCommand(
    Guid OrganizationId,
    Guid ClientId,
    IReadOnlyList<string> BackgroundPattern) : IRequest<Result<UpdateClientAestheticProfileBackgroundPatternResponse>>;

public sealed record UpdateClientAestheticProfileBackgroundPatternResponse(
    Guid AnalysisId,
    IReadOnlyList<string> BackgroundPattern,
    int TypicalSlideCount);
