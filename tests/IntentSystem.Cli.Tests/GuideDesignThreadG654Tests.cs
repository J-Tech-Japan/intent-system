using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

public sealed class GuideDesignThreadG654Tests
{
    private static readonly string[] ParentPayloadFieldNames =
    [
        "process",
        "preview_status",
        "agent_kind_neutral",
        "session_layer_rule",
        "domain",
        "team",
        "routing_root",
        "reachability",
        "wake_rule",
        "provenance",
        "approval",
        "dialog_answering_rule",
        "residual_approval",
        "merge_authority",
        "delegation_verification",
        "observation_boundary",
        "team_and_duty_split",
        "monitoring",
        "reporting",
        "negative_invariants",
    ];

    // G774 captures every parent field name and raw JSON value in rendered
    // order. Only the separately asserted packet_authoring_check block may
    // differ; changing any parent value, nesting, or field order changes this
    // oracle rather than weakening a full-payload assertion.
    // G789 is additive: these immutable hashes remain the parent values from
    // cfdacb4a657d9a60ab82fea3faa435ff732f389f after the new nested keys are
    // removed from the rendered head.
    private const string ParentPayloadOracleHash = "c43e27f362c39d9c737fc2269b1979bdf8ad9b7ecb07f3dd0491ba1325d0c54f";
    private static readonly string[] G774BaselinePayloadFieldNames =
        ParentPayloadFieldNames.Append("packet_authoring_check").ToArray();
    private const string G774BaselinePayloadOracleHash = "8110a6150605810aaa609fc2c34668341b939e58bc0dc35085c7290e6c72b136";
    // G776 may append exactly one declaration field. The existing G775
    // operating-contract fields remain a raw-value and rendered-order oracle.
    private const string G775ExternalResidenceContractOracleHash = "1c297f028c3e8ea5e1901b84ff962e542d864aa1139130ea9c1092539789cbe4";

