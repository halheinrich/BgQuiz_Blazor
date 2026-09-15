using Microsoft.AspNetCore.Components;

namespace BgQuiz_Blazor.Client.Components;

/// <summary>
/// The <c>Problem folder:</c> caption followed by the folder it names — the
/// caption's single owner, rendered by every surface that names the picked
/// folder (issue <c>halheinrich/backgammon#199</c>).
///
/// <para>
/// <b>Two rules, two owners.</b> How a folder's name <i>displays</i> — the
/// quoted <c>'MyMatches'</c> — is <c>PickedProblemFolder.DisplayName</c>, and
/// <c>PickedProblemFolder.Summary</c> composes from it. How the caption
/// <i>reads</i> is this component. Hosts choose which of the holder's two
/// descriptions the caption frames: <c>Home</c> passes <c>Summary</c>, whose
/// file count matters at setup; <c>Done</c> and <c>Stats</c> pass the bare
/// <c>DisplayName</c> in front of the breakdown's heading, where the count
/// would describe the folder in hand rather than the run being scored.
/// </para>
///
/// <para>
/// <b>An inline run that positions nothing.</b> It takes the host's text
/// styling and layout, so the setup page's muted small print and the stats
/// pages' heading line each look like their own surroundings. An empty
/// <see cref="Description"/> — nothing is picked — renders nothing at all, so a
/// host may bind it unconditionally, the way it binds <see cref="XgidLabel"/>.
/// </para>
/// </summary>
public partial class ProblemFolderLabel : ComponentBase
{
    /// <summary>
    /// The folder as this surface describes it, rendered after the caption
    /// verbatim — one of <c>PickedProblemFolder</c>'s descriptions, never text
    /// the host formats itself. Null or empty hides the whole label.
    /// </summary>
    [Parameter, EditorRequired]
    public string? Description { get; set; }
}
