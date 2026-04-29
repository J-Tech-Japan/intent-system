using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

public sealed class ClarifyRecordCommandTests
{
    [Fact]
    public void Execute_GivenValidDecisionArtifact_RecordsEntryAndPreservesExistingContent()
    {
        // Required scenario 1 (G182): successful record. The accepted decision must
        // appear under the "## Recently Resolved" section, the existing
        // "## Current Open Blockers" entries must remain intact, and the file
        // must still parse as the host-shape clarification artifact.
        using var workspace = new ClarifyRecordWorkspace();
        const string existing =
            """
            # intent-cli clarifications

            durable prose preserved by the recorder.

            ## Current Open Blockers

            - 現時点で child issue cut を要する root blocker はない。

            ## Recently Resolved

            - 2026-04-28T12:00:00Z — Earlier accepted decision
              - Decision: ship G179 as a status brief command.
            """;
        workspace.WriteClarification(existing);
        workspace.WriteDecisionArtifact(
            """
            # Clarification decision

            ## Question
            Should we ship clarify draft as the structured A/B option scaffold?

            ## Decision
            Yes — proceed with the structured scaffold approach.

            ## Rationale
            The shape produced by G181 is enough to support owner review.
            """);

        using var writer = new StringWriter();
        var exitCode = ClarifyRecordCommand.Execute(
            workspace.Context,
            ["--from-file", workspace.DecisionArtifactPath],
            writer);

        Assert.Equal(0, exitCode);
        var updated = workspace.ReadClarification();
        Assert.Contains("Should we ship clarify draft as the structured A/B option scaffold?", updated, StringComparison.Ordinal);
        Assert.Contains("Decision: Yes — proceed with the structured scaffold approach.", updated, StringComparison.Ordinal);
        Assert.Contains("Rationale: The shape produced by G181 is enough", updated, StringComparison.Ordinal);
        // Existing content preserved.
        Assert.Contains("durable prose preserved by the recorder.", updated, StringComparison.Ordinal);
        Assert.Contains("現時点で child issue cut を要する root blocker はない", updated, StringComparison.Ordinal);
        Assert.Contains("Earlier accepted decision", updated, StringComparison.Ordinal);

        // New entry appears above the earlier "Recently Resolved" entry (newest-first).
        var newIndex = updated.IndexOf("Should we ship clarify draft", StringComparison.Ordinal);
        var oldIndex = updated.IndexOf("Earlier accepted decision", StringComparison.Ordinal);
        Assert.True(newIndex >= 0 && oldIndex >= 0);
        Assert.True(newIndex < oldIndex, "expected newest decision to appear above the older one");

        Assert.Contains("Recorded decision into", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenDryRunFlag_PrintsIntendedChangeAndDoesNotMutateFile()
    {
        // Required scenario 2: dry-run no-mutation. Stdout shows the entry; the
        // file on disk is byte-identical to the pre-call content.
        using var workspace = new ClarifyRecordWorkspace();
        var existing = "# clar\n\n## Current Open Blockers\n\n- still here\n";
        workspace.WriteClarification(existing);
        workspace.WriteDecisionArtifact(
            """
            # Clarification decision

            ## Question
            Should we run the dry-run path?

            ## Decision
            Yes — dry-run path must not mutate.
            """);

        using var writer = new StringWriter();
        var exitCode = ClarifyRecordCommand.Execute(
            workspace.Context,
            ["--from-file", workspace.DecisionArtifactPath, "--dry-run"],
            writer);

        Assert.Equal(0, exitCode);
        Assert.Equal(existing, workspace.ReadClarification());
        var output = writer.ToString();
        Assert.Contains("Would record decision into", output, StringComparison.Ordinal);
        Assert.Contains("Should we run the dry-run path?", output, StringComparison.Ordinal);
        Assert.Contains("Decision: Yes — dry-run path must not mutate.", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenMalformedDecisionArtifact_FailsBeforeMutation()
    {
        // Required scenario 3: malformed input. Missing the "## Decision"
        // section must fail with a clear error before any mutation.
        using var workspace = new ClarifyRecordWorkspace();
        var existing = "# clar\n\n## Current Open Blockers\n\n- still here\n";
        workspace.WriteClarification(existing);
        workspace.WriteDecisionArtifact(
            """
            # Clarification decision

            ## Question
            Should we ship clarify draft now?
            """);

        using var writer = new StringWriter();
        var exitCode = ClarifyRecordCommand.Execute(
            workspace.Context,
            ["--from-file", workspace.DecisionArtifactPath],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Equal(existing, workspace.ReadClarification());
        Assert.Contains("Decision", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenMissingClarificationReturnPath_FailsWithClearError()
    {
        // Required scenario 4: missing return path. clarifications/open.md does
        // not exist for the resolved domain; must fail with a clear error and no
        // mutation. Any neighbouring host files must remain untouched.
        using var workspace = new ClarifyRecordWorkspace(writeClarification: false);
        workspace.WriteDecisionArtifact(
            """
            # Clarification decision

            ## Question
            Q?

            ## Decision
            A.
            """);

        using var writer = new StringWriter();
        var exitCode = ClarifyRecordCommand.Execute(
            workspace.Context,
            ["--from-file", workspace.DecisionArtifactPath],
            writer);

        Assert.Equal(1, exitCode);
        var output = writer.ToString();
        Assert.Contains("Clarification return path", output, StringComparison.Ordinal);
        Assert.Contains("does not exist", output, StringComparison.Ordinal);
        Assert.False(File.Exists(workspace.ClarificationPath));
    }

    [Fact]
    public void Execute_GivenMissingFromFileFlag_ReturnsErrorExitCode()
    {
        using var workspace = new ClarifyRecordWorkspace();
        using var writer = new StringWriter();

        var exitCode = ClarifyRecordCommand.Execute(workspace.Context, [], writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("--from-file", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenFromFilePathThatDoesNotExist_ReturnsErrorExitCode()
    {
        using var workspace = new ClarifyRecordWorkspace();
        using var writer = new StringWriter();

        var exitCode = ClarifyRecordCommand.Execute(
            workspace.Context,
            ["--from-file", Path.Combine(workspace.RootPath, "missing.md")],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("does not exist", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenUnknownArgument_ReturnsErrorExitCode()
    {
        using var workspace = new ClarifyRecordWorkspace();
        workspace.WriteDecisionArtifact(MinimalArtifact);
        using var writer = new StringWriter();

        var exitCode = ClarifyRecordCommand.Execute(
            workspace.Context,
            ["--from-file", workspace.DecisionArtifactPath, "--bogus"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("--bogus", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenDomainOverride_ResolvesReturnPathUnderOverrideDomain()
    {
        using var workspace = new ClarifyRecordWorkspace(writeClarification: false);
        workspace.WriteDecisionArtifact(MinimalArtifact);
        var altPath = Path.Combine(workspace.RootPath, "intents", "alt-domain", "clarifications", "open.md");
        Directory.CreateDirectory(Path.GetDirectoryName(altPath)!);
        File.WriteAllText(altPath, "# alt clarifications\n\n## Recently Resolved\n");

        using var writer = new StringWriter();
        var exitCode = ClarifyRecordCommand.Execute(
            workspace.Context,
            ["--domain", "alt-domain", "--from-file", workspace.DecisionArtifactPath],
            writer);

        Assert.Equal(0, exitCode);
        var content = File.ReadAllText(altPath);
        Assert.Contains("Q?", content, StringComparison.Ordinal);
        Assert.Contains("Decision: A.", content, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenClarificationFileWithoutRecentlyResolvedSection_AppendsSection()
    {
        using var workspace = new ClarifyRecordWorkspace();
        var existing = "# clar\n\n## Current Open Blockers\n\n- still here\n";
        workspace.WriteClarification(existing);
        workspace.WriteDecisionArtifact(MinimalArtifact);

        using var writer = new StringWriter();
        var exitCode = ClarifyRecordCommand.Execute(
            workspace.Context,
            ["--from-file", workspace.DecisionArtifactPath],
            writer);

        Assert.Equal(0, exitCode);
        var updated = workspace.ReadClarification();
        Assert.Contains("- still here", updated, StringComparison.Ordinal);
        Assert.Contains("## Recently Resolved", updated, StringComparison.Ordinal);
        Assert.Contains("Q?", updated, StringComparison.Ordinal);
    }

    private const string MinimalArtifact =
        """
        # Clarification decision

        ## Question
        Q?

        ## Decision
        A.
        """;

    private sealed class ClarifyRecordWorkspace : IDisposable
    {
        public ClarifyRecordWorkspace(bool writeClarification = true)
        {
            RootPath = Directory.CreateTempSubdirectory("clarify-record-tests-").FullName;
            Directory.CreateDirectory(Path.Combine(RootPath, ".intent-cli"));
            ClarificationPath = Path.Combine(
                RootPath,
                "intents",
                "intent-cli",
                "clarifications",
                "open.md");
            DecisionArtifactPath = Path.Combine(RootPath, "decision.md");

            if (writeClarification)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ClarificationPath)!);
                File.WriteAllText(ClarificationPath, "# placeholder\n");
            }

            Context = new CliContext
            {
                RepoRoot = RootPath,
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

        public string RootPath { get; }

        public string ClarificationPath { get; }

        public string DecisionArtifactPath { get; }

        public void WriteClarification(string content)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ClarificationPath)!);
            File.WriteAllText(ClarificationPath, content);
        }

        public string ReadClarification() => File.ReadAllText(ClarificationPath);

        public void WriteDecisionArtifact(string content)
        {
            File.WriteAllText(DecisionArtifactPath, content);
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
