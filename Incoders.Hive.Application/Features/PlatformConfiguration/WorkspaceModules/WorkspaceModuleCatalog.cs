namespace Incoders.Hive.Application.Features.PlatformConfiguration.WorkspaceModules;

public sealed record WorkspaceModuleDefinition(
    Guid Id,
    string Key,
    string Label,
    string Description,
    string ShortLabel,
    DateTime CreatedAtUtc);

public static class WorkspaceModuleCatalog
{
    public static readonly IReadOnlyList<WorkspaceModuleDefinition> All =
    [
        new(
            new Guid("00000000-0000-0000-0002-000000000001"),
            "hive-social",
            "Hive Social",
            "Content operations",
            "Social",
            new DateTime(2026, 5, 23, 0, 0, 0, DateTimeKind.Utc)),
        new(
            new Guid("00000000-0000-0000-0002-000000000002"),
            "hive-ecommerce",
            "Hive E-Commerce",
            "Commerce operations",
            "Commerce",
            new DateTime(2026, 5, 15, 0, 0, 0, DateTimeKind.Utc)),
    ];

    public static WorkspaceModuleDefinition? Find(string key) =>
        All.FirstOrDefault(x => string.Equals(x.Key, key?.Trim(), StringComparison.OrdinalIgnoreCase));

    public static bool IsSupported(string key) =>
        Find(key) is not null;

    public static WorkspaceModuleDefinition? FindById(Guid id) =>
        All.FirstOrDefault(x => x.Id == id);
}
