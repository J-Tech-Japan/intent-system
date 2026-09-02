using System.Diagnostics;
using System.Text;
using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

[Collection("WorkerNextActionSharedState")]
public sealed class ClaimCommandG780Tests : IDisposable
{
    public ClaimCommandG780Tests()
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
    public void DeclaredTopology_AcquireVerifyProbeAndReverseStrandedMigrationUseMetadataWriteBranch_G780()
    {
        const string strandedScope = "execution-unit:G780-stranded";
        const string acquiredScope = "execution-unit:G780-acquired";
        using var repos = new TopologyClaimRepositories(
            mainRecords: [Record(strandedScope, "main-holder", "implementation")],
            metadataRecords: [Record("execution-unit:G780-metadata-baseline", "metadata-holder", "implementation")]);
        repos.ConfigureTopology(sameRepoTopology: true, metadataWriteBranch: "main-metadata");

        var mainBefore = repos.ReadRef("main");
        var metadataBefore = repos.ReadRef("main-metadata");
        var acquire = ClaimCommand.RunTransaction(
            repos.FirstClone,
            Request(acquiredScope));

        Assert.Equal("acquired", acquire.Status);
        Assert.True(acquire.PushSucceeded);
        Assert.Equal("refs/heads/main-metadata", acquire.TargetRef);
        Assert.Equal(mainBefore, repos.ReadRef("main"));
        Assert.NotEqual(metadataBefore, repos.ReadRef("main-metadata"));

        var verification = ClaimOwnershipVerifier.Verify(
            repos.FirstClone,
            acquiredScope,
            "implementation");
        Assert.True(verification.Passed);
        Assert.Equal(ClaimOwnershipVerification.StatusOwned, verification.Status);
        Assert.Equal("implementation", verification.Holder);

        var takeover = ClaimCommand.RunTransaction(
            repos.FirstClone,
            new ClaimRequest(
                ClaimOperation.Takeover,
                acquiredScope,
                "reviewer",
                "review",
                "handoff",
                "implementation",
                Write: true,
                Format: "json",
                MaxAttempts: ClaimCommand.DefaultMaxAttempts));
        Assert.Equal("taken-over", takeover.Status);
        Assert.Equal("refs/heads/main-metadata", takeover.TargetRef);
        Assert.Equal(mainBefore, repos.ReadRef("main"));

        var release = ClaimCommand.RunTransaction(
            repos.FirstClone,
            new ClaimRequest(
                ClaimOperation.Release,
                acquiredScope,
                "reviewer",
                "review",
                "completed",
                null,
                Write: true,
                Format: "json",
                MaxAttempts: ClaimCommand.DefaultMaxAttempts));
        Assert.Equal("released", release.Status);
        Assert.Equal("refs/heads/main-metadata", release.TargetRef);
        Assert.Equal(mainBefore, repos.ReadRef("main"));

        var probe = ClaimOwnershipVerifier.ProbeStore(repos.FirstClone);
        Assert.True(probe.Available);
        Assert.True(probe.StoreConfigured);

        using var reportOutput = new StringWriter();
        Assert.Equal(
            0,
            ClaimCommand.ExecuteStranded(
                Context(repos.FirstClone),
                ["--format", "json"],
                reportOutput));
        using var report = JsonDocument.Parse(reportOutput.ToString());
        Assert.Equal("stranded", report.RootElement.GetProperty("status").GetString());
        Assert.Equal("main", report.RootElement.GetProperty("metadata_branch").GetString());
        Assert.Equal("main-metadata", report.RootElement.GetProperty("canonical_branch").GetString());
        Assert.Equal("refs/remotes/origin/main", report.RootElement.GetProperty("metadata_ref").GetString());
        Assert.Equal("refs/remotes/origin/main-metadata", report.RootElement.GetProperty("canonical_ref").GetString());
        Assert.Collection(
            report.RootElement.GetProperty("items").EnumerateArray(),
            item => Assert.Equal(strandedScope, item.GetProperty("scope").GetString()));

        var metadataAfterAcquire = repos.ReadRef("main-metadata");
        using var dryRunOutput = new StringWriter();
        Assert.Equal(
            0,
            ClaimCommand.ExecuteStranded(
                Context(repos.FirstClone),
                MigrationArguments(write: false),
                dryRunOutput));
        using (var dryRun = JsonDocument.Parse(dryRunOutput.ToString()))
        {
            Assert.Equal("planned", dryRun.RootElement.GetProperty("status").GetString());
            Assert.Equal("refs/heads/main-metadata", dryRun.RootElement.GetProperty("target_ref").GetString());
            Assert.False(dryRun.RootElement.GetProperty("push_succeeded").GetBoolean());
        }
        Assert.Equal(mainBefore, repos.ReadRef("main"));
        Assert.Equal(metadataAfterAcquire, repos.ReadRef("main-metadata"));

        using var migrationOutput = new StringWriter();
        Assert.Equal(
            0,
            ClaimCommand.ExecuteStranded(
                Context(repos.FirstClone),
                MigrationArguments(write: true),
                migrationOutput));
        using (var migration = JsonDocument.Parse(migrationOutput.ToString()))
        {
            Assert.Equal("migrated", migration.RootElement.GetProperty("status").GetString());
            Assert.True(migration.RootElement.GetProperty("push_succeeded").GetBoolean());
            Assert.Equal("refs/heads/main-metadata", migration.RootElement.GetProperty("target_ref").GetString());
            Assert.False(string.IsNullOrWhiteSpace(migration.RootElement.GetProperty("commit").GetString()));
        }
        Assert.Equal(mainBefore, repos.ReadRef("main"));
        Assert.NotEqual(metadataAfterAcquire, repos.ReadRef("main-metadata"));
        Assert.Equal(
            repos.ReadFile("main", ClaimCommand.ClaimPath(strandedScope)),
            repos.ReadFile("main-metadata", ClaimCommand.ClaimPath(strandedScope)));

        using var secondReportOutput = new StringWriter();
        Assert.Equal(
            0,
            ClaimCommand.ExecuteStranded(
                Context(repos.FirstClone),
                ["--format", "json"],
                secondReportOutput));
        using var secondReport = JsonDocument.Parse(secondReportOutput.ToString());
        Assert.Equal("clean", secondReport.RootElement.GetProperty("status").GetString());
        Assert.Empty(secondReport.RootElement.GetProperty("items").EnumerateArray());
    }

