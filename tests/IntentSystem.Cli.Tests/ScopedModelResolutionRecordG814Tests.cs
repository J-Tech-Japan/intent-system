using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

public sealed class ScopedModelResolutionRecordG814Tests : IDisposable
{
    private readonly string root = Directory.CreateTempSubdirectory("g814-record-").FullName;
    private string HostRoot => Path.Combine(root, "host");
    private string RepoRoot => Path.Combine(root, "repo");
    private NotifyTeamTopology topology = CreateTopology();
    private ScopedModelResolutionTarget target = null!;
    private NotifyTeamTopology? observerTopologySeen;
    private int topologyReads;
    private int observerReads;
    private int appendCalls;
    private string? topologyRootSeen;
    private string? observerRootSeen;
    private string? appendRootSeen;
    private ModelResolutionLedgerEntry? appendedEntry;
    private bool? appendedWrite;
    private Func<ScopedModelResolutionObservedEvidence, ScopedModelResolutionObservedEvidence> evidenceMutation = value => value;
    private ScopedModelResolutionObservationResult? observationOverride;
    private Func<string, ModelResolutionLedgerEntry, bool, ModelResolutionLedgerWriteResult>? appendBehavior;

    [Theory]
    [InlineData("partial")]
    [InlineData("relative-root")]
    [InlineData("duplicate-scope")]
    [InlineData("missing-mode")]
    [InlineData("conflicting-mode")]
    [InlineData("unknown-option")]
    [InlineData("missing-capture")]
    [InlineData("refusal-capture")]
    public void InvalidScopedRecordArgumentsFailBeforeTopologyObserverOrAppend(string variant)
    {
        var args = ValidatedArgs();
        switch (variant)
        {
            case "partial": args = RemovePair(args, "--team"); break;
            case "relative-root": args = ReplaceValue(args, "--routing-root", "relative-host-root"); break;
            case "duplicate-scope": args = [.. args, "--domain", "other-domain"]; break;
            case "missing-mode": args = RemoveFlag(args, "--write"); break;
            case "conflicting-mode": args = [.. args, "--dry-run"]; break;
            case "unknown-option": args = [.. args, "--approve-provider"]; break;
            case "missing-capture": args = RemoveFlag(args, "--capture-target-evidence"); break;
            case "refusal-capture": args = RefusedArgs("not a valid model command", includeCapture: true); break;
        }
        InstallSeams();

        using var writer = new StringWriter();
        Assert.Equal(1, ModelResolutionLedgerCommand.Execute(CreateContext(), ["record", .. args], writer));
        Assert.Equal(0, topologyReads);
        Assert.Equal(0, observerReads);
        Assert.Equal(0, appendCalls);
    }

