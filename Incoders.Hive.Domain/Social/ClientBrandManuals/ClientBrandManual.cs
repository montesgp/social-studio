using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Social.Clients;

namespace Incoders.Hive.Domain.Social.ClientBrandManuals;

/// <summary>
/// Aggregate root representing a client brand manual.
/// Sibling aggregate to Client — owns its own repository, keyed by ClientId FK.
/// One active manual per client is enforced at the application layer and backed
/// by a Postgres partial unique index WHERE is_active = true.
/// </summary>
public sealed class ClientBrandManual : Entity<ClientBrandManualId>
{
    public const int TitleMaxLength = 80;
    public const int VoiceAndToneMaxLength = 2000;
    public const int NotesMaxLength = 4000;

    private ClientBrandManual(
        ClientBrandManualId id,
        ClientId clientId,
        Guid organizationId,
        string title,
        IReadOnlyList<ColorEntry> colorPalette,
        IReadOnlyList<LogoEntry> logos,
        IReadOnlyList<BackgroundEntry> backgroundTemplates,
        IReadOnlyList<TypographyEntry> typography,
        string? voiceAndTone,
        string? notes,
        bool isActive,
        DateTime createdAtUtc,
        DateTime updatedAtUtc)
        : base(id)
    {
        ClientId = clientId;
        OrganizationId = organizationId;
        Title = title;
        ColorPalette = colorPalette;
        Logos = logos;
        BackgroundTemplates = backgroundTemplates;
        Typography = typography;
        VoiceAndTone = voiceAndTone;
        Notes = notes;
        IsActive = isActive;
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = updatedAtUtc;
    }

    // Parameterless constructor for EF Core
    private ClientBrandManual() : base(default) { }

    public ClientId ClientId { get; private set; }

    public Guid OrganizationId { get; private set; }

    public string Title { get; private set; } = default!;

    public IReadOnlyList<ColorEntry> ColorPalette { get; private set; } = Array.Empty<ColorEntry>();

    public IReadOnlyList<LogoEntry> Logos { get; private set; } = Array.Empty<LogoEntry>();

    public IReadOnlyList<BackgroundEntry> BackgroundTemplates { get; private set; } = Array.Empty<BackgroundEntry>();

    public IReadOnlyList<TypographyEntry> Typography { get; private set; } = Array.Empty<TypographyEntry>();

    public string? VoiceAndTone { get; private set; }

    public string? Notes { get; private set; }

