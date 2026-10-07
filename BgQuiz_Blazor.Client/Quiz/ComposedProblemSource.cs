namespace BgQuiz_Blazor.Client.Quiz;

using BgGame_Lib;
using XgFilter_Lib;

/// <summary>
/// What one <see cref="ProblemSetSourceFactory"/> invocation produces: the
/// composed stack to enumerate, plus a way to ask that same stack how many
/// duplicate records its dedupe layer collapsed while enumerating, and which
/// of the picked files its parse could not read.
///
/// <para>
/// <b>Why the factory returns a pair at all.</b> The pre-Start match count is
/// a count of <i>distinct positions</i> — the dedupe layer sits at the bottom
/// of every stack (<see cref="PickedFolderSourceFactory"/>), so the number on
/// screen is already deduplicated. Saying so on screen needs the collapse
/// magnitude, and the magnitude is telemetry of the enumeration the caller has
/// just run. Returning a bare <see cref="IProblemSetSource"/> hides it: the
/// dedupe layer may be wrapped (a passthrough run with shuffle enabled), so
/// the caller cannot reach it without type-testing through the wrapper — which
/// is exactly the fragility this type removes.
/// </para>
///
/// <para>
/// <b>A reader, not the decorator.</b> The telemetry arrives as a
/// <see cref="Func{TResult}"/> rather than as a typed
/// <c>DistinctPositionProblemSetSource</c> reference, for two reasons. It keeps
/// the layer stack the factory's own secret — a consumer asks "how many did you
/// collapse?" and never learns which layer answered, so a future change to the
/// composition is invisible here. And it lets a stack that legitimately has no
/// dedupe layer say so honestly (<c>() =&gt; 0</c>) instead of having to
/// fabricate a decorator that is not in the chain, which is what a typed field
/// would force on every substitute stack.
/// </para>
///
/// <para>
/// <b>Deferred by construction.</b> How many copies collapse is unknowable
/// before enumeration — that is the producer's own
/// <c>DistinctPositionProblemSetSource</c> contract, and the reason
/// <see cref="IProblemSetSource.Count"/> is null beneath that layer. So the
/// magnitude is read through a call, after <see cref="Source"/> has been
/// enumerated, never captured as a value at composition time.
/// </para>
///
/// <para>
/// <b>The second reader: which files the stack's parse could not read</b>
/// (halheinrich/backgammon#368). The parse-once layer at the bottom of the
/// stack keeps the <see cref="SourceReport"/> of the one walk that produced
/// its decisions, and a count over the stack is a count over an incomplete
/// selection whenever that report names a rejected file. The reader
/// travels back the same way the collapse magnitude does, and for the same
/// reasons: the composition knows which layer holds it and no caller has to,
/// and a substitute stack with no parse layer answers honestly with the
/// completed report of a walk over no sources. It reads <i>this stack's</i>
/// parse — the one its cache layer adopted or made — never whatever the
/// picked-folder holder contains at the time of the call, so a stack built
/// against a since-superseded pick still reports its own walk.
/// </para>
/// </summary>
internal sealed class ComposedProblemSource
{
    private readonly Func<int> _duplicatesCollapsed;
    private readonly Func<SourceReport?> _sourceReport;

    /// <summary>
    /// Pair <paramref name="source"/> with the readers that report its collapse
    /// magnitude and its parse's source report.
    /// </summary>
    /// <param name="source">The composed stack to enumerate.</param>
    /// <param name="duplicatesCollapsed">
    /// Reads how many records <paramref name="source"/>'s dedupe layer dropped
    /// as duplicates during the most recent enumeration of that stack —
    /// <c>() =&gt; 0</c> for a stack with no dedupe layer. Called after
    /// enumeration; see <see cref="GetDuplicatesCollapsed"/>.
    /// </param>
    /// <param name="sourceReport">
    /// Reads the completed <see cref="SourceReport"/> of the parse
    /// <paramref name="source"/>'s cache layer draws from, or null before the
    /// stack's first enumeration has resolved one. Called after enumeration;
    /// see <see cref="GetSourceReport"/>.
    /// </param>
    /// <exception cref="ArgumentNullException">Any argument is null.</exception>
    internal ComposedProblemSource(
        IProblemSetSource source, Func<int> duplicatesCollapsed, Func<SourceReport?> sourceReport)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(duplicatesCollapsed);
        ArgumentNullException.ThrowIfNull(sourceReport);
        Source = source;
        _duplicatesCollapsed = duplicatesCollapsed;
        _sourceReport = sourceReport;
    }

    /// <summary>The composed stack — the one thing a caller enumerates.</summary>
    internal IProblemSetSource Source { get; }

    /// <summary>
    /// How many matching records the stack collapsed as duplicates of a record
    /// it had already yielded, during the most recent enumeration of
    /// <see cref="Source"/>. Zero before any enumeration, and zero for a stack
    /// with no dedupe layer — a method rather than a property because the answer
    /// changes with each enumeration.
    /// </summary>
    internal int GetDuplicatesCollapsed() => _duplicatesCollapsed();

    /// <summary>
    /// The completed <see cref="SourceReport"/> of the parse this stack draws
    /// from — the files attempted, the files rejected and why — or null
    /// before the stack's first enumeration has resolved one. Unlike the
    /// collapse magnitude it does not change with each enumeration: the parse
    /// happens once per pick and every later enumeration reads it, so a
    /// re-count and a Start after it read the same object (a method all the
    /// same, since the answer is resolved by enumerating, not by composing).
    /// </summary>
    internal SourceReport? GetSourceReport() => _sourceReport();
}
