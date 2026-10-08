using Microsoft.Playwright;
using Xunit.Abstractions;
using static Microsoft.Playwright.Assertions;

namespace BgQuiz_Blazor.E2eTests;

/// <summary>
/// <see cref="E2eTestBase.ExpectPageRenderedAsync"/> and the route landmarks
/// it waits for (<see cref="E2eTestBase.AppRoute"/>): what says a page has
/// rendered, as distinct from its URL having arrived
/// (halheinrich/backgammon#372, halheinrich/backgammon#374).
/// </summary>
public sealed class PageRenderedTests : E2eTestBase
{
    private readonly ITestOutputHelper _output;

    public PageRenderedTests(PublishedAppFixture app, PlaywrightFixture playwright, ITestOutputHelper output)
        : base(app, playwright)
    {
        _output = output;
    }

    /// <summary>The two gestures that navigate in-app: a link the browser follows, and the app's own <c>NavigationManager</c>.</summary>
    public enum Gesture
    {
        /// <summary>The persistent navigation's Settings link, from Home.</summary>
        NavigationLink,

        /// <summary>Start Quiz, which navigates from Home by <c>NavigationManager.NavigateTo</c>.</summary>
        StartQuiz,
    }

    /// <summary>
    /// <b>Page rendered is not met while the page being left still shows,
    /// though the URL already names the destination; URL arrived is.</b>
    ///
    /// <para>
    /// The checkpoint is the state halheinrich/backgammon#372's click met on
    /// production: the navigation requested, its URL up, the old page still on
    /// screen. It is held, not raced: the page's request for the destination is
    /// routed and kept until the scenario lets it go, so nothing about the page
    /// can change while the evidence is taken. Enhanced navigation pushes the
    /// URL before it fetches the page, so with that fetch held the URL names
    /// the destination, the page being left stays, and the destination's
    /// landmark cannot exist.
    /// </para>
    ///
    /// <para>
    /// At the checkpoint, <see cref="E2eTestBase.ExpectPageRenderedAsync"/>
    /// is issued first, then <see cref="E2eTestBase.ExpectUrlAsync"/>. The URL
    /// wait completes, which is why it cannot stand in for the other: an
    /// action after it would land on the page being left. The page-rendered
    /// wait, issued before it, has not completed once it and the evidence
    /// reads have. Only after the fetch is let through does it complete, and
    /// then the destination's landmark is up and the old page's is gone.
    /// Every fact is printed, pass or fail.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData(Gesture.NavigationLink)]
    [InlineData(Gesture.StartQuiz)]
    public async Task PageRendered_IsNotMetWhileTheOldPageShows_ThoughTheUrlAlreadyIs(Gesture gesture)
    {
        var (destination, step) = gesture switch
        {
            Gesture.NavigationLink => (AppRoute.Settings, "the navigation's Settings link"),
            _ => (AppRoute.Quiz, "Start Quiz"),
        };
        var evidence = new List<string>();
        void Note(string line)
        {
            evidence.Add(line);
            _output.WriteLine("[checkpoint] " + line);
        }

        await BootHomeAsync();
        if (gesture == Gesture.StartQuiz)
        {
            await PickFixtureAsync(CubeFixture);
            await ApplyFilterAsync();
        }

        var requested = new TaskCompletionSource<IRequest>(TaskCreationOptions.RunContinuationsAsynchronously);
        var released = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await Page.RouteAsync(
            url => new Uri(url).AbsolutePath == destination.Path,
            async route =>
            {
                requested.TrySetResult(route.Request);
                await released.Task;
                await route.ContinueAsync();
            });

        try
        {
            if (gesture == Gesture.NavigationLink)
                await Page.GetByRole(AriaRole.Link, new() { Name = ExpectedText.SettingsNavLink, Exact = true }).ClickAsync();
            else
                await StartButton.ClickAsync();

            // The checkpoint: the page has asked for the destination, and the
            // request is held.
            var request = await requested.Task.WaitAsync(TimeSpan.FromMilliseconds(PlaywrightFixture.DefaultTimeoutMs));
            Note($"after {step}: the page requested {new Uri(request.Url).AbsolutePath} "
                + $"({request.ResourceType}, accept: {await request.HeaderValueAsync("accept") ?? "none"}); the request is held");

            var rendered = ExpectPageRenderedAsync(destination);
            await ExpectUrlAsync(destination);
            Note($"URL arrived completed: the address is {Page.Url}");
            await Expect(AppRoute.Home.LandmarkOn(Page)).ToBeVisibleAsync();
            Note($"the page being left still shows: Home's landmark, {AppRoute.Home.Landmark}, is visible");
            await Expect(destination.LandmarkOn(Page)).ToHaveCountAsync(0);
            Note($"the destination has not rendered: its landmark, {destination.Landmark}, is absent");
            Note(rendered.IsCompleted
                ? $"page rendered had completed ({rendered.Status})"
                : "page rendered, issued before URL arrived, had not completed");
            Assert.False(rendered.IsCompleted,
                "page rendered completed while the old page still showed and the destination's landmark was absent: "
                + "it does not wait for the page to render");

            released.TrySetResult();
            await rendered;
            Note("let through: page rendered completed");
            await Expect(AppRoute.Home.LandmarkOn(Page)).ToHaveCountAsync(0);
            Note("Home's landmark is gone");
        }
        finally
        {
            released.TrySetResult();
            if (evidence.Count == 0) _output.WriteLine("[checkpoint] the page never requested the destination");
        }
    }

