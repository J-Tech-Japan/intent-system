using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G337: tests for <c>intent-cli guide workflow task packet-draft</c>.
/// The surface must explain the four packet files, the standalone
/// issue contract sections, the canonical `packet draft` commands,
/// and the stop conditions that surface missing contract sections
/// before any GitHub mutation.
/// </summary>
public sealed class GuideWorkflowTaskPacketDraftCommandTests
{
    [Fact]
    public void Execute_ListsFourPacketFilesAndContractSectionsAndCommands_ExitZero()
    {
        using var writer = new StringWriter();

        var exitCode = GuideWorkflowTaskPacketDraftCommand.Execute(
            CreateContext(),
            Array.Empty<string>(),
            writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        // Four packet files (acceptance criterion 1).
        foreach (var file in new[] { "packet.yaml", "implementation.md", "review-context.md", "github-body.md" })
        {
            Assert.Contains(file, output, StringComparison.Ordinal);
        }
        // Required standalone issue contract sections.
        foreach (var section in new[] { "goal", "why-this-slice-exists-now", "current-observed-state", "in-scope", "out-of-scope", "acceptance-criteria", "verification" })
        {
            Assert.Contains(section, output, StringComparison.Ordinal);
        }
        // Canonical commands.
        Assert.Contains("intent-cli packet draft --execution-unit", output, StringComparison.Ordinal);
        // Stop conditions surface BEFORE GitHub mutation (acceptance criterion 4).
        Assert.Contains("BEFORE", output, StringComparison.Ordinal);
        Assert.Contains("issue validate-body", output, StringComparison.Ordinal);
        // intent-target warning (acceptance criterion 3).
        Assert.Contains("intent-target", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_JsonFormat_HasStableShape()
    {
        using var writer = new StringWriter();

        var exitCode = GuideWorkflowTaskPacketDraftCommand.Execute(
            CreateContext(),
            new[] { "--format", "json" },
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.True(root.TryGetProperty("usage", out _));
        Assert.True(root.TryGetProperty("packet_files", out var files));
        Assert.Equal(4, files.GetArrayLength());
        Assert.True(root.TryGetProperty("issue_contract_sections", out var sections));
        Assert.True(sections.GetArrayLength() >= 7);
        Assert.True(root.TryGetProperty("commands", out _));
        Assert.True(root.TryGetProperty("stop_conditions", out _));
        Assert.True(root.TryGetProperty("invariants", out _));

        var fileNames = files.EnumerateArray().Select(f => f.GetProperty("name").GetString()).ToArray();
        Assert.Equal(new[] { "packet.yaml", "implementation.md", "review-context.md", "github-body.md" }, fileNames);
    }

    [Fact]
    public void PacketDraftCommands_FlagsExistInActualCliCommandSource()
    {
        // Regression test (mirroring the G336 / PR #776 fix): each
        // --flag named in the guide's `packet draft` example MUST
        // exist as a `case "--flag":` (or any literal string mention)
        // in the real command source file.
        var commands = GuideWorkflowTaskPacketDraftCommand.Commands;
        var flagPattern = new System.Text.RegularExpressions.Regex(
            @"--[a-z][a-z-]*",
            System.Text.RegularExpressions.RegexOptions.Compiled);

        var repoRoot = FindRepoRoot();
        var packetDraftSource = File.ReadAllText(Path.Combine(repoRoot, "src/IntentSystem.Cli/Commands/PacketDraftCommand.cs"));

        foreach (var commandLine in commands)
        {
            foreach (System.Text.RegularExpressions.Match m in flagPattern.Matches(commandLine))
            {
                var flag = m.Value;
                Assert.True(
                    packetDraftSource.Contains($"\"{flag}\"", StringComparison.Ordinal),
                    $"Guide example flag '{flag}' is missing from PacketDraftCommand.cs. Guide example: `{commandLine}`");
            }
        }
    }

    [Fact]
    public void Execute_EmitsPacketTimeIntentMaintenancePrompts()
    {
        // G461 AC: new packet draft guidance includes intent placement, ADR
        // candidate, diagram candidate, docs update, and closeout learning prompts.
        using var writer = new StringWriter();
        var exitCode = GuideWorkflowTaskPacketDraftCommand.Execute(
            CreateContext(), new[] { "--format", "json" }, writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.True(root.TryGetProperty("intent_maintenance_prompts", out var prompts));
        var ids = prompts.EnumerateArray().Select(p => p.GetProperty("id").GetString()).ToArray();
        Assert.Equal(
            new[] { "intent-placement", "adr-candidate", "diagram-candidate", "docs-update", "closeout-learning" },
            ids);
        // Each prompt carries actionable text.
        foreach (var p in prompts.EnumerateArray())
        {
            Assert.False(string.IsNullOrWhiteSpace(p.GetProperty("prompt").GetString()));
        }
    }

    [Fact]
    public void Execute_Markdown_DescribesPacketTimeMaintenanceAsNormalPathAndImproveAsSafetyNet()
    {
        // G461 AC: docs/guide text explains packet-time intent maintenance is the
        // normal path, while improve catches missed drift later.
        using var writer = new StringWriter();
        var exitCode = GuideWorkflowTaskPacketDraftCommand.Execute(
            CreateContext(), Array.Empty<string>(), writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("Packet-time intent maintenance", output, StringComparison.Ordinal);
        Assert.Contains("safety net", output, StringComparison.OrdinalIgnoreCase);
        // The five prompt ids surface in the markdown too.
        foreach (var id in new[] { "intent-placement", "adr-candidate", "diagram-candidate", "docs-update", "closeout-learning" })
        {
            Assert.Contains(id, output, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Execute_Guide_ListsCompletePublishContractAndPublishDryRunChecklist()
    {
        // G482: the guide enumerates the full publish-ready contract shape
        // (including Standalone Child Issue Contract and Base Branch Policy) and
        // tells agents to dry-run publish validation before declaring a packet
        // ready for GitHub issue creation.
        using var writer = new StringWriter();
        var exitCode = GuideWorkflowTaskPacketDraftCommand.Execute(
            CreateContext(), Array.Empty<string>(), writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("standalone-child-issue-contract", output, StringComparison.Ordinal);
        Assert.Contains("base-branch-policy", output, StringComparison.Ordinal);

        var stopConditions = string.Join(" ", GuideWorkflowTaskPacketDraftCommand.StopConditions);
        Assert.Contains(
            "Dry-run the publish validation BEFORE declaring the packet issue-ready (G482)",
            stopConditions,
            StringComparison.Ordinal);
        Assert.Contains("intent next-slice --dry-run", stopConditions, StringComparison.Ordinal);
    }

    [Fact]
    public void Invariants_IncludeIntentTargetFinalBoundaryWarning()
    {
        // Acceptance criterion: "The guide warns that intent-target
        // is final publish boundary, not issue creation default."
        var allCopy = string.Join(" ", GuideWorkflowTaskPacketDraftCommand.Invariants)
            + " " + string.Join(" ", GuideWorkflowTaskPacketDraftCommand.StopConditions);
        Assert.Contains("intent-target", allCopy, StringComparison.Ordinal);
        Assert.Contains("FINAL publish boundary", allCopy, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_UnknownArgument_ExitsOne()
    {
        using var writer = new StringWriter();

        var exitCode = GuideWorkflowTaskPacketDraftCommand.Execute(
            CreateContext(),
            new[] { "--bogus" },
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("Unknown argument", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void GuideWorkflowTaskCommand_DispatchesPacketDraft()
    {
        using var writer = new StringWriter();

        var exitCode = GuideWorkflowTaskCommand.Execute(
            CreateContext(),
            new[] { "packet-draft", "--format", "json" },
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        Assert.Contains("packet-draft", document.RootElement.GetProperty("usage").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public void GuideHelpCommand_PacketPhase_PointsToTaskPacketDraft()
    {
        var packetPointer = GuideHelpCommand.WorkflowGuides
            .First(p => string.Equals(p.Phase, "packet", StringComparison.Ordinal));
        Assert.Contains("guide workflow task packet-draft", packetPointer.Command, StringComparison.Ordinal);
        var seeAlso = string.Join(" ", packetPointer.SeeAlso ?? Array.Empty<string>());
        Assert.Contains("packet draft", seeAlso, StringComparison.Ordinal);
        Assert.Contains("issue validate-body", seeAlso, StringComparison.Ordinal);
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

    // ----- G349: copilot-local host agent can ask packet-draft guide -----

    [Fact]
    public void Execute_G349_CopilotLocalHostCwd_CanAskPacketDraftGuide_ExitZero()
    {
        // G349 Verification: a local Copilot host agent running in a host cwd
        // can call `guide workflow task packet-draft` and receive full packet
        // directory layout and contract completeness guidance (exit 0).
        // This surface is accessible to any host-cwd agent including copilot-local.
        using var writer = new StringWriter();

        var exitCode = GuideWorkflowTaskPacketDraftCommand.Execute(
            CreateContext(),
            Array.Empty<string>(),
            writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        // All four packet files surfaced.
        Assert.Contains("packet.yaml", output, StringComparison.Ordinal);
        Assert.Contains("implementation.md", output, StringComparison.Ordinal);
        Assert.Contains("review-context.md", output, StringComparison.Ordinal);
        Assert.Contains("github-body.md", output, StringComparison.Ordinal);
        // Canonical packet-draft command named.
        Assert.Contains("intent-cli packet draft", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_G349_CopilotLocalHostCwd_PacketDraftJsonFormat_ExitZero()
    {
        // G349 Verification: local Copilot host agent can request JSON format from
        // the packet-draft guide and gets a parsable payload with packet files.
        using var writer = new StringWriter();

        var exitCode = GuideWorkflowTaskPacketDraftCommand.Execute(
            CreateContext(),
            new[] { "--format", "json" },
            writer);

        Assert.Equal(0, exitCode);
        var json = writer.ToString();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.True(root.TryGetProperty("packet_files", out _), "packet_files key must be present.");
    }
}
