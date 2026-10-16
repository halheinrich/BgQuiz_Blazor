using BgQuiz_Blazor.Client.Quiz;
using Microsoft.AspNetCore.Components;

namespace BgQuiz_Blazor.Client.Components.Pages;

/// <summary>
/// The mid-quiz scoreboard: Done's score panel and breakdown over the live run, reached from
/// the quiz page's Show stats. Read-only, so the round trip leaves the run as it was. A visit
/// with no quiz in progress bounces to Home, and with the quiz finished to Done, as the quiz
/// page's own guards do.
/// </summary>
public partial class Stats : ComponentBase
{
    /// <inheritdoc />
    protected override void OnInitialized()
    {
        if (!App.HasStarted)
        {
            Nav.NavigateTo("/", replace: true);
            return;
        }

        if (App.IsFinished)
        {
            Nav.NavigateTo("/done", replace: true);
        }
    }
}
