using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

[Collection(AutomationStalledWorkSharedStateCollection.Name)]
public sealed class PacketScopeSourcesTests
{
    private const string Domain = "intent-cli";
    private const string Team = "intent-cli-dev";
    private const string Repo = "J-Tech-Japan/intent-system";
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-10T12:00:00Z");

    [Fact]
    public void DeclarationShapeAliasesAndIdentityAreClosedBeforeRulingReads()
    {
        using var fixture = new SourceFixture();
        var validYaml = fixture.PacketYaml;
        var invalid = new (string Name, string Yaml, string Detail)[]
        {
            ("refs-null", validYaml.Replace("scope_sources:\n  -", "scope_sources: null\n# -", StringComparison.Ordinal), "scope_sources must be a sequence"),
            ("refs-scalar", validYaml.Replace("scope_sources:\n  - \"ruling:R-G863-SOURCE\"", "scope_sources: \"ruling:R-G863-SOURCE\"", StringComparison.Ordinal), "scope_sources must be a sequence"),
            ("refs-map", validYaml.Replace("scope_sources:\n  - \"ruling:R-G863-SOURCE\"", "scope_sources: { item: \"ruling:R-G863-SOURCE\" }", StringComparison.Ordinal), "scope_sources must be a sequence"),
            ("ref-null-member", validYaml.Replace("\"ruling:R-G863-SOURCE\"", "null", StringComparison.Ordinal), "textual scalar"),
            ("ref-map-member", validYaml.Replace("\"ruling:R-G863-SOURCE\"", "{ ref: \"ruling:R-G863-SOURCE\" }", StringComparison.Ordinal), "textual scalar"),
            ("ref-array-member", validYaml.Replace("\"ruling:R-G863-SOURCE\"", "[\"ruling:R-G863-SOURCE\"]", StringComparison.Ordinal), "textual scalar"),
            ("ref-wrong-prefix", validYaml.Replace("ruling:R-G863-SOURCE", "R-G863-SOURCE", StringComparison.Ordinal), "invalid ruling reference"),
            ("ref-path", validYaml.Replace("ruling:R-G863-SOURCE", "ruling:../R-G863-SOURCE", StringComparison.Ordinal), "invalid ruling reference"),
            ("duplicate-ref", validYaml.Replace("  - \"ruling:R-G863-SOURCE\"", "  - \"ruling:R-G863-SOURCE\"\n  - \"ruling:R-G863-SOURCE\"", StringComparison.Ordinal), "duplicate or case-alias"),
            ("case-alias-ref", validYaml.Replace("scope_sources:\n  - \"ruling:R-G863-SOURCE\"", "scope_sources:\n  - \"ruling:R-G863-SOURCE\"\n  - \"ruling:r-g863-source\"", StringComparison.Ordinal), "duplicate or case-alias"),
            ("empty-ref-id", validYaml.Replace("ruling:R-G863-SOURCE", "ruling:", StringComparison.Ordinal), "invalid ruling reference"),
            ("whitespace-ref-id", validYaml.Replace("ruling:R-G863-SOURCE", "ruling: R-G863-SOURCE", StringComparison.Ordinal), "invalid ruling reference"),
            ("duplicate-root-source-key", validYaml + "\nscope_sources: []\n", "duplicate source or pin keys"),
            ("duplicate-root-pin-key", validYaml + "\nscope_source_digests: {}\n", "duplicate source or pin keys"),
            ("duplicate-pin-reference", validYaml.Replace(
                $"  \"ruling:R-G863-SOURCE\": \"{fixture.Digest}\"",
                $"  \"ruling:R-G863-SOURCE\": \"{fixture.Digest}\"\n  \"ruling:R-G863-SOURCE\": \"{fixture.Digest}\"",
                StringComparison.Ordinal), "duplicate source or pin keys"),
            ("pins-null", validYaml.Replace("scope_source_digests:\n", "scope_source_digests: null\n#", StringComparison.Ordinal), "scope_source_digests must be a mapping"),
            ("pins-scalar", validYaml.Replace("scope_source_digests:\n", "scope_source_digests: abc\n#", StringComparison.Ordinal), "scope_source_digests must be a mapping"),
            ("pins-sequence", validYaml.Replace("scope_source_digests:\n", "scope_source_digests: []\n#", StringComparison.Ordinal), "scope_source_digests must be a mapping"),
            ("pin-null", validYaml.Replace($"\"{fixture.Digest}\"", "null", StringComparison.Ordinal), "textual scalar"),
            ("pin-map", validYaml.Replace($"\"{fixture.Digest}\"", "{sha256: " + fixture.Digest + "}", StringComparison.Ordinal), "textual scalar"),
            ("pin-sequence", validYaml.Replace($"\"{fixture.Digest}\"", "[" + fixture.Digest + "]", StringComparison.Ordinal), "textual scalar"),
            ("pin-short-digest", validYaml.Replace(fixture.Digest, "abc123", StringComparison.Ordinal), "64 lowercase hexadecimal"),
            ("missing-pin", validYaml.Replace("  \"ruling:R-G863-SOURCE\":", "  \"ruling:R-OTHER\":", StringComparison.Ordinal), "exactly one pin"),
            ("extra-pin", validYaml.Replace(
                $"  \"ruling:R-G863-SOURCE\": \"{fixture.Digest}\"",
                $"  \"ruling:R-G863-SOURCE\": \"{fixture.Digest}\"\n  \"ruling:R-G863-EXTRA\": \"{fixture.Digest}\"",
                StringComparison.Ordinal), "exactly one pin"),
            ("nested-pin-key", validYaml.Replace("  \"ruling:R-G863-SOURCE\":", "  nested:\n    \"ruling:R-G863-SOURCE\":", StringComparison.Ordinal), "keys and values must be textual scalars"),
            ("uppercase-digest", validYaml.Replace(fixture.Digest, fixture.Digest.ToUpperInvariant(), StringComparison.Ordinal), "lowercase hexadecimal"),
            ("misplaced-nested", validYaml.Replace("  target_repo: J-Tech-Japan/intent-system\n", "  target_repo: J-Tech-Japan/intent-system\n  scope_sources: []\n", StringComparison.Ordinal), "belong at the packet root"),
            ("tagged-root-key", validYaml.Replace("scope_sources:", "!!int scope_sources:", StringComparison.Ordinal), "plain textual keys"),
            ("source-alias", "refs: &source_refs [\"ruling:R-G863-SOURCE\"]\n" + validYaml.Replace("scope_sources:\n  - \"ruling:R-G863-SOURCE\"", "scope_sources: *source_refs", StringComparison.Ordinal), "aliases are not accepted"),
            ("identity-alias", validYaml.Replace("implementation_issue_packet:\n", "unit_value: &source_unit G863\nimplementation_issue_packet:\n", StringComparison.Ordinal)
                .Replace("source_execution_unit: G863", "source_execution_unit: *source_unit", StringComparison.Ordinal), "aliases are not accepted"),
        };

        foreach (var row in invalid)
        {
            var reads = 0;
            var result = PacketScopeSources.Evaluate(fixture.Root, "G863", Encoding.UTF8.GetBytes(row.Yaml),
                Encoding.UTF8.GetBytes(fixture.Body), Now, (_, _) => reads++);
            Assert.Equal("refused", result.State);
            Assert.Equal("scope-sources-invalid-declaration", result.Cause);
            Assert.Contains(row.Detail, result.Detail, StringComparison.OrdinalIgnoreCase);
            Assert.Null(result.Provenance);
            Assert.Equal(0, reads);
        }

        var metadataAlias = "metadata:\n  base: &text unrelated\n  alias: *text\n  scope_sources: *text\nimplementation_issue_packet/domain: *text\n"
            + validYaml.Replace("  target_repo: J-Tech-Japan/intent-system\n",
                "  target_repo: J-Tech-Japan/intent-system\n  metadata:\n    scope_sources: *text\n", StringComparison.Ordinal);
        var unrelated = fixture.Evaluate(metadataAlias, fixture.Body);
        Assert.Equal("satisfied", unrelated.State);
        Assert.NotEmpty(Assert.Single(unrelated.Provenance!).Sha256);
        Assert.Equal(fixture.Bytes, File.ReadAllBytes(Path.Combine(fixture.Root,
            fixture.Record.RelativePath.Replace('/', Path.DirectorySeparatorChar))));

        var duplicateIdentity = validYaml.Replace(
            "  domain: intent-cli\n",
            "  domain: intent-cli\n  domain: intent-cli\n",
            StringComparison.Ordinal);
        var identityRefusal = fixture.Evaluate(duplicateIdentity, fixture.Body);
        Assert.Equal("refused", identityRefusal.State);
        Assert.Equal("scope-sources-identity-mismatch", identityRefusal.Cause);
        Assert.Contains("duplicate", identityRefusal.Detail, StringComparison.OrdinalIgnoreCase);

        var quoted = validYaml.Replace("source_execution_unit: G863", "source_execution_unit: 'G863'", StringComparison.Ordinal)
            .Replace("domain: intent-cli", "domain: \"intent-cli\"", StringComparison.Ordinal)
            .Replace("team: intent-cli-dev", "team: 'intent-cli-dev'", StringComparison.Ordinal)
            .Replace("target_repo: J-Tech-Japan/intent-system", "target_repo: \"J-Tech-Japan/intent-system\"", StringComparison.Ordinal);
        Assert.Equal("satisfied", fixture.Evaluate(quoted, fixture.Body).State);

        var flow = $$"""{ implementation_issue_packet: { source_execution_unit: G863, domain: intent-cli, team: intent-cli-dev, target_repo: J-Tech-Japan/intent-system }, scope_sources: ["ruling:R-G863-SOURCE"], scope_source_digests: { "ruling:R-G863-SOURCE": "{{fixture.Digest}}" } }""";
        Assert.Equal("satisfied", fixture.Evaluate(flow, fixture.Body).State);
        var jsonAsYaml = $$$"""{"implementation_issue_packet":{"source_execution_unit":"G863","domain":"intent-cli","team":"intent-cli-dev","target_repo":"J-Tech-Japan/intent-system"},"scope_sources":["ruling:R-G863-SOURCE"],"scope_source_digests":{"ruling:R-G863-SOURCE":"{{{fixture.Digest}}}"}}""";
        Assert.Equal("satisfied", fixture.Evaluate(jsonAsYaml, fixture.Body).State);
    }

