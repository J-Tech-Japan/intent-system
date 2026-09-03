using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;
using IntentSystem.Supervisor.Models;
using IntentSystem.Supervisor.Serialization;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G789: topology-backed guide rendering for the non-normative Orca operating
/// block and the mixed-kind review-seat rule. The fixtures intentionally use
/// recorded role fields rather than model or role-name inference.
/// </summary>
public sealed class GuideSeatSelectionG789Tests
{
    private const string Domain = "intent-cli";
    private const string Repo = "J-Tech-Japan/intent-system";
    private const string ParentGuideReviewPayloadOracleHash = "8e90346d3975ec83b2c450cbd60fdb8ef47a467c759ec8d6fdce6b4f27670e35";
    private static readonly string[] ParentGuideReviewPayloadFieldNames =
    [
        "domain",
        "repo",
        "pr",
        "queue_state_path",
        "execution_unit",
        "queue_item_title",
        "queue_item_state",
        "packet_directory",
        "packet_files",
        "packet_paths",
        "intent_reference_paths",
        "review_checklist",
        "review_boundaries",
        "approval_summary_requirements",
        "same_account_review_verdict",
        "guide_reachability",
        "topology_workspace_move",
        "request_update_requirements",
        "automated_reviewer_comment_triage",
        "device_gated_evidence_policy",
        "review_standing_policy",
        "review_policy_source",
        "review_blocker_protocol",
        "pr_blocker_comment_template",
        "review_blocker_routing_examples",
        "chat_is_not_durable_workflow_state",
        "validation_suggestions",
        "tests_pass_is_necessary_not_sufficient",
        "gaps",
        "ready",
    ];
    private static readonly string[] ParentGuideReviewPolicyFieldNames =
    [
        "source",
        "domain",
        "warnings",
        "device_gated_evidence",
        "draft_handling",
        "external_artifact_intake",
        "test_evidence_sufficiency",
        "follow_up_tracking",
    ];

