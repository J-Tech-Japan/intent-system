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
            Path.Combine("repo", ".intent-cli", "sources", "G2", "source.json"),
            CreateSourceBundleJson("G2"));
        using var writer = new StringWriter();

        var exitCode = CommandRouter.Execute(["projection", "generate", "G2"], CreateContext(repoRoot), writer);

        Assert.Equal(0, exitCode);
        Assert.True(File.Exists(Path.Combine(repoRoot, ".intent-cli", "issues", "g2", "implementation.md")));
        Assert.True(File.Exists(Path.Combine(repoRoot, ".intent-cli", "issues", "g2", "review-context.md")));
        Assert.True(File.Exists(Path.Combine(repoRoot, ".intent-cli", "issues", "g2", "packet.yaml")));
        Assert.Contains(
            "# [G2] Projection Generate Command",
            File.ReadAllText(Path.Combine(repoRoot, ".intent-cli", "issues", "g2", "implementation.md")),
            StringComparison.Ordinal);
        Assert.Contains(
            "# Review Context",
            File.ReadAllText(Path.Combine(repoRoot, ".intent-cli", "issues", "g2", "review-context.md")),
            StringComparison.Ordinal);
        Assert.Contains(
            "source_execution_unit: G2",
            File.ReadAllText(Path.Combine(repoRoot, ".intent-cli", "issues", "g2", "packet.yaml")),
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
    public void Execute_GivenProjectionGenerateAndMissingSourceBundle_ReturnsExitCodeOne()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        using var writer = new StringWriter();

        var exitCode = CommandRouter.Execute(["projection", "generate", "G2"], CreateContext(repoRoot), writer);

        Assert.Equal(1, exitCode);
        Assert.Contains(".intent-cli", writer.ToString(), StringComparison.Ordinal);
        Assert.Contains("source.json", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenProjectionGenerateAndMismatchedBundleExecutionUnit_ReturnsExitCodeOne()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "sources", "G2", "source.json"),
            CreateSourceBundleJson("G3"));
        using var writer = new StringWriter();

        var exitCode = CommandRouter.Execute(["projection", "generate", "G2"], CreateContext(repoRoot), writer);

        Assert.Equal(1, exitCode);
        Assert.False(File.Exists(Path.Combine(repoRoot, ".intent-cli", "issues", "g2", "implementation.md")));
        Assert.Contains("G3", writer.ToString(), StringComparison.Ordinal);
        Assert.Contains("G2", writer.ToString(), StringComparison.Ordinal);
        Assert.Contains("must match", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenProjectionGenerateAndExistingArtifacts_ReturnsExitCodeOneWithoutOverwriting()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        var implementationPath = Path.Combine(repoRoot, ".intent-cli", "issues", "g2", "implementation.md");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "sources", "G2", "source.json"),
            CreateSourceBundleJson("G2"));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "g2", "implementation.md"),
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
            Path.Combine("repo", ".intent-cli", "sources", "G2", "source.json"),
            CreateSourceBundleJson("G2"));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "g2", "implementation.md"),
            "stale implementation");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "g2", "review-context.md"),
            "stale review context");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "g2", "packet.yaml"),
            "stale packet yaml");
        using var writer = new StringWriter();

        var exitCode = CommandRouter.Execute(["projection", "regenerate", "G2"], CreateContext(repoRoot), writer);

        Assert.Equal(0, exitCode);
        Assert.Contains(
            "## Goal",
            File.ReadAllText(Path.Combine(repoRoot, ".intent-cli", "issues", "g2", "implementation.md")),
            StringComparison.Ordinal);
        Assert.Contains(
            "## Deterministic Review Checks",
            File.ReadAllText(Path.Combine(repoRoot, ".intent-cli", "issues", "g2", "review-context.md")),
            StringComparison.Ordinal);
        Assert.Contains(
            "review_context_packet:",
            File.ReadAllText(Path.Combine(repoRoot, ".intent-cli", "issues", "g2", "packet.yaml")),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenProjectionRegenerateRunTwice_ProducesIdenticalArtifacts()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "sources", "G2", "source.json"),
            CreateSourceBundleJson("G2"));
        using var writer = new StringWriter();

        var firstExitCode = CommandRouter.Execute(["projection", "regenerate", "G2"], CreateContext(repoRoot), writer);
        var firstImplementation = File.ReadAllText(Path.Combine(repoRoot, ".intent-cli", "issues", "g2", "implementation.md"));
        var firstReviewContext = File.ReadAllText(Path.Combine(repoRoot, ".intent-cli", "issues", "g2", "review-context.md"));
        var firstPacketYaml = File.ReadAllText(Path.Combine(repoRoot, ".intent-cli", "issues", "g2", "packet.yaml"));

        var secondExitCode = CommandRouter.Execute(["projection", "regenerate", "G2"], CreateContext(repoRoot), writer);

        Assert.Equal(0, firstExitCode);
        Assert.Equal(0, secondExitCode);
        Assert.Equal(
            firstImplementation,
            File.ReadAllText(Path.Combine(repoRoot, ".intent-cli", "issues", "g2", "implementation.md")));
        Assert.Equal(
            firstReviewContext,
            File.ReadAllText(Path.Combine(repoRoot, ".intent-cli", "issues", "g2", "review-context.md")));
        Assert.Equal(
            firstPacketYaml,
            File.ReadAllText(Path.Combine(repoRoot, ".intent-cli", "issues", "g2", "packet.yaml")));
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
                    WorkflowEngine = "takt",
                    ArtifactRoot = ".intent-cli"
                }
            }
        };
    }

    private static string CreateSourceBundleJson(string executionUnit)
    {
        return $$"""
        {
          "row": {
            "source_execution_unit": "{{executionUnit}}",
            "goal": "Generate projection packet artifacts from current source data.",
            "target_repo": "J-Tech-Japan/intent-system",
            "target_path": ".",
            "target_part": "cli projection command",
            "depends_on_subslices": ["G1", "A2"],
            "depends_on": [],
            "related_intents": ["intents/intent-cli/intent-tree/00-map.md"],
            "source_concepts": ["intents/rules/issue-projection-format.md"],
            "success_signal": "projection command can regenerate packet artifacts deterministically",
            "review_mode": "manual-review",
            "completion_action": "open-pr",
            "landing_policy": "squash"
          },
          "context": {
            "issue_title": "[{{executionUnit}}] Projection Generate Command",
            "issue_kind": "feature",
            "parent_intent_root": "intents/intent-cli/intent-tree/00-map.md",
            "clarification_return_path": ".takt/runs/20260403-122452-issue-31-g2-projection-generat/context/task/order.md",
            "acceptance_criteria": [
              "projection generate writes implementation.md",
              "projection regenerate is deterministic"
            ],
            "deterministic_review_checks": [
              "artifact path stays under .intent-cli/issues/<execution-unit>/",
              "projection command stays thin"
            ],
            "verification_evidence": [
              "contract-reviewed",
              "tests-passing",
              "acceptance-criteria-checked"
            ],
            "technical_baseline": [
              "C# / .NET",
              ".NET 10.0.100+ baseline"
            ],
            "project_local_guide": ["AGENTS.md", "CLAUDE.md"],
            "intent_baseline": [
              "G1 and A2 are fixed baselines"
            ],
            "additional_in_scope": [
              "artifact path baseline",
              "generator wiring"
            ],
            "out_of_scope": [
              "queue mutation",
              "workflow execution",
              "GitHub mutation"
            ]
          }
        }
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
