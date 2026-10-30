using SocialStudio.Application.Abstractions;
using SocialStudio.Application.Abstractions.Social;
using SocialStudio.Application.Common.Pagination;
using SocialStudio.Application.Features.Social.Networks;
using SocialStudio.Application.Features.Social.Networks.Activate;
using SocialStudio.Application.Features.Social.Networks.Create;
using SocialStudio.Application.Features.Social.Networks.Deactivate;
using SocialStudio.Application.Features.Social.Networks.GetAll;
using SocialStudio.Application.Features.Social.Networks.GetById;
using SocialStudio.Application.Features.Social.Networks.Update;
using SocialStudio.Domain.Social.Networks;
using SocialStudio.Tests.Common;

namespace SocialStudio.Tests.Application.Social.Networks;

public sealed class SocialNetworkLifecycleTests
{
    private static readonly DateTime Now = new(2026, 5, 17, 12, 0, 0, DateTimeKind.Utc);

    // --- Create ---

    [Fact]
    public async Task CreateHandler_ValidCommand_ReturnsNetworkResponse()
    {
        var harness = new Harness(Now);

        var result = await harness.Create.Handle(
            new CreateSocialNetworkCommand("instagram", "Instagram", "instagram"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("instagram", result.Value.Code);
        Assert.Equal("Instagram", result.Value.DisplayName);
        Assert.True(result.Value.IsActive);
    }

    [Fact]
    public async Task CreateHandler_DuplicateCode_ReturnsCodeAlreadyExists()
    {
        var harness = new Harness(Now);
        await harness.Create.Handle(
            new CreateSocialNetworkCommand("instagram", "Instagram", "instagram"),
            CancellationToken.None);

        var result = await harness.Create.Handle(
            new CreateSocialNetworkCommand("instagram", "Instagram 2", "instagram"),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("social.networks.code_already_exists", result.Error.Code);
    }

    [Fact]
    public async Task CreateHandler_InvalidCode_ReturnsValidationError()
    {
        var harness = new Harness(Now);

        var result = await harness.Create.Handle(
            new CreateSocialNetworkCommand("INVALID_CODE!", "Instagram", "instagram"),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("social.networks.invalid_code", result.Error.Code);
    }

    [Fact]
    public async Task CreateHandler_EmptyDisplayName_ReturnsValidationError()
    {
        var harness = new Harness(Now);

        var result = await harness.Create.Handle(
            new CreateSocialNetworkCommand("instagram", "", "instagram"),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("social.networks.invalid_display_name", result.Error.Code);
    }

    // --- GetById ---

    [Fact]
    public async Task GetByIdHandler_ExistingId_ReturnsNetworkResponse()
    {
        var harness = new Harness(Now);
        var created = await harness.Create.Handle(
            new CreateSocialNetworkCommand("instagram", "Instagram", "instagram"),
            CancellationToken.None);

        var result = await harness.GetById.Handle(
            new GetSocialNetworkByIdQuery(created.Value.Id),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("instagram", result.Value.Code);
    }

    [Fact]
    public async Task GetByIdHandler_UnknownId_ReturnsNotFound()
    {
        var harness = new Harness(Now);

        var result = await harness.GetById.Handle(
            new GetSocialNetworkByIdQuery(Guid.NewGuid()),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("social.networks.not_found", result.Error.Code);
    }

    // --- GetAll ---

    [Fact]
    public async Task GetAllHandler_ActiveOnly_ReturnsOnlyActiveNetworks()
    {
        var harness = new Harness(Now);
        var created = await harness.Create.Handle(
            new CreateSocialNetworkCommand("instagram", "Instagram", "instagram"),
            CancellationToken.None);
        await harness.Create.Handle(
            new CreateSocialNetworkCommand("facebook", "Facebook", "facebook"),
            CancellationToken.None);
        await harness.Deactivate.Handle(
            new DeactivateSocialNetworkCommand(created.Value.Id),
            CancellationToken.None);

        var result = await harness.GetAll.Handle(
            new GetAllSocialNetworksQuery(false, 1, 20, null),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value.Items);
        Assert.Equal("facebook", result.Value.Items[0].Code);
    }

    [Fact]
    public async Task GetAllHandler_IncludeInactive_ReturnsAllNetworks()
    {
        var harness = new Harness(Now);
        var created = await harness.Create.Handle(
            new CreateSocialNetworkCommand("instagram", "Instagram", "instagram"),
            CancellationToken.None);
        await harness.Create.Handle(
            new CreateSocialNetworkCommand("facebook", "Facebook", "facebook"),
            CancellationToken.None);
        await harness.Deactivate.Handle(
            new DeactivateSocialNetworkCommand(created.Value.Id),
            CancellationToken.None);

        var result = await harness.GetAll.Handle(
            new GetAllSocialNetworksQuery(true, 1, 20, null),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Items.Count);
    }

    [Fact]
    public async Task GetAllHandler_WithSearchTerm_FiltersResults()
    {
        var harness = new Harness(Now);
        await harness.Create.Handle(new CreateSocialNetworkCommand("instagram", "Instagram", "instagram"), CancellationToken.None);
        await harness.Create.Handle(new CreateSocialNetworkCommand("facebook", "Facebook", "facebook"), CancellationToken.None);
        await harness.Create.Handle(new CreateSocialNetworkCommand("tiktok", "TikTok", "tiktok"), CancellationToken.None);

        var result = await harness.GetAll.Handle(
            new GetAllSocialNetworksQuery(false, 1, 20, "face"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value.Items);
        Assert.Equal("facebook", result.Value.Items[0].Code);
    }

    [Fact]
    public async Task GetAllHandler_Paginated_RespectsPageAndPageSize()
    {
        var harness = new Harness(Now);
        await harness.Create.Handle(new CreateSocialNetworkCommand("instagram", "Instagram", "instagram"), CancellationToken.None);
        await harness.Create.Handle(new CreateSocialNetworkCommand("facebook", "Facebook", "facebook"), CancellationToken.None);
        await harness.Create.Handle(new CreateSocialNetworkCommand("tiktok", "TikTok", "tiktok"), CancellationToken.None);

        var result = await harness.GetAll.Handle(
            new GetAllSocialNetworksQuery(false, 1, 2, null),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Items.Count);
        Assert.Equal(3, result.Value.TotalCount);
    }

    // --- Update ---

    [Fact]
    public async Task UpdateHandler_ValidCommand_ReturnsUpdatedNetwork()
    {
        var harness = new Harness(Now);
        var created = await harness.Create.Handle(
            new CreateSocialNetworkCommand("instagram", "Instagram", "instagram"),
            CancellationToken.None);

        var result = await harness.Update.Handle(
            new UpdateSocialNetworkCommand(created.Value.Id, "Instagram Renamed", "newicon"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Instagram Renamed", result.Value.DisplayName);
        Assert.Equal("newicon", result.Value.IconKey);
        Assert.Equal("instagram", result.Value.Code);
    }

    [Fact]
    public async Task UpdateHandler_UnknownId_ReturnsNotFound()
    {
        var harness = new Harness(Now);

        var result = await harness.Update.Handle(
            new UpdateSocialNetworkCommand(Guid.NewGuid(), "Display", "icon"),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("social.networks.not_found", result.Error.Code);
    }

    [Fact]
    public async Task UpdateHandler_InvalidDisplayName_ReturnsValidationError()
    {
        var harness = new Harness(Now);
        var created = await harness.Create.Handle(
            new CreateSocialNetworkCommand("instagram", "Instagram", "instagram"),
            CancellationToken.None);

        var result = await harness.Update.Handle(
            new UpdateSocialNetworkCommand(created.Value.Id, "", "icon"),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("social.networks.invalid_display_name", result.Error.Code);
    }

    // --- Deactivate ---

    [Fact]
    public async Task DeactivateHandler_ActiveNetwork_ReturnsDeactivatedNetwork()
    {
        var harness = new Harness(Now);
        var created = await harness.Create.Handle(
            new CreateSocialNetworkCommand("instagram", "Instagram", "instagram"),
            CancellationToken.None);

        var result = await harness.Deactivate.Handle(
            new DeactivateSocialNetworkCommand(created.Value.Id),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.IsActive);
    }

    [Fact]
    public async Task DeactivateHandler_AlreadyInactive_ReturnsAlreadyInactive()
    {
        var harness = new Harness(Now);
        var created = await harness.Create.Handle(
            new CreateSocialNetworkCommand("instagram", "Instagram", "instagram"),
            CancellationToken.None);
        await harness.Deactivate.Handle(
            new DeactivateSocialNetworkCommand(created.Value.Id),
            CancellationToken.None);

        var result = await harness.Deactivate.Handle(
            new DeactivateSocialNetworkCommand(created.Value.Id),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("social.networks.already_inactive", result.Error.Code);
    }

    [Fact]
    public async Task DeactivateHandler_UnknownId_ReturnsNotFound()
    {
        var harness = new Harness(Now);

        var result = await harness.Deactivate.Handle(
            new DeactivateSocialNetworkCommand(Guid.NewGuid()),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("social.networks.not_found", result.Error.Code);
    }

    // --- Activate ---

    [Fact]
    public async Task ActivateHandler_InactiveNetwork_ReturnsActivatedNetwork()
    {
        var harness = new Harness(Now);
        var created = await harness.Create.Handle(
            new CreateSocialNetworkCommand("instagram", "Instagram", "instagram"),
            CancellationToken.None);
        await harness.Deactivate.Handle(
            new DeactivateSocialNetworkCommand(created.Value.Id),
            CancellationToken.None);

        var result = await harness.Activate.Handle(
            new ActivateSocialNetworkCommand(created.Value.Id),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.IsActive);
    }

    [Fact]
    public async Task ActivateHandler_AlreadyActive_ReturnsAlreadyActive()
    {
        var harness = new Harness(Now);
        var created = await harness.Create.Handle(
            new CreateSocialNetworkCommand("instagram", "Instagram", "instagram"),
            CancellationToken.None);

        var result = await harness.Activate.Handle(
            new ActivateSocialNetworkCommand(created.Value.Id),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("social.networks.already_active", result.Error.Code);
    }

    [Fact]
    public async Task ActivateHandler_UnknownId_ReturnsNotFound()
    {
        var harness = new Harness(Now);

        var result = await harness.Activate.Handle(
            new ActivateSocialNetworkCommand(Guid.NewGuid()),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("social.networks.not_found", result.Error.Code);
    }

    // --- GetByIds (Resolution 4) ---

    [Fact]
    public async Task GetByIdsHandler_ReturnsRequestedNetworksByIds()
    {
        var harness = new Harness(Now);
        var r1 = await harness.Create.Handle(
            new CreateSocialNetworkCommand("instagram", "Instagram", "instagram"),
            CancellationToken.None);
        var r2 = await harness.Create.Handle(
            new CreateSocialNetworkCommand("tiktok", "TikTok", "tiktok"),
            CancellationToken.None);
        await harness.Create.Handle(
            new CreateSocialNetworkCommand("facebook", "Facebook", "facebook"),
            CancellationToken.None);

        var ids = new[] { new SocialNetworkId(r1.Value.Id), new SocialNetworkId(r2.Value.Id) };
        var results = await harness.Repository.GetByIdsAsync(ids, CancellationToken.None);

        Assert.Equal(2, results.Count);
        Assert.Contains(results, n => n.Code == "instagram");
        Assert.Contains(results, n => n.Code == "tiktok");
    }

    // --- Harness ---

    private sealed class Harness
    {
        public Harness(DateTime nowUtc)
        {
            Repository = new InMemorySocialNetworkRepository();
            var clock = new TestClock(nowUtc);

            Create = new CreateSocialNetworkHandler(Repository, clock);
            GetById = new GetSocialNetworkByIdHandler(Repository);
            GetAll = new GetAllSocialNetworksHandler(Repository);
            Update = new UpdateSocialNetworkHandler(Repository, clock);
            Deactivate = new DeactivateSocialNetworkHandler(Repository, clock);
            Activate = new ActivateSocialNetworkHandler(Repository, clock);
        }

        public InMemorySocialNetworkRepository Repository { get; }
        public CreateSocialNetworkHandler Create { get; }
        public GetSocialNetworkByIdHandler GetById { get; }
        public GetAllSocialNetworksHandler GetAll { get; }
        public UpdateSocialNetworkHandler Update { get; }
        public DeactivateSocialNetworkHandler Deactivate { get; }
        public ActivateSocialNetworkHandler Activate { get; }
    }

    private sealed class InMemorySocialNetworkRepository : ISocialNetworkRepository
    {
        private readonly Dictionary<SocialNetworkId, SocialNetwork> _store = new();
        private readonly Dictionary<string, SocialNetworkId> _byCode = new(StringComparer.OrdinalIgnoreCase);

        public Task<SocialNetwork?> GetByIdAsync(SocialNetworkId id, CancellationToken cancellationToken)
        {
            _store.TryGetValue(id, out var network);
            return Task.FromResult(network);
        }

        public Task<SocialNetwork?> GetByCodeAsync(string code, CancellationToken cancellationToken)
        {
            if (_byCode.TryGetValue(code, out var id) && _store.TryGetValue(id, out var network))
            {
                return Task.FromResult<SocialNetwork?>(network);
            }

            return Task.FromResult<SocialNetwork?>(null);
        }

        public Task<PageResult<SocialNetwork>> ListAsync(SocialNetworkListQuery query, CancellationToken cancellationToken)
        {
            IEnumerable<SocialNetwork> source = _store.Values;

            if (!query.IncludeInactive)
            {
                source = source.Where(x => x.IsActive);
            }

            if (!string.IsNullOrWhiteSpace(query.SearchTerm))
            {
                var term = query.SearchTerm.Trim();
                source = source.Where(x =>
                    x.DisplayName.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                    x.Code.Contains(term, StringComparison.OrdinalIgnoreCase));
            }

            var ordered = source.OrderBy(x => x.DisplayName).ToList();
            var items = ordered
                .Skip((query.Page - 1) * query.PageSize)
                .Take(query.PageSize)
                .ToList();

            return Task.FromResult(new PageResult<SocialNetwork>(items, query.Page, query.PageSize, ordered.Count));
        }

        public Task<IReadOnlyList<SocialNetwork>> GetByIdsAsync(
            IReadOnlyCollection<SocialNetworkId> ids,
            CancellationToken cancellationToken)
        {
            var result = ids
                .Where(id => _store.ContainsKey(id))
                .Select(id => _store[id])
                .ToList();
            return Task.FromResult<IReadOnlyList<SocialNetwork>>(result);
        }

        public Task AddAsync(SocialNetwork network, CancellationToken cancellationToken)
        {
            if (_byCode.ContainsKey(network.Code))
            {
                throw new InvalidOperationException($"Duplicate code: {network.Code}");
            }

            _store[network.Id] = network;
            _byCode[network.Code] = network.Id;
            return Task.CompletedTask;
        }

        public void Update(SocialNetwork network)
        {
            _store[network.Id] = network;
        }

        public Task SaveChangesAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
