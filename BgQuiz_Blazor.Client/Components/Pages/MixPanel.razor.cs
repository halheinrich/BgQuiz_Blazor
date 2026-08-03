using BgGame_Lib;
using BgQuiz_Blazor.Client.Quiz;
using Microsoft.AspNetCore.Components;

namespace BgQuiz_Blazor.Client.Components.Pages;

/// <summary>
/// The stats-weighted mix builder hosted on <c>Home</c> — the
/// <c>FilterPanel</c> of quiz composition, and a <b>view</b> over the
/// app-scoped <see cref="MixDraft"/>: every gesture routes through the
/// draft's mutators, and the markup renders the draft's rows, toggle, length,
/// and validation. The panel holds no edit state of its own, so mix edits
/// survive in-app navigation with the draft (ratified product behavior) and
/// the start gate can be derived from state that shares the draft's lifetime
/// — no dirty events, no hydration reconcile (both dissolved with the stored
/// <c>AppliedMix.IsDirty</c> flag this architecture replaced).
///
/// <para>
/// <b>Commit model mirrors <c>FilterPanel</c>.</b>
/// <see cref="OnMixApplied"/> fires on Apply, on Reset, and when the last row
/// is removed (both Reset and the last-row removal are an explicit apply of
/// <see cref="QuizMix.Empty"/>, distinct from the never-silently-rewrite rule
/// because the user asked for the blank state) — never per keystroke. Commits
/// persist through <see cref="MixDraft.PersistAsync"/> (committed-only
/// persistence) before the parent adopts the mix into <c>AppliedMix</c>.
/// Edits raise no event at all: the draft notifies its own subscribers
/// (<see cref="MixDraft.Changed"/>), and the gate re-derives from
/// draft-vs-committed content equality.
/// </para>
///
/// <para>
/// <b>Hydration is the draft's, triggered here.</b> Init awaits the
/// idempotent <see cref="MixDraft.EnsureHydratedAsync"/>: the first mount of
/// a setup loads the stored mix into the draft (re-offering it, gated by the
/// derived rule, until the user Applies or Resets); a re-mount after in-app
/// navigation finds the draft already hydrated and shows it as-is — edits
/// included.
/// </para>
///
/// <para>
/// <b>Row order is semantic.</b> Composition draws entries in declared order
/// — a contested (overlapping) decision goes to the earlier entry (producer
/// contract) — so the rows carry explicit ↑/↓ reorder buttons, the commit
/// preserves order exactly, and a reorder alone derives dirty.
/// </para>
///
/// <para>
/// <b>The row count owns the percents.</b> Every change to the number of rows
/// — Add and Remove alike — re-derives <i>all</i> percents as an even split
/// totalling exactly 100 (<see cref="MixDraft.AddRow"/> /
/// <see cref="MixDraft.RemoveRow"/>), deliberately overwriting hand-edited
/// values: the panel demands a 100 total, so a structural edit that left the
/// old numbers standing simply handed the user arithmetic (findings AH/AI).
/// Because the split always lands on 100, the "must reach 100%" error can
/// never appear as a <i>consequence</i> of Add/Remove — only of a subsequent
/// hand edit, which is the one case where it is informative. A new row also
/// starts on the first kind no existing row uses, so successive Adds walk
/// <see cref="MixDraft.CategoryKinds"/> in order instead of piling up
/// duplicates. Both rules are Add/Remove-time seeding only: once a row exists
/// the user owns its kind and its percent, and a duplicate kind chosen by
/// hand is left to stand as the validation error it is.
/// </para>
/// </summary>
public partial class MixPanel : ComponentBase
{
    /// <summary>
    /// Raised on <b>Apply</b>, on <b>Reset</b>, and when the <b>last row is
    /// removed</b> (which returns the builder to the blank passthrough state) —
    /// never per keystroke — carrying the committed <see cref="QuizMix"/>.
    /// Required: the panel exists to produce this, so a missing binding is an
    /// <c>RZ2012</c> compile error rather than a silent Razor splat.
    /// </summary>
    [Parameter, EditorRequired] public EventCallback<QuizMix> OnMixApplied { get; set; }

