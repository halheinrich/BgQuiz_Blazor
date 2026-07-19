using System.Reflection;
using BgQuiz_Blazor.Client.Quiz;
using Microsoft.AspNetCore.Components;
using XgFilter_Lib.Filtering;

namespace BgQuiz_Blazor.Client.Components.Pages;

/// <summary>
/// Landing page: problem-set folder selection, filter selection, and the
/// quiz-start gate.
///
/// <para>
/// The user picks a local folder with one "Choose folder…" gesture, served by
/// whichever mechanism the browser offers (probed at pick time through
/// <see cref="IFolderAccess"/>): the File System Access directory picker where
/// available — which can also grant the writable handle that enables lifetime
/// stats — or the hidden <c>webkitdirectory</c> input elsewhere (read-only; the
/// quiz runs without stats). Either way the folder's top-level <c>.xg</c> /
/// <c>.xgp</c> files are buffered into <see cref="PickedFile"/>s (bytes +
/// extension-bearing names) held in the per-app
/// <see cref="PickedProblemFolder"/>; the bytes are parsed entirely in the
/// browser and never leave it. Buffering up front is what lets the source
/// re-enumerate on Restart. The pick-time
/// <see cref="StatsSaveCapability"/> verdict rides on the holder and drives
/// this page's stats status notice; the stats lifecycle itself is not this
/// page's concern — the controller binds the stats context at Start.
/// </para>
///
/// <para>
/// Start is gated on two conditions: the filters Applied at least once and a
/// folder picked with at least one problem file. Both halves live in per-app
/// scoped holders (<see cref="AppliedFilter"/> and
/// <see cref="PickedProblemFolder"/>) rather than transient component fields,
/// so the gate survives in-app navigation — when the page is re-instantiated
/// on navigate-back it re-derives from the holders instead of resetting. On
/// Start the applied <see cref="FilterConfig"/> is handed to the
/// <see cref="QuizController"/>, whose source factory builds a
/// <see cref="WasmUploadedProblemSetSource"/> over the picked files, and the
/// app navigates to <c>/quiz</c>. Pick failures and
/// <see cref="FilterConfig.Build"/> / source-construction failures are caught
/// and surfaced as banners rather than faulting the WebAssembly app.
/// </para>
///
/// <para>
/// A third, ungated toggle — "Shuffle order" — lives alongside the gate in the
/// per-app <see cref="ShuffleOption"/> holder. It is presentation-only (order,
/// not admission), so it plays no part in <c>CanStart</c>: the source factory
/// reads it live at Start to decide whether to wrap the picked set in a
/// <c>ShuffledProblemSetSource</c>.
/// </para>
/// </summary>
public partial class Home : ComponentBase
{
    /// <summary>
    /// App version surfaced on the landing page, resolved once from the client
    /// assembly's <see cref="AssemblyInformationalVersionAttribute"/> — declared
    /// via <c>&lt;Version&gt;</c> in the csproj, the single source of truth (no
    /// hardcoded literal). Rendered as <c>v{AppVersion}</c>. Falls back to the
    /// assembly version, then a placeholder, if the attribute is ever absent.
    /// </summary>
    internal static string AppVersion { get; } =
        typeof(Home).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion
        ?? typeof(Home).Assembly.GetName().Version?.ToString()
        ?? "unknown";

    private string? _startError;

    /// <summary>
    /// Sibling of <see cref="_startError"/> for pick failures — an unexpected
    /// browser error or a folder past the <see cref="PickedFileLimits"/> caps.
    /// A per-visit failure banner (assertive), like the start error.
    /// </summary>
    private string? _pickError;

    /// <summary>
    /// Set when a completed pick yielded no top-level <c>.xg</c> / <c>.xgp</c>
    /// files — an outcome (polite notice), not a failure: the holder stays
    /// clear and the Start gate stays disabled. Per-visit, so a component field.
    /// </summary>
    private bool _emptyFolderNotice;

