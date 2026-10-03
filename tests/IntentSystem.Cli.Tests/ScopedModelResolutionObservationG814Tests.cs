using System.Text.Json;
using IntentSystem.Cli.Commands;

namespace IntentSystem.Cli.Tests;

public sealed class ScopedModelResolutionObservationG814Tests
{
    private const string Domain = "intent-cli";
    private const string Team = "dev";
    private const string Role = "architect";
    private const string Pane = "workspace-a:p1";

    [Fact]
    public async Task MixedTypeForegroundArgv_FailsBeforeLocalProcessRead()
    {
        var events = new List<string> { "T1" };
        var runner = new ObservationRunner(events,
            Agents(),
            ProcessInfo(101, ["claude", "--model", "m-test", 17, "--effort", "medium"]));
        var identityReads = 0;
        var observer = CreateObserver(events, runner, _ =>
        {
            identityReads++;
            return Identity(101);
        });

        var result = await Observe(observer, Topology());

        Assert.False(result.Resolved);
        Assert.Equal("argv-unreadable", result.Reason);
        Assert.Equal(0, identityReads);
        Assert.Equal(new[] { "T1", "A1", "P1" }, events);
    }

    [Fact]
    public async Task DuplicateExactRegistration_FailsBeforePaneProcessInfo()
    {
        var events = new List<string> { "T1" };
        var duplicate = """
            {"result":{"agents":[
              {"workspace_id":"workspace-a","pane_id":"workspace-a:p1","agent":"claude","agent_session":{},"interactive_ready":true,"agent_status":"running"},
              {"workspace_id":"workspace-a","pane_id":"workspace-a:p1","agent":"codex","interactive_ready":false,"agent_status":"unknown"}
            ]}}
            """;
        var runner = new ObservationRunner(events, duplicate);
        var identityReads = 0;
        var observer = CreateObserver(events, runner, _ =>
        {
            identityReads++;
            return Identity(101);
        });

        var result = await Observe(observer, Topology());

        Assert.False(result.Resolved);
        Assert.Equal("target-ambiguous", result.Reason);
        Assert.Equal(0, identityReads);
        Assert.Equal(new[] { "T1", "A1" }, events);
    }

    [Fact]
    public async Task ChangedSecondPid_StopsBeforeReadingItsStartTimeOrFinalTopology()
    {
        var events = new List<string> { "T1" };
        var runner = new ObservationRunner(events,
            Agents(), ProcessInfo(101, ModelArgv()),
            Agents(), ProcessInfo(202, ModelArgv()));
        var identityReads = new List<long>();
        var observer = CreateObserver(events, runner, pid =>
        {
            identityReads.Add(pid);
            return Identity(pid);
        });

        var result = await Observe(observer, Topology());

        Assert.False(result.Resolved);
        Assert.Equal("observation-race", result.Reason);
        Assert.Equal(new long[] { 101 }, identityReads);
        Assert.Equal(new[] { "T1", "A1", "P1", "O1:101", "A2", "P2" }, events);
    }

    [Fact]
    public async Task ChangedCompleteArgv_StopsBeforeSecondStartTimeRead()
    {
        var events = new List<string> { "T1" };
        var runner = new ObservationRunner(events,
            Agents(), ProcessInfo(101, ModelArgv()),
            Agents(), ProcessInfo(101, ["claude", "--model", "m-test", "--effort", "high"]));
        var identityReads = 0;
        var observer = CreateObserver(events, runner, pid =>
        {
            identityReads++;
            return Identity(pid);
        });

        var result = await Observe(observer, Topology());

        Assert.False(result.Resolved);
        Assert.Equal("observation-race", result.Reason);
        Assert.Equal(1, identityReads);
        Assert.Equal(new[] { "T1", "A1", "P1", "O1:101", "A2", "P2" }, events);
    }

    [Fact]
    public async Task FinalSelectedDigestRace_FailsAfterBoundedStableProcessReads()
    {
        var events = new List<string> { "T1" };
        var runner = new ObservationRunner(events,
            Agents(), ProcessInfo(101, ModelArgv()),
            Agents(), ProcessInfo(101, ModelArgv()));
        var identityReads = 0;
        var observer = CreateObserver(events, runner, pid =>
        {
            identityReads++;
            return Identity(pid);
        }, Topology(model: "declared-after-race"));

        var result = await Observe(observer, Topology());

        Assert.False(result.Resolved);
        Assert.Equal("topology-mismatch", result.Reason);
        Assert.Equal(2, identityReads);
        Assert.Equal(new[] { "T1", "A1", "P1", "O1:101", "A2", "P2", "O2:101", "T2" }, events);
    }

