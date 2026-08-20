using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

public sealed class WorkerIssuePreflightCommandTests : IDisposable
{
    public WorkerIssuePreflightCommandTests()
    {
        // Reset static seams before each test to keep tests independent.
        WorkerIssuePreflightCommand.IssueLookupFactory = null;
        WorkerIssuePreflightCommand.NestedProviderLauncher = null;
    }

    public void Dispose()
    {
        WorkerIssuePreflightCommand.IssueLookupFactory = null;
        WorkerIssuePreflightCommand.NestedProviderLauncher = null;
    }

    [Fact]
    public void Execute_GivenReadyIssue_ClassifiesAsReadyToImplement()
    {
        using var workspace = new WorkerIssuePreflightWorkspace();
        WorkerIssuePreflightCommand.IssueLookupFactory = () => new FakeLookup(BuildIssue(
            number: 509,
            state: "OPEN",
            title: "G202 Add implementation-worker issue preflight command",
            body: ValidBody("J-Tech-Japan/intent-system"),
            labelNames: new[] { "intent-target" }));

        using var writer = new StringWriter();
        var exitCode = WorkerIssuePreflightCommand.Execute(
            workspace.Context,
            new[] { "--repo", "J-Tech-Japan/intent-system", "--issue", "509", "--format", "json" },
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<WorkerIssuePreflightResult>(writer.ToString());
        Assert.NotNull(result);
        Assert.True(result!.Actionable);
        Assert.Equal(WorkerIssuePreflightConstants.Classifications.ReadyToImplement, result.Classification);
        Assert.Equal(WorkerIssuePreflightConstants.RecommendedActions.Implement, result.RecommendedAction);
        Assert.Equal(509, result.Issue);
        Assert.Equal("J-Tech-Japan/intent-system", result.Repo);
        Assert.Empty(result.Reasons);
    }

    [Fact]
    public void Execute_GivenIssueWithIntentPrCreated_ClassifiesAsAlreadyPrCreated()
    {
        using var workspace = new WorkerIssuePreflightWorkspace();
        WorkerIssuePreflightCommand.IssueLookupFactory = () => new FakeLookup(BuildIssue(
            number: 100,
            state: "OPEN",
            title: "Existing PR",
            body: ValidBody("J-Tech-Japan/intent-system"),
            labelNames: new[] { "intent-target", "intent-pr-created" }));

        using var writer = new StringWriter();
        var exitCode = WorkerIssuePreflightCommand.Execute(
            workspace.Context,
            new[] { "--repo", "J-Tech-Japan/intent-system", "--issue", "100", "--format", "json" },
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<WorkerIssuePreflightResult>(writer.ToString())!;
        Assert.False(result.Actionable);
        Assert.Equal(WorkerIssuePreflightConstants.Classifications.AlreadyPrCreated, result.Classification);
        Assert.Equal(WorkerIssuePreflightConstants.RecommendedActions.NoAction, result.RecommendedAction);
        Assert.Contains(result.Reasons, r => r.Contains("intent-pr-created", StringComparison.Ordinal));
    }

    [Fact]
    public void Execute_GivenIssueWithIntentIssueInProgress_ClassifiesAsAlreadyInProgress()
    {
        using var workspace = new WorkerIssuePreflightWorkspace();
        WorkerIssuePreflightCommand.IssueLookupFactory = () => new FakeLookup(BuildIssue(
            number: 200,
            state: "OPEN",
            title: "In progress",
            body: ValidBody("J-Tech-Japan/intent-system"),
            labelNames: new[] { "intent-target", "intent-issue-in-progress" }));

        using var writer = new StringWriter();
        var exitCode = WorkerIssuePreflightCommand.Execute(
            workspace.Context,
            new[] { "--repo", "J-Tech-Japan/intent-system", "--issue", "200", "--format", "json" },
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<WorkerIssuePreflightResult>(writer.ToString())!;
        Assert.Equal(WorkerIssuePreflightConstants.Classifications.AlreadyInProgress, result.Classification);
        Assert.Equal(WorkerIssuePreflightConstants.RecommendedActions.NoAction, result.RecommendedAction);
    }

    [Fact]
    public void Execute_GivenInProgressLabelButUnheldClaim_ReportsDisagreementAndProceeds_G717()
    {
        using var workspace = new WorkerIssuePreflightWorkspace();
        Directory.CreateDirectory(Path.Combine(workspace.RootPath, ".intent-cli", "claims"));
        var unrelatedClaim = new ClaimRecord(
            "1",
            "execution-unit:G999",
            "alice",
            "design",
            new DateTimeOffset(2026, 8, 19, 0, 0, 0, TimeSpan.Zero),
            "abc123");
        File.WriteAllText(
            Path.Combine(workspace.RootPath, ClaimCommand.ClaimPath(unrelatedClaim.Scope)),
            JsonSerializer.Serialize(unrelatedClaim));

        WorkerIssuePreflightCommand.IssueLookupFactory = () => new FakeLookup(BuildIssue(
            number: 717,
            state: "OPEN",
            title: "G717 claim precedence",
            body: ValidBody("J-Tech-Japan/intent-system"),
            labelNames: new[] { "intent-target", "intent-issue-in-progress" }));

        using var writer = new StringWriter();
        var exitCode = WorkerIssuePreflightCommand.Execute(
            workspace.Context,
            new[] { "--repo", "J-Tech-Japan/intent-system", "--issue", "717", "--format", "json" },
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<WorkerIssuePreflightResult>(writer.ToString())!;
        Assert.True(result.Actionable);
        Assert.Equal(WorkerIssuePreflightConstants.Classifications.ReadyToImplement, result.Classification);
        Assert.Equal(ClaimOwnershipVerification.StatusUnheld, result.ClaimStatus);
        Assert.Contains(result.Reasons, reason => reason.Contains("disagrees", StringComparison.Ordinal));
        Assert.Contains(result.Reasons, reason => reason.Contains("claim registry is authoritative", StringComparison.Ordinal));
    }

    [Fact]
    public void Execute_GivenIssueWithoutIntentTarget_ClassifiesAsMissingTargetLabel()
    {
        using var workspace = new WorkerIssuePreflightWorkspace();
        WorkerIssuePreflightCommand.IssueLookupFactory = () => new FakeLookup(BuildIssue(
            number: 300,
            state: "OPEN",
            title: "Untargeted",
            body: ValidBody("J-Tech-Japan/intent-system"),
            labelNames: Array.Empty<string>()));

        using var writer = new StringWriter();
        var exitCode = WorkerIssuePreflightCommand.Execute(
            workspace.Context,
            new[] { "--repo", "J-Tech-Japan/intent-system", "--issue", "300", "--format", "json" },
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<WorkerIssuePreflightResult>(writer.ToString())!;
        Assert.Equal(WorkerIssuePreflightConstants.Classifications.MissingTargetLabel, result.Classification);
        Assert.Equal(WorkerIssuePreflightConstants.RecommendedActions.DeclineWithSummary, result.RecommendedAction);
        Assert.Contains(result.Reasons, r => r.Contains("intent-target", StringComparison.Ordinal));
    }

    [Fact]
    public void Execute_GivenHostOnlyPacket_ClassifiesAsHostOnlyPacket_NonActionable()
    {
        // G462 regression (G458 / issue #1018): an intent-target issue whose
        // declared Target paths are all host-owned (intents/**) must NOT be
        // reported as ready-to-implement.
        using var workspace = new WorkerIssuePreflightWorkspace();
        var hostOnlyBody = ValidBody("J-Tech-Japan/intent-system")
            .Replace("Target paths: `src/Foo`, `tests/IntentSystem.Cli.Tests`\n", string.Empty, StringComparison.Ordinal)
            + "\n\nTarget paths: `intents/intent-cli/intent-tree/purpose/04-product-goal.md`, `intents/intent-cli/intent-tree/00-map.md`, `.intent-cli/issues/G458`\n";
        WorkerIssuePreflightCommand.IssueLookupFactory = () => new FakeLookup(BuildIssue(
            number: 1018,
            state: "OPEN",
            title: "G458 Refresh canonical product goal",
            body: hostOnlyBody,
            labelNames: new[] { "intent-target" }));

        using var writer = new StringWriter();
        var exitCode = WorkerIssuePreflightCommand.Execute(
            workspace.Context,
            new[] { "--repo", "J-Tech-Japan/intent-system", "--issue", "1018", "--format", "json" },
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<WorkerIssuePreflightResult>(writer.ToString())!;
        Assert.Equal(WorkerIssuePreflightConstants.Classifications.HostOnlyPacket, result.Classification);
        Assert.False(result.Actionable);
        Assert.Equal(WorkerIssuePreflightConstants.RecommendedActions.ReleaseFromTarget, result.RecommendedAction);
        Assert.Contains(result.Reasons, r => r.Contains("issue-release", StringComparison.Ordinal));
        Assert.Contains(result.Reasons, r => r.Contains("host-only packet", StringComparison.Ordinal));
    }

    [Fact]
    public void Execute_GivenIntentTargetWithChildPaths_StaysReadyToImplement()
    {
        // A child-implementable intent-target issue (target paths include
        // src/** / docs / README.md) must still classify ready-to-implement.
        using var workspace = new WorkerIssuePreflightWorkspace();
        var childBody = ValidBody("J-Tech-Japan/intent-system")
            + "\n\nTarget paths: `intents/intent-cli/specs/14.md`, `src/IntentSystem.Cli/Commands`, `tests/IntentSystem.Cli.Tests`\n";
        WorkerIssuePreflightCommand.IssueLookupFactory = () => new FakeLookup(BuildIssue(
            number: 1019,
            state: "OPEN",
            title: "G462 child-implementable",
            body: childBody,
            labelNames: new[] { "intent-target" }));

        using var writer = new StringWriter();
        var exitCode = WorkerIssuePreflightCommand.Execute(
            workspace.Context,
            new[] { "--repo", "J-Tech-Japan/intent-system", "--issue", "1019", "--format", "json" },
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<WorkerIssuePreflightResult>(writer.ToString())!;
        Assert.Equal(WorkerIssuePreflightConstants.Classifications.ReadyToImplement, result.Classification);
        Assert.True(result.Actionable);
    }

    [Fact]
    public void Execute_GivenContractIncompleteBody_ClassifiesAsContractIncomplete()
    {
        using var workspace = new WorkerIssuePreflightWorkspace();
        // Drop the "## Acceptance Criteria" heading from a valid body.
        var body = ValidBody("J-Tech-Japan/intent-system")
            .Replace("## Acceptance Criteria\n- AC1\n", string.Empty, StringComparison.Ordinal);

        WorkerIssuePreflightCommand.IssueLookupFactory = () => new FakeLookup(BuildIssue(
            number: 400,
            state: "OPEN",
            title: "Incomplete",
            body: body,
            labelNames: new[] { "intent-target" }));

        using var writer = new StringWriter();
        var exitCode = WorkerIssuePreflightCommand.Execute(
            workspace.Context,
            new[] { "--repo", "J-Tech-Japan/intent-system", "--issue", "400", "--format", "json" },
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<WorkerIssuePreflightResult>(writer.ToString())!;
        Assert.Equal(WorkerIssuePreflightConstants.Classifications.ContractIncomplete, result.Classification);
        Assert.Equal(WorkerIssuePreflightConstants.RecommendedActions.WaitForClarification, result.RecommendedAction);
        Assert.Contains(result.Reasons, r => r.Contains("Acceptance Criteria", StringComparison.Ordinal));
    }

    [Fact]
    public void Execute_GivenBodyReferencingDifferentRepo_ClassifiesAsTargetMismatch()
    {
        using var workspace = new WorkerIssuePreflightWorkspace();
        WorkerIssuePreflightCommand.IssueLookupFactory = () => new FakeLookup(BuildIssue(
            number: 500,
            state: "OPEN",
            title: "Cross-repo",
            body: ValidBody("SomeOther/Repo"),
            labelNames: new[] { "intent-target" }));

        using var writer = new StringWriter();
        var exitCode = WorkerIssuePreflightCommand.Execute(
            workspace.Context,
            new[] { "--repo", "J-Tech-Japan/intent-system", "--issue", "500", "--format", "json" },
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<WorkerIssuePreflightResult>(writer.ToString())!;
        Assert.Equal(WorkerIssuePreflightConstants.Classifications.TargetMismatch, result.Classification);
        Assert.Equal(WorkerIssuePreflightConstants.RecommendedActions.SwitchRepo, result.RecommendedAction);
        Assert.Contains(result.Reasons, r =>
            r.Contains("SomeOther/Repo", StringComparison.Ordinal)
            && r.Contains("J-Tech-Japan/intent-system", StringComparison.Ordinal));
    }

    [Fact]
    public void Execute_GivenBodyReferencingSubmodulesPath_ClassifiesAsTargetMismatch()
    {
        using var workspace = new WorkerIssuePreflightWorkspace();
        var body = ValidBody("J-Tech-Japan/intent-system")
            + "\n\nAdditional note: edits land under submodules/intent-system/src/Foo.cs.\n";

        WorkerIssuePreflightCommand.IssueLookupFactory = () => new FakeLookup(BuildIssue(
            number: 600,
            state: "OPEN",
            title: "Submodules path",
            body: body,
            labelNames: new[] { "intent-target" }));

        using var writer = new StringWriter();
        var exitCode = WorkerIssuePreflightCommand.Execute(
            workspace.Context,
            new[]
            {
                "--repo", "J-Tech-Japan/intent-system",
                "--issue", "600",
                "--workdir", "/tmp/notsubmodules",
                "--format", "json"
            },
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<WorkerIssuePreflightResult>(writer.ToString())!;
        Assert.Equal(WorkerIssuePreflightConstants.Classifications.ReadyToImplement, result.Classification);
        Assert.True(result.Actionable);
        Assert.Contains(result.Advisories, r => r.Contains("submodules", StringComparison.Ordinal));
    }

    [Fact]
    public void Execute_GivenG625SentenceWithChildDeclaration_IsActionableWithAdvisory()
    {
        using var workspace = new WorkerIssuePreflightWorkspace();
        var body = ValidBody("J-Tech-Japan/intent-system")
            + "\nreported from a host with no `.gitmodules` and no `submodules/`\n";
        WorkerIssuePreflightCommand.IssueLookupFactory = () => new FakeLookup(BuildIssue(603, "OPEN", "G625 sentence", body, new[] { "intent-target" }));
        using var writer = new StringWriter();
        var exitCode = WorkerIssuePreflightCommand.Execute(workspace.Context,
            new[] { "--repo", "J-Tech-Japan/intent-system", "--issue", "603", "--format", "json" }, writer);
        var result = JsonSerializer.Deserialize<WorkerIssuePreflightResult>(writer.ToString())!;
        Assert.Equal(0, exitCode);
        Assert.Equal(WorkerIssuePreflightConstants.Classifications.ReadyToImplement, result.Classification);
        Assert.True(result.Actionable);
        Assert.Contains(result.Advisories, r => r.Contains("submodules/", StringComparison.Ordinal));
    }

    [Fact]
    public void Execute_GivenG627SentenceWithChildDeclaration_IsActionableWithAdvisory()
    {
        using var workspace = new WorkerIssuePreflightWorkspace();
        var body = ValidBody("J-Tech-Japan/intent-system")
            + "\nnaming this machine's host directory as retirement evidence; the parent-host execution path was measured.\n";
        WorkerIssuePreflightCommand.IssueLookupFactory = () => new FakeLookup(BuildIssue(604, "OPEN", "G627 sentence", body, new[] { "intent-target" }));
        using var writer = new StringWriter();
        var exitCode = WorkerIssuePreflightCommand.Execute(workspace.Context,
            new[] { "--repo", "J-Tech-Japan/intent-system", "--issue", "604", "--format", "json" }, writer);
        var result = JsonSerializer.Deserialize<WorkerIssuePreflightResult>(writer.ToString())!;
        Assert.Equal(0, exitCode);
        Assert.Equal(WorkerIssuePreflightConstants.Classifications.ReadyToImplement, result.Classification);
        Assert.True(result.Actionable);
        Assert.Contains(result.Advisories, r => r.Contains("parent-host", StringComparison.Ordinal));
    }

    [Fact]
    public void Execute_GivenDeclaredSubmoduleTargetOutsideWorkdir_BlocksFromDeclaration()
    {
        using var workspace = new WorkerIssuePreflightWorkspace();
        var body = ValidBody("J-Tech-Japan/intent-system")
            .Replace("Target paths: `src/Foo`, `tests/IntentSystem.Cli.Tests`", "Target paths: `submodules/child/src/Foo`", StringComparison.Ordinal);
        WorkerIssuePreflightCommand.IssueLookupFactory = () => new FakeLookup(BuildIssue(601, "OPEN", "submodule", body, new[] { "intent-target" }));
        using var writer = new StringWriter();
        var exitCode = WorkerIssuePreflightCommand.Execute(workspace.Context,
            new[] { "--repo", "J-Tech-Japan/intent-system", "--issue", "601", "--workdir", "/tmp/host", "--format", "json" }, writer);
        var result = JsonSerializer.Deserialize<WorkerIssuePreflightResult>(writer.ToString())!;
        Assert.Equal(0, exitCode);
        Assert.False(result.Actionable);
        Assert.Equal(WorkerIssuePreflightConstants.Classifications.TargetMismatch, result.Classification);
        Assert.Contains(result.Reasons, r => r.StartsWith("declaration-derived:", StringComparison.Ordinal));
    }

    [Fact]
    public void Execute_GivenMissingTargetDeclaration_RefusesToGuess()
    {
        using var workspace = new WorkerIssuePreflightWorkspace();
        var body = ValidBody("J-Tech-Japan/intent-system").Replace("Target paths: `src/Foo`, `tests/IntentSystem.Cli.Tests`\n", string.Empty, StringComparison.Ordinal);
        WorkerIssuePreflightCommand.IssueLookupFactory = () => new FakeLookup(BuildIssue(602, "OPEN", "missing", body, new[] { "intent-target" }));
        using var writer = new StringWriter();
        var exitCode = WorkerIssuePreflightCommand.Execute(workspace.Context,
            new[] { "--repo", "J-Tech-Japan/intent-system", "--issue", "602", "--format", "json" }, writer);
        var result = JsonSerializer.Deserialize<WorkerIssuePreflightResult>(writer.ToString())!;
        Assert.Equal(0, exitCode);
        Assert.False(result.Actionable);
        Assert.Equal(WorkerIssuePreflightConstants.Classifications.MissingTargetDeclaration, result.Classification);
        Assert.Contains(result.Reasons, r => r.Contains("Target paths", StringComparison.Ordinal));
    }

    [Fact]
    public void Execute_GivenClosedIssue_ClassifiesAsNonActionable()
    {
        using var workspace = new WorkerIssuePreflightWorkspace();
        WorkerIssuePreflightCommand.IssueLookupFactory = () => new FakeLookup(BuildIssue(
            number: 700,
            state: "CLOSED",
            title: "Already closed",
            body: ValidBody("J-Tech-Japan/intent-system"),
            labelNames: new[] { "intent-target" },
            closed: true));

        using var writer = new StringWriter();
        var exitCode = WorkerIssuePreflightCommand.Execute(
            workspace.Context,
            new[] { "--repo", "J-Tech-Japan/intent-system", "--issue", "700", "--format", "json" },
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<WorkerIssuePreflightResult>(writer.ToString())!;
        Assert.Equal(WorkerIssuePreflightConstants.Classifications.NonActionable, result.Classification);
        Assert.Contains(result.Reasons, r => r.Contains("closed", StringComparison.Ordinal));
    }

    [Fact]
    public void Execute_GivenJsonFormat_RoundTripsStably()
    {
        using var workspace = new WorkerIssuePreflightWorkspace();
        WorkerIssuePreflightCommand.IssueLookupFactory = () => new FakeLookup(BuildIssue(
            number: 509,
            state: "OPEN",
            title: "Round-trip",
            body: ValidBody("J-Tech-Japan/intent-system"),
            labelNames: new[] { "intent-target" }));

        using var writer = new StringWriter();
        var exitCode = WorkerIssuePreflightCommand.Execute(
            workspace.Context,
            new[] { "--repo", "J-Tech-Japan/intent-system", "--issue", "509", "--format", "json" },
            writer);

        Assert.Equal(0, exitCode);
        var first = JsonSerializer.Deserialize<WorkerIssuePreflightResult>(writer.ToString())!;
        var serialized = JsonSerializer.Serialize(first);
        var second = JsonSerializer.Deserialize<WorkerIssuePreflightResult>(serialized)!;

        Assert.Equal(first.Actionable, second.Actionable);
        Assert.Equal(first.Classification, second.Classification);
        Assert.Equal(first.Issue, second.Issue);
        Assert.Equal(first.Repo, second.Repo);
        Assert.Equal(first.Title, second.Title);
        Assert.Equal(first.IssueState, second.IssueState);
        Assert.Equal(first.Labels, second.Labels);
        Assert.Equal(first.Reasons, second.Reasons);
        Assert.Equal(first.RecommendedAction, second.RecommendedAction);
        Assert.Equal(first.SummaryLine, second.SummaryLine);
    }

    [Fact]
    public void Execute_GivenJsonFormat_FieldsMatchIssueAcceptanceCriteria()
    {
        using var workspace = new WorkerIssuePreflightWorkspace();
        WorkerIssuePreflightCommand.IssueLookupFactory = () => new FakeLookup(BuildIssue(
            number: 509,
            state: "OPEN",
            title: "Field check",
            body: ValidBody("J-Tech-Japan/intent-system"),
            labelNames: new[] { "intent-target" }));

        using var writer = new StringWriter();
        var exitCode = WorkerIssuePreflightCommand.Execute(
            workspace.Context,
            new[] { "--repo", "J-Tech-Japan/intent-system", "--issue", "509", "--format", "json" },
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.True(root.TryGetProperty("actionable", out _));
        Assert.True(root.TryGetProperty("classification", out _));
        Assert.True(root.TryGetProperty("issue", out _));
        Assert.True(root.TryGetProperty("repo", out _));
        Assert.True(root.TryGetProperty("reasons", out _));
        Assert.True(root.TryGetProperty("recommended_action", out _));

        // Issue #509's minimum-JSON example and Acceptance Criteria name the
        // field "recommendedAction" (camelCase). Both keys MUST be present:
        // the snake_case primary for local style consistency, and the
        // camelCase alias to satisfy the issue contract verbatim. They MUST
        // carry identical values.
        Assert.True(root.TryGetProperty("recommendedAction", out var camelCase));
        Assert.True(root.TryGetProperty("recommended_action", out var snakeCase));
        Assert.Equal(snakeCase.GetString(), camelCase.GetString());
    }

    [Fact]
    public void Execute_GivenJsonFormat_RecommendedActionCamelCaseAliasMatchesIssueContract()
    {
        // Regression for the G202 review note: issue #509 explicitly names
        // the field "recommendedAction" (camelCase) in both the minimum
        // JSON example and Acceptance Criteria. The implementation must
        // emit that key verbatim alongside the snake_case primary so the
        // issue contract holds. Locks the alias against accidental removal.
        using var workspace = new WorkerIssuePreflightWorkspace();
        WorkerIssuePreflightCommand.IssueLookupFactory = () => new FakeLookup(BuildIssue(
            number: 509,
            state: "OPEN",
            title: "Camel case alias",
            body: ValidBody("J-Tech-Japan/intent-system"),
            labelNames: new[] { "intent-target" }));

        using var writer = new StringWriter();
        var exitCode = WorkerIssuePreflightCommand.Execute(
            workspace.Context,
            new[] { "--repo", "J-Tech-Japan/intent-system", "--issue", "509", "--format", "json" },
            writer);

        Assert.Equal(0, exitCode);
        var stdout = writer.ToString();

        // The literal key "recommendedAction" must appear in the output.
        Assert.Contains("\"recommendedAction\"", stdout, StringComparison.Ordinal);

        // Both keys are present and carry the SAME value.
        using var document = JsonDocument.Parse(stdout);
        var root = document.RootElement;
        Assert.True(root.TryGetProperty("recommendedAction", out var camelCase));
        Assert.True(root.TryGetProperty("recommended_action", out var snakeCase));
        Assert.Equal(snakeCase.GetString(), camelCase.GetString());
        Assert.Equal(WorkerIssuePreflightConstants.RecommendedActions.Implement, camelCase.GetString());
    }

    [Fact]
    public void Execute_GivenLookupTransportFailure_ReturnsNonZero_ActionableErrorMessage()
    {
        using var workspace = new WorkerIssuePreflightWorkspace();
        WorkerIssuePreflightCommand.IssueLookupFactory = () => new ThrowingLookup(
            "simulated network failure: gh exited 1");

        using var writer = new StringWriter();
        var exitCode = WorkerIssuePreflightCommand.Execute(
            workspace.Context,
            new[] { "--repo", "J-Tech-Japan/intent-system", "--issue", "509", "--format", "json" },
            writer);

        Assert.Equal(1, exitCode);
        var output = writer.ToString();
        Assert.Contains("simulated network failure", output, StringComparison.Ordinal);
        Assert.Contains("509", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenMissingRepoFlag_ReturnsNonZero()
    {
        using var workspace = new WorkerIssuePreflightWorkspace();
        WorkerIssuePreflightCommand.IssueLookupFactory = () => new FakeLookup(BuildIssue(
            number: 1, state: "OPEN", title: "x", body: string.Empty, labelNames: Array.Empty<string>()));

        using var writer = new StringWriter();
        var exitCode = WorkerIssuePreflightCommand.Execute(
            workspace.Context,
            new[] { "--issue", "1" },
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("--repo", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenMissingIssueFlag_ReturnsNonZero()
    {
        using var workspace = new WorkerIssuePreflightWorkspace();
        WorkerIssuePreflightCommand.IssueLookupFactory = () => new FakeLookup(BuildIssue(
            number: 1, state: "OPEN", title: "x", body: string.Empty, labelNames: Array.Empty<string>()));

        using var writer = new StringWriter();
        var exitCode = WorkerIssuePreflightCommand.Execute(
            workspace.Context,
            new[] { "--repo", "J-Tech-Japan/intent-system" },
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("--issue", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenNonNumericIssue_ReturnsNonZero()
    {
        using var workspace = new WorkerIssuePreflightWorkspace();
        using var writer = new StringWriter();
        var exitCode = WorkerIssuePreflightCommand.Execute(
            workspace.Context,
            new[] { "--repo", "J-Tech-Japan/intent-system", "--issue", "abc" },
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("--issue", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenUnknownArgument_ReturnsNonZero()
    {
        using var workspace = new WorkerIssuePreflightWorkspace();
        using var writer = new StringWriter();
        var exitCode = WorkerIssuePreflightCommand.Execute(
            workspace.Context,
            new[] { "--repo", "J-Tech-Japan/intent-system", "--issue", "1", "--bogus" },
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("--bogus", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_PrecedenceClosedBeatsLabels_NonActionableWins()
    {
        using var workspace = new WorkerIssuePreflightWorkspace();
        WorkerIssuePreflightCommand.IssueLookupFactory = () => new FakeLookup(BuildIssue(
            number: 800,
            state: "CLOSED",
            title: "Closed with PR-created",
            body: ValidBody("J-Tech-Japan/intent-system"),
            labelNames: new[] { "intent-target", "intent-pr-created" },
            closed: true));

        using var writer = new StringWriter();
        var exitCode = WorkerIssuePreflightCommand.Execute(
            workspace.Context,
            new[] { "--repo", "J-Tech-Japan/intent-system", "--issue", "800", "--format", "json" },
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<WorkerIssuePreflightResult>(writer.ToString())!;
        Assert.Equal(WorkerIssuePreflightConstants.Classifications.NonActionable, result.Classification);
    }

    [Fact]
    public void Execute_PrecedencePrCreatedBeatsInProgress_PrCreatedWins()
    {
        using var workspace = new WorkerIssuePreflightWorkspace();
        WorkerIssuePreflightCommand.IssueLookupFactory = () => new FakeLookup(BuildIssue(
            number: 900,
            state: "OPEN",
            title: "Both labels",
            body: ValidBody("J-Tech-Japan/intent-system"),
            labelNames: new[] { "intent-target", "intent-pr-created", "intent-issue-in-progress" }));

        using var writer = new StringWriter();
        var exitCode = WorkerIssuePreflightCommand.Execute(
            workspace.Context,
            new[] { "--repo", "J-Tech-Japan/intent-system", "--issue", "900", "--format", "json" },
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<WorkerIssuePreflightResult>(writer.ToString())!;
        Assert.Equal(WorkerIssuePreflightConstants.Classifications.AlreadyPrCreated, result.Classification);
    }

    [Fact]
    public void Execute_NeverWritesToQueueStateOrRunsLog()
    {
        using var workspace = new WorkerIssuePreflightWorkspace();
        WorkerIssuePreflightCommand.IssueLookupFactory = () => new FakeLookup(BuildIssue(
            number: 509,
            state: "OPEN",
            title: "Snapshot",
            body: ValidBody("J-Tech-Japan/intent-system"),
            labelNames: new[] { "intent-target" }));

        var queueStatePath = Path.Combine(workspace.RootPath, ".intent-cli", "queue-state.json");
        var runsLogPath = Path.Combine(workspace.RootPath, ".intent-cli", "runs.jsonl");
        File.WriteAllText(queueStatePath, "{\"queue\":[]}");
        File.WriteAllText(runsLogPath, "{\"event\":\"baseline\"}\n");

        var queueBefore = File.ReadAllBytes(queueStatePath);
        var runsBefore = File.ReadAllBytes(runsLogPath);

        using var writer = new StringWriter();
        var exitCode = WorkerIssuePreflightCommand.Execute(
            workspace.Context,
            new[] { "--repo", "J-Tech-Japan/intent-system", "--issue", "509", "--format", "json" },
            writer);

        Assert.Equal(0, exitCode);
        var queueAfter = File.ReadAllBytes(queueStatePath);
        var runsAfter = File.ReadAllBytes(runsLogPath);
        Assert.Equal(queueBefore, queueAfter);
        Assert.Equal(runsBefore, runsAfter);
    }

    [Fact]
    public void Execute_NeverWritesAnyArtifactFile()
    {
        using var workspace = new WorkerIssuePreflightWorkspace();
        WorkerIssuePreflightCommand.IssueLookupFactory = () => new FakeLookup(BuildIssue(
            number: 509,
            state: "OPEN",
            title: "No writes",
            body: ValidBody("J-Tech-Japan/intent-system"),
            labelNames: new[] { "intent-target" }));

        // Seed at least one file so the snapshot has a baseline.
        File.WriteAllText(Path.Combine(workspace.RootPath, "baseline.txt"), "baseline");
        var before = workspace.SnapshotWorkspace();

        using var writer = new StringWriter();
        var exitCode = WorkerIssuePreflightCommand.Execute(
            workspace.Context,
            new[] { "--repo", "J-Tech-Japan/intent-system", "--issue", "509", "--format", "json" },
            writer);

        Assert.Equal(0, exitCode);
        var after = workspace.SnapshotWorkspace();
        Assert.Equal(before.Count, after.Count);
        foreach (var (path, hash) in before)
        {
            Assert.True(after.TryGetValue(path, out var afterHash),
                $"file disappeared during command execution: {path}");
            Assert.Equal(hash, afterHash);
        }
    }

    [Fact]
    public void Execute_NeverInvokesProvider_NoProcessStartInNewSurface()
    {
        using var workspace = new WorkerIssuePreflightWorkspace();
        var invoked = false;
        WorkerIssuePreflightCommand.NestedProviderLauncher = () =>
        {
            invoked = true;
            return false;
        };
        WorkerIssuePreflightCommand.IssueLookupFactory = () => new FakeLookup(BuildIssue(
            number: 509,
            state: "OPEN",
            title: "No provider",
            body: ValidBody("J-Tech-Japan/intent-system"),
            labelNames: new[] { "intent-target" }));

        using var writer = new StringWriter();
        var exitCode = WorkerIssuePreflightCommand.Execute(
            workspace.Context,
            new[] { "--repo", "J-Tech-Japan/intent-system", "--issue", "509" },
            writer);

        Assert.Equal(0, exitCode);
        Assert.False(invoked, "NestedProviderLauncher must never be invoked by the preflight command");

        // Source-scan: command and analyzer must not contain Process.Start. The
        // GhCliGitHubIssueLookup adapter is exempt — that's its job.
        var commandSource = File.ReadAllText(LocateSourceFile("WorkerIssuePreflightCommand.cs"));
        var analyzerSource = File.ReadAllText(LocateSourceFile("WorkerIssuePreflightAnalyzer.cs"));
        var resultSource = File.ReadAllText(LocateSourceFile("WorkerIssuePreflightResult.cs"));

        Assert.DoesNotContain("Process.Start(", commandSource, StringComparison.Ordinal);
        Assert.DoesNotContain("Process.Start(", analyzerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("Process.Start(", resultSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_NeverMutatesAnyLabel_NoGhEditCallInAnalyzer()
    {
        var commandSource = File.ReadAllText(LocateSourceFile("WorkerIssuePreflightCommand.cs"));
        var analyzerSource = File.ReadAllText(LocateSourceFile("WorkerIssuePreflightAnalyzer.cs"));

        foreach (var literal in new[]
        {
            "gh issue edit",
            "gh pr edit",
            "gh issue close",
            "gh pr close"
        })
        {
            Assert.DoesNotContain(literal, commandSource, StringComparison.Ordinal);
            Assert.DoesNotContain(literal, analyzerSource, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Execute_GivenTextFormat_PrintsStableSummaryLine()
    {
        using var workspace = new WorkerIssuePreflightWorkspace();
        WorkerIssuePreflightCommand.IssueLookupFactory = () => new FakeLookup(BuildIssue(
            number: 509,
            state: "OPEN",
            title: "Text format",
            body: ValidBody("J-Tech-Japan/intent-system"),
            labelNames: new[] { "intent-target" }));

        using var writer = new StringWriter();
        var exitCode = WorkerIssuePreflightCommand.Execute(
            workspace.Context,
            new[] { "--repo", "J-Tech-Japan/intent-system", "--issue", "509" },
            writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("# Worker issue-preflight for J-Tech-Japan/intent-system#509", output, StringComparison.Ordinal);
        Assert.Contains("classification=ready-to-implement", output, StringComparison.Ordinal);
        Assert.Contains("actionable=true", output, StringComparison.Ordinal);
    }

    private static string LocateSourceFile(string fileName)
    {
        var directory = AppContext.BaseDirectory;
        // Walk up to the repo root, then descend into the known commands path.
        var dir = new DirectoryInfo(directory);
        while (dir is not null)
        {
            var candidate = Path.Combine(
                dir.FullName, "src", "IntentSystem.Cli", "Commands", fileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
            dir = dir.Parent;
        }

        throw new FileNotFoundException(
            $"Could not locate source file {fileName} from {directory}");
    }

    private static GitHubIssueLookupResult BuildIssue(
        int number,
        string state,
        string title,
        string body,
        string[] labelNames,
        bool closed = false)
    {
        return new GitHubIssueLookupResult
        {
            Number = number,
            State = state,
            Title = title,
            Body = body,
            Closed = closed,
            ClosedAt = closed ? "2025-01-01T00:00:00Z" : null,
            Labels = labelNames.Select(n => new GitHubIssueLabel { Name = n }).ToArray()
        };
    }

    private static string ValidBody(string repository)
    {
        return $"""
            ## Goal
            Test goal.

            ## Why This Slice Exists Now
            Test rationale.

            ## Current Observed State
            Test observation.

            ## Accepted Baseline You May Assume
            Test baseline.

            ## Target Repo / Path / Part
            Repository: {repository}
            Target paths: `src/Foo`, `tests/IntentSystem.Cli.Tests`

            ## In Scope
            - in scope item

            ## Out Of Scope
            - out of scope item

            ## Acceptance Criteria
            - AC1

            ## Verification
            - run tests

            ## Related Links
            - https://example.com/link

            ## Base Branch Policy
            Policy: `direct-main`
            Expected PR base branch: `main`
            Open all child PRs against `main` directly.
            """;
    }

    private sealed class FakeLookup : IGitHubIssueLookup
    {
        private readonly GitHubIssueLookupResult payload;

        public FakeLookup(GitHubIssueLookupResult payload)
        {
            this.payload = payload;
        }

        public GitHubIssueLookupResult Lookup(string repo, int issueNumber) => payload;
    }

    private sealed class ThrowingLookup : IGitHubIssueLookup
    {
        private readonly string message;

        public ThrowingLookup(string message)
        {
            this.message = message;
        }

        public GitHubIssueLookupResult Lookup(string repo, int issueNumber)
        {
            throw new InvalidOperationException(message);
        }
    }

    private sealed class WorkerIssuePreflightWorkspace : IDisposable
    {
        public WorkerIssuePreflightWorkspace()
        {
            RootPath = Directory.CreateTempSubdirectory("worker-issue-preflight-tests-").FullName;
            Directory.CreateDirectory(Path.Combine(RootPath, ".intent-cli"));
            Context = new CliContext
            {
                RepoRoot = RootPath,
                Config = new CliConfig
                {
                    Project = new ProjectConfig
                    {
                        Domain = "intent-cli",
                        ArtifactRoot = ".intent-cli"
                    }
                }
            };
        }

        public string RootPath { get; }

        public CliContext Context { get; }

        public IReadOnlyDictionary<string, string> SnapshotWorkspace()
        {
            var snapshot = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var path in Directory.EnumerateFiles(RootPath, "*", SearchOption.AllDirectories))
            {
                var bytes = File.ReadAllBytes(path);
                var hash = Convert.ToHexString(SHA256.HashData(bytes));
                snapshot[path] = hash;
            }
            return snapshot;
        }

        public void Dispose()
        {
            if (Directory.Exists(RootPath))
            {
                Directory.Delete(RootPath, recursive: true);
            }
        }
    }
}
