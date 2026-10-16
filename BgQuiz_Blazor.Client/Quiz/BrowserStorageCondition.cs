namespace BgQuiz_Blazor.Client.Quiz;

/// <summary>
/// The one fact that the browser refused a storage call this visit, and whether the user has
/// dismissed the notice about it. It begins once and holds; a reload starts afresh. Every
/// store that touches browser storage reports here, and so does the filter surface through
/// <see cref="FilterStorageRefusalSink"/>, so Home has one notice however many keys were
/// refused.
/// </summary>
internal sealed class BrowserStorageCondition
{
    /// <summary>The occurrence, or null before any refusal; compared by reference.</summary>
    public object? Occurrence { get; private set; }

    /// <summary>Whether the user has dismissed this occurrence's notice; it stays dismissed across navigation.</summary>
    public bool IsDismissed { get; private set; }

    /// <summary>Raised once, when the first refusal begins the condition.</summary>
    public event Action? Began;

    public void ReportRefused()
    {
        if (Occurrence is not null) return;
        Occurrence = new object();
        Began?.Invoke();
    }

    public void Dismiss() => IsDismissed = true;
}
