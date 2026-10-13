using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Xml.Linq;
using IntentSystem.Cli.Commands;

namespace IntentSystem.Cli.Tests;

public sealed class VerificationEvidenceCommandTests
{
    internal const string Unit = "G866";
    internal const string Domain = "intent-cli";
    internal const string Team = "intent-cli-dev";
    internal const string Repo = "J-Tech-Japan/intent-system";
    internal const string Head = "0b21df035c2f9f19935f2e09b3300c4eedb2e0d2";
    internal const string Id = "E1";
    private const string SmallTrxResource = "g866-small-vstest.trx";
    private const string PublicationGuidance = "Publication is not verified by this command. To make a local record visible in a fresh clone, stage the artifact and commit/plain-push it yourself.";

    [Theory]
    [InlineData("json")]
    [InlineData("markdown")]
    public void ActualVstestBytesPreviewWriteShowValidateAndReplayAreBounded(string format)
    {
        using var host = new TempHost();
        var trx = ReadSmallVstest();
        host.WriteTrx("capture.trx", trx);
        var packetSha = Hash(host.PacketBytes);
        var args = RecordArgs(host, format, packetSha, "capture.trx");

        var (previewExit, previewText) = Invoke(host, args);
        var preview = Result(previewText, format);
        Assert.Equal(0, previewExit);
        Assert.Equal("current", Text(preview, "state"));
        Assert.Equal("planned", Text(preview, "disposition"));
        Assert.Equal(PublicationGuidance, Text(preview, "publication_guidance"));
        Assert.Contains(PublicationGuidance, previewText, StringComparison.Ordinal);
        Assert.False(preview.GetProperty("wrote").GetBoolean());
        Assert.Equal(JsonValueKind.Null, preview.GetProperty("artifact_sha256").ValueKind);
        Assert.Equal(".intent-cli/verification-evidence/intent-cli/intent-cli-dev/G866/E1.json",
            Text(preview, "planned_artifact_path"));
        Assert.Equal(Hash(trx), Text(preview, "trx_sha256"));
        Assert.False(Directory.Exists(host.EvidenceRoot));

        var (writeExit, writeText) = Invoke(host, [.. args, "--write"]);
        var written = Result(writeText, format);
        Assert.Equal(0, writeExit);
        Assert.Equal("recorded", Text(written, "disposition"));
        Assert.Equal(PublicationGuidance, Text(written, "publication_guidance"));
        Assert.True(written.GetProperty("wrote").GetBoolean());
        var relative = Text(written, "artifact_path");
        var artifactBytes = File.ReadAllBytes(host.Absolute(relative));
        Assert.NotEmpty(artifactBytes);
        Assert.Equal((byte)'\n', artifactBytes[^1]);
        Assert.DoesNotContain((byte)'\r', artifactBytes);
        using var artifactDocument = JsonDocument.Parse(artifactBytes);
        Assert.Equal(trx, Convert.FromBase64String(Text(artifactDocument.RootElement, "trx_base64")));
        Assert.Equal(Hash(artifactBytes), Text(written, "artifact_sha256"));
        Assert.Equal(Hash(trx), Text(written, "trx_sha256"));
        Assert.Equal("not-observed", Text(written, "execution"));
        Assert.Equal("not-authenticated", Text(written, "producer"));
        Assert.Equal("not-assessed", Text(written, "acceptance"));
        Assert.Equal("not-verified", Text(written, "publication"));
        Assert.Equal("caller-supplied-not-authenticated", Text(written, "head_binding"));
        Assert.Equal("P1", Text(written.GetProperty("binding"), "proof_id"));
        Assert.Equal("AC1", Text(written.GetProperty("binding").GetProperty("acceptance_criteria")[0], "id"));

        foreach (var operation in new[] { "show", "validate" })
        {
            var (exit, output) = Invoke(host, ReadArgs(operation, format));
            var result = Result(output, format);
            Assert.Equal(0, exit);
            Assert.Equal("current", Text(result, "state"));
            Assert.Equal(PublicationGuidance, Text(result, "publication_guidance"));
            Assert.Equal(relative, Text(result, "artifact_path"));
            Assert.Equal(Hash(artifactBytes), Text(result, "artifact_sha256"));
            Assert.Equal(Hash(trx), Text(result, "trx_sha256"));
        }

        host.WriteTrx("renamed-capture.trx", trx);
        var (replayExit, replayText) = Invoke(host, RecordArgs(host, format, packetSha, "renamed-capture.trx"));
        var replay = Result(replayText, format);
        Assert.Equal(0, replayExit);
        Assert.Equal("idempotent", Text(replay, "disposition"));
        Assert.False(replay.GetProperty("wrote").GetBoolean());
        Assert.True(replay.GetProperty("idempotent").GetBoolean());
        Assert.Equal(relative, Text(replay, "artifact_path"));
        Assert.Equal(Hash(artifactBytes), Hash(File.ReadAllBytes(host.Absolute(relative))));

        var (writeReplayExit, writeReplayText) = Invoke(host,
            [.. RecordArgs(host, format, packetSha, "renamed-capture.trx"), "--write"]);
        var writeReplay = Result(writeReplayText, format);
        Assert.Equal(0, writeReplayExit);
        Assert.Equal("idempotent", Text(writeReplay, "disposition"));
        Assert.False(writeReplay.GetProperty("wrote").GetBoolean());
        Assert.True(writeReplay.GetProperty("idempotent").GetBoolean());
        Assert.Equal(artifactBytes, File.ReadAllBytes(host.Absolute(relative)));
    }

    [Theory]
    [InlineData("json")]
    [InlineData("markdown")]
    public void CallerPacketDigestMismatchRefusesBeforeTrxRead(string format)
    {
        using var host = new TempHost();
        host.WriteTrx("capture.trx", ReadSmallVstest());
        var trxRead = 0;
        var args = RecordArgs(host, format, new string('0', 64), "capture.trx");
        var (exit, output) = Invoke(host, args, (operation, _) => { if (operation == "trx-read") trxRead++; });
        var result = Result(output, format);
        Assert.Equal(1, exit);
        Assert.Equal("stale", Text(result, "state"));
        Assert.Equal("verification-evidence-packet-changed", Text(result, "cause"));
        Assert.Equal(0, trxRead);
        Assert.False(Directory.Exists(host.EvidenceRoot));
    }

    [Theory]
    [InlineData("domain")]
    [InlineData("team")]
    [InlineData("unit")]
    public void RequestedPacketIdentityMismatchRefusesWithoutDiskProof(string field)
    {
        foreach (var format in new[] { "json", "markdown" })
        {
            var packet = field == "unit"
                ? ValidPacket.Replace("source_execution_unit: G866", "source_execution_unit: G999", StringComparison.Ordinal)
                : ValidPacket;
            using var host = new TempHost(packet: packet);
            host.WriteTrx("capture.trx", ReadSmallVstest());
            var args = field switch
            {
                "domain" => RecordArgs(host, format, Hash(host.PacketBytes), "capture.trx", domain: "other-domain"),
                "team" => RecordArgs(host, format, Hash(host.PacketBytes), "capture.trx", team: "other-team"),
                "unit" => RecordArgs(host, format, Hash(host.PacketBytes), "capture.trx"),
                _ => throw new ArgumentOutOfRangeException(nameof(field))
            };
            var trxReads = 0;
            var (exit, output) = Invoke(host, args, (operation, _) => { if (operation == "trx-read") trxReads++; });
            var result = Result(output, format);
            Assert.Equal(1, exit);
            Assert.Equal("conflict", Text(result, "state"));
            Assert.Equal("verification-evidence-identity-conflict", Text(result, "cause"));
            Assert.Null(result.GetProperty("artifact_path").GetString());
            Assert.Null(result.GetProperty("artifact_sha256").GetString());
            Assert.Equal(0, trxReads);
            Assert.False(Directory.Exists(host.EvidenceRoot));
        }
    }

