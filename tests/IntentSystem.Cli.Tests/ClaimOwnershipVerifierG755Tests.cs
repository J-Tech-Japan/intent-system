using System.Diagnostics;
using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

public sealed class ClaimOwnershipVerifierG755Tests
{
    [Fact]
    public void NonDefaultCheckout_ReadsCanonicalClaim_G755()
    {
        using var repos = new ClaimRepositories();
        var scope = PrepareOwnedNonDefaultCheckout(repos);

        var result = ClaimOwnershipVerifier.Verify(repos.FirstClone, scope, "team-a");

        Assert.True(result.Passed);
        Assert.Equal(ClaimOwnershipVerification.StatusOwned, result.Status);
        Assert.Equal("alice", result.Holder);
        Assert.Equal("team-a", result.HolderTeam);
    }

    [Fact]
    public void NonDefaultCheckout_WithNoLocalClaimsStore_DoesNotFailOpen_G755()
    {
        using var repos = new ClaimRepositories();
        var scope = PrepareOwnedNonDefaultCheckout(repos);

        var result = ClaimOwnershipVerifier.Verify(repos.FirstClone, scope, "team-a");

        Assert.NotEqual(ClaimOwnershipVerification.StatusNotConfigured, result.Status);
        Assert.True(result.Passed);
        Assert.Equal(ClaimOwnershipVerification.StatusOwned, result.Status);
        Assert.Equal("alice", result.Holder);
        Assert.Equal("team-a", result.HolderTeam);
    }

    [Fact]
    public void CanonicalBranchResolutionFailure_FailsClosed_G755()
    {
        using var repos = new ClaimRepositories();
        var scope = PrepareOwnedNonDefaultCheckout(repos);
        Run(repos.Bare, "git", "symbolic-ref", "HEAD", "refs/heads/missing");

        var result = ClaimOwnershipVerifier.Verify(repos.FirstClone, scope, "team-a");

        Assert.False(result.Passed);
        Assert.Equal(ClaimOwnershipVerification.StatusCanonicalUnavailable, result.Status);
        Assert.NotEqual(ClaimOwnershipVerification.StatusNotConfigured, result.Status);
        Assert.NotEmpty(result.Detail);
    }

    [Fact]
    public void CanonicalNoStore_PreservesLegacyVerificationOutput_G755()
    {
        using var repos = new ClaimRepositories();
        using var localRoot = new TempDirectory("claim-g755-no-store-");
        const string scope = "execution-unit:G755-no-store";

        var local = Render(localRoot.Path, scope);
        var canonical = Render(repos.FirstClone, scope);

        Assert.Equal(local, canonical);
        using var document = JsonDocument.Parse(canonical);
        Assert.True(document.RootElement.GetProperty("passed").GetBoolean());
        Assert.Equal(
            ClaimOwnershipVerification.StatusNotConfigured,
            document.RootElement.GetProperty("status").GetString());
        Assert.False(document.RootElement.GetProperty("store_configured").GetBoolean());
    }

    [Fact]
    public void DetachedHead_ReadsCanonicalClaim_G755()
    {
        using var repos = new ClaimRepositories();
        var scope = PrepareOwnedNonDefaultCheckout(repos);
        var canonicalCommit = ReadBareRef(repos.Bare, "main");
        Run(repos.FirstClone, "git", "checkout", "--quiet", "--detach", canonicalCommit);

        var result = ClaimOwnershipVerifier.Verify(repos.FirstClone, scope, "team-a");

        Assert.True(result.Passed);
        Assert.Equal(ClaimOwnershipVerification.StatusOwned, result.Status);
        Assert.Equal("alice", result.Holder);
        Assert.Equal("team-a", result.HolderTeam);
    }