    [Fact]
    public void VerifiedInformalCaptureCreatesFirstBaselineAtExplicitHostRootWithCompleteEvidence()
    {
        Assert.False(File.Exists(ModelResolutionLedgerStore.ResolvePath(HostRoot)));
        InstallSeams();
        CountAndAppendToStore();

        var result = Run(ValidatedArgs());

        Assert.Equal(0, result.ExitCode);
        using var json = JsonDocument.Parse(result.Text);
        var rootJson = json.RootElement;
        Assert.True(rootJson.GetProperty("applied").GetBoolean());
        Assert.True(rootJson.GetProperty("resolved").GetBoolean());
        Assert.Equal("recorded", rootJson.GetProperty("reason").GetString());
        Assert.Equal("none", rootJson.GetProperty("provider_operation").GetString());
        Assert.Equal(1, topologyReads);
        Assert.Equal(1, observerReads);
        Assert.Same(topology, observerTopologySeen);
        Assert.Equal(Path.GetFullPath(HostRoot), topologyRootSeen);
        Assert.Equal(Path.GetFullPath(HostRoot), observerRootSeen);
        Assert.Equal(Path.GetFullPath(HostRoot), appendRootSeen);
        Assert.NotEqual(Path.GetFullPath(RepoRoot), Path.GetFullPath(HostRoot));
        Assert.Equal(1, appendCalls);
        Assert.True(appendedWrite);
        Assert.NotNull(appendedEntry);
        var entry = appendedEntry!;
        Assert.Equal(1, entry.ScopeVersion);
        Assert.Equal("intent-cli", entry.Domain);
        Assert.Equal("dev", entry.Team);
        Assert.Equal("architect", entry.Role);
        Assert.Equal(new[] { "architect" }, entry.RoleAliases);
        Assert.Equal("workspace-a", entry.WorkspaceId);
        Assert.Equal("workspace-a:p1", entry.PaneId);
        Assert.Equal("informal", entry.RequestForm);
        Assert.Null(entry.RequestedModel);
        Assert.Equal("medium", entry.RequestedEffort);
        Assert.Equal("machine-a", entry.Host);
        Assert.Equal(101, entry.ProcessId);
        Assert.Equal("2026-10-03T12:00:00.0000000Z", entry.ProcessStartTimeUtc);
        Assert.Equal("local-process-start-time", entry.IdentitySource);
        Assert.Equal(target.TopologyDigest, entry.TopologyDigest);
        Assert.Equal(new[] { "claude", "--model", "m-test", "--effort", "medium" }, entry.ObservedArgv);
        Assert.Equal("m-test", entry.ObservedModel);
        Assert.Equal("medium", entry.ObservedEffort);
        Assert.Equal("claude --model m-test --effort medium", entry.FullInvocation);
        Assert.Equal("captured READY banner and running argv", entry.Evidence);
        Assert.True(File.Exists(ModelResolutionLedgerStore.ResolvePath(HostRoot)));
        Assert.True(File.Exists(Path.Combine(Path.GetDirectoryName(ModelResolutionLedgerStore.ResolvePath(HostRoot))!, ".gitignore")));
        Assert.False(File.Exists(ModelResolutionLedgerStore.ResolvePath(RepoRoot)));
    }

