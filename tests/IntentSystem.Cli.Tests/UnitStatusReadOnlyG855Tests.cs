using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;
using IntentSystem.Supervisor.Models;
using IntentSystem.Supervisor.Serialization;

namespace IntentSystem.Cli.Tests;

[Collection("WorkerNextActionSharedState")]
public sealed class UnitStatusReadOnlyG855Tests
{
    private readonly string _repo = "J-Tech-Japan/intent-system";
    private readonly string _head = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private readonly string _metadataOid = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    private readonly int _publicIssue = 1862;
    private readonly int _pullRequest = 1863;

    [Theory]
    [InlineData("success", 0)]
    [InlineData("provenance", 0)]
    [InlineData("api-503", 1)]
    [InlineData("malformed-json", 1)]
    public void ExecuteCore_UsesRealLocalAndRemoteParsers_AndLeavesTheHostByteIdentical(string scenario, int expectedExit)
    {
        using var host = new HostFixture();
        host.Prepare(includeCurrentSoloMode: true);
        var git = new BoundedGitRunner(_head, _metadataOid);
        var github = new BoundedGitHubRunner(scenario, _repo, _head, _publicIssue, _pullRequest);
        var adapter = new UnitStatusReadAdapter(github, git);
        var before = host.SnapshotAllFilesAndGitMarkers();
        using var output = new StringWriter();

        var exit = UnitStatusCommand.ExecuteCore(
            host.Context,
            ["--execution-unit", "G855", "--domain", "intent-cli", "--team", "intent-cli-dev", "--format", "json"],
            output,
            adapter);

        var after = host.SnapshotAllFilesAndGitMarkers();
        Assert.Equal(before, after);
        Assert.Equal(0, git.ForbiddenCalls);
        Assert.Equal(0, github.ForbiddenCalls);
        Assert.Equal(4, git.Calls.Count);
        Assert.Equal(6, github.Calls.Count);
        Assert.True(expectedExit == exit, output.ToString());

        using var report = JsonDocument.Parse(output.ToString());
        var root = report.RootElement;
        Assert.Equal("G855", root.GetProperty("execution_unit").GetString());
        Assert.Equal(_repo, root.GetProperty("identity").GetProperty("repo").GetString());
        Assert.Equal(_publicIssue, root.GetProperty("identity").GetProperty("issue").GetInt32());
        Assert.Equal(_pullRequest, root.GetProperty("identity").GetProperty("pr").GetInt32());
        Assert.Equal(_head, root.GetProperty("identity").GetProperty("head_sha").GetString());
        Assert.Equal("current-recorded-entry", root.GetProperty("mode_basis").GetString());
        Assert.Equal("solo-conductor", root.GetProperty("team_mode").GetString());
        Assert.Equal(expectedExit, root.GetProperty("summary").GetProperty("observation_exit_code").GetInt32());
        Assert.Equal(26, root.GetProperty("summary").GetProperty("state_counts").EnumerateObject().Sum(item => item.Value.GetInt32()));

        if (scenario == "success")
        {
            Assert.Equal("completed", root.GetProperty("observation").GetProperty("github_snapshot").GetProperty("state").GetString());
            Assert.Equal("missing", FindFact(root, "issue-completion-marker").GetProperty("state").GetString());
            Assert.Equal("missing", FindFact(root, "posted-review").GetProperty("state").GetString());
            Assert.Equal("missing", FindFact(root, "observed-ci").GetProperty("state").GetString());
            Assert.Equal(1, root.GetProperty("summary").GetProperty("state_counts").GetProperty("unavailable").GetInt32());
            Assert.Equal(1, root.GetProperty("summary").GetProperty("unavailable_class_counts").GetProperty("provenance-limit").GetInt32());
        }
        else if (scenario == "provenance")
        {
            Assert.Equal("completed", root.GetProperty("observation").GetProperty("github_snapshot").GetProperty("state").GetString());
            AssertProvenanceLimit(FindFact(root, "posted-review"));
            AssertProvenanceLimit(FindFact(root, "delta-review"));
            var classes = root.GetProperty("summary").GetProperty("unavailable_class_counts");
            Assert.True(classes.GetProperty("provenance-limit").GetInt32() >= 2);
            Assert.Equal(0, root.GetProperty("summary").GetProperty("state_counts").GetProperty("unavailable").GetInt32()
                - classes.GetProperty("provenance-limit").GetInt32());
        }
        else if (scenario == "api-503")
        {
            Assert.Equal("unavailable", root.GetProperty("observation").GetProperty("github_snapshot").GetProperty("state").GetString());
            var marker = FindFact(root, "issue-completion-marker");
            Assert.Equal("unavailable", marker.GetProperty("state").GetString());
            Assert.Equal("github-read-failed", marker.GetProperty("cause").GetString());
            Assert.Equal("read-failure", marker.GetProperty("unavailable_class").GetString());
        }
        else
        {
            Assert.Equal("unavailable", root.GetProperty("observation").GetProperty("github_snapshot").GetProperty("state").GetString());
            var ci = FindFact(root, "observed-ci");
            Assert.Equal("unavailable", ci.GetProperty("state").GetString());
            Assert.Equal("github-json-invalid", ci.GetProperty("cause").GetString());
            Assert.Equal("read-failure", ci.GetProperty("unavailable_class").GetString());
        }
    }

    [Fact]
    public void ExecuteCore_WronglyTypedGithubNumberReturnsStructuredReportAndKeepsLocalFacts()
    {
        using var host = new HostFixture();
        host.Prepare(includeCurrentSoloMode: true);
        var git = new BoundedGitRunner(_head, _metadataOid);
        var github = new BoundedGitHubRunner("success", _repo, _head, _publicIssue, _pullRequest,
            responseOverride: endpoint => endpoint == $"repos/{_repo}/pulls/{_pullRequest}"
                ? new GitHubCommandResult
                {
                    ExitCode = 0,
                    StdOut = $"{{\"number\":\"{_pullRequest}\",\"head\":{{\"sha\":\"{_head}\"}},\"merged\":false,\"labels\":[]}}",
                    StdErr = string.Empty,
                }
                : null);
        var adapter = new UnitStatusReadAdapter(github, git);
        var before = host.SnapshotAllFilesAndGitMarkers();
        using var output = new StringWriter();

        var exit = UnitStatusCommand.ExecuteCore(
            host.Context,
            ["--execution-unit", "G855", "--domain", "intent-cli", "--team", "intent-cli-dev", "--format", "json"],
            output,
            adapter);

        Assert.Equal(before, host.SnapshotAllFilesAndGitMarkers());
        Assert.Equal(0, git.ForbiddenCalls);
        Assert.Equal(0, github.ForbiddenCalls);
        Assert.All(github.Calls, call =>
        {
            Assert.Equal("api", call[0]);
            Assert.Equal("--method", call[1]);
            Assert.Equal("GET", call[2]);
        });
        Assert.Single(github.Calls);
        Assert.Equal(1, exit);

        using var report = JsonDocument.Parse(output.ToString());
        var root = report.RootElement;
        Assert.Equal("G855", root.GetProperty("execution_unit").GetString());
        Assert.Equal(26, root.GetProperty("summary").GetProperty("state_counts").EnumerateObject().Sum(item => item.Value.GetInt32()));
        Assert.Equal("unavailable", root.GetProperty("observation").GetProperty("github_snapshot").GetProperty("state").GetString());
        Assert.Equal("github-api-error", root.GetProperty("observation").GetProperty("github_snapshot").GetProperty("cause").GetString());
        var ci = FindFact(root, "observed-ci");
        Assert.Equal("unavailable", ci.GetProperty("state").GetString());
        Assert.Equal("github-api-error", ci.GetProperty("cause").GetString());
        Assert.Equal("done", FindFact(root, "publication-artifact").GetProperty("state").GetString());
        Assert.Equal(1, root.GetProperty("summary").GetProperty("observation_exit_code").GetInt32());
    }

    [Fact]
    public void ExecuteCore_AbsentExactModeRefusesBeforeGithub_AndPreservesEveryHostByte()
    {
        using var host = new HostFixture();
        host.Prepare(includeCurrentSoloMode: false);
        var git = new BoundedGitRunner(_head, _metadataOid);
        var github = new BoundedGitHubRunner("success", _repo, _head, _publicIssue, _pullRequest);
        var adapter = new UnitStatusReadAdapter(github, git);
        var before = host.SnapshotAllFilesAndGitMarkers();
        using var output = new StringWriter();

        var exit = UnitStatusCommand.ExecuteCore(
            host.Context,
            ["--execution-unit", "G855", "--domain", "intent-cli", "--team", "intent-cli-dev", "--format", "json"],
            output,
            adapter);

        var after = host.SnapshotAllFilesAndGitMarkers();
        Assert.Equal(before, after);
        Assert.Equal(0, git.ForbiddenCalls);
        Assert.Equal(0, github.ForbiddenCalls);
        Assert.Equal(4, git.Calls.Count);
        Assert.Empty(github.Calls);
        Assert.Equal(1, exit);

        using var report = JsonDocument.Parse(output.ToString());
        var root = report.RootElement;
        Assert.Equal("team-mode-unrecorded", root.GetProperty("applicability").GetProperty("cause").GetString());
        Assert.Equal("applicability-unresolved", root.GetProperty("applicability").GetProperty("unavailable_class").GetString());
        Assert.Equal("not-observed", root.GetProperty("observation").GetProperty("github_snapshot").GetProperty("state").GetString());
        Assert.All(root.GetProperty("steps").EnumerateArray(), step => Assert.Equal("unavailable", step.GetProperty("state").GetString()));
        var facts = root.GetProperty("steps").EnumerateArray().SelectMany(step => step.GetProperty("subchecks").EnumerateArray()).ToArray();
        Assert.Equal(26, facts.Length);
        Assert.All(facts, fact =>
        {
            Assert.Equal("unavailable", fact.GetProperty("state").GetString());
            Assert.Equal("team-mode-unrecorded", fact.GetProperty("cause").GetString());
            Assert.Equal("applicability-unresolved", fact.GetProperty("unavailable_class").GetString());
        });
        Assert.Equal(26, root.GetProperty("summary").GetProperty("state_counts").GetProperty("unavailable").GetInt32());
        Assert.Equal(1, root.GetProperty("summary").GetProperty("observation_exit_code").GetInt32());
    }

