using System.Text.Json;
using System.Text.RegularExpressions;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G797: the four named guide routes render one canonical human-facing role
/// vocabulary while retaining their installed route names and payload shape.
/// </summary>
public sealed class GuideRoleNamesG797Tests
{
    private static readonly string[] CanonicalNames =
    [
        GuideRoleVocabulary.Architect,
        GuideRoleVocabulary.Orchestrator,
        GuideRoleVocabulary.Builder,
        GuideRoleVocabulary.Reviewer,
        GuideRoleVocabulary.Steward,
    ];

    [Fact]
    public void FourNamedSurfaces_RenderCanonicalFiveAndStewardBoundary_G797()
    {
        var context = CreateContext();
        var surfaces = new (string Name, Func<TextWriter, int> Render)[]
        {
            ("guide design-thread", writer => GuideDesignThreadCommand.Execute(context, ["--format", "markdown"], writer)),
            ("guide orchestrator-thread", writer => GuideOrchestratorThreadCommand.Execute(context, ["--format", "markdown"], writer)),
            ("guide workflow task implementation-loop", writer => GuideWorkflowTaskImplementationLoopCommand.Execute(context, ["--format", "markdown"], writer)),
            ("guide workflow task review-next-slice-loop", writer => GuideWorkflowTaskReviewNextSliceLoopCommand.Execute(context, ["--format", "markdown"], writer)),
        };

        foreach (var (name, render) in surfaces)
        {
            using var writer = new StringWriter();
            Assert.Equal(0, render(writer));
            var output = writer.ToString();
            Assert.Contains("Canonical role vocabulary (G797)", output, StringComparison.Ordinal);
            foreach (var canonicalName in CanonicalNames)
            {
                Assert.Contains(canonicalName, output, StringComparison.Ordinal);
            }

            Assert.Contains(GuideRoleVocabulary.StewardBoundarySentence, output, StringComparison.Ordinal);
            Assert.DoesNotContain("--role orchestrator", output, StringComparison.Ordinal);
            Console.WriteLine($"G797 AC1/AC4 {name}: canonical_names={string.Join(",", CanonicalNames)}; steward_boundary=present");
        }
    }

    [Fact]
    public void ExistingRouteNames_StillResolveWithoutWarnings_G797()
    {
        var context = CreateContext();
        var routes = new (string Name, Func<TextWriter, int> Render)[]
        {
            ("guide design-thread", writer => GuideDesignThreadCommand.Execute(context, ["--format", "json"], writer)),
            ("guide orchestrator-thread", writer => GuideOrchestratorThreadCommand.Execute(context, ["--format", "json"], writer)),
            ("guide workflow task implementation-loop", writer => GuideWorkflowTaskImplementationLoopCommand.Execute(context, ["--format", "json"], writer)),
            ("guide workflow task review-next-slice-loop", writer => GuideWorkflowTaskReviewNextSliceLoopCommand.Execute(context, ["--format", "json"], writer)),
        };

        foreach (var (name, render) in routes)
        {
            using var writer = new StringWriter();
            var exitCode = render(writer);
            Assert.Equal(0, exitCode);
            Assert.NotEmpty(writer.ToString());
            Assert.DoesNotContain("Unknown argument", writer.ToString(), StringComparison.Ordinal);
            Assert.DoesNotContain("Refusing to render", writer.ToString(), StringComparison.Ordinal);
            Console.WriteLine($"G797 AC2 route={name}; exit_code={exitCode}; warning=false");
        }
    }

    [Fact]
    public void OrchestratorGuide_UsesRouteCompatibleOrchestrationIdentifier_G797()
    {
        using var writer = new StringWriter();
        Assert.Equal(
            0,
            GuideOrchestratorThreadCommand.Execute(
                CreateContext(),
                ["--format", "markdown"],
                writer));

        var output = writer.ToString();
        Assert.Contains("guide orchestrator-thread", output, StringComparison.Ordinal);
        Assert.Contains("working role identifier is `orchestration`", output, StringComparison.Ordinal);
        Assert.Contains("Use that identifier in role-bearing commands", output, StringComparison.Ordinal);
        Assert.DoesNotContain("--role orchestrator", output, StringComparison.Ordinal);
        Console.WriteLine("G797 AC3 orchestrator_route=guide orchestrator-thread; role_identifier=orchestration; old_role_flag_present=false");
    }

