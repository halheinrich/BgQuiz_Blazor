using BgQuiz_Blazor.Client.Quiz;

namespace BgQuiz_Blazor.Tests;

/// <summary>
/// <see cref="BrowserStorageCondition"/>, the one fact that browser storage
/// has been refused this visit (issue <c>halheinrich/backgammon#360</c>):
/// nothing until a refusal, then one occurrence for the rest of the visit,
/// whoever reports and however often — which is what lets a dismissal of
/// <c>Home</c>'s notice survive a remounted panel's fresh report.
/// </summary>
public class BrowserStorageConditionTests
{
    [Fact]
    public void BeforeAnyRefusal_ThereIsNoOccurrence()
    {
        Assert.Null(new BrowserStorageCondition().Occurrence);
    }

    [Fact]
    public void TheFirstRefusal_BeginsTheCondition_AndSaysSoOnce()
    {
        var condition = new BrowserStorageCondition();
        var began = 0;
        condition.Began += () => began++;

        condition.ReportRefused();

        Assert.NotNull(condition.Occurrence);
        Assert.Equal(1, began);
    }

    [Fact]
    public void EveryLaterRefusal_IsTheSameOccurrence_AndBeginsNothing()
    {
        // A remounted filter panel reports again, and so does every store that
        // is refused after the first: the same condition, so the same token —
        // by identity, which is how QuizNoticeDismissal compares it.
        var condition = new BrowserStorageCondition();
        var began = 0;
        condition.Began += () => began++;
        condition.ReportRefused();
        var occurrence = condition.Occurrence;

        condition.ReportRefused();
        condition.ReportRefused();

        Assert.Same(occurrence, condition.Occurrence);
        Assert.Equal(1, began);
    }

    [Fact]
    public void ADismissalOfTheOccurrence_HoldsAcrossLaterRefusals_AndANewAppShowsItFresh()
    {
        // The holder's half of SPEC-notices.md §2 for this notice: the
        // dismissal is keyed on the occurrence, which later reports keep; a
        // reload is a new app, whose condition is a new occurrence.
        var notices = new QuizNoticeDismissal();
        var condition = new BrowserStorageCondition();
        condition.ReportRefused();
        notices.Dismiss(QuizNotice.StorageUnavailable, condition.Occurrence!);

        condition.ReportRefused();
        Assert.True(notices.IsDismissed(QuizNotice.StorageUnavailable, condition.Occurrence));

        var afterAReload = new BrowserStorageCondition();
        afterAReload.ReportRefused();
        Assert.False(notices.IsDismissed(QuizNotice.StorageUnavailable, afterAReload.Occurrence));
    }
}
