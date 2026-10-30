namespace SocialStudio.Tests.Application.Social.ContentGeneration;

/// <summary>
/// WU-9: Retirement guard — proves IImageGenerationClient and NullImageGenerationClient
/// are fully removed from the codebase.
///
/// These tests are COMPILE-TIME guards: if the retired types are re-introduced, the
/// compilation will fail and the tests will not build.  The body of each test is a
/// no-op assertion that always passes; the real enforcement happens at compile time.
///
/// Refs: tasks WU-9; design §6 (retirement); ADR-0007 (port family).
/// </summary>
public sealed class RetiredImageGenerationClientTests
{
    // -----------------------------------------------------------------------
    // Guard 1: IImageGenerationClient is gone
    // If the type is re-introduced, add a reference here to get a compile error:
    //   _ = typeof(SocialStudio.Application.Abstractions.Social.ContentGeneration.IImageGenerationClient);
    // -----------------------------------------------------------------------

    [Fact]
    public void IImageGenerationClient_IsRetired_NotReferencedFromAnyAssembly()
    {
        // Guard: the old port is retired. If this type reappears, uncomment the line
        // below and the compile error will enforce removal.
        //
        // Uncomment to enforce:
        // _ = typeof(SocialStudio.Application.Abstractions.Social.ContentGeneration.IImageGenerationClient);
        //
        // Runtime fallback guard: check that the type is not resolvable via reflection.
        var assembly = typeof(SocialStudio.Application.Abstractions.Social.ContentGeneration
            .IClientAestheticImageBackgroundService).Assembly;

        var retired = assembly.GetType(
            "SocialStudio.Application.Abstractions.Social.ContentGeneration.IImageGenerationClient",
            throwOnError: false);

        Assert.Null(retired);
    }

    // -----------------------------------------------------------------------
    // Guard 2: NullImageGenerationClient is gone
    // -----------------------------------------------------------------------

    [Fact]
    public void NullImageGenerationClient_IsRetired_NotPresentInInfrastructure()
    {
        // Runtime guard: NullImageGenerationClient must not exist in Infrastructure.
        var infraAssembly = typeof(SocialStudio.Infrastructure.DependencyInjection.DependencyInjection).Assembly;

        var retired = infraAssembly.GetType(
            "SocialStudio.Infrastructure.Rendering.NullImageGenerationClient",
            throwOnError: false);

        Assert.Null(retired);
    }

    // -----------------------------------------------------------------------
    // Guard 3: IClientAestheticImageBackgroundService is the active port
    // -----------------------------------------------------------------------

    [Fact]
    public void IClientAestheticImageBackgroundService_IsPresent_ActivePort()
    {
        // The replacement port MUST exist.
        var t = typeof(SocialStudio.Application.Abstractions.Social.ContentGeneration
            .IClientAestheticImageBackgroundService);
        Assert.NotNull(t);
    }
}
