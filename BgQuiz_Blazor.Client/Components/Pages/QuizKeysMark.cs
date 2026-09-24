namespace BgQuiz_Blazor.Client.Components.Pages;

/// <summary>
/// The readiness mark of the quiz page's keyboard module
/// (<c>wwwroot/js/quizKeys.js</c>): an attribute the module sets on the
/// document element once its Space listener is attached, and removes when it
/// detaches (issue <c>halheinrich/backgammon#198</c>).
///
/// <para>
/// <b>Why it exists.</b> The page imports the module on its first render and
/// attaches only once the import lands, so on a cold fetch of the module there
/// is a window in which the page is fully rendered and a Space press is inert
/// by design. That is an accepted cost for a user — the buttons work
/// throughout — but a browser test cannot make a key press a retrying
/// assertion, so it needs something to wait on before it presses. Rendered
/// controls prove nothing about the listener; this mark is set by the very
/// code that adds it, immediately after adding it.
/// </para>
///
/// <para>
/// <b>One spelling, three readers.</b> The page hands the name to the module's
/// <c>attach</c>, the way it hands over the callback's name, so the module
/// never spells it; the bUnit fixture pins that hand-over; and the e2e suite,
/// which deliberately references no app project, compiles this one file by
/// link and waits on the same constant. That link is the suite's one
/// exception to its independent-literal rule, and it is safe where importing
/// copy is not: this is a handshake, not an expectation about what a user
/// reads, and every way of breaking it fails loudly — a renamed mark changes
/// both sides at once, an emptied one makes the module's
/// <c>setAttribute</c> throw and the test's selector invalid, and a module
/// that stops setting it times the wait out. Nothing else may be added to this
/// file for that reason: whatever it holds is compiled into the test assembly.
/// </para>
/// </summary>
internal static class QuizKeysMark
{
    /// <summary>
    /// The attribute's name. Presence is the whole signal; the value is empty.
    /// </summary>
    internal const string AttachedAttribute = "data-quiz-keys-attached";
}