    [Fact]
    public void MixedKindTopology_RendersOrderedOrcaBlockAndDifferentKindReviewSeat_G789()
    {
        using var fixture = new TopologyFixture("mixed");
        fixture.RecordExternal("design", "claude-app");
        fixture.RecordHerdr("implementation", "codex");
        fixture.RecordHerdr("orchestration", "codex");
        fixture.RecordHerdr("review", "codex");

        using var jsonWriter = new StringWriter();
        Assert.Equal(0, GuideDesignThreadCommand.Execute(
            fixture.Context,
            ["--domain", Domain, "--team", fixture.Team, "--routing-root", fixture.Root, "--format", "json"],
            jsonWriter));
        using var designDocument = JsonDocument.Parse(jsonWriter.ToString());
        var contract = designDocument.RootElement.GetProperty("external_residence_operating_contract");
        var operatingBlock = contract.GetProperty("orca_operating_block");
        Assert.Equal(
            ["label", "setup_order", "send_form", "check_form", "shared_channel", "durable_record"],
            operatingBlock.EnumerateObject().Select(property => property.Name));
        Assert.Equal(
            [
                "Create or bind a Run before seat messages: `orca orchestration run-create --objective <text> [--from <handle>]` or `orca orchestration run-use --id <run-id> [--from <handle>]`.",
                "Share the resulting `<run-id>` with every sender before anyone addresses `run:<run-id>`.",
                "Each sender supplies its own `--from <role>` handle; it is a sender handle, not a routing identity.",
            ],
            operatingBlock.GetProperty("setup_order").EnumerateArray().Select(item => item.GetString()));
        Assert.Equal(GuideDesignThreadCommand.OrcaWakeSendForm, operatingBlock.GetProperty("send_form").GetString());
        Assert.Equal(GuideDesignThreadCommand.OrcaCheckForm, operatingBlock.GetProperty("check_form").GetString());
        Assert.Contains("courtesy wakes and design-to-design messages", operatingBlock.GetProperty("shared_channel").GetString(), StringComparison.Ordinal);
        Assert.Contains("neither launches nor manages Orca", operatingBlock.GetProperty("durable_record").GetString(), StringComparison.Ordinal);
        Assert.Contains(GuideDesignThreadCommand.OrcaWakeSendForm, contract.GetProperty("wake_channel_declaration").GetString(), StringComparison.Ordinal);

        var selection = designDocument.RootElement.GetProperty("team_and_duty_split").GetProperty("review_seat_selection");
        Assert.Equal(
            [
                "design (frontend:claude-app)",
                "implementation (kind:codex)",
                "orchestration (kind:codex)",
                "review (kind:codex)",
            ],
            selection.GetProperty("recorded_seat_kinds").EnumerateArray().Select(item => item.GetString()));
        Assert.Equal("design (frontend:claude-app)", selection.GetProperty("design_seat").GetString());
        Assert.Equal("review (kind:codex)", selection.GetProperty("review_seat").GetString());
        Assert.Equal("review", selection.GetProperty("selected_review_seat").GetString());
        Assert.Contains("different recorded kind/frontend from design", selection.GetProperty("selection").GetString(), StringComparison.Ordinal);
        Assert.Contains("Only recorded topology fields decide", selection.GetProperty("recorded_fields_decide").GetString(), StringComparison.Ordinal);

        using var markdownWriter = new StringWriter();
        Assert.Equal(0, GuideDesignThreadCommand.Execute(
            fixture.Context,
            ["--domain", Domain, "--team", fixture.Team, "--routing-root", fixture.Root, "--format", "markdown"],
            markdownWriter));
        var markdown = markdownWriter.ToString();
        var blockStart = markdown.IndexOf("Non-normative Orca operating block", StringComparison.Ordinal);
        var createIndex = markdown.IndexOf("run-create --objective <text>", blockStart, StringComparison.Ordinal);
        var bindIndex = markdown.IndexOf("run-use --id <run-id>", blockStart, StringComparison.Ordinal);
        var shareIndex = markdown.IndexOf("Share the resulting `<run-id>`", blockStart, StringComparison.Ordinal);
        var senderIndex = markdown.IndexOf("Each sender supplies its own", blockStart, StringComparison.Ordinal);
        var sendIndex = markdown.IndexOf(GuideDesignThreadCommand.OrcaWakeSendForm, blockStart, StringComparison.Ordinal);
        var checkIndex = markdown.IndexOf(GuideDesignThreadCommand.OrcaCheckForm, blockStart, StringComparison.Ordinal);
        Assert.True(blockStart >= 0 && createIndex >= 0 && bindIndex > createIndex && shareIndex > bindIndex && senderIndex > shareIndex && sendIndex > senderIndex && checkIndex > sendIndex, markdown);
        Assert.Contains("review-seat selection (G789)", markdown, StringComparison.Ordinal);

        Fixture("mixed design-thread Orca block JSON", operatingBlock.GetRawText());
        Fixture("mixed design-thread wake declaration JSON", contract.GetProperty("wake_channel_declaration").GetRawText());
        Fixture("mixed design-thread review-seat JSON", selection.GetRawText());
        Fixture("mixed design-thread Markdown Orca block", Section(
            markdown,
            "- **Non-normative Orca operating block:**",
            "- **different transition:**"));
    }

    [Fact]
    public void GuideReview_UsesSameMixedKindSelectionFromTheOnlyRecordedTeam_G789()
    {
        using var fixture = new TopologyFixture("review-mixed");
        fixture.RecordExternal("design", "claude-app");
        fixture.RecordHerdr("implementation", "codex");
        fixture.RecordHerdr("orchestration", "codex");
        fixture.RecordHerdr("review", "codex");
        fixture.SeedReviewablePr();

        using var jsonWriter = new StringWriter();
        Assert.Equal(0, GuideReviewCommand.Execute(
            fixture.Context,
            ["--repo", Repo, "--pr", "1724", "--domain", Domain, "--format", "json"],
            jsonWriter));
        using var document = JsonDocument.Parse(jsonWriter.ToString());
        var selection = document.RootElement
            .GetProperty("review_standing_policy")
            .GetProperty("review_seat_selection");
        Assert.Equal(fixture.Team, selection.GetProperty("topology_team").GetString());
        Assert.Equal("review", selection.GetProperty("selected_review_seat").GetString());
        Assert.Contains("reviews design output and PRs", selection.GetProperty("selection").GetString(), StringComparison.Ordinal);

        using var markdownWriter = new StringWriter();
        Assert.Equal(0, GuideReviewCommand.Execute(
            fixture.Context,
            ["--repo", Repo, "--pr", "1724", "--domain", Domain, "--format", "markdown"],
            markdownWriter));
        var markdown = markdownWriter.ToString();
        Assert.Contains("### Review-seat selection (G789)", markdown, StringComparison.Ordinal);
        Assert.Contains("design (frontend:claude-app)", markdown, StringComparison.Ordinal);
        Assert.Contains("review (kind:codex)", markdown, StringComparison.Ordinal);

        Fixture("mixed guide review JSON", selection.GetRawText());
        Fixture("mixed guide review Markdown", Section(
            markdown,
            "### Review-seat selection (G789)",
            "## Review blocker protocol"));
    }

