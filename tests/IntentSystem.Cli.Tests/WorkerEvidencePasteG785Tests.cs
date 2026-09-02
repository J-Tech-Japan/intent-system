using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G785 durable fixtures for the bounded pasted-evidence contract. The tests
/// deliberately exercise only child GitHub inputs: source issue body plus PR
/// body. They never seed a host packet or queue as evidence.
/// </summary>
[Collection("WorkerNextActionSharedState")]
public sealed class WorkerEvidencePasteG785Tests : IDisposable
{
    public WorkerEvidencePasteG785Tests()
    {
        WorkerResultSummaryCommand.IssueLookupFactory = null;
        WorkerCompleteCommand.MutatorFactory = null;
        WorkerCompleteCommand.PrLookupFactory = null;
        WorkerCompleteCommand.IssueLookupFactory = null;
    }

    public void Dispose()
    {
        WorkerResultSummaryCommand.IssueLookupFactory = null;
        WorkerCompleteCommand.MutatorFactory = null;
        WorkerCompleteCommand.PrLookupFactory = null;
        WorkerCompleteCommand.IssueLookupFactory = null;
    }

    [Fact]
    public void Guides_RenderEvidencePasteRuleAndPreserveParentPayloads_G785()
    {
        var issueJson = RenderGuide(
            (context, writer) => GuideWorkerIssueToPrCommand.Execute(
                context,
                ["--repo", Repo, "--domain", "intent-cli", "--format", "json"],
                writer));
        var issueMarkdown = RenderGuide(
            (context, writer) => GuideWorkerIssueToPrCommand.Execute(
                context,
                ["--repo", Repo, "--domain", "intent-cli", "--format", "markdown"],
                writer));
        var repairJson = RenderGuide(
            (context, writer) => GuideWorkerPrCommentFixCommand.Execute(
                context,
                ["--repo", Repo, "--domain", "intent-cli", "--format", "json"],
                writer));
        var repairMarkdown = RenderGuide(
            (context, writer) => GuideWorkerPrCommentFixCommand.Execute(
                context,
                ["--repo", Repo, "--domain", "intent-cli", "--format", "markdown"],
                writer));

        foreach (var json in new[] { issueJson, repairJson })
        {
            using var document = JsonDocument.Parse(json);
            Assert.Equal(
                WorkerEvidencePasteRule.Text,
                document.RootElement.GetProperty("evidence_paste_rule").GetString());
        }

        foreach (var markdown in new[] { issueMarkdown, repairMarkdown })
        {
            Assert.Contains("## Evidence-paste rule (G785)", markdown, StringComparison.Ordinal);
            Assert.Contains("actual output pasted", markdown, StringComparison.Ordinal);
            Assert.Contains("actual counts pasted", markdown, StringComparison.Ordinal);
            Assert.Contains("paraphrased or expected values", markdown, StringComparison.Ordinal);
        }

        // The four parent payloads are pinned byte-for-byte after removing
        // only the new rendered rule. This prevents an evidence-guide change
        // from smuggling unrelated prompt or workflow edits into G785.
        Assert.Equal(
            "a4b127234b70964de16278fc333a3a507121f2c9aaa1e3019167d3647d8785b2",
            Sha256(RemoveEvidenceRuleFromJson(issueJson)));
        Assert.Equal(
            "9e053b805cab22020fa59ee024e8bbab83a5b179d8dd19d002a6d53717dc3e25",
            Sha256(RemoveEvidenceRuleFromMarkdown(issueMarkdown)));
        Assert.Equal(
            "5d8aec25a1111a1fcdaaee52ed8f04a9d6debc9f68900b4f3fbbf222c64cfbc8",
            Sha256(RemoveEvidenceRuleFromJson(repairJson)));
        Assert.Equal(
            "7e5c9a4e9d09a68c4f18a5859b33edc82c00a84622de2cf304a9226fe78ccf1b",
            Sha256(RemoveEvidenceRuleFromMarkdown(repairMarkdown)));
    }

