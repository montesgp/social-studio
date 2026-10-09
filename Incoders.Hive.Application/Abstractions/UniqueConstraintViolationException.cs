namespace Incoders.Hive.Application.Abstractions;

/// <summary>
/// Provider-neutral signal that an <see cref="IUnitOfWork.SaveChangesAsync"/> call violated a
/// unique constraint/index. Infrastructure's <c>IUnitOfWork</c> implementation translates the
/// underlying provider exception (e.g. a Postgres unique-violation) into this type so Application
/// code can react to a specific expected race — such as the org-scoped single-analyzer duplicate
/// gate's DB-level backstop — without depending on Npgsql/EF Core exception types.
/// </summary>
public sealed class UniqueConstraintViolationException : Exception
{
    public UniqueConstraintViolationException(string constraintName, Exception innerException)
        : base($"Unique constraint '{constraintName}' violated.", innerException)
    {
        ConstraintName = constraintName;
    }

    /// <summary>The database-level name of the violated unique index/constraint.</summary>
    public string ConstraintName { get; }
}
