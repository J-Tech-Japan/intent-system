using System.Text.Json;
using System.Globalization;

namespace IntentSystem.Cli.Commands;

internal static class UnitStatusRenderer
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    public static void Write(TextWriter writer, UnitStatusReport report, string format)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(report);
        if (string.Equals(format, "json", StringComparison.Ordinal))
        {
            writer.WriteLine(JsonSerializer.Serialize(report, JsonOptions));
            return;
        }

        WriteMarkdown(writer, report);
    }

    private static void WriteMarkdown(TextWriter writer, UnitStatusReport report)
    {
        writer.WriteLine($"# Unit status: {report.ExecutionUnit}");
        writer.WriteLine("- schema_version: `1`");
        writer.WriteLine($"- domain: {Value(report.Domain)}");
        writer.WriteLine($"- team: {Value(report.Team)}");
        writer.WriteLine($"- team_mode: {Value(report.TeamMode)}");
        writer.WriteLine($"- mode_basis: {Value(report.ModeBasis)}");
        writer.WriteLine($"- applicability: {report.Applicability.State} ({Value(report.Applicability.Cause)})");
        writer.WriteLine($"- applicability_detail: {Value(report.Applicability.Detail)}");
        writer.WriteLine($"- unavailable_class: {Value(report.Applicability.UnavailableClass)}");
        writer.WriteLine($"- mode_entry: {Value(report.Applicability.ModeEntryPath)} @ {Value(report.Applicability.ModeEntryUpdatedAt)}");
        writer.WriteLine();
        writer.WriteLine("## Identity");
        writer.WriteLine($"- repo: {Value(report.Identity.Repo)}");
        writer.WriteLine($"- issue: {Value(report.Identity.Issue)}");
        writer.WriteLine($"- pr: {Value(report.Identity.Pr)}");
        writer.WriteLine($"- head_sha: {Value(report.Identity.HeadSha)}");
        writer.WriteLine($"- sources: {List(report.Identity.Sources)}");
        writer.WriteLine();
        writer.WriteLine("## Observation");
        writer.WriteLine($"- observed_at: {report.Observation.ObservedAt:O}");
        writer.WriteLine($"- local_head: {Value(report.Observation.LocalHeadSha)} ({Value(report.Observation.LocalHeadRef)})");
        writer.WriteLine($"- claim_metadata_ref: {Value(report.Observation.ClaimMetadataRef)}");
        writer.WriteLine($"- claim_metadata_oid: {Value(report.Observation.ClaimMetadataOid)}");
        writer.WriteLine($"- local_sources: {List(report.Observation.LocalSources)}");
        var github = report.Observation.GitHubSnapshot;
        writer.WriteLine($"- github_snapshot: {github.State} ({Value(github.Cause)}): {Value(github.Detail)}");
        writer.WriteLine($"- github_head_before_after: {Value(github.HeadBefore)} -> {Value(github.HeadAfter)}");
        writer.WriteLine($"- inventory_scope: {github.InventoryScope}");
        writer.WriteLine($"- branch_requirements_evaluated: {github.BranchRequirementsEvaluated.ToString().ToLowerInvariant()}");
        writer.WriteLine($"- issue_labels: {List(github.IssueLabels)}");
        writer.WriteLine($"- pull_request_labels: {List(github.PullRequestLabels)}");
        writer.WriteLine($"- merged: {Value(github.Merged)}");
        writer.WriteLine($"- merge_commit_sha: {Value(github.MergeCommitSha)}");
        writer.WriteLine($"- warnings: {List(report.Observation.Warnings)}");
        writer.WriteLine();
        writer.WriteLine("## Solo-conductor phases");
        foreach (var step in report.Steps)
        {
            writer.WriteLine($"### {step.Id}: {step.State}");
            writer.WriteLine($"- cause: {Value(step.Cause)}");
            writer.WriteLine($"- detail: {Value(step.Detail)}");
            foreach (var fact in step.Subchecks)
            {
                writer.WriteLine($"- subcheck `{fact.Id}`: **{fact.State}**");
                writer.WriteLine($"  - cause: {Value(fact.Cause)}");
                writer.WriteLine($"  - detail: {Value(fact.Detail)}");
                writer.WriteLine($"  - unavailable_class: {Value(fact.UnavailableClass)}");
                foreach (var evidence in fact.Evidence)
                {
                    writer.WriteLine(
                        $"  - evidence: kind={evidence.Kind}; path={Value(evidence.Path)}; url={Value(evidence.Url)}; "
                        + $"record_id={Value(evidence.RecordId)}; line={Value(evidence.Line)}; role={Value(evidence.Role)}; "
                        + $"unit={Value(evidence.ExecutionUnit)}; repo={Value(evidence.Repo)}; pr={Value(evidence.Pr)}; "
                        + $"head={Value(evidence.HeadSha)}; at={DateTimeValue(evidence.RecordedAt)}; "
                        + $"claim_epoch_claimed_at={DateTimeValue(evidence.ClaimEpochClaimedAt)}; claim_operation={Value(evidence.ClaimOperation)}; "
                        + $"claim_disposition={Value(evidence.ClaimDisposition)}; claim_actor={Value(evidence.ClaimActor)}; "
                        + $"claim_team={Value(evidence.ClaimTeam)}; displaced_claimed_at={DateTimeValue(evidence.DisplacedClaimedAt)}; "
                        + $"review_verdict={Value(evidence.ReviewVerdict)}; review_state={Value(evidence.ReviewState)}; "
                        + $"review_disposition={Value(evidence.ReviewDisposition)}; provenance={evidence.Provenance}");
                }

                writer.WriteLine($"  - repair_commands: {List(fact.RepairCommands)}");
                writer.WriteLine($"  - repair_unavailable_reason: {Value(fact.RepairUnavailableReason)}");
            }
        }

        writer.WriteLine();
        writer.WriteLine("## Observed reviews");
        foreach (var review in github.Reviews)
        {
            writer.WriteLine(
                $"- source={review.Source}; head={review.HeadSha}; verdict={review.Verdict}; "
                + $"review_state={Value(review.ReviewState)}; reviewer={Value(review.Reviewer)}; runtime={Value(review.Runtime)}; "
                + $"relation={Value(review.Relation)}; qualification={Value(review.Qualification)}; at={DateTimeValue(review.At)}; record_id={Value(review.RecordId)}; "
                + $"url={Value(review.Url)}; dismissed={review.Dismissed.ToString().ToLowerInvariant()}");
        }

        writer.WriteLine();
        writer.WriteLine("## Observed exact-head checks");
        foreach (var check in github.Checks)
        {
            writer.WriteLine(
                $"- source={check.Source}; identity={check.Identity}; record_id={Value(check.RecordId)}; sha={check.Sha}; status={check.Status}; "
                + $"conclusion={Value(check.Conclusion)}; run_id={Value(check.RunId)}; attempt={Value(check.Attempt)}; "
                + $"reported_run_attempt={Value(check.ReportedRunAttempt)}; attempt_basis={Value(check.AttemptBasis)}; url={Value(check.Url)}");
        }

        writer.WriteLine();
        writer.WriteLine("## Summary");
        writer.WriteLine($"- state_counts: {Counts(report.Summary.StateCounts)}");
        writer.WriteLine($"- unavailable_class_counts: {Counts(report.Summary.UnavailableClassCounts)}");
        writer.WriteLine($"- observation_exit_code: {report.Summary.ObservationExitCode}");
        writer.WriteLine("Exit status reports whether observation completed; it does not mean completion or merge readiness.");
    }

    private static string Counts(IReadOnlyDictionary<string, int> counts) =>
        string.Join(", ", counts.OrderBy(item => item.Key, StringComparer.Ordinal).Select(item => $"{item.Key}={item.Value}"));

    private static string List(IReadOnlyList<string> values) =>
        values.Count == 0 ? "[]" : string.Join(", ", values.Select(value => $"`{value}`"));

    private static string Value<T>(T? value) => value is null ? "null" : $"`{value}`";

    private static string DateTimeValue(DateTimeOffset? value) => value is null
        ? "null"
        : $"`{value.Value.ToString("O", CultureInfo.InvariantCulture)}`";
}
