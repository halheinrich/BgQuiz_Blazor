namespace BgQuiz_Blazor.Client.Quiz;

using System.Globalization;
using BgGame_Lib;
using Microsoft.JSInterop;

/// <summary>
/// The per-app (Scoped, one-per-tab in WASM) <b>mix draft</b>: the edit state
/// of the stats-weighted mix builder — the ordered category rows (kind /
/// parameter text / percent text), the Random-order toggle, and the optional
/// quiz length. <c>MixPanel</c> is a view over this service: it renders
/// <see cref="Rows"/> and routes every gesture through the mutators here, so
/// mix edits are deliberately <i>not</i> lost by visiting another page
/// (ratified product behavior; the draft and everything derived from it share
/// one app scope).
///
/// <para>
/// <b>There is no committed copy of the mix</b> (<c>SPEC-filtering.md</c> §5,
/// Fork B). What runs, when the app-scoped <see cref="MixConsent"/> bit is
/// checked, is this draft itself — <see cref="Build"/>'s result — so screen
/// and effect cannot diverge and no draft-vs-committed comparison exists to
/// gate anything. An un-consented draft, however divergent from whatever ran
/// last, never gates Start; a consented draft that fails to build is the one
/// mix state that does (<c>Home.EffectiveMix</c> reads null), with the box
/// left checked because it records intent.
/// </para>
///
/// <para>
/// <b>Persistence follows the screen — last-valid write-through.</b> This
/// service owns the one localStorage key (<see cref="StorageKey"/>) in both
/// directions. Every mutator, after mutating, persists the built
/// <see cref="QuizMix"/> <i>when the draft validates</i> (the blank draft
/// included — it builds <see cref="QuizMix.Empty"/>, so Clear persists
/// blank); a mutation that leaves the draft invalid skips the write, so
/// storage always holds the last well-formed screen state. Ruled consequence:
/// a reload mid-half-edit restores that last valid mix, not the torn edit.
/// The wire format is unchanged — one lib-owned <see cref="QuizMix"/> JSON
/// blob — so mixes stored by earlier builds load with no migration. Writes
/// are best-effort: a storage fault must never break editing (the same
/// degrade posture as hydration's tolerant read).
/// </para>
///
/// <para>
/// <b>A pick (or Clear) discards the draft — not the storage.</b>
/// <c>Home.EndCurrentSetupAsync</c> calls <see cref="Discard"/> beside
/// <c>MixConsent.Revoke</c>: ending a setup blanks the builder and forgets
/// hydration, so the next panel mount (stats-capable picks only) re-hydrates
/// the <i>stored</i> last-valid mix afresh — the rows outlive the setup
/// (§4: they are choice), visible but inert until the user re-checks the
/// box (consent died with the setup). Under a no-stats pick no panel mounts,
/// nothing re-hydrates, and the unchecked consent keeps the mix out of Start
/// with no capability fork in the gate.
/// </para>
///
/// <para>
/// Mutations raise <see cref="Changed"/> so <c>Home</c> can re-derive its
/// start gate (the state-container pattern); all callers run on Blazor's
/// single-threaded WASM sync context, so no marshalling is needed.
/// </para>
/// </summary>
internal sealed class MixDraft(IJSRuntime js)
{
    // Single localStorage key holding the last well-formed mix as one
    // serialized QuizMix blob. The lib owns the JSON shape (ToJson /
    // TryFromJson); camelCase after the xg_ prefix per the existing key family.
    // Deliberately the same key and format the committed-mix era wrote, so
    // stored mixes carry across the model change with no migration.
    internal const string StorageKey = "xg_quizMix";

    /// <summary>
    /// Raised after every draft mutation — edits, hydration, <see cref="ClearAsync"/>,
    /// <see cref="Discard"/> — so subscribers (Home) can re-derive state that
    /// depends on the draft. Subscribers must unsubscribe on dispose.
    /// </summary>
    public event Action? Changed;

    /// <summary>
    /// One editable mix row. Free-text buffers (not parsed values) because
    /// in-progress typing needs a string distinct from any parsed value —
    /// hydrated from a stored mix, flushed into a <see cref="QuizMixEntry"/>
    /// by <see cref="Build"/>, never persisted on their own (the
    /// <c>FilterPanel</c> buffer pattern; what persists is the built
    /// <see cref="QuizMix"/>, and only while the draft validates).
    /// <see cref="Kind"/> has no default:
    /// the enum starts at 1, and each construction site (Add, hydration)
    /// chooses the kind deliberately. Read-only outside this service: every
    /// write goes through a <see cref="MixDraft"/> mutator so
    /// <see cref="Changed"/> fires and derived state stays live.
    /// </summary>
    public sealed class Row
    {
        internal Row(QuizCategoryKind kind, string paramText, string percentText)
        {
            Kind = kind;
            ParamText = paramText;
            PercentText = percentText;
        }

