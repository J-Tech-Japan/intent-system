using System.Diagnostics;
using System.Text;
using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

[Collection("WorkerNextActionSharedState")]
public sealed class ClaimCommandG779Tests : IDisposable
{
    public ClaimCommandG779Tests()
    {
        WorkerClaimCommand.MutatorFactory = null;
        WorkerClaimCommand.IssueLookupFactory = null;
        WorkerClaimCommand.NestedProviderLauncher = null;
    }

    public void Dispose()
    {
        WorkerClaimCommand.MutatorFactory = null;
        WorkerClaimCommand.IssueLookupFactory = null;
        WorkerClaimCommand.NestedProviderLauncher = null;
    }

    [Fact]
    public void RejectedPushWithoutRemoteAdvance_ReportsPushRejectedWithoutRetry_AndRendersDiagnostics_G779()
    {
        using var repos = new ClaimRepositories();
        repos.RejectEveryPushWithoutAdvancingTarget();
        const string scope = "execution-unit:G779-rejected";
        var baseCommit = repos.ReadRef("main");

        using var json = new StringWriter();
        var jsonExit = ClaimCommand.ExecuteAcquire(
            Context(repos.FirstClone),
            [
                "--scope", scope,
                "--actor", "alice",
                "--team", "implementation",
                "--max-attempts", "5",
                "--write",
                "--format", "json",
            ],
            json);

        Assert.Equal(1, jsonExit);
        using (var emitted = JsonDocument.Parse(json.ToString()))
        {
            var result = emitted.RootElement;
            Assert.Equal("push-rejected", result.GetProperty("status").GetString());
            Assert.False(result.GetProperty("push_succeeded").GetBoolean());
            Assert.Equal(1, result.GetProperty("attempts").GetInt32());
            Assert.Equal("refs/heads/main", result.GetProperty("target_ref").GetString());
            Assert.Equal(baseCommit, result.GetProperty("base_commit").GetString());
            Assert.Equal(baseCommit, result.GetProperty("remote_head").GetString());
            Assert.False(result.GetProperty("remote_advanced").GetBoolean());
            Assert.Contains(
                "G779 test: protected default branch",
                result.GetProperty("git_push_error").GetString(),
                StringComparison.Ordinal);
        }

        Assert.Equal(1, repos.HookInvocations);
        Assert.Equal(baseCommit, repos.ReadRef("main"));
        var inspection = repos.CloneForInspection();
        Assert.False(File.Exists(Path.Combine(inspection, ClaimCommand.ClaimPath(scope))));

        using var markdown = new StringWriter();
        var markdownExit = ClaimCommand.ExecuteAcquire(
            Context(repos.FirstClone),
            [
                "--scope", scope,
                "--actor", "alice",
                "--team", "implementation",
                "--max-attempts", "5",
                "--write",
                "--format", "markdown",
            ],
            markdown);

        Assert.Equal(1, markdownExit);
        Assert.Contains($"- base_commit: `{baseCommit}`", markdown.ToString(), StringComparison.Ordinal);
        Assert.Contains("- remote_advanced: false", markdown.ToString(), StringComparison.Ordinal);
        Assert.Contains(
            "- git_push_error: remote: G779 test: protected default branch",
            markdown.ToString(),
            StringComparison.Ordinal);
        Assert.Equal(2, repos.HookInvocations);
    }

    [Fact]
    public void RejectedPushWithOneRemoteAdvance_ReappliesAndAcquiresOnSecondAttempt_G779()
    {
        using var repos = new ClaimRepositories();
        repos.AdvanceTargetThenRejectOnlyTheFirstPush();
        const string scope = "execution-unit:G779-advance-once";
        var before = repos.ReadRef("main");

        var result = ClaimCommand.RunTransaction(
            repos.FirstClone,
            Request(scope, maxAttempts: 5));

        Assert.Equal("acquired", result.Status);
        Assert.True(result.PushSucceeded);
        Assert.Equal(2, result.Attempts);
        Assert.Equal("refs/heads/main", result.TargetRef);
        Assert.Equal(2, repos.HookInvocations);
        Assert.NotEqual(before, repos.ReadRef("main"));
        var inspection = repos.CloneForInspection();
        Assert.True(File.Exists(Path.Combine(inspection, ClaimCommand.ClaimPath(scope))));
    }

