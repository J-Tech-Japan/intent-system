using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

public sealed class AutomationSameRepoMetadataPreflightCommandTests : IDisposable
{
    public AutomationSameRepoMetadataPreflightCommandTests()
    {
        AutomationSameRepoMetadataPreflightCommand.ProbeFactory = null;
    }

    public void Dispose()
    {
        AutomationSameRepoMetadataPreflightCommand.ProbeFactory = null;
    }

    [Fact]
    public void Classify_LocalEqualsOrigin_ReturnsClean()
    {
        var probe = new SameRepoMetadataPreflightProbe
        {
            LocalSha = "abcdef1",
            OriginSha = "abcdef1",
            OriginBranchExists = true,
            LocalIsAncestorOfOrigin = true,
        };

        var result = AutomationSameRepoMetadataPreflightCommand.Classify(probe, "main-metadata");

        Assert.Equal(AutomationSameRepoMetadataPreflightCommand.ClassificationClean, result.Classification);
        Assert.Equal("main-metadata", result.Branch);
        Assert.Empty(result.RecommendedActions);
    }

    [Fact]
    public void Classify_LocalIsAncestorOfOrigin_ReturnsBehindOrigin()
    {
        // G362 acceptance: local is strictly behind origin (a
        // fast-forward pull would resolve it). The classifier
        // surfaces `behind-origin` so the host loop pulls before
        // reading queue-state.
        var probe = new SameRepoMetadataPreflightProbe
        {
            LocalSha = "aaaaaaa",
            OriginSha = "bbbbbbb",
            OriginBranchExists = true,
            LocalIsAncestorOfOrigin = true,
        };

        var result = AutomationSameRepoMetadataPreflightCommand.Classify(probe, "main-metadata");

        Assert.Equal(AutomationSameRepoMetadataPreflightCommand.ClassificationBehindOrigin, result.Classification);
        Assert.Contains("git pull --ff-only", result.RecommendedActions[0], StringComparison.Ordinal);
    }