        public QuizCategoryKind Kind { get; internal set; }
        public string ParamText { get; internal set; }
        public string PercentText { get; internal set; }
    }

    private readonly List<Row> _rows = [];

    /// <summary>The draft rows, in draw order (order is contractual).</summary>
    public IReadOnlyList<Row> Rows => _rows;

    /// <summary>The Random-order toggle; on is the blank builder's default.</summary>
    public bool RandomOrder { get; private set; } = true;

    /// <summary>The optional quiz-length buffer; blank means no cap.</summary>
    public string LengthText { get; private set; } = string.Empty;

    /// <summary>Every selectable category kind, in the picker's display order.</summary>
    public static IReadOnlyList<QuizCategoryKind> CategoryKinds { get; } =
    [
        QuizCategoryKind.NeverSeen,
        QuizCategoryKind.GotWrong,
        QuizCategoryKind.SeenFewerThan,
        QuizCategoryKind.NotSeenInDays,
        QuizCategoryKind.AvgEquityLossOver,
        QuizCategoryKind.WrongRateOver,
        QuizCategoryKind.EverythingElse,
    ];

    /// <summary>
    /// The completed (or in-flight) hydration for the current setup; null when
    /// no hydration has run since construction or the last <see cref="Discard"/>.
    /// Caching the task is what makes <see cref="EnsureHydratedAsync"/>
    /// idempotent per setup.
    /// </summary>
    private Task? _hydration;

    /// <summary>
    /// Bumped by <see cref="Discard"/> so a hydration still awaiting its
    /// localStorage read when the setup ends cannot land stale rows on the
    /// discarded draft — the same stale-async discipline as Home's count
    /// request id.
    /// </summary>
    private int _generation;

    /// <summary>
    /// Load the stored mix into the draft — once per setup. The first caller
    /// (the panel's init) runs the localStorage read; later calls (re-mounts
    /// after in-app navigation) return the cached task, leaving the surviving
    /// draft — edits included — untouched. A missing key, the literal null
    /// token, or corrupt JSON leaves the draft blank, never an error; only a
    /// <i>successful</i> parse projects (TryFromJson's Empty fallback is a
    /// usable mix, but projecting it would overwrite the blank draft's own
    /// defaults with Empty's). Hydration fills the draft only — it never
    /// writes storage and never touches <c>MixConsent</c>, so a restored mix
    /// arrives visible but inert until the user checks "Mix applies" (§5
    /// rule 3: activation is explicit, in <i>this</i> setup).
    /// </summary>
    public Task EnsureHydratedAsync() => _hydration ??= HydrateAsync();

    private async Task HydrateAsync()
    {
        var generation = _generation;
        var stored = await js.InvokeAsync<string?>("localStorage.getItem", StorageKey);
        if (generation != _generation) return; // setup ended mid-read — nothing to land on

        if (QuizMix.TryFromJson(stored, out var mix)) Project(mix);
        Changed?.Invoke();
    }

    /// <summary>
    /// The last-valid write-through behind every mutator: persist the built
    /// mix when the draft validates, keep the previous well-formed state when
    /// it doesn't (ruled — storage always holds the last well-formed screen
    /// state, so a torn half-edit is never what a reload restores). The blank
    /// draft builds <see cref="QuizMix.Empty"/> and therefore <i>does</i>
    /// write through: clearing the rows clears the stored mix too. Best-effort
    /// by design — a storage fault must not break editing, so a JS failure is
    /// swallowed here exactly as a corrupt read is swallowed in hydration;
    /// the cost is silence, the same degrade the read path already accepts.
    /// </summary>
    private async Task WriteThroughAsync()
    {
        if (Build() is not { } mix) return;
        try
        {
            await js.InvokeVoidAsync("localStorage.setItem", StorageKey, mix.ToJson());
        }
        catch (JSException)
        {
            // Storage unavailable/full: the draft is still the screen's truth;
            // only durability degrades.
        }
    }

    /// <summary>
    /// The shared tail of every mutator: announce the change, then write
    /// through. Raising <see cref="Changed"/> first keeps derived state (the
    /// start gate, the panel) current even when the write is skipped or slow.
    /// </summary>
    private Task MutatedAsync()
    {
        Changed?.Invoke();
        return WriteThroughAsync();
    }