    [Fact]
    public void RejectedPushWithRemoteAdvanceOnEveryAttempt_ReportsTruthfulRetryExhausted_G779()
    {
        using var repos = new ClaimRepositories();
        repos.AdvanceTargetThenRejectEveryPush();
        const string scope = "execution-unit:G779-retry-exhausted";

        var result = ClaimCommand.RunTransaction(
            repos.FirstClone,
            Request(scope, maxAttempts: 3));

        Assert.Equal("retry-exhausted", result.Status);
        Assert.False(result.PushSucceeded);
        Assert.Equal(3, result.Attempts);
        Assert.Equal("refs/heads/main", result.TargetRef);
        Assert.True(result.RemoteAdvanced);
        Assert.NotNull(result.BaseCommit);
        Assert.NotNull(result.RemoteHead);
        Assert.NotEqual(result.BaseCommit, result.RemoteHead);
        Assert.Equal(repos.ReadRef("main"), result.RemoteHead);
        Assert.Contains("G779 test: advanced default branch", result.GitPushError, StringComparison.Ordinal);
        Assert.Contains("refs/heads/main", result.Detail, StringComparison.Ordinal);
        Assert.Contains("unrelated remote advance", result.Detail, StringComparison.Ordinal);
        Assert.Equal(3, repos.HookInvocations);
    }

    [Fact]
    public void MetadataBranchHistoryOnly_IsAdoptedStoreForVerifyTriple_G779()
    {
        const string scope = "execution-unit:G779-history-only";
        using var historyOnly = new ClaimMetadataRepositories(MetadataFixture.HistoryOnly);
        using var absentEverywhere = new ClaimMetadataRepositories(MetadataFixture.AbsentEverywhere);
        using var activeMetadataOnly = new ClaimMetadataRepositories(MetadataFixture.ActiveOnly);

        var historyVerification = ClaimOwnershipVerifier.Verify(
            historyOnly.FirstClone,
            scope,
            "implementation");
        Assert.False(historyVerification.Passed);
        Assert.Equal(ClaimOwnershipVerification.StatusUnheld, historyVerification.Status);
        Assert.True(historyVerification.StoreConfigured);
        Assert.Contains("intent-metadata", historyVerification.Detail, StringComparison.Ordinal);
        Assert.Contains("no active record exists", historyVerification.Detail, StringComparison.Ordinal);

        var absentVerification = ClaimOwnershipVerifier.Verify(
            absentEverywhere.FirstClone,
            scope,
            "implementation");
        Assert.True(absentVerification.Passed);
        Assert.Equal(ClaimOwnershipVerification.StatusNotConfigured, absentVerification.Status);
        Assert.False(absentVerification.StoreConfigured);

        var activeVerification = ClaimOwnershipVerifier.Verify(
            activeMetadataOnly.FirstClone,
            scope,
            "implementation");
        Assert.False(activeVerification.Passed);
        Assert.Equal(ClaimOwnershipVerification.StatusMetadataBranchOnly, activeVerification.Status);
        Assert.True(activeVerification.StoreConfigured);

        var historyOutput = ExecuteVerification(historyOnly.FirstClone, scope, out var historyExit);
        var absentOutput = ExecuteVerification(absentEverywhere.FirstClone, scope, out var absentExit);
        var activeOutput = ExecuteVerification(activeMetadataOnly.FirstClone, scope, out var activeExit);

        Assert.Equal(1, historyExit);
        Assert.Equal(0, absentExit);
        Assert.Equal(1, activeExit);
        using var historyJson = JsonDocument.Parse(historyOutput);
        using var absentJson = JsonDocument.Parse(absentOutput);
        using var activeJson = JsonDocument.Parse(activeOutput);
        Assert.Equal("unheld", historyJson.RootElement.GetProperty("status").GetString());
        Assert.False(historyJson.RootElement.GetProperty("passed").GetBoolean());
        Assert.True(historyJson.RootElement.GetProperty("store_configured").GetBoolean());
        Assert.Equal("not-configured", absentJson.RootElement.GetProperty("status").GetString());
        Assert.True(absentJson.RootElement.GetProperty("passed").GetBoolean());
        Assert.False(absentJson.RootElement.GetProperty("store_configured").GetBoolean());
        Assert.Equal("metadata-branch-only", activeJson.RootElement.GetProperty("status").GetString());
        Assert.False(activeJson.RootElement.GetProperty("passed").GetBoolean());
        Assert.True(activeJson.RootElement.GetProperty("store_configured").GetBoolean());
    }

