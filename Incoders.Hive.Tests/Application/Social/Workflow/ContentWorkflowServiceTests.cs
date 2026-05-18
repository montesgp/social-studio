using Incoders.Hive.Application.Features.Social.Workflow;
using Incoders.Hive.Infrastructure.Social.Workflow;
using Incoders.Hive.Tests.Common;

namespace Incoders.Hive.Tests.Application.Social.Workflow;

public sealed class ContentWorkflowServiceTests
{
    [Fact]
    public async Task Start_ThenGetResults_ReturnsPaginatedEnvelope()
    {
        var clock = new TestClock(new DateTime(2026, 5, 17, 12, 0, 0, DateTimeKind.Utc));
        var service = new InMemoryContentWorkflowService(clock);
        var organizationId = Guid.NewGuid();

        var started = await service.StartAsync(
            organizationId,
            new ContentWorkflowStartInput(
                Guid.NewGuid(),
                "CM Team",
                "viral",
                "mixed",
                ["tiktok", "instagram"]));

        Assert.True(started.IsSuccess);

        var page1 = await service.GetResultsAsync(organizationId, started.Value.WorkflowId, 1, 10);
        var page2 = await service.GetResultsAsync(organizationId, started.Value.WorkflowId, 2, 10);

        Assert.True(page1.IsSuccess);
        Assert.True(page2.IsSuccess);
        Assert.Equal(10, page1.Value.Items.Count);
        Assert.Equal(10, page2.Value.Items.Count);
        Assert.True(page1.Value.TotalCount >= 50);
        Assert.Equal(1, page1.Value.Page);
        Assert.Equal(2, page2.Value.Page);
        Assert.NotEqual(page1.Value.Items[0].Id, page2.Value.Items[0].Id);
    }

    [Fact]
    public async Task Select_Action_AndPackage_WorksForChosenItems()
    {
        var clock = new TestClock(new DateTime(2026, 5, 17, 12, 0, 0, DateTimeKind.Utc));
        var service = new InMemoryContentWorkflowService(clock);
        var organizationId = Guid.NewGuid();

        var started = await service.StartAsync(
            organizationId,
            new ContentWorkflowStartInput(
                Guid.NewGuid(),
                "Acme Social",
                "niche",
                "educational",
                ["tiktok"]));

        var results = await service.GetResultsAsync(organizationId, started.Value.WorkflowId, 1, 5);
        var selectedIds = results.Value.Items.Take(2).Select(x => x.Id).ToList();

        var selection = await service.SetSelectionAsync(organizationId, started.Value.WorkflowId, selectedIds);
        var action = await service.RunActionAsync(
            organizationId,
            started.Value.WorkflowId,
            "download_audio",
            selectedIds);
        var package = await service.GeneratePackageAsync(organizationId, started.Value.WorkflowId);

        Assert.True(selection.IsSuccess);
        Assert.Equal(2, selection.Value.SelectedCount);
        Assert.True(action.IsSuccess);
        Assert.Equal("completed", action.Value.Status);
        Assert.True(package.IsSuccess);
        Assert.Equal(2, package.Value.ItemCount);
        Assert.Contains(".zip", package.Value.PackageFileName);
    }

    [Fact]
    public async Task History_IsScopedByOrganization()
    {
        var clock = new TestClock(new DateTime(2026, 5, 17, 12, 0, 0, DateTimeKind.Utc));
        var service = new InMemoryContentWorkflowService(clock);
        var organizationA = Guid.NewGuid();
        var organizationB = Guid.NewGuid();

        await service.StartAsync(
            organizationA,
            new ContentWorkflowStartInput(Guid.NewGuid(), "Org A Client", "references", "mixed", ["instagram"]));

        await service.StartAsync(
            organizationB,
            new ContentWorkflowStartInput(Guid.NewGuid(), "Org B Client", "competitors", "informative", ["youtube"]));

        var historyA = await service.ListHistoryAsync(organizationA, 20);
        var historyB = await service.ListHistoryAsync(organizationB, 20);

        Assert.True(historyA.IsSuccess);
        Assert.True(historyB.IsSuccess);
        Assert.Single(historyA.Value);
        Assert.Single(historyB.Value);
        Assert.Equal("Org A Client", historyA.Value[0].ClientName);
        Assert.Equal("Org B Client", historyB.Value[0].ClientName);
    }
}
