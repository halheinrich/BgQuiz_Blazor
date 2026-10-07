using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
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
/// The copy button's copy and its confirmation are <see cref="XgidCopy"/>'s,
/// the one statement the "⋯" list's Copy XGID item (<see cref="TailMenu"/>)
/// shares: the browser's <c>navigator.clipboard.writeText</c> through
/// <see cref="IJSRuntime"/>, then, once that resolves, the button flips for a
/// moment to "Copied" — or, where the browser refused the write, to the
/// failure — and back. Its accessible name and tooltip are one string, the
/// control's name or the result showing, because they name the same control
/// to two audiences.
/// </para>
/// </summary>
public partial class XgidLabel : ComponentBase
{
    /// <summary>
    /// The XGID to display. Empty (the default) hides the label entirely —
    /// callers need not branch, they can bind it unconditionally.
    /// </summary>
    [Parameter, EditorRequired]
    public string Xgid { get; set; } = string.Empty;

    [Inject]
    private IJSRuntime JS { get; set; } = default!;

    [Inject]
    private TimeProvider Clock { get; set; } = default!;

    [Inject]
    private ILogger<XgidCopy> Logger { get; set; } = default!;

    /// <summary>This button's copies and the result it is showing.</summary>
    private XgidCopy _copy = default!;

    /// <summary>The button's name now: the result showing, else its own.</summary>
    private string Name => _copy.ShowingLabel ?? XgidCopy.CopyLabel;

    /// <summary>Give this button its copies, over the injected browser, clock and log.</summary>
    protected override void OnInitialized() => _copy = new XgidCopy(JS, Clock, Logger);

    private Task CopyAsync() => _copy.CopyAsync(Xgid, StateHasChanged);
}
