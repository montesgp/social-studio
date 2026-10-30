namespace SocialStudio.Application.Features.Social.ContentGeneration.Shared;

/// <summary>
/// Closed vocabulary of art-direction methods detected from a client's visual corpus.
/// Enables deterministic routing at concept-ideation time (OQ-1; CACD-PA-01).
/// </summary>
/// <remarks>
/// <para><see cref="Unknown"/> is the zero-value / cold-start fallback. Unrecognised model output
/// MUST map to <see cref="Unknown"/> (no nullable suppression; <c>TreatWarningsAsErrors=true</c>).</para>
/// <para>Exemplar mappings: Atlas → <see cref="MetaphoricalEditorial"/>; Comet → <see cref="LiteralIllustration"/>.</para>
/// </remarks>
public enum ArtDirectionMethod
{
    /// <summary>Zero-value / cold-start. ImagePromptWriter runs the existing emulate-style instruction unchanged.</summary>
    Unknown = 0,

    /// <summary>Message becomes a visual metaphor; mono-color still-life / staged photography (Atlas exemplar).</summary>
    MetaphoricalEditorial,

    /// <summary>Literal tender illustration of the subject matter (Comet exemplar).</summary>
    LiteralIllustration,

    /// <summary>Symbolic object arrangement conveying the message.</summary>
    SymbolicStillLife,

    /// <summary>Candid or documentary-style photography of real-life subjects.</summary>
    DocumentaryPhotography,

    /// <summary>Bold graphic design with strong typographic elements as the primary visual.</summary>
    GraphicTypographic,

    /// <summary>Blends two or more methods within the same feed.</summary>
    Mixed,
}
