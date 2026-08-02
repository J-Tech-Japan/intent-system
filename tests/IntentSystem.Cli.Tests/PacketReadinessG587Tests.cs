using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

public sealed class PacketReadinessG587Tests : IDisposable
{
    private const string Domain = "intent-cli";
    private const string ExecutionUnit = "G587";
    private const string TargetRepo = "J-Tech-Japan/intent-system";

    private readonly string root = Directory.CreateTempSubdirectory("packet-readiness-g587-").FullName;
    private readonly CliContext context;

    public PacketReadinessG587Tests()
    {
        context = new CliContext
        {
            RepoRoot = root,
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
        WriteBinding("^G[0-9]+$");
    }

    [Theory]
    [InlineData("only-packet-yaml", false, 3, true)]
    [InlineData("body-present-missing-sections", false, 0, true)]
    [InlineData("all-four-files-complete", true, 0, false)]
    [InlineData("body-present-and-complete", false, 2, false)]
    public void PacketDraftAndQueueSeed_AgreeAcrossRequiredMatrix_G587(
        string state,
        bool expectedPublishable,
        int expectedMissingFileCount,
        bool expectMissingSections)
    {
        WritePacketYaml(TargetRepo);
        switch (state)
        {
            case "only-packet-yaml":
                break;
            case "body-present-missing-sections":
                WriteImplementationAndReview();
                WriteGithubBody("# Title\n\n## Goal\nPresent.\n");
                break;
            case "all-four-files-complete":
                WriteImplementationAndReview();
                WriteGithubBody(CompleteGithubBody());
                break;
            case "body-present-and-complete":
                WriteGithubBody(CompleteGithubBody());
                break;
            default:
                throw new InvalidOperationException($"Unknown matrix state: {state}");
        }

        using var draft = ExecutePacketDraft();
        using var seed = ExecuteQueueSeed(out var seedExitCode);

        Assert.Equal(expectedPublishable, draft.RootElement.GetProperty("contract_publishable").GetBoolean());
        Assert.Equal(expectedPublishable, seed.RootElement.GetProperty("contract_publishable").GetBoolean());
        Assert.Equal(expectedPublishable ? 0 : 1, seedExitCode);
        Assert.Equal(
            PreparedPacketCommitReadyAnalyzer.CanonicalFileNames.Count - expectedMissingFileCount,
            Directory.EnumerateFiles(PacketDirectory()).Count());

        var draftMissingFiles = Strings(draft.RootElement, "missing_canonical_files");
        var seedMissingFiles = Strings(seed.RootElement, "missing_canonical_files");
        Assert.Equal(expectedMissingFileCount, draftMissingFiles.Length);
        Assert.Equal(draftMissingFiles, seedMissingFiles);

        var draftMissingSections = Strings(draft.RootElement, "missing_contract_sections");
        var seedMissingSections = Strings(seed.RootElement, "missing_contract_sections");
        Assert.Equal(expectMissingSections, draftMissingSections.Length > 0);
        Assert.Equal(draftMissingSections, seedMissingSections);

        var draftReasons = Strings(draft.RootElement, "refusal_reasons");
        var seedReasons = Strings(seed.RootElement, "refusal_reasons");
        Assert.Equal(draftReasons, seedReasons);
        Assert.Equal(expectedPublishable, draftReasons.Length == 0);

        var draftActions = Strings(draft.RootElement, "recommended_actions");
        var seedActions = Strings(seed.RootElement, "recommended_actions");
        if (expectedPublishable)
        {
            Assert.Empty(draftActions);
        }
        else
        {
            Assert.NotEmpty(draftActions);
            Assert.NotEmpty(seedActions);
        }
    }

    [Fact]
    public void Readiness_ReportsFilesSectionsAndEveryOtherRefusalTogether_G587()
    {
        WriteBinding("^Z4R-G[0-9]+$");
        WritePacketYaml("Other/Repo");
        WriteGithubBody("# Title\n\n## Goal\nPresent.\n");

        using var draft = ExecutePacketDraft();
        using var seed = ExecuteQueueSeed(out var seedExitCode);

        var expectedReasons = new[]
        {
            PreparedPacketCommitReadyAnalyzer.ReasonMissingCanonicalFile,
            PreparedPacketCommitReadyAnalyzer.ReasonWrongDomain,
            PreparedPacketCommitReadyAnalyzer.ReasonWrongTargetRepo,
            PreparedPacketCommitReadyAnalyzer.ReasonGithubBodyMissingSection,
        };
        Assert.Equal(expectedReasons, Strings(draft.RootElement, "refusal_reasons"));
        Assert.Equal(expectedReasons, Strings(seed.RootElement, "refusal_reasons"));
        Assert.Equal(1, seedExitCode);
        Assert.Equal(2, Strings(seed.RootElement, "missing_canonical_files").Length);
        Assert.Contains("Acceptance Criteria", Strings(seed.RootElement, "missing_contract_sections"));
        Assert.Contains("Base Branch Policy", Strings(seed.RootElement, "missing_contract_sections"));
        Assert.True(Strings(draft.RootElement, "recommended_actions").Length >= expectedReasons.Length);
        Assert.True(Strings(seed.RootElement, "recommended_actions").Length >= expectedReasons.Length);
        Assert.Equal(
            PreparedPacketCommitReadyAnalyzer.ReasonMissingCanonicalFile,
            seed.RootElement.GetProperty("unsafe_reason").GetString());
    }

    [Fact]
    public void RequiredSections_StayOnTheExistingPublishContractSource_G587()
    {
        Assert.Same(
            PublishContractSections.Required,
            PreparedPacketCommitReadyAnalyzer.RequiredGithubBodySections);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void DeveloperGuidance_NamesOneShotCheckAndGreenGuarantee_G587(string language)
    {
        var path = Path.Combine(
            RepoVersionPolicySource.RepoRoot(),
            "docs",
            language,
            "09-developer-reference.md");
        var doc = NormalizeWhitespace(File.ReadAllText(path));

        Assert.Contains(
            "intent-cli packet draft --execution-unit <unit> --domain <d> --target-repo <owner/repo> --dry-run --format json",
            doc,
            StringComparison.Ordinal);
        Assert.Contains("missing_canonical_files", doc, StringComparison.Ordinal);
        Assert.Contains("missing_contract_sections", doc, StringComparison.Ordinal);
        Assert.Contains("refusal_reasons", doc, StringComparison.Ordinal);
        Assert.Contains("recommended_actions", doc, StringComparison.Ordinal);
        Assert.Contains(
            language == "en"
                ? "green means every canonical packet file currently exists and every required contract section is present"
                : "green は、すべての canonical packet file が現在存在し、すべての required contract section が存在することを意味します",
            doc,
            StringComparison.Ordinal);
    }

    private JsonDocument ExecutePacketDraft()
    {
        using var writer = new StringWriter();
        var exitCode = PacketDraftCommand.Execute(
            context,
            [
                "--execution-unit", ExecutionUnit,
                "--domain", Domain,
                "--target-repo", TargetRepo,
                "--dry-run",
                "--format", "json",
            ],
            writer);
        Assert.Equal(0, exitCode);
        return JsonDocument.Parse(writer.ToString());
    }

    private JsonDocument ExecuteQueueSeed(out int exitCode)
    {
        using var writer = new StringWriter();
        exitCode = AutomationQueueSeedFromPacketCommand.Execute(
            context,
            [
                "--execution-unit", ExecutionUnit,
                "--domain", Domain,
                "--target-repo", TargetRepo,
                "--format", "json",
            ],
            writer);
        return JsonDocument.Parse(writer.ToString());
    }

    private void WritePacketYaml(string targetRepo)
    {
        Directory.CreateDirectory(PacketDirectory());
        File.WriteAllText(
            Path.Combine(PacketDirectory(), "packet.yaml"),
            $$"""
            domain: {{Domain}}
            implementation_issue_packet:
              source_execution_unit: {{ExecutionUnit}}
              issue_title: G587 readiness matrix
              target_repo: {{targetRepo}}
            """);
    }

    private void WriteImplementationAndReview()
    {
        File.WriteAllText(Path.Combine(PacketDirectory(), "implementation.md"), "# Implementation\n");
        File.WriteAllText(Path.Combine(PacketDirectory(), "review-context.md"), "# Review context\n");
    }

    private void WriteGithubBody(string content) =>
        File.WriteAllText(Path.Combine(PacketDirectory(), "github-body.md"), content);

    private void WriteBinding(string regex)
    {
        var directory = Path.Combine(root, "intents", Domain, "automation");
        Directory.CreateDirectory(directory);
        File.WriteAllText(
            Path.Combine(directory, "bindings.md"),
            $"---\nexecution_unit_regex: '{regex}'\n---\n");
    }

    private string PacketDirectory() =>
        Path.Combine(root, ".intent-cli", "issues", ExecutionUnit);

    private static string CompleteGithubBody() =>
        "# G587 readiness matrix\n\n"
        + string.Join("\n\n", PublishContractSections.Required.Select(section => $"## {section}\nComplete."))
        + "\n";

    private static string[] Strings(JsonElement rootElement, string property) =>
        rootElement.GetProperty(property).EnumerateArray().Select(value => value.GetString()!).ToArray();

    private static string NormalizeWhitespace(string value) =>
        string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
