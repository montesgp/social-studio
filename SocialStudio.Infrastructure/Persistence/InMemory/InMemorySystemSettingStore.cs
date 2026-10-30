using System.Collections.Concurrent;
using SocialStudio.Domain.SystemSettings;

namespace SocialStudio.Infrastructure.Persistence.InMemory;

public sealed class InMemorySystemSettingStore
{
    private readonly ConcurrentDictionary<SystemSettingId, SystemSetting> _settings = new();

    internal ConcurrentDictionary<SystemSettingId, SystemSetting> Settings => _settings;
}
