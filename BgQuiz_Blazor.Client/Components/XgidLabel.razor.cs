using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace BgQuiz_Blazor.Client.Components;

/// <summary>
/// Overlays a decision's XGID as real, selectable text in the board's
/// upper-right corner, with a one-click copy-to-clipboard button — the
/// HTML counterpart of the right-justified label the PDF / PPTX / PNG
/// exporters bake in (see <c>DiagramRenderer.AppendXgidLabel</c> and
/// <c>PptxBuilder.BuildXgidTextBox</c>). BgQuiz shows it as DOM text rather
/// than via <c>DiagramOptions.ShowXgid</c> (the raster-only baked-pixel
/// path) so the value stays selectable and copyable.
///
/// <para>
/// The host must give the surrounding board wrapper
/// <c>position: relative</c>; this component positions itself absolutely
/// within it (see the <c>.board-xgid</c> rule in <c>app.css</c>). An empty
/// <see cref="Xgid"/> renders nothing at all — no badge, no button.
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
