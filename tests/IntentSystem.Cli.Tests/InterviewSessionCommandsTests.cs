using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

public sealed class InterviewSessionCommandsTests
{
    [Fact]
    public void NextQuestion_GivenSeededPending_ReturnsFirstPendingId()
    {
        using var workspace = new InterviewSessionWorkspace();
        workspace.SeedSession("intent-cli", "alpha", new[]
        {
            (Id: "q1", Prompt: "Goal scope?", Answer: "scoped"),
            (Id: "q2", Prompt: "Verification approach?", Answer: (string?)null),
            (Id: "q3", Prompt: "Out of scope?", Answer: (string?)null)
        });

        using var writer = new StringWriter();
        var exitCode = InterviewNextQuestionCommand.Execute(
            workspace.Context,
            ["--session", "alpha", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.True(root.GetProperty("session_exists").GetBoolean());
        Assert.Equal(3, root.GetProperty("total_questions").GetInt32());
        Assert.Equal(2, root.GetProperty("pending_count").GetInt32());
        Assert.True(root.GetProperty("has_pending").GetBoolean());
        Assert.Equal("q2", root.GetProperty("pending").GetProperty("id").GetString());
    }

    [Fact]
    public void NextQuestion_GivenAllAnswered_ReportsHasPendingFalse()
    {
        using var workspace = new InterviewSessionWorkspace();
        workspace.SeedSession("intent-cli", "alpha", new[]
        {
            (Id: "q1", Prompt: "Done?", Answer: "yes")
        });

        using var writer = new StringWriter();
        var exitCode = InterviewNextQuestionCommand.Execute(
            workspace.Context,
            ["--session", "alpha", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.False(root.GetProperty("has_pending").GetBoolean());
        Assert.Equal(0, root.GetProperty("pending_count").GetInt32());
    }

    [Fact]
    public void NextQuestion_GivenMissingSession_ReportsSessionDoesNotExist()
    {
        using var workspace = new InterviewSessionWorkspace();
        using var writer = new StringWriter();

        var exitCode = InterviewNextQuestionCommand.Execute(
            workspace.Context,
            ["--session", "missing", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.False(root.GetProperty("session_exists").GetBoolean());
        Assert.Equal(0, root.GetProperty("total_questions").GetInt32());
        Assert.False(root.GetProperty("has_pending").GetBoolean());
    }

    [Fact]
    public void NextQuestion_MissingSession_ReturnsUsageError()
    {
        using var workspace = new InterviewSessionWorkspace();
        using var writer = new StringWriter();

        var exitCode = InterviewNextQuestionCommand.Execute(
            workspace.Context,
            [],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("--session is required", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void RecordAnswer_GivenWriteOnExistingPendingQuestion_PersistsAnswer()
    {
        using var workspace = new InterviewSessionWorkspace();
        workspace.SeedSession("intent-cli", "alpha", new[]
        {
            (Id: "q1", Prompt: "Goal?", Answer: (string?)null)
        });
        var answerFile = workspace.WriteAnswerFile("alpha-q1.txt", "Add deterministic CLI surfaces.");

        using var writer = new StringWriter();
        var exitCode = InterviewRecordAnswerCommand.Execute(
            workspace.Context,
            ["--session", "alpha", "--question", "q1", "--from-file", answerFile, "--write", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.Equal("write", root.GetProperty("mode").GetString());
        Assert.False(root.GetProperty("newly_added").GetBoolean());
        Assert.Equal(0, root.GetProperty("pending_count").GetInt32());

        var stored = workspace.ReadSession("intent-cli", "alpha");
        Assert.Single(stored.Questions);
        Assert.Equal("Add deterministic CLI surfaces.", stored.Questions[0].Answer);
    }

    [Fact]
    public void RecordAnswer_DryRun_DoesNotWriteFile()
    {
        using var workspace = new InterviewSessionWorkspace();
        workspace.SeedSession("intent-cli", "alpha", new[]
        {
            (Id: "q1", Prompt: "Goal?", Answer: (string?)null)
        });
        var answerFile = workspace.WriteAnswerFile("answer.txt", "draft");

        using var writer = new StringWriter();
        var exitCode = InterviewRecordAnswerCommand.Execute(
            workspace.Context,
            ["--session", "alpha", "--question", "q1", "--from-file", answerFile, "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        Assert.Equal("dry-run", document.RootElement.GetProperty("mode").GetString());

        var stored = workspace.ReadSession("intent-cli", "alpha");
        Assert.Null(stored.Questions[0].Answer);
    }

    [Fact]
    public void RecordAnswer_WithPromptOnNewQuestion_AppendsEntry()
    {
        using var workspace = new InterviewSessionWorkspace();
        var answerFile = workspace.WriteAnswerFile("first.txt", "yes");

        using var writer = new StringWriter();
        var exitCode = InterviewRecordAnswerCommand.Execute(
            workspace.Context,
            ["--session", "alpha", "--question", "q1", "--from-file", answerFile, "--prompt", "Is in scope?", "--write", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        Assert.True(document.RootElement.GetProperty("newly_added").GetBoolean());
        var stored = workspace.ReadSession("intent-cli", "alpha");
        Assert.Single(stored.Questions);
        Assert.Equal("q1", stored.Questions[0].Id);
        Assert.Equal("Is in scope?", stored.Questions[0].Prompt);
        Assert.Equal("yes", stored.Questions[0].Answer);
    }

    [Fact]
    public void RecordAnswer_NewQuestionWithoutPrompt_ReturnsError()
    {
        using var workspace = new InterviewSessionWorkspace();
        var answerFile = workspace.WriteAnswerFile("a.txt", "yes");

        using var writer = new StringWriter();
        var exitCode = InterviewRecordAnswerCommand.Execute(
            workspace.Context,
            ["--session", "alpha", "--question", "q-new", "--from-file", answerFile, "--write", "--format", "json"],
            writer);

        Assert.Equal(1, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        Assert.Contains("no question with id 'q-new'", document.RootElement.GetProperty("error").GetString()!, StringComparison.Ordinal);
    }

    [Fact]
    public void RecordAnswer_MissingAnswerFile_ReturnsError()
    {
        using var workspace = new InterviewSessionWorkspace();

        using var writer = new StringWriter();
        var exitCode = InterviewRecordAnswerCommand.Execute(
            workspace.Context,
            ["--session", "alpha", "--question", "q1", "--from-file", "/tmp/does-not-exist", "--write", "--format", "json"],
            writer);

        Assert.Equal(1, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        Assert.Contains("answer file not found", document.RootElement.GetProperty("error").GetString()!, StringComparison.Ordinal);
    }

    [Fact]
    public void RecordAnswer_MissingFlags_ReturnUsageErrors()
    {
        using var workspace = new InterviewSessionWorkspace();

        using var writer = new StringWriter();
        var exitCode = InterviewRecordAnswerCommand.Execute(
            workspace.Context,
            ["--question", "q1"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("--session is required", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void RecordAnswer_HelpFlag_PrintsUsage()
    {
        using var workspace = new InterviewSessionWorkspace();

        using var writer = new StringWriter();
        var exitCode = InterviewRecordAnswerCommand.Execute(
            workspace.Context,
            ["--help"],
            writer);

        Assert.Equal(0, exitCode);
        Assert.Contains("interview record-answer", writer.ToString(), StringComparison.Ordinal);
    }

    private sealed class InterviewSessionWorkspace : IDisposable
    {
        private readonly string rootPath = Directory
            .CreateTempSubdirectory("interview-session-tests-")
            .FullName;

        public InterviewSessionWorkspace()
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
                        ArtifactRoot = ".intent-cli",
                        WorktreeRoot = ".intent-cli/worktrees"
                    }
                }
            };
        }

        public CliContext Context { get; }

        public void SeedSession(string domain, string session, IEnumerable<(string Id, string Prompt, string? Answer)> questions)
        {
            var path = InterviewSessionStore.ResolvePath(rootPath, domain, session);
            var stored = new InterviewSession
            {
                Session = session,
                Domain = domain,
                Questions = questions
                    .Select(q => new InterviewQuestion { Id = q.Id, Prompt = q.Prompt, Answer = q.Answer })
                    .ToList()
            };
            InterviewSessionStore.Write(path, stored);
        }

        public string WriteAnswerFile(string name, string content)
        {
            var path = Path.Combine(rootPath, name);
            File.WriteAllText(path, content);
            return path;
        }

        public InterviewSession ReadSession(string domain, string session)
        {
            var path = InterviewSessionStore.ResolvePath(rootPath, domain, session);
            return InterviewSessionStore.Read(path)
                ?? throw new InvalidOperationException($"Session not found at {path}");
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
