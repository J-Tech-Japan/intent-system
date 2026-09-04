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
    private static readonly string[] RetiredAliases =
    [
        "design",
        "orchestration",
        "implementation",
        "review",
    ];

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
            AssertRoleBearingValuesAreCanonical(output, name);
            var expectedRoleCommand = name switch
            {
                "guide design-thread" => "--role architect",
                "guide orchestrator-thread" => "--from architect --to orchestrator",
                "guide workflow task implementation-loop" => "--from builder --to orchestrator",
                // The host-loop prompt is intentionally role-neutral in its
                // transport examples; the canonical vocabulary block and
                // projection check above still cover its role-bearing values.
                "guide workflow task review-next-slice-loop" => null,
                _ => throw new InvalidOperationException($"Unexpected guide surface: {name}"),
            };
            if (expectedRoleCommand is not null)
            {
                Assert.Contains(expectedRoleCommand, output, StringComparison.Ordinal);
            }
            foreach (var retiredAlias in RetiredAliases)
            {
                Assert.DoesNotContain($"role identifier is `{retiredAlias}`", output, StringComparison.Ordinal);
            }
            Console.WriteLine($"G797 AC1/AC4 {name}: canonical_names={string.Join(",", CanonicalNames)}; steward_boundary=present");
        }
    }

    [Fact]
    public void ExistingRouteNames_StillResolveWithoutWarnings_G797()
    {
        var context = CreateContext();
        var routes = new (string Name, string[] Args)[]
        {
            ("guide design-thread", ["guide", "design-thread", "--format", "json"]),
            ("guide orchestrator-thread", ["guide", "orchestrator-thread", "--format", "json"]),
            ("guide workflow task implementation-loop", ["guide", "workflow", "task", "implementation-loop", "--format", "json"]),
            ("guide workflow task review-next-slice-loop", ["guide", "workflow", "task", "review-next-slice-loop", "--format", "json"]),
        };

        foreach (var (name, args) in routes)
        {
            using var writer = new StringWriter();
            var exitCode = CommandRouter.Execute(args, context, writer);
            Assert.Equal(0, exitCode);
            Assert.NotEmpty(writer.ToString());
            Assert.DoesNotContain("Unknown argument", writer.ToString(), StringComparison.Ordinal);
            Assert.DoesNotContain("Refusing to render", writer.ToString(), StringComparison.Ordinal);
            Assert.DoesNotContain("\nWarning:", writer.ToString(), StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("\nwarning:", writer.ToString(), StringComparison.OrdinalIgnoreCase);
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
        Assert.Contains("working role identifier is `orchestrator`", output, StringComparison.Ordinal);
        Assert.Contains("Use that identifier in role-bearing commands", output, StringComparison.Ordinal);
        Assert.Contains("retired `orchestration` recording spelling belongs only in the glossary", output, StringComparison.Ordinal);
        Assert.DoesNotContain("working role identifier is `orchestration`", output, StringComparison.Ordinal);
        Console.WriteLine("G797 AC3 orchestrator_route=guide orchestrator-thread; role_identifier=orchestrator; retired_alias=orchestration; projection_direction=canonical");
    }

    [Fact]
    public void GuideRoleIdentifier_UsesTheG795NormalizerProjection_G797()
    {
        Assert.Equal(LogicalRoleNormalizer.Architect, GuideRoleVocabulary.Identifier(LogicalRoleNormalizer.Architect));
        Assert.Equal(LogicalRoleNormalizer.Orchestrator, GuideRoleVocabulary.Identifier(LogicalRoleNormalizer.Orchestrator));
        Assert.Equal(LogicalRoleNormalizer.Builder, GuideRoleVocabulary.Identifier(LogicalRoleNormalizer.Builder));
        Assert.Equal(LogicalRoleNormalizer.Reviewer, GuideRoleVocabulary.Identifier(LogicalRoleNormalizer.Reviewer));
        Assert.Equal(LogicalRoleNormalizer.Steward, GuideRoleVocabulary.Identifier(LogicalRoleNormalizer.Steward));

        foreach (var (alias, canonical) in LogicalRoleNormalizer.Aliases)
        {
            Assert.Equal(canonical, GuideRoleVocabulary.Identifier(alias));
            Assert.Equal(canonical, GuideRoleContractGuidance.Normalize(alias));
        }

        Assert.Equal(LogicalRoleNormalizer.Orchestrator, GuideRoleContractGuidance.Normalize(LogicalRoleNormalizer.Orchestrator));
        Console.WriteLine("G797 AC3 G795_identifier_projection: architect=architect; orchestrator=orchestrator; builder=builder; reviewer=reviewer; steward=steward; aliases=accepted-input-only");
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
        var rolePattern = string.Join("|", LogicalRoleNormalizer.CanonicalRoles
            .Concat(LogicalRoleNormalizer.Aliases.Keys)
            .Select(Regex.Escape));
        var vendorHeading = new Regex(@"(?im)^#{2,6}[^\r\n]*(?:Claude|Codex|opencode)[^\r\n]*$", RegexOptions.Compiled);
        var vendorBeforeRole = new Regex($@"(?i)(?:Claude|Codex|opencode)[^\r\n]*(?:{rolePattern})", RegexOptions.Compiled);
        var roleBeforeVendor = new Regex($@"(?i)(?:{rolePattern})[^\r\n]*(?:Claude|Codex|opencode)", RegexOptions.Compiled);
        var directVendorRoleAssignment = new Regex($@"(?i)\b(?:{rolePattern})\s*[=:]\s*(?:Claude|Codex|opencode)\b|\b(?:Claude|Codex|opencode)\s*[=:]\s*(?:{rolePattern})\b", RegexOptions.Compiled);

        foreach (var (name, render) in surfaces)
        {
            using var writer = new StringWriter();
            Assert.Equal(0, render(writer));
            var output = writer.ToString();
            var headings = vendorHeading.Matches(output).Select(match => match.Value).ToArray();
            var vendorBeforeRoleMatches = headings
                .Where(heading => vendorBeforeRole.IsMatch(heading))
                .ToArray();
            var roleBeforeVendorMatches = headings
                .Where(heading => roleBeforeVendor.IsMatch(heading))
                .ToArray();
            Assert.Empty(vendorBeforeRoleMatches);
            Assert.Empty(roleBeforeVendorMatches);
            var matches = vendorBeforeRoleMatches
                .Concat(roleBeforeVendorMatches)
                .Distinct(StringComparer.Ordinal)
                .Where(heading => vendorBeforeRole.IsMatch(heading) || roleBeforeVendor.IsMatch(heading))
                .ToArray();
            Assert.Empty(matches);
            var fullPayloadAssignments = output
                .Split('\n')
                .Where(line => directVendorRoleAssignment.IsMatch(line))
                .ToArray();
            Assert.Empty(fullPayloadAssignments);
            Console.WriteLine($"G797 AC6 vendor-adjacency-search {name}: vendor_headings={headings.Length}; vendor-before-role={vendorBeforeRoleMatches.Length}; role-before-vendor={roleBeforeVendorMatches.Length}; full-payload-assignments={fullPayloadAssignments.Length}; matching_headings={(matches.Length == 0 ? "<none>" : string.Join(" || ", matches))}");
        }
    }

    [Fact]
    public void FullRenderedPayload_RejectsRoleVendorAndDefaultPairings_G797()
    {
        var context = CreateContext();
        var surfaces = new (string Name, Func<TextWriter, int> Render)[]
        {
            ("guide design-thread", writer => GuideDesignThreadCommand.Execute(context, ["--format", "markdown"], writer)),
            ("guide orchestrator-thread", writer => GuideOrchestratorThreadCommand.Execute(context, ["--format", "markdown"], writer)),
            ("guide workflow task implementation-loop", writer => GuideWorkflowTaskImplementationLoopCommand.Execute(context, ["--format", "markdown"], writer)),
            ("guide workflow task review-next-slice-loop", writer => GuideWorkflowTaskReviewNextSliceLoopCommand.Execute(context, ["--format", "markdown"], writer)),
        };

        // G797 AC6 is about the complete rendered payload, not only headings
        // or direct role=vendor assignments. Keep the role markers explicit so
        // unrelated runtime examples (for example a scheduler name) do not
        // become false positives, while catching prose such as
        // "review_role ... default Codex" anywhere in the payload.
        const string vendor = @"(?:Claude|Codex|OpenCode|opencode)";
        const string roleMarker =
            @"(?:review_role|roles\.[a-z_]+|(?:architect|orchestrator|builder|reviewer|steward|design|orchestration|implementation|review)\s+role)";
        var roleVendorProse = new Regex(
            $@"(?im)(?:{roleMarker}[^\r\n]{{0,120}}\b{vendor}\b|\b{vendor}\b[^\r\n]{{0,120}}{roleMarker})",
            RegexOptions.Compiled);
        var defaultPairing = new Regex(
            $@"(?im)(?:{roleMarker}[^\r\n]{{0,120}}\bdefault(?:ed|s)?\b[^\r\n]{{0,120}}\b{vendor}\b|\b{vendor}\b[^\r\n]{{0,120}}\bdefault(?:ed|s)?\b[^\r\n]{{0,120}}{roleMarker})",
            RegexOptions.Compiled);

        foreach (var (name, render) in surfaces)
        {
            using var writer = new StringWriter();
            Assert.Equal(0, render(writer));
            var output = writer.ToString();
            var roleVendorMatches = roleVendorProse.Matches(output).Select(match => match.Value).ToArray();
            var defaultPairingMatches = defaultPairing.Matches(output).Select(match => match.Value).ToArray();

            Assert.Empty(roleVendorMatches);
            Assert.Empty(defaultPairingMatches);
            Assert.Contains(GuideRoleVocabulary.Reviewer, output, StringComparison.Ordinal);
            if (name.EndsWith("review-next-slice-loop", StringComparison.Ordinal))
            {
                Assert.Contains("canonical logical `reviewer`", output, StringComparison.Ordinal);
            }
            Assert.DoesNotContain("roles.review, default Codex", output, StringComparison.OrdinalIgnoreCase);
            Console.WriteLine($"G797 AC6 full-payload-role-vendor-scan {name}: role_vendor_matches={roleVendorMatches.Length}; default_pairings={defaultPairingMatches.Length}; matches=<none>; canonical_reviewer=present");
        }
    }

    [Fact]
    public void FullRenderedPayloads_MarkdownAndJsonRejectVendorRoleFusionsAndDefaults_G797Round5()
    {
        var context = CreateContext();
        var surfaces = new (string Name, Func<string, TextWriter, int> Render)[]
        {
            ("guide design-thread", (format, writer) => GuideDesignThreadCommand.Execute(context, ["--format", format], writer)),
            ("guide orchestrator-thread", (format, writer) => GuideOrchestratorThreadCommand.Execute(context, ["--format", format], writer)),
            ("guide workflow task implementation-loop", (format, writer) => GuideWorkflowTaskImplementationLoopCommand.Execute(context, ["--format", format], writer)),
            ("guide workflow task review-next-slice-loop", (format, writer) => GuideWorkflowTaskReviewNextSliceLoopCommand.Execute(context, ["--format", format], writer)),
        };

        // AC6 is a class-level rule over the complete compiled payload. A
        // runtime may be mentioned conditionally, but it must never become a
        // noun phrase with a logical role (for example "Codex reviewer" or
        // "sandboxed Codex orchestrator") or a vendor default. Keep the
        // vendor list explicit so this guard covers every supported spelling,
        // including the lower-case runtime names used by older guidance.
        const string vendor = @"(?:Claude|Codex|Astra|Fable|luna|terra|sol|opencode)";
        const string logicalRole = @"(?:architect|orchestrator|builder|reviewer|steward)";
        const string roleMarker = @"(?:review_role|roles\.[a-z_]+|(?:architect|orchestrator|builder|reviewer|steward)\s+role)";
        var fusedRoleVendor = new Regex(
            $@"(?ix)\b(?:sandboxed\s+)?{vendor}\s+{logicalRole}\b|\b{logicalRole}\s+{vendor}\b",
            RegexOptions.Compiled);
        var vendorDefault = new Regex(
            $@"(?ix)(?:\b{roleMarker}\b[^\r\n]{{0,120}}\bdefault(?:ed|s)?\b[^\r\n]{{0,120}}\b{vendor}\b|\b{vendor}\b[^\r\n]{{0,120}}\bdefault(?:ed|s)?\b[^\r\n]{{0,120}}\b{roleMarker}\b)",
            RegexOptions.Compiled);

        foreach (var (name, render) in surfaces)
        {
            foreach (var format in new[] { "markdown", "json" })
            {
                using var writer = new StringWriter();
                Assert.Equal(0, render(format, writer));
                var output = writer.ToString();
                var fusedMatches = fusedRoleVendor.Matches(output).Select(match => match.Value).ToArray();
                var defaultMatches = vendorDefault.Matches(output).Select(match => match.Value).ToArray();

                Assert.Empty(fusedMatches);
                Assert.Empty(defaultMatches);
                if (name == "guide orchestrator-thread")
                {
                    Assert.Contains("running on a sandboxed Codex runtime", output, StringComparison.Ordinal);
                }

                Console.WriteLine($"G797 AC6 round5 full-payload-scan {name} format={format}: fused_role_vendor={fusedMatches.Length}; vendor_defaults={defaultMatches.Length}; matches=<none>");
            }
        }
    }

    [Fact]
    public void RoleBearingCommandsAndJsonProjectOnlyCanonicalIdentifiers_G797()
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
            AssertRoleBearingValuesAreCanonical(writer.ToString(), name);
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

    private static void AssertRoleBearingValuesAreCanonical(string output, string surface)
    {
        var aliases = string.Join("|", RetiredAliases.Select(Regex.Escape));
        var cliFlag = new Regex($@"(?i)--(?:role|from|to|report-to|owner-role|owner)\s+(?:{aliases})(?=\s|`|'|""|$)", RegexOptions.Compiled);
        var normalizedJson = output.Replace("\\\"", "\"", StringComparison.Ordinal);
        var jsonValue = new Regex($@"(?i)""(?:role|from|to|report_to|thread|owner_role|destination_thread)""\s*:\s*""(?:{aliases})(?=@|""|\s|$)", RegexOptions.Compiled);
        var agmsgJoin = new Regex($@"(?i)agmsg\s+join\.sh\s+\S+\s+(?:{aliases})\s+(?=\S)", RegexOptions.Compiled);

        var matches = cliFlag.Matches(output)
            .Cast<Match>()
            .Concat(jsonValue.Matches(normalizedJson).Cast<Match>())
            .Concat(agmsgJoin.Matches(output).Cast<Match>())
            .Select(match => match.Value)
            .ToArray();
        Assert.Empty(matches);
        Console.WriteLine($"G797 role-bearing canonical projection {surface}: retired_value_matches=0");
    }
}
