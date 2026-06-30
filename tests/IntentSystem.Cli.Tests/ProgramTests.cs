using IntentSystem.Cli;
using IntentSystem.Cli.Commands;

namespace IntentSystem.Cli.Tests;

[Collection("WorkerNextActionSharedState")]
public sealed class ProgramTests
{
    private static readonly Lock ProcessStateLock = new();

    [Fact]
    public void Main_GivenProjectStatusCommand_ResolvesRepoRootAndWritesRuntimeBaseline()
    {
        lock (ProcessStateLock)
        {
            using var tempDirectory = new TemporaryDirectory();
            _ = tempDirectory.CreateDirectory("repo");
            tempDirectory.CreateDirectory(Path.Combine("repo", ".intent-cli"));
            tempDirectory.CreateFile(
                Path.Combine("repo", ".intent-cli", "config.toml"),
                """
                default_domain = "intent-cli"
                artifact_root = ".intent-cli"
                worktree_root = ".intent-cli/worktrees"
                """);
            var workingDirectory = tempDirectory.CreateDirectory(Path.Combine("repo", "src", "feature"));
            using var consoleScope = new ConsoleScope();
            using var currentDirectoryScope = new CurrentDirectoryScope(workingDirectory);

            var exitCode = Program.Main(["project", "status"]);
            var output = consoleScope.Out.ToString();
            var repoRootLine = GetRequiredOutputLine(output, "Repo root: ");
            var configPathLine = GetRequiredOutputLine(output, "Config path: ");

            Assert.Equal(0, exitCode);
            Assert.Contains("Domain: intent-cli", output, StringComparison.Ordinal);
            Assert.True(Directory.Exists(repoRootLine), $"Expected repo root directory to exist, but got '{repoRootLine}'.");
            Assert.True(File.Exists(configPathLine), $"Expected config path to exist, but got '{configPathLine}'.");
            Assert.EndsWith(
                Path.Combine("repo", ".intent-cli", "config.toml"),
                configPathLine,
                StringComparison.Ordinal);
            Assert.Equal(string.Empty, consoleScope.Error.ToString());
        }
    }

