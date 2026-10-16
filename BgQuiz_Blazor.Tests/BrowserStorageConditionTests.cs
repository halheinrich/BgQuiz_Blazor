using BgQuiz_Blazor.Client.Quiz;

namespace BgQuiz_Blazor.Tests;

/// <summary>The one fact that the browser refused a storage call this visit: begins once, then holds.</summary>
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
    public void ANewApp_HasItsOwnOccurrence()
    {
        var first = new BrowserStorageCondition();
        first.ReportRefused();
        var afterAReload = new BrowserStorageCondition();
        afterAReload.ReportRefused();
        Assert.NotSame(first.Occurrence, afterAReload.Occurrence);
    }
}
