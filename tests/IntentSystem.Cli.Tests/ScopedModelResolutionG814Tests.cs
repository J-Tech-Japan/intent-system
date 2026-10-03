using IntentSystem.Cli.Commands;

namespace IntentSystem.Cli.Tests;

public sealed class ScopedModelResolutionG814Tests : IDisposable
{
    private readonly string root = Directory.CreateTempSubdirectory("g814-scoped-model-").FullName;

    [Theory]
    [InlineData("\"/usr/local/bin/codex\" --model \"m-fixture\" -c \"model_reasoning_effort=medium\" --add-dir \"/tmp/project with spaces\"", "codex", "m-fixture", "medium")]
    [InlineData("claude --model 'm-fixture' --effort \"high\"", "claude", "m-fixture", "high")]
    public void InvocationParser_AcceptsMeasuredGrammarAndBalancedTokenQuotes(
        string invocation,
        string kind,
        string model,
        string effort)
    {
        var parsed = ModelResolutionInvocationParser.ParseInvocation(invocation, kind);

        Assert.True(parsed.Resolved, parsed.Reason);
        Assert.Equal(kind, parsed.Kind);
        Assert.Equal(model, parsed.Model);
        Assert.Equal(effort, parsed.Effort);
    }

    [Theory]
    [InlineData("codex --model=m-fixture -c model_reasoning_effort=medium", "codex")]
    [InlineData("codex --model m-fixture -c=model_reasoning_effort=medium", "codex")]
    [InlineData("codex --model m-fixture -c model_reasoning_effort=medium -c model_reasoning_effort=high", "codex")]
    [InlineData("codex --model m-fixture --model other -c model_reasoning_effort=medium", "codex")]
    [InlineData("codex --model -c model_reasoning_effort=medium", "codex")]
    [InlineData("codex --model m-fixture", "codex")]
    [InlineData("codex --prompt \"use --model fake -c model_reasoning_effort=low\"", "codex")]
    [InlineData("codex --prompt --model decoy --model real -c model_reasoning_effort=medium", "codex")]
    [InlineData("codex --model m-fixture -c model_reasoning_effort=medium; echo unsafe", "codex")]
    [InlineData("codex --model \"m-fixture -c model_reasoning_effort=medium", "codex")]
    [InlineData("claude --model m-fixture --effort=high", "claude")]
    [InlineData("claude --model m-fixture --effort high --effort low", "claude")]
    [InlineData("claude --model m-fixture", "claude")]
    [InlineData("claude --model m-fixture -c model_reasoning_effort=medium", "claude")]
    public void InvocationParser_FailsClosedOnAmbiguousOrUnmeasuredSyntax(string invocation, string kind)
    {
        var parsed = ModelResolutionInvocationParser.ParseInvocation(invocation, kind);

        Assert.False(parsed.Resolved);
        Assert.False(string.IsNullOrWhiteSpace(parsed.Reason));
    }

    [Fact]
    public void StructuredArgvParser_DoesNotFindFlagsInsidePromptOrAddDirectoryValues()
    {
        var parsed = ModelResolutionInvocationParser.ParseArgv(
            ["codex", "--prompt", "write about --model fake and -c model_reasoning_effort=low", "--add-dir",
                "/tmp/--model=decoy", "--model", "m-actual", "-c", "model_reasoning_effort=medium"],
            "codex");

        Assert.True(parsed.Resolved, parsed.Reason);
        Assert.Equal("m-actual", parsed.Model);
        Assert.Equal("medium", parsed.Effort);

        var flagLikePromptValue = ModelResolutionInvocationParser.ParseArgv(
            ["codex", "--prompt", "--model", "decoy", "--model", "m-actual", "-c", "model_reasoning_effort=medium"],
            "codex");
        Assert.False(flagLikePromptValue.Resolved);
        Assert.Equal("ambiguous-option-value", flagLikePromptValue.Reason);

        var lookalikeOnly = ModelResolutionInvocationParser.ParseArgv(
            ["codex", "--prompt", "--model fake -c model_reasoning_effort=low", "--add-dir",
                "/tmp/--model=decoy"],
            "codex");
        Assert.False(lookalikeOnly.Resolved);
        Assert.Equal("ambiguous-option-value", lookalikeOnly.Reason);
    }