    [Fact]
    public void DeclaredTopology_WriterVerifierWorkerAndStrandedReaderResolveTheSameTarget_G780()
    {
        const string scope = "execution-unit:G780";
        using var repos = new TopologyClaimRepositories(
            mainRecords: [Record(scope, "main-holder", "implementation")],
            metadataRecords: [Record(scope, "metadata-holder", "implementation")]);
        repos.ConfigureTopology(sameRepoTopology: true, metadataWriteBranch: "main-metadata");

        var writer = ClaimCommand.RunTransaction(repos.FirstClone, Request(scope));
        Assert.Equal("held", writer.Status);
        Assert.Equal("metadata-holder", writer.Holder);
        Assert.Equal("refs/heads/main-metadata", writer.TargetRef);

        var verifier = ClaimOwnershipVerifier.Verify(repos.FirstClone, scope, "implementation");
        Assert.True(verifier.Passed);
        Assert.Equal(ClaimOwnershipVerification.StatusOwned, verifier.Status);
        Assert.Equal("metadata-holder", verifier.Holder);

        var probe = ClaimOwnershipVerifier.ProbeStore(repos.FirstClone);
        Assert.True(probe.Available);
        Assert.True(probe.StoreConfigured);

        var mutator = new FakeLabelMutator(["intent-target"]);
        WorkerClaimCommand.MutatorFactory = () => mutator;
        WorkerClaimCommand.IssueLookupFactory = () => new FakeIssueLookup("G780 topology target");
        using var workerOutput = new StringWriter();
        var workerExit = WorkerClaimCommand.Execute(
            Context(repos.FirstClone),
            [
                "--repo", "J-Tech-Japan/intent-system",
                "--kind", "issue",
                "--number", "1780",
                "--github-only",
                "--write",
                "--format", "json",
            ],
            workerOutput);
        Assert.Equal(2, workerExit);
        var worker = JsonSerializer.Deserialize<WorkerClaimResult>(workerOutput.ToString())!;
        Assert.False(worker.Proceed);
        Assert.False(worker.Applied);
        Assert.Empty(mutator.Transitions);
        Assert.Contains(
            worker.Errors,
            error => error.Contains("metadata-holder", StringComparison.Ordinal));
        Assert.DoesNotContain(
            worker.Errors,
            error => error.Contains("main-holder", StringComparison.Ordinal));

        using var strandedOutput = new StringWriter();
        Assert.Equal(
            0,
            ClaimCommand.ExecuteStranded(
                Context(repos.FirstClone),
                ["--format", "json"],
                strandedOutput));
        using var stranded = JsonDocument.Parse(strandedOutput.ToString());
        Assert.Equal("conflict", stranded.RootElement.GetProperty("status").GetString());
        Assert.Equal("main", stranded.RootElement.GetProperty("metadata_branch").GetString());
        Assert.Equal("main-metadata", stranded.RootElement.GetProperty("canonical_branch").GetString());
        Assert.Contains(
            stranded.RootElement.GetProperty("warnings").EnumerateArray(),
            warning => warning.GetString()!.Contains(scope, StringComparison.Ordinal));
    }

