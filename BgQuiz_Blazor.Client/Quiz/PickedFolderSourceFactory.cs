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
/// <item><see cref="ShuffledProblemSetSource"/>, conditionally — see the
/// arbitration rule below.</item>
/// </list>
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
            IProblemSetSource inner = new CachedProblemSetSource(picked, filters, loggerFactory, clock);
            return mix.IsPassthrough && shuffle.Enabled ? new ShuffledProblemSetSource(inner) : inner;
        };
    }
}
