using System.Reflection;
using BgQuiz_Blazor.Client.Quiz;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using XgFilter_Lib.Filtering;

namespace BgQuiz_Blazor.Client.Components.Pages;

/// <summary>
/// Landing page: problem-set file selection, filter selection, and the
/// quiz-start gate.
///
/// <para>
/// The user picks one or more local <c>.xg</c> / <c>.xgp</c> files via an
/// <see cref="InputFile"/>. Each picked file is read out of its
/// <c>IBrowserFile</c> stream once and buffered into a <see cref="PickedFile"/>
/// (bytes + extension-bearing name) held in the per-app
/// <see cref="PickedProblemSet"/>; the bytes are parsed entirely in the browser
/// and never leave it. Buffering up front is what lets the source re-enumerate
/// on Restart.
/// </para>
///
/// <para>
/// Start is gated on two conditions: the filters Applied at least once and at
/// least one file picked. Both halves live in per-app scoped holders
/// (<see cref="AppliedFilter"/> and <see cref="PickedProblemSet"/>) rather than
/// transient component fields, so the gate survives in-app navigation — when the
/// page is re-instantiated on navigate-back it re-derives from the holders
/// instead of resetting. On Start the applied <see cref="FilterConfig"/> is
/// handed to the <see cref="QuizController"/>, whose source factory builds a
/// <see cref="WasmUploadedProblemSetSource"/> over the picked set, and the app
/// navigates to <c>/quiz</c>. Read failures and
/// <see cref="FilterConfig.Build"/> / source-construction failures are caught
/// and surfaced as a banner rather than faulting the WebAssembly app.
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

    private bool CanStart => AppliedFilter.IsApplied && ProblemSet.HasFiles;

    private async Task HandleFilesPickedAsync(InputFileChangeEventArgs e)
    {
        _startError = null;
        _noMatchNotice = null;
        try
        {
            var files = e.GetMultipleFiles(PickedFileLimits.MaxFileCount);
            var picked = new List<PickedFile>(files.Count);
            foreach (var file in files)
            {
                // Read the browser stream once, now, into memory: the source
                // re-enumerates (Restart) from these bytes, so it must not depend
                // on the IBrowserFile stream still being open later.
                using var ms = new MemoryStream();
                await file.OpenReadStream(PickedFileLimits.MaxFileBytes).CopyToAsync(ms);
                // file.Name carries the extension — required by the stream
                // iterator's DecisionId stamping (see XgFileStream).
                picked.Add(new PickedFile(file.Name, ms.ToArray()));
            }

            // The rendered summary derives from ProblemSet.Summary, so setting
            // the holder is all that's needed — no transient field to keep in
            // sync (that desynced on navigate-back, when Home re-instantiated).
            ProblemSet.Set(picked);
        }
        catch (Exception ex)
        {
            ProblemSet.Clear();
            _startError = $"Could not read the selected file(s): {ex.Message}";
        }
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
