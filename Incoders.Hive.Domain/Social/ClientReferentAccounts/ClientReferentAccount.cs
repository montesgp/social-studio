using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Social.Clients;
using Incoders.Hive.Domain.Social.Networks;

namespace Incoders.Hive.Domain.Social.ClientReferentAccounts;

/// <summary>
/// Tracks a client's referent social media account for a given network.
/// Sibling aggregate to Client — owns its own repository, keyed by ClientId FK.
/// </summary>
public sealed class ClientReferentAccount : Entity<ClientReferentAccountId>
{
    public const int ProfileUrlMinLength = 10;
    public const int ProfileUrlMaxLength = 2048;
    public const int HandleMaxLength = 100;
    public const int LabelMaxLength = 80;
    public const int NotesMaxLength = 500;

    private ClientReferentAccount(
        ClientReferentAccountId id,
        ClientId clientId,
        Guid organizationId,
        SocialNetworkId socialNetworkId,
        string profileUrl,
        string? handle,
        string? label,
        string? notes,
        bool isActive,
        DateTime createdAtUtc,
        DateTime updatedAtUtc)
        : base(id)
    {
        ClientId = clientId;
        OrganizationId = organizationId;
        SocialNetworkId = socialNetworkId;
        ProfileUrl = profileUrl;
        Handle = handle;
        Label = label;
        Notes = notes;
        IsActive = isActive;
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = updatedAtUtc;
    }

    // Parameterless constructor for EF Core
    private ClientReferentAccount() : base(default) { }

    public ClientId ClientId { get; private set; }

    public Guid OrganizationId { get; private set; }

    public SocialNetworkId SocialNetworkId { get; private set; }

    public string ProfileUrl { get; private set; } = default!;

    public string? Handle { get; private set; }

    public string? Label { get; private set; }

    public string? Notes { get; private set; }

    public bool IsActive { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime UpdatedAtUtc { get; private set; }

    /// <summary>
    /// Factory for creating a new ClientReferentAccount. Validates all fields.
    /// </summary>
    public static Result<ClientReferentAccount> Create(
        ClientReferentAccountId id,
        ClientId clientId,
        Guid organizationId,
        SocialNetworkId socialNetworkId,
        string profileUrl,
        string? handle,
        string? label,
        string? notes,
        DateTime nowUtc)
    {
        var urlValidation = ValidateProfileUrl(profileUrl);
        if (urlValidation.IsFailure)
        {
            return Result.Failure<ClientReferentAccount>(urlValidation.Error);
        }

        var handleValidation = ValidateOptionalHandle(handle);
        if (handleValidation.IsFailure)
        {
            return Result.Failure<ClientReferentAccount>(handleValidation.Error);
        }

        var labelValidation = ValidateOptionalLabel(label);
        if (labelValidation.IsFailure)
        {
            return Result.Failure<ClientReferentAccount>(labelValidation.Error);
        }

        var notesValidation = ValidateOptionalNotes(notes);
        if (notesValidation.IsFailure)
        {
            return Result.Failure<ClientReferentAccount>(notesValidation.Error);
        }

        return Result.Success(new ClientReferentAccount(
            id,
            clientId,
            organizationId,
            socialNetworkId,
            urlValidation.Value,
            handleValidation.Value,
            labelValidation.Value,
            notesValidation.Value,
            true,
            nowUtc,
            nowUtc));
    }

    /// <summary>
    /// Re-hydration factory used by EF mapping. Bypasses validation — data is trusted from persistence.
    /// </summary>
    public static ClientReferentAccount Restore(
        ClientReferentAccountId id,
        ClientId clientId,
        Guid organizationId,
        SocialNetworkId socialNetworkId,
        string profileUrl,
        string? handle,
        string? label,
        string? notes,
        bool isActive,
        DateTime createdAtUtc,
        DateTime updatedAtUtc)
    {
        return new ClientReferentAccount(
            id, clientId, organizationId, socialNetworkId,
            profileUrl, handle, label, notes,
            isActive, createdAtUtc, updatedAtUtc);
    }

    /// <summary>
    /// Updates all editable fields. ClientId, OrganizationId, SocialNetworkId are immutable.
    /// </summary>
    public Result<ClientReferentAccount> Update(
        string profileUrl,
        string? handle,
        string? label,
        string? notes,
        DateTime nowUtc)
    {
        var urlValidation = ValidateProfileUrl(profileUrl);
        if (urlValidation.IsFailure)
        {
            return Result.Failure<ClientReferentAccount>(urlValidation.Error);
        }

        var handleValidation = ValidateOptionalHandle(handle);
        if (handleValidation.IsFailure)
        {
            return Result.Failure<ClientReferentAccount>(handleValidation.Error);
        }

        var labelValidation = ValidateOptionalLabel(label);
        if (labelValidation.IsFailure)
        {
            return Result.Failure<ClientReferentAccount>(labelValidation.Error);
        }

        var notesValidation = ValidateOptionalNotes(notes);
        if (notesValidation.IsFailure)
        {
            return Result.Failure<ClientReferentAccount>(notesValidation.Error);
        }

        ProfileUrl = urlValidation.Value;
        Handle = handleValidation.Value;
        Label = labelValidation.Value;
        Notes = notesValidation.Value;
        UpdatedAtUtc = nowUtc;

        return Result.Success(this);
    }

    public Result<ClientReferentAccount> Deactivate(DateTime nowUtc)
    {
        if (!IsActive)
        {
            return Result.Failure<ClientReferentAccount>(ClientReferentAccountErrors.AlreadyInactive);
        }

        IsActive = false;
        UpdatedAtUtc = nowUtc;
        return Result.Success(this);
    }

    public Result<ClientReferentAccount> Activate(DateTime nowUtc)
    {
        if (IsActive)
        {
            return Result.Failure<ClientReferentAccount>(ClientReferentAccountErrors.AlreadyActive);
        }

        IsActive = true;
        UpdatedAtUtc = nowUtc;
        return Result.Success(this);
    }

    private static Result<string> ValidateProfileUrl(string profileUrl)
    {
        if (string.IsNullOrWhiteSpace(profileUrl))
        {
            return Result.Failure<string>(ClientReferentAccountErrors.InvalidProfileUrl("ProfileUrl is required."));
        }

        var trimmed = profileUrl.Trim();

        if (trimmed.Length < ProfileUrlMinLength)
        {
            return Result.Failure<string>(ClientReferentAccountErrors.InvalidProfileUrl(
                $"ProfileUrl must be at least {ProfileUrlMinLength} characters."));
        }

        if (trimmed.Length > ProfileUrlMaxLength)
        {
            return Result.Failure<string>(ClientReferentAccountErrors.InvalidProfileUrl(
                $"ProfileUrl must not exceed {ProfileUrlMaxLength} characters."));
        }

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return Result.Failure<string>(ClientReferentAccountErrors.InvalidProfileUrl(
                "ProfileUrl must be an absolute http or https URL."));
        }

        return Result.Success(trimmed);
    }

