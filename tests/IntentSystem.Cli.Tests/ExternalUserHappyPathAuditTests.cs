using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G340: external-user happy-path audit. Exercises the full
/// documented journey from <c>init</c> guidance through
/// <c>interview</c>, <c>packet</c>, <c>issue</c> publish,
/// implementation-loop, review-next-slice-loop, and bug-to-intent-repair
/// guidance — every workflow surface an external agent uses for the
/// first time without prior knowledge of intent-system internals.
///
/// The audit guards against regressions that would reintroduce:
/// <list type="bullet">
///   <item>missing or empty workflow guides;</item>
///   <item>generated guidance that instructs agents to read local
///         skill files / copied prompt files / <c>intents/rules/**</c>
///         (the G334 / G338 / G339 anti-local-rule baseline);</item>
///   <item>child implementation guidance that breaks when there is no
///         <c>.intent-cli/</c> in cwd (the G300 / G330 / G333
///         child-cwd contract);</item>
///   <item>generic "true-idle" / "unsupported" responses that would
///         hide a missing-surface gap from the operator.</item>
/// </list>
///
/// Failure messages name the specific phase id and the specific
/// anchor so the operator can repair the gap without digging.
/// </summary>
public sealed class ExternalUserHappyPathAuditTests
{
    /// <summary>
    /// G340: the canonical guide-task surface set. Each entry pins
    /// a phase id (matching <see cref="GuideHelpCommand.WorkflowGuides"/>),
    /// the args to dispatch through <see cref="CommandRouter.Execute"/>,
    /// and whether the surface MUST work from a child cwd without
    /// `.intent-cli/`. Adding a new G34X workflow task means adding
    /// a row here too — the parity test guards drift.
    /// </summary>
    private static readonly (string Phase, string[] Args, bool ChildCwdSafe)[] WorkflowGuideTasks = new[]
    {
        ("init",
         new[] { "guide", "workflow", "task", "init-host", "--format", "json" },
         false),
        ("interview",
         new[] { "guide", "workflow", "task", "intent-interview", "--format", "json" },
         true),
        ("packet",
         new[] { "guide", "workflow", "task", "packet-draft", "--format", "json" },
         true),
        ("issue",
         new[] { "guide", "workflow", "task", "issue-publish", "--format", "json" },
         true),
        ("implementation-loop",
         new[] { "guide", "workflow", "task", "implementation-loop", "--target-repo", "example/repo", "--agent", "claude", "--frequency", "5m", "--format", "json" },
         true),
        ("review-next-slice-loop",
         new[] { "guide", "workflow", "task", "review-next-slice-loop", "--domain", "intent-cli", "--target-repo", "example/repo", "--agent", "claude", "--frequency", "20m", "--format", "json" },
         true),
        ("bug-to-intent-repair",
         new[] { "guide", "workflow", "task", "bug-to-intent-repair", "--format", "json" },
         true),
        ("supervision-setup",
         new[] { "guide", "workflow", "task", "supervision-setup", "--format", "json" },
         true)
    };

    [Fact]
    public void EveryRequiredWorkflowGuide_ExistsAndReturnsNonEmptyContent()
    {
        // Acceptance criterion: "A test or command verifies every
        // required workflow guide exists and returns non-empty
        // content."
        foreach (var (phase, args, _) in WorkflowGuideTasks)
        {
            using var writer = new StringWriter();
            var exit = CommandRouter.Execute(args, CreateContext(), writer);
            var output = writer.ToString();

            Assert.True(
                exit == 0,
                $"Phase `{phase}` guide returned non-zero exit ({exit}). Command: `intent-cli {string.Join(" ", args)}`. Output: {Truncate(output)}");
            Assert.False(
                string.IsNullOrWhiteSpace(output),
                $"Phase `{phase}` guide produced empty output. Command: `intent-cli {string.Join(" ", args)}`.");

            // The output must be parsable JSON (every task takes
            // --format json above) — a stale alias that silently
            // emitted markdown would otherwise pass the non-empty
            // check.
            try
            {
                using var _ = JsonDocument.Parse(output);
            }
            catch (JsonException exception)
            {
                Assert.Fail(
                    $"Phase `{phase}` guide returned non-JSON despite `--format json`: {exception.Message}. Output: {Truncate(output)}");
            }
        }
    }

