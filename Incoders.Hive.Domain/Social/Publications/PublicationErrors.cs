using Incoders.Hive.Domain.Common;

namespace Incoders.Hive.Domain.Social.Publications;

public static class PublicationErrors
{
    public static Error Validation(string detail) =>
        Error.Validation("social.publications.validation", detail);

    public static readonly Error IllegalPublicationTransition = Error.Conflict(
        "social.publications.illegal_transition",
        "social.publications.errors.illegal_transition");

    public static readonly Error ItemNotPublishable = Error.Conflict(
        "social.publications.item_not_publishable",
        "social.publications.errors.item_not_publishable");

    public static readonly Error DestinationUnresolved = Error.Conflict(
        "social.publications.destination_unresolved",
        "social.publications.errors.destination_unresolved");
}
