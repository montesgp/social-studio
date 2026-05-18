using System.Text.RegularExpressions;
using Incoders.Hive.Domain.Common;

namespace Incoders.Hive.Domain.Social.Networks;

/// <summary>
/// Platform-managed catalog entry for a social network (e.g. Instagram, TikTok).
/// PlatformAdmin-curated; no OrganizationId column.
/// </summary>
public sealed class SocialNetwork : Entity<SocialNetworkId>
{
    public const int CodeMinLength = 2;
    public const int CodeMaxLength = 32;
    public const int DisplayNameMaxLength = 80;
    public const int IconKeyMaxLength = 32;

    private static readonly Regex CodePattern = new(@"^[a-z0-9\-]+$", RegexOptions.Compiled);
    private static readonly Regex IconKeyPattern = new(@"^[a-z0-9]+$", RegexOptions.Compiled);

    private SocialNetwork(
        SocialNetworkId id,
        string code,
        string displayName,
        string iconKey,
        bool isActive,
        DateTime createdAtUtc,
        DateTime updatedAtUtc)
        : base(id)
    {
        Code = code;
        DisplayName = displayName;
        IconKey = iconKey;
        IsActive = isActive;
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = updatedAtUtc;
    }

    // Parameterless constructor for EF Core re-hydration
    private SocialNetwork() : base(default) { }

    public string Code { get; private set; } = default!;

    public string DisplayName { get; private set; } = default!;

    public string IconKey { get; private set; } = default!;

    public bool IsActive { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime UpdatedAtUtc { get; private set; }

    /// <summary>
    /// Factory for creating a new SocialNetwork. Code is normalized (lowercased + trimmed) before validation.
    /// </summary>
    public static Result<SocialNetwork> Create(
        string code,
        string displayName,
        string iconKey,
        DateTime nowUtc)
    {
        var normalizedCode = NormalizeCode(code);

        var codeValidation = ValidateCode(normalizedCode);
        if (codeValidation.IsFailure)
        {
            return Result.Failure<SocialNetwork>(codeValidation.Error);
        }

        var displayNameValidation = ValidateDisplayName(displayName);
        if (displayNameValidation.IsFailure)
        {
            return Result.Failure<SocialNetwork>(displayNameValidation.Error);
        }

        var iconKeyValidation = ValidateIconKey(iconKey);
        if (iconKeyValidation.IsFailure)
        {
            return Result.Failure<SocialNetwork>(iconKeyValidation.Error);
        }

        return Result.Success(new SocialNetwork(
            SocialNetworkId.New(),
            normalizedCode,
            displayName.Trim(),
            iconKey.Trim(),
            true,
            nowUtc,
            nowUtc));
    }

    /// <summary>
    /// Re-hydration factory used by EF mapping. Bypasses validation — data is trusted from persistence.
    /// </summary>
    public static SocialNetwork Restore(
        SocialNetworkId id,
        string code,
        string displayName,
        string iconKey,
        bool isActive,
        DateTime createdAtUtc,
        DateTime updatedAtUtc)
    {
        return new SocialNetwork(id, code, displayName, iconKey, isActive, createdAtUtc, updatedAtUtc);
    }

    /// <summary>
    /// Updates DisplayName and IconKey. Code is immutable (ADR 6).
    /// </summary>
    public Result Rename(string displayName, string iconKey, DateTime nowUtc)
    {
        var displayNameValidation = ValidateDisplayName(displayName);
        if (displayNameValidation.IsFailure)
        {
            return Result.Failure(displayNameValidation.Error);
        }

        var iconKeyValidation = ValidateIconKey(iconKey);
        if (iconKeyValidation.IsFailure)
        {
            return Result.Failure(iconKeyValidation.Error);
        }

        DisplayName = displayName.Trim();
        IconKey = iconKey.Trim();
        UpdatedAtUtc = nowUtc;
        return Result.Success();
    }

    public Result Deactivate(DateTime nowUtc)
    {
        if (!IsActive)
        {
            return Result.Failure(SocialNetworkErrors.AlreadyInactive);
        }

        IsActive = false;
        UpdatedAtUtc = nowUtc;
        return Result.Success();
    }

    public Result Activate(DateTime nowUtc)
    {
        if (IsActive)
        {
            return Result.Failure(SocialNetworkErrors.AlreadyActive);
        }

        IsActive = true;
        UpdatedAtUtc = nowUtc;
        return Result.Success();
    }

    private static string NormalizeCode(string code) =>
        code?.Trim() ?? string.Empty;

    private static Result ValidateCode(string normalizedCode)
    {
        if (normalizedCode.Length < CodeMinLength || normalizedCode.Length > CodeMaxLength)
        {
            return Result.Failure(SocialNetworkErrors.InvalidCode(
                $"Code must be between {CodeMinLength} and {CodeMaxLength} characters."));
        }

        if (!CodePattern.IsMatch(normalizedCode))
        {
            return Result.Failure(SocialNetworkErrors.InvalidCode(
                "Code must be lowercase alphanumeric with optional hyphens."));
        }

        return Result.Success();
    }

    private static Result ValidateDisplayName(string displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            return Result.Failure(SocialNetworkErrors.InvalidDisplayName("DisplayName is required."));
        }

        if (displayName.Trim().Length > DisplayNameMaxLength)
        {
            return Result.Failure(SocialNetworkErrors.InvalidDisplayName(
                $"DisplayName must not exceed {DisplayNameMaxLength} characters."));
        }

        return Result.Success();
    }

    private static Result ValidateIconKey(string iconKey)
    {
        if (string.IsNullOrWhiteSpace(iconKey))
        {
            return Result.Failure(SocialNetworkErrors.InvalidIconKey("IconKey is required."));
        }

        var trimmed = iconKey.Trim();

        if (trimmed.Length > IconKeyMaxLength)
        {
            return Result.Failure(SocialNetworkErrors.InvalidIconKey(
                $"IconKey must not exceed {IconKeyMaxLength} characters."));
        }

        if (!IconKeyPattern.IsMatch(trimmed))
        {
            return Result.Failure(SocialNetworkErrors.InvalidIconKey(
                "Must be lowercase alphanumeric (a-z, 0-9) only."));
        }

        return Result.Success();
    }
}