    [Fact]
    public void DeclaredTopology_AbsentOrUnsafeMetadataWriteBranchFailsClosedWithoutChangingTips_G780()
    {
        using var repos = new TopologyClaimRepositories();
        var mainBefore = repos.ReadRef("main");
        var metadataBefore = repos.ReadRef("main-metadata");

        repos.ConfigureTopology(sameRepoTopology: true, metadataWriteBranch: "missing-metadata");
        using var absentOutput = new StringWriter();
        Assert.Equal(
            1,
            ClaimCommand.ExecuteAcquire(
                Context(repos.FirstClone, metadataWriteBranch: "missing-metadata"),
                AcquireArguments("execution-unit:G780-missing"),
                absentOutput));
        using (var absent = JsonDocument.Parse(absentOutput.ToString()))
        {
            Assert.Equal("error", absent.RootElement.GetProperty("status").GetString());
            var detail = absent.RootElement.GetProperty("detail").GetString()!;
            Assert.Contains("metadata_write_branch", detail, StringComparison.Ordinal);
            Assert.Contains("missing-metadata", detail, StringComparison.Ordinal);
            Assert.Contains("refusing to fall back", detail, StringComparison.Ordinal);
        }
        Assert.Equal(mainBefore, repos.ReadRef("main"));
        Assert.Equal(metadataBefore, repos.ReadRef("main-metadata"));

        var absentVerify = ClaimOwnershipVerifier.Verify(
            repos.FirstClone,
            "execution-unit:G780-missing",
            "implementation");
        Assert.False(absentVerify.Passed);
        Assert.Equal(ClaimOwnershipVerification.StatusCanonicalUnavailable, absentVerify.Status);
        Assert.Contains("missing-metadata", absentVerify.Detail, StringComparison.Ordinal);
        Assert.False(ClaimOwnershipVerifier.ProbeStore(repos.FirstClone).Available);
        using (var absentVerificationOutput = new StringWriter())
        {
            Assert.Equal(
                1,
                ClaimVerificationCommand.Execute(
                    Context(repos.FirstClone, metadataWriteBranch: "missing-metadata"),
                    ["--scope", "execution-unit:G780-missing", "--team", "implementation", "--format", "json"],
                    absentVerificationOutput));
            using var absentVerificationJson = JsonDocument.Parse(absentVerificationOutput.ToString());
            Assert.Equal(
                ClaimOwnershipVerification.StatusCanonicalUnavailable,
                absentVerificationJson.RootElement.GetProperty("status").GetString());
        }

        repos.ConfigureTopology(sameRepoTopology: true, metadataWriteBranch: "../unsafe");
        using var unsafeOutput = new StringWriter();
        Assert.Equal(
            1,
            ClaimCommand.ExecuteAcquire(
                Context(repos.FirstClone, metadataWriteBranch: "../unsafe"),
                AcquireArguments("execution-unit:G780-unsafe"),
                unsafeOutput));
        using (var unsafeResult = JsonDocument.Parse(unsafeOutput.ToString()))
        {
            Assert.Equal("error", unsafeResult.RootElement.GetProperty("status").GetString());
            var detail = unsafeResult.RootElement.GetProperty("detail").GetString()!;
            Assert.Contains("metadata_write_branch", detail, StringComparison.Ordinal);
            Assert.Contains("../unsafe", detail, StringComparison.Ordinal);
            Assert.Contains("refusing to fall back", detail, StringComparison.Ordinal);
        }
        Assert.Equal(mainBefore, repos.ReadRef("main"));
        Assert.Equal(metadataBefore, repos.ReadRef("main-metadata"));

        var unsafeVerify = ClaimOwnershipVerifier.Verify(
            repos.FirstClone,
            "execution-unit:G780-unsafe",
            "implementation");
        Assert.False(unsafeVerify.Passed);
        Assert.Equal(ClaimOwnershipVerification.StatusCanonicalUnavailable, unsafeVerify.Status);
        Assert.Contains("../unsafe", unsafeVerify.Detail, StringComparison.Ordinal);
        using (var unsafeVerificationOutput = new StringWriter())
        {
            Assert.Equal(
                1,
                ClaimVerificationCommand.Execute(
                    Context(repos.FirstClone, metadataWriteBranch: "../unsafe"),
                    ["--scope", "execution-unit:G780-unsafe", "--team", "implementation", "--format", "json"],
                    unsafeVerificationOutput));
            using var unsafeVerificationJson = JsonDocument.Parse(unsafeVerificationOutput.ToString());
            Assert.Equal(
                ClaimOwnershipVerification.StatusCanonicalUnavailable,
                unsafeVerificationJson.RootElement.GetProperty("status").GetString());
        }
    }

