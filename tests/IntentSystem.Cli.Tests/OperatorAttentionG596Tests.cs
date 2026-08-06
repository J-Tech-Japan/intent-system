using System.Text.Json;
using System.Text.Json.Nodes;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;
using IntentSystem.Supervisor;

namespace IntentSystem.Cli.Tests;

/// <summary>G596 lifecycle, no-inference, routing, and fail-closed contract.</summary>
[Collection(AutomationStalledWorkSharedStateCollection.Name)]
public sealed class OperatorAttentionG596Tests : IDisposable
{
    private static readonly DateTimeOffset FixedNow = new(2026, 8, 3, 18, 0, 0, TimeSpan.Zero);
    private readonly Workspace workspace = new();

    public OperatorAttentionG596Tests()
    {
        OperatorAttentionCommand.UtcNowFactory = () => FixedNow;
        AutomationStalledWorkCommand.UtcNowFactory = () => FixedNow;
        AutomationStalledWorkCommand.CandidateListerFactory = () => new EmptyLister();
    }

    public void Dispose()
    {
        OperatorAttentionCommand.UtcNowFactory = null;
        AutomationStalledWorkCommand.UtcNowFactory = null;
        AutomationStalledWorkCommand.CandidateListerFactory = null;
        workspace.Dispose();
    }

    [Fact]
    public void Query_AbsentStore_IsCheckNotCompletedAndNeverGreen_G596()
    {
        var (exitCode, result) = workspace.Run(
            "operator-attention", "query", "--domain", "intent-cli", "--format", "json");

        Assert.Equal(1, exitCode);
        Assert.Equal("check-not-completed", result.GetProperty("status").GetString());
        Assert.Equal(0, result.GetProperty("open_count").GetInt32());
        Assert.Contains("has not been completed", result.GetProperty("error").GetString(), StringComparison.Ordinal);
        Assert.False(File.Exists(workspace.StorePath));
    }

    [Fact]
    public void JudgmentWait_CanonicalAndDeprecatedAliasHaveEquivalentJsonContracts_G623()
    {
        var openArguments = new[]
        {
            "open", "--record", "design-rename", "--domain", "intent-cli", "--team", "intent-cli-dev",
            "--owner", "design", "--blocking-reference", "issue:1349", "--action-needed", "Choose the replacement",
            "--evidence", "G599 applies to every judging party", "--dry-run", "--format", "json",
        };
        AssertEquivalentExceptDeprecationWarning(
            workspace.Run(["judgment-wait", .. openArguments]).Result,
            workspace.Run(["operator-attention", .. openArguments]).Result);

        var oldOpen = workspace.Run(
            "operator-attention", "open", "--record", "written-under-old-name", "--domain", "intent-cli", "--team", "intent-cli-dev",
            "--owner", "design", "--blocking-reference", "issue:1349", "--action-needed", "Provide design judgment",
            "--evidence", "old command remains supported through 1.x", "--write", "--format", "json");
        Assert.Equal(0, oldOpen.ExitCode);
        AssertDeprecatedAliasWarning(oldOpen.Result);

        var queryArguments = new[] { "query", "--domain", "intent-cli", "--team", "intent-cli-dev", "--format", "json" };
        AssertEquivalentExceptDeprecationWarning(
            workspace.Run(["judgment-wait", .. queryArguments]).Result,
            workspace.Run(["operator-attention", .. queryArguments]).Result);

        var resolveArguments = new[]
        {
            "resolve", "--record", "written-under-old-name", "--resolution-evidence", "design decision recorded", "--dry-run", "--format", "json",
        };
        AssertEquivalentExceptDeprecationWarning(
            workspace.Run(["judgment-wait", .. resolveArguments]).Result,
            workspace.Run(["operator-attention", .. resolveArguments]).Result);

        var supersedeArguments = new[]
        {
            "supersede", "--record", "written-under-old-name", "--evidence", "scope changed", "--dry-run", "--format", "json",
        };
        AssertEquivalentExceptDeprecationWarning(
            workspace.Run(["judgment-wait", .. supersedeArguments]).Result,
            workspace.Run(["operator-attention", .. supersedeArguments]).Result);

        var canonicalQuery = workspace.Run("judgment-wait", "query", "--domain", "intent-cli", "--format", "json").Result;
        Assert.Contains(
            canonicalQuery.GetProperty("records").EnumerateArray(),
            record => record.GetProperty("record_id").GetString() == "written-under-old-name");
        Assert.False(canonicalQuery.TryGetProperty("deprecation_warning", out _));
    }