    [Fact]
    public void GuideReview_RemovingOnlyG789SelectionMatchesImmutableParentPayload_G789()
    {
        using var fixture = new TopologyFixture(
            "review-oracle",
            Path.Combine(Path.GetTempPath(), "g789-guide-review-oracle-root"));
        fixture.SeedReviewablePr();

        using var writer = new StringWriter();
        Assert.Equal(0, GuideReviewCommand.Execute(
            fixture.Context,
            ["--repo", Repo, "--pr", "1724", "--domain", Domain, "--format", "json"],
            writer));
        using var document = JsonDocument.Parse(writer.ToString());
        var head = document.RootElement;
        var headPolicy = head.GetProperty("review_standing_policy");
        Assert.True(headPolicy.TryGetProperty("review_seat_selection", out _));

        using var projected = JsonDocument.Parse(RemoveG789Additions(head));
        var fields = projected.RootElement.EnumerateObject().ToArray();
        Assert.Equal(ParentGuideReviewPayloadFieldNames, fields.Select(field => field.Name));
        var policyFields = fields.Single(field => field.Name == "review_standing_policy")
            .Value.EnumerateObject().Select(field => field.Name);
        Assert.Equal(ParentGuideReviewPolicyFieldNames, policyFields);

        var actualOracle = ComputePayloadOracle(fields);
        Assert.Equal(ParentGuideReviewPayloadOracleHash, actualOracle);
        Console.WriteLine($"G789 guide-review parent remainder oracle: expected={ParentGuideReviewPayloadOracleHash}; actual={actualOracle}; removed=review_standing_policy.review_seat_selection");
    }

