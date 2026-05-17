using Incoders.Hive.Application.Features.SystemSettings.Create;
using Incoders.Hive.Application.Features.SystemSettings.Delete;
using Incoders.Hive.Application.Features.SystemSettings.GetAll;
using Incoders.Hive.Application.Features.SystemSettings.GetById;
using Incoders.Hive.Application.Features.SystemSettings.Update;
using Incoders.Hive.Infrastructure.Persistence.InMemory;
using Incoders.Hive.Tests.Common;

namespace Incoders.Hive.Tests.Application.SystemSettings;

internal sealed class SystemSettingsTestHarness
{
    public SystemSettingsTestHarness(DateTime nowUtc)
    {
        Store = new InMemorySystemSettingStore();
        var repo = new InMemorySystemSettingRepository(Store);
        var uow = new InMemoryUnitOfWork();
        Clock = new TestClock(nowUtc);
        Create = new CreateSystemSettingHandler(repo, uow, Clock);
        GetById = new GetSystemSettingByIdHandler(repo);
        List = new GetSystemSettingsHandler(repo);
        Update = new UpdateSystemSettingHandler(repo, uow, Clock);
        Delete = new DeleteSystemSettingHandler(repo, uow);
    }

    public InMemorySystemSettingStore Store { get; }
    public TestClock Clock { get; }
    public CreateSystemSettingHandler Create { get; }
    public GetSystemSettingByIdHandler GetById { get; }
    public GetSystemSettingsHandler List { get; }
    public UpdateSystemSettingHandler Update { get; }
    public DeleteSystemSettingHandler Delete { get; }
}