    [Fact]
    public void ExecuteCore_MalformedModeIsUnavailableBeforeGithub_AndPreservesEveryHostByte()
    {
        using var host = new HostFixture();
        host.Prepare(includeCurrentSoloMode: false);
        host.WriteMalformedMode();
        var git = new BoundedGitRunner(_head, _metadataOid);
        var github = new BoundedGitHubRunner("success", _repo, _head, _publicIssue, _pullRequest);
        var adapter = new UnitStatusReadAdapter(github, git);
        var before = host.SnapshotAllFilesAndGitMarkers();
        using var output = new StringWriter();

        var exit = UnitStatusCommand.ExecuteCore(
            host.Context,
            ["--execution-unit", "G855", "--domain", "intent-cli", "--team", "intent-cli-dev", "--format", "json"],
            output,
            adapter);

        var after = host.SnapshotAllFilesAndGitMarkers();
        Assert.Equal(before, after);
        Assert.Equal(0, git.ForbiddenCalls);
        Assert.Equal(0, github.ForbiddenCalls);
        Assert.Equal(4, git.Calls.Count);
        Assert.Empty(github.Calls);
        Assert.Equal(1, exit);

        using var report = JsonDocument.Parse(output.ToString());
        AssertAllFactsUnavailable(report.RootElement, "team-mode-unreadable", "applicability-unresolved");
        Assert.Equal("not-observed", report.RootElement.GetProperty("observation").GetProperty("github_snapshot").GetProperty("state").GetString());
    }

    [Fact]
    public void ExecuteCore_CurrentDeliveryOverridesHistoricSolo_AndSkipsCorruptOptionalReceipt()
    {
        using var host = new HostFixture();
        host.Prepare(includeCurrentSoloMode: false);
        host.WriteModeHistory(TeamMode.Delivery, TeamMode.SoloConductor);
        host.WriteCorruptOptionalReviewRecord(_repo, _pullRequest);
        var git = new BoundedGitRunner(_head, _metadataOid);
        var github = new BoundedGitHubRunner("success", _repo, _head, _publicIssue, _pullRequest);
        var adapter = new UnitStatusReadAdapter(github, git);
        var before = host.SnapshotAllFilesAndGitMarkers();
        using var output = new StringWriter();

        var exit = UnitStatusCommand.ExecuteCore(
            host.Context,
            ["--execution-unit", "G855", "--domain", "intent-cli", "--team", "intent-cli-dev", "--format", "json"],
            output,
            adapter);

        var after = host.SnapshotAllFilesAndGitMarkers();
        Assert.Equal(before, after);
        Assert.Equal(0, git.ForbiddenCalls);
        Assert.Equal(0, github.ForbiddenCalls);
        Assert.Equal(4, git.Calls.Count);
        Assert.Empty(github.Calls);
        Assert.Equal(0, exit);

        using var report = JsonDocument.Parse(output.ToString());
        var root = report.RootElement;
        Assert.Equal("delivery", root.GetProperty("team_mode").GetString());
        Assert.Equal("current-recorded-entry", root.GetProperty("mode_basis").GetString());
        Assert.StartsWith("2026-10-03T", root.GetProperty("applicability").GetProperty("mode_entry_updated_at").GetString());
        Assert.All(root.GetProperty("steps").EnumerateArray(), step => Assert.Equal("not-applicable", step.GetProperty("state").GetString()));
        var facts = root.GetProperty("steps").EnumerateArray().SelectMany(step => step.GetProperty("subchecks").EnumerateArray()).ToArray();
        Assert.Equal(26, facts.Length);
        Assert.All(facts, fact =>
        {
            Assert.Equal("not-applicable", fact.GetProperty("state").GetString());
            Assert.Equal("recorded-non-solo-mode", fact.GetProperty("cause").GetString());
        });
        Assert.Equal(26, root.GetProperty("summary").GetProperty("state_counts").GetProperty("not-applicable").GetInt32());
        Assert.Equal(0, root.GetProperty("summary").GetProperty("observation_exit_code").GetInt32());
    }

    [Fact]
    public void ExecuteCore_CurrentSoloOverridesHistoricDelivery_AndObservesThePublishedIssueAndPr()
    {
        using var host = new HostFixture();
        host.Prepare(includeCurrentSoloMode: false);
        host.WriteModeHistory(TeamMode.SoloConductor, TeamMode.Delivery);
        var git = new BoundedGitRunner(_head, _metadataOid);
        var github = new BoundedGitHubRunner("success", _repo, _head, _publicIssue, _pullRequest);
        var adapter = new UnitStatusReadAdapter(github, git);
        var before = host.SnapshotAllFilesAndGitMarkers();
        using var output = new StringWriter();

        var exit = UnitStatusCommand.ExecuteCore(
            host.Context,
            ["--execution-unit", "G855", "--domain", "intent-cli", "--team", "intent-cli-dev", "--format", "json"],
            output,
            adapter);

        var after = host.SnapshotAllFilesAndGitMarkers();
        Assert.Equal(before, after);
        Assert.Equal(0, git.ForbiddenCalls);
        Assert.Equal(0, github.ForbiddenCalls);
        Assert.Equal(4, git.Calls.Count);
        Assert.Equal(6, github.Calls.Count);
        Assert.Equal(0, exit);

        using var report = JsonDocument.Parse(output.ToString());
        var root = report.RootElement;
        Assert.Equal("solo-conductor", root.GetProperty("team_mode").GetString());
        Assert.Equal("current-recorded-entry", root.GetProperty("mode_basis").GetString());
        Assert.Equal("completed", root.GetProperty("observation").GetProperty("github_snapshot").GetProperty("state").GetString());
        Assert.Equal(_repo, root.GetProperty("identity").GetProperty("repo").GetString());
        Assert.Equal(_publicIssue, root.GetProperty("identity").GetProperty("issue").GetInt32());
        Assert.Equal(_pullRequest, root.GetProperty("identity").GetProperty("pr").GetInt32());
    }

    [Fact]
    public void ExecuteCore_ClaimSnapshotFailurePrecedesCurrentDeliveryAndKeepsItsObservedPointer()
    {
        using var host = new HostFixture();
        host.Prepare(includeCurrentSoloMode: false);
        host.WriteModeHistory(TeamMode.Delivery, TeamMode.SoloConductor);
        var git = new BoundedGitRunner(_head, _metadataOid, failMetadataRef: true);
        var github = new BoundedGitHubRunner("success", _repo, _head, _publicIssue, _pullRequest);
        var adapter = new UnitStatusReadAdapter(github, git);
        var before = host.SnapshotAllFilesAndGitMarkers();
        using var output = new StringWriter();

        var exit = UnitStatusCommand.ExecuteCore(
            host.Context,
            ["--execution-unit", "G855", "--domain", "intent-cli", "--team", "intent-cli-dev", "--format", "json"],
            output,
            adapter);

        var after = host.SnapshotAllFilesAndGitMarkers();
        Assert.Equal(before, after);
        Assert.Equal(0, git.ForbiddenCalls);
        Assert.Equal(0, github.ForbiddenCalls);
        Assert.Equal(3, git.Calls.Count);
        Assert.Empty(github.Calls);
        Assert.Equal(1, exit);

        using var report = JsonDocument.Parse(output.ToString());
        var root = report.RootElement;
        AssertClaimReadFailureFacts(root, "claim-snapshot-unavailable");
        AssertClaimSnapshotEvidence(root);
    }

    [Fact]
    public void ExecuteCore_TeamUnresolvedRefusalPreservesClaimSnapshotReadFailureFacts()
    {
        using var host = new HostFixture();
        host.Prepare(includeCurrentSoloMode: false);
        host.WriteModeHistory(TeamMode.Delivery, TeamMode.SoloConductor);
        var git = new BoundedGitRunner(_head, _metadataOid, failMetadataRef: true);
        var github = new BoundedGitHubRunner("success", _repo, _head, _publicIssue, _pullRequest);
        var adapter = new UnitStatusReadAdapter(github, git);
        var before = host.SnapshotAllFilesAndGitMarkers();
        using var output = new StringWriter();

        var exit = UnitStatusCommand.ExecuteCore(
            host.Context,
            ["--execution-unit", "G855", "--domain", "intent-cli", "--format", "json"],
            output,
            adapter);

        Assert.Equal(before, host.SnapshotAllFilesAndGitMarkers());
        Assert.Equal(0, git.ForbiddenCalls);
        Assert.Empty(github.Calls);
        Assert.Equal(3, git.Calls.Count);
        Assert.Equal(1, exit);
        using var report = JsonDocument.Parse(output.ToString());
        var root = report.RootElement;
        Assert.Null(root.GetProperty("team").GetString());
        Assert.Equal("team-unresolved", root.GetProperty("applicability").GetProperty("cause").GetString());
        AssertClaimReadFailureFacts(root, "team-unresolved");
    }

