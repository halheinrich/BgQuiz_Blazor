using System.Globalization;
using AngleSharp.Dom;
using BgQuiz_Blazor.Client.Components;
using BgQuiz_Blazor.Client.Quiz;
using BgUiPrimitives_Razor;
using BgUiPrimitives_Razor.TestSupport;
using Bunit;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

namespace BgQuiz_Blazor.Tests;

/// <summary>
/// The fixture <see cref="DecisionNotes"/>' gesture suites share —
/// <c>DecisionNotesPlacementTests</c> (moving, issue
/// <c>halheinrich/backgammon#344</c>) and <c>DecisionNotesResizeTests</c>
/// (resizing, issue <c>halheinrich/backgammon#414</c>): the placement module
/// planned with a capture that succeeds, storage planned so every write is a
/// test's statement, and the stage reported by hand.
///
/// <para>
/// <b>The browser is played by hand here.</b> The module's stage report is
/// <see cref="DecisionNotes.OnStageMeasured"/> called directly, and pointer
/// capture is a planned call. What a real pointer, real keys and real storage
/// do with all of it is the e2e suite's (<c>NotesPlacement*Tests</c>,
/// <c>NotesSize*Tests</c>); the arithmetic behind every number is
/// <c>NotesStageTests</c>'.
/// </para>
/// </summary>
public abstract class DecisionNotesGestureTestBase : BunitContext
{
    protected const string Note = "Hit loose here; the gammons are worth it.";

    /// <summary>
    /// The stage reported in these tests: a 1280 × 800 area, the 16 px
    /// clearance, a 576 × 200 overlay whose title bar ends 56 px down it, and
    /// text at a 24 px line height. Its space is 1248 across and 768 down, its
    /// travel 672 and 568, so the overlay is drawn centred at (352, 300), its
    /// bottom-right corner at (928, 500); an eighth of the travel is 84 across
    /// and 71 down. Its minimum height is 56 + 3 × 24 = 128.
    /// </summary>
    protected static readonly double[] Desktop = [1280, 800, 16, 576, 200, 56, 24];

    /// <summary>A phone: the overlay fills the width less the clearance, so there is no horizontal travel.</summary>
    protected static readonly double[] Phone = [375, 812, 16, 343, 220, 56, 24];

    protected const string Centred = "--notes-left: 352px; --notes-top: 300px";

    protected const long Mouse = 1;

    protected readonly BunitJSModuleInterop Module;

    protected readonly BunitJSModuleInterop Handle;

    protected DecisionNotesGestureTestBase()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Module = JSInterop.SetupModule(DecisionNotes.ModulePath);
        Module.Mode = JSRuntimeMode.Loose;
        Handle = Module.SetupModule("watch", _ => true);
        Handle.Mode = JSRuntimeMode.Loose;
        Handle.Setup<bool>("capture", _ => true).SetResult(true);
        Services.AddReportingBrowserStorage();
        Services.AddScoped<NotesPreferencesStore>();

