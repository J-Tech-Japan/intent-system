using System.Diagnostics;
using System.Text;
using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;
using IntentSystem.Supervisor.Models;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G846 AC9: exercise the surrounding command routes against a large prepared
/// packet and compare their complete in-process output with merge-base goldens.
/// Every external boundary is either injected or a per-test fake executable.
/// </summary>
[Collection("WorkerNextActionSharedState")]
public sealed class G846SurroundingSurfaceHarnessTests
{
    private const string Domain = "intent-cli";
    private const string Repo = "J-Tech-Japan/intent-system";
    private const string Unit = "G846";
    private const string Team = "intent-cli-dev";
    private const string CaptureDirectoryVariable = "G846_AC9_CAPTURE_DIR";
    private const string RootToken = "{{G846_ROOT}}";

    [Fact]
    public void SurroundingCommandsMatchMergeBaseGoldens_AndOnlyInjectedBoundariesRun()
    {
        using var workspace = new Ac9Workspace();
        using var fakeGh = new FakeGhScope(workspace.RootPath);
        var seams = new SeamRecorder();

        workspace.WritePacket(70_000);
        InstallSeams(workspace, seams, fakeGh);
        try
        {
            AssertGolden(
                "next-slice-70000.json",
                Run(workspace, [
                    "intent", "next-slice", "--dry-run",
                    "--domain", Domain,
                    "--target-repo", Repo,
                    "--team", Team,
                    "--format", "json"
                ]),
                workspace.RootPath);

            var durableStateOutput = Run(workspace, [
                "automation", "durable-state-preflight",
                "--domain", Domain,
                "--target-repo", Repo,
                "--format", "json"
            ]);
            AssertGolden("durable-state-preflight-70000.json", durableStateOutput, workspace.RootPath);
            Assert.True(seams.ProbeFactoryCalls > 0, "ProbeFactory was not reached by durable-state-preflight.");

            var hostReviewOutput = Run(workspace, [
                "automation", "host-review-diagnostics",
                "--repo", Repo,
                "--domain", Domain,
                "--format", "json"
            ]);
            AssertGolden("host-review-diagnostics-70000.json", hostReviewOutput, workspace.RootPath);
            Assert.True(
                seams.CandidateListerFactoryCalls > 0,
                "CandidateListerFactory was not reached by host-review-diagnostics.");
            Assert.True(
                seams.CandidateListerMethodCalls > 0,
                "CandidateLister adapter seam was not reached by host-review-diagnostics.");
            Assert.True(
                seams.NextSliceProbeFactoryCalls > 0,
                "NextSliceDryRunProbeFactory was not reached by host-review-diagnostics.");
            Assert.True(
                seams.PublishRecoveryProbeFactoryCalls > 0,
                "PublishRecoveryProbeFactory was not reached by host-review-diagnostics.");

            workspace.WritePacket(50_000);
            var queueSeed50KOutput = Run(workspace, [
                "automation", "queue-seed-from-packet",
                "--execution-unit", Unit,
                "--team", Team,
                "--domain", Domain,
                "--target-repo", Repo,
                "--format", "json"
            ]);
            AssertQueueSeedReachesAnalyzer(queueSeed50KOutput);
            AssertGolden("queue-seed-from-packet-50000.json", queueSeed50KOutput, workspace.RootPath);

            workspace.WritePacket(70_000);
            var queueSeed70KOutput = Run(workspace, [
                "automation", "queue-seed-from-packet",
                "--execution-unit", Unit,
                "--team", Team,
                "--domain", Domain,
                "--target-repo", Repo,
                "--format", "json"
            ]);
            AssertQueueSeedReachesAnalyzer(queueSeed70KOutput);
            AssertGolden("queue-seed-from-packet-70000.json", queueSeed70KOutput, workspace.RootPath);

            Assert.True(seams.InstalledSurfaceProbeCalls > 0, "installed CLI surface probe was not reached.");
            Assert.Equal(0, seams.ProviderLauncherCalls);
            Assert.True(fakeGh.HasInvocations, "the per-test fake gh was not invoked");
        }
        finally
        {
            ResetSeams();
        }
    }