    [Fact]
    public void SingleKindTopology_AllowsDesignOrchestrationCrossReview_G789()
    {
        using var fixture = new TopologyFixture("single");
        fixture.RecordHerdr("design", "codex");
        fixture.RecordHerdr("implementation", "codex");
        fixture.RecordHerdr("orchestration", "codex");
        fixture.RecordHerdr("review", "codex");
        fixture.SeedReviewablePr();

        using var designWriter = new StringWriter();
        Assert.Equal(0, GuideDesignThreadCommand.Execute(
            fixture.Context,
            ["--domain", Domain, "--team", fixture.Team, "--routing-root", fixture.Root, "--format", "json"],
            designWriter));
        using var designDocument = JsonDocument.Parse(designWriter.ToString());
        var designSelection = designDocument.RootElement
            .GetProperty("team_and_duty_split")
            .GetProperty("review_seat_selection");
        Assert.True(designDocument.RootElement.GetProperty("external_residence_operating_contract").TryGetProperty("orca_operating_block", out _));
        Assert.Contains("Single recorded kind", designSelection.GetProperty("selection").GetString(), StringComparison.Ordinal);
        Assert.Contains("design↔orchestration cross-review is acceptable", designSelection.GetProperty("selection").GetString(), StringComparison.Ordinal);

        using var reviewWriter = new StringWriter();
        Assert.Equal(0, GuideReviewCommand.Execute(
            fixture.Context,
            ["--repo", Repo, "--pr", "1724", "--domain", Domain, "--format", "json"],
            reviewWriter));
        using var reviewDocument = JsonDocument.Parse(reviewWriter.ToString());
        var reviewSelection = reviewDocument.RootElement
            .GetProperty("review_standing_policy")
            .GetProperty("review_seat_selection");
        Assert.Equal(designSelection.GetProperty("selection").GetString(), reviewSelection.GetProperty("selection").GetString());

        Fixture("single-kind design-thread JSON", designSelection.GetRawText());
        Fixture("single-kind guide review JSON", reviewSelection.GetRawText());
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("unreadable")]
    [InlineData("ambiguous")]
    public void MissingUnreadableOrAmbiguousTopology_StillRendersStaticGuidanceWithoutResolution_G789(string scenario)
    {
        using var fixture = new TopologyFixture($"fallback-{scenario}");
        if (string.Equals(scenario, "unreadable", StringComparison.Ordinal))
        {
            fixture.RecordUnreadable();
        }
        else if (string.Equals(scenario, "ambiguous", StringComparison.Ordinal))
        {
            fixture.RecordAmbiguousTeams();
        }
        fixture.SeedReviewablePr();

        var designArgs = new List<string>
        {
            "--domain", Domain,
            "--routing-root", fixture.Root,
            "--format", "json",
        };
        // With multiple recorded teams there is no selected team to resolve;
        // the guide must still render its static rule instead of guessing.
        if (!string.Equals(scenario, "ambiguous", StringComparison.Ordinal))
        {
            designArgs.InsertRange(2, ["--team", fixture.Team]);
        }

        using var designJsonWriter = new StringWriter();
        Assert.Equal(0, GuideDesignThreadCommand.Execute(fixture.Context, designArgs.ToArray(), designJsonWriter));
        using var designDocument = JsonDocument.Parse(designJsonWriter.ToString());
        var designRoot = designDocument.RootElement;
        var designSelection = designRoot.GetProperty("team_and_duty_split").GetProperty("review_seat_selection");
        AssertStaticSelectionWithoutResolution(designSelection);
        var designContract = designRoot.GetProperty("external_residence_operating_contract");
        Assert.True(designContract.TryGetProperty("orca_operating_block", out var designOrca));
        Assert.Equal("Non-normative Orca operating block", designOrca.GetProperty("label").GetString());

        designArgs[^1] = "markdown";
        using var designMarkdownWriter = new StringWriter();
        Assert.Equal(0, GuideDesignThreadCommand.Execute(fixture.Context, designArgs.ToArray(), designMarkdownWriter));
        var designMarkdown = designMarkdownWriter.ToString();
        var designSelectionSection = Section(
            designMarkdown,
            "- **review-seat selection (G789):**",
            "- **different transition:**");
        Assert.Contains("When recorded seat kinds differ", designSelectionSection, StringComparison.Ordinal);
        Assert.Contains("Non-normative Orca operating block", designMarkdown, StringComparison.Ordinal);
        Assert.DoesNotContain("- recorded topology:", designSelectionSection, StringComparison.Ordinal);
        Assert.DoesNotContain("- selection:", designSelectionSection, StringComparison.Ordinal);

        using var reviewJsonWriter = new StringWriter();
        Assert.Equal(0, GuideReviewCommand.Execute(
            fixture.Context,
            ["--repo", Repo, "--pr", "1724", "--domain", Domain, "--format", "json"],
            reviewJsonWriter));
        using var reviewDocument = JsonDocument.Parse(reviewJsonWriter.ToString());
        var reviewSelection = reviewDocument.RootElement
            .GetProperty("review_standing_policy")
            .GetProperty("review_seat_selection");
        AssertStaticSelectionWithoutResolution(reviewSelection);

        using var reviewMarkdownWriter = new StringWriter();
        Assert.Equal(0, GuideReviewCommand.Execute(
            fixture.Context,
            ["--repo", Repo, "--pr", "1724", "--domain", Domain, "--format", "markdown"],
            reviewMarkdownWriter));
        var reviewMarkdown = reviewMarkdownWriter.ToString();
        var reviewSelectionSection = Section(
            reviewMarkdown,
            "### Review-seat selection (G789)",
            "## Review blocker protocol");
        Assert.Contains("When recorded seat kinds differ", reviewSelectionSection, StringComparison.Ordinal);
        Assert.DoesNotContain("- recorded topology (", reviewSelectionSection, StringComparison.Ordinal);
        Assert.DoesNotContain("- selection:", reviewSelectionSection, StringComparison.Ordinal);

        Fixture($"{scenario} fallback design-thread JSON", designSelection.GetRawText());
        Fixture($"{scenario} fallback design-thread Markdown", designSelectionSection);
        Fixture($"{scenario} fallback guide review JSON", reviewSelection.GetRawText());
        Fixture($"{scenario} fallback guide review Markdown", reviewSelectionSection);
    }