    [Theory]
    [InlineData("nonexistent")]
    [InlineData("existing")]
    public void DryRunCapturesEvidenceButPreservesLedgerAndCreatesNoFilesystemState(string state)
    {
        if (state == "existing")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ModelResolutionLedgerStore.ResolvePath(HostRoot))!);
            File.WriteAllText(ModelResolutionLedgerStore.ResolvePath(HostRoot), "operator-owned-bytes\n");
        }
        var before = SnapshotFiles(HostRoot);
        InstallSeams();

        var result = Run(ReplaceFlag(ValidatedArgs(), "--write", "--dry-run"));

        Assert.Equal(0, result.ExitCode);
        using var json = JsonDocument.Parse(result.Text);
        Assert.False(json.RootElement.GetProperty("applied").GetBoolean());
        Assert.True(json.RootElement.GetProperty("resolved").GetBoolean());
        Assert.Equal("dry-run-ready", json.RootElement.GetProperty("reason").GetString());
        Assert.Equal(1, topologyReads);
        Assert.Equal(1, observerReads);
        Assert.Equal(1, appendCalls);
        Assert.False(appendedWrite);
        Assert.Equal(before, SnapshotFiles(HostRoot));
        Assert.False(File.Exists(ModelResolutionLedgerStore.ResolvePath(RepoRoot)));
        Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(ModelResolutionLedgerStore.ResolvePath(HostRoot))!, ".gitignore")));
    }

    [Theory]
    [InlineData("claude --config model=other --model m-test --effort medium")]
    [InlineData("claude --model m-test --effort medium --config model=other")]
    [InlineData("claude --config=model=other --model m-test --effort medium")]
    public void ContradictoryConfigInvocationDoesNotResolveOrAppend(string invocation)
    {
        InstallSeams();

        var result = Run(ReplaceValue(ValidatedArgs(), "--invocation", invocation));

        Assert.Equal(1, result.ExitCode);
        using var json = JsonDocument.Parse(result.Text);
        Assert.Equal("invocation-unreadable", json.RootElement.GetProperty("reason").GetString());
        Assert.Equal(0, topologyReads);
        Assert.Equal(0, observerReads);
        Assert.Equal(0, appendCalls);
    }

    [Fact]
    public void MeasuredBooleanFlagDoesNotBlockStableVerifiedCapture()
    {
        const string invocation = "claude --dangerously-skip-permissions --model m-test --effort medium";
        var observedArgv = new[] { "claude", "--dangerously-skip-permissions", "--model", "m-test", "--effort", "medium" };
        InstallSeams();
        evidenceMutation = value => value with { ObservedArgv = observedArgv };
        CountAndAppendToStore();

        var result = Run(ReplaceValue(ValidatedArgs(), "--invocation", invocation));

        Assert.Equal(0, result.ExitCode);
        using var json = JsonDocument.Parse(result.Text);
        Assert.True(json.RootElement.GetProperty("resolved").GetBoolean());
        Assert.Equal(1, topologyReads);
        Assert.Equal(1, observerReads);
        Assert.Equal(1, appendCalls);
        Assert.Equal(invocation, appendedEntry!.FullInvocation);
        Assert.Equal(observedArgv, appendedEntry.ObservedArgv);
    }

    [Theory]
    [InlineData("invocation")]
    [InlineData("requested-effort")]
    [InlineData("requested-model")]
    [InlineData("declaration")]
    public void InvocationRequestAndDeclarationMismatchesDoNotAppend(string mismatch)
    {
        var args = ValidatedArgs();
        switch (mismatch)
        {
            case "invocation": args = ReplaceValue(args, "--invocation", "claude --model m-other --effort medium"); break;
            case "requested-effort": args = ReplaceValue(args, "--requested-effort", "high"); break;
            case "requested-model": args = [.. args, "--requested-model", "m-requested"]; break;
        }
        InstallSeams();
        if (mismatch == "declaration")
        {
            topology = CreateTopology(model: "m-other");
            target = SelectTarget(topology);
        }

        var result = Run(args);

        Assert.Equal(1, result.ExitCode);
        using var json = JsonDocument.Parse(result.Text);
        Assert.Equal("request-mismatch", json.RootElement.GetProperty("reason").GetString());
        Assert.False(json.RootElement.GetProperty("applied").GetBoolean());
        Assert.Equal(1, topologyReads);
        Assert.Equal(1, observerReads);
        Assert.Equal(0, appendCalls);
    }

    [Theory]
    [InlineData("identity-unavailable")]
    [InlineData("observation-race")]
    public void ObserverFailureAndRaceDoNotAppend(string reason)
    {
        observationOverride = new ScopedModelResolutionObservationResult(
            false, reason, null, null, "synthetic observer failure");
        InstallSeams();

        var result = Run(ValidatedArgs());

        Assert.Equal(1, result.ExitCode);
        using var json = JsonDocument.Parse(result.Text);
        Assert.Equal(reason, json.RootElement.GetProperty("reason").GetString());
        Assert.Equal(0, appendCalls);
    }

    [Theory]
    [InlineData("host")]
    [InlineData("pid")]
    [InlineData("start-time")]
    [InlineData("identity-source")]
    [InlineData("digest")]
    [InlineData("argv")]
    public void IncompleteCapturedIdentityFailsClosedBeforeAppend(string missingField)
    {
        evidenceMutation = missingField switch
        {
            "host" => value => value with { Host = " " },
            "pid" => value => value with { Pid = 0 },
            "start-time" => value => value with { ProcessStartTimeUtc = null! },
            "identity-source" => value => value with { IdentitySource = "fallback" },
            "digest" => value => value with { TopologyDigest = "digest" },
            "argv" => value => value with { ObservedArgv = [] },
            _ => evidenceMutation,
        };
        InstallSeams();

        var result = Run(ValidatedArgs());

        Assert.Equal(1, result.ExitCode);
        using var json = JsonDocument.Parse(result.Text);
        Assert.Equal("identity-unavailable", json.RootElement.GetProperty("reason").GetString());
        Assert.Equal(0, appendCalls);
    }

    [Fact]
    public void RefusedRawInvocationWritesScopedRefusalWithoutObserverOrGenerationFields()
    {
        const string rawInvocation = "claude --model=unmeasured --effort maybe; operator refused";
        InstallSeams();
        CountAndAppendToStore();

        var result = Run(RefusedArgs(rawInvocation));

        Assert.Equal(0, result.ExitCode);
        using var json = JsonDocument.Parse(result.Text);
        var entryJson = json.RootElement.GetProperty("entry");
        Assert.Equal("refused", entryJson.GetProperty("outcome").GetString());
        Assert.Equal(rawInvocation, entryJson.GetProperty("refused_invocation").GetString());
        Assert.Equal("captured provider refusal", entryJson.GetProperty("error_text").GetString());
        Assert.Equal(1, entryJson.GetProperty("scope_version").GetInt32());
        Assert.Equal("informal", entryJson.GetProperty("request_form").GetString());
        Assert.True(entryJson.TryGetProperty("topology_digest", out _));
        Assert.False(entryJson.TryGetProperty("host", out _));
        Assert.False(entryJson.TryGetProperty("pid", out _));
        Assert.False(entryJson.TryGetProperty("process_start_time_utc", out _));
        Assert.False(entryJson.TryGetProperty("identity_source", out _));
        Assert.False(entryJson.TryGetProperty("observed_at", out _));
        Assert.False(entryJson.TryGetProperty("observed_argv", out _));
        Assert.Equal(1, topologyReads);
        Assert.Equal(0, observerReads);
        Assert.Equal(1, appendCalls);
        Assert.True(appendedWrite);
    }

    [Fact]
    public void TargetSelectionFailureAndAppendFailureReturnNonzeroWithoutRetry()
    {
        InstallSeams();
        topology = CreateTopology(includeArchitect: false);
        var targetResult = Run(ValidatedArgs());
        Assert.Equal(1, targetResult.ExitCode);
        using (var json = JsonDocument.Parse(targetResult.Text))
            Assert.Equal("target-absent", json.RootElement.GetProperty("reason").GetString());
        Assert.Equal(0, observerReads);
        Assert.Equal(0, appendCalls);

        InstallSeams();
        appendBehavior = (path, _, _) => new ModelResolutionLedgerWriteResult(false, path, "synthetic append failure");
        var appendResult = Run(ValidatedArgs());
        Assert.Equal(1, appendResult.ExitCode);
        using var appendJson = JsonDocument.Parse(appendResult.Text);
        Assert.False(appendJson.RootElement.GetProperty("applied").GetBoolean());
        Assert.Equal("append-failed", appendJson.RootElement.GetProperty("reason").GetString());
        Assert.Equal(1, observerReads);
        Assert.Equal(1, appendCalls);
    }

    private (int ExitCode, string Text) Run(string[] args)
    {
        using var writer = new StringWriter();
        var exitCode = ModelResolutionLedgerCommand.Execute(CreateContext(), ["record", .. args], writer);
        return (exitCode, writer.ToString());
    }

    private void InstallSeams()
    {
        topologyReads = observerReads = appendCalls = 0;
        topologyRootSeen = observerRootSeen = appendRootSeen = null;
        observerTopologySeen = null;
        appendedEntry = null;
        appendedWrite = null;
        appendBehavior = null;
        topology = CreateTopology();
        var selection = ScopedModelResolutionTargetSelector.Select(topology, "intent-cli", "dev", "architect", "claude");
        Assert.True(selection.Resolved, selection.Detail);
        target = selection.Target!;
        ScopedModelResolutionRecord.TopologyReadOverride = (routingRoot, domain, team) =>
        {
            topologyReads++;
            topologyRootSeen = routingRoot;
            Assert.Equal("intent-cli", domain);
            Assert.Equal("dev", team);
            return new NotifyTopologyResolution { Resolved = true, Topology = topology, Summary = "synthetic T1" };
        };
        ScopedModelResolutionRecord.ObserverOverride = (routingRoot, initial, domain, team, role, kind) =>
        {
            observerReads++;
            observerRootSeen = routingRoot;
            observerTopologySeen = initial;
            Assert.Same(topology, initial);
            Assert.Equal("intent-cli", domain);
            Assert.Equal("dev", team);
            Assert.Equal("architect", role);
            Assert.Equal("claude", kind);
            if (observationOverride is not null) return Task.FromResult(observationOverride);
            var evidence = evidenceMutation(new ScopedModelResolutionObservedEvidence(
                "machine-a", 101, "2026-10-03T12:00:00.0000000Z", "local-process-start-time",
                target.TopologyDigest, target.RoleAliases,
                ["claude", "--model", "m-test", "--effort", "medium"], "m-test", "medium"));
            return Task.FromResult(new ScopedModelResolutionObservationResult(
                true, "target-current", target, evidence, null));
        };
        ScopedModelResolutionRecord.AppendOverride = (routingRoot, entry, write) =>
        {
            appendCalls++;
            appendRootSeen = routingRoot;
            appendedEntry = entry;
            appendedWrite = write;
            return appendBehavior?.Invoke(routingRoot, entry, write)
                ?? ModelResolutionLedgerStore.Append(routingRoot, entry, write);
        };
        ScopedModelResolutionRecord.UtcNowOverride = () => new DateTimeOffset(2026, 10, 3, 12, 5, 0, TimeSpan.Zero);
    }

    private void CountAndAppendToStore() => appendBehavior =
        (routingRoot, entry, write) => ModelResolutionLedgerStore.Append(routingRoot, entry, write);

    private string[] ValidatedArgs() =>
    [
        "--routing-root", HostRoot,
        "--domain", "intent-cli",
        "--team", "dev",
        "--role", "architect",
        "--kind", "claude",
        "--informal-name", "fixture medium",
        "--requested-effort", "medium",
        "--outcome", "verified",
        "--invocation", "claude --model m-test --effort medium",
        "--evidence", "captured READY banner and running argv",
        "--capture-target-evidence",
        "--write",
        "--format", "json",
    ];

    private string[] RefusedArgs(string rawInvocation, bool includeCapture = false) =>
    [
        "--routing-root", HostRoot,
        "--domain", "intent-cli",
        "--team", "dev",
        "--role", "architect",
        "--kind", "claude",
        "--informal-name", "fixture medium",
        "--requested-effort", "medium",
        "--outcome", "refused",
        "--invocation", rawInvocation,
        "--error", "captured provider refusal",
        .. (includeCapture ? new[] { "--capture-target-evidence" } : []),
        "--write",
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

    private static NotifyTeamTopology CreateTopology(
        string? model = "m-test",
        bool includeArchitect = true) => new(
        "synthetic-topology",
        "intent-cli",
        "dev",
        "workspace-a",
        includeArchitect
            ? new Dictionary<string, NotifyRecordedRole>(StringComparer.Ordinal)
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
                    Model: model,
                    ReasoningEffort: "medium"),
            }
            : new Dictionary<string, NotifyRecordedRole>(StringComparer.Ordinal),
        new Dictionary<string, AgentLaunchEnvelopeProfile>(StringComparer.Ordinal));

    private static string[] RemovePair(string[] args, string option)
    {
        var index = Array.IndexOf(args, option);
        return [.. args[..index], .. args[(index + 2)..]];
    }

    private static string[] RemoveFlag(string[] args, string flag)
    {
        var index = Array.IndexOf(args, flag);
        return [.. args[..index], .. args[(index + 1)..]];
    }

    private static string[] ReplaceValue(string[] args, string option, string replacement)
    {
        var index = Array.IndexOf(args, option);
        var copy = args.ToArray();
        copy[index + 1] = replacement;
        return copy;
    }

    private static string[] ReplaceFlag(string[] args, string oldFlag, string newFlag)
    {
        var index = Array.IndexOf(args, oldFlag);
        var copy = args.ToArray();
        copy[index] = newFlag;
        return copy;
    }

    private static ScopedModelResolutionTarget SelectTarget(NotifyTeamTopology currentTopology)
    {
        var selected = ScopedModelResolutionTargetSelector.Select(currentTopology, "intent-cli", "dev", "architect", "claude");
        Assert.True(selected.Resolved, selected.Detail);
        return selected.Target!;
    }

    private static IReadOnlyDictionary<string, string> SnapshotFiles(string directory)
    {
        if (!Directory.Exists(directory)) return new Dictionary<string, string>(StringComparer.Ordinal);
        return Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
            .ToDictionary(path => Path.GetRelativePath(directory, path), File.ReadAllText, StringComparer.Ordinal);
    }

    public void Dispose()
    {
        ScopedModelResolutionRecord.TopologyReadOverride = null;
        ScopedModelResolutionRecord.ObserverOverride = null;
        ScopedModelResolutionRecord.AppendOverride = null;
        ScopedModelResolutionRecord.UtcNowOverride = null;
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }
}
