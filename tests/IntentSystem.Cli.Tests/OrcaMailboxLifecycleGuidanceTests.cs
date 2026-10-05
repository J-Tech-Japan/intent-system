using System.Text.Json;
using System.Text;
using IntentSystem.Cli.Commands;

namespace IntentSystem.Cli.Tests;

[Collection("WorkerNextActionSharedState")]
public sealed class OrcaMailboxLifecycleGuidanceTests
{
    private static readonly (string Frontend, string Policy)[] Frontends =
    [
        ("orca", OrcaRunBinding.OrcaPushPolicy),
        ("codex-app", OrcaRunBinding.InboxPullPolicy),
        ("claude-app", OrcaRunBinding.InboxPullPolicy),
    ];

    [Fact]
    public void Template_IsVersionedStructuredAndMarkdownRendersTheSameCommands()
    {
        var contract = OrcaMailboxLifecycleGuidance.CreateTemplate();
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(contract));
        using var markdown = new StringWriter();
        OrcaMailboxLifecycleGuidance.WriteMarkdown(markdown, contract);
        var text = markdown.ToString();

        Assert.Equal("orca-mailbox-lifecycle/v1", json.RootElement.GetProperty("contract_version").GetString());
        Assert.Equal("agent", json.RootElement.GetProperty("execution_owner").GetString());
        Assert.False(json.RootElement.GetProperty("intent_cli_executes_orca").GetBoolean());
        Assert.Equal(["solo-conductor", "four-seat", "five-seat"], contract.BindingSelection.Branches.Select(branch => branch.TeamShape));
        Assert.Equal(["orca", "codex-app", "claude-app"], contract.CallerIdentity.Frontends);
        Assert.Contains("orca-push", string.Join("\n", contract.BindingSelection.FrontendPolicies), StringComparison.Ordinal);
        Assert.Contains("inbox-pull", string.Join("\n", contract.BindingSelection.FrontendPolicies), StringComparison.Ordinal);

        var serializedCommands = FindSerializedCommands(json.RootElement).ToArray();
        foreach (var command in AllCommands(contract))
        {
            Assert.False(string.IsNullOrWhiteSpace(command.Executable));
            Assert.DoesNotContain("[", command.Render(), StringComparison.Ordinal);
            Assert.DoesNotContain("]", command.Render(), StringComparison.Ordinal);
            Assert.Contains(command.Render(), text, StringComparison.Ordinal);
            var parsed = ParseRenderedCommand(command.Render());
            Assert.Equal(command.Executable, parsed[0]);
            Assert.Equal(command.Arguments, parsed.Skip(1));
            Assert.Contains(serializedCommands, item =>
                item.GetProperty("executable").GetString() == command.Executable
                && item.GetProperty("caller").GetString() == command.Caller
                && item.GetProperty("arguments").EnumerateArray().Select(argument => argument.GetString()).SequenceEqual(command.Arguments));
        }

