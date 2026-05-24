using System.Linq;
using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G393: tests for the guide-first entrypoint `intent-cli guide start`.
/// Covers the per-phase guide-command map (design/packet, implementation
/// loop, review-next-slice loop), the metadata-free child / ask-first host
/// role split, and the AGENTS.md / CLAUDE.md guide-first snippets.
/// </summary>
public sealed class GuideStartCommandTests
{
    [Fact]
    public void Execute_DefaultMarkdown_EmitsGuideFirstSectionsAndSnippets()
    {
        using var writer = new StringWriter();
        var exitCode = GuideStartCommand.Execute(CreateContext(), [], writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("# Guide start — ask intent-cli first", output, StringComparison.Ordinal);
        Assert.Contains("## Guide-first rule", output, StringComparison.Ordinal);
        Assert.Contains("## Which guide command for which phase", output, StringComparison.Ordinal);
        Assert.Contains("## Agent roles", output, StringComparison.Ordinal);
        Assert.Contains("Paste into `AGENTS.md`:", output, StringComparison.Ordinal);
        Assert.Contains("Paste into `CLAUDE.md`:", output, StringComparison.Ordinal);
        Assert.Contains("intent-cli guide start", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_Json_CoversDesignImplementationAndReviewPhases()
    {
        using var writer = new StringWriter();
        var exitCode = GuideStartCommand.Execute(CreateContext(), ["--format", "json"], writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;

        var phases = root.GetProperty("workflow_phases").EnumerateArray().ToArray();
        var phaseNames = phases.Select(p => p.GetProperty("phase").GetString()!).ToArray();
        // AC#5: at least design/packet, implementation loop, review-next-slice loop.
        Assert.Contains("packet-draft", phaseNames);
        Assert.Contains("implementation-loop", phaseNames);
        Assert.Contains("review-next-slice-loop", phaseNames);

        // Each phase points at a concrete intent-cli guide command.
        Assert.All(phases, p =>
            Assert.StartsWith("intent-cli ", p.GetProperty("guide_command").GetString()!, StringComparison.Ordinal));

        // The implementation loop phase points at the child-implement oneshot.
        var impl = phases.Single(p => p.GetProperty("phase").GetString() == "implementation-loop");
        Assert.Contains("child-implement-or-update", impl.GetProperty("guide_command").GetString()!, StringComparison.Ordinal);
        Assert.Equal("child-implementation", impl.GetProperty("side").GetString());

        // The review/next-slice phase points at the host-review oneshot.
        var review = phases.Single(p => p.GetProperty("phase").GetString() == "review-next-slice-loop");
        Assert.Contains("host-review-next-slice", review.GetProperty("guide_command").GetString()!, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_Json_StatesMetadataFreeChildAndAskFirstHostRoles()
    {
        using var writer = new StringWriter();
        var exitCode = GuideStartCommand.Execute(CreateContext(), ["--format", "json"], writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var roles = document.RootElement.GetProperty("agent_roles").EnumerateArray().ToArray();

        var child = roles.Single(r => r.GetProperty("role").GetString() == "child-implementation");
        var childRule = child.GetProperty("rule").GetString()!;
        // AC#3: implementation-side agents are GitHub-contract-only & metadata-free.
        Assert.Contains("metadata-free", childRule, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("MUST NOT", childRule, StringComparison.Ordinal);

        var host = roles.Single(r => r.GetProperty("role").GetString() == "host/design");
        var hostRule = host.GetProperty("rule").GetString()!;
        // AC#4: host/design agents ask intent-cli before hand-editing metadata.
        Assert.Contains("ask intent-cli", hostRule, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("before hand-editing", hostRule, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_Json_SnippetsAreShortGuideFirstRulesNotFullSpecs()
    {
        using var writer = new StringWriter();
        var exitCode = GuideStartCommand.Execute(CreateContext(), ["--format", "json"], writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var snippets = document.RootElement.GetProperty("repository_instruction_snippets");

        var agentsMd = snippets.GetProperty("agents_md").GetString()!;
        var claudeMd = snippets.GetProperty("claude_md").GetString()!;

        // AC#2: short guide-first rule, not a long copied spec.
        Assert.Contains("intent-cli guide start", agentsMd, StringComparison.Ordinal);
        Assert.Contains("intent-cli guide start", claudeMd, StringComparison.Ordinal);
        Assert.True(agentsMd.Length < 700, $"AGENTS.md snippet should stay short (was {agentsMd.Length}).");
        Assert.True(claudeMd.Length < 700, $"CLAUDE.md snippet should stay short (was {claudeMd.Length}).");

        // Multi-agent note names several agent families using the same entrypoint.
        var multi = document.RootElement.GetProperty("multi_agent_note").GetString()!;
        Assert.Contains("Codex", multi, StringComparison.Ordinal);
        Assert.Contains("Claude", multi, StringComparison.Ordinal);
        Assert.Contains("Copilot", multi, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_UnknownArgument_ReturnsUsageError()
    {
        using var writer = new StringWriter();
        var exitCode = GuideStartCommand.Execute(CreateContext(), ["--bogus"], writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("Usage: intent-cli guide start", writer.ToString(), StringComparison.Ordinal);
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
