using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;
using IntentSystem.Projection.Serialization;

namespace IntentSystem.Cli.Tests;

public sealed class IntakeIssueCommandTests
{
    [Fact]
    public void Execute_GivenIntakeOriginExecutionUnits_GeneratesIssueArtifacts()
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

            ## G36 の current baseline

            - `intake execution apply <domain>` を最初の execution source-of-truth apply command にする
            - execution_unit: AUTH-01
            - source_file_path: intents/intent-cli/concepts/oauth2.md
            - target_part: concepts
            - readiness_notes: Current heading: # Auth Concept
            - verification_hints: dotnet test IntentSystem.sln

            - execution_unit: AUTH-02
            - source_file_path: intents/intent-cli/intent-tree/means/device-code.md
            - target_part: intent-tree/means
            - dependencies: AUTH-01
            - readiness_notes: Current heading: # Device Code
            - verification_hints: dotnet test IntentSystem.sln

            - execution_unit: BILLING-01
            - source_file_path: intents/intent-cli/concepts/billing.md
            - target_part: concepts
            - readiness_notes: Current heading: # Billing
            - verification_hints: dotnet test IntentSystem.sln

            ## G37 の current baseline

            - `intake issue <domain>` を最初の intake issue-artifact generation command にする
            - canonical source は current `execution/` source files と current `G2` / `G29` / `G30` / `G32` / `G33` / `G34` / `G35` / `G36` intake baseline である
            - successful output は selected domain の intake-origin issue-ready execution unit に対応する `.intent-cli/issues/<execution-unit>/implementation.md`, `review-context.md`, `packet.yaml`, and `github-body.md` の deterministic generation を baseline にする
            """);
        using var writer = new StringWriter();

        var exitCode = IntakeIssueCommand.Execute(CreateContext(repoRoot), ["auth"], writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("Intake issue artifacts generated for domain 'auth'.", output, StringComparison.Ordinal);
        Assert.Contains("- AUTH-01", output, StringComparison.Ordinal);
        Assert.Contains("- AUTH-02", output, StringComparison.Ordinal);
        Assert.DoesNotContain("BILLING-01", output, StringComparison.Ordinal);
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

        var packet = ProjectionPacketSerializer.Deserialize(
            File.ReadAllText(Path.Combine(repoRoot, ".intent-cli", "issues", "AUTH-01", "packet.yaml")));
        Assert.Equal("AUTH-01", packet.ImplementationIssuePacket.SourceExecutionUnit);
        Assert.Equal("submodules/intent-system", packet.ImplementationIssuePacket.TargetRepo);
        Assert.Equal(".", packet.ImplementationIssuePacket.TargetPath);
        Assert.Equal("concepts", packet.ImplementationIssuePacket.TargetPart);

        var githubBody = File.ReadAllText(Path.Combine(repoRoot, ".intent-cli", "issues", "AUTH-01", "github-body.md"));
        Assert.Equal(implementationMarkdown, githubBody);
        Assert.False(File.Exists(Path.Combine(repoRoot, ".intent-cli", "issues", "BILLING-01", "implementation.md")));
    }

    [Fact]
    public void Execute_GivenExistingArtifacts_SkipsExecutionUnit()
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

            ## G36 の current baseline

            - `intake execution apply <domain>` を最初の execution source-of-truth apply command にする
            - execution_unit: AUTH-01
            - source_file_path: intents/intent-cli/concepts/oauth2.md
            - target_part: concepts
            - readiness_notes: Current heading: # Auth Concept
            - verification_hints: dotnet test IntentSystem.sln

            - execution_unit: AUTH-02
            - source_file_path: intents/intent-cli/intent-tree/means/device-code.md
            - target_part: intent-tree/means
            - readiness_notes: Current heading: # Device Code
            - verification_hints: dotnet test IntentSystem.sln

            ## G37 の current baseline

            - `intake issue <domain>` を最初の intake issue-artifact generation command にする
            """);
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
    public void Execute_GivenNoMatchingExecutionUnits_ReturnsExitCodeOne()
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

            ## G36 の current baseline

            - `intake execution apply <domain>` を最初の execution source-of-truth apply command にする
            - execution_unit: BILLING-01
            - source_file_path: intents/intent-cli/concepts/billing.md
            - target_part: concepts
            - readiness_notes: Current heading: # Billing
            - verification_hints: dotnet test IntentSystem.sln

            ## G37 の current baseline

            - `intake issue <domain>` を最初の intake issue-artifact generation command にする
            """);
        using var writer = new StringWriter();

        var exitCode = IntakeIssueCommand.Execute(CreateContext(repoRoot), ["auth"], writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("No intake-origin issue-ready execution units were found for domain 'auth'.", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenMissingDomainArgument_ReturnsExitCodeOne()
    {
        using var writer = new StringWriter();

        var exitCode = IntakeIssueCommand.Execute(CreateContext("/tmp/intent-system"), [], writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("requires a domain", writer.ToString(), StringComparison.OrdinalIgnoreCase);
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
