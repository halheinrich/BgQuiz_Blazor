using BgGame_Lib;
using BgQuiz_Blazor.Client.Quiz;
using Microsoft.AspNetCore.Components;

namespace BgQuiz_Blazor.Client.Components.Pages;

/// <summary>
/// The stats-weighted mix builder hosted on <c>Home</c>: a view over the app-scoped
/// <see cref="MixDraft"/>, with every gesture routed through the draft's mutators and no
/// state of its own. The host mounts it exactly while the mix is in effect and composes
/// Start from the same fact, so the rows on screen are the rows that run; the user's
/// off-switch is the Settings page's one control, which unmounts this panel.
/// </summary>
public partial class MixPanel : ComponentBase
{
    /// <summary>Trigger the draft's once-per-visit hydration, so the stored mix is offered where the panel is.</summary>
    protected override Task OnInitializedAsync() => Draft.EnsureHydratedAsync();

    private static bool KindTakesParameter(QuizCategoryKind kind) => kind is
        QuizCategoryKind.SeenFewerThan or QuizCategoryKind.NotSeenInDays or
        QuizCategoryKind.AvgEquityLossOver or QuizCategoryKind.WrongRateOver;

    private static string ParameterLabel(QuizCategoryKind kind) => kind switch
    {
        QuizCategoryKind.SeenFewerThan => "Times",
        QuizCategoryKind.NotSeenInDays => "Days",
        QuizCategoryKind.AvgEquityLossOver => "Equity loss",
        QuizCategoryKind.WrongRateOver => "Percent wrong",
        _ => string.Empty,
    };

    private static string ParameterUnit(QuizCategoryKind kind) => kind switch
    {
        QuizCategoryKind.SeenFewerThan => "times",
        QuizCategoryKind.NotSeenInDays => "days",
        QuizCategoryKind.AvgEquityLossOver => "equity",
        QuizCategoryKind.WrongRateOver => "% of the time",
        _ => string.Empty,
    };

    /// <summary>
    /// The read half of the category select, the inverse of its write half: a token becomes
    /// a kind by searching the offered list for the one whose name it is, never by parsing,
    /// which would also accept an ordinal (halheinrich/backgammon#164). An unrecognised
    /// token is ignored.
    /// </summary>
    private Task HandleKindChangedAsync(int index, ChangeEventArgs e)
    {
        var token = e.Value?.ToString();

        foreach (var kind in MixDraft.CategoryKinds)
        {
            if (string.Equals(token, kind.ToString(), StringComparison.Ordinal))
            {
                return Draft.SetKindAsync(index, kind);
            }
        }

        return Task.CompletedTask;
    }
}
