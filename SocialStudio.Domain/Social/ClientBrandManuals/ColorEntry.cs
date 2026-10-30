using System.Text.RegularExpressions;
using SocialStudio.Domain.Common;

namespace SocialStudio.Domain.Social.ClientBrandManuals;

public sealed record ColorEntry(string Name, string Hex, ColorRole? Role, int Rank)
{
    public const int NameMaxLength = 50;

    private static readonly Regex HexRegex = new(
        @"^#([0-9a-fA-F]{3}|[0-9a-fA-F]{6}|[0-9a-fA-F]{8})$",
        RegexOptions.Compiled);

    public static Result<ColorEntry> Create(string name, string hex, ColorRole? role, int rank)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Result.Failure<ColorEntry>(ClientBrandManualErrors.Validation("Name is required."));
        }

        var trimmedName = name.Trim();
        if (trimmedName.Length > NameMaxLength)
        {
            return Result.Failure<ColorEntry>(ClientBrandManualErrors.Validation(
                $"Name must not exceed {NameMaxLength} characters."));
        }

        if (string.IsNullOrWhiteSpace(hex))
        {
            return Result.Failure<ColorEntry>(ClientBrandManualErrors.Validation("Hex is required."));
        }

        var trimmedHex = hex.Trim();
        if (!HexRegex.IsMatch(trimmedHex))
        {
            return Result.Failure<ColorEntry>(ClientBrandManualErrors.Validation(
                "Hex must be a valid hex color code (#RGB, #RRGGBB, or #RRGGBBAA)."));
        }

        if (rank < 0)
        {
            return Result.Failure<ColorEntry>(ClientBrandManualErrors.Validation(
                "Rank must be >= 0."));
        }

        return Result.Success(new ColorEntry(trimmedName, trimmedHex, role, rank));
    }

    /// <summary>
    /// Trusted rehydration from persisted data (EF/jsonb).
    /// Tolerates null role and zero rank from pre-migration rows — no validation.
    /// </summary>
    public static ColorEntry Restore(string name, string hex, ColorRole? role, int rank)
        => new(name, hex, role, rank);

    /// <summary>
    /// Maps a free-text role string (legacy jsonb field) to <see cref="ColorRole"/>.
    /// Case-insensitive. Unknown or null values return null (value dropped, treated as unclassified).
    /// </summary>
    public static ColorRole? TryParseRole(string? freeText)
    {
        if (string.IsNullOrWhiteSpace(freeText))
        {
            return null;
        }

        return freeText.Trim().ToLowerInvariant() switch
        {
            "primary"   => ColorRole.Primary,
            "secondary" => ColorRole.Secondary,
            "accent"    => ColorRole.Accent,
            "neutral"   => ColorRole.Neutral,
            _           => null
        };
    }
}