    [Fact]
    public void ResultSummary_MeasuresNamedBlocksAndLeavesAggregateCountsAsGap_G785()
    {
        using var workspace = new EvidenceWorkspace();
        WorkerResultSummaryCommand.IssueLookupFactory = () => new StubIssueLookup(PasteCriteriaIssue);

        var complete = ExecuteResultSummary(workspace.Context, CompleteEvidencePrBody);
        Assert.Collection(
            complete.EvidenceRequired,
            criterion => Assert.Equal((1, "Criterion one — actual output pasted."), (criterion.Ordinal, criterion.Text)),
            criterion => Assert.Equal((2, "Criterion two — actual counts pasted."), (criterion.Ordinal, criterion.Text)),
            criterion => Assert.Equal((3, "Criterion three — actual output pasted."), (criterion.Ordinal, criterion.Text)));
        Assert.Equal(new[] { 1, 2, 3 }, complete.EvidenceBlocksPresent.Select(criterion => criterion.Ordinal));
        Assert.Empty(complete.EvidenceGap);

        var oneBlock = ExecuteResultSummary(workspace.Context, OneNamedEvidencePrBody);
        Assert.Equal(new[] { 1 }, oneBlock.EvidenceBlocksPresent.Select(criterion => criterion.Ordinal));
        Assert.Equal(new[] { 2, 3 }, oneBlock.EvidenceGap.Select(criterion => criterion.Ordinal));

        var bodyFile = Path.Combine(workspace.RootPath, "g785-one-named-block.md");
        File.WriteAllText(bodyFile, OneNamedEvidencePrBody);
        using (var fileWriter = new StringWriter())
        {
            var fileExit = WorkerResultSummaryCommand.Execute(
                workspace.Context,
                new[]
                {
                    "--kind", "issue-to-pr",
                    "--repo", Repo,
                    "--issue", "785",
                    "--pr", "786",
                    "--outcome", "pr-created",
                    "--pr-body-file", bodyFile,
                    "--format", "json",
                },
                fileWriter);
            Assert.Equal(0, fileExit);
            var fromFile = JsonSerializer.Deserialize<WorkerResultSummaryResult>(fileWriter.ToString())!;
            Assert.Equal(new[] { 1 }, fromFile.EvidenceBlocksPresent.Select(criterion => criterion.Ordinal));
            Assert.Equal(new[] { 2, 3 }, fromFile.EvidenceGap.Select(criterion => criterion.Ordinal));
        }

        var aggregateOnly = ExecuteResultSummary(workspace.Context, AggregateCountsOnlyPrBody);
        Assert.Empty(aggregateOnly.EvidenceBlocksPresent);
        Assert.Equal(new[] { 1, 2, 3 }, aggregateOnly.EvidenceGap.Select(criterion => criterion.Ordinal));
        Assert.Contains(
            aggregateOnly.Warnings,
            warning => warning.Contains("evidence-paste (G785)", StringComparison.Ordinal)
                && warning.Contains("Criterion 3", StringComparison.Ordinal));
    }

