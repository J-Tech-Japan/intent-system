using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

[Collection("WorkerNextActionSharedState")]
public sealed class NotifyCollectCommandTests : IDisposable
{
    private static readonly DateTimeOffset FixedNow = new(2026, 8, 29, 18, 0, 0, TimeSpan.Zero);
    private readonly Workspace workspace = new();
    private readonly VirtualWaitClockHarness waitClockHarness = new();

    public NotifyCollectCommandTests()
    {
        NotifyCommand.RoleCollectWaitClockFactory = waitClockHarness.CreateClock;
    }

    public void Dispose()
    {
        NotifyCommand.RoleCollectWaitClockFactory = NotifyCommand.DefaultRoleCollectWaitClockFactory;
        workspace.Dispose();
    }

    [Fact]
    public void RoleCollectUsesReturnedCursorWithoutLossOrDuplicate()
    {
        workspace.AppendEvent("G757-first", "first");
        workspace.AppendEvent("G757-second", "second");

        var first = workspace.Run(RoleCollectArgs());
        Assert.Equal(0, first.ExitCode);
        Assert.Equal("events", first.Result.GetProperty("outcome").GetString());
        Assert.Equal(workspace.ScopedEventPath, first.Result.GetProperty("reader_path").GetString());
        Assert.Equal(
            ["G757-first", "G757-second"],
            EventUnits(first.Result));
        var firstCursor = first.Result.GetProperty("next_cursor").GetString();
        Assert.False(string.IsNullOrWhiteSpace(firstCursor));

        workspace.AppendEvent("G757-third", "third");
        var resumed = workspace.Run(RoleCollectArgs("--since", firstCursor!));
        Assert.Equal(0, resumed.ExitCode);
        Assert.Equal(["G757-third"], EventUnits(resumed.Result));
        var resumedCursor = resumed.Result.GetProperty("next_cursor").GetString();
        Assert.NotEqual(firstCursor, resumedCursor);

        var complete = workspace.Run(RoleCollectArgs("--since", resumedCursor!));
        Assert.Equal(0, complete.ExitCode);
        Assert.Empty(EventUnits(complete.Result));
        Assert.Equal("no-events", complete.Result.GetProperty("cause").GetString());
    }

