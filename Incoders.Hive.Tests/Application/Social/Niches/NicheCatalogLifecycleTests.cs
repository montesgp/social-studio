using Incoders.Hive.Application.Features.Social.Niches.Activate;
using Incoders.Hive.Application.Features.Social.Niches.Create;
using Incoders.Hive.Application.Features.Social.Niches.Deactivate;
using Incoders.Hive.Application.Features.Social.Niches.GetAll;
using Incoders.Hive.Application.Features.Social.Niches.GetById;
using Incoders.Hive.Application.Features.Social.Niches.Update;
using Incoders.Hive.Tests.Common;

namespace Incoders.Hive.Tests.Application.Social.Niches;

public sealed class NicheCatalogLifecycleTests
{
    private static readonly DateTime Now = new(2026, 8, 13, 10, 0, 0, DateTimeKind.Utc);

    // --- Create ---

    [Fact]
    public async Task CreateHandler_ValidCommand_ReturnsNicheWithCuratedHashtags()
    {
        var harness = new Harness(Now);

        var result = await harness.Create.Handle(
            new CreateNicheCommand("software", "Software & Technology", "cpu", ["#Software", "technology"]),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("software", result.Value.Code);
        Assert.Equal("Software & Technology", result.Value.DisplayName);
        Assert.Equal("cpu", result.Value.IconKey);
        Assert.Equal(new[] { "software", "technology" }, result.Value.SearchHashtags);
        Assert.True(result.Value.IsActive);
    }

    [Fact]
    public async Task CreateHandler_DuplicateCode_IsRejected()
    {
        var harness = new Harness(Now);
        await harness.Create.Handle(
            new CreateNicheCommand("software", "Software", null, []),
            CancellationToken.None);

        var result = await harness.Create.Handle(
            new CreateNicheCommand("software", "Software Again", null, []),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("social.niches.code_already_exists", result.Error.Code);
    }

    [Fact]
    public async Task CreateHandler_DuplicateDisplayName_IsRejected()
    {
        var harness = new Harness(Now);
        await harness.Create.Handle(
            new CreateNicheCommand("software", "Software", null, []),
            CancellationToken.None);

        var result = await harness.Create.Handle(
            new CreateNicheCommand("software-dev", "software", null, []),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("social.niches.display_name_already_exists", result.Error.Code);
    }

    [Fact]
    public async Task CreateHandler_InvalidCode_ReturnsValidationError()
    {
        var harness = new Harness(Now);

        var result = await harness.Create.Handle(
            new CreateNicheCommand("Not Valid!", "Software", null, []),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("social.niches.invalid_code", result.Error.Code);
    }

    // --- GetById ---

    [Fact]
    public async Task GetByIdHandler_UnknownId_ReturnsNotFound()
    {
        var harness = new Harness(Now);

        var result = await harness.GetById.Handle(new GetNicheByIdQuery(Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("social.niches.not_found", result.Error.Code);
    }

    [Fact]
    public async Task GetByIdHandler_ExistingId_ReturnsNiche()
    {
        var harness = new Harness(Now);
        var created = await harness.Create.Handle(
            new CreateNicheCommand("software", "Software", null, []),
            CancellationToken.None);

        var result = await harness.GetById.Handle(
            new GetNicheByIdQuery(created.Value.Id),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("software", result.Value.Code);
    }

    // --- List query ---

    [Fact]
    public async Task ListHandler_WithSearchTerm_FiltersByName()
    {
        var harness = new Harness(Now);
        await harness.CreateAt(Now, "software", "Software");
        await harness.CreateAt(Now, "medical-health", "Medical & Health");
        await harness.CreateAt(Now, "gastronomy", "Gastronomy");

        var result = await harness.GetAll.Handle(
            new GetAllNichesQuery(false, 1, 20, "medic"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value.Items);
        Assert.Equal("medical-health", result.Value.Items[0].Code);
    }

    [Fact]
    public async Task ListHandler_WithoutSearchTerm_ReturnsMostRecentlyCreatedFirst()
    {
        var harness = new Harness(Now);
        await harness.CreateAt(Now, "oldest", "Oldest");
        await harness.CreateAt(Now.AddHours(1), "middle", "Middle");
        await harness.CreateAt(Now.AddHours(2), "newest", "Newest");

        var result = await harness.GetAll.Handle(
            new GetAllNichesQuery(false, 1, 20, null),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(new[] { "newest", "middle", "oldest" }, result.Value.Items.Select(x => x.Code).ToArray());
    }

    [Fact]
    public async Task ListHandler_CapsPageSize_SoTheCatalogIsNeverReturnedUnbounded()
    {
        var harness = new Harness(Now);
        for (var i = 0; i < GetAllNichesQuery.MaxPageSize + 5; i++)
        {
            await harness.CreateAt(Now.AddMinutes(i), $"niche-{i}", $"Niche {i}");
        }

        var result = await harness.GetAll.Handle(
            new GetAllNichesQuery(false, 1, 10_000, null),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(GetAllNichesQuery.MaxPageSize, result.Value.PageSize);
        Assert.Equal(GetAllNichesQuery.MaxPageSize, result.Value.Items.Count);
    }

    [Fact]
    public async Task ListHandler_ActiveOnly_HidesDeactivatedNiches()
    {
        var harness = new Harness(Now);
        var created = await harness.Create.Handle(
            new CreateNicheCommand("software", "Software", null, []),
            CancellationToken.None);
        await harness.Create.Handle(new CreateNicheCommand("gastronomy", "Gastronomy", null, []), CancellationToken.None);
        await harness.Deactivate.Handle(new DeactivateNicheCommand(created.Value.Id), CancellationToken.None);

        var result = await harness.GetAll.Handle(
            new GetAllNichesQuery(false, 1, 20, null),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value.Items);
        Assert.Equal("gastronomy", result.Value.Items[0].Code);
    }

    // --- Update ---

    [Fact]
    public async Task UpdateHandler_ReplacesDisplayNameIconAndHashtags()
    {
        var harness = new Harness(Now);
        var created = await harness.Create.Handle(
            new CreateNicheCommand("medical-health", "Medical & Health", null, ["medical"]),
            CancellationToken.None);

        var result = await harness.Update.Handle(
            new UpdateNicheCommand(created.Value.Id, "Medical & Health Care", "stethoscope", ["medical", "health"]),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("medical-health", result.Value.Code);
        Assert.Equal("Medical & Health Care", result.Value.DisplayName);
        Assert.Equal("stethoscope", result.Value.IconKey);
        Assert.Equal(new[] { "medical", "health" }, result.Value.SearchHashtags);
    }

    [Fact]
    public async Task UpdateHandler_UnknownId_ReturnsNotFound()
    {
        var harness = new Harness(Now);

        var result = await harness.Update.Handle(
            new UpdateNicheCommand(Guid.NewGuid(), "Whatever", null, []),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("social.niches.not_found", result.Error.Code);
    }

    [Fact]
    public async Task UpdateHandler_DisplayNameTakenByAnotherNiche_IsRejected()
    {
        var harness = new Harness(Now);
        await harness.Create.Handle(new CreateNicheCommand("software", "Software", null, []), CancellationToken.None);
        var second = await harness.Create.Handle(
            new CreateNicheCommand("gastronomy", "Gastronomy", null, []),
            CancellationToken.None);

        var result = await harness.Update.Handle(
            new UpdateNicheCommand(second.Value.Id, "Software", null, []),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("social.niches.display_name_already_exists", result.Error.Code);
    }

    [Fact]
    public async Task UpdateHandler_KeepingItsOwnDisplayName_IsAllowed()
    {
        var harness = new Harness(Now);
        var created = await harness.Create.Handle(
            new CreateNicheCommand("software", "Software", null, []),
            CancellationToken.None);

        var result = await harness.Update.Handle(
            new UpdateNicheCommand(created.Value.Id, "Software", "cpu", ["software"]),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("cpu", result.Value.IconKey);
    }

    // --- Activate / Deactivate ---

    [Fact]
    public async Task DeactivateHandler_ActiveNiche_Deactivates()
    {
        var harness = new Harness(Now);
        var created = await harness.Create.Handle(
            new CreateNicheCommand("software", "Software", null, []),
            CancellationToken.None);

        var result = await harness.Deactivate.Handle(
            new DeactivateNicheCommand(created.Value.Id),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.IsActive);
    }

    [Fact]
    public async Task DeactivateHandler_AlreadyInactive_ReturnsConflict()
    {
        var harness = new Harness(Now);
        var created = await harness.Create.Handle(
            new CreateNicheCommand("software", "Software", null, []),
            CancellationToken.None);
        await harness.Deactivate.Handle(new DeactivateNicheCommand(created.Value.Id), CancellationToken.None);

        var result = await harness.Deactivate.Handle(
            new DeactivateNicheCommand(created.Value.Id),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("social.niches.already_inactive", result.Error.Code);
    }

    [Fact]
    public async Task ActivateHandler_InactiveNiche_Activates()
    {
        var harness = new Harness(Now);
        var created = await harness.Create.Handle(
            new CreateNicheCommand("software", "Software", null, []),
            CancellationToken.None);
        await harness.Deactivate.Handle(new DeactivateNicheCommand(created.Value.Id), CancellationToken.None);

        var result = await harness.Activate.Handle(
            new ActivateNicheCommand(created.Value.Id),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.IsActive);
    }

    [Fact]
    public async Task ActivateHandler_AlreadyActive_ReturnsConflict()
    {
        var harness = new Harness(Now);
        var created = await harness.Create.Handle(
            new CreateNicheCommand("software", "Software", null, []),
            CancellationToken.None);

        var result = await harness.Activate.Handle(
            new ActivateNicheCommand(created.Value.Id),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("social.niches.already_active", result.Error.Code);
    }

    private sealed class Harness
    {
        private readonly TestClock _clock;

        public Harness(DateTime nowUtc)
        {
            Repository = new InMemoryNicheRepository();
            _clock = new TestClock(nowUtc);

            Create = new CreateNicheHandler(Repository, _clock);
            GetById = new GetNicheByIdHandler(Repository);
            GetAll = new GetAllNichesHandler(Repository);
            Update = new UpdateNicheHandler(Repository, _clock);
            Deactivate = new DeactivateNicheHandler(Repository, _clock);
            Activate = new ActivateNicheHandler(Repository, _clock);
        }

        public InMemoryNicheRepository Repository { get; }
        public CreateNicheHandler Create { get; }
        public GetNicheByIdHandler GetById { get; }
        public GetAllNichesHandler GetAll { get; }
        public UpdateNicheHandler Update { get; }
        public DeactivateNicheHandler Deactivate { get; }
        public ActivateNicheHandler Activate { get; }

        public async Task CreateAt(DateTime createdAtUtc, string code, string displayName)
        {
            _clock.UtcNow = createdAtUtc;
            var result = await Create.Handle(
                new CreateNicheCommand(code, displayName, null, []),
                CancellationToken.None);
            Assert.True(result.IsSuccess);
        }
    }
}
