using AngleSharp.Dom;
using BgDataTypes_Lib.TestSupport;
using BgQuiz_Blazor.Client;
using BgQuiz_Blazor.Client.Components;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace BgQuiz_Blazor.Tests;

/// <summary>
/// The feedback address beside each feedback link (<see cref="FeedbackAddress"/>;
/// issue <c>halheinrich/backgammon#267</c>): the address as text, and a copy
/// button that copies it and confirms truthfully, through the one copy every
/// copy control shares (<see cref="ClipboardCopy"/>, whose own contract is
/// <see cref="ClipboardCopyTests"/>'). The button's three labels are spelled
/// out here, so a control handed another use's labels — the XGID's — fails by
/// name.
/// </summary>
public class FeedbackAddressTests : BunitContext
{
    private const string CopyName = "Copy the feedback address";
    private const string CopiedName = "Copied";
    private const string RefusedName = "Couldn't copy the address";

    private readonly ManualTimeProvider _clock = new();
    private readonly RecordingLogger<ClipboardCopy> _log = new();
    private readonly JSRuntimeInvocationHandler _write;

    public FeedbackAddressTests()
    {
        _write = JSInterop.SetupVoid("navigator.clipboard.writeText", _ => true);
        Services.AddSingleton<TimeProvider>(_clock);
        Services.AddSingleton<ILogger<ClipboardCopy>>(_log);
    }

    private static IElement Button(IRenderedComponent<FeedbackAddress> cut) => cut.Find(".feedback-address-copy");

    /// <summary>The button's name: its tooltip and its accessible text, which must agree.</summary>
    private static string Name(IRenderedComponent<FeedbackAddress> cut)
    {
        var title = Button(cut).GetAttribute("title")!;
        Assert.Equal(title, Button(cut).QuerySelector(".visually-hidden")!.TextContent);
        return title;
    }

    private static string? Mark(IRenderedComponent<FeedbackAddress> cut) =>
        Button(cut).ClassList.FirstOrDefault(c => c.StartsWith("is-", StringComparison.Ordinal));

    [Fact]
    public void ShowsTheAddressAsText_BesideACopyButtonNamedForIt()
    {
        var cut = Render<FeedbackAddress>();

        Assert.Equal(AppInfo.FeedbackAddress, cut.Find(".feedback-address-text").TextContent);
        Assert.Equal("button", Button(cut).GetAttribute("type"));
        Assert.Null(Button(cut).GetAttribute("aria-label"));   // named by real text, which carries the result
        Assert.Equal(CopyName, Name(cut));
        Assert.Null(Mark(cut));
    }

    [Fact]
    public async Task ACopy_WritesTheAddress_AndConfirmsOnlyOnceTheWriteResolves_ThenIsItsOwnAgain()
    {
        var cut = Render<FeedbackAddress>();

        var handler = Button(cut).ClickAsync(new());

        Assert.Equal(AppInfo.FeedbackAddress, _write.Invocations.Single().Arguments[0]);
        Assert.Equal(CopyName, Name(cut));   // nothing claimed before the write resolves

        _write.SetVoidResult();
        cut.WaitForAssertion(() => Assert.Equal(CopiedName, Name(cut)));
        Assert.Equal("is-copied", Mark(cut));

        _clock.Advance(ClipboardCopy.ConfirmationTime);
        await handler.WaitAsync(DefaultWaitTimeout);
        Assert.Equal(CopyName, Name(cut));
        Assert.Null(Mark(cut));
        Assert.Empty(_log.Entries);
    }

    [Fact]
    public async Task ARefusedCopy_IsReportedAsTheRefusal_NeverAsCopied_AndThrowsNowhere()
    {
        var cut = Render<FeedbackAddress>();

        var handler = Button(cut).ClickAsync(new());
        _write.SetException(new JSException("NotAllowedError: Write permission denied."));

        cut.WaitForAssertion(() => Assert.Equal(RefusedName, Name(cut)));
        Assert.Equal("is-failed", Mark(cut));

        _clock.Advance(ClipboardCopy.ConfirmationTime);
        await handler.WaitAsync(DefaultWaitTimeout);   // completes, rather than rethrowing the refusal
        Assert.Equal(CopyName, Name(cut));
        var entry = Assert.Single(_log.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.IsType<JSException>(entry.Exception);
    }
}
