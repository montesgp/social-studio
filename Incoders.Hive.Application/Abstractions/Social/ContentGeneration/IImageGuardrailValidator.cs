using Incoders.Hive.Application.Abstractions.Ai;
using Incoders.Hive.Domain.Common;

namespace Incoders.Hive.Application.Abstractions.Social.ContentGeneration;

/// <summary>
/// Application port for validating a generated image before accepting it as the final result.
/// Used by <see cref="IClientAestheticImageBackgroundService"/> to enforce the background-only
/// contract per slide (CAC-IMG-03): each generated image is passed to this validator; if it
/// fails, the image is discarded and generation is retried up to the bounded budget.
/// On budget exhaustion the slide degrades to the CSS/descriptor fallback path — the job
/// is never marked failed.
///
/// <para>
/// This is a pure Application port — no provider, HTTP, or EF types in any signature.
/// The concrete implementation validates that the generated image does not contain foreground
/// elements (text, logos, handles, signatures) that violate the background-only directive.
/// </para>
///
/// Refs: CAC-IMG-03; design §3.4.
/// </summary>
public interface IImageGuardrailValidator
{
    /// <summary>
    /// Validates that <paramref name="result"/> meets the background-only contract.
    /// Returns <c>Result.Success(result)</c> when the image passes;
    /// returns <c>Result.Failure</c> with a descriptive error when it does not.
    /// </summary>
    Result<ImageGenerationResult> Validate(ImageGenerationResult result);
}
