using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

[Collection("WorkerNextActionSharedState")]
public sealed class PromptAdjudicationG683Tests : IDisposable
{
    private const string Domain = "g683-domain";
    private const string Team = "g683-team";
    private readonly string root = Directory.CreateTempSubdirectory("intent-g683-").FullName;
    private readonly DateTimeOffset now = DateTimeOffset.Parse("2026-08-12T20:00:00Z");

    public PromptAdjudicationG683Tests()
    {
        NotifyCommand.UtcNowFactory = () => now;
        NotifyPromptClassProducerRegistry.AvailabilityOverride = null;
    }

    [Fact]
    public void SeededDialog_EmitsWakesOrchestrationExecutesExactScopeAndAudits_G683()
    {
        var context = CreateContext();
        RecordMode(context);
        WriteTopology();
        RecordPolicy(context);
        var pendingWasDurableBeforeSend = false;
        var runner = new PromptRunner(
            "Allow GitHub to add a comment to a pull request?",
            beforeSendKeys: () =>
            {
                pendingWasDurableBeforeSend = NotifySupervisionStore.Read(
                    context.ResolveSupervisionArtifactRootPath(), Domain, Team).PromptAudits.Any(audit =>
                        audit.Outcome == "bounded-answer-execution-pending");
            });

        var pass = CreateSupervisor(context, runner).RunOnce();

        var finding = Assert.Single(pass.Findings, item => item.Kind == "observed-prompt");
        Assert.Equal("orchestration", finding.WakeTargetRole);
        Assert.Equal("bounded-prompt-answer", finding.WakeClass);
        Assert.Equal("codex", finding.Prompt!.AgentKind);
        Assert.Equal("github-comment-post", finding.Prompt.PromptClass);
        Assert.Equal("accept", finding.Prompt.Decision);
        Assert.Contains("always-allow", finding.Prompt.ExactAnswerScope, StringComparison.Ordinal);
        Assert.Contains(runner.Calls, call => call.Arguments.SequenceEqual(
            ["agent", "send-keys", "wG683:p2", "2", "enter"]));
        Assert.True(pendingWasDurableBeforeSend);
        Assert.Contains(runner.Calls, call =>
            call.Arguments.Take(3).SequenceEqual(["agent", "prompt", "wG683:p1"])
            && call.Arguments[3].Contains("github-comment-post", StringComparison.Ordinal)
            && call.Arguments[3].Contains("exact_answer_scope", StringComparison.Ordinal));
        Assert.DoesNotContain(runner.Calls, call =>
            call.Arguments.Take(3).SequenceEqual(["agent", "prompt", "wG683:p0"]));

        using (var jsonWriter = new StringWriter())
        {
            NotifyCommand.EmitSupervision(
                jsonWriter, pass, Domain, Team, 300, autoRedispatch: false, write: true, format: "json");
            using var output = JsonDocument.Parse(jsonWriter.ToString());
            var emitted = output.RootElement.GetProperty("findings").EnumerateArray()
                .Single(item => item.GetProperty("kind").GetString() == "observed-prompt")
                .GetProperty("observed_prompt");
            Assert.Equal("codex", emitted.GetProperty("agent_kind").GetString());
            Assert.Equal("wG683:p2", emitted.GetProperty("pane").GetString());
            Assert.Equal("github-comment-post", emitted.GetProperty("prompt_class").GetString());
            Assert.Equal("Allow GitHub to add a comment to a pull request?", emitted.GetProperty("observed_text").GetString());
        }
        using (var markdownWriter = new StringWriter())
        {
            NotifyCommand.EmitSupervision(
                markdownWriter, pass, Domain, Team, 300, autoRedispatch: false, write: true, format: "markdown");
            Assert.Contains("observed prompt: agent_kind=codex; pane=wG683:p2; prompt_class=github-comment-post", markdownWriter.ToString(), StringComparison.Ordinal);
            Assert.Contains("Allow GitHub to add a comment to a pull request?", markdownWriter.ToString(), StringComparison.Ordinal);
        }

        var state = NotifySupervisionStore.Read(
            context.ResolveSupervisionArtifactRootPath(), Domain, Team);
        Assert.True(state.Resolved, state.Error);
        Assert.Collection(
            state.PromptAudits.Where(audit => audit.PromptClass == "github-comment-post"),
            audit => AssertAudit(audit, "authorized-before-execution"),
            audit => AssertAudit(audit, "bounded-answer-execution-pending"),
            audit => AssertAudit(audit, "bounded-answer-executed"));
        Assert.Contains(runner.Calls, call => call.Arguments.SequenceEqual(
            ["agent", "read", "wG683:p2", "--source", "detection", "--lines", "200"]));
    }

