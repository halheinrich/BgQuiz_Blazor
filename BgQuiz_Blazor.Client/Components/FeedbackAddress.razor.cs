using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace BgQuiz_Blazor.Client.Components;

/// <summary>
/// The beta feedback address, <see cref="AppInfo.FeedbackAddress"/>, as
/// selectable text with a one-click copy button (issue
/// <c>halheinrich/backgammon#267</c>): where a machine has no handler for email
/// links, the <c>mailto:</c> link beside it does nothing at all, and the
/// address on the page is the tester's way to write anyway.
///
/// <para>
/// The copy and its confirmation are <see cref="ClipboardCopy"/>'s, under this
/// control's own labels, so it reports truthfully, as the XGID's copy button
/// does: "Copied" only once the clipboard has taken the address, the refusal
/// where the browser declined, for a moment, then back.
/// </para>
/// </summary>
public partial class FeedbackAddress : ComponentBase
{
    /// <summary>The copy button's labels: its name, its confirmation, its refusal.</summary>
    internal static ClipboardCopyLabels CopyLabels { get; } =
        new(Copy: "Copy the feedback address", Copied: "Copied", Failed: "Couldn't copy the address");

    [Inject]
    private IJSRuntime JS { get; set; } = default!;

    [Inject]
    private TimeProvider Clock { get; set; } = default!;

    [Inject]
    private ILogger<ClipboardCopy> Logger { get; set; } = default!;

    /// <summary>This button's copies and the result it is showing.</summary>
    private ClipboardCopy _copy = default!;

    /// <summary>The button's name now: the result showing, else its own.</summary>
    private string Name => _copy.ShowingLabel ?? CopyLabels.Copy;

    /// <summary>Give this button its copies, over the injected browser, clock and log.</summary>
    protected override void OnInitialized() => _copy = new ClipboardCopy(JS, Clock, Logger, CopyLabels);

    private Task CopyAsync() => _copy.CopyAsync(AppInfo.FeedbackAddress, StateHasChanged);
}