    [Fact]
    public void GuideRoleIdentifier_UsesTheG795NormalizerProjection_G797()
    {
        Assert.Equal("design", GuideRoleVocabulary.Identifier(LogicalRoleNormalizer.Architect));
        Assert.Equal("orchestration", GuideRoleVocabulary.Identifier(LogicalRoleNormalizer.Orchestrator));
        Assert.Equal("implementation", GuideRoleVocabulary.Identifier(LogicalRoleNormalizer.Builder));
        Assert.Equal("review", GuideRoleVocabulary.Identifier(LogicalRoleNormalizer.Reviewer));
        Assert.Equal("steward", GuideRoleVocabulary.Identifier(LogicalRoleNormalizer.Steward));
        Console.WriteLine("G797 AC3 G795_identifier_projection: architect=design; orchestrator=orchestration; builder=implementation; reviewer=review; steward=steward");
    }

    [Fact]
    public void GuideText_DoesNotMakeRoleRequireAProviderRuntime_G797()
    {
        var context = CreateContext();
        var surfaces = new (string Name, Func<TextWriter, int> Render)[]
        {
            ("guide design-thread", writer => GuideDesignThreadCommand.Execute(context, ["--format", "markdown"], writer)),
            ("guide orchestrator-thread", writer => GuideOrchestratorThreadCommand.Execute(context, ["--format", "markdown"], writer)),
            ("guide workflow task implementation-loop", writer => GuideWorkflowTaskImplementationLoopCommand.Execute(context, ["--format", "markdown"], writer)),
            ("guide workflow task review-next-slice-loop", writer => GuideWorkflowTaskReviewNextSliceLoopCommand.Execute(context, ["--format", "markdown"], writer)),
        };
        var requiringRuntime = new Regex(
            @"(?ix)\b(?:Architect|Orchestrator|Builder|Reviewer|Steward)\b[^.!?\r\n]{0,80}\b(?:requires?|must\s+(?:use|run|be)|only\s+works?\s+with)\b[^.!?\r\n]{0,80}\b(?:Claude|Codex)\b",
            RegexOptions.Compiled);

        foreach (var (name, render) in surfaces)
        {
            using var writer = new StringWriter();
            Assert.Equal(0, render(writer));
            var output = writer.ToString();
            var matches = requiringRuntime.Matches(output).Select(match => match.Value).ToArray();
            Assert.Empty(matches);
            Console.WriteLine($"G797 AC6 vendor-adjacency-search {name}: pattern={requiringRuntime}; matches={matches.Length}; lines={(matches.Length == 0 ? "<none>" : string.Join(" || ", matches))}");
        }
    }

