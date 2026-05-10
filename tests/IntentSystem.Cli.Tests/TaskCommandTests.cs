using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G317: explicit one-shot task planner surface — verifies each
/// subcommand returns a bounded executable contract, validates inputs
/// before mutation guidance is emitted, and never crosses the read-only
/// boundary (no gh call, no state mutation in the planner itself).
/// </summary>
public sealed class TaskCommandTests
{
    // ---- task issue-to-pr ---------------------------------------------------

    [Fact]
    public void IssueToPr_HappyPath_JsonContractContainsClaimWorkdirCloseAndComplete()
    {
        using var writer = new StringWriter();
        var exit = TaskCommand.Execute(
            CreateContext(),
            ["issue-to-pr", "--repo", "J-Tech-Japan/intent-system", "--issue", "5000",
             "--workdir", "/tmp/child", "--format", "json"],
            writer);

        Assert.Equal(0, exit);
        using var doc = JsonDocument.Parse(writer.ToString());
        var root = doc.RootElement;
        Assert.Equal("issue-to-pr", root.GetProperty("task").GetString());
        Assert.Equal("J-Tech-Japan/intent-system", root.GetProperty("repo").GetString());
        Assert.Equal(5000, root.GetProperty("issue").GetInt32());
        Assert.Equal("/tmp/child", root.GetProperty("workdir").GetString());
        Assert.False(root.TryGetProperty("refusals", out _),
            "happy-path plan must not include refusals.");

        var steps = root.GetProperty("steps").EnumerateArray().Select(e => e.GetString()!).ToArray();
        // Concrete contract: claim, branch, push, base-branch-check, result-summary, complete.
        Assert.Contains(steps, s => s.Contains("worker claim --kind issue", StringComparison.Ordinal)
            && s.Contains("--number 5000", StringComparison.Ordinal));
        Assert.Contains(steps, s => s.Contains("git checkout -b claude/g-issue-5000", StringComparison.Ordinal));
        Assert.Contains(steps, s => s.Contains("git push -u origin claude/g-issue-5000", StringComparison.Ordinal));
        Assert.Contains(steps, s => s.Contains("automation base-branch-check", StringComparison.Ordinal));
        Assert.Contains(steps, s => s.Contains("worker result-summary --kind issue-to-pr", StringComparison.Ordinal));
        Assert.Contains(steps, s => s.Contains("worker complete --kind issue", StringComparison.Ordinal)
            && s.Contains("--number 5000", StringComparison.Ordinal)
            && s.Contains("--outcome pr-created", StringComparison.Ordinal)
            && s.Contains("--write", StringComparison.Ordinal));

        // G311: Closes #<source-issue> must be named in the steps so the
        // controller does not forget the closing reference.
        Assert.Contains(steps, s => s.Contains("Closes #5000", StringComparison.Ordinal));

        // Abort conditions name G300 linked_pr_synced expected warning,
        // G311 missing closing reference, and base-branch policy mismatch.
        var aborts = root.GetProperty("abort_conditions")
            .EnumerateArray().Select(e => e.GetString()!).ToArray();
        Assert.Contains(aborts, a => a.Contains("linked_pr_synced: false", StringComparison.Ordinal));
        Assert.Contains(aborts, a => a.Contains("Closes #5000", StringComparison.Ordinal));
        Assert.Contains(aborts, a => a.Contains("base-branch-check", StringComparison.Ordinal));

        // Label transitions never apply intent-target / intent-pr-created
        // from the child loop.
        var labels = root.GetProperty("label_transitions")
            .EnumerateArray().Select(e => e.GetString()!).ToArray();
        Assert.Contains(labels, l => l.Contains("intent-issue-in-progress", StringComparison.Ordinal));
        Assert.Contains(labels, l => l.Contains("intent-pr-created", StringComparison.Ordinal));
        Assert.Contains(labels, l => l.Contains("NEVER applies `intent-target`", StringComparison.Ordinal));
    }

    [Fact]
    public void IssueToPr_MissingIssue_EmitsStructuredRefusal_NoSteps_ExitOne()
    {
        using var writer = new StringWriter();
        var exit = TaskCommand.Execute(
            CreateContext(),
            ["issue-to-pr", "--repo", "J-Tech-Japan/intent-system",
             "--workdir", "/tmp/child", "--format", "json"],
            writer);

        Assert.Equal(1, exit);
        using var doc = JsonDocument.Parse(writer.ToString());
        var root = doc.RootElement;
        Assert.Equal("issue-to-pr", root.GetProperty("task").GetString());
        var refusals = root.GetProperty("refusals").EnumerateArray()
            .Select(e => e.GetString()!).ToArray();
        Assert.Contains(refusals, r => r.Contains("--issue", StringComparison.Ordinal));
        // Refusal plans never emit steps — the controller must repair its
        // input before any mutation guidance appears.
        Assert.Empty(root.GetProperty("steps").EnumerateArray());
    }