    /// <summary>
    /// The hidden <c>webkitdirectory</c> input the fallback mechanism drives.
    /// The JS module reads its FileList directly (for <c>webkitRelativePath</c>);
    /// this reference is only ever handed across <see cref="IFolderAccess"/>.
    /// </summary>
    private ElementReference _fallbackInput;

    /// <summary>
    /// Sibling of <see cref="_startError"/> for the empty-result <i>outcome</i> —
    /// distinct from the failure the error banner reports. A successful
    /// <see cref="QuizController.StartAsync"/> that leaves the controller already
    /// <see cref="QuizController.IsFinished"/> means the source admitted no
    /// showable problem; rather than bounce silently through <c>/quiz</c> to a
    /// <c>0/0</c> <c>/done</c>, the page stays on <c>/</c> and surfaces this as a
    /// neutral status message (see <see cref="StartQuizAsync"/>). Genuinely
    /// per-visit page state, so a component field — see the holder-vs-field note
    /// in INSTRUCTIONS' Pitfalls.
    /// </summary>
    private string? _noMatchNotice;

    /// <summary>
    /// Set once, on a boot that finds the <see cref="QuizLiveMarker"/> present
    /// with no live quiz in the (freshly-booted) controller — i.e. a full reload
    /// silently reset a quiz that was underway. Drives the polite reset notice.
    /// A per-visit outcome flag, so a component field like the two banners above.
    /// </summary>
    private bool _showReloadNotice;

    private bool CanStart => AppliedFilter.IsApplied && Folder.HasFiles;

    /// <summary>
    /// On boot, surface the reload-reset notice when the marker says a quiz was
    /// live but the controller has none — the signature of a full reload having
    /// rebooted the runtime out from under an in-progress quiz. Then clear the
    /// marker so the notice shows once.
    ///
    /// <para>
    /// The <see cref="QuizController.HasStarted"/> guard is what distinguishes a
    /// reload from in-app navigation back to <c>Home</c> mid-quiz: the latter
    /// keeps the same per-tab controller (quiz still live), so the marker is set
    /// <i>and</i> <c>HasStarted</c> is true — no notice, and the marker is left
    /// in place for a genuine later reload.
    /// </para>
    /// </summary>
    protected override async Task OnInitializedAsync()
    {
        if (await Marker.WasLiveAsync() && !Controller.HasStarted)
        {
            _showReloadNotice = true;
            await Marker.ClearAsync();
        }
    }

    /// <summary>
    /// The one "pick a folder" gesture. Probes the mechanism at pick time:
    /// with File System Access, the whole pick (picker, permission,
    /// enumeration, buffering) completes inside <see cref="IFolderAccess"/>;
    /// without it, this click only opens the hidden <c>webkitdirectory</c>
    /// input's picker and the pick arrives later via that input's own change
    /// event (<see cref="HandleFallbackPickedAsync"/>).
    /// </summary>
    private async Task PickFolderAsync()
    {
        ClearPickNotices();
        try
        {
            if (await FolderAccess.SupportsDirectoryPickerAsync())
            {
                ApplyPickOutcome(await FolderAccess.PickFolderAsync());
            }
            else
            {
                await FolderAccess.TriggerFallbackPickerAsync(_fallbackInput);
            }
        }
        catch (Exception ex)
        {
            Folder.Clear();
            _pickError = ex.Message;
        }
    }

    /// <summary>
    /// The fallback pick landing: the hidden input's FileList is collected and
    /// filtered by the JS module (top-level <c>.xg</c> / <c>.xgp</c> only).
    /// Capability is always <see cref="StatsSaveCapability.BrowserUnsupported"/>
    /// on this mechanism — no writable handle exists.
    /// </summary>
    private async Task HandleFallbackPickedAsync(ChangeEventArgs _)
    {
        ClearPickNotices();
        try
        {
            ApplyPickOutcome(await FolderAccess.CollectFallbackAsync(_fallbackInput));
        }
        catch (Exception ex)
        {
            Folder.Clear();
            _pickError = ex.Message;
        }
    }

