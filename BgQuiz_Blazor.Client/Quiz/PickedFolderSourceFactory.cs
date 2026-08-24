namespace BgQuiz_Blazor.Client.Quiz;

using BgGame_Lib;
using Microsoft.Extensions.Logging;

/// <summary>
/// Builds the production <see cref="ProblemSetSourceFactory"/> — the one
/// definition of how the running quiz's source is composed over the user's
/// picked folder. The client's <c>Program.cs</c> resolves the ingredients and
/// registers what this returns; nothing else constructs the layer stack.
///
/// <para>
/// <b>Why a named type rather than the DI lambda it replaced.</b> The
/// composition is the app's most wiring-sensitive code and the only place the
/// layer <i>order</i> is stated, so it has to be reachable from a test. As a
/// lambda inside the registration it was not, and the tests that pinned "the
/// path Program.cs wires" re-typed it by hand — a copy that can agree with
/// itself while production is mis-wired (they had already drifted to the
/// stream source, skipping the parse-once layer entirely). Tests now call
/// <see cref="Create"/>, so there is one composition and one thing to get
/// right.
/// </para>
///
/// <para>
/// <b>The stack, innermost first.</b>
/// <list type="number">
/// <item><see cref="CachedProblemSetSource"/> over the pick — the parse-once
/// layer that parses the picked files unfiltered on the first Start and serves
/// every later Start/Restart by filtering the cached decisions (the cache slot
/// rides <see cref="PickedProblemFolder"/>, so a re-pick or Clear invalidates
/// it by construction). See its own section in INSTRUCTIONS.md.</item>
/// <item><see cref="JacobyStampedProblemSetSource"/> — the pool-composition
/// guard: a money record that does not state its Jacoby rule fails the folder
/// load, naming the file, instead of quizzing unkeyed and uncounted
/// (<c>../SPEC-stats-identity.md</c> §2, amended 2026-08-24; issue
/// <c>halheinrich/backgammon#142</c>). Beneath the dedupe on purpose — see that
/// type for why, and for why this rung alone fails loud.</item>
/// <item><see cref="DistinctPositionProblemSetSource"/> — one item per distinct
/// position, always. See the placement rule below.</item>
/// <item><see cref="ShuffledProblemSetSource"/>, conditionally — see the
/// arbitration rule below.</item>
/// </list>
/// </para>
///
/// <para>
/// <b>Dedupe sits at the bottom, so every quiz mode inherits the rule</b>
/// (issue <c>halheinrich/backgammon#84</c>). A quiz could serve the same
/// position twice, because the records' identities are file-relative:
/// duplicate files of one match, or an identical early position reached in two
/// different matches, carry distinct <c>DecisionId</c>s yet render the same
/// problem — the decorator keys on the content identity instead. Wiring the
/// dedupe beneath shuffle and mix means plain, shuffled and weighted runs all draw
/// from an already position-distinct supply — one rule, stated once, with no
/// per-mode variant to keep in step. It is also what makes the pre-Start match
/// summary agree with what a capless quiz serves: that count comes from the
/// same factory (<c>QuizController.SummarizeMatchesAsync</c>), so "N decisions
/// match your filters" counts deduped positions too.
/// </para>
///
/// <para>
/// <b>The stack reports what it collapsed.</b> A count the user cannot
/// reconcile with their own file count reads as a bug
/// (halheinrich/backgammon#104), so the factory returns a
/// <see cref="ComposedProblemSource"/> rather than a bare source: the pair
/// carries the stack to enumerate and a reader for the dedupe layer's collapse
/// magnitude. The magnitude is the producer's own duplicate-class telemetry
/// (<see cref="DistinctPositionProblemSetSource.LastDuplicateClasses"/>) folded
/// to one number here — the composition knows which layer holds it, and no
/// caller has to.
/// </para>
///
/// <para>
/// <b>Above the filter, not below it.</b> The decorator wraps the
/// <i>filtered</i> stream deliberately. Deduping the raw parse first could
/// elect a survivor the filter then rejects while dropping the content-equal
/// copy that would have passed — filters are not purely positional (players,
/// dates, error bands), so the copies are not interchangeable to them, and a
/// matching position would be silently lost. Filter-then-dedupe cannot lose
/// one.
/// </para>
///
/// <para>
/// <b>Which copy survives is nobody's business here.</b> First occurrence
/// survives, for display and provenance only — lifetime stats are keyed by
/// content (<c>ProblemKey</c>), so every content-equal copy folds into and
/// reads the same record whichever one the quiz shows. The stats-bearing
/// survivor preference this factory used to pass existed solely to keep
/// id-keyed stats reachable across that fragmentation, and is deleted with the
/// fragmentation (SPEC-stats-identity.md §4) — which is why this factory no
/// longer takes the stats seam at all.
/// </para>
///
/// <para>
/// <b>Shuffle applies only to a passthrough (blank-mix) run.</b> An active mix
/// owns presentation order through its own <c>RandomOrder</c> toggle, and a
/// shuffled inner under the composing decorator would silently break
/// <c>RandomOrder: false</c>'s fully-deterministic contract (draws and
/// presentation in source order). That single rule is the whole reason the
/// factory delegate takes the mix at all. The composition layer itself is the
/// controller's to wire, never this factory's.
/// </para>
///
/// <para>
/// <b>Read live at invocation, not at registration.</b> Every holder is read
/// inside the returned delegate — which the controller invokes at
/// <c>StartAsync</c> — so a pick or a shuffle toggle made before Start takes
/// effect on that Start. The unseeded <see cref="ShuffledProblemSetSource"/>
/// ctor is used deliberately: reproducibility is a test-only concern (see that
/// type's seeded ctor), never user-facing.
/// </para>
/// </summary>
internal static class PickedFolderSourceFactory
{
    /// <summary>
    /// Build the factory delegate over the supplied holders and services. The
    /// arguments are the app-scoped instances; the returned delegate closes
    /// over them and reads their state at each invocation.
    /// </summary>
    /// <param name="picked">The picked-folder holder — supplies the files and carries the cross-Start parse cache.</param>
    /// <param name="shuffle">The user's "Shuffle order" choice, read per invocation.</param>
    /// <param name="loggerFactory">Forwarded to the parsing source for its per-file failure logging.</param>
    /// <param name="clock">Monotonic pacing clock for the sources' cooperative yields.</param>
    /// <exception cref="ArgumentNullException">Any argument is null.</exception>
    internal static ProblemSetSourceFactory Create(
        PickedProblemFolder picked,
        ShuffleOption shuffle,
        ILoggerFactory loggerFactory,
        TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(picked);
        ArgumentNullException.ThrowIfNull(shuffle);
        ArgumentNullException.ThrowIfNull(loggerFactory);
        ArgumentNullException.ThrowIfNull(clock);

        return (filters, mix) =>
        {
            var deduped = new DistinctPositionProblemSetSource(
                new JacobyStampedProblemSetSource(
                    new CachedProblemSetSource(picked, filters, loggerFactory, clock)));
            IProblemSetSource composed = mix.IsPassthrough && shuffle.Enabled
                ? new ShuffledProblemSetSource(deduped)
                : deduped;
            // The reader closes over the layer that owns the telemetry, so no
            // caller has to reach through the conditional shuffle wrapper to
            // find it — see ComposedProblemSource for why a reader travels back
            // rather than the decorator itself.
            return new ComposedProblemSource(composed, () => DuplicatesCollapsed(deduped));
        };
    }

    /// <summary>
    /// How many records <paramref name="deduped"/> dropped in its most recent
    /// enumeration: every member of every duplicate class past the one that
    /// survived it. The producer reports the classes; folding them into the one
    /// number the app talks about belongs here, with the composer that put the
    /// layer in the stack.
    ///
    /// <para>
    /// Null telemetry — no enumeration has run yet — reads as zero rather than
    /// as unknown: a stack nobody has drawn from has collapsed nothing, and the
    /// only caller reads this after enumerating.
    /// </para>
    /// </summary>
    private static int DuplicatesCollapsed(DistinctPositionProblemSetSource deduped) =>
        deduped.LastDuplicateClasses?.Sum(duplicateClass => duplicateClass.Members.Count - 1) ?? 0;
}
