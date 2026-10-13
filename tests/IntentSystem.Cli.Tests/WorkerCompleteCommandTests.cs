using System.Security.Cryptography;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;
using IntentSystem.Supervisor.Models;
using IntentSystem.Supervisor.Serialization;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G211: Tests for <c>intent-cli worker complete</c>. Cover the
/// issue-side and PR-side completion transitions for every supported
/// outcome, the stale-state refusals, the "intent-pr-created stays on
/// the issue" defensive guard, the dry-run vs write split, and the
/// no-mutation invariants.
/// </summary>
// G569 audit: joins the non-parallel collection that already owns the
// process-global statics this class assigns, so it can no longer interleave
// with the other class that assigns them.
[Collection("WorkerNextActionSharedState")]
public sealed class WorkerCompleteCommandTests : IDisposable
{
    public WorkerCompleteCommandTests()
    {
        WorkerCompleteCommand.MutatorFactory = null;
        WorkerCompleteCommand.NestedProviderLauncher = null;
        // G311: permissive default — pre-existing tests assume the
        // closing-reference gate is satisfied. Returns a fake PR lookup
        // whose `closingIssuesReferences` cover the issue numbers used
        // throughout the suite (514, 525). The fake intentionally
        // returns `Repository = null` so `IsSameRepo` accepts. New
        // G311-focused tests override this factory to exercise the
        // gate's failure paths.
        WorkerCompleteCommand.PrLookupFactory = () => new PermissivePrLookup();
        // G347/G389: permissive default — a single-unit stub issue (no
        // baseRefName-driven mismatch, no multi-packet ambiguity) so the
        // G347 base-branch and G389 ambiguous-contract gates are hermetic and
        // pass for pre-existing tests. G347/G389-focused tests override this
        // factory to exercise the failure paths.
        WorkerCompleteCommand.IssueLookupFactory = () => new StubIssueLookup();
    }

    public void Dispose()
    {
        WorkerCompleteCommand.MutatorFactory = null;
        WorkerCompleteCommand.NestedProviderLauncher = null;
        WorkerCompleteCommand.PrLookupFactory = null;
        WorkerCompleteCommand.IssueLookupFactory = null;
    }

