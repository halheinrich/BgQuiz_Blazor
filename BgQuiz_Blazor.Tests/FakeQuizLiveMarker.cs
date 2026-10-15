using BgQuiz_Blazor.Client.Quiz;

namespace BgQuiz_Blazor.Tests;

/// <summary>
/// The live-quiz marker as the controller writes it, recorded: every write in
/// order, and whether the marker now reads as live. The controller is the
/// marker's one writer (<see cref="IQuizLiveMarker"/>), so a test of where a
/// run is marked live or cleared reads this; the page suite drives the real
/// <see cref="QuizLiveMarker"/> over its storage plan instead.
/// </summary>
internal sealed class FakeQuizLiveMarker : IQuizLiveMarker
{
    /// <summary>The writes, in order: <c>"live"</c> for a mark, <c>"clear"</c> for a clear.</summary>
    public List<string> Writes { get; } = [];

    /// <summary>Whether the marker reads as live now — what a reload would find.</summary>
    public bool IsLive { get; private set; }

    /// <summary>Invoked at each clear, as it is made — the observation point for what else had happened by then.</summary>
    public Action? OnClear { get; set; }

    public ValueTask MarkLiveAsync()
    {
        Writes.Add("live");
        IsLive = true;
        return ValueTask.CompletedTask;
    }

    public ValueTask ClearAsync()
    {
        Writes.Add("clear");
        IsLive = false;
        OnClear?.Invoke();
        return ValueTask.CompletedTask;
    }
}