    /// <summary>
    /// Gates the <b>Apply Mix</b> gesture only — the host's sequencing switch,
    /// mirroring <c>SavedFiltersPanel.CanPersist</c>. <c>Home</c> binds it to
    /// "a filter has been applied for the currently picked folder": the mix
    /// draws from the filtered pool, so composing one before any filter exists
    /// is premature (issue #45). The panel is <i>told</i>, never asks — it holds
    /// no notion of filters, and none of the mix's own state
    /// (<see cref="MixDraft"/>, <c>AppliedMix</c>) takes part in the gate, which
    /// is what keeps this from re-creating the (AK) lifetime split.
    ///
    /// <para>
    /// <b>Reset and the blank path stay enabled regardless.</b> Returning to the
    /// passthrough mix — explicit Reset, or removing the last row — is how a
    /// user escapes a dirty draft that is gating Start, and a hydrated stored
    /// mix arrives dirty <i>before</i> any filter is applied. Gating that too
    /// would wedge the page. Only the forward commit is sequenced.
    /// </para>
    ///
    /// <para>
    /// Defaults to <see langword="true"/>, like its precedent, so a host that
    /// doesn't sequence its panels simply omits it. <see cref="ApplyAsync"/>
    /// early-returns as well, so the contract holds even for programmatic event
    /// dispatch that ignores the <c>disabled</c> attribute.
    /// </para>
    /// </summary>
    [Parameter] public bool CanApply { get; set; } = true;

    /// <summary>
    /// Optional host-supplied explanation shown while <see cref="CanApply"/> is
    /// <see langword="false"/> — as the disabled button's <c>title</c> and as a
    /// muted hint line beneath it. Ignored while Apply is enabled. The sentence
    /// belongs to the host because the <i>rule</i> does: this panel knows
    /// nothing of what it is being sequenced behind.
    /// </summary>
    [Parameter] public string? ApplyDisabledReason { get; set; }

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

    private void HandleKindChanged(int index, ChangeEventArgs e)
    {
        if (!Enum.TryParse<QuizCategoryKind>(e.Value?.ToString(), out var kind)) return;
        Draft.SetKind(index, kind);
    }

    private Task RemoveRowAsync(int index)
    {
        Draft.RemoveRow(index);
        // Removing the last row returns the draft to its blank (passthrough)
        // state. Apply is disabled at zero rows (committing Empty is the blank
        // path's job, not Apply's), so leaving this a mere edit would gate
        // Start with no in-panel commit but Reset — the pre-beta wedge. Auto-
        // commit the blank mix through the same channel Reset uses, so the
        // holder, the gate, and localStorage all land where Reset would put them.
        return Draft.Rows.Count == 0 ? GoBlankAsync() : Task.CompletedTask;
    }

    /// <summary>The disabled button's tooltip — the host's reason, or nothing while Apply is enabled.</summary>
    private string? ApplyDisabledTitle => CanApply ? null : ApplyDisabledReason;

    private Task ApplyAsync()
    {
        // Both backstops behind a disabled button, for the same reason: a
        // programmatic dispatch ignores `disabled`, and neither the host's
        // sequencing gate nor the draft's validity may be bypassed that way.
        if (!CanApply) return Task.CompletedTask;
        if (Draft.Build() is not { } mix) return Task.CompletedTask;
        return CommitAsync(mix);
    }

    private Task ResetAsync() => GoBlankAsync();

    /// <summary>
    /// Normalize to the blank draft and commit <see cref="QuizMix.Empty"/> —
    /// the shared path for the explicit Reset gesture and for removing the
    /// last row, which lands in the same state. Both persist Empty
    /// (localStorage stays consistent) and raise <see cref="OnMixApplied"/>,
    /// the sanctioned way this panel writes Empty over a stored mix.
    /// <see cref="MixDraft.Clear"/> also resets the toggle and length to their
    /// blank-builder defaults so "zero rows" means one state regardless of how
    /// it was reached.
    /// </summary>
    private Task GoBlankAsync()
    {
        Draft.Clear();
        return CommitAsync(QuizMix.Empty);
    }

    private async Task CommitAsync(QuizMix mix)
    {
        await Draft.PersistAsync(mix);
        await OnMixApplied.InvokeAsync(mix);
    }
}
