namespace BgQuiz_Blazor.Tests;

/// <summary>
/// Stats-file contents no current-version reader can parse, staged by the tests
/// that exercise what the store does about each. One spelling per case, shared
/// between the store suite and the page suite so the two cannot drift about what
/// "a retired file" is.
///
/// <para>
/// These are deliberately hand-written literals, unlike every other stats
/// fixture in this project (which serializes a real document so a wire-format
/// change reaches it). Nothing can produce them: the retired format has no
/// writer left, and the newer one has no reader — a literal is the only way to
/// stage a file this build cannot itself create.
/// </para>
/// </summary>
internal static class RetiredStatsFixture
{
    /// <summary>
    /// A genuine version-1 document: <c>schemaVersion</c> first, then a
    /// <c>decisions</c> array of <c>DecisionId</c>-keyed records — the format
    /// retired by the clean break (SPEC-stats-identity.md §3). The array's
    /// contents are never parsed by anything, in the app or here; they are real
    /// v1 shape so that what a test stages is what a tester actually has.
    /// </summary>
    public const string V1Json = """
        {
          "schemaVersion": 1,
          "decisions": [
            { "id": "legacy.xg:g3:m12:play",
              "tally": { "submitted": 3, "correct": 2, "totalEquityLoss": 0.125 },
              "lastQuizzed": "2026-07-18T19:04:11+00:00" }
          ]
        }
        """;

    /// <summary>
    /// A genuine version-2 document: the <see cref="BgDataTypes_Lib.ProblemKey"/>-keyed
    /// <c>problems</c> map from before the Jacoby rule entered money keys, retired
    /// in turn by the v3 break (SPEC-stats-identity.md §3, amended for
    /// halheinrich/backgammon#120). The second retired version, and the reason
    /// the set-aside name is derived rather than fixed — a tester who skipped a
    /// release meets this retirement <i>after</i> the v1 one, in the same folder.
    /// Its money key carries no Jacoby token, which is precisely what v3 keys
    /// spell and why the format could not be carried forward.
    /// </summary>
    public const string V2Json = """
        {
          "schemaVersion": 2,
          "problems": {
            "0,0,0,0,0,0,5,0,3,0,0,0,-5,5,0,0,0,-3,0,-5,0,0,0,0,2,0/0a0/1c": {
              "tally": { "submitted": 4, "correct": 1, "totalEquityLoss": 0.250 },
              "lastQuizzed": "2026-08-19T11:22:33+00:00"
            }
          }
        }
        """;

    /// <summary>
    /// A genuine version-3 document: the <see cref="BgDataTypes_Lib.ProblemKey"/>-keyed
    /// <c>problems</c> map with the Jacoby token in its money key, from before
    /// answer kinds entered the per-problem records — retired by the v4 break
    /// (SPEC-stats-identity.md §3, amended for halheinrich/backgammon#86). The
    /// third retired version, and the one every current tester actually holds:
    /// v3 was the shipping format from the Jacoby re-key through v1.9.x. Its
    /// per-problem values are bare tally-plus-date objects, which is precisely
    /// what v4 nests under an answer-kind token — and its cube tallies accrued
    /// under action-vs-action scoring, which is why the content is set aside
    /// rather than carried (BgGame_Lib's <c>CurrentSchemaVersion</c> remarks).
    /// One play record (money, Jacoby on, dice on the key) and one cube record
    /// (match, no dice), so the shape a tester's file has is the shape staged.
    /// </summary>
    public const string V3Json = """
        {
          "schemaVersion": 3,
          "problems": {
            "0,-2,0,0,0,0,5,0,3,0,0,0,-5,5,0,0,0,-3,0,-5,0,0,0,0,2,0/0a0j/1c/31": {
              "tally": { "submitted": 1, "correct": 1, "totalEquityLoss": 0 },
              "lastQuizzed": "2026-08-30T09:15:00+00:00"
            },
            "0,-2,0,0,0,0,5,0,3,0,0,0,-5,5,0,0,0,-3,0,-5,0,0,0,0,2,0/7a7/1c": {
              "tally": { "submitted": 2, "correct": 1, "totalEquityLoss": 0.08 },
              "lastQuizzed": "2026-08-30T09:15:00+00:00"
            }
          }
        }
        """;

    /// <summary>
    /// Claims version 1 but is not shaped like one. Corrupt, not retired: it
    /// must take the untouched-file path, never the set-aside one — a file
    /// nobody can identify is a file nobody may rewrite.
    /// </summary>
    public const string ClaimsV1ButMalformedJson = """
        { "schemaVersion": 1, "somethingElse": [] }
        """;

    /// <summary>
    /// A schema version newer than this build reads — written by a later
    /// BgQuiz. Fail-loud and untouched, explicitly not retired: retiring it
    /// would set aside a file whose owner is a version still to come.
    /// </summary>
    public const string NewerSchemaJson = """
        { "schemaVersion": 99, "problems": [] }
        """;
}
