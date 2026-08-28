using System.Net;
using System.Text;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace BgQuiz_Blazor.E2eTests;

/// <summary>
/// The gate's first line: the environment the rest of the suite runs in is the
/// product's (issues <c>halheinrich/backgammon#126</c>, <c>#127</c>). Every other
/// scenario here asks whether the app behaves; these ask whether the app under
/// them is the one that ships.
///
/// <para>
/// <b>What went wrong without it.</b> <c>App.razor</c> links one file under
/// <c>wwwroot/lib</c>, nothing in the repo restores that folder, and
/// <c>.gitignore</c> excluded it — so Bootstrap existed on one developer machine
/// and nowhere else. Umbrella CI (run 32534082312) built and served an unstyled
/// app for months: <c>.btn</c> at the user-agent's 21px, the quiz's trailing
/// cluster laid out as a block on its own line, no container gutters. Every test
/// stayed green, because every test asked about behaviour — which survives a
/// missing stylesheet — and the one geometric assertion in the suite (the
/// locator's, <c>#115</c>) was read as a layout bug rather than as the messenger
/// it was. A green test proves the test passed <i>in its environment</i>; it says
/// nothing about whether that environment is the product's.
/// </para>
///
/// <para>
/// <b>The page's own requests are the inventory.</b> The cold-load scenario names
/// no asset. It records what the browser actually fetched while the route loaded
/// and requires every one of those to have arrived — so an asset added tomorrow
/// is covered the day it is linked, and an asset that quietly stops being served
/// fails here rather than as a puzzling layout report three suites away. A
/// hand-written list of files would have to be maintained against the app, and a
/// list that drifts is exactly the SSOT defect #126 was: two statements of what
/// the app needs, one of them wrong. That also rules out naming any producer's
/// <c>_content/</c> path here: those belong to the submodules that ship them, and
/// this suite would only be restating a contract it does not own.
/// </para>
///
/// <para>
/// <b>Why three applied pins survive that.</b> A 200 says the bytes were served;
/// it cannot say the browser understood them or that anything on the page changed
/// as a result. So each of the three stylesheets the shell links is also read
/// back through <c>getComputedStyle</c>, at a value that stylesheet alone
/// produces — Bootstrap's own custom property, app.css's named container, and one
/// scoped rule out of the generated bundle. Three sheets, three pins, and no more
/// than that: every other scenario in this suite may then take the styled page as
/// given rather than re-proving it (see <c>SidebarCollapseTests</c>).
/// </para>
///
/// <para>
/// <b>A fourth pin, asking a different question.</b> The three above ask
/// whether the page is styled <i>at all</i>; the checkbox border below asks
/// whether one particular rule of app.css took effect. It earns its own
/// scenario because it is the rule whose entire purpose is that something be
/// visible (issue <c>halheinrich/backgammon#154</c>) — the one kind of rule
/// whose absence changes no behaviour, breaks no layout, and so would be
/// reported by nothing else in this suite.
/// </para>
///
/// <para>
/// <b>And one thing no visitor ever asks for.</b> The health endpoint Azure App
/// Service's probe will hit (<c>halheinrich/backgammon#24</c>) is a claim of the
/// same kind as the stylesheets — what the <i>artifact</i> serves, not how the
/// app behaves — so it is pinned here. It is the one thing in this class the
/// request sweep cannot reach, because the sweep is an inventory of what a page
/// fetched and nothing the app serves links it; it gets its own request.
/// </para>
/// </summary>
public sealed class EnvironmentFidelityTests : E2eTestBase
{
    public EnvironmentFidelityTests(PublishedAppFixture app, PlaywrightFixture playwright)
        : base(app, playwright) { }

    /// <summary>
    /// Wide enough to land in Bootstrap's <c>xl</c> tier (≥1200px), so the
    /// container width below is a single known number rather than a function of
    /// whatever the runner's default viewport happens to be.
    /// </summary>
    private const int DesktopWidth = 1280;

    private const int DesktopHeight = 800;

    /// <summary>Bootstrap 5's <c>.container</c> max-width in the <c>xl</c> tier.</summary>
    private const string ContainerWidthAtXl = "1140px";

    /// <summary>
    /// The container name <c>app.css</c> gives the layout's content area. Nothing
    /// else in the app — no producer stylesheet, no Bootstrap build, no user-agent
    /// default — declares a container name at all, so this string is that file's
    /// fingerprint and cannot be produced by any other sheet arriving in its place.
    /// </summary>
    private const string ContentContainerName = "app-content";