    [Fact]
    public void DeclaredTopology_PreservesG779PushRejectedFieldsOnlyForTheRemoteDefaultFallback_G780()
    {
        if (OperatingSystem.IsWindows()) return;

        using var repos = new TopologyClaimRepositories(metadataBranch: "intent-metadata");
        repos.ConfigureTopology(sameRepoTopology: true, metadataWriteBranch: "intent-metadata");
        repos.RejectOnlyMainPushes();

        var declared = ClaimCommand.RunTransaction(
            repos.FirstClone,
            Request("execution-unit:G780-unprotected-metadata"));
        Assert.Equal("acquired", declared.Status);
        Assert.True(declared.PushSucceeded);
        Assert.Equal("refs/heads/intent-metadata", declared.TargetRef);
        var declaredVerification = ClaimOwnershipVerifier.Verify(
            repos.FirstClone,
            "execution-unit:G780-unprotected-metadata",
            "implementation");
        Assert.True(declaredVerification.Passed);
        Assert.Equal(ClaimOwnershipVerification.StatusOwned, declaredVerification.Status);

        repos.ConfigureTopology(sameRepoTopology: false, metadataWriteBranch: "intent-metadata");
        using var rejectedOutput = new StringWriter();
        Assert.Equal(
            1,
            ClaimCommand.ExecuteAcquire(
                Context(repos.FirstClone, metadataWriteBranch: "intent-metadata", sameRepoTopology: false),
                AcquireArguments("execution-unit:G780-default-rejected"),
                rejectedOutput));
        using var rejected = JsonDocument.Parse(rejectedOutput.ToString());
        Assert.Equal("push-rejected", rejected.RootElement.GetProperty("status").GetString());
        Assert.Equal("refs/heads/main", rejected.RootElement.GetProperty("target_ref").GetString());
        Assert.False(rejected.RootElement.GetProperty("remote_advanced").GetBoolean());
        Assert.Contains(
            "G780 fixture: protected main",
            rejected.RootElement.GetProperty("git_push_error").GetString(),
            StringComparison.Ordinal);

        repos.RemoveTopologyConfig();
        var unset = ClaimCommand.ResolveRemoteDefaultBranch(repos.FirstClone);
        Assert.Equal("main", unset.Name);
        Assert.False(unset.UsesMetadataWriteBranch);
    }