    [Fact]
    public void DocumentationMirrors_RenderedOrcaOrderAndReviewSeatRule_G789()
    {
        var repoRoot = RepoVersionPolicySource.RepoRoot();
        var english = File.ReadAllText(Path.Combine(repoRoot, "docs", "en", "12-agent-message-orchestration.md"));
        var japanese = File.ReadAllText(Path.Combine(repoRoot, "docs", "ja", "12-agent-message-orchestration.md"));

        foreach (var document in new[] { english, japanese })
        {
            Assert.Contains("run-create --objective <text> [--from <handle>]", document, StringComparison.Ordinal);
            Assert.Contains("run-use --id <run-id> [--from <handle>]", document, StringComparison.Ordinal);
            Assert.Contains(GuideDesignThreadCommand.OrcaWakeSendForm, document, StringComparison.Ordinal);
            Assert.Contains(GuideDesignThreadCommand.OrcaCheckForm, document, StringComparison.Ordinal);
            Assert.Contains("review_seat_selection", document, StringComparison.Ordinal);
            Assert.Contains("review_standing_policy", document, StringComparison.Ordinal);
            Assert.Contains("kind`", document, StringComparison.Ordinal);
            Assert.Contains("frontend`", document, StringComparison.Ordinal);
        }

        Assert.Contains("Mixed-kind review-seat selection (G789)", english, StringComparison.Ordinal);
        Assert.Contains("mixed-kind review-seat selection（G789）", japanese, StringComparison.Ordinal);
    }

    private static void Fixture(string name, string value) =>
        Console.WriteLine($"G789 {name}:\n{value.TrimEnd()}");

    private static string Section(string document, string start, string end)
    {
        var startIndex = document.IndexOf(start, StringComparison.Ordinal);
        Assert.True(startIndex >= 0, $"Missing section start '{start}'.");
        var endIndex = document.IndexOf(end, startIndex, StringComparison.Ordinal);
        Assert.True(endIndex > startIndex, $"Missing section end '{end}'.");
        return document[startIndex..endIndex];
    }

    private static void AssertStaticSelectionWithoutResolution(JsonElement selection)
    {
        Assert.Contains("When recorded seat kinds differ", selection.GetProperty("mixed_kind_rule").GetString(), StringComparison.Ordinal);
        Assert.Contains("design↔orchestration cross-review is acceptable", selection.GetProperty("single_kind_allowance").GetString(), StringComparison.Ordinal);
        Assert.Contains("Only recorded topology fields decide", selection.GetProperty("recorded_fields_decide").GetString(), StringComparison.Ordinal);
        foreach (var property in new[] { "topology_team", "recorded_seat_kinds", "design_seat", "review_seat", "selected_review_seat", "selection" })
        {
            Assert.False(selection.TryGetProperty(property, out _), $"Unexpected topology resolution property '{property}'.");
        }
    }

