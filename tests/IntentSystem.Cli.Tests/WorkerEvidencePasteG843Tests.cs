using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

/// <summary>G843 coverage for labelled acceptance criteria and fence names.</summary>
[Collection("WorkerNextActionSharedState")]
public sealed class WorkerEvidencePasteG843Tests : IDisposable
{
    private readonly Func<IGitHubIssueLookup>? priorSummaryIssueLookupFactory;
    private readonly Func<IGitHubLabelMutator>? priorMutatorFactory;
    private readonly Func<IGitHubPrLookup>? priorPrLookupFactory;
    private readonly Func<IGitHubIssueLookup>? priorCompleteIssueLookupFactory;

    public WorkerEvidencePasteG843Tests()
    {
        priorSummaryIssueLookupFactory = WorkerResultSummaryCommand.IssueLookupFactory;
        priorMutatorFactory = WorkerCompleteCommand.MutatorFactory;
        priorPrLookupFactory = WorkerCompleteCommand.PrLookupFactory;
        priorCompleteIssueLookupFactory = WorkerCompleteCommand.IssueLookupFactory;
    }

    public void Dispose()
    {
        WorkerResultSummaryCommand.IssueLookupFactory = priorSummaryIssueLookupFactory;
        WorkerCompleteCommand.MutatorFactory = priorMutatorFactory;
        WorkerCompleteCommand.PrLookupFactory = priorPrLookupFactory;
        WorkerCompleteCommand.IssueLookupFactory = priorCompleteIssueLookupFactory;
    }

    [Theory]
    [MemberData(nameof(LabelGrammarCases))]
    public void LabelGrammar_RecognizesOnlyLeadingUniqueAcLabels_G843(string bullet, string? expectedLabel)
    {
        var analysis = WorkerEvidencePasteAnalyzer.Analyze(
            $"## Acceptance Criteria\n\n{bullet}\n",
            string.Empty);

        var criterion = Assert.Single(analysis.EvidenceRequired);
        Assert.Equal(expectedLabel, criterion.Label);
    }

    public static IEnumerable<object[]> LabelGrammarCases()
    {
        foreach (var bullet in new[]
                 {
                     "- **AC7 —** x actual output pasted.",
                     "- [ ] **AC7 — x** actual output pasted.",
                     "- AC7: x actual output pasted.",
                     "- **AC 7.** x actual output pasted.",
                     "- __AC#7__ x actual output pasted.",
                     "- ac7 x actual output pasted.",
                     "- AC 07 x actual output pasted.",
                 })
        {
            yield return [bullet, "AC7"];
        }

        foreach (var bullet in new[]
                 {
                     "- The AC7 note actual output pasted.",
                     "- **Note (AC7)** x actual output pasted.",
                     "- AC7x note actual output pasted.",
                     "- ACME 7 x actual output pasted.",
                     "- AC-7 x actual output pasted.",
                     "- **Criterion 7 —** x actual output pasted.",
                     "- AC 99999999999 x actual output pasted.",
                 })
        {
            yield return [bullet, (string)null!];
        }
    }

    [Fact]
    public void LabelGrammar_DuplicateAndSubBulletLabelsAreNotInherited_G843()
    {
        var duplicate = WorkerEvidencePasteAnalyzer.Analyze(
            "## Acceptance Criteria\n\n"
            + "- **AC7 —** first actual output pasted.\n"
            + "- **AC7 —** second actual output pasted.\n",
            "### AC7\n\n```text\ncollected\n```");
        Assert.Equal(new[] { 1, 2 }, duplicate.EvidenceRequired.Select(item => item.Ordinal));
        Assert.All(duplicate.EvidenceRequired, item => Assert.Null(item.Label));

        var nested = WorkerEvidencePasteAnalyzer.Analyze(
            "## Acceptance Criteria\n\n"
            + "- **AC7 —** parent actual output pasted.\n"
            + "  - child actual output pasted.\n",
            string.Empty);
        Assert.Equal(new[] { "AC7", null }, nested.EvidenceRequired.Select(item => item.Label));
    }

