using System.Diagnostics;
using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G822: the team-scoped host-loop identity refusal separates what a caller
/// can supply from what the checkout could not show and from dispatch identity
/// that this build cannot provide. The production-path cases leave
/// <see cref="AutomationHostLoopNextActionCommand.IdentityCaptureFactory"/>
/// unset and run the real capture against a temporary git checkout, because
/// PR #1781 proved its qualified path only through that seam.
/// </summary>
[Collection("WorkerNextActionSharedState")]
public sealed class G822ActionableIdentityRefusalTests : IDisposable
{
    private readonly string checkoutRoot;

    public G822ActionableIdentityRefusalTests()
    {
        ResetSeams();
        checkoutRoot = Path.Combine(Path.GetTempPath(), "g822-checkout-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(checkoutRoot);
    }

    public void Dispose()
    {
        ResetSeams();
        try
        {
            Directory.Delete(checkoutRoot, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    [Fact]
    public void ProductionCapture_TeamAllSupplied_ReportsOnlyDispatchIdentitySourceUnavailable()
    {
        const string origin = "https://github.com/J-Tech-Japan/intent-system.git";
        InitCheckout(origin, "g822-branch");
        var lister = new CountingLister();
        AutomationHostLoopNextActionCommand.CandidateListerFactory = () => lister;
        Assert.Null(AutomationHostLoopNextActionCommand.IdentityCaptureFactory);

        var root = RunJson(TeamAllSupplied());

        Assert.Equal(AutomationHostLoopNextActionCommand.ClassificationIdentityUnresolved,
            root.GetProperty("classification").GetString());
        Assert.False(root.GetProperty("mutation_allowed").GetBoolean());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("candidate_execution_unit").ValueKind);
        Assert.Equal(
            [AutomationHostLoopNextActionCommand.ReasonDispatchIdentitySourceUnavailable],
            Strings(root, "identity_unresolved_reasons"));
        Assert.Empty(Strings(root, "missing_caller_arguments"));
        Assert.Empty(Strings(root, "unobservable_source_facts"));

        // The real capture observed the checkout; only dispatch identity is absent.
        Assert.Equal(origin, root.GetProperty("captured_origin").GetString());
        Assert.Equal("g822-branch", root.GetProperty("captured_ref").GetString());
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("captured_head").GetString()));
        Assert.Equal(JsonValueKind.Null, root.GetProperty("dispatch_generation").ValueKind);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("dispatch_digest").ValueKind);

        var evidence = Evidence(root);
        Assert.Contains("no CLI argument or checkout change can supply it", evidence, StringComparison.Ordinal);
        Assert.Contains("#1774", evidence, StringComparison.Ordinal);
        Assert.DoesNotContain("missing_fields", evidence, StringComparison.Ordinal);
        Assert.DoesNotContain(AutomationHostLoopNextActionCommand.ReasonCallerContextMissing, evidence, StringComparison.Ordinal);
        Assert.Equal(0, lister.TotalCalls);
    }

    [Fact]
    public void ProductionCapture_TaskOmitted_DiffersOnlyByCallerContext()
    {
        InitCheckout("https://github.com/J-Tech-Japan/intent-system.git", "g822-branch");
        var lister = new CountingLister();
        AutomationHostLoopNextActionCommand.CandidateListerFactory = () => lister;

        var withTask = RunJson(TeamAllSupplied());
        var withoutTask = RunJson(TeamAllSupplied().Where((_, i) => i is not (6 or 7)).ToArray());

        Assert.Equal(
            [AutomationHostLoopNextActionCommand.ReasonDispatchIdentitySourceUnavailable],
            Strings(withTask, "identity_unresolved_reasons"));
        Assert.Empty(Strings(withTask, "missing_caller_arguments"));

        Assert.Equal(
            [
                AutomationHostLoopNextActionCommand.ReasonCallerContextMissing,
                AutomationHostLoopNextActionCommand.ReasonDispatchIdentitySourceUnavailable,
            ],
            Strings(withoutTask, "identity_unresolved_reasons"));
        Assert.Equal(["--task-id"], Strings(withoutTask, "missing_caller_arguments"));
        Assert.Empty(Strings(withoutTask, "unobservable_source_facts"));
        var evidence = Evidence(withoutTask);
        Assert.Contains("task_id not supplied; pass --task-id", evidence, StringComparison.Ordinal);

        Assert.Equal(AutomationHostLoopNextActionCommand.ClassificationIdentityUnresolved,
            withoutTask.GetProperty("classification").GetString());
        Assert.False(withoutTask.GetProperty("mutation_allowed").GetBoolean());
        Assert.Equal(0, lister.TotalCalls);
    }