    [Fact]
    public void Open_QueryByDomainAndTeam_CarriesActionableFieldsTransitionsAndAge_G596()
    {
        OperatorAttentionCommand.UtcNowFactory = () => FixedNow.AddMinutes(-75);
        var (openExit, openResult) = workspace.Open("release-091", write: true);
        Assert.Equal(0, openExit);
        Assert.True(openResult.GetProperty("applied").GetBoolean());

        OperatorAttentionCommand.UtcNowFactory = () => FixedNow;
        var (domainExit, domainResult) = workspace.Run(
            "operator-attention", "query", "--domain", "intent-cli", "--format", "json");
        var (teamExit, teamResult) = workspace.Run(
            "operator-attention", "query", "--team", "intent-cli-dev", "--format", "json");

        Assert.Equal(0, domainExit);
        Assert.Equal(0, teamExit);
        foreach (var result in new[] { domainResult, teamResult })
        {
            Assert.Equal("attention-pending", result.GetProperty("status").GetString());
            var record = Assert.Single(result.GetProperty("open_records").EnumerateArray());
            Assert.Equal("release-091", record.GetProperty("record_id").GetString());
            Assert.Equal("release-operator", record.GetProperty("owner").GetString());
            Assert.Equal("release:v0.9.1", record.GetProperty("blocking_reference").GetString());
            Assert.Equal("Approve and publish v0.9.1", record.GetProperty("action_needed").GetString());
            Assert.Equal("release candidate checks are green", record.GetProperty("establishing_evidence").GetString());
            Assert.Equal(75, record.GetProperty("age_minutes").GetInt32());
            var transition = Assert.Single(record.GetProperty("transitions").EnumerateArray());
            Assert.Equal(JsonValueKind.Null, transition.GetProperty("from_status").ValueKind);
            Assert.Equal("open", transition.GetProperty("to_status").GetString());
            Assert.Equal(FixedNow.AddMinutes(-75), transition.GetProperty("transitioned_at").GetDateTimeOffset());
        }
    }

    [Fact]
    public void Query_DomainAndTeamScopesAreIndependentAndIntersectionIsExact_G596()
    {
        Assert.Equal(0, workspace.OpenScoped("intent-main", "intent-cli", "intent-cli-dev").ExitCode);
        Assert.Equal(0, workspace.OpenScoped("intent-other-team", "intent-cli", "release-team").ExitCode);
        Assert.Equal(0, workspace.OpenScoped("other-same-team", "sekiban", "intent-cli-dev").ExitCode);

        var domain = workspace.Run(
            "operator-attention", "query", "--domain", "intent-cli", "--format", "json").Result;
        var team = workspace.Run(
            "operator-attention", "query", "--team", "intent-cli-dev", "--format", "json").Result;
        var intersection = workspace.Run(
            "operator-attention", "query", "--domain", "intent-cli", "--team", "intent-cli-dev", "--format", "json").Result;

        Assert.Equal(
            ["intent-main", "intent-other-team"],
            domain.GetProperty("open_records").EnumerateArray().Select(record => record.GetProperty("record_id").GetString()));
        Assert.Equal(
            ["intent-main", "other-same-team"],
            team.GetProperty("open_records").EnumerateArray().Select(record => record.GetProperty("record_id").GetString()));
        Assert.Equal(
            "intent-main",
            Assert.Single(intersection.GetProperty("open_records").EnumerateArray()).GetProperty("record_id").GetString());
    }

