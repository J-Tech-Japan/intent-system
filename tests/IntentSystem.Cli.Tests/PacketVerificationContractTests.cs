using System.Security.Cryptography;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using IntentSystem.Cli.Commands;

namespace IntentSystem.Cli.Tests;

public sealed class PacketVerificationContractTests
{
    private const string Unit = "G865";
    private const string PacketRelativePath = ".intent-cli/issues/G865/packet.yaml";

    [Theory]
    [InlineData("json")]
    [InlineData("markdown")]
    public void ActualCommandReportsCompleteTwoProofContractWithoutClaimingExecution(string format)
    {
        using var workspace = new TemporaryWorkspace();
        var bytes = Encoding.UTF8.GetBytes(CompletePacket);
        workspace.WritePacket(bytes);

        var (exitCode, output) = Run(workspace.Root, format);

        Assert.Equal(0, exitCode);
        Assert.Contains("complete", output, StringComparison.Ordinal);
        Assert.Contains("not-observed", output, StringComparison.Ordinal);
        Assert.Contains("Structural coverage only", output, StringComparison.Ordinal);
        using var result = JsonDocument.Parse(StructuredJson(output, format));
        var root = result.RootElement;
        Assert.Equal(Unit, root.GetProperty("execution_unit").GetString());
        Assert.Equal("complete", root.GetProperty("state").GetString());
        Assert.Equal("verification-contract-complete", root.GetProperty("cause").GetString());
        Assert.Equal(PacketRelativePath, root.GetProperty("packet_path").GetString());
        Assert.Equal(Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), root.GetProperty("packet_sha256").GetString());
        Assert.Equal("not-observed", root.GetProperty("execution").GetString());
        Assert.Contains("execution, publication, and authority are not observed or verified", root.GetProperty("detail").GetString(), StringComparison.Ordinal);
        Assert.Equal(2, root.GetProperty("acceptance_criteria").GetArrayLength());
        Assert.Equal("AC1", root.GetProperty("acceptance_criteria")[0].GetProperty("id").GetString());
        Assert.Equal(1, root.GetProperty("acceptance_criteria")[0].GetProperty("criterion_index").GetInt32());
        Assert.Equal("Preserve marker identity", root.GetProperty("acceptance_criteria")[0].GetProperty("text").GetString());
        Assert.Equal("AC2", root.GetProperty("acceptance_criteria")[1].GetProperty("id").GetString());
        Assert.Equal(2, root.GetProperty("acceptance_criteria")[1].GetProperty("criterion_index").GetInt32());
        Assert.Equal("Preserve legacy output", root.GetProperty("acceptance_criteria")[1].GetProperty("text").GetString());
        Assert.Equal(2, root.GetProperty("lanes").GetArrayLength());
        Assert.Equal(new[] { "mutation", "unit" }, root.GetProperty("lanes").EnumerateArray().Select(item => item.GetProperty("id").GetString()).ToArray());
        Assert.Equal(2, root.GetProperty("proofs").GetArrayLength());
        Assert.Equal("P1", root.GetProperty("proofs")[0].GetProperty("id").GetString());
        Assert.Equal("AC1", root.GetProperty("proofs")[0].GetProperty("acceptance_criteria")[0].GetString());
        Assert.Equal("npm run test:mutation", root.GetProperty("proofs")[0].GetProperty("command").GetString());
        Assert.Equal("local-required", root.GetProperty("proofs")[0].GetProperty("evidence").GetString());
        Assert.Equal("P2", root.GetProperty("proofs")[1].GetProperty("id").GetString());
        Assert.Equal("dotnet test", root.GetProperty("proofs")[1].GetProperty("command").GetString());
        Assert.Empty(root.GetProperty("missing_acceptance_criteria").EnumerateArray());
        Assert.Empty(root.GetProperty("missing_lanes").EnumerateArray());
        Assert.Empty(root.GetProperty("missing_local_required_lanes").EnumerateArray());
        Assert.All(root.GetProperty("proofs").EnumerateArray(), proof =>
            Assert.Equal("not-observed", proof.GetProperty("execution").GetString()));
        Assert.False(Directory.Exists(Path.Combine(workspace.Root, ".intent-cli", "rulings")));

