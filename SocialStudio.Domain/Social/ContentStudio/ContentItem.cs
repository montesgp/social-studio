using SocialStudio.Domain.Common;

namespace SocialStudio.Domain.Social.ContentStudio;

/// <summary>
/// Aggregate root for a single reviewable/editable content piece. It is a working surface DERIVED
/// from an immutable generation format result (referenced softly via <see cref="ContentOrigin"/>);
/// it never mutates the source. The editorial lifecycle is a guarded <see cref="ReviewState"/> machine.
/// </summary>
public sealed class ContentItem : Entity<ContentItemId>
{
    private ContentItem(
        ContentItemId id,
        Guid organizationId,
        Guid clientId,
        ContentOrigin origin,
        ContentKind kind,
        ContentDraft draft,
        ReviewState state,
        EditTrace editTrace,
        Guid variantGroupId,
        bool isPrimaryVariant,
        PlanContext? planContext,
        string? rejectionReason,
        Guid? reviewerUserId,
        DateTime createdAtUtc,
        DateTime updatedAtUtc)
        : base(id)
    {
        OrganizationId = organizationId;
        ClientId = clientId;
        Origin = origin;
        Kind = kind;
        Draft = draft;
        State = state;
        EditTrace = editTrace;
        VariantGroupId = variantGroupId;
        IsPrimaryVariant = isPrimaryVariant;
        PlanContext = planContext;
        RejectionReason = rejectionReason;
        ReviewerUserId = reviewerUserId;
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = updatedAtUtc;
    }

    // Parameterless constructor for EF Core
    private ContentItem() : base(default) { }

    public Guid OrganizationId { get; private set; }

    public Guid ClientId { get; private set; }

    public ContentOrigin Origin { get; private set; } = default!;

    public ContentKind Kind { get; private set; } = default!;

    public ContentDraft Draft { get; private set; } = default!;

    public ReviewState State { get; private set; }

    public EditTrace EditTrace { get; private set; } = default!;

    public Guid VariantGroupId { get; private set; }

    public bool IsPrimaryVariant { get; private set; }

    public PlanContext? PlanContext { get; private set; }

    public string? RejectionReason { get; private set; }