    [Fact]
    public void IssueToPr_NegativeIssue_RejectsBeforeBuildingPlan()
    {
        using var writer = new StringWriter();
        var exit = TaskCommand.Execute(
            CreateContext(),
            ["issue-to-pr", "--repo", "x/y", "--issue", "-1", "--workdir", "/tmp/c"],
            writer);

        Assert.Equal(1, exit);
        Assert.Contains("--issue must be a positive integer", writer.ToString(), StringComparison.Ordinal);
    }

    // ---- task review-pr -----------------------------------------------------

    [Fact]
    public void ReviewPr_HappyPath_JsonContractContainsThreeDecisionBranchesAndG316Hooks()
    {
        using var writer = new StringWriter();
        var exit = TaskCommand.Execute(
            CreateContext(),
            ["review-pr", "--repo", "J-Tech-Japan/intent-system", "--pr", "501",
             "--domain", "intent-cli", "--format", "json"],
            writer);

        Assert.Equal(0, exit);
        using var doc = JsonDocument.Parse(writer.ToString());
        var root = doc.RootElement;
        Assert.Equal("review-pr", root.GetProperty("task").GetString());
        Assert.Equal(501, root.GetProperty("pr").GetInt32());
        Assert.Equal("intent-cli", root.GetProperty("domain").GetString());

        var steps = root.GetProperty("steps").EnumerateArray().Select(e => e.GetString()!).ToArray();
        // Decision A (approve), Decision B (request-update), Decision C
        // (host-metadata-blocked → publish-recovery / reconcile, never PR
        // comment) all appear in the contract.
        Assert.Contains(steps, s => s.Contains("review closeout-plan --pr 501", StringComparison.Ordinal));
        Assert.Contains(steps, s => s.Contains("guide review --pr 501", StringComparison.Ordinal));
        Assert.Contains(steps, s => s.Contains("pr-transition --transition approved", StringComparison.Ordinal));
        Assert.Contains(steps, s => s.Contains("closeout pr --pr 501", StringComparison.Ordinal));
        Assert.Contains(steps, s => s.Contains("pr-transition --transition request-update", StringComparison.Ordinal));
        Assert.Contains(steps, s => s.Contains("automation publish-recovery", StringComparison.Ordinal));
        Assert.Contains(steps, s => s.Contains("automation reconcile --lane host-review", StringComparison.Ordinal));
        // base-branch-check is the FIRST validation step.
        Assert.Contains(steps, s => s.Contains("base-branch-check", StringComparison.Ordinal)
            && s.Contains("--pr 501", StringComparison.Ordinal));

        // G316 packet/intent-aware: approval_summary_requirements text is
        // surfaced via approval-summary cite + tests-pass evidence.
        Assert.Contains(steps, s => s.Contains("approval_summary_requirements", StringComparison.Ordinal));
        Assert.Contains(steps, s => s.Contains("request_update_requirements", StringComparison.Ordinal));
        Assert.Contains(steps, s => s.Contains("intent_reference_paths", StringComparison.Ordinal));

        var aborts = root.GetProperty("abort_conditions")
            .EnumerateArray().Select(e => e.GetString()!).ToArray();
        Assert.Contains(aborts, a => a.Contains("Tests-only evidence", StringComparison.Ordinal)
            || a.Contains("only `tests passed`", StringComparison.Ordinal));
        Assert.Contains(aborts, a => a.Contains("Closes #<source-issue>", StringComparison.Ordinal)
            || a.Contains("G311", StringComparison.Ordinal));

        // Draft-aware approval (G297) is in the preconditions.
        var preconds = root.GetProperty("preconditions")
            .EnumerateArray().Select(e => e.GetString()!).ToArray();
        Assert.Contains(preconds, p => p.Contains("isDraft", StringComparison.Ordinal)
            || p.Contains("G297", StringComparison.Ordinal));
    }

    [Fact]
    public void ReviewPr_MissingDomain_RefusesBeforeAnyApprovalSteps()
    {
        using var writer = new StringWriter();
        var exit = TaskCommand.Execute(
            CreateContext(),
            ["review-pr", "--repo", "J-Tech-Japan/intent-system", "--pr", "501", "--format", "json"],
            writer);

        Assert.Equal(1, exit);
        using var doc = JsonDocument.Parse(writer.ToString());
        var refusals = doc.RootElement.GetProperty("refusals")
            .EnumerateArray().Select(e => e.GetString()!).ToArray();
        Assert.Contains(refusals, r => r.Contains("--domain", StringComparison.Ordinal));
        Assert.Empty(doc.RootElement.GetProperty("steps").EnumerateArray());
    }

