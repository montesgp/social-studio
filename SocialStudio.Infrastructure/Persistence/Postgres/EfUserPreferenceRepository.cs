using SocialStudio.Application.Abstractions;
using SocialStudio.Domain.UserPreferences;
using SocialStudio.Infrastructure.Identity;
using SocialStudio.Infrastructure.Persistence.Postgres.Entities;
using Microsoft.EntityFrameworkCore;

namespace SocialStudio.Infrastructure.Persistence.Postgres;

public sealed class EfUserPreferenceRepository : IUserPreferenceRepository
{
    private readonly AppIdentityDbContext _dbContext;

    public EfUserPreferenceRepository(AppIdentityDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<UserPreference?> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var row = await _dbContext.UserPreferences
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.UserId == userId, cancellationToken);

        return row is null ? null : ToDomain(row);
    }

    public Task AddAsync(UserPreference preference, CancellationToken cancellationToken = default)
    {
        _dbContext.UserPreferences.Add(ToData(preference));
        return Task.CompletedTask;
    }

    public void Update(UserPreference preference)
    {
        var data = ToData(preference);

        var tracked = _dbContext.UserPreferences.Local.FirstOrDefault(x => x.Id == data.Id);
        if (tracked is not null)
        {
            tracked.ThemeKey = data.ThemeKey;
            tracked.AccentTheme = data.AccentTheme;
            tracked.DateFormat = data.DateFormat;
            tracked.UpdatedAtUtc = data.UpdatedAtUtc;
        }
        else
        {
            _dbContext.UserPreferences.Update(data);
        }
    }

    private static UserPreference ToDomain(UserPreferenceData row) =>
        UserPreference.Restore(
            new UserPreferenceId(row.Id),
            row.UserId,
            row.ThemeKey,
            row.AccentTheme,
            row.DateFormat,
            row.CreatedAtUtc,
            row.UpdatedAtUtc);

    private static UserPreferenceData ToData(UserPreference preference) =>
        new()
        {
            Id = preference.Id.Value,
            UserId = preference.UserId,
            ThemeKey = preference.ThemeKey,
            AccentTheme = preference.AccentTheme,
            DateFormat = preference.DateFormat,
            CreatedAtUtc = preference.CreatedAtUtc,
            UpdatedAtUtc = preference.UpdatedAtUtc,
        };
}
