using System.Diagnostics;
using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G699: repeated supervision observations remain durable and visible while
/// duplicate emissions back off, and pane status classification requires the
/// recorded consecutive-observation threshold.
///
/// The temporary fixtures are intentionally retained. In particular, these
/// tests never delete a path under the system temporary directory.
/// </summary>
[Collection("WorkerNextActionSharedState")]
public sealed class NotifySupervisionG699Tests : IDisposable
{
    private const string Domain = "intent-cli";
    private const string Team = "intent-cli-dev";
    private readonly string root = Path.Combine(Path.GetTempPath(), $"intent-g699-{Guid.NewGuid():N}");
    private readonly DateTimeOffset firstNow = new(2026, 8, 14, 12, 0, 0, TimeSpan.Zero);
    private DateTimeOffset now;

    public NotifySupervisionG699Tests()
    {
        Directory.CreateDirectory(root);
        now = firstNow;
        NotifyCommand.UtcNowFactory = () => now;
        NotifySupervisor.Delay = _ => { };
    }

    public void Dispose()
    {
        NotifyCommand.UtcNowFactory = null;
        NotifyCommand.ProcessRunnerFactory = null;
        NotifyCommand.AgmsgScriptsDirectoryFactory = null;
        NotifyCommand.HerdrExecutableFactory = null;
        NotifyCommand.BashExecutableFactory = null;
        NotifySupervisionStore.WriteOverride = null;
        NotifySupervisor.Delay = Thread.Sleep;
    }

    [Fact]
    public void TenSameKeyObservationsUseRecordedCadenceAndExposeParkedCounters_G699()
    {
        var context = CreateContext();
        var supervisor = CreateSupervisor(
            context,
            _ => [Observation("same-key", "the same durable escalation remains owed")],
            repeatBackoffSeconds: 20,
            debounceConsecutiveObservations: 3);

        var passes = new List<NotifySupervisorPass>();
        for (var i = 0; i < 10; i++)
        {
            passes.Add(supervisor.RunOnce());
            now = firstNow.AddSeconds((i + 1) * 10);
        }

        var emissionCycles = passes.Count(pass => pass.Findings.Any(finding => finding.Key == "fixture:same-key"));
        Assert.Equal(5, emissionCycles);
        var final = Assert.Single(passes[^1].RecoveryRecords, record => record.Key == "fixture:same-key");
        var policy = passes[^1].EmissionPolicy;
        Assert.NotNull(policy);
        Assert.Equal(1_800, NotifySupervisionEmissionPolicy.DefaultRepeatBackoffSeconds);
        Assert.Equal(20, policy.RepeatBackoffSeconds);
        Assert.Equal(3, policy.DebounceConsecutiveObservations);
        Assert.Equal(firstNow, final.FirstSeenAt);
        Assert.Equal(firstNow.AddSeconds(90), final.LastSeenAt);
        Assert.Equal(10, final.RepeatCount);
        Assert.True(final.Parked);
        Assert.Equal(20, final.EmissionCadenceSeconds);

        var policyPath = NotifySupervisionStore.ResolveEmissionPolicyPath(
            context.ResolveSupervisionArtifactRootPath(),
            Domain,
            Team);
        Assert.True(File.Exists(policyPath));
        Assert.Contains("repeat_backoff_seconds", File.ReadAllText(policyPath), StringComparison.Ordinal);
        Assert.Contains("debounce_consecutive_observations", File.ReadAllText(policyPath), StringComparison.Ordinal);
    }

