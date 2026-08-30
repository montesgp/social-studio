using Incoders.Hive.Domain.Common;

namespace Incoders.Hive.Domain.Social.ContentStudio;

public static class ContentStudioErrors
{
    public static Error Validation(string detail) =>
        Error.Validation("social.content_studio.validation", detail);

    public static readonly Error IllegalReviewTransition = Error.Conflict(
        "social.content_studio.illegal_transition",
        "social.content_studio.errors.illegal_transition");

    public static readonly Error RejectionReasonRequired = Error.Validation(
        "social.content_studio.rejection_reason_required",
        "social.content_studio.errors.rejection_reason_required");

    public static readonly Error TextRequired = Error.Validation(
        "social.content_studio.text_required",
        "social.content_studio.errors.text_required");

    public static readonly Error TextTooLong = Error.Validation(
        "social.content_studio.text_too_long",
        "social.content_studio.errors.text_too_long");

    public static readonly Error ItemNotFound = Error.NotFound(
        "social.content_studio.item_not_found",
        "social.content_studio.errors.item_not_found");

    public static readonly Error SetNotFound = Error.NotFound(
        "social.content_studio.set_not_found",
        "social.content_studio.errors.set_not_found");

    public static readonly Error MemberAlreadyExists = Error.Conflict(
        "social.content_studio.member_already_exists",
        "social.content_studio.errors.member_already_exists");

    public static readonly Error MemberNotFound = Error.NotFound(
        "social.content_studio.member_not_found",
        "social.content_studio.errors.member_not_found");

    public static readonly Error CaptionRequired = Error.Validation(
        "social.content_studio.caption_required",
        "social.content_studio.errors.caption_required");

    public static readonly Error CaptionTooLong = Error.Validation(
        "social.content_studio.caption_too_long",
        "social.content_studio.errors.caption_too_long");

    /// <summary>
    /// A purge was requested for an item that still carries a live publication record
    /// (Scheduled, Publishing or Published). Purging is permanent and would destroy the record of
    /// what actually went out to a network, so the item must be unpublished/cancelled first.
    /// </summary>
    public static readonly Error PurgeBlockedByPublication = Error.Conflict(
        "social.content_studio.purge_blocked_by_publication",
        "social.content_studio.errors.purge_blocked_by_publication");

    public static readonly Error CaptionTooManyHashtags = Error.Validation(
        "social.content_studio.caption_too_many_hashtags",
        "social.content_studio.errors.caption_too_many_hashtags");
}
