using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

public sealed class UnitStatusCommandG855Tests
{
    private const string HeadSha = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    private static string SnapshotFiles(string root) => string.Join("\n", Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
        .OrderBy(path => path, StringComparer.Ordinal)
        .Select(path => Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/') + "=" + Convert.ToHexString(File.ReadAllBytes(path))));

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
            LifecycleState = "drafted",
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
        Assert.Equal("claim-release-superseded-by-new-epoch", release.GetProperty("cause").GetString());
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
        host.WriteBugSourceChain("BUG-1861", sourceUrl);
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
        var packetYaml = "schema_version: 1\ndomain: " + packetDomain + "\nexecution_unit: " + (packetUnit ?? "G855") + "\nimplementation_issue_packet:\n  target_repo: J-Tech-Japan/intent-system\n";
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
            string? linkedIssueUrl = null, IReadOnlyList<string>? linkedUnits = null)
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
                ResolvedPacketRefs = [],
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
                ResolvedPacketRefs = corruptPlan ? [".intent-cli/issues/G854/packet.yaml"] : [],
                ImplementationTaskCandidates = [],
                IntentTaskCandidates = ["INTENT-1857-STATUS"],
                ClarificationRequired = false,
                ReadyToLaunch = false,
            };
            File.WriteAllText(Path.Combine(bugs, bugId + ".plan.yaml"), BugExecutionArtifactYaml.Serialize(plan));
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