    [Fact]
    public void UnknownDialog_IsLiteralUnknownEscalateOnlyAuditedAndNeverAnswered_G683()
    {
        var context = CreateContext();
        RecordMode(context);
        WriteTopology();
        RecordPolicy(context);
        var runner = new PromptRunner("A brand-new permission shape that is not in any recipe");

        var pass = CreateSupervisor(context, runner).RunOnce();

        var finding = Assert.Single(pass.Findings, item => item.Kind == "observed-prompt");
        Assert.Equal("unknown", finding.Prompt!.PromptClass);
        Assert.Equal("escalate", finding.Prompt.Decision);
        Assert.Null(finding.Prompt.ExactAnswerScope);
        Assert.Equal("orchestration", finding.WakeTargetRole);
        Assert.Equal("escalation", finding.WakeClass);
        Assert.DoesNotContain(runner.Calls, call => call.Arguments.Take(2).SequenceEqual(["agent", "send-keys"]));
        Assert.DoesNotContain(runner.Calls, call =>
            call.Arguments.Take(3).SequenceEqual(["agent", "prompt", "wG683:p0"]));

        var audit = Assert.Single(NotifySupervisionStore.Read(
            context.ResolveSupervisionArtifactRootPath(), Domain, Team).PromptAudits);
        Assert.Equal("review", audit.Seat);
        Assert.Equal("wG683:p2", audit.Pane);
        Assert.Equal("orchestration", audit.Actor);
        Assert.Equal("escalate-only", audit.Outcome);
        Assert.Equal("unknown", audit.PromptClass);
    }

    [Fact]
    public void StaleKnownDialogFollowedByCurrentUnknown_IsUnknownEscalateOnly_G683Repair()
    {
        var context = CreateContext();
        RecordMode(context);
        WriteTopology();
        RecordPolicy(context);
        var runner = new PromptRunner(
            "Allow GitHub to add a comment to a pull request?\n"
            + "1. Allow once\n"
            + "2. Always allow\n"
            + "A current unknown approval is now active");

        var finding = Assert.Single(
            CreateSupervisor(context, runner).RunOnce().Findings,
            item => item.Kind == "observed-prompt");

        Assert.Equal("unknown", finding.Prompt!.PromptClass);
        Assert.Equal("escalate", finding.Prompt.Decision);
        Assert.Null(finding.Prompt.ExactAnswerScope);
        Assert.DoesNotContain(runner.Calls, call =>
            call.Arguments.Take(2).SequenceEqual(["agent", "send-keys"]));
        Assert.Contains(runner.Calls, call => call.Arguments.SequenceEqual(
            ["agent", "read", "wG683:p2", "--source", "detection", "--lines", "200"]));
    }

    [Theory]
    [InlineData("codex", "Do you trust the authors of the files in this folder?", "launch-hook-trust", "codex:launch-hook-trust")]
    [InlineData("copilot", "Enable all permissions (recommended)\nContinue with limited permissions\nCancel", "launch-limited-permissions", "unmatched")]
    public void MatchedPreEscalateAndKnownUnmatched_AreAuditedEscalateOnly_G683(
        string agentKind,
        string text,
        string expectedClass,
        string expectedRule)
    {
        var context = CreateContext();
        RecordMode(context);
        WriteTopology(agentKind);
        RecordPolicy(context);
        var runner = new PromptRunner(text, agentKind);

        var finding = Assert.Single(
            CreateSupervisor(context, runner).RunOnce().Findings,
            item => item.Kind == "observed-prompt");

        Assert.Equal(expectedClass, finding.Prompt!.PromptClass);
        Assert.Equal("escalate", finding.Prompt.Decision);
        Assert.Equal(expectedRule, finding.Prompt.Rule);
        Assert.Null(finding.Prompt.ExactAnswerScope);
        Assert.DoesNotContain(runner.Calls, call => call.Arguments.Take(2).SequenceEqual(["agent", "send-keys"]));
        var audit = Assert.Single(NotifySupervisionStore.Read(
            context.ResolveSupervisionArtifactRootPath(), Domain, Team).PromptAudits);
        Assert.Equal("escalate-only", audit.Outcome);
        Assert.Equal(expectedRule, audit.Rule);
    }

