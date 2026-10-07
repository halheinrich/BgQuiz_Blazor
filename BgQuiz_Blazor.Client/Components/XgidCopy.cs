using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace BgQuiz_Blazor.Client.Components;

/// <summary>
/// What a copy of an XGID came to, as the control that made it confirms it.
/// </summary>
internal enum XgidCopyResult
{
    /// <summary>The clipboard holds the XGID.</summary>
    Copied,

    /// <summary>The browser refused the write, so the clipboard does not.</summary>
    Failed,
}

/// <summary>
/// <b>Copying an XGID and confirming it — the one statement of both</b>, which
/// the two controls that offer the copy share: the badge's copy button
/// (<see cref="XgidLabel"/>) and the "⋯" list's Copy XGID item
/// (<see cref="TailMenu"/>), so the item does and says what the button does
/// (issue <c>halheinrich/backgammon#334</c>). Each control holds its own
/// instance, because each confirms on itself; what a copy is, what it
/// reports and for how long are stated here once.
///
/// <para>
/// <b>Truthful in both directions.</b> The confirmation is decided by the
/// write's own outcome, after it resolves: <see cref="XgidCopyResult.Copied"/>
/// only once the clipboard has taken the value, and
/// <see cref="XgidCopyResult.Failed"/> where the browser refused it — a
/// denied permission, a document without focus, an insecure context with no
/// clipboard at all, each of which reaches Blazor as a
/// <see cref="JSException"/>. A refusal is reported, logged and never
/// thrown: before this the button's handler let it escape unhandled, and the
/// item claimed nothing either way. <see cref="JSException"/> only, so a
/// fault that is not the browser's refusal still surfaces.
/// </para>
///
/// <para>
/// <b>For a moment, then back to the control's own name.</b> The result
/// shows for <see cref="ConfirmationTime"/>, measured on the injected clock
/// so a test can stand on it. A second copy while the first is still showing
/// shows its own result for its own full moment: the earlier copy's moment
/// ending does not cut the later one short.
/// </para>
/// </summary>
internal sealed class XgidCopy(IJSRuntime js, TimeProvider clock, ILogger<XgidCopy> logger)
{
    /// <summary>
    /// The copy control's name — the badge button's accessible name and
    /// tooltip, and the "⋯" list item's text, since the two are one control
    /// in two places.
    /// </summary>
    internal const string CopyLabel = "Copy XGID to clipboard";

    /// <summary>The confirmation of a copy the clipboard took.</summary>
    internal const string CopiedLabel = "Copied";

    /// <summary>The report of a copy the browser refused — never a claim that it landed.</summary>
    internal const string FailedLabel = "Couldn't copy the XGID";

    /// <summary>How long a copy's result shows on the control that made it.</summary>
    internal static readonly TimeSpan ConfirmationTime = TimeSpan.FromMilliseconds(1500);

    /// <summary>Counts copies, so only the latest one's moment ending clears what shows.</summary>
    private int _copies;

    /// <summary>
    /// The result the control is showing now, or <see langword="null"/> when
    /// it shows its own name — before any copy, and once a copy's moment has
    /// passed.
    /// </summary>
    public XgidCopyResult? Showing { get; private set; }

    /// <summary>What the control is named by while <see cref="Showing"/> a result, else <see langword="null"/>.</summary>
    public string? ShowingLabel => Showing switch
    {
        XgidCopyResult.Copied => CopiedLabel,
        XgidCopyResult.Failed => FailedLabel,
        _ => null,
    };

    /// <summary>
    /// The stylesheet's mark for the result showing (<c>app.css</c>: the
    /// ticked clipboard, or the crossed one), else <see langword="null"/>.
    /// </summary>
    public string? ShowingClass => Showing switch
    {
        XgidCopyResult.Copied => "is-copied",
        XgidCopyResult.Failed => "is-failed",
        _ => null,
    };

    /// <summary>
    /// Write <paramref name="xgid"/> to the clipboard, then show what came of
    /// it for <see cref="ConfirmationTime"/>: <paramref name="render"/> is
    /// asked to draw the result once the write has resolved, and the control's
    /// own name is back when this returns (the caller's handler completing
    /// renders it). Never throws for the browser's refusal.
    /// </summary>
    public async Task CopyAsync(string xgid, Action render)
    {
        var copy = ++_copies;
        Showing = await TryWriteAsync(xgid) ? XgidCopyResult.Copied : XgidCopyResult.Failed;
        render();
        await Task.Delay(ConfirmationTime, clock);
        if (copy == _copies) Showing = null;
    }

    private async Task<bool> TryWriteAsync(string xgid)
    {
        try
        {
            await js.InvokeVoidAsync("navigator.clipboard.writeText", xgid);
            return true;
        }
        catch (JSException e)
        {
            logger.LogWarning(e, "The browser refused to write the XGID to the clipboard.");
            return false;
        }
    }
}
