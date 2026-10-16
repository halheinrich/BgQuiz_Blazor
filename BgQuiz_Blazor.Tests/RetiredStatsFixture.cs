namespace BgQuiz_Blazor.Tests;

/// <summary>
/// Stats-file contents the ordinary current-version read does not simply accept, staged by
/// the tests of what the bind does about each: the retired versions it sets aside, the
/// foldable version it merges, and the unreadable ones it leaves alone — plus the one
/// current-version literal the fold reads as its base. Hand-written literals, unlike every
/// other stats fixture here: nothing can produce them, since the retired and foldable
/// formats have no writer left and the newer one has no reader.
/// </summary>
internal static class RetiredStatsFixture
{
    /// <summary>A genuine version-1 document: a <c>decisions</c> array of <c>DecisionId</c>-keyed records, never parsed by anything.</summary>
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

    /// <summary>A genuine version-2 document: the key-keyed map from before the Jacoby rule entered money keys; its money key carries no Jacoby token.</summary>
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
    /// A genuine version-3 document, the current format: staged as the standard file, where
    /// it reads as current, and as the set-aside sibling the fold merges <see cref="V4Json"/>
    /// into. One play record (money, Jacoby on, dice on the key) and one cube record.
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

    /// <summary>The play key <see cref="V3Json"/> and <see cref="V4Json"/> share — the record the fold sums.</summary>
    public const string SharedPlayKeyText =
        "0,-2,0,0,0,0,5,0,3,0,0,0,-5,5,0,0,0,-3,0,-5,0,0,0,0,2,0/0a0j/1c/31";

    /// <summary>The match cube key only <see cref="V3Json"/> holds.</summary>
    public const string V3OnlyCubeKeyText =
        "0,-2,0,0,0,0,5,0,3,0,0,0,-5,5,0,0,0,-3,0,-5,0,0,0,0,2,0/7a7/1c";

    /// <summary>The money cube key only <see cref="V4Json"/> holds.</summary>
    public const string V4OnlyCubeKeyText =
        "0,-2,0,0,0,0,5,0,3,0,0,0,-5,5,0,0,0,-3,0,-5,0,0,0,0,2,0/0a0j/1c";

    /// <summary>
    /// A genuine version-4 document, the interim answer-kind format that never shipped: each
    /// value wrapped in its one kind record. The one version that folds rather than retires.
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

    /// <summary>A version-4 document with no records: foldable and empty, so the sibling is the whole result and the probe reads no stats.</summary>
    public const string V4EmptyJson = """
        { "schemaVersion": 4, "problems": {} }
        """;

    /// <summary>Claims version 4 and passes the shallow shape check, but its value carries no answer-kind record: the fold reader rejects it.</summary>
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

    /// <summary>Claims version 1 but is not shaped like one: corrupt, not retired, so it takes the untouched-file path.</summary>
    public const string ClaimsV1ButMalformedJson = """
        { "schemaVersion": 1, "somethingElse": [] }
        """;

    /// <summary>A schema version newer than this build reads: fail-loud and untouched, never retired.</summary>
    public const string NewerSchemaJson = """
        { "schemaVersion": 99, "problems": [] }
        """;
}
