namespace Incoders.Hive.Application.Features.Social.DiscoveryBankIntelligence;

/// <summary>
/// Cost and blast-radius bounds for the AI Analysis tool, bound from the configuration section
/// <c>Social:DiscoveryBankIntelligence</c>.
///
/// WHY THESE EXIST. The bank grows without an upper bound — it is append-only and shared by every client
/// of an organization. Two things must therefore never scale with it: what the model is shown, and what
/// one question costs. These caps are the only thing standing between "an operator asked a question" and
/// "we paid to send a model several thousand rows it would have ranked wrongly anyway".
///
/// Defaults are deliberately small. The tool's value is a verifiable shortlist, not a data dump.
/// </summary>
public sealed class DiscoveryBankIntelligenceOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Social:DiscoveryBankIntelligence";

    /// <summary>
    /// The HARD ceiling on how many computed rows (or breakdown buckets) reach the model, whatever the
    /// question asked for. A "top 500" is answered as a top <see cref="MaxRowsToModel"/> against the full
    /// bank, and the response says how many candidates actually matched, so the narrowing is visible
    /// rather than silent.
    ///
    /// Default 10 — twice the "top 5" the tool exists to answer, small enough that the narration prompt
    /// stays cheap no matter how large the bank gets.
    /// </summary>
    public int MaxRowsToModel { get; set; } = 10;

    /// <summary>
    /// The limit used when the question named no explicit count ("which ones perform best?").
    /// Default 5 — the shortlist size the operator asks for in practice.
    /// </summary>
    public int DefaultLimit { get; set; } = 5;

    /// <summary>
    /// How many previous chat turns are forwarded with the planning call. Only the planner needs history
    /// (to resolve "and by comments?"); the narration call sees the computed rows and nothing else.
    ///
    /// Default 6 — three exchanges, enough for follow-ups without letting a long session inflate the
    /// prompt turn after turn.
    /// </summary>
    public int MaxHistoryTurns { get; set; } = 6;

    /// <summary>
    /// Longest accepted question, in characters. A rejected question costs nothing; a pasted document
    /// forwarded to a provider costs money and answers nothing.
    /// Default 500.
    /// </summary>
    public int MaxQuestionLength { get; set; } = 500;

    /// <summary>
    /// Longest history entry forwarded, in characters. Older turns are summaries for the planner, not
    /// evidence, so truncating them loses nothing that matters.
    /// Default 400.
    /// </summary>
    public int MaxHistoryEntryLength { get; set; } = 400;
}