    [Fact]
    public void PublicBlockRequiresExactIdentityAndDisclaimerAndNormalizesCrLfOnlyForMatching()
    {
        using var fixture = new SourceFixture();
        var valid = fixture.Evaluate(fixture.PacketYaml, fixture.Body);
        Assert.Equal("satisfied", valid.State);
        Assert.Equal(fixture.Digest, Assert.Single(valid.Provenance!).Sha256);

        var crlf = fixture.Evaluate(fixture.PacketYaml, fixture.Body.Replace("\n", "\r\n", StringComparison.Ordinal));
        Assert.Equal("satisfied", crlf.State);

        var firstPath = Path.Combine(fixture.Root, fixture.Record.RelativePath.Replace('/', Path.DirectorySeparatorChar));
        var first = (fixture.Record, fixture.Bytes, fixture.Digest, firstPath);
        var second = WriteActualRuling(fixture.Root, "R-G863-SOURCE-SECOND", recordedAt: Now.AddMinutes(-1));
        var twoSources = BuildPacket("G863", [first, second], Domain, Team, Repo);
        var reverseYaml = $$"""
            implementation_issue_packet:
              source_execution_unit: G863
              domain: intent-cli
              team: intent-cli-dev
              target_repo: J-Tech-Japan/intent-system
            scope_sources:
              - "ruling:R-G863-SOURCE-SECOND"
              - "ruling:R-G863-SOURCE"
            scope_source_digests:
              "ruling:R-G863-SOURCE-SECOND": "{{second.Digest}}"
              "ruling:R-G863-SOURCE": "{{fixture.Digest}}"
            """;
        var reverse = PacketScopeSources.Evaluate(fixture.Root, "G863", Encoding.UTF8.GetBytes(reverseYaml),
            Encoding.UTF8.GetBytes(twoSources.Body), Now);
        Assert.Equal("satisfied", reverse.State);
        var ordinalFirst = twoSources.Body.IndexOf("ruling:R-G863-SOURCE |", StringComparison.Ordinal);
        var ordinalSecond = twoSources.Body.IndexOf("ruling:R-G863-SOURCE-SECOND |", StringComparison.Ordinal);
        Assert.True(ordinalFirst >= 0 && ordinalSecond > ordinalFirst);
        Assert.DoesNotContain(fixture.Record.Decision, twoSources.Body, StringComparison.Ordinal);
        Assert.DoesNotContain(fixture.Record.Rationale, twoSources.Body, StringComparison.Ordinal);
        Assert.DoesNotContain(string.Join(" ", fixture.Record.EvidenceRefs), twoSources.Body, StringComparison.Ordinal);
        var extraSourceLine = twoSources.Body.Replace("Authority: supplied-not-authenticated.", "- extra source line\nAuthority: supplied-not-authenticated.", StringComparison.Ordinal);
        Assert.Equal("scope-sources-provenance-mismatch", PacketScopeSources.Evaluate(fixture.Root, "G863",
            Encoding.UTF8.GetBytes(reverseYaml), Encoding.UTF8.GetBytes(extraSourceLine), Now).Cause);

        foreach (var mutatedBody in new[]
        {
            fixture.Body.Replace("Authority: supplied-not-authenticated. Publication: not-verified.\n", "", StringComparison.Ordinal),
            fixture.Body + "\n" + fixture.Body,
            fixture.Body.Replace(fixture.Digest, new string('0', 64), StringComparison.Ordinal),
            fixture.Body.Replace("team=intent-cli-dev", "team=other-team", StringComparison.Ordinal),
            fixture.Body.Replace("ruling:R-G863-SOURCE", "ruling:R-OTHER", StringComparison.Ordinal),
            fixture.Body.Replace("<!-- /intent-cli:scope-sources:v1 -->", "", StringComparison.Ordinal),
        })
        {
            var mismatch = fixture.Evaluate(fixture.PacketYaml, mutatedBody);
            Assert.Equal("refused", mismatch.State);
            Assert.Equal("scope-sources-provenance-mismatch", mismatch.Cause);
            Assert.NotNull(mismatch.ExpectedProvenanceBlock);
            Assert.Single(mismatch.Provenance!);
        }

        var invalidUtf8 = PacketScopeSources.Evaluate(fixture.Root, "G863", Encoding.UTF8.GetBytes(fixture.PacketYaml),
            [0xff, 0xfe], Now);
        Assert.Equal("unavailable", invalidUtf8.State);
        Assert.Equal("scope-sources-packet-unavailable", invalidUtf8.Cause);
        Assert.Single(invalidUtf8.Provenance!);
        Assert.Equal(valid.ExpectedProvenanceBlock, invalidUtf8.ExpectedProvenanceBlock);
    }

    [Fact]
    public void PublicBlockMatchesIndependentCanonicalV1Literal()
    {
        using var fixture = new SourceFixture();
        var expectedBlock = string.Join("\n",
        [
            "<!-- intent-cli:scope-sources:v1 -->",
            "### Ruling scope provenance",
            $"- ruling:R-G863-SOURCE | domain=intent-cli | team=intent-cli-dev | repo=J-Tech-Japan/intent-system | unit=G863 | sha256={fixture.Digest}",
            "Authority: supplied-not-authenticated. Publication: not-verified.",
            "<!-- /intent-cli:scope-sources:v1 -->",
        ]);

        Assert.Equal(4, expectedBlock.Count(character => character == '\n'));
        Assert.DoesNotContain('\r', expectedBlock);
        Assert.Equal(expectedBlock, fixture.Body);

        var accepted = fixture.Evaluate(fixture.PacketYaml, expectedBlock);
        Assert.Equal("satisfied", accepted.State);
        Assert.Equal(expectedBlock, accepted.ExpectedProvenanceBlock);
        Assert.Equal(fixture.Digest, Assert.Single(accepted.Provenance!).Sha256);

        var mismatch = fixture.Evaluate(fixture.PacketYaml,
            expectedBlock.Replace("team=intent-cli-dev", "team=other-team", StringComparison.Ordinal));
        Assert.Equal("refused", mismatch.State);
        Assert.Equal("scope-sources-provenance-mismatch", mismatch.Cause);
        Assert.Equal(expectedBlock, mismatch.ExpectedProvenanceBlock);
    }

