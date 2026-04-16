namespace IntentSystem.Cli.Commands;

internal static class RunFixRenderer
{
    public static string RenderMarkdown(RunFixRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var lines = new List<string>
        {
            "# Repair Worker Handoff",
            string.Empty,
            "## Execution Unit",
            string.Empty,
            $"`{request.ExecutionUnit}`",
            string.Empty,
            "## Role Mapping",
            string.Empty,
            $"- implement: {request.ImplementRole}",
            $"- queue_worker_role: {request.QueueWorkerRole}",
            $"- queue_review_role: {request.QueueReviewRole}",
            string.Empty,
            "## Run Context",
            string.Empty,
            $"- state: {request.State}",
            $"- worktree_path: {request.WorktreePath}",
            $"- child_repo_path: {request.ChildRepoPath}",
            $"- branch: {request.Branch}",
            $"- linked_issue: {request.LinkedIssue}",
            $"- latest_linked_pr: {request.LatestLinkedPr}",
            $"- latest_comment_ref: {request.LatestCommentRef}",
            string.Empty,
            "## Repair Inputs",
            string.Empty,
            $"- packet_ref: {request.PacketRef}",
            $"- review_context_ref: {request.ReviewContextRef}",
            $"- review_comment_artifact_ref: {request.ReviewCommentArtifactRef}",
            $"- review_request_ref: {request.ReviewRequestRef}",
            $"- review_comment_body_path: {request.ReviewCommentBodyPath}",
            string.Empty,
            "## Packet Inputs",
            string.Empty,
            $"- issue_title: {request.IssueTitle}",
            $"- goal: {request.Goal}",
            $"- target_part: {request.TargetPart}",
            $"- target_repo: {request.TargetRepo}",
            $"- target_path: {request.TargetPath}",
            string.Empty,
            "## In Scope",
            string.Empty
        };

        lines.AddRange(FormatList(request.InScope));
        lines.Add(string.Empty);
        lines.Add("## Out Of Scope");
        lines.Add(string.Empty);
        lines.AddRange(FormatList(request.OutOfScope));
        lines.Add(string.Empty);
        lines.Add("## Acceptance Criteria");
        lines.Add(string.Empty);
        lines.AddRange(FormatList(request.AcceptanceCriteria));
        lines.Add(string.Empty);
        lines.Add("## Deterministic Review Checks");
        lines.Add(string.Empty);
        lines.AddRange(FormatList(request.DeterministicReviewChecks));
        lines.Add(string.Empty);
        lines.Add("## Expected Evidence");
        lines.Add(string.Empty);
        lines.AddRange(FormatList(request.ExpectedEvidence));
        lines.Add(string.Empty);
        lines.Add("## Execution Contract");
        lines.Add(string.Empty);
        lines.Add("- Continue beyond initial repository inspection; do not stop after a single listing/read-only command.");
        lines.Add("- Use the repair inputs to attempt the bounded fix inside the provided worktree when the change is feasible.");
        lines.Add("- If no safe bounded repair can continue, end with a deterministic refusal or contract-gap explanation instead of silent termination.");

        return string.Join(Environment.NewLine, lines);
    }

    public static void WriteSummary(
        TextWriter writer,
        RunFixRequest request,
        string artifactPath,
        DirectRunLaunchResult? directRun = null)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactPath);

        writer.WriteLine($"Repair handoff artifact generated for {request.ExecutionUnit}.");
        writer.WriteLine($"Artifact path: {artifactPath}");
        writer.WriteLine($"Implement role: {request.ImplementRole}");
        writer.WriteLine($"Worktree path: {request.WorktreePath}");
        writer.WriteLine($"Branch: {request.Branch}");
        writer.WriteLine($"Latest linked PR: {request.LatestLinkedPr}");
        writer.WriteLine($"Latest comment ref: {request.LatestCommentRef}");

        if (directRun is not null)
        {
            writer.WriteLine($"Direct run request artifact: {directRun.RequestArtifactPath}");
            writer.WriteLine($"Provider raw event log: {directRun.ProviderEventLogPath}");
            if (!string.IsNullOrWhiteSpace(directRun.ResultArtifactPath))
            {
                writer.WriteLine($"Normalized run result: {directRun.ResultArtifactPath}");
            }
            writer.WriteLine($"Direct provider: {directRun.Provider}");
            writer.WriteLine($"Direct model: {directRun.Model}");
            writer.WriteLine($"Direct transport: {directRun.Transport}");
            writer.WriteLine($"Provider session: {directRun.ProviderSessionId}");
            if (!string.IsNullOrWhiteSpace(directRun.RunStatus))
            {
                writer.WriteLine($"Run status: {directRun.RunStatus}");
            }
        }
    }

    private static IReadOnlyList<string> FormatList(IReadOnlyList<string> values)
    {
        if (values.Count == 0)
        {
            return ["- none"];
        }

        return values.Select(value => $"- {value}").ToArray();
    }
}
