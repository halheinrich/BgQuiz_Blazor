namespace BgQuiz_Blazor.Tests;

/// <summary>
/// Stats-file contents the ordinary current-version read does not simply
/// accept, staged by the tests that exercise what the store does about each:
/// the retired versions it sets aside, the foldable version it merges, and
/// the unreadable ones it leaves alone — plus the one current-version literal
/// the fold reads as its base. One spelling per case, shared between the
/// store suite and the page suite so the two cannot drift about what "a
/// retired file" or "a foldable file" is.
///
/// <para>
/// These are deliberately hand-written literals, unlike every other stats
/// fixture in this project (which serializes a real document so a wire-format
/// change reaches it). Nothing can produce them: the retired and foldable
/// formats have no writer left, and the newer one has no reader — a literal
/// is the only way to stage a file this build cannot itself create.
/// <see cref="V3Json"/> is the exception that proves it: it is the current
/// format, kept as a literal so the fold's base is the bytes the interim
/// build actually set aside, not a document this build wrote.
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
    /// A genuine version-3 document — the <b>current</b> format again since
    /// SPEC-stats-identity.md §3's 2026-09-02 amendment
    /// (halheinrich/backgammon#187): the <see cref="BgDataTypes_Lib.ProblemKey"/>-keyed
    /// <c>problems</c> map with the Jacoby token in its money key and bare
    /// tally-plus-date values. It was retired for one interim build
    /// (halheinrich/backgammon#86's v4, which set it aside as
    /// <c>bgquiz-stats.v3.json</c> and never shipped) and reinstated when the
    /// amended Too Good predicate made its tallies comparable after all. So it
    /// is staged two ways: as the standard file, where it reads as current and
    /// nothing is set aside; and as the set-aside sibling a folder the interim
    /// build touched still holds, where it is the <b>base</b> the fold merges
    /// <see cref="V4Json"/> into. One play record (money, Jacoby on, dice on
    /// the key) and one cube record (match, no dice), so the shape a tester's
    /// file has is the shape staged.
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
    /// The play key <see cref="V3Json"/> and <see cref="V4Json"/> share — the
    /// record the fold sums. Spelled once so a merge pin cannot drift from the
    /// two literals it merges.
    /// </summary>
    public const string SharedPlayKeyText =
        "0,-2,0,0,0,0,5,0,3,0,0,0,-5,5,0,0,0,-3,0,-5,0,0,0,0,2,0/0a0j/1c/31";

    /// <summary>The match cube key only <see cref="V3Json"/> holds.</summary>
    public const string V3OnlyCubeKeyText =
        "0,-2,0,0,0,0,5,0,3,0,0,0,-5,5,0,0,0,-3,0,-5,0,0,0,0,2,0/7a7/1c";

    /// <summary>The money cube key only <see cref="V4Json"/> holds.</summary>
    public const string V4OnlyCubeKeyText =
        "0,-2,0,0,0,0,5,0,3,0,0,0,-5,5,0,0,0,-3,0,-5,0,0,0,0,2,0/0a0j/1c";

    /// <summary>
    /// A genuine version-4 document: the interim answer-kind format
    /// (halheinrich/backgammon#86 leg 2, never shipped), each per-problem value
    /// wrapped in its one kind record — <c>checkerPlay</c> under a key with
    /// dice, <c>cubePair</c> under one without. The one version that
    /// <b>folds</b> rather than retiring (SPEC-stats-identity.md §3, amended
    /// 2026-09-02): the bind reads it through the producer's fold reader,
    /// merges it into <see cref="V3Json"/> when that sibling exists, writes the
    /// result as the current document and copies these bytes aside as
    /// <c>bgquiz-stats.v4.merged.json</c>. Two records: the play key it shares
    /// with <see cref="V3Json"/> (so the merge has a tally to sum and a later
    /// date to keep) and a money cube key <see cref="V3Json"/> lacks (so a
    /// one-sided record has to pass through).
    /// </summary>
    public const string V4Json = """
        {
          "schemaVersion": 4,
          "problems": {
            "0,-2,0,0,0,0,5,0,3,0,0,0,-5,5,0,0,0,-3,0,-5,0,0,0,0,2,0/0a0j/1c/31": {
              "checkerPlay": {
                "tally": { "submitted": 2, "correct": 1, "totalEquityLoss": 0.05 },
                "lastQuizzed": "2026-09-01T18:30:00+00:00"
              }
            },
            "0,-2,0,0,0,0,5,0,3,0,0,0,-5,5,0,0,0,-3,0,-5,0,0,0,0,2,0/0a0j/1c": {
              "cubePair": {
                "tally": { "submitted": 4, "correct": 2, "totalEquityLoss": 0.2 },
                "lastQuizzed": "2026-09-01T18:31:00+00:00"
              }
            }
          }
        }
        """;

    /// <summary>
    /// A version-4 document with no records — what the interim build seeded
    /// when it retired a v3 file, in a folder whose owner then never answered
    /// a problem. Foldable and empty: the fold still runs (the v3 sibling is
    /// the whole result), and the probe reads it as no stats to weight by.
    /// </summary>
    public const string V4EmptyJson = """
        { "schemaVersion": 4, "problems": {} }
        """;

    /// <summary>
    /// Claims version 4 and passes the shallow shape check — so the ordinary
    /// read raises the foldable signal — but its body is not a v4 body: the
    /// value carries no answer-kind record. The fold reader rejects it, and a
    /// folder this build cannot read whole is a folder it does not rewrite.
    /// </summary>
    public const string ClaimsV4ButMalformedJson = """
        {
          "schemaVersion": 4,
          "problems": {
            "0,-2,0,0,0,0,5,0,3,0,0,0,-5,5,0,0,0,-3,0,-5,0,0,0,0,2,0/0a0j/1c/31": {
              "tally": { "submitted": 2, "correct": 1, "totalEquityLoss": 0.05 },
              "lastQuizzed": "2026-09-01T18:30:00+00:00"
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