    [Fact]
    public void LabelGrammar_NonRequiredDuplicateRemovesLabelAndRestoresOrdinalMeaning_G843()
    {
        const string issue = "## Acceptance Criteria\n\n"
            + "- AC2 — this bullet is not required.\n"
            + "- AC2 — this required criterion says actual output pasted.\n";

        var analysis = WorkerEvidencePasteAnalyzer.Analyze(issue, "### AC 2\n\n```text\ncollected\n```");

        var required = Assert.Single(analysis.EvidenceRequired);
        Assert.Equal(2, required.Ordinal);
        Assert.Null(required.Label);
        Assert.Equal(new[] { 2 }, analysis.EvidenceBlocksPresent.Select(item => item.Ordinal));
    }

    [Theory]
    [MemberData(nameof(FixtureFNames))]
    public void FixtureF_EveryNameAtEveryAllowedLocationRunsThroughBothCommands_G843(
        string location,
        string name,
        bool expectedPresent)
    {
        using var workspace = new EvidenceWorkspace();
        var prBody = NamedBody(1833, location, name);

        var summaryOutput = RunResultSummary(workspace.Context, FixtureFIssue, prBody, "json");
        var summary = JsonSerializer.Deserialize<WorkerResultSummaryResult>(summaryOutput)!;
        Assert.Equal(expectedPresent ? [4] : Array.Empty<int>(), summary.EvidenceBlocksPresent.Select(item => item.Ordinal));
        Assert.Equal(expectedPresent ? Array.Empty<int>() : [4], summary.EvidenceGap.Select(item => item.Ordinal));

        var (completeExit, completeOutput) = RunWorkerComplete(workspace.Context, FixtureFIssue, prBody);
        if (expectedPresent)
        {
            Assert.Equal(0, completeExit);
            var completion = JsonSerializer.Deserialize<WorkerCompleteResult>(completeOutput)!;
            Assert.Equal(new[] { 4 }, completion.EvidenceBlocksPresent.Select(item => item.Ordinal));
            Assert.Empty(completion.EvidenceGap);
        }
        else
        {
            Assert.Equal(1, completeExit);
            Assert.Contains("AC2 (Criterion 4): " + FixtureFCriterionText, completeOutput, StringComparison.Ordinal);
        }
    }

    public static IEnumerable<object[]> FixtureFNames()
    {
        var cases = new (string Name, bool Present, bool FirstLineOnly)[]
        {
            ("AC2", true, false),
            ("AC 2", true, false),
            ("AC #2", true, false),
            ("AC#2", true, false),
            ("ac2", true, false),
            ("AC2:", true, false),
            ("AC2 — run", true, false),
            ("**AC2**", true, false),
            ("**AC2 — run**", true, false),
            ("AC 02", true, false),
            ("AC4", true, false),
            ("AC 4", true, false),
            ("AC #4", true, false),
            ("Criterion 4", true, false),
            ("Criteria 4", true, false),
            ("criterion #4", true, false),
            ("**Criterion 4 (AC2)**", true, false),
            ("AC4 passed", true, true),
            ("Labeled criterion paste the run", true, false),
            ("AC-2", false, false),
            ("Criterion 2", false, false),
            ("Criterion2", false, false),
            ("Criteria2", false, false),
            ("AC 2a", false, false),
            ("AC20", false, false),
            ("TRAC2", false, false),
            ("MAC2", false, false),
            ("AC 4a", false, false),
            ("AC 40", false, false),
            ("TRAC 4", false, false),
        };

        foreach (var (name, present, firstLineOnly) in cases)
        {
            foreach (var location in firstLineOnly
                         ? new[] { "fence first line" }
                         : new[] { "heading", "non-empty line", "fence first line" })
            {
                yield return [location, name, present];
            }
        }
    }

    private static string FixtureFIssue => "## Acceptance Criteria\n\n"
        + "- AC1 — parent not required.\n"
        + "  - first sub-bullet not required.\n"
        + "  - second sub-bullet not required.\n"
        + "- **AC2 —** Labeled criterion. Paste the run (**actual output pasted**).\n";

