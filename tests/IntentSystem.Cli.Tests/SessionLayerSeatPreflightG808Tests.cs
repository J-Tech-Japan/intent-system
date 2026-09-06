using System.Text.Json;
using System.Diagnostics;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;
using Xunit.Abstractions;

namespace IntentSystem.Cli.Tests;

[Collection("WorkerNextActionSharedState")]
public sealed class SessionLayerSeatPreflightG808Tests : IDisposable
{
    private const string Domain = "intent-cli";
    private const string Team = "intent-cli-dev";
    private readonly string root = Directory.CreateTempSubdirectory("session-layer-seat-g808-").FullName;
    private readonly ITestOutputHelper testOutput;

    public SessionLayerSeatPreflightG808Tests(ITestOutputHelper testOutput)
    {
        this.testOutput = testOutput;
    }

    public void Dispose()
    {
        SessionLayerSeatPreflightCommand.GitRunnerFactory = null;
        SessionLayerSeatPreflightCommand.EnvironmentFactory = () => Environment.GetEnvironmentVariables()
            .Cast<System.Collections.DictionaryEntry>()
            .Select(entry => (Key: entry.Key as string, Value: entry.Value as string))
            .Where(entry => entry.Key is not null && entry.Value is not null)
            .Select(entry => new KeyValuePair<string, string>(entry.Key!, entry.Value!));
        SessionLayerSeatPreflightCommand.UtcNowFactory = () => DateTimeOffset.UtcNow;
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }

    [Fact]
    public void PreflightRunsFiveProbesRecordsResultAndLeavesIndexAndBranchCommandsUntouched_G808()
    {
        Directory.CreateDirectory(Path.Combine(root, ".intent-cli", "claims"));
        var runner = new FixtureRunner(root, writable: true);
        SessionLayerSeatPreflightCommand.GitRunnerFactory = _ => runner;
        SessionLayerSeatPreflightCommand.EnvironmentFactory = () =>
            [new KeyValuePair<string, string>("INTENT_CLI_RUNTIME_FAMILY", "sandbox")];
        var output = new StringWriter();

        var exitCode = SessionLayerSeatPreflightCommand.Execute(Context(),
            ["--domain", Domain, "--team", Team, "--role", "architect", "--launch-at", "2026-09-06T11:59:00Z", "--format", "json"], output);

        Assert.Equal(0, exitCode);
        using var json = JsonDocument.Parse(output.ToString());
        Assert.True(json.RootElement.GetProperty("passed").GetBoolean());
        Assert.Equal(5, json.RootElement.GetProperty("probes").GetArrayLength());
        Assert.Equal(5, runner.Calls.Count);
        Assert.Equal(2, runner.Calls.Count(call => call[0] == "update-ref"));
        Assert.False(runner.ScratchRefPresent);
        Assert.DoesNotContain(runner.Calls, call => call[0] is "reset" or "checkout" or "update-index");
        Assert.True(File.Exists(SessionLayerSeatPreflightStore.ResolvePath(root)));
        Assert.Contains("\"runtime_family\": \"intent-cli-runtime\"", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void ReadOnlyGitFailureIsRecordedWithActionableRemedyAndNonzeroExit_G808()
    {
        Directory.CreateDirectory(Path.Combine(root, ".intent-cli", "claims"));
        var runner = new FixtureRunner(root, writable: false);
        SessionLayerSeatPreflightCommand.GitRunnerFactory = _ => runner;
        var output = new StringWriter();

        var exitCode = SessionLayerSeatPreflightCommand.Execute(Context(),
            ["--domain", Domain, "--team", Team, "--role", "reviewer", "--launch-at", "2026-09-06T11:59:00Z", "--format", "json"], output);

        Assert.Equal(1, exitCode);
        using var json = JsonDocument.Parse(output.ToString());
        Assert.False(json.RootElement.GetProperty("passed").GetBoolean());
        var writable = json.RootElement.GetProperty("probes").EnumerateArray()
            .Single(probe => probe.GetProperty("name").GetString() == "git-writable");
        Assert.False(writable.GetProperty("passed").GetBoolean());
        Assert.Contains("Grant the seat write access", writable.GetProperty("remedy").GetString(), StringComparison.Ordinal);
        Assert.True(File.Exists(SessionLayerSeatPreflightStore.ResolvePath(root)));
    }

    [Fact]
    public void RemedyTableIsProbeAndMarkerFamilyKeyedWithoutModelNames_G808()
    {
        var forbidden = new[] { "claude", "codex", "sonnet", "luna", "astra", "opencode" };
        Assert.Contains(SessionLayerSeatPreflightCommand.RemedyTable.Keys, key => key == "git-writable|intent-cli-runtime");
        Assert.Contains(SessionLayerSeatPreflightCommand.RemedyTable.Keys, key => key == "runtime-family|agent-runtime");
        foreach (var entry in SessionLayerSeatPreflightCommand.RemedyTable)
        {
            Assert.DoesNotContain(forbidden, name => entry.Key.Contains(name, StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(forbidden, name => entry.Value.Contains(name, StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public void LiveValidationNamesMissingOrStalePreflightAndAcceptsOneCurrentPass_G808()
    {
        var now = DateTimeOffset.Parse("2026-09-06T12:00:00Z");
        SessionLayerSeatPreflightCommand.UtcNowFactory = () => now;
        var stale = Record("architect", now.AddMinutes(-10), now.AddMinutes(1), passed: true);
        var current = Record("reviewer", now, now.AddMinutes(-1), passed: true);
        Assert.True(stale.Applied);
        Assert.True(current.Applied);
        var topology = new NotifyTeamTopology(
            "test",
            Domain,
            Team,
            "workspace",
            new Dictionary<string, NotifyRecordedRole>
            {
                ["architect"] = new(NotifyRecordedRole.HerdrResident, "workspace", "p1", null, "/repo", null, null, null),
                ["reviewer"] = new(NotifyRecordedRole.HerdrResident, "workspace", "p2", null, "/repo", null, null, null),
            },
            new Dictionary<string, AgentLaunchEnvelopeProfile>());

        var findings = SessionLayerSeatPreflightStore.EvaluateLive(root, Domain, Team, topology);

        var finding = Assert.Single(findings);
        Assert.Equal("architect", finding.Role);
        Assert.Equal("seat-preflight-missing-or-stale", finding.Cause);
        Assert.DoesNotContain(findings, item => item.Role == "reviewer");
        Assert.All(findings, item => Assert.True(item.IsInformational));
    }

    [Fact]
    public void LiveTopologyValidationKeepsSeatPreflightInformationalBeforeAndAfter_G808()
    {
        Directory.CreateDirectory(Path.Combine(root, ".intent-cli", "claims"));
        var topology = SessionLayerTopologyWriter.Record(root, new SessionLayerTopologyRecordRequest
        {
            Domain = Domain,
            Team = Team,
            Role = "architect",
            Resident = NotifyRecordedRole.HerdrResident,
            WorkspaceId = "wG808",
            PaneId = "wG808:p1",
            Cwd = "/machine-local",
            Kind = "codex",
            Write = true,
            Format = "json",
        });
        Assert.True(topology.Applied);
        NotifyCommand.ProcessRunnerFactory = () => new TopologyProcessRunner();

        var beforeOutput = new StringWriter();
        var beforeExit = SessionLayerTopologyCommand.ExecuteValidate(Context(), ValidateArguments(), beforeOutput);
        Assert.Equal(0, beforeExit);
        using var beforeJson = JsonDocument.Parse(beforeOutput.ToString());
        var beforeFinding = Assert.Single(
            beforeJson.RootElement.GetProperty("findings").EnumerateArray(),
            finding => finding.GetProperty("field").GetString() == "seat_preflight");
        Assert.True(beforeFinding.GetProperty("is_informational").GetBoolean());
        Assert.True(beforeJson.RootElement.GetProperty("valid").GetBoolean());

        var now = DateTimeOffset.Parse("2026-09-06T12:00:00Z");
        Assert.True(Record("architect", now, now.AddMinutes(-1), passed: true).Applied);
        var afterOutput = new StringWriter();
        var afterExit = SessionLayerTopologyCommand.ExecuteValidate(Context(), ValidateArguments(), afterOutput);
        Assert.Equal(0, afterExit);
        using var afterJson = JsonDocument.Parse(afterOutput.ToString());
        Assert.DoesNotContain(afterJson.RootElement.GetProperty("findings").EnumerateArray(),
            finding => finding.GetProperty("field").GetString() == "seat_preflight");
        Assert.True(afterJson.RootElement.GetProperty("valid").GetBoolean());

        testOutput.WriteLine("live topology validation before (no ledger):");
        testOutput.WriteLine(beforeOutput.ToString());
        testOutput.WriteLine("live topology validation after (passing record):");
        testOutput.WriteLine(afterOutput.ToString());
    }

    [Fact]
    public void NoLaunchAtUsesDurableVerifiedLaunchRecordInsteadOfObservedAt_G808()
    {
        Directory.CreateDirectory(Path.Combine(root, ".intent-cli", "claims"));
        var observedAt = DateTimeOffset.Parse("2026-09-06T12:00:00Z");
        var launchAt = observedAt.AddHours(-2);
        SessionLayerSeatPreflightCommand.UtcNowFactory = () => observedAt;
        Assert.True(SessionLayerTopologyWriter.Record(root, new SessionLayerTopologyRecordRequest
        {
            Domain = Domain,
            Team = Team,
            Role = "architect",
            Resident = NotifyRecordedRole.HerdrResident,
            WorkspaceId = "wG808",
            PaneId = "wG808:p1",
            Cwd = "/machine-local",
            Kind = "codex",
            Write = true,
            Format = "json",
        }).Applied);
        Assert.True(ModelResolutionLedgerStore.Append(root, new ModelResolutionLedgerEntry
        {
            InformalName = "recorded-seat",
            Kind = "codex",
            Outcome = ModelResolutionLedgerCommand.VerifiedOutcome,
            FullInvocation = "codex --model recorded-seat",
            Evidence = "READY banner and running argv",
            RecordedAt = launchAt,
        }, write: true).Applied);

        var runner = new FixtureRunner(root, writable: true);
        SessionLayerSeatPreflightCommand.GitRunnerFactory = _ => runner;
        using var output = new StringWriter();
        var exitCode = SessionLayerSeatPreflightCommand.Execute(Context(),
            ["--domain", Domain, "--team", Team, "--role", "architect", "--format", "json"], output);

        Assert.Equal(0, exitCode);
        using var json = JsonDocument.Parse(output.ToString());
        Assert.Equal(launchAt, json.RootElement.GetProperty("launch_at").GetDateTimeOffset());
        Assert.Contains("model-resolution verified launch",
            json.RootElement.GetProperty("launch_source").GetString(), StringComparison.Ordinal);
        Assert.NotEqual(json.RootElement.GetProperty("observed_at").GetDateTimeOffset(),
            json.RootElement.GetProperty("launch_at").GetDateTimeOffset());
    }

    [Fact]
    public void RealRepositoryReadOnlyGitProbePreservesStatusBranchAndRefs_G808()
    {
        var remoteRoot = Directory.CreateTempSubdirectory("session-layer-seat-g808-remote-").FullName;
        try
        {
            Directory.CreateDirectory(Path.Combine(root, ".intent-cli", "claims"));
            RunGit(remoteRoot, "init", "--bare");
            RunGit(root, "init", "-b", "main");
            RunGit(root, "config", "user.email", "operator@example.test");
            RunGit(root, "config", "user.name", "Operator");
            File.WriteAllText(Path.Combine(root, "README.md"), "G808 real repository\n");
            File.WriteAllText(Path.Combine(root, ".gitignore"), ".intent-cli/\n");
            Directory.CreateDirectory(Path.Combine(root, ".intent-cli", "claims"));
            RunGit(root, "add", "README.md");
            RunGit(root, "add", ".gitignore");
            RunGit(root, "commit", "-m", "fixture");
            RunGit(root, "remote", "add", "origin", remoteRoot);
            RunGit(root, "push", "--set-upstream", "origin", "main");
            var canonicalRoot = RunGit(root, "rev-parse", "--show-toplevel").StdOut.Trim();

            var before = RepositorySnapshot(root);
            MakeGitReadOnly(root, readOnly: true);
            try
            {
                SessionLayerSeatPreflightCommand.GitRunnerFactory = null;
                SessionLayerSeatPreflightCommand.UtcNowFactory = () => DateTimeOffset.Parse("2026-09-06T12:00:00Z");
                using var output = new StringWriter();
                var exitCode = SessionLayerSeatPreflightCommand.Execute(Context(canonicalRoot),
                    ["--domain", Domain, "--team", Team, "--role", "architect", "--launch-at", "2026-09-06T11:00:00Z", "--format", "json"],
                    output);

                Assert.Equal(1, exitCode);
                using var json = JsonDocument.Parse(output.ToString());
                var writable = json.RootElement.GetProperty("probes").EnumerateArray()
                    .Single(probe => probe.GetProperty("name").GetString() == "git-writable");
                Assert.False(writable.GetProperty("passed").GetBoolean());
                Assert.Contains("Grant the seat write access", writable.GetProperty("remedy").GetString(), StringComparison.Ordinal);
                testOutput.WriteLine("real-repository read-only preflight output:");
                testOutput.WriteLine(output.ToString());
            }
            finally
            {
                MakeGitReadOnly(root, readOnly: false);
            }

            var after = RepositorySnapshot(root);
            Assert.Equal(before, after);
            testOutput.WriteLine("real-repository before/after snapshot:");
            testOutput.WriteLine(before);
            testOutput.WriteLine(after);
        }
        finally
        {
            if (Directory.Exists(remoteRoot)) Directory.Delete(remoteRoot, recursive: true);
        }
    }

    [Fact]
    public void GuidesNamePreflightBeforeDelegationInMarkdownAndJson_G808()
    {
        using var onboarding = new StringWriter();
        Assert.Equal(0, GuideOnboardingCommand.Execute(Context(), ["--format", "markdown"], onboarding));
        Assert.Contains("session-layer seat preflight", onboarding.ToString(), StringComparison.Ordinal);

        using var onboardingJson = new StringWriter();
        Assert.Equal(0, GuideOnboardingCommand.Execute(Context(), ["--format", "json"], onboardingJson));
        using var onboardingDocument = JsonDocument.Parse(onboardingJson.ToString());
        Assert.Contains("session-layer seat preflight",
            onboardingDocument.RootElement.GetProperty("seat_preflight").GetProperty("command").GetString(),
            StringComparison.Ordinal);

        using var orchestrator = new StringWriter();
        Assert.Equal(0, GuideOrchestratorThreadCommand.Execute(Context(),
            ["--domain", Domain, "--target-repo", "J-Tech-Japan/intent-system", "--agent", "codex", "--format", "markdown"],
            orchestrator));
        Assert.Contains("seat preflight (G808)", orchestrator.ToString(), StringComparison.Ordinal);
        Assert.Contains("session-layer seat preflight", orchestrator.ToString(), StringComparison.Ordinal);
    }

    private SessionLayerSeatPreflightAppendResult Record(string role, DateTimeOffset observedAt, DateTimeOffset launchAt, bool passed)
        => SessionLayerSeatPreflightStore.Append(root, new SessionLayerSeatPreflightRecord
        {
            Domain = Domain,
            Team = Team,
            Role = role,
            ObservedAt = observedAt,
            LaunchAt = launchAt,
            Passed = passed,
            RuntimeFamily = "unmarked",
            Probes = [],
        });

    private static void MakeGitReadOnly(string repository, bool readOnly)
    {
        var mode = readOnly ? "a-w" : "u+w";
        var result = RunProcess("/bin/chmod", repository, "-R", mode, Path.Combine(repository, ".git"));
        Assert.Equal(0, result.ExitCode);
    }

    private static string RepositorySnapshot(string repository)
    {
        var status = RunGit(repository, "status", "--porcelain").StdOut.TrimEnd();
        var branch = RunGit(repository, "branch", "--show-current").StdOut.TrimEnd();
        var head = RunGit(repository, "rev-parse", "HEAD").StdOut.TrimEnd();
        var refs = RunGit(repository, "for-each-ref", "--format=%(refname) %(objectname)", "refs/heads", "refs/intent-cli/preflight").StdOut.TrimEnd();
        var index = RunGit(repository, "hash-object", ".git/index").StdOut.TrimEnd();
        return $"status={status}\nbranch={branch}\nhead={head}\nrefs={refs}\nindex={index}";
    }

    private static GitRemoteCommandResult RunGit(string repository, params string[] arguments)
    {
        var result = RunProcess("git", repository, arguments);
        Assert.Equal(0, result.ExitCode);
        return new GitRemoteCommandResult
        {
            ExitCode = result.ExitCode,
            StdOut = result.StdOut,
            StdErr = result.StdErr,
        };
    }

    private static (int ExitCode, string StdOut, string StdErr) RunProcess(string fileName, string workingDirectory, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);
        using var process = Process.Start(startInfo)!;
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, stdout, stderr);
    }

    private CliContext Context(string? repoRoot = null) => new()
    {
        RepoRoot = repoRoot ?? root,
        Config = new CliConfig { Project = new ProjectConfig { Domain = Domain, ArtifactRoot = ".intent-cli" } },
    };

    private sealed class FixtureRunner(string root, bool writable) : IGitRemoteCommandRunner
    {
        public List<IReadOnlyList<string>> Calls { get; } = [];
        public bool ScratchRefPresent { get; private set; }

        public GitRemoteCommandResult Run(string workingDirectory, IReadOnlyList<string> arguments)
        {
            Calls.Add([.. arguments]);
            if (arguments[0] == "update-ref")
                ScratchRefPresent = !arguments.Contains("-d");
            var stdout = arguments.SequenceEqual(["rev-parse", "--show-toplevel"])
                ? root + Environment.NewLine
                : arguments[0] == "var"
                    ? "Operator <operator@example.test> 1700000000 -0700\n"
                    : string.Empty;
            var success = arguments[0] switch
            {
                "update-ref" => writable,
                "ls-remote" => true,
                "var" => true,
                "rev-parse" => true,
                _ => false,
            };
            return new GitRemoteCommandResult
            {
                ExitCode = success ? 0 : 1,
                StdOut = stdout,
                StdErr = success ? string.Empty : "permission denied",
            };
        }
    }

    private sealed class TopologyProcessRunner : INotifyProcessRunner
    {
        public NotifyProcessResult Run(string fileName, IReadOnlyList<string> arguments)
            => new(0, "{\"result\":{\"panes\":[{\"workspace_id\":\"wG808\",\"pane_id\":\"wG808:p1\",\"label\":\"architect\"}]}}", string.Empty);
    }

    private static string[] ValidateArguments() =>
    [
        "--domain", Domain,
        "--team", Team,
        "--live",
        "--format", "json",
    ];
}