    [Fact]
    public void AbsentAndExplicitEmptySourcesAreNotDeclaredAndNeverReadRulings()
    {
        using var fixture = new SourceFixture();
        foreach (var yaml in new[]
        {
            "implementation_issue_packet:\n  source_execution_unit: G863\n  domain: intent-cli\n",
            "scope_sources: []\nscope_source_digests: {}\n",
            "legacy_identity: &legacy {source_execution_unit: G863, domain: intent-cli}\nimplementation_issue_packet: *legacy\nscope_sources: []\nscope_source_digests: {}\n",
            "!!int implementation_issue_packet: {legacy: true}\nscope_sources: []\nscope_source_digests: {}\n",
        })
        {
            var reads = 0;
            var result = PacketScopeSources.Evaluate(fixture.Root, "G863", Encoding.UTF8.GetBytes(yaml), [], Now, (_, _) => reads++);
            Assert.Equal("not-declared", result.State);
            Assert.Equal("scope-sources-not-declared", result.Cause);
            Assert.Null(result.Provenance);
            Assert.Equal(0, reads);
        }

        var emptySourceAlias = "empty: &no_sources []\nscope_sources: *no_sources\nscope_source_digests: {}\n";
        var aliased = PacketScopeSources.Evaluate(fixture.Root, "G863", Encoding.UTF8.GetBytes(emptySourceAlias), [], Now);
        Assert.Equal("refused", aliased.State);
        Assert.Equal("scope-sources-invalid-declaration", aliased.Cause);
    }

