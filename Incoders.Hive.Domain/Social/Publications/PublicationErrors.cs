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

    public static readonly Error NotFound = Error.NotFound(
        "social.publications.not_found",
        "social.publications.errors.not_found");

    public static readonly Error ConnectorNotFound = Error.Validation(
        "social.publications.connector_not_found",
        "social.publications.errors.connector_not_found");

    public static Error ConnectorFailure(string detail) =>
        Error.Failure("social.publications.connector_failed", detail);
}
