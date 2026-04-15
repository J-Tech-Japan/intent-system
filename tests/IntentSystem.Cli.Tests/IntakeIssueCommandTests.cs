using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;
using IntentSystem.Projection.Serialization;
using IntentSystem.Review;

namespace IntentSystem.Cli.Tests;

public sealed class IntakeIssueCommandTests
{
    [Fact]
    public void Execute_GivenCanonicalExecutionArtifact_GeneratesIssueArtifacts()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", "intents", "intent-cli", "execution", "05-post-mvp-sub-slices.md"),
            CreateExecutionBaselineMarkdown());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "intake", "auth.execution.md"),
            CreateExecutionArtifactMarkdown("auth"));
        tempDirectory.CreateFile(
            Path.Combine("repo", "intents", "intent-cli", "intent-tree", "00-map.md"),
            "# Intent CLI Map");
        tempDirectory.CreateFile(
            Path.Combine("repo", "intents", "intent-cli", "clarifications", "open.md"),
            "# Clarifications");
        tempDirectory.CreateFile(
            Path.Combine("repo", "intents", "intent-cli", "concepts", "oauth2.md"),
            "# Auth Concept");
        tempDirectory.CreateFile(
            Path.Combine("repo", "intents", "intent-cli", "intent-tree", "means", "device-code.md"),
            "# Device Code");
        using var writer = new StringWriter();

        var exitCode = IntakeIssueCommand.Execute(CreateContext(repoRoot), ["auth"], writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("Intake issue artifacts generated for domain 'auth'.", output, StringComparison.Ordinal);
        Assert.Contains("- AUTH-01", output, StringComparison.Ordinal);
        Assert.Contains("- AUTH-02", output, StringComparison.Ordinal);
        Assert.Contains(".intent-cli/issues/AUTH-01/github-body.md", output, StringComparison.Ordinal);

        Assert.True(File.Exists(Path.Combine(repoRoot, ".intent-cli", "issues", "AUTH-01", "implementation.md")));
        Assert.True(File.Exists(Path.Combine(repoRoot, ".intent-cli", "issues", "AUTH-01", "review-context.md")));
        Assert.True(File.Exists(Path.Combine(repoRoot, ".intent-cli", "issues", "AUTH-01", "packet.yaml")));
        Assert.True(File.Exists(Path.Combine(repoRoot, ".intent-cli", "issues", "AUTH-01", "github-body.md")));

        var implementationMarkdown = File.ReadAllText(Path.Combine(repoRoot, ".intent-cli", "issues", "AUTH-01", "implementation.md"));
        Assert.Contains("# [AUTH-01] Auth Concept", implementationMarkdown, StringComparison.Ordinal);
        Assert.Contains("updated intake-origin source `intents/intent-cli/concepts/oauth2.md`", implementationMarkdown, StringComparison.Ordinal);

        var reviewMarkdown = File.ReadAllText(Path.Combine(repoRoot, ".intent-cli", "issues", "AUTH-01", "review-context.md"));
        Assert.Contains("intents/intent-cli/intent-tree/00-map.md", reviewMarkdown, StringComparison.Ordinal);
        var parsedReviewContext = ReviewContextMarkdownParser.Parse(reviewMarkdown);
        Assert.Equal("AUTH-01", parsedReviewContext.SourceExecutionUnit);
        Assert.NotEmpty(parsedReviewContext.DeterministicReviewChecks);

        var packet = ProjectionPacketSerializer.Deserialize(
            File.ReadAllText(Path.Combine(repoRoot, ".intent-cli", "issues", "AUTH-01", "packet.yaml")));
        Assert.Equal("AUTH-01", packet.ImplementationIssuePacket.SourceExecutionUnit);
        Assert.Equal("submodules/intent-system", packet.ImplementationIssuePacket.TargetRepo);
        Assert.Equal(".", packet.ImplementationIssuePacket.TargetPath);
        Assert.Equal("concepts", packet.ImplementationIssuePacket.TargetPart);

        var githubBody = File.ReadAllText(Path.Combine(repoRoot, ".intent-cli", "issues", "AUTH-01", "github-body.md"));
        Assert.Equal(implementationMarkdown, githubBody);
    }

    [Fact]
    public void Execute_GivenGenericIntentNamespace_DerivesParentRefsFromSourceFiles()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", "intents", "payments", "execution", "05-post-mvp-sub-slices.md"),
            CreateExecutionBaselineMarkdown("payments"));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "intake", "payments.execution.md"),
            CreateSingleExecutionArtifactMarkdown(
                "payments",
                "PAY-01",
                "intents/payments/concepts/checkout.md",
                "concepts",
                "Checkout"));
        tempDirectory.CreateFile(
            Path.Combine("repo", "intents", "payments", "intent-tree", "00-map.md"),
            "# Payments Map");
        tempDirectory.CreateFile(
            Path.Combine("repo", "intents", "payments", "clarifications", "open.md"),
            "# Payments Clarifications");
        tempDirectory.CreateFile(
            Path.Combine("repo", "intents", "payments", "concepts", "checkout.md"),
            "# Checkout");
        using var writer = new StringWriter();

        var exitCode = IntakeIssueCommand.Execute(CreateContext(repoRoot), ["payments"], writer);

        Assert.Equal(0, exitCode);
        var reviewMarkdown = File.ReadAllText(Path.Combine(repoRoot, ".intent-cli", "issues", "PAY-01", "review-context.md"));
        Assert.Contains("intents/payments/intent-tree/00-map.md", reviewMarkdown, StringComparison.Ordinal);
        Assert.DoesNotContain("intents/intent-cli/intent-tree/00-map.md", reviewMarkdown, StringComparison.Ordinal);

        var packet = ProjectionPacketSerializer.Deserialize(
            File.ReadAllText(Path.Combine(repoRoot, ".intent-cli", "issues", "PAY-01", "packet.yaml")));
        Assert.Equal("intents/payments/intent-tree/00-map.md", packet.ReviewContextPacket.ParentIntentRoot);
        Assert.Equal("intents/payments/clarifications/open.md", packet.ReviewContextPacket.ClarificationReturnPath);
        Assert.DoesNotContain(
            "intents/intent-cli/specs/04-concept-intake-and-interview.md",
            packet.ImplementationIssuePacket.RulesAndSpecs,
            StringComparer.Ordinal);
    }

    [Fact]
    public void Execute_GivenExistingArtifacts_SkipsExecutionUnit()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", "intents", "intent-cli", "execution", "05-post-mvp-sub-slices.md"),
            CreateExecutionBaselineMarkdown());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "intake", "auth.execution.md"),
            CreateExecutionArtifactMarkdown("auth"));
        tempDirectory.CreateFile(
            Path.Combine("repo", "intents", "intent-cli", "intent-tree", "00-map.md"),
            "# Intent CLI Map");
        tempDirectory.CreateFile(
            Path.Combine("repo", "intents", "intent-cli", "clarifications", "open.md"),
            "# Clarifications");
        tempDirectory.CreateFile(
            Path.Combine("repo", "intents", "intent-cli", "concepts", "oauth2.md"),
            "# Auth Concept");
        tempDirectory.CreateFile(
            Path.Combine("repo", "intents", "intent-cli", "intent-tree", "means", "device-code.md"),
            "# Device Code");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "AUTH-02", "implementation.md"),
            "existing implementation");
        using var writer = new StringWriter();

        var exitCode = IntakeIssueCommand.Execute(CreateContext(repoRoot), ["auth"], writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("- AUTH-01", output, StringComparison.Ordinal);
        Assert.Contains("Skipped units:", output, StringComparison.Ordinal);
        Assert.Contains("- AUTH-02", output, StringComparison.Ordinal);
        Assert.Equal(
            "existing implementation",
            File.ReadAllText(Path.Combine(repoRoot, ".intent-cli", "issues", "AUTH-02", "implementation.md")));
    }

    [Fact]
    public void Execute_GivenExecutionArtifactWithNoUnits_ReturnsExitCodeOne()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", "intents", "intent-cli", "execution", "05-post-mvp-sub-slices.md"),
            CreateExecutionBaselineMarkdown());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "intake", "auth.execution.md"),
            """
            # Intake Execution Draft

            ## Domain
            `auth`

            ## Proposed Execution Units
            """);
        using var writer = new StringWriter();

        var exitCode = IntakeIssueCommand.Execute(CreateContext(repoRoot), ["auth"], writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("No intake-origin issue-ready execution units were found for domain 'auth'.", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenMissingExecutionArtifact_ReturnsExitCodeOne()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", "intents", "intent-cli", "execution", "05-post-mvp-sub-slices.md"),
            CreateExecutionBaselineMarkdown());
        using var writer = new StringWriter();

        var exitCode = IntakeIssueCommand.Execute(CreateContext(repoRoot), ["auth"], writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("Intake execution artifact was not found", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenMissingDomainArgument_ReturnsExitCodeOne()
    {
        using var writer = new StringWriter();

        var exitCode = IntakeIssueCommand.Execute(CreateContext("/tmp/intent-system"), [], writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("requires a domain", writer.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Execute_GivenRuntimeOnlyTargetPart_ReturnsExitCodeOneWithoutGeneratingChildFacingArtifacts()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", "intents", "intent-cli", "execution", "05-post-mvp-sub-slices.md"),
            CreateExecutionBaselineMarkdown());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "intake", "auth.execution.md"),
            CreateSingleExecutionArtifactMarkdown(
                "auth",
                "AUTH-01",
                "intents/intent-cli/concepts/oauth2.md",
                ".intent-cli/intake",
                "Auth Concept"));
        tempDirectory.CreateFile(
            Path.Combine("repo", "intents", "intent-cli", "intent-tree", "00-map.md"),
            "# Intent CLI Map");
        tempDirectory.CreateFile(
            Path.Combine("repo", "intents", "intent-cli", "clarifications", "open.md"),
            "# Clarifications");
        tempDirectory.CreateFile(
            Path.Combine("repo", "intents", "intent-cli", "concepts", "oauth2.md"),
            "# Auth Concept");
        using var writer = new StringWriter();

        var exitCode = IntakeIssueCommand.Execute(CreateContext(repoRoot), ["auth"], writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("host runtime-only '.intent-cli/**' content", writer.ToString(), StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(repoRoot, ".intent-cli", "issues", "AUTH-01", "github-body.md")));
    }

    [Fact]
    public void Execute_GivenRuntimeOnlyTargetRepo_ReturnsExitCodeOneWithoutGeneratingChildFacingArtifacts()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", "intents", "intent-cli", "execution", "05-post-mvp-sub-slices.md"),
            CreateExecutionBaselineMarkdown(targetRepo: ".intent-cli"));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "intake", "auth.execution.md"),
            CreateSingleExecutionArtifactMarkdown(
                "auth",
                "AUTH-01",
                "intents/intent-cli/concepts/oauth2.md",
                "concepts",
                "Auth Concept"));
        tempDirectory.CreateFile(
            Path.Combine("repo", "intents", "intent-cli", "intent-tree", "00-map.md"),
            "# Intent CLI Map");
        tempDirectory.CreateFile(
            Path.Combine("repo", "intents", "intent-cli", "clarifications", "open.md"),
            "# Clarifications");
        tempDirectory.CreateFile(
            Path.Combine("repo", "intents", "intent-cli", "concepts", "oauth2.md"),
            "# Auth Concept");
        using var writer = new StringWriter();

        var exitCode = IntakeIssueCommand.Execute(CreateContext(repoRoot), ["auth"], writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("Child target repo '.intent-cli'", writer.ToString(), StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(repoRoot, ".intent-cli", "issues", "AUTH-01", "github-body.md")));
    }

    private static string CreateExecutionBaselineMarkdown(
        string namespaceSegment = "intent-cli",
        string targetRepo = "submodules/intent-system")
    {
        return $$"""
            # Post-MVP Sub-Slices

            | subslice_id | belongs_to_slice | goal | depends_on_subslices | target_repo | target_path | target_part | issue_cut_ready |
            |---|---|---|---|---|---|---|---|
            | G37 | G | `intake issue <domain>` を CLI shell から使えるようにし、updated intake-origin `execution/` source-of-truth から issue-ready execution unit の issue artifact 群を deterministic に生成できるようにする | G2, G35, G36 | {{targetRepo}} | . | cli intake issue command | yes |

            ## G37 の current baseline

            - `intake issue <domain>` を最初の intake issue-artifact generation command にする
            - canonical source は current `execution/` source files と current `G2` / `G29` / `G30` / `G32` / `G33` / `G34` / `G35` / `G36` intake baseline である
            - successful output は selected domain の intake-origin issue-ready execution unit に対応する `.intent-cli/issues/<execution-unit>/implementation.md`, `review-context.md`, `packet.yaml`, and `github-body.md` の deterministic generation を baseline にする
            """;
    }

    private static string CreateExecutionArtifactMarkdown(
        string domain,
        string firstExecutionUnit = "AUTH-01",
        string firstSourceFilePath = "intents/intent-cli/concepts/oauth2.md",
        string firstTargetPart = "concepts",
        string firstHeading = "Auth Concept")
    {
        return $$"""
            # Intake Execution Draft

            ## Domain
            `{{domain}}`

            ## Proposed Execution Units

            ### `{{firstExecutionUnit}}`
            source_file_path: {{firstSourceFilePath}}
            target_part: {{firstTargetPart}}
            dependencies:
            - none
            readiness_notes:
            - Current heading: # {{firstHeading}}
            verification_hints:
            - dotnet test IntentSystem.sln

            ### `AUTH-02`
            source_file_path: intents/intent-cli/intent-tree/means/device-code.md
            target_part: intent-tree/means
            dependencies:
            - AUTH-01
            readiness_notes:
            - Current heading: # Device Code
            verification_hints:
            - dotnet test IntentSystem.sln

            """;
    }

    private static string CreateSingleExecutionArtifactMarkdown(
        string domain,
        string executionUnit,
        string sourceFilePath,
        string targetPart,
        string heading)
    {
        return $$"""
            # Intake Execution Draft

            ## Domain
            `{{domain}}`

            ## Proposed Execution Units

            ### `{{executionUnit}}`
            source_file_path: {{sourceFilePath}}
            target_part: {{targetPart}}
            dependencies:
            - none
            readiness_notes:
            - Current heading: # {{heading}}
            verification_hints:
            - dotnet test IntentSystem.sln

            """;
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
                    ArtifactRoot = ".intent-cli"
                }
            }
        };
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        private readonly string rootPath = Directory.CreateTempSubdirectory("intent-cli-intake-issue-command-tests-").FullName;

        public string CreateDirectory(string relativePath)
        {
            var fullPath = Path.Combine(rootPath, relativePath);
            Directory.CreateDirectory(fullPath);
            return fullPath;
        }

        public string CreateFile(string relativePath, string contents)
        {
            var fullPath = Path.Combine(rootPath, relativePath);
            var directoryPath = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(directoryPath))
            {
                Directory.CreateDirectory(directoryPath);
            }

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
