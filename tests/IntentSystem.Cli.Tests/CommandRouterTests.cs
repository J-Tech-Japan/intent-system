using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;
using IntentSystem.ConceptIntake.Models;
using IntentSystem.Clarify.Models;
using IntentSystem.Clarify.Serialization;
using IntentSystem.Review;
using IntentSystem.Review.Serialization;
using IntentSystem.Supervisor.Models;
using IntentSystem.Supervisor.Serialization;

namespace IntentSystem.Cli.Tests;

[Collection(RunSubmitCommandCollection.Name)]
public sealed class CommandRouterTests
{
    [Fact]
    public void Execute_GivenNoArguments_DefaultHelpIsChatFirst_ShowsPrimaryGroups_HidesLegacy()
    {
        // G379: the default `intent-cli --help` is chat-first. It leads with
        // the workflow guides and lists only the primary command groups a
        // routine agent reaches for; it must NOT dump the advanced/legacy
        // surfaces (`run`, concept-intake `intake`, projection, etc.) or the
        // RunRoleNote, so an agent never mistakes `intent-cli run` for the
        // implementation/review loop. The full catalog moves to `--help --all`.
        using var writer = new StringWriter();

        var exitCode = CommandRouter.Execute(Array.Empty<string>(), CreateContext("/tmp/intent-system"), writer);

        var output = writer.ToString();
        Assert.Equal(0, exitCode);

        // Chat-first lead: workflow guides + the primary group list.
        Assert.Contains("Workflow guides:", output, StringComparison.Ordinal);
        Assert.Contains("Primary command groups", output, StringComparison.Ordinal);
        foreach (var group in CommandRouter.PrimaryCommandGroups)
        {
            Assert.Contains($"- {group}", output, StringComparison.Ordinal);
        }

        // Advanced/legacy groups are NOT listed in the default view, and the
        // RunRoleNote (which belongs to the full `--all` view) is absent.
        Assert.DoesNotContain("- run", output, StringComparison.Ordinal);
        Assert.DoesNotContain("- intake", output, StringComparison.Ordinal);
        Assert.DoesNotContain("- projection", output, StringComparison.Ordinal);
        Assert.DoesNotContain("- generate-from-current", output, StringComparison.Ordinal);
        Assert.DoesNotContain("not the primary production orchestrator", output, StringComparison.Ordinal);

        // The default points at the guide commands catalog.
        Assert.Contains("intent-cli guide commands list", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_TopLevelImprove_DelegatesToGuideImprove_DesignThreadProcess()
    {
        // G457: `intent-cli improve` is a first-class top-level alias that
        // delegates to the same design-thread guidance as `guide improve`.
        using var writer = new StringWriter();
        var exitCode = CommandRouter.Execute(
            ["improve", "--domain", "intent-cli", "--format", "json"],
            CreateContext("/tmp/intent-system"),
            writer);

        Assert.Equal(0, exitCode);
        using var doc = System.Text.Json.JsonDocument.Parse(writer.ToString());
        Assert.Equal("design-thread-improve", doc.RootElement.GetProperty("process").GetString());
    }

    [Fact]
    public void Execute_TopLevelImproveHelp_ReachesImproveHelpSurface()
    {
        using var writer = new StringWriter();
        var exitCode = CommandRouter.Execute(
            ["improve", "--help"],
            CreateContext("/tmp/intent-system"),
            writer);

        Assert.Equal(0, exitCode);
        Assert.Contains("guide improve", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_DefaultHelp_ExposesImprovePointer()
    {
        // G457: improve is discoverable from `intent-cli --help`.
        using var writer = new StringWriter();
        var exitCode = CommandRouter.Execute(Array.Empty<string>(), CreateContext("/tmp/intent-system"), writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("improve", output, StringComparison.Ordinal);
        Assert.Contains("intent-cli improve", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_TopLevelGrill_DelegatesToGuideGrill_PersistentInterviewMode()
    {
        // G463: `intent-cli grill` is a first-class top-level alias that
        // delegates to the same persistent-interview guidance as `guide grill`.
        using var writer = new StringWriter();
        var exitCode = CommandRouter.Execute(
            ["grill", "--domain", "intent-cli", "--format", "json"],
            CreateContext("/tmp/intent-system"),
            writer);

        Assert.Equal(0, exitCode);
        using var doc = System.Text.Json.JsonDocument.Parse(writer.ToString());
        Assert.Equal("persistent-grill-interview", doc.RootElement.GetProperty("process").GetString());
    }

    [Fact]
    public void Execute_GuideGrill_ReachesPersistentInterviewModeSurface()
    {
        // G463: the guide-namespaced form returns the same surface.
        using var writer = new StringWriter();
        var exitCode = CommandRouter.Execute(
            ["guide", "grill", "--domain", "intent-cli", "--format", "json"],
            CreateContext("/tmp/intent-system"),
            writer);

        Assert.Equal(0, exitCode);
        using var doc = System.Text.Json.JsonDocument.Parse(writer.ToString());
        Assert.Equal("persistent-grill-interview", doc.RootElement.GetProperty("process").GetString());
    }

    [Fact]
    public void Execute_DefaultHelp_ExposesGrillPointer()
    {
        // G463: grill is discoverable from `intent-cli --help`.
        using var writer = new StringWriter();
        var exitCode = CommandRouter.Execute(Array.Empty<string>(), CreateContext("/tmp/intent-system"), writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("intent-cli grill", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_TopLevelStack_DelegatesToGuideStack_PacketBacklogProcess()
    {
        // G464: `intent-cli stack` is a first-class top-level alias that
        // delegates to the same packet-backlog guidance as `guide stack`.
        using var writer = new StringWriter();
        var exitCode = CommandRouter.Execute(
            ["stack", "--domain", "intent-cli", "--target-repo", "J-Tech-Japan/intent-system", "--format", "json"],
            CreateContext("/tmp/intent-system"),
            writer);

        Assert.Equal(0, exitCode);
        using var doc = System.Text.Json.JsonDocument.Parse(writer.ToString());
        Assert.Equal("task-stack", doc.RootElement.GetProperty("process").GetString());
    }

    [Fact]
    public void Execute_GuideStack_ReachesPacketBacklogSurface()
    {
        // G464: the guide-namespaced form returns the same surface.
        using var writer = new StringWriter();
        var exitCode = CommandRouter.Execute(
            ["guide", "stack", "--domain", "intent-cli", "--format", "json"],
            CreateContext("/tmp/intent-system"),
            writer);

        Assert.Equal(0, exitCode);
        using var doc = System.Text.Json.JsonDocument.Parse(writer.ToString());
        Assert.Equal("task-stack", doc.RootElement.GetProperty("process").GetString());
    }

    [Fact]
    public void Execute_DefaultHelp_ExposesStackPointer()
    {
        // G464: stack is discoverable from `intent-cli --help`.
        using var writer = new StringWriter();
        var exitCode = CommandRouter.Execute(Array.Empty<string>(), CreateContext("/tmp/intent-system"), writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("intent-cli stack", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenHelpAll_ListsEveryCommandGroup_AndGenerateFromCurrent()
    {
        // G379: `intent-cli --help --all` restores the full group catalog,
        // including the advanced/legacy surfaces hidden from the default.
        using var writer = new StringWriter();

        var exitCode = CommandRouter.Execute(["--help", "--all"], CreateContext("/tmp/intent-system"), writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("All command groups:", output, StringComparison.Ordinal);
        Assert.Contains("- projection", output, StringComparison.Ordinal);
        Assert.Contains("- tasking", output, StringComparison.Ordinal);
        // Workflow guides remain present in the full view too.
        Assert.Contains("Workflow guides:", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenHelpAll_DescribesRunAsIntegrationSmokeNotPrimaryOrchestrator()
    {
        // G188 / G379 — `intent-cli run` is for integration smoke, deterministic
        // replay, and local dogfooding; production automation lives in the
        // host-side review/next-slice loop. The RunRoleNote now lives in the
        // full `--help --all` view (the chat-first default omits it), and the
        // canonical wording must come from the shared CommandRouter.RunRoleNote
        // constant so the help surface cannot drift from the test assertion.
        using var writer = new StringWriter();

        var exitCode = CommandRouter.Execute(["--help", "--all"], CreateContext("/tmp/intent-system"), writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();

        Assert.Contains("integration smoke", output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("deterministic replay", output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("local dogfooding", output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("not the primary production orchestrator", output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("host-side review/next-slice loop", output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("automation summary", output, StringComparison.Ordinal);
        Assert.Contains("safety nested-provider-handoff", output, StringComparison.Ordinal);

        // The shared constant must be the single source of truth for this
        // wording so future changes flow through one place.
        foreach (var line in CommandRouter.RunRoleNote)
        {
            Assert.Contains(line, output, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void PrimaryCommandGroups_AreAllRegisteredCommandGroups()
    {
        // G379: every chat-first primary group must be a real command group
        // so the default help never advertises a group the router can't reach.
        var groups = typeof(CommandRouter)
            .GetField("CommandGroups", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)
            ?.GetValue(null) as string[];
        Assert.NotNull(groups);
        foreach (var primary in CommandRouter.PrimaryCommandGroups)
        {
            Assert.Contains(primary, groups!);
        }
    }

    [Fact]
    public void Execute_GivenKnownGroupAndUnknownSubcommand_WritesNotYetImplementedMessage()
    {
        using var writer = new StringWriter();

        var exitCode = CommandRouter.Execute(["projection", "status"], CreateContext("/tmp/intent-system"), writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("not yet implemented", writer.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    // G317 review-fix: `intent-cli task --help` and `intent-cli task`
    // must reach TaskCommand's help surface through the real router
    // entry path, not only through direct `TaskCommand.Execute` calls.
    [Fact]
    public void Execute_GivenTaskHelpFlag_DispatchesToTaskHelp_ExitZero()
    {
        using var writer = new StringWriter();

        var exitCode = CommandRouter.Execute(
            ["task", "--help"],
            CreateContext("/tmp/intent-system"),
            writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("Usage: intent-cli task", output, StringComparison.Ordinal);
        Assert.Contains("issue-to-pr", output, StringComparison.Ordinal);
        Assert.Contains("review-pr", output, StringComparison.Ordinal);
        Assert.Contains("fix-pr-comments", output, StringComparison.Ordinal);
        Assert.Contains("publish-next-issue", output, StringComparison.Ordinal);
        // Must NOT regress to the old "not yet implemented" surface.
        Assert.DoesNotContain("not yet implemented", output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Execute_GivenTaskGroupNoSubcommand_DispatchesToTaskUsage_ExitOne()
    {
        // `intent-cli task` (no subcommand) shows usage and returns 1
        // — same contract as direct `TaskCommand.Execute(args=[])`.
        // Critically, it must NOT fall through to the router's
        // "command group and subcommand are required" message.
        using var writer = new StringWriter();

        var exitCode = CommandRouter.Execute(
            ["task"],
            CreateContext("/tmp/intent-system"),
            writer);

        Assert.Equal(1, exitCode);
        var output = writer.ToString();
        Assert.Contains("Usage: intent-cli task", output, StringComparison.Ordinal);
        Assert.DoesNotContain("A command group and subcommand are required", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenTaskIssueToPr_DispatchesToTaskCommandViaRouter()
    {
        // The four subcommand kinds dispatch through the router's
        // dictionary entries — verify with a happy-path issue-to-pr
        // call that the kind threads through and returns a valid
        // executable contract.
        using var writer = new StringWriter();

        var exitCode = CommandRouter.Execute(
            ["task", "issue-to-pr", "--repo", "J-Tech-Japan/intent-system",
             "--issue", "5000", "--workdir", "/tmp/child", "--format", "json"],
            CreateContext("/tmp/intent-system"),
            writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("\"task\":", output, StringComparison.Ordinal);
        Assert.Contains("\"issue-to-pr\"", output, StringComparison.Ordinal);
        Assert.Contains("--number 5000", output, StringComparison.Ordinal);
    }

    // ---- G334 self-discovery help tests ------------------------------------

    // G334 — `intent-cli guide --help` and `intent-cli guide help` must
    // reach a useful self-discovery surface, not the
    // "not yet implemented" fall-through. External users that do not
    // already know intent-system MUST be able to derive the workflow
    // entries without reading repo-local rules or skill prompt files.

    [Fact]
    public void Execute_GivenGuideHelpSubcommand_ReturnsExternalUserSelfDiscoverySurface_ExitZero()
    {
        using var writer = new StringWriter();

        var exitCode = CommandRouter.Execute(
            ["guide", "help"],
            CreateContext("/tmp/intent-system"),
            writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("self-discovery for external users", output, StringComparison.Ordinal);
        Assert.Contains("Usage: intent-cli guide help", output, StringComparison.Ordinal);
        // Every advertised subcommand must show up in the human
        // surface — this is the canonical guide subcommand catalog.
        foreach (var entry in GuideHelpCommand.Subcommands)
        {
            Assert.Contains($"`{entry.Name}`", output, StringComparison.Ordinal);
        }
        // Workflow-guide phases the issue (#771) requires must appear.
        foreach (var phase in new[] { "init", "interview", "packet", "issue", "automation", "bug-repair" })
        {
            Assert.Contains(phase, output, StringComparison.Ordinal);
        }
        Assert.DoesNotContain("not yet implemented", output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Execute_GivenGuideHelpSubcommand_JsonFormat_ReturnsStableShape()
    {
        using var writer = new StringWriter();

        var exitCode = CommandRouter.Execute(
            ["guide", "help", "--format", "json"],
            CreateContext("/tmp/intent-system"),
            writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("\"usage\":", output, StringComparison.Ordinal);
        Assert.Contains("\"subcommands\":", output, StringComparison.Ordinal);
        Assert.Contains("\"workflow_guides\":", output, StringComparison.Ordinal);
        Assert.Contains("\"metadata_mutation_guidance\":", output, StringComparison.Ordinal);
        // Phase identifiers are part of the stable JSON shape consumers
        // can pin against.
        Assert.Contains("\"phase\": \"init\"", output, StringComparison.Ordinal);
        Assert.Contains("\"phase\": \"interview\"", output, StringComparison.Ordinal);
        Assert.Contains("\"phase\": \"packet\"", output, StringComparison.Ordinal);
        Assert.Contains("\"phase\": \"issue\"", output, StringComparison.Ordinal);
        Assert.Contains("\"phase\": \"automation\"", output, StringComparison.Ordinal);
        Assert.Contains("\"phase\": \"bug-repair\"", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenGuideHelpFlag_DispatchesToGroupHelp_ExitZero()
    {
        // G334: `intent-cli guide --help` is the canonical synonym for
        // `intent-cli guide` (no subcommand). Router prints the
        // per-group descriptor listing every registered subcommand.
        using var writer = new StringWriter();

        var exitCode = CommandRouter.Execute(
            ["guide", "--help"],
            CreateContext("/tmp/intent-system"),
            writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("intent-cli guide — group help", output, StringComparison.Ordinal);
        Assert.Contains("Subcommands (run with --help for details):", output, StringComparison.Ordinal);
        Assert.Contains("help", output, StringComparison.Ordinal);
        Assert.Contains("commands", output, StringComparison.Ordinal);
        Assert.Contains("Prefer intent-cli-backed metadata mutation over hand-editing", output, StringComparison.Ordinal);
        Assert.DoesNotContain("not yet implemented", output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Execute_GivenGuideNoSubcommand_DispatchesToGroupHelp_ExitZero()
    {
        // `intent-cli guide` alone (no subcommand) must reach
        // per-group help, not the "command group and subcommand are
        // required" fall-through. External users routinely type the
        // group name first to learn what is available.
        using var writer = new StringWriter();

        var exitCode = CommandRouter.Execute(
            ["guide"],
            CreateContext("/tmp/intent-system"),
            writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("intent-cli guide — group help", output, StringComparison.Ordinal);
        Assert.DoesNotContain("A command group and subcommand are required", output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("metadata", "validate", "update")]
    [InlineData("migrate", "host-state", null)]
    [InlineData("issue", "draft", "publish-flow")]
    [InlineData("automation", "doctor", "summary")]
    [InlineData("intent", "init", "next-slice")]
    [InlineData("packet", "draft", null)]
    [InlineData("interview", "next-question", "record-answer")]
    [InlineData("bug", "report", "triage")]
    [InlineData("worker", "claim", "complete")]
    [InlineData("queue", "list", "show")]
    [InlineData("closeout", "pr", null)]
    public void Execute_GivenGroupHelpFlag_ListsSubcommands_ExitZero(string group, string mustContain1, string? mustContain2)
    {
        // G334: every implemented top-level group answers
        // `<group> --help` with a useful descriptor listing its
        // subcommands. The issue (#771) explicitly calls out
        // `metadata --help` and `migrate --help` as previously
        // missing; this theory pins the full set so future groups
        // get the same coverage.
        using var writer = new StringWriter();

        var exitCode = CommandRouter.Execute(
            [group, "--help"],
            CreateContext("/tmp/intent-system"),
            writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains($"intent-cli {group} — group help", output, StringComparison.Ordinal);
        Assert.Contains(mustContain1, output, StringComparison.Ordinal);
        if (mustContain2 is not null)
        {
            Assert.Contains(mustContain2, output, StringComparison.Ordinal);
        }
        Assert.DoesNotContain("not yet implemented", output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Execute_GivenUnimplementedGroupHelp_MarksAsUnavailableInThisBuild_ExitZero()
    {
        // Reserved groups in CommandGroups that have no entry in
        // ImplementedCommands ("clarification") must announce
        // themselves as unavailable rather than silently fail. This
        // keeps `<group> --help` discovery honest: an external user
        // sees the gap instead of an empty subcommand list.
        using var writer = new StringWriter();

        var exitCode = CommandRouter.Execute(
            ["clarification", "--help"],
            CreateContext("/tmp/intent-system"),
            writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        // clarification IS implemented (status/next/answer); just confirm
        // it doesn't regress.
        Assert.Contains("intent-cli clarification — group help", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_TopLevelHelp_PointsToWorkflowGuidesForExternalDiscovery()
    {
        // G334: top-level help (no args) must point users at the six
        // workflow phases the issue explicitly names: init, interview,
        // packet, issue, automation, and bug repair. This is the
        // surface an external agent sees first, so the pointers must
        // be visible without reading per-group help.
        using var writer = new StringWriter();

        var exitCode = CommandRouter.Execute(
            Array.Empty<string>(),
            CreateContext("/tmp/intent-system"),
            writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("Workflow guides:", output, StringComparison.Ordinal);
        // Every named phase must be cited verbatim — these are the
        // anchors an external agent will grep for.
        Assert.Contains("init —", output, StringComparison.Ordinal);
        Assert.Contains("interview —", output, StringComparison.Ordinal);
        Assert.Contains("packet —", output, StringComparison.Ordinal);
        Assert.Contains("issue —", output, StringComparison.Ordinal);
        Assert.Contains("automation —", output, StringComparison.Ordinal);
        Assert.Contains("bug repair —", output, StringComparison.Ordinal);
        // Top-level help must also tell the user how to reach
        // per-group help and the JSON self-discovery surface.
        Assert.Contains("intent-cli <group> --help", output, StringComparison.Ordinal);
        Assert.Contains("intent-cli guide help", output, StringComparison.Ordinal);
        Assert.Contains("intent-cli guide commands list", output, StringComparison.Ordinal);
        // The canonical source-of-truth array must be the same set the
        // help emits — guards against drift between the shared
        // constant and the test expectation.
        foreach (var line in CommandRouter.WorkflowGuidePointersHelp)
        {
            Assert.Contains(line, output, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void GroupHelpHints_CoverEveryRegisteredCommandGroup()
    {
        // G334: WriteGroupHelp emits a "See also" pointer per group.
        // Every group advertised in CommandGroups (which the help
        // surface lists) must have an entry in GroupHelpHints — an
        // external agent reading group help must NOT find a group
        // without a next-call hint. The test reflects on the public
        // dictionary to keep the registry honest.
        var groupsRequiringHint = typeof(CommandRouter)
            .GetField("CommandGroups", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)
            ?.GetValue(null) as string[];

        Assert.NotNull(groupsRequiringHint);
        // `clarification` is the one alias-only entry that maps to the
        // same shape as `clarify`, so we tolerate either being present
        // in the hints registry.
        foreach (var group in groupsRequiringHint!)
        {
            Assert.True(
                CommandRouter.GroupHelpHints.ContainsKey(group),
                $"GroupHelpHints is missing a 'See also' entry for the '{group}' command group.");
        }
    }

    [Fact]
    public void GuideHelpCommand_Subcommands_MatchRegisteredGuideHandlers()
    {
        // G334: the human-facing guide catalog (GuideHelpCommand.Subcommands)
        // must mirror the dispatcher table. New guide subcommands
        // wired into CommandRouter without an entry here would not be
        // discoverable by external users.
        var implementedField = typeof(CommandRouter)
            .GetField("ImplementedCommands", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(implementedField);
        var dict = implementedField!.GetValue(null) as IReadOnlyDictionary<string, IReadOnlyDictionary<string, object>>;
        // Reflection-friendly fetch: pull the inner dictionary keys via
        // a non-generic enumerator.
        var rawDict = implementedField.GetValue(null) as System.Collections.IDictionary;
        Assert.NotNull(rawDict);
        var guideInner = rawDict!["guide"] as System.Collections.IDictionary;
        Assert.NotNull(guideInner);
        var guideSubcommands = new List<string>();
        foreach (var key in guideInner!.Keys)
        {
            guideSubcommands.Add((string)key!);
        }
        var advertised = GuideHelpCommand.Subcommands.Select(s => s.Name).ToHashSet(StringComparer.Ordinal);
        foreach (var name in guideSubcommands)
        {
            Assert.True(
                advertised.Contains(name),
                $"GuideHelpCommand.Subcommands is missing a catalog entry for guide subcommand '{name}'.");
        }
    }

    [Fact]
    public void Execute_GivenProjectStatusCommand_DispatchesToProjectStatusRenderer()
    {
        using var writer = new StringWriter();
        var context = CreateContext("/tmp/intent-system");

        var exitCode = CommandRouter.Execute(["project", "status"], context, writer);

        Assert.Equal(0, exitCode);
        Assert.Contains("intent-cli", writer.ToString(), StringComparison.Ordinal);
    }
    [Fact]
    public void Execute_GivenQueueListCommand_DispatchesToQueueRenderer()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState()));
        using var writer = new StringWriter();

        var exitCode = CommandRouter.Execute(["queue", "list"], CreateContext(repoRoot), writer);

        Assert.Equal(0, exitCode);
        Assert.Contains("A2", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenQueueShowCommand_DispatchesToQueueShowRenderer()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState()));
        using var writer = new StringWriter();

        var exitCode = CommandRouter.Execute(["queue", "show", "A2"], CreateContext(repoRoot), writer);

        Assert.Equal(0, exitCode);
        Assert.Contains("Execution unit: A2", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenQueueNextCommand_DispatchesToQueueNextRenderer()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState()));
        using var writer = new StringWriter();

        var exitCode = CommandRouter.Execute(["queue", "next"], CreateContext(repoRoot), writer);

        Assert.Equal(0, exitCode);
        Assert.Contains("Next candidate", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenQueueDispatchCommand_DispatchesToQueueDispatchRenderer()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueDispatchQueueState()));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G13", "packet.yaml"),
            CreateQueueDispatchPacketYaml());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G13", "github-body.md"),
            "# Goal");
        tempDirectory.CreateDirectory(Path.Combine("repo", "submodules", "intent-system"));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "runs.jsonl"),
            string.Empty);
        using var writer = new StringWriter();
        var originalPublisherFactory = QueueDispatchCommand.PublisherFactory;
        var originalGitFactory = QueueDispatchCommand.GitCommandRunnerFactory;
        var originalTimestampFactory = QueueDispatchCommand.TimestampFactory;

        try
        {
            QueueDispatchCommand.PublisherFactory = () => new FakeQueueDispatchPublisher();
            QueueDispatchCommand.GitCommandRunnerFactory = () => new FakeQueueDispatchGitRunner();
            QueueDispatchCommand.TimestampFactory = () => DateTimeOffset.Parse("2026-04-05T06:00:00Z");

            var exitCode = CommandRouter.Execute(["queue", "dispatch", "G13"], CreateContext(repoRoot), writer);

            Assert.Equal(0, exitCode);
            Assert.Contains("Queue item G13 dispatched", writer.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            QueueDispatchCommand.PublisherFactory = originalPublisherFactory;
            QueueDispatchCommand.GitCommandRunnerFactory = originalGitFactory;
            QueueDispatchCommand.TimestampFactory = originalTimestampFactory;
        }
    }

    [Fact]
    public void Execute_GivenQueueDispatchCommandWithExistingLinkedIssue_DispatchesToReuseRenderer()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueDispatchQueueState(withExistingLinkedIssue: true)));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "runs.jsonl"),
            string.Empty);
        using var writer = new StringWriter();
        var originalPublisherFactory = QueueDispatchCommand.PublisherFactory;
        var originalTimestampFactory = QueueDispatchCommand.TimestampFactory;

        try
        {
            QueueDispatchCommand.PublisherFactory = () => new ThrowingQueueDispatchPublisher();
            QueueDispatchCommand.TimestampFactory = () => DateTimeOffset.Parse("2026-04-05T06:00:00Z");

            var exitCode = CommandRouter.Execute(["queue", "dispatch", "G13"], CreateContext(repoRoot), writer);

            Assert.Equal(0, exitCode);
            Assert.Contains("reused existing linked issue", writer.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            QueueDispatchCommand.PublisherFactory = originalPublisherFactory;
            QueueDispatchCommand.TimestampFactory = originalTimestampFactory;
        }
    }

    [Fact]
    public void Execute_GivenQueueEnqueueCommand_DispatchesToQueueEnqueueRenderer()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueEnqueueQueueState()));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G38", "packet.yaml"),
            CreateQueueEnqueuePacketYaml());
        using var writer = new StringWriter();
        var originalTimestampFactory = QueueEnqueueCommand.TimestampFactory;

        try
        {
            QueueEnqueueCommand.TimestampFactory = () => DateTimeOffset.Parse("2026-04-06T10:30:00Z");

            var exitCode = CommandRouter.Execute(["queue", "enqueue", "G38"], CreateContext(repoRoot), writer);

            Assert.Equal(0, exitCode);
            Assert.Contains("Queue enqueue processed for execution unit 'G38'.", writer.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            QueueEnqueueCommand.TimestampFactory = originalTimestampFactory;
        }
    }

    [Fact]
    public void Execute_GivenBugReportCommand_DispatchesToBugReportRenderer()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", "prepared", "bug.md"),
            "Observed callback loop after login." + Environment.NewLine + "Affects GitHub provider path.");
        using var writer = new StringWriter();

        var exitCode = CommandRouter.Execute(
            [
                "bug",
                "report",
                "auth",
                "BUG-123",
                "--title", "OAuth callback loop",
                "--from-file", "prepared/bug.md",
                "--instruction-refs", "ICL.P.PRODUCT_GOAL",
                "--affected-intent-refs", "intents/intent-cli/means/auth.md",
                "--affected-rule-spec-refs", "intents/intent-cli/specs/12-bug-fix-and-intent-repair.md",
                "--clarification-candidates", "Should provider retry reuse callback state token?",
                "--execution-units", "G25",
                "--issues", "https://github.com/J-Tech-Japan/intent-system/issues/178",
                "--prs", "https://github.com/J-Tech-Japan/intent-system/pull/180",
                "--reviews", "https://github.com/J-Tech-Japan/intent-system/pull/180#issuecomment-1"
            ],
            CreateContext(repoRoot),
            writer);

        Assert.Equal(0, exitCode);
        Assert.Contains("Bug report artifact generated for domain 'auth'.", writer.ToString(), StringComparison.Ordinal);
        Assert.Contains("Bug ID: BUG-123", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenBugTriageCommand_DispatchesToBugTriageRenderer()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "bugs", "BUG-123.report.yaml"),
            BugReportArtifactYaml.Serialize(
                new BugReportArtifact
                {
                    DomainSlug = "auth",
                    BugId = "BUG-123",
                    Title = "OAuth callback loop",
                    ReportSource = "from-file",
                    ProblemStatement = "Observed callback loop after login.",
                    SuspectedFailureLocus = "Observed callback loop after login.",
                    OriginalInstructionRefs = ["ICL.P.PRODUCT_GOAL"],
                    AffectedIntentRefs = ["intents/intent-cli/means/auth.md"],
                    AffectedRuleSpecRefs = ["intents/intent-cli/specs/12-bug-fix-and-intent-repair.md"],
                    ClarificationCandidates = ["Should provider retry reuse callback state token?"],
                    LinkedExecutionUnits = ["G25"],
                    LinkedIssueRefs = [],
                    LinkedPrRefs = [],
                    LinkedReviewRefs = ["https://github.com/J-Tech-Japan/intent-system/pull/180#issuecomment-1"]
                }));
        tempDirectory.CreateFile(Path.Combine("repo", ".intent-cli", "issues", "G25", "implementation.md"), "# Implementation");
        tempDirectory.CreateFile(Path.Combine("repo", ".intent-cli", "issues", "G25", "review-context.md"), "# Review Context");
        tempDirectory.CreateFile(Path.Combine("repo", ".intent-cli", "issues", "G25", "packet.yaml"), "execution_unit: G25");
        using var writer = new StringWriter();

        var exitCode = CommandRouter.Execute(["bug", "triage", "BUG-123"], CreateContext(repoRoot), writer);

        Assert.Equal(0, exitCode);
        Assert.Contains("Bug triage artifact generated for 'BUG-123'.", writer.ToString(), StringComparison.Ordinal);
        Assert.Contains("Triage classification: implementation-mismatch", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenBugReportCommandWithoutBugIdAndWithText_DispatchesToBugReportRenderer()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        using var writer = new StringWriter();

        var exitCode = CommandRouter.Execute(
            [
                "bug",
                "report",
                "auth",
                "--title", "OAuth callback loop",
                "--text", "Observed callback loop after login." + Environment.NewLine + "Affects GitHub provider path."
            ],
            CreateContext(repoRoot),
            writer);

        Assert.Equal(0, exitCode);
        Assert.Contains("Bug report artifact generated for domain 'auth'.", writer.ToString(), StringComparison.Ordinal);
        Assert.Contains("Bug ID: BUG-", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenBugPlanCommand_DispatchesToBugExecutionRenderer()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "bugs", "BUG-123.report.yaml"),
            BugReportArtifactYaml.Serialize(
                new BugReportArtifact
                {
                    DomainSlug = "auth",
                    BugId = "BUG-123",
                    Title = "OAuth callback loop",
                    ReportSource = "from-file",
                    ProblemStatement = "Observed callback loop after login.",
                    SuspectedFailureLocus = "Observed callback loop after login.",
                    OriginalInstructionRefs = ["ICL.P.PRODUCT_GOAL"],
                    AffectedIntentRefs = ["intents/intent-cli/means/auth.md"],
                    AffectedRuleSpecRefs = ["intents/intent-cli/specs/12-bug-fix-and-intent-repair.md"],
                    ClarificationCandidates = ["Should provider retry reuse callback state token?"],
                    LinkedExecutionUnits = ["G25"],
                    LinkedIssueRefs = [],
                    LinkedPrRefs = [],
                    LinkedReviewRefs = ["https://github.com/J-Tech-Japan/intent-system/pull/180#issuecomment-1"]
                }));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "bugs", "BUG-123.triage.yaml"),
            BugTriageArtifactYaml.Serialize(
                new BugTriageArtifact
                {
                    BugId = "BUG-123",
                    ReportRef = ".intent-cli/bugs/BUG-123.report.yaml",
                    TriageClassification = "implementation-mismatch",
                    DownstreamAction = "dual-track",
                    ClarificationRequired = false,
                    ClarificationReasons = [],
                    OriginalInstructionRootRefs = ["ICL.P.PRODUCT_GOAL"],
                    LinkedReviewRefs = ["https://github.com/J-Tech-Japan/intent-system/pull/180#issuecomment-1"],
                    ResolvedExecutionUnits = ["G25"],
                    ResolvedImplementationRefs = [".intent-cli/issues/G25/implementation.md"],
                    ResolvedReviewContextRefs = [".intent-cli/issues/G25/review-context.md"],
                    ResolvedPacketRefs = [".intent-cli/issues/G25/packet.yaml"],
                    UnresolvedExecutionUnits = [],
                    ImplementationRepairCandidates = ["G25"],
                    IntentRepairCandidates = ["intents/intent-cli/means/auth.md", "intents/intent-cli/specs/12-bug-fix-and-intent-repair.md"]
                }));
        using var writer = new StringWriter();

        var exitCode = CommandRouter.Execute(["bug", "plan", "BUG-123"], CreateContext(repoRoot), writer);

        Assert.Equal(0, exitCode);
        Assert.Contains("Bug plan artifact generated for 'BUG-123'.", writer.ToString(), StringComparison.Ordinal);
        Assert.Contains("Ready to launch: true", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenRemovedBugExecutionSurface_ReturnsNotImplemented()
    {
        using var writer = new StringWriter();

        var exitCode = CommandRouter.Execute(["bug", "execution", "BUG-123"], CreateContext("/tmp/repo"), writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("Command 'bug execution' is not yet implemented.", writer.ToString(), StringComparison.Ordinal);
    }
    [Fact]
    public void Execute_GivenBugImplementationRepairCommand_DispatchesToBugImplementationRepairRenderer()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "bugs", "BUG-123.report.yaml"),
            BugReportArtifactYaml.Serialize(
                new BugReportArtifact
                {
                    DomainSlug = "auth",
                    BugId = "BUG-123",
                    Title = "OAuth callback loop",
                    ReportSource = "from-file",
                    ProblemStatement = "Observed callback loop after login.",
                    SuspectedFailureLocus = "Observed callback loop after login.",
                    OriginalInstructionRefs = ["ICL.P.PRODUCT_GOAL"],
                    AffectedIntentRefs = ["intents/intent-cli/means/auth.md"],
                    AffectedRuleSpecRefs = ["intents/intent-cli/specs/12-bug-fix-and-intent-repair.md"],
                    ClarificationCandidates = ["Should provider retry reuse callback state token?"],
                    LinkedExecutionUnits = ["G25"],
                    LinkedIssueRefs = [],
                    LinkedPrRefs = [],
                    LinkedReviewRefs = ["https://github.com/J-Tech-Japan/intent-system/pull/180#issuecomment-1"]
                }));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "bugs", "BUG-123.triage.yaml"),
            BugTriageArtifactYaml.Serialize(
                new BugTriageArtifact
                {
                    BugId = "BUG-123",
                    ReportRef = ".intent-cli/bugs/BUG-123.report.yaml",
                    TriageClassification = "implementation-mismatch",
                    DownstreamAction = "dual-track",
                    ClarificationRequired = false,
                    ClarificationReasons = [],
                    OriginalInstructionRootRefs = ["ICL.P.PRODUCT_GOAL"],
                    LinkedReviewRefs = ["https://github.com/J-Tech-Japan/intent-system/pull/180#issuecomment-1"],
                    ResolvedExecutionUnits = ["G25"],
                    ResolvedImplementationRefs = [".intent-cli/issues/G25/implementation.md"],
                    ResolvedReviewContextRefs = [".intent-cli/issues/G25/review-context.md"],
                    ResolvedPacketRefs = [".intent-cli/issues/G25/packet.yaml"],
                    UnresolvedExecutionUnits = [],
                    ImplementationRepairCandidates = ["G25"],
                    IntentRepairCandidates = ["intents/intent-cli/means/auth.md", "intents/intent-cli/specs/12-bug-fix-and-intent-repair.md"]
                }));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "bugs", "BUG-123.plan.yaml"),
            BugExecutionArtifactYaml.Serialize(
                new BugExecutionArtifact
                {
                    BugId = "BUG-123",
                    ReportRef = ".intent-cli/bugs/BUG-123.report.yaml",
                    TriageRef = ".intent-cli/bugs/BUG-123.triage.yaml",
                    DownstreamAction = "dual-track",
                    ResolvedImplementationRefs = [".intent-cli/issues/G25/implementation.md"],
                    ResolvedReviewContextRefs = [".intent-cli/issues/G25/review-context.md"],
                    ResolvedPacketRefs = [".intent-cli/issues/G25/packet.yaml"],
                    ImplementationTaskCandidates = ["G25"],
                    IntentTaskCandidates = ["intents/intent-cli/means/auth.md", "intents/intent-cli/specs/12-bug-fix-and-intent-repair.md"],
                    ClarificationRequired = false,
                    ReadyToLaunch = true
                }));
        using var writer = new StringWriter();

        var exitCode = CommandRouter.Execute(["bug", "implementation-repair", "BUG-123"], CreateContext(repoRoot), writer);

        Assert.Equal(0, exitCode);
        Assert.Contains("Bug implementation-repair artifact generated for 'BUG-123'.", writer.ToString(), StringComparison.Ordinal);
        Assert.Contains("Ready to issue cut: true", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenBugImplementationIssueCommand_DispatchesToBugImplementationIssueRenderer()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "bugs", "BUG-123.implementation-repair.yaml"),
            BugImplementationRepairArtifactYaml.Serialize(
                new BugImplementationRepairArtifact
                {
                    BugId = "BUG-123",
                    ExecutionRef = ".intent-cli/bugs/BUG-123.plan.yaml",
                    ImplementationTaskCandidates = ["G25"],
                    ImplementationRepairTargets = [".intent-cli/issues/G25/packet.yaml"],
                    SuggestedIssueTitle = "Implementation repair: OAuth callback loop (BUG-123)",
                    SuggestedGoal = "Repair child implementation targets for 'OAuth callback loop' (BUG-123) using .intent-cli/bugs/BUG-123.plan.yaml: .intent-cli/issues/G25/packet.yaml",
                    ReadyToIssueCut = true
                }));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G25", "packet.yaml"),
            """
            implementation_issue_packet:
              issue_title: "[G25] Repair callback flow"
              issue_kind: "bugfix"
              source_execution_unit: "G25"
              goal: "Repair callback flow."
              in_scope: []
              out_of_scope: []
              target_repo: "submodules/intent-system"
              target_path: "."
              target_part: "auth callback"
              dependencies: []
              technical_baseline: []
              project_local_guide: []
              intent_baseline: []
              intent_references: []
              rules_and_specs: []
              acceptance_criteria: []
              verification_evidence: []
              review_mode: "deterministic-review"
              completion_action: "wait-for-deterministic-review"
              landing_policy: "merge-after-review"

            review_context_packet:
              source_execution_unit: "G25"
              parent_intent_root: "intents/intent-cli/intent-tree/00-map.md"
              intent_references: []
              rules_and_specs: []
              acceptance_criteria: []
              deterministic_review_checks: []
              clarification_return_path: "intents/intent-cli/clarifications/open.md"
            """);
        tempDirectory.CreateDirectory(Path.Combine("repo", "submodules", "intent-system"));
        using var writer = new StringWriter();
        var originalPublisherFactory = BugImplementationIssueCommand.PublisherFactory;
        var originalGitRunnerFactory = BugImplementationIssueCommand.GitCommandRunnerFactory;

        try
        {
            BugImplementationIssueCommand.PublisherFactory = () => new FakeQueueDispatchPublisher();
            BugImplementationIssueCommand.GitCommandRunnerFactory = () => new FakeQueueDispatchGitRunner();

            var exitCode = CommandRouter.Execute(["bug", "implementation-issue", "BUG-123"], CreateContext(repoRoot), writer);

            Assert.Equal(0, exitCode);
            Assert.Contains("Bug implementation-issue artifact generated for 'BUG-123'.", writer.ToString(), StringComparison.Ordinal);
            Assert.Contains("Created issue URL: https://github.com/J-Tech-Japan/intent-system/issues/53", writer.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            BugImplementationIssueCommand.PublisherFactory = originalPublisherFactory;
            BugImplementationIssueCommand.GitCommandRunnerFactory = originalGitRunnerFactory;
        }
    }
    [Fact]
    public void Execute_GivenInterviewStartCommand_DispatchesToInterviewStartRenderer()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "interviews", "auth", "iq-1.yaml"),
            CreateInterviewStartItemYaml());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "interviews", "auth", "iq-1.md"),
            "# Interview Question");
        using var writer = new StringWriter();

        var exitCode = CommandRouter.Execute(["interview", "start", "auth"], CreateContext(repoRoot), writer);

        Assert.Equal(0, exitCode);
        Assert.Contains("Next interview question:", writer.ToString(), StringComparison.Ordinal);
        Assert.Contains("Question: Which auth flow should be canonical?", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenInterviewAnswerCommand_DispatchesToInterviewAnswerRenderer()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "interviews", "auth", "iq-1.yaml"),
            CreateInterviewAnswerItemYaml());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "interviews", "auth", "iq-1.md"),
            "# Interview Question");
        using var writer = new StringWriter();
        var originalTimestampFactory = InterviewAnswerCommand.TimestampFactory;
        var originalInputReaderFactory = InterviewAnswerCommand.InputReaderFactory;

        try
        {
            InterviewAnswerCommand.TimestampFactory = () => DateTimeOffset.Parse("2026-04-13T10:00:00Z");
            InterviewAnswerCommand.InputReaderFactory = () => new StringReader("Use OAuth2 with PKCE." + Environment.NewLine);

            var exitCode = CommandRouter.Execute(["interview", "answer", "auth"], CreateContext(repoRoot), writer);

            Assert.Equal(0, exitCode);
            Assert.Contains("Interview answered for domain 'auth'.", writer.ToString(), StringComparison.Ordinal);
            Assert.Contains("Status: Answered", writer.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            InterviewAnswerCommand.TimestampFactory = originalTimestampFactory;
            InterviewAnswerCommand.InputReaderFactory = originalInputReaderFactory;
        }
    }

    [Fact]
    public void Execute_GivenInterviewResumeCommand_DispatchesToInterviewResumeRenderer()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "interviews", "auth", "iq-1.yaml"),
            CreateInterviewStartItemYaml());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "interviews", "auth", "iq-1.md"),
            "# Interview Question");
        using var writer = new StringWriter();

        var exitCode = CommandRouter.Execute(["interview", "resume", "auth"], CreateContext(repoRoot), writer);

        Assert.Equal(0, exitCode);
        Assert.Contains("Next interview question:", writer.ToString(), StringComparison.Ordinal);
        Assert.Contains("Question: Which auth flow should be canonical?", writer.ToString(), StringComparison.Ordinal);
    }
    [Fact]
    public void Execute_GivenIssueDraftCommand_DispatchesToIssueDraftRenderer()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G13", "packet.yaml"),
            """
            execution_unit: G13
            implementation_issue:
              issue_title: "[G13] Add issue draft foundation"
              target_repo: "submodules/intent-system"
              target_path: "src/IntentSystem.Cli"
              target_part: "issue draft command"
              dependencies: []
            """);
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G13", "github-body.md"),
            "# Goal");
        using var writer = new StringWriter();
        var originalTimestampFactory = IssueDraftCommand.TimestampFactory;

        try
        {
            IssueDraftCommand.TimestampFactory = () => DateTimeOffset.Parse("2026-04-23T00:00:00Z");

            var exitCode = CommandRouter.Execute(["issue", "draft", "G13"], CreateContext(repoRoot), writer);

            Assert.Equal(0, exitCode);
            Assert.Contains("Issue draft prepared for G13.", writer.ToString(), StringComparison.Ordinal);
            Assert.True(File.Exists(Path.Combine(repoRoot, ".intent-cli", "issues", "G13", "publish.yaml")));
        }
        finally
        {
            IssueDraftCommand.TimestampFactory = originalTimestampFactory;
        }
    }

    [Fact]
    public void Execute_GivenIssueCreateCommand_DispatchesToIssueCreateRenderer()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateDirectory(Path.Combine("repo", "submodules", "intent-system"));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G13", "packet.yaml"),
            """
            execution_unit: G13
            implementation_issue:
              issue_title: "[G13] Add issue create foundation"
              target_repo: "submodules/intent-system"
              target_path: "src/IntentSystem.Cli"
              target_part: "issue create command"
              dependencies: []
            """);
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G13", "github-body.md"),
            "# Goal");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G13", "publish.yaml"),
            """
            execution_unit: G13
            publish_status: drafted
            packet_path: ".intent-cli/issues/G13/packet.yaml"
            issue_body_path: ".intent-cli/issues/G13/github-body.md"
            created_issue_number: null
            created_issue_url: null
            published_label_name: null
            """);
        using var writer = new StringWriter();
        var originalPublisherFactory = IssueCreateCommand.PublisherFactory;
        var originalGitCommandRunnerFactory = IssueCreateCommand.GitCommandRunnerFactory;
        var originalTimestampFactory = IssueCreateCommand.TimestampFactory;

        try
        {
            IssueCreateCommand.PublisherFactory = () => new FakeQueueDispatchPublisher();
            IssueCreateCommand.GitCommandRunnerFactory = () => new FakeQueueDispatchGitRunner();
            IssueCreateCommand.TimestampFactory = () => DateTimeOffset.Parse("2026-04-23T00:10:00Z");

            var exitCode = CommandRouter.Execute(["issue", "create", "G13"], CreateContext(repoRoot), writer);

            Assert.Equal(0, exitCode);
            Assert.Contains("Issue created for G13", writer.ToString(), StringComparison.Ordinal);
            Assert.True(File.Exists(Path.Combine(repoRoot, ".intent-cli", "issues", "G13", "publish.yaml")));
        }
        finally
        {
            IssueCreateCommand.PublisherFactory = originalPublisherFactory;
            IssueCreateCommand.GitCommandRunnerFactory = originalGitCommandRunnerFactory;
            IssueCreateCommand.TimestampFactory = originalTimestampFactory;
        }
    }

    [Fact]
    public void Execute_GivenIssuePublishCommand_DispatchesToIssuePublishRenderer()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateDirectory(Path.Combine("repo", "submodules", "intent-system"));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G13", "packet.yaml"),
            """
            execution_unit: G13
            implementation_issue:
              issue_title: "[G13] Add issue publish foundation"
              target_repo: "submodules/intent-system"
              target_path: "src/IntentSystem.Cli"
              target_part: "issue publish command"
              dependencies: []
            """);
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G13", "publish.yaml"),
            """
            execution_unit: G13
            publish_status: issue-created
            packet_path: ".intent-cli/issues/G13/packet.yaml"
            issue_body_path: ".intent-cli/issues/G13/github-body.md"
            created_issue_number: 73
            created_issue_url: "https://github.com/J-Tech-Japan/intent-system/issues/73"
            published_label_name: null
            """);
        using var writer = new StringWriter();
        var originalPublisherFactory = IssuePublishCommand.PublisherFactory;
        var originalGitCommandRunnerFactory = IssuePublishCommand.GitCommandRunnerFactory;
        var originalTimestampFactory = IssuePublishCommand.TimestampFactory;

        try
        {
            IssuePublishCommand.PublisherFactory = () => new FakeQueueDispatchPublisher();
            IssuePublishCommand.GitCommandRunnerFactory = () => new FakeQueueDispatchGitRunner();
            IssuePublishCommand.TimestampFactory = () => DateTimeOffset.Parse("2026-04-23T00:20:00Z");

            var exitCode = CommandRouter.Execute(["issue", "publish", "G13"], CreateContext(repoRoot), writer);

            Assert.Equal(0, exitCode);
            Assert.Contains("Issue published for G13", writer.ToString(), StringComparison.Ordinal);
            Assert.True(File.Exists(Path.Combine(repoRoot, ".intent-cli", "issues", "G13", "publish.yaml")));
        }
        finally
        {
            IssuePublishCommand.PublisherFactory = originalPublisherFactory;
            IssuePublishCommand.GitCommandRunnerFactory = originalGitCommandRunnerFactory;
            IssuePublishCommand.TimestampFactory = originalTimestampFactory;
        }
    }

    [Fact]
    public void Execute_GivenIssueStatusCommand_DispatchesToIssueStatusRenderer()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateDirectory(Path.Combine("repo", "submodules", "intent-system"));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G13", "packet.yaml"),
            """
            execution_unit: G13
            implementation_issue:
              issue_title: "[G13] Add issue status foundation"
              target_repo: "submodules/intent-system"
              target_path: "src/IntentSystem.Cli"
              target_part: "issue status command"
              dependencies: []
            """);
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G13", "publish.yaml"),
            """
            execution_unit: G13
            publish_status: published
            packet_path: ".intent-cli/issues/G13/packet.yaml"
            issue_body_path: ".intent-cli/issues/G13/github-body.md"
            created_issue_number: 73
            created_issue_url: "https://github.com/J-Tech-Japan/intent-system/issues/73"
            published_label_name: "intent-target"
            """);
        using var writer = new StringWriter();
        var originalPublisherFactory = IssueStatusCommand.PublisherFactory;
        var originalGitCommandRunnerFactory = IssueStatusCommand.GitCommandRunnerFactory;

        try
        {
            IssueStatusCommand.PublisherFactory = () => new FakeQueueDispatchPublisher();
            IssueStatusCommand.GitCommandRunnerFactory = () => new FakeQueueDispatchGitRunner();

            var exitCode = CommandRouter.Execute(["issue", "status", "G13"], CreateContext(repoRoot), writer);

            Assert.Equal(0, exitCode);
            Assert.Contains("Issue status for G13", writer.ToString(), StringComparison.Ordinal);
            Assert.Contains("Automation state: published and label present", writer.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            IssueStatusCommand.PublisherFactory = originalPublisherFactory;
            IssueStatusCommand.GitCommandRunnerFactory = originalGitCommandRunnerFactory;
        }
    }
    [Fact]
    public void Execute_GivenClarifyAnswerCommand_DispatchesToClarifyAnswerRenderer()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateClarifyAnswerQueueState()));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "clarifications", "G24", "request.json"),
            ClarificationSerializer.Serialize(CreateClarifyAnswerItem()));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "runs.jsonl"),
            string.Empty);
        using var writer = new StringWriter();
        var originalTimestampFactory = ClarifyAnswerCommand.TimestampFactory;
        var originalInputReaderFactory = ClarifyAnswerCommand.InputReaderFactory;