    private static void InstallSeams(Ac9Workspace workspace, SeamRecorder seams, FakeGhScope fakeGh)
    {
        AutomationDurableStatePreflightCommand.ProbeFactory = _ =>
        {
            seams.ProbeFactoryCalls++;
            return new DurableStatePreflightProbe
            {
                DirtyPaths = Array.Empty<DurableStateDirtyPath>()
            };
        };

        var installedCli = workspace.WriteInstalledCliPlaceholder();
        AutomationInstalledCliSurfaceProbe.ExplicitInstalledCliPathReader = () => null;
        AutomationInstalledCliSurfaceProbe.PathResolver = _ => installedCli;
        AutomationInstalledCliSurfaceProbe.ProbeRunner = (_, _) =>
        {
            seams.InstalledSurfaceProbeCalls++;
            return new InstalledCliProbeResult(
                0,
                "automation pr-transition review-start request-update approved",
                string.Empty);
        };

        AutomationHostReviewDiagnosticsCommand.CandidateListerFactory = () =>
        {
            seams.CandidateListerFactoryCalls++;
            return new Ac9CandidateLister(seams, fakeGh);
        };
        AutomationHostReviewDiagnosticsCommand.NextSliceDryRunProbeFactory = _ =>
        {
            seams.NextSliceProbeFactoryCalls++;
            return new Ac9NextSliceProbe();
        };
        AutomationHostReviewDiagnosticsCommand.PublishRecoveryProbeFactory = _ =>
        {
            seams.PublishRecoveryProbeFactoryCalls++;
            return new Ac9RecoveryProbe();
        };
        AutomationHostReviewDiagnosticsCommand.NestedProviderLauncher = () =>
        {
            seams.ProviderLauncherCalls++;
            throw new InvalidOperationException("provider must not run");
        };
    }

    private static void ResetSeams()
    {
        AutomationDurableStatePreflightCommand.ProbeFactory = null;
        AutomationHostReviewDiagnosticsCommand.CandidateListerFactory = null;
        AutomationHostReviewDiagnosticsCommand.NextSliceDryRunProbeFactory = null;
        AutomationHostReviewDiagnosticsCommand.PublishRecoveryProbeFactory = null;
        AutomationHostReviewDiagnosticsCommand.NestedProviderLauncher = null;
        AutomationInstalledCliSurfaceProbe.ProbeRunner = null;
        AutomationInstalledCliSurfaceProbe.PathResolver = null;
        AutomationInstalledCliSurfaceProbe.ExplicitInstalledCliPathReader = null;
    }

    private static string Run(Ac9Workspace workspace, string[] args)
    {
        using var writer = new StringWriter();
        var exitCode = CommandRouter.Execute(args, workspace.Context, writer);
        return $"exit_code={exitCode}\n{NormalizePaths(writer.ToString(), workspace.RootPath)}";
    }

    private static void AssertQueueSeedReachesAnalyzer(string output)
    {
        Assert.StartsWith("exit_code=0\n", output, StringComparison.Ordinal);
        using var document = JsonDocument.Parse(output[(output.IndexOf('\n') + 1)..]);
        var result = document.RootElement;
        Assert.Equal("queue-seed-ready", result.GetProperty("classification").GetString());
        Assert.True(result.GetProperty("contract_publishable").GetBoolean());
        Assert.Empty(result.GetProperty("refusal_reasons").EnumerateArray());
        Assert.Empty(result.GetProperty("missing_canonical_files").EnumerateArray());
        Assert.Empty(result.GetProperty("missing_contract_sections").EnumerateArray());
    }

    private static void AssertGolden(string fileName, string actual, string workspaceRoot)
    {
        var captureDirectory = Environment.GetEnvironmentVariable(CaptureDirectoryVariable);
        if (!string.IsNullOrWhiteSpace(captureDirectory))
        {
            Directory.CreateDirectory(captureDirectory!);
            File.WriteAllText(Path.Combine(captureDirectory!, fileName), actual);
            return;
        }

        var goldenPath = Path.Combine(
            RepoVersionPolicySource.RepoRoot(),
            "tests",
            "IntentSystem.Cli.Tests",
            "Fixtures",
            "G846",
            fileName);
        Assert.True(File.Exists(goldenPath), $"Missing G846 golden: {goldenPath}");
        Assert.Equal(File.ReadAllText(goldenPath), actual);
    }

