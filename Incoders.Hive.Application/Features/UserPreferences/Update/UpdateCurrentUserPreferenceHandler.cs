using Incoders.Hive.Application.Abstractions;
using Incoders.Hive.Application.Features.UserPreferences;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.UserPreferences;
using MediatR;

namespace Incoders.Hive.Application.Features.UserPreferences.Update;

public sealed class UpdateCurrentUserPreferenceHandler
    : IRequestHandler<UpdateCurrentUserPreferenceCommand, Result<UserPreferenceResponse>>
{
    private readonly IUserPreferenceRepository _preferences;
    private readonly ICurrentUser _currentUser;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;

    public UpdateCurrentUserPreferenceHandler(
        IUserPreferenceRepository preferences,
        ICurrentUser currentUser,
        IUnitOfWork unitOfWork,
        IClock clock)
    {
        _preferences = preferences;
        _currentUser = currentUser;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<Result<UserPreferenceResponse>> Handle(
        UpdateCurrentUserPreferenceCommand request,
        CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is not { } userId)
        {
            return Result.Failure<UserPreferenceResponse>(UserPreferenceErrors.UserRequired);
        }

        var existing = await _preferences.GetByUserIdAsync(userId, cancellationToken);
        if (existing is null)
        {
            var created = UserPreference.Create(
                UserPreferenceId.New(),
                userId,
                request.ThemeKey,
                request.AccentTheme,
                request.DateFormat,
                _clock.UtcNow);

            if (created.IsFailure)
            {
                return Result.Failure<UserPreferenceResponse>(created.Error);
            }

            await _preferences.AddAsync(created.Value, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return Result.Success(UserPreferenceResponse.FromDomain(created.Value));
        }

        var updated = existing.UpdateTheme(request.ThemeKey, request.AccentTheme, request.DateFormat, _clock.UtcNow);
        if (updated.IsFailure)
        {
            return Result.Failure<UserPreferenceResponse>(updated.Error);
        }

        _preferences.Update(existing);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(UserPreferenceResponse.FromDomain(existing));
    }
}