    [Fact]
    public void Documentation_DescribesBothClaimTopologiesAndTheV030MinorContract_G780()
    {
        var root = RepoVersionPolicySource.RepoRoot();
        foreach (var language in new[] { "en", "ja" })
        {
            var reference = File.ReadAllText(Path.Combine(
                root,
                "docs",
                language,
                "09-developer-reference.md"));
            var notes = File.ReadAllText(Path.Combine(
                root,
                "docs",
                language,
                "release-notes-v0.30.0.md"));

            Assert.Contains("claim target topology (G747, G780)", reference, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("same_repo_topology", reference, StringComparison.Ordinal);
            Assert.Contains("metadata_write_branch", reference, StringComparison.Ordinal);
            Assert.Contains("fail closed", reference, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("stranded", reference, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("G780", notes, StringComparison.Ordinal);
            Assert.Contains("minor", notes, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("metadata_write_branch", notes, StringComparison.Ordinal);
        }
    }

    private static ClaimRequest Request(string scope) =>
        new(
            ClaimOperation.Acquire,
            scope,
            "implementation",
            "implementation",
            null,
            null,
            Write: true,
            Format: "json",
            MaxAttempts: ClaimCommand.DefaultMaxAttempts);

    private static string[] AcquireArguments(string scope) =>
    [
        "--scope", scope,
        "--actor", "implementation",
        "--team", "implementation",
        "--write",
        "--format", "json",
    ];

    private static string[] MigrationArguments(bool write) =>
    [
        "migrate",
        "--current-metadata-branch", "main",
        "--new-canonical-branch", "main-metadata",
        "--actor", "implementation",
        "--team", "implementation",
        "--confirm-migrate-stranded",
        write ? "--write" : "--dry-run",
        "--format", "json",
    ];

    private static ClaimRecord Record(string scope, string actor, string team) =>
        new(
            "1",
            scope,
            actor,
            team,
            DateTimeOffset.Parse("2026-09-02T00:00:00Z"),
            "g780-base");

    private static CliContext Context(
        string root,
        string metadataWriteBranch = "main-metadata",
        bool sameRepoTopology = true) =>
        new()
        {
            RepoRoot = root,
            Config = new CliConfig
            {
                Project = new ProjectConfig
                {
                    Domain = "intent-cli",
                    ArtifactRoot = ".intent-cli",
                    SameRepoTopology = sameRepoTopology,
                    MetadataWriteBranch = metadataWriteBranch,
                },
            },
        };

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

    private sealed class TopologyClaimRepositories : IDisposable
    {
        private readonly TempDirectory temp = new("claim-g780-repos-");

        public TopologyClaimRepositories(
            IReadOnlyList<ClaimRecord>? mainRecords = null,
            IReadOnlyList<ClaimRecord>? metadataRecords = null,
            string metadataBranch = "main-metadata")
        {
            Bare = Path.Combine(temp.Path, "origin.git");
            var seed = Path.Combine(temp.Path, "seed");
            FirstClone = Path.Combine(temp.Path, "first");
            Directory.CreateDirectory(Bare);
            Run(Bare, "git", "init", "--bare", "--quiet");
            Directory.CreateDirectory(seed);
            Run(seed, "git", "init", "--quiet", "--initial-branch=main");
            Run(seed, "git", "config", "user.name", "g780-fixture");
            Run(seed, "git", "config", "user.email", "g780-fixture@example.invalid");
            File.WriteAllText(Path.Combine(seed, "README.md"), "g780 fixture\n", new UTF8Encoding(false));
            Run(seed, "git", "add", "README.md");
            Run(seed, "git", "commit", "--quiet", "-m", "seed");
            Run(seed, "git", "remote", "add", "origin", Bare);
            Run(seed, "git", "branch", metadataBranch);

            WriteRecords(seed, mainRecords ?? []);
            CommitClaimsIfChanged(seed, "main claims");
            Run(seed, "git", "push", "--quiet", "-u", "origin", "main");

            Run(seed, "git", "switch", "--quiet", metadataBranch);
            WriteRecords(seed, metadataRecords ?? []);
            CommitClaimsIfChanged(seed, "metadata claims");
            Run(seed, "git", "push", "--quiet", "-u", "origin", metadataBranch);
            Run(Bare, "git", "symbolic-ref", "HEAD", "refs/heads/main");
            Run(temp.Path, "git", "clone", "--quiet", Bare, FirstClone);
        }

        public string Bare { get; }
        public string FirstClone { get; }

        public void ConfigureTopology(bool sameRepoTopology, string metadataWriteBranch)
        {
            var configDirectory = Path.Combine(FirstClone, ".intent-cli");
            Directory.CreateDirectory(configDirectory);
            File.WriteAllText(
                Path.Combine(configDirectory, "config.toml"),
                "[project]\n"
                + "domain = \"intent-cli\"\n"
                + "artifact_root = \".intent-cli\"\n"
                + $"same_repo_topology = {(sameRepoTopology ? "true" : "false")}\n"
                + $"metadata_write_branch = \"{metadataWriteBranch}\"\n",
                new UTF8Encoding(false));
        }

        public void RemoveTopologyConfig()
        {
            var configPath = Path.Combine(FirstClone, ".intent-cli", "config.toml");
            if (File.Exists(configPath)) File.Delete(configPath);
        }

        public void RejectOnlyMainPushes()
        {
            var hookPath = Path.Combine(Bare, "hooks", "pre-receive");
            File.WriteAllText(
                hookPath,
                "#!/bin/sh\n"
                + "set -eu\n"
                + "while read old new ref; do\n"
                + "  if [ \"$ref\" = \"refs/heads/main\" ]; then\n"
                + "    printf '%s\\n' 'G780 fixture: protected main' >&2\n"
                + "    exit 1\n"
                + "  fi\n"
                + "done\n"
                + "exit 0\n",
                new UTF8Encoding(false));
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(
                    hookPath,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
        }

        public string ReadRef(string branch) =>
            Run(Bare, "git", "rev-parse", $"refs/heads/{branch}").Trim();

        public string ReadFile(string branch, string relativePath) =>
            Run(Bare, "git", "show", $"refs/heads/{branch}:{relativePath}");

        private static void WriteRecords(string root, IEnumerable<ClaimRecord> records)
        {
            foreach (var record in records)
            {
                var path = Path.Combine(
                    root,
                    ClaimCommand.ClaimPath(record.Scope).Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(
                    path,
                    JsonSerializer.Serialize(record, new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine,
                    new UTF8Encoding(false));
            }
        }

        private static void CommitClaimsIfChanged(string root, string message)
        {
            if (!Directory.Exists(Path.Combine(root, ".intent-cli", "claims"))) return;
            Run(root, "git", "add", "--", ClaimCommand.ClaimsDirectory);
            if (RunExitCode(root, "git", "diff", "--cached", "--quiet") != 0)
            {
                Run(root, "git", "commit", "--quiet", "-m", message);
            }
        }

        private static int RunExitCode(string workdir, string fileName, params string[] arguments)
        {
            var info = new ProcessStartInfo(fileName)
            {
                WorkingDirectory = workdir,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            foreach (var argument in arguments) info.ArgumentList.Add(argument);
            using var process = Process.Start(info)!;
            process.WaitForExit();
            return process.ExitCode;
        }

        public void Dispose() => temp.Dispose();
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
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
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
            if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
        }
    }
}
