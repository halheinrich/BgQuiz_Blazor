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
/// is committed. One deliberate divergence: the first-render localStorage
/// restore raises <see cref="OnMixRestored"/> so the parent's holder adopts
/// the restored mix — a persisted mix is by construction a previously-applied
/// one, unlike the filter panel, whose restore deliberately raises nothing
/// (its "applied" means a gesture in <i>this</i> visit).
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
    /// Raised once after the first-render localStorage restore succeeds,
    /// carrying the restored (previously-applied) mix so the parent's holder
    /// adopts it. Required: without the binding a restored mix would render
    /// in the panel while Start silently used a blank one — the exact
    /// divergence the applied-state machinery exists to prevent.
    /// </summary>
    [Parameter, EditorRequired] public EventCallback<QuizMix> OnMixRestored { get; set; }

    /// <summary>
    /// Raised on every input change (row edit, add/remove/reorder, toggle,
    /// length) so the parent can gate Start until the user commits via Apply.
    /// Optional by design, like the filter panel's dirty callback.
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
    /// pattern).
    /// </summary>
    private sealed class MixRow
    {
        public QuizCategoryKind Kind { get; set; } = QuizCategoryKind.NeverSeen;
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
    /// through <see cref="QuizMix.TryFromJson"/> — absent or corrupt leaves
    /// it blank, never an error — then let the parent adopt the restored mix
    /// via <see cref="OnMixRestored"/>.
    /// </summary>
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender) return;

        var stored = await JS.InvokeAsync<string?>("localStorage.getItem", MixKey);
        // Tolerant restore: a missing key, the literal null token, or corrupt
        // JSON all leave the builder blank — no try/catch here. Only a
        // successful parse hydrates and is adopted by the parent (a persisted
        // mix is by construction a previously-applied one).
        if (QuizMix.TryFromJson(stored, out var mix))
        {
            HydrateFrom(mix);
            await OnMixRestored.InvokeAsync(mix);
        }

        StateHasChanged();
    }

    private void MarkDirty() => OnMixDirty.InvokeAsync();

    private void AddRow()
    {
        // A fresh row lands valid: default kind takes no parameter, and its
        // percent defaults to whatever completes the sum (at least 1).
        _rows.Add(new MixRow
        {
            PercentText = Math.Clamp(100 - PercentSum, 1, 100)
                .ToString(CultureInfo.InvariantCulture),
        });
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