    public Guid? ReviewerUserId { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime UpdatedAtUtc { get; private set; }

    /// <summary>
    /// Factory that derives a new item from a generation format result. Starts in
    /// <see cref="ReviewState.Draft"/>; its variant group is its own id (a standalone, primary variant).
    /// </summary>
    public static Result<ContentItem> Materialize(
        ContentItemId id,
        Guid organizationId,
        Guid clientId,
        ContentOrigin origin,
        ContentKind kind,
        ContentDraft draft,
        DateTime nowUtc)
    {
        if (string.IsNullOrWhiteSpace(kind.FormatKey))
        {
            return Result.Failure<ContentItem>(ContentStudioErrors.Validation("Format key is required."));
        }

        return Result.Success(new ContentItem(
            id,
            organizationId,
            clientId,
            origin,
            kind,
            draft,
            ReviewState.Draft,
            EditTrace.Initial(),
            variantGroupId: id.Value,
            isPrimaryVariant: true,
            planContext: null,
            rejectionReason: null,
            reviewerUserId: null,
            createdAtUtc: nowUtc,
            updatedAtUtc: nowUtc));
    }

    /// <summary>Re-hydration factory used by EF mapping. Bypasses validation — data is trusted.</summary>
    public static ContentItem Restore(
        ContentItemId id,
        Guid organizationId,
        Guid clientId,
        ContentOrigin origin,
        ContentKind kind,
        ContentDraft draft,
        ReviewState state,
        EditTrace editTrace,
        Guid variantGroupId,
        bool isPrimaryVariant,
        PlanContext? planContext,
        string? rejectionReason,
        Guid? reviewerUserId,
        DateTime createdAtUtc,
        DateTime updatedAtUtc) =>
        new(id, organizationId, clientId, origin, kind, draft, state, editTrace,
            variantGroupId, isPrimaryVariant, planContext, rejectionReason, reviewerUserId,
            createdAtUtc, updatedAtUtc);

    /// <summary>Opens the item for review (→ <see cref="ReviewState.InReview"/>).</summary>
    public Result BeginReview(DateTime nowUtc) => Transition(ReviewState.InReview, nowUtc);

    /// <summary>
    /// Edits the draft text (caption/script). Validates non-empty and within <paramref name="maxLength"/>,
    /// records a manual edit, and moves to <see cref="ReviewState.Edited"/>. The source result is untouched.
    /// </summary>
    public Result EditText(string newText, int maxLength, DateTime nowUtc)
    {
        if (string.IsNullOrWhiteSpace(newText))
        {
            return Result.Failure(ContentStudioErrors.TextRequired);
        }

        var trimmed = newText.Trim();
        if (trimmed.Length > maxLength)
        {
            return Result.Failure(ContentStudioErrors.TextTooLong);
        }

        if (!ReviewStateTransitions.IsValid(State, ReviewState.Edited))
        {
            return Result.Failure(ContentStudioErrors.IllegalReviewTransition);
        }

        Draft = Draft with { Text = trimmed };
        EditTrace = EditTrace.ManualEdit(nowUtc);
        State = ReviewState.Edited;
        UpdatedAtUtc = nowUtc;
        return Result.Success();
    }

    /// <summary>
    /// Edits the dedicated publication <see cref="Caption"/> (R5.5 — Content Studio editing
    /// completeness). Validates through <see cref="Caption.Create"/> (non-empty, IG length/hashtag
    /// ceilings), records a manual edit, and moves to <see cref="ReviewState.Edited"/>. Never touches
    /// <see cref="ContentDraft.Text"/> — that field keeps carrying reel scripts.
    /// </summary>
    public Result EditCaption(string? newCaption, DateTime nowUtc)
    {
        var captionResult = Caption.Create(newCaption);
        if (captionResult.IsFailure)
        {
            return Result.Failure(captionResult.Error);
        }

        if (!ReviewStateTransitions.IsValid(State, ReviewState.Edited))
        {
            return Result.Failure(ContentStudioErrors.IllegalReviewTransition);
        }

        Draft = Draft with { Caption = captionResult.Value };
        EditTrace = EditTrace.ManualEdit(nowUtc);
        State = ReviewState.Edited;
        UpdatedAtUtc = nowUtc;
        return Result.Success();
    }

    /// <summary>Approves the item, recording the reviewer and clearing any rejection reason.</summary>
    public Result Approve(Guid reviewerUserId, DateTime nowUtc)
    {
        var transition = Transition(ReviewState.Approved, nowUtc);
        if (transition.IsFailure)
        {
            return transition;
        }

        ReviewerUserId = reviewerUserId;
        RejectionReason = null;
        return Result.Success();
    }

    /// <summary>Rejects the item with a required reason.</summary>
    public Result Reject(Guid reviewerUserId, string reason, DateTime nowUtc)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return Result.Failure(ContentStudioErrors.RejectionReasonRequired);
        }

        var transition = Transition(ReviewState.Rejected, nowUtc);
        if (transition.IsFailure)
        {
            return transition;
        }

