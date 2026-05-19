using System.Text.RegularExpressions;
using Incoders.Hive.Domain.Common;

namespace Incoders.Hive.Domain.Social.ClientBrandManuals;

public sealed record ColorEntry(string Name, string Hex, string? Role)
{
    public const int NameMaxLength = 50;
    public const int RoleMaxLength = 50;

    private static readonly Regex HexRegex = new(
        @"^#([0-9a-fA-F]{3}|[0-9a-fA-F]{6}|[0-9a-fA-F]{8})$",
        RegexOptions.Compiled);

    public static Result<ColorEntry> Create(string name, string hex, string? role)
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

        string? trimmedRole = null;
        if (role is not null)
        {
            trimmedRole = role.Trim();
            if (trimmedRole.Length == 0)
            {
                trimmedRole = null;
            }
            else if (trimmedRole.Length > RoleMaxLength)
            {
                return Result.Failure<ColorEntry>(ClientBrandManualErrors.Validation(
                    $"Role must not exceed {RoleMaxLength} characters."));
            }
        }

        return Result.Success(new ColorEntry(trimmedName, trimmedHex, trimmedRole));
    }

    public static ColorEntry Restore(string name, string hex, string? role)
        => new(name, hex, role);
}
