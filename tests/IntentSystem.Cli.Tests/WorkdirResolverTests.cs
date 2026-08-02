using System.Collections;
using System.Reflection;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

[Collection("WorkerNextActionSharedState")]
public sealed class WorkdirResolverTests
{
    [Fact]
    public void Resolve_RelativePathUsesRepoRoot_AndRootedPathIsUnchanged()
    {
        using var workspace = new WorkdirResolutionWorkspace();
        var context = CreateContext(workspace.RepoRoot);
        var rootedWithTraversal = Path.Combine(workspace.Root, "absolute", "..", "target");

        Assert.Equal(
            Path.GetFullPath(Path.Combine(workspace.RepoRoot, "relative", "target")),
            WorkdirResolver.Resolve(context, Path.Combine("relative", "target")));
        Assert.Equal(rootedWithTraversal, WorkdirResolver.Resolve(context, rootedWithTraversal));
        Assert.Equal(workspace.RepoRoot, WorkdirResolver.Resolve(context, null));
        Assert.Equal(workspace.RepoRoot, WorkdirResolver.Resolve(context, "   "));
    }

    [Fact]
    public void RegisteredAutomationWorkdirCommands_ResolveTheSameRelativePathFromNonRootCwd()
    {
        using var workspace = new WorkdirResolutionWorkspace();
        var context = CreateContext(workspace.RepoRoot);
        var relativeWorkdir = "missing-relative-workdir";
        var expectedWorkdir = Path.GetFullPath(Path.Combine(workspace.RepoRoot, relativeWorkdir));
        var cwdRelativeWorkdir = Path.GetFullPath(Path.Combine(workspace.CallerRoot, relativeWorkdir));
        var commands = DiscoverAutomationCommandsAcceptingWorkdir(context);
        var originalDirectory = Directory.GetCurrentDirectory();

        Assert.Equal(10, commands.Count);

        try
        {
            Directory.SetCurrentDirectory(workspace.CallerRoot);

            foreach (var command in commands)
            {
                using var writer = new StringWriter();
                var exitCode = CommandRouter.Execute(
                    ["automation", command, .. BuildArguments(command, relativeWorkdir)],
                    context,
                    writer);

                Assert.Equal(1, exitCode);
                Assert.Contains(expectedWorkdir, writer.ToString(), StringComparison.Ordinal);
                Assert.DoesNotContain(cwdRelativeWorkdir, writer.ToString(), StringComparison.Ordinal);
            }
        }
        finally
        {
            Directory.SetCurrentDirectory(originalDirectory);
        }
    }

    [Fact]
    public void RegisteredAutomationWorkdirCommands_HaveNoPrivateResolverCopies()
    {
        using var workspace = new WorkdirResolutionWorkspace();
        var context = CreateContext(workspace.RepoRoot);
        var commands = DiscoverAutomationCommandsAcceptingWorkdir(context);
        var handlers = GetAutomationHandlers();

        Assert.Equal(10, commands.Count);

        foreach (var command in commands)
        {
            var commandType = ((Delegate)handlers[command]!).Method.DeclaringType;
            Assert.NotNull(commandType);
            Assert.Null(commandType!.GetMethod(
                "ResolveWorkdir",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly));
        }
    }

    private static IReadOnlyList<string> DiscoverAutomationCommandsAcceptingWorkdir(CliContext context)
    {
        var commands = new List<string>();

        foreach (var key in GetAutomationHandlers().Keys)
        {
            var command = (string)key!;
            using var writer = new StringWriter();

            CommandRouter.Execute(["automation", command, "--discover-options"], context, writer);
            if (writer.ToString().Contains("--workdir", StringComparison.Ordinal))
            {
                commands.Add(command);
            }
        }

        return commands.OrderBy(command => command, StringComparer.Ordinal).ToArray();
    }

    private static IDictionary GetAutomationHandlers()
    {
        var commandsField = typeof(CommandRouter).GetField(
            "ImplementedCommands",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(commandsField);

        var groups = commandsField!.GetValue(null) as IDictionary;
        Assert.NotNull(groups);

        var automation = groups!["automation"] as IDictionary;
        Assert.NotNull(automation);
        return automation!;
    }

    private static string[] BuildArguments(string command, string relativeWorkdir) => command switch
    {
        "check" => ["--workdir", relativeWorkdir, "--format", "json"],
        "clarification-stop" =>
        [
            "--kind", "issue-to-pr",
            "--issue", "1",
            "--reason", "reason",
            "--recommended-owner-action", "action",
            "--workdir", relativeWorkdir,
            "--format", "json"
        ],
        "complete" =>
        [
            "--kind", "issue-to-pr",
            "--issue", "1",
            "--outcome", "failed",
            "--workdir", relativeWorkdir,
            "--format", "json"
        ],
        "host-review-diagnostics" => ["--workdir", relativeWorkdir, "--format", "json"],
        "host-review-preflight" => ["--workdir", relativeWorkdir, "--format", "json"],
        "issue-publish" => ["--issue", "1", "--workdir", relativeWorkdir, "--format", "json"],
        "issue-release" => ["--issue", "1", "--workdir", relativeWorkdir, "--format", "json"],
        "pr-transition" =>
        [
            "--pr", "1",
            "--transition", "review-start",
            "--workdir", relativeWorkdir,
            "--format", "json"
        ],
        "reconcile" => ["--workdir", relativeWorkdir, "--format", "json"],
        "state-doctor" => ["--workdir", relativeWorkdir, "--format", "json"],
        _ => throw new InvalidOperationException(
            $"Registered automation command '{command}' accepts --workdir but has no drive arguments.")
    };

    private static CliContext CreateContext(string repoRoot) => new()
    {
        RepoRoot = repoRoot,
        Config = new CliConfig
        {
            Project = new ProjectConfig
            {
                Domain = "intent-cli",
                ArtifactRoot = ".intent-cli"
            }
        }
    };

    private sealed class WorkdirResolutionWorkspace : IDisposable
    {
        public WorkdirResolutionWorkspace()
        {
            Root = Path.Combine(Path.GetTempPath(), $"intent-cli-workdir-tests-{Guid.NewGuid():N}");
            RepoRoot = Path.Combine(Root, "repo-root");
            CallerRoot = Path.Combine(Root, "caller-root");
            Directory.CreateDirectory(RepoRoot);
            Directory.CreateDirectory(CallerRoot);
        }

        public string Root { get; }

        public string RepoRoot { get; }

        public string CallerRoot { get; }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }
}
