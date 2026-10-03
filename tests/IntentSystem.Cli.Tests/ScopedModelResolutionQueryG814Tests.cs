using System.Globalization;
using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

public sealed class ScopedModelResolutionQueryG814Tests : IDisposable
{
    private readonly string root = Directory.CreateTempSubdirectory("g814-query-").FullName;
    private string HostRoot => Path.Combine(root, "host");
    private string RepoRoot => Path.Combine(root, "repo");
    private NotifyTeamTopology topology = CreateTopology();
    private ScopedModelResolutionTarget target = null!;
    private int ledgerReads;
    private int topologyReads;
    private int observerReads;
    private string? ledgerRootSeen;
    private string? topologyRootSeen;
    private string? observerRootSeen;
    private NotifyTeamTopology? observerTopologySeen;
    private IReadOnlyList<ModelResolutionLedgerEntry> entries = [];
    private Func<ScopedModelResolutionObservedEvidence, ScopedModelResolutionObservedEvidence> evidenceMutation = value => value;

    [Theory]
    [InlineData("--partial")]
    [InlineData("--relative")]
    [InlineData("--unsupported-kind")]
    [InlineData("--duplicate-domain")]
    [InlineData("--invalid-candidate")]
    [InlineData("--write")]
    public void InvalidScopedArgumentsFailBeforeLedgerTopologyOrObserver(string variant)
    {
        var args = BaseArgs();
        switch (variant)
        {
            case "--partial": args = RemovePair(args, "--team"); break;
            case "--relative": args = ReplaceValue(args, "--routing-root", "relative-root"); break;
            case "--unsupported-kind": args = ReplaceValue(args, "--kind", "cursor"); break;
            case "--duplicate-domain": args = [.. args, "--domain", "other-domain"]; break;
            case "--invalid-candidate": args = [.. args, "--candidate-invocation", "claude --model=m-test --effort medium"]; break;
            case "--write": args = [.. args, "--write"]; break;
        }

        InstallSeams([]);
        using var writer = new StringWriter();
        Assert.Equal(1, ModelResolutionLedgerCommand.Execute(CreateContext(), ["query", .. args], writer));
        Assert.Equal(0, ledgerReads);
        Assert.Equal(0, topologyReads);
        Assert.Equal(0, observerReads);
    }

    [Fact]
    public void MissingBaselineReadsOneTopologyButNeverStartsObserver()
    {
        InstallSeams([]);

        var result = Run();

        Assert.Equal("missing-scoped-baseline", result.RootElement.GetProperty("reason").GetString());
        Assert.False(result.RootElement.GetProperty("resolved").GetBoolean());
        Assert.True(result.RootElement.GetProperty("human_required").GetBoolean());
        Assert.Equal(1, ledgerReads);
        Assert.Equal(1, topologyReads);
        Assert.Equal(0, observerReads);
    }

    [Fact]
    public void NewestIncompleteBaselineDoesNotFallBackToOlderCompleteBaseline()
    {
        var older = CompleteBaseline();
        var newer = older with
        {
            RecordedAt = older.RecordedAt.AddMinutes(1),
            ObservedArgv = null,
        };
        InstallSeams([older, newer]);

        var result = Run();

        Assert.Equal("identity-unavailable", result.RootElement.GetProperty("reason").GetString());
        Assert.Equal(0, observerReads);
    }

    [Fact]
    public void RefusedOptionalCandidateStopsBeforeObserver()
    {
        const string candidate = "claude --model m-candidate --effort high";
        var baseline = CompleteBaseline();
        var refusal = baseline with
        {
            Outcome = ModelResolutionLedgerCommand.RefusedOutcome,
            FullInvocation = null,
            Evidence = null,
            RefusedInvocation = candidate,
            ErrorText = "captured refusal",
            RecordedAt = baseline.RecordedAt.AddMinutes(1),
        };
        InstallSeams([baseline, refusal]);

        var result = Run("--candidate-invocation", candidate);

        Assert.Equal("refused-invocation", result.RootElement.GetProperty("reason").GetString());
        Assert.Equal(0, observerReads);
        Assert.Equal(1, topologyReads);
    }

