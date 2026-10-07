using AngleSharp.Dom;
using BgQuiz_Blazor.Client.Components;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace BgQuiz_Blazor.Tests;

/// <summary>
/// Copying an XGID and confirming it (<see cref="XgidCopy"/>; issue
/// <c>halheinrich/backgammon#334</c>, Hal: "Report success and failure
/// truthfully: never claim success before the clipboard write succeeds"),
/// through both controls that offer it — the badge's copy button
/// (<see cref="XgidLabel"/>) and the "⋯" list's Copy XGID item
/// (<see cref="TailMenu"/>), which confirms on the toggle. Each fact is
/// pinned on both, because the two are one statement: a control that drifted
/// from it fails here by name.
///
/// <para>
/// The clipboard write is held open by the JSInterop double until the test
/// resolves or refuses it, and the confirmation's moment runs on a
/// <see cref="ManualTimeProvider"/>, so "only after the write" and "for the
/// moment, then back" are each observed at the instant they claim.
/// </para>
/// </summary>
public class XgidCopyTests : BunitContext
{
    private const string Xgid = "XGID=-b----E-C---eE---c-e----B-:0:0:1:52:0:0:0:0:10";

    private readonly ManualTimeProvider _clock = new();
    private readonly RecordingLogger<XgidCopy> _log = new();
    private readonly JSRuntimeInvocationHandler _write;