    public bool IsActive { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime UpdatedAtUtc { get; private set; }

    /// <summary>
    /// Factory for creating a new ClientBrandManual. Validates all fields including
    /// per-entry value-object validation and the non-empty signal rule.
    /// </summary>
    public static Result<ClientBrandManual> Create(
        ClientBrandManualId id,
        ClientId clientId,
        Guid organizationId,
        string title,
        IReadOnlyList<ColorEntry>? colorPalette,
        IReadOnlyList<LogoEntry>? logos,
        IReadOnlyList<BackgroundEntry>? backgroundTemplates,
        IReadOnlyList<TypographyEntry>? typography,
        string? voiceAndTone,
        string? notes,
        DateTime nowUtc)
    {
        var titleResult = ValidateTitle(title);
        if (titleResult.IsFailure)
        {
            return Result.Failure<ClientBrandManual>(titleResult.Error);
        }

        var voiceResult = ValidateOptionalVoiceAndTone(voiceAndTone);
        if (voiceResult.IsFailure)
        {
            return Result.Failure<ClientBrandManual>(voiceResult.Error);
        }

        var notesResult = ValidateOptionalNotes(notes);
        if (notesResult.IsFailure)
        {
            return Result.Failure<ClientBrandManual>(notesResult.Error);
        }

        var palette = colorPalette ?? new List<ColorEntry>();
        var logoList = logos ?? new List<LogoEntry>();
        var bgList = backgroundTemplates ?? new List<BackgroundEntry>();
        var typoList = typography ?? new List<TypographyEntry>();

        if (!HasSomeSignal(palette, logoList, bgList, typoList, voiceResult.Value, notesResult.Value))
        {
            return Result.Failure<ClientBrandManual>(ClientBrandManualErrors.EmptyManual);
        }

        var forcedDefaultCheck = ValidateForcedDefaultUniqueness(bgList);
        if (forcedDefaultCheck.IsFailure)
        {
            return Result.Failure<ClientBrandManual>(forcedDefaultCheck.Error);
        }

        return Result.Success(new ClientBrandManual(
            id,
            clientId,
            organizationId,
            titleResult.Value,
            palette,
            logoList,
            bgList,
            typoList,
            voiceResult.Value,
            notesResult.Value,
            true,
            nowUtc,
            nowUtc));
    }

    /// <summary>
    /// Re-hydration factory used by EF mapping. Bypasses validation — data is trusted from persistence.
    /// </summary>
    public static ClientBrandManual Restore(
        ClientBrandManualId id,
        ClientId clientId,
        Guid organizationId,
        string title,
        IReadOnlyList<ColorEntry> colorPalette,
        IReadOnlyList<LogoEntry> logos,
        IReadOnlyList<BackgroundEntry> backgroundTemplates,
        IReadOnlyList<TypographyEntry> typography,
        string? voiceAndTone,
        string? notes,
        bool isActive,
        DateTime createdAtUtc,
        DateTime updatedAtUtc)
    {
        return new ClientBrandManual(
            id, clientId, organizationId, title,
            colorPalette, logos, backgroundTemplates, typography,
            voiceAndTone, notes,
            isActive, createdAtUtc, updatedAtUtc);
    }

    /// <summary>
    /// Updates all editable fields. ClientId and OrganizationId are immutable.
    /// Resolution 6: collection refs are replaced with new List&lt;T&gt; instances
    /// so EF ValueComparer detects them as changed.
    /// </summary>
    public Result<ClientBrandManual> Update(
        string title,
        IReadOnlyList<ColorEntry>? colorPalette,
        IReadOnlyList<LogoEntry>? logos,
        IReadOnlyList<BackgroundEntry>? backgroundTemplates,
        IReadOnlyList<TypographyEntry>? typography,
        string? voiceAndTone,
        string? notes,
        DateTime nowUtc)
    {
        var titleResult = ValidateTitle(title);
        if (titleResult.IsFailure)
        {
            return Result.Failure<ClientBrandManual>(titleResult.Error);
        }

        var voiceResult = ValidateOptionalVoiceAndTone(voiceAndTone);
        if (voiceResult.IsFailure)
        {
            return Result.Failure<ClientBrandManual>(voiceResult.Error);
        }

        var notesResult = ValidateOptionalNotes(notes);
        if (notesResult.IsFailure)
        {
            return Result.Failure<ClientBrandManual>(notesResult.Error);
        }

        var palette = colorPalette is null ? new List<ColorEntry>() : new List<ColorEntry>(colorPalette);
        var logoList = logos is null ? new List<LogoEntry>() : new List<LogoEntry>(logos);
        var bgList = backgroundTemplates is null ? new List<BackgroundEntry>() : new List<BackgroundEntry>(backgroundTemplates);
        var typoList = typography is null ? new List<TypographyEntry>() : new List<TypographyEntry>(typography);

        if (!HasSomeSignal(palette, logoList, bgList, typoList, voiceResult.Value, notesResult.Value))
        {
            return Result.Failure<ClientBrandManual>(ClientBrandManualErrors.EmptyManual);
        }

        var forcedDefaultCheck = ValidateForcedDefaultUniqueness(bgList);
        if (forcedDefaultCheck.IsFailure)
        {
            return Result.Failure<ClientBrandManual>(forcedDefaultCheck.Error);
        }

        Title = titleResult.Value;
        ColorPalette = palette;
        Logos = logoList;
        BackgroundTemplates = bgList;
        Typography = typoList;
        VoiceAndTone = voiceResult.Value;
        Notes = notesResult.Value;
        UpdatedAtUtc = nowUtc;

        return Result.Success(this);
    }

    public Result<ClientBrandManual> Deactivate(DateTime nowUtc)
    {
        if (!IsActive)
        {
            return Result.Failure<ClientBrandManual>(ClientBrandManualErrors.AlreadyInactive);
        }

        IsActive = false;
        UpdatedAtUtc = nowUtc;
        return Result.Success(this);
    }

    public Result<ClientBrandManual> Activate(DateTime nowUtc)
    {
        if (IsActive)
        {
            return Result.Failure<ClientBrandManual>(ClientBrandManualErrors.AlreadyActive);
        }

        IsActive = true;
        UpdatedAtUtc = nowUtc;
        return Result.Success(this);
    }

    private static Result<string> ValidateTitle(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return Result.Failure<string>(ClientBrandManualErrors.Validation("Title is required."));
        }

        var trimmed = title.Trim();
        if (trimmed.Length > TitleMaxLength)
        {
            return Result.Failure<string>(ClientBrandManualErrors.Validation(
                $"Title must not exceed {TitleMaxLength} characters."));
        }

        return Result.Success(trimmed);
    }

    private static Result<string?> ValidateOptionalVoiceAndTone(string? voiceAndTone)
    {
        if (voiceAndTone is null)
        {
            return Result.Success<string?>(null);
        }

        var trimmed = voiceAndTone.Trim();
        if (trimmed.Length == 0)
        {
            return Result.Success<string?>(null);
        }

        if (trimmed.Length > VoiceAndToneMaxLength)
        {
            return Result.Failure<string?>(ClientBrandManualErrors.Validation(
                $"VoiceAndTone must not exceed {VoiceAndToneMaxLength} characters."));
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
            return Result.Failure<string?>(ClientBrandManualErrors.Validation(
                $"Notes must not exceed {NotesMaxLength} characters."));
        }

        return Result.Success<string?>(trimmed);
    }

    private static Result ValidateForcedDefaultUniqueness(IReadOnlyList<BackgroundEntry> backgrounds)
    {
        // Reject any forced default missing a FormatKey (entry-level guard may have caught this,
        // but the aggregate is the authority when entries arrive pre-built via the list).
        var orphan = backgrounds
            .FirstOrDefault(b => b.IsFormatDefault && string.IsNullOrWhiteSpace(b.FormatKey));
        if (orphan is not null)
        {
            return Result.Failure(ClientBrandManualErrors.ForcedDefaultWithoutFormat(orphan.Name));
        }

        // Reject duplicate forced defaults for the same FormatKey.
        var duplicate = backgrounds
            .Where(b => b.IsFormatDefault && !string.IsNullOrWhiteSpace(b.FormatKey))
            .GroupBy(b => b.FormatKey!, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(g => g.Count() > 1);

        if (duplicate is not null)
        {
            return Result.Failure(ClientBrandManualErrors.DuplicateForcedDefault(duplicate.Key));
        }

        return Result.Success();
    }

    private static bool HasSomeSignal(
        IReadOnlyList<ColorEntry> palette,
        IReadOnlyList<LogoEntry> logos,
        IReadOnlyList<BackgroundEntry> backgrounds,
        IReadOnlyList<TypographyEntry> typography,
        string? voiceAndTone,
        string? notes)
    {
        return palette.Count > 0
            || logos.Count > 0
            || backgrounds.Count > 0
            || typography.Count > 0
            || !string.IsNullOrWhiteSpace(voiceAndTone)
            || !string.IsNullOrWhiteSpace(notes);
    }
}
