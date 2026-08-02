using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

public sealed class InterviewCompileAndDraftCommandsTests
{
    [Fact]
    public void Compile_GivenMixedQuestions_BucketsAcceptedAndOpen()
    {
        using var workspace = new InterviewSessionWorkspace();
        workspace.SeedSession("intent-cli", "alpha", new[]
        {
            (Id: "q1", Prompt: "Goal?", Answer: "deterministic CLI"),
            (Id: "q2", Prompt: "Verification?", Answer: (string?)null),
            (Id: "q3", Prompt: "In Scope?", Answer: "guide commands")
        });

        using var writer = new StringWriter();
        var exitCode = InterviewCompileCommand.Execute(
            workspace.Context,
            ["--session", "alpha", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.True(root.GetProperty("session_exists").GetBoolean());
        Assert.True(root.GetProperty("ready").GetBoolean());
        Assert.Equal(2, root.GetProperty("accepted_count").GetInt32());
        Assert.Equal(1, root.GetProperty("open_count").GetInt32());
        Assert.Equal(2, root.GetProperty("accepted_baseline").GetArrayLength());
        Assert.Equal(1, root.GetProperty("open_questions").GetArrayLength());
        Assert.Equal("q2", root.GetProperty("open_questions")[0].GetProperty("id").GetString());
    }

    [Fact]
    public void Compile_GivenMissingSession_ReportsNotReady()
    {
        using var workspace = new InterviewSessionWorkspace();
        using var writer = new StringWriter();

        var exitCode = InterviewCompileCommand.Execute(
            workspace.Context,
            ["--session", "missing", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        Assert.False(document.RootElement.GetProperty("session_exists").GetBoolean());
        Assert.False(document.RootElement.GetProperty("ready").GetBoolean());
    }

    [Fact]
    public void Compile_MarkdownFormat_EmitsHumanReadableOutput()
    {
        using var workspace = new InterviewSessionWorkspace();
        workspace.SeedSession("intent-cli", "alpha", new[]
        {
            (Id: "q1", Prompt: "Goal?", Answer: "deterministic CLI"),
            (Id: "q2", Prompt: "In Scope?", Answer: (string?)null)
        });

        using var writer = new StringWriter();
        var exitCode = InterviewCompileCommand.Execute(
            workspace.Context,
            ["--session", "alpha"],
            writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("# Interview compile — intent-cli / alpha", output, StringComparison.Ordinal);
        Assert.Contains("## Accepted baseline", output, StringComparison.Ordinal);
        Assert.Contains("## Open questions", output, StringComparison.Ordinal);
        Assert.Contains("## Candidate execution units", output, StringComparison.Ordinal);
        Assert.Contains("ready for draft: yes", output, StringComparison.Ordinal);
    }

    [Fact]
    public void DraftFromInterview_DryRun_EmitsDraftWithoutWriting()
    {
        using var workspace = new InterviewSessionWorkspace();
        workspace.SeedSession("intent-cli", "alpha", new[]
        {
            (Id: "q1", Prompt: "Goal?", Answer: "deterministic CLI"),
            (Id: "q2", Prompt: "Verification?", Answer: (string?)null)
        });

        using var writer = new StringWriter();
        var exitCode = IntentDraftFromInterviewCommand.Execute(
            workspace.Context,
            ["--session", "alpha", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.Equal("dry-run", root.GetProperty("mode").GetString());
        Assert.Equal(1, root.GetProperty("accepted_count").GetInt32());
        Assert.Equal(1, root.GetProperty("open_count").GetInt32());
        Assert.Contains("# Draft intent — intent-cli / session alpha", root.GetProperty("draft_markdown").GetString()!, StringComparison.Ordinal);
        Assert.Contains("## Accepted baseline", root.GetProperty("draft_markdown").GetString()!, StringComparison.Ordinal);

        var draftPath = root.GetProperty("draft_path").GetString()!;
        Assert.False(File.Exists(draftPath));
    }

    [Fact]
    public void DraftFromInterview_Write_CreatesDraftFile()
    {
        using var workspace = new InterviewSessionWorkspace();
        workspace.SeedSession("intent-cli", "alpha", new (string Id, string Prompt, string? Answer)[]
        {
            (Id: "q1", Prompt: "Goal?", Answer: "deterministic CLI")
        });

        using var writer = new StringWriter();
        var exitCode = IntentDraftFromInterviewCommand.Execute(
            workspace.Context,
            ["--session", "alpha", "--write", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.Equal("write", root.GetProperty("mode").GetString());
        var draftPath = root.GetProperty("draft_path").GetString()!;
        Assert.True(File.Exists(draftPath));
        var draft = File.ReadAllText(draftPath);
        Assert.Contains("# Draft intent — intent-cli / session alpha", draft, StringComparison.Ordinal);
        Assert.Contains("deterministic CLI", draft, StringComparison.Ordinal);
    }

    [Fact]
    public void DraftFromInterview_GivenMissingSession_ReturnsError()
    {
        using var workspace = new InterviewSessionWorkspace();
        using var writer = new StringWriter();

        var exitCode = IntentDraftFromInterviewCommand.Execute(
            workspace.Context,
            ["--session", "missing", "--format", "json"],
            writer);

        Assert.Equal(1, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        Assert.Contains("interview session not found", document.RootElement.GetProperty("error").GetString()!, StringComparison.Ordinal);
    }

    [Fact]
    public void DraftFromInterview_GivenNoAcceptedAnswers_ReturnsError()
    {
        using var workspace = new InterviewSessionWorkspace();
        workspace.SeedSession("intent-cli", "alpha", new[]
        {
            (Id: "q1", Prompt: "Goal?", Answer: (string?)null)
        });

        using var writer = new StringWriter();
        var exitCode = IntentDraftFromInterviewCommand.Execute(
            workspace.Context,
            ["--session", "alpha", "--format", "json"],
            writer);

        Assert.Equal(1, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        Assert.Contains("no accepted answers", document.RootElement.GetProperty("error").GetString()!, StringComparison.Ordinal);
    }

    [Fact]
    public void Compile_MissingSession_ReturnsUsageError()
    {
        using var workspace = new InterviewSessionWorkspace();
        using var writer = new StringWriter();

        var exitCode = InterviewCompileCommand.Execute(
            workspace.Context,
            [],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("--session is required", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void DraftFromInterview_MissingSession_ReturnsUsageError()
    {
        using var workspace = new InterviewSessionWorkspace();
        using var writer = new StringWriter();

        var exitCode = IntentDraftFromInterviewCommand.Execute(
            workspace.Context,
            [],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("--session is required", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void DraftFromInterview_HelpFlag_PrintsUsage()
    {
        using var workspace = new InterviewSessionWorkspace();
        using var writer = new StringWriter();

        var exitCode = IntentDraftFromInterviewCommand.Execute(
            workspace.Context,
            ["--help"],
            writer);

        Assert.Equal(0, exitCode);
        Assert.Contains("intent draft-from-interview", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Compile_HelpFlag_PrintsUsage()
    {
        using var workspace = new InterviewSessionWorkspace();
        using var writer = new StringWriter();

        var exitCode = InterviewCompileCommand.Execute(
            workspace.Context,
            ["--help"],
            writer);

        Assert.Equal(0, exitCode);
        Assert.Contains("interview compile", writer.ToString(), StringComparison.Ordinal);
    }

    private sealed class InterviewSessionWorkspace : IDisposable
    {
        private readonly string rootPath = Directory
            .CreateTempSubdirectory("interview-compile-tests-")
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

        public void Dispose()
        {
            if (Directory.Exists(rootPath))
            {
                Directory.Delete(rootPath, recursive: true);
            }
        }
    }
}
