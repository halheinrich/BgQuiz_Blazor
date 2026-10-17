namespace BgQuiz_Blazor.Tests;

using BgQuiz_Blazor.Client.Quiz;

/// <summary>The live marker as a value in memory: what the coordinator wrote, and what a page reads back.</summary>
internal sealed class FakeQuizLiveMarker : IQuizLiveMarker
{
    public bool IsLive { get; set; }
    public int MarkCount { get; private set; }
    public int ClearCount { get; private set; }

    public ValueTask MarkLiveAsync()
    {
        IsLive = true;
        MarkCount++;
        return ValueTask.CompletedTask;
    }

    public ValueTask ClearAsync()
    {
        IsLive = false;
        ClearCount++;
        return ValueTask.CompletedTask;
    }

    public ValueTask<bool> WasLiveAsync() => ValueTask.FromResult(IsLive);
}
