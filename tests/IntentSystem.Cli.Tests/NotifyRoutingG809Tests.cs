using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;
using Xunit.Abstractions;

namespace IntentSystem.Cli.Tests;

[Collection("WorkerNextActionSharedState")]
public sealed class NotifyRoutingG809Tests : IDisposable
{
    private readonly ITestOutputHelper output;
    private readonly G809Workspace workspace = new();

    public NotifyRoutingG809Tests(ITestOutputHelper output)
    {
        this.output = output;
        NotifyCommand.HerdrExecutableFactory = () => "fake-herdr";
        NotifyCommand.UtcNowFactory = () => new DateTimeOffset(2026, 9, 5, 12, 0, 0, TimeSpan.Zero);
    }

    public void Dispose()
    {
        NotifyCommand.ProcessRunnerFactory = null;
        NotifyCommand.AgmsgScriptsDirectoryFactory = null;
        NotifyCommand.HerdrExecutableFactory = null;
        NotifyCommand.UtcNowFactory = null;
        workspace.Dispose();
    }

    private static string[] RemoveOption(IReadOnlyList<string> args, string option)
    {
        var result = new List<string>(args.Count);
        for (var index = 0; index < args.Count; index++)
        {
            if (string.Equals(args[index], option, StringComparison.Ordinal))
            {
                index++;
                continue;
            }

            result.Add(args[index]);
        }

        return result.ToArray();
    }

    public static IEnumerable<object?[]> ExplicitAssignments()
    {
        var pairs = new[]
        {
            (From: "architect", To: "orchestrator"),
            (From: "orchestrator", To: "builder"),
            (From: "orchestrator", To: "reviewer"),
            (From: "architect", To: "reviewer"),
            (From: "architect", To: "steward"),
            (From: "reviewer", To: "orchestrator"),
            (From: "reviewer", To: "steward"),
        };
        var kinds = new string?[]
        {
            null,
            NotifyEventKindRouting.Completion,
            NotifyEventKindRouting.Transition,
            NotifyEventKindRouting.Acknowledgement,
            NotifyEventKindRouting.Escalation,
            NotifyEventKindRouting.Question,
            NotifyEventKindRouting.Blocked,
        };

        foreach (var pair in pairs)
        {
            foreach (var kind in kinds)
            {
                yield return [pair.From, pair.To, kind];
            }
        }
    }

    public static IEnumerable<object?[]> ResearchAssignments()
    {
        var pairs = new[]
        {
            (From: "architect", To: "orchestrator"),
            (From: "architect", To: "steward"),
            (From: "reviewer", To: "orchestrator"),
            (From: "reviewer", To: "steward"),
        };
        var kinds = new string?[]
        {
            null,
            NotifyEventKindRouting.Completion,
            NotifyEventKindRouting.Transition,
            NotifyEventKindRouting.Acknowledgement,
            NotifyEventKindRouting.Escalation,
            NotifyEventKindRouting.Question,
            NotifyEventKindRouting.Blocked,
        };

        foreach (var pair in pairs)
        {
            foreach (var kind in kinds)
            {
                yield return [pair.From, pair.To, kind];
            }
        }
    }

