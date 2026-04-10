using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

[Collection(RunSubmitCommandCollection.Name)]
public sealed class GenerateFromCurrentCommandTests
{
    [Fact]
    public void Execute_GivenSelectedSignals_WritesCurrentSourcesArtifact()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(Path.Combine("repo", "README.md"), "# Intent System");
        tempDirectory.CreateFile(Path.Combine("repo", "AGENTS.md"), "# Agent Guide");
        tempDirectory.CreateFile(Path.Combine("repo", "CLAUDE.md"), "# Claude Guide");
        tempDirectory.CreateFile(Path.Combine("repo", "src", "feature", "FeatureA.cs"), "namespace FeatureA;");
        tempDirectory.CreateFile(Path.Combine("repo", "src", "feature", "FeatureB.cs"), "namespace FeatureB;");
        tempDirectory.CreateFile(Path.Combine("repo", "src", "feature", "FeatureC.cs"), "namespace FeatureC;");
        tempDirectory.CreateFile(Path.Combine("repo", "tests", "FeatureA.Tests", "FeatureATests.cs"), "namespace FeatureATests;");
        using var writer = new StringWriter();
        var originalFactory = GenerateFromCurrentCommand.GitHubCommandRunnerFactory;

        try
        {
            GenerateFromCurrentCommand.GitHubCommandRunnerFactory = () => new FakeGitHubRunner();

            var exitCode = GenerateFromCurrentCommand.Execute(
                CreateContext(repoRoot),
                ["auth", "--from-path", "src/feature", "--issues", "114", "--prs", "113", "--altitudes", "means,execution", "--include-readme", "--include-docs", "--include-tests", "--max-files", "2"],
                writer);

            Assert.Equal(0, exitCode);
            var output = writer.ToString();
            Assert.Contains("Generate-from-current processed for domain 'auth'.", output, StringComparison.Ordinal);
            Assert.Contains("Artifact path: .intent-cli/intake/auth.current-sources.yaml", output, StringComparison.Ordinal);
            Assert.Contains("Source root: src/feature", output, StringComparison.Ordinal);
            Assert.Contains("Selected issue scope: 114", output, StringComparison.Ordinal);
            Assert.Contains("Selected PR scope: 113", output, StringComparison.Ordinal);
            Assert.Contains("- means", output, StringComparison.Ordinal);
            Assert.Contains("- execution", output, StringComparison.Ordinal);
            Assert.Contains("- src/feature/FeatureA.cs", output, StringComparison.Ordinal);
            Assert.Contains("- src/feature/FeatureB.cs", output, StringComparison.Ordinal);
            Assert.DoesNotContain("FeatureC.cs", output, StringComparison.Ordinal);
            Assert.Contains("- issue:114 https://github.com/J-Tech-Japan/intent-system/issues/114 [G44] Generate From Current", output, StringComparison.Ordinal);
            Assert.Contains("- issue-comment:114#1 Need deterministic output.", output, StringComparison.Ordinal);
            Assert.Contains("- pr:113 https://github.com/J-Tech-Japan/intent-system/pull/113 [codex] Add intake activate command", output, StringComparison.Ordinal);
            Assert.Contains("- pr-comment:113#1 Looks good.", output, StringComparison.Ordinal);
            Assert.Contains("- pr-review:113#1 state=COMMENTED Scope stayed thin.", output, StringComparison.Ordinal);
            Assert.Contains("code scope truncated to first 2 files out of 3 eligible files", output, StringComparison.Ordinal);
            Assert.Contains("issue-comment:114#1 body=Need deterministic output.", output, StringComparison.Ordinal);
            Assert.Contains("pr-comment:113#1 body=Looks good.", output, StringComparison.Ordinal);
            Assert.Contains("pr-review:113#1 state=COMMENTED body=Scope stayed thin.", output, StringComparison.Ordinal);

            var artifactPath = Path.Combine(repoRoot, ".intent-cli", "intake", "auth.current-sources.yaml");
            Assert.True(File.Exists(artifactPath));
            var artifact = CurrentSourcesArtifactYaml.Deserialize(File.ReadAllText(artifactPath));
            Assert.Equal("auth", artifact.DomainSlug);
            Assert.Equal("src/feature", artifact.SourceRoot);
            Assert.Equal(["means", "execution"], artifact.SelectedAltitudes);
            Assert.Equal("114", artifact.SelectedIssueScope);
            Assert.Equal("113", artifact.SelectedPrScope);
            Assert.Equal(
                [
                    "src/feature/FeatureA.cs",
                    "src/feature/FeatureB.cs",
                    "README.md",
                    "AGENTS.md",
                    "CLAUDE.md",
                    "tests/FeatureA.Tests/FeatureATests.cs"
                ],
                artifact.SelectedPaths);
            Assert.Contains("issue:114 https://github.com/J-Tech-Japan/intent-system/issues/114 [G44] Generate From Current", artifact.SourceRefs, StringComparer.Ordinal);
            Assert.Contains("issue-comment:114#1 Need deterministic output.", artifact.SourceRefs, StringComparer.Ordinal);
            Assert.Contains("pr:113 https://github.com/J-Tech-Japan/intent-system/pull/113 [codex] Add intake activate command", artifact.SourceRefs, StringComparer.Ordinal);
            Assert.Contains("pr-comment:113#1 Looks good.", artifact.SourceRefs, StringComparer.Ordinal);
            Assert.Contains("pr-review:113#1 state=COMMENTED Scope stayed thin.", artifact.SourceRefs, StringComparer.Ordinal);
            Assert.Contains("issue-comment:114#1 body=Need deterministic output.", artifact.SamplingNotes, StringComparer.Ordinal);
            Assert.Contains("pr-comment:113#1 body=Looks good.", artifact.SamplingNotes, StringComparer.Ordinal);
            Assert.Contains("pr-review:113#1 state=COMMENTED body=Scope stayed thin.", artifact.SamplingNotes, StringComparer.Ordinal);
            Assert.DoesNotContain(artifact.Gaps, gap => gap.Contains("sparse signal", StringComparison.Ordinal));
        }
        finally
        {
            GenerateFromCurrentCommand.GitHubCommandRunnerFactory = originalFactory;
        }
    }

    [Fact]
    public void Execute_GivenSourceBundleModeWithoutFromPathValue_ReturnsExitCodeOne()
    {
        using var writer = new StringWriter();

        var exitCode = GenerateFromCurrentCommand.Execute(CreateContext("/tmp/intent-system"), ["auth", "--from-path"], writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("--from-path requires a value", writer.ToString(), StringComparison.Ordinal);
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
                    ArtifactRoot = ".intent-cli",
                    WorktreeRoot = ".intent-cli/worktrees"
                }
            }
        };
    }

    private sealed class FakeGitHubRunner : IGitHubCommandRunner
    {
        public GitHubCommandResult Run(IReadOnlyList<string> arguments)
        {
            if (arguments.SequenceEqual(["issue", "view", "114", "--comments", "--json", "number,title,body,url,state,comments"]))
            {
                return Success("""{"number":114,"title":"[G44] Generate From Current","body":"Reconstruct from selected current signals.","url":"https://github.com/J-Tech-Japan/intent-system/issues/114","state":"OPEN","comments":[{"body":"Need deterministic output."}]}""");
            }

            if (arguments.SequenceEqual(["pr", "view", "113", "--comments", "--json", "number,title,body,url,state,isDraft,mergeStateStatus,comments,reviews"]))
            {
                return Success("""{"number":113,"title":"[codex] Add intake activate command","body":"Adds intake activate.","url":"https://github.com/J-Tech-Japan/intent-system/pull/113","state":"OPEN","isDraft":true,"mergeStateStatus":"CLEAN","comments":[{"body":"Looks good."}],"reviews":[{"state":"COMMENTED","body":"Scope stayed thin."}]}""");
            }

            throw new InvalidOperationException($"Unexpected gh arguments: {string.Join(' ', arguments)}");
        }

        private static GitHubCommandResult Success(string stdOut)
        {
            return new GitHubCommandResult
            {
                ExitCode = 0,
                StdOut = stdOut,
                StdErr = string.Empty
            };
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        private readonly string rootPath = Directory.CreateTempSubdirectory("intent-cli-generate-from-current-tests-").FullName;

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