    [Fact]
    public void Execute_IssuePrCreatedOutcome_DryRunPlansSwapInProgressForPrCreated()
    {
        using var workspace = new WorkerCompleteWorkspace();
        var mutator = new FakeMutator
        {
            Labels = new[] { "intent-target", "intent-issue-in-progress" },
        };
        WorkerCompleteCommand.MutatorFactory = () => mutator;

        using var writer = new StringWriter();
        var exitCode = WorkerCompleteCommand.Execute(
            workspace.Context,
            new[]
            {
                "--repo", "J-Tech-Japan/intent-system",
                "--kind", "issue",
                "--number", "525",
                "--outcome", WorkerResultSummaryConstants.Outcomes.PrCreated,
                "--pr", "999",
                "--format", "json",
            },
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<WorkerCompleteResult>(writer.ToString())!;
        Assert.True(result.Proceed);
        Assert.False(result.Applied);
        Assert.Contains("intent-pr-created", result.AddLabels);
        Assert.Contains("intent-issue-in-progress", result.RemoveLabels);
        Assert.Empty(mutator.AppliedTransitions);

        Assert.Contains(result.Warnings, w =>
            w.Contains("intent-pr-created", StringComparison.Ordinal)
            && w.Contains("ISSUE", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("json")]
    [InlineData("markdown")]
    public void G868ActualWriterSatisfiedCoveragePassesBothGateObservations(string format)
    {
        using var host = new VerificationEvidenceCommandTests.TempHost();
        var trx = VerificationEvidenceCommandTests.ReadSmallVstest();
        host.WriteTrx("capture.trx", trx);
        var packetSha = VerificationEvidenceCommandTests.Hash(host.PacketBytes);
        var (recordExit, recordOutput) = VerificationEvidenceCommandTests.Invoke(host,
            [.. VerificationEvidenceCommandTests.RecordArgs(host, "json", packetSha, "capture.trx", id: "E1"), "--write"]);
        Assert.Equal(0, recordExit);
        using (var record = JsonDocument.Parse(recordOutput))
        {
            Assert.Equal("recorded", record.RootElement.GetProperty("disposition").GetString());
            Assert.True(record.RootElement.GetProperty("wrote").GetBoolean());
        }

        var context = G868Context(host.Root);
        var mutator = new FakeMutator
        {
            Labels = new[] { WorkerNextActionConstants.Labels.IntentPrUpdateInProgress },
        };
        var mutatorFactories = 0;
        WorkerCompleteCommand.MutatorFactory = () => { mutatorFactories++; return mutator; };
        var headReads = 0;
        var coverageReads = 0;
        var dependencies = new WorkerVerificationGate.Dependencies(
            ReadPullRequestHeadSha: (repo, pr) =>
            {
                Assert.Equal(VerificationEvidenceCommandTests.Repo, repo);
                Assert.Equal(1900, pr);
                headReads++;
                return VerificationEvidenceCommandTests.Head;
            },
            BeforeCoverageOperation: (_, _) => coverageReads++);

        var args = G868Arguments(host, format, includeEvidence: true);
        using var writer = new StringWriter();
        var exitCode = WorkerCompleteCommand.Execute(context, args, writer, dependencies);

        Assert.Equal(0, exitCode);
        Assert.Equal(2, headReads);
        Assert.True(coverageReads > 0);
        Assert.Equal(1, mutatorFactories);
        Assert.Empty(mutator.AppliedTransitions);
        if (format == "json")
        {
            using var output = JsonDocument.Parse(writer.ToString());
            var root = output.RootElement;
            var gate = root.GetProperty("verification_gate");
            Assert.True(root.GetProperty("proceed").GetBoolean());
            Assert.False(root.GetProperty("applied").GetBoolean());
            Assert.Equal("satisfied", gate.GetProperty("state").GetString());
            Assert.Equal("worker-verification-satisfied", gate.GetProperty("cause").GetString());
            Assert.Equal("pre-write", gate.GetProperty("phase").GetString());
            var coverage = gate.GetProperty("coverage");
            Assert.Equal("complete", coverage.GetProperty("state").GetString());
            Assert.Equal("not-observed", coverage.GetProperty("execution").GetString());
            Assert.Equal("not-authenticated", coverage.GetProperty("producer_identity").GetString());
            Assert.Equal("not-assessed", coverage.GetProperty("acceptance").GetString());
            Assert.Equal("not-verified", coverage.GetProperty("publication").GetString());
        }
        else
        {
            var output = writer.ToString();
            Assert.Contains("## Verification gate", output, StringComparison.Ordinal);
            Assert.Contains("- state: satisfied", output, StringComparison.Ordinal);
            Assert.Contains("- cause: worker-verification-satisfied", output, StringComparison.Ordinal);
            Assert.Contains("- phase: pre-write", output, StringComparison.Ordinal);
            Assert.Contains("\"state\": \"complete\"", output, StringComparison.Ordinal);
            Assert.Contains("\"execution\": \"not-observed\"", output, StringComparison.Ordinal);
            Assert.Contains("\"producer_identity\": \"not-authenticated\"", output, StringComparison.Ordinal);
            Assert.Contains("\"acceptance\": \"not-assessed\"", output, StringComparison.Ordinal);
            Assert.Contains("\"publication\": \"not-verified\"", output, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("json")]
    [InlineData("markdown")]
    public void G868ActualWriterSupportsAnOtherwiseValidNonDefaultIdentity(string format)
    {
        const string unit = "G868-OtherUnit";
        const string domain = "sample-domain";
        const string team = "proof-team";
        const string repo = "sample-org/target-repository";
        var packet = G868TwoProofPacket
            .Replace("source_execution_unit: G866", "source_execution_unit: " + unit, StringComparison.Ordinal)
            .Replace("domain: intent-cli", "domain: " + domain, StringComparison.Ordinal)
            .Replace("team: intent-cli-dev", "team: " + team, StringComparison.Ordinal)
            .Replace("target_repo: J-Tech-Japan/intent-system", "target_repo: " + repo, StringComparison.Ordinal);
        using var host = new VerificationEvidenceCommandTests.TempHost(unit, packet);
        WriteG868Record(host, "P1", "E1", domain: domain, team: team, repo: repo, unit: unit);
        WriteG868Record(host, "P2", "E2", domain: domain, team: team, repo: repo, unit: unit);
        WorkerCompleteCommand.MutatorFactory = () => new FakeMutator
        {
            Labels = [WorkerNextActionConstants.Labels.IntentPrUpdateInProgress],
        };
        var headReads = 0;
        using var writer = new StringWriter();
        var exit = WorkerCompleteCommand.Execute(G868Context(host.Root),
            G868Arguments(host, format, ["--evidence", "P1=E1", "--evidence", "P2=E2"], write: false,
                domain: domain, team: team, repo: repo), writer,
            new WorkerVerificationGate.Dependencies(
                ReadPullRequestHeadSha: (requestedRepo, _) =>
                {
                    Assert.Equal(repo, requestedRepo);
                    headReads++;
                    return VerificationEvidenceCommandTests.Head;
                }));

        Assert.True(exit == 0, writer.ToString());
        Assert.Equal(2, headReads);
        if (format == "json")
        {
            using var document = JsonDocument.Parse(writer.ToString());
            var coverage = document.RootElement.GetProperty("verification_gate").GetProperty("coverage");
            Assert.Equal(unit, coverage.GetProperty("execution_unit").GetString());
            Assert.Equal(domain, coverage.GetProperty("domain").GetString());
            Assert.Equal(team, coverage.GetProperty("team").GetString());
            Assert.Equal(repo, coverage.GetProperty("target_repo").GetString());
            Assert.Equal("satisfied", document.RootElement.GetProperty("verification_gate").GetProperty("state").GetString());
        }
        else
        {
            Assert.Contains(unit, writer.ToString(), StringComparison.Ordinal);
            Assert.Contains(domain, writer.ToString(), StringComparison.Ordinal);
            Assert.Contains(team, writer.ToString(), StringComparison.Ordinal);
            Assert.Contains(repo, writer.ToString(), StringComparison.Ordinal);
            Assert.Contains("worker-verification-satisfied", writer.ToString(), StringComparison.Ordinal);
        }
    }

    [Fact]
    public void G868SatisfiedMarkdownFencesUntrustedCommandAndCriterionText()
    {
        const string command = "dotnet test\n```json\n# injected command heading\n[run](file:///tmp/evil)";
        const string criterion = "Preserve first local criterion\n```yaml\n# injected criterion heading";
        var packet = ReplaceFirst(G868TwoProofPacket, "- Preserve first local criterion",
                "- \"Preserve first local criterion\\n```yaml\\n# injected criterion heading\"")
            .Replace("id: unit, weight: ordinary", "id: unit, weight: heavy", StringComparison.Ordinal)
            .Replace("command: dotnet test", "command: \"dotnet test\\n```json\\n# injected command heading\\n[run](file:///tmp/evil)\"", StringComparison.Ordinal);
        using var host = new VerificationEvidenceCommandTests.TempHost(packet: packet);
        WriteG868Record(host, "P1", "E1");
        WriteG868Record(host, "P2", "E2");
        WorkerCompleteCommand.MutatorFactory = () => new FakeMutator
        {
            Labels = [WorkerNextActionConstants.Labels.IntentPrUpdateInProgress],
        };
        var headReads = 0;
        using var writer = new StringWriter();

        var exit = WorkerCompleteCommand.Execute(G868Context(host.Root),
            G868Arguments(host, "markdown", ["--evidence", "P1=E1", "--evidence", "P2=E2"], write: false), writer,
            new WorkerVerificationGate.Dependencies(
                ReadPullRequestHeadSha: (_, _) => { headReads++; return VerificationEvidenceCommandTests.Head; }));

        Assert.True(exit == 0, writer.ToString());
        Assert.Equal(2, headReads);
        var output = writer.ToString();
        var coverageLabel = output.IndexOf("- coverage:", StringComparison.Ordinal);
        Assert.True(coverageLabel >= 0);
        var openingStart = output.IndexOf("```", coverageLabel, StringComparison.Ordinal);
        Assert.True(openingStart > coverageLabel);
        var openingEnd = output.IndexOf('\n', openingStart);
        var openingFence = output[openingStart..openingEnd];
        var fence = new string('`', openingFence.TakeWhile(character => character == '`').Count());
        Assert.Equal(3, fence.Length);
        var closingStart = output.IndexOf("\n" + fence, openingEnd, StringComparison.Ordinal);
        Assert.True(closingStart > openingEnd);
        var fencedJson = output[(openingEnd + 1)..closingStart];
        using var coverage = JsonDocument.Parse(fencedJson);
        Assert.Equal("complete", coverage.RootElement.GetProperty("state").GetString());
        Assert.Equal(command, coverage.RootElement.GetProperty("proofs")[0].GetProperty("command").GetString());
        Assert.Equal(criterion, coverage.RootElement.GetProperty("proofs")[0]
            .GetProperty("acceptance_criteria")[0].GetProperty("text").GetString());
        Assert.DoesNotContain("# injected command heading", output[..openingStart], StringComparison.Ordinal);
        Assert.DoesNotContain("# injected criterion heading", output[..openingStart], StringComparison.Ordinal);
        Assert.DoesNotContain("[run](file:///tmp/evil)", output[..openingStart], StringComparison.Ordinal);
        Assert.Contains("\"execution\": \"not-observed\"", fencedJson, StringComparison.Ordinal);
        Assert.Contains("\"producer_identity\": \"not-authenticated\"", fencedJson, StringComparison.Ordinal);
        Assert.Contains("\"acceptance\": \"not-assessed\"", fencedJson, StringComparison.Ordinal);
        Assert.Contains("\"publication\": \"not-verified\"", fencedJson, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("json")]
    [InlineData("markdown")]
    public void G868MissingRecordRefusesBeforeHeadOrLegacyProviders(string format)
    {
        using var host = new VerificationEvidenceCommandTests.TempHost();
        var context = G868Context(host.Root);
        var mutatorFactories = 0;
        var prLookupFactories = 0;
        var issueLookupFactories = 0;
        WorkerCompleteCommand.MutatorFactory = () =>
        {
            mutatorFactories++;
            return new FakeMutator { Labels = new[] { WorkerNextActionConstants.Labels.IntentPrUpdateInProgress } };
        };
        WorkerCompleteCommand.PrLookupFactory = () => { prLookupFactories++; return new PermissivePrLookup(); };
        WorkerCompleteCommand.IssueLookupFactory = () => { issueLookupFactories++; return new StubIssueLookup(); };
        var headReads = 0;
        var dependencies = new WorkerVerificationGate.Dependencies(
            ReadPullRequestHeadSha: (_, _) => { headReads++; return VerificationEvidenceCommandTests.Head; });

        using var writer = new StringWriter();
        var exitCode = WorkerCompleteCommand.Execute(
            context, G868Arguments(host, format, includeEvidence: true), writer, dependencies);

        Assert.Equal(2, exitCode);
        Assert.Equal(0, headReads);
        Assert.Equal(0, mutatorFactories);
        Assert.Equal(0, prLookupFactories);
        Assert.Equal(0, issueLookupFactories);
        Assert.False(Directory.Exists(host.EvidenceRoot));
        if (format == "json")
        {
            using var output = JsonDocument.Parse(writer.ToString());
            var root = output.RootElement;
            var gate = root.GetProperty("verification_gate");
            Assert.False(root.GetProperty("proceed").GetBoolean());
            Assert.False(root.GetProperty("applied").GetBoolean());
            Assert.Empty(root.GetProperty("add_labels").EnumerateArray());
            Assert.Empty(root.GetProperty("remove_labels").EnumerateArray());
            Assert.Empty(root.GetProperty("current_labels").EnumerateArray());
            Assert.Equal("refused", gate.GetProperty("state").GetString());
            Assert.Equal("worker-verification-coverage-incomplete", gate.GetProperty("cause").GetString());
            Assert.Equal("initial", gate.GetProperty("phase").GetString());
            Assert.Contains("P1", gate.GetProperty("coverage").GetProperty("missing_proof_ids")
                .EnumerateArray().Select(item => item.GetString()!));
        }
        else
        {
            Assert.Contains("- cause: worker-verification-coverage-incomplete", writer.ToString(), StringComparison.Ordinal);
            Assert.Contains("- phase: initial", writer.ToString(), StringComparison.Ordinal);
        }
    }

    [Fact]
    public void G868NoMarkerOutputAndDependenciesRemainExactlyOnLegacyPath()
    {
        using var workspace = new WorkerCompleteWorkspace();
        var args = new[]
        {
            "--repo", VerificationEvidenceCommandTests.Repo,
            "--kind", "pr",
            "--number", "1900",
            "--outcome", WorkerResultSummaryConstants.Outcomes.RepairPushed,
            "--github-only",
            "--format", "json",
        };
        string Run(WorkerVerificationGate.Dependencies? dependencies)
        {
            var mutator = new FakeMutator
            {
                Labels = new[] { WorkerNextActionConstants.Labels.IntentPrUpdateInProgress },
            };
            WorkerCompleteCommand.MutatorFactory = () => mutator;
            using var writer = new StringWriter();
            var exitCode = WorkerCompleteCommand.Execute(workspace.Context, args, writer, dependencies);
            Assert.Equal(0, exitCode);
            return writer.ToString();
        }

        var dependencyCalls = 0;
        var withoutDependencies = Run(null);
        var withUnusedDependencies = Run(new WorkerVerificationGate.Dependencies(
            ReadPullRequestHeadSha: (_, _) => { dependencyCalls++; throw new InvalidOperationException("must not run"); },
            BeforeCoverageOperation: (_, _) => dependencyCalls++));

        Assert.Equal(withoutDependencies, withUnusedDependencies);
        Assert.Equal(0, dependencyCalls);
        using var result = JsonDocument.Parse(withoutDependencies);
        Assert.False(result.RootElement.TryGetProperty("verification_gate", out _));
    }

    [Theory]
    [InlineData("json", false)]
    [InlineData("json", true)]
    [InlineData("markdown", false)]
    [InlineData("markdown", true)]
    public void G868TwoActualRecordsPassPreviewAndWriteForRepairOutcome(string format, bool write)
    {
        using var host = new VerificationEvidenceCommandTests.TempHost(packet: G868TwoProofPacket);
        var trx = VerificationEvidenceCommandTests.ReadSmallVstest();
        host.WriteTrx("capture.trx", trx);
        var packetSha = VerificationEvidenceCommandTests.Hash(host.PacketBytes);
        foreach (var (proof, id) in new[] { ("P1", "E1"), ("P2", "E2") })
        {
            var (recordExit, recordOutput) = VerificationEvidenceCommandTests.Invoke(host,
                [.. VerificationEvidenceCommandTests.RecordArgs(host, "json", packetSha, "capture.trx",
                    id: id, proof: proof), "--write"]);
            Assert.Equal(0, recordExit);
            using var record = JsonDocument.Parse(recordOutput);
            Assert.Equal("recorded", record.RootElement.GetProperty("disposition").GetString());
            Assert.True(record.RootElement.GetProperty("wrote").GetBoolean());
        }

        var context = G868Context(host.Root);
        var events = new List<string>();
        var mutator = new FakeMutator
        {
            Labels = new[] { WorkerNextActionConstants.Labels.IntentPrUpdateInProgress },
            ApplyCallback = () => events.Add("apply"),
        };
        WorkerCompleteCommand.MutatorFactory = () => mutator;
        var headReads = new List<(string Repo, int Pr)>();
        var coverageOperations = new List<string>();
        var dependencies = new WorkerVerificationGate.Dependencies(
            ReadPullRequestHeadSha: (repo, pr) =>
            {
                headReads.Add((repo, pr));
                events.Add("head");
                return VerificationEvidenceCommandTests.Head;
            },
            BeforeCoverageOperation: (operation, _) =>
            {
                coverageOperations.Add(operation);
                events.Add(operation);
            });
        var refs = new[] { "--evidence", "P2=E2", "--evidence", "P1=E1" };
        var args = G868Arguments(host, format, refs, write);
        using var writer = new StringWriter();

        var exitCode = WorkerCompleteCommand.Execute(context, args, writer, dependencies);

        Assert.Equal(0, exitCode);
        Assert.Equal(new[]
        {
            (VerificationEvidenceCommandTests.Repo, 1900),
            (VerificationEvidenceCommandTests.Repo, 1900),
        }, headReads);
        Assert.Equal(2, coverageOperations.Count(operation => operation == "packet-read"));
        Assert.Equal(4, coverageOperations.Count(operation => operation == "stored-read"));
        Assert.Equal(write ? 1 : 0, mutator.AppliedTransitions.Count);
        if (write)
        {
            Assert.Equal(new[] { "head", "packet-read", "stored-read", "stored-read", "apply" }, events.TakeLast(5));
            var transition = Assert.Single(mutator.AppliedTransitions);
            Assert.Equal("pr", transition.Kind);
            Assert.Equal(1900, transition.Number);
            Assert.Contains(WorkerNextActionConstants.Labels.IntentPrRereviewReady, transition.AddLabels);
            Assert.Contains(WorkerNextActionConstants.Labels.IntentPrUpdateInProgress, transition.RemoveLabels);
        }

        var output = writer.ToString();
        if (format == "json")
        {
            using var document = JsonDocument.Parse(output);
            var root = document.RootElement;
            Assert.Equal(write, root.GetProperty("applied").GetBoolean());
            var gate = root.GetProperty("verification_gate");
            Assert.Equal("satisfied", gate.GetProperty("state").GetString());
            Assert.Equal("pre-write", gate.GetProperty("phase").GetString());
            Assert.Equal(1900, gate.GetProperty("target_pr").GetInt32());
            var coverage = gate.GetProperty("coverage");
            Assert.Equal("complete", coverage.GetProperty("state").GetString());
            var proofs = coverage.GetProperty("proofs").EnumerateArray().ToArray();
            Assert.Equal(new[] { "P1", "P2" }, proofs.Select(proof => proof.GetProperty("proof_id").GetString()));
            Assert.All(proofs, proof => Assert.Equal("present", proof.GetProperty("state").GetString()));
        }
        else
        {
            Assert.Contains("- state: satisfied", output, StringComparison.Ordinal);
            Assert.Contains("- phase: pre-write", output, StringComparison.Ordinal);
            Assert.Contains("\"proof_id\": \"P1\"", output, StringComparison.Ordinal);
            Assert.Contains("\"proof_id\": \"P2\"", output, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("json")]
    [InlineData("markdown")]
    public void G868NameOnlyDiscrepancyWithZeroDerivedSkipsRemainsEligible(string format)
    {
        using var host = new VerificationEvidenceCommandTests.TempHost(packet: G868TwoProofPacket);
        var captured = VerificationEvidenceCommandTests.ReadSmallVstest();
        var nameDifference = MakeG868ResultNameDifference(captured);
        Assert.NotEqual(captured, nameDifference);
        Assert.Equal(0, TrxEvidenceReader.Read(nameDifference).Report!.Skipped);
        Assert.Contains(TrxEvidenceReader.Read(nameDifference).Report!.Discrepancies,
            item => item.Code == "definition-name-differs");
        WriteG868Record(host, "P1", "E1", nameDifference);
        WriteG868Record(host, "P2", "E2", nameDifference);

        var providers = 0;
        WorkerCompleteCommand.MutatorFactory = () =>
        {
            providers++;
            return new FakeMutator { Labels = [WorkerNextActionConstants.Labels.IntentPrUpdateInProgress] };
        };
        using var writer = new StringWriter();
        var headReads = 0;
        var exit = WorkerCompleteCommand.Execute(G868Context(host.Root),
            G868Arguments(host, format, ["--evidence", "P1=E1", "--evidence", "P2=E2"], write: false),
            writer,
            new WorkerVerificationGate.Dependencies(ReadPullRequestHeadSha: (_, _) =>
            {
                headReads++;
                return VerificationEvidenceCommandTests.Head;
            }));

        Assert.True(exit == 0, writer.ToString());
        Assert.Equal(2, headReads);
        Assert.Equal(1, providers);
        Assert.DoesNotContain("trx_base64", writer.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("testName", writer.ToString(), StringComparison.Ordinal);
        if (format == "json")
        {
            using var document = JsonDocument.Parse(writer.ToString());
            var proofs = document.RootElement.GetProperty("verification_gate").GetProperty("coverage")
                .GetProperty("proofs").EnumerateArray().ToArray();
            Assert.Equal(new[] { "P1", "P2" }, proofs.Select(proof => proof.GetProperty("proof_id").GetString()));
            Assert.All(proofs, proof =>
            {
                Assert.Equal("present", proof.GetProperty("state").GetString());
                Assert.Equal(0, proof.GetProperty("report").GetProperty("skipped").GetInt32());
                Assert.Equal("definition-name-differs",
                    proof.GetProperty("report").GetProperty("discrepancies")[0].GetProperty("code").GetString());
            });
        }
        else
        {
            Assert.Contains("worker-verification-satisfied", writer.ToString(), StringComparison.Ordinal);
            Assert.Contains("definition-name-differs", writer.ToString(), StringComparison.Ordinal);
            Assert.Contains("\"skipped\": 0", writer.ToString(), StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("json", "missing-unit")]
    [InlineData("json", "missing-domain")]
    [InlineData("json", "missing-team")]
    [InlineData("json", "missing-head")]
    [InlineData("json", "missing-packet")]
    [InlineData("json", "duplicate-marker")]
    [InlineData("json", "duplicate-context")]
    [InlineData("json", "duplicate-repo")]
    [InlineData("json", "duplicate-format")]
    [InlineData("json", "duplicate-kind")]
    [InlineData("json", "duplicate-number")]
    [InlineData("json", "duplicate-outcome")]
    [InlineData("json", "duplicate-pr")]
    [InlineData("json", "duplicate-domain")]
    [InlineData("json", "duplicate-mode")]
    [InlineData("json", "duplicate-child-cwd")]
    [InlineData("json", "duplicate-github-only")]
    [InlineData("json", "duplicate-accept-gap")]
    [InlineData("json", "write-and-dry-run")]
    [InlineData("json", "too-many-references")]
    [InlineData("json", "malformed-reference")]
    [InlineData("json", "duplicate-reference")]
    [InlineData("json", "case-alias-proof-reference")]
    [InlineData("json", "case-alias-record-reference")]
    [InlineData("json", "unknown-option")]
    [InlineData("json", "unsupported-kind")]
    [InlineData("json", "unsupported-outcome")]
    [InlineData("json", "recognized-format-order")]
    [InlineData("markdown", "missing-unit")]
    [InlineData("markdown", "missing-domain")]
    [InlineData("markdown", "missing-team")]
    [InlineData("markdown", "missing-head")]
    [InlineData("markdown", "missing-packet")]
    [InlineData("markdown", "duplicate-marker")]
    [InlineData("markdown", "duplicate-context")]
    [InlineData("markdown", "duplicate-repo")]
    [InlineData("markdown", "duplicate-format")]
    [InlineData("markdown", "duplicate-kind")]
    [InlineData("markdown", "duplicate-number")]
    [InlineData("markdown", "duplicate-outcome")]
    [InlineData("markdown", "duplicate-pr")]
    [InlineData("markdown", "duplicate-domain")]
    [InlineData("markdown", "duplicate-mode")]
    [InlineData("markdown", "duplicate-child-cwd")]
    [InlineData("markdown", "duplicate-github-only")]
    [InlineData("markdown", "duplicate-accept-gap")]
    [InlineData("markdown", "write-and-dry-run")]
    [InlineData("markdown", "too-many-references")]
    [InlineData("markdown", "malformed-reference")]
    [InlineData("markdown", "duplicate-reference")]
    [InlineData("markdown", "case-alias-proof-reference")]
    [InlineData("markdown", "case-alias-record-reference")]
    [InlineData("markdown", "unknown-option")]
    [InlineData("markdown", "unsupported-kind")]
    [InlineData("markdown", "unsupported-outcome")]
    [InlineData("markdown", "recognized-format-order")]
    public void G868InvalidOptInArgumentsUseMinimalEnvelopeAndReadNothing(string format, string mutation)
    {
        using var host = new VerificationEvidenceCommandTests.TempHost();
        var context = G868Context(host.Root);
        var headReads = 0;
        var coverageReads = 0;
        var providerReads = 0;
        WorkerCompleteCommand.MutatorFactory = () => { providerReads++; return new FakeMutator(); };
        WorkerCompleteCommand.PrLookupFactory = () => { providerReads++; return new PermissivePrLookup(); };
        WorkerCompleteCommand.IssueLookupFactory = () => { providerReads++; return new StubIssueLookup(); };
        var args = G868Arguments(host, format, includeEvidence: true).ToList();
        switch (mutation)
        {
            case "missing-unit": RemoveOption(args, "--verification-unit"); break;
            case "missing-domain": RemoveOption(args, "--verification-domain"); break;
            case "missing-team": RemoveOption(args, "--verification-team"); break;
            case "missing-head": RemoveOption(args, "--verification-head-sha"); break;
            case "missing-packet": RemoveOption(args, "--verification-packet-sha256"); break;
            case "duplicate-marker": args.Add("--require-verification"); break;
            case "duplicate-context": args.AddRange(["--verification-unit", host.Unit]); break;
            case "duplicate-repo": args.AddRange(["--repo", VerificationEvidenceCommandTests.Repo]); break;
            case "duplicate-format": args.AddRange(["--format", format]); break;
            case "duplicate-kind": args.AddRange(["--kind", "pr"]); break;
            case "duplicate-number": args.AddRange(["--number", "1901"]); break;
            case "duplicate-outcome": args.AddRange(["--outcome", WorkerResultSummaryConstants.Outcomes.RepairPushed]); break;
            case "duplicate-pr": args.AddRange(["--pr", "1901", "--pr", "1902"]); break;
            case "duplicate-domain": args.AddRange(["--domain", VerificationEvidenceCommandTests.Domain,
                "--domain", VerificationEvidenceCommandTests.Domain]); break;
            case "duplicate-mode": args.Add("--dry-run"); break;
            case "duplicate-child-cwd": args.AddRange(["--child-cwd", "--child-cwd"]); break;
            case "duplicate-github-only": args.Add("--github-only"); break;
            case "duplicate-accept-gap": args.AddRange(["--accept-evidence-gap", "x", "--accept-evidence-gap", "y"]); break;
            case "write-and-dry-run": args.Add("--write"); break;
            case "too-many-references":
                RemoveOption(args, "--evidence");
                for (var index = 1; index <= 17; index++)
                    args.AddRange(["--evidence", $"P{index}=E{index}"]);
                break;
            case "malformed-reference":
                var refIndex = args.IndexOf("P1=E1");
                args[refIndex] = "P1=E1=extra";
                break;
            case "duplicate-reference": args.AddRange(["--evidence", "P1=E1"]); break;
            case "case-alias-proof-reference": args.AddRange(["--evidence", "p1=E1"]); break;
            case "case-alias-record-reference": args.AddRange(["--evidence", "P2=e1"]); break;
            case "unknown-option": args.Add("--unrecognized-g868-option"); break;
            case "unsupported-kind": ReplaceOptionValue(args, "--kind", "comment"); break;
            case "unsupported-outcome": ReplaceOptionValue(args, "--outcome", "merged"); break;
            case "recognized-format-order":
                RemoveOption(args, "--format");
                args.AddRange(["--format", "json", "--format", "markdown"]);
                break;
            default: throw new ArgumentOutOfRangeException(nameof(mutation));
        }

        using var writer = new StringWriter();
        var exitCode = WorkerCompleteCommand.Execute(context, args.ToArray(), writer,
            new WorkerVerificationGate.Dependencies(
                ReadPullRequestHeadSha: (_, _) => { headReads++; return VerificationEvidenceCommandTests.Head; },
                BeforeCoverageOperation: (_, _) => coverageReads++));

        Assert.Equal(1, exitCode);
        Assert.Equal(0, headReads);
        Assert.Equal(0, coverageReads);
        Assert.Equal(0, providerReads);
        if (format == "json" || mutation == "recognized-format-order")
        {
            using var output = JsonDocument.Parse(writer.ToString());
            var root = output.RootElement;
            Assert.Equal(new[] { "command", "proceed", "applied", "verification_gate" },
                root.EnumerateObject().Select(property => property.Name));
            Assert.Equal("worker complete", root.GetProperty("command").GetString());
            Assert.False(root.GetProperty("proceed").GetBoolean());
            Assert.False(root.GetProperty("applied").GetBoolean());
            var gate = root.GetProperty("verification_gate");
            Assert.Equal("invalid", gate.GetProperty("state").GetString());
            Assert.Equal("worker-verification-invalid-arguments", gate.GetProperty("cause").GetString());
            Assert.Equal("arguments", gate.GetProperty("phase").GetString());
            Assert.Equal(JsonValueKind.Null, gate.GetProperty("target_pr").ValueKind);
            Assert.Equal(JsonValueKind.Null, gate.GetProperty("requested_head_sha").ValueKind);
            Assert.Equal(JsonValueKind.Null, gate.GetProperty("observed_head_sha").ValueKind);
            Assert.Equal(JsonValueKind.Null, gate.GetProperty("coverage").ValueKind);
        }
        else
        {
            Assert.Contains("worker-verification-invalid-arguments", writer.ToString(), StringComparison.Ordinal);
            Assert.Contains("phase: arguments", writer.ToString(), StringComparison.Ordinal);
            Assert.Contains("requested_head_sha: (unknown)", writer.ToString(), StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("json")]
    [InlineData("text")]
    public void G868MarkerConsumedAsLegacyValueDoesNotActivateOptIn(string format)
    {
        using var workspace = new WorkerCompleteWorkspace();
        var mutator = new FakeMutator
        {
            Labels = [WorkerNextActionConstants.Labels.IntentPrUpdateInProgress],
        };
        WorkerCompleteCommand.MutatorFactory = () => mutator;
        var headReads = 0;
        var coverageReads = 0;
        using var writer = new StringWriter();
        var args = new[]
        {
            "--repo", WorkerVerificationGate.Marker,
            "--kind", "pr",
            "--number", "1900",
            "--outcome", WorkerResultSummaryConstants.Outcomes.RepairPushed,
            "--github-only",
            "--dry-run",
            "--format", format,
        };

        var exitCode = WorkerCompleteCommand.Execute(workspace.Context, args, writer,
            new WorkerVerificationGate.Dependencies(
                ReadPullRequestHeadSha: (_, _) => { headReads++; return VerificationEvidenceCommandTests.Head; },
                BeforeCoverageOperation: (_, _) => coverageReads++));

        Assert.Equal(0, exitCode);
        Assert.Equal(0, headReads);
        Assert.Equal(0, coverageReads);
        Assert.Empty(mutator.AppliedTransitions);
        if (format == "json")
        {
            using var document = JsonDocument.Parse(writer.ToString());
            Assert.Equal(WorkerVerificationGate.Marker, document.RootElement.GetProperty("repo").GetString());
            Assert.False(document.RootElement.TryGetProperty("verification_gate", out _));
        }
        else
        {
            Assert.Contains(WorkerVerificationGate.Marker, writer.ToString(), StringComparison.Ordinal);
            Assert.DoesNotContain("Verification gate", writer.ToString(), StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("json")]
    [InlineData("markdown")]
    public void G868VerificationFlagsWithoutMarkerRemainLegacyUnknownArguments(string format)
    {
        using var workspace = new WorkerCompleteWorkspace();
        var providerReads = 0;
        WorkerCompleteCommand.MutatorFactory = () => { providerReads++; return new FakeMutator(); };
        var headReads = 0;
        var coverageReads = 0;
        using var writer = new StringWriter();
        var args = new[]
        {
            "--repo", VerificationEvidenceCommandTests.Repo,
            "--kind", "pr",
            "--number", "1900",
            "--outcome", WorkerResultSummaryConstants.Outcomes.RepairPushed,
            "--github-only",
            "--verification-unit", "G868",
            "--format", format,
        };

        var exitCode = WorkerCompleteCommand.Execute(workspace.Context, args, writer,
            new WorkerVerificationGate.Dependencies(
                ReadPullRequestHeadSha: (_, _) => { headReads++; return VerificationEvidenceCommandTests.Head; },
                BeforeCoverageOperation: (_, _) => coverageReads++));

        Assert.Equal(1, exitCode);
        Assert.Equal(0, providerReads);
        Assert.Equal(0, headReads);
        Assert.Equal(0, coverageReads);
        Assert.Contains("Unknown argument '--verification-unit'", writer.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("verification_gate", writer.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("json")]
    [InlineData("markdown")]
    public void G868SixteenEvidenceReferencesReachTypedCoverageRatherThanArgumentsRefusal(string format)
    {
        using var host = new VerificationEvidenceCommandTests.TempHost();
        var refs = Enumerable.Range(1, 16)
            .SelectMany(index => new[] { "--evidence", $"P{index}=E{index}" }).ToArray();
        var args = G868Arguments(host, format, refs, write: false);
        var headReads = 0;
        var providerReads = 0;
        WorkerCompleteCommand.MutatorFactory = () => { providerReads++; return new FakeMutator(); };
        using var writer = new StringWriter();

        var exitCode = WorkerCompleteCommand.Execute(G868Context(host.Root), args, writer,
            new WorkerVerificationGate.Dependencies(
                ReadPullRequestHeadSha: (_, _) => { headReads++; return VerificationEvidenceCommandTests.Head; }));

        Assert.Equal(2, exitCode);
        Assert.Equal(0, headReads);
        Assert.Equal(0, providerReads);
        if (format == "json")
        {
            using var document = JsonDocument.Parse(writer.ToString());
            var gate = document.RootElement.GetProperty("verification_gate");
            Assert.NotEqual("invalid", gate.GetProperty("state").GetString());
            Assert.NotEqual("worker-verification-invalid-arguments", gate.GetProperty("cause").GetString());
            Assert.NotEqual("arguments", gate.GetProperty("phase").GetString());
        }
        else
        {
            Assert.DoesNotContain("worker-verification-invalid-arguments", writer.ToString(), StringComparison.Ordinal);
            Assert.Contains("phase: initial", writer.ToString(), StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("json", null)]
    [InlineData("json", "malformed")]
    [InlineData("json", "trailing-space")]
    [InlineData("markdown", null)]
    [InlineData("markdown", "malformed")]
    [InlineData("markdown", "trailing-space")]
    public void G868LiveHeadMustBeAnExactLowercaseFullSha(string format, string? headVariant)
    {
        using var host = new VerificationEvidenceCommandTests.TempHost();
        WriteG868Record(host, "P1", "E1");
        var context = G868Context(host.Root);
        var mutatorCalls = 0;
        WorkerCompleteCommand.MutatorFactory = () => { mutatorCalls++; return new FakeMutator(); };
        var headReads = 0;
        string? returned = headVariant switch
        {
            null => null,
            "malformed" => "not-a-head-sha",
            "trailing-space" => VerificationEvidenceCommandTests.Head + " ",
            _ => throw new ArgumentOutOfRangeException(nameof(headVariant)),
        };
        using var writer = new StringWriter();
        var exitCode = WorkerCompleteCommand.Execute(G868Context(host.Root),
            G868Arguments(host, format, includeEvidence: true), writer,
            new WorkerVerificationGate.Dependencies(
                ReadPullRequestHeadSha: (_, _) => { headReads++; return returned; }));

        Assert.Equal(2, exitCode);
        Assert.Equal(1, headReads);
        Assert.Equal(0, mutatorCalls);
        if (format == "json")
        {
            using var document = JsonDocument.Parse(writer.ToString());
            var gate = document.RootElement.GetProperty("verification_gate");
            Assert.Equal("unavailable", gate.GetProperty("state").GetString());
            Assert.Equal("worker-verification-head-unavailable", gate.GetProperty("cause").GetString());
            Assert.Equal("initial", gate.GetProperty("phase").GetString());
            Assert.Equal("complete", gate.GetProperty("coverage").GetProperty("state").GetString());
            Assert.Equal(JsonValueKind.Null, gate.GetProperty("observed_head_sha").ValueKind);
        }
        else
        {
            Assert.Contains("worker-verification-head-unavailable", writer.ToString(), StringComparison.Ordinal);
            Assert.Contains("phase: initial", writer.ToString(), StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("json", "io")]
    [InlineData("json", "invalid-operation")]
    [InlineData("json", "unauthorized")]
    [InlineData("markdown", "io")]
    [InlineData("markdown", "invalid-operation")]
    [InlineData("markdown", "unauthorized")]
    public void G868InitialLiveHeadReadFailuresAreStructuredAndPrecedeProviders(string format, string failure)
    {
        using var host = new VerificationEvidenceCommandTests.TempHost();
        WriteG868Record(host, "P1", "E1");
        var headReads = 0;
        var providers = 0;
        WorkerCompleteCommand.MutatorFactory = () => { providers++; return new FakeMutator(); };
        WorkerCompleteCommand.IssueLookupFactory = () => { providers++; return new StubIssueLookup(); };
        WorkerCompleteCommand.PrLookupFactory = () => { providers++; return new PermissivePrLookup(); };
        using var writer = new StringWriter();

        var exit = WorkerCompleteCommand.Execute(G868Context(host.Root),
            G868Arguments(host, format, includeEvidence: true), writer,
            new WorkerVerificationGate.Dependencies(ReadPullRequestHeadSha: (_, _) =>
            {
                headReads++;
                throw failure switch
                {
                    "io" => new IOException("private path must not escape"),
                    "invalid-operation" => new InvalidOperationException("private path must not escape"),
                    "unauthorized" => new UnauthorizedAccessException("private path must not escape"),
                    _ => new ArgumentOutOfRangeException(nameof(failure)),
                };
            }));

        Assert.Equal(2, exit);
        Assert.Equal(1, headReads);
        Assert.Equal(0, providers);
        Assert.DoesNotContain(host.Root, writer.ToString(), StringComparison.Ordinal);
        if (format == "json")
        {
            using var document = JsonDocument.Parse(writer.ToString());
            var root = document.RootElement;
            var gate = root.GetProperty("verification_gate");
            Assert.False(root.GetProperty("proceed").GetBoolean());
            Assert.False(root.GetProperty("applied").GetBoolean());
            Assert.Equal("unavailable", gate.GetProperty("state").GetString());
            Assert.Equal("worker-verification-head-unavailable", gate.GetProperty("cause").GetString());
            Assert.Equal("initial", gate.GetProperty("phase").GetString());
            Assert.Equal(JsonValueKind.Null, gate.GetProperty("observed_head_sha").ValueKind);
            Assert.Equal("complete", gate.GetProperty("coverage").GetProperty("state").GetString());
        }
        else
        {
            Assert.Contains("worker-verification-head-unavailable", writer.ToString(), StringComparison.Ordinal);
            Assert.Contains("- phase: initial", writer.ToString(), StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("json", "malformed")]
    [InlineData("json", "noncanonical")]
    [InlineData("json", "tampered-trx")]
    [InlineData("json", "foreign-domain")]
    [InlineData("json", "foreign-team")]
    [InlineData("json", "foreign-unit")]
    [InlineData("json", "foreign-id")]
    [InlineData("json", "foreign-repository")]
    [InlineData("markdown", "malformed")]
    [InlineData("markdown", "noncanonical")]
    [InlineData("markdown", "tampered-trx")]
    [InlineData("markdown", "foreign-domain")]
    [InlineData("markdown", "foreign-team")]
    [InlineData("markdown", "foreign-unit")]
    [InlineData("markdown", "foreign-id")]
    [InlineData("markdown", "foreign-repository")]
    public void G868StoredRecordFailuresKeepTypedRefusalBeforeHeadAndProviders(string format, string mutation)
    {
        using var host = new VerificationEvidenceCommandTests.TempHost();
        WriteG868Record(host, "P1", "E1");
        var recordPath = G868RecordPath(host, "E1");
        if (mutation == "malformed")
        {
            File.WriteAllText(recordPath, "{ not a record");
        }
        else if (mutation == "noncanonical")
        {
            File.AppendAllText(recordPath, " \n");
        }
        else if (mutation == "tampered-trx")
        {
            var record = JsonSerializer.Deserialize<VerificationEvidenceRecord>(File.ReadAllBytes(recordPath))!;
            File.WriteAllBytes(recordPath, VerificationEvidenceStore.Serialize(
                record with { TrxSha256 = new string('0', 64) }));
        }
        else
        {
            var record = JsonSerializer.Deserialize<VerificationEvidenceRecord>(File.ReadAllBytes(recordPath))!;
            var foreign = mutation switch
            {
                "foreign-domain" => record with { Domain = "other-domain" },
                "foreign-team" => record with { Team = "other-team" },
                "foreign-unit" => record with { ExecutionUnit = "G999" },
                "foreign-id" => record with { Id = "E2" },
                "foreign-repository" => record with { TargetRepo = "Other/Repository" },
                _ => throw new ArgumentOutOfRangeException(nameof(mutation)),
            };
            File.WriteAllBytes(recordPath, VerificationEvidenceStore.Serialize(foreign));
        }
        var mutatedRecord = File.ReadAllBytes(recordPath);
        var headReads = 0;
        var providers = 0;
        WorkerCompleteCommand.MutatorFactory = () => { providers++; return new FakeMutator(); };
        WorkerCompleteCommand.PrLookupFactory = () => { providers++; return new PermissivePrLookup(); };
        using var writer = new StringWriter();

        var exit = WorkerCompleteCommand.Execute(G868Context(host.Root),
            G868Arguments(host, format, includeEvidence: true), writer,
            new WorkerVerificationGate.Dependencies(
                ReadPullRequestHeadSha: (_, _) => { headReads++; return VerificationEvidenceCommandTests.Head; }));

        Assert.Equal(2, exit);
        Assert.Equal(0, headReads);
        Assert.Equal(0, providers);
        Assert.Equal(mutatedRecord, File.ReadAllBytes(recordPath));
        if (format == "json")
        {
            using var document = JsonDocument.Parse(writer.ToString());
            var root = document.RootElement;
            var gate = root.GetProperty("verification_gate");
            Assert.Equal("unavailable", gate.GetProperty("state").GetString());
            Assert.Equal("worker-verification-coverage-unavailable", gate.GetProperty("cause").GetString());
            Assert.Equal("initial", gate.GetProperty("phase").GetString());
            var coverage = gate.GetProperty("coverage");
            Assert.Equal("unavailable", coverage.GetProperty("state").GetString());
            var proof = Assert.Single(coverage.GetProperty("proofs").EnumerateArray().ToArray());
            Assert.Equal("unavailable", proof.GetProperty("state").GetString());
            Assert.Equal(JsonValueKind.Null, proof.GetProperty("report").ValueKind);
            Assert.Null(proof.GetProperty("artifact_path").GetString());
            Assert.Null(proof.GetProperty("artifact_sha256").GetString());
        }
        else
        {
            Assert.Contains("worker-verification-coverage-unavailable", writer.ToString(), StringComparison.Ordinal);
            Assert.Contains("verification-coverage-record-unavailable", writer.ToString(), StringComparison.Ordinal);
            Assert.Contains("- phase: initial", writer.ToString(), StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("json")]
    [InlineData("markdown")]
    public void G868UnavailableFirstProofPreservesSecondVerifiedProofFacts(string format)
    {
        using var host = new VerificationEvidenceCommandTests.TempHost(packet: G868TwoProofPacket);
        WriteG868Record(host, "P1", "E1");
        WriteG868Record(host, "P2", "E2");
        var firstPath = G868RecordPath(host, "E1");
        File.AppendAllText(firstPath, " \n");
        var firstBytes = File.ReadAllBytes(firstPath);
        var secondPath = G868RecordPath(host, "E2");
        var secondBytes = File.ReadAllBytes(secondPath);
        var secondSha = VerificationEvidenceCommandTests.Hash(secondBytes);
        var headReads = 0;
        var providers = 0;
        WorkerCompleteCommand.MutatorFactory = () => { providers++; return new FakeMutator(); };
        WorkerCompleteCommand.PrLookupFactory = () => { providers++; return new PermissivePrLookup(); };
        using var writer = new StringWriter();

        var exit = WorkerCompleteCommand.Execute(G868Context(host.Root),
            G868Arguments(host, format, ["--evidence", "P1=E1", "--evidence", "P2=E2"], write: false),
            writer,
            new WorkerVerificationGate.Dependencies(
                ReadPullRequestHeadSha: (_, _) => { headReads++; return VerificationEvidenceCommandTests.Head; }));

        Assert.Equal(2, exit);
        Assert.Equal(0, headReads);
        Assert.Equal(0, providers);
        Assert.Equal(firstBytes, File.ReadAllBytes(firstPath));
        Assert.Equal(secondBytes, File.ReadAllBytes(secondPath));
        if (format == "json")
        {
            using var document = JsonDocument.Parse(writer.ToString());
            var gate = document.RootElement.GetProperty("verification_gate");
            var coverage = gate.GetProperty("coverage");
            Assert.Equal("unavailable", coverage.GetProperty("state").GetString());
            var proofs = coverage.GetProperty("proofs").EnumerateArray().ToArray();
            var first = proofs.Single(proof => proof.GetProperty("proof_id").GetString() == "P1");
            var second = proofs.Single(proof => proof.GetProperty("proof_id").GetString() == "P2");
            Assert.Equal("unavailable", first.GetProperty("state").GetString());
            Assert.Equal("verification-coverage-record-unavailable", first.GetProperty("cause").GetString());
            Assert.Equal("present", second.GetProperty("state").GetString());
            Assert.Equal("E2", second.GetProperty("record_id").GetString());
            Assert.Equal(secondSha, second.GetProperty("artifact_sha256").GetString());
            Assert.Equal(2, second.GetProperty("report").GetProperty("total").GetInt32());
        }
        else
        {
            Assert.Contains("verification-coverage-record-unavailable", writer.ToString(), StringComparison.Ordinal);
            Assert.Contains("verification-coverage-present", writer.ToString(), StringComparison.Ordinal);
            Assert.Contains("\"record_id\": \"E2\"", writer.ToString(), StringComparison.Ordinal);
            Assert.Contains(secondSha, writer.ToString(), StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("json")]
    [InlineData("markdown")]
    public void G868EvidenceGapCannotBypassVerificationAndReasonedOverrideRemainsExplicit(string format)
    {
        const string issueBody = """
            # G785 fixture

            ## Acceptance Criteria

            - Criterion one — actual output pasted.
            - Criterion two — actual counts pasted.
            - Criterion three — actual output pasted.
            """;
        const string aggregateOnlyBody = """
            Closes #525

            ## Validation

            All three checks passed.
            """;
        using var host = new VerificationEvidenceCommandTests.TempHost(packet: G868TwoProofPacket);
        WriteG868Record(host, "P1", "E1");
        WriteG868Record(host, "P2", "E2");
        WorkerCompleteCommand.PrLookupFactory = () => new StubPrLookup
        {
            Body = aggregateOnlyBody,
            ClosingIssuesReferences = [new GitHubPrClosingIssueReference { Number = 525, Repository = null }],
        };
        WorkerCompleteCommand.IssueLookupFactory = () => new StubIssueLookup { Body = issueBody };
        var mutator = new FakeMutator { Labels = ["intent-target", "intent-issue-in-progress"] };
        WorkerCompleteCommand.MutatorFactory = () => mutator;
        var headReads = 0;
        var args = G868IssueArguments(host, format,
            ["--evidence", "P1=E1", "--evidence", "P2=E2"], write: true);

        using var noReason = new StringWriter();
        var gapExit = WorkerCompleteCommand.Execute(G868Context(host.Root), args, noReason,
            new WorkerVerificationGate.Dependencies(
                ReadPullRequestHeadSha: (_, _) => { headReads++; return VerificationEvidenceCommandTests.Head; }));
        Assert.Equal(1, gapExit);
        Assert.Equal(1, headReads);
        Assert.Empty(mutator.AppliedTransitions);
        Assert.Contains("evidence gap (G785)", noReason.ToString(), StringComparison.Ordinal);

        var waivedArgs = args.ToList();
        waivedArgs.Insert(waivedArgs.IndexOf("--write"), "--accept-evidence-gap");
        waivedArgs.Insert(waivedArgs.IndexOf("--write"), "operator recorded missing pasted output");
        using var withReason = new StringWriter();
        var acceptedExit = WorkerCompleteCommand.Execute(G868Context(host.Root), waivedArgs.ToArray(), withReason,
            new WorkerVerificationGate.Dependencies(
                ReadPullRequestHeadSha: (_, _) => { headReads++; return VerificationEvidenceCommandTests.Head; }));
        Assert.Equal(0, acceptedExit);
        Assert.Equal(3, headReads);
        if (format == "json")
        {
            var accepted = JsonSerializer.Deserialize<WorkerCompleteResult>(withReason.ToString())!;
            Assert.Equal("operator recorded missing pasted output", accepted.EvidenceGapAccepted);
            Assert.Equal("satisfied", accepted.VerificationGate!.State);
        }
        else
        {
            Assert.Contains("- evidence_gap_accepted: operator recorded missing pasted output", withReason.ToString(), StringComparison.Ordinal);
            Assert.Contains("- state: satisfied", withReason.ToString(), StringComparison.Ordinal);
        }
        Assert.Equal(2, mutator.AppliedTransitions.Count);

        var invalidArgs = G868IssueArguments(host, format, Array.Empty<string>(), write: true).ToList();
        invalidArgs.Insert(invalidArgs.IndexOf("--write"), "--accept-evidence-gap");
        invalidArgs.Insert(invalidArgs.IndexOf("--write"), "operator recorded missing pasted output");
        using var invalidGate = new StringWriter();
        var invalidExit = WorkerCompleteCommand.Execute(G868Context(host.Root), invalidArgs.ToArray(), invalidGate,
            new WorkerVerificationGate.Dependencies(
                ReadPullRequestHeadSha: (_, _) => { headReads++; return VerificationEvidenceCommandTests.Head; }));
        Assert.Equal(2, invalidExit);
        Assert.Equal(3, headReads);
        Assert.Equal(2, mutator.AppliedTransitions.Count);
        Assert.Contains("worker-verification-coverage-incomplete", invalidGate.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("json")]
    [InlineData("markdown")]
    public void G868SeparateEvidenceBranchPassesAtModeledPrHeadAndRefusesAfterPrAdvance(string format)
    {
        var tempBase = OperatingSystem.IsMacOS() && Directory.Exists("/private/tmp")
            ? "/private/tmp"
            : Path.GetTempPath();
        var root = Path.Combine(tempBase, "intent-cli-g868-worker-git-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var bare = Path.Combine(root, "origin.git");
            var seed = Path.Combine(root, "seed");
            var writerRepo = Path.Combine(root, "writer");
            var evidenceClone = Path.Combine(root, "evidence-clone");
            Directory.CreateDirectory(seed);
            RunG868Git(root, "init", "--bare", "--quiet", "--initial-branch=pr", bare);
            RunG868Git(seed, "init", "--quiet", "--initial-branch=pr");
            RunG868Git(seed, "config", "user.name", "intent-cli-g868-test");
            RunG868Git(seed, "config", "user.email", "intent-cli-g868@example.invalid");
            RunG868Git(seed, "remote", "add", "origin", bare);
            var packetRelative = Path.Combine(".intent-cli", "issues", "G866", "packet.yaml");
            var packetBytes = Encoding.UTF8.GetBytes(G868TwoProofPacket + "\n");
            var packetPath = Path.Combine(seed, packetRelative);
            Directory.CreateDirectory(Path.GetDirectoryName(packetPath)!);
            File.WriteAllBytes(packetPath, packetBytes);
            File.WriteAllText(Path.Combine(seed, "README.md"), "G868 modeled PR branch H1.\n");
            RunG868Git(seed, "add", "--", packetRelative, "README.md");
            RunG868Git(seed, "commit", "--quiet", "-m", "seed G868 modeled PR branch");
            var h1 = RunG868Git(seed, "rev-parse", "HEAD");
            RunG868Git(seed, "push", "--quiet", "origin", "HEAD:refs/heads/pr");
            Assert.Equal(h1, RunG868Git(bare, "rev-parse", "refs/heads/pr"));

            RunG868Git(root, "clone", "--quiet", "--branch", "pr", bare, writerRepo);
            RunG868Git(writerRepo, "config", "user.name", "intent-cli-g868-test");
            RunG868Git(writerRepo, "config", "user.email", "intent-cli-g868@example.invalid");
            RunG868Git(writerRepo, "checkout", "--quiet", "-b", "evidence");
            var sourceTrx = Path.Combine(writerRepo, "capture.trx");
            File.WriteAllBytes(sourceTrx, VerificationEvidenceCommandTests.ReadSmallVstest());
            var packetSha = Convert.ToHexString(SHA256.HashData(packetBytes)).ToLowerInvariant();
            foreach (var (proof, id) in new[] { ("P1", "E1"), ("P2", "E2") })
            {
                var recordArgs = new[]
                {
                    "record", "--id", id, "--execution-unit", "G866", "--domain", "intent-cli",
                    "--team", "intent-cli-dev", "--repo", VerificationEvidenceCommandTests.Repo,
                    "--head-sha", h1, "--packet-sha256", packetSha, "--proof", proof,
                    "--trx", "capture.trx", "--format", "json", "--write",
                };
                using var recordWriter = new StringWriter();
                Assert.Equal(0, VerificationEvidenceCommand.Execute(writerRepo, recordArgs, recordWriter));
                using var record = JsonDocument.Parse(recordWriter.ToString());
                Assert.Equal("recorded", record.RootElement.GetProperty("disposition").GetString());
                Assert.True(record.RootElement.GetProperty("wrote").GetBoolean());
            }
            File.Delete(sourceTrx);
            RunG868Git(writerRepo, "add", "--", ".intent-cli/verification-evidence");
            RunG868Git(writerRepo, "commit", "--quiet", "-m", "publish G868 evidence on separate branch");
            var evidenceHead = RunG868Git(writerRepo, "rev-parse", "HEAD");
            Assert.NotEqual(h1, evidenceHead);
            RunG868Git(writerRepo, "push", "--quiet", "origin", "HEAD:refs/heads/evidence");
            Assert.Equal(h1, RunG868Git(bare, "rev-parse", "refs/heads/pr"));
            Assert.Equal(evidenceHead, RunG868Git(bare, "rev-parse", "refs/heads/evidence"));

            RunG868Git(root, "clone", "--quiet", "--branch", "evidence", bare, evidenceClone);
            Assert.Equal(evidenceHead, RunG868Git(evidenceClone, "rev-parse", "HEAD"));
            Assert.False(File.Exists(Path.Combine(evidenceClone, "capture.trx")),
                "A fresh evidence checkout contains the preserved record but not the original raw input path.");
            Assert.Equal(h1, RunG868Git(bare, "rev-parse", "refs/heads/pr"));
            var before = SnapshotG868GitState(evidenceClone, bare);
            var liveHeadReads = 0;
            var mutator = new FakeMutator { Labels = [WorkerNextActionConstants.Labels.IntentPrUpdateInProgress] };
            WorkerCompleteCommand.MutatorFactory = () => mutator;

            string[] GateArgs(string requested, string selectedFormat, bool write) =>
            [
                "--repo", VerificationEvidenceCommandTests.Repo,
                "--kind", "pr", "--number", "1900",
                "--outcome", WorkerResultSummaryConstants.Outcomes.RepairPushed,
                "--github-only", "--require-verification",
                "--verification-unit", "G866", "--verification-domain", "intent-cli",
                "--verification-team", "intent-cli-dev", "--verification-head-sha", requested,
                "--verification-packet-sha256", packetSha, "--evidence", "P1=E1",
                "--evidence", "P2=E2",
                write ? "--write" : "--dry-run", "--format", selectedFormat,
            ];

            using (var writer = new StringWriter())
            {
                var exit = WorkerCompleteCommand.Execute(G868Context(evidenceClone),
                    GateArgs(h1, format, write: false), writer,
                    new WorkerVerificationGate.Dependencies(ReadPullRequestHeadSha: (repo, number) =>
                    {
                        Assert.Equal(VerificationEvidenceCommandTests.Repo, repo);
                        Assert.Equal(1900, number);
                        liveHeadReads++;
                        return RunG868Git(bare, "rev-parse", "refs/heads/pr");
                    }));
                Assert.True(exit == 0, writer.ToString());
                Assert.Equal(2, liveHeadReads);
                Assert.Empty(mutator.AppliedTransitions);
                if (format == "json")
                {
                    using var result = JsonDocument.Parse(writer.ToString());
                    var gate = result.RootElement.GetProperty("verification_gate");
                    Assert.True(result.RootElement.GetProperty("proceed").GetBoolean());
                    Assert.Equal("satisfied", gate.GetProperty("state").GetString());
                    Assert.Equal(h1, gate.GetProperty("observed_head_sha").GetString());
                }
                else
                {
                    Assert.Contains("worker-verification-satisfied", writer.ToString(), StringComparison.Ordinal);
                    Assert.Contains(h1, writer.ToString(), StringComparison.Ordinal);
                }
            }
            Assert.Equal(before, SnapshotG868GitState(evidenceClone, bare));

            // Advance only the modeled PR ref to the evidence commit. The evidence
            // commit is E1, while the earlier record still binds to H1.
            RunG868Git(bare, "update-ref", "refs/heads/pr", evidenceHead);
            Assert.Equal(evidenceHead, RunG868Git(bare, "rev-parse", "refs/heads/pr"));
            var afterAdvance = SnapshotG868GitState(evidenceClone, bare);
            using (var writer = new StringWriter())
            {
                var exit = WorkerCompleteCommand.Execute(G868Context(evidenceClone),
                    GateArgs(h1, format, write: false), writer,
                    new WorkerVerificationGate.Dependencies(ReadPullRequestHeadSha: (_, _) =>
                    {
                        liveHeadReads++;
                        return RunG868Git(bare, "rev-parse", "refs/heads/pr");
                    }));
                Assert.Equal(2, exit);
                Assert.Equal(3, liveHeadReads);
                Assert.Empty(mutator.AppliedTransitions);
                if (format == "json")
                {
                    using var result = JsonDocument.Parse(writer.ToString());
                    var gate = result.RootElement.GetProperty("verification_gate");
                    Assert.Equal("refused", gate.GetProperty("state").GetString());
                    Assert.Equal("worker-verification-head-mismatch", gate.GetProperty("cause").GetString());
                    Assert.Equal(h1, gate.GetProperty("requested_head_sha").GetString());
                    Assert.Equal(evidenceHead, gate.GetProperty("observed_head_sha").GetString());
                }
                else
                {
                    Assert.Contains("worker-verification-head-mismatch", writer.ToString(), StringComparison.Ordinal);
                    Assert.Contains(evidenceHead, writer.ToString(), StringComparison.Ordinal);
                }
            }
            Assert.Equal(afterAdvance, SnapshotG868GitState(evidenceClone, bare));

            // Even when the caller names the new head, the H1-bound report is stale;
            // an attempted write refuses before another live-head observation.
            using (var writer = new StringWriter())
            {
                var exit = WorkerCompleteCommand.Execute(G868Context(evidenceClone),
                    GateArgs(evidenceHead, format, write: true), writer,
                    new WorkerVerificationGate.Dependencies(ReadPullRequestHeadSha: (_, _) =>
                    {
                        liveHeadReads++;
                        return RunG868Git(bare, "rev-parse", "refs/heads/pr");
                    }));
                Assert.Equal(2, exit);
                Assert.Equal(3, liveHeadReads);
                Assert.Empty(mutator.AppliedTransitions);
                if (format == "json")
                {
                    using var result = JsonDocument.Parse(writer.ToString());
                    var gate = result.RootElement.GetProperty("verification_gate");
                    Assert.Equal("worker-verification-coverage-incomplete", gate.GetProperty("cause").GetString());
                    Assert.Equal("initial", gate.GetProperty("phase").GetString());
                }
                else
                {
                    Assert.Contains("worker-verification-coverage-incomplete", writer.ToString(), StringComparison.Ordinal);
                    Assert.Contains("- phase: initial", writer.ToString(), StringComparison.Ordinal);
                }
            }
            Assert.Equal(afterAdvance, SnapshotG868GitState(evidenceClone, bare));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("json")]
    [InlineData("markdown")]
    public void G868ExplicitDomainDisagreementRefusesBeforeCoverageOrHead(string format)
    {
        using var host = new VerificationEvidenceCommandTests.TempHost();
        WriteG868Record(host, "P1", "E1");
        var args = G868Arguments(host, format, includeEvidence: true).ToList();
        var repoIndex = args.IndexOf("--repo");
        args.InsertRange(repoIndex, ["--domain", "different-domain"]);
        var headReads = 0;
        var coverageReads = 0;
        using var writer = new StringWriter();
        var exitCode = WorkerCompleteCommand.Execute(G868Context(host.Root), args.ToArray(), writer,
            new WorkerVerificationGate.Dependencies(
                ReadPullRequestHeadSha: (_, _) => { headReads++; return VerificationEvidenceCommandTests.Head; },
                BeforeCoverageOperation: (_, _) => coverageReads++));

        Assert.Equal(2, exitCode);
        Assert.Equal(0, headReads);
        Assert.Equal(0, coverageReads);
        if (format == "json")
        {
            using var document = JsonDocument.Parse(writer.ToString());
            var gate = document.RootElement.GetProperty("verification_gate");
            Assert.Equal("refused", gate.GetProperty("state").GetString());
            Assert.Equal("worker-verification-identity-conflict", gate.GetProperty("cause").GetString());
            Assert.Equal("initial", gate.GetProperty("phase").GetString());
            Assert.Equal(JsonValueKind.Null, gate.GetProperty("coverage").ValueKind);
        }
        else
        {
            Assert.Contains("worker-verification-identity-conflict", writer.ToString(), StringComparison.Ordinal);
            Assert.Contains("phase: initial", writer.ToString(), StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("json", "no-references", "refused", "worker-verification-coverage-incomplete")]
    [InlineData("json", "packet-digest", "unavailable", "worker-verification-coverage-unavailable")]
    [InlineData("json", "stale-head", "refused", "worker-verification-coverage-incomplete")]
    [InlineData("json", "failed-report", "refused", "worker-verification-coverage-incomplete")]
    [InlineData("json", "skipped-report", "refused", "worker-verification-coverage-incomplete")]
    [InlineData("json", "zero-report", "refused", "worker-verification-coverage-incomplete")]
    [InlineData("json", "counter-zero-skipped-report", "refused", "worker-verification-coverage-incomplete")]
    [InlineData("json", "failed-and-skipped-report", "refused", "worker-verification-coverage-incomplete")]
    [InlineData("json", "tampered-summary", "unavailable", "worker-verification-coverage-unavailable")]
    [InlineData("markdown", "no-references", "refused", "worker-verification-coverage-incomplete")]
    [InlineData("markdown", "packet-digest", "unavailable", "worker-verification-coverage-unavailable")]
    [InlineData("markdown", "stale-head", "refused", "worker-verification-coverage-incomplete")]
    [InlineData("markdown", "failed-report", "refused", "worker-verification-coverage-incomplete")]
    [InlineData("markdown", "skipped-report", "refused", "worker-verification-coverage-incomplete")]
    [InlineData("markdown", "zero-report", "refused", "worker-verification-coverage-incomplete")]
    [InlineData("markdown", "counter-zero-skipped-report", "refused", "worker-verification-coverage-incomplete")]
    [InlineData("markdown", "failed-and-skipped-report", "refused", "worker-verification-coverage-incomplete")]
    [InlineData("markdown", "tampered-summary", "unavailable", "worker-verification-coverage-unavailable")]
    public void G868InitialCoverageFailuresPreserveTypedCauseAndPrecedeHead(string format, string mutation,
        string expectedState, string expectedCause)
    {
        using var host = new VerificationEvidenceCommandTests.TempHost();
        var args = G868Arguments(host, format, includeEvidence: true).ToList();
        switch (mutation)
        {
            case "no-references": RemoveOption(args, "--evidence"); break;
            case "packet-digest":
                ReplaceOptionValue(args, "--verification-packet-sha256", new string('a', 64));
                break;
            case "stale-head":
                WriteG868Record(host, "P1", "E1");
                ReplaceOptionValue(args, "--verification-head-sha", new string('a', 40));
                ReplaceOptionValue(args, "--verification-packet-sha256", VerificationEvidenceCommandTests.Hash(host.PacketBytes));
                break;
            case "failed-report":
                WriteG868Record(host, "P1", "E1", VerificationEvidenceCommandTests.MutateSmallTrx("failed-row"));
                break;
            case "skipped-report":
                WriteG868Record(host, "P1", "E1", VerificationEvidenceCommandTests.MutateSmallTrx("skipped-row"));
                break;
            case "zero-report":
                WriteG868Record(host, "P1", "E1", VerificationEvidenceCommandTests.MutateSmallTrx("zero-row"));
                break;
            case "counter-zero-skipped-report":
            {
                var captured = VerificationEvidenceCommandTests.ReadFullVstest();
                using var capturedStream = new MemoryStream(captured);
                var capturedRoot = XDocument.Load(capturedStream).Root!;
                XNamespace trxNamespace = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";
                var capturedCounters = capturedRoot.Element(trxNamespace + "ResultSummary")!
                    .Element(trxNamespace + "Counters")!;
                Assert.Equal("0", (string?)capturedCounters.Attribute("notExecuted"));
                Assert.Equal(6, capturedRoot.Element(trxNamespace + "Results")!
                    .Elements(trxNamespace + "UnitTestResult")
                    .Count(result => string.Equals((string?)result.Attribute("outcome"), "NotExecuted", StringComparison.Ordinal)));
                WriteG868Record(host, "P1", "E1", captured);
                var capturedRecord = JsonSerializer.Deserialize<VerificationEvidenceRecord>(
                    File.ReadAllBytes(G868RecordPath(host, "E1")))!;
                Assert.Equal(6, capturedRecord.Report.Skipped);
                break;
            }
            case "failed-and-skipped-report":
                WriteG868Record(host, "P1", "E1", MakeG868FailedAndSkippedTrx());
                break;
            case "tampered-summary":
                WriteG868Record(host, "P1", "E1");
                var storedPath = G868RecordPath(host, "E1");
                var stored = JsonSerializer.Deserialize<VerificationEvidenceRecord>(File.ReadAllBytes(storedPath))!;
                File.WriteAllBytes(storedPath, VerificationEvidenceStore.Serialize(stored with
                {
                    Report = stored.Report with { Passed = stored.Report.Passed + 1 },
                }));
                break;
            default: throw new ArgumentOutOfRangeException(nameof(mutation));
        }

        var headReads = 0;
        var coverageReads = 0;
        var mutatorReads = 0;
        WorkerCompleteCommand.MutatorFactory = () => { mutatorReads++; return new FakeMutator(); };
        using var writer = new StringWriter();
        var exitCode = WorkerCompleteCommand.Execute(G868Context(host.Root), args.ToArray(), writer,
            new WorkerVerificationGate.Dependencies(
                ReadPullRequestHeadSha: (_, _) => { headReads++; return VerificationEvidenceCommandTests.Head; },
                BeforeCoverageOperation: (_, _) => coverageReads++));

        Assert.Equal(2, exitCode);
        Assert.Equal(0, headReads);
        Assert.Equal(0, mutatorReads);
        Assert.True(coverageReads > 0);
        if (format == "json")
        {
            using var document = JsonDocument.Parse(writer.ToString());
            var gate = document.RootElement.GetProperty("verification_gate");
            Assert.Equal(expectedState, gate.GetProperty("state").GetString());
            Assert.Equal(expectedCause, gate.GetProperty("cause").GetString());
            Assert.Equal("initial", gate.GetProperty("phase").GetString());
            Assert.True(gate.GetProperty("coverage").GetProperty("diagnostics").GetArrayLength() >= 0);
            if (mutation == "failed-report")
                Assert.Contains("verification-coverage-reported-failed", JsonSerializer.Serialize(gate.GetProperty("coverage")), StringComparison.Ordinal);
            if (mutation == "skipped-report")
                Assert.Contains("verification-coverage-reported-skipped", JsonSerializer.Serialize(gate.GetProperty("coverage")), StringComparison.Ordinal);
            if (mutation == "zero-report")
                Assert.Contains("verification-coverage-no-results", JsonSerializer.Serialize(gate.GetProperty("coverage")), StringComparison.Ordinal);
            if (mutation == "counter-zero-skipped-report")
            {
                var row = gate.GetProperty("coverage").GetProperty("proofs")[0];
                Assert.Equal("verification-coverage-reported-skipped", row.GetProperty("cause").GetString());
                Assert.Equal(0, row.GetProperty("report").GetProperty("raw_counters").GetProperty("not_executed").GetInt32());
                Assert.Equal(6, row.GetProperty("report").GetProperty("skipped").GetInt32());
            }
            if (mutation == "failed-and-skipped-report")
            {
                var report = gate.GetProperty("coverage").GetProperty("proofs")[0].GetProperty("report");
                Assert.Equal(1, report.GetProperty("failed").GetInt32());
                Assert.Equal(1, report.GetProperty("skipped").GetInt32());
                Assert.Equal("verification-coverage-reported-failed",
                    gate.GetProperty("coverage").GetProperty("proofs")[0].GetProperty("cause").GetString());
            }
            if (mutation == "tampered-summary")
            {
                Assert.Equal("verification-coverage-record-unavailable",
                    gate.GetProperty("coverage").GetProperty("proofs")[0].GetProperty("cause").GetString());
                Assert.Null(gate.GetProperty("coverage").GetProperty("proofs")[0].GetProperty("artifact_sha256").GetString());
            }
        }
        else
        {
            Assert.Contains(expectedCause, writer.ToString(), StringComparison.Ordinal);
            Assert.Contains("phase: initial", writer.ToString(), StringComparison.Ordinal);
            Assert.Contains("- coverage:", writer.ToString(), StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("json", "contract-absent", "worker-verification-coverage-unavailable")]
    [InlineData("markdown", "contract-absent", "worker-verification-coverage-unavailable")]
    [InlineData("json", "contract-null", "worker-verification-coverage-unavailable")]
    [InlineData("markdown", "contract-null", "worker-verification-coverage-unavailable")]
    [InlineData("json", "contract-invalid", "worker-verification-coverage-unavailable")]
    [InlineData("markdown", "contract-invalid", "worker-verification-coverage-unavailable")]
    [InlineData("json", "uncovered-criterion", "worker-verification-coverage-incomplete")]
    [InlineData("markdown", "uncovered-criterion", "worker-verification-coverage-incomplete")]
    [InlineData("json", "uncovered-lane", "worker-verification-coverage-incomplete")]
    [InlineData("markdown", "uncovered-lane", "worker-verification-coverage-incomplete")]
    [InlineData("json", "heavy-local-missing", "worker-verification-unsupported-evidence")]
    [InlineData("markdown", "heavy-local-missing", "worker-verification-unsupported-evidence")]
    public void G868InitialPacketContractAndStructuralGapsRefuseBeforeHeadOrLegacyProviders(
        string format, string mutation, string expectedCause)
    {
        using var host = new VerificationEvidenceCommandTests.TempHost(packet: G868TwoProofPacket);
        WriteG868Record(host, "P1", "E1");
        WriteG868Record(host, "P2", "E2");
        var alternate = mutation switch
        {
            "contract-absent" => G868TwoProofPacket[..G868TwoProofPacket.IndexOf("verification_contract:", StringComparison.Ordinal)],
            "contract-null" => G868TwoProofPacket[..G868TwoProofPacket.IndexOf("verification_contract:", StringComparison.Ordinal)]
                + "verification_contract: null\n",
            "contract-invalid" => ReplaceFirst(G868TwoProofPacket, "version: 1", "version: 2"),
            "uncovered-criterion" => ReplaceFirst(G868TwoProofPacket,
                "- Preserve second local criterion\n", "- Preserve second local criterion\n    - Add a third covered criterion\n")
                .Replace("- {id: AC2, criterion_index: 2}\n",
                    "- {id: AC2, criterion_index: 2}\n    - {id: AC3, criterion_index: 3}\n", StringComparison.Ordinal),
            "uncovered-lane" => ReplaceFirst(G868TwoProofPacket,
                "- {id: build, weight: ordinary}\n",
                "- {id: build, weight: ordinary}\n    - {id: extra, weight: ordinary}\n")
                .Replace("affected_lanes: [unit, build]", "affected_lanes: [unit, build, extra]", StringComparison.Ordinal),
            "heavy-local-missing" => ReplaceFirst(
                ReplaceFirst(G868TwoProofPacket, "id: unit, weight: ordinary", "id: unit, weight: heavy"),
                "evidence: local-required", "evidence: hosted-required"),
            _ => throw new ArgumentOutOfRangeException(nameof(mutation)),
        };
        Assert.NotEqual(G868TwoProofPacket, alternate);
        var structural = PacketVerificationContract.Evaluate(
            host.Unit, $".intent-cli/issues/{host.Unit}/packet.yaml", Encoding.UTF8.GetBytes(alternate + "\n"));
        Assert.Equal(mutation switch
        {
            "contract-absent" => "not-declared",
            "contract-null" => "invalid",
            "contract-invalid" => "invalid",
            _ => "incomplete",
        }, structural.State);
        if (mutation == "contract-null") Assert.Equal("verification-contract-invalid", structural.Cause);
        host.WritePacket(Encoding.UTF8.GetBytes(alternate + "\n"));
        var packetSha = VerificationEvidenceCommandTests.Hash(host.PacketBytes);
        foreach (var id in mutation == "heavy-local-missing" ? new[] { "E2" } : new[] { "E1", "E2" })
        {
            var path = G868RecordPath(host, id);
            var record = JsonSerializer.Deserialize<VerificationEvidenceRecord>(File.ReadAllBytes(path))!;
            File.WriteAllBytes(path, VerificationEvidenceStore.Serialize(record with { PacketSha256 = packetSha }));
            Assert.True(VerificationEvidenceStore.TryParse(File.ReadAllBytes(path), out _, out var detail), detail);
        }

        var references = mutation == "heavy-local-missing"
            ? new[] { "--evidence", "P2=E2" }
            : new[] { "--evidence", "P1=E1", "--evidence", "P2=E2" };
        var args = G868Arguments(host, format, references, write: false).ToList();
        ReplaceOptionValue(args, "--verification-packet-sha256", packetSha);
        var headReads = 0;
        var providers = 0;
        var coverageReads = 0;
        WorkerCompleteCommand.MutatorFactory = () => { providers++; return new FakeMutator(); };
        WorkerCompleteCommand.PrLookupFactory = () => { providers++; return new PermissivePrLookup(); };
        WorkerCompleteCommand.IssueLookupFactory = () => { providers++; return new StubIssueLookup(); };
        using var writer = new StringWriter();
        var exit = WorkerCompleteCommand.Execute(G868Context(host.Root), args.ToArray(), writer,
            new WorkerVerificationGate.Dependencies(
                ReadPullRequestHeadSha: (_, _) => { headReads++; return VerificationEvidenceCommandTests.Head; },
                BeforeCoverageOperation: (_, _) => coverageReads++));

        Assert.Equal(2, exit);
        Assert.Equal(0, headReads);
        Assert.Equal(0, providers);
        Assert.True(coverageReads > 0);
        Assert.DoesNotContain(host.Root, writer.ToString(), StringComparison.Ordinal);
        if (format == "json")
        {
            using var document = JsonDocument.Parse(writer.ToString());
            var gate = document.RootElement.GetProperty("verification_gate");
            Assert.Equal(expectedCause, gate.GetProperty("cause").GetString());
            Assert.Equal("initial", gate.GetProperty("phase").GetString());
            Assert.NotEqual("satisfied", gate.GetProperty("state").GetString());
            if (mutation == "uncovered-criterion")
                Assert.Contains("AC3", gate.GetProperty("coverage").GetProperty("missing_acceptance_criteria")
                    .EnumerateArray().Select(item => item.GetString()));
            if (mutation == "uncovered-lane")
                Assert.Contains("extra", gate.GetProperty("coverage").GetProperty("missing_lanes")
                    .EnumerateArray().Select(item => item.GetString()));
            if (mutation == "heavy-local-missing")
            {
                Assert.Contains("unit", gate.GetProperty("coverage").GetProperty("missing_local_required_lanes")
                    .EnumerateArray().Select(item => item.GetString()));
                Assert.Equal("present", gate.GetProperty("coverage").GetProperty("proofs")[1].GetProperty("state").GetString());
            }
        }
        else
        {
            Assert.Contains(expectedCause, writer.ToString(), StringComparison.Ordinal);
            Assert.Contains("phase: initial", writer.ToString(), StringComparison.Ordinal);
            Assert.DoesNotContain("- state: satisfied", writer.ToString(), StringComparison.Ordinal);
            if (mutation == "uncovered-criterion") Assert.Contains("AC3", writer.ToString(), StringComparison.Ordinal);
            if (mutation == "uncovered-lane") Assert.Contains("extra", writer.ToString(), StringComparison.Ordinal);
            if (mutation == "heavy-local-missing") Assert.Contains("unit", writer.ToString(), StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("json", "packet-missing")]
    [InlineData("markdown", "packet-missing")]
    [InlineData("json", "packet-symlink")]
    [InlineData("markdown", "packet-symlink")]
    [InlineData("json", "packet-case-alias")]
    [InlineData("markdown", "packet-case-alias")]
    [InlineData("json", "packet-locked")]
    [InlineData("markdown", "packet-locked")]
    [InlineData("json", "record-symlink")]
    [InlineData("markdown", "record-symlink")]
    [InlineData("json", "record-directory")]
    [InlineData("markdown", "record-directory")]
    [InlineData("json", "record-case-alias")]
    [InlineData("markdown", "record-case-alias")]
    [InlineData("json", "record-locked")]
    [InlineData("markdown", "record-locked")]
    public void G868InitialUnsafePacketAndRecordReadsRefuseBeforeHeadOrProviders(string format, string mutation)
    {
        using var host = new VerificationEvidenceCommandTests.TempHost();
        WriteG868Record(host, "P1", "E1");
        var outside = Path.Combine(Path.GetDirectoryName(host.Root)!, "g868-outside-" + Guid.NewGuid().ToString("N"));
        var validRecordBytes = File.ReadAllBytes(G868RecordPath(host, "E1"));
        File.WriteAllBytes(outside, mutation.StartsWith("packet-", StringComparison.Ordinal)
            ? host.PacketBytes
            : validRecordBytes);
        FileStream? readLock = null;
        try
        {
            if (mutation == "packet-missing") File.Delete(host.PacketPath);
            else if (mutation == "packet-symlink")
            {
                File.Delete(host.PacketPath);
                File.CreateSymbolicLink(host.PacketPath, outside);
            }
            else if (mutation == "packet-case-alias")
            {
                File.Delete(host.PacketPath);
                File.WriteAllBytes(Path.Combine(Path.GetDirectoryName(host.PacketPath)!, "Packet.yaml"), host.PacketBytes);
            }
            else if (mutation is "packet-locked" or "record-locked")
            {
                // The lock is acquired at the reached read callback below so
                // the initial packet/store setup remains valid.
            }
            else if (mutation == "record-symlink")
            {
                var recordPath = G868RecordPath(host, "E1");
                File.Delete(recordPath);
                File.CreateSymbolicLink(recordPath, outside);
            }
            else if (mutation == "record-directory")
            {
                var recordPath = G868RecordPath(host, "E1");
                File.Delete(recordPath);
                Directory.CreateDirectory(recordPath);
                File.WriteAllText(Path.Combine(recordPath, "sentinel"), "outside target must not change");
            }
            else if (mutation == "record-case-alias")
            {
                var recordPath = G868RecordPath(host, "E1");
                File.Delete(recordPath);
                File.WriteAllBytes(Path.Combine(Path.GetDirectoryName(recordPath)!, "e1.JSON"), validRecordBytes);
            }

            var outsideBefore = File.ReadAllBytes(outside);
            var headReads = 0;
            var providers = 0;
            var operations = new List<string>();
            WorkerCompleteCommand.MutatorFactory = () => { providers++; return new FakeMutator(); };
            WorkerCompleteCommand.PrLookupFactory = () => { providers++; return new PermissivePrLookup(); };
            using var writer = new StringWriter();
            var exit = WorkerCompleteCommand.Execute(G868Context(host.Root),
                G868Arguments(host, format, includeEvidence: true), writer,
                new WorkerVerificationGate.Dependencies(
                    ReadPullRequestHeadSha: (_, _) => { headReads++; return VerificationEvidenceCommandTests.Head; },
                    BeforeCoverageOperation: (operation, path) =>
                    {
                        operations.Add(operation);
                        if (readLock is null
                            && ((mutation == "packet-locked" && operation == "packet-read")
                                || (mutation == "record-locked" && operation == "stored-read")))
                        {
                            readLock = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                        }
                    }));

            Assert.Equal(2, exit);
            Assert.Equal(0, headReads);
            Assert.Equal(0, providers);
            if (mutation == "packet-case-alias") Assert.DoesNotContain("packet-read", operations);
            else Assert.Contains("packet-read", operations);
            if (mutation == "record-locked") Assert.Contains("stored-read", operations);
            else if (mutation.StartsWith("record-", StringComparison.Ordinal)) Assert.DoesNotContain("stored-read", operations);
            Assert.Equal(outsideBefore, File.ReadAllBytes(outside));
            Assert.DoesNotContain(host.Root, writer.ToString(), StringComparison.Ordinal);
            if (format == "json")
            {
                using var document = JsonDocument.Parse(writer.ToString());
                var gate = document.RootElement.GetProperty("verification_gate");
                Assert.Equal("worker-verification-coverage-unavailable", gate.GetProperty("cause").GetString());
                Assert.Equal("unavailable", gate.GetProperty("state").GetString());
                Assert.Equal("initial", gate.GetProperty("phase").GetString());
                var coverage = gate.GetProperty("coverage");
                Assert.Equal("unavailable", coverage.GetProperty("state").GetString());
                var expectedCause = mutation == "packet-case-alias"
                    ? "verification-coverage-identity-conflict"
                    : mutation.StartsWith("packet-", StringComparison.Ordinal)
                    ? "verification-coverage-packet-unavailable"
                    : "verification-coverage-record-unavailable";
                Assert.Equal(expectedCause, coverage.GetProperty("cause").GetString());
            }
            else
            {
                Assert.Contains("worker-verification-coverage-unavailable", writer.ToString(), StringComparison.Ordinal);
                Assert.Contains(mutation == "packet-case-alias"
                    ? "verification-coverage-identity-conflict"
                    : mutation.StartsWith("packet-", StringComparison.Ordinal)
                    ? "verification-coverage-packet-unavailable"
                    : "verification-coverage-record-unavailable", writer.ToString(), StringComparison.Ordinal);
                Assert.Contains("phase: initial", writer.ToString(), StringComparison.Ordinal);
            }
        }
        finally
        {
            readLock?.Dispose();
            if (File.Exists(host.PacketPath) && new FileInfo(host.PacketPath).LinkTarget is not null) File.Delete(host.PacketPath);
            if (File.Exists(outside)) File.Delete(outside);
        }
    }

    [Theory]
    [InlineData("json", "domain", "worker-verification-coverage-unavailable", "verification-coverage-identity-conflict")]
    [InlineData("json", "team", "worker-verification-coverage-unavailable", "verification-coverage-identity-conflict")]
    [InlineData("json", "repo", "worker-verification-coverage-unavailable", "verification-coverage-identity-conflict")]
    [InlineData("json", "unit", "worker-verification-coverage-unavailable", "verification-coverage-packet-unavailable")]
    [InlineData("json", "record-id", "worker-verification-coverage-incomplete", "verification-coverage-incomplete")]
    [InlineData("markdown", "domain", "worker-verification-coverage-unavailable", "verification-coverage-identity-conflict")]
    [InlineData("markdown", "team", "worker-verification-coverage-unavailable", "verification-coverage-identity-conflict")]
    [InlineData("markdown", "repo", "worker-verification-coverage-unavailable", "verification-coverage-identity-conflict")]
    [InlineData("markdown", "unit", "worker-verification-coverage-unavailable", "verification-coverage-packet-unavailable")]
    [InlineData("markdown", "record-id", "worker-verification-coverage-incomplete", "verification-coverage-incomplete")]
    public void G868CallerIdentityAndReferenceMismatchesStayTypedAndPrecedeLiveHead(
        string format, string mutation, string expectedGateCause, string expectedCoverageCause)
    {
        using var host = new VerificationEvidenceCommandTests.TempHost();
        WriteG868Record(host, "P1", "E1");
        var args = G868Arguments(host, format, includeEvidence: true).ToList();
        switch (mutation)
        {
            case "domain": ReplaceOptionValue(args, "--verification-domain", "other-domain"); break;
            case "team": ReplaceOptionValue(args, "--verification-team", "other-team"); break;
            case "repo": ReplaceOptionValue(args, "--repo", "Other/Repository"); break;
            case "unit": ReplaceOptionValue(args, "--verification-unit", "G999"); break;
            case "record-id": ReplaceOptionValue(args, "--evidence", "P1=E2"); break;
            default: throw new ArgumentOutOfRangeException(nameof(mutation));
        }
        var headReads = 0;
        var providers = 0;
        WorkerCompleteCommand.MutatorFactory = () => { providers++; return new FakeMutator(); };
        WorkerCompleteCommand.PrLookupFactory = () => { providers++; return new PermissivePrLookup(); };
        using var writer = new StringWriter();

        var exit = WorkerCompleteCommand.Execute(G868Context(host.Root), args.ToArray(), writer,
            new WorkerVerificationGate.Dependencies(
                ReadPullRequestHeadSha: (_, _) => { headReads++; return VerificationEvidenceCommandTests.Head; }));

        Assert.Equal(2, exit);
        Assert.Equal(0, headReads);
        Assert.Equal(0, providers);
        if (format == "json")
        {
            using var document = JsonDocument.Parse(writer.ToString());
            var gate = document.RootElement.GetProperty("verification_gate");
            Assert.Equal(expectedGateCause, gate.GetProperty("cause").GetString());
            Assert.Equal("initial", gate.GetProperty("phase").GetString());
            Assert.Equal(expectedCoverageCause,
                gate.GetProperty("coverage").GetProperty("cause").GetString());
            if (mutation == "record-id")
            {
                Assert.Equal("verification-coverage-record-missing",
                    gate.GetProperty("coverage").GetProperty("proofs")[0].GetProperty("cause").GetString());
            }
        }
        else
        {
            Assert.Contains(expectedGateCause, writer.ToString(), StringComparison.Ordinal);
            Assert.Contains(expectedCoverageCause, writer.ToString(), StringComparison.Ordinal);
            if (mutation == "record-id")
                Assert.Contains("verification-coverage-record-missing", writer.ToString(), StringComparison.Ordinal);
            Assert.Contains("- phase: initial", writer.ToString(), StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("json")]
    [InlineData("markdown")]
    public void G868CurrentPacketUnitMismatchIsIdentityConflictBeforeStoredEvidenceOrHead(string format)
    {
        using var host = new VerificationEvidenceCommandTests.TempHost(packet: G868TwoProofPacket);
        WriteG868Record(host, "P1", "E1");
        var alternatePacket = G868TwoProofPacket.Replace(
            "source_execution_unit: G866", "source_execution_unit: G999", StringComparison.Ordinal);
        Assert.NotEqual(G868TwoProofPacket, alternatePacket);
        host.WritePacket(Encoding.UTF8.GetBytes(alternatePacket + "\n"));
        var packetSha = VerificationEvidenceCommandTests.Hash(host.PacketBytes);
        var options = G868Arguments(host, format, includeEvidence: true).ToList();
        ReplaceOptionValue(options, "--verification-packet-sha256", packetSha);

        var headReads = 0;
        using var writer = new StringWriter();
        var exit = WorkerCompleteCommand.Execute(G868Context(host.Root), options.ToArray(), writer,
            new WorkerVerificationGate.Dependencies(
                ReadPullRequestHeadSha: (_, _) => { headReads++; return VerificationEvidenceCommandTests.Head; }));

        Assert.Equal(2, exit);
        Assert.Equal(0, headReads);
        Assert.Contains("verification-coverage-identity-conflict", writer.ToString(), StringComparison.Ordinal);
        if (format == "json")
        {
            using var document = JsonDocument.Parse(writer.ToString());
            var coverage = document.RootElement.GetProperty("verification_gate").GetProperty("coverage");
            Assert.Equal("unavailable", coverage.GetProperty("state").GetString());
            Assert.Equal("verification-coverage-identity-conflict", coverage.GetProperty("cause").GetString());
            Assert.Empty(coverage.GetProperty("proofs").EnumerateArray());
        }
        else
        {
            Assert.Contains("verification-coverage-identity-conflict", writer.ToString(), StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("json", "criterion-text")]
    [InlineData("json", "criterion-index")]
    [InlineData("json", "lane-weight")]
    [InlineData("json", "proof-lane")]
    [InlineData("json", "proof-command")]
    [InlineData("json", "proof-ac-ref")]
    [InlineData("markdown", "criterion-text")]
    [InlineData("markdown", "criterion-index")]
    [InlineData("markdown", "lane-weight")]
    [InlineData("markdown", "proof-lane")]
    [InlineData("markdown", "proof-command")]
    [InlineData("markdown", "proof-ac-ref")]
    public void G868TypedBindingMutationsRemainHistoricalBeforeAnyHeadOrProviderRead(string format, string mutation)
    {
        using var host = new VerificationEvidenceCommandTests.TempHost(packet: G868TwoProofPacket);
        WriteG868Record(host, "P1", "E1");
        WriteG868Record(host, "P2", "E2");
        var alternate = mutation switch
        {
            "criterion-text" => G868TwoProofPacket.Replace(
                "Preserve first local criterion", "Changed first local criterion", StringComparison.Ordinal),
            "criterion-index" => G868TwoProofPacket
                .Replace("id: AC1, criterion_index: 1", "id: AC1, criterion_index: 2", StringComparison.Ordinal)
                .Replace("id: AC2, criterion_index: 2", "id: AC2, criterion_index: 1", StringComparison.Ordinal),
            "lane-weight" => G868TwoProofPacket.Replace(
                "id: unit, weight: ordinary", "id: unit, weight: heavy", StringComparison.Ordinal),
            "proof-lane" => G868TwoProofPacket
                .Replace("lane: unit", "lane: G868_TEMP", StringComparison.Ordinal)
                .Replace("lane: build", "lane: unit", StringComparison.Ordinal)
                .Replace("lane: G868_TEMP", "lane: build", StringComparison.Ordinal),
            "proof-command" => ReplaceFirst(G868TwoProofPacket,
                "command: dotnet test", "command: dotnet test --no-restore"),
            // Keep AC1 covered while expanding P1's binding; this isolates
            // its old writer-backed binding from an otherwise complete packet.
            "proof-ac-ref" => ReplaceFirst(G868TwoProofPacket,
                "acceptance_criteria: [AC1]", "acceptance_criteria: [AC1, AC2]"),
            _ => throw new ArgumentOutOfRangeException(nameof(mutation)),
        };
        Assert.NotEqual(G868TwoProofPacket, alternate);
        Assert.Equal("complete", PacketVerificationContract.Evaluate(
            host.Unit, $".intent-cli/issues/{host.Unit}/packet.yaml", Encoding.UTF8.GetBytes(alternate + "\n")).State);
        host.WritePacket(Encoding.UTF8.GetBytes(alternate + "\n"));
        var alternatePacketSha = VerificationEvidenceCommandTests.Hash(host.PacketBytes);
        foreach (var id in new[] { "E1", "E2" })
        {
            var path = G868RecordPath(host, id);
            var oldRecord = JsonSerializer.Deserialize<VerificationEvidenceRecord>(File.ReadAllBytes(path))!;
            File.WriteAllBytes(path, VerificationEvidenceStore.Serialize(oldRecord with
            {
                PacketSha256 = alternatePacketSha,
            }));
            Assert.True(VerificationEvidenceStore.TryParse(File.ReadAllBytes(path), out _, out var detail), detail);
        }

        var headReads = 0;
        var providerReads = 0;
        WorkerCompleteCommand.MutatorFactory = () => { providerReads++; return new FakeMutator(); };
        WorkerCompleteCommand.PrLookupFactory = () => { providerReads++; return new PermissivePrLookup(); };
        using var writer = new StringWriter();
        var args = G868Arguments(host, format,
            ["--evidence", "P1=E1", "--evidence", "P2=E2"], write: false).ToList();
        ReplaceOptionValue(args, "--verification-packet-sha256", alternatePacketSha);

        var exit = WorkerCompleteCommand.Execute(G868Context(host.Root), args.ToArray(), writer,
            new WorkerVerificationGate.Dependencies(
                ReadPullRequestHeadSha: (_, _) => { headReads++; return VerificationEvidenceCommandTests.Head; }));

        Assert.Equal(2, exit);
        Assert.Equal(0, headReads);
        Assert.Equal(0, providerReads);
        Assert.DoesNotContain("trx_base64", writer.ToString(), StringComparison.Ordinal);
        if (format == "json")
        {
            using var document = JsonDocument.Parse(writer.ToString());
            var gate = document.RootElement.GetProperty("verification_gate");
            Assert.Equal("worker-verification-coverage-incomplete", gate.GetProperty("cause").GetString());
            Assert.Equal("incomplete", gate.GetProperty("coverage").GetProperty("state").GetString());
            var proofs = gate.GetProperty("coverage").GetProperty("proofs").EnumerateArray().ToArray();
            var first = proofs.Single(proof => proof.GetProperty("proof_id").GetString() == "P1");
            var second = proofs.Single(proof => proof.GetProperty("proof_id").GetString() == "P2");
            Assert.Equal("ineligible", first.GetProperty("state").GetString());
            Assert.Equal("verification-coverage-stale-binding", first.GetProperty("cause").GetString());
            Assert.Equal("stale", first.GetProperty("binding_state").GetString());
            Assert.NotNull(first.GetProperty("artifact_path").GetString());
            Assert.NotNull(first.GetProperty("artifact_sha256").GetString());
            if (mutation == "proof-lane" || mutation == "criterion-index")
            {
                Assert.Equal("ineligible", second.GetProperty("state").GetString());
                Assert.Equal("verification-coverage-stale-binding", second.GetProperty("cause").GetString());
            }
            else Assert.Equal("present", second.GetProperty("state").GetString());
        }
        else
        {
            Assert.Contains("worker-verification-coverage-incomplete", writer.ToString(), StringComparison.Ordinal);
            Assert.Contains("verification-coverage-stale-binding", writer.ToString(), StringComparison.Ordinal);
            if (mutation is "proof-lane" or "criterion-index")
                Assert.DoesNotContain("verification-coverage-present", writer.ToString(), StringComparison.Ordinal);
            else
                Assert.Contains("verification-coverage-present", writer.ToString(), StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("json")]
    [InlineData("markdown")]
    public void G868SameCommandProofRecordSwapIsStaleForBothReferences(string format)
    {
        using var host = new VerificationEvidenceCommandTests.TempHost(packet: G868TwoProofPacket);
        WriteG868Record(host, "P1", "E1");
        WriteG868Record(host, "P2", "E2");
        var beforePacketSha = VerificationEvidenceCommandTests.Hash(host.PacketBytes);
        var headReads = 0;
        var providerReads = 0;
        WorkerCompleteCommand.MutatorFactory = () => { providerReads++; return new FakeMutator(); };
        using var writer = new StringWriter();
        var args = G868Arguments(host, format,
            ["--evidence", "P1=E2", "--evidence", "P2=E1"], write: false).ToList();

        var exit = WorkerCompleteCommand.Execute(G868Context(host.Root), args.ToArray(), writer,
            new WorkerVerificationGate.Dependencies(
                ReadPullRequestHeadSha: (_, _) => { headReads++; return VerificationEvidenceCommandTests.Head; }));

        Assert.Equal(2, exit);
        Assert.Equal(beforePacketSha, VerificationEvidenceCommandTests.Hash(host.PacketBytes));
        Assert.Equal(0, headReads);
        Assert.Equal(0, providerReads);
        if (format == "json")
        {
            using var document = JsonDocument.Parse(writer.ToString());
            var gate = document.RootElement.GetProperty("verification_gate");
            Assert.Equal("worker-verification-coverage-incomplete", gate.GetProperty("cause").GetString());
            var proofs = gate.GetProperty("coverage").GetProperty("proofs").EnumerateArray().ToArray();
            var first = proofs.Single(item => item.GetProperty("proof_id").GetString() == "P1");
            var second = proofs.Single(item => item.GetProperty("proof_id").GetString() == "P2");
            Assert.Equal("E2", first.GetProperty("record_id").GetString());
            Assert.Equal("E1", second.GetProperty("record_id").GetString());
            Assert.Equal("verification-coverage-stale-binding", first.GetProperty("cause").GetString());
            Assert.Equal("verification-coverage-stale-binding", second.GetProperty("cause").GetString());
            Assert.Equal("stale", first.GetProperty("binding_state").GetString());
            Assert.Equal("stale", second.GetProperty("binding_state").GetString());
        }
        else
        {
            Assert.Contains("verification-coverage-stale-binding", writer.ToString(), StringComparison.Ordinal);
            Assert.Contains("\"record_id\": \"E1\"", writer.ToString(), StringComparison.Ordinal);
            Assert.Contains("\"record_id\": \"E2\"", writer.ToString(), StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("json")]
    [InlineData("markdown")]
    public void G868AffectedLaneRemovalIsTypedPacketRefusalBeforeHeadOrProviders(string format)
    {
        using var host = new VerificationEvidenceCommandTests.TempHost(packet: G868TwoProofPacket);
        WriteG868Record(host, "P1", "E1");
        WriteG868Record(host, "P2", "E2");
        var alternate = G868TwoProofPacket.Replace(
            "affected_lanes: [unit, build]", "affected_lanes: [build]", StringComparison.Ordinal);
        Assert.NotEqual(G868TwoProofPacket, alternate);
        Assert.Equal("invalid", PacketVerificationContract.Evaluate(
            host.Unit, $".intent-cli/issues/{host.Unit}/packet.yaml", Encoding.UTF8.GetBytes(alternate + "\n")).State);
        host.WritePacket(Encoding.UTF8.GetBytes(alternate + "\n"));

        var headReads = 0;
        var providerReads = 0;
        WorkerCompleteCommand.MutatorFactory = () => { providerReads++; return new FakeMutator(); };
        WorkerCompleteCommand.PrLookupFactory = () => { providerReads++; return new PermissivePrLookup(); };
        using var writer = new StringWriter();
        var args = G868Arguments(host, format,
            ["--evidence", "P1=E1", "--evidence", "P2=E2"], write: false).ToList();
        ReplaceOptionValue(args, "--verification-packet-sha256", VerificationEvidenceCommandTests.Hash(host.PacketBytes));

        var exit = WorkerCompleteCommand.Execute(G868Context(host.Root), args.ToArray(), writer,
            new WorkerVerificationGate.Dependencies(
                ReadPullRequestHeadSha: (_, _) => { headReads++; return VerificationEvidenceCommandTests.Head; }));

        Assert.Equal(2, exit);
        Assert.Equal(0, headReads);
        Assert.Equal(0, providerReads);
        Assert.DoesNotContain("trx_base64", writer.ToString(), StringComparison.Ordinal);
        if (format == "json")
        {
            using var document = JsonDocument.Parse(writer.ToString());
            var gate = document.RootElement.GetProperty("verification_gate");
            Assert.Equal("worker-verification-coverage-unavailable", gate.GetProperty("cause").GetString());
            Assert.Equal("unavailable", gate.GetProperty("coverage").GetProperty("state").GetString());
            Assert.Equal("verification-coverage-packet-unavailable",
                gate.GetProperty("coverage").GetProperty("cause").GetString());
        }
        else
        {
            Assert.Contains("worker-verification-coverage-unavailable", writer.ToString(), StringComparison.Ordinal);
            Assert.Contains("verification-coverage-packet-unavailable", writer.ToString(), StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("json", "hosted-only")]
    [InlineData("json", "mixed-hosted-local")]
    [InlineData("markdown", "hosted-only")]
    [InlineData("markdown", "mixed-hosted-local")]
    public void G868RefusesHostedProofsWithoutReadingHead(string format, string contractKind)
    {
        var packet = G868TwoProofPacket;
        if (contractKind == "hosted-only")
        {
            packet = packet.Replace("evidence: local-required", "evidence: hosted-required", StringComparison.Ordinal);
        }
        else
        {
            var hostedIndex = packet.LastIndexOf("evidence: local-required", StringComparison.Ordinal);
            Assert.True(hostedIndex > 0);
            packet = packet[..hostedIndex] + "evidence: hosted-required"
                + packet[(hostedIndex + "evidence: local-required".Length)..];
        }
        using var host = new VerificationEvidenceCommandTests.TempHost(packet: packet);
        var references = contractKind == "hosted-only"
            ? Array.Empty<string>()
            : new[] { "--evidence", "P1=E1" };
        if (contractKind == "mixed-hosted-local") WriteG868Record(host, "P1", "E1");
        var args = G868Arguments(host, format, references, write: false);
        var headReads = 0;
        using var writer = new StringWriter();
        var exitCode = WorkerCompleteCommand.Execute(G868Context(host.Root), args, writer,
            new WorkerVerificationGate.Dependencies(
                ReadPullRequestHeadSha: (_, _) => { headReads++; return VerificationEvidenceCommandTests.Head; }));

        Assert.Equal(2, exitCode);
        Assert.Equal(0, headReads);
        if (format == "json")
        {
            using var document = JsonDocument.Parse(writer.ToString());
            var gate = document.RootElement.GetProperty("verification_gate");
            Assert.Equal("refused", gate.GetProperty("state").GetString());
            Assert.Equal("worker-verification-unsupported-evidence", gate.GetProperty("cause").GetString());
            Assert.Equal("initial", gate.GetProperty("phase").GetString());
            if (contractKind == "hosted-only")
            {
                var report = gate.GetProperty("coverage");
                Assert.Equal("complete", report.GetProperty("structural_state").GetString());
                Assert.Empty(report.GetProperty("missing_proof_ids").EnumerateArray());
                Assert.All(report.GetProperty("proofs").EnumerateArray(), proof =>
                    Assert.Equal("not-applicable", proof.GetProperty("state").GetString()));
            }
        }
        else
        {
            Assert.Contains("worker-verification-unsupported-evidence", writer.ToString(), StringComparison.Ordinal);
            if (contractKind == "hosted-only")
                Assert.Contains("\"structural_state\": \"complete\"", writer.ToString(), StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("json", "label-read-record-delete")]
    [InlineData("markdown", "label-read-record-delete")]
    [InlineData("json", "final-head-record-delete")]
    [InlineData("markdown", "final-head-record-delete")]
    [InlineData("json", "final-head-packet-delete")]
    [InlineData("markdown", "final-head-packet-delete")]
    [InlineData("json", "final-head-packet-symlink")]
    [InlineData("markdown", "final-head-packet-symlink")]
    [InlineData("json", "final-head-record-symlink")]
    [InlineData("markdown", "final-head-record-symlink")]
    [InlineData("json", "final-head-record-malformed")]
    [InlineData("markdown", "final-head-record-malformed")]
    [InlineData("json", "final-head-record-failed")]
    [InlineData("markdown", "final-head-record-failed")]
    [InlineData("json", "final-head-record-tampered-summary")]
    [InlineData("markdown", "final-head-record-tampered-summary")]
    [InlineData("json", "final-head-packet-change")]
    [InlineData("markdown", "final-head-packet-change")]
    [InlineData("json", "final-head-mismatch")]
    [InlineData("markdown", "final-head-mismatch")]
    [InlineData("json", "final-head-io")]
    [InlineData("markdown", "final-head-io")]
    [InlineData("json", "final-head-invalid-operation")]
    [InlineData("markdown", "final-head-invalid-operation")]
    [InlineData("json", "final-head-unauthorized")]
    [InlineData("markdown", "final-head-unauthorized")]
    public void G868LateRecheckRefusesDriftBeforeFirstLifecycleWrite(string format, string drift)
    {
        using var host = new VerificationEvidenceCommandTests.TempHost();
        WriteG868Record(host, "P1", "E1");
        using var outsideHost = new VerificationEvidenceCommandTests.TempHost();
        WriteG868Record(outsideHost, "P1", "E1");
        using var failedRecordHost = new VerificationEvidenceCommandTests.TempHost(
            packet: Encoding.UTF8.GetString(host.PacketBytes).TrimEnd('\r', '\n'));
        Assert.Equal(host.PacketBytes, failedRecordHost.PacketBytes);
        WriteG868Record(failedRecordHost, "P1", "E1", MakeG868FailedAndSkippedTrx());
        var recordPath = G868RecordPath(host, "E1");
        var outsideRecordPath = G868RecordPath(outsideHost, "E1");
        var failedRecordPath = G868RecordPath(failedRecordHost, "E1");
        var events = new List<string>();
        var headReads = 0;
        var coverageOperations = new List<string>();
        var mutateInRead = drift == "label-read-record-delete";
        var mutator = new FakeMutator
        {
            Labels = new[] { WorkerNextActionConstants.Labels.IntentPrUpdateInProgress },
            ReadCallback = () =>
            {
                events.Add("labels");
                if (mutateInRead) File.Delete(recordPath);
            },
            ApplyCallback = () => events.Add("apply"),
        };
        WorkerCompleteCommand.MutatorFactory = () => mutator;
        var dependencies = new WorkerVerificationGate.Dependencies(
            ReadPullRequestHeadSha: (_, _) =>
            {
                headReads++;
                events.Add($"head-{headReads}");
                if (headReads == 2)
                {
                    if (drift == "final-head-record-delete") File.Delete(recordPath);
                    if (drift == "final-head-packet-delete") File.Delete(host.PacketPath);
                    if (drift == "final-head-packet-symlink")
                    {
                        File.Delete(host.PacketPath);
                        File.CreateSymbolicLink(host.PacketPath, outsideHost.PacketPath);
                    }
                    if (drift == "final-head-record-symlink")
                    {
                        File.Delete(recordPath);
                        File.CreateSymbolicLink(recordPath, outsideRecordPath);
                    }
                    if (drift == "final-head-record-malformed") File.WriteAllText(recordPath, "{ malformed");
                    if (drift == "final-head-record-failed") File.Copy(failedRecordPath, recordPath, overwrite: true);
                    if (drift == "final-head-record-tampered-summary")
                    {
                        var stored = JsonSerializer.Deserialize<VerificationEvidenceRecord>(File.ReadAllBytes(recordPath))!;
                        File.WriteAllBytes(recordPath, VerificationEvidenceStore.Serialize(stored with
                        {
                            Report = stored.Report with { Passed = stored.Report.Passed - 1 },
                        }));
                    }
                    if (drift == "final-head-packet-change")
                        host.WritePacket([.. host.PacketBytes, (byte)'\n', (byte)'#', (byte)'x']);
                    if (drift == "final-head-mismatch") return new string('f', 40);
                    if (drift == "final-head-io") throw new IOException("private path must not escape");
                    if (drift == "final-head-invalid-operation") throw new InvalidOperationException("private path must not escape");
                    if (drift == "final-head-unauthorized") throw new UnauthorizedAccessException("private path must not escape");
                }
                return VerificationEvidenceCommandTests.Head;
            },
            BeforeCoverageOperation: (operation, _) =>
            {
                coverageOperations.Add(operation);
                events.Add(operation);
            });
        using var writer = new StringWriter();

        var exitCode = WorkerCompleteCommand.Execute(G868Context(host.Root),
            G868Arguments(host, format, ["--evidence", "P1=E1"], write: true), writer, dependencies);

        Assert.Equal(2, exitCode);
        Assert.Equal(2, headReads);
        Assert.Empty(mutator.AppliedTransitions);
        Assert.DoesNotContain("apply", events);
        Assert.Contains("labels", events);
        var headFailure = drift.StartsWith("final-head-", StringComparison.Ordinal)
            && drift is not ("final-head-record-delete" or "final-head-packet-delete"
                or "final-head-packet-symlink" or "final-head-record-symlink"
                or "final-head-record-malformed" or "final-head-record-failed" or "final-head-record-tampered-summary"
                or "final-head-packet-change");
        Assert.Equal(headFailure ? 1 : 2,
            coverageOperations.Count(operation => operation == "packet-read"));

        if (format == "json")
        {
            using var document = JsonDocument.Parse(writer.ToString());
            var root = document.RootElement;
            Assert.False(root.GetProperty("proceed").GetBoolean());
            Assert.False(root.GetProperty("applied").GetBoolean());
            var gate = root.GetProperty("verification_gate");
            Assert.Equal("pre-write", gate.GetProperty("phase").GetString());
            Assert.Equal(VerificationEvidenceCommandTests.Head, gate.GetProperty("requested_head_sha").GetString());
            if (drift is "final-head-mismatch" or "final-head-io" or "final-head-invalid-operation" or "final-head-unauthorized")
            {
                Assert.Equal("worker-verification-head-" + (drift == "final-head-mismatch" ? "mismatch" : "unavailable"),
                    gate.GetProperty("cause").GetString());
                Assert.Equal(JsonValueKind.Null, gate.GetProperty("coverage").ValueKind);
            }
            else
            {
                var unavailable = drift is "final-head-packet-change" or "final-head-packet-delete"
                    or "final-head-packet-symlink" or "final-head-record-symlink"
                    or "final-head-record-malformed" or "final-head-record-tampered-summary";
                Assert.Equal("worker-verification-" + (unavailable ? "coverage-unavailable" : "coverage-incomplete"),
                    gate.GetProperty("cause").GetString());
                Assert.NotEqual("satisfied", gate.GetProperty("state").GetString());
                Assert.NotEqual("satisfied", gate.GetProperty("coverage").GetProperty("state").GetString());
                if (drift == "final-head-record-failed")
                    Assert.Contains("verification-coverage-reported-failed",
                        JsonSerializer.Serialize(gate.GetProperty("coverage")), StringComparison.Ordinal);
            }
        }
        else
        {
            var output = writer.ToString();
            Assert.Contains("- phase: pre-write", output, StringComparison.Ordinal);
            var unavailable = drift is "final-head-packet-change" or "final-head-packet-delete"
                or "final-head-packet-symlink" or "final-head-record-symlink"
                or "final-head-record-malformed" or "final-head-record-tampered-summary";
            var expectedCause = drift == "final-head-mismatch" ? "worker-verification-head-mismatch"
                : headFailure ? "worker-verification-head-unavailable"
                : unavailable ? "worker-verification-coverage-unavailable"
                : "worker-verification-coverage-incomplete";
            Assert.Contains(expectedCause, output, StringComparison.Ordinal);
            if (drift == "final-head-record-failed")
                Assert.Contains("verification-coverage-reported-failed", output, StringComparison.Ordinal);
            Assert.DoesNotContain(host.Root, output, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("json", "packet")]
    [InlineData("markdown", "packet")]
    [InlineData("json", "record")]
    [InlineData("markdown", "record")]
    public void G868LateLocalReadFailureRefusesBeforeFirstLifecycleWrite(string format, string member)
    {
        using var host = new VerificationEvidenceCommandTests.TempHost();
        WriteG868Record(host, "P1", "E1");
        var mutator = new FakeMutator
        {
            Labels = [WorkerNextActionConstants.Labels.IntentPrUpdateInProgress],
        };
        WorkerCompleteCommand.MutatorFactory = () => mutator;
        var headReads = 0;
        var packetReads = 0;
        var recordReads = 0;
        FileStream? held = null;
        using var writer = new StringWriter();
        try
        {
            var exit = WorkerCompleteCommand.Execute(G868Context(host.Root),
                G868Arguments(host, format, ["--evidence", "P1=E1"], write: true), writer,
                new WorkerVerificationGate.Dependencies(
                    ReadPullRequestHeadSha: (_, _) =>
                    {
                        headReads++;
                        return VerificationEvidenceCommandTests.Head;
                    },
                    BeforeCoverageOperation: (operation, path) =>
                    {
                        if (operation == "packet-read")
                        {
                            packetReads++;
                            if (member == "packet" && packetReads == 2)
                                held = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                        }
                        else if (operation == "stored-read")
                        {
                            recordReads++;
                            if (member == "record" && recordReads == 2)
                                held = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                        }
                    }));

            Assert.Equal(2, exit);
            Assert.Equal(2, headReads);
            Assert.Equal(2, packetReads);
            Assert.Equal(member == "record" ? 2 : 1, recordReads);
            Assert.Empty(mutator.AppliedTransitions);
            if (format == "json")
            {
                using var document = JsonDocument.Parse(writer.ToString());
                var root = document.RootElement;
                Assert.False(root.GetProperty("proceed").GetBoolean());
                Assert.False(root.GetProperty("applied").GetBoolean());
                var gate = root.GetProperty("verification_gate");
                Assert.Equal("worker-verification-coverage-unavailable", gate.GetProperty("cause").GetString());
                Assert.Equal("pre-write", gate.GetProperty("phase").GetString());
                Assert.Equal("unavailable", gate.GetProperty("coverage").GetProperty("state").GetString());
                var proofs = gate.GetProperty("coverage").GetProperty("proofs");
                if (member == "packet")
                {
                    Assert.Empty(proofs.EnumerateArray());
                }
                else
                {
                    Assert.Null(proofs[0].GetProperty("artifact_sha256").GetString());
                }
            }
            else
            {
                Assert.Contains("worker-verification-coverage-unavailable", writer.ToString(), StringComparison.Ordinal);
                Assert.Contains("phase: pre-write", writer.ToString(), StringComparison.Ordinal);
                Assert.DoesNotContain(host.Root, writer.ToString(), StringComparison.Ordinal);
            }
        }
        finally
        {
            held?.Dispose();
        }
    }

    private static string G868RecordPath(VerificationEvidenceCommandTests.TempHost host, string id) =>
        Path.Combine(host.EvidenceRoot, VerificationEvidenceCommandTests.Domain,
            VerificationEvidenceCommandTests.Team, host.Unit, id + ".json");

    private static string G868RecordPath(VerificationEvidenceCommandTests.TempHost host, string id,
        string domain, string team, string unit) =>
        Path.Combine(host.EvidenceRoot, domain, team, unit, id + ".json");

    [Theory]
    [InlineData("json", "pr-read")]
    [InlineData("markdown", "pr-read")]
    [InlineData("json", "issue-read")]
    [InlineData("markdown", "issue-read")]
    public void G868IssueAndPrValidationReadsAreFollowedByFreshCoverage(string format, string readFamily)
    {
        using var host = new VerificationEvidenceCommandTests.TempHost();
        WriteG868Record(host, "P1", "E1");
        var recordPath = G868RecordPath(host, "E1");
        var events = new List<string>();
        var deleted = false;
        void Drift()
        {
            if (deleted) return;
            deleted = true;
            File.Delete(recordPath);
        }

        WorkerCompleteCommand.PrLookupFactory = () => new StubPrLookup
        {
            Body = "Closes #525",
            ClosingIssuesReferences = [new GitHubPrClosingIssueReference { Number = 525, Repository = null }],
            LookupCallback = () =>
            {
                events.Add("pr-read");
                if (readFamily == "pr-read") Drift();
            },
        };
        WorkerCompleteCommand.IssueLookupFactory = () => new StubIssueLookup
        {
            LookupCallback = () =>
            {
                events.Add("issue-read");
                if (readFamily == "issue-read") Drift();
            },
        };
        var mutator = new FakeMutator
        {
            Labels = ["intent-target", "intent-issue-in-progress"],
            ReadCallback = () => events.Add("labels"),
            ApplyCallback = () => events.Add("apply"),
        };
        WorkerCompleteCommand.MutatorFactory = () => mutator;
        var headReads = 0;
        var coveragePacketReads = 0;
        var baseArgs = G868Arguments(host, format, ["--evidence", "P1=E1"], write: true).ToList();
        ReplaceOptionValue(baseArgs, "--kind", "issue");
        ReplaceOptionValue(baseArgs, "--number", "525");
        ReplaceOptionValue(baseArgs, "--outcome", WorkerResultSummaryConstants.Outcomes.PrCreated);
        baseArgs.Remove("--github-only");
        baseArgs.Add("--pr");
        baseArgs.Add("764");
        using var writer = new StringWriter();

        var exitCode = WorkerCompleteCommand.Execute(G868Context(host.Root), baseArgs.ToArray(), writer,
            new WorkerVerificationGate.Dependencies(
                ReadPullRequestHeadSha: (_, _) =>
                {
                    headReads++;
                    events.Add($"head-{headReads}");
                    return VerificationEvidenceCommandTests.Head;
                },
                BeforeCoverageOperation: (operation, _) =>
                {
                    if (operation == "packet-read") coveragePacketReads++;
                    events.Add(operation);
                }));

        Assert.Equal(2, exitCode);
        Assert.True(deleted);
        Assert.Equal(2, headReads);
        Assert.Equal(2, coveragePacketReads);
        Assert.DoesNotContain("apply", events);
        Assert.Equal("head-1", events[events.IndexOf("head-1")]);
        Assert.True(events.IndexOf("head-1") < events.IndexOf(readFamily));
        Assert.True(events.IndexOf(readFamily) < events.IndexOf("head-2"));
        Assert.True(events.IndexOf("head-2") < events.LastIndexOf("packet-read"));

        if (format == "json")
        {
            using var document = JsonDocument.Parse(writer.ToString());
            var root = document.RootElement;
            Assert.False(root.GetProperty("proceed").GetBoolean());
            Assert.False(root.GetProperty("applied").GetBoolean());
            var gate = root.GetProperty("verification_gate");
            Assert.Equal("pre-write", gate.GetProperty("phase").GetString());
            Assert.Equal("worker-verification-coverage-incomplete", gate.GetProperty("cause").GetString());
            Assert.Equal("incomplete", gate.GetProperty("coverage").GetProperty("state").GetString());
            Assert.NotEqual("satisfied", gate.GetProperty("state").GetString());
        }
        else
        {
            Assert.Contains("worker-verification-coverage-incomplete", writer.ToString(), StringComparison.Ordinal);
            Assert.Contains("- phase: pre-write", writer.ToString(), StringComparison.Ordinal);
        }
        Assert.DoesNotContain(host.Root, writer.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("json", false)]
    [InlineData("json", true)]
    [InlineData("markdown", false)]
    [InlineData("markdown", true)]
    public void G868IssuePrCreatedOutcomePreservesHostQueueAndLabelSequence(string format, bool write)
    {
        using var host = new VerificationEvidenceCommandTests.TempHost(packet: G868TwoProofPacket);
        WriteG868Record(host, "P1", "E1");
        WriteG868Record(host, "P2", "E2");
        using var workspace = new WorkerCompleteWorkspace();
        workspace.WriteQueueState(workspace.CreateItem(host.Unit,
            VerificationEvidenceCommandTests.Repo, 525, VerificationEvidenceCommandTests.Domain));
        var context = G868Context(host.Root, workspace.RootPath);
        var queueBefore = File.ReadAllText(workspace.QueueStatePath);
        var events = new List<string>();
        var mutator = new FakeMutator
        {
            Labels = ["intent-target", "intent-issue-in-progress"],
            ReadCallback = () => events.Add("labels"),
            ApplyCallback = () => events.Add("apply"),
        };
        WorkerCompleteCommand.MutatorFactory = () => mutator;
        var headReads = new List<int>();
        var args = G868IssueArguments(host, format,
            ["--evidence", "P2=E2", "--evidence", "P1=E1"], write);
        using var writer = new StringWriter();

        var exitCode = WorkerCompleteCommand.Execute(context, args, writer,
            new WorkerVerificationGate.Dependencies(
                ReadPullRequestHeadSha: (_, pr) =>
                {
                    headReads.Add(pr);
                    events.Add($"head-{headReads.Count}");
                    return VerificationEvidenceCommandTests.Head;
                },
                BeforeCoverageOperation: (operation, _) => events.Add(operation)));

        Assert.Equal(0, exitCode);
        Assert.Equal(new[] { 764, 764 }, headReads);
        Assert.Equal(write ? 2 : 0, mutator.AppliedTransitions.Count);
        Assert.Contains("head-2", events);
        if (write)
        {
            Assert.Equal("head-2", events[events.IndexOf("head-2")]);
            var firstApply = events.IndexOf("apply");
            Assert.True(firstApply > events.IndexOf("head-2"));
            Assert.Equal("pr", mutator.AppliedTransitions[1].Kind);
            Assert.Equal(764, mutator.AppliedTransitions[1].Number);
            Assert.Contains(WorkerNextActionConstants.Labels.IntentTarget,
                mutator.AppliedTransitions[1].AddLabels);
            Assert.Contains("https://github.com/J-Tech-Japan/intent-system/pull/764",
                File.ReadAllText(workspace.QueueStatePath), StringComparison.Ordinal);
        }
        else
        {
            Assert.Equal(queueBefore, File.ReadAllText(workspace.QueueStatePath));
        }

        if (format == "json")
        {
            using var document = JsonDocument.Parse(writer.ToString());
            var root = document.RootElement;
            Assert.True(root.GetProperty("proceed").GetBoolean());
            Assert.Equal(write, root.GetProperty("applied").GetBoolean());
            Assert.Equal(764, root.GetProperty("pr_number").GetInt32());
            if (write)
            {
                Assert.True(root.GetProperty("pr_target_applied").GetBoolean());
                Assert.True(root.GetProperty("linked_pr_synced").GetBoolean());
            }
            else
            {
                Assert.Equal(JsonValueKind.Null, root.GetProperty("pr_target_applied").ValueKind);
                Assert.Equal(JsonValueKind.Null, root.GetProperty("linked_pr_synced").ValueKind);
            }
            var gate = root.GetProperty("verification_gate");
            Assert.Equal("satisfied", gate.GetProperty("state").GetString());
            Assert.Equal("pre-write", gate.GetProperty("phase").GetString());
            Assert.Equal(new[] { "P1", "P2" }, gate.GetProperty("coverage").GetProperty("proofs")
                .EnumerateArray().Select(proof => proof.GetProperty("proof_id").GetString()).ToArray());
        }
        else
        {
            Assert.Contains("worker-verification-satisfied", writer.ToString(), StringComparison.Ordinal);
            Assert.Contains("- phase: pre-write", writer.ToString(), StringComparison.Ordinal);
            Assert.Contains(write ? "- applied: True" : "- applied: False", writer.ToString(), StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("json")]
    [InlineData("markdown")]
    public void G868AlreadyPrCreatedRepairChecksGateBeforePrTargetWrite(string format)
    {
        using var host = new VerificationEvidenceCommandTests.TempHost();
        WriteG868Record(host, "P1", "E1");
        using var workspace = new WorkerCompleteWorkspace();
        workspace.WriteQueueState(workspace.CreateItem(host.Unit,
            VerificationEvidenceCommandTests.Repo, 525, VerificationEvidenceCommandTests.Domain));
        var context = G868Context(host.Root, workspace.RootPath);
        var events = new List<string>();
        var mutator = new FakeMutator
        {
            Labels = ["intent-pr-created", "intent-target"],
            ReadCallback = () => events.Add("labels"),
            ApplyCallback = () => events.Add("apply"),
        };
        WorkerCompleteCommand.MutatorFactory = () => mutator;
        var headReads = 0;
        using var writer = new StringWriter();

        var exitCode = WorkerCompleteCommand.Execute(context,
            G868IssueArguments(host, format, ["--evidence", "P1=E1"], write: true), writer,
            new WorkerVerificationGate.Dependencies(
                ReadPullRequestHeadSha: (_, pr) =>
                {
                    Assert.Equal(764, pr);
                    headReads++;
                    events.Add($"head-{headReads}");
                    return VerificationEvidenceCommandTests.Head;
                },
                BeforeCoverageOperation: (operation, _) => events.Add(operation)));

        Assert.Equal(0, exitCode);
        Assert.Equal(2, headReads);
        var transition = Assert.Single(mutator.AppliedTransitions);
        Assert.Equal("pr", transition.Kind);
        Assert.Equal(764, transition.Number);
        Assert.Equal(new[] { WorkerNextActionConstants.Labels.IntentTarget }, transition.AddLabels);
        Assert.True(events.IndexOf("apply") > events.IndexOf("head-2"));
        Assert.Contains("https://github.com/J-Tech-Japan/intent-system/pull/764",
            File.ReadAllText(workspace.QueueStatePath), StringComparison.Ordinal);
        if (format == "json")
        {
            using var document = JsonDocument.Parse(writer.ToString());
            Assert.True(document.RootElement.GetProperty("applied").GetBoolean());
            Assert.Equal("pre-write", document.RootElement.GetProperty("verification_gate").GetProperty("phase").GetString());
        }
        else
        {
            Assert.Contains("worker-verification-satisfied", writer.ToString(), StringComparison.Ordinal);
            Assert.Contains("- applied: True", writer.ToString(), StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("json")]
    [InlineData("markdown")]
    public void G868AlreadyPrCreatedRepairRefusesChangedEvidenceBeforePrOnlyWrite(string format)
    {
        using var host = new VerificationEvidenceCommandTests.TempHost(packet: G868TwoProofPacket);
        WriteG868Record(host, "P1", "E1");
        WriteG868Record(host, "P2", "E2");
        using var workspace = new WorkerCompleteWorkspace();
        workspace.WriteQueueState(workspace.CreateItem(host.Unit,
            VerificationEvidenceCommandTests.Repo, 525, VerificationEvidenceCommandTests.Domain));
        var queueBefore = File.ReadAllBytes(workspace.QueueStatePath);
        var events = new List<string>();
        var headReads = 0;
        var mutator = new FakeMutator
        {
            Labels = [WorkerResultSummaryConstants.Labels.IntentPrCreated, "intent-target"],
            ReadCallback = () => events.Add("labels"),
            ApplyCallback = () => events.Add("apply"),
        };
        WorkerCompleteCommand.MutatorFactory = () => mutator;
        WorkerCompleteCommand.PrLookupFactory = () => new StubPrLookup
        {
            Body = "Closes #525",
            ClosingIssuesReferences = [new GitHubPrClosingIssueReference { Number = 525, Repository = null }],
        };
        WorkerCompleteCommand.IssueLookupFactory = () => new StubIssueLookup { Body = "## Acceptance Criteria\n- Existing contract.\n" };
        using var writer = new StringWriter();

        var exit = WorkerCompleteCommand.Execute(G868Context(host.Root, workspace.RootPath),
            G868IssueArguments(host, format,
                ["--evidence", "P1=E1", "--evidence", "P2=E2"], write: true), writer,
            new WorkerVerificationGate.Dependencies(
                ReadPullRequestHeadSha: (_, _) =>
                {
                    headReads++;
                    events.Add($"head-{headReads}");
                    if (headReads == 2) File.Delete(G868RecordPath(host, "E1"));
                    return VerificationEvidenceCommandTests.Head;
                },
                BeforeCoverageOperation: (operation, _) => events.Add(operation)));

        Assert.Equal(2, exit);
        Assert.Equal(2, headReads);
        Assert.Contains("labels", events);
        Assert.DoesNotContain("apply", events);
        Assert.Empty(mutator.AppliedTransitions);
        Assert.Equal(queueBefore, File.ReadAllBytes(workspace.QueueStatePath));
        Assert.DoesNotContain("https://github.com/J-Tech-Japan/intent-system/pull/764",
            File.ReadAllText(workspace.QueueStatePath), StringComparison.Ordinal);
        if (format == "json")
        {
            using var document = JsonDocument.Parse(writer.ToString());
            var root = document.RootElement;
            Assert.False(root.GetProperty("proceed").GetBoolean());
            Assert.False(root.GetProperty("applied").GetBoolean());
            var gate = root.GetProperty("verification_gate");
            Assert.Equal("worker-verification-coverage-incomplete", gate.GetProperty("cause").GetString());
            Assert.Equal("pre-write", gate.GetProperty("phase").GetString());
        }
        else
        {
            Assert.Contains("worker-verification-coverage-incomplete", writer.ToString(), StringComparison.Ordinal);
            Assert.Contains("phase: pre-write", writer.ToString(), StringComparison.Ordinal);
            Assert.DoesNotContain("- applied: True", writer.ToString(), StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("json")]
    [InlineData("markdown")]
    public void G868SatisfiedGateDoesNotRewriteExistingPartialPublicationTruth(string format)
    {
        using var host = new VerificationEvidenceCommandTests.TempHost();
        WriteG868Record(host, "P1", "E1");
        using var workspace = new WorkerCompleteWorkspace();
        workspace.WriteQueueState(workspace.CreateItem(host.Unit,
            VerificationEvidenceCommandTests.Repo, 525, VerificationEvidenceCommandTests.Domain));
        var context = G868Context(host.Root, workspace.RootPath);
        var events = new List<string>();
        var mutator = new FailingPrMutator
        {
            Labels = ["intent-target", "intent-issue-in-progress"],
            ApplyCallback = () => events.Add("apply"),
        };
        WorkerCompleteCommand.MutatorFactory = () => mutator;
        var headReads = 0;
        using var writer = new StringWriter();

        var exitCode = WorkerCompleteCommand.Execute(context,
            G868IssueArguments(host, format, ["--evidence", "P1=E1"], write: true), writer,
            new WorkerVerificationGate.Dependencies(
                ReadPullRequestHeadSha: (_, _) =>
                {
                    headReads++;
                    events.Add($"head-{headReads}");
                    return VerificationEvidenceCommandTests.Head;
                },
                BeforeCoverageOperation: (operation, _) => events.Add(operation)));

        Assert.Equal(2, exitCode);
        Assert.Equal(2, headReads);
        Assert.Single(mutator.AppliedTransitions);
        Assert.Equal("issue", Assert.Single(mutator.AppliedTransitions).Kind);
        Assert.Equal(2, events.Count(item => item == "apply"));
        Assert.True(events.IndexOf("apply") > events.IndexOf("head-2"));
        Assert.DoesNotContain("https://github.com/J-Tech-Japan/intent-system/pull/764",
            File.ReadAllText(workspace.QueueStatePath), StringComparison.Ordinal);

        if (format == "json")
        {
            using var document = JsonDocument.Parse(writer.ToString());
            var root = document.RootElement;
            Assert.False(root.GetProperty("proceed").GetBoolean());
            Assert.False(root.GetProperty("applied").GetBoolean());
            Assert.False(root.GetProperty("pr_target_applied").GetBoolean());
            Assert.Contains(root.GetProperty("errors").EnumerateArray().Select(item => item.GetString()!),
                error => error.Contains("Source issue completion was applied", StringComparison.Ordinal));
            var gate = root.GetProperty("verification_gate");
            Assert.Equal("satisfied", gate.GetProperty("state").GetString());
            Assert.Equal("pre-write", gate.GetProperty("phase").GetString());
        }
        else
        {
            Assert.Contains("Issue-to-PR completion partially applied", writer.ToString(), StringComparison.Ordinal);
            Assert.Contains("Source issue completion was applied", writer.ToString(), StringComparison.Ordinal);
            Assert.Contains("worker-verification-satisfied", writer.ToString(), StringComparison.Ordinal);
            Assert.Contains("- phase: pre-write", writer.ToString(), StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("json", "resolved-unit")]
    [InlineData("markdown", "resolved-unit")]
    [InlineData("json", "resolved-domain")]
    [InlineData("markdown", "resolved-domain")]
    public void G868ResolvedHostIdentityMismatchRefusesBeforeIssueProviders(string format, string mismatch)
    {
        using var host = new VerificationEvidenceCommandTests.TempHost();
        WriteG868Record(host, "P1", "E1");
        using var workspace = new WorkerCompleteWorkspace();
        var queueUnit = mismatch == "resolved-unit" ? "G999" : host.Unit;
        var queueDomain = mismatch == "resolved-domain" ? "other-domain" : VerificationEvidenceCommandTests.Domain;
        workspace.WriteQueueState(workspace.CreateItem(queueUnit,
            VerificationEvidenceCommandTests.Repo, 525, queueDomain));
        var context = G868Context(host.Root, workspace.RootPath);
        var headReads = 0;
        var coverageReads = 0;
        var issueFactories = 0;
        var prFactories = 0;
        var mutatorFactories = 0;
        WorkerCompleteCommand.IssueLookupFactory = () => { issueFactories++; return new StubIssueLookup(); };
        WorkerCompleteCommand.PrLookupFactory = () => { prFactories++; return new PermissivePrLookup(); };
        WorkerCompleteCommand.MutatorFactory = () => { mutatorFactories++; return new FakeMutator(); };
        using var writer = new StringWriter();

        var exitCode = WorkerCompleteCommand.Execute(context,
            G868IssueArguments(host, format, ["--evidence", "P1=E1"], write: false), writer,
            new WorkerVerificationGate.Dependencies(
                ReadPullRequestHeadSha: (_, pr) =>
                {
                    Assert.Equal(764, pr);
                    headReads++;
                    return VerificationEvidenceCommandTests.Head;
                },
                BeforeCoverageOperation: (_, _) => coverageReads++));

        Assert.Equal(2, exitCode);
        Assert.Equal(1, headReads);
        Assert.True(coverageReads > 0);
        Assert.Equal(0, issueFactories);
        Assert.Equal(0, prFactories);
        Assert.Equal(0, mutatorFactories);
        if (format == "json")
        {
            using var document = JsonDocument.Parse(writer.ToString());
            var root = document.RootElement;
            Assert.False(root.GetProperty("proceed").GetBoolean());
            Assert.False(root.GetProperty("applied").GetBoolean());
            var gate = root.GetProperty("verification_gate");
            Assert.Equal("refused", gate.GetProperty("state").GetString());
            Assert.Equal("worker-verification-identity-conflict", gate.GetProperty("cause").GetString());
            Assert.Equal("initial", gate.GetProperty("phase").GetString());
            Assert.Equal(764, gate.GetProperty("target_pr").GetInt32());
            Assert.Equal("complete", gate.GetProperty("coverage").GetProperty("state").GetString());
        }
        else
        {
            Assert.Contains("worker-verification-identity-conflict", writer.ToString(), StringComparison.Ordinal);
            Assert.Contains("- phase: initial", writer.ToString(), StringComparison.Ordinal);
            Assert.DoesNotContain("## Errors\n- (none)", writer.ToString(), StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("json")]
    [InlineData("markdown")]
    public void G868GithubOnlyIssueWriteNeverReadsOrWritesLocalQueueState(string format)
    {
        using var host = new VerificationEvidenceCommandTests.TempHost(packet: G868TwoProofPacket);
        WriteG868Record(host, "P1", "E1");
        WriteG868Record(host, "P2", "E2");
        var queuePath = Path.Combine(host.Root, ".intent-cli", "queue-state.json");
        var sentinel = System.Text.Encoding.UTF8.GetBytes("not valid queue state; github-only must not read this");
        File.WriteAllBytes(queuePath, sentinel);
        var mutator = new FakeMutator
        {
            Labels = ["intent-target", "intent-issue-in-progress"],
        };
        WorkerCompleteCommand.MutatorFactory = () => mutator;
        var headReads = 0;
        using var writer = new StringWriter();
        var args = G868IssueArguments(host, format,
            ["--evidence", "P1=E1", "--evidence", "P2=E2"], write: true).ToList();
        args.Add("--github-only");

        var exitCode = WorkerCompleteCommand.Execute(G868Context(host.Root), args.ToArray(), writer,
            new WorkerVerificationGate.Dependencies(
                ReadPullRequestHeadSha: (_, _) => { headReads++; return VerificationEvidenceCommandTests.Head; }));

        Assert.Equal(0, exitCode);
        Assert.Equal(2, headReads);
        Assert.Equal(2, mutator.AppliedTransitions.Count);
        Assert.Equal(sentinel, File.ReadAllBytes(queuePath));
        if (format == "json")
        {
            using var document = JsonDocument.Parse(writer.ToString());
            var root = document.RootElement;
            Assert.True(root.GetProperty("github_only").GetBoolean());
            Assert.False(root.GetProperty("linked_pr_synced").GetBoolean());
            Assert.Equal("satisfied", root.GetProperty("verification_gate").GetProperty("state").GetString());
        }
        else
        {
            Assert.Contains("child-cwd mode: skipped queue-state linked_pr sync", writer.ToString(), StringComparison.Ordinal);
            Assert.Contains("worker-verification-satisfied", writer.ToString(), StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("json", "pr", false)]
    [InlineData("json", "pr", true)]
    [InlineData("text", "pr", false)]
    [InlineData("text", "pr", true)]
    [InlineData("json", "issue", false)]
    [InlineData("json", "issue", true)]
    [InlineData("text", "issue", false)]
    [InlineData("text", "issue", true)]
    public void G868NoMarkerPreservesExactLegacyOutputAcrossKindsModesAndFormats(
        string format, string kind, bool write)
    {
        using var workspace = new WorkerCompleteWorkspace();
        var unrelated = Path.Combine(workspace.RootPath, ".intent-cli", "issues", "G868");
        Directory.CreateDirectory(unrelated);
        File.WriteAllText(Path.Combine(unrelated, "packet.yaml"), "invalid: [unrelated");
        var unrelatedStore = Path.Combine(workspace.RootPath, ".intent-cli", "verification-evidence",
            "intent-cli", "intent-cli-dev", "G868");
        Directory.CreateDirectory(unrelatedStore);
        File.WriteAllText(Path.Combine(unrelatedStore, "E1.json"), "not canonical evidence");

        var args = new List<string>
        {
            "--repo", VerificationEvidenceCommandTests.Repo,
            "--kind", kind,
            "--number", kind == "issue" ? "525" : "1900",
            "--outcome", kind == "issue"
                ? WorkerResultSummaryConstants.Outcomes.PrCreated
                : WorkerResultSummaryConstants.Outcomes.RepairPushed,
            "--github-only",
        };
        if (kind == "issue") args.AddRange(["--pr", "764"]);
        args.Add(write ? "--write" : "--dry-run");
        args.AddRange(["--format", format]);

        (string Output, int ExitCode, int MutatorFactories, int LabelReads, int ApplyCalls,
            int PrFactories, int PrReads, int IssueFactories, int IssueReads,
            bool QueueExists, string? QueueBytes) Run(WorkerVerificationGate.Dependencies? dependencies)
        {
            var mutatorFactories = 0;
            var labelReads = 0;
            var applyCalls = 0;
            var prFactories = 0;
            var prReads = 0;
            var issueFactories = 0;
            var issueReads = 0;
            var mutator = new FakeMutator
            {
                Labels = kind == "issue"
                    ? ["intent-target", "intent-issue-in-progress"]
                    : [WorkerNextActionConstants.Labels.IntentPrUpdateInProgress],
                ReadCallback = () => labelReads++,
                ApplyCallback = () => applyCalls++,
            };
            WorkerCompleteCommand.MutatorFactory = () => { mutatorFactories++; return mutator; };
            WorkerCompleteCommand.PrLookupFactory = () =>
            {
                prFactories++;
                return new StubPrLookup
                {
                    Body = "Closes #525",
                    ClosingIssuesReferences = [new GitHubPrClosingIssueReference { Number = 525, Repository = null }],
                    LookupCallback = () => prReads++,
                };
            };
            WorkerCompleteCommand.IssueLookupFactory = () =>
            {
                issueFactories++;
                return new StubIssueLookup { Body = IssuePrepareCommandTests.CompleteValidBody, LookupCallback = () => issueReads++ };
            };
            using var writer = new StringWriter();
            var exitCode = WorkerCompleteCommand.Execute(workspace.Context, args.ToArray(), writer, dependencies);
            return (writer.ToString(), exitCode, mutatorFactories, labelReads, applyCalls,
                prFactories, prReads, issueFactories, issueReads,
                File.Exists(workspace.QueueStatePath), File.Exists(workspace.QueueStatePath)
                    ? Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(workspace.QueueStatePath)))
                    : null);
        }

        var baseline = Run(null);
        var dependencyCalls = 0;
        var observed = Run(new WorkerVerificationGate.Dependencies(
            ReadPullRequestHeadSha: (_, _) => { dependencyCalls++; return VerificationEvidenceCommandTests.Head; },
            BeforeCoverageOperation: (_, _) => dependencyCalls++));

        Assert.Equal(baseline.ExitCode, observed.ExitCode);
        Assert.Equal(baseline.Output, observed.Output);
        Assert.Equal(baseline.MutatorFactories, observed.MutatorFactories);
        Assert.Equal(baseline.LabelReads, observed.LabelReads);
        Assert.Equal(baseline.ApplyCalls, observed.ApplyCalls);
        Assert.Equal(baseline.PrFactories, observed.PrFactories);
        Assert.Equal(baseline.PrReads, observed.PrReads);
        Assert.Equal(baseline.IssueFactories, observed.IssueFactories);
        Assert.Equal(baseline.IssueReads, observed.IssueReads);
        Assert.Equal(baseline.QueueExists, observed.QueueExists);
        Assert.Equal(baseline.QueueBytes, observed.QueueBytes);
        Assert.Equal(0, dependencyCalls);
        Assert.DoesNotContain("verification_gate", observed.Output, StringComparison.Ordinal);
        if (format == "json")
        {
            using var document = JsonDocument.Parse(observed.Output);
            Assert.False(document.RootElement.TryGetProperty("verification_gate", out _));
        }
    }

    [Theory]
    [InlineData("json", "analyzer-refusal")]
    [InlineData("text", "analyzer-refusal")]
    [InlineData("json", "already-created-dry-run")]
    [InlineData("text", "already-created-dry-run")]
    public void G868NoMarkerPreservesAnalyzerAndAlreadyCreatedDryRunBudgets(string format, string path)
    {
        using var workspace = new WorkerCompleteWorkspace();
        workspace.WriteQueueState(workspace.CreateItem("G868", VerificationEvidenceCommandTests.Repo, 525,
            VerificationEvidenceCommandTests.Domain));
        var queueBefore = File.ReadAllBytes(workspace.QueueStatePath);
        var issueBody = path == "analyzer-refusal"
            ? "## Acceptance Criteria\n- Criterion one — actual output pasted.\n- Criterion two — actual counts pasted.\n"
            : "## Acceptance Criteria\n- Preserve the documented behavior.\n";
        var prBody = path == "analyzer-refusal"
            ? "Closes #525\n\n## Validation\nAll checks passed.\n"
            : "Closes #525\n";
        var expectedLabels = path == "already-created-dry-run"
            ? new[] { "intent-target", WorkerResultSummaryConstants.Labels.IntentPrCreated }
            : new[] { "intent-target", "intent-issue-in-progress" };

        (string Output, int Exit, int PrFactory, int PrLookup, int IssueFactory, int IssueLookup,
            int MutatorFactory, int LabelRead, int Apply, byte[] Queue) Run(
            WorkerVerificationGate.Dependencies? dependencies)
        {
            var counts = new int[7];
            WorkerCompleteCommand.PrLookupFactory = () =>
            {
                counts[0]++;
                return new StubPrLookup
                {
                    Body = prBody,
                    ClosingIssuesReferences = [new GitHubPrClosingIssueReference { Number = 525, Repository = null }],
                    LookupCallback = () => counts[1]++,
                };
            };
            WorkerCompleteCommand.IssueLookupFactory = () =>
            {
                counts[2]++;
                return new StubIssueLookup { Body = issueBody, LookupCallback = () => counts[3]++ };
            };
            WorkerCompleteCommand.MutatorFactory = () =>
            {
                counts[4]++;
                return new FakeMutator
                {
                    Labels = expectedLabels,
                    ReadCallback = () => counts[5]++,
                    ApplyCallback = () => counts[6]++,
                };
            };
            var args = new[]
            {
                "--repo", VerificationEvidenceCommandTests.Repo,
                "--kind", "issue", "--number", "525",
                "--outcome", WorkerResultSummaryConstants.Outcomes.PrCreated,
                "--pr", "764", "--github-only", "--dry-run", "--format", format,
            };
            using var writer = new StringWriter();
            var exit = WorkerCompleteCommand.Execute(workspace.Context, args, writer, dependencies);
            return (writer.ToString(), exit, counts[0], counts[1], counts[2], counts[3],
                counts[4], counts[5], counts[6], File.ReadAllBytes(workspace.QueueStatePath));
        }

        var baseline = Run(null);
        var dependencyCalls = 0;
        var withDependencies = Run(new WorkerVerificationGate.Dependencies(
            ReadPullRequestHeadSha: (_, _) => { dependencyCalls++; return VerificationEvidenceCommandTests.Head; },
            BeforeCoverageOperation: (_, _) => dependencyCalls++));

        Assert.Equal(baseline.Exit, withDependencies.Exit);
        Assert.Equal(baseline.Output, withDependencies.Output);
        Assert.Equal(baseline.PrFactory, withDependencies.PrFactory);
        Assert.Equal(baseline.PrLookup, withDependencies.PrLookup);
        Assert.Equal(baseline.IssueFactory, withDependencies.IssueFactory);
        Assert.Equal(baseline.IssueLookup, withDependencies.IssueLookup);
        Assert.Equal(baseline.MutatorFactory, withDependencies.MutatorFactory);
        Assert.Equal(baseline.LabelRead, withDependencies.LabelRead);
        Assert.Equal(baseline.Apply, withDependencies.Apply);
        Assert.Equal(queueBefore, baseline.Queue);
        Assert.Equal(queueBefore, withDependencies.Queue);
        Assert.Equal(0, dependencyCalls);
        if (path == "analyzer-refusal")
        {
            Assert.Equal(1, baseline.Exit);
            Assert.Contains("evidence gap (G785)", baseline.Output, StringComparison.Ordinal);
            Assert.Equal(0, baseline.LabelRead);
            Assert.Equal(0, baseline.Apply);
        }
        else
        {
            Assert.Equal(2, baseline.Exit);
            Assert.Equal(1, baseline.LabelRead);
            Assert.Equal(0, baseline.Apply);
            Assert.Contains("already", baseline.Output, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void G868NoMarkerMarkdownRefusalKeepsLegacyPriorityAndBytes()
    {
        using var workspace = new WorkerCompleteWorkspace();
        var args = new[]
        {
            "--repo", VerificationEvidenceCommandTests.Repo,
            "--kind", "pr",
            "--number", "1900",
            "--outcome", WorkerResultSummaryConstants.Outcomes.RepairPushed,
            "--github-only",
            "--format", "markdown",
        };
        var mutatorFactories = 0;
        WorkerCompleteCommand.MutatorFactory = () => { mutatorFactories++; return new FakeMutator(); };
        using var baselineWriter = new StringWriter();
        var baselineExit = WorkerCompleteCommand.Execute(workspace.Context, args, baselineWriter);
        var dependencyCalls = 0;
        using var dependentWriter = new StringWriter();
        var dependentExit = WorkerCompleteCommand.Execute(workspace.Context, args, dependentWriter,
            new WorkerVerificationGate.Dependencies(
                ReadPullRequestHeadSha: (_, _) => { dependencyCalls++; return VerificationEvidenceCommandTests.Head; },
                BeforeCoverageOperation: (_, _) => dependencyCalls++));

        Assert.Equal(1, baselineExit);
        Assert.Equal(baselineExit, dependentExit);
        Assert.Equal(baselineWriter.ToString(), dependentWriter.ToString());
        Assert.Equal(0, dependencyCalls);
        Assert.Equal(0, mutatorFactories);
        Assert.Contains("--format must be 'text' or 'json'", dependentWriter.ToString(), StringComparison.Ordinal);
    }

    private static string[] G868IssueArguments(
        VerificationEvidenceCommandTests.TempHost host,
        string format,
        IReadOnlyList<string> evidenceArguments,
        bool write)
    {
        var args = G868Arguments(host, format, evidenceArguments, write).ToList();
        ReplaceOptionValue(args, "--kind", "issue");
        ReplaceOptionValue(args, "--number", "525");
        ReplaceOptionValue(args, "--outcome", WorkerResultSummaryConstants.Outcomes.PrCreated);
        args.Remove("--github-only");
        args.AddRange(["--pr", "764"]);
        return args.ToArray();
    }

    private static CliContext G868Context(string root, string parentIntentRepoRoot = "") => new()
    {
        RepoRoot = root,
        Config = new CliConfig
        {
            Project = new ProjectConfig
            {
                Domain = VerificationEvidenceCommandTests.Domain,
                ArtifactRoot = ".intent-cli",
                ParentIntentRepoRoot = parentIntentRepoRoot,
            },
        },
    };

    private static string[] G868Arguments(
        VerificationEvidenceCommandTests.TempHost host,
        string format,
        bool includeEvidence) => G868Arguments(host, format,
            includeEvidence ? ["--evidence", "P1=E1"] : Array.Empty<string>(), write: false);

    private static string[] G868Arguments(
        VerificationEvidenceCommandTests.TempHost host,
        string format,
        IReadOnlyList<string> evidenceArguments,
        bool write,
        string? domain = null,
        string? team = null,
        string? repo = null) =>
    [
        "--repo", repo ?? VerificationEvidenceCommandTests.Repo,
        "--kind", "pr",
        "--number", "1900",
        "--outcome", WorkerResultSummaryConstants.Outcomes.RepairPushed,
        "--github-only",
        "--require-verification",
        "--verification-unit", host.Unit,
        "--verification-domain", domain ?? VerificationEvidenceCommandTests.Domain,
        "--verification-team", team ?? VerificationEvidenceCommandTests.Team,
        "--verification-head-sha", VerificationEvidenceCommandTests.Head,
        "--verification-packet-sha256", VerificationEvidenceCommandTests.Hash(host.PacketBytes),
        .. evidenceArguments,
        .. (write ? new[] { "--write" } : new[] { "--dry-run" }),
        "--format", format,
    ];

    private static void RemoveOption(List<string> arguments, string option)
    {
        var index = arguments.IndexOf(option);
        Assert.True(index >= 0, $"Expected option {option} in fixture arguments.");
        arguments.RemoveAt(index);
        if (index < arguments.Count && !arguments[index].StartsWith("--", StringComparison.Ordinal))
            arguments.RemoveAt(index);
    }

    private static void ReplaceOptionValue(List<string> arguments, string option, string value)
    {
        var index = arguments.IndexOf(option);
        Assert.True(index >= 0, $"Expected option {option} in fixture arguments.");
        Assert.True(index + 1 < arguments.Count, $"Expected a value after {option}.");
        arguments[index + 1] = value;
    }

    private static string ReplaceFirst(string input, string oldValue, string newValue)
    {
        var index = input.IndexOf(oldValue, StringComparison.Ordinal);
        Assert.True(index >= 0, $"Expected fixture text '{oldValue}'.");
        return input[..index] + newValue + input[(index + oldValue.Length)..];
    }

    private static byte[] MakeG868ResultNameDifference(byte[] authenticTrx)
    {
        var xml = Encoding.UTF8.GetString(authenticTrx);
        var resultStart = xml.IndexOf("<UnitTestResult ", StringComparison.Ordinal);
        Assert.True(resultStart >= 0, "Expected the authentic VSTest fixture to contain a result row.");
        var nameStart = xml.IndexOf("testName=\"", resultStart, StringComparison.Ordinal);
        Assert.True(nameStart >= 0, "Expected the authentic VSTest result row to have a name.");
        nameStart += "testName=\"".Length;
        var nameEnd = xml.IndexOf('"', nameStart);
        Assert.True(nameEnd > nameStart, "Expected the authentic VSTest result name to be nonempty.");
        xml = xml[..nameStart] + "G868 result-name discrepancy" + xml[nameEnd..];
        return Encoding.UTF8.GetBytes(xml);
    }

    private static byte[] MakeG868FailedAndSkippedTrx()
    {
        const string namespaceText = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";
        XNamespace ns = namespaceText;
        var document = XDocument.Parse(Encoding.UTF8.GetString(
            VerificationEvidenceCommandTests.MutateSmallTrx("skipped-row")));
        var root = document.Root!;
        var results = root.Element(ns + "Results")!.Elements(ns + "UnitTestResult").ToArray();
        Assert.True(results.Length > 1, "The authentic small TRX must support a simultaneous failure and skip.");
        results[0].SetAttributeValue("outcome", "Failed");
        var counters = root.Element(ns + "ResultSummary")!.Element(ns + "Counters")!;
        counters.SetAttributeValue("total", results.Length);
        counters.SetAttributeValue("executed", 1);
        counters.SetAttributeValue("passed", 0);
        counters.SetAttributeValue("failed", 1);
        counters.SetAttributeValue("notExecuted", results.Length - 1);
        root.Element(ns + "ResultSummary")!.SetAttributeValue("outcome", "Failed");
        return Encoding.UTF8.GetBytes(document.ToString(SaveOptions.DisableFormatting));
    }

    private static string RunG868Git(string workingDirectory, params string[] arguments)
    {
        var start = new ProcessStartInfo("git")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)
            ?? throw new InvalidOperationException("Could not start the local Git fixture command.");
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"Local Git fixture command failed: {error}");
        return output.TrimEnd('\r', '\n');
    }

    private static string SnapshotG868GitState(string repository, string bare)
    {
        var indexPath = Path.Combine(repository, ".git", "index");
        var indexSha = File.Exists(indexPath)
            ? Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(indexPath))).ToLowerInvariant()
            : "missing";
        var filesAndDirectories = new List<string>();
        foreach (var directory in Directory.EnumerateDirectories(repository, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(repository, directory);
            if (relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)[0] == ".git") continue;
            filesAndDirectories.Add("D " + relative.Replace(Path.DirectorySeparatorChar, '/'));
        }
        foreach (var path in Directory.EnumerateFiles(repository, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(repository, path);
            if (relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)[0] == ".git") continue;
            var sha = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
            filesAndDirectories.Add("F " + relative.Replace(Path.DirectorySeparatorChar, '/') + " " + sha);
        }
        filesAndDirectories.Sort(StringComparer.Ordinal);
        return string.Join('\n',
            "head=" + RunG868Git(repository, "rev-parse", "HEAD"),
            "index=" + indexSha,
            "refs=" + RunG868Git(repository, "show-ref", "--head"),
            "status=" + RunG868Git(repository, "status", "--porcelain=v1", "--untracked-files=all"),
            "remote-refs=" + RunG868Git(bare, "show-ref", "--head"),
            string.Join('\n', filesAndDirectories));
    }

    private static void WriteG868Record(
        VerificationEvidenceCommandTests.TempHost host,
        string proof,
        string id,
        byte[]? trx = null,
        string? domain = null,
        string? team = null,
        string? repo = null,
        string? unit = null)
    {
        trx ??= VerificationEvidenceCommandTests.ReadSmallVstest();
        host.WriteTrx("capture.trx", trx);
        var (exitCode, output) = VerificationEvidenceCommandTests.Invoke(host,
            [.. VerificationEvidenceCommandTests.RecordArgs(host, "json",
                VerificationEvidenceCommandTests.Hash(host.PacketBytes), "capture.trx", id: id, unit: unit,
                domain: domain, team: team, repo: repo, proof: proof),
                "--write"]);
        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(output);
        Assert.Equal("recorded", document.RootElement.GetProperty("disposition").GetString());
        Assert.True(document.RootElement.GetProperty("wrote").GetBoolean());
    }

    private const string G868TwoProofPacket = """
        implementation_issue_packet:
          source_execution_unit: G866
          domain: intent-cli
          team: intent-cli-dev
          target_repo: J-Tech-Japan/intent-system
          acceptance_criteria:
            - Preserve first local criterion
            - Preserve second local criterion
        verification_contract:
          version: 1
          acceptance_criteria:
            - {id: AC1, criterion_index: 1}
            - {id: AC2, criterion_index: 2}
          lanes:
            - {id: unit, weight: ordinary}
            - {id: build, weight: ordinary}
          affected_lanes: [unit, build]
          proofs:
            - id: P1
              acceptance_criteria: [AC1]
              lane: unit
              command: dotnet test
              evidence: local-required
            - id: P2
              acceptance_criteria: [AC2]
              lane: build
              command: dotnet test
              evidence: local-required
        """;

    [Fact]
    public void Execute_IssuePrCreatedOutcome_WriteModeAppliesSwapAndPublishesPrIntentTarget()
    {
        using var workspace = new WorkerCompleteWorkspace();
        var mutator = new FakeMutator
        {
            Labels = new[] { "intent-target", "intent-issue-in-progress" },
        };
        WorkerCompleteCommand.MutatorFactory = () => mutator;

        using var writer = new StringWriter();
        var exitCode = WorkerCompleteCommand.Execute(
            workspace.Context,
            new[]
            {
                "--repo", "J-Tech-Japan/intent-system",
                "--kind", "issue",
                "--number", "525",
                "--outcome", WorkerResultSummaryConstants.Outcomes.PrCreated,
                "--pr", "999",
                "--write",
                "--format", "json",
            },
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<WorkerCompleteResult>(writer.ToString())!;
        Assert.True(result.Applied);
        Assert.Equal(999, result.PrNumber);
        Assert.True(result.PrTargetApplied);

        // G283: two writer calls — one for the source issue swap, one for the PR intent-target publish.
        Assert.Equal(2, mutator.AppliedTransitions.Count);
        var issueWrite = mutator.AppliedTransitions[0];
        Assert.Equal("issue", issueWrite.Kind);
        Assert.Equal(525, issueWrite.Number);
        Assert.Contains("intent-pr-created", issueWrite.AddLabels);
        Assert.Contains("intent-issue-in-progress", issueWrite.RemoveLabels);

        var prWrite = mutator.AppliedTransitions[1];
        Assert.Equal("pr", prWrite.Kind);
        Assert.Equal(999, prWrite.Number);
        Assert.Contains("intent-target", prWrite.AddLabels);
        Assert.DoesNotContain("intent-pr-created", prWrite.AddLabels);
    }

    [Fact]
    public void Execute_G733_RenderedChildContractMatchesCanonicalTargetPrTransition()
    {
        using var workspace = new WorkerCompleteWorkspace();
        var mutator = new FakeMutator
        {
            Labels = new[] { "intent-target", "intent-issue-in-progress" },
        };
        WorkerCompleteCommand.MutatorFactory = () => mutator;

        using var guideWriter = new StringWriter();
        var guideExit = GuideWorkerIssueToPrCommand.Execute(
            workspace.Context,
            ["--domain", "intent-cli", "--format", "json"],
            guideWriter);

        Assert.Equal(0, guideExit);
        using var guideDocument = JsonDocument.Parse(guideWriter.ToString());
        var prompt = guideDocument.RootElement.GetProperty("prompt").GetString()!;
        Assert.Contains("may apply `intent-target` to the target-repository PR", prompt, StringComparison.Ordinal);
        Assert.Contains("distinct from host-state linkage/publication", prompt, StringComparison.Ordinal);
        Assert.Contains("Do not use raw GitHub label mutation", prompt, StringComparison.Ordinal);

        using var writer = new StringWriter();
        var exitCode = WorkerCompleteCommand.Execute(
            workspace.Context,
            [
                "--repo", "J-Tech-Japan/intent-system",
                "--kind", "issue",
                "--number", "525",
                "--outcome", WorkerResultSummaryConstants.Outcomes.PrCreated,
                "--pr", "999",
                "--github-only",
                "--write",
                "--format", "json",
            ],
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<WorkerCompleteResult>(writer.ToString())!;
        Assert.True(result.GithubOnly);
        Assert.True(result.ChildCwd);
        Assert.True(result.PrTargetApplied);
        Assert.False(result.LinkedPrSynced);

        Assert.Contains(mutator.AppliedTransitions, transition =>
            transition.Kind == "pr"
            && transition.Number == 999
            && transition.AddLabels.Contains("intent-target", StringComparer.Ordinal)
            && !transition.AddLabels.Contains("intent-pr-created", StringComparer.Ordinal));
    }

    // ── G283: PR review publication during issue-to-pr completion ─────

    [Fact]
    public void Execute_IssuePrCreatedOutcomeWithoutPr_ReturnsUsageError()
    {
        using var workspace = new WorkerCompleteWorkspace();
        var mutator = new FakeMutator
        {
            Labels = new[] { "intent-target", "intent-issue-in-progress" },
        };
        WorkerCompleteCommand.MutatorFactory = () => mutator;

        using var writer = new StringWriter();
        var exitCode = WorkerCompleteCommand.Execute(
            workspace.Context,
            new[]
            {
                "--repo", "J-Tech-Japan/intent-system",
                "--kind", "issue",
                "--number", "525",
                "--outcome", WorkerResultSummaryConstants.Outcomes.PrCreated,
                "--write",
                "--format", "json",
            },
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("--pr is required", writer.ToString(), StringComparison.Ordinal);
        Assert.Empty(mutator.AppliedTransitions);
    }

    [Fact]
    public void Execute_IssuePrCreatedOutcomeWithPr_AppliesIntentTargetToPrAndSyncsLinkedPr()
    {
        using var workspace = new WorkerCompleteWorkspace();
        workspace.SeedQueueStateWithLinkedIssue(
            executionUnit: "G283",
            title: "G283 source unit",
            sourceIssueNumber: 525);

        var mutator = new FakeMutator
        {
            Labels = new[] { "intent-target", "intent-issue-in-progress" },
        };
        WorkerCompleteCommand.MutatorFactory = () => mutator;

        using var writer = new StringWriter();
        var exitCode = WorkerCompleteCommand.Execute(
            workspace.Context,
            new[]
            {
                "--repo", "J-Tech-Japan/intent-system",
                "--kind", "issue",
                "--number", "525",
                "--outcome", WorkerResultSummaryConstants.Outcomes.PrCreated,
                "--pr", "999",
                "--write",
                "--format", "json",
            },
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<WorkerCompleteResult>(writer.ToString())!;
        Assert.True(result.Proceed);
        Assert.True(result.Applied);
        Assert.Equal(999, result.PrNumber);
        Assert.True(result.PrTargetApplied);
        Assert.True(result.LinkedPrSynced);

        // Mutator received: (1) issue-side swap, (2) PR-side intent-target add.
        Assert.Equal(2, mutator.AppliedTransitions.Count);
        Assert.Contains(mutator.AppliedTransitions, t =>
            t.Kind == "pr" && t.Number == 999
            && t.AddLabels.Contains("intent-target")
            && !t.AddLabels.Contains("intent-pr-created"));

        // Queue-state linked_pr is synced for the matching unit.
        var queueState = QueueStateSerializer.Deserialize(File.ReadAllText(workspace.QueueStatePath));
        var item = Assert.Single(queueState.Items);
        Assert.Equal("https://github.com/J-Tech-Japan/intent-system/pull/999", item.LinkedPr);
    }

    [Fact]
    public void Execute_CollidingIssueNumbers_UpdatesOnlyRepoQualifiedUnit()
    {
        using var workspace = new WorkerCompleteWorkspace();
        workspace.WriteQueueState(
            workspace.CreateItem("G603", "J-Tech-Japan/intent-system", 525, "intent-cli"),
            workspace.CreateItem("SKS-G593", "J-Tech-Japan/SekibanAsAService", 525, "sekiban-as-a-service",
                existingLinkedPr: "https://github.com/J-Tech-Japan/SekibanAsAService/pull/1306"));
        var foreignBefore = QueueItemBytes(workspace.QueueStatePath, index: 1);
        var mutator = new FakeMutator { Labels = new[] { "intent-target", "intent-issue-in-progress" } };
        WorkerCompleteCommand.MutatorFactory = () => mutator;

        using var writer = new StringWriter();
        var exitCode = WorkerCompleteCommand.Execute(workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--kind", "issue", "--number", "525",
             "--outcome", WorkerResultSummaryConstants.Outcomes.PrCreated, "--pr", "1306", "--write", "--format", "json"], writer);

        Assert.Equal(0, exitCode);
        var state = QueueStateSerializer.Deserialize(File.ReadAllText(workspace.QueueStatePath));
        Assert.Equal("https://github.com/J-Tech-Japan/intent-system/pull/1306", state.Items[0].LinkedPr);
        Assert.Equal("https://github.com/J-Tech-Japan/SekibanAsAService/pull/1306", state.Items[1].LinkedPr);
        Assert.Equal(foreignBefore, QueueItemBytes(workspace.QueueStatePath, index: 1));
    }

    [Fact]
    public void Execute_DurableQueueDomainWinsOverHostDefaultAndCompletes()
    {
        using var workspace = new WorkerCompleteWorkspace();
        workspace.WriteQueueState(workspace.CreateItem("G724", "J-Tech-Japan/intent-system", 1305, "sekiban-as-a-service"));
        var mutator = new FakeMutator { Labels = new[] { "intent-target", "intent-issue-in-progress" } };
        WorkerCompleteCommand.MutatorFactory = () => mutator;
        WorkerCompleteCommand.PrLookupFactory = () => new StubPrLookup
        {
            Body = "Closes #1305",
        };

        using var writer = new StringWriter();
        var exitCode = WorkerCompleteCommand.Execute(workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--kind", "issue", "--number", "1305",
             "--outcome", WorkerResultSummaryConstants.Outcomes.PrCreated, "--pr", "1306", "--write", "--format", "json"], writer);

        Assert.True(exitCode == 0, writer.ToString());
        var result = JsonSerializer.Deserialize<WorkerCompleteResult>(writer.ToString())!;
        Assert.Equal("sekiban-as-a-service", result.Domain);
        Assert.Equal("queue-record", result.DomainSource);
        Assert.Equal("G724", result.ExecutionUnit);
        Assert.NotEmpty(mutator.AppliedTransitions);
    }

    [Fact]
    public void Execute_TwoDomainHost_CompletesDomainBAndLeavesDomainAMarkerUntouched_G724()
    {
        using var workspace = new WorkerCompleteWorkspace();
        workspace.RecordSessionLayer("intent-cli", "alpha", SessionLayerMode.Agmsg);
        workspace.RecordSessionLayer("sekiban-as-a-service", "beta", SessionLayerMode.HerdrOnly);
        var startup = workspace.WriteStartupMarker(
            "<!-- intent-cli:session-layer-marker:start domain=\"intent-cli\" team=\"alpha\" -->\n"
            + "<!-- intent-cli:session-layer-marker:end -->\n");
        workspace.GenerateSessionLayerMarker("intent-cli", "alpha", startup);
        var markerBefore = File.ReadAllBytes(startup);
        workspace.WriteQueueState(workspace.CreateItem("G724", "J-Tech-Japan/intent-system", 1570, "sekiban-as-a-service"));

        var mutator = new FakeMutator { Labels = new[] { "intent-target", "intent-issue-in-progress" } };
        WorkerCompleteCommand.MutatorFactory = () => mutator;
        WorkerCompleteCommand.PrLookupFactory = () => new StubPrLookup
        {
            Body = "Closes #1570",
        };

        using var writer = new StringWriter();
        var exitCode = WorkerCompleteCommand.Execute(workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--kind", "issue", "--number", "1570",
             "--outcome", WorkerResultSummaryConstants.Outcomes.PrCreated, "--pr", "1571", "--write", "--format", "json"], writer);

        Assert.True(exitCode == 0, writer.ToString());
        var result = JsonSerializer.Deserialize<WorkerCompleteResult>(writer.ToString())!;
        Assert.Equal("sekiban-as-a-service", result.Domain);
        Assert.Equal("queue-record", result.DomainSource);
        Assert.Equal("G724", result.ExecutionUnit);
        Assert.Equal(markerBefore, File.ReadAllBytes(startup));
        Assert.Contains("domain=\"intent-cli\" team=\"alpha\"", File.ReadAllText(startup), StringComparison.Ordinal);
        Assert.DoesNotContain("sekiban-as-a-service", File.ReadAllText(startup), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_ExplicitDomainContradiction_RefusesBeforeLabelOrQueueWrite()
    {
        using var workspace = new WorkerCompleteWorkspace();
        workspace.WriteQueueState(workspace.CreateItem("SKS-G593", "J-Tech-Japan/intent-system", 1305, "sekiban-as-a-service"));
        var before = File.ReadAllBytes(workspace.QueueStatePath);
        var mutator = new FakeMutator { Labels = new[] { "intent-target", "intent-issue-in-progress" } };
        WorkerCompleteCommand.MutatorFactory = () => mutator;

        using var writer = new StringWriter();
        var exitCode = WorkerCompleteCommand.Execute(workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--domain", "intent-cli", "--kind", "issue", "--number", "1305",
             "--outcome", WorkerResultSummaryConstants.Outcomes.PrCreated, "--pr", "1306", "--write", "--format", "json"], writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("explicit domain 'intent-cli'", writer.ToString(), StringComparison.Ordinal);
        Assert.Contains("sekiban-as-a-service", writer.ToString(), StringComparison.Ordinal);
        Assert.Contains("--domain sekiban-as-a-service", writer.ToString(), StringComparison.Ordinal);
        Assert.Empty(mutator.AppliedTransitions);
        Assert.Equal(before, File.ReadAllBytes(workspace.QueueStatePath));
    }

    [Fact]
    public void Execute_MissingDurableDomainOnMultiDomainHost_EmitsWorkerRecovery()
    {
        using var workspace = new WorkerCompleteWorkspace();
        workspace.RecordSessionLayer("intent-cli", "alpha", SessionLayerMode.Agmsg);
        workspace.RecordSessionLayer("sekiban-as-a-service", "beta", SessionLayerMode.HerdrOnly);
        workspace.WriteQueueState(workspace.CreateItemWithoutDomain("G724-legacy", "J-Tech-Japan/intent-system", 1570));
        var before = File.ReadAllBytes(workspace.QueueStatePath);
        var mutator = new FakeMutator { Labels = new[] { "intent-target", "intent-issue-in-progress" } };
        WorkerCompleteCommand.MutatorFactory = () => mutator;

        using var writer = new StringWriter();
        var exitCode = WorkerCompleteCommand.Execute(workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--kind", "issue", "--number", "1570",
             "--outcome", WorkerResultSummaryConstants.Outcomes.PrCreated, "--pr", "1571", "--write", "--format", "json"], writer);

        Assert.Equal(1, exitCode);
        var output = writer.ToString();
        Assert.Contains("has no declared domain", output, StringComparison.Ordinal);
        Assert.Contains("--domain intent-cli", output, StringComparison.Ordinal);
        Assert.Contains("--domain sekiban-as-a-service", output, StringComparison.Ordinal);
        Assert.Contains(" or ", output, StringComparison.Ordinal);
        Assert.Contains("startup marker", output, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(mutator.AppliedTransitions);
        Assert.Equal(before, File.ReadAllBytes(workspace.QueueStatePath));
    }

    [Fact]
    public void Execute_LegacyDomainlessRowWithoutRecordedSessionDomains_UsesPlaceholderRecovery_G724()
    {
        using var workspace = new WorkerCompleteWorkspace();
        workspace.WriteQueueState(workspace.CreateItemWithoutDomain("G724-legacy", "J-Tech-Japan/intent-system", 1570));
        var before = File.ReadAllBytes(workspace.QueueStatePath);
        var mutator = new FakeMutator { Labels = new[] { "intent-target", "intent-issue-in-progress" } };
        WorkerCompleteCommand.MutatorFactory = () => mutator;

        using var writer = new StringWriter();
        var exitCode = WorkerCompleteCommand.Execute(workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--kind", "issue", "--number", "1570",
             "--outcome", WorkerResultSummaryConstants.Outcomes.PrCreated, "--pr", "1571", "--write", "--format", "json"], writer);

        Assert.Equal(1, exitCode);
        var output = writer.ToString();
        Assert.Contains("has no declared domain", output, StringComparison.Ordinal);
        Assert.Contains("--domain <domain>", output, StringComparison.Ordinal);
        Assert.DoesNotContain("--domain intent-cli", output, StringComparison.Ordinal);
        Assert.Contains("startup marker", output, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(mutator.AppliedTransitions);
        Assert.Equal(before, File.ReadAllBytes(workspace.QueueStatePath));
    }

    [Fact]
    public void Execute_ExplicitLegacyDomainMustAgreeWithRecordedSessionLayer_G724()
    {
        using var workspace = new WorkerCompleteWorkspace();
        workspace.RecordSessionLayer("sekiban-as-a-service", "beta", SessionLayerMode.HerdrOnly);
        workspace.WriteQueueState(workspace.CreateItemWithoutDomain("G724-legacy", "J-Tech-Japan/intent-system", 1570));
        var before = File.ReadAllBytes(workspace.QueueStatePath);
        var mutator = new FakeMutator { Labels = new[] { "intent-target", "intent-issue-in-progress" } };
        WorkerCompleteCommand.MutatorFactory = () => mutator;

        using var writer = new StringWriter();
        var exitCode = WorkerCompleteCommand.Execute(workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--domain", "intent-cli", "--kind", "issue", "--number", "1570",
             "--outcome", WorkerResultSummaryConstants.Outcomes.PrCreated, "--pr", "1571", "--write", "--format", "json"], writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("explicit domain 'intent-cli'", writer.ToString(), StringComparison.Ordinal);
        Assert.Contains("session-layer record", writer.ToString(), StringComparison.Ordinal);
        Assert.Contains("--domain sekiban-as-a-service", writer.ToString(), StringComparison.Ordinal);
        Assert.Empty(mutator.AppliedTransitions);
        Assert.Equal(before, File.ReadAllBytes(workspace.QueueStatePath));
    }

    [Fact]
    public void Execute_RecordedSessionLayerDomainIsUsedWhenIssueHasNoQueueMatch_G724()
    {
        using var workspace = new WorkerCompleteWorkspace();
        workspace.RecordSessionLayer("sekiban-as-a-service", "beta", SessionLayerMode.HerdrOnly);
        workspace.WriteQueueState(workspace.CreateItemWithoutDomain("unrelated", "J-Tech-Japan/intent-system", 9999));
        var mutator = new FakeMutator { Labels = new[] { "intent-target", "intent-issue-in-progress" } };
        WorkerCompleteCommand.MutatorFactory = () => mutator;
        WorkerCompleteCommand.PrLookupFactory = () => new StubPrLookup { Body = "Closes #1570" };

        using var writer = new StringWriter();
        var exitCode = WorkerCompleteCommand.Execute(workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--kind", "issue", "--number", "1570",
             "--outcome", WorkerResultSummaryConstants.Outcomes.PrCreated, "--pr", "1571", "--dry-run", "--format", "json"], writer);

        Assert.True(exitCode == 0, writer.ToString());
        var result = JsonSerializer.Deserialize<WorkerCompleteResult>(writer.ToString())!;
        Assert.True(string.Equals(result.Domain, "sekiban-as-a-service", StringComparison.Ordinal), writer.ToString());
        Assert.Equal("session-layer-record", result.DomainSource);
        Assert.Null(result.ExecutionUnit);
        Assert.Empty(mutator.AppliedTransitions);
    }

    [Theory]
    [InlineData("", "intent-system")]
    [InlineData("J-Tech-Japan", "")]
    public void MatchesClosingIssue_PartialRepositoryDescriptor_IsRejected(string owner, string name)
    {
        var reference = new GitHubPrClosingIssueReference
        {
            Number = 1309,
            Repository = new GitHubPrClosingIssueRepository
            {
                Name = name,
                Owner = new GitHubPrClosingIssueRepositoryOwner { Login = owner },
            },
        };

        Assert.False(GitHubWorkItemIdentity.MatchesClosingIssue(
            reference, "J-Tech-Japan/intent-system", 1309));
    }

    [Fact]
    public void Execute_IssuePrCreatedOutcomeWithParentIntentRoot_SyncsParentQueueStateLinkedPr()
    {
        using var parentWorkspace = new WorkerCompleteWorkspace();
        parentWorkspace.SeedQueueStateWithLinkedIssue(
            executionUnit: "G283",
            title: "G283 source unit",
            sourceIssueNumber: 525);
        using var childWorkspace = new WorkerCompleteWorkspace(parentIntentRepoRoot: parentWorkspace.RootPath);
        Assert.False(File.Exists(childWorkspace.QueueStatePath));

        var mutator = new FakeMutator
        {
            Labels = new[] { "intent-target", "intent-issue-in-progress" },
        };
        WorkerCompleteCommand.MutatorFactory = () => mutator;

        using var writer = new StringWriter();
        var exitCode = WorkerCompleteCommand.Execute(
            childWorkspace.Context,
            new[]
            {
                "--repo", "J-Tech-Japan/intent-system",
                "--kind", "issue",
                "--number", "525",
                "--outcome", WorkerResultSummaryConstants.Outcomes.PrCreated,
                "--pr", "999",
                "--write",
                "--format", "json",
            },
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<WorkerCompleteResult>(writer.ToString())!;
        Assert.True(result.LinkedPrSynced);
        Assert.False(File.Exists(childWorkspace.QueueStatePath));

        var queueState = QueueStateSerializer.Deserialize(File.ReadAllText(parentWorkspace.QueueStatePath));
        var item = Assert.Single(queueState.Items);
        Assert.Equal("https://github.com/J-Tech-Japan/intent-system/pull/999", item.LinkedPr);
    }

    [Fact]
    public void Execute_PrTargetApplicationFailure_ReportsIncompleteWithReconcileGuidance()
    {
        using var workspace = new WorkerCompleteWorkspace();
        var mutator = new FailingPrMutator
        {
            Labels = new[] { "intent-target", "intent-issue-in-progress" },
        };
        WorkerCompleteCommand.MutatorFactory = () => mutator;

        using var writer = new StringWriter();
        var exitCode = WorkerCompleteCommand.Execute(
            workspace.Context,
            new[]
            {
                "--repo", "J-Tech-Japan/intent-system",
                "--kind", "issue",
                "--number", "525",
                "--outcome", WorkerResultSummaryConstants.Outcomes.PrCreated,
                "--pr", "999",
                "--write",
                "--format", "json",
            },
            writer);

        Assert.Equal(2, exitCode);
        var result = JsonSerializer.Deserialize<WorkerCompleteResult>(writer.ToString())!;
        Assert.False(result.Proceed);
        Assert.False(result.Applied);
        Assert.False(result.PrTargetApplied);
        Assert.Contains(result.Errors, e =>
            e.Contains("intent-target", StringComparison.Ordinal)
            && e.Contains("PR #999", StringComparison.Ordinal)
            && e.Contains("automation reconcile", StringComparison.Ordinal));
    }

    [Fact]
    public void Execute_IssuePrCreatedOutcome_LinkedPrSyncWarnsWhenQueueStateAbsent()
    {
        // G283/G330: when a parent intent repo root IS configured but
        // the queue-state file is missing, the writer surfaces the
        // pre-G330 "queue-state.json not found" warning — this is the
        // host-context drift signal that recommends running
        // `automation reconcile`. (Child-cwd mode without a parent
        // root is exercised in a separate G330 test, see below.)
        using var parentWorkspace = new WorkerCompleteWorkspace();
        // intentionally do NOT seed queue-state.json on the parent
        Assert.False(File.Exists(parentWorkspace.QueueStatePath));
        using var workspace = new WorkerCompleteWorkspace(parentIntentRepoRoot: parentWorkspace.RootPath);

        var mutator = new FakeMutator
        {
            Labels = new[] { "intent-target", "intent-issue-in-progress" },
        };
        WorkerCompleteCommand.MutatorFactory = () => mutator;

        using var writer = new StringWriter();
        var exitCode = WorkerCompleteCommand.Execute(
            workspace.Context,
            new[]
            {
                "--repo", "J-Tech-Japan/intent-system",
                "--kind", "issue",
                "--number", "525",
                "--outcome", WorkerResultSummaryConstants.Outcomes.PrCreated,
                "--pr", "999",
                "--write",
                "--format", "json",
            },
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<WorkerCompleteResult>(writer.ToString())!;
        Assert.True(result.Proceed);
        Assert.True(result.PrTargetApplied);
        Assert.False(result.LinkedPrSynced);
        Assert.False(result.ChildCwd,
            "with a parent intent repo root configured the writer must NOT auto-enter child-cwd mode.");
        Assert.Contains(result.Warnings, w => w.Contains("queue-state.json not found", StringComparison.Ordinal));
    }

    [Fact]
    public void Execute_IssuePrCreatedRerunIdempotent_DoesNotDoubleSyncLinkedPr()
    {
        using var workspace = new WorkerCompleteWorkspace();
        workspace.SeedQueueStateWithLinkedIssue(
            executionUnit: "G283",
            title: "G283 unit",
            sourceIssueNumber: 525,
            existingLinkedPr: "https://github.com/J-Tech-Japan/intent-system/pull/999");

        var mutator = new FakeMutator
        {
            Labels = new[] { "intent-target", "intent-issue-in-progress" },
        };
        WorkerCompleteCommand.MutatorFactory = () => mutator;

        var beforeBytes = File.ReadAllBytes(workspace.QueueStatePath);

        using var writer = new StringWriter();
        var exitCode = WorkerCompleteCommand.Execute(
            workspace.Context,
            new[]
            {
                "--repo", "J-Tech-Japan/intent-system",
                "--kind", "issue",
                "--number", "525",
                "--outcome", WorkerResultSummaryConstants.Outcomes.PrCreated,
                "--pr", "999",
                "--write",
                "--format", "json",
            },
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<WorkerCompleteResult>(writer.ToString())!;
        Assert.True(result.LinkedPrSynced);
        // queue-state.json is unchanged when linked_pr already matches.
        var afterBytes = File.ReadAllBytes(workspace.QueueStatePath);
        Assert.Equal(beforeBytes, afterBytes);
    }

    [Fact]
    public void Execute_IssuePrCreatedAlreadyRecordedWithMissingLinkedPr_RepairsPrPublicationMetadata()
    {
        using var workspace = new WorkerCompleteWorkspace();
        workspace.SeedQueueStateWithLinkedIssue(
            executionUnit: "G283",
            title: "G283 unit",
            sourceIssueNumber: 525);

        var mutator = new FakeMutator
        {
            Labels = new[] { "intent-target", "intent-pr-created" },
        };
        WorkerCompleteCommand.MutatorFactory = () => mutator;

        using var writer = new StringWriter();
        var exitCode = WorkerCompleteCommand.Execute(
            workspace.Context,
            new[]
            {
                "--repo", "J-Tech-Japan/intent-system",
                "--kind", "issue",
                "--number", "525",
                "--outcome", WorkerResultSummaryConstants.Outcomes.PrCreated,
                "--pr", "999",
                "--write",
                "--format", "json",
            },
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<WorkerCompleteResult>(writer.ToString())!;
        Assert.True(result.Proceed);
        Assert.True(result.Applied);
        Assert.True(result.PrTargetApplied);
        Assert.True(result.LinkedPrSynced);
        Assert.Empty(result.Errors);

        var transition = Assert.Single(mutator.AppliedTransitions);
        Assert.Equal("pr", transition.Kind);
        Assert.Equal(999, transition.Number);
        Assert.Contains("intent-target", transition.AddLabels);

        var queueState = QueueStateSerializer.Deserialize(File.ReadAllText(workspace.QueueStatePath));
        var item = Assert.Single(queueState.Items);
        Assert.Equal("https://github.com/J-Tech-Japan/intent-system/pull/999", item.LinkedPr);
    }

    [Fact]
    public void Execute_IssueNotClaimed_RefusesCompleteNotClaimed()
    {
        using var workspace = new WorkerCompleteWorkspace();
        var mutator = new FakeMutator
        {
            Labels = new[] { "intent-target" }, // no in-progress
        };
        WorkerCompleteCommand.MutatorFactory = () => mutator;

        using var writer = new StringWriter();
        var exitCode = WorkerCompleteCommand.Execute(
            workspace.Context,
            new[]
            {
                "--repo", "J-Tech-Japan/intent-system",
                "--kind", "issue",
                "--number", "525",
                "--outcome", WorkerResultSummaryConstants.Outcomes.PrCreated,
                "--pr", "999",
                "--write",
                "--format", "json",
            },
            writer);

        Assert.Equal(2, exitCode);
        var result = JsonSerializer.Deserialize<WorkerCompleteResult>(writer.ToString())!;
        Assert.False(result.Proceed);
        Assert.False(result.Applied);
        Assert.Contains(result.Errors, e => e.StartsWith(WorkerClaimCompleteConstants.ErrorCodes.CompleteNotClaimed, StringComparison.Ordinal));
        Assert.Empty(mutator.AppliedTransitions);
    }

    [Fact]
    public void Execute_IssueAlreadyPrCreated_RefusesAlreadyCompleted()
    {
        using var workspace = new WorkerCompleteWorkspace();
        var mutator = new FakeMutator
        {
            Labels = new[] { "intent-target", "intent-issue-in-progress", "intent-pr-created" },
        };
        WorkerCompleteCommand.MutatorFactory = () => mutator;

        using var writer = new StringWriter();
        var exitCode = WorkerCompleteCommand.Execute(
            workspace.Context,
            new[]
            {
                "--repo", "J-Tech-Japan/intent-system",
                "--kind", "issue",
                "--number", "525",
                "--outcome", WorkerResultSummaryConstants.Outcomes.PrCreated,
                "--pr", "999",
                "--format", "json",
            },
            writer);

        Assert.Equal(2, exitCode);
        var result = JsonSerializer.Deserialize<WorkerCompleteResult>(writer.ToString())!;
        Assert.Contains(result.Errors, e => e.StartsWith(WorkerClaimCompleteConstants.ErrorCodes.CompleteAlreadyCompleted, StringComparison.Ordinal));
    }

    [Fact]
    public void Execute_IssueClarificationOutcome_RemovesInProgressOnly()
    {
        using var workspace = new WorkerCompleteWorkspace();
        var mutator = new FakeMutator
        {
            Labels = new[] { "intent-target", "intent-issue-in-progress" },
        };
        WorkerCompleteCommand.MutatorFactory = () => mutator;

        using var writer = new StringWriter();
        var exitCode = WorkerCompleteCommand.Execute(
            workspace.Context,
            new[]
            {
                "--repo", "J-Tech-Japan/intent-system",
                "--kind", "issue",
                "--number", "525",
                "--outcome", WorkerResultSummaryConstants.Outcomes.ClarificationRequired,
                "--write",
                "--format", "json",
            },
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<WorkerCompleteResult>(writer.ToString())!;
        Assert.Contains("intent-issue-in-progress", result.RemoveLabels);
        Assert.DoesNotContain("intent-pr-created", result.AddLabels);
    }

    [Fact]
    public void Execute_PrRepairPushedOutcome_DryRunPlansSwapUpdateInProgressForRereviewReady()
    {
        using var workspace = new WorkerCompleteWorkspace();
        var mutator = new FakeMutator
        {
            Labels = new[] { "intent-target", "intent-pr-request-update", "intent-pr-update-in-progress" },
        };
        WorkerCompleteCommand.MutatorFactory = () => mutator;

        using var writer = new StringWriter();
        var exitCode = WorkerCompleteCommand.Execute(
            workspace.Context,
            new[]
            {
                "--repo", "J-Tech-Japan/intent-system",
                "--kind", "pr",
                "--number", "514",
                "--outcome", WorkerResultSummaryConstants.Outcomes.RepairPushed,
                "--format", "json",
            },
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<WorkerCompleteResult>(writer.ToString())!;
        Assert.True(result.Proceed);
        Assert.False(result.Applied);
        Assert.Contains("intent-pr-rereview-ready", result.AddLabels);
        Assert.Contains("intent-pr-update-in-progress", result.RemoveLabels);
        Assert.Contains("intent-pr-request-update", result.RemoveLabels);
        Assert.Empty(mutator.AppliedTransitions);

        // intent-pr-created MUST NOT be added to a PR.
        Assert.DoesNotContain("intent-pr-created", result.AddLabels);
    }

    [Fact]
    public void Execute_PrAlreadyRereviewReady_RefusesAlreadyCompletedOnRepairPushed()
    {
        using var workspace = new WorkerCompleteWorkspace();
        var mutator = new FakeMutator
        {
            Labels = new[] { "intent-target", "intent-pr-update-in-progress", "intent-pr-rereview-ready" },
        };
        WorkerCompleteCommand.MutatorFactory = () => mutator;

        using var writer = new StringWriter();
        var exitCode = WorkerCompleteCommand.Execute(
            workspace.Context,
            new[]
            {
                "--repo", "J-Tech-Japan/intent-system",
                "--kind", "pr",
                "--number", "514",
                "--outcome", WorkerResultSummaryConstants.Outcomes.RepairPushed,
                "--write",
                "--format", "json",
            },
            writer);

        Assert.Equal(2, exitCode);
        var result = JsonSerializer.Deserialize<WorkerCompleteResult>(writer.ToString())!;
        Assert.Contains(result.Errors, e => e.StartsWith(WorkerClaimCompleteConstants.ErrorCodes.CompleteAlreadyCompleted, StringComparison.Ordinal));
        Assert.Empty(mutator.AppliedTransitions);
    }

    [Fact]
    public void Execute_PrRepairPushedOutcome_WriteRemovesRequestUpdateWhenAddingRereviewReady()
    {
        using var workspace = new WorkerCompleteWorkspace();
        var mutator = new FakeMutator
        {
            Labels = new[] { "intent-target", "intent-pr-request-update", "intent-pr-update-in-progress" },
        };
        WorkerCompleteCommand.MutatorFactory = () => mutator;

        using var writer = new StringWriter();
        var exitCode = WorkerCompleteCommand.Execute(
            workspace.Context,
            new[]
            {
                "--repo", "J-Tech-Japan/intent-system",
                "--kind", "pr",
                "--number", "514",
                "--outcome", WorkerResultSummaryConstants.Outcomes.RepairPushed,
                "--write",
                "--format", "json",
            },
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<WorkerCompleteResult>(writer.ToString())!;
        Assert.True(result.Proceed);
        Assert.True(result.Applied);
        Assert.Contains("intent-pr-rereview-ready", result.AddLabels);
        Assert.Contains("intent-pr-update-in-progress", result.RemoveLabels);
        Assert.Contains("intent-pr-request-update", result.RemoveLabels);

        var transition = Assert.Single(mutator.AppliedTransitions);
        Assert.Contains("intent-pr-rereview-ready", transition.AddLabels);
        Assert.Contains("intent-pr-update-in-progress", transition.RemoveLabels);
        Assert.Contains("intent-pr-request-update", transition.RemoveLabels);

        var finalLabels = mutator.Labels
            .Except(transition.RemoveLabels, StringComparer.Ordinal)
            .Concat(transition.AddLabels)
            .ToArray();
        Assert.Contains("intent-pr-rereview-ready", finalLabels);
        Assert.DoesNotContain("intent-pr-request-update", finalLabels);
    }

    [Fact]
    public void Execute_PrAlreadyResolvedOutcome_WriteConvergesToRereviewReadyAndClearsRequestUpdate()
    {
        // G372: completing a PR-comment-fix with `already-resolved` (the
        // requested change is already on the branch) must converge the PR
        // exactly like repair-pushed — remove intent-pr-update-in-progress
        // AND intent-pr-request-update, add intent-pr-rereview-ready — so
        // the selector cannot re-pick the PR on the next wake. This pins the
        // fix for the observed PR #842 5-minute oscillation.
        using var workspace = new WorkerCompleteWorkspace();
        var mutator = new FakeMutator
        {
            Labels = new[] { "intent-target", "intent-pr-request-update", "intent-pr-update-in-progress" },
        };
        WorkerCompleteCommand.MutatorFactory = () => mutator;

        using var writer = new StringWriter();
        var exitCode = WorkerCompleteCommand.Execute(
            workspace.Context,
            new[]
            {
                "--repo", "J-Tech-Japan/intent-system",
                "--kind", "pr",
                "--number", "842",
                "--outcome", WorkerResultSummaryConstants.Outcomes.AlreadyResolved,
                "--write",
                "--format", "json",
            },
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<WorkerCompleteResult>(writer.ToString())!;
        Assert.True(result.Proceed);
        Assert.True(result.Applied);
        Assert.Contains("intent-pr-rereview-ready", result.AddLabels);
        Assert.Contains("intent-pr-update-in-progress", result.RemoveLabels);
        Assert.Contains("intent-pr-request-update", result.RemoveLabels);
        // intent-pr-created MUST NOT be added to a PR.
        Assert.DoesNotContain("intent-pr-created", result.AddLabels);

        var transition = Assert.Single(mutator.AppliedTransitions);
        Assert.Contains("intent-pr-rereview-ready", transition.AddLabels);
        Assert.Contains("intent-pr-update-in-progress", transition.RemoveLabels);
        Assert.Contains("intent-pr-request-update", transition.RemoveLabels);

        // The resulting PR must carry NO child-actionable update label, so
        // `worker next-action` returns `none` for it until the host opens a
        // new review request.
        var finalLabels = mutator.Labels
            .Except(transition.RemoveLabels, StringComparer.Ordinal)
            .Concat(transition.AddLabels)
            .ToArray();
        Assert.Contains("intent-pr-rereview-ready", finalLabels);
        Assert.DoesNotContain("intent-pr-request-update", finalLabels);
        Assert.DoesNotContain("intent-pr-update-in-progress", finalLabels);
    }

    [Fact]
    public void Execute_PrAlreadyResolvedOnConvergedPr_RefusesAlreadyCompleted()
    {
        // G372: once a PR has converged to intent-pr-rereview-ready, a
        // repeat already-resolved completion must refuse rather than
        // re-add the label — mirroring the existing repair-pushed guard.
        using var workspace = new WorkerCompleteWorkspace();
        var mutator = new FakeMutator
        {
            Labels = new[] { "intent-target", "intent-pr-update-in-progress", "intent-pr-rereview-ready" },
        };
        WorkerCompleteCommand.MutatorFactory = () => mutator;

        using var writer = new StringWriter();
        var exitCode = WorkerCompleteCommand.Execute(
            workspace.Context,
            new[]
            {
                "--repo", "J-Tech-Japan/intent-system",
                "--kind", "pr",
                "--number", "842",
                "--outcome", WorkerResultSummaryConstants.Outcomes.AlreadyResolved,
                "--write",
                "--format", "json",
            },
            writer);

        Assert.Equal(2, exitCode);
        var result = JsonSerializer.Deserialize<WorkerCompleteResult>(writer.ToString())!;
        Assert.Contains(result.Errors, e => e.StartsWith(WorkerClaimCompleteConstants.ErrorCodes.CompleteAlreadyCompleted, StringComparison.Ordinal));
        Assert.Empty(mutator.AppliedTransitions);
    }

    [Fact]
    public void Execute_PrFailedOutcome_RemovesUpdateInProgressOnly()
    {
        using var workspace = new WorkerCompleteWorkspace();
        var mutator = new FakeMutator
        {
            Labels = new[] { "intent-target", "intent-pr-update-in-progress" },
        };
        WorkerCompleteCommand.MutatorFactory = () => mutator;

        using var writer = new StringWriter();
        var exitCode = WorkerCompleteCommand.Execute(
            workspace.Context,
            new[]
            {
                "--repo", "J-Tech-Japan/intent-system",
                "--kind", "pr",
                "--number", "514",
                "--outcome", WorkerResultSummaryConstants.Outcomes.Failed,
                "--write",
                "--format", "json",
            },
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<WorkerCompleteResult>(writer.ToString())!;
        Assert.Contains("intent-pr-update-in-progress", result.RemoveLabels);
        Assert.Empty(result.AddLabels);
    }

    [Fact]
    public void Execute_PrUnsupportedOutcomeForKind_RefusesUnsupportedKindOutcome()
    {
        using var workspace = new WorkerCompleteWorkspace();
        var mutator = new FakeMutator
        {
            Labels = new[] { "intent-target", "intent-pr-update-in-progress" },
        };
        WorkerCompleteCommand.MutatorFactory = () => mutator;

        using var writer = new StringWriter();
        // pr-created is an issue-to-pr outcome, not a pr-comment-fix outcome.
        var exitCode = WorkerCompleteCommand.Execute(
            workspace.Context,
            new[]
            {
                "--repo", "J-Tech-Japan/intent-system",
                "--kind", "pr",
                "--number", "514",
                "--outcome", WorkerResultSummaryConstants.Outcomes.PrCreated,
                "--format", "json",
            },
            writer);

        Assert.Equal(2, exitCode);
        var result = JsonSerializer.Deserialize<WorkerCompleteResult>(writer.ToString())!;
        Assert.Contains(result.Errors, e => e.StartsWith(WorkerClaimCompleteConstants.ErrorCodes.UnsupportedKindOutcome, StringComparison.Ordinal));
    }

    [Fact]
    public void Execute_IssueUnsupportedOutcomeForKind_RefusesUnsupportedKindOutcome()
    {
        using var workspace = new WorkerCompleteWorkspace();
        var mutator = new FakeMutator
        {
            Labels = new[] { "intent-target", "intent-issue-in-progress" },
        };
        WorkerCompleteCommand.MutatorFactory = () => mutator;

        using var writer = new StringWriter();
        // repair-pushed is a pr-comment-fix outcome, not an issue-to-pr outcome.
        var exitCode = WorkerCompleteCommand.Execute(
            workspace.Context,
            new[]
            {
                "--repo", "J-Tech-Japan/intent-system",
                "--kind", "issue",
                "--number", "525",
                "--outcome", WorkerResultSummaryConstants.Outcomes.RepairPushed,
                "--format", "json",
            },
            writer);

        Assert.Equal(2, exitCode);
        var result = JsonSerializer.Deserialize<WorkerCompleteResult>(writer.ToString())!;
        Assert.Contains(result.Errors, e => e.StartsWith(WorkerClaimCompleteConstants.ErrorCodes.UnsupportedKindOutcome, StringComparison.Ordinal));
    }

    [Fact]
    public void Execute_IntentPrCreatedNeverEndsUpInPrAddList()
    {
        // This test sweeps every supported pr-comment-fix outcome and
        // asserts that intent-pr-created is never proposed as an add on
        // a PR. Together with the mutator's defensive guard it locks the
        // "intent-pr-created is issue-only" invariant end-to-end.
        var prOutcomes = new[]
        {
            WorkerResultSummaryConstants.Outcomes.RepairPushed,
            WorkerResultSummaryConstants.Outcomes.NoActionableComments,
            WorkerResultSummaryConstants.Outcomes.AlreadyResolved,
            WorkerResultSummaryConstants.Outcomes.ClarificationRequired,
            WorkerResultSummaryConstants.Outcomes.Failed,
            WorkerResultSummaryConstants.Outcomes.LabelCleanupRequired,
        };

        foreach (var outcome in prOutcomes)
        {
            using var workspace = new WorkerCompleteWorkspace();
            // Different setups for "stale" outcomes — but the test is
            // structural: regardless of proceed=true/false, the proposed
            // AddLabels must not contain intent-pr-created.
            var mutator = new FakeMutator
            {
                Labels = new[] { "intent-target", "intent-pr-update-in-progress" },
            };
            WorkerCompleteCommand.MutatorFactory = () => mutator;

            using var writer = new StringWriter();
            WorkerCompleteCommand.Execute(
                workspace.Context,
                new[]
                {
                    "--repo", "J-Tech-Japan/intent-system",
                    "--kind", "pr",
                    "--number", "514",
                    "--outcome", outcome,
                    "--format", "json",
                },
                writer);

            var raw = writer.ToString();
            // Trim warning text that mentions "intent-pr-created" in
            // the misplaced-label policy phrasing — we only care about
            // the add_labels[] array contents here.
            using var doc = JsonDocument.Parse(raw);
            var addLabels = doc.RootElement.GetProperty("add_labels");
            for (var i = 0; i < addLabels.GetArrayLength(); i++)
            {
                var label = addLabels[i].GetString();
                Assert.NotEqual("intent-pr-created", label);
            }
        }
    }

    [Fact]
    public void Execute_MissingRequiredArguments_ReturnsNonZero()
    {
        using var workspace = new WorkerCompleteWorkspace();
        using var writer = new StringWriter();

        var exitCode = WorkerCompleteCommand.Execute(
            workspace.Context, Array.Empty<string>(), writer);

        Assert.NotEqual(0, exitCode);
        Assert.Contains("--repo is required", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_MissingOutcome_ReturnsNonZero()
    {
        using var workspace = new WorkerCompleteWorkspace();
        using var writer = new StringWriter();

        var exitCode = WorkerCompleteCommand.Execute(
            workspace.Context,
            new[] { "--repo", "J-Tech-Japan/intent-system", "--kind", "issue", "--number", "525" },
            writer);

        Assert.NotEqual(0, exitCode);
        Assert.Contains("--outcome is required", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void JsonOutput_IncludesCamelCaseAliasesForLabelArrays()
    {
        using var workspace = new WorkerCompleteWorkspace();
        var mutator = new FakeMutator
        {
            Labels = new[] { "intent-target", "intent-issue-in-progress" },
        };
        WorkerCompleteCommand.MutatorFactory = () => mutator;

        using var writer = new StringWriter();
        var exitCode = WorkerCompleteCommand.Execute(
            workspace.Context,
            new[]
            {
                "--repo", "J-Tech-Japan/intent-system",
                "--kind", "issue",
                "--number", "525",
                "--outcome", WorkerResultSummaryConstants.Outcomes.PrCreated,
                "--pr", "999",
                "--format", "json",
            },
            writer);

        Assert.Equal(0, exitCode);
        var raw = writer.ToString();
        Assert.Contains("\"add_labels\"", raw, StringComparison.Ordinal);
        Assert.Contains("\"addLabels\"", raw, StringComparison.Ordinal);
        Assert.Contains("\"remove_labels\"", raw, StringComparison.Ordinal);
        Assert.Contains("\"removeLabels\"", raw, StringComparison.Ordinal);
        Assert.Contains("\"current_labels\"", raw, StringComparison.Ordinal);
        Assert.Contains("\"currentLabels\"", raw, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_NeverInvokesNestedProviderLauncher()
    {
        using var workspace = new WorkerCompleteWorkspace();
        var launcherInvoked = false;
        WorkerCompleteCommand.NestedProviderLauncher = () =>
        {
            launcherInvoked = true;
            return true;
        };

        WorkerCompleteCommand.MutatorFactory = () => new FakeMutator
        {
            Labels = new[] { "intent-target", "intent-issue-in-progress" },
        };

        using var writer = new StringWriter();
        Assert.Equal(0, WorkerCompleteCommand.Execute(workspace.Context,
            new[]
            {
                "--repo", "J-Tech-Japan/intent-system",
                "--kind", "issue",
                "--number", "525",
                "--outcome", WorkerResultSummaryConstants.Outcomes.PrCreated,
                "--pr", "999",
                "--write",
                "--format", "json",
            },
            writer));

        Assert.False(launcherInvoked,
            "WorkerCompleteCommand must never invoke NestedProviderLauncher.");
    }

    [Fact]
    public void Execute_DryRun_LeavesIntentCliWorkspaceByteEquivalent()
    {
        using var workspace = new WorkerCompleteWorkspace();
        var before = workspace.SnapshotWorkspace();

        WorkerCompleteCommand.MutatorFactory = () => new FakeMutator
        {
            Labels = new[] { "intent-target", "intent-issue-in-progress" },
        };

        using (var writer = new StringWriter())
        {
            Assert.Equal(0, WorkerCompleteCommand.Execute(workspace.Context,
                new[]
                {
                    "--repo", "J-Tech-Japan/intent-system",
                    "--kind", "issue",
                    "--number", "525",
                    "--outcome", WorkerResultSummaryConstants.Outcomes.PrCreated,
                    "--pr", "999",
                    "--format", "json",
                },
                writer));
        }

        var after = workspace.SnapshotWorkspace();
        Assert.Equal(before.Count, after.Count);
        foreach (var (path, hash) in before)
        {
            Assert.True(after.TryGetValue(path, out var afterHash));
            Assert.Equal(hash, afterHash);
        }
    }

    [Fact]
    public void SourceScan_AnalyzerAndCommand_ContainNoProcessStartOrGhMutationLiterals()
    {
        var analyzer = StripCsharpComments(File.ReadAllText(LocateSourceFile("WorkerCompleteAnalyzer.cs")));
        var command = StripCsharpComments(File.ReadAllText(LocateSourceFile("WorkerCompleteCommand.cs")));
        var combined = analyzer + "\n" + command;

        Assert.DoesNotContain("Process.Start(", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("gh issue edit", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("gh pr edit", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("gh pr merge", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("gh pr close", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("gh pr reopen", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("gh pr comment", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("gh pr review", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("resolveReviewThread", combined, StringComparison.Ordinal);
    }

    // ----- G311: closing-reference gate on `--kind issue --outcome pr-created` -----

    [Fact]
    public void Execute_G311_RefusesPrCreatedWhenPrBodyHasNoClosingReference()
    {
        using var workspace = new WorkerCompleteWorkspace();
        var mutator = new FakeMutator
        {
            Labels = new[] { "intent-target", "intent-issue-in-progress" },
        };
        WorkerCompleteCommand.MutatorFactory = () => mutator;
        // Override default permissive lookup with a body that lacks a closing reference.
        WorkerCompleteCommand.PrLookupFactory = () => new StubPrLookup
        {
            Body = "## Summary\n- Implement something. (no closing reference at all)",
            ClosingIssuesReferences = Array.Empty<GitHubPrClosingIssueReference>(),
        };

        using var writer = new StringWriter();
        var exitCode = WorkerCompleteCommand.Execute(
            workspace.Context,
            new[]
            {
                "--repo", "J-Tech-Japan/intent-system",
                "--kind", "issue",
                "--number", "725",
                "--outcome", WorkerResultSummaryConstants.Outcomes.PrCreated,
                "--pr", "724",
                "--write",
                "--format", "json",
            },
            writer);

        Assert.Equal(1, exitCode);
        var output = writer.ToString();
        Assert.Contains("refused to complete issue #725", output, StringComparison.Ordinal);
        Assert.Contains("Closes #725", output, StringComparison.Ordinal);
        // The mutator must NOT have been invoked — gate fires before any write.
        Assert.Empty(mutator.AppliedTransitions);
    }

    [Fact]
    public void Execute_G311_RefusesPrCreatedWhenPrBodyClosesWrongIssue()
    {
        using var workspace = new WorkerCompleteWorkspace();
        var mutator = new FakeMutator
        {
            Labels = new[] { "intent-target", "intent-issue-in-progress" },
        };
        WorkerCompleteCommand.MutatorFactory = () => mutator;
        WorkerCompleteCommand.PrLookupFactory = () => new StubPrLookup
        {
            Body = "Closes #999", // wrong issue
            ClosingIssuesReferences = Array.Empty<GitHubPrClosingIssueReference>(),
        };

        using var writer = new StringWriter();
        var exitCode = WorkerCompleteCommand.Execute(
            workspace.Context,
            new[]
            {
                "--repo", "J-Tech-Japan/intent-system",
                "--kind", "issue",
                "--number", "725",
                "--outcome", WorkerResultSummaryConstants.Outcomes.PrCreated,
                "--pr", "724",
                "--write",
                "--format", "json",
            },
            writer);

        Assert.Equal(1, exitCode);
        var output = writer.ToString();
        Assert.Contains("#999", output, StringComparison.Ordinal);
        Assert.Contains("#725", output, StringComparison.Ordinal);
        Assert.Empty(mutator.AppliedTransitions);
    }

    [Fact]
    public void Execute_G311_RefusesPrCreatedWhenPrBodyHasMultipleDistinctClosingReferences()
    {
        using var workspace = new WorkerCompleteWorkspace();
        var mutator = new FakeMutator
        {
            Labels = new[] { "intent-target", "intent-issue-in-progress" },
        };
        WorkerCompleteCommand.MutatorFactory = () => mutator;
        WorkerCompleteCommand.PrLookupFactory = () => new StubPrLookup
        {
            // Body text closes two different issues; GitHub-resolved
            // closingIssuesReferences left empty so the body parser runs.
            Body = "Closes #725 and Fixes #800.",
            ClosingIssuesReferences = Array.Empty<GitHubPrClosingIssueReference>(),
        };

        using var writer = new StringWriter();
        var exitCode = WorkerCompleteCommand.Execute(
            workspace.Context,
            new[]
            {
                "--repo", "J-Tech-Japan/intent-system",
                "--kind", "issue",
                "--number", "725",
                "--outcome", WorkerResultSummaryConstants.Outcomes.PrCreated,
                "--pr", "724",
                "--write",
                "--format", "json",
            },
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("multiple", writer.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Empty(mutator.AppliedTransitions);
    }

    [Fact]
    public void Execute_G311_AcceptsPrCreatedWhenGitHubClosingIssuesReferencesContainSourceIssue()
    {
        using var workspace = new WorkerCompleteWorkspace();
        var mutator = new FakeMutator
        {
            Labels = new[] { "intent-target", "intent-issue-in-progress" },
        };
        WorkerCompleteCommand.MutatorFactory = () => mutator;
        // The PR body wording is irrelevant — GitHub-resolved
        // closingIssuesReferences is the authoritative signal.
        WorkerCompleteCommand.PrLookupFactory = () => new StubPrLookup
        {
            Body = "Body without explicit Closes wording — GitHub still resolved the link.",
            ClosingIssuesReferences = new[]
            {
                new GitHubPrClosingIssueReference { Number = 725, Repository = null },
            },
        };

        using var writer = new StringWriter();
        var exitCode = WorkerCompleteCommand.Execute(
            workspace.Context,
            new[]
            {
                "--repo", "J-Tech-Japan/intent-system",
                "--kind", "issue",
                "--number", "725",
                "--outcome", WorkerResultSummaryConstants.Outcomes.PrCreated,
                "--pr", "724",
                "--write",
                "--format", "json",
            },
            writer);

        Assert.Equal(0, exitCode);
        // Mutator must have been called — the gate did NOT block the write.
        Assert.NotEmpty(mutator.AppliedTransitions);
    }

    [Fact]
    public void Execute_G311_DryRunAlsoEnforcesGate_NoMutation()
    {
        // Dry-run must report the same refusal so an automation operator
        // never thinks the completion plan is safe when it isn't.
        using var workspace = new WorkerCompleteWorkspace();
        var mutator = new FakeMutator
        {
            Labels = new[] { "intent-target", "intent-issue-in-progress" },
        };
        WorkerCompleteCommand.MutatorFactory = () => mutator;
        WorkerCompleteCommand.PrLookupFactory = () => new StubPrLookup
        {
            Body = "no closing reference here",
            ClosingIssuesReferences = Array.Empty<GitHubPrClosingIssueReference>(),
        };

        using var writer = new StringWriter();
        var exitCode = WorkerCompleteCommand.Execute(
            workspace.Context,
            new[]
            {
                "--repo", "J-Tech-Japan/intent-system",
                "--kind", "issue",
                "--number", "725",
                "--outcome", WorkerResultSummaryConstants.Outcomes.PrCreated,
                "--pr", "724",
                "--format", "json",
            },
            writer);

        Assert.Equal(1, exitCode);
        Assert.Empty(mutator.AppliedTransitions);
    }

    // ----- G347: base-branch gate on `--kind issue --outcome pr-created` -----

    [Fact]
    public void Execute_G347_RefusesPrCreatedWhenPrTargetsWrongBaseBranch()
    {
        // G347 AC: when the source issue body contains `## Base Branch Policy`
        // with `Expected PR base branch: \`main\`` and the PR targets `main-ai`,
        // the command must refuse and emit the G347 error message.
        using var workspace = new WorkerCompleteWorkspace();
        var mutator = new FakeMutator
        {
            Labels = new[] { "intent-target", "intent-issue-in-progress" },
        };
        WorkerCompleteCommand.MutatorFactory = () => mutator;

        // PR body has closing reference and correct base branch embedded,
        // but targets wrong base branch.
        WorkerCompleteCommand.PrLookupFactory = () => new StubPrLookup
        {
            Body = "Closes #797",
            ClosingIssuesReferences = new[]
            {
                new GitHubPrClosingIssueReference { Number = 797, Repository = null },
            },
            BaseRefName = "main-ai",    // WRONG — issue contract says `main`
        };
        WorkerCompleteCommand.IssueLookupFactory = () => new StubIssueLookup
        {
            Body = IssuePrepareCommandTests.CompleteValidBody,   // contains Base Branch Policy: direct-main / main
        };

        using var writer = new StringWriter();
        var exitCode = WorkerCompleteCommand.Execute(
            workspace.Context,
            new[]
            {
                "--repo", "J-Tech-Japan/intent-system",
                "--kind", "issue",
                "--number", "797",
                "--outcome", WorkerResultSummaryConstants.Outcomes.PrCreated,
                "--pr", "800",
            },
            writer);

        Assert.Equal(1, exitCode);
        var output = writer.ToString();
        Assert.Contains("G347", output, StringComparison.Ordinal);
        Assert.Contains("main-ai", output, StringComparison.Ordinal);
        Assert.Contains("main", output, StringComparison.Ordinal);
        Assert.Empty(mutator.AppliedTransitions);
    }

    [Fact]
    public void Execute_G347_AllowsPrCreatedWhenPrTargetsCorrectBaseBranch()
    {
        // G347 AC: when PR targets the same branch the issue contract mandates,
        // the completion must proceed normally.
        using var workspace = new WorkerCompleteWorkspace();
        var mutator = new FakeMutator
        {
            Labels = new[] { "intent-target", "intent-issue-in-progress" },
        };
        WorkerCompleteCommand.MutatorFactory = () => mutator;

        WorkerCompleteCommand.PrLookupFactory = () => new StubPrLookup
        {
            Body = "Closes #797",
            ClosingIssuesReferences = new[]
            {
                new GitHubPrClosingIssueReference { Number = 797, Repository = null },
            },
            BaseRefName = "main",    // CORRECT — issue contract says `main`
        };
        WorkerCompleteCommand.IssueLookupFactory = () => new StubIssueLookup
        {
            Body = IssuePrepareCommandTests.CompleteValidBody,
        };

        using var writer = new StringWriter();
        var exitCode = WorkerCompleteCommand.Execute(
            workspace.Context,
            new[]
            {
                "--repo", "J-Tech-Japan/intent-system",
                "--kind", "issue",
                "--number", "797",
                "--outcome", WorkerResultSummaryConstants.Outcomes.PrCreated,
                "--pr", "800",
                "--format", "json",
            },
            writer);

        // Dry-run succeeds (proceeds), no mutations.
        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<WorkerCompleteResult>(writer.ToString())!;
        Assert.True(result.Proceed);
    }

    [Fact]
    public void Execute_G347_AllowsPrCreatedWhenIssueBodyHasNoBaseBranchSection()
    {
        // G347 AC: when the issue body lacks the `## Base Branch Policy` section
        // the check is inconclusive and completion must be allowed (best-effort).
        using var workspace = new WorkerCompleteWorkspace();
        var mutator = new FakeMutator
        {
            Labels = new[] { "intent-target", "intent-issue-in-progress" },
        };
        WorkerCompleteCommand.MutatorFactory = () => mutator;

        WorkerCompleteCommand.PrLookupFactory = () => new StubPrLookup
        {
            Body = "Closes #797",
            ClosingIssuesReferences = new[]
            {
                new GitHubPrClosingIssueReference { Number = 797, Repository = null },
            },
            BaseRefName = "any-branch",
        };
        // Issue body without a Base Branch Policy section.
        WorkerCompleteCommand.IssueLookupFactory = () => new StubIssueLookup
        {
            Body = "## Goal\n\nSomething.\n\n## Verification\n\nRun tests.",
        };

        using var writer = new StringWriter();
        var exitCode = WorkerCompleteCommand.Execute(
            workspace.Context,
            new[]
            {
                "--repo", "J-Tech-Japan/intent-system",
                "--kind", "issue",
                "--number", "797",
                "--outcome", WorkerResultSummaryConstants.Outcomes.PrCreated,
                "--pr", "800",
                "--format", "json",
            },
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<WorkerCompleteResult>(writer.ToString())!;
        Assert.True(result.Proceed);
    }

    [Fact]
    public void Execute_G389_RefusesPrCreatedWhenIssueContractBundlesMultipleUnits()
    {
        // G389 AC: an issue whose body declares multiple execution-unit
        // identities (multiple H1 packets) must be refused as ambiguous-contract,
        // with no label mutation applied.
        using var workspace = new WorkerCompleteWorkspace();
        var mutator = new FakeMutator
        {
            Labels = new[] { "intent-target", "intent-issue-in-progress" },
        };
        WorkerCompleteCommand.MutatorFactory = () => mutator;
        WorkerCompleteCommand.PrLookupFactory = () => new StubPrLookup
        {
            Body = "Closes #797",
            ClosingIssuesReferences = new[]
            {
                new GitHubPrClosingIssueReference { Number = 797, Repository = null },
            },
        };
        WorkerCompleteCommand.IssueLookupFactory = () => new StubIssueLookup
        {
            Body = "# G390 First packet\n\n## Goal\nA.\n\n# G391 Second packet\n\n## Goal\nB.\n",
        };

        using var writer = new StringWriter();
        var exitCode = WorkerCompleteCommand.Execute(
            workspace.Context,
            new[]
            {
                "--repo", "J-Tech-Japan/intent-system",
                "--kind", "issue",
                "--number", "797",
                "--outcome", WorkerResultSummaryConstants.Outcomes.PrCreated,
                "--pr", "800",
                "--format", "json",
            },
            writer);

        Assert.NotEqual(0, exitCode);
        Assert.Contains("ambiguous-contract", writer.ToString(), StringComparison.Ordinal);
        Assert.Empty(mutator.AppliedTransitions);
    }

    [Fact]
    public void Execute_G347_AllowsPrCreatedWhenIssueLookupThrows()
    {
        // G347 AC: best-effort — when the issue lookup throws (network error,
        // auth issue, etc.) the command treats the check as inconclusive and
        // allows the completion.
        using var workspace = new WorkerCompleteWorkspace();
        var mutator = new FakeMutator
        {
            Labels = new[] { "intent-target", "intent-issue-in-progress" },
        };
        WorkerCompleteCommand.MutatorFactory = () => mutator;

        WorkerCompleteCommand.PrLookupFactory = () => new StubPrLookup
        {
            Body = "Closes #797",
            ClosingIssuesReferences = new[]
            {
                new GitHubPrClosingIssueReference { Number = 797, Repository = null },
            },
            BaseRefName = "wrong-base",
        };
        WorkerCompleteCommand.IssueLookupFactory = () => new ThrowingIssueLookup();

        using var writer = new StringWriter();
        var exitCode = WorkerCompleteCommand.Execute(
            workspace.Context,
            new[]
            {
                "--repo", "J-Tech-Japan/intent-system",
                "--kind", "issue",
                "--number", "797",
                "--outcome", WorkerResultSummaryConstants.Outcomes.PrCreated,
                "--pr", "800",
                "--format", "json",
            },
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<WorkerCompleteResult>(writer.ToString())!;
        Assert.True(result.Proceed);
    }

    // G347 unit tests for the standalone body-parser helper.

    [Theory]
    [InlineData("## Base Branch Policy\nPolicy: `direct-main`\nExpected PR base branch: `main`\n", "main")]
    [InlineData("## Base Branch Policy\nPolicy: `main-ai`\nExpected PR base branch: `main-ai`\n", "main-ai")]
    [InlineData("## Base Branch Policy\nExpected PR base branch: `main`\nPolicy: `direct-main`\n", "main")]
    [InlineData("## Other Heading\nExpected PR base branch: `main`\n", null)]  // not inside section
    [InlineData("", null)]
    [InlineData("  ", null)]
    public void ParseExpectedBaseBranchFromIssueBody_ParsesCorrectly(string body, string? expected)
    {
        var result = WorkerCompleteCommand.ParseExpectedBaseBranchFromIssueBody(body);
        Assert.Equal(expected, result);
    }

    private static string StripCsharpComments(string source)
    {
        var noBlockComments = System.Text.RegularExpressions.Regex.Replace(
            source, @"/\*[\s\S]*?\*/", string.Empty);
        var noLineComments = System.Text.RegularExpressions.Regex.Replace(
            noBlockComments, @"//.*?$", string.Empty,
            System.Text.RegularExpressions.RegexOptions.Multiline);
        return noLineComments;
    }

    private static string LocateSourceFile(string fileName)
    {
        var directory = AppContext.BaseDirectory;
        var dir = new DirectoryInfo(directory);
        while (dir is not null)
        {
            var candidate = Path.Combine(
                dir.FullName, "src", "IntentSystem.Cli", "Commands", fileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
            dir = dir.Parent;
        }
        throw new FileNotFoundException(
            $"Could not locate source file {fileName} from {directory}");
    }

    /// <summary>
    /// G311: default <see cref="IGitHubPrLookup"/> fake that satisfies the
    /// closing-reference gate for any issue number used by pre-existing
    /// tests. Returns a body that names both 514 and 525 in
    /// <c>closingIssuesReferences</c> with <c>Repository = null</c> so
    /// <c>IsSameRepo</c> accepts it. Tests that need to exercise the
    /// gate's failure paths inject a different fake via
    /// <see cref="WorkerCompleteCommand.PrLookupFactory"/>.
    /// </summary>
    internal sealed class PermissivePrLookup : IGitHubPrLookup
    {
        public GitHubPrLookupResult Lookup(string repo, int prNumber) => new()
        {
            Number = prNumber,
            State = "OPEN",
            Title = "permissive fake",
            Body = "Closes #525\nCloses #514",
            ClosingIssuesReferences = new[]
            {
                new GitHubPrClosingIssueReference { Number = 514, Repository = null },
                new GitHubPrClosingIssueReference { Number = 525, Repository = null },
            },
        };
    }

    /// <summary>
    /// G311: parametric <see cref="IGitHubPrLookup"/> fake. Tests inject
    /// this when they want to exercise the gate's failure paths or vary
    /// the body text and closing-issue references PR-by-PR.
    /// </summary>
    internal sealed class StubPrLookup : IGitHubPrLookup
    {
        public string Body { get; init; } = string.Empty;

        public Action? LookupCallback { get; init; }

        public IReadOnlyList<GitHubPrClosingIssueReference> ClosingIssuesReferences { get; init; }
            = Array.Empty<GitHubPrClosingIssueReference>();

        /// <summary>G347: actual base branch reported by the PR (empty = G347 check skipped).</summary>
        public string BaseRefName { get; init; } = string.Empty;

        public GitHubPrLookupResult Lookup(string repo, int prNumber)
        {
            LookupCallback?.Invoke();
            return new()
            {
                Number = prNumber,
                State = "OPEN",
                Title = "stub",
                Body = Body,
                ClosingIssuesReferences = ClosingIssuesReferences,
                BaseRefName = BaseRefName,
            };
        }
    }

    /// <summary>
    /// G347: parametric <see cref="IGitHubIssueLookup"/> fake for testing the
    /// base-branch policy section parser without shelling out to <c>gh</c>.
    /// </summary>
    internal sealed class StubIssueLookup : IGitHubIssueLookup
    {
        public string Body { get; init; } = string.Empty;

        public Action? LookupCallback { get; init; }

        public GitHubIssueLookupResult Lookup(string repo, int issueNumber)
        {
            LookupCallback?.Invoke();
            return new()
            {
                Number = issueNumber,
                State = "OPEN",
                Title = "stub issue",
                Body = Body,
            };
        }
    }

    /// <summary>
    /// G347: fake <see cref="IGitHubIssueLookup"/> that always throws so tests
    /// can verify the best-effort (inconclusive) path lets the completion proceed.
    /// </summary>
    internal sealed class ThrowingIssueLookup : IGitHubIssueLookup
    {
        public GitHubIssueLookupResult Lookup(string repo, int issueNumber) =>
            throw new InvalidOperationException("simulated gh issue view failure");
    }

    internal sealed class FakeMutator : IGitHubLabelMutator
    {
        public IReadOnlyList<string> Labels { get; init; } = Array.Empty<string>();

        public Action? ReadCallback { get; init; }

        public Action? ApplyCallback { get; init; }

        public List<AppliedTransition> AppliedTransitions { get; } = new();

        public IReadOnlyList<GitHubAutomationLabel> ReadLabels(string repo, string kind, int number)
        {
            ReadCallback?.Invoke();
            return Labels.Select(n => new GitHubAutomationLabel { Name = n }).ToArray();
        }

        public void ApplyLabelTransitions(
            string repo,
            string kind,
            int number,
            IReadOnlyCollection<string> addLabels,
            IReadOnlyCollection<string> removeLabels)
        {
            ApplyCallback?.Invoke();
            if (string.Equals(kind, GhCliGitHubLabelMutator.Kinds.Pr, StringComparison.Ordinal)
                && (addLabels.Contains("intent-pr-created", StringComparer.Ordinal)
                    || removeLabels.Contains("intent-pr-created", StringComparer.Ordinal)))
            {
                throw new InvalidOperationException(
                    "label policy violation: 'intent-pr-created' is issue-only.");
            }

            AppliedTransitions.Add(new AppliedTransition
            {
                Repo = repo,
                Kind = kind,
                Number = number,
                AddLabels = addLabels.ToArray(),
                RemoveLabels = removeLabels.ToArray(),
            });
        }

        public void ApplyReconcileTransitions(
            string repo,
            string kind,
            int number,
            IReadOnlyCollection<string> addLabels,
            IReadOnlyCollection<string> removeLabels) =>
            throw new NotSupportedException("reconcile path not exercised by these tests");
    }

    /// <summary>G283: variant that throws on PR-side intent-target writes so tests can
    /// assert the partial-failure path (issue swap applied, PR publish failed).</summary>
    internal sealed class FailingPrMutator : IGitHubLabelMutator
    {
        public IReadOnlyList<string> Labels { get; init; } = Array.Empty<string>();
        public Action? ApplyCallback { get; init; }
        public List<AppliedTransition> AppliedTransitions { get; } = new();

        public IReadOnlyList<GitHubAutomationLabel> ReadLabels(string repo, string kind, int number) =>
            Labels.Select(n => new GitHubAutomationLabel { Name = n }).ToArray();

        public void ApplyLabelTransitions(
            string repo,
            string kind,
            int number,
            IReadOnlyCollection<string> addLabels,
            IReadOnlyCollection<string> removeLabels)
        {
            ApplyCallback?.Invoke();
            if (string.Equals(kind, "pr", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("simulated PR-side mutator failure");
            }
            AppliedTransitions.Add(new AppliedTransition
            {
                Repo = repo,
                Kind = kind,
                Number = number,
                AddLabels = addLabels.ToArray(),
                RemoveLabels = removeLabels.ToArray(),
            });
        }

        public void ApplyReconcileTransitions(
            string repo,
            string kind,
            int number,
            IReadOnlyCollection<string> addLabels,
            IReadOnlyCollection<string> removeLabels) =>
            throw new NotSupportedException("reconcile path not exercised by these tests");
    }

    internal sealed record AppliedTransition
    {
        public required string Repo { get; init; }
        public required string Kind { get; init; }
        public required int Number { get; init; }
        public required IReadOnlyList<string> AddLabels { get; init; }
        public required IReadOnlyList<string> RemoveLabels { get; init; }
    }

    // --- G330: child worker flows are parent-host-state-free -----------------

    [Fact]
    public void Execute_G330_ChildCwdImplicit_IssueToPr_NeverTouchesParentState()
    {
        // G330 acceptance: a child worker completing an issue-to-pr
        // from a child worktree WITHOUT parent .intent-cli/ MUST:
        //  - apply GitHub label transitions (issue-side + PR-side)
        //  - report `child_cwd: true` (auto-detected — no parent root
        //    configured AND no local queue-state)
        //  - NOT touch any queue-state file
        //  - surface the G329 closeout-plan recovery command pointer
        //    so the operator/review-runtime can recover linkage host-side
        using var workspace = new WorkerCompleteWorkspace();
        // No parent intent repo root configured; no local queue-state.
        Assert.False(File.Exists(workspace.QueueStatePath));

        var mutator = new FakeMutator
        {
            Labels = new[] { "intent-target", "intent-issue-in-progress" },
        };
        WorkerCompleteCommand.MutatorFactory = () => mutator;

        using var writer = new StringWriter();
        var exitCode = WorkerCompleteCommand.Execute(
            workspace.Context,
            new[]
            {
                "--repo", "J-Tech-Japan/intent-system",
                "--kind", "issue",
                "--number", "525",
                "--outcome", WorkerResultSummaryConstants.Outcomes.PrCreated,
                "--pr", "764",
                "--write",
                "--format", "json",
            },
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<WorkerCompleteResult>(writer.ToString())!;
        Assert.True(result.Proceed);
        Assert.True(result.PrTargetApplied);
        Assert.False(result.LinkedPrSynced);
        Assert.True(result.ChildCwd);
        Assert.Contains(result.Warnings,
            w => w.Contains("child-cwd mode", StringComparison.Ordinal));
        Assert.Contains(result.Warnings,
            w => w.Contains("closeout-plan", StringComparison.Ordinal));
        // The "queue-state.json not found" warning MUST NOT fire in
        // child-cwd mode — that signal is for hosts with a real
        // parent-host-root configuration.
        Assert.DoesNotContain(result.Warnings,
            w => w.Contains("queue-state.json not found", StringComparison.Ordinal));

        // G330 review fix: the child-cwd warning must contain the
        // ACTUAL pr number and repo, not the literal `{0}` / `{1}`
        // placeholders. A child worker pasting the warning text into
        // a host shell must get a directly runnable command.
        Assert.Contains(result.Warnings,
            w => w.Contains("--pr 764", StringComparison.Ordinal)
                && w.Contains("--repo J-Tech-Japan/intent-system", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Warnings,
            w => w.Contains("{0}", StringComparison.Ordinal) || w.Contains("{1}", StringComparison.Ordinal));

        // No queue-state file was ever created by the writer.
        Assert.False(File.Exists(workspace.QueueStatePath));
    }

    [Fact]
    public void Execute_G330_ChildCwdExplicit_SkipsQueueStateEvenWhenLocalFileExists()
    {
        // G330 invariant: --child-cwd is a hard refuse-to-write
        // assertion. Even if a stray local `.intent-cli/queue-state.json`
        // exists in the child worktree (which violates G300), the
        // command must NOT touch it.
        using var workspace = new WorkerCompleteWorkspace();
        // Seed a local queue-state on the child workspace — a G300
        // violation, but the test guards against accidentally writing
        // to it.
        workspace.SeedQueueStateWithLinkedIssue(
            executionUnit: "G330",
            title: "G330 unit",
            sourceIssueNumber: 525,
            existingLinkedPr: null);
        var queueBefore = File.ReadAllText(workspace.QueueStatePath);

        var mutator = new FakeMutator
        {
            Labels = new[] { "intent-target", "intent-issue-in-progress" },
        };
        WorkerCompleteCommand.MutatorFactory = () => mutator;

        using var writer = new StringWriter();
        var exitCode = WorkerCompleteCommand.Execute(
            workspace.Context,
            new[]
            {
                "--repo", "J-Tech-Japan/intent-system",
                "--kind", "issue",
                "--number", "525",
                "--outcome", WorkerResultSummaryConstants.Outcomes.PrCreated,
                "--pr", "764",
                "--child-cwd",
                "--write",
                "--format", "json",
            },
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<WorkerCompleteResult>(writer.ToString())!;
        Assert.True(result.Proceed);
        Assert.True(result.ChildCwd);
        Assert.False(result.LinkedPrSynced);

        // Local queue-state must be byte-identical to before — even
        // though it existed on disk, --child-cwd refused to touch it.
        Assert.Equal(queueBefore, File.ReadAllText(workspace.QueueStatePath));
    }

    [Fact]
    public void Execute_G330_ChildCwd_PrCommentFix_MovesToRereviewReadyWithoutHostState()
    {
        // G330 acceptance: child PR-comment-fix completion (repair-pushed)
        // moves the PR to rereview-ready via GitHub labels only —
        // no parent durable state involved at all. linked_pr_synced is
        // null because that field is issue-to-pr-specific.
        using var workspace = new WorkerCompleteWorkspace();
        Assert.False(File.Exists(workspace.QueueStatePath));

        var mutator = new FakeMutator
        {
            Labels = new[] { "intent-target", "intent-pr-update-in-progress", "intent-pr-request-update" },
        };
        WorkerCompleteCommand.MutatorFactory = () => mutator;

        using var writer = new StringWriter();
        var exitCode = WorkerCompleteCommand.Execute(
            workspace.Context,
            new[]
            {
                "--repo", "J-Tech-Japan/intent-system",
                "--kind", "pr",
                "--number", "762",
                "--outcome", WorkerResultSummaryConstants.Outcomes.RepairPushed,
                "--write",
                "--format", "json",
            },
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<WorkerCompleteResult>(writer.ToString())!;
        Assert.True(result.Proceed);
        Assert.True(result.Applied);
        Assert.True(result.ChildCwd,
            "pr-comment-fix completion from a child cwd must auto-detect child_cwd=true.");
        Assert.Contains("intent-pr-rereview-ready", result.AddLabels);
        Assert.DoesNotContain(result.Warnings,
            w => w.Contains("queue-state.json not found", StringComparison.Ordinal));
        // No queue-state file ever created.
        Assert.False(File.Exists(workspace.QueueStatePath));
    }

    [Fact]
    public void Execute_G330_HostContextWithParentRootSeeded_ChildCwdFalseAndQueueStateWritten()
    {
        // G330 invariant: hosts with a real parent root + seeded
        // queue-state must NOT auto-enter child-cwd mode. The writer
        // continues its pre-G330 host behavior: linked_pr is synced
        // and `child_cwd` is false.
        using var parent = new WorkerCompleteWorkspace();
        parent.SeedQueueStateWithLinkedIssue(
            executionUnit: "G283",
            title: "host packet",
            sourceIssueNumber: 525,
            existingLinkedPr: null);
        using var workspace = new WorkerCompleteWorkspace(parentIntentRepoRoot: parent.RootPath);

        var mutator = new FakeMutator
        {
            Labels = new[] { "intent-target", "intent-issue-in-progress" },
        };
        WorkerCompleteCommand.MutatorFactory = () => mutator;

        using var writer = new StringWriter();
        var exitCode = WorkerCompleteCommand.Execute(
            workspace.Context,
            new[]
            {
                "--repo", "J-Tech-Japan/intent-system",
                "--kind", "issue",
                "--number", "525",
                "--outcome", WorkerResultSummaryConstants.Outcomes.PrCreated,
                "--pr", "999",
                "--write",
                "--format", "json",
            },
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<WorkerCompleteResult>(writer.ToString())!;
        Assert.True(result.Proceed);
        Assert.False(result.ChildCwd);
        Assert.True(result.LinkedPrSynced);
        // The parent queue-state actually got the linked_pr URL.
        var parentQueue = QueueStateSerializer.Deserialize(File.ReadAllText(parent.QueueStatePath));
        var matched = parentQueue.Items.Single(i => i.ExecutionUnit == "G283");
        Assert.Equal("https://github.com/J-Tech-Japan/intent-system/pull/999", matched.LinkedPr);
    }

    // --- G333: --github-only strict child-loop assertion ---------------------

    [Fact]
    public void Execute_G333_GithubOnly_ImpliesChildCwdAndRefusesQueueStateMutation()
    {
        // G333 acceptance: `--github-only` is the strict assertion used
        // by the Claude/Codex child implementation loop. It implies
        // --child-cwd (so the writer never touches parent durable
        // state), and the result records `github_only: true` so the
        // host loop can audit the binding.
        //
        // Seed BOTH a host parent root AND a local queue-state to prove
        // --github-only refuses both: even when the writer could find
        // a parent file, it must not write.
        using var parent = new WorkerCompleteWorkspace();
        parent.SeedQueueStateWithLinkedIssue(
            executionUnit: "G333",
            title: "host packet",
            sourceIssueNumber: 525,
            existingLinkedPr: null);
        using var workspace = new WorkerCompleteWorkspace(parentIntentRepoRoot: parent.RootPath);
        workspace.SeedQueueStateWithLinkedIssue(
            executionUnit: "G333-LOCAL",
            title: "stray local file",
            sourceIssueNumber: 525,
            existingLinkedPr: null);
        var parentBefore = File.ReadAllText(parent.QueueStatePath);
        var localBefore = File.ReadAllText(workspace.QueueStatePath);

        var mutator = new FakeMutator
        {
            Labels = new[] { "intent-target", "intent-issue-in-progress" },
        };
        WorkerCompleteCommand.MutatorFactory = () => mutator;

        using var writer = new StringWriter();
        var exit = WorkerCompleteCommand.Execute(
            workspace.Context,
            new[]
            {
                "--repo", "J-Tech-Japan/intent-system",
                "--kind", "issue",
                "--number", "525",
                "--outcome", WorkerResultSummaryConstants.Outcomes.PrCreated,
                "--pr", "999",
                "--github-only",
                "--write",
                "--format", "json",
            },
            writer);

        Assert.Equal(0, exit);
        var result = JsonSerializer.Deserialize<WorkerCompleteResult>(writer.ToString())!;
        Assert.True(result.Proceed);
        Assert.True(result.PrTargetApplied);
        Assert.False(result.LinkedPrSynced);
        // --github-only implies --child-cwd.
        Assert.True(result.ChildCwd);
        Assert.True(result.GithubOnly);

        // Both queue-state files are byte-identical.
        Assert.Equal(parentBefore, File.ReadAllText(parent.QueueStatePath));
        Assert.Equal(localBefore, File.ReadAllText(workspace.QueueStatePath));
    }

    [Fact]
    public void Execute_G333_WithoutGithubOnly_PreservesPriorBehavior_GithubOnlyFalseOnResult()
    {
        // G333 invariant: callers that don't pass --github-only keep
        // their pre-G333 result shape. The `github_only` field is
        // explicitly false on host/review-runtime invocations so the
        // host loop can tell the strict child-loop binding from the
        // host-context binding.
        using var parent = new WorkerCompleteWorkspace();
        parent.SeedQueueStateWithLinkedIssue(
            executionUnit: "G333",
            title: "host packet",
            sourceIssueNumber: 525,
            existingLinkedPr: null);
        using var workspace = new WorkerCompleteWorkspace(parentIntentRepoRoot: parent.RootPath);

        var mutator = new FakeMutator
        {
            Labels = new[] { "intent-target", "intent-issue-in-progress" },
        };
        WorkerCompleteCommand.MutatorFactory = () => mutator;

        using var writer = new StringWriter();
        var exit = WorkerCompleteCommand.Execute(
            workspace.Context,
            new[]
            {
                "--repo", "J-Tech-Japan/intent-system",
                "--kind", "issue",
                "--number", "525",
                "--outcome", WorkerResultSummaryConstants.Outcomes.PrCreated,
                "--pr", "999",
                "--write",
                "--format", "json",
            },
            writer);

        Assert.Equal(0, exit);
        var result = JsonSerializer.Deserialize<WorkerCompleteResult>(writer.ToString())!;
        Assert.False(result.GithubOnly);
        // The host-context write actually synced linked_pr (pre-G333
        // behavior preserved).
        Assert.True(result.LinkedPrSynced);
    }

    private sealed class WorkerCompleteWorkspace : IDisposable
    {
        public WorkerCompleteWorkspace(string parentIntentRepoRoot = "")
        {
            RootPath = Directory.CreateTempSubdirectory("worker-complete-tests-").FullName;
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
                        ParentIntentRepoRoot = parentIntentRepoRoot
                    }
                }
            };
        }

        public string RootPath { get; }
        public CliContext Context { get; }

        public string QueueStatePath => Path.Combine(RootPath, ".intent-cli", "queue-state.json");

        public void RecordSessionLayer(string domain, string team, string mode)
        {
            using var writer = new StringWriter();
            var exitCode = SessionLayerCommand.ExecuteSet(Context,
                ["--domain", domain, "--team", team, "--mode", mode, "--write", "--format", "json"], writer);
            Assert.Equal(0, exitCode);
        }

        public string WriteStartupMarker(string content)
        {
            var path = Path.Combine(RootPath, "CLAUDE.md");
            File.WriteAllText(path, content);
            return path;
        }

        public void GenerateSessionLayerMarker(string domain, string team, string path)
        {
            using var writer = new StringWriter();
            var exitCode = SessionLayerMarkerCommand.Execute(Context,
                ["generate", "--domain", domain, "--team", team, "--file", path, "--write", "--format", "json"], writer);
            Assert.Equal(0, exitCode);
        }

        /// <summary>G283: seed queue-state.json with a single execution-unit row whose
        /// linked_issue.number matches the source issue, optionally pre-filling linked_pr
        /// (for the idempotent rerun test).</summary>
        public void SeedQueueStateWithLinkedIssue(
            string executionUnit,
            string title,
            int sourceIssueNumber,
            string? existingLinkedPr = null)
        {
            var state = new QueueState
            {
                SchemaVersion = "1",
                UpdatedAt = new DateTimeOffset(2026, 5, 7, 0, 0, 0, TimeSpan.Zero),
                Items =
                [
                    new QueueItem
                    {
                        ExecutionUnit = executionUnit,
                        Title = title,
                        State = QueueItemState.Queued,
                        Dependencies = Array.Empty<string>(),
                        BlockedBy = Array.Empty<string>(),
                        ClarificationReturnPath = "intents/intent-cli/clarifications/open.md",
                        PacketPaths = new PacketPaths
                        {
                            Implementation = $".intent-cli/issues/{executionUnit}/implementation.md",
                            ReviewContext = $".intent-cli/issues/{executionUnit}/review-context.md",
                            Yaml = $".intent-cli/issues/{executionUnit}/packet.yaml",
                        },
                        LinkedIssue = new LinkedIssue
                        {
                            Repo = "J-Tech-Japan/intent-system",
                            Number = sourceIssueNumber,
                            Url = $"https://github.com/J-Tech-Japan/intent-system/issues/{sourceIssueNumber}",
                        },
                        LinkedPr = existingLinkedPr,
                        WorkerRole = "child-impl",
                        ReviewRole = "host-review",
                        Priority = "normal",
                    }
                ]
            };
            File.WriteAllText(QueueStatePath, QueueStateSerializer.Serialize(state));
        }

        public QueueItem CreateItem(string executionUnit, string repo, int issue, string domain, string? existingLinkedPr = null) =>
            new()
            {
                ExecutionUnit = executionUnit,
                Title = executionUnit,
                State = QueueItemState.Queued,
                Dependencies = Array.Empty<string>(),
                BlockedBy = Array.Empty<string>(),
                ClarificationReturnPath = $"intents/{domain}/clarifications/open.md",
                PacketPaths = new PacketPaths { Implementation = "i", ReviewContext = "r", Yaml = "p" },
                LinkedIssue = new LinkedIssue { Repo = repo, Number = issue, Url = $"https://github.com/{repo}/issues/{issue}" },
                LinkedPr = existingLinkedPr,
                WorkerRole = "child-impl",
                ReviewRole = "host-review",
                Priority = "normal",
            };

        public QueueItem CreateItemWithoutDomain(string executionUnit, string repo, int issue) =>
            new()
            {
                ExecutionUnit = executionUnit,
                Title = executionUnit,
                State = QueueItemState.Queued,
                Dependencies = Array.Empty<string>(),
                BlockedBy = Array.Empty<string>(),
                ClarificationReturnPath = string.Empty,
                PacketPaths = new PacketPaths { Implementation = "i", ReviewContext = "r", Yaml = "p" },
                LinkedIssue = new LinkedIssue { Repo = repo, Number = issue, Url = $"https://github.com/{repo}/issues/{issue}" },
                LinkedPr = null,
                WorkerRole = "child-impl",
                ReviewRole = "host-review",
                Priority = "normal",
            };

        public void WriteQueueState(params QueueItem[] items) => File.WriteAllText(QueueStatePath,
            QueueStateSerializer.Serialize(new QueueState
            {
                SchemaVersion = "1",
                UpdatedAt = DateTimeOffset.UtcNow,
                Items = items,
            }));

        public IReadOnlyDictionary<string, string> SnapshotWorkspace()
        {
            var snapshot = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var path in Directory.EnumerateFiles(RootPath, "*", SearchOption.AllDirectories))
            {
                var bytes = File.ReadAllBytes(path);
                var hash = Convert.ToHexString(SHA256.HashData(bytes));
                snapshot[path] = hash;
            }
            return snapshot;
        }

        public void Dispose()
        {
            if (Directory.Exists(RootPath))
            {
                Directory.Delete(RootPath, recursive: true);
            }
        }
    }

    private static byte[] QueueItemBytes(string queueStatePath, int index)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(queueStatePath));
        return System.Text.Encoding.UTF8.GetBytes(
            document.RootElement.GetProperty("items")[index].GetRawText());
    }
}