    private static string NormalizePaths(string output, string workspaceRoot)
    {
        var normalized = output.Replace(workspaceRoot, RootToken, StringComparison.Ordinal);
        var forwardSlashRoot = workspaceRoot.Replace('\\', '/');
        if (!string.Equals(workspaceRoot, forwardSlashRoot, StringComparison.Ordinal))
        {
            normalized = normalized.Replace(forwardSlashRoot, RootToken, StringComparison.Ordinal);
        }

        return normalized;
    }

    private sealed class SeamRecorder
    {
        public int CandidateListerFactoryCalls { get; set; }
        public int CandidateListerMethodCalls { get; set; }
        public int NextSliceProbeFactoryCalls { get; set; }
        public int PublishRecoveryProbeFactoryCalls { get; set; }
        public int ProbeFactoryCalls { get; set; }
        public int InstalledSurfaceProbeCalls { get; set; }
        public int ProviderLauncherCalls { get; set; }
    }

    private sealed class Ac9CandidateLister(SeamRecorder seams, FakeGhScope fakeGh) : IGitHubAutomationCandidateLister
    {
        public IReadOnlyList<GitHubAutomationPrCandidate> ListPullRequests(
            string repo,
            IReadOnlyCollection<string> requiredLabels)
        {
            seams.CandidateListerMethodCalls++;
            fakeGh.Invoke();
            return Array.Empty<GitHubAutomationPrCandidate>();
        }

        public IReadOnlyList<GitHubAutomationIssueCandidate> ListIssues(
            string repo,
            IReadOnlyCollection<string> requiredLabels)
        {
            seams.CandidateListerMethodCalls++;
            fakeGh.Invoke();
            return Array.Empty<GitHubAutomationIssueCandidate>();
        }
    }

    private sealed class Ac9NextSliceProbe : INextSliceDryRunProbe
    {
        public NextSliceProbeResult? Probe(string repo, string domain)
        {
            return new NextSliceProbeResult
            {
                RecommendedOutcome = "no-actionable-item"
            };
        }
    }

    private sealed class Ac9RecoveryProbe : IPublishRecoveryProbe
    {
        public PublishRecoveryProbeResult? Probe(string repo) =>
            new()
            {
                SafeRepairCount = 0,
                UnsafeStopCount = 0
            };
    }

    private sealed class FakeGhScope : IDisposable
    {
        private readonly string? originalPath;
        private readonly string binDirectory;

        public FakeGhScope(string root)
        {
            originalPath = Environment.GetEnvironmentVariable("PATH");
            binDirectory = Path.Combine(root, "fake-gh-bin");
            Directory.CreateDirectory(binDirectory);
            LogPath = Path.Combine(root, "fake-gh.log");
            if (OperatingSystem.IsWindows())
            {
                File.WriteAllText(
                    Path.Combine(binDirectory, "gh.cmd"),
                    "@echo off\r\necho gh %*>>\"%G846_AC9_FAKE_GH_LOG%\"\r\necho []\r\nexit /b 0\r\n");
            }
            else
            {
                var path = Path.Combine(binDirectory, "gh");
                File.WriteAllText(
                    path,
                    "#!/bin/sh\nprintf 'gh' >> \"$G846_AC9_FAKE_GH_LOG\"\nprintf ' %s' \"$@\" >> \"$G846_AC9_FAKE_GH_LOG\"\nprintf '\\n' >> \"$G846_AC9_FAKE_GH_LOG\"\nprintf '[]\\n'\n");
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }

            var pathValue = string.IsNullOrWhiteSpace(originalPath)
                ? binDirectory
                : binDirectory + Path.PathSeparator + originalPath;
            Environment.SetEnvironmentVariable("PATH", pathValue);
        }

        public string LogPath { get; }

