using System.Text;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

public sealed class VerificationEvidenceCoverageTests
{
    private const string Unit = "G867";
    private const string Domain = "intent-cli";
    private const string Team = "intent-cli-dev";
    private const string Repo = "J-Tech-Japan/intent-system";
    private const string Head = VerificationEvidenceCommandTests.Head;

    private const string Packet = """
        implementation_issue_packet:
          source_execution_unit: G867
          domain: intent-cli
          team: intent-cli-dev
          target_repo: J-Tech-Japan/intent-system
          acceptance_criteria:
            - Preserve independently ordered criterion one.
            - Preserve independently ordered criterion two.
        verification_contract:
          version: 1
          acceptance_criteria:
            - {id: AC1, criterion_index: 1}
            - {id: AC2, criterion_index: 2}
          lanes:
            - {id: unit, weight: ordinary}
            - {id: build, weight: heavy}
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

    private const string SingleProofPacket = """
        implementation_issue_packet:
          source_execution_unit: G867
          domain: intent-cli
          team: intent-cli-dev
          target_repo: J-Tech-Japan/intent-system
          acceptance_criteria:
            - Preserve the selected criterion in a fresh clone.
        verification_contract:
          version: 1
          acceptance_criteria:
            - {id: AC1, criterion_index: 1}
          lanes:
            - {id: unit, weight: ordinary}
          affected_lanes: [unit]
          proofs:
            - id: P1
              acceptance_criteria: [AC1]
              lane: unit
              command: dotnet test
              evidence: local-required
        """;

    [Fact]
    public void ActualWriterCoverageAndSummaryProjectionPreserveLegacyOptOut()
    {
        using var host = new VerificationEvidenceCommandTests.TempHost(Unit, Packet);
        var trx = VerificationEvidenceCommandTests.ReadSmallVstest();
        host.WriteTrx("capture.trx", trx);
        var packetSha = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(host.PacketBytes)).ToLowerInvariant();
        var baseSummaryArgs = new[]
        {
            "--kind", "issue-to-pr", "--repo", Repo, "--issue", "1895", "--pr", "1900",
            "--outcome", "pr-created", "--format", "json",
        };
        var context = new CliContext
        {
            RepoRoot = host.Root,
            Config = new CliConfig { Project = new ProjectConfig { Domain = Domain, ArtifactRoot = ".intent-cli" } },
        };

        var legacyBefore = RunSummary(context, baseSummaryArgs);
        Assert.Equal(0, legacyBefore.Exit);
        Assert.DoesNotContain("verification_coverage", legacyBefore.Output, StringComparison.Ordinal);

        var artifactFacts = new Dictionary<string, (string Path, string Sha256)>(StringComparer.Ordinal);
        foreach (var proof in new[] { ("P1", "E1"), ("P2", "E2") })
        {
            var writeArgs = VerificationEvidenceCommandTests.RecordArgs(host, "json", packetSha, "capture.trx",
                id: proof.Item2, unit: Unit, proof: proof.Item1);
            var (writeExit, writeText) = VerificationEvidenceCommandTests.Invoke(host, [.. writeArgs, "--write"]);
            using var writeJson = JsonDocument.Parse(writeText);
            Assert.Equal(0, writeExit);
            Assert.Equal("recorded", writeJson.RootElement.GetProperty("disposition").GetString());
            Assert.True(writeJson.RootElement.GetProperty("wrote").GetBoolean());
            var artifactPath = writeJson.RootElement.GetProperty("artifact_path").GetString()!;
            var artifactBytes = File.ReadAllBytes(host.Absolute(artifactPath));
            artifactFacts.Add(proof.Item1, (artifactPath, VerificationEvidenceCommandTests.Hash(artifactBytes)));
        }

        var references = new[] { "--evidence", "P2=E2", "--evidence", "P1=E1" };
        var coverageArgs = CoverageArgs(host, packetSha, references);
        string? directJsonText = null;
        foreach (var format in new[] { "json", "markdown" })
        {
            var direct = RunCoverage(host.Root, coverageArgs, format);
            Assert.True(direct.Exit == 0, direct.Output);
            var directReport = ParseCoverage(direct.Output, format);
            directJsonText ??= JsonSerializer.Serialize(directReport);
            Assert.Equal(directJsonText, JsonSerializer.Serialize(directReport));
            Assert.Equal("complete", Text(directReport, "state"));
            Assert.Equal("verification-coverage-complete", Text(directReport, "cause"));
            Assert.Equal("not-observed", Text(directReport, "execution"));
            Assert.Equal("not-authenticated", Text(directReport, "producer_identity"));
            Assert.Equal(new[] { "P1", "P2" }, directReport.GetProperty("proofs").EnumerateArray()
                .Select(row => Text(row, "proof_id")).ToArray());
            foreach (var expected in new[]
                     {
                         (Proof: "P1", Ac: "AC1", Index: 1, Text: "Preserve independently ordered criterion one.", Lane: "unit", Weight: "ordinary"),
                         (Proof: "P2", Ac: "AC2", Index: 2, Text: "Preserve independently ordered criterion two.", Lane: "build", Weight: "heavy"),
                     })
            {
                var row = directReport.GetProperty("proofs").EnumerateArray()
                    .Single(item => Text(item, "proof_id") == expected.Proof);
                Assert.Equal("present", Text(row, "state"));
                Assert.Equal("current", Text(row, "binding_state"));
                Assert.Equal(expected.Ac, Text(row.GetProperty("acceptance_criteria")[0], "id"));
                Assert.Equal(expected.Index, row.GetProperty("acceptance_criteria")[0].GetProperty("criterion_index").GetInt32());
                Assert.Equal(expected.Text, Text(row.GetProperty("acceptance_criteria")[0], "text"));
                Assert.Equal(expected.Lane, Text(row, "lane_id"));
                Assert.Equal(expected.Weight, Text(row, "lane_weight"));
                Assert.True(row.GetProperty("lane_affected").GetBoolean());
                Assert.Equal("dotnet test", Text(row, "command"));
                Assert.Equal("local-required", Text(row, "evidence"));
                Assert.Equal(artifactFacts[expected.Proof].Path, Text(row, "artifact_path"));
                Assert.Equal(artifactFacts[expected.Proof].Sha256, Text(row, "artifact_sha256"));
                Assert.Equal(VerificationEvidenceCommandTests.Hash(trx), Text(row, "trx_sha256"));
                Assert.Equal(2, row.GetProperty("report").GetProperty("total").GetInt32());
                Assert.False(row.TryGetProperty("trx_base64", out _));
            }
            Assert.Equal(new[] { "P1", "P2" }, directReport.GetProperty("present_proof_ids").EnumerateArray()
                .Select(value => value.GetString()).ToArray());
            Assert.Empty(directReport.GetProperty("missing_proof_ids").EnumerateArray());
            if (format == "markdown") Assert.Contains("# Verification evidence coverage", direct.Output, StringComparison.Ordinal);
        }

        // Both packet proofs use the same command, so a command-only matcher
        // would incorrectly let either record satisfy the other proof. The
        // full proof binding keeps both rows historical when swapped.
        var swapped = RunCoverage(host.Root, CoverageArgs(host, packetSha,
            ["--evidence", "P1=E2", "--evidence", "P2=E1"]));
        Assert.Equal(1, swapped.Exit);
        using (var swappedJson = JsonDocument.Parse(swapped.Output))
        {
            var swappedRows = swappedJson.RootElement.GetProperty("proofs").EnumerateArray().ToArray();
            Assert.All(swappedRows, row =>
            {
                Assert.Equal("dotnet test", row.GetProperty("command").GetString());
                Assert.Equal("ineligible", row.GetProperty("state").GetString());
                Assert.Equal("verification-coverage-stale-binding", row.GetProperty("cause").GetString());
                Assert.Equal("stale", row.GetProperty("binding_state").GetString());
                Assert.NotNull(row.GetProperty("artifact_path").GetString());
                Assert.NotNull(row.GetProperty("artifact_sha256").GetString());
                Assert.NotNull(row.GetProperty("trx_sha256").GetString());
                Assert.Equal(JsonValueKind.Object, row.GetProperty("report").ValueKind);
            });
            Assert.Equal(new[] { "P1", "P2" }, swappedJson.RootElement.GetProperty("ineligible_proof_ids")
                .EnumerateArray().Select(item => item.GetString()).ToArray());
        }

        var repeated = RunCoverage(host.Root, coverageArgs);
        Assert.Equal(directJsonText, JsonSerializer.Serialize(ParseCoverage(repeated.Output, "json")));

        var summaryArgs = new[]
        {
            "--kind", "issue-to-pr", "--repo", Repo, "--issue", "1895", "--pr", "1900",
            "--outcome", "pr-created", "--verification-report",
            "--verification-unit", Unit, "--verification-domain", Domain, "--verification-team", Team,
            "--verification-head-sha", Head, "--verification-packet-sha256", packetSha,
            "--verification-evidence", "P2=E2", "--verification-evidence", "P1=E1", "--format", "json",
        };
        var summary = RunSummary(context, summaryArgs);
        Assert.Equal(0, summary.Exit);
        using var summaryJson = JsonDocument.Parse(summary.Output);
        Assert.True(summaryJson.RootElement.TryGetProperty("verification_coverage", out var projected));
        Assert.Equal("complete", projected.GetProperty("state").GetString());
        Assert.Equal("completed", summaryJson.RootElement.GetProperty("status").GetString());
        Assert.Equal("pr-created", summaryJson.RootElement.GetProperty("outcome").GetString());
        Assert.Equal(0, WorkerResultSummaryCommand.Execute(context, baseSummaryArgs, new StringWriter()));
        Assert.Equal(legacyBefore.Output, RunSummary(context, baseSummaryArgs).Output);

        var markdownSummary = RunSummary(context, [.. summaryArgs[..^2], "--format", "markdown"]);
        Assert.Equal(0, markdownSummary.Exit);
        Assert.Contains("## Verification evidence coverage", markdownSummary.Output, StringComparison.Ordinal);
        Assert.Contains("publication: not-verified", markdownSummary.Output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("json")]
    [InlineData("markdown")]
    public void G869DesignSelectorDoesNotChangeCoverageEligibility(string format)
    {
        var selectedPacket = Packet + "\n  design_review:\n    safety_critical_ac: AC1\n    proof_id: P1\n";
        using (var host = new VerificationEvidenceCommandTests.TempHost(Unit, selectedPacket))
        {
            var trx = VerificationEvidenceCommandTests.ReadSmallVstest();
            host.WriteTrx("capture.trx", trx);
            var packetSha = VerificationEvidenceCommandTests.Hash(host.PacketBytes);
            var (writeExit, writeOutput) = VerificationEvidenceCommandTests.Invoke(host,
                [.. VerificationEvidenceCommandTests.RecordArgs(host, "json", packetSha, "capture.trx",
                    id: "E1", unit: Unit, proof: "P1"), "--write"]);
            Assert.Equal(0, writeExit);
            using (var write = JsonDocument.Parse(writeOutput))
            {
                Assert.Equal("recorded", Text(write.RootElement, "disposition"));
                Assert.False(write.RootElement.TryGetProperty("design_review", out _));
            }
            var (secondWriteExit, secondWriteOutput) = VerificationEvidenceCommandTests.Invoke(host,
                [.. VerificationEvidenceCommandTests.RecordArgs(host, "json", packetSha, "capture.trx",
                    id: "E2", unit: Unit, proof: "P2"), "--write"]);
            Assert.Equal(0, secondWriteExit);
            using (var secondWrite = JsonDocument.Parse(secondWriteOutput))
            {
                Assert.Equal("recorded", Text(secondWrite.RootElement, "disposition"));
                Assert.False(secondWrite.RootElement.TryGetProperty("design_review", out _));
            }

            var reads = new List<string>();
            var coverage = RunCoverage(host.Root,
                CoverageArgs(host, packetSha, ["--evidence", "P1=E1", "--evidence", "P2=E2"]), format,
                (operation, _) => reads.Add(operation));
            var report = ParseCoverage(coverage.Output, format);
            Assert.Equal(0, coverage.Exit);
            Assert.Equal("complete", Text(report, "structural_state"));
            Assert.Equal("complete", Text(report, "state"));
            Assert.Equal(new[] { "P1", "P2" }, report.GetProperty("present_proof_ids").EnumerateArray()
                .Select(value => value.GetString()).ToArray());
            Assert.Contains("packet-read", reads);
            Assert.Contains("stored-read", reads);
        }

        var invalidPacket = selectedPacket.Replace("proof_id: P1", "proof_id: P9", StringComparison.Ordinal);
        Assert.NotEqual(selectedPacket, invalidPacket);
        Assert.Equal("invalid", PacketVerificationContract.Evaluate(Unit,
            $".intent-cli/issues/{Unit}/packet.yaml", Encoding.UTF8.GetBytes(invalidPacket)).State);
        using (var host = new VerificationEvidenceCommandTests.TempHost(Unit, invalidPacket))
        {
            var reads = new List<string>();
            var coverage = RunCoverage(host.Root,
                CoverageArgs(host, VerificationEvidenceCommandTests.Hash(host.PacketBytes), []), format,
                (operation, _) => reads.Add(operation));
            var report = ParseCoverage(coverage.Output, format);
            Assert.Equal(1, coverage.Exit);
            Assert.Equal("unavailable", Text(report, "state"));
            Assert.Equal("verification-coverage-packet-unavailable", Text(report, "cause"));
            Assert.Empty(report.GetProperty("proofs").EnumerateArray());
            Assert.Equal(new[] { "packet-read" }, reads);
        }
    }

    [Theory]
    [InlineData("json")]
    [InlineData("markdown")]
    public void StaleHeadAndPacketBindingsRetainOnlyVerifiedHistoricalFacts(string format)
    {
        using var host = new VerificationEvidenceCommandTests.TempHost(Unit, Packet);
        var trx = VerificationEvidenceCommandTests.ReadSmallVstest();
        host.WriteTrx("capture.trx", trx);
        var packetSha = VerificationEvidenceCommandTests.Hash(host.PacketBytes);
        var (writeExit, writeOutput) = VerificationEvidenceCommandTests.Invoke(host,
            [.. VerificationEvidenceCommandTests.RecordArgs(host, format, packetSha, "capture.trx", id: "E1", unit: Unit, proof: "P1"), "--write"]);
        Assert.Equal(0, writeExit);
        var stored = VerificationEvidenceCommandTests.Result(writeOutput, format);
        var artifactPath = VerificationEvidenceCommandTests.Text(stored, "artifact_path");
        var artifactBytes = File.ReadAllBytes(host.Absolute(artifactPath));
        var artifactSha = VerificationEvidenceCommandTests.Hash(artifactBytes);

        foreach (var (head, initialPacketSha) in new[]
                 {
                     (new string('a', 40), packetSha),
                     (Head, ""),
                 })
        {
            var currentPacketSha = initialPacketSha;
            if (currentPacketSha.Length == 0)
            {
                var changedPacket = Encoding.UTF8.GetBytes(Packet + "\n# caller packet bytes changed\n");
                host.WritePacket(changedPacket);
                currentPacketSha = VerificationEvidenceCommandTests.Hash(changedPacket);
            }

            var args = CoverageArgs(host, currentPacketSha, ["--evidence", "P1=E1"]);
            var headIndex = Array.IndexOf(args, "--head-sha") + 1;
            args[headIndex] = head;
            args[^1] = format;
            var (exit, output) = RunCoverage(host.Root, args, format);
            var report = ParseCoverage(output, format);
            Assert.Equal(1, exit);
            Assert.Equal("incomplete", Text(report, "state"));
            var row = report.GetProperty("proofs")[0];
            Assert.Equal("ineligible", Text(row, "state"));
            Assert.Equal("verification-coverage-stale-binding", Text(row, "cause"));
            Assert.Equal("stale", Text(row, "binding_state"));
            Assert.Equal("E1", Text(row, "record_id"));
            Assert.Equal(artifactPath, Text(row, "artifact_path"));
            Assert.Equal(artifactSha, Text(row, "artifact_sha256"));
            Assert.Equal(VerificationEvidenceCommandTests.Hash(trx), Text(row, "trx_sha256"));
            Assert.Equal(2, row.GetProperty("report").GetProperty("total").GetInt32());
            Assert.Equal(artifactBytes, File.ReadAllBytes(host.Absolute(artifactPath)));
        }
    }

    [Theory]
    [InlineData("criterion-index")]
    [InlineData("proof-reference")]
    [InlineData("proof-lane")]
    [InlineData("proof-command")]
    [InlineData("criterion-text")]
    [InlineData("lane-weight")]
    public void SamePacketDigestWithDifferentTypedBindingIsStale(string mutation)
    {
        using var host = new VerificationEvidenceCommandTests.TempHost(Unit, Packet);
        var trx = VerificationEvidenceCommandTests.ReadSmallVstest();
        host.WriteTrx("capture.trx", trx);
        var originalPacketSha = VerificationEvidenceCommandTests.Hash(host.PacketBytes);
        var (writeExit, writeOutput) = VerificationEvidenceCommandTests.Invoke(host,
            [.. VerificationEvidenceCommandTests.RecordArgs(host, "json", originalPacketSha, "capture.trx", id: "E1", unit: Unit, proof: "P1"), "--write"]);
        Assert.Equal(0, writeExit);
        var storedResult = VerificationEvidenceCommandTests.Result(writeOutput, "json");
        var storedPath = host.Absolute(VerificationEvidenceCommandTests.Text(storedResult, "artifact_path"));
        var storedBytes = File.ReadAllBytes(storedPath);
        var storedRecord = JsonSerializer.Deserialize<VerificationEvidenceRecord>(storedBytes)!;

        var alternatePacket = AlternatePacket(mutation);
        Assert.NotEqual(Packet, Encoding.UTF8.GetString(alternatePacket).TrimEnd('\n'));
        var structural = PacketVerificationContract.Evaluate(Unit, $".intent-cli/issues/{Unit}/packet.yaml", alternatePacket);
        Assert.Equal("complete", structural.State);
        host.WritePacket(alternatePacket);
        var alternatePacketSha = VerificationEvidenceCommandTests.Hash(alternatePacket);
        // Keep the record canonical and its packet digest current while
        // preserving the actual writer's original typed binding. This
        // isolates the strict AC/index/text/lane/command comparison.
        File.WriteAllBytes(storedPath, VerificationEvidenceStore.Serialize(storedRecord with
        {
            PacketSha256 = alternatePacketSha,
        }));
        Assert.True(VerificationEvidenceStore.TryParse(File.ReadAllBytes(storedPath), out var reparsed, out var parseDetail), parseDetail);
        Assert.Equal(alternatePacketSha, reparsed!.PacketSha256);

        foreach (var format in new[] { "json", "markdown" })
        {
            var coverage = RunCoverage(host.Root, CoverageArgs(host, alternatePacketSha, ["--evidence", "P1=E1"]), format);
            var report = ParseCoverage(coverage.Output, format);
            Assert.Equal(1, coverage.Exit);
            Assert.Equal("incomplete", Text(report, "state"));
            var row = report.GetProperty("proofs")[0];
            Assert.Equal("ineligible", Text(row, "state"));
            Assert.Equal("verification-coverage-stale-binding", Text(row, "cause"));
            Assert.Equal("stale", Text(row, "binding_state"));
            Assert.Equal(VerificationEvidenceCommandTests.Hash(trx), Text(row, "trx_sha256"));
            Assert.Equal(2, row.GetProperty("report").GetProperty("total").GetInt32());
        }
    }

    [Theory]
    [InlineData("failed-row", "verification-coverage-reported-failed")]
    [InlineData("passed-with-skips-row", "verification-coverage-reported-skipped")]
    [InlineData("skipped-row", "verification-coverage-reported-skipped")]
    [InlineData("zero-row", "verification-coverage-no-results")]
    public void ActualWriterOutcomeRowsRemainIneligibleWithVerifiedReportFacts(string mutation, string expectedCause)
    {
        foreach (var format in new[] { "json", "markdown" })
        {
            using var host = new VerificationEvidenceCommandTests.TempHost(Unit, Packet);
            var trx = VerificationEvidenceCommandTests.MutateSmallTrx(mutation);
            host.WriteTrx("capture.trx", trx);
            var packetSha = VerificationEvidenceCommandTests.Hash(host.PacketBytes);
            var (writeExit, writeOutput) = VerificationEvidenceCommandTests.Invoke(host,
                [.. VerificationEvidenceCommandTests.RecordArgs(host, format, packetSha, "capture.trx", id: "E1", unit: Unit, proof: "P1"), "--write"]);
            var written = VerificationEvidenceCommandTests.Result(writeOutput, format);
            Assert.Equal(0, writeExit);
            Assert.True(written.GetProperty("wrote").GetBoolean());

            var coverage = RunCoverage(host.Root, CoverageArgs(host, packetSha, ["--evidence", "P1=E1"]), format);
            var report = ParseCoverage(coverage.Output, format);
            Assert.Equal(1, coverage.Exit);
            Assert.Equal("incomplete", Text(report, "state"));
            var row = report.GetProperty("proofs")[0];
            Assert.Equal("ineligible", Text(row, "state"));
            Assert.Equal(expectedCause, Text(row, "cause"));
            Assert.Equal("current", Text(row, "binding_state"));
            Assert.Equal("E1", Text(row, "record_id"));
            Assert.Equal(VerificationEvidenceCommandTests.Hash(trx), Text(row, "trx_sha256"));
            Assert.NotNull(row.GetProperty("artifact_path").GetString());
            Assert.NotNull(row.GetProperty("artifact_sha256").GetString());
            if (mutation == "zero-row")
                Assert.Equal(0, row.GetProperty("report").GetProperty("total").GetInt32());
        }
    }

    [Fact]
    public void FailedTakesPrecedenceWhenVerifiedCurrentReportAlsoContainsSkippedRows()
    {
        var trx = MutateFailedAndSkipped(VerificationEvidenceCommandTests.ReadSmallVstest());
        using var host = new VerificationEvidenceCommandTests.TempHost(Unit, Packet);
        host.WriteTrx("capture.trx", trx);
        var packetSha = VerificationEvidenceCommandTests.Hash(host.PacketBytes);
        var (writeExit, writeOutput) = VerificationEvidenceCommandTests.Invoke(host,
            [.. VerificationEvidenceCommandTests.RecordArgs(host, "json", packetSha, "capture.trx", id: "E1", unit: Unit, proof: "P1"), "--write"]);
        Assert.Equal(0, writeExit);

        var coverage = RunCoverage(host.Root, CoverageArgs(host, packetSha, ["--evidence", "P1=E1"]));
        using var result = JsonDocument.Parse(coverage.Output);
        var row = result.RootElement.GetProperty("proofs")[0];
        Assert.Equal(1, coverage.Exit);
        Assert.Equal("ineligible", Text(row, "state"));
        Assert.Equal("verification-coverage-reported-failed", Text(row, "cause"));
        Assert.Equal(1, row.GetProperty("report").GetProperty("failed").GetInt32());
        Assert.Equal(1, row.GetProperty("report").GetProperty("skipped").GetInt32());
        Assert.Equal(0, row.GetProperty("report").GetProperty("raw_counters").GetProperty("not_executed").GetInt32());
    }

    [Fact]
    public void InvalidArgumentsUnknownReferencesAndHostedReferencesStopBeforeStoreReads()
    {
        using var host = new VerificationEvidenceCommandTests.TempHost(Unit, Packet);
        var packetSha = VerificationEvidenceCommandTests.Hash(host.PacketBytes);
        var callbacks = new List<string>();
        var invalid = CoverageArgs(host, packetSha, ["--evidence", "not=exact=pair"]);
        var (invalidExit, invalidOutput) = RunCoverage(host.Root, invalid, beforeOperation: (op, _) => callbacks.Add(op));
        using (var invalidJson = JsonDocument.Parse(invalidOutput))
        {
            Assert.Equal(1, invalidExit);
            Assert.Equal("invalid", Text(invalidJson.RootElement, "state"));
            Assert.Equal("verification-coverage-argument-invalid", Text(invalidJson.RootElement, "cause"));
            Assert.Empty(invalidJson.RootElement.GetProperty("proofs").EnumerateArray());
        }
        Assert.Empty(callbacks);

        var unknown = CoverageArgs(host, packetSha, ["--evidence", "Unknown=E1"]);
        var (unknownExit, unknownOutput) = RunCoverage(host.Root, unknown, beforeOperation: (op, _) => callbacks.Add(op));
        using (var unknownJson = JsonDocument.Parse(unknownOutput))
        {
            Assert.Equal(1, unknownExit);
            Assert.Equal("invalid", Text(unknownJson.RootElement, "state"));
            Assert.Equal("verification-coverage-reference-invalid", Text(unknownJson.RootElement, "cause"));
            Assert.Empty(unknownJson.RootElement.GetProperty("proofs").EnumerateArray());
        }
        Assert.Equal(new[] { "packet-read" }, callbacks);

        var hostedPacketText = Packet;
        var hostedProofMarker = "evidence: local-required";
        var hostedProofOffset = hostedPacketText.LastIndexOf(hostedProofMarker, StringComparison.Ordinal);
        Assert.True(hostedProofOffset >= 0);
        hostedPacketText = hostedPacketText[..hostedProofOffset] + "evidence: hosted-required"
            + hostedPacketText[(hostedProofOffset + hostedProofMarker.Length)..];
        var hostedPacket = Encoding.UTF8.GetBytes(hostedPacketText + "\n");
        using var hostedHost = new VerificationEvidenceCommandTests.TempHost(Unit, Encoding.UTF8.GetString(hostedPacket));
        var hostedSha = VerificationEvidenceCommandTests.Hash(hostedHost.PacketBytes);
        callbacks.Clear();
        var (hostedExit, hostedOutput) = RunCoverage(hostedHost.Root,
            CoverageArgs(hostedHost, hostedSha, ["--evidence", "P2=E2"]), beforeOperation: (op, _) => callbacks.Add(op));
        using (var hostedJson = JsonDocument.Parse(hostedOutput))
        {
            Assert.Equal(1, hostedExit);
            Assert.Equal("invalid", Text(hostedJson.RootElement, "state"));
            Assert.Equal("verification-coverage-reference-invalid", Text(hostedJson.RootElement, "cause"));
            Assert.Empty(hostedJson.RootElement.GetProperty("proofs").EnumerateArray());
        }
        Assert.Equal(new[] { "packet-read" }, callbacks);

        callbacks.Clear();
        var (emptyExit, emptyOutput) = RunCoverage(hostedHost.Root,
            CoverageArgs(hostedHost, hostedSha, []), beforeOperation: (op, _) => callbacks.Add(op));
        using var emptyJson = JsonDocument.Parse(emptyOutput);
        Assert.Equal(1, emptyExit);
        Assert.Equal("incomplete", Text(emptyJson.RootElement, "state"));
        Assert.Equal(new[] { "P1" }, emptyJson.RootElement.GetProperty("missing_proof_ids").EnumerateArray()
            .Select(value => value.GetString()).ToArray());
        Assert.Equal("not-applicable", Text(emptyJson.RootElement.GetProperty("proofs")[1], "state"));
        Assert.Equal("verification-coverage-hosted-not-observed", Text(emptyJson.RootElement.GetProperty("proofs")[1], "cause"));
        Assert.Equal(new[] { "packet-read" }, callbacks);
    }

    [Fact]
    public void AffectedLaneRemovalRefusesBeforeStoreReadsAndAllHostedContractIsStructurallyComplete()
    {
        var removedAffectedLane = Packet.Replace("affected_lanes: [unit, build]", "affected_lanes: [unit]", StringComparison.Ordinal);
        Assert.NotEqual(Packet, removedAffectedLane);
        var invalidBytes = Encoding.UTF8.GetBytes(removedAffectedLane + "\n");
        var structural = PacketVerificationContract.Evaluate(Unit, $".intent-cli/issues/{Unit}/packet.yaml", invalidBytes);
        Assert.Equal("invalid", structural.State);
        Assert.Equal("invalid-schema", structural.Diagnostics.Single().Cause);
        using (var host = new VerificationEvidenceCommandTests.TempHost(Unit, removedAffectedLane))
        {
            var reads = new List<string>();
            var result = RunCoverage(host.Root, CoverageArgs(host, VerificationEvidenceCommandTests.Hash(host.PacketBytes),
                    ["--evidence", "P1=E1", "--evidence", "P2=E2"]),
                beforeOperation: (operation, _) => reads.Add(operation));
            using var json = JsonDocument.Parse(result.Output);
            Assert.Equal(1, result.Exit);
            Assert.Equal("unavailable", Text(json.RootElement, "state"));
            Assert.Equal("verification-coverage-packet-unavailable", Text(json.RootElement, "cause"));
            Assert.Empty(json.RootElement.GetProperty("proofs").EnumerateArray());
            Assert.DoesNotContain("stored-read", reads);
        }

        var allHostedPacket = SingleProofPacket.Replace("evidence: local-required", "evidence: hosted-required", StringComparison.Ordinal);
        Assert.NotEqual(SingleProofPacket, allHostedPacket);
        using var hosted = new VerificationEvidenceCommandTests.TempHost(Unit, allHostedPacket);
        var hostedBytes = Encoding.UTF8.GetBytes(allHostedPacket + "\n");
        Assert.Equal("complete", PacketVerificationContract.Evaluate(Unit,
            $".intent-cli/issues/{Unit}/packet.yaml", hostedBytes).State);
        foreach (var format in new[] { "json", "markdown" })
        {
            var result = RunCoverage(hosted.Root,
                CoverageArgs(hosted, VerificationEvidenceCommandTests.Hash(hosted.PacketBytes), []), format);
            var report = ParseCoverage(result.Output, format);
            Assert.Equal(0, result.Exit);
            Assert.Equal("complete", Text(report, "structural_state"));
            Assert.Empty(report.GetProperty("missing_acceptance_criteria").EnumerateArray());
            Assert.Empty(report.GetProperty("missing_lanes").EnumerateArray());
            Assert.Equal(new[] { "not-applicable" }, report.GetProperty("proofs").EnumerateArray()
                .Select(row => Text(row, "state")).ToArray());
            Assert.Equal(new[] { "verification-coverage-hosted-not-observed" },
                report.GetProperty("proofs").EnumerateArray().Select(row => Text(row, "cause")).ToArray());
            Assert.Empty(report.GetProperty("present_proof_ids").EnumerateArray());
        }
    }

    [Fact]
    public void StandaloneArgumentMatrixRefusesBeforePacketOrEvidenceReadsInBothFormats()
    {
        using var host = new VerificationEvidenceCommandTests.TempHost(Unit, Packet);
        var packetSha = VerificationEvidenceCommandTests.Hash(host.PacketBytes);
        var baseline = CoverageArgs(host, packetSha, ["--evidence", "P1=E1"]);
        var cases = new List<(string Name, string[] Args)>
        {
            ("unknown-option", [.. baseline, "--unknown"]),
            ("write-not-supported", [.. baseline, "--write"]),
            ("dry-run-not-supported", [.. baseline, "--dry-run"]),
            ("trx-not-supported", [.. baseline, "--trx", "capture.trx"]),
            ("malformed-reference", [.. baseline, "--evidence", "P1==E2"]),
            ("invalid-unit", ReplaceValue(baseline, "--execution-unit", "G/867")),
            ("invalid-domain", ReplaceValue(baseline, "--domain", "bad/domain")),
            ("invalid-team", ReplaceValue(baseline, "--team", " bad")),
            ("invalid-repo", ReplaceValue(baseline, "--repo", "owner/repo/extra")),
            ("invalid-head", ReplaceValue(baseline, "--head-sha", new string('A', 40))),
            ("invalid-packet-hash", ReplaceValue(baseline, "--packet-sha256", "not-a-hash")),
            ("missing-team", RemoveOption(baseline, "--team")),
            ("duplicate-domain", [.. baseline, "--domain", Domain]),
            ("case-aliased-proof", [.. CoverageArgs(host, packetSha,
                ["--evidence", "P1=E1", "--evidence", "p1=E2"])]),
            ("case-aliased-record", [.. CoverageArgs(host, packetSha,
                ["--evidence", "P1=E1", "--evidence", "P2=e1"])]),
        };

        foreach (var format in new[] { "json", "markdown" })
        foreach (var scenario in cases)
        {
            var reads = new List<string>();
            var result = RunCoverage(host.Root, [.. scenario.Args], format,
                (operation, _) => reads.Add(operation));
            var report = ParseCoverage(result.Output, format);
            Assert.Equal(1, result.Exit);
            Assert.Equal("invalid", Text(report, "state"));
            Assert.Equal("verification-coverage-argument-invalid", Text(report, "cause"));
            Assert.Null(report.GetProperty("packet_path").GetString());
            Assert.Null(report.GetProperty("observed_packet_sha256").GetString());
            Assert.Empty(report.GetProperty("proofs").EnumerateArray());
            Assert.Empty(reads);
            Assert.False(Directory.Exists(host.EvidenceRoot));
        }
    }

    [Fact]
    public void MissingPartialAndAbsentRecordsStayDistinctAndOrdered()
    {
        using var host = new VerificationEvidenceCommandTests.TempHost(Unit, Packet);
        var packetSha = VerificationEvidenceCommandTests.Hash(host.PacketBytes);
        var reads = new List<string>();

        var (noneExit, noneOutput) = RunCoverage(host.Root, CoverageArgs(host, packetSha, []),
            beforeOperation: (operation, _) => reads.Add(operation));
        using (var none = JsonDocument.Parse(noneOutput))
        {
            Assert.Equal(1, noneExit);
            Assert.Equal("incomplete", Text(none.RootElement, "state"));
            Assert.Equal("verification-coverage-incomplete", Text(none.RootElement, "cause"));
            Assert.Equal(new[] { "P1", "P2" }, none.RootElement.GetProperty("missing_proof_ids").EnumerateArray()
                .Select(item => item.GetString()).ToArray());
            Assert.All(none.RootElement.GetProperty("proofs").EnumerateArray(), row =>
            {
                Assert.Equal("missing", Text(row, "state"));
                Assert.Equal("verification-coverage-reference-missing", Text(row, "cause"));
                Assert.Equal("unknown", Text(row, "binding_state"));
                Assert.Null(row.GetProperty("artifact_path").GetString());
                Assert.Null(row.GetProperty("artifact_sha256").GetString());
                Assert.Null(row.GetProperty("report").GetString());
            });
        }
        Assert.Equal(new[] { "packet-read" }, reads);

        WriteActualRecord(host, "P1", "E1", VerificationEvidenceCommandTests.ReadSmallVstest());
        reads.Clear();
        foreach (var format in new[] { "json", "markdown" })
        {
            reads.Clear();
            var partial = RunCoverage(host.Root, CoverageArgs(host, packetSha,
                ["--evidence", "P2=E2", "--evidence", "P1=E1"]), format,
                (operation, _) => reads.Add(operation));
            var partialJson = ParseCoverage(partial.Output, format);
            Assert.Equal(1, partial.Exit);
            Assert.Equal("incomplete", Text(partialJson, "state"));
            Assert.Equal(new[] { "P1" }, partialJson.GetProperty("present_proof_ids").EnumerateArray()
                .Select(item => item.GetString()).ToArray());
            Assert.Equal(new[] { "P2" }, partialJson.GetProperty("missing_proof_ids").EnumerateArray()
                .Select(item => item.GetString()).ToArray());
            var absent = partialJson.GetProperty("proofs")[1];
            Assert.Equal("P2", Text(absent, "proof_id"));
            Assert.Equal("missing", Text(absent, "state"));
            Assert.Equal("verification-coverage-record-missing", Text(absent, "cause"));
            Assert.Equal("verification-coverage-record-missing",
                Text(absent.GetProperty("diagnostics")[0], "cause"));
            Assert.Contains(".intent-cli/verification-evidence/intent-cli/intent-cli-dev/G867/E2.json",
                Text(absent.GetProperty("diagnostics")[0], "path"), StringComparison.Ordinal);
            Assert.Equal(new[] { "packet-read", "stored-read" }, reads);
        }

        var p2Path = WriteActualRecord(host, "P2", "E2", VerificationEvidenceCommandTests.ReadSmallVstest());
        var p1Path = host.Absolute(Path.Combine(".intent-cli", "verification-evidence", Domain, Team, Unit, "E1.json"));
        Assert.True(File.Exists(p1Path));
        File.WriteAllBytes(p1Path, "not a canonical record"u8.ToArray());
        var unavailableP1Bytes = File.ReadAllBytes(p1Path);
        var currentP2Bytes = File.ReadAllBytes(p2Path);
        foreach (var format in new[] { "json", "markdown" })
        {
            var mixed = RunCoverage(host.Root, CoverageArgs(host, packetSha,
                ["--evidence", "P1=E1", "--evidence", "P2=E2"]), format);
            var report = ParseCoverage(mixed.Output, format);
            Assert.Equal(1, mixed.Exit);
            Assert.Equal("unavailable", Text(report, "state"));
            Assert.Equal(new[] { "P1" }, report.GetProperty("unavailable_proof_ids").EnumerateArray()
                .Select(item => item.GetString()).ToArray());
            var rows = report.GetProperty("proofs").EnumerateArray().ToArray();
            var p1 = rows.Single(row => Text(row, "proof_id") == "P1");
            var p2 = rows.Single(row => Text(row, "proof_id") == "P2");
            Assert.Equal("unavailable", Text(p1, "state"));
            Assert.Equal("verification-coverage-record-unavailable",
                Text(p1.GetProperty("diagnostics")[0], "cause"));
            Assert.Equal(JsonValueKind.Null, p1.GetProperty("report").ValueKind);
            Assert.Equal("present", Text(p2, "state"));
            Assert.Equal(2, p2.GetProperty("report").GetProperty("total").GetInt32());
            Assert.NotNull(p2.GetProperty("artifact_path").GetString());
            Assert.NotNull(p2.GetProperty("artifact_sha256").GetString());
            Assert.Equal(VerificationEvidenceCommandTests.Hash(VerificationEvidenceCommandTests.ReadSmallVstest()),
                Text(p2, "trx_sha256"));
        }
        Assert.Equal(unavailableP1Bytes, File.ReadAllBytes(p1Path));
        Assert.Equal(currentP2Bytes, File.ReadAllBytes(p2Path));
    }

    [Theory]
    [InlineData("malformed-record")]
    [InlineData("empty-record")]
    [InlineData("null-record")]
    [InlineData("noncanonical-record")]
    [InlineData("wrong-trx-hash")]
    [InlineData("wrong-report-summary")]
    [InlineData("foreign-domain")]
    [InlineData("foreign-team")]
    [InlineData("foreign-unit")]
    [InlineData("foreign-id")]
    [InlineData("foreign-repository")]
    public void UnavailableOrForeignStoredRecordsDoNotCreateProofFacts(string mutation)
    {
        using var host = new VerificationEvidenceCommandTests.TempHost(Unit, Packet);
        var trx = VerificationEvidenceCommandTests.ReadSmallVstest();
        var packetSha = VerificationEvidenceCommandTests.Hash(host.PacketBytes);
        var path = WriteActualRecord(host, "P1", "E1", trx);
        var before = File.ReadAllBytes(path);
        if (mutation == "malformed-record")
        {
            File.WriteAllBytes(path, Encoding.UTF8.GetBytes("{ not canonical record"));
        }
        else if (mutation == "empty-record")
        {
            File.WriteAllBytes(path, []);
        }
        else if (mutation == "null-record")
        {
            File.WriteAllBytes(path, "null\n"u8.ToArray());
        }
        else if (mutation == "noncanonical-record")
        {
            File.WriteAllBytes(path, Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(before).Replace("\n", "\r\n", StringComparison.Ordinal)));
        }
        else
        {
            var record = JsonSerializer.Deserialize<VerificationEvidenceRecord>(before)!;
            var foreign = mutation switch
            {
                "wrong-trx-hash" => record with { TrxSha256 = new string('0', 64) },
                "wrong-report-summary" => record with { Report = record.Report with { Total = record.Report.Total + 1 } },
                "foreign-domain" => record with { Domain = "other-domain" },
                "foreign-team" => record with { Team = "other-team" },
                "foreign-unit" => record with { ExecutionUnit = "G868" },
                "foreign-id" => record with { Id = "E2" },
                "foreign-repository" => record with { TargetRepo = "Other/Repository" },
                _ => throw new ArgumentOutOfRangeException(nameof(mutation), mutation, null),
            };
            File.WriteAllBytes(path, VerificationEvidenceStore.Serialize(foreign));
        }
        var mutatedBytes = File.ReadAllBytes(path);
        foreach (var format in new[] { "json", "markdown" })
        {
            var coverage = RunCoverage(host.Root, CoverageArgs(host, packetSha, ["--evidence", "P1=E1"]), format);
            var result = ParseCoverage(coverage.Output, format);
            var row = result.GetProperty("proofs")[0];
            Assert.Equal(1, coverage.Exit);
            Assert.Equal("unavailable", Text(result, "state"));
            Assert.Equal(new[] { "P1" }, result.GetProperty("unavailable_proof_ids").EnumerateArray()
                .Select(item => item.GetString()).ToArray());
            Assert.Equal("unavailable", Text(row, "state"));
            Assert.Equal("verification-coverage-record-unavailable", Text(row, "cause"));
            Assert.Equal("unknown", Text(row, "binding_state"));
            Assert.Null(row.GetProperty("artifact_path").GetString());
            Assert.Null(row.GetProperty("artifact_sha256").GetString());
            Assert.Null(row.GetProperty("trx_sha256").GetString());
            Assert.Equal(JsonValueKind.Null, row.GetProperty("report").ValueKind);
            Assert.NotEmpty(row.GetProperty("diagnostics").EnumerateArray());
            Assert.Equal(mutatedBytes, File.ReadAllBytes(path));
        }
    }

    [Fact]
    public void CallerIdentityAndPacketDigestFailuresAreUnavailableBeforeStoreReads()
    {
        using var host = new VerificationEvidenceCommandTests.TempHost(Unit, Packet);
        WriteActualRecord(host, "P1", "E1", VerificationEvidenceCommandTests.ReadSmallVstest());
        var packetSha = VerificationEvidenceCommandTests.Hash(host.PacketBytes);
        var reads = new List<string>();

        foreach (var format in new[] { "json", "markdown" })
        {
            reads.Clear();
            var digestArgs = new[]
            {
                "--execution-unit", Unit, "--domain", Domain, "--team", Team, "--repo", Repo,
                "--head-sha", Head, "--packet-sha256", new string('0', 64), "--evidence", "P1=E1", "--format", format,
            };
            var wrongDigest = RunCoverage(host.Root, digestArgs, format,
                beforeOperation: (operation, _) => reads.Add(operation));
            var digest = ParseCoverage(wrongDigest.Output, format);
            Assert.Equal(1, wrongDigest.Exit);
            Assert.Equal("unavailable", Text(digest, "state"));
            Assert.Equal("verification-coverage-packet-digest-mismatch", Text(digest, "cause"));
            Assert.Equal(packetSha, Text(digest, "observed_packet_sha256"));
            Assert.Empty(digest.GetProperty("proofs").EnumerateArray());
            Assert.Equal(new[] { "packet-read" }, reads);
        }

        foreach (var format in new[] { "json", "markdown" })
        foreach (var flagAndValue in new[]
                 {
                     (Flag: "--execution-unit", Value: "G868", Cause: "verification-coverage-packet-unavailable", Observed: (string?)null, Reads: Array.Empty<string>()),
                     (Flag: "--domain", Value: "other-domain", Cause: "verification-coverage-identity-conflict", Observed: packetSha, Reads: new[] { "packet-read" }),
                     (Flag: "--team", Value: "other-team", Cause: "verification-coverage-identity-conflict", Observed: packetSha, Reads: new[] { "packet-read" }),
                     (Flag: "--repo", Value: "Other/Repository", Cause: "verification-coverage-identity-conflict", Observed: packetSha, Reads: new[] { "packet-read" }),
                 })
        {
            reads.Clear();
            var wrongIdentityArgs = CoverageArgs(host, packetSha, ["--evidence", "P1=E1"]);
            wrongIdentityArgs[Array.IndexOf(wrongIdentityArgs, flagAndValue.Flag) + 1] = flagAndValue.Value;
            var wrongIdentity = RunCoverage(host.Root, wrongIdentityArgs, format,
                beforeOperation: (operation, _) => reads.Add(operation));
            var identityJson = ParseCoverage(wrongIdentity.Output, format);
            Assert.Equal(1, wrongIdentity.Exit);
            Assert.Equal("unavailable", Text(identityJson, "state"));
            Assert.Equal(flagAndValue.Cause, Text(identityJson, "cause"));
            Assert.Equal(flagAndValue.Observed, identityJson.GetProperty("observed_packet_sha256").GetString());
            Assert.Empty(identityJson.GetProperty("proofs").EnumerateArray());
            Assert.Equal(flagAndValue.Reads, reads);
        }

        var requestedUnitPacket = VerificationEvidenceCommandTests.ValidPacket.Replace(
            "source_execution_unit: G866", "source_execution_unit: G867", StringComparison.Ordinal);
        using var requestedHost = new VerificationEvidenceCommandTests.TempHost("G868", requestedUnitPacket);
        var requestedPacketPath = Path.Combine(requestedHost.Root, ".intent-cli", "issues", "G868", "packet.yaml");
        Directory.CreateDirectory(Path.GetDirectoryName(requestedPacketPath)!);
        var requestedPacketBytes = Encoding.UTF8.GetBytes(requestedUnitPacket + "\n");
        File.WriteAllBytes(requestedPacketPath, requestedPacketBytes);
        var requestedPacketSha = VerificationEvidenceCommandTests.Hash(requestedPacketBytes);
        foreach (var format in new[] { "json", "markdown" })
        {
            reads.Clear();
            var args = CoverageArgs(requestedHost, requestedPacketSha, ["--evidence", "P1=E1"]);
            args[Array.IndexOf(args, "--execution-unit") + 1] = "G868";
            var mismatch = RunCoverage(requestedHost.Root, args, format,
                (operation, _) => reads.Add(operation));
            var report = ParseCoverage(mismatch.Output, format);
            Assert.Equal(1, mismatch.Exit);
            Assert.Equal("unavailable", Text(report, "state"));
            Assert.Equal("verification-coverage-identity-conflict", Text(report, "cause"));
            Assert.Equal(requestedPacketSha, Text(report, "observed_packet_sha256"));
            Assert.Empty(report.GetProperty("proofs").EnumerateArray());
            Assert.Equal(new[] { "packet-read" }, reads);
            Assert.False(Directory.Exists(requestedHost.EvidenceRoot));
        }
    }

    [Fact]
    public void StructuralG865GapsRemainVisibleAndInvalidPacketStopsBeforeStoreReads()
    {
        var proof2 = Packet.IndexOf("- id: P2", StringComparison.Ordinal);
        Assert.True(proof2 >= 0);
        var proof2Start = Packet.LastIndexOf('\n', proof2) + 1;
        var proof2End = Packet.IndexOf("evidence: local-required", proof2, StringComparison.Ordinal)
            + "evidence: local-required".Length;
        Assert.True(proof2End > proof2Start);
        var incompletePacket = Packet.Remove(proof2Start, proof2End - proof2Start).TrimEnd() + "\n";
        using (var host = new VerificationEvidenceCommandTests.TempHost(Unit, incompletePacket))
        {
            var sha = VerificationEvidenceCommandTests.Hash(host.PacketBytes);
            var incomplete = RunCoverage(host.Root, CoverageArgs(host, sha, []));
            using var result = JsonDocument.Parse(incomplete.Output);
            Assert.Equal(1, incomplete.Exit);
            Assert.Equal("incomplete", Text(result.RootElement, "state"));
            Assert.Equal("incomplete", Text(result.RootElement, "structural_state"));
            Assert.Equal(new[] { "AC2" }, result.RootElement.GetProperty("missing_acceptance_criteria").EnumerateArray()
                .Select(item => item.GetString()).ToArray());
            Assert.Equal(new[] { "build" }, result.RootElement.GetProperty("missing_lanes").EnumerateArray()
                .Select(item => item.GetString()).ToArray());
            Assert.Equal(new[] { "build" }, result.RootElement.GetProperty("missing_local_required_lanes").EnumerateArray()
                .Select(item => item.GetString()).ToArray());
        }

        var invalidPacket = RemoveLineContaining(Packet, "id: build, weight: heavy");
        Assert.NotEqual(Packet, invalidPacket);
        using var invalidHost = new VerificationEvidenceCommandTests.TempHost(Unit, invalidPacket);
        var invalidSha = VerificationEvidenceCommandTests.Hash(invalidHost.PacketBytes);
        var storeReads = new List<string>();
        var invalidResult = RunCoverage(invalidHost.Root, CoverageArgs(invalidHost, invalidSha,
            ["--evidence", "P1=E1"]), beforeOperation: (operation, _) => storeReads.Add(operation));
        using var invalidJson = JsonDocument.Parse(invalidResult.Output);
        Assert.Equal(1, invalidResult.Exit);
        Assert.Equal("unavailable", Text(invalidJson.RootElement, "state"));
        Assert.Equal("verification-coverage-packet-unavailable", Text(invalidJson.RootElement, "cause"));
        Assert.Empty(invalidJson.RootElement.GetProperty("proofs").EnumerateArray());
        Assert.Equal(new[] { "packet-read" }, storeReads);
    }

    [Fact]
    public void ReferenceCapAcceptsSixteenDistinctRecordsAndRejectsSeventeenBeforeReads()
    {
        const int maximum = VerificationEvidenceCoverage.MaximumReferences;
        var packet = BuildPacketWithProofs(maximum);
        using var host = new VerificationEvidenceCommandTests.TempHost(Unit, packet);
        var trx = VerificationEvidenceCommandTests.ReadSmallVstest();
        for (var index = 1; index <= maximum; index++)
        {
            var id = $"E{index:D3}";
            WriteActualRecord(host, $"P{index}", id, trx);
        }

        var sixteenReferences = Enumerable.Range(1, maximum).Reverse()
            .SelectMany(index => new[] { "--evidence", $"P{index}=E{index:D3}" }).ToArray();
        var reads = new List<string>();
        var sixteen = RunCoverage(host.Root,
            CoverageArgs(host, VerificationEvidenceCommandTests.Hash(host.PacketBytes), sixteenReferences),
            beforeOperation: (operation, _) => reads.Add(operation));
        using (var sixteenJson = JsonDocument.Parse(sixteen.Output))
        {
            Assert.Equal(0, sixteen.Exit);
            Assert.Equal("complete", Text(sixteenJson.RootElement, "state"));
            Assert.Equal(maximum, sixteenJson.RootElement.GetProperty("present_proof_ids").GetArrayLength());
            Assert.Equal(maximum, sixteenJson.RootElement.GetProperty("proofs").GetArrayLength());
        }
        Assert.Equal(1, reads.Count(operation => operation == "packet-read"));
        Assert.Equal(maximum, reads.Count(operation => operation == "stored-read"));

        reads.Clear();
        var seventeenReferences = Enumerable.Range(1, maximum + 1)
            .SelectMany(index => new[] { "--evidence", $"P{index}=E{index:D3}" }).ToArray();
        var seventeen = RunCoverage(host.Root,
            CoverageArgs(host, VerificationEvidenceCommandTests.Hash(host.PacketBytes), seventeenReferences),
            beforeOperation: (operation, _) => reads.Add(operation));
        using var seventeenJson = JsonDocument.Parse(seventeen.Output);
        Assert.Equal(1, seventeen.Exit);
        Assert.Equal("invalid", Text(seventeenJson.RootElement, "state"));
        Assert.Equal("verification-coverage-argument-invalid", Text(seventeenJson.RootElement, "cause"));
        Assert.Empty(seventeenJson.RootElement.GetProperty("proofs").EnumerateArray());
        Assert.Empty(reads);
    }

    [Fact]
    public void GenuineCapturedSkippedRowsStayIneligibleAndNameOnlyDiscrepanciesDoNot()
    {
        var skippedTrx = VerificationEvidenceCommandTests.ReadFullVstest();
        var nameDifferenceOnly = MakeCapturedTrxAllPassing(skippedTrx);
        var rawTestNames = XDocument.Parse(Encoding.UTF8.GetString(skippedTrx).TrimStart('\uFEFF'))
            .Descendants().Where(element => element.Name.LocalName == "UnitTestResult")
            .Select(element => (string?)element.Attribute("testName"))
            .Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.Ordinal).ToArray();
        Assert.NotEmpty(rawTestNames);

        foreach (var format in new[] { "json", "markdown" })
        {
            using var skippedHost = new VerificationEvidenceCommandTests.TempHost(Unit, Packet);
            var packetSha = VerificationEvidenceCommandTests.Hash(skippedHost.PacketBytes);
            WriteActualRecord(skippedHost, "P1", "E1", skippedTrx);
            var coverage = RunCoverage(skippedHost.Root,
                CoverageArgs(skippedHost, packetSha, ["--evidence", "P1=E1"]), format);
            var report = ParseCoverage(coverage.Output, format);
            var row = report.GetProperty("proofs")[0];
            Assert.Equal(1, coverage.Exit);
            Assert.Equal("ineligible", Text(row, "state"));
            Assert.Equal("verification-coverage-reported-skipped", Text(row, "cause"));
            Assert.Equal(9543, row.GetProperty("report").GetProperty("total").GetInt32());
            Assert.Equal(9537, row.GetProperty("report").GetProperty("executed").GetInt32());
            Assert.Equal(9537, row.GetProperty("report").GetProperty("passed").GetInt32());
            Assert.Equal(0, row.GetProperty("report").GetProperty("failed").GetInt32());
            Assert.Equal(6, row.GetProperty("report").GetProperty("skipped").GetInt32());
            Assert.Equal(0, row.GetProperty("report").GetProperty("raw_counters").GetProperty("not_executed").GetInt32());
            Assert.False(row.TryGetProperty("trx_base64", out _));
            Assert.DoesNotContain("<TestRun", coverage.Output, StringComparison.Ordinal);
            Assert.DoesNotContain("UnitTestResult", coverage.Output, StringComparison.Ordinal);

            using var passHost = new VerificationEvidenceCommandTests.TempHost(Unit, Packet);
            var passPacketSha = VerificationEvidenceCommandTests.Hash(passHost.PacketBytes);
            WriteActualRecord(passHost, "P1", "E1", nameDifferenceOnly);
            WriteActualRecord(passHost, "P2", "E2", nameDifferenceOnly);
            var pass = RunCoverage(passHost.Root, CoverageArgs(passHost, passPacketSha,
                ["--evidence", "P1=E1", "--evidence", "P2=E2"]), format);
            var passReport = ParseCoverage(pass.Output, format);
            var rows = passReport.GetProperty("proofs").EnumerateArray().ToArray();
            Assert.Equal(0, pass.Exit);
            Assert.Equal("complete", Text(passReport, "state"));
            Assert.All(rows, passingRow =>
            {
                Assert.Equal("present", Text(passingRow, "state"));
                Assert.Equal(0, passingRow.GetProperty("report").GetProperty("skipped").GetInt32());
                var discrepancy = passingRow.GetProperty("report").GetProperty("discrepancies")[0];
                Assert.Equal(4, discrepancy.GetProperty("count").GetInt32());
                Assert.Equal("definition-name-differs", Text(discrepancy, "code"));
            });
            Assert.DoesNotContain("testName", pass.Output, StringComparison.Ordinal);
            Assert.DoesNotContain("<TestRun", pass.Output, StringComparison.Ordinal);
            Assert.DoesNotContain("UnitTestResult", pass.Output, StringComparison.Ordinal);
            foreach (var name in rawTestNames) Assert.DoesNotContain(name!, pass.Output, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("json")]
    [InlineData("markdown")]
    public void PacketAndRecordSymlinkConsumersRefuseBeforeReadingOrChangingOutsideBytes(string format)
    {
        using (var outside = new VerificationEvidenceCommandTests.TempHost(Unit, Packet))
        using (var inside = new VerificationEvidenceCommandTests.TempHost(Unit, Packet))
        {
            var outsidePacketBytes = File.ReadAllBytes(outside.PacketPath);
            var outsidePacket = outside.Absolute("outside-packet.yaml");
            File.WriteAllBytes(outsidePacket, outsidePacketBytes);
            var insidePacketBackup = inside.PacketPath + ".regular-backup";
            File.Move(inside.PacketPath, insidePacketBackup);
            File.CreateSymbolicLink(inside.PacketPath, outsidePacket);
            var callbackOperations = new List<string>();
            var packetCoverage = RunCoverage(inside.Root,
                CoverageArgs(inside, VerificationEvidenceCommandTests.Hash(outsidePacketBytes), []), format,
                (operation, _) => callbackOperations.Add(operation));
            var packetReport = ParseCoverage(packetCoverage.Output, format);
            Assert.Equal(1, packetCoverage.Exit);
            Assert.Equal("unavailable", Text(packetReport, "state"));
            Assert.Equal("verification-coverage-packet-unavailable", Text(packetReport, "cause"));
            Assert.Equal(".intent-cli/issues/G867/packet.yaml", Text(packetReport, "packet_path"));
            Assert.Equal(JsonValueKind.Null, packetReport.GetProperty("observed_packet_sha256").ValueKind);
            Assert.Empty(packetReport.GetProperty("proofs").EnumerateArray());
            Assert.Equal(new[] { "packet-read" }, callbackOperations);
            Assert.Equal(outsidePacketBytes, File.ReadAllBytes(outsidePacket));
            Assert.Equal(outsidePacketBytes, File.ReadAllBytes(insidePacketBackup));
        }

        using (var outside = new VerificationEvidenceCommandTests.TempHost(Unit, Packet))
        using (var inside = new VerificationEvidenceCommandTests.TempHost(Unit, Packet))
        {
            var trx = VerificationEvidenceCommandTests.ReadSmallVstest();
            var outsideRecord = WriteActualRecord(outside, "P1", "E1", trx);
            var outsideBytes = File.ReadAllBytes(outsideRecord);
            var recordSha = VerificationEvidenceCommandTests.Hash(outside.PacketBytes);
            var insideRecord = inside.Absolute(Path.Combine(".intent-cli", "verification-evidence", Domain, Team, Unit, "E1.json"));
            Directory.CreateDirectory(Path.GetDirectoryName(insideRecord)!);
            File.CreateSymbolicLink(insideRecord, outsideRecord);
            var callbackOperations = new List<string>();
            var coverage = RunCoverage(inside.Root, CoverageArgs(inside, recordSha, ["--evidence", "P1=E1"]), format,
                (operation, _) => callbackOperations.Add(operation));
            var report = ParseCoverage(coverage.Output, format);
            var row = report.GetProperty("proofs")[0];
            Assert.Equal(1, coverage.Exit);
            Assert.Equal("unavailable", Text(report, "state"));
            Assert.Equal("unavailable", Text(row, "state"));
            Assert.Equal("unknown", Text(row, "binding_state"));
            Assert.Equal(JsonValueKind.Null, row.GetProperty("artifact_path").ValueKind);
            Assert.Equal(JsonValueKind.Null, row.GetProperty("artifact_sha256").ValueKind);
            Assert.Contains(".intent-cli/verification-evidence/intent-cli/intent-cli-dev/G867/E1.json",
                Text(row.GetProperty("diagnostics")[0], "path"), StringComparison.Ordinal);
            Assert.Equal(new[] { "packet-read" }, callbackOperations);
            Assert.Equal(outsideBytes, File.ReadAllBytes(outsideRecord));
        }
    }

    [Theory]
    [InlineData("json")]
    [InlineData("markdown")]
    public void PacketLeafCaseAliasIsRejectedByCoverageConsumer(string format)
    {
        using var host = new VerificationEvidenceCommandTests.TempHost(Unit, Packet);
        var packetBytes = File.ReadAllBytes(host.PacketPath);
        var aliasPath = Path.Combine(Path.GetDirectoryName(host.PacketPath)!, "Packet.yaml");
        var temporaryPath = host.PacketPath + ".case-alias-temp";
        File.Move(host.PacketPath, temporaryPath);
        File.Move(temporaryPath, aliasPath);
        var reads = new List<string>();

        var result = RunCoverage(host.Root, CoverageArgs(host, VerificationEvidenceCommandTests.Hash(packetBytes), []), format,
            (operation, path) => reads.Add(operation + ":" + path));
        var report = ParseCoverage(result.Output, format);
        Assert.Equal(1, result.Exit);
        Assert.Equal("unavailable", Text(report, "state"));
            Assert.Equal("verification-coverage-identity-conflict", Text(report, "cause"));
        Assert.Null(report.GetProperty("observed_packet_sha256").GetString());
        Assert.Empty(report.GetProperty("proofs").EnumerateArray());
        Assert.Empty(reads);
        Assert.True(File.Exists(aliasPath));
        Assert.Equal(new[] { "Packet.yaml" }, Directory.EnumerateFiles(Path.GetDirectoryName(host.PacketPath)!)
            .Select(Path.GetFileName).ToArray());
        Assert.Equal(packetBytes, File.ReadAllBytes(aliasPath));
    }

    [Theory]
    [InlineData("json")]
    [InlineData("markdown")]
    public void ReachedStoredRecordReadFailureIsUnavailableWithoutCandidateFacts(string format)
    {
        using var host = new VerificationEvidenceCommandTests.TempHost(Unit, Packet);
        var trx = VerificationEvidenceCommandTests.ReadSmallVstest();
        var recordPath = WriteActualRecord(host, "P1", "E1", trx);
        var originalBytes = File.ReadAllBytes(recordPath);
        var packetSha = VerificationEvidenceCommandTests.Hash(host.PacketBytes);
        var readReached = false;
        var coverage = RunCoverage(host.Root, CoverageArgs(host, packetSha, ["--evidence", "P1=E1"]), format,
            (operation, path) =>
            {
                if (operation != "stored-read") return;
                Assert.Equal(recordPath, path);
                readReached = true;
                File.Delete(path);
            });
        var report = ParseCoverage(coverage.Output, format);
        var row = report.GetProperty("proofs")[0];
        Assert.True(readReached);
        Assert.Equal(1, coverage.Exit);
        Assert.Equal("unavailable", Text(report, "state"));
        Assert.Equal("unavailable", Text(row, "state"));
        Assert.Equal("verification-coverage-record-unavailable", Text(row, "cause"));
        Assert.Null(row.GetProperty("artifact_path").GetString());
        Assert.Null(row.GetProperty("artifact_sha256").GetString());
        Assert.Null(row.GetProperty("trx_sha256").GetString());
        Assert.Equal(JsonValueKind.Null, row.GetProperty("report").ValueKind);
        Assert.Contains(".intent-cli/verification-evidence/intent-cli/intent-cli-dev/G867/E1.json",
            Text(row.GetProperty("diagnostics")[0], "path"), StringComparison.Ordinal);
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(recordPath)!, "*.tmp", SearchOption.TopDirectoryOnly));
        Assert.False(File.Exists(recordPath));
        Assert.NotEmpty(originalBytes);
    }

    [Theory]
    [InlineData("json")]
    [InlineData("markdown")]
    public void OsDeniedStoredRecordReadIsUnavailableAfterProvenPermissionFailure(string format)
    {
        if (OperatingSystem.IsWindows() || string.Equals(Environment.UserName, "root", StringComparison.OrdinalIgnoreCase))
            throw Xunit.Sdk.SkipException.ForSkip("Unix permission enforcement is unavailable under this test identity.");

        using var host = new VerificationEvidenceCommandTests.TempHost(Unit, Packet);
        var trx = VerificationEvidenceCommandTests.ReadSmallVstest();
        var recordPath = WriteActualRecord(host, "P1", "E1", trx);
        var originalBytes = File.ReadAllBytes(recordPath);
        var originalMode = File.GetUnixFileMode(recordPath);
        File.SetUnixFileMode(recordPath, UnixFileMode.None);
        try
        {
            if (Record.Exception(() => File.ReadAllBytes(recordPath)) is not UnauthorizedAccessException)
                throw Xunit.Sdk.SkipException.ForSkip("The OS did not reject a direct read of the mode-000 evidence record.");

            var coverage = RunCoverage(host.Root,
                CoverageArgs(host, VerificationEvidenceCommandTests.Hash(host.PacketBytes), ["--evidence", "P1=E1"]), format);
            var report = ParseCoverage(coverage.Output, format);
            var row = report.GetProperty("proofs")[0];
            Assert.Equal(1, coverage.Exit);
            Assert.Equal("unavailable", Text(report, "state"));
            Assert.Equal("unavailable", Text(row, "state"));
            Assert.Equal("verification-coverage-record-unavailable", Text(row, "cause"));
            Assert.Null(row.GetProperty("artifact_path").GetString());
            Assert.Null(row.GetProperty("artifact_sha256").GetString());
            Assert.Null(row.GetProperty("trx_sha256").GetString());
            Assert.Equal(JsonValueKind.Null, row.GetProperty("report").ValueKind);
            Assert.Equal(".intent-cli/verification-evidence/intent-cli/intent-cli-dev/G867/E1.json",
                Text(row.GetProperty("diagnostics")[0], "path"));
        }
        finally
        {
            File.SetUnixFileMode(recordPath, originalMode);
        }
        Assert.Equal(originalBytes, File.ReadAllBytes(recordPath));
    }

    [Fact]
    public void FreshCloneSeesOnlyCommittedWriterRecordAndCoverageNeverTouchesGitState()
    {
        var tempRoot = OperatingSystem.IsMacOS() && Directory.Exists("/private/tmp") ? "/private/tmp" : Path.GetTempPath();
        var workspace = Path.Combine(tempRoot, "intent-cli-g867-git-" + Guid.NewGuid().ToString("N"));
        var bare = Path.Combine(workspace, "origin.git");
        var seed = Path.Combine(workspace, "seed");
        var writer = Path.Combine(workspace, "writer");
        var reader = Path.Combine(workspace, "reader-before-push");
        var after = Path.Combine(workspace, "reader-after-push");
        Directory.CreateDirectory(workspace);
        try
        {
            RunGit(workspace, "init", "--bare", "--initial-branch=main", bare);
            RunGit(workspace, "clone", bare, seed);
            ConfigureGit(seed);
            var packetPath = Path.Combine(seed, ".intent-cli", "issues", Unit, "packet.yaml");
            Directory.CreateDirectory(Path.GetDirectoryName(packetPath)!);
            var packetBytes = Encoding.UTF8.GetBytes(SingleProofPacket + "\n");
            File.WriteAllBytes(packetPath, packetBytes);
            RunGit(seed, "add", "--", ".intent-cli/issues/" + Unit + "/packet.yaml");
            RunGit(seed, "commit", "-m", "publish packet fixture");
            RunGit(seed, "push", "origin", "HEAD:refs/heads/main");

            RunGit(workspace, "clone", bare, reader);
            RunGit(workspace, "clone", bare, writer);
            ConfigureGit(writer);
            var packetSha = VerificationEvidenceCommandTests.Hash(packetBytes);
            var references = new[] { "--evidence", "P1=E1" };
            var readerArgs = CoverageArgsForRoot(reader, packetSha, references);

            var beforeState = SnapshotGit(reader);
            var beforeTree = SnapshotTree(reader);
            var before = RunCoverage(reader, readerArgs);
            AssertCoverageRow(before, expectedTop: "incomplete", expectedRow: "missing",
                expectedCause: "verification-coverage-record-missing");
            AssertReadOnly(reader, beforeState, beforeTree);
            Assert.False(File.Exists(Path.Combine(reader, "capture.trx")));

            File.WriteAllBytes(Path.Combine(writer, "capture.trx"), VerificationEvidenceCommandTests.ReadSmallVstest());
            var recordArgs = new[]
            {
                "record", "--id", "E1", "--execution-unit", Unit, "--domain", Domain, "--team", Team,
                "--repo", Repo, "--head-sha", Head, "--packet-sha256", packetSha, "--proof", "P1",
                "--trx", "capture.trx", "--write", "--format", "json",
            };
            using (var writerOutput = new StringWriter())
            {
                var writerExit = VerificationEvidenceCommand.Execute(writer, recordArgs, writerOutput);
                using var written = JsonDocument.Parse(writerOutput.ToString());
                Assert.Equal(0, writerExit);
                Assert.Equal("recorded", Text(written.RootElement, "disposition"));
                Assert.True(written.RootElement.GetProperty("wrote").GetBoolean());
            }

            var stillBeforePushState = SnapshotGit(reader);
            var stillBeforePushTree = SnapshotTree(reader);
            var stillBeforePush = RunCoverage(reader, readerArgs);
            AssertCoverageRow(stillBeforePush, expectedTop: "incomplete", expectedRow: "missing",
                expectedCause: "verification-coverage-record-missing");
            AssertReadOnly(reader, stillBeforePushState, stillBeforePushTree);
            Assert.False(File.Exists(Path.Combine(reader, "capture.trx")));

            var recordRelative = Path.Combine(".intent-cli", "verification-evidence", Domain, Team, Unit, "E1.json");
            RunGit(writer, "add", "--", recordRelative);
            RunGit(writer, "commit", "-m", "publish supplied verification record");
            RunGit(writer, "push", "origin", "HEAD:refs/heads/main");
            RunGit(workspace, "clone", bare, after);
            ConfigureGit(after);
            Assert.False(File.Exists(Path.Combine(after, "capture.trx")));
            // Prove observation succeeds with no reachable origin and no raw TRX.
            RunGit(after, "remote", "set-url", "origin", Path.Combine(workspace, "origin-does-not-exist.git"));
            var afterArgs = CoverageArgsForRoot(after, packetSha, references);
            var afterState = SnapshotGit(after);
            var afterTree = SnapshotTree(after);
            var observed = RunCoverage(after, afterArgs);
            AssertCoverageRow(observed, expectedTop: "complete", expectedRow: "present",
                expectedCause: "verification-coverage-present");
            AssertReadOnly(after, afterState, afterTree);
        }
        finally
        {
            if (Directory.Exists(workspace)) Directory.Delete(workspace, recursive: true);
        }
    }

    [Theory]
    [InlineData("issue-to-pr", "pr-created")]
    [InlineData("pr-comment-fix", "repair-pushed")]
    public void ResultSummaryProjectionAddsCoverageWithoutChangingLifecycleOrExit(string kind, string outcome)
    {
        using var host = new VerificationEvidenceCommandTests.TempHost(Unit, Packet);
        var trx = VerificationEvidenceCommandTests.ReadSmallVstest();
        var packetSha = VerificationEvidenceCommandTests.Hash(host.PacketBytes);
        WriteActualRecord(host, "P1", "E1", trx);
        WriteActualRecord(host, "P2", "E2", trx);
        var context = Context(host.Root);

        foreach (var format in new[] { "json", "text", "markdown" })
        {
            var legacy = RunSummary(context, LegacySummaryArgs(kind, outcome, format == "markdown" ? "text" : format));
            var summaryArgs = SummaryArgs(kind, outcome, packetSha,
                ["--verification-evidence", "P2=E2", "--verification-evidence", "P1=E1"], format);
            var projected = RunSummary(context, summaryArgs);
            Assert.Equal(0, legacy.Exit);
            Assert.Equal(0, projected.Exit);
            if (format == "json")
            {
                using var before = JsonDocument.Parse(legacy.Output);
                using var after = JsonDocument.Parse(projected.Output);
                Assert.Equal(before.RootElement.GetProperty("status").GetString(), after.RootElement.GetProperty("status").GetString());
                Assert.Equal(before.RootElement.GetProperty("outcome").GetString(), after.RootElement.GetProperty("outcome").GetString());
                Assert.Equal(before.RootElement.GetProperty("warnings").GetRawText(), after.RootElement.GetProperty("warnings").GetRawText());
                Assert.Equal(before.RootElement.GetProperty("recommended_label_actions").GetRawText(),
                    after.RootElement.GetProperty("recommended_label_actions").GetRawText());
                var nested = after.RootElement.GetProperty("verification_coverage");
                Assert.Equal("complete", Text(nested, "state"));
                var direct = RunCoverage(host.Root, CoverageArgs(host, packetSha,
                    ["--evidence", "P2=E2", "--evidence", "P1=E1"]));
                Assert.Equal(0, direct.Exit);
                Assert.True(JsonNode.DeepEquals(JsonNode.Parse(direct.Output), JsonNode.Parse(nested.GetRawText())));
            }
            else
            {
                Assert.Contains("- status: completed", projected.Output, StringComparison.Ordinal);
                Assert.Contains($"- outcome: {outcome}", projected.Output, StringComparison.Ordinal);
                Assert.Contains("## Verification evidence coverage", projected.Output, StringComparison.Ordinal);
                var nested = ParseCoverage(projected.Output, "markdown");
                Assert.Equal("complete", Text(nested, "state"));
            }
        }
    }

    [Fact]
    public void ResultSummaryPacketDiscoveredInvalidCoverageKeepsExitZeroAndLifecycleFields()
    {
        using var host = new VerificationEvidenceCommandTests.TempHost(Unit, Packet);
        var packetSha = VerificationEvidenceCommandTests.Hash(host.PacketBytes);
        var args = SummaryArgs("issue-to-pr", "pr-created", packetSha,
            ["--verification-evidence", "Unknown=E1"], "json");
        var result = RunSummary(Context(host.Root), args);
        using var json = JsonDocument.Parse(result.Output);
        Assert.Equal(0, result.Exit);
        Assert.Equal("completed", Text(json.RootElement, "status"));
        Assert.Equal("pr-created", Text(json.RootElement, "outcome"));
        Assert.Equal("invalid", Text(json.RootElement.GetProperty("verification_coverage"), "state"));
        Assert.Equal("verification-coverage-reference-invalid", Text(json.RootElement.GetProperty("verification_coverage"), "cause"));
        Assert.Empty(json.RootElement.GetProperty("verification_coverage").GetProperty("proofs").EnumerateArray());
        Assert.False(Directory.Exists(host.EvidenceRoot));
    }

    [Fact]
    public void ResultSummaryIncompleteStaleAndUnavailableCoverageNeverChangesLifecycleFields()
    {
        using var host = new VerificationEvidenceCommandTests.TempHost(Unit, Packet);
        var trx = VerificationEvidenceCommandTests.ReadSmallVstest();
        var originalPacketSha = VerificationEvidenceCommandTests.Hash(host.PacketBytes);
        WriteActualRecord(host, "P1", "E1", trx);
        var context = Context(host.Root);
        var legacyArgs = LegacySummaryArgs("pr-comment-fix", "repair-pushed", "json");
        var (legacyExit, legacyOutput) = RunSummary(context, legacyArgs);
        Assert.Equal(0, legacyExit);
        using var legacy = JsonDocument.Parse(legacyOutput);

        var scenarios = new List<(string Name, string[] Args, string ExpectedCoverageState)>
        {
            ("missing", SummaryArgs("pr-comment-fix", "repair-pushed", originalPacketSha, [], "json"), "incomplete"),
            ("stale", SummaryArgs("pr-comment-fix", "repair-pushed", originalPacketSha,
                ["--verification-evidence", "P1=E1"], "json"), "incomplete"),
            ("unavailable", SummaryArgs("pr-comment-fix", "repair-pushed", new string('0', 64),
                ["--verification-evidence", "P1=E1"], "json"), "unavailable"),
        };
        var staleIndex = Array.IndexOf(scenarios[1].Args, "--verification-head-sha") + 1;
        scenarios[1].Args[staleIndex] = new string('a', 40);

        foreach (var scenario in scenarios)
        {
            var args = scenario.Args;
            if (scenario.Name == "unavailable")
            {
                host.WritePacket(Encoding.UTF8.GetBytes(Packet + "\n# current bytes differ from supplied packet digest\n"));
            }
            var (exit, output) = RunSummary(context, args);
            using var summary = JsonDocument.Parse(output);
            Assert.Equal(0, exit);
            Assert.Equal("completed", Text(summary.RootElement, "status"));
            Assert.Equal("repair-pushed", Text(summary.RootElement, "outcome"));
            Assert.Equal(legacy.RootElement.GetProperty("warnings").GetRawText(),
                summary.RootElement.GetProperty("warnings").GetRawText());
            Assert.Equal(legacy.RootElement.GetProperty("recommended_label_actions").GetRawText(),
                summary.RootElement.GetProperty("recommended_label_actions").GetRawText());
            Assert.Equal(scenario.ExpectedCoverageState,
                Text(summary.RootElement.GetProperty("verification_coverage"), "state"));
        }
    }

    [Theory]
    [InlineData("issue-to-pr", "pr-created")]
    [InlineData("pr-comment-fix", "repair-pushed")]
    public void ResultSummaryAdvisoryStatesPreserveLifecycleAcrossKindsAndFormats(string kind, string outcome)
    {
        var cases = new[] { "missing", "record-missing", "stale", "unavailable", "invalid-reference", "hosted-reference" };
        foreach (var format in new[] { "json", "markdown" })
        foreach (var scenario in cases)
        {
            var packet = scenario == "hosted-reference"
                ? Packet.Replace("evidence: local-required", "evidence: hosted-required", StringComparison.Ordinal)
                : Packet;
            using var host = new VerificationEvidenceCommandTests.TempHost(Unit, packet);
            var originalPacketBytes = File.ReadAllBytes(host.PacketPath);
            var originalPacketSha = VerificationEvidenceCommandTests.Hash(originalPacketBytes);
            if (scenario is "stale" or "unavailable")
                WriteActualRecord(host, "P1", "E1", VerificationEvidenceCommandTests.ReadSmallVstest());

            var references = scenario switch
            {
                "missing" => Array.Empty<string>(),
                "record-missing" => ["--verification-evidence", "P1=E1"],
                "invalid-reference" => ["--verification-evidence", "Unknown=E1"],
                "hosted-reference" => ["--verification-evidence", "P1=E1"],
                _ => ["--verification-evidence", "P1=E1"],
            };
            var args = SummaryArgs(kind, outcome, originalPacketSha, references, format);
            if (scenario == "stale")
                args[Array.IndexOf(args, "--verification-head-sha") + 1] = new string('a', 40);
            if (scenario == "unavailable")
                host.WritePacket(Encoding.UTF8.GetBytes(Packet + "\n# caller digest is now stale\n"));

            var beforeTree = SnapshotTree(host.Root);
            var result = RunSummary(Context(host.Root), args);
            Assert.Equal(0, result.Exit);
            Assert.Equal(beforeTree, SnapshotTree(host.Root));
            JsonElement coverage;
            if (format == "json")
            {
                using var json = JsonDocument.Parse(result.Output);
                Assert.Equal("completed", Text(json.RootElement, "status"));
                Assert.Equal(outcome, Text(json.RootElement, "outcome"));
                Assert.True(json.RootElement.TryGetProperty("verification_coverage", out var nestedCoverage));
                coverage = nestedCoverage.Clone();
                AssertSummaryCoverageState(coverage, scenario);
            }
            else
            {
                Assert.Contains("- status: completed", result.Output, StringComparison.Ordinal);
                Assert.Contains($"- outcome: {outcome}", result.Output, StringComparison.Ordinal);
                coverage = ParseCoverage(result.Output, "markdown");
                AssertSummaryCoverageState(coverage, scenario);
            }
            if (scenario == "record-missing")
            {
                var missing = coverage.GetProperty("proofs").EnumerateArray()
                    .Single(row => Text(row, "proof_id") == "P1");
                Assert.Equal("missing", Text(missing, "state"));
                Assert.Equal("verification-coverage-record-missing", Text(missing, "cause"));
                Assert.Equal("verification-coverage-record-missing",
                    Text(missing.GetProperty("diagnostics")[0], "cause"));
            }
        }
    }

    private static void AssertSummaryCoverageState(JsonElement coverage, string scenario)
    {
        var expected = scenario switch
        {
            "missing" or "record-missing" or "stale" => "incomplete",
            "unavailable" => "unavailable",
            _ => "invalid",
        };
        Assert.Equal(expected, Text(coverage, "state"));
        if (scenario is "invalid-reference" or "hosted-reference")
            Assert.Empty(coverage.GetProperty("proofs").EnumerateArray());
        if (scenario == "hosted-reference")
            Assert.Equal("verification-coverage-reference-invalid", Text(coverage, "cause"));
    }

    [Fact]
    public void NoMarkerSummaryPreservesBothLegacyFormatsAndKindsWithUnrelatedMalformedLocalFiles()
    {
        foreach (var (kind, outcome) in new[] { ("issue-to-pr", "pr-created"), ("pr-comment-fix", "repair-pushed") })
        foreach (var format in new[] { "text", "json" })
        {
            using var host = new VerificationEvidenceCommandTests.TempHost(Unit, "not: [valid packet");
            var context = Context(host.Root);
            var args = LegacySummaryArgs(kind, outcome, format);
            var before = RunSummary(context, args);
            Assert.Equal(0, before.Exit);

            var poisonedRecord = Path.Combine(host.EvidenceRoot, Domain, Team, Unit, "unrelated.json");
            Directory.CreateDirectory(Path.GetDirectoryName(poisonedRecord)!);
            File.WriteAllText(poisonedRecord, "{ not canonical evidence");
            var after = RunSummary(context, args);
            Assert.Equal(0, after.Exit);
            Assert.Equal(before.Output, after.Output);
            Assert.DoesNotContain("verification_coverage", after.Output, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("--kind", "unknown", "--repo", Repo, "--outcome", "unknown", "--format", "markdown")]
    [InlineData("--kind", "issue-to-pr", "--repo", Repo, "--outcome", "pr-created", "--pr-body", "--verification-report", "--format", "markdown")]
    public void NoMarkerMarkdownRefusalKeepsLegacyPriorityAndDoesNotActivateOnPrBodyValue(params string[] args)
    {
        using var host = new VerificationEvidenceCommandTests.TempHost(Unit, "malformed packet");
        var (exit, output) = RunSummary(Context(host.Root), args);
        Assert.Equal(1, exit);
        Assert.Equal("--format must be 'text' or 'json' (got 'markdown')." + Environment.NewLine, output);
        Assert.False(Directory.Exists(host.EvidenceRoot));
    }

    [Theory]
    [InlineData("--repo", Repo, "--repo", Repo)]
    [InlineData("--format", "json", "--format", "json")]
    public void OptInDuplicateExistingSingletonsRefuseBeforePacketOrStoreReads(params string[] duplicate)
    {
        using var host = new VerificationEvidenceCommandTests.TempHost(Unit, Packet);
        string[] args = ["--verification-report", "--kind", "issue-to-pr", "--repo", Repo,
            "--issue", "1895", "--pr", "1900", "--outcome", "pr-created", .. duplicate,
            "--verification-unit", Unit, "--verification-domain", Domain, "--verification-team", Team,
            "--verification-head-sha", Head, "--verification-packet-sha256", new string('0', 64), "--format", "json"];
        var result = RunSummary(Context(host.Root), args);
        Assert.Equal(1, result.Exit);
        Assert.Contains("Duplicate argument", result.Output, StringComparison.Ordinal);
        Assert.False(Directory.Exists(host.EvidenceRoot));
    }

    [Theory]
    [InlineData("issue-to-pr", "pr-created")]
    [InlineData("pr-comment-fix", "repair-pushed")]
    public void ResultSummaryInvalidVerificationGroupsRefuseBeforeCoverageReads(string kind, string outcome)
    {
        using var host = new VerificationEvidenceCommandTests.TempHost(Unit, "not: [a valid packet");
        var packetSha = VerificationEvidenceCommandTests.Hash(host.PacketBytes);
        var baseArgs = SummaryArgs(kind, outcome, packetSha, [], "json");
        var seventeen = Enumerable.Range(0, 17)
            .SelectMany(index => new[] { "--verification-evidence", $"P{index}=E{index}" }).ToArray();
        var cases = new List<(string Name, string[] Args)>
        {
            ("missing-member", RemoveOption(baseArgs, "--verification-team")),
            ("stray-without-marker", [.. LegacySummaryArgs(kind, outcome, "json"), "--verification-unit", Unit]),
            ("invalid-unit", ReplaceValue(baseArgs, "--verification-unit", "G/867")),
            ("invalid-repo", ReplaceValue(baseArgs, "--repo", "owner/repo/extra")),
            ("invalid-packet-hash", ReplaceValue(baseArgs, "--verification-packet-sha256", "bad")),
            ("malformed-reference", [.. SummaryArgs(kind, outcome, packetSha,
                ["--verification-evidence", "P1==E1"], "json")]),
            ("case-aliased-reference", [.. SummaryArgs(kind, outcome, packetSha,
                ["--verification-evidence", "P1=E1", "--verification-evidence", "p1=E2"], "json")]),
            ("reference-over-cap", [.. LegacySummaryArgs(kind, outcome, "json"), "--verification-report",
                "--verification-unit", Unit, "--verification-domain", Domain, "--verification-team", Team,
                "--verification-head-sha", Head, "--verification-packet-sha256", packetSha, .. seventeen]),
        };

        foreach (var scenario in cases)
        {
            var result = RunSummary(Context(host.Root), scenario.Args);
            Assert.Equal(1, result.Exit);
            Assert.DoesNotContain("verification_coverage", result.Output, StringComparison.Ordinal);
            Assert.DoesNotContain("packet-unavailable", result.Output, StringComparison.Ordinal);
            Assert.False(Directory.Exists(host.EvidenceRoot));
        }
    }

    private static string[] CoverageArgs(VerificationEvidenceCommandTests.TempHost host, string packetSha,
        IEnumerable<string> references) =>
    ["--execution-unit", Unit, "--domain", Domain, "--team", Team, "--repo", Repo,
     "--head-sha", Head, "--packet-sha256", packetSha, .. references, "--format", "json"];

    private static string[] ReplaceValue(string[] args, string flag, string value)
    {
        var index = Array.IndexOf(args, flag);
        Assert.True(index >= 0 && index + 1 < args.Length, $"test fixture must contain {flag}");
        var result = (string[])args.Clone();
        result[index + 1] = value;
        return result;
    }

    private static string[] RemoveOption(string[] args, string flag)
    {
        var index = Array.IndexOf(args, flag);
        Assert.True(index >= 0 && index + 1 < args.Length, $"test fixture must contain {flag}");
        return args.Where((_, position) => position != index && position != index + 1).ToArray();
    }

    private static string[] CoverageArgsForRoot(string root, string packetSha,
        IEnumerable<string> references) =>
    ["--execution-unit", Unit, "--domain", Domain, "--team", Team, "--repo", Repo,
     "--head-sha", Head, "--packet-sha256", packetSha, .. references, "--format", "json"];

    private static (int Exit, string Output) RunCoverage(string root, string[] args, string format = "json",
        Action<string, string>? beforeOperation = null)
    {
        if (args.Length > 0)
        {
            var formatIndex = Array.IndexOf(args, "--format");
            if (formatIndex >= 0 && formatIndex + 1 < args.Length) args[formatIndex + 1] = format;
            else args = [.. args, "--format", format];
        }
        using var writer = new StringWriter();
        var exit = VerificationEvidenceCoverage.Execute(root, args, writer, beforeOperation);
        return (exit, writer.ToString());
    }

    private static JsonElement ParseCoverage(string output, string format)
    {
        if (format == "json")
        {
            using var document = JsonDocument.Parse(output);
            return document.RootElement.Clone();
        }
        var marker = "```json" + Environment.NewLine;
        var start = output.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(start >= 0, "coverage Markdown must include its structured JSON projection");
        start += marker.Length;
        var end = output.IndexOf(Environment.NewLine + "```", start, StringComparison.Ordinal);
        Assert.True(end >= 0, "coverage Markdown JSON fence must be closed");
        using var json = JsonDocument.Parse(output[start..end]);
        return json.RootElement.Clone();
    }

    private static string Text(JsonElement parent, string property) => parent.GetProperty(property).GetString()!;

    private static CliContext Context(string root) => new()
    {
        RepoRoot = root,
        Config = new CliConfig { Project = new ProjectConfig { Domain = Domain, ArtifactRoot = ".intent-cli" } },
    };

    private static string[] LegacySummaryArgs(string kind, string outcome, string format) => kind switch
    {
        "issue-to-pr" => ["--kind", kind, "--repo", Repo, "--issue", "1895", "--pr", "1900", "--outcome", outcome, "--format", format],
        _ => ["--kind", kind, "--repo", Repo, "--pr", "1900", "--outcome", outcome, "--format", format],
    };

    private static string[] SummaryArgs(string kind, string outcome, string packetSha,
        IReadOnlyList<string> references, string format)
    {
        var baseArgs = LegacySummaryArgs(kind, outcome, format).ToList();
        var marker = Array.IndexOf(baseArgs.ToArray(), "--format");
        baseArgs.Insert(marker, "--verification-report");
        baseArgs.InsertRange(marker + 1, [
            "--verification-unit", Unit,
            "--verification-domain", Domain,
            "--verification-team", Team,
            "--verification-head-sha", Head,
            "--verification-packet-sha256", packetSha,
            .. references,
        ]);
        return baseArgs.ToArray();
    }

    private static string WriteActualRecord(VerificationEvidenceCommandTests.TempHost host,
        string proofId, string recordId, byte[] trx)
    {
        var trxPath = $"capture-{recordId}.trx";
        host.WriteTrx(trxPath, trx);
        var packetSha = VerificationEvidenceCommandTests.Hash(host.PacketBytes);
        var (exit, output) = VerificationEvidenceCommandTests.Invoke(host,
            [.. VerificationEvidenceCommandTests.RecordArgs(host, "json", packetSha, trxPath,
                id: recordId, unit: Unit, proof: proofId), "--write"]);
        using var result = JsonDocument.Parse(output);
        Assert.Equal(0, exit);
        Assert.Equal("recorded", Text(result.RootElement, "disposition"));
        Assert.True(result.RootElement.GetProperty("wrote").GetBoolean());
        return host.Absolute(Text(result.RootElement, "artifact_path"));
    }

    private static byte[] AlternatePacket(string mutation)
    {
        var changed = mutation switch
        {
            "criterion-index" => Packet
            .Replace("criterion_index: 1", "criterion_index: 99", StringComparison.Ordinal)
            .Replace("criterion_index: 2", "criterion_index: 1", StringComparison.Ordinal)
            .Replace("criterion_index: 99", "criterion_index: 2", StringComparison.Ordinal),
            "proof-reference" => Packet
            .Replace("acceptance_criteria: [AC1]", "acceptance_criteria: [__swap__]", StringComparison.Ordinal)
            .Replace("acceptance_criteria: [AC2]", "acceptance_criteria: [AC1]", StringComparison.Ordinal)
            .Replace("acceptance_criteria: [__swap__]", "acceptance_criteria: [AC2]", StringComparison.Ordinal),
            "proof-lane" => Packet
            .Replace("lane: unit", "lane: __swap__", StringComparison.Ordinal)
            .Replace("lane: build", "lane: unit", StringComparison.Ordinal)
            .Replace("lane: __swap__", "lane: build", StringComparison.Ordinal),
            "proof-command" => ChangeFirstCommand(Packet),
            "criterion-text" => Packet.Replace("Preserve independently ordered criterion one.",
                "Preserve independently ordered criterion one with changed text.", StringComparison.Ordinal),
            "lane-weight" => Packet.Replace("id: unit, weight: ordinary", "id: unit, weight: heavy", StringComparison.Ordinal),
            _ => throw new ArgumentOutOfRangeException(nameof(mutation), mutation, null),
        };
        return Encoding.UTF8.GetBytes(changed + "\n");
    }

    private static string ChangeFirstCommand(string packet)
    {
        const string current = "command: dotnet test";
        var position = packet.IndexOf(current, StringComparison.Ordinal);
        Assert.True(position >= 0);
        return packet[..position] + "command: dotnet test --no-restore" + packet[(position + current.Length)..];
    }

    private static string RemoveLineContaining(string text, string fragment)
    {
        var position = text.IndexOf(fragment, StringComparison.Ordinal);
        Assert.True(position >= 0);
        var start = text.LastIndexOf('\n', position) + 1;
        var end = text.IndexOf('\n', position);
        return text.Remove(start, (end < 0 ? text.Length : end + 1) - start);
    }

    private static string BuildPacketWithProofs(int count)
    {
        var text = new StringBuilder()
            .AppendLine("implementation_issue_packet:")
            .AppendLine("  source_execution_unit: G867")
            .AppendLine("  domain: intent-cli")
            .AppendLine("  team: intent-cli-dev")
            .AppendLine("  target_repo: J-Tech-Japan/intent-system")
            .AppendLine("  acceptance_criteria:");
        for (var index = 1; index <= count; index++)
            text.Append("    - criterion ").Append(index).AppendLine(" is structurally registered.");
        text.AppendLine("verification_contract:")
            .AppendLine("  version: 1")
            .AppendLine("  acceptance_criteria:");
        for (var index = 1; index <= count; index++)
            text.Append("    - {id: AC").Append(index).Append(", criterion_index: ").Append(index).AppendLine("}");
        text.AppendLine("  lanes:")
            .AppendLine("    - {id: unit, weight: ordinary}")
            .AppendLine("  affected_lanes: [unit]")
            .AppendLine("  proofs:");
        for (var index = 1; index <= count; index++)
        {
            text.Append("    - id: P").Append(index).AppendLine()
                .Append("      acceptance_criteria: [AC").Append(index).AppendLine("]")
                .AppendLine("      lane: unit")
                .AppendLine("      command: dotnet test")
                .AppendLine("      evidence: local-required");
        }
        return text.ToString();
    }

    private static byte[] MakeCapturedTrxAllPassing(byte[] bytes)
    {
        const string nsText = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";
        XNamespace ns = nsText;
        var document = XDocument.Parse(Encoding.UTF8.GetString(bytes).TrimStart('\uFEFF'));
        var root = document.Root!;
        var results = root.Element(ns + "Results")!.Elements(ns + "UnitTestResult").ToArray();
        foreach (var result in results)
            if ((string?)result.Attribute("outcome") == "NotExecuted") result.SetAttributeValue("outcome", "Passed");
        var counters = root.Element(ns + "ResultSummary")!.Element(ns + "Counters")!;
        counters.SetAttributeValue("total", results.Length);
        counters.SetAttributeValue("executed", results.Length);
        counters.SetAttributeValue("passed", results.Length);
        counters.SetAttributeValue("failed", 0);
        counters.SetAttributeValue("notExecuted", 0);
        root.Element(ns + "ResultSummary")!.SetAttributeValue("outcome", "Completed");
        return Encoding.UTF8.GetBytes(document.ToString(SaveOptions.DisableFormatting));
    }

    private static byte[] MutateFailedAndSkipped(byte[] bytes)
    {
        const string nsText = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";
        XNamespace ns = nsText;
        var document = XDocument.Parse(Encoding.UTF8.GetString(bytes).TrimStart('\uFEFF'));
        var root = document.Root!;
        var results = root.Element(ns + "Results")!.Elements(ns + "UnitTestResult").ToArray();
        Assert.True(results.Length >= 2);
        results[0].SetAttributeValue("outcome", "Failed");
        results[1].SetAttributeValue("outcome", "NotExecuted");
        var summary = root.Element(ns + "ResultSummary")!;
        summary.SetAttributeValue("outcome", "Failed");
        var counters = summary.Element(ns + "Counters")!;
        counters.SetAttributeValue("total", results.Length);
        counters.SetAttributeValue("executed", results.Length - 1);
        counters.SetAttributeValue("passed", results.Length - 2);
        counters.SetAttributeValue("failed", 1);
        counters.SetAttributeValue("notExecuted", 0);
        return Encoding.UTF8.GetBytes(document.ToString(SaveOptions.DisableFormatting));
    }

    private static void AssertCoverageRow((int Exit, string Output) result, string expectedTop,
        string expectedRow, string expectedCause)
    {
        using var json = JsonDocument.Parse(result.Output);
        Assert.Equal(expectedTop == "complete" ? 0 : 1, result.Exit);
        Assert.Equal(expectedTop, Text(json.RootElement, "state"));
        var row = json.RootElement.GetProperty("proofs")[0];
        Assert.Equal(expectedRow, Text(row, "state"));
        Assert.Equal(expectedCause, Text(row, "cause"));
        if (expectedRow == "present")
        {
            Assert.Equal("current", Text(row, "binding_state"));
            Assert.NotNull(row.GetProperty("artifact_path").GetString());
            Assert.NotNull(row.GetProperty("artifact_sha256").GetString());
            Assert.NotNull(row.GetProperty("trx_sha256").GetString());
            Assert.Equal(JsonValueKind.Object, row.GetProperty("report").ValueKind);
        }
        else
        {
            Assert.Equal("unknown", Text(row, "binding_state"));
            Assert.Null(row.GetProperty("artifact_path").GetString());
            Assert.Null(row.GetProperty("artifact_sha256").GetString());
            Assert.Null(row.GetProperty("trx_sha256").GetString());
            Assert.Equal(JsonValueKind.Null, row.GetProperty("report").ValueKind);
        }
    }

    private static void AssertReadOnly(string root, GitSnapshot beforeGit, string[] beforeTree)
    {
        Assert.Equal(beforeTree, SnapshotTree(root));
        var afterGit = SnapshotGit(root);
        Assert.Equal(beforeGit.Index, afterGit.Index);
        Assert.Equal(beforeGit.Head, afterGit.Head);
        Assert.Equal(beforeGit.Refs, afterGit.Refs);
    }

    private sealed record GitSnapshot(byte[] Index, string Head, string Refs);

    private static GitSnapshot SnapshotGit(string root)
    {
        return new GitSnapshot(File.ReadAllBytes(Path.Combine(root, ".git", "index")),
            RunGit(root, "rev-parse", "HEAD").Trim(), RunGit(root, "for-each-ref", "--format=%(refname) %(objectname)"));
    }

    private static string[] SnapshotTree(string root)
    {
        var gitDirectory = Path.GetFullPath(Path.Combine(root, ".git")) + Path.DirectorySeparatorChar;
        var entries = new List<string>();
        foreach (var directory in Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories))
        {
            var full = Path.GetFullPath(directory);
            if (full.StartsWith(gitDirectory, StringComparison.Ordinal)) continue;
            entries.Add("D " + Path.GetRelativePath(root, directory).Replace('\\', '/'));
        }
        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            var full = Path.GetFullPath(file);
            if (full.StartsWith(gitDirectory, StringComparison.Ordinal)) continue;
            entries.Add("F " + Path.GetRelativePath(root, file).Replace('\\', '/') + " "
                + VerificationEvidenceCommandTests.Hash(File.ReadAllBytes(file)));
        }
        return entries.OrderBy(item => item, StringComparer.Ordinal).ToArray();
    }

    private static void ConfigureGit(string root)
    {
        RunGit(root, "config", "user.name", "G867 test operator");
        RunGit(root, "config", "user.email", "g867-test@example.invalid");
    }

    private static string RunGit(string workingDirectory, params string[] arguments)
    {
        var start = new ProcessStartInfo("git")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("git did not start");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(15000))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException("bounded G867 test Git command timed out");
        }
        Task.WaitAll(stdout, stderr);
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"git {string.Join(' ', arguments)} failed: {stderr.Result}");
        return stdout.Result;
    }

    private static (int Exit, string Output) RunSummary(CliContext context, string[] args)
    {
        using var writer = new StringWriter();
        var exit = WorkerResultSummaryCommand.Execute(context, args, writer);
        return (exit, writer.ToString());
    }
}