    [Fact]
    public void Resolve_RequiresResolutionEvidenceAndRecordsTerminalTimestamp_G596()
    {
        Assert.Equal(0, workspace.Open("release-091", write: true).ExitCode);
        OperatorAttentionCommand.UtcNowFactory = () => FixedNow.AddMinutes(10);

        var (exitCode, result) = workspace.Run(
            "operator-attention", "resolve", "--record", "release-091",
            "--resolution-evidence", "operator approved release in signed change record", "--write", "--format", "json");

        Assert.Equal(0, exitCode);
        var record = result.GetProperty("record");
        Assert.Equal("resolved", record.GetProperty("status").GetString());
        Assert.Equal("operator approved release in signed change record", record.GetProperty("resolution_evidence").GetString());
        Assert.Equal(2, record.GetProperty("transitions").GetArrayLength());
        Assert.Equal(FixedNow.AddMinutes(10), record.GetProperty("transitions")[1].GetProperty("transitioned_at").GetDateTimeOffset());

        var query = workspace.Run("operator-attention", "query", "--domain", "intent-cli", "--format", "json").Result;
        Assert.Equal("no-attention-pending", query.GetProperty("status").GetString());
        Assert.Equal(0, query.GetProperty("open_count").GetInt32());
        Assert.Equal("resolved", Assert.Single(query.GetProperty("records").EnumerateArray()).GetProperty("status").GetString());
    }

    [Fact]
    public void SupersedeThenReopen_CreatesNewReferencingRecordWithoutMutatingOld_G596()
    {
        Assert.Equal(0, workspace.Open("release-091-old", write: true).ExitCode);
        OperatorAttentionCommand.UtcNowFactory = () => FixedNow.AddMinutes(5);
        Assert.Equal(0, workspace.Run(
            "operator-attention", "supersede", "--record", "release-091-old",
            "--evidence", "approval scope changed", "--write", "--format", "json").ExitCode);

        OperatorAttentionCommand.UtcNowFactory = () => FixedNow.AddMinutes(6);
        var (exitCode, result) = workspace.Open("release-091-new", write: true, supersedes: "release-091-old");

        Assert.Equal(0, exitCode);
        Assert.Equal("release-091-old", result.GetProperty("record").GetProperty("supersedes_record_id").GetString());
        var query = workspace.Run(
            "operator-attention", "query", "--domain", "intent-cli", "--team", "intent-cli-dev", "--format", "json").Result;
        var records = query.GetProperty("records").EnumerateArray().ToDictionary(
            record => record.GetProperty("record_id").GetString()!, record => record.Clone(), StringComparer.Ordinal);
        Assert.Equal("superseded", records["release-091-old"].GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, records["release-091-old"].GetProperty("resolution_evidence").ValueKind);
        Assert.Equal(2, records["release-091-old"].GetProperty("transitions").GetArrayLength());
        Assert.Equal("open", records["release-091-new"].GetProperty("status").GetString());
        Assert.Single(query.GetProperty("open_records").EnumerateArray());
    }

    [Fact]
    public void NotifyEscalationAlone_AppendsEventButLeavesAttentionStoreAbsent_G596()
    {
        workspace.RecordAgmsgMode();
        var before = File.Exists(workspace.StorePath) ? File.ReadAllText(workspace.StorePath) : null;

        var (exitCode, result) = workspace.Run(
            "notify", "escalate", "--domain", "intent-cli", "--team", "intent-cli-dev",
            "--from", "orchestration", "--task-id", "G596-inference-guard",
            "--artifact", "https://example.test/blocker", "--summary", "operator approval required",
            "--write", "--format", "json");

        Assert.Equal(0, exitCode);
        Assert.True(result.GetProperty("event_appended").GetBoolean());
        Assert.True(File.Exists(Path.Combine(workspace.RootPath, ".intent-cli", "events", "intent-cli-dev.jsonl")));
        Assert.Equal(before, File.Exists(workspace.StorePath) ? File.ReadAllText(workspace.StorePath) : null);
        Assert.False(File.Exists(workspace.StorePath));
    }

