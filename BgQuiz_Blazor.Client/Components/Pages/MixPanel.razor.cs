using BgGame_Lib;
using BgQuiz_Blazor.Client.Quiz;
using Microsoft.AspNetCore.Components;

namespace BgQuiz_Blazor.Client.Components.Pages;

/// <summary>
/// The stats-weighted mix builder hosted on <c>Home</c> — a <b>view</b> over
/// the app-scoped <see cref="MixDraft"/> and <see cref="MixConsent"/>: every
/// gesture routes through the draft's mutators or the consent bit, and the
/// markup renders the draft's rows, toggle, length, and validation. The panel
/// holds no state of its own, so mix edits and the activation bit survive
/// in-app navigation with their services (ratified product behavior), and
/// everything Start derives from shares one lifetime.
///
/// <para>
/// <b>Activation is the "Mix applies" checkbox — the sole control</b>
/// (<c>SPEC-filtering.md</c> §5, Fork B; it replaced the Apply Mix button
/// outright). Checked means the on-screen mix is in effect: there is no
/// committed copy, no commit gesture, and no event to the host — the host
/// observes <see cref="MixConsent.Changed"/> / <see cref="MixDraft.Changed"/>
/// like any other state-container subscriber. The check gesture is gated by
/// <see cref="CanActivate"/> (the host's Fork A fact: the filter is in effect
/// <i>now</i>); <b>unchecking is always live</b> — the box is disabled only
/// while unchecked-and-gated, so consent can always be withdrawn, which is
/// what keeps a checked-but-invalid mix (Start gated, hint says
/// fix-or-uncheck) from ever wedging.
/// </para>
///
/// <para>
/// <b>Persistence is the draft's, per edit.</b> Every mutator writes the
/// built mix through while the draft validates (blank included), so storage
/// follows the screen with no commit moment; this panel never touches a
/// serializer or localStorage. <i>Clear mix</i> is
/// <see cref="MixDraft.ClearAsync"/> whole: blank the builder and persist the
/// blank mix — deliberate row removal, its one honest job. It never touches
/// the checkbox (checked over blank is vacuous, in-effect passthrough — the
/// app flips the bit in neither direction).
/// </para>
///
/// <para>
/// <b>Hydration is the draft's, triggered here.</b> Init awaits the
/// idempotent <see cref="MixDraft.EnsureHydratedAsync"/>: the first mount of
/// a setup loads the stored last-valid mix into the draft — visible but inert
/// until the user checks the box; a re-mount after in-app navigation finds
/// the draft already hydrated and shows it as-is, edits included.
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
    /// Gates the <b>check</b> gesture of "Mix applies" — the host's Fork A
    /// fact ("the filter is in effect right now", the same fact Start reads),
    /// told to this panel because the panel knows nothing of filters. Gates
    /// checking only: while the box is already checked it renders enabled
    /// regardless, so unchecking — the universal way out — is never taken
    /// away, and the app never unchecks on the user's behalf.
    ///
    /// <para>
    /// Defaults to <see langword="true"/> (a host that doesn't sequence its
    /// panels gets an always-checkable box), but is <c>[EditorRequired]</c>
    /// all the same: with the Apply event gone this component has no other
    /// required binding, and a future host mounting it bare would otherwise
    /// compile silently into an ungated activation control. The
    /// <see cref="HandleAppliesChanged"/> backstop enforces the gate even for
    /// programmatic event dispatch that ignores the <c>disabled</c> attribute.
    /// </para>
    /// </summary>
    [Parameter, EditorRequired] public bool CanActivate { get; set; } = true;

    /// <summary>
    /// Host-supplied explanation shown while the check gesture is gated
    /// (<see cref="CanActivate"/> false and the box unchecked) — as the
    /// disabled checkbox's <c>title</c> and as a muted hint line beneath the
    /// controls. Ignored otherwise. The sentence belongs to the host because
    /// the <i>rule</i> does: this panel knows nothing of what it is being
    /// sequenced behind. <c>[EditorRequired]</c> beside its gate for the same
    /// reason the gate is: a host that sequences must also say why.
    /// </summary>
    [Parameter, EditorRequired] public string? ActivateDisabledReason { get; set; }

    /// <summary>
    /// Trigger the draft's once-per-setup hydration. Awaiting it here (rather
    /// than in the draft's constructor) keeps the JS read tied to the panel
    /// actually being offered — under a no-stats pick no panel mounts, the
    /// draft stays blank, and the mix plays no part in the start gate.
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

    /// <summary>The gated checkbox's tooltip — the host's reason, or nothing while checking is available (or the box is checked).</summary>
    private string? ActivateDisabledTitle =>
        !CanActivate && !Consent.Applies ? ActivateDisabledReason : null;

    /// <summary>
    /// The "Mix applies" gesture. Asymmetric by design: unchecking always
    /// lands (consent can always be withdrawn), while a <i>check</i> arriving
    /// past the gate — programmatic dispatch ignores <c>disabled</c> — is
    /// dropped, mirroring the old Apply backstop. No other logic: effect is
    /// derived by the host from the bit and the draft, never computed here.
    /// </summary>
    private void HandleAppliesChanged(ChangeEventArgs e)
    {
        var requested = e.Value is true;
        if (requested && !CanActivate) return;
        Consent.Set(requested);
    }
}
