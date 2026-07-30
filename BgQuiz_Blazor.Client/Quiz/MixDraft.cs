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
/// the draft survives in-app navigation exactly like its committed sibling
/// <see cref="AppliedMix"/> — mix edits are deliberately <i>not</i> lost by
/// visiting another page (ratified product behavior, superseding finding AK's
/// letter: the wedge AK fixed cannot recur, because the draft and any gate
/// derived from it now live in the same scope).
///
/// <para>
/// <b>Dirtiness is derived, never stored.</b> This type keeps no flag; the
/// start gate's mix half is <see cref="Matches"/>: the draft builds
/// (<see cref="Build"/>) <i>and</i> the built mix content-equals the committed
/// one (<see cref="QuizMix"/> value equality — ordered entries,
/// <see cref="QuizMix.QuizLength"/>, <see cref="QuizMix.RandomOrder"/>). An
/// unbuildable draft is dirty by definition; a blank draft builds
/// <see cref="QuizMix.Empty"/>, so "blank vs. passthrough" is the same rule
/// with no special case; a draft edited back into exact agreement with the
/// committed mix derives clean with no bookkeeping to reconcile. This
/// dissolves the stored-flag choreography (<c>AppliedMix.IsDirty</c> /
/// <c>MarkDirty</c> / <c>ClearDirty</c>, <c>OnMixDirty</c>,
/// <c>OnMixHydrated</c> and <c>Home</c>'s hydration reconcile), whose
/// mismatched lifetimes produced three wedge variants.
/// </para>
///
/// <para>
/// <b>Persistence stays committed-only</b>, and this service owns the one
/// localStorage key (<see cref="StorageKey"/>) in both directions:
/// <see cref="PersistAsync"/> writes the mix the panel commits (Apply, Reset,
/// last-row removal), and <see cref="EnsureHydratedAsync"/> — idempotent per
/// setup — loads the stored mix into the draft on the panel's first mount.
/// The draft itself is never persisted: an uncommitted edit dies with the app
/// scope, exactly as before. On a fresh load the committed holder stays
/// <see cref="QuizMix.Empty"/> while the hydrated draft shows the stored mix,
/// so the "re-offer the persisted mix, gated until re-Applied" behavior
/// emerges from the derived rule with zero reconcile code.
/// </para>
///
/// <para>
/// <b>A pick (or Clear) discards the draft.</b>
/// <c>Home.EndCurrentSetupAsync</c> calls <see cref="Discard"/> beside
/// <see cref="AppliedMix.Reset"/>: ending a setup drops its uncommitted mix
/// edits — a new pick means a new stats slot, so a stale draft is noise — and
/// forgets hydration, so the next panel mount (Enabled picks only) re-offers
/// the <i>stored</i> mix afresh. Under a no-stats pick no panel mounts,
/// nothing re-hydrates, and the blank draft matches the reset holder — the
/// mix plays no part in Start, with no capability fork in the gate.
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
    // Single localStorage key holding the whole committed mix as one
    // serialized QuizMix blob. The lib owns the JSON shape (ToJson /
    // TryFromJson); camelCase after the xg_ prefix per the existing key family.
    internal const string StorageKey = "xg_quizMix";

    /// <summary>
    /// Raised after every draft mutation — edits, hydration, <see cref="Clear"/>,
    /// <see cref="Discard"/> — so subscribers (Home) can re-derive state that
    /// depends on the draft. Subscribers must unsubscribe on dispose.
    /// </summary>
    public event Action? Changed;

    /// <summary>
    /// One editable mix row. Free-text buffers (not parsed values) because
    /// in-progress typing needs a string distinct from the committed value —
    /// hydrated from a stored mix, flushed into a <see cref="QuizMixEntry"/>
    /// by <see cref="Build"/>, never persisted on their own (the
    /// <c>FilterPanel</c> buffer pattern). <see cref="Kind"/> has no default:
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
    /// draft — including uncommitted edits — untouched. A missing key, the
    /// literal null token, or corrupt JSON leaves the draft blank, never an
    /// error; only a <i>successful</i> parse projects (TryFromJson's Empty
    /// fallback is a usable mix, but projecting it would overwrite the blank
    /// draft's own defaults with Empty's). Hydration fills the draft only —
    /// committing is Apply's job, so on a fresh load the stored mix arrives
    /// gated by the derived rule until the user re-Applies or Resets.
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

    /// <summary>Persist <paramref name="mix"/> as the stored mix — called by the panel's commit gestures (Apply / Reset / last-row removal), never for mere edits.</summary>
    public async Task PersistAsync(QuizMix mix)
    {
        ArgumentNullException.ThrowIfNull(mix);
        await js.InvokeVoidAsync("localStorage.setItem", StorageKey, mix.ToJson());
    }

    /// <summary>
    /// Return the draft to the blank builder — the shared normalization behind
    /// the panel's Reset gesture and its last-row removal, both of which then
    /// commit and persist <see cref="QuizMix.Empty"/>. The toggle and length
    /// reset to their blank-builder defaults so "zero rows" means one state
    /// however it was reached. Hydration is <i>not</i> forgotten: the current
    /// setup keeps its now-blank draft (its blankness is about to be persisted,
    /// so a re-mount would hydrate blank anyway). Ending a setup is
    /// <see cref="Discard"/>'s job.
    /// </summary>
    public void Clear()
    {
        ClearCore();
        Changed?.Invoke();
    }

    /// <summary>
    /// End the draft's setup: blank the builder <i>and</i> forget hydration, so
    /// the next panel mount re-offers the stored mix afresh. Called from
    /// <c>Home.EndCurrentSetupAsync</c> — the start of every pick gesture, and
    /// Clear — beside <see cref="AppliedMix.Reset"/>: uncommitted mix edits
    /// were made against the outgoing setup's stats slot and do not carry to
    /// the next. Does not touch localStorage — the stored mix survives for the
    /// next hydration to re-offer, mirroring <see cref="AppliedMix.Reset"/>'s
    /// hands-off treatment.
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
    //  Mutators — the only writes to the draft, so Changed always fires
    // -----------------------------------------------------------------------

    /// <summary>
    /// Append a row that lands valid and distinct: the next unused kind
    /// (finding AI), seeded with that kind's default parameter (a row is never
    /// born invalid), then every row's percent re-derived to an even 100 total
    /// (finding AH).
    /// </summary>
    public void AddRow()
    {
        var kind = NextUnusedKind();
        _rows.Add(new Row(kind, DefaultParamText(kind), string.Empty));
        RebalancePercentsEvenly();
        Changed?.Invoke();
    }

    /// <summary>
    /// Remove the row at <paramref name="index"/>, rebalancing the survivors
    /// to an even 100 split (symmetric with <see cref="AddRow"/> — a removal
    /// must not strand the panel demanding percent the user never chose to
    /// give away). Removing the last row leaves the blank draft; committing
    /// the blank mix that follows is the panel's job (its shared blank path).
    /// </summary>
    public void RemoveRow(int index)
    {
        _rows.RemoveAt(index);
        if (_rows.Count > 0) RebalancePercentsEvenly();
        Changed?.Invoke();
    }

    /// <summary>Move the row at <paramref name="index"/> by <paramref name="delta"/> (±1) — order is semantic, so a reorder is a real edit and derives dirty.</summary>
    public void MoveRow(int index, int delta)
    {
        var target = index + delta;
        if (target < 0 || target >= _rows.Count) return;
        (_rows[index], _rows[target]) = (_rows[target], _rows[index]);
        Changed?.Invoke();
    }

    /// <summary>
    /// Change a row's kind, seeding the new kind's sensible default parameter
    /// so the row is immediately valid; the user edits from there.
    /// </summary>
    public void SetKind(int index, QuizCategoryKind kind)
    {
        _rows[index].Kind = kind;
        _rows[index].ParamText = DefaultParamText(kind);
        Changed?.Invoke();
    }

    public void SetParamText(int index, string text)
    {
        _rows[index].ParamText = text;
        Changed?.Invoke();
    }

    public void SetPercentText(int index, string text)
    {
        _rows[index].PercentText = text;
        Changed?.Invoke();
    }

    public void SetRandomOrder(bool value)
    {
        RandomOrder = value;
        Changed?.Invoke();
    }

    public void SetLengthText(string text)
    {
        LengthText = text;
        Changed?.Invoke();
    }

    // -----------------------------------------------------------------------
    //  Derivations — validation, build, and the derived start-gate rule
    // -----------------------------------------------------------------------

    /// <summary>The rows' percent total as typed (unparseable buffers count 0) — the panel's running-total line.</summary>
    public int PercentSum =>
        _rows.Sum(r => int.TryParse(r.PercentText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var p) ? p : 0);

    /// <summary>
    /// The first problem with the current draft, or null when it would build
    /// cleanly. Recomputed per read; the panel disables Apply while non-null,
    /// so the construction-time try/catch in <see cref="Build"/> is a
    /// backstop, not the primary validation. A blank draft reports no error —
    /// it <i>would</i> build the inert <see cref="QuizMix.Empty"/> — but the
    /// panel separately disables Apply at zero rows: committing the blank mix
    /// is the blank path's job (Reset / last-row removal), so Apply requires
    /// at least one row.
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
    /// preserving row order (order is contractual). Zero rows build
    /// <see cref="QuizMix.Empty"/>; null when the draft doesn't build — which
    /// the derived gate reads as dirty by definition (an unbuildable draft can
    /// never agree with a committed mix).
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

    /// <summary>
    /// The start gate's mix half, fully derived: whether this draft builds and
    /// the built mix content-equals <paramref name="committed"/> (the
    /// <see cref="AppliedMix.Current"/> the quiz would actually run). False —
    /// i.e. dirty, Start gated — while the draft holds an uncommitted or
    /// unbuildable edit; true again the moment the draft and the commitment
    /// agree, however that came about (Apply, Reset, or an edit that returns
    /// the draft to exactly the committed content). Because a blank draft
    /// builds <see cref="QuizMix.Empty"/>, the fresh blank state matches the
    /// fresh holder with no special case.
    /// </summary>
    public bool Matches(QuizMix committed)
    {
        ArgumentNullException.ThrowIfNull(committed);
        return Build() is { } built && built == committed;
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
    /// reports it: that state is genuinely uncommittable, not a rounding bug.
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
