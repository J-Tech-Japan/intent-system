using System.Text.Json;
using System.Xml.Linq;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

[Collection("WorkerNextActionSharedState")]
public sealed class NotifySuperviseInstallG786Tests : IDisposable
{
    private const string Domain = "intent-cli";
    private const string Team = "intent-cli-dev";
    private const string Label = "intent-cli.supervise.intent-cli.intent-cli-dev";
    private readonly string root = Path.Combine(
        RepoVersionPolicySource.RepoRoot(),
        ".artifacts",
        "g786-install-" + Guid.NewGuid().ToString("N"));
    private readonly string home;

    public NotifySuperviseInstallG786Tests()
    {
        Directory.CreateDirectory(root);
        home = Path.Combine(root, "home");
        NotifyCommand.ProcessRunnerFactory = () => new FixtureRunner();
        NotifySuperviseInstallCommand.FirstCycleProbeFactory = _ => new NotifySuperviseFirstCycleResult
        {
            Verified = true,
            Status = "first-cycle-verified",
            CycleId = "g786-install-first-cycle",
            Writer = new NotifySupervisionWriterIdentity
            {
                Pid = 7860,
                ProcessStartTime = new DateTimeOffset(2026, 9, 2, 12, 0, 0, TimeSpan.Zero),
                Host = "g786-fixture",
            },
            ObservedAt = new DateTimeOffset(2026, 9, 2, 12, 0, 1, TimeSpan.Zero),
        };
        NotifySuperviseReconcileCommand.MacOsDetector = () => true;
        NotifySuperviseArtifactInventory.UserProfileDirectoryFactory = () => home;
    }

    public void Dispose()
    {
        NotifyCommand.ProcessRunnerFactory = null;
        NotifySuperviseInstallCommand.FirstCycleProbeFactory = null;
        NotifySuperviseReconcileCommand.MacOsDetector = OperatingSystem.IsMacOS;
        NotifySuperviseArtifactInventory.UserProfileDirectoryFactory =
            () => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    }

    [Fact]
    public void Install_EmbedsValidatedPolicyArgumentsAndLaunchedArtifactRecordsThem()
    {
        var shellPolicyJson = JsonSerializer.Serialize(ProjectTestPolicy());
        using var installWriter = new StringWriter();
        var installExitCode = NotifyCommand.ExecuteSupervise(
            CreateContext(),
            InstallArguments("--write")
                .Concat(
                [
                    "--pre-approve", "codex:github-comment-post",
                    "--pre-escalate", "codex:launch-hook-trust",
                    "--shell-policy", shellPolicyJson,
                    "--format", "json",
                ])
                .ToArray(),
            installWriter);

        Assert.Equal(0, installExitCode);
        using var install = JsonDocument.Parse(installWriter.ToString());
        var artifactPath = install.RootElement.GetProperty("artifact_path").GetString()!;
        var programArguments = XDocument.Parse(File.ReadAllText(artifactPath))
            .Descendants("array")
            .Single()
            .Elements("string")
            .Select(element => element.Value)
            .ToArray();

        AssertContainsSequence(programArguments, "--pre-approve", "codex:github-comment-post");
        AssertContainsSequence(programArguments, "--pre-escalate", "codex:launch-hook-trust");
        AssertContainsSequence(programArguments, "--shell-policy", shellPolicyJson);

        var artifactSuperviseArguments = programArguments
            .SkipWhile(argument => !string.Equals(argument, "notify", StringComparison.Ordinal))
            .Skip(2)
            .Append("--once")
            .ToArray();
        using var supervisorWriter = new StringWriter();
        Assert.Equal(
            0,
            NotifyCommand.ExecuteSupervise(CreateContext(), artifactSuperviseArguments, supervisorWriter));

        var recorded = NotifyPreApprovalPolicyStore.Read(
            CreateContext().ResolveSupervisionArtifactRootPath(),
            Domain,
            Team);
        Assert.True(recorded.Resolved, recorded.Error);
        Assert.NotNull(recorded.Policy);
        Assert.Contains(recorded.Policy!.Accept, rule => rule.ToString() == "codex:github-comment-post");
        Assert.Contains(recorded.Policy.Escalate, rule => rule.ToString() == "codex:launch-hook-trust");
        Assert.Contains(recorded.Policy.ScopedPolicies, policy => policy.PolicyId == "g786-project-test");

        using var reconcileWriter = new StringWriter();
        Assert.Equal(
            0,
            NotifyCommand.ExecuteSupervise(
                CreateContext(),
                ["reconcile", "--domain", Domain, "--team", Team, "--platform", "macos", "--dry-run", "--format", "json"],
                reconcileWriter));
        using var reconcile = JsonDocument.Parse(reconcileWriter.ToString());
        Assert.Contains(
            reconcile.RootElement.GetProperty("artifacts_before").EnumerateArray().Select(value => value.GetString()),
            value => string.Equals(value, artifactPath, StringComparison.Ordinal));
    }

