namespace BgQuiz_Blazor.Client.Quiz;

using System.Globalization;
using BgGame_Lib;

/// <summary>
/// The one home for user-facing stats-weighted-mix wording shared across
/// surfaces: category names (the mix panel's picker and the Quiz page's
/// shortfall notice must agree) and the stats-unavailable refusal reason
/// (Home's Start and Done's Restart render the same rule). Keeping these
/// here — rather than per-page string literals — is what stops the wording
/// and the rules behind it from drifting apart.
/// </summary>
internal static class MixDisplay
{
    /// <summary>
    /// The kind-level label for the mix panel's category picker. Parameterized
    /// kinds trail an ellipsis — the parameter input beside the picker
    /// completes the phrase; <see cref="CategoryLabel"/> is the completed
    /// form.
    /// </summary>
    public static string KindLabel(QuizCategoryKind kind) => kind switch
    {
        QuizCategoryKind.NeverSeen => "Never seen",
        QuizCategoryKind.GotWrong => "Ever got wrong",
        QuizCategoryKind.SeenFewerThan => "Seen fewer than…",
        QuizCategoryKind.NotSeenInDays => "Not seen in…",
        QuizCategoryKind.AvgEquityLossOver => "Avg equity loss over…",
        QuizCategoryKind.WrongRateOver => "Wrong more than…",
        QuizCategoryKind.EverythingElse => "Everything else",
        _ => kind.ToString(),
    };

    /// <summary>
    /// The full label for a concrete category, parameter included — e.g.
    /// <c>"Seen fewer than 3 times"</c>. The wrong-rate fraction renders as
    /// its display percent (thresholds are fractions per producer contract;
    /// rendering is a display concern); all formatting is invariant.
    /// </summary>
    public static string CategoryLabel(QuizCategory category) => category.Kind switch
    {
        QuizCategoryKind.SeenFewerThan => string.Create(CultureInfo.InvariantCulture,
            $"Seen fewer than {(int)category.Value!.Value} times"),
        QuizCategoryKind.NotSeenInDays => string.Create(CultureInfo.InvariantCulture,
            $"Not seen in {(int)category.Value!.Value} days"),
        QuizCategoryKind.AvgEquityLossOver => string.Create(CultureInfo.InvariantCulture,
            $"Avg equity loss over {category.Value!.Value.ToString("0.###", CultureInfo.InvariantCulture)}"),
        QuizCategoryKind.WrongRateOver => string.Create(CultureInfo.InvariantCulture,
            $"Wrong more than {(category.Value!.Value * 100.0).ToString("0.##", CultureInfo.InvariantCulture)}% of the time"),
        _ => KindLabel(category.Kind),
    };

    /// <summary>
    /// Why a weighted start was refused, worded for the refusal notice —
    /// derived from the pick-time capability and, when the capability was
    /// fine, the bound context's condition (the stage-2 unreadable-file
    /// case). One rule, rendered identically by Home's Start and Done's
    /// Restart.
    /// </summary>
    public static string RefusalReason(StatsSaveCapability capability, QuizStatsStatus status) =>
        capability switch
        {
            StatsSaveCapability.BrowserUnsupported => "this folder pick can't save stats in your browser",
            StatsSaveCapability.PermissionDenied => "write access to the folder was declined",
            _ => status == QuizStatsStatus.LoadFailed
                ? $"the existing {QuizStatsFile.FileName} couldn't be read"
                : "no stats context could be bound",
        };
}
