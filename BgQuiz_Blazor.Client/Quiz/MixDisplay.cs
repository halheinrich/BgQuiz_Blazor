namespace BgQuiz_Blazor.Client.Quiz;

using System.Globalization;
using BgGame_Lib;

/// <summary>
/// The one home for the mix's user-facing wording shared across surfaces: the category
/// names (the panel's picker and the quiz page's composition line must agree), the
/// composition summary the quiz page leads with, and the refusal reason Home's Start and
/// Done's Restart render alike. All formatting is invariant.
/// </summary>
internal static class MixDisplay
{
    /// <summary>The kind-level label for the picker; parameterized kinds trail an ellipsis the parameter input completes.</summary>
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

    /// <summary>The full label for a concrete category, parameter included; the wrong-rate fraction renders as its display percent.</summary>
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
    /// The composition-first summary of a weighted run — e.g. <c>"Your quiz has 200 problems:
    /// 195 Never seen + 5 Ever got wrong."</c> — from every entry's actual draw, zero-draw
    /// entries included, in declared entry order.
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
    /// Why a weighted start was refused, worded for the refusal notice. One reason, because
    /// one is reachable: the mix is in effect only over a folder whose stats file was found
    /// readable at the pick, so a bind that finds no document found the file unreadable.
    /// </summary>
    public const string RefusalReason = "the existing " + QuizStatsFile.FileName + " couldn't be read";
}