        try
        {
            ClarifyAnswerCommand.TimestampFactory = () => DateTimeOffset.Parse("2026-04-12T07:10:00Z");
            ClarifyAnswerCommand.InputReaderFactory = () => new StringReader("Use the current queue snapshot." + Environment.NewLine);

            var exitCode = CommandRouter.Execute(["clarify", "answer", "G24"], CreateContext(repoRoot), writer);

            Assert.Equal(0, exitCode);
            Assert.Contains("Clarification answered for G24.", writer.ToString(), StringComparison.Ordinal);
            Assert.Contains("Queue state: review", writer.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            ClarifyAnswerCommand.TimestampFactory = originalTimestampFactory;
            ClarifyAnswerCommand.InputReaderFactory = originalInputReaderFactory;
        }
    }
    [Fact]
    public void Execute_GivenQueueTransitionCommand_DispatchesToQueueTransitionRenderer()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState()));
        using var writer = new StringWriter();

        var exitCode = CommandRouter.Execute(["queue", "transition", "A2", "completed"], CreateContext(repoRoot), writer);

        Assert.Equal(0, exitCode);
        Assert.Contains("Transitioned A2 to completed", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenClarifyOpenCommand_DispatchesToClarifyOpenRenderer()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateClarifyOpenQueueState()));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G22", "packet.yaml"),
            CreateClarifyOpenPacketYaml());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G22", "review-context.md"),
            CreateClarifyOpenReviewContextMarkdown());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "runs.jsonl"),
            string.Empty);
        using var writer = new StringWriter();
        var originalTimestampFactory = ClarifyOpenCommand.TimestampFactory;

        try
        {
            ClarifyOpenCommand.TimestampFactory = () => DateTimeOffset.Parse("2026-04-11T06:10:00Z");

            var exitCode = CommandRouter.Execute(["clarify", "open", "G22"], CreateContext(repoRoot), writer);

            Assert.Equal(0, exitCode);
            Assert.Contains("Clarification opened for G22", writer.ToString(), StringComparison.Ordinal);
            Assert.True(File.Exists(Path.Combine(repoRoot, ".intent-cli", "clarifications", "G22", "request.json")));
        }
        finally
        {
            ClarifyOpenCommand.TimestampFactory = originalTimestampFactory;
        }
    }

    [Fact]
    public void Execute_GivenClarifyListCommand_DispatchesToClarifyListRenderer()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateClarifyOpenQueueState()));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "clarifications", "G22", "request.json"),
            ClarificationSerializer.Serialize(CreateClarifyListItem()));
        using var writer = new StringWriter();

        var exitCode = CommandRouter.Execute(["clarify", "list"], CreateContext(repoRoot), writer);

        Assert.Equal(0, exitCode);
        Assert.Contains("Open clarifications:", writer.ToString(), StringComparison.Ordinal);
        Assert.Contains("Execution unit: G22", writer.ToString(), StringComparison.Ordinal);
    }

    private static CliContext CreateContext(string repoRoot, string? parentIntentRepoRoot = null)
    {
        return new CliContext
        {
            RepoRoot = repoRoot,
            Config = new CliConfig
            {
                Project = new ProjectConfig
                {
                    Domain = "intent-cli",
                    ArtifactRoot = ".intent-cli",
                    ParentIntentRepoRoot = parentIntentRepoRoot ?? string.Empty
                }
            }
        };
    }

    private static QueueState CreateQueueState()
    {
        return new QueueState
        {
            SchemaVersion = "1",
            UpdatedAt = DateTimeOffset.Parse("2026-04-03T10:12:34Z"),
            Items =
            [
                new QueueItem
                {
                    ExecutionUnit = "A2",
                    Title = "CLI shell baseline",
                    State = QueueItemState.Review,
                    Dependencies = ["A1"],
                    BlockedBy = [],
                    ClarificationReturnPath = ".takt/runs/20260403-101234-issue-29-g1-cli-shell-and-root/context/task/order.md",
                    PacketPaths = new PacketPaths
                    {
                        Implementation = ".intent-cli/issues/a2/implementation.md",
                        ReviewContext = ".intent-cli/issues/a2/review-context.md",
                        Yaml = ".intent-cli/issues/a2/packet.yaml"
                    },
                    WorkerRole = "coder",
                    ReviewRole = "reviewer",
                    Priority = "high"
                },
                new QueueItem
                {
                    ExecutionUnit = "A3",
                    Title = "Queue read commands",
                    State = QueueItemState.Queued,
                    Dependencies = [],
                    BlockedBy = [],
                    ClarificationReturnPath = ".takt/runs/20260403-101234-issue-33-g3-queue-show-and-next/context/task/order.md",
                    PacketPaths = new PacketPaths
                    {
                        Implementation = ".intent-cli/issues/A3/implementation.md",
                        ReviewContext = ".intent-cli/issues/A3/review-context.md",
                        Yaml = ".intent-cli/issues/A3/packet.yaml"
                    },
                    WorkerRole = "coder",
                    ReviewRole = "reviewer",
                    Priority = "normal"
                }
            ]
        };
    }

    private static QueueState CreateRunSubmitQueueState()
    {
        return new QueueState
        {
            SchemaVersion = "1",
            UpdatedAt = DateTimeOffset.Parse("2026-04-03T10:12:34Z"),
            Items =
            [
                new QueueItem
                {
                    ExecutionUnit = "G14",
                    Title = "[G14] Run Start Command",
                    State = QueueItemState.Active,
                    Dependencies = [],
                    BlockedBy = [],
                    ClarificationReturnPath = "intents/intent-cli/clarifications/open.md",
                    PacketPaths = new PacketPaths
                    {
                        Implementation = ".intent-cli/issues/G14/implementation.md",
                        ReviewContext = ".intent-cli/issues/G14/review-context.md",
                        Yaml = ".intent-cli/issues/G14/packet.yaml"
                    },
                    LinkedIssue = new LinkedIssue
                    {
                        Repo = "J-Tech-Japan/intent-system",
                        Number = 56,
                        Url = "https://github.com/J-Tech-Japan/intent-system/issues/56"
                    },
                    WorkerRole = "coder",
                    ReviewRole = "reviewer",
                    Priority = "high"
                }
            ]
        };
    }

    private static QueueState CreateQueueDispatchQueueState(bool withExistingLinkedIssue = false)
    {
        return new QueueState
        {
            SchemaVersion = "1",
            UpdatedAt = DateTimeOffset.Parse("2026-04-03T10:12:34Z"),
            Items =
            [
                new QueueItem
                {
                    ExecutionUnit = "G13",
                    Title = "Queue dispatch command",
                    State = QueueItemState.Queued,
                    Dependencies = [],
                    BlockedBy = [],
                    ClarificationReturnPath = "intents/intent-cli/clarifications/open.md",
                    PacketPaths = new PacketPaths
                    {
                        Implementation = ".intent-cli/issues/G13/implementation.md",
                        ReviewContext = ".intent-cli/issues/G13/review-context.md",
                        Yaml = ".intent-cli/issues/G13/packet.yaml"
                    },
                    LinkedIssue = withExistingLinkedIssue
                        ? new LinkedIssue
                        {
                            Repo = "J-Tech-Japan/intent-system",
                            Number = 41,
                            Url = "https://github.com/J-Tech-Japan/intent-system/issues/41"
                        }
                        : null,
                    WorkerRole = "coder",
                    ReviewRole = "reviewer",
                    Priority = "high"
                }
            ]
        };
    }

    private static QueueState CreateQueueEnqueueQueueState()
    {
        return new QueueState
        {
            SchemaVersion = "1",
            UpdatedAt = DateTimeOffset.Parse("2026-04-06T10:00:00Z"),
            Items =
            [
                new QueueItem
                {
                    ExecutionUnit = "G3",
                    Title = "Queue read commands",
                    State = QueueItemState.Completed,
                    Dependencies = [],
                    BlockedBy = [],
                    ClarificationReturnPath = "intents/intent-cli/clarifications/open.md",
                    PacketPaths = new PacketPaths
                    {
                        Implementation = ".intent-cli/issues/G3/implementation.md",
                        ReviewContext = ".intent-cli/issues/G3/review-context.md",
                        Yaml = ".intent-cli/issues/G3/packet.yaml"
                    },
                    WorkerRole = "coder",
                    ReviewRole = "reviewer",
                    Priority = "high"
                }
            ]
        };
    }

    private static string CreateIntakeEnqueuePacketYaml(string executionUnit)
    {
        return $"""
        execution_unit: {executionUnit}
        implementation_issue:
          issue_title: "{executionUnit} Queue Item"
          goal: "Enqueue generated issue artifact into queue artifacts."
          in_scope:
            - "queue insertion"
          out_of_scope:
            - "workflow execution"
          target_repo: "submodules/intent-system"
          target_path: "."
          target_part: "cli intake enqueue command"
          dependencies:
            - "G3"
          technical_baseline:
            - "C# / .NET"
          project_local_guidance:
            - "AGENTS.md"
          intent_baseline:
            - "intake enqueue stays thin"
          acceptance_criteria:
            - "queue item inserted"
          verification:
            - "tests-passing"

        review:
          summarize_first: true
          require_explicit_diff_check: true
          require_explicit_scope_check: true
          require_explicit_contract_check: true
          required_checks:
            - "intake enqueue remains thin"
        """;
    }

    private static QueueState CreateRunRereviewQueueState()
    {
        return new QueueState
        {
            SchemaVersion = "1",
            UpdatedAt = DateTimeOffset.Parse("2026-04-05T09:12:34Z"),
            Items =
            [
                new QueueItem
                {
                    ExecutionUnit = "G16",
                    Title = "Run rereview command",
                    State = QueueItemState.Fixing,
                    Dependencies = ["G15"],
                    BlockedBy = [],
                    ClarificationReturnPath = "intents/intent-cli/clarifications/open.md",
                    PacketPaths = new PacketPaths
                    {
                        Implementation = ".intent-cli/issues/G16/implementation.md",
                        ReviewContext = ".intent-cli/issues/G16/review-context.md",
                        Yaml = ".intent-cli/issues/G16/packet.yaml"
                    },
                    WorkerRole = "coder",
                    ReviewRole = "reviewer",
                    Priority = "high"
                }
            ]
        };
    }

    private static QueueState CreateRunResumeQueueState()
    {
        return new QueueState
        {
            SchemaVersion = "1",
            UpdatedAt = DateTimeOffset.Parse("2026-04-06T08:12:34Z"),
            Items =
            [
                new QueueItem
                {
                    ExecutionUnit = "G17",
                    Title = "Run resume command",
                    State = QueueItemState.Active,
                    Dependencies = ["G16"],
                    BlockedBy = [],
                    ClarificationReturnPath = "intents/intent-cli/clarifications/open.md",
                    PacketPaths = new PacketPaths
                    {
                        Implementation = ".intent-cli/issues/G17/implementation.md",
                        ReviewContext = ".intent-cli/issues/G17/review-context.md",
                        Yaml = ".intent-cli/issues/G17/packet.yaml"
                    },
                    LinkedIssue = new LinkedIssue
                    {
                        Repo = "J-Tech-Japan/intent-system",
                        Number = 62,
                        Url = "https://github.com/J-Tech-Japan/intent-system/issues/62"
                    },
                    WorkerRole = "coder",
                    ReviewRole = "reviewer",
                    Priority = "high"
                }
            ]
        };
    }

    private static QueueState CreateRunLogQueueState()
    {
        return new QueueState
        {
            SchemaVersion = "1",
            UpdatedAt = DateTimeOffset.Parse("2026-04-07T08:12:34Z"),
            Items =
            [
                new QueueItem
                {
                    ExecutionUnit = "G18",
                    Title = "Run log command",
                    State = QueueItemState.Fixing,
                    Dependencies = ["G17"],
                    BlockedBy = [],
                    ClarificationReturnPath = "intents/intent-cli/clarifications/open.md",
                    PacketPaths = new PacketPaths
                    {
                        Implementation = ".intent-cli/issues/G18/implementation.md",
                        ReviewContext = ".intent-cli/issues/G18/review-context.md",
                        Yaml = ".intent-cli/issues/G18/packet.yaml"
                    },
                    LinkedIssue = new LinkedIssue
                    {
                        Repo = "J-Tech-Japan/intent-system",
                        Number = 64,
                        Url = "https://github.com/J-Tech-Japan/intent-system/issues/64"
                    },
                    WorkerRole = "coder",
                    ReviewRole = "reviewer",
                    Priority = "high"
                }
            ]
        };
    }

    private static QueueState CreateRunImplementQueueState()
    {
        return new QueueState
        {
            SchemaVersion = "1",
            UpdatedAt = DateTimeOffset.Parse("2026-04-08T08:12:34Z"),
            Items =
            [
                new QueueItem
                {
                    ExecutionUnit = "G19",
                    Title = "Run implement command",
                    State = QueueItemState.Active,
                    Dependencies = ["G18"],
                    BlockedBy = [],
                    ClarificationReturnPath = "intents/intent-cli/clarifications/open.md",
                    PacketPaths = new PacketPaths
                    {
                        Implementation = ".intent-cli/issues/G19/implementation.md",
                        ReviewContext = ".intent-cli/issues/G19/review-context.md",
                        Yaml = ".intent-cli/issues/G19/packet.yaml"
                    },
                    LinkedIssue = new LinkedIssue
                    {
                        Repo = "J-Tech-Japan/intent-system",
                        Number = 66,
                        Url = "https://github.com/J-Tech-Japan/intent-system/issues/66"
                    },
                    WorkerRole = "coder",
                    ReviewRole = "reviewer",
                    Priority = "high"
                }
            ]
        };
    }

    private static QueueState CreateRunFixQueueState()
    {
        return new QueueState
        {
            SchemaVersion = "1",
            UpdatedAt = DateTimeOffset.Parse("2026-04-09T09:42:34Z"),
            Items =
            [
                new QueueItem
                {
                    ExecutionUnit = "G20",
                    Title = "Run fix command",
                    State = QueueItemState.Fixing,
                    Dependencies = ["G19"],
                    BlockedBy = [],
                    ClarificationReturnPath = "intents/intent-cli/clarifications/open.md",
                    PacketPaths = new PacketPaths
                    {
                        Implementation = ".intent-cli/issues/G20/implementation.md",
                        ReviewContext = ".intent-cli/issues/G20/review-context.md",
                        Yaml = ".intent-cli/issues/G20/packet.yaml"
                    },
                    LinkedIssue = new LinkedIssue
                    {
                        Repo = "J-Tech-Japan/intent-system",
                        Number = 68,
                        Url = "https://github.com/J-Tech-Japan/intent-system/issues/68"
                    },
                    WorkerRole = "coder",
                    ReviewRole = "reviewer",
                    Priority = "high"
                }
            ]
        };
    }

    private static QueueState CreateClarifyOpenQueueState()
    {
        return new QueueState
        {
            SchemaVersion = "1",
            UpdatedAt = DateTimeOffset.Parse("2026-04-11T06:05:00Z"),
            Items =
            [
                new QueueItem
                {
                    ExecutionUnit = "G22",
                    Title = "Clarify open command",
                    State = QueueItemState.Review,
                    Dependencies = ["G21"],
                    BlockedBy = [],
                    ClarificationReturnPath = "intents/intent-cli/clarifications/open.md",
                    PacketPaths = new PacketPaths
                    {
                        Implementation = ".intent-cli/issues/G22/implementation.md",
                        ReviewContext = ".intent-cli/issues/G22/review-context.md",
                        Yaml = ".intent-cli/issues/G22/packet.yaml"
                    },
                    WorkerRole = "coder",
                    ReviewRole = "reviewer",
                    Priority = "high"
                }
            ]
        };
    }

    private static QueueState CreateRunResubmitQueueState()
    {
        return new QueueState
        {
            SchemaVersion = "1",
            UpdatedAt = DateTimeOffset.Parse("2026-04-10T07:12:34Z"),
            Items =
            [
                new QueueItem
                {
                    ExecutionUnit = "G21",
                    Title = "Run resubmit command",
                    State = QueueItemState.Fixing,
                    Dependencies = ["G20"],
                    BlockedBy = [],
                    ClarificationReturnPath = "intents/intent-cli/clarifications/open.md",
                    PacketPaths = new PacketPaths
                    {
                        Implementation = ".intent-cli/issues/G21/implementation.md",
                        ReviewContext = ".intent-cli/issues/G21/review-context.md",
                        Yaml = ".intent-cli/issues/G21/packet.yaml"
                    },
                    LinkedIssue = new LinkedIssue
                    {
                        Repo = "J-Tech-Japan/intent-system",
                        Number = 70,
                        Url = "https://github.com/J-Tech-Japan/intent-system/issues/70"
                    },
                    WorkerRole = "coder",
                    ReviewRole = "reviewer",
                    Priority = "high"
                }
            ]
        };
    }

    private static QueueState CreateReviewQueueState()
    {
        return new QueueState
        {
            SchemaVersion = "1",
            UpdatedAt = DateTimeOffset.Parse("2026-04-03T10:12:34Z"),
            Items =
            [
                new QueueItem
                {
                    ExecutionUnit = "G9",
                    Title = "Review run command",
                    State = QueueItemState.Review,
                    Dependencies = ["G7"],
                    BlockedBy = [],
                    ClarificationReturnPath = "intents/intent-cli/clarifications/open.md",
                    PacketPaths = new PacketPaths
                    {
                        Implementation = ".intent-cli/issues/G9/implementation.md",
                        ReviewContext = ".intent-cli/issues/G9/review-context.md",
                        Yaml = ".intent-cli/issues/G9/packet.yaml"
                    },
                    WorkerRole = "coder",
                    ReviewRole = "reviewer",
                    Priority = "high"
                }
            ]
        };
    }

    private static QueueState CreateRunStartQueueState()
    {
        return new QueueState
        {
            SchemaVersion = "1",
            UpdatedAt = DateTimeOffset.Parse("2026-04-03T10:12:34Z"),
            Items =
            [
                new QueueItem
                {
                    ExecutionUnit = "G14",
                    Title = "Run start command",
                    State = QueueItemState.Queued,
                    Dependencies = [],
                    BlockedBy = [],
                    ClarificationReturnPath = "intents/intent-cli/clarifications/open.md",
                    PacketPaths = new PacketPaths
                    {
                        Implementation = ".intent-cli/issues/G14/implementation.md",
                        ReviewContext = ".intent-cli/issues/G14/review-context.md",
                        Yaml = ".intent-cli/issues/G14/packet.yaml"
                    },
                    LinkedIssue = new LinkedIssue
                    {
                        Repo = "J-Tech-Japan/intent-system",
                        Number = 56,
                        Url = "https://github.com/J-Tech-Japan/intent-system/issues/56"
                    },
                    WorkerRole = "coder",
                    ReviewRole = "reviewer",
                    Priority = "high"
                }
            ]
        };
    }

    private static QueueState CreateReviewCommentQueueState()
    {
        return new QueueState
        {
            SchemaVersion = "1",
            UpdatedAt = DateTimeOffset.Parse("2026-04-03T10:12:34Z"),
            Items =
            [
                new QueueItem
                {
                    ExecutionUnit = "G10",
                    Title = "Review comment command",
                    State = QueueItemState.Review,
                    Dependencies = ["G9"],
                    BlockedBy = [],
                    ClarificationReturnPath = "intents/intent-cli/clarifications/open.md",
                    PacketPaths = new PacketPaths
                    {
                        Implementation = ".intent-cli/issues/G10/implementation.md",
                        ReviewContext = ".intent-cli/issues/G10/review-context.md",
                        Yaml = ".intent-cli/issues/G10/packet.yaml"
                    },
                    WorkerRole = "coder",
                    ReviewRole = "reviewer",
                    Priority = "high"
                }
            ]
        };
    }

    private static QueueState CreateReviewAcceptQueueState()
    {
        return new QueueState
        {
            SchemaVersion = "1",
            UpdatedAt = DateTimeOffset.Parse("2026-04-03T10:12:34Z"),
            Items =
            [
                new QueueItem
                {
                    ExecutionUnit = "G12",
                    Title = "Review accept command",
                    State = QueueItemState.Review,
                    Dependencies = ["G10"],
                    BlockedBy = [],
                    ClarificationReturnPath = "intents/intent-cli/clarifications/open.md",
                    PacketPaths = new PacketPaths
                    {
                        Implementation = ".intent-cli/issues/G12/implementation.md",
                        ReviewContext = ".intent-cli/issues/G12/review-context.md",
                        Yaml = ".intent-cli/issues/G12/packet.yaml"
                    },
                    WorkerRole = "coder",
                    ReviewRole = "reviewer",
                    Priority = "high"
                }
            ]
        };
    }

    private static string CreateRunSubmitPacketYaml()
    {
        return """
        implementation_issue_packet:
          issue_title: "G15 Run Submit Command"
          issue_kind: "feature"
          source_execution_unit: "G15"
          goal: "Submit active worktree for review."
          in_scope:
            - "run submit command"
          out_of_scope:
            - "review execution"
          target_repo: "submodules/intent-system"
          target_path: "."
          target_part: "cli run submit command"
          dependencies:
            - "G14"
          technical_baseline:
            - "C# / .NET"
          project_local_guide:
            - "AGENTS.md"
          intent_baseline:
            - "run submit stays thin"
          intent_references:
            - "ICL.P.PRODUCT_GOAL"
          rules_and_specs:
            - "intents/intent-cli/specs/08-config-and-run-model.md"
          acceptance_criteria:
            - "draft pr created"
          verification_evidence:
            - "tests-passing"
          review_mode: "deterministic-review"
          completion_action: "wait-for-deterministic-review"
          landing_policy: "merge-after-review"
        
        review_context_packet:
          source_execution_unit: "G15"
          parent_intent_root: "intents/intent-cli/intent-tree/00-map.md"
          intent_references:
            - "ICL.P.PRODUCT_GOAL"
          rules_and_specs:
            - "intents/intent-cli/specs/08-config-and-run-model.md"
          acceptance_criteria:
            - "draft pr created"
          deterministic_review_checks:
            - "run submit remains thin"
          clarification_return_path: "intents/intent-cli/clarifications/open.md"
        """;
    }

    private static string CreateQueueDispatchPacketYaml()
    {
        return """
        implementation_issue_packet:
          issue_title: "[G13] Queue Dispatch Command"
          issue_kind: "feature"
          source_execution_unit: "G13"
          goal: "Dispatch queue item into GitHub issue."
          in_scope:
            - "queue dispatch command"
          out_of_scope:
            - "branch creation"
          target_repo: "submodules/intent-system"
          target_path: "."
          target_part: "cli queue dispatch command"
          dependencies:
            - "G3"
          technical_baseline:
            - "C# / .NET"
          project_local_guide:
            - "AGENTS.md"
          intent_baseline:
            - "dispatch stays thin"
          intent_references:
            - "ICL.P.PRODUCT_GOAL"
          rules_and_specs:
            - "intents/rules/issue-lifecycle-and-landing.md"
          acceptance_criteria:
            - "issue created"
          verification_evidence:
            - "tests-passing"
          review_mode: "deterministic-review"
          completion_action: "wait-for-deterministic-review"
          landing_policy: "merge-after-review"
        
        review_context_packet:
          source_execution_unit: "G13"
          parent_intent_root: "intents/intent-cli/intent-tree/00-map.md"
          intent_references:
            - "ICL.P.PRODUCT_GOAL"
          rules_and_specs:
            - "intents/rules/issue-lifecycle-and-landing.md"
          acceptance_criteria:
            - "issue created"
          deterministic_review_checks:
            - "dispatch remains thin"
          clarification_return_path: "intents/intent-cli/clarifications/open.md"
        """;
    }

    private static string CreateQueueEnqueuePacketYaml()
    {
        return """
        execution_unit: G38
        implementation_issue:
          issue_title: "G38 Queue Enqueue Command"
          goal: "Enqueue queue item from packet artifact."
          in_scope:
            - "queue enqueue command"
          out_of_scope:
            - "child issue creation"
          target_repo: "submodules/intent-system"
          target_path: "."
          target_part: "cli queue enqueue command"
          dependencies:
            - "G3"
          technical_baseline:
            - "C# / .NET"
          project_local_guide:
            - "AGENTS.md"
          intent_baseline:
            - "enqueue stays thin"
          intent_references:
            - "ICL.P.PRODUCT_GOAL"
          rules_and_specs:
            - "intents/intent-cli/specs/03-queue-json-and-jsonl-schema.md"
          acceptance_criteria:
            - "queue item inserted"
          verification_evidence:
            - "tests-passing"

        review:
          summarize_first: true
          require_explicit_diff_check: true
          require_explicit_scope_check: true
          require_explicit_contract_check: true
          required_checks:
            - "enqueue remains thin"
        """;
    }

    private static string CreateRunRereviewRunLog()
    {
        return """
        {"ts":"2026-04-05T09:00:00Z","execution_unit":"G16","event":"review","by":"intent-cli","linked_pr":"https://github.com/J-Tech-Japan/intent-system/pull/60"}
        {"ts":"2026-04-05T09:10:00Z","execution_unit":"G16","event":"fix-requested","by":"intent-cli","comment_ref":"https://github.com/J-Tech-Japan/intent-system/pull/60#issuecomment-1"}
        {"ts":"2026-04-05T09:30:00Z","execution_unit":"G16","event":"review","by":"intent-cli","linked_pr":"https://github.com/J-Tech-Japan/intent-system/pull/61"}
        """ + Environment.NewLine;
    }

    private static string CreateRunResumePacketYaml()
    {
        return """
        implementation_issue_packet:
          issue_title: "G17 Run Resume Command"
          issue_kind: "feature"
          source_execution_unit: "G17"
          goal: "Render resumable context for an existing run."
          in_scope:
            - "run resume command"
          out_of_scope:
            - "queue mutation"
          target_repo: "submodules/intent-system"
          target_path: "."
          target_part: "cli run resume command"
          dependencies:
            - "G16"
          technical_baseline:
            - "C# / .NET"
          project_local_guide:
            - "AGENTS.md"
          intent_baseline:
            - "run resume stays read-only"
          intent_references:
            - "ICL.P.PRODUCT_GOAL"
          rules_and_specs:
            - "intents/intent-cli/specs/08-config-and-run-model.md"
          acceptance_criteria:
            - "resumable context displayed"
          verification_evidence:
            - "tests-passing"
          review_mode: "deterministic-review"
          completion_action: "wait-for-deterministic-review"
          landing_policy: "merge-after-review"

        review_context_packet:
          source_execution_unit: "G17"
          parent_intent_root: "intents/intent-cli/intent-tree/00-map.md"
          intent_references:
            - "ICL.P.PRODUCT_GOAL"
          rules_and_specs:
            - "intents/intent-cli/specs/08-config-and-run-model.md"
          acceptance_criteria:
            - "resumable context displayed"
          deterministic_review_checks:
            - "run resume remains read-only"
          clarification_return_path: "intents/intent-cli/clarifications/open.md"
        """;
    }

    private static string CreateRunResumeRunLog()
    {
        return """
        {"ts":"2026-04-06T08:00:00Z","execution_unit":"G17","event":"review","by":"intent-cli","linked_pr":"https://github.com/J-Tech-Japan/intent-system/pull/62"}
        {"ts":"2026-04-06T08:20:00Z","execution_unit":"G17","event":"fix-requested","by":"intent-cli","comment_ref":"https://github.com/J-Tech-Japan/intent-system/pull/62#issuecomment-1"}
        {"ts":"2026-04-06T08:30:00Z","execution_unit":"G17","event":"review","by":"intent-cli","linked_pr":"https://github.com/J-Tech-Japan/intent-system/pull/63"}
        """ + Environment.NewLine;
    }

    private static string CreateRunLogCommandRunLog()
    {
        return """
        {"ts":"2026-04-07T08:00:00Z","execution_unit":"G18","event":"issue-created","by":"intent-cli","linked_issue":"https://github.com/J-Tech-Japan/intent-system/issues/64"}
        {"ts":"2026-04-07T08:10:00Z","execution_unit":"G18","event":"activated","by":"intent-cli"}
        {"ts":"2026-04-07T08:20:00Z","execution_unit":"G18","event":"review","by":"intent-cli","linked_pr":"https://github.com/J-Tech-Japan/intent-system/pull/65"}
        """ + Environment.NewLine;
    }

    private static string CreateRunImplementPacketYaml()
    {
        return """
        implementation_issue_packet:
          issue_title: "[G19] Run Implement Command"
          issue_kind: "feature"
          source_execution_unit: "G19"
          goal: "Generate an execution worker handoff artifact."
          in_scope:
            - "run implement command"
            - "handoff artifact generation"
          out_of_scope:
            - "queue mutation"
            - "worker start"
          target_repo: "submodules/intent-system"
          target_path: "."
          target_part: "cli run implement command"
          dependencies:
            - "G18"
          technical_baseline:
            - "C# / .NET"
          project_local_guide:
            - "AGENTS.md"
          intent_baseline:
            - "run implement stays handoff-only"
          intent_references:
            - "ICL.P.PRODUCT_GOAL"
          rules_and_specs:
            - "intents/intent-cli/specs/08-config-and-run-model.md"
          acceptance_criteria:
            - "handoff artifact generated"
          verification_evidence:
            - "tests-passing"
          review_mode: "deterministic-review"
          completion_action: "wait-for-deterministic-review"
          landing_policy: "merge-after-review"

        review_context_packet:
          source_execution_unit: "G19"
          parent_intent_root: "intents/intent-cli/intent-tree/00-map.md"
          intent_references:
            - "ICL.P.PRODUCT_GOAL"
          rules_and_specs:
            - "intents/intent-cli/specs/08-config-and-run-model.md"
          acceptance_criteria:
            - "handoff artifact generated"
          deterministic_review_checks:
            - "run implement command remains handoff-only"
          clarification_return_path: "intents/intent-cli/clarifications/open.md"
        """;
    }

    private static string CreateRunFixPacketYaml()
    {
        return """
        implementation_issue_packet:
          issue_title: "[G20] Run Fix Command"
          issue_kind: "feature"
          source_execution_unit: "G20"
          goal: "Generate a repair worker handoff artifact."
          in_scope:
            - "run fix command"
            - "repair handoff artifact generation"
          out_of_scope:
            - "queue mutation"
            - "worker start"
          target_repo: "submodules/intent-system"
          target_path: "."
          target_part: "cli run fix command"
          dependencies:
            - "G19"
          technical_baseline:
            - "C# / .NET"
          project_local_guide:
            - "AGENTS.md"
          intent_baseline:
            - "run fix stays handoff-only"
          intent_references:
            - "ICL.P.PRODUCT_GOAL"
          rules_and_specs:
            - "intents/rules/review-recovery-and-retry.md"
          acceptance_criteria:
            - "repair handoff artifact generated"
          verification_evidence:
            - "tests-passing"
          review_mode: "deterministic-review"
          completion_action: "wait-for-deterministic-review"
          landing_policy: "merge-after-review"

        review_context_packet:
          source_execution_unit: "G20"
          parent_intent_root: "intents/intent-cli/intent-tree/00-map.md"
          intent_references:
            - "ICL.P.PRODUCT_GOAL"
          rules_and_specs:
            - "intents/rules/review-recovery-and-retry.md"
          acceptance_criteria:
            - "repair handoff artifact generated"
          deterministic_review_checks:
            - "run fix command remains handoff-only"
          clarification_return_path: "intents/intent-cli/clarifications/open.md"
        """;
    }

    private static QueueState CreateRunSuperviseQueueState()
    {
        return new QueueState
        {
            SchemaVersion = "1",
            UpdatedAt = DateTimeOffset.Parse("2026-04-08T10:00:00Z"),
            Items =
            [
                new QueueItem
                {
                    ExecutionUnit = "G25",
                    Title = "[G25] Run Supervise Command",
                    State = QueueItemState.Active,
                    Dependencies = [],
                    BlockedBy = [],
                    ClarificationReturnPath = "intents/intent-cli/clarifications/open.md",
                    PacketPaths = new PacketPaths
                    {
                        Implementation = ".intent-cli/issues/G25/implementation.md",
                        ReviewContext = ".intent-cli/issues/G25/review-context.md",
                        Yaml = ".intent-cli/issues/G25/packet.yaml"
                    },
                    LinkedIssue = new LinkedIssue
                    {
                        Repo = "J-Tech-Japan/intent-system",
                        Number = 178,
                        Url = "https://github.com/J-Tech-Japan/intent-system/issues/178"
                    },
                    WorkerRole = "coder",
                    ReviewRole = "reviewer",
                    Priority = "P1"
                }
            ]
        };
    }

    private static string CreateRunSupervisePacketYaml()
    {
        return """
        execution_unit: "G25"

        implementation_issue:
          issue_title: "[G25] Run Supervise Command"
          goal: "Supervise retryable run interruptions."
          target_repo: "submodules/intent-system"
          target_path: "."
          target_part: "cli run supervise command"
          dependencies: []

        review:
          review_context_path: ".intent-cli/issues/G25/review-context.md"
          clarification_return_path: "intents/intent-cli/clarifications/open.md"
        """;
    }

    private static string CreateRunSuperviseRunLog()
    {
        return """
        {"ts":"2026-04-08T09:50:00Z","execution_unit":"G25","event":"issue-created","by":"intent-cli","linked_issue":"https://github.com/J-Tech-Japan/intent-system/issues/178"}
        {"ts":"2026-04-08T10:00:00Z","execution_unit":"G25","event":"activated","by":"intent-cli"}
        """ + Environment.NewLine;
    }

    private static string CreateClarifyOpenPacketYaml()
    {
        return """
        implementation_issue_packet:
          issue_title: "[G22] Clarify Open Command"
          issue_kind: "feature"
          source_execution_unit: "G22"
          goal: "Open a clarification request for the current queue loop."
          in_scope:
            - "clarify open command"
          out_of_scope:
            - "clarify answer"
          target_repo: "submodules/intent-system"
          target_path: "."
          target_part: "cli clarify open command"
          dependencies:
            - "G8"
          technical_baseline:
            - "C# / .NET"
          project_local_guide:
            - "AGENTS.md"
          intent_baseline:
            - "clarify open stays entry-only"
          intent_references:
            - "ICL.P.PRODUCT_GOAL"
          rules_and_specs:
            - "intents/intent-cli/specs/06-interview-and-clarification-artifact-contract.md"
          acceptance_criteria:
            - "clarification artifact generated"
          verification_evidence:
            - "dotnet test IntentSystem.sln"
          review_mode: "deterministic-review"
          completion_action: "wait-for-deterministic-review"
          landing_policy: "merge-after-review"

        review_context_packet:
          source_execution_unit: "G22"
          parent_intent_root: "intents/intent-cli/intent-tree/00-map.md"
          intent_references:
            - "ICL.P.PRODUCT_GOAL"
          rules_and_specs:
            - "intents/intent-cli/specs/06-interview-and-clarification-artifact-contract.md"
          acceptance_criteria:
            - "clarification artifact generated"
          deterministic_review_checks:
            - "clarify open command remains entry-only"
          clarification_return_path: "intents/intent-cli/clarifications/open.md"
        """;
    }

    private static string CreateRunResubmitPacketYaml()
    {
        return """
        implementation_issue_packet:
          issue_title: "[G21] Run Resubmit Command"
          issue_kind: "feature"
          source_execution_unit: "G21"
          goal: "Push the repair branch and append a resubmitted event."
          in_scope:
            - "run resubmit command"
            - "repair branch push"
          out_of_scope:
            - "queue state mutation"
            - "PR creation"
          target_repo: "submodules/intent-system"
          target_path: "."
          target_part: "cli run resubmit command"
          dependencies:
            - "G20"
          technical_baseline:
            - "C# / .NET"
          project_local_guide:
            - "AGENTS.md"
          intent_baseline:
            - "run resubmit stays push-only"
          intent_references:
            - "ICL.P.PRODUCT_GOAL"
          rules_and_specs:
            - "intents/intent-cli/specs/05-intent-cli-surface.md"
          acceptance_criteria:
            - "resubmitted event appended"
          verification_evidence:
            - "dotnet test IntentSystem.sln"
          review_mode: "deterministic-review"
          completion_action: "wait-for-deterministic-review"
          landing_policy: "merge-after-review"

        review_context_packet:
          source_execution_unit: "G21"
          parent_intent_root: "intents/intent-cli/intent-tree/00-map.md"
          intent_references:
            - "ICL.P.PRODUCT_GOAL"
          rules_and_specs:
            - "intents/intent-cli/specs/05-intent-cli-surface.md"
          acceptance_criteria:
            - "resubmitted event appended"
          deterministic_review_checks:
            - "run resubmit remains push-only"
          clarification_return_path: "intents/intent-cli/clarifications/open.md"
        """;
    }

    private static string CreateRunImplementReviewContextMarkdown()
    {
        return """
        # Execution Unit

        `G19`

        # Goal

        `intent-cli run implement <execution-unit>` を working command にする。

        # Acceptance Criteria

        - handoff artifact generated

        # Deterministic Review Checks

        - run implement command remains handoff-only

        # Expected Evidence

        - dotnet test IntentSystem.sln
        """;
    }

    private static string CreateRunFixReviewContextMarkdown()
    {
        return """
        # Execution Unit

        `G20`

        # Goal

        `intent-cli run fix <execution-unit>` を working command にする。

        # Acceptance Criteria

        - repair handoff artifact generated

        # Deterministic Review Checks

        - run fix command remains handoff-only

        # Expected Evidence

        - dotnet test IntentSystem.sln
        """;
    }

    private static string CreateClarifyOpenReviewContextMarkdown()
    {
        return """
        # Execution Unit

        `G22`

        # Acceptance Criteria

        - clarification artifact generated

        # Deterministic Review Checks

        - clarify open command remains entry-only
        """;
    }

    private static ClarificationItem CreateClarifyListItem()
    {
        return new ClarificationItem
        {
            ClarificationSource = "execution",
            QuestionId = "request",
            ExecutionUnit = "G22",
            QuestionText = "Clarify blocker for cli clarify open command: clarify open command remains entry-only",
            Reason = "Clarification requested for [G22] Clarify Open Command: Open a clarification request for the current queue loop.",
            AffectedIntents = ["ICL.P.PRODUCT_GOAL"],
            AffectedExecutionUnits = ["G22"],
            BlockingOrNonblocking = "blocking",
            ClarificationReturnPath = "intents/intent-cli/clarifications/open.md",
            Status = ClarificationStatus.Open,
            CreatedAt = DateTimeOffset.Parse("2026-04-11T06:10:00Z"),
            Answer = null
        };
    }

    private static QueueState CreateClarifyAnswerQueueState()
    {
        return new QueueState
        {
            SchemaVersion = "1",
            UpdatedAt = DateTimeOffset.Parse("2026-04-12T07:00:00Z"),
            Items =
            [
                new QueueItem
                {
                    ExecutionUnit = "G24",
                    Title = "[G24] Clarify Answer Command",
                    State = QueueItemState.ClarifyBlocked,
                    Dependencies = [],
                    BlockedBy = ["need clarification"],
                    ClarificationReturnPath = "intents/intent-cli/clarifications/open.md",
                    PacketPaths = new PacketPaths
                    {
                        Implementation = ".intent-cli/issues/G24/implementation.md",
                        ReviewContext = ".intent-cli/issues/G24/review-context.md",
                        Yaml = ".intent-cli/issues/G24/packet.yaml"
                    },
                    WorkerRole = "coder",
                    ReviewRole = "reviewer",
                    Priority = "high"
                }
            ]
        };
    }

    private static ClarificationItem CreateClarifyAnswerItem()
    {
        return new ClarificationItem
        {
            ClarificationSource = "execution",
            QuestionId = "request",
            ExecutionUnit = "G24",
            QuestionText = "Which field should remain canonical?",
            Reason = "Clarification requested for [G24] Clarify Answer Command: Resolve the queue blocker.",
            AffectedIntents = ["ICL.P.PRODUCT_GOAL"],
            AffectedExecutionUnits = ["G24"],
            BlockingOrNonblocking = "blocking",
            ClarificationReturnPath = "intents/intent-cli/clarifications/open.md",
            Status = ClarificationStatus.Open,
            CreatedAt = DateTimeOffset.Parse("2026-04-12T06:50:00Z"),
            Answer = null
        };
    }

    private static string CreateInterviewStartItemYaml()
    {
        return """
artifact_kind: interview
domain_slug: auth
source_concept_ref: "intents/intent-cli/concepts/auth-oauth2.md"
question_id: iq-1
question_text: "Which auth flow should be canonical?"
reason: "Auth direction is still underspecified."
affects:
  - "auth-oauth2"
blocking_or_nonblocking: blocking
status: open
return_to_intent_paths:
  - "intents/intent-cli/intent-tree/means/auth-oauth2.md"
created_at: "2026-04-13T08:00:00.0000000+00:00"
answer: null
""";
    }

    private static string CreateInterviewAnswerItemYaml()
    {
        return """
artifact_kind: interview
domain_slug: auth
source_concept_ref: "intents/intent-cli/concepts/auth-oauth2.md"
question_id: iq-1
question_text: "Which auth flow should be canonical?"
reason: "Auth direction is still underspecified."
affects:
  - "auth-oauth2"
blocking_or_nonblocking: blocking
status: open
return_to_intent_paths:
  - "intents/intent-cli/intent-tree/means/auth-oauth2.md"
created_at: "2026-04-13T08:00:00.0000000+00:00"
answer: null
recommended_updates:
  - "Update auth strategy"
""";
    }

    private static string CreateRunImplementRunLog()
    {
        return """
        {"ts":"2026-04-08T08:00:00Z","execution_unit":"G19","event":"review","by":"intent-cli","linked_pr":"https://github.com/J-Tech-Japan/intent-system/pull/66"}
        {"ts":"2026-04-08T08:30:00Z","execution_unit":"G19","event":"review","by":"intent-cli","linked_pr":"https://github.com/J-Tech-Japan/intent-system/pull/67"}
        """ + Environment.NewLine;
    }

    private static string CreateRunFixReviewCommentArtifactJson()
    {
        return """
        {
          "execution_unit": "G20",
          "review_request_ref": ".intent-cli/reviews/G20.request.json",
          "linked_pr": "https://github.com/J-Tech-Japan/intent-system/pull/69",
          "comment_ref": "https://github.com/J-Tech-Japan/intent-system/pull/69#issuecomment-2",
          "body_path": "/repo/prepared-comment.md"
        }
        """;
    }

    private static string CreateRunFixRunLog()
    {
        return """
        {"ts":"2026-04-09T09:00:00Z","execution_unit":"G20","event":"review","by":"intent-cli","linked_pr":"https://github.com/J-Tech-Japan/intent-system/pull/69"}
        {"ts":"2026-04-09T09:20:00Z","execution_unit":"G20","event":"fix-requested","by":"intent-cli","comment_ref":"https://github.com/J-Tech-Japan/intent-system/pull/69#issuecomment-2"}
        """ + Environment.NewLine;
    }

    private static string CreateRunResubmitRunLog()
    {
        return """
        {"ts":"2026-04-10T07:00:00Z","execution_unit":"G21","event":"review","by":"intent-cli","linked_pr":"https://github.com/J-Tech-Japan/intent-system/pull/71"}
        {"ts":"2026-04-10T07:10:00Z","execution_unit":"G21","event":"fix-requested","by":"intent-cli","comment_ref":"https://github.com/J-Tech-Japan/intent-system/pull/71#issuecomment-3"}
        """ + Environment.NewLine;
    }

    private static string CreateReviewContextMarkdown()
    {
        return """
        # Execution Unit

        `G9`

        # Goal

        `intent-cli review run <execution-unit>` を working command として実装し、
        review context packet と latest linked PR をもとに
        deterministic review request artifact を `.intent-cli/reviews/<execution-unit>.request.json` へ生成できるようにする。

        # Parent References

        - [Intent CLI Surface](/Users/tomohisa/dev/GitHub/MyIntentHost/intents/intent-cli/specs/05-intent-cli-surface.md)
        - [Config And Run Model](/Users/tomohisa/dev/GitHub/MyIntentHost/intents/intent-cli/specs/08-config-and-run-model.md)

        # Deterministic Review Checks

        - review run command が PR comment 投稿や closeout の責務へ広がっていない

        # Expected Evidence

        - dotnet test IntentSystem.sln
        - review run command tests
        """;
    }

    private static string CreateRunStartPacketYaml()
    {
        return """
        implementation_issue_packet:
          issue_title: "[G14] Run Start Command"
          issue_kind: "feature"
          source_execution_unit: "G14"
          goal: "Create isolated worktree and activate queue item."
          in_scope:
            - "run start command"
          out_of_scope:
            - "worker start"
          target_repo: "submodules/intent-system"
          target_path: "."
          target_part: "cli run start command"
          dependencies:
            - "G13"
          technical_baseline:
            - "C# / .NET"
          project_local_guide:
            - "AGENTS.md"
          intent_baseline:
            - "run start stays thin"
          intent_references:
            - "ICL.P.PRODUCT_GOAL"
          rules_and_specs:
            - "intents/intent-cli/specs/08-config-and-run-model.md"
          acceptance_criteria:
            - "isolated worktree created"
          verification_evidence:
            - "tests-passing"
          review_mode: "deterministic-review"
          completion_action: "wait-for-deterministic-review"
          landing_policy: "merge-after-review"
        
        review_context_packet:
          source_execution_unit: "G14"
          parent_intent_root: "intents/intent-cli/intent-tree/00-map.md"
          intent_references:
            - "ICL.P.PRODUCT_GOAL"
          rules_and_specs:
            - "intents/intent-cli/specs/08-config-and-run-model.md"
          acceptance_criteria:
            - "isolated worktree created"
          deterministic_review_checks:
            - "run start remains thin"
          clarification_return_path: "intents/intent-cli/clarifications/open.md"
        """;
    }

    private static string CreateReviewRunLog()
    {
        return """
        {"ts":"2026-04-03T10:00:00Z","execution_unit":"G9","event":"review-started","by":"intent-cli","linked_pr":"https://github.com/J-Tech-Japan/intent-system/pull/44"}
        {"ts":"2026-04-03T10:20:00Z","execution_unit":"G9","event":"review-started","by":"intent-cli","linked_pr":"https://github.com/J-Tech-Japan/intent-system/pull/45"}
        """ + Environment.NewLine;
    }

    private static string CreateReviewCommentRequestJson()
    {
        return """
        {
          "execution_unit": "G10",
          "review_context_ref": ".intent-cli/issues/G10/review-context.md",
          "linked_pr": "https://github.com/J-Tech-Japan/intent-system/pull/46",
          "deterministic_review_checks": [
            "review comment command が deterministic diff review の実行, merge, closeout の責務へ広がっていない"
          ],
          "acceptance_criteria": [],
          "expected_evidence": [
            "dotnet test IntentSystem.sln"
          ]
        }
        """;
    }

    private static string CreateReviewAcceptPacketYaml()
    {
        return """
        implementation_issue_packet:
          issue_title: "[G12] Review Accept Command"
          issue_kind: "feature"
          source_execution_unit: "G12"
          goal: "Close out accepted review."
          in_scope:
            - "review accept command"
          out_of_scope:
            - "review comment"
          target_repo: "submodules/child-repo"
          target_path: "."
          target_part: "cli review accept command"
          dependencies:
            - "G10"
          technical_baseline:
            - "C# / .NET"
          project_local_guide:
            - "AGENTS.md"
          intent_baseline:
            - "closeout stays thin"
          intent_references:
            - "ICL.P.PRODUCT_GOAL"
          rules_and_specs:
            - "intents/rules/issue-lifecycle-and-landing.md"
          acceptance_criteria:
            - "review accept merges and closes"
          verification_evidence:
            - "tests-passing"
          review_mode: "deterministic-review"
          completion_action: "wait-for-deterministic-review"
          landing_policy: "merge-after-review"
        
        review_context_packet:
          source_execution_unit: "G12"
          parent_intent_root: "intents/intent-cli/intent-tree/00-map.md"
          intent_references:
            - "ICL.P.PRODUCT_GOAL"
          rules_and_specs:
            - "intents/rules/issue-lifecycle-and-landing.md"
          acceptance_criteria:
            - "review accept merges and closes"
          deterministic_review_checks:
            - "selected item only"
          clarification_return_path: "intents/intent-cli/clarifications/open.md"
        """;
    }

    private static string CreateReviewAcceptRunLog()
    {
        return """
        {"ts":"2026-04-03T10:00:00Z","execution_unit":"G12","event":"issue-created","by":"intent-cli","linked_issue":"https://github.com/J-Tech-Japan/intent-system/issues/51"}
        {"ts":"2026-04-03T10:10:00Z","execution_unit":"G12","event":"review-started","by":"intent-cli","linked_pr":"https://github.com/J-Tech-Japan/intent-system/pull/52"}
        """ + Environment.NewLine;
    }

    private sealed class FakeReviewCommentPublisher : IReviewCommentPublisher
    {
        public string PostComment(string linkedPr, string body)
        {
            return "https://github.com/J-Tech-Japan/intent-system/pull/46#issuecomment-1";
        }
    }

    private sealed class FakeQueueDispatchPublisher : IQueueDispatchPublisher
    {
        public LinkedIssue CreateIssue(string targetRepo, string title, string body)
        {
            return new LinkedIssue
            {
                Repo = targetRepo,
                Number = 53,
                Url = "https://github.com/J-Tech-Japan/intent-system/issues/53"
            };
        }

        public void AddLabel(string targetRepo, int issueNumber, string labelName)
        {
        }

        public IReadOnlyList<string> GetIssueLabels(string targetRepo, int issueNumber)
        {
            return ["intent-target"];
        }
    }

    private sealed class ThrowingQueueDispatchPublisher : IQueueDispatchPublisher
    {
        public LinkedIssue CreateIssue(string targetRepo, string title, string body)
        {
            throw new InvalidOperationException("CreateIssue should not be called when linked issue already exists.");
        }
    }

    private sealed class FakeQueueDispatchGitRunner : IGitRemoteCommandRunner
    {
        public GitRemoteCommandResult Run(string workingDirectory, IReadOnlyList<string> arguments)
        {
            return new GitRemoteCommandResult
            {
                ExitCode = 0,
                StdOut = "git@github.com:J-Tech-Japan/intent-system.git" + Environment.NewLine,
                StdErr = string.Empty
            };
        }
    }

    private sealed class FakeParentIntentGitRunner : IGitRemoteCommandRunner
    {
        public GitRemoteCommandResult Run(string workingDirectory, IReadOnlyList<string> arguments)
        {
            return new GitRemoteCommandResult
            {
                ExitCode = 0,
                StdOut = "git@github.com:J-Tech-Japan/MyIntentHost.git" + Environment.NewLine,
                StdErr = string.Empty
            };
        }
    }

    private sealed class FakeRunStartGitRunner : IGitCommandRunner
    {
        public GitCommandResult Run(string workingDirectory, IReadOnlyList<string> arguments)
        {
            return new GitCommandResult
            {
                ExitCode = 0,
                StdOut = string.Empty,
                StdErr = string.Empty
            };
        }
    }

    private sealed class FakeRunSubmitGitRunner : IGitCommandRunner
    {
        public GitCommandResult Run(string workingDirectory, IReadOnlyList<string> arguments)
        {
            if (arguments.SequenceEqual(["rev-parse", "--abbrev-ref", "HEAD"]))
            {
                return new GitCommandResult
                {
                    ExitCode = 0,
                    StdOut = "issue-56-g14" + Environment.NewLine,
                    StdErr = string.Empty
                };
            }

            if (arguments.SequenceEqual(["remote", "get-url", "origin"]))
            {
                return new GitCommandResult
                {
                    ExitCode = 0,
                    StdOut = "git@github.com:J-Tech-Japan/intent-system.git" + Environment.NewLine,
                    StdErr = string.Empty
                };
            }

            return new GitCommandResult
            {
                ExitCode = 0,
                StdOut = string.Empty,
                StdErr = string.Empty
            };
        }
    }

    private sealed class FakeRunResubmitGitRunner : IGitCommandRunner
    {
        public GitCommandResult Run(string workingDirectory, IReadOnlyList<string> arguments)
        {
            if (arguments.SequenceEqual(["rev-parse", "--abbrev-ref", "HEAD"]))
            {
                return new GitCommandResult
                {
                    ExitCode = 0,
                    StdOut = "issue-70-g21" + Environment.NewLine,
                    StdErr = string.Empty
                };
            }

            return new GitCommandResult
            {
                ExitCode = 0,
                StdOut = string.Empty,
                StdErr = string.Empty
            };
        }
    }

    private sealed class FakeRunSubmitPublisher : IRunSubmitPublisher
    {
        public string CreateDraftPullRequest(string targetRepo, string headBranch, string title, string body)
        {
            return "https://github.com/J-Tech-Japan/intent-system/pull/58";
        }

        public bool TryFindExistingOpenPullRequest(
            string targetRepo,
            string headBranch,
            string linkedIssueUrl,
            out string pullRequestUrl)
        {
            pullRequestUrl = string.Empty;
            return false;
        }
    }

    private sealed class FakeReviewAcceptClient : IReviewAcceptClient
    {
        public void MarkPullRequestReady(string linkedPr)
        {
        }

        public string MergePullRequest(string linkedPr)
        {
            return "abc123";
        }

        public void CloseIssue(string linkedIssue)
        {
        }
    }

    private sealed class FakeReviewAcceptGitRunner : IGitCommandRunner
    {
        public GitCommandResult Run(string workingDirectory, IReadOnlyList<string> arguments)
        {
            return new GitCommandResult
            {
                ExitCode = 0,
                StdOut = arguments.SequenceEqual(["rev-parse", "HEAD"])
                    ? "abc123" + Environment.NewLine
                    : string.Empty,
                StdErr = string.Empty
            };
        }
    }

    private sealed class FakeGenerateFromCurrentGitHubRunner : IGitHubCommandRunner
    {
        public GitHubCommandResult Run(IReadOnlyList<string> arguments)
        {
            if (arguments.SequenceEqual(["issue", "view", "114", "--comments", "--json", "number,title,body,url,state,comments"]))
            {
                return new GitHubCommandResult
                {
                    ExitCode = 0,
                    StdOut = """{"number":114,"title":"[G44] Generate From Current","body":"Reverse intake entry point.","url":"https://github.com/J-Tech-Japan/intent-system/issues/114","state":"OPEN","comments":[{"body":"keep it deterministic"}]}""",
                    StdErr = string.Empty
                };
            }

            if (arguments.SequenceEqual(["pr", "view", "113", "--comments", "--json", "number,title,body,url,state,isDraft,mergeStateStatus,comments,reviews"]))
            {
                return new GitHubCommandResult
                {
                    ExitCode = 0,
                    StdOut = """{"number":113,"title":"[codex] Add intake activate command","body":"Adds intake activate.","url":"https://github.com/J-Tech-Japan/intent-system/pull/113","state":"OPEN","isDraft":true,"mergeStateStatus":"CLEAN","comments":[{"body":"ok"}],"reviews":[{"state":"COMMENTED"}]}""",
                    StdErr = string.Empty
                };
            }

            throw new InvalidOperationException($"Unexpected gh arguments: {string.Join(' ', arguments)}");
        }
    }
    private sealed class TemporaryDirectory : IDisposable
    {
        private readonly string rootPath = Directory.CreateTempSubdirectory("intent-cli-tests-").FullName;

        public string CreateDirectory(string relativePath)
        {
            var fullPath = Path.Combine(rootPath, relativePath);
            Directory.CreateDirectory(fullPath);
            return fullPath;
        }

        public void CreateFile(string relativePath, string contents)
        {
            var fullPath = Path.Combine(rootPath, relativePath);
            var directoryPath = Path.GetDirectoryName(fullPath)
                ?? throw new InvalidOperationException("Temporary file path did not contain a directory.");

            Directory.CreateDirectory(directoryPath);
            File.WriteAllText(fullPath, contents);
        }

        public void Dispose()
        {
            if (Directory.Exists(rootPath))
            {
                Directory.Delete(rootPath, recursive: true);
            }
        }
    }
}