    private static string FixtureFCriterionText => "**AC2 —** Labeled criterion. Paste the run (**actual output pasted**).";

    private static string NamedBody(int issueNumber, string location, string name) => location switch
    {
        "heading" => $"Closes #{issueNumber}\n\n### {name}\n\n```text\ncollected output\n```",
        "non-empty line" => $"Closes #{issueNumber}\n\n{name}\n\n```text\ncollected output\n```",
        "fence first line" => $"Closes #{issueNumber}\n\n```text\n{name}\ncollected output\n```",
        _ => throw new ArgumentOutOfRangeException(nameof(location), location, "Unsupported fence-name location."),
    };

    private static string RunResultSummary(CliContext context, string issueBody, string prBody, string format)
    {
        WorkerResultSummaryCommand.IssueLookupFactory = () => new StubIssueLookup(issueBody);
        using var writer = new StringWriter();
        var exit = WorkerResultSummaryCommand.Execute(context,
            ["--kind", "issue-to-pr", "--repo", Repo, "--issue", "1833", "--pr", "1827",
             "--outcome", "pr-created", "--pr-body", prBody, "--format", format], writer);
        Assert.Equal(0, exit);
        return writer.ToString();
    }

    private static (int ExitCode, string Output) RunWorkerComplete(
        CliContext context,
        string issueBody,
        string prBody,
        bool acceptGap = false)
    {
        WorkerCompleteCommand.IssueLookupFactory = () => new StubIssueLookup(issueBody);
        WorkerCompleteCommand.PrLookupFactory = () => new StubPrLookup(prBody);
        WorkerCompleteCommand.MutatorFactory = () => new RecordingMutator();
        var args = new List<string>
        {
            "--repo", Repo, "--kind", "issue", "--number", "1833", "--outcome", "pr-created",
            "--pr", "1827", "--github-only",
        };
        if (acceptGap)
        {
            args.Add("--accept-evidence-gap");
            args.Add("G843 test records intentionally incomplete evidence.");
        }
        args.Add("--write");
        args.Add("--format");
        args.Add("json");

        using var writer = new StringWriter();
        var exit = WorkerCompleteCommand.Execute(context, args.ToArray(), writer);
        return (exit, writer.ToString());
    }

    private const string Repo = "J-Tech-Japan/intent-system";

    private sealed class StubIssueLookup(string body) : IGitHubIssueLookup
    {
        public GitHubIssueLookupResult Lookup(string repo, int issueNumber) => new()
        {
            Number = issueNumber,
            State = "OPEN",
            Title = "G843 fixture",
            Body = body,
        };
    }

    private sealed class StubPrLookup(string body) : IGitHubPrLookup
    {
        public GitHubPrLookupResult Lookup(string repo, int prNumber) => new()
        {
            Number = prNumber,
            State = "OPEN",
            Title = "G843 fixture PR",
            Body = body,
            ClosingIssuesReferences = Array.Empty<GitHubPrClosingIssueReference>(),
        };
    }

    private sealed class RecordingMutator : IGitHubLabelMutator
    {
        public IReadOnlyList<GitHubAutomationLabel> ReadLabels(string repo, string kind, int number) =>
        [new GitHubAutomationLabel { Name = "intent-target" },
         new GitHubAutomationLabel { Name = "intent-issue-in-progress" }];

        public void ApplyLabelTransitions(string repo, string kind, int number,
            IReadOnlyCollection<string> addLabels, IReadOnlyCollection<string> removeLabels) { }

        public void ApplyReconcileTransitions(string repo, string kind, int number,
            IReadOnlyCollection<string> addLabels, IReadOnlyCollection<string> removeLabels) =>
            throw new NotSupportedException();
    }

    private sealed class EvidenceWorkspace : IDisposable
    {
        public EvidenceWorkspace()
        {
            var root = Directory.CreateTempSubdirectory("worker-evidence-paste-g843-").FullName;
            Context = new CliContext
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

        public CliContext Context { get; }

        public void Dispose()
        {
            if (Directory.Exists(Context.RepoRoot))
            {
                Directory.Delete(Context.RepoRoot, recursive: true);
            }
        }
    }
}