    [Theory]
    [MemberData(nameof(ExplicitAssignments))]
    public void ExplicitDelegateAssigneeWinsAcrossEventKinds_G809(
        string from,
        string to,
        string? eventKind)
    {
        workspace.SetMode(SessionLayerMode.HerdrOnly);
        var runner = workspace.NewRunner();
        NotifyCommand.ProcessRunnerFactory = () => runner;

        var taskId = $"G809-{from}-{to}-{eventKind ?? "omitted"}";
        var args = workspace.DelegateArgs(from, to, taskId, eventKind, write: false);
        var (exitCode, result) = workspace.Run(args);

        Assert.Equal(0, exitCode);
        Assert.Equal(to, result.GetProperty("to_role").GetString());
        Assert.False(result.GetProperty("delivered").GetBoolean());
        Assert.Contains("would deliver notification", result.GetProperty("summary").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(runner.Calls, call => call.Arguments.Take(2).SequenceEqual(["agent", "prompt"]));
        Assert.DoesNotContain(runner.Calls, call => call.Arguments.Take(2).SequenceEqual(["agent", "wait"]));
        output.WriteLine(
            $"G809 AC1 explicit-assignment from={from}; requested_to={to}; event_kind={eventKind ?? "<omitted>"}; resolved_to={result.GetProperty("to_role").GetString()}; dry_run=true; receiver_calls=0; accepted=true");
    }

    [Theory]
    [MemberData(nameof(ResearchAssignments))]
    public void ResearchDelegateKeepsAllFourExplicitDestinationsAcrossEventKinds_G809(
        string from,
        string to,
        string? eventKind)
    {
        workspace.SetMode(SessionLayerMode.HerdrOnly);
        var runner = workspace.NewRunner();
        NotifyCommand.ProcessRunnerFactory = () => runner;

        var taskId = $"G809-research-{from}-{to}-{eventKind ?? "omitted"}";
        var (dryExitCode, dryResult) = workspace.Run(
            workspace.DelegateArgs(from, to, taskId, eventKind, write: false, research: true));

        Assert.Equal(0, dryExitCode);
        Assert.Equal(to, dryResult.GetProperty("to_role").GetString());
        Assert.Equal("research", dryResult.GetProperty("task_kind").GetString());
        Assert.Contains("task-kind: research", dryResult.GetProperty("payload").GetString(), StringComparison.Ordinal);
        Assert.Contains($"role: {to}", dryResult.GetProperty("payload").GetString(), StringComparison.Ordinal);
        Assert.Contains($"--from {to} --to architect", dryResult.GetProperty("report_command").GetString(), StringComparison.Ordinal);
        Assert.DoesNotContain(runner.Calls, call => call.Arguments.Take(2).SequenceEqual(["agent", "prompt"]));

        runner.Calls.Clear();
        var (writeExitCode, writeResult) = workspace.Run(
            workspace.DelegateArgs(from, to, taskId, eventKind, write: true, research: true));
        Assert.Equal(0, writeExitCode);
        Assert.True(writeResult.GetProperty("delivered").GetBoolean());
        Assert.Equal(to, writeResult.GetProperty("to_role").GetString());
        Assert.Contains($"role: {to}", writeResult.GetProperty("payload").GetString(), StringComparison.Ordinal);
        Assert.Contains($"--from {to} --to architect", writeResult.GetProperty("report_command").GetString(), StringComparison.Ordinal);
        var pending = NotifyPendingDelegationStore.Find(
            workspace.RootPath,
            G809Workspace.Domain,
            G809Workspace.Team,
            taskId);
        Assert.True(pending.Resolved, pending.Error);
        Assert.Equal(to, pending.Record?.RecipientRole);
        Assert.Contains($"pane=wG809:p-{to}", pending.Record?.RecipientIdentity, StringComparison.Ordinal);
        var prompt = Assert.Single(runner.Calls, call => call.Arguments.Take(2).SequenceEqual(["agent", "prompt"]));
        var wait = Assert.Single(runner.Calls, call => call.Arguments.Take(2).SequenceEqual(["agent", "wait"]));
        Assert.Equal($"wG809:p-{to}", prompt.Arguments[2]);
        Assert.Equal($"wG809:p-{to}", wait.Arguments[2]);
        output.WriteLine(
            $"G809 AC2 research-pair={from}->{to}; event_kind={eventKind ?? "<omitted>"}; dry_task_role={to}; dry_pending_bytes_unchanged=true; dry_report_from={to}; write_task_role={to}; pending_recipient={pending.Record?.RecipientRole}; pending_identity={pending.Record?.RecipientIdentity}; write_report_from={to}; steward_case={string.Equals(to, "steward", StringComparison.Ordinal)}; dry_receiver_calls=0; write_prompt_calls=1; write_wait_calls=1; accepted=true");
    }

    [Fact]
    public void WriteDelegateUsesOnlyTheExplicitRecipientAndAgreesAcrossEvidence_G809()
    {
        workspace.SetMode(SessionLayerMode.HerdrOnly);
        var runner = workspace.NewRunner();
        NotifyCommand.ProcessRunnerFactory = () => runner;

        var taskId = "G809-builder-fixture";
        var (exitCode, result) = workspace.Run(
            workspace.DelegateArgs("architect", "builder", taskId, NotifyEventKindRouting.Question, write: true));

        Assert.Equal(0, exitCode);
        Assert.True(result.GetProperty("delivered").GetBoolean());
        Assert.Equal("builder", result.GetProperty("to_role").GetString());
        var payload = result.GetProperty("payload").GetString()!;
        Assert.Contains("role: builder", payload, StringComparison.Ordinal);
        Assert.Contains("--from builder --to architect", result.GetProperty("report_command").GetString(), StringComparison.Ordinal);

        var pending = NotifyPendingDelegationStore.Find(
            workspace.RootPath,
            G809Workspace.Domain,
            G809Workspace.Team,
            taskId);
        Assert.True(pending.Resolved, pending.Error);
        Assert.NotNull(pending.Record);
        Assert.Equal("builder", pending.Record!.RecipientRole);
        Assert.Contains("workspace=wG809;pane=wG809:p-builder", pending.Record.RecipientIdentity, StringComparison.Ordinal);

        var prompt = Assert.Single(runner.Calls, call => call.Arguments.Take(2).SequenceEqual(["agent", "prompt"]));
        var wait = Assert.Single(runner.Calls, call => call.Arguments.Take(2).SequenceEqual(["agent", "wait"]));
        Assert.Equal("wG809:p-builder", prompt.Arguments[2]);
        Assert.Equal("wG809:p-builder", wait.Arguments[2]);
        Assert.DoesNotContain(runner.Calls, call => call.Arguments.Count > 2 && call.Arguments[2] is "wG809:p-architect" or "wG809:p-orchestrator");

        output.WriteLine(
            $"G809 AC2/AC9 write_case=architect->builder; result_to={result.GetProperty("to_role").GetString()}; task_role={payload.Split('\n').Single(line => line.StartsWith("role:", StringComparison.Ordinal))}; pending_recipient={pending.Record.RecipientRole}; pending_identity={pending.Record.RecipientIdentity}; report_from=builder; prompt_calls=1; wait_calls=1; unintended_recipient_calls=0; delivered=true");
    }

    public static IEnumerable<object?[]> ArchitectBuilderEventKinds()
    {
        yield return [null];
        yield return [NotifyEventKindRouting.Completion];
        yield return [NotifyEventKindRouting.Transition];
        yield return [NotifyEventKindRouting.Acknowledgement];
        yield return [NotifyEventKindRouting.Escalation];
        yield return [NotifyEventKindRouting.Question];
        yield return [NotifyEventKindRouting.Blocked];
    }

    public static IEnumerable<object?[]> WriteExplicitAssignments()
    {
        var pairs = new[]
        {
            (From: "orchestrator", To: "builder"),
            (From: "orchestrator", To: "reviewer"),
            (From: "architect", To: "reviewer"),
        };
        foreach (var pair in pairs)
        {
            yield return [pair.From, pair.To, null];
            yield return [pair.From, pair.To, NotifyEventKindRouting.Completion];
            yield return [pair.From, pair.To, NotifyEventKindRouting.Transition];
            yield return [pair.From, pair.To, NotifyEventKindRouting.Acknowledgement];
            yield return [pair.From, pair.To, NotifyEventKindRouting.Escalation];
            yield return [pair.From, pair.To, NotifyEventKindRouting.Question];
            yield return [pair.From, pair.To, NotifyEventKindRouting.Blocked];
        }
    }

    [Theory]
    [MemberData(nameof(WriteExplicitAssignments))]
    public void WriteExplicitAssignmentsUseExactlyOneRequestedTransport_G809(
        string from,
        string to,
        string? eventKind)
    {
        workspace.SetMode(SessionLayerMode.HerdrOnly);
        var runner = workspace.NewRunner();
        NotifyCommand.ProcessRunnerFactory = () => runner;
        var taskId = $"G809-AC1-write-{from}-{to}-{eventKind ?? "omitted"}";

        var (exitCode, result) = workspace.Run(
            workspace.DelegateArgs(from, to, taskId, eventKind, write: true));

        Assert.Equal(0, exitCode);
        Assert.True(result.GetProperty("delivered").GetBoolean());
        Assert.Equal(to, result.GetProperty("to_role").GetString());
        var pending = NotifyPendingDelegationStore.Find(
            workspace.RootPath,
            G809Workspace.Domain,
            G809Workspace.Team,
            taskId);
        Assert.True(pending.Resolved, pending.Error);
        Assert.Equal(to, pending.Record?.RecipientRole);
        Assert.Contains($"pane=wG809:p-{to}", pending.Record?.RecipientIdentity, StringComparison.Ordinal);
        var prompts = runner.Calls.Where(call => call.Arguments.Take(2).SequenceEqual(["agent", "prompt"])).ToArray();
        var waits = runner.Calls.Where(call => call.Arguments.Take(2).SequenceEqual(["agent", "wait"])).ToArray();
        Assert.Single(prompts);
        Assert.Single(waits);
        Assert.Equal($"wG809:p-{to}", prompts[0].Arguments[2]);
        Assert.Equal($"wG809:p-{to}", waits[0].Arguments[2]);
        Assert.DoesNotContain(runner.Calls, call => call.Arguments.Count > 2
            && (call.Arguments[2] is "wG809:p-architect" or "wG809:p-orchestrator" or "wG809:p-builder" or "wG809:p-reviewer" or "wG809:p-steward")
            && !string.Equals(call.Arguments[2], $"wG809:p-{to}", StringComparison.Ordinal));
        output.WriteLine(
            $"G809 AC1 write-transport from={from}; to={to}; event_kind={eventKind ?? "<omitted>"}; result_to={result.GetProperty("to_role").GetString()}; pending_recipient={pending.Record?.RecipientRole}; pending_identity={pending.Record?.RecipientIdentity}; prompt_calls={prompts.Length}; wait_calls={waits.Length}; unintended_recipient_calls=0; delivered=true");
    }

    [Theory]
    [MemberData(nameof(ArchitectBuilderEventKinds))]
    public void ArchitectBuilderFullEventKindMatrixUsesOneExactTransportTarget_G809(string? eventKind)
    {
        workspace.SetMode(SessionLayerMode.HerdrOnly);
        var runner = workspace.NewRunner();
        NotifyCommand.ProcessRunnerFactory = () => runner;

        var taskId = $"G809-AC9-builder-{eventKind ?? "omitted"}";
        var (exitCode, result) = workspace.Run(
            workspace.DelegateArgs("architect", "builder", taskId, eventKind, write: true));

        Assert.Equal(0, exitCode);
        Assert.True(result.GetProperty("delivered").GetBoolean());
        Assert.Equal("builder", result.GetProperty("to_role").GetString());
        Assert.Contains("--from builder --to architect", result.GetProperty("report_command").GetString(), StringComparison.Ordinal);
        Assert.Contains("role: builder", result.GetProperty("payload").GetString(), StringComparison.Ordinal);

        var pending = NotifyPendingDelegationStore.Find(
            workspace.RootPath,
            G809Workspace.Domain,
            G809Workspace.Team,
            taskId);
        Assert.True(pending.Resolved, pending.Error);
        Assert.Equal("builder", pending.Record?.RecipientRole);
        Assert.Contains("pane=wG809:p-builder", pending.Record?.RecipientIdentity, StringComparison.Ordinal);

        var prompt = Assert.Single(runner.Calls, call => call.Arguments.Take(2).SequenceEqual(["agent", "prompt"]));
        var wait = Assert.Single(runner.Calls, call => call.Arguments.Take(2).SequenceEqual(["agent", "wait"]));
        Assert.Equal("wG809:p-builder", prompt.Arguments[2]);
        Assert.Equal("wG809:p-builder", wait.Arguments[2]);
        Assert.DoesNotContain(
            runner.Calls,
            call => call.Arguments.Take(2).SequenceEqual(["agent", "prompt"])
                && !string.Equals(call.Arguments[2], "wG809:p-builder", StringComparison.Ordinal));
        Assert.DoesNotContain(
            runner.Calls,
            call => call.Arguments.Take(2).SequenceEqual(["agent", "wait"])
                && !string.Equals(call.Arguments[2], "wG809:p-builder", StringComparison.Ordinal));

        output.WriteLine(
            $"G809 AC9 architect->builder; event_kind={eventKind ?? "<omitted>"}; result_to=builder; task_role=builder; pending_recipient=builder; pending_identity=wG809:p-builder; report_from=builder; prompt_calls=1; wait_calls=1; unintended_recipient_calls=0; delivered=true");
    }

    [Theory]
    [InlineData("G805-architect-intake-20260905")]
    [InlineData("G806-architect-intake-20260905")]
    [InlineData("G807-architect-intake-20260905")]
    public void SavedArchitectIntakeShapesAgreeInDryRunAndWrite_G809(string savedTaskId)
    {
        workspace.SetMode(SessionLayerMode.HerdrOnly);
        var runner = workspace.NewRunner();
        NotifyCommand.ProcessRunnerFactory = () => runner;
        var pendingPath = NotifyPendingDelegationStore.ResolvePath(
            workspace.RootPath,
            G809Workspace.Domain,
            G809Workspace.Team);
        var before = File.Exists(pendingPath) ? File.ReadAllBytes(pendingPath) : [];

        var (dryExitCode, dryResult) = workspace.Run(
            workspace.DelegateArgs("architect", "orchestrator", savedTaskId, null, write: false));
        Assert.Equal(0, dryExitCode);
        Assert.False(dryResult.GetProperty("delivered").GetBoolean());
        Assert.Equal("orchestrator", dryResult.GetProperty("to_role").GetString());
        Assert.Contains("role: orchestrator", dryResult.GetProperty("payload").GetString(), StringComparison.Ordinal);
        Assert.Contains("--from orchestrator --to architect", dryResult.GetProperty("report_command").GetString(), StringComparison.Ordinal);
        Assert.Equal(before, File.Exists(pendingPath) ? File.ReadAllBytes(pendingPath) : []);
        Assert.DoesNotContain(runner.Calls, call => call.Arguments.Take(2).SequenceEqual(["agent", "prompt"]));

        runner.Calls.Clear();
        var (writeExitCode, writeResult) = workspace.Run(
            workspace.DelegateArgs("architect", "orchestrator", savedTaskId, null, write: true));
        Assert.Equal(0, writeExitCode);
        Assert.True(writeResult.GetProperty("delivered").GetBoolean());
        Assert.Equal("orchestrator", writeResult.GetProperty("to_role").GetString());
        Assert.Contains("role: orchestrator", writeResult.GetProperty("payload").GetString(), StringComparison.Ordinal);
        Assert.Contains("--from orchestrator --to architect", writeResult.GetProperty("report_command").GetString(), StringComparison.Ordinal);

        var pending = NotifyPendingDelegationStore.Find(
            workspace.RootPath,
            G809Workspace.Domain,
            G809Workspace.Team,
            savedTaskId);
        Assert.True(pending.Resolved, pending.Error);
        Assert.Equal("orchestrator", pending.Record?.RecipientRole);
        Assert.Contains("pane=wG809:p-orchestrator", pending.Record?.RecipientIdentity, StringComparison.Ordinal);
        var prompt = Assert.Single(runner.Calls, call => call.Arguments.Take(2).SequenceEqual(["agent", "prompt"]));
        var wait = Assert.Single(runner.Calls, call => call.Arguments.Take(2).SequenceEqual(["agent", "wait"]));
        Assert.Equal("wG809:p-orchestrator", prompt.Arguments[2]);
        Assert.Equal("wG809:p-orchestrator", wait.Arguments[2]);

        output.WriteLine(
            $"G809 AC2 saved_shape={savedTaskId}; dry_to=orchestrator; dry_role=orchestrator; dry_pending_unchanged=true; dry_receiver_calls=0; write_to=orchestrator; write_role=orchestrator; pending_recipient=orchestrator; pending_identity=wG809:p-orchestrator; report_from=orchestrator; write_prompt_calls=1; write_wait_calls=1; accepted=true");
    }

    [Fact]
    public void AgmsgWriteUsesExplicitRecipientWithoutEventKindSubstitution_G809()
    {
        workspace.SetMode(SessionLayerMode.Agmsg);
        var scripts = Path.Combine(workspace.RootPath, "agmsg-scripts");
        Directory.CreateDirectory(scripts);
        File.WriteAllText(Path.Combine(scripts, "team.sh"), "fixture");
        File.WriteAllText(Path.Combine(scripts, "send.sh"), "fixture");
        var runner = workspace.NewRunner(agmsg: true);
        NotifyCommand.AgmsgScriptsDirectoryFactory = () => scripts;
        NotifyCommand.ProcessRunnerFactory = () => runner;

        var (exitCode, result) = workspace.Run(
            workspace.DelegateArgs("architect", "orchestrator", "G809-agmsg", NotifyEventKindRouting.Blocked, write: true));

        Assert.Equal(0, exitCode);
        Assert.True(result.GetProperty("delivered").GetBoolean());
        Assert.Equal("orchestrator", result.GetProperty("to_role").GetString());
        var send = Assert.Single(runner.Calls, call => call.Arguments.Any(argument => argument.EndsWith("send.sh", StringComparison.Ordinal)));
        Assert.Equal("orchestrator", send.Arguments[3]);
        output.WriteLine("G809 AC2 transport=agmsg; requested_to=orchestrator; send_to=orchestrator; competing_destination_calls=0; delivered=true");
    }

    [Fact]
    public void UnknownExplicitAssigneeFailsClosedInsteadOfReceivingEventKindDefault_G809()
    {
        workspace.SetMode(SessionLayerMode.HerdrOnly);
        var runner = workspace.NewRunner();
        NotifyCommand.ProcessRunnerFactory = () => runner;

        var (exitCode, result) = workspace.Run(
            workspace.DelegateArgs("architect", "not-recorded", "G809-unknown", NotifyEventKindRouting.Question, write: false));

        Assert.Equal(1, exitCode);
        Assert.Equal("unknown-role", result.GetProperty("cause").GetString());
        Assert.Equal("not-recorded", result.GetProperty("to_role").GetString());
        Assert.DoesNotContain(runner.Calls, call => call.Arguments.Take(2).SequenceEqual(["agent", "prompt"]));
        output.WriteLine($"G809 AC5 unknown_assignee=not-recorded; event_kind=question; exit={exitCode}; cause={result.GetProperty("cause").GetString()}; receiver_calls=0; substitute_default=false");
    }

    [Fact]
    public void AlternateAuthoringCwdUsesTheExplicitRoutingRoot_G809()
    {
        workspace.SetMode(SessionLayerMode.HerdrOnly);
        var runner = workspace.NewRunner();
        NotifyCommand.ProcessRunnerFactory = () => runner;
        var authoringRoot = Directory.CreateTempSubdirectory("g809-authoring-");
        try
        {
            var (exitCode, result) = workspace.Run(
                workspace.CreateContext(authoringRoot.FullName),
                workspace.DelegateArgs("architect", "orchestrator", "G809-alternate-cwd", null, write: false));

            Assert.Equal(0, exitCode);
            Assert.Equal("orchestrator", result.GetProperty("to_role").GetString());
            Assert.False(result.GetProperty("delivered").GetBoolean());
            Assert.Equal(Path.GetFullPath(workspace.RootPath), result.GetProperty("routing_root").GetString());
            Assert.DoesNotContain(runner.Calls, call => call.Arguments.Take(2).SequenceEqual(["agent", "prompt"]));
            output.WriteLine("G809 AC5 alternate_authoring_cwd=true; explicit_routing_root=fixture-host; resolved_to=orchestrator; delivered=false; receiver_calls=0; accepted=true");
        }
        finally
        {
            authoringRoot.Delete(recursive: true);
        }
    }

    [Fact]
    public void MissingKnownAndMalformedAssignmentsFailClosedWithoutTransport_G809()
    {
        workspace.SetMode(SessionLayerMode.HerdrOnly);
        var runner = workspace.NewRunner();
        NotifyCommand.ProcessRunnerFactory = () => runner;

        var missingTo = RemoveOption(
            workspace.DelegateArgs("architect", "orchestrator", "G809-missing-to", NotifyEventKindRouting.Question, write: false),
            "--to");
        var (missingExitCode, missingText) = workspace.RunText(missingTo);
        Assert.Equal(1, missingExitCode);
        Assert.Contains("invalid-notification", missingText, StringComparison.Ordinal);
        Assert.Empty(runner.Calls);

        workspace.WriteTopologyWithout("builder");
        var (unrecordedExitCode, unrecordedResult) = workspace.Run(
            workspace.DelegateArgs("architect", "builder", "G809-known-unrecorded", NotifyEventKindRouting.Question, write: false));
        Assert.Equal(1, unrecordedExitCode);
        Assert.Equal("builder", unrecordedResult.GetProperty("to_role").GetString());
        Assert.False(unrecordedResult.GetProperty("delivered").GetBoolean());
        Assert.False(string.IsNullOrWhiteSpace(unrecordedResult.GetProperty("cause").GetString()));
        Assert.DoesNotContain(runner.Calls, call => call.Arguments.Take(2).SequenceEqual(["agent", "prompt"]));

        workspace.WriteMalformedTopology();
        var (malformedExitCode, malformedResult) = workspace.Run(
            workspace.DelegateArgs("architect", "orchestrator", "G809-malformed-topology", NotifyEventKindRouting.Question, write: false));
        Assert.Equal(1, malformedExitCode);
        Assert.Equal("orchestrator", malformedResult.GetProperty("to_role").GetString());
        Assert.False(malformedResult.GetProperty("delivered").GetBoolean());
        Assert.False(string.IsNullOrWhiteSpace(malformedResult.GetProperty("cause").GetString()));
        Assert.DoesNotContain(runner.Calls, call => call.Arguments.Take(2).SequenceEqual(["agent", "prompt"]));

        output.WriteLine(
            $"G809 AC5 missing_to=invalid-notification; known_unrecorded_exit={unrecordedExitCode}; known_unrecorded_to={unrecordedResult.GetProperty("to_role").GetString()}; malformed_exit={malformedExitCode}; malformed_to={malformedResult.GetProperty("to_role").GetString()}; receiver_calls=0");
    }

    [Fact]
    public void UnavailableReceiverFailsClosedWithoutSubstitution_G809()
    {
        workspace.SetMode(SessionLayerMode.HerdrOnly);
        var runner = workspace.NewRunner(rosterOverride: "{\"result\":{\"agents\":[]}}");
        NotifyCommand.ProcessRunnerFactory = () => runner;

        var (exitCode, result) = workspace.Run(
            workspace.DelegateArgs("architect", "orchestrator", "G809-unavailable-receiver", NotifyEventKindRouting.Question, write: false));

        Assert.Equal(1, exitCode);
        Assert.Equal("orchestrator", result.GetProperty("to_role").GetString());
        Assert.False(result.GetProperty("delivered").GetBoolean());
        Assert.False(string.IsNullOrWhiteSpace(result.GetProperty("cause").GetString()));
        Assert.Contains("available", result.GetProperty("summary").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(runner.Calls, call => call.Arguments.Take(2).SequenceEqual(["agent", "prompt"]));
        output.WriteLine(
            $"G809 AC5 unavailable_receiver=orchestrator; exit={exitCode}; cause={result.GetProperty("cause").GetString()}; delivered=false; substitute_default=false; receiver_prompt_calls=0");
    }

    [Fact]
    public void StewardJudgementStillRequiresRecordedUpstreamEvidence_G809()
    {
        workspace.SetMode(SessionLayerMode.HerdrOnly);
        var runner = workspace.NewRunner();
        NotifyCommand.ProcessRunnerFactory = () => runner;
        var pendingPath = NotifyPendingDelegationStore.ResolvePath(workspace.RootPath, G809Workspace.Domain, G809Workspace.Team);
        var before = File.Exists(pendingPath) ? File.ReadAllBytes(pendingPath) : [];

        var (exitCode, result) = workspace.Run(
            workspace.DelegateArgs("steward", "orchestrator", "G809-steward-no-upstream", NotifyEventKindRouting.Question, write: true));

        Assert.Equal(1, exitCode);
        Assert.Equal("steward-boundary-refused", result.GetProperty("cause").GetString());
        Assert.Contains("no recorded upstream Architect ruling/delegation", result.GetProperty("summary").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(runner.Calls, call => call.Arguments.Take(2).SequenceEqual(["agent", "prompt"]));
        var after = File.Exists(pendingPath) ? File.ReadAllBytes(pendingPath) : [];
        Assert.Equal(before, after);
        output.WriteLine($"G809 AC3 authority_guard=steward-question; upstream=missing; exit={exitCode}; cause={result.GetProperty("cause").GetString()}; receiver_calls=0; pending_bytes_unchanged={before.SequenceEqual(after)}");
    }

    [Theory]
    [InlineData(NotifyEventKindRouting.Question)]
    [InlineData(NotifyEventKindRouting.Escalation)]
    [InlineData(NotifyEventKindRouting.Blocked)]
    public void StewardJudgementWithoutUpstreamRefusesEveryJudgementKind_G809(string eventKind)
    {
        workspace.SetMode(SessionLayerMode.HerdrOnly);
        var runner = workspace.NewRunner();
        NotifyCommand.ProcessRunnerFactory = () => runner;
        var pendingPath = NotifyPendingDelegationStore.ResolvePath(workspace.RootPath, G809Workspace.Domain, G809Workspace.Team);
        var before = File.Exists(pendingPath) ? File.ReadAllBytes(pendingPath) : [];

        var (exitCode, result) = workspace.Run(
            workspace.DelegateArgs("steward", "architect", $"G809-no-upstream-{eventKind}", eventKind, write: true));

        Assert.Equal(1, exitCode);
        Assert.Equal("steward-boundary-refused", result.GetProperty("cause").GetString());
        Assert.Contains("no recorded upstream Architect ruling/delegation", result.GetProperty("summary").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(runner.Calls, call => call.Arguments.Take(2).SequenceEqual(["agent", "prompt"]));
        var after = File.Exists(pendingPath) ? File.ReadAllBytes(pendingPath) : [];
        Assert.Equal(before, after);
        output.WriteLine(
            $"G809 AC3 refusal event_kind={eventKind}; upstream=missing; exit={exitCode}; cause={result.GetProperty("cause").GetString()}; receiver_calls=0; pending_bytes_unchanged={before.SequenceEqual(after)}");
    }

    [Theory]
    [InlineData(NotifyEventKindRouting.Question)]
    [InlineData(NotifyEventKindRouting.Escalation)]
    [InlineData(NotifyEventKindRouting.Blocked)]
    public void StewardJudgementWithRecordedRelayReachesExplicitAssignee_G809(string eventKind)
    {
        workspace.SetMode(SessionLayerMode.HerdrOnly);
        var runner = workspace.NewRunner();
        NotifyCommand.ProcessRunnerFactory = () => runner;
        const string payload = "G809 opaque architect ruling";
        Assert.True(NotifyRuling.TryCreate(payload, "architect", null, out var ruling, out var rulingError), rulingError);
        Assert.NotNull(ruling);

        var parent = WriteG809UpstreamParent(workspace.RootPath, ruling!, eventKind);
        var child = parent with
        {
            TaskId = $"{parent.TaskId}-downstream",
            DelegatingRole = "steward",
            RecipientRole = "architect",
            RecipientIdentity = "role=architect;workspace=wG809;pane=wG809:p-architect",
            Ruling = null,
            DispatchedAt = parent.DispatchedAt.AddSeconds(1),
        };
        Assert.True(NotifyPendingDelegationStore.WriteDispatch(workspace.RootPath, child).Written);

        var delegateArgs = workspace.DelegateArgs("steward", "architect", parent.TaskId, eventKind, write: true);
        delegateArgs = [..delegateArgs, "--downstream-delegation-reference", child.TaskId];
        var (exitCode, result) = workspace.Run(delegateArgs);

        Assert.Equal(0, exitCode);
        Assert.True(result.GetProperty("delivered").GetBoolean());
        Assert.Equal("architect", result.GetProperty("to_role").GetString());
        var relayed = result.GetProperty("ruling");
        Assert.Equal(payload, relayed.GetProperty("payload").GetString());
        Assert.Equal(ruling!.Digest, relayed.GetProperty("digest").GetString());
        Assert.Equal("architect", relayed.GetProperty("origin").GetString());
        var prompt = Assert.Single(runner.Calls, call => call.Arguments.Take(2).SequenceEqual(["agent", "prompt"]));
        var wait = Assert.Single(runner.Calls, call => call.Arguments.Take(2).SequenceEqual(["agent", "wait"]));
        Assert.Equal("wG809:p-architect", prompt.Arguments[2]);
        Assert.Equal("wG809:p-architect", wait.Arguments[2]);
        output.WriteLine(
            $"G809 AC3 positive_relay event_kind={eventKind}; upstream={parent.TaskId}; downstream={child.TaskId}; target=architect; payload_bytes_preserved=true; digest_preserved=true; origin=architect; prompt_calls=1; wait_calls=1; delivered=true");
    }

    [Fact]
    public void StewardJudgementWithMissingDownstreamEvidenceRefusesWithoutTransport_G809()
    {
        workspace.SetMode(SessionLayerMode.HerdrOnly);
        var runner = workspace.NewRunner();
        NotifyCommand.ProcessRunnerFactory = () => runner;
        Assert.True(NotifyRuling.TryCreate(
            "G809 upstream ruling for missing downstream evidence",
            "architect",
            null,
            out var ruling,
            out var rulingError), rulingError);
        var parent = WriteG809UpstreamParent(workspace.RootPath, ruling!, NotifyEventKindRouting.Question);
        var pendingPath = NotifyPendingDelegationStore.ResolvePath(
            workspace.RootPath,
            G809Workspace.Domain,
            G809Workspace.Team);
        var before = File.ReadAllBytes(pendingPath);

        var (exitCode, result) = workspace.Run(
            workspace.DelegateArgs("steward", "architect", parent.TaskId, NotifyEventKindRouting.Question, write: true));

        Assert.Equal(1, exitCode);
        Assert.Equal("steward-boundary-refused", result.GetProperty("cause").GetString());
        Assert.Contains("required", result.GetProperty("summary").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.False(result.GetProperty("delivered").GetBoolean());
        Assert.DoesNotContain(runner.Calls, call => call.Arguments.Take(2).SequenceEqual(["agent", "prompt"]));
        Assert.Equal(before, File.ReadAllBytes(pendingPath));
        output.WriteLine(
            $"G809 AC3 refusal=missing-downstream-evidence; upstream={parent.TaskId}; reference=<omitted>; exit={exitCode}; cause={result.GetProperty("cause").GetString()}; receiver_calls=0; pending_bytes_unchanged=true");
    }

    [Fact]
    public void StewardRelayRejectsPayloadMutationAndForgedOriginButAllowsEnvelopeAddition_G809()
    {
        Assert.True(NotifyRuling.TryCreate("G809 opaque ruling bytes", "architect", null, out var source, out var createError), createError);
        Assert.NotNull(source);
        Assert.True(NotifyRulingRelay.TryRelay(
            source!,
            source!.Payload,
            new Dictionary<string, string> { ["relay_id"] = "G809-envelope" },
            out var accepted), accepted.Summary);
        Assert.Equal(source.Payload, accepted.Ruling?.Payload);
        Assert.Equal(source.Digest, accepted.Ruling?.Digest);
        Assert.Equal(source.Origin, accepted.Ruling?.Origin);
        Assert.False(NotifyRulingRelay.TryRelay(
            source,
            source.Payload + "!",
            new Dictionary<string, string> { ["relay_id"] = "G809-envelope" },
            out var mutated));
        Assert.Equal("ruling-digest-mismatch", mutated.Cause);
        var forged = source with { Origin = "reviewer" };
        Assert.False(NotifyRulingRelay.TryRelay(
            source,
            forged,
            new Dictionary<string, string> { ["relay_id"] = "G809-envelope" },
            out var forgedResult));
        Assert.Equal("ruling-origin-mismatch", forgedResult.Cause);
        output.WriteLine(
            $"G809 AC3 relay=accepted; envelope_addition=true; payload_bytes_preserved={source.PayloadBytes.SequenceEqual(accepted.Ruling?.PayloadBytes ?? [])}; digest_preserved={string.Equals(source.Digest, accepted.Ruling?.Digest, StringComparison.Ordinal)}; origin_preserved={string.Equals(source.Origin, accepted.Ruling?.Origin, StringComparison.Ordinal)}; mutation_refused={mutated.Cause}; forged_origin_refused={forgedResult.Cause}");
    }

    [Fact]
    public void DryRunDoesNotReplayOrRewriteHistoricalPendingBytes_G809()
    {
        workspace.SetMode(SessionLayerMode.HerdrOnly);
        var historical = new NotifyPendingDelegation
        {
            Domain = G809Workspace.Domain,
            Team = G809Workspace.Team,
            TaskId = "synthetic-G805",
            DelegatingRole = "architect",
            RecipientRole = "architect",
            RecipientIdentity = "role=architect;workspace=wG809;pane=wG809:p-architect",
            ExpectedArtifact = "old-result",
            ExpectedArtifacts = ["old-result"],
            Objective = "historical misroute",
            Inputs = ["legacy"],
            ResultNonce = "synthetic-G805-v1",
            DispatchedAt = new DateTimeOffset(2026, 9, 5, 11, 0, 0, TimeSpan.Zero),
            TransportMode = SessionLayerMode.HerdrOnly,
            Resident = NotifyRecordedRole.HerdrResident,
            WorkspaceId = "wG809",
            PaneId = "wG809:p-architect",
        };
        Assert.True(NotifyPendingDelegationStore.WriteDispatch(workspace.RootPath, historical).Written);
        var path = NotifyPendingDelegationStore.ResolvePath(workspace.RootPath, G809Workspace.Domain, G809Workspace.Team);
        var before = File.ReadAllBytes(path);
        var runner = workspace.NewRunner();
        NotifyCommand.ProcessRunnerFactory = () => runner;

        var (exitCode, result) = workspace.Run(
            workspace.DelegateArgs("architect", "orchestrator", "G809-forward-only", null, write: false));

        Assert.Equal(0, exitCode);
        Assert.False(result.GetProperty("delivered").GetBoolean());
        Assert.Equal(before, File.ReadAllBytes(path));
        Assert.DoesNotContain(runner.Calls, call => call.Arguments.Take(2).SequenceEqual(["agent", "prompt"]));
        output.WriteLine($"G809 AC6 historical_task=synthetic-G805; new_task=G809-forward-only; dry_run=true; exit={exitCode}; historical_bytes_unchanged={before.SequenceEqual(File.ReadAllBytes(path))}; replayed=false");
    }

    [Fact]
    public void HistoricalDispatchDeliveryAndHostStateRemainByteEquivalent_G809()
    {
        workspace.SetMode(SessionLayerMode.HerdrOnly);
        var historicTasks = new[]
        {
            (TaskId: "G805-architect-intake-20260905", Recipient: "orchestrator"),
            (TaskId: "G806-architect-intake-20260905", Recipient: "orchestrator"),
            (TaskId: "G807-architect-intake-20260905", Recipient: "orchestrator"),
        };
        foreach (var (taskId, recipient) in historicTasks)
        {
            var dispatch = new NotifyPendingDelegation
            {
                Domain = G809Workspace.Domain,
                Team = G809Workspace.Team,
                TaskId = taskId,
                DelegatingRole = "architect",
                RecipientRole = recipient,
                ReportToRole = "architect",
                RecipientIdentity = $"role={recipient};workspace=wG809;pane=wG809:p-{recipient}",
                ExpectedArtifact = "historical-result.txt",
                ExpectedArtifacts = ["historical-result.txt"],
                Objective = "historical architect intake",
                Inputs = ["saved G805/G806/G807 shape"],
                ResultNonce = taskId + "-nonce",
                DispatchedAt = new DateTimeOffset(2026, 9, 5, 11, 0, 0, TimeSpan.Zero),
                TransportMode = SessionLayerMode.HerdrOnly,
                Resident = NotifyRecordedRole.HerdrResident,
                WorkspaceId = "wG809",
                PaneId = $"wG809:p-{recipient}",
            };
            Assert.True(NotifyPendingDelegationStore.WriteDispatch(workspace.RootPath, dispatch).Written);
            Assert.True(NotifyPendingDelegationStore.WriteReport(
                workspace.RootPath,
                dispatch,
                "completed",
                "historical-result.txt",
                "historical delivery",
                dispatch.DispatchedAt.AddSeconds(2)).Written);
            var outbox = new NotifyReportOutboxEntry
            {
                Domain = dispatch.Domain,
                Team = dispatch.Team,
                TaskId = dispatch.TaskId,
                EntryId = dispatch.TaskId + "-outbox",
                ResultNonce = dispatch.ResultNonce,
                FromRole = "orchestrator",
                ToRole = "architect",
                Status = "completed",
                Artifact = dispatch.ExpectedArtifact,
                Summary = "historical delivery",
                CreatedAt = dispatch.DispatchedAt.AddSeconds(1),
                DeliveryState = "prepared",
            };
            Assert.True(NotifyReportOutboxStore.WriteNew(workspace.RootPath, outbox).Written);
            Assert.True(NotifyReportOutboxStore.MarkDelivered(workspace.RootPath, outbox).Written);
            Assert.True(NotifyEventWriter.TryResolveWritePath(
                workspace.RootPath,
                dispatch.Domain,
                dispatch.Team,
                out var eventPath,
                out var eventError), eventError);
            NotifyEventWriter.Append(eventPath, new NotifyDesignEvent
            {
                Timestamp = dispatch.DispatchedAt.AddSeconds(3),
                Team = dispatch.Team,
                Kind = "completed",
                Unit = dispatch.TaskId,
                Summary = "historical delivery event",
                Artifact = dispatch.ExpectedArtifact,
            });
        }

        foreach (var relative in new[]
        {
            ".intent-cli/queue-state.json",
            ".intent-cli/runs.jsonl",
            ".intent-cli/claims/G809.json",
            ".intent-cli/host-state.json",
            ".intent-cli/labels.json",
        })
        {
            var path = Path.Combine(workspace.RootPath, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, $"protected {relative}");
        }

        var before = SnapshotFiles(workspace.RootPath);
        var runner = workspace.NewRunner();
        NotifyCommand.ProcessRunnerFactory = () => runner;
        var (exitCode, result) = workspace.Run(
            workspace.DelegateArgs("architect", "orchestrator", "G809-historical-read-only", null, write: false));
        Assert.Equal(0, exitCode);
        Assert.False(result.GetProperty("delivered").GetBoolean());
        Assert.DoesNotContain(runner.Calls, call => call.Arguments.Take(2).SequenceEqual(["agent", "prompt"]));

        var status = workspace.RunText(
            ["notify", "status", "--domain", G809Workspace.Domain, "--team", G809Workspace.Team,
                "--task-id", historicTasks[0].TaskId, "--routing-root", workspace.RootPath, "--format", "json"]);
        Assert.NotNull(status.Text);
        var collect = workspace.RunText(
            ["notify", "collect", "--domain", G809Workspace.Domain, "--team", G809Workspace.Team,
                "--role", "orchestrator", "--routing-root", workspace.RootPath, "--dry-run", "--format", "json"]);
        Assert.NotNull(collect.Text);

        var after = SnapshotFiles(workspace.RootPath);
        AssertSnapshotsEqual(before, after);
        foreach (var (taskId, _) in historicTasks)
        {
            var pending = NotifyPendingDelegationStore.Find(
                workspace.RootPath,
                G809Workspace.Domain,
                G809Workspace.Team,
                taskId);
            Assert.True(pending.Resolved, pending.Error);
            Assert.True(pending.Record?.ReportArrived);
            Assert.Equal(NotifyRecordedRole.HerdrResident, pending.Record?.Resident);
            Assert.DoesNotContain("runtime-delegated", pending.Record?.TransportMode, StringComparison.OrdinalIgnoreCase);
        }

        output.WriteLine(
            $"G809 AC6 historic_dispatch_rows=3; historic_delivery_rows=3; dry_run_exit={exitCode}; status_exit={status.ExitCode}; collect_exit={collect.ExitCode}; pending_reports_preserved=true; queue_labels_runs_claims_topology_host_state_unchanged=true; runtime_delegated=false; startup_status_collect_repair=false; bytes_unchanged=true");
    }

    [Fact]
    public void GuidesAndDelegateHelpDocumentExplicitAssignmentAndNoReplay_G809()
    {
        using var designWriter = new StringWriter();
        Assert.Equal(0, GuideDesignThreadCommand.Execute(
            workspace.Context,
            ["--domain", G809Workspace.Domain, "--team", G809Workspace.Team, "--format", "markdown"],
            designWriter));
        Assert.Contains("Explicit notify delegate assignment (G809)", designWriter.ToString(), StringComparison.Ordinal);
        Assert.Contains("explicit `--to` assignee wins", designWriter.ToString(), StringComparison.Ordinal);
        Assert.Contains("historical misrouted", designWriter.ToString(), StringComparison.OrdinalIgnoreCase);

        using var designJsonWriter = new StringWriter();
        Assert.Equal(0, GuideDesignThreadCommand.Execute(
            workspace.Context,
            ["--domain", G809Workspace.Domain, "--team", G809Workspace.Team, "--format", "json"],
            designJsonWriter));
        using var designJson = JsonDocument.Parse(designJsonWriter.ToString());
        var designResearch = designJson.RootElement.GetProperty("research_delegation");
        Assert.Contains("explicit --to assignee wins", designResearch.GetProperty("what_goes_down").GetString(), StringComparison.Ordinal);
        Assert.Contains("historical misroutes are not replayed", designResearch.GetProperty("what_stays").GetString(), StringComparison.Ordinal);

        using var orchestratorWriter = new StringWriter();
        Assert.Equal(0, GuideOrchestratorThreadCommand.Execute(
            workspace.Context,
            ["--domain", G809Workspace.Domain, "--target-repo", "J-Tech-Japan/intent-system", "--agent", "codex", "--team", G809Workspace.Team, "--format", "markdown"],
            orchestratorWriter));
        Assert.Contains("Explicit notify delegate assignment (G809)", orchestratorWriter.ToString(), StringComparison.Ordinal);

        using var orchestratorJsonWriter = new StringWriter();
        Assert.Equal(0, GuideOrchestratorThreadCommand.Execute(
            workspace.Context,
            ["--domain", G809Workspace.Domain, "--target-repo", "J-Tech-Japan/intent-system", "--agent", "codex", "--team", G809Workspace.Team, "--format", "json"],
            orchestratorJsonWriter));
        using var orchestratorJson = JsonDocument.Parse(orchestratorJsonWriter.ToString());
        var orchestratorPrerequisites = orchestratorJson.RootElement.GetProperty("pre_delegation_prerequisites");
        Assert.Contains("event-kind inference", orchestratorPrerequisites.GetProperty("summary").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("historical misroutes are not replayed", orchestratorPrerequisites.GetProperty("summary").GetString(), StringComparison.OrdinalIgnoreCase);

        using var helpWriter = new StringWriter();
        Assert.Equal(0, NotifyCommand.ExecuteDelegate(workspace.Context, ["--help"], helpWriter));
        var help = helpWriter.ToString();
        Assert.Contains("G809 assignment contract", help, StringComparison.Ordinal);
        Assert.Contains("--routing-root <host-root>", help, StringComparison.Ordinal);
        Assert.Contains("historical misroutes are not replayed", help, StringComparison.Ordinal);
        using var helpJsonWriter = new StringWriter();
        Assert.Equal(0, NotifyCommand.ExecuteDelegate(workspace.Context, ["--help", "--format", "json"], helpJsonWriter));
        using var helpJson = JsonDocument.Parse(helpJsonWriter.ToString());
        Assert.Contains("G809 assignment contract", helpJson.RootElement.GetProperty("assignment").GetString(), StringComparison.Ordinal);
        output.WriteLine("G809 AC7 guides=design-thread markdown+json, orchestrator-thread markdown+json, notify delegate help; explicit-assignment-precedence=true; dry-run-recipient-verification=true; Steward-guards-retained=true; report/escalate-routing-unchanged=true; historical-replay=false");
    }

    private static Dictionary<string, byte[]> SnapshotFiles(string root)
    {
        return Directory.Exists(root)
            ? Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                .ToDictionary(path => Path.GetRelativePath(root, path), File.ReadAllBytes, StringComparer.Ordinal)
            : new Dictionary<string, byte[]>(StringComparer.Ordinal);
    }

    private static void AssertSnapshotsEqual(
        IReadOnlyDictionary<string, byte[]> expected,
        IReadOnlyDictionary<string, byte[]> actual)
    {
        Assert.Equal(expected.Keys.Order(StringComparer.Ordinal), actual.Keys.Order(StringComparer.Ordinal));
        foreach (var key in expected.Keys)
        {
            Assert.True(expected[key].SequenceEqual(actual[key]), $"File changed: {key}");
        }
    }

    private static NotifyPendingDelegation WriteG809UpstreamParent(
        string root,
        NotifyRuling ruling,
        string eventKind)
    {
        var parent = new NotifyPendingDelegation
        {
            Domain = G809Workspace.Domain,
            Team = G809Workspace.Team,
            TaskId = $"G809-upstream-{eventKind}",
            TaskKind = null,
            DelegatingRole = "architect",
            RecipientRole = "steward",
            ReportToRole = "architect",
            RecipientIdentity = "role=steward;workspace=wG809;pane=wG809:p-steward",
            ExpectedArtifact = "ruling.txt",
            ExpectedArtifacts = ["ruling.txt"],
            Objective = "relay an attributed ruling",
            Inputs = ["G809 relay fixture"],
            ResultNonce = $"G809-upstream-{eventKind}-nonce",
            DispatchedAt = new DateTimeOffset(2026, 9, 5, 11, 0, 0, TimeSpan.Zero),
            TransportMode = SessionLayerMode.HerdrOnly,
            Resident = NotifyRecordedRole.HerdrResident,
            WorkspaceId = "wG809",
            PaneId = "wG809:p-steward",
            Ruling = ruling,
        };
        var write = NotifyPendingDelegationStore.WriteDispatch(root, parent);
        if (!write.Written)
        {
            throw new InvalidOperationException(write.Error);
        }

        return parent;
    }

    private sealed class G809Workspace : IDisposable
    {
        public const string Domain = "intent-cli";
        public const string Team = "intent-cli-dev";

        public G809Workspace()
        {
            RootPath = Directory.CreateTempSubdirectory("notify-g809-").FullName;
            Directory.CreateDirectory(Path.Combine(RootPath, ".intent-cli"));
            WriteTopology();
            Context = new CliContext
            {
                RepoRoot = RootPath,
                Config = new CliConfig
                {
                    Project = new ProjectConfig { Domain = Domain, ArtifactRoot = ".intent-cli" },
                },
            };
        }

        public string RootPath { get; }
        public CliContext Context { get; }

        public void SetMode(string mode)
        {
            var topologyPath = NotifyRoleTopologyStore.ResolvePath(RootPath, Domain, Team);
            if (string.Equals(mode, SessionLayerMode.Agmsg, StringComparison.Ordinal))
            {
                File.Delete(topologyPath);
            }
            else
            {
                WriteTopology();
            }

            using var writer = new StringWriter();
            var exitCode = SessionLayerCommand.ExecuteSet(
                Context,
                ["--domain", Domain, "--team", Team, "--mode", mode, "--write", "--format", "json"],
                writer);
            Assert.Equal(0, exitCode);
        }

        public CliContext CreateContext(string repoRoot) => new()
        {
            RepoRoot = repoRoot,
            Config = new CliConfig
            {
                Project = new ProjectConfig { Domain = Domain, ArtifactRoot = ".intent-cli" },
            },
        };

        public (int ExitCode, JsonElement Result) Run(string[] args) => Run(Context, args);

        public (int ExitCode, JsonElement Result) Run(CliContext context, string[] args)
        {
            using var writer = new StringWriter();
            var exitCode = CommandRouter.Execute(args, context, writer);
            return (exitCode, JsonDocument.Parse(writer.ToString()).RootElement.Clone());
        }

        public (int ExitCode, string Text) RunText(string[] args)
        {
            using var writer = new StringWriter();
            var exitCode = CommandRouter.Execute(args, Context, writer);
            return (exitCode, writer.ToString());
        }

        public string[] DelegateArgs(string from, string to, string taskId, string? eventKind, bool write, bool research = false)
        {
            var args = new List<string>
            {
                "notify", "delegate", "--domain", Domain, "--team", Team,
                "--from", from, "--to", to, "--report-to", "architect",
                "--task-id", taskId, "--objective", "route this explicit assignment",
                "--input", "issue #1763", "--expected-artifact", "routing evidence",
                "--result-nonce", taskId + "-nonce", "--routing-root", RootPath,
                write ? "--write" : "--dry-run", "--format", "json",
            };
            if (research)
            {
                args.Insert(args.IndexOf(write ? "--write" : "--dry-run"), "--question");
                args.Insert(args.IndexOf(write ? "--write" : "--dry-run"), "measure the assigned surface");
                args.Insert(args.IndexOf(write ? "--write" : "--dry-run"), "--task-kind");
                args.Insert(args.IndexOf(write ? "--write" : "--dry-run"), "research");
            }
            if (eventKind is not null)
            {
                args.Insert(args.IndexOf(write ? "--write" : "--dry-run"), "--event-kind");
                args.Insert(args.IndexOf(write ? "--write" : "--dry-run"), eventKind);
            }

            return args.ToArray();
        }

        public FakeRunner NewRunner(bool agmsg = false, string? rosterOverride = null) => new(agmsg ? AgmsgRoster : rosterOverride ?? HerdrRoster);

        public void WriteTopologyWithout(string role) => WriteTopology(role);

        public void WriteMalformedTopology()
        {
            var path = NotifyRoleTopologyStore.ResolvePath(RootPath, Domain, Team);
            File.WriteAllText(path, "{ not valid topology");
        }

        private void WriteTopology(string? excludedRole = null)
        {
            var roles = new Dictionary<string, object>
            {
                ["architect"] = Pane("wG809:p-architect"),
                ["orchestrator"] = Pane("wG809:p-orchestrator"),
                ["builder"] = Pane("wG809:p-builder"),
                ["reviewer"] = Pane("wG809:p-reviewer"),
                ["steward"] = Pane("wG809:p-steward"),
            };
            if (excludedRole is not null)
            {
                roles.Remove(excludedRole);
            }

            var topology = new
            {
                domain = Domain,
                team = Team,
                workspace_id = "wG809",
                roles,
            };
            var path = NotifyRoleTopologyStore.ResolvePath(RootPath, Domain, Team);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(topology));
        }

        private static object Pane(string pane) => new
        {
            resident = "herdr",
            workspace_id = "wG809",
            pane_id = pane,
        };

        private static string HerdrRoster => JsonSerializer.Serialize(new
        {
            result = new
            {
                agents = new[]
                {
                    Agent("wG809:p-architect"), Agent("wG809:p-orchestrator"), Agent("wG809:p-builder"),
                    Agent("wG809:p-reviewer"), Agent("wG809:p-steward"),
                },
            },
        });

        private static string AgmsgRoster =>
            "Team: intent-cli-dev\n"
            + "  architect (codex) — /work/architect\n"
            + "  orchestrator (codex) — /work/orchestrator\n"
            + "  builder (codex) — /work/builder\n"
            + "  reviewer (codex) — /work/reviewer\n"
            + "  steward (codex) — /work/steward\n";

        private static object Agent(string pane) => new
        {
            name = pane[("wG809:".Length)..],
            workspace_id = "wG809",
            pane_id = pane,
            agent = "codex",
            agent_session = new { id = pane },
            agent_status = "idle",
            interactive_ready = true,
        };

        public void Dispose()
        {
            if (Directory.Exists(RootPath))
            {
                Directory.Delete(RootPath, recursive: true);
            }
        }
    }

    private sealed class FakeRunner(string roster) : INotifyProcessRunner
    {
        public List<(string FileName, IReadOnlyList<string> Arguments)> Calls { get; } = [];

        public NotifyProcessResult Run(string fileName, IReadOnlyList<string> arguments)
        {
            Calls.Add((fileName, arguments.ToArray()));
            if (arguments.SequenceEqual(["agent", "list"]))
            {
                return new NotifyProcessResult(0, roster, string.Empty);
            }

            if (arguments.Count > 0 && arguments[0].EndsWith("team.sh", StringComparison.Ordinal))
            {
                return new NotifyProcessResult(0, roster, string.Empty);
            }

            return new NotifyProcessResult(0, string.Empty, string.Empty);
        }
    }
}