    [Theory]
    [InlineData("different-host", false)]
    [InlineData("fixture-host", true)]
    public async Task SecondLocalIdentityMismatch_FailsBeforeFinalTopology(string secondHost, bool changeStartTime)
    {
        var events = new List<string> { "T1" };
        var runner = new ObservationRunner(events,
            Agents(), ProcessInfo(101, ModelArgv()),
            Agents(), ProcessInfo(101, ModelArgv()));
        var identityReads = 0;
        var observer = CreateObserver(events, runner, pid =>
        {
            identityReads++;
            var startTime = changeStartTime && identityReads > 1
                ? new DateTime(2026, 10, 3, 12, 30, 1, DateTimeKind.Utc)
                : new DateTime(2026, 10, 3, 12, 30, 0, DateTimeKind.Utc);
            return new ScopedLocalProcessIdentityReadResult(true, pid,
                identityReads == 1 ? "fixture-host" : secondHost, startTime, false, null);
        });

        var result = await Observe(observer, Topology());

        Assert.False(result.Resolved);
        Assert.Equal("observation-race", result.Reason);
        Assert.Equal(2, identityReads);
        Assert.DoesNotContain("T2", events);
    }

    [Fact]
    public async Task StableTarget_UsesExactMaximumObservationOrderAndReturnsLedgerEvidence()
    {
        var events = new List<string> { "T1" };
        var runner = new ObservationRunner(events,
            Agents(), ProcessInfo(101, ModelArgv()),
            Agents(), ProcessInfo(101, ModelArgv()));
        var identityReads = 0;
        var observer = CreateObserver(events, runner, pid =>
        {
            identityReads++;
            return Identity(pid);
        }, Topology());

        var result = await Observe(observer, Topology());

        Assert.True(result.Resolved, result.Detail);
        Assert.Equal("target-current", result.Reason);
        Assert.Equal(new[] { "T1", "A1", "P1", "O1:101", "A2", "P2", "O2:101", "T2" }, events);
        Assert.Equal(2, events.Count(item => item is "A1" or "A2"));
        Assert.Equal(2, events.Count(item => item is "P1" or "P2"));
        Assert.Equal(2, identityReads);
        Assert.Equal(4, runner.ReadCalls);
        Assert.Equal(new[] { Pane, Pane }, runner.PaneIds);
        Assert.Equal(new[] { "agent", "list" }, runner.Arguments[0]);
        Assert.All(runner.Arguments.Where(args => args.Count > 0 && args[0] == "pane"),
            args => Assert.Equal(new[] { "pane", "process-info", "--pane", Pane }, args));
        Assert.Equal(2, events.Count(item => item == "T1" || item == "T2"));

        var evidence = Assert.IsType<ScopedModelResolutionObservedEvidence>(result.Evidence);
        Assert.Equal(101, evidence.Pid);
        Assert.Equal("fixture-host", evidence.Host);
        Assert.Equal("local-process-start-time", evidence.IdentitySource);
        Assert.Equal(new[] { "claude", "--model", "m-test", "--effort", "medium" }, evidence.ObservedArgv);
        Assert.Equal("m-test", evidence.ObservedModel);
        Assert.Equal("medium", evidence.ObservedEffort);
        Assert.Equal(64, evidence.TopologyDigest.Length);
        Assert.EndsWith("Z", evidence.ProcessStartTimeUtc, StringComparison.Ordinal);
    }

    [Fact]
    public void StrictProviderParser_RejectsDuplicateForegroundProviderAndNonArrayArgv()
    {
        var duplicate = """
            {"result":{"process_info":{"foreground_processes":[
              {"pid":101,"argv":["claude","--model","m-test","--effort","medium"]},
              {"pid":202,"argv":["claude","--model","m-other","--effort","high"]}
            ]}}}
            """;
        var ambiguous = ScopedModelResolutionObservationParser.ParseProvider(duplicate, "claude");
        Assert.False(ambiguous.Resolved);
        Assert.Equal("target-ambiguous", ambiguous.Reason);

        var malformed = ScopedModelResolutionObservationParser.ParseProvider(
            "{\"result\":{\"process_info\":{\"foreground_processes\":[{\"pid\":101,\"argv\":\"claude --model m-test\"}]}}}",
            "claude");
        Assert.False(malformed.Resolved);
        Assert.Equal("argv-unreadable", malformed.Reason);
    }

