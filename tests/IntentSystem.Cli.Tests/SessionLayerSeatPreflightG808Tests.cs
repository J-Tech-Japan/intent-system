using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

[Collection("WorkerNextActionSharedState")]
public sealed class SessionLayerSeatPreflightG808Tests : IDisposable
{
    private const string Domain = "intent-cli";
    private const string Team = "intent-cli-dev";
    private readonly string root = Directory.CreateTempSubdirectory("session-layer-seat-g808-").FullName;

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
            ["--domain", Domain, "--team", Team, "--role", "architect", "--format", "json"], output);

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
            ["--domain", Domain, "--team", Team, "--role", "reviewer", "--format", "json"], output);

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

    private CliContext Context() => new()
    {
        RepoRoot = root,
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
}
