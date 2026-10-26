using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace BgQuiz_Blazor.Client.Components;

/// <summary>
/// What a copy to the clipboard came to, as the control that made it confirms it.
/// </summary>
internal enum ClipboardCopyResult
{
    /// <summary>The clipboard holds the value.</summary>
    Copied,

    /// <summary>The browser refused the write, so the clipboard does not.</summary>
    Failed,
}

/// <summary>
/// The three things a copy control is named by, which each use supplies for its
/// own value: <paramref name="Copy"/>, the control's own name;
/// <paramref name="Copied"/>, the confirmation of a copy the clipboard took;
/// and <paramref name="Failed"/>, the report of a copy the browser refused —
/// never a claim that it landed.
/// </summary>
internal sealed record ClipboardCopyLabels(string Copy, string Copied, string Failed);

/// <summary>
/// <b>Copying a value to the clipboard and confirming it — the one statement of
/// both</b>, which every control that offers a copy shares: the XGID badge's
/// copy button (<see cref="XgidLabel"/>), the "⋯" list's Copy XGID item
/// (<see cref="TailMenu"/>), so the item does and says what the button does
/// (issue <c>halheinrich/backgammon#334</c>), and the feedback address's copy
/// button (<see cref="FeedbackAddress"/>, halheinrich/backgammon#267). Each
/// control holds its own instance, because each confirms on itself, and
/// supplies its own <see cref="ClipboardCopyLabels"/>; what a copy is, what it
/// reports and for how long are stated here once.
///
/// <para>
/// <b>Truthful in both directions.</b> The confirmation is decided by the
/// write's own outcome, after it resolves: <see cref="ClipboardCopyResult.Copied"/>
/// only once the clipboard has taken the value, and
/// <see cref="ClipboardCopyResult.Failed"/> where the browser refused it — a
/// denied permission, a document without focus, an insecure context with no
/// clipboard at all, each of which reaches Blazor as a
/// <see cref="JSException"/>. A refusal is reported, logged and never
/// thrown. <see cref="JSException"/> only, so a fault that is not the
/// browser's refusal still surfaces.
/// </para>
///
/// <para>
/// <b>For a moment, then back to the control's own name.</b> The result
/// shows for <see cref="ConfirmationTime"/>, measured on the injected clock
/// so a test can stand on it.
/// </para>
///
/// <para>
/// <b>Only the latest attempt owns what the control shows</b> (the one
/// ownership policy, halheinrich/backgammon#334). Each copy is an attempt, and
/// the moment a newer one starts, every earlier attempt is obsolete. Starting
/// an attempt retires whatever result the control is showing, so the control
/// is its own until the new attempt's write resolves; and an obsolete attempt
/// neither publishes its result nor expires anything, at either of its two
/// awaits. So a write that resolves after a newer copy began
/// — however late, and whether the newer one is still waiting, showing, or
/// already back to the control's name — shows nothing and clears nothing, and
/// the result on the control is always the latest attempt's, for exactly its
/// own moment. (Ownership is checked rather than overlap prevented: the user's
/// second press is a real, newer request and gets a real, newer answer.) The
/// browser's refusal of an obsolete write is still logged, since it happened.
/// </para>
/// </summary>
internal sealed class ClipboardCopy(
    IJSRuntime js, TimeProvider clock, ILogger<ClipboardCopy> logger, ClipboardCopyLabels labels)
{
    /// <summary>How long a copy's result shows on the control that made it.</summary>
    internal static readonly TimeSpan ConfirmationTime = TimeSpan.FromMilliseconds(1500);

    /// <summary>Counts attempts; the attempt holding the latest count is the one that owns what shows.</summary>
    private int _copies;

    /// <summary>What this control is named by, as its use supplied them.</summary>
    public ClipboardCopyLabels Labels { get; } = labels;

    /// <summary>
    /// The result the control is showing now, or <see langword="null"/> when
    /// it shows its own name — before any copy, and once a copy's moment has
    /// passed.
    /// </summary>
    public ClipboardCopyResult? Showing { get; private set; }

    /// <summary>What the control is named by while <see cref="Showing"/> a result, else <see langword="null"/>.</summary>
    public string? ShowingLabel => Showing switch
    {
        ClipboardCopyResult.Copied => Labels.Copied,
        ClipboardCopyResult.Failed => Labels.Failed,
        _ => null,
    };

    /// <summary>
    /// The stylesheet's mark for the result showing (<c>app.css</c>: the
    /// ticked clipboard, or the crossed one), else <see langword="null"/>.
    /// </summary>
    public string? ShowingClass => Showing switch
    {
        ClipboardCopyResult.Copied => "is-copied",
        ClipboardCopyResult.Failed => "is-failed",
        _ => null,
    };

    /// <summary>
    /// Write <paramref name="value"/> to the clipboard, then show what came of
    /// it for <see cref="ConfirmationTime"/>: <paramref name="render"/> is
    /// asked to draw the result once the write has resolved, and the control's
    /// own name is back when this returns (the caller's handler completing
    /// renders it) — unless a newer copy has started meanwhile, which then owns
    /// what shows, and this attempt publishes and clears nothing. Starting it
    /// first retires any result still showing, rendering the control back to
    /// its own name until this write resolves. Never throws for the browser's
    /// refusal.
    /// </summary>
    public async Task CopyAsync(string value, Action render)
    {
        var attempt = ++_copies;
        if (Showing is not null)
        {
            // The previous attempt's result is retired with it: it says nothing
            // about this copy, which has not resolved yet.
            Showing = null;
            render();
        }

        var result = await TryWriteAsync(value) ? ClipboardCopyResult.Copied : ClipboardCopyResult.Failed;
        if (!IsCurrent(attempt)) return;

        Showing = result;
        render();
        await Task.Delay(ConfirmationTime, clock);
        if (IsCurrent(attempt)) Showing = null;
    }

    /// <summary>Whether <paramref name="attempt"/> is still the latest copy — the only one that may publish or expire.</summary>
    private bool IsCurrent(int attempt) => attempt == _copies;

    private async Task<bool> TryWriteAsync(string value)
    {
        try
        {
            await js.InvokeVoidAsync("navigator.clipboard.writeText", value);
            return true;
        }
        catch (JSException e)
        {
            logger.LogWarning(e, "The browser refused a clipboard write ({Control}).", Labels.Copy);
            return false;
        }
    }
}