    /// <summary>
    /// The navigation panel's gradient, as Chromium resolves it (the authored
    /// <c>180deg</c> is the default and drops out of the computed value). Its rule
    /// lives in <c>MainLayout.razor.css</c>, so it reaches the browser only as
    /// <c>.sidebar[b-…]</c> inside the generated <c>BgQuiz_Blazor.styles.css</c>
    /// bundle — the third of the shell's three stylesheets, and the one with no
    /// file of its own in the repo to notice the absence of.
    /// </summary>
    private const string SidebarGradient =
        "linear-gradient(rgb(5, 39, 103) 0%, rgb(58, 6, 71) 70%)";

    /// <summary>
    /// The resting border <c>app.css</c> gives every <c>.form-check-input</c>
    /// (Bootstrap's gray-600), as Chromium reports it. Bootstrap's own value for
    /// that element is <c>#dee2e6</c> — 1.30:1 against this app's white page,
    /// where WCAG 2.1 SC 1.4.11 asks 3:1 of a control's boundary — so this string
    /// is that rule's fingerprint: no other sheet the app loads, and no user
    /// agent, borders a checkbox in it.
    /// </summary>
    private const string CheckboxRestingBorder = "rgb(108, 117, 125)";

    /// <summary>
    /// Bootstrap's <c>--bs-primary</c>, which fills a checked box and draws its
    /// border. The ruling on <c>halheinrich/backgammon#154</c> was that only the
    /// <i>empty</i> box changes, so this is the half that must stay Bootstrap's.
    /// </summary>
    private const string CheckboxCheckedFill = "rgb(13, 110, 253)";

    /// <summary>
    /// The path <c>Program.cs</c> maps the health endpoint at. It is also half of
    /// a contract with the deploy: the App Service site's <c>healthCheckPath</c>
    /// must name this same path, and the umbrella owns that half.
    /// </summary>
    private const string HealthPath = "/healthz";

    /// <summary>
    /// The entire body ASP.NET Core's default response writer emits for a passing
    /// <c>HealthCheckService</c> — the <c>HealthStatus</c> name, nothing around
    /// it, no trailing newline (measured against this app's own publish output on
    /// 2026-08-24). Spelled out rather than derived, so a host that starts
    /// formatting a response of its own has to come here and say so.
    /// </summary>
    private const string HealthyPayload = "Healthy";

    /// <summary>
    /// The routed page's content, which exists only once the WASM runtime has
    /// booted and rendered it: every routable page is <c>.Client</c>-side with
    /// <c>prerender: false</c>, so the layout's <c>article.content</c> is served
    /// empty and fills in afterwards. Route-independent by construction, which is
    /// what lets the cold-load scenario below take the route as data.
    /// </summary>
    private ILocator RoutedPageContent => Page.Locator("article.content > *").First;

    /// <summary>
    /// Every route a visitor can arrive at cold, with its own document request.
    /// <c>/quiz</c> is not among them — it cannot be arrived at cold (a visitor
    /// with no quiz running is bounced home), so it has its own scenario below,
    /// reached the way a user reaches it.
    /// </summary>
    [Theory]
    [InlineData("/")]
    [InlineData("/help")]
    [InlineData("/settings")]
    public async Task ColdRoute_ServesEveryAssetItAsksFor_AndLogsNothing(string route)
    {
        var load = new ColdLoadLog(Page);

        await Page.GotoAsync(BaseUrl + route);
        await Expect(RoutedPageContent).ToBeVisibleAsync();

        load.AssertClean(route);
    }

    /// <summary>
    /// The quiz page, at the end of the flow that is the only way to reach it: a
    /// folder pick, a filter, a Start. Worth its own scenario rather than a fourth
    /// row above, because it is the route that pulls the most in — the producers'
    /// diagram and folder-access assets among them — and the one whose arrival is
    /// a client-side navigation rather than a document request, so a missing asset
    /// here surfaces as a half-rendered board instead of a blank page.
    /// </summary>
    [Fact]
    public async Task TheQuizRoute_ServesEveryAssetItAsksFor_AndLogsNothing()
    {
        var load = new ColdLoadLog(Page);

        await BootHomeAsync();
        await PickFixtureAsync(CubeFixture);
        await ApplyFilterAsync();
        await StartQuizAsync();
        await Expect(HitOverlaySvg).ToBeVisibleAsync();

        load.AssertClean("/quiz (through the pick → apply → start flow)");
    }

