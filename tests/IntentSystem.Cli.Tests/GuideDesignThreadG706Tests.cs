using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

public sealed class GuideDesignThreadG706Tests
{
    [Fact]
    public void MetadataFreeJson_ExposesScopedPaneObservationFallbackAndG701Boundary()
    {
        var context = BareContext();
        Assert.False(File.Exists(Path.Combine(context.RepoRoot, ".intent-cli", "config.toml")));

        using var writer = new StringWriter();
        Assert.Equal(0, GuideDesignThreadCommand.Execute(context, ["--format", "json"], writer));

        var output = writer.ToString();
        Assert.DoesNotContain("No terminal parsing", output, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "No terminal parsing, provider launch, hidden fifth role, or design-owned stall recovery.",
            output,
            StringComparison.Ordinal);

        using var document = JsonDocument.Parse(output);
        var boundary = document.RootElement.GetProperty("observation_boundary");
        Assert.Contains("operational liveness diagnosis", boundary.GetProperty("pane_read_rule").GetString()!, StringComparison.Ordinal);
        Assert.Contains("canonical workflow evidence", boundary.GetProperty("canonical_evidence_rule").GetString()!, StringComparison.Ordinal);
        Assert.Contains("never transfers", boundary.GetProperty("recovery_ownership_rule").GetString()!, StringComparison.Ordinal);
        Assert.Contains("intent-cli notify status", boundary.GetProperty("fallback_route").GetString()!, StringComparison.Ordinal);
        Assert.Contains("status-request", boundary.GetProperty("fallback_route").GetString()!, StringComparison.Ordinal);

        var keystrokeBoundary = boundary.GetProperty("keystroke_boundary").GetString()!;
        Assert.Contains("G701", keystrokeBoundary, StringComparison.Ordinal);
        Assert.Contains("dialog-answering/v1", keystrokeBoundary, StringComparison.Ordinal);
        Assert.Contains("exact dialog/action match", keystrokeBoundary, StringComparison.Ordinal);
        Assert.Contains("human as decision actor", keystrokeBoundary, StringComparison.Ordinal);
        Assert.Contains("no per-action class generalization", keystrokeBoundary, StringComparison.Ordinal);
        Assert.Contains("unknown-origin", keystrokeBoundary, StringComparison.Ordinal);
    }

    [Fact]
    public void MetadataFreeMarkdown_StatesObservationIsNotEvidenceOrRecoveryOwnership()
    {
        using var writer = new StringWriter();
        Assert.Equal(0, GuideDesignThreadCommand.Execute(BareContext(), [], writer));
        var output = writer.ToString();

        Assert.Contains("Terminal observation and keystroke boundary (G706)", output, StringComparison.Ordinal);
        Assert.Contains("**pane read:** Terminal pane reading is permitted only for operational liveness diagnosis", output, StringComparison.Ordinal);
        Assert.Contains("**canonical evidence:** Terminal content is never parsed, promoted, or cited as canonical workflow evidence", output, StringComparison.Ordinal);
        Assert.Contains("**fallback observation route:** If orchestration cannot read panes", output, StringComparison.Ordinal);
        Assert.Contains("**keystroke/dialog boundary:** Keystrokes are never a generic design relay", output, StringComparison.Ordinal);
        Assert.DoesNotContain("No terminal parsing", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Help_ScopesPaneReadsToOperationalLiveness()
    {
        using var writer = new StringWriter();
        Assert.Equal(0, GuideDesignThreadCommand.Execute(BareContext(), ["--help"], writer));
        var output = writer.ToString();

        Assert.Contains("canonical terminal parsing", output, StringComparison.Ordinal);
        Assert.Contains("Pane reads are for operational liveness diagnosis only", output, StringComparison.Ordinal);
        Assert.DoesNotContain("supervision, terminal parsing, or mutation", output, StringComparison.Ordinal);
    }

    [Fact]
    public void EnglishAndJapaneseGuides_PreserveG706BoundaryAndRemoveUnqualifiedForm()
    {
        var repo = RepoVersionPolicySource.RepoRoot();
        foreach (var language in new[] { "en", "ja" })
        {
            var document = File.ReadAllText(Path.Combine(repo, "docs", language, "12-agent-message-orchestration.md"));
            Assert.Contains("G706", document, StringComparison.Ordinal);
            Assert.Contains("operational liveness", document, StringComparison.Ordinal);
            Assert.Contains("canonical workflow evidence", document, StringComparison.Ordinal);
            Assert.Contains("intent-cli notify status", document, StringComparison.Ordinal);
            Assert.Contains("status-request", document, StringComparison.Ordinal);
            Assert.Contains("recovery ownership", document, StringComparison.Ordinal);
            Assert.Contains("dialog-answering/v1", document, StringComparison.Ordinal);
            Assert.DoesNotContain(
                "No terminal parsing, provider launch, hidden fifth role, or design-owned stall recovery.",
                document,
                StringComparison.Ordinal);
        }
    }

    private static CliContext BareContext() => new()
    {
        RepoRoot = AppContext.BaseDirectory,
        Config = new CliConfig
        {
            Project = new ProjectConfig
            {
                Domain = "intent-cli",
                ArtifactRoot = ".intent-cli",
                WorktreeRoot = ".intent-cli/worktrees",
            },
        },
    };
}
