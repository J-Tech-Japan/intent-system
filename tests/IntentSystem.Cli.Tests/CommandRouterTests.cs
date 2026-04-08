using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;
using IntentSystem.ConceptIntake.Models;
using IntentSystem.Clarify.Models;
using IntentSystem.Clarify.Serialization;
using IntentSystem.Review;
using IntentSystem.Review.Serialization;
using IntentSystem.Supervisor.Models;
using IntentSystem.Supervisor.Serialization;
using IntentSystem.WorkerAdapter.Serialization;

namespace IntentSystem.Cli.Tests;

[Collection(RunSubmitCommandCollection.Name)]
public sealed class CommandRouterTests
{
    [Fact]
    public void Execute_GivenNoArguments_WritesHelpIncludingAllCommandGroups()
    {
        using var writer = new StringWriter();

        var exitCode = CommandRouter.Execute(Array.Empty<string>(), CreateContext("/tmp/intent-system"), writer);

        var output = writer.ToString();
        Assert.Equal(0, exitCode);
        Assert.Contains("project", output, StringComparison.Ordinal);
        Assert.Contains("projection", output, StringComparison.Ordinal);
        Assert.Contains("queue", output, StringComparison.Ordinal);
        Assert.Contains("run", output, StringComparison.Ordinal);
        Assert.Contains("review", output, StringComparison.Ordinal);
        Assert.Contains("interview", output, StringComparison.Ordinal);
        Assert.Contains("clarify", output, StringComparison.Ordinal);
        Assert.Contains("workflow", output, StringComparison.Ordinal);
        Assert.Contains("intake", output, StringComparison.Ordinal);
        Assert.Contains("generate-from-current", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenKnownGroupAndUnknownSubcommand_WritesNotYetImplementedMessage()
    {
        using var writer = new StringWriter();

        var exitCode = CommandRouter.Execute(["projection", "status"], CreateContext("/tmp/intent-system"), writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("not yet implemented", writer.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Execute_GivenProjectStatusCommand_DispatchesToProjectStatusRenderer()
    {
        using var writer = new StringWriter();
        var context = CreateContext("/tmp/intent-system");

        var exitCode = CommandRouter.Execute(["project", "status"], context, writer);

        Assert.Equal(0, exitCode);
        Assert.Contains("intent-cli", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenGenerateFromCurrentCommand_DispatchesToTopLevelRenderer()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(Path.Combine("repo", "README.md"), "# Intent System");
        tempDirectory.CreateFile(Path.Combine("repo", "src", "feature", "FeatureA.cs"), "namespace FeatureA;");
        using var writer = new StringWriter();
        var originalFactory = GenerateFromCurrentCommand.GitHubCommandRunnerFactory;

        try
        {
            GenerateFromCurrentCommand.GitHubCommandRunnerFactory = () => new FakeGenerateFromCurrentGitHubRunner();

            var exitCode = CommandRouter.Execute(
                ["generate-from-current", "auth", "--from-path", "src/feature", "--issues", "114", "--prs", "113"],
                CreateContext(repoRoot),
                writer);

            Assert.Equal(0, exitCode);
            Assert.Contains("Generate-from-current processed for domain 'auth'.", writer.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            GenerateFromCurrentCommand.GitHubCommandRunnerFactory = originalFactory;
        }
    }

    [Fact]
    public void Execute_GivenGenerateFromCurrentReconstructionCommand_DispatchesToReconstructionRenderer()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(Path.Combine("repo", "README.md"), "# Intent System");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "intake", "auth.current-sources.yaml"),
            CurrentSourcesArtifactYaml.Serialize(
                new CurrentSourcesArtifact
                {
                    DomainSlug = "auth",
                    SourceRoot = "src/feature",
                    SelectedAltitudes = ["execution"],
                    SelectedIssueScope = "none",
                    SelectedPrScope = "none",
                    SelectedPaths = ["src/feature/FeatureA.cs"],
                    SourceRefs = ["code:src/feature/FeatureA.cs"],
                    SamplingNotes = ["code:src/feature/FeatureA.cs summary=namespace FeatureA;"],
                    Gaps = []
                }));
        using var writer = new StringWriter();

        var exitCode = CommandRouter.Execute(["generate-from-current", "auth"], CreateContext(repoRoot), writer);

        Assert.Equal(0, exitCode);
        Assert.Contains("Generate-from-current reconstruction processed for domain 'auth'.", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenGenerateFromCurrentBridgeCommand_DispatchesToBridgeRenderer()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "intake", "auth.reconstructed-concept.yaml"),
            ReconstructedConceptArtifactYaml.Serialize(
                new ReconstructedConceptArtifact
                {
                    DomainSlug = "auth",
                    InitialGoal = "Reconstruct auth domain intent.",
                    CandidateIntentNodes = [],
                    CandidateUserContext = [],
                    CandidateMeans = [],
                    CandidateRules = [],
                    CandidateSpecs = [],
                    CandidateExecutionUnits = [],
                    ConfidenceByAltitude = [],
                    SourceConceptRefs = []
                }));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "intake", "auth.reconstructed-interview.md"),
            GenerateFromCurrentReconstructionRenderer.RenderInterviewMarkdown(
                "auth",
                [],
                [],
                [],
                [],
                [],
                ["Which missing intent detail should be clarified first for domain 'auth'?"],
                [
                    new ReconstructedBridgeQuestion
                    {
                        QuestionId = "iq-1",
                        QuestionText = "Which missing intent detail should be clarified first for domain 'auth'?",
                        Reason = "Clarify root-near intent before standard intake resumes.",
                        Affects = ["auth"],
                        BlockingOrNonblocking = "blocking"
                    }
                ],
                [],
                []));
        using var writer = new StringWriter();

        var exitCode = CommandRouter.Execute(["generate-from-current", "bridge", "auth"], CreateContext(repoRoot), writer);

        Assert.Equal(0, exitCode);
        Assert.Contains("Generate-from-current bridge processed for domain 'auth'.", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenGenerateFromCurrentAdvanceCommand_DispatchesToAdvanceRenderer()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(Path.Combine("repo", "README.md"), "# Intent System");
        tempDirectory.CreateFile(Path.Combine("repo", "src", "feature", "FeatureA.cs"), "namespace FeatureA;");
        using var writer = new StringWriter();
        var originalFactory = GenerateFromCurrentCommand.GitHubCommandRunnerFactory;

        try
        {
            GenerateFromCurrentCommand.GitHubCommandRunnerFactory = () => new FakeGenerateFromCurrentGitHubRunner();

            var exitCode = CommandRouter.Execute(
                ["generate-from-current", "advance", "auth", "--from-path", "src/feature", "--issues", "114", "--prs", "113", "--altitudes", "execution"],
                CreateContext(repoRoot),
                writer);

            Assert.Equal(0, exitCode);
            Assert.Contains("Generate-from-current advance processed for domain 'auth'.", writer.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            GenerateFromCurrentCommand.GitHubCommandRunnerFactory = originalFactory;
        }
    }

    [Fact]
    public void Execute_GivenGenerateFromCurrentSubmitCommand_DispatchesToSubmitRenderer()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(Path.Combine("repo", "README.md"), "# Intent System");
        tempDirectory.CreateFile(Path.Combine("repo", "src", "feature", "FeatureA.cs"), "namespace FeatureA;");
        using var writer = new StringWriter();
        var originalFactory = GenerateFromCurrentCommand.GitHubCommandRunnerFactory;

        try
        {
            GenerateFromCurrentCommand.GitHubCommandRunnerFactory = () => new FakeGenerateFromCurrentGitHubRunner();

            var exitCode = CommandRouter.Execute(
                ["generate-from-current", "submit", "auth", "--from-path", "src/feature", "--issues", "114", "--prs", "113", "--altitudes", "execution"],
                CreateContext(repoRoot),
                writer);

            Assert.Equal(0, exitCode);
            Assert.Contains("Generate-from-current submit processed for domain 'auth'.", writer.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            GenerateFromCurrentCommand.GitHubCommandRunnerFactory = originalFactory;
        }
    }

    [Fact]
    public void Execute_GivenGenerateFromCurrentReviewCommand_DispatchesToReviewRenderer()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(Path.Combine("repo", "README.md"), "# Intent System");
        tempDirectory.CreateFile(Path.Combine("repo", "src", "feature", "FeatureA.cs"), "namespace FeatureA;");
        using var writer = new StringWriter();
        var originalFactory = GenerateFromCurrentCommand.GitHubCommandRunnerFactory;

        try
        {
            GenerateFromCurrentCommand.GitHubCommandRunnerFactory = () => new FakeGenerateFromCurrentGitHubRunner();

            var exitCode = CommandRouter.Execute(
                ["generate-from-current", "review", "auth", "--from-path", "src/feature", "--issues", "114", "--prs", "113", "--altitudes", "execution"],
                CreateContext(repoRoot),
                writer);

            Assert.Equal(0, exitCode);
            Assert.Contains("Generate-from-current review processed for domain 'auth'.", writer.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            GenerateFromCurrentCommand.GitHubCommandRunnerFactory = originalFactory;
        }
    }

    [Fact]
    public void Execute_GivenGenerateFromCurrentAcceptCommand_DispatchesToAcceptRenderer()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(Path.Combine("repo", "README.md"), "# Intent System");
        tempDirectory.CreateFile(Path.Combine("repo", "src", "feature", "FeatureA.cs"), "namespace FeatureA;");
        using var writer = new StringWriter();
        var originalFactory = GenerateFromCurrentCommand.GitHubCommandRunnerFactory;

        try
        {
            GenerateFromCurrentCommand.GitHubCommandRunnerFactory = () => new FakeGenerateFromCurrentGitHubRunner();

            var exitCode = CommandRouter.Execute(
                ["generate-from-current", "accept", "auth", "--from-path", "src/feature", "--issues", "114", "--prs", "113", "--altitudes", "execution"],
                CreateContext(repoRoot),
                writer);

            Assert.Equal(0, exitCode);
            Assert.Contains("Generate-from-current accept processed for domain 'auth'.", writer.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            GenerateFromCurrentCommand.GitHubCommandRunnerFactory = originalFactory;
        }
    }

    [Fact]
    public void Execute_GivenGenerateFromCurrentCommentCommand_DispatchesToCommentRenderer()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(Path.Combine("repo", "README.md"), "# Intent System");
        tempDirectory.CreateFile(Path.Combine("repo", "src", "feature", "FeatureA.cs"), "namespace FeatureA;");
        tempDirectory.CreateFile(Path.Combine("repo", "repair-comment.md"), "repair in place");
        using var writer = new StringWriter();
        var originalFactory = GenerateFromCurrentCommand.GitHubCommandRunnerFactory;

        try
        {
            GenerateFromCurrentCommand.GitHubCommandRunnerFactory = () => new FakeGenerateFromCurrentGitHubRunner();

            var exitCode = CommandRouter.Execute(
                ["generate-from-current", "comment", "auth", "--from-path", "src/feature", "--issues", "114", "--prs", "113", "--altitudes", "execution", "--from-file", "repair-comment.md"],
                CreateContext(repoRoot),
                writer);

            Assert.Equal(0, exitCode);
            Assert.Contains("Generate-from-current comment processed for domain 'auth'.", writer.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            GenerateFromCurrentCommand.GitHubCommandRunnerFactory = originalFactory;
        }
    }

    [Fact]
    public void Execute_GivenGenerateFromCurrentFixCommand_DispatchesToFixRenderer()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(Path.Combine("repo", "README.md"), "# Intent System");
        tempDirectory.CreateFile(Path.Combine("repo", "src", "feature", "FeatureA.cs"), "namespace FeatureA;");
        tempDirectory.CreateFile(Path.Combine("repo", "repair-comment.md"), "repair in place");
        using var writer = new StringWriter();
        var originalFactory = GenerateFromCurrentCommand.GitHubCommandRunnerFactory;

        try
        {
            GenerateFromCurrentCommand.GitHubCommandRunnerFactory = () => new FakeGenerateFromCurrentGitHubRunner();

            var exitCode = CommandRouter.Execute(
                ["generate-from-current", "fix", "auth", "--from-path", "src/feature", "--issues", "114", "--prs", "113", "--altitudes", "execution", "--from-file", "repair-comment.md"],
                CreateContext(repoRoot),
                writer);

            Assert.Equal(0, exitCode);
            Assert.Contains("Generate-from-current fix processed for domain 'auth'.", writer.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            GenerateFromCurrentCommand.GitHubCommandRunnerFactory = originalFactory;
        }
    }

    [Fact]
    public void Execute_GivenGenerateFromCurrentResubmitCommand_DispatchesToResubmitRenderer()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(Path.Combine("repo", "README.md"), "# Intent System");
        tempDirectory.CreateFile(Path.Combine("repo", "src", "feature", "FeatureA.cs"), "namespace FeatureA;");
        tempDirectory.CreateFile(Path.Combine("repo", "repair-comment.md"), "repair in place");
        using var writer = new StringWriter();
        var originalFactory = GenerateFromCurrentCommand.GitHubCommandRunnerFactory;

        try
        {
            GenerateFromCurrentCommand.GitHubCommandRunnerFactory = () => new FakeGenerateFromCurrentGitHubRunner();

            var exitCode = CommandRouter.Execute(
                ["generate-from-current", "resubmit", "auth", "--from-path", "src/feature", "--issues", "114", "--prs", "113", "--altitudes", "execution", "--from-file", "repair-comment.md"],
                CreateContext(repoRoot),
                writer);

            Assert.Equal(0, exitCode);
            Assert.Contains("Generate-from-current resubmit processed for domain 'auth'.", writer.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            GenerateFromCurrentCommand.GitHubCommandRunnerFactory = originalFactory;
        }
    }

    [Fact]
    public void Execute_GivenGenerateFromCurrentRereviewCommand_DispatchesToRereviewRenderer()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(Path.Combine("repo", "README.md"), "# Intent System");
        tempDirectory.CreateFile(Path.Combine("repo", "src", "feature", "FeatureA.cs"), "namespace FeatureA;");
        tempDirectory.CreateFile(Path.Combine("repo", "repair-comment.md"), "repair in place");
        using var writer = new StringWriter();
        var originalFactory = GenerateFromCurrentCommand.GitHubCommandRunnerFactory;

        try
        {
            GenerateFromCurrentCommand.GitHubCommandRunnerFactory = () => new FakeGenerateFromCurrentGitHubRunner();

            var exitCode = CommandRouter.Execute(
                ["generate-from-current", "rereview", "auth", "--from-path", "src/feature", "--issues", "114", "--prs", "113", "--altitudes", "execution", "--from-file", "repair-comment.md"],
                CreateContext(repoRoot),
                writer);

            Assert.Equal(0, exitCode);
            Assert.Contains("Generate-from-current rereview processed for domain 'auth'.", writer.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            GenerateFromCurrentCommand.GitHubCommandRunnerFactory = originalFactory;
        }
    }

    [Fact]
    public void Execute_GivenGenerateFromCurrentReacceptCommand_DispatchesToReacceptRenderer()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(Path.Combine("repo", "README.md"), "# Intent System");
        tempDirectory.CreateFile(Path.Combine("repo", "src", "feature", "FeatureA.cs"), "namespace FeatureA;");
        tempDirectory.CreateFile(Path.Combine("repo", "repair-comment.md"), "repair in place");
        using var writer = new StringWriter();
        var originalFactory = GenerateFromCurrentCommand.GitHubCommandRunnerFactory;

        try
        {
            GenerateFromCurrentCommand.GitHubCommandRunnerFactory = () => new FakeGenerateFromCurrentGitHubRunner();

            var exitCode = CommandRouter.Execute(
                ["generate-from-current", "reaccept", "auth", "--from-path", "src/feature", "--issues", "114", "--prs", "113", "--altitudes", "execution", "--from-file", "repair-comment.md"],
                CreateContext(repoRoot),
                writer);

            Assert.Equal(0, exitCode);
            Assert.Contains("Generate-from-current reaccept processed for domain 'auth'.", writer.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            GenerateFromCurrentCommand.GitHubCommandRunnerFactory = originalFactory;
        }
    }

    [Fact]
    public void Execute_GivenGenerateFromCurrentCloseoutCommand_DispatchesToCloseoutRenderer()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(Path.Combine("repo", "README.md"), "# Intent System");
        tempDirectory.CreateFile(Path.Combine("repo", "src", "feature", "FeatureA.cs"), "namespace FeatureA;");
        using var writer = new StringWriter();
        var originalFactory = GenerateFromCurrentCommand.GitHubCommandRunnerFactory;

        try
        {
            GenerateFromCurrentCommand.GitHubCommandRunnerFactory = () => new FakeGenerateFromCurrentGitHubRunner();

            var exitCode = CommandRouter.Execute(
                ["generate-from-current", "closeout", "auth", "--from-path", "src/feature", "--issues", "114", "--prs", "113", "--altitudes", "execution"],
                CreateContext(repoRoot),
                writer);

            Assert.Equal(0, exitCode);
            Assert.Contains("Generate-from-current closeout processed for domain 'auth'.", writer.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            GenerateFromCurrentCommand.GitHubCommandRunnerFactory = originalFactory;
        }
    }

    [Fact]
    public void Execute_GivenGenerateFromCurrentBestPracticeCommand_DispatchesToBestPracticeRenderer()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        var parentRepoRoot = tempDirectory.CreateDirectory("parent");
        tempDirectory.CreateDirectory(Path.Combine("repo", ".intent", "model-registry"));
        tempDirectory.CreateDirectory(Path.Combine("repo", ".intent", "best-practices"));
        tempDirectory.CreateFile(Path.Combine("repo", ".intent", "model-registry", "auth-model.md"), "# auth model");
        tempDirectory.CreateFile(Path.Combine("repo", ".intent", "best-practices", "performance.md"), "# performance");
        tempDirectory.CreateFile(Path.Combine("repo", "README.md"), "# Intent System");
        tempDirectory.CreateFile(Path.Combine("repo", "AGENTS.md"), "# Agent Guide");
        tempDirectory.CreateFile(Path.Combine("repo", "src", "feature", "FeatureA.cs"), "namespace FeatureA;");
        tempDirectory.CreateFile(Path.Combine("parent", "intents", "intent-cli", "specs", "11-reconstruction-review-and-confirmation.md"), "# review");
        using var writer = new StringWriter();
        var originalFactory = GenerateFromCurrentCommand.GitHubCommandRunnerFactory;

        try
        {
            GenerateFromCurrentCommand.GitHubCommandRunnerFactory = () => new FakeGenerateFromCurrentGitHubRunner();

            var exitCode = CommandRouter.Execute(
                ["generate-from-current", "best-practice", "auth", "--from-path", "src/feature", "--issues", "114", "--prs", "113", "--altitudes", "purpose,execution"],
                CreateContext(repoRoot, parentRepoRoot),
                writer);

            Assert.Equal(0, exitCode);
            Assert.Contains("Generate-from-current best-practice processed for domain 'auth'.", writer.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            GenerateFromCurrentCommand.GitHubCommandRunnerFactory = originalFactory;
        }
    }

    [Fact]
    public void Execute_GivenGenerateFromCurrentConfirmCommand_DispatchesToConfirmRenderer()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        var parentRepoRoot = tempDirectory.CreateDirectory("parent");
        tempDirectory.CreateDirectory(Path.Combine("repo", ".intent", "model-registry"));
        tempDirectory.CreateDirectory(Path.Combine("repo", ".intent", "best-practices"));
        tempDirectory.CreateFile(Path.Combine("repo", ".intent", "model-registry", "auth-model.md"), "# auth model");
        tempDirectory.CreateFile(Path.Combine("repo", ".intent", "best-practices", "performance.md"), "# performance");
        tempDirectory.CreateFile(Path.Combine("repo", "README.md"), "# Intent System");
        tempDirectory.CreateFile(Path.Combine("repo", "AGENTS.md"), "# Agent Guide");
        tempDirectory.CreateFile(Path.Combine("repo", "src", "feature", "FeatureA.cs"), "namespace FeatureA;");
        tempDirectory.CreateFile(Path.Combine("parent", "intents", "intent-cli", "specs", "11-reconstruction-review-and-confirmation.md"), "# review");
        tempDirectory.CreateFile(Path.Combine("repo", "prepared", "auth.decisions.md"), """
            # Current review decisions
            - confirm: validate the best-practice review suggestions for 'auth' against parent rules/specs before any canonical mutation.
            - confirm: choose which of the 2 suggested intent additions should return to the parent intent tree.
            - reject: explicitly reject any suggested intent addition that conflicts with project rules or specs.
            """);
        using var writer = new StringWriter();
        var originalFactory = GenerateFromCurrentCommand.GitHubCommandRunnerFactory;

        try
        {
            GenerateFromCurrentCommand.GitHubCommandRunnerFactory = () => new FakeGenerateFromCurrentGitHubRunner();

            var exitCode = CommandRouter.Execute(
                ["generate-from-current", "confirm", "auth", "--from-path", "src/feature", "--issues", "114", "--prs", "113", "--altitudes", "purpose,execution", "--from-file", "prepared/auth.decisions.md"],
                CreateContext(repoRoot, parentRepoRoot),
                writer);

            Assert.Equal(0, exitCode);
            Assert.Contains("Generate-from-current confirm processed for domain 'auth'.", writer.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            GenerateFromCurrentCommand.GitHubCommandRunnerFactory = originalFactory;
        }
    }

    [Fact]
    public void Execute_GivenGenerateFromCurrentClarifyCommand_DispatchesToClarifyRenderer()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        var parentRepoRoot = tempDirectory.CreateDirectory("parent");
        tempDirectory.CreateDirectory(Path.Combine("repo", ".intent", "model-registry"));
        tempDirectory.CreateDirectory(Path.Combine("repo", ".intent", "best-practices"));
        tempDirectory.CreateFile(Path.Combine("repo", ".intent", "model-registry", "auth-model.md"), "# auth model");
        tempDirectory.CreateFile(Path.Combine("repo", ".intent", "best-practices", "performance.md"), "# performance");
        tempDirectory.CreateFile(Path.Combine("repo", "README.md"), "# Intent System");
        tempDirectory.CreateFile(Path.Combine("repo", "AGENTS.md"), "# Agent Guide");
        tempDirectory.CreateFile(Path.Combine("repo", "src", "feature", "FeatureA.cs"), "namespace FeatureA;");
        tempDirectory.CreateFile(Path.Combine("parent", "intents", "intent-cli", "specs", "11-reconstruction-review-and-confirmation.md"), "# review");
        tempDirectory.CreateFile(Path.Combine("parent", "intents", "rules", "reconstruction-feedback-loop.md"), "# loop");
        tempDirectory.CreateFile(Path.Combine("repo", "prepared", "auth.decisions.md"), """
            # Current review decisions
            - confirm: validate the best-practice review suggestions for 'auth' against parent rules/specs before any canonical mutation.
            - reject: explicitly reject any suggested intent addition that conflicts with project rules or specs.
            - clarify: resolve 1 clarification candidate(s) before issue-cut-ready treatment.
            """);
        using var writer = new StringWriter();
        var originalFactory = GenerateFromCurrentCommand.GitHubCommandRunnerFactory;

        try
        {
            GenerateFromCurrentCommand.GitHubCommandRunnerFactory = () => new FakeGenerateFromCurrentGitHubRunner();

            var confirmExitCode = CommandRouter.Execute(
                ["generate-from-current", "confirm", "auth", "--from-path", "src/feature", "--issues", "114", "--prs", "113", "--altitudes", "purpose,execution", "--from-file", "prepared/auth.decisions.md"],
                CreateContext(repoRoot, parentRepoRoot),
                TextWriter.Null);
            Assert.Equal(0, confirmExitCode);

            var exitCode = CommandRouter.Execute(
                ["generate-from-current", "clarify", "auth", "--from-path", "src/feature", "--issues", "114", "--prs", "113", "--altitudes", "purpose,execution"],
                CreateContext(repoRoot, parentRepoRoot),
                writer);

            Assert.Equal(0, exitCode);
            Assert.Contains("Generate-from-current clarify processed for domain 'auth'.", writer.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            GenerateFromCurrentCommand.GitHubCommandRunnerFactory = originalFactory;
        }
    }

    [Fact]
    public void Execute_GivenGenerateFromCurrentReconcileCommand_DispatchesToReconcileRenderer()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        var parentRepoRoot = tempDirectory.CreateDirectory("parent");
        tempDirectory.CreateDirectory(Path.Combine("repo", ".intent", "model-registry"));
        tempDirectory.CreateDirectory(Path.Combine("repo", ".intent", "best-practices"));
        tempDirectory.CreateFile(Path.Combine("repo", ".intent", "model-registry", "auth-model.md"), "# auth model");
        tempDirectory.CreateFile(Path.Combine("repo", ".intent", "best-practices", "security.md"), "# security");
        tempDirectory.CreateFile(Path.Combine("repo", "README.md"), "# Intent System");
        tempDirectory.CreateFile(Path.Combine("repo", "AGENTS.md"), "# Agent Guide");
        tempDirectory.CreateFile(Path.Combine("repo", "src", "feature", "FeatureA.cs"), "namespace FeatureA;");
        tempDirectory.CreateFile(Path.Combine("parent", "intents", "intent-cli", "specs", "11-reconstruction-review-and-confirmation.md"), "# review");
        tempDirectory.CreateFile(Path.Combine("parent", "intents", "rules", "reconstruction-feedback-loop.md"), "# loop");
        tempDirectory.CreateFile(
            Path.Combine("repo", "prepared", "auth.decisions.md"),
            """
            # Current review decisions
            - confirm: validate the best-practice review suggestions for 'auth' against parent rules/specs before any canonical mutation.
            - reject: explicitly reject any suggested intent addition that conflicts with project rules or specs.
            """);
        using var writer = new StringWriter();
        var originalFactory = GenerateFromCurrentCommand.GitHubCommandRunnerFactory;

        try
        {
            GenerateFromCurrentCommand.GitHubCommandRunnerFactory = () => new FakeGenerateFromCurrentGitHubRunner();

            var confirmExitCode = CommandRouter.Execute(
                ["generate-from-current", "confirm", "auth", "--from-path", "src/feature", "--issues", "114", "--prs", "113", "--altitudes", "purpose,execution", "--from-file", "prepared/auth.decisions.md"],
                CreateContext(repoRoot, parentRepoRoot),
                TextWriter.Null);

            Assert.Equal(0, confirmExitCode);

            var exitCode = CommandRouter.Execute(
                ["generate-from-current", "reconcile", "auth", "--from-path", "src/feature", "--issues", "114", "--prs", "113", "--altitudes", "purpose,execution"],
                CreateContext(repoRoot, parentRepoRoot),
                writer);

            Assert.Equal(0, exitCode);
            Assert.Contains("Generate-from-current reconcile processed for domain 'auth'.", writer.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            GenerateFromCurrentCommand.GitHubCommandRunnerFactory = originalFactory;
        }
    }

    [Fact]
    public void Execute_GivenGenerateFromCurrentImplementCommand_DispatchesToImplementRenderer()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(Path.Combine("repo", "README.md"), "# Intent System");
        tempDirectory.CreateFile(Path.Combine("repo", "src", "feature", "FeatureA.cs"), "namespace FeatureA;");
        using var writer = new StringWriter();
        var originalFactory = GenerateFromCurrentCommand.GitHubCommandRunnerFactory;

        try
        {
            GenerateFromCurrentCommand.GitHubCommandRunnerFactory = () => new FakeGenerateFromCurrentGitHubRunner();

            var exitCode = CommandRouter.Execute(
                ["generate-from-current", "implement", "auth", "--from-path", "src/feature", "--issues", "114", "--prs", "113", "--altitudes", "execution"],
                CreateContext(repoRoot),
                writer);

            Assert.Equal(0, exitCode);
            Assert.Contains("Generate-from-current implement processed for domain 'auth'.", writer.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            GenerateFromCurrentCommand.GitHubCommandRunnerFactory = originalFactory;
        }
    }

    [Fact]
    public void Execute_GivenQueueListCommand_DispatchesToQueueRenderer()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState()));
        using var writer = new StringWriter();

        var exitCode = CommandRouter.Execute(["queue", "list"], CreateContext(repoRoot), writer);

        Assert.Equal(0, exitCode);
        Assert.Contains("A2", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenQueueShowCommand_DispatchesToQueueShowRenderer()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState()));
        using var writer = new StringWriter();

