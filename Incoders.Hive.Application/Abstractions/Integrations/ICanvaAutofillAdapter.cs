namespace Incoders.Hive.Application.Abstractions.Integrations;

/// <summary>
/// Deferred extension point — Canva Autofill / Brand Templates adapter (FUTURE, NOT wired in DI).
///
/// <para>
/// This port is intentionally left unimplemented. It exists as a named seam for the future
/// Autofill integration. The current implementation (WU-7) uses the Design Import API only
/// (<see cref="ICanvaDesignImportClient"/>).
/// </para>
///
/// <para>
/// Spec: CANVA-EXPORT-04 — Autofill and Brand Templates endpoints are NEVER called from
/// the current Export-to-Canva path. Do NOT wire this adapter in DI until the feature is
/// explicitly scoped and tested.
/// </para>
/// </summary>
// ReSharper disable once CommentTypo
// NOTE: Not registered in DI by design — kept as a compile-time seam only.
public interface ICanvaAutofillAdapter
{
    // Placeholder — no members until the feature is scoped.
    // Future: ApplyBrandTemplateAsync(Guid designId, Guid brandTemplateId, CancellationToken ct)
}
