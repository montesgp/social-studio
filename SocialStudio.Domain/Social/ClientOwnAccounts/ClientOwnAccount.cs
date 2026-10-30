using SocialStudio.Domain.Common;
using SocialStudio.Domain.Social.Clients;
using SocialStudio.Domain.Social.Networks;

namespace SocialStudio.Domain.Social.ClientOwnAccounts;

/// <summary>
/// Tracks a client's own social media account for a given network.
/// Sibling aggregate to Client — owns its own repository, keyed by ClientId FK.
/// Reserved for a future strategy feature that derives link profiles from the client's own accounts.
/// </summary>
public sealed class ClientOwnAccount : Entity<ClientOwnAccountId>
{
    public const int ProfileUrlMinLength = 10;
    public const int ProfileUrlMaxLength = 2048;
    public const int HandleMaxLength = 100;
    public const int LabelMaxLength = 80;
    public const int NotesMaxLength = 500;

    private ClientOwnAccount(
        ClientOwnAccountId id,
        ClientId clientId,
        Guid organizationId,
        SocialNetworkId socialNetworkId,
        string profileUrl,
        string? handle,
        string? label,
        string? notes,
        bool isActive,
        DateTime createdAtUtc,
        DateTime updatedAtUtc,
        int sortOrder)
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
        SortOrder = sortOrder;
    }

    // Parameterless constructor for EF Core
    private ClientOwnAccount() : base(default) { }

    public ClientId ClientId { get; private set; }

    public Guid OrganizationId { get; private set; }

    public SocialNetworkId SocialNetworkId { get; private set; }

    public string ProfileUrl { get; private set; } = default!;

    public string? Handle { get; private set; }

    public string? Label { get; private set; }

    public string? Notes { get; private set; }

    public bool IsActive { get; private set; }

    public int SortOrder { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime UpdatedAtUtc { get; private set; }

    /// <summary>
    /// Factory for creating a new ClientOwnAccount. Validates all fields.
    /// </summary>
    public static Result<ClientOwnAccount> Create(
        ClientOwnAccountId id,
        ClientId clientId,
        Guid organizationId,
        SocialNetworkId socialNetworkId,
        string profileUrl,
        string? handle,
        string? label,
        string? notes,
        DateTime nowUtc,
        int sortOrder = 0)
    {
        var urlValidation = ValidateProfileUrl(profileUrl);
        if (urlValidation.IsFailure)
        {
            return Result.Failure<ClientOwnAccount>(urlValidation.Error);
        }

        var handleValidation = ValidateOptionalHandle(handle);
        if (handleValidation.IsFailure)
        {
            return Result.Failure<ClientOwnAccount>(handleValidation.Error);
        }

        var labelValidation = ValidateOptionalLabel(label);
        if (labelValidation.IsFailure)
        {
            return Result.Failure<ClientOwnAccount>(labelValidation.Error);
        }

        var notesValidation = ValidateOptionalNotes(notes);
        if (notesValidation.IsFailure)
        {
            return Result.Failure<ClientOwnAccount>(notesValidation.Error);
        }

        return Result.Success(new ClientOwnAccount(
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
            nowUtc,
            sortOrder));
    }

    /// <summary>
    /// Re-hydration factory used by EF mapping. Bypasses validation — data is trusted from persistence.
    /// </summary>
    public static ClientOwnAccount Restore(
        ClientOwnAccountId id,
        ClientId clientId,
        Guid organizationId,
        SocialNetworkId socialNetworkId,
        string profileUrl,
        string? handle,
        string? label,
        string? notes,
        bool isActive,
        DateTime createdAtUtc,
        DateTime updatedAtUtc,
        int sortOrder = 0)
    {
        return new ClientOwnAccount(
            id, clientId, organizationId, socialNetworkId,
            profileUrl, handle, label, notes,
            isActive, createdAtUtc, updatedAtUtc, sortOrder);
    }

    /// <summary>
    /// Updates all editable fields. ClientId and OrganizationId are immutable.
    /// </summary>
    public Result<ClientOwnAccount> Update(
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
            return Result.Failure<ClientOwnAccount>(urlValidation.Error);
        }

        var handleValidation = ValidateOptionalHandle(handle);
        if (handleValidation.IsFailure)
        {
            return Result.Failure<ClientOwnAccount>(handleValidation.Error);
        }

        var labelValidation = ValidateOptionalLabel(label);
        if (labelValidation.IsFailure)
        {
            return Result.Failure<ClientOwnAccount>(labelValidation.Error);
        }

        var notesValidation = ValidateOptionalNotes(notes);
        if (notesValidation.IsFailure)
        {
            return Result.Failure<ClientOwnAccount>(notesValidation.Error);
        }

        SocialNetworkId = socialNetworkId;
        ProfileUrl = urlValidation.Value;
        Handle = handleValidation.Value;
        Label = labelValidation.Value;
        Notes = notesValidation.Value;
        UpdatedAtUtc = nowUtc;

        return Result.Success(this);
    }

    public Result<ClientOwnAccount> Update(
        string profileUrl,
        string? handle,
        string? label,
        string? notes,
        DateTime nowUtc)
    {
        return Update(SocialNetworkId, profileUrl, handle, label, notes, nowUtc);
    }

    /// <summary>
    /// Sets the sort order for this account. Used by the static Reorder helper.
    /// </summary>
    public void SetSortOrder(int order, DateTime nowUtc)
    {
        SortOrder = order;
        UpdatedAtUtc = nowUtc;
    }

    /// <summary>
    /// Assigns 0-based sequential SortOrder to all accounts following the supplied orderedIds sequence.
    /// </summary>
    public static Result<IReadOnlyList<ClientOwnAccount>> Reorder(
        IReadOnlyList<ClientOwnAccount> accounts,
        IReadOnlyList<Guid> orderedIds,
        DateTime nowUtc)
    {
        var accountById = accounts.ToDictionary(a => a.Id.Value);

        foreach (var id in orderedIds)
        {
            if (!accountById.ContainsKey(id))
            {
                return Result.Failure<IReadOnlyList<ClientOwnAccount>>(
                    ClientOwnAccountErrors.Validation($"Account id '{id}' was not found in the client's accounts."));
            }
        }

        for (var i = 0; i < orderedIds.Count; i++)
        {
            var account = accountById[orderedIds[i]];
            account.SetSortOrder(i, nowUtc);
        }

        return Result.Success<IReadOnlyList<ClientOwnAccount>>(accounts);
    }

    public Result<ClientOwnAccount> Deactivate(DateTime nowUtc)
    {
        if (!IsActive)
        {
            return Result.Failure<ClientOwnAccount>(ClientOwnAccountErrors.AlreadyInactive);
        }

        IsActive = false;
        UpdatedAtUtc = nowUtc;
        return Result.Success(this);
    }

    public Result<ClientOwnAccount> Activate(DateTime nowUtc)
    {
        if (IsActive)
        {
            return Result.Failure<ClientOwnAccount>(ClientOwnAccountErrors.AlreadyActive);
        }

        IsActive = true;
        UpdatedAtUtc = nowUtc;
        return Result.Success(this);
    }

    private static Result<string> ValidateProfileUrl(string profileUrl)
    {
        if (string.IsNullOrWhiteSpace(profileUrl))
        {
            return Result.Failure<string>(ClientOwnAccountErrors.InvalidProfileUrl("ProfileUrl is required."));
        }

        var trimmed = profileUrl.Trim();

        if (trimmed.Length < ProfileUrlMinLength)
        {
            return Result.Failure<string>(ClientOwnAccountErrors.InvalidProfileUrl(
                $"ProfileUrl must be at least {ProfileUrlMinLength} characters."));
        }

        if (trimmed.Length > ProfileUrlMaxLength)
        {
            return Result.Failure<string>(ClientOwnAccountErrors.InvalidProfileUrl(
                $"ProfileUrl must not exceed {ProfileUrlMaxLength} characters."));
        }

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return Result.Failure<string>(ClientOwnAccountErrors.InvalidProfileUrl(
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
            return Result.Failure<string?>(ClientOwnAccountErrors.InvalidHandle(
                $"Handle must not exceed {HandleMaxLength} characters."));
        }

        if (trimmed.Any(char.IsWhiteSpace))
        {
            return Result.Failure<string?>(ClientOwnAccountErrors.InvalidHandle(
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
            return Result.Failure<string?>(ClientOwnAccountErrors.InvalidLabel(
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
            return Result.Failure<string?>(ClientOwnAccountErrors.InvalidNotes(
                $"Notes must not exceed {NotesMaxLength} characters."));
        }

        return Result.Success<string?>(trimmed);
    }
}
