using Incoders.Hive.Domain.Common;

namespace Incoders.Hive.Domain.Social.SingleAnalyzer;

public sealed record SingleAnalyzerScore(
    int Novelty,
    int ClientFit,
    int PlatformFit,
    int HookQuality,
    int Feasibility,
    int ExpectedEngagement)
{
    public static Result<SingleAnalyzerScore> Create(
        int novelty,
        int clientFit,
        int platformFit,
        int hookQuality,
        int feasibility,
        int expectedEngagement)
    {
        var score = new SingleAnalyzerScore(
            novelty,
            clientFit,
            platformFit,
            hookQuality,
            feasibility,
            expectedEngagement);

        return score.IsValid()
            ? Result.Success(score)
            : Result.Failure<SingleAnalyzerScore>(SingleAnalyzerErrors.ScoreInvalid);
    }

    public bool IsValid() =>
        IsDimensionValid(Novelty)
        && IsDimensionValid(ClientFit)
        && IsDimensionValid(PlatformFit)
        && IsDimensionValid(HookQuality)
        && IsDimensionValid(Feasibility)
        && IsDimensionValid(ExpectedEngagement);

    private static bool IsDimensionValid(int value) => value is >= 1 and <= 5;
}
