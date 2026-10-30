using SocialStudio.Domain.Common;
using MediatR;
using SocialStudio.Application.Features.UserPreferences;

namespace SocialStudio.Application.Features.UserPreferences.Update;

public sealed record UpdateCurrentUserPreferenceCommand(
    string ThemeKey,
    string? AccentTheme,
    string DateFormat) : IRequest<Result<UserPreferenceResponse>>;
