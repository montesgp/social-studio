using SocialStudio.Application.Abstractions;
using SocialStudio.Application.Abstractions.Social.ContentGeneration;
using SocialStudio.Application.Features.Social.ContentStudio.Materialize;
using SocialStudio.Domain.Social.ContentStudio;
using SocialStudio.Infrastructure.Identity;
using SocialStudio.Infrastructure.Persistence.Postgres;
using Microsoft.EntityFrameworkCore;

namespace SocialStudio.Tests.Application.Social.ContentStudio;

/// <summary>
/// Covers the regeneration → studio refresh contract: re-opening the studio after a format was
/// regenerated must surface the NEW draft for a pristine item, while never clobbering an item the
/// reviewer has already edited.
/// </summary>
public sealed class MaterializeAnalysisRegenerationTests
{
    private static readonly Guid Org = Guid.NewGuid();
    private static readonly Guid Client = Guid.NewGuid();
    private static readonly Guid Analysis = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 6, 14, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Rematerialize_AfterRegeneration_RefreshesPristineItemDraft()
    {
        await using var db = CreateDbContext();
        var sets = new EfContentSetRepository(db);
        var items = new EfContentItemRepository(db);

        var jobId = Guid.NewGuid();
        var resultId = Guid.NewGuid();

        var formatResults = new MutableFormatResultRepo();
        formatResults.Set(FormatResult(resultId, jobId, "carousel", compositionSpec: "spec-v1", updatedAt: Now));

        var handler = new MaterializeAnalysisCommandHandler(
            sets, items, formatResults, new EmptyAssetRepo(), new NullPackageRepo(), new FixedClock(Now));

        var first = await handler.Handle(new MaterializeAnalysisCommand(Org, Client, Analysis), CancellationToken.None);
        Assert.True(first.IsSuccess);

        var beforeItem = (await items.ListByRunAsync(Org, jobId, CancellationToken.None)).Single();
        Assert.Equal("spec-v1", beforeItem.Draft.VisualSpecMarkup);

        // Regeneration re-runs the pipeline and rewrites the SAME format result row with a newer spec.
        formatResults.Set(FormatResult(resultId, jobId, "carousel", compositionSpec: "spec-v2", updatedAt: Now.AddHours(1)));

        var second = await handler.Handle(new MaterializeAnalysisCommand(Org, Client, Analysis), CancellationToken.None);
        Assert.True(second.IsSuccess);
        Assert.Equal(first.Value.Id, second.Value.Id); // same set, incremental

        var afterItem = (await items.ListByRunAsync(Org, jobId, CancellationToken.None)).Single();
        Assert.Equal("spec-v2", afterItem.Draft.VisualSpecMarkup);
    }

    [Fact]
    public async Task Rematerialize_AfterRegeneration_DoesNotClobberEditedItem()
    {
        await using var db = CreateDbContext();
        var sets = new EfContentSetRepository(db);
        var items = new EfContentItemRepository(db);

        var jobId = Guid.NewGuid();
        var resultId = Guid.NewGuid();

        var formatResults = new MutableFormatResultRepo();
        formatResults.Set(FormatResult(resultId, jobId, "carousel", compositionSpec: "spec-v1", updatedAt: Now));

        var handler = new MaterializeAnalysisCommandHandler(
            sets, items, formatResults, new EmptyAssetRepo(), new NullPackageRepo(), new FixedClock(Now));

        await handler.Handle(new MaterializeAnalysisCommand(Org, Client, Analysis), CancellationToken.None);

        // Reviewer hand-edits the item: it leaves the pristine Draft state.
        var item = (await items.ListByRunAsync(Org, jobId, CancellationToken.None)).Single();
        var edit = item.EditVisual("hand-edited", item.Draft.Assets, Now.AddMinutes(5));
        Assert.True(edit.IsSuccess);
        await items.UpdateAsync(item, CancellationToken.None);

        // Regeneration produces a newer spec, but the edited item must be protected.
        formatResults.Set(FormatResult(resultId, jobId, "carousel", compositionSpec: "spec-v2", updatedAt: Now.AddHours(1)));

        await handler.Handle(new MaterializeAnalysisCommand(Org, Client, Analysis), CancellationToken.None);

        var afterItem = (await items.ListByRunAsync(Org, jobId, CancellationToken.None)).Single();
        Assert.Equal("hand-edited", afterItem.Draft.VisualSpecMarkup);
        Assert.Equal(ReviewState.Edited, afterItem.State);
    }

    private static ContentGenerationFormatResult FormatResult(
        Guid id, Guid jobId, string formatKey, string compositionSpec, DateTime updatedAt) =>
        new(id, jobId, formatKey, "Succeeded", OutputJson: null, CompositionSpecJson: compositionSpec,
            RenderedAssetUri: null, InferredFieldsJson: null, PromptVersion: "v1", ErrorCode: null,
            CreatedAt: Now, UpdatedAt: updatedAt);

    private static AppIdentityDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppIdentityDbContext>()
            .UseInMemoryDatabase($"materialize-regen-{Guid.NewGuid():N}")
            .Options;
        return new AppIdentityDbContext(options);
    }
}

file sealed class FixedClock(DateTime utcNow) : IClock
{
    public DateTime UtcNow { get; } = utcNow;
}

file sealed class MutableFormatResultRepo : IContentGenerationFormatResultRepository
{
    private IReadOnlyList<ContentGenerationFormatResult> _results = [];

    public void Set(params ContentGenerationFormatResult[] results) => _results = results;

    public Task<ContentGenerationFormatResult> UpsertAsync(ContentGenerationFormatResult result, CancellationToken ct) =>
        throw new NotSupportedException();

    public Task<IReadOnlyList<ContentGenerationFormatResult>> ListByJobAsync(Guid jobId, CancellationToken ct) =>
        throw new NotSupportedException();

    public Task<IReadOnlyList<ContentGenerationFormatResult>> ListByAnalysisAndClientAsync(
        Guid organizationId, Guid analysisId, Guid clientId, CancellationToken ct) =>
        Task.FromResult(_results);
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
