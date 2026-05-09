using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

public sealed class ClarificationCommandTests : IDisposable
{
    public ClarificationCommandTests()
    {
        ClarificationCommand.UtcNowFactory = null;
    }

    public void Dispose()
    {
        ClarificationCommand.UtcNowFactory = null;
    }

    [Fact]
    public void Status_NoDirectory_ReturnsZeroCounts()
    {
        using var workspace = new ClarificationWorkspace();
        using var writer = new StringWriter();

        var exitCode = ClarificationCommand.ExecuteStatus(
            workspace.Context,
            ["--domain", "demo", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.Equal(0, doc.RootElement.GetProperty("open_count").GetInt32());
        Assert.Equal(0, doc.RootElement.GetProperty("answered_count").GetInt32());
        Assert.Empty(doc.RootElement.GetProperty("items").EnumerateArray());
    }

    [Fact]
    public void Status_OneOpenAndOneAnswered_ReportsCounts()
    {
        using var workspace = new ClarificationWorkspace();
        workspace.WriteClarification("demo", "open-q", BuildOpenToml("open-q", "Question A?"));
        workspace.WriteClarification("demo", "answered-q", BuildAnsweredToml("answered-q", "Question B?", "yes"));
        using var writer = new StringWriter();

        ClarificationCommand.ExecuteStatus(
            workspace.Context,
            ["--domain", "demo", "--format", "json"],
            writer);

        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.Equal(1, doc.RootElement.GetProperty("open_count").GetInt32());
        Assert.Equal(1, doc.RootElement.GetProperty("answered_count").GetInt32());
        Assert.Equal(2, doc.RootElement.GetProperty("items").GetArrayLength());
    }

    [Fact]
    public void Next_ReturnsFirstOpenWithStructuredFields()
    {
        using var workspace = new ClarificationWorkspace();
        workspace.WriteClarification("demo", "storage", BuildOpenToml("storage", "Which backend?"));
        using var writer = new StringWriter();

        var exitCode = ClarificationCommand.ExecuteNext(
            workspace.Context,
            ["--domain", "demo", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.True(doc.RootElement.GetProperty("has_open").GetBoolean());
        var c = doc.RootElement.GetProperty("clarification");
        Assert.Equal("storage", c.GetProperty("id").GetString());
        Assert.Equal("Which backend?", c.GetProperty("question").GetString());
        Assert.Equal(2, c.GetProperty("options").GetArrayLength());
        Assert.Equal("yes", c.GetProperty("recommendation").GetString());
        Assert.Contains(
            c.GetProperty("blocks").EnumerateArray(),
            e => e.GetString() == "TF-G3");
    }

    [Fact]
    public void Next_NoOpenClarification_ReturnsHasOpenFalse()
    {
        using var workspace = new ClarificationWorkspace();
        workspace.WriteClarification("demo", "answered-q", BuildAnsweredToml("answered-q", "Q?", "yes"));
        using var writer = new StringWriter();

        ClarificationCommand.ExecuteNext(
            workspace.Context,
            ["--domain", "demo", "--format", "json"],
            writer);

        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.False(doc.RootElement.GetProperty("has_open").GetBoolean());
        Assert.Equal(JsonValueKind.Null, doc.RootElement.GetProperty("clarification").ValueKind);
    }

    [Fact]
    public void Answer_WriteRecordsChoiceNoteAndTimestamp_AndMarksAnswered()
    {
        using var workspace = new ClarificationWorkspace();
        workspace.WriteClarification("demo", "storage", BuildOpenToml("storage", "Which backend?"));
        ClarificationCommand.UtcNowFactory = () => new DateTimeOffset(2026, 5, 8, 12, 34, 56, TimeSpan.Zero);

        using var writer = new StringWriter();
        var exitCode = ClarificationCommand.ExecuteAnswer(
            workspace.Context,
            ["--domain", "demo", "--id", "storage", "--choice", "yes", "--note", "Approved 2026-05-08", "--write", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.Equal("write", doc.RootElement.GetProperty("mode").GetString());
        var c = doc.RootElement.GetProperty("clarification");
        Assert.Equal("answered", c.GetProperty("status").GetString());
        var ans = c.GetProperty("answer");
        Assert.Equal("yes", ans.GetProperty("choice").GetString());
        Assert.Equal("Approved 2026-05-08", ans.GetProperty("note").GetString());
        Assert.Equal("2026-05-08T12:34:56Z", ans.GetProperty("answered_at").GetString());

        // File on disk reflects the change.
        var path = StructuredClarificationsDirectory.ResolveFile(workspace.RepoRoot, "demo", "storage");
        var roundTrip = StructuredClarificationToml.Deserialize(File.ReadAllText(path), sourcePath: path);
        Assert.False(roundTrip.IsOpen());
        Assert.Equal("yes", roundTrip.Answer!.Choice);
    }

    [Fact]
    public void Answer_DryRunByDefault_DoesNotMutateFile()
    {
        using var workspace = new ClarificationWorkspace();
        workspace.WriteClarification("demo", "storage", BuildOpenToml("storage", "Which backend?"));
        var path = StructuredClarificationsDirectory.ResolveFile(workspace.RepoRoot, "demo", "storage");
        var before = File.ReadAllText(path);

        using var writer = new StringWriter();
        ClarificationCommand.ExecuteAnswer(
            workspace.Context,
            ["--domain", "demo", "--id", "storage", "--choice", "yes", "--format", "json"],
            writer);

        Assert.Equal(before, File.ReadAllText(path));
        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.Equal("dry-run", doc.RootElement.GetProperty("mode").GetString());
    }

    [Fact]
    public void Answer_RejectsUnknownChoice()
    {
        using var workspace = new ClarificationWorkspace();
        workspace.WriteClarification("demo", "storage", BuildOpenToml("storage", "Which backend?"));
        using var writer = new StringWriter();

        var exitCode = ClarificationCommand.ExecuteAnswer(
            workspace.Context,
            ["--domain", "demo", "--id", "storage", "--choice", "azure-blob", "--write"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("not one of the recorded option ids", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Answer_MissingId_ReturnsExitCodeOne()
    {
        using var workspace = new ClarificationWorkspace();
        using var writer = new StringWriter();

        var exitCode = ClarificationCommand.ExecuteAnswer(
            workspace.Context,
            ["--domain", "demo", "--choice", "yes", "--write"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("--id", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void IntentStatus_Reports_ClarificationOpen_WhenStructuredOpen()
    {
        using var workspace = new ClarificationWorkspace();
        workspace.WriteClarification("demo", "storage", BuildOpenToml("storage", "Which backend?"));

        var result = IntentStatusCommand.Analyze(workspace.Context, "demo");

        Assert.True(result.ClarificationOpen);
    }

    [Fact]
    public void IntentStatus_Reports_ClarificationClosed_WhenAllAnswered()
    {
        using var workspace = new ClarificationWorkspace();
        workspace.WriteClarification("demo", "storage", BuildAnsweredToml("storage", "Which backend?", "yes"));

        var result = IntentStatusCommand.Analyze(workspace.Context, "demo");

        Assert.False(result.ClarificationOpen);
    }

    private static string BuildOpenToml(string id, string question) =>
        $$"""
        id = "{{id}}"
        status = "open"
        question = "{{question}}"
        recommendation = "yes"
        blocks = ["TF-G3"]

        [[options]]
        id = "yes"
        label = "Yes"
        pros = ["Simple"]
        cons = []

        [[options]]
        id = "no"
        label = "No"
        pros = ["Defer"]
        cons = ["Blocks"]
        """;

    private static string BuildAnsweredToml(string id, string question, string choice) =>
        $$"""
        id = "{{id}}"
        status = "answered"
        question = "{{question}}"
        recommendation = "yes"
        blocks = ["TF-G3"]

        [[options]]
        id = "yes"
        label = "Yes"
        pros = ["Simple"]
        cons = []

        [answer]
        choice = "{{choice}}"
        answered_at = "2026-05-08T00:00:00Z"
        """;

    private sealed class ClarificationWorkspace : IDisposable
    {
        public ClarificationWorkspace()
        {
            RepoRoot = Directory.CreateTempSubdirectory("clarification-tests-").FullName;
            Context = new CliContext
            {
                RepoRoot = RepoRoot,
                Config = new CliConfig
                {
                    Project = new ProjectConfig
                    {
                        Domain = "demo",
                        ArtifactRoot = ".intent-cli",
                        WorktreeRoot = ".intent-cli/worktrees"
                    }
                }
            };
        }

        public string RepoRoot { get; }

        public CliContext Context { get; }

        public void WriteClarification(string domain, string id, string toml)
        {
            var dir = Path.Combine(RepoRoot, "intents", domain, "clarifications");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, id + ".toml"), toml);
        }

        public void Dispose()
        {
            if (Directory.Exists(RepoRoot))
            {
                Directory.Delete(RepoRoot, recursive: true);
            }
        }
    }
}
