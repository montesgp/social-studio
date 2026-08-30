using Incoders.Hive.Application.Abstractions;
using Incoders.Hive.Application.Features.UserPreferences.GetCurrent;
using Incoders.Hive.Application.Features.UserPreferences.Update;
using Incoders.Hive.Domain.UserPreferences;
using Incoders.Hive.Infrastructure.Persistence.InMemory;
using Incoders.Hive.Tests.Common;

namespace Incoders.Hive.Tests.Application.UserPreferences;

public class CurrentUserPreferenceTests
{
    private static readonly DateTime Now = new(2026, 5, 16, 6, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task GetCurrent_WhenMissing_ReturnsDarkDefault()
    {
        var userId = Guid.NewGuid();
        var repo = new FakeUserPreferenceRepository();
        var currentUser = new FakeCurrentUser(userId);
        var handler = new GetCurrentUserPreferenceHandler(repo, currentUser);

        var result = await handler.Handle(new GetCurrentUserPreferenceQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(ThemeKeys.Dark, result.Value.ThemeKey);
        Assert.Equal(userId, result.Value.UserId);
        Assert.Equal(DateFormats.Default, result.Value.DateFormat);
    }

    [Fact]
    public async Task UpdateCurrent_CreatesThenUpdatesPreference()
    {
        var userId = Guid.NewGuid();
        var repo = new FakeUserPreferenceRepository();
        var currentUser = new FakeCurrentUser(userId);
        var clock = new TestClock(Now);
        var handler = new UpdateCurrentUserPreferenceHandler(repo, currentUser, new InMemoryUnitOfWork(), clock);

        var created = await handler.Handle(
            new UpdateCurrentUserPreferenceCommand(ThemeKeys.Light, null, DateFormats.Default),
            CancellationToken.None);

        Assert.True(created.IsSuccess);
        Assert.Equal(ThemeKeys.Light, created.Value.ThemeKey);
        Assert.Equal(userId, created.Value.UserId);
        Assert.Equal(DateFormats.Default, created.Value.DateFormat);

        clock.UtcNow = Now.AddMinutes(15);
        var updated = await handler.Handle(
            new UpdateCurrentUserPreferenceCommand(ThemeKeys.System, "social-light", DateFormats.YyyyMmDd),
            CancellationToken.None);

        Assert.True(updated.IsSuccess);
        Assert.Equal(ThemeKeys.System, updated.Value.ThemeKey);
        Assert.Equal("social-light", updated.Value.AccentTheme);
        Assert.Equal(DateFormats.YyyyMmDd, updated.Value.DateFormat);
        Assert.Equal(Now.AddMinutes(15), updated.Value.UpdatedAtUtc);
    }

    [Fact]
    public async Task UpdateCurrent_InvalidDateFormat_Fails()
    {
        var userId = Guid.NewGuid();
        var repo = new FakeUserPreferenceRepository();
        var currentUser = new FakeCurrentUser(userId);
        var clock = new TestClock(Now);
        var handler = new UpdateCurrentUserPreferenceHandler(repo, currentUser, new InMemoryUnitOfWork(), clock);

        var result = await handler.Handle(
            new UpdateCurrentUserPreferenceCommand(ThemeKeys.Light, null, "invalid-format"),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("user_preferences.invalid_date_format", result.Error.Code);
    }
}

internal sealed class FakeCurrentUser : ICurrentUser
{
    public FakeCurrentUser(Guid userId)
    {
        UserId = userId;
        Email = "theme@incoders.dev";
    }

    public bool IsAuthenticated => true;

    public Guid? UserId { get; }

    public string? Email { get; }

    public bool IsPlatformAdmin => false;
}

internal sealed class FakeUserPreferenceRepository : IUserPreferenceRepository
{
    private readonly Dictionary<Guid, UserPreference> _store = [];

    public Task<UserPreference?> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        _store.TryGetValue(userId, out var preference);
        return Task.FromResult(preference);
    }

    public Task AddAsync(UserPreference preference, CancellationToken cancellationToken = default)
    {
        _store[preference.UserId] = preference;
        return Task.CompletedTask;
    }

    public void Update(UserPreference preference)
    {
        _store[preference.UserId] = preference;
    }
}
