using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace BgQuiz_Blazor.Client.Components;

/// <summary>
/// A decision's XGID as real, selectable text with a one-click
/// copy-to-clipboard button — the HTML counterpart of the right-justified
/// label the PDF / PPTX / PNG exporters bake in (see
/// <c>DiagramRenderer.AppendXgidLabel</c> and
/// <c>PptxBuilder.BuildXgidTextBox</c>). BgQuiz shows it as DOM text rather
/// than via <c>DiagramOptions.ShowXgid</c> (the raster-only baked-pixel
/// path) so the value stays selectable and copyable.
///
/// <para>
/// <b>It is an in-flow badge and positions nothing</b> (the
/// <c>.xgid-label</c> rule in <c>app.css</c>): it takes the space the host
/// gives it, wherever that is. It used to overlay the board's upper-right
/// corner, absolutely positioned inside the producer's Overlay slot against a
/// <c>position: relative</c> wrapper; <c>SPEC-quiz-view.md</c> §4's one-home
/// ruling (issue <c>halheinrich/backgammon#98</c>) moved it off the canvas to
/// the quiz page's bottom row, so neither the absolute positioning nor the
/// host's positioning context exists any more. An empty <see cref="Xgid"/>
/// renders nothing at all — no badge, no button — so a host may bind it
/// unconditionally, and a layout that must survive that (an <c>ms-auto</c>,
/// say) has to live on something other than this component.
/// </para>
///
/// <para>
/// Copying uses the browser's <c>navigator.clipboard.writeText</c> through
/// <see cref="IJSRuntime"/>, matching how the app already calls browser
/// globals directly (e.g. <c>localStorage.*</c> in the filter panel) rather
/// than shipping a bespoke JS module. The button flips to a transient
/// "Copied" confirmation.
/// </para>
/// </summary>
public partial class XgidLabel : ComponentBase
{
    /// <summary>How long the post-copy "Copied" confirmation stays shown.</summary>
    private const int CopiedFeedbackMs = 1500;

    /// <summary>
    /// The copy button's accessible name, and its tooltip — one string for both,
    /// because they name the same control to two audiences.
    /// </summary>
    private const string CopyLabel = "Copy XGID to clipboard";

    /// <summary>The post-copy confirmation, in the same two places.</summary>
    private const string CopiedLabel = "Copied";

    /// <summary>
    /// The XGID to display. Empty (the default) hides the label entirely —
    /// callers need not branch, they can bind it unconditionally.
    /// </summary>
    [Parameter, EditorRequired]
    public string Xgid { get; set; } = string.Empty;

    [Inject]
    private IJSRuntime JS { get; set; } = default!;

    private bool _copied;

    private async Task CopyAsync()
    {
        await JS.InvokeVoidAsync("navigator.clipboard.writeText", Xgid);

        // Show the confirmation immediately, then revert after a beat. The
        // implicit re-render when this handler completes flips the label back.
        _copied = true;
        StateHasChanged();
        await Task.Delay(CopiedFeedbackMs);
        _copied = false;
    }
}
