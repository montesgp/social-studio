namespace SocialStudio.Domain.Social.ContentStudio;

/// <summary>
/// Value mirror of a content-plan period (kind/year/month). Held locally to avoid importing the
/// <c>ContentPlans.Period</c> domain type and coupling the two bounded contexts.
/// </summary>
public readonly record struct PeriodRef(
    short Kind,
    int Year,
    int Month);
