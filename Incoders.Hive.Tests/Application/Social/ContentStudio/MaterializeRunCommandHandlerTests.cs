using Incoders.Hive.Application.Abstractions;
using Incoders.Hive.Application.Abstractions.Social.ContentGeneration;
using Incoders.Hive.Application.Features.Social.ContentStudio.Materialize;
using Incoders.Hive.Domain.Social.ContentStudio;
using Incoders.Hive.Infrastructure.Identity;
using Incoders.Hive.Infrastructure.Persistence.Postgres;
using Microsoft.EntityFrameworkCore;

namespace Incoders.Hive.Tests.Application.Social.ContentStudio;

public sealed class MaterializeRunCommandHandlerTests
{
    private static readonly Guid Org = Guid.NewGuid();
    private static readonly Guid Client = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 6, 14, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Materialize_CreatesOneItemPerSuccessfulFormat_ExcludesFailed_AndIsIdempotent()
    {
        await using var db = CreateDbContext();
        var sets = new EfContentSetRepository(db);
        var items = new EfContentItemRepository(db);

        var jobId = Guid.NewGuid();
        var packageId = Guid.NewGuid();
        var flyerResultId = Guid.NewGuid();
        var captionResultId = Guid.NewGuid();
        var failedResultId = Guid.NewGuid();

        var formatResults = new FakeFormatResultRepo(new[]
        {
            FormatResult(flyerResultId, jobId, "flyer", "Succeeded", outputJson: null, compositionSpec: "<spec/>", errorCode: null),
            FormatResult(captionResultId, jobId, "caption", "Succeeded", outputJson: "{\"caption\":\"hi\"}", compositionSpec: null, errorCode: null),
            FormatResult(failedResultId, jobId, "carousel", "Failed", outputJson: null, compositionSpec: null, errorCode: "content-gen.failed"),
        });

        var assets = new FakeFormatAssetRepo(new[]
        {
            new FormatAssetRow(Guid.NewGuid(), flyerResultId, 0, "storage://flyer.pptx", "flyer.pptx",
                "application/vnd.openxmlformats-officedocument.presentationml.presentation", 10, "hash", Now, Now),
        });

        var packages = new FakePackageRepo(new GeneratedPackage(packageId, jobId, "storage://pkg.zip", 100, "{}", null, Now));

        var handler = new MaterializeRunCommandHandler(sets, items, formatResults, assets, packages, new FixedClock(Now));

        var first = await handler.Handle(new MaterializeRunCommand(Org, Client, jobId), CancellationToken.None);

        Assert.True(first.IsSuccess);
        Assert.Equal("GenerationRun", first.Value.Kind);
        Assert.Equal(jobId, first.Value.SourceRunId);
        Assert.Equal(2, first.Value.Summary.Total);   // failed carousel excluded
        Assert.Equal(2, first.Value.Summary.Draft);
        Assert.Equal(2, first.Value.Members.Count);

        var persisted = await items.ListByRunAsync(Org, jobId, CancellationToken.None);
        Assert.Equal(2, persisted.Count);
        Assert.All(persisted, i => Assert.Equal(jobId, i.Origin.SourceRunId));
        Assert.All(persisted, i => Assert.Equal(packageId, i.Origin.SourcePackageId));

        var flyer = persisted.Single(i => i.Origin.FormatKey == "flyer");
        Assert.Equal(flyerResultId, flyer.Origin.FormatResultId);
        Assert.Equal(ContentMedium.Visual, flyer.Kind.Medium);
        Assert.Equal("<spec/>", flyer.Draft.VisualSpecMarkup);
        Assert.Single(flyer.Draft.Assets);
        Assert.Equal("storage://flyer.pptx", flyer.Draft.Assets[0].StorageUri);

        var caption = persisted.Single(i => i.Origin.FormatKey == "caption");
        Assert.Equal(ContentMedium.Text, caption.Kind.Medium);
        Assert.Equal("{\"caption\":\"hi\"}", caption.Draft.Text);

        // Idempotent: re-materialization returns the same set without duplicating items.
        var second = await handler.Handle(new MaterializeRunCommand(Org, Client, jobId), CancellationToken.None);
        Assert.True(second.IsSuccess);
        Assert.Equal(first.Value.Id, second.Value.Id);

        var afterSecond = await items.ListByRunAsync(Org, jobId, CancellationToken.None);
        Assert.Equal(2, afterSecond.Count);
    }

    [Fact]
    public async Task Materialize_WithNoSuccessfulFormats_Fails()
    {
        await using var db = CreateDbContext();
        var handler = new MaterializeRunCommandHandler(
            new EfContentSetRepository(db),
            new EfContentItemRepository(db),
            new FakeFormatResultRepo(new[]
            {
                FormatResult(Guid.NewGuid(), Guid.NewGuid(), "flyer", "Failed", null, null, "content-gen.failed"),
            }),
            new FakeFormatAssetRepo(Array.Empty<FormatAssetRow>()),
            new FakePackageRepo(null),
            new FixedClock(Now));

        var result = await handler.Handle(new MaterializeRunCommand(Org, Client, Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsFailure);
    }

    private static ContentGenerationFormatResult FormatResult(
        Guid id, Guid jobId, string formatKey, string status, string? outputJson, string? compositionSpec, string? errorCode) =>
        new(id, jobId, formatKey, status, outputJson, compositionSpec,
            RenderedAssetUri: null, InferredFieldsJson: null, PromptVersion: "v1", ErrorCode: errorCode,
            CreatedAt: Now, UpdatedAt: Now);

    private static AppIdentityDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppIdentityDbContext>()
            .UseInMemoryDatabase($"materialize-{Guid.NewGuid():N}")
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
        Task.FromResult(results);
}

file sealed class FakeFormatAssetRepo(IReadOnlyList<FormatAssetRow> assets)
    : IContentGenerationFormatAssetRepository
{
    public Task UpsertManyForFormatAsync(Guid formatResultId, IReadOnlyList<FormatAssetRow> a, CancellationToken ct) =>
        throw new NotSupportedException();

    public Task<IReadOnlyList<FormatAssetRow>> ListByJobAsync(Guid jobId, CancellationToken ct) =>
        Task.FromResult(assets);

    public Task<IReadOnlyList<FormatAssetRow>> ListByAnalysisClientAsync(Guid organizationId, Guid analysisId, CancellationToken ct) =>
        throw new NotSupportedException();

    public Task DeleteByFormatResultIdAsync(Guid formatResultId, CancellationToken ct) =>
        throw new NotSupportedException();
}

file sealed class FakePackageRepo(GeneratedPackage? package) : IGeneratedPackageRepository
{
    public Task InsertAsync(GeneratedPackage p, CancellationToken ct) => throw new NotSupportedException();

    public Task<GeneratedPackage?> GetByJobIdAsync(Guid jobId, Guid organizationId, CancellationToken ct) =>
        Task.FromResult(package);
}
