namespace SocialStudio.Application.Features.Social.ContentGeneration.Shared;

/// <summary>
/// Corpus-grounded image-type distribution across all observed posts for a client.
/// Proportions sum approximately 1.0; <see cref="SampleSize"/> is the confidence / grounding signal.
/// CACD-PA-02; OQ-2.
/// </summary>
/// <remarks>
/// <para>
/// Derived by normalizing aggregate counts the multimodal extractor reports across ALL attached posts,
/// not a single carousel. This reconciles with the existing <c>BackgroundPattern</c>:
/// pattern = ORDER (which slides are creative), ratio = MAGNITUDE (how many, statistically).
/// </para>
/// <para>
/// <c>SampleSize = photo_count + flat_count + decorator_count</c> (total slides observed).
/// A larger <see cref="SampleSize"/> signals higher confidence in the proportions.
/// </para>
/// <para>Pure Application value object — no Infrastructure imports.</para>
/// </remarks>
/// <param name="Photo">Proportion of slides classified as photographic / staged photography.</param>
/// <param name="FlatColor">Proportion of slides classified as flat-color / mono-color.</param>
/// <param name="Decorator">Proportion of slides classified as decorator (non-hero) slides.</param>
/// <param name="SampleSize">Total slides observed across all analyzed posts (confidence signal).</param>
public sealed record ImageTypeRatio(
    double Photo,
    double FlatColor,
    double Decorator,
    int SampleSize);
