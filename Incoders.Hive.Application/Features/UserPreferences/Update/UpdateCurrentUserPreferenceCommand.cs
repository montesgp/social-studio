using Incoders.Hive.Domain.Common;
using MediatR;
using Incoders.Hive.Application.Features.UserPreferences;

namespace Incoders.Hive.Application.Features.UserPreferences.Update;

public sealed record UpdateCurrentUserPreferenceCommand(
    string ThemeKey,
    string? AccentTheme) : IRequest<Result<UserPreferenceResponse>>;