    private sealed class TopologyFixture : IDisposable
    {
        public TopologyFixture(string suffix, string? rootOverride = null)
        {
            Root = rootOverride ?? Directory.CreateTempSubdirectory($"guide-g789-{suffix}-").FullName;
            if (rootOverride is not null && Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
            Team = $"g789-{suffix}";
            Context = new CliContext
            {
                RepoRoot = Root,
                Config = new CliConfig
                {
                    Project = new ProjectConfig
                    {
                        Domain = Domain,
                        ArtifactRoot = ".intent-cli",
                        WorktreeRoot = ".intent-cli/worktrees",
                    },
                },
            };
        }

        public string Root { get; }
        public string Team { get; }
        public CliContext Context { get; }

        public void RecordExternal(string role, string frontend)
        {
            var result = Run(
            [
                "session-layer", "topology", "record", "--domain", Domain, "--team", Team,
                "--role", role, "--resident", "external",
                "--reader", $".intent-cli/events/{Team}-{role}.jsonl", "--frontend", frontend,
                "--write", "--format", "json",
            ]);
            Assert.Equal(0, result.ExitCode);
        }

        public void RecordHerdr(string role, string kind)
            => RecordHerdr(Team, role, kind);

        public void RecordHerdr(string team, string role, string kind)
        {
            var result = Run(
            [
                "session-layer", "topology", "record", "--domain", Domain, "--team", team,
                "--role", role, "--resident", "herdr", "--workspace-id", "wG789",
                "--pane-id", $"wG789:{team}:{role}", "--cwd", "/g789", "--kind", kind,
                "--delivery-method", "inline", "--write", "--format", "json",
            ]);
            Assert.Equal(0, result.ExitCode);
        }

        public void RecordUnreadable()
        {
            var directory = Path.Combine(Root, ".intent-cli", "topology", Domain);
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, $"{Team}.json"), "{ not valid json");
        }

        public void RecordAmbiguousTeams()
        {
            foreach (var team in new[] { $"{Team}-a", $"{Team}-b" })
            {
                RecordHerdr(team, "design", "codex");
                RecordHerdr(team, "implementation", "codex");
                RecordHerdr(team, "orchestration", "codex");
                RecordHerdr(team, "review", "codex");
            }
        }

        public void SeedReviewablePr()
        {
            Directory.CreateDirectory(Path.Combine(Root, ".intent-cli"));
            var state = new QueueState
            {
                SchemaVersion = "1",
                UpdatedAt = new DateTimeOffset(2026, 9, 2, 0, 0, 0, TimeSpan.Zero),
                Items =
                [
                    new QueueItem
                    {
                        ExecutionUnit = "G789",
                        Title = "Orca guide and review-seat selection",
                        State = QueueItemState.Review,
                        Dependencies = Array.Empty<string>(),
                        BlockedBy = Array.Empty<string>(),
                        ClarificationReturnPath = string.Empty,
                        PacketPaths = new PacketPaths
                        {
                            Yaml = ".intent-cli/issues/G789/packet.yaml",
                            Implementation = ".intent-cli/issues/G789/implementation.md",
                            ReviewContext = ".intent-cli/issues/G789/review-context.md",
                        },
                        LinkedIssue = new LinkedIssue
                        {
                            Repo = Repo,
                            Number = 1724,
                            Url = "https://github.com/J-Tech-Japan/intent-system/issues/1724",
                        },
                        LinkedPr = "https://github.com/J-Tech-Japan/intent-system/pull/1724",
                        WorkerRole = "implementation",
                        ReviewRole = "review",
                        Priority = "normal",
                    },
                ],
            };
            File.WriteAllText(Context.GetQueueStatePath(), QueueStateSerializer.Serialize(state));
            var packetDirectory = Path.Combine(Root, ".intent-cli", "issues", "G789");
            Directory.CreateDirectory(packetDirectory);
            File.WriteAllText(Path.Combine(packetDirectory, "packet.yaml"), "g789: guide-only");
        }

        private (int ExitCode, string Output) Run(string[] args)
        {
            using var writer = new StringWriter();
            var exitCode = CommandRouter.Execute(args, Context, writer);
            return (exitCode, writer.ToString());
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }

    private static string RemoveG789Additions(JsonElement root)
    {
        var projected = JsonNode.Parse(root.GetRawText())!.AsObject();
        projected["team_and_duty_split"]?.AsObject().Remove("review_seat_selection");
        projected["external_residence_operating_contract"]?.AsObject().Remove("orca_operating_block");
        projected["review_standing_policy"]?.AsObject().Remove("review_seat_selection");
        return projected.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    private static string ComputePayloadOracle(IEnumerable<JsonProperty> fields)
    {
        var payload = string.Join(
            "\u001E",
            fields.Select(field => field.Name + "\u001F" + field.Value.GetRawText()));
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
    }
}
