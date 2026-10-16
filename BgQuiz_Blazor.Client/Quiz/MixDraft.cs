namespace BgQuiz_Blazor.Client.Quiz;

using System.Collections.Immutable;
using System.Globalization;
using BgGame_Lib;
using BgUiPrimitives_Razor;
using Microsoft.Extensions.Logging;

/// <summary>
/// The app-scoped mix draft: the edit state of the stats-weighted mix builder, the ordered
/// category rows (kind, parameter text, percent text) and the optional quiz length. The mix
/// selects and nothing else; the run's Order presents (SPEC-filtering.md §5). <c>MixPanel</c>
/// is a view over this: it renders <see cref="Rows"/> and routes every gesture through the
/// mutators here, so edits survive in-app navigation.
///
/// <para>
/// There is no committed copy of the mix: what runs, while the panel is on screen, is
/// <see cref="Build"/>'s result, so screen and effect cannot diverge. A visible draft that
/// fails to build is the one mix state that gates Start. Persistence follows the screen as
/// last-valid write-through: every mutation that validates persists the built
/// <see cref="QuizMix"/> (the blank draft included, so Clear persists blank), a mutation
/// that leaves the draft invalid skips the write, and the stored mix is read once per visit.
/// Storage that refuses never stops the mix (halheinrich/backgammon#360): a refused read
/// hydrates nothing, a refused write keeps the draft for the visit, and each is logged and
/// reported to <see cref="BrowserStorageCondition"/>.
/// </para>
/// </summary>
internal sealed class MixDraft(BrowserStorage browser, ILogger<MixDraft> logger, BrowserStorageCondition storage)
{
    /// <summary>The one localStorage key, holding the last well-formed mix as the lib's own JSON.</summary>
    internal const string StorageKey = "xg_quizMix";

    /// <summary>Raised after every draft mutation and after hydration, so Home re-derives what depends on the draft.</summary>
    public event Action? Changed;

    /// <summary>
    /// One editable mix row: free-text buffers, since in-progress typing needs a string
    /// distinct from any parsed value; flushed into a <see cref="QuizMixEntry"/> by
    /// <see cref="Build"/>. Written only through a <see cref="MixDraft"/> mutator.
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

    private ImmutableArray<Row> _rows = [];

    /// <summary>The draft rows, in draw order (order is contractual). Replaced on every structural edit, never written through a cast.</summary>
    public ImmutableArray<Row> Rows => _rows;

    /// <summary>The optional quiz-length buffer; blank means no cap.</summary>
    public string LengthText { get; private set; } = string.Empty;

    /// <summary>Every selectable category kind, in the picker's display order, most specific first and the residual last.</summary>
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

    private Task? _hydration;

    /// <summary>
    /// Load the stored mix into the draft, once per visit: the first caller (the panel's init)
    /// runs the read; later calls return the cached task and leave the surviving draft, edits
    /// included, untouched. A missing key, corrupt JSON or a refused read leaves the draft
    /// blank. Hydration fills the draft only and never writes storage.
    /// </summary>
    public Task EnsureHydratedAsync() => _hydration ??= HydrateAsync();

    private async Task HydrateAsync()
    {
        var read = await browser.ReadAsync(BrowserStorageArea.Local, StorageKey);
        if (read.IsRefused)
        {
            logger.LogWarning(read.Refusal,
                "The mix could not be read from browser storage ({Key}); the mix panel starts blank for this visit.",
                StorageKey);
            storage.ReportRefused();
            return;
        }

        if (QuizMix.TryFromJson(read.Value, out var mix)) Project(mix);
        Changed?.Invoke();
    }

    private async Task WriteThroughAsync()
    {
        if (Build() is not { } mix) return;
        var written = await browser.WriteAsync(BrowserStorageArea.Local, StorageKey, mix.ToJson());
        if (written.IsRefused)
        {
            logger.LogWarning(written.Refusal,
                "The mix could not be saved to browser storage ({Key}); it is kept for this visit only.",
                StorageKey);
            storage.ReportRefused();
        }
    }

    /// <summary>The shared tail of every mutator: announce the change, then write through.</summary>
    private Task MutatedAsync()
    {
        Changed?.Invoke();
        return WriteThroughAsync();
    }

    /// <summary>
    /// Return the draft to the blank builder, the Clear mix gesture: the rows go, and the
    /// write-through persists the blank mix. The mix stays in effect: blank is the blank mix,
    /// the passthrough; turning the mix off is the setting's job.
    /// </summary>
    public Task ClearAsync()
    {
        _rows = [];
        LengthText = string.Empty;
        return MutatedAsync();
    }