    /// <summary>
    /// <b>Each landmark is on its own page and on no other.</b> One journey
    /// through every route, by the gestures a user makes: the navigation
    /// links, Start, Show stats, Back to quiz, an answer and Continue. On each
    /// page, once it has rendered, every other route's landmark is absent.
    /// That is the property that keeps a wait for a landmark from being
    /// satisfied by the page a navigation is leaving. The absences follow the
    /// page's own landmark, so none of them can pass on a page that has not
    /// rendered yet.
    /// </summary>
    [Fact]
    public async Task EachRoutesLandmark_IsOnItsOwnPage_AndOnNoOther()
    {
        // Wide enough that Show stats is a button of the row, not an item behind its "⋯".
        await Page.SetViewportSizeAsync(1600, 900);
        await BootHomeAsync();
        await ExpectOnlyTheLandmarkOfAsync(AppRoute.Home);

        await NavLink(ExpectedText.SettingsNavLink).ClickAsync();
        await ExpectOnlyTheLandmarkOfAsync(AppRoute.Settings);

        await NavLink(ExpectedText.HelpNavLink).ClickAsync();
        await ExpectOnlyTheLandmarkOfAsync(AppRoute.Help);

        await NavLink(ExpectedText.HomeNavLink).ClickAsync();
        await ExpectOnlyTheLandmarkOfAsync(AppRoute.Home);

        await PickFixtureAsync(CubeFixture);
        await ApplyFilterAsync();
        await StartQuizAsync();
        await ExpectOnlyTheLandmarkOfAsync(AppRoute.Quiz);

        await Page.GetByRole(AriaRole.Button, new() { Name = ExpectedText.ShowStatsButton, Exact = true }).ClickAsync();
        await ExpectOnlyTheLandmarkOfAsync(AppRoute.Stats);

        await Page.GetByRole(AriaRole.Button, new() { Name = ExpectedText.BackToQuizButton }).ClickAsync();
        await ExpectOnlyTheLandmarkOfAsync(AppRoute.Quiz);

        await AnswerCubeNoDoubleAsync();
        await ContinueToDoneAsync();
        await ExpectOnlyTheLandmarkOfAsync(AppRoute.Done);
    }

    private ILocator NavLink(string name) => Page.GetByRole(AriaRole.Link, new() { Name = name, Exact = true });

    /// <summary>
    /// The page at <paramref name="route"/> has rendered and shows its own
    /// landmark once, and no other route's.
    /// </summary>
    private async Task ExpectOnlyTheLandmarkOfAsync(AppRoute route)
    {
        await ExpectPageRenderedAsync(route);
        await Expect(route.LandmarkOn(Page)).ToHaveCountAsync(1);
        foreach (var other in AppRoute.All.Where(r => r != route))
            await Expect(other.LandmarkOn(Page)).ToHaveCountAsync(0);
    }
}