    [Fact]
    public void WorkerComplete_RefusesGapRecordsReasonedOverrideAndRejectsBlankReason_G785()
    {
        using var workspace = new EvidenceWorkspace();
        var mutator = new RecordingMutator();
        WorkerCompleteCommand.MutatorFactory = () => mutator;
        WorkerCompleteCommand.PrLookupFactory = () => new StubPrLookup(AggregateCountsOnlyPrBody);
        WorkerCompleteCommand.IssueLookupFactory = () => new StubIssueLookup(PasteCriteriaIssue);

        using var refusalWriter = new StringWriter();
        var refusalExit = WorkerCompleteCommand.Execute(
            workspace.Context,
            CompletionArgs(),
            refusalWriter);

        Assert.Equal(1, refusalExit);
        Assert.Empty(mutator.Transitions);
        Console.WriteLine("G785 worker-complete aggregate-only refusal:\n" + refusalWriter);
        Assert.Contains("evidence gap (G785)", refusalWriter.ToString(), StringComparison.Ordinal);
        Assert.Contains("Criterion 1", refusalWriter.ToString(), StringComparison.Ordinal);
        Assert.Contains("Criterion 3", refusalWriter.ToString(), StringComparison.Ordinal);

        using var overrideWriter = new StringWriter();
        var overrideExit = WorkerCompleteCommand.Execute(
            workspace.Context,
            CompletionArgs("operator approved absent CI transcript for recovery drill"),
            overrideWriter);

        Assert.Equal(0, overrideExit);
        Console.WriteLine("G785 worker-complete recorded override:\n" + overrideWriter);
        var accepted = JsonSerializer.Deserialize<WorkerCompleteResult>(overrideWriter.ToString())!;
        Assert.Equal(
            "operator approved absent CI transcript for recovery drill",
            accepted.EvidenceGapAccepted);
        Assert.Equal(new[] { 1, 2, 3 }, accepted.EvidenceGap.Select(criterion => criterion.Ordinal));
        Assert.Empty(accepted.EvidenceBlocksPresent);
        Assert.Equal(2, mutator.Transitions.Count);

        using var blankReasonWriter = new StringWriter();
        var blankReasonExit = WorkerCompleteCommand.Execute(
            workspace.Context,
            CompletionArgs("   "),
            blankReasonWriter);

        Assert.Equal(1, blankReasonExit);
        Assert.Contains("requires a non-empty recorded reason", blankReasonWriter.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Detection_IgnoresVerificationAndUnnamedFences_G785()
    {
        var verificationOnly = WorkerEvidencePasteAnalyzer.Analyze(
            """
            ## Acceptance Criteria

            - The implementation changes one command.

            ## Verification

            - actual output pasted here is not an Acceptance Criteria bullet.
            """,
            """
            ### Criterion 1

            ```text
            collected output
            ```
            """);
        Assert.Empty(verificationOnly.EvidenceRequired);
        Assert.Empty(verificationOnly.EvidenceGap);

        var acceptanceProseOnly = WorkerEvidencePasteAnalyzer.Analyze(
            """
            ## Acceptance Criteria

            actual output pasted in unbulleted prose does not opt in.
            """,
            "### Criterion 1\n```text\ncollected output\n```");
        Assert.Empty(acceptanceProseOnly.EvidenceRequired);

        var unnamedFence = WorkerEvidencePasteAnalyzer.Analyze(
            PasteCriteriaIssue,
            """
            Closes #785

            ### Validation transcript

            ```json
            { "passed": true }
            ```
            """);
        Assert.Equal(new[] { 1, 2, 3 }, unnamedFence.EvidenceGap.Select(criterion => criterion.Ordinal));
        Assert.Empty(unnamedFence.EvidenceBlocksPresent);

        var firstLineNamesCriterion = WorkerEvidencePasteAnalyzer.Analyze(
            PasteCriteriaIssue,
            """
            ```text
            Criterion 1
            collected output
            ```
            """);
        Assert.Equal(new[] { 1 }, firstLineNamesCriterion.EvidenceBlocksPresent.Select(criterion => criterion.Ordinal));
        Assert.Equal(new[] { 2, 3 }, firstLineNamesCriterion.EvidenceGap.Select(criterion => criterion.Ordinal));
    }

    [Fact]
    public void NoPasteIssue_KeepsLegacySummaryShapeAndCompletes_G785()
    {
        using var workspace = new EvidenceWorkspace();
        WorkerResultSummaryCommand.IssueLookupFactory = () => new StubIssueLookup(NoPasteCriteriaIssue);

        var measuredOutput = ExecuteResultSummaryOutput(workspace.Context, "Closes #785\n\n```text\nno criterion name needed\n```");
        var measured = JsonSerializer.Deserialize<WorkerResultSummaryResult>(measuredOutput)!;
        Assert.Empty(measured.EvidenceRequired);
        Assert.Empty(measured.EvidenceBlocksPresent);
        Assert.Empty(measured.EvidenceGap);
        Assert.Equal(
            "2c66c7076afb1c1806052352bff1582e1a26a50431d8c5b396a930b703578cea",
            Sha256(RemoveEvidenceFieldsFromJson(measuredOutput)));

        var mutator = new RecordingMutator();
        WorkerCompleteCommand.MutatorFactory = () => mutator;
        WorkerCompleteCommand.PrLookupFactory = () => new StubPrLookup("Closes #785");
        WorkerCompleteCommand.IssueLookupFactory = () => new StubIssueLookup(NoPasteCriteriaIssue);
        using var completionWriter = new StringWriter();
        var completionExit = WorkerCompleteCommand.Execute(
            workspace.Context,
            CompletionArgs(),
            completionWriter);

        Assert.Equal(0, completionExit);
        var completion = JsonSerializer.Deserialize<WorkerCompleteResult>(completionWriter.ToString())!;
        Assert.Empty(completion.EvidenceRequired);
        Assert.Empty(completion.EvidenceGap);
        Assert.Null(completion.EvidenceGapAccepted);
        Assert.Equal(2, mutator.Transitions.Count);
    }

    [Fact]
    public void GithubOnlyCompletion_UsesOnlyIssueAndPrEvidence_G785()
    {
        using var workspace = new EvidenceWorkspace();
        workspace.WriteQueueStateSentinel();
        var queueBefore = File.ReadAllText(workspace.QueueStatePath);
        var issueLookup = new StubIssueLookup(PasteCriteriaIssue);
        var mutator = new RecordingMutator();
        WorkerCompleteCommand.IssueLookupFactory = () => issueLookup;
        WorkerCompleteCommand.PrLookupFactory = () => new StubPrLookup(CompleteEvidencePrBody);
        WorkerCompleteCommand.MutatorFactory = () => mutator;

        using var writer = new StringWriter();
        var exitCode = WorkerCompleteCommand.Execute(
            workspace.Context,
            CompletionArgs(githubOnly: true),
            writer);

        Assert.Equal(0, exitCode);
        Console.WriteLine("G785 worker-complete all-named-block success:\n" + writer);
        var result = JsonSerializer.Deserialize<WorkerCompleteResult>(writer.ToString())!;
        Assert.True(result.GithubOnly);
        Assert.True(result.ChildCwd);
        Assert.Equal("github-only-no-host-state", result.DomainSource);
        Assert.Equal(new[] { 1, 2, 3 }, result.EvidenceBlocksPresent.Select(criterion => criterion.Ordinal));
        Assert.Equal(2, issueLookup.Calls);
        Assert.Equal(queueBefore, File.ReadAllText(workspace.QueueStatePath));
        Assert.Equal(2, mutator.Transitions.Count);
    }

    [Fact]
    public void DocsAndPacketDraftGuide_DescribeTheBoundedRule_G785()
    {
        var packetGuide = RenderGuide(
            (context, writer) => GuideWorkflowTaskPacketDraftCommand.Execute(
                context,
                ["--format", "json"],
                writer));
        Assert.Contains("actual output pasted", packetGuide, StringComparison.Ordinal);
        Assert.Contains("actual counts pasted", packetGuide, StringComparison.Ordinal);

        foreach (var path in new[]
                 {
                     LocateRepositoryFile("docs", "en", "08-command-reference.md"),
                     LocateRepositoryFile("docs", "ja", "08-command-reference.md"),
                 })
        {
            var docs = File.ReadAllText(path);
            Assert.Contains("evidence_required", docs, StringComparison.Ordinal);
            Assert.Contains("evidence_blocks_present", docs, StringComparison.Ordinal);
            Assert.Contains("evidence_gap", docs, StringComparison.Ordinal);
            Assert.Contains("--accept-evidence-gap", docs, StringComparison.Ordinal);
        }
    }

    private static WorkerResultSummaryResult ExecuteResultSummary(CliContext context, string prBody)
    {
        var output = ExecuteResultSummaryOutput(context, prBody);
        return JsonSerializer.Deserialize<WorkerResultSummaryResult>(output)!;
    }

    private static string ExecuteResultSummaryOutput(CliContext context, string prBody)
    {
        using var writer = new StringWriter();
        var exitCode = WorkerResultSummaryCommand.Execute(
            context,
            new[]
            {
                "--kind", "issue-to-pr",
                "--repo", Repo,
                "--issue", "785",
                "--pr", "786",
                "--outcome", "pr-created",
                "--pr-body", prBody,
                "--format", "json",
            },
            writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Console.WriteLine("G785 worker result-summary fixture:\n" + output);
        return output;
    }

    private static string[] CompletionArgs(string? acceptEvidenceGap = null, bool githubOnly = false)
    {
        var args = new List<string>
        {
            "--repo", Repo,
            "--kind", "issue",
            "--number", "785",
            "--outcome", "pr-created",
            "--pr", "786",
        };
        if (githubOnly)
        {
            args.Add("--github-only");
        }
        if (acceptEvidenceGap is not null)
        {
            args.Add("--accept-evidence-gap");
            args.Add(acceptEvidenceGap);
        }
        args.Add("--write");
        args.Add("--format");
        args.Add("json");
        return args.ToArray();
    }

    private static string RenderGuide(Func<CliContext, StringWriter, int> execute)
    {
        using var workspace = new EvidenceWorkspace();
        using var writer = new StringWriter();
        Assert.Equal(0, execute(workspace.Context, writer));
        return writer.ToString();
    }

    private static string RemoveEvidenceRuleFromJson(string json)
    {
        var line = "  \"evidence_paste_rule\": " + JsonSerializer.Serialize(WorkerEvidencePasteRule.Text) + ",\n";
        Assert.Contains(line, json, StringComparison.Ordinal);
        return json.Replace(line, string.Empty, StringComparison.Ordinal);
    }

    private static string RemoveEvidenceRuleFromMarkdown(string markdown)
    {
        var section = $"## Evidence-paste rule (G785)\n\n{WorkerEvidencePasteRule.Text}\n\n";
        Assert.Contains(section, markdown, StringComparison.Ordinal);
        return markdown.Replace(section, string.Empty, StringComparison.Ordinal);
    }

    private static string RemoveEvidenceFieldsFromJson(string json)
    {
        const string fields = ",\n  \"evidence_required\": [],\n  \"evidence_blocks_present\": [],\n  \"evidence_gap\": []";
        Assert.Contains(fields, json, StringComparison.Ordinal);
        return json.Replace(fields, string.Empty, StringComparison.Ordinal);
    }

    private static string Sha256(string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();

    private static string LocateRepositoryFile(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(new[] { directory.FullName }.Concat(segments).ToArray());
            if (File.Exists(candidate))
            {
                return candidate;
            }
            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not locate {string.Join('/', segments)}.");
    }

    private const string Repo = "J-Tech-Japan/intent-system";

    private const string PasteCriteriaIssue = """
        # G785 fixture

        ## Acceptance Criteria

        - Criterion one — actual output pasted.
        - Criterion two — actual counts pasted.
        - Criterion three — actual output pasted.

        ## Verification

        - Run the fixture.
        """;

    private const string NoPasteCriteriaIssue = """
        # G785 no-paste fixture

        ## Acceptance Criteria

        - A regular assertion has no durable transcript requirement.

        ## Verification

        - actual output pasted here does not count.
        """;

    private const string CompleteEvidencePrBody = """
        Closes #785

        ### Criterion 1

        ```json
        { "fixture": "first" }
        ```

        ### Criterion 2

        ```text
        42 passed
        ```

        ### Criterion 3

        ```json
        { "fixture": "third" }
        ```
        """;

    private const string OneNamedEvidencePrBody = """
        Closes #785

        ### Criterion 1

        ```json
        { "fixture": "first" }
        ```

        ## Aggregate counts

        3 checks passed.
        """;

    private const string AggregateCountsOnlyPrBody = """
        Closes #785

        ## Validation

        All three checks passed.
        """;

    private sealed class StubIssueLookup : IGitHubIssueLookup
    {
        private readonly string body;

        public StubIssueLookup(string body)
        {
            this.body = body;
        }

        public int Calls { get; private set; }

        public GitHubIssueLookupResult Lookup(string repo, int issueNumber)
        {
            Calls++;
            return new GitHubIssueLookupResult
            {
                Number = issueNumber,
                State = "OPEN",
                Title = "G785 fixture",
                Body = body,
            };
        }
    }

    private sealed class StubPrLookup : IGitHubPrLookup
    {
        private readonly string body;

        public StubPrLookup(string body)
        {
            this.body = body;
        }

        public GitHubPrLookupResult Lookup(string repo, int prNumber) => new()
        {
            Number = prNumber,
            State = "OPEN",
            Title = "G785 fixture PR",
            Body = body,
            ClosingIssuesReferences = Array.Empty<GitHubPrClosingIssueReference>(),
        };
    }

    private sealed class RecordingMutator : IGitHubLabelMutator
    {
        public List<(string Kind, int Number, IReadOnlyList<string> Add, IReadOnlyList<string> Remove)> Transitions { get; } = new();

        public IReadOnlyList<GitHubAutomationLabel> ReadLabels(string repo, string kind, int number) =>
            new[]
            {
                new GitHubAutomationLabel { Name = "intent-target" },
                new GitHubAutomationLabel { Name = "intent-issue-in-progress" },
            };

        public void ApplyLabelTransitions(
            string repo,
            string kind,
            int number,
            IReadOnlyCollection<string> addLabels,
            IReadOnlyCollection<string> removeLabels) =>
            Transitions.Add((kind, number, addLabels.ToArray(), removeLabels.ToArray()));

        public void ApplyReconcileTransitions(
            string repo,
            string kind,
            int number,
            IReadOnlyCollection<string> addLabels,
            IReadOnlyCollection<string> removeLabels) =>
            throw new NotSupportedException();
    }

    private sealed class EvidenceWorkspace : IDisposable
    {
        public EvidenceWorkspace()
        {
            RootPath = Directory.CreateTempSubdirectory("worker-evidence-paste-g785-").FullName;
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
        }

        public string RootPath { get; }

        public CliContext Context { get; }

        public string QueueStatePath => Path.Combine(RootPath, ".intent-cli", "queue-state.json");

        public void WriteQueueStateSentinel()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(QueueStatePath)!);
            File.WriteAllText(QueueStatePath, "G785 host sentinel must remain unchanged\n");
        }

        public void Dispose()
        {
            if (Directory.Exists(RootPath))
            {
                Directory.Delete(RootPath, recursive: true);
            }
        }
    }
}
