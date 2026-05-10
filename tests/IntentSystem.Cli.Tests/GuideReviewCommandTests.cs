using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

public sealed class GuideReviewCommandTests
{
    [Fact]
    public void Execute_GivenQueueMatchAndReviewContext_EmitsReadyTrueWithExcerpt()
    {
        using var workspace = new GuideReviewWorkspace();
        workspace.WriteQueueState(BuildQueueState("G248", "review", title: "guide review", linkedPr: "598"));
        workspace.WriteFile(".intent-cli/issues/G248/review-context.md",
            """
            # G248 Review Context

            Review that this slice keeps review-only behavior and emits deterministic guidance.

            Flag findings if the implementation:

            - launches AI providers from `intent-cli`;
            - mutates GitHub or parent state for a read-only command.
            """);
        workspace.WriteFile(".intent-cli/issues/G248/packet.yaml", "x");

        using var writer = new StringWriter();
        var exitCode = GuideReviewCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--pr", "598", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.True(root.GetProperty("ready").GetBoolean());
        Assert.Equal("G248", root.GetProperty("execution_unit").GetString());
        Assert.Equal("review", root.GetProperty("queue_item_state").GetString());
        Assert.Equal("guide review", root.GetProperty("queue_item_title").GetString());
        Assert.Contains("Review that this slice", root.GetProperty("review_context_head").GetString()!, StringComparison.Ordinal);
        Assert.True(root.GetProperty("review_checklist").GetArrayLength() >= 5);
        Assert.True(root.GetProperty("review_boundaries").GetArrayLength() >= 3);
        Assert.True(root.GetProperty("validation_suggestions").GetArrayLength() >= 2);
        Assert.Equal(0, root.GetProperty("gaps").GetArrayLength());
    }

    [Fact]
    public void Execute_GivenQueueMatchWithoutReviewContext_EmitsReadyTrueWithoutExcerpt()
    {
        using var workspace = new GuideReviewWorkspace();
        workspace.WriteQueueState(BuildQueueState("G248", "review", title: "guide review", linkedPr: "598"));
        workspace.WriteFile(".intent-cli/issues/G248/packet.yaml", "x");

        using var writer = new StringWriter();
        var exitCode = GuideReviewCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--pr", "598", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.True(root.GetProperty("ready").GetBoolean());
        Assert.False(root.TryGetProperty("review_context_head", out _));
    }

    [Fact]
    public void Execute_GivenNoMatchingLinkedPr_ReportsQueueGap()
    {
        using var workspace = new GuideReviewWorkspace();
        workspace.WriteQueueState(BuildQueueState("G248", "review", title: "guide review", linkedPr: "999"));

        using var writer = new StringWriter();
        var exitCode = GuideReviewCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--pr", "598", "--format", "json"],
            writer);

        Assert.Equal(1, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var gaps = document.RootElement.GetProperty("gaps").EnumerateArray().Select(e => e.GetString()).ToArray();
        Assert.Contains(gaps, gap => gap!.Contains("no queue item found with linked_pr", StringComparison.Ordinal));
    }

    [Fact]
    public void Execute_GivenSamePrNumberInDifferentRepo_SkipsOtherRepo()
    {
        using var workspace = new GuideReviewWorkspace();
        workspace.WriteQueueState("""
            {
              "schema_version": "1",
              "updated_at": "2026-04-28T23:00:00Z",
              "items": [
                {
                  "execution_unit": "G192",
                  "title": "wrong repo",
                  "state": "completed",
                  "dependencies": [],
                  "blocked_by": [],
                  "clarification_return_path": "intents/intent-cli/clarifications/open.md",
                  "packet_paths": {"implementation": "a", "review_context": "b", "yaml": "c"},
                  "linked_pr": {"repo": "J-Tech-Japan/intent-system", "number": 490, "url": "https://github.com/J-Tech-Japan/intent-system/pull/490"},
                  "worker_role": "coder",
                  "review_role": "reviewer",
                  "priority": "normal"
                },
                {
                  "execution_unit": "SKS-G185",
                  "title": "right repo",
                  "state": "review",
                  "dependencies": [],
                  "blocked_by": [],
                  "clarification_return_path": "intents/sekiban-as-a-service/clarifications/open.md",
                  "packet_paths": {"implementation": "a", "review_context": "b", "yaml": "c"},
                  "linked_pr": {"repo": "J-Tech-Japan/SekibanAsAService", "number": 490, "url": "https://github.com/J-Tech-Japan/SekibanAsAService/pull/490"},
                  "worker_role": "coder",
                  "review_role": "reviewer",
                  "priority": "normal"
                }
              ]
            }
            """);
        workspace.WriteFile(".intent-cli/issues/SKS-G185/packet.yaml", "x");

        using var writer = new StringWriter();
        var exitCode = GuideReviewCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/SekibanAsAService", "--pr", "490", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        Assert.Equal("SKS-G185", document.RootElement.GetProperty("execution_unit").GetString());
    }