    [Fact]
    public void ResolutionAndConditionChangeResetSameKeyState_AndNewKeyIsImmediate_G699()
    {
        var context = CreateContext();
        string? current = "same-key";
        var summary = "condition-a";
        var supervisor = CreateSupervisor(
            context,
            _ => current is null
                ? []
                : [Observation(current, summary)],
            repeatBackoffSeconds: 60,
            debounceConsecutiveObservations: 3);

        supervisor.RunOnce();
        now = firstNow.AddSeconds(10);
        supervisor.RunOnce();
        now = firstNow.AddSeconds(20);
        var parked = supervisor.RunOnce();
        Assert.True(Assert.Single(parked.RecoveryRecords, record => record.Key == "fixture:same-key").Parked);

        // A genuinely new key is not held behind the parked key's backoff.
        now = firstNow.AddSeconds(30);
        current = "new-key";
        var newKey = supervisor.RunOnce();
        Assert.Contains(newKey.Findings, finding => finding.Key == "fixture:new-key");

        // A condition change resets the identity's observation window even
        // though the durable key remains the same.
        current = "same-key";
        summary = "condition-b";
        now = firstNow.AddSeconds(40);
        var changed = supervisor.RunOnce();
        var changedRecord = Assert.Single(changed.RecoveryRecords, record => record.Key == "fixture:same-key" && record.ClearedAt is null);
        Assert.Equal(1, changedRecord.RepeatCount);
        Assert.Equal(now, changedRecord.FirstSeenAt);
        Assert.False(changedRecord.Parked);
        Assert.Contains(changed.Findings, finding => finding.Key == "fixture:same-key");

        // Resolution clears the active record. A later reappearance starts a
        // fresh window rather than inheriting the old repeat count.
        current = null;
        now = firstNow.AddSeconds(50);
        var resolved = supervisor.RunOnce();
        Assert.DoesNotContain(resolved.RecoveryRecords, record => record.Key == "fixture:same-key" && record.ClearedAt is null);
        current = "same-key";
        summary = "condition-b";
        now = firstNow.AddSeconds(60);
        var reappeared = supervisor.RunOnce();
        var fresh = Assert.Single(reappeared.RecoveryRecords, record => record.Key == "fixture:same-key" && record.ClearedAt is null);
        Assert.Equal(1, fresh.RepeatCount);
        Assert.Equal(now, fresh.FirstSeenAt);
        Assert.False(fresh.Parked);
    }

    [Fact]
    public void OnePollFlapIsNotClassified_ButConstantSequenceThresholdClassifiesAndSeedsG695Chain_G699()
    {
        var context = CreateContext();
        RecordHerdrOnlyMode(context);
        WriteTopology();
        var status = "working";
        long sequence = 1;
        var runner = new FixtureRunner(() => AgentsJson(status, sequence));
        var supervisor = new NotifyMeasuredSupervisor(
            context: context,
            routingRoot: root,
            domain: Domain,
            team: Team,
            repo: null,
            ownerRole: "orchestration",
            intervalSeconds: 10,
            declaredBoundSeconds: null,
            staleMinutes: 45,
            claimedSilentMinutes: 720,
            backlogIdleMinutes: 45,
            repairSilentMinutes: 180,
            autoRedispatch: false,
            write: true,
            format: "json",
            runner: runner,
            herdrExecutable: "fake-herdr",
            agmsgScriptsDirectory: root,
            repeatBackoffSeconds: 60,
            debounceConsecutiveObservations: 3);

        var first = supervisor.RunOnce();
        Assert.DoesNotContain(first.Findings, finding => finding.Kind == "seat-state-transition");

        status = "blocked";
        sequence++;
        now = firstNow.AddSeconds(10);
        var onePollFlap = supervisor.RunOnce();
        Assert.DoesNotContain(onePollFlap.Findings, finding => finding.Kind == "seat-state-transition");
        Assert.DoesNotContain(onePollFlap.RecoveryRecords, record => record.Kind == "seat-state-transition");

        status = "working";
        sequence++;
        now = firstNow.AddSeconds(20);
        var flap = supervisor.RunOnce();
        Assert.DoesNotContain(flap.Findings, finding => finding.Kind == "seat-state-transition");

        // state_change_sequence advances only for this real working→blocked
        // transition. It remains constant for every later poll of the same
        // blocked state.
        status = "blocked";
        sequence++;
        now = firstNow.AddSeconds(30);
        var sustainedPoll1 = supervisor.RunOnce();
        Assert.DoesNotContain(sustainedPoll1.Findings, finding => finding.Kind == "seat-state-transition");
        Assert.DoesNotContain(sustainedPoll1.RecoveryRecords, record => record.Kind == "seat-state-transition");

        now = firstNow.AddSeconds(40);
        var sustainedPoll2 = supervisor.RunOnce();
        Assert.DoesNotContain(sustainedPoll2.Findings, finding => finding.Kind == "seat-state-transition");
        Assert.DoesNotContain(sustainedPoll2.RecoveryRecords, record => record.Kind == "seat-state-transition");

        now = firstNow.AddSeconds(50);
        var sustainedPoll3 = supervisor.RunOnce();
        var classified = sustainedPoll3;
        var transition = Assert.Single(classified.Findings, finding => finding.Kind == "seat-state-transition");
        Assert.Equal("implementation", transition.SubjectRole);
        Assert.Contains("working→blocked", transition.Summary, StringComparison.Ordinal);
        Assert.Equal(3, classified.EmissionPolicy!.DebounceConsecutiveObservations);
        var cycle = NotifySupervisionStore.Read(context.ResolveSupervisionArtifactRootPath(), Domain, Team).LastCycle!;
        Assert.Equal(3, cycle.LastObservedAgentStatusConsecutiveCounts["seat-state:wG699:wG699:p2"]);
        Assert.Equal(sequence, cycle.LastObservedStateChangeSequences["seat-state:wG699:wG699:p2"]);

        var transitionChain = ContinuationChainStore.Read(root, Domain, Team);
        var chain = Assert.Single(transitionChain.Records);
        Assert.Contains(chain.Links, link => link.Name == ContinuationChainStore.ReportReceived
            && link.Source == "herdr-state-transition");
        Assert.Contains(chain.Links, link => link.Name == ContinuationChainStore.WakeDeliveredOrObserved);
        Assert.Equal(ContinuationChainStore.CanonicalStateClassified, chain.NextMissingLink);
    }

