namespace Incoders.Hive.Application.Features.Social.ContentGeneration.Formats.CtaVariation;

/// <summary>A single CTA variation with its tone and intent.</summary>
public sealed record CtaVariation(
    string Text,
    string Tone,
    string Intent);

/// <summary>Structured output schema for CTA variation format generation.</summary>
public sealed record CtaVariationContent(IReadOnlyList<CtaVariation> Variations);