    [Fact]
    public void WorkerClaimHistoryOnlyStoreProbe_RefusesInsteadOfBypassing_G779()
    {
        using var repos = new ClaimMetadataRepositories(MetadataFixture.HistoryOnly);
        var mutator = new FakeLabelMutator(["intent-target"]);
        WorkerClaimCommand.MutatorFactory = () => mutator;
        WorkerClaimCommand.IssueLookupFactory = () => new FakeIssueLookup("claim history fixture");

        using var writer = new StringWriter();
        var exitCode = WorkerClaimCommand.Execute(
            Context(repos.FirstClone),
            [
                "--repo", "J-Tech-Japan/intent-system",
                "--kind", "issue",
                "--number", "1779",
                "--github-only",
                "--write",
                "--format", "json",
            ],
            writer);

        Assert.Equal(2, exitCode);
        var result = JsonSerializer.Deserialize<WorkerClaimResult>(writer.ToString())!;
        Assert.False(result.Proceed);
        Assert.False(result.Applied);
        Assert.Empty(mutator.Transitions);
        Assert.Contains(
            result.Errors,
            error => error.Contains("could not resolve a leading execution-unit token", StringComparison.Ordinal));
    }

    private static ClaimRequest Request(string scope, int maxAttempts) =>
        new(
            ClaimOperation.Acquire,
            scope,
            "alice",
            "implementation",
            null,
            null,
            true,
            "json",
            maxAttempts);

    private static string ExecuteVerification(string root, string scope, out int exitCode)
    {
        using var writer = new StringWriter();
        exitCode = ClaimVerificationCommand.Execute(
            Context(root),
            ["--scope", scope, "--team", "implementation", "--format", "json"],
            writer);
        return writer.ToString();
    }

