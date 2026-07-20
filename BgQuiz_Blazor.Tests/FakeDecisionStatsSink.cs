using BgGame_Lib;
using BgQuiz_Blazor.Client.Quiz;

namespace BgQuiz_Blazor.Tests;

/// <summary>
/// Recording <see cref="IDecisionStatsSink"/> for controller and page tests:
/// counts binds and captures every folded submission in order, so tests can
/// assert exactly which answers the controller finalized — and, as important,
/// which flows (skip, off-list, auto-skip, redo) folded nothing.
/// </summary>
internal sealed class FakeDecisionStatsSink : IDecisionStatsSink
{
    public int BeginQuizCallCount { get; private set; }

    /// <summary>
    /// Scriptable capability peek. Defaults to false — the no-stats posture a
    /// fresh app has — so blank-mix tests never depend on stats state; tests
    /// exercising a weighted start opt in explicitly.
    /// </summary>
    public bool CanBindStats { get; set; }

    /// <summary>
    /// Scriptable live document. Defaults to null (no bound context); a
    /// weighted-start test sets it — and can replace it mid-test to model the
    /// lifetime record advancing between runs (the Restart-recomposes pin).
    /// </summary>
    public DecisionStatsDocument? CurrentDocument { get; set; }

    /// <summary>Checker-play folds, in fold order.</summary>
    public List<SubmittedPlay> Plays { get; } = [];

    /// <summary>Cube folds, in fold order.</summary>
    public List<SubmittedCubeAction> Cubes { get; } = [];

    public int TotalFolds => Plays.Count + Cubes.Count;

    public Task BeginQuizAsync()
    {
        BeginQuizCallCount++;
        return Task.CompletedTask;
    }

    public Task RecordAsync(SubmittedPlay play)
    {
        Plays.Add(play);
        return Task.CompletedTask;
    }

    public Task RecordAsync(SubmittedCubeAction cube)
    {
        Cubes.Add(cube);
        return Task.CompletedTask;
    }
}