    [Fact]
    public void PreRepairSequenceOrderingConsumesConstantSequenceAndNeverClassifies_G699()
    {
        // Negative control for the shipped defect: writing the new sequence
        // before the debounce gate makes the next cycle's prior sequence
        // equal to the still-current sequence, so the pre-existing advance
        // guard prevents classification forever.
        Assert.Null(PreRepairClassificationPollForConstantSequence());
    }

    [Fact]
    public void OrchestratorGuideNamesRecordedPolicyAndRunsFromMetadataFreeBareDirectory_G699()
    {
        var cliDll = Path.Combine(Path.GetFullPath(AppContext.BaseDirectory), "IntentSystem.Cli.dll");
        Assert.True(File.Exists(cliDll), $"built CLI not found beside active test output: {cliDll}");
        var bareDirectory = Path.Combine(Path.GetTempPath(), $"intent-g699-guide-{Guid.NewGuid():N}");
        Directory.CreateDirectory(bareDirectory);

        var guide = RunBuiltCli(
            cliDll,
            bareDirectory,
            "guide", "orchestrator-thread",
            "--domain", Domain,
            "--target-repo", "J-Tech-Japan/intent-system",
            "--agent", "claude",
            "--format", "json");
        Assert.Equal(0, guide.ExitCode);
        using var document = JsonDocument.Parse(guide.Output);
        var hygiene = document.RootElement
            .GetProperty("design_workspace_supervision")
            .GetProperty("emission_hygiene");
        var commands = hygiene.GetProperty("commands").EnumerateArray().Select(value => value.GetString()!).ToArray();
        Assert.Contains(commands, command => command.Contains("--repeat-backoff-seconds 1800", StringComparison.Ordinal));
        Assert.Contains(commands, command => command.Contains("--debounce-consecutive-observations 3", StringComparison.Ordinal));
        var configuration = string.Join("\n", hygiene.GetProperty("recorded_configuration").EnumerateArray().Select(value => value.GetString()));
        Assert.Contains("emission-policy.json", configuration, StringComparison.Ordinal);
        var semantics = string.Join("\n", hygiene.GetProperty("operating_semantics").EnumerateArray().Select(value => value.GetString()));
        foreach (var marker in new[] { "first_seen", "last_seen", "repeat_count", "parked", "new observation key", "G695" })
        {
            Assert.Contains(marker, semantics, StringComparison.OrdinalIgnoreCase);
        }
        var corroboration = hygiene.GetProperty("corroboration_contract");
        Assert.Equal("observation-conflict", corroboration.GetProperty("conflict_kind").GetString());
        Assert.Equal(
            ["registration_definition", "registration_lookup", "registration_result", "consulted_observations"],
            corroboration.GetProperty("self_verifying_fields").EnumerateArray().Select(value => value.GetString()!).ToArray());
        Assert.Contains("same-cycle", corroboration.GetProperty("same_cycle_rule").GetString(), StringComparison.Ordinal);
        Assert.Contains("no automatic action", corroboration.GetProperty("inconclusive_rule").GetString(), StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(bareDirectory, ".intent-cli")));
        Assert.DoesNotContain("config.toml", guide.Output, StringComparison.Ordinal);

