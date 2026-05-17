using Incoders.Hive.Application.Abstractions;
using Incoders.Hive.Domain.SystemSettings;
using Incoders.Hive.Infrastructure.Identity;
using Incoders.Hive.Infrastructure.Persistence.Postgres.Entities;
using Microsoft.EntityFrameworkCore;

namespace Incoders.Hive.Infrastructure.Persistence.Postgres;

public sealed class EfSystemSettingRepository : ISystemSettingRepository
{
    private readonly AppIdentityDbContext _dbContext;

    public EfSystemSettingRepository(AppIdentityDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<SystemSetting?> GetByIdAsync(SystemSettingId id, CancellationToken cancellationToken = default)
    {
        var row = await _dbContext.SystemSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id.Value, cancellationToken);
        return row is null ? null : ToDomain(row);
    }

    public async Task<SystemSetting?> GetByKeyAsync(
        string key,
        SystemSettingScope scope,
        Guid? tenantId,
        Guid? userId,
        CancellationToken cancellationToken = default)
    {
        var row = await _dbContext.SystemSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.Key == key
                     && x.Scope == scope.ToString()
                     && x.TenantId == tenantId
                     && x.UserId == userId,
                cancellationToken);

        return row is null ? null : ToDomain(row);
    }

    public async Task<(IReadOnlyList<SystemSetting> Items, int TotalCount)> ListAsync(
        SystemSettingQuery query,
        CancellationToken cancellationToken = default)
    {
        IQueryable<SystemSettingData> source = _dbContext.SystemSettings.AsNoTracking();

        if (query.Scope is { } scope)
        {
            source = source.Where(x => x.Scope == scope.ToString());
        }

        if (query.TenantId is { } tenantId)
        {
            source = source.Where(x => x.TenantId == tenantId);
        }

        if (query.UserId is { } userId)
        {
            source = source.Where(x => x.UserId == userId);
        }

        if (!string.IsNullOrWhiteSpace(query.KeyContains))
        {
            var needle = query.KeyContains.Trim();
            source = source.Where(x => x.Key.Contains(needle));
        }

        var totalCount = await source.CountAsync(cancellationToken);
        var rows = await source
            .OrderBy(x => x.Key)
            .ThenBy(x => x.CreatedAtUtc)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(cancellationToken);

        return (rows.Select(ToDomain).ToList(), totalCount);
    }

    public Task AddAsync(SystemSetting setting, CancellationToken cancellationToken = default)
    {
        _dbContext.SystemSettings.Add(ToData(setting));
        return Task.CompletedTask;
    }

    public void Update(SystemSetting setting)
    {
        var entity = _dbContext.SystemSettings.Local.FirstOrDefault(x => x.Id == setting.Id.Value)
                     ?? _dbContext.SystemSettings.First(x => x.Id == setting.Id.Value);

        entity.Key = setting.Key;
        entity.Value = setting.Value;
        entity.Scope = setting.Scope.ToString();
        entity.TenantId = setting.TenantId;
        entity.UserId = setting.UserId;
        entity.UpdatedAtUtc = setting.UpdatedAtUtc;
    }

    public void Remove(SystemSetting setting)
    {
        var entity = _dbContext.SystemSettings.Local.FirstOrDefault(x => x.Id == setting.Id.Value)
                     ?? _dbContext.SystemSettings.First(x => x.Id == setting.Id.Value);
        _dbContext.SystemSettings.Remove(entity);
    }

    private static SystemSetting ToDomain(SystemSettingData row) =>
        SystemSetting.Restore(
            new SystemSettingId(row.Id),
            row.Key,
            row.Value,
            Enum.Parse<SystemSettingScope>(row.Scope),
            row.TenantId,
            row.UserId,
            row.CreatedAtUtc,
            row.UpdatedAtUtc);

    private static SystemSettingData ToData(SystemSetting setting) =>
        new()
        {
            Id = setting.Id.Value,
            Key = setting.Key,
            Value = setting.Value,
            Scope = setting.Scope.ToString(),
            TenantId = setting.TenantId,
            UserId = setting.UserId,
            CreatedAtUtc = setting.CreatedAtUtc,
            UpdatedAtUtc = setting.UpdatedAtUtc,
        };
}