    [Fact]
    public void UnobservableOrigin_IsReportedAsSourceFact_NotCallerContext()
    {
        var lister = new CountingLister();
        AutomationHostLoopNextActionCommand.CandidateListerFactory = () => lister;
        AutomationHostLoopNextActionCommand.IdentityCaptureFactory = _ => new HostLoopIdentityCapture
        {
            Cwd = "/child",
            Origin = null,
            Ref = "g822-branch",
            Head = "head-1",
            DispatchGeneration = "generation-1",
            DispatchDigest = "digest-1",
        };

        var root = RunJson(TeamAllSupplied());

        Assert.Equal(
            [AutomationHostLoopNextActionCommand.ReasonSourceFactUnobservable],
            Strings(root, "identity_unresolved_reasons"));
        Assert.Equal(["captured_origin"], Strings(root, "unobservable_source_facts"));
        Assert.Empty(Strings(root, "missing_caller_arguments"));
        Assert.Contains("run from a git checkout that has an origin remote", Evidence(root), StringComparison.Ordinal);
        Assert.Equal(0, lister.TotalCalls);
    }

    [Fact]
    public void QualifiedSeamPath_CarriesNoUnresolvedReasons()
    {
        var lister = new CountingLister();
        AutomationHostLoopNextActionCommand.CandidateListerFactory = () => lister;
        AutomationHostLoopNextActionCommand.IdentityCaptureFactory = _ => new HostLoopIdentityCapture
        {
            Cwd = "/child",
            Origin = "https://github.com/J-Tech-Japan/intent-system.git",
            Ref = "g822-branch",
            Head = "head-1",
            DispatchGeneration = "generation-1",
            DispatchDigest = "digest-1",
        };
        AutomationHostLoopNextActionCommand.NextSliceDryRunProbeFactory = _ => new FixedNextSliceProbe();
        AutomationHostLoopNextActionCommand.PublishRecoveryProbeFactory = _ => new FixedRecoveryProbe();
        AutomationHostLoopNextActionCommand.CloseoutDriftCheckProbeFactory = _ => new FixedDriftProbe();

        var root = RunJson(TeamAllSupplied());

        Assert.Equal("qualified", root.GetProperty("identity_qualification").GetString());
        Assert.Equal(
            "J-Tech-Japan/intent-system/intent-cli/intent-cli-dev/G822-task/G822-NONCE/generation-1/digest-1",
            root.GetProperty("completion_identity").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("identity_unresolved_reasons").ValueKind);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("missing_caller_arguments").ValueKind);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("unobservable_source_facts").ValueKind);
    }

    [Fact]
    public void EveryAcceptedFlag_IsAdvertisedInErrorMessageAndUsage()
    {
        string[] expected =
        [
            "--repo", "--domain", "--team", "--task-id", "--result-nonce", "--routing-root",
            "--timeout-seconds", "--stale-cli", "--sync-classification", "--safe-stash-required",
            "--publish-recovery-repairs", "--publish-lifecycle-drift", "--next-slice-issue-cut-ready",
            "--publish-next-execution-unit", "--hard-clarification-open", "--approved-pr-merge-state",
            "--approved-pr-metadata-blocked", "--prepared-packet-commit-ready",
            "--prepared-packet-execution-unit", "--format",
        ];
        Assert.Equal(expected.Order(StringComparer.Ordinal), AutomationHostLoopNextActionCommand.AcceptedFlags.Order(StringComparer.Ordinal));

        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["--repo"] = "J-Tech-Japan/intent-system",
            ["--domain"] = "intent-cli",
            ["--team"] = "intent-cli-dev",
            ["--task-id"] = "task",
            ["--result-nonce"] = "nonce",
            ["--routing-root"] = "/host",
            ["--timeout-seconds"] = "5",
            ["--sync-classification"] = "clean",
            ["--publish-recovery-repairs"] = "0",
            ["--publish-lifecycle-drift"] = "0",
            ["--publish-next-execution-unit"] = "G822",
            ["--approved-pr-merge-state"] = "CLEAN",
            ["--prepared-packet-execution-unit"] = "G822",
            ["--format"] = "json",
        };

        const string sentinel = "--g822-not-a-flag";
        var usage = CommandRouter.AutomationCommandHelp.Single(line =>
            line.StartsWith("automation host-loop-next-action ", StringComparison.Ordinal));

        foreach (var flag in AutomationHostLoopNextActionCommand.AcceptedFlags)
        {
            var args = new List<string> { "--repo", "J-Tech-Japan/intent-system", flag };
            if (values.TryGetValue(flag, out var value) && flag != "--repo")
            {
                args.Add(value);
            }
            else if (flag == "--repo")
            {
                args.Add("J-Tech-Japan/intent-system");
            }
            args.Add(sentinel);

            using var writer = new StringWriter();
            var exit = AutomationHostLoopNextActionCommand.Execute(CreateContext(checkoutRoot), args.ToArray(), writer);
            var output = writer.ToString();

            // The parser consumed the flag and stopped at the sentinel, so the
            // error names the sentinel rather than the flag under test.
            Assert.Equal(1, exit);
            Assert.Contains($"Unknown argument '{sentinel}'.", output, StringComparison.Ordinal);
            Assert.DoesNotContain($"Unknown argument '{flag}'", output, StringComparison.Ordinal);

            var supported = output[(output.IndexOf("Supported:", StringComparison.Ordinal) + "Supported:".Length)..];
            Assert.Contains(" " + flag, " " + supported.Replace(".", " "), StringComparison.Ordinal);
            Assert.Contains(flag, usage, StringComparison.Ordinal);
        }
    }

    private static string[] TeamAllSupplied() =>
    [
        "--repo", "J-Tech-Japan/intent-system",
        "--domain", "intent-cli",
        "--team", "intent-cli-dev",
        "--task-id", "G822-task",
        "--result-nonce", "G822-NONCE",
        "--routing-root", "/registered-host",
        "--timeout-seconds", "10",
        "--sync-classification", "clean",
        "--format", "json",
    ];

    private JsonElement RunJson(string[] args)
    {
        using var writer = new StringWriter();
        Assert.Equal(0, AutomationHostLoopNextActionCommand.Execute(CreateContext(checkoutRoot), args, writer));
        using var json = JsonDocument.Parse(writer.ToString());
        return json.RootElement.Clone();
    }

    private static string[] Strings(JsonElement root, string property) =>
        root.GetProperty(property).EnumerateArray().Select(item => item.GetString()!).ToArray();

    private static string Evidence(JsonElement root) =>
        string.Join(" ", root.GetProperty("evidence").EnumerateArray().Select(item => item.GetString()));

    private void InitCheckout(string origin, string branch)
    {
        RunGit(checkoutRoot, "init", "-q", "-b", branch);
        File.WriteAllText(Path.Combine(checkoutRoot, "README.md"), "g822\n");
        RunGit(checkoutRoot, "add", "README.md");
        RunGit(checkoutRoot, "-c", "user.email=g822@example.invalid", "-c", "user.name=g822", "commit", "-q", "-m", "init");
        RunGit(checkoutRoot, "remote", "add", "origin", origin);
    }

    private static CliContext CreateContext(string repoRoot) => new()
    {
        RepoRoot = repoRoot,
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

    private static void ResetSeams()
    {
        AutomationHostLoopNextActionCommand.CandidateListerFactory = null;
        AutomationHostLoopNextActionCommand.IdentityCaptureFactory = null;
        AutomationHostLoopNextActionCommand.NextSliceDryRunProbeFactory = null;
        AutomationHostLoopNextActionCommand.PublishRecoveryProbeFactory = null;
        AutomationHostLoopNextActionCommand.HostSyncPreflightProbeFactory = null;
        AutomationHostLoopNextActionCommand.CloseoutDriftCheckProbeFactory = null;
        AutomationHostLoopNextActionCommand.HostBindingDomainResolverDelegate = null;
    }

    private static void RunGit(string workingDirectory, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = workingDirectory,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo);
        Assert.NotNull(process);
        process!.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, $"git {string.Join(' ', arguments)} failed: {error}");
    }

    private sealed class CountingLister : IGitHubAutomationCandidateLister
    {
        public int TotalCalls { get; private set; }

        public IReadOnlyList<GitHubAutomationPrCandidate> ListPullRequests(
            string repo,
            IReadOnlyCollection<string> requiredLabels)
        {
            TotalCalls++;
            return Array.Empty<GitHubAutomationPrCandidate>();
        }

        public IReadOnlyList<GitHubAutomationIssueCandidate> ListIssues(
            string repo,
            IReadOnlyCollection<string> requiredLabels)
        {
            TotalCalls++;
            return Array.Empty<GitHubAutomationIssueCandidate>();
        }
    }

    private sealed class FixedNextSliceProbe : INextSliceDryRunProbe
    {
        public NextSliceProbeResult Probe(string repo, string domain) => new() { RecommendedOutcome = "no-action" };
    }

    private sealed class FixedRecoveryProbe : IPublishRecoveryProbe
    {
        public PublishRecoveryProbeResult Probe(string repo) => new() { SafeRepairCount = 0, UnsafeStopCount = 0 };
    }

    private sealed class FixedDriftProbe : ICloseoutDriftCheckProbe
    {
        public CloseoutDriftCheckProbeResult Probe(string repo) => new() { SafeRepairCount = 0, UnsafeStopCount = 0 };
    }
}