    [Fact]
    public void Execute_GivenMissingPacketDirectory_ReportsPacketGap()
    {
        using var workspace = new GuideReviewWorkspace();
        workspace.WriteQueueState(BuildQueueState("G248", "review", title: "guide review", linkedPr: "598"));

        using var writer = new StringWriter();
        var exitCode = GuideReviewCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--pr", "598", "--format", "json"],
            writer);

        Assert.Equal(1, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var gaps = document.RootElement.GetProperty("gaps").EnumerateArray().Select(e => e.GetString()).ToArray();
        Assert.Contains(gaps, gap => gap!.Contains("packet directory not found", StringComparison.Ordinal));
    }

    [Fact]
    public void Execute_GivenMissingQueueState_ReportsQueueStateGap()
    {
        using var workspace = new GuideReviewWorkspace();
        // No queue state written.

        using var writer = new StringWriter();
        var exitCode = GuideReviewCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--pr", "598", "--format", "json"],
            writer);

        Assert.Equal(1, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var gaps = document.RootElement.GetProperty("gaps").EnumerateArray().Select(e => e.GetString()).ToArray();
        Assert.Contains(gaps, gap => gap!.Contains("queue-state file not found", StringComparison.Ordinal));
    }

    [Fact]
    public void Execute_MarkdownFormat_EmitsHumanReadableOutput()
    {
        using var workspace = new GuideReviewWorkspace();
        workspace.WriteQueueState(BuildQueueState("G248", "review", title: "guide review", linkedPr: "598"));
        workspace.WriteFile(".intent-cli/issues/G248/review-context.md", "# G248 Review Context\nReview head.\n");

        using var writer = new StringWriter();
        var exitCode = GuideReviewCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--pr", "598"],
            writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("# Guide review — J-Tech-Japan/intent-system#598", output, StringComparison.Ordinal);
        Assert.Contains("ready: yes", output, StringComparison.Ordinal);
        Assert.Contains("## Review checklist", output, StringComparison.Ordinal);
        Assert.Contains("## Review boundaries", output, StringComparison.Ordinal);
        Assert.Contains("## Validation suggestions", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_MissingPr_ReturnsUsageError()
    {
        using var workspace = new GuideReviewWorkspace();
        using var writer = new StringWriter();

        var exitCode = GuideReviewCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("--pr is required", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_MissingRepo_ReturnsUsageError()
    {
        using var workspace = new GuideReviewWorkspace();
        using var writer = new StringWriter();

        var exitCode = GuideReviewCommand.Execute(
            workspace.Context,
            ["--pr", "598"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("--repo is required", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_UnsupportedFormat_ReturnsUsageError()
    {
        using var workspace = new GuideReviewWorkspace();
        using var writer = new StringWriter();

        var exitCode = GuideReviewCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--pr", "598", "--format", "yaml"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("--format must be 'markdown' or 'json'", writer.ToString(), StringComparison.Ordinal);
    }

    // --- G316: intent-and-packet-aware review --------------------------------

    [Fact]
    public void Execute_G316_JsonIncludesPacketPathsAndIntentReferenceAndSufficiencyFields()
    {
        // Acceptance: guide review must surface structured packet_paths
        // (canonical packet files with exists flags), intent_reference_paths
        // (specs/intent-tree/rules under the resolved domain),
        // approval_summary_requirements, request_update_requirements, and
        // tests_pass_is_necessary_not_sufficient: true.
        using var workspace = new GuideReviewWorkspace();
        workspace.WriteQueueState(BuildQueueState("G316", "review", title: "intent-aware review", linkedPr: "598"));
        workspace.WriteFile(".intent-cli/issues/G316/packet.yaml", "execution_unit: G316");
        workspace.WriteFile(".intent-cli/issues/G316/implementation.md", "# Implementation");
        // Seed only specs/ to confirm exists is true for some entries and false for others.
        workspace.WriteFile("intents/intent-cli/specs/00-map.md", "map");

        using var writer = new StringWriter();
        var exitCode = GuideReviewCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--pr", "598", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;

        // tests-pass sufficiency signal
        Assert.True(root.GetProperty("tests_pass_is_necessary_not_sufficient").GetBoolean());

        // packet_paths covers the four canonical files in the documented order
        var packetPaths = root.GetProperty("packet_paths").EnumerateArray().ToArray();
        Assert.Equal(4, packetPaths.Length);
        Assert.Equal("packet.yaml", packetPaths[0].GetProperty("name").GetString());
        Assert.Equal("implementation.md", packetPaths[1].GetProperty("name").GetString());
        Assert.Equal("review-context.md", packetPaths[2].GetProperty("name").GetString());
        Assert.Equal("github-body.md", packetPaths[3].GetProperty("name").GetString());
        Assert.True(packetPaths[0].GetProperty("exists").GetBoolean()); // packet.yaml seeded
        Assert.True(packetPaths[1].GetProperty("exists").GetBoolean()); // implementation.md seeded
        Assert.False(packetPaths[2].GetProperty("exists").GetBoolean()); // review-context.md not seeded
        Assert.False(packetPaths[3].GetProperty("exists").GetBoolean()); // github-body.md not seeded

        // intent_reference_paths covers specs/intent-tree/rules under the
        // domain, with relative_path always populated and exists reflecting
        // what's on disk.
        var intentRefs = root.GetProperty("intent_reference_paths").EnumerateArray().ToArray();
        Assert.Equal(3, intentRefs.Length);
        var specsRef = intentRefs.Single(e => e.GetProperty("kind").GetString() == "specs");
        Assert.Equal("intents/intent-cli/specs", specsRef.GetProperty("relative_path").GetString());
        Assert.True(specsRef.GetProperty("exists").GetBoolean());
        var intentTreeRef = intentRefs.Single(e => e.GetProperty("kind").GetString() == "intent-tree");
        Assert.False(intentTreeRef.GetProperty("exists").GetBoolean());
        var rulesRef = intentRefs.Single(e => e.GetProperty("kind").GetString() == "rules");
        Assert.False(rulesRef.GetProperty("exists").GetBoolean());

        // approval_summary_requirements references packet contract, AC, OOS,
        // intent reference, tests-pass-paired-with-evidence, and Closes ref.
        var approvalReqs = root.GetProperty("approval_summary_requirements")
            .EnumerateArray().Select(e => e.GetString()!).ToArray();
        Assert.True(approvalReqs.Length >= 5);
        Assert.Contains(approvalReqs, r => r.Contains("packet.yaml", StringComparison.Ordinal));
        Assert.Contains(approvalReqs, r => r.Contains("acceptance criteria", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(approvalReqs, r => r.Contains("out-of-scope", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(approvalReqs, r => r.Contains("intent / spec / rule", StringComparison.Ordinal));
        Assert.Contains(approvalReqs, r => r.Contains("Closes #", StringComparison.Ordinal));
        Assert.Contains(approvalReqs, r => r.Contains("necessary, not sufficient", StringComparison.Ordinal));

        // request_update_requirements force the three-way classification.
        var requestReqs = root.GetProperty("request_update_requirements")
            .EnumerateArray().Select(e => e.GetString()!).ToArray();
        Assert.Contains(requestReqs, r => r.Contains("implementation-finding", StringComparison.Ordinal));
        Assert.Contains(requestReqs, r => r.Contains("host-metadata-blocked", StringComparison.Ordinal));
        Assert.Contains(requestReqs, r => r.Contains("intent-ambiguity", StringComparison.Ordinal));
        // Tests-only failure mode is itself an implementation-finding.
        Assert.Contains(requestReqs, r => r.Contains("tests pass but evidence missing", StringComparison.Ordinal)
            || r.Contains("packet/intent conformance", StringComparison.Ordinal));
    }

    [Fact]
    public void Execute_G316_ChecklistEnforcesPacketAndIntentEvidenceBeyondTests()
    {
        using var workspace = new GuideReviewWorkspace();
        workspace.WriteQueueState(BuildQueueState("G316", "review", title: "intent-aware review", linkedPr: "598"));
        workspace.WriteFile(".intent-cli/issues/G316/packet.yaml", "x");

        using var writer = new StringWriter();
        var exitCode = GuideReviewCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--pr", "598", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var checklist = document.RootElement.GetProperty("review_checklist")
            .EnumerateArray().Select(e => e.GetString()!).ToArray();

        // Each canonical packet file is named in the checklist.
        Assert.Contains(checklist, item => item.Contains("packet.yaml", StringComparison.Ordinal));
        Assert.Contains(checklist, item => item.Contains("implementation.md", StringComparison.Ordinal));
        Assert.Contains(checklist, item => item.Contains("review-context.md", StringComparison.Ordinal));
        // Acceptance Criteria + Out-of-Scope boundaries
        Assert.Contains(checklist, item => item.Contains("Acceptance Criteria", StringComparison.Ordinal));
        Assert.Contains(checklist, item => item.Contains("Out-of-scope boundaries", StringComparison.Ordinal));
        // Related intent/spec/rule reference
        Assert.Contains(checklist, item => item.Contains("intent / spec / rule", StringComparison.Ordinal)
            || item.Contains("design intent", StringComparison.Ordinal));
        // PR closing reference (G311) — explicitly required
        Assert.Contains(checklist, item => item.Contains("Closes/Fixes/Resolves", StringComparison.Ordinal)
            || item.Contains("G311", StringComparison.Ordinal));
        // Tests-pass-not-sufficient explicit
        Assert.Contains(checklist, item =>
            item.Contains("NECESSARY but NOT SUFFICIENT", StringComparison.Ordinal)
            || item.Contains("necessary but not sufficient", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Execute_G316_MarkdownIncludesNewSections()
    {
        using var workspace = new GuideReviewWorkspace();
        workspace.WriteQueueState(BuildQueueState("G316", "review", title: "intent-aware review", linkedPr: "598"));
        workspace.WriteFile(".intent-cli/issues/G316/packet.yaml", "x");

        using var writer = new StringWriter();
        var exitCode = GuideReviewCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--pr", "598"],
            writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("## Intent reference paths", output, StringComparison.Ordinal);
        Assert.Contains("## Sufficiency of evidence", output, StringComparison.Ordinal);
        Assert.Contains("tests_pass_is_necessary_not_sufficient: yes", output, StringComparison.Ordinal);
        Assert.Contains("## Approval summary requirements", output, StringComparison.Ordinal);
        Assert.Contains("## Request-update requirements", output, StringComparison.Ordinal);
        Assert.Contains("canonical paths:", output, StringComparison.Ordinal);
        // Domain placeholder is the test workspace's intent-cli domain.
        Assert.Contains("intents/intent-cli/specs", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_HelpFlag_PrintsUsage()
    {
        using var workspace = new GuideReviewWorkspace();
        using var writer = new StringWriter();

        var exitCode = GuideReviewCommand.Execute(
            workspace.Context,
            ["--help"],
            writer);

        Assert.Equal(0, exitCode);
        Assert.Contains("guide review", writer.ToString(), StringComparison.Ordinal);
    }

    private static string BuildQueueState(string executionUnit, string state, string title, string? linkedPr)
    {
        var linked = linkedPr is null ? "null" : $"\"{linkedPr}\"";
        return $$"""
            {
              "schema_version": "1",
              "updated_at": "2026-04-28T23:00:00Z",
              "items": [
                {
                  "execution_unit": "{{executionUnit}}",
                  "title": "{{title}}",
                  "state": "{{state}}",
                  "dependencies": [],
                  "blocked_by": [],
                  "clarification_return_path": "intents/intent-cli/clarifications/open.md",
                  "packet_paths": {"implementation": "a", "review_context": "b", "yaml": "c"},
                  "linked_pr": {{linked}},
                  "worker_role": "coder",
                  "review_role": "reviewer",
                  "priority": "normal"
                }
              ]
            }
            """;
    }

    private sealed class GuideReviewWorkspace : IDisposable
    {
        private readonly string rootPath = Directory
            .CreateTempSubdirectory("guide-review-tests-")
            .FullName;

        public GuideReviewWorkspace()
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
                        ArtifactRoot = ".intent-cli",
                        WorktreeRoot = ".intent-cli/worktrees"
                    }
                }
            };
        }

        public CliContext Context { get; }

        public void WriteQueueState(string content)
        {
            File.WriteAllText(Context.GetQueueStatePath(), content);
        }

        public void WriteFile(string relativePath, string content)
        {
            var full = Path.Combine(rootPath, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, content);
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
