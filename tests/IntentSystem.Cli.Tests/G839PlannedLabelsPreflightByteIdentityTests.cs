using IntentSystem.Cli.Commands;

namespace IntentSystem.Cli.Tests;

[Collection(WorkerPrCommentPreflightSharedStateCollection.Name)]
public sealed class G839PlannedLabelsPreflightByteIdentityTests
{
    private static readonly string FixtureRoot = Path.Combine(
        RepoVersionPolicySource.RepoRoot(),
        "tests",
        "IntentSystem.Cli.Tests",
        "Fixtures",
        "G839",
        "base");

    [Fact]
    public void PlannedLabelsConsumer_MatchesBase()
    {
        const string fixtureId = "planned-labels-worker-pr-comment-preflight";
        var expected = File.ReadAllText(FixturePath(fixtureId));
        var actual = Capture();
        Assert.Equal(expected, actual);
    }

    [Fact(Skip = "Set G839_CAPTURE=1 and remove Skip while checked out at base ba496314; running on head overwrites committed base fixtures.")]
    public void Capture_PreflightBaseFixture()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("G839_CAPTURE"), "1", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Set G839_CAPTURE=1, remove the Skip attribute on this test, and run against base commit ba496314 "
                + "to refresh tests/IntentSystem.Cli.Tests/Fixtures/G839/base/*.txt. Do not capture on head.");
        }

        Directory.CreateDirectory(FixtureRoot);
        File.WriteAllText(
            Path.Combine(FixtureRoot, "planned-labels-worker-pr-comment-preflight.txt"),
            Capture());
    }

    internal static string Capture()
    {
        using var scope = new WorkerPrCommentPreflightSeams();
        using var workspace = new G839ByteIdentityHarness.CliWorkspace("worker-pr-comment-preflight-g839-");
        using var writer = new StringWriter();
        WorkerPrCommentPreflightCommand.Execute(
            workspace.Context,
            ["--repo", G839ByteIdentityHarness.Repo, "--pr", "616", "--format", "json"],
            writer);
        return writer.ToString();
    }

    private sealed class WorkerPrCommentPreflightSeams : IDisposable
    {
        public WorkerPrCommentPreflightSeams()
        {
            WorkerPrCommentPreflightCommand.PrLookupFactory = () => new G839ByteIdentityHarness.WorkerPrLookup();
            WorkerPrCommentPreflightCommand.IssueLookupFactory = () => new G839ByteIdentityHarness.WorkerIssueLookup();
            WorkerPrCommentPreflightCommand.CommentsLookupFactory = () => new G839ByteIdentityHarness.WorkerCommentsLookup();
            WorkerPrCommentPreflightCommand.NestedProviderLauncher = null;
        }

        public void Dispose()
        {
            WorkerPrCommentPreflightCommand.PrLookupFactory = null;
            WorkerPrCommentPreflightCommand.IssueLookupFactory = null;
            WorkerPrCommentPreflightCommand.CommentsLookupFactory = null;
            WorkerPrCommentPreflightCommand.NestedProviderLauncher = null;
        }
    }

    private static string FixturePath(string fixtureId) => Path.Combine(FixtureRoot, fixtureId + ".txt");
}
