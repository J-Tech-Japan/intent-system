using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Infrastructure;
using IntentSystem.Cli.Models;
using IntentSystem.Supervisor;
using IntentSystem.Supervisor.Models;
using IntentSystem.Supervisor.Serialization;
using static IntentSystem.Cli.Tests.G839RecoveryFakes;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G839: deterministic command-output capture for byte-identity fixtures against base
/// <c>ba496314</c>. Every GitHub seam stays on fakes; <see cref="FixedNow"/> is pinned.
/// </summary>
internal static class G839ByteIdentityHarness
{
    internal const string Domain = "intent-cli";
    internal const string Repo = "J-Tech-Japan/intent-system";
    internal const string UngatedRepo = "J-Tech-Japan/other";
    internal const string Unit = "G839-BYTE";
    internal const int Issue = 839;
    internal const int Pr = 1823;
    internal const string Team = "intent-cli-dev";
    internal const string Ruling = "stale intent-pr-created after unmerged PR close";
    internal const string H2 = "2222222222222222222222222222222222222222";
    internal const string H3 = "3333333333333333333333333333333333333333";

    internal static readonly DateTimeOffset FixedNow = new(2026, 9, 16, 12, 0, 0, TimeSpan.Zero);

    internal static readonly string[] RecoveryScenarioIds =
    [
        "recovery-proceed-dry-run-json",
        "recovery-proceed-dry-run-markdown",
        "recovery-recovered-write-json",
        "recovery-issue-not-open-refusal-json",
        "recovery-issue-not-open-refusal-markdown",
        "recovery-already-recovered-json",
        "recovery-ruling-missing-markdown",
        "recovery-help",
        "recovery-parse-error",
        "recovery-recovered-event-append-failed-write-json",
        "recovery-label-readback-unconfirmed-write-json",
        "recovery-event-completed-write-json",
        "recovery-closed-then-label-removed-write-json",
        "recovery-recovery-completed-write-json",
        "recovery-target-absent-refusal-json",
        "recovery-pr-merged-refusal-json",
        "recovery-started-event-append-failed-write-json",
        "recovery-label-removal-failed-write-json",
        "recovery-label-readback-failed-write-json",
        "recovery-superseded-event-append-failed-write-json",
    ];

    internal static readonly string[] PrTransitionRefusalTextScenarioIds =
    [
        "pr-transition-refusal-head-required-text",
        "pr-transition-refusal-head-stale-text",
        "pr-transition-refusal-team-unresolved-text",
        "pr-transition-refusal-missing-text",
        "pr-transition-refusal-blocked-text",
    ];

    internal static readonly string[] PrTransitionNonRefusalScenarioIds =
    [
        "pr-transition-review-start-dry-run-json",
        "pr-transition-review-start-dry-run-text",
        "pr-transition-approved-ungated-dry-run-json",
        "pr-transition-review-release-write-text",
        "pr-transition-request-update-dry-run-json",
        "pr-transition-approved-undeclared-dry-run-json",
        "pr-transition-review-start-write-json",
        "pr-transition-failure-may-have-applied-write-json",
    ];

    internal static readonly string[] PlannedLabelsConsumerScenarioIds =
    [
        "planned-labels-automation-summary",
        "planned-labels-automation-doctor",
        "planned-labels-worker-next-action",
        "planned-labels-worker-pr-comment-preflight",
    ];



    internal static string CapturePlannedLabelsConsumer(string fixtureId) =>
        fixtureId switch
        {
            "planned-labels-automation-summary" => CaptureAutomationSummary(),
            "planned-labels-automation-doctor" => CaptureAutomationDoctor(),
            "planned-labels-worker-next-action" => G839RecoveryByteIdentityTests.CaptureWorkerNextAction(),
            "planned-labels-worker-pr-comment-preflight" => WorkerPrCommentPreflightCommandTests.G839PlannedLabelsPreflightCapture.Capture(),
            _ => throw new ArgumentOutOfRangeException(nameof(fixtureId), fixtureId, "unknown planned-labels consumer"),
        };

    private static string CaptureAutomationSummary()
    {
        using var workspace = new SummaryWorkspace();
        workspace.WriteBindings(
            """
            ---
            domain: intent-cli
            repo: owner/repo
            submodule_path: submodules/repo
            queue_state_path: .intent-cli/queue-state.json
            runs_log_path: .intent-cli/runs.jsonl
            packet_root: .intent-cli/issues
            execution_unit_regex: '^G[0-9]+$'
            ---
            """);

        using var writer = new StringWriter();
        AutomationSummaryCommand.Execute(
            workspace.Context,
            ["--format", "json", "--domain", Domain],
            writer);
        return writer.ToString();
    }

