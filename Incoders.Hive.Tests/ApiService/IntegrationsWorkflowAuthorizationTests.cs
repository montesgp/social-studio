namespace Incoders.Hive.Tests.ApiService;

public sealed class IntegrationsWorkflowAuthorizationTests
{
    [Fact]
    public void WorkflowConfigurationEndpointsRequirePlatformAdminPolicy()
    {
        var source = ReadRepositoryFile(
            "Incoders.Hive.ApiService",
            "Endpoints",
            "Integrations",
            "MapIntegrationsEndpoints.cs");

        Assert.Contains("app.MapGroup(\"/api/integrations/workflow-configurations\")", source);
        Assert.Contains(
            ".RequireAuthorization(IdentityServicesExtensions.Policies.PlatformAdmin)",
            source);
    }

    private static string ReadRepositoryFile(params string[] relativePathParts)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);

        while (current is not null)
        {
            var candidate = Path.Combine([current.FullName, .. relativePathParts]);
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }

            current = current.Parent;
        }

        throw new FileNotFoundException(
            $"Could not locate repository file {Path.Combine(relativePathParts)} from {AppContext.BaseDirectory}.");
    }
}