    // ---- task fix-pr-comments ----------------------------------------------

    [Fact]
    public void FixPrComments_HappyPath_NarrowsToLatestActionableCommentOnly()
    {
        using var writer = new StringWriter();
        var exit = TaskCommand.Execute(
            CreateContext(),
            ["fix-pr-comments", "--repo", "J-Tech-Japan/intent-system", "--pr", "501",
             "--workdir", "/tmp/child", "--format", "json"],
            writer);

        Assert.Equal(0, exit);
        using var doc = JsonDocument.Parse(writer.ToString());
        var steps = doc.RootElement.GetProperty("steps")
            .EnumerateArray().Select(e => e.GetString()!).ToArray();
        Assert.Contains(steps, s => s.Contains("worker claim --kind pr", StringComparison.Ordinal)
            && s.Contains("--number 501", StringComparison.Ordinal));
        Assert.Contains(steps, s => s.Contains("gh pr view 501", StringComparison.Ordinal)
            && s.Contains("headRefName", StringComparison.Ordinal));
        Assert.Contains(steps, s => s.Contains("worker result-summary --kind pr-comment-fix", StringComparison.Ordinal)
            && s.Contains("--outcome repair-pushed", StringComparison.Ordinal));
        Assert.Contains(steps, s => s.Contains("worker complete --kind pr", StringComparison.Ordinal)
            && s.Contains("--outcome repair-pushed", StringComparison.Ordinal)
            && s.Contains("--write", StringComparison.Ordinal));

        // Preconditions explicitly forbid rediscovery via worker next-action
        // while a pr-comment-fix claim is active.
        var preconds = doc.RootElement.GetProperty("preconditions")
            .EnumerateArray().Select(e => e.GetString()!).ToArray();
        Assert.Contains(preconds, p => p.Contains("Do NOT rediscover", StringComparison.Ordinal)
            || p.Contains("worker next-action", StringComparison.Ordinal));

        // Abort conditions name scope-drift.
        var aborts = doc.RootElement.GetProperty("abort_conditions")
            .EnumerateArray().Select(e => e.GetString()!).ToArray();
        Assert.Contains(aborts, a => a.Contains("Scope drift", StringComparison.Ordinal));

        // Labels: intent-pr-update-in-progress → intent-pr-rereview-ready.
        var labels = doc.RootElement.GetProperty("label_transitions")
            .EnumerateArray().Select(e => e.GetString()!).ToArray();
        Assert.Contains(labels, l => l.Contains("intent-pr-update-in-progress", StringComparison.Ordinal));
        Assert.Contains(labels, l => l.Contains("intent-pr-rereview-ready", StringComparison.Ordinal));
    }

    [Fact]
    public void FixPrComments_MissingWorkdir_StructuredRefusal()
    {
        using var writer = new StringWriter();
        var exit = TaskCommand.Execute(
            CreateContext(),
            ["fix-pr-comments", "--repo", "x/y", "--pr", "1", "--format", "json"],
            writer);

        Assert.Equal(1, exit);
        using var doc = JsonDocument.Parse(writer.ToString());
        var refusals = doc.RootElement.GetProperty("refusals")
            .EnumerateArray().Select(e => e.GetString()!).ToArray();
        Assert.Contains(refusals, r => r.Contains("--workdir", StringComparison.Ordinal));
    }

    // ---- task publish-next-issue -------------------------------------------

