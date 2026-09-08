namespace BgQuiz_Blazor.Client.Quiz;

using System.Globalization;
using BgFolderAccess_Razor;
using BgGame_Lib;

/// <summary>
/// The one home for user-facing stats-weighted-mix wording shared across
/// surfaces: category names (the mix panel's picker and the Quiz page's mix
/// notices must agree), the composition summary those notices lead with,
/// and the stats-unavailable refusal reason (Home's Start and Done's
/// Restart render the same rule). Keeping these here — rather than per-page
/// string literals — is what stops the wording and the rules behind it from
/// drifting apart.
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
    /// The composition-first summary of a started weighted quiz — e.g.
    /// <c>"Your quiz has 200 problems: 195 Never seen + 5 Ever got wrong."</c>
    /// Rendered from every entry's actual draw (zero-draw entries included —
    /// honesty over tidiness) in declared entry order, which is contractual.
    /// This is the line every mix notice on the Quiz page leads with, so the
    /// user reads the quiz they actually got before any shortfall
    /// explanation. All formatting is invariant.
    /// </summary>
    public static string CompositionSummary(MixComposition composition)
    {
        ArgumentNullException.ThrowIfNull(composition);

        var entries = string.Join(" + ", composition.Entries.Select(e =>
            string.Create(CultureInfo.InvariantCulture, $"{e.Drawn} {CategoryLabel(e.Category)}")));
        var noun = composition.DrawnCount == 1 ? "problem" : "problems";
        return string.Create(CultureInfo.InvariantCulture,
            $"Your quiz has {composition.DrawnCount} {noun}: {entries}.");
    }

    /// <summary>
    /// Why a weighted start was refused, worded for the refusal notice — the
    /// bound context's condition, and nothing else. One rule, rendered
    /// identically by Home's Start and Done's Restart.
    ///
    /// <para>
    /// <b>Status-only since <c>halheinrich/backgammon#5</c> (2026-09-07).</b>
    /// It used to take the pick-time <see cref="FolderWriteCapability"/> and
    /// lead with two capability arms — "can't save stats in your browser",
    /// "write access was declined". Under <c>SPEC-filtering.md</c> §5's
    /// "Visible means in effect" those arms are unreachable: a weighted run
    /// happens only where the mix panel was visible, visibility reads the
    /// folder's stats fact, and the probe behind that fact cannot find stats in
    /// a folder it could not have written. So the capability is
    /// <see cref="FolderWriteCapability.Enabled"/> by construction at every
    /// call, and a parameter whose every non-default value is unreachable is a
    /// parameter that only invites a caller to pass the wrong thing. Restart
    /// closed the last hole by following the one rule with no special case,
    /// which is what let this collapse (the 2026-09-07 reachability proof
    /// against the consent model failed, and is recorded on the issue).
    /// </para>
    /// </summary>
    public static string RefusalReason(QuizStatsStatus status) =>
        status == QuizStatsStatus.LoadFailed
            ? $"the existing {QuizStatsFile.FileName} couldn't be read"
            : "no stats context could be bound";
}
