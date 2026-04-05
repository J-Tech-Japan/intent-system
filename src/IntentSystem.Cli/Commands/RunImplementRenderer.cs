namespace IntentSystem.Cli.Commands;

internal static class RunImplementRenderer
{
    public static string RenderMarkdown(RunImplementRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var lines = new List<string>
        {
            "# Execution Worker Handoff",
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
            $"- linked_issue: {request.LinkedIssue}"
        };

        if (!string.IsNullOrWhiteSpace(request.LatestLinkedPr))
        {
            lines.Add($"- latest_linked_pr: {request.LatestLinkedPr}");
        }

        lines.AddRange(
        [
            string.Empty,
            "## Packet Inputs",
            string.Empty,
            $"- packet_ref: {request.PacketRef}",
            $"- review_context_ref: {request.ReviewContextRef}",
            $"- issue_title: {request.IssueTitle}",
            $"- goal: {request.Goal}",
            $"- target_part: {request.TargetPart}",
            $"- target_repo: {request.TargetRepo}",
            $"- target_path: {request.TargetPath}",
            string.Empty,
            "## In Scope",
            string.Empty
        ]);
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

        return string.Join(Environment.NewLine, lines);
    }

    public static void WriteSummary(TextWriter writer, RunImplementRequest request, string artifactPath)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactPath);

        writer.WriteLine($"Implementation handoff artifact generated for {request.ExecutionUnit}.");
        writer.WriteLine($"Artifact path: {artifactPath}");
        writer.WriteLine($"Implement role: {request.ImplementRole}");
        writer.WriteLine($"Worktree path: {request.WorktreePath}");
        writer.WriteLine($"Branch: {request.Branch}");

        if (!string.IsNullOrWhiteSpace(request.LatestLinkedPr))
        {
            writer.WriteLine($"Latest linked PR: {request.LatestLinkedPr}");
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