        Assert.Contains(contract.CompletionBoundary.NoExecutionBoundary, text, StringComparison.Ordinal);
        Assert.Contains(contract.Recovery.UnknownBranch, text, StringComparison.Ordinal);
        Assert.Contains($"- ACK from authenticated session: `{contract.Receive.SessionAckCommand.Render()}` — {contract.Receive.AckRule}", text, StringComparison.Ordinal);
        Assert.Contains($"- ACK from verified terminal: `{contract.Receive.TerminalAckCommand.Render()}` — {contract.Receive.AckRule}", text, StringComparison.Ordinal);
        Assert.Contains("#1771 disposition and completion boundary", text, StringComparison.Ordinal);
    }

    [Fact]
    public void CommandAlternativesHaveExactSeparateSessionAndTerminalArgumentLists()
    {
        var contract = OrcaMailboxLifecycleGuidance.CreateTemplate();
        var createCommands = contract.CreateOrAdopt.Commands.Where(command => command.Arguments.Contains("run-create")).ToArray();
        Assert.Equal(2, createCommands.Length);
        Assert.DoesNotContain("--from", createCommands.Single(command => command.Caller == "authenticated session caller").Arguments);
        Assert.Contains("--from", createCommands.Single(command => command.Caller == "verified terminal caller").Arguments);

        Assert.Equal(
            ["orchestration", "send", "--to", "run:<recipient-run-id>", "--type", "status", "--subject", "<subject>", "--body", "<body>", "--json"],
            contract.Delivery.SessionSendCommand.Arguments);
        Assert.Equal(
            ["orchestration", "send", "--to", "run:<recipient-run-id>", "--from", "<own-terminal-handle>", "--type", "status", "--subject", "<subject>", "--body", "<body>", "--json"],
            contract.Delivery.TerminalSendCommand.Arguments);
        Assert.DoesNotContain("--from", contract.Delivery.SessionSendCommand.Arguments);

        Assert.DoesNotContain("--terminal", contract.Receive.SessionCheckCommand.Arguments);
        Assert.Contains("--terminal", contract.Receive.TerminalCheckCommand.Arguments);
        Assert.DoesNotContain("--terminal", contract.Receive.SessionAckCommand.Arguments);
        Assert.Contains("--terminal", contract.Receive.TerminalAckCommand.Arguments);

        var dryRun = contract.RecordBinding.DryRunCommand.Arguments;
        var write = contract.RecordBinding.WriteCommand.Arguments;
        Assert.Contains("--current", dryRun);
        Assert.Contains("<actual-current|absent|malformed>", dryRun);
        Assert.Contains("--new", dryRun);
        Assert.DoesNotContain("--routing-root", dryRun);
        Assert.DoesNotContain("--frontend", dryRun);
        Assert.Contains("--dry-run", dryRun);
        Assert.Contains("--write", write);
        Assert.DoesNotContain("--dry-run", write);
        Assert.Contains("--frontend <recorded-frontend>", contract.RecordBinding.SoloFrontendOption, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(ShapeFrontendCases))]
    public void Bootstrap_UsesRecordedShapeRoleHealthAndFrontendPolicy(string shapeName, string frontend, string policy)
    {
        using var workspace = new OrcaRunTestSupport.OrcaRunWorkspace($"mail-{shapeName}-{frontend}");
        string selectedRole;
        switch (shapeName)
        {
            case "solo-conductor":
                workspace.InstallSoloFixture();
                workspace.WriteSoloBinding(new
                {
                    schema_version = "1",
                    domain = OrcaRunTestSupport.Domain,
                    team = workspace.Team,
                    orca_run = new { role = "design", run_id = OrcaRunTestSupport.RunId, receive_policy = policy, frontend },
                });
                selectedRole = "design";
                break;
            case "four-seat":
                workspace.WriteDeliveryTopology(new Dictionary<string, object>
                {
                    ["orchestration"] = OrcaRunTestSupport.OrcaRunWorkspace.HerdrRole("w1:p1"),
                    ["implementation"] = OrcaRunTestSupport.OrcaRunWorkspace.HerdrRole("w1:p2"),
                    ["review"] = OrcaRunTestSupport.OrcaRunWorkspace.HerdrRole("w1:p3"),
                    ["architect"] = OrcaRunTestSupport.OrcaRunWorkspace.ExternalRoleFor(workspace.Team, frontend),
                });
                workspace.WriteTopologyOrcaRun("architect", OrcaRunTestSupport.RunId, policy);
                selectedRole = "architect";
                break;
            default:
                workspace.WriteDeliveryTopology(new Dictionary<string, object>
                {
                    ["orchestration"] = OrcaRunTestSupport.OrcaRunWorkspace.HerdrRole("w1:p1"),
                    ["implementation"] = OrcaRunTestSupport.OrcaRunWorkspace.HerdrRole("w1:p2"),
                    ["review"] = OrcaRunTestSupport.OrcaRunWorkspace.HerdrRole("w1:p3"),
                    ["design"] = OrcaRunTestSupport.OrcaRunWorkspace.ExternalRoleFor(workspace.Team, "claude-app"),
                    ["steward"] = OrcaRunTestSupport.OrcaRunWorkspace.ExternalRoleFor(workspace.Team, frontend),
                });
                workspace.WriteTopologyOrcaRun("steward", OrcaRunTestSupport.RunId, policy);
                selectedRole = "steward";
                break;
        }

        var contract = OrcaMailboxLifecycleGuidance.ResolveBootstrap(
            workspace.Root,
            OrcaRunTestSupport.Domain,
            workspace.Team);

        Assert.Equal(shapeName, contract.BindingSelection.TeamShape);
        Assert.Equal("healthy", contract.BindingSelection.ContextStatus);
        Assert.Equal(selectedRole, contract.BindingSelection.BindingSeatKey);
        Assert.Equal(selectedRole, contract.BindingSelection.ObservedBindingRole);
        Assert.Equal(OrcaRunTestSupport.RunId, contract.BindingSelection.RecordedRunId);
        Assert.Equal(policy, contract.BindingSelection.ReceivePolicy);
        Assert.Equal(OrcaRunBinding.RecordedHealth, contract.BindingSelection.BindingHealth);
        using var lifecycleJson = JsonDocument.Parse(JsonSerializer.Serialize(contract));
        var selection = lifecycleJson.RootElement.GetProperty("binding_selection");
        Assert.Equal(contract.BindingSelection.BindingLocation, selection.GetProperty("binding_location").GetString());
        Assert.Equal(contract.BindingSelection.BindingHealth, selection.GetProperty("binding_health").GetString());
        Assert.Equal(contract.BindingSelection.BindingCurrentToken, selection.GetProperty("binding_current_token").GetString());
        Assert.Equal(frontend, selection.GetProperty("selected_frontend").GetString());

        using var markdown = new StringWriter();
        OrcaMailboxLifecycleGuidance.WriteMarkdown(markdown, contract);
        var rendered = markdown.ToString();
        Assert.Contains($"- context status: `healthy`", rendered, StringComparison.Ordinal);
        Assert.Contains($"- recorded team shape: `{shapeName}`", rendered, StringComparison.Ordinal);
        Assert.Contains($"- selected recorded role key: `{selectedRole}`", rendered, StringComparison.Ordinal);
        Assert.Contains($"- recorded Run id: `{OrcaRunTestSupport.RunId}`", rendered, StringComparison.Ordinal);
        Assert.Contains($"- recorded receive policy: `{policy}`", rendered, StringComparison.Ordinal);
        Assert.Contains($"- binding location: `{contract.BindingSelection.BindingLocation}`", rendered, StringComparison.Ordinal);
        Assert.Contains($"- binding health: `{contract.BindingSelection.BindingHealth}`", rendered, StringComparison.Ordinal);
        Assert.Contains($"- binding current token: `{contract.BindingSelection.BindingCurrentToken}`", rendered, StringComparison.Ordinal);
        Assert.Contains($"- selected frontend: `{frontend}`", rendered, StringComparison.Ordinal);
    }

    public static IEnumerable<object[]> ShapeFrontendCases()
    {
        foreach (var shape in new[] { "solo-conductor", "four-seat", "five-seat" })
        foreach (var (frontend, policy) in Frontends)
            yield return [shape, frontend, policy];
    }

    [Fact]
    public void Bootstrap_SeparatesAbsentUnusableAndAmbiguousRoleBranches()
    {
        using var absent = new OrcaRunTestSupport.OrcaRunWorkspace("mail-absent");
        absent.WriteDeliveryTopology(new Dictionary<string, object>
        {
            ["orchestration"] = OrcaRunTestSupport.OrcaRunWorkspace.HerdrRole("w1:p1"),
            ["implementation"] = OrcaRunTestSupport.OrcaRunWorkspace.HerdrRole("w1:p2"),
            ["review"] = OrcaRunTestSupport.OrcaRunWorkspace.HerdrRole("w1:p3"),
            ["architect"] = OrcaRunTestSupport.OrcaRunWorkspace.ExternalRoleFor(absent.Team, "claude-app"),
        });
        var absentContract = OrcaMailboxLifecycleGuidance.ResolveBootstrap(absent.Root, OrcaRunTestSupport.Domain, absent.Team);
        Assert.Equal("absent", absentContract.BindingSelection.ContextStatus);
        Assert.Equal("architect", absentContract.BindingSelection.BindingSeatKey);
        Assert.Equal("absent", absentContract.BindingSelection.BindingCurrentToken);
        Assert.Null(absentContract.BindingSelection.RecordedRunId);

        absent.WriteTopologyOrcaRun("architect", "bad-run-id", "inbox-pull");
        var unusableContract = OrcaMailboxLifecycleGuidance.ResolveBootstrap(absent.Root, OrcaRunTestSupport.Domain, absent.Team);
        Assert.Equal("unusable", unusableContract.BindingSelection.ContextStatus);
        Assert.Equal("orca-run-id-malformed", unusableContract.BindingSelection.BindingHealth);
        Assert.NotEqual(OrcaRunBinding.RecordedHealth, unusableContract.BindingSelection.BindingHealth);
        Assert.Equal("malformed", unusableContract.BindingSelection.BindingCurrentToken);
        Assert.Null(unusableContract.BindingSelection.RecordedRunId);

        using var ambiguous = new OrcaRunTestSupport.OrcaRunWorkspace("mail-ambiguous");
        ambiguous.InstallAliasFourSeatFixture();
        var ambiguousContract = OrcaMailboxLifecycleGuidance.ResolveBootstrap(ambiguous.Root, OrcaRunTestSupport.Domain, ambiguous.Team);
        Assert.Equal("prerequisite", ambiguousContract.BindingSelection.ContextStatus);
        Assert.Null(ambiguousContract.BindingSelection.BindingSeatKey);
        Assert.Contains("more than one role key", ambiguousContract.BindingSelection.UnresolvedReason, StringComparison.Ordinal);

        var missingScope = OrcaMailboxLifecycleGuidance.ResolveBootstrap(absent.Root, domain: null, team: null);
        Assert.Equal("prerequisite", missingScope.BindingSelection.ContextStatus);
        Assert.Null(missingScope.BindingSelection.TeamShape);
    }

    [Fact]
    public void Bootstrap_MalformedSoloSidecarIsUnusableAndPreservesItsCanonicalCurrentToken()
    {
        using var workspace = new OrcaRunTestSupport.OrcaRunWorkspace("mail-solo-malformed");
        workspace.InstallSoloFixture();
        Directory.CreateDirectory(Path.GetDirectoryName(workspace.SoloBindingPath)!);
        var malformedBytes = Encoding.UTF8.GetBytes("{\"orca_run\":{\"run_id\":\n");
        File.WriteAllBytes(workspace.SoloBindingPath, malformedBytes);
        var before = File.ReadAllBytes(workspace.SoloBindingPath);

        var contract = OrcaMailboxLifecycleGuidance.ResolveBootstrap(
            workspace.Root,
            OrcaRunTestSupport.Domain,
            workspace.Team);

        Assert.Equal("unusable", contract.BindingSelection.ContextStatus);
        Assert.Equal("orca-run-binding-malformed", contract.BindingSelection.BindingHealth);
        Assert.Equal("malformed", contract.BindingSelection.BindingCurrentToken);
        Assert.Equal(OrcaRunBinding.OrcaRunFileLocation, contract.BindingSelection.BindingLocation);
        Assert.Equal(before, File.ReadAllBytes(workspace.SoloBindingPath));
    }

    [Fact]
    public void Bootstrap_UnreadableTopologyIsNotReportedAsAbsentAndPreservesItsBytes()
    {
        using var workspace = new OrcaRunTestSupport.OrcaRunWorkspace("mail-topology-unreadable");
        workspace.InstallFourSeatDeliveryFixture();
        var unreadableBytes = Encoding.UTF8.GetBytes("{\"roles\":\n");
        File.WriteAllBytes(workspace.TopologyPath, unreadableBytes);

        var contract = OrcaMailboxLifecycleGuidance.ResolveBootstrap(
            workspace.Root,
            OrcaRunTestSupport.Domain,
            workspace.Team);

        Assert.Equal("unusable", contract.BindingSelection.ContextStatus);
        Assert.Equal("topology-unreadable", contract.BindingSelection.BindingHealth);
        Assert.Null(contract.BindingSelection.BindingCurrentToken);
        Assert.Equal(unreadableBytes, File.ReadAllBytes(workspace.TopologyPath));
    }

    [Fact]
    public void Bootstrap_ConflictingSoloAndTopologyRecordsStayUnusableAndPreserveBoth()
    {
        using var workspace = new OrcaRunTestSupport.OrcaRunWorkspace("mail-cross-location-conflict");
        workspace.InstallFourSeatDeliveryFixture();
        workspace.WriteTopologyOrcaRun("design", OrcaRunTestSupport.RunId, OrcaRunBinding.InboxPullPolicy);
        var topologyBefore = workspace.TopologyBytes();
        workspace.WriteSoloBinding(new
        {
            schema_version = "1",
            domain = OrcaRunTestSupport.Domain,
            team = workspace.Team,
            orca_run = new
            {
                role = "design",
                run_id = "run_111111111111",
                receive_policy = OrcaRunBinding.InboxPullPolicy,
                frontend = "claude-app",
            },
        });
        var soloBefore = File.ReadAllBytes(workspace.SoloBindingPath);

        var contract = OrcaMailboxLifecycleGuidance.ResolveBootstrap(
            workspace.Root,
            OrcaRunTestSupport.Domain,
            workspace.Team);

        Assert.Equal("unusable", contract.BindingSelection.ContextStatus);
        Assert.Equal("binding-location-not-allowed", contract.BindingSelection.BindingHealth);
        Assert.Equal(OrcaRunBinding.TopologyRoleLocation, contract.BindingSelection.BindingLocation);
        Assert.Equal(OrcaRunTestSupport.RunId, contract.BindingSelection.BindingCurrentToken);
        Assert.Equal(topologyBefore, workspace.TopologyBytes());
        Assert.Equal(soloBefore, File.ReadAllBytes(workspace.SoloBindingPath));
    }

    [Fact]
    public void Bootstrap_StrangerTopologyBindingUnderSoloModeIsUnusableAndPreserved()
    {
        using var workspace = new OrcaRunTestSupport.OrcaRunWorkspace("mail-solo-stranded-topology");
        workspace.InstallFourSeatDeliveryFixture();
        workspace.WriteTopologyOrcaRun("design", OrcaRunTestSupport.RunId, OrcaRunBinding.InboxPullPolicy);
        workspace.InstallSoloFixture();
        var topologyBefore = workspace.TopologyBytes();
        Assert.False(File.Exists(workspace.SoloBindingPath));

        var contract = OrcaMailboxLifecycleGuidance.ResolveBootstrap(
            workspace.Root,
            OrcaRunTestSupport.Domain,
            workspace.Team);

        Assert.Equal("unusable", contract.BindingSelection.ContextStatus);
        Assert.Equal("binding-location-not-allowed", contract.BindingSelection.BindingHealth);
        Assert.Equal(OrcaRunBinding.TopologyRoleLocation, contract.BindingSelection.BindingLocation);
        Assert.Equal(OrcaRunTestSupport.RunId, contract.BindingSelection.BindingCurrentToken);
        Assert.Equal(topologyBefore, workspace.TopologyBytes());
        Assert.False(File.Exists(workspace.SoloBindingPath));
    }

    [Theory]
    [InlineData("four-seat", "herdr", "receive-policy-herdr-seat")]
    [InlineData("four-seat", "missing-frontend", "receive-policy-seat-kind-unrecorded")]
    [InlineData("four-seat", "unsupported-frontend", "receive-policy-seat-kind-unrecorded")]
    [InlineData("five-seat", "herdr", "receive-policy-herdr-seat")]
    [InlineData("five-seat", "missing-frontend", "receive-policy-seat-kind-unrecorded")]
    [InlineData("five-seat", "unsupported-frontend", "receive-policy-seat-kind-unrecorded")]
    public void Bootstrap_AbsentDeliveryBindingRequiresRecordableSelectedSeat(
        string shapeName,
        string selectedSeatState,
        string expectedCause)
    {
        using var workspace = new OrcaRunTestSupport.OrcaRunWorkspace($"mail-seat-prerequisite-{shapeName}-{selectedSeatState}");
        var selectedRole = shapeName == "four-seat" ? "design" : "steward";
        object selectedSeat = selectedSeatState switch
        {
            "herdr" => OrcaRunTestSupport.OrcaRunWorkspace.HerdrRole("w1:p9"),
            "missing-frontend" => new
            {
                resident = "external",
                reader = $".intent-cli/events/{OrcaRunTestSupport.Domain}/{workspace.Team}-reader.jsonl",
            },
            _ => new
            {
                resident = "external",
                reader = $".intent-cli/events/{OrcaRunTestSupport.Domain}/{workspace.Team}-reader.jsonl",
                frontend = "unsupported-app",
            },
        };
        var roles = new Dictionary<string, object>
        {
            ["orchestration"] = OrcaRunTestSupport.OrcaRunWorkspace.HerdrRole("w1:p1"),
            ["implementation"] = OrcaRunTestSupport.OrcaRunWorkspace.HerdrRole("w1:p2"),
            ["review"] = OrcaRunTestSupport.OrcaRunWorkspace.HerdrRole("w1:p3"),
            ["design"] = shapeName == "four-seat"
                ? selectedSeat
                : OrcaRunTestSupport.OrcaRunWorkspace.ExternalRoleFor(workspace.Team, "claude-app"),
        };
        if (shapeName == "five-seat")
        {
            roles["steward"] = selectedSeat;
        }
        workspace.WriteDeliveryTopology(roles);
        var topologyBefore = workspace.TopologyBytes();

        var contract = OrcaMailboxLifecycleGuidance.ResolveBootstrap(
            workspace.Root,
            OrcaRunTestSupport.Domain,
            workspace.Team);

        Assert.Equal("prerequisite", contract.BindingSelection.ContextStatus);
        Assert.Equal(selectedRole, contract.BindingSelection.BindingSeatKey);
        Assert.Equal("absent", contract.BindingSelection.BindingCurrentToken);
        Assert.Contains(expectedCause, contract.BindingSelection.UnresolvedReason, StringComparison.Ordinal);
        Assert.Contains("Do not create a Run", contract.BindingSelection.UnresolvedReason, StringComparison.Ordinal);
        Assert.Equal(topologyBefore, workspace.TopologyBytes());
    }

    [Fact]
    public void Bootstrap_SoloModeWithNoBindingIsGenuinelyAbsent()
    {
        using var workspace = new OrcaRunTestSupport.OrcaRunWorkspace("mail-solo-absent");
        workspace.InstallSoloFixture();

        var contract = OrcaMailboxLifecycleGuidance.ResolveBootstrap(
            workspace.Root,
            OrcaRunTestSupport.Domain,
            workspace.Team);

        Assert.Equal("absent", contract.BindingSelection.ContextStatus);
        Assert.Equal("design", contract.BindingSelection.BindingSeatKey);
        Assert.Equal(OrcaRunBinding.OrcaRunFileLocation, contract.BindingSelection.BindingLocation);
        Assert.Equal("absent", contract.BindingSelection.BindingCurrentToken);
    }

    [Fact]
    public void Bootstrap_RecordedAuthoringOnlyModeRemainsAPrerequisite()
    {
        using var workspace = new OrcaRunTestSupport.OrcaRunWorkspace("mail-authoring-only");
        workspace.WriteTeamModeFile(TeamMode.AuthoringOnly, workspace.Team);

        var contract = OrcaMailboxLifecycleGuidance.ResolveBootstrap(
            workspace.Root,
            OrcaRunTestSupport.Domain,
            workspace.Team);

        Assert.Equal("prerequisite", contract.BindingSelection.ContextStatus);
        Assert.Null(contract.BindingSelection.TeamShape);
        Assert.Null(contract.BindingSelection.BindingCurrentToken);
        Assert.Contains("Recorded team mode or roster is a prerequisite", contract.BindingSelection.UnresolvedReason, StringComparison.Ordinal);
        Assert.Contains("Repair canonical mode or roster", contract.BindingSelection.UnresolvedReason, StringComparison.Ordinal);
    }

    [Fact]
    public void Bootstrap_UnrecordedTeamModeRemainsAPrerequisite()
    {
        using var workspace = new OrcaRunTestSupport.OrcaRunWorkspace("mail-mode-unrecorded");
        workspace.WriteDeliveryTopology(new Dictionary<string, object>
        {
            ["orchestration"] = OrcaRunTestSupport.OrcaRunWorkspace.HerdrRole("w1:p1"),
            ["implementation"] = OrcaRunTestSupport.OrcaRunWorkspace.HerdrRole("w1:p2"),
            ["review"] = OrcaRunTestSupport.OrcaRunWorkspace.HerdrRole("w1:p3"),
            ["design"] = OrcaRunTestSupport.OrcaRunWorkspace.ExternalRoleFor(workspace.Team, "claude-app"),
        });
        File.Delete(workspace.TeamModePath);

        var contract = OrcaMailboxLifecycleGuidance.ResolveBootstrap(
            workspace.Root,
            OrcaRunTestSupport.Domain,
            workspace.Team);

        Assert.Equal("prerequisite", contract.BindingSelection.ContextStatus);
        Assert.Null(contract.BindingSelection.TeamShape);
        Assert.Null(contract.BindingSelection.BindingCurrentToken);
        Assert.Contains("no shape or frontend is guessed", contract.BindingSelection.UnresolvedReason, StringComparison.Ordinal);
    }

    [Fact]
    public void Bootstrap_WrongSeatBindingIsUnusableAndRetainsItsCurrentToken()
    {
        using var workspace = new OrcaRunTestSupport.OrcaRunWorkspace("mail-wrong-seat");
        workspace.InstallFourSeatDeliveryFixture();
        workspace.WriteTopologyOrcaRun("review", OrcaRunTestSupport.RunId, OrcaRunBinding.InboxPullPolicy);
        var before = workspace.TopologyBytes();

        var contract = OrcaMailboxLifecycleGuidance.ResolveBootstrap(
            workspace.Root,
            OrcaRunTestSupport.Domain,
            workspace.Team);

        Assert.Equal("unusable", contract.BindingSelection.ContextStatus);
        Assert.Equal("binding-seat-not-allowed", contract.BindingSelection.BindingHealth);
        Assert.Equal("review", contract.BindingSelection.ObservedBindingRole);
        Assert.Equal("design", contract.BindingSelection.BindingSeatKey);
        Assert.Equal(OrcaRunTestSupport.RunId, contract.BindingSelection.BindingCurrentToken);
        Assert.Equal(before, workspace.TopologyBytes());
    }

    [Fact]
    public void Bootstrap_ContradictoryAliasBindingsStayUnusableAndUnselected()
    {
        using var conflicting = new OrcaRunTestSupport.OrcaRunWorkspace("mail-conflicting-bindings");
        conflicting.InstallAliasFourSeatFixture();
        conflicting.WriteTopologyOrcaRun("design", OrcaRunTestSupport.RunId, OrcaRunBinding.InboxPullPolicy);
        conflicting.WriteTopologyOrcaRun("architect", "run_111111111111", OrcaRunBinding.InboxPullPolicy);

        var contract = OrcaMailboxLifecycleGuidance.ResolveBootstrap(
            conflicting.Root,
            OrcaRunTestSupport.Domain,
            conflicting.Team);

        Assert.Equal("unusable", contract.BindingSelection.ContextStatus);
        Assert.Equal("binding-duplicate", contract.BindingSelection.BindingHealth);
        Assert.Null(contract.BindingSelection.BindingSeatKey);
        Assert.Contains("more than one role key", contract.BindingSelection.UnresolvedReason, StringComparison.Ordinal);
        Assert.Null(contract.BindingSelection.RecordedRunId);
    }

    [Fact]
    public void GuideConsumers_RenderContractWithoutCallingOrcaOrProvidersOrChangingRecordedState()
    {
        using var workspace = new OrcaRunTestSupport.OrcaRunWorkspace("mail-guide-no-exec");
        workspace.InstallFourSeatDeliveryFixture();
        var foreignTeam = $"{workspace.Team}-foreign";
        var foreignPath = NotifyRoleTopologyStore.ResolvePath(workspace.Root, OrcaRunTestSupport.Domain, foreignTeam);
        Directory.CreateDirectory(Path.GetDirectoryName(foreignPath)!);
        File.WriteAllText(foreignPath, "{\"foreign_sentinel\":\"preserve-exactly\"}\n");
        var foreignBefore = File.ReadAllBytes(foreignPath);
        var topologyBefore = workspace.TopologyBytes();
        var teamModeBefore = workspace.TeamModeBytes();
        var providerLog = Path.Combine(workspace.Root, "fake-provider.log");
        var oldProviderLog = Environment.GetEnvironmentVariable("G853_FAKE_PROVIDER_LOG");
        foreach (var provider in new[] { "codex", "claude" })
        {
            var path = Path.Combine(workspace.FakeBin.BinDirectory, provider);
            File.WriteAllText(path, "#!/bin/sh\necho called >> \"$G853_FAKE_PROVIDER_LOG\"\nexit 97\n");
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        Environment.SetEnvironmentVariable("G853_FAKE_PROVIDER_LOG", providerLog);

        try
        {
            var context = workspace.Context;
            var guideCalls = new (string Name, Func<string, int> Render)[]
            {
                ("onboarding", format => GuideOnboardingCommand.Execute(context, ["--format", format], new StringWriter())),
                ("bootstrap", format => GuideBootstrapCommand.Execute(context, ["--domain", OrcaRunTestSupport.Domain, "--team", workspace.Team, "--routing-root", workspace.Root, "--format", format], new StringWriter())),
                ("design-thread", format => GuideDesignThreadCommand.Execute(context, ["--domain", OrcaRunTestSupport.Domain, "--team", workspace.Team, "--routing-root", workspace.Root, "--format", format], new StringWriter())),
                ("solo-conductor", format => GuideSoloConductorCommand.Execute(context, ["--format", format], new StringWriter())),
                ("steward-thread", format => GuideStewardThreadCommand.Execute(context, ["--format", format], new StringWriter())),
            };
            foreach (var (name, render) in guideCalls)
            foreach (var format in new[] { "json", "markdown" })
                Assert.Equal(0, render(format));

            OrcaRunTestSupport.AssertOrcaLogEmpty(workspace.FakeBin);
            Assert.Empty(File.ReadAllText(workspace.FakeBin.GhLogPath));
            Assert.Empty(File.Exists(providerLog) ? File.ReadAllText(providerLog) : string.Empty);
            Assert.Equal(topologyBefore, workspace.TopologyBytes());
            Assert.Equal(teamModeBefore, workspace.TeamModeBytes());
            Assert.Equal(foreignBefore, File.ReadAllBytes(foreignPath));
        }
        finally
        {
            Environment.SetEnvironmentVariable("G853_FAKE_PROVIDER_LOG", oldProviderLog);
        }
    }

    [Fact]
    public void AllFiveGuideEntryPointsExposeTheSameContractInJsonAndMarkdown()
    {
        using var workspace = new OrcaRunTestSupport.OrcaRunWorkspace("mail-guide-reachability");
        workspace.InstallFourSeatDeliveryFixture();
        var context = workspace.Context;
        var guides = new (string Name, Func<string, TextWriter, int> Execute)[]
        {
            ("onboarding", (format, writer) => GuideOnboardingCommand.Execute(context, ["--format", format], writer)),
            ("bootstrap", (format, writer) => GuideBootstrapCommand.Execute(context, ["--domain", OrcaRunTestSupport.Domain, "--team", workspace.Team, "--routing-root", workspace.Root, "--format", format], writer)),
            ("design-thread", (format, writer) => GuideDesignThreadCommand.Execute(context, ["--domain", OrcaRunTestSupport.Domain, "--team", workspace.Team, "--routing-root", workspace.Root, "--format", format], writer)),
            ("solo-conductor", (format, writer) => GuideSoloConductorCommand.Execute(context, ["--format", format], writer)),
            ("steward-thread", (format, writer) => GuideStewardThreadCommand.Execute(context, ["--format", format], writer)),
        };

        foreach (var (name, execute) in guides)
        {
            using var jsonWriter = new StringWriter();
            Assert.Equal(0, execute("json", jsonWriter));
            using var document = JsonDocument.Parse(jsonWriter.ToString());
            var contract = document.RootElement.GetProperty("orca_mailbox_lifecycle");
            Assert.Equal(OrcaMailboxLifecycleGuidance.ContractVersion, contract.GetProperty("contract_version").GetString());
            Assert.False(contract.GetProperty("intent_cli_executes_orca").GetBoolean());
            Assert.Contains("capability_check", contract.ToString(), StringComparison.Ordinal);
            Assert.Contains("caller_identity", contract.ToString(), StringComparison.Ordinal);
            Assert.Contains("binding_selection", contract.ToString(), StringComparison.Ordinal);
            Assert.Contains("record_binding", contract.ToString(), StringComparison.Ordinal);
            Assert.Contains("recovery", contract.ToString(), StringComparison.Ordinal);
            Assert.Contains("completion_boundary", contract.ToString(), StringComparison.Ordinal);

            using var markdownWriter = new StringWriter();
            Assert.Equal(0, execute("markdown", markdownWriter));
            var markdown = markdownWriter.ToString();
            Assert.Contains("## Orca mailbox lifecycle", markdown, StringComparison.Ordinal);
            Assert.Contains(OrcaMailboxLifecycleGuidance.ContractVersion, markdown, StringComparison.Ordinal);
            Assert.Contains("intent-cli executes Orca: **false**", markdown, StringComparison.Ordinal);
            Assert.Contains("run-create", markdown, StringComparison.Ordinal);
            Assert.Contains("record-orca-run", markdown, StringComparison.Ordinal);
            Assert.Contains("### Mutation recovery", markdown, StringComparison.Ordinal);
            Assert.Contains("#1771 disposition and completion boundary", markdown, StringComparison.Ordinal);
            Assert.False(string.IsNullOrWhiteSpace(name));
        }
    }

    [Fact]
    public void Contract_ContainsCallerUnknownOutcomeAndWholeBatchRefusals()
    {
        var contract = OrcaMailboxLifecycleGuidance.CreateTemplate();
        var identityless = contract.CallerIdentity.Branches.Single(branch => branch.Caller.Contains("identity-less", StringComparison.Ordinal));
        Assert.Contains("Stop lifecycle mutation", identityless.FailureAction, StringComparison.Ordinal);
        Assert.Contains("borrow a terminal", identityless.FailureAction, StringComparison.Ordinal);
        Assert.Contains("fabricate a session identity", identityless.FailureAction, StringComparison.Ordinal);
        Assert.Contains("provider", contract.CapabilityCheck.FailureAction, StringComparison.Ordinal);
        Assert.Contains("completed", contract.Recovery.CompletedBranch, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Pending", contract.Recovery.PendingBranch, StringComparison.Ordinal);
        Assert.Contains("Absent is not proof", contract.Recovery.UnknownBranch, StringComparison.Ordinal);
        Assert.Contains("never authorize a fresh create/send/ACK", contract.Recovery.FailureAction, StringComparison.Ordinal);
        Assert.Contains("Handle every row", contract.Receive.BatchRule, StringComparison.Ordinal);
        Assert.Contains("--types affects wake conditions, not batch membership", contract.Receive.BatchRule, StringComparison.Ordinal);
        Assert.Contains("ACK may return another batch", contract.Receive.AckRule, StringComparison.Ordinal);
        Assert.Contains("--peek and --all inspect only", contract.Receive.InspectionRule, StringComparison.Ordinal);
    }

    [Fact]
    public void MissingCapabilityAndIdentityHaveExplicitStopBranches()
    {
        var contract = OrcaMailboxLifecycleGuidance.CreateTemplate();
        Assert.Contains("installed CLI", contract.CapabilityCheck.Preconditions.Single(), StringComparison.Ordinal);
        Assert.Contains("Report capability unavailable", contract.CapabilityCheck.FailureAction, StringComparison.Ordinal);
        Assert.Contains("preserve any existing binding", contract.CapabilityCheck.FailureAction, StringComparison.Ordinal);
        Assert.Contains("Do not install/start Orca", contract.CapabilityCheck.FailureAction, StringComparison.Ordinal);

        var session = contract.CallerIdentity.Branches.Single(branch => branch.Caller == "authenticated Orca session");
        Assert.Contains("omit terminal caller overrides", session.Rule, StringComparison.Ordinal);
        var terminal = contract.CallerIdentity.Branches.Single(branch => branch.Caller == "terminal caller");
        Assert.Contains("verified as this caller's own identity", terminal.Rule, StringComparison.Ordinal);
        var identityless = contract.CallerIdentity.Branches.Single(branch => branch.Caller.Contains("identity-less", StringComparison.Ordinal));
        Assert.Contains("missing Orca caller identity prerequisite", identityless.Check, StringComparison.Ordinal);
        Assert.Contains("Never borrow a terminal handle", identityless.FailureAction, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("create")]
    [InlineData("send")]
    [InlineData("ACK")]
    public void UnknownCreateSendAndAckOutcomesRequireExactReceiptRecovery(string operation)
    {
        var recovery = OrcaMailboxLifecycleGuidance.CreateTemplate().Recovery;
        Assert.Contains(operation, "create/send/ACK", StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<reported-request-id>", recovery.RequestShowCommand.Arguments);
        Assert.Contains(operation, recovery.FailureAction, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<exact-original-command> --retry-request <same-reported-request-id>", recovery.ExactReplayTemplate, StringComparison.Ordinal);
        Assert.Contains("exact original command", recovery.PreserveRule, StringComparison.Ordinal);
        Assert.Contains("same id", recovery.PendingBranch, StringComparison.Ordinal);
        Assert.Contains("never authorize a fresh create/send/ACK", recovery.FailureAction, StringComparison.Ordinal);
        Assert.Contains("guessed Delivery id", recovery.FailureAction, StringComparison.Ordinal);
    }

    private static IEnumerable<OrcaMailboxCommand> AllCommands(OrcaMailboxLifecycleContract contract) =>
        contract.CapabilityCheck.Commands
            .Concat(contract.CreateOrAdopt.Commands)
            .Append(contract.RecordBinding.DryRunCommand)
            .Append(contract.RecordBinding.WriteCommand)
            .Append(contract.Delivery.DiscoveryCommand)
            .Append(contract.Delivery.SessionSendCommand)
            .Append(contract.Delivery.TerminalSendCommand)
            .Append(contract.Receive.SessionCheckCommand)
            .Append(contract.Receive.TerminalCheckCommand)
            .Append(contract.Receive.SessionAckCommand)
            .Append(contract.Receive.TerminalAckCommand)
            .Append(contract.Recovery.RequestShowCommand);

    private static IEnumerable<JsonElement> FindSerializedCommands(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty("executable", out _)
                && element.TryGetProperty("caller", out _)
                && element.TryGetProperty("arguments", out _))
            {
                yield return element;
                yield break;
            }

            foreach (var property in element.EnumerateObject())
            foreach (var command in FindSerializedCommands(property.Value))
                yield return command;
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in element.EnumerateArray())
            foreach (var command in FindSerializedCommands(child))
                yield return command;
        }
    }

    private static string[] ParseRenderedCommand(string commandLine)
    {
        var arguments = new List<string>();
        var token = new StringBuilder();
        var quoted = false;
        foreach (var character in commandLine)
        {
            if (character == '"')
            {
                quoted = !quoted;
            }
            else if (char.IsWhiteSpace(character) && !quoted)
            {
                if (token.Length > 0)
                {
                    arguments.Add(token.ToString());
                    token.Clear();
                }
            }
            else
            {
                token.Append(character);
            }
        }

        Assert.False(quoted, $"Unbalanced quotes in rendered command: {commandLine}");
        if (token.Length > 0) arguments.Add(token.ToString());
        return arguments.ToArray();
    }
}
