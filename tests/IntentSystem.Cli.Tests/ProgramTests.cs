using System.Text.Json;
using System.Text;
using System.Diagnostics;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

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
    public void Main_PacketValidateSources_UsesNestedRepoRootWithoutConfig()
    {
        lock (ProcessStateLock)
        {
            using var tempDirectory = new TemporaryDirectory();
            var repoRoot = tempDirectory.CreateDirectory("repo");
            tempDirectory.CreateDirectory(Path.Combine("repo", ".intent-cli", "issues", "G863"));
            tempDirectory.CreateFile(Path.Combine("repo", ".intent-cli", "issues", "G863", "packet.yaml"),
                "implementation_issue_packet:\n  source_execution_unit: G863\n  domain: intent-cli\n");
            tempDirectory.CreateFile(Path.Combine("repo", ".intent-cli", "issues", "G863", "github-body.md"), "# Legacy packet\n");
            var nestedCwd = tempDirectory.CreateDirectory(Path.Combine("repo", "src", "feature"));
            Assert.False(File.Exists(Path.Combine(repoRoot, ".intent-cli", "config.toml")));
            using var console = new ConsoleScope();
            using var currentDirectory = new CurrentDirectoryScope(nestedCwd);

            var exitCode = Program.Main(["packet", "validate-sources", "--execution-unit", "G863", "--format", "json"]);

            Assert.Equal(0, exitCode);
            using var result = JsonDocument.Parse(console.Out.ToString());
            Assert.Equal("not-declared", result.RootElement.GetProperty("state").GetString());
            Assert.Equal("scope-sources-not-declared", result.RootElement.GetProperty("cause").GetString());
            Assert.False(Directory.Exists(Path.Combine(repoRoot, ".intent-cli", "rulings")));
            Assert.Equal(string.Empty, console.Error.ToString());
        }
    }

    [Fact]
    public void Main_PacketValidateSourcesHelpShowsTheRegisteredRoute()
    {
        lock (ProcessStateLock)
        {
            using var tempDirectory = new TemporaryDirectory();
            var repoRoot = tempDirectory.CreateDirectory("repo");
            tempDirectory.CreateDirectory(Path.Combine("repo", ".intent-cli"));
            var nestedCwd = tempDirectory.CreateDirectory(Path.Combine("repo", "src", "feature"));
            Assert.False(File.Exists(Path.Combine(repoRoot, ".intent-cli", "config.toml")));
            using var console = new ConsoleScope();
            using var currentDirectory = new CurrentDirectoryScope(nestedCwd);

            var exit = Program.Main(["packet", "validate-sources", "--help"]);

            Assert.Equal(0, exit);
            Assert.Contains("Usage: intent-cli packet validate-sources --execution-unit <unit> --format json|markdown", console.Out.ToString(), StringComparison.Ordinal);
            Assert.Contains("Validates declared ruling pins", console.Out.ToString(), StringComparison.Ordinal);
            Assert.False(Directory.Exists(Path.Combine(repoRoot, ".intent-cli", "rulings")));
            Assert.Equal(string.Empty, console.Error.ToString());
        }
    }

    [Fact]
    public void Main_PacketValidateSources_RequiresPlainPushAndWorksFromFreshCloneWithoutConfig()
    {
        lock (ProcessStateLock)
        {
            using var tempDirectory = new TemporaryDirectory(OperatingSystem.IsMacOS() ? "/private/tmp" : null);
            var bare = tempDirectory.CreateDirectory("origin.git");
            var writerRepo = Path.Combine(tempDirectory.CreateDirectory("repos"), "writer");
            var beforeRepo = Path.Combine(tempDirectory.CreateDirectory("repos"), "before-publication");
            var afterRepo = Path.Combine(tempDirectory.CreateDirectory("repos"), "after-publication");
            RunGit(tempDirectory.CreateDirectory("."), "init", "--bare", "--quiet", "--initial-branch=main", bare);
            Directory.CreateDirectory(writerRepo);
            RunGit(writerRepo, "init", "--quiet", "--initial-branch=main");
            RunGit(writerRepo, "config", "user.name", "intent-cli-g863-test");
            RunGit(writerRepo, "config", "user.email", "intent-cli-g863@example.invalid");
            RunGit(writerRepo, "remote", "add", "origin", bare);
            File.WriteAllText(Path.Combine(writerRepo, "README.md"), "G863 fresh-clone source fixture\n");
            RunGit(writerRepo, "add", "README.md");
            RunGit(writerRepo, "commit", "--quiet", "-m", "baseline");
            RunGit(writerRepo, "push", "--quiet", "origin", "HEAD:main");

            var unit = "G863";
            var id = "R-G863-REMOTE";
            var domain = "intent-cli";
            var team = "intent-cli-dev";
            var targetRepo = "J-Tech-Japan/intent-system";
            var source = new RulingArtifact
            {
                Id = id,
                Domain = domain,
                Team = team,
                AuthorityRole = "operator",
                TargetRepo = targetRepo,
                ScopeKind = "execution-units",
                ExecutionUnits = [unit],
                Decision = "Use this published source.",
                Rationale = "The actual writer emits the pinned canonical bytes.",
                EvidenceRefs = ["https://github.com/J-Tech-Japan/intent-system/issues/1887"],
                RecordedAt = DateTimeOffset.Parse("2026-10-10T11:00:00Z"),
                ExpiresAt = null,
                Supersedes = [],
            };
            var rulingInput = Path.Combine(writerRepo, "ruling-input.json");
            File.WriteAllBytes(rulingInput, RulingArtifact.Serialize(source));
            var inputContext = new CliContext
            {
                RepoRoot = writerRepo,
                Config = new CliConfig { Project = new ProjectConfig { Domain = domain, ArtifactRoot = ".intent-cli" } },
            };
            using var rulingOutput = new StringWriter();
            var rulingExit = RulingCommand.ExecuteRecord(inputContext,
                ["--id", id, "--domain", domain, "--team", team, "--from-file", rulingInput,
                 "--authority-role", "operator", "--write", "--format", "json"], rulingOutput, null, source.RecordedAt);
            Assert.True(rulingExit == 0, rulingOutput.ToString());
            using var writerResult = JsonDocument.Parse(rulingOutput.ToString());
            Assert.True(writerResult.RootElement.GetProperty("wrote").GetBoolean());
            Assert.Equal("not-verified", writerResult.RootElement.GetProperty("publication_status").GetString());
            var rulingRelativePath = Path.Combine(".intent-cli", "rulings", domain, team, id + ".json");
            var rulingBytes = File.ReadAllBytes(Path.Combine(writerRepo, rulingRelativePath));
            var rulingDigest = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(rulingBytes)).ToLowerInvariant();

            var packetDirectory = Path.Combine(writerRepo, ".intent-cli", "issues", unit);
            Directory.CreateDirectory(packetDirectory);
            var pinnedYaml = $$"""
                implementation_issue_packet:
                  source_execution_unit: {{unit}}
                  domain: {{domain}}
                  team: {{team}}
                  target_repo: {{targetRepo}}
                scope_sources:
                  - "ruling:{{id}}"
                scope_source_digests:
                  "ruling:{{id}}": "{{rulingDigest}}"
                """;
            File.WriteAllText(Path.Combine(packetDirectory, "packet.yaml"), pinnedYaml);
            File.WriteAllText(Path.Combine(packetDirectory, "implementation.md"), "# Implementation\n");
            File.WriteAllText(Path.Combine(packetDirectory, "review-context.md"), "# Review context\n");
            var authoredBody = "# G863 pinned source\n";
            var expectedBlock = PacketScopeSources.Evaluate(writerRepo, unit, System.Text.Encoding.UTF8.GetBytes(pinnedYaml),
                System.Text.Encoding.UTF8.GetBytes(authoredBody), source.RecordedAt);
            Assert.Equal("scope-sources-provenance-mismatch", expectedBlock.Cause);
            Assert.NotNull(expectedBlock.ExpectedProvenanceBlock);
            File.WriteAllText(Path.Combine(packetDirectory, "github-body.md"), authoredBody.TrimEnd() + "\n\n" + expectedBlock.ExpectedProvenanceBlock + "\n");

            var packetFiles = new[]
            {
                Path.Combine(".intent-cli", "issues", unit, "packet.yaml"),
                Path.Combine(".intent-cli", "issues", unit, "implementation.md"),
                Path.Combine(".intent-cli", "issues", unit, "review-context.md"),
                Path.Combine(".intent-cli", "issues", unit, "github-body.md"),
            };
            RunGit(writerRepo, new[] { "add", "--" }.Concat(packetFiles).ToArray());
            RunGit(writerRepo, "commit", "--quiet", "-m", "publish four-file packet with exact ruling pin and provenance");
            RunGit(writerRepo, "push", "--quiet", "origin", "HEAD:main");
            RunGit(tempDirectory.CreateDirectory("."), "clone", "--quiet", bare, beforeRepo);
            Assert.False(File.Exists(Path.Combine(beforeRepo, ".intent-cli", "config.toml")));
            Assert.False(File.Exists(Path.Combine(beforeRepo, rulingRelativePath)));
            AssertPacketValidationFromClone(beforeRepo, unit, "scope-sources-ruling-missing", 1);

            RunGit(writerRepo, "add", "--", rulingRelativePath);
            RunGit(writerRepo, "commit", "--quiet", "-m", "publish pinned ruling artifact");
            RunGit(writerRepo, "push", "--quiet", "origin", "HEAD:main");
            RunGit(tempDirectory.CreateDirectory("."), "clone", "--quiet", bare, afterRepo);
            Assert.False(File.Exists(Path.Combine(afterRepo, ".intent-cli", "config.toml")));
            Assert.Equal(rulingBytes, File.ReadAllBytes(Path.Combine(afterRepo, rulingRelativePath)));

            Assert.False(Directory.Exists(Path.Combine(beforeRepo, ".intent-cli", "rulings")));
            AssertPacketValidationFromClone(afterRepo, unit, "scope-sources-satisfied", 0, rulingDigest);
            var foreignConfig = Encoding.UTF8.GetBytes("[project]\ndomain = \"foreign-default\"\nartifact_root = \".foreign-artifacts\"\nworktree_root = \".foreign-worktrees\"\n");
            File.WriteAllBytes(Path.Combine(afterRepo, ".intent-cli", "config.toml"), foreignConfig);
            AssertPacketValidationFromClone(afterRepo, unit, "scope-sources-satisfied", 0, rulingDigest, foreignConfig);
            RunGit(afterRepo, "remote", "set-url", "origin", "https://github.com/foreign-owner/foreign-repository.git");
            Assert.Equal("https://github.com/foreign-owner/foreign-repository.git", RunGit(afterRepo, "remote", "get-url", "origin"));
            AssertPacketValidationFromClone(afterRepo, unit, "scope-sources-satisfied", 0, rulingDigest, foreignConfig, targetRepo);
        }
    }

    private static void AssertPacketValidationFromClone(string repoRoot, string unit, string expectedCause, int expectedExit,
        string? expectedDigest = null, byte[]? expectedConfigBytes = null, string? expectedTargetRepo = null)
    {
        var nestedCwd = Directory.CreateDirectory(Path.Combine(repoRoot, "src", "feature")).FullName;
        var configPath = Path.Combine(repoRoot, ".intent-cli", "config.toml");
        if (expectedConfigBytes is null)
        {
            Assert.False(File.Exists(configPath));
        }
        else
        {
            Assert.Equal(expectedConfigBytes, File.ReadAllBytes(configPath));
        }
        var headBefore = RunGit(repoRoot, "rev-parse", "HEAD");
        var refsBefore = RunGit(repoRoot, "show-ref", "--head");
        var indexPath = Path.Combine(repoRoot, ".git", "index");
        var indexBefore = File.ReadAllBytes(indexPath);
        using var console = new ConsoleScope();
        using var currentDirectory = new CurrentDirectoryScope(nestedCwd);

        var exit = Program.Main(["packet", "validate-sources", "--execution-unit", unit, "--format", "json"]);

        Assert.Equal(expectedExit, exit);
        using var result = JsonDocument.Parse(console.Out.ToString());
        Assert.True(expectedCause == result.RootElement.GetProperty("cause").GetString(), console.Out.ToString());
        Assert.Equal("not-verified", result.RootElement.GetProperty("publication").GetString());
        Assert.Equal("supplied-not-authenticated", result.RootElement.GetProperty("authority_verification").GetString());
        if (expectedDigest is not null)
        {
            var provenance = result.RootElement.GetProperty("provenance")[0];
            Assert.Equal(expectedDigest, provenance.GetProperty("sha256").GetString());
            Assert.StartsWith(".intent-cli/rulings/", provenance.GetProperty("path").GetString()!, StringComparison.Ordinal);
            if (expectedTargetRepo is not null)
            {
                Assert.Equal(expectedTargetRepo, provenance.GetProperty("target_repo").GetString());
            }
        }
        Assert.Equal(headBefore, RunGit(repoRoot, "rev-parse", "HEAD"));
        Assert.Equal(refsBefore, RunGit(repoRoot, "show-ref", "--head"));
        Assert.Equal(indexBefore, File.ReadAllBytes(indexPath));
        if (expectedConfigBytes is null)
        {
            Assert.False(File.Exists(configPath));
        }
        else
        {
            Assert.Equal(expectedConfigBytes, File.ReadAllBytes(configPath));
        }
        Assert.Equal(string.Empty, console.Error.ToString());
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
    public void Main_UnitStatusUsesRecordedNonSoloHostEntryAndDoesNotObserveGithub()
    {
        lock (ProcessStateLock)
        {
            using var tempDirectory = new TemporaryDirectory();
            var repoRoot = tempDirectory.CreateDirectory("repo");
            tempDirectory.CreateDirectory(Path.Combine("repo", ".intent-cli"));
            tempDirectory.CreateFile(
                Path.Combine("repo", ".intent-cli", "config.toml"),
                """
                [project]
                domain = "intent-cli"
                artifact_root = ".intent-cli"
                metadata_source_branch = "metadata"
                """);
            InitializeReadOnlyMetadataRef(repoRoot);
            var timestamp = DateTimeOffset.Parse("2026-10-01T00:00:00Z");
            var modeState = new TeamModeState
            {
                SchemaVersion = TeamModeStore.SchemaVersion,
                Entries =
                [
                    new TeamModeEntry
                    {
                        Domain = "intent-cli",
                        Team = "intent-cli-dev",
                        Mode = TeamMode.Delivery,
                        UpdatedAt = timestamp,
                        Transitions = [new TeamModeTransition { From = TeamMode.Default, To = TeamMode.Delivery, At = timestamp }],
                    },
                ],
            };
            File.WriteAllText(TeamModeStore.ResolvePath(repoRoot), JsonSerializer.Serialize(modeState));
            var packetDirectory = Directory.CreateDirectory(Path.Combine(repoRoot, ".intent-cli", "issues", "G855"));
            File.WriteAllText(Path.Combine(packetDirectory.FullName, "packet.yaml"),
                "schema_version: 1\ndomain: intent-cli\nexecution_unit: G855\nimplementation_issue_packet:\n  target_repo: J-Tech-Japan/intent-system\n");
            var publish = new IssuePublishArtifact
            {
                ExecutionUnit = "G855",
                PublishStatus = "published",
                PacketPath = ".intent-cli/issues/G855/packet.yaml",
                IssueBodyPath = ".intent-cli/issues/G855/github-body.md",
                CreatedIssueNumber = 1862,
                CreatedIssueUrl = "https://github.com/J-Tech-Japan/intent-system/issues/1862",
                PublishedLabelName = "intent-target",
                LifecycleState = "pr-created",
                LinkedPrNumber = 1863,
                LinkedPrUrl = "https://github.com/J-Tech-Japan/intent-system/pull/1863",
            };
            File.WriteAllText(Path.Combine(packetDirectory.FullName, "publish.yaml"), IssuePublishArtifactYaml.Serialize(publish));
            var workingDirectory = tempDirectory.CreateDirectory(Path.Combine("repo", "src", "feature"));
            var headBefore = RunGit(repoRoot, "rev-parse", "HEAD");
            var metadataBefore = RunGit(repoRoot, "rev-parse", "refs/remotes/origin/metadata");
            var indexBefore = File.ReadAllBytes(Path.Combine(repoRoot, ".git", "index"));
            using var consoleScope = new ConsoleScope();
            using var currentDirectoryScope = new CurrentDirectoryScope(workingDirectory);

            var exitCode = Program.Main(
                ["unit", "status", "--execution-unit", "G855", "--domain", "intent-cli", "--team", "intent-cli-dev", "--format", "json"]);

            Assert.Equal(0, exitCode);
            using var report = JsonDocument.Parse(consoleScope.Out.ToString());
            var root = report.RootElement;
            Assert.Equal("delivery", root.GetProperty("team_mode").GetString());
            Assert.Equal("current-recorded-entry", root.GetProperty("mode_basis").GetString());
            Assert.Equal("not-observed", root.GetProperty("observation").GetProperty("github_snapshot").GetProperty("state").GetString());
            Assert.All(root.GetProperty("steps").EnumerateArray(), step => Assert.Equal("not-applicable", step.GetProperty("state").GetString()));
            Assert.Equal(headBefore, RunGit(repoRoot, "rev-parse", "HEAD"));
            Assert.Equal(metadataBefore, RunGit(repoRoot, "rev-parse", "refs/remotes/origin/metadata"));
            Assert.Equal(indexBefore, File.ReadAllBytes(Path.Combine(repoRoot, ".git", "index")));
        }
    }

    private static string RunGit(string repoRoot, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            WorkingDirectory = repoRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start git fixture command.");
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.Equal(0, process.ExitCode);
        Assert.Equal(string.Empty, stderr);
        return stdout.Trim();
    }

    private static void InitializeReadOnlyMetadataRef(string repoRoot)
    {
        _ = RunGit(repoRoot, "init", "--quiet", "--initial-branch=main");
        _ = RunGit(repoRoot, "config", "user.name", "intent-cli-g855-test");
        _ = RunGit(repoRoot, "config", "user.email", "intent-cli-g855@example.invalid");
        File.WriteAllText(Path.Combine(repoRoot, "README.md"), "G855 status fixture\n");
        _ = RunGit(repoRoot, "add", "--", "README.md");
        _ = RunGit(repoRoot, "commit", "--quiet", "-m", "seed G855 readonly status fixture");
        var head = RunGit(repoRoot, "rev-parse", "HEAD");
        _ = RunGit(repoRoot, "update-ref", "refs/remotes/origin/metadata", head);
    }

    [Fact]
    public void Main_UnitStatusMalformedHostConfigReturnsStructuredRefusal()
    {
        lock (ProcessStateLock)
        {
            using var tempDirectory = new TemporaryDirectory();
            tempDirectory.CreateDirectory(Path.Combine("repo", ".intent-cli"));
            tempDirectory.CreateFile(Path.Combine("repo", ".intent-cli", "config.toml"), "[project\nmalformed");
            var workingDirectory = tempDirectory.CreateDirectory(Path.Combine("repo", "src", "feature"));
            using var consoleScope = new ConsoleScope();
            using var currentDirectoryScope = new CurrentDirectoryScope(workingDirectory);

            var exitCode = Program.Main(
                ["unit", "status", "--execution-unit", "G855", "--format", "json"]);

            Assert.Equal(1, exitCode);
            using var report = JsonDocument.Parse(consoleScope.Out.ToString());
            Assert.Equal("host-config-unreadable", report.RootElement.GetProperty("applicability").GetProperty("cause").GetString());
            Assert.Equal("read-failure", report.RootElement.GetProperty("applicability").GetProperty("unavailable_class").GetString());
            Assert.Equal("not-observed", report.RootElement.GetProperty("observation").GetProperty("github_snapshot").GetProperty("state").GetString());
            var facts = report.RootElement.GetProperty("steps").EnumerateArray()
                .SelectMany(step => step.GetProperty("subchecks").EnumerateArray()).ToArray();
            Assert.Equal(26, facts.Length);
            Assert.All(facts.Where(fact => fact.GetProperty("repair_commands").GetArrayLength() == 0), fact =>
                Assert.False(string.IsNullOrWhiteSpace(fact.GetProperty("repair_unavailable_reason").GetString())));
        }
    }

    [Fact]
    public void Main_UnitStatusUnreadableHostConfigPermissionReturnsStructuredRefusal()
    {
        lock (ProcessStateLock)
        {
            if (OperatingSystem.IsWindows() || string.Equals(Environment.UserName, "root", StringComparison.OrdinalIgnoreCase))
                throw Xunit.Sdk.SkipException.ForSkip("Unix permission enforcement is unavailable under this test identity.");

            using var tempDirectory = new TemporaryDirectory();
            tempDirectory.CreateDirectory(Path.Combine("repo", ".intent-cli"));
            var configPath = tempDirectory.CreateFile(Path.Combine("repo", ".intent-cli", "config.toml"), "default_domain = \"intent-cli\"\n");
            var originalMode = File.GetUnixFileMode(configPath);
            File.SetUnixFileMode(configPath, UnixFileMode.None);
            try
            {
                if (Record.Exception(() => File.ReadAllText(configPath)) is not UnauthorizedAccessException)
                    throw Xunit.Sdk.SkipException.ForSkip("The operating system did not reject a direct read of the mode-000 config fixture.");

                var workingDirectory = tempDirectory.CreateDirectory(Path.Combine("repo", "src", "feature"));
                using var consoleScope = new ConsoleScope();
                using var currentDirectoryScope = new CurrentDirectoryScope(workingDirectory);

                var exitCode = Program.Main(["unit", "status", "--execution-unit", "G855", "--format", "json"]);

                Assert.Equal(1, exitCode);
                using var report = JsonDocument.Parse(consoleScope.Out.ToString());
                Assert.Equal("host-config-unreadable", report.RootElement.GetProperty("applicability").GetProperty("cause").GetString());
                Assert.Equal("read-failure", report.RootElement.GetProperty("applicability").GetProperty("unavailable_class").GetString());
                Assert.Equal("not-observed", report.RootElement.GetProperty("observation").GetProperty("github_snapshot").GetProperty("state").GetString());
            }
            finally
            {
                File.SetUnixFileMode(configPath, originalMode);
            }
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
        private readonly string rootPath;

        public TemporaryDirectory(string? parent = null)
        {
            if (parent is null)
            {
                rootPath = Directory.CreateTempSubdirectory("intent-cli-program-tests-").FullName;
            }
            else
            {
                rootPath = Path.Combine(parent, "intent-cli-program-tests-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(rootPath);
            }
        }

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
