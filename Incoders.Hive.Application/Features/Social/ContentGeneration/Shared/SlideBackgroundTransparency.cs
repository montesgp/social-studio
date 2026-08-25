namespace Incoders.Hive.Application.Features.Social.ContentGeneration.Shared;

/// <summary>
/// Per-slide photo-transparency signal — index-aligned and parallel to <c>BackgroundPattern</c>
/// (<see cref="SlideBackgroundKind"/>) inside <c>AestheticExtraction.BackgroundTransparency</c> /
/// <c>ClientAestheticProfile.BackgroundTransparency</c>.
///
/// <para>
/// client-aesthetic-fidelity-fixes (WU-3a, design D3): the signal is deliberately a boolean HABIT flag
/// plus an advisory qualitative <see cref="Descriptor"/>, NEVER a numeric opacity/alpha ratio (settled
/// decision 2; CAFF-PA-03). The descriptor is advisory metadata only — it is never parsed at render
/// time (D5); the render mechanism uses one fixed opacity constant when <see cref="Blended"/> is true.
/// </para>
/// </summary>
/// <param name="Blended">
/// True when this slide shows a faded/blended (semi-transparent) photo background treatment.
/// </param>
/// <param name="Descriptor">
/// Optional qualitative description of the treatment (e.g. "faded overlay"). Null when the client's
/// habit doesn't warrant a description, or when only the boolean was determinable.
/// </param>
public sealed record SlideBackgroundTransparency(bool Blended, string? Descriptor);
