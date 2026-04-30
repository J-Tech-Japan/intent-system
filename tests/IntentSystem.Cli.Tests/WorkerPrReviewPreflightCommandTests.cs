using System.Security.Cryptography;
using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

public sealed class WorkerPrReviewPreflightCommandTests : IDisposable
{
    public WorkerPrReviewPreflightCommandTests()
    {
        WorkerPrReviewPreflightCommand.PrLookupFactory = null;
        WorkerPrReviewPreflightCommand.IssueLookupFactory = null;
        WorkerPrReviewPreflightCommand.NestedProviderLauncher = null;
    }

    public void Dispose()
    {
        WorkerPrReviewPreflightCommand.PrLookupFactory = null;
        WorkerPrReviewPreflightCommand.IssueLookupFactory = null;
        WorkerPrReviewPreflightCommand.NestedProviderLauncher = null;
    }

    [Fact]
    public void Execute_GivenReadyPr_ClassifiesAsReadyToReview()
    {
        using var workspace = new WorkerPrReviewPreflightWorkspace();
        WorkerPrReviewPreflightCommand.PrLookupFactory = () => new FakePrLookup(BuildPr(
            number: 600,
            state: "OPEN",
            title: "Ready PR",
            body: "Implements feature.\n\nCloses #100",
            labelNames: new[] { "intent-target" },
            isDraft: false,
            closingIssueNumbers: new[] { 100 }));
        WorkerPrReviewPreflightCommand.IssueLookupFactory = () => new FakeIssueLookup(BuildIssue(
            number: 100,
            state: "OPEN",
            title: "Source",
            body: string.Empty,
            labelNames: new[] { "intent-target", "intent-pr-created" }));

        using var writer = new StringWriter();
        var exitCode = WorkerPrReviewPreflightCommand.Execute(
            workspace.Context,
            new[] { "--repo", "J-Tech-Japan/intent-system", "--pr", "600", "--format", "json" },
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<WorkerPrReviewPreflightResult>(writer.ToString());
        Assert.NotNull(result);
        Assert.True(result!.Actionable);
        Assert.Equal(WorkerPrReviewPreflightConstants.Classifications.ReadyToReview, result.Classification);
        Assert.Equal(WorkerPrReviewPreflightConstants.RecommendedActions.Review, result.RecommendedAction);
        Assert.Equal(600, result.Pr);
        Assert.Equal("J-Tech-Japan/intent-system", result.Repo);
        Assert.Equal(100, result.SourceIssue);
        Assert.Empty(result.Reasons);
    }

    [Fact]
    public void Execute_GivenPrWithIntentPrRequestUpdate_ClassifiesAsRequestUpdatePending()
    {
        using var workspace = new WorkerPrReviewPreflightWorkspace();
        WorkerPrReviewPreflightCommand.PrLookupFactory = () => new FakePrLookup(BuildPr(
            number: 1,
            state: "OPEN",
            title: "Update requested",
            body: "Closes #100",
            labelNames: new[] { "intent-target", "intent-pr-request-update" },
            closingIssueNumbers: new[] { 100 }));
        WorkerPrReviewPreflightCommand.IssueLookupFactory = () => new FakeIssueLookup(BuildIssue(
            number: 100,
            state: "OPEN",
            title: "Source",
            body: string.Empty,
            labelNames: new[] { "intent-target", "intent-pr-created" }));

        using var writer = new StringWriter();
        var exitCode = WorkerPrReviewPreflightCommand.Execute(
            workspace.Context,
            new[] { "--repo", "J-Tech-Japan/intent-system", "--pr", "1", "--format", "json" },
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<WorkerPrReviewPreflightResult>(writer.ToString())!;
        Assert.False(result.Actionable);
        Assert.Equal(WorkerPrReviewPreflightConstants.Classifications.RequestUpdatePending, result.Classification);
        Assert.Equal(WorkerPrReviewPreflightConstants.RecommendedActions.WaitForWorkerUpdate, result.RecommendedAction);
    }

    [Fact]
    public void Execute_GivenPrWithIntentPrUpdateInProgress_ClassifiesAsUpdateInProgress()
    {
        using var workspace = new WorkerPrReviewPreflightWorkspace();
        WorkerPrReviewPreflightCommand.PrLookupFactory = () => new FakePrLookup(BuildPr(
            number: 2,
            state: "OPEN",
            title: "Worker iterating",
            body: "Closes #100",
            labelNames: new[] { "intent-target", "intent-pr-update-in-progress" },
            closingIssueNumbers: new[] { 100 }));
        WorkerPrReviewPreflightCommand.IssueLookupFactory = () => new FakeIssueLookup(BuildIssue(
            number: 100,
            state: "OPEN",
            title: "Source",
            body: string.Empty,
            labelNames: new[] { "intent-target", "intent-pr-created" }));

        using var writer = new StringWriter();
        var exitCode = WorkerPrReviewPreflightCommand.Execute(
            workspace.Context,
            new[] { "--repo", "J-Tech-Japan/intent-system", "--pr", "2", "--format", "json" },
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<WorkerPrReviewPreflightResult>(writer.ToString())!;
        Assert.Equal(WorkerPrReviewPreflightConstants.Classifications.UpdateInProgress, result.Classification);
        Assert.Equal(WorkerPrReviewPreflightConstants.RecommendedActions.WaitForWorkerUpdate, result.RecommendedAction);
    }

    [Fact]
    public void Execute_GivenPrWithIntentPrReviewing_ClassifiesAsAlreadyReviewing()
    {
        using var workspace = new WorkerPrReviewPreflightWorkspace();
        WorkerPrReviewPreflightCommand.PrLookupFactory = () => new FakePrLookup(BuildPr(
            number: 3,
            state: "OPEN",
            title: "Being reviewed",
            body: "Closes #100",
            labelNames: new[] { "intent-target", "intent-pr-reviewing" },
            closingIssueNumbers: new[] { 100 }));
        WorkerPrReviewPreflightCommand.IssueLookupFactory = () => new FakeIssueLookup(BuildIssue(
            number: 100,
            state: "OPEN",
            title: "Source",
            body: string.Empty,
            labelNames: new[] { "intent-target", "intent-pr-created" }));

        using var writer = new StringWriter();
        var exitCode = WorkerPrReviewPreflightCommand.Execute(
            workspace.Context,
            new[] { "--repo", "J-Tech-Japan/intent-system", "--pr", "3", "--format", "json" },
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<WorkerPrReviewPreflightResult>(writer.ToString())!;
        Assert.Equal(WorkerPrReviewPreflightConstants.Classifications.AlreadyReviewing, result.Classification);
        Assert.Equal(WorkerPrReviewPreflightConstants.RecommendedActions.NoAction, result.RecommendedAction);
    }

    [Fact]
    public void Execute_GivenPrWithIntentPrApproved_ClassifiesAsApprovedOrMerged()
    {
        using var workspace = new WorkerPrReviewPreflightWorkspace();
        WorkerPrReviewPreflightCommand.PrLookupFactory = () => new FakePrLookup(BuildPr(
            number: 4,
            state: "OPEN",
            title: "Approved",
            body: "Closes #100",
            labelNames: new[] { "intent-target", "intent-pr-approved" },
            closingIssueNumbers: new[] { 100 }));
        WorkerPrReviewPreflightCommand.IssueLookupFactory = () => new FakeIssueLookup(BuildIssue(
            number: 100,
            state: "OPEN",
            title: "Source",
            body: string.Empty,
            labelNames: new[] { "intent-target", "intent-pr-created" }));

        using var writer = new StringWriter();
        var exitCode = WorkerPrReviewPreflightCommand.Execute(
            workspace.Context,
            new[] { "--repo", "J-Tech-Japan/intent-system", "--pr", "4", "--format", "json" },
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<WorkerPrReviewPreflightResult>(writer.ToString())!;
        Assert.Equal(WorkerPrReviewPreflightConstants.Classifications.ApprovedOrMerged, result.Classification);
        Assert.Equal(WorkerPrReviewPreflightConstants.RecommendedActions.NoAction, result.RecommendedAction);
        Assert.Contains(result.Reasons, r => r.Contains("intent-pr-approved", StringComparison.Ordinal));
    }

    [Fact]
    public void Execute_GivenMergedPr_ClassifiesAsApprovedOrMerged()
    {
        using var workspace = new WorkerPrReviewPreflightWorkspace();
        WorkerPrReviewPreflightCommand.PrLookupFactory = () => new FakePrLookup(BuildPr(
            number: 5,
            state: "MERGED",
            title: "Merged",
            body: "Closes #100",
            labelNames: new[] { "intent-target" },
            merged: true,
            closingIssueNumbers: new[] { 100 }));
        WorkerPrReviewPreflightCommand.IssueLookupFactory = () => new FakeIssueLookup(BuildIssue(
            number: 100,
            state: "OPEN",
            title: "Source",
            body: string.Empty,
            labelNames: new[] { "intent-target", "intent-pr-created" }));

        using var writer = new StringWriter();
        var exitCode = WorkerPrReviewPreflightCommand.Execute(
            workspace.Context,
            new[] { "--repo", "J-Tech-Japan/intent-system", "--pr", "5", "--format", "json" },
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<WorkerPrReviewPreflightResult>(writer.ToString())!;
        Assert.Equal(WorkerPrReviewPreflightConstants.Classifications.ApprovedOrMerged, result.Classification);
        Assert.Equal("merged", result.PrState);
        Assert.Contains(result.Reasons, r => r.Contains("merged", StringComparison.Ordinal));
    }

    [Fact]
    public void Execute_GivenPrWithoutIntentTarget_ClassifiesAsMissingTargetLabel()
    {
        using var workspace = new WorkerPrReviewPreflightWorkspace();
        WorkerPrReviewPreflightCommand.PrLookupFactory = () => new FakePrLookup(BuildPr(
            number: 6,
            state: "OPEN",
            title: "Untargeted",
            body: "Closes #100",
            labelNames: Array.Empty<string>(),
            closingIssueNumbers: new[] { 100 }));
        WorkerPrReviewPreflightCommand.IssueLookupFactory = () => new FakeIssueLookup(BuildIssue(
            number: 100,
            state: "OPEN",
            title: "Source",
            body: string.Empty,
            labelNames: new[] { "intent-target", "intent-pr-created" }));

        using var writer = new StringWriter();
        var exitCode = WorkerPrReviewPreflightCommand.Execute(
            workspace.Context,
            new[] { "--repo", "J-Tech-Japan/intent-system", "--pr", "6", "--format", "json" },
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<WorkerPrReviewPreflightResult>(writer.ToString())!;
        Assert.Equal(WorkerPrReviewPreflightConstants.Classifications.MissingTargetLabel, result.Classification);
        Assert.Equal(WorkerPrReviewPreflightConstants.RecommendedActions.DeclineWithSummary, result.RecommendedAction);
        Assert.Contains(result.Reasons, r => r.Contains("intent-target", StringComparison.Ordinal));
    }

    [Fact]
    public void Execute_GivenPrWithNoLinkedIssue_ClassifiesAsSourceIssueMissing()
    {
        using var workspace = new WorkerPrReviewPreflightWorkspace();
        WorkerPrReviewPreflightCommand.PrLookupFactory = () => new FakePrLookup(BuildPr(
            number: 7,
            state: "OPEN",
            title: "No link",
            body: "No issue references at all here.",
            labelNames: new[] { "intent-target" },
            closingIssueNumbers: Array.Empty<int>()));

        using var writer = new StringWriter();
        var exitCode = WorkerPrReviewPreflightCommand.Execute(
            workspace.Context,
            new[] { "--repo", "J-Tech-Japan/intent-system", "--pr", "7", "--format", "json" },
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<WorkerPrReviewPreflightResult>(writer.ToString())!;
        Assert.Equal(WorkerPrReviewPreflightConstants.Classifications.SourceIssueMissing, result.Classification);
        Assert.Equal(WorkerPrReviewPreflightConstants.RecommendedActions.DeclineWithSummary, result.RecommendedAction);
        Assert.Null(result.SourceIssue);
    }

    [Fact]
    public void Execute_GivenPrSourceIssueMissingIntentTarget_ClassifiesAsSourceIssueNotTarget()
    {
        using var workspace = new WorkerPrReviewPreflightWorkspace();
        WorkerPrReviewPreflightCommand.PrLookupFactory = () => new FakePrLookup(BuildPr(
            number: 8,
            state: "OPEN",
            title: "Source missing intent-target",
            body: "Closes #100",
            labelNames: new[] { "intent-target" },
            closingIssueNumbers: new[] { 100 }));
        WorkerPrReviewPreflightCommand.IssueLookupFactory = () => new FakeIssueLookup(BuildIssue(
            number: 100,
            state: "OPEN",
            title: "Source",
            body: string.Empty,
            labelNames: new[] { "intent-pr-created" }));

        using var writer = new StringWriter();
        var exitCode = WorkerPrReviewPreflightCommand.Execute(
            workspace.Context,
            new[] { "--repo", "J-Tech-Japan/intent-system", "--pr", "8", "--format", "json" },
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<WorkerPrReviewPreflightResult>(writer.ToString())!;
        Assert.Equal(WorkerPrReviewPreflightConstants.Classifications.SourceIssueNotTarget, result.Classification);
        Assert.Equal(WorkerPrReviewPreflightConstants.RecommendedActions.LabelCleanupRequired, result.RecommendedAction);
        Assert.Contains(result.Reasons, r => r.Contains("intent-target", StringComparison.Ordinal));
    }

    [Fact]
    public void Execute_GivenPrSourceIssueMissingIntentPrCreated_ClassifiesAsSourceIssueNotTarget()
    {
        using var workspace = new WorkerPrReviewPreflightWorkspace();
        WorkerPrReviewPreflightCommand.PrLookupFactory = () => new FakePrLookup(BuildPr(
            number: 9,
            state: "OPEN",
            title: "Source missing pr-created",
            body: "Closes #100",
            labelNames: new[] { "intent-target" },
            closingIssueNumbers: new[] { 100 }));
        WorkerPrReviewPreflightCommand.IssueLookupFactory = () => new FakeIssueLookup(BuildIssue(
            number: 100,
            state: "OPEN",
            title: "Source",
            body: string.Empty,
            labelNames: new[] { "intent-target" }));

        using var writer = new StringWriter();
        var exitCode = WorkerPrReviewPreflightCommand.Execute(
            workspace.Context,
            new[] { "--repo", "J-Tech-Japan/intent-system", "--pr", "9", "--format", "json" },
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<WorkerPrReviewPreflightResult>(writer.ToString())!;
        Assert.Equal(WorkerPrReviewPreflightConstants.Classifications.SourceIssueNotTarget, result.Classification);
        Assert.Contains(result.Reasons, r => r.Contains("intent-pr-created", StringComparison.Ordinal));
    }

    [Fact]
    public void Execute_GivenPrCarriesIntentPrCreatedItself_ClassifiesAsSourceIssueNotTarget_LabelPolicyViolation()
    {
        using var workspace = new WorkerPrReviewPreflightWorkspace();
        WorkerPrReviewPreflightCommand.PrLookupFactory = () => new FakePrLookup(BuildPr(
            number: 10,
            state: "OPEN",
            title: "PR carries intent-pr-created",
            body: "Closes #100",
            labelNames: new[] { "intent-target", "intent-pr-created" },
            closingIssueNumbers: new[] { 100 }));
        WorkerPrReviewPreflightCommand.IssueLookupFactory = () => new FakeIssueLookup(BuildIssue(
            number: 100,
            state: "OPEN",
            title: "Source",
            body: string.Empty,
            labelNames: new[] { "intent-target", "intent-pr-created" }));

        using var writer = new StringWriter();
        var exitCode = WorkerPrReviewPreflightCommand.Execute(
            workspace.Context,
            new[] { "--repo", "J-Tech-Japan/intent-system", "--pr", "10", "--format", "json" },
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<WorkerPrReviewPreflightResult>(writer.ToString())!;
        Assert.Equal(WorkerPrReviewPreflightConstants.Classifications.SourceIssueNotTarget, result.Classification);
        Assert.Equal(WorkerPrReviewPreflightConstants.RecommendedActions.LabelCleanupRequired, result.RecommendedAction);
        Assert.Contains(result.Reasons, r =>
            r.Contains("label-policy violation", StringComparison.Ordinal)
            && r.Contains("intent-pr-created", StringComparison.Ordinal)
            && r.Contains("PR", StringComparison.Ordinal));
    }

    [Fact]
    public void Execute_GivenPrBodyReferencingDifferentRepo_ClassifiesAsTargetMismatch()
    {
        using var workspace = new WorkerPrReviewPreflightWorkspace();
        WorkerPrReviewPreflightCommand.PrLookupFactory = () => new FakePrLookup(BuildPr(
            number: 11,
            state: "OPEN",
            title: "Cross-repo",
            body: "Repository: SomeOther/Repo\n\nCloses #100",
            labelNames: new[] { "intent-target" },
            closingIssueNumbers: new[] { 100 }));
        WorkerPrReviewPreflightCommand.IssueLookupFactory = () => new FakeIssueLookup(BuildIssue(
            number: 100,
            state: "OPEN",
            title: "Source",
            body: string.Empty,
            labelNames: new[] { "intent-target", "intent-pr-created" }));

        using var writer = new StringWriter();
        var exitCode = WorkerPrReviewPreflightCommand.Execute(
            workspace.Context,
            new[] { "--repo", "J-Tech-Japan/intent-system", "--pr", "11", "--format", "json" },
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<WorkerPrReviewPreflightResult>(writer.ToString())!;
        Assert.Equal(WorkerPrReviewPreflightConstants.Classifications.TargetMismatch, result.Classification);
        Assert.Equal(WorkerPrReviewPreflightConstants.RecommendedActions.SwitchRepo, result.RecommendedAction);
        Assert.Contains(result.Reasons, r =>
            r.Contains("SomeOther/Repo", StringComparison.Ordinal)
            && r.Contains("J-Tech-Japan/intent-system", StringComparison.Ordinal));
    }

    [Fact]
    public void Execute_GivenClosedNotMergedPr_ClassifiesAsNonActionable()
    {
        using var workspace = new WorkerPrReviewPreflightWorkspace();
        WorkerPrReviewPreflightCommand.PrLookupFactory = () => new FakePrLookup(BuildPr(
            number: 12,
            state: "CLOSED",
            title: "Closed",
            body: "Closes #100",
            labelNames: new[] { "intent-target" },
            closed: true,
            closingIssueNumbers: new[] { 100 }));

        using var writer = new StringWriter();
        var exitCode = WorkerPrReviewPreflightCommand.Execute(
            workspace.Context,
            new[] { "--repo", "J-Tech-Japan/intent-system", "--pr", "12", "--format", "json" },
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<WorkerPrReviewPreflightResult>(writer.ToString())!;
        Assert.Equal(WorkerPrReviewPreflightConstants.Classifications.NonActionable, result.Classification);
        Assert.Contains(result.Reasons, r => r.Contains("closed", StringComparison.Ordinal));
    }

    [Fact]
    public void Execute_GivenFreshDraftPrNoReviewLabel_ClassifiesAsNonActionable()
    {
        using var workspace = new WorkerPrReviewPreflightWorkspace();
        WorkerPrReviewPreflightCommand.PrLookupFactory = () => new FakePrLookup(BuildPr(
            number: 13,
            state: "OPEN",
            title: "Fresh draft",
            body: "Closes #100",
            labelNames: new[] { "intent-target" },
            isDraft: true,
            closingIssueNumbers: new[] { 100 }));

        using var writer = new StringWriter();
        var exitCode = WorkerPrReviewPreflightCommand.Execute(
            workspace.Context,
            new[] { "--repo", "J-Tech-Japan/intent-system", "--pr", "13", "--format", "json" },
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<WorkerPrReviewPreflightResult>(writer.ToString())!;
        Assert.Equal(WorkerPrReviewPreflightConstants.Classifications.NonActionable, result.Classification);
        Assert.Contains(result.Reasons, r => r.Contains("draft", StringComparison.Ordinal));
    }

    [Fact]
    public void Execute_GivenJsonFormat_RoundTripsStably()
    {
        using var workspace = new WorkerPrReviewPreflightWorkspace();
        WorkerPrReviewPreflightCommand.PrLookupFactory = () => new FakePrLookup(BuildPr(
            number: 14,
            state: "OPEN",
            title: "Round trip",
            body: "Closes #100",
            labelNames: new[] { "intent-target" },
            closingIssueNumbers: new[] { 100 }));
        WorkerPrReviewPreflightCommand.IssueLookupFactory = () => new FakeIssueLookup(BuildIssue(
            number: 100,
            state: "OPEN",
            title: "Source",
            body: string.Empty,
            labelNames: new[] { "intent-target", "intent-pr-created" }));

        using var writer = new StringWriter();
        var exitCode = WorkerPrReviewPreflightCommand.Execute(
            workspace.Context,
            new[] { "--repo", "J-Tech-Japan/intent-system", "--pr", "14", "--format", "json" },
            writer);

        Assert.Equal(0, exitCode);
        var first = JsonSerializer.Deserialize<WorkerPrReviewPreflightResult>(writer.ToString())!;
        var serialized = JsonSerializer.Serialize(first);
        var second = JsonSerializer.Deserialize<WorkerPrReviewPreflightResult>(serialized)!;

        Assert.Equal(first.Actionable, second.Actionable);
        Assert.Equal(first.Classification, second.Classification);
        Assert.Equal(first.Pr, second.Pr);
        Assert.Equal(first.Repo, second.Repo);
        Assert.Equal(first.Title, second.Title);
        Assert.Equal(first.PrState, second.PrState);
        Assert.Equal(first.IsDraft, second.IsDraft);
        Assert.Equal(first.Labels, second.Labels);
        Assert.Equal(first.SourceIssue, second.SourceIssue);
        Assert.Equal(first.SourceIssueLabels, second.SourceIssueLabels);
        Assert.Equal(first.Reasons, second.Reasons);
        Assert.Equal(first.RecommendedAction, second.RecommendedAction);
        Assert.Equal(first.SummaryLine, second.SummaryLine);
    }

    [Fact]
    public void Execute_GivenJsonFormat_FieldsMatchIssueAcceptanceCriteria()
    {
        using var workspace = new WorkerPrReviewPreflightWorkspace();
        WorkerPrReviewPreflightCommand.PrLookupFactory = () => new FakePrLookup(BuildPr(
            number: 15,
            state: "OPEN",
            title: "Field check",
            body: "Closes #100",
            labelNames: new[] { "intent-target" },
            closingIssueNumbers: new[] { 100 }));
        WorkerPrReviewPreflightCommand.IssueLookupFactory = () => new FakeIssueLookup(BuildIssue(
            number: 100,
            state: "OPEN",
            title: "Source",
            body: string.Empty,
            labelNames: new[] { "intent-target", "intent-pr-created" }));

        using var writer = new StringWriter();
        var exitCode = WorkerPrReviewPreflightCommand.Execute(
            workspace.Context,
            new[] { "--repo", "J-Tech-Japan/intent-system", "--pr", "15", "--format", "json" },
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        // Required fields named in the issue's minimum example.
        Assert.True(root.TryGetProperty("actionable", out _));
        Assert.True(root.TryGetProperty("classification", out _));
        Assert.True(root.TryGetProperty("pr", out _));
        Assert.True(root.TryGetProperty("repo", out _));
        Assert.True(root.TryGetProperty("sourceIssue", out var camelSource));
        Assert.True(root.TryGetProperty("reasons", out _));
        Assert.True(root.TryGetProperty("recommendedAction", out var camelAction));

        // Both camelCase and snake_case keys must be present.
        Assert.True(root.TryGetProperty("source_issue", out var snakeSource));
        Assert.True(root.TryGetProperty("recommended_action", out var snakeAction));
        Assert.Equal(snakeAction.GetString(), camelAction.GetString());
        if (snakeSource.ValueKind == JsonValueKind.Number)
        {
            Assert.Equal(snakeSource.GetInt32(), camelSource.GetInt32());
        }
    }

    [Fact]
    public void Execute_GivenJsonFormat_CamelCaseAliasesMatchIssueContract()
    {
        using var workspace = new WorkerPrReviewPreflightWorkspace();
        WorkerPrReviewPreflightCommand.PrLookupFactory = () => new FakePrLookup(BuildPr(
            number: 16,
            state: "OPEN",
            title: "Camel alias",
            body: "Closes #100",
            labelNames: new[] { "intent-target" },
            closingIssueNumbers: new[] { 100 }));
        WorkerPrReviewPreflightCommand.IssueLookupFactory = () => new FakeIssueLookup(BuildIssue(
            number: 100,
            state: "OPEN",
            title: "Source",
            body: string.Empty,
            labelNames: new[] { "intent-target", "intent-pr-created" }));

        using var writer = new StringWriter();
        var exitCode = WorkerPrReviewPreflightCommand.Execute(
            workspace.Context,
            new[] { "--repo", "J-Tech-Japan/intent-system", "--pr", "16", "--format", "json" },
            writer);

        Assert.Equal(0, exitCode);
        var stdout = writer.ToString();
        Assert.Contains("\"sourceIssue\"", stdout, StringComparison.Ordinal);
        Assert.Contains("\"recommendedAction\"", stdout, StringComparison.Ordinal);

        using var document = JsonDocument.Parse(stdout);
        var root = document.RootElement;
        Assert.True(root.TryGetProperty("recommendedAction", out var camelAction));
        Assert.True(root.TryGetProperty("recommended_action", out var snakeAction));
        Assert.Equal(snakeAction.GetString(), camelAction.GetString());
        Assert.Equal(WorkerPrReviewPreflightConstants.RecommendedActions.Review, camelAction.GetString());

        Assert.True(root.TryGetProperty("sourceIssue", out var camelSource));
        Assert.True(root.TryGetProperty("source_issue", out var snakeSource));
        Assert.Equal(snakeSource.GetInt32(), camelSource.GetInt32());
        Assert.Equal(100, camelSource.GetInt32());
    }

    [Fact]
    public void Execute_GivenSourceIssueViaClosingIssuesMetadata_TracesCorrectly()
    {
        using var workspace = new WorkerPrReviewPreflightWorkspace();
        WorkerPrReviewPreflightCommand.PrLookupFactory = () => new FakePrLookup(BuildPr(
            number: 17,
            state: "OPEN",
            title: "Closing-issues metadata",
            body: "No body issue references at all.",
            labelNames: new[] { "intent-target" },
            closingIssueNumbers: new[] { 100 }));
        WorkerPrReviewPreflightCommand.IssueLookupFactory = () => new FakeIssueLookup(BuildIssue(
            number: 100,
            state: "OPEN",
            title: "Source",
            body: string.Empty,
            labelNames: new[] { "intent-target", "intent-pr-created" }));

        using var writer = new StringWriter();
        var exitCode = WorkerPrReviewPreflightCommand.Execute(
            workspace.Context,
            new[] { "--repo", "J-Tech-Japan/intent-system", "--pr", "17", "--format", "json" },
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<WorkerPrReviewPreflightResult>(writer.ToString())!;
        Assert.Equal(100, result.SourceIssue);
    }

    [Fact]
    public void Execute_GivenSourceIssueViaPrBodyClosesReference_TracesCorrectly()
    {
        using var workspace = new WorkerPrReviewPreflightWorkspace();
        WorkerPrReviewPreflightCommand.PrLookupFactory = () => new FakePrLookup(BuildPr(
            number: 18,
            state: "OPEN",
            title: "Body Closes ref",
            body: "Some text. Closes #100. More text.",
            labelNames: new[] { "intent-target" },
            closingIssueNumbers: Array.Empty<int>()));
        WorkerPrReviewPreflightCommand.IssueLookupFactory = () => new FakeIssueLookup(BuildIssue(
            number: 100,
            state: "OPEN",
            title: "Source",
            body: string.Empty,
            labelNames: new[] { "intent-target", "intent-pr-created" }));

        using var writer = new StringWriter();
        var exitCode = WorkerPrReviewPreflightCommand.Execute(
            workspace.Context,
            new[] { "--repo", "J-Tech-Japan/intent-system", "--pr", "18", "--format", "json" },
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<WorkerPrReviewPreflightResult>(writer.ToString())!;
        Assert.Equal(100, result.SourceIssue);
        Assert.Equal(WorkerPrReviewPreflightConstants.Classifications.ReadyToReview, result.Classification);
    }

    [Fact]
    public void Execute_PrecedenceClosedBeatsLabels()
    {
        using var workspace = new WorkerPrReviewPreflightWorkspace();
        WorkerPrReviewPreflightCommand.PrLookupFactory = () => new FakePrLookup(BuildPr(
            number: 19,
            state: "CLOSED",
            title: "Closed with reviewing label",
            body: "Closes #100",
            labelNames: new[] { "intent-target", "intent-pr-reviewing" },
            closed: true,
            closingIssueNumbers: new[] { 100 }));

        using var writer = new StringWriter();
        var exitCode = WorkerPrReviewPreflightCommand.Execute(
            workspace.Context,
            new[] { "--repo", "J-Tech-Japan/intent-system", "--pr", "19", "--format", "json" },
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<WorkerPrReviewPreflightResult>(writer.ToString())!;
        Assert.Equal(WorkerPrReviewPreflightConstants.Classifications.NonActionable, result.Classification);
    }

    [Fact]
    public void Execute_PrecedenceApprovedBeatsRequestUpdate()
    {
        using var workspace = new WorkerPrReviewPreflightWorkspace();
        WorkerPrReviewPreflightCommand.PrLookupFactory = () => new FakePrLookup(BuildPr(
            number: 20,
            state: "OPEN",
            title: "Approved + request",
            body: "Closes #100",
            labelNames: new[] { "intent-target", "intent-pr-approved", "intent-pr-request-update" },
            closingIssueNumbers: new[] { 100 }));
        WorkerPrReviewPreflightCommand.IssueLookupFactory = () => new FakeIssueLookup(BuildIssue(
            number: 100,
            state: "OPEN",
            title: "Source",
            body: string.Empty,
            labelNames: new[] { "intent-target", "intent-pr-created" }));

        using var writer = new StringWriter();
        var exitCode = WorkerPrReviewPreflightCommand.Execute(
            workspace.Context,
            new[] { "--repo", "J-Tech-Japan/intent-system", "--pr", "20", "--format", "json" },
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<WorkerPrReviewPreflightResult>(writer.ToString())!;
        Assert.Equal(WorkerPrReviewPreflightConstants.Classifications.ApprovedOrMerged, result.Classification);
    }

    [Fact]
    public void Execute_PrecedenceUpdateInProgressBeatsRequestUpdate()
    {
        using var workspace = new WorkerPrReviewPreflightWorkspace();
        WorkerPrReviewPreflightCommand.PrLookupFactory = () => new FakePrLookup(BuildPr(
            number: 21,
            state: "OPEN",
            title: "Update + request",
            body: "Closes #100",
            labelNames: new[] { "intent-target", "intent-pr-update-in-progress", "intent-pr-request-update" },
            closingIssueNumbers: new[] { 100 }));
        WorkerPrReviewPreflightCommand.IssueLookupFactory = () => new FakeIssueLookup(BuildIssue(
            number: 100,
            state: "OPEN",
            title: "Source",
            body: string.Empty,
            labelNames: new[] { "intent-target", "intent-pr-created" }));

        using var writer = new StringWriter();
        var exitCode = WorkerPrReviewPreflightCommand.Execute(
            workspace.Context,
            new[] { "--repo", "J-Tech-Japan/intent-system", "--pr", "21", "--format", "json" },
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<WorkerPrReviewPreflightResult>(writer.ToString())!;
        Assert.Equal(WorkerPrReviewPreflightConstants.Classifications.UpdateInProgress, result.Classification);
    }

    [Fact]
    public void Execute_GivenPrLookupTransportFailure_ReturnsNonZero_ActionableErrorMessage()
    {
        using var workspace = new WorkerPrReviewPreflightWorkspace();
        WorkerPrReviewPreflightCommand.PrLookupFactory = () => new ThrowingPrLookup(
            "simulated network failure: gh pr view exited 1");

        using var writer = new StringWriter();
        var exitCode = WorkerPrReviewPreflightCommand.Execute(
            workspace.Context,
            new[] { "--repo", "J-Tech-Japan/intent-system", "--pr", "511", "--format", "json" },
            writer);

        Assert.Equal(1, exitCode);
        var output = writer.ToString();
        Assert.Contains("simulated network failure", output, StringComparison.Ordinal);
        Assert.Contains("511", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenIssueLookupTransportFailure_ReturnsNonZero_ActionableErrorMessage()
    {
        using var workspace = new WorkerPrReviewPreflightWorkspace();
        WorkerPrReviewPreflightCommand.PrLookupFactory = () => new FakePrLookup(BuildPr(
            number: 22,
            state: "OPEN",
            title: "Issue lookup throws",
            body: "Closes #100",
            labelNames: new[] { "intent-target" },
            closingIssueNumbers: new[] { 100 }));
        WorkerPrReviewPreflightCommand.IssueLookupFactory = () =>
            new ThrowingIssueLookup("simulated issue-lookup failure: gh issue view exited 1");

        using var writer = new StringWriter();
        var exitCode = WorkerPrReviewPreflightCommand.Execute(
            workspace.Context,
            new[] { "--repo", "J-Tech-Japan/intent-system", "--pr", "22", "--format", "json" },
            writer);

        Assert.Equal(1, exitCode);
        var output = writer.ToString();
        Assert.Contains("simulated issue-lookup failure", output, StringComparison.Ordinal);
        Assert.Contains("100", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenMissingRepoFlag_ReturnsNonZero()
    {
        using var workspace = new WorkerPrReviewPreflightWorkspace();
        using var writer = new StringWriter();
        var exitCode = WorkerPrReviewPreflightCommand.Execute(
            workspace.Context,
            new[] { "--pr", "1" },
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("--repo", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenMissingPrFlag_ReturnsNonZero()
    {
        using var workspace = new WorkerPrReviewPreflightWorkspace();
        using var writer = new StringWriter();
        var exitCode = WorkerPrReviewPreflightCommand.Execute(
            workspace.Context,
            new[] { "--repo", "J-Tech-Japan/intent-system" },
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("--pr", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenNonNumericPr_ReturnsNonZero()
    {
        using var workspace = new WorkerPrReviewPreflightWorkspace();
        using var writer = new StringWriter();
        var exitCode = WorkerPrReviewPreflightCommand.Execute(
            workspace.Context,
            new[] { "--repo", "J-Tech-Japan/intent-system", "--pr", "abc" },
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("--pr", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenUnknownArgument_ReturnsNonZero()
    {
        using var workspace = new WorkerPrReviewPreflightWorkspace();
        using var writer = new StringWriter();
        var exitCode = WorkerPrReviewPreflightCommand.Execute(
            workspace.Context,
            new[] { "--repo", "J-Tech-Japan/intent-system", "--pr", "1", "--bogus" },
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("--bogus", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_NeverWritesToQueueStateOrRunsLog()
    {
        using var workspace = new WorkerPrReviewPreflightWorkspace();
        WorkerPrReviewPreflightCommand.PrLookupFactory = () => new FakePrLookup(BuildPr(
            number: 23,
            state: "OPEN",
            title: "Snapshot",
            body: "Closes #100",
            labelNames: new[] { "intent-target" },
            closingIssueNumbers: new[] { 100 }));
        WorkerPrReviewPreflightCommand.IssueLookupFactory = () => new FakeIssueLookup(BuildIssue(
            number: 100,
            state: "OPEN",
            title: "Source",
            body: string.Empty,
            labelNames: new[] { "intent-target", "intent-pr-created" }));

        var queueStatePath = Path.Combine(workspace.RootPath, ".intent-cli", "queue-state.json");
        var runsLogPath = Path.Combine(workspace.RootPath, ".intent-cli", "runs.jsonl");
        File.WriteAllText(queueStatePath, "{\"queue\":[]}");
        File.WriteAllText(runsLogPath, "{\"event\":\"baseline\"}\n");

        var queueBefore = File.ReadAllBytes(queueStatePath);
        var runsBefore = File.ReadAllBytes(runsLogPath);

        using var writer = new StringWriter();
        var exitCode = WorkerPrReviewPreflightCommand.Execute(
            workspace.Context,
            new[] { "--repo", "J-Tech-Japan/intent-system", "--pr", "23", "--format", "json" },
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
        using var workspace = new WorkerPrReviewPreflightWorkspace();
        WorkerPrReviewPreflightCommand.PrLookupFactory = () => new FakePrLookup(BuildPr(
            number: 24,
            state: "OPEN",
            title: "No writes",
            body: "Closes #100",
            labelNames: new[] { "intent-target" },
            closingIssueNumbers: new[] { 100 }));
        WorkerPrReviewPreflightCommand.IssueLookupFactory = () => new FakeIssueLookup(BuildIssue(
            number: 100,
            state: "OPEN",
            title: "Source",
            body: string.Empty,
            labelNames: new[] { "intent-target", "intent-pr-created" }));

        File.WriteAllText(Path.Combine(workspace.RootPath, "baseline.txt"), "baseline");
        var before = workspace.SnapshotWorkspace();

        using var writer = new StringWriter();
        var exitCode = WorkerPrReviewPreflightCommand.Execute(
            workspace.Context,
            new[] { "--repo", "J-Tech-Japan/intent-system", "--pr", "24", "--format", "json" },
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
        using var workspace = new WorkerPrReviewPreflightWorkspace();
        var invoked = false;
        WorkerPrReviewPreflightCommand.NestedProviderLauncher = () =>
        {
            invoked = true;
            return false;
        };
        WorkerPrReviewPreflightCommand.PrLookupFactory = () => new FakePrLookup(BuildPr(
            number: 25,
            state: "OPEN",
            title: "No provider",
            body: "Closes #100",
            labelNames: new[] { "intent-target" },
            closingIssueNumbers: new[] { 100 }));
        WorkerPrReviewPreflightCommand.IssueLookupFactory = () => new FakeIssueLookup(BuildIssue(
            number: 100,
            state: "OPEN",
            title: "Source",
            body: string.Empty,
            labelNames: new[] { "intent-target", "intent-pr-created" }));

        using var writer = new StringWriter();
        var exitCode = WorkerPrReviewPreflightCommand.Execute(
            workspace.Context,
            new[] { "--repo", "J-Tech-Japan/intent-system", "--pr", "25" },
            writer);

        Assert.Equal(0, exitCode);
        Assert.False(invoked, "NestedProviderLauncher must never be invoked by the preflight command");

        var commandSource = File.ReadAllText(LocateSourceFile("WorkerPrReviewPreflightCommand.cs"));
        var analyzerSource = File.ReadAllText(LocateSourceFile("WorkerPrReviewPreflightAnalyzer.cs"));
        var resultSource = File.ReadAllText(LocateSourceFile("WorkerPrReviewPreflightResult.cs"));

        Assert.DoesNotContain("Process.Start(", commandSource, StringComparison.Ordinal);
        Assert.DoesNotContain("Process.Start(", analyzerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("Process.Start(", resultSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_NeverMutatesAnyLabelOrPr_NoGhEditOrMergeCallInAnalyzer()
    {
        var commandSource = File.ReadAllText(LocateSourceFile("WorkerPrReviewPreflightCommand.cs"));
        var analyzerSource = File.ReadAllText(LocateSourceFile("WorkerPrReviewPreflightAnalyzer.cs"));

        foreach (var literal in new[]
        {
            "gh issue edit",
            "gh pr edit",
            "gh pr merge",
            "gh pr close",
            "gh pr reopen",
            "gh pr comment",
            "gh pr review"
        })
        {
            Assert.DoesNotContain(literal, commandSource, StringComparison.Ordinal);
            Assert.DoesNotContain(literal, analyzerSource, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Execute_GivenTextFormat_PrintsStableSummaryLine()
    {
        using var workspace = new WorkerPrReviewPreflightWorkspace();
        WorkerPrReviewPreflightCommand.PrLookupFactory = () => new FakePrLookup(BuildPr(
            number: 26,
            state: "OPEN",
            title: "Text format",
            body: "Closes #100",
            labelNames: new[] { "intent-target" },
            closingIssueNumbers: new[] { 100 }));
        WorkerPrReviewPreflightCommand.IssueLookupFactory = () => new FakeIssueLookup(BuildIssue(
            number: 100,
            state: "OPEN",
            title: "Source",
            body: string.Empty,
            labelNames: new[] { "intent-target", "intent-pr-created" }));

        using var writer = new StringWriter();
        var exitCode = WorkerPrReviewPreflightCommand.Execute(
            workspace.Context,
            new[] { "--repo", "J-Tech-Japan/intent-system", "--pr", "26" },
            writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("# Worker pr-review-preflight for J-Tech-Japan/intent-system#26", output, StringComparison.Ordinal);
        Assert.Contains("classification=ready-to-review", output, StringComparison.Ordinal);
        Assert.Contains("actionable=true", output, StringComparison.Ordinal);
    }

    private static string LocateSourceFile(string fileName)
    {
        var directory = AppContext.BaseDirectory;
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

    private static GitHubPrLookupResult BuildPr(
        int number,
        string state,
        string title,
        string body,
        string[] labelNames,
        bool isDraft = false,
        bool closed = false,
        bool merged = false,
        int[]? closingIssueNumbers = null)
    {
        var refs = (closingIssueNumbers ?? Array.Empty<int>())
            .Select(n => new GitHubPrClosingIssueReference { Number = n })
            .ToArray();

        return new GitHubPrLookupResult
        {
            Number = number,
            State = state,
            Title = title,
            Body = body,
            IsDraft = isDraft,
            Closed = closed,
            Merged = merged,
            MergedAt = merged ? "2025-01-01T00:00:00Z" : null,
            ClosedAt = (closed || merged) ? "2025-01-01T00:00:00Z" : null,
            Labels = labelNames.Select(n => new GitHubPrLabel { Name = n }).ToArray(),
            ClosingIssuesReferences = refs
        };
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

    private sealed class FakePrLookup : IGitHubPrLookup
    {
        private readonly GitHubPrLookupResult payload;

        public FakePrLookup(GitHubPrLookupResult payload)
        {
            this.payload = payload;
        }

        public GitHubPrLookupResult Lookup(string repo, int prNumber) => payload;
    }

    private sealed class ThrowingPrLookup : IGitHubPrLookup
    {
        private readonly string message;

        public ThrowingPrLookup(string message)
        {
            this.message = message;
        }

        public GitHubPrLookupResult Lookup(string repo, int prNumber)
        {
            throw new InvalidOperationException(message);
        }
    }

    private sealed class FakeIssueLookup : IGitHubIssueLookup
    {
        private readonly GitHubIssueLookupResult payload;

        public FakeIssueLookup(GitHubIssueLookupResult payload)
        {
            this.payload = payload;
        }

        public GitHubIssueLookupResult Lookup(string repo, int issueNumber) => payload;
    }

    private sealed class ThrowingIssueLookup : IGitHubIssueLookup
    {
        private readonly string message;

        public ThrowingIssueLookup(string message)
        {
            this.message = message;
        }

        public GitHubIssueLookupResult Lookup(string repo, int issueNumber)
        {
            throw new InvalidOperationException(message);
        }
    }

    private sealed class WorkerPrReviewPreflightWorkspace : IDisposable
    {
        public WorkerPrReviewPreflightWorkspace()
        {
            RootPath = Directory.CreateTempSubdirectory("worker-pr-review-preflight-tests-").FullName;
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