    /// <summary>
    /// Append a row that lands valid and distinct: the next unused kind, seeded with that
    /// kind's default parameter, then every row's percent re-derived to an even 100 total.
    /// </summary>
    public Task AddRowAsync()
    {
        var kind = NextUnusedKind();
        _rows = _rows.Add(new Row(kind, DefaultParamText(kind), string.Empty));
        RebalancePercentsEvenly();
        return MutatedAsync();
    }

    /// <summary>Remove the row at <paramref name="index"/>, rebalancing the survivors to an even 100 split; removing the last row leaves the blank draft.</summary>
    public Task RemoveRowAsync(int index)
    {
        _rows = _rows.RemoveAt(index);
        if (!_rows.IsEmpty) RebalancePercentsEvenly();
        return MutatedAsync();
    }

    /// <summary>Move the row at <paramref name="index"/> by <paramref name="delta"/> (±1); order is semantic, so a reorder is a real edit.</summary>
    public Task MoveRowAsync(int index, int delta)
    {
        var target = index + delta;
        if (target < 0 || target >= _rows.Length) return Task.CompletedTask;
        var moving = _rows[index];
        _rows = _rows.SetItem(index, _rows[target]).SetItem(target, moving);
        return MutatedAsync();
    }

    /// <summary>Change a row's kind, seeding the new kind's default parameter so the row is immediately valid.</summary>
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

    public Task SetLengthTextAsync(string text)
    {
        LengthText = text;
        return MutatedAsync();
    }

    /// <summary>The rows' percent total as typed (unparseable buffers count 0).</summary>
    public int PercentSum =>
        _rows.Sum(r => int.TryParse(r.PercentText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var p) ? p : 0);

    /// <summary>The first problem with the current draft, or null when it would build cleanly; a blank draft reports none.</summary>
    public string? ValidationError
    {
        get
        {
            if (_rows.IsEmpty) return null;

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
    /// Flush the rows and length into a validated <see cref="QuizMix"/>, preserving row order.
    /// The blank draft builds <see cref="QuizMix.Empty"/>, never null: blank is the
    /// passthrough in effect, and null is reserved for an invalid draft.
    /// </summary>
    public QuizMix? Build()
    {
        try
        {
            var entries = new List<QuizMixEntry>(_rows.Length);
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
                : new QuizMix(entries, length);
        }
        catch (ArgumentException)
        {
            return null; // a set-level rule the per-row checks missed
        }
    }

    /// <summary>Build the row's category through the producer's validating factories, the one kind-to-factory mapping in the app.</summary>
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
            error = ex.Message;
            return false;
        }
    }

    private static bool TryParseInt(string text, out int value) =>
        int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);

    private static bool TryParseDouble(string text, out double value) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
        && double.IsFinite(value);

    /// <summary>The valid, editable parameter a kind starts with, so a fresh row is never born invalid.</summary>
    private static string DefaultParamText(QuizCategoryKind kind) => kind switch
    {
        QuizCategoryKind.SeenFewerThan => "3",
        QuizCategoryKind.NotSeenInDays => "30",
        QuizCategoryKind.AvgEquityLossOver => "0.05",
        QuizCategoryKind.WrongRateOver => "25",
        _ => string.Empty,
    };

    /// <summary>The first kind of <see cref="CategoryKinds"/> no row uses, so successive Adds walk the list; the first kind once every one is used.</summary>
    private QuizCategoryKind NextUnusedKind()
    {
        foreach (var kind in CategoryKinds)
        {
            if (!_rows.Any(row => row.Kind == kind)) return kind;
        }
        return CategoryKinds[0];
    }

    /// <summary>
    /// Re-derive every row's percent as an even split summing to exactly 100: each row takes
    /// the floor share and the leftover units go one apiece from the top. Deliberately
    /// overwrites hand-edited percents: a change to the row count restructures the mix.
    /// </summary>
    private void RebalancePercentsEvenly()
    {
        var share = 100 / _rows.Length;
        var remainder = 100 % _rows.Length;
        for (var i = 0; i < _rows.Length; i++)
            _rows[i].PercentText = (share + (i < remainder ? 1 : 0))
                .ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>Inverse of <see cref="Build"/>: project a stored mix onto the draft, in wire order.</summary>
    private void Project(QuizMix mix)
    {
        _rows = [.. mix.Entries.Select(entry => new Row(
            entry.Category.Kind,
            ParamTextFor(entry.Category),
            entry.Percent.ToString(CultureInfo.InvariantCulture)))];
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