        ReviewerUserId = reviewerUserId;
        RejectionReason = reason.Trim();
        return Result.Success();
    }

    /// <summary>Sets the item aside (recoverable) → <see cref="ReviewState.Discarded"/>.</summary>
    public Result Discard(DateTime nowUtc) => Transition(ReviewState.Discarded, nowUtc);

    /// <summary>Stages an approved item for publication → <see cref="ReviewState.ReadyForPublication"/>.</summary>
    public Result StageForPublication(DateTime nowUtc) => Transition(ReviewState.ReadyForPublication, nowUtc);

    /// <summary>Marks the item handed off after the publication handoff has been emitted (terminal).</summary>
    public Result MarkHandedOff(DateTime nowUtc) => Transition(ReviewState.HandedOff, nowUtc);

    /// <summary>
    /// Creates an independent duplicate of this item: new identity and its own variant group,
    /// copying origin/kind/draft but NOT the reviewer, rejection reason, or lifecycle (starts in Draft).
    /// </summary>
    public ContentItem Duplicate(ContentItemId newId, DateTime nowUtc) =>
        new(newId, OrganizationId, ClientId, Origin, Kind, Draft,
            ReviewState.Draft, EditTrace.Initial(),
            variantGroupId: newId.Value, isPrimaryVariant: true,
            planContext: null, rejectionReason: null, reviewerUserId: null,
            createdAtUtc: nowUtc, updatedAtUtc: nowUtc);

    /// <summary>
    /// Creates a variant in the SAME variant group (a non-primary alternative the reviewer can edit).
    /// Starts in <see cref="ReviewState.Draft"/>; copies origin/kind/draft, drops reviewer/reason.
    /// </summary>
    public ContentItem SpawnVariant(ContentItemId newId, DateTime nowUtc) =>
        new(newId, OrganizationId, ClientId, Origin, Kind, Draft,
            ReviewState.Draft, EditTrace.Initial(),
            variantGroupId: VariantGroupId, isPrimaryVariant: false,
            planContext: null, rejectionReason: null, reviewerUserId: null,
            createdAtUtc: nowUtc, updatedAtUtc: nowUtc);

    /// <summary>
    /// Edits the visual draft (composition markup + asset references) for a visual piece — image
    /// replacement, background swap, and basic layout/branding adjustments all flow through here.
    /// Sets the item to <see cref="ReviewState.Edited"/>. The immutable source result is untouched.
    /// Pixel re-rendering of the edited markup is a generation-pipeline concern performed at export.
    /// </summary>
    public Result EditVisual(string? visualSpecMarkup, IReadOnlyList<ContentAssetRef> assets, DateTime nowUtc)
    {
        if (!ReviewStateTransitions.IsValid(State, ReviewState.Edited))
        {
            return Result.Failure(ContentStudioErrors.IllegalReviewTransition);
        }

        Draft = Draft with
        {
            VisualSpecMarkup = visualSpecMarkup,
            Assets = assets ?? Array.Empty<ContentAssetRef>(),
        };
        EditTrace = EditTrace.ManualEdit(nowUtc);
        State = ReviewState.Edited;
        UpdatedAtUtc = nowUtc;
        return Result.Success();
    }

    /// <summary>
    /// Re-hydrates the draft (and refreshes the soft source reference) from a freshly REGENERATED
    /// format result — but ONLY while the item is still pristine (<see cref="ReviewState.Draft"/>,
    /// never opened for review or edited). Once a reviewer has touched the item, regeneration must not
    /// clobber that editorial work, so this is a guarded no-op. The source result itself is never written back.
    /// </summary>
    /// <returns><c>true</c> if the draft was refreshed; <c>false</c> if the item was protected (not pristine).</returns>
    public bool RefreshFromSource(ContentOrigin origin, ContentDraft draft, DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(origin);
        ArgumentNullException.ThrowIfNull(draft);

        if (State != ReviewState.Draft)
        {
            return false;
        }

        Origin = origin;
        Draft = draft;
        UpdatedAtUtc = nowUtc;
        return true;
    }

    /// <summary>Associates the item with a content-plan calendar slot (soft refs; audit metadata, not a transition).</summary>
    public Result LinkToPlan(PlanContext planContext, DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(planContext);
        PlanContext = planContext;
        UpdatedAtUtc = nowUtc;
        return Result.Success();
    }

    /// <summary>Removes any content-plan association.</summary>
    public Result UnlinkFromPlan(DateTime nowUtc)
    {
        PlanContext = null;
        UpdatedAtUtc = nowUtc;
        return Result.Success();
    }

    private Result Transition(ReviewState target, DateTime nowUtc)
    {
        if (!ReviewStateTransitions.IsValid(State, target))
        {
            return Result.Failure(ContentStudioErrors.IllegalReviewTransition);
        }

        State = target;
        UpdatedAtUtc = nowUtc;
        return Result.Success();
    }
}