    [Fact]
    public void NoWorkflowGuide_InstructsAgentToReadLocalSkillFilesOrCopiedPrompts()
    {
        // Acceptance criterion: "The audit checks no generated
        // guidance references local skills or copied prompt files."
        //
        // The guides MAY mention these paths in their forbidden-
        // sources list (e.g. "Do not read intents/rules/** ... use
        // intent-cli guide instead"). Those negative mentions are
        // correct. The audit flags POSITIVE instructions only.
        var positiveInstructionAnchors = new[]
        {
            "Read intents/rules/",
            "Consult intents/rules/",
            "Refer to intents/rules/",
            "Open the local skill file",
            "Read the copied prompt",
            // `.claude/skills/` and `.skills/` are local-skill
            // directories; no guide should point an agent at them.
            ".claude/skills/",
            ".skills/gh-",
            "Open `.claude/",
            "Read `gh-issue-to-pr",
            "Read `gh-fix-pr-comment"
        };

        foreach (var (phase, args, _) in WorkflowGuideTasks)
        {
            using var writer = new StringWriter();
            var exit = CommandRouter.Execute(args, CreateContext(), writer);
            Assert.Equal(0, exit);
            var output = writer.ToString();

            foreach (var anchor in positiveInstructionAnchors)
            {
                Assert.False(
                    output.Contains(anchor, StringComparison.Ordinal),
                    $"Phase `{phase}` guide instructs the agent to read a local skill / prompt: anchor `{anchor}` appeared in the output. Local-rule dependencies break the G334 / G338 / G339 chat-first baseline; route the agent through `intent-cli guide ...` instead.");
            }
        }
    }

    [Fact]
    public void EveryWorkflowGuide_NamesAtLeastOneIntentCliCommand()
    {
        // The point of a workflow guide is to name the real
        // intent-cli surface(s) the agent runs. A guide that omits
        // `intent-cli` is a discovery dead-end — the operator gets
        // text without a command to execute.
        var intentCliMention = new System.Text.RegularExpressions.Regex(
            @"intent-cli\s+",
            System.Text.RegularExpressions.RegexOptions.Compiled);

        foreach (var (phase, args, _) in WorkflowGuideTasks)
        {
            using var writer = new StringWriter();
            var exit = CommandRouter.Execute(args, CreateContext(), writer);
            Assert.Equal(0, exit);
            var output = writer.ToString();

            Assert.True(
                intentCliMention.IsMatch(output),
                $"Phase `{phase}` guide does not name any `intent-cli` command — the operator has no concrete surface to execute. Add at least one `intent-cli <group> <subcommand>` mention so the discovery chain is unbroken.");
        }
    }

