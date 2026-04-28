using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

public sealed class StatusBriefCommandTests
{
    [Fact]
    public void Execute_GivenNoQueueStateAndNoClarification_RecommendsNoActionableItem()
    {
        // Scenario 1 of the four-required cases (G179): nothing to do, no actionable item.
        using var workspace = new StatusBriefWorkspace();
        using var writer = new StringWriter();

        var exitCode = StatusBriefCommand.Execute(workspace.Context, [], writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains($"Recommended action: {StatusBriefRecommendation.NoActionableItem}", output, StringComparison.Ordinal);
        Assert.Contains("In-flight units: -", output, StringComparison.Ordinal);
        Assert.Contains("Clarification open: no", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenActiveQueueItem_RecommendsSkipDueToWip()
    {
        // Scenario 2: WIP present (Active), no review, no clarification.
        using var workspace = new StatusBriefWorkspace();
        workspace.WriteQueueState(
            """
            {
              "schema_version": "1",
              "updated_at": "2026-04-28T23:00:00Z",
              "items": [
                {
                  "execution_unit": "G180",
                  "title": "context collect command",
                  "state": "active",
                  "dependencies": [],
                  "blocked_by": [],
                  "clarification_return_path": "intents/intent-cli/clarifications/open.md",
                  "packet_paths": {
                    "implementation": ".intent-cli/issues/G180/implementation.md",
                    "review_context": ".intent-cli/issues/G180/review-context.md",
                    "yaml": ".intent-cli/issues/G180/packet.yaml"
                  },
                  "worker_role": "coder",
                  "review_role": "reviewer",
                  "priority": "high"
                }
              ]
            }
            """);

        using var writer = new StringWriter();
        var exitCode = StatusBriefCommand.Execute(workspace.Context, [], writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains($"Recommended action: {StatusBriefRecommendation.SkipDueToWip}", output, StringComparison.Ordinal);
        Assert.Contains("In-flight units: G180", output, StringComparison.Ordinal);
        Assert.Contains("WIP present: yes", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenOpenClarificationFile_RecommendsClarificationRequiredOverEverythingElse()
    {
        // Scenario 3: clarification blocker present. Even with WIP and a queued candidate
        // present, clarification-required wins (HARD CLARIFICATION precedence).
        using var workspace = new StatusBriefWorkspace();
        workspace.WriteQueueState(
            """
            {
              "schema_version": "1",
              "updated_at": "2026-04-28T23:00:00Z",
              "items": [
                {
                  "execution_unit": "G181",
                  "title": "clarify draft command",
                  "state": "active",
                  "dependencies": [],
                  "blocked_by": [],
                  "clarification_return_path": "intents/intent-cli/clarifications/open.md",
                  "packet_paths": {
                    "implementation": ".intent-cli/issues/G181/implementation.md",
                    "review_context": ".intent-cli/issues/G181/review-context.md",
                    "yaml": ".intent-cli/issues/G181/packet.yaml"
                  },
                  "worker_role": "coder",
                  "review_role": "reviewer",
                  "priority": "high"
                },
                {
                  "execution_unit": "G182",
                  "title": "clarify record command",
                  "state": "queued",
                  "dependencies": [],
                  "blocked_by": [],
                  "clarification_return_path": "intents/intent-cli/clarifications/open.md",
                  "packet_paths": {
                    "implementation": ".intent-cli/issues/G182/implementation.md",
                    "review_context": ".intent-cli/issues/G182/review-context.md",
                    "yaml": ".intent-cli/issues/G182/packet.yaml"
                  },
                  "worker_role": "coder",
                  "review_role": "reviewer",
                  "priority": "high"
                }
              ]
            }
            """);
        workspace.WriteClarificationOpen(
            """
            # intent-cli clarifications

            Some durable prose that should not by itself force the brief to
            report a clarification.

            ## Current Open Blockers

            - Should we use plain text format by default for `status brief`?
            """);

        using var writer = new StringWriter();
        var exitCode = StatusBriefCommand.Execute(
            workspace.Context,
            ["--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        using var document = JsonDocument.Parse(output);
        var root = document.RootElement;
        Assert.Equal(
            StatusBriefRecommendation.ClarificationRequired,
            root.GetProperty("recommended_action").GetString());
        Assert.True(root.GetProperty("clarification_open").GetBoolean());
        Assert.True(root.GetProperty("wip_present").GetBoolean());
        Assert.Equal("intent-cli", root.GetProperty("domain").GetString());
    }

    [Fact]
    public void Execute_GivenMalformedQueueState_RecommendsInspectManuallyAndDoesNotThrow()
    {
        // Scenario 4: degraded state — queue-state.json exists but cannot be parsed.
        // Must not throw; must return a deterministic inspect-manually fallback.
        using var workspace = new StatusBriefWorkspace();
        workspace.WriteQueueState("{ this is not valid json at all");

        using var writer = new StringWriter();
        var exitCode = StatusBriefCommand.Execute(workspace.Context, [], writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains($"Recommended action: {StatusBriefRecommendation.InspectManually}", output, StringComparison.Ordinal);
        Assert.Contains("Queue state: present-unreadable", output, StringComparison.Ordinal);
        Assert.Contains("Notes:", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenReviewItem_RecommendsReviewCloseout()
    {
        using var workspace = new StatusBriefWorkspace();
        workspace.WriteQueueState(
            """
            {
              "schema_version": "1",
              "updated_at": "2026-04-28T23:00:00Z",
              "items": [
                {
                  "execution_unit": "G179",
                  "title": "status brief",
                  "state": "review",
                  "dependencies": [],
                  "blocked_by": [],
                  "clarification_return_path": "intents/intent-cli/clarifications/open.md",
                  "packet_paths": {
                    "implementation": ".intent-cli/issues/G179/implementation.md",
                    "review_context": ".intent-cli/issues/G179/review-context.md",
                    "yaml": ".intent-cli/issues/G179/packet.yaml"
                  },
                  "worker_role": "coder",
                  "review_role": "reviewer",
                  "priority": "high"
                }
              ]
            }
            """);

        using var writer = new StringWriter();
        var exitCode = StatusBriefCommand.Execute(workspace.Context, [], writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains($"Recommended action: {StatusBriefRecommendation.ReviewCloseout}", output, StringComparison.Ordinal);
        Assert.Contains("Review units: G179", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenQueuedItemAndNoBlockers_RecommendsIssueCutReady()
    {
        using var workspace = new StatusBriefWorkspace();
        workspace.WriteQueueState(
            """
            {
              "schema_version": "1",
              "updated_at": "2026-04-28T23:00:00Z",
              "items": [
                {
                  "execution_unit": "G180",
                  "title": "context collect",
                  "state": "queued",
                  "dependencies": [],
                  "blocked_by": [],
                  "clarification_return_path": "intents/intent-cli/clarifications/open.md",
                  "packet_paths": {
                    "implementation": ".intent-cli/issues/G180/implementation.md",
                    "review_context": ".intent-cli/issues/G180/review-context.md",
                    "yaml": ".intent-cli/issues/G180/packet.yaml"
                  },
                  "worker_role": "coder",
                  "review_role": "reviewer",
                  "priority": "high"
                }
              ]
            }
            """);

        using var writer = new StringWriter();
        var exitCode = StatusBriefCommand.Execute(workspace.Context, [], writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains($"Recommended action: {StatusBriefRecommendation.IssueCutReady}", output, StringComparison.Ordinal);
        Assert.Contains("Next candidate: G180", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenRealisticNoBlockerClarificationFile_DoesNotForceClarificationRequired()
    {
        // Review fix for G179: parent-host `intents/<domain>/clarifications/open.md` is a
        // durable markdown artifact and remains non-empty even when there is no root
        // blocker. The brief must parse the structured "Current Open Blockers" section
        // and treat the explicit no-blocker sentinel as not-open, so review/WIP/issue-cut
        // recommendations are not masked by `clarification-required`.
        using var workspace = new StatusBriefWorkspace();
        workspace.WriteQueueState(
            """
            {
              "schema_version": "1",
              "updated_at": "2026-04-28T23:00:00Z",
              "items": [
                {
                  "execution_unit": "G180",
                  "title": "context collect",
                  "state": "queued",
                  "dependencies": [],
                  "blocked_by": [],
                  "clarification_return_path": "intents/intent-cli/clarifications/open.md",
                  "packet_paths": {
                    "implementation": ".intent-cli/issues/G180/implementation.md",
                    "review_context": ".intent-cli/issues/G180/review-context.md",
                    "yaml": ".intent-cli/issues/G180/packet.yaml"
                  },
                  "worker_role": "coder",
                  "review_role": "reviewer",
                  "priority": "high"
                }
              ]
            }
            """);
        workspace.WriteClarificationOpen(
            """
            # intent-cli clarifications

            このファイルは durable な markdown artifact として残し続けるため、本文が
            空になることはない。clarification-required を出すのは「現時点で実際に
            blocker が立っているとき」だけにする。

            ## Recently Resolved

            - G178 pending alias を queued に正規化する方針で確定。

            ## Current Open Blockers

            - 現時点で child issue cut を要する root blocker はない。
            """);

        using var writer = new StringWriter();
        var exitCode = StatusBriefCommand.Execute(
            workspace.Context,
            ["--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.False(root.GetProperty("clarification_open").GetBoolean());
        Assert.NotEqual(
            StatusBriefRecommendation.ClarificationRequired,
            root.GetProperty("recommended_action").GetString());
        // With no WIP / no review and a deps-satisfied queued item, the next
        // recommendation should fall through to issue-cut-ready.
        Assert.Equal(
            StatusBriefRecommendation.IssueCutReady,
            root.GetProperty("recommended_action").GetString());
    }

    [Fact]
    public void Execute_GivenClarificationFileWithoutCurrentOpenBlockersHeading_DoesNotMarkClarificationOpen()
    {
        // Defensive: a clarifications/open.md that contains only prose (no
        // structured "## Current Open Blockers" heading) should not be misread
        // as an open blocker.
        using var workspace = new StatusBriefWorkspace();
        workspace.WriteClarificationOpen(
            """
            # intent-cli clarifications

            参考メモ。実際の blocker 一覧は別ドキュメントで管理している想定の
            導入文だけのファイル。
            """);

        using var writer = new StringWriter();
        var exitCode = StatusBriefCommand.Execute(
            workspace.Context,
            ["--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        Assert.False(document.RootElement.GetProperty("clarification_open").GetBoolean());
    }

    [Fact]
    public void Execute_GivenJsonFormatRequest_EmitsParsableJsonWithStableFieldNames()
    {
        using var workspace = new StatusBriefWorkspace();
        using var writer = new StringWriter();

        var exitCode = StatusBriefCommand.Execute(
            workspace.Context,
            ["--format", "json", "--domain", "custom-domain"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.Equal("custom-domain", root.GetProperty("domain").GetString());
        Assert.False(root.GetProperty("queue_state_present").GetBoolean());
        Assert.False(root.GetProperty("queue_state_readable").GetBoolean());
        Assert.False(root.GetProperty("wip_present").GetBoolean());
        Assert.Equal(
            StatusBriefRecommendation.NoActionableItem,
            root.GetProperty("recommended_action").GetString());
    }

    [Fact]
    public void Execute_GivenUnknownArgument_ReturnsErrorExitCodeAndMessage()
    {
        using var workspace = new StatusBriefWorkspace();
        using var writer = new StringWriter();

        var exitCode = StatusBriefCommand.Execute(workspace.Context, ["--bogus"], writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("--bogus", writer.ToString(), StringComparison.Ordinal);
    }

    private sealed class StatusBriefWorkspace : IDisposable
    {
        private readonly string rootPath = Directory
            .CreateTempSubdirectory("status-brief-tests-")
            .FullName;

        public StatusBriefWorkspace()
        {
            Directory.CreateDirectory(Path.Combine(rootPath, ".intent-cli"));
            Context = new CliContext
            {
                RepoRoot = rootPath,
                Config = new CliConfig
                {
                    Project = new ProjectConfig
                    {
                        Domain = "intent-cli",
                        ArtifactRoot = ".intent-cli"
                    }
                }
            };
        }

        public CliContext Context { get; }

        public void WriteQueueState(string content)
        {
            File.WriteAllText(Context.GetQueueStatePath(), content);
        }

        public void WriteClarificationOpen(string content)
        {
            var path = Path.Combine(rootPath, "intents", "intent-cli", "clarifications");
            Directory.CreateDirectory(path);
            File.WriteAllText(Path.Combine(path, "open.md"), content);
        }

        public void Dispose()
        {
            if (Directory.Exists(rootPath))
            {
                Directory.Delete(rootPath, recursive: true);
            }
        }
    }
}
