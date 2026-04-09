using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

[Collection(RunSubmitCommandCollection.Name)]
public sealed class BugReportCommandTests
{
    [Fact]
    public void ExecuteCore_GivenInlineTextWithoutBugId_UsesCurrentDatePlusNormalizedTitle()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        var originalTimestampFactory = BugReportCommand.TimestampFactory;
        BugReportCommand.TimestampFactory = () => new DateTimeOffset(2026, 4, 9, 8, 30, 0, TimeSpan.Zero);

        try
        {
            var result = BugReportCommand.ExecuteCore(
                CreateContext(repoRoot),
                [
                    "auth",
                    "--title", "OAuth callback loop!",
                    "--text", "Observed callback loop after login."
                ]);

            Assert.Equal("BUG-20260409-oauth-callback-loop", result.Artifact.BugId);
            Assert.Equal(".intent-cli/bugs/BUG-20260409-oauth-callback-loop.report.yaml", result.ArtifactPath);
        }
        finally
        {
            BugReportCommand.TimestampFactory = originalTimestampFactory;
        }
    }

    [Fact]
    public void Execute_GivenPreparedProblemStatementAndRefs_WritesBugReportArtifact()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", "prepared", "bug.md"),
            "Observed callback loop after login." + Environment.NewLine + "Affects GitHub provider path.");
        using var writer = new StringWriter();

        var exitCode = BugReportCommand.Execute(
            CreateContext(repoRoot),
            [
                "auth",
                "BUG-123",
                "--title", "OAuth callback loop",
                "--from-file", "prepared/bug.md",
                "--instruction-refs", "ICL.P.PRODUCT_GOAL,intents/rules/provider-interruption-and-retry.md",
                "--affected-intent-refs", "intents/intent-cli/means/auth.md,intents/intent-cli/README.md",
                "--affected-rule-spec-refs", "intents/intent-cli/specs/12-bug-fix-and-intent-repair.md",
                "--clarification-candidates", "Should provider retry reuse callback state token?,Should callback token be invalidated on second pass?",
                "--execution-units", "G25,G77",
                "--issues", "https://github.com/J-Tech-Japan/intent-system/issues/178",
                "--prs", "https://github.com/J-Tech-Japan/intent-system/pull/180",
                "--reviews", "https://github.com/J-Tech-Japan/intent-system/pull/180#issuecomment-1"
            ],
            writer);

        Assert.Equal(0, exitCode);
        Assert.Contains("Bug report artifact generated for domain 'auth'.", writer.ToString(), StringComparison.Ordinal);
        Assert.Contains("Bug ID: BUG-123", writer.ToString(), StringComparison.Ordinal);

        var artifact = BugReportArtifactYaml.Deserialize(
            File.ReadAllText(Path.Combine(repoRoot, ".intent-cli", "bugs", "BUG-123.report.yaml")));
        Assert.Equal("auth", artifact.DomainSlug);
        Assert.Equal("BUG-123", artifact.BugId);
        Assert.Equal("OAuth callback loop", artifact.Title);
        Assert.Equal("from-file", artifact.ReportSource);
        Assert.Equal(
            "Observed callback loop after login." + Environment.NewLine + "Affects GitHub provider path.",
            artifact.ProblemStatement);
        Assert.Equal("Observed callback loop after login.", artifact.SuspectedFailureLocus);
        Assert.Equal(["ICL.P.PRODUCT_GOAL", "intents/rules/provider-interruption-and-retry.md"], artifact.OriginalInstructionRefs);
        Assert.Equal(["intents/intent-cli/means/auth.md", "intents/intent-cli/README.md"], artifact.AffectedIntentRefs);
        Assert.Equal(["intents/intent-cli/specs/12-bug-fix-and-intent-repair.md"], artifact.AffectedRuleSpecRefs);
        Assert.Equal(
            ["Should provider retry reuse callback state token?", "Should callback token be invalidated on second pass?"],
            artifact.ClarificationCandidates);
        Assert.Equal(["G25", "G77"], artifact.LinkedExecutionUnits);
        Assert.Equal(["https://github.com/J-Tech-Japan/intent-system/issues/178"], artifact.LinkedIssueRefs);
        Assert.Equal(["https://github.com/J-Tech-Japan/intent-system/pull/180"], artifact.LinkedPrRefs);
        Assert.Equal(["https://github.com/J-Tech-Japan/intent-system/pull/180#issuecomment-1"], artifact.LinkedReviewRefs);
    }

    [Fact]
    public void Execute_GivenMissingProblemStatementFile_ReturnsExitCodeOne()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        using var writer = new StringWriter();

        var exitCode = BugReportCommand.Execute(
            CreateContext(repoRoot),
            ["auth", "BUG-123", "--title", "OAuth callback loop", "--from-file", "prepared/missing.md"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("Prepared problem statement file was not found", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenMissingRequiredFlags_ReturnsExitCodeOne()
    {
        using var writer = new StringWriter();

        var exitCode = BugReportCommand.Execute(CreateContext("/tmp/intent-system"), ["auth"], writer);

        Assert.Equal(1, exitCode);
        Assert.Contains(
            "requires '<domain> [<bug-id>] --title <text> [--text <text> | --from-file <path>]'",
            writer.ToString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenSuspectedFailureLocusFlag_UsesExplicitValue()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(Path.Combine("repo", "prepared", "bug.md"), "Observed callback loop after login.");
        using var writer = new StringWriter();

        var exitCode = BugReportCommand.Execute(
            CreateContext(repoRoot),
            [
                "auth",
                "BUG-123",
                "--title", "OAuth callback loop",
                "--from-file", "prepared/bug.md",
                "--suspected-failure-locus", "explicit fallback locus"
            ],
            writer);

        Assert.Equal(0, exitCode);

        var artifact = BugReportArtifactYaml.Deserialize(
            File.ReadAllText(Path.Combine(repoRoot, ".intent-cli", "bugs", "BUG-123.report.yaml")));
        Assert.Equal("explicit fallback locus", artifact.SuspectedFailureLocus);
    }

    [Fact]
    public void ExecuteCore_GivenInlineTextWithoutBugId_SameTitleAndDateStayDeterministicAcrossDifferentText()
    {
        using var firstTempDirectory = new TemporaryDirectory();
        using var secondTempDirectory = new TemporaryDirectory();
        var firstRepoRoot = firstTempDirectory.CreateDirectory("repo");
        var secondRepoRoot = secondTempDirectory.CreateDirectory("repo");
        var originalTimestampFactory = BugReportCommand.TimestampFactory;
        BugReportCommand.TimestampFactory = () => new DateTimeOffset(2026, 4, 9, 8, 30, 0, TimeSpan.Zero);

        try
        {
            var firstResult = BugReportCommand.ExecuteCore(
                CreateContext(firstRepoRoot),
                [
                    "auth",
                    "--title", "OAuth callback loop",
                    "--text", "Observed callback loop after login." + Environment.NewLine + "Affects GitHub provider path."
                ]);
            var secondResult = BugReportCommand.ExecuteCore(
                CreateContext(secondRepoRoot),
                [
                    "auth",
                    "--title", "OAuth callback loop",
                    "--text", "Observed callback loop after login." + Environment.NewLine + "Affects second-pass callback path."
                ]);

            Assert.Equal("BUG-20260409-oauth-callback-loop", firstResult.Artifact.BugId);
            Assert.Equal(firstResult.Artifact.BugId, secondResult.Artifact.BugId);
            Assert.Equal(firstResult.ArtifactPath, secondResult.ArtifactPath);
            Assert.True(
                File.Exists(
                    Path.Combine(firstRepoRoot, ".intent-cli", "bugs", $"{firstResult.Artifact.BugId}.report.yaml")));
        }
        finally
        {
            BugReportCommand.TimestampFactory = originalTimestampFactory;
        }
    }

    [Fact]
    public void ExecuteCore_GivenTextAndFileInput_ConvergeToSameArtifactShapeApartFromReportSource()
    {
        using var fileTempDirectory = new TemporaryDirectory();
        using var textTempDirectory = new TemporaryDirectory();
        var fileRepoRoot = fileTempDirectory.CreateDirectory("repo");
        var textRepoRoot = textTempDirectory.CreateDirectory("repo");
        fileTempDirectory.CreateFile(
            Path.Combine("repo", "prepared", "bug.md"),
            "Observed callback loop after login." + Environment.NewLine + "Affects GitHub provider path.");
        var originalTimestampFactory = BugReportCommand.TimestampFactory;
        BugReportCommand.TimestampFactory = () => new DateTimeOffset(2026, 4, 9, 8, 30, 0, TimeSpan.Zero);

        try
        {
            var fileResult = BugReportCommand.ExecuteCore(
                CreateContext(fileRepoRoot),
                [
                    "auth",
                    "--title", "OAuth callback loop",
                    "--from-file", "prepared/bug.md",
                    "--instruction-refs", "ICL.P.PRODUCT_GOAL",
                    "--affected-intent-refs", "intents/intent-cli/means/auth.md",
                    "--affected-rule-spec-refs", "intents/intent-cli/specs/12-bug-fix-and-intent-repair.md",
                    "--clarification-candidates", "Should provider retry reuse callback state token?",
                    "--execution-units", "G25",
                    "--issues", "https://github.com/J-Tech-Japan/intent-system/issues/178",
                    "--prs", "https://github.com/J-Tech-Japan/intent-system/pull/180",
                    "--reviews", "https://github.com/J-Tech-Japan/intent-system/pull/180#issuecomment-1"
                ]);
            var textResult = BugReportCommand.ExecuteCore(
                CreateContext(textRepoRoot),
                [
                    "auth",
                    "--title", "OAuth callback loop",
                    "--text", "Observed callback loop after login." + Environment.NewLine + "Affects GitHub provider path.",
                    "--instruction-refs", "ICL.P.PRODUCT_GOAL",
                    "--affected-intent-refs", "intents/intent-cli/means/auth.md",
                    "--affected-rule-spec-refs", "intents/intent-cli/specs/12-bug-fix-and-intent-repair.md",
                    "--clarification-candidates", "Should provider retry reuse callback state token?",
                    "--execution-units", "G25",
                    "--issues", "https://github.com/J-Tech-Japan/intent-system/issues/178",
                    "--prs", "https://github.com/J-Tech-Japan/intent-system/pull/180",
                    "--reviews", "https://github.com/J-Tech-Japan/intent-system/pull/180#issuecomment-1"
                ]);

            Assert.Equal(fileResult.Artifact.DomainSlug, textResult.Artifact.DomainSlug);
            Assert.Equal(fileResult.Artifact.BugId, textResult.Artifact.BugId);
            Assert.Equal(fileResult.Artifact.Title, textResult.Artifact.Title);
            Assert.Equal(fileResult.Artifact.ProblemStatement, textResult.Artifact.ProblemStatement);
            Assert.Equal(fileResult.Artifact.SuspectedFailureLocus, textResult.Artifact.SuspectedFailureLocus);
            Assert.Equal(fileResult.Artifact.OriginalInstructionRefs, textResult.Artifact.OriginalInstructionRefs);
            Assert.Equal(fileResult.Artifact.AffectedIntentRefs, textResult.Artifact.AffectedIntentRefs);
            Assert.Equal(fileResult.Artifact.AffectedRuleSpecRefs, textResult.Artifact.AffectedRuleSpecRefs);
            Assert.Equal(fileResult.Artifact.ClarificationCandidates, textResult.Artifact.ClarificationCandidates);
            Assert.Equal(fileResult.Artifact.LinkedExecutionUnits, textResult.Artifact.LinkedExecutionUnits);
            Assert.Equal(fileResult.Artifact.LinkedIssueRefs, textResult.Artifact.LinkedIssueRefs);
            Assert.Equal(fileResult.Artifact.LinkedPrRefs, textResult.Artifact.LinkedPrRefs);
            Assert.Equal(fileResult.Artifact.LinkedReviewRefs, textResult.Artifact.LinkedReviewRefs);
            Assert.Equal("from-file", fileResult.Artifact.ReportSource);
            Assert.Equal("inline-text", textResult.Artifact.ReportSource);
        }
        finally
        {
            BugReportCommand.TimestampFactory = originalTimestampFactory;
        }
    }

    [Fact]
    public void ExecuteCore_GivenInlineTextWithoutBugId_GeneratesDeterministicBugId()
    {
        using var firstTempDirectory = new TemporaryDirectory();
        using var secondTempDirectory = new TemporaryDirectory();
        var firstRepoRoot = firstTempDirectory.CreateDirectory("repo");
        var secondRepoRoot = secondTempDirectory.CreateDirectory("repo");
        var originalTimestampFactory = BugReportCommand.TimestampFactory;
        BugReportCommand.TimestampFactory = () => new DateTimeOffset(2026, 4, 9, 8, 30, 0, TimeSpan.Zero);

        try
        {
            string[] args =
            [
                "auth",
                "--title", "OAuth callback loop",
                "--text", "Observed callback loop after login." + Environment.NewLine + "Affects GitHub provider path."
            ];

            var firstResult = BugReportCommand.ExecuteCore(CreateContext(firstRepoRoot), args);
            var secondResult = BugReportCommand.ExecuteCore(CreateContext(secondRepoRoot), args);

            Assert.Equal("BUG-20260409-oauth-callback-loop", firstResult.Artifact.BugId);
            Assert.Equal(firstResult.Artifact.BugId, secondResult.Artifact.BugId);
            Assert.Equal(firstResult.ArtifactPath, secondResult.ArtifactPath);
            Assert.Equal("inline-text", firstResult.Artifact.ReportSource);
            Assert.Equal(
                "Observed callback loop after login." + Environment.NewLine + "Affects GitHub provider path.",
                firstResult.Artifact.ProblemStatement);
            Assert.True(
                File.Exists(
                    Path.Combine(firstRepoRoot, ".intent-cli", "bugs", $"{firstResult.Artifact.BugId}.report.yaml")));
        }
        finally
        {
            BugReportCommand.TimestampFactory = originalTimestampFactory;
        }
    }

    [Fact]
    public void Execute_GivenInlineTextAndPreparedFileTogether_ReturnsExitCodeOne()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(Path.Combine("repo", "prepared", "bug.md"), "Observed callback loop after login.");
        using var writer = new StringWriter();

        var exitCode = BugReportCommand.Execute(
            CreateContext(repoRoot),
            [
                "auth",
                "--title", "OAuth callback loop",
                "--text", "Observed callback loop after login.",
                "--from-file", "prepared/bug.md"
            ],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains(
            "requires exactly one of '--text <text>' or '--from-file <path>'",
            writer.ToString(),
            StringComparison.Ordinal);
    }

    private static CliContext CreateContext(string repoRoot)
    {
        return new CliContext
        {
            RepoRoot = repoRoot,
            Config = new CliConfig
            {
                Project = new ProjectConfig
                {
                    Domain = "intent-system",
                    WorkflowEngine = "intent-cli",
                    ArtifactRoot = ".intent-cli"
                }
            }
        };
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        private readonly string rootPath = Directory.CreateTempSubdirectory("intent-cli-bug-report-tests-").FullName;

        public string CreateDirectory(string relativePath)
        {
            var fullPath = Path.Combine(rootPath, relativePath);
            Directory.CreateDirectory(fullPath);
            return fullPath;
        }

        public string CreateFile(string relativePath, string contents)
        {
            var fullPath = Path.Combine(rootPath, relativePath);
            var directoryPath = Path.GetDirectoryName(fullPath)
                ?? throw new InvalidOperationException("Temporary file path did not contain a directory.");

            Directory.CreateDirectory(directoryPath);
            File.WriteAllText(fullPath, contents);
            return fullPath;
        }

        public void Dispose()
        {
            if (Directory.Exists(rootPath))
            {
                Directory.Delete(rootPath, recursive: true);
            }
        }
    }
}