    [Fact]
    public void CreateBootstrapContext_GivenHostRepoWithSameRepoConfig_LoadsConfiguredValues_G514()
    {
        // G514: bootstrap-routed automation commands (summary /
        // same-repo-metadata-preflight / queue-seed-from-packet) must use the
        // SAME effective project config as the normal path — non-default
        // same-repo topology values must NOT be silently replaced by default
        // bootstrap config. The config uses deliberately non-default values so a
        // default config cannot accidentally pass.
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateDirectory(Path.Combine("repo", ".intent-cli"));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "config.toml"),
            """
            [project]
            domain = "estivo"
            artifact_root = ".intent-cli"
            same_repo_topology = true
            metadata_source_branch = "main-metadata"
            metadata_write_branch = "main-metadata"
            implementation_base_branch = "main"
            base_branch_policy = "main-ai"
            """);
        // Invoke from a nested cwd so the resolver must walk up to the repo root.
        var workingDirectory = tempDirectory.CreateDirectory(Path.Combine("repo", "src", "feature"));

        var context = Program.CreateBootstrapContext(
            workingDirectory,
            ["automation", "summary", "--domain", "estivo"]);

        Assert.Equal(repoRoot, context.RepoRoot);
        Assert.Equal("estivo", context.Config.Project.Domain);
        Assert.True(context.Config.Project.SameRepoTopology);
        Assert.Equal("main-metadata", context.Config.Project.MetadataSourceBranch);
        Assert.Equal("main-metadata", context.Config.Project.MetadataWriteBranch);
        Assert.Equal("main", context.Config.Project.ImplementationBaseBranch);
        Assert.Equal("main-ai", context.Config.Project.BaseBranchPolicy);
    }

    [Fact]
    public void CreateBootstrapContext_GivenNoIntentCliConfig_KeepsSafeDefaultBootstrap_G514()
    {
        // G514: a child/standalone repo with no `.intent-cli/config.toml` must
        // keep the safe default bootstrap behavior — no parent metadata
        // required, default same-repo topology (false), and the cwd as RepoRoot.
        using var tempDirectory = new TemporaryDirectory();
        var childCwd = tempDirectory.CreateDirectory("child-impl");

        var context = Program.CreateBootstrapContext(
            childCwd,
            ["automation", "summary", "mydomain"]);

        Assert.Equal(childCwd, context.RepoRoot);
        Assert.Equal("mydomain", context.Config.Project.Domain);
        Assert.False(context.Config.Project.SameRepoTopology);
        Assert.Equal(string.Empty, context.Config.Project.MetadataSourceBranch);
        Assert.Equal(".intent-cli", context.Config.Project.ArtifactRoot);
    }

    [Fact]
    public void Main_GivenDirectoryWithoutIntentCliRoot_ReturnsExitCodeOne_AndEmitsStructuredFailClosed()
    {
        // G299: a non-bootstrap command (e.g. `project status`) invoked from a
        // directory that has no `.intent-cli/` no longer prints the bare
        // "Could not find .intent-cli directory" error to stderr. Instead it
        // writes a structured fail-closed guidance to stdout naming the host
        // vs child distinction and the canonical re-run path, then exits 1.
        lock (ProcessStateLock)
        {
            using var tempDirectory = new TemporaryDirectory();
            var workingDirectory = tempDirectory.CreateDirectory(Path.Combine("repo", "src", "feature"));
            using var consoleScope = new ConsoleScope();
            using var currentDirectoryScope = new CurrentDirectoryScope(workingDirectory);

            var exitCode = Program.Main(["project", "status"]);

            Assert.Equal(1, exitCode);
            var stdout = consoleScope.Out.ToString();
            Assert.Contains("missing host state (G299)", stdout, StringComparison.Ordinal);
            Assert.Contains("Host repo cwd: _unresolved_", stdout, StringComparison.Ordinal);
            Assert.Contains("Child implementation repo cwd:", stdout, StringComparison.Ordinal);
            Assert.Equal(string.Empty, consoleScope.Error.ToString());
        }
    }

    [Fact]
    public void Main_GivenMissingConfigFile_ReturnsExitCodeOne()
    {
        lock (ProcessStateLock)
        {
            using var tempDirectory = new TemporaryDirectory();
            var repoRoot = tempDirectory.CreateDirectory("repo");
            tempDirectory.CreateDirectory(Path.Combine("repo", ".intent-cli"));
            var workingDirectory = tempDirectory.CreateDirectory(Path.Combine("repo", "src", "feature"));
            using var consoleScope = new ConsoleScope();
            using var currentDirectoryScope = new CurrentDirectoryScope(workingDirectory);

            var exitCode = Program.Main(["project", "status"]);

            Assert.Equal(1, exitCode);
            Assert.Contains(
                Path.Combine(repoRoot, ".intent-cli", "config.toml"),
                consoleScope.Error.ToString(),
                StringComparison.Ordinal);
            Assert.Equal(string.Empty, consoleScope.Out.ToString());
        }
    }

    // ── G300 child worker is host-state-free ─────────────────────────────

    [Fact]
    public void Main_GivenWorkerNextActionFromChildCwdWithoutIntentCli_RunsAgainstGitHubOnly()
    {
        // G300: a child implementation cwd has no `.intent-cli/` and must
        // not be expected to. `worker next-action --repo <r>` should run
        // through Program.Main using a bootstrap context (cwd = RepoRoot)
        // and return a GitHub-derived no-action result, NOT the
        // missing-host-state structured guidance from G299.
        lock (ProcessStateLock)
        {
            using var tempDirectory = new TemporaryDirectory();
            var childCwd = tempDirectory.CreateDirectory("child-impl");
            using var consoleScope = new ConsoleScope();
            using var currentDirectoryScope = new CurrentDirectoryScope(childCwd);

            var lister = new EmptyAutomationCandidateLister();
            WorkerNextActionCommand.CandidateListerFactory = () => lister;
            try
            {
                var exitCode = Program.Main(
                    ["worker", "next-action", "--repo", "J-Tech-Japan/intent-system", "--workdir", childCwd, "--format", "json"]);

                Assert.Equal(0, exitCode);
                var stdout = consoleScope.Out.ToString();
                // G299 missing-host-state guidance must NOT have fired.
                Assert.DoesNotContain("missing host state (G299)", stdout, StringComparison.Ordinal);
                Assert.DoesNotContain("\"status\": \"missing-host-state\"", stdout, StringComparison.Ordinal);
                // Empty GitHub state → deterministic no-action result.
                Assert.Contains("\"action\": \"none\"", stdout, StringComparison.Ordinal);
                Assert.Equal(string.Empty, consoleScope.Error.ToString());
            }
            finally
            {
                WorkerNextActionCommand.CandidateListerFactory = null;
            }
        }
    }

    // ── G333 child-loop guidance is GitHub-contract-only ─────────────────

    [Fact]
    public void Main_GivenGuidePromptMatrixChildLoopFromChildCwdWithoutIntentCli_Succeeds()
    {
        // G333 acceptance: from a standalone child repo cwd that has no
        // `.intent-cli/`, the child-loop guidance command must run
        // (bootstrap context, exit 0, no G299 missing-host-state
        // structured fail-closed). This unblocks Claude / Codex child
        // loops configured against e.g. /Users/.../SekibanAsAService
        // where no parent host root is available.
        lock (ProcessStateLock)
        {
            using var tempDirectory = new TemporaryDirectory();
            var childCwd = tempDirectory.CreateDirectory("child-impl");
            using var consoleScope = new ConsoleScope();
            using var currentDirectoryScope = new CurrentDirectoryScope(childCwd);

            var exitCode = Program.Main(
                ["guide", "prompt-matrix", "--mode", "child-loop", "--format", "json"]);

            Assert.Equal(0, exitCode);
            var stdout = consoleScope.Out.ToString();
            Assert.DoesNotContain("missing host state (G299)", stdout, StringComparison.Ordinal);
            Assert.DoesNotContain("\"status\": \"missing-host-state\"", stdout, StringComparison.Ordinal);
            Assert.Contains("\"mode\": \"child-loop\"", stdout, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Main_GivenGuideHostOwnershipFromChildCwdWithoutIntentCli_Succeeds()
    {
        // G333: `guide host-ownership` is also a read-only surface
        // child loops can reference. Must bootstrap.
        lock (ProcessStateLock)
        {
            using var tempDirectory = new TemporaryDirectory();
            var childCwd = tempDirectory.CreateDirectory("child-impl");
            using var consoleScope = new ConsoleScope();
            using var currentDirectoryScope = new CurrentDirectoryScope(childCwd);

            var exitCode = Program.Main(
                ["guide", "host-ownership", "--role", "child-worker", "--format", "json"]);

            Assert.Equal(0, exitCode);
            var stdout = consoleScope.Out.ToString();
            Assert.DoesNotContain("missing host state (G299)", stdout, StringComparison.Ordinal);
            Assert.Contains("\"focus_role\": \"child-worker\"", stdout, StringComparison.Ordinal);
        }
    }

    private sealed class CurrentDirectoryScope : IDisposable
    {
        private readonly string originalCurrentDirectory = Directory.GetCurrentDirectory();

        public CurrentDirectoryScope(string currentDirectory)
        {
            Directory.SetCurrentDirectory(currentDirectory);
        }

        public void Dispose()
        {
            Directory.SetCurrentDirectory(originalCurrentDirectory);
        }
    }

    private static string GetRequiredOutputLine(string output, string prefix)
    {
        var value = output
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(line => line.StartsWith(prefix, StringComparison.Ordinal));

        return value is null
            ? throw new InvalidOperationException($"Expected output line starting with '{prefix}'.")
            : value[prefix.Length..];
    }

    private sealed class ConsoleScope : IDisposable
    {
        private readonly TextWriter originalError = Console.Error;
        private readonly TextWriter originalOut = Console.Out;

        public StringWriter Error { get; } = new();

        public StringWriter Out { get; } = new();

        public ConsoleScope()
        {
            Console.SetOut(Out);
            Console.SetError(Error);
        }

        public void Dispose()
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
            Out.Dispose();
            Error.Dispose();
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        private readonly string rootPath = Directory.CreateTempSubdirectory("intent-cli-program-tests-").FullName;

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

    /// <summary>
    /// G300: empty GitHub candidate lister for the child-cwd worker test.
    /// Returns no PRs and no issues so `worker next-action` produces the
    /// deterministic <c>action: none</c> result without touching the
    /// network.
    /// </summary>
    private sealed class EmptyAutomationCandidateLister : IGitHubAutomationCandidateLister
    {
        public IReadOnlyList<GitHubAutomationPrCandidate> ListPullRequests(
            string repo,
            IReadOnlyCollection<string> requiredLabels) =>
            Array.Empty<GitHubAutomationPrCandidate>();

        public IReadOnlyList<GitHubAutomationIssueCandidate> ListIssues(
            string repo,
            IReadOnlyCollection<string> requiredLabels) =>
            Array.Empty<GitHubAutomationIssueCandidate>();
    }
}