    [Theory]
    [InlineData("json", true)]
    [InlineData("json", false)]
    [InlineData("markdown", true)]
    [InlineData("markdown", false)]
    public void UnsupportedWriteFlagUsesAnySupportedFormatForStructuredRefusal(string format, bool writeBeforeFormat)
    {
        using var fixture = new SourceFixture();
        var args = writeBeforeFormat
            ? new[] { "--write", "--execution-unit", "G863", "--format", format }
            : new[] { "--execution-unit", "G863", "--format", format, "--write" };
        using var output = new StringWriter();

        var exit = PacketValidateSourcesCommand.Execute(fixture.Root, args, output, Now, null);

        Assert.Equal(1, exit);
        Assert.Contains("scope-sources-invalid-declaration", output.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("rulings", output.ToString(), StringComparison.Ordinal);
        var sourcePath = Path.Combine(fixture.Root, ".intent-cli", "rulings", fixture.Record.Domain,
            fixture.Record.Team, fixture.Record.Id + ".json");
        Assert.Equal(fixture.Bytes, File.ReadAllBytes(sourcePath));
    }

    [Theory]
    [InlineData("repository")]
    [InlineData("execution-units")]
    public void ActualRulingWriter_ActiveRepositoryAndExactExecutionUnitScopesValidate(string scopeKind)
    {
        var root = NewRoot();
        try
        {
            var record = WriteActualRuling(root, "R-G863-BOUND", scopeKind: scopeKind,
                executionUnits: scopeKind == "execution-units" ? ["G863"] : []);
            var packet = BuildPacket("G863", [record], Domain, Team, Repo);
            var pending = PacketScopeSources.Evaluate(root, "G863", Encoding.UTF8.GetBytes(packet.Yaml), [], Now);
            Assert.Equal("scope-sources-provenance-mismatch", pending.Cause);
            Assert.NotNull(pending.ExpectedProvenanceBlock);
            var result = PacketScopeSources.Evaluate(root, "G863", Encoding.UTF8.GetBytes(packet.Yaml),
                Encoding.UTF8.GetBytes(pending.ExpectedProvenanceBlock!), Now);
            Assert.Equal("satisfied", result.State);
            Assert.Equal(record.Digest, Assert.Single(result.Provenance!).Sha256);
            Assert.Equal(record.Record.RelativePath, result.Provenance![0].Path);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("json")]
    [InlineData("markdown")]
    public void ValidateSourcesCommandReadsTheActualWriterAndMatchesJSONMarkdown(string format)
    {
        using var fixture = new SourceFixture();
        var packetDirectory = Path.Combine(fixture.Root, ".intent-cli", "issues", "G863");
        Directory.CreateDirectory(packetDirectory);
        File.WriteAllText(Path.Combine(packetDirectory, "packet.yaml"), fixture.PacketYaml);
        File.WriteAllBytes(Path.Combine(packetDirectory, "github-body.md"), Encoding.UTF8.GetBytes(fixture.Body));
        using var output = new StringWriter();

        var exit = PacketValidateSourcesCommand.Execute(fixture.Root,
            ["--execution-unit", "G863", "--format", format], output, Now, null);

        Assert.Equal(0, exit);
        if (format == "json")
        {
            using var result = JsonDocument.Parse(output.ToString());
            Assert.Equal("satisfied", result.RootElement.GetProperty("state").GetString());
            Assert.Equal("scope-sources-satisfied", result.RootElement.GetProperty("cause").GetString());
            Assert.Equal("supplied-not-authenticated", result.RootElement.GetProperty("authority_verification").GetString());
            Assert.Equal("not-verified", result.RootElement.GetProperty("publication").GetString());
            var source = Assert.Single(result.RootElement.GetProperty("provenance").EnumerateArray());
            Assert.Equal(fixture.Digest, source.GetProperty("sha256").GetString());
            Assert.Equal(fixture.Record.RelativePath, source.GetProperty("path").GetString());
        }
        else
        {
            Assert.Contains("- State: satisfied", output.ToString(), StringComparison.Ordinal);
            Assert.Contains("- Cause: scope-sources-satisfied", output.ToString(), StringComparison.Ordinal);
            Assert.Contains("- Authority verification: supplied-not-authenticated", output.ToString(), StringComparison.Ordinal);
            Assert.Contains("- Publication: not-verified", output.ToString(), StringComparison.Ordinal);
            Assert.Contains(fixture.Digest, output.ToString(), StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("json")]
    [InlineData("markdown")]
    public void ValidateSourcesCommandRejectsValidJsonWithNoncanonicalWriterBytes(string format)
    {
        using var fixture = new SourceFixture();
        var packetDirectory = Path.Combine(fixture.Root, ".intent-cli", "issues", "G863");
        Directory.CreateDirectory(packetDirectory);
        File.WriteAllText(Path.Combine(packetDirectory, "packet.yaml"), fixture.PacketYaml);
        File.WriteAllText(Path.Combine(packetDirectory, "github-body.md"), fixture.Body);

        var artifactPath = Path.Combine(fixture.Root, fixture.Record.RelativePath.Replace('/', Path.DirectorySeparatorChar));
        var noncanonicalBytes = fixture.Bytes.Concat(Encoding.UTF8.GetBytes(" \n")).ToArray();
        using (var syntacticallyValid = JsonDocument.Parse(noncanonicalBytes))
        {
            Assert.Equal("1", syntacticallyValid.RootElement.GetProperty("schema_version").GetString());
        }
        File.WriteAllBytes(artifactPath, noncanonicalBytes);
        var packetYamlBefore = File.ReadAllBytes(Path.Combine(packetDirectory, "packet.yaml"));
        var bodyBefore = File.ReadAllBytes(Path.Combine(packetDirectory, "github-body.md"));
        using var output = new StringWriter();

        var exit = PacketValidateSourcesCommand.Execute(fixture.Root,
            ["--execution-unit", "G863", "--format", format], output, Now, null);

        Assert.Equal(1, exit);
        if (format == "json")
        {
            using var result = JsonDocument.Parse(output.ToString());
            Assert.Equal("unavailable", result.RootElement.GetProperty("state").GetString());
            Assert.Equal("scope-sources-ruling-unavailable", result.RootElement.GetProperty("cause").GetString());
            AssertNullOrMissing(result.RootElement, "provenance");
            AssertNullOrMissing(result.RootElement, "expected_provenance_block");
            var diagnostic = Assert.Single(result.RootElement.GetProperty("diagnostics").EnumerateArray());
            Assert.Equal("ruling-noncanonical-artifact", diagnostic.GetProperty("cause").GetString());
            Assert.Equal(fixture.Record.RelativePath, diagnostic.GetProperty("path").GetString());
        }
        else
        {
            Assert.Contains("- State: unavailable", output.ToString(), StringComparison.Ordinal);
            Assert.Contains("- Cause: scope-sources-ruling-unavailable", output.ToString(), StringComparison.Ordinal);
            Assert.Contains("ruling-noncanonical-artifact", output.ToString(), StringComparison.Ordinal);
            Assert.Contains(fixture.Record.RelativePath, output.ToString(), StringComparison.Ordinal);
            Assert.DoesNotContain("## Verified sources", output.ToString(), StringComparison.Ordinal);
            Assert.DoesNotContain("## Expected public block", output.ToString(), StringComparison.Ordinal);
        }

        Assert.Equal(noncanonicalBytes, File.ReadAllBytes(artifactPath));
        Assert.Equal(packetYamlBefore, File.ReadAllBytes(Path.Combine(packetDirectory, "packet.yaml")));
        Assert.Equal(bodyBefore, File.ReadAllBytes(Path.Combine(packetDirectory, "github-body.md")));
    }

    [Fact]
    public void ActualRulingWriter_SixteenSourcesPassAndSeventeenthIsRejectedBeforeReads()
    {
        var root = NewRoot();
        try
        {
            var sixteen = Enumerable.Range(0, 16)
                .Select(index => WriteActualRuling(root, $"R-G863-{index:D2}"))
                .ToArray();
            var packet16 = BuildPacket("G863", sixteen, Domain, Team, Repo);
            var reads16 = 0;
            var mismatch16 = PacketScopeSources.Evaluate(root, "G863", Encoding.UTF8.GetBytes(packet16.Yaml), [], Now,
                (_, _) => reads16++);
            Assert.Equal("scope-sources-provenance-mismatch", mismatch16.Cause);
            Assert.True(reads16 > 0);
            var valid16 = PacketScopeSources.Evaluate(root, "G863", Encoding.UTF8.GetBytes(packet16.Yaml),
                Encoding.UTF8.GetBytes(mismatch16.ExpectedProvenanceBlock!), Now);
            Assert.Equal("satisfied", valid16.State);
            Assert.Equal(16, valid16.Provenance!.Count);

            var seventeen = sixteen.Append(WriteActualRuling(root, "R-G863-16")).ToArray();
            var packet17 = BuildPacket("G863", seventeen, Domain, Team, Repo);
            var reads17 = 0;
            var overLimit = PacketScopeSources.Evaluate(root, "G863", Encoding.UTF8.GetBytes(packet17.Yaml), [], Now,
                (_, _) => reads17++);
            Assert.Equal("refused", overLimit.State);
            Assert.Equal("scope-sources-invalid-declaration", overLimit.Cause);
            Assert.Contains("at most 16", overLimit.Detail, StringComparison.Ordinal);
            Assert.Equal(0, reads17);

            var invalid129 = packet16.Yaml.Replace("R-G863-00", "R" + new string('A', 128), StringComparison.Ordinal);
            var reads129 = 0;
            var invalid = PacketScopeSources.Evaluate(root, "G863", Encoding.UTF8.GetBytes(invalid129), [], Now,
                (_, _) => reads129++);
            Assert.Equal("scope-sources-invalid-declaration", invalid.Cause);
            Assert.Equal(0, reads129);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ActualRulingWriter_OneHundredTwentyEightCharacterIdentifiersRemainSupported()
    {
        var root = NewRoot();
        try
        {
            var domain = "D" + new string('d', 127);
            var team = "T" + new string('t', 127);
            var id = "R" + new string('r', 127);
            var unit = "U" + new string('u', 127);
            var source = WriteActualRuling(root, id, domain, team, scopeKind: "execution-units", executionUnits: [unit]);
            var packet = BuildPacket(unit, [source], domain, team, Repo);
            var pending = PacketScopeSources.Evaluate(root, unit, Encoding.UTF8.GetBytes(packet.Yaml), [], Now);
            Assert.Equal("scope-sources-provenance-mismatch", pending.Cause);
            var result = PacketScopeSources.Evaluate(root, unit, Encoding.UTF8.GetBytes(packet.Yaml),
                Encoding.UTF8.GetBytes(pending.ExpectedProvenanceBlock!), Now);
            Assert.Equal("satisfied", result.State);
            Assert.Equal(source.Digest, Assert.Single(result.Provenance!).Sha256);
            Assert.Equal(unit, Assert.Single(result.Provenance!).ExecutionUnit);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("foreign-domain", "intent-cli", "intent-cli-dev", "J-Tech-Japan/intent-system", "scope-sources-ruling-missing")]
    [InlineData("foreign-team", "intent-cli", "intent-cli-dev", "J-Tech-Japan/intent-system", "scope-sources-ruling-missing")]
    [InlineData("wrong-repo-case", "intent-cli", "intent-cli-dev", "j-tech-japan/intent-system", "scope-sources-applicability-mismatch")]
    [InlineData("unit-excluded", "intent-cli", "intent-cli-dev", "J-Tech-Japan/intent-system", "scope-sources-applicability-mismatch")]
    public void ActualRulingWriter_IdentityAndApplicabilityFailuresDoNotFallback(string scenario, string packetDomain,
        string packetTeam, string packetRepo, string expectedCause)
    {
        var root = NewRoot();
        try
        {
            var record = scenario == "foreign-domain"
                ? WriteActualRuling(root, "R-G863-BOUND", domain: "foreign-domain")
                : scenario == "foreign-team"
                    ? WriteActualRuling(root, "R-G863-BOUND", team: "foreign-team")
                : WriteActualRuling(root, "R-G863-BOUND", scopeKind: scenario == "unit-excluded" ? "execution-units" : "repository",
                    executionUnits: scenario == "unit-excluded" ? ["G999"] : []);
            var packet = BuildPacket("G863", [record], packetDomain, packetTeam, packetRepo);
            var result = PacketScopeSources.Evaluate(root, "G863", Encoding.UTF8.GetBytes(packet.Yaml), [], Now);
            Assert.Equal("refused", result.State);
            Assert.Equal(expectedCause, result.Cause);
            Assert.Null(result.Provenance);
            if (scenario == "foreign-domain")
                Assert.Contains("missing", result.Detail, StringComparison.Ordinal);
            else if (scenario is "wrong-repo-case" or "unit-excluded")
                Assert.Contains("does not apply", result.Detail, StringComparison.Ordinal);
            else
                Assert.Contains("missing", result.Detail, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("unit", "scope-sources-identity-mismatch")]
    [InlineData("missing-unit", "scope-sources-identity-mismatch")]
    [InlineData("missing-domain", "scope-sources-identity-mismatch")]
    [InlineData("missing-team", "scope-sources-identity-mismatch")]
    [InlineData("missing-repo", "scope-sources-identity-mismatch")]
    [InlineData("domain", "scope-sources-ruling-missing")]
    [InlineData("team", "scope-sources-ruling-missing")]
    [InlineData("repo", "scope-sources-applicability-mismatch")]
    public void ActualRulingWriter_RequiredIdentityFieldsAreExactAndNeverDefaulted(string field, string expectedCause)
    {
        using var fixture = new SourceFixture();
        var yaml = field switch
        {
            "unit" => fixture.PacketYaml.Replace("source_execution_unit: G863", "source_execution_unit: G864", StringComparison.Ordinal),
            "missing-unit" => fixture.PacketYaml.Replace("  source_execution_unit: G863\n", string.Empty, StringComparison.Ordinal),
            "missing-domain" => fixture.PacketYaml.Replace("  domain: intent-cli\n", string.Empty, StringComparison.Ordinal),
            "missing-team" => fixture.PacketYaml.Replace("  team: intent-cli-dev\n", string.Empty, StringComparison.Ordinal),
            "missing-repo" => fixture.PacketYaml.Replace("  target_repo: J-Tech-Japan/intent-system\n", string.Empty, StringComparison.Ordinal),
            "domain" => fixture.PacketYaml.Replace("domain: intent-cli", "domain: foreign-domain", StringComparison.Ordinal),
            "team" => fixture.PacketYaml.Replace("team: intent-cli-dev", "team: foreign-team", StringComparison.Ordinal),
            "repo" => fixture.PacketYaml.Replace("target_repo: J-Tech-Japan/intent-system", "target_repo: J-Tech-Japan/other", StringComparison.Ordinal),
            _ => throw new ArgumentOutOfRangeException(nameof(field)),
        };
        var reads = 0;

        var result = PacketScopeSources.Evaluate(fixture.Root, "G863", Encoding.UTF8.GetBytes(yaml),
            Encoding.UTF8.GetBytes(fixture.Body), Now, (_, _) => reads++);

        Assert.Equal("refused", result.State);
        Assert.Equal(expectedCause, result.Cause);
        Assert.Null(result.Provenance);
        if (field is "unit" or "missing-unit" or "missing-domain" or "missing-team" or "missing-repo")
            Assert.Equal(0, reads);
    }

    [Fact]
    public void ActualRulingWriter_ExpirySupersessionAndPinnedContentTamperingAreDistinct()
    {
        var root = NewRoot();
        try
        {
            var expiry = Now;
            var expiring = WriteActualRuling(root, "R-G863-EXPIRES", recordedAt: Now.AddHours(-1), expiresAt: expiry);
            var expiredPacket = BuildPacket("G863", [expiring], Domain, Team, Repo);
            var expired = PacketScopeSources.Evaluate(root, "G863", Encoding.UTF8.GetBytes(expiredPacket.Yaml), [], expiry);
            Assert.Equal("refused", expired.State);
            Assert.Equal("scope-sources-ruling-inactive", expired.Cause);
            Assert.Contains("expired", expired.Detail, StringComparison.Ordinal);
            var beforeExpiry = PacketScopeSources.Evaluate(root, "G863", Encoding.UTF8.GetBytes(expiredPacket.Yaml), [], expiry.AddTicks(-1));
            Assert.Equal("scope-sources-provenance-mismatch", beforeExpiry.Cause);
            Assert.NotNull(beforeExpiry.ExpectedProvenanceBlock);

            var predecessor = WriteActualRuling(root, "R-G863-PREV", recordedAt: Now.AddHours(-2));
            var successor = WriteActualRuling(root, "R-G863-NEXT", recordedAt: Now.AddHours(-1), supersedes: [predecessor.Record.Id]);
            var oldPin = BuildPacket("G863", [predecessor], Domain, Team, Repo);
            var superseded = PacketScopeSources.Evaluate(root, "G863", Encoding.UTF8.GetBytes(oldPin.Yaml), [], Now);
            Assert.Equal("scope-sources-ruling-inactive", superseded.Cause);
            Assert.Contains("superseded", superseded.Detail, StringComparison.Ordinal);
            var newPin = BuildPacket("G863", [successor], Domain, Team, Repo);
            var activeNew = PacketScopeSources.Evaluate(root, "G863", Encoding.UTF8.GetBytes(newPin.Yaml), [], Now);
            Assert.Equal("scope-sources-provenance-mismatch", activeNew.Cause);
            Assert.NotNull(activeNew.ExpectedProvenanceBlock);

            var tampered = successor.Record with { Decision = "Canonical content changed after the packet was pinned." };
            File.WriteAllBytes(successor.Path, RulingArtifact.Serialize(tampered));
            var changed = PacketScopeSources.Evaluate(root, "G863", Encoding.UTF8.GetBytes(newPin.Yaml), [], Now);
            Assert.Equal("scope-sources-digest-mismatch", changed.Cause);
            Assert.Null(changed.Provenance);
            Assert.Null(changed.ExpectedProvenanceBlock);

            File.WriteAllText(successor.Path, "{\"schema_version\":\"1\"}\n");
            var malformed = PacketScopeSources.Evaluate(root, "G863", Encoding.UTF8.GetBytes(newPin.Yaml), [], Now);
            Assert.Equal("unavailable", malformed.State);
            Assert.Equal("scope-sources-ruling-unavailable", malformed.Cause);
            Assert.Contains(malformed.Diagnostics, diagnostic => diagnostic.Cause == "ruling-invalid-input");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ActualRulingWriter_CompetingSuccessorsRemainAConflict()
    {
        var root = NewRoot();
        var branchA = NewRoot();
        var branchB = NewRoot();
        try
        {
            var predecessor = WriteActualRuling(root, "R-G863-CONFLICT-PREV", recordedAt: Now.AddHours(-3));
            SeedActualArtifact(branchA, predecessor);
            SeedActualArtifact(branchB, predecessor);
            var successorA = WriteActualRuling(branchA, "R-G863-CONFLICT-A", recordedAt: Now.AddHours(-2), supersedes: [predecessor.Record.Id]);
            var successorB = WriteActualRuling(branchB, "R-G863-CONFLICT-B", recordedAt: Now.AddHours(-1), supersedes: [predecessor.Record.Id]);
            SeedActualArtifact(root, successorA);
            SeedActualArtifact(root, successorB);
            var packet = BuildPacket("G863", [predecessor], Domain, Team, Repo);

            var directRead = new RulingArtifactStore(root).Evaluate(Domain, Team, predecessor.Record.Id, Now);
            Assert.Equal("conflict", directRead.Status);
            Assert.Equal("ruling-successor-conflict", directRead.Cause);

            var result = PacketScopeSources.Evaluate(root, "G863", Encoding.UTF8.GetBytes(packet.Yaml), [], Now);

            Assert.Equal("refused", result.State);
            Assert.Equal("scope-sources-ruling-conflict", result.Cause);
            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Cause == "ruling-successor-conflict");
            Assert.Null(result.Provenance);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
            Directory.Delete(branchA, recursive: true);
            Directory.Delete(branchB, recursive: true);
        }
    }

    [Fact]
    public void MalformedStoredSuccessorGraphIsUnavailableWithUnderlyingReaderCause()
    {
        var root = NewRoot();
        try
        {
            var predecessor = WriteActualRuling(root, "R-G863-GRAPH-PREV", recordedAt: Now.AddHours(-2));
            var successor = WriteActualRuling(root, "R-G863-GRAPH-NEXT", recordedAt: Now.AddHours(-1), supersedes: [predecessor.Record.Id]);
            File.WriteAllBytes(successor.Path, RulingArtifact.Serialize(successor.Record with { Supersedes = ["R-G863-UNKNOWN"] }));
            var storeResult = new RulingArtifactStore(root).Evaluate(Domain, Team, predecessor.Record.Id, Now);
            Assert.Equal("unavailable", storeResult.Status);
            Assert.Contains(storeResult.Diagnostics, diagnostic => diagnostic.Cause == "ruling-graph-conflict");

            var packet = BuildPacket("G863", [predecessor], Domain, Team, Repo);
            var result = PacketScopeSources.Evaluate(root, "G863", Encoding.UTF8.GetBytes(packet.Yaml), Encoding.UTF8.GetBytes(packet.Body), Now);
            Assert.Equal("unavailable", result.State);
            Assert.Equal("scope-sources-ruling-unavailable", result.Cause);
            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Cause == "ruling-graph-conflict");
            Assert.Null(result.Provenance);
            Assert.Null(result.ExpectedProvenanceBlock);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void FiveHundredOneActualWriterArtifactsHitTheExistingInventoryLimit()
    {
        var root = NewRoot();
        try
        {
            var scopeDirectory = Path.Combine(root, ".intent-cli", "rulings", Domain, Team);
            Directory.CreateDirectory(scopeDirectory);
            (RulingArtifact Record, byte[] Bytes, string Digest, string Path) first = default;
            for (var index = 0; index < 501; index++)
            {
                var seedRoot = NewRoot();
                try
                {
                    var id = $"R-G863-LIMIT-{index:D3}";
                    var emitted = WriteActualRuling(seedRoot, id, recordedAt: Now.AddMinutes(-1));
                    if (index == 0) first = emitted;
                    File.WriteAllBytes(Path.Combine(scopeDirectory, id + ".json"), emitted.Bytes);
                }
                finally
                {
                    Directory.Delete(seedRoot, recursive: true);
                }
            }

            var packet = BuildPacket("G863", [first], Domain, Team, Repo);
            var reads = 0;
            var result = PacketScopeSources.Evaluate(root, "G863", Encoding.UTF8.GetBytes(packet.Yaml),
                Encoding.UTF8.GetBytes(packet.Body), Now, (operation, _) => { if (operation == "read") reads++; });

            Assert.Equal("unavailable", result.State);
            Assert.Equal("scope-sources-ruling-unavailable", result.Cause);
            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Cause == "ruling-inventory-limit");
            Assert.Equal(0, reads);
            Assert.Equal(501, Directory.GetFiles(scopeDirectory, "*.json").Length);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void NonrequestedScopedJsonDirectoryMakesTheWholeSourceInventoryUnavailable()
    {
        using var fixture = new SourceFixture();
        var packetDirectory = Path.Combine(fixture.Root, ".intent-cli", "issues", "G863");
        Directory.CreateDirectory(packetDirectory);
        File.WriteAllText(Path.Combine(packetDirectory, "packet.yaml"), fixture.PacketYaml);
        File.WriteAllText(Path.Combine(packetDirectory, "github-body.md"), fixture.Body);
        var scopedDirectory = Path.Combine(fixture.Root, ".intent-cli", "rulings", Domain, Team);
        Directory.CreateDirectory(Path.Combine(scopedDirectory, "R-G863-ZZZ.json"));

        var result = PacketScopeSources.Evaluate(fixture.Root, "G863", Encoding.UTF8.GetBytes(fixture.PacketYaml),
            Encoding.UTF8.GetBytes(fixture.Body), Now);

        Assert.Equal("unavailable", result.State);
        Assert.Equal("scope-sources-ruling-unavailable", result.Cause);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Cause is "ruling-unsafe-path" or "ruling-artifact-unavailable" or "ruling-invalid-layout");
        Assert.Null(result.Provenance);
    }

    [Theory]
    [InlineData("json")]
    [InlineData("markdown")]
    public void TargetScopedJsonDirectoryIsRefusedByTheActualValidationCommand(string format)
    {
        using var fixture = new SourceFixture();
        var packetDirectory = Path.Combine(fixture.Root, ".intent-cli", "issues", "G863");
        Directory.CreateDirectory(packetDirectory);
        File.WriteAllText(Path.Combine(packetDirectory, "packet.yaml"), fixture.PacketYaml);
        File.WriteAllText(Path.Combine(packetDirectory, "github-body.md"), fixture.Body);
        var targetPath = Path.Combine(fixture.Root, fixture.Record.RelativePath.Replace('/', Path.DirectorySeparatorChar));
        File.Delete(targetPath);
        Directory.CreateDirectory(targetPath);
        var reads = new List<string>();
        using var output = new StringWriter();

        var exit = PacketValidateSourcesCommand.Execute(fixture.Root,
            ["--execution-unit", "G863", "--format", format], output, Now,
            (operation, path) => { if (operation == "read") reads.Add(path); });

        Assert.Equal(1, exit);
        Assert.Contains("scope-sources-ruling-unavailable", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("ruling-unsafe-path", output.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(reads, path => path == targetPath);
        Assert.True(Directory.Exists(targetPath));
        Assert.Empty(Directory.GetFileSystemEntries(targetPath));
    }

    [Theory]
    [InlineData("json")]
    [InlineData("markdown")]
    public void NullSourceJsonIsRefusedByTheActualValidationCommandWithoutChangingRulingBytes(string format)
    {
        using var fixture = new SourceFixture();
        var packetDirectory = Path.Combine(fixture.Root, ".intent-cli", "issues", "G863");
        Directory.CreateDirectory(packetDirectory);
        File.WriteAllText(Path.Combine(packetDirectory, "packet.yaml"),
            "implementation_issue_packet:\n  source_execution_unit: G863\n  domain: intent-cli\n  team: intent-cli-dev\n  target_repo: J-Tech-Japan/intent-system\nscope_sources: null\nscope_source_digests: {}\n");
        File.WriteAllText(Path.Combine(packetDirectory, "github-body.md"), fixture.Body);
        var targetPath = Path.Combine(fixture.Root, fixture.Record.RelativePath.Replace('/', Path.DirectorySeparatorChar));
        var original = File.ReadAllBytes(targetPath);
        using var output = new StringWriter();

        var exit = PacketValidateSourcesCommand.Execute(fixture.Root,
            ["--execution-unit", "G863", "--format", format], output, Now, null);

        Assert.Equal(1, exit);
        Assert.Contains("scope-sources-invalid-declaration", output.ToString(), StringComparison.Ordinal);
        Assert.Equal(original, File.ReadAllBytes(targetPath));
        Assert.False(Directory.Exists(Path.Combine(fixture.Root, ".intent-cli", "rulings", Domain, Team, "R-G863-SOURCE.tmp")));
    }

    [Theory]
    [InlineData("json")]
    [InlineData("markdown")]
    public void NullJsonInAValidPinnedRulingIsRejectedByTheActualValidationCommandWithoutChangingState(string format)
    {
        using var fixture = new SourceFixture();
        var packetDirectory = Path.Combine(fixture.Root, ".intent-cli", "issues", "G863");
        Directory.CreateDirectory(packetDirectory);
        var packetPath = Path.Combine(packetDirectory, "packet.yaml");
        var bodyPath = Path.Combine(packetDirectory, "github-body.md");
        File.WriteAllText(packetPath, fixture.PacketYaml);
        File.WriteAllText(bodyPath, fixture.Body);

        var targetPath = Path.Combine(fixture.Root, fixture.Record.RelativePath.Replace('/', Path.DirectorySeparatorChar));
        var invalidBytes = Encoding.UTF8.GetBytes("null");
        File.WriteAllBytes(targetPath, invalidBytes);
        var stateRoot = Path.Combine(fixture.Root, ".intent-cli");
        var directoriesBefore = Directory.EnumerateDirectories(stateRoot, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(stateRoot, path))
            .Order(StringComparer.Ordinal)
            .ToArray();
        var filesBefore = Directory.EnumerateFiles(stateRoot, "*", SearchOption.AllDirectories)
            .ToDictionary(path => Path.GetRelativePath(stateRoot, path), File.ReadAllBytes, StringComparer.Ordinal);
        var reads = new List<string>();
        using var output = new StringWriter();

        var exit = PacketValidateSourcesCommand.Execute(fixture.Root,
            ["--execution-unit", "G863", "--format", format], output, Now,
            (operation, path) => { if (operation == "read") reads.Add(path); });

        Assert.Equal(1, exit);
        Assert.Contains(targetPath, reads, StringComparer.Ordinal);
        Assert.Equal(invalidBytes, File.ReadAllBytes(targetPath));
        var directoriesAfter = Directory.EnumerateDirectories(stateRoot, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(stateRoot, path))
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(directoriesBefore, directoriesAfter);
        var filesAfter = Directory.EnumerateFiles(stateRoot, "*", SearchOption.AllDirectories)
            .ToDictionary(path => Path.GetRelativePath(stateRoot, path), File.ReadAllBytes, StringComparer.Ordinal);
        Assert.Equal(filesBefore.Keys.Order(StringComparer.Ordinal), filesAfter.Keys.Order(StringComparer.Ordinal));
        foreach (var (relativePath, bytes) in filesBefore)
            Assert.Equal(bytes, filesAfter[relativePath]);

        if (format == "json")
        {
            using var result = JsonDocument.Parse(output.ToString());
            Assert.Equal("unavailable", result.RootElement.GetProperty("state").GetString());
            Assert.Equal("scope-sources-ruling-unavailable", result.RootElement.GetProperty("cause").GetString());
            Assert.Equal(JsonValueKind.Null, result.RootElement.GetProperty("provenance").ValueKind);
            Assert.Contains(result.RootElement.GetProperty("diagnostics").EnumerateArray(),
                diagnostic => diagnostic.GetProperty("cause").GetString() == "ruling-invalid-input");
        }
        else
        {
            Assert.Contains("- State: unavailable", output.ToString(), StringComparison.Ordinal);
            Assert.Contains("scope-sources-ruling-unavailable", output.ToString(), StringComparison.Ordinal);
            Assert.Contains("ruling-invalid-input", output.ToString(), StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData(typeof(IOException))]
    [InlineData(typeof(UnauthorizedAccessException))]
    public void ActualRulingReadFailurePreservesTheReachedPathAndUnavailableState(Type exceptionType)
    {
        using var fixture = new SourceFixture();
        var reads = new List<(string Operation, string Path)>();
        var result = PacketScopeSources.Evaluate(fixture.Root, "G863", Encoding.UTF8.GetBytes(fixture.PacketYaml),
            Encoding.UTF8.GetBytes(fixture.Body), Now, (operation, path) =>
            {
                reads.Add((operation, path));
                if (operation == "read") throw (Exception)Activator.CreateInstance(exceptionType)!;
            });

        Assert.Equal("unavailable", result.State);
        Assert.Equal("scope-sources-ruling-unavailable", result.Cause);
        Assert.Contains(reads, read => read.Operation == "read" && read.Path.EndsWith("R-G863-SOURCE.json", StringComparison.Ordinal));
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Cause == "ruling-artifact-unavailable");
        Assert.Null(result.Provenance);
        Assert.Null(result.ExpectedProvenanceBlock);
    }

    [Fact]
    public void TargetSymlinkIsRejectedBeforeReadingItsOutsideTarget()
    {
        using var fixture = new SourceFixture();
        var externalPath = Path.Combine(Path.GetDirectoryName(fixture.Root)!, "g863-external-" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllBytes(externalPath, fixture.Bytes);
        try
        {
            var packetDirectory = Path.Combine(fixture.Root, ".intent-cli", "issues", "G863");
            Directory.CreateDirectory(packetDirectory);
            File.WriteAllText(Path.Combine(packetDirectory, "packet.yaml"), fixture.PacketYaml);
            File.WriteAllText(Path.Combine(packetDirectory, "github-body.md"), fixture.Body);
            File.Delete(Path.Combine(fixture.Root, ".intent-cli", "rulings", Domain, Team, fixture.Record.Id + ".json"));
            var targetPath = Path.Combine(fixture.Root, fixture.Record.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            File.CreateSymbolicLink(targetPath, externalPath);
            var readPaths = new List<string>();

            using var output = new StringWriter();
            var exit = PacketValidateSourcesCommand.Execute(fixture.Root,
                ["--execution-unit", "G863", "--format", "json"], output, Now,
                (operation, path) => { if (operation == "read") readPaths.Add(path); });

            Assert.Equal(1, exit);
            using var result = JsonDocument.Parse(output.ToString());
            Assert.Equal("unavailable", result.RootElement.GetProperty("state").GetString());
            Assert.Equal("scope-sources-ruling-unavailable", result.RootElement.GetProperty("cause").GetString());
            Assert.Contains(result.RootElement.GetProperty("diagnostics").EnumerateArray(),
                diagnostic => diagnostic.GetProperty("cause").GetString() == "ruling-unsafe-path");
            Assert.DoesNotContain(readPaths, path => path == externalPath || path.EndsWith(fixture.Record.Id + ".json", StringComparison.Ordinal));
            Assert.True(File.Exists(externalPath));
            Assert.Equal(fixture.Bytes, File.ReadAllBytes(externalPath));
            Assert.True(File.Exists(targetPath));
            Assert.True(new FileInfo(targetPath).LinkTarget is not null);
        }
        finally
        {
            File.Delete(externalPath);
        }
    }

    private static void SeedActualArtifact(string root,
        (RulingArtifact Record, byte[] Bytes, string Digest, string Path) artifact)
    {
        var path = Path.Combine(root, artifact.Record.RelativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, artifact.Bytes);
        Assert.Equal(artifact.Digest, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant());
    }

    [Theory]
    [InlineData("json")]
    [InlineData("markdown")]
    public void NonrequestedScopedJsonSymlinkMakesTheWholeSourceInventoryUnavailable(string format)
    {
        using var fixture = new SourceFixture();
        var externalPath = Path.Combine(Path.GetDirectoryName(fixture.Root)!, "g863-unrelated-" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllBytes(externalPath, fixture.Bytes);
        try
        {
            var rulingDirectory = Path.GetDirectoryName(Path.Combine(fixture.Root, fixture.Record.RelativePath.Replace('/', Path.DirectorySeparatorChar)))!;
            var siblingLink = Path.Combine(rulingDirectory, "R-G863-UNRELATED.json");
            File.CreateSymbolicLink(siblingLink, externalPath);
            var packetDirectory = Path.Combine(fixture.Root, ".intent-cli", "issues", "G863");
            Directory.CreateDirectory(packetDirectory);
            File.WriteAllText(Path.Combine(packetDirectory, "packet.yaml"), fixture.PacketYaml);
            File.WriteAllBytes(Path.Combine(packetDirectory, "github-body.md"), Encoding.UTF8.GetBytes(fixture.Body));
            var attemptedReads = new List<string>();
            using var output = new StringWriter();

            var exit = PacketValidateSourcesCommand.Execute(fixture.Root,
                ["--execution-unit", "G863", "--format", format], output, Now,
                (operation, path) => { if (operation == "read") attemptedReads.Add(path); });

            Assert.Equal(1, exit);
            if (format == "json")
            {
                using var result = JsonDocument.Parse(output.ToString());
                Assert.Equal("unavailable", result.RootElement.GetProperty("state").GetString());
                Assert.Equal("scope-sources-ruling-unavailable", result.RootElement.GetProperty("cause").GetString());
                Assert.Contains(result.RootElement.GetProperty("diagnostics").EnumerateArray(),
                    diagnostic => diagnostic.GetProperty("cause").GetString() == "ruling-unsafe-path");
            }
            else
            {
                Assert.Contains("- State: unavailable", output.ToString(), StringComparison.Ordinal);
                Assert.Contains("scope-sources-ruling-unavailable", output.ToString(), StringComparison.Ordinal);
                Assert.Contains("ruling-unsafe-path", output.ToString(), StringComparison.Ordinal);
            }
            Assert.DoesNotContain(attemptedReads, path => path == siblingLink || path == externalPath);
            Assert.Equal(fixture.Bytes, File.ReadAllBytes(Path.Combine(fixture.Root, fixture.Record.RelativePath.Replace('/', Path.DirectorySeparatorChar))));
            Assert.Equal(fixture.Bytes, File.ReadAllBytes(externalPath));
        }
        finally
        {
            File.Delete(externalPath);
        }
    }

    [Theory]
    [InlineData("json")]
    [InlineData("markdown")]
    public void TeamDirectorySymlinkIsRefusedByTheActualValidationCommandBeforeReadingOutside(string format)
    {
        using var fixture = new SourceFixture();
        var rulingDirectory = Path.GetDirectoryName(Path.Combine(fixture.Root,
            fixture.Record.RelativePath.Replace('/', Path.DirectorySeparatorChar)))!;
        var teamDirectory = rulingDirectory;
        var externalDirectory = Path.Combine(Path.GetDirectoryName(fixture.Root)!, "g863-team-target-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(externalDirectory);
        var externalRecord = Path.Combine(externalDirectory, fixture.Record.Id + ".json");
        File.WriteAllBytes(externalRecord, fixture.Bytes);
        try
        {
            Directory.Delete(teamDirectory, recursive: true);
            Directory.CreateSymbolicLink(teamDirectory, externalDirectory);
            var packetDirectory = Path.Combine(fixture.Root, ".intent-cli", "issues", "G863");
            Directory.CreateDirectory(packetDirectory);
            File.WriteAllText(Path.Combine(packetDirectory, "packet.yaml"), fixture.PacketYaml);
            File.WriteAllText(Path.Combine(packetDirectory, "github-body.md"), fixture.Body);
            var reads = new List<string>();
            using var output = new StringWriter();

            var exit = PacketValidateSourcesCommand.Execute(fixture.Root,
                ["--execution-unit", "G863", "--format", format], output, Now,
                (operation, path) => { if (operation == "read") reads.Add(path); });

            Assert.Equal(1, exit);
            Assert.Contains("scope-sources-ruling-unavailable", output.ToString(), StringComparison.Ordinal);
            Assert.Contains("ruling-unsafe-path", output.ToString(), StringComparison.Ordinal);
            Assert.DoesNotContain(reads, path => path == externalRecord);
            Assert.Equal(fixture.Bytes, File.ReadAllBytes(externalRecord));
            Assert.True(new DirectoryInfo(teamDirectory).LinkTarget is not null);
        }
        finally
        {
            if (Directory.Exists(teamDirectory) || new DirectoryInfo(teamDirectory).LinkTarget is not null)
                Directory.Delete(teamDirectory);
            Directory.Delete(externalDirectory, recursive: true);
        }
    }

    private static string NewRoot()
    {
        var root = Path.Combine(OperatingSystem.IsMacOS() ? "/private/tmp" : Path.GetTempPath(), "intent-cli-g863-source-matrix-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static (RulingArtifact Record, byte[] Bytes, string Digest, string Path) WriteActualRuling(
        string root, string id, string domain = "intent-cli", string team = "intent-cli-dev", string targetRepo = "J-Tech-Japan/intent-system",
        string scopeKind = "repository", IReadOnlyList<string>? executionUnits = null, DateTimeOffset? recordedAt = null,
        DateTimeOffset? expiresAt = null, IReadOnlyList<string>? supersedes = null)
    {
        var record = new RulingArtifact
        {
            Id = id,
            Domain = domain,
            Team = team,
            AuthorityRole = "operator",
            TargetRepo = targetRepo,
            ScopeKind = scopeKind,
            ExecutionUnits = executionUnits ?? [],
            Decision = "The actual command writer recorded this ruling.",
            Rationale = "The validator checks the emitted canonical artifact.",
            EvidenceRefs = ["https://github.com/J-Tech-Japan/intent-system/issues/1887"],
            RecordedAt = recordedAt ?? Now.AddHours(-1),
            ExpiresAt = expiresAt,
            Supersedes = supersedes ?? [],
        };
        var inputDirectory = Path.Combine(root, "inputs");
        Directory.CreateDirectory(inputDirectory);
        var inputPath = Path.Combine(inputDirectory, id + ".json");
        File.WriteAllBytes(inputPath, RulingArtifact.Serialize(record));
        using var output = new StringWriter();
        var context = new CliContext
        {
            RepoRoot = root,
            Config = new CliConfig { Project = new ProjectConfig { Domain = domain, ArtifactRoot = ".intent-cli" } },
        };
        var exit = RulingCommand.ExecuteRecord(context,
            ["--id", id, "--domain", domain, "--team", team, "--from-file", inputPath, "--authority-role", "operator", "--write", "--format", "json"],
            output, null, record.RecordedAt);
        Assert.True(exit == 0, output.ToString());
        using var writerResult = JsonDocument.Parse(output.ToString());
        Assert.Equal("written", writerResult.RootElement.GetProperty("disposition").GetString());
        Assert.True(writerResult.RootElement.GetProperty("wrote").GetBoolean());
        var path = Path.Combine(root, record.RelativePath.Replace('/', Path.DirectorySeparatorChar));
        var bytes = File.ReadAllBytes(path);
        Assert.Equal(RulingArtifact.Serialize(record), bytes);
        var digest = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        return (record, bytes, digest, path);
    }

    private static (string Yaml, string Body) BuildPacket(string unit,
        IReadOnlyList<(RulingArtifact Record, byte[] Bytes, string Digest, string Path)> sources,
        string domain, string team, string targetRepo)
    {
        var references = sources.Select(source => "ruling:" + source.Record.Id).Order(StringComparer.Ordinal).ToArray();
        var yaml = $"implementation_issue_packet:\n  source_execution_unit: {unit}\n  domain: {domain}\n  team: {team}\n  target_repo: {targetRepo}\n";
        yaml += "scope_sources:\n" + string.Join("\n", references.Select(reference => $"  - \"{reference}\"")) + "\n";
        yaml += "scope_source_digests:\n" + string.Join("\n", sources.OrderBy(source => "ruling:" + source.Record.Id, StringComparer.Ordinal)
            .Select(source => $"  \"ruling:{source.Record.Id}\": \"{source.Digest}\"")) + "\n";
        var provenance = sources.OrderBy(source => source.Record.Id, StringComparer.Ordinal).Select(source =>
            new PacketScopeSources.ProvenanceEntry("ruling:" + source.Record.Id, source.Record.Domain, source.Record.Team,
                source.Record.TargetRepo, unit, source.Record.RelativePath, source.Digest, Encoding.UTF8.GetString(source.Bytes))).ToArray();
        return (yaml, PacketScopeSources.FormatProvenanceBlock(provenance));
    }

    private static void AssertNullOrMissing(JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out var value))
        {
            Assert.Equal(JsonValueKind.Null, value.ValueKind);
        }
    }

    private sealed class SourceFixture : IDisposable
    {
        private readonly string sourcePath;
        public string Root { get; }
        public RulingArtifact Record { get; }
        public byte[] Bytes { get; }
        public string Digest { get; }
        public string PacketYaml { get; }
        public string Body { get; }

        public SourceFixture()
        {
            Root = Path.Combine(OperatingSystem.IsMacOS() ? "/private/tmp" : Path.GetTempPath(), "intent-cli-g863-sources-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
            Record = new RulingArtifact
            {
                Id = "R-G863-SOURCE", Domain = "intent-cli", Team = "intent-cli-dev", AuthorityRole = "operator",
                TargetRepo = "J-Tech-Japan/intent-system", ScopeKind = "execution-units", ExecutionUnits = ["G863"],
                Decision = "Use the pinned decision.", Rationale = "The actual writer emitted these bytes.",
                EvidenceRefs = ["https://github.com/J-Tech-Japan/intent-system/issues/1887"], RecordedAt = Now,
                ExpiresAt = null, Supersedes = [],
            };
            sourcePath = Path.Combine(Root, "input.json");
            File.WriteAllBytes(sourcePath, RulingArtifact.Serialize(Record));
            using var output = new StringWriter();
            var args = new[]
            {
                "--id", Record.Id, "--domain", Record.Domain, "--team", Record.Team,
                "--from-file", sourcePath, "--authority-role", "operator", "--write", "--format", "json",
            };
            var exit = RulingCommand.ExecuteRecord(new CliContext
            {
                RepoRoot = Root,
                Config = new CliConfig { Project = new ProjectConfig { Domain = "intent-cli", ArtifactRoot = ".intent-cli" } },
            }, args, output, null, Now);
            Assert.True(exit == 0, output.ToString());
            using var writerResult = JsonDocument.Parse(output.ToString());
            Assert.Equal("written", writerResult.RootElement.GetProperty("disposition").GetString());
            Assert.True(writerResult.RootElement.GetProperty("wrote").GetBoolean());
            var artifactPath = Path.Combine(Root, ".intent-cli", "rulings", Record.Domain, Record.Team, Record.Id + ".json");
            Bytes = File.ReadAllBytes(artifactPath);
            Assert.Equal(RulingArtifact.Serialize(Record), Bytes);
            Digest = Convert.ToHexString(SHA256.HashData(Bytes)).ToLowerInvariant();
            PacketYaml = $$"""
                implementation_issue_packet:
                  source_execution_unit: G863
                  domain: intent-cli
                  team: intent-cli-dev
                  target_repo: J-Tech-Japan/intent-system
                scope_sources:
                  - "ruling:R-G863-SOURCE"
                scope_source_digests:
                  "ruling:R-G863-SOURCE": "{{Digest}}"
                """;
            var initial = PacketScopeSources.Evaluate(Root, "G863", Encoding.UTF8.GetBytes(PacketYaml), [], Now);
            Assert.Equal("scope-sources-provenance-mismatch", initial.Cause);
            Body = initial.ExpectedProvenanceBlock!;
        }

        public PacketScopeSources.Result Evaluate(string yaml, string body) =>
            PacketScopeSources.Evaluate(Root, "G863", Encoding.UTF8.GetBytes(yaml), Encoding.UTF8.GetBytes(body), Now);

        public void Dispose()
        {
            try { Directory.Delete(Root, recursive: true); } catch (IOException) { }
        }
    }
}