        var exitCode = CommandRouter.Execute(["queue", "show", "A2"], CreateContext(repoRoot), writer);

        Assert.Equal(0, exitCode);
        Assert.Contains("Execution unit: A2", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenQueueNextCommand_DispatchesToQueueNextRenderer()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState()));
        using var writer = new StringWriter();

        var exitCode = CommandRouter.Execute(["queue", "next"], CreateContext(repoRoot), writer);

        Assert.Equal(0, exitCode);
        Assert.Contains("Next candidate", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenQueueDispatchCommand_DispatchesToQueueDispatchRenderer()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueDispatchQueueState()));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G13", "packet.yaml"),
            CreateQueueDispatchPacketYaml());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G13", "github-body.md"),
            "# Goal");
        tempDirectory.CreateDirectory(Path.Combine("repo", "submodules", "intent-system"));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "runs.jsonl"),
            string.Empty);
        using var writer = new StringWriter();
        var originalPublisherFactory = QueueDispatchCommand.PublisherFactory;
        var originalGitFactory = QueueDispatchCommand.GitCommandRunnerFactory;
        var originalTimestampFactory = QueueDispatchCommand.TimestampFactory;

        try
        {
            QueueDispatchCommand.PublisherFactory = () => new FakeQueueDispatchPublisher();
            QueueDispatchCommand.GitCommandRunnerFactory = () => new FakeQueueDispatchGitRunner();
            QueueDispatchCommand.TimestampFactory = () => DateTimeOffset.Parse("2026-04-05T06:00:00Z");

            var exitCode = CommandRouter.Execute(["queue", "dispatch", "G13"], CreateContext(repoRoot), writer);

            Assert.Equal(0, exitCode);
            Assert.Contains("Queue item G13 dispatched", writer.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            QueueDispatchCommand.PublisherFactory = originalPublisherFactory;
            QueueDispatchCommand.GitCommandRunnerFactory = originalGitFactory;
            QueueDispatchCommand.TimestampFactory = originalTimestampFactory;
        }
    }

    [Fact]
    public void Execute_GivenQueueEnqueueCommand_DispatchesToQueueEnqueueRenderer()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueEnqueueQueueState()));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G38", "packet.yaml"),
            CreateQueueEnqueuePacketYaml());
        using var writer = new StringWriter();
        var originalTimestampFactory = QueueEnqueueCommand.TimestampFactory;

        try
        {
            QueueEnqueueCommand.TimestampFactory = () => DateTimeOffset.Parse("2026-04-06T10:30:00Z");

            var exitCode = CommandRouter.Execute(["queue", "enqueue", "G38"], CreateContext(repoRoot), writer);

            Assert.Equal(0, exitCode);
            Assert.Contains("Queue enqueue processed for execution unit 'G38'.", writer.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            QueueEnqueueCommand.TimestampFactory = originalTimestampFactory;
        }
    }

    [Fact]
    public void Execute_GivenInterviewStartCommand_DispatchesToInterviewStartRenderer()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "interviews", "auth", "iq-1.yaml"),
            CreateInterviewStartItemYaml());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "interviews", "auth", "iq-1.md"),
            "# Interview Question");
        using var writer = new StringWriter();

        var exitCode = CommandRouter.Execute(["interview", "start", "auth"], CreateContext(repoRoot), writer);

        Assert.Equal(0, exitCode);
        Assert.Contains("Next interview question:", writer.ToString(), StringComparison.Ordinal);
        Assert.Contains("Question: Which auth flow should be canonical?", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenInterviewAnswerCommand_DispatchesToInterviewAnswerRenderer()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "interviews", "auth", "iq-1.yaml"),
            CreateInterviewAnswerItemYaml());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "interviews", "auth", "iq-1.md"),
            "# Interview Question");
        using var writer = new StringWriter();
        var originalTimestampFactory = InterviewAnswerCommand.TimestampFactory;
        var originalInputReaderFactory = InterviewAnswerCommand.InputReaderFactory;

        try
        {
            InterviewAnswerCommand.TimestampFactory = () => DateTimeOffset.Parse("2026-04-13T10:00:00Z");
            InterviewAnswerCommand.InputReaderFactory = () => new StringReader("Use OAuth2 with PKCE." + Environment.NewLine);

            var exitCode = CommandRouter.Execute(["interview", "answer", "auth"], CreateContext(repoRoot), writer);

            Assert.Equal(0, exitCode);
            Assert.Contains("Interview answered for domain 'auth'.", writer.ToString(), StringComparison.Ordinal);
            Assert.Contains("Status: Answered", writer.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            InterviewAnswerCommand.TimestampFactory = originalTimestampFactory;
            InterviewAnswerCommand.InputReaderFactory = originalInputReaderFactory;
        }
    }

    [Fact]
    public void Execute_GivenInterviewResumeCommand_DispatchesToInterviewResumeRenderer()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "interviews", "auth", "iq-1.yaml"),
            CreateInterviewStartItemYaml());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "interviews", "auth", "iq-1.md"),
            "# Interview Question");
        using var writer = new StringWriter();

        var exitCode = CommandRouter.Execute(["interview", "resume", "auth"], CreateContext(repoRoot), writer);

        Assert.Equal(0, exitCode);
        Assert.Contains("Next interview question:", writer.ToString(), StringComparison.Ordinal);
        Assert.Contains("Question: Which auth flow should be canonical?", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenIntakeCompileCommand_DispatchesToIntakeCompileRenderer()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "interviews", "auth", "iq-1.yaml"),
            CreateInterviewAnswerItemYaml());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "interviews", "auth", "iq-1.md"),
            "# Interview Question");
        using var writer = new StringWriter();

        var exitCode = CommandRouter.Execute(["intake", "compile", "auth"], CreateContext(repoRoot), writer);

        Assert.Equal(0, exitCode);
        Assert.Contains("Intake compile is not ready", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenIntakeFoldinCommand_DispatchesToIntakeFoldinRenderer()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "intake", "auth.compile.md"),
            """
            # Intake Compile

            ## Domain

            `auth`

            answered_question_ids:
            - iq-1

            recommended_updates:
            - Add device-code note

            return_to_intent_paths:
            - intents/intent-cli/intent-tree/means/auth-oauth2.md

            source_concept_refs:
            - intents/intent-cli/concepts/auth-oauth2.md
            """);
        using var writer = new StringWriter();

        var exitCode = CommandRouter.Execute(["intake", "foldin", "auth"], CreateContext(repoRoot), writer);

        Assert.Equal(0, exitCode);
        Assert.Contains("Intake fold-in draft generated for domain 'auth'.", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenIntakePatchCommand_DispatchesToIntakePatchRenderer()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "intake", "auth.foldin.md"),
            """
            # Intake Fold-In Draft

            ## Domain

            `auth`

            answered_question_ids:
            - iq-1

            recommended_updates:
            - Add device-code note

            return_to_intent_paths:
            - intents/intent-cli/intent-tree/means/auth-oauth2.md

            source_concept_refs:
            - intents/intent-cli/concepts/auth-oauth2.md
            """);
        tempDirectory.CreateFile(
            Path.Combine("repo", "intents", "intent-cli", "intent-tree", "means", "auth-oauth2.md"),
            "# Auth Means");
        tempDirectory.CreateFile(
            Path.Combine("repo", "intents", "intent-cli", "concepts", "auth-oauth2.md"),
            "# Auth Concept");
        using var writer = new StringWriter();

        var exitCode = CommandRouter.Execute(["intake", "patch", "auth"], CreateContext(repoRoot), writer);

        Assert.Equal(0, exitCode);
        Assert.Contains("Intake patch draft generated for domain 'auth'.", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenIntakeApplyCommand_DispatchesToIntakeApplyRenderer()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "intake", "auth.patch.md"),
            """
            # Intake Patch Draft

            ## Domain

            `auth`

            target_file_paths:
            - intents/intent-cli/intent-tree/means/auth-oauth2.md

            source_concept_refs:
            - intents/intent-cli/concepts/auth-oauth2.md

            ## File-By-File Patch Candidates

            ### `intents/intent-cli/intent-tree/means/auth-oauth2.md`

            current_file_state: present
            foldin_anchors:
            - answered_question_ids:iq-1
            source_concept_refs:
            - intents/intent-cli/concepts/auth-oauth2.md
            proposed_edits:
            - Apply update candidate: Add device-code note
            rationale:
            - This path is listed in return_to_intent_paths.
            current_file_excerpt:
            ```text
            # Auth Means
            Existing line
            ```
            """);
        tempDirectory.CreateFile(
            Path.Combine("repo", "intents", "intent-cli", "intent-tree", "means", "auth-oauth2.md"),
            "# Auth Means" + Environment.NewLine + "Existing line");
        using var writer = new StringWriter();

        var exitCode = CommandRouter.Execute(["intake", "apply", "auth"], CreateContext(repoRoot), writer);

        Assert.Equal(0, exitCode);
        Assert.Contains("Intake apply completed for domain 'auth'.", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenIntakeExecutionCommand_DispatchesToIntakeExecutionRenderer()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "intake", "auth.patch.md"),
            """
            # Intake Patch Draft

            ## Domain

            `auth`

            target_file_paths:
            - intents/intent-cli/concepts/oauth2.md
            - intents/intent-cli/intent-tree/means/device-code.md

            source_concept_refs:
            - intents/intent-cli/concepts/oauth2.md

            ## File-By-File Patch Candidates
            """);
        tempDirectory.CreateFile(
            Path.Combine("repo", "intents", "intent-cli", "concepts", "oauth2.md"),
            "# Auth Concept" + Environment.NewLine + "- Reconcile this source concept file with the current fold-in draft.");
        tempDirectory.CreateFile(
            Path.Combine("repo", "intents", "intent-cli", "intent-tree", "means", "device-code.md"),
            "# Auth Means" + Environment.NewLine + "- Add device-code note");
        using var writer = new StringWriter();

        var exitCode = CommandRouter.Execute(["intake", "execution", "auth"], CreateContext(repoRoot), writer);

        Assert.Equal(0, exitCode);
        Assert.Contains("Intake execution draft generated for domain 'auth'.", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenIntakeExecutionApplyCommand_DispatchesToIntakeExecutionApplyRenderer()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "intake", "auth.execution.md"),
            """
            # Intake Execution Draft

            ## Domain

            `auth`

            ## Proposed Execution Units

            ### `AUTH-01`

            source_file_path: intents/intent-cli/concepts/oauth2.md
            target_part: concepts
            dependencies:
            - none
            readiness_notes:
            - Source file path: intents/intent-cli/concepts/oauth2.md
            - Current heading: # Auth Concept
            verification_hints:
            - dotnet test IntentSystem.sln
            """);
        tempDirectory.CreateFile(
            Path.Combine("repo", "intents", "intent-cli", "execution", "05-post-mvp-sub-slices.md"),
            """
            # Post-MVP Sub-Slices

            ## G36 の current baseline

            - `intake execution apply <domain>` を最初の execution source-of-truth apply command にする
            """);
        using var writer = new StringWriter();

        var exitCode = CommandRouter.Execute(["intake", "execution", "apply", "auth"], CreateContext(repoRoot), writer);

        Assert.Equal(0, exitCode);
        Assert.Contains("Intake execution apply completed for domain 'auth'.", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenIntakeIssueCommand_DispatchesToIntakeIssueRenderer()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", "intents", "intent-cli", "execution", "05-post-mvp-sub-slices.md"),
            """
            # Post-MVP Sub-Slices

            | subslice_id | belongs_to_slice | goal | depends_on_subslices | target_repo | target_path | target_part | issue_cut_ready |
            |---|---|---|---|---|---|---|---|
            | G37 | G | `intake issue <domain>` を CLI shell から使えるようにし、updated intake-origin `execution/` source-of-truth から issue-ready execution unit の issue artifact 群を deterministic に生成できるようにする | G2, G35, G36 | submodules/intent-system | . | cli intake issue command | yes |

            ## G37 の current baseline

            - `intake issue <domain>` を最初の intake issue-artifact generation command にする
            """);
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "intake", "auth.execution.md"),
            """
            # Intake Execution Draft

            ## Domain
            `auth`

            ## Proposed Execution Units

            ### `AUTH-01`
            source_file_path: intents/intent-cli/concepts/oauth2.md
            target_part: concepts
            dependencies:
            - none
            readiness_notes:
            - Current heading: # Auth Concept
            verification_hints:
            - dotnet test IntentSystem.sln
            """);
        tempDirectory.CreateFile(
            Path.Combine("repo", "intents", "intent-cli", "concepts", "oauth2.md"),
            "# Auth Concept");
        using var writer = new StringWriter();

        var exitCode = CommandRouter.Execute(["intake", "issue", "auth"], CreateContext(repoRoot), writer);

        Assert.Equal(0, exitCode);
        Assert.Contains("Intake issue artifacts generated for domain 'auth'.", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenIntakeEnqueueCommand_DispatchesToIntakeEnqueueRenderer()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueEnqueueQueueState()));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "intake", "auth.execution.md"),
            """
            # Intake Execution Draft

            ## Domain
            `auth`

            ## Proposed Execution Units

            ### `AUTH-01`
            source_file_path: intents/intent-cli/concepts/oauth2.md
            target_part: concepts
            dependencies:
            - none
            readiness_notes:
            - Current heading: # Auth Concept
            verification_hints:
            - dotnet test IntentSystem.sln
            """);
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "AUTH-01", "packet.yaml"),
            CreateIntakeEnqueuePacketYaml("AUTH-01"));
        using var writer = new StringWriter();
        var originalTimestampFactory = QueueEnqueueCommand.TimestampFactory;

        try
        {
            QueueEnqueueCommand.TimestampFactory = () => DateTimeOffset.Parse("2026-04-06T11:00:00Z");

            var exitCode = CommandRouter.Execute(["intake", "enqueue", "auth"], CreateContext(repoRoot), writer);

            Assert.Equal(0, exitCode);
            Assert.Contains("Intake enqueue processed for domain 'auth'.", writer.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            QueueEnqueueCommand.TimestampFactory = originalTimestampFactory;
        }
    }

    [Fact]
    public void Execute_GivenIntakeConceptCommand_DispatchesToIntakeConceptRenderer()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", "concepts", "auth.txt"),
            "Add OAuth2 provider support.");
        using var writer = new StringWriter();

        var exitCode = CommandRouter.Execute(
            ["intake", "concept", "auth", "--from-file", "concepts/auth.txt"],
            CreateContext(repoRoot),
            writer);

        Assert.Equal(0, exitCode);
        Assert.Contains("Intake concept artifact generated for domain 'auth'.", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenIntakeAutostartCommand_DispatchesToIntakeAutostartRenderer()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateDirectory(Path.Combine("repo", "submodules", "intent-system"));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueDispatchQueueState()));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G13", "packet.yaml"),
            CreateQueueDispatchPacketYaml());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G13", "github-body.md"),
            "# Goal");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "runs.jsonl"),
            string.Empty);
        using var writer = new StringWriter();
        var originalPublisherFactory = QueueDispatchCommand.PublisherFactory;
        var originalRemoteGitFactory = QueueDispatchCommand.GitCommandRunnerFactory;
        var originalDispatchTimestampFactory = QueueDispatchCommand.TimestampFactory;
        var originalStartGitFactory = RunStartCommand.GitCommandRunnerFactory;
        var originalStartTimestampFactory = RunStartCommand.TimestampFactory;

        try
        {
            QueueDispatchCommand.PublisherFactory = () => new FakeQueueDispatchPublisher();
            QueueDispatchCommand.GitCommandRunnerFactory = () => new FakeQueueDispatchGitRunner();
            QueueDispatchCommand.TimestampFactory = () => DateTimeOffset.Parse("2026-04-05T06:00:00Z");
            RunStartCommand.GitCommandRunnerFactory = () => new FakeRunStartGitRunner();
            RunStartCommand.TimestampFactory = () => DateTimeOffset.Parse("2026-04-05T09:30:00Z");

            var exitCode = CommandRouter.Execute(["intake", "autostart", "G13"], CreateContext(repoRoot), writer);

            Assert.Equal(0, exitCode);
            Assert.Contains("Intake autostart completed for G13.", writer.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            QueueDispatchCommand.PublisherFactory = originalPublisherFactory;
            QueueDispatchCommand.GitCommandRunnerFactory = originalRemoteGitFactory;
            QueueDispatchCommand.TimestampFactory = originalDispatchTimestampFactory;
            RunStartCommand.GitCommandRunnerFactory = originalStartGitFactory;
            RunStartCommand.TimestampFactory = originalStartTimestampFactory;
        }
    }

    [Fact]
    public void Execute_GivenIntakeLaunchCommand_DispatchesToIntakeLaunchRenderer()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateDirectory(Path.Combine("repo", "submodules", "intent-system"));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueEnqueueQueueState()));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "runs.jsonl"),
            string.Empty);
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "intake", "auth.execution.md"),
            """
            # Intake Execution Draft

            ## Domain
            `auth`

            ## Proposed Execution Units

            ### `AUTH-01`
            source_file_path: intents/intent-cli/concepts/oauth2.md
            target_part: concepts
            dependencies:
            - none
            readiness_notes:
            - Current heading: # Auth Concept
            verification_hints:
            - dotnet test IntentSystem.sln
            """);
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "AUTH-01", "packet.yaml"),
            """
            execution_unit: AUTH-01
            implementation_issue:
              issue_title: "AUTH-01 Intake Launch"
              goal: "Launch generated issue-ready execution unit into queue and autostart flow."
              in_scope:
                - "queue insertion"
                - "issue creation"
                - "run start"
              out_of_scope:
                - "review execution"
              target_repo: "submodules/intent-system"
              target_path: "."
              target_part: "cli intake launch command"
              dependencies:
                - "G3"
              technical_baseline:
                - "C# / .NET"
              project_local_guidance:
                - "AGENTS.md"
              intent_baseline:
                - "intake launch stays thin"
              acceptance_criteria:
                - "issue-ready unit launches deterministically"
              verification:
                - "tests-passing"

            review:
              summarize_first: true
              require_explicit_diff_check: true
              require_explicit_scope_check: true
              require_explicit_contract_check: true
              required_checks:
                - "intake launch remains thin"
            """);
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "AUTH-01", "github-body.md"),
            "# AUTH-01");
        using var writer = new StringWriter();
        var originalEnqueueTimestampFactory = QueueEnqueueCommand.TimestampFactory;
        var originalPublisherFactory = QueueDispatchCommand.PublisherFactory;
        var originalRemoteGitFactory = QueueDispatchCommand.GitCommandRunnerFactory;
        var originalDispatchTimestampFactory = QueueDispatchCommand.TimestampFactory;
        var originalStartGitFactory = RunStartCommand.GitCommandRunnerFactory;
        var originalStartTimestampFactory = RunStartCommand.TimestampFactory;

        try
        {
            QueueEnqueueCommand.TimestampFactory = () => DateTimeOffset.Parse("2026-04-06T11:00:00Z");
            QueueDispatchCommand.PublisherFactory = () => new FakeQueueDispatchPublisher();
            QueueDispatchCommand.GitCommandRunnerFactory = () => new FakeQueueDispatchGitRunner();
            QueueDispatchCommand.TimestampFactory = () => DateTimeOffset.Parse("2026-04-06T11:05:00Z");
            RunStartCommand.GitCommandRunnerFactory = () => new FakeRunStartGitRunner();
            RunStartCommand.TimestampFactory = () => DateTimeOffset.Parse("2026-04-06T11:10:00Z");

            var exitCode = CommandRouter.Execute(["intake", "launch", "auth"], CreateContext(repoRoot), writer);

            Assert.Equal(0, exitCode);
            Assert.Contains("Intake launch processed for domain 'auth'.", writer.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            QueueEnqueueCommand.TimestampFactory = originalEnqueueTimestampFactory;
            QueueDispatchCommand.PublisherFactory = originalPublisherFactory;
            QueueDispatchCommand.GitCommandRunnerFactory = originalRemoteGitFactory;
            QueueDispatchCommand.TimestampFactory = originalDispatchTimestampFactory;
            RunStartCommand.GitCommandRunnerFactory = originalStartGitFactory;
            RunStartCommand.TimestampFactory = originalStartTimestampFactory;
        }
    }

    [Fact]
    public void Execute_GivenIntakeStartCommand_DispatchesToIntakeStartRenderer()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateDirectory(Path.Combine("repo", "submodules", "intent-system"));
        tempDirectory.CreateFile(
            Path.Combine("repo", "intents", "intent-cli", "execution", "05-post-mvp-sub-slices.md"),
            """
            # Post-MVP Sub-Slices

            | subslice_id | belongs_to_slice | goal | depends_on_subslices | target_repo | target_path | target_part | issue_cut_ready |
            |---|---|---|---|---|---|---|---|
            | G37 | G | `intake issue <domain>` を CLI shell から使えるようにし、updated intake-origin `execution/` source-of-truth から issue-ready execution unit の issue artifact 群を deterministic に生成できるようにする | G2, G35, G36 | submodules/intent-system | . | cli intake issue command | yes |

            ## G37 の current baseline

            - `intake issue <domain>` を最初の intake issue-artifact generation command にする
            - canonical source は current `execution/` source files と current `G2` / `G29` / `G30` / `G32` / `G33` / `G34` / `G35` / `G36` intake baseline である
            - successful output は selected domain の intake-origin issue-ready execution unit に対応する `.intent-cli/issues/<execution-unit>/implementation.md`, `review-context.md`, `packet.yaml`, and `github-body.md` の deterministic generation を baseline にする
            """);
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "intake", "auth.execution.md"),
            """
            # Intake Execution Draft

            ## Domain
            `auth`

            ## Proposed Execution Units

            ### `AUTH-01`
            source_file_path: intents/intent-cli/concepts/oauth2.md
            target_part: concepts
            dependencies:
            - none
            readiness_notes:
            - Current heading: # Auth Concept
            verification_hints:
            - dotnet test IntentSystem.sln
            """);
        tempDirectory.CreateFile(
            Path.Combine("repo", "intents", "intent-cli", "concepts", "oauth2.md"),
            "# Auth Concept");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueEnqueueQueueState()));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "runs.jsonl"),
            string.Empty);
        using var writer = new StringWriter();
        var originalEnqueueTimestampFactory = QueueEnqueueCommand.TimestampFactory;
        var originalPublisherFactory = QueueDispatchCommand.PublisherFactory;
        var originalRemoteGitFactory = QueueDispatchCommand.GitCommandRunnerFactory;
        var originalDispatchTimestampFactory = QueueDispatchCommand.TimestampFactory;
        var originalStartGitFactory = RunStartCommand.GitCommandRunnerFactory;
        var originalStartTimestampFactory = RunStartCommand.TimestampFactory;

        try
        {
            QueueEnqueueCommand.TimestampFactory = () => DateTimeOffset.Parse("2026-04-06T11:00:00Z");
            QueueDispatchCommand.PublisherFactory = () => new FakeQueueDispatchPublisher();
            QueueDispatchCommand.GitCommandRunnerFactory = () => new FakeQueueDispatchGitRunner();
            QueueDispatchCommand.TimestampFactory = () => DateTimeOffset.Parse("2026-04-06T11:05:00Z");
            RunStartCommand.GitCommandRunnerFactory = () => new FakeRunStartGitRunner();
            RunStartCommand.TimestampFactory = () => DateTimeOffset.Parse("2026-04-06T11:10:00Z");

            var exitCode = CommandRouter.Execute(["intake", "start", "auth"], CreateContext(repoRoot), writer);

            Assert.Equal(0, exitCode);
            Assert.Contains("Intake start processed for domain 'auth'.", writer.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            QueueEnqueueCommand.TimestampFactory = originalEnqueueTimestampFactory;
            QueueDispatchCommand.PublisherFactory = originalPublisherFactory;
            QueueDispatchCommand.GitCommandRunnerFactory = originalRemoteGitFactory;
            QueueDispatchCommand.TimestampFactory = originalDispatchTimestampFactory;
            RunStartCommand.GitCommandRunnerFactory = originalStartGitFactory;
            RunStartCommand.TimestampFactory = originalStartTimestampFactory;
        }
    }

    [Fact]
    public void Execute_GivenIntakeActivateCommand_DispatchesToIntakeActivateRenderer()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "intake", "auth.concept.yaml"),
            """
            domain_slug: auth
            concept_source: interactive
            concept_text: "Add OAuth2 provider support."
            upstream_paths:
              - "intents/intent-cli/intent-tree/means/04-worker-interface-strategy.md"
            initial_goal: "Add OAuth2 provider support."
            constraints:
              - "Must not break existing session flow"
            known_unknowns:
              - "Which OAuth providers to support?"
            """);
        using var writer = new StringWriter();

        var exitCode = CommandRouter.Execute(["intake", "activate", "auth"], CreateContext(repoRoot), writer);

        Assert.Equal(0, exitCode);
        Assert.Contains("Intake activate processed for domain 'auth'.", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenIntakeAdvanceCommand_DispatchesToIntakeAdvanceRenderer()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "intake", "auth.concept.yaml"),
            """
            domain_slug: auth
            concept_source: interactive
            concept_text: "Add OAuth2 provider support."
            upstream_paths:
              - "intents/intent-cli/intent-tree/means/04-worker-interface-strategy.md"
            initial_goal: "Add OAuth2 provider support."
            constraints:
              - "Must not break existing session flow"
            known_unknowns:
              - "Which OAuth providers to support?"
            """);
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "interviews", "auth", "iq-1.yaml"),
            """
            artifact_kind: interview
            domain_slug: auth
            source_concept_ref: "intents/intent-cli/concepts/auth-oauth2.md"
            question_id: iq-1
            question_text: "What should be updated?"
            reason: "Clarify auth direction."
            affects:
              - "auth-oauth2"
            blocking_or_nonblocking: blocking
            status: answered
            return_to_intent_paths:
              - "intents/intent-cli/intent-tree/means/auth-oauth2.md"
            created_at: "2026-04-13T07:00:00.0000000+00:00"
            answer: "Align login UX wording."
            answered_at: "2026-04-13T10:00:00.0000000+00:00"
            recommended_updates:
              - "Align login UX wording"
            """);
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "interviews", "auth", "iq-1.md"),
            "# Interview Question");
        tempDirectory.CreateFile(
            Path.Combine("repo", "intents", "intent-cli", "concepts", "auth-oauth2.md"),
            "# Auth Concept" + Environment.NewLine + Environment.NewLine + "- Existing note" + Environment.NewLine);
        tempDirectory.CreateFile(
            Path.Combine("repo", "intents", "intent-cli", "intent-tree", "means", "auth-oauth2.md"),
            "# Auth Means" + Environment.NewLine + Environment.NewLine + "- Existing rule" + Environment.NewLine);
        tempDirectory.CreateFile(
            Path.Combine("repo", "intents", "intent-cli", "execution", "05-post-mvp-sub-slices.md"),
            """
            # Post-MVP Sub-Slices

            ## G36 の current baseline

            - `intake execution apply <domain>` を最初の execution source-of-truth apply command にする
            - successful output は execution draft で指定された source files だけを deterministic に更新することを baseline にする
            """);
        using var writer = new StringWriter();

        var exitCode = CommandRouter.Execute(["intake", "advance", "auth"], CreateContext(repoRoot), writer);

        Assert.Equal(0, exitCode);
        Assert.Contains("Intake advance processed for domain 'auth'.", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenClarifyAnswerCommand_DispatchesToClarifyAnswerRenderer()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateClarifyAnswerQueueState()));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "clarifications", "G24", "request.json"),
            ClarificationSerializer.Serialize(CreateClarifyAnswerItem()));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "runs.jsonl"),
            string.Empty);
        using var writer = new StringWriter();
        var originalTimestampFactory = ClarifyAnswerCommand.TimestampFactory;
        var originalInputReaderFactory = ClarifyAnswerCommand.InputReaderFactory;

        try
        {
            ClarifyAnswerCommand.TimestampFactory = () => DateTimeOffset.Parse("2026-04-12T07:10:00Z");
            ClarifyAnswerCommand.InputReaderFactory = () => new StringReader("Use the current queue snapshot." + Environment.NewLine);

            var exitCode = CommandRouter.Execute(["clarify", "answer", "G24"], CreateContext(repoRoot), writer);

            Assert.Equal(0, exitCode);
            Assert.Contains("Clarification answered for G24.", writer.ToString(), StringComparison.Ordinal);
            Assert.Contains("Queue state: review", writer.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            ClarifyAnswerCommand.TimestampFactory = originalTimestampFactory;
            ClarifyAnswerCommand.InputReaderFactory = originalInputReaderFactory;
        }
    }

    [Fact]
    public void Execute_GivenRunStartCommand_DispatchesToRunStartRenderer()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateDirectory(Path.Combine("repo", "submodules", "intent-system"));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateRunStartQueueState()));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G14", "packet.yaml"),
            CreateRunStartPacketYaml());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "runs.jsonl"),
            string.Empty);
        using var writer = new StringWriter();
        var originalGitFactory = RunStartCommand.GitCommandRunnerFactory;
        var originalTimestampFactory = RunStartCommand.TimestampFactory;

        try
        {
            RunStartCommand.GitCommandRunnerFactory = () => new FakeRunStartGitRunner();
            RunStartCommand.TimestampFactory = () => DateTimeOffset.Parse("2026-04-05T09:30:00Z");

            var exitCode = CommandRouter.Execute(["run", "start", "G14"], CreateContext(repoRoot), writer);

            Assert.Equal(0, exitCode);
            Assert.Contains("Run started for G14", writer.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            RunStartCommand.GitCommandRunnerFactory = originalGitFactory;
            RunStartCommand.TimestampFactory = originalTimestampFactory;
        }
    }

    [Fact]
    public void Execute_GivenRunSubmitCommand_DispatchesToRunSubmitRenderer()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateDirectory(Path.Combine("repo", "submodules", "intent-system"));
        tempDirectory.CreateDirectory(Path.Combine("repo", ".intent-cli", "worktrees", "G14"));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateRunSubmitQueueState()));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G14", "packet.yaml"),
            CreateRunSubmitPacketYaml());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "runs.jsonl"),
            string.Empty);
        using var writer = new StringWriter();
        var originalGitFactory = RunSubmitCommand.GitCommandRunnerFactory;
        var originalPublisherFactory = RunSubmitCommand.PublisherFactory;
        var originalTimestampFactory = RunSubmitCommand.TimestampFactory;

        try
        {
            RunSubmitCommand.GitCommandRunnerFactory = () => new FakeRunSubmitGitRunner();
            RunSubmitCommand.PublisherFactory = () => new FakeRunSubmitPublisher();
            RunSubmitCommand.TimestampFactory = () => DateTimeOffset.Parse("2026-04-05T10:15:00Z");

            var exitCode = CommandRouter.Execute(["run", "submit", "G14"], CreateContext(repoRoot), writer);

            Assert.Equal(0, exitCode);
            Assert.Contains("Run submitted for G14", writer.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            RunSubmitCommand.GitCommandRunnerFactory = originalGitFactory;
            RunSubmitCommand.PublisherFactory = originalPublisherFactory;
            RunSubmitCommand.TimestampFactory = originalTimestampFactory;
        }
    }

    [Fact]
    public void Execute_GivenRunResubmitCommand_DispatchesToRunResubmitRenderer()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateDirectory(Path.Combine("repo", "submodules", "intent-system"));
        tempDirectory.CreateDirectory(Path.Combine("repo", ".intent-cli", "worktrees", "G21"));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateRunResubmitQueueState()));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G21", "packet.yaml"),
            CreateRunResubmitPacketYaml());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "runs.jsonl"),
            CreateRunResubmitRunLog());
        using var writer = new StringWriter();
        var originalGitFactory = RunResubmitCommand.GitCommandRunnerFactory;
        var originalTimestampFactory = RunResubmitCommand.TimestampFactory;

        try
        {
            RunResubmitCommand.GitCommandRunnerFactory = () => new FakeRunResubmitGitRunner();
            RunResubmitCommand.TimestampFactory = () => DateTimeOffset.Parse("2026-04-10T07:15:00Z");

            var exitCode = CommandRouter.Execute(["run", "resubmit", "G21"], CreateContext(repoRoot), writer);

            Assert.Equal(0, exitCode);
            Assert.Contains("Run resubmitted for G21", writer.ToString(), StringComparison.Ordinal);
            Assert.Contains("Latest linked PR: https://github.com/J-Tech-Japan/intent-system/pull/71", writer.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            RunResubmitCommand.GitCommandRunnerFactory = originalGitFactory;
            RunResubmitCommand.TimestampFactory = originalTimestampFactory;
        }
    }

    [Fact]
    public void Execute_GivenRunRereviewCommand_DispatchesToRunRereviewRenderer()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateRunRereviewQueueState()));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "runs.jsonl"),
            CreateRunRereviewRunLog());
        using var writer = new StringWriter();
        var originalTimestampFactory = RunRereviewCommand.TimestampFactory;

        try
        {
            RunRereviewCommand.TimestampFactory = () => DateTimeOffset.Parse("2026-04-06T08:30:00Z");

            var exitCode = CommandRouter.Execute(["run", "rereview", "G16"], CreateContext(repoRoot), writer);

            Assert.Equal(0, exitCode);
            Assert.Contains("Run rereviewed for G16", writer.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            RunRereviewCommand.TimestampFactory = originalTimestampFactory;
        }
    }

    [Fact]
    public void Execute_GivenRunResumeCommand_DispatchesToRunResumeRenderer()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateDirectory(Path.Combine("repo", "submodules", "intent-system"));
        tempDirectory.CreateDirectory(Path.Combine("repo", ".intent-cli", "worktrees", "G17"));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateRunResumeQueueState()));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G17", "packet.yaml"),
            CreateRunResumePacketYaml());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "runs.jsonl"),
            CreateRunResumeRunLog());
        using var writer = new StringWriter();

        var exitCode = CommandRouter.Execute(["run", "resume", "G17"], CreateContext(repoRoot), writer);

        Assert.Equal(0, exitCode);
        Assert.Contains("Execution unit: G17", writer.ToString(), StringComparison.Ordinal);
        Assert.Contains("Latest linked PR: https://github.com/J-Tech-Japan/intent-system/pull/63", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenRunLogCommand_DispatchesToRunLogRenderer()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateRunLogQueueState()));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "runs.jsonl"),
            CreateRunLogCommandRunLog());
        using var writer = new StringWriter();

        var exitCode = CommandRouter.Execute(["run", "log", "G18"], CreateContext(repoRoot), writer);

        Assert.Equal(0, exitCode);
        Assert.Contains("Execution unit: G18", writer.ToString(), StringComparison.Ordinal);
        Assert.Contains("event=review", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenRunImplementCommand_DispatchesToRunImplementRenderer()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateDirectory(Path.Combine("repo", "submodules", "intent-system"));
        tempDirectory.CreateDirectory(Path.Combine("repo", ".intent-cli", "worktrees", "G19"));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateRunImplementQueueState()));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "runs.jsonl"),
            CreateRunImplementRunLog());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G19", "packet.yaml"),
            CreateRunImplementPacketYaml());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G19", "review-context.md"),
            CreateRunImplementReviewContextMarkdown());
        using var writer = new StringWriter();

        var exitCode = CommandRouter.Execute(["run", "implement", "G19"], CreateContext(repoRoot), writer);

        Assert.Equal(0, exitCode);
        Assert.Contains("Implementation handoff artifact generated for G19", writer.ToString(), StringComparison.Ordinal);
        Assert.Contains("Implement role: Claude", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenRunFixCommand_DispatchesToRunFixRenderer()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateDirectory(Path.Combine("repo", "submodules", "intent-system"));
        tempDirectory.CreateDirectory(Path.Combine("repo", ".intent-cli", "worktrees", "G20"));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateRunFixQueueState()));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "runs.jsonl"),
            CreateRunFixRunLog());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G20", "packet.yaml"),
            CreateRunFixPacketYaml());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G20", "review-context.md"),
            CreateRunFixReviewContextMarkdown());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "reviews", "G20.comment.json"),
            CreateRunFixReviewCommentArtifactJson());
        using var writer = new StringWriter();

        var exitCode = CommandRouter.Execute(["run", "fix", "G20"], CreateContext(repoRoot), writer);

        Assert.Equal(0, exitCode);
        Assert.Contains("Repair handoff artifact generated for G20", writer.ToString(), StringComparison.Ordinal);
        Assert.Contains("Latest comment ref: https://github.com/J-Tech-Japan/intent-system/pull/69#issuecomment-2", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenWorkflowRenderCommand_DispatchesToWorkflowRenderer()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateWorkflowQueueState()));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "C2", "packet.yaml"),
            CreateWorkflowPacketYaml());
        using var writer = new StringWriter();

        var exitCode = CommandRouter.Execute(["workflow", "render", "C2"], CreateContext(repoRoot), writer);

        Assert.Equal(0, exitCode);
        Assert.Contains("Workflow definition rendered for C2", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenWorkflowRunCommand_DispatchesToWorkflowRunRenderer()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateWorkflowQueueState()));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "workflows", "C2.yaml"),
            CreateWorkflowDefinitionJson());
        using var writer = new StringWriter();

        var exitCode = CommandRouter.Execute(["workflow", "run", "C2"], CreateContext(repoRoot), writer);

        Assert.Equal(0, exitCode);
        Assert.Contains("Workflow run artifact generated for C2", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenWorkflowStatusCommand_DispatchesToWorkflowStatusRenderer()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "workflows", "C2.yaml"),
            CreateWorkflowDefinitionJson());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "workflows", "C2.run.json"),
            CreateWorkflowRunArtifactJson());
        using var writer = new StringWriter();

        var exitCode = CommandRouter.Execute(["workflow", "status", "C2"], CreateContext(repoRoot), writer);

        Assert.Equal(0, exitCode);
        Assert.Contains("Run status: Running", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenQueueTransitionCommand_DispatchesToQueueTransitionRenderer()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState()));
        using var writer = new StringWriter();

        var exitCode = CommandRouter.Execute(["queue", "transition", "A2", "completed"], CreateContext(repoRoot), writer);

        Assert.Equal(0, exitCode);
        Assert.Contains("Transitioned A2 to completed", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenReviewRunCommand_DispatchesToReviewRunRenderer()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateReviewQueueState()));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G9", "review-context.md"),
            CreateReviewContextMarkdown());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "runs.jsonl"),
            CreateReviewRunLog());
        using var writer = new StringWriter();

        var exitCode = CommandRouter.Execute(["review", "run", "G9"], CreateContext(repoRoot), writer);

        Assert.Equal(0, exitCode);
        Assert.Contains("Review request artifact generated for G9", writer.ToString(), StringComparison.Ordinal);

        var artifact = ReviewRequestSerializer.Deserialize(
            File.ReadAllText(Path.Combine(repoRoot, ".intent-cli", "reviews", "G9.request.json")));
        Assert.Equal("https://github.com/J-Tech-Japan/intent-system/pull/45", artifact.LinkedPr);
    }

    [Fact]
    public void Execute_GivenReviewCommentCommand_DispatchesToReviewCommentRenderer()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateReviewCommentQueueState()));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "reviews", "G10.request.json"),
            CreateReviewCommentRequestJson());
        tempDirectory.CreateFile(
            Path.Combine("repo", "prepared-comment.md"),
            "repair in place");
        using var writer = new StringWriter();
        var originalFactory = ReviewCommentCommand.PublisherFactory;
        var originalTimestampFactory = ReviewCommentCommand.TimestampFactory;

        try
        {
            ReviewCommentCommand.PublisherFactory = () => new FakeReviewCommentPublisher();
            ReviewCommentCommand.TimestampFactory = () => DateTimeOffset.Parse("2026-04-04T04:40:00Z");

            var exitCode = CommandRouter.Execute(
                ["review", "comment", "G10", "--from-file", "prepared-comment.md"],
                CreateContext(repoRoot),
                writer);

            Assert.Equal(0, exitCode);
            Assert.Contains("Review comment posted for G10", writer.ToString(), StringComparison.Ordinal);
            Assert.True(File.Exists(Path.Combine(repoRoot, ".intent-cli", "reviews", "G10.comment.json")));
        }
        finally
        {
            ReviewCommentCommand.PublisherFactory = originalFactory;
            ReviewCommentCommand.TimestampFactory = originalTimestampFactory;
        }
    }

    [Fact]
    public void Execute_GivenReviewAcceptCommand_DispatchesToReviewAcceptRenderer()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateDirectory(Path.Combine("repo", "submodules", "child-repo"));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateReviewAcceptQueueState()));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G12", "packet.yaml"),
            CreateReviewAcceptPacketYaml());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "runs.jsonl"),
            CreateReviewAcceptRunLog());
        using var writer = new StringWriter();
        var originalClientFactory = ReviewAcceptCommand.AcceptClientFactory;
        var originalGitFactory = ReviewAcceptCommand.GitCommandRunnerFactory;
        var originalTimestampFactory = ReviewAcceptCommand.TimestampFactory;

        try
        {
            ReviewAcceptCommand.AcceptClientFactory = () => new FakeReviewAcceptClient();
            ReviewAcceptCommand.GitCommandRunnerFactory = () => new FakeReviewAcceptGitRunner();
            ReviewAcceptCommand.TimestampFactory = () => DateTimeOffset.Parse("2026-04-05T01:02:03Z");

            var exitCode = CommandRouter.Execute(["review", "accept", "G12"], CreateContext(repoRoot), writer);

            Assert.Equal(0, exitCode);
            Assert.Contains("Review accepted for G12", writer.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            ReviewAcceptCommand.AcceptClientFactory = originalClientFactory;
            ReviewAcceptCommand.GitCommandRunnerFactory = originalGitFactory;
            ReviewAcceptCommand.TimestampFactory = originalTimestampFactory;
        }
    }

    [Fact]
    public void Execute_GivenClarifyOpenCommand_DispatchesToClarifyOpenRenderer()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateClarifyOpenQueueState()));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G22", "packet.yaml"),
            CreateClarifyOpenPacketYaml());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G22", "review-context.md"),
            CreateClarifyOpenReviewContextMarkdown());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "runs.jsonl"),
            string.Empty);
        using var writer = new StringWriter();
        var originalTimestampFactory = ClarifyOpenCommand.TimestampFactory;

        try
        {
            ClarifyOpenCommand.TimestampFactory = () => DateTimeOffset.Parse("2026-04-11T06:10:00Z");

            var exitCode = CommandRouter.Execute(["clarify", "open", "G22"], CreateContext(repoRoot), writer);

            Assert.Equal(0, exitCode);
            Assert.Contains("Clarification opened for G22", writer.ToString(), StringComparison.Ordinal);
            Assert.True(File.Exists(Path.Combine(repoRoot, ".intent-cli", "clarifications", "G22", "request.json")));
        }
        finally
        {
            ClarifyOpenCommand.TimestampFactory = originalTimestampFactory;
        }
    }

    [Fact]
    public void Execute_GivenClarifyListCommand_DispatchesToClarifyListRenderer()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateClarifyOpenQueueState()));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "clarifications", "G22", "request.json"),
            ClarificationSerializer.Serialize(CreateClarifyListItem()));
        using var writer = new StringWriter();

        var exitCode = CommandRouter.Execute(["clarify", "list"], CreateContext(repoRoot), writer);

        Assert.Equal(0, exitCode);
        Assert.Contains("Open clarifications:", writer.ToString(), StringComparison.Ordinal);
        Assert.Contains("Execution unit: G22", writer.ToString(), StringComparison.Ordinal);
    }

    private static CliContext CreateContext(string repoRoot, string? parentIntentRepoRoot = null)
    {
        return new CliContext
        {
            RepoRoot = repoRoot,
            Config = new CliConfig
            {
                Project = new ProjectConfig
                {
                    Domain = "intent-cli",
                    WorkflowEngine = "takt",
                    ArtifactRoot = ".intent-cli",
                    ParentIntentRepoRoot = parentIntentRepoRoot ?? string.Empty
                }
            }
        };
    }

    private static QueueState CreateQueueState()
    {
        return new QueueState
        {
            SchemaVersion = "1",
            UpdatedAt = DateTimeOffset.Parse("2026-04-03T10:12:34Z"),
            Items =
            [
                new QueueItem
                {
                    ExecutionUnit = "A2",
                    Title = "CLI shell baseline",
                    State = QueueItemState.Review,
                    Dependencies = ["A1"],
                    BlockedBy = [],
                    ClarificationReturnPath = ".takt/runs/20260403-101234-issue-29-g1-cli-shell-and-root/context/task/order.md",
                    PacketPaths = new PacketPaths
                    {
                        Implementation = ".intent-cli/issues/a2/implementation.md",
                        ReviewContext = ".intent-cli/issues/a2/review-context.md",
                        Yaml = ".intent-cli/issues/a2/packet.yaml"
                    },
                    WorkerRole = "coder",
                    ReviewRole = "reviewer",
                    Priority = "high"
                },
                new QueueItem
                {
                    ExecutionUnit = "A3",
                    Title = "Queue read commands",
                    State = QueueItemState.Queued,
                    Dependencies = [],
                    BlockedBy = [],
                    ClarificationReturnPath = ".takt/runs/20260403-101234-issue-33-g3-queue-show-and-next/context/task/order.md",
                    PacketPaths = new PacketPaths
                    {
                        Implementation = ".intent-cli/issues/A3/implementation.md",
                        ReviewContext = ".intent-cli/issues/A3/review-context.md",
                        Yaml = ".intent-cli/issues/A3/packet.yaml"
                    },
                    WorkerRole = "coder",
                    ReviewRole = "reviewer",
                    Priority = "normal"
                }
            ]
        };
    }

    private static QueueState CreateWorkflowQueueState()
    {
        return new QueueState
        {
            SchemaVersion = "1",
            UpdatedAt = DateTimeOffset.Parse("2026-04-03T10:12:34Z"),
            Items =
            [
                new QueueItem
                {
                    ExecutionUnit = "C2",
                    Title = "Workflow render command",
                    State = QueueItemState.Queued,
                    Dependencies = ["A1"],
                    BlockedBy = [],
                    ClarificationReturnPath = "intents/intent-cli/clarifications/open.md",
                    PacketPaths = new PacketPaths
                    {
                        Implementation = ".intent-cli/issues/C2/implementation.md",
                        ReviewContext = ".intent-cli/issues/C2/review-context.md",
                        Yaml = ".intent-cli/issues/C2/packet.yaml"
                    },
                    WorkerRole = "coder",
                    ReviewRole = "reviewer",
                    Priority = "high"
                }
            ]
        };
    }

    private static QueueState CreateRunSubmitQueueState()
    {
        return new QueueState
        {
            SchemaVersion = "1",
            UpdatedAt = DateTimeOffset.Parse("2026-04-03T10:12:34Z"),
            Items =
            [
                new QueueItem
                {
                    ExecutionUnit = "G14",
                    Title = "[G14] Run Start Command",
                    State = QueueItemState.Active,
                    Dependencies = [],
                    BlockedBy = [],
                    ClarificationReturnPath = "intents/intent-cli/clarifications/open.md",
                    PacketPaths = new PacketPaths
                    {
                        Implementation = ".intent-cli/issues/G14/implementation.md",
                        ReviewContext = ".intent-cli/issues/G14/review-context.md",
                        Yaml = ".intent-cli/issues/G14/packet.yaml"
                    },
                    LinkedIssue = new LinkedIssue
                    {
                        Repo = "J-Tech-Japan/intent-system",
                        Number = 56,
                        Url = "https://github.com/J-Tech-Japan/intent-system/issues/56"
                    },
                    WorkerRole = "coder",
                    ReviewRole = "reviewer",
                    Priority = "high"
                }
            ]
        };
    }

    private static QueueState CreateQueueDispatchQueueState()
    {
        return new QueueState
        {
            SchemaVersion = "1",
            UpdatedAt = DateTimeOffset.Parse("2026-04-03T10:12:34Z"),
            Items =
            [
                new QueueItem
                {
                    ExecutionUnit = "G13",
                    Title = "Queue dispatch command",
                    State = QueueItemState.Queued,
                    Dependencies = [],
                    BlockedBy = [],
                    ClarificationReturnPath = "intents/intent-cli/clarifications/open.md",
                    PacketPaths = new PacketPaths
                    {
                        Implementation = ".intent-cli/issues/G13/implementation.md",
                        ReviewContext = ".intent-cli/issues/G13/review-context.md",
                        Yaml = ".intent-cli/issues/G13/packet.yaml"
                    },
                    WorkerRole = "coder",
                    ReviewRole = "reviewer",
                    Priority = "high"
                }
            ]
        };
    }

    private static QueueState CreateQueueEnqueueQueueState()
    {
        return new QueueState
        {
            SchemaVersion = "1",
            UpdatedAt = DateTimeOffset.Parse("2026-04-06T10:00:00Z"),
            Items =
            [
                new QueueItem
                {
                    ExecutionUnit = "G3",
                    Title = "Queue read commands",
                    State = QueueItemState.Completed,
                    Dependencies = [],
                    BlockedBy = [],
                    ClarificationReturnPath = "intents/intent-cli/clarifications/open.md",
                    PacketPaths = new PacketPaths
                    {
                        Implementation = ".intent-cli/issues/G3/implementation.md",
                        ReviewContext = ".intent-cli/issues/G3/review-context.md",
                        Yaml = ".intent-cli/issues/G3/packet.yaml"
                    },
                    WorkerRole = "coder",
                    ReviewRole = "reviewer",
                    Priority = "high"
                }
            ]
        };
    }

    private static string CreateIntakeEnqueuePacketYaml(string executionUnit)
    {
        return $"""
        execution_unit: {executionUnit}
        implementation_issue:
          issue_title: "{executionUnit} Queue Item"
          goal: "Enqueue generated issue artifact into queue artifacts."
          in_scope:
            - "queue insertion"
          out_of_scope:
            - "workflow execution"
          target_repo: "submodules/intent-system"
          target_path: "."
          target_part: "cli intake enqueue command"
          dependencies:
            - "G3"
          technical_baseline:
            - "C# / .NET"
          project_local_guidance:
            - "AGENTS.md"
          intent_baseline:
            - "intake enqueue stays thin"
          acceptance_criteria:
            - "queue item inserted"
          verification:
            - "tests-passing"

        review:
          summarize_first: true
          require_explicit_diff_check: true
          require_explicit_scope_check: true
          require_explicit_contract_check: true
          required_checks:
            - "intake enqueue remains thin"
        """;
    }

    private static QueueState CreateRunRereviewQueueState()
    {
        return new QueueState
        {
            SchemaVersion = "1",
            UpdatedAt = DateTimeOffset.Parse("2026-04-05T09:12:34Z"),
            Items =
            [
                new QueueItem
                {
                    ExecutionUnit = "G16",
                    Title = "Run rereview command",
                    State = QueueItemState.Fixing,
                    Dependencies = ["G15"],
                    BlockedBy = [],
                    ClarificationReturnPath = "intents/intent-cli/clarifications/open.md",
                    PacketPaths = new PacketPaths
                    {
                        Implementation = ".intent-cli/issues/G16/implementation.md",
                        ReviewContext = ".intent-cli/issues/G16/review-context.md",
                        Yaml = ".intent-cli/issues/G16/packet.yaml"
                    },
                    WorkerRole = "coder",
                    ReviewRole = "reviewer",
                    Priority = "high"
                }
            ]
        };
    }

    private static QueueState CreateRunResumeQueueState()
    {
        return new QueueState
        {
            SchemaVersion = "1",
            UpdatedAt = DateTimeOffset.Parse("2026-04-06T08:12:34Z"),
            Items =
            [
                new QueueItem
                {
                    ExecutionUnit = "G17",
                    Title = "Run resume command",
                    State = QueueItemState.Active,
                    Dependencies = ["G16"],
                    BlockedBy = [],
                    ClarificationReturnPath = "intents/intent-cli/clarifications/open.md",
                    PacketPaths = new PacketPaths
                    {
                        Implementation = ".intent-cli/issues/G17/implementation.md",
                        ReviewContext = ".intent-cli/issues/G17/review-context.md",
                        Yaml = ".intent-cli/issues/G17/packet.yaml"
                    },
                    LinkedIssue = new LinkedIssue
                    {
                        Repo = "J-Tech-Japan/intent-system",
                        Number = 62,
                        Url = "https://github.com/J-Tech-Japan/intent-system/issues/62"
                    },
                    WorkerRole = "coder",
                    ReviewRole = "reviewer",
                    Priority = "high"
                }
            ]
        };
    }

    private static QueueState CreateRunLogQueueState()
    {
        return new QueueState
        {
            SchemaVersion = "1",
            UpdatedAt = DateTimeOffset.Parse("2026-04-07T08:12:34Z"),
            Items =
            [
                new QueueItem
                {
                    ExecutionUnit = "G18",
                    Title = "Run log command",
                    State = QueueItemState.Fixing,
                    Dependencies = ["G17"],
                    BlockedBy = [],
                    ClarificationReturnPath = "intents/intent-cli/clarifications/open.md",
                    PacketPaths = new PacketPaths
                    {
                        Implementation = ".intent-cli/issues/G18/implementation.md",
                        ReviewContext = ".intent-cli/issues/G18/review-context.md",
                        Yaml = ".intent-cli/issues/G18/packet.yaml"
                    },
                    LinkedIssue = new LinkedIssue
                    {
                        Repo = "J-Tech-Japan/intent-system",
                        Number = 64,
                        Url = "https://github.com/J-Tech-Japan/intent-system/issues/64"
                    },
                    WorkerRole = "coder",
                    ReviewRole = "reviewer",
                    Priority = "high"
                }
            ]
        };
    }

    private static QueueState CreateRunImplementQueueState()
    {
        return new QueueState
        {
            SchemaVersion = "1",
            UpdatedAt = DateTimeOffset.Parse("2026-04-08T08:12:34Z"),
            Items =
            [
                new QueueItem
                {
                    ExecutionUnit = "G19",
                    Title = "Run implement command",
                    State = QueueItemState.Active,
                    Dependencies = ["G18"],
                    BlockedBy = [],
                    ClarificationReturnPath = "intents/intent-cli/clarifications/open.md",
                    PacketPaths = new PacketPaths
                    {
                        Implementation = ".intent-cli/issues/G19/implementation.md",
                        ReviewContext = ".intent-cli/issues/G19/review-context.md",
                        Yaml = ".intent-cli/issues/G19/packet.yaml"
                    },
                    LinkedIssue = new LinkedIssue
                    {
                        Repo = "J-Tech-Japan/intent-system",
                        Number = 66,
                        Url = "https://github.com/J-Tech-Japan/intent-system/issues/66"
                    },
                    WorkerRole = "coder",
                    ReviewRole = "reviewer",
                    Priority = "high"
                }
            ]
        };
    }

    private static QueueState CreateRunFixQueueState()
    {
        return new QueueState
        {
            SchemaVersion = "1",
            UpdatedAt = DateTimeOffset.Parse("2026-04-09T09:42:34Z"),
            Items =
            [
                new QueueItem
                {
                    ExecutionUnit = "G20",
                    Title = "Run fix command",
                    State = QueueItemState.Fixing,
                    Dependencies = ["G19"],
                    BlockedBy = [],
                    ClarificationReturnPath = "intents/intent-cli/clarifications/open.md",
                    PacketPaths = new PacketPaths
                    {
                        Implementation = ".intent-cli/issues/G20/implementation.md",
                        ReviewContext = ".intent-cli/issues/G20/review-context.md",
                        Yaml = ".intent-cli/issues/G20/packet.yaml"
                    },
                    LinkedIssue = new LinkedIssue
                    {
                        Repo = "J-Tech-Japan/intent-system",
                        Number = 68,
                        Url = "https://github.com/J-Tech-Japan/intent-system/issues/68"
                    },
                    WorkerRole = "coder",
                    ReviewRole = "reviewer",
                    Priority = "high"
                }
            ]
        };
    }

    private static QueueState CreateClarifyOpenQueueState()
    {
        return new QueueState
        {
            SchemaVersion = "1",
            UpdatedAt = DateTimeOffset.Parse("2026-04-11T06:05:00Z"),
            Items =
            [
                new QueueItem
                {
                    ExecutionUnit = "G22",
                    Title = "Clarify open command",
                    State = QueueItemState.Review,
                    Dependencies = ["G21"],
                    BlockedBy = [],
                    ClarificationReturnPath = "intents/intent-cli/clarifications/open.md",
                    PacketPaths = new PacketPaths
                    {
                        Implementation = ".intent-cli/issues/G22/implementation.md",
                        ReviewContext = ".intent-cli/issues/G22/review-context.md",
                        Yaml = ".intent-cli/issues/G22/packet.yaml"
                    },
                    WorkerRole = "coder",
                    ReviewRole = "reviewer",
                    Priority = "high"
                }
            ]
        };
    }

    private static QueueState CreateRunResubmitQueueState()
    {
        return new QueueState
        {
            SchemaVersion = "1",
            UpdatedAt = DateTimeOffset.Parse("2026-04-10T07:12:34Z"),
            Items =
            [
                new QueueItem
                {
                    ExecutionUnit = "G21",
                    Title = "Run resubmit command",
                    State = QueueItemState.Fixing,
                    Dependencies = ["G20"],
                    BlockedBy = [],
                    ClarificationReturnPath = "intents/intent-cli/clarifications/open.md",
                    PacketPaths = new PacketPaths
                    {
                        Implementation = ".intent-cli/issues/G21/implementation.md",
                        ReviewContext = ".intent-cli/issues/G21/review-context.md",
                        Yaml = ".intent-cli/issues/G21/packet.yaml"
                    },
                    LinkedIssue = new LinkedIssue
                    {
                        Repo = "J-Tech-Japan/intent-system",
                        Number = 70,
                        Url = "https://github.com/J-Tech-Japan/intent-system/issues/70"
                    },
                    WorkerRole = "coder",
                    ReviewRole = "reviewer",
                    Priority = "high"
                }
            ]
        };
    }

    private static QueueState CreateReviewQueueState()
    {
        return new QueueState
        {
            SchemaVersion = "1",
            UpdatedAt = DateTimeOffset.Parse("2026-04-03T10:12:34Z"),
            Items =
            [
                new QueueItem
                {
                    ExecutionUnit = "G9",
                    Title = "Review run command",
                    State = QueueItemState.Review,
                    Dependencies = ["G7"],
                    BlockedBy = [],
                    ClarificationReturnPath = "intents/intent-cli/clarifications/open.md",
                    PacketPaths = new PacketPaths
                    {
                        Implementation = ".intent-cli/issues/G9/implementation.md",
                        ReviewContext = ".intent-cli/issues/G9/review-context.md",
                        Yaml = ".intent-cli/issues/G9/packet.yaml"
                    },
                    WorkerRole = "coder",
                    ReviewRole = "reviewer",
                    Priority = "high"
                }
            ]
        };
    }

    private static QueueState CreateRunStartQueueState()
    {
        return new QueueState
        {
            SchemaVersion = "1",
            UpdatedAt = DateTimeOffset.Parse("2026-04-03T10:12:34Z"),
            Items =
            [
                new QueueItem
                {
                    ExecutionUnit = "G14",
                    Title = "Run start command",
                    State = QueueItemState.Queued,
                    Dependencies = [],
                    BlockedBy = [],
                    ClarificationReturnPath = "intents/intent-cli/clarifications/open.md",
                    PacketPaths = new PacketPaths
                    {
                        Implementation = ".intent-cli/issues/G14/implementation.md",
                        ReviewContext = ".intent-cli/issues/G14/review-context.md",
                        Yaml = ".intent-cli/issues/G14/packet.yaml"
                    },
                    LinkedIssue = new LinkedIssue
                    {
                        Repo = "J-Tech-Japan/intent-system",
                        Number = 56,
                        Url = "https://github.com/J-Tech-Japan/intent-system/issues/56"
                    },
                    WorkerRole = "coder",
                    ReviewRole = "reviewer",
                    Priority = "high"
                }
            ]
        };
    }

    private static QueueState CreateReviewCommentQueueState()
    {
        return new QueueState
        {
            SchemaVersion = "1",
            UpdatedAt = DateTimeOffset.Parse("2026-04-03T10:12:34Z"),
            Items =
            [
                new QueueItem
                {
                    ExecutionUnit = "G10",
                    Title = "Review comment command",
                    State = QueueItemState.Review,
                    Dependencies = ["G9"],
                    BlockedBy = [],
                    ClarificationReturnPath = "intents/intent-cli/clarifications/open.md",
                    PacketPaths = new PacketPaths
                    {
                        Implementation = ".intent-cli/issues/G10/implementation.md",
                        ReviewContext = ".intent-cli/issues/G10/review-context.md",
                        Yaml = ".intent-cli/issues/G10/packet.yaml"
                    },
                    WorkerRole = "coder",
                    ReviewRole = "reviewer",
                    Priority = "high"
                }
            ]
        };
    }

    private static QueueState CreateReviewAcceptQueueState()
    {
        return new QueueState
        {
            SchemaVersion = "1",
            UpdatedAt = DateTimeOffset.Parse("2026-04-03T10:12:34Z"),
            Items =
            [
                new QueueItem
                {
                    ExecutionUnit = "G12",
                    Title = "Review accept command",
                    State = QueueItemState.Review,
                    Dependencies = ["G10"],
                    BlockedBy = [],
                    ClarificationReturnPath = "intents/intent-cli/clarifications/open.md",
                    PacketPaths = new PacketPaths
                    {
                        Implementation = ".intent-cli/issues/G12/implementation.md",
                        ReviewContext = ".intent-cli/issues/G12/review-context.md",
                        Yaml = ".intent-cli/issues/G12/packet.yaml"
                    },
                    WorkerRole = "coder",
                    ReviewRole = "reviewer",
                    Priority = "high"
                }
            ]
        };
    }

    private static string CreateWorkflowPacketYaml()
    {
        return """
        implementation_issue_packet:
          issue_title: "[C2] Workflow Render Command"
          issue_kind: "feature"
          source_execution_unit: "C2"
          goal: "Render workflow definition artifact from queue and packet sources."
          in_scope:
            - "cli workflow render command"
          out_of_scope:
            - "workflow execution"
          target_repo: "submodules/intent-system"
          target_path: "."
          target_part: "cli workflow render command"
          dependencies:
            - "G1"
            - "B2"
            - "C1"
            - "C2"
          technical_baseline:
            - "C# / .NET"
          project_local_guide:
            - "AGENTS.md"
          intent_baseline:
            - "C1 and C2 are fixed baselines"
          intent_references:
            - "ICL.E.SLICES"
          rules_and_specs:
            - "intents/intent-cli/specs/07-workflow-definition-and-takt-adapter.md"
          acceptance_criteria:
            - "workflow render writes workflow artifact"
          verification_evidence:
            - "contract-reviewed"
            - "tests-passing"
            - "acceptance-criteria-checked"
          review_mode: "deterministic-review"
          completion_action: "wait-for-deterministic-review"
          landing_policy: "merge-after-review"
        
        review_context_packet:
          source_execution_unit: "C2"
          parent_intent_root: "intents/intent-cli/intent-tree/00-map.md"
          intent_references:
            - "ICL.E.SLICES"
          rules_and_specs:
            - "intents/intent-cli/specs/07-workflow-definition-and-takt-adapter.md"
          acceptance_criteria:
            - "workflow render writes workflow artifact"
          deterministic_review_checks:
            - "definition shape stays canonical"
          clarification_return_path: "intents/intent-cli/clarifications/open.md"
        """;
    }

    private static string CreateRunSubmitPacketYaml()
    {
        return """
        implementation_issue_packet:
          issue_title: "G15 Run Submit Command"
          issue_kind: "feature"
          source_execution_unit: "G15"
          goal: "Submit active worktree for review."
          in_scope:
            - "run submit command"
          out_of_scope:
            - "review execution"
          target_repo: "submodules/intent-system"
          target_path: "."
          target_part: "cli run submit command"
          dependencies:
            - "G14"
          technical_baseline:
            - "C# / .NET"
          project_local_guide:
            - "AGENTS.md"
          intent_baseline:
            - "run submit stays thin"
          intent_references:
            - "ICL.P.PRODUCT_GOAL"
          rules_and_specs:
            - "intents/intent-cli/specs/08-config-and-run-model.md"
          acceptance_criteria:
            - "draft pr created"
          verification_evidence:
            - "tests-passing"
          review_mode: "deterministic-review"
          completion_action: "wait-for-deterministic-review"
          landing_policy: "merge-after-review"
        
        review_context_packet:
          source_execution_unit: "G15"
          parent_intent_root: "intents/intent-cli/intent-tree/00-map.md"
          intent_references:
            - "ICL.P.PRODUCT_GOAL"
          rules_and_specs:
            - "intents/intent-cli/specs/08-config-and-run-model.md"
          acceptance_criteria:
            - "draft pr created"
          deterministic_review_checks:
            - "run submit remains thin"
          clarification_return_path: "intents/intent-cli/clarifications/open.md"
        """;
    }

    private static string CreateQueueDispatchPacketYaml()
    {
        return """
        implementation_issue_packet:
          issue_title: "[G13] Queue Dispatch Command"
          issue_kind: "feature"
          source_execution_unit: "G13"
          goal: "Dispatch queue item into GitHub issue."
          in_scope:
            - "queue dispatch command"
          out_of_scope:
            - "branch creation"
          target_repo: "submodules/intent-system"
          target_path: "."
          target_part: "cli queue dispatch command"
          dependencies:
            - "G3"
          technical_baseline:
            - "C# / .NET"
          project_local_guide:
            - "AGENTS.md"
          intent_baseline:
            - "dispatch stays thin"
          intent_references:
            - "ICL.P.PRODUCT_GOAL"
          rules_and_specs:
            - "intents/rules/issue-lifecycle-and-landing.md"
          acceptance_criteria:
            - "issue created"
          verification_evidence:
            - "tests-passing"
          review_mode: "deterministic-review"
          completion_action: "wait-for-deterministic-review"
          landing_policy: "merge-after-review"
        
        review_context_packet:
          source_execution_unit: "G13"
          parent_intent_root: "intents/intent-cli/intent-tree/00-map.md"
          intent_references:
            - "ICL.P.PRODUCT_GOAL"
          rules_and_specs:
            - "intents/rules/issue-lifecycle-and-landing.md"
          acceptance_criteria:
            - "issue created"
          deterministic_review_checks:
            - "dispatch remains thin"
          clarification_return_path: "intents/intent-cli/clarifications/open.md"
        """;
    }

    private static string CreateQueueEnqueuePacketYaml()
    {
        return """
        execution_unit: G38
        implementation_issue:
          issue_title: "G38 Queue Enqueue Command"
          goal: "Enqueue queue item from packet artifact."
          in_scope:
            - "queue enqueue command"
          out_of_scope:
            - "child issue creation"
          target_repo: "submodules/intent-system"
          target_path: "."
          target_part: "cli queue enqueue command"
          dependencies:
            - "G3"
          technical_baseline:
            - "C# / .NET"
          project_local_guide:
            - "AGENTS.md"
          intent_baseline:
            - "enqueue stays thin"
          intent_references:
            - "ICL.P.PRODUCT_GOAL"
          rules_and_specs:
            - "intents/intent-cli/specs/03-queue-json-and-jsonl-schema.md"
          acceptance_criteria:
            - "queue item inserted"
          verification_evidence:
            - "tests-passing"

        review:
          summarize_first: true
          require_explicit_diff_check: true
          require_explicit_scope_check: true
          require_explicit_contract_check: true
          required_checks:
            - "enqueue remains thin"
        """;
    }

    private static string CreateRunRereviewRunLog()
    {
        return """
        {"ts":"2026-04-05T09:00:00Z","execution_unit":"G16","event":"review","by":"intent-cli","linked_pr":"https://github.com/J-Tech-Japan/intent-system/pull/60"}
        {"ts":"2026-04-05T09:10:00Z","execution_unit":"G16","event":"fix-requested","by":"intent-cli","comment_ref":"https://github.com/J-Tech-Japan/intent-system/pull/60#issuecomment-1"}
        {"ts":"2026-04-05T09:30:00Z","execution_unit":"G16","event":"review","by":"intent-cli","linked_pr":"https://github.com/J-Tech-Japan/intent-system/pull/61"}
        """ + Environment.NewLine;
    }

    private static string CreateRunResumePacketYaml()
    {
        return """
        implementation_issue_packet:
          issue_title: "G17 Run Resume Command"
          issue_kind: "feature"
          source_execution_unit: "G17"
          goal: "Render resumable context for an existing run."
          in_scope:
            - "run resume command"
          out_of_scope:
            - "queue mutation"
          target_repo: "submodules/intent-system"
          target_path: "."
          target_part: "cli run resume command"
          dependencies:
            - "G16"
          technical_baseline:
            - "C# / .NET"
          project_local_guide:
            - "AGENTS.md"
          intent_baseline:
            - "run resume stays read-only"
          intent_references:
            - "ICL.P.PRODUCT_GOAL"
          rules_and_specs:
            - "intents/intent-cli/specs/08-config-and-run-model.md"
          acceptance_criteria:
            - "resumable context displayed"
          verification_evidence:
            - "tests-passing"
          review_mode: "deterministic-review"
          completion_action: "wait-for-deterministic-review"
          landing_policy: "merge-after-review"

        review_context_packet:
          source_execution_unit: "G17"
          parent_intent_root: "intents/intent-cli/intent-tree/00-map.md"
          intent_references:
            - "ICL.P.PRODUCT_GOAL"
          rules_and_specs:
            - "intents/intent-cli/specs/08-config-and-run-model.md"
          acceptance_criteria:
            - "resumable context displayed"
          deterministic_review_checks:
            - "run resume remains read-only"
          clarification_return_path: "intents/intent-cli/clarifications/open.md"
        """;
    }

    private static string CreateRunResumeRunLog()
    {
        return """
        {"ts":"2026-04-06T08:00:00Z","execution_unit":"G17","event":"review","by":"intent-cli","linked_pr":"https://github.com/J-Tech-Japan/intent-system/pull/62"}
        {"ts":"2026-04-06T08:20:00Z","execution_unit":"G17","event":"fix-requested","by":"intent-cli","comment_ref":"https://github.com/J-Tech-Japan/intent-system/pull/62#issuecomment-1"}
        {"ts":"2026-04-06T08:30:00Z","execution_unit":"G17","event":"review","by":"intent-cli","linked_pr":"https://github.com/J-Tech-Japan/intent-system/pull/63"}
        """ + Environment.NewLine;
    }

    private static string CreateRunLogCommandRunLog()
    {
        return """
        {"ts":"2026-04-07T08:00:00Z","execution_unit":"G18","event":"issue-created","by":"intent-cli","linked_issue":"https://github.com/J-Tech-Japan/intent-system/issues/64"}
        {"ts":"2026-04-07T08:10:00Z","execution_unit":"G18","event":"activated","by":"intent-cli"}
        {"ts":"2026-04-07T08:20:00Z","execution_unit":"G18","event":"review","by":"intent-cli","linked_pr":"https://github.com/J-Tech-Japan/intent-system/pull/65"}
        """ + Environment.NewLine;
    }

    private static string CreateRunImplementPacketYaml()
    {
        return """
        implementation_issue_packet:
          issue_title: "[G19] Run Implement Command"
          issue_kind: "feature"
          source_execution_unit: "G19"
          goal: "Generate an execution worker handoff artifact."
          in_scope:
            - "run implement command"
            - "handoff artifact generation"
          out_of_scope:
            - "queue mutation"
            - "worker start"
          target_repo: "submodules/intent-system"
          target_path: "."
          target_part: "cli run implement command"
          dependencies:
            - "G18"
          technical_baseline:
            - "C# / .NET"
          project_local_guide:
            - "AGENTS.md"
          intent_baseline:
            - "run implement stays handoff-only"
          intent_references:
            - "ICL.P.PRODUCT_GOAL"
          rules_and_specs:
            - "intents/intent-cli/specs/08-config-and-run-model.md"
          acceptance_criteria:
            - "handoff artifact generated"
          verification_evidence:
            - "tests-passing"
          review_mode: "deterministic-review"
          completion_action: "wait-for-deterministic-review"
          landing_policy: "merge-after-review"

        review_context_packet:
          source_execution_unit: "G19"
          parent_intent_root: "intents/intent-cli/intent-tree/00-map.md"
          intent_references:
            - "ICL.P.PRODUCT_GOAL"
          rules_and_specs:
            - "intents/intent-cli/specs/08-config-and-run-model.md"
          acceptance_criteria:
            - "handoff artifact generated"
          deterministic_review_checks:
            - "run implement command remains handoff-only"
          clarification_return_path: "intents/intent-cli/clarifications/open.md"
        """;
    }

    private static string CreateRunFixPacketYaml()
    {
        return """
        implementation_issue_packet:
          issue_title: "[G20] Run Fix Command"
          issue_kind: "feature"
          source_execution_unit: "G20"
          goal: "Generate a repair worker handoff artifact."
          in_scope:
            - "run fix command"
            - "repair handoff artifact generation"
          out_of_scope:
            - "queue mutation"
            - "worker start"
          target_repo: "submodules/intent-system"
          target_path: "."
          target_part: "cli run fix command"
          dependencies:
            - "G19"
          technical_baseline:
            - "C# / .NET"
          project_local_guide:
            - "AGENTS.md"
          intent_baseline:
            - "run fix stays handoff-only"
          intent_references:
            - "ICL.P.PRODUCT_GOAL"
          rules_and_specs:
            - "intents/rules/review-recovery-and-retry.md"
          acceptance_criteria:
            - "repair handoff artifact generated"
          verification_evidence:
            - "tests-passing"
          review_mode: "deterministic-review"
          completion_action: "wait-for-deterministic-review"
          landing_policy: "merge-after-review"

        review_context_packet:
          source_execution_unit: "G20"
          parent_intent_root: "intents/intent-cli/intent-tree/00-map.md"
          intent_references:
            - "ICL.P.PRODUCT_GOAL"
          rules_and_specs:
            - "intents/rules/review-recovery-and-retry.md"
          acceptance_criteria:
            - "repair handoff artifact generated"
          deterministic_review_checks:
            - "run fix command remains handoff-only"
          clarification_return_path: "intents/intent-cli/clarifications/open.md"
        """;
    }

    private static string CreateClarifyOpenPacketYaml()
    {
        return """
        implementation_issue_packet:
          issue_title: "[G22] Clarify Open Command"
          issue_kind: "feature"
          source_execution_unit: "G22"
          goal: "Open a clarification request for the current queue loop."
          in_scope:
            - "clarify open command"
          out_of_scope:
            - "clarify answer"
          target_repo: "submodules/intent-system"
          target_path: "."
          target_part: "cli clarify open command"
          dependencies:
            - "G8"
          technical_baseline:
            - "C# / .NET"
          project_local_guide:
            - "AGENTS.md"
          intent_baseline:
            - "clarify open stays entry-only"
          intent_references:
            - "ICL.P.PRODUCT_GOAL"
          rules_and_specs:
            - "intents/intent-cli/specs/06-interview-and-clarification-artifact-contract.md"
          acceptance_criteria:
            - "clarification artifact generated"
          verification_evidence:
            - "dotnet test IntentSystem.sln"
          review_mode: "deterministic-review"
          completion_action: "wait-for-deterministic-review"
          landing_policy: "merge-after-review"

        review_context_packet:
          source_execution_unit: "G22"
          parent_intent_root: "intents/intent-cli/intent-tree/00-map.md"
          intent_references:
            - "ICL.P.PRODUCT_GOAL"
          rules_and_specs:
            - "intents/intent-cli/specs/06-interview-and-clarification-artifact-contract.md"
          acceptance_criteria:
            - "clarification artifact generated"
          deterministic_review_checks:
            - "clarify open command remains entry-only"
          clarification_return_path: "intents/intent-cli/clarifications/open.md"
        """;
    }

    private static string CreateRunResubmitPacketYaml()
    {
        return """
        implementation_issue_packet:
          issue_title: "[G21] Run Resubmit Command"
          issue_kind: "feature"
          source_execution_unit: "G21"
          goal: "Push the repair branch and append a resubmitted event."
          in_scope:
            - "run resubmit command"
            - "repair branch push"
          out_of_scope:
            - "queue state mutation"
            - "PR creation"
          target_repo: "submodules/intent-system"
          target_path: "."
          target_part: "cli run resubmit command"
          dependencies:
            - "G20"
          technical_baseline:
            - "C# / .NET"
          project_local_guide:
            - "AGENTS.md"
          intent_baseline:
            - "run resubmit stays push-only"
          intent_references:
            - "ICL.P.PRODUCT_GOAL"
          rules_and_specs:
            - "intents/intent-cli/specs/05-intent-cli-surface.md"
          acceptance_criteria:
            - "resubmitted event appended"
          verification_evidence:
            - "dotnet test IntentSystem.sln"
          review_mode: "deterministic-review"
          completion_action: "wait-for-deterministic-review"
          landing_policy: "merge-after-review"

        review_context_packet:
          source_execution_unit: "G21"
          parent_intent_root: "intents/intent-cli/intent-tree/00-map.md"
          intent_references:
            - "ICL.P.PRODUCT_GOAL"
          rules_and_specs:
            - "intents/intent-cli/specs/05-intent-cli-surface.md"
          acceptance_criteria:
            - "resubmitted event appended"
          deterministic_review_checks:
            - "run resubmit remains push-only"
          clarification_return_path: "intents/intent-cli/clarifications/open.md"
        """;
    }

    private static string CreateRunImplementReviewContextMarkdown()
    {
        return """
        # Execution Unit

        `G19`

        # Goal

        `intent-cli run implement <execution-unit>` を working command にする。

        # Acceptance Criteria

        - handoff artifact generated

        # Deterministic Review Checks

        - run implement command remains handoff-only

        # Expected Evidence

        - dotnet test IntentSystem.sln
        """;
    }

    private static string CreateRunFixReviewContextMarkdown()
    {
        return """
        # Execution Unit

        `G20`

        # Goal

        `intent-cli run fix <execution-unit>` を working command にする。

        # Acceptance Criteria

        - repair handoff artifact generated

        # Deterministic Review Checks

        - run fix command remains handoff-only

        # Expected Evidence

        - dotnet test IntentSystem.sln
        """;
    }

    private static string CreateClarifyOpenReviewContextMarkdown()
    {
        return """
        # Execution Unit

        `G22`

        # Acceptance Criteria

        - clarification artifact generated

        # Deterministic Review Checks

        - clarify open command remains entry-only
        """;
    }

    private static ClarificationItem CreateClarifyListItem()
    {
        return new ClarificationItem
        {
            ClarificationSource = "execution",
            QuestionId = "request",
            ExecutionUnit = "G22",
            QuestionText = "Clarify blocker for cli clarify open command: clarify open command remains entry-only",
            Reason = "Clarification requested for [G22] Clarify Open Command: Open a clarification request for the current queue loop.",
            AffectedIntents = ["ICL.P.PRODUCT_GOAL"],
            AffectedExecutionUnits = ["G22"],
            BlockingOrNonblocking = "blocking",
            ClarificationReturnPath = "intents/intent-cli/clarifications/open.md",
            Status = ClarificationStatus.Open,
            CreatedAt = DateTimeOffset.Parse("2026-04-11T06:10:00Z"),
            Answer = null
        };
    }

    private static QueueState CreateClarifyAnswerQueueState()
    {
        return new QueueState
        {
            SchemaVersion = "1",
            UpdatedAt = DateTimeOffset.Parse("2026-04-12T07:00:00Z"),
            Items =
            [
                new QueueItem
                {
                    ExecutionUnit = "G24",
                    Title = "[G24] Clarify Answer Command",
                    State = QueueItemState.ClarifyBlocked,
                    Dependencies = [],
                    BlockedBy = ["need clarification"],
                    ClarificationReturnPath = "intents/intent-cli/clarifications/open.md",
                    PacketPaths = new PacketPaths
                    {
                        Implementation = ".intent-cli/issues/G24/implementation.md",
                        ReviewContext = ".intent-cli/issues/G24/review-context.md",
                        Yaml = ".intent-cli/issues/G24/packet.yaml"
                    },
                    WorkerRole = "coder",
                    ReviewRole = "reviewer",
                    Priority = "high"
                }
            ]
        };
    }

    private static ClarificationItem CreateClarifyAnswerItem()
    {
        return new ClarificationItem
        {
            ClarificationSource = "execution",
            QuestionId = "request",
            ExecutionUnit = "G24",
            QuestionText = "Which field should remain canonical?",
            Reason = "Clarification requested for [G24] Clarify Answer Command: Resolve the queue blocker.",
            AffectedIntents = ["ICL.P.PRODUCT_GOAL"],
            AffectedExecutionUnits = ["G24"],
            BlockingOrNonblocking = "blocking",
            ClarificationReturnPath = "intents/intent-cli/clarifications/open.md",
            Status = ClarificationStatus.Open,
            CreatedAt = DateTimeOffset.Parse("2026-04-12T06:50:00Z"),
            Answer = null
        };
    }

    private static string CreateInterviewStartItemYaml()
    {
        return """
artifact_kind: interview
domain_slug: auth
source_concept_ref: "intents/intent-cli/concepts/auth-oauth2.md"
question_id: iq-1
question_text: "Which auth flow should be canonical?"
reason: "Auth direction is still underspecified."
affects:
  - "auth-oauth2"
blocking_or_nonblocking: blocking
status: open
return_to_intent_paths:
  - "intents/intent-cli/intent-tree/means/auth-oauth2.md"
created_at: "2026-04-13T08:00:00.0000000+00:00"
answer: null
""";
    }

    private static string CreateInterviewAnswerItemYaml()
    {
        return """
artifact_kind: interview
domain_slug: auth
source_concept_ref: "intents/intent-cli/concepts/auth-oauth2.md"
question_id: iq-1
question_text: "Which auth flow should be canonical?"
reason: "Auth direction is still underspecified."
affects:
  - "auth-oauth2"
blocking_or_nonblocking: blocking
status: open
return_to_intent_paths:
  - "intents/intent-cli/intent-tree/means/auth-oauth2.md"
created_at: "2026-04-13T08:00:00.0000000+00:00"
answer: null
recommended_updates:
  - "Update auth strategy"
""";
    }

    private static string CreateRunImplementRunLog()
    {
        return """
        {"ts":"2026-04-08T08:00:00Z","execution_unit":"G19","event":"review","by":"intent-cli","linked_pr":"https://github.com/J-Tech-Japan/intent-system/pull/66"}
        {"ts":"2026-04-08T08:30:00Z","execution_unit":"G19","event":"review","by":"intent-cli","linked_pr":"https://github.com/J-Tech-Japan/intent-system/pull/67"}
        """ + Environment.NewLine;
    }

    private static string CreateRunFixReviewCommentArtifactJson()
    {
        return """
        {
          "execution_unit": "G20",
          "review_request_ref": ".intent-cli/reviews/G20.request.json",
          "linked_pr": "https://github.com/J-Tech-Japan/intent-system/pull/69",
          "comment_ref": "https://github.com/J-Tech-Japan/intent-system/pull/69#issuecomment-2",
          "body_path": "/repo/prepared-comment.md"
        }
        """;
    }

    private static string CreateRunFixRunLog()
    {
        return """
        {"ts":"2026-04-09T09:00:00Z","execution_unit":"G20","event":"review","by":"intent-cli","linked_pr":"https://github.com/J-Tech-Japan/intent-system/pull/69"}
        {"ts":"2026-04-09T09:20:00Z","execution_unit":"G20","event":"fix-requested","by":"intent-cli","comment_ref":"https://github.com/J-Tech-Japan/intent-system/pull/69#issuecomment-2"}
        """ + Environment.NewLine;
    }

    private static string CreateRunResubmitRunLog()
    {
        return """
        {"ts":"2026-04-10T07:00:00Z","execution_unit":"G21","event":"review","by":"intent-cli","linked_pr":"https://github.com/J-Tech-Japan/intent-system/pull/71"}
        {"ts":"2026-04-10T07:10:00Z","execution_unit":"G21","event":"fix-requested","by":"intent-cli","comment_ref":"https://github.com/J-Tech-Japan/intent-system/pull/71#issuecomment-3"}
        """ + Environment.NewLine;
    }

    private static string CreateWorkflowDefinitionJson()
    {
        return """
        {
          "execution_unit": "C2",
          "packet_paths": {
            "implementation": ".intent-cli/issues/C2/implementation.md",
            "review_context": ".intent-cli/issues/C2/review-context.md",
            "yaml": ".intent-cli/issues/C2/packet.yaml"
          },
          "worker_roles": {
            "worker": "coder",
            "reviewer": "reviewer"
          },
          "dependency_snapshot": ["A1"],
          "entry_conditions": ["A1 completed"],
          "steps": [
            {
              "kind": "implement",
              "role": "coder",
              "on_success": ["review"],
              "on_failure": []
            },
            {
              "kind": "review",
              "role": "reviewer",
              "on_success": ["complete"],
              "on_failure": ["comment-findings"]
            }
          ],
          "success_signal": "workflow render writes workflow artifact",
          "review_mode": "deterministic-review",
          "completion_action": "wait-for-deterministic-review"
        }
        """;
    }

    private static string CreateWorkflowRunArtifactJson()
    {
        return WorkerAdapterSerializer.SerializeResult(
            new WorkerAdapter.Models.WorkerAdapterResult
            {
                RunStatus = WorkerAdapter.Models.WorkerAdapterRunStatus.Running,
                StepStatuses =
                [
                    new WorkerAdapter.Models.WorkerAdapterStepStatus
                    {
                        Step = Workflow.Models.WorkflowStepKind.Implement,
                        Status = WorkerAdapter.Models.WorkerAdapterStepState.Running
                    },
                    new WorkerAdapter.Models.WorkerAdapterStepStatus
                    {
                        Step = Workflow.Models.WorkflowStepKind.Review,
                        Status = WorkerAdapter.Models.WorkerAdapterStepState.Pending
                    }
                ],
                ReviewResult = new WorkerAdapter.Models.WorkerReviewResult
                {
                    Disposition = WorkerAdapter.Models.WorkerReviewDisposition.Pending
                },
                ReviewCommentRefs = [],
                ClarificationRequests = [],
                ResultSummary = "Workflow run artifact initialized for C2.",
                RunLogRefs = [".intent-cli/workflows/C2.run.json"]
            });
    }

    private static string CreateReviewContextMarkdown()
    {
        return """
        # Execution Unit

        `G9`

        # Goal

        `intent-cli review run <execution-unit>` を working command として実装し、
        review context packet と latest linked PR をもとに
        deterministic review request artifact を `.intent-cli/reviews/<execution-unit>.request.json` へ生成できるようにする。

        # Parent References

        - [Intent CLI Surface](/Users/tomohisa/dev/GitHub/MyIntentHost/intents/intent-cli/specs/05-intent-cli-surface.md)
        - [Config And Run Model](/Users/tomohisa/dev/GitHub/MyIntentHost/intents/intent-cli/specs/08-config-and-run-model.md)

        # Deterministic Review Checks

        - review run command が PR comment 投稿や closeout の責務へ広がっていない

        # Expected Evidence

        - dotnet test IntentSystem.sln
        - review run command tests
        """;
    }

    private static string CreateRunStartPacketYaml()
    {
        return """
        implementation_issue_packet:
          issue_title: "[G14] Run Start Command"
          issue_kind: "feature"
          source_execution_unit: "G14"
          goal: "Create isolated worktree and activate queue item."
          in_scope:
            - "run start command"
          out_of_scope:
            - "worker start"
          target_repo: "submodules/intent-system"
          target_path: "."
          target_part: "cli run start command"
          dependencies:
            - "G13"
          technical_baseline:
            - "C# / .NET"
          project_local_guide:
            - "AGENTS.md"
          intent_baseline:
            - "run start stays thin"
          intent_references:
            - "ICL.P.PRODUCT_GOAL"
          rules_and_specs:
            - "intents/intent-cli/specs/08-config-and-run-model.md"
          acceptance_criteria:
            - "isolated worktree created"
          verification_evidence:
            - "tests-passing"
          review_mode: "deterministic-review"
          completion_action: "wait-for-deterministic-review"
          landing_policy: "merge-after-review"
        
        review_context_packet:
          source_execution_unit: "G14"
          parent_intent_root: "intents/intent-cli/intent-tree/00-map.md"
          intent_references:
            - "ICL.P.PRODUCT_GOAL"
          rules_and_specs:
            - "intents/intent-cli/specs/08-config-and-run-model.md"
          acceptance_criteria:
            - "isolated worktree created"
          deterministic_review_checks:
            - "run start remains thin"
          clarification_return_path: "intents/intent-cli/clarifications/open.md"
        """;
    }

    private static string CreateReviewRunLog()
    {
        return """
        {"ts":"2026-04-03T10:00:00Z","execution_unit":"G9","event":"review-started","by":"intent-cli","linked_pr":"https://github.com/J-Tech-Japan/intent-system/pull/44"}
        {"ts":"2026-04-03T10:20:00Z","execution_unit":"G9","event":"review-started","by":"intent-cli","linked_pr":"https://github.com/J-Tech-Japan/intent-system/pull/45"}
        """ + Environment.NewLine;
    }

    private static string CreateReviewCommentRequestJson()
    {
        return """
        {
          "execution_unit": "G10",
          "review_context_ref": ".intent-cli/issues/G10/review-context.md",
          "linked_pr": "https://github.com/J-Tech-Japan/intent-system/pull/46",
          "deterministic_review_checks": [
            "review comment command が deterministic diff review の実行, merge, closeout の責務へ広がっていない"
          ],
          "acceptance_criteria": [],
          "expected_evidence": [
            "dotnet test IntentSystem.sln"
          ]
        }
        """;
    }

    private static string CreateReviewAcceptPacketYaml()
    {
        return """
        implementation_issue_packet:
          issue_title: "[G12] Review Accept Command"
          issue_kind: "feature"
          source_execution_unit: "G12"
          goal: "Close out accepted review."
          in_scope:
            - "review accept command"
          out_of_scope:
            - "review comment"
          target_repo: "submodules/child-repo"
          target_path: "."
          target_part: "cli review accept command"
          dependencies:
            - "G10"
          technical_baseline:
            - "C# / .NET"
          project_local_guide:
            - "AGENTS.md"
          intent_baseline:
            - "closeout stays thin"
          intent_references:
            - "ICL.P.PRODUCT_GOAL"
          rules_and_specs:
            - "intents/rules/issue-lifecycle-and-landing.md"
          acceptance_criteria:
            - "review accept merges and closes"
          verification_evidence:
            - "tests-passing"
          review_mode: "deterministic-review"
          completion_action: "wait-for-deterministic-review"
          landing_policy: "merge-after-review"
        
        review_context_packet:
          source_execution_unit: "G12"
          parent_intent_root: "intents/intent-cli/intent-tree/00-map.md"
          intent_references:
            - "ICL.P.PRODUCT_GOAL"
          rules_and_specs:
            - "intents/rules/issue-lifecycle-and-landing.md"
          acceptance_criteria:
            - "review accept merges and closes"
          deterministic_review_checks:
            - "selected item only"
          clarification_return_path: "intents/intent-cli/clarifications/open.md"
        """;
    }

    private static string CreateReviewAcceptRunLog()
    {
        return """
        {"ts":"2026-04-03T10:00:00Z","execution_unit":"G12","event":"issue-created","by":"intent-cli","linked_issue":"https://github.com/J-Tech-Japan/intent-system/issues/51"}
        {"ts":"2026-04-03T10:10:00Z","execution_unit":"G12","event":"review-started","by":"intent-cli","linked_pr":"https://github.com/J-Tech-Japan/intent-system/pull/52"}
        """ + Environment.NewLine;
    }

    private sealed class FakeReviewCommentPublisher : IReviewCommentPublisher
    {
        public string PostComment(string linkedPr, string body)
        {
            return "https://github.com/J-Tech-Japan/intent-system/pull/46#issuecomment-1";
        }
    }

    private sealed class FakeQueueDispatchPublisher : IQueueDispatchPublisher
    {
        public LinkedIssue CreateIssue(string targetRepo, string title, string body)
        {
            return new LinkedIssue
            {
                Repo = targetRepo,
                Number = 53,
                Url = "https://github.com/J-Tech-Japan/intent-system/issues/53"
            };
        }
    }

    private sealed class FakeQueueDispatchGitRunner : IGitRemoteCommandRunner
    {
        public GitRemoteCommandResult Run(string workingDirectory, IReadOnlyList<string> arguments)
        {
            return new GitRemoteCommandResult
            {
                ExitCode = 0,
                StdOut = "git@github.com:J-Tech-Japan/intent-system.git" + Environment.NewLine,
                StdErr = string.Empty
            };
        }
    }

    private sealed class FakeRunStartGitRunner : IGitCommandRunner
    {
        public GitCommandResult Run(string workingDirectory, IReadOnlyList<string> arguments)
        {
            return new GitCommandResult
            {
                ExitCode = 0,
                StdOut = string.Empty,
                StdErr = string.Empty
            };
        }
    }

    private sealed class FakeRunSubmitGitRunner : IGitCommandRunner
    {
        public GitCommandResult Run(string workingDirectory, IReadOnlyList<string> arguments)
        {
            if (arguments.SequenceEqual(["rev-parse", "--abbrev-ref", "HEAD"]))
            {
                return new GitCommandResult
                {
                    ExitCode = 0,
                    StdOut = "issue-56-g14" + Environment.NewLine,
                    StdErr = string.Empty
                };
            }

            if (arguments.SequenceEqual(["remote", "get-url", "origin"]))
            {
                return new GitCommandResult
                {
                    ExitCode = 0,
                    StdOut = "git@github.com:J-Tech-Japan/intent-system.git" + Environment.NewLine,
                    StdErr = string.Empty
                };
            }

            return new GitCommandResult
            {
                ExitCode = 0,
                StdOut = string.Empty,
                StdErr = string.Empty
            };
        }
    }

    private sealed class FakeRunResubmitGitRunner : IGitCommandRunner
    {
        public GitCommandResult Run(string workingDirectory, IReadOnlyList<string> arguments)
        {
            if (arguments.SequenceEqual(["rev-parse", "--abbrev-ref", "HEAD"]))
            {
                return new GitCommandResult
                {
                    ExitCode = 0,
                    StdOut = "issue-70-g21" + Environment.NewLine,
                    StdErr = string.Empty
                };
            }

            return new GitCommandResult
            {
                ExitCode = 0,
                StdOut = string.Empty,
                StdErr = string.Empty
            };
        }
    }

    private sealed class FakeRunSubmitPublisher : IRunSubmitPublisher
    {
        public string CreateDraftPullRequest(string targetRepo, string headBranch, string title, string body)
        {
            return "https://github.com/J-Tech-Japan/intent-system/pull/58";
        }
    }

    private sealed class FakeReviewAcceptClient : IReviewAcceptClient
    {
        public string MergePullRequest(string linkedPr)
        {
            return "abc123";
        }

        public void CloseIssue(string linkedIssue)
        {
        }
    }

    private sealed class FakeReviewAcceptGitRunner : IGitCommandRunner
    {
        public GitCommandResult Run(string workingDirectory, IReadOnlyList<string> arguments)
        {
            return new GitCommandResult
            {
                ExitCode = 0,
                StdOut = arguments.SequenceEqual(["rev-parse", "HEAD"])
                    ? "abc123" + Environment.NewLine
                    : string.Empty,
                StdErr = string.Empty
            };
        }
    }

    private sealed class FakeGenerateFromCurrentGitHubRunner : IGitHubCommandRunner
    {
        public GitHubCommandResult Run(IReadOnlyList<string> arguments)
        {
            if (arguments.SequenceEqual(["issue", "view", "114", "--comments", "--json", "number,title,body,url,state,comments"]))
            {
                return new GitHubCommandResult
                {
                    ExitCode = 0,
                    StdOut = """{"number":114,"title":"[G44] Generate From Current","body":"Reverse intake entry point.","url":"https://github.com/J-Tech-Japan/intent-system/issues/114","state":"OPEN","comments":[{"body":"keep it deterministic"}]}""",
                    StdErr = string.Empty
                };
            }

            if (arguments.SequenceEqual(["pr", "view", "113", "--comments", "--json", "number,title,body,url,state,isDraft,mergeStateStatus,comments,reviews"]))
            {
                return new GitHubCommandResult
                {
                    ExitCode = 0,
                    StdOut = """{"number":113,"title":"[codex] Add intake activate command","body":"Adds intake activate.","url":"https://github.com/J-Tech-Japan/intent-system/pull/113","state":"OPEN","isDraft":true,"mergeStateStatus":"CLEAN","comments":[{"body":"ok"}],"reviews":[{"state":"COMMENTED"}]}""",
                    StdErr = string.Empty
                };
            }

            throw new InvalidOperationException($"Unexpected gh arguments: {string.Join(' ', arguments)}");
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        private readonly string rootPath = Directory.CreateTempSubdirectory("intent-cli-tests-").FullName;

        public string CreateDirectory(string relativePath)
        {
            var fullPath = Path.Combine(rootPath, relativePath);
            Directory.CreateDirectory(fullPath);
            return fullPath;
        }

        public void CreateFile(string relativePath, string contents)
        {
            var fullPath = Path.Combine(rootPath, relativePath);
            var directoryPath = Path.GetDirectoryName(fullPath)
                ?? throw new InvalidOperationException("Temporary file path did not contain a directory.");

            Directory.CreateDirectory(directoryPath);
            File.WriteAllText(fullPath, contents);
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
