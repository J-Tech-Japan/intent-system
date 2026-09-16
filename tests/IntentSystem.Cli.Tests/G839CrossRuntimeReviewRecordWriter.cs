using IntentSystem.Cli.Commands;

namespace IntentSystem.Cli.Tests;

internal static class G839CrossRuntimeReviewRecordWriter
{
    internal static void WriteImplementationRecord(
        string repoRoot,
        string repo,
        int pr,
        string executionUnit,
        string domain,
        string team,
        string runtime,
        string verdict,
        string headSha,
        DateTimeOffset recordedAt)
    {
        var raw = "{}"u8.ToArray();
        var blockingFindings = verdict == "request-changes"
            ? (IReadOnlyList<CrossRuntimeReviewFinding>)
            [
                new CrossRuntimeReviewFinding
                {
                    File = "src/A.cs",
                    Line = 12,
                    Scenario = "fixture blocking finding",
                },
            ]
            : Array.Empty<CrossRuntimeReviewFinding>();
        var record = new CrossRuntimeReviewRecord
        {
            ArtifactKind = CrossRuntimeReviewRecord.ArtifactKindValue,
            Repo = repo,
            Pr = pr,
            HeadSha = headSha.ToLowerInvariant(),
            ExecutionUnit = executionUnit,
            Domain = domain,
            Team = team,
            Kind = CrossRuntimeReviewRecord.KindImplementation,
            Runtime = runtime,
            RuntimeVersion = "fixture",
            ConductorRuntime = "claude",
            Relation = CrossRuntimeReviewRecord.RelationFor(runtime, "claude"),
            Verdict = verdict,
            BlockingFindings = blockingFindings,
            Notes = Array.Empty<string>(),
            RecordedAt = recordedAt.ToUniversalTime(),
            RawVerdictFile = string.Empty,
            RawVerdictSha256 = CrossRuntimeReviewStore.Sha256Hex(raw),
        };

        record = record with { RawVerdictFile = CrossRuntimeReviewStore.RawRelativePath(record) };
        var result = CrossRuntimeReviewStore.Write(repoRoot, record, raw);
        if (!result.Written)
        {
            throw new InvalidOperationException(result.Error ?? "record write failed");
        }
    }
}