    /// <summary>
    /// The three stylesheets the shell links, each read back at a value only that
    /// stylesheet produces. One scenario, on a cold <c>/</c>, because the three
    /// facts fail together as often as they fail apart — they are one claim: the
    /// page the rest of this suite measures is styled.
    /// </summary>
    [Fact]
    public async Task EveryLinkedStylesheet_IsAppliedOnAColdHomePage()
    {
        await Page.SetViewportSizeAsync(DesktopWidth, DesktopHeight);
        await BootHomeAsync();

        // --- Bootstrap ------------------------------------------------------
        // A real layout value on a real element: at a viewport of 1280 the
        // .container resolves to its xl width, where an unstyled div computes
        // `none`. Deliberately not the button's height — the symptom CI showed,
        // but a value the user agent also has an opinion about, so a wrong number
        // there reads as ambiguous.
        var container = Page.Locator("div.container").First;
        await Expect(container).ToBeVisibleAsync();
        await Expect(container).ToHaveCSSAsync("max-width", ContainerWidthAtXl);

        // ...and it is Bootstrap's stylesheet doing it. --bs-* custom properties
        // are declared by Bootstrap's :root block and by nothing else this app
        // loads, so a non-empty value here is the file's fingerprint.
        var bootstrapVariable = await Page.EvaluateAsync<string>(
            "() => getComputedStyle(document.documentElement)"
            + ".getPropertyValue('--bs-primary').trim()");

        Assert.False(
            string.IsNullOrEmpty(bootstrapVariable),
            "--bs-primary is empty: Bootstrap's stylesheet did not load. It is vendored at "
            + "BgQuiz_Blazor/wwwroot/lib/bootstrap/dist/css/bootstrap.min.css and tracked by a "
            + "negation in .gitignore — check that the file is present and still re-included.");

        // --- app.css --------------------------------------------------------
        // The layout's content area is a NAMED inline-size query container, and
        // app.css is the only thing anywhere in the served app that names one.
        await Expect(Page.Locator("article.content"))
            .ToHaveCSSAsync("container-name", ContentContainerName);

        // --- BgQuiz_Blazor.styles.css (the scoped bundle) --------------------
        // MainLayout.razor.css's `.sidebar` gradient, which reaches the browser
        // only as an attribute-scoped `.sidebar[b-…]` rule inside the generated
        // bundle. The b-* hash itself is deliberately not pinned: it is derived
        // per build, and the fact worth pinning is that the rule took effect.
        await Expect(Page.Locator(".sidebar"))
            .ToHaveCSSAsync("background-image", SidebarGradient);
    }

    /// <summary>
    /// The darkened checkbox border, read back where a user meets it (tester
    /// report, issue <c>halheinrich/backgammon#154</c>: the box outlining the
    /// check was hard to see). Gated rather than assumed for the reason the rest
    /// of this class exists — a rule that only makes something visible is
    /// invisible to every test that asks about behaviour.
    ///
    /// <para>
    /// <b>Both states, because the fix is as much about what it left alone.</b>
    /// The complaint was the empty box and the ruling was that the filled one
    /// keeps Bootstrap's styling. app.css's rule is <c>.form-check-input</c> at
    /// (0,1,0) and Bootstrap's is <c>.form-check-input:checked</c> at (0,2,0), so
    /// the checked half holds by specificity alone — a claim about a cascade,
    /// which a computed value can settle and reading either file cannot.
    /// </para>
    ///
    /// <para>
    /// <b>Why each state is selected rather than named.</b> <c>/settings</c>
    /// renders a two-way radio group, so exactly one control on it is checked and
    /// at least one is not, whatever the stored preferences happen to be — both
    /// locators are non-empty by construction. Naming a particular toggle would
    /// instead pin this style claim to that toggle's default value, and fail here
    /// on the day a product decision moved it.
    /// </para>
    /// </summary>
    [Fact]
    public async Task TheCheckboxBorder_IsAppliedOnASettingsPage()
    {
        await Page.GotoAsync(BaseUrl + "/settings");
        await Expect(RoutedPageContent).ToBeVisibleAsync();

        await Expect(Page.Locator(".form-check-input:not(:checked)").First)
            .ToHaveCSSAsync("border-color", CheckboxRestingBorder);

        var filled = Page.Locator(".form-check-input:checked").First;
        await Expect(filled).ToHaveCSSAsync("border-color", CheckboxCheckedFill);
        await Expect(filled).ToHaveCSSAsync("background-color", CheckboxCheckedFill);
    }

