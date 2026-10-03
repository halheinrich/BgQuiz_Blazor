using BgGame_Lib;
using BgQuiz_Blazor.Client.Quiz;

namespace BgQuiz_Blazor.Tests;

/// <summary>
/// Recording <see cref="IProblemStatsSink"/> for controller and page tests:
/// counts binds and captures every folded submission in order, so tests can
/// assert exactly which answers the controller finalized — and, as important,
/// which flows (skip, off-list, auto-skip, redo) folded nothing.
/// </summary>
internal sealed class FakeProblemStatsSink : IProblemStatsSink
{
    public int BeginQuizCallCount { get; private set; }

    /// <summary>
    /// Scriptable policy — "may a weighted mix run for the picked folder":
    /// a folder that can hold stats <i>and</i> already holds some. Defaults to
    /// false — the no-stats posture a fresh app has — so blank-mix tests never
    /// depend on stats state; tests exercising a weighted start opt in
    /// explicitly.
    /// </summary>
    public bool CanWeightMix { get; set; }

    /// <summary>
    /// Scriptable fact — "does the picked folder hold a stats document with
    /// content" (<c>SPEC-filtering.md</c> §5). Defaults to false for the same
    /// reason its policy sibling does. A page test that wants the mix panel on
    /// screen sets <b>this</b>, since visibility reads the fact.
    ///
    /// <para>
    /// <b>Independently settable, deliberately, though production couples
    /// them.</b> There the probe cannot find stats in a folder it could not
    /// have written, so a true fact implies a true policy. Letting a test
    /// script the pair apart is what keeps the controller's stage-1 refusal
    /// pinnable at all: that backstop exists precisely for a caller reaching
    /// the controller without the host's gating, and a fake that enforced the
    /// host's invariant could not express one.
    /// </para>
    /// </summary>
    public bool PickedFolderHasStats { get; set; }

    /// <summary>
    /// Scriptable live document. Defaults to null (no bound context); a
    /// weighted-start test sets it — and can replace it mid-test to model the
    /// lifetime record advancing between runs (the Restart-recomposes pin).
    /// </summary>
    public ProblemStatsDocument? CurrentDocument { get; set; }

    /// <summary>Checker-play folds, in fold order.</summary>
    public List<SubmittedPlay> Plays { get; } = [];

    /// <summary>Cube folds, in fold order.</summary>
    public List<SubmittedCubeAnswer> Cubes { get; } = [];

    public int TotalFolds => Plays.Count + Cubes.Count;

    /// <summary>
    /// Scriptable fold gate: <see cref="RecordAsync(SubmittedPlay)"/> /
    /// <see cref="RecordAsync(SubmittedCubeAnswer)"/> await this before
    /// folding. Defaults to completed (folds are synchronous, as before); an
    /// overlap test sets a <see cref="TaskCompletionSource"/> task here to
    /// freeze the controller <i>inside</i> a Submit's awaited write — the
    /// window where the submission's review is already on screen and only the
    /// transition gate stands between the write and a gesture that would
    /// begin a new run, move on or end the quiz under it.
    /// </summary>
    public Task RecordGate { get; set; } = Task.CompletedTask;

    /// <summary>
    /// Scriptable observation point: called as a fold begins, on the caller's
    /// own thread and before <see cref="RecordGate"/> is awaited. A test sets
    /// it to read what the controller shows at the moment it folds — from
    /// inside the fold, so there is no window to race.
    /// </summary>
    public Action? OnRecording { get; set; }

    public Task BeginQuizAsync()
    {
        BeginQuizCallCount++;
        return Task.CompletedTask;
    }

    public async Task RecordAsync(SubmittedPlay play)
    {
        OnRecording?.Invoke();
        await RecordGate;
        Plays.Add(play);
    }

    public async Task RecordAsync(SubmittedCubeAnswer cube)
    {
        OnRecording?.Invoke();
        await RecordGate;
        Cubes.Add(cube);
    }
}
