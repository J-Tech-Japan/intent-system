using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

[Collection(AutomationStalledWorkSharedStateCollection.Name)]
public sealed class HerdrTrialFindingsG582Tests : IDisposable
{
    private static readonly DateTimeOffset FixedNow = new(2026, 8, 2, 18, 0, 0, TimeSpan.Zero);
    private readonly string root = Directory.CreateTempSubdirectory("herdr-trial-g582-").FullName;

    public HerdrTrialFindingsG582Tests()
    {
        AutomationStalledWorkCommand.CandidateListerFactory = null;
        AutomationStalledWorkCommand.UtcNowFactory = () => FixedNow;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void F1_SwitchChecklistAndPointerResolveInBothSessionModes(bool herdrOnly)
    {
        var (context, markdown, json) = RenderGuide(herdrOnly);

        Assert.Equal(1, Count(markdown, SessionLayerSwitchChecklist.Heading));
        Assert.True(json.TryGetProperty(SessionLayerSwitchChecklist.JsonProperty, out var checklist));
        Assert.Equal(5, checklist.GetProperty("agmsg_to_herdr_only").GetArrayLength());
        Assert.Equal(5, checklist.GetProperty("herdr_only_to_agmsg").GetArrayLength());

        using var writer = new StringWriter();
        var exitCode = SessionLayerCommand.ExecuteShow(
            context,
            ["--domain", "intent-cli", "--team", "intent-cli-dev", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var show = JsonDocument.Parse(writer.ToString());
        var pointer = show.RootElement.GetProperty("switch_checklist").GetString();
        Assert.Contains($"`{SessionLayerSwitchChecklist.Heading}`", pointer, StringComparison.Ordinal);
        Assert.DoesNotContain("ships in G571", pointer, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void F2_AgmsgTeardownRemovesProjectHookAndDeliveryModeAndNamesObservedBlock(bool herdrOnly)
    {
        var (_, markdown, json) = RenderGuide(herdrOnly);
        var checklist = json.GetProperty(SessionLayerSwitchChecklist.JsonProperty);
        var teardown = checklist.GetProperty("agmsg_to_herdr_only")[1].GetString();

        Assert.Contains("per-project agmsg hook configuration", teardown, StringComparison.Ordinal);
        Assert.Contains("delivery mode", teardown, StringComparison.Ordinal);
        Assert.Contains("Turn off or remove", teardown, StringComparison.Ordinal);
        Assert.Contains("next-launch hook-trust screen", teardown, StringComparison.Ordinal);
        Assert.Contains("block the next Codex launch", teardown, StringComparison.Ordinal);
        Assert.Contains(teardown!, markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void F3_HerdrFocusDefaultHazardFailsClosedOnEmptyMappedIdAndReferencesG555()
    {
        var (_, markdown, json) = RenderGuide(herdrOnly: true);
        var operations = json.GetProperty(HerdrOnlyOperatingGuide.JsonProperty);
        var targetRule = operations.GetProperty("provisioning").GetProperty("target_id_rule").GetString();
        var recovery = string.Join('\n', operations.GetProperty("failure_recovery")
            .EnumerateArray().Select(value => value.GetString()));

        foreach (var token in new[]
                 {
                     "every provisioning or mutation command MUST resolve",
                     "explicit non-empty pane/workspace target id",
                     "recorded mapping",
                     "fail closed and DO NOT run the command",
                     "focus-default",
                     "currently focused pane in another team",
                     "G555",
                     "authoritative and unchanged",
                 })
        {
            Assert.Contains(token, markdown, StringComparison.Ordinal);
        }

        Assert.Contains("every provisioning/mutation command", targetRule, StringComparison.Ordinal);
        Assert.Contains("command does not run", targetRule, StringComparison.Ordinal);
        Assert.Contains("G555 attribution rules", targetRule, StringComparison.Ordinal);
        Assert.Contains("Focus-default cross-team mutation", recovery, StringComparison.Ordinal);
    }

    [Fact]
    public void F4_EveryEventsReaderUsesRestartDurableWatermarkAndFailsClosedWithoutReplay()
    {
        var (_, markdown, json) = RenderGuide(herdrOnly: true);
        var eventsJson = json.GetProperty(HerdrOnlyOperatingGuide.JsonProperty).GetProperty("events_jsonl");
        var invariant = eventsJson.GetProperty("watermark_invariant").GetString();

        Assert.Contains("file identity, byte offset, and complete-line count", invariant, StringComparison.Ordinal);
        Assert.Contains("across watcher restarts", invariant, StringComparison.Ordinal);
        Assert.Contains("replay can duplicate a design decision", invariant, StringComparison.Ordinal);

        foreach (var reader in eventsJson.GetProperty("readers").EnumerateObject())
        {
            var recipe = reader.Value.GetString()!;
            foreach (var token in new[]
                     {
                         "durable",
                         "restart",
                         "file-identity/byte-offset/complete-line-count",
                         "rotation",
                         "truncation",
                         "backwards",
                         "file replacement",
                         "fail closed",
                         "never reset to the beginning",
                     })
            {
                Assert.Contains(token, recipe, StringComparison.OrdinalIgnoreCase);
            }
        }

        Assert.Contains("NEVER silently resets to the beginning", markdown, StringComparison.Ordinal);
        Assert.Contains("replay can duplicate a design decision", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void F5_ApprovedOpenPastThresholdIsActionableAndHeartbeatCarriesIt()
    {
        var context = CreateStalledWorkContext();
        WritePacket("G582");
        var issue = BuildIssue(1267, "G582: Close herdr trial findings", "OPEN", "intent-target", "intent-pr-created");
        var pr = BuildPr(1268, "G582: Close herdr trial findings", "OPEN", 1267,
            FixedNow.AddMinutes(-90), false, "intent-pr-approved");
        AutomationStalledWorkCommand.CandidateListerFactory = () => new FakeLister([issue], [pr]);

        using var stalledWriter = new StringWriter();
        var stalledExit = AutomationStalledWorkCommand.Execute(
            context,
            ["--domain", "intent-cli", "--repo", "J-Tech-Japan/intent-system", "--stale-minutes", "45", "--format", "json"],
            stalledWriter);

        Assert.Equal(0, stalledExit);
        using var stalled = JsonDocument.Parse(stalledWriter.ToString());
        var item = Assert.Single(stalled.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal(AutomationStalledWorkCommand.KindApprovedNotMerged, item.GetProperty("kind").GetString());
        Assert.Equal(90, item.GetProperty("age_minutes").GetInt32());
        Assert.False(item.GetProperty("is_informational").GetBoolean());
        Assert.Contains("merge PR #1268", item.GetProperty("recommended_action").GetString(), StringComparison.Ordinal);
        Assert.Contains("closeout pr --pr 1268", item.GetProperty("recommended_action").GetString(), StringComparison.Ordinal);

        using var heartbeatWriter = new StringWriter();
        var heartbeatExit = AutomationHeartbeatCommand.Execute(
            context,
            ["--domain", "intent-cli", "--repo", "J-Tech-Japan/intent-system", "--stale-minutes", "45", "--format", "json"],
            heartbeatWriter);

        Assert.Equal(0, heartbeatExit);
        using var heartbeat = JsonDocument.Parse(heartbeatWriter.ToString());
        Assert.Contains(
            AutomationStalledWorkCommand.KindApprovedNotMerged,
            heartbeat.RootElement.GetProperty("message_body").GetString(),
            StringComparison.Ordinal);
        Assert.Equal(
            AutomationStalledWorkCommand.KindApprovedNotMerged,
            Assert.Single(heartbeat.RootElement.GetProperty("items").EnumerateArray()).GetProperty("kind").GetString());
    }

    [Fact]
    public void F5_MergedClosedDraftRequestUpdateAndGithubBlockedCandidatesStaySilent()
    {
        var context = CreateStalledWorkContext();
        var issues = new List<GitHubAutomationIssueCandidate>();
        var prs = new List<GitHubAutomationPrCandidate>();

        AddCandidate("G583", 1280, 1281, "MERGED", isDraft: false, issueBlocked: false, ["intent-pr-approved"]);
        AddCandidate("G584", 1282, 1283, "CLOSED", isDraft: false, issueBlocked: false, ["intent-pr-approved"]);
        AddCandidate("G585", 1284, 1285, "OPEN", isDraft: true, issueBlocked: false,
            ["intent-pr-approved", "intent-pr-request-update"]);
        AddCandidate("G586", 1286, 1287, "OPEN", isDraft: false, issueBlocked: true, ["intent-pr-approved"]);

        AutomationStalledWorkCommand.CandidateListerFactory = () => new FakeLister(issues, prs);
        using var writer = new StringWriter();
        var exitCode = AutomationStalledWorkCommand.Execute(
            context,
            ["--domain", "intent-cli", "--repo", "J-Tech-Japan/intent-system", "--stale-minutes", "45", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var result = JsonDocument.Parse(writer.ToString());
        Assert.DoesNotContain(
            result.RootElement.GetProperty("items").EnumerateArray(),
            item => item.GetProperty("kind").GetString() == AutomationStalledWorkCommand.KindApprovedNotMerged);

        void AddCandidate(
            string unit,
            int issueNumber,
            int prNumber,
            string prState,
            bool isDraft,
            bool issueBlocked,
            string[] prLabels)
        {
            WritePacket(unit);
            var issueLabels = issueBlocked
                ? new[] { "intent-target", "intent-pr-created", "intent-issue-blocked" }
                : new[] { "intent-target", "intent-pr-created" };
            issues.Add(BuildIssue(issueNumber, $"{unit}: exclusion", "OPEN", issueLabels));
            prs.Add(BuildPr(prNumber, $"{unit}: exclusion", prState, issueNumber,
                FixedNow.AddMinutes(-90), isDraft, prLabels));
        }
    }

    [Fact]
    public void F5_QueueBlockedUnitStaysSilent()
    {
        var context = CreateStalledWorkContext();
        WritePacket("G587");
        File.WriteAllText(
            context.GetQueueStatePath(),
            """
            {
              "schema_version": "1",
              "updated_at": "2026-08-02T16:00:00Z",
              "items": [
                {
                  "execution_unit": "G587",
                  "title": "G587 blocked",
                  "state": "blocked",
                  "dependencies": [],
                  "blocked_by": ["operator decision"],
                  "clarification_return_path": "",
                  "packet_paths": {"implementation":"a","review_context":"b","yaml":"c"},
                  "linked_issue": null,
                  "linked_pr": null,
                  "worker_role": "implementation",
                  "review_role": "review",
                  "priority": "normal"
                }
              ]
            }
            """);

        var issue = BuildIssue(1288, "G587: blocked", "OPEN", "intent-target", "intent-pr-created");
        var pr = BuildPr(1289, "G587: blocked", "OPEN", 1288,
            FixedNow.AddMinutes(-90), false, "intent-pr-approved");
        AutomationStalledWorkCommand.CandidateListerFactory = () => new FakeLister([issue], [pr]);

        using var writer = new StringWriter();
        AutomationStalledWorkCommand.Execute(
            context,
            ["--domain", "intent-cli", "--repo", "J-Tech-Japan/intent-system", "--stale-minutes", "45", "--format", "json"],
            writer);

        using var result = JsonDocument.Parse(writer.ToString());
        Assert.DoesNotContain(
            result.RootElement.GetProperty("items").EnumerateArray(),
            item => item.GetProperty("kind").GetString() == AutomationStalledWorkCommand.KindApprovedNotMerged);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void EnJaDocsPinAllFiveFindingsAndPreserveG581WakeContract(string language)
    {
        var content = File.ReadAllText(Path.Combine(
            RepoVersionPolicySource.RepoRoot(), "docs", language, "12-agent-message-orchestration.md"));

        foreach (var token in new[]
                 {
                     "Session-layer switch checklist",
                     "per-project agmsg hook configuration",
                     "delivery mode",
                     "next-launch hook-trust screen",
                     "focus-default",
                     "currently focused pane",
                     "G555",
                     "file identity",
                     "complete-line count",
                     "backwards",
                     "file replacement",
                     "approved-not-merged",
                     "merge",
                     "closeout pr",
                     "SECOND wake source",
                     "pane.agent_status_changed",
                     "intent-cli notify report",
                 })
        {
            Assert.Contains(token, content, StringComparison.OrdinalIgnoreCase);
        }
    }

    private (CliContext Context, string Markdown, JsonElement Json) RenderGuide(bool herdrOnly)
    {
        var guideRoot = Path.Combine(root, $"guide-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(guideRoot, ".intent-cli"));
        var context = ContextFor(guideRoot);
        if (herdrOnly)
        {
            File.WriteAllText(
                SessionLayerModeStore.ResolvePath(guideRoot),
                """
                {
                  "schema_version": "1",
                  "entries": [
                    {
                      "domain": "intent-cli",
                      "team": "intent-cli-dev",
                      "mode": "herdr-only",
                      "updated_at": "2026-08-02T12:00:00+00:00",
                      "transitions": [
                        { "from": "agmsg", "to": "herdr-only", "at": "2026-08-02T12:00:00+00:00" }
                      ]
                    }
                  ]
                }
                """);
        }

        var arguments = new[]
        {
            "guide", "orchestrator-thread", "--domain", "intent-cli", "--team", "intent-cli-dev",
            "--target-repo", "J-Tech-Japan/intent-system", "--agent", "claude",
        };
        using var markdownWriter = new StringWriter();
        Assert.Equal(0, CommandRouter.Execute([.. arguments, "--format", "markdown"], context, markdownWriter));
        using var jsonWriter = new StringWriter();
        Assert.Equal(0, CommandRouter.Execute([.. arguments, "--format", "json"], context, jsonWriter));
        using var json = JsonDocument.Parse(jsonWriter.ToString());
        return (context, markdownWriter.ToString(), json.RootElement.Clone());
    }

    private CliContext CreateStalledWorkContext()
    {
        Directory.CreateDirectory(Path.Combine(root, ".intent-cli"));
        return ContextFor(root);
    }

    private static CliContext ContextFor(string repoRoot) => new()
    {
        RepoRoot = repoRoot,
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

    private void WritePacket(string executionUnit)
    {
        var directory = Path.Combine(root, ".intent-cli", "issues", executionUnit);
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "packet.yaml"), $"domain: intent-cli\nsource_execution_unit: {executionUnit}\n");
    }

    private static GitHubAutomationIssueCandidate BuildIssue(
        int number,
        string title,
        string state,
        params string[] labels) => new()
        {
            Number = number,
            Title = title,
            Url = $"https://github.com/J-Tech-Japan/intent-system/issues/{number}",
            CreatedAt = FixedNow.AddHours(-4).ToString("O"),
            UpdatedAt = FixedNow.AddMinutes(-90).ToString("O"),
            State = state,
            Labels = labels.Select(name => new GitHubAutomationLabel { Name = name }).ToArray(),
        };

    private static GitHubAutomationPrCandidate BuildPr(
        int number,
        string title,
        string state,
        int closingIssue,
        DateTimeOffset updatedAt,
        bool isDraft,
        params string[] labels) => new()
        {
            Number = number,
            Title = title,
            Url = $"https://github.com/J-Tech-Japan/intent-system/pull/{number}",
            CreatedAt = FixedNow.AddHours(-4).ToString("O"),
            UpdatedAt = updatedAt.ToString("O"),
            State = state,
            IsDraft = isDraft,
            Labels = labels.Select(name => new GitHubAutomationLabel { Name = name }).ToArray(),
            ClosingIssuesReferences =
            [
                new GitHubPrClosingIssueReference
                {
                    Number = closingIssue,
                    Repository = new GitHubPrClosingIssueRepository
                    {
                        Name = "intent-system",
                        Owner = new GitHubPrClosingIssueRepositoryOwner { Login = "J-Tech-Japan" },
                    },
                },
            ],
        };

    private static int Count(string value, string token) =>
        (value.Length - value.Replace(token, string.Empty, StringComparison.Ordinal).Length) / token.Length;

    public void Dispose()
    {
        AutomationStalledWorkCommand.CandidateListerFactory = null;
        AutomationStalledWorkCommand.UtcNowFactory = null;
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private sealed class FakeLister(
        IReadOnlyList<GitHubAutomationIssueCandidate> issues,
        IReadOnlyList<GitHubAutomationPrCandidate> prs) : IGitHubAutomationCandidateLister
    {
        public IReadOnlyList<GitHubAutomationPrCandidate> ListPullRequests(
            string repo, IReadOnlyCollection<string> requiredLabels) => prs;

        public IReadOnlyList<GitHubAutomationIssueCandidate> ListIssues(
            string repo, IReadOnlyCollection<string> requiredLabels) => issues;
    }
}
