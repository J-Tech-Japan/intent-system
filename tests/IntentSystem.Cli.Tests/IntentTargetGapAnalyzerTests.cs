using IntentSystem.Cli.Commands;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G343 AC2 / AC4: unit coverage for the recovery analyzer that
/// detects "created issue without intent-target" gaps.
/// </summary>
public sealed class IntentTargetGapAnalyzerTests
{
    private const string Repo = "owner/repo";

    [Fact]
    public void Analyze_CanonicalArtifactAndOpenIssueWithoutIntentTarget_RecommendsRecovery()
    {
        var artifact = BuildArtifact(executionUnit: "SKS-G343", createdIssueNumber: 1234);
        var candidate = new IntentTargetGapCandidate
        {
            ExecutionUnit = "SKS-G343",
            PublishArtifactPath = ".intent-cli/issues/SKS-G343/publish.yaml",
            Artifact = artifact,
            ArtifactIsCanonical = true,
            IssueExistsOnGitHub = true,
            IssueState = "open",
            IssueLabels = Array.Empty<string>(),
        };

        var result = IntentTargetGapAnalyzer.Analyze(Repo, new[] { candidate });

        Assert.Single(result.SafeRepairs);
        Assert.Empty(result.UnsafeStops);
        var repair = result.SafeRepairs[0];
        Assert.Equal(IntentTargetGapAnalyzer.RepairType, repair.Type);
        Assert.Equal(1234, repair.IssueNumber);
        Assert.Contains("automation issue-publish", repair.RecoveryCommand, StringComparison.Ordinal);
        Assert.Contains("--issue 1234", repair.RecoveryCommand, StringComparison.Ordinal);
        Assert.Contains("--write", repair.RecoveryCommand, StringComparison.Ordinal);
        Assert.Equal(3, repair.Evidence.Count);
    }

    [Fact]
    public void Analyze_IssueAlreadyHasIntentTarget_NoGap()
    {
        // The host loop has already applied intent-target through
        // the normal lane. Nothing to recover.
        var artifact = BuildArtifact(executionUnit: "SKS-G343", createdIssueNumber: 1234);
        var candidate = new IntentTargetGapCandidate
        {
            ExecutionUnit = "SKS-G343",
            PublishArtifactPath = ".intent-cli/issues/SKS-G343/publish.yaml",
            Artifact = artifact,
            ArtifactIsCanonical = true,
            IssueExistsOnGitHub = true,
            IssueState = "open",
            IssueLabels = new[] { "intent-target" },
        };

        var result = IntentTargetGapAnalyzer.Analyze(Repo, new[] { candidate });

        Assert.Empty(result.SafeRepairs);
        Assert.Empty(result.UnsafeStops);
    }

    [Fact]
    public void Analyze_IssueHasIntentIssueInProgress_NoGap()
    {
        // Child loop has already claimed the issue → publish
        // boundary clearly succeeded by some other route. No
        // recovery action needed.
        var artifact = BuildArtifact(executionUnit: "SKS-G343", createdIssueNumber: 1234);
        var candidate = new IntentTargetGapCandidate
        {
            ExecutionUnit = "SKS-G343",
            PublishArtifactPath = ".intent-cli/issues/SKS-G343/publish.yaml",
            Artifact = artifact,
            ArtifactIsCanonical = true,
            IssueExistsOnGitHub = true,
            IssueState = "open",
            IssueLabels = new[] { "intent-issue-in-progress" },
        };

        var result = IntentTargetGapAnalyzer.Analyze(Repo, new[] { candidate });

        Assert.Empty(result.SafeRepairs);
        Assert.Empty(result.UnsafeStops);
    }

    [Fact]
    public void Analyze_TwoArtifactsClaimSameIssueNumber_AmbiguousUnsafeStop()
    {
        // AC4: ambiguous identity → hard structured stop.
        var artifactA = BuildArtifact(executionUnit: "SKS-G343", createdIssueNumber: 1234);
        var artifactB = BuildArtifact(executionUnit: "SKS-G999", createdIssueNumber: 1234);
        var candidates = new[]
        {
            new IntentTargetGapCandidate
            {
                ExecutionUnit = "SKS-G343",
                PublishArtifactPath = ".intent-cli/issues/SKS-G343/publish.yaml",
                Artifact = artifactA,
                ArtifactIsCanonical = true,
                IssueExistsOnGitHub = true,
                IssueState = "open",
                IssueLabels = Array.Empty<string>(),
            },
            new IntentTargetGapCandidate
            {
                ExecutionUnit = "SKS-G999",
                PublishArtifactPath = ".intent-cli/issues/SKS-G999/publish.yaml",
                Artifact = artifactB,
                ArtifactIsCanonical = true,
                IssueExistsOnGitHub = true,
                IssueState = "open",
                IssueLabels = Array.Empty<string>(),
            },
        };

        var result = IntentTargetGapAnalyzer.Analyze(Repo, candidates);

        Assert.Empty(result.SafeRepairs);
        Assert.Equal(2, result.UnsafeStops.Count);
        Assert.All(result.UnsafeStops, stop =>
            Assert.Equal(IntentTargetGapAnalyzer.UnsafeAmbiguousArtifacts, stop.Kind));
    }