    private static string CaptureAutomationDoctor()
    {
        using var workspace = new DoctorWorkspace();
        using var writer = new StringWriter();
        AutomationDoctorCommand.Execute(workspace.Context, ["--format", "text"], writer);
        return writer.ToString();
    }



    internal static string[] BuildRecoveryArgs(string fixtureId)
    {
        if (fixtureId == "recovery-help")
        {
            return ["--help"];
        }

        if (fixtureId == "recovery-parse-error")
        {
            return ["--repo", Repo];
        }

        var args = new List<string>
        {
            "--repo", Repo,
            "--issue", Issue.ToString(CultureInfo.InvariantCulture),
            "--execution-unit", Unit,
            "--team", Team,
        };

        if (fixtureId == "recovery-ruling-missing-markdown")
        {
            args.AddRange(["--ruling", "   "]);
        }
        else
        {
            args.AddRange(["--ruling", Ruling]);
        }

        if (fixtureId.EndsWith("-json", StringComparison.Ordinal))
        {
            args.AddRange(["--format", "json"]);
        }
        else if (fixtureId.EndsWith("-markdown", StringComparison.Ordinal))
        {
            args.AddRange(["--format", "markdown"]);
        }

        if (fixtureId.Contains("-write-", StringComparison.Ordinal))
        {
            args.Add("--write");
        }

        return args.ToArray();
    }

    internal static string RunRecovery(RecoveryWorkspace workspace, string[] args)
    {
        using var writer = new StringWriter();
        AutomationPrCreatedStaleRecoveryCommand.Execute(workspace.Context, args, writer);
        return writer.ToString();
    }

    internal static RecoveryWorkspace CreateProceedWorkspace()
    {
        var workspace = new RecoveryWorkspace();
        workspace.WriteProceedHostState(linkedPr: Pr);
        workspace.EnsureClaimsStore();
        return workspace;
    }


    internal static GitHubIssueLookupResult OpenIssue(params string[] labels) => new()
    {
        Number = Issue,
        State = "OPEN",
        Title = $"{Unit}: stale pr-created recovery fixture",
        Labels = labels.Select(name => new GitHubIssueLabel { Name = name }).ToArray(),
    };

    internal static GitHubIssueLookupResult ClosedIssue() => new()
    {
        Number = Issue,
        State = "CLOSED",
        Title = $"{Unit}: closed",
        Labels = Array.Empty<GitHubIssueLabel>(),
    };

    internal static GitHubPrLookupResult ClosedUnmerged(int pr) => new()
    {
        Number = pr,
        State = "CLOSED",
        Merged = false,
        MergedAt = null,
    };

    internal static GitHubPrLookupResult Merged(int pr) => new()
    {
        Number = pr,
        State = "MERGED",
        Merged = true,
        MergedAt = "2026-01-01T00:00:00Z",
    };

    internal static RunEvent BuildRecoveryEvent(string eventName, int pr, string? reason = null) => new()
    {
        Ts = FixedNow.AddMinutes(-10),
        ExecutionUnit = Unit,
        Event = eventName,
        By = AutomationPrCreatedStaleRecoveryCommand.By,
        Repo = Repo,
        LinkedIssue = $"https://github.com/{Repo}/issues/{Issue}",
        LinkedPr = $"https://github.com/{Repo}/pull/{pr}",
        Pr = pr,
        Reason = reason ?? $"ruling: {Ruling}; claim: unheld-available",
    };

    internal static void SetRunLogReadOnly(CliContext context, bool readOnly)
    {
        var runsPath = context.GetRunLogPath();
        Directory.CreateDirectory(Path.GetDirectoryName(runsPath)!);
        if (!File.Exists(runsPath))
        {
            File.WriteAllText(runsPath, string.Empty);
        }

        if (readOnly)
        {
            if (OperatingSystem.IsWindows())
            {
                File.SetAttributes(runsPath, FileAttributes.ReadOnly);
            }
            else
            {
                RunChmod(runsPath, "444");
            }
        }
        else
        {
            File.SetAttributes(runsPath, FileAttributes.Normal);
            if (!OperatingSystem.IsWindows())
            {
                RunChmod(runsPath, "644");
            }
        }
    }

