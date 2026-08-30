using Incoders.Hive.Domain.Common;

namespace Incoders.Hive.Domain.Social.ClientAccountConnections;

public static class ClientAccountConnectionErrors
{
    public static Error Validation(string detail) =>
        Error.Validation("social.client_account_connections.validation", detail);

    public static readonly Error Disabled = Error.Conflict(
        "social.client_account_connections.disabled",
        "social.client_account_connections.errors.disabled");
}
