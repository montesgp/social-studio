namespace SocialStudio.Application.Abstractions.Social.ContentStudio;

/// <summary>
/// Hands a saved Content Studio item to a background worker that re-renders its flattened preview.
///
/// <para>
/// Flattening a visual means driving headless Chromium once PER ENVELOPE — a five-slide carousel is
/// five sequential renders, behind a two-slot render semaphore shared with content generation. Doing
/// that inside the <c>PUT …/items/{id}/visual</c> request made an interactive save take tens of
/// seconds and routinely outlive the dev proxy's socket, surfacing to the operator as
/// "Failed to fetch" on a save that had, in fact, partially happened.
/// </para>
///
/// <para>
/// So the save persists the markup — the source of truth — synchronously and returns, then enqueues
/// the flatten. The flattened image is a derived PREVIEW used at export time; if the process dies
/// before the worker gets to it, nothing authored is lost and the next save re-enqueues it.
/// </para>
/// </summary>
public interface IVisualFlattenQueue
{
    /// <summary>
    /// Queues a flatten pass for one item. Never throws and never blocks the caller: a queue that is
    /// full or shutting down drops the request, because a missing preview must never fail a save.
    /// </summary>
    /// <returns>True when the request was accepted; false when it was dropped.</returns>
    bool Enqueue(VisualFlattenRequest request);
}

/// <param name="OrganizationId">Tenant that owns the item — the worker opens its own scope and must re-scope.</param>
/// <param name="ItemId">Content Studio item whose markup was just saved.</param>
public sealed record VisualFlattenRequest(Guid OrganizationId, Guid ItemId);