    [Fact]
    public void ParentPayloadKeysRemainPresentAcrossTheFourSurfaces_G797()
    {
        var context = CreateContext();
        var outputs = new (string Name, Func<TextWriter, int> Render)[]
        {
            ("design-thread", writer => GuideDesignThreadCommand.Execute(context, ["--format", "json"], writer)),
            ("orchestrator-thread", writer => GuideOrchestratorThreadCommand.Execute(context, ["--format", "json"], writer)),
            ("implementation-loop", writer => GuideWorkflowTaskImplementationLoopCommand.Execute(context, ["--format", "json"], writer)),
            ("review-next-slice-loop", writer => GuideWorkflowTaskReviewNextSliceLoopCommand.Execute(context, ["--format", "json"], writer)),
        };

        // These are the immutable parent key sets used by the G797 packet.
        // The assertion is intentionally a subset check: this slice changes
        // role-bearing values only and may not drop an established key.
        var parentKeys = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["design-thread"] =
            [
                "process", "preview_status", "agent_kind_neutral", "session_layer_rule", "routing_root",
                "reachability", "wake_rule", "provenance", "approval", "dialog_answering_rule",
                "residual_approval", "merge_authority", "delegation_verification", "observation_boundary",
                "team_and_duty_split", "monitoring", "reporting", "negative_invariants", "packet_authoring_check",
                "external_residence_operating_contract", "unreadable_repair_response",
            ],
            ["orchestrator-thread"] =
            [
                "host_state_discovery", "session_layer_switch_checklist", "setup_intake", "guide_reachability",
                "topology_workspace_move", "herdr_standard_layout", "dialog_answering_rule", "closeout_runs_contract",
                "summary", "mode_separation", "role_boundary", "claim_routing", "domain_routing", "scheduling",
                "ci_wait_state", "draft_pr_reviewability", "next_slice_publication", "end_of_wake_check",
                "dispatch_verification", "dependency_planning", "stale_thread_health_check", "design_thread_escalation",
                "design_receiver", "design_handoff", "design_watchdog", "orchestrator_automation_alternative",
                "monitor_recovery", "monitor_tool_distinction", "codex_bridge_guidance", "intake_form",
                "terminal_workspace_provisioning", "design_workspace_supervision", "design_decision_holds",
                "cross_project_isolation", "design_traffic_controller", "pre_delegation_prerequisites", "worktree_management",
                "review_delegation_contract", "setup", "preflight", "troubleshooting", "receiver_readiness",
                "threads", "agmsg_reply_contract", "orchestrator_first_wake", "safety_boundaries", "detailed_guide_commands",
            ],
            ["implementation-loop"] = ["mode", "kind", "target", "frequency_guidance", "forbidden_sources", "first_calls", "prompt", "agent", "base_branch_policy", "expected_base_branch"],
            ["review-next-slice-loop"] = ["mode", "kind", "target", "frequency_guidance", "forbidden_sources", "first_calls", "prompt", "agent", "base_branch_policy", "expected_base_branch"],
        };

        foreach (var (name, render) in outputs)
        {
            using var writer = new StringWriter();
            Assert.Equal(0, render(writer));
            using var document = JsonDocument.Parse(writer.ToString());
            var actual = document.RootElement.ValueKind == JsonValueKind.Array
                ? document.RootElement[0]
                : document.RootElement;
            var actualNames = actual.EnumerateObject().Select(property => property.Name).ToHashSet(StringComparer.Ordinal);
            foreach (var key in parentKeys[name])
            {
                Assert.Contains(key, actualNames);
            }

            Console.WriteLine($"G797 AC7 parent_key_set {name}: required={string.Join(",", parentKeys[name])}; present={parentKeys[name].All(actualNames.Contains)}");
        }
    }

    [Fact]
    public void RetiredNameGlossary_MapsEveryHistoricalKey_G797()
    {
        var path = Path.Combine(RepoVersionPolicySource.RepoRoot(), "docs", "role-name-glossary.md");
        Assert.True(File.Exists(path), $"Missing glossary: {path}");
        var glossary = File.ReadAllText(path);
        foreach (var (retired, canonical) in new[]
        {
            ("design", "architect"),
            ("orchestration", "orchestrator"),
            ("implementation", "builder"),
            ("review", "reviewer"),
        })
        {
            Assert.Contains($"| `{retired}` | `{canonical}` |", glossary, StringComparison.Ordinal);
        }

        Assert.Contains("2026-09-03", glossary, StringComparison.Ordinal);
        Assert.Contains("archived artifacts", glossary, StringComparison.Ordinal);
        Console.WriteLine("G797 AC5 retired_glossary: design→architect; orchestration→orchestrator; implementation→builder; review→reviewer; stopped=2026-09-03; archived_artifacts=stated");
    }

    private static CliContext CreateContext() => new()
    {
        RepoRoot = Path.GetTempPath(),
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
}