    [Fact]
    public void Classify_LocalAndOriginDiverged_ReturnsDiverged()
    {
        // G362 acceptance: local and origin SHAs differ and local is
        // NOT an ancestor of origin — true fork that requires manual
        // reconciliation. Structured stop blocks the wake.
        var probe = new SameRepoMetadataPreflightProbe
        {
            LocalSha = "aaaaaaa",
            OriginSha = "bbbbbbb",
            OriginBranchExists = true,
            LocalIsAncestorOfOrigin = false,
        };

        var result = AutomationSameRepoMetadataPreflightCommand.Classify(probe, "main-metadata");

        Assert.Equal(AutomationSameRepoMetadataPreflightCommand.ClassificationDiverged, result.Classification);
        Assert.Contains(result.RecommendedActions, a => a.Contains("rebase", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.RecommendedActions, a => a.Contains("Do NOT proceed", StringComparison.Ordinal));
    }

    [Fact]
    public void Classify_OriginBranchMissing_ReturnsMissingBranch()
    {
        var probe = new SameRepoMetadataPreflightProbe
        {
            LocalSha = "aaaaaaa",
            OriginSha = null,
            OriginBranchExists = false,
            LocalIsAncestorOfOrigin = false,
        };

        var result = AutomationSameRepoMetadataPreflightCommand.Classify(probe, "main-metadata");

        Assert.Equal(AutomationSameRepoMetadataPreflightCommand.ClassificationMissingBranch, result.Classification);
        Assert.Contains(result.RecommendedActions, a => a.Contains("git push origin main-metadata", StringComparison.Ordinal));
    }

    [Fact]
    public void Execute_NoMetadataBranchConfigured_ReturnsNotConfigured_AndExitZero()
    {
        // G362 acceptance: hosts without same-repo metadata branch
        // configuration must NOT be blocked by this preflight —
        // they keep the pre-G362 pull-first main behavior (G357).
        using var workspace = new TestWorkspace(metadataSourceBranch: string.Empty);
        using var writer = new StringWriter();

        var exitCode = AutomationSameRepoMetadataPreflightCommand.Execute(
            workspace.Context,
            new[] { "--format", "json" },
            writer);

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.Equal(
            AutomationSameRepoMetadataPreflightCommand.ClassificationNotConfigured,
            doc.RootElement.GetProperty("classification").GetString());
    }

    [Fact]
    public void Execute_BranchFlagOverridesConfig_AndProbeRuns()
    {
        // The --branch flag overrides the configured metadata
        // source branch so an operator can preflight any branch
        // ad-hoc without editing config.toml.
        using var workspace = new TestWorkspace(metadataSourceBranch: "main-metadata");
        AutomationSameRepoMetadataPreflightCommand.ProbeFactory = (_, branch) =>
        {
            Assert.Equal("main-ai", branch);
            return new SameRepoMetadataPreflightProbe
            {
                LocalSha = "aaa",
                OriginSha = "aaa",
                OriginBranchExists = true,
                LocalIsAncestorOfOrigin = true,
            };
        };

        using var writer = new StringWriter();
        var exitCode = AutomationSameRepoMetadataPreflightCommand.Execute(
            workspace.Context,
            new[] { "--branch", "main-ai", "--format", "json" },
            writer);

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.Equal("main-ai", doc.RootElement.GetProperty("branch").GetString());
        Assert.Equal(
            AutomationSameRepoMetadataPreflightCommand.ClassificationClean,
            doc.RootElement.GetProperty("classification").GetString());
    }

    [Fact]
    public void Execute_StaleMetadataBranch_Zero4RacerScenario_ReturnsBehindOriginAndExitOne()
    {
        // G362 acceptance: Zero4Racer-like setup with `main-metadata`
        // configured but local stale relative to origin. Wake must
        // refuse to read queue-state until operator pulls.
        using var workspace = new TestWorkspace(metadataSourceBranch: "main-metadata");
        AutomationSameRepoMetadataPreflightCommand.ProbeFactory = (_, _) => new SameRepoMetadataPreflightProbe
        {
            LocalSha = "stalelocal",
            OriginSha = "freshorigin",
            OriginBranchExists = true,
            LocalIsAncestorOfOrigin = true,
        };

        using var writer = new StringWriter();
        var exitCode = AutomationSameRepoMetadataPreflightCommand.Execute(
            workspace.Context,
            new[] { "--format", "json" },
            writer);

        Assert.Equal(1, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.Equal(
            AutomationSameRepoMetadataPreflightCommand.ClassificationBehindOrigin,
            doc.RootElement.GetProperty("classification").GetString());
        Assert.Equal("main-metadata", doc.RootElement.GetProperty("branch").GetString());
    }

    [Fact]
    public void Execute_FallsBackToLegacyMetadataBranch_WhenSourceFieldEmpty()
    {
        // G362 backwards compat: the legacy single-field
        // MetadataBranch (G350) is used when the explicit
        // MetadataSourceBranch (G362) is not set.
        using var workspace = new TestWorkspace(metadataSourceBranch: string.Empty, legacyMetadataBranch: "main-metadata");
        AutomationSameRepoMetadataPreflightCommand.ProbeFactory = (_, branch) =>
        {
            Assert.Equal("main-metadata", branch);
            return new SameRepoMetadataPreflightProbe
            {
                LocalSha = "x",
                OriginSha = "x",
                OriginBranchExists = true,
                LocalIsAncestorOfOrigin = true,
            };
        };

        using var writer = new StringWriter();
        var exitCode = AutomationSameRepoMetadataPreflightCommand.Execute(
            workspace.Context,
            new[] { "--format", "json" },
            writer);

        Assert.Equal(0, exitCode);
    }

    private sealed class TestWorkspace : IDisposable
    {
        public TestWorkspace(string metadataSourceBranch, string legacyMetadataBranch = "")
        {
            RepoRoot = Directory.CreateTempSubdirectory("g362-").FullName;
            Directory.CreateDirectory(Path.Combine(RepoRoot, ".intent-cli"));
            Context = new CliContext
            {
                RepoRoot = RepoRoot,
                Config = new CliConfig
                {
                    Project = new ProjectConfig
                    {
                        Domain = "intent-cli",
                        ArtifactRoot = ".intent-cli",
                        WorktreeRoot = ".intent-cli/worktrees",
                        SameRepoTopology = !string.IsNullOrWhiteSpace(metadataSourceBranch) || !string.IsNullOrWhiteSpace(legacyMetadataBranch),
                        MetadataSourceBranch = metadataSourceBranch,
                        MetadataBranch = legacyMetadataBranch,
                    },
                },
            };
        }

        public string RepoRoot { get; }
        public CliContext Context { get; }

        public void Dispose()
        {
            if (Directory.Exists(RepoRoot)) Directory.Delete(RepoRoot, recursive: true);
        }
    }
}
