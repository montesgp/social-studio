using Incoders.Hive.Domain.Common;

namespace Incoders.Hive.Domain.Social.Clients;

/// <summary>
/// Organization-owned customer/account managed inside the Hive Social module.
/// </summary>
public sealed class Client : Entity<ClientId>
{
    public const int NameMaxLength = 200;
    public const int DescriptionMaxLength = 1_000;

    private Client(
        ClientId id,
        Guid organizationId,
        string name,
        string? description,
        bool isActive,
        DateTime createdAtUtc,
        DateTime updatedAtUtc)
        : base(id)
    {
        OrganizationId = organizationId;
        Name = name;
        Description = description;
        IsActive = isActive;
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = updatedAtUtc;
    }

    public Guid OrganizationId { get; private set; }

    public string Name { get; private set; }

    public string? Description { get; private set; }

    public bool IsActive { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime UpdatedAtUtc { get; private set; }

    public static Result<Client> Create(
        ClientId id,
        Guid organizationId,
        string name,
        string? description,
        DateTime nowUtc)
    {
        var validation = Validate(organizationId, name, description);
        if (validation.IsFailure)
        {
            return Result.Failure<Client>(validation.Error);
        }

        return Result.Success(new Client(
            id,
            organizationId,
            name.Trim(),
            NormalizeDescription(description),
            true,
            nowUtc,
            nowUtc));
    }

    public static Client Restore(
        ClientId id,
        Guid organizationId,
        string name,
        string? description,
        bool isActive,
        DateTime createdAtUtc,
        DateTime updatedAtUtc)
    {
        return new Client(id, organizationId, name, description, isActive, createdAtUtc, updatedAtUtc);
    }

    public Result Rename(string name, string? description, DateTime nowUtc)
    {
        var validation = Validate(OrganizationId, name, description);
        if (validation.IsFailure)
        {
            return Result.Failure(validation.Error);
        }

        Name = name.Trim();
        Description = NormalizeDescription(description);
        UpdatedAtUtc = nowUtc;
        return Result.Success();
    }

    public Result Deactivate(DateTime nowUtc)
    {
        if (!IsActive)
        {
            return Result.Failure(ClientErrors.AlreadyInactive);
        }

        IsActive = false;
        UpdatedAtUtc = nowUtc;
        return Result.Success();
    }

    public Result Activate(DateTime nowUtc)
    {
        if (IsActive)
        {
            return Result.Failure(ClientErrors.AlreadyActive);
        }

        IsActive = true;
        UpdatedAtUtc = nowUtc;
        return Result.Success();
    }

    private static Result Validate(Guid organizationId, string name, string? description)
    {
        if (organizationId == Guid.Empty)
        {
            return Result.Failure(ClientErrors.OrganizationRequired);
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            return Result.Failure(ClientErrors.NameRequired);
        }

        if (name.Length > NameMaxLength)
        {
            return Result.Failure(ClientErrors.NameTooLong);
        }

        if (description?.Length > DescriptionMaxLength)
        {
            return Result.Failure(ClientErrors.DescriptionTooLong);
        }

        return Result.Success();
    }

    private static string? NormalizeDescription(string? description) =>
        string.IsNullOrWhiteSpace(description) ? null : description.Trim();
}
