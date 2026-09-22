using System.Globalization;
using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Infrastructure;
using IntentSystem.Cli.Models;
using IntentSystem.Supervisor.Models;
using IntentSystem.Supervisor.Serialization;

namespace IntentSystem.Cli.Tests;

[Collection(AutomationPrTransitionSharedStateCollection.Name)]
public sealed class G842Ac13GoldenTests
{
    private static readonly string FixtureRoot = Path.Combine(
        RepoVersionPolicySource.RepoRoot(), "tests", "IntentSystem.Cli.Tests", "Fixtures", "G842", "base", "ac13", "gated");

    [Fact]
    public void IssuePublishFlowDryRun_OnGatedRepo_MatchesMergeBaseGolden()
    {
        var actual = G842Ac13Capture.CapturePublishFlow();
        Assert.Equal(File.ReadAllText(Path.Combine(FixtureRoot, "publish-flow-dry-run.json")), actual);
    }

    [Fact]
    public void PrTransitionDryRun_OnSatisfiedGatedRepo_MatchesMergeBaseGolden()
    {
        var actual = G839ByteIdentityTests.CapturePrTransition("pr-transition-approved-satisfied-dry-run-json")
            .Replace("{{G839_WORKSPACE_ROOT}}", "{{G842_WORKSPACE_ROOT}}", StringComparison.Ordinal);
        Assert.Equal(File.ReadAllText(Path.Combine(FixtureRoot, "pr-transition-dry-run.json")), actual);
    }

    [Fact(Skip = "Run only in detached merge-base a24cf8ab; never capture on head.")]
    public void CaptureMergeBaseAc13Goldens()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("G842_CAPTURE"), "1", StringComparison.Ordinal))
        {
            return;
        }

        Directory.CreateDirectory(FixtureRoot);
        File.WriteAllText(Path.Combine(FixtureRoot, "publish-flow-dry-run.json"), G842Ac13Capture.CapturePublishFlow());
        File.WriteAllText(
            Path.Combine(FixtureRoot, "pr-transition-dry-run.json"),
            G839ByteIdentityTests.CapturePrTransition("pr-transition-approved-satisfied-dry-run-json")
                .Replace("{{G839_WORKSPACE_ROOT}}", "{{G842_WORKSPACE_ROOT}}", StringComparison.Ordinal));
    }
}

[Collection(AutomationPrTransitionSharedStateCollection.Name)]
internal sealed class G842Ac13Capture : IDisposable
{
    private const string Domain = "intent-cli";
    private const string Team = "intent-cli-dev";
    private const string Repo = "J-Tech-Japan/intent-system";
    private const string Unit = "G842";
    private static readonly DateTimeOffset FixedNow = new(2026, 9, 14, 12, 0, 0, TimeSpan.Zero);

    private readonly string root = Directory.CreateTempSubdirectory("g842-ac13-").FullName;

    private G842Ac13Capture()
    {
        G842CrossRuntimeReviewTests.ConfigureG842ClaimReader((_, scope) =>
            new ClaimOwnershipVerification(false, ClaimOwnershipVerification.StatusTeamRequired, scope, true, null, "implementation", Team, "held"));
        G835PublishFlowTests.ConfigureG842PublishFlowSeams(
            () => new ThrowingCreator(),
            () => new NoExistingIssueChecker(),
            () => FixedNow);

        Directory.CreateDirectory(Path.Combine(root, ".intent-cli"));
        File.WriteAllText(Path.Combine(root, ".intent-cli", "config.toml"),
            "default_domain = \"intent-cli\"\nartifact_root = \".intent-cli\"\n\n"
            + "[[cross_runtime_review.teams]]\n"
            + "team = \"intent-cli/intent-cli-dev\"\n"
            + "conductor_runtime = \"claude\"\n"
            + "repos = [\"J-Tech-Japan/intent-system\"]\n");
        WriteQueue();
        WritePacket();
    }

