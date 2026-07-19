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
