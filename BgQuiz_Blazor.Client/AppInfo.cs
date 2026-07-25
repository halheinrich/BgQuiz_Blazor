using System.Reflection;

namespace BgQuiz_Blazor.Client;

/// <summary>
/// App-level identity: the version the app reports about itself, and the beta
/// feedback address that version has to travel with.
///
/// <para>
/// <b>Why it isn't on <c>Home</c> any more.</b> The version began life as
/// <c>Home.AppVersion</c> because exactly one surface — the landing page's
/// <c>v{version}</c> footer — needed it. The beta feedback link needs the same
/// value on <i>two</i> pages, and a page class is the wrong owner of app-level
/// metadata the moment a second page reaches into it: <c>Help</c> would be
/// taking a dependency on <c>Home</c> for a fact that has nothing to do with the
/// landing page. Hoisting it here leaves each page depending on the app, not on
/// each other.
/// </para>
///
/// <para>
/// Lives at the client root rather than under <c>Quiz/</c>, which is the quiz
/// domain's home (controller, holders, stats, folder access, the display SSOTs).
/// Nothing here is about quizzing.
/// </para>
/// </summary>
internal static class AppInfo
{
    /// <summary>
    /// The running app's version, resolved once from the client assembly's
    /// <see cref="AssemblyInformationalVersionAttribute"/> — declared via
    /// <c>&lt;Version&gt;</c> in the csproj, the single source of truth (no
    /// hardcoded literal anywhere). Falls back to the assembly version, then a
    /// placeholder, if the attribute is ever absent.
    ///
    /// <para>
    /// A build that is not the shipping publish appends <c>+g&lt;shortsha&gt;</c>
    /// (see <c>StampGitShaSuffix</c> in the csproj), so a deploy candidate under
    /// acceptance names the commit it was built from rather than looking like the
    /// release it is not yet. The leading SemVer is always <c>&lt;Version&gt;</c>.
    /// That suffix is also what makes a beta tester's report actionable, which is
    /// why <see cref="FeedbackMailto"/> carries this value rather than the bare
    /// release number.
    /// </para>
    /// </summary>
    internal static string Version { get; } =
        typeof(AppInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion
        ?? typeof(AppInfo).Assembly.GetName().Version?.ToString()
        ?? "unknown";

    /// <summary>
    /// Where beta feedback goes. A plain mailbox, deliberately: the app has no
    /// server, no account, and nothing to POST a form to — see the privacy stance
    /// <c>Help</c> states — so the one channel that doesn't contradict that is
    /// the user's own mail client.
    /// </summary>
    internal const string FeedbackAddress = "bgquiz.beta@gmail.com";

    /// <summary>
    /// The <c>mailto:</c> href both feedback affordances render — Home's footer
    /// and Help's feedback section — with the running <see cref="Version"/>
    /// pre-filled into the subject. Two surfaces rendering one link is what earns
    /// it a single constructed value rather than a literal per page.
    ///
    /// <para>
    /// The subject is percent-encoded (<see cref="Uri.EscapeDataString(string)"/>),
    /// not interpolated raw. It has to be: a non-shipping build's version carries a
    /// <c>+</c> (<c>1.0.10+gabc1234</c>), and a bare <c>+</c> in a URI query is
    /// read as a space by mail clients that decode the query as form data — the
    /// commit the tester is reporting against would silently arrive mangled.
    /// Escaping also covers the parentheses and spaces in the subject text.
    /// </para>
    /// </summary>
    internal static string FeedbackMailto { get; } =
        $"mailto:{FeedbackAddress}?subject={Uri.EscapeDataString($"BgQuiz feedback ({Version})")}";
}
