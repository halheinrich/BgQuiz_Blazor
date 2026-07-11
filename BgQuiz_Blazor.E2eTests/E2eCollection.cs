namespace BgQuiz_Blazor.E2eTests;

/// <summary>
/// The single test collection every e2e class joins. Membership does two jobs:
/// the two collection fixtures (publish-and-spawn, browser) run once per test
/// run, and xunit executes the collection's tests sequentially — one browser
/// context at a time against the one spawned artifact, so no scenario ever
/// observes another's traffic.
/// </summary>
[CollectionDefinition(Name)]
public sealed class E2eCollection
    : ICollectionFixture<PublishedAppFixture>, ICollectionFixture<PlaywrightFixture>
{
    public const string Name = "BgQuiz e2e";
}
