using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

public sealed class AutomationDurableStatePreflightCommandTests : IDisposable
{
    public AutomationDurableStatePreflightCommandTests()
    {
        AutomationDurableStatePreflightCommand.ProbeFactory = null;
    }

    public void Dispose()
    {
        AutomationDurableStatePreflightCommand.ProbeFactory = null;
    }

    [Fact]
    public void Execute_VerifiedCommitReady_ReturnsExitZero_AndRecommendedCommitMessage()
    {
        AutomationDurableStatePreflightCommand.ProbeFactory = _ => new DurableStatePreflightProbe
        {
            DirtyPaths = new[]
            {
                new DurableStateDirtyPath
                {
                    Path = ".intent-cli/queue-state.json",
                    IsDeleted = false,
                    QueueStateDelta = new QueueStateForwardDeltaResult
                    {
                        Classification = QueueStateForwardDeltaAnalyzer.ClassificationForwardOnly,
                        Summary = "added linked_pr=`https://github.com/o/r/pull/551` on `SKS-G215`",
                        Changes = new[]
                        {
                            new QueueStateForwardChange
                            {
                                ExecutionUnit = "SKS-G215",
                                Kind = QueueStateForwardChangeKind.AddedLinkedPr,
                                LinkedPrUrl = "https://github.com/o/r/pull/551",
                            },
                        },
                    },
                },
            },
        };

        using var workspace = new DurableStateWorkspace();
        using var writer = new StringWriter();

        var exitCode = AutomationDurableStatePreflightCommand.Execute(
            workspace.Context,
            new[] { "--format", "json" },
            writer);

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.Equal(
            DurableStatePreflightAnalyzer.ClassificationVerifiedCommitReady,
            doc.RootElement.GetProperty("classification").GetString());
        var commitMessage = doc.RootElement.GetProperty("recommended_commit_message").GetString();
        Assert.NotNull(commitMessage);
        Assert.Contains("G312", commitMessage!, StringComparison.Ordinal);
        Assert.Contains(".intent-cli/queue-state.json", commitMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_NeedsOperatorReview_ReturnsExitOne()
    {
        AutomationDurableStatePreflightCommand.ProbeFactory = _ => new DurableStatePreflightProbe
        {
            DirtyPaths = new[]
            {
                new DurableStateDirtyPath
                {
                    Path = ".intent-cli/queue-state.json",
                    IsDeleted = false,
                    QueueStateDelta = new QueueStateForwardDeltaResult
                    {
                        Classification = QueueStateForwardDeltaAnalyzer.ClassificationNeedsOperatorReview,
                        Summary = "title changed",
                        Changes = Array.Empty<QueueStateForwardChange>(),
                    },
                },
            },
        };

        using var workspace = new DurableStateWorkspace();
        using var writer = new StringWriter();

        var exitCode = AutomationDurableStatePreflightCommand.Execute(
            workspace.Context,
            new[] { "--format", "json" },
            writer);

        Assert.Equal(1, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.Equal(
            DurableStatePreflightAnalyzer.ClassificationNeedsOperatorReview,
            doc.RootElement.GetProperty("classification").GetString());
    }

    [Fact]
    public void Execute_UnsafeDurableState_ReturnsExitOne()
    {
        AutomationDurableStatePreflightCommand.ProbeFactory = _ => new DurableStatePreflightProbe
        {
            DirtyPaths = new[]
            {
                new DurableStateDirtyPath
                {
                    Path = "intents/intent-cli/intent-tree/00-map.md",
                    IsDeleted = false,
                },
            },
        };

        using var workspace = new DurableStateWorkspace();
        using var writer = new StringWriter();

        var exitCode = AutomationDurableStatePreflightCommand.Execute(
            workspace.Context,
            new[] { "--format", "json" },
            writer);

        Assert.Equal(1, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.Equal(
            DurableStatePreflightAnalyzer.ClassificationUnsafe,
            doc.RootElement.GetProperty("classification").GetString());
    }

    [Fact]
    public void Execute_MarkdownDefault_RendersHeaderAndSections()
    {
        AutomationDurableStatePreflightCommand.ProbeFactory = _ => new DurableStatePreflightProbe
        {
            DirtyPaths = new[]
            {
                new DurableStateDirtyPath
                {
                    Path = ".intent-cli/runs.jsonl",
                    IsDeleted = false,
                    RunsJsonlDelta = new RunsJsonlAppendOnlyResult
                    {
                        Classification = RunsJsonlAppendOnlyAnalyzer.ClassificationAppendOnly,
                        Summary = "runs.jsonl is append-only with 2 new event(s).",
                        AppendedEventCount = 2,
                    },
                },
            },
        };

        using var workspace = new DurableStateWorkspace();
        using var writer = new StringWriter();

        var exitCode = AutomationDurableStatePreflightCommand.Execute(
            workspace.Context,
            Array.Empty<string>(),
            writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("# automation durable-state-preflight (G312)", output, StringComparison.Ordinal);
        Assert.Contains("verified-commit-ready", output, StringComparison.Ordinal);
        Assert.Contains("Recommended commit message", output, StringComparison.Ordinal);
        Assert.Contains("```", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_RejectsUnknownArgument()
    {
        using var workspace = new DurableStateWorkspace();
        using var writer = new StringWriter();

        var exitCode = AutomationDurableStatePreflightCommand.Execute(
            workspace.Context,
            new[] { "--unknown" },
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("Unknown argument", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_AcceptsDomainAndRepoFlagsButIgnoresThem()
    {
        // Host-loop guidance passes --domain / --repo / --target-repo
        // for parity with other automation commands; this command must
        // accept them rather than error out. G361 makes --domain and
        // --target-repo functional for the prepared-packet probe; this
        // test only confirms they parse without error when there are no
        // dirty paths.
        AutomationDurableStatePreflightCommand.ProbeFactory = _ => new DurableStatePreflightProbe
        {
            DirtyPaths = Array.Empty<DurableStateDirtyPath>(),
        };

        using var workspace = new DurableStateWorkspace();
        using var writer = new StringWriter();

        var exitCode = AutomationDurableStatePreflightCommand.Execute(
            workspace.Context,
            new[]
            {
                "--domain", "intent-cli",
                "--repo", "J-Tech-Japan/intent-system",
                "--target-repo", "J-Tech-Japan/intent-system",
                "--format", "json",
            },
            writer);

        // Empty bundle classifies as needs-operator-review per the analyzer.
        Assert.Equal(1, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.Equal(
            DurableStatePreflightAnalyzer.ClassificationNeedsOperatorReview,
            doc.RootElement.GetProperty("classification").GetString());
    }

    [Fact]
    public void Execute_PreparedPacketDirectoryWithFourCanonicalFiles_ReturnsVerifiedCommitReady()
    {
        // G361 AC1: a complete prepared packet directory under
        // .intent-cli/issues/<unit>/ — picked up by the real probe
        // (git status + disk read + bindings.md regex) — is classified
        // verified-commit-ready end-to-end. This exercises the new
        // CaptureProbe lane that groups dirty canonical files by EU.

        AutomationDurableStatePreflightCommand.ProbeFactory = null;

        using var workspace = new DurableStateGitWorkspace();
        workspace.WritePreparedPacket("Z4R-G3", targetRepo: "J-Tech-Japan/intent-system");
        workspace.WriteBindings("intent-cli", executionUnitRegex: "^Z4R-G[0-9]+$");

        using var writer = new StringWriter();
        var exitCode = AutomationDurableStatePreflightCommand.Execute(
            workspace.Context,
            new[]
            {
                "--domain", "intent-cli",
                "--target-repo", "J-Tech-Japan/intent-system",
                "--format", "json",
            },
            writer);

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.Equal(
            DurableStatePreflightAnalyzer.ClassificationVerifiedCommitReady,
            doc.RootElement.GetProperty("classification").GetString());
        var verified = doc.RootElement.GetProperty("verified_paths");
        Assert.Equal(4, verified.GetArrayLength());
    }

    [Fact]
    public void Execute_PreparedPacketWrongDomain_ReturnsUnsafe()
    {
        // G361 AC3: a SKS-G<N> packet directory when the active domain
        // regex targets ^Z4R-G[0-9]+$ surfaces as unsafe; the operator
        // can see the cross-domain mismatch reason in the unsafe lane.

        AutomationDurableStatePreflightCommand.ProbeFactory = null;

        using var workspace = new DurableStateGitWorkspace();
        workspace.WritePreparedPacket("SKS-G42", targetRepo: "J-Tech-Japan/intent-system");
        workspace.WriteBindings("intent-cli", executionUnitRegex: "^Z4R-G[0-9]+$");

        using var writer = new StringWriter();
        var exitCode = AutomationDurableStatePreflightCommand.Execute(
            workspace.Context,
            new[]
            {
                "--domain", "intent-cli",
                "--target-repo", "J-Tech-Japan/intent-system",
                "--format", "json",
            },
            writer);

        Assert.Equal(1, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.Equal(
            DurableStatePreflightAnalyzer.ClassificationUnsafe,
            doc.RootElement.GetProperty("classification").GetString());
    }

    [Fact]
    public void Execute_PreparedPacketWithoutDomainFlag_ReturnsVerifiedCommitReady_OnConfiguredHost()
    {
        // PR #824 review repair #4: the legacy documented host-loop
        // path is `automation durable-state-preflight --format json`
        // (no --domain). On a host whose CliContext.Config.Project.Domain
        // is populated (the normal case), the command MUST NOT
        // silently treat that as an opt-in to fail-closed scoping.
        // Otherwise a complete prepared packet would be blocked on the
        // legacy path because the workspace has no bindings.md for
        // the configured domain.

        AutomationDurableStatePreflightCommand.ProbeFactory = null;

        using var workspace = new DurableStateGitWorkspace();
        // Packet directory present, but NO bindings.md is seeded —
        // legacy path must not require one.
        workspace.WritePreparedPacket("AnyExecutionUnit", targetRepo: "J-Tech-Japan/intent-system");

        using var writer = new StringWriter();
        var exitCode = AutomationDurableStatePreflightCommand.Execute(
            workspace.Context,
            new[] { "--format", "json" }, // No --domain — legacy path.
            writer);

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.Equal(
            DurableStatePreflightAnalyzer.ClassificationVerifiedCommitReady,
            doc.RootElement.GetProperty("classification").GetString());
    }

    [Fact]
    public void Execute_PreparedPacket_ParentBindingsAreAuthoritativeOverChildStaleBindings()
    {
        // PR #824 review repair #5: when a parent intent repo root
        // is configured, the PARENT bindings.md must be authoritative.
        // A stale child workspace bindings.md (with a wrong regex)
        // must NOT override the parent's authoritative regex.
        //
        // Setup: packet `Z4R-G3` would match a parent regex
        // `^Z4R-G[0-9]+$` but does NOT match a stale child regex
        // `^G[0-9]+$`. With the bug, the child's regex won (Z4R-G3
        // misclassified as wrong-domain unsafe). With the fix, the
        // parent regex wins (Z4R-G3 is commit-ready).

        AutomationDurableStatePreflightCommand.ProbeFactory = null;

        using var childWorkspace = new DurableStateGitWorkspace();
        var parentRoot = Directory.CreateTempSubdirectory("durable-state-parent-").FullName;
        try
        {
            // Parent (authoritative) bindings.
            var parentBindingsDir = Path.Combine(parentRoot, "intents", "intent-cli", "automation");
            Directory.CreateDirectory(parentBindingsDir);
            File.WriteAllText(
                Path.Combine(parentBindingsDir, "bindings.md"),
                "---\nexecution_unit_regex: '^Z4R-G[0-9]+$'\n---\n");

            // Stale child bindings: would reject Z4R-G3.
            childWorkspace.WriteBindings("intent-cli", executionUnitRegex: "^G[0-9]+$");

            childWorkspace.WritePreparedPacket(
                "Z4R-G3", targetRepo: "J-Tech-Japan/intent-system");

            // ProjectConfig is init-only, so build a child context
            // with the parent root configured rather than mutating
            // the workspace's context.
            var contextWithParentRoot = new CliContext
            {
                RepoRoot = childWorkspace.Context.RepoRoot,
                Config = new CliConfig
                {
                    Project = new ProjectConfig
                    {
                        Domain = childWorkspace.Context.Config.Project.Domain,
                        ArtifactRoot = childWorkspace.Context.Config.Project.ArtifactRoot,
                        WorktreeRoot = childWorkspace.Context.Config.Project.WorktreeRoot,
                        ParentIntentRepoRoot = parentRoot,
                    },
                },
            };

            using var writer = new StringWriter();
            var exitCode = AutomationDurableStatePreflightCommand.Execute(
                contextWithParentRoot,
                new[]
                {
                    "--domain", "intent-cli",
                    "--target-repo", "J-Tech-Japan/intent-system",
                    "--format", "json",
                },
                writer);

            Assert.Equal(0, exitCode);
            using var doc = JsonDocument.Parse(writer.ToString());
            Assert.Equal(
                DurableStatePreflightAnalyzer.ClassificationVerifiedCommitReady,
                doc.RootElement.GetProperty("classification").GetString());
        }
        finally
        {
            if (Directory.Exists(parentRoot)) Directory.Delete(parentRoot, recursive: true);
        }
    }

    private sealed class DurableStateWorkspace : IDisposable
    {
        public DurableStateWorkspace()
        {
            RepoRoot = Directory.CreateTempSubdirectory("durable-state-preflight-tests-").FullName;
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

    /// <summary>
    /// G361: end-to-end workspace that initializes a real git repo with
    /// an initial commit, so the real <c>CaptureProbe</c> (which calls
    /// <c>git status --porcelain</c>) sees the prepared-packet files as
    /// untracked additions.
    /// </summary>
    private sealed class DurableStateGitWorkspace : IDisposable
    {
        public DurableStateGitWorkspace()
        {
            RepoRoot = Directory.CreateTempSubdirectory("durable-state-preflight-git-").FullName;
            RunGit("init -q");
            RunGit("config user.email test@example.com");
            RunGit("config user.name test");
            File.WriteAllText(Path.Combine(RepoRoot, "README.md"), "# seed\n");
            RunGit("add README.md");
            RunGit("commit -q -m seed");
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
                    },
                },
            };
        }

        public string RepoRoot { get; }
        public CliContext Context { get; }

        public void WritePreparedPacket(string executionUnit, string targetRepo)
        {
            var dir = Path.Combine(RepoRoot, ".intent-cli", "issues", executionUnit);
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "packet.yaml"),
                $"implementation_issue_packet:\n  source_execution_unit: {executionUnit}\n  issue_title: Demo\n  target_repo: {targetRepo}\n");
            File.WriteAllText(Path.Combine(dir, "implementation.md"), "# impl\n");
            File.WriteAllText(Path.Combine(dir, "review-context.md"), "# review\n");
            File.WriteAllText(Path.Combine(dir, "github-body.md"),
                "# Title\n## Goal\nx\n## Why This Slice Exists Now\nx\n## Current Observed State\nx\n## Accepted Baseline You May Assume\nx\n## Target Repo / Path / Part\nx\n## In Scope\nx\n## Out Of Scope\nx\n## Acceptance Criteria\nx\n## Verification\nx\n## Related Links\nx\n");
        }

        public void WriteBindings(string domain, string executionUnitRegex)
        {
            var dir = Path.Combine(RepoRoot, "intents", domain, "automation");
            Directory.CreateDirectory(dir);
            var relativePath = $"intents/{domain}/automation/bindings.md";
            File.WriteAllText(Path.Combine(dir, "bindings.md"),
                $"---\nexecution_unit_regex: '{executionUnitRegex}'\n---\n");
            // Commit bindings immediately so it does not appear in
            // `git status` as an additional dirty path that would
            // otherwise downgrade the verdict to unsafe (intents/** is
            // always-unsafe). The prepared-packet probe still resolves
            // the regex from the committed working-tree copy.
            RunGit($"add {relativePath}");
            RunGit("commit -q -m bindings");
        }

        public void Dispose()
        {
            if (Directory.Exists(RepoRoot)) Directory.Delete(RepoRoot, recursive: true);
        }

        private void RunGit(string arguments)
        {
            using var process = new System.Diagnostics.Process();
            process.StartInfo.FileName = "git";
            process.StartInfo.Arguments = arguments;
            process.StartInfo.WorkingDirectory = RepoRoot;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardError = true;
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.CreateNoWindow = true;
            process.Start();
            process.StandardOutput.ReadToEnd();
            process.StandardError.ReadToEnd();
            process.WaitForExit();
        }
    }
}