    /// <summary>
    /// Return the draft to the blank builder — the <i>Clear mix</i> gesture's
    /// whole substance now: rows go, the toggle and length reset to their
    /// blank-builder defaults so "zero rows" means one state however it was
    /// reached, and the write-through persists <see cref="QuizMix.Empty"/> so
    /// storage follows the screen (deliberate data removal — the button's one
    /// honest job). Consent is untouched: a checked box over the blank mix is
    /// vacuous, in-effect passthrough (ruled — the app flips the bit in
    /// neither direction). Hydration is <i>not</i> forgotten: the current
    /// setup keeps its now-blank draft, whose blankness is persisted, so a
    /// re-mount would hydrate blank anyway. Ending a setup is
    /// <see cref="Discard"/>'s job.
    /// </summary>
    public Task ClearAsync()
    {
        ClearCore();
        return MutatedAsync();
    }

    /// <summary>
    /// End the draft's setup: blank the builder <i>and</i> forget hydration, so
    /// the next panel mount re-offers the stored mix afresh. Called from
    /// <c>Home.EndCurrentSetupAsync</c> — the start of every pick gesture, and
    /// Clear — beside <c>MixConsent.Revoke</c>. Deliberately <b>not</b> a
    /// write-through mutator: it does not touch localStorage, so the stored
    /// last-valid mix survives the end of the setup for the next hydration to
    /// re-offer — this asymmetry (Clear persists blank, Discard persists
    /// nothing) is exactly §4's choice-vs-consent line drawn through the
    /// draft.
    /// </summary>
    public void Discard()
    {
        _generation++;
        _hydration = null;
        ClearCore();
        Changed?.Invoke();
    }

    private void ClearCore()
    {
        _rows.Clear();
        RandomOrder = true;
        LengthText = string.Empty;
    }

    // -----------------------------------------------------------------------
    //  Mutators — the only writes to the draft, so Changed always fires and
    //  the last-valid write-through always runs (see MutatedAsync)
    // -----------------------------------------------------------------------

    /// <summary>
    /// Append a row that lands valid and distinct: the next unused kind
    /// (finding AI), seeded with that kind's default parameter (a row is never
    /// born invalid), then every row's percent re-derived to an even 100 total
    /// (finding AH).
    /// </summary>
    public Task AddRowAsync()
    {
        var kind = NextUnusedKind();
        _rows.Add(new Row(kind, DefaultParamText(kind), string.Empty));
        RebalancePercentsEvenly();
        return MutatedAsync();
    }

    /// <summary>
    /// Remove the row at <paramref name="index"/>, rebalancing the survivors
    /// to an even 100 split (symmetric with <see cref="AddRowAsync"/> — a
    /// removal must not strand the panel demanding percent the user never
    /// chose to give away). Removing the last row leaves the blank draft,
    /// which builds <see cref="QuizMix.Empty"/> and so writes the blank mix
    /// through — no special panel path any more; it is an edit like any other.
    /// </summary>
    public Task RemoveRowAsync(int index)
    {
        _rows.RemoveAt(index);
        if (_rows.Count > 0) RebalancePercentsEvenly();
        return MutatedAsync();
    }

    /// <summary>Move the row at <paramref name="index"/> by <paramref name="delta"/> (±1) — order is semantic (earlier rows win contested overlap), so a reorder is a real edit and writes through.</summary>
    public Task MoveRowAsync(int index, int delta)
    {
        var target = index + delta;
        if (target < 0 || target >= _rows.Count) return Task.CompletedTask;
        (_rows[index], _rows[target]) = (_rows[target], _rows[index]);
        return MutatedAsync();
    }

    /// <summary>
    /// Change a row's kind, seeding the new kind's sensible default parameter
    /// so the row is immediately valid; the user edits from there.
    /// </summary>
    public Task SetKindAsync(int index, QuizCategoryKind kind)
    {
        _rows[index].Kind = kind;
        _rows[index].ParamText = DefaultParamText(kind);
        return MutatedAsync();
    }

    public Task SetParamTextAsync(int index, string text)
    {
        _rows[index].ParamText = text;
        return MutatedAsync();
    }

    public Task SetPercentTextAsync(int index, string text)
    {
        _rows[index].PercentText = text;
        return MutatedAsync();
    }

    public Task SetRandomOrderAsync(bool value)
    {
        RandomOrder = value;
        return MutatedAsync();
    }

    public Task SetLengthTextAsync(string text)
    {
        LengthText = text;
        return MutatedAsync();
    }

    // -----------------------------------------------------------------------
    //  Derivations — validation and the build
    // -----------------------------------------------------------------------

    /// <summary>The rows' percent total as typed (unparseable buffers count 0) — the panel's running-total line.</summary>
    public int PercentSum =>
        _rows.Sum(r => int.TryParse(r.PercentText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var p) ? p : 0);

