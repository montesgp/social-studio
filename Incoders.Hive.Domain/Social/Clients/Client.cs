using Incoders.Hive.Domain.Common;

namespace Incoders.Hive.Domain.Social.Clients;

/// <summary>
/// Organization-owned customer/account managed inside the Hive Social module.
/// </summary>
public sealed class Client : Entity<ClientId>
{
    public const int NameMaxLength = 200;
    public const int DescriptionMaxLength = 1_000;
    public const int SectorOrNicheMaxLength = 300;

    private Client(
        ClientId id,
        Guid organizationId,
        string name,
        string? description,
        bool isActive,
        DateTime createdAtUtc,
        DateTime updatedAtUtc,
        string? sectorOrNiche)
        : base(id)
    {
        OrganizationId = organizationId;
        Name = name;
        Description = description;
        IsActive = isActive;
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = updatedAtUtc;
        SectorOrNiche = sectorOrNiche;
    }

    public Guid OrganizationId { get; private set; }

    public string Name { get; private set; }

    public string? Description { get; private set; }

    public string? SectorOrNiche { get; private set; }

    public bool IsActive { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime UpdatedAtUtc { get; private set; }

    public static Result<Client> Create(
        ClientId id,
        Guid organizationId,
        string name,
        string? description,
        DateTime nowUtc,
        string? sectorOrNiche = null)
    {
        var validation = Validate(organizationId, name, description, sectorOrNiche);
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
            nowUtc,
            NormalizeSectorOrNiche(sectorOrNiche)));
    }

    public static Client Restore(
        ClientId id,
        Guid organizationId,
        string name,
        string? description,
        bool isActive,
        DateTime createdAtUtc,
        DateTime updatedAtUtc,
        string? sectorOrNiche = null)
    {
        return new Client(id, organizationId, name, description, isActive, createdAtUtc, updatedAtUtc, sectorOrNiche);
    }

    public Result Rename(string name, string? description, DateTime nowUtc, string? sectorOrNiche = null)
    {
        var validation = Validate(OrganizationId, name, description, sectorOrNiche);
        if (validation.IsFailure)
        {
            return Result.Failure(validation.Error);
        }

        Name = name.Trim();
        Description = NormalizeDescription(description);
        SectorOrNiche = NormalizeSectorOrNiche(sectorOrNiche);
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

    private static Result Validate(Guid organizationId, string name, string? description, string? sectorOrNiche = null)
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

        if (sectorOrNiche?.Length > SectorOrNicheMaxLength)
        {
            return Result.Failure(ClientErrors.SectorOrNicheTooLong);
        }

        return Result.Success();
    }

    private static string? NormalizeDescription(string? description) =>
        string.IsNullOrWhiteSpace(description) ? null : description.Trim();

    private static string? NormalizeSectorOrNiche(string? sectorOrNiche) =>
        string.IsNullOrWhiteSpace(sectorOrNiche) ? null : sectorOrNiche.Trim();
}
