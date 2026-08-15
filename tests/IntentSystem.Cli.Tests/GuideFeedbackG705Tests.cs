using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G705: project feedback is a public, render-only guide surface. These tests
/// keep the useful filing form discoverable while guarding that no send,
/// process, network, telemetry, or issue-publishing boundary is introduced.
/// </summary>
public sealed class GuideFeedbackG705Tests
{
    [Fact]
    public void JsonRoute_RendersPublicChannelWarningShapeAndNoSendContract()
    {
        using var writer = new StringWriter();
        Assert.Equal(0, CommandRouter.Execute(
            ["guide", "feedback", "--format", "json"],
            BareContext(),
            writer));

        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.Equal("project-feedback", root.GetProperty("surface").GetString());
        Assert.Equal("J-Tech-Japan/intent-system", root.GetProperty("repository").GetString());
        Assert.Equal(
            "https://github.com/J-Tech-Japan/intent-system/issues",
            root.GetProperty("issues_url").GetString());
        Assert.True(root.GetProperty("render_only").GetBoolean());

        var warning = root.GetProperty("public_channel_warning").GetString()!;
        foreach (var marker in new[]
        {
            "PUBLIC / WORLD-READABLE PERMANENTLY",
            "credentials or tokens",
            "private hostnames",
            "private paths",
            "customer or personal data",
            "internal URLs",
            "Review pasted logs",
        })
        {
            Assert.Contains(marker, warning, StringComparison.OrdinalIgnoreCase);
        }

        var reportShape = root.GetProperty("recommended_report_shape");
        Assert.Contains("Recommendations only; never required gates", reportShape.GetProperty("recommendation_status").GetString()!, StringComparison.Ordinal);
        var elements = reportShape.GetProperty("elements").EnumerateArray().Select(element => element.GetString()!).ToArray();
        foreach (var marker in new[]
        {
            "Exact installed version string",
            "Timestamped observations",
            "Expected versus actual",
            "Reproduction context",
            "verified-versus-assumed",
        })
        {
            Assert.Contains(elements, element => element.Contains(marker, StringComparison.OrdinalIgnoreCase));
        }

        var aiSeatRule = root.GetProperty("ai_seat_rule").GetString()!;
        Assert.Contains("draft", aiSeatRule, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("design thread", aiSeatRule, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("operator", aiSeatRule, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("per-action", aiSeatRule, StringComparison.Ordinal);
        Assert.Contains("standing authority", aiSeatRule, StringComparison.OrdinalIgnoreCase);

        Assert.Contains("execution-unit child issue publishing", root.GetProperty("scope_boundary").GetString()!, StringComparison.Ordinal);
        var noSend = string.Join("\n", root.GetProperty("no_send_invariants").EnumerateArray().Select(value => value.GetString()));
        Assert.Contains("never executes", noSend, StringComparison.Ordinal);
        Assert.Contains("API POST", noSend, StringComparison.Ordinal);
        Assert.Contains("network connection", noSend, StringComparison.Ordinal);
        Assert.Contains("subprocess", noSend, StringComparison.Ordinal);
        Assert.Contains("telemetry", noSend, StringComparison.Ordinal);
        Assert.Contains("gh issue create", root.GetProperty("filing_command").GetString()!, StringComparison.Ordinal);
    }

    [Fact]
    public void MarkdownRoute_RendersCommandFormButNoSubmissionSwitch()
    {
        using var writer = new StringWriter();
        Assert.Equal(0, CommandRouter.Execute(
            ["guide", "feedback", "--format", "markdown"],
            BareContext(),
            writer));

        var output = writer.ToString();
        Assert.Contains("# intent-cli guide feedback (G705)", output, StringComparison.Ordinal);
        Assert.Contains("J-Tech-Japan/intent-system", output, StringComparison.Ordinal);
        Assert.Contains("https://github.com/J-Tech-Japan/intent-system/issues", output, StringComparison.Ordinal);
        Assert.Contains("gh issue create --repo J-Tech-Japan/intent-system", output, StringComparison.Ordinal);
        Assert.Contains("PUBLIC / WORLD-READABLE PERMANENTLY", output, StringComparison.Ordinal);
        Assert.Contains("Recommendations only; never required gates", output, StringComparison.Ordinal);
        Assert.Contains("AI seat may draft", output, StringComparison.Ordinal);
        Assert.Contains("execution-unit child issue publishing", output, StringComparison.Ordinal);
        Assert.Contains("## No-send invariants", output, StringComparison.Ordinal);

        // The guide has no write or confirmation option that could turn the
        // rendered form into a one-command submission path.
        Assert.DoesNotContain("--write", output, StringComparison.Ordinal);
        Assert.DoesNotContain("--confirm", output, StringComparison.Ordinal);
        Assert.DoesNotContain("issue-publish", output, StringComparison.Ordinal);
        Assert.DoesNotContain("intent-cli issue create", output, StringComparison.Ordinal);
    }

    [Fact]
    public void OnboardingPointer_ExposesBothFormatsAndPreservesBareRoute()
    {
        using var jsonWriter = new StringWriter();
        Assert.Equal(0, GuideOnboardingCommand.Execute(
            BareContext(),
            ["--format", "json"],
            jsonWriter));

        using var document = JsonDocument.Parse(jsonWriter.ToString());
        var root = document.RootElement;
        var feedback = root.GetProperty("feedback_guidance");
        Assert.Equal("intent-cli guide feedback --format json", feedback.GetProperty("json_command").GetString());
        Assert.Equal("intent-cli guide feedback --format markdown", feedback.GetProperty("markdown_command").GetString());
        Assert.Contains("public GitHub issue channel", feedback.GetProperty("pointer").GetString()!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("AI seat drafts only", feedback.GetProperty("pointer").GetString()!, StringComparison.Ordinal);
        Assert.Contains("no gh issue create execution", feedback.GetProperty("no_mutation").GetString()!, StringComparison.Ordinal);

        // The legacy ten-step first-call sequence is compatibility-sensitive;
        // feedback is an explicit onboarding pointer rather than a new
        // mandatory step in that sequence.
        Assert.Equal(10, root.GetProperty("first_call_sequence").GetArrayLength());

        using var markdownWriter = new StringWriter();
        Assert.Equal(0, GuideOnboardingCommand.Execute(BareContext(), [], markdownWriter));
        var markdown = markdownWriter.ToString();
        Assert.Contains("## Project feedback route (G705)", markdown, StringComparison.Ordinal);
        Assert.Contains("intent-cli guide feedback --format json", markdown, StringComparison.Ordinal);
        Assert.Contains("intent-cli guide feedback --format markdown", markdown, StringComparison.Ordinal);
        Assert.Contains("no gh issue create execution", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void SourceAndBehavior_HaveNoIssueCreationOrTransmissionBoundary()
    {
        var repo = RepoVersionPolicySource.RepoRoot();
        var sourcePath = Path.Combine(repo, "src", "IntentSystem.Cli", "Commands", "GuideFeedbackCommand.cs");
        var source = File.ReadAllText(sourcePath);

        foreach (var forbiddenSourceToken in new[]
        {
            "Process.Start",
            "ProcessStartInfo",
            "HttpClient",
            "HttpWebRequest",
            "PostAsync",
            "GhIssueCreator",
            "IssueCreateCommand",
            "File.WriteAllText",
            "File.AppendAllText",
        })
        {
            Assert.DoesNotContain(forbiddenSourceToken, source, StringComparison.Ordinal);
        }

        var nonexistentBareRoot = Path.Combine(
            repo,
            ".artifacts",
            $"g705-test-bare-no-write-{Guid.NewGuid():N}");
        Assert.False(Directory.Exists(nonexistentBareRoot));

        using var writer = new StringWriter();
        Assert.Equal(0, GuideFeedbackCommand.Execute(
            new CliContext
            {
                RepoRoot = nonexistentBareRoot,
                Config = DefaultConfig(),
            },
            ["--format", "json"],
            writer));
        Assert.False(Directory.Exists(nonexistentBareRoot));
        Assert.DoesNotContain("applied", writer.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EnglishAndJapaneseDocs_KeepPublicFeedbackSemanticsInParity()
    {
        var repo = RepoVersionPolicySource.RepoRoot();
        var english = File.ReadAllText(Path.Combine(repo, "docs", "en", "12-agent-message-orchestration.md"));
        var japanese = File.ReadAllText(Path.Combine(repo, "docs", "ja", "12-agent-message-orchestration.md"));

        foreach (var document in new[] { english, japanese })
        {
            var normalized = NormalizeWhitespace(document);
            foreach (var marker in new[]
            {
                "G705",
                "intent-cli guide feedback",
                "J-Tech-Japan/intent-system",
                "gh issue create --repo J-Tech-Japan/intent-system",
                "PUBLIC / WORLD-READABLE PERMANENTLY",
                "credentials or tokens",
                "private hostnames",
                "private paths",
                "customer or personal data",
                "internal URLs",
                "Review pasted logs",
                "Exact installed version string",
                "Timestamped observations",
                "Expected versus actual",
                "Reproduction context",
                "verified-versus-assumed",
                "Recommendations only; never required gates",
                "AI seat may draft",
                "design thread or the operator",
                "execution-unit child issue publishing",
                "no API POST",
                "no network connection",
                "no subprocess",
                "telemetry",
            })
            {
                Assert.Contains(marker, normalized, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    private static string NormalizeWhitespace(string value) =>
        string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static CliContext BareContext() => new()
    {
        RepoRoot = Path.Combine(RepoVersionPolicySource.RepoRoot(), ".artifacts", "g705-test-bare-static"),
        Config = DefaultConfig(),
    };

    private static CliConfig DefaultConfig() => new()
    {
        Project = new ProjectConfig
        {
            Domain = "intent-cli",
            ArtifactRoot = ".intent-cli",
            WorktreeRoot = ".intent-cli/worktrees",
        },
    };
}