    private static CliContext Context(string root) => new()
    {
        RepoRoot = root,
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

    private sealed class ClaimRepositories : IDisposable
    {
        private readonly TempDirectory temp = new("claim-g779-repos-");
        private readonly string hookCountPath;

        public ClaimRepositories()
        {
            Bare = Path.Combine(temp.Path, "origin.git");
            var seed = Path.Combine(temp.Path, "seed");
            FirstClone = Path.Combine(temp.Path, "first");
            Directory.CreateDirectory(Bare);
            Run(Bare, "git", "init", "--bare", "--quiet");
            Directory.CreateDirectory(seed);
            Run(seed, "git", "init", "--quiet", "--initial-branch=main");
            Run(seed, "git", "config", "user.name", "g779-fixture");
            Run(seed, "git", "config", "user.email", "g779-fixture@example.invalid");
            File.WriteAllText(Path.Combine(seed, "README.md"), "g779 fixture\n");
            Run(seed, "git", "add", "README.md");
            Run(seed, "git", "commit", "--quiet", "-m", "seed");
            Run(seed, "git", "remote", "add", "origin", Bare);
            Run(seed, "git", "push", "--quiet", "-u", "origin", "main");
            Run(Bare, "git", "symbolic-ref", "HEAD", "refs/heads/main");
            Run(temp.Path, "git", "clone", "--quiet", Bare, FirstClone);
            hookCountPath = Path.Combine(Bare, "hooks", "g779-push-count");
        }

        public string Bare { get; }
        public string FirstClone { get; }
        public int HookInvocations => !File.Exists(hookCountPath)
            ? 0
            : int.Parse(File.ReadAllText(hookCountPath).Trim(), System.Globalization.CultureInfo.InvariantCulture);

        public string ReadRef(string branch) =>
            Run(Bare, "git", "rev-parse", $"refs/heads/{branch}").Trim();

        public string CloneForInspection()
        {
            var path = Path.Combine(temp.Path, $"inspect-{Guid.NewGuid():N}");
            Run(temp.Path, "git", "clone", "--quiet", Bare, path);
            return path;
        }

        public void RejectEveryPushWithoutAdvancingTarget() => InstallPreReceiveHook("""
            count_file="$PWD/hooks/g779-push-count"
            count=0
            if [ -f "$count_file" ]; then
              count=$(cat "$count_file")
            fi
            count=$((count + 1))
            printf '%s\n' "$count" > "$count_file"
            printf '%s\n' 'G779 test: protected default branch' >&2
            exit 1
            """);

        public void AdvanceTargetThenRejectOnlyTheFirstPush() => InstallPreReceiveHook("""
            count_file="$PWD/hooks/g779-push-count"
            count=0
            if [ -f "$count_file" ]; then
              count=$(cat "$count_file")
            fi
            count=$((count + 1))
            printf '%s\n' "$count" > "$count_file"
            if [ "$count" -eq 1 ]; then
              unset GIT_QUARANTINE_PATH GIT_OBJECT_DIRECTORY GIT_ALTERNATE_OBJECT_DIRECTORIES
              old=$(git rev-parse refs/heads/main)
              tree=$(git rev-parse "$old^{tree}")
              advanced=$(printf '%s\n' 'G779 unrelated advance' | GIT_AUTHOR_NAME=g779 GIT_AUTHOR_EMAIL=g779@example.invalid GIT_COMMITTER_NAME=g779 GIT_COMMITTER_EMAIL=g779@example.invalid git commit-tree "$tree" -p "$old")
              git update-ref refs/heads/main "$advanced" "$old"
              printf '%s\n' 'G779 test: advanced default branch' >&2
              exit 1
            fi
            exit 0
            """);

        public void AdvanceTargetThenRejectEveryPush() => InstallPreReceiveHook("""
            count_file="$PWD/hooks/g779-push-count"
            count=0
            if [ -f "$count_file" ]; then
              count=$(cat "$count_file")
            fi
            count=$((count + 1))
            printf '%s\n' "$count" > "$count_file"
            unset GIT_QUARANTINE_PATH GIT_OBJECT_DIRECTORY GIT_ALTERNATE_OBJECT_DIRECTORIES
            old=$(git rev-parse refs/heads/main)
            tree=$(git rev-parse "$old^{tree}")
            advanced=$(printf '%s\n' 'G779 unrelated advance' | GIT_AUTHOR_NAME=g779 GIT_AUTHOR_EMAIL=g779@example.invalid GIT_COMMITTER_NAME=g779 GIT_COMMITTER_EMAIL=g779@example.invalid git commit-tree "$tree" -p "$old")
            git update-ref refs/heads/main "$advanced" "$old"
            printf '%s\n' 'G779 test: advanced default branch' >&2
            exit 1
            """);

        private void InstallPreReceiveHook(string body)
        {
            var hookPath = Path.Combine(Bare, "hooks", "pre-receive");
            File.WriteAllText(hookPath, "#!/bin/sh\nset -eu\n" + body + "\n", new UTF8Encoding(false));
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(
                    hookPath,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
        }

        public void Dispose() => temp.Dispose();
    }

    private enum MetadataFixture
    {
        HistoryOnly,
        ActiveOnly,
        AbsentEverywhere,
    }

    private sealed class ClaimMetadataRepositories : IDisposable
    {
        private readonly TempDirectory temp = new("claim-g779-metadata-");

        public ClaimMetadataRepositories(MetadataFixture fixture)
        {
            Bare = Path.Combine(temp.Path, "origin.git");
            var seed = Path.Combine(temp.Path, "seed");
            FirstClone = Path.Combine(temp.Path, "first");
            Directory.CreateDirectory(Bare);
            Run(Bare, "git", "init", "--bare", "--quiet");
            Directory.CreateDirectory(seed);
            Run(seed, "git", "init", "--quiet", "--initial-branch=main");
            Run(seed, "git", "config", "user.name", "g779-fixture");
            Run(seed, "git", "config", "user.email", "g779-fixture@example.invalid");
            File.WriteAllText(Path.Combine(seed, "README.md"), "g779 metadata fixture\n");
            Run(seed, "git", "add", "README.md");
            Run(seed, "git", "commit", "--quiet", "-m", "seed");
            Run(seed, "git", "remote", "add", "origin", Bare);
            Run(seed, "git", "push", "--quiet", "-u", "origin", "main");
            Run(Bare, "git", "symbolic-ref", "HEAD", "refs/heads/main");

            if (fixture is not MetadataFixture.AbsentEverywhere)
            {
                Run(seed, "git", "switch", "--quiet", "-c", "intent-metadata");
                if (fixture == MetadataFixture.HistoryOnly)
                {
                    var path = Path.Combine(
                        seed,
                        ".intent-cli",
                        "claims",
                        "history",
                        "g779-history",
                        "released.json");
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    File.WriteAllText(path, "{\"operation\":\"release\"}\n", new UTF8Encoding(false));
                }
                else
                {
                    var record = new ClaimRecord(
                        "1",
                        "execution-unit:G779-history-only",
                        "alice",
                        "implementation",
                        DateTimeOffset.Parse("2026-09-01T00:00:00Z"),
                        "base-commit");
                    var path = Path.Combine(
                        seed,
                        ClaimCommand.ClaimPath(record.Scope).Replace('/', Path.DirectorySeparatorChar));
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    File.WriteAllText(path, JsonSerializer.Serialize(record) + Environment.NewLine, new UTF8Encoding(false));
                }
                Run(seed, "git", "add", "--", ClaimCommand.ClaimsDirectory);
                Run(seed, "git", "commit", "--quiet", "-m", "metadata claim evidence");
                Run(seed, "git", "push", "--quiet", "-u", "origin", "intent-metadata");
            }

            Run(temp.Path, "git", "clone", "--quiet", Bare, FirstClone);
            if (fixture is not MetadataFixture.AbsentEverywhere)
            {
                var configDirectory = Path.Combine(FirstClone, ".intent-cli");
                Directory.CreateDirectory(configDirectory);
                File.WriteAllText(
                    Path.Combine(configDirectory, "config.toml"),
                    "[project]\n"
                    + "domain = \"intent-cli\"\n"
                    + "artifact_root = \".intent-cli\"\n"
                    + "metadata_source_branch = \"intent-metadata\"\n",
                    new UTF8Encoding(false));
            }
        }

        public string Bare { get; }
        public string FirstClone { get; }

        public void Dispose() => temp.Dispose();
    }

    private sealed class FakeLabelMutator(IReadOnlyList<string> labels) : IGitHubLabelMutator
    {
        public List<(IReadOnlyList<string> Add, IReadOnlyList<string> Remove)> Transitions { get; } = [];

        public IReadOnlyList<GitHubAutomationLabel> ReadLabels(string repo, string kind, int number) =>
            labels.Select(name => new GitHubAutomationLabel { Name = name }).ToArray();

        public void ApplyLabelTransitions(
            string repo,
            string kind,
            int number,
            IReadOnlyCollection<string> addLabels,
            IReadOnlyCollection<string> removeLabels) =>
            Transitions.Add((addLabels.ToArray(), removeLabels.ToArray()));

        public void ApplyReconcileTransitions(
            string repo,
            string kind,
            int number,
            IReadOnlyCollection<string> addLabels,
            IReadOnlyCollection<string> removeLabels) =>
            throw new NotSupportedException();
    }

    private sealed class FakeIssueLookup(string title) : IGitHubIssueLookup
    {
        public GitHubIssueLookupResult Lookup(string repo, int issueNumber) => new()
        {
            Number = issueNumber,
            State = "OPEN",
            Title = title,
            Body = string.Empty,
            Labels = Array.Empty<GitHubIssueLabel>(),
        };
    }

    private static string Run(string workdir, string fileName, params string[] arguments)
    {
        var info = new ProcessStartInfo(fileName)
        {
            WorkingDirectory = workdir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }
        using var process = Process.Start(info)!;
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, $"{fileName} {string.Join(' ', arguments)} failed: {error}");
        return output;
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory(string prefix) => Path = Directory.CreateTempSubdirectory(prefix).FullName;
        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