        var invalid = RunBuiltCli(
            cliDll,
            bareDirectory,
            "notify", "supervise",
            "--domain", Domain,
            "--team", Team,
            "--repeat-backoff-seconds", "0",
            "--format", "json");
        Assert.Equal(1, invalid.ExitCode);
        Assert.Contains("--repeat-backoff-seconds must be between", invalid.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void EnglishAndJapaneseGuidanceDeclareTheSameEmissionPolicy_G699()
    {
        var root = RepoVersionPolicySource.RepoRoot();
        var english = File.ReadAllText(Path.Combine(root, "docs", "en", "12-agent-message-orchestration.md"));
        var japanese = File.ReadAllText(Path.Combine(root, "docs", "ja", "12-agent-message-orchestration.md"));
        foreach (var marker in new[]
        {
            "G699",
            "--repeat-backoff-seconds",
            "--debounce-consecutive-observations",
            "emission-policy.json",
            "first_seen",
            "last_seen",
            "repeat_count",
            "parked",
            "G695",
            "G707",
            "observation-conflict",
            "registration_definition",
            "consulted_observations",
        })
        {
            Assert.Contains(marker, english, StringComparison.Ordinal);
            Assert.Contains(marker, japanese, StringComparison.Ordinal);
        }

        var englishLedger = File.ReadAllText(Path.Combine(root, "docs", "en", "1.0-compatibility-ledger.md"));
        var japaneseLedger = File.ReadAllText(Path.Combine(root, "docs", "ja", "1.0-compatibility-ledger.md"));
        Assert.Contains("repeated-observation emission hygiene", englishLedger, StringComparison.Ordinal);
        Assert.Contains("repeated-observation emission hygiene", japaneseLedger, StringComparison.Ordinal);
    }

    private NotifyMeasuredSupervisor CreateSupervisor(
        CliContext context,
        Func<DateTimeOffset, IReadOnlyList<NotifySupervisionObservation>> observations,
        int repeatBackoffSeconds,
        int debounceConsecutiveObservations) => new(
        context: context,
        routingRoot: root,
        domain: Domain,
        team: Team,
        repo: null,
        ownerRole: "orchestration",
        intervalSeconds: 10,
        declaredBoundSeconds: null,
        staleMinutes: 45,
        claimedSilentMinutes: 720,
        backlogIdleMinutes: 45,
        repairSilentMinutes: 180,
        autoRedispatch: false,
        write: true,
        format: "json",
        runner: new FixtureRunner(() => "{\"result\":{\"agents\":[]}}"),
        herdrExecutable: "fake-herdr",
        agmsgScriptsDirectory: root,
        repeatBackoffSeconds: repeatBackoffSeconds,
        debounceConsecutiveObservations: debounceConsecutiveObservations,
        observationProvider: observations);

    private static NotifySupervisionObservation Observation(string key, string summary) => new()
    {
        Key = $"fixture:{key}",
        Kind = "fixture-escalation",
        OwnerRole = "orchestration",
        Source = "g699-test",
        Summary = summary,
        WakeAlreadyAttempted = true,
        WakeAlreadyDelivered = true,
    };

    private CliContext CreateContext() => new()
    {
        RepoRoot = root,
        Config = new CliConfig
        {
            Project = new ProjectConfig { Domain = Domain, ArtifactRoot = ".intent-cli" },
        },
    };

    private void RecordHerdrOnlyMode(CliContext context)
    {
        using var writer = new StringWriter();
        Assert.Equal(0, SessionLayerCommand.ExecuteSet(
            context,
            ["--domain", Domain, "--team", Team, "--mode", "herdr-only", "--write", "--format", "json"],
            writer));
    }

    private void WriteTopology()
    {
        var path = NotifyRoleTopologyStore.ResolvePath(root, Domain, Team);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(new
        {
            domain = Domain,
            team = Team,
            workspace_id = "wG699",
            roles = new Dictionary<string, object>
            {
                ["orchestration"] = new { resident = "herdr", workspace_id = "wG699", pane_id = "wG699:p1" },
                ["implementation"] = new { resident = "herdr", workspace_id = "wG699", pane_id = "wG699:p2" },
                ["review"] = new { resident = "herdr", workspace_id = "wG699", pane_id = "wG699:p3" },
            },
        }));
    }

