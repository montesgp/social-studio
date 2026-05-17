using Incoders.Hive.Domain.UserPreferences;

namespace Incoders.Hive.Application.Abstractions;

public interface IUserPreferenceRepository
{
    Task<UserPreference?> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken = default);

    Task AddAsync(UserPreference preference, CancellationToken cancellationToken = default);

    void Update(UserPreference preference);
}