    private static void RunChmod(string path, string mode)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "chmod",
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add(mode);
        startInfo.ArgumentList.Add(path);
        using var process = Process.Start(startInfo)!;
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(process.StandardError.ReadToEnd());
        }
    }



    private static QueueItem BuildQueueItem(string? linkedPr) =>
        new()
        {
            ExecutionUnit = Unit,
            Title = $"{Unit} title",
            State = QueueItemState.Queued,
            Dependencies = Array.Empty<string>(),
            BlockedBy = Array.Empty<string>(),
            ClarificationReturnPath = string.Empty,
            PacketPaths = new PacketPaths
            {
                Yaml = $".intent-cli/issues/{Unit}/packet.yaml",
                Implementation = $".intent-cli/issues/{Unit}/implementation.md",
                ReviewContext = $".intent-cli/issues/{Unit}/review-context.md",
            },
            LinkedIssue = new LinkedIssue
            {
                Repo = Repo,
                Number = Issue,
                Url = $"https://github.com/{Repo}/issues/{Issue}",
            },
            LinkedPr = linkedPr,
            WorkerRole = LogicalRoleNormalizer.Builder,
            ReviewRole = LogicalRoleNormalizer.Reviewer,
            Priority = "normal",
        };

    internal sealed class RecoveryWorkspace : IDisposable
    {
        public RecoveryWorkspace()
        {
            RootPath = Directory.CreateTempSubdirectory("g839-byte-recovery-").FullName;
            Directory.CreateDirectory(Path.Combine(RootPath, ".intent-cli"));
            Context = new CliContext
            {
                RepoRoot = RootPath,
                Config = new CliConfig
                {
                    Project = new ProjectConfig
                    {
                        Domain = Domain,
                        ArtifactRoot = ".intent-cli",
                        WorktreeRoot = ".intent-cli/worktrees",
                    },
                },
            };
        }

        public string RootPath { get; }

        public CliContext Context { get; }

        public void WriteProceedHostState(int linkedPr, int? publishPr = null)
        {
            WriteQueueState(linkedPr);
            var issueDir = Path.Combine(RootPath, ".intent-cli", "issues", Unit);
            Directory.CreateDirectory(issueDir);
            File.WriteAllText(
                Path.Combine(issueDir, "publish.yaml"),
                IssuePublishArtifactYaml.Serialize(new IssuePublishArtifact
                {
                    ExecutionUnit = Unit,
                    PublishStatus = "published",
                    PacketPath = $".intent-cli/issues/{Unit}/packet.yaml",
                    IssueBodyPath = $".intent-cli/issues/{Unit}/issue-body.md",
                    CreatedIssueNumber = Issue,
                    CreatedIssueUrl = $"https://github.com/{Repo}/issues/{Issue}",
                    PublishedLabelName = "intent-target",
                    LinkedPrNumber = publishPr ?? linkedPr,
                    LinkedPrUrl = $"https://github.com/{Repo}/pull/{publishPr ?? linkedPr}",
                }));
        }

        public void WriteQueueState(int linkedPr, int publishPr = Pr)
        {
            File.WriteAllText(
                Context.GetQueueStatePath(),
                QueueStateSerializer.Serialize(new QueueState
                {
                    SchemaVersion = "1",
                    UpdatedAt = FixedNow,
                    Items = [BuildQueueItem($"https://github.com/{Repo}/pull/{linkedPr}")],
                }));

            var issueDir = Path.Combine(RootPath, ".intent-cli", "issues", Unit);
            Directory.CreateDirectory(issueDir);
            File.WriteAllText(
                Path.Combine(issueDir, "publish.yaml"),
                IssuePublishArtifactYaml.Serialize(new IssuePublishArtifact
                {
                    ExecutionUnit = Unit,
                    PublishStatus = "published",
                    PacketPath = $".intent-cli/issues/{Unit}/packet.yaml",
                    IssueBodyPath = $".intent-cli/issues/{Unit}/issue-body.md",
                    CreatedIssueNumber = Issue,
                    CreatedIssueUrl = $"https://github.com/{Repo}/issues/{Issue}",
                    PublishedLabelName = "intent-target",
                    LinkedPrNumber = publishPr,
                    LinkedPrUrl = $"https://github.com/{Repo}/pull/{publishPr}",
                }));
        }

        public void EnsureClaimsStore() =>
            Directory.CreateDirectory(Path.Combine(RootPath, ".intent-cli", "claims"));

        public void AppendRunEvent(RunEvent runEvent)
        {
            var runsPath = Context.GetRunLogPath();
            Directory.CreateDirectory(Path.GetDirectoryName(runsPath)!);
            File.AppendAllText(runsPath, RunLogSerializer.SerializeLine(runEvent) + "\n");
        }

        public void Dispose()
        {
            if (Directory.Exists(RootPath))
            {
                Directory.Delete(RootPath, recursive: true);
            }
        }
    }

    internal sealed class CliWorkspace : IDisposable
    {
        public CliWorkspace(string prefix)
        {
            RootPath = Directory.CreateTempSubdirectory(prefix).FullName;
            Directory.CreateDirectory(Path.Combine(RootPath, ".intent-cli"));
            Context = new CliContext
            {
                RepoRoot = RootPath,
                Config = new CliConfig
                {
                    Project = new ProjectConfig
                    {
                        Domain = Domain,
                        ArtifactRoot = ".intent-cli",
                    },
                },
            };
        }

        public string RootPath { get; }

        public CliContext Context { get; }

        public void Dispose()
        {
            if (Directory.Exists(RootPath))
            {
                Directory.Delete(RootPath, recursive: true);
            }
        }
    }

    private sealed class SummaryWorkspace : IDisposable
    {
        private readonly string rootPath = Directory.CreateTempSubdirectory("automation-summary-g839-").FullName;

        public SummaryWorkspace()
        {
            Directory.CreateDirectory(Path.Combine(rootPath, ".intent-cli"));
            Context = new CliContext
            {
                RepoRoot = rootPath,
                Config = new CliConfig
                {
                    Project = new ProjectConfig
                    {
                        Domain = Domain,
                        ArtifactRoot = ".intent-cli",
                    },
                },
            };
        }

        public CliContext Context { get; }

        public void WriteBindings(string content)
        {
            var dir = Path.Combine(rootPath, "intents", Domain, "automation");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "bindings.md"), content);
        }

        public void Dispose()
        {
            if (Directory.Exists(rootPath))
            {
                Directory.Delete(rootPath, recursive: true);
            }
        }
    }

    private sealed class DoctorWorkspace : IDisposable
    {
        private readonly string rootPath = Path.Combine(Path.GetTempPath(), "automation-doctor-g839-byte-identity");

        public DoctorWorkspace()
        {
            if (Directory.Exists(rootPath))
            {
                Directory.Delete(rootPath, recursive: true);
            }

            Directory.CreateDirectory(rootPath);
            var binPath = Path.Combine(rootPath, ".intent-cli", "bin");
            Directory.CreateDirectory(binPath);
            var scriptPath = Path.Combine(binPath, "intent-cli");
            File.WriteAllText(
                scriptPath,
                "#!/bin/sh\n"
                + "case \"$*\" in\n"
                + "  'automation summary') echo '--domain is required.'; exit 1 ;;\n"
                + "  'automation host-review-preflight') echo '--repo is required.'; exit 1 ;;\n"
                + "  'automation issue-publish') echo '--issue is required.'; exit 1 ;;\n"
                + "  'automation pr-transition')\n"
                + "    echo '--transition is required (review-start, request-update, or approved).'\n"
                + "    exit 1\n"
                + "    ;;\n"
                + "  *) echo \"unexpected probe: $*\"; exit 1 ;;\n"
                + "esac\n");
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(
                    scriptPath,
                    UnixFileMode.UserRead
                    | UnixFileMode.UserWrite
                    | UnixFileMode.UserExecute
                    | UnixFileMode.GroupRead
                    | UnixFileMode.GroupExecute
                    | UnixFileMode.OtherRead
                    | UnixFileMode.OtherExecute);
            }

            Context = new CliContext
            {
                RepoRoot = rootPath,
                Config = new CliConfig
                {
                    Project = new ProjectConfig
                    {
                        Domain = Domain,
                        ArtifactRoot = ".intent-cli",
                    },
                },
            };
        }

        public CliContext Context { get; }

        public void Dispose()
        {
            if (Directory.Exists(rootPath))
            {
                Directory.Delete(rootPath, recursive: true);
            }
        }
    }

    internal sealed class ReadbackUnconfirmedLabelMutator : IGitHubLabelMutator
    {
        public void ApplyLabelTransitions(string repo, string kind, int number,
            IReadOnlyCollection<string> addLabels, IReadOnlyCollection<string> removeLabels)
        {
        }

        public IReadOnlyList<GitHubAutomationLabel> ReadLabels(string repo, string kind, int number) =>
        [
            new GitHubAutomationLabel { Name = WorkerNextActionConstants.Labels.IntentTarget },
            new GitHubAutomationLabel { Name = WorkerNextActionConstants.Labels.IntentPrCreated },
        ];

        public void ApplyReconcileTransitions(string repo, string kind, int number,
            IReadOnlyCollection<string> addLabels, IReadOnlyCollection<string> removeLabels) =>
            throw new NotSupportedException();
    }

    internal sealed class ThrowingReadbackLabelMutator : IGitHubLabelMutator
    {
        private readonly RecordingLabelMutator inner;

        public ThrowingReadbackLabelMutator(params string[] labels) =>
            inner = new RecordingLabelMutator(labels);

        public void ApplyLabelTransitions(string repo, string kind, int number,
            IReadOnlyCollection<string> addLabels, IReadOnlyCollection<string> removeLabels) =>
            inner.ApplyLabelTransitions(repo, kind, number, addLabels, removeLabels);

        public IReadOnlyList<GitHubAutomationLabel> ReadLabels(string repo, string kind, int number) =>
            throw new IOException("readback failed");

        public void ApplyReconcileTransitions(string repo, string kind, int number,
            IReadOnlyCollection<string> addLabels, IReadOnlyCollection<string> removeLabels) =>
            throw new NotSupportedException();
    }

    internal sealed class ThrowingApplyLabelMutator : IGitHubLabelMutator
    {
        private readonly RecordingLabelMutator inner;

        public ThrowingApplyLabelMutator(params string[] labels) =>
            inner = new RecordingLabelMutator(labels);

        public IReadOnlyList<GitHubAutomationLabel> ReadLabels(string repo, string kind, int number) =>
            inner.ReadLabels(repo, kind, number);

        public void ApplyLabelTransitions(string repo, string kind, int number,
            IReadOnlyCollection<string> addLabels, IReadOnlyCollection<string> removeLabels) =>
            throw new IOException("label removal failed");

        public void ApplyReconcileTransitions(string repo, string kind, int number,
            IReadOnlyCollection<string> addLabels, IReadOnlyCollection<string> removeLabels) =>
            throw new NotSupportedException();
    }

    internal sealed class ReadonlyAfterMutationLabelMutator : IGitHubLabelMutator
    {
        private readonly RecoveryWorkspace workspace;
        private readonly RecordingLabelMutator inner;

        public ReadonlyAfterMutationLabelMutator(RecoveryWorkspace workspace, params string[] labels)
        {
            this.workspace = workspace;
            inner = new RecordingLabelMutator(labels);
        }

        public void ApplyLabelTransitions(string repo, string kind, int number,
            IReadOnlyCollection<string> addLabels, IReadOnlyCollection<string> removeLabels)
        {
            inner.ApplyLabelTransitions(repo, kind, number, addLabels, removeLabels);
            SetRunLogReadOnly(workspace.Context, readOnly: true);
        }

        public IReadOnlyList<GitHubAutomationLabel> ReadLabels(string repo, string kind, int number) =>
            inner.ReadLabels(repo, kind, number);

        public void ApplyReconcileTransitions(string repo, string kind, int number,
            IReadOnlyCollection<string> addLabels, IReadOnlyCollection<string> removeLabels) =>
            throw new NotSupportedException();
    }

    internal sealed class RecordingPrMutator : IGitHubLabelMutator, IGitHubLabelSetReplacer
    {
        public List<string> Labels { get; set; } = [];

        public IReadOnlyList<GitHubAutomationLabel> ReadLabels(string repo, string kind, int number) =>
            Labels.Select(name => new GitHubAutomationLabel { Name = name }).ToArray();

        public void ApplyLabelTransitions(string repo, string kind, int number,
            IReadOnlyCollection<string> addLabels, IReadOnlyCollection<string> removeLabels)
        {
            foreach (var label in removeLabels)
            {
                Labels.Remove(label);
            }

            foreach (var label in addLabels)
            {
                if (!Labels.Contains(label))
                {
                    Labels.Add(label);
                }
            }
        }

        public void ApplyReconcileTransitions(string repo, string kind, int number,
            IReadOnlyCollection<string> addLabels, IReadOnlyCollection<string> removeLabels) =>
            throw new NotSupportedException();

        public LabelSetReplacementCertainty ReplaceLabelSet(string repo, string kind, int number,
            IReadOnlyCollection<string> currentLabels, IReadOnlyCollection<string> desiredLabels) =>
            LabelSetReplacementCertainty.AppliedAndVerified;
    }

    internal sealed class ThrowingPrMutator : IGitHubLabelMutator, IGitHubLabelSetReplacer
    {
        public List<string> Labels { get; set; } = [];

        public LabelSetReplacementFailureCertainty ReplaceLabelSetFailureCertainty { get; set; }
            = LabelSetReplacementFailureCertainty.KnownUnapplied;

        public IReadOnlyList<GitHubAutomationLabel> ReadLabels(string repo, string kind, int number) =>
            Labels.Select(name => new GitHubAutomationLabel { Name = name }).ToArray();

        public void ApplyLabelTransitions(string repo, string kind, int number,
            IReadOnlyCollection<string> addLabels, IReadOnlyCollection<string> removeLabels) =>
            throw new IOException("failed to apply PR transition");

        public void ApplyReconcileTransitions(string repo, string kind, int number,
            IReadOnlyCollection<string> addLabels, IReadOnlyCollection<string> removeLabels) =>
            throw new NotSupportedException();

        public LabelSetReplacementCertainty ReplaceLabelSet(string repo, string kind, int number,
            IReadOnlyCollection<string> currentLabels, IReadOnlyCollection<string> desiredLabels) =>
            throw new LabelSetReplacementException("simulated ambiguous gh API failure", ReplaceLabelSetFailureCertainty);
    }

    internal sealed class WorkerLister : IGitHubAutomationCandidateLister
    {
        public IReadOnlyList<GitHubAutomationPrCandidate> ListPullRequests(string repo, IReadOnlyCollection<string> requiredLabels) =>
        [
            new GitHubAutomationPrCandidate
            {
                Number = 514,
                Title = "G839 PR",
                Url = $"https://github.com/{Repo}/pull/514",
                CreatedAt = "2026-04-30T00:00:00Z",
                UpdatedAt = "2026-04-30T00:00:00Z",
                State = "OPEN",
                Labels = [new GitHubAutomationLabel { Name = "intent-target" }, new GitHubAutomationLabel { Name = "intent-pr-request-update" }],
            },
        ];

        public IReadOnlyList<GitHubAutomationIssueCandidate> ListIssues(string repo, IReadOnlyCollection<string> requiredLabels) =>
            Array.Empty<GitHubAutomationIssueCandidate>();
    }

    internal sealed class WorkerCommentsLookup : IGitHubPrCommentsLookup
    {
        public GitHubPrCommentsLookupResult Lookup(string repo, int prNumber) => new()
        {
            ReviewThreads =
            [
                new GitHubPrReviewThread
                {
                    Id = "RT_actionable",
                    IsResolved = false,
                    Comments =
                    [
                        new GitHubPrReviewThreadComment
                        {
                            Id = "C_actionable",
                            Author = "human-reviewer",
                            Body = "Please add a null check before dereferencing config in Foo().",
                        },
                    ],
                },
            ],
        };
    }

    internal sealed class WorkerIssueLookup : IGitHubIssueLookup
    {
        public GitHubIssueLookupResult Lookup(string repo, int issueNumber) => new()
        {
            Number = 100,
            State = "OPEN",
            Title = "source issue",
            Labels =
            [
                new GitHubIssueLabel { Name = "intent-target" },
                new GitHubIssueLabel { Name = "intent-pr-created" },
            ],
        };
    }

    internal sealed class WorkerPrLookup : IGitHubPrLookup
    {
        public GitHubPrLookupResult Lookup(string repo, int prNumber) => new()
        {
            Number = 616,
            State = "OPEN",
            Title = "Targeted PR",
            Body = "Closes #100",
            Labels = [new GitHubPrLabel { Name = "intent-target" }],
            ClosingIssuesReferences =
            [
                new GitHubPrClosingIssueReference
                {
                    Number = 100,
                    Repository = new GitHubPrClosingIssueRepository
                    {
                        Name = "intent-system",
                        Owner = new GitHubPrClosingIssueRepositoryOwner { Login = "J-Tech-Japan" },
                    },
                },
            ],
        };
    }
}