    [Fact]
    public void ChildImplementationGuides_WorkWithoutDotIntentCliInCwd()
    {
        // Acceptance criterion: "The audit checks child implementation
        // guidance works without dot-intent-cli." Build a fresh tmp
        // cwd (no .intent-cli/) and re-invoke every child-cwd-safe
        // phase from it. Each must still emit non-empty guidance
        // and not fail-closed with the G299 host-state warning.
        var tempCwd = Path.Combine(Path.GetTempPath(), $"intent-cli-audit-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempCwd);
        try
        {
            var childCwdContext = new CliContext
            {
                RepoRoot = tempCwd,
                Config = new CliConfig
                {
                    Project = new ProjectConfig
                    {
                        Domain = "bootstrap",
                        ArtifactRoot = ".intent-cli",
                        WorktreeRoot = ".intent-cli/worktrees"
                    }
                }
            };

            foreach (var (phase, args, childCwdSafe) in WorkflowGuideTasks)
            {
                if (!childCwdSafe)
                {
                    continue;
                }
                using var writer = new StringWriter();
                var exit = CommandRouter.Execute(args, childCwdContext, writer);
                var output = writer.ToString();

                Assert.True(
                    exit == 0,
                    $"Phase `{phase}` (marked child-cwd-safe) failed when invoked from a fresh tmp cwd without `.intent-cli/` (exit {exit}). Either fix the guide to not require host state or drop the child-cwd-safe flag. Output: {Truncate(output)}");
                Assert.False(
                    string.IsNullOrWhiteSpace(output),
                    $"Phase `{phase}` produced empty output from a child cwd. Child-cwd guides must emit guidance regardless of `.intent-cli/` presence (G300 / G330 / G333).");
                Assert.False(
                    output.Contains("missing host state", StringComparison.Ordinal),
                    $"Phase `{phase}` returned the G299 missing-host-state guidance from a child cwd — but this guide is marked child-cwd-safe. Either the bootstrap allow-list in `Program.cs` is missing the entry, or the guide consults host state it should not need.");
            }
        }
        finally
        {
            try { Directory.Delete(tempCwd, recursive: true); }
            catch { /* best-effort cleanup */ }
        }
    }

    [Fact]
    public void AuditCoverage_MatchesWorkflowGuidePointers()
    {
        // Parity guard: every phase id in this audit MUST appear in
        // `GuideHelpCommand.WorkflowGuides`, and every phase id in
        // `WorkflowGuides` that corresponds to a `guide workflow
        // task` surface MUST appear in this audit. New workflow
        // tasks added by future G34X slices fail this guard until
        // the audit covers them — that is intentional.
        var workflowGuidePhases = GuideHelpCommand.WorkflowGuides
            .Select(p => p.Phase)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var (phase, _, _) in WorkflowGuideTasks)
        {
            Assert.True(
                workflowGuidePhases.Contains(phase),
                $"Audit phase `{phase}` is not listed in `GuideHelpCommand.WorkflowGuides`. Add the row or drop the audit entry.");
        }

        // Every phase whose canonical command starts with
        // `intent-cli guide workflow task` MUST be exercised here.
        // Sibling phases (`automation`, `bug-repair`) point at
        // host-state surfaces, not guide tasks, so they are out of
        // scope for the workflow-task audit.
        var auditedPhases = WorkflowGuideTasks
            .Select(t => t.Phase)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var pointer in GuideHelpCommand.WorkflowGuides)
        {
            var routesThroughGuideTask = pointer.Command
                .Contains("guide workflow task", StringComparison.Ordinal);
            if (!routesThroughGuideTask)
            {
                continue;
            }
            Assert.True(
                auditedPhases.Contains(pointer.Phase),
                $"Phase `{pointer.Phase}` routes through `guide workflow task` but the external-user happy-path audit does not exercise it. Add a row to `WorkflowGuideTasks` so a fresh agent's first wake is covered.");
        }
    }

    [Fact]
    public void AuditFailures_AreActionableNotGeneric()
    {
        // Acceptance criterion: "The audit reports actionable
        // missing-surface failures instead of true-idle or generic
        // unsupported messages."
        //
        // This is a meta-test: the assertion messages elsewhere in
        // this file must name a specific phase and a specific
        // anchor / cause. Failing for "test failed" or "exit code
        // was non-zero" without naming the phase makes the audit
        // useless when it actually catches drift.
        //
        // We verify by simulating a single phase's failure path and
        // checking that the resulting message includes the phase id
        // and a recognizable cause. We do not actually trip a real
        // assertion — we just verify the message-template literals
        // contain the expected anchors.
        var auditSource = File.ReadAllText(Path.Combine(FindRepoRoot(), "tests/IntentSystem.Cli.Tests/ExternalUserHappyPathAuditTests.cs"));
        // Every Assert message in this file must mention "phase"
        // and use the `{phase}` template placeholder.
        var assertMessages = System.Text.RegularExpressions.Regex.Matches(
            auditSource,
            @"\$""Phase `\{phase\}` [^""]+""",
            System.Text.RegularExpressions.RegexOptions.Compiled);
        Assert.True(
            assertMessages.Count >= 5,
            "Audit assertion messages must name the specific phase that failed. Expected at least 5 `Phase `{phase}`` template strings in this file; found " + assertMessages.Count + ".");
    }

    private static CliContext CreateContext()
    {
        // Bootstrap-style context (no host state required) so the
        // audit runs from a host-less environment — matching the
        // operator's first wake from a fresh terminal.
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

    private static string FindRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !Directory.Exists(Path.Combine(dir, "src")))
        {
            dir = Path.GetDirectoryName(dir);
        }
        Assert.NotNull(dir);
        return dir!;
    }

    private static string Truncate(string s)
    {
        const int limit = 400;
        return s.Length <= limit ? s : s.Substring(0, limit) + "…";
    }
}
