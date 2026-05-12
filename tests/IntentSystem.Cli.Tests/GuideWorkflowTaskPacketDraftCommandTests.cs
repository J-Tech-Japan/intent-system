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
}
