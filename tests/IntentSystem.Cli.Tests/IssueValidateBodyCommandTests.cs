using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

public sealed class IssueValidateBodyCommandTests
{
    [Fact]
    public void Execute_GivenCompleteIssueBody_ReturnsZeroExitAndReportsValid()
    {
        // Required scenario 1 (G183): a complete Child Issue Contract body must
        // exit 0 and report valid.
        using var workspace = new IssueValidateBodyWorkspace();
        workspace.WriteBody("issue.md", CompleteValidBody);
        using var writer = new StringWriter();

        var exitCode = IssueValidateBodyCommand.Execute(
            workspace.Context,
            ["--from-file", workspace.GetPath("issue.md")],
            writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("valid", output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Execute_GivenBodyMissingRequiredHeading_ReturnsNonZeroAndIdentifiesMissingHeading()
    {
        // Required scenario 2: missing required heading must exit non-zero and
        // identify the missing heading deterministically.
        using var workspace = new IssueValidateBodyWorkspace();
        var bodyWithoutVerification = CompleteValidBody.Replace(
            "## Verification\n\nRun the focused suite.\n\n",
            string.Empty,
            StringComparison.Ordinal);
        workspace.WriteBody("issue.md", bodyWithoutVerification);

        using var writer = new StringWriter();
        var exitCode = IssueValidateBodyCommand.Execute(
            workspace.Context,
            ["--from-file", workspace.GetPath("issue.md")],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("Verification", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenBodyWithEmptyRelatedLinks_ReturnsNonZeroAndExplainsRelatedLinks()
    {
        // Required scenario 3 (a): empty Related Links section must exit non-zero.
        using var workspace = new IssueValidateBodyWorkspace();
        var bodyWithEmptyLinks = CompleteValidBody.Replace(
            "## Related Links\n\n- Host runbook: intents/rules/automations/runbook.md",
            "## Related Links\n",
            StringComparison.Ordinal);
        workspace.WriteBody("issue.md", bodyWithEmptyLinks);

        using var writer = new StringWriter();
        var exitCode = IssueValidateBodyCommand.Execute(
            workspace.Context,
            ["--from-file", workspace.GetPath("issue.md"), "--format", "json"],
            writer);

        Assert.Equal(1, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.False(root.GetProperty("is_valid").GetBoolean());
        Assert.True(root.GetProperty("related_links_invalid").GetBoolean());
        Assert.NotNull(root.GetProperty("related_links_reason").GetString());
    }

    [Fact]
    public void Execute_GivenBodyWithPlaceholderOnlyRelatedLinks_ReturnsNonZero()
    {
        // Required scenario 3 (b): TODO/FIXME/placeholder-only Related Links
        // must exit non-zero. We exercise both list-marker styles and a couple
        // of placeholder spellings to lock the behaviour down.
        using var workspace = new IssueValidateBodyWorkspace();
        var placeholderLinks =
            "## Related Links\n\n- TODO\n- (FIXME)\n* placeholder\n- N/A";
        var body = CompleteValidBody.Replace(
            "## Related Links\n\n- Host runbook: intents/rules/automations/runbook.md",
            placeholderLinks,
            StringComparison.Ordinal);
        workspace.WriteBody("issue.md", body);

        using var writer = new StringWriter();
        var exitCode = IssueValidateBodyCommand.Execute(
            workspace.Context,
            ["--from-file", workspace.GetPath("issue.md")],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("Related Links", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenMissingFromFileFlag_ReturnsNonZero()
    {
        // Required scenario 4 (a): missing --from-file flag.
        using var workspace = new IssueValidateBodyWorkspace();
        using var writer = new StringWriter();

        var exitCode = IssueValidateBodyCommand.Execute(workspace.Context, [], writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("--from-file", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenBlankFromFileValue_ReturnsNonZero()
    {
        // Required scenario 4 (b): blank --from-file value.
        using var workspace = new IssueValidateBodyWorkspace();
        using var writer = new StringWriter();

        var exitCode = IssueValidateBodyCommand.Execute(
            workspace.Context,
            ["--from-file", "   "],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("--from-file", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenNonexistentInputFile_ReturnsNonZero()
    {
        // Required scenario 4 (c): nonexistent input file.
        using var workspace = new IssueValidateBodyWorkspace();
        using var writer = new StringWriter();

        var exitCode = IssueValidateBodyCommand.Execute(
            workspace.Context,
            ["--from-file", workspace.GetPath("missing.md")],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("does not exist", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenJsonFormat_EmitsParsableSnakeCaseFields()
    {
        using var workspace = new IssueValidateBodyWorkspace();
        workspace.WriteBody("issue.md", CompleteValidBody);

        using var writer = new StringWriter();
        var exitCode = IssueValidateBodyCommand.Execute(
            workspace.Context,
            ["--from-file", workspace.GetPath("issue.md"), "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.True(root.GetProperty("is_valid").GetBoolean());
        Assert.False(root.GetProperty("related_links_invalid").GetBoolean());
        Assert.Equal(JsonValueKind.Array, root.GetProperty("missing_headings").ValueKind);
        Assert.Equal(0, root.GetProperty("missing_headings").GetArrayLength());
        Assert.True(root.TryGetProperty("source_path", out _));
    }

    [Fact]
    public void Execute_GivenBodyWithHyphenTargetRepoVariant_AcceptsAsAlias()
    {
        // The host-review-loop runbook canonical heading is "Target Repo / Path / Part",
        // but earlier issues in the workflow use the "Target Repo-Path-Part" hyphen
        // variant. The validator should treat both as satisfying the contract.
        using var workspace = new IssueValidateBodyWorkspace();
        var hyphenVariant = CompleteValidBody.Replace(
            "## Target Repo / Path / Part",
            "## Target Repo-Path-Part",
            StringComparison.Ordinal);
        workspace.WriteBody("issue.md", hyphenVariant);

        using var writer = new StringWriter();
        var exitCode = IssueValidateBodyCommand.Execute(
            workspace.Context,
            ["--from-file", workspace.GetPath("issue.md")],
            writer);

        Assert.Equal(0, exitCode);
    }

    [Fact]
    public void Execute_GivenUnknownArgument_ReturnsNonZero()
    {
        using var workspace = new IssueValidateBodyWorkspace();
        workspace.WriteBody("issue.md", CompleteValidBody);

        using var writer = new StringWriter();
        var exitCode = IssueValidateBodyCommand.Execute(
            workspace.Context,
            ["--from-file", workspace.GetPath("issue.md"), "--bogus"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("--bogus", writer.ToString(), StringComparison.Ordinal);
    }

    private const string CompleteValidBody =
        """
        # G999 Example child issue body for validator tests

        ## Goal

        Stand-alone description of what this slice changes.

        ## Why This Slice Exists Now

        Surrounding rationale for cutting the slice now.

        ## Current Observed State

        Observed state described in actionable terms.

        ## Accepted Baseline You May Assume

        Baseline assumptions stated explicitly.

        ## Target Repo / Path / Part

        - Repository: J-Tech-Japan/intent-system
        - Likely areas: src/, tests/

        ## In Scope

        - The slice change itself.

        ## Out Of Scope

        - Adjacent refactors.

        ## Acceptance Criteria

        - Deterministic behaviour described here.

        ## Verification

        Run the focused suite.

        ## Related Links

        - Host runbook: intents/rules/automations/runbook.md
        """;

    private sealed class IssueValidateBodyWorkspace : IDisposable
    {
        private readonly string rootPath = Directory
            .CreateTempSubdirectory("issue-validate-body-tests-")
            .FullName;

        public IssueValidateBodyWorkspace()
        {
            Directory.CreateDirectory(Path.Combine(rootPath, ".intent-cli"));
            Context = new CliContext
            {
                RepoRoot = rootPath,
                Config = new CliConfig
                {
                    Project = new ProjectConfig
                    {
                        Domain = "intent-cli",
                        ArtifactRoot = ".intent-cli"
                    }
                }
            };
        }

        public CliContext Context { get; }

        public string GetPath(string relative) => Path.Combine(rootPath, relative);

        public void WriteBody(string relative, string content)
        {
            File.WriteAllText(GetPath(relative), content);
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
