using System.Text.Json;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G829: agmsg + herdr is deprecated as a disposition only. The unrecorded
/// default, stored values, set semantics, and exit codes are unchanged; the
/// wording changes and an unrecorded scope is told how to record herdr-only.
/// </summary>
public sealed class G829AgmsgDeprecationTests : IDisposable
{
    private const string Domain = "intent-cli";
    private const string Team = "demo-team";
    private readonly string root = Directory.CreateTempSubdirectory("g829-agmsg-").FullName;
    private readonly CliContext context;

    public G829AgmsgDeprecationTests()
    {
        Directory.CreateDirectory(Path.Combine(root, ".intent-cli"));
        context = new CliContext
        {
            RepoRoot = root,
            Config = new CliConfig
            {
                Project = new ProjectConfig { Domain = Domain, ArtifactRoot = ".intent-cli", WorktreeRoot = ".intent-cli/worktrees" },
            },
        };
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void DefaultAndDescriptions_DeprecateAgmsgWithoutChangingTheDefault()
    {
        Assert.Equal(SessionLayerMode.Agmsg, SessionLayerMode.Default);
        Assert.Equal("agmsg + herdr (deprecated)", SessionLayerMode.Describe(SessionLayerMode.Agmsg));
        Assert.True(SessionLayerMode.IsDeprecated(SessionLayerMode.Agmsg));
        Assert.False(SessionLayerMode.IsDeprecated(SessionLayerMode.HerdrOnly));
        Assert.Contains(SessionLayerMode.AgmsgDeprecationNotice, SessionLayerMode.TransportPreferenceSentence, StringComparison.Ordinal);
        Assert.DoesNotContain("not retired", SessionLayerMode.TransportPreferenceSentence, StringComparison.Ordinal);
    }

    [Fact]
    public void ShowUnrecorded_IsDeprecatedDefault_WithTheExactRecordingCommand()
    {
        var (exitCode, result) = RunShow(Team);

        Assert.Equal(0, exitCode);
        Assert.Equal("agmsg", result.GetProperty("mode").GetString());
        Assert.Equal("default", result.GetProperty("source").GetString());
        Assert.True(result.GetProperty("mode_deprecated").GetBoolean());
        Assert.Equal(SessionLayerMode.AgmsgDeprecationNotice, result.GetProperty("deprecation_notice").GetString());
        Assert.Contains(
            "`intent-cli session-layer set --domain intent-cli --team demo-team --mode herdr-only --write`",
            result.GetProperty("summary").GetString()!,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ShowRecordedAgmsg_IsDeprecatedWithoutTheRecordingHint_AndSetStillWorks()
    {
        var set = RunSet(SessionLayerMode.Agmsg, Team);
        Assert.Equal(0, set.ExitCode);
        Assert.Equal("agmsg", set.Result.GetProperty("mode").GetString());
        Assert.True(set.Result.GetProperty("applied").GetBoolean());

        var (exitCode, result) = RunShow(Team);
        Assert.Equal(0, exitCode);
        Assert.Equal("recorded", result.GetProperty("source").GetString());
        Assert.True(result.GetProperty("mode_deprecated").GetBoolean());
        Assert.DoesNotContain("--mode herdr-only --write", result.GetProperty("summary").GetString()!, StringComparison.Ordinal);
    }

    [Fact]
    public void ShowRecordedHerdrOnly_IsNotDeprecated()
    {
        Assert.Equal(0, RunSet(SessionLayerMode.HerdrOnly, Team).ExitCode);

        var (exitCode, result) = RunShow(Team);
        Assert.Equal(0, exitCode);
        Assert.Equal("herdr-only", result.GetProperty("mode").GetString());
        Assert.False(result.GetProperty("mode_deprecated").GetBoolean());
        // Null notices are omitted, matching the command's WhenWritingNull serializer.
        Assert.True(!result.TryGetProperty("deprecation_notice", out var notice) || notice.ValueKind == JsonValueKind.Null);
        Assert.DoesNotContain("--mode herdr-only --write", result.GetProperty("summary").GetString()!, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderedTransportGuides_NoLongerSayNotRetired()
    {
        var outputs = new[]
        {
            Render(["guide", "model"]),
            Render(["guide", "onboarding"]),
            Render(["guide", "commands", "list"]),
            Render(["session-layer", "show", "--domain", Domain, "--team", Team]),
            Render(["guide", "orchestrator-thread", "--domain", Domain, "--team", Team, "--target-repo", "J-Tech-Japan/intent-system", "--agent", "codex", "--format", "markdown"]),
        };

        foreach (var output in outputs)
        {
            Assert.DoesNotContain("not retired", output, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("non-retired", output, StringComparison.OrdinalIgnoreCase);
        }

        Assert.Contains("--mode herdr-only --write", outputs[4], StringComparison.Ordinal);
    }

    private (int ExitCode, JsonElement Result) RunShow(string team)
    {
        using var writer = new StringWriter();
        var exitCode = SessionLayerCommand.ExecuteShow(context, ["--domain", Domain, "--team", team, "--format", "json"], writer);
        return (exitCode, JsonDocument.Parse(writer.ToString()).RootElement.Clone());
    }

    private (int ExitCode, JsonElement Result) RunSet(string mode, string team)
    {
        using var writer = new StringWriter();
        var exitCode = SessionLayerCommand.ExecuteSet(context, ["--domain", Domain, "--team", team, "--mode", mode, "--write", "--format", "json"], writer);
        return (exitCode, JsonDocument.Parse(writer.ToString()).RootElement.Clone());
    }

    private string Render(string[] args)
    {
        using var writer = new StringWriter();
        var exitCode = CommandRouter.Execute(args, context, writer);
        Assert.True(exitCode == 0, $"`intent-cli {string.Join(' ', args)}` exited {exitCode}: {writer}");
        return writer.ToString();
    }
}