    [Fact]
    public void UnhonourableCursorIsExplicitAndNeverReset()
    {
        workspace.AppendEvent("G757-existing", "existing");

        var initial = workspace.Run(RoleCollectArgs());
        var cursor = initial.Result.GetProperty("next_cursor").GetString()!;
        File.WriteAllText(workspace.ScopedEventPath, string.Empty);

        var (exitCode, result) = workspace.Run(RoleCollectArgs("--since", cursor));

        Assert.Equal(1, exitCode);
        Assert.Equal("cursor-unhonourable", result.GetProperty("cause").GetString());
        Assert.Equal("error", result.GetProperty("outcome").GetString());
        Assert.Empty(EventUnits(result));
        Assert.Contains("refusing to reset or skip events", result.GetProperty("summary").GetString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// The bounded wait re-reads the reader and returns events a second writer
    /// appended after the first read came back empty. The old background appender
    /// with a 100 ms delay against a 2000 ms timeout did not prove concurrent-append
    /// safety for <see cref="NotifyEventWriter"/>;
    /// that concurrency is untested and out of scope. Driving the append from the
    /// pass-1 sleep callback follows <c>NotifyEventSupervisionG751Tests</c>.
    /// </summary>
    [Fact]
    public void WaitReturnsAfterGenuineSecondWriterAppends()
    {
        waitClockHarness.OnSleepPass = pass =>
        {
            if (pass == 1)
            {
                workspace.AppendEvent("G757-woken", "woken");
            }
        };

        var (exitCode, result) = workspace.Run(RoleCollectArgs("--wait", "--timeout-ms", "2000"));

        Assert.Equal(0, exitCode);
        Assert.Equal("events", result.GetProperty("outcome").GetString());
        Assert.False(result.GetProperty("timed_out").GetBoolean());
        Assert.Equal(["G757-woken"], EventUnits(result));
        Assert.Equal([TimeSpan.FromMilliseconds(25)], waitClockHarness.LastClock!.Sleeps);
        Assert.Equal(1, waitClockHarness.ConstructionCount);
    }

    /// <summary>
    /// At 75 ms the last <c>remaining</c> is 25, so clamped and unclamped sleeps
    /// are both <c>[25, 25, 25]</c> and the clamp is unobservable. At 70 ms the
    /// third pass has <c>remaining = 20</c>: only the clamp produces the 20 ms
    /// sleep and the 70 ms total.
    /// </summary>
    [Fact]
    public void WaitTimeoutIsExplicitNoNewEventsAndNonError()
    {
        workspace.CreateEmptyReader();
        var (exitCode, result) = workspace.Run(RoleCollectArgs("--wait", "--timeout-ms", "70"));

        Assert.Equal(
            [
                TimeSpan.FromMilliseconds(25),
                TimeSpan.FromMilliseconds(25),
                TimeSpan.FromMilliseconds(20),
            ],
            waitClockHarness.LastClock!.Sleeps);
        Assert.Equal(70, waitClockHarness.LastClock!.VirtualElapsedMilliseconds);
        Assert.Equal(0, exitCode);
        Assert.Equal("no-new-events", result.GetProperty("outcome").GetString());
        Assert.Equal("no-new-events", result.GetProperty("cause").GetString());
        Assert.True(result.GetProperty("timed_out").GetBoolean());
        Assert.Empty(EventUnits(result));
    }

    [Fact]
    public void NonWaitMissingReaderIsNoEventsAndReturnsImmediately()
    {
        var (exitCode, result) = workspace.Run(RoleCollectArgs());

        Assert.Equal(0, waitClockHarness.ConstructionCount);
        Assert.Empty(waitClockHarness.LastClock?.Sleeps ?? []);
        Assert.Equal(0, exitCode);
        Assert.Equal("no-events", result.GetProperty("outcome").GetString());
        Assert.Equal("no-events", result.GetProperty("cause").GetString());
        Assert.Empty(EventUnits(result));
        Assert.False(string.IsNullOrWhiteSpace(result.GetProperty("next_cursor").GetString()));

        workspace.AppendEvent("G757-after-missing", "after missing reader");
        var resumed = workspace.Run(RoleCollectArgs(
            "--since", result.GetProperty("next_cursor").GetString()!));
        Assert.Equal(0, resumed.ExitCode);
        Assert.Equal(["G757-after-missing"], EventUnits(resumed.Result));
    }

    [Fact]
    public void DisposeRestoresTheProductionWaitClockFactory()
    {
        using (var probe = new NotifyCollectCommandTests())
        {
        }

        Assert.Same(NotifyCommand.DefaultRoleCollectWaitClockFactory, NotifyCommand.RoleCollectWaitClockFactory);
        Assert.IsType<StopwatchNotifyRoleCollectWaitClock>(NotifyCommand.RoleCollectWaitClockFactory());
    }

    [Fact]
    public void WaitRequiresBoundedTimeout()
    {
        var raw = workspace.RunRaw(
            "notify", "collect", "--domain", Workspace.Domain, "--team", Workspace.Team,
            "--role", Workspace.Role, "--wait", "--format", "json");

        Assert.Equal(1, raw.ExitCode);
        Assert.Contains("--wait requires --timeout-ms", raw.Output, StringComparison.Ordinal);
        Assert.Contains("Usage: intent-cli notify collect", raw.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void TaskIdCollectKeepsLegacyResultShape()
    {
        var raw = workspace.RunRaw(
            "notify", "collect", "--domain", Workspace.Domain, "--team", Workspace.Team,
            "--task-id", "G757-legacy-shape", "--format", "json");

        Assert.Equal(1, raw.ExitCode);
        using var document = JsonDocument.Parse(raw.Output);
        var result = document.RootElement;
        Assert.Equal("collect", result.GetProperty("operation").GetString());
        Assert.Equal("G757-legacy-shape", result.GetProperty("task_id").GetString());
        Assert.Equal("outbox-entry-unavailable", result.GetProperty("cause").GetString());
        Assert.False(result.TryGetProperty("role", out _));
        Assert.False(result.TryGetProperty("events", out _));
    }

    private static string[] EventUnits(JsonElement result) =>
        result.GetProperty("events")
            .EnumerateArray()
            .Select(item => item.GetProperty("unit").GetString()!)
            .ToArray();

    private static string[] RoleCollectArgs(params string[] extra)
    {
        var args = new List<string>
        {
            "notify", "collect", "--domain", Workspace.Domain, "--team", Workspace.Team,
            "--role", Workspace.Role, "--format", "json",
        };
        args.AddRange(extra);
        return args.ToArray();
    }

    private sealed class VirtualWaitClockHarness
    {
        private int constructionCount;
        public Action<int>? OnSleepPass { get; set; }
        public VirtualWaitClock? LastClock { get; private set; }
        public int ConstructionCount => constructionCount;

        public INotifyRoleCollectWaitClock CreateClock()
        {
            constructionCount++;
            LastClock = new VirtualWaitClock(OnSleepPass);
            return LastClock;
        }
    }

    private sealed class VirtualWaitClock : INotifyRoleCollectWaitClock
    {
        private readonly Action<int>? onSleepPass;
        private long virtualElapsedMilliseconds;
        private int sleepPasses;

        public VirtualWaitClock(Action<int>? onSleepPass) => this.onSleepPass = onSleepPass;

        public List<TimeSpan> Sleeps { get; } = [];

        public long VirtualElapsedMilliseconds => virtualElapsedMilliseconds;

        long INotifyRoleCollectWaitClock.ElapsedMilliseconds => virtualElapsedMilliseconds;

        public void Sleep(TimeSpan duration)
        {
            sleepPasses++;
            Sleeps.Add(duration);
            onSleepPass?.Invoke(sleepPasses);
            virtualElapsedMilliseconds += (long)duration.TotalMilliseconds;
        }
    }

    private sealed class Workspace : IDisposable
    {
        public const string Domain = "intent-cli";
        public const string Team = "intent-cli-dev";
        public const string Role = "external-review";

        public Workspace()
        {
            RootPath = Directory.CreateTempSubdirectory("notify-g757-").FullName;
            Directory.CreateDirectory(Path.Combine(RootPath, ".intent-cli"));
            var topologyPath = NotifyRoleTopologyStore.ResolvePath(RootPath, Domain, Team);
            Directory.CreateDirectory(Path.GetDirectoryName(topologyPath)!);
            File.WriteAllText(topologyPath, JsonSerializer.Serialize(new
            {
                domain = Domain,
                team = Team,
                workspace_id = "external-workspace",
                roles = new Dictionary<string, object>
                {
                    [Role] = new
                    {
                        resident = NotifyRecordedRole.ExternalResident,
                        reader = NotifyEventWriter.RelativePathFor(Domain, Team),
                    },
                },
            }));
        }

        public string RootPath { get; }

        public string ScopedEventPath => Path.Combine(
            RootPath,
            NotifyEventWriter.RelativePathFor(Domain, Team).Replace('/', Path.DirectorySeparatorChar));

        public void CreateEmptyReader()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ScopedEventPath)!);
            File.WriteAllText(ScopedEventPath, string.Empty);
        }

        public void AppendEvent(string unit, string summary)
        {
            NotifyEventWriter.Append(ScopedEventPath, new NotifyDesignEvent
            {
                Timestamp = FixedNow,
                Team = Team,
                Kind = "question",
                Unit = unit,
                Summary = summary,
                Artifact = "issue #1645",
            });
        }

        public (int ExitCode, JsonElement Result) Run(string[] args)
        {
            var raw = RunRaw(args);
            using var document = JsonDocument.Parse(raw.Output);
            return (raw.ExitCode, document.RootElement.Clone());
        }

        public (int ExitCode, string Output) RunRaw(params string[] args)
        {
            using var writer = new StringWriter();
            var exitCode = CommandRouter.Execute(args, CreateContext(), writer);
            return (exitCode, writer.ToString());
        }

        private CliContext CreateContext() => new()
        {
            RepoRoot = RootPath,
            Config = new CliConfig
            {
                Project = new ProjectConfig
                {
                    Domain = Domain,
                    ArtifactRoot = ".intent-cli",
                },
            },
        };

        public void Dispose()
        {
            if (Directory.Exists(RootPath))
            {
                Directory.Delete(RootPath, recursive: true);
            }
        }
    }
}
