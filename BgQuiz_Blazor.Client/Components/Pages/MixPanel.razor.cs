using System.Globalization;
using BgGame_Lib;
using BgQuiz_Blazor.Client.Quiz;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace BgQuiz_Blazor.Client.Components.Pages;

/// <summary>
/// The stats-weighted mix builder hosted on <c>Home</c> — the
/// <c>FilterPanel</c> of quiz composition. Owns all mix edit state (ordered
/// category rows, the Random-order toggle, the optional quiz length) and
/// commits it through the Apply gesture as a validated
/// <see cref="QuizMix"/>.
///
/// <para>
/// <b>Commit model mirrors <c>FilterPanel</c>.</b>
/// <see cref="OnMixApplied"/> fires on Apply, on Reset, and when the last row
/// is removed (both Reset and the last-row removal are an explicit apply of
/// <see cref="QuizMix.Empty"/>, distinct from the never-silently-rewrite rule
/// because the user asked for the blank state); <see cref="OnMixDirty"/> fires
/// on every other control change so the parent can gate Start until the edit
/// is committed. The first-render localStorage restore hydrates the panel for
/// the user's convenience but does <b>not</b> commit — it raises
/// <see cref="OnMixRestored"/> so the parent <i>reconciles</i>: a
/// non-passthrough restore gates Start (dirty) on a fresh load, but is left
/// untouched when a committed mix already survives in the holder
/// (navigate-back), so the user isn't forced to re-Apply. This is deliberately
/// <i>not</i> the earlier adopt-on-restore, which silently committed the stored
/// mix (removed in finding W).
/// </para>
///
/// <para>
/// <b>Row order is semantic.</b> Composition draws entries in declared order
/// — a contested (overlapping) decision goes to the earlier entry (producer
/// contract) — so the rows carry explicit ↑/↓ reorder buttons and both the
/// commit and the restore preserve order exactly.
/// </para>
///
/// <para>
/// <b>The row count owns the percents.</b> Every change to the number of rows
/// — Add and Remove alike — re-derives <i>all</i> percents as an even split
/// totalling exactly 100 (<see cref="RebalancePercentsEvenly"/>), deliberately
/// overwriting hand-edited values: the panel demands a 100 total, so a
/// structural edit that left the old numbers standing simply handed the user
/// arithmetic (findings AH/AI). Because the split always lands on 100, the
/// "must reach 100%" error can never appear as a <i>consequence</i> of
/// Add/Remove — only of a subsequent hand edit, which is the one case where it
/// is informative. A new row also starts on the first kind no existing row uses
/// (<see cref="NextUnusedKind"/>), so successive Adds walk
/// <see cref="CategoryKinds"/> in order instead of piling up duplicates. Both
/// rules are Add/Remove-time seeding only: once a row exists the user owns its
/// kind and its percent, and a duplicate kind chosen by hand is left to stand
/// as the validation error it is.
/// </para>
///
/// <para>
/// <b>Persistence</b> is the <c>FilterPanel</c> trio over one localStorage
/// key (<see cref="MixKey"/>): <see cref="QuizMix.ToJson"/> on Apply,
/// <see cref="QuizMix.TryFromJson"/> on restore — absent or corrupt yields
/// the blank builder, never an error. The lib owns the JSON shape; this
/// component never touches a serializer. Thresholds follow the producer's
/// fraction contract: the wrong-rate row <i>displays</i> percent and
/// <i>stores</i> the fraction (percentage rendering stays a display
/// concern).
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
    /// Raised once after the first-render localStorage restore succeeds, carrying
    /// the restored mix so the parent can <b>reconcile</b> it against the
    /// committed-mix holder — <i>not</i> adopt it (the adopt semantics were
    /// removed in finding W). The parent gates Start (marks the mix dirty) only
    /// when the restore is non-passthrough <i>and</i> the holder is still at its
    /// passthrough default (a fresh load); when the holder already holds a
    /// committed mix (navigate-back, the Scoped holder surviving), the restore is
    /// left untouched so the user needn't re-Apply. Required: without the binding
    /// a fresh-load restore wouldn't gate, and a non-passthrough mix would render
    /// in the panel while Start silently ran passthrough — the exact divergence
    /// the dirty machinery exists to prevent.
    /// </summary>
    [Parameter, EditorRequired] public EventCallback<QuizMix> OnMixRestored { get; set; }

    /// <summary>
    /// Raised on every input change (row edit, add/remove/reorder, toggle,
    /// length) so the parent can gate Start until the user commits via Apply.
    /// Optional by design, like the filter panel's dirty callback. (The restore
    /// path signals through <see cref="OnMixRestored"/>, not this — it reconciles
    /// against the holder rather than unconditionally dirtying.)
    /// </summary>
    [Parameter] public EventCallback OnMixDirty { get; set; }

    // Single localStorage key holding the whole mix as one serialized QuizMix
    // blob. The lib owns the JSON shape (ToJson / TryFromJson); camelCase
    // after the xg_ prefix per the existing key family.
    internal const string MixKey = "xg_quizMix";

    /// <summary>
    /// One editable mix row. Free-text buffers (not parsed values) because
    /// in-progress typing needs a string distinct from the committed value —
    /// hydrated from a restored mix, flushed into a <see cref="QuizMixEntry"/>
    /// on Apply, never persisted on their own (the <c>FilterPanel</c> buffer
    /// pattern). <see cref="Kind"/> is <c>required</c>: the enum starts at 1, so
    /// there is no meaningful default, and each of the two construction sites
    /// (Add, restore-hydrate) chooses the kind deliberately.
    /// </summary>
    private sealed class MixRow
    {
        public required QuizCategoryKind Kind { get; set; }
        public string ParamText { get; set; } = string.Empty;
        public string PercentText { get; set; } = string.Empty;
    }

    private readonly List<MixRow> _rows = [];
    private bool _randomOrder = true;
    private string _lengthText = string.Empty;

    /// <summary>Every selectable category kind, in the picker's display order.</summary>
    private static readonly QuizCategoryKind[] CategoryKinds =
    [
        QuizCategoryKind.NeverSeen,
        QuizCategoryKind.GotWrong,
        QuizCategoryKind.SeenFewerThan,
        QuizCategoryKind.NotSeenInDays,
        QuizCategoryKind.AvgEquityLossOver,
        QuizCategoryKind.WrongRateOver,
        QuizCategoryKind.EverythingElse,
    ];

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
    /// The sensible starting parameter a kind gets when selected — a valid,
    /// editable value so a fresh row is never born invalid. The wrong-rate
    /// default is the display percent (25 ⇒ fraction 0.25 on Apply).
    /// </summary>
    private static string DefaultParamText(QuizCategoryKind kind) => kind switch
    {
        QuizCategoryKind.SeenFewerThan => "3",
        QuizCategoryKind.NotSeenInDays => "30",
        QuizCategoryKind.AvgEquityLossOver => "0.05",
        QuizCategoryKind.WrongRateOver => "25",
        _ => string.Empty,
    };

    private int PercentSum =>
        _rows.Sum(r => int.TryParse(r.PercentText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var p) ? p : 0);

    /// <summary>
    /// The first problem with the current edit state, or null when the state
    /// would commit cleanly. Recomputed per render; Apply is disabled while
    /// non-null, so the (still-present) construction-time try/catch in
    /// <see cref="ApplyAsync"/> is a backstop, not the primary validation.
    /// A blank builder (zero rows) reports no error — it <i>would</i> build the
    /// inert <see cref="QuizMix.Empty"/> — but Apply is separately disabled at
    /// zero rows: committing the blank mix is the blank path's job
    /// (<see cref="GoBlankAsync"/>, shared by Reset and the last-row removal),
    /// so Apply requires at least one row.
    /// </summary>
    private string? ValidationError
    {
        get
        {
            if (_rows.Count == 0) return null;

            var categories = new HashSet<QuizCategory>();
            foreach (var row in _rows)
            {
                if (!TryBuildCategory(row, out var category, out var error))
                    return error;
                if (!categories.Add(category!))
                    return $"Duplicate category: {MixDisplay.KindLabel(row.Kind)} with the same value.";
                if (!int.TryParse(row.PercentText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var percent)
                    || percent is < 1 or > 100)
                    return $"{MixDisplay.KindLabel(row.Kind)}: percent must be a whole number from 1 to 100.";
            }

            if (PercentSum != 100)
                return "Percents must sum to exactly 100.";

            if (_lengthText.Trim().Length > 0
                && (!int.TryParse(_lengthText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var length)
                    || length < 1))
                return "Quiz length must be a whole number of at least 1 (or blank).";

            return null;
        }
    }

    /// <summary>
    /// Build the row's <see cref="QuizCategory"/> through the producer's
    /// validating factories — the one kind→factory mapping in the app. The
    /// wrong-rate row converts its display percent to the stored fraction
    /// here; all parsing is invariant.
    /// </summary>
    private static bool TryBuildCategory(MixRow row, out QuizCategory? category, out string? error)
    {
        category = null;
        error = null;
        try
        {
            switch (row.Kind)
            {
                case QuizCategoryKind.NeverSeen:
                    category = QuizCategory.NeverSeen;
                    return true;
                case QuizCategoryKind.GotWrong:
                    category = QuizCategory.GotWrong;
                    return true;
                case QuizCategoryKind.EverythingElse:
                    category = QuizCategory.EverythingElse;
                    return true;
                case QuizCategoryKind.SeenFewerThan:
                    if (!TryParseInt(row.ParamText, out var times) || times < 1)
                    {
                        error = "Seen fewer than…: times must be a whole number of at least 1.";
                        return false;
                    }
                    category = QuizCategory.SeenFewerThan(times);
                    return true;
                case QuizCategoryKind.NotSeenInDays:
                    if (!TryParseInt(row.ParamText, out var days) || days < 1)
                    {
                        error = "Not seen in…: days must be a whole number of at least 1.";
                        return false;
                    }
                    category = QuizCategory.NotSeenInDays(days);
                    return true;
                case QuizCategoryKind.AvgEquityLossOver:
                    if (!TryParseDouble(row.ParamText, out var loss) || loss < 0.0)
                    {
                        error = "Avg equity loss over…: the threshold must be a number of at least 0.";
                        return false;
                    }
                    category = QuizCategory.AvgEquityLossOver(loss);
                    return true;
                case QuizCategoryKind.WrongRateOver:
                    // Displayed as percent, stored as the producer's fraction.
                    if (!TryParseDouble(row.ParamText, out var percentWrong)
                        || percentWrong is < 0.0 or >= 100.0)
                    {
                        error = "Wrong more than…: the rate must be a percent from 0 to below 100.";
                        return false;
                    }
                    category = QuizCategory.WrongRateOver(percentWrong / 100.0);
                    return true;
                default:
                    error = $"Unknown category kind {row.Kind}.";
                    return false;
            }
        }
        catch (ArgumentException ex)
        {
            // Backstop: the factory rejected a value the checks above let
            // through — surface its message rather than faulting the app.
            error = ex.Message;
            return false;
        }
    }

    private static bool TryParseInt(string text, out int value) =>
        int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);

    private static bool TryParseDouble(string text, out double value) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
        && double.IsFinite(value);

    /// <summary>
    /// First-render restore: read the persisted mix and hydrate the builder
    /// through <see cref="QuizMix.TryFromJson"/> — absent or corrupt leaves it
    /// blank, never an error — then raise <see cref="OnMixRestored"/> so the
    /// parent reconciles the restored mix against the committed-mix holder (gate
    /// on a fresh load, leave a surviving committed mix untouched). The restore
    /// never commits or dirties here.
    /// </summary>
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender) return;

        var stored = await JS.InvokeAsync<string?>("localStorage.getItem", MixKey);
        // Tolerant restore: a missing key, the literal null token, or corrupt
        // JSON all leave the builder blank — no try/catch here. A successful
        // parse hydrates the panel for convenience but does NOT commit: it raises
        // OnMixRestored so the parent reconciles against the holder (dirty on a
        // fresh load; untouched when a committed mix survives navigate-back). The
        // panel neither adopts nor dirties on restore itself.
        if (QuizMix.TryFromJson(stored, out var mix))
        {
            HydrateFrom(mix);
            await OnMixRestored.InvokeAsync(mix);
        }

        StateHasChanged();
    }

    private void MarkDirty() => OnMixDirty.InvokeAsync();

    /// <summary>
    /// The kind a newly added row starts on: the first entry of
    /// <see cref="CategoryKinds"/> no existing row already uses, so successive
    /// Adds walk the picker's display order (finding AI) rather than stacking
    /// duplicate <c>NeverSeen</c> rows the user must then re-pick one by one.
    /// The list is ordered from most- to least-specific and ends in
    /// <c>EverythingElse</c>, which suits it as the last one offered. When every
    /// kind is in use, fall back to the first: further rows can only be
    /// duplicates whatever we pick, and the existing "Duplicate category" error
    /// says so plainly.
    /// </summary>
    private QuizCategoryKind NextUnusedKind() =>
        CategoryKinds.FirstOrDefault(
            kind => !_rows.Any(row => row.Kind == kind), CategoryKinds[0]);

    /// <summary>
    /// Re-derive every row's percent as an even split summing to <b>exactly</b>
    /// 100 (finding AH). Rounding policy: each row takes the floor share, and
    /// the leftover units are handed out one apiece from the top, so the total
    /// is 100 by construction and the earlier — contested-overlap-winning — rows
    /// carry the extra unit. Deliberately overwrites hand-edited percents; the
    /// gesture that changes the row count is a restructuring of the mix, and the
    /// alternative is leaving the user to redo the arithmetic the panel already
    /// knows. Above 100 rows the floor share is 0 and the per-row 1–100 check
    /// reports it: that state is genuinely uncommittable, not a rounding bug.
    /// Both call sites guarantee at least one row — Add has just appended, and
    /// Remove hands zero rows to the blank path before reaching here.
    /// </summary>
    private void RebalancePercentsEvenly()
    {
        var share = 100 / _rows.Count;
        var remainder = 100 % _rows.Count;
        for (var i = 0; i < _rows.Count; i++)
            _rows[i].PercentText = (share + (i < remainder ? 1 : 0))
                .ToString(CultureInfo.InvariantCulture);
    }

    private void AddRow()
    {
        // A fresh row lands valid and distinct: the next unused kind, seeded
        // with that kind's default parameter (a row is never born invalid), and
        // then every row's percent re-derived to an even 100 total.
        var kind = NextUnusedKind();
        _rows.Add(new MixRow { Kind = kind, ParamText = DefaultParamText(kind) });
        RebalancePercentsEvenly();
        // One dirty per gesture, however many rows the rebalance touched.
        MarkDirty();
    }

    private Task RemoveRow(int index)
    {
        _rows.RemoveAt(index);
        // Removing the last row returns the builder to its blank (passthrough)
        // state. Apply is disabled at zero rows (committing Empty is the blank
        // path's job, not Apply's), so leaving this a mere dirty edit would
        // strand AppliedMix dirty with no in-panel way to commit — Start wedged
        // with only Reset as a non-obvious escape (the reported bug). Auto-
        // commit the blank mix through the same channel Reset uses, so
        // AppliedMix un-dirties, Start un-gates, and localStorage matches Reset.
        if (_rows.Count == 0) return GoBlankAsync();
        // Symmetric with Add: the surviving rows split 100 evenly, so a removal
        // never strands the panel showing "must reach 100%" for percent the user
        // did not choose to give away.
        RebalancePercentsEvenly();
        MarkDirty();
        return Task.CompletedTask;
    }

    /// <summary>Move the row at <paramref name="index"/> by <paramref name="delta"/> (±1) — order is semantic, so reordering is a real edit.</summary>
    private void MoveRow(int index, int delta)
    {
        var target = index + delta;
        if (target < 0 || target >= _rows.Count) return;
        (_rows[index], _rows[target]) = (_rows[target], _rows[index]);
        MarkDirty();
    }

    private void HandleKindChanged(MixRow row, ChangeEventArgs e)
    {
        if (!Enum.TryParse<QuizCategoryKind>(e.Value?.ToString(), out var kind)) return;
        row.Kind = kind;
        // Selecting a kind seeds its sensible default parameter so the row is
        // immediately valid; the user edits from there.
        row.ParamText = DefaultParamText(kind);
        MarkDirty();
    }

    private Task ApplyAsync()
    {
        if (BuildMix() is not { } mix) return Task.CompletedTask; // backstop; Apply is disabled while invalid
        return PersistAndRaiseAsync(mix);
    }

    private Task ResetAsync() => GoBlankAsync();

    /// <summary>
    /// Normalize to the blank (passthrough) builder and commit
    /// <see cref="QuizMix.Empty"/> — the shared path for the explicit Reset
    /// gesture and for removing the last row, which lands in the same state.
    /// Both persist Empty (localStorage stays consistent) and raise
    /// <see cref="OnMixApplied"/>, the sanctioned way this panel writes Empty
    /// over a stored mix. The toggle and length are reset to their blank-
    /// builder defaults so "zero rows" means one state regardless of how it was
    /// reached; both controls are disabled at zero rows, and Empty carries
    /// neither, so the reset only affects a subsequently re-added row.
    /// </summary>
    private Task GoBlankAsync()
    {
        _rows.Clear();
        _randomOrder = true;
        _lengthText = string.Empty;
        return PersistAndRaiseAsync(QuizMix.Empty);
    }

    private async Task PersistAndRaiseAsync(QuizMix mix)
    {
        await JS.InvokeVoidAsync("localStorage.setItem", MixKey, mix.ToJson());
        await OnMixApplied.InvokeAsync(mix);
    }

    /// <summary>
    /// Flush the rows / toggle / length into a validated <see cref="QuizMix"/>,
    /// preserving row order (order is contractual). Null when the current
    /// state doesn't build — unreachable through the UI, where Apply is
    /// disabled while <see cref="ValidationError"/> is non-null.
    /// </summary>
    private QuizMix? BuildMix()
    {
        try
        {
            var entries = new List<QuizMixEntry>(_rows.Count);
            foreach (var row in _rows)
            {
                if (!TryBuildCategory(row, out var category, out _)) return null;
                if (!int.TryParse(row.PercentText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var percent))
                    return null;
                entries.Add(new QuizMixEntry(category!, percent));
            }

            int? length = null;
            if (entries.Count > 0 && _lengthText.Trim().Length > 0)
            {
                if (!int.TryParse(_lengthText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                    return null;
                length = parsed;
            }

            return entries.Count == 0
                ? QuizMix.Empty
                : new QuizMix(entries, length, _randomOrder);
        }
        catch (ArgumentException)
        {
            return null; // set-level rule the per-row checks missed — Apply stays a no-op
        }
    }

    /// <summary>
    /// Inverse of <see cref="BuildMix"/>: project a restored mix onto the
    /// edit state, in wire order. The wrong-rate fraction renders back as its
    /// display percent; integer-kind parameters render without decimals.
    /// </summary>
    private void HydrateFrom(QuizMix mix)
    {
        _rows.Clear();
        foreach (var entry in mix.Entries)
        {
            _rows.Add(new MixRow
            {
                Kind = entry.Category.Kind,
                ParamText = ParamTextFor(entry.Category),
                PercentText = entry.Percent.ToString(CultureInfo.InvariantCulture),
            });
        }
        _randomOrder = mix.RandomOrder;
        _lengthText = mix.QuizLength?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
    }

    private static string ParamTextFor(QuizCategory category) => category.Kind switch
    {
        QuizCategoryKind.SeenFewerThan or QuizCategoryKind.NotSeenInDays =>
            ((int)category.Value!.Value).ToString(CultureInfo.InvariantCulture),
        QuizCategoryKind.AvgEquityLossOver =>
            category.Value!.Value.ToString("0.###", CultureInfo.InvariantCulture),
        QuizCategoryKind.WrongRateOver =>
            (category.Value!.Value * 100.0).ToString("0.##", CultureInfo.InvariantCulture),
        _ => string.Empty,
    };
}