    [Fact]
    public void RulesValidateAgainstRecipeVocabularyAndCoveredKindsClearG682_G683()
    {
        Assert.True(NotifyPreApprovalPolicyStore.TryParseRule(
            "codex:does-not-exist", out var bogus));
        Assert.False(NotifyPreApprovalPolicyStore.TryValidateRule(bogus!, out var error));
        Assert.Contains("codex:github-comment-post", error, StringComparison.Ordinal);
        Assert.Contains("copilot:launch-limited-permissions", error, StringComparison.Ordinal);

        Assert.True(NotifyPreApprovalPolicyStore.TryParseRule(
            "codex:github-comment-post", out var valid));
        Assert.True(NotifyPreApprovalPolicyStore.TryValidateRule(valid!, out _));
        var policy = NotifyPreApprovalPolicyStore.WithCurrentApplicability(new NotifyPreApprovalPolicy
        {
            Domain = Domain,
            Team = Team,
            RecordedAt = now,
            Accept = [valid!],
            Escalate = [new NotifyPreApprovalRule { AgentKind = "codex", PromptClass = "launch-hook-trust" }],
        });
        Assert.True(policy.Applicable);
        Assert.All(policy.Accept.Concat(policy.Escalate), rule => Assert.True(rule.Applicable));

        using var writer = new StringWriter();
        var exit = CommandRouter.Execute(
            [
                "notify", "supervise", "--domain", Domain, "--team", Team,
                "--routing-root", root, "--once", "--write", "--format", "json",
                "--pre-approve", "codex:does-not-exist",
                "--pre-escalate", "codex:launch-hook-trust",
            ],
            CreateContext(),
            writer);
        Assert.Equal(1, exit);
        Assert.Contains("Known values", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void ConflictingApproveAndEscalatePair_IsRejectedAndEscalationWinsForLegacyPolicy_G683Repair()
    {
        var context = CreateContext();
        using (var writer = new StringWriter())
        {
            var exit = CommandRouter.Execute(
                [
                    "notify", "supervise", "--domain", Domain, "--team", Team,
                    "--routing-root", root, "--once", "--write", "--format", "json",
                    "--pre-approve", "codex:github-comment-post",
                    "--pre-escalate", "codex:github-comment-post",
                ],
                context,
                writer);
            Assert.Equal(1, exit);
            Assert.Contains("cannot be recorded in both", writer.ToString(), StringComparison.Ordinal);
            Assert.Contains("codex:github-comment-post", writer.ToString(), StringComparison.Ordinal);
        }

        var conflicting = NotifyPreApprovalPolicyStore.WithCurrentApplicability(new NotifyPreApprovalPolicy
        {
            Domain = Domain,
            Team = Team,
            RecordedAt = now,
            Accept = [new NotifyPreApprovalRule { AgentKind = "codex", PromptClass = "github-comment-post" }],
            Escalate = [new NotifyPreApprovalRule { AgentKind = "codex", PromptClass = "github-comment-post" }],
        });
        var refused = NotifyPreApprovalPolicyStore.Record(
            context.ResolveSupervisionArtifactRootPath(), conflicting, write: true);
        Assert.False(refused.Applied);
        Assert.Contains("cannot be recorded in both", refused.Error, StringComparison.Ordinal);
        Assert.Equal("escalate", NotifyPreApprovalPolicyStore.Adjudicate(
            conflicting, "codex", "github-comment-post"));

        RecordMode(context);
        WriteTopology();
        var policyPath = NotifyPreApprovalPolicyStore.ResolvePath(
            context.ResolveSupervisionArtifactRootPath(), Domain, Team);
        Directory.CreateDirectory(Path.GetDirectoryName(policyPath)!);
        File.WriteAllText(policyPath, JsonSerializer.Serialize(conflicting));
        var runner = new PromptRunner("Allow GitHub to add a comment to a pull request?");

        var finding = Assert.Single(
            CreateSupervisor(context, runner).RunOnce().Findings,
            item => item.Kind == "observed-prompt");
        Assert.Equal("escalate", finding.Prompt!.Decision);
        Assert.Equal("codex:github-comment-post", finding.Prompt.Rule);
        Assert.DoesNotContain(runner.Calls, call =>
            call.Arguments.Take(2).SequenceEqual(["agent", "send-keys"]));
    }

    [Fact]
    public void FinalAuditFailure_LeavesDurablePendingAndReconcilesWithoutRetry_G683Repair()
    {
        var context = CreateContext();
        RecordMode(context);
        WriteTopology();
        RecordPolicy(context);
        var pendingWasDurableBeforeSend = false;
        var runner = new PromptRunner(
            "Allow GitHub to add a comment to a pull request?",
            beforeSendKeys: () =>
            {
                pendingWasDurableBeforeSend = NotifySupervisionStore.Read(
                    context.ResolveSupervisionArtifactRootPath(), Domain, Team).PromptAudits.Any(audit =>
                        audit.Outcome == "bounded-answer-execution-pending");
            });
        NotifySupervisionStore.WriteOverride = (path, line) =>
        {
            if (line.Contains("\"outcome\":\"bounded-answer-executed\"", StringComparison.Ordinal))
            {
                return new NotifySupervisionWriteResult(false, false, path, "injected final audit failure");
            }
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.AppendAllText(path, line);
            return new NotifySupervisionWriteResult(true, false, path, null);
        };

        var first = CreateSupervisor(context, runner).RunOnce();

        var firstFinding = Assert.Single(first.Findings, item => item.Kind == "observed-prompt");
        Assert.Equal("prompt-final-audit-write-failed", firstFinding.Cause);
        Assert.Single(runner.Calls, call =>
            call.Arguments.Take(2).SequenceEqual(["agent", "send-keys"]));
        Assert.True(pendingWasDurableBeforeSend);
        var pendingState = NotifySupervisionStore.Read(
            context.ResolveSupervisionArtifactRootPath(), Domain, Team);
        Assert.Contains(pendingState.PromptAudits, audit =>
            audit.Outcome == "authorized-before-execution");
        var pending = Assert.Single(pendingState.PromptAudits, audit =>
            audit.Outcome == "bounded-answer-execution-pending");
        Assert.False(string.IsNullOrWhiteSpace(pending.AttemptId));
        Assert.DoesNotContain(pendingState.PromptAudits, audit =>
            audit.Outcome == "bounded-answer-executed");

        NotifySupervisionStore.WriteOverride = null;
        var second = CreateSupervisor(context, runner).RunOnce();

        var reconciledFinding = Assert.Single(second.Findings, item => item.Kind == "observed-prompt");
        Assert.Equal("escalate", reconciledFinding.Prompt!.Decision);
        Assert.Single(runner.Calls, call =>
            call.Arguments.Take(2).SequenceEqual(["agent", "send-keys"]));
        var reconciledState = NotifySupervisionStore.Read(
            context.ResolveSupervisionArtifactRootPath(), Domain, Team);
        var reconciliation = Assert.Single(reconciledState.PromptAudits, audit =>
            audit.Outcome == "bounded-answer-outcome-unknown-reconciliation-required");
        Assert.Equal(pending.AttemptId, reconciliation.AttemptId);
    }

    [Fact]
    public void RegistryAndDocsExposeReviewablePreviewContractInEnglishAndJapanese_G683()
    {
        var codex = Assert.IsType<AgentLaunchRecipe>(AgentLaunchRecipeRegistry.Find("codex"));
        Assert.Contains(codex.PromptClasses, item => item.PromptClass == "github-comment-post");
        Assert.Contains(codex.PromptClasses, item => item.PromptClass == "launch-hook-trust");
        Assert.Contains(Assert.IsType<AgentLaunchRecipe>(AgentLaunchRecipeRegistry.Find("copilot")).PromptClasses,
            item => item.PromptClass == "launch-limited-permissions");
        foreach (var language in new[] { "en", "ja" })
        {
            var doc = ReadRepoFile($"docs/{language}/12-agent-message-orchestration.md");
            var ledger = ReadRepoFile($"docs/{language}/1.0-compatibility-ledger.md");
            Assert.Contains("G683", doc, StringComparison.Ordinal);
            Assert.Contains("github-comment-post", doc, StringComparison.Ordinal);
            Assert.Contains("unknown", doc, StringComparison.Ordinal);
            Assert.Contains("G683", ledger, StringComparison.Ordinal);
            Assert.Contains("preview-through-1.x", ledger, StringComparison.Ordinal);
        }
    }

    private NotifyMeasuredSupervisor CreateSupervisor(CliContext context, PromptRunner runner) => new(
        context,
        root,
        Domain,
        Team,
        repo: null,
        ownerRole: "orchestration",
        intervalSeconds: 300,
        declaredBoundSeconds: null,
        staleMinutes: 45,
        claimedSilentMinutes: 720,
        backlogIdleMinutes: 45,
        repairSilentMinutes: 180,
        autoRedispatch: false,
        write: true,
        format: "json",
        runner,
        herdrExecutable: "fake-herdr",
        agmsgScriptsDirectory: "unused");

    private CliContext CreateContext() => new()
    {
        RepoRoot = root,
        Config = new CliConfig
        {
            Project = new ProjectConfig { Domain = Domain, ArtifactRoot = ".intent-cli" },
        },
    };

    private void RecordMode(CliContext context)
    {
        using var writer = new StringWriter();
        Assert.Equal(0, SessionLayerCommand.ExecuteSet(
            context,
            ["--domain", Domain, "--team", Team, "--mode", "herdr-only", "--write", "--format", "json"],
            writer));
    }

    private void RecordPolicy(CliContext context)
    {
        var result = NotifyPreApprovalPolicyStore.Record(
            context.ResolveSupervisionArtifactRootPath(),
            new NotifyPreApprovalPolicy
            {
                Domain = Domain,
                Team = Team,
                RecordedAt = now,
                Accept = [new NotifyPreApprovalRule { AgentKind = "codex", PromptClass = "github-comment-post" }],
                Escalate = [new NotifyPreApprovalRule { AgentKind = "codex", PromptClass = "launch-hook-trust" }],
            },
            write: true);
        Assert.True(result.Applied, result.Error);
    }

    private void WriteTopology(string agentKind = "codex")
    {
        var path = NotifyRoleTopologyStore.ResolvePath(root, Domain, Team);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(new
        {
            domain = Domain,
            team = Team,
            workspace_id = "wG683",
            roles = new Dictionary<string, object>
            {
                ["design"] = new { resident = "herdr", workspace_id = "wG683", pane_id = "wG683:p0", kind = agentKind },
                ["orchestration"] = new { resident = "herdr", workspace_id = "wG683", pane_id = "wG683:p1", kind = agentKind },
                ["review"] = new { resident = "herdr", workspace_id = "wG683", pane_id = "wG683:p2", kind = agentKind },
            },
        }));
    }

    private static void AssertAudit(NotifyPromptAudit audit, string outcome)
    {
        Assert.Equal("review", audit.Seat);
        Assert.Equal("wG683:p2", audit.Pane);
        Assert.Equal("codex", audit.AgentKind);
        Assert.Equal("codex:github-comment-post", audit.Rule);
        Assert.Equal("orchestration", audit.Actor);
        Assert.Equal(outcome, audit.Outcome);
    }

    private sealed class PromptRunner(
        string promptText,
        string agentKind = "codex",
        Action? beforeSendKeys = null) : INotifyProcessRunner
    {
        public List<(string FileName, IReadOnlyList<string> Arguments)> Calls { get; } = [];

        public NotifyProcessResult Run(string fileName, IReadOnlyList<string> arguments)
        {
            Calls.Add((fileName, arguments.ToArray()));
            if (arguments.Take(2).SequenceEqual(["agent", "send-keys"]))
            {
                beforeSendKeys?.Invoke();
            }
            if (arguments.SequenceEqual(["agent", "list"]))
            {
                return new NotifyProcessResult(0, JsonSerializer.Serialize(new
                {
                    result = new
                    {
                        agents = new object[]
                        {
                            new { name = "design", workspace_id = "wG683", pane_id = "wG683:p0", agent = agentKind, agent_session = new { id = "d" }, agent_status = "working", interactive_ready = true, state_change_seq = 1 },
                            new { name = "orchestration", workspace_id = "wG683", pane_id = "wG683:p1", agent = agentKind, agent_session = new { id = "o" }, agent_status = "working", interactive_ready = true, state_change_seq = 1 },
                            new { name = "review", workspace_id = "wG683", pane_id = "wG683:p2", agent = agentKind, agent_session = new { id = "r" }, agent_status = "blocked", interactive_ready = true, state_change_seq = 7 },
                        },
                    },
                }), string.Empty);
            }
            if (arguments.Take(3).SequenceEqual(["agent", "read", "wG683:p2"]))
            {
                return new NotifyProcessResult(0, promptText, string.Empty);
            }
            if (arguments.Take(2).SequenceEqual(["pane", "process-info"]))
            {
                return new NotifyProcessResult(0,
                    "{\"result\":{\"process_info\":{\"foreground_processes\":[]}}}", string.Empty);
            }
            return new NotifyProcessResult(0, string.Empty, string.Empty);
        }
    }

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

    public void Dispose()
    {
        NotifyCommand.UtcNowFactory = null;
        NotifyCommand.ProcessRunnerFactory = null;
        NotifyPromptClassProducerRegistry.AvailabilityOverride = null;
        NotifySupervisionStore.WriteOverride = null;
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }
}
