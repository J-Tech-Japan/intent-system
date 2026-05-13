using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G344: Command-surface tests for
/// <see cref="AutomationHostQueueItemRecoveryCommand"/>. Mirrors the
/// reflection-via-non-generic-IDictionary trick used in G343.
/// </summary>
public sealed class AutomationHostQueueItemRecoveryCommandTests : IDisposable
{
    private const string Repo = "J-Tech-Japan/SekibanAsAService";
    private const string Unit = "SKS-G263";
    private const int IssueNumber = 646;
    private const int PrNumber = 647;

    public AutomationHostQueueItemRecoveryCommandTests()
    {
        AutomationHostQueueItemRecoveryCommand.PrLookupFactory = null;
        AutomationHostQueueItemRecoveryCommand.IssueLookupFactory = null;
    }

    public void Dispose()
    {
        AutomationHostQueueItemRecoveryCommand.PrLookupFactory = null;
        AutomationHostQueueItemRecoveryCommand.IssueLookupFactory = null;
    }

    [Fact]
    public void Execute_HappyPath_DryRun_ReportsRecoverableRepairScopedToRepo()
    {
        using var workspace = new RecoveryWorkspace();
        workspace.WritePublishArtifact(Unit, IssueNumber, Repo);
        workspace.WritePacketYaml(Unit, Repo);

        AutomationHostQueueItemRecoveryCommand.PrLookupFactory = () =>
            FakePrLookup.WithIssueClosure(PrNumber, IssueNumber, Repo);
        AutomationHostQueueItemRecoveryCommand.IssueLookupFactory = () =>
            FakeIssueLookup.Open(IssueNumber, labels: new[] { "intent-target", "intent-pr-created" }, repo: Repo);

        using var writer = new StringWriter();
        var exitCode = AutomationHostQueueItemRecoveryCommand.Execute(
            workspace.Context,
            ["--repo", Repo, "--pr", PrNumber.ToString(), "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.Equal("dry-run", doc.RootElement.GetProperty("mode").GetString());
        Assert.Equal(1, doc.RootElement.GetProperty("safe_repair_count").GetInt32());
        Assert.Equal(0, doc.RootElement.GetProperty("applied_count").GetInt32());

        var record = doc.RootElement.GetProperty("records")[0];
        Assert.Equal("recoverable", record.GetProperty("result").GetString());
        Assert.Equal(Unit, record.GetProperty("proposed_queue_item").GetProperty("unit_id").GetString());
        Assert.Equal(IssueNumber, record.GetProperty("proposed_queue_item").GetProperty("linked_issue").GetInt32());
        Assert.Equal(PrNumber, record.GetProperty("proposed_queue_item").GetProperty("linked_pr").GetInt32());
    }

    [Fact]
    public void Execute_Write_MutatesQueueStateAndReportsApplied()
    {
        using var workspace = new RecoveryWorkspace();
        workspace.WritePublishArtifact(Unit, IssueNumber, Repo);
        workspace.WritePacketYaml(Unit, Repo);
        // queue-state.json deliberately missing — this is exactly the
        // G344 fixture (queue lacks the item).

        AutomationHostQueueItemRecoveryCommand.PrLookupFactory = () =>
            FakePrLookup.WithIssueClosure(PrNumber, IssueNumber, Repo);
        AutomationHostQueueItemRecoveryCommand.IssueLookupFactory = () =>
            FakeIssueLookup.Open(IssueNumber, labels: new[] { "intent-target", "intent-pr-created" }, repo: Repo);

        using var writer = new StringWriter();
        var exitCode = AutomationHostQueueItemRecoveryCommand.Execute(
            workspace.Context,
            ["--repo", Repo, "--pr", PrNumber.ToString(), "--write", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.Equal("write", doc.RootElement.GetProperty("mode").GetString());
        Assert.Equal(1, doc.RootElement.GetProperty("applied_count").GetInt32());

        // Verify queue-state was actually written and now contains the
        // execution unit with the proposed bindings.
        var queueStatePath = workspace.FindQueueStatePath();
        Assert.NotNull(queueStatePath);
        var queueJson = File.ReadAllText(queueStatePath!);
        Assert.Contains(Unit, queueJson, StringComparison.Ordinal);
        Assert.Contains(PrNumber.ToString(), queueJson, StringComparison.Ordinal);
        Assert.Contains(IssueNumber.ToString(), queueJson, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_DryRun_DoesNotMutateQueueState()
    {
        using var workspace = new RecoveryWorkspace();
        workspace.WritePublishArtifact(Unit, IssueNumber, Repo);
        workspace.WritePacketYaml(Unit, Repo);

        AutomationHostQueueItemRecoveryCommand.PrLookupFactory = () =>
            FakePrLookup.WithIssueClosure(PrNumber, IssueNumber, Repo);
        AutomationHostQueueItemRecoveryCommand.IssueLookupFactory = () =>
            FakeIssueLookup.Open(IssueNumber, labels: new[] { "intent-target", "intent-pr-created" }, repo: Repo);

        using var writer = new StringWriter();
        var exitCode = AutomationHostQueueItemRecoveryCommand.Execute(
            workspace.Context,
            ["--repo", Repo, "--pr", PrNumber.ToString(), "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        Assert.Null(workspace.FindQueueStatePath());
    }

    [Fact]
    public void Execute_OtherRepoPublishArtifact_FilteredFromOutput()
    {
        using var workspace = new RecoveryWorkspace();
        // Same issue/pr numbers but URL points to a different repo —
        // must NOT appear in scoped results.
        workspace.WritePublishArtifact("OTHER-G001", IssueNumber, "other-org/other-repo");
        workspace.WritePacketYaml("OTHER-G001", "other-org/other-repo");
        // Plus the actual target repo unit.
        workspace.WritePublishArtifact(Unit, IssueNumber, Repo);
        workspace.WritePacketYaml(Unit, Repo);

        AutomationHostQueueItemRecoveryCommand.PrLookupFactory = () =>
            FakePrLookup.WithIssueClosure(PrNumber, IssueNumber, Repo);
        AutomationHostQueueItemRecoveryCommand.IssueLookupFactory = () =>
            FakeIssueLookup.Open(IssueNumber, labels: new[] { "intent-target", "intent-pr-created" }, repo: Repo);

        using var writer = new StringWriter();
        var exitCode = AutomationHostQueueItemRecoveryCommand.Execute(
            workspace.Context,
            ["--repo", Repo, "--pr", PrNumber.ToString(), "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        var records = doc.RootElement.GetProperty("records").EnumerateArray().ToArray();
        Assert.Single(records);
        Assert.Equal(Unit, records[0].GetProperty("unit_id").GetString());
        Assert.DoesNotContain("OTHER-G001", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_MissingRepo_ReturnsError()
    {
        using var workspace = new RecoveryWorkspace();

        using var writer = new StringWriter();
        var exitCode = AutomationHostQueueItemRecoveryCommand.Execute(
            workspace.Context,
            ["--format", "json"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("--repo", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_NoPrSupplied_SkipsArtifact_WithStructuredRecord()
    {
        // The recovery lane requires a specific PR to anchor the
        // proposed queue item; without --pr we cannot deterministically
        // attach a PR. Surface as `skipped` with a structured reason.
        using var workspace = new RecoveryWorkspace();
        workspace.WritePublishArtifact(Unit, IssueNumber, Repo);
        workspace.WritePacketYaml(Unit, Repo);

        AutomationHostQueueItemRecoveryCommand.PrLookupFactory = () => new FakePrLookup();
        AutomationHostQueueItemRecoveryCommand.IssueLookupFactory = () => new FakeIssueLookup();

        using var writer = new StringWriter();
        var exitCode = AutomationHostQueueItemRecoveryCommand.Execute(
            workspace.Context,
            ["--repo", Repo, "--format", "json"],
            writer);

        // Dry-run with neither safe repairs nor unsafe-stops → exit 0.
        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.Equal(0, doc.RootElement.GetProperty("safe_repair_count").GetInt32());
        var records = doc.RootElement.GetProperty("records").EnumerateArray().ToArray();
        Assert.Single(records);
        Assert.Equal("skipped", records[0].GetProperty("result").GetString());
        Assert.Equal("no-pr-supplied", records[0].GetProperty("reason_code").GetString());
    }

    [Fact]
    public void Execute_ConflictingExistingQueueItem_UnsafeStop_NoApply()
    {
        using var workspace = new RecoveryWorkspace();
        workspace.WritePublishArtifact(Unit, IssueNumber, Repo);
        workspace.WritePacketYaml(Unit, Repo);
        // Existing queue-state already binds a DIFFERENT unit to the
        // same PR — must surface conflicting-existing-queue-item.
        workspace.WriteQueueStateLegacy($$"""
            {
              "schema_version": "1",
              "updated_at": "2026-05-13T00:00:00Z",
              "items": [
                {
                  "execution_unit": "SKS-OTHER",
                  "title": "other",
                  "state": "review",
                  "dependencies": [],
                  "blocked_by": [],
                  "clarification_return_path": "intents/sekiban-as-a-service/clarifications/open.md",
                  "packet_paths": {"implementation": "a", "review_context": "b", "yaml": "c"},
                  "linked_issue": {"repo": "{{Repo}}", "number": 999, "url": "https://github.com/{{Repo}}/issues/999"},
                  "linked_pr": "https://github.com/{{Repo}}/pull/{{PrNumber}}",
                  "worker_role": "coder",
                  "review_role": "reviewer",
                  "priority": "normal"
                }
              ]
            }
            """);

        AutomationHostQueueItemRecoveryCommand.PrLookupFactory = () =>
            FakePrLookup.WithIssueClosure(PrNumber, IssueNumber, Repo);
        AutomationHostQueueItemRecoveryCommand.IssueLookupFactory = () =>
            FakeIssueLookup.Open(IssueNumber, labels: new[] { "intent-target", "intent-pr-created" }, repo: Repo);

        using var writer = new StringWriter();
        var exitCode = AutomationHostQueueItemRecoveryCommand.Execute(
            workspace.Context,
            ["--repo", Repo, "--pr", PrNumber.ToString(), "--write", "--format", "json"],
            writer);

        Assert.Equal(1, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.Equal(0, doc.RootElement.GetProperty("applied_count").GetInt32());
        Assert.True(doc.RootElement.GetProperty("unsafe_stop_count").GetInt32() >= 1);
        var record = doc.RootElement.GetProperty("records").EnumerateArray()
            .First(r => string.Equals(r.GetProperty("result").GetString(), "unsafe-stop", StringComparison.Ordinal));
        Assert.Equal("conflicting-existing-queue-item", record.GetProperty("reason_code").GetString());
    }

    [Fact]
    public void Execute_IgnoresUnrelatedRepoQueueItemsWithSameIssueOrPrNumber()
    {
        // Regression for G344 review finding 1: queue-state has an item
        // for an unrelated repo with the SAME issue/PR numbers as the
        // recovery request. The unrelated-repo item must be filtered out
        // before reaching the analyzer's ExistingQueueItems, so it cannot
        // trigger `conflicting-existing-queue-item`.
        using var workspace = new RecoveryWorkspace();
        workspace.WritePublishArtifact(Unit, IssueNumber, Repo);
        workspace.WritePacketYaml(Unit, Repo);
        const string otherRepo = "other-org/other-repo";
        workspace.WriteQueueStateLegacy($$"""
            {
              "schema_version": "1",
              "updated_at": "2026-05-13T00:00:00Z",
              "items": [
                {
                  "execution_unit": "OTHER-G001",
                  "title": "unrelated repo, same numbers",
                  "state": "review",
                  "dependencies": [],
                  "blocked_by": [],
                  "clarification_return_path": "intents/sekiban-as-a-service/clarifications/open.md",
                  "packet_paths": {"implementation": "a", "review_context": "b", "yaml": "c"},
                  "linked_issue": {"repo": "{{otherRepo}}", "number": {{IssueNumber}}, "url": "https://github.com/{{otherRepo}}/issues/{{IssueNumber}}"},
                  "linked_pr": "https://github.com/{{otherRepo}}/pull/{{PrNumber}}",
                  "worker_role": "coder",
                  "review_role": "reviewer",
                  "priority": "normal"
                }
              ]
            }
            """);

        AutomationHostQueueItemRecoveryCommand.PrLookupFactory = () =>
            FakePrLookup.WithIssueClosure(PrNumber, IssueNumber, Repo);
        AutomationHostQueueItemRecoveryCommand.IssueLookupFactory = () =>
            FakeIssueLookup.Open(IssueNumber, labels: new[] { "intent-target", "intent-pr-created" }, repo: Repo);

        using var writer = new StringWriter();
        var exitCode = AutomationHostQueueItemRecoveryCommand.Execute(
            workspace.Context,
            ["--repo", Repo, "--pr", PrNumber.ToString(), "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.Equal(1, doc.RootElement.GetProperty("safe_repair_count").GetInt32());
        Assert.Equal(0, doc.RootElement.GetProperty("unsafe_stop_count").GetInt32());
        var records = doc.RootElement.GetProperty("records").EnumerateArray().ToArray();
        Assert.Single(records);
        var record = records[0];
        Assert.Equal("recoverable", record.GetProperty("result").GetString());
        Assert.Equal(Unit, record.GetProperty("unit_id").GetString());
        Assert.Equal(Repo, record.GetProperty("proposed_queue_item").GetProperty("target_repo").GetString());
        var recommended = record.GetProperty("recommended_command").GetString();
        Assert.NotNull(recommended);
        Assert.Contains("--repo " + Repo, recommended!, StringComparison.Ordinal);
        Assert.DoesNotContain("conflicting-existing-queue-item", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_FailedIssueLookup_SurfacesUnsafeStop_NoFabricatedIssue()
    {
        // G344 second-round repair: when the GitHub issue lookup fails
        // (PR closes #N but we cannot retrieve that issue) the command
        // MUST emit a structured unsafe stop with reason
        // `issue-lookup-failed`. Previously it fabricated an open issue
        // with empty labels, which silently bypassed the published-issue
        // label contract.
        using var workspace = new RecoveryWorkspace();
        workspace.WritePublishArtifact(Unit, IssueNumber, Repo);
        workspace.WritePacketYaml(Unit, Repo);

        AutomationHostQueueItemRecoveryCommand.PrLookupFactory = () =>
            FakePrLookup.WithIssueClosure(PrNumber, IssueNumber, Repo);
        // FakeIssueLookup with no map entry → GetIssue throws → command
        // sees lookup failure.
        AutomationHostQueueItemRecoveryCommand.IssueLookupFactory = () => new FakeIssueLookup();

        using var writer = new StringWriter();
        var exitCode = AutomationHostQueueItemRecoveryCommand.Execute(
            workspace.Context,
            ["--repo", Repo, "--pr", PrNumber.ToString(), "--format", "json"],
            writer);

        Assert.Equal(1, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.Equal(0, doc.RootElement.GetProperty("safe_repair_count").GetInt32());
        Assert.Equal(0, doc.RootElement.GetProperty("applied_count").GetInt32());
        Assert.True(doc.RootElement.GetProperty("unsafe_stop_count").GetInt32() >= 1);
        var records = doc.RootElement.GetProperty("records").EnumerateArray().ToArray();
        Assert.Single(records);
        Assert.Equal("unsafe-stop", records[0].GetProperty("result").GetString());
        Assert.Equal(
            AutomationHostQueueItemRecoveryCommand.ReasonIssueLookupFailed,
            records[0].GetProperty("reason_code").GetString());
        Assert.Equal("issue-lookup-failed", records[0].GetProperty("reason_code").GetString());
        // No proposed queue item — the command must not advance toward
        // recovery without verified issue evidence.
        Assert.False(records[0].TryGetProperty("proposed_queue_item", out var proposed)
            && proposed.ValueKind != JsonValueKind.Null);
    }

    [Fact]
    public void Execute_ParsesImplementationIssueSchema_DetectsRepoMismatch()
    {
        // G344 second-round repair: real published packets use the
        // canonical `implementation_issue:` section (not
        // `implementation_issue_packet:`). The recovery command must
        // parse that section so an explicit target_repo mismatch is
        // surfaced as `repo-mismatch`.
        using var workspace = new RecoveryWorkspace();
        workspace.WritePublishArtifact(Unit, IssueNumber, Repo);
        workspace.WritePacketYamlImplementationIssueSchema(Unit, targetRepo: "other-org/other-repo");

        AutomationHostQueueItemRecoveryCommand.PrLookupFactory = () =>
            FakePrLookup.WithIssueClosure(PrNumber, IssueNumber, Repo);
        AutomationHostQueueItemRecoveryCommand.IssueLookupFactory = () =>
            FakeIssueLookup.Open(IssueNumber, labels: new[] { "intent-target", "intent-pr-created" }, repo: Repo);

        using var writer = new StringWriter();
        var exitCode = AutomationHostQueueItemRecoveryCommand.Execute(
            workspace.Context,
            ["--repo", Repo, "--pr", PrNumber.ToString(), "--format", "json"],
            writer);

        Assert.Equal(1, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.Equal(0, doc.RootElement.GetProperty("safe_repair_count").GetInt32());
        Assert.True(doc.RootElement.GetProperty("unsafe_stop_count").GetInt32() >= 1);
        var record = doc.RootElement.GetProperty("records").EnumerateArray()
            .First(r => string.Equals(r.GetProperty("result").GetString(), "unsafe-stop", StringComparison.Ordinal));
        Assert.Equal(
            HostQueueItemRecoveryAnalyzer.ReasonRepoMismatch,
            record.GetProperty("reason_code").GetString());
        Assert.Contains("other-org/other-repo", record.GetProperty("explanation").GetString()!, StringComparison.Ordinal);
    }

    [Fact]
    public void CommandRouter_HelpListsHostQueueItemRecovery()
    {
        var router = typeof(CommandRouter);
        var helpField = router.GetField("AutomationCommandHelp",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.NotNull(helpField);
        var lines = (System.Collections.Generic.IReadOnlyList<string>?)helpField!.GetValue(null);
        Assert.NotNull(lines);
        Assert.Contains(lines!, l => l.Contains("host-queue-item-recovery", StringComparison.Ordinal));
    }

    [Fact]
    public void CommandRouter_DispatchesHostQueueItemRecoveryToNewCommand()
    {
        var router = typeof(CommandRouter);
        var commandsField = router.GetField("ImplementedCommands",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.NotNull(commandsField);
        var groups = commandsField!.GetValue(null);
        Assert.NotNull(groups);

        var outer = (System.Collections.IDictionary)groups!;
        Assert.True(outer.Contains("automation"));
        var inner = (System.Collections.IDictionary)outer["automation"]!;
        Assert.True(inner.Contains("host-queue-item-recovery"));
        var handler = (Delegate)inner["host-queue-item-recovery"]!;
        Assert.Equal(
            typeof(AutomationHostQueueItemRecoveryCommand).GetMethod(
                "Execute",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)!,
            handler.Method);
    }

    private sealed class FakePrLookup : IGitHubPrLookup
    {
        private readonly Dictionary<int, GitHubPrLookupResult> map = new();

        public static FakePrLookup WithIssueClosure(int prNumber, int issueNumber, string repo)
        {
            var ownerRepo = repo.Split('/', 2);
            var view = new GitHubPrLookupResult
            {
                Number = prNumber,
                State = "OPEN",
                Title = $"PR #{prNumber}",
                Body = $"Closes #{issueNumber}",
                IsDraft = false,
                Closed = false,
                Merged = false,
                Labels = new[] { new GitHubPrLabel { Name = "intent-target" } },
                ClosingIssuesReferences = new[]
                {
                    new GitHubPrClosingIssueReference
                    {
                        Number = issueNumber,
                        Repository = new GitHubPrClosingIssueRepository
                        {
                            Name = ownerRepo.Length == 2 ? ownerRepo[1] : repo,
                            Owner = new GitHubPrClosingIssueRepositoryOwner
                            {
                                Login = ownerRepo.Length == 2 ? ownerRepo[0] : ""
                            }
                        }
                    }
                },
            };
            var lookup = new FakePrLookup();
            lookup.map[prNumber] = view;
            return lookup;
        }

        public GitHubPrLookupResult Lookup(string repo, int prNumber)
        {
            if (map.TryGetValue(prNumber, out var view))
            {
                return view;
            }
            throw new InvalidOperationException($"no fake PR for #{prNumber}");
        }
    }

    private sealed class FakeIssueLookup : IGitHubAutomationIssueLookup
    {
        private readonly Dictionary<int, GitHubAutomationIssueCandidate> map = new();

        public static FakeIssueLookup Open(int issueNumber, IReadOnlyList<string> labels, string repo)
        {
            var lookup = new FakeIssueLookup();
            lookup.map[issueNumber] = new GitHubAutomationIssueCandidate
            {
                Number = issueNumber,
                Title = $"Issue {issueNumber}",
                Url = $"https://github.com/{repo}/issues/{issueNumber}",
                CreatedAt = "2026-05-13T00:00:00Z",
                Labels = labels.Select(name => new GitHubAutomationLabel { Name = name }).ToArray(),
                State = "OPEN",
            };
            return lookup;
        }

        public GitHubAutomationIssueCandidate GetIssue(string repo, int issueNumber)
        {
            if (map.TryGetValue(issueNumber, out var candidate))
            {
                return candidate;
            }
            throw new InvalidOperationException($"no fake issue for #{issueNumber}");
        }
    }

    private sealed class RecoveryWorkspace : IDisposable
    {
        public RecoveryWorkspace()
        {
            RootPath = Directory.CreateTempSubdirectory("host-queue-recovery-tests-").FullName;
            Directory.CreateDirectory(Path.Combine(RootPath, ".intent-cli"));
            Context = new CliContext
            {
                RepoRoot = RootPath,
                Config = new CliConfig
                {
                    Project = new ProjectConfig
                    {
                        Domain = "sekiban-as-a-service",
                        ArtifactRoot = ".intent-cli",
                        WorktreeRoot = ".intent-cli/worktrees"
                    }
                }
            };
        }

        public string RootPath { get; }
        public CliContext Context { get; }

        public void WritePublishArtifact(string executionUnit, int createdIssueNumber, string repo)
        {
            var dir = Path.Combine(RootPath, ".intent-cli", "issues", executionUnit);
            Directory.CreateDirectory(dir);
            var artifact = new IssuePublishArtifact
            {
                ExecutionUnit = executionUnit,
                PublishStatus = "issue-created",
                PacketPath = $".intent-cli/issues/{executionUnit}/packet.yaml",
                IssueBodyPath = $".intent-cli/issues/{executionUnit}/github-body.md",
                CreatedIssueNumber = createdIssueNumber,
                CreatedIssueUrl = $"https://github.com/{repo}/issues/{createdIssueNumber}",
                PublishedLabelName = "intent-target",
            };
            File.WriteAllText(Path.Combine(dir, "publish.yaml"), IssuePublishArtifactYaml.Serialize(artifact));
        }

        public void WritePacketYaml(string executionUnit, string targetRepo)
        {
            var dir = Path.Combine(RootPath, ".intent-cli", "issues", executionUnit);
            Directory.CreateDirectory(dir);
            var yaml = $$"""
                implementation_issue_packet:
                  issue_title: "{{executionUnit}} TODO"
                  issue_kind: feature
                  source_execution_unit: {{executionUnit}}
                  domain: sekiban-as-a-service
                  target_repo: {{targetRepo}}
                  target_path: <paths>
                  target_part: "one-line"
                  dependencies: []
                  technical_baseline: []
                  intent_references: []
                  acceptance_criteria:
                    - "AC1"
                """;
            File.WriteAllText(Path.Combine(dir, "packet.yaml"), yaml);
        }

        /// <summary>
        /// G344 second-round repair fixture: writes a packet.yaml using
        /// the canonical <c>implementation_issue:</c> section (the schema
        /// actually used by published packets — see
        /// <c>IssuePublishCommandTests</c>), not the legacy
        /// <c>implementation_issue_packet:</c> alias.
        /// </summary>
        public void WritePacketYamlImplementationIssueSchema(string executionUnit, string targetRepo)
        {
            var dir = Path.Combine(RootPath, ".intent-cli", "issues", executionUnit);
            Directory.CreateDirectory(dir);
            var yaml = $$"""
                execution_unit: {{executionUnit}}
                implementation_issue:
                  issue_title: "[{{executionUnit}}] TODO"
                  target_repo: {{targetRepo}}
                  target_path: <paths>
                  target_part: "one-line"
                  dependencies: []
                """;
            File.WriteAllText(Path.Combine(dir, "packet.yaml"), yaml);
        }

        public void WriteQueueStateLegacy(string json)
        {
            var path = Path.Combine(RootPath, ".intent-cli", "queue-state.json");
            File.WriteAllText(path, json);
        }

        public string? FindQueueStatePath()
        {
            // Look in scoped or legacy locations.
            var legacy = Path.Combine(RootPath, ".intent-cli", "queue-state.json");
            if (File.Exists(legacy))
            {
                return legacy;
            }
            // Scoped: .intent-cli/runtime/<domain>/<owner>__<repo>/queue-state.json
            var runtime = Path.Combine(RootPath, ".intent-cli", "runtime");
            if (!Directory.Exists(runtime))
            {
                return null;
            }
            foreach (var file in Directory.EnumerateFiles(runtime, "queue-state.json", SearchOption.AllDirectories))
            {
                return file;
            }
            return null;
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