        // Storage is planned for every test: what is written, and that nothing
        // is, are these suites' statements. Notes() declares the store's one
        // load; each test declares the writes it expects, so an undeclared
        // write fails where it is made and Verify reports a missing one.
        Plan = BrowserStoragePlan.On(JSInterop);
    }

    protected BrowserStoragePlan Plan { get; }

    /// <summary>The preferences the store's load finds; nothing stored unless a test says otherwise.</summary>
    private BrowserStorageReadAnswer _stored = BrowserStorageReadAnswer.Absent;

    protected void StageStored(string json) => _stored = BrowserStorageReadAnswer.Stored(json);

    /// <summary>
    /// The payload the store writes for these preferences — the pinned format
    /// of <c>NotesPreferencesStoreTests</c>, each number in its shortest
    /// round-trip form, an unset axis <c>null</c> — so a test can state the
    /// written values from the ruling's arithmetic.
    /// </summary>
    protected static string Json(double? horizontal, double? vertical, double? width = null, double? height = null) =>
        $$"""{"horizontal":{{Number(horizontal)}},"vertical":{{Number(vertical)}},"width":{{Number(width)}},"height":{{Number(height)}}}""";

    private static string Number(double? value) =>
        value is double number ? number.ToString("R", CultureInfo.InvariantCulture) : "null";

    /// <summary>Expect the gesture or Reset that writes <paramref name="json"/> — or removes the entry, for <see langword="null"/>.</summary>
    protected void ExpectWritten(string? json)
    {
        if (json is null)
            Plan.ExpectRemove(BrowserStorageArea.Local, NotesPreferencesStore.StorageKey, BrowserStorageWriteAnswer.Succeeded);
        else
            Plan.ExpectWrite(BrowserStorageArea.Local, NotesPreferencesStore.StorageKey, json, BrowserStorageWriteAnswer.Succeeded);
    }

    /// <summary>The notes, rendered, with the store's one load of this app declared.</summary>
    protected IRenderedComponent<DecisionNotes> Notes()
    {
        Plan.ExpectRead(BrowserStorageArea.Local, NotesPreferencesStore.StorageKey, _stored);
        return Render<DecisionNotes>(p => p.Add(c => c.Comment, Note));
    }

    protected static async Task OpenAsync(IRenderedComponent<DecisionNotes> cut) =>
        await cut.Find("button.decision-notes-toggle").ClickAsync(new());

    protected static Task ReportAsync(IRenderedComponent<DecisionNotes> cut, double[] stage) =>
        cut.InvokeAsync(() => cut.Instance.OnStageMeasured(stage[0], stage[1], stage[2], stage[3], stage[4], stage[5], stage[6]));

    /// <summary>
    /// The element-reference ids of the title bar and the resize handle, read
    /// off the render that opened the overlay — the one render bUnit prints
    /// them on — so a test can say which element a capture was asked of.
    /// </summary>
    protected (string? TitleBar, string? ResizeHandle) References { get; private set; }

    /// <summary>Open the notes and report the desktop stage: the overlay is placed.</summary>
    protected async Task<IRenderedComponent<DecisionNotes>> PlacedAsync(double[]? stage = null)
    {
        var cut = Notes();
        await OpenAsync(cut);
        References = (
            cut.Find(".decision-notes-header").GetAttribute("blazor:elementreference"),
            cut.Find(".decision-notes-resize").GetAttribute("blazor:elementreference"));
        await ReportAsync(cut, stage ?? Desktop);
        return cut;
    }

    protected static IElement Dialog(IRenderedComponent<DecisionNotes> cut) => cut.Find("dialog");

    protected static string? Drawn(IRenderedComponent<DecisionNotes> cut) => Dialog(cut).GetAttribute("style");

    protected static IElement Heading(IRenderedComponent<DecisionNotes> cut) => cut.Find(".decision-notes-header h2");

    protected static PointerEventArgs Pointer(double x, double y, long id = Mouse, long button = 0, bool primary = true) =>
        new() { PointerId = id, ClientX = x, ClientY = y, Button = button, IsPrimary = primary, PointerType = "mouse" };

    /// <summary>Press on the title bar's heading — a spot on the handle that is no button — at (400, 330).</summary>
    protected static Task GrabAsync(IRenderedComponent<DecisionNotes> cut, long id = Mouse) =>
        Heading(cut).TriggerEventAsync("onpointerdown", Pointer(400, 330, id));

    protected static Task MoveToAsync(IRenderedComponent<DecisionNotes> cut, double x, double y, long id = Mouse) =>
        cut.Find(".decision-notes-header").TriggerEventAsync("onpointermove", Pointer(x, y, id));

    protected static Task ReleaseAtAsync(IRenderedComponent<DecisionNotes> cut, double x, double y, long id = Mouse) =>
        cut.Find(".decision-notes-header").TriggerEventAsync("onpointerup", Pointer(x, y, id));

    protected int Captures => Handle.Invocations["capture"].Count;

    protected static IElement StepButton(IRenderedComponent<DecisionNotes> cut, string name) =>
        cut.FindAll(".decision-notes-steps button").Single(b =>
            (b.GetAttribute("aria-label") ?? b.TextContent.Trim()) == name);

    protected static IElement AdjustButton(IRenderedComponent<DecisionNotes> cut) =>
        cut.Find(".decision-notes-tools button:not(.btn-close)");

    protected static async Task OpenAdjustAsync(IRenderedComponent<DecisionNotes> cut) =>
        await AdjustButton(cut).ClickAsync(new());
}
