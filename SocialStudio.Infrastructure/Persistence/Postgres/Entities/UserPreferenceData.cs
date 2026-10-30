namespace SocialStudio.Infrastructure.Persistence.Postgres.Entities;

public sealed class UserPreferenceData
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public string ThemeKey { get; set; } = string.Empty;

    public string? AccentTheme { get; set; }

    public string DateFormat { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }
}
