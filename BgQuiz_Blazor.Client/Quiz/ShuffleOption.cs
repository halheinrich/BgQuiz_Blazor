namespace BgQuiz_Blazor.Client.Quiz;

/// <summary>
/// Per-app holder for the "Shuffle order" toggle on <c>Home</c>'s quiz-start
/// gate — the presentation-only complement to <see cref="AppliedFilter"/>
/// (admission: which decisions are in scope) and <see cref="PickedProblemSet"/>
/// (which files are in scope). Shuffling changes only the order decisions are
/// presented in; it is deliberately not folded into <c>FilterConfig</c>.
///
/// <para>
/// Lifetime: <b>Scoped</b> — the same per-app lifetime as the other start-gate
/// holders, so the toggle survives in-app navigation (<c>Home</c> is
/// re-instantiated on navigate-back). Read by the <c>ProblemSetSourceFactory</c>
/// registered in <c>Program.cs</c> at <see cref="QuizController.StartAsync"/>
/// invocation time, not at registration time.
/// </para>
///
/// <para>
/// <b>No applied/dirty gate.</b> Unlike <see cref="AppliedFilter"/>, a checkbox
/// has no half-edited state to guard against — every toggle is a complete,
/// immediately valid choice. <see cref="Enabled"/> is simply read live at
/// Start; there is nothing to "apply".
/// </para>
/// </summary>
internal sealed class ShuffleOption
{
    /// <summary>True when the user wants the problem-set order shuffled.</summary>
    public bool Enabled { get; private set; }

    /// <summary>Record the user's current toggle state.</summary>
    public void Set(bool enabled) => Enabled = enabled;
}
