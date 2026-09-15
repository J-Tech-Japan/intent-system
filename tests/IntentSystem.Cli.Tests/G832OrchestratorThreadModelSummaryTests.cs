using System.Text.Json;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G832: the orchestrator-thread model summary names the canonical roles with
/// their legacy aliases and the optional Steward five-thread shape in both
/// session-layer modes, and the agmsg mode says the transport is deprecated.
/// </summary>
public sealed class G832OrchestratorThreadModelSummaryTests : IDisposable
{
    private readonly string root = Directory.CreateTempSubdirectory("g832-orch-").FullName;
    private readonly CliContext context;

    public G832OrchestratorThreadModelSummaryTests()
    {
        Directory.CreateDirectory(Path.Combine(root, ".intent-cli"));
        context = new CliContext
        {
            RepoRoot = root,
            Config = new CliConfig { Project = new ProjectConfig { Domain = "intent-cli", ArtifactRoot = ".intent-cli", WorktreeRoot = ".intent-cli/worktrees" } },
        };
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("herdr-only")]
    public void Summary_NamesCanonicalRolesAliasesAndOptionalSteward_InBothModes(string? mode)
    {
        if (mode is not null)
        {
            using var set = new StringWriter();
            Assert.Equal(0, SessionLayerCommand.ExecuteSet(context, ["--domain", "intent-cli", "--team", "t1", "--mode", mode, "--write", "--format", "json"], set));
        }

        var summary = Render("json", "t1").GetProperty("summary").GetString()!;
        Assert.Contains("PRIMARY four-thread orchestrator model", summary, StringComparison.Ordinal);
        Assert.Contains("ADR-012 / spec-26", summary, StringComparison.Ordinal);
        Assert.Contains(GuideOrchestratorThreadCommand.ModelRolesPhrase, summary, StringComparison.Ordinal);
        Assert.Contains(GuideOrchestratorThreadCommand.StewardShapeSentence, summary, StringComparison.Ordinal);
        Assert.DoesNotContain("design / orchestrator / implementation / review", summary, StringComparison.Ordinal);

        if (mode is null)
        {
            Assert.Contains(GuideOrchestratorThreadCommand.AgmsgModeDeprecationSentence, summary, StringComparison.Ordinal);
        }
        else
        {
            Assert.DoesNotContain(GuideOrchestratorThreadCommand.AgmsgModeDeprecationSentence, summary, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void RolePhrase_MatchesTheRoleNormalizer()
    {
        foreach (var (alias, canonical) in LogicalRoleNormalizer.Aliases)
        {
            Assert.Contains($"{canonical} ({alias})", GuideOrchestratorThreadCommand.ModelRolesPhrase, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Help_NamesCanonicalRolesAndSteward_WithoutAgmsgSignalLayerWording()
    {
        using var writer = new StringWriter();
        Assert.Equal(0, GuideOrchestratorThreadCommand.Execute(context, ["--help"], writer));
        var help = writer.ToString();
        Assert.Contains("architect/orchestrator/builder/reviewer", help, StringComparison.Ordinal);
        Assert.Contains("design/orchestration/", help, StringComparison.Ordinal);
        Assert.Contains("implementation/review are accepted", help, StringComparison.Ordinal);
        Assert.DoesNotContain("session-layer set --mode herdr-only --write", GuideOrchestratorThreadCommand.AgmsgModeDeprecationSentence, StringComparison.Ordinal);
        Assert.Contains("optional Steward relay seat", help, StringComparison.Ordinal);
        Assert.DoesNotContain("agmsg is a signal layer only", help, StringComparison.Ordinal);
    }

    private JsonElement Render(string format, string team)
    {
        using var writer = new StringWriter();
        var exit = CommandRouter.Execute(["guide", "orchestrator-thread", "--domain", "intent-cli", "--team", team, "--target-repo", "J-Tech-Japan/intent-system", "--agent", "codex", "--format", format], context, writer);
        Assert.True(exit == 0, writer.ToString());
        return JsonDocument.Parse(writer.ToString()).RootElement.Clone();
    }
}