    [Fact]
    public void IdlePipelineWithOpenRecord_RoutesHeartbeatToOperatorNotOrchestrator_G596()
    {
        OperatorAttentionCommand.UtcNowFactory = () => FixedNow.AddMinutes(-1);
        Assert.Equal(0, workspace.OpenOwned("release-approval", "operator", write: true).ExitCode);

        var stalled = AutomationStalledWorkCommand.Analyze(
            workspace.Context, "intent-cli", "J-Tech-Japan/intent-system", staleMinutes: 45);
        var item = Assert.Single(stalled.Items);
        Assert.Equal("operator-attention-pending", item.Kind);
        Assert.Equal("release-approval", item.OperatorAttentionRecordId);
        Assert.Equal("operator", item.RequiredActor);
        Assert.False(item.OrchestratorActionable);
        Assert.True(stalled.Stalled);

        var (heartbeatExit, heartbeat) = workspace.Run(
            "automation", "heartbeat", "--domain", "intent-cli",
            "--repo", "J-Tech-Japan/intent-system", "--format", "json");
        Assert.Equal(0, heartbeatExit);
        Assert.Equal("operator", heartbeat.GetProperty("route_to").GetString());
        var message = heartbeat.GetProperty("message_body").GetString()!;
        Assert.Contains("ROUTE TO OPERATOR", message, StringComparison.Ordinal);
        Assert.Contains("orchestrator_actionable=false", message, StringComparison.Ordinal);
        Assert.Contains("release-approval", message, StringComparison.Ordinal);
        Assert.DoesNotContain("1 orchestrator-actionable", message, StringComparison.Ordinal);
    }

    [Fact]
    public void DesignOwnedLifecycle_PropagatesOwnerThroughStalledWorkHeartbeatAndResolution_G599()
    {
        workspace.WriteTopology();
        Assert.Equal(0, workspace.OpenOwned("design-gate", "design", write: true).ExitCode);

        var stalled = AutomationStalledWorkCommand.Analyze(
            workspace.Context, "intent-cli", "J-Tech-Japan/intent-system", staleMinutes: 45);
        var item = Assert.Single(stalled.Items);
        Assert.Equal("design", item.RequiredActor);
        Assert.Equal("design", item.OperatorAttentionOwner);
        Assert.Contains(":design:", item.DedupeKey, StringComparison.Ordinal);

        var (_, heartbeat) = workspace.Run("automation", "heartbeat", "--domain", "intent-cli",
            "--repo", "J-Tech-Japan/intent-system", "--team", "intent-cli-dev", "--format", "json");
        Assert.Equal("operator-required", heartbeat.GetProperty("verdict").GetString());
        Assert.Equal("design", heartbeat.GetProperty("action_owner").GetString());
        Assert.Equal("design", heartbeat.GetProperty("target_role").GetString());
        Assert.Equal("design", heartbeat.GetProperty("route_to").GetString());
        Assert.Contains("--to design", heartbeat.GetProperty("canonical_notify_command").GetString(), StringComparison.Ordinal);
        Assert.Contains(":design:", heartbeat.GetProperty("dedupe_key").GetString(), StringComparison.Ordinal);
        var message = heartbeat.GetProperty("message_body").GetString()!;
        Assert.Contains("ROUTE TO DESIGN", message, StringComparison.Ordinal);
        Assert.Contains("design-required attention item", message, StringComparison.Ordinal);
        Assert.Contains("DESIGN REQUIRED (orchestrator_actionable=false)", message, StringComparison.Ordinal);
        Assert.Contains("Owner: design.", message, StringComparison.Ordinal);
        Assert.Contains("Blocking reference: design:design-gate.", message, StringComparison.Ordinal);

        Assert.Equal(0, workspace.Run("operator-attention", "resolve", "--record", "design-gate",
            "--resolution-evidence", "design ruling recorded", "--write", "--format", "json").ExitCode);
        var (_, restored) = workspace.Run("automation", "heartbeat", "--domain", "intent-cli",
            "--repo", "J-Tech-Japan/intent-system", "--team", "intent-cli-dev", "--format", "json");
        Assert.Equal("actionable-stall", restored.GetProperty("verdict").GetString());
    }

    [Fact]
    public void UnknownOwnerAndCommentOnlyBlock_FailClosedOrRemainActionable_G599()
    {
        workspace.WriteTopology();
        var (_, commentOnly) = workspace.Run("automation", "heartbeat", "--domain", "intent-cli",
            "--repo", "J-Tech-Japan/intent-system", "--team", "intent-cli-dev", "--format", "json");
        Assert.Equal("actionable-stall", commentOnly.GetProperty("verdict").GetString());

        Assert.Equal(0, workspace.OpenOwned("unknown-gate", "unrecorded-owner", write: true).ExitCode);
        var (_, unknown) = workspace.Run("automation", "heartbeat", "--domain", "intent-cli",
            "--repo", "J-Tech-Japan/intent-system", "--team", "intent-cli-dev", "--format", "json");
        Assert.Equal("cannot-determine", unknown.GetProperty("verdict").GetString());
        Assert.Contains("unrecorded-owner", unknown.GetProperty("reason").GetString(), StringComparison.Ordinal);
        Assert.NotEqual("operator", unknown.GetProperty("action_owner").GetString());
    }

