namespace Incoders.Hive.Application.Features.Social.ContentGeneration.Shared;

/// <summary>
/// client-style-fidelity (CSF-PA-06): corrects the per-slide background kinds with the extractor's
/// own per-slide observation of a photographic subject.
///
/// <para>
/// "Flat" used to mean "a solid background", so a product photographed on a seamless black backdrop
/// was classified FlatColor and rendered as an empty colour <c>&lt;div&gt;</c> — the client's image was
/// lost. Demo Roasters had ten "flat" slides and nine of them carried a product, cut-out or photo.
/// A slide with any photographic subject needs a generated image, whatever its backdrop.
/// </para>
/// </summary>
public static class SlideKindReconciler
{
    /// <summary>
    /// Returns the pattern with every FlatColor slide whose aligned transparency entry reports a
    /// photographic subject promoted to Photo. Entries that are null, or that say nothing about a
    /// subject, leave the slide unchanged. Never demotes a slide.
    /// </summary>
    public static IReadOnlyList<SlideBackgroundKind>? Reclassify(
        IReadOnlyList<SlideBackgroundKind>? pattern,
        IReadOnlyList<SlideBackgroundTransparency?>? transparency,
        out bool changed)
    {
        changed = false;
        if (pattern is null || transparency is null)
        {
            return pattern;
        }

        var result = pattern.ToArray();
        for (var i = 0; i < result.Length && i < transparency.Count; i++)
        {
            if (result[i] == SlideBackgroundKind.FlatColor && transparency[i]?.HasPhotographicSubject == true)
            {
                result[i] = SlideBackgroundKind.Photo;
                changed = true;
            }
        }

        return result;
    }

    /// <summary>
    /// The photo/flat/decorator mix of a pattern. Used when reclassification changed the pattern, so
    /// the ratio downstream image planning reads agrees with the slides it plans for.
    /// </summary>
    public static ImageTypeRatio? RatioOf(IReadOnlyList<SlideBackgroundKind>? pattern)
    {
        if (pattern is not { Count: > 0 })
        {
            return null;
        }

        double count = pattern.Count;
        return new ImageTypeRatio(
            Photo: pattern.Count(k => k == SlideBackgroundKind.Photo) / count,
            FlatColor: pattern.Count(k => k == SlideBackgroundKind.FlatColor) / count,
            Decorator: pattern.Count(k => k == SlideBackgroundKind.Decorator) / count,
            SampleSize: pattern.Count);
    }
}