    /// <summary>
    /// The first problem with the current draft, or null when it would build
    /// cleanly. Recomputed per read; the panel renders it as the in-place
    /// account of <i>why</i> the state is invalid — which matters more now
    /// than under the Apply era, because a checked "Mix applies" over an
    /// invalid draft gates Start (Home's hint says fix-or-uncheck; this line
    /// says what to fix). The construction-time try/catch in
    /// <see cref="Build"/> is a backstop, not the primary validation. A blank
    /// draft reports no error: it builds the inert <see cref="QuizMix.Empty"/>
    /// (see <see cref="Build"/> — that is a ruled, load-bearing line).
    /// </summary>
    public string? ValidationError
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

            if (LengthText.Trim().Length > 0
                && (!int.TryParse(LengthText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var length)
                    || length < 1))
                return "Quiz length must be a whole number of at least 1 (or blank).";

            return null;
        }
    }

    /// <summary>
    /// Flush the rows / toggle / length into a validated <see cref="QuizMix"/>,
    /// preserving row order (order is contractual). <b>The blank (zero-row)
    /// draft builds <see cref="QuizMix.Empty"/> — never null.</b> Null is
    /// reserved for genuinely invalid states (validation errors), and two
    /// rulings load-bear on that line: checked + blank is in-effect
    /// <i>passthrough</i>, not the gated mix-invalid state (Home's
    /// <c>EffectiveMix</c> must read <see cref="QuizMix.Empty"/> there), and
    /// Clear-persists-blank requires the write-through to see blank as a
    /// persistable mix.
    /// </summary>
    public QuizMix? Build()
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
            if (entries.Count > 0 && LengthText.Trim().Length > 0)
            {
                if (!int.TryParse(LengthText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                    return null;
                length = parsed;
            }

            return entries.Count == 0
                ? QuizMix.Empty
                : new QuizMix(entries, length, RandomOrder);
        }
        catch (ArgumentException)
        {
            return null; // set-level rule the per-row checks missed — unbuildable, so dirty
        }
    }

    // -----------------------------------------------------------------------
    //  Internals — category building, seeding, hydration projection
    // -----------------------------------------------------------------------

    /// <summary>
    /// Build the row's <see cref="QuizCategory"/> through the producer's
    /// validating factories — the one kind→factory mapping in the app. The
    /// wrong-rate row converts its display percent to the stored fraction
    /// here; all parsing is invariant.
    /// </summary>
    private static bool TryBuildCategory(Row row, out QuizCategory? category, out string? error)
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
    /// The sensible starting parameter a kind gets when selected — a valid,
    /// editable value so a fresh row is never born invalid. The wrong-rate
    /// default is the display percent (25 ⇒ fraction 0.25 on build).
    /// </summary>
    private static string DefaultParamText(QuizCategoryKind kind) => kind switch
    {
        QuizCategoryKind.SeenFewerThan => "3",
        QuizCategoryKind.NotSeenInDays => "30",
        QuizCategoryKind.AvgEquityLossOver => "0.05",
        QuizCategoryKind.WrongRateOver => "25",
        _ => string.Empty,
    };

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
    private QuizCategoryKind NextUnusedKind()
    {
        foreach (var kind in CategoryKinds)
        {
            if (!_rows.Any(row => row.Kind == kind)) return kind;
        }
        return CategoryKinds[0];
    }

    /// <summary>
    /// Re-derive every row's percent as an even split summing to <b>exactly</b>
    /// 100 (finding AH). Rounding policy: each row takes the floor share, and
    /// the leftover units are handed out one apiece from the top, so the total
    /// is 100 by construction and the earlier — contested-overlap-winning — rows
    /// carry the extra unit. Deliberately overwrites hand-edited percents; the
    /// gesture that changes the row count is a restructuring of the mix, and the
    /// alternative is leaving the user to redo the arithmetic the panel already
    /// knows. Above 100 rows the floor share is 0 and the per-row 1–100 check
    /// reports it: that state is genuinely invalid, not a rounding bug.
    /// Both call sites guarantee at least one row — Add has just appended, and
    /// Remove skips the rebalance at zero rows.
    /// </summary>
    private void RebalancePercentsEvenly()
    {
        var share = 100 / _rows.Count;
        var remainder = 100 % _rows.Count;
        for (var i = 0; i < _rows.Count; i++)
            _rows[i].PercentText = (share + (i < remainder ? 1 : 0))
                .ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Inverse of <see cref="Build"/>: project a stored mix onto the draft, in
    /// wire order. The wrong-rate fraction renders back as its display
    /// percent; integer-kind parameters render without decimals.
    /// </summary>
    private void Project(QuizMix mix)
    {
        _rows.Clear();
        foreach (var entry in mix.Entries)
        {
            _rows.Add(new Row(
                entry.Category.Kind,
                ParamTextFor(entry.Category),
                entry.Percent.ToString(CultureInfo.InvariantCulture)));
        }
        RandomOrder = mix.RandomOrder;
        LengthText = mix.QuizLength?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
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