    internal static string CapturePublishFlow()
    {
        using var capture = new G842Ac13Capture();
        using var writer = new StringWriter();
        var context = new CliContext
        {
            RepoRoot = capture.root,
            Config = CliConfigLoader.Load(File.ReadAllText(Path.Combine(capture.root, ".intent-cli", "config.toml"))),
        };
        var exit = IssuePublishFlowCommand.Execute(
            context,
            [Unit, "--repo", Repo, "--domain", Domain, "--team", Team, "--format", "json"],
            writer);
        Assert.Equal(0, exit);
        return Normalize(writer.ToString(), capture.root);
    }

    public void Dispose()
    {
        G842CrossRuntimeReviewTests.ConfigureG842ClaimReader(null);
        G835PublishFlowTests.ConfigureG842PublishFlowSeams(null, null, null);
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private void WriteQueue()
    {
        var state = new QueueState
        {
            SchemaVersion = "1",
            UpdatedAt = FixedNow,
            Items =
            [
                new QueueItem
                {
                    ExecutionUnit = Unit,
                    Title = "G842 AC13 gated dry-run",
                    State = QueueItemState.Queued,
                    Dependencies = [],
                    BlockedBy = [],
                    ClarificationReturnPath = string.Empty,
                    PacketPaths = new PacketPaths
                    {
                        Implementation = $".intent-cli/issues/{Unit}/implementation.md",
                        ReviewContext = $".intent-cli/issues/{Unit}/review-context.md",
                        Yaml = $".intent-cli/issues/{Unit}/packet.yaml",
                    },
                    LinkedIssue = null,
                    WorkerRole = "child-impl",
                    ReviewRole = "host-review",
                    Priority = "normal",
                },
            ],
        };
        File.WriteAllText(Path.Combine(root, ".intent-cli", "queue-state.json"), QueueStateSerializer.Serialize(state));
    }

    private void WritePacket()
    {
        var directory = Path.Combine(root, ".intent-cli", "issues", Unit);
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "packet.yaml"),
            "implementation_issue_packet:\n"
            + "  issue_title: \"G842 AC13 gated dry-run\"\n"
            + "  domain: intent-cli\n"
            + "  target_repo: J-Tech-Japan/intent-system\n");
        File.WriteAllText(Path.Combine(directory, "github-body.md"),
            "# G842 AC13 gated dry-run\n\n"
            + "## Goal\n\nfixture\n\n"
            + "## Why This Slice Exists Now\n\nfixture\n\n"
            + "## Current Observed State\n\nfixture\n\n"
            + "## Accepted Baseline You May Assume\n\nfixture\n\n"
            + "## Target Repo / Path / Part\n\nfixture\n\n"
            + "## In Scope\n\n- fixture\n\n"
            + "## Out Of Scope\n\n- fixture\n\n"
            + "## Acceptance Criteria\n\n- fixture\n\n"
            + "## Verification\n\nfixture\n\n"
            + "## Related Links\n\n- fixture\n\n"
            + "## Base Branch Policy\n\nPolicy: `direct-main`\nExpected PR base branch: `main`\nOpen all child PRs against `main` directly.\n");
        File.WriteAllText(Path.Combine(directory, "review-context.md"), "# review\n");
        File.WriteAllText(Path.Combine(directory, "implementation.md"), "# implementation\n");
    }

    private static string Normalize(string value, string root)
    {
        var normalized = value.Replace(root, "{{G842_WORKSPACE_ROOT}}", StringComparison.Ordinal);
        var forwardRoot = root.Replace('\\', '/');
        return normalized.Replace(forwardRoot, "{{G842_WORKSPACE_ROOT}}", StringComparison.Ordinal);
    }

    private sealed class ThrowingCreator : IIssueCreator
    {
        public IssueCreateOutcome CreateIssue(string repo, string title, string bodyFilePath) =>
            throw new InvalidOperationException("dry-run must not create an issue");
    }

    private sealed class NoExistingIssueChecker : IGitHubExistingIssueChecker
    {
        public GitHubExistingIssueLookupResult FindExistingIssue(string repo, string executionUnit, string expectedTitle, string expectedBody) =>
            new() { Classification = GitHubExistingIssueClassification.None };
    }
}