        var (repeatExitCode, repeatOutput) = Run(workspace.Root, format);
        Assert.Equal(exitCode, repeatExitCode);
        Assert.Equal(output, repeatOutput);
    }

    [Fact]
    public void SixEnglishAndJapaneseDocsDescribeTheSameReadOnlyContractAndLimits()
    {
        var root = FindRepositoryRoot();
        foreach (var language in new[] { "en", "ja" })
        {
            var commandReference = File.ReadAllText(Path.Combine(root, "docs", language, "08-command-reference.md"));
            var orchestration = File.ReadAllText(Path.Combine(root, "docs", language, "12-agent-message-orchestration.md"));
            var ledger = File.ReadAllText(Path.Combine(root, "docs", language, "1.0-compatibility-ledger.md"));
            foreach (var text in new[] { commandReference, orchestration, ledger })
            {
                Assert.Contains("G865", text, StringComparison.Ordinal);
                Assert.Contains("verification_contract", text, StringComparison.Ordinal);
                Assert.Contains("not-observed", text, StringComparison.Ordinal);
                Assert.Contains("#1866", text, StringComparison.Ordinal);
                Assert.Contains("16,384", text, StringComparison.Ordinal);
                Assert.Contains("1 MiB", text, StringComparison.Ordinal);
                Assert.Contains("32", text, StringComparison.Ordinal);
            }
            Assert.Contains("acceptance_criteria", commandReference, StringComparison.Ordinal);
            Assert.Contains("affected_lanes", commandReference, StringComparison.Ordinal);
            Assert.Contains("hosted-required", commandReference, StringComparison.Ordinal);
            Assert.Contains("local-required", commandReference, StringComparison.Ordinal);
            Assert.Contains(language == "en" ? "unsupported" : "未対応", commandReference, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(language == "en" ? "not a gate" : "gate ではありません", commandReference, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(language == "en" ? "Omitting the field returns `not-declared`" : "field を省略すると `not-declared`", commandReference, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(language == "en" ? "present empty or" : "存在して空または null", commandReference, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("[A-Za-z][A-Za-z0-9_-]{0,63}", commandReference, StringComparison.Ordinal);
            Assert.Contains(
                language == "en" ? "128 actual criteria and registry entries; 64 lanes; 256 proofs" : "actual criteria と registry は各128件、lane 64件、proof 256件",
                commandReference.Replace("\n", " ", StringComparison.Ordinal),
                StringComparison.OrdinalIgnoreCase);
            Assert.Contains(language == "en" ? "Separately, absent or explicit empty G863 source declarations" : "別途、G863 source declaration が absent/empty", commandReference, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(language == "en" ? "gates a later step" : "gate にもなりません", orchestration, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(language == "en" ? "does not gate" : "gate にもならない", ledger, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Theory]
    [InlineData("json")]
    [InlineData("markdown")]
    public void ActualCommandSeparatelyReportsUncoveredAcAndHeavyLaneWithoutLocalProof(string format)
    {
        using var workspace = new TemporaryWorkspace();
        workspace.WritePacket(Encoding.UTF8.GetBytes(HeavyLaneIncompletePacket));

        var (exitCode, output) = Run(workspace.Root, format);

        Assert.Equal(1, exitCode);
        using var result = JsonDocument.Parse(StructuredJson(output, format));
        var root = result.RootElement;
        Assert.Equal("incomplete", root.GetProperty("state").GetString());
        Assert.Equal("verification-contract-incomplete", root.GetProperty("cause").GetString());
        Assert.Equal(new[] { "AC2" }, root.GetProperty("missing_acceptance_criteria").EnumerateArray().Select(item => item.GetString()).ToArray());
        Assert.Empty(root.GetProperty("missing_lanes").EnumerateArray());
        Assert.Equal(new[] { "heavy" }, root.GetProperty("missing_local_required_lanes").EnumerateArray().Select(item => item.GetString()).ToArray());
        Assert.Equal("not-observed", root.GetProperty("execution").GetString());
        Assert.Equal("hosted-required", root.GetProperty("proofs")[0].GetProperty("evidence").GetString());
        Assert.DoesNotContain(root.GetProperty("diagnostics").EnumerateArray(), item => item.GetProperty("cause").GetString() == "missing-lane");
        Assert.All(root.GetProperty("diagnostics").EnumerateArray(), item => Assert.Equal(PacketRelativePath, item.GetProperty("path").GetString()));
        Assert.False(Directory.Exists(Path.Combine(workspace.Root, ".intent-cli", "rulings")));
    }

    [Theory]
    [InlineData("json")]
    [InlineData("markdown")]
    public void ActualCommandSortsReversedRegistryAndProofInputsAndKeepsNamespacesIndependent(string format)
    {
        using var workspace = new TemporaryWorkspace();
        var firstBytes = Encoding.UTF8.GetBytes(CompletePacket);
        workspace.WritePacket(firstBytes);
        var (firstExit, firstOutput) = Run(workspace.Root, format);
        Assert.Equal(0, firstExit);
        using var first = JsonDocument.Parse(StructuredJson(firstOutput, format));

        var reversed = CompletePacket
            .Replace("    - {id: AC1, criterion_index: 1}\n    - {id: AC2, criterion_index: 2}",
                "    - {id: AC2, criterion_index: 2}\n    - {id: AC1, criterion_index: 1}", StringComparison.Ordinal)
            .Replace("    - id: P1\n      acceptance_criteria: [AC1]\n      lane: mutation\n      command: npm run test:mutation\n      evidence: local-required\n    - id: P2\n      acceptance_criteria: [AC2]\n      lane: unit\n      command: dotnet test\n      evidence: hosted-required",
                "    - id: P2\n      acceptance_criteria: [AC2]\n      lane: unit\n      command: dotnet test\n      evidence: hosted-required\n    - id: P1\n      acceptance_criteria: [AC1]\n      lane: mutation\n      command: npm run test:mutation\n      evidence: local-required", StringComparison.Ordinal);
        Assert.NotEqual(CompletePacket, reversed);
        var reversedBytes = Encoding.UTF8.GetBytes(reversed);
        workspace.WritePacket(reversedBytes);
        var (reversedExit, reversedOutput) = Run(workspace.Root, format);
        Assert.Equal(0, reversedExit);
        using var second = JsonDocument.Parse(StructuredJson(reversedOutput, format));
        Assert.Equal(Convert.ToHexString(SHA256.HashData(reversedBytes)).ToLowerInvariant(), second.RootElement.GetProperty("packet_sha256").GetString());
        Assert.NotEqual(first.RootElement.GetProperty("packet_sha256").GetString(), second.RootElement.GetProperty("packet_sha256").GetString());
        foreach (var property in new[] { "acceptance_criteria", "lanes", "proofs", "missing_acceptance_criteria", "missing_lanes", "missing_local_required_lanes" })
            Assert.Equal(first.RootElement.GetProperty(property).GetRawText(), second.RootElement.GetProperty(property).GetRawText());

        var sharedReferences = CompletePacket.Replace(
            "  proofs:\n    - id: P1",
            "  proofs:\n    - id: P0\n      acceptance_criteria: [AC2, AC1]\n      lane: mutation\n      command: shared verification\n      evidence: local-required\n    - id: P1",
            StringComparison.Ordinal);
        Assert.NotEqual(CompletePacket, sharedReferences);
        workspace.WritePacket(Encoding.UTF8.GetBytes(sharedReferences));
        var (sharedReferenceExit, sharedReferenceOutput) = Run(workspace.Root, format);
        Assert.Equal(0, sharedReferenceExit);
        using var sharedReferenceResult = JsonDocument.Parse(StructuredJson(sharedReferenceOutput, format));
        var sharedProof = sharedReferenceResult.RootElement.GetProperty("proofs")[0];
        Assert.Equal("P0", sharedProof.GetProperty("id").GetString());
        Assert.Equal(new[] { "AC1", "AC2" }, Strings(sharedProof.GetProperty("acceptance_criteria")));
        var reversedReferences = sharedReferences.Replace("[AC2, AC1]", "[AC1, AC2]", StringComparison.Ordinal);
        Assert.NotEqual(sharedReferences, reversedReferences);
        workspace.WritePacket(Encoding.UTF8.GetBytes(reversedReferences));
        var (reversedReferenceExit, reversedReferenceOutput) = Run(workspace.Root, format);
        Assert.Equal(0, reversedReferenceExit);
        using var reversedReferenceResult = JsonDocument.Parse(StructuredJson(reversedReferenceOutput, format));
        foreach (var property in new[] { "acceptance_criteria", "lanes", "proofs", "missing_acceptance_criteria", "missing_lanes", "missing_local_required_lanes" })
            Assert.Equal(sharedReferenceResult.RootElement.GetProperty(property).GetRawText(), reversedReferenceResult.RootElement.GetProperty(property).GetRawText());

        const string shared = """
            {"implementation_issue_packet":{"source_execution_unit":"G865","domain":"legacy-domain","team":"legacy-team","target_repo":"legacy/repo","acceptance_criteria":["same"]},"verification_contract":{"version":"1","acceptance_criteria":[{"id":"Shared","criterion_index":"1"}],"lanes":[{"id":"Shared","weight":"ordinary"}],"affected_lanes":["Shared"],"proofs":[{"id":"Shared","acceptance_criteria":["Shared"],"lane":"Shared","command":"echo Shared","evidence":"hosted-required"}]}}
            """;
        workspace.WritePacket(Encoding.UTF8.GetBytes(shared));
        var (sharedExit, sharedOutput) = Run(workspace.Root, format);
        Assert.Equal(0, sharedExit);
        using var sharedResult = JsonDocument.Parse(StructuredJson(sharedOutput, format));
        Assert.Equal("complete", sharedResult.RootElement.GetProperty("state").GetString());
        Assert.Equal("Shared", sharedResult.RootElement.GetProperty("acceptance_criteria")[0].GetProperty("id").GetString());
        Assert.Equal("Shared", sharedResult.RootElement.GetProperty("lanes")[0].GetProperty("id").GetString());
        Assert.Equal("Shared", sharedResult.RootElement.GetProperty("proofs")[0].GetProperty("id").GetString());

        var reversedBindings = CompletePacket
            .Replace("{id: AC1, criterion_index: 1}", "{id: AC1, criterion_index: 2}", StringComparison.Ordinal)
            .Replace("{id: AC2, criterion_index: 2}", "{id: AC2, criterion_index: 1}", StringComparison.Ordinal);
        workspace.WritePacket(Encoding.UTF8.GetBytes(reversedBindings));
        var (bindingExit, bindingOutput) = Run(workspace.Root, format);
        Assert.Equal(0, bindingExit);
        using var bindingResult = JsonDocument.Parse(StructuredJson(bindingOutput, format));
        var boundCriteria = bindingResult.RootElement.GetProperty("acceptance_criteria");
        Assert.Equal("Preserve legacy output", boundCriteria[0].GetProperty("text").GetString());
        Assert.Equal("Preserve marker identity", boundCriteria[1].GetProperty("text").GetString());
    }

    [Theory]
    [InlineData("json")]
    [InlineData("markdown")]
    public void EmptyProofArrayIsValidAndReportsEveryCoverageGap(string format)
    {
        using var workspace = new TemporaryWorkspace();
        var bytes = Encoding.UTF8.GetBytes(WithEmptyProofArray());
        workspace.WritePacket(bytes);
        var (exitCode, output) = Run(workspace.Root, format);

        Assert.Equal(1, exitCode);
        using var document = JsonDocument.Parse(StructuredJson(output, format));
        var root = document.RootElement;
        Assert.Equal("incomplete", root.GetProperty("state").GetString());
        Assert.Empty(root.GetProperty("proofs").EnumerateArray());
        Assert.Equal(new[] { "AC1", "AC2" }, root.GetProperty("missing_acceptance_criteria").EnumerateArray().Select(item => item.GetString()).ToArray());
        Assert.Equal(new[] { "mutation", "unit" }, root.GetProperty("missing_lanes").EnumerateArray().Select(item => item.GetString()).ToArray());
        Assert.Equal(new[] { "mutation" }, root.GetProperty("missing_local_required_lanes").EnumerateArray().Select(item => item.GetString()).ToArray());
        Assert.All(root.GetProperty("diagnostics").EnumerateArray(), item => Assert.Equal(PacketRelativePath, item.GetProperty("path").GetString()));
    }

    [Theory]
    [InlineData("json")]
    [InlineData("markdown")]
    public void ActualCommandSeparatesEachCoverageGapAndHeavyLocalRequirement(string format)
    {
        var rows = new (string Name, byte[] Bytes, string[] MissingAcs, string[] MissingLanes, string[] MissingLocal)[]
        {
            ("only-uncovered-ac", BuildPacketBytes(acceptanceCount: 2, laneCount: 1, proofCount: 1), ["A002cccc"], [], []),
            ("only-uncovered-lane", BuildPacketBytes(acceptanceCount: 1, laneCount: 2, proofCount: 1), [], ["L002cccc"], []),
            ("only-heavy-hosted-without-local", BuildPacketBytes(acceptanceCount: 1, laneCount: 1, proofCount: 1, firstEvidence: "hosted-required"), [], [], ["L001bbbb"]),
        };
        foreach (var row in rows)
        {
            using var workspace = new TemporaryWorkspace();
            workspace.WritePacket(row.Bytes);
            var (exit, output) = Run(workspace.Root, format);
            Assert.Equal(1, exit);
            using var document = JsonDocument.Parse(StructuredJson(output, format));
            var result = document.RootElement;
            Assert.Equal("incomplete", result.GetProperty("state").GetString());
            Assert.Equal(row.MissingAcs, Strings(result.GetProperty("missing_acceptance_criteria")));
            Assert.Equal(row.MissingLanes, Strings(result.GetProperty("missing_lanes")));
            Assert.Equal(row.MissingLocal, Strings(result.GetProperty("missing_local_required_lanes")));
            Assert.All(result.GetProperty("diagnostics").EnumerateArray(), item => Assert.Equal(PacketRelativePath, item.GetProperty("path").GetString()));
        }

        using (var workspace = new TemporaryWorkspace())
        {
            var bytes = BuildPacketBytes(acceptanceCount: 1, laneCount: 1, proofCount: 2);
            workspace.WritePacket(bytes);
            var (exit, output) = Run(workspace.Root, format);
            Assert.Equal(0, exit);
            using var document = JsonDocument.Parse(StructuredJson(output, format));
            var proofs = document.RootElement.GetProperty("proofs");
            Assert.Equal("local-required", proofs[0].GetProperty("evidence").GetString());
            Assert.Equal("hosted-required", proofs[1].GetProperty("evidence").GetString());
            Assert.Equal("complete", document.RootElement.GetProperty("state").GetString());
        }
    }

    [Fact]
    public void ActualCommandRejectsSingleSchemaMutationsWithNoPartialArrays()
    {
        var cases = InvalidSchemaCases();
        Assert.NotEmpty(cases);
        foreach (var row in cases)
        foreach (var format in new[] { "json", "markdown" })
        {
            Assert.True(!StringComparer.Ordinal.Equals(CompletePacket, row.Packet), $"{row.Name} did not mutate its valid baseline.");
            using var workspace = new TemporaryWorkspace();
            var bytes = Encoding.UTF8.GetBytes(row.Packet);
            workspace.WritePacket(bytes);
            var before = workspace.SnapshotTree();

            var (exitCode, output) = Run(workspace.Root, format);

            Assert.True(exitCode == 1, $"{row.Name}/{format} should refuse.\n{output}");
            using var document = JsonDocument.Parse(StructuredJson(output, format));
            var result = document.RootElement;
            Assert.True(result.GetProperty("state").GetString() == "invalid", $"{row.Name}/{format} expected invalid.\n{output}");
            Assert.Equal("verification-contract-invalid", result.GetProperty("cause").GetString());
            Assert.Contains("execution, publication, and authority are not observed or verified", result.GetProperty("detail").GetString(), StringComparison.Ordinal);
            Assert.Equal(row.DiagnosticCause, result.GetProperty("diagnostics")[0].GetProperty("cause").GetString());
            if (row.DetailContains is not null)
                Assert.Contains(row.DetailContains, result.GetProperty("diagnostics")[0].GetProperty("detail").GetString(), StringComparison.Ordinal);
            Assert.Equal(PacketRelativePath, result.GetProperty("packet_path").GetString());
            Assert.Equal(Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), result.GetProperty("packet_sha256").GetString());
            Assert.Empty(result.GetProperty("acceptance_criteria").EnumerateArray());
            Assert.Empty(result.GetProperty("lanes").EnumerateArray());
            Assert.Empty(result.GetProperty("proofs").EnumerateArray());
            Assert.Empty(result.GetProperty("missing_acceptance_criteria").EnumerateArray());
            Assert.Empty(result.GetProperty("missing_lanes").EnumerateArray());
            Assert.Empty(result.GetProperty("missing_local_required_lanes").EnumerateArray());
            Assert.Equal("not-observed", result.GetProperty("execution").GetString());
            Assert.Equal(before, workspace.SnapshotTree());
        }
    }

    [Fact]
    public void ActualCommandDistinguishesAbsentContractAndTextualNullSpellings()
    {
        using var workspace = new TemporaryWorkspace();
        var absent = Encoding.UTF8.GetBytes("implementation_issue_packet:\n  source_execution_unit: G865\n  acceptance_criteria: [legacy]\n");
        workspace.WritePacket(absent);
        var (absentExit, absentOutput) = Run(workspace.Root, "json");
        Assert.Equal(0, absentExit);
        using (var absentDocument = JsonDocument.Parse(absentOutput))
        {
            var result = absentDocument.RootElement;
            Assert.Equal("not-declared", result.GetProperty("state").GetString());
            Assert.Equal("verification-contract-not-declared", result.GetProperty("cause").GetString());
            Assert.Equal(Convert.ToHexString(SHA256.HashData(absent)).ToLowerInvariant(), result.GetProperty("packet_sha256").GetString());
            Assert.Empty(result.GetProperty("diagnostics").EnumerateArray());
        }

        foreach (var (name, value, expectedState) in new[]
        {
            ("plain-null", "null", "invalid"),
            ("plain-Null", "Null", "invalid"),
            ("plain-NULL", "NULL", "invalid"),
            ("plain-mixed-case", "nUlL", "complete"),
            ("quoted-null", "\"null\"", "complete"),
            ("quoted-tilde", "\"~\"", "complete"),
        })
        {
            var yaml = CompletePacket.Replace("command: dotnet test", "command: " + value, StringComparison.Ordinal);
            workspace.WritePacket(Encoding.UTF8.GetBytes(yaml));
            var (exit, output) = Run(workspace.Root, "json");
            var expectedExit = expectedState == "complete" ? 0 : 1;
            Assert.Equal(expectedExit, exit);
            using var document = JsonDocument.Parse(output);
            Assert.Equal(expectedState, document.RootElement.GetProperty("state").GetString());
            Assert.Equal("not-observed", document.RootElement.GetProperty("execution").GetString());
        }
    }

    [Theory]
    [InlineData("json")]
    [InlineData("markdown")]
    public void ActualCommandAcceptsJsonFlowBomAndUnrelatedYamlMetadata(string format)
    {
        using var workspace = new TemporaryWorkspace();
        var packets = new[]
        {
            JsonPacket,
            FlowPacket,
            CompletePacket.Replace("Preserve marker identity", "Preserve � identity", StringComparison.Ordinal)
                .Replace("command: dotnet test", "command: \"printf '` ` `\\ncomplete'\"", StringComparison.Ordinal),
            "\uFEFF" + CompletePacket,
            "metadata: &unrelated {note: !custom safe}\nextra: *unrelated\n" + CompletePacket,
            CompletePacket.Replace("criterion_index: 1", "criterion_index: '1'", StringComparison.Ordinal)
                .Replace("criterion_index: 2", "criterion_index: \"2\"", StringComparison.Ordinal)
                .Replace("Preserve legacy output", "Preserve marker identity", StringComparison.Ordinal),
        };
        foreach (var packet in packets)
        {
            var bytes = Encoding.UTF8.GetBytes(packet);
            workspace.WritePacket(bytes);
            var (exitCode, output) = Run(workspace.Root, format);
            Assert.True(exitCode == 0, $"Expected complete for {format}.\n{output}");
            using var result = JsonDocument.Parse(StructuredJson(output, format));
            Assert.Equal("complete", result.RootElement.GetProperty("state").GetString());
            Assert.Equal(Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), result.RootElement.GetProperty("packet_sha256").GetString());
            Assert.Equal("not-observed", result.RootElement.GetProperty("execution").GetString());
            if (packet == JsonPacket || packet == FlowPacket)
            {
                var criteria = result.RootElement.GetProperty("acceptance_criteria");
                Assert.Equal(new[] { "AC1", "AC2" }, criteria.EnumerateArray().Select(item => item.GetProperty("id").GetString()).ToArray());
                Assert.Equal(new[] { 1, 2 }, criteria.EnumerateArray().Select(item => item.GetProperty("criterion_index").GetInt32()).ToArray());
                var repeatedText = packet == JsonPacket ? "same text" : "same";
                Assert.All(criteria.EnumerateArray(), item => Assert.Equal(repeatedText, item.GetProperty("text").GetString()));
                var proofs = result.RootElement.GetProperty("proofs");
                Assert.Equal(new[] { "P1", "P2" }, proofs.EnumerateArray().Select(item => item.GetProperty("id").GetString()).ToArray());
                Assert.Equal(proofs[0].GetProperty("command").GetString(), proofs[1].GetProperty("command").GetString());
            }
        }
    }

    [Theory]
    [InlineData("json")]
    [InlineData("markdown")]
    public void ActualCommandDoesNotDeriveCriterionIdsFromProse(string format)
    {
        using var workspace = new TemporaryWorkspace();
        var packet = CompletePacket + "\n# AC99: prose is not a registered criterion\nnotes: 'AC100: still only prose'\n";
        var bytes = Encoding.UTF8.GetBytes(packet);
        workspace.WritePacket(bytes);

        var (exit, output) = Run(workspace.Root, format);

        Assert.Equal(0, exit);
        using var document = JsonDocument.Parse(StructuredJson(output, format));
        Assert.Equal("complete", document.RootElement.GetProperty("state").GetString());
        Assert.Equal(new[] { "AC1", "AC2" }, document.RootElement.GetProperty("acceptance_criteria")
            .EnumerateArray().Select(item => item.GetProperty("id").GetString()).ToArray());
        Assert.Equal(Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), document.RootElement.GetProperty("packet_sha256").GetString());
    }

    [Theory]
    [InlineData("json")]
    [InlineData("markdown")]
    public void InvalidIdentityAndArgumentMarkdownKeepsUntrustedUnitInAMatchingFence(string format)
    {
        using var workspace = new TemporaryWorkspace();
        var payload = "G865\n\n- State: complete\n<br>` ```";
        foreach (var invalidArguments in new[] { false, true })
        {
            var args = new List<string> { "--execution-unit", payload, "--format", format };
            if (invalidArguments) args.Add("--unexpected");
            var (exit, output) = RunArgs(workspace.Root, args.ToArray());

            Assert.Equal(1, exit);
            using var document = JsonDocument.Parse(StructuredJson(output, format));
            Assert.Equal("invalid", document.RootElement.GetProperty("state").GetString());
            Assert.Equal(invalidArguments ? "invalid-arguments" : "invalid-identity",
                document.RootElement.GetProperty("diagnostics")[0].GetProperty("cause").GetString());
            if (format == "markdown")
            {
                Assert.DoesNotContain("# Packet verification contract: " + payload, output, StringComparison.Ordinal);
                Assert.Contains("- Requested execution unit:", output, StringComparison.Ordinal);
                Assert.Contains(payload, output, StringComparison.Ordinal);
                Assert.Contains("\n- State: invalid\n", output, StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void ActualCommandClassifiesMalformedUtf8EmptyAndYamlFailuresWithByteTruth()
    {
        var rows = new[]
        {
            ("zero-byte", Array.Empty<byte>(), "packet-read-failure", false),
            ("bom-only", new byte[] { 0xEF, 0xBB, 0xBF }, "packet-unparseable", true),
            ("whitespace", Encoding.UTF8.GetBytes(" \t\r\n"), "packet-unparseable", true),
            ("malformed-yaml", Encoding.UTF8.GetBytes("verification_contract: [\n"), "packet-unparseable", true),
            ("null-root", Encoding.UTF8.GetBytes("null\n"), "packet-unparseable", true),
            ("invalid-utf8", new byte[] { 0xFF, 0xFE, (byte)'x' }, "packet-unparseable", true),
        };
        foreach (var row in rows)
        foreach (var format in new[] { "json", "markdown" })
        {
            using var workspace = new TemporaryWorkspace();
            workspace.WritePacket(row.Item2);
            var (exit, output) = Run(workspace.Root, format);
            Assert.Equal(1, exit);
            using var document = JsonDocument.Parse(StructuredJson(output, format));
            var result = document.RootElement;
            Assert.Equal("unavailable", result.GetProperty("state").GetString());
            Assert.Equal(row.Item3, result.GetProperty("diagnostics")[0].GetProperty("cause").GetString());
            Assert.Equal(PacketRelativePath, result.GetProperty("packet_path").GetString());
            Assert.Equal(row.Item4
                    ? Convert.ToHexString(SHA256.HashData(row.Item2)).ToLowerInvariant()
                    : null,
                result.GetProperty("packet_sha256").ValueKind == JsonValueKind.Null
                    ? null
                    : result.GetProperty("packet_sha256").GetString());
            Assert.Empty(result.GetProperty("acceptance_criteria").EnumerateArray());
            Assert.Empty(result.GetProperty("lanes").EnumerateArray());
            Assert.Empty(result.GetProperty("proofs").EnumerateArray());
            Assert.Contains("execution, publication, and authority are not observed or verified", result.GetProperty("detail").GetString(), StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ActualCommandRejectsEveryMultipleDocumentPlacementWithReadByteHash()
    {
        var rows = new[]
        {
            CompletePacket + "\n---\nunrelated: second\n",
            "unrelated: first\n---\n" + CompletePacket,
            "unrelated: first\n---\nunrelated: second\n",
            CompletePacket.Replace("  version: 1\n", "  version: 1\n  version: 1\n", StringComparison.Ordinal) + "\n---\nunrelated: second\n",
        };
        foreach (var yaml in rows)
        foreach (var format in new[] { "json", "markdown" })
        {
            using var workspace = new TemporaryWorkspace();
            var bytes = Encoding.UTF8.GetBytes(yaml);
            workspace.WritePacket(bytes);
            var (exit, output) = Run(workspace.Root, format);
            Assert.Equal(1, exit);
            using var document = JsonDocument.Parse(StructuredJson(output, format));
            var result = document.RootElement;
            Assert.Equal("unavailable", result.GetProperty("state").GetString());
            Assert.Equal("packet-unparseable", result.GetProperty("diagnostics")[0].GetProperty("cause").GetString());
            Assert.Equal(Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), result.GetProperty("packet_sha256").GetString());
            Assert.Empty(result.GetProperty("acceptance_criteria").EnumerateArray());
            Assert.Empty(result.GetProperty("proofs").EnumerateArray());
        }
    }

    [Theory]
    [InlineData("json")]
    [InlineData("markdown")]
    public void ActualCommandReportsParseFailureBeforeDuplicateKeySchema(string format)
    {
        using var workspace = new TemporaryWorkspace();
        var packet = CompletePacket.Replace("  version: 1\n", "  version: 1\n  version: 1\n", StringComparison.Ordinal) + "\nbroken: [\n";
        var bytes = Encoding.UTF8.GetBytes(packet);
        workspace.WritePacket(bytes);

        var (exit, output) = Run(workspace.Root, format);

        Assert.Equal(1, exit);
        using var document = JsonDocument.Parse(StructuredJson(output, format));
        AssertUnavailable(document.RootElement, "packet-unparseable", PacketRelativePath,
            Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
    }

    [Fact]
    public void ActualCommandRejectsParticipatingTagsAliasesAndDuplicateYamlKeys()
    {
        var cases = new[]
        {
            ("tagged-contract-key", CompletePacket.Replace("verification_contract:", "!!str verification_contract:", StringComparison.Ordinal), "invalid-schema"),
            ("tagged-contract-version", CompletePacket.Replace("version: 1", "version: !!str 1", StringComparison.Ordinal), "invalid-schema"),
            ("tagged-actual-criterion", CompletePacket.Replace("- Preserve marker identity", "- !!str Preserve marker identity", StringComparison.Ordinal), "invalid-schema"),
            ("tagged-proof-command", CompletePacket.Replace("command: dotnet test", "command: !!str dotnet test", StringComparison.Ordinal), "invalid-schema"),
            ("explicit-nonspecific-version-tag", CompletePacket.Replace("version: 1", "version: ! 1", StringComparison.Ordinal), "invalid-schema"),
            ("explicit-nonspecific-proof-value-tag", CompletePacket.Replace("command: dotnet test", "command: ! dotnet test", StringComparison.Ordinal), "invalid-schema"),
            ("explicit-nonspecific-unit-tag", CompletePacket.Replace("source_execution_unit: G865", "source_execution_unit: ! G865", StringComparison.Ordinal), "invalid-identity"),
            ("explicit-nonspecific-criteria-sequence-tag", CompletePacket.Replace("  acceptance_criteria:\n    - Preserve marker identity", "  acceptance_criteria: !\n    - Preserve marker identity", StringComparison.Ordinal), "invalid-schema"),
            ("alias-contract-scalar", "version_alias: &version 1\n" + CompletePacket.Replace("version: 1", "version: *version", StringComparison.Ordinal), "invalid-schema"),
            ("alias-registry-id", "criterion_alias: &criterion AC1\n" + CompletePacket.Replace("{id: AC1, criterion_index: 1}", "{id: *criterion, criterion_index: 1}", StringComparison.Ordinal), "invalid-schema"),
            ("alias-lane-id", "lane_alias: &lane mutation\n" + CompletePacket.Replace("id: mutation, weight: heavy", "id: *lane, weight: heavy", StringComparison.Ordinal), "invalid-schema"),
            ("alias-affected-lane", "lane_alias: &lane mutation\n" + CompletePacket.Replace("affected_lanes: [mutation, unit]", "affected_lanes: [*lane, unit]", StringComparison.Ordinal), "invalid-schema"),
            ("alias-proof-criterion-reference", "criterion_alias: &criterion AC1\n" + CompletePacket.Replace("acceptance_criteria: [AC1]", "acceptance_criteria: [*criterion]", StringComparison.Ordinal), "invalid-schema"),
            ("aliased-unit", "unit_alias: &source_unit G865\n" + CompletePacket.Replace("source_execution_unit: G865", "source_execution_unit: *source_unit", StringComparison.Ordinal), "invalid-identity"),
            ("aliased-actual-criterion", CompletePacket.Replace("- Preserve marker identity", "- &actual_text Preserve marker identity", StringComparison.Ordinal)
                .Replace("- Preserve legacy output", "- *actual_text", StringComparison.Ordinal), "invalid-schema"),
            ("aliased-proof-command", "command_alias: &test_command dotnet test\n" + CompletePacket.Replace("command: dotnet test", "command: *test_command", StringComparison.Ordinal), "invalid-schema"),
            ("duplicate-root-contract-key", CompletePacket + "\nverification_contract: {}\n", "invalid-schema"),
            ("duplicate-root-identity-key", CompletePacket + "\nimplementation_issue_packet: {}\n", "invalid-schema"),
            ("duplicate-source-unit-key", CompletePacket.Replace("  source_execution_unit: G865\n", "  source_execution_unit: G865\n  source_execution_unit: G865\n", StringComparison.Ordinal), "invalid-schema"),
            ("duplicate-actual-criteria-key", CompletePacket.Replace("  acceptance_criteria:\n    - Preserve marker identity\n    - Preserve legacy output\n", "  acceptance_criteria: [Preserve marker identity]\n  acceptance_criteria: [Preserve legacy output]\n", StringComparison.Ordinal), "invalid-schema"),
            ("duplicate-contract-version-key", CompletePacket.Replace("  version: 1\n", "  version: 1\n  version: 1\n", StringComparison.Ordinal), "invalid-schema"),
            ("duplicate-registry-id-key", CompletePacket.Replace("{id: AC1, criterion_index: 1}", "{id: AC1, id: AC1, criterion_index: 1}", StringComparison.Ordinal), "invalid-schema"),
            ("duplicate-lane-weight-key", CompletePacket.Replace("{id: mutation, weight: heavy}", "{id: mutation, weight: heavy, weight: heavy}", StringComparison.Ordinal), "invalid-schema"),
            ("duplicate-proof-lane-key", CompletePacket.Replace("      lane: mutation\n", "      lane: mutation\n      lane: mutation\n", StringComparison.Ordinal), "invalid-schema"),
        };
        foreach (var (name, yaml, expectedDiagnostic) in cases)
        foreach (var format in new[] { "json", "markdown" })
        {
            Assert.NotEqual(CompletePacket, yaml);
            using var workspace = new TemporaryWorkspace();
            var bytes = Encoding.UTF8.GetBytes(yaml);
            workspace.WritePacket(bytes);
            var (exit, output) = Run(workspace.Root, format);
            Assert.True(exit == 1, $"{name}/{format} should refuse.\n{output}");
            using var document = JsonDocument.Parse(StructuredJson(output, format));
            var result = document.RootElement;
            Assert.True(result.GetProperty("state").GetString() == "invalid", $"{name}/{format} expected invalid.\n{output}");
            Assert.Equal(expectedDiagnostic, result.GetProperty("diagnostics")[0].GetProperty("cause").GetString());
            Assert.Empty(result.GetProperty("acceptance_criteria").EnumerateArray());
            Assert.Empty(result.GetProperty("lanes").EnumerateArray());
            Assert.Empty(result.GetProperty("proofs").EnumerateArray());
        }
    }

    [Theory]
    [InlineData("json")]
    [InlineData("markdown")]
    public void ActualCommandAcceptsInclusiveSchemaAndResourceBoundaries(string format)
    {
        var textAtLimit = new string('x', 16_382) + "😀";
        Assert.Equal(16_384, textAtLimit.Length);
        var sizeBase = Encoding.UTF8.GetBytes(CompletePacket);
        var exactSize = sizeBase.Concat(Encoding.UTF8.GetBytes("\n#" + new string('x', PacketVerificationContract.MaximumBytes - sizeBase.Length - 2))).ToArray();
        Assert.Equal(PacketVerificationContract.MaximumBytes, exactSize.Length);

        var cases = new (string Name, string Unit, byte[] Bytes)[]
        {
            ("identifier-64", Unit, BuildPacketBytes(idLength: 64)),
            ("criterion-16384-utf16", Unit, BuildPacketBytes(actualCriterion: textAtLimit)),
            ("command-16384-utf16", Unit, BuildPacketBytes(command: textAtLimit)),
            ("acceptance-criteria-128", Unit, BuildPacketBytes(acceptanceCount: 128)),
            ("lanes-64", Unit, BuildPacketBytes(laneCount: 64)),
            ("proofs-256", Unit, BuildPacketBytes(proofCount: 256)),
            ("packet-bytes-1048576", Unit, exactSize),
            ("collection-depth-32", Unit, Encoding.UTF8.GetBytes(CompletePacket + DeepUnrelatedValue(32))),
        };

        foreach (var row in cases)
        {
            using var workspace = new TemporaryWorkspace();
            workspace.WritePacket(row.Bytes, row.Unit);
            var (exit, output) = Run(workspace.Root, format, row.Unit);
            Assert.True(exit == 0, $"{row.Name}/{format} should be accepted.\n{output}");
            using var document = JsonDocument.Parse(StructuredJson(output, format));
            var result = document.RootElement;
            Assert.Equal("complete", result.GetProperty("state").GetString());
            Assert.Equal(Convert.ToHexString(SHA256.HashData(row.Bytes)).ToLowerInvariant(), result.GetProperty("packet_sha256").GetString());
            Assert.Equal("not-observed", result.GetProperty("execution").GetString());
            Assert.Contains("execution, publication, and authority are not observed or verified", result.GetProperty("detail").GetString(), StringComparison.Ordinal);
        }

        var unitAtLimit = "G" + new string('8', 127);
        using (var workspace = new TemporaryWorkspace())
        {
            var bytes = Encoding.UTF8.GetBytes(CompletePacket.Replace(Unit, unitAtLimit, StringComparison.Ordinal));
            workspace.WritePacket(bytes, unitAtLimit);
            var (exit, output) = Run(workspace.Root, format, unitAtLimit);
            Assert.Equal(0, exit);
            using var document = JsonDocument.Parse(StructuredJson(output, format));
            Assert.Equal("complete", document.RootElement.GetProperty("state").GetString());
            Assert.Equal(unitAtLimit, document.RootElement.GetProperty("execution_unit").GetString());
        }
    }

    [Theory]
    [InlineData("json")]
    [InlineData("markdown")]
    public void ActualCommandRejectsOneBeyondSchemaAndResourceBoundaries(string format)
    {
        var textOverLimit = new string('x', 16_383) + "😀";
        Assert.Equal(16_385, textOverLimit.Length);
        var sizeBase = Encoding.UTF8.GetBytes(CompletePacket);
        var overSize = sizeBase.Concat(Encoding.UTF8.GetBytes("\n#" + new string('x', PacketVerificationContract.MaximumBytes - sizeBase.Length - 1))).ToArray();
        Assert.Equal(PacketVerificationContract.MaximumBytes + 1, overSize.Length);
        var cases = new (string Name, string Unit, byte[] Bytes, string State, string Diagnostic)[]
        {
            ("identifier-65", Unit, BuildPacketBytes(idLength: 65), "invalid", "invalid-schema"),
            ("criterion-16385-utf16", Unit, BuildPacketBytes(actualCriterion: textOverLimit), "invalid", "invalid-schema"),
            ("command-16385-utf16", Unit, BuildPacketBytes(command: textOverLimit), "invalid", "invalid-schema"),
            ("acceptance-criteria-129", Unit, BuildPacketBytes(acceptanceCount: 129), "invalid", "invalid-schema"),
            ("lanes-65", Unit, BuildPacketBytes(laneCount: 65), "invalid", "invalid-schema"),
            ("proofs-257", Unit, BuildPacketBytes(proofCount: 257), "invalid", "invalid-schema"),
            ("packet-bytes-1048577", Unit, overSize, "unavailable", "resource-limit"),
            ("collection-depth-33", Unit, Encoding.UTF8.GetBytes(CompletePacket + DeepUnrelatedValue(33)), "unavailable", "resource-limit"),
        };

        foreach (var row in cases)
        {
            using var workspace = new TemporaryWorkspace();
            workspace.WritePacket(row.Bytes, row.Unit);
            var before = workspace.SnapshotTree();
            var (exit, output) = Run(workspace.Root, format, row.Unit);
            Assert.True(exit == 1, $"{row.Name}/{format} should refuse.\n{output}");
            using var document = JsonDocument.Parse(StructuredJson(output, format));
            var result = document.RootElement;
            Assert.Equal(row.State, result.GetProperty("state").GetString());
            Assert.Equal(row.Diagnostic, result.GetProperty("diagnostics")[0].GetProperty("cause").GetString());
            Assert.Empty(result.GetProperty("acceptance_criteria").EnumerateArray());
            Assert.Empty(result.GetProperty("lanes").EnumerateArray());
            Assert.Empty(result.GetProperty("proofs").EnumerateArray());
            Assert.Empty(result.GetProperty("missing_acceptance_criteria").EnumerateArray());
            Assert.Empty(result.GetProperty("missing_lanes").EnumerateArray());
            Assert.Empty(result.GetProperty("missing_local_required_lanes").EnumerateArray());
            Assert.Equal(row.Diagnostic == "resource-limit" && row.Name == "packet-bytes-1048577"
                    ? null
                    : Convert.ToHexString(SHA256.HashData(row.Bytes)).ToLowerInvariant(),
                result.GetProperty("packet_sha256").ValueKind == JsonValueKind.Null ? null : result.GetProperty("packet_sha256").GetString());
            Assert.Equal(before, workspace.SnapshotTree());
            Assert.Contains("execution, publication, and authority are not observed or verified", result.GetProperty("detail").GetString(), StringComparison.Ordinal);
        }

        var overLengthUnit = "G" + new string('8', 128);
        using (var workspace = new TemporaryWorkspace())
        {
            var (exit, output) = Run(workspace.Root, format, overLengthUnit);
            Assert.Equal(1, exit);
            using var document = JsonDocument.Parse(StructuredJson(output, format));
            Assert.Equal("invalid", document.RootElement.GetProperty("state").GetString());
            Assert.Equal("invalid-identity", document.RootElement.GetProperty("diagnostics")[0].GetProperty("cause").GetString());
            Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("packet_path").ValueKind);
            Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("packet_sha256").ValueKind);
            Assert.DoesNotContain("issues/" + overLengthUnit, output, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ActualCommandRejectsMissingAndMismatchedIdentityWithNoPartialResult()
    {
        var cases = new[]
        {
            ("missing-unit", CompletePacket.Replace("  source_execution_unit: G865\n", string.Empty, StringComparison.Ordinal), "invalid-identity"),
            ("wrong-unit", CompletePacket.Replace("source_execution_unit: G865", "source_execution_unit: G866", StringComparison.Ordinal), "invalid-identity"),
            ("missing-actual-criteria", CompletePacket.Replace("  acceptance_criteria:\n    - Preserve marker identity\n    - Preserve legacy output\n", string.Empty, StringComparison.Ordinal), "invalid-schema"),
        };
        foreach (var (name, yaml, diagnosticCause) in cases)
        foreach (var format in new[] { "json", "markdown" })
        {
            using var workspace = new TemporaryWorkspace();
            workspace.WritePacket(Encoding.UTF8.GetBytes(yaml));
            var (exit, output) = Run(workspace.Root, format);
            Assert.True(exit == 1, $"{name}/{format} should refuse.\n{output}");
            using var document = JsonDocument.Parse(StructuredJson(output, format));
            var result = document.RootElement;
            Assert.Equal("invalid", result.GetProperty("state").GetString());
            Assert.Equal(diagnosticCause, result.GetProperty("diagnostics")[0].GetProperty("cause").GetString());
            Assert.Empty(result.GetProperty("acceptance_criteria").EnumerateArray());
            Assert.Empty(result.GetProperty("lanes").EnumerateArray());
            Assert.Empty(result.GetProperty("proofs").EnumerateArray());
        }
    }

    [Theory]
    [InlineData("json")]
    [InlineData("markdown")]
    public void ActualCommandValidatesArgumentOrderAndRefusesUnsupportedOptions(string format)
    {
        var validOrders = new[]
        {
            new[] { "--execution-unit", Unit, "--format", format },
            new[] { "--format", format, "--execution-unit", Unit },
        };
        foreach (var args in validOrders)
        {
            using var workspace = new TemporaryWorkspace();
            workspace.WritePacket(Encoding.UTF8.GetBytes(CompletePacket));
            var (exit, output) = RunArgs(workspace.Root, args);
            Assert.Equal(0, exit);
            Assert.Equal("complete", JsonDocument.Parse(StructuredJson(output, format)).RootElement.GetProperty("state").GetString());
        }

        var refusals = new (string Name, string[] Args, bool Structured)[]
        {
            ("write-flag-before-format", ["--write", "--format", format, "--execution-unit", Unit], true),
            ("unknown-before-unit-retains-first-supplied-unit", ["--unknown", "--execution-unit", "G866", "--format", format], true),
            ("write-flag-after-format", ["--format", format, "--write", "--execution-unit", Unit], true),
            ("dry-run-flag", ["--execution-unit", Unit, "--format", format, "--dry-run"], true),
            ("duplicate-unit", ["--execution-unit", Unit, "--format", format, "--execution-unit", Unit], true),
            ("duplicate-format", ["--execution-unit", Unit, "--format", format, "--format", format], true),
            ("missing-unit-value", ["--execution-unit", "--format", format], true),
            ("missing-format-value", ["--execution-unit", Unit, "--format"], false),
            ("missing-unit-flag", ["--format", format], true),
            ("missing-format-flag", ["--execution-unit", Unit], false),
        };
        foreach (var refusal in refusals)
        {
            using var workspace = new TemporaryWorkspace();
            var before = workspace.SnapshotTree();
            var (exit, output) = RunArgs(workspace.Root, refusal.Args);
            Assert.True(exit == 1, $"{refusal.Name}/{format} should refuse.\n{output}");
            if (refusal.Structured)
            {
                using var document = JsonDocument.Parse(StructuredJson(output, format));
                Assert.Equal("invalid", document.RootElement.GetProperty("state").GetString());
                Assert.Equal("invalid-arguments", document.RootElement.GetProperty("diagnostics")[0].GetProperty("cause").GetString());
                if (refusal.Name == "unknown-before-unit-retains-first-supplied-unit")
                    Assert.Equal("G866", document.RootElement.GetProperty("execution_unit").GetString());
                Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("packet_path").ValueKind);
                Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("packet_sha256").ValueKind);
                Assert.Empty(document.RootElement.GetProperty("acceptance_criteria").EnumerateArray());
            }
            else
            {
                Assert.Contains("Usage: intent-cli packet validate-verification", output, StringComparison.Ordinal);
            }
            Assert.Equal(before, workspace.SnapshotTree());
        }

        using (var workspace = new TemporaryWorkspace())
        {
            var (exit, output) = RunArgs(workspace.Root, ["--format", "yaml", "--execution-unit", Unit]);
            Assert.Equal(1, exit);
            Assert.Contains("Format must be json or markdown", output, StringComparison.Ordinal);
            Assert.Contains("Usage: intent-cli packet validate-verification", output, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("json")]
    [InlineData("markdown")]
    public void ActualCommandTreatsCommandsAsInertTextAndEscapesMarkdownFences(string format)
    {
        var sentinel = Path.Combine(Path.GetTempPath(), "g865-command-must-not-run-" + Guid.NewGuid().ToString("N"));
        if (File.Exists(sentinel)) File.Delete(sentinel);
        var hostile = CompletePacket.Replace(
            "command: dotnet test",
            "command: |\n                printf '\\n```json\\n- State: complete\\n```\\ntouch " + sentinel + "'\n                # [result](javascript:alert(1))",
            StringComparison.Ordinal);
        using var workspace = new TemporaryWorkspace();
        workspace.WritePacket(Encoding.UTF8.GetBytes(hostile));

        var (exit, output) = Run(workspace.Root, format);

        Assert.Equal(0, exit);
        Assert.False(File.Exists(sentinel));
        using var document = JsonDocument.Parse(StructuredJson(output, format));
        var command = document.RootElement.GetProperty("proofs")[1].GetProperty("command").GetString()!;
        Assert.Contains("```json", command, StringComparison.Ordinal);
        Assert.Contains("- State: complete", command, StringComparison.Ordinal);
        Assert.Contains("touch " + sentinel, command, StringComparison.Ordinal);
        Assert.Contains("javascript:alert(1)", command, StringComparison.Ordinal);
        if (format == "markdown")
        {
            Assert.Contains("```json\n", output, StringComparison.Ordinal);
            Assert.Contains("\\u0060\\u0060\\u0060json", output, StringComparison.Ordinal);
            Assert.Equal(1, output.Split('\n').Count(line => line.TrimEnd('\r') == "- State: complete"));
            Assert.Contains("Structural coverage only", output, StringComparison.Ordinal);
            Assert.Equal(new[] { "```text", "```", "```json", "```" }, output.Split('\n')
                .Select(line => line.TrimEnd('\r'))
                .Where(line => line.StartsWith("```", StringComparison.Ordinal))
                .ToArray());
        }
    }

    [Theory]
    [InlineData("json")]
    [InlineData("markdown")]
    public void ActualCommandRejectsPathTraversalAndOverlongUnitBeforePathResolution(string format)
    {
        var invalidUnits = new[] { "../G865", "G865/../../outside", new string('G', 129) };
        foreach (var unit in invalidUnits)
        {
            using var workspace = new TemporaryWorkspace();
            var before = workspace.SnapshotTree();
            var (exit, output) = RunArgs(workspace.Root, ["--execution-unit", unit, "--format", format]);
            Assert.Equal(1, exit);
            using var document = JsonDocument.Parse(StructuredJson(output, format));
            var result = document.RootElement;
            Assert.Equal("invalid", result.GetProperty("state").GetString());
            Assert.Equal("invalid-identity", result.GetProperty("diagnostics")[0].GetProperty("cause").GetString());
            Assert.Equal(JsonValueKind.Null, result.GetProperty("packet_path").ValueKind);
            Assert.Equal(JsonValueKind.Null, result.GetProperty("packet_sha256").ValueKind);
            Assert.Equal(unit, result.GetProperty("execution_unit").GetString());
            Assert.Equal(before, workspace.SnapshotTree());
        }
    }

    [Theory]
    [InlineData("json")]
    [InlineData("markdown")]
    public void ActualCommandRejectsUnsafeOrMissingFilesystemMembersWithoutChangingThem(string format)
    {
        var cases = new[] { "missing-root-segment", "intent-cli-file", "issues-file", "unit-file", "leaf-directory", "leaf-symlink" };
        foreach (var scenario in cases)
        {
            using var workspace = new TemporaryWorkspace();
            var packetPath = Path.Combine(workspace.Root, ".intent-cli", "issues", Unit, "packet.yaml");
            var external = Path.Combine(workspace.Root, "outside-target");
            var outsideBytes = Encoding.UTF8.GetBytes(CompletePacket);
            File.WriteAllBytes(external, outsideBytes);
            switch (scenario)
            {
                case "missing-root-segment":
                    break;
                case "intent-cli-file":
                    File.WriteAllText(Path.Combine(workspace.Root, ".intent-cli"), "sentinel");
                    break;
                case "issues-file":
                    Directory.CreateDirectory(Path.Combine(workspace.Root, ".intent-cli"));
                    File.WriteAllText(Path.Combine(workspace.Root, ".intent-cli", "issues"), "sentinel");
                    break;
                case "unit-file":
                    Directory.CreateDirectory(Path.Combine(workspace.Root, ".intent-cli", "issues"));
                    File.WriteAllText(Path.Combine(workspace.Root, ".intent-cli", "issues", Unit), "sentinel");
                    break;
                case "leaf-directory":
                    Directory.CreateDirectory(Path.GetDirectoryName(packetPath)!);
                    Directory.CreateDirectory(packetPath);
                    break;
                case "leaf-symlink":
                    Directory.CreateDirectory(Path.GetDirectoryName(packetPath)!);
                    CreateFileSymlinkOrSkip(packetPath, external);
                    break;
            }
            var before = workspace.SnapshotTree();
            var (exit, output) = Run(workspace.Root, format);
            Assert.Equal(1, exit);
            using var document = JsonDocument.Parse(StructuredJson(output, format));
            AssertUnavailable(document.RootElement, "packet-read-failure", PacketRelativePath, null);
            Assert.Equal(before, workspace.SnapshotTree());
            Assert.Equal(outsideBytes, File.ReadAllBytes(external));
        }
    }

    [Theory]
    [InlineData("json")]
    [InlineData("markdown")]
    public void ActualCommandRejectsRootAndBelowRootDirectorySymlinks(string format)
    {
        foreach (var segment in new[] { "root", ".intent-cli", "issues", Unit })
        {
            using var workspace = new TemporaryWorkspace();
            var outside = Path.Combine(workspace.Root, "outside");
            var outsidePacket = Path.Combine(outside, ".intent-cli", "issues", Unit, "packet.yaml");
            Directory.CreateDirectory(Path.GetDirectoryName(outsidePacket)!);
            var bytes = Encoding.UTF8.GetBytes(CompletePacket);
            File.WriteAllBytes(outsidePacket, bytes);
            var selectedRoot = Path.Combine(workspace.Root, "selected-root");
            Directory.CreateDirectory(selectedRoot);
            string commandRoot;
            if (segment == "root")
            {
                commandRoot = Path.Combine(workspace.Root, "root-link");
                CreateDirectorySymlinkOrSkip(commandRoot, selectedRoot);
            }
            else
            {
                commandRoot = selectedRoot;
                var linkPath = segment switch
                {
                    ".intent-cli" => Path.Combine(selectedRoot, ".intent-cli"),
                    "issues" => Path.Combine(selectedRoot, ".intent-cli", "issues"),
                    _ => Path.Combine(selectedRoot, ".intent-cli", "issues", Unit),
                };
                Directory.CreateDirectory(Path.GetDirectoryName(linkPath)!);
                var target = segment switch
                {
                    ".intent-cli" => outside,
                    "issues" => Path.Combine(outside, ".intent-cli", "issues"),
                    _ => Path.Combine(outside, ".intent-cli", "issues", Unit),
                };
                CreateDirectorySymlinkOrSkip(linkPath, target);
            }
            var before = workspace.SnapshotTree();
            var (exit, output) = Run(commandRoot, format);
            Assert.Equal(1, exit);
            using var document = JsonDocument.Parse(StructuredJson(output, format));
            AssertUnavailable(document.RootElement, "packet-read-failure", PacketRelativePath, null);
            Assert.Equal(before, workspace.SnapshotTree());
            Assert.Equal(bytes, File.ReadAllBytes(outsidePacket));
        }
    }

    [Fact]
    public void ActualCommandReportsProvenPermissionDenialWhenThePlatformEnforcesIt()
    {
        if (OperatingSystem.IsWindows()) throw Xunit.Sdk.SkipException.ForSkip("The reached permission fixture requires Unix file modes.");
        using var workspace = new TemporaryWorkspace();
        var bytes = Encoding.UTF8.GetBytes(CompletePacket);
        workspace.WritePacket(bytes);
        var path = Path.Combine(workspace.Root, ".intent-cli", "issues", Unit, "packet.yaml");
        var original = File.GetUnixFileMode(path);
        File.SetUnixFileMode(path, UnixFileMode.None);
        try
        {
            if (Record.Exception(() => File.ReadAllBytes(path)) is not UnauthorizedAccessException)
                throw Xunit.Sdk.SkipException.ForSkip("This test identity can still read a mode-000 packet.");
            foreach (var format in new[] { "json", "markdown" })
            {
                var (exit, output) = Run(workspace.Root, format);
                Assert.Equal(1, exit);
                using var document = JsonDocument.Parse(StructuredJson(output, format));
                AssertUnavailable(document.RootElement, "packet-read-failure", PacketRelativePath, null);
            }
        }
        finally
        {
            File.SetUnixFileMode(path, original);
        }
    }

    [Theory]
    [InlineData("json")]
    [InlineData("markdown")]
    public void ActualCommandRefusesSearchOnlyUnitDirectoryBeforeReadingPacket(string format)
    {
        if (OperatingSystem.IsWindows()) throw Xunit.Sdk.SkipException.ForSkip("The reached directory permission fixture requires Unix file modes.");
        using var workspace = new TemporaryWorkspace();
        var bytes = Encoding.UTF8.GetBytes(CompletePacket);
        workspace.WritePacket(bytes);
        var packetPath = Path.Combine(workspace.Root, ".intent-cli", "issues", Unit, "packet.yaml");
        var unitDirectory = Path.GetDirectoryName(packetPath)!;
        var original = File.GetUnixFileMode(unitDirectory);
        File.SetUnixFileMode(unitDirectory, UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
        try
        {
            if (!File.ReadAllBytes(packetPath).SequenceEqual(bytes))
                throw new InvalidOperationException("The packet leaf should remain readable when only directory read bits are removed.");
            var denied = Record.Exception(() =>
            {
                using var entries = Directory.EnumerateFileSystemEntries(unitDirectory).GetEnumerator();
                _ = entries.MoveNext();
            });
            if (denied is not UnauthorizedAccessException)
                throw Xunit.Sdk.SkipException.ForSkip("This test identity can still enumerate a search-only unit directory.");

            var (exit, output) = Run(workspace.Root, format);
            Assert.Equal(1, exit);
            using var document = JsonDocument.Parse(StructuredJson(output, format));
            AssertUnavailable(document.RootElement, "packet-read-failure", PacketRelativePath, null);
            Assert.Contains($".intent-cli/issues/{Unit}: read-error", document.RootElement.GetProperty("detail").GetString(), StringComparison.Ordinal);
        }
        finally
        {
            File.SetUnixFileMode(unitDirectory, original);
        }
    }

    [Theory]
    [InlineData("json")]
    [InlineData("markdown")]
    public void ActualCommandRejectsFifoLeafBeforeOpeningItWhereSupported(string format)
    {
        if (OperatingSystem.IsWindows()) throw Xunit.Sdk.SkipException.ForSkip("FIFO creation is Unix-specific.");
        using var workspace = new TemporaryWorkspace();
        workspace.WritePacket(Encoding.UTF8.GetBytes(CompletePacket));
        var packetPath = Path.Combine(workspace.Root, ".intent-cli", "issues", Unit, "packet.yaml");
        File.Delete(packetPath);
        int status;
        try
        {
            status = MkFifo(packetPath, 0x180); // 0600; no data is written to the FIFO.
        }
        catch (DllNotFoundException exception)
        {
            throw Xunit.Sdk.SkipException.ForSkip("The platform has no libc FIFO primitive: " + exception.Message);
        }
        catch (EntryPointNotFoundException exception)
        {
            throw Xunit.Sdk.SkipException.ForSkip("The platform has no mkfifo entry point: " + exception.Message);
        }
        if (status != 0)
            throw Xunit.Sdk.SkipException.ForSkip("mkfifo is unavailable, errno=" + Marshal.GetLastPInvokeError());

        var entriesBefore = Directory.EnumerateFileSystemEntries(Path.GetDirectoryName(packetPath)!).Order(StringComparer.Ordinal).ToArray();
        var (exit, output) = Run(workspace.Root, format);

        Assert.Equal(1, exit);
        using var document = JsonDocument.Parse(StructuredJson(output, format));
        AssertUnavailable(document.RootElement, "packet-read-failure", PacketRelativePath, null);
        Assert.Equal(entriesBefore, Directory.EnumerateFileSystemEntries(Path.GetDirectoryName(packetPath)!).Order(StringComparer.Ordinal).ToArray());
    }

    private sealed record InvalidSchemaCase(string Name, string Packet, string DiagnosticCause = "invalid-schema", string? DetailContains = null);

    private static IReadOnlyList<InvalidSchemaCase> InvalidSchemaCases()
    {
        var contractsStart = CompletePacket.IndexOf("verification_contract:", StringComparison.Ordinal);
        string Replace(string oldValue, string newValue) => CompletePacket.Replace(oldValue, newValue, StringComparison.Ordinal);
        string ReplaceContract(string newContract) => CompletePacket[..contractsStart] + newContract;
        return
        [
            new("contract-null", ReplaceContract("verification_contract: null\n")),
            new("contract-scalar", ReplaceContract("verification_contract: true\n")),
            new("contract-sequence", ReplaceContract("verification_contract: []\n")),
            new("contract-unknown-key", Replace("  version: 1\n", "  version: 1\n  extra: true\n")),
            new("contract-missing-version", Replace("  version: 1\n", string.Empty)),
            new("contract-missing-registry", Replace("  acceptance_criteria:\n    - {id: AC1, criterion_index: 1}\n    - {id: AC2, criterion_index: 2}\n", string.Empty)),
            new("contract-missing-lanes", Replace("  lanes:\n    - {id: mutation, weight: heavy}\n    - {id: unit, weight: ordinary}\n", string.Empty)),
            new("contract-missing-affected", Replace("  affected_lanes: [mutation, unit]\n", string.Empty)),
            new("contract-missing-proofs", MissingProofsField()),
            new("contract-version-bool", Replace("version: 1", "version: true")),
            new("contract-version-invalid", Replace("version: 1", "version: 1.0")),
            new("contract-version-explicit-bang", Replace("version: 1", "version: ! 1")),
            new("contract-map-explicit-bang", Replace("verification_contract:\n", "verification_contract: !\n")),
            new("contract-key-explicit-bang", Replace("verification_contract:", "! verification_contract:")),
            new("root-map-explicit-bang", "!\n" + CompletePacket),
            new("registry-item-null", Replace("- {id: AC1, criterion_index: 1}", "- null")),
            new("registry-item-scalar", Replace("- {id: AC1, criterion_index: 1}", "- AC1")),
            new("registry-item-sequence", Replace("- {id: AC1, criterion_index: 1}", "- [AC1, 1]")),
            new("registry-missing-id", Replace("{id: AC1, criterion_index: 1}", "{criterion_index: 1}")),
            new("registry-missing-index", Replace("{id: AC1, criterion_index: 1}", "{id: AC1}")),
            new("registry-unknown-key", Replace("{id: AC1, criterion_index: 1}", "{id: AC1, criterion_index: 1, extra: no}")),
            new("registry-item-explicit-bang", Replace("- {id: AC1, criterion_index: 1}", "- ! {id: AC1, criterion_index: 1}")),
            new("registry-id-explicit-bang", Replace("id: AC1", "id: ! AC1")),
            new("registry-id-null", Replace("id: AC1", "id: null")),
            new("registry-id-map", Replace("id: AC1", "id: {value: AC1}")),
            new("registry-id-sequence", Replace("id: AC1", "id: [AC1]")),
            new("registry-empty-id", Replace("id: AC1", "id: ''")),
            new("registry-whitespace-id", Replace("id: AC1", "id: '  '")),
            new("registry-case-alias-id", Replace("id: AC2, criterion_index: 2", "id: ac1, criterion_index: 2")
                .Replace("acceptance_criteria: [AC2]", "acceptance_criteria: [ac1]", StringComparison.Ordinal),
                DetailContains: "acceptance_criteria IDs must be unique canonical identifiers"),
            new("registry-index-zero", Replace("criterion_index: 1", "criterion_index: 0")),
            new("registry-index-negative", Replace("criterion_index: 1", "criterion_index: -1")),
            new("registry-index-fraction", Replace("criterion_index: 1", "criterion_index: 1.0")),
            new("registry-index-exponent", Replace("criterion_index: 1", "criterion_index: 1e0")),
            new("registry-index-null", Replace("criterion_index: 1", "criterion_index: null")),
            new("registry-index-duplicate", Replace("criterion_index: 2", "criterion_index: 1")),
            new("registry-index-out-of-range", Replace("criterion_index: 2", "criterion_index: 3")),
            new("registry-index-missing-actual-binding", Replace("    - {id: AC2, criterion_index: 2}\n", string.Empty)),
            new("actual-criteria-scalar", Replace("acceptance_criteria:\n    - Preserve marker identity\n    - Preserve legacy output", "acceptance_criteria: Preserve marker identity")),
            new("actual-criteria-map-item", Replace("- Preserve marker identity", "- {text: Preserve marker identity}")),
            new("actual-criteria-key-explicit-bang", Replace("  acceptance_criteria:\n    - Preserve marker identity", "  ! acceptance_criteria:\n    - Preserve marker identity")),
            new("actual-criteria-sequence-explicit-bang", Replace("  acceptance_criteria:\n    - Preserve marker identity", "  acceptance_criteria: !\n    - Preserve marker identity")),
            new("actual-criteria-item-explicit-bang", Replace("- Preserve marker identity", "- ! Preserve marker identity")),
            new("actual-criteria-empty", Replace("- Preserve marker identity", "- ''")),
            new("lane-map-null", Replace("- {id: mutation, weight: heavy}", "- null")),
            new("lane-item-scalar", Replace("- {id: mutation, weight: heavy}", "- mutation")),
            new("lane-item-sequence", Replace("- {id: mutation, weight: heavy}", "- [mutation, heavy]")),
            new("lane-unknown-key", Replace("{id: mutation, weight: heavy}", "{id: mutation, weight: heavy, extra: no}")),
            new("lane-missing-weight", Replace("{id: mutation, weight: heavy}", "{id: mutation}")),
            new("lane-missing-id", Replace("{id: mutation, weight: heavy}", "{weight: heavy}")),
            new("lane-empty-id", Replace("id: mutation, weight: heavy", "id: '', weight: heavy")),
            new("lane-whitespace-id", Replace("id: mutation, weight: heavy", "id: '  ', weight: heavy")),
            new("lane-id-null", Replace("id: mutation, weight: heavy", "id: null, weight: heavy")),
            new("lane-id-map", Replace("id: mutation, weight: heavy", "id: {value: mutation}, weight: heavy")),
            new("lane-id-sequence", Replace("id: mutation, weight: heavy", "id: [mutation], weight: heavy")),
            new("lane-duplicate-id-key", Replace("{id: mutation, weight: heavy}", "{id: mutation, id: mutation, weight: heavy}")),
            new("lane-case-alias-id", Replace("id: unit, weight: ordinary", "id: Mutation, weight: ordinary")
                .Replace("affected_lanes: [mutation, unit]", "affected_lanes: [mutation, Mutation]", StringComparison.Ordinal),
                DetailContains: "lane IDs must be unique canonical identifiers"),
            new("lane-invalid-weight", Replace("weight: heavy", "weight: heavyweight")),
            new("lane-weight-null", Replace("weight: heavy", "weight: null")),
            new("lane-weight-map", Replace("weight: heavy", "weight: {kind: heavy}")),
            new("lane-weight-sequence", Replace("weight: heavy", "weight: [heavy]")),
            new("lane-weight-explicit-bang", Replace("weight: heavy", "weight: ! heavy")),
            new("lane-entry-explicit-bang", Replace("- {id: mutation, weight: heavy}", "- ! {id: mutation, weight: heavy}")),
            new("lane-id-explicit-bang", Replace("id: mutation, weight: heavy", "id: ! mutation, weight: heavy")),
            new("affected-empty", Replace("affected_lanes: [mutation, unit]", "affected_lanes: []")),
            new("affected-null", Replace("affected_lanes: [mutation, unit]", "affected_lanes: null")),
            new("affected-map", Replace("affected_lanes: [mutation, unit]", "affected_lanes: {lane: mutation}")),
            new("affected-scalar", Replace("affected_lanes: [mutation, unit]", "affected_lanes: mutation")),
            new("affected-member-null", Replace("affected_lanes: [mutation, unit]", "affected_lanes: [null, unit]")),
            new("affected-member-map", Replace("affected_lanes: [mutation, unit]", "affected_lanes: [{lane: mutation}, unit]")),
            new("affected-member-sequence", Replace("affected_lanes: [mutation, unit]", "affected_lanes: [[mutation], unit]")),
            new("affected-duplicate", Replace("affected_lanes: [mutation, unit]", "affected_lanes: [mutation, mutation]")),
            new("affected-case-mismatch", Replace("affected_lanes: [mutation, unit]", "affected_lanes: [Mutation, unit]")
                .Replace("lane: mutation", "lane: unit", StringComparison.Ordinal)
                .Replace("evidence: local-required", "evidence: hosted-required", StringComparison.Ordinal)),
            new("affected-unknown", Replace("affected_lanes: [mutation, unit]", "affected_lanes: [missing]")),
            new("affected-duplicate-key", Replace("  affected_lanes: [mutation, unit]\n", "  affected_lanes: [mutation, unit]\n  affected_lanes: [mutation, unit]\n")),
            new("affected-explicit-bang", Replace("affected_lanes: [mutation, unit]", "affected_lanes: ! [mutation, unit]")),
            new("proof-item-null", Replace("    - id: P1\n      acceptance_criteria: [AC1]\n      lane: mutation\n      command: npm run test:mutation\n      evidence: local-required", "    - null")),
            new("proof-item-scalar", Replace("    - id: P1\n      acceptance_criteria: [AC1]\n      lane: mutation\n      command: npm run test:mutation\n      evidence: local-required", "    - P1")),
            new("proof-item-sequence", Replace("    - id: P1\n      acceptance_criteria: [AC1]\n      lane: mutation\n      command: npm run test:mutation\n      evidence: local-required", "    - [P1, AC1]")),
            new("proof-missing-id", Replace("    - id: P1\n      acceptance_criteria: [AC1]\n      lane: mutation\n      command: npm run test:mutation\n      evidence: local-required",
                "    - {acceptance_criteria: [AC1], lane: mutation, command: npm run test:mutation, evidence: local-required}")),
            new("proof-missing-acceptance", Replace("      acceptance_criteria: [AC1]\n", string.Empty)),
            new("proof-missing-lane", Replace("      lane: mutation\n", string.Empty)),
            new("proof-missing-evidence", Replace("      evidence: local-required\n", string.Empty)),
            new("proof-empty-id", Replace("id: P1", "id: ''")),
            new("proof-whitespace-id", Replace("id: P1", "id: '  '")),
            new("proof-unknown-key", Replace("      evidence: local-required", "      evidence: local-required\n      authority: approved")),
            new("proof-missing-command", Replace("      command: npm run test:mutation\n", string.Empty)),
            new("proof-case-alias-id", Replace("id: P2", "id: p1")),
            new("proof-duplicate-id-key", Replace("    - id: P1\n", "    - id: P1\n      id: P1\n")),
            new("proof-empty-command", Replace("command: dotnet test", "command: '  '")),
            new("proof-command-explicit-bang", Replace("command: dotnet test", "command: ! dotnet test")),
            new("proof-entry-explicit-bang", Replace("    - id: P1", "    - ! id: P1")),
            new("proof-reference-explicit-bang", Replace("acceptance_criteria: [AC1]", "acceptance_criteria: ! [AC1]")),
            new("proof-id-explicit-bang", Replace("id: P1", "id: ! P1")),
            new("proof-id-null", Replace("id: P1", "id: null")),
            new("proof-id-map", Replace("id: P1", "id: {value: P1}")),
            new("proof-id-sequence", Replace("id: P1", "id: [P1]")),
            new("proof-empty-evidence", Replace("evidence: hosted-required", "evidence: ' '")),
            new("proof-evidence-null", Replace("evidence: hosted-required", "evidence: null")),
            new("proof-evidence-map", Replace("evidence: hosted-required", "evidence: {kind: hosted-required}")),
            new("proof-evidence-sequence", Replace("evidence: hosted-required", "evidence: [hosted-required]")),
            new("proof-evidence-explicit-bang", Replace("evidence: hosted-required", "evidence: ! hosted-required")),
            new("proof-unknown-evidence", Replace("evidence: hosted-required", "evidence: passed")),
            new("proof-unknown-ac", Replace("acceptance_criteria: [AC2]", "acceptance_criteria: [AC9]")),
            new("proof-case-mismatched-ac", Replace("acceptance_criteria: [AC2]", "acceptance_criteria: [ac2]")),
            new("proof-duplicate-ac", Replace("acceptance_criteria: [AC2]", "acceptance_criteria: [AC2, AC2]")),
            new("proof-ac-null", Replace("acceptance_criteria: [AC2]", "acceptance_criteria: null")),
            new("proof-ac-map", Replace("acceptance_criteria: [AC2]", "acceptance_criteria: {id: AC2}")),
            new("proof-ac-nested-sequence", Replace("acceptance_criteria: [AC2]", "acceptance_criteria: [[AC2]]")),
            new("proof-ac-member-null", Replace("acceptance_criteria: [AC2]", "acceptance_criteria: [null]")),
            new("proof-ac-member-map", Replace("acceptance_criteria: [AC2]", "acceptance_criteria: [{id: AC2}]")),
            new("proof-unregistered-lane", Replace("lane: unit", "lane: unknown")),
            new("proof-lane-case-mismatch", Replace("lane: unit", "lane: Unit")),
            new("proof-lane-null", Replace("lane: unit", "lane: null")),
            new("proof-lane-map", Replace("lane: unit", "lane: {id: unit}")),
            new("proof-lane-sequence", Replace("lane: unit", "lane: [unit]")),
            new("proof-lane-explicit-bang", Replace("lane: unit", "lane: ! unit")),
            new("proof-duplicate-lane-key", Replace("      lane: mutation\n", "      lane: mutation\n      lane: mutation\n")),
            new("proof-unaffected-lane", Replace("affected_lanes: [mutation, unit]", "affected_lanes: [mutation]")),
            new("nested-contract-only", ReplaceContract(string.Empty).Replace(
                "  source_execution_unit: G865\n",
                "  source_execution_unit: G865\n  verification_contract: {}\n",
                StringComparison.Ordinal), "invalid-schema"),
            new("misplaced-nested-contract", Replace("  source_execution_unit: G865\n", "  source_execution_unit: G865\n  verification_contract: {}\n")),
            new("unsupported-deferral", Replace("  version: 1\n", "  version: 1\n  deferral: approved\n")),
            new("unsupported-evidence-pointer", Replace("  version: 1\n", "  version: 1\n  evidence_pointer: .artifacts/proof\n")),
            new("unsupported-authority", Replace("  version: 1\n", "  version: 1\n  authority: approved\n")),
            new("duplicate-contract-key", Replace("  version: 1\n", "  version: 1\n  version: 1\n")),
            new("unit-explicit-bang", Replace("source_execution_unit: G865", "source_execution_unit: ! G865"), "invalid-identity"),
            new("unit-key-explicit-bang", Replace("source_execution_unit:", "! source_execution_unit:"), "invalid-identity"),
            new("criteria-key-explicit-bang", Replace("  acceptance_criteria:\n    - Preserve marker identity", "  ! acceptance_criteria:\n    - Preserve marker identity")),
            new("identity-map-explicit-bang", Replace("implementation_issue_packet:\n", "implementation_issue_packet: !\n"), "invalid-identity"),
            new("acceptance-sequence-tag-invalid-schema", Replace("  acceptance_criteria:\n    - Preserve marker identity", "  acceptance_criteria: !!str\n    - Preserve marker identity")),
        ];

        string MissingProofsField()
        {
            var proofStart = CompletePacket.IndexOf("  proofs:\n", StringComparison.Ordinal);
            if (proofStart < 0) throw new InvalidOperationException("The complete control is missing its proofs field.");
            return CompletePacket[..proofStart];
        }
    }

    private static (int ExitCode, string Output) Run(string root, string format) => Run(root, format, Unit);

    private static (int ExitCode, string Output) Run(string root, string format, string unit)
    {
        return RunArgs(root, ["--execution-unit", unit, "--format", format]);
    }

    private static (int ExitCode, string Output) RunArgs(string root, string[] args)
    {
        using var writer = new StringWriter();
        var exit = PacketValidateVerificationCommand.Execute(root, args, writer);
        return (exit, writer.ToString());
    }

    private static void AssertUnavailable(JsonElement result, string diagnostic, string packetPath, string? hash)
    {
        Assert.Equal("unavailable", result.GetProperty("state").GetString());
        Assert.Equal("verification-contract-unavailable", result.GetProperty("cause").GetString());
        Assert.Equal("not-observed", result.GetProperty("execution").GetString());
        Assert.Equal(packetPath, result.GetProperty("packet_path").GetString());
        Assert.Equal(hash, result.GetProperty("packet_sha256").ValueKind == JsonValueKind.Null ? null : result.GetProperty("packet_sha256").GetString());
        Assert.Equal(diagnostic, result.GetProperty("diagnostics")[0].GetProperty("cause").GetString());
        Assert.Equal(packetPath, result.GetProperty("diagnostics")[0].GetProperty("path").GetString());
        Assert.Empty(result.GetProperty("acceptance_criteria").EnumerateArray());
        Assert.Empty(result.GetProperty("lanes").EnumerateArray());
        Assert.Empty(result.GetProperty("proofs").EnumerateArray());
        Assert.Empty(result.GetProperty("missing_acceptance_criteria").EnumerateArray());
        Assert.Empty(result.GetProperty("missing_lanes").EnumerateArray());
        Assert.Empty(result.GetProperty("missing_local_required_lanes").EnumerateArray());
    }

    private static void CreateFileSymlinkOrSkip(string link, string target)
    {
        try { File.CreateSymbolicLink(link, target); }
        catch (Exception exception) when (exception is PlatformNotSupportedException or UnauthorizedAccessException or IOException)
        {
            throw Xunit.Sdk.SkipException.ForSkip($"The platform did not permit a file symlink fixture: {exception.Message}");
        }
    }

    private static void CreateDirectorySymlinkOrSkip(string link, string target)
    {
        try { Directory.CreateSymbolicLink(link, target); }
        catch (Exception exception) when (exception is PlatformNotSupportedException or UnauthorizedAccessException or IOException)
        {
            throw Xunit.Sdk.SkipException.ForSkip($"The platform did not permit a directory symlink fixture: {exception.Message}");
        }
    }

    private static byte[] BuildPacketBytes(int acceptanceCount = 2, int laneCount = 2, int? proofCount = null,
        int idLength = 0, string? actualCriterion = null, string? command = null, string firstEvidence = "local-required")
    {
        if (acceptanceCount < 1 || laneCount < 1) throw new ArgumentOutOfRangeException(nameof(acceptanceCount));
        if (idLength == 0) idLength = 8;
        string Id(char prefix, int index)
        {
            var serial = index.ToString("D3", System.Globalization.CultureInfo.InvariantCulture);
            return prefix + serial + new string((char)('a' + (index % 26)), Math.Max(0, idLength - serial.Length - 1));
        }

        var acceptanceIds = Enumerable.Range(1, acceptanceCount).Select(index => Id('A', index)).ToArray();
        var laneIds = Enumerable.Range(1, laneCount).Select(index => Id('L', index)).ToArray();
        var actualCriteria = Enumerable.Range(1, acceptanceCount)
            .Select(index => index == 1 && actualCriterion is not null ? actualCriterion : $"criterion {index}")
            .ToArray();
        var count = proofCount ?? Math.Max(acceptanceCount, laneCount);
        var proofs = Enumerable.Range(1, count).Select(index => new
        {
            id = Id('P', index),
            acceptance_criteria = new[] { acceptanceIds[(index - 1) % acceptanceCount] },
            lane = laneIds[(index - 1) % laneCount],
            command = command ?? "echo structural test",
            evidence = index == 1 ? firstEvidence : "hosted-required",
        }).ToArray();
        var packet = new
        {
            implementation_issue_packet = new
            {
                source_execution_unit = Unit,
                acceptance_criteria = actualCriteria,
            },
            verification_contract = new
            {
                version = 1,
                acceptance_criteria = acceptanceIds.Select((id, index) => new { id, criterion_index = index + 1 }).ToArray(),
                lanes = laneIds.Select((id, index) => new { id, weight = index == 0 ? "heavy" : "ordinary" }).ToArray(),
                affected_lanes = laneIds,
                proofs,
            },
        };
        return JsonSerializer.SerializeToUtf8Bytes(packet);
    }

    private static string?[] Strings(JsonElement array) => array.EnumerateArray().Select(item => item.GetString()).ToArray();

    private static string FindRepositoryRoot()
    {
        var directory = AppContext.BaseDirectory;
        while (directory is not null && !Directory.Exists(Path.Combine(directory, "docs")))
            directory = Path.GetDirectoryName(directory);
        return directory ?? throw new DirectoryNotFoundException("Could not find repository docs from test base directory.");
    }

    [DllImport("libc", EntryPoint = "mkfifo", SetLastError = true, CharSet = CharSet.Ansi)]
    private static extern int MkFifo(string pathname, uint mode);

    private static string DeepUnrelatedValue(int maximumDepth)
    {
        var nested = "leaf";
        for (var depth = 1; depth < maximumDepth; depth++) nested = "{x: " + nested + "}";
        return "\npadding: " + nested + "\n";
    }

    private static string WithEmptyProofArray()
    {
        var proofStart = CompletePacket.IndexOf("  proofs:\n", StringComparison.Ordinal);
        if (proofStart < 0) throw new InvalidOperationException("The complete control is missing its proofs field.");
        return CompletePacket[..proofStart] + "  proofs: []\n";
    }

    private static string StructuredJson(string output, string format)
    {
        if (format == "json") return output;
        var heading = output.IndexOf("## Structured result (same facts as JSON)\n", StringComparison.Ordinal);
        Assert.True(heading >= 0, "Markdown must identify its structured result block.");
        var fenceStart = output.IndexOf('\n', heading + "## Structured result (same facts as JSON)".Length) + 1;
        if (fenceStart < output.Length && output[fenceStart] == '\n') fenceStart++;
        var openingEnd = output.IndexOf('\n', fenceStart);
        Assert.True(openingEnd > fenceStart, "Markdown JSON block must have an opening fence.");
        var opening = output[fenceStart..openingEnd];
        var ticks = opening.TakeWhile(character => character == '`').Count();
        Assert.True(ticks >= 3 && opening[ticks..] == "json", "Markdown result block must use a JSON code fence.");
        var start = openingEnd + 1;
        var closingFence = "\n" + new string('`', ticks);
        var end = output.IndexOf(closingFence, start, StringComparison.Ordinal);
        Assert.True(end > start, "Markdown JSON block must be closed.");
        return output[start..end];
    }

    private const string CompletePacket = """
        implementation_issue_packet:
          source_execution_unit: G865
          acceptance_criteria:
            - Preserve marker identity
            - Preserve legacy output
        verification_contract:
          version: 1
          acceptance_criteria:
            - {id: AC1, criterion_index: 1}
            - {id: AC2, criterion_index: 2}
          lanes:
            - {id: mutation, weight: heavy}
            - {id: unit, weight: ordinary}
          affected_lanes: [mutation, unit]
          proofs:
            - id: P1
              acceptance_criteria: [AC1]
              lane: mutation
              command: npm run test:mutation
              evidence: local-required
            - id: P2
              acceptance_criteria: [AC2]
              lane: unit
              command: dotnet test
              evidence: hosted-required
        """;

    private const string JsonPacket = """
        {"implementation_issue_packet":{"source_execution_unit":"G865","acceptance_criteria":["same text","same text"]},"verification_contract":{"version":1,"acceptance_criteria":[{"id":"AC2","criterion_index":"2"},{"id":"AC1","criterion_index":1}],"lanes":[{"id":"unit","weight":"ordinary"}],"affected_lanes":["unit"],"proofs":[{"id":"P2","acceptance_criteria":["AC2"],"lane":"unit","command":"echo \uFFFD","evidence":"hosted-required"},{"id":"P1","acceptance_criteria":["AC1"],"lane":"unit","command":"echo \uFFFD","evidence":"hosted-required"}]}}
        """;

    private const string FlowPacket = """
        {implementation_issue_packet: {source_execution_unit: G865, acceptance_criteria: [same, same]}, verification_contract: {version: 1, acceptance_criteria: [{id: AC2, criterion_index: 2}, {id: AC1, criterion_index: 1}], lanes: [{id: unit, weight: ordinary}], affected_lanes: [unit], proofs: [{id: P2, acceptance_criteria: [AC2], lane: unit, command: "echo ` ` `", evidence: hosted-required}, {id: P1, acceptance_criteria: [AC1], lane: unit, command: "echo ` ` `", evidence: hosted-required}]}}
        """;

    private const string HeavyLaneIncompletePacket = """
        implementation_issue_packet:
          source_execution_unit: G865
          acceptance_criteria: [first, second]
        verification_contract:
          version: 1
          acceptance_criteria:
            - {id: AC1, criterion_index: 1}
            - {id: AC2, criterion_index: 2}
          lanes:
            - {id: heavy, weight: heavy}
          affected_lanes: [heavy]
          proofs:
            - id: P1
              acceptance_criteria: [AC1]
              lane: heavy
              command: dotnet test
              evidence: hosted-required
        """;

    private sealed class TemporaryWorkspace : IDisposable
    {
        public TemporaryWorkspace()
        {
            Root = Path.Combine(Path.GetTempPath(), "intent-cli-g865-verification-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
        }

        public string Root { get; }

        public void WritePacket(byte[] bytes)
            => WritePacket(bytes, Unit);

        public void WritePacket(byte[] bytes, string unit)
        {
            var directory = Path.Combine(Root, ".intent-cli", "issues", unit);
            Directory.CreateDirectory(directory);
            File.WriteAllBytes(Path.Combine(directory, "packet.yaml"), bytes);
        }

        public string[] SnapshotTree() => Directory
            .EnumerateFileSystemEntries(Root, "*", SearchOption.AllDirectories)
            .Select(path =>
            {
                var relative = Path.GetRelativePath(Root, path).Replace(Path.DirectorySeparatorChar, '/');
                var attributes = File.GetAttributes(path);
                return (attributes & FileAttributes.Directory) != 0
                    ? $"directory:{relative}"
                    : $"file:{relative}:{Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant()}";
            })
            .Order(StringComparer.Ordinal)
            .ToArray();

        public void Dispose()
        {
            if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
        }
    }
}