    private static string AgentsJson(string implementationStatus, long implementationSequence)
    {
        static object Agent(string role, string pane, string status, long sequence) => new
        {
            name = role,
            workspace_id = "wG699",
            pane_id = pane,
            agent = "fixture",
            agent_session = new { id = role },
            agent_status = status,
            agent_running = true,
            interactive_ready = true,
            state_change_seq = sequence,
            last_state_change_at = "2026-08-14T12:00:00.0000000+00:00",
        };

        return JsonSerializer.Serialize(new
        {
            result = new
            {
                agents = new[]
                {
                    Agent("orchestration", "wG699:p1", "working", 1),
                    Agent("implementation", "wG699:p2", implementationStatus, implementationSequence),
                    Agent("review", "wG699:p3", "working", 1),
                },
            },
        });
    }

    private static int? PreRepairClassificationPollForConstantSequence()
    {
        long previousCycleSequence = 1;
        var previousStatus = "working";
        var previousCount = 1;
        var previousRunFrom = "working";

        for (var poll = 1; poll <= 3; poll++)
        {
            const long sequence = 2;
            const string status = "blocked";
            var priorSequence = (long?)previousCycleSequence;
            var consecutiveCount = string.Equals(previousStatus, status, StringComparison.Ordinal)
                ? Math.Max(1, previousCount) + 1
                : 1;
            var runFrom = string.Equals(previousStatus, status, StringComparison.Ordinal)
                ? previousRunFrom
                : previousStatus;

            // This is the pre-repair ordering from ReadRecordedSeatTransitions.
            var currentCycleSequence = sequence;
            var classified = string.Equals(runFrom, "working", StringComparison.Ordinal)
                && consecutiveCount >= 3
                && priorSequence is not null
                && sequence > priorSequence.Value;
            if (classified)
            {
                return poll;
            }

            previousCycleSequence = currentCycleSequence;
            previousStatus = status;
            previousCount = consecutiveCount;
            previousRunFrom = runFrom;
        }

        return null;
    }

    private static ProcessResult RunBuiltCli(string cliDll, string workingDirectory, params string[] arguments)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo("dotnet")
            {
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            },
        };
        process.StartInfo.ArgumentList.Add("exec");
        process.StartInfo.ArgumentList.Add(cliDll);
        foreach (var argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        Assert.True(process.Start());
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return new ProcessResult(process.ExitCode, output + error);
    }

    private sealed record ProcessResult(int ExitCode, string Output);

    private sealed class FixtureRunner(Func<string> agentsJson) : INotifyProcessRunner
    {
        public NotifyProcessResult Run(string fileName, IReadOnlyList<string> arguments)
        {
            if (arguments.SequenceEqual(["agent", "list"]))
            {
                return new NotifyProcessResult(0, agentsJson(), string.Empty);
            }

            return new NotifyProcessResult(
                0,
                "{\"result\":{\"process_info\":{\"foreground_processes\":[]}}}",
                string.Empty);
        }
    }
}