    [Fact]
    public void PublishNextIssue_HappyPath_BoundedToSingleIssuePerWake()
    {
        using var writer = new StringWriter();
        var exit = TaskCommand.Execute(
            CreateContext(),
            ["publish-next-issue", "--domain", "intent-cli",
             "--target-repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        Assert.Equal(0, exit);
        using var doc = JsonDocument.Parse(writer.ToString());
        var root = doc.RootElement;
        Assert.Equal("publish-next-issue", root.GetProperty("task").GetString());

        var steps = root.GetProperty("steps")
            .EnumerateArray().Select(e => e.GetString()!).ToArray();
        Assert.Contains(steps, s => s.Contains("intent next-slice --dry-run", StringComparison.Ordinal));
        Assert.Contains(steps, s => s.Contains("packet draft", StringComparison.Ordinal)
            && s.Contains("--dry-run", StringComparison.Ordinal));
        Assert.Contains(steps, s => s.Contains("issue publish-flow", StringComparison.Ordinal)
            && s.Contains("--write", StringComparison.Ordinal));
        Assert.Contains(steps, s => s.Contains("automation issue-publish", StringComparison.Ordinal)
            && s.Contains("--write", StringComparison.Ordinal));

        var preconds = root.GetProperty("preconditions")
            .EnumerateArray().Select(e => e.GetString()!).ToArray();
        Assert.Contains(preconds, p => p.Contains("WIP cap", StringComparison.Ordinal));
        Assert.Contains(preconds, p => p.Contains("host-sync-preflight", StringComparison.Ordinal));

        var aborts = root.GetProperty("abort_conditions")
            .EnumerateArray().Select(e => e.GetString()!).ToArray();
        Assert.Contains(aborts, a => a.Contains("clarification-required", StringComparison.Ordinal));
        Assert.Contains(aborts, a => a.Contains("WIP cap", StringComparison.Ordinal));

        var notes = root.GetProperty("notes")
            .EnumerateArray().Select(e => e.GetString()!).ToArray();
        Assert.Contains(notes, n => n.Contains("at most ONE issue is published per wake", StringComparison.OrdinalIgnoreCase)
            || n.Contains("ONE issue", StringComparison.Ordinal));
    }

    [Fact]
    public void PublishNextIssue_MissingTargetRepo_StructuredRefusal()
    {
        using var writer = new StringWriter();
        var exit = TaskCommand.Execute(
            CreateContext(),
            ["publish-next-issue", "--domain", "intent-cli", "--format", "json"],
            writer);

        Assert.Equal(1, exit);
        using var doc = JsonDocument.Parse(writer.ToString());
        var refusals = doc.RootElement.GetProperty("refusals")
            .EnumerateArray().Select(e => e.GetString()!).ToArray();
        Assert.Contains(refusals, r => r.Contains("--target-repo", StringComparison.Ordinal));
    }

    // ---- shared surface ----------------------------------------------------

    [Fact]
    public void Markdown_DefaultFormat_RendersNumberedSectionsAndPathsAsCode()
    {
        using var writer = new StringWriter();
        var exit = TaskCommand.Execute(
            CreateContext(),
            ["issue-to-pr", "--repo", "J-Tech-Japan/intent-system", "--issue", "5000",
             "--workdir", "/tmp/child"],
            writer);

        Assert.Equal(0, exit);
        var output = writer.ToString();
        Assert.Contains("# task issue-to-pr", output, StringComparison.Ordinal);
        Assert.Contains("- repo: `J-Tech-Japan/intent-system`", output, StringComparison.Ordinal);
        Assert.Contains("- issue: #5000", output, StringComparison.Ordinal);
        Assert.Contains("- workdir: `/tmp/child`", output, StringComparison.Ordinal);
        Assert.Contains("## Preconditions", output, StringComparison.Ordinal);
        Assert.Contains("## Steps", output, StringComparison.Ordinal);
        Assert.Contains("## Label transitions", output, StringComparison.Ordinal);
        Assert.Contains("## Abort conditions", output, StringComparison.Ordinal);
    }

    [Fact]
    public void UnknownKind_ReturnsUsageError()
    {
        using var writer = new StringWriter();
        var exit = TaskCommand.Execute(CreateContext(), ["unknown-kind"], writer);
        Assert.Equal(1, exit);
        Assert.Contains("Unknown task kind 'unknown-kind'", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void NoArgs_PrintsUsage_ExitOne()
    {
        using var writer = new StringWriter();
        var exit = TaskCommand.Execute(CreateContext(), Array.Empty<string>(), writer);
        Assert.Equal(1, exit);
        Assert.Contains("Usage: intent-cli task", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void HelpFlag_PrintsUsage_ExitZero()
    {
        using var writer = new StringWriter();
        var exit = TaskCommand.Execute(CreateContext(), ["--help"], writer);
        Assert.Equal(0, exit);
        var output = writer.ToString();
        Assert.Contains("issue-to-pr", output, StringComparison.Ordinal);
        Assert.Contains("review-pr", output, StringComparison.Ordinal);
        Assert.Contains("fix-pr-comments", output, StringComparison.Ordinal);
        Assert.Contains("publish-next-issue", output, StringComparison.Ordinal);
    }

    [Fact]
    public void UnknownFormat_RejectedByParser()
    {
        using var writer = new StringWriter();
        var exit = TaskCommand.Execute(
            CreateContext(),
            ["issue-to-pr", "--repo", "x/y", "--issue", "1", "--workdir", "/tmp/c", "--format", "yaml"],
            writer);
        Assert.Equal(1, exit);
        Assert.Contains("--format must be 'markdown' or 'json'", writer.ToString(), StringComparison.Ordinal);
    }

    private static CliContext CreateContext()
    {
        return new CliContext
        {
            RepoRoot = Path.GetTempPath(),
            Config = new CliConfig
            {
                Project = new ProjectConfig
                {
                    Domain = "intent-cli",
                    ArtifactRoot = ".intent-cli",
                    WorktreeRoot = ".intent-cli/worktrees"
                }
            }
        };
    }
}