    [Fact]
    public void StableScopedQueryUsesExactInitialTopologyAndReturnsPositiveEvidence()
    {
        InstallSeams([CompleteBaseline()]);

        var result = Run();

        Assert.Equal("target-current", result.RootElement.GetProperty("reason").GetString());
        Assert.True(result.RootElement.GetProperty("resolved").GetBoolean());
        Assert.False(result.RootElement.GetProperty("human_required").GetBoolean());
        Assert.Equal("use-scoped-model-effort", result.RootElement.GetProperty("next_step").GetString());
        Assert.Equal("none", result.RootElement.GetProperty("provider_operation").GetString());
        Assert.False(result.RootElement.TryGetProperty("live_argv_fallback", out _));
        Assert.Same(topology, observerTopologySeen);
        Assert.Equal(Path.GetFullPath(HostRoot), ledgerRootSeen);
        Assert.Equal(Path.GetFullPath(HostRoot), topologyRootSeen);
        Assert.Equal(Path.GetFullPath(HostRoot), observerRootSeen);
        Assert.Equal(1, ledgerReads);
        Assert.Equal(1, topologyReads);
        Assert.Equal(1, observerReads);
    }

    [Theory]
    [InlineData("host-mismatch")]
    [InlineData("generation-mismatch")]
    [InlineData("starttime-mismatch")]
    [InlineData("topology-mismatch")]
    [InlineData("request-mismatch")]
    public void CurrentEvidenceMismatchReturnsHumanRequiredWithObservedEvidence(string reason)
    {
        evidenceMutation = reason switch
        {
            "host-mismatch" => value => value with { Host = "another-host" },
            "generation-mismatch" => value => value with { Pid = value.Pid + 1 },
            "starttime-mismatch" => value => value with { ProcessStartTimeUtc = "2026-10-03T12:00:01.0000000Z" },
            "topology-mismatch" => value => value with { TopologyDigest = new string('a', 64) },
            "request-mismatch" => value => value with { ObservedModel = "m-other" },
            _ => evidenceMutation,
        };
        InstallSeams([CompleteBaseline()]);

        var result = Run();

        Assert.Equal(reason == "starttime-mismatch" ? "generation-mismatch" : reason,
            result.RootElement.GetProperty("reason").GetString());
        Assert.False(result.RootElement.GetProperty("resolved").GetBoolean());
        Assert.True(result.RootElement.GetProperty("human_required").GetBoolean());
        Assert.Equal("ask-human", result.RootElement.GetProperty("next_step").GetString());
        Assert.Equal("none", result.RootElement.GetProperty("provider_operation").GetString());
        Assert.True(result.RootElement.TryGetProperty("observed_evidence", out _));
        Assert.Equal(1, observerReads);
        Assert.Same(topology, observerTopologySeen);
    }

