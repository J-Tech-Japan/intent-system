using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G569: this class no longer saves and restores a process-global clock. It
/// passes one to the call it is testing, so nothing it does is observable by
/// another test class — and it needs no <c>IDisposable</c> to stay that way.
/// </summary>
public sealed class IssuePrepareCommandTests
{
    /// <summary>The clock these fixtures pin their expected timestamps to.</summary>
    private static Func<DateTimeOffset> ClockAt(int hour, int minute, int second) =>
        () => new DateTimeOffset(2026, 4, 28, hour, minute, second, TimeSpan.Zero);

    [Fact]
    public void Execute_GivenValidBody_WritesReviewedPacketAndReturnsZero()
    {
        using var workspace = new IssuePrepareWorkspace();
        workspace.WriteFile("body.md", CompleteValidBody);
        var bodyPath = workspace.GetPath("body.md");
        var outPath = workspace.GetPath("packet.json");
        using var writer = new StringWriter();
        var exitCode = IssuePrepareCommand.Execute(
            workspace.Context,
            ["--from-file", bodyPath, "--execution-unit", "G184", "--title", "G184 sample title", "--out", outPath],
            writer,
            ClockAt(12, 34, 56));

        Assert.Equal(0, exitCode);
        Assert.True(File.Exists(outPath));
        Assert.Contains("Prepared reviewed publish packet for G184", writer.ToString(), StringComparison.Ordinal);

        using var doc = JsonDocument.Parse(File.ReadAllText(outPath));
        var root = doc.RootElement;
        Assert.Equal("G184", root.GetProperty("execution_unit").GetString());
        Assert.Equal("G184 sample title", root.GetProperty("title").GetString());
        Assert.Equal(bodyPath, root.GetProperty("source_path").GetString());
        Assert.Equal("valid", root.GetProperty("validation_status").GetString());
        Assert.True(root.GetProperty("reviewed").GetBoolean());
        Assert.False(root.GetProperty("published").GetBoolean());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("issue_number").ValueKind);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("issue_url").ValueKind);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("published_at_utc").ValueKind);
        Assert.Equal("2026-04-28T12:34:56.000Z", root.GetProperty("prepared_at_utc").GetString());
        var sha = root.GetProperty("source_body_sha256").GetString();
        Assert.NotNull(sha);
        Assert.Equal(64, sha!.Length);
    }

    [Fact]
    public void Execute_GivenInvalidBody_ReturnsNonZeroAndWritesNoPacket()
    {
        using var workspace = new IssuePrepareWorkspace();
        var brokenBody = CompleteValidBody.Replace(
            "## Verification",
            "## Not Verification",
            StringComparison.Ordinal);
        workspace.WriteFile("body.md", brokenBody);
        var outPath = workspace.GetPath("packet.json");

        using var writer = new StringWriter();
        var exitCode = IssuePrepareCommand.Execute(
            workspace.Context,
            ["--from-file", workspace.GetPath("body.md"), "--execution-unit", "G184", "--title", "t", "--out", outPath],
            writer);

        Assert.Equal(1, exitCode);
        Assert.False(File.Exists(outPath));
        Assert.Contains("Verification", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenMissingFlag_ReturnsNonZero()
    {
        using var workspace = new IssuePrepareWorkspace();
        workspace.WriteFile("body.md", CompleteValidBody);

        using var writer = new StringWriter();
        // missing --out
        var exitCode = IssuePrepareCommand.Execute(
            workspace.Context,
            ["--from-file", workspace.GetPath("body.md"), "--execution-unit", "G184", "--title", "t"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("--out", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenNonexistentBodyFile_ReturnsNonZero()
    {
        using var workspace = new IssuePrepareWorkspace();
        var outPath = workspace.GetPath("packet.json");

        using var writer = new StringWriter();
        var exitCode = IssuePrepareCommand.Execute(
            workspace.Context,
            ["--from-file", workspace.GetPath("missing.md"), "--execution-unit", "G184", "--title", "t", "--out", outPath],
            writer);

        Assert.Equal(1, exitCode);
        Assert.False(File.Exists(outPath));
        Assert.Contains("does not exist", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenValidBody_PacketJsonRoundTripsStably()
    {
        using var workspace = new IssuePrepareWorkspace();
        workspace.WriteFile("body.md", CompleteValidBody);
        var outPath = workspace.GetPath("packet.json");
        using var writer = new StringWriter();
        var exitCode = IssuePrepareCommand.Execute(
            workspace.Context,
            ["--from-file", workspace.GetPath("body.md"), "--execution-unit", "G184", "--title", "G184 RT", "--out", outPath],
            writer,
            ClockAt(9, 0, 0));

        Assert.Equal(0, exitCode);

        var json = File.ReadAllText(outPath);
        var packet = JsonSerializer.Deserialize<IssueReviewedPublishPacket>(json);
        Assert.NotNull(packet);
        Assert.Equal("G184", packet!.ExecutionUnit);
        Assert.Equal("G184 RT", packet.Title);
        Assert.Equal(workspace.GetPath("body.md"), packet.SourcePath);
        Assert.Equal("valid", packet.ValidationStatus);
        Assert.True(packet.Reviewed);
        Assert.False(packet.Published);
        Assert.Null(packet.IssueNumber);
        Assert.Null(packet.IssueUrl);
        Assert.Null(packet.PublishedAtUtc);
        Assert.Equal("2026-04-28T09:00:00.000Z", packet.PreparedAtUtc);

        // Round-trip again to confirm stable shape.
        var again = JsonSerializer.Serialize(packet);
        var packet2 = JsonSerializer.Deserialize<IssueReviewedPublishPacket>(again);
        Assert.NotNull(packet2);
        Assert.Equal(packet.SourceBodySha256, packet2!.SourceBodySha256);
        Assert.Equal(packet.PreparedAtUtc, packet2.PreparedAtUtc);
        Assert.Equal(packet.Title, packet2.Title);
        Assert.Equal(packet.ExecutionUnit, packet2.ExecutionUnit);
    }

    // Hand-crafted minimal Markdown body satisfying all 10 G183 required headings.
    // NOTE: closing `"""` does not add a trailing newline, so any subsequent
    // string.Replace must not assume one.
    internal const string CompleteValidBody =
        """
        # G184 Sample child issue body for prepare/publish-reviewed tests

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

        ## Base Branch Policy

        Policy: `direct-main`
        Expected PR base branch: `main`
        Open all child PRs against `main` directly.
        """;

    /// <summary>
    /// G569: the direct proof. Many callers run <c>issue prepare</c>
    /// CONCURRENTLY with DIFFERENT clocks, and every one must observe its own —
    /// which is possible only because there is no shared cell left to observe.
    ///
    /// Against the removed static this fails deterministically rather than
    /// occasionally: the last writer wins, so the recorded timestamps collapse
    /// onto whichever clock was assigned last. That is the same interleaving
    /// that turned one full-suite run red on 2026-08-01, reproduced on demand
    /// instead of waited for.
    /// </summary>
    [Fact]
    public void Execute_ConcurrentCallsWithDifferentClocks_EachObservesItsOwn_G569()
    {
        const int Callers = 32;
        using var workspace = new IssuePrepareWorkspace();
        workspace.WriteFile("body.md", CompleteValidBody);
        var bodyPath = workspace.GetPath("body.md");

        var recorded = new string?[Callers];
        Parallel.For(0, Callers, index =>
        {
            var outPath = workspace.GetPath($"packet-{index}.json");
            using var writer = new StringWriter();
            var exitCode = IssuePrepareCommand.Execute(
                workspace.Context,
                ["--from-file", bodyPath, "--execution-unit", $"G{index}", "--title", $"title {index}", "--out", outPath],
                writer,
                // A distinct second per caller: any leakage between callers
                // shows up as a duplicated or wrong timestamp.
                () => new DateTimeOffset(2026, 4, 28, 0, 0, index, TimeSpan.Zero));

            Assert.Equal(0, exitCode);
            using var document = JsonDocument.Parse(File.ReadAllText(outPath));
            recorded[index] = document.RootElement.GetProperty("prepared_at_utc").GetString();
        });

        for (var index = 0; index < Callers; index++)
        {
            Assert.Equal($"2026-04-28T00:00:{index:D2}.000Z", recorded[index]);
        }

        // And no two callers share a timestamp — the assertion above already
        // implies it, but stating it separately is what a leak would trip first.
        Assert.Equal(Callers, recorded.Distinct(StringComparer.Ordinal).Count());
    }

    internal sealed class IssuePrepareWorkspace : IDisposable
    {
        private readonly string rootPath = Directory
            .CreateTempSubdirectory("issue-prepare-tests-")
            .FullName;

        public IssuePrepareWorkspace()
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

        public void WriteFile(string relative, string content)
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
