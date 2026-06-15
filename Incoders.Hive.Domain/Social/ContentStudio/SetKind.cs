namespace Incoders.Hive.Domain.Social.ContentStudio;

/// <summary>
/// Kind of a <see cref="ContentSet"/>. Only <see cref="GenerationRun"/> is produced in Phase 1;
/// the remaining values are forward seams for bulk/campaign/presentation grouping.
/// </summary>
public enum SetKind
{
    GenerationRun = 0,
    Campaign = 1,
    PlanPeriod = 2,
    ClientPresentation = 3,
    AdHocSelection = 4,
}