    [Theory]
    [InlineData("json")]
    [InlineData("markdown")]
    public void SequentialDifferentTrxForSameIdentityConflictsAndPreservesFirstBytes(string format)
    {
        using var host = new TempHost();
        var firstTrx = ReadSmallVstest();
        var secondTrx = MutateSmallTrx("unicode-name");
        host.WriteTrx("first.trx", firstTrx);
        host.WriteTrx("second.trx", secondTrx);
        var packetSha = Hash(host.PacketBytes);
        var (firstExit, firstOutput) = Invoke(host,
            [.. RecordArgs(host, format, packetSha, "first.trx"), "--write"]);
        var first = Result(firstOutput, format);
        Assert.Equal(0, firstExit);
        var path = Text(first, "artifact_path");
        var originalBytes = File.ReadAllBytes(host.Absolute(path));
        var originalSha = Hash(originalBytes);

        var (secondExit, secondOutput) = Invoke(host,
            [.. RecordArgs(host, format, packetSha, "second.trx"), "--write"]);
        var second = Result(secondOutput, format);
        Assert.Equal(1, secondExit);
        Assert.Equal("conflict", Text(second, "state"));
        Assert.Equal("verification-evidence-record-conflict", Text(second, "cause"));
        Assert.Equal(path, Text(second, "artifact_path"));
        Assert.Equal(originalSha, Text(second, "artifact_sha256"));
        Assert.Equal(originalBytes, File.ReadAllBytes(host.Absolute(path)));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(host.Absolute(path))!, "*.tmp"));
    }

    [Theory]
    [InlineData("source_execution_unit", "missing", "conflict", "verification-evidence-identity-conflict")]
    [InlineData("source_execution_unit", "wrong-type", "conflict", "verification-evidence-identity-conflict")]
    [InlineData("source_execution_unit", "tagged", "conflict", "verification-evidence-identity-conflict")]
    [InlineData("source_execution_unit", "aliased", "conflict", "verification-evidence-identity-conflict")]
    [InlineData("source_execution_unit", "duplicate", "invalid", "verification-evidence-packet-invalid")]
    [InlineData("source_execution_unit", "case", "conflict", "verification-evidence-identity-conflict")]
    [InlineData("domain", "missing", "conflict", "verification-evidence-identity-conflict")]
    [InlineData("domain", "wrong-type", "conflict", "verification-evidence-identity-conflict")]
    [InlineData("domain", "tagged", "conflict", "verification-evidence-identity-conflict")]
    [InlineData("domain", "aliased", "conflict", "verification-evidence-identity-conflict")]
    [InlineData("domain", "key-aliased", "conflict", "verification-evidence-identity-conflict")]
    [InlineData("domain", "duplicate", "unavailable", "verification-evidence-packet-invalid")]
    [InlineData("domain", "case", "conflict", "verification-evidence-identity-conflict")]
    [InlineData("team", "missing", "conflict", "verification-evidence-identity-conflict")]
    [InlineData("team", "wrong-type", "conflict", "verification-evidence-identity-conflict")]
    [InlineData("team", "tagged", "conflict", "verification-evidence-identity-conflict")]
    [InlineData("team", "aliased", "conflict", "verification-evidence-identity-conflict")]
    [InlineData("team", "key-aliased", "conflict", "verification-evidence-identity-conflict")]
    [InlineData("team", "duplicate", "unavailable", "verification-evidence-packet-invalid")]
    [InlineData("team", "case", "conflict", "verification-evidence-identity-conflict")]
    [InlineData("target_repo", "missing", "conflict", "verification-evidence-identity-conflict")]
    [InlineData("target_repo", "wrong-type", "conflict", "verification-evidence-identity-conflict")]
    [InlineData("target_repo", "tagged", "conflict", "verification-evidence-identity-conflict")]
    [InlineData("target_repo", "aliased", "conflict", "verification-evidence-identity-conflict")]
    [InlineData("target_repo", "key-aliased", "conflict", "verification-evidence-identity-conflict")]
    [InlineData("target_repo", "duplicate", "unavailable", "verification-evidence-packet-invalid")]
    [InlineData("target_repo", "case", "conflict", "verification-evidence-identity-conflict")]
    public void PacketIdentityMutationsRefuseBeforeReadingTrx(string field, string mutation, string expectedState, string expectedCause)
    {
        var packet = MutateIdentityPacket(field, mutation);
        Assert.NotEqual(ValidPacket, packet);
        foreach (var format in new[] { "json", "markdown" })
        {
            using var host = new TempHost(packet: packet);
            host.WriteTrx("capture.trx", ReadSmallVstest());
            var trxReads = 0;
            var (exit, output) = Invoke(host,
                RecordArgs(host, format, Hash(host.PacketBytes), "capture.trx"),
                (operation, _) => { if (operation == "trx-read") trxReads++; });
            var result = Result(output, format);
            Assert.Equal(1, exit);
            Assert.True(Text(result, "state") == expectedState, $"{mutation}: {output}");
            Assert.Equal(expectedCause, Text(result, "cause"));
            Assert.Equal(0, trxReads);
            Assert.Equal(JsonValueKind.Null, result.GetProperty("artifact_path").ValueKind);
            Assert.False(Directory.Exists(host.EvidenceRoot));
        }
    }

    [Theory]
    [InlineData("json")]
    [InlineData("markdown")]
    public void RootIdentityKeyAliasRefusesBeforeReadingTrxWhileUnrelatedMetadataAliasIsAccepted(string format)
    {
        var rootKeyAlias = "metadata: &identityKey implementation_issue_packet\n" +
            ValidPacket.Replace("implementation_issue_packet:\n", "*identityKey:\n", StringComparison.Ordinal);
        using (var host = new TempHost(packet: rootKeyAlias))
        {
            host.WriteTrx("capture.trx", ReadSmallVstest());
            var trxReads = 0;
            var (exit, output) = Invoke(host, RecordArgs(host, format, Hash(host.PacketBytes), "capture.trx"),
                (operation, _) => { if (operation == "trx-read") trxReads++; });
            var result = Result(output, format);
            Assert.Equal(1, exit);
            Assert.Equal("conflict", Text(result, "state"));
            Assert.Equal("verification-evidence-identity-conflict", Text(result, "cause"));
            Assert.Equal(0, trxReads);
            Assert.False(Directory.Exists(host.EvidenceRoot));
        }

        var unrelatedAlias = ValidPacket + "\nmetadata: &ordinary note\nother_metadata: *ordinary\n";
        using var positive = new TempHost(packet: unrelatedAlias);
        positive.WriteTrx("capture.trx", ReadSmallVstest());
        var (positiveExit, positiveOutput) = Invoke(positive,
            RecordArgs(positive, format, Hash(positive.PacketBytes), "capture.trx"));
        Assert.Equal(0, positiveExit);
        Assert.Equal("planned", Text(Result(positiveOutput, format), "disposition"));
        Assert.False(Directory.Exists(positive.EvidenceRoot));
    }

    [Fact]
    public void ExactMaximumIdentityAndProofIdentifiersAreAcceptedByTheActualWriter()
    {
        var unit = new string('U', 128);
        var domain = new string('D', 128);
        var team = new string('T', 128);
        var proof = "P" + new string('x', 63);
        var packet = ValidPacket
            .Replace("source_execution_unit: G866", "source_execution_unit: " + unit, StringComparison.Ordinal)
            .Replace("domain: intent-cli", "domain: " + domain, StringComparison.Ordinal)
            .Replace("team: intent-cli-dev", "team: " + team, StringComparison.Ordinal)
            .Replace("id: P1", "id: " + proof, StringComparison.Ordinal);
        using var host = new TempHost(unit, packet);
        host.WriteTrx("capture.trx", ReadSmallVstest());
        var (exit, output) = Invoke(host,
            [.. RecordArgs(host, "json", Hash(host.PacketBytes), "capture.trx", unit: unit, domain: domain, team: team, proof: proof), "--write"]);
        var result = Result(output, "json");
        Assert.Equal(0, exit);
        Assert.Equal("recorded", Text(result, "disposition"));
        Assert.Equal(unit, Text(result, "execution_unit"));
        Assert.Equal(domain, Text(result, "domain"));
        Assert.Equal(team, Text(result, "team"));
        Assert.Equal(proof, Text(result.GetProperty("binding"), "proof_id"));
        Assert.True(File.Exists(host.Absolute(Text(result, "artifact_path"))));
    }

    [Theory]
    [InlineData("hash")]
    [InlineData("id")]
    [InlineData("proof")]
    [InlineData("head")]
    public void InvalidCallerIdentityAndDigestRefuseBeforePacketOrTrxRead(string invalidField)
    {
        foreach (var format in new[] { "json", "markdown" })
        {
            using var host = new TempHost();
            host.WriteTrx("capture.trx", ReadSmallVstest());
            var args = RecordArgs(host, format, Hash(host.PacketBytes), "capture.trx");
            switch (invalidField)
            {
                case "hash": args[Array.IndexOf(args, "--packet-sha256") + 1] = "ABC"; break;
                case "id": args[Array.IndexOf(args, "--id") + 1] = "../escape"; break;
                case "proof": args[Array.IndexOf(args, "--proof") + 1] = "bad/proof"; break;
                case "head": args[Array.IndexOf(args, "--head-sha") + 1] = "not-a-commit"; break;
            }
            var packetReads = 0;
            var trxReads = 0;
            var (exit, output) = Invoke(host, args, (operation, _) =>
            {
                if (operation == "packet-read") packetReads++;
                if (operation == "trx-read") trxReads++;
            });
            var result = Result(output, format);
            Assert.Equal(1, exit);
            Assert.Equal(invalidField == "id" ? "conflict" : "invalid", Text(result, "state"));
            Assert.Equal(invalidField == "id" ? "verification-evidence-identity-conflict" : "verification-evidence-argument-invalid",
                Text(result, "cause"));
            Assert.Equal(0, packetReads);
            Assert.Equal(0, trxReads);
            Assert.False(Directory.Exists(host.EvidenceRoot));
        }
    }

    [Theory]
    [InlineData("unknown-before-format", "markdown")]
    [InlineData("duplicate-id", "json")]
    [InlineData("missing-value-before-format", "markdown")]
    [InlineData("write-and-dry-run", "json")]
    [InlineData("duplicate-write", "markdown")]
    [InlineData("read-with-trx", "json")]
    [InlineData("read-with-proof", "markdown")]
    [InlineData("read-with-packet-sha", "json")]
    [InlineData("read-dry-run", "markdown")]
    public void InvalidFlagsAreStructuredAndRejectedBeforeAnyInputRead(string mutation, string format)
    {
        using var host = new TempHost();
        host.WriteTrx("capture.trx", ReadSmallVstest());
        byte[]? seededArtifact = null;
        if (mutation.StartsWith("read-", StringComparison.Ordinal))
        {
            var (seedExit, seedOutput) = Invoke(host,
                [.. RecordArgs(host, format, Hash(host.PacketBytes), "capture.trx"), "--write"]);
            Assert.Equal(0, seedExit);
            var seeded = Result(seedOutput, format);
            seededArtifact = File.ReadAllBytes(host.Absolute(Text(seeded, "artifact_path")));
        }
        var args = RecordArgs(host, format, Hash(host.PacketBytes), "capture.trx");
        switch (mutation)
        {
            case "unknown-before-format": args = [.. args[..^2], "--unknown", "x", "--format", format]; break;
            case "duplicate-id": args = [.. args, "--id", Id]; break;
            case "missing-value-before-format": args = [.. args, "--team", "--format", format]; break;
            case "write-and-dry-run": args = [.. args, "--write", "--dry-run"]; break;
            case "duplicate-write": args = [.. args, "--write", "--write"]; break;
            case "read-with-trx": args = [.. ReadArgs("show", format), "--trx", "capture.trx"]; break;
            case "read-with-proof": args = [.. ReadArgs("show", format), "--proof", "P1"]; break;
            case "read-with-packet-sha": args = [.. ReadArgs("validate", format), "--packet-sha256", Hash(host.PacketBytes)]; break;
            case "read-dry-run": args = [.. ReadArgs("show", format), "--dry-run"]; break;
            default: throw new ArgumentOutOfRangeException(nameof(mutation));
        }
        var reads = 0;
        var (exit, output) = Invoke(host, args, (_, _) => reads++);
        var result = Result(output, format);
        Assert.Equal(1, exit);
        Assert.Equal("invalid", Text(result, "state"));
        Assert.Equal("verification-evidence-argument-invalid", Text(result, "cause"));
        Assert.Equal("refused", Text(result, "disposition"));
        Assert.Equal(0, reads);
        if (seededArtifact is null) Assert.False(Directory.Exists(host.EvidenceRoot));
        else Assert.Equal(seededArtifact, File.ReadAllBytes(host.Absolute(".intent-cli/verification-evidence/intent-cli/intent-cli-dev/G866/E1.json")));
        Assert.DoesNotContain(host.Root, output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("head")]
    [InlineData("packet-byte")]
    [InlineData("proof-command")]
    [InlineData("criterion-text")]
    [InlineData("criterion-index")]
    [InlineData("proof-reference")]
    [InlineData("proof-lane")]
    [InlineData("proof-evidence")]
    public void CurrentContextMutationsPreserveVerifiedStoredFactsAndNeverRewrite(string mutation)
    {
        foreach (var format in new[] { "json", "markdown" })
        {
            using var host = new TempHost();
            var trx = ReadSmallVstest();
            host.WriteTrx("capture.trx", trx);
            var (writeExit, writeOutput) = Invoke(host,
                [.. RecordArgs(host, format, Hash(host.PacketBytes), "capture.trx"), "--write"]);
            Assert.Equal(0, writeExit);
            var written = Result(writeOutput, format);
            var relative = Text(written, "artifact_path");
            var artifactBytes = File.ReadAllBytes(host.Absolute(relative));
            var artifactSha = Hash(artifactBytes);
            var changedPacket = MutateCurrentPacket(mutation);
            if (changedPacket is not null)
            {
                Assert.NotEqual(host.PacketBytes, changedPacket);
                host.WritePacket(changedPacket);
            }

            var readHead = mutation == "head" ? new string('a', 40) : Head;
            var (showExit, showOutput) = Invoke(host, ReadArgs("show", format, readHead));
            var shown = Result(showOutput, format);
            var expected = mutation switch
            {
                "head" => ("stale", "verification-evidence-stale-head", 0),
                "criterion-index" or "proof-reference" or "proof-lane" => ("unavailable", "verification-evidence-packet-invalid", 1),
                "proof-evidence" => ("stale", "verification-evidence-stale-packet", 0),
                _ => ("stale", "verification-evidence-stale-packet", 0),
            };
            Assert.Equal(expected.Item3, showExit);
            Assert.Equal(expected.Item1, Text(shown, "state"));
            Assert.Equal(expected.Item2, Text(shown, "cause"));
            Assert.Equal(relative, Text(shown, "artifact_path"));
            Assert.Equal(artifactSha, Text(shown, "artifact_sha256"));
            Assert.Equal(Hash(trx), Text(shown, "trx_sha256"));
            Assert.Equal(Text(written.GetProperty("report"), "reported_outcome"),
                Text(shown.GetProperty("report"), "reported_outcome"));

            var (validateExit, validateOutput) = Invoke(host, ReadArgs("validate", format, readHead));
            var validated = Result(validateOutput, format);
            Assert.Equal(1, validateExit);
            Assert.Equal(expected.Item1, Text(validated, "state"));
            Assert.Equal(expected.Item2, Text(validated, "cause"));
            Assert.Equal(relative, Text(validated, "artifact_path"));
            Assert.Equal(artifactSha, Text(validated, "artifact_sha256"));
            Assert.Equal(artifactBytes, File.ReadAllBytes(host.Absolute(relative)));
        }
    }

    [Theory]
    [InlineData("criterion-index")]
    [InlineData("proof-reference")]
    [InlineData("proof-lane")]
    public void ValidAlternateCriterionAndLaneBindingsAreStaleButRetainHistoricalStoredFacts(string mutation)
    {
        var initialPacket = TwoCriterionPacket;
        var changedPacket = MutateValidAlternateBinding(initialPacket, mutation);
        Assert.NotEqual(initialPacket, changedPacket);
        Assert.Equal("complete", PacketVerificationContract.Evaluate(Unit,
            $".intent-cli/issues/{Unit}/packet.yaml", Encoding.UTF8.GetBytes(initialPacket)).State);
        Assert.Equal("complete", PacketVerificationContract.Evaluate(Unit,
            $".intent-cli/issues/{Unit}/packet.yaml", Encoding.UTF8.GetBytes(changedPacket)).State);
        foreach (var format in new[] { "json", "markdown" })
        {
            using var host = new TempHost(packet: initialPacket);
            var trx = ReadSmallVstest();
            host.WriteTrx("capture.trx", trx);
            var (writeExit, writeOutput) = Invoke(host,
                [.. RecordArgs(host, format, Hash(host.PacketBytes), "capture.trx", proof: "P1"), "--write"]);
            Assert.Equal(0, writeExit);
            var written = Result(writeOutput, format);
            var artifactPath = Text(written, "artifact_path");
            var artifactBytes = File.ReadAllBytes(host.Absolute(artifactPath));
            var artifactSha = Hash(artifactBytes);
            host.WritePacket(Encoding.UTF8.GetBytes(changedPacket + "\n"));

            foreach (var operation in new[] { "show", "validate" })
            {
                var (exit, output) = Invoke(host, ReadArgs(operation, format));
                var result = Result(output, format);
                Assert.Equal(operation == "show" ? 0 : 1, exit);
                Assert.Equal("stale", Text(result, "state"));
                Assert.Equal("verification-evidence-stale-packet", Text(result, "cause"));
                Assert.Equal(artifactPath, Text(result, "artifact_path"));
                Assert.Equal(artifactSha, Text(result, "artifact_sha256"));
                Assert.Equal(Hash(trx), Text(result, "trx_sha256"));
                Assert.Equal(Text(written.GetProperty("binding"), "proof_id"),
                    Text(result.GetProperty("binding"), "proof_id"));
                Assert.Equal(Text(written.GetProperty("report"), "reported_outcome"),
                    Text(result.GetProperty("report"), "reported_outcome"));
                Assert.Equal(artifactBytes, File.ReadAllBytes(host.Absolute(artifactPath)));
            }
        }
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("hosted")]
    public void MissingOrHostedSelectedProofRefusesBeforeTrxRead(string selectedProof)
    {
        var packet = selectedProof == "hosted"
            ? ValidPacket.Replace("evidence: local-required", "evidence: hosted-required", StringComparison.Ordinal)
            : ValidPacket;
        using var host = new TempHost(packet: packet);
        host.WriteTrx("capture.trx", ReadSmallVstest());
        var trxReads = 0;
        var proof = selectedProof == "missing" ? "P-MISSING" : "P1";
        var (exit, output) = Invoke(host,
            RecordArgs(host, "markdown", Hash(host.PacketBytes), "capture.trx", proof: proof),
            (operation, _) => { if (operation == "trx-read") trxReads++; });
        var result = Result(output, "markdown");
        Assert.Equal(1, exit);
        Assert.Equal("verification-evidence-proof-unsupported", Text(result, "cause"));
        Assert.Equal(0, trxReads);
        Assert.False(Directory.Exists(host.EvidenceRoot));
    }

    [Theory]
    [InlineData("json")]
    [InlineData("markdown")]
    public void IncompletePacketAndNoncontiguousSelectedCriterionRemainRecordableAndReadable(string format)
    {
        var packet = ValidPacket
            .Replace("- Preserve supplied local test bytes", "- Preserve supplied local test bytes\n    - Second criterion", StringComparison.Ordinal)
            .Replace("- {id: AC1, criterion_index: 1}",
                "- {id: AC1, criterion_index: 1}\n    - {id: AC2, criterion_index: 2}",
                StringComparison.Ordinal)
            .Replace("- id: P1\n", "- id: P2\n", StringComparison.Ordinal)
            .Replace("acceptance_criteria: [AC1]", "acceptance_criteria: [AC2]", StringComparison.Ordinal);
        Assert.NotEqual(ValidPacket, packet);
        using var host = new TempHost(packet: packet);
        host.WriteTrx("capture.trx", ReadSmallVstest());
        var (exit, output) = Invoke(host,
            RecordArgs(host, format, Hash(host.PacketBytes), "capture.trx", proof: "P2"));
        var result = Result(output, format);
        Assert.True(exit == 0, output);
        Assert.Equal("planned", Text(result, "disposition"));
        var boundCriterion = result.GetProperty("binding").GetProperty("acceptance_criteria")[0];
        Assert.Equal("AC2", Text(boundCriterion, "id"));
        Assert.Equal(2, boundCriterion.GetProperty("criterion_index").GetInt32());
        Assert.Equal("Second criterion", Text(boundCriterion, "text"));
        Assert.False(Directory.Exists(host.EvidenceRoot));

        var (writeExit, writeText) = Invoke(host,
            [.. RecordArgs(host, format, Hash(host.PacketBytes), "capture.trx", proof: "P2"), "--write"]);
        var written = Result(writeText, format);
        Assert.Equal(0, writeExit);
        Assert.Equal("recorded", Text(written, "disposition"));
        var artifactPath = Text(written, "artifact_path");
        var artifactBytes = File.ReadAllBytes(host.Absolute(artifactPath));
        using (var stored = JsonDocument.Parse(artifactBytes))
        {
            var storedCriterion = stored.RootElement.GetProperty("binding").GetProperty("acceptance_criteria")[0];
            Assert.Equal("AC2", Text(storedCriterion, "id"));
            Assert.Equal(2, storedCriterion.GetProperty("criterion_index").GetInt32());
            Assert.Equal("Second criterion", Text(storedCriterion, "text"));
        }
        foreach (var operation in new[] { "show", "validate" })
        {
            var (readExit, readText) = Invoke(host, ReadArgs(operation, format));
            var read = Result(readText, format);
            Assert.Equal(0, readExit);
            Assert.Equal("current", Text(read, "state"));
            Assert.Equal(artifactPath, Text(read, "artifact_path"));
            Assert.Equal(Hash(artifactBytes), Text(read, "artifact_sha256"));
            Assert.Equal("AC2", Text(read.GetProperty("binding").GetProperty("acceptance_criteria")[0], "id"));
        }
    }

    [Theory]
    [InlineData("json")]
    [InlineData("markdown")]
    public void PacketChangeAtReachedPrepublicationBoundaryLeavesNoFinalArtifact(string format)
    {
        using var host = new TempHost();
        var trx = ReadSmallVstest();
        host.WriteTrx("capture.trx", trx);
        var packetSha = Hash(host.PacketBytes);
        var changed = Encoding.UTF8.GetBytes(ValidPacket + "\n# changed after validation\n");
        var reached = false;
        var (exit, output) = Invoke(host, [.. RecordArgs(host, format, packetSha, "capture.trx"), "--write"],
            (operation, _) =>
            {
                if (operation == "before-publish")
                {
                    reached = true;
                    File.WriteAllBytes(host.PacketPath, changed);
                }
            });
        var result = Result(output, format);
        Assert.True(reached);
        Assert.Equal(1, exit);
        Assert.Equal("stale", Text(result, "state"));
        Assert.Equal("verification-evidence-packet-changed", Text(result, "cause"));
        Assert.False(result.GetProperty("wrote").GetBoolean());
        Assert.False(File.Exists(host.Absolute(".intent-cli/verification-evidence/intent-cli/intent-cli-dev/G866/E1.json")));
        Assert.False(Directory.Exists(host.EvidenceRoot));
    }

    [Theory]
    [InlineData("json")]
    [InlineData("markdown")]
    public void PacketRecheckRefusalRetainsCleanupFailureWithoutClaimingPublication(string format)
    {
        using var host = new TempHost();
        host.WriteTrx("capture.trx", ReadSmallVstest());
        var changedPacket = Encoding.UTF8.GetBytes(ValidPacket + "\n# changed before publication\n");
        string? temporaryPath = null;
        var reachedRecheck = false;
        var (exit, output) = Invoke(host,
            [.. RecordArgs(host, format, Hash(host.PacketBytes), "capture.trx"), "--write"],
            (operation, path) =>
            {
                if (operation == "before-publish")
                {
                    reachedRecheck = true;
                    File.WriteAllBytes(host.PacketPath, changedPacket);
                }
                else if (operation == "temp-cleanup")
                {
                    temporaryPath = path;
                    throw new IOException("injected cleanup failure before publication");
                }
            });
        var result = Result(output, format);
        Assert.True(reachedRecheck);
        Assert.NotNull(temporaryPath);
        Assert.Equal(1, exit);
        Assert.Equal("stale", Text(result, "state"));
        Assert.Equal("verification-evidence-packet-changed", Text(result, "cause"));
        Assert.False(result.GetProperty("wrote").GetBoolean());
        var cleanup = result.GetProperty("diagnostics").EnumerateArray()
            .Single(item => Text(item, "cause") == "verification-evidence-owned-temp-cleanup");
        Assert.Contains("publication did not complete", Text(cleanup, "detail"), StringComparison.Ordinal);
        Assert.DoesNotContain("published record is complete", Text(cleanup, "detail"), StringComparison.Ordinal);
        var relativeTemp = Path.GetRelativePath(host.Root, temporaryPath!).Replace(Path.DirectorySeparatorChar, '/');
        Assert.Contains(relativeTemp, result.GetProperty("residual_owned_paths").EnumerateArray()
            .Select(item => item.GetString()));
        Assert.False(File.Exists(host.Absolute(".intent-cli/verification-evidence/intent-cli/intent-cli-dev/G866/E1.json")));
    }

    [Theory]
    [InlineData("json")]
    [InlineData("markdown")]
    public void StoreAncestorSwapAtEveryPublicationPhaseNeverTouchesReplacementOrOutside(string format)
    {
        foreach (var phase in new[] { "temp-create", "before-publish", "atomic-create", "temp-cleanup" })
        {
            using var host = new TempHost();
            using var outside = new TempHost();
            var trx = ReadSmallVstest();
            host.WriteTrx("capture.trx", trx);
            var movedStore = host.Absolute("moved-original-store");
            var outsideTarget = Path.Combine(outside.EvidenceRoot, Domain, Team, Unit, Id + ".json");
            Directory.CreateDirectory(Path.GetDirectoryName(outsideTarget)!);
            var outsideTargetBytes = Encoding.UTF8.GetBytes("external target sentinel\n");
            File.WriteAllBytes(outsideTarget, outsideTargetBytes);
            var outsideTempBytes = Encoding.UTF8.GetBytes("external temp sentinel\n");
            string? outsideTemp = null;
            string? replacementTemp = null;
            string? originalTemp = null;
            var swapped = false;
            var replacedWithRegularDirectory = false;
            var reached = false;

            void SwapToOutside(string tempPath)
            {
                reached = true;
                var relativeTemp = Path.GetRelativePath(host.EvidenceRoot, tempPath);
                var tempWasCreated = File.Exists(tempPath);
                Directory.Move(host.EvidenceRoot, movedStore);
                if (tempWasCreated) originalTemp = Path.Combine(movedStore, relativeTemp);
                Directory.CreateSymbolicLink(host.EvidenceRoot, outside.EvidenceRoot);
                outsideTemp = Path.Combine(outside.EvidenceRoot, relativeTemp);
                Directory.CreateDirectory(Path.GetDirectoryName(outsideTemp)!);
                File.WriteAllBytes(outsideTemp, outsideTempBytes);
                swapped = true;
            }

            var (exit, output) = Invoke(host,
                [.. RecordArgs(host, format, Hash(host.PacketBytes), "capture.trx"), "--write"],
                (operation, path) =>
                {
                    if (operation == phase)
                    {
                        var tempPath = operation is "temp-create" or "temp-cleanup"
                            ? path
                            : Directory.GetFiles(host.EvidenceRoot, "*.tmp", SearchOption.AllDirectories).Single();
                        SwapToOutside(tempPath);
                    }
                    else if (operation == "temp-cleanup" && swapped && (phase is "before-publish" or "atomic-create")
                        && !replacedWithRegularDirectory)
                    {
                        // The original scope was moved and a symlink was already detected. A later
                        // regular directory at the old pathname is a replacement, not an owned path.
                        Directory.Delete(host.EvidenceRoot);
                        Directory.CreateDirectory(host.EvidenceRoot);
                        replacementTemp = Path.Combine(host.EvidenceRoot,
                            Path.GetRelativePath(movedStore, originalTemp!));
                        Directory.CreateDirectory(Path.GetDirectoryName(replacementTemp)!);
                        File.WriteAllBytes(replacementTemp, Encoding.UTF8.GetBytes("replacement directory sentinel\n"));
                        replacedWithRegularDirectory = true;
                    }
                });

            var result = Result(output, format);
            Assert.True(reached, $"phase {phase} was not reached");
            Assert.True(swapped);
            Assert.Equal(outsideTargetBytes, File.ReadAllBytes(outsideTarget));
            Assert.Equal(outsideTempBytes, File.ReadAllBytes(outsideTemp!));
            Assert.DoesNotContain(host.Root, output, StringComparison.Ordinal);
            Assert.DoesNotContain(outside.Root, output, StringComparison.Ordinal);

            if (phase == "temp-cleanup")
            {
                Assert.Equal(0, exit);
                Assert.True(result.GetProperty("wrote").GetBoolean());
                Assert.Equal("recorded", Text(result, "disposition"));
                Assert.True(File.Exists(Path.Combine(movedStore, Domain, Team, Unit, Id + ".json")));
                Assert.NotNull(originalTemp);
                Assert.True(File.Exists(originalTemp));
                Assert.Contains(result.GetProperty("diagnostics").EnumerateArray(), item =>
                    Text(item, "cause") == "verification-evidence-owned-temp-cleanup"
                    && Text(item, "detail").Contains("original owned temporary path cannot be safely resolved", StringComparison.Ordinal));
                Assert.Empty(result.GetProperty("residual_owned_paths").EnumerateArray());
            }
            else
            {
                Assert.Equal(1, exit);
                Assert.False(result.GetProperty("wrote").GetBoolean());
                Assert.Null(result.GetProperty("artifact_path").GetString());
                Assert.Null(result.GetProperty("artifact_sha256").GetString());
                Assert.Equal("verification-evidence-identity-conflict", Text(result, "cause"));
                Assert.Empty(result.GetProperty("residual_owned_paths").EnumerateArray());
                Assert.False(File.Exists(Path.Combine(movedStore, Domain, Team, Unit, Id + ".json")));
                if (phase == "temp-create")
                {
                    Assert.Null(originalTemp);
                    Assert.False(File.Exists(Path.Combine(movedStore, Domain, Team, Unit,
                        Path.GetFileName(outsideTemp!))));
                }
                else
                {
                    Assert.NotNull(originalTemp);
                    Assert.True(File.Exists(originalTemp));
                    Assert.True(replacedWithRegularDirectory);
                    Assert.Equal(Encoding.UTF8.GetBytes("replacement directory sentinel\n"), File.ReadAllBytes(replacementTemp!));
                    Assert.Contains(result.GetProperty("diagnostics").EnumerateArray(), item =>
                        Text(item, "cause") == "verification-evidence-owned-directory-cleanup"
                        && Text(item, "detail").Contains("original owned directory path cannot be safely resolved", StringComparison.Ordinal));
                }
            }

            if (new DirectoryInfo(host.EvidenceRoot).LinkTarget is not null)
                Directory.Delete(host.EvidenceRoot);
        }
    }

    [Theory]
    [InlineData("json")]
    [InlineData("markdown")]
    public void CrLfStoredJsonIsNotCanonicalEvidence(string format)
    {
        using var host = new TempHost();
        var trx = ReadSmallVstest();
        host.WriteTrx("capture.trx", trx);
        var (writeExit, writeOutput) = Invoke(host,
            [.. RecordArgs(host, format, Hash(host.PacketBytes), "capture.trx"), "--write"]);
        Assert.Equal(0, writeExit);
        var written = Result(writeOutput, format);
        var target = host.Absolute(Text(written, "artifact_path"));
        var canonicalBytes = File.ReadAllBytes(target);
        Assert.DoesNotContain((byte)'\r', canonicalBytes);
        var crlfBytes = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(canonicalBytes).Replace("\n", "\r\n", StringComparison.Ordinal));
        Assert.NotEqual(canonicalBytes, crlfBytes);
        File.WriteAllBytes(target, crlfBytes);

        foreach (var operation in new[] { "show", "validate" })
        {
            var (exit, output) = Invoke(host, ReadArgs(operation, format));
            var result = Result(output, format);
            Assert.Equal(1, exit);
            Assert.Equal("unavailable", Text(result, "state"));
            Assert.Equal("verification-evidence-record-invalid", Text(result, "cause"));
            Assert.Null(result.GetProperty("artifact_path").GetString());
            Assert.Null(result.GetProperty("artifact_sha256").GetString());
            Assert.Equal(crlfBytes, File.ReadAllBytes(target));
        }
    }

    [Theory]
    [InlineData("json")]
    [InlineData("markdown")]
    public void PrepublicationCompetingTargetKeepsConflictAndCleanupDiagnostics(string format)
    {
        using var host = new TempHost();
        host.WriteTrx("capture.trx", ReadSmallVstest());
        var target = host.Absolute(".intent-cli/verification-evidence/intent-cli/intent-cli-dev/G866/E1.json");
        var racedBytes = Encoding.UTF8.GetBytes("{ malformed prepublication target");
        string? temporaryPath = null;
        var reached = false;
        var (exit, output) = Invoke(host,
            [.. RecordArgs(host, format, Hash(host.PacketBytes), "capture.trx"), "--write"],
            (operation, path) =>
            {
                if (operation == "before-publish")
                {
                    reached = true;
                    File.WriteAllBytes(target, racedBytes);
                }
                else if (operation == "temp-cleanup")
                {
                    temporaryPath = path;
                    throw new IOException("injected cleanup failure after competing target");
                }
            });
        var result = Result(output, format);
        Assert.True(reached);
        Assert.NotNull(temporaryPath);
        Assert.Equal(1, exit);
        Assert.Equal("conflict", Text(result, "state"));
        Assert.Equal("verification-evidence-record-conflict", Text(result, "cause"));
        Assert.Equal(racedBytes, File.ReadAllBytes(target));
        var diagnostics = result.GetProperty("diagnostics").EnumerateArray().ToArray();
        Assert.Contains(diagnostics, item => Text(item, "cause") == "verification-evidence-record-conflict");
        Assert.Contains(diagnostics, item => Text(item, "cause") == "verification-evidence-owned-temp-cleanup");
        var relativeTemp = Path.GetRelativePath(host.Root, temporaryPath!).Replace(Path.DirectorySeparatorChar, '/');
        Assert.Contains(relativeTemp, result.GetProperty("residual_owned_paths").EnumerateArray()
            .Select(item => item.GetString()));
        Assert.False(result.GetProperty("wrote").GetBoolean());
    }

    [Theory]
    [InlineData("json")]
    [InlineData("markdown")]
    public void MalformedExactTargetAppearingAtAtomicCreateIsAConflict(string format)
    {
        using var host = new TempHost();
        var trx = ReadSmallVstest();
        host.WriteTrx("capture.trx", trx);
        var target = host.Absolute(".intent-cli/verification-evidence/intent-cli/intent-cli-dev/G866/E1.json");
        var racedBytes = Encoding.UTF8.GetBytes("{ malformed raced destination");
        var reached = false;

        var (exit, output) = Invoke(host,
            [.. RecordArgs(host, format, Hash(host.PacketBytes), "capture.trx"), "--write"],
            (operation, path) =>
            {
                if (operation != "atomic-create") return;
                Assert.Equal(target, path);
                reached = true;
                File.WriteAllBytes(target, racedBytes);
            });

        var result = Result(output, format);
        Assert.True(reached);
        Assert.Equal(1, exit);
        Assert.Equal("conflict", Text(result, "state"));
        Assert.Equal("verification-evidence-record-conflict", Text(result, "cause"));
        Assert.Equal(JsonValueKind.Null, result.GetProperty("artifact_path").ValueKind);
        Assert.Equal(".intent-cli/verification-evidence/intent-cli/intent-cli-dev/G866/E1.json",
            Text(result.GetProperty("diagnostics")[0], "path"));
        Assert.Equal(JsonValueKind.Null, result.GetProperty("artifact_sha256").ValueKind);
        Assert.False(result.GetProperty("wrote").GetBoolean());
        Assert.Equal(racedBytes, File.ReadAllBytes(target));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(target)!, "*.tmp", SearchOption.TopDirectoryOnly));
    }

    [Theory]
    [InlineData("json")]
    [InlineData("markdown")]
    public void ReachedDirectorySetupFailureCleansEarlierOwnedDirectories(string format)
    {
        using var host = new TempHost();
        var trx = ReadSmallVstest();
        host.WriteTrx("capture.trx", trx);
        var targetDirectory = Path.Combine(host.EvidenceRoot, Domain);
        var reached = false;

        var (exit, output) = Invoke(host,
            [.. RecordArgs(host, format, Hash(host.PacketBytes), "capture.trx"), "--write"],
            (operation, path) =>
            {
                if (operation != "directory-create" || !string.Equals(path, targetDirectory, StringComparison.Ordinal)) return;
                reached = true;
                throw new IOException("sentinel directory setup failure");
            });

        var result = Result(output, format);
        Assert.True(reached);
        Assert.Equal(1, exit);
        Assert.Equal("unavailable", Text(result, "state"));
        Assert.Equal("verification-evidence-read-unavailable", Text(result, "cause"));
        Assert.False(result.GetProperty("wrote").GetBoolean());
        Assert.Empty(result.GetProperty("residual_owned_paths").EnumerateArray());
        Assert.False(Directory.Exists(host.EvidenceRoot));
    }

    [Theory]
    [InlineData("json")]
    [InlineData("markdown")]
    public void ReachedPrepublicationIoFailureCleansOwnedTempAndDirectories(string format)
    {
        using var host = new TempHost();
        host.WriteTrx("capture.trx", ReadSmallVstest());
        var reached = false;
        var (exit, output) = Invoke(host,
            [.. RecordArgs(host, format, Hash(host.PacketBytes), "capture.trx"), "--write"],
            (operation, _) =>
            {
                if (operation != "before-publish") return;
                reached = true;
                throw new IOException("sentinel prepublication failure");
            });
        var result = Result(output, format);
        Assert.True(reached);
        Assert.Equal(1, exit);
        Assert.Equal("unavailable", Text(result, "state"));
        Assert.Equal("verification-evidence-read-unavailable", Text(result, "cause"));
        Assert.Equal("refused", Text(result, "disposition"));
        Assert.False(result.GetProperty("wrote").GetBoolean());
        Assert.False(result.GetProperty("idempotent").GetBoolean());
        Assert.Empty(result.GetProperty("residual_owned_paths").EnumerateArray());
        Assert.Null(result.GetProperty("artifact_path").GetString());
        Assert.Null(result.GetProperty("artifact_sha256").GetString());
        Assert.False(Directory.Exists(host.EvidenceRoot));
        Assert.DoesNotContain(host.Root, output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("json")]
    [InlineData("markdown")]
    public void SuccessfulPublicationRetainsVerifiedTargetAndReportsOwnedCleanupWarning(string format)
    {
        using var host = new TempHost();
        host.WriteTrx("capture.trx", ReadSmallVstest());
        var cleanupReached = false;
        string? temporaryPath = null;
        try
        {
            var (exit, output) = Invoke(host,
                [.. RecordArgs(host, format, Hash(host.PacketBytes), "capture.trx"), "--write"],
                (operation, path) =>
                {
                    if (operation != "temp-cleanup") return;
                    cleanupReached = true;
                    temporaryPath = path;
                    throw new IOException("injected owned temporary cleanup failure");
                });
            var result = Result(output, format);
            Assert.True(cleanupReached);
            Assert.Equal(0, exit);
            Assert.Equal("current", Text(result, "state"));
            Assert.Equal("recorded", Text(result, "disposition"));
            Assert.True(result.GetProperty("wrote").GetBoolean());
            Assert.False(result.GetProperty("idempotent").GetBoolean());
            var expectedTempRelative = Path.GetRelativePath(host.Root, temporaryPath!).Replace(Path.DirectorySeparatorChar, '/');
            Assert.Equal("verification-evidence-owned-temp-cleanup", Text(result.GetProperty("diagnostics")[0], "cause"));
            Assert.Equal(expectedTempRelative, Text(result.GetProperty("diagnostics")[0], "path"));
            Assert.Contains(expectedTempRelative, result.GetProperty("residual_owned_paths").EnumerateArray()
                .Select(item => item.GetString()));
            var targetPath = host.Absolute(Text(result, "artifact_path"));
            var targetBytes = File.ReadAllBytes(targetPath);
            Assert.Equal(Hash(targetBytes), Text(result, "artifact_sha256"));
            using var stored = JsonDocument.Parse(targetBytes);
            Assert.Equal(Hash(ReadSmallVstest()), Text(stored.RootElement, "trx_sha256"));
            Assert.True(File.Exists(temporaryPath));
            Assert.DoesNotContain(host.Root, output, StringComparison.Ordinal);
        }
        finally
        {
            if (temporaryPath is not null && File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SimultaneousCreateOnlyWritersDistinguishIdenticalAndDifferentBytes(bool differentTrx)
    {
        using var host = new TempHost();
        var original = ReadSmallVstest();
        var competing = MutateSmallTrx("unicode-name");
        host.WriteTrx("first.trx", original);
        host.WriteTrx("second.trx", differentTrx ? competing : original);
        Directory.CreateDirectory(Path.Combine(host.EvidenceRoot, Domain, Team, Unit));
        using var barrier = new Barrier(2);
        var reached = 0;
        Action<string, string> beforeOperation = (operation, _) =>
        {
            if (operation != "atomic-create") return;
            Interlocked.Increment(ref reached);
            if (!barrier.SignalAndWait(TimeSpan.FromSeconds(15)))
                throw new IOException("concurrent publication barrier timed out");
        };

        var first = Task.Run(() => Invoke(host,
            [.. RecordArgs(host, "json", Hash(host.PacketBytes), "first.trx"), "--write"], beforeOperation));
        var second = Task.Run(() => Invoke(host,
            [.. RecordArgs(host, "json", Hash(host.PacketBytes), "second.trx"), "--write"], beforeOperation));
        var results = await Task.WhenAll(first, second);
        Assert.Equal(2, reached);

        var outputs = results.Select(item => Result(item.Text, "json")).ToArray();
        Assert.Single(outputs, item => item.GetProperty("wrote").GetBoolean());
        Assert.Empty(Directory.GetFiles(Path.Combine(host.EvidenceRoot, Domain, Team, Unit), "*.tmp"));
        var finalPath = Path.Combine(host.EvidenceRoot, Domain, Team, Unit, Id + ".json");
        var finalBytes = File.ReadAllBytes(finalPath);
        Assert.Contains(outputs, item => Text(item, "artifact_sha256") == Hash(finalBytes));

        if (!differentTrx)
        {
            Assert.All(results, item => Assert.Equal(0, item.Exit));
            Assert.Single(outputs, item => item.GetProperty("idempotent").GetBoolean());
        }
        else
        {
            Assert.Single(results, item => item.Exit == 0);
            var loser = Assert.Single(outputs, item => Text(item, "state") == "conflict");
            Assert.Equal("verification-evidence-record-conflict", Text(loser, "cause"));
            Assert.Equal(".intent-cli/verification-evidence/intent-cli/intent-cli-dev/G866/E1.json",
                Text(loser, "artifact_path"));
            Assert.Equal(Hash(finalBytes), Text(loser, "artifact_sha256"));
            using var storedDocument = JsonDocument.Parse(finalBytes);
            Assert.Equal(Text(storedDocument.RootElement, "trx_sha256"), Text(loser, "trx_sha256"));
        }
    }

    [Theory]
    [InlineData("json")]
    [InlineData("markdown")]
    public void FailedPacketRecheckReportsOnlyCreatedDirectoryResiduals(string format)
    {
        using var host = new TempHost();
        var trx = ReadSmallVstest();
        host.WriteTrx("capture.trx", trx);
        var changedPacket = Encoding.UTF8.GetBytes(ValidPacket + "\n# changed after read\n");
        string? sentinelPath = null;
        var reached = false;
        var (exit, output) = Invoke(host,
            [.. RecordArgs(host, format, Hash(host.PacketBytes), "capture.trx"), "--write"],
            (operation, path) =>
            {
                if (operation != "before-publish") return;
                reached = true;
                var parent = Path.GetDirectoryName(path)!;
                sentinelPath = Path.Combine(parent, "concurrent-file.keep");
                File.WriteAllText(sentinelPath, "external sibling", new UTF8Encoding(false));
                File.WriteAllBytes(host.PacketPath, changedPacket);
            });
        var result = Result(output, format);
        Assert.True(reached);
        Assert.Equal(1, exit);
        Assert.Equal("stale", Text(result, "state"));
        Assert.Equal("verification-evidence-packet-changed", Text(result, "cause"));
        Assert.False(result.GetProperty("wrote").GetBoolean());
        Assert.Equal("external sibling", File.ReadAllText(sentinelPath!));
        Assert.False(File.Exists(host.Absolute(".intent-cli/verification-evidence/intent-cli/intent-cli-dev/G866/E1.json")));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(sentinelPath!)!, "*.tmp"));
        var residuals = result.GetProperty("residual_owned_paths").EnumerateArray().Select(item => item.GetString()).ToHashSet(StringComparer.Ordinal);
        Assert.Contains(".intent-cli/verification-evidence", residuals);
        Assert.Contains(".intent-cli/verification-evidence/intent-cli/intent-cli-dev/G866", residuals);
        Assert.All(residuals, path => Assert.True(Directory.Exists(host.Absolute(path!))));
    }

    [Theory]
    [InlineData("json")]
    [InlineData("markdown")]
    public void VerifiedStoredFactsSurviveCurrentPacketRemoval(string format)
    {
        using var host = new TempHost();
        var trx = ReadSmallVstest();
        host.WriteTrx("capture.trx", trx);
        var (writeExit, writeOutput) = Invoke(host, [.. RecordArgs(host, format, Hash(host.PacketBytes), "capture.trx"), "--write"]);
        Assert.Equal(0, writeExit);
        var written = Result(writeOutput, format);
        var expectedPath = Text(written, "artifact_path");
        var expectedSha = Text(written, "artifact_sha256");
        File.Delete(host.PacketPath);

        var (showExit, showOutput) = Invoke(host, ReadArgs("show", format));
        var shown = Result(showOutput, format);
        Assert.Equal(1, showExit);
        Assert.Equal("unavailable", Text(shown, "state"));
        Assert.Equal(expectedPath, Text(shown, "artifact_path"));
        Assert.Equal(expectedSha, Text(shown, "artifact_sha256"));
        Assert.Equal(Hash(trx), Text(shown, "trx_sha256"));
        Assert.Equal(JsonValueKind.Object, shown.GetProperty("report").ValueKind);
        var (validateExit, validateOutput) = Invoke(host, ReadArgs("validate", format));
        var validated = Result(validateOutput, format);
        Assert.Equal(1, validateExit);
        Assert.Equal("unavailable", Text(validated, "state"));
        Assert.Equal("verification-evidence-read-unavailable", Text(validated, "cause"));
        Assert.Equal(expectedPath, Text(validated, "artifact_path"));
        Assert.Equal(expectedSha, Text(validated, "artifact_sha256"));
        Assert.Equal(Hash(trx), Text(validated, "trx_sha256"));
    }

    [Theory]
    [InlineData("json")]
    [InlineData("markdown")]
    public void InvalidCurrentPacketIsUnavailableButRetainsVerifiedStoredFacts(string format)
    {
        using var host = new TempHost();
        var trx = ReadSmallVstest();
        host.WriteTrx("capture.trx", trx);
        var (writeExit, writeText) = Invoke(host,
            [.. RecordArgs(host, format, Hash(host.PacketBytes), "capture.trx"), "--write"]);
        Assert.Equal(0, writeExit);
        var written = Result(writeText, format);
        var artifactPath = Text(written, "artifact_path");
        var artifactBytes = File.ReadAllBytes(host.Absolute(artifactPath));
        host.WritePacket(Encoding.UTF8.GetBytes("implementation_issue_packet: [unterminated\n"));

        foreach (var operation in new[] { "show", "validate" })
        {
            var (exit, text) = Invoke(host, ReadArgs(operation, format));
            var result = Result(text, format);
            Assert.Equal(1, exit);
            Assert.Equal("unavailable", Text(result, "state"));
            Assert.Equal("verification-evidence-packet-invalid", Text(result, "cause"));
            Assert.Equal(artifactPath, Text(result, "artifact_path"));
            Assert.Equal(Hash(artifactBytes), Text(result, "artifact_sha256"));
            Assert.Equal(Hash(trx), Text(result, "trx_sha256"));
            Assert.Equal(Text(written.GetProperty("report"), "reported_outcome"),
                Text(result.GetProperty("report"), "reported_outcome"));
        }
        Assert.Equal(artifactBytes, File.ReadAllBytes(host.Absolute(artifactPath)));
    }

    [Fact]
    public void UnreadableCurrentPacketIsUnavailableButRetainsVerifiedStoredFacts()
    {
        if (OperatingSystem.IsWindows() || string.Equals(Environment.UserName, "root", StringComparison.OrdinalIgnoreCase))
            throw Xunit.Sdk.SkipException.ForSkip("Unix file permission enforcement is unavailable under this test identity.");

        foreach (var format in new[] { "json", "markdown" })
        {
            using var host = new TempHost();
            var trx = ReadSmallVstest();
            host.WriteTrx("capture.trx", trx);
            var (writeExit, writeText) = Invoke(host,
                [.. RecordArgs(host, format, Hash(host.PacketBytes), "capture.trx"), "--write"]);
            Assert.Equal(0, writeExit);
            var written = Result(writeText, format);
            var artifactPath = Text(written, "artifact_path");
            var artifactBytes = File.ReadAllBytes(host.Absolute(artifactPath));
            var packetMode = File.GetUnixFileMode(host.PacketPath);
            File.SetUnixFileMode(host.PacketPath, UnixFileMode.None);
            try
            {
                if (Record.Exception(() => File.ReadAllBytes(host.PacketPath)) is not UnauthorizedAccessException)
                    throw Xunit.Sdk.SkipException.ForSkip("The OS did not deny reading the permission-restricted packet.");

                foreach (var operation in new[] { "show", "validate" })
                {
                    var (exit, output) = Invoke(host, ReadArgs(operation, format));
                    var result = Result(output, format);
                    Assert.Equal(1, exit);
                    Assert.Equal("unavailable", Text(result, "state"));
                    Assert.Equal("verification-evidence-read-unavailable", Text(result, "cause"));
                    Assert.Equal(artifactPath, Text(result, "artifact_path"));
                    Assert.Equal(Hash(artifactBytes), Text(result, "artifact_sha256"));
                    Assert.Equal(Hash(trx), Text(result, "trx_sha256"));
                    Assert.Equal(".intent-cli/issues/G866/packet.yaml", Text(result.GetProperty("diagnostics")[0], "path"));
                    Assert.DoesNotContain(host.Root, output, StringComparison.Ordinal);
                }
                Assert.Equal(artifactBytes, File.ReadAllBytes(host.Absolute(artifactPath)));
            }
            finally
            {
                File.SetUnixFileMode(host.PacketPath, packetMode);
            }
        }
    }

    [Theory]
    [InlineData("json")]
    [InlineData("markdown")]
    public void NoncanonicalStoredBytesAreUnavailableWithoutUnverifiedDiskFacts(string format)
    {
        using var host = new TempHost();
        var trx = ReadSmallVstest();
        host.WriteTrx("capture.trx", trx);
        var (writeExit, writeOutput) = Invoke(host, [.. RecordArgs(host, format, Hash(host.PacketBytes), "capture.trx"), "--write"]);
        Assert.Equal(0, writeExit);
        var written = Result(writeOutput, format);
        var artifact = host.Absolute(Text(written, "artifact_path"));
        File.AppendAllText(artifact, " ", new UTF8Encoding(false));

        var (showExit, showOutput) = Invoke(host, ReadArgs("show", format));
        var shown = Result(showOutput, format);
        Assert.Equal(1, showExit);
        Assert.Equal("unavailable", Text(shown, "state"));
        Assert.Equal("verification-evidence-record-invalid", Text(shown, "cause"));
        Assert.Equal(JsonValueKind.Null, shown.GetProperty("artifact_sha256").ValueKind);
        Assert.Equal(JsonValueKind.Null, shown.GetProperty("report").ValueKind);
        Assert.Empty(shown.GetProperty("residual_owned_paths").EnumerateArray());
    }

    [Theory]
    [MemberData(nameof(StoredRecordMutations))]
    public void StoredSchemaAndEmbeddedEvidenceMutationsRefuseWithoutDiskProof(string mutation, string replacement)
    {
        foreach (var format in new[] { "json", "markdown" })
        {
            using var host = new TempHost();
            var trx = ReadSmallVstest();
            host.WriteTrx("capture.trx", trx);
            var (writeExit, writeOutput) = Invoke(host,
                [.. RecordArgs(host, format, Hash(host.PacketBytes), "capture.trx"), "--write"]);
            Assert.Equal(0, writeExit);
            var written = Result(writeOutput, format);
            var relative = Text(written, "artifact_path");
            var path = host.Absolute(relative);
            var canonical = File.ReadAllText(path, Encoding.UTF8);
            var mutated = replacement == "duplicate-property"
                ? canonical.Replace("\"schema_version\": \"1\",", "\"schema_version\": \"1\",\n  \"schema_version\": \"1\",", StringComparison.Ordinal)
                : MutateStoredJson(canonical, mutation);
            Assert.NotEqual(canonical, mutated);
            File.WriteAllText(path, mutated, new UTF8Encoding(false));
            var tamperedBytes = File.ReadAllBytes(path);

            foreach (var operation in new[] { "show", "validate" })
            {
                var (exit, output) = Invoke(host, ReadArgs(operation, format));
                var result = Result(output, format);
                Assert.Equal(1, exit);
                Assert.Equal("unavailable", Text(result, "state"));
                Assert.Equal("verification-evidence-record-invalid", Text(result, "cause"));
                Assert.Equal("refused", Text(result, "disposition"));
                Assert.Null(result.GetProperty("artifact_path").GetString());
                Assert.Null(result.GetProperty("artifact_sha256").GetString());
                Assert.Null(result.GetProperty("trx_sha256").GetString());
                Assert.Equal(JsonValueKind.Null, result.GetProperty("binding").ValueKind);
                Assert.Equal(JsonValueKind.Null, result.GetProperty("report").ValueKind);
                Assert.Equal($".intent-cli/verification-evidence/{Domain}/{Team}/{Unit}/{Id}.json",
                    Text(result.GetProperty("diagnostics")[0], "path"));
                Assert.DoesNotContain(host.Root, output, StringComparison.Ordinal);
                Assert.Equal(tamperedBytes, File.ReadAllBytes(path));
            }
        }
    }

    public static IEnumerable<object[]> StoredRecordMutations()
    {
        foreach (var mutation in new[]
        {
            "required-null", "required-wrong-type", "unknown-field", "bad-base64", "bad-trx-sha",
            "bad-report", "null-binding-command", "null-binding-criterion", "duplicate-property"
        })
            yield return [mutation, mutation];
    }

    private static string MutateStoredJson(string canonical, string mutation)
    {
        var root = JsonNode.Parse(canonical)!.AsObject();
        switch (mutation)
        {
            case "required-null": root["schema_version"] = null; break;
            case "required-wrong-type": root["schema_version"] = 1; break;
            case "unknown-field": root["unknown_field"] = true; break;
            case "bad-base64": root["trx_base64"] = "%%%"; break;
            case "bad-trx-sha": root["trx_sha256"] = new string('0', 64); break;
            case "bad-report": root["report"]!["total"] = 999; break;
            case "null-binding-command": root["binding"]!["command"] = null; break;
            case "null-binding-criterion": root["binding"]!["acceptance_criteria"]![0] = null; break;
            default: throw new ArgumentOutOfRangeException(nameof(mutation), mutation, "Unknown stored-record mutation.");
        }
        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n";
    }

    [Theory]
    [InlineData("json", false)]
    [InlineData("json", true)]
    [InlineData("markdown", false)]
    [InlineData("markdown", true)]
    public void ExistingMalformedTargetConflictsForRecordButRemainsUnavailableForRead(string format, bool write)
    {
        using var host = new TempHost();
        var trx = ReadSmallVstest();
        host.WriteTrx("capture.trx", trx);
        var (initialExit, initialOutput) = Invoke(host,
            [.. RecordArgs(host, format, Hash(host.PacketBytes), "capture.trx"), "--write"]);
        Assert.Equal(0, initialExit);
        var initial = Result(initialOutput, format);
        var path = host.Absolute(Text(initial, "artifact_path"));
        File.AppendAllText(path, " ", new UTF8Encoding(false));
        var malformedBytes = File.ReadAllBytes(path);

        var recordArgs = RecordArgs(host, format, Hash(host.PacketBytes), "capture.trx");
        if (write) recordArgs = [.. recordArgs, "--write"];
        var (recordExit, recordOutput) = Invoke(host, recordArgs);
        var recorded = Result(recordOutput, format);
        Assert.Equal(1, recordExit);
        Assert.Equal("conflict", Text(recorded, "state"));
        Assert.Equal("verification-evidence-record-conflict", Text(recorded, "cause"));
        Assert.Equal(JsonValueKind.Null, recorded.GetProperty("artifact_sha256").ValueKind);
        Assert.Equal(JsonValueKind.Null, recorded.GetProperty("report").ValueKind);
        Assert.Equal(malformedBytes, File.ReadAllBytes(path));
        Assert.False(recorded.GetProperty("wrote").GetBoolean());

        foreach (var operation in new[] { "show", "validate" })
        {
            var (readExit, readOutput) = Invoke(host, ReadArgs(operation, format));
            var read = Result(readOutput, format);
            Assert.Equal(1, readExit);
            Assert.Equal("unavailable", Text(read, "state"));
            Assert.Equal("verification-evidence-record-invalid", Text(read, "cause"));
            Assert.Equal(JsonValueKind.Null, read.GetProperty("artifact_sha256").ValueKind);
            Assert.Equal(JsonValueKind.Null, read.GetProperty("report").ValueKind);
        }
    }

    [Theory]
    [InlineData("json")]
    [InlineData("markdown")]
    public void WrongEmbeddedIdAtRequestedFilenameIsConflictWithoutStoredProof(string format)
    {
        using var host = new TempHost();
        var trx = ReadSmallVstest();
        host.WriteTrx("capture.trx", trx);
        var (writeExit, writeOutput) = Invoke(host, [.. RecordArgs(host, format, Hash(host.PacketBytes), "capture.trx"), "--write"]);
        Assert.Equal(0, writeExit);
        var written = Result(writeOutput, format);
        var path = host.Absolute(Text(written, "artifact_path"));
        var stored = JsonSerializer.Deserialize<VerificationEvidenceRecord>(File.ReadAllBytes(path))!;
        File.WriteAllBytes(path, VerificationEvidenceStore.Serialize(stored with { Id = "E2" }));

        var (showExit, showOutput) = Invoke(host, ReadArgs("show", format));
        var shown = Result(showOutput, format);
        Assert.Equal(1, showExit);
        Assert.Equal("conflict", Text(shown, "state"));
        Assert.Equal("verification-evidence-identity-conflict", Text(shown, "cause"));
        Assert.Equal(JsonValueKind.Null, shown.GetProperty("artifact_sha256").ValueKind);
        Assert.Equal(JsonValueKind.Null, shown.GetProperty("trx_sha256").ValueKind);
        Assert.Equal(JsonValueKind.Null, shown.GetProperty("report").ValueKind);
    }

    [Theory]
    [InlineData("show", "json")]
    [InlineData("validate", "json")]
    [InlineData("show", "markdown")]
    [InlineData("validate", "markdown")]
    public void RequestedRepositoryMismatchPrecedesCurrentPacketReadAndReturnsNoWrongScopeProof(string operation, string format)
    {
        using var host = new TempHost();
        host.WriteTrx("capture.trx", ReadSmallVstest());
        var (writeExit, writeOutput) = Invoke(host,
            [.. RecordArgs(host, "json", Hash(host.PacketBytes), "capture.trx"), "--write"]);
        Assert.Equal(0, writeExit);
        var written = Result(writeOutput, "json");
        File.Delete(host.PacketPath);

        var packetReads = 0;
        var args = ReadArgs(operation, format, repo: "another-owner/another-repo");
        var (exit, output) = Invoke(host, args, (name, _) => { if (name == "packet-read") packetReads++; });
        var result = Result(output, format);
        Assert.Equal(1, exit);
        Assert.Equal("conflict", Text(result, "state"));
        Assert.Equal("verification-evidence-identity-conflict", Text(result, "cause"));
        Assert.Equal(0, packetReads);
        foreach (var property in new[] { "packet_path", "observed_packet_sha256", "artifact_path", "artifact_sha256", "trx_sha256", "binding", "report" })
            Assert.Equal(JsonValueKind.Null, result.GetProperty(property).ValueKind);
        Assert.NotNull(Text(written, "artifact_sha256"));
    }

    [Fact]
    public void WrongHeadShowsVerifiedHistoryButValidationRefusesStaleBinding()
    {
        using var host = new TempHost();
        var trx = ReadSmallVstest();
        host.WriteTrx("capture.trx", trx);
        var (writeExit, writeOutput) = Invoke(host, [.. RecordArgs(host, "json", Hash(host.PacketBytes), "capture.trx"), "--write"]);
        Assert.Equal(0, writeExit);
        var written = Result(writeOutput, "json");
        var otherHead = new string('a', 40);

        var (showExit, showOutput) = Invoke(host, ReadArgs("show", "json", otherHead));
        var shown = Result(showOutput, "json");
        Assert.Equal(0, showExit);
        Assert.Equal("stale", Text(shown, "state"));
        Assert.Equal(Text(written, "artifact_sha256"), Text(shown, "artifact_sha256"));

        var (validateExit, validateOutput) = Invoke(host, ReadArgs("validate", "json", otherHead));
        Assert.Equal(1, validateExit);
        Assert.Equal("stale", Text(Result(validateOutput, "json"), "state"));
    }

    [Theory]
    [InlineData("packet-leaf")]
    [InlineData("packet-parent")]
    [InlineData("trx-leaf")]
    [InlineData("trx-parent")]
    public void PopulatedPacketAndTrxSymlinksRefuseWithRelativeDiagnostics(string linkKind)
    {
        foreach (var format in new[] { "json", "markdown" })
        {
            using var host = new TempHost();
            using var outside = new TempHost();
            var sentinel = Encoding.UTF8.GetBytes("external sentinel remains untouched\n");
            var outsideFile = Path.Combine(outside.Root, "sentinel.trx");
            File.WriteAllBytes(outsideFile, sentinel);
            var operations = new List<string>();
            string relative;
            if (linkKind == "packet-leaf")
            {
                File.Delete(host.PacketPath);
                File.CreateSymbolicLink(host.PacketPath, outsideFile);
                relative = $".intent-cli/issues/{Unit}/packet.yaml";
            }
            else if (linkKind == "packet-parent")
            {
                var issues = Path.Combine(host.Root, ".intent-cli", "issues");
                Directory.Delete(issues, recursive: true);
                var outsideIssues = Path.Combine(outside.Root, ".intent-cli", "issues");
                Assert.True(File.Exists(Path.Combine(outsideIssues, Unit, "packet.yaml")));
                Directory.CreateSymbolicLink(issues, outsideIssues);
                relative = $".intent-cli/issues/{Unit}/packet.yaml";
            }
            else if (linkKind == "trx-leaf")
            {
                File.CreateSymbolicLink(Path.Combine(host.Root, "linked.trx"), outsideFile);
                relative = "linked.trx";
            }
            else
            {
                Directory.CreateSymbolicLink(Path.Combine(host.Root, "linked-directory"), outside.Root);
                relative = "linked-directory/sentinel.trx";
            }

            var (exit, output) = Invoke(host,
                RecordArgs(host, format, Hash(host.PacketBytes),
                    linkKind.StartsWith("trx-", StringComparison.Ordinal) ? relative : "missing.trx"),
                (operation, _) => operations.Add(operation));
            var result = Result(output, format);
            Assert.Equal(1, exit);
            Assert.Equal("unavailable", Text(result, "state"));
            if (linkKind == "packet-parent")
                Assert.Equal("verification-evidence-identity-conflict", Text(result, "cause"));
            else
                Assert.Contains(Text(result, "cause"), new[]
                {
                    "verification-evidence-read-unavailable", "verification-evidence-identity-conflict"
                });
            Assert.Equal(relative, Text(result.GetProperty("diagnostics")[0], "path"));
            Assert.DoesNotContain(host.Root, output, StringComparison.Ordinal);
            Assert.DoesNotContain(outside.Root, output, StringComparison.Ordinal);
            Assert.Equal(sentinel, File.ReadAllBytes(outsideFile));
            Assert.DoesNotContain("trx-read", operations);
            if (linkKind == "packet-parent")
                Assert.DoesNotContain("packet-read", operations);
            if (linkKind.StartsWith("trx-", StringComparison.Ordinal))
                Assert.Contains("packet-read", operations);
            Assert.False(Directory.Exists(host.EvidenceRoot));
        }
    }

    [Theory]
    [InlineData("store-root-link")]
    [InlineData("target-link")]
    [InlineData("target-directory")]
    [InlineData("case-alias")]
    public void EvidencePathSymlinkDirectoryAndCaseAliasesDoNotOpenOrOverwrite(string entryKind)
    {
        foreach (var format in new[] { "json", "markdown" })
        {
            using var host = new TempHost();
            using var outside = new TempHost();
            var trx = ReadSmallVstest();
            host.WriteTrx("capture.trx", trx);
            var sentinelPath = Path.Combine(outside.Root, "sentinel.json");
            var sentinelBytes = Encoding.UTF8.GetBytes("external store sentinel\n");
            File.WriteAllBytes(sentinelPath, sentinelBytes);
            var target = host.Absolute($".intent-cli/verification-evidence/{Domain}/{Team}/{Unit}/{Id}.json");
            var diagnosticPath = $".intent-cli/verification-evidence/{Domain}/{Team}/{Unit}/{Id}.json";
            if (entryKind == "store-root-link")
            {
                Directory.CreateSymbolicLink(host.EvidenceRoot, outside.Root);
            }
            else
            {
                if (entryKind == "target-link")
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    File.CreateSymbolicLink(target, sentinelPath);
                }
                else if (entryKind == "target-directory")
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    Directory.CreateDirectory(target);
                }
                else
                {
                    Directory.CreateDirectory(host.EvidenceRoot);
                    var alias = host.Absolute($".intent-cli/verification-evidence/{Domain.ToUpperInvariant()}");
                    Directory.CreateDirectory(alias);
                }
            }

            var (exit, output) = Invoke(host,
                RecordArgs(host, format, Hash(host.PacketBytes), "capture.trx"));
            var result = Result(output, format);
            Assert.Equal(1, exit);
            Assert.Equal("unavailable", Text(result, "state"));
            Assert.Equal(diagnosticPath, Text(result.GetProperty("diagnostics")[0], "path"));
            Assert.DoesNotContain(host.Root, output, StringComparison.Ordinal);
            Assert.DoesNotContain(outside.Root, output, StringComparison.Ordinal);
            Assert.Equal(sentinelBytes, File.ReadAllBytes(sentinelPath));
            Assert.Null(result.GetProperty("artifact_path").GetString());
            Assert.Null(result.GetProperty("artifact_sha256").GetString());
            Assert.Equal(trx, File.ReadAllBytes(host.Absolute("capture.trx")));
        }
    }

    [Theory]
    [InlineData("regular-file")]
    [InlineData("fifo")]
    public void EvidenceStoreIntermediateNonDirectoryIsRefusedWithoutOpening(string entryKind)
    {
        if (entryKind == "fifo" && OperatingSystem.IsWindows())
            throw Xunit.Sdk.SkipException.ForSkip("POSIX FIFO fixture is unavailable on Windows.");

        foreach (var format in new[] { "json", "markdown" })
        {
            using var host = new TempHost();
            using var outside = new TempHost();
            var trx = ReadSmallVstest();
            host.WriteTrx("capture.trx", trx);
            var sentinelPath = Path.Combine(outside.Root, "sentinel.json");
            var sentinel = Encoding.UTF8.GetBytes("outside evidence remains unchanged\n");
            File.WriteAllBytes(sentinelPath, sentinel);
            if (entryKind == "regular-file") File.WriteAllBytes(host.EvidenceRoot, sentinel);
            else if (CreateFifo(host.EvidenceRoot) != 0)
                throw Xunit.Sdk.SkipException.ForSkip("The filesystem does not support a POSIX FIFO in the temporary root.");

            var operations = new List<string>();
            var (showExit, showOutput) = Invoke(host, ReadArgs("show", format),
                (operation, _) => operations.Add(operation));
            var shown = Result(showOutput, format);
            Assert.Equal(1, showExit);
            Assert.Equal("unavailable", Text(shown, "state"));
            Assert.Contains(Text(shown, "cause"), new[]
            {
                "verification-evidence-read-unavailable", "verification-evidence-identity-conflict"
            });
            Assert.Null(shown.GetProperty("artifact_path").GetString());
            Assert.Null(shown.GetProperty("artifact_sha256").GetString());
            Assert.DoesNotContain("stored-read", operations);

            operations.Clear();
            var (recordExit, recordOutput) = Invoke(host,
                RecordArgs(host, format, Hash(host.PacketBytes), "capture.trx"),
                (operation, _) => operations.Add(operation));
            var recorded = Result(recordOutput, format);
            Assert.Equal(1, recordExit);
            Assert.Equal("unavailable", Text(recorded, "state"));
            Assert.Contains(Text(recorded, "cause"), new[]
            {
                "verification-evidence-read-unavailable", "verification-evidence-identity-conflict"
            });
            Assert.Null(recorded.GetProperty("artifact_path").GetString());
            Assert.Null(recorded.GetProperty("artifact_sha256").GetString());
            Assert.DoesNotContain("stored-read", operations);
            if (entryKind == "regular-file") Assert.Equal(sentinel, File.ReadAllBytes(host.EvidenceRoot));
            Assert.Equal(sentinel, File.ReadAllBytes(sentinelPath));
        }
    }

    [Theory]
    [InlineData("json")]
    [InlineData("markdown")]
    public void RepositoryRootFileOrSymlinkIsNotUsedAsEvidenceTrustAnchor(string format)
    {
        using var host = new TempHost();
        using var outside = new TempHost();
        var args = RecordArgs(host, format, Hash(host.PacketBytes), "capture.trx");
        var rootFile = Path.Combine(outside.Root, "not-a-directory");
        File.WriteAllText(rootFile, "not a directory");
        using (var writer = new StringWriter())
        {
            var exit = VerificationEvidenceCommand.Execute(rootFile, args, writer);
            var result = Result(writer.ToString(), format);
            Assert.Equal(1, exit);
            Assert.Equal("unavailable", Text(result, "state"));
            Assert.Equal("verification-evidence-read-unavailable", Text(result, "cause"));
            Assert.Null(result.GetProperty("packet_path").GetString());
            Assert.Null(result.GetProperty("diagnostics")[0].GetProperty("path").GetString());
            Assert.DoesNotContain(outside.Root, writer.ToString(), StringComparison.Ordinal);
        }

        var rootLink = Path.Combine(outside.Root, "linked-repository");
        Directory.CreateSymbolicLink(rootLink, host.Root);
        using (var writer = new StringWriter())
        {
            var exit = VerificationEvidenceCommand.Execute(rootLink, args, writer);
            var result = Result(writer.ToString(), format);
            Assert.Equal(1, exit);
            Assert.Equal("unavailable", Text(result, "state"));
            Assert.Equal("verification-evidence-read-unavailable", Text(result, "cause"));
            Assert.Null(result.GetProperty("diagnostics")[0].GetProperty("path").GetString());
            Assert.DoesNotContain(host.Root, writer.ToString(), StringComparison.Ordinal);
            Assert.False(Directory.Exists(host.EvidenceRoot));
        }
    }

    [Fact]
    public void UnreadablePacketDirectoryRefusesAfterProvingLeafRemainsReadable()
    {
        if (OperatingSystem.IsWindows() || string.Equals(Environment.UserName, "root", StringComparison.OrdinalIgnoreCase))
            throw Xunit.Sdk.SkipException.ForSkip("Unix directory permission enforcement is unavailable under this test identity.");

        using var host = new TempHost();
        host.WriteTrx("capture.trx", ReadSmallVstest());
        var directory = Path.GetDirectoryName(host.PacketPath)!;
        var originalMode = File.GetUnixFileMode(directory);
        Assert.Equal(host.PacketBytes, File.ReadAllBytes(host.PacketPath));
        File.SetUnixFileMode(directory, UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
        try
        {
            var denied = Record.Exception(() => Directory.EnumerateFileSystemEntries(directory).ToArray());
            if (denied is not UnauthorizedAccessException)
                throw Xunit.Sdk.SkipException.ForSkip("The OS did not deny listing the execute-only packet directory.");
            Assert.Equal(host.PacketBytes, File.ReadAllBytes(host.PacketPath));

            foreach (var format in new[] { "json", "markdown" })
            {
                var operations = new List<string>();
                var (exit, output) = Invoke(host,
                    RecordArgs(host, format, Hash(host.PacketBytes), "capture.trx"),
                    (operation, _) => operations.Add(operation));
                var result = Result(output, format);
                Assert.Equal(1, exit);
                Assert.Equal("unavailable", Text(result, "state"));
                Assert.Equal("verification-evidence-read-unavailable", Text(result, "cause"));
                Assert.Equal($".intent-cli/issues/{Unit}/packet.yaml", Text(result.GetProperty("diagnostics")[0], "path"));
                Assert.DoesNotContain(host.Root, output, StringComparison.Ordinal);
                Assert.DoesNotContain("packet-read", operations);
                Assert.DoesNotContain("trx-read", operations);
                Assert.False(Directory.Exists(host.EvidenceRoot));
            }
        }
        finally
        {
            File.SetUnixFileMode(directory, originalMode);
        }
    }

    [Fact]
    public void UnreadableTrxFileRefusesWithoutLeakingAbsoluteScratchPaths()
    {
        if (OperatingSystem.IsWindows() || string.Equals(Environment.UserName, "root", StringComparison.OrdinalIgnoreCase))
            throw Xunit.Sdk.SkipException.ForSkip("Unix file permission enforcement is unavailable under this test identity.");

        using var host = new TempHost();
        var trxPath = host.Absolute("capture.trx");
        var trx = ReadSmallVstest();
        host.WriteTrx("capture.trx", trx);
        var originalMode = File.GetUnixFileMode(trxPath);
        Assert.Equal(trx, File.ReadAllBytes(trxPath));
        File.SetUnixFileMode(trxPath, UnixFileMode.None);
        try
        {
            var denied = Record.Exception(() => File.ReadAllBytes(trxPath));
            if (denied is not UnauthorizedAccessException)
                throw Xunit.Sdk.SkipException.ForSkip("The OS did not deny reading the permission-restricted TRX file.");

            foreach (var format in new[] { "json", "markdown" })
            {
                var operations = new List<string>();
                var (exit, output) = Invoke(host,
                    RecordArgs(host, format, Hash(host.PacketBytes), "capture.trx"),
                    (operation, _) => operations.Add(operation));
                var result = Result(output, format);
                Assert.Equal(1, exit);
                Assert.Equal("unavailable", Text(result, "state"));
                Assert.Equal("verification-evidence-read-unavailable", Text(result, "cause"));
                Assert.Equal("refused", Text(result, "disposition"));
                Assert.Equal("capture.trx", Text(result.GetProperty("diagnostics")[0], "path"));
                Assert.Contains("trx-read", operations);
                Assert.DoesNotContain(host.Root, output, StringComparison.Ordinal);
                Assert.Null(result.GetProperty("artifact_path").GetString());
                Assert.Null(result.GetProperty("artifact_sha256").GetString());
                Assert.False(Directory.Exists(host.EvidenceRoot));
            }
        }
        finally
        {
            File.SetUnixFileMode(trxPath, originalMode);
        }
    }

    [Theory]
    [InlineData("json")]
    [InlineData("markdown")]
    public void BoundedSiblingInspectionRefusesInsteadOfScanningPastLimit(string format)
    {
        using var host = new TempHost();
        host.WriteTrx("capture.trx", ReadSmallVstest());
        var packetDirectory = Path.GetDirectoryName(host.PacketPath)!;
        for (var index = 0; index < VerificationEvidenceStore.MaximumSiblingEntries; index++)
            File.WriteAllText(Path.Combine(packetDirectory, $"extra-{index:D4}.yaml"), "x");
        var reads = 0;
        var (exit, output) = Invoke(host,
            RecordArgs(host, format, Hash(host.PacketBytes), "capture.trx"),
            (_, _) => reads++);
        var result = Result(output, format);
        Assert.Equal(1, exit);
        Assert.Equal("unavailable", Text(result, "state"));
        Assert.Equal("verification-evidence-read-unavailable", Text(result, "cause"));
        Assert.Contains("4096-entry", Text(result, "detail"), StringComparison.Ordinal);
        Assert.Equal($".intent-cli/issues/{Unit}/packet.yaml", Text(result.GetProperty("diagnostics")[0], "path"));
        Assert.Equal(0, reads);
        Assert.DoesNotContain(host.Root, output, StringComparison.Ordinal);
        Assert.False(Directory.Exists(host.EvidenceRoot));
    }

    [Fact]
    public void SmallCaptureIsCommittedWithAuthenticVstestProvenance()
    {
        var bytes = ReadSmallVstest();
        Assert.Equal(2, TrxEvidenceReader.Read(bytes).Report?.Total);
        Assert.Equal("05806d94d6d671b5aa58200faeb2092a09cfef07cd6b3b7a0e82cc5aef9a95ef", Hash(bytes));
    }

    [Theory]
    [InlineData("json")]
    [InlineData("markdown")]
    public void PrefixedVstestNamespaceIsAcceptedFromActualBytes(string format)
    {
        var bytes = PrefixTrxNamespace(ReadSmallVstest());
        Assert.True(TrxEvidenceReader.Read(bytes).Success);
        using var host = new TempHost();
        host.WriteTrx("prefixed.trx", bytes);
        var (exit, output) = Invoke(host, RecordArgs(host, format, Hash(host.PacketBytes), "prefixed.trx"));
        var result = Result(output, format);
        Assert.Equal(0, exit);
        Assert.Equal("planned", Text(result, "disposition"));
        Assert.Equal(Hash(bytes), Text(result, "trx_sha256"));
        Assert.Equal(2, result.GetProperty("report").GetProperty("total").GetInt32());
        Assert.False(Directory.Exists(host.EvidenceRoot));
    }

    [Theory]
    [MemberData(nameof(InvalidTrxMutations))]
    public void OtherwiseValidTrxSingleMutationsRefuseWithoutCreatingEvidence(string mutation, string expectedCause)
    {
        var bytes = MutateSmallTrx(mutation);
        foreach (var format in new[] { "json", "markdown" })
        {
            using var host = new TempHost();
            host.WriteTrx("mutation.trx", bytes);
            var (exit, output) = Invoke(host,
                RecordArgs(host, format, Hash(host.PacketBytes), "mutation.trx"));
            var result = Result(output, format);
            Assert.Equal(1, exit);
            Assert.Equal("invalid", Text(result, "state"));
            Assert.Equal(expectedCause, Text(result, "cause"));
            Assert.Equal(Hash(bytes), Text(result, "trx_sha256"));
            Assert.Equal(JsonValueKind.Null, result.GetProperty("artifact_path").ValueKind);
            Assert.Equal(JsonValueKind.Null, result.GetProperty("artifact_sha256").ValueKind);
            Assert.False(Directory.Exists(host.EvidenceRoot));
        }
    }

    public static IEnumerable<object[]> InvalidTrxMutations()
    {
        foreach (var mutation in new[]
        {
            "total", "executed", "passed", "failed", "not-executed", "root-guid", "aborted-counter", "duplicate-entry", "missing-result", "bad-guid", "bad-name",
            "long-name", "bad-definition-name", "long-definition-name", "bad-outcome", "duplicate-execution", "missing-entry", "extra-entry",
            "missing-definition", "extra-definition", "wrong-definition-execution", "wrong-entry-execution",
            "duplicate-result", "duplicate-definition", "nested-result", "wrong-root-namespace", "wrong-result-namespace",
            "wrong-execution-namespace", "missing-results-section", "duplicate-results-section", "misplaced-counters",
            "stray-execution", "stray-counters", "wrong-summary-outcome"
        })
        {
            var cause = mutation is "root-guid" or "aborted-counter" or "duplicate-entry" or "missing-result"
                or "bad-guid" or "bad-name" or "long-name" or "bad-definition-name" or "long-definition-name" or "bad-outcome"
                or "duplicate-execution" or "missing-entry" or "extra-entry" or "duplicate-definition"
                or "missing-definition" or "extra-definition" or "wrong-definition-execution" or "wrong-entry-execution"
                or "duplicate-result" or "wrong-execution-namespace" or "missing-results-section" or "duplicate-results-section" or "misplaced-counters"
                or "nested-result" or "wrong-root-namespace" or "wrong-result-namespace"
                or "stray-execution" or "stray-counters"
                ? "verification-evidence-unsupported-dialect"
                : "verification-evidence-trx-invalid";
            yield return [mutation, cause];
        }
    }

    [Theory]
    [InlineData("json")]
    [InlineData("markdown")]
    public void StrictUtf8BomPrefixAndPermittedZeroFailedOutcomesAreReported(string format)
    {
        using var host = new TempHost();
        var utf8Bytes = MutateSmallTrx("unicode-name");
        host.WriteTrx("unicode.trx", utf8Bytes);
        var (unicodeExit, unicodeOutput) = Invoke(host,
            RecordArgs(host, format, Hash(host.PacketBytes), "unicode.trx"));
        var unicode = Result(unicodeOutput, format);
        Assert.Equal(0, unicodeExit);
        Assert.Equal(Hash(utf8Bytes), Text(unicode, "trx_sha256"));
        Assert.Equal("passed", Text(unicode.GetProperty("report"), "reported_outcome"));

        var zeroBytes = MutateSmallTrx("zero-row");
        host.WriteTrx("zero.trx", zeroBytes);
        var (zeroExit, zeroOutput) = Invoke(host,
            RecordArgs(host, format, Hash(host.PacketBytes), "zero.trx"));
        var zero = Result(zeroOutput, format);
        Assert.Equal(0, zeroExit);
        Assert.Equal("not-observed", Text(zero.GetProperty("report"), "reported_outcome"));
        Assert.Equal(0, zero.GetProperty("report").GetProperty("total").GetInt32());

        var failedBytes = MutateSmallTrx("failed-row");
        host.WriteTrx("failed.trx", failedBytes);
        var (failedExit, failedOutput) = Invoke(host,
            RecordArgs(host, format, Hash(host.PacketBytes), "failed.trx"));
        var failed = Result(failedOutput, format);
        Assert.Equal(0, failedExit);
        Assert.Equal("failed", Text(failed.GetProperty("report"), "reported_outcome"));
        Assert.Equal(1, failed.GetProperty("report").GetProperty("failed").GetInt32());
        Assert.False(Directory.Exists(host.EvidenceRoot));
    }

    [Theory]
    [InlineData("passed-with-skips")]
    [InlineData("skipped")]
    [InlineData("failed")]
    [InlineData("not-observed")]
    public void ValidSkippedFailedAndZeroReportsSurviveWriteShowAndValidate(string expectedOutcome)
    {
        var bytes = expectedOutcome switch
        {
            "passed-with-skips" => MutateSmallTrx("passed-with-skips-row"),
            "skipped" => MutateSmallTrx("skipped-row"),
            "failed" => MutateSmallTrx("failed-row"),
            "not-observed" => MutateSmallTrx("zero-row"),
            _ => throw new ArgumentOutOfRangeException(nameof(expectedOutcome)),
        };

        foreach (var format in new[] { "json", "markdown" })
        {
            using var host = new TempHost();
            host.WriteTrx("capture.trx", bytes);
            var packetSha = Hash(host.PacketBytes);
            var (writeExit, writeOutput) = Invoke(host,
                [.. RecordArgs(host, format, packetSha, "capture.trx"), "--write"]);
            var written = Result(writeOutput, format);
            Assert.Equal(0, writeExit);
            Assert.Equal("recorded", Text(written, "disposition"));
            Assert.Equal(expectedOutcome, Text(written.GetProperty("report"), "reported_outcome"));

            foreach (var operation in new[] { "show", "validate" })
            {
                var (exit, output) = Invoke(host, ReadArgs(operation, format));
                var result = Result(output, format);
                Assert.Equal(0, exit);
                Assert.Equal("current", Text(result, "state"));
                Assert.Equal(expectedOutcome, Text(result.GetProperty("report"), "reported_outcome"));
                Assert.Equal(Hash(bytes), Text(result, "trx_sha256"));
            }
        }
    }

    [Theory]
    [InlineData("json")]
    [InlineData("markdown")]
    public void MalformedEncodingTruncatedXmlAndMultipleDocumentsRefuse(string format)
    {
        var good = ReadSmallVstest();
        byte[] malformedUtf8 = [.. good, (byte)0xff];
        var truncated = good[..^20];
        byte[] secondDocument = [.. good, .. good];
        var utf16Text = Encoding.UTF8.GetString(good).TrimStart('\uFEFF');
        byte[] utf16Bytes = [.. Encoding.Unicode.GetPreamble(), .. Encoding.Unicode.GetBytes(utf16Text)];
        var wrongEncoding = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(good).TrimStart('\uFEFF')
            .Replace("encoding=\"utf-8\"", "encoding=\"utf-16\"", StringComparison.OrdinalIgnoreCase));
        foreach (var (name, bytes, cause) in new[]
        {
            ("invalid-utf8", malformedUtf8, "verification-evidence-trx-invalid"),
            ("truncated", truncated, "verification-evidence-trx-invalid"),
            ("multiple-documents", secondDocument, "verification-evidence-trx-invalid"),
            ("wrong-encoding", wrongEncoding, "verification-evidence-unsupported-dialect"),
            ("utf16-bom", utf16Bytes, "verification-evidence-trx-invalid")
        })
        {
            using var host = new TempHost();
            host.WriteTrx(name + ".trx", bytes);
            var (exit, output) = Invoke(host,
                RecordArgs(host, format, Hash(host.PacketBytes), name + ".trx"));
            var result = Result(output, format);
            Assert.Equal(1, exit);
            Assert.Equal("invalid", Text(result, "state"));
            Assert.Equal(cause, Text(result, "cause"));
            Assert.Equal(Hash(bytes), Text(result, "trx_sha256"));
            Assert.False(Directory.Exists(host.EvidenceRoot));
        }
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("with-result")]
    public void TestResultAggregationIsExplicitlyUnsupportedWhileOrdinaryVstestOutputRemainsAccepted(string aggregation)
    {
        Assert.True(TrxEvidenceReader.Read(ReadSmallVstest()).Success);
        var mutation = aggregation == "empty" ? "aggregation-empty" : "aggregation-with-result";
        var bytes = MutateSmallTrx(mutation);
        Assert.False(ReadSmallVstest().AsSpan().SequenceEqual(bytes));
        foreach (var format in new[] { "json", "markdown" })
        {
            using var host = new TempHost();
            host.WriteTrx("aggregation.trx", bytes);
            var (exit, output) = Invoke(host,
                RecordArgs(host, format, Hash(host.PacketBytes), "aggregation.trx"));
            var result = Result(output, format);
            Assert.Equal(1, exit);
            Assert.Equal("invalid", Text(result, "state"));
            Assert.Equal("verification-evidence-unsupported-dialect", Text(result, "cause"));
            Assert.Contains("TestResultAggregation", Text(result, "detail"), StringComparison.Ordinal);
            Assert.Equal(Hash(bytes), Text(result, "trx_sha256"));
            Assert.False(Directory.Exists(host.EvidenceRoot));
        }
    }

    [Theory]
    [InlineData("json")]
    [InlineData("markdown")]
    public async Task ExternalDtdIsRejectedWithoutConnectingToItsLoopbackEntity(string format)
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        using var cancellation = new CancellationTokenSource();
        var accepted = 0;
        var acceptLoop = Task.Run(async () =>
        {
            try
            {
                while (!cancellation.IsCancellationRequested)
                {
                    using var client = await listener.AcceptTcpClientAsync(cancellation.Token);
                    Interlocked.Increment(ref accepted);
                    var response = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
                    await client.GetStream().WriteAsync(response, cancellation.Token);
                }
            }
            catch (OperationCanceledException) { }
            catch (SocketException) { }
        });

        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var xml = Encoding.UTF8.GetString(ReadSmallVstest()).TrimStart('\uFEFF');
        var rootStart = xml.IndexOf("<TestRun", StringComparison.Ordinal);
        Assert.True(rootStart >= 0);
        var withDtd = xml.Insert(rootStart,
                $"<!DOCTYPE TestRun [<!ENTITY external SYSTEM \"http://127.0.0.1:{port}/must-not-fetch\">]>")
            .Replace("</TestRun>", "<Metadata>&external;</Metadata></TestRun>", StringComparison.Ordinal);
        using var host = new TempHost();
        var bytes = Encoding.UTF8.GetBytes(withDtd);
        host.WriteTrx("external.trx", bytes);
        var (exit, output) = Invoke(host, RecordArgs(host, format, Hash(host.PacketBytes), "external.trx"));

        cancellation.Cancel();
        listener.Stop();
        await acceptLoop;

        var result = Result(output, format);
        Assert.Equal(1, exit);
        Assert.Equal("invalid", Text(result, "state"));
        Assert.Equal("verification-evidence-trx-invalid", Text(result, "cause"));
        Assert.Equal(0, Volatile.Read(ref accepted));
        Assert.False(Directory.Exists(host.EvidenceRoot));
    }

    [Theory]
    [InlineData("internal")]
    [InlineData("file")]
    public void InternalAndFileEntitiesAreRejectedBeforeExpansion(string entityKind)
    {
        foreach (var format in new[] { "json", "markdown" })
        {
            using var host = new TempHost();
            using var outside = new TempHost();
            var sentinelPath = Path.Combine(outside.Root, "entity-sentinel.txt");
            var sentinel = Encoding.UTF8.GetBytes("entity sentinel remains unchanged\n");
            File.WriteAllBytes(sentinelPath, sentinel);
            var systemId = entityKind == "internal" ? null : new Uri(sentinelPath).AbsoluteUri;
            var declaration = systemId is null
                ? "<!DOCTYPE TestRun [<!ENTITY probe \"expanded\">]>"
                : $"<!DOCTYPE TestRun [<!ENTITY probe SYSTEM \"{systemId}\">]>";
            var xml = Encoding.UTF8.GetString(ReadSmallVstest()).TrimStart('\uFEFF');
            var rootStart = xml.IndexOf("<TestRun", StringComparison.Ordinal);
            Assert.True(rootStart >= 0);
            var hostile = xml.Insert(rootStart, declaration)
                .Replace("</TestRun>", "<Metadata>&probe;</Metadata></TestRun>", StringComparison.Ordinal);
            var bytes = Encoding.UTF8.GetBytes(hostile);
            host.WriteTrx("entity.trx", bytes);
            var (exit, output) = Invoke(host,
                RecordArgs(host, format, Hash(host.PacketBytes), "entity.trx"));
            var result = Result(output, format);
            Assert.Equal(1, exit);
            Assert.Equal("verification-evidence-trx-invalid", Text(result, "cause"));
            Assert.Equal(Hash(bytes), Text(result, "trx_sha256"));
            Assert.False(Directory.Exists(host.EvidenceRoot));
            Assert.Equal(sentinel, File.ReadAllBytes(sentinelPath));
            Assert.DoesNotContain("entity sentinel", output, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ExactRawByteDepthAndResultCountLimitsAreEnforcedAtTheCommand()
    {
        using var host = new TempHost();

        var exactBytes = PadTrxWithComment(
            BuildBoundedTrx(1, 16, TrxEvidenceReader.MaximumDepth - 1),
            TrxEvidenceReader.MaximumBytes);
        Assert.Equal(TrxEvidenceReader.MaximumBytes, exactBytes.Length);
        using var exactHost = new TempHost();
        exactHost.WriteTrx("exact-limit.trx", exactBytes);
        var (exactExit, exactOutput) = Invoke(exactHost,
            [.. RecordArgs(exactHost, "json", Hash(exactHost.PacketBytes), "exact-limit.trx"), "--write"]);
        var exact = Result(exactOutput, "json");
        Assert.Equal(0, exactExit);
        Assert.Equal("recorded", Text(exact, "disposition"));
        Assert.Equal(Hash(exactBytes), Text(exact, "trx_sha256"));
        var exactArtifactPath = exactHost.Absolute(Text(exact, "artifact_path"));
        var exactArtifactBytes = File.ReadAllBytes(exactArtifactPath);
        Assert.True(exactArtifactBytes.Length <= VerificationEvidenceStore.MaximumArtifactBytes);
        using (var exactRecord = JsonDocument.Parse(exactArtifactBytes))
            Assert.Equal(exactBytes, Convert.FromBase64String(Text(exactRecord.RootElement, "trx_base64")));

        var largePacket = BuildLargeBindingPacket();
        Assert.InRange(Encoding.UTF8.GetByteCount(largePacket), 1, PacketVerificationContract.MaximumBytes);
        using var combinedLimitHost = new TempHost(packet: largePacket);
        combinedLimitHost.WriteTrx("combined-limit.trx", exactBytes);
        var (combinedExit, combinedOutput) = Invoke(combinedLimitHost,
            RecordArgs(combinedLimitHost, "markdown", Hash(combinedLimitHost.PacketBytes), "combined-limit.trx", proof: "P1"));
        var combined = Result(combinedOutput, "markdown");
        Assert.Equal(1, combinedExit);
        Assert.Equal("unavailable", Text(combined, "state"));
        Assert.Equal("verification-evidence-resource-limit", Text(combined, "cause"));
        Assert.Null(combined.GetProperty("artifact_path").GetString());
        Assert.Null(combined.GetProperty("artifact_sha256").GetString());
        Assert.Equal(Hash(exactBytes), Text(combined, "trx_sha256"));
        Assert.False(Directory.Exists(combinedLimitHost.EvidenceRoot));

        byte[] overBytes = [.. exactBytes, (byte)' '];
        host.WriteTrx("over-limit.trx", overBytes);
        var (overExit, overOutput) = Invoke(host,
            RecordArgs(host, "markdown", Hash(host.PacketBytes), "over-limit.trx"));
        var over = Result(overOutput, "markdown");
        Assert.Equal(1, overExit);
        Assert.Equal("verification-evidence-resource-limit", Text(over, "cause"));
        Assert.Equal(JsonValueKind.Null, over.GetProperty("trx_sha256").ValueKind);

        var exactDepth = BuildBoundedTrx(1, 16, TrxEvidenceReader.MaximumDepth - 1);
        host.WriteTrx("depth-32.trx", exactDepth);
        var (depth32Exit, depth32Output) = Invoke(host,
            RecordArgs(host, "json", Hash(host.PacketBytes), "depth-32.trx"));
        Assert.Equal(0, depth32Exit);
        Assert.Equal("planned", Text(Result(depth32Output, "json"), "disposition"));

        var overDepth = BuildBoundedTrx(1, 16, TrxEvidenceReader.MaximumDepth);
        host.WriteTrx("depth-33.trx", overDepth);
        var (depth33Exit, depth33Output) = Invoke(host,
            RecordArgs(host, "markdown", Hash(host.PacketBytes), "depth-33.trx"));
        var depth33 = Result(depth33Output, "markdown");
        Assert.Equal(1, depth33Exit);
        Assert.Equal("verification-evidence-resource-limit", Text(depth33, "cause"));

        var maxRows = BuildBoundedTrx(TrxEvidenceReader.MaximumRows, 12);
        host.WriteTrx("rows-maximum.trx", maxRows);
        var (rowsExit, rowsOutput) = Invoke(host,
            RecordArgs(host, "json", Hash(host.PacketBytes), "rows-maximum.trx"));
        var rows = Result(rowsOutput, "json");
        Assert.Equal(0, rowsExit);
        Assert.Equal(TrxEvidenceReader.MaximumRows, rows.GetProperty("report").GetProperty("total").GetInt32());

        var overRows = BuildBoundedTrx(TrxEvidenceReader.MaximumRows + 1, 12);
        host.WriteTrx("rows-over-maximum.trx", overRows);
        var (overRowsExit, overRowsOutput) = Invoke(host,
            RecordArgs(host, "markdown", Hash(host.PacketBytes), "rows-over-maximum.trx"));
        var tooMany = Result(overRowsOutput, "markdown");
        Assert.Equal(1, overRowsExit);
        Assert.Equal("verification-evidence-resource-limit", Text(tooMany, "cause"));
        Assert.False(Directory.Exists(host.EvidenceRoot));
    }

    [Theory]
    [InlineData("json")]
    [InlineData("markdown")]
    public void TrxNamesAcceptExactUtf16LimitAndRejectOneCodeUnitOver(string format)
    {
        using var host = new TempHost();
        var exactDefinitionName = MutateSmallTrx("exact-definition-name");
        host.WriteTrx("definition-name-exact.trx", exactDefinitionName);
        var (definitionExactExit, definitionExactOutput) = Invoke(host,
            RecordArgs(host, format, Hash(host.PacketBytes), "definition-name-exact.trx"));
        var definitionExact = Result(definitionExactOutput, format);
        Assert.Equal(0, definitionExactExit);
        Assert.Equal("planned", Text(definitionExact, "disposition"));
        Assert.Equal(2, definitionExact.GetProperty("report").GetProperty("total").GetInt32());
        Assert.Contains(definitionExact.GetProperty("report").GetProperty("discrepancies").EnumerateArray(), item =>
            Text(item, "code") == "definition-name-differs");
        Assert.False(Directory.Exists(host.EvidenceRoot));

        var exact = BuildBoundedTrx(1, TrxEvidenceReader.MaximumNameLength);
        host.WriteTrx("name-exact.trx", exact);
        var (exactExit, exactOutput) = Invoke(host,
            RecordArgs(host, format, Hash(host.PacketBytes), "name-exact.trx"));
        Assert.Equal(0, exactExit);
        Assert.Equal(1, Result(exactOutput, format).GetProperty("report").GetProperty("total").GetInt32());

        var over = BuildBoundedTrx(1, TrxEvidenceReader.MaximumNameLength + 1);
        host.WriteTrx("name-over.trx", over);
        var (overExit, overOutput) = Invoke(host,
            RecordArgs(host, format, Hash(host.PacketBytes), "name-over.trx"));
        var refused = Result(overOutput, format);
        Assert.Equal(1, overExit);
        Assert.Equal("verification-evidence-unsupported-dialect", Text(refused, "cause"));
        Assert.False(Directory.Exists(host.EvidenceRoot));
    }

    [Theory]
    [InlineData("json")]
    [InlineData("markdown")]
    public void OriginalCliVstestReportKeepsSkippedAndTruncatedNameDiscrepancies(string format)
    {
        using var host = new TempHost();
        var trx = ReadFullVstest();
        host.WriteTrx("full.trx", trx);
        var (exit, output) = Invoke(host, RecordArgs(host, format, Hash(host.PacketBytes), "full.trx"));
        var result = Result(output, format);
        Assert.Equal(0, exit);
        Assert.Equal("planned", Text(result, "disposition"));
        Assert.Equal(Hash(trx), Text(result, "trx_sha256"));
        var report = result.GetProperty("report");
        Assert.Equal("passed-with-skips", Text(report, "reported_outcome"));
        Assert.Equal(9543, report.GetProperty("total").GetInt32());
        Assert.Equal(9537, report.GetProperty("passed").GetInt32());
        Assert.Equal(6, report.GetProperty("skipped").GetInt32());
        Assert.Equal(0, report.GetProperty("failed").GetInt32());
        Assert.Equal(0, report.GetProperty("raw_counters").GetProperty("not_executed").GetInt32());
        var discrepancies = report.GetProperty("discrepancies").EnumerateArray().ToArray();
        Assert.Equal(new[] { "definition-name-differs", "not-executed-counter-differs" },
            discrepancies.Select(item => Text(item, "code")).ToArray());
        Assert.Equal(4, discrepancies[0].GetProperty("count").GetInt32());
        Assert.False(discrepancies[0].TryGetProperty("declared", out _));
        Assert.False(discrepancies[0].TryGetProperty("derived", out _));
        Assert.Equal(0, discrepancies[1].GetProperty("declared").GetInt32());
        Assert.Equal(6, discrepancies[1].GetProperty("derived").GetInt32());
        Assert.False(discrepancies[1].TryGetProperty("count", out _));
        Assert.DoesNotContain("testName", output, StringComparison.Ordinal);
    }

    internal static int Invoke(TempHost host, string[] args, Action<string, string>? beforeOperation,
        out string text)
    {
        using var writer = new StringWriter();
        var exit = VerificationEvidenceCommand.Execute(host.Root, args, writer, beforeOperation);
        text = writer.ToString();
        return exit;
    }

    internal static (int Exit, string Text) Invoke(TempHost host, string[] args,
        Action<string, string>? beforeOperation = null)
    {
        var exit = Invoke(host, args, beforeOperation, out var text);
        return (exit, text);
    }

    internal static string[] RecordArgs(TempHost host, string format, string packetSha, string trxPath,
        string? id = null, string? unit = null, string? domain = null, string? team = null,
        string? repo = null, string proof = "P1") =>
    ["record", "--id", id ?? Id, "--execution-unit", unit ?? host.Unit, "--domain", domain ?? Domain,
     "--team", team ?? Team, "--repo", repo ?? Repo, "--head-sha", Head,
     "--packet-sha256", packetSha, "--proof", proof, "--trx", trxPath, "--format", format];

    internal static string[] ReadArgs(string operation, string format, string? head = null,
        string? id = null, string? unit = null, string? domain = null, string? team = null, string? repo = null) =>
    [operation, "--id", id ?? Id, "--execution-unit", unit ?? Unit, "--domain", domain ?? Domain,
     "--team", team ?? Team, "--repo", repo ?? Repo, "--head-sha", head ?? Head, "--format", format];

    internal static JsonElement Result(string output, string format)
    {
        JsonElement root;
        if (format == "json")
        {
            using var document = JsonDocument.Parse(output);
            root = document.RootElement.Clone();
        }
        else
        {
            const string marker = "## Structured result (same facts as JSON)";
            var index = output.IndexOf(marker, StringComparison.Ordinal);
            Assert.True(index >= 0, "Markdown must include the structured JSON facts.");
            var fenceStart = output.IndexOf("```", index, StringComparison.Ordinal);
            Assert.True(fenceStart >= 0, "Markdown structured JSON fence is missing.");
            var lineStart = output.LastIndexOf('\n', fenceStart) + 1;
            var lineEnd = output.IndexOf('\n', lineStart);
            Assert.True(lineEnd > lineStart, "Markdown JSON fence line is missing.");
            var opening = output[lineStart..lineEnd];
            Assert.EndsWith("json", opening, StringComparison.Ordinal);
            var fence = opening[..^"json".Length];
            Assert.True(fence.Length >= 3 && fence.All(character => character == '`'));
            var jsonStart = lineEnd + 1;
            var fenceEnd = output.IndexOf("\n" + fence, jsonStart, StringComparison.Ordinal);
            Assert.True(fenceEnd > jsonStart, "Markdown JSON fence is not closed.");
            using var result = JsonDocument.Parse(output[jsonStart..fenceEnd]);
            root = result.RootElement.Clone();
        }

        if (root.TryGetProperty("diagnostics", out var diagnostics))
        {
            Assert.Equal(JsonValueKind.Array, diagnostics.ValueKind);
            foreach (var diagnostic in diagnostics.EnumerateArray())
                Assert.True(Text(diagnostic, "cause").StartsWith("verification-evidence-", StringComparison.Ordinal),
                    "Every public verification evidence diagnostic cause must be namespaced.");
        }
        return root;
    }

    internal static string Text(JsonElement parent, string property) => parent.GetProperty(property).GetString()!;
    internal static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    internal static byte[] ReadSmallVstest()
    {
        using var stream = typeof(VerificationEvidenceCommandTests).Assembly.GetManifestResourceStream(SmallTrxResource)
            ?? throw new InvalidOperationException("G866 authentic VSTest fixture is missing.");
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }

    private static byte[] ReadFullVstest()
    {
        using var resource = typeof(VerificationEvidenceCommandTests).Assembly.GetManifestResourceStream("g866-full-cli-vstest.trx.gz")
            ?? throw new InvalidOperationException("G866 original CLI VSTest fixture is missing.");
        using var compressed = new GZipStream(resource, CompressionMode.Decompress);
        using var memory = new MemoryStream();
        compressed.CopyTo(memory);
        return memory.ToArray();
    }

    internal const string ValidPacket = """
        implementation_issue_packet:
          source_execution_unit: G866
          domain: intent-cli
          team: intent-cli-dev
          target_repo: J-Tech-Japan/intent-system
          acceptance_criteria:
            - Preserve supplied local test bytes
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

    private const string TwoCriterionPacket = """
        implementation_issue_packet:
          source_execution_unit: G866
          domain: intent-cli
          team: intent-cli-dev
          target_repo: J-Tech-Japan/intent-system
          acceptance_criteria:
            - Preserve the first independently verified behavior
            - Preserve the second independently verified behavior
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
              command: dotnet test --no-restore
              evidence: local-required
        """;

    private static string BuildLargeBindingPacket()
    {
        const int criteriaCount = 128;
        const int textLength = 3_800;
        var text = "A" + new string('é', textLength - 1);
        var output = new StringBuilder(1_050_000);
        output.AppendLine("implementation_issue_packet:")
            .AppendLine("  source_execution_unit: G866")
            .AppendLine("  domain: intent-cli")
            .AppendLine("  team: intent-cli-dev")
            .AppendLine("  target_repo: J-Tech-Japan/intent-system")
            .AppendLine("  acceptance_criteria:");
        for (var index = 1; index <= criteriaCount; index++)
            output.Append("    - \"").Append(text).AppendLine("\"");
        output.AppendLine("verification_contract:")
            .AppendLine("  version: 1")
            .AppendLine("  acceptance_criteria:");
        for (var index = 1; index <= criteriaCount; index++)
            output.Append("    - {id: AC").Append(index.ToString("D3", System.Globalization.CultureInfo.InvariantCulture))
                .Append(", criterion_index: ").Append(index.ToString(System.Globalization.CultureInfo.InvariantCulture)).AppendLine("}");
        output.AppendLine("  lanes:")
            .AppendLine("    - {id: unit, weight: ordinary}")
            .AppendLine("  affected_lanes: [unit]")
            .AppendLine("  proofs:")
            .AppendLine("    - id: P1")
            .AppendLine("      acceptance_criteria: [");
        for (var index = 1; index <= criteriaCount; index++)
            output.Append("        AC").Append(index.ToString("D3", System.Globalization.CultureInfo.InvariantCulture))
                .AppendLine(index == criteriaCount ? "]" : ",");
        output.AppendLine("      lane: unit")
            .AppendLine("      command: dotnet test")
            .AppendLine("      evidence: local-required");
        return output.ToString();
    }

    private static byte[] MutateSmallTrx(string mutation)
    {
        const string nsText = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";
        XNamespace ns = nsText;
        var source = Encoding.UTF8.GetString(ReadSmallVstest()).TrimStart('\uFEFF');
        var document = XDocument.Parse(source, LoadOptions.None);
        var root = document.Root!;
        var result = root.Element(ns + "Results")!.Elements(ns + "UnitTestResult").First();
        var results = root.Element(ns + "Results")!;
        var definitions = root.Element(ns + "TestDefinitions")!;
        var entries = root.Element(ns + "TestEntries")!;
        var summary = root.Element(ns + "ResultSummary")!;
        var counters = summary.Element(ns + "Counters")!;

        switch (mutation)
        {
            case "total": counters.SetAttributeValue("total", "3"); break;
            case "executed": counters.SetAttributeValue("executed", "3"); break;
            case "passed": counters.SetAttributeValue("passed", "3"); break;
            case "failed": counters.SetAttributeValue("failed", "1"); break;
            case "not-executed": counters.SetAttributeValue("notExecuted", "1"); break;
            case "root-guid": root.SetAttributeValue("id", "not-a-guid"); break;
            case "aborted-counter": counters.SetAttributeValue("aborted", "1"); break;
            case "duplicate-entry": entries.Add(new XElement(entries.Elements(ns + "TestEntry").First())); break;
            case "missing-result": results.Elements(ns + "UnitTestResult").First().Remove(); break;
            case "bad-guid": result.SetAttributeValue("testId", "not-a-guid"); break;
            case "bad-name": result.SetAttributeValue("testName", "  "); break;
            case "long-name": result.SetAttributeValue("testName", new string('x', TrxEvidenceReader.MaximumNameLength + 1)); break;
            case "bad-definition-name":
                definitions.Elements(ns + "UnitTest").First(item =>
                    (string?)item.Attribute("id") == (string?)result.Attribute("testId")).SetAttributeValue("name", "  ");
                break;
            case "long-definition-name":
                definitions.Elements(ns + "UnitTest").First(item =>
                    (string?)item.Attribute("id") == (string?)result.Attribute("testId")).SetAttributeValue("name",
                        new string('x', TrxEvidenceReader.MaximumNameLength + 1));
                break;
            case "exact-definition-name":
                definitions.Elements(ns + "UnitTest").First(item =>
                    (string?)item.Attribute("id") == (string?)result.Attribute("testId")).SetAttributeValue("name",
                        new string('x', TrxEvidenceReader.MaximumNameLength));
                break;
            case "bad-outcome": result.SetAttributeValue("outcome", "Aborted"); break;
            case "duplicate-execution":
                result.SetAttributeValue("executionId", results.Elements(ns + "UnitTestResult").Skip(1).First().Attribute("executionId")!.Value);
                break;
            case "missing-entry": entries.Elements(ns + "TestEntry").First().Remove(); break;
            case "extra-entry": entries.Add(new XElement(ns + "TestEntry",
                new XAttribute("testId", "11111111-1111-4111-8111-111111111111"),
                new XAttribute("executionId", "22222222-2222-4222-8222-222222222222")));
                break;
            case "missing-definition":
                definitions.Elements(ns + "UnitTest").First(item => (string?)item.Attribute("id") == (string?)result.Attribute("testId")).Remove();
                break;
            case "extra-definition":
                definitions.Add(new XElement(ns + "UnitTest", new XAttribute("id", "33333333-3333-4333-8333-333333333333"),
                    new XAttribute("name", "unused definition"), new XElement(ns + "Execution",
                        new XAttribute("id", "44444444-4444-4444-8444-444444444444"))));
                break;
            case "wrong-definition-execution":
                definitions.Elements(ns + "UnitTest").First().Element(ns + "Execution")!
                    .SetAttributeValue("id", "55555555-5555-4555-8555-555555555555");
                break;
            case "wrong-entry-execution":
                entries.Elements(ns + "TestEntry").First().SetAttributeValue("executionId", "66666666-6666-4666-8666-666666666666");
                break;
            case "duplicate-result": results.Add(new XElement(result)); break;
            case "duplicate-definition": definitions.Add(new XElement(definitions.Elements(ns + "UnitTest").First())); break;
            case "nested-result": results.Add(new XElement(ns + "Message", new XElement(result))); break;
            case "aggregation-empty": results.Add(new XElement(ns + "TestResultAggregation")); break;
            case "aggregation-with-result": results.Add(new XElement(ns + "TestResultAggregation", new XElement(result))); break;
            case "wrong-root-namespace":
                root.Name = XName.Get("TestRun", "urn:g866:wrong");
                root.SetAttributeValue("xmlns", "urn:g866:wrong");
                break;
            case "wrong-result-namespace": result.Name = XName.Get("UnitTestResult", "urn:g866:wrong"); break;
            case "wrong-execution-namespace":
                definitions.Elements(ns + "UnitTest").First().Element(ns + "Execution")!.Name = XName.Get("Execution", "urn:g866:wrong");
                break;
            case "missing-results-section": results.Remove(); break;
            case "duplicate-results-section": root.Add(new XElement(results)); break;
            case "misplaced-counters": counters.Remove(); root.Add(counters); break;
            case "stray-execution": root.Add(new XElement(ns + "Execution", new XAttribute("id", "33333333-3333-4333-8333-333333333333"))); break;
            case "stray-counters": root.Add(new XElement(ns + "Counters")); break;
            case "wrong-summary-outcome": summary.SetAttributeValue("outcome", "Failed"); break;
            case "unicode-name":
                result.SetAttributeValue("testName", "Résumé 測試");
                var definition = definitions.Elements(ns + "UnitTest").First(item =>
                    (string?)item.Attribute("id") == (string?)result.Attribute("testId"));
                definition.SetAttributeValue("name", "Résumé 測試");
                break;
            case "zero-row":
                results.Elements(ns + "UnitTestResult").Remove();
                entries.Elements(ns + "TestEntry").Remove();
                definitions.Elements(ns + "UnitTest").Remove();
                foreach (var name in new[] { "total", "executed", "passed", "failed", "notExecuted" }) counters.SetAttributeValue(name, "0");
                break;
            case "failed-row":
                result.SetAttributeValue("outcome", "Failed");
                counters.SetAttributeValue("passed", "1");
                counters.SetAttributeValue("failed", "1");
                summary.SetAttributeValue("outcome", "Failed");
                break;
            case "passed-with-skips-row":
                result.SetAttributeValue("outcome", "NotExecuted");
                counters.SetAttributeValue("executed", "1");
                counters.SetAttributeValue("passed", "1");
                counters.SetAttributeValue("notExecuted", "0");
                summary.SetAttributeValue("outcome", "Completed");
                break;
            case "skipped-row":
                foreach (var item in results.Elements(ns + "UnitTestResult")) item.SetAttributeValue("outcome", "NotExecuted");
                foreach (var name in new[] { "executed", "passed", "failed" }) counters.SetAttributeValue(name, "0");
                counters.SetAttributeValue("notExecuted", "2");
                summary.SetAttributeValue("outcome", "Completed");
                break;
            default: throw new ArgumentOutOfRangeException(nameof(mutation), mutation, "Unknown TRX mutation.");
        }

        return Encoding.UTF8.GetBytes(document.ToString(SaveOptions.DisableFormatting));
    }

    private static byte[] PrefixTrxNamespace(byte[] bytes)
    {
        const string namespaceUri = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";
        var xml = Encoding.UTF8.GetString(bytes).TrimStart('\uFEFF');
        xml = xml.Replace($"xmlns=\"{namespaceUri}\"", $"xmlns:v=\"{namespaceUri}\"", StringComparison.Ordinal);
        foreach (var name in new[]
        {
            "UnitTestResult", "ResultSummary", "TestDefinitions", "TestEntries", "TestRun",
            "UnitTest", "Results", "Execution", "TestEntry", "Counters"
        })
        {
            xml = xml.Replace("<" + name, "<v:" + name, StringComparison.Ordinal);
            xml = xml.Replace("</" + name + ">", "</v:" + name + ">", StringComparison.Ordinal);
        }
        return Encoding.UTF8.GetBytes(xml);
    }

    private static string MutateIdentityPacket(string field, string mutation)
    {
        var originals = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["source_execution_unit"] = "G866",
            ["domain"] = "intent-cli",
            ["team"] = "intent-cli-dev",
            ["target_repo"] = "J-Tech-Japan/intent-system",
        };
        var original = originals[field];
        var line = $"  {field}: {original}\n";
        var caseValue = field == "source_execution_unit" ? "g866" : original.ToUpperInvariant();
        return mutation switch
        {
            "missing" => ValidPacket.Replace(line, string.Empty, StringComparison.Ordinal),
            "wrong-type" => ValidPacket.Replace(line, $"  {field}: [{original}]\n", StringComparison.Ordinal),
            "tagged" => ValidPacket.Replace(line, $"  {field}: !str {original}\n", StringComparison.Ordinal),
            "aliased" => $"metadata: &identityValue {original}\n" + ValidPacket.Replace(line,
                $"  {field}: *identityValue\n", StringComparison.Ordinal),
            "key-aliased" => $"metadata: &identityKey {field}\n" + ValidPacket.Replace(line,
                $"  *identityKey: {original}\n", StringComparison.Ordinal),
            "duplicate" => ValidPacket.Replace(line, line + $"  {field}: duplicate\n", StringComparison.Ordinal),
            "case" => ValidPacket.Replace(line, $"  {field}: {caseValue}\n", StringComparison.Ordinal),
            _ => throw new ArgumentOutOfRangeException(nameof(mutation), mutation, "Unknown packet mutation.")
        };
    }

    private static string MutateValidAlternateBinding(string packet, string mutation) => mutation switch
    {
        "criterion-index" => packet
            .Replace("{id: AC1, criterion_index: 1}", "{id: AC1, criterion_index: 2}", StringComparison.Ordinal)
            .Replace("{id: AC2, criterion_index: 2}", "{id: AC2, criterion_index: 1}", StringComparison.Ordinal),
        "proof-reference" => packet
            .Replace("acceptance_criteria: [AC1]", "acceptance_criteria: [__swap__]", StringComparison.Ordinal)
            .Replace("acceptance_criteria: [AC2]", "acceptance_criteria: [AC1]", StringComparison.Ordinal)
            .Replace("acceptance_criteria: [__swap__]", "acceptance_criteria: [AC2]", StringComparison.Ordinal),
        "proof-lane" => packet
            .Replace("lane: unit", "lane: __swap__", StringComparison.Ordinal)
            .Replace("lane: build", "lane: unit", StringComparison.Ordinal)
            .Replace("lane: __swap__", "lane: build", StringComparison.Ordinal),
        _ => throw new ArgumentOutOfRangeException(nameof(mutation), mutation, "Unknown alternate binding mutation.")
    };

    private static int CreateFifo(string path) => MkFifo(path, Convert.ToUInt32("600", 8));

    [DllImport("libc", EntryPoint = "mkfifo", SetLastError = true)]
    private static extern int MkFifo([MarshalAs(UnmanagedType.LPUTF8Str)] string path, uint mode);

    private static byte[]? MutateCurrentPacket(string mutation)
    {
        if (mutation == "head") return null;
        var changed = mutation switch
        {
            "packet-byte" => ValidPacket + "\n# unrelated packet byte changed\n",
            "proof-command" => ValidPacket.Replace("command: dotnet test", "command: dotnet test --no-restore", StringComparison.Ordinal),
            "criterion-text" => ValidPacket.Replace("Preserve supplied local test bytes", "Preserve the updated local test bytes", StringComparison.Ordinal),
            "criterion-index" => ValidPacket.Replace("criterion_index: 1", "criterion_index: 2", StringComparison.Ordinal),
            "proof-reference" => ValidPacket.Replace("acceptance_criteria: [AC1]", "acceptance_criteria: [AC2]", StringComparison.Ordinal),
            "proof-lane" => ValidPacket.Replace("lane: unit", "lane: missing", StringComparison.Ordinal),
            "proof-evidence" => ValidPacket.Replace("evidence: local-required", "evidence: hosted-required", StringComparison.Ordinal),
            _ => throw new ArgumentOutOfRangeException(nameof(mutation), mutation, "Unknown current-packet mutation.")
        };
        return Encoding.UTF8.GetBytes(changed + "\n");
    }

    private static byte[] BuildBoundedTrx(int rows, int nameLength, int nestedMetadataElements = 0)
    {
        if (rows < 0 || nameLength <= 0 || nestedMetadataElements < 0)
            throw new ArgumentOutOfRangeException(nameof(rows));
        var name = new string('x', nameLength);
        var output = new StringBuilder(Math.Max(1024, rows * 340));
        output.Append("<TestRun xmlns=\"http://microsoft.com/schemas/VisualStudio/TeamTest/2010\" id=\"aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa\">");
        output.Append("<Results>");
        for (var index = 1; index <= rows; index++)
        {
            var (testId, executionId) = RowIds(index);
            output.Append("<UnitTestResult testId=\"").Append(testId).Append("\" executionId=\"")
                .Append(executionId).Append("\" testName=\"").Append(name).Append("\" outcome=\"Passed\" />");
        }
        output.Append("</Results><TestDefinitions>");
        for (var index = 1; index <= rows; index++)
        {
            var (testId, executionId) = RowIds(index);
            output.Append("<UnitTest id=\"").Append(testId).Append("\" name=\"").Append(name)
                .Append("\"><Execution id=\"").Append(executionId).Append("\" /></UnitTest>");
        }
        output.Append("</TestDefinitions><TestEntries>");
        for (var index = 1; index <= rows; index++)
        {
            var (testId, executionId) = RowIds(index);
            output.Append("<TestEntry testId=\"").Append(testId).Append("\" executionId=\"").Append(executionId).Append("\" />");
        }
        output.Append("</TestEntries><ResultSummary outcome=\"").Append(rows == 0 ? "Completed" : "Completed")
            .Append("\"><Counters total=\"").Append(rows).Append("\" executed=\"").Append(rows)
            .Append("\" passed=\"").Append(rows).Append("\" failed=\"0\" notExecuted=\"0\" /></ResultSummary>");
        if (nestedMetadataElements > 0)
        {
            for (var index = 0; index < nestedMetadataElements; index++) output.Append(index == 0 ? "<Metadata>" : "<Nested>");
            for (var index = 0; index < nestedMetadataElements; index++) output.Append(index == nestedMetadataElements - 1 ? "</Metadata>" : "</Nested>");
        }
        output.Append("</TestRun>");
        return Encoding.UTF8.GetBytes(output.ToString());
    }

    private static byte[] PadTrxWithComment(byte[] source, int targetLength)
    {
        var xml = Encoding.UTF8.GetString(source).TrimStart('\uFEFF');
        var closing = "</TestRun>";
        var index = xml.LastIndexOf(closing, StringComparison.Ordinal);
        if (index < 0) throw new InvalidOperationException("TRX root close marker is missing.");
        var padding = targetLength - Encoding.UTF8.GetByteCount(xml) - 7;
        if (padding < 0) throw new InvalidOperationException("TRX fixture exceeds requested exact byte size.");
        var comment = "<!--" + new string('x', padding) + "-->";
        return Encoding.UTF8.GetBytes(xml.Insert(index, comment));
    }

    private static (string TestId, string ExecutionId) RowIds(int index) =>
        ($"00000000-0000-4000-8000-{index.ToString("x12", System.Globalization.CultureInfo.InvariantCulture)}",
         $"00000000-0000-4001-8001-{index.ToString("x12", System.Globalization.CultureInfo.InvariantCulture)}");

    internal sealed class TempHost : IDisposable
    {
        public TempHost(string unit = VerificationEvidenceCommandTests.Unit, string? packet = null)
        {
            var tempRoot = OperatingSystem.IsMacOS() && Directory.Exists("/private/tmp") ? "/private/tmp" : Path.GetTempPath();
            Root = Path.Combine(tempRoot, "intent-cli-g866-evidence-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
            Unit = unit;
            PacketPath = Path.Combine(Root, ".intent-cli", "issues", Unit, "packet.yaml");
            Directory.CreateDirectory(Path.GetDirectoryName(PacketPath)!);
            PacketBytes = Encoding.UTF8.GetBytes((packet ?? ValidPacket) + "\n");
            File.WriteAllBytes(PacketPath, PacketBytes);
        }

        public string Root { get; }
        public string Unit { get; }
        public string PacketPath { get; }
        public string EvidenceRoot => Path.Combine(Root, ".intent-cli", "verification-evidence");
        public byte[] PacketBytes { get; private set; }

        public void WriteTrx(string relative, byte[] bytes) => File.WriteAllBytes(Absolute(relative), bytes);
        public void WritePacket(byte[] bytes)
        {
            PacketBytes = bytes;
            File.WriteAllBytes(PacketPath, bytes);
        }
        public string Absolute(string relative) => Path.Combine(Root, relative.Replace('/', Path.DirectorySeparatorChar));

        public void Dispose()
        {
            if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
        }
    }
}