    [Fact]
    public void DocumentationMirrorsDescribeCanonicalRead_G755()
    {
        var root = RepoVersionPolicySource.RepoRoot();
        var english = File.ReadAllText(Path.Combine(root, "docs", "en", "05-implementation-loop.md"));
        var japanese = File.ReadAllText(Path.Combine(root, "docs", "ja", "05-implementation-loop.md"));

        Assert.Contains("remote-default-branch resolver", english, StringComparison.Ordinal);
        Assert.Contains("canonical-unavailable", english, StringComparison.Ordinal);
        Assert.DoesNotContain("the verifier first fetches\nthe current branch", english, StringComparison.Ordinal);
        Assert.Contains("remote-default-branch resolver", japanese, StringComparison.Ordinal);
        Assert.Contains("canonical-unavailable", japanese, StringComparison.Ordinal);
        Assert.DoesNotContain("verifier が current branch を最初に\nfetch", japanese, StringComparison.Ordinal);
    }

    private static string PrepareOwnedNonDefaultCheckout(ClaimRepositories repos)
    {
        repos.PrepareNonDefaultCheckout();
        const string scope = "execution-unit:G755-reader";
        var acquired = ClaimCommand.RunTransaction(
            repos.FirstClone,
            Request(scope, "alice", "team-a"));

        Assert.Equal("acquired", acquired.Status);
        Assert.True(acquired.PushSucceeded);
        Assert.Equal("refs/heads/main", acquired.TargetRef);
        Assert.False(Directory.Exists(
            Path.Combine(repos.FirstClone, ClaimCommand.ClaimsDirectory)));
        return scope;
    }

    private static string Render(string repoRoot, string scope)
    {
        using var writer = new StringWriter();
        var exitCode = ClaimVerificationCommand.Execute(
            Context(repoRoot),
            ["--scope", scope, "--team", "team-a", "--format", "json"],
            writer);
        Assert.Equal(0, exitCode);
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

    private static ClaimRequest Request(string scope, string actor, string team) =>
        new(ClaimOperation.Acquire, scope, actor, team, null, null, true, "json", ClaimCommand.DefaultMaxAttempts);

    private static string ReadBareRef(string bare, string branch) =>
        Run(bare, "git", "rev-parse", $"refs/heads/{branch}").Trim();

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
        private readonly TempDirectory temp = new("claim-repos-g755-");

        public ClaimRepositories()
        {
            Bare = Path.Combine(temp.Path, "origin.git");
            var seed = Path.Combine(temp.Path, "seed");
            FirstClone = Path.Combine(temp.Path, "first");
            Directory.CreateDirectory(Bare);
            Run(Bare, "git", "init", "--bare", "--quiet");
            Directory.CreateDirectory(seed);
            Run(seed, "git", "init", "--quiet", "--initial-branch=main");
            Run(seed, "git", "config", "user.name", "seed");
            Run(seed, "git", "config", "user.email", "seed@example.invalid");
            File.WriteAllText(Path.Combine(seed, "README.md"), "seed\n");
            Run(seed, "git", "add", "README.md");
            Run(seed, "git", "commit", "--quiet", "-m", "seed");
            Run(seed, "git", "remote", "add", "origin", Bare);
            Run(seed, "git", "push", "--quiet", "-u", "origin", "main");
            Run(Bare, "git", "symbolic-ref", "HEAD", "refs/heads/main");
            Run(temp.Path, "git", "clone", "--quiet", Bare, FirstClone);
        }

        public string Bare { get; }
        public string FirstClone { get; }

        public void PrepareNonDefaultCheckout()
        {
            Run(FirstClone, "git", "switch", "--quiet", "-c", "design-thread");
            File.WriteAllText(Path.Combine(FirstClone, "feature.txt"), "feature\n");
            Run(FirstClone, "git", "add", "feature.txt");
            Run(FirstClone, "git", "-c", "user.name=feature", "-c", "user.email=feature@example.invalid",
                "commit", "--quiet", "-m", "feature");
            Run(FirstClone, "git", "push", "--quiet", "-u", "origin", "design-thread");
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
