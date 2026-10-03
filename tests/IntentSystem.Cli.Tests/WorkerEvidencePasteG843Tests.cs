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

    [Theory]
    [MemberData(nameof(FixtureGNames))]
    public void CollisionRule_UsesLabelBeforeOrdinalThroughBothCommands_G843(
        string name,
        int[] expectedPresent,
        int[] expectedGap)
    {
        using var workspace = new EvidenceWorkspace();
        var prBody = NamedBody(1833, "heading", name);

        var summary = JsonSerializer.Deserialize<WorkerResultSummaryResult>(
            RunResultSummary(workspace.Context, FixtureGIssue, prBody, "json"))!;
        Assert.Equal(expectedPresent, summary.EvidenceBlocksPresent.Select(item => item.Ordinal));
        Assert.Equal(expectedGap, summary.EvidenceGap.Select(item => item.Ordinal));

        var (exit, output) = RunWorkerComplete(workspace.Context, FixtureGIssue, prBody, acceptGap: true);
        Assert.Equal(0, exit);
        var completion = JsonSerializer.Deserialize<WorkerCompleteResult>(output)!;
        Assert.Equal(expectedPresent, completion.EvidenceBlocksPresent.Select(item => item.Ordinal));
        Assert.Equal(expectedGap, completion.EvidenceGap.Select(item => item.Ordinal));
    }

    public static IEnumerable<object[]> FixtureGNames()
    {
        foreach (var name in new[] { "AC4", "AC 4", "AC #4" })
        {
            yield return [name, new[] { 6 }, new[] { 4 }];
        }
        foreach (var name in new[] { "AC2", "AC 2" })
        {
            yield return [name, new[] { 4 }, new[] { 6 }];
        }
        yield return ["Criterion 4", new[] { 4 }, new[] { 6 }];
        yield return ["Criterion 6", new[] { 6 }, new[] { 4 }];
    }

    [Fact]
    public void CollisionRule_Ac4RefusalNamesOnlyLabelledCriterion_G843()
    {
        using var workspace = new EvidenceWorkspace();
        var (exit, output) = RunWorkerComplete(
            workspace.Context,
            FixtureGIssue,
            NamedBody(1833, "heading", "AC4"));

        Assert.Equal(1, exit);
        Assert.Contains("AC2 (Criterion 4): " + FixtureGCriterion2, output, StringComparison.Ordinal);
        Assert.DoesNotContain("Criterion 6:", output, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(FixtureHNames))]
    public void NonRequiredLabel_DoesNotCaptureAnOrdinalThroughBothCommands_G843(
        string name,
        int[] expectedPresent,
        int[] expectedGap)
    {
        using var workspace = new EvidenceWorkspace();
        var prBody = NamedBody(1833, "heading", name);

        var summary = JsonSerializer.Deserialize<WorkerResultSummaryResult>(
            RunResultSummary(workspace.Context, FixtureHIssue, prBody, "json"))!;
        Assert.Equal(expectedPresent, summary.EvidenceBlocksPresent.Select(item => item.Ordinal));
        Assert.Equal(expectedGap, summary.EvidenceGap.Select(item => item.Ordinal));

        var (exit, output) = RunWorkerComplete(workspace.Context, FixtureHIssue, prBody, acceptGap: true);
        Assert.Equal(0, exit);
        var completion = JsonSerializer.Deserialize<WorkerCompleteResult>(output)!;
        Assert.Equal(expectedPresent, completion.EvidenceBlocksPresent.Select(item => item.Ordinal));
        Assert.Equal(expectedGap, completion.EvidenceGap.Select(item => item.Ordinal));
    }

    public static IEnumerable<object[]> FixtureHNames()
    {
        yield return ["AC 2", Array.Empty<int>(), new[] { 2 }];
        yield return ["AC2", Array.Empty<int>(), new[] { 2 }];
        yield return ["Criterion 2", new[] { 2 }, Array.Empty<int>()];
    }

    [Theory]
    [MemberData(nameof(UnlabelledGlueNames))]
    public void GlueBoundary_AppliesOnlyToAcReferences_G843(string location, string name, bool expectedPresent)
    {
        using var workspace = new EvidenceWorkspace();
        var prBody = NamedBody(1833, location, name);

        var summary = JsonSerializer.Deserialize<WorkerResultSummaryResult>(
            RunResultSummary(workspace.Context, UnlabelledOrdinalSevenIssue, prBody, "json"))!;
        Assert.Equal(expectedPresent ? new[] { 7 } : Array.Empty<int>(),
            summary.EvidenceBlocksPresent.Select(item => item.Ordinal));
        Assert.Equal(expectedPresent ? Array.Empty<int>() : new[] { 7 },
            summary.EvidenceGap.Select(item => item.Ordinal));

        var (exit, output) = RunWorkerComplete(
            workspace.Context, UnlabelledOrdinalSevenIssue, prBody, acceptGap: true);
        Assert.Equal(0, exit);
        var completion = JsonSerializer.Deserialize<WorkerCompleteResult>(output)!;
        Assert.Equal(expectedPresent ? new[] { 7 } : Array.Empty<int>(),
            completion.EvidenceBlocksPresent.Select(item => item.Ordinal));
        Assert.Equal(expectedPresent ? Array.Empty<int>() : new[] { 7 },
            completion.EvidenceGap.Select(item => item.Ordinal));
    }

    public static IEnumerable<object[]> UnlabelledGlueNames()
    {
        yield return ["heading", "AC7", true];
        yield return ["heading", "AC 7", true];
        yield return ["heading", "Criterion7", false];
        yield return ["heading", "Criteria7", false];
        yield return ["heading", "TRAC7", false];
        yield return ["heading", "AC7a", false];
        yield return ["heading", "AC70", false];
        yield return ["fence first line", "-ac7 ok", true];
    }

    [Fact]
    public void RefusalAndSummary_UseLabelFirstTextAndPreserveLegacyG785Output_G843()
    {
        using var workspace = new EvidenceWorkspace();
        var emptyEvidenceBody = "Closes #1833\n\n";

        var (labelledExit, labelledRefusal) = RunWorkerComplete(workspace.Context, FixtureFIssue, emptyEvidenceBody);
        Assert.Equal(1, labelledExit);
        var labelledItem = "AC2 (Criterion 4): " + FixtureFCriterionText;
        Assert.Contains(labelledItem, labelledRefusal, StringComparison.Ordinal);
        Assert.Contains(
            "- Repair: paste the collected output in a fenced block whose heading or first line contains the label printed first for each missing criterion (`AC<n>` or `Criterion <n>`), or re-run with `--accept-evidence-gap <recorded reason>`.\n",
            labelledRefusal,
            StringComparison.Ordinal);

        var summaryJson = RunResultSummary(workspace.Context, FixtureFIssue, emptyEvidenceBody, "json");
        var summary = JsonSerializer.Deserialize<WorkerResultSummaryResult>(summaryJson)!;
        Assert.Contains(summary.Warnings, warning => warning.Contains(labelledItem, StringComparison.Ordinal));
        var summaryMarkdown = RunResultSummary(workspace.Context, FixtureFIssue, emptyEvidenceBody, "text");
        Assert.Contains(labelledItem, summaryMarkdown, StringComparison.Ordinal);

        const string unlabelledIssue = "# G785 fixture\n\n## Acceptance Criteria\n\n"
            + "- Criterion one — actual output pasted.\n"
            + "- Criterion two — actual counts pasted.\n"
            + "- Criterion three — actual output pasted.\n";
        var (legacyExit, legacyRefusal) = RunWorkerComplete(workspace.Context, unlabelledIssue, emptyEvidenceBody);
        Assert.Equal(1, legacyExit);
        Assert.Equal(
            "refused to complete issue #1833 as `pr-created`: evidence gap (G785) — "
            + "no named fenced collected-output block for Criterion 1: Criterion one — actual output pasted., "
            + "Criterion 2: Criterion two — actual counts pasted., "
            + "Criterion 3: Criterion three — actual output pasted.." + Environment.NewLine
            + "- Repair: paste the collected output in a fenced block whose heading or first line names each missing Criterion, "
            + "or re-run with `--accept-evidence-gap <recorded reason>`." + Environment.NewLine,
            legacyRefusal);

        Console.WriteLine("G843 AC4 labelled refusal as printed:\n" + labelledRefusal);
        Console.WriteLine("G843 AC4 unlabelled G785 refusal as printed:\n" + legacyRefusal);
    }

    [Fact]
    public void RealG840IssueAndPullRequestPair_MatchesLabelsAndHistoricalOrdinals_G843()
    {
        var issueBody = File.ReadAllText(LocateRepositoryFile(
            "tests", "IntentSystem.Cli.Tests", "Fixtures", "G843", "G840-issue-1825.md"));
        var realPrBody = File.ReadAllText(LocateRepositoryFile(
            "tests", "IntentSystem.Cli.Tests", "Fixtures", "G843", "G840-pr-1827.md"));

        var realPair = WorkerEvidencePasteAnalyzer.Analyze(issueBody, realPrBody);
        Assert.Equal(new[] { 12, 13, 14, 16 }, realPair.EvidenceRequired.Select(item => item.Ordinal));
        Assert.Equal(new string?[] { null, null, "AC8", "AC10" }, realPair.EvidenceRequired.Select(item => item.Label));
        Assert.Equal(new[] { 12, 13, 14, 16 }, realPair.EvidenceBlocksPresent.Select(item => item.Ordinal));
        Assert.Empty(realPair.EvidenceGap);

        var fourNamedBlocks = NamedEvidenceBlocks(1825,
            ("AC8", "repeat output"),
            ("AC10", "suite output"),
            ("Criterion 12", "repetition counts"),
            ("Criterion 13", "wall-clock scan"));
        var allNamed = WorkerEvidencePasteAnalyzer.Analyze(issueBody, fourNamedBlocks);
        Assert.Empty(allNamed.EvidenceGap);

        var twoNamedBlocks = NamedEvidenceBlocks(1825,
            ("AC8", "repeat output"),
            ("AC10", "suite output"));
        var partial = WorkerEvidencePasteAnalyzer.Analyze(issueBody, twoNamedBlocks);
        Assert.Equal(new[] { 12, 13 }, partial.EvidenceGap.Select(item => item.Ordinal));
        Assert.StartsWith("Criterion 12: ", WorkerEvidenceCriterionFormatter.Format(partial.EvidenceGap), StringComparison.Ordinal);
        Assert.Contains(", Criterion 13: ", WorkerEvidenceCriterionFormatter.Format(partial.EvidenceGap), StringComparison.Ordinal);
    }

    [Fact]
    public void Docs_DescribeLabelsRuleLGlueAndRenderedItem_G843()
    {
        foreach (var path in new[]
                 {
                     LocateRepositoryFile("docs", "en", "08-command-reference.md"),
                     LocateRepositoryFile("docs", "ja", "08-command-reference.md"),
                 })
        {
            var docs = File.ReadAllText(path);
            Assert.Contains("AC<n>", docs, StringComparison.Ordinal);
            Assert.Contains("(Criterion <ordinal>)", docs, StringComparison.Ordinal);
            Assert.Contains("AC <ordinal>", docs, StringComparison.Ordinal);
            Assert.Contains("Criterion <ordinal>", docs, StringComparison.Ordinal);
            Assert.Contains("Criteria <ordinal>", docs, StringComparison.Ordinal);
            Assert.Contains("Rule L", docs, StringComparison.Ordinal);
        }
    }

    [Theory]
    [MemberData(nameof(C66G785ArrayCases))]
    public void Compatibility_C66G785RawEvidenceArrayHashesStayIdentical_G843(
        string fixture,
        string issueBody,
        string prBody,
        string requiredHash,
        string presentHash,
        string gapHash)
    {
        using var workspace = new EvidenceWorkspace();
        var childPrBody = prBody.Replace("Closes #785", "Closes #1833", StringComparison.Ordinal);

        var summaryJson = RunResultSummary(workspace.Context, issueBody, childPrBody, "json");
        AssertEvidenceArrayHashes(summaryJson, requiredHash, presentHash, gapHash, "result-summary", fixture);

        var (completeExit, completeJson) = RunWorkerComplete(
            workspace.Context, issueBody, childPrBody, acceptGap: true);
        Assert.Equal(0, completeExit);
        AssertEvidenceArrayHashes(completeJson, requiredHash, presentHash, gapHash, "worker-complete", fixture);
    }

    public static IEnumerable<object[]> C66G785ArrayCases()
    {
        const string allRequired = "5e15034819a358a77f6ac62cc6ea95972a17b395a3be95105efdbfcce1f3cb68";
        const string emptyArray = "4f53cda18c2baa0c0354bb5f9a3ecbe5ed12ab4d8e11ba873c2f11161202b945";
        yield return ["complete", LegacyPasteIssue, LegacyCompleteBody, allRequired, allRequired, emptyArray];
        yield return ["partial", LegacyPasteIssue, LegacyPartialBody,
            allRequired,
            "079ed62d278ea35a1e75dac5786e8ce089c3dd772a489f8fd1abff542f0cef65",
            "8b5238b76298be09f60ad6bc57190ceb6ae5168d7bd4e9b1f53231290a11f72f"];
        yield return ["aggregate", LegacyPasteIssue, LegacyAggregateBody, allRequired, emptyArray, allRequired];
        yield return ["phrase", LegacyPhraseIssue, LegacyPhraseBody,
            "0b1871ec8bbc183c4c43b773d7dd88c72376a53186aed29ea6089062450a53f9",
            "0b1871ec8bbc183c4c43b773d7dd88c72376a53186aed29ea6089062450a53f9",
            emptyArray];
        yield return ["empty", LegacyNoPasteIssue, "Closes #785", emptyArray, emptyArray, emptyArray];
    }

    private static void AssertEvidenceArrayHashes(
        string json,
        string requiredHash,
        string presentHash,
        string gapHash,
        string command,
        string fixture)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        Assert.True(string.Equals(requiredHash, Sha256RawJson(root.GetProperty("evidence_required")), StringComparison.Ordinal),
            $"{command} {fixture} evidence_required hash differs from c66f493.");
        Assert.True(string.Equals(presentHash, Sha256RawJson(root.GetProperty("evidence_blocks_present")), StringComparison.Ordinal),
            $"{command} {fixture} evidence_blocks_present hash differs from c66f493.");
        Assert.True(string.Equals(gapHash, Sha256RawJson(root.GetProperty("evidence_gap")), StringComparison.Ordinal),
            $"{command} {fixture} evidence_gap hash differs from c66f493.");
    }

    private static string Sha256RawJson(JsonElement element) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(element.GetRawText()))).ToLowerInvariant();

    public static IEnumerable<object[]> C66ObservedStateRows()
    {
        yield return ["heading", "AC2", Array.Empty<int>(), new[] { 4 }, true];
        yield return ["heading", "AC 2", Array.Empty<int>(), new[] { 4 }, true];
        yield return ["heading", "AC #2", Array.Empty<int>(), new[] { 4 }, true];
        yield return ["heading", "AC2:", Array.Empty<int>(), new[] { 4 }, true];
        yield return ["heading", "AC2 — run", Array.Empty<int>(), new[] { 4 }, true];
        yield return ["heading", "**AC2**", Array.Empty<int>(), new[] { 4 }, true];
        yield return ["heading", "**AC2 — run**", Array.Empty<int>(), new[] { 4 }, true];
        yield return ["heading", "ac2", Array.Empty<int>(), new[] { 4 }, true];
        yield return ["heading", "AC-2", Array.Empty<int>(), Array.Empty<int>(), false];
        yield return ["heading", "Criterion2", Array.Empty<int>(), Array.Empty<int>(), false];
        yield return ["heading", "Criterion 2", Array.Empty<int>(), Array.Empty<int>(), false];
        yield return ["heading", "AC4", Array.Empty<int>(), new[] { 4 }, true];
        yield return ["heading", "AC 4", new[] { 4 }, new[] { 4 }, false];
        yield return ["heading", "AC #4", new[] { 4 }, new[] { 4 }, false];
        yield return ["heading", "Criterion 4", new[] { 4 }, new[] { 4 }, false];
        yield return ["heading", "Criteria 4", new[] { 4 }, new[] { 4 }, false];
        yield return ["heading", "criterion #4", new[] { 4 }, new[] { 4 }, false];
        yield return ["heading", "**Criterion 4 (AC2)**", new[] { 4 }, new[] { 4 }, false];
        yield return ["heading", "AC 4a", Array.Empty<int>(), Array.Empty<int>(), false];
        yield return ["heading", "AC40", Array.Empty<int>(), Array.Empty<int>(), false];
        yield return ["heading", "TRAC 4", Array.Empty<int>(), Array.Empty<int>(), false];
        yield return ["heading", "Labeled criterion paste the run", new[] { 4 }, new[] { 4 }, false];
        yield return ["fence first line", "AC2", Array.Empty<int>(), new[] { 4 }, true];
        yield return ["fence first line", "AC 4", new[] { 4 }, new[] { 4 }, false];
    }

    [Theory]
    [MemberData(nameof(C66ObservedStateRows))]
    public void Compatibility_CurrentObservedStateRowsMatchPinnedOldAndNewResults_G843(
        string location,
        string name,
        int[] c66Present,
        int[] currentPresent,
        bool changedFromC66)
    {
        var analysis = WorkerEvidencePasteAnalyzer.Analyze(
            FixtureFIssue,
            NamedBody(1833, location, name));

        // These table values are the c66f493 Current Observed State. Rows
        // marked changed assert the section-2 result specified by G843.
        Assert.Equal(changedFromC66, !c66Present.SequenceEqual(currentPresent));
        Assert.Equal(currentPresent, analysis.EvidenceBlocksPresent.Select(item => item.Ordinal));
        Assert.Equal(currentPresent.Length == 0 ? new[] { 4 } : Array.Empty<int>(),
            analysis.EvidenceGap.Select(item => item.Ordinal));
    }

    private static string NamedEvidenceBlocks(int issueNumber, params (string Name, string Output)[] blocks)
    {
        var builder = new StringBuilder($"Closes #{issueNumber}\n\n");
        foreach (var (name, output) in blocks)
        {
            builder.Append("### ").Append(name).Append("\n\n```text\n")
                .Append(output).Append("\n```\n\n");
        }
        return builder.ToString();
    }

    private static string FixtureGIssue => "## Acceptance Criteria\n\n"
        + "- Parent criterion is not required.\n"
        + "  - first nested bullet.\n"
        + "  - second nested bullet.\n"
        + "- **AC2 — Criterion 2.** actual output pasted.\n"
        + "- Ordinary criterion is not required.\n"
        + "- **AC4 — Criterion 4.** actual output pasted.\n";

    private static string FixtureGCriterion2 => "**AC2 — Criterion 2.** actual output pasted.";

    private static string FixtureHIssue => "## Acceptance Criteria\n\n"
        + "- AC2 — this criterion is not required.\n"
        + "- Unlabelled required criterion actual output pasted.\n";

    private const string LegacyPasteIssue = "# G785 fixture\n\n## Acceptance Criteria\n\n"
        + "- Criterion one — actual output pasted.\n"
        + "- Criterion two — actual counts pasted.\n"
        + "- Criterion three — actual output pasted.\n";

    private const string LegacyCompleteBody = "Closes #785\n\n### Criterion 1\n\n```json\n{ \"fixture\": \"first\" }\n```\n\n"
        + "### Criterion 2\n\n```text\n42 passed\n```\n\n"
        + "### Criterion 3\n\n```json\n{ \"fixture\": \"third\" }\n```";

    private const string LegacyPartialBody = "Closes #785\n\n### Criterion 1\n\n```json\n{ \"fixture\": \"first\" }\n```\n\n"
        + "## Aggregate counts\n\n3 checks passed.";

    private const string LegacyAggregateBody = "Closes #785\n\n## Validation\n\nAll three checks passed.";

    private const string LegacyPhraseIssue = "# G785 packet-defined fence-name fixture\n\n## Acceptance Criteria\n\n"
        + "- Retain operational telemetry snapshot persistence for repair evidence — actual output pasted.\n";

    private const string LegacyPhraseBody = "Closes #785\n\n### Operational telemetry snapshot persistence\n\n```text\ncollected output\n```";

    private const string LegacyNoPasteIssue = "# G785 no-paste fixture\n\n## Acceptance Criteria\n\n"
        + "- A regular assertion has no durable transcript requirement.\n";

    private static string UnlabelledOrdinalSevenIssue => "## Acceptance Criteria\n\n"
        + "- first non-required criterion.\n"
        + "  - nested bullet two.\n"
        + "  - nested bullet three.\n"
        + "- fourth non-required criterion.\n"
        + "- fifth non-required criterion.\n"
        + "  - nested bullet six.\n"
        + "- Required ordinal seven actual output pasted.\n";

    private static string FixtureFIssue => "## Acceptance Criteria\n\n"
        + "- AC1 — parent not required.\n"
        + "  - first sub-bullet not required.\n"
        + "  - second sub-bullet not required.\n"
        + "- **AC2 — Labeled criterion.** Paste the run (**actual output pasted**).\n";

    private static string FixtureFCriterionText => "**AC2 — Labeled criterion.** Paste the run (**actual output pasted**).";

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

    private sealed class StubIssueLookup(string body) : IGitHubIssueLookup
    {
        public GitHubIssueLookupResult Lookup(string repo, int issueNumber) => new()
        {
            Number = issueNumber,
            State = "OPEN",
            Title = "Evidence paste fixture",
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
