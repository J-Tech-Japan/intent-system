using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G712 guide-reachability repair: the declared supervision-setup route must
/// execute from a bare metadata-free directory and must remain discoverable.
/// The route renders guidance only; these tests never register or reconcile a
/// real supervisor and retain their repo-local scratch directories.
/// </summary>
public sealed class GuideWorkflowTaskSupervisionSetupG712Tests
{
    private readonly string root = Path.Combine(
        RepoVersionPolicySource.RepoRoot(),
        ".artifacts",
        "g712-guide-repair-bare-" + Guid.NewGuid().ToString("N"));

    public GuideWorkflowTaskSupervisionSetupG712Tests()
    {
        Directory.CreateDirectory(root);
    }

    [Fact]
    public void JsonRoute_RendersTheShippedSessionScopedContract_FromBareDirectory()
    {
        using var writer = new StringWriter();
        var exitCode = GuideWorkflowTaskCommand.Execute(
            CreateContext(),
            ["supervision-setup", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var result = document.RootElement;
        Assert.Equal("supervision-setup", result.GetProperty("task").GetString());
        Assert.Equal("g712-session-scoped-supervision/v1", result.GetProperty("contract_version").GetString());
        Assert.True(result.GetProperty("metadata_free").GetBoolean());
        Assert.True(result.GetProperty("read_only").GetBoolean());

        var commands = result.GetProperty("commands").EnumerateArray().ToArray();
        Assert.Equal(6, commands.Length);
        Assert.Contains(commands, command =>
            command.GetProperty("name").GetString() == "install"
            && command.GetProperty("command").GetString()!.Contains("notify supervise install", StringComparison.Ordinal));
        Assert.Contains(commands, command =>
            command.GetProperty("name").GetString() == "register-current-gui-session"
            && command.GetProperty("command").GetString()!.Contains("launchctl bootstrap gui/$(id -u)", StringComparison.Ordinal));
        var registrationIndex = Array.FindIndex(
            commands,
            command => command.GetProperty("name").GetString() == "register-current-gui-session");
        var verifyIndex = Array.FindIndex(
            commands,
            command => command.GetProperty("name").GetString() == "verify-first-cycle");
        Assert.True(verifyIndex > registrationIndex);
        Assert.Contains(
            "--verify",
            commands[verifyIndex].GetProperty("command").GetString(),
            StringComparison.Ordinal);
        Assert.Contains(commands, command =>
            command.GetProperty("name").GetString() == "reconcile"
            && command.GetProperty("command").GetString()!.Contains("notify supervise reconcile --write", StringComparison.Ordinal));
        Assert.Contains(commands, command =>
            command.GetProperty("name").GetString() == "uninstall"
            && command.GetProperty("command").GetString()!.Contains("notify supervise uninstall --write", StringComparison.Ordinal));
        Assert.Contains(commands, command =>
            command.GetProperty("name").GetString() == "shrink"
            && command.GetProperty("command").GetString()!.Contains("notify supervise shrink --domain", StringComparison.Ordinal));

        Assert.Contains(
            "no managed artifact is emitted to `~/Library/LaunchAgents`",
            result.GetProperty("artifact_location").GetString()!,
            StringComparison.Ordinal);
        Assert.Contains(
            "does not grant workflow or recovery authority",
            result.GetProperty("authority_boundary").GetString()!,
            StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(root, ".intent-cli", "config.toml")));
        Assert.False(Directory.Exists(Path.Combine(root, ".intent-cli")));
    }

    [Fact]
    public void MarkdownRoute_RendersLifecycleCommandsAndNegativeBoundaries_FromBareDirectory()
    {
        using var writer = new StringWriter();
        var exitCode = GuideWorkflowTaskCommand.Execute(
            CreateContext(),
            ["supervision-setup", "--format", "markdown"],
            writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("# intent-cli — supervision setup workflow guide (G712)", output, StringComparison.Ordinal);
        Assert.Contains("metadata-free route", output, StringComparison.Ordinal);
        Assert.Contains("notify supervise install", output, StringComparison.Ordinal);
        Assert.Contains("launchctl bootstrap gui/$(id -u)", output, StringComparison.Ordinal);
        Assert.Contains("verify-first-cycle", output, StringComparison.Ordinal);
        Assert.Contains("--verify", output, StringComparison.Ordinal);
        Assert.Contains("notify supervise reconcile --write --format json", output, StringComparison.Ordinal);
        Assert.Contains("notify supervise uninstall --write --format json", output, StringComparison.Ordinal);
        Assert.Contains("notify supervise shrink --domain <domain> --team <team> --write --format json", output, StringComparison.Ordinal);
        Assert.Contains("must not read .intent-cli/config.toml", output, StringComparison.Ordinal);
        Assert.Contains("must not be emitted under ~/Library/LaunchAgents", output, StringComparison.Ordinal);
        Assert.Contains("never auto-kill or mutate unrelated jobs", output, StringComparison.Ordinal);
        Assert.DoesNotContain("launchctl load ", output, StringComparison.Ordinal);
        Assert.DoesNotContain("launchctl unload ", output, StringComparison.Ordinal);
    }

    [Fact]
    public void MissingTask_IsRejectedButSupportedHelpKeepsTheDeclaredRouteVisible()
    {
        using var writer = new StringWriter();
        var exitCode = GuideWorkflowTaskCommand.Execute(
            CreateContext(),
            ["supervision-setup-missing", "--format", "json"],
            writer);

        Assert.Equal(1, exitCode);
        var output = writer.ToString();
        Assert.Contains("Unknown 'guide workflow task' name 'supervision-setup-missing'", output, StringComparison.Ordinal);
        Assert.Contains("supervision-setup", output, StringComparison.Ordinal);
    }

    [Fact]
    public void CatalogAndTopLevelHelp_AdvertiseTheSameReachableRoute()
    {
        Assert.Contains("supervision-setup", GuideWorkflowTaskCommand.SupportedTasks);

        var catalog = GuideCommandsListCommand.Groups.Single(
            group => string.Equals(group.Name, "guide workflow task supervision-setup", StringComparison.Ordinal));
        Assert.Contains("metadata-free", catalog.Purpose, StringComparison.Ordinal);
        Assert.Contains("reconcile", catalog.Purpose, StringComparison.Ordinal);

        var pointer = GuideHelpCommand.WorkflowGuides.Single(
            item => string.Equals(item.Phase, "supervision-setup", StringComparison.Ordinal));
        Assert.Equal("intent-cli guide workflow task supervision-setup --format json", pointer.Command);
        Assert.Contains("session-scoped", pointer.Purpose, StringComparison.Ordinal);

        var topLevelPointer = CommandRouter.WorkflowGuidePointersHelp.Single(
            line => line.StartsWith("supervision-setup —", StringComparison.Ordinal));
        Assert.Contains("guide workflow task supervision-setup", topLevelPointer, StringComparison.Ordinal);
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
        },
    };
}
