using Incoders.Hive.Application.Features.Social.ContentGeneration.Shared;
using Incoders.Hive.Domain.Social.ContentGeneration;

namespace Incoders.Hive.Tests.Application.Social.ContentGeneration.Shared;

/// <summary>
/// Issue #70: conditional @handle signature — client-field-only gate.
/// <see cref="AttributionPolicy.ResolveSignature"/> must only emit an attribution/signature when
/// the client's <see cref="ClientProfileSnapshot.SignsOwnSlides"/> is <c>true</c>. Plain boolean
/// gate, no tri-state, no profile-analysis detection.
/// </summary>
public sealed class AttributionPolicySignsOwnSlidesTests
{
    [Fact]
    public void ResolveSignature_SignsOwnSlidesTrue_HandlePresent_ReturnsHandle()
    {
        var client = new ClientProfileSnapshot("Target Brand", null, null, Handle: "@target_brand", SignsOwnSlides: true);
        var brand = BrandSnapshot.FromBrandData(brand: null, strategy: null, monthlyObjective: null, client: client);

        var signature = AttributionPolicy.ResolveSignature(brand, modelSignatureText: null);

        Assert.Equal("@target_brand", signature);
    }

    [Fact]
    public void ResolveSignature_SignsOwnSlidesFalse_HandlePresent_ReturnsNull()
    {
        var client = new ClientProfileSnapshot("Target Brand", null, null, Handle: "@target_brand", SignsOwnSlides: false);
        var brand = BrandSnapshot.FromBrandData(brand: null, strategy: null, monthlyObjective: null, client: client);

        var signature = AttributionPolicy.ResolveSignature(brand, modelSignatureText: null);

        Assert.Null(signature);
    }

    [Fact]
    public void ResolveSignature_SignsOwnSlidesTrue_HandleAndAllFallbacksAbsent_ReturnsNull()
    {
        // Handle null, no notes-derived handle, blank name, no model signature — the pre-existing
        // "nothing to attribute" outcome is unchanged by the new SignsOwnSlides gate.
        var client = new ClientProfileSnapshot(" ", null, null, Handle: null, SignsOwnSlides: true);
        var brand = BrandSnapshot.FromBrandData(brand: null, strategy: null, monthlyObjective: null, client: client);

        var signature = AttributionPolicy.ResolveSignature(brand, modelSignatureText: null);

        Assert.Null(signature);
    }

    [Fact]
    public void ResolveSignature_SignsOwnSlidesFalse_ModelSignatureProvided_ReturnsNull()
    {
        // Even an AI-authored signature must be suppressed when the client has not opted in.
        var client = new ClientProfileSnapshot("Target Brand", null, null, SignsOwnSlides: false);
        var brand = BrandSnapshot.FromBrandData(brand: null, strategy: null, monthlyObjective: null, client: client);

        var signature = AttributionPolicy.ResolveSignature(brand, modelSignatureText: "@model_provided");

        Assert.Null(signature);
    }

    [Fact]
    public void ResolveSignature_NoClientSnapshot_ReturnsNull()
    {
        var brand = BrandSnapshot.FromBrandData(brand: null, strategy: null, monthlyObjective: null, client: null);

        var signature = AttributionPolicy.ResolveSignature(brand, modelSignatureText: "@model_provided");

        Assert.Null(signature);
    }
}
