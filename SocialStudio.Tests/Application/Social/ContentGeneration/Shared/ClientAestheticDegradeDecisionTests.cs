using SocialStudio.Application.Features.Social.ContentGeneration.Shared;
using SocialStudio.Domain.Social.Clients;
using SocialStudio.Domain.Social.ContentGeneration;

namespace SocialStudio.Tests.Application.Social.ContentGeneration.Shared;

/// <summary>
/// Truth table for <see cref="ClientAestheticDegradeDecision"/> — the pure predicate that decides
/// whether a ClientAesthetic carousel slide is eligible for the deterministic faithful-flat degrade
/// and whether trigger A (failed photo image generation) or trigger B (unusable AI designer output)
/// fires. Design §1 (D1). Satisfies CAFF-CG-01/02/03 scenarios at the pure-function layer.
/// </summary>
public sealed class ClientAestheticDegradeDecisionTests
{
    // -----------------------------------------------------------------------
    // IsEligible — requires ClientAesthetic policy AND carousel layout AND resolved ColorPairings.
    // -----------------------------------------------------------------------

    [Fact]
    public void IsEligible_ClientAestheticPolicy_CarouselLayout_HasColorPairings_ReturnsTrue()
    {
        Assert.True(ClientAestheticDegradeDecision.IsEligible(
            BackgroundSourcingPolicy.ClientAesthetic, isCarouselLayout: true, hasColorPairings: true));
    }

    [Fact]
    public void IsEligible_NonClientAestheticPolicy_ReturnsFalse()
    {
        // A non-ClientAesthetic policy MUST never become eligible — the legacy scrim-card fallback
        // MUST keep running for every other policy (CAFF-CG-02 regression scenario).
        Assert.False(ClientAestheticDegradeDecision.IsEligible(
            BackgroundSourcingPolicy.AiPalette, isCarouselLayout: true, hasColorPairings: true));
    }

    [Fact]
    public void IsEligible_NullPolicy_ReturnsFalse()
    {
        Assert.False(ClientAestheticDegradeDecision.IsEligible(
            policy: null, isCarouselLayout: true, hasColorPairings: true));
    }

    [Fact]
    public void IsEligible_NonCarouselLayout_ReturnsFalse()
    {
        Assert.False(ClientAestheticDegradeDecision.IsEligible(
            BackgroundSourcingPolicy.ClientAesthetic, isCarouselLayout: false, hasColorPairings: true));
    }

    [Fact]
    public void IsEligible_NoColorPairings_ReturnsFalse()
    {
        // No ColorPairings means there is nothing faithful to degrade to — cold-start parity.
        Assert.False(ClientAestheticDegradeDecision.IsEligible(
            BackgroundSourcingPolicy.ClientAesthetic, isCarouselLayout: true, hasColorPairings: false));
    }

    // -----------------------------------------------------------------------
    // TriggerA — Photo-classified slide whose image generation failed.
    // -----------------------------------------------------------------------

    [Fact]
    public void TriggerA_Eligible_PhotoKind_NoGeneratedImage_ReturnsPhotoImageUnavailable()
    {
        var result = ClientAestheticDegradeDecision.TriggerA(
            eligible: true, mappedKind: SlideBackgroundKind.Photo, hasGeneratedImage: false);

        Assert.Equal(SlideDegradeTrigger.PhotoImageUnavailable, result);
    }

    [Fact]
    public void TriggerA_Eligible_PhotoKind_HasGeneratedImage_ReturnsNone()
    {
        // The slide DID receive a generated image — no degrade needed.
        var result = ClientAestheticDegradeDecision.TriggerA(
            eligible: true, mappedKind: SlideBackgroundKind.Photo, hasGeneratedImage: true);

        Assert.Equal(SlideDegradeTrigger.None, result);
    }

    [Fact]
    public void TriggerA_Eligible_FlatColorKind_ReturnsNone()
    {
        // FlatColor never reaches trigger A — the short-circuit already handles it upstream.
        var result = ClientAestheticDegradeDecision.TriggerA(
            eligible: true, mappedKind: SlideBackgroundKind.FlatColor, hasGeneratedImage: false);

        Assert.Equal(SlideDegradeTrigger.None, result);
    }

    [Fact]
    public void TriggerA_Eligible_NullMappedKind_ReturnsNone()
    {
        var result = ClientAestheticDegradeDecision.TriggerA(
            eligible: true, mappedKind: null, hasGeneratedImage: false);

        Assert.Equal(SlideDegradeTrigger.None, result);
    }

    [Fact]
    public void TriggerA_NotEligible_PhotoKind_NoGeneratedImage_ReturnsNone()
    {
        // Not eligible (e.g. non-ClientAesthetic policy) MUST never fire, regardless of kind/image state.
        var result = ClientAestheticDegradeDecision.TriggerA(
            eligible: false, mappedKind: SlideBackgroundKind.Photo, hasGeneratedImage: false);

        Assert.Equal(SlideDegradeTrigger.None, result);
    }

    // -----------------------------------------------------------------------
    // TriggerB — AI designer returned no usable background (error OR guardrail rejection) and no
    // enforced background exists.
    // -----------------------------------------------------------------------

    [Fact]
    public void TriggerB_Eligible_NoEnforcedBackground_NoUsableAiMarkup_ReturnsDesignerUnusable()
    {
        var result = ClientAestheticDegradeDecision.TriggerB(
            eligible: true, hasEnforcedBackground: false, hasUsableAiMarkup: false);

        Assert.Equal(SlideDegradeTrigger.DesignerUnusable, result);
    }

    [Fact]
    public void TriggerB_Eligible_HasEnforcedBackground_ReturnsNone()
    {
        // An enforced background (brand template / promoted AI image) always wins — no degrade.
        var result = ClientAestheticDegradeDecision.TriggerB(
            eligible: true, hasEnforcedBackground: true, hasUsableAiMarkup: false);

        Assert.Equal(SlideDegradeTrigger.None, result);
    }

    [Fact]
    public void TriggerB_Eligible_HasUsableAiMarkup_ReturnsNone()
    {
        var result = ClientAestheticDegradeDecision.TriggerB(
            eligible: true, hasEnforcedBackground: false, hasUsableAiMarkup: true);

        Assert.Equal(SlideDegradeTrigger.None, result);
    }

    [Fact]
    public void TriggerB_NotEligible_NoEnforcedBackground_NoUsableAiMarkup_ReturnsNone()
    {
        // A non-ClientAesthetic job MUST keep the legacy scrim-card fallback (CAFF-CG-02 regression).
        var result = ClientAestheticDegradeDecision.TriggerB(
            eligible: false, hasEnforcedBackground: false, hasUsableAiMarkup: false);

        Assert.Equal(SlideDegradeTrigger.None, result);
    }
}
