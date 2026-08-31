using System.Diagnostics;
using System.Text;
using System.Text.Json;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

public sealed class ClaimCommandG763Tests
{
    [Fact]
    public void StrandedReport_ListsEveryMetadataRecordAndCanonicalVerifyRemainsUnheld_G763()
    {
        var stranded = new[]
        {
            Record("execution-unit:G763-legacy-a", "alice", "implementation"),
            Record("execution-unit:G763-legacy-b", "bob", "review"),
        };
        using var repos = new ClaimRepositories(
            [Record("execution-unit:G763-canonical-baseline", "canonical", "implementation")], stranded);

        using var output = new StringWriter();
        var exit = ClaimCommand.ExecuteStranded(
            Context(repos.FirstClone), ["--format", "json"], output);

        Assert.Equal(0, exit);
        using var report = JsonDocument.Parse(output.ToString());
        Assert.Equal("stranded", report.RootElement.GetProperty("status").GetString());
        Assert.Equal("main-metadata", report.RootElement.GetProperty("metadata_branch").GetString());
        Assert.Equal("main", report.RootElement.GetProperty("canonical_branch").GetString());
        Assert.Equal("refs/remotes/origin/main-metadata", report.RootElement.GetProperty("metadata_ref").GetString());
        Assert.Equal("refs/remotes/origin/main", report.RootElement.GetProperty("canonical_ref").GetString());
        var scopes = report.RootElement.GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("scope").GetString()!)
            .ToArray();
        Assert.Equal(
            ["execution-unit:G763-legacy-a", "execution-unit:G763-legacy-b"],
            scopes);
        Assert.All(
            report.RootElement.GetProperty("items").EnumerateArray(),
            item =>
            {
                Assert.Equal("refs/remotes/origin/main-metadata", item.GetProperty("metadata_ref").GetString());
                Assert.Equal("refs/remotes/origin/main", item.GetProperty("canonical_ref").GetString());
            });

