using SocialStudio.Application.Abstractions.Social;
using SocialStudio.Application.Features.Social.ContentGeneration.Visual.Fonts;
using SocialStudio.Domain.Social.ClientBrandManuals;
using SocialStudio.Domain.Social.Clients;

namespace SocialStudio.Tests.Application.Social.ContentGeneration.Visual.Fonts;

/// <summary>
/// TDD tests for <see cref="GetRenderCapabilitiesQueryHandler"/> — the endpoint the Web font picker
/// consumes (R2.1-R2.3, F3). Verifies the picker/render contract is sourced from
/// <see cref="FontCatalog"/> + <see cref="IFontProvider"/>, never a hardcoded list.
/// </summary>
public sealed class GetRenderCapabilitiesQueryTests
{
    private static readonly Guid Org = Guid.NewGuid();
    private static readonly Guid Client = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 7, 19, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Handle_WithBrandTypography_ListsBrandFirst_ThenSystemList()
    {
        var manual = ClientBrandManual.Create(
            ClientBrandManualId.New(), new ClientId(Client), Org, "Acme",
            null, null, null,
            new[] { new TypographyEntry("Heading", "Poppins", "heading", null, null) },
            null, null, Now).Value;
        var handler = new GetRenderCapabilitiesQueryHandler(
            new FakeManualRepository(manual), new FakeFontProvider(supportsColorEmoji: true));

        var result = await handler.Handle(new GetRenderCapabilitiesQuery(Org, Client), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.Fonts.Count > 1);
        Assert.Equal("Poppins", result.Value.Fonts[0].Family);
        Assert.True(result.Value.Fonts[0].IsBrand);
        Assert.Contains(result.Value.Fonts, f => f.Family == "Liberation Sans" && !f.IsBrand);
        Assert.True(result.Value.SupportsColorEmoji);
    }

    [Fact]
    public async Task Handle_NoActiveBrandManual_ReturnsSystemListOnly_NotAFailure()
    {
        var handler = new GetRenderCapabilitiesQueryHandler(
            new FakeManualRepository(manual: null), new FakeFontProvider(supportsColorEmoji: false));

        var result = await handler.Handle(new GetRenderCapabilitiesQuery(Org, Client), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotEmpty(result.Value.Fonts);
        Assert.All(result.Value.Fonts, f => Assert.False(f.IsBrand));
        Assert.False(result.Value.SupportsColorEmoji);
    }

    private sealed class FakeManualRepository : IClientBrandManualRepository
    {
        private readonly ClientBrandManual? _manual;

        public FakeManualRepository(ClientBrandManual? manual) => _manual = manual;

        public Task<ClientBrandManual?> GetByIdAsync(ClientBrandManualId id, Guid organizationId, CancellationToken ct) =>
            Task.FromResult(_manual);

        public Task<ClientBrandManual?> GetActiveByClientAsync(ClientId clientId, Guid organizationId, CancellationToken ct) =>
            Task.FromResult(_manual);

        public Task<(IReadOnlyList<ClientBrandManual> Items, int TotalCount)> ListByClientAsync(
            ClientId clientId, Guid organizationId, bool includeInactive, int page, int pageSize, CancellationToken ct) =>
            Task.FromResult<(IReadOnlyList<ClientBrandManual>, int)>((Array.Empty<ClientBrandManual>(), 0));

        public Task AddAsync(ClientBrandManual manual, CancellationToken ct) => Task.CompletedTask;

        public void Update(ClientBrandManual manual)
        {
        }

        public Task SaveChangesAsync(CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class FakeFontProvider : IFontProvider
    {
        private readonly bool _supportsColorEmoji;

        public FakeFontProvider(bool supportsColorEmoji) => _supportsColorEmoji = supportsColorEmoji;

        public byte[]? Resolve(string family) => null;

        public FontCapability Probe() => new(_supportsColorEmoji);
    }
}