    private static ScopedModelResolutionTargetObserver CreateObserver(
        List<string> events,
        ObservationRunner runner,
        Func<long, ScopedLocalProcessIdentityReadResult> identityReader,
        NotifyTeamTopology? finalTopology = null)
    {
        var after = finalTopology ?? Topology();
        var identityCall = 0;
        return new ScopedModelResolutionTargetObserver(
            (_, _, _) =>
            {
                events.Add("T2");
                return new NotifyTopologyResolution
                {
                    Resolved = true,
                    Topology = after,
                    Summary = "synthetic selected topology",
                };
            },
            runner,
            pid =>
            {
                events.Add($"O{++identityCall}:{pid}");
                return Task.FromResult(identityReader(pid));
            },
            TimeSpan.FromSeconds(1));
    }

    private static Task<ScopedModelResolutionObservationResult> Observe(
        ScopedModelResolutionTargetObserver observer,
        NotifyTeamTopology topology) =>
        observer.ObserveStableTarget("/synthetic/routing-root", topology, Domain, Team, Role, "claude");

    private static ScopedLocalProcessIdentityReadResult Identity(long pid) =>
        new(true, pid, "fixture-host", new DateTime(2026, 10, 3, 12, 30, 0, DateTimeKind.Utc), false, null);

    private static string Agents() => """
        {"result":{"agents":[
          {"workspace_id":"workspace-b","pane_id":"workspace-a:p1","agent":"claude","agent_session":{},"interactive_ready":true,"agent_status":"running"},
          {"workspace_id":"workspace-a","pane_id":"workspace-a:p2","agent":"codex","agent_session":{},"interactive_ready":true,"agent_status":"running"},
          {"workspace_id":"workspace-a","pane_id":"workspace-a:p1","agent":"claude","agent_session":{},"interactive_ready":true,"agent_status":"running"}
        ]}}
        """;

    private static IReadOnlyList<string> ModelArgv() =>
        ["claude", "--model", "m-test", "--effort", "medium"];

    private static string ProcessInfo(long pid, IReadOnlyList<object> argv) =>
        JsonSerializer.Serialize(new
        {
            result = new
            {
                process_info = new
                {
                    foreground_processes = new[] { new { pid, argv } },
                },
            },
        });

    private static NotifyTeamTopology Topology(string? model = null) => new(
        "synthetic-topology.json",
        Domain,
        Team,
        "workspace-a",
        new Dictionary<string, NotifyRecordedRole>(StringComparer.Ordinal)
        {
            [Role] = new NotifyRecordedRole(
                NotifyRecordedRole.HerdrResident,
                "workspace-a",
                Pane,
                null,
                null,
                "claude",
                null,
                null,
                Model: model),
        },
        new Dictionary<string, AgentLaunchEnvelopeProfile>(StringComparer.Ordinal));

    private sealed class ObservationRunner : INotifyProcessRunner
    {
        private readonly Queue<NotifyProcessResult> responses;
        private readonly List<string> events;

        public ObservationRunner(List<string> events, params string[] responses)
        {
            this.events = events;
            this.responses = new Queue<NotifyProcessResult>(responses.Select(output => new NotifyProcessResult(0, output, string.Empty)));
        }

        public int ReadCalls { get; private set; }
        public List<IReadOnlyList<string>> Arguments { get; } = [];
        public List<string> PaneIds { get; } = [];

        public NotifyProcessResult Run(string fileName, IReadOnlyList<string> arguments) =>
            throw new NotSupportedException("The observer must use the bounded async runner path.");

        public Task<NotifyProcessResult> RunAsync(
            string fileName,
            IReadOnlyList<string> arguments,
            CancellationToken cancellationToken)
        {
            ReadCalls++;
            Arguments.Add(arguments.ToArray());
            if (arguments.SequenceEqual(["agent", "list"], StringComparer.Ordinal))
                events.Add(ReadCalls == 1 ? "A1" : "A2");
            else if (arguments.Count == 4 && arguments[0] == "pane")
            {
                events.Add(ReadCalls == 2 ? "P1" : "P2");
                PaneIds.Add(arguments[3]);
            }
            else
                throw new InvalidOperationException("Unexpected observation command.");
            return Task.FromResult(responses.Dequeue());
        }
    }
}