        var verification = ClaimOwnershipVerifier.Verify(
            repos.FirstClone, stranded[0].Scope, "implementation");
        Assert.False(verification.Passed);
        Assert.Equal(ClaimOwnershipVerification.StatusUnheld, verification.Status);
        Assert.Contains("unheld", verification.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void StrandedReport_NoMetadataConfigurationIsNotConfiguredAndMatchingBranchesAreClean_G763()
    {
        using var noConfiguration = new ClaimRepositories([], []);
        using var noConfigOutput = new StringWriter();
        Assert.Equal(
            0,
            ClaimCommand.ExecuteStranded(
                Context(noConfiguration.FirstClone, metadataBranch: string.Empty),
                ["--format", "json"],
                noConfigOutput));
        using var noConfig = JsonDocument.Parse(noConfigOutput.ToString());
        Assert.Equal("not-configured", noConfig.RootElement.GetProperty("status").GetString());

        var same = Record("execution-unit:G763-same", "alice", "implementation");
        using var matchingBranches = new ClaimRepositories([same], [same]);
        using var cleanOutput = new StringWriter();
        Assert.Equal(
            0,
            ClaimCommand.ExecuteStranded(
                Context(matchingBranches.FirstClone), ["--format", "json"], cleanOutput));
        using var clean = JsonDocument.Parse(cleanOutput.ToString());
        Assert.Equal("clean", clean.RootElement.GetProperty("status").GetString());
        Assert.Empty(clean.RootElement.GetProperty("items").EnumerateArray());
        Assert.Empty(clean.RootElement.GetProperty("warnings").EnumerateArray());
    }

    [Fact]
    public void StrandedMigration_RequiresConfirmationAndDryRunDoesNotMutateEitherRef_G763()
    {
        var stranded = Record("execution-unit:G763-dry-run", "alice", "implementation");
        using var repos = new ClaimRepositories(
            [Record("execution-unit:G763-canonical-baseline", "canonical", "implementation")], [stranded]);
        var beforeCanonical = repos.ReadRef("main");
        var beforeMetadata = repos.ReadRef("main-metadata");

        using var missingConfirmation = new StringWriter();
        var missingExit = ClaimCommand.ExecuteStranded(
            Context(repos.FirstClone),
            MigrationArguments(write: true, includeConfirmation: false),
            missingConfirmation);
        Assert.Equal(1, missingExit);
        Assert.Contains("--confirm-migrate-stranded", missingConfirmation.ToString(), StringComparison.Ordinal);

        using var dryRunOutput = new StringWriter();
        var dryRunExit = ClaimCommand.ExecuteStranded(
            Context(repos.FirstClone),
            MigrationArguments(write: false, includeConfirmation: true),
            dryRunOutput);

        Assert.Equal(0, dryRunExit);
        using var dryRun = JsonDocument.Parse(dryRunOutput.ToString());
        Assert.Equal("planned", dryRun.RootElement.GetProperty("status").GetString());
        Assert.False(dryRun.RootElement.GetProperty("push_succeeded").GetBoolean());
        Assert.Equal(beforeCanonical, repos.ReadRef("main"));
        Assert.Equal(beforeMetadata, repos.ReadRef("main-metadata"));
        Assert.False(repos.HasPath("main", ClaimCommand.ClaimPath(stranded.Scope)));
    }

    [Fact]
    public void StrandedMigration_UsesCanonicalTransactionAndNormalVerifySeesTheRecord_G763()
    {
        var stranded = Record("execution-unit:G763-migrate", "alice", "implementation");
        using var repos = new ClaimRepositories(
            [Record("execution-unit:G763-canonical-baseline", "canonical", "implementation")], [stranded]);
        var expectedRaw = repos.ReadFile("main-metadata", ClaimCommand.ClaimPath(stranded.Scope));

        using var output = new StringWriter();
        var exit = ClaimCommand.ExecuteStranded(
            Context(repos.FirstClone), MigrationArguments(write: true, includeConfirmation: true), output);

        Assert.Equal(0, exit);
        using var result = JsonDocument.Parse(output.ToString());
        Assert.Equal("migrated", result.RootElement.GetProperty("status").GetString());
        Assert.True(result.RootElement.GetProperty("push_succeeded").GetBoolean());
        Assert.False(string.IsNullOrWhiteSpace(result.RootElement.GetProperty("commit").GetString()));
        Assert.Equal("refs/heads/main", result.RootElement.GetProperty("target_ref").GetString());
        Assert.Equal(expectedRaw, repos.ReadFile("main", ClaimCommand.ClaimPath(stranded.Scope)));
        Assert.Equal(expectedRaw, repos.ReadFile("main-metadata", ClaimCommand.ClaimPath(stranded.Scope)));

        var verification = ClaimOwnershipVerifier.Verify(
            repos.FirstClone, stranded.Scope, "implementation");
        Assert.True(verification.Passed);
        Assert.Equal(ClaimOwnershipVerification.StatusOwned, verification.Status);
        Assert.Equal("alice", verification.Holder);
    }

    [Fact]
    public void StrandedMigration_ReportsCanonicalConflictAndDoesNotOverwriteIt_G763()
    {
        var canonical = Record("execution-unit:G763-conflict", "alice", "implementation", "canonical");
        var metadata = Record("execution-unit:G763-conflict", "bob", "review", "metadata");
        using var repos = new ClaimRepositories([canonical], [metadata]);
        var before = repos.ReadFile("main", ClaimCommand.ClaimPath(canonical.Scope));
        var beforeCanonicalRef = repos.ReadRef("main");

        using var output = new StringWriter();
        var exit = ClaimCommand.ExecuteStranded(
            Context(repos.FirstClone), MigrationArguments(write: true, includeConfirmation: true), output);

        Assert.Equal(1, exit);
        using var result = JsonDocument.Parse(output.ToString());
        Assert.Equal("conflict", result.RootElement.GetProperty("status").GetString());
        Assert.False(result.RootElement.GetProperty("push_succeeded").GetBoolean());
        Assert.Equal(1, result.RootElement.GetProperty("conflicts").GetArrayLength());
        Assert.Equal(beforeCanonicalRef, repos.ReadRef("main"));
        Assert.Equal(before, repos.ReadFile("main", ClaimCommand.ClaimPath(canonical.Scope)));
        Assert.Contains("canonical record", result.RootElement.GetProperty("detail").GetString(), StringComparison.OrdinalIgnoreCase);

        using var reportOutput = new StringWriter();
        Assert.Equal(
            0,
            ClaimCommand.ExecuteStranded(
                Context(repos.FirstClone), ["--format", "json"], reportOutput));
        using var report = JsonDocument.Parse(reportOutput.ToString());
        Assert.Equal("conflict", report.RootElement.GetProperty("status").GetString());
        Assert.Contains(
            report.RootElement.GetProperty("warnings").EnumerateArray(),
            warning => warning.GetString()!.Contains("G763-conflict", StringComparison.Ordinal));
    }

    [Fact]
    public void AcquireAndVerify_NeverImplicitlyMigrateMetadataClaims_G763()
    {
        var stranded = Record("execution-unit:G763-stays-stranded", "alice", "implementation");
        using var repos = new ClaimRepositories(
            [Record("execution-unit:G763-canonical-baseline", "canonical", "implementation")], [stranded]);
        var metadataBefore = repos.ReadRef("main-metadata");
        var verification = ClaimOwnershipVerifier.Verify(
            repos.FirstClone, stranded.Scope, "implementation");
        Assert.Equal(ClaimOwnershipVerification.StatusUnheld, verification.Status);

        using var acquireOutput = new StringWriter();
        var acquireExit = ClaimCommand.ExecuteAcquire(
            Context(repos.FirstClone),
            ["--scope", "execution-unit:G763-other", "--actor", "operator", "--team", "implementation",
             "--write", "--format", "json"],
            acquireOutput);
        Assert.Equal(0, acquireExit);
        Assert.Equal("acquired", JsonDocument.Parse(acquireOutput.ToString()).RootElement.GetProperty("status").GetString());
        Assert.Equal(metadataBefore, repos.ReadRef("main-metadata"));
        Assert.False(repos.HasPath("main", ClaimCommand.ClaimPath(stranded.Scope)));

        using var reportOutput = new StringWriter();
        Assert.Equal(
            0,
            ClaimCommand.ExecuteStranded(
                Context(repos.FirstClone), ["--format", "json"], reportOutput));
        using var report = JsonDocument.Parse(reportOutput.ToString());
        Assert.Contains(
            report.RootElement.GetProperty("items").EnumerateArray(),
            item => item.GetProperty("scope").GetString() == stranded.Scope);
    }

    [Fact]
    public void CommandRouter_ExposesStrandedClaimReport_G763()
    {
        using var temp = new TempDirectory("claim-g763-router-");
        using var output = new StringWriter();
        var exit = CommandRouter.Execute(
            ["claim", "stranded", "--format", "json"], Context(temp.Path, metadataBranch: string.Empty), output);

        Assert.Equal(0, exit);
        using var result = JsonDocument.Parse(output.ToString());
        Assert.Equal("not-configured", result.RootElement.GetProperty("status").GetString());
    }

    private static string[] MigrationArguments(bool write, bool includeConfirmation) =>
    [
        "migrate",
        "--current-metadata-branch", "main-metadata",
        "--new-canonical-branch", "main",
        "--actor", "operator",
        "--team", "implementation",
        .. (includeConfirmation ? ["--confirm-migrate-stranded"] : Array.Empty<string>()),
        write ? "--write" : "--dry-run",
        "--format", "json",
    ];

    private static ClaimRecord Record(
        string scope,
        string actor,
        string team,
        string baseCommit = "base-commit") =>
        new("1", scope, actor, team, DateTimeOffset.Parse("2026-08-30T00:00:00Z"), baseCommit);

    private static CliContext Context(string root, string metadataBranch = "main-metadata") => new()
    {
        RepoRoot = root,
        Config = new CliConfig
        {
            Project = new ProjectConfig
            {
                Domain = "intent-cli",
                ArtifactRoot = ".intent-cli",
                MetadataSourceBranch = metadataBranch,
            },
        },
    };

    private static string Serialize(ClaimRecord record) =>
        JsonSerializer.Serialize(record, new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine;

    private static void WriteRecords(string repo, IEnumerable<ClaimRecord> records)
    {
        foreach (var record in records)
        {
            var relative = ClaimCommand.ClaimPath(record.Scope);
            var absolute = Path.Combine(repo, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(absolute)!);
            File.WriteAllText(absolute, Serialize(record), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }
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

    private sealed class ClaimRepositories : IDisposable
    {
        private readonly TempDirectory temp = new("claim-g763-repos-");

        public ClaimRepositories(
            IReadOnlyList<ClaimRecord> canonical,
            IReadOnlyList<ClaimRecord> metadata)
        {
            Bare = Path.Combine(temp.Path, "origin.git");
            var seed = Path.Combine(temp.Path, "seed");
            FirstClone = Path.Combine(temp.Path, "first");
            Directory.CreateDirectory(Bare);
            Run(Bare, "git", "init", "--bare", "--quiet");
            Directory.CreateDirectory(seed);
            Run(seed, "git", "init", "--quiet", "--initial-branch=main");
            Run(seed, "git", "config", "user.name", "g763-fixture");
            Run(seed, "git", "config", "user.email", "g763-fixture@example.invalid");
            File.WriteAllText(Path.Combine(seed, "README.md"), "g763 fixture\n");
            Run(seed, "git", "add", "README.md");
            Run(seed, "git", "commit", "--quiet", "-m", "seed");
            Run(seed, "git", "remote", "add", "origin", Bare);

            WriteRecords(seed, canonical);
            if (canonical.Count > 0)
            {
                Run(seed, "git", "add", "--", ClaimCommand.ClaimsDirectory);
                Run(seed, "git", "commit", "--quiet", "-m", "canonical claims");
            }
            Run(seed, "git", "push", "--quiet", "-u", "origin", "main");
            Run(seed, "git", "switch", "--quiet", "-c", "main-metadata");
            WriteRecords(seed, metadata);
            if (metadata.Count > 0)
            {
                Run(seed, "git", "add", "--", ClaimCommand.ClaimsDirectory);
                if (RunExitCode(seed, "git", "diff", "--cached", "--quiet") != 0)
                {
                    Run(seed, "git", "commit", "--quiet", "-m", "metadata claims");
                }
            }
            Run(seed, "git", "push", "--quiet", "-u", "origin", "main-metadata");
            Run(Bare, "git", "symbolic-ref", "HEAD", "refs/heads/main");
            Run(temp.Path, "git", "clone", "--quiet", Bare, FirstClone);
        }

        public string Bare { get; }
        public string FirstClone { get; }

        public string ReadRef(string branch) => Run(Bare, "git", "rev-parse", $"refs/heads/{branch}").Trim();

        public string ReadFile(string branch, string relative) =>
            Run(Bare, "git", "show", $"refs/heads/{branch}:{relative}");

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

        public bool HasPath(string branch, string relative)
        {
            var info = new ProcessStartInfo("git")
            {
                WorkingDirectory = Bare,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            foreach (var argument in new[] { "cat-file", "-e", $"refs/heads/{branch}:{relative}" })
            {
                info.ArgumentList.Add(argument);
            }
            using var process = Process.Start(info)!;
            process.WaitForExit();
            return process.ExitCode == 0;
        }

        public void Dispose() => temp.Dispose();
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