    [Fact]
    public void ProductionTopologySelection_CoalescesAgreeingAliasesAndReportsConflictingFields()
    {
        var topology = ReadTopology("architect", "design");
        var resolved = ScopedModelResolutionTargetSelector.Select(
            topology, "intent-cli", "dev", "design", "codex");

        Assert.True(resolved.Resolved, resolved.Detail);
        Assert.Equal("architect", resolved.Target!.Role);
        Assert.Equal("workspace-a:p1", resolved.Target.PaneId);
        Assert.Equal(new[] { "architect", "design" }, resolved.Target.RoleAliases);
        Assert.Equal(64, resolved.Target.TopologyDigest.Length);

        var conflicting = ReadTopology("architect", "design", designPane: "workspace-a:p2");
        var conflict = ScopedModelResolutionTargetSelector.Select(
            conflicting, "intent-cli", "dev", "architect", "codex");

        Assert.False(conflict.Resolved);
        Assert.Equal("alias-conflict", conflict.Reason);
        Assert.Contains("architect", conflict.Detail, StringComparison.Ordinal);
        Assert.Contains("design", conflict.Detail, StringComparison.Ordinal);
        Assert.Contains("pane_id", conflict.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void SelectedDigest_IgnoresUnrelatedRoleButChangesWithSelectedDeclaration()
    {
        var baseTopology = CreateTopology(
            ("architect", Herdr("workspace-a:p1", "codex", model: "m1")),
            ("design", Herdr("workspace-a:p1", "codex", model: "m1")),
            ("builder", Herdr("workspace-a:p2", "claude", model: "m-builder")));
        var first = ScopedModelResolutionTargetSelector.Select(baseTopology, "intent-cli", "dev", "architect", "codex");
        Assert.True(first.Resolved, first.Detail);

        var unrelatedChanged = baseTopology with
        {
            Roles = new Dictionary<string, NotifyRecordedRole>(StringComparer.Ordinal)
            {
                ["architect"] = Herdr("workspace-a:p1", "codex", model: "m1"),
                ["design"] = Herdr("workspace-a:p1", "codex", model: "m1"),
                ["builder"] = Herdr("workspace-a:p9", "codex", model: "other"),
                ["reviewer"] = Herdr("workspace-a:p7", "claude", effort: "high"),
            },
        };
        var second = ScopedModelResolutionTargetSelector.Select(unrelatedChanged, "intent-cli", "dev", "architect", "codex");
        Assert.True(second.Resolved, second.Detail);
        Assert.Equal(first.Target!.TopologyDigest, second.Target!.TopologyDigest);

        var selectedChanged = baseTopology with
        {
            Roles = new Dictionary<string, NotifyRecordedRole>(StringComparer.Ordinal)
            {
                ["architect"] = Herdr("workspace-a:p1", "codex", model: "m2"),
                ["design"] = Herdr("workspace-a:p1", "codex", model: "m2"),
                ["builder"] = Herdr("workspace-a:p2", "claude", model: "m-builder"),
            },
        };
        var third = ScopedModelResolutionTargetSelector.Select(selectedChanged, "intent-cli", "dev", "architect", "codex");
        Assert.True(third.Resolved, third.Detail);
        Assert.NotEqual(first.Target.TopologyDigest, third.Target!.TopologyDigest);
    }

    [Fact]
    public void ExactScopeKey_ExcludesGenerationAndExplicitInformalAttributionLabel()
    {
        var target = SelectTarget();
        var request = new ScopedModelResolutionRequest("friendly label A", "m-requested", "medium");
        var key = ScopedModelResolutionScopeKey.Create(target, request);
        var sameExplicitKey = ScopedModelResolutionScopeKey.Create(
            target, request with { InformalName = "friendly label B" });
        var entry = ScopedEntry(
            target,
            request,
            informalName: "friendly label B",
            host: "another-ignored-provenance-value",
            processId: 999,
            startTime: "different-generation",
            digest: "older-digest");

        Assert.True(key.Matches(entry));
        Assert.Equal(key, sameExplicitKey);
        Assert.False(key.Matches(entry with { RequestedModel = "other-model" }));
        Assert.False(key.Matches(entry with { RequestedEffort = "high" }));

        var informalRequest = new ScopedModelResolutionRequest("informal A", null, "medium");
        var informalKey = ScopedModelResolutionScopeKey.Create(target, informalRequest);
        Assert.True(informalKey.Matches(ScopedEntry(target, informalRequest, "informal A")));
        Assert.False(informalKey.Matches(ScopedEntry(target, informalRequest, "informal B")));
    }

    [Fact]
    public void ExternalResidentAndMissingTargetSelectNoPane()
    {
        var external = CreateTopology(("architect", new NotifyRecordedRole(
            NotifyRecordedRole.ExternalResident, "workspace-a", null, "reader.jsonl", null, "codex",
            null, null)));
        var externalResult = ScopedModelResolutionTargetSelector.Select(
            external, "intent-cli", "dev", "architect", "codex");
        Assert.False(externalResult.Resolved);
        Assert.Equal("target-absent", externalResult.Reason);
        Assert.Null(externalResult.Target);

        var absent = ScopedModelResolutionTargetSelector.Select(
            CreateTopology(("builder", Herdr("workspace-a:p2", "codex"))),
            "intent-cli", "dev", "architect", "codex");
        Assert.False(absent.Resolved);
        Assert.Equal("target-absent", absent.Reason);
        Assert.Null(absent.Target);
    }

    private ScopedModelResolutionTarget SelectTarget()
    {
        var result = ScopedModelResolutionTargetSelector.Select(
            CreateTopology(("architect", Herdr("workspace-a:p1", "codex"))),
            "intent-cli", "dev", "architect", "codex");
        Assert.True(result.Resolved, result.Detail);
        return result.Target!;
    }

    private NotifyTeamTopology ReadTopology(string firstAlias, string secondAlias, string? designPane = null)
    {
        var path = Path.Combine(root, ".intent-cli", "topology", "intent-cli", "dev.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var pane = designPane ?? "workspace-a:p1";
        File.WriteAllText(path, $$"""
        {
          "domain": "intent-cli",
          "team": "dev",
          "workspace_id": "workspace-a",
          "roles": {
            "{{firstAlias}}": { "resident": "herdr", "workspace_id": "workspace-a", "pane_id": "workspace-a:p1", "kind": "codex", "model": "m1", "reasoning_effort": "medium" },
            "{{secondAlias}}": { "resident": "herdr", "workspace_id": "workspace-a", "pane_id": "{{pane}}", "kind": "codex", "model": "m1", "reasoning_effort": "medium" }
          }
        }
        """);

        var resolution = NotifyRoleTopologyStore.Resolve(root, "intent-cli", "dev");
        Assert.True(resolution.Resolved, resolution.Summary);
        return resolution.Topology!;
    }

    private static NotifyTeamTopology CreateTopology(params (string Role, NotifyRecordedRole Record)[] roles) => new(
        "synthetic-topology",
        "intent-cli",
        "dev",
        "workspace-a",
        roles.ToDictionary(item => item.Role, item => item.Record, StringComparer.Ordinal),
        new Dictionary<string, AgentLaunchEnvelopeProfile>(StringComparer.Ordinal));

    private static NotifyRecordedRole Herdr(
        string pane,
        string kind,
        string? model = null,
        string? effort = null) => new(
            NotifyRecordedRole.HerdrResident,
            "workspace-a",
            pane,
            null,
            null,
            kind,
            null,
            null,
            Model: model,
            ReasoningEffort: effort);

    private static ModelResolutionLedgerEntry ScopedEntry(
        ScopedModelResolutionTarget target,
        ScopedModelResolutionRequest request,
        string informalName,
        string host = "machine",
        long processId = 123,
        string startTime = "start",
        string digest = "digest") => new()
    {
        InformalName = informalName,
        Kind = target.Kind,
        Outcome = ModelResolutionLedgerCommand.VerifiedOutcome,
        FullInvocation = "codex --model m-requested -c model_reasoning_effort=medium",
        Evidence = "READY captured",
        RecordedAt = DateTimeOffset.UnixEpoch,
        ScopeVersion = 1,
        Domain = target.Domain,
        Team = target.Team,
        Role = target.Role,
        RoleAliases = target.RoleAliases,
        WorkspaceId = target.WorkspaceId,
        PaneId = target.PaneId,
        RequestForm = request.Form,
        RequestedModel = request.RequestedModel,
        RequestedEffort = request.RequestedEffort,
        Host = host,
        ProcessId = processId,
        ProcessStartTimeUtc = startTime,
        IdentitySource = "local-process-start-time",
        TopologyDigest = digest,
        ObservedAt = DateTimeOffset.UnixEpoch,
        ObservedArgv = ["codex", "--model", "m-requested", "-c", "model_reasoning_effort=medium"],
        ObservedModel = "m-requested",
        ObservedEffort = "medium",
    };

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }
}
