
namespace BgQuiz_Blazor.E2eTests;

/// <summary>
/// A hold's timeline written to the test's output: what
/// <see cref="RowFitModuleHold"/> or <see cref="RowFitFirstFitHold"/> saw,
/// each at its instant on the test's clock. A scenario under a hold writes it
/// in its <c>finally</c>, so the timeline reaches the log on a pass as on a
/// failure — the diagnostic the holds keep of their own since the quiz-page
/// evidence that used to carry it was retired (halheinrich/backgammon#349).
/// </summary>
internal static class HoldEvents
{
    /// <summary>Write <paramref name="events"/>, in order, one line each.</summary>
    internal static void Write(ITestOutputHelper output, IEnumerable<(DateTimeOffset At, string What)> events)
    {
        foreach (var (at, what) in events)
            output.WriteLine($"[hold {at:HH:mm:ss.fff}] {what}");
    }
}
