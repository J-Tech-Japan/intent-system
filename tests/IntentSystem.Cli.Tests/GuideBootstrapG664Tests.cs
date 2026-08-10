using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

public sealed class GuideBootstrapG664Tests : IDisposable
{
    private readonly string root = Directory.CreateTempSubdirectory("g664-bootstrap-").FullName;

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }

    public static TheoryData<string, string, bool> RenderingMatrix => new()
    {
        { "agmsg", "json", false }, { "agmsg", "json", true },
        { "agmsg", "markdown", false }, { "agmsg", "markdown", true },
        { "herdr-only", "json", false }, { "herdr-only", "json", true },
        { "herdr-only", "markdown", false }, { "herdr-only", "markdown", true },
    };

    [Theory]
    [MemberData(nameof(RenderingMatrix))]
    public void Guide_RendersSixOrderedQuestionAndHandoffSteps_InEveryModeFormatAndTeamShape(
        string mode,
        string format,
        bool includeTeam)
    {
        var context = CreateContext();
        using var writer = new StringWriter();
        var args = new List<string>
        {
            "--domain", "intent-cli", "--target-repo", "example/repo",
            "--routing-root", root, "--format", format,
        };
        if (includeTeam) args.InsertRange(2, ["--team", "intent-cli-dev"]);

        Assert.Equal(0, GuideBootstrapCommand.Execute(context, args.ToArray(), writer));
        var output = writer.ToString();
        Assert.Contains(mode, output, StringComparison.Ordinal);
        if (format == "json")
        {
            using var document = JsonDocument.Parse(output);
            var guide = document.RootElement;
            Assert.Equal(GuideBootstrapCommand.TriggerEnglish, guide.GetProperty("trigger_phrases").GetProperty("english").GetString());
            Assert.Equal(GuideBootstrapCommand.TriggerJapanese, guide.GetProperty("trigger_phrases").GetProperty("japanese").GetString());
            Assert.Equal(includeTeam, guide.TryGetProperty("team", out _));
            var steps = guide.GetProperty("steps").EnumerateArray().ToArray();
            Assert.Equal(6, steps.Length);
            Assert.Equal(Enumerable.Range(1, 6), steps.Select(item => item.GetProperty("number").GetInt32()));
            Assert.Contains("Ask the human", steps[0].GetProperty("instruction").GetString()!, StringComparison.Ordinal);
            Assert.Contains("model", steps[0].GetProperty("instruction").GetString()!, StringComparison.Ordinal);
            Assert.Contains("Never infer or default", steps[4].GetProperty("instruction").GetString()!, StringComparison.Ordinal);
            Assert.Contains("application conversation", steps[4].GetProperty("instruction").GetString()!, StringComparison.Ordinal);
            Assert.Contains("notify delegate", Join(steps[5].GetProperty("emitted_commands")), StringComparison.Ordinal);
            Assert.Contains("operator's front door", guide.GetProperty("final_handoff_statement").GetString()!, StringComparison.Ordinal);
        }
        else
        {
            Assert.Contains(GuideBootstrapCommand.TriggerEnglish, output, StringComparison.Ordinal);
            Assert.Contains(GuideBootstrapCommand.TriggerJapanese, output, StringComparison.Ordinal);
            for (var number = 1; number <= 6; number++)
                Assert.Contains($"### {number}.", output, StringComparison.Ordinal);
            Assert.EndsWith("it is not a design, orchestration, implementation, review, or supervision loop seat.\n", output, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void RecordedTopology_SelectsJoinFlow_AndNamesMissingSeatsWithoutRecreation()
    {
        WriteTopology(new Dictionary<string, object>
        {
            ["orchestration"] = HerdrRole("w1:p1", "/host"),
        });

        using var writer = new StringWriter();
        Assert.Equal(0, GuideBootstrapCommand.Execute(
            CreateContext(),
            ["--domain", "intent-cli", "--team", "intent-cli-dev", "--routing-root", root, "--format", "json"],
            writer));
        using var document = JsonDocument.Parse(writer.ToString());
        var guide = document.RootElement;

        Assert.Equal("join-and-delegate", guide.GetProperty("flow").GetString());
        Assert.Equal("topology-recorded-seats-missing", guide.GetProperty("state").GetProperty("name").GetString());
        var missing = Join(guide.GetProperty("state").GetProperty("missing_facts"));
        Assert.Contains("design", missing, StringComparison.Ordinal);
        Assert.Contains("implementation", missing, StringComparison.Ordinal);
        Assert.Contains("review", missing, StringComparison.Ordinal);
        var stepTwo = guide.GetProperty("steps")[1];
        Assert.Contains("do not recreate", stepTwo.GetProperty("instruction").GetString()!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("only commands for named missing seats", stepTwo.GetProperty("instruction").GetString()!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GuideNext_RecommendsHalfDoneBootstrap_AndClearsAfterCompletedCycle()
    {
        WriteTopology(new Dictionary<string, object>
        {
            ["design"] = HerdrRole("w1:p0", root),
            ["orchestration"] = HerdrRole("w1:p1", root),
            ["implementation"] = HerdrRole("w1:p2", "/work"),
            ["review"] = HerdrRole("w1:p3", "/review"),
        });
        var context = CreateContext();

        using var halfDoneWriter = new StringWriter();
        Assert.Equal(0, GuideNextCommand.Execute(
            context,
            ["--domain", "intent-cli", "--team", "intent-cli-dev", "--target-repo", "example/repo", "--format", "json"],
            halfDoneWriter));
        using var halfDoneDocument = JsonDocument.Parse(halfDoneWriter.ToString());
        var halfDone = halfDoneDocument.RootElement;
        Assert.True(halfDone.GetProperty("bootstrap").GetProperty("resume_recommended").GetBoolean());
        Assert.Equal("bootstrap-resume", halfDone.GetProperty("decision_set")[0].GetProperty("action").GetString());
        Assert.Contains("guide bootstrap", halfDone.GetProperty("decision_set")[0].GetProperty("suggested_prompt").GetString()!, StringComparison.Ordinal);

        var now = DateTimeOffset.UtcNow;
        var cyclePath = NotifySupervisionStore.ResolveCyclePath(context.ResolveSupervisionArtifactRootPath(), "intent-cli", "intent-cli-dev");
        var write = NotifySupervisionStore.RecordCycle(cyclePath, new NotifySupervisionCycle
        {
            CycleId = "g664-bootstrap-complete",
            StartedAt = now,
            CompletedAt = now,
            IntervalSeconds = 60,
        }, write: true);
        Assert.True(write.Applied, write.Error);

        using var completeWriter = new StringWriter();
        Assert.Equal(0, GuideNextCommand.Execute(
            context,
            ["--domain", "intent-cli", "--team", "intent-cli-dev", "--target-repo", "example/repo", "--format", "json"],
            completeWriter));
        using var completeDocument = JsonDocument.Parse(completeWriter.ToString());
        var complete = completeDocument.RootElement;
        Assert.True(complete.GetProperty("bootstrap").GetProperty("complete").GetBoolean());
        Assert.False(complete.GetProperty("bootstrap").GetProperty("resume_recommended").GetBoolean());
        Assert.DoesNotContain(complete.GetProperty("decision_set").EnumerateArray(), item => item.GetProperty("action").GetString() == "bootstrap-resume");
    }

    [Fact]
    public void Guide_IsCataloguedDocumentedPreviewAndRenderOnly()
    {
        Assert.Contains(GuideCommandsListCommand.Groups, entry => entry.Name == "guide bootstrap" && entry.Mutability == "read-only");
        Assert.Contains(GuideHelpCommand.Subcommands, entry => entry.Name == "bootstrap");

        var en = ReadRepoFile("docs/en/12-agent-message-orchestration.md");
        var ja = ReadRepoFile("docs/ja/12-agent-message-orchestration.md");
        var enLedger = ReadRepoFile("docs/en/1.0-compatibility-ledger.md");
        var jaLedger = ReadRepoFile("docs/ja/1.0-compatibility-ledger.md");
        foreach (var document in new[] { en, ja })
        {
            Assert.Contains("intent-cli guide bootstrap", document, StringComparison.Ordinal);
            Assert.Contains("bootstrap-resume", document, StringComparison.Ordinal);
            Assert.Contains("front door", document, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("herdr-only で起動して", document, StringComparison.Ordinal);
            Assert.Contains("Start this work in a herdr-only team", document, StringComparison.Ordinal);
            Assert.Contains("executes nothing", document, StringComparison.OrdinalIgnoreCase);
        }
        foreach (var ledger in new[] { enLedger, jaLedger })
        {
            Assert.Contains("| `guide bootstrap` |", ledger, StringComparison.Ordinal);
            Assert.Contains("preview-through-1.x", ledger, StringComparison.Ordinal);
        }

        var source = ReadRepoFile("src/IntentSystem.Cli/Commands/GuideBootstrapCommand.cs");
        Assert.DoesNotContain("Process.Start", source, StringComparison.Ordinal);
        Assert.DoesNotContain("IProcessRunner", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ProcessRunner", source, StringComparison.Ordinal);
    }

    private void WriteTopology(IReadOnlyDictionary<string, object> roles)
    {
        var path = NotifyRoleTopologyStore.ResolvePath(root, "intent-cli", "intent-cli-dev");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(new
        {
            schema_version = "1",
            domain = "intent-cli",
            team = "intent-cli-dev",
            workspace_id = "w1",
            roles,
        }));
    }

    private static object HerdrRole(string paneId, string cwd) => new
    {
        resident = "herdr",
        workspace_id = "w1",
        pane_id = paneId,
        cwd,
        kind = "codex",
    };

    private CliContext CreateContext() => new()
    {
        RepoRoot = root,
        Config = new CliConfig
        {
            Project = new ProjectConfig { Domain = "intent-cli", ArtifactRoot = ".intent-cli", WorktreeRoot = ".intent-cli/worktrees" },
            Supervision = new SupervisionConfig { ArtifactRoot = ".intent-cli/supervision" },
        },
    };

    private static string Join(JsonElement array) => string.Join('\n', array.EnumerateArray().Select(item => item.GetString()));

    private static string ReadRepoFile(string relativePath) => File.ReadAllText(Path.Combine(RepoVersionPolicySource.RepoRoot(), relativePath));
}
