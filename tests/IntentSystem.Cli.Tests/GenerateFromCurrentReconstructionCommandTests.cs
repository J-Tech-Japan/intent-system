using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

public sealed class GenerateFromCurrentReconstructionCommandTests
{
    [Fact]
    public void Execute_GivenCurrentSourcesArtifact_GeneratesReconstructedArtifacts()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(Path.Combine("repo", "README.md"), "# Intent System");
        tempDirectory.CreateFile(Path.Combine("repo", "AGENTS.md"), "# Agent Guide");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "intake", "auth.current-sources.yaml"),
            CurrentSourcesArtifactYaml.Serialize(
                new CurrentSourcesArtifact
                {
                    DomainSlug = "auth",
                    SourceRoot = "src/feature",
                    SelectedAltitudes = ["purpose", "rules", "execution"],
                    SelectedIssueScope = "114",
                    SelectedPrScope = "113",
                    SelectedPaths = ["src/feature/FeatureA.cs", "README.md", "AGENTS.md"],
                    SourceRefs =
                    [
                        "issue:114 https://github.com/J-Tech-Japan/intent-system/issues/114 [G44] Generate From Current",
                        "issue-comment:114#1 Need deterministic output.",
                        "pr:113 https://github.com/J-Tech-Japan/intent-system/pull/113 [codex] Add intake activate command",
                        "pr-review:113#1 state=COMMENTED Scope stayed thin.",
                        "code:src/feature/FeatureA.cs",
                        "readme:README.md",
                        "doc:AGENTS.md"
                    ],
                    SamplingNotes =
                    [
                        "issue-comment:114#1 body=Need deterministic output.",
                        "pr-review:113#1 state=COMMENTED body=Scope stayed thin."
                    ],
                    Gaps =
                    [
                        "Need stronger purpose signal."
                    ]
                }));
        using var writer = new StringWriter();

        var exitCode = GenerateFromCurrentCommand.Execute(CreateContext(repoRoot), ["auth"], writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("Generate-from-current reconstruction processed for domain 'auth'.", output, StringComparison.Ordinal);
        Assert.Contains(".intent-cli/intake/auth.reconstructed-concept.yaml", output, StringComparison.Ordinal);
        Assert.Contains(".intent-cli/intake/auth.reconstructed-interview.md", output, StringComparison.Ordinal);
        Assert.Contains("Selected altitudes:", output, StringComparison.Ordinal);
        Assert.Contains("- purpose", output, StringComparison.Ordinal);
        Assert.Contains("- rules", output, StringComparison.Ordinal);
        Assert.Contains("- execution", output, StringComparison.Ordinal);
        Assert.Contains("Candidate intent nodes:", output, StringComparison.Ordinal);
        Assert.Contains("Candidate execution units:", output, StringComparison.Ordinal);
        Assert.Contains("Confidence by altitude:", output, StringComparison.Ordinal);
        Assert.Contains("Recommended follow-up interview questions:", output, StringComparison.Ordinal);
        Assert.Contains("Return-to-intent paths:", output, StringComparison.Ordinal);
        Assert.Contains("Gaps:", output, StringComparison.Ordinal);

        var conceptArtifactPath = Path.Combine(repoRoot, ".intent-cli", "intake", "auth.reconstructed-concept.yaml");
        var interviewArtifactPath = Path.Combine(repoRoot, ".intent-cli", "intake", "auth.reconstructed-interview.md");
        Assert.True(File.Exists(conceptArtifactPath));
        Assert.True(File.Exists(interviewArtifactPath));

        var conceptArtifact = ReconstructedConceptArtifactYaml.Deserialize(File.ReadAllText(conceptArtifactPath));
        Assert.Equal("auth", conceptArtifact.DomainSlug);
        Assert.Equal("Generate From Current", conceptArtifact.InitialGoal);
        Assert.Contains(
            "Clarify the primary purpose for domain 'auth' from selected issue and PR signals.",
            conceptArtifact.CandidateIntentNodes,
            StringComparer.Ordinal);
        Assert.Contains(
            "Execution candidate from src/feature/FeatureA.cs.",
            conceptArtifact.CandidateExecutionUnits,
            StringComparer.Ordinal);
        Assert.Contains("purpose: high", conceptArtifact.ConfidenceByAltitude, StringComparer.Ordinal);
        Assert.Contains("rules: medium", conceptArtifact.ConfidenceByAltitude, StringComparer.Ordinal);
        Assert.Contains("execution: medium", conceptArtifact.ConfidenceByAltitude, StringComparer.Ordinal);
        Assert.Contains(
            "issue:114 https://github.com/J-Tech-Japan/intent-system/issues/114 [G44] Generate From Current",
            conceptArtifact.SourceConceptRefs,
            StringComparer.Ordinal);

        var interviewMarkdown = File.ReadAllText(interviewArtifactPath);
        Assert.Contains("# Reconstructed Interview", interviewMarkdown, StringComparison.Ordinal);
        Assert.Contains("recommended_follow_up_questions:", interviewMarkdown, StringComparison.Ordinal);
        Assert.Contains("What user-facing outcome should 'auth' prioritize based on the selected current signals?", interviewMarkdown, StringComparison.Ordinal);
        Assert.Contains("Which execution-ready change slice should be cut first from the selected current paths?", interviewMarkdown, StringComparison.Ordinal);
        Assert.Contains("return_to_intent_paths:", interviewMarkdown, StringComparison.Ordinal);
        Assert.Contains("- README.md", interviewMarkdown, StringComparison.Ordinal);
        Assert.Contains("- AGENTS.md", interviewMarkdown, StringComparison.Ordinal);
        Assert.Contains("gaps:", interviewMarkdown, StringComparison.Ordinal);
        Assert.Contains("- Need stronger purpose signal.", interviewMarkdown, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenMissingCurrentSourcesArtifact_ReturnsExitCodeOne()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        using var writer = new StringWriter();

        var exitCode = GenerateFromCurrentCommand.Execute(CreateContext(repoRoot), ["auth"], writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("Current sources artifact was not found", writer.ToString(), StringComparison.Ordinal);
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
                    WorkflowEngine = "intent-cli",
                    ArtifactRoot = ".intent-cli",
                    WorktreeRoot = ".intent-cli/worktrees"
                }
            }
        };
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        private readonly string rootPath = Directory.CreateTempSubdirectory("intent-cli-generate-from-current-reconstruction-tests-").FullName;

        public string CreateDirectory(string relativePath)
        {
            var fullPath = Path.Combine(rootPath, relativePath);
            Directory.CreateDirectory(fullPath);
            return fullPath;
        }

        public string CreateFile(string relativePath, string contents)
        {
            var fullPath = Path.Combine(rootPath, relativePath);
            var directoryPath = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(directoryPath))
            {
                Directory.CreateDirectory(directoryPath);
            }

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
