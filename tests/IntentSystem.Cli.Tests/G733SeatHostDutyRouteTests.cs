using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G733: the child implementation contract must carry the seat/host split,
/// the exact message-channel request, claim CAS evidence, and the negative
/// host-boundary proof without requiring host metadata.
/// </summary>
public sealed class G733SeatHostDutyRouteTests
{
    [Fact]
    public void ChildLoopPrompt_EmitsBoundaryWithoutHostRoundTrip()
    {
        using var writer = new StringWriter();
        var exitCode = GuidePromptMatrixCommand.Execute(
            CreateContext(),
            [
                "--mode", "child-loop",
                "--domain", "intent-cli",
                "--target-repo", "J-Tech-Japan/intent-system",
                "--agent", "claude",
                "--frequency", "5m",
                "--format", "json"
            ],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var prompt = document.RootElement.GetProperty("prompt").GetString()!;
        Assert.Contains("Seat/host duty boundary (G733)", prompt, StringComparison.Ordinal);
        Assert.Contains("intent-cli notify report", prompt, StringComparison.Ordinal);
        Assert.Contains("claim acquire --scope execution-unit:<EU>", prompt, StringComparison.Ordinal);
        Assert.Contains("push_succeeded=true", prompt, StringComparison.Ordinal);
        Assert.Contains("passed=true", prompt, StringComparison.Ordinal);
        Assert.Contains("host-repository GitHub API", prompt, StringComparison.Ordinal);
        Assert.Contains("remote-herdr", prompt, StringComparison.Ordinal);
        Assert.Contains("probe-should-fail", prompt, StringComparison.Ordinal);
        Assert.Contains("--github-only", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void EnglishAndJapaneseDocsAndAdrMirrorTheBoundary_G733()
    {
        var root = RepoVersionPolicySource.RepoRoot();
        var en = File.ReadAllText(Path.Combine(root, "docs", "en", "05-implementation-loop.md"));
        var ja = File.ReadAllText(Path.Combine(root, "docs", "ja", "05-implementation-loop.md"));
        var adr = File.ReadAllText(Path.Combine(root, "docs", "adr", "0010-seat-host-duty-route.md"));

        foreach (var doc in new[] { en, ja })
        {
            Assert.Contains("G733", doc, StringComparison.Ordinal);
            Assert.Contains("intent-cli notify report", doc, StringComparison.Ordinal);
            Assert.Contains("intent-cli claim acquire", doc, StringComparison.Ordinal);
            Assert.Contains("intent-cli claim verify", doc, StringComparison.Ordinal);
            Assert.Contains("push_succeeded=true", doc, StringComparison.Ordinal);
            Assert.Contains("passed=true", doc, StringComparison.Ordinal);
            Assert.Contains("FETCH_HEAD", doc, StringComparison.Ordinal);
            Assert.Contains("remote-herdr", doc, StringComparison.Ordinal);
            Assert.Contains("probe-should-fail", doc, StringComparison.Ordinal);
            Assert.Contains("0010-seat-host-duty-route.md", doc, StringComparison.Ordinal);
            Assert.Contains("canonical", doc, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("intent-target", doc, StringComparison.Ordinal);
            Assert.Contains("linked_pr_synced", doc, StringComparison.Ordinal);
        }

        Assert.Contains("Status: Accepted", adr, StringComparison.Ordinal);
        Assert.Contains("Only successful remote push is acquisition", adr, StringComparison.Ordinal);
        Assert.Contains("host-repository GitHub API", adr, StringComparison.Ordinal);
        Assert.Contains("co-located", adr, StringComparison.Ordinal);
        Assert.Contains("remote-herdr", adr, StringComparison.Ordinal);
        Assert.Contains("intent-cli notify report", adr, StringComparison.Ordinal);
        Assert.Contains("command-owned transition", adr, StringComparison.Ordinal);
    }

    [Fact]
    public void WorkspaceWriteTranscriptContainsSeatOwnedEvidence_G733()
    {
        var root = RepoVersionPolicySource.RepoRoot();
        var transcript = File.ReadAllText(Path.Combine(root, "docs", "g733-workspace-write-transcript.md"));

        Assert.Contains("workspace-write", transcript, StringComparison.Ordinal);
        Assert.Contains("--github-only", transcript, StringComparison.Ordinal);
        Assert.Contains("issue-to-pr", transcript, StringComparison.Ordinal);
        Assert.Contains("git fetch origin main", transcript, StringComparison.Ordinal);
        Assert.Contains("claude/g733-host-state-duty-route", transcript, StringComparison.Ordinal);
        Assert.Contains("ad1f6cf780266576c69aeb4dc8f204dec1f059bb", transcript, StringComparison.Ordinal);
        Assert.Contains("git push", transcript, StringComparison.Ordinal);
        Assert.Contains("ready-for-review", transcript, StringComparison.Ordinal);
        Assert.Contains("intent-cli notify report", transcript, StringComparison.Ordinal);
        Assert.Contains("intent-cli claim acquire --scope execution-unit:<EU>", transcript, StringComparison.Ordinal);
        Assert.Contains("Operation not permitted", transcript, StringComparison.Ordinal);
        Assert.Contains("exit=1", transcript, StringComparison.Ordinal);
        Assert.Contains("danger-full-access review seat evidence is excluded", transcript, StringComparison.Ordinal);
    }

    private static CliContext CreateContext() => new()
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
