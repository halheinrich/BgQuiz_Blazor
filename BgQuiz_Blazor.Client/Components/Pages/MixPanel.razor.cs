using BgGame_Lib;
using BgQuiz_Blazor.Client.Quiz;
using Microsoft.AspNetCore.Components;

namespace BgQuiz_Blazor.Client.Components.Pages;

/// <summary>
/// The stats-weighted mix builder hosted on <c>Home</c> — a <b>view</b> over
/// the app-scoped <see cref="MixDraft"/>: every gesture routes through the
/// draft's mutators, and the markup renders the draft's rows, toggle, length,
/// and validation. The panel holds no state of its own, so mix edits survive
/// in-app navigation with their service (ratified product behavior), and
/// everything Start derives from shares one lifetime.
///
/// <para>
/// <b>There is no activation control, because being mounted is the
/// activation</b> (<c>SPEC-filtering.md</c> §5, "Visible means in effect",
/// ruled 2026-09-07). The host mounts this panel exactly while
/// <see cref="MixVisibility.IsVisible"/>, and reads the same member to decide
/// what Start composes with, so the rows on screen are in effect whenever they
/// are on screen at all. That deletes the "Mix applies" checkbox
/// (<c>halheinrich/backgammon#182</c>'s form switch with it), its asymmetric
/// gate, and Fork A's activation sequencing — the panel now takes no parameters
/// whatever, and the host observes <see cref="MixDraft.Changed"/> like any other
/// state-container subscriber. The user's off-switch is the
/// <see cref="QuizSettings.WeightQuizzesByStats"/> setting, which unmounts this
/// panel rather than sitting inside it; a mix that fails to validate gates Start
/// with a hint saying to fix it or turn the mix off.
/// </para>
///
/// <para>
/// <b>Persistence is the draft's, per edit.</b> Every mutator writes the
/// built mix through while the draft validates (blank included), so storage
/// follows the screen with no commit moment; this panel never touches a
/// serializer or localStorage. <i>Clear mix</i> is
/// <see cref="MixDraft.ClearAsync"/> whole: blank the builder and persist the
/// blank mix — deliberate row removal, its one honest job. Blank stays a mix
/// in effect, the passthrough (ruled), which is what the zero-row copy says.
/// </para>
///
/// <para>
/// <b>Hydration is the draft's, triggered here.</b> Init awaits the
/// idempotent <see cref="MixDraft.EnsureHydratedAsync"/>: the first mount of
/// a setup loads the stored last-valid mix into the draft — and, since this
/// panel is mounted only where the mix is in effect, loads it <i>in effect</i>;
/// a re-mount after in-app navigation finds the draft already hydrated and
/// shows it as-is, edits included.
/// </para>
///
/// <para>
/// <b>Row order is semantic.</b> Composition draws entries in declared order
/// — a contested (overlapping) decision goes to the earlier entry (producer
/// contract) — so the rows carry explicit ↑/↓ reorder buttons and a reorder
/// alone is a real, persisted edit.
/// </para>
///
/// <para>
/// <b>The row count owns the percents.</b> Every change to the number of rows
/// — Add and Remove alike — re-derives <i>all</i> percents as an even split
/// totalling exactly 100 (<see cref="MixDraft.AddRowAsync"/> /
/// <see cref="MixDraft.RemoveRowAsync"/>), deliberately overwriting
/// hand-edited values: the panel demands a 100 total, so a structural edit
/// that left the old numbers standing simply handed the user arithmetic
/// (findings AH/AI). A new row also starts on the first kind no existing row
/// uses, so successive Adds walk <see cref="MixDraft.CategoryKinds"/> in
/// order instead of piling up duplicates. Both rules are Add/Remove-time
/// seeding only: once a row exists the user owns its kind and its percent,
/// and a duplicate kind chosen by hand is left to stand as the validation
/// error it is.
/// </para>
/// </summary>
public partial class MixPanel : ComponentBase
{
    /// <summary>
    /// Trigger the draft's once-per-setup hydration. Awaiting it here (rather
    /// than in the draft's constructor) keeps the JS read tied to the panel
    /// actually being offered — where the mix is not visible no panel mounts,
    /// the draft stays blank, and the mix plays no part in the start gate.
    /// </summary>
    protected override Task OnInitializedAsync() => Draft.EnsureHydratedAsync();

    private static bool KindTakesParameter(QuizCategoryKind kind) => kind is
        QuizCategoryKind.SeenFewerThan or QuizCategoryKind.NotSeenInDays or
        QuizCategoryKind.AvgEquityLossOver or QuizCategoryKind.WrongRateOver;

    private static string ParameterLabel(QuizCategoryKind kind) => kind switch
    {
        QuizCategoryKind.SeenFewerThan => "Times",
        QuizCategoryKind.NotSeenInDays => "Days",
        QuizCategoryKind.AvgEquityLossOver => "Equity loss",
        QuizCategoryKind.WrongRateOver => "Percent wrong",
        _ => string.Empty,
    };

    private static string ParameterUnit(QuizCategoryKind kind) => kind switch
    {
        QuizCategoryKind.SeenFewerThan => "times",
        QuizCategoryKind.NotSeenInDays => "days",
        QuizCategoryKind.AvgEquityLossOver => "equity",
        QuizCategoryKind.WrongRateOver => "% of the time",
        _ => string.Empty,
    };

    /// <summary>
    /// The read half of the category <c>&lt;select&gt;</c>, defined as the
    /// inverse of its write half: the options are rendered
    /// <c>value="@kind"</c> over <see cref="MixDraft.CategoryKinds"/>, so a
    /// token becomes a kind by searching that same offered list for the one
    /// whose name it is — the shape <see cref="QuizSettings.LevelFromToken"/>
    /// already uses for the hide-depth token.
    ///
    /// <para>Searching rather than parsing is what closes the ordinal hole:
    /// <see cref="Enum.TryParse{TEnum}(string, out TEnum)"/> also accepts a
    /// number, and this reader takes a value the browser posts, which the app
    /// does not control. Measured before the change — <c>"5"</c> selected
    /// <see cref="QuizCategoryKind.AvgEquityLossOver"/>, coupling a user-facing
    /// control to member numbering (halheinrich/backgammon#164). Searching the
    /// offered list also rejects a kind the picker never presented, which a
    /// parse plus a membership test would take two steps to say.</para>
    ///
    /// <para>An unrecognized token is ignored, unchanged from the parse
    /// spelling: a value the select never offered is not a gesture to honour,
    /// and there is no user to show an error to.</para>
    /// </summary>
    private Task HandleKindChangedAsync(int index, ChangeEventArgs e)
    {
        var token = e.Value?.ToString();

        foreach (var kind in MixDraft.CategoryKinds)
        {
            if (string.Equals(token, kind.ToString(), StringComparison.Ordinal))
            {
                return Draft.SetKindAsync(index, kind);
            }
        }

        return Task.CompletedTask;
    }

}
