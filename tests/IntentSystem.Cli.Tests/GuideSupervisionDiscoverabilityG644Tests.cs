using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G644: supervision must be reachable from the guides a role actually reads,
/// and a missing recorded cycle must be detectable through guide next.
/// </summary>
public sealed class GuideSupervisionDiscoverabilityG644Tests : IDisposable
{
    private readonly string root = Directory.CreateTempSubdirectory("g644-guide-supervision-").FullName;

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void InitHost_NamesSupervisionSetupForHostRoles_NotChildWorker()
    {
        using var writer = new StringWriter();
        var exitCode = GuideWorkflowTaskInitHostCommand.Execute(
            CreateContext(),
            ["--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var roles = document.RootElement.GetProperty("roles").EnumerateArray().ToArray();

        var design = roles.Single(role => role.GetProperty("role").GetString() == "design");
        var review = roles.Single(role => role.GetProperty("role").GetString() == "review-runtime");
        var child = roles.Single(role => role.GetProperty("role").GetString() == "child-worker");

        foreach (var role in new[] { design, review })
        {
            var setup = role.GetProperty("supervision_setup").GetString()!;
            Assert.Contains("one standing", setup, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("outside the agent seats", setup, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("neither starts nor manages", setup, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("measured on this host", setup, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("12-agent-message-orchestration.md", setup, StringComparison.Ordinal);
            Assert.Contains("Preview through 1.x", setup, StringComparison.Ordinal);
        }

        Assert.False(child.TryGetProperty("supervision_setup", out _));
    }

    [Fact]
    public void ReviewNextSliceLoop_NamesDeploymentFacts_WhileImplementationLoopDoesNot()
    {
        using var reviewWriter = new StringWriter();
        var reviewExitCode = GuideWorkflowTaskReviewNextSliceLoopCommand.Execute(
            CreateContext(),
            ["--domain", "intent-cli", "--target-repo", "example/repo", "--agent", "claude", "--format", "json"],
            reviewWriter);

        Assert.Equal(0, reviewExitCode);
        using var reviewDocument = JsonDocument.Parse(reviewWriter.ToString());
        var reviewPrompt = reviewDocument.RootElement.GetProperty("prompt").GetString()!;
        Assert.Contains("Supervision setup (G644)", reviewPrompt, StringComparison.Ordinal);
        Assert.Contains("exactly one standing", reviewPrompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("outside the agent seats", reviewPrompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("omitting `--once`", reviewPrompt, StringComparison.Ordinal);
        Assert.Contains("two supervisors on one team", reviewPrompt, StringComparison.Ordinal);
        Assert.Contains("neither starts nor manages", reviewPrompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("12-agent-message-orchestration.md", reviewPrompt, StringComparison.Ordinal);
        Assert.Contains("Preview through 1.x", reviewPrompt, StringComparison.Ordinal);

        using var implementationWriter = new StringWriter();
        var implementationExitCode = GuideWorkflowTaskImplementationLoopCommand.Execute(
            CreateContext(),
            ["--target-repo", "example/repo", "--agent", "claude", "--format", "json"],
            implementationWriter);

        Assert.Equal(0, implementationExitCode);
        using var implementationDocument = JsonDocument.Parse(implementationWriter.ToString());
        var implementationPrompt = implementationDocument.RootElement.GetProperty("prompt").GetString()!;
        Assert.DoesNotContain("Supervision setup (G644)", implementationPrompt, StringComparison.Ordinal);
    }

    [Fact]
    public void GuideNext_RecommendsMissingCycle_ThenStaysSilentAfterCycle()
    {
        var context = CreateContext();

        using var missingWriter = new StringWriter();
        var missingExitCode = GuideNextCommand.Execute(
            context,
            ["--domain", "intent-cli", "--team", "intent-cli-dev", "--target-repo", "example/repo", "--format", "json"],
            missingWriter);

        Assert.Equal(0, missingExitCode);
        using var missingDocument = JsonDocument.Parse(missingWriter.ToString());
        var missingRoot = missingDocument.RootElement;
        Assert.True(missingRoot.GetProperty("supervision").GetProperty("checked").GetBoolean());
        Assert.False(missingRoot.GetProperty("supervision").GetProperty("cycle_recorded").GetBoolean());
        Assert.True(missingRoot.GetProperty("supervision").GetProperty("setup_recommended").GetBoolean());
        Assert.Equal("supervision-setup", missingRoot.GetProperty("decision_set")[0].GetProperty("action").GetString());
        Assert.Contains("--team intent-cli-dev", missingRoot.GetProperty("decision_set")[0].GetProperty("suggested_prompt").GetString()!, StringComparison.Ordinal);

        var cyclePath = NotifySupervisionStore.ResolveCyclePath(
            context.ResolveSupervisionArtifactRootPath(),
            "intent-cli",
            "intent-cli-dev");
        var now = DateTimeOffset.UtcNow;
        var recorded = NotifySupervisionStore.RecordCycle(
            cyclePath,
            new NotifySupervisionCycle
            {
                CycleId = "g644-test-cycle",
                StartedAt = now,
                CompletedAt = now,
                IntervalSeconds = 300,
            },
            write: true);
        Assert.True(recorded.Applied, recorded.Error);

        using var recordedWriter = new StringWriter();
        var recordedExitCode = GuideNextCommand.Execute(
            context,
            ["--domain", "intent-cli", "--team", "intent-cli-dev", "--target-repo", "example/repo", "--format", "json"],
            recordedWriter);

        Assert.Equal(0, recordedExitCode);
        using var recordedDocument = JsonDocument.Parse(recordedWriter.ToString());
        var recordedRoot = recordedDocument.RootElement;
        Assert.True(recordedRoot.GetProperty("supervision").GetProperty("cycle_recorded").GetBoolean());
        Assert.False(recordedRoot.GetProperty("supervision").GetProperty("setup_recommended").GetBoolean());
        Assert.DoesNotContain(
            recordedRoot.GetProperty("decision_set").EnumerateArray(),
            action => action.GetProperty("action").GetString() == "supervision-setup");
    }

    [Fact]
    public void EnglishAndJapaneseCommandReferences_PointToThePreviewGuideSurface()
    {
        var root = RepoVersionPolicySource.RepoRoot();
        var english = File.ReadAllText(Path.Combine(root, "docs", "en", "08-command-reference.md"));
        var japanese = File.ReadAllText(Path.Combine(root, "docs", "ja", "08-command-reference.md"));

        foreach (var document in new[] { english, japanese })
        {
            Assert.Contains("--team <team>", document, StringComparison.Ordinal);
            Assert.Contains("supervision-setup", document, StringComparison.Ordinal);
            Assert.Contains("12-agent-message-orchestration.md", document, StringComparison.Ordinal);
            Assert.Contains("1.x", document, StringComparison.Ordinal);
        }
    }

    private CliContext CreateContext() => new()
    {
        RepoRoot = root,
        Config = new CliConfig
        {
            Project = new ProjectConfig
            {
                Domain = "intent-cli",
                ArtifactRoot = ".intent-cli",
                WorktreeRoot = ".intent-cli/worktrees",
            },
            Supervision = new SupervisionConfig
            {
                ArtifactRoot = ".intent-cli/supervision",
            },
        },
    };
}