    /// <summary>
    /// The shared landing for both mechanisms' outcomes. A cancelled picker
    /// changes nothing (no notice — the user changed their mind); an empty
    /// folder surfaces the polite outcome notice and leaves the holder clear;
    /// otherwise the holder takes the pick, and the rendered summary + stats
    /// status notice derive from it (no transient field to keep in sync — that
    /// desynced on navigate-back, when Home re-instantiated).
    /// </summary>
    private void ApplyPickOutcome(FolderPickOutcome outcome)
    {
        if (outcome.Cancelled) return;

        if (outcome.Files.Count == 0)
        {
            Folder.Clear();
            _emptyFolderNotice = true;
            return;
        }

        Folder.Set(outcome.DirectoryName, outcome.Files, outcome.Capability);
    }

    private async Task ClearPickedFolderAsync()
    {
        // Discards the pick; the Start gate re-disables by construction
        // (HasFiles goes false) and the holder-derived summary and stats
        // notice disappear. Safe to call mid-quiz: the picked files are read
        // only at Start time (the source factory reads Folder.Files in
        // StartAsync) and the JS side clears the PICKED slot only — a running
        // quiz's stats context lives in the ACTIVE slot, bound at Start, so
        // recording continues untouched until the next Start re-binds.
        Folder.Clear();
        await FolderAccess.ClearPickedAsync();
        ClearPickNotices();
    }

    private void ClearPickNotices()
    {
        _pickError = null;
        _emptyFolderNotice = false;
        _startError = null;
        _noMatchNotice = null;
    }

    private void HandleFilterConfigApplied(FilterConfig cfg)
    {
        // The user clicked Apply: record the deliberate applied state on the
        // scoped holder so it survives navigate-back (not a transient field).
        AppliedFilter.Set(cfg);
        _startError = null;
        _noMatchNotice = null;
    }

    private void HandleFiltersDirty()
    {
        // Any filter edit re-gates Start: a half-edited, un-applied set must
        // clear the applied state, not just a local flag.
        AppliedFilter.Clear();
    }

    private void HandleShuffleToggled(ChangeEventArgs e)
    {
        // A checkbox has no half-edited state, so the toggle is recorded live —
        // no applied/dirty gate the way AppliedFilter needs one.
        ShuffleOption.Set(e.Value is true);
    }

    private async Task StartQuizAsync()
    {
        if (AppliedFilter.Config is not { } cfg) return;
        _startError = null;
        _noMatchNotice = null;
        try
        {
            await Controller.StartAsync(cfg);

            // StartAsync already advanced to the first showable problem, so an
            // immediately-finished controller means the source yielded nothing
            // the quiz could present. Two indistinguishable causes flip this —
            // zero filter matches, or every match auto-skipped as a pass
            // position — so stay on / and surface a neutral outcome notice
            // rather than navigating into a 0/0 /quiz → /done bounce with no
            // hint of why.
            if (Controller.IsFinished)
            {
                _noMatchNotice =
                    "No quiz problems matched these filters — adjust the filters or pick different files.";
                return;
            }

            // A live quiz is starting: record it so a mid-quiz full reload (which
            // reboots the WASM runtime and silently discards this quiz) is
            // acknowledged on the next boot rather than dropping the user on a
            // fresh Home. Set only past the empty-result guard — the no-match
            // path above stays on Home with no live quiz to lose.
            await Marker.MarkLiveAsync();

            Nav.NavigateTo("/quiz");
        }
        catch (Exception ex)
        {
            // FilterConfig.Build() validation failure, source construction
            // failure, etc. Surface to the user rather than faulting the app.
            _startError = ex.Message;
        }
    }
}