    private static Result<string?> ValidateOptionalHandle(string? handle)
    {
        if (handle is null)
        {
            return Result.Success<string?>(null);
        }

        var trimmed = handle.Trim();

        if (trimmed.Length == 0)
        {
            return Result.Success<string?>(null);
        }

        if (trimmed.Length > HandleMaxLength)
        {
            return Result.Failure<string?>(ClientReferentAccountErrors.InvalidHandle(
                $"Handle must not exceed {HandleMaxLength} characters."));
        }

        // Must not contain any whitespace within the trimmed value
        if (trimmed.Any(char.IsWhiteSpace))
        {
            return Result.Failure<string?>(ClientReferentAccountErrors.InvalidHandle(
                "Handle must not contain whitespace."));
        }

        return Result.Success<string?>(trimmed);
    }

    private static Result<string?> ValidateOptionalLabel(string? label)
    {
        if (label is null)
        {
            return Result.Success<string?>(null);
        }

        var trimmed = label.Trim();

        if (trimmed.Length == 0)
        {
            return Result.Success<string?>(null);
        }

        if (trimmed.Length > LabelMaxLength)
        {
            return Result.Failure<string?>(ClientReferentAccountErrors.InvalidLabel(
                $"Label must not exceed {LabelMaxLength} characters."));
        }

        return Result.Success<string?>(trimmed);
    }

    private static Result<string?> ValidateOptionalNotes(string? notes)
    {
        if (notes is null)
        {
            return Result.Success<string?>(null);
        }

        var trimmed = notes.Trim();

        if (trimmed.Length == 0)
        {
            return Result.Success<string?>(null);
        }

        if (trimmed.Length > NotesMaxLength)
        {
            return Result.Failure<string?>(ClientReferentAccountErrors.InvalidNotes(
                $"Notes must not exceed {NotesMaxLength} characters."));
        }

        return Result.Success<string?>(trimmed);
    }
}
