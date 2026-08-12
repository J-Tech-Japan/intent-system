using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

public sealed class ProjectionCommandTests
{
    [Fact]
    public void Execute_GivenProjectionGenerateAndSourceBundle_WritesArtifactsToIssueDirectory()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G2", "packet.yaml"),
            CreatePacketYaml("G2"));
        using var writer = new StringWriter();

        var exitCode = CommandRouter.Execute(["projection", "generate", "G2"], CreateContext(repoRoot), writer);

        Assert.Equal(0, exitCode);
        Assert.True(File.Exists(Path.Combine(repoRoot, ".intent-cli", "issues", "G2", "implementation.md")));
        Assert.True(File.Exists(Path.Combine(repoRoot, ".intent-cli", "issues", "G2", "review-context.md")));
        Assert.True(File.Exists(Path.Combine(repoRoot, ".intent-cli", "issues", "G2", "packet.yaml")));
        Assert.Contains(
            "# [G2] Projection Generate Command",
            File.ReadAllText(Path.Combine(repoRoot, ".intent-cli", "issues", "G2", "implementation.md")),
            StringComparison.Ordinal);
        Assert.Contains(
            "# Execution Unit",
            File.ReadAllText(Path.Combine(repoRoot, ".intent-cli", "issues", "G2", "review-context.md")),
            StringComparison.Ordinal);
        Assert.Contains(
            "source_execution_unit: G2",
            File.ReadAllText(Path.Combine(repoRoot, ".intent-cli", "issues", "G2", "packet.yaml")),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenProjectionGenerateWithoutExecutionUnit_ReturnsExitCodeOne()
    {
        using var writer = new StringWriter();

        var exitCode = CommandRouter.Execute(["projection", "generate"], CreateContext("/tmp/intent-system"), writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("execution unit", writer.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Execute_GivenProjectionGenerateAndMissingPacketYaml_ReturnsExitCodeOne()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        using var writer = new StringWriter();

        var exitCode = CommandRouter.Execute(["projection", "generate", "G2"], CreateContext(repoRoot), writer);

        Assert.Equal(1, exitCode);
        Assert.Contains(".intent-cli", writer.ToString(), StringComparison.Ordinal);
        Assert.Contains("packet.yaml", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenProjectionGenerateAndMismatchedPacketExecutionUnit_ReturnsExitCodeOne()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G2", "packet.yaml"),
            CreatePacketYaml("G3"));
        using var writer = new StringWriter();

        var exitCode = CommandRouter.Execute(["projection", "generate", "G2"], CreateContext(repoRoot), writer);

        Assert.Equal(1, exitCode);
        Assert.False(File.Exists(Path.Combine(repoRoot, ".intent-cli", "issues", "G2", "implementation.md")));
        Assert.Contains("G3", writer.ToString(), StringComparison.Ordinal);
        Assert.Contains("G2", writer.ToString(), StringComparison.Ordinal);
        Assert.Contains("must match", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenProjectionGenerateAndExistingArtifacts_ReturnsExitCodeOneWithoutOverwriting()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        var implementationPath = Path.Combine(repoRoot, ".intent-cli", "issues", "G2", "implementation.md");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G2", "packet.yaml"),
            CreatePacketYaml("G2"));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G2", "implementation.md"),
            "existing implementation");
        using var writer = new StringWriter();

        var exitCode = CommandRouter.Execute(["projection", "generate", "G2"], CreateContext(repoRoot), writer);

        Assert.Equal(1, exitCode);
        Assert.Equal("existing implementation", File.ReadAllText(implementationPath));
        Assert.Contains("already exists", writer.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Execute_GivenProjectionRegenerateAndExistingArtifacts_OverwritesArtifacts()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G2", "packet.yaml"),
            CreatePacketYaml("G2"));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G2", "implementation.md"),
            "stale implementation");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G2", "review-context.md"),
            "stale review context");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G2", "packet.yaml"),
            CreatePacketYaml("G2"));
        using var writer = new StringWriter();

        var exitCode = CommandRouter.Execute(["projection", "regenerate", "G2"], CreateContext(repoRoot), writer);

        Assert.Equal(0, exitCode);
        Assert.Contains(
            "## Goal",
            File.ReadAllText(Path.Combine(repoRoot, ".intent-cli", "issues", "G2", "implementation.md")),
            StringComparison.Ordinal);
        Assert.Contains(
            "# Deterministic Review Checks",
            File.ReadAllText(Path.Combine(repoRoot, ".intent-cli", "issues", "G2", "review-context.md")),
            StringComparison.Ordinal);
        Assert.Contains(
            "review_context_packet:",
            File.ReadAllText(Path.Combine(repoRoot, ".intent-cli", "issues", "G2", "packet.yaml")),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenProjectionRegenerateRunTwice_ProducesIdenticalArtifacts()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G2", "packet.yaml"),
            CreatePacketYaml("G2"));
        using var writer = new StringWriter();

        var firstExitCode = CommandRouter.Execute(["projection", "regenerate", "G2"], CreateContext(repoRoot), writer);
        var firstImplementation = File.ReadAllText(Path.Combine(repoRoot, ".intent-cli", "issues", "G2", "implementation.md"));
        var firstReviewContext = File.ReadAllText(Path.Combine(repoRoot, ".intent-cli", "issues", "G2", "review-context.md"));
        var firstPacketYaml = File.ReadAllText(Path.Combine(repoRoot, ".intent-cli", "issues", "G2", "packet.yaml"));

        var secondExitCode = CommandRouter.Execute(["projection", "regenerate", "G2"], CreateContext(repoRoot), writer);

        Assert.Equal(0, firstExitCode);
        Assert.Equal(0, secondExitCode);
        Assert.Equal(
            firstImplementation,
            File.ReadAllText(Path.Combine(repoRoot, ".intent-cli", "issues", "G2", "implementation.md")));
        Assert.Equal(
            firstReviewContext,
            File.ReadAllText(Path.Combine(repoRoot, ".intent-cli", "issues", "G2", "review-context.md")));
        Assert.Equal(
            firstPacketYaml,
            File.ReadAllText(Path.Combine(repoRoot, ".intent-cli", "issues", "G2", "packet.yaml")));
    }

    [Fact]
    public void Execute_G668_ProjectionRegeneratePreservesMaterializedRoutingSnapshot()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        var packetYaml = BranchLaneRoutingYaml.InjectIntoPacketYaml(
            CreatePacketYaml("G668"),
            "hotfix",
            BranchLaneResolver.SourceExplicit,
            new BranchRoutingSnapshot
            {
                LaneId = "hotfix",
                DefinitionRevision = "registry-r1",
                StartBranch = "main",
                PrBaseBranch = "main",
                LandingMode = "direct"
            });
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G668", "packet.yaml"),
            packetYaml);
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G668", "implementation.md"),
            "stale implementation");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G668", "review-context.md"),
            "stale review context");
        using var writer = new StringWriter();

        var exitCode = CommandRouter.Execute(
            ["projection", "regenerate", "G668"],
            CreateContext(repoRoot),
            writer);

        Assert.Equal(0, exitCode);
        var regenerated = File.ReadAllText(
            Path.Combine(repoRoot, ".intent-cli", "issues", "G668", "packet.yaml"));
        Assert.Contains("branch_lane: hotfix", regenerated, StringComparison.Ordinal);
        Assert.Contains("branch_lane_source: explicit", regenerated, StringComparison.Ordinal);
        Assert.Contains("definition_revision: registry-r1", regenerated, StringComparison.Ordinal);
        Assert.Contains("start_branch: main", regenerated, StringComparison.Ordinal);
        Assert.Contains("pr_base_branch: main", regenerated, StringComparison.Ordinal);
    }

    private static CliContext CreateContext(string repoRoot)
    {
        return new CliContext
        {
            RepoRoot = repoRoot,
            Config = new CliConfig
            {
                Project = new ProjectConfig
                {
                    Domain = "intent-system",
                    ArtifactRoot = ".intent-cli"
                }
            }
        };
    }

    private static string CreatePacketYaml(string executionUnit)
    {
        return $$"""
        implementation_issue_packet:
          issue_title: "[{{executionUnit}}] Projection Generate Command"
          issue_kind: "feature"
          source_execution_unit: "{{executionUnit}}"
          goal: "Generate projection packet artifacts from current source data."
          in_scope:
            - "cli projection command"
            - "artifact path baseline"
            - "generator wiring"
          out_of_scope:
            - "queue mutation"
            - "workflow execution"
            - "GitHub mutation"
          target_repo: "J-Tech-Japan/intent-system"
          target_path: "."
          target_part: "cli projection command"
          dependencies:
            - "G1"
            - "A2"
          technical_baseline:
            - "C# / .NET"
            - ".NET 10.0.100+ baseline"
          project_local_guide:
            - "AGENTS.md"
            - "CLAUDE.md"
          intent_baseline:
            - "G1 and A2 are fixed baselines"
          intent_references:
            - "intents/intent-cli/intent-tree/00-map.md"
            - "intents/rules/issue-projection-format.md"
          rules_and_specs:
            - "intents/rules/issue-projection-format.md"
          acceptance_criteria:
            - "projection generate writes implementation.md"
            - "projection regenerate is deterministic"
          verification_evidence:
            - "contract-reviewed"
            - "tests-passing"
            - "acceptance-criteria-checked"
          review_mode: "manual-review"
          completion_action: "open-pr"
          landing_policy: "squash"
        
        review_context_packet:
          source_execution_unit: "{{executionUnit}}"
          parent_intent_root: "intents/intent-cli/intent-tree/00-map.md"
          intent_references:
            - "intents/intent-cli/intent-tree/00-map.md"
            - "intents/rules/issue-projection-format.md"
          rules_and_specs:
            - "intents/rules/issue-projection-format.md"
          acceptance_criteria:
            - "projection generate writes implementation.md"
            - "projection regenerate is deterministic"
          deterministic_review_checks:
            - "artifact path stays under .intent-cli/issues/<execution-unit>/"
            - "projection command stays thin"
          clarification_return_path: ".takt/runs/20260403-122452-issue-31-g2-projection-generat/context/task/order.md"
        """;
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        private readonly string rootPath = Directory.CreateTempSubdirectory("intent-cli-projection-tests-").FullName;

        public string CreateDirectory(string relativePath)
        {
            var fullPath = Path.Combine(rootPath, relativePath);
            Directory.CreateDirectory(fullPath);
            return fullPath;
        }

        public string CreateFile(string relativePath, string contents)
        {
            var fullPath = Path.Combine(rootPath, relativePath);
            var directoryPath = Path.GetDirectoryName(fullPath)
                ?? throw new InvalidOperationException("Temporary file path did not contain a directory.");

            Directory.CreateDirectory(directoryPath);
            File.WriteAllText(fullPath, contents);
            return fullPath;
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
