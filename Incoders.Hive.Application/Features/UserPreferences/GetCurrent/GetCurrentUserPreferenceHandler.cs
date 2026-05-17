using Incoders.Hive.Application.Abstractions;
using Incoders.Hive.Application.Features.UserPreferences;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.UserPreferences;
using MediatR;

namespace Incoders.Hive.Application.Features.UserPreferences.GetCurrent;

public sealed class GetCurrentUserPreferenceHandler
    : IRequestHandler<GetCurrentUserPreferenceQuery, Result<UserPreferenceResponse>>
{
    private readonly IUserPreferenceRepository _preferences;
    private readonly ICurrentUser _currentUser;

    public GetCurrentUserPreferenceHandler(
        IUserPreferenceRepository preferences,
        ICurrentUser currentUser)
    {
        _preferences = preferences;
        _currentUser = currentUser;
    }

    public async Task<Result<UserPreferenceResponse>> Handle(
        GetCurrentUserPreferenceQuery request,
        CancellationToken cancellationToken)
    {
        _ = request;

        if (_currentUser.UserId is not { } userId)
        {
            return Result.Failure<UserPreferenceResponse>(UserPreferenceErrors.UserRequired);
        }

        var preference = await _preferences.GetByUserIdAsync(userId, cancellationToken);

        if (preference is null)
        {
            return Result.Success(new UserPreferenceResponse(
                null,
                userId,
                ThemeKeys.Dark,
                null,
                null,
                null));
        }

        return Result.Success(UserPreferenceResponse.FromDomain(preference));
    }
}