    /// <summary>
    /// The liveness endpoint, asked the way its consumer asks it: a cold,
    /// browser-free <c>GET</c> against the published artifact
    /// (<c>halheinrich/backgammon#24</c>). Deliberately <see cref="HttpClient"/>
    /// and not <see cref="Page"/> — the consumer is App Service's prober, roughly
    /// once a minute per instance, and driving it through a browser would test a
    /// client this endpoint never has, carrying headers and a cookie jar the
    /// probe does not send.
    ///
    /// <para>
    /// <b>Both halves are load-bearing.</b> The status code is what App Service
    /// grades on, and it is the half that fails if the mapping goes: an unmatched
    /// path is re-executed to the not-found page, so a missing endpoint reports
    /// as <c>404</c> rather than as a connection error (mutation-checked by
    /// pointing this at a path the host does not map). The body is what says a
    /// real health-checks endpoint answered rather than something else that
    /// happens to return 200 there — a static file dropped into <c>wwwroot</c>,
    /// or some future catch-all — which is the whole of the ruling behind #24's
    /// app half.
    /// </para>
    /// </summary>
    [Fact]
    public async Task TheHealthEndpoint_AnswersAColdProbeWith200Healthy()
    {
        using var probe = new HttpClient();

        using var response = await probe.GetAsync(BaseUrl + HealthPath);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(HealthyPayload, await response.Content.ReadAsStringAsync());
    }

    /// <summary>
    /// What the browser reported while a route loaded: every response that was
    /// not served, every request that never got one, and everything the page
    /// logged as an error.
    ///
    /// <para>
    /// Subscribed in the constructor and never unsubscribed, so a recorder is
    /// created before the navigation it is recording and lives as long as the
    /// scenario. Playwright raises these on its own reader loop, so the lists are
    /// guarded — the assertion runs on the test's thread.
    /// </para>
    ///
    /// <para>
    /// <b>Both request channels are needed and they do not overlap.</b> A server
    /// that answers with a 404 raises <c>Response</c>; a request that never
    /// completes at all — DNS, connection reset, a blocked route — raises
    /// <c>RequestFailed</c> and never raises <c>Response</c>.
    /// <c>PageError</c> is likewise not a duplicate of the console: an exception
    /// that escapes to the top level is reported there whether or not anything
    /// logged it.
    /// </para>
    ///
    /// <para>
    /// <b>An empty 200 counts as unserved, and that is the load-bearing part.</b>
    /// A status check alone would have been vacuous against the very defect this
    /// class exists for: <c>MapStaticAssets</c> serves its endpoints from a
    /// manifest built at publish time, so an asset the manifest names but the
    /// disk does not have comes back <c>200 OK</c> with <c>Content-Length: 0</c>
    /// and no content type — measured against this app's own publish output on
    /// 2026-08-21, and the same shape <c>PublishedAppFixture</c> warns about when
    /// the content root is wrong. A stylesheet that arrives empty is a stylesheet
    /// that did not arrive. The header is only consulted when the server sent it
    /// (everything Kestrel serves from the publish output does); a response
    /// without one is left alone rather than guessed at.
    /// </para>
    /// </summary>
    private sealed class ColdLoadLog
    {
        private readonly Lock _gate = new();
        private readonly List<string> _resourceFailures = [];
        private readonly List<string> _pageErrors = [];

        public ColdLoadLog(IPage page)
        {
            page.Response += (_, response) =>
            {
                if (response.Status >= 400)
                    Record(_resourceFailures, $"HTTP {response.Status} — {response.Url}");
                else if (response.Status == 200
                    && response.Headers.TryGetValue("content-length", out string? length)
                    && length == "0")
                {
                    Record(
                        _resourceFailures,
                        $"HTTP 200 but empty — {response.Url}");
                }
            };
            page.RequestFailed += (_, request) =>
                Record(_resourceFailures, $"{request.Failure} — {request.Url}");
            page.Console += (_, message) =>
            {
                if (message.Type == "error")
                    Record(_pageErrors, $"console.error — {message.Text}");
            };
            page.PageError += (_, error) => Record(_pageErrors, $"uncaught — {error}");
        }

        /// <summary>
        /// Fail unless the route asked for nothing it did not get and said nothing
        /// went wrong. The message carries every entry: a fidelity failure is
        /// read by someone who was not watching the run, and "1 request failed"
        /// would send them back to reproduce it.
        /// </summary>
        public void AssertClean(string route)
        {
            List<string> failures;
            List<string> errors;
            lock (_gate)
            {
                failures = [.. _resourceFailures];
                errors = [.. _pageErrors];
            }

            if (failures.Count == 0 && errors.Count == 0) return;

            var report = new StringBuilder()
                .AppendLine($"{route} did not load cleanly.")
                .AppendLine()
                .AppendLine($"Unserved requests ({failures.Count}):");
            foreach (string failure in failures) report.AppendLine($"  {failure}");
            report.AppendLine().AppendLine($"Page errors ({errors.Count}):");
            foreach (string error in errors) report.AppendLine($"  {error}");

            Assert.Fail(report.ToString());
        }

        private void Record(List<string> sink, string entry)
        {
            lock (_gate) sink.Add(entry);
        }
    }
}
