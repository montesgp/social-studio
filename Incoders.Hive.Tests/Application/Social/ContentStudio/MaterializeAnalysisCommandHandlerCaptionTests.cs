using Incoders.Hive.Application.Abstractions;
using Incoders.Hive.Application.Abstractions.Social.ContentGeneration;
using Incoders.Hive.Application.Features.Social.ContentStudio.Materialize;
using Incoders.Hive.Domain.Social.ContentStudio;
using Incoders.Hive.Infrastructure.Identity;
using Incoders.Hive.Infrastructure.Persistence.Postgres;
using Microsoft.EntityFrameworkCore;

namespace Incoders.Hive.Tests.Application.Social.ContentStudio;

/// <summary>
/// Closes the caption gap in the analysis-scoped materialization path (used by the single-analyzer
/// flow, <see cref="GeneratorKind.SingleAnalyzer"/>). Before this, <see cref="MaterializeAnalysisCommandHandler"/>
/// always built a <c>null</c> caption for visual formats, deferring extraction/fallback as a stale
/// follow-up comment. The user's requirement ("a caption is required and can never be empty") applies
/// to this path exactly as it does to <see cref="MaterializeRunCommandHandler"/> — both now share
/// <see cref="VisualCaptionResolver"/> so they can never drift again.
/// </summary>
public sealed class MaterializeAnalysisCommandHandlerCaptionTests
{
    private static readonly Guid Org = Guid.NewGuid();
    private static readonly Guid Client = Guid.NewGuid();
    private static readonly Guid Analysis = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 6, 14, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Materialize_VisualItem_SeedsCaptionFromPublicationDescriptionAndHashtags()
    {
        await using var db = CreateDbContext();
        var sets = new EfContentSetRepository(db);
        var items = new EfContentItemRepository(db);

        var jobId = Guid.NewGuid();
        var resultId = Guid.NewGuid();
        var outputJson =
            "{\"coverTitle\":\"X\",\"publicationDescription\":\"Grow your brand today.\",\"hashtags\":[\"#growth\",\"#brand\"]}";

        var formatResults = new FakeFormatResultRepo(new[]
        {
            FormatResult(resultId, jobId, "carousel", outputJson, compositionSpec: "<spec/>"),
        });

        var handler = new MaterializeAnalysisCommandHandler(
            sets, items, formatResults, new EmptyAssetRepo(), new NullPackageRepo(), new FixedClock(Now));

        var result = await handler.Handle(new MaterializeAnalysisCommand(Org, Client, Analysis), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var persisted = (await items.ListByRunAsync(Org, jobId, CancellationToken.None)).Single();
        Assert.Equal(ContentMedium.Visual, persisted.Kind.Medium);
        Assert.Null(persisted.Draft.Text);
        Assert.NotNull(persisted.Draft.Caption);
        Assert.Equal("Grow your brand today.\n\n#growth #brand", persisted.Draft.Caption!.Value);
    }

    [Fact]
    public async Task Materialize_VisualItem_WithNoCaptionInOutput_UsesDeterministicFallback_NeverFailsJob()
    {
        await using var db = CreateDbContext();
        var sets = new EfContentSetRepository(db);
        var items = new EfContentItemRepository(db);

        var jobId = Guid.NewGuid();
        var resultId = Guid.NewGuid();
        var outputJson = "{\"coverTitle\":\"X\"}";
        var compositionSpecJson =
            """{"schemaVersion":1,"markup":"<div class=\"composition\"><div data-layer=\"content\"><h1 data-slot=\"headline\">Grow your brand today</h1></div></div>","canvas":{"width":1080,"height":1350,"dpi":96},"slotGeometry":[]}""";

        var formatResults = new FakeFormatResultRepo(new[]
        {
            FormatResult(resultId, jobId, "carousel", outputJson, compositionSpecJson),
        });

        var handler = new MaterializeAnalysisCommandHandler(
            sets, items, formatResults, new EmptyAssetRepo(), new NullPackageRepo(), new FixedClock(Now));

        var result = await handler.Handle(new MaterializeAnalysisCommand(Org, Client, Analysis), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var persisted = (await items.ListByRunAsync(Org, jobId, CancellationToken.None)).Single();
        Assert.NotNull(persisted.Draft.Caption);
        Assert.Equal("Grow your brand today", persisted.Draft.Caption!.Value);
    }

    [Fact]
    public async Task Materialize_TextItem_KeepsTextField_NoCaption()
    {
        await using var db = CreateDbContext();
        var sets = new EfContentSetRepository(db);
        var items = new EfContentItemRepository(db);

        var jobId = Guid.NewGuid();
        var resultId = Guid.NewGuid();
        var outputJson = "{\"script\":\"Hook. Body. CTA.\"}";

        var formatResults = new FakeFormatResultRepo(new[]
        {
            FormatResult(resultId, jobId, "reel-script", outputJson, compositionSpec: null),
        });

        var handler = new MaterializeAnalysisCommandHandler(
            sets, items, formatResults, new EmptyAssetRepo(), new NullPackageRepo(), new FixedClock(Now));

        var result = await handler.Handle(new MaterializeAnalysisCommand(Org, Client, Analysis), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var persisted = (await items.ListByRunAsync(Org, jobId, CancellationToken.None)).Single();
        Assert.Equal(ContentMedium.Text, persisted.Kind.Medium);
        Assert.Equal(outputJson, persisted.Draft.Text);
        Assert.Null(persisted.Draft.Caption);
    }

    private static ContentGenerationFormatResult FormatResult(
        Guid id, Guid jobId, string formatKey, string? outputJson, string? compositionSpec) =>
        new(id, jobId, formatKey, "Succeeded", outputJson, compositionSpec,
            RenderedAssetUri: null, InferredFieldsJson: null, PromptVersion: "v1", ErrorCode: null,
            CreatedAt: Now, UpdatedAt: Now);

    private static AppIdentityDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppIdentityDbContext>()
            .UseInMemoryDatabase($"materialize-analysis-caption-{Guid.NewGuid():N}")
            .Options;
        return new AppIdentityDbContext(options);
    }
}

file sealed class FixedClock(DateTime utcNow) : IClock
{
    public DateTime UtcNow { get; } = utcNow;
}

file sealed class FakeFormatResultRepo(IReadOnlyList<ContentGenerationFormatResult> results)
    : IContentGenerationFormatResultRepository
{
    public Task<ContentGenerationFormatResult> UpsertAsync(ContentGenerationFormatResult result, CancellationToken ct) =>
        throw new NotSupportedException();

    public Task<IReadOnlyList<ContentGenerationFormatResult>> ListByJobAsync(Guid jobId, CancellationToken ct) =>
        throw new NotSupportedException();

    public Task<IReadOnlyList<ContentGenerationFormatResult>> ListByAnalysisAndClientAsync(
        Guid organizationId, Guid analysisId, Guid clientId, CancellationToken ct) =>
        Task.FromResult(results);
}

file sealed class EmptyAssetRepo : IContentGenerationFormatAssetRepository
{
    public Task UpsertManyForFormatAsync(Guid formatResultId, IReadOnlyList<FormatAssetRow> assets, CancellationToken ct) =>
        throw new NotSupportedException();

    public Task<IReadOnlyList<FormatAssetRow>> ListByJobAsync(Guid jobId, CancellationToken ct) =>
        throw new NotSupportedException();

    public Task<IReadOnlyList<FormatAssetRow>> ListByAnalysisClientAsync(Guid organizationId, Guid analysisId, CancellationToken ct) =>
        throw new NotSupportedException();

    public Task<IReadOnlyList<FormatAssetRow>> ListByAnalysisAndClientAsync(
        Guid organizationId, Guid analysisId, Guid clientId, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<FormatAssetRow>>([]);

    public Task DeleteByFormatResultIdAsync(Guid formatResultId, CancellationToken ct) =>
        throw new NotSupportedException();
}

file sealed class NullPackageRepo : IGeneratedPackageRepository
{
    public Task InsertAsync(GeneratedPackage p, CancellationToken ct) => throw new NotSupportedException();

    public Task<GeneratedPackage?> GetByJobIdAsync(Guid jobId, Guid organizationId, CancellationToken ct) =>
        Task.FromResult<GeneratedPackage?>(null);
}