    [Theory]
    [InlineData("agmsg", false)]
    [InlineData("agmsg", true)]
    [InlineData("herdr-only", false)]
    [InlineData("herdr-only", true)]
    public void Guide_RendersSameContract_InEverySessionLayer_WithOrWithoutTeam(string sessionLayer, bool includeTeam)
    {
        using var writer = new StringWriter();
        var args = new List<string> { "--domain", "intent-cli", "--routing-root", "/host", "--format", "json" };
        if (includeTeam) args.InsertRange(2, new[] { "--team", "intent-cli-dev" });

        var exitCode = GuideDesignThreadCommand.Execute(CreateContext(), args.ToArray(), writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.Equal("design-thread-operating-contract", root.GetProperty("process").GetString());
        Assert.Equal("preview-through-1.x", root.GetProperty("preview_status").GetString());
        Assert.True(root.GetProperty("agent_kind_neutral").GetBoolean());
        Assert.Contains(sessionLayer, root.GetProperty("session_layer_rule").GetString()!, StringComparison.Ordinal);
        Assert.Equal(includeTeam, root.TryGetProperty("team", out _));
    }

    [Fact]
    public void Json_PinsWakeProvenanceApprovalAuthorityVerificationAndReportingContracts()
    {
        using var writer = new StringWriter();
        Assert.Equal(0, GuideDesignThreadCommand.Execute(CreateContext(), ["--format", "json"], writer));
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;

        Assert.Equal(4, root.GetProperty("wake_rule").GetProperty("valid_outcomes").GetArrayLength());
        var invalid = Join(root.GetProperty("wake_rule").GetProperty("not_outcomes"));
        foreach (var value in new[] { "no-actionable", "running=true", "liveness", "unchanged", "no change" })
            Assert.Contains(value, invalid, StringComparison.Ordinal);

        Assert.Equal(
            new[] { "candidate", "accepted design", "packet", "queued unit", "published unit", "WIP" },
            root.GetProperty("provenance").GetProperty("vocabulary").EnumerateArray().Select(item => item.GetString()));
        Assert.Equal(5, root.GetProperty("provenance").GetProperty("external_origin_fields").GetArrayLength());

        Assert.Equal(
            new[] { "merge", "verify merge commit", "close linked issue", "transition queue", "append runs", "write back host state", "push host state" },
            root.GetProperty("approval").GetProperty("merge_transaction").EnumerateArray().Select(item => item.GetString()));
        Assert.Contains("once", root.GetProperty("approval").GetProperty("merge_rule").GetString()!, StringComparison.Ordinal);

        Assert.Contains("reviewDecision alone never proves", root.GetProperty("merge_authority").GetProperty("rule").GetString()!, StringComparison.Ordinal);
        Assert.Equal(3, root.GetProperty("delegation_verification").GetProperty("layers").GetArrayLength());
        Assert.Contains("G652", Join(root.GetProperty("delegation_verification").GetProperty("layers")), StringComparison.Ordinal);
        Assert.Contains("running=true", root.GetProperty("delegation_verification").GetProperty("rule").GetString()!, StringComparison.Ordinal);

        Assert.Contains("every stall class", root.GetProperty("team_and_duty_split").GetProperty("orchestration_ownership").GetString()!, StringComparison.Ordinal);
        Assert.Contains("review wedges", root.GetProperty("team_and_duty_split").GetProperty("orchestration_ownership").GetString()!, StringComparison.Ordinal);
        Assert.Equal(9, root.GetProperty("team_and_duty_split").GetProperty("design_escalations").GetArrayLength());
        Assert.Contains("greater than", root.GetProperty("monitoring").GetProperty("bound_rule").GetString()!, StringComparison.Ordinal);
        Assert.Contains("persistent AGENTS", root.GetProperty("monitoring").GetProperty("deployment_rule").GetString()!, StringComparison.Ordinal);
        Assert.Contains("minimal concrete operation", root.GetProperty("reporting").GetProperty("human_action_rule").GetString()!, StringComparison.Ordinal);

        var residual = root.GetProperty("residual_approval");
        var residualText = Join(residual.GetProperty("layers"))
            + "\n"
            + residual.GetProperty("no_policy_rule").GetString()
            + "\n"
            + residual.GetProperty("watcher_boundary").GetString();
        Assert.Contains("notify adjudicate", residualText, StringComparison.Ordinal);
        Assert.Contains("answerable_by", residualText, StringComparison.Ordinal);
        Assert.Contains("hard risk floor", residualText, StringComparison.Ordinal);
        Assert.Contains("caller-mismatched", residualText, StringComparison.Ordinal);
        Assert.DoesNotContain("design never answers", residualText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Json_PacketAuthoringCheckRemainsTheG774Baseline_G774()
    {
        using var writer = new StringWriter();
        Assert.Equal(
            0,
            GuideDesignThreadCommand.Execute(
                CreateContext(),
                ["--domain", "intent-cli", "--team", "intent-cli-dev", "--routing-root", "/g774-parent", "--format", "json"],
                writer));
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        var fields = root.EnumerateObject().ToArray();
        using var projected = JsonDocument.Parse(RemoveG789Additions(root));
        var projectedFields = projected.RootElement.EnumerateObject().ToArray();

        var g774BaselineFields = projectedFields
            .Where(field => field.Name is not "external_residence_operating_contract" and not "unreadable_repair_response")
            .ToArray();
        Assert.Equal(G774BaselinePayloadFieldNames, g774BaselineFields.Select(field => field.Name));
        var g774BaselineOracle = ComputePayloadOracle(g774BaselineFields);
        Assert.True(
            string.Equals(G774BaselinePayloadOracleHash, g774BaselineOracle, StringComparison.Ordinal),
            $"G774 baseline payload oracle changed. Expected '{G774BaselinePayloadOracleHash}', actual '{g774BaselineOracle}'.");
        var parentPayloadOracle = ComputePayloadOracle(projectedFields.Where(field => field.Name is not "packet_authoring_check" and not "external_residence_operating_contract" and not "unreadable_repair_response"));
        Assert.True(
            string.Equals(ParentPayloadOracleHash, parentPayloadOracle, StringComparison.Ordinal),
            $"G654 parent payload oracle changed. Expected '{ParentPayloadOracleHash}', actual '{parentPayloadOracle}'.");
        Console.WriteLine($"G789 guide design-thread parent remainder oracle: expected={ParentPayloadOracleHash}; actual={parentPayloadOracle}; removed=team_and_duty_split.review_seat_selection,external_residence_operating_contract.orca_operating_block");

        var check = root.GetProperty("packet_authoring_check");
        Assert.Equal(
            new[]
            {
                "before_publish",
                "per_criterion_satisfiability",
                "negative_criterion_scoping",
                "request_update_condition",
                "discriminating_pair",
                "recognition_examples",
                "g770_resolution_rule",
            },
            check.EnumerateObject().Select(field => field.Name));
        Assert.Contains("packet's own constraints", check.GetProperty("per_criterion_satisfiability").GetString()!, StringComparison.Ordinal);
        Assert.Contains("every negative criterion", check.GetProperty("negative_criterion_scoping").GetString()!, StringComparison.Ordinal);
        Assert.Contains("limiting word", check.GetProperty("negative_criterion_scoping").GetString()!, StringComparison.Ordinal);
        Assert.Contains("Request an update", check.GetProperty("request_update_condition").GetString()!, StringComparison.Ordinal);
        Assert.Contains("named discriminating pair", check.GetProperty("discriminating_pair").GetString()!, StringComparison.Ordinal);

        var examples = Join(check.GetProperty("recognition_examples"));
        Assert.Contains("G765 AC4/AC6", examples, StringComparison.Ordinal);
        Assert.Contains("G767 AC1/AC6", examples, StringComparison.Ordinal);
        Assert.Contains("G769 AC3/AC4", examples, StringComparison.Ordinal);
        Assert.Contains("root resolution", check.GetProperty("g770_resolution_rule").GetString()!, StringComparison.Ordinal);
    }

    [Fact]
    public void Json_ExternalResidenceOperatingContractIsTheOnlyAdditivePayloadDelta_G775()
    {
        using var writer = new StringWriter();
        Assert.Equal(
            0,
            GuideDesignThreadCommand.Execute(
                CreateContext(),
                ["--domain", "intent-cli", "--team", "intent-cli-dev", "--routing-root", "/g774-parent", "--format", "json"],
                writer));
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        var fields = root.EnumerateObject().ToArray();
        using var projected = JsonDocument.Parse(RemoveG789Additions(root));
        var projectedFields = projected.RootElement.EnumerateObject().ToArray();

        var g775BaselineFields = projectedFields
            .Where(field => field.Name != "unreadable_repair_response")
            .ToArray();
        Assert.Equal(
            G774BaselinePayloadFieldNames.Append("external_residence_operating_contract"),
            g775BaselineFields.Select(field => field.Name));
        var g774BaselineOracle = ComputePayloadOracle(g775BaselineFields.Where(field => field.Name != "external_residence_operating_contract"));
        Assert.True(
            string.Equals(G774BaselinePayloadOracleHash, g774BaselineOracle, StringComparison.Ordinal),
            $"G774 baseline payload oracle changed. Expected '{G774BaselinePayloadOracleHash}', actual '{g774BaselineOracle}'.");

        var contract = root.GetProperty("external_residence_operating_contract");
        Assert.Equal(
            new[]
            {
                "frontend_relabel",
                "routing_root_must",
                "collect_loop",
                "wake_channel_pattern",
                "wake_channel_declaration",
                "orca_worked_example",
                "orca_operating_block",
                "residence_transition",
            },
            contract.EnumerateObject().Select(field => field.Name));

        var projectedContract = projected.RootElement.GetProperty("external_residence_operating_contract");
        var g775Fields = projectedContract
            .EnumerateObject()
            .Where(field => field.Name != "wake_channel_declaration")
            .ToArray();
        Assert.Equal(
            new[]
            {
                "frontend_relabel",
                "routing_root_must",
                "collect_loop",
                "wake_channel_pattern",
                "orca_worked_example",
                "residence_transition",
            },
            g775Fields.Select(field => field.Name));
        var g775Oracle = ComputePayloadOracle(g775Fields);
        Assert.True(
            string.Equals(G775ExternalResidenceContractOracleHash, g775Oracle, StringComparison.Ordinal),
            $"G775 operating-contract oracle changed. Expected '{G775ExternalResidenceContractOracleHash}', actual '{g775Oracle}'.");
        Console.WriteLine($"G789 guide design-thread G775 remainder oracle: expected={G775ExternalResidenceContractOracleHash}; actual={g775Oracle}; removed=external_residence_operating_contract.orca_operating_block");

        var frontendRelabel = contract.GetProperty("frontend_relabel").GetString()!;
        Assert.StartsWith("External-to-external frontend relabel:", frontendRelabel);
        Assert.Contains("residence, reader, and routing root stay unchanged", frontendRelabel, StringComparison.Ordinal);
        Assert.Contains("no transition command is involved", frontendRelabel, StringComparison.Ordinal);
        Assert.Contains("do not use `session-layer topology update-residence`", frontendRelabel, StringComparison.Ordinal);
        Assert.Contains("frontend is an operator label, never a routing input", frontendRelabel, StringComparison.Ordinal);
        Assert.DoesNotContain("session-layer topology record", frontendRelabel, StringComparison.Ordinal);
        Assert.Contains("session-layer topology update-field", frontendRelabel, StringComparison.Ordinal);
        Assert.Contains("--field frontend", frontendRelabel, StringComparison.Ordinal);

        var routingRootMust = contract.GetProperty("routing_root_must").GetString()!;
        Assert.Contains("MUST", routingRootMust, StringComparison.Ordinal);
        Assert.Contains("strands notify records", routingRootMust, StringComparison.Ordinal);
        Assert.Contains("delivered: true", routingRootMust, StringComparison.Ordinal);

        var collectLoop = contract.GetProperty("collect_loop").GetString()!;
        Assert.Contains("--role design", collectLoop, StringComparison.Ordinal);
        Assert.Contains("--wait --timeout-ms", collectLoop, StringComparison.Ordinal);
        Assert.Contains("--routing-root /g774-parent", collectLoop, StringComparison.Ordinal);
        Assert.Contains("caller holds the cursor", collectLoop, StringComparison.Ordinal);

        var wakeChannel = contract.GetProperty("wake_channel_pattern").GetString()!;
        Assert.Contains("courtesy-only", wakeChannel, StringComparison.Ordinal);
        Assert.Contains("dual-send", wakeChannel, StringComparison.Ordinal);
        Assert.Contains("durable wake addresses", wakeChannel, StringComparison.Ordinal);

        var wakeDeclaration = contract.GetProperty("wake_channel_declaration").GetString()!;
        Assert.Contains("--wake-command", wakeDeclaration, StringComparison.Ordinal);
        Assert.Contains("{task_id}", wakeDeclaration, StringComparison.Ordinal);
        Assert.Contains("{summary}", wakeDeclaration, StringComparison.Ordinal);
        Assert.Contains("session-layer topology update-field", wakeDeclaration, StringComparison.Ordinal);
        Assert.Contains("--field wake_command", wakeDeclaration, StringComparison.Ordinal);
        Assert.Contains("unknown placeholders", wakeDeclaration, StringComparison.Ordinal);
        Assert.Contains("never executes", wakeDeclaration, StringComparison.Ordinal);

        var orcaExample = contract.GetProperty("orca_worked_example").GetString()!;
        Assert.Contains("Non-normative Orca example", orcaExample, StringComparison.Ordinal);
        Assert.Contains("orca orchestration run-use --id <run-id>", orcaExample, StringComparison.Ordinal);
        Assert.Contains("orca orchestration check --run <run-id> --wait --timeout-ms <timeout-ms> --json", orcaExample, StringComparison.Ordinal);
        Assert.Contains("intent-cli neither launches nor manages Orca", orcaExample, StringComparison.Ordinal);

        var residenceTransition = contract.GetProperty("residence_transition").GetString()!;
        Assert.Contains("different operation", residenceTransition, StringComparison.Ordinal);
        Assert.Contains("herdr↔external", residenceTransition, StringComparison.Ordinal);
        Assert.Contains("session-layer topology update-residence", residenceTransition, StringComparison.Ordinal);

        using var unknownArgumentWriter = new StringWriter();
        Assert.Equal(1, GuideDesignThreadCommand.Execute(CreateContext(), ["--frontend", "orca"], unknownArgumentWriter));
        Assert.Contains("Unknown argument '--frontend'.", unknownArgumentWriter.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Markdown_RendersPacketAuthoringCheckWithAllRecognitionExamples_G774()
    {
        using var writer = new StringWriter();
        Assert.Equal(0, GuideDesignThreadCommand.Execute(CreateContext(), [], writer));
        var output = writer.ToString();

        Assert.Contains("## 8. Packet-authoring self-check (G774)", output, StringComparison.Ordinal);
        Assert.Contains("per-criterion satisfiability", output, StringComparison.Ordinal);
        Assert.Contains("limiting word", output, StringComparison.Ordinal);
        Assert.Contains("Request an update", output, StringComparison.Ordinal);
        Assert.Contains("named discriminating pair", output, StringComparison.Ordinal);
        foreach (var recognitionExample in new[] { "G765 AC4/AC6", "G767 AC1/AC6", "G769 AC3/AC4", "root resolution" })
            Assert.Contains(recognitionExample, output, StringComparison.Ordinal);
    }

    [Fact]
    public void Markdown_RendersExternalResidenceOperatingContractInIncidentOrder_G775()
    {
        using var writer = new StringWriter();
        Assert.Equal(0, GuideDesignThreadCommand.Execute(CreateContext(), ["--domain", "intent-cli", "--team", "intent-cli-dev", "--routing-root", "/g775-routing-root"], writer));
        var output = writer.ToString();
        var sectionStart = output.IndexOf("## 9. External-residence operating contract (G775)", StringComparison.Ordinal);
        var sectionEnd = output.IndexOf("## Negative invariants", sectionStart, StringComparison.Ordinal);
        Assert.True(sectionStart >= 0 && sectionEnd > sectionStart, output);
        var section = output[sectionStart..sectionEnd];

        Assert.StartsWith(
            "## 9. External-residence operating contract (G775)\n- **frontend relabel first:** External-to-external frontend relabel:",
            section);
        Assert.Contains("residence, reader, and routing root stay unchanged", section, StringComparison.Ordinal);
        Assert.Contains("no transition command is involved", section, StringComparison.Ordinal);
        Assert.Contains("do not use `session-layer topology update-residence`", section, StringComparison.Ordinal);
        Assert.Contains("frontend is an operator label, never a routing input", section, StringComparison.Ordinal);
        Assert.Contains("Routing-root MUST", section, StringComparison.Ordinal);
        Assert.Contains("delivered: true", section, StringComparison.Ordinal);
        Assert.Contains("--role design", section, StringComparison.Ordinal);
        Assert.Contains("--wait --timeout-ms", section, StringComparison.Ordinal);
        Assert.Contains("--routing-root /g775-routing-root", section, StringComparison.Ordinal);
        Assert.Contains("courtesy-only", section, StringComparison.Ordinal);
        Assert.Contains("dual-send", section, StringComparison.Ordinal);
        Assert.Contains("--wake-command", section, StringComparison.Ordinal);
        Assert.Contains("never executes", section, StringComparison.Ordinal);
        Assert.Contains("Non-normative Orca example", section, StringComparison.Ordinal);
        Assert.Contains("intent-cli neither launches nor manages Orca", section, StringComparison.Ordinal);
        Assert.Contains("A herdr↔external residence change is a different operation", section, StringComparison.Ordinal);
    }

    [Fact]
    public void BootstrapExternalBranchNamesDistinctUpdateResidenceRoute_G775()
    {
        using var writer = new StringWriter();
        Assert.Equal(
            0,
            GuideBootstrapCommand.Execute(
                CreateContext(),
                ["--domain", "intent-cli", "--team", "intent-cli-dev", "--target-repo", "example/repo", "--routing-root", "/g775-routing-root", "--format", "markdown"],
                writer));
        var output = writer.ToString();
        var stepStart = output.IndexOf("### 5. ask-app-kind-and-place-design", StringComparison.Ordinal);
        var stepEnd = output.IndexOf("### 6. delegate-first-task", stepStart, StringComparison.Ordinal);
        Assert.True(stepStart >= 0 && stepEnd > stepStart, output);
        var externalBranch = output[stepStart..stepEnd];

        var relabelIndex = externalBranch.IndexOf("external-to-external frontend relabel", StringComparison.Ordinal);
        var transitionIndex = externalBranch.IndexOf("session-layer topology update-residence", StringComparison.Ordinal);
        Assert.True(relabelIndex >= 0 && transitionIndex > relabelIndex, externalBranch);
        Assert.Contains("different operation", externalBranch, StringComparison.Ordinal);
        Assert.Contains("--current-resident <herdr|external>", externalBranch, StringComparison.Ordinal);
    }

    [Fact]
    public void Markdown_IsAgentKindNeutral_AndContainsNoNormativeProviderNames()
    {
        using var writer = new StringWriter();
        Assert.Equal(0, GuideDesignThreadCommand.Execute(CreateContext(), ["--team", "intent-cli-dev"], writer));
        var output = writer.ToString();

        Assert.Contains("Four-outcome wake rule", output, StringComparison.Ordinal);
        Assert.Contains("four judgment-bearing threads plus one supervision process", output, StringComparison.Ordinal);
        Assert.Contains("at most once per design wake", output, StringComparison.Ordinal);
        Assert.DoesNotContain("codex", output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Claude app safety", output, StringComparison.Ordinal);
        Assert.DoesNotContain("copilot", output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CatalogHelpAndNext_AllReachTheGuide()
    {
        Assert.Contains(GuideCommandsListCommand.Groups, entry => entry.Name == "guide design-thread" && entry.Role == "design");
        Assert.Contains(GuideHelpCommand.Subcommands, entry => entry.Name == "design-thread");

        using var writer = new StringWriter();
        Assert.Equal(0, GuideNextCommand.Execute(CreateContext(), ["--format", "json"], writer));
        using var document = JsonDocument.Parse(writer.ToString());
        Assert.Equal(GuideDesignThreadCommand.CommandName, document.RootElement.GetProperty("design_role_guide").GetString());
    }

    [Fact]
    public void EnglishAndJapaneseDocs_MirrorGuideContractAndPreviewLedger()
    {
        var en = ReadRepoFile("docs/en/12-agent-message-orchestration.md");
        var ja = ReadRepoFile("docs/ja/12-agent-message-orchestration.md");
        var enLedger = ReadRepoFile("docs/en/1.0-compatibility-ledger.md");
        var jaLedger = ReadRepoFile("docs/ja/1.0-compatibility-ledger.md");

        foreach (var doc in new[] { en, ja })
        {
            Assert.Contains("intent-cli guide design-thread", doc, StringComparison.Ordinal);
            Assert.Contains("no-actionable", doc, StringComparison.Ordinal);
            Assert.Contains("reviewDecision", doc, StringComparison.Ordinal);
            Assert.Contains("G652", doc, StringComparison.Ordinal);
            Assert.Contains("running=true", doc, StringComparison.Ordinal);
            Assert.Contains("AGENTS", doc, StringComparison.Ordinal);
        }
        foreach (var ledger in new[] { enLedger, jaLedger })
        {
            Assert.Contains("| `guide design-thread` |", ledger, StringComparison.Ordinal);
            Assert.Contains("| `notify adjudicate` |", ledger, StringComparison.Ordinal);
            Assert.Contains("exit_code", ledger, StringComparison.Ordinal);
            Assert.Contains("preview-through-1.x", ledger, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void EnglishAndJapaneseDocs_SemanticallyMirrorPacketAuthoringCheck_G774()
    {
        var en = ReadRepoFile("docs/en/12-agent-message-orchestration.md");
        var ja = ReadRepoFile("docs/ja/12-agent-message-orchestration.md");

        foreach (var recognitionExample in new[] { "G765 AC4/AC6", "G767 AC1/AC6", "G769 AC3/AC4", "root resolution" })
        {
            Assert.Contains(recognitionExample, en, StringComparison.Ordinal);
            Assert.Contains(recognitionExample, ja, StringComparison.Ordinal);
        }

        Assert.Contains("per-criterion satisfiability", en, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("limiting word", en, StringComparison.Ordinal);
        Assert.Contains("request an update", en, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("discriminating pair", en, StringComparison.Ordinal);

        Assert.Contains("criterion ごとの充足可能性", ja, StringComparison.Ordinal);
        Assert.Contains("限定語", ja, StringComparison.Ordinal);
        Assert.Contains("更新を要求", ja, StringComparison.Ordinal);
        Assert.Contains("判別対", ja, StringComparison.Ordinal);
    }

    [Fact]
    public void EnglishAndJapaneseDocs_SemanticallyMirrorExternalResidenceOperatingContract_G775()
    {
        var en = ReadRepoFile("docs/en/12-agent-message-orchestration.md");
        var ja = ReadRepoFile("docs/ja/12-agent-message-orchestration.md");
        var enSection = Section(en, "### External-residence operating contract (G775)", "### Role-contract precedence");
        var jaSection = Section(ja, "### external residence の運用契約（G775）", "### role contract の precedence");

        Assert.StartsWith("### External-residence operating contract (G775)\n\n**Frontend relabel first.", enSection);
        Assert.Contains("residence, reader, and routing root are unchanged", enSection, StringComparison.Ordinal);
        Assert.Contains("No transition command is involved", enSection, StringComparison.Ordinal);
        Assert.Contains("delivered: true", enSection, StringComparison.Ordinal);
        Assert.Contains("--wait --timeout-ms", enSection, StringComparison.Ordinal);
        Assert.Contains("courtesy-only", enSection, StringComparison.Ordinal);
        Assert.Contains("dual-send", enSection, StringComparison.Ordinal);
        Assert.Contains("Non-normative Orca worked example", enSection, StringComparison.Ordinal);
        Assert.Contains("intent-cli neither launches nor manages Orca", enSection, StringComparison.Ordinal);
        Assert.Contains("different operation", enSection, StringComparison.Ordinal);
        Assert.Contains("guide bootstrap", enSection, StringComparison.Ordinal);
        Assert.True(
            enSection.IndexOf("A herdr↔external residence transition", StringComparison.Ordinal)
            < enSection.IndexOf("**Bind the wake address.**", StringComparison.Ordinal));
        Assert.True(
            enSection.IndexOf("**Bind the wake address.**", StringComparison.Ordinal)
            < enSection.IndexOf("**Collect after binding.**", StringComparison.Ordinal));
        Assert.True(
            enSection.IndexOf("**Collect after binding.**", StringComparison.Ordinal)
            < enSection.IndexOf("**Dual-send after the loop is established.**", StringComparison.Ordinal));

        Assert.StartsWith("### external residence の運用契約（G775）\n\n**最初にフロントエンドの表示名変更。", jaSection);
        Assert.Contains("residence、reader、routing root は変わりません", jaSection, StringComparison.Ordinal);
        Assert.Contains("transition command は関与しません", jaSection, StringComparison.Ordinal);
        Assert.Contains("delivered: true", jaSection, StringComparison.Ordinal);
        Assert.Contains("--wait --timeout-ms", jaSection, StringComparison.Ordinal);
        Assert.Contains("courtesy-only", jaSection, StringComparison.Ordinal);
        Assert.Contains("dual-send", jaSection, StringComparison.Ordinal);
        Assert.Contains("非規範的な Orca の例", jaSection, StringComparison.Ordinal);
        Assert.Contains("intent-cli は Orca を起動も管理もしません", jaSection, StringComparison.Ordinal);
        Assert.Contains("別の操作", jaSection, StringComparison.Ordinal);
        Assert.Contains("guide bootstrap", jaSection, StringComparison.Ordinal);
        Assert.True(
            jaSection.IndexOf("herdr↔external の residence transition", StringComparison.Ordinal)
            < jaSection.IndexOf("**wake address を接続。**", StringComparison.Ordinal));
        Assert.True(
            jaSection.IndexOf("**wake address を接続。**", StringComparison.Ordinal)
            < jaSection.IndexOf("**接続後に collect。**", StringComparison.Ordinal));
        Assert.True(
            jaSection.IndexOf("**接続後に collect。**", StringComparison.Ordinal)
            < jaSection.IndexOf("**loop を確立してから dual-send。**", StringComparison.Ordinal));
    }

    private static string Join(JsonElement array) => string.Join('\n', array.EnumerateArray().Select(item => item.GetString()));

    private static string Section(string document, string start, string end)
    {
        var startIndex = document.IndexOf(start, StringComparison.Ordinal);
        Assert.True(startIndex >= 0, $"Missing section '{start}'.");
        var endIndex = document.IndexOf(end, startIndex, StringComparison.Ordinal);
        Assert.True(endIndex > startIndex, $"Missing section terminator '{end}'.");
        return document[startIndex..endIndex];
    }

    private static string ComputePayloadOracle(IEnumerable<JsonProperty> fields)
    {
        var payload = string.Join(
            "\u001E",
            fields.Select(field => field.Name + "\u001F" + field.Value.GetRawText()));
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
    }

    private static string RemoveG789Additions(JsonElement root)
    {
        var projected = JsonNode.Parse(root.GetRawText())!.AsObject();
        projected["team_and_duty_split"]?.AsObject().Remove("review_seat_selection");
        projected["external_residence_operating_contract"]?.AsObject().Remove("orca_operating_block");
        projected["observation_boundary"]?.AsObject().Remove("inspect_route");
        return projected.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    private static CliContext CreateContext() => new()
    {
        RepoRoot = Path.GetTempPath(),
        Config = new CliConfig { Project = new ProjectConfig { Domain = "intent-cli", ArtifactRoot = ".intent-cli", WorktreeRoot = ".intent-cli/worktrees" } },
    };

    private static string ReadRepoFile(string relativePath)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, relativePath);
            if (File.Exists(candidate)) return File.ReadAllText(candidate);
            current = current.Parent;
        }
        throw new FileNotFoundException(relativePath);
    }
}
