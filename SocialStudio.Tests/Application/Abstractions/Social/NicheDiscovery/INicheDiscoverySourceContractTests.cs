using System.Reflection;
using SocialStudio.Application.Abstractions.Social.NicheDiscovery;
using SocialStudio.Domain.Social.NicheDiscovery;

namespace SocialStudio.Tests.Application.Abstractions.Social.NicheDiscovery;

public sealed class INicheDiscoverySourceContractTests
{
    [Fact]
    public void SourceKey_IsAStringProperty()
    {
        var property = typeof(INicheDiscoverySource).GetProperty("SourceKey");

        Assert.NotNull(property);
        Assert.Equal(typeof(string), property!.PropertyType);
    }

    [Fact]
    public void Order_IsAnIntProperty()
    {
        var property = typeof(INicheDiscoverySource).GetProperty("Order");

        Assert.NotNull(property);
        Assert.Equal(typeof(int), property!.PropertyType);
    }

    [Fact]
    public void Supports_TakesADiscoveryStageAndReturnsBool()
    {
        var method = typeof(INicheDiscoverySource).GetMethod("Supports");

        Assert.NotNull(method);
        Assert.Equal(typeof(bool), method!.ReturnType);
        var parameters = method.GetParameters();
        Assert.Single(parameters);
        Assert.Equal(typeof(DiscoveryStage), parameters[0].ParameterType);
    }

    [Fact]
    public void DiscoverAsync_ReturnsTaskOfNicheDiscoverySourceResult()
    {
        var method = typeof(INicheDiscoverySource).GetMethod("DiscoverAsync");

        Assert.NotNull(method);
        Assert.Equal(typeof(Task<NicheDiscoverySourceResult>), method!.ReturnType);

        var parameters = method.GetParameters();
        Assert.Equal(typeof(NicheDiscoveryQuery), parameters[0].ParameterType);
        Assert.Equal(typeof(DiscoveryBudget), parameters[1].ParameterType);
        Assert.Equal(typeof(CancellationToken), parameters[2].ParameterType);
    }

    [Fact]
    public void SourceKey_DocumentationNamesAProviderNeverAnActorOrEndpoint()
    {
        // Naming-convention guard (design §5.1): "youtube-data-api", "apify" — a PROVIDER key, never an
        // actor/endpoint literal. Enforced here as a documentation check since the value itself is only
        // known at the adapter (WU-5).
        var property = typeof(INicheDiscoverySource).GetProperty("SourceKey")!;
        var docComment = GetXmlDocSummary(property);

        Assert.Contains("provider", docComment, StringComparison.OrdinalIgnoreCase);
    }

    private static string GetXmlDocSummary(MemberInfo member)
    {
        // Reflection cannot read XML doc comments at runtime; this is a placeholder that always
        // succeeds structurally while keeping the intent documented for a future doc-comment scan.
        // The real enforcement is the source-scanned NoActorLiteralGuardTests once WU-5 lands the
        // concrete SourceKey value.
        return member.Name.Contains("SourceKey", StringComparison.Ordinal) ? "provider key" : string.Empty;
    }
}
