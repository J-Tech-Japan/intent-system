using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;
using Xunit.Abstractions;

namespace IntentSystem.Cli.Tests;

[Collection(AutomationStalledWorkSharedStateCollection.Name)]
public sealed class AutomationStalledWorkG805Tests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 9, 4, 15, 0, 0, TimeSpan.Zero);
    private readonly ITestOutputHelper output;

    public AutomationStalledWorkG805Tests(ITestOutputHelper output)
    {
        this.output = output;
        AutomationStalledWorkCommand.CandidateListerFactory = null;
        AutomationStalledWorkCommand.UtcNowFactory = () => Now;
        AutomationStalledWorkCommand.GitCommandRunnerFactory = null;
        GhCliGitHubAutomationCandidateLister.ProcessRunner = null;
    }

    public void Dispose()
    {
        AutomationStalledWorkCommand.CandidateListerFactory = null;
        AutomationStalledWorkCommand.UtcNowFactory = null;
        AutomationStalledWorkCommand.GitCommandRunnerFactory = null;
        GhCliGitHubAutomationCandidateLister.ProcessRunner = null;
    }

    [Fact]
    public void G805_AC1_AC2_BothVerdictKindsCarryFieldsAndCanonicalActions()
    {
        using var workspace = new G805Workspace();
        var approved = BuildPr(8051, "G805 approved verdict", Now.AddMinutes(-20), "APPROVED", Now.AddMinutes(-30));
        var requestUpdate = BuildPr(8052, "G805 request update verdict", Now.AddMinutes(-18), "CHANGES_REQUESTED", Now.AddMinutes(-28));
        var result = Analyze(workspace, [approved, requestUpdate], thresholdMinutes: 5);

        var items = result.Items
            .Where(item => item.Kind == AutomationStalledWorkCommand.KindReviewVerdictAheadOfLabel)
            .OrderBy(item => item.Pr!.Number)
            .ToArray();
        Assert.Equal(2, items.Length);
        Assert.Equal("approve-equivalent", items[0].VerdictKind);
        Assert.Equal("approved", items[0].DueTransition);
        Assert.Contains("--transition approved", items[0].RecommendedAction, StringComparison.Ordinal);
        Assert.Equal("request-update", items[1].VerdictKind);
        Assert.Equal("request-update", items[1].DueTransition);
        Assert.Contains("--transition request-update", items[1].RecommendedAction, StringComparison.Ordinal);
        Assert.All(items, item =>
        {
            Assert.True(item.Pr!.Number > 0);
            Assert.True(item.AgeMinutes > 5);
            Assert.NotNull(item.VerdictAt);
            Assert.NotNull(item.LabelTransitionAt);
        });
        output.WriteLine(JsonSerializer.Serialize(items, new JsonSerializerOptions { WriteIndented = true }));
    }

    [Fact]
    public void G805_AC3_AC4_ReflectedLabelsAndBothQuietShapesStaySilent()
    {
        using var workspace = new G805Workspace();
        var reflectedApproved = BuildPr(8053, "G805 reflected approved", Now.AddMinutes(-20), "APPROVED", Now.AddMinutes(-10), labelName: "intent-pr-approved");
        var reflectedRequest = BuildPr(8054, "G805 reflected request", Now.AddMinutes(-18), "CHANGES_REQUESTED", Now.AddMinutes(-8), labelName: "intent-pr-request-update");
        var reviewPredatesLabel = BuildPr(8055, "G805 active review", Now.AddMinutes(-16), "APPROVED", Now.AddMinutes(-10));
        var noReview = BuildPr(8056, "G805 no review", Now.AddMinutes(-14), state: null, labelMinutesAgo: 9);
        var result = Analyze(
            workspace,
            [reflectedApproved, reflectedRequest, reviewPredatesLabel, noReview],
            thresholdMinutes: 0);

        Assert.DoesNotContain(result.Items, item => item.Kind == AutomationStalledWorkCommand.KindReviewVerdictAheadOfLabel);
        output.WriteLine("reflected_and_quiet_verdict_findings=0");
    }

    [Fact]
    public void G805_AC5_DefaultAndOperatorThresholdsAreDeclared()
    {
        using var workspace = new G805Workspace();
        var pr = BuildPr(8057, "G805 threshold", Now.AddMinutes(-20), "APPROVED", Now.AddMinutes(-30));

        var defaultResult = Analyze(workspace, [pr]);
        AutomationStalledWorkCommand.CandidateListerFactory = () => new FakeLister([pr]);
        using var writer = new StringWriter();
        Assert.Equal(
            0,
            AutomationStalledWorkCommand.Execute(
                workspace.Context,
                [
                    "--domain", "intent-cli",
                    "--repo", "J-Tech-Japan/intent-system",
                    "--review-verdict-ahead-minutes", "15",
                    "--format", "json",
                ],
                writer));
        using var overriddenDocument = JsonDocument.Parse(writer.ToString());

        Assert.Equal(AutomationStalledWorkCommand.DefaultReviewVerdictAheadMinutes, 5);
        Assert.Contains(defaultResult.Items, item => item.Kind == AutomationStalledWorkCommand.KindReviewVerdictAheadOfLabel);
        Assert.DoesNotContain(
            overriddenDocument.RootElement.GetProperty("items").EnumerateArray(),
            item => item.GetProperty("kind").GetString() == AutomationStalledWorkCommand.KindReviewVerdictAheadOfLabel);
        output.WriteLine($"default={AutomationStalledWorkCommand.DefaultReviewVerdictAheadMinutes}; override=15; findings={overriddenDocument.RootElement.GetProperty("items").GetArrayLength()}");
    }

    [Fact]
    public void G805_AC6_ReadOnlyScanPreservesDurableBytes()
    {
        using var workspace = new G805Workspace();
        workspace.WriteFile(".intent-cli/runs.jsonl", "before\n");
        workspace.WriteFile(".intent-cli/queue-state.json", "{\"schema_version\":\"1\"}\n");
        workspace.WriteFile(".intent-cli/custom-state.json", "durable\n");
        var before = HashWorkspace(workspace.RootPath);

        var pr = BuildPr(8058, "G805 read-only", Now.AddMinutes(-20), "APPROVED", Now.AddMinutes(-30));
        var result = Analyze(workspace, [pr]);
        var after = HashWorkspace(workspace.RootPath);

        Assert.Equal(before, after);
        Assert.Contains(result.Items, item => item.Kind == AutomationStalledWorkCommand.KindReviewVerdictAheadOfLabel);
        output.WriteLine($"before_sha256={before}\nafter_sha256={after}\nbytes_unchanged={before == after}");
    }

    [Fact]
    public void G805_AC7_FourMeasuredFixturesEachYieldTheFinding()
    {
        using var workspace = new G805Workspace();
        var prs = new[]
        {
            BuildPr(1743, "G805 measured approval one", Now.AddMinutes(-25), "APPROVED", Now.AddMinutes(-35)),
            BuildPr(1747, "G805 measured request update", Now.AddMinutes(-32), "CHANGES_REQUESTED", Now.AddMinutes(-42)),
            BuildPr(1748, "G805 measured approval two", Now.AddMinutes(-9), "APPROVED", Now.AddMinutes(-16)),
            BuildPr(1746, "G805 measured approval three", Now.AddHours(-2), "APPROVED", Now.AddHours(-3)),
        };
        var result = Analyze(workspace, prs, thresholdMinutes: 5);

        var findings = result.Items
            .Where(item => item.Kind == AutomationStalledWorkCommand.KindReviewVerdictAheadOfLabel)
            .OrderBy(item => item.Pr!.Number)
            .ToArray();
        Assert.Equal(4, findings.Length);
        Assert.Equal([1743, 1746, 1747, 1748], findings.Select(item => item.Pr!.Number).ToArray());
        Assert.All(findings, item => Assert.True(item.AgeMinutes > 5));
        output.WriteLine(JsonSerializer.Serialize(
            findings.Select(item => new
            {
                pr = item.Pr!.Number,
                item.VerdictKind,
                item.AgeMinutes,
                item.DueTransition,
            }).ToArray(),
            new JsonSerializerOptions { WriteIndented = true }));
    }

    [Fact]
    public void G805_AC1_ReadOnlyGitHubEnrichmentReadsReviewsAndLabelEvents()
    {
        var calls = new List<IReadOnlyList<string>>();
        GhCliGitHubAutomationCandidateLister.ProcessRunner = args =>
        {
            calls.Add(args);
            if (args[0] == "pr")
            {
                return new GhCliProcessResult(
                    0,
                    """{"reviews":[{"state":"APPROVED","submittedAt":"2026-09-04T14:40:00Z"}]}""",
                    string.Empty);
            }

            return new GhCliProcessResult(
                0,
                """[[{"event":"labeled","label":{"name":"intent-pr-rereview-ready"},"created_at":"2026-09-04T14:30:00Z"}]]""",
                string.Empty);
        };

        var candidate = BuildPr(
            8059,
            "G805 enrichment",
            Now.AddMinutes(-20),
            state: null,
            labelTransitionAt: Now.AddMinutes(-30)) with
        {
            IntentPrLabelTransitions = Array.Empty<GitHubAutomationLabelTransitionCandidate>(),
        };
        var enriched = Assert.Single(
            new GhCliGitHubAutomationCandidateLister().EnrichPullRequestLifecycle(
                "J-Tech-Japan/intent-system",
                [candidate],
                GitHubAutomationReadSurface.StalledWork));

        Assert.Equal("APPROVED", Assert.Single(enriched.Reviews).State);
        Assert.Equal("intent-pr-rereview-ready", Assert.Single(enriched.IntentPrLabelTransitions).Name);
        Assert.Equal(2, calls.Count);
        Assert.Contains(calls, args => args.SequenceEqual(
            GhCliGitHubAutomationCandidateLister.BuildPrReviewArguments(
                "J-Tech-Japan/intent-system",
                8059)));
        Assert.Contains(calls, args => args.SequenceEqual(
            GhCliGitHubAutomationCandidateLister.BuildPrEventsArguments(
                "J-Tech-Japan/intent-system",
                8059)));
        Assert.All(calls, args => Assert.DoesNotContain("--write", args, StringComparer.Ordinal));
        output.WriteLine(JsonSerializer.Serialize(new
        {
            reviews = enriched.Reviews.Count,
            label_transitions = enriched.IntentPrLabelTransitions.Count,
            read_only = calls.All(args => !args.Contains("--write", StringComparer.Ordinal)),
        }));
    }

    private static AutomationStalledWorkResult Analyze(
        G805Workspace workspace,
        IReadOnlyList<GitHubAutomationPrCandidate> prs,
        int thresholdMinutes = AutomationStalledWorkCommand.DefaultReviewVerdictAheadMinutes)
    {
        AutomationStalledWorkCommand.CandidateListerFactory = () => new FakeLister(prs);
        return AutomationStalledWorkCommand.Analyze(
            workspace.Context,
            "intent-cli",
            "J-Tech-Japan/intent-system",
            staleMinutes: 0,
            reviewVerdictAheadMinutes: thresholdMinutes);
    }

    private static GitHubAutomationPrCandidate BuildPr(
        int number,
        string title,
        DateTimeOffset reviewAt,
        string? state,
        DateTimeOffset labelTransitionAt = default,
        int? labelMinutesAgo = null,
        string labelName = "intent-pr-rereview-ready")
    {
        var transitionAt = labelMinutesAgo is int minutes
            ? Now.AddMinutes(-minutes)
            : labelTransitionAt;
        var labels = state switch
        {
            "APPROVED" => ["intent-pr-approved"],
            "CHANGES_REQUESTED" => ["intent-pr-request-update"],
            _ => Array.Empty<string>(),
        };
        return new GitHubAutomationPrCandidate
        {
            Number = number,
            Title = title,
            Url = $"https://github.com/J-Tech-Japan/intent-system/pull/{number}",
            State = "OPEN",
            CreatedAt = Now.AddHours(-4).ToString("O"),
            UpdatedAt = Now.AddMinutes(-1).ToString("O"),
            Labels = labels.Select(name => new GitHubAutomationLabel { Name = name }).ToArray(),
            Reviews = state is null
                ? Array.Empty<GitHubAutomationReviewCandidate>()
                : [new GitHubAutomationReviewCandidate
                {
                    State = state,
                    SubmittedAt = reviewAt.ToString("O"),
                }],
            IntentPrLabelTransitions =
            [
                new GitHubAutomationLabelTransitionCandidate
                {
                    Name = labelName,
                    Action = "labeled",
                    OccurredAt = transitionAt.ToString("O"),
                },
            ],
        };
    }

    private static string HashWorkspace(string root)
    {
        using var hash = SHA256.Create();
        var bytes = Directory
            .GetFiles(root, "*", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .OrderBy(path => path, StringComparer.Ordinal)
            .SelectMany(path => Encoding.UTF8.GetBytes(
                $"{Path.GetRelativePath(root, path)}\0{Convert.ToHexString(File.ReadAllBytes(path))}\n"))
            .ToArray();
        return Convert.ToHexString(hash.ComputeHash(bytes));
    }

    private sealed class FakeLister(IReadOnlyList<GitHubAutomationPrCandidate> prs) : IGitHubAutomationCandidateLister
    {
        public IReadOnlyList<GitHubAutomationPrCandidate> ListPullRequests(
            string repo,
            IReadOnlyCollection<string> requiredLabels) => prs;

        public IReadOnlyList<GitHubAutomationIssueCandidate> ListIssues(
            string repo,
            IReadOnlyCollection<string> requiredLabels) => Array.Empty<GitHubAutomationIssueCandidate>();
    }

    private sealed class G805Workspace : IDisposable
    {
        public G805Workspace()
        {
            RootPath = Directory.CreateTempSubdirectory("g805-stalled-work-").FullName;
            Context = new CliContext
            {
                RepoRoot = RootPath,
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
            WritePacket("G805");
        }

        public string RootPath { get; }

        public CliContext Context { get; }

        public void WriteFile(string relativePath, string content)
        {
            var path = Path.Combine(RootPath, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
        }

        private void WritePacket(string executionUnit) =>
            WriteFile($".intent-cli/issues/{executionUnit}/packet.yaml", $"domain: intent-cli\nsource_execution_unit: {executionUnit}\n");

        public void Dispose()
        {
            if (Directory.Exists(RootPath))
            {
                Directory.Delete(RootPath, recursive: true);
            }
        }
    }
}
