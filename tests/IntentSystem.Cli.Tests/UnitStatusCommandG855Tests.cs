using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;
using YamlDotNet.RepresentationModel;

namespace IntentSystem.Cli.Tests;

[Collection("WorkerNextActionSharedState")]
public sealed class UnitStatusCommandG855Tests
{
    private const string HeadSha = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string SourceIssueUrl = "https://github.com/J-Tech-Japan/intent-system/issues/1878";
    private const string ForeignIssueUrl = "https://github.com/tomohisa/toy-calc-sample/issues/3";
    private static string LegacyForeignSourceReport => File.ReadAllText(Path.Combine(
        RepoVersionPolicySource.RepoRoot(), "tests", "IntentSystem.Cli.Tests", "Fixtures", "G859", "legacy-observed-report.yaml"));

    private static string SnapshotFiles(string root) => string.Join("\n", Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
        .OrderBy(path => path, StringComparer.Ordinal)
        .Select(path => Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/') + "=" + Convert.ToHexString(File.ReadAllBytes(path))));

    private static JsonDocument RunStatusJson(TempHost host, FixedReader reader, out int exit)
    {
        return JsonDocument.Parse(RunStatusOutput(host, reader, "json", out exit));
    }

    private static string RunStatusOutput(TempHost host, FixedReader reader, string format, out int exit)
    {
        using var writer = new StringWriter();
        exit = UnitStatusCommand.ExecuteCore(host.Context,
            ["--execution-unit", "G855", "--domain", "intent-cli", "--team", "intent-cli-dev", "--format", format], writer, reader);
        return writer.ToString();
    }

    private static Dictionary<string, string> SnapshotRenderedFacts(JsonElement report, string? excludedFactId = null) =>
        report.GetProperty("steps").EnumerateArray()
            .SelectMany(step => step.GetProperty("subchecks").EnumerateArray()
                .Where(fact => fact.GetProperty("id").GetString() != excludedFactId)
                .Select(fact => (Key: step.GetProperty("id").GetString() + "/" + fact.GetProperty("id").GetString(), Value: fact.GetRawText())))
            .ToDictionary(item => item.Key!, item => item.Value, StringComparer.Ordinal);

    private static JsonElement FindBugSource(JsonElement report) => TempHost.FindSubcheck(report, "bug-chain-or-ruling");

    private static string SetTopLevelYamlField(string yaml, string fieldName, string replacement)
    {
        var lines = yaml.Split('\n').ToList();
        var start = lines.FindIndex(line => line.StartsWith(fieldName + ":", StringComparison.Ordinal));
        var replacementLines = replacement.TrimEnd('\n').Split('\n');
        if (start < 0)
        {
            lines.AddRange(replacementLines);
            return string.Join('\n', lines);
        }

        var end = start + 1;
        while (end < lines.Count && lines[end].StartsWith("  - ", StringComparison.Ordinal)) end++;
        lines.RemoveRange(start, end - start);
        lines.InsertRange(start, replacementLines);
        return string.Join('\n', lines);
    }

    private static string RemoveTopLevelYamlField(string yaml, string fieldName) =>
        string.Join('\n', yaml.Split('\n').Where(line => !line.StartsWith(fieldName + ":", StringComparison.Ordinal)));

    private static void AssertMalformedSourceInventoryReport(TempHost host, string reportFileName, string yaml, string sourceUrl)
    {
        Assert.Throws<InvalidOperationException>(() => BugReportArtifactYaml.Deserialize(yaml));
        var reportPath = Path.Combine(host.Root, ".intent-cli", "bugs", reportFileName);
        File.WriteAllText(reportPath, yaml);
        var bytes = File.ReadAllBytes(reportPath);
        var reader = new FixedReader(new UnitStatusRemoteSnapshot
        {
            State = "completed", HeadBefore = HeadSha, HeadAfter = HeadSha, HeadSha = HeadSha, Facts = [],
        });

        using var report = RunStatusJson(host, reader, out var exit);

        Assert.Equal(1, exit);
        var source = FindBugSource(report.RootElement);
        Assert.Equal("unavailable", source.GetProperty("state").GetString());
        Assert.Equal("source-report-unreadable", source.GetProperty("cause").GetString());
        Assert.Equal("read-failure", source.GetProperty("unavailable_class").GetString());
        Assert.Contains(reportFileName, source.GetProperty("detail").GetString(), StringComparison.Ordinal);
        Assert.Contains(sourceUrl, source.GetProperty("detail").GetString(), StringComparison.Ordinal);
        var attempted = Assert.Single(source.GetProperty("evidence").EnumerateArray());
        Assert.Equal("bug-report-artifact", attempted.GetProperty("kind").GetString());
        Assert.Equal(".intent-cli/bugs/" + reportFileName, attempted.GetProperty("path").GetString());
        Assert.Equal(sourceUrl, attempted.GetProperty("url").GetString());
        Assert.Equal("attempted-canonical-source-inventory-read", attempted.GetProperty("provenance").GetString());
        Assert.Equal(bytes, File.ReadAllBytes(reportPath));
    }

    private static FixedReader CreateKnownSixProvenanceLimitsReader()
    {
        const string repo = "J-Tech-Japan/intent-system";
        const int pullRequest = 1864;
        const string pullRequestUrl = "https://github.com/J-Tech-Japan/intent-system/pull/1864";
        var approvalEvidence = new UnitStatusEvidencePointer
        {
            Kind = "pull-request-label",
            Url = pullRequestUrl,
            ExecutionUnit = "G855",
            Repo = repo,
            Pr = pullRequest,
            HeadSha = HeadSha,
            Provenance = "github-rest",
        };
        return new FixedReader(
            new UnitStatusRemoteSnapshot
            {
                State = "completed",
                HeadBefore = HeadSha,
                HeadAfter = HeadSha,
                HeadSha = HeadSha,
                Facts =
                [
                    new UnitStatusFact
                    {
                        Id = "approval-head-receipt",
                        State = UnitStatusStates.Unavailable,
                        Cause = "approval-head-receipt-not-recorded",
                        UnavailableClass = UnitStatusStates.ProvenanceLimit,
                        RepairUnavailableReason = "no-supported-historical-receipt-writer",
                        Evidence = [approvalEvidence],
                    },
                ],
            },
            new UnitStatusClaimSnapshot
            {
                State = UnitStatusStates.Unavailable,
                Cause = "local-claim-ref-unavailable",
                Detail = "local-claim-ref-unavailable: no configured local metadata branch is available; this reader does not infer or fetch a canonical claim branch.",
                UnavailableClass = UnitStatusStates.ProvenanceLimit,
            });
    }

    private static void AssertKnownSixProvenanceLimits(JsonElement report)
    {
        var expectedIds = new[]
        {
            "approval-head-receipt",
            "design-claim-acquired",
            "design-claim-release",
            "implementation-claim-acquired",
            "implementation-claim-release",
            "worker-completion-receipt",
        };
        var allFacts = report.GetProperty("steps").EnumerateArray()
            .SelectMany(step => step.GetProperty("subchecks").EnumerateArray()).ToArray();
        var actualIds = allFacts
            .Where(fact => fact.TryGetProperty("unavailable_class", out var unavailableClass)
                && unavailableClass.GetString() == UnitStatusStates.ProvenanceLimit)
            .Select(fact => fact.GetProperty("id").GetString()!)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(expectedIds.OrderBy(id => id, StringComparer.Ordinal), actualIds);
        Assert.Equal(6, report.GetProperty("summary").GetProperty("unavailable_class_counts")
            .GetProperty(UnitStatusStates.ProvenanceLimit).GetInt32());
        Assert.Equal(0, report.GetProperty("summary").GetProperty("observation_exit_code").GetInt32());

        foreach (var id in expectedIds.Where(id => id.EndsWith("claim-acquired", StringComparison.Ordinal)
            || id.EndsWith("claim-release", StringComparison.Ordinal)))
        {
            var fact = TempHost.FindSubcheck(report, id);
            Assert.Equal("unavailable", fact.GetProperty("state").GetString());
            Assert.Equal("local-claim-ref-unavailable", fact.GetProperty("cause").GetString());
            Assert.Equal("provenance-limit", fact.GetProperty("unavailable_class").GetString());
            Assert.Empty(fact.GetProperty("repair_commands").EnumerateArray());
            Assert.Equal("no-supported-repair-command-in-this-slice", fact.GetProperty("repair_unavailable_reason").GetString());
            var evidence = Assert.Single(fact.GetProperty("evidence").EnumerateArray());
            Assert.Equal("local-claim-snapshot", evidence.GetProperty("kind").GetString());
            Assert.Null(evidence.GetProperty("path").GetString());
            Assert.Null(evidence.GetProperty("record_id").GetString());
            Assert.Equal("G855", evidence.GetProperty("execution_unit").GetString());
            Assert.Equal("intent-cli-dev", evidence.GetProperty("claim_team").GetString());
            Assert.Equal("local-claim-snapshot-ref-unconfigured:scope=execution-unit:G855; ref=(unresolved); oid=(unresolved)",
                evidence.GetProperty("provenance").GetString());
        }

        var worker = TempHost.FindSubcheck(report, "worker-completion-receipt");
        Assert.Equal("unavailable", worker.GetProperty("state").GetString());
        Assert.Equal("worker-completion-receipt-not-recorded", worker.GetProperty("cause").GetString());
        Assert.Equal("provenance-limit", worker.GetProperty("unavailable_class").GetString());
        Assert.Empty(worker.GetProperty("evidence").EnumerateArray());
        Assert.Equal("no-supported-historical-worker-completion-receipt-writer", worker.GetProperty("repair_unavailable_reason").GetString());

        var approval = TempHost.FindSubcheck(report, "approval-head-receipt");
        Assert.Equal("unavailable", approval.GetProperty("state").GetString());
        Assert.Equal("approval-head-receipt-not-recorded", approval.GetProperty("cause").GetString());
        Assert.Equal("provenance-limit", approval.GetProperty("unavailable_class").GetString());
        Assert.Equal("no-supported-historical-receipt-writer", approval.GetProperty("repair_unavailable_reason").GetString());
        var approvalPointer = Assert.Single(approval.GetProperty("evidence").EnumerateArray());
        Assert.Equal("pull-request-label", approvalPointer.GetProperty("kind").GetString());
        Assert.Equal("https://github.com/J-Tech-Japan/intent-system/pull/1864", approvalPointer.GetProperty("url").GetString());
        Assert.Equal("G855", approvalPointer.GetProperty("execution_unit").GetString());
        Assert.Equal("J-Tech-Japan/intent-system", approvalPointer.GetProperty("repo").GetString());
        Assert.Equal(1864, approvalPointer.GetProperty("pr").GetInt32());
        Assert.Equal(HeadSha, approvalPointer.GetProperty("head_sha").GetString());
        Assert.Equal("github-rest", approvalPointer.GetProperty("provenance").GetString());
    }

    [Fact]
    public void RoleRecordEnumerationMoveNextFailureIsStructuredAndKeepsRowsReadBeforeFailure()
    {
        static IEnumerable<string> EnumeratePaths()
        {
            yield return "first-record.json";
            throw new IOException("role directory enumeration failed");
        }

        var result = UnitStatusCommand.ReadRoleRecordRows(EnumeratePaths, path => path);

        Assert.Equal(["first-record.json"], result.Rows);
        Assert.Contains(result.Errors, error => error.Contains("role directory enumeration failed", StringComparison.Ordinal));
    }

    [Fact]
    public void HostlessStatusReturnsStructuredUnavailableAndHelpRemainsMetadataFree()
    {
        using var writer = new StringWriter();
        var exit = UnitStatusCommand.ExecuteMetadataFree(
            ["unit", "status", "--execution-unit", "G855", "--format", "json"],
            "/tmp/child-without-host",
            writer);

        Assert.Equal(1, exit);
        using var document = JsonDocument.Parse(writer.ToString());
        Assert.Equal("host-context-unavailable", document.RootElement.GetProperty("applicability").GetProperty("cause").GetString());
        Assert.Equal("applicability-unresolved", document.RootElement.GetProperty("applicability").GetProperty("unavailable_class").GetString());
        Assert.Equal("not-observed", document.RootElement.GetProperty("observation").GetProperty("github_snapshot").GetProperty("state").GetString());

        using var help = new StringWriter();
        Assert.Equal(0, UnitStatusCommand.ExecuteMetadataFree(["unit", "status", "--help"], "/tmp/child", help));
        Assert.Contains("Read-only lifecycle evidence", help.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void InvalidFormatProducesStructuredJsonInsteadOfSilentlyUsingMarkdown()
    {
        using var host = new TempHost();
        var reader = new FixedReader();
        using var writer = new StringWriter();

        var exit = UnitStatusCommand.ExecuteCore(host.Context,
            ["--execution-unit", "G855", "--format", "xml"], writer, reader);

        Assert.Equal(1, exit);
        using var document = JsonDocument.Parse(writer.ToString());
        Assert.Equal("invalid-request", document.RootElement.GetProperty("applicability").GetProperty("cause").GetString());
        Assert.Equal(0, reader.ClaimReads);
        Assert.Equal(0, reader.GitHubReads);
    }

    [Fact]
    public void MissingFormatIsStructuredInvalidRequest()
    {
        using var host = new TempHost();
        var reader = new FixedReader();
        using var writer = new StringWriter();

        var exit = UnitStatusCommand.ExecuteCore(host.Context,
            ["--execution-unit", "G855"], writer, reader);

        Assert.Equal(1, exit);
        using var document = JsonDocument.Parse(writer.ToString());
        Assert.Equal("invalid-request", document.RootElement.GetProperty("applicability").GetProperty("unavailable_class").GetString());
        Assert.Equal(0, reader.ClaimReads);
        Assert.Equal(0, reader.GitHubReads);
    }

    [Fact]
    public void ExplicitDotSegmentScopesAreInvalidRequestsBeforeHostReads()
    {
        foreach (var scope in new[] { ".", ".." })
        {
            using var host = new TempHost();
            var reader = new FixedReader();
            using var writer = new StringWriter();

            var exit = UnitStatusCommand.ExecuteCore(host.Context,
                ["--execution-unit", "G855", "--domain", scope, "--team", "intent-cli-dev", "--format", "json"], writer, reader);

            Assert.Equal(1, exit);
            Assert.Equal(0, reader.ClaimReads);
            Assert.Equal(0, reader.GitHubReads);
            using var report = JsonDocument.Parse(writer.ToString());
            Assert.Equal("invalid-request", report.RootElement.GetProperty("applicability").GetProperty("cause").GetString());
        }
    }

    [Fact]
    public void ExactRecordedNonSoloModeReturnsNotApplicableWithoutGithubCalls()
    {
        using var host = new TempHost();
        host.WriteMode("delivery");
        host.WritePacketAndPublishArtifact();
        var reader = new FixedReader();
        using var writer = new StringWriter();

        var exit = UnitStatusCommand.ExecuteCore(host.Context,
            ["--execution-unit", "G855", "--domain", "intent-cli", "--team", "intent-cli-dev", "--format", "json"],
            writer,
            reader);

        Assert.Equal(0, exit);
        Assert.Equal(0, reader.GitHubReads);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.Equal("delivery", root.GetProperty("team_mode").GetString());
        Assert.Equal("current-recorded-entry", root.GetProperty("mode_basis").GetString());
        Assert.All(root.GetProperty("steps").EnumerateArray(), step => Assert.Equal("not-applicable", step.GetProperty("state").GetString()));
        Assert.Equal(26, root.GetProperty("summary").GetProperty("state_counts").GetProperty("not-applicable").GetInt32());
    }

    [Fact]
    public void LocallyEstablishedUnitWithoutPublishedIssueIsMissingEvidenceAndSkipsGithub()
    {
        using var host = new TempHost();
        host.WriteMode(TeamMode.SoloConductor);
        host.WritePacketAndPublishArtifact();
        var publishPath = Path.Combine(host.Root, ".intent-cli", "issues", "G855", "publish.yaml");
        var publish = IssuePublishArtifactYaml.Deserialize(File.ReadAllText(publishPath));
        File.WriteAllText(publishPath, IssuePublishArtifactYaml.Serialize(publish with
        {
            PublishStatus = "drafted",
            CreatedIssueNumber = null,
            CreatedIssueUrl = null,
            PublishedLabelName = null,
            LifecycleState = null,
            LinkedPrNumber = null,
            LinkedPrUrl = null,
        }));
        var before = SnapshotFiles(host.Root);
        var reader = new FixedReader();
        using var writer = new StringWriter();

        var exit = UnitStatusCommand.ExecuteCore(host.Context,
            ["--execution-unit", "G855", "--domain", "intent-cli", "--team", "intent-cli-dev", "--format", "json"], writer, reader);

        Assert.Equal(0, exit);
        Assert.Equal(0, reader.GitHubReads);
        Assert.Equal(before, SnapshotFiles(host.Root));
        using var report = JsonDocument.Parse(writer.ToString());
        foreach (var id in new[]
        {
            "issue-completion-marker", "posted-review", "delta-review", "observed-ci", "approved-marker",
            "approval-head-receipt", "pr-merged", "recorded-review",
        })
        {
            var fact = TempHost.FindSubcheck(report.RootElement, id);
            Assert.Equal("missing", fact.GetProperty("state").GetString());
            Assert.Equal("issue-not-published", fact.GetProperty("cause").GetString());
        }
    }

    [Fact]
    public void CanonicalPacketSourceExecutionUnitMismatchIsIdentityConflict()
    {
        using var host = new TempHost();
        host.WriteMode(TeamMode.SoloConductor);
        host.WritePacketAndPublishArtifact(packetUnit: "G999", publishUnit: "G855");
        var reader = new FixedReader();
        using var writer = new StringWriter();

        var exit = UnitStatusCommand.ExecuteCore(host.Context,
            ["--execution-unit", "G855", "--domain", "intent-cli", "--team", "intent-cli-dev", "--format", "json"], writer, reader);

        Assert.Equal(1, exit);
        Assert.Equal(0, reader.GitHubReads);
        using var report = JsonDocument.Parse(writer.ToString());
        Assert.Equal("identity-conflict", report.RootElement.GetProperty("applicability").GetProperty("cause").GetString());
        Assert.Equal("identity-conflict", report.RootElement.GetProperty("applicability").GetProperty("unavailable_class").GetString());
        Assert.All(report.RootElement.GetProperty("steps").EnumerateArray(), step =>
        {
            Assert.Equal("unavailable", step.GetProperty("state").GetString());
            Assert.All(step.GetProperty("subchecks").EnumerateArray(), fact =>
            {
                Assert.Equal("unavailable", fact.GetProperty("state").GetString());
                Assert.Equal("identity-conflict", fact.GetProperty("cause").GetString());
                Assert.Equal("identity-conflict", fact.GetProperty("unavailable_class").GetString());
            });
        });
    }

    [Fact]
    public void PacketDerivedTraversalDomainIsRejectedBeforeScopedPathResolution()
    {
        using var host = new TempHost();
        host.WritePacketAndPublishArtifact(packetDomain: "../../escape");
        var sentinelPath = Path.Combine(host.Root, "escape", "J-Tech-Japan__intent-system", "queue-state.json");
        Directory.CreateDirectory(Path.GetDirectoryName(sentinelPath)!);
        File.WriteAllText(sentinelPath, "not valid queue JSON");
        var before = SnapshotFiles(host.Root);
        var reader = new FixedReader();
        using var writer = new StringWriter();

        var exit = UnitStatusCommand.ExecuteCore(host.Context,
            ["--execution-unit", "G855", "--team", "intent-cli-dev", "--format", "json"], writer, reader);

        Assert.Equal(1, exit);
        Assert.Equal(0, reader.GitHubReads);
        Assert.Equal(before, SnapshotFiles(host.Root));
        using var report = JsonDocument.Parse(writer.ToString());
        Assert.Equal("packet-domain-invalid", report.RootElement.GetProperty("applicability").GetProperty("cause").GetString());
        Assert.Equal("identity-conflict", report.RootElement.GetProperty("applicability").GetProperty("unavailable_class").GetString());
    }

    [Fact]
    public void ClaimDerivedTraversalTeamIsRejectedBeforeScopedPathResolution()
    {
        using var host = new TempHost();
        host.WritePacketAndPublishArtifact();
        var claim = new UnitStatusClaimRecordFact
        {
            Path = ".intent-cli/claims/active.json",
            Scope = "execution-unit:G855",
            Actor = "builder",
            Team = "../../escape",
            ClaimedAt = DateTimeOffset.Parse("2026-10-07T00:00:00Z"),
        };
        var reader = new FixedReader(claims: new UnitStatusClaimSnapshot { State = "completed", ActiveClaim = claim });
        using var writer = new StringWriter();

        var exit = UnitStatusCommand.ExecuteCore(host.Context,
            ["--execution-unit", "G855", "--domain", "intent-cli", "--format", "json"], writer, reader);

        Assert.Equal(1, exit);
        Assert.Equal(0, reader.GitHubReads);
        using var report = JsonDocument.Parse(writer.ToString());
        Assert.Equal("team-scope-invalid", report.RootElement.GetProperty("applicability").GetProperty("cause").GetString());
        Assert.Equal("identity-conflict", report.RootElement.GetProperty("applicability").GetProperty("unavailable_class").GetString());
    }

    [Fact]
    public void MalformedRequiredPublishArtifactIsReadFailureEvenForRecordedDeliveryMode()
    {
        using var host = new TempHost();
        host.WriteMode("delivery");
        host.WritePacketAndPublishArtifact();
        File.WriteAllText(Path.Combine(host.Context.RepoRoot, ".intent-cli", "issues", "G855", "publish.yaml"), "not a publish artifact\n");
        var reader = new FixedReader();
        using var writer = new StringWriter();

        var exit = UnitStatusCommand.ExecuteCore(host.Context,
            ["--execution-unit", "G855", "--domain", "intent-cli", "--team", "intent-cli-dev", "--format", "json"], writer, reader);

        Assert.Equal(1, exit);
        Assert.Equal(0, reader.GitHubReads);
        using var report = JsonDocument.Parse(writer.ToString());
        Assert.Equal("publication-artifact-unreadable", report.RootElement.GetProperty("applicability").GetProperty("cause").GetString());
        Assert.Equal("read-failure", report.RootElement.GetProperty("applicability").GetProperty("unavailable_class").GetString());
        Assert.Contains(TempHost.FindSubcheck(report.RootElement, "publication-artifact").GetProperty("evidence").EnumerateArray(),
            evidence => evidence.GetProperty("path").GetString() == ".intent-cli/issues/G855/publish.yaml");
    }

    [Fact]
    public void MalformedRequiredPacketIsReadFailureEvenForRecordedDeliveryMode()
    {
        using var host = new TempHost();
        host.WriteMode("delivery");
        host.WritePacketAndPublishArtifact();
        File.WriteAllText(Path.Combine(host.Context.RepoRoot, ".intent-cli", "issues", "G855", "packet.yaml"), "not a packet artifact\n");
        var reader = new FixedReader();
        using var writer = new StringWriter();

        var exit = UnitStatusCommand.ExecuteCore(host.Context,
            ["--execution-unit", "G855", "--domain", "intent-cli", "--team", "intent-cli-dev", "--format", "json"], writer, reader);

        Assert.Equal(1, exit);
        Assert.Equal(0, reader.GitHubReads);
        using var report = JsonDocument.Parse(writer.ToString());
        Assert.Equal("packet-identity-unreadable", report.RootElement.GetProperty("applicability").GetProperty("cause").GetString());
        Assert.Equal("read-failure", report.RootElement.GetProperty("applicability").GetProperty("unavailable_class").GetString());
        Assert.Contains(TempHost.FindSubcheck(report.RootElement, "packet-current-validation").GetProperty("evidence").EnumerateArray(),
            evidence => evidence.GetProperty("path").GetString() == ".intent-cli/issues/G855/packet.yaml");
    }

    [Fact]
    public void MissingExactModeEntryIsUnavailableAndNeverUsesFallbackOrGithub()
    {
        using var host = new TempHost();
        host.WriteMode("delivery", domain: "other-domain", team: "other-team");
        var reader = new FixedReader();
        using var writer = new StringWriter();

        var exit = UnitStatusCommand.ExecuteCore(host.Context,
            ["--execution-unit", "G855", "--domain", "intent-cli", "--team", "intent-cli-dev", "--format", "json"],
            writer,
            reader);

        Assert.Equal(1, exit);
        Assert.Equal(0, reader.GitHubReads);
        using var document = JsonDocument.Parse(writer.ToString());
        Assert.Equal("team-mode-unrecorded", document.RootElement.GetProperty("applicability").GetProperty("cause").GetString());
        Assert.Equal("applicability-unresolved", document.RootElement.GetProperty("applicability").GetProperty("unavailable_class").GetString());
    }

    [Fact]
    public void ExplicitDomainWithoutResolvableTeamIsUnavailableAndNeverUsesGithub()
    {
        using var host = new TempHost();
        host.WriteMode("delivery");
        host.WritePacketAndPublishArtifact();
        var reader = new FixedReader();
        using var writer = new StringWriter();

        var exit = UnitStatusCommand.ExecuteCore(host.Context,
            ["--execution-unit", "G855", "--domain", "intent-cli", "--format", "json"], writer, reader);

        Assert.Equal(1, exit);
        Assert.Equal(0, reader.GitHubReads);
        using var report = JsonDocument.Parse(writer.ToString());
        Assert.Equal("team-unresolved", report.RootElement.GetProperty("applicability").GetProperty("cause").GetString());
        Assert.Equal("applicability-unresolved", report.RootElement.GetProperty("applicability").GetProperty("unavailable_class").GetString());
    }

    [Fact]
    public void ExplicitTeamConflictingWithRecordedClaimIsIdentityConflict()
    {
        using var host = new TempHost();
        host.WritePacketAndPublishArtifact();
        var claims = new UnitStatusClaimSnapshot
        {
            State = "completed",
            ActiveClaim = new UnitStatusClaimRecordFact
            {
                Path = ClaimCommand.ClaimPath("execution-unit:G855"),
                Scope = "execution-unit:G855",
                Actor = "builder",
                Team = "team-a",
                ClaimedAt = DateTimeOffset.Parse("2026-10-01T00:00:00Z"),
            },
        };
        var reader = new FixedReader(claims: claims);
        using var writer = new StringWriter();

        var exit = UnitStatusCommand.ExecuteCore(host.Context,
            ["--execution-unit", "G855", "--domain", "intent-cli", "--team", "team-b", "--format", "json"], writer, reader);

        Assert.Equal(1, exit);
        Assert.Equal(0, reader.GitHubReads);
        using var report = JsonDocument.Parse(writer.ToString());
        Assert.Equal("identity-conflict", report.RootElement.GetProperty("applicability").GetProperty("cause").GetString());
        Assert.Equal("identity-conflict", report.RootElement.GetProperty("applicability").GetProperty("unavailable_class").GetString());
    }

    [Fact]
    public void PacketAndPublishExecutionUnitMismatchIsIdentityConflict()
    {
        using var host = new TempHost();
        host.WriteMode("delivery");
        host.WritePacketAndPublishArtifact(packetUnit: "G854", publishUnit: "G855");
        var reader = new FixedReader();
        using var writer = new StringWriter();

        var exit = UnitStatusCommand.ExecuteCore(host.Context,
            ["--execution-unit", "G855", "--domain", "intent-cli", "--team", "intent-cli-dev", "--format", "json"], writer, reader);

        Assert.Equal(1, exit);
        Assert.Equal(0, reader.GitHubReads);
        using var report = JsonDocument.Parse(writer.ToString());
        Assert.Equal("identity-conflict", report.RootElement.GetProperty("applicability").GetProperty("cause").GetString());
        Assert.Equal("identity-conflict", report.RootElement.GetProperty("applicability").GetProperty("unavailable_class").GetString());
    }

    [Fact]
    public void UnreadableCurrentPacketFileIsUnavailableRatherThanReportedMissing()
    {
        using var host = new TempHost();
        host.WriteMode(TeamMode.SoloConductor);
        host.WritePacketAndPublishArtifact();
        Directory.CreateDirectory(Path.Combine(host.Context.RepoRoot, ".intent-cli", "issues", "G855", "implementation.md"));
        using var writer = new StringWriter();
        var reader = new FixedReader(new UnitStatusRemoteSnapshot
        {
            State = "completed",
            HeadBefore = HeadSha,
            HeadAfter = HeadSha,
            HeadSha = HeadSha,
            Facts = [],
        });

        var exit = UnitStatusCommand.ExecuteCore(host.Context,
            ["--execution-unit", "G855", "--domain", "intent-cli", "--team", "intent-cli-dev", "--format", "json"], writer, reader);

        Assert.Equal(1, exit);
        using var document = JsonDocument.Parse(writer.ToString());
        var fileFact = document.RootElement.GetProperty("steps").EnumerateArray()
            .SelectMany(step => step.GetProperty("subchecks").EnumerateArray())
            .Single(fact => fact.GetProperty("id").GetString() == "packet-current-files");
        Assert.Equal("unavailable", fileFact.GetProperty("state").GetString());
        Assert.Equal("read-failure", fileFact.GetProperty("unavailable_class").GetString());
        Assert.Contains(fileFact.GetProperty("evidence").EnumerateArray(), evidence => evidence.GetProperty("path").GetString() == ".intent-cli/issues/G855/packet.yaml");
    }

    [Theory]
    [InlineData("architect", "design-claim-release")]
    [InlineData("builder", "implementation-claim-release")]
    public void EarlierReleaseCannotSatisfyAReacquiredActiveClaimEpoch(string actor, string releaseFactId)
    {
        using var host = new TempHost();
        host.WriteMode(TeamMode.SoloConductor);
        host.WritePacketAndPublishArtifact();
        var claimedAtA = DateTimeOffset.Parse("2026-10-01T00:00:00Z");
        var claimedAtB = DateTimeOffset.Parse("2026-10-03T00:00:00Z");
        var oldRelease = TempHost.History("release", actor, actor, claimedAtA,
            DateTimeOffset.Parse("2026-10-02T00:00:00Z"));
        var active = TempHost.Claim(actor, claimedAtB);
        var reader = TempHost.CreateRealAdapter(host, active, [oldRelease]);
        using var writer = new StringWriter();

        var exit = UnitStatusCommand.ExecuteCore(host.Context,
            ["--execution-unit", "G855", "--domain", "intent-cli", "--team", "intent-cli-dev", "--format", "json"], writer, reader);

        Assert.Equal(0, exit);
        using var report = JsonDocument.Parse(writer.ToString());
        var release = TempHost.FindSubcheck(report.RootElement, releaseFactId);
        Assert.Equal("missing", release.GetProperty("state").GetString());
        Assert.Equal("claim-release-superseded-by-new-epoch", release.GetProperty("cause").GetString());
        var oldEvidence = Assert.Single(release.GetProperty("evidence").EnumerateArray());
        Assert.Equal(claimedAtA, oldEvidence.GetProperty("claim_epoch_claimed_at").GetDateTimeOffset());
        Assert.Equal("release", oldEvidence.GetProperty("claim_operation").GetString());
        Assert.Equal(actor == "architect" ? "design-handoff" : "implementation-release",
            oldEvidence.GetProperty("claim_disposition").GetString());
        Assert.Contains("0000-release.json", oldEvidence.GetProperty("path").GetString()!, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("architect", "design-claim-acquired", "design-claim-release")]
    [InlineData("builder", "implementation-claim-acquired", "implementation-claim-release")]
    public void ActiveClaimWithoutAnyReleaseHistoryReportsReleaseNotObserved(string actor, string acquiredFactId, string releaseFactId)
    {
        using var host = new TempHost();
        host.WriteMode(TeamMode.SoloConductor);
        host.WritePacketAndPublishArtifact();
        var active = TempHost.Claim(actor, DateTimeOffset.Parse("2026-10-03T00:00:00Z"));
        var reader = TempHost.CreateRealAdapter(host, active, []);
        using var writer = new StringWriter();

        var exit = UnitStatusCommand.ExecuteCore(host.Context,
            ["--execution-unit", "G855", "--domain", "intent-cli", "--team", "intent-cli-dev", "--format", "json"], writer, reader);

        Assert.Equal(0, exit);
        using var report = JsonDocument.Parse(writer.ToString());
        Assert.Equal("done", TempHost.FindSubcheck(report.RootElement, acquiredFactId).GetProperty("state").GetString());
        var release = TempHost.FindSubcheck(report.RootElement, releaseFactId);
        Assert.Equal("missing", release.GetProperty("state").GetString());
        Assert.Equal("claim-release-not-observed", release.GetProperty("cause").GetString());
        Assert.Contains("no release record", release.GetProperty("detail").GetString(), StringComparison.OrdinalIgnoreCase);
        var evidence = Assert.Single(release.GetProperty("evidence").EnumerateArray());
        Assert.Contains("configured-local-claim-snapshot:scope=execution-unit:G855", evidence.GetProperty("provenance").GetString());
        Assert.Contains("ref=refs/remotes/origin/metadata", evidence.GetProperty("provenance").GetString());
        Assert.Contains("oid=bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb", evidence.GetProperty("provenance").GetString());
    }

    [Fact]
    public void ActualTakeoverTimestampMayMatchItsIncomingActiveClaimEpoch()
    {
        using var host = new TempHost();
        host.WriteMode(TeamMode.SoloConductor);
        host.WritePacketAndPublishArtifact();
        var newEpoch = DateTimeOffset.Parse("2026-10-03T00:00:00Z");
        var takeover = TempHost.History("takeover", "builder", "architect",
            DateTimeOffset.Parse("2026-10-01T00:00:00Z"), newEpoch);
        var active = TempHost.Claim("builder", newEpoch);
        var reader = TempHost.CreateRealAdapter(host, active, [takeover]);
        using var writer = new StringWriter();

        Assert.Equal(0, UnitStatusCommand.ExecuteCore(host.Context,
            ["--execution-unit", "G855", "--domain", "intent-cli", "--team", "intent-cli-dev", "--format", "json"], writer, reader));

        using var report = JsonDocument.Parse(writer.ToString());
        Assert.Equal("done", TempHost.FindSubcheck(report.RootElement, "implementation-claim-acquired").GetProperty("state").GetString());
        var release = TempHost.FindSubcheck(report.RootElement, "implementation-claim-release");
        Assert.Equal("missing", release.GetProperty("state").GetString());
        Assert.Equal("claim-release-not-observed", release.GetProperty("cause").GetString());
        Assert.Contains("no release record", release.GetProperty("detail").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TakeoverAcquisitionAndLaterMatchingReleaseShareOneClaimEpoch()
    {
        using var host = new TempHost();
        host.WriteMode(TeamMode.SoloConductor);
        host.WritePacketAndPublishArtifact();
        var oldEpoch = DateTimeOffset.Parse("2026-10-01T00:00:00Z");
        var newEpoch = DateTimeOffset.Parse("2026-10-03T00:00:00Z");
        var history = new[]
        {
            TempHost.History("takeover", "builder", "architect", oldEpoch, newEpoch),
            TempHost.History("release", "builder", "builder", newEpoch, DateTimeOffset.Parse("2026-10-04T00:00:00Z")),
        };
        var reader = TempHost.CreateRealAdapter(host, null, history);
        using var writer = new StringWriter();

        Assert.Equal(0, UnitStatusCommand.ExecuteCore(host.Context,
            ["--execution-unit", "G855", "--domain", "intent-cli", "--team", "intent-cli-dev", "--format", "json"], writer, reader));

        using var report = JsonDocument.Parse(writer.ToString());
        var release = TempHost.FindSubcheck(report.RootElement, "implementation-claim-release");
        Assert.Equal("done", release.GetProperty("state").GetString());
        Assert.Contains(release.GetProperty("evidence").EnumerateArray(), evidence =>
            evidence.GetProperty("claim_epoch_claimed_at").GetDateTimeOffset() == newEpoch);
    }

    [Theory]
    [InlineData("architect", "design-claim-release")]
    [InlineData("builder", "implementation-claim-release")]
    public void LatestMatchingReleaseSatisfiesOnlyItsOwnClaimEpoch(string actor, string releaseFactId)
    {
        using var host = new TempHost();
        host.WriteMode(TeamMode.SoloConductor);
        host.WritePacketAndPublishArtifact();
        var claimedAtA = DateTimeOffset.Parse("2026-10-01T00:00:00Z");
        var claimedAtB = DateTimeOffset.Parse("2026-10-03T00:00:00Z");
        var history = new[]
        {
            TempHost.History("release", actor, actor, claimedAtA, DateTimeOffset.Parse("2026-10-02T00:00:00Z")),
            TempHost.History("release", actor, actor, claimedAtB, DateTimeOffset.Parse("2026-10-04T00:00:00Z")),
        };
        var reader = TempHost.CreateRealAdapter(host, null, history);
        using var writer = new StringWriter();

        var exit = UnitStatusCommand.ExecuteCore(host.Context,
            ["--execution-unit", "G855", "--domain", "intent-cli", "--team", "intent-cli-dev", "--format", "json"], writer, reader);

        Assert.Equal(0, exit);
        using var report = JsonDocument.Parse(writer.ToString());
        var release = TempHost.FindSubcheck(report.RootElement, releaseFactId);
        Assert.Equal("done", release.GetProperty("state").GetString());
        var evidence = Assert.Single(release.GetProperty("evidence").EnumerateArray());
        Assert.Equal(claimedAtB, evidence.GetProperty("claim_epoch_claimed_at").GetDateTimeOffset());
        Assert.Equal("release", evidence.GetProperty("claim_operation").GetString());
        Assert.Contains("0001-release.json", evidence.GetProperty("path").GetString()!, StringComparison.Ordinal);

        using var markdown = new StringWriter();
        var markdownExit = UnitStatusCommand.ExecuteCore(host.Context,
            ["--execution-unit", "G855", "--domain", "intent-cli", "--team", "intent-cli-dev", "--format", "markdown"], markdown, reader);
        Assert.Equal(0, markdownExit);
        Assert.Contains($"claim_epoch_claimed_at=`{claimedAtB:O}`", markdown.ToString(), StringComparison.Ordinal);
        Assert.Contains("claim_operation=`release`", markdown.ToString(), StringComparison.Ordinal);
        Assert.Contains(actor == "architect" ? "claim_disposition=`design-handoff`" : "claim_disposition=`implementation-release`",
            markdown.ToString(), StringComparison.Ordinal);
        Assert.Contains($"claim_actor=`{actor}`", markdown.ToString(), StringComparison.Ordinal);
        Assert.Contains("claim_team=`intent-cli-dev`", markdown.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void ExplicitSourceIssueUsesOneCanonicalReportTriageAndPlanChain()
    {
        using var host = new TempHost();
        host.WriteMode(TeamMode.SoloConductor);
        const string sourceUrl = "https://github.com/J-Tech-Japan/intent-system/issues/1861";
        host.WritePacketAndPublishArtifact(sourceArtifact: sourceUrl);
        host.WriteBugSourceChain("BUG-1861", sourceUrl, resolvedPacketRefs: [".intent-cli/issues/G855/packet.yaml"]);
        var childDirectory = Directory.CreateDirectory(Path.Combine(host.Context.RepoRoot, "subdirectory"));
        using var currentDirectory = new CurrentDirectoryScope(childDirectory.FullName);
        var reader = new FixedReader(new UnitStatusRemoteSnapshot
        {
            State = "completed", HeadBefore = HeadSha, HeadAfter = HeadSha, HeadSha = HeadSha, Facts = [],
        });
        using var writer = new StringWriter();

        var exit = UnitStatusCommand.ExecuteCore(host.Context,
            ["--execution-unit", "G855", "--domain", "intent-cli", "--team", "intent-cli-dev", "--format", "json"], writer, reader);

        Assert.Equal(0, exit);
        using var report = JsonDocument.Parse(writer.ToString());
        var source = TempHost.FindSubcheck(report.RootElement, "bug-chain-or-ruling");
        Assert.Equal("done", source.GetProperty("state").GetString());
        Assert.Equal("canonical-source-chain-recorded", source.GetProperty("cause").GetString());
        Assert.Equal(4, source.GetProperty("evidence").GetArrayLength());
        Assert.Contains(source.GetProperty("evidence").EnumerateArray(), item => item.GetProperty("path").GetString() == ".intent-cli/bugs/BUG-1861.report.yaml");
        Assert.Contains(source.GetProperty("evidence").EnumerateArray(), item => item.GetProperty("path").GetString() == ".intent-cli/bugs/BUG-1861.triage.yaml");
        Assert.Contains(source.GetProperty("evidence").EnumerateArray(), item => item.GetProperty("path").GetString() == ".intent-cli/bugs/BUG-1861.plan.yaml");
    }

    [Theory]
    [InlineData("BUG-20260416-legacy.report.yaml")]
    [InlineData("ZZZ-legacy.report.yaml")]
    public void WriterEmittedTargetChainSkipsOnlyPositivelyForeignLegacySourceReport(string legacyFileName)
    {
        using var host = new TempHost();
        host.WriteMode(TeamMode.SoloConductor);
        host.WritePacketAndPublishArtifact(sourceArtifact: SourceIssueUrl);
        host.WriteActualBugSourceChain("BUG-G859-TARGET", SourceIssueUrl);
        var foreignPath = Path.Combine(host.Context.RepoRoot, ".intent-cli", "bugs", legacyFileName);
        File.WriteAllText(foreignPath, LegacyForeignSourceReport);
        var foreignBytes = File.ReadAllBytes(foreignPath);
        var beforeStatus = SnapshotFiles(host.Root);

        var reader = new FixedReader(new UnitStatusRemoteSnapshot
        {
            State = "completed", HeadBefore = HeadSha, HeadAfter = HeadSha, HeadSha = HeadSha, Facts = [],
        });
        using var writer = new StringWriter();

        var exit = UnitStatusCommand.ExecuteCore(host.Context,
            ["--execution-unit", "G855", "--domain", "intent-cli", "--team", "intent-cli-dev", "--format", "json"], writer, reader);

        Assert.Equal(0, exit);
        using var report = JsonDocument.Parse(writer.ToString());
        var source = TempHost.FindSubcheck(report.RootElement, "bug-chain-or-ruling");
        Assert.Equal("done", source.GetProperty("state").GetString());
        Assert.Equal("canonical-source-chain-recorded", source.GetProperty("cause").GetString());
        var evidence = source.GetProperty("evidence").EnumerateArray().ToArray();
        Assert.Equal(4, evidence.Length);
        Assert.Equal(new[]
        {
            ".intent-cli/bugs/BUG-G859-TARGET.report.yaml",
            ".intent-cli/bugs/BUG-G859-TARGET.triage.yaml",
            ".intent-cli/bugs/BUG-G859-TARGET.plan.yaml",
            ".intent-cli/issues/G855/packet.yaml",
        }, evidence.Select(item => item.GetProperty("path").GetString()).ToArray());
        Assert.DoesNotContain(evidence, item => item.GetProperty("path").GetString() == ".intent-cli/bugs/" + legacyFileName);
        Assert.Equal(foreignBytes, File.ReadAllBytes(foreignPath));
        Assert.Equal(beforeStatus, SnapshotFiles(host.Root));
        Assert.Equal(1, reader.ClaimReads);
        Assert.Equal(1, reader.GitHubReads);
    }

    [Fact]
    public void ForeignLegacyAdditionPreservesOtherFactSnapshotsAndJsonMarkdownSourceEvidence()
    {
        using var host = new TempHost();
        host.WriteMode(TeamMode.SoloConductor);
        host.WritePacketAndPublishArtifact(sourceArtifact: SourceIssueUrl);
        host.WriteActualBugSourceChain("BUG-G859-TARGET", SourceIssueUrl);
        var reader = CreateKnownSixProvenanceLimitsReader();

        using var before = RunStatusJson(host, reader, out var beforeExit);
        Assert.Equal(0, beforeExit);
        AssertKnownSixProvenanceLimits(before.RootElement);
        var legacyPath = Path.Combine(host.Context.RepoRoot, ".intent-cli", "bugs", "AAA-legacy.report.yaml");
        File.WriteAllText(legacyPath, LegacyForeignSourceReport);
        var legacyBytes = File.ReadAllBytes(legacyPath);
        using var after = RunStatusJson(host, reader, out var afterExit);
        Assert.Equal(0, afterExit);
        AssertKnownSixProvenanceLimits(after.RootElement);
        var markdown = RunStatusOutput(host, reader, "markdown", out var markdownExit);
        Assert.Equal(0, markdownExit);

        var beforeSource = FindBugSource(before.RootElement);
        var afterSource = FindBugSource(after.RootElement);
        Assert.Equal(beforeSource.GetRawText(), afterSource.GetRawText());
        Assert.Equal("done", afterSource.GetProperty("state").GetString());
        Assert.Equal("canonical-source-chain-recorded", afterSource.GetProperty("cause").GetString());
        Assert.Equal(SnapshotRenderedFacts(before.RootElement, "bug-chain-or-ruling"),
            SnapshotRenderedFacts(after.RootElement, "bug-chain-or-ruling"));
        Assert.Equal(before.RootElement.GetProperty("summary").GetRawText(), after.RootElement.GetProperty("summary").GetRawText());
        Assert.Equal(4, FindBugSource(after.RootElement).GetProperty("evidence").GetArrayLength());
        Assert.Equal(legacyBytes, File.ReadAllBytes(legacyPath));

        var markdownSource = string.Join("\n", markdown.Split('\n')
            .SkipWhile(line => !line.StartsWith("### bug-chain-or-ruling:", StringComparison.Ordinal))
            .TakeWhile((line, index) => index == 0 || !line.StartsWith("### ", StringComparison.Ordinal)));
        Assert.Contains("### bug-chain-or-ruling: done", markdownSource, StringComparison.Ordinal);
        Assert.Contains("- cause: `canonical-source-chain-recorded`", markdownSource, StringComparison.Ordinal);
        foreach (var pointer in afterSource.GetProperty("evidence").EnumerateArray())
        {
            var path = pointer.GetProperty("path").GetString();
            var kind = pointer.GetProperty("kind").GetString();
            Assert.Contains($"kind={kind}; path=`{path}`;", markdownSource, StringComparison.Ordinal);
            Assert.Contains("provenance=" + pointer.GetProperty("provenance").GetString(), markdownSource, StringComparison.Ordinal);
        }
        Assert.Equal(3, reader.ClaimReads);
        Assert.Equal(3, reader.GitHubReads);
    }

    [Fact]
    public void RecordedNonSoloOptionalSourceShortCircuitIsUnchangedByForeignAndMalformedReports()
    {
        using var host = new TempHost();
        host.WriteMode("delivery");
        host.WritePacketAndPublishArtifact(sourceArtifact: SourceIssueUrl);
        host.WriteActualBugSourceChain("BUG-G859-TARGET", SourceIssueUrl);
        var reader = new FixedReader();

        using var before = RunStatusJson(host, reader, out var beforeExit);
        Assert.Equal(0, beforeExit);
        var bugs = Path.Combine(host.Root, ".intent-cli", "bugs");
        File.WriteAllText(Path.Combine(bugs, "AAA-foreign.report.yaml"), LegacyForeignSourceReport);
        File.WriteAllText(Path.Combine(bugs, "ZZZ-unscopable.report.yaml"), "bug_id: unrelated\n");
        using var after = RunStatusJson(host, reader, out var afterExit);
        Assert.Equal(0, afterExit);

        Assert.Equal("not-applicable", FindBugSource(before.RootElement).GetProperty("state").GetString());
        Assert.Equal("recorded-non-solo-mode", FindBugSource(after.RootElement).GetProperty("cause").GetString());
        Assert.All(after.RootElement.GetProperty("steps").EnumerateArray(), step =>
        {
            Assert.Equal("not-applicable", step.GetProperty("state").GetString());
            Assert.All(step.GetProperty("subchecks").EnumerateArray(), fact => Assert.Equal("not-applicable", fact.GetProperty("state").GetString()));
        });
        Assert.Equal(SnapshotRenderedFacts(before.RootElement), SnapshotRenderedFacts(after.RootElement));
        Assert.Equal(2, reader.ClaimReads);
        Assert.Equal(0, reader.GitHubReads);
    }

    [Fact]
    public void SourceInventoryBranchesPreserveHostAndGitMetadataSnapshotsAndReaderBudgets()
    {
        using var host = new TempHost();
        host.WriteMode(TeamMode.SoloConductor);
        host.WritePacketAndPublishArtifact(sourceArtifact: SourceIssueUrl);
        host.WriteActualBugSourceChain("BUG-G859-TARGET", SourceIssueUrl);
        var gitRefs = Path.Combine(host.Root, ".git", "refs", "heads");
        Directory.CreateDirectory(gitRefs);
        File.WriteAllText(Path.Combine(host.Root, ".git", "index"), "fixture-index\n");
        File.WriteAllText(Path.Combine(gitRefs, "main"), HeadSha + "\n");
        var foreignPath = Path.Combine(host.Root, ".intent-cli", "bugs", "AAA-foreign.report.yaml");
        File.WriteAllText(foreignPath, LegacyForeignSourceReport);
        var reader = new FixedReader(new UnitStatusRemoteSnapshot
        {
            State = "completed", HeadBefore = HeadSha, HeadAfter = HeadSha, HeadSha = HeadSha, Facts = [],
        });

        var beforeDone = SnapshotFiles(host.Root);
        using (var done = RunStatusJson(host, reader, out var exit))
        {
            Assert.Equal(0, exit);
            Assert.Equal("done", FindBugSource(done.RootElement).GetProperty("state").GetString());
        }
        Assert.Equal(beforeDone, SnapshotFiles(host.Root));

        var targetPlan = Path.Combine(host.Root, ".intent-cli", "bugs", "BUG-G859-TARGET.plan.yaml");
        var originalTargetPlan = File.ReadAllBytes(targetPlan);
        File.WriteAllText(targetPlan, "not a canonical plan\n");
        var beforeRelevantFailure = SnapshotFiles(host.Root);
        using (var relevantFailure = RunStatusJson(host, reader, out var exit))
        {
            Assert.Equal(1, exit);
            Assert.Equal("unavailable", FindBugSource(relevantFailure.RootElement).GetProperty("state").GetString());
            Assert.Equal("source-triage-plan-unreadable", FindBugSource(relevantFailure.RootElement).GetProperty("cause").GetString());
        }
        Assert.Equal(beforeRelevantFailure, SnapshotFiles(host.Root));

        File.WriteAllBytes(targetPlan, originalTargetPlan);
        var unscopablePath = Path.Combine(host.Root, ".intent-cli", "bugs", "ZZZ-unscopable.report.yaml");
        File.WriteAllText(unscopablePath, "bug_id: unrelated\n");
        var beforeUnscopable = SnapshotFiles(host.Root);
        using (var unscopable = RunStatusJson(host, reader, out var exit))
        {
            Assert.Equal(1, exit);
            var source = FindBugSource(unscopable.RootElement);
            Assert.Equal("unavailable", source.GetProperty("state").GetString());
            Assert.Equal("source-report-unreadable", source.GetProperty("cause").GetString());
            Assert.Contains(source.GetProperty("evidence").EnumerateArray(), item =>
                item.GetProperty("path").GetString() == ".intent-cli/bugs/ZZZ-unscopable.report.yaml");
        }
        Assert.Equal(beforeUnscopable, SnapshotFiles(host.Root));
        Assert.Equal(3, reader.ClaimReads);
        Assert.Equal(3, reader.GitHubReads);
        Assert.Equal("fixture-index\n", File.ReadAllText(Path.Combine(host.Root, ".git", "index")));
        Assert.Equal(HeadSha + "\n", File.ReadAllText(Path.Combine(gitRefs, "main")));
    }

    [Theory]
    [InlineData("domain_slug")]
    [InlineData("title")]
    public void WriterEmittedCanonicalForeignReportCanBeSkippedOnlyAfterItsRoutingIsProven(string missingField)
    {
        using var host = new TempHost();
        host.WriteMode(TeamMode.SoloConductor);
        host.WritePacketAndPublishArtifact(sourceArtifact: SourceIssueUrl);
        host.WriteActualBugSourceChain("BUG-G859-TARGET", SourceIssueUrl);
        const string foreignUrl = "https://github.com/elsewhere/intent-system/issues/1878";
        var foreignPath = host.WriteActualBugReport("AAA-G859-FOREIGN", "Foreign report", [foreignUrl], ["G859-OTHER"]);
        var beforeMutation = File.ReadAllText(foreignPath);
        var mutated = string.Join("\n", beforeMutation.Split('\n')
            .Where(line => !line.StartsWith(missingField + ":", StringComparison.Ordinal)));
        Assert.NotEqual(beforeMutation, mutated);
        Assert.Contains(foreignUrl, mutated, StringComparison.Ordinal);
        Assert.Contains("G859-OTHER", mutated, StringComparison.Ordinal);
        Assert.Throws<InvalidOperationException>(() => BugReportArtifactYaml.Deserialize(mutated));
        File.WriteAllText(foreignPath, mutated);
        var mutatedBytes = File.ReadAllBytes(foreignPath);

        var reader = new FixedReader(new UnitStatusRemoteSnapshot
        {
            State = "completed", HeadBefore = HeadSha, HeadAfter = HeadSha, HeadSha = HeadSha, Facts = [],
        });
        using var report = RunStatusJson(host, reader, out var exit);

        Assert.Equal(0, exit);
        var source = TempHost.FindSubcheck(report.RootElement, "bug-chain-or-ruling");
        Assert.Equal("done", source.GetProperty("state").GetString());
        Assert.Equal("canonical-source-chain-recorded", source.GetProperty("cause").GetString());
        Assert.DoesNotContain(source.GetProperty("evidence").EnumerateArray(), item => item.GetProperty("path").GetString() == Path.GetRelativePath(host.Root, foreignPath).Replace('\\', '/'));
        Assert.Equal(mutatedBytes, File.ReadAllBytes(foreignPath));
    }

    [Fact]
    public void TargetClaimsInEitherRoutingFormDominateForeignClaims()
    {
        using (var host = new TempHost())
        {
            host.WriteMode(TeamMode.SoloConductor);
            host.WritePacketAndPublishArtifact(sourceArtifact: SourceIssueUrl);
            host.WriteActualBugSourceChain("BUG-G859-TARGET", SourceIssueUrl);
            var targetPath = Path.Combine(host.Root, ".intent-cli", "bugs", "BUG-G859-TARGET.report.yaml");
            File.AppendAllText(targetPath,
                "observed_in:\n  linked_issue: \"https://github.com/tomohisa/toy-calc-sample/issues/3\"\n  execution_unit: OTHER-UNIT\n");
            Assert.NotNull(BugReportArtifactYaml.Deserialize(File.ReadAllText(targetPath)));
            var reader = new FixedReader(new UnitStatusRemoteSnapshot
            {
                State = "completed", HeadBefore = HeadSha, HeadAfter = HeadSha, HeadSha = HeadSha, Facts = [],
            });

            using var report = RunStatusJson(host, reader, out var exit);
            Assert.Equal(0, exit);
            Assert.Equal("done", TempHost.FindSubcheck(report.RootElement, "bug-chain-or-ruling").GetProperty("state").GetString());
        }

        using (var host = new TempHost())
        {
            host.WriteMode(TeamMode.SoloConductor);
            host.WritePacketAndPublishArtifact(sourceArtifact: SourceIssueUrl);
            host.WriteActualBugSourceChain("BUG-G859-TARGET", SourceIssueUrl);
            const string sameNumberForeignUrl = "https://github.com/elsewhere/intent-system/issues/1878";
            var foreignPath = host.WriteActualBugReport("AAA-G859-FOREIGN", "Foreign same-number report", [sameNumberForeignUrl], ["G855"]);
            File.AppendAllText(foreignPath, $"observed_in:\n  linked_issue: \"{SourceIssueUrl}\"\n  execution_unit: G855\n");
            Assert.NotNull(BugReportArtifactYaml.Deserialize(File.ReadAllText(foreignPath)));
            var reader = new FixedReader(new UnitStatusRemoteSnapshot
            {
                State = "completed", HeadBefore = HeadSha, HeadAfter = HeadSha, HeadSha = HeadSha, Facts = [],
            });

            using var report = RunStatusJson(host, reader, out var exit);
            Assert.Equal(1, exit);
            var source = TempHost.FindSubcheck(report.RootElement, "bug-chain-or-ruling");
            Assert.Equal("unavailable", source.GetProperty("state").GetString());
            Assert.Equal("source-chain-identity-conflict", source.GetProperty("cause").GetString());
            Assert.Equal("identity-conflict", source.GetProperty("unavailable_class").GetString());
        }
    }

    [Fact]
    public void LegacyTargetUrlVetoesCanonicalForeignIssueAndUnitWhenReportSchemaIsInvalid()
    {
        using var host = new TempHost();
        host.WriteMode(TeamMode.SoloConductor);
        host.WritePacketAndPublishArtifact(sourceArtifact: SourceIssueUrl);
        host.WriteActualBugSourceChain("BUG-G859-TARGET", SourceIssueUrl);
        const string canonicalForeignUrl = "https://github.com/elsewhere/intent-system/issues/1878";
        const string reportFileName = "AAA-G859-LEGACY-TARGET.report.yaml";
        var reportPath = host.WriteActualBugReport("AAA-G859-LEGACY-TARGET", "Foreign canonical report", [canonicalForeignUrl], ["OTHER-UNIT"]);
        var emitted = File.ReadAllText(reportPath);
        var canonical = BugReportArtifactYaml.Deserialize(emitted);
        Assert.Equal([canonicalForeignUrl], canonical.LinkedIssueRefs);
        Assert.Equal(["OTHER-UNIT"], canonical.LinkedExecutionUnits);

        var dualRouted = emitted + "observed_in:\n  linked_issue: \"" + SourceIssueUrl
            + "\"\n  execution_unit: OTHER-LEGACY-UNIT\n";
        Assert.Contains("linked_issue: \"" + SourceIssueUrl + "\"", dualRouted, StringComparison.Ordinal);
        Assert.Contains("execution_unit: OTHER-LEGACY-UNIT", dualRouted, StringComparison.Ordinal);
        var dualCanonical = BugReportArtifactYaml.Deserialize(dualRouted);
        Assert.Equal([canonicalForeignUrl], dualCanonical.LinkedIssueRefs);
        Assert.Equal(["OTHER-UNIT"], dualCanonical.LinkedExecutionUnits);
        var invalid = RemoveTopLevelYamlField(dualRouted, "title");
        Assert.NotEqual(dualRouted, invalid);

        AssertMalformedSourceInventoryReport(host, reportFileName, invalid, SourceIssueUrl);
    }

    [Fact]
    public void CanonicalTargetUrlVetoesLegacyForeignIssueAndUnitWhenReportSchemaIsInvalid()
    {
        using var host = new TempHost();
        host.WriteMode(TeamMode.SoloConductor);
        host.WritePacketAndPublishArtifact(sourceArtifact: SourceIssueUrl);
        host.WriteActualBugSourceChain("BUG-G859-TARGET", SourceIssueUrl);
        const string reportFileName = "AAA-G859-CANONICAL-TARGET.report.yaml";
        var reportPath = host.WriteActualBugReport("AAA-G859-CANONICAL-TARGET", "Target canonical report", [SourceIssueUrl], ["OTHER-UNIT"]);
        var emitted = File.ReadAllText(reportPath);
        var canonical = BugReportArtifactYaml.Deserialize(emitted);
        Assert.Equal([SourceIssueUrl], canonical.LinkedIssueRefs);
        Assert.Equal(["OTHER-UNIT"], canonical.LinkedExecutionUnits);

        var dualRouted = emitted
            + "observed_in:\n  linked_issue: \"" + ForeignIssueUrl
            + "\"\n  execution_unit: OTHER-LEGACY-UNIT\n";
        Assert.Contains("linked_issue: \"" + ForeignIssueUrl + "\"", dualRouted, StringComparison.Ordinal);
        Assert.Contains("execution_unit: OTHER-LEGACY-UNIT", dualRouted, StringComparison.Ordinal);
        var dualCanonical = BugReportArtifactYaml.Deserialize(dualRouted);
        Assert.Equal([SourceIssueUrl], dualCanonical.LinkedIssueRefs);
        Assert.Equal(["OTHER-UNIT"], dualCanonical.LinkedExecutionUnits);
        var invalid = RemoveTopLevelYamlField(dualRouted, "title");
        Assert.NotEqual(dualRouted, invalid);

        AssertMalformedSourceInventoryReport(host, reportFileName, invalid, SourceIssueUrl);
    }

    [Fact]
    public void LegacyCurrentUnitAndForeignIssueCannotBeExcluded()
    {
        using var host = new TempHost();
        host.WriteMode(TeamMode.SoloConductor);
        host.WritePacketAndPublishArtifact(sourceArtifact: SourceIssueUrl);
        host.WriteActualBugSourceChain("BUG-G859-TARGET", SourceIssueUrl);
        var currentUnitLegacy = LegacyForeignSourceReport.Replace("TOY-CALC-V0-01", "G855", StringComparison.Ordinal);
        var legacyPath = Path.Combine(host.Root, ".intent-cli", "bugs", "AAA-current-unit.report.yaml");
        File.WriteAllText(legacyPath, currentUnitLegacy);
        var reader = new FixedReader(new UnitStatusRemoteSnapshot
        {
            State = "completed", HeadBefore = HeadSha, HeadAfter = HeadSha, HeadSha = HeadSha, Facts = [],
        });

        using var report = RunStatusJson(host, reader, out var exit);

        Assert.Equal(1, exit);
        var source = TempHost.FindSubcheck(report.RootElement, "bug-chain-or-ruling");
        Assert.Equal("unavailable", source.GetProperty("state").GetString());
        Assert.Equal("source-report-unreadable", source.GetProperty("cause").GetString());
        Assert.Equal("read-failure", source.GetProperty("unavailable_class").GetString());
        Assert.Contains("AAA-current-unit.report.yaml", source.GetProperty("detail").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public void MatchedTargetSchemaAndChainFailuresRemainStrict()
    {
        var cases = new[]
        {
            (Name: "missing-title", ExpectedState: "unavailable", ExpectedCause: "source-report-unreadable"),
            (Name: "wrong-embedded-id", ExpectedState: "unavailable", ExpectedCause: "source-report-identity-conflict"),
            (Name: "malformed-triage", ExpectedState: "unavailable", ExpectedCause: "source-triage-plan-unreadable"),
            (Name: "malformed-plan", ExpectedState: "unavailable", ExpectedCause: "source-triage-plan-unreadable"),
            (Name: "missing-triage", ExpectedState: "missing", ExpectedCause: "source-triage-plan-not-recorded"),
            (Name: "missing-plan", ExpectedState: "missing", ExpectedCause: "source-triage-plan-not-recorded"),
        };

        foreach (var item in cases)
        {
            using var host = new TempHost();
            host.WriteMode(TeamMode.SoloConductor);
            host.WritePacketAndPublishArtifact(sourceArtifact: SourceIssueUrl);
            host.WriteActualBugSourceChain("BUG-G859-TARGET", SourceIssueUrl);
            var reportPath = Path.Combine(host.Root, ".intent-cli", "bugs", "BUG-G859-TARGET.report.yaml");
            var triagePath = Path.Combine(host.Root, ".intent-cli", "bugs", "BUG-G859-TARGET.triage.yaml");
            var planPath = Path.Combine(host.Root, ".intent-cli", "bugs", "BUG-G859-TARGET.plan.yaml");
            switch (item.Name)
            {
                case "missing-title":
                    File.WriteAllText(reportPath, string.Join("\n", File.ReadAllText(reportPath).Split('\n')
                        .Where(line => !line.StartsWith("title:", StringComparison.Ordinal))));
                    break;
                case "wrong-embedded-id":
                    File.WriteAllText(reportPath, File.ReadAllText(reportPath).Replace("bug_id: BUG-G859-TARGET", "bug_id: BUG-G859-OTHER", StringComparison.Ordinal));
                    break;
                case "malformed-triage":
                    File.WriteAllText(triagePath, "not a canonical triage artifact\n");
                    break;
                case "malformed-plan":
                    File.WriteAllText(planPath, "not a canonical plan artifact\n");
                    break;
                case "missing-triage":
                    File.Delete(triagePath);
                    break;
                case "missing-plan":
                    File.Delete(planPath);
                    break;
                default:
                    throw new InvalidOperationException("Unexpected strict-chain fixture.");
            }

            var reader = new FixedReader(new UnitStatusRemoteSnapshot
            {
                State = "completed", HeadBefore = HeadSha, HeadAfter = HeadSha, HeadSha = HeadSha, Facts = [],
            });
            using var report = RunStatusJson(host, reader, out var exit);
            var source = TempHost.FindSubcheck(report.RootElement, "bug-chain-or-ruling");
            Assert.Equal(item.ExpectedState, source.GetProperty("state").GetString());
            Assert.Equal(item.ExpectedCause, source.GetProperty("cause").GetString());
            Assert.Equal(item.ExpectedState == "missing" ? 0 : 1, exit);
            if (item.Name == "missing-title")
                Assert.Equal("read-failure", source.GetProperty("unavailable_class").GetString());
            if (item.Name == "wrong-embedded-id")
                Assert.Equal("identity-conflict", source.GetProperty("unavailable_class").GetString());
        }
    }

    [Fact]
    public void DirectReportPathKeepsStrictValidationAndDoesNotUseForeignProjection()
    {
        using (var host = new TempHost())
        {
            host.WriteMode(TeamMode.SoloConductor);
            host.WritePacketAndPublishArtifact(sourceArtifact: ".intent-cli/bugs/BUG-G859-TARGET.report.yaml");
            host.WriteActualBugSourceChain("BUG-G859-TARGET", SourceIssueUrl);
            var reader = new FixedReader(new UnitStatusRemoteSnapshot
            {
                State = "completed", HeadBefore = HeadSha, HeadAfter = HeadSha, HeadSha = HeadSha, Facts = [],
            });

            using var report = RunStatusJson(host, reader, out var exit);
            Assert.Equal(0, exit);
            var source = TempHost.FindSubcheck(report.RootElement, "bug-chain-or-ruling");
            Assert.Equal("done", source.GetProperty("state").GetString());
            Assert.Contains(source.GetProperty("evidence").EnumerateArray(), item =>
                item.GetProperty("path").GetString() == ".intent-cli/bugs/BUG-G859-TARGET.report.yaml"
                && item.GetProperty("url").ValueKind == JsonValueKind.Null);
        }

        using (var host = new TempHost())
        {
            host.WriteMode(TeamMode.SoloConductor);
            host.WritePacketAndPublishArtifact(sourceArtifact: ".intent-cli/bugs/BUG-20260416-fix-worktree-diff-blocked-after-backend-exit.report.yaml");
            var foreignPath = Path.Combine(host.Root, ".intent-cli", "bugs", "BUG-20260416-fix-worktree-diff-blocked-after-backend-exit.report.yaml");
            Directory.CreateDirectory(Path.GetDirectoryName(foreignPath)!);
            File.WriteAllText(foreignPath, LegacyForeignSourceReport);
            var reader = new FixedReader(new UnitStatusRemoteSnapshot
            {
                State = "completed", HeadBefore = HeadSha, HeadAfter = HeadSha, HeadSha = HeadSha, Facts = [],
            });

            using var report = RunStatusJson(host, reader, out var exit);
            Assert.Equal(1, exit);
            var source = TempHost.FindSubcheck(report.RootElement, "bug-chain-or-ruling");
            Assert.Equal("unavailable", source.GetProperty("state").GetString());
            Assert.Equal("source-report-unreadable", source.GetProperty("cause").GetString());
            Assert.Contains(".intent-cli/bugs/BUG-20260416-fix-worktree-diff-blocked-after-backend-exit.report.yaml",
                source.GetProperty("detail").GetString(), StringComparison.Ordinal);
        }

        using (var host = new TempHost())
        {
            host.WriteMode(TeamMode.SoloConductor);
            host.WritePacketAndPublishArtifact(sourceArtifact: ".intent-cli/bugs/nested/BUG-G859-TARGET.report.yaml");
            Directory.CreateDirectory(Path.Combine(host.Root, ".intent-cli", "bugs", "nested"));
            File.WriteAllText(Path.Combine(host.Root, ".intent-cli", "bugs", "nested", "BUG-G859-TARGET.report.yaml"), LegacyForeignSourceReport);
            var reader = new FixedReader(new UnitStatusRemoteSnapshot
            {
                State = "completed", HeadBefore = HeadSha, HeadAfter = HeadSha, HeadSha = HeadSha, Facts = [],
            });

            using var report = RunStatusJson(host, reader, out var exit);
            Assert.Equal(1, exit);
            var source = FindBugSource(report.RootElement);
            Assert.Equal("source-reference-identity-conflict", source.GetProperty("cause").GetString());
            Assert.Equal("identity-conflict", source.GetProperty("unavailable_class").GetString());
        }
    }

    [Fact]
    public void MalformedOrAmbiguousRoutingNeverAuthorizesForeignExclusion()
    {
        var uncertainReports = new[]
        {
            "linked_issue_refs: null\n",
            "linked_issue_refs:\n  - null\n",
            "linked_issue_refs:\n  - 1878\n",
            "linked_issue_refs:\n  - {issue: \"https://github.com/tomohisa/toy-calc-sample/issues/3\"}\n",
            "linked_execution_units: null\n",
            "linked_execution_units:\n  - null\n",
            "linked_execution_units:\n  - [G855]\n",
            "linked_execution_units:\n  - ../unsafe\n",
            "observed_in: null\n",
            "observed_in:\n  linked_issue: [\"https://github.com/tomohisa/toy-calc-sample/issues/3\"]\n  execution_unit: G855\n",
            "observed_in:\n  linked_issue: \"https://github.com/tomohisa/toy-calc-sample/issues/3\"\n",
            "observed_in:\n  execution_unit: TOY-CALC-V0-01\n",
            "linked_issue_refs:\n  - \"https://github.com/tomohisa/toy-calc-sample/issues/3\"\nlinked_issue_refs:\n  - \"https://github.com/elsewhere/project/issues/2\"\n",
            "observed_in:\n  linked_issue: \"https://github.com/tomohisa/toy-calc-sample/issues/3\"\n  linked_issue: \"https://github.com/elsewhere/project/issues/2\"\n  execution_unit: TOY-CALC-V0-01\n",
            "metadata:\n  linked_issue_refs:\n    - \"https://github.com/J-Tech-Japan/intent-system/issues/1878\"\n  linked_execution_units: [G855]\n",
            "broken: [\n",
            "just a scalar\n",
            "linked_issue_refs:\n  - \"https://github.com/tomohisa/toy-calc-sample/issues/3\"\nlinked_execution_units:\n  - TOY-CALC-V0-01\n---\nlinked_issue_refs:\n  - \"https://github.com/J-Tech-Japan/intent-system/issues/1878\"\n",
        };

        foreach (var yaml in uncertainReports)
        {
            using var host = new TempHost();
            host.WriteMode(TeamMode.SoloConductor);
            host.WritePacketAndPublishArtifact(sourceArtifact: SourceIssueUrl);
            host.WriteActualBugSourceChain("BUG-G859-TARGET", SourceIssueUrl);
            var path = Path.Combine(host.Root, ".intent-cli", "bugs", "AAA-UNCERTAIN.report.yaml");
            File.WriteAllText(path, yaml);
            var reader = new FixedReader(new UnitStatusRemoteSnapshot
            {
                State = "completed", HeadBefore = HeadSha, HeadAfter = HeadSha, HeadSha = HeadSha, Facts = [],
            });

            using var report = RunStatusJson(host, reader, out var exit);

            Assert.Equal(1, exit);
            var source = TempHost.FindSubcheck(report.RootElement, "bug-chain-or-ruling");
            Assert.Equal("unavailable", source.GetProperty("state").GetString());
            Assert.Equal("source-report-unreadable", source.GetProperty("cause").GetString());
            Assert.Equal("read-failure", source.GetProperty("unavailable_class").GetString());
            Assert.Contains("AAA-UNCERTAIN.report.yaml", source.GetProperty("detail").GetString(), StringComparison.Ordinal);
            var attempted = Assert.Single(source.GetProperty("evidence").EnumerateArray());
            Assert.Equal(".intent-cli/bugs/AAA-UNCERTAIN.report.yaml", attempted.GetProperty("path").GetString());
            Assert.Equal(SourceIssueUrl, attempted.GetProperty("url").GetString());
            Assert.Equal("attempted-canonical-source-inventory-read", attempted.GetProperty("provenance").GetString());
        }
    }

    public static IEnumerable<object[]> MalformedRoutingWithIndependentForeignProofCases()
    {
        yield return new object[] { "canonical", "linked_execution_units", "linked_execution_units: null\n" };
        yield return new object[] { "canonical", "linked_execution_units", "linked_execution_units: OTHER-UNIT\n" };
        yield return new object[] { "canonical", "linked_execution_units", "linked_execution_units:\n  - null\n" };
        yield return new object[] { "canonical", "linked_execution_units", "linked_execution_units:\n  - [OTHER-UNIT]\n" };
        yield return new object[] { "canonical", "linked_execution_units", "linked_execution_units:\n  - ../unsafe\n" };
        yield return new object[] { "canonical", "observed_in", "observed_in: null\n" };
        yield return new object[] { "canonical", "observed_in", "observed_in: OTHER\n" };
        yield return new object[] { "canonical", "observed_in", "observed_in: []\n" };
        yield return new object[] { "canonical", "observed_in", "observed_in:\n  linked_issue: \"" + ForeignIssueUrl + "\"\n" };
        yield return new object[] { "canonical", "observed_in", "observed_in:\n  execution_unit: OTHER-UNIT\n" };
        yield return new object[] { "canonical", "observed_in", "observed_in:\n  linked_issue: [\"" + ForeignIssueUrl + "\"]\n  execution_unit: OTHER-UNIT\n" };
        yield return new object[] { "canonical", "observed_in", "observed_in:\n  linked_issue: \"" + ForeignIssueUrl + "\"\n  execution_unit: {unit: OTHER-UNIT}\n" };
        yield return new object[] { "legacy", "linked_issue_refs", "linked_issue_refs: null\n" };
        yield return new object[] { "legacy", "linked_issue_refs", "linked_issue_refs: \"" + ForeignIssueUrl + "\"\n" };
        yield return new object[] { "legacy", "linked_issue_refs", "linked_issue_refs:\n  - null\n" };
        yield return new object[] { "legacy", "linked_issue_refs", "linked_issue_refs:\n  - 1878\n" };
        yield return new object[] { "legacy", "linked_issue_refs", "linked_issue_refs:\n  - {issue: \"" + ForeignIssueUrl + "\"}\n" };
        yield return new object[] { "legacy", "linked_issue_refs", "linked_issue_refs:\n  - \"" + ForeignIssueUrl + "\"\n  - 1878\n" };
    }

    [Theory]
    [MemberData(nameof(MalformedRoutingWithIndependentForeignProofCases))]
    public void MalformedRoutingVetoesExclusionEvenWhenOtherFieldsProveForeignIdentity(
        string sourceProof, string malformedField, string malformedRouting)
    {
        using var host = new TempHost();
        host.WriteMode(TeamMode.SoloConductor);
        host.WritePacketAndPublishArtifact(sourceArtifact: SourceIssueUrl);
        host.WriteActualBugSourceChain("BUG-G859-TARGET", SourceIssueUrl);
        const string reportFileName = "AAA-G859-VETO.report.yaml";
        var reportPath = host.WriteActualBugReport("AAA-G859-VETO", "Foreign veto fixture",
            sourceProof == "canonical" ? [ForeignIssueUrl] : [],
            sourceProof == "canonical" ? ["OTHER-UNIT"] : []);
        var yaml = File.ReadAllText(reportPath);

        if (sourceProof == "canonical")
        {
            Assert.Contains("linked_issue_refs:\n  - \"" + ForeignIssueUrl + "\"", yaml, StringComparison.Ordinal);
        }
        else
        {
            var legacyProof = "observed_in:\n  linked_issue: \"" + ForeignIssueUrl
                + "\"\n  execution_unit: OTHER-UNIT\n";
            yaml = SetTopLevelYamlField(yaml, "observed_in", legacyProof);
            Assert.Contains(legacyProof.TrimEnd('\n'), yaml, StringComparison.Ordinal);
        }

        yaml = SetTopLevelYamlField(yaml, malformedField, malformedRouting);
        Assert.Contains(malformedRouting.TrimEnd('\n'), yaml, StringComparison.Ordinal);
        var invalid = RemoveTopLevelYamlField(yaml, "title");
        Assert.NotEqual(yaml, invalid);

        AssertMalformedSourceInventoryReport(host, reportFileName, invalid, SourceIssueUrl);
    }

    [Fact]
    public void NestedMetadataTargetLookalikesDoNotVetoCanonicalForeignExclusion()
    {
        using var host = new TempHost();
        host.WriteMode(TeamMode.SoloConductor);
        host.WritePacketAndPublishArtifact(sourceArtifact: SourceIssueUrl);
        host.WriteActualBugSourceChain("BUG-G859-TARGET", SourceIssueUrl);
        const string reportFileName = "AAA-G859-METADATA.report.yaml";
        var reportPath = host.WriteActualBugReport("AAA-G859-METADATA", "Foreign report with metadata lookalikes",
            [ForeignIssueUrl], ["OTHER-UNIT"]);
        var emitted = File.ReadAllText(reportPath);
        var withMetadata = emitted
            + "metadata:\n  linked_issue_refs:\n    - \"" + SourceIssueUrl
            + "\"\n  linked_execution_units: [G855]\n";
        Assert.Contains("linked_issue_refs:\n  - \"" + ForeignIssueUrl + "\"", withMetadata, StringComparison.Ordinal);
        Assert.Contains("    - \"" + SourceIssueUrl + "\"", withMetadata, StringComparison.Ordinal);
        Assert.Contains("linked_execution_units: [G855]", withMetadata, StringComparison.Ordinal);
        var invalid = RemoveTopLevelYamlField(withMetadata, "title");
        Assert.NotEqual(withMetadata, invalid);
        Assert.Throws<InvalidOperationException>(() => BugReportArtifactYaml.Deserialize(invalid));
        File.WriteAllText(reportPath, invalid);
        var bytes = File.ReadAllBytes(reportPath);
        var reader = new FixedReader(new UnitStatusRemoteSnapshot
        {
            State = "completed", HeadBefore = HeadSha, HeadAfter = HeadSha, HeadSha = HeadSha, Facts = [],
        });

        using var report = RunStatusJson(host, reader, out var exit);

        Assert.Equal(0, exit);
        var source = FindBugSource(report.RootElement);
        Assert.Equal("done", source.GetProperty("state").GetString());
        Assert.Equal("canonical-source-chain-recorded", source.GetProperty("cause").GetString());
        Assert.DoesNotContain(source.GetProperty("evidence").EnumerateArray(), item =>
            item.GetProperty("path").GetString() == ".intent-cli/bugs/" + reportFileName);
        Assert.Equal(bytes, File.ReadAllBytes(reportPath));
    }

    [Fact]
    public void YamlDotNetKeepsImplicitNullAndExplicitStringUnitTagsDistinct()
    {
        static YamlScalarNode ReadExecutionUnit(string value)
        {
            var stream = new YamlStream();
            using var reader = new StringReader("observed_in:\n  execution_unit: " + value + "\n");
            stream.Load(reader);
            var root = Assert.IsType<YamlMappingNode>(stream.Documents.Single().RootNode);
            var observedIn = Assert.IsType<YamlMappingNode>(root.Children[new YamlScalarNode("observed_in")]);
            return Assert.IsType<YamlScalarNode>(observedIn.Children[new YamlScalarNode("execution_unit")]);
        }

        var implicitNull = ReadExecutionUnit("null");
        var explicitString = ReadExecutionUnit("!!str null");
        var quotedString = ReadExecutionUnit("\"null\"");
        var explicitlyNull = ReadExecutionUnit("!!null G859-OTHER");

        Assert.Equal("null", explicitString.Value);
        Assert.Equal("null", quotedString.Value);
        Assert.NotEqual(implicitNull.Tag, explicitString.Tag);
        Assert.Equal(YamlDotNet.Core.ScalarStyle.Plain, explicitString.Style);
        Assert.NotEqual(implicitNull.Tag, explicitlyNull.Tag);
    }

    [Theory]
    [InlineData("null", "unavailable")]
    [InlineData("~", "unavailable")]
    [InlineData("!!null G859-OTHER", "unavailable")]
    [InlineData("!!str null", "done")]
    [InlineData("\"null\"", "done")]
    public void LegacyUnitScalarNullnessDoesNotConfuseExplicitStringUnits(string unitYaml, string expectedState)
    {
        using var host = new TempHost();
        host.WriteMode(TeamMode.SoloConductor);
        host.WritePacketAndPublishArtifact(sourceArtifact: SourceIssueUrl);
        host.WriteActualBugSourceChain("BUG-G859-TARGET", SourceIssueUrl);
        var path = Path.Combine(host.Root, ".intent-cli", "bugs", "AAA-LEGACY-UNIT.report.yaml");
        File.WriteAllText(path,
            "observed_in:\n  linked_issue: \"https://github.com/tomohisa/toy-calc-sample/issues/3\"\n  execution_unit: " + unitYaml + "\n");
        var reader = new FixedReader(new UnitStatusRemoteSnapshot
        {
            State = "completed", HeadBefore = HeadSha, HeadAfter = HeadSha, HeadSha = HeadSha, Facts = [],
        });

        using var report = RunStatusJson(host, reader, out var exit);

        Assert.Equal(expectedState == "done" ? 0 : 1, exit);
        var source = TempHost.FindSubcheck(report.RootElement, "bug-chain-or-ruling");
        Assert.Equal(expectedState, source.GetProperty("state").GetString());
        if (expectedState == "unavailable")
        {
            Assert.Equal("source-report-unreadable", source.GetProperty("cause").GetString());
            Assert.Equal("read-failure", source.GetProperty("unavailable_class").GetString());
        }
        else
        {
            Assert.Equal("canonical-source-chain-recorded", source.GetProperty("cause").GetString());
        }
    }

    [Fact]
    public void DisappearedInventoryMemberRetainsExactReadFailureAlongsideTarget()
    {
        using var host = new TempHost();
        host.WriteMode(TeamMode.SoloConductor);
        host.WritePacketAndPublishArtifact(sourceArtifact: SourceIssueUrl);
        host.WriteActualBugSourceChain("BUG-G859-TARGET", SourceIssueUrl);
        var missingTarget = Path.Combine(host.Root, "removed-before-read.report.yaml");
        var disappearedPath = Path.Combine(host.Root, ".intent-cli", "bugs", "AAA-DISAPPEARED.report.yaml");
        File.CreateSymbolicLink(disappearedPath, missingTarget);
        Assert.Contains(disappearedPath, Directory.EnumerateFiles(Path.GetDirectoryName(disappearedPath)!, "*.report.yaml"));
        Assert.ThrowsAny<IOException>(() => File.ReadAllText(disappearedPath));
        var reader = new FixedReader(new UnitStatusRemoteSnapshot
        {
            State = "completed", HeadBefore = HeadSha, HeadAfter = HeadSha, HeadSha = HeadSha, Facts = [],
        });

        using var report = RunStatusJson(host, reader, out var exit);

        Assert.Equal(1, exit);
        var source = TempHost.FindSubcheck(report.RootElement, "bug-chain-or-ruling");
        Assert.Equal("unavailable", source.GetProperty("state").GetString());
        Assert.Equal("source-report-unreadable", source.GetProperty("cause").GetString());
        Assert.Contains("AAA-DISAPPEARED.report.yaml", source.GetProperty("detail").GetString(), StringComparison.Ordinal);
        var attempted = Assert.Single(source.GetProperty("evidence").EnumerateArray());
        Assert.Equal(".intent-cli/bugs/AAA-DISAPPEARED.report.yaml", attempted.GetProperty("path").GetString());
        Assert.Equal(SourceIssueUrl, attempted.GetProperty("url").GetString());
        Assert.Equal("attempted-canonical-source-inventory-read", attempted.GetProperty("provenance").GetString());
    }

    [Fact]
    public void TargetMatchDoesNotHideLaterUnscopableInventoryMember()
    {
        using var host = new TempHost();
        host.WriteMode(TeamMode.SoloConductor);
        host.WritePacketAndPublishArtifact(sourceArtifact: SourceIssueUrl);
        host.WriteActualBugSourceChain("BUG-G859-TARGET", SourceIssueUrl);
        var laterPath = Path.Combine(host.Root, ".intent-cli", "bugs", "ZZZ-target-unscopable.report.yaml");
        File.WriteAllText(laterPath,
            "bug_id: ZZZ-target-unscopable\nlinked_issue_refs:\n  - \"" + SourceIssueUrl + "\"\n");
        var reader = new FixedReader(new UnitStatusRemoteSnapshot
        {
            State = "completed", HeadBefore = HeadSha, HeadAfter = HeadSha, HeadSha = HeadSha, Facts = [],
        });

        using var report = RunStatusJson(host, reader, out var exit);

        Assert.Equal(1, exit);
        var source = TempHost.FindSubcheck(report.RootElement, "bug-chain-or-ruling");
        Assert.Equal("unavailable", source.GetProperty("state").GetString());
        Assert.Equal("source-report-unreadable", source.GetProperty("cause").GetString());
        Assert.Contains("ZZZ-target-unscopable.report.yaml", source.GetProperty("detail").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public void EmptyAndCompleteCanonicalNonmatchInventoriesRemainMissing()
    {
        using (var host = new TempHost())
        {
            host.WriteMode(TeamMode.SoloConductor);
            host.WritePacketAndPublishArtifact(sourceArtifact: SourceIssueUrl);
            var reader = new FixedReader(new UnitStatusRemoteSnapshot
            {
                State = "completed", HeadBefore = HeadSha, HeadAfter = HeadSha, HeadSha = HeadSha, Facts = [],
            });

            using var report = RunStatusJson(host, reader, out var exit);
            Assert.Equal(0, exit);
            var source = TempHost.FindSubcheck(report.RootElement, "bug-chain-or-ruling");
            Assert.Equal("missing", source.GetProperty("state").GetString());
            Assert.Equal("source-chain-not-recorded", source.GetProperty("cause").GetString());
        }

        using (var host = new TempHost())
        {
            host.WriteMode(TeamMode.SoloConductor);
            host.WritePacketAndPublishArtifact(sourceArtifact: SourceIssueUrl);
            host.WriteActualBugReport("BUG-G859-NONMATCH", "Canonical report without source links", [], []);
            var reader = new FixedReader(new UnitStatusRemoteSnapshot
            {
                State = "completed", HeadBefore = HeadSha, HeadAfter = HeadSha, HeadSha = HeadSha, Facts = [],
            });

            using var report = RunStatusJson(host, reader, out var exit);
            Assert.Equal(0, exit);
            var source = TempHost.FindSubcheck(report.RootElement, "bug-chain-or-ruling");
            Assert.Equal("missing", source.GetProperty("state").GetString());
            Assert.Equal("source-chain-not-recorded", source.GetProperty("cause").GetString());
        }
    }

    [Theory]
    [InlineData(499, "done", "canonical-source-chain-recorded")]
    [InlineData(500, "unavailable", "source-inventory-truncated")]
    public void SourceInventoryBoundStillCountsEveryUrlReport(int foreignReportCount, string expectedState, string expectedCause)
    {
        using var host = new TempHost();
        host.WriteMode(TeamMode.SoloConductor);
        host.WritePacketAndPublishArtifact(sourceArtifact: SourceIssueUrl);
        host.WriteActualBugSourceChain("BUG-G859-TARGET", SourceIssueUrl);
        var bugs = Path.Combine(host.Root, ".intent-cli", "bugs");
        Directory.CreateDirectory(bugs);
        const string unrelated = "observed_in:\n  linked_issue: \"https://github.com/tomohisa/toy-calc-sample/issues/3\"\n  execution_unit: OTHER-UNIT\n";
        for (var index = 0; index < foreignReportCount; index++)
            File.WriteAllText(Path.Combine(bugs, $"AAA-G859-FOREIGN-{index:D3}.report.yaml"), unrelated);
        Assert.Equal(foreignReportCount + 1, Directory.EnumerateFiles(bugs, "*.report.yaml").Count());
        var reader = new FixedReader(new UnitStatusRemoteSnapshot
        {
            State = "completed", HeadBefore = HeadSha, HeadAfter = HeadSha, HeadSha = HeadSha, Facts = [],
        });

        using var report = RunStatusJson(host, reader, out var exit);

        Assert.Equal(expectedState == "done" ? 0 : 1, exit);
        var source = TempHost.FindSubcheck(report.RootElement, "bug-chain-or-ruling");
        Assert.Equal(expectedState, source.GetProperty("state").GetString());
        Assert.Equal(expectedCause, source.GetProperty("cause").GetString());
        if (expectedState == "unavailable")
            Assert.Equal("read-failure", source.GetProperty("unavailable_class").GetString());
    }

    [Fact]
    public void UnrelatedMalformedReportMakesSourceInventoryUnavailableWithPathAndProvenance()
    {
        using var host = new TempHost();
        host.WriteMode(TeamMode.SoloConductor);
        const string sourceUrl = "https://github.com/J-Tech-Japan/intent-system/issues/1861";
        host.WritePacketAndPublishArtifact(sourceArtifact: sourceUrl);
        host.WriteBugSourceChain("BUG-1861", sourceUrl);
        var malformedPath = Path.Combine(host.Context.RepoRoot, ".intent-cli", "bugs", "UNRELATED.report.yaml");
        File.WriteAllText(malformedPath, "bug_id: unrelated\n");
        var reader = new FixedReader(new UnitStatusRemoteSnapshot
        {
            State = "completed", HeadBefore = HeadSha, HeadAfter = HeadSha, HeadSha = HeadSha, Facts = [],
        });
        using var writer = new StringWriter();

        var exit = UnitStatusCommand.ExecuteCore(host.Context,
            ["--execution-unit", "G855", "--domain", "intent-cli", "--team", "intent-cli-dev", "--format", "json"], writer, reader);

        Assert.Equal(1, exit);
        using var report = JsonDocument.Parse(writer.ToString());
        var source = TempHost.FindSubcheck(report.RootElement, "bug-chain-or-ruling");
        Assert.Equal("unavailable", source.GetProperty("state").GetString());
        Assert.Equal("source-report-unreadable", source.GetProperty("cause").GetString());
        Assert.Equal("read-failure", source.GetProperty("unavailable_class").GetString());
        Assert.Contains(sourceUrl, source.GetProperty("detail").GetString(), StringComparison.Ordinal);
        Assert.Contains(".intent-cli/bugs/UNRELATED.report.yaml", source.GetProperty("detail").GetString(), StringComparison.Ordinal);
        var attempted = Assert.Single(source.GetProperty("evidence").EnumerateArray());
        Assert.Equal("bug-report-artifact", attempted.GetProperty("kind").GetString());
        Assert.Equal(".intent-cli/bugs/UNRELATED.report.yaml", attempted.GetProperty("path").GetString());
        Assert.Equal(sourceUrl, attempted.GetProperty("url").GetString());
        Assert.Equal("attempted-canonical-source-inventory-read", attempted.GetProperty("provenance").GetString());
        Assert.Equal("done", TempHost.FindSubcheck(report.RootElement, "publication-artifact").GetProperty("state").GetString());
    }

    [Fact]
    public void ExplicitUnsupportedRulingReferenceRemainsProvenanceLimited()
    {
        using var host = new TempHost();
        host.WriteMode(TeamMode.SoloConductor);
        host.WritePacketAndPublishArtifact(rulingArtifact: ".intent-cli/rulings/G855.yaml");
        var reader = new FixedReader(new UnitStatusRemoteSnapshot
        {
            State = "completed", HeadBefore = HeadSha, HeadAfter = HeadSha, HeadSha = HeadSha, Facts = [],
        });
        using var writer = new StringWriter();

        var exit = UnitStatusCommand.ExecuteCore(host.Context,
            ["--execution-unit", "G855", "--domain", "intent-cli", "--team", "intent-cli-dev", "--format", "json"], writer, reader);

        Assert.Equal(0, exit);
        using var report = JsonDocument.Parse(writer.ToString());
        var source = TempHost.FindSubcheck(report.RootElement, "bug-chain-or-ruling");
        Assert.Equal("unavailable", source.GetProperty("state").GetString());
        Assert.Equal("ruling-reference-unsupported", source.GetProperty("cause").GetString());
        Assert.Equal("provenance-limit", source.GetProperty("unavailable_class").GetString());
        Assert.Empty(source.GetProperty("repair_commands").EnumerateArray());
        Assert.Equal("no-supported-ruling-artifact-reader", source.GetProperty("repair_unavailable_reason").GetString());
    }

    [Theory]
    [InlineData("foreign-repo", "G855")]
    [InlineData("same-repo", "G854")]
    public void ForeignOrContradictoryCanonicalSourceIdentityIsUnavailable(string linkKind, string linkedUnit)
    {
        using var host = new TempHost();
        host.WriteMode(TeamMode.SoloConductor);
        const string sourceUrl = "https://github.com/J-Tech-Japan/intent-system/issues/1861";
        var reportLink = linkKind == "foreign-repo"
            ? "https://github.com/elsewhere/intent-system/issues/1861"
            : sourceUrl;
        host.WritePacketAndPublishArtifact(sourceArtifact: sourceUrl);
        host.WriteBugSourceChain("BUG-1861", sourceUrl, linkedIssueUrl: reportLink, linkedUnits: [linkedUnit]);
        var reader = new FixedReader(new UnitStatusRemoteSnapshot
        {
            State = "completed", HeadBefore = HeadSha, HeadAfter = HeadSha, HeadSha = HeadSha, Facts = [],
        });
        using var writer = new StringWriter();

        var exit = UnitStatusCommand.ExecuteCore(host.Context,
            ["--execution-unit", "G855", "--domain", "intent-cli", "--team", "intent-cli-dev", "--format", "json"], writer, reader);

        Assert.Equal(1, exit);
        using var report = JsonDocument.Parse(writer.ToString());
        var source = TempHost.FindSubcheck(report.RootElement, "bug-chain-or-ruling");
        Assert.Equal("unavailable", source.GetProperty("state").GetString());
        Assert.Equal("source-chain-identity-conflict", source.GetProperty("cause").GetString());
        Assert.Equal("identity-conflict", source.GetProperty("unavailable_class").GetString());
    }

    [Fact]
    public void ExplicitLocalSourcePathThatCannotBeReadIsUnavailableReadFailure()
    {
        foreach (var malformed in new[] { false, true })
        {
            using var host = new TempHost();
            host.WriteMode(TeamMode.SoloConductor);
            host.WritePacketAndPublishArtifact(sourceArtifact: ".intent-cli/bugs/BUG-1861.report.yaml");
            if (malformed)
            {
                var bugDirectory = Directory.CreateDirectory(Path.Combine(host.Context.RepoRoot, ".intent-cli", "bugs"));
                File.WriteAllText(Path.Combine(bugDirectory.FullName, "BUG-1861.report.yaml"), "not a canonical bug report\n");
            }
            var reader = new FixedReader(new UnitStatusRemoteSnapshot
            {
                State = "completed", HeadBefore = HeadSha, HeadAfter = HeadSha, HeadSha = HeadSha, Facts = [],
            });
            using var writer = new StringWriter();

            var exit = UnitStatusCommand.ExecuteCore(host.Context,
                ["--execution-unit", "G855", "--domain", "intent-cli", "--team", "intent-cli-dev", "--format", "json"], writer, reader);

            Assert.Equal(1, exit);
            using var report = JsonDocument.Parse(writer.ToString());
            var source = TempHost.FindSubcheck(report.RootElement, "bug-chain-or-ruling");
            Assert.Equal("unavailable", source.GetProperty("state").GetString());
            Assert.Equal("read-failure", source.GetProperty("unavailable_class").GetString());
        }
    }

    [Fact]
    public void AmbiguousOrMismatchedCanonicalSourceChainIsUnavailable()
    {
        const string sourceUrl = "https://github.com/J-Tech-Japan/intent-system/issues/1861";
        foreach (var duplicate in new[] { true, false })
        {
            using var host = new TempHost();
            host.WriteMode(TeamMode.SoloConductor);
            host.WritePacketAndPublishArtifact(sourceArtifact: sourceUrl);
            host.WriteBugSourceChain("BUG-1861", sourceUrl, duplicate: duplicate, corruptPlan: !duplicate);
            var reader = new FixedReader(new UnitStatusRemoteSnapshot
            {
                State = "completed", HeadBefore = HeadSha, HeadAfter = HeadSha, HeadSha = HeadSha, Facts = [],
            });
            using var writer = new StringWriter();

            var exit = UnitStatusCommand.ExecuteCore(host.Context,
                ["--execution-unit", "G855", "--domain", "intent-cli", "--team", "intent-cli-dev", "--format", "json"], writer, reader);

            Assert.Equal(1, exit);
            using var report = JsonDocument.Parse(writer.ToString());
            var source = TempHost.FindSubcheck(report.RootElement, "bug-chain-or-ruling");
            Assert.Equal("unavailable", source.GetProperty("state").GetString());
            Assert.Equal("identity-conflict", source.GetProperty("unavailable_class").GetString());
        }
    }

    [Fact]
    public void LegacyWritebackAbsenceIsMissingAndExplicitFalseIsNotApplicable()
    {
        foreach (var explicitFalse in new[] { false, true })
        {
            using var host = new TempHost();
            host.WriteMode(TeamMode.SoloConductor);
            host.WritePacketAndPublishArtifact(knowledgeRequired: explicitFalse ? false : null);
            var reader = new FixedReader(new UnitStatusRemoteSnapshot
            {
                State = "completed", HeadBefore = HeadSha, HeadAfter = HeadSha, HeadSha = HeadSha, Facts = [],
            });
            using var writer = new StringWriter();

            var exit = UnitStatusCommand.ExecuteCore(host.Context,
                ["--execution-unit", "G855", "--domain", "intent-cli", "--team", "intent-cli-dev", "--format", "json"], writer, reader);

            Assert.Equal(0, exit);
            using var report = JsonDocument.Parse(writer.ToString());
            var architect = TempHost.FindSubcheck(report.RootElement, "architect-knowledge-writeback");
            var orchestrator = TempHost.FindSubcheck(report.RootElement, "orchestrator-knowledge-writeback");
            Assert.Equal(explicitFalse ? "not-applicable" : "missing", architect.GetProperty("state").GetString());
            Assert.Equal(explicitFalse ? "knowledge-writeback-not-required" : "knowledge-writeback-declaration-absent", architect.GetProperty("cause").GetString());
            Assert.Equal(explicitFalse ? "not-applicable" : "missing", orchestrator.GetProperty("state").GetString());
            Assert.Equal("missing", TempHost.FindSubcheck(report.RootElement, "guide-reachability").GetProperty("state").GetString());
        }
    }

    [Fact]
    public void LegacyUnattributedWritebackIsRetainedButDoesNotClearArchitectDuty()
    {
        using var host = new TempHost();
        host.WriteMode(TeamMode.SoloConductor);
        host.WritePacketAndPublishArtifact(knowledgeRequired: true);
        host.WriteWritebackRecord(role: null, "record.json");
        var reader = new FixedReader(new UnitStatusRemoteSnapshot
        {
            State = "completed", HeadBefore = HeadSha, HeadAfter = HeadSha, HeadSha = HeadSha, Facts = [],
        });
        using var writer = new StringWriter();

        var exit = UnitStatusCommand.ExecuteCore(host.Context,
            ["--execution-unit", "G855", "--domain", "intent-cli", "--team", "intent-cli-dev", "--format", "json"], writer, reader);

        Assert.Equal(0, exit);
        using var report = JsonDocument.Parse(writer.ToString());
        var architect = TempHost.FindSubcheck(report.RootElement, "architect-knowledge-writeback");
        Assert.Equal("unavailable", architect.GetProperty("state").GetString());
        Assert.Equal("provenance-limit", architect.GetProperty("unavailable_class").GetString());
        Assert.Contains(architect.GetProperty("evidence").EnumerateArray(), evidence => evidence.GetProperty("role").ValueKind == JsonValueKind.Null);
    }

    [Fact]
    public void RoleAliasesAreNormalizedAndOrchestratorRecordDoesNotSatisfyArchitectDuty()
    {
        foreach (var role in new[] { "design", "orchestrator" })
        {
            using var host = new TempHost();
            host.WriteMode(TeamMode.SoloConductor);
            host.WritePacketAndPublishArtifact(knowledgeRequired: true);
            host.WriteWritebackRecord(role, role + ".json");
            var reader = new FixedReader(new UnitStatusRemoteSnapshot
            {
                State = "completed", HeadBefore = HeadSha, HeadAfter = HeadSha, HeadSha = HeadSha, Facts = [],
            });
            using var writer = new StringWriter();

            Assert.Equal(0, UnitStatusCommand.ExecuteCore(host.Context,
                ["--execution-unit", "G855", "--domain", "intent-cli", "--team", "intent-cli-dev", "--format", "json"], writer, reader));
            using var report = JsonDocument.Parse(writer.ToString());
            var architect = TempHost.FindSubcheck(report.RootElement, "architect-knowledge-writeback");
            Assert.Equal(role == "design" ? "done" : "missing", architect.GetProperty("state").GetString());
            var architectEvidence = Assert.Single(architect.GetProperty("evidence").EnumerateArray());
            Assert.Equal(role == "design" ? "architect" : role, architectEvidence.GetProperty("role").GetString());
            Assert.Contains("required-role=architect; qualifies=" + (role == "design" ? "true" : "false"),
                architectEvidence.GetProperty("provenance").GetString(), StringComparison.Ordinal);
            var orchestrator = TempHost.FindSubcheck(report.RootElement, "orchestrator-knowledge-writeback");
            Assert.Equal(role == "orchestrator" ? "done" : "missing",
                orchestrator.GetProperty("state").GetString());
            Assert.Single(orchestrator.GetProperty("evidence").EnumerateArray());
        }
    }

    [Fact]
    public void GuideRecordsPreserveAllRoleEvidenceWhileOnlyArchitectQualifies()
    {
        using var host = new TempHost();
        host.WriteMode(TeamMode.SoloConductor);
        host.WritePacketAndPublishArtifact(guideDeclared: true);
        host.WriteGuideRecord("architect", ["architect"], roleSlot: true, fileName: "architect.json");
        host.WriteGuideRecord("orchestrator", ["architect"], roleSlot: true, fileName: "orchestrator.json");
        Assert.Equal(2, RoleScopedCloseoutRecordStore.EnumerateExistingPaths(
            host.Context.RepoRoot, GuideReachabilityRecord.RecordRootRelativePath, "G855").Count);
        var reader = new FixedReader(new UnitStatusRemoteSnapshot
        {
            State = "completed", HeadBefore = HeadSha, HeadAfter = HeadSha, HeadSha = HeadSha, Facts = [],
        });
        using var writer = new StringWriter();

        Assert.Equal(0, UnitStatusCommand.ExecuteCore(host.Context,
            ["--execution-unit", "G855", "--domain", "intent-cli", "--team", "intent-cli-dev", "--format", "json"], writer, reader));
        using var report = JsonDocument.Parse(writer.ToString());
        var guide = TempHost.FindSubcheck(report.RootElement, "guide-reachability");
        Assert.Equal("guide-reachability-recorded", guide.GetProperty("cause").GetString());
        Assert.Equal("done", guide.GetProperty("state").GetString());
        var evidence = guide.GetProperty("evidence").EnumerateArray().ToArray();
        Assert.Equal(2, evidence.Length);
        Assert.Contains(evidence, pointer => pointer.GetProperty("role").GetString() == "architect"
            && pointer.GetProperty("provenance").GetString()!.Contains("required-role=architect; qualifies=true", StringComparison.Ordinal));
        Assert.Contains(evidence, pointer => pointer.GetProperty("role").GetString() == "orchestrator"
            && pointer.GetProperty("provenance").GetString()!.Contains("required-role=architect; qualifies=false", StringComparison.Ordinal));
    }

    [Fact]
    public void DuplicateGuideArchitectRecordsRetainEveryObservedRolePointer()
    {
        using var host = new TempHost();
        host.WriteMode(TeamMode.SoloConductor);
        host.WritePacketAndPublishArtifact(guideDeclared: true);
        host.WriteGuideRecord("architect", ["architect"], roleSlot: true, fileName: "architect-a.json");
        host.WriteGuideRecord("design", ["architect"], roleSlot: true, fileName: "architect-b.json");
        host.WriteGuideRecord("orchestrator", ["architect"], roleSlot: true, fileName: "orchestrator.json");
        var reader = new FixedReader(new UnitStatusRemoteSnapshot
        {
            State = "completed", HeadBefore = HeadSha, HeadAfter = HeadSha, HeadSha = HeadSha, Facts = [],
        });
        using var writer = new StringWriter();

        Assert.Equal(1, UnitStatusCommand.ExecuteCore(host.Context,
            ["--execution-unit", "G855", "--domain", "intent-cli", "--team", "intent-cli-dev", "--format", "json"], writer, reader));
        using var report = JsonDocument.Parse(writer.ToString());
        var guide = TempHost.FindSubcheck(report.RootElement, "guide-reachability");
        Assert.Equal("unavailable", guide.GetProperty("state").GetString());
        Assert.Equal("identity-conflict", guide.GetProperty("unavailable_class").GetString());
        Assert.Equal(3, guide.GetProperty("evidence").GetArrayLength());
    }

    [Fact]
    public void DuplicateArchitectWritebackRecordsAreUnavailableAndNotSelectedByOrder()
    {
        using var host = new TempHost();
        host.WriteMode(TeamMode.SoloConductor);
        host.WritePacketAndPublishArtifact(knowledgeRequired: true);
        host.WriteWritebackRecord("architect", "architect.json");
        host.WriteWritebackRecord("design", "design.json");
        host.WriteWritebackRecord("orchestrator", "orchestrator.json");
        var reader = new FixedReader(new UnitStatusRemoteSnapshot
        {
            State = "completed", HeadBefore = HeadSha, HeadAfter = HeadSha, HeadSha = HeadSha, Facts = [],
        });
        using var writer = new StringWriter();

        var exit = UnitStatusCommand.ExecuteCore(host.Context,
            ["--execution-unit", "G855", "--domain", "intent-cli", "--team", "intent-cli-dev", "--format", "json"], writer, reader);

        Assert.Equal(1, exit);
        using var report = JsonDocument.Parse(writer.ToString());
        var architect = TempHost.FindSubcheck(report.RootElement, "architect-knowledge-writeback");
        Assert.Equal("unavailable", architect.GetProperty("state").GetString());
        Assert.Equal("identity-conflict", architect.GetProperty("unavailable_class").GetString());
        Assert.Equal(3, architect.GetProperty("evidence").GetArrayLength());
        Assert.Contains(architect.GetProperty("evidence").EnumerateArray(), pointer =>
            pointer.GetProperty("role").GetString() == "orchestrator"
            && pointer.GetProperty("provenance").GetString()!.Contains("required-role=architect; qualifies=false", StringComparison.Ordinal));
    }

    [Fact]
    public void WritebackFactsRetainBothRolePointersAndQualifyEachDutySeparately()
    {
        using var host = new TempHost();
        host.WriteMode(TeamMode.SoloConductor);
        host.WritePacketAndPublishArtifact(knowledgeRequired: true);
        host.WriteWritebackRecord("architect", "architect.json");
        host.WriteWritebackRecord("orchestrator", "orchestrator.json");
        var reader = new FixedReader(new UnitStatusRemoteSnapshot
        {
            State = "completed", HeadBefore = HeadSha, HeadAfter = HeadSha, HeadSha = HeadSha, Facts = [],
        });
        using var writer = new StringWriter();

        Assert.Equal(0, UnitStatusCommand.ExecuteCore(host.Context,
            ["--execution-unit", "G855", "--domain", "intent-cli", "--team", "intent-cli-dev", "--format", "json"], writer, reader));
        using var report = JsonDocument.Parse(writer.ToString());
        foreach (var (id, expectedRole) in new[]
        {
            ("architect-knowledge-writeback", "architect"),
            ("orchestrator-knowledge-writeback", "orchestrator"),
        })
        {
            var fact = TempHost.FindSubcheck(report.RootElement, id);
            Assert.Equal("done", fact.GetProperty("state").GetString());
            var evidence = fact.GetProperty("evidence").EnumerateArray().ToArray();
            Assert.Equal(2, evidence.Length);
            Assert.Contains(evidence, pointer => pointer.GetProperty("role").GetString() == expectedRole
                && pointer.GetProperty("provenance").GetString()!.Contains("qualifies=true", StringComparison.Ordinal));
            Assert.Contains(evidence, pointer => pointer.GetProperty("role").GetString() != expectedRole
                && pointer.GetProperty("provenance").GetString()!.Contains("qualifies=false", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void GuideAudienceRoleDoesNotReplaceGuideRecorderRole()
    {
        using var host = new TempHost();
        host.WriteMode(TeamMode.SoloConductor);
        host.WritePacketAndPublishArtifact(guideDeclared: true);
        host.WriteGuideRecord(recorderRole: "orchestrator", audiences: ["architect"]);
        var reader = new FixedReader(new UnitStatusRemoteSnapshot
        {
            State = "completed", HeadBefore = HeadSha, HeadAfter = HeadSha, HeadSha = HeadSha, Facts = [],
        });
        using var writer = new StringWriter();

        Assert.Equal(0, UnitStatusCommand.ExecuteCore(host.Context,
            ["--execution-unit", "G855", "--domain", "intent-cli", "--team", "intent-cli-dev", "--format", "json"], writer, reader));
        using var report = JsonDocument.Parse(writer.ToString());
        var guide = TempHost.FindSubcheck(report.RootElement, "guide-reachability");
        Assert.Equal("missing", guide.GetProperty("state").GetString());
        Assert.Equal("guide-record-absent", guide.GetProperty("cause").GetString());
        var evidence = Assert.Single(guide.GetProperty("evidence").EnumerateArray());
        Assert.Equal("orchestrator", evidence.GetProperty("role").GetString());
        Assert.Contains("required-role=architect; qualifies=false", evidence.GetProperty("provenance").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public void DigestValidatedLocalReviewIsRecordedEvidenceAndDoesNotSubstituteForPostedReview()
    {
        using var host = new TempHost();
        host.WriteMode(TeamMode.SoloConductor);
        host.WritePacketAndPublishArtifact();
        host.WriteReviewRecord();
        var reader = new FixedReader(new UnitStatusRemoteSnapshot
        {
            State = "completed",
            HeadBefore = HeadSha,
            HeadAfter = HeadSha,
            HeadSha = HeadSha,
            Facts =
            [
                new UnitStatusFact { Id = "delta-review", State = UnitStatusStates.Done, Cause = "delta-review-current-head" },
                new UnitStatusFact { Id = "posted-review", State = UnitStatusStates.Missing, Cause = "posted-review-not-recorded-current-head" },
            ],
        });
        using var writer = new StringWriter();

        Assert.Equal(0, UnitStatusCommand.ExecuteCore(host.Context,
            ["--execution-unit", "G855", "--domain", "intent-cli", "--team", "intent-cli-dev", "--format", "json"], writer, reader));

        using var document = JsonDocument.Parse(writer.ToString());
        var reviewStep = document.RootElement.GetProperty("steps").EnumerateArray()
            .Single(step => step.GetProperty("id").GetString() == "independent-subagent-review");
        Assert.Equal("done", reviewStep.GetProperty("subchecks")[0].GetProperty("state").GetString());
        Assert.Equal("missing", reviewStep.GetProperty("subchecks")[1].GetProperty("state").GetString());
        var observed = Assert.Single(document.RootElement.GetProperty("observation").GetProperty("github_snapshot").GetProperty("reviews").EnumerateArray());
        Assert.Equal("local-cross-runtime-record", observed.GetProperty("source").GetString());
        Assert.Equal("request-changes", observed.GetProperty("verdict").GetString());
        Assert.Equal("cross-runtime", observed.GetProperty("relation").GetString());
    }

    private sealed class FixedReader : IUnitStatusSnapshotReader
    {
        private readonly UnitStatusRemoteSnapshot? _remote;
        private readonly UnitStatusClaimSnapshot? _claims;
        public FixedReader(UnitStatusRemoteSnapshot? remote = null, UnitStatusClaimSnapshot? claims = null)
        {
            _remote = remote;
            _claims = claims;
        }
        public int ClaimReads { get; private set; }
        public int GitHubReads { get; private set; }

        public UnitStatusClaimSnapshot ReadClaimSnapshot(CliContext context, string executionUnit)
        {
            ClaimReads++;
            return _claims ?? new UnitStatusClaimSnapshot { State = "completed" };
        }

        public UnitStatusRemoteSnapshot ObserveGitHub(CliContext context, string repo, int issue, int pullRequest,
            string executionUnit, string domain, string team)
        {
            GitHubReads++;
            return _remote ?? new UnitStatusRemoteSnapshot { State = "completed", Facts = [] };
        }

        public UnitStatusRemoteSnapshot ObserveGitHubIssue(CliContext context, string repo, int issue, string executionUnit)
        {
            GitHubReads++;
            return _remote ?? new UnitStatusRemoteSnapshot { State = "completed", Facts = [] };
        }
    }

    private sealed class CurrentDirectoryScope : IDisposable
    {
        private readonly string _original = Directory.GetCurrentDirectory();

        public CurrentDirectoryScope(string path) => Directory.SetCurrentDirectory(path);

        public void Dispose() => Directory.SetCurrentDirectory(_original);
    }

    private sealed class TempHost : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "g855-status-" + Guid.NewGuid().ToString("N"));
        public string Root => _root;

        public TempHost()
        {
            Directory.CreateDirectory(Path.Combine(_root, ".intent-cli"));
        }

        public CliContext Context => new()
        {
            RepoRoot = _root,
            Config = new CliConfig
            {
                Project = new ProjectConfig { Domain = "intent-cli", ArtifactRoot = ".intent-cli", MetadataSourceBranch = "metadata" },
            },
        };

        public void WriteMode(string mode, string domain = "intent-cli", string team = "intent-cli-dev", string? previousMode = null)
        {
            var firstAt = DateTimeOffset.Parse("2026-09-30T00:00:00Z");
            var currentAt = DateTimeOffset.Parse("2026-10-01T00:00:00Z");
            var transitions = previousMode is null
                ? new[] { new TeamModeTransition { From = TeamMode.Default, To = mode, At = currentAt } }
                : new[]
                {
                    new TeamModeTransition { From = TeamMode.Default, To = previousMode, At = firstAt },
                    new TeamModeTransition { From = previousMode, To = mode, At = currentAt },
                };
            var state = new TeamModeState
            {
                SchemaVersion = TeamModeStore.SchemaVersion,
                Entries =
                [
                    new TeamModeEntry
                    {
                        Domain = domain,
                        Team = team,
                        Mode = mode,
                        UpdatedAt = currentAt,
                        Transitions = transitions,
                    },
                ],
            };
            File.WriteAllText(TeamModeStore.ResolvePath(_root), JsonSerializer.Serialize(state));
        }

    public void WritePacketAndPublishArtifact(string? sourceArtifact = null, bool? knowledgeRequired = null,
        string? packetUnit = null, string? publishUnit = null, string? rulingArtifact = null, bool guideDeclared = false,
        string packetDomain = "intent-cli")
    {
        var directory = Path.Combine(_root, ".intent-cli", "issues", "G855");
        Directory.CreateDirectory(directory);
        var packetYaml = "schema_version: 1\nimplementation_issue_packet:\n  source_execution_unit: "
            + (packetUnit ?? "G855") + "\n  domain: " + packetDomain + "\n  target_repo: J-Tech-Japan/intent-system\n";
        if (sourceArtifact is not null) packetYaml += "  source_artifact: \"" + sourceArtifact + "\"\n";
        if (rulingArtifact is not null) packetYaml += "  ruling_artifact: \"" + rulingArtifact + "\"\n";
        if (knowledgeRequired is { } required)
            packetYaml += "knowledge_updates:\n  intent_tree:\n    required: " + required.ToString().ToLowerInvariant() + "\n    target_paths: []\n";
        if (guideDeclared)
            packetYaml += "guide_reachability:\n  no_role_facing_surface: false\n  routes:\n    - guide_surface: \"guide solo-conductor\"\n      role: architect\n      target_surface: \"unit status\"\n";
        File.WriteAllText(Path.Combine(directory, "packet.yaml"), packetYaml);
            var artifact = new IssuePublishArtifact
            {
                ExecutionUnit = publishUnit ?? "G855",
                PublishStatus = "published",
                PacketPath = ".intent-cli/issues/G855/packet.yaml",
                IssueBodyPath = ".intent-cli/issues/G855/github-body.md",
                CreatedIssueNumber = 1862,
                CreatedIssueUrl = "https://github.com/J-Tech-Japan/intent-system/issues/1862",
                PublishedLabelName = "intent-target",
                LifecycleState = "pr-created",
                LinkedPrNumber = 1864,
                LinkedPrUrl = "https://github.com/J-Tech-Japan/intent-system/pull/1864",
            };
            File.WriteAllText(Path.Combine(directory, "publish.yaml"), IssuePublishArtifactYaml.Serialize(artifact));
        }

        public void WriteWritebackRecord(string? role, string fileName)
        {
            var directory = Path.Combine(_root, KnowledgeWriteBackRecord.RecordRootRelativePath, "G855");
            if (role is not null) directory = Path.Combine(directory, RoleScopedCloseoutRecordStore.RoleRecordsDirectoryName);
            Directory.CreateDirectory(directory);
            var record = new KnowledgeWriteBackRecord
            {
                ArtifactKind = KnowledgeWriteBackRecord.ArtifactKindValue,
                ExecutionUnit = "G855",
                Role = role,
                HostCommit = HeadSha,
                RecordedAt = DateTimeOffset.Parse("2026-10-07T00:00:00Z"),
                Targets = [],
            };
            File.WriteAllText(Path.Combine(directory, fileName), KnowledgeWriteBackRecord.Serialize(record));
        }

        public void WriteGuideRecord(string recorderRole, IReadOnlyList<string> audiences, bool roleSlot = false, string? fileName = null)
        {
            var directory = Path.Combine(_root, GuideReachabilityRecord.RecordRootRelativePath, "G855");
            if (roleSlot) directory = Path.Combine(directory, RoleScopedCloseoutRecordStore.RoleRecordsDirectoryName);
            Directory.CreateDirectory(directory);
            var record = new GuideReachabilityRecord
            {
                ArtifactKind = GuideReachabilityRecord.ArtifactKindValue,
                ExecutionUnit = "G855",
                Role = recorderRole,
                HostCommit = HeadSha,
                RecordedAt = DateTimeOffset.Parse("2026-10-07T00:00:00Z"),
                GuideSurfaces = ["guide solo-conductor"],
                Roles = audiences,
            };
            File.WriteAllText(Path.Combine(directory, fileName ?? "record.json"), GuideReachabilityRecord.Serialize(record));
        }

        public void WriteBugSourceChain(string bugId, string sourceIssueUrl, bool duplicate = false, bool corruptPlan = false,
            string? linkedIssueUrl = null, IReadOnlyList<string>? linkedUnits = null, IReadOnlyList<string>? resolvedPacketRefs = null)
        {
            var bugs = Path.Combine(_root, ".intent-cli", "bugs");
            Directory.CreateDirectory(bugs);
            var report = new BugReportArtifact
            {
                DomainSlug = "intent-cli",
                BugId = bugId,
                Title = "Source issue chain fixture",
                ReportSource = sourceIssueUrl,
                ProblemStatement = "A source issue for a command-level status test.",
                SuspectedFailureLocus = "status reader",
                OriginalInstructionRefs = [],
                AffectedIntentRefs = [],
                AffectedRuleSpecRefs = ["INTENT-1857-STATUS"],
                ClarificationCandidates = [],
                LinkedExecutionUnits = linkedUnits ?? [],
                LinkedIssueRefs = [linkedIssueUrl ?? sourceIssueUrl],
                LinkedPrRefs = [],
                LinkedReviewRefs = [],
            };
            var reportRef = BugReportArtifactPathResolver.Resolve(bugId);
            var triageRef = BugTriageArtifactPathResolver.Resolve(bugId);
            var planRef = BugExecutionArtifactPathResolver.Resolve(bugId);
            File.WriteAllText(Path.Combine(bugs, bugId + ".report.yaml"), BugReportArtifactYaml.Serialize(report));
            if (duplicate)
            {
                var second = report with { BugId = bugId + "-duplicate" };
                File.WriteAllText(Path.Combine(bugs, second.BugId + ".report.yaml"), BugReportArtifactYaml.Serialize(second));
            }
            var triage = new BugTriageArtifact
            {
                BugId = bugId,
                ReportRef = reportRef,
                TriageClassification = "rule-gap",
                DownstreamAction = "intent-only",
                ClarificationRequired = false,
                ClarificationReasons = [],
                OriginalInstructionRootRefs = ["INTENT-1857-STATUS"],
                LinkedReviewRefs = [],
                ResolvedExecutionUnits = [],
                ResolvedImplementationRefs = [],
                ResolvedReviewContextRefs = [],
                ResolvedPacketRefs = resolvedPacketRefs ?? [],
                UnresolvedExecutionUnits = [],
                ImplementationRepairCandidates = [],
                IntentRepairCandidates = ["INTENT-1857-STATUS"],
            };
            File.WriteAllText(Path.Combine(bugs, bugId + ".triage.yaml"), BugTriageArtifactYaml.Serialize(triage));
            var plan = new BugExecutionArtifact
            {
                BugId = bugId,
                ReportRef = reportRef,
                TriageRef = triageRef,
                DownstreamAction = "intent-only",
                ResolvedImplementationRefs = [],
                ResolvedReviewContextRefs = [],
                ResolvedPacketRefs = corruptPlan ? [".intent-cli/issues/G854/packet.yaml"] : resolvedPacketRefs ?? [],
                ImplementationTaskCandidates = [],
                IntentTaskCandidates = ["INTENT-1857-STATUS"],
                ClarificationRequired = false,
                ReadyToLaunch = false,
            };
            File.WriteAllText(Path.Combine(bugs, bugId + ".plan.yaml"), BugExecutionArtifactYaml.Serialize(plan));
        }

        public void WriteActualBugSourceChain(string bugId, string sourceIssueUrl)
        {
            var issueDirectory = Path.Combine(_root, ".intent-cli", "issues", "G855");
            File.WriteAllText(Path.Combine(issueDirectory, "implementation.md"), "# G855 implementation\n");
            File.WriteAllText(Path.Combine(issueDirectory, "review-context.md"), "# G855 review context\n");
            File.WriteAllText(Path.Combine(issueDirectory, "github-body.md"), "# G855 issue body\n");

            using var reportOutput = new StringWriter();
            Assert.Equal(0, BugReportCommand.Execute(Context,
                ["intent-cli", bugId, "--title", "G859 source report fixture", "--text", "A source issue report produced by the canonical writer.",
                    "--instruction-refs", "INTENT-1857-ADOPTION", "--affected-rule-spec-refs", "INTENT-1857-ADOPTION",
                    "--execution-units", "G855", "--issues", sourceIssueUrl], reportOutput));
            var reportPath = Path.Combine(_root, BugReportArtifactPathResolver.Resolve(bugId).Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(reportPath));
            var report = BugReportArtifactYaml.Deserialize(File.ReadAllText(reportPath));
            Assert.Equal("intent-cli", report.DomainSlug);
            Assert.Equal(bugId, report.BugId);
            Assert.Contains(sourceIssueUrl, report.LinkedIssueRefs, StringComparer.Ordinal);
            Assert.Contains("G855", report.LinkedExecutionUnits, StringComparer.Ordinal);

            using var triageOutput = new StringWriter();
            Assert.Equal(0, BugTriageCommand.Execute(Context, [bugId], triageOutput));
            var triagePath = Path.Combine(_root, BugTriageArtifactPathResolver.Resolve(bugId).Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(triagePath));
            var triage = BugTriageArtifactYaml.Deserialize(File.ReadAllText(triagePath));
            Assert.Equal(bugId, triage.BugId);
            Assert.Equal(BugReportArtifactPathResolver.Resolve(bugId), triage.ReportRef);
            Assert.False(triage.ClarificationRequired);
            Assert.Contains(".intent-cli/issues/G855/packet.yaml", triage.ResolvedPacketRefs, StringComparer.Ordinal);

            using var planOutput = new StringWriter();
            Assert.Equal(0, BugExecutionCommand.Execute(Context, [bugId], planOutput));
            var planPath = Path.Combine(_root, BugExecutionArtifactPathResolver.Resolve(bugId).Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(planPath));
            var plan = BugExecutionArtifactYaml.Deserialize(File.ReadAllText(planPath));
            Assert.Equal(bugId, plan.BugId);
            Assert.Equal(BugReportArtifactPathResolver.Resolve(bugId), plan.ReportRef);
            Assert.Equal(BugTriageArtifactPathResolver.Resolve(bugId), plan.TriageRef);
            Assert.True(plan.ReadyToLaunch);
            Assert.Contains(".intent-cli/issues/G855/packet.yaml", plan.ResolvedPacketRefs, StringComparer.Ordinal);
        }

        public string WriteActualBugReport(string bugId, string title, IReadOnlyList<string> issueRefs,
            IReadOnlyList<string> executionUnits)
        {
            var args = new List<string>
            {
                "intent-cli", bugId,
                "--title", title,
                "--text", "A canonical bug report emitted by the actual G859 writer fixture.",
                "--instruction-refs", "INTENT-1857-ADOPTION",
                "--affected-rule-spec-refs", "INTENT-1857-ADOPTION",
            };
            if (executionUnits.Count > 0)
            {
                args.Add("--execution-units");
                args.Add(string.Join(',', executionUnits));
            }
            if (issueRefs.Count > 0)
            {
                args.Add("--issues");
                args.Add(string.Join(',', issueRefs));
            }

            using var output = new StringWriter();
            Assert.Equal(0, BugReportCommand.Execute(Context, args.ToArray(), output));
            var reportRef = BugReportArtifactPathResolver.Resolve(bugId);
            var reportPath = Path.Combine(_root, reportRef.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(reportPath));
            var report = BugReportArtifactYaml.Deserialize(File.ReadAllText(reportPath));
            Assert.Equal("intent-cli", report.DomainSlug);
            Assert.Equal(bugId, report.BugId);
            Assert.Equal(title, report.Title);
            Assert.Equal(issueRefs, report.LinkedIssueRefs);
            Assert.Equal(executionUnits, report.LinkedExecutionUnits);
            return reportPath;
        }

        public void WriteReviewRecord()
        {
            var raw = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
            {
                verdict = "request-changes",
                head_sha = HeadSha,
                blocking_findings = new[] { new { file = "src/Unit.cs", line = 1, scenario = "fixture finding" } },
                notes = Array.Empty<string>(),
            }));
            var record = new CrossRuntimeReviewRecord
            {
                ArtifactKind = CrossRuntimeReviewRecord.ArtifactKindValue,
                Repo = "J-Tech-Japan/intent-system",
                Pr = 1864,
                HeadSha = HeadSha,
                ExecutionUnit = "G855",
                Domain = "intent-cli",
                Team = "intent-cli-dev",
                Kind = CrossRuntimeReviewRecord.KindImplementation,
                Runtime = "codex",
                RuntimeVersion = "codex-test",
                ConductorRuntime = "claude",
                Relation = CrossRuntimeReviewRecord.RelationCrossRuntime,
                Verdict = "request-changes",
                BlockingFindings = [new CrossRuntimeReviewFinding { File = "src/Unit.cs", Line = 1, Scenario = "fixture finding" }],
                Notes = [],
                RecordedAt = DateTimeOffset.Parse("2026-10-07T00:00:00Z"),
                RawVerdictFile = "",
                RawVerdictSha256 = Convert.ToHexString(SHA256.HashData(raw)).ToLowerInvariant(),
            };
            record = record with { RawVerdictFile = CrossRuntimeReviewStore.RawRelativePath(record) };
            var result = CrossRuntimeReviewStore.Write(_root, record, raw);
            Assert.True(result.Written, result.Error);
        }

        internal static JsonElement FindSubcheck(JsonElement root, string factId) => root.GetProperty("steps").EnumerateArray()
            .SelectMany(step => step.GetProperty("subchecks").EnumerateArray())
            .Single(fact => fact.GetProperty("id").GetString() == factId);

        internal static UnitStatusReadAdapter CreateRealAdapter(TempHost host, ClaimRecord? active, IReadOnlyList<ClaimHistoryRecord> history) =>
            new(new FakeGitHub(), new ClaimSnapshotGit(active, history));

        internal static ClaimRecord Claim(string actor, DateTimeOffset claimedAt) => new(
            "1", "execution-unit:G855", actor, "intent-cli-dev", claimedAt, HeadSha);

        internal static ClaimHistoryRecord History(string operation, string actor, string displacedHolder,
            DateTimeOffset claimedAt, DateTimeOffset recordedAt) => new(
            "1", operation, "execution-unit:G855", actor, "intent-cli-dev", recordedAt, "fixture", displacedHolder,
            "intent-cli-dev", claimedAt, HeadSha);

        private sealed class ClaimSnapshotGit : IGitRemoteCommandRunner
        {
            private const string MetadataOid = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
            private readonly Dictionary<string, string> _documents;
            private readonly string _claimPath = ClaimCommand.ClaimPath("execution-unit:G855");
            private readonly string _historyDirectory;
            private readonly List<string> _paths = [];

            public ClaimSnapshotGit(ClaimRecord? active, IReadOnlyList<ClaimHistoryRecord> history)
            {
                _historyDirectory = $"{ClaimCommand.ClaimsDirectory}/history/{Path.GetFileNameWithoutExtension(_claimPath)}";
                _documents = new Dictionary<string, string>(StringComparer.Ordinal);
                if (active is not null)
                {
                    _documents[_claimPath] = JsonSerializer.Serialize(active);
                    _paths.Add(_claimPath);
                }
                for (var index = 0; index < history.Count; index++)
                {
                    var path = $"{_historyDirectory}/{index:D4}-{history[index].Operation}.json";
                    _documents[path] = JsonSerializer.Serialize(history[index]);
                    _paths.Add(path);
                }
            }

            public List<IReadOnlyList<string>> Calls { get; } = [];

            public GitRemoteCommandResult Run(string workingDirectory, IReadOnlyList<string> arguments)
            {
                var args = arguments.ToArray();
                Calls.Add(args);
                var output = args[0] switch
                {
                    "rev-parse" when args.Contains("--abbrev-ref", StringComparer.Ordinal) => "feature/unit",
                    "rev-parse" when args.Contains("HEAD", StringComparer.Ordinal) => HeadSha,
                    "rev-parse" => MetadataOid,
                    "ls-tree" => string.Join('\0', _paths) + (_paths.Count == 0 ? "" : "\0"),
                    "show" when args.Length == 2 && args[1].StartsWith(MetadataOid + ":", StringComparison.Ordinal)
                        && _documents.TryGetValue(args[1][(MetadataOid.Length + 1)..], out var value) => value,
                    _ => throw new InvalidOperationException("Unexpected bounded claim snapshot request: " + string.Join(' ', args)),
                };
                return new GitRemoteCommandResult { ExitCode = 0, StdOut = output, StdErr = "" };
            }
        }

        private sealed class FakeGitHub : IGitHubCommandRunner
        {
            public GitHubCommandResult Run(IReadOnlyList<string> arguments)
            {
                var endpoint = arguments[3];
                var json = endpoint switch
                {
                    "repos/J-Tech-Japan/intent-system/pulls/1864" => "{\"number\":1864,\"head\":{\"sha\":\"" + HeadSha + "\"},\"merged\":false,\"merge_commit_sha\":null,\"labels\":[]}",
                    "repos/J-Tech-Japan/intent-system/issues/1862" => "{\"number\":1862,\"labels\":[]}",
                    var path when path.StartsWith("repos/J-Tech-Japan/intent-system/pulls/1864/reviews?", StringComparison.Ordinal) => "[]",
                    var path when path.StartsWith("repos/J-Tech-Japan/intent-system/commits/" + HeadSha + "/check-runs?", StringComparison.Ordinal) => "{\"total_count\":0,\"check_runs\":[]}",
                    var path when path.StartsWith("repos/J-Tech-Japan/intent-system/commits/" + HeadSha + "/statuses?", StringComparison.Ordinal) => "[]",
                    _ => throw new InvalidOperationException("Unexpected bounded status request: " + endpoint),
                };
                return new GitHubCommandResult { ExitCode = 0, StdOut = json, StdErr = "" };
            }
        }

        public void Dispose()
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
    }
}
