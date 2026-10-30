namespace SocialStudio.Infrastructure.Persistence;

public sealed class PersistenceOptions
{
    public const string SectionName = "Persistence";

    public string Provider { get; init; } = "Postgres";

    public string ConnectionStringName { get; init; } = "Postgres";
}