    [Fact]
    public void ExecuteCore_ExplicitScopeDoesNotEstablishAnUnknownUnitBeforeDeliveryRefusal()
    {
        using var host = new HostFixture();
        host.Prepare(includeCurrentSoloMode: false);
        host.WriteModeHistory(TeamMode.Delivery, TeamMode.SoloConductor);
        var git = new BoundedGitRunner(_head, _metadataOid, executionUnit: "G999");
        var github = new BoundedGitHubRunner("success", _repo, _head, _publicIssue, _pullRequest);
        var adapter = new UnitStatusReadAdapter(github, git);
        var before = host.SnapshotAllFilesAndGitMarkers();
        using var output = new StringWriter();

        var exit = UnitStatusCommand.ExecuteCore(
            host.Context,
            ["--execution-unit", "G999", "--domain", "intent-cli", "--team", "intent-cli-dev", "--format", "json"],
            output,
            adapter);

        var after = host.SnapshotAllFilesAndGitMarkers();
        Assert.Equal(before, after);
        Assert.Equal(0, git.ForbiddenCalls);
        Assert.Equal(0, github.ForbiddenCalls);
        Assert.Equal(4, git.Calls.Count);
        Assert.Empty(github.Calls);
        Assert.Equal(1, exit);

        using var report = JsonDocument.Parse(output.ToString());
        var root = report.RootElement;
        AssertAllFactsUnavailable(root, "unit-identity-unestablished", "applicability-unresolved");
        Assert.Equal("G999", root.GetProperty("execution_unit").GetString());
        Assert.Equal("intent-cli", root.GetProperty("domain").GetString());
        Assert.Equal("intent-cli-dev", root.GetProperty("team").GetString());
        Assert.Null(root.GetProperty("identity").GetProperty("repo").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("identity").GetProperty("issue").ValueKind);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("identity").GetProperty("pr").ValueKind);
        Assert.Empty(root.GetProperty("identity").GetProperty("sources").EnumerateArray());
        AssertClaimSnapshotEvidence(root);
    }

    [Fact]
    public void ExecuteCore_UnconfiguredDefaultClaimSourceIsAProvenanceLimitWhileIndependentEvidenceContinues()
    {
        using var host = new HostFixture();
        host.Prepare(includeCurrentSoloMode: true);
        var git = new BoundedGitRunner(_head, _metadataOid);
        var github = new BoundedGitHubRunner("success", _repo, _head, _publicIssue, _pullRequest);
        var adapter = new UnitStatusReadAdapter(github, git);
        var before = host.SnapshotAllFilesAndGitMarkers();
        using var output = new StringWriter();

        var exit = UnitStatusCommand.ExecuteCore(
            host.ContextWithoutMetadataBranch,
            ["--execution-unit", "G855", "--domain", "intent-cli", "--team", "intent-cli-dev", "--format", "json"],
            output,
            adapter);

        var after = host.SnapshotAllFilesAndGitMarkers();
        Assert.Equal(before, after);
        Assert.Equal(0, git.ForbiddenCalls);
        Assert.Equal(2, git.Calls.Count);
        Assert.Contains(git.Calls, call => call.SequenceEqual(new[] { "rev-parse", "--verify", "HEAD" }));
        Assert.Contains(git.Calls, call => call.SequenceEqual(new[] { "rev-parse", "--abbrev-ref", "HEAD" }));
        Assert.Equal(0, github.ForbiddenCalls);
        Assert.Equal(6, github.Calls.Count);
        Assert.Equal(0, exit);

        using var report = JsonDocument.Parse(output.ToString());
        var root = report.RootElement;
        Assert.Equal("G855", root.GetProperty("execution_unit").GetString());
        Assert.Equal("intent-cli", root.GetProperty("domain").GetString());
        Assert.Equal("intent-cli-dev", root.GetProperty("team").GetString());
        Assert.Equal(_repo, root.GetProperty("identity").GetProperty("repo").GetString());
        Assert.Equal(_publicIssue, root.GetProperty("identity").GetProperty("issue").GetInt32());
        Assert.Equal(_pullRequest, root.GetProperty("identity").GetProperty("pr").GetInt32());
        Assert.Equal("solo-conductor", root.GetProperty("team_mode").GetString());
        Assert.Equal("current-recorded-entry", root.GetProperty("mode_basis").GetString());
        Assert.Equal(_head, root.GetProperty("observation").GetProperty("local_head_sha").GetString());
        Assert.Equal("feature/g855", root.GetProperty("observation").GetProperty("local_head_ref").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("observation").GetProperty("claim_metadata_ref").ValueKind);
        Assert.Equal("completed", root.GetProperty("observation").GetProperty("github_snapshot").GetProperty("state").GetString());
        Assert.Equal("done", FindFact(root, "publication-artifact").GetProperty("state").GetString());

        var claimIds = new[]
        {
            "design-claim-acquired", "design-claim-release", "implementation-claim-acquired", "implementation-claim-release",
        };
        foreach (var id in claimIds)
        {
            var fact = FindFact(root, id);
            Assert.Equal("unavailable", fact.GetProperty("state").GetString());
            Assert.Equal("local-claim-ref-unavailable", fact.GetProperty("cause").GetString());
            Assert.Equal("provenance-limit", fact.GetProperty("unavailable_class").GetString());
            AssertUnconfiguredClaimPointer(fact);
        }

        Assert.Equal("missing", FindFact(root, "host-pr-linkage").GetProperty("state").GetString());
        Assert.Equal("missing", FindFact(root, "posted-review").GetProperty("state").GetString());
        Assert.Contains(root.GetProperty("observation").GetProperty("warnings").EnumerateArray(), warning =>
            warning.GetString()!.Contains("local-claim-ref-unavailable", StringComparison.Ordinal));
        Assert.Equal(0, root.GetProperty("summary").GetProperty("observation_exit_code").GetInt32());
    }

    [Theory]
    [InlineData("head")]
    [InlineData("branch")]
    public void ExecuteCore_UnconfiguredClaimSourceFreshnessReadFailureRemainsReadFailure(string failingRead)
    {
        using var host = new HostFixture();
        host.Prepare(includeCurrentSoloMode: true);
        var git = new BoundedGitRunner(_head, _metadataOid, failFreshnessRead: failingRead);
        var github = new BoundedGitHubRunner("success", _repo, _head, _publicIssue, _pullRequest);
        using var output = new StringWriter();

        var exit = UnitStatusCommand.ExecuteCore(
            host.ContextWithoutMetadataBranch,
            ["--execution-unit", "G855", "--domain", "intent-cli", "--team", "intent-cli-dev", "--format", "json"],
            output,
            new UnitStatusReadAdapter(github, git));

        Assert.Equal(1, exit);
        Assert.Equal(2, git.Calls.Count);
        Assert.Empty(github.Calls);
        using var report = JsonDocument.Parse(output.ToString());
        var root = report.RootElement;
        Assert.Equal("claim-snapshot-unavailable", root.GetProperty("applicability").GetProperty("cause").GetString());
        Assert.Equal("read-failure", root.GetProperty("applicability").GetProperty("unavailable_class").GetString());
        var observation = root.GetProperty("observation");
        Assert.Equal(failingRead == "head" ? JsonValueKind.Null : JsonValueKind.String,
            observation.GetProperty("local_head_sha").ValueKind);
        Assert.Equal(failingRead == "branch" ? JsonValueKind.Null : JsonValueKind.String,
            observation.GetProperty("local_head_ref").ValueKind);
        Assert.Contains(root.GetProperty("observation").GetProperty("warnings").EnumerateArray(), warning =>
            warning.GetString()!.Contains("no configured local metadata branch", StringComparison.Ordinal));
        Assert.Contains(root.GetProperty("observation").GetProperty("warnings").EnumerateArray(), warning =>
            warning.GetString()!.Contains("Local HEAD/ref freshness read failure", StringComparison.Ordinal));
        foreach (var id in new[]
        {
            "design-claim-acquired", "design-claim-release", "implementation-claim-acquired", "implementation-claim-release",
        })
        {
            var fact = FindFact(root, id);
            Assert.Equal("local-claim-ref-unavailable", fact.GetProperty("cause").GetString());
            Assert.Equal("read-failure", fact.GetProperty("unavailable_class").GetString());
            AssertUnconfiguredClaimPointer(fact);
        }
    }

    [Theory]
    [InlineData("shorthand")]
    [InlineData("url")]
    public void ExecuteCore_RecognizesCanonicalIssuePublishFlowEventAndArtifact(string descriptorKind)
    {
        using var host = new HostFixture();
        host.Prepare(includeCurrentSoloMode: true, includePullRequestLink: false);
        host.WriteIssueCreatedPublishArtifact(_repo, _publicIssue);
        host.WriteQueueLinkedIssue("intent-cli", _repo, _publicIssue);
        host.WriteRunEvent("intent-cli", _repo, new RunEvent
        {
            Ts = DateTimeOffset.Parse("2026-10-07T00:00:00Z"),
            ExecutionUnit = "G855",
            Event = "issue-created",
            By = "issue-publish-flow",
            LinkedIssue = descriptorKind == "shorthand"
                ? $"{_repo}#{_publicIssue}"
                : $"https://github.com/{_repo}/issues/{_publicIssue}",
            Reason = $"https://github.com/{_repo}/issues/{_publicIssue}",
        });
        var git = new BoundedGitRunner(_head, _metadataOid);
        var github = new BoundedGitHubRunner("success", _repo, _head, _publicIssue, _pullRequest);
        var before = host.SnapshotAllFilesAndGitMarkers();
        using var output = new StringWriter();

        var exit = UnitStatusCommand.ExecuteCore(
            host.Context,
            ["--execution-unit", "G855", "--domain", "intent-cli", "--team", "intent-cli-dev", "--format", "json"],
            output,
            new UnitStatusReadAdapter(github, git));

        Assert.Equal(before, host.SnapshotAllFilesAndGitMarkers());
        Assert.Equal(4, git.Calls.Count);
        Assert.Single(github.Calls);
        Assert.Equal(0, exit);
        using var report = JsonDocument.Parse(output.ToString());
        var root = report.RootElement;
        Assert.Equal(_repo, root.GetProperty("identity").GetProperty("repo").GetString());
        Assert.Equal(_publicIssue, root.GetProperty("identity").GetProperty("issue").GetInt32());
        Assert.Equal("completed", root.GetProperty("observation").GetProperty("github_snapshot").GetProperty("state").GetString());
        var artifact = FindFact(root, "publication-artifact");
        Assert.Equal("done", artifact.GetProperty("state").GetString());
        Assert.Equal("issue-created-observed", artifact.GetProperty("cause").GetString());
        var run = FindFact(root, "issue-published-run");
        Assert.Equal("done", run.GetProperty("state").GetString());
        Assert.Equal("issue-created-run-recorded", run.GetProperty("cause").GetString());
        Assert.Contains(root.GetProperty("identity").GetProperty("sources").EnumerateArray(), source => source.GetString() == "runs.jsonl");
    }

    [Fact]
    public void ExecuteCore_IssueCreatedArtifactWithoutMatchingRunDoesNotInventRunEvidence()
    {
        using var host = new HostFixture();
        host.Prepare(includeCurrentSoloMode: true, includePullRequestLink: false);
        host.WriteIssueCreatedPublishArtifact(_repo, _publicIssue);
        host.WriteQueueLinkedIssue("intent-cli", _repo, _publicIssue);
        var git = new BoundedGitRunner(_head, _metadataOid);
        var github = new BoundedGitHubRunner("success", _repo, _head, _publicIssue, _pullRequest);
        using var output = new StringWriter();

        var exit = UnitStatusCommand.ExecuteCore(
            host.Context,
            ["--execution-unit", "G855", "--domain", "intent-cli", "--team", "intent-cli-dev", "--format", "json"],
            output,
            new UnitStatusReadAdapter(github, git));

        Assert.Equal(0, exit);
        Assert.Single(github.Calls);
        using var report = JsonDocument.Parse(output.ToString());
        Assert.Equal("done", FindFact(report.RootElement, "publication-artifact").GetProperty("state").GetString());
        Assert.Equal("missing", FindFact(report.RootElement, "issue-published-run").GetProperty("state").GetString());
    }

    [Fact]
    public void ExecuteCore_PublishedIssueStillAcceptsCanonicalIssuePublishedRunEvent()
    {
        using var host = new HostFixture();
        host.Prepare(includeCurrentSoloMode: true, includePullRequestLink: false);
        host.WriteQueueLinkedIssue("intent-cli", _repo, _publicIssue);
        host.WriteRunEvent("intent-cli", _repo, new RunEvent
        {
            Ts = DateTimeOffset.Parse("2026-10-07T00:00:00Z"),
            ExecutionUnit = "G855",
            Event = "issue-published",
            By = "test fixture",
            LinkedIssue = $"https://github.com/{_repo}/issues/{_publicIssue}",
            Reason = "published",
        });
        var git = new BoundedGitRunner(_head, _metadataOid);
        var github = new BoundedGitHubRunner("success", _repo, _head, _publicIssue, _pullRequest);
        using var output = new StringWriter();

        var exit = UnitStatusCommand.ExecuteCore(
            host.Context,
            ["--execution-unit", "G855", "--domain", "intent-cli", "--team", "intent-cli-dev", "--format", "json"],
            output,
            new UnitStatusReadAdapter(github, git));

        Assert.Equal(0, exit);
        using var report = JsonDocument.Parse(output.ToString());
        var run = FindFact(report.RootElement, "issue-published-run");
        Assert.Equal("done", run.GetProperty("state").GetString());
        Assert.Equal("issue-published-run-recorded", run.GetProperty("cause").GetString());
        Assert.Equal("publication-issue-published", FindFact(report.RootElement, "publication-artifact").GetProperty("cause").GetString());
    }

    [Fact]
    public void ExecuteCore_MissingPacketIsMissingEvidenceWhenPublishIdentityEstablishesUnit()
    {
        using var host = new HostFixture();
        host.Prepare(includeCurrentSoloMode: true, includePullRequestLink: false);
        File.Delete(Path.Combine(host.Root, ".intent-cli", "issues", "G855", "packet.yaml"));
        host.WriteIssueCreatedPublishArtifact(_repo, _publicIssue);
        var git = new BoundedGitRunner(_head, _metadataOid);
        var github = new BoundedGitHubRunner("success", _repo, _head, _publicIssue, _pullRequest);
        using var output = new StringWriter();

        var exit = UnitStatusCommand.ExecuteCore(
            host.Context,
            ["--execution-unit", "G855", "--domain", "intent-cli", "--team", "intent-cli-dev", "--format", "json"],
            output,
            new UnitStatusReadAdapter(github, git));

        Assert.Equal(0, exit);
        using var report = JsonDocument.Parse(output.ToString());
        var root = report.RootElement;
        foreach (var (id, cause) in new[]
        {
            ("packet-current-files", "packet-file-missing"),
            ("packet-current-validation", "packet-file-missing"),
            ("guide-declaration", "packet-declaration-absent"),
            ("bug-chain-or-ruling", "packet-file-missing"),
            ("architect-knowledge-writeback", "packet-file-missing"),
            ("orchestrator-knowledge-writeback", "packet-file-missing"),
            ("guide-reachability", "packet-file-missing"),
        })
        {
            var fact = FindFact(root, id);
            Assert.Equal("missing", fact.GetProperty("state").GetString());
            Assert.Equal(cause, fact.GetProperty("cause").GetString());
        }
        Assert.Equal("completed", root.GetProperty("observation").GetProperty("github_snapshot").GetProperty("state").GetString());
    }

    [Fact]
    public void ExecuteCore_UnreadablePacketRemainsStructuredReadFailure()
    {
        using var host = new HostFixture();
        host.Prepare(includeCurrentSoloMode: true, includePullRequestLink: false);
        var packetPath = Path.Combine(host.Root, ".intent-cli", "issues", "G855", "packet.yaml");
        File.Delete(packetPath);
        Directory.CreateDirectory(packetPath);
        var git = new BoundedGitRunner(_head, _metadataOid);
        var github = new BoundedGitHubRunner("success", _repo, _head, _publicIssue, _pullRequest);
        using var output = new StringWriter();

        var exit = UnitStatusCommand.ExecuteCore(
            host.Context,
            ["--execution-unit", "G855", "--domain", "intent-cli", "--team", "intent-cli-dev", "--format", "json"],
            output,
            new UnitStatusReadAdapter(github, git));

        Assert.Equal(1, exit);
        Assert.Empty(github.Calls);
        using var report = JsonDocument.Parse(output.ToString());
        Assert.Equal("packet-identity-unreadable", report.RootElement.GetProperty("applicability").GetProperty("cause").GetString());
        Assert.Equal("read-failure", report.RootElement.GetProperty("applicability").GetProperty("unavailable_class").GetString());
    }

    [Theory]
    [InlineData("other/repo#1862")]
    [InlineData("J-Tech-Japan/intent-system#not-a-number")]
    [InlineData("J-Tech-Japan/intent-system#1862#extra")]
    [InlineData("owner!/intent-system#1862")]
    [InlineData("https://github.com/owner!/intent-system/issues/1862")]
    public void ExecuteCore_CanonicalRunIssueParserStillRejectsMismatchedOrMalformedDescriptors(string linkedIssue)
    {
        using var host = new HostFixture();
        host.Prepare(includeCurrentSoloMode: true);
        host.WriteRunEvent("intent-cli", _repo, new RunEvent
        {
            Ts = DateTimeOffset.Parse("2026-10-07T00:00:00Z"),
            ExecutionUnit = "G855",
            Event = "issue-created",
            By = "automation issue publish-flow",
            LinkedIssue = linkedIssue,
            Reason = $"https://github.com/{_repo}/issues/{_publicIssue}",
        });
        var git = new BoundedGitRunner(_head, _metadataOid);
        var github = new BoundedGitHubRunner("success", _repo, _head, _publicIssue, _pullRequest);
        using var output = new StringWriter();

        var exit = UnitStatusCommand.ExecuteCore(
            host.Context,
            ["--execution-unit", "G855", "--domain", "intent-cli", "--team", "intent-cli-dev", "--format", "json"],
            output,
            new UnitStatusReadAdapter(github, git));

        Assert.Equal(1, exit);
        Assert.Empty(github.Calls);
        using var report = JsonDocument.Parse(output.ToString());
        Assert.Equal("identity-conflict", report.RootElement.GetProperty("applicability").GetProperty("cause").GetString());
        Assert.Equal("identity-conflict", report.RootElement.GetProperty("applicability").GetProperty("unavailable_class").GetString());
    }

    [Fact]
    public void ExecuteCore_CanonicalPacketDraftEstablishesUnitWithoutPublishOrConfiguredClaimRef()
    {
        using var host = new HostFixture();
        host.Prepare(includeCurrentSoloMode: true);
        var packetPath = Path.Combine(host.Root, ".intent-cli", "issues", "G855", "packet.yaml");
        var publishPath = Path.Combine(host.Root, ".intent-cli", "issues", "G855", "publish.yaml");
        File.Delete(packetPath);
        File.Delete(publishPath);

        using var draftOutput = new StringWriter();
        var draftExit = PacketDraftCommand.Execute(host.ContextWithoutMetadataBranch,
            ["--execution-unit", "G855", "--domain", "intent-cli", "--target-repo", _repo], draftOutput);
        Assert.Equal(0, draftExit);
        Assert.True(File.Exists(packetPath));
        Assert.False(File.Exists(publishPath));
        var packetText = File.ReadAllText(packetPath);
        Assert.Contains("implementation_issue_packet:", packetText, StringComparison.Ordinal);
        Assert.Contains("source_execution_unit: G855", packetText, StringComparison.Ordinal);
        Assert.Contains("domain: intent-cli", packetText, StringComparison.Ordinal);
        Assert.True(PacketYamlDocument.TryParse(packetText, out var draftedPacket, out var packetError), packetError);
        Assert.Equal("G855", draftedPacket!.Fields["implementation_issue_packet.source_execution_unit"]);
        Assert.False(draftedPacket.Fields.ContainsKey("execution_unit"));

        var git = new BoundedGitRunner(_head, _metadataOid);
        var github = new BoundedGitHubRunner("success", _repo, _head, _publicIssue, _pullRequest);
        var adapter = new UnitStatusReadAdapter(github, git);
        var before = host.SnapshotAllFilesAndGitMarkers();
        using var output = new StringWriter();

        var exit = UnitStatusCommand.ExecuteCore(
            host.ContextWithoutMetadataBranch,
            ["--execution-unit", "G855", "--domain", "intent-cli", "--team", "intent-cli-dev", "--format", "json"],
            output,
            adapter);

        Assert.Equal(before, host.SnapshotAllFilesAndGitMarkers());
        Assert.Equal(2, git.Calls.Count);
        Assert.Empty(github.Calls);
        Assert.Equal(0, exit);
        using var report = JsonDocument.Parse(output.ToString());
        var root = report.RootElement;
        Assert.Equal("G855", root.GetProperty("execution_unit").GetString());
        Assert.Equal("intent-cli", root.GetProperty("domain").GetString());
        Assert.Equal("intent-cli-dev", root.GetProperty("team").GetString());
        Assert.Equal(_repo, root.GetProperty("identity").GetProperty("repo").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("identity").GetProperty("issue").ValueKind);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("identity").GetProperty("pr").ValueKind);
        Assert.Equal("done", FindFact(root, "packet-current-validation").GetProperty("state").GetString());
        Assert.Equal("missing", FindFact(root, "publication-artifact").GetProperty("state").GetString());
        Assert.Equal(_head, root.GetProperty("observation").GetProperty("local_head_sha").GetString());
        Assert.Equal("feature/g855", root.GetProperty("observation").GetProperty("local_head_ref").GetString());
        foreach (var id in new[]
        {
            "design-claim-acquired", "design-claim-release", "implementation-claim-acquired", "implementation-claim-release",
        })
        {
            var fact = FindFact(root, id);
            Assert.Equal("local-claim-ref-unavailable", fact.GetProperty("cause").GetString());
            Assert.Equal("provenance-limit", fact.GetProperty("unavailable_class").GetString());
        }
    }

    [Fact]
    public void ExecuteCore_UnconfiguredClaimSourceWithoutIndependentTeamRetainsItsCauseAndEvidence()
    {
        using var host = new HostFixture();
        host.Prepare(includeCurrentSoloMode: true);
        var git = new BoundedGitRunner(_head, _metadataOid);
        var github = new BoundedGitHubRunner("success", _repo, _head, _publicIssue, _pullRequest);
        var adapter = new UnitStatusReadAdapter(github, git);
        var before = host.SnapshotAllFilesAndGitMarkers();
        using var output = new StringWriter();

        var exit = UnitStatusCommand.ExecuteCore(
            host.ContextWithoutMetadataBranch,
            ["--execution-unit", "G855", "--domain", "intent-cli", "--format", "json"],
            output,
            adapter);

        var after = host.SnapshotAllFilesAndGitMarkers();
        Assert.Equal(before, after);
        Assert.Equal(0, git.ForbiddenCalls);
        Assert.Equal(2, git.Calls.Count);
        Assert.Equal(0, github.ForbiddenCalls);
        Assert.Empty(github.Calls);
        Assert.Equal(1, exit);

        using var report = JsonDocument.Parse(output.ToString());
        var root = report.RootElement;
        Assert.Equal("team-unresolved", root.GetProperty("applicability").GetProperty("cause").GetString());
        Assert.Equal("applicability-unresolved", root.GetProperty("applicability").GetProperty("unavailable_class").GetString());
        Assert.Equal("not-observed", root.GetProperty("observation").GetProperty("github_snapshot").GetProperty("state").GetString());
        foreach (var id in new[]
                 {
                     "design-claim-acquired", "design-claim-release", "implementation-claim-acquired", "implementation-claim-release",
                 })
        {
            var fact = FindFact(root, id);
            Assert.Equal("unavailable", fact.GetProperty("state").GetString());
            Assert.Equal("local-claim-ref-unavailable", fact.GetProperty("cause").GetString());
            Assert.Equal("provenance-limit", fact.GetProperty("unavailable_class").GetString());
            AssertUnconfiguredClaimPointer(fact);
        }

        var facts = root.GetProperty("steps").EnumerateArray().SelectMany(step => step.GetProperty("subchecks").EnumerateArray()).ToArray();
        Assert.Equal(26, facts.Length);
        Assert.All(facts, fact => Assert.Equal("unavailable", fact.GetProperty("state").GetString()));
        var preservedClaimIds = new HashSet<string>(StringComparer.Ordinal)
        {
            "design-claim-acquired", "design-claim-release", "implementation-claim-acquired", "implementation-claim-release",
        };
        Assert.All(facts.Where(fact => !preservedClaimIds.Contains(fact.GetProperty("id").GetString()!)), fact =>
        {
            Assert.Equal("team-unresolved", fact.GetProperty("cause").GetString());
            Assert.Equal("applicability-unresolved", fact.GetProperty("unavailable_class").GetString());
        });
        Assert.Equal(26, root.GetProperty("summary").GetProperty("state_counts").GetProperty("unavailable").GetInt32());
        Assert.Contains(root.GetProperty("observation").GetProperty("warnings").EnumerateArray(), warning =>
            warning.GetString()!.Contains("local-claim-ref-unavailable", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(false, "missing", "issue-pr-created-marker-absent")]
    [InlineData(true, "done", "intent-pr-created-observed")]
    public void ExecuteCore_PublishedIssueWithoutPrIsSuccessfulAbsence_AndReadsOnlyTheIssue(bool markerPresent, string expectedMarkerState, string expectedMarkerCause)
    {
        using var host = new HostFixture();
        host.Prepare(includeCurrentSoloMode: true, includePullRequestLink: false);
        var git = new BoundedGitRunner(_head, _metadataOid);
        var github = new BoundedGitHubRunner("success", _repo, _head, _publicIssue, _pullRequest,
            allowPullRequestEndpoints: false, issueCompletionMarkerPresent: markerPresent);
        var adapter = new UnitStatusReadAdapter(github, git);
        var before = host.SnapshotAllFilesAndGitMarkers();
        using var output = new StringWriter();

        var exit = UnitStatusCommand.ExecuteCore(
            host.ContextWithoutMetadataBranch,
            ["--execution-unit", "G855", "--domain", "intent-cli", "--team", "intent-cli-dev", "--format", "json"],
            output,
            adapter);

        var after = host.SnapshotAllFilesAndGitMarkers();
        Assert.Equal(before, after);
        Assert.Equal(0, git.ForbiddenCalls);
        Assert.Equal(2, git.Calls.Count);
        Assert.Equal(0, github.ForbiddenCalls);
        Assert.Single(github.Calls);
        Assert.Equal(new[] { "api", "--method", "GET", $"repos/{_repo}/issues/{_publicIssue}" }, github.Calls[0]);
        Assert.Equal(0, exit);

        using var report = JsonDocument.Parse(output.ToString());
        var root = report.RootElement;
        Assert.Equal(_repo, root.GetProperty("identity").GetProperty("repo").GetString());
        Assert.Equal(_publicIssue, root.GetProperty("identity").GetProperty("issue").GetInt32());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("identity").GetProperty("pr").ValueKind);
        Assert.Equal("completed", root.GetProperty("observation").GetProperty("github_snapshot").GetProperty("state").GetString());

        var marker = FindFact(root, "issue-completion-marker");
        Assert.Equal(expectedMarkerState, marker.GetProperty("state").GetString());
        Assert.Equal(expectedMarkerCause, marker.GetProperty("cause").GetString());
        Assert.Equal("done", FindFact(root, "publication-artifact").GetProperty("state").GetString());
        Assert.Equal("missing", FindFact(root, "host-pr-linkage").GetProperty("state").GetString());
        foreach (var id in new[]
                 {
                     "recorded-review", "posted-review", "delta-review", "observed-ci", "approved-marker",
                     "approval-head-receipt", "pr-merged",
                 })
        {
            var fact = FindFact(root, id);
            Assert.Equal("missing", fact.GetProperty("state").GetString());
            Assert.Equal("pr-not-linked", fact.GetProperty("cause").GetString());
        }

        foreach (var id in new[]
                 {
                     "design-claim-acquired", "design-claim-release", "implementation-claim-acquired", "implementation-claim-release",
                 })
        {
            var fact = FindFact(root, id);
            Assert.Equal("unavailable", fact.GetProperty("state").GetString());
            Assert.Equal("local-claim-ref-unavailable", fact.GetProperty("cause").GetString());
            Assert.Equal("provenance-limit", fact.GetProperty("unavailable_class").GetString());
            AssertUnconfiguredClaimPointer(fact);
        }

        Assert.Equal(0, root.GetProperty("summary").GetProperty("observation_exit_code").GetInt32());
    }

    [Fact]
    public void ExecuteCore_PublishOnlyPrUrlDoesNotSatisfyQueuePrLinkage()
    {
        using var host = new HostFixture();
        host.Prepare(includeCurrentSoloMode: true, includePullRequestLink: true);
        var git = new BoundedGitRunner(_head, _metadataOid);
        var github = new BoundedGitHubRunner("success", _repo, _head, _publicIssue, _pullRequest);
        var before = host.SnapshotAllFilesAndGitMarkers();
        using var output = new StringWriter();

        var exit = UnitStatusCommand.ExecuteCore(
            host.Context,
            ["--execution-unit", "G855", "--domain", "intent-cli", "--team", "intent-cli-dev", "--format", "json"],
            output,
            new UnitStatusReadAdapter(github, git));

        Assert.Equal(before, host.SnapshotAllFilesAndGitMarkers());
        Assert.Equal(0, exit);
        using var report = JsonDocument.Parse(output.ToString());
        var root = report.RootElement;
        Assert.Equal("done", FindFact(root, "publication-artifact").GetProperty("state").GetString());
        var linkage = FindFact(root, "host-pr-linkage");
        Assert.Equal("missing", linkage.GetProperty("state").GetString());
        Assert.Equal("queue-pr-link-absent", linkage.GetProperty("cause").GetString());
    }

    [Fact]
    public void ExecuteCore_MatchingQueuePrLinkSatisfiesHostPrLinkage()
    {
        using var host = new HostFixture();
        host.Prepare(includeCurrentSoloMode: true, includePullRequestLink: true);
        host.WriteQueueLinkedPr($"https://github.com/{_repo}/pull/{_pullRequest}", _repo, _publicIssue);
        var git = new BoundedGitRunner(_head, _metadataOid);
        var github = new BoundedGitHubRunner("success", _repo, _head, _publicIssue, _pullRequest);
        var before = host.SnapshotAllFilesAndGitMarkers();
        using var output = new StringWriter();

        var exit = UnitStatusCommand.ExecuteCore(
            host.Context,
            ["--execution-unit", "G855", "--domain", "intent-cli", "--team", "intent-cli-dev", "--format", "json"],
            output,
            new UnitStatusReadAdapter(github, git));

        Assert.Equal(before, host.SnapshotAllFilesAndGitMarkers());
        Assert.Equal(0, exit);
        using var report = JsonDocument.Parse(output.ToString());
        var root = report.RootElement;
        Assert.Equal("done", FindFact(root, "publication-artifact").GetProperty("state").GetString());
        Assert.Equal("done", FindFact(root, "queue-seed").GetProperty("state").GetString());
        var linkage = FindFact(root, "host-pr-linkage");
        Assert.Equal("done", linkage.GetProperty("state").GetString());
        Assert.Equal("host-pr-linkage-present", linkage.GetProperty("cause").GetString());
        Assert.Contains(linkage.GetProperty("evidence").EnumerateArray(), evidence =>
            evidence.GetProperty("kind").GetString() == "queue-state"
            && evidence.GetProperty("url").GetString() == $"https://github.com/{_repo}/pull/{_pullRequest}");
    }

    [Fact]
    public void ExecuteCore_ConflictingQueuePrAndPublishPrRemainAnIdentityConflict()
    {
        using var host = new HostFixture();
        host.Prepare(includeCurrentSoloMode: true, includePullRequestLink: true);
        host.WriteQueueLinkedPr($"https://github.com/{_repo}/pull/1864", _repo, _publicIssue);
        var git = new BoundedGitRunner(_head, _metadataOid);
        var github = new BoundedGitHubRunner("success", _repo, _head, _publicIssue, _pullRequest);
        using var output = new StringWriter();

        var exit = UnitStatusCommand.ExecuteCore(
            host.Context,
            ["--execution-unit", "G855", "--domain", "intent-cli", "--team", "intent-cli-dev", "--format", "json"],
            output,
            new UnitStatusReadAdapter(github, git));

        Assert.Equal(1, exit);
        Assert.Empty(github.Calls);
        using var report = JsonDocument.Parse(output.ToString());
        var root = report.RootElement;
        Assert.Equal("identity-conflict", root.GetProperty("applicability").GetProperty("cause").GetString());
        Assert.Equal("identity-conflict", root.GetProperty("applicability").GetProperty("unavailable_class").GetString());
        Assert.Equal("unavailable", FindFact(root, "host-pr-linkage").GetProperty("state").GetString());
    }

    [Theory]
    [InlineData("issue-created", "issue-created", "done", "issue-created-observed", 0)]
    [InlineData("issue-created", null, "done", "issue-created-observed", 0)]
    [InlineData("published", "issue-created", "unavailable", "publication-status-identity-conflict", 1)]
    public void ExecuteCore_PublicationStatusAndLifecycleMustAgree(
        string publishStatus,
        string? lifecycleState,
        string expectedState,
        string expectedCause,
        int expectedExit)
    {
        using var host = new HostFixture();
        host.Prepare(includeCurrentSoloMode: true, includePullRequestLink: false);
        host.WritePublishArtifact(new IssuePublishArtifact
        {
            ExecutionUnit = "G855",
            PublishStatus = publishStatus,
            PacketPath = ".intent-cli/issues/G855/packet.yaml",
            IssueBodyPath = ".intent-cli/issues/G855/github-body.md",
            CreatedIssueNumber = _publicIssue,
            CreatedIssueUrl = $"https://github.com/{_repo}/issues/{_publicIssue}",
            PublishedLabelName = "intent-target",
            LifecycleState = lifecycleState,
        });
        var git = new BoundedGitRunner(_head, _metadataOid);
        var github = new BoundedGitHubRunner("success", _repo, _head, _publicIssue, _pullRequest,
            allowPullRequestEndpoints: false);
        var before = host.SnapshotAllFilesAndGitMarkers();
        using var output = new StringWriter();

        var exit = UnitStatusCommand.ExecuteCore(
            host.Context,
            ["--execution-unit", "G855", "--domain", "intent-cli", "--team", "intent-cli-dev", "--format", "json"],
            output,
            new UnitStatusReadAdapter(github, git));

        Assert.Equal(before, host.SnapshotAllFilesAndGitMarkers());
        Assert.Equal(expectedExit, exit);
        Assert.Single(github.Calls);
        Assert.Equal($"repos/{_repo}/issues/{_publicIssue}", github.Calls[0][3]);
        using var report = JsonDocument.Parse(output.ToString());
        var publication = FindFact(report.RootElement, "publication-artifact");
        Assert.Equal(expectedState, publication.GetProperty("state").GetString());
        Assert.Equal(expectedCause, publication.GetProperty("cause").GetString());
    }

    [Fact]
    public void ProgramMain_HostlessStatusIsStructuredAndHelpIsMetadataFreeWithoutWritingFiles()
    {
        using var host = new HostFixture();
        var child = Directory.CreateDirectory(Path.Combine(host.Root, "child"));
        using var directoryScope = new CurrentDirectoryScope(child.FullName);
        using var console = new ConsoleScope();
        var before = host.SnapshotAllFilesAndGitMarkers();

        var statusExit = Program.Main(["unit", "status", "--execution-unit", "G855", "--format", "json"]);
        var statusOutput = console.Output.ToString();
        console.Output.GetStringBuilder().Clear();
        var helpExit = Program.Main(["unit", "status", "--help"]);

        var after = host.SnapshotAllFilesAndGitMarkers();
        Assert.Equal(before, after);
        Assert.Equal(1, statusExit);
        Assert.Equal(0, helpExit);
        using var report = JsonDocument.Parse(statusOutput);
        var root = report.RootElement;
        Assert.Equal("host-context-unavailable", root.GetProperty("applicability").GetProperty("cause").GetString());
        Assert.Equal("applicability-unresolved", root.GetProperty("applicability").GetProperty("unavailable_class").GetString());
        Assert.Equal("not-observed", root.GetProperty("observation").GetProperty("github_snapshot").GetProperty("state").GetString());
        Assert.Contains("Read-only lifecycle evidence", console.Output.ToString(), StringComparison.Ordinal);
        Assert.Equal(string.Empty, console.Error.ToString());
    }

    private JsonElement FindFact(JsonElement root, string id) => root.GetProperty("steps").EnumerateArray()
        .SelectMany(step => step.GetProperty("subchecks").EnumerateArray())
        .Single(fact => fact.GetProperty("id").GetString() == id);

    private void AssertAllFactsUnavailable(JsonElement root, string cause, string unavailableClass)
    {
        Assert.Equal("unavailable", root.GetProperty("applicability").GetProperty("state").GetString());
        Assert.Equal(cause, root.GetProperty("applicability").GetProperty("cause").GetString());
        Assert.Equal(unavailableClass, root.GetProperty("applicability").GetProperty("unavailable_class").GetString());
        Assert.All(root.GetProperty("steps").EnumerateArray(), step => Assert.Equal("unavailable", step.GetProperty("state").GetString()));
        var facts = root.GetProperty("steps").EnumerateArray().SelectMany(step => step.GetProperty("subchecks").EnumerateArray()).ToArray();
        Assert.Equal(26, facts.Length);
        Assert.All(facts, fact =>
        {
            Assert.Equal("unavailable", fact.GetProperty("state").GetString());
            Assert.Equal(cause, fact.GetProperty("cause").GetString());
            Assert.Equal(unavailableClass, fact.GetProperty("unavailable_class").GetString());
            if (fact.GetProperty("repair_commands").GetArrayLength() == 0)
                Assert.False(string.IsNullOrWhiteSpace(fact.GetProperty("repair_unavailable_reason").GetString()));
        });
        Assert.Equal(26, root.GetProperty("summary").GetProperty("state_counts").GetProperty("unavailable").GetInt32());
        Assert.Equal(1, root.GetProperty("summary").GetProperty("observation_exit_code").GetInt32());
    }

    private void AssertClaimSnapshotEvidence(JsonElement root)
    {
        var claimFact = FindFact(root, "design-claim-acquired");
        var pointer = Assert.Single(claimFact.GetProperty("evidence").EnumerateArray());
        Assert.Equal("local-claim-snapshot", pointer.GetProperty("kind").GetString());
        Assert.Contains("refs/remotes/origin/metadata", pointer.GetProperty("provenance").GetString(), StringComparison.Ordinal);
    }

    private void AssertClaimReadFailureFacts(JsonElement root, string applicabilityCause)
    {
        var claimFactIds = new HashSet<string>(StringComparer.Ordinal)
        {
            "design-claim-acquired", "design-claim-release", "implementation-claim-acquired", "implementation-claim-release",
        };
        var facts = root.GetProperty("steps").EnumerateArray()
            .SelectMany(step => step.GetProperty("subchecks").EnumerateArray()).ToArray();
        Assert.Equal(26, facts.Length);
        Assert.All(facts, fact =>
        {
            Assert.Equal("unavailable", fact.GetProperty("state").GetString());
            if (claimFactIds.Contains(fact.GetProperty("id").GetString()!))
            {
                Assert.Equal("local-claim-ref-unavailable", fact.GetProperty("cause").GetString());
                Assert.Equal("read-failure", fact.GetProperty("unavailable_class").GetString());
                AssertClaimFactSnapshotEvidence(fact);
            }
            else
            {
                Assert.Equal(applicabilityCause, fact.GetProperty("cause").GetString());
                Assert.Equal(applicabilityCause == "claim-snapshot-unavailable" ? "read-failure" : "applicability-unresolved",
                    fact.GetProperty("unavailable_class").GetString());
            }
            if (fact.GetProperty("repair_commands").GetArrayLength() == 0)
                Assert.False(string.IsNullOrWhiteSpace(fact.GetProperty("repair_unavailable_reason").GetString()));
        });
        Assert.Equal(4, facts.Count(fact => claimFactIds.Contains(fact.GetProperty("id").GetString()!)));
        Assert.Equal(22, facts.Count(fact => fact.GetProperty("cause").GetString() == applicabilityCause));
    }

    private static void AssertClaimFactSnapshotEvidence(JsonElement fact)
    {
        var pointer = Assert.Single(fact.GetProperty("evidence").EnumerateArray());
        Assert.Equal("local-claim-snapshot", pointer.GetProperty("kind").GetString());
    }

    private void AssertUnconfiguredClaimPointer(JsonElement fact)
    {
        var pointer = Assert.Single(fact.GetProperty("evidence").EnumerateArray());
        Assert.Equal("local-claim-snapshot", pointer.GetProperty("kind").GetString());
        Assert.Equal(JsonValueKind.Null, pointer.GetProperty("path").ValueKind);
        Assert.Equal("G855", pointer.GetProperty("execution_unit").GetString());
        Assert.Contains("unconfigured", pointer.GetProperty("provenance").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("scope=execution-unit:G855", pointer.GetProperty("provenance").GetString(), StringComparison.Ordinal);
    }

    private void AssertProvenanceLimit(JsonElement fact)
    {
        Assert.Equal("unavailable", fact.GetProperty("state").GetString());
        Assert.Equal("legacy-review-identity-unrecorded", fact.GetProperty("cause").GetString());
        Assert.Equal("provenance-limit", fact.GetProperty("unavailable_class").GetString());
        Assert.Empty(fact.GetProperty("repair_commands").EnumerateArray());
    }

    private sealed class HostFixture : IDisposable
    {
        private readonly string _root = Directory.CreateTempSubdirectory("unit-status-readonly-g855-").FullName;
        private readonly string[] _trackedGitFiles =
        [
            ".git/HEAD",
            ".git/index",
            ".git/refs/heads/main",
            ".git/packed-refs",
            ".git/FETCH_HEAD",
        ];

        public string Root => _root;

        public CliContext Context => new()
        {
            RepoRoot = _root,
            Config = new CliConfig
            {
                Project = new ProjectConfig
                {
                    Domain = "intent-cli",
                    ArtifactRoot = ".intent-cli",
                    MetadataSourceBranch = "metadata",
                },
            },
        };

        public CliContext ContextWithoutMetadataBranch => new()
        {
            RepoRoot = _root,
            Config = new CliConfig
            {
                Project = new ProjectConfig
                {
                    Domain = "intent-cli",
                    ArtifactRoot = ".intent-cli",
                },
            },
        };

        public void Prepare(bool includeCurrentSoloMode, bool includePullRequestLink = true)
        {
            Directory.CreateDirectory(Path.Combine(_root, ".intent-cli", "issues", "G855"));
            Directory.CreateDirectory(Path.Combine(_root, ".git", "refs", "heads"));
            File.WriteAllText(Path.Combine(_root, ".git", "HEAD"), "ref: refs/heads/main\n");
            File.WriteAllBytes(Path.Combine(_root, ".git", "index"), [0x44, 0x49, 0x52, 0x43, 0x00, 0x01]);
            File.WriteAllText(Path.Combine(_root, ".git", "refs", "heads", "main"), "cccccccccccccccccccccccccccccccccccccccc\n");
            File.WriteAllText(Path.Combine(_root, ".git", "packed-refs"), "# pack-refs with: peeled fully-peeled\n");
            File.WriteAllText(Path.Combine(_root, ".git", "FETCH_HEAD"), "fixture-fetch-head-bytes\n");
            File.WriteAllText(
                Path.Combine(_root, ".intent-cli", "issues", "G855", "packet.yaml"),
                "schema_version: 1\nimplementation_issue_packet:\n  source_execution_unit: G855\n  domain: intent-cli\n  target_repo: J-Tech-Japan/intent-system\n");
            var artifact = new IssuePublishArtifact
            {
                ExecutionUnit = "G855",
                PublishStatus = "published",
                PacketPath = ".intent-cli/issues/G855/packet.yaml",
                IssueBodyPath = ".intent-cli/issues/G855/github-body.md",
                CreatedIssueNumber = 1862,
                CreatedIssueUrl = "https://github.com/J-Tech-Japan/intent-system/issues/1862",
                PublishedLabelName = "intent-target",
                LifecycleState = includePullRequestLink ? IssuePublishLifecycle.PrCreated : IssuePublishLifecycle.Published,
                LinkedPrNumber = includePullRequestLink ? 1863 : null,
                LinkedPrUrl = includePullRequestLink ? "https://github.com/J-Tech-Japan/intent-system/pull/1863" : null,
            };
            File.WriteAllText(
                Path.Combine(_root, ".intent-cli", "issues", "G855", "publish.yaml"),
                IssuePublishArtifactYaml.Serialize(artifact));

            if (includeCurrentSoloMode)
            {
                var timestamp = DateTimeOffset.Parse("2026-10-01T00:00:00Z");
                var state = new TeamModeState
                {
                    SchemaVersion = TeamModeStore.SchemaVersion,
                    Entries =
                    [
                        new TeamModeEntry
                        {
                            Domain = "intent-cli",
                            Team = "intent-cli-dev",
                            Mode = TeamMode.SoloConductor,
                            UpdatedAt = timestamp,
                            Transitions = [new TeamModeTransition { From = TeamMode.Default, To = TeamMode.SoloConductor, At = timestamp }],
                        },
                    ],
                };
                File.WriteAllText(TeamModeStore.ResolvePath(_root), JsonSerializer.Serialize(state));
            }
        }

        public void WritePublishArtifact(IssuePublishArtifact artifact) =>
            File.WriteAllText(
                Path.Combine(_root, ".intent-cli", "issues", "G855", "publish.yaml"),
                IssuePublishArtifactYaml.Serialize(artifact));

        public void WriteIssueCreatedPublishArtifact(string repo, int issueNumber)
        {
            var artifact = IssuePublishArtifactYaml.Deserialize(File.ReadAllText(
                Path.Combine(_root, ".intent-cli", "issues", "G855", "publish.yaml")));
            WritePublishArtifact(artifact with
            {
                PublishStatus = "issue-created",
                LifecycleState = IssuePublishLifecycle.IssueCreated,
                CreatedIssueNumber = issueNumber,
                CreatedIssueUrl = $"https://github.com/{repo}/issues/{issueNumber}",
                LinkedPrNumber = null,
                LinkedPrUrl = null,
            });
        }

        public void WriteRunEvent(string domain, string repo, RunEvent runEvent)
        {
            var path = RuntimeScopedStateResolver.GetScopedRunLogPath(_root, domain, repo);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, RunLogSerializer.SerializeLine(runEvent) + Environment.NewLine);
        }

        public void WriteQueueLinkedPr(string linkedPr, string repo, int issueNumber)
        {
            File.WriteAllText(
                Path.Combine(_root, ".intent-cli", "queue-state.json"),
                QueueStateSerializer.Serialize(new QueueState
                {
                    SchemaVersion = "1",
                    UpdatedAt = DateTimeOffset.Parse("2026-10-07T00:00:00Z"),
                    Items =
                    [
                        new QueueItem
                        {
                            ExecutionUnit = "G855",
                            Title = "G855 queue item",
                            State = QueueItemState.Active,
                            Dependencies = [],
                            BlockedBy = [],
                            ClarificationReturnPath = string.Empty,
                            PacketPaths = new PacketPaths
                            {
                                Yaml = ".intent-cli/issues/G855/packet.yaml",
                                Implementation = ".intent-cli/issues/G855/implementation.md",
                                ReviewContext = ".intent-cli/issues/G855/review-context.md",
                            },
                            LinkedIssue = new LinkedIssue
                            {
                                Repo = repo,
                                Number = issueNumber,
                                Url = $"https://github.com/{repo}/issues/{issueNumber}",
                            },
                            LinkedPr = linkedPr,
                            WorkerRole = "builder",
                            ReviewRole = "reviewer",
                            Priority = "normal",
                        },
                    ],
            }));
        }

        public void WriteQueueLinkedIssue(string domain, string repo, int issueNumber)
        {
            var path = RuntimeScopedStateResolver.ResolveQueueStatePathForRead(_root, domain, repo).Path;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, QueueStateSerializer.Serialize(new QueueState
            {
                SchemaVersion = "1",
                UpdatedAt = DateTimeOffset.Parse("2026-10-07T00:00:00Z"),
                Items =
                [
                    new QueueItem
                    {
                        ExecutionUnit = "G855",
                        Title = "G855 queue item",
                        State = QueueItemState.Active,
                        Dependencies = [],
                        BlockedBy = [],
                        ClarificationReturnPath = string.Empty,
                        PacketPaths = new PacketPaths
                        {
                            Yaml = ".intent-cli/issues/G855/packet.yaml",
                            Implementation = ".intent-cli/issues/G855/implementation.md",
                            ReviewContext = ".intent-cli/issues/G855/review-context.md",
                        },
                        LinkedIssue = new LinkedIssue
                        {
                            Repo = repo,
                            Number = issueNumber,
                            Url = $"https://github.com/{repo}/issues/{issueNumber}",
                        },
                        WorkerRole = "builder",
                        ReviewRole = "reviewer",
                        Priority = "normal",
                    },
                ],
            }));
        }

        public void WriteMalformedMode()
        {
            File.WriteAllText(TeamModeStore.ResolvePath(_root), "{ malformed team mode");
        }

        public void WriteModeHistory(string currentMode, string historicalMode)
        {
            var earlier = DateTimeOffset.Parse("2026-10-02T00:00:00Z");
            var current = DateTimeOffset.Parse("2026-10-03T00:00:00Z");
            IReadOnlyList<TeamModeTransition> transitions;
            if (currentMode == TeamMode.Delivery && historicalMode == TeamMode.SoloConductor)
            {
                transitions =
                [
                    new TeamModeTransition { From = TeamMode.Default, To = TeamMode.SoloConductor, At = earlier },
                    new TeamModeTransition { From = TeamMode.SoloConductor, To = TeamMode.Delivery, At = current },
                ];
            }
            else if (currentMode == TeamMode.SoloConductor && historicalMode == TeamMode.Delivery)
            {
                transitions = [new TeamModeTransition { From = TeamMode.Default, To = TeamMode.SoloConductor, At = current }];
            }
            else
            {
                throw new ArgumentException("The fixture accepts only the two requested current/history mode pairs.");
            }

            var entry = new TeamModeEntry
            {
                Domain = "intent-cli",
                Team = "intent-cli-dev",
                Mode = currentMode,
                UpdatedAt = transitions[^1].At,
                Transitions = transitions,
            };
            var state = new TeamModeState { SchemaVersion = TeamModeStore.SchemaVersion, Entries = [entry] };
            File.WriteAllText(TeamModeStore.ResolvePath(_root), JsonSerializer.Serialize(state));
        }

        public void WriteCorruptOptionalReviewRecord(string repo, int pr)
        {
            var directory = CrossRuntimeReviewPaths.PrDirectory(_root, repo, pr);
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "broken-record.json"), "{ malformed optional review receipt");
        }

        public string SnapshotAllFilesAndGitMarkers()
        {
            var rows = Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories)
                .Select(path => new
                {
                    Path = Path.GetRelativePath(_root, path).Replace('\\', '/'),
                    Bytes = Convert.ToHexString(File.ReadAllBytes(path)),
                })
                .OrderBy(row => row.Path, StringComparer.Ordinal)
                .Select(row => row.Path + "\0" + row.Bytes)
                .ToList();
            rows.AddRange(_trackedGitFiles.Select(path =>
            {
                var fullPath = Path.Combine(_root, path.Replace('/', Path.DirectorySeparatorChar));
                return "git-marker\0" + path + "\0" + (File.Exists(fullPath)
                    ? "present\0" + Convert.ToHexString(File.ReadAllBytes(fullPath))
                    : "absent");
            }));
            return string.Join("\n", rows.OrderBy(row => row, StringComparer.Ordinal));
        }

        public void Dispose()
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
    }

    private sealed class BoundedGitRunner : IGitRemoteCommandRunner
    {
        private readonly string _head;
        private readonly string _metadataOid;
        private readonly string _claimPath;
        private readonly string _historyDirectory;
        private readonly bool _failMetadataRef;
        private readonly string? _failFreshnessRead;

        public BoundedGitRunner(
            string head,
            string metadataOid,
            string executionUnit = "G855",
            bool failMetadataRef = false,
            string? failFreshnessRead = null)
        {
            _head = head;
            _metadataOid = metadataOid;
            _claimPath = ClaimCommand.ClaimPath("execution-unit:" + executionUnit);
            _historyDirectory = $"{ClaimCommand.ClaimsDirectory}/history/{Path.GetFileNameWithoutExtension(_claimPath)}";
            _failMetadataRef = failMetadataRef;
            _failFreshnessRead = failFreshnessRead;
        }

        public List<IReadOnlyList<string>> Calls { get; } = [];
        public int ForbiddenCalls { get; private set; }

        public GitRemoteCommandResult Run(string workingDirectory, IReadOnlyList<string> arguments)
        {
            var copy = arguments.ToArray();
            Calls.Add(copy);
            if (copy.Length == 3 && copy[0] == "rev-parse" && copy[1] == "--verify" && copy[2] == "HEAD")
                return _failFreshnessRead == "head" ? Failure("configured test HEAD read failure") : Success(_head);
            if (copy.Length == 3 && copy[0] == "rev-parse" && copy[1] == "--abbrev-ref" && copy[2] == "HEAD")
                return _failFreshnessRead == "branch" ? Failure("configured test branch read failure") : Success("feature/g855");
            if (copy.Length == 3 && copy[0] == "rev-parse" && copy[1] == "--verify"
                && copy[2] == "refs/remotes/origin/metadata^{commit}")
            {
                return _failMetadataRef
                    ? new GitRemoteCommandResult { ExitCode = 1, StdOut = string.Empty, StdErr = "configured metadata ref is unavailable" }
                    : Success(_metadataOid);
            }
            if (copy.Length == 8 && copy[0] == "ls-tree" && copy[1] == "-r" && copy[2] == "-z"
                && copy[3] == "--name-only" && copy[4] == _metadataOid && copy[5] == "--"
                && copy[6] == _claimPath && copy[7] == _historyDirectory)
                return Success(string.Empty);
            if (copy.Length == 2 && copy[0] == "show" && copy[1].StartsWith(_metadataOid + ":", StringComparison.Ordinal)
                && (copy[1][(_metadataOid.Length + 1)..] == _claimPath
                    || copy[1][(_metadataOid.Length + 1)..].StartsWith(_historyDirectory + "/", StringComparison.Ordinal)))
                return Success(string.Empty);

            ForbiddenCalls++;
            return new GitRemoteCommandResult { ExitCode = 1, StdOut = string.Empty, StdErr = "forbidden or unbounded git request" };
        }

        private GitRemoteCommandResult Success(string output) => new() { ExitCode = 0, StdOut = output, StdErr = string.Empty };
        private GitRemoteCommandResult Failure(string error) => new() { ExitCode = 1, StdOut = string.Empty, StdErr = error };
    }

    private sealed class BoundedGitHubRunner : IGitHubCommandRunner
    {
        private readonly string _scenario;
        private readonly string _repo;
        private readonly string _head;
        private readonly int _issue;
        private readonly int _pullRequest;
        private readonly bool _allowPullRequestEndpoints;
        private readonly bool _issueCompletionMarkerPresent;
        private readonly Func<string, GitHubCommandResult?>? _responseOverride;

        public BoundedGitHubRunner(string scenario, string repo, string head, int issue, int pullRequest,
            bool allowPullRequestEndpoints = true, bool issueCompletionMarkerPresent = false,
            Func<string, GitHubCommandResult?>? responseOverride = null)
        {
            _scenario = scenario;
            _repo = repo;
            _head = head;
            _issue = issue;
            _pullRequest = pullRequest;
            _allowPullRequestEndpoints = allowPullRequestEndpoints;
            _issueCompletionMarkerPresent = issueCompletionMarkerPresent;
            _responseOverride = responseOverride;
        }

        public List<IReadOnlyList<string>> Calls { get; } = [];
        public int ForbiddenCalls { get; private set; }

        public GitHubCommandResult Run(IReadOnlyList<string> arguments)
        {
            var copy = arguments.ToArray();
            Calls.Add(copy);
            if (copy.Length != 4 || copy[0] != "api" || copy[1] != "--method" || copy[2] != "GET" || !IsAllowedEndpoint(copy[3]))
            {
                ForbiddenCalls++;
                return new GitHubCommandResult { ExitCode = 1, StdOut = string.Empty, StdErr = "forbidden or unbounded GitHub request" };
            }

            var endpoint = copy[3];
            if (_responseOverride?.Invoke(endpoint) is { } overridden) return overridden;
            if (endpoint == $"repos/{_repo}/pulls/{_pullRequest}")
                return Json($"{{\"number\":{_pullRequest},\"head\":{{\"sha\":\"{_head}\"}},\"merged\":false,\"merge_commit_sha\":null,\"labels\":[]}}");
            if (endpoint == $"repos/{_repo}/issues/{_issue}")
            {
                return _scenario == "api-503"
                    ? new GitHubCommandResult { ExitCode = 1, StdOut = string.Empty, StdErr = "gh: HTTP 503 Service Unavailable" }
                    : Json(_issueCompletionMarkerPresent
                        ? $"{{\"number\":{_issue},\"labels\":[{{\"name\":\"intent-pr-created\"}}]}}"
                        : $"{{\"number\":{_issue},\"labels\":[]}}");
            }
            if (endpoint == $"repos/{_repo}/pulls/{_pullRequest}/reviews?per_page=100&page=1")
            {
                return _scenario == "provenance"
                    ? Json("[{\"id\":74,\"state\":\"COMMENTED\",\"commit_id\":null,\"body\":\"Reviewer: independent subagent review\\nNotes: historical prose without unit/head identity\",\"submitted_at\":\"2026-10-07T00:00:00Z\",\"user\":{\"login\":\"reviewer\"}}]")
                    : Json("[]");
            }
            if (endpoint == $"repos/{_repo}/commits/{_head}/check-runs?per_page=100&page=1&filter=all")
            {
                return _scenario == "malformed-json"
                    ? Json("{not-json")
                    : Json("{\"total_count\":0,\"check_runs\":[]}");
            }
            if (endpoint == $"repos/{_repo}/commits/{_head}/statuses?per_page=100&page=1") return Json("[]");

            ForbiddenCalls++;
            return new GitHubCommandResult { ExitCode = 1, StdOut = string.Empty, StdErr = "forbidden GitHub endpoint" };
        }

        private bool IsAllowedEndpoint(string endpoint) =>
            endpoint == $"repos/{_repo}/issues/{_issue}"
            || _allowPullRequestEndpoints && (
                endpoint == $"repos/{_repo}/pulls/{_pullRequest}"
                || endpoint == $"repos/{_repo}/pulls/{_pullRequest}/reviews?per_page=100&page=1"
                || endpoint == $"repos/{_repo}/commits/{_head}/check-runs?per_page=100&page=1&filter=all"
                || endpoint == $"repos/{_repo}/commits/{_head}/statuses?per_page=100&page=1");

        private GitHubCommandResult Json(string json) => new() { ExitCode = 0, StdOut = json, StdErr = string.Empty };
    }

    private sealed class CurrentDirectoryScope : IDisposable
    {
        private readonly string _original = Directory.GetCurrentDirectory();

        public CurrentDirectoryScope(string path) => Directory.SetCurrentDirectory(path);

        public void Dispose() => Directory.SetCurrentDirectory(_original);
    }

    private sealed class ConsoleScope : IDisposable
    {
        private readonly TextWriter _originalOut = Console.Out;
        private readonly TextWriter _originalError = Console.Error;

        public StringWriter Output { get; } = new();
        public StringWriter Error { get; } = new();

        public ConsoleScope()
        {
            Console.SetOut(Output);
            Console.SetError(Error);
        }

        public void Dispose()
        {
            Console.SetOut(_originalOut);
            Console.SetError(_originalError);
            Output.Dispose();
            Error.Dispose();
        }
    }
}
