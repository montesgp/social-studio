using SocialStudio.Application.Abstractions.Social.ContentGeneration;
using SocialStudio.Application.Features.Social.ContentStudio.Export;
using SocialStudio.Application.Features.Social.ContentGeneration.Pipeline;
using SocialStudio.Domain.Common;
using SocialStudio.Domain.Social.ContentStudio;
using SocialStudio.Infrastructure.Identity;
using SocialStudio.Infrastructure.Persistence.Postgres;
using Microsoft.EntityFrameworkCore;

namespace SocialStudio.Tests.Application.Social.ContentStudio;

public sealed class ExportPresentationQueryHandlerTests
{
    private static readonly Guid Org = Guid.NewGuid();
    private static readonly Guid Client = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 6, 14, 12, 0, 0, DateTimeKind.Utc);

    private const string Envelope =
        "{\"schemaVersion\":1,\"markup\":\"<div/>\",\"canvas\":{\"width\":1080,\"height\":1080,\"dpi\":96},\"slotGeometry\":[]}";

    [Fact]
    public async Task Export_BuildsDeckFromVisualItems_ExcludingDiscarded()
    {
        await using var db = CreateDbContext();
        var sets = new EfContentSetRepository(db);
        var items = new EfContentItemRepository(db);

        var runId = Guid.NewGuid();
        var setId = ContentSetId.New();
        var set = ContentSet.CreateForRun(setId, Org, Client, SetOrigin.ForRun(GeneratorKind.SingleAnalyzer, runId), Now).Value;

        var visual1 = VisualItem(runId, Envelope);
        var visual2 = VisualItem(runId, Envelope);
        var discarded = VisualItem(runId, Envelope);
        discarded.Discard(Now);
        var textOnly = ContentItem.Materialize(
            ContentItemId.New(), Org, Client,
            new ContentOrigin(GeneratorKind.SingleAnalyzer, runId, null, Guid.NewGuid(), "caption"),
            new ContentKind("caption", ContentMedium.Text),
            ContentDraft.ForText("hi", new TargetChannel("instagram", "feed")), Now).Value;

        await items.AddRangeAsync(new[] { visual1, visual2, discarded, textOnly }, CancellationToken.None);
        foreach (var (item, i) in new[] { visual1, visual2, discarded, textOnly }.Select((x, i) => (x, i)))
        {
            set.AddMember(item.Id, i);
        }
        await sets.AddAsync(set, CancellationToken.None);

        var adapter = new FakePptxAdapter(new byte[] { 1, 2, 3 });
        var handler = new ExportPresentationQueryHandler(sets, items, adapter);

        var result = await handler.Handle(new ExportPresentationQuery(Org, setId.Value), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(new byte[] { 1, 2, 3 }, result.Value.PptxBytes);
        Assert.EndsWith(".pptx", result.Value.DownloadFilename);
        // Only the two non-discarded visual items contribute envelopes (text + discarded excluded).
        Assert.Equal(2, adapter.LastEnvelopeCount);
    }

    [Fact]
    public async Task Export_WithNoVisualContent_Fails()
    {
        await using var db = CreateDbContext();
        var sets = new EfContentSetRepository(db);
        var items = new EfContentItemRepository(db);
        var setId = ContentSetId.New();
        var set = ContentSet.CreateForRun(setId, Org, Client, SetOrigin.ForRun(GeneratorKind.SingleAnalyzer, Guid.NewGuid()), Now).Value;
        await sets.AddAsync(set, CancellationToken.None);

        var handler = new ExportPresentationQueryHandler(sets, items, new FakePptxAdapter(new byte[] { 9 }));
        var result = await handler.Handle(new ExportPresentationQuery(Org, setId.Value), CancellationToken.None);

        Assert.True(result.IsFailure);
    }

    private static ContentItem VisualItem(Guid runId, string markup)
    {
        var id = ContentItemId.New();
        var draft = new ContentDraft(null, null, markup, Array.Empty<ContentAssetRef>(), new TargetChannel("instagram", "carousel"));
        return ContentItem.Materialize(
            id, Org, Client,
            new ContentOrigin(GeneratorKind.SingleAnalyzer, runId, null, Guid.NewGuid(), "carousel"),
            new ContentKind("carousel", ContentMedium.Visual), draft, Now).Value;
    }

    private static AppIdentityDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppIdentityDbContext>()
            .UseInMemoryDatabase($"cs-export-{Guid.NewGuid():N}")
            .Options;
        return new AppIdentityDbContext(options);
    }
}

file sealed class FakePptxAdapter(byte[] bytes) : IPptxExportAdapter
{
    public int LastEnvelopeCount { get; private set; }

    public Task<Result<byte[]>> BuildAsync(CompositionSpecEnvelope envelope, CancellationToken ct)
    {
        LastEnvelopeCount = 1;
        return Task.FromResult(Result.Success(bytes));
    }

    public Task<Result<byte[]>> BuildManyAsync(IReadOnlyList<CompositionSpecEnvelope> envelopes, CancellationToken ct)
    {
        LastEnvelopeCount = envelopes.Count;
        return Task.FromResult(Result.Success(bytes));
    }
}