    [Fact]
    public void MalformedStore_IsCannotDetermineInQueryStalledWorkAndHeartbeat_G596()
    {
        File.WriteAllText(workspace.StorePath, "{ not-json");

        var (queryExit, query) = workspace.Run(
            "operator-attention", "query", "--domain", "intent-cli", "--format", "json");
        Assert.Equal(1, queryExit);
        Assert.Equal("cannot-determine", query.GetProperty("status").GetString());

        var stalled = AutomationStalledWorkCommand.Analyze(
            workspace.Context, "intent-cli", "J-Tech-Japan/intent-system", staleMinutes: 45);
        var item = Assert.Single(stalled.Items);
        Assert.Equal("operator-attention-cannot-determine", item.Kind);
        Assert.Equal("operator", item.RequiredActor);
        Assert.False(item.OrchestratorActionable);
        Assert.Equal("cannot-determine", stalled.OperatorAttentionStatus);

        var (_, heartbeat) = workspace.Run(
            "automation", "heartbeat", "--domain", "intent-cli",
            "--repo", "J-Tech-Japan/intent-system", "--format", "json");
        Assert.True(heartbeat.GetProperty("stale").GetBoolean());
        Assert.Equal("operator", heartbeat.GetProperty("route_to").GetString());
        Assert.Equal("cannot-determine", heartbeat.GetProperty("operator_attention_status").GetString());
    }

