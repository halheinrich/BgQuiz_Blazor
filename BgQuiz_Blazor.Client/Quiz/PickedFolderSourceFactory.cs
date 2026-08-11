namespace BgQuiz_Blazor.Client.Quiz;

using BgDataTypes_Lib;
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
/// position twice, because <c>DecisionId</c> is file-relative: duplicate files
/// of one match, or an identical early position reached in two different
/// matches, carry distinct ids yet render the same problem. Wiring the dedupe
/// beneath shuffle and mix means plain, shuffled and weighted runs all draw
/// from an already position-distinct supply — one rule, stated once, with no
/// per-mode variant to keep in step. It is also what makes the pre-Start match
/// summary agree with what a capless quiz serves: that count comes from the
/// same factory (<c>QuizController.SummarizeMatchesAsync</c>), so "N decisions
/// match your filters" counts deduped positions too.
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
/// <b>The survivor preference is the stats seam.</b> Among content-equal
/// copies, one carrying a lifetime-stats record beats one that does not: plain
/// first-wins could drop the id the user's history is attached to and silently
/// empty the mix pool that history feeds. The predicate reads
/// <see cref="IDecisionStatsSink.CurrentDocument"/> <i>live</i> on every call
/// rather than closing over a snapshot, because the decorator re-arbitrates on
/// every enumeration — so a Restart after this session's folds re-picks
/// survivors against the record as it now stands. It is passed unconditionally,
/// never as null: with no document bound the predicate simply answers false for
/// every id, which the producer's tie rule resolves to first occurrence — the
/// same outcome "no preference" would give, without a branch that would have to
/// guess at wiring time whether a document will be bound by enumeration time.
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
    /// <param name="stats">The lifetime-stats seam the survivor preference reads, live per call.</param>
    /// <param name="loggerFactory">Forwarded to the parsing source for its per-file failure logging.</param>
    /// <param name="clock">Monotonic pacing clock for the sources' cooperative yields.</param>
    /// <exception cref="ArgumentNullException">Any argument is null.</exception>
    internal static ProblemSetSourceFactory Create(
        PickedProblemFolder picked,
        ShuffleOption shuffle,
        IDecisionStatsSink stats,
        ILoggerFactory loggerFactory,
        TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(picked);
        ArgumentNullException.ThrowIfNull(shuffle);
        ArgumentNullException.ThrowIfNull(stats);
        ArgumentNullException.ThrowIfNull(loggerFactory);
        ArgumentNullException.ThrowIfNull(clock);

        return (filters, mix) =>
        {
            IProblemSetSource inner = new DistinctPositionProblemSetSource(
                new CachedProblemSetSource(picked, filters, loggerFactory, clock),
                HasLifetimeRecord);
            return mix.IsPassthrough && shuffle.Enabled ? new ShuffledProblemSetSource(inner) : inner;
        };

        // Whether the lifetime record holds anything at all for this id — any
        // history counts, which is the whole question the survivor rule asks. A
        // local function rather than a captured delegate so it is named where it
        // is read, and so each call re-reads CurrentDocument (see the remarks).
        bool HasLifetimeRecord(DecisionId id) =>
            stats.CurrentDocument?.Decisions.ContainsKey(id) == true;
    }
}