    [Fact]
    public void InstallWithoutPolicies_PreservesThePolicyFreeInvocation()
    {
        using var writer = new StringWriter();
        Assert.Equal(
            0,
            NotifyCommand.ExecuteSupervise(
                CreateContext(),
                InstallArguments("--dry-run").Concat(["--format", "json"]).ToArray(),
                writer));

        using var output = JsonDocument.Parse(writer.ToString());
        var invocation = output.RootElement.GetProperty("supervise_invocation").GetString()!;
        Assert.DoesNotContain("--pre-approve", invocation, StringComparison.Ordinal);
        Assert.DoesNotContain("--pre-escalate", invocation, StringComparison.Ordinal);
        Assert.DoesNotContain("--shell-policy", invocation, StringComparison.Ordinal);
    }

    [Fact]
    public void Install_UsesTheSameScopedPolicyValidationAsSupervise()
    {
        using var writer = new StringWriter();
        var exitCode = NotifyCommand.ExecuteSupervise(
            CreateContext(),
            InstallArguments("--dry-run")
                .Concat(["--pre-approve", "codex:shell-command", "--format", "json"])
                .ToArray(),
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("scoped shell policy", writer.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DocumentationStatesTheCommandOnlyRecognizerAndInstallPolicyPassThrough()
    {
        foreach (var language in new[] { "en", "ja" })
        {
            var documentation = File.ReadAllText(Path.Combine(
                RepoVersionPolicySource.RepoRoot(),
                "docs",
                language,
                "12-agent-message-orchestration.md"));
            Assert.Contains("--pre-approve <agent-kind>:<prompt-class>", documentation, StringComparison.Ordinal);
            Assert.Contains("--pre-escalate <agent-kind>:<prompt-class>", documentation, StringComparison.Ordinal);
            Assert.Contains("--shell-policy <json>", documentation, StringComparison.Ordinal);
            Assert.Contains("first choice", documentation, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("chrome", documentation, StringComparison.OrdinalIgnoreCase);
        }
    }

    private IEnumerable<string> InstallArguments(string mode) =>
    [
        "install", "--domain", Domain, "--team", Team,
        "--repo", "J-Tech-Japan/intent-system", "--owner-role", "orchestration",
        "--bound", "900", "--interval", "300", "--platform", "macos",
        "--routing-root", root, mode,
    ];

    private CliContext CreateContext() => new()
    {
        RepoRoot = root,
        Config = new CliConfig
        {
            Project = new ProjectConfig
            {
                Domain = Domain,
                ArtifactRoot = ".intent-cli",
                WorktreeRoot = ".intent-cli/worktrees",
            },
            Supervision = new SupervisionConfig { ArtifactRoot = ".intent-cli/supervision" },
        },
    };

    private static NotifyScopedPromptPolicy ProjectTestPolicy() => new()
    {
        PolicyId = "g786-project-test",
        AgentKind = "codex",
        PromptClass = "shell-command",
        Scope = "project-test",
        Decision = "accept",
        Category = "test-execution",
        ArgvTokenPrefix = ["dotnet", "test"],
        Cwd = "/repo",
        Root = "/repo",
        PathConstraints = ["/repo/tests"],
        EffectTags = ["executes-tests"],
    };

    private static void AssertContainsSequence(IReadOnlyList<string> values, params string[] expected)
    {
        for (var index = 0; index <= values.Count - expected.Length; index++)
        {
            if (values.Skip(index).Take(expected.Length).SequenceEqual(expected, StringComparer.Ordinal))
            {
                return;
            }
        }

        Assert.Fail("Expected ProgramArguments sequence was not emitted: " + string.Join(" ", expected));
    }

    private sealed class FixtureRunner : INotifyProcessRunner
    {
        public NotifyProcessResult Run(string fileName, IReadOnlyList<string> arguments) =>
            string.Equals(fileName, "id", StringComparison.Ordinal)
                && arguments.SequenceEqual(["-u"])
                ? new NotifyProcessResult(0, "501\n", string.Empty)
                : new NotifyProcessResult(0, "{\"result\":{\"agents\":[]}}", string.Empty);
    }
}