    [Fact]
    public void AtomicWriterInterruption_PreservesPriorParseableLifecycleAndCleansTemp_G596()
    {
        Assert.Equal(0, workspace.Open("release-091", write: true).ExitCode);
        var before = File.ReadAllText(workspace.StorePath);
        using var hook = AtomicFileWriter.RegisterBeforeMoveHook(
            workspace.StorePath,
            _ => throw new IOException("controlled interruption"));

        Assert.Throws<IOException>(() => workspace.Run(
            "operator-attention", "resolve", "--record", "release-091",
            "--resolution-evidence", "not published", "--write", "--format", "json"));

        Assert.Equal(before, File.ReadAllText(workspace.StorePath));
        using var document = JsonDocument.Parse(File.ReadAllText(workspace.StorePath));
        Assert.Equal("open", document.RootElement.GetProperty("records")[0].GetProperty("status").GetString());
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(workspace.StorePath)!, ".operator-attention.json.*.tmp"));
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void DeveloperReference_DocumentsExplicitLifecycleAndNoInference_G596(string language)
    {
        var content = File.ReadAllText(Path.Combine(
            RepoVersionPolicySource.RepoRoot(), "docs", language, "09-developer-reference.md"));

        Assert.Contains("judgment-wait open", content, StringComparison.Ordinal);
        Assert.Contains("judgment-wait resolve", content, StringComparison.Ordinal);
        Assert.Contains("judgment-wait supersede", content, StringComparison.Ordinal);
        Assert.Contains("judgment-wait query", content, StringComparison.Ordinal);
        Assert.Contains("operator-attention", content, StringComparison.Ordinal);
        Assert.Contains("deprecation_warning", content, StringComparison.Ordinal);
        Assert.Contains("operator-attention-pending", content, StringComparison.Ordinal);
        Assert.Contains("cannot-determine", content, StringComparison.Ordinal);
        Assert.Contains("events.jsonl", content, StringComparison.Ordinal);
    }

    private static void AssertEquivalentExceptDeprecationWarning(JsonElement canonical, JsonElement alias)
    {
        Assert.False(canonical.TryGetProperty("deprecation_warning", out _));
        AssertDeprecatedAliasWarning(alias);

        var aliasNode = JsonNode.Parse(alias.GetRawText())!.AsObject();
        Assert.True(aliasNode.Remove("deprecation_warning"));
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(canonical.GetRawText()), aliasNode));
    }

    private static void AssertDeprecatedAliasWarning(JsonElement result)
    {
        var warning = result.GetProperty("deprecation_warning");
        Assert.Equal("judgment-wait", warning.GetProperty("replacement").GetString());
        Assert.Equal("next-major", warning.GetProperty("removal").GetString());
        Assert.Contains("operator-attention", warning.GetProperty("message").GetString(), StringComparison.Ordinal);
    }

    private sealed class EmptyLister : IGitHubAutomationCandidateLister
    {
        public IReadOnlyList<GitHubAutomationPrCandidate> ListPullRequests(
            string repo, IReadOnlyCollection<string> requiredLabels) => Array.Empty<GitHubAutomationPrCandidate>();

        public IReadOnlyList<GitHubAutomationIssueCandidate> ListIssues(
            string repo, IReadOnlyCollection<string> requiredLabels) => Array.Empty<GitHubAutomationIssueCandidate>();
    }

    private sealed class Workspace : IDisposable
    {
        public Workspace()
        {
            RootPath = Directory.CreateTempSubdirectory("operator-attention-g596-").FullName;
            Directory.CreateDirectory(Path.Combine(RootPath, ".intent-cli"));
            Context = new CliContext
            {
                RepoRoot = RootPath,
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

        public string RootPath { get; }
        public CliContext Context { get; }
        public string StorePath => Path.Combine(RootPath, ".intent-cli", "operator-attention.json");

        public (int ExitCode, JsonElement Result) Open(string record, bool write, string? supersedes = null)
        {
            var args = new List<string>
            {
                "operator-attention", "open", "--record", record,
                "--domain", "intent-cli", "--team", "intent-cli-dev",
                "--owner", "release-operator", "--blocking-reference", "release:v0.9.1",
                "--action-needed", "Approve and publish v0.9.1",
                "--evidence", "release candidate checks are green",
            };
            if (supersedes is not null)
            {
                args.AddRange(["--supersedes", supersedes]);
            }
            args.Add(write ? "--write" : "--dry-run");
            args.AddRange(["--format", "json"]);
            return Run(args.ToArray());
        }

        public (int ExitCode, JsonElement Result) OpenScoped(string record, string domain, string team)
        {
            return Run(
                "operator-attention", "open", "--record", record,
                "--domain", domain, "--team", team, "--owner", "operator",
                "--blocking-reference", $"unit:{record}", "--action-needed", $"Decide {record}",
                "--evidence", $"Evidence for {record}", "--write", "--format", "json");
        }

        public (int ExitCode, JsonElement Result) OpenOwned(string record, string owner, bool write)
        {
            return Run("operator-attention", "open", "--record", record,
                "--domain", "intent-cli", "--team", "intent-cli-dev", "--owner", owner,
                "--blocking-reference", $"design:{record}", "--action-needed", "Record a design ruling",
                "--evidence", "implementation is blocked", write ? "--write" : "--dry-run", "--format", "json");
        }

        public void WriteTopology()
        {
            var path = NotifyRoleTopologyStore.ResolvePath(RootPath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(new
            {
                team = "intent-cli-dev", workspace_id = "workspace", roles = new Dictionary<string, object>
                {
                    ["design"] = new { resident = "herdr", workspace_id = "workspace", pane_id = "workspace:p1" },
                    ["orchestration"] = new { resident = "herdr", workspace_id = "workspace", pane_id = "workspace:p2" },
                },
            }));
        }

        public void RecordAgmsgMode()
        {
            using var writer = new StringWriter();
            var exitCode = SessionLayerCommand.ExecuteSet(
                Context,
                ["--domain", "intent-cli", "--team", "intent-cli-dev", "--mode", "agmsg", "--write", "--format", "json"],
                writer);
            Assert.True(exitCode == 0, writer.ToString());
        }

        public (int ExitCode, JsonElement Result) Run(params string[] args)
        {
            using var writer = new StringWriter();
            var exitCode = CommandRouter.Execute(args, Context, writer);
            return (exitCode, JsonDocument.Parse(writer.ToString()).RootElement.Clone());
        }

        public void Dispose()
        {
            if (Directory.Exists(RootPath))
            {
                Directory.Delete(RootPath, recursive: true);
            }
        }
    }
}
