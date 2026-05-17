using System.Collections.Concurrent;
using Incoders.Hive.Domain.SystemSettings;

namespace Incoders.Hive.Infrastructure.Persistence.InMemory;

public sealed class InMemorySystemSettingStore
{
    private readonly ConcurrentDictionary<SystemSettingId, SystemSetting> _settings = new();

    internal ConcurrentDictionary<SystemSettingId, SystemSetting> Settings => _settings;
}