    [Fact]
    public void ExplicitHostRootIsReadOnlyAndJsonAndMarkdownShareEvidence()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ModelResolutionLedgerStore.ResolvePath(HostRoot))!);
        var ledgerPath = ModelResolutionLedgerStore.ResolvePath(HostRoot);
        var ledgerBytes = JsonSerializer.Serialize(CompleteBaseline(), ModelResolutionLedgerStore.JsonOptions) + Environment.NewLine;
        File.WriteAllText(ledgerPath, ledgerBytes);
        var beforeHostFiles = SnapshotFiles(HostRoot);
        var beforeRepoFiles = SnapshotFiles(RepoRoot);
        InstallSeams([]);
        ScopedModelResolutionQuery.LedgerReadOverride = null;

        var json = Run();
        var markdown = Run("--format", "markdown");

        Assert.Equal("target-current", json.RootElement.GetProperty("reason").GetString());
        Assert.Contains("reason: **target-current**", markdown.Text, StringComparison.Ordinal);
        Assert.Contains("scope: `intent-cli/dev/architect`", markdown.Text, StringComparison.Ordinal);
        Assert.Contains("request: `informal` informal-name `fixture medium`", markdown.Text, StringComparison.Ordinal);
        Assert.Contains("observed evidence: host `machine-a`, pid `101`", markdown.Text, StringComparison.Ordinal);
        Assert.Contains("observed model/effort: `m-test` / `medium`", markdown.Text, StringComparison.Ordinal);
        Assert.Contains("observed argv: `claude --model m-test --effort medium`", markdown.Text, StringComparison.Ordinal);
        Assert.Equal(Path.GetFullPath(HostRoot), topologyRootSeen);
        Assert.Equal(Path.GetFullPath(HostRoot), observerRootSeen);
        Assert.Equal(beforeHostFiles, SnapshotFiles(HostRoot));
        Assert.Equal(beforeRepoFiles, SnapshotFiles(RepoRoot));
        Assert.Equal(ledgerBytes, File.ReadAllText(ledgerPath));
        Assert.False(File.Exists(ModelResolutionLedgerStore.ResolvePath(RepoRoot)));
        Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(ledgerPath)!, ".gitignore")));
    }

    private (JsonElement RootElement, string Text) Run(params string[] extraArgs)
    {
        using var writer = new StringWriter();
        var args = BaseArgs();
        if (extraArgs.Length > 0)
        {
            args = RemovePair(args, "--format");
            args = [.. args, .. extraArgs];
            if (!extraArgs.Contains("--format", StringComparer.Ordinal))
                args = [.. args, "--format", "json"];
        }
        Assert.Equal(0, ModelResolutionLedgerCommand.Execute(CreateContext(), ["query", .. args], writer));
        var output = writer.ToString();
        if (args[^1] == "markdown")
        {
            using var empty = JsonDocument.Parse("{}");
            return (empty.RootElement.Clone(), output);
        }
        using var document = JsonDocument.Parse(output);
        return (document.RootElement.Clone(), output);
    }

    private void InstallSeams(IReadOnlyList<ModelResolutionLedgerEntry> ledgerEntries)
    {
        entries = ledgerEntries;
        ledgerReads = 0;
        topologyReads = 0;
        observerReads = 0;
        ledgerRootSeen = topologyRootSeen = observerRootSeen = null;
        observerTopologySeen = null;
        topology = CreateTopology();
        var selected = ScopedModelResolutionTargetSelector.Select(topology, "intent-cli", "dev", "architect", "claude");
        Assert.True(selected.Resolved, selected.Detail);
        target = selected.Target!;
        ScopedModelResolutionQuery.LedgerReadOverride = routingRoot =>
        {
            ledgerReads++;
            ledgerRootSeen = routingRoot;
            return new ModelResolutionLedgerReadResult
            {
                Resolved = true,
                Path = ModelResolutionLedgerStore.ResolvePath(routingRoot),
                Entries = entries,
            };
        };
        ScopedModelResolutionQuery.TopologyReadOverride = (routingRoot, domain, team) =>
        {
            topologyReads++;
            topologyRootSeen = routingRoot;
            Assert.Equal("intent-cli", domain);
            Assert.Equal("dev", team);
            return new NotifyTopologyResolution { Resolved = true, Topology = topology, Summary = "synthetic T1" };
        };
        ScopedModelResolutionQuery.ObserverOverride = (routingRoot, initial, domain, team, role, kind) =>
        {
            observerReads++;
            observerRootSeen = routingRoot;
            observerTopologySeen = initial;
            Assert.Equal("intent-cli", domain);
            Assert.Equal("dev", team);
            Assert.Equal("architect", role);
            Assert.Equal("claude", kind);
            var evidence = evidenceMutation(new ScopedModelResolutionObservedEvidence(
                "machine-a",
                101,
                UtcString,
                "local-process-start-time",
                target.TopologyDigest,
                target.RoleAliases,
                ["claude", "--model", "m-test", "--effort", "medium"],
                "m-test",
                "medium"));
            return Task.FromResult(new ScopedModelResolutionObservationResult(
                true, "target-current", target, evidence, null));
        };
    }

    private ModelResolutionLedgerEntry CompleteBaseline()
    {
        target ??= SelectTarget(topology);
        return new ModelResolutionLedgerEntry
        {
            InformalName = "fixture medium",
            Kind = "claude",
            Outcome = ModelResolutionLedgerCommand.VerifiedOutcome,
            FullInvocation = "claude --model m-test --effort medium",
            Evidence = "captured READY banner and running argv",
            RecordedAt = DateTimeOffset.Parse("2026-10-03T12:00:00.0000000Z", CultureInfo.InvariantCulture),
            ScopeVersion = 1,
            Domain = target.Domain,
            Team = target.Team,
            Role = target.Role,
            RoleAliases = target.RoleAliases,
            WorkspaceId = target.WorkspaceId,
            PaneId = target.PaneId,
            RequestForm = "informal",
            RequestedEffort = "medium",
            Host = "machine-a",
            ProcessId = 101,
            ProcessStartTimeUtc = UtcString,
            IdentitySource = "local-process-start-time",
            TopologyDigest = target.TopologyDigest,
            ObservedAt = DateTimeOffset.Parse("2026-10-03T12:01:00.0000000Z", CultureInfo.InvariantCulture),
            ObservedArgv = ["claude", "--model", "m-test", "--effort", "medium"],
            ObservedModel = "m-test",
            ObservedEffort = "medium",
        };
    }

    private string[] BaseArgs() =>
    [
        "--routing-root", HostRoot,
        "--domain", "intent-cli",
        "--team", "dev",
        "--role", "architect",
        "--kind", "claude",
        "--informal-name", "fixture medium",
        "--requested-effort", "medium",
        "--format", "json",
    ];

    private CliContext CreateContext() => new()
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

    private static NotifyTeamTopology CreateTopology() => new(
        "synthetic-topology",
        "intent-cli",
        "dev",
        "workspace-a",
        new Dictionary<string, NotifyRecordedRole>(StringComparer.Ordinal)
        {
            ["architect"] = new NotifyRecordedRole(
                NotifyRecordedRole.HerdrResident,
                "workspace-a",
                "workspace-a:p1",
                null,
                null,
                "claude",
                null,
                null,
                Model: "m-test",
                ReasoningEffort: "medium"),
        },
        new Dictionary<string, AgentLaunchEnvelopeProfile>(StringComparer.Ordinal));

    private static ScopedModelResolutionTarget SelectTarget(NotifyTeamTopology currentTopology)
    {
        var selected = ScopedModelResolutionTargetSelector.Select(currentTopology, "intent-cli", "dev", "architect", "claude");
        Assert.True(selected.Resolved, selected.Detail);
        return selected.Target!;
    }

    private static string UtcString => "2026-10-03T12:00:00.0000000Z";

    private static string[] RemovePair(string[] args, string option)
    {
        var index = Array.IndexOf(args, option);
        return [.. args[..index], .. args[(index + 2)..]];
    }

    private static string[] ReplaceValue(string[] args, string option, string replacement)
    {
        var index = Array.IndexOf(args, option);
        var copy = args.ToArray();
        copy[index + 1] = replacement;
        return copy;
    }

    private static IReadOnlyDictionary<string, string> SnapshotFiles(string directory)
    {
        if (!Directory.Exists(directory)) return new Dictionary<string, string>(StringComparer.Ordinal);
        return Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
            .ToDictionary(path => Path.GetRelativePath(directory, path), File.ReadAllText, StringComparer.Ordinal);
    }

    public void Dispose()
    {
        ScopedModelResolutionQuery.LedgerReadOverride = null;
        ScopedModelResolutionQuery.TopologyReadOverride = null;
        ScopedModelResolutionQuery.ObserverOverride = null;
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }
}