        public bool HasInvocations => File.Exists(LogPath) && new FileInfo(LogPath).Length > 0;

        public void Invoke()
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "gh",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                Environment =
                {
                    ["G846_AC9_FAKE_GH_LOG"] = LogPath
                }
            });
            Assert.NotNull(process);
            var stdout = process!.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();
            Assert.Equal(0, process.ExitCode);
            Assert.Equal("[]\n", stdout);
            Assert.Empty(stderr);
        }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable("PATH", originalPath);
        }
    }

    private sealed class Ac9Workspace : IDisposable
    {
        public Ac9Workspace()
        {
            RootPath = Directory.CreateTempSubdirectory("g846-ac9-").FullName;
            Context = new CliContext
            {
                RepoRoot = RootPath,
                Config = new CliConfig
                {
                    Project = new ProjectConfig
                    {
                        Domain = Domain,
                        ArtifactRoot = ".intent-cli",
                        WorktreeRoot = ".intent-cli/worktrees"
                    }
                }
            };

            var bindingsDirectory = Path.Combine(RootPath, "intents", Domain, "automation");
            Directory.CreateDirectory(bindingsDirectory);
            File.WriteAllText(
                Path.Combine(bindingsDirectory, "bindings.md"),
                "---\nexecution_unit_regex: '^G[0-9]+$'\n---\n");
        }

        public string RootPath { get; }
        public CliContext Context { get; }
        private string PacketDirectory => Path.Combine(RootPath, ".intent-cli", "issues", Unit);
        private string BodyPath => Path.Combine(PacketDirectory, "github-body.md");

        public void WritePacket(int bodyBytes)
        {
            Directory.CreateDirectory(PacketDirectory);
            File.WriteAllText(
                Path.Combine(PacketDirectory, "packet.yaml"),
                """
                implementation_issue_packet:
                  issue_title: "G846 deterministic AC9 fixture"
                  issue_kind: feature
                  source_execution_unit: G846
                  domain: intent-cli
                  target_repo: J-Tech-Japan/intent-system
                  target_path: src/IntentSystem.Cli/Commands
                  target_part: early body size
                  dependencies: []
                  technical_baseline:
                    - C# / .NET 10
                  intent_references: []
                  acceptance_criteria:
                    - count file bytes
                """);
            File.WriteAllText(Path.Combine(PacketDirectory, "implementation.md"), "# deterministic implementation\n");
            File.WriteAllText(Path.Combine(PacketDirectory, "review-context.md"), "# deterministic review context\n");
            File.WriteAllBytes(BodyPath, FixtureBytes(bodyBytes));
        }

        public string WriteInstalledCliPlaceholder()
        {
            var path = Path.Combine(RootPath, "installed-intent-cli");
            File.WriteAllText(path, "surface probe placeholder\n");
            return path;
        }

        public void Dispose()
        {
            if (Directory.Exists(RootPath))
            {
                Directory.Delete(RootPath, recursive: true);
            }
        }

        private static byte[] FixtureBytes(int totalBytes)
        {
            var content = new StringBuilder("# G846 deterministic AC9 fixture\n\n");
            foreach (var heading in IssueValidateBodyValidator.RequiredHeadings)
            {
                content.Append("## ").Append(heading).Append("\n\n");
                if (heading == "Target Repo / Path / Part")
                {
                    content.Append("- Repository: J-Tech-Japan/intent-system\n");
                    content.Append("- Target paths: src/IntentSystem.Cli/Commands\n");
                }
                else if (heading == "Related Links")
                {
                    content.Append("- https://github.com/J-Tech-Japan/intent-system/issues/1835\n");
                }
                else
                {
                    content.Append("Deterministic AC9 fixture content.\n");
                }

                content.Append('\n');
            }

            var prefix = Encoding.UTF8.GetBytes(content.ToString());
            Assert.True(totalBytes >= prefix.Length);
            var bytes = new byte[totalBytes];
            prefix.CopyTo(bytes, 0);
            for (var index = prefix.Length; index < bytes.Length; index++)
            {
                bytes[index] = (byte)('a' + (index % 26));
            }

            return bytes;
        }
    }
}
