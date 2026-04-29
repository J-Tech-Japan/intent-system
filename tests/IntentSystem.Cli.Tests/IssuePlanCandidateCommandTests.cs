using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G189 — coverage for <c>intent-cli issue plan-candidate</c>. Class name
/// matches the issue's recommended verification filter
/// <c>~Issue|~Backlog|~Planner|~Publish</c>.
/// </summary>
public sealed class IssuePlanCandidateCommandTests : IDisposable
{
    private readonly Func<DateTimeOffset> originalTimestampFactory;

    public IssuePlanCandidateCommandTests()
    {
        originalTimestampFactory = IssuePlanCandidateCommand.TimestampFactory;
    }

    public void Dispose()
    {
        IssuePlanCandidateCommand.TimestampFactory = originalTimestampFactory;
    }

    [Fact]
    public void Execute_GivenAllRequiredFlags_WritesUnpublishedCandidateArtifact()
    {
        using var workspace = new PlanCandidateWorkspace();
        IssuePlanCandidateCommand.TimestampFactory =
            () => new DateTimeOffset(2026, 4, 29, 12, 34, 56, 789, TimeSpan.Zero);

        var outPath = workspace.GetPath("artifact.json");

        using var writer = new StringWriter();
        var exitCode = IssuePlanCandidateCommand.Execute(
            workspace.Context,
            new[]
            {
                "--execution-unit", "G189",
                "--title", "candidate planner slice",
                "--out", outPath
            },
            writer);

        Assert.Equal(0, exitCode);
        Assert.True(File.Exists(outPath));

        using var doc = JsonDocument.Parse(File.ReadAllText(outPath));
        var root = doc.RootElement;
        Assert.Equal("G189", root.GetProperty("execution_unit").GetString());
        Assert.Equal("candidate planner slice", root.GetProperty("title").GetString());
        Assert.False(root.GetProperty("is_published").GetBoolean());
        Assert.False(root.GetProperty("is_automation_visible").GetBoolean());
        Assert.Equal(
            IssuePlanCandidateConstants.UnpublishedCandidateStatus,
            root.GetProperty("candidate_spec_status").GetString());
        Assert.Equal("unpublished_candidate", root.GetProperty("candidate_spec_status").GetString());
        Assert.Equal("2026-04-29T12:34:56.789Z", root.GetProperty("generated_at_utc").GetString());
        Assert.Equal(0, root.GetProperty("context_paths").GetArrayLength());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("packet_path").ValueKind);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("review_context_path").ValueKind);

        var summary = root.GetProperty("summary_line").GetString();
        Assert.NotNull(summary);
        Assert.Contains("UNPUBLISHED", summary, StringComparison.Ordinal);
        Assert.Contains("not automation-visible", summary, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenAllRequiredFlags_ArtifactJsonRoundTripsStably()
    {
        using var workspace = new PlanCandidateWorkspace();
        IssuePlanCandidateCommand.TimestampFactory =
            () => new DateTimeOffset(2026, 4, 29, 1, 2, 3, TimeSpan.Zero);

        var outPath = workspace.GetPath("artifact-roundtrip.json");

        using var writer = new StringWriter();
        var exitCode = IssuePlanCandidateCommand.Execute(
            workspace.Context,
            new[]
            {
                "--execution-unit", "G189",
                "--title", "roundtrip-stable",
                "--out", outPath
            },
            writer);

        Assert.Equal(0, exitCode);

        var roundTripped = JsonSerializer.Deserialize<IssuePlanCandidateArtifact>(File.ReadAllText(outPath));
        Assert.NotNull(roundTripped);
        Assert.Equal("G189", roundTripped!.ExecutionUnit);
        Assert.Equal("roundtrip-stable", roundTripped.Title);
        Assert.False(roundTripped.IsPublished);
        Assert.False(roundTripped.IsAutomationVisible);
        Assert.Equal(IssuePlanCandidateConstants.UnpublishedCandidateStatus, roundTripped.CandidateSpecStatus);
        Assert.Empty(roundTripped.ContextPaths);
        Assert.Null(roundTripped.PacketPath);
        Assert.Null(roundTripped.ReviewContextPath);
        Assert.Equal("2026-04-29T01:02:03.000Z", roundTripped.GeneratedAtUtc);
        Assert.Equal(Path.GetFullPath(outPath), roundTripped.ArtifactPath);
        Assert.Contains("UNPUBLISHED", roundTripped.SummaryLine, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenContextPaths_RecordsExistsSizeAndSha256()
    {
        using var workspace = new PlanCandidateWorkspace();

        var firstRel = "intents/intent-cli/notes/first.md";
        var secondRel = "intents/intent-cli/notes/second.md";
        var firstBytes = Encoding.UTF8.GetBytes("first candidate context\n");
        var secondBytes = Encoding.UTF8.GetBytes("second candidate context body — longer.\n");
        workspace.WriteFile(firstRel, firstBytes);
        workspace.WriteFile(secondRel, secondBytes);

        var outPath = workspace.GetPath("artifact-context.json");

        using var writer = new StringWriter();
        var exitCode = IssuePlanCandidateCommand.Execute(
            workspace.Context,
            new[]
            {
                "--execution-unit", "G189",
                "--title", "context refs",
                "--context-path", firstRel,
                "--context-path", secondRel,
                "--out", outPath
            },
            writer);

        Assert.Equal(0, exitCode);

        var artifact = JsonSerializer.Deserialize<IssuePlanCandidateArtifact>(File.ReadAllText(outPath));
        Assert.NotNull(artifact);
        Assert.Equal(2, artifact!.ContextPaths.Count);

        var first = artifact.ContextPaths[0];
        Assert.Equal(firstRel, first.Path);
        Assert.True(first.Exists);
        Assert.Equal(firstBytes.LongLength, first.SizeBytes);
        Assert.Equal(ExpectedSha256Hex(firstBytes), first.Sha256);

        var second = artifact.ContextPaths[1];
        Assert.Equal(secondRel, second.Path);
        Assert.True(second.Exists);
        Assert.Equal(secondBytes.LongLength, second.SizeBytes);
        Assert.Equal(ExpectedSha256Hex(secondBytes), second.Sha256);
    }

    [Fact]
    public void Execute_GivenMissingContextPath_RecordsExistsFalseAndNullFields()
    {
        using var workspace = new PlanCandidateWorkspace();
        var outPath = workspace.GetPath("artifact-missing-context.json");

        using var writer = new StringWriter();
        var exitCode = IssuePlanCandidateCommand.Execute(
            workspace.Context,
            new[]
            {
                "--execution-unit", "G189",
                "--title", "missing context refs not an error",
                "--context-path", "intents/intent-cli/does-not-exist.md",
                "--out", outPath
            },
            writer);

        Assert.Equal(0, exitCode);

        var artifact = JsonSerializer.Deserialize<IssuePlanCandidateArtifact>(File.ReadAllText(outPath));
        Assert.NotNull(artifact);
        Assert.Single(artifact!.ContextPaths);
        var entry = artifact.ContextPaths[0];
        Assert.Equal("intents/intent-cli/does-not-exist.md", entry.Path);
        Assert.False(entry.Exists);
        Assert.Null(entry.SizeBytes);
        Assert.Null(entry.Sha256);
    }

    [Fact]
    public void Execute_NeverWritesToQueueStateOrRunsLog()
    {
        using var workspace = new PlanCandidateWorkspace();
        var queueStatePath = workspace.Context.GetQueueStatePath();
        var runsLogPath = workspace.Context.GetRunLogPath();

        var queueBytes = Encoding.UTF8.GetBytes("{\"schema_version\":\"1\",\"items\":[]}\n");
        var runsBytes = Encoding.UTF8.GetBytes("{\"event\":\"sentinel\",\"ts\":\"2026-04-29T00:00:00Z\"}\n");
        Directory.CreateDirectory(Path.GetDirectoryName(queueStatePath)!);
        File.WriteAllBytes(queueStatePath, queueBytes);
        File.WriteAllBytes(runsLogPath, runsBytes);

        var queueBefore = File.ReadAllBytes(queueStatePath);
        var runsBefore = File.ReadAllBytes(runsLogPath);

        var outPath = workspace.GetPath("artifact-no-mutation.json");
        using var writer = new StringWriter();
        var exitCode = IssuePlanCandidateCommand.Execute(
            workspace.Context,
            new[]
            {
                "--execution-unit", "G189",
                "--title", "queue/runs invariance",
                "--out", outPath
            },
            writer);

        Assert.Equal(0, exitCode);
        Assert.True(File.Exists(outPath));

        var queueAfter = File.ReadAllBytes(queueStatePath);
        var runsAfter = File.ReadAllBytes(runsLogPath);
        Assert.Equal(queueBefore, queueAfter);
        Assert.Equal(runsBefore, runsAfter);
    }

    [Fact]
    public void Execute_NeverInvokesGitHubNetwork_NoGhProcessSpawned()
    {
        // No injection seam was added for this command — it is purely a
        // local artifact-writer with no Process.Start, no `gh ` shell-out,
        // and no IGhIssueCreator usage. We assert the invariant by source
        // scanning the new command file. The queue-state/runs invariance
        // test (above) is the runtime evidence; this is the contract guard
        // against future drift.
        if (!TryResolveCommandsSourceDirectory(out var commandsDir))
        {
            return;
        }

        var commandFile = Path.Combine(commandsDir, "IssuePlanCandidateCommand.cs");
        var analyzerFile = Path.Combine(commandsDir, "IssuePlanCandidateAnalyzer.cs");
        if (!File.Exists(commandFile) || !File.Exists(analyzerFile))
        {
            return;
        }

        foreach (var path in new[] { commandFile, analyzerFile })
        {
            var text = File.ReadAllText(path);
            Assert.DoesNotContain("Process.Start(", text, StringComparison.Ordinal);
            Assert.DoesNotContain("\"gh\"", text, StringComparison.Ordinal);
            Assert.DoesNotContain("IGhIssueCreator", text, StringComparison.Ordinal);
            Assert.DoesNotContain("HttpClient", text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Execute_GivenMissingFlag_ReturnsNonZero_NoArtifactWritten()
    {
        using var workspace = new PlanCandidateWorkspace();
        var outPath = workspace.GetPath("artifact-missing-flag.json");

        // Drop --execution-unit.
        var args = new[]
        {
            "--title", "missing required flag",
            "--out", outPath
        };

        using var writer = new StringWriter();
        var exitCode = IssuePlanCandidateCommand.Execute(workspace.Context, args, writer);

        Assert.Equal(1, exitCode);
        Assert.False(File.Exists(outPath));
        Assert.Contains("--execution-unit", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenUnknownArgument_ReturnsNonZero()
    {
        using var workspace = new PlanCandidateWorkspace();
        var outPath = workspace.GetPath("artifact-unknown.json");

        var args = new[]
        {
            "--execution-unit", "G189",
            "--title", "unknown arg case",
            "--out", outPath,
            "--bogus", "value"
        };

        using var writer = new StringWriter();
        var exitCode = IssuePlanCandidateCommand.Execute(workspace.Context, args, writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("--bogus", writer.ToString(), StringComparison.Ordinal);
        Assert.Contains("Unknown argument", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_AlwaysMarksIsPublishedFalseAndAutomationVisibleFalse()
    {
        using var workspace = new PlanCandidateWorkspace();

        var ctxRel = "intents/intent-cli/notes/all-optionals.md";
        var packetRel = "intents/intent-cli/notes/packet.yaml";
        var reviewRel = "intents/intent-cli/notes/review-context.md";
        workspace.WriteFile(ctxRel, Encoding.UTF8.GetBytes("ctx body\n"));
        workspace.WriteFile(packetRel, Encoding.UTF8.GetBytes("packet body\n"));
        workspace.WriteFile(reviewRel, Encoding.UTF8.GetBytes("review body\n"));

        var outPath = workspace.GetPath("artifact-contract-locked.json");

        using var writer = new StringWriter();
        var exitCode = IssuePlanCandidateCommand.Execute(
            workspace.Context,
            new[]
            {
                "--execution-unit", "G189",
                "--title", "contract-locked",
                "--context-path", ctxRel,
                "--packet-path", packetRel,
                "--review-context-path", reviewRel,
                "--out", outPath
            },
            writer);

        Assert.Equal(0, exitCode);

        var artifact = JsonSerializer.Deserialize<IssuePlanCandidateArtifact>(File.ReadAllText(outPath));
        Assert.NotNull(artifact);
        Assert.False(artifact!.IsPublished);
        Assert.False(artifact.IsAutomationVisible);
        Assert.Equal(IssuePlanCandidateConstants.UnpublishedCandidateStatus, artifact.CandidateSpecStatus);
        Assert.NotNull(artifact.PacketPath);
        Assert.True(artifact.PacketPath!.Exists);
        Assert.NotNull(artifact.ReviewContextPath);
        Assert.True(artifact.ReviewContextPath!.Exists);
        Assert.Single(artifact.ContextPaths);
        Assert.True(artifact.ContextPaths[0].Exists);
    }

    [Fact]
    public void Execute_GivenJsonFormat_StdoutContainsArtifactJson()
    {
        using var workspace = new PlanCandidateWorkspace();
        IssuePlanCandidateCommand.TimestampFactory =
            () => new DateTimeOffset(2026, 4, 29, 5, 0, 0, TimeSpan.Zero);

        var outPath = workspace.GetPath("artifact-json-format.json");
        using var writer = new StringWriter();
        var exitCode = IssuePlanCandidateCommand.Execute(
            workspace.Context,
            new[]
            {
                "--execution-unit", "G189",
                "--title", "json-format",
                "--out", outPath,
                "--format", "json"
            },
            writer);

        Assert.Equal(0, exitCode);
        var stdoutArtifact = JsonSerializer.Deserialize<IssuePlanCandidateArtifact>(writer.ToString());
        var fileArtifact = JsonSerializer.Deserialize<IssuePlanCandidateArtifact>(File.ReadAllText(outPath));
        Assert.NotNull(stdoutArtifact);
        Assert.NotNull(fileArtifact);

        // Records compare context-paths list by reference; assert structural
        // equality via canonical JSON instead.
        var canonical = new JsonSerializerOptions { WriteIndented = false };
        var fileJson = JsonSerializer.Serialize(fileArtifact, canonical);
        var stdoutJson = JsonSerializer.Serialize(stdoutArtifact, canonical);
        Assert.Equal(fileJson, stdoutJson);
    }

    private static string ExpectedSha256Hex(byte[] bytes)
    {
        var hash = SHA256.HashData(bytes);
        var builder = new StringBuilder(hash.Length * 2);
        foreach (var b in hash)
        {
            builder.Append(b.ToString("x2", System.Globalization.CultureInfo.InvariantCulture));
        }

        return builder.ToString();
    }

    private static bool TryResolveCommandsSourceDirectory(out string commandsDirectory)
    {
        commandsDirectory = string.Empty;
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, "src", "IntentSystem.Cli", "Commands");
            if (Directory.Exists(candidate))
            {
                commandsDirectory = candidate;
                return true;
            }

            current = current.Parent;
        }

        return false;
    }

    private sealed class PlanCandidateWorkspace : IDisposable
    {
        private readonly string rootPath = Directory
            .CreateTempSubdirectory("issue-plan-candidate-tests-")
            .FullName;

        public PlanCandidateWorkspace()
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

        public void WriteFile(string relativePath, byte[] bytes)
        {
            var fullPath = Path.Combine(rootPath, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            File.WriteAllBytes(fullPath, bytes);
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
