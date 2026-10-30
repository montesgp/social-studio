using SocialStudio.Domain.Common;
using MediatR;
using SocialStudio.Application.Features.UserPreferences;

namespace SocialStudio.Application.Features.UserPreferences.GetCurrent;

public sealed record GetCurrentUserPreferenceQuery : IRequest<Result<UserPreferenceResponse>>;
