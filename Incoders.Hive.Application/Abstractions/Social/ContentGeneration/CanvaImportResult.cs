namespace Incoders.Hive.Application.Abstractions.Social.ContentGeneration;

/// <summary>
/// Provider-neutral result returned by <see cref="ICanvaDesignImportClient"/> on success.
///
/// <para>
/// Contains the Canva design identifier and the direct edit URL the user can open
/// to edit the imported design in Canva. The edit URL is valid for approximately 30 days.
/// </para>
///
/// <para>
/// This record lives in the Application layer and contains NO Infrastructure types,
/// no HttpClient, no JSON serializer attributes, and no Canva SDK dependencies.
/// </para>
/// </summary>
/// <param name="DesignId">The Canva design id returned by the Design Import API.</param>
/// <param name="EditUrl">
/// The direct edit URL for the imported design (e.g. <c>https://www.canva.com/design/{id}/edit</c>).
/// Valid for ~30 days after import.
/// </param>
/// <param name="ExpiresAt">
/// Approximate expiry of the edit URL. Set to 30 days from the time of import.
/// </param>
public sealed record CanvaImportResult(
    string DesignId,
    string EditUrl,
    DateTimeOffset ExpiresAt);
