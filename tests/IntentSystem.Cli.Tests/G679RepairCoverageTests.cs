using System.Text.Json;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

public sealed class G679RepairCoverageTests
{
    [Fact]
    public void GuideNext_RendersBothClaimBeforeStartScopesInMarkdownAndJson_G679()
    {
        using var workspace = new GuideWorkspace();
        var args = new[]
        {
            "--domain", "intent-cli", "--team", "intent-cli-dev",
            "--target-repo", "J-Tech-Japan/intent-system", "--role", "design",
        };

        using var markdownWriter = new StringWriter();
        Assert.Equal(0, GuideNextCommand.Execute(workspace.Context, args, markdownWriter));
        var markdown = markdownWriter.ToString();
        Assert.Contains("Claim before starting named work", markdown, StringComparison.Ordinal);
        Assert.Contains("execution-unit:<EU>", markdown, StringComparison.Ordinal);
        Assert.Contains("release-prep:J-Tech-Japan/intent-system:<version>", markdown, StringComparison.Ordinal);
        Assert.Contains("status=acquired", markdown, StringComparison.Ordinal);
        Assert.Contains("push_succeeded=true", markdown, StringComparison.Ordinal);
        Assert.Contains("holder_team", markdown, StringComparison.Ordinal);

        using var jsonWriter = new StringWriter();
        Assert.Equal(0, GuideNextCommand.Execute(
            workspace.Context, [.. args, "--format", "json"], jsonWriter));
        using var document = JsonDocument.Parse(jsonWriter.ToString());
        var route = string.Join('\n', document.RootElement.GetProperty("claim_before_start")
            .EnumerateArray().Select(item => item.GetString()));
        Assert.Contains("execution-unit:<EU>", route, StringComparison.Ordinal);
        Assert.Contains("release-prep:J-Tech-Japan/intent-system:<version>", route, StringComparison.Ordinal);
        Assert.Contains("status=acquired", route, StringComparison.Ordinal);
        Assert.Contains("Never force-push", route, StringComparison.Ordinal);
    }

    [Fact]
    public void GuideOrchestratorThread_RendersVerificationRejectAndStaleRoutes_G679()
    {
        using var workspace = new GuideWorkspace();
        var args = new[]
        {
            "--domain", "intent-cli", "--team", "intent-cli-dev",
            "--target-repo", "J-Tech-Japan/intent-system", "--agent", "claude",
        };

        using var markdownWriter = new StringWriter();
        Assert.Equal(0, GuideOrchestratorThreadCommand.Execute(workspace.Context, args, markdownWriter));
        var markdown = markdownWriter.ToString();
        Assert.Contains("Git-backed claim verification and routing", markdown, StringComparison.Ordinal);
        Assert.Contains("Fast-forward from origin", markdown, StringComparison.Ordinal);
        Assert.Contains("`held`", markdown, StringComparison.Ordinal);
        Assert.Contains("Unrelated remote advance", markdown, StringComparison.Ordinal);
        Assert.Contains("`retry-exhausted`", markdown, StringComparison.Ordinal);
        Assert.Contains("`claim-stale` route", markdown, StringComparison.Ordinal);
        Assert.Contains("operator judgment", markdown, StringComparison.Ordinal);
        Assert.Contains("G680 owns command-level consumer enforcement", markdown, StringComparison.Ordinal);

        using var jsonWriter = new StringWriter();
        Assert.Equal(0, GuideOrchestratorThreadCommand.Execute(
            workspace.Context, [.. args, "--format", "json"], jsonWriter));
        using var document = JsonDocument.Parse(jsonWriter.ToString());
        var route = document.RootElement.GetProperty("claim_routing");
        Assert.Contains("G680", route.GetProperty("summary").GetString(), StringComparison.Ordinal);
        Assert.Contains("scope, actor, and team", string.Join('\n', route.GetProperty("verification")
            .EnumerateArray().Select(item => item.GetString())), StringComparison.Ordinal);
        Assert.Contains("held", string.Join('\n', route.GetProperty("reject_disambiguation")
            .EnumerateArray().Select(item => item.GetString())), StringComparison.Ordinal);
        Assert.Contains("last_evidence", route.GetProperty("claim_stale_route").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Adr0003_RecordsPushCasDecisionAndIsLinkedFromEnglishAndJapaneseDocs_G679()
    {
        var root = RepoVersionPolicySource.RepoRoot();
        var adrPath = Path.Combine(root, "docs", "adr", "0003-git-push-cas-work-ownership.md");
        var adr = File.ReadAllText(adrPath);
        Assert.Contains("Status: Accepted", adr, StringComparison.Ordinal);
        Assert.Contains("Only successful remote push is acquisition", adr, StringComparison.Ordinal);
        Assert.Contains("both actor and", adr, StringComparison.Ordinal);
        Assert.Contains("team must match", adr, StringComparison.Ordinal);
        Assert.Contains(".intent-cli/claims/** -merge", adr, StringComparison.Ordinal);
        Assert.Contains("time never expires", adr, StringComparison.Ordinal);
        Assert.Contains("G680 separately owns", adr, StringComparison.Ordinal);

        foreach (var language in new[] { "en", "ja" })
        {
            var guide = File.ReadAllText(Path.Combine(root, "docs", language, "05-implementation-loop.md"));
            var ledger = File.ReadAllText(Path.Combine(root, "docs", language, "1.0-compatibility-ledger.md"));
            Assert.Contains("../adr/0003-git-push-cas-work-ownership.md", guide, StringComparison.Ordinal);
            Assert.Contains("0003-git-push-cas-work-ownership.md", ledger, StringComparison.Ordinal);
        }
    }

    private sealed class GuideWorkspace : IDisposable
    {
        public GuideWorkspace()
        {
            Root = Directory.CreateTempSubdirectory("g679-guide-reachability-").FullName;
            Context = new CliContext
            {
                RepoRoot = Root,
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

        public string Root { get; }
        public CliContext Context { get; }

        public void Dispose()
        {
            if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
        }
    }
}
