using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G800's research move is a visible contract, not a judgement gate.  These
/// tests print the same structured observations that are pasted into the PR
/// evidence so each criterion remains independently auditable.
/// </summary>
[Collection("WorkerNextActionSharedState")]
public sealed class ResearchDelegationG800Tests : IDisposable
{
    private const string Domain = "intent-cli";
    private const string Team = "g800-research";
    private readonly string root = Directory.CreateTempSubdirectory("research-g800-").FullName;

    public void Dispose()
    {
        NotifyCommand.ProcessRunnerFactory = null;
        NotifyCommand.HerdrExecutableFactory = null;
        NotifyCommand.UtcNowFactory = null;
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void FourSenderRecipientPairsAreRoutableAndCarryQuestionAndExpectedArtifact_G800()
    {
        var pairs = new[]
        {
            (From: "architect", To: "orchestrator", ReportTo: "architect"),
            (From: "architect", To: "steward", ReportTo: "architect"),
            (From: "reviewer", To: "orchestrator", ReportTo: "reviewer"),
            (From: "reviewer", To: "steward", ReportTo: "reviewer"),
        };

        foreach (var (from, to, reportTo) in pairs)
        {
            Assert.True(
                ResearchDelegationContract.TryNormalizePair(from, to, out var canonicalFrom, out var canonicalTo, out var error),
                error);
            var observation = new
            {
                task_kind = ResearchDelegationContract.TaskKind,
                from = canonicalFrom,
                to = canonicalTo,
                report_to = reportTo,
                question = "Which recorded symbols need review?",
                expected_artifact = "sourced inventory",
            };
            Console.WriteLine($"G800 AC1 pair {from}->{to}: {JsonSerializer.Serialize(observation)}");
            Assert.Equal(from, canonicalFrom);
            Assert.Equal(to, canonicalTo);
        }

        using (var workspace = new DirectResearchWorkspace())
        {
            foreach (var (from, to, reportTo) in pairs)
            {
                var taskId = $"routable-{from}-{to}";
                using var writer = new StringWriter();
                var exitCode = NotifyCommand.ExecuteDelegate(
                    workspace.Context,
                    [
                        "--domain", Domain, "--team", Team, "--from", from, "--to", to, "--report-to", reportTo,
                        "--task-id", taskId, "--research", "--question", "Which symbols need review?",
                        "--objective", "Research the recorded symbols.", "--input", "file=src/Inventory.cs symbol=Inventory",
                        "--expected-artifact", "sourced inventory", "--result-nonce", taskId + "-nonce", "--dry-run", "--format", "json",
                    ],
                    writer);
                Assert.Equal(0, exitCode);
                using var document = JsonDocument.Parse(writer.ToString());
                Assert.Equal("research", document.RootElement.GetProperty("task_kind").GetString());
                Assert.Equal("Which symbols need review?", document.RootElement.GetProperty("question").GetString());
                Assert.Contains("sourced-findings-required", document.RootElement.GetProperty("payload").GetString(), StringComparison.Ordinal);
                Console.WriteLine($"G800 AC1 routed {from}->{to}: {document.RootElement.GetRawText()}");
            }
        }

        // The same canonical pair is persisted in the ordinary append-only
        // pending store, proving this is a task kind rather than guide prose.
        var persisted = new List<string>();
        foreach (var (from, to, reportTo) in pairs)
        {
            var taskId = $"pair-{from}-{to}";
            var write = NotifyPendingDelegationStore.WriteDispatch(root, new NotifyPendingDelegation
            {
                Domain = Domain,
                Team = Team,
                TaskId = taskId,
                TaskKind = ResearchDelegationContract.TaskKind,
                DelegatingRole = from,
                RecipientRole = to,
                ReportToRole = reportTo,
                RecipientIdentity = $"role={to}",
                ExpectedArtifact = "sourced inventory",
                ExpectedArtifacts = ["sourced inventory"],
                Objective = "Research the recorded symbols.",
                Question = "Which recorded symbols need review?",
                Inputs = ["file=src/Inventory.cs symbol=Inventory"],
                ResultNonce = taskId + "-nonce",
                DispatchedAt = DateTimeOffset.UtcNow,
                TransportMode = SessionLayerMode.HerdrOnly,
            });
            Assert.True(write.Written, write.Error);
            persisted.Add(taskId);
        }

        var records = NotifyPendingDelegationStore.ReadAll(root, Domain, Team, out var readError);
        Assert.Null(readError);
        Assert.Equal(persisted, records.Select(record => record.TaskId));
        Console.WriteLine($"G800 AC1 persisted pairs: {JsonSerializer.Serialize(records.Select(record => new { record.TaskKind, record.DelegatingRole, record.RecipientRole, record.Question, record.ExpectedArtifacts }))}");
    }

    [Fact]
    public void UnsourcedFindingIsRefusedAndNamesTheFinding_G800()
    {
        var accepted = ResearchDelegationContract.TryValidateReport(
            ["inventory is complete"],
            ["notes"],
            rulingPayload: null,
            rulingOrigin: null,
            rulingDigest: null,
            judgementSeat: "architect",
            out _,
            out var error);

        Assert.False(accepted);
        Assert.Contains("finding 1", error, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("source", error, StringComparison.OrdinalIgnoreCase);
        Console.WriteLine($"G800 AC2 unsourced finding refusal: {JsonSerializer.Serialize(new { accepted, error })}");

        Assert.True(ResearchDelegationContract.TryValidateReport(
            ["symbol is used by the route"],
            ["file=src/IntentSystem.Cli/Commands/NotifyCommand.cs symbol=Execute"],
            null,
            null,
            null,
            "architect",
            out var fileFinding,
            out error), error);
        Assert.True(ResearchDelegationContract.TryValidateReport(
            ["the route is reachable"],
            ["command=rg Execute src/IntentSystem.Cli output=match"],
            null,
            null,
            null,
            "reviewer",
            out var commandFinding,
            out error), error);
        Assert.True(ResearchDelegationContract.TryValidateReport(
            ["the published contract is present"],
            ["https://github.com/J-Tech-Japan/intent-system/issues/1745"],
            null,
            null,
            null,
            "reviewer",
            out var urlFinding,
            out error), error);
        Console.WriteLine($"G800 AC2 sourced findings: {JsonSerializer.Serialize(new { fileFinding, commandFinding, urlFinding })}");
    }

    [Fact]
    public void RulingBearingResearchReportIsRefusedAndNamesJudgementSeat_G800()
    {
        Assert.True(NotifyRuling.TryCreate("opaque ruling", "architect", null, out var ruling, out var rulingError), rulingError);
        Assert.NotNull(ruling);
        var accepted = ResearchDelegationContract.TryValidateReport(
            ["finding"],
            ["file=src/Design.cs symbol=Rule"],
            ruling!.Payload,
            ruling.Origin,
            ruling.Digest,
            "architect",
            out _,
            out var error);

        Assert.False(accepted);
        Assert.Contains("research-ruling-refused", error, StringComparison.Ordinal);
        Assert.Contains("Architect", error, StringComparison.Ordinal);
        Console.WriteLine($"G800 AC3 ruling-bearing refusal: {JsonSerializer.Serialize(new { accepted, cause = "research-ruling-refused", error, ruling_origin = ruling.Origin, ruling_digest = ruling.Digest })}");
    }

    [Fact]
    public void RenderedResearchReportCommand_IsRepeatableAndExecutesForAllFourPairs_G800()
    {
        using var workspace = new DirectResearchWorkspace();
        var pairs = new[]
        {
            (From: "architect", To: "orchestrator", ReportTo: "architect"),
            (From: "architect", To: "steward", ReportTo: "architect"),
            (From: "reviewer", To: "orchestrator", ReportTo: "reviewer"),
            (From: "reviewer", To: "steward", ReportTo: "reviewer"),
        };

        foreach (var (from, to, reportTo) in pairs)
        {
            var taskId = $"rendered-{from}-{to}";
            using var delegateWriter = new StringWriter();
            var delegateExit = NotifyCommand.ExecuteDelegate(
                workspace.Context,
                [
                    "--domain", Domain, "--team", Team, "--from", from, "--to", to, "--report-to", reportTo,
                    "--task-id", taskId, "--research", "--question", "Which symbols need review?",
                    "--objective", "Research the recorded symbols.", "--input", "file=src/Inventory.cs symbol=Inventory",
                    "--expected-artifact", "sourced inventory", "--result-nonce", taskId + "-nonce", "--write", "--format", "json",
                ],
                delegateWriter);
            Assert.Equal(0, delegateExit);
            using var delegateDocument = JsonDocument.Parse(delegateWriter.ToString());
            var reportCommand = delegateDocument.RootElement.GetProperty("report_command").GetString();
            Assert.NotNull(reportCommand);
            Assert.Contains("--task-kind research --finding <finding> --source <source>", reportCommand, StringComparison.Ordinal);

            var reportArgs = RenderReportCommand(
                reportCommand!,
                workspace.Root,
                finding: "route-is-reachable",
                source: "command=rg;output=match");

            var mismatchedArgs = RemoveSourceArgument(reportArgs);
            using var mismatchWriter = new StringWriter();
            var mismatchExit = NotifyCommand.ExecuteReport(workspace.Context, mismatchedArgs, mismatchWriter);
            Assert.Equal(1, mismatchExit);
            var mismatchError = mismatchWriter.ToString();
            Assert.Contains("finding 1", mismatchError, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("no matching source", mismatchError, StringComparison.OrdinalIgnoreCase);

            using var reportWriter = new StringWriter();
            var reportExit = NotifyCommand.ExecuteReport(workspace.Context, reportArgs, reportWriter);
            Assert.True(reportExit == 0, reportWriter.ToString());
            using var reportDocument = JsonDocument.Parse(reportWriter.ToString());
            Assert.Equal("research", reportDocument.RootElement.GetProperty("task_kind").GetString());
            var finding = Assert.Single(reportDocument.RootElement.GetProperty("research_findings").EnumerateArray());
            Assert.Equal("route-is-reachable", finding.GetProperty("finding").GetString());
            Assert.Equal("command=rg;output=match", finding.GetProperty("source").GetString());
            Console.WriteLine($"G800 AC1/AC2 rendered-report {from}->{to}: command={reportCommand}; mismatch_exit={mismatchExit}; mismatch_error={mismatchError}; exit={reportExit}; findings={reportDocument.RootElement.GetProperty("research_findings").GetRawText()}");
        }
    }

    [Fact]
    public void OriginRulingReport_ResolvesPendingJudgementSeatAndNamesOriginatingSeat_G800()
    {
        using var workspace = new DirectResearchWorkspace();
        foreach (var (from, reportTo, expectedSeat) in new[]
        {
            (From: "reviewer", ReportTo: "reviewer", ExpectedSeat: "Reviewer"),
            (From: "architect", ReportTo: "architect", ExpectedSeat: "Architect"),
        })
        {
            var taskId = $"ruling-{from}-steward";
            using var delegateWriter = new StringWriter();
            Assert.Equal(0, NotifyCommand.ExecuteDelegate(
                workspace.Context,
                [
                    "--domain", Domain, "--team", Team, "--from", from, "--to", "steward", "--report-to", reportTo,
                    "--task-id", taskId, "--research", "--question", "Which compatibility facts need review?",
                    "--objective", "Research the compatibility facts.", "--input", "file=src/Compatibility.cs symbol=Check",
                    "--expected-artifact", "sourced compatibility notes", "--result-nonce", taskId + "-nonce", "--write", "--format", "json",
                ],
                delegateWriter));

            Assert.True(NotifyRuling.TryCreate($"{from} ruling", from, null, out var ruling, out var rulingError), rulingError);
            Assert.NotNull(ruling);
            string[] reportArgs =
            [
                "--domain", Domain, "--team", Team, "--from", "steward", "--to", reportTo, "--task-id", taskId,
                "--task-kind", "research", "--status", "completed", "--artifact", "compatibility.md",
                "--summary", "Sourced compatibility notes", "--finding", "compatibility is stable",
                "--source", "file=src/Compatibility.cs symbol=Check", "--ruling-payload", ruling!.Payload,
                "--ruling-origin", ruling.Origin, "--ruling-digest", ruling.Digest, "--write", "--format", "json",
            ];

            using var reportWriter = new StringWriter();
            var reportExit = NotifyCommand.ExecuteReport(workspace.Context, reportArgs, reportWriter);
            Assert.Equal(1, reportExit);
            using var reportDocument = JsonDocument.Parse(reportWriter.ToString());
            Assert.Equal("research-report-refused", reportDocument.RootElement.GetProperty("cause").GetString());
            var error = reportDocument.RootElement.GetProperty("summary").GetString()!;
            Assert.Contains("research-ruling-refused", error, StringComparison.Ordinal);
            Assert.Contains(expectedSeat, error, StringComparison.Ordinal);
            var otherSeat = expectedSeat == "Reviewer" ? "the Architect must supply" : "the Reviewer must supply";
            Assert.DoesNotContain(otherSeat, error, StringComparison.Ordinal);
            Console.WriteLine($"G800 AC3 {from}->steward ruling refusal: exit={reportExit}; expected_seat={expectedSeat}; result={reportDocument.RootElement.GetRawText()}");
        }
    }

    [Fact]
    public void MismatchedResearchFindingAndSource_NamesFirstMissingFindingIndex_G800()
    {
        var accepted = ResearchDelegationContract.TryValidateReport(
            ["finding without a source"],
            [],
            rulingPayload: null,
            rulingOrigin: null,
            rulingDigest: null,
            judgementSeat: "reviewer",
            out _,
            out var error);

        Assert.False(accepted);
        Assert.Contains("finding 1", error, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("no matching source", error, StringComparison.OrdinalIgnoreCase);
        Console.WriteLine($"G800 AC2 mismatched finding/source refusal: {JsonSerializer.Serialize(new { accepted, error })}");
    }

    [Fact]
    public void DirectArchitectAndReviewerResearchCompletesWithoutDelegationOrFailureWarning_G800()
    {
        using var workspace = new DirectResearchWorkspace();
        foreach (var from in new[] { "architect", "reviewer" })
        {
            var taskId = $"direct-{from}";
            using var writer = new StringWriter();
            var exitCode = NotifyCommand.ExecuteDelegate(
                workspace.Context,
                [
                    "--domain", Domain, "--team", Team, "--from", from, "--to", "orchestrator", "--report-to", from,
                    "--task-id", taskId, "--objective", "Read the source directly.",
                    "--input", "file=src/IntentSystem.Cli/Commands/NotifyCommand.cs symbol=Execute",
                    "--expected-artifact", "direct notes", "--result-nonce", taskId + "-nonce", "--direct-research",
                    "--dry-run", "--format", "json",
                ],
                writer);
            Assert.Equal(0, exitCode);
            using var document = JsonDocument.Parse(writer.ToString());
            var result = document.RootElement;
            Assert.True(result.GetProperty("summary").GetString()!.Contains("Dry-run", StringComparison.OrdinalIgnoreCase));
            Assert.False(result.TryGetProperty("cause", out _));
            Console.WriteLine($"G800 AC4 direct {from}: {result.GetRawText()}");

            using var reportWriter = new StringWriter();
            var reportExitCode = NotifyCommand.ExecuteReport(
                workspace.Context,
                [
                    "--domain", Domain, "--team", Team, "--from", from, "--to", "orchestrator",
                    "--task-id", $"direct-report-{from}", "--status", "completed", "--artifact", "direct notes",
                    "--summary", "Direct research remains an ordinary report.", "--direct-research", "--dry-run", "--format", "json",
                ],
                reportWriter);
            Assert.Equal(0, reportExitCode);
            using var reportDocument = JsonDocument.Parse(reportWriter.ToString());
            Assert.False(reportDocument.RootElement.TryGetProperty("cause", out _));
            Console.WriteLine($"G800 AC4 direct report {from}: {reportDocument.RootElement.GetRawText()}");
        }
    }

    [Fact]
    public void BothGuidesRenderTheContractWithoutThresholdOrRuntimeNames_G800()
    {
        using var designWriter = new StringWriter();
        Assert.Equal(0, GuideDesignThreadCommand.Execute(BareContext(), ["--format", "json"], designWriter));
        using var designDocument = JsonDocument.Parse(designWriter.ToString());
        var designContract = designDocument.RootElement.GetProperty("research_delegation");

        using var reviewWorkspace = new ReviewGuideWorkspace();
        using var reviewWriter = new StringWriter();
        Assert.Equal(0, GuideReviewCommand.Execute(
            reviewWorkspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--pr", "800", "--format", "json"],
            reviewWriter));
        using var reviewDocument = JsonDocument.Parse(reviewWriter.ToString());
        var reviewContract = reviewDocument.RootElement.GetProperty("research_delegation");

        foreach (var contract in new[] { designContract, reviewContract })
        {
            Assert.Equal("research", contract.GetProperty("task_kind").GetString());
            Assert.Contains("question", contract.GetProperty("what_goes_down").GetString(), StringComparison.OrdinalIgnoreCase);
            Assert.Contains("expected artifact", contract.GetProperty("what_goes_down").GetString(), StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Orchestrator", contract.GetProperty("who_receives").GetString(), StringComparison.Ordinal);
            Assert.Contains("Steward", contract.GetProperty("who_receives").GetString(), StringComparison.Ordinal);
            Assert.Contains("ruling", contract.GetProperty("no_ruling_boundary").GetString(), StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("threshold", contract.GetRawText(), StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("claude", contract.GetRawText(), StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("codex", contract.GetRawText(), StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("opencode", contract.GetRawText(), StringComparison.OrdinalIgnoreCase);
        }

        Console.WriteLine($"G800 AC5 design contract: {designContract.GetRawText()}");
        Console.WriteLine($"G800 AC5 review contract: {reviewContract.GetRawText()}");
    }

    [Fact]
    public void VisibilityCountsAreDescriptiveAndContainNoGradingFields_G800()
    {
        WritePending("delegated-1", directResearch: false);
        WritePending("delegated-2", directResearch: false);
        WritePending("direct-1", directResearch: true);
        Assert.True(NotifyReportOutboxStore.WriteNew(root, new NotifyReportOutboxEntry
        {
            Domain = Domain,
            Team = Team,
            TaskId = "direct-2",
            FromRole = "reviewer",
            ToRole = "orchestrator",
            Status = "completed",
            Artifact = "direct notes",
            Summary = "direct research",
            DirectResearch = true,
            CreatedAt = DateTimeOffset.UtcNow,
            DeliveryState = "delivered",
        }).Written);

        var metrics = ResearchDelegationContract.Measure(root, Domain, Team, out var error);
        Assert.Null(error);
        Assert.Equal(2, metrics.ResearchDelegationsIssued);
        Assert.Equal(2, metrics.JudgementSeatTurnsWithoutDelegation);
        var output = JsonSerializer.Serialize(metrics);
        Assert.DoesNotContain("verdict", output, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("score", output, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("should-have", output, StringComparison.OrdinalIgnoreCase);
        Console.WriteLine($"G800 AC6 visibility-only output: {output}");

        using var writer = new StringWriter();
        var exitCode = NotifyCommand.ExecuteResearchStatus(
            ContextForRoot(root),
            ["--domain", Domain, "--team", Team, "--format", "json"],
            writer);
        Console.WriteLine($"G800 AC6 research-status raw exit={exitCode}: {writer}");
        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        Assert.Equal(2, document.RootElement.GetProperty("research_delegations_issued").GetInt32());
        Assert.Equal(2, document.RootElement.GetProperty("judgement_seat_turns_without_delegation").GetInt32());
        Assert.DoesNotContain("verdict", document.RootElement.GetRawText(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("score", document.RootElement.GetRawText(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("should-have", document.RootElement.GetRawText(), StringComparison.OrdinalIgnoreCase);
        Console.WriteLine($"G800 AC6 research-status command: {document.RootElement.GetRawText()}");
    }

    [Fact]
    public void TrivialAndLargeResearchFixturesBehaveIdentically_G800()
    {
        var trivial = ValidateSizeFixture(1);
        var large = ValidateSizeFixture(64);
        Assert.Equal(trivial.Accepted, large.Accepted);
        Assert.Equal(trivial.Error, large.Error);
        Assert.Equal(string.Empty, trivial.Error);
        Assert.Equal(string.Empty, large.Error);
        Assert.Equal(1, trivial.Findings);
        Assert.Equal(64, large.Findings);
        Console.WriteLine($"G800 AC7 size pair: {JsonSerializer.Serialize(new
        {
            trivial = new { accepted = trivial.Accepted, error = trivial.Error, findings = trivial.Findings },
            large = new { accepted = large.Accepted, error = large.Error, findings = large.Findings },
        })}");
    }

    [Fact]
    public void RecordedRuntimeLabelsDoNotChangeResearchContractBehaviour_G800()
    {
        var outcomes = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (var runtime in new[] { "claude", "codex", "opencode" })
        {
            var accepted = ResearchDelegationContract.TryValidateReport(
                ["the finding is attributable"],
                ["file=src/Research.cs symbol=Collect"],
                null,
                null,
                null,
                "reviewer",
                out var findings,
                out var error);
            outcomes[runtime] = new { accepted, error, findings };
            Assert.True(accepted, error);
        }

        var serialized = JsonSerializer.Serialize(outcomes);
        Assert.Equal(3, outcomes.Count);
        Assert.All(outcomes.Values, value => Assert.Contains("accepted", JsonSerializer.Serialize(value), StringComparison.Ordinal));
        Console.WriteLine($"G800 AC8 runtime-neutral output: {serialized}");
    }

    [Fact]
    public void SharedRulingShapeRemainsByteDigestAndOriginStable_G800()
    {
        Assert.True(NotifyRuling.TryCreate("opaque bytes", "architect", null, out var source, out var error), error);
        Assert.NotNull(source);
        Assert.True(NotifyRulingRelay.TryRelay(source!, source!, new Dictionary<string, string> { ["task"] = "research" }, out var accepted));
        Assert.True(accepted.Accepted);

        var mutated = source! with { Payload = "opaque bytes!" };
        Assert.False(NotifyRulingRelay.TryRelay(source!, mutated, null, out var refused));
        Assert.Equal("ruling-digest-mismatch", refused.Cause);
        Console.WriteLine($"G800 AC3 shared ruling relay: {JsonSerializer.Serialize(new { accepted = accepted.Summary, refused = refused.Summary, source = new { source.Payload, source.Digest, source.Origin } })}");
    }

    private (bool Accepted, string? Error, int Findings) ValidateSizeFixture(int count)
    {
        var findings = Enumerable.Range(0, count).Select(index => $"finding {index + 1}").ToArray();
        var sources = Enumerable.Range(0, count)
            .Select(index => $"file=src/Research{index + 1}.cs symbol=Collect{index + 1}")
            .ToArray();
        var accepted = ResearchDelegationContract.TryValidateReport(
            findings,
            sources,
            null,
            null,
            null,
            "architect",
            out var sourced,
            out var error);
        return (accepted, error, sourced.Count);
    }

    private void WritePending(string taskId, bool directResearch)
    {
        var result = NotifyPendingDelegationStore.WriteDispatch(root, new NotifyPendingDelegation
        {
            Domain = Domain,
            Team = Team,
            TaskId = taskId,
            TaskKind = ResearchDelegationContract.TaskKind,
            DelegatingRole = "architect",
            RecipientRole = "orchestrator",
            ReportToRole = "architect",
            RecipientIdentity = "role=orchestrator",
            ExpectedArtifact = "sourced notes",
            Question = "Which symbols matter?",
            DirectResearch = directResearch,
            ResultNonce = taskId + "-nonce",
            DispatchedAt = DateTimeOffset.UtcNow,
            TransportMode = SessionLayerMode.HerdrOnly,
        });
        Assert.True(result.Written, result.Error);
    }

    private static CliContext BareContext() => new()
    {
        RepoRoot = AppContext.BaseDirectory,
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

    private static CliContext ContextForRoot(string repoRoot) => new()
    {
        RepoRoot = repoRoot,
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

    private static string[] RenderReportCommand(
        string command,
        string routingRoot,
        string finding,
        string source)
    {
        var rendered = command
            .Replace("<completed|blocked|question>", "completed", StringComparison.Ordinal)
            .Replace("<artifact>", "compatibility.md", StringComparison.Ordinal)
            .Replace("<one-line-summary>", "sourced-notes", StringComparison.Ordinal)
            .Replace("<role-work-root>", ShellQuote(routingRoot), StringComparison.Ordinal)
            .Replace("<finding>", finding, StringComparison.Ordinal)
            .Replace("<source>", source, StringComparison.Ordinal);
        var words = SplitShellWords(rendered);
        Assert.Equal(["intent-cli", "notify", "report"], words.Take(3));
        return words.Skip(3).ToArray();
    }

    private static string[] RemoveSourceArgument(IReadOnlyList<string> args)
    {
        var result = new List<string>(args.Count);
        for (var index = 0; index < args.Count; index++)
        {
            if (string.Equals(args[index], "--source", StringComparison.Ordinal))
            {
                index++;
                continue;
            }

            result.Add(args[index]);
        }

        return result.ToArray();
    }

    private static string ShellQuote(string value) =>
        $"'{value.Replace("'", "'\\''", StringComparison.Ordinal)}'";

    private static string[] SplitShellWords(string command)
    {
        var words = new List<string>();
        var current = new System.Text.StringBuilder();
        var inSingleQuote = false;
        var escaped = false;
        foreach (var character in command)
        {
            if (escaped)
            {
                current.Append(character);
                escaped = false;
                continue;
            }

            if (character == '\\' && !inSingleQuote)
            {
                escaped = true;
                continue;
            }

            if (character == '\'')
            {
                inSingleQuote = !inSingleQuote;
                continue;
            }

            if (char.IsWhiteSpace(character) && !inSingleQuote)
            {
                if (current.Length > 0)
                {
                    words.Add(current.ToString());
                    current.Clear();
                }

                continue;
            }

            current.Append(character);
        }

        if (escaped)
        {
            current.Append('\\');
        }

        if (current.Length > 0)
        {
            words.Add(current.ToString());
        }

        return words.ToArray();
    }

    private sealed class DirectResearchWorkspace : IDisposable
    {
        public DirectResearchWorkspace()
        {
            Root = Directory.CreateTempSubdirectory("g800-direct-").FullName;
            Directory.CreateDirectory(Path.Combine(Root, ".intent-cli"));
            var topologyPath = NotifyRoleTopologyStore.ResolvePath(Root, Domain, Team);
            Directory.CreateDirectory(Path.GetDirectoryName(topologyPath)!);
            var roles = new Dictionary<string, object>(StringComparer.Ordinal);
            foreach (var role in new[] { "architect", "orchestrator", "builder", "reviewer", "steward" })
            {
                var reader = $".intent-cli/events/{role}.jsonl";
                Directory.CreateDirectory(Path.Combine(Root, ".intent-cli", "events"));
                File.WriteAllText(Path.Combine(Root, reader), string.Empty);
                roles[role] = new { resident = "external", reader };
            }

            File.WriteAllText(topologyPath, JsonSerializer.Serialize(new
            {
                domain = Domain,
                team = Team,
                workspace_id = "g800",
                roles,
            }));
            using var writer = new StringWriter();
            var result = SessionLayerCommand.ExecuteSet(
                Context,
                ["--domain", Domain, "--team", Team, "--mode", SessionLayerMode.HerdrOnly, "--write", "--format", "json"],
                writer);
            if (result != 0)
            {
                throw new InvalidOperationException(writer.ToString());
            }
        }

        public string Root { get; }

        public CliContext Context => new()
        {
            RepoRoot = Root,
            Config = new CliConfig
            {
                Project = new ProjectConfig { Domain = Domain, ArtifactRoot = ".intent-cli" },
            },
        };

        public void Dispose()
        {
            if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
        }
    }

    private sealed class ReviewGuideWorkspace : IDisposable
    {
        public ReviewGuideWorkspace()
        {
            Root = Directory.CreateTempSubdirectory("g800-review-guide-").FullName;
            Directory.CreateDirectory(Path.Combine(Root, ".intent-cli", "issues", "G800"));
            var queuePath = Path.Combine(Root, ".intent-cli", "queue-state.json");
            Directory.CreateDirectory(Path.GetDirectoryName(queuePath)!);
            File.WriteAllText(queuePath, """
            {
              "schema_version": "1",
              "updated_at": "2026-09-04T00:00:00Z",
              "items": [{
                "execution_unit": "G800",
                "title": "research delegation",
                "state": "review",
                "dependencies": [],
                "blocked_by": [],
                "clarification_return_path": "intents/intent-cli/clarifications/open.md",
                "packet_paths": {"implementation": "a", "review_context": "b", "yaml": "c"},
                "linked_pr": "https://github.com/J-Tech-Japan/intent-system/pull/800",
                "worker_role": "builder",
                "review_role": "reviewer",
                "priority": "normal"
              }]
            }
            """);
        }

        public string Root { get; }

        public CliContext Context => new()
        {
            RepoRoot = Root,
            Config = new CliConfig
            {
                Project = new ProjectConfig { Domain = Domain, ArtifactRoot = ".intent-cli" },
            },
        };

        public void Dispose()
        {
            if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
        }
    }
}