    [Fact]
    public void Analyze_NonCanonicalArtifact_UnsafeStop()
    {
        // AC4: operator-edited publish.yaml stays a hard stop.
        var artifact = BuildArtifact(executionUnit: "SKS-G343", createdIssueNumber: 1234);
        var candidate = new IntentTargetGapCandidate
        {
            ExecutionUnit = "SKS-G343",
            PublishArtifactPath = ".intent-cli/issues/SKS-G343/publish.yaml",
            Artifact = artifact,
            ArtifactIsCanonical = false,
            IssueExistsOnGitHub = true,
            IssueState = "open",
            IssueLabels = Array.Empty<string>(),
        };

        var result = IntentTargetGapAnalyzer.Analyze(Repo, new[] { candidate });

        Assert.Empty(result.SafeRepairs);
        var stop = Assert.Single(result.UnsafeStops);
        Assert.Equal(IntentTargetGapAnalyzer.UnsafeNonCanonicalArtifact, stop.Kind);
        Assert.Contains(PublishYamlCanonicalAnalyzer.RecoveryCommand, stop.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Analyze_IssueClosed_UnsafeStop()
    {
        var artifact = BuildArtifact(executionUnit: "SKS-G343", createdIssueNumber: 1234);
        var candidate = new IntentTargetGapCandidate
        {
            ExecutionUnit = "SKS-G343",
            PublishArtifactPath = ".intent-cli/issues/SKS-G343/publish.yaml",
            Artifact = artifact,
            ArtifactIsCanonical = true,
            IssueExistsOnGitHub = true,
            IssueState = "closed",
            IssueLabels = Array.Empty<string>(),
        };

        var result = IntentTargetGapAnalyzer.Analyze(Repo, new[] { candidate });

        Assert.Empty(result.SafeRepairs);
        var stop = Assert.Single(result.UnsafeStops);
        Assert.Equal(IntentTargetGapAnalyzer.UnsafeIssueClosed, stop.Kind);
    }

    [Fact]
    public void Analyze_IssueMissing_UnsafeStop()
    {
        var artifact = BuildArtifact(executionUnit: "SKS-G343", createdIssueNumber: 1234);
        var candidate = new IntentTargetGapCandidate
        {
            ExecutionUnit = "SKS-G343",
            PublishArtifactPath = ".intent-cli/issues/SKS-G343/publish.yaml",
            Artifact = artifact,
            ArtifactIsCanonical = true,
            IssueExistsOnGitHub = false,
            IssueState = null,
            IssueLabels = Array.Empty<string>(),
        };

        var result = IntentTargetGapAnalyzer.Analyze(Repo, new[] { candidate });

        Assert.Empty(result.SafeRepairs);
        var stop = Assert.Single(result.UnsafeStops);
        Assert.Equal(IntentTargetGapAnalyzer.UnsafeIssueMissing, stop.Kind);
    }

    [Fact]
    public void Analyze_ArtifactUrlMismatchedRepo_UnsafeStop()
    {
        // Defense-in-depth: created_issue_url points at a different
        // GitHub repository than the --repo argument.
        var artifact = new IssuePublishArtifact
        {
            ExecutionUnit = "SKS-G343",
            PublishStatus = "issue-created",
            PacketPath = "intents/packets/SKS-G343/packet.md",
            IssueBodyPath = "intents/packets/SKS-G343/issue-body.md",
            CreatedIssueNumber = 1234,
            CreatedIssueUrl = "https://github.com/other/repo/issues/1234",
            PublishedLabelName = null,
        };
        var candidate = new IntentTargetGapCandidate
        {
            ExecutionUnit = "SKS-G343",
            PublishArtifactPath = ".intent-cli/issues/SKS-G343/publish.yaml",
            Artifact = artifact,
            ArtifactIsCanonical = true,
            IssueExistsOnGitHub = true,
            IssueState = "open",
            IssueLabels = Array.Empty<string>(),
        };

        var result = IntentTargetGapAnalyzer.Analyze(Repo, new[] { candidate });

        Assert.Empty(result.SafeRepairs);
        var stop = Assert.Single(result.UnsafeStops);
        Assert.Equal(IntentTargetGapAnalyzer.UnsafeRepoMismatch, stop.Kind);
    }

    [Fact]
    public void Analyze_NoArtifact_NotInScope()
    {
        var candidate = new IntentTargetGapCandidate
        {
            ExecutionUnit = "SKS-G343",
            PublishArtifactPath = ".intent-cli/issues/SKS-G343/publish.yaml",
            Artifact = null,
            ArtifactIsCanonical = false,
            IssueExistsOnGitHub = true,
            IssueState = "open",
            IssueLabels = Array.Empty<string>(),
        };

        var result = IntentTargetGapAnalyzer.Analyze(Repo, new[] { candidate });

        Assert.Empty(result.SafeRepairs);
        Assert.Empty(result.UnsafeStops);
    }

    [Fact]
    public void Analyze_BlankRepo_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            IntentTargetGapAnalyzer.Analyze("   ", Array.Empty<IntentTargetGapCandidate>()));
    }

    private static IssuePublishArtifact BuildArtifact(string executionUnit, int createdIssueNumber) =>
        new()
        {
            ExecutionUnit = executionUnit,
            PublishStatus = "issue-created",
            PacketPath = $"intents/packets/{executionUnit}/packet.md",
            IssueBodyPath = $"intents/packets/{executionUnit}/issue-body.md",
            CreatedIssueNumber = createdIssueNumber,
            CreatedIssueUrl = $"https://github.com/owner/repo/issues/{createdIssueNumber}",
            PublishedLabelName = null,
        };
}