    public XgidCopyTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        var module = JSInterop.SetupModule(TailMenu.ModulePath);
        module.Mode = JSRuntimeMode.Loose;
        module.SetupModule("attach", _ => true).Mode = JSRuntimeMode.Loose;
        _write = JSInterop.SetupVoid("navigator.clipboard.writeText", _ => true);
        Services.AddSingleton<TimeProvider>(_clock);
        Services.AddSingleton<ILogger<XgidCopy>>(_log);
    }

    /// <summary>The two places a copy is offered, by what the test drives and reads.</summary>
    public static TheoryData<string> Controls() => ["badge", "list item"];

    /// <summary>
    /// One control under test: <see cref="Press"/> starts a copy through it and
    /// returns the handler still running; <see cref="Name"/> and
    /// <see cref="Mark"/> read the control that confirms — the badge's button,
    /// or the toggle the list hangs from; <see cref="WaitFor"/> retries an
    /// assertion until a render makes it hold, since what follows the write
    /// runs on the renderer once the write resolves. It is for what a render
    /// shows, never for whether a handler has finished — see
    /// <see cref="FinishedAsync"/>.
    /// </summary>
    private sealed record Control(
        string OwnName, Func<Task<Task>> Press, Func<string> Name, Func<string?> Mark, Action<Action> WaitFor);

    /// <summary>
    /// Wait for a copy's handler — the click's dispatch task, which
    /// <see cref="Control.Press"/> returns — to finish, bounded by bUnit's wait
    /// timeout so a handler that never ends fails the test rather than stalls
    /// it. Awaited, and never watched with <see cref="Control.WaitFor"/>: that
    /// retries on renders, and the one render a handler's completion causes
    /// (the component re-rendering after its event handler — an obsolete
    /// attempt renders nothing itself) lands before the dispatch task is marked
    /// complete, measured 2026-10-07 — so a render-driven check of the task's
    /// completion passes only when its first try happens to run late
    /// (halheinrich/backgammon#334).
    /// </summary>
    private static Task FinishedAsync(Task handler) => handler.WaitAsync(DefaultWaitTimeout);

    private Control Render(string control) => control switch
    {
        "badge" => Badge(),
        "list item" => ListItem(),
        _ => throw new ArgumentOutOfRangeException(nameof(control)),
    };

    private Control Badge()
    {
        var cut = Render<XgidLabel>(p => p.Add(c => c.Xgid, Xgid));
        IElement Button() => cut.Find(".xgid-label-copy");
        return new Control(
            XgidCopy.CopyLabel,
            () => Task.FromResult(Button().ClickAsync(new())),
            () =>
            {
                // The name is one string in two places; both are read so a
                // control that confirmed in only one of them is caught.
                var title = Button().GetAttribute("title")!;
                Assert.Equal(title, Button().QuerySelector(".visually-hidden")!.TextContent);
                return title;
            },
            () => Button().ClassList.FirstOrDefault(c => c.StartsWith("is-", StringComparison.Ordinal)),
            assertion => cut.WaitForAssertion(assertion));
    }

    private Control ListItem()
    {
        var cut = Render<TailMenu>(p => p
            .Add(c => c.Xgid, Xgid)
            .Add(c => c.SourceFile, "synthetic-match-2026-04-12.xg")
            .Add(c => c.Game, 2)
            .Add(c => c.MoveNumber, 4)
            .Add(c => c.Actions, Array.Empty<TailMenuAction>()));
        IElement Toggle() => cut.Find(".tail-menu > button");
        return new Control(
            TailMenu.ToggleName,
            async () =>
            {
                await Toggle().ClickAsync(new());
                var item = cut.FindAll("[role=menu] [role=menuitem]")[0];
                Assert.Equal(XgidCopy.CopyLabel, item.TextContent.Trim());
                return item.ClickAsync(new());
            },
            () =>
            {
                var label = Toggle().GetAttribute("aria-label")!;
                Assert.Equal(label, Toggle().GetAttribute("title"));
                return label;
            },
            () => Toggle().QuerySelector(".xgid-copy-mark")?.ClassList
                .FirstOrDefault(c => c.StartsWith("is-", StringComparison.Ordinal)),
            assertion => cut.WaitForAssertion(assertion));
    }

    [Theory]
    [MemberData(nameof(Controls))]
    public async Task ACopy_ConfirmsOnlyOnceTheWriteResolves_ForItsMoment_ThenTheControlIsItsOwnAgain(string control)
    {
        var copy = Render(control);

        var handler = await copy.Press();

        // The write is out and has not resolved: nothing is claimed yet.
        Assert.Equal(Xgid, _write.Invocations.Single().Arguments[0]);
        Assert.Equal(copy.OwnName, copy.Name());
        Assert.Null(copy.Mark());

        _write.SetVoidResult();

        copy.WaitFor(() => Assert.Equal(XgidCopy.CopiedLabel, copy.Name()));
        Assert.Equal("is-copied", copy.Mark());

        _clock.Advance(XgidCopy.ConfirmationTime - TimeSpan.FromTicks(1));
        Assert.Equal(XgidCopy.CopiedLabel, copy.Name());

        _clock.Advance(TimeSpan.FromTicks(1));
        await FinishedAsync(handler);

        Assert.Equal(copy.OwnName, copy.Name());
        Assert.Null(copy.Mark());
        Assert.Empty(_log.Entries);
    }

    [Theory]
    [MemberData(nameof(Controls))]
    public async Task ARefusedWrite_IsReportedAsAFailure_NeverAsCopied_AndThrowsNowhere(string control)
    {
        var copy = Render(control);

        var handler = await copy.Press();
        Assert.Equal(copy.OwnName, copy.Name());

        // What a denied permission, or a page without focus, raises in Blazor.
        _write.SetException(new JSException("NotAllowedError: Write permission denied."));

        copy.WaitFor(() => Assert.Equal(XgidCopy.FailedLabel, copy.Name()));
        Assert.Equal("is-failed", copy.Mark());

        _clock.Advance(XgidCopy.ConfirmationTime);
        await FinishedAsync(handler); // completes, rather than rethrowing the refusal

        Assert.Equal(copy.OwnName, copy.Name());
        Assert.Null(copy.Mark());
        var entry = Assert.Single(_log.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.IsType<JSException>(entry.Exception);
    }

    [Fact]
    public async Task ASecondCopy_ShowsItsOwnResultForItsWholeMoment()
    {
        // The first copy's moment ending must not cut the second's short — and
        // the second's result, not the first's, is what shows.
        var copy = Badge();
        var first = await copy.Press();
        _write.SetVoidResult();
        copy.WaitFor(() => Assert.Equal(XgidCopy.CopiedLabel, copy.Name()));
        _clock.Advance(XgidCopy.ConfirmationTime / 2);

        var refusal = JSInterop.SetupVoid("navigator.clipboard.writeText", _ => true);
        var second = await copy.Press();
        refusal.SetException(new JSException("NotAllowedError: Document is not focused."));
        copy.WaitFor(() => Assert.Equal(XgidCopy.FailedLabel, copy.Name()));

        _clock.Advance(XgidCopy.ConfirmationTime / 2);
        await FinishedAsync(first);
        Assert.Equal(XgidCopy.FailedLabel, copy.Name());

        _clock.Advance(XgidCopy.ConfirmationTime / 2);
        await FinishedAsync(second);
        Assert.Equal(XgidCopy.CopyLabel, copy.Name());
    }

    [Theory]
    [MemberData(nameof(Controls))]
    public async Task AnObsoleteCopysLateResult_AfterTheNewerCopysMomentHasEnded_ShowsNothing(string control)
    {
        // The ordering halheinrich/backgammon#334's review found: copy A's write
        // waits; copy B starts, lands, shows and its moment ends; then A's write
        // fails. Before the one ownership policy A published its failure over
        // the control's own name and — its expiry being stale — kept it there.
        var copy = Render(control);
        var first = await copy.Press();                       // A: its write held open by _write

        var newer = JSInterop.SetupVoid("navigator.clipboard.writeText", _ => true);
        var second = await copy.Press();                      // B: its write held open by `newer`
        newer.SetVoidResult();
        copy.WaitFor(() => Assert.Equal(XgidCopy.CopiedLabel, copy.Name()));
        _clock.Advance(XgidCopy.ConfirmationTime);
        await FinishedAsync(second);
        Assert.Equal(copy.OwnName, copy.Name());

        _write.SetException(new JSException("NotAllowedError: Document is not focused."));

        // A has run to its end — the positive signal that its completion was
        // handled, so the reads below are not taken before it could act.
        await FinishedAsync(first);
        Assert.Equal(copy.OwnName, copy.Name());
        Assert.Null(copy.Mark());
        _clock.Advance(XgidCopy.ConfirmationTime);
        Assert.Equal(copy.OwnName, copy.Name());
    }

    [Fact]
    public async Task AnObsoleteCopysLateResult_WhileTheNewerCopyShows_NeitherReplacesNorOutlivesIt()
    {
        // The same rule met mid-moment: B is showing "Copied" when A's write
        // fails. B's result stays, for B's moment, and then the control is its
        // own again — A neither publishes over B nor keeps anything after it.
        var copy = Badge();
        var first = await copy.Press();
        var newer = JSInterop.SetupVoid("navigator.clipboard.writeText", _ => true);
        var second = await copy.Press();
        newer.SetVoidResult();
        copy.WaitFor(() => Assert.Equal(XgidCopy.CopiedLabel, copy.Name()));

        _write.SetException(new JSException("NotAllowedError: Document is not focused."));
        await FinishedAsync(first);
        Assert.Equal(XgidCopy.CopiedLabel, copy.Name());
        Assert.Equal("is-copied", copy.Mark());

        _clock.Advance(XgidCopy.ConfirmationTime);
        await FinishedAsync(second);
        Assert.Equal(XgidCopy.CopyLabel, copy.Name());
        Assert.Null(copy.Mark());
    }
}
