using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

public sealed class SafetyNestedProviderHandoffCommandTests : IDisposable
{
    private readonly Func<DateTimeOffset> originalTimestampFactory;
    private readonly Func<bool>? originalLauncher;

    public SafetyNestedProviderHandoffCommandTests()
    {
        originalTimestampFactory = SafetyNestedProviderHandoffCommand.TimestampFactory;
        originalLauncher = SafetyNestedProviderHandoffCommand.NestedProviderLauncher;
    }

    public void Dispose()
    {
        SafetyNestedProviderHandoffCommand.TimestampFactory = originalTimestampFactory;
        SafetyNestedProviderHandoffCommand.NestedProviderLauncher = originalLauncher;
    }

    [Fact]
    public void Execute_GivenAllRequiredFlags_WritesArtifactAndStatesNoNestedLaunch()
    {
        using var workspace = new SafetyHandoffWorkspace();
        SafetyNestedProviderHandoffCommand.TimestampFactory =
            () => new DateTimeOffset(2026, 4, 29, 12, 34, 56, 789, TimeSpan.Zero);

        var outPath = workspace.GetPath("artifact.json");

        using var writer = new StringWriter();
        var exitCode = SafetyNestedProviderHandoffCommand.Execute(
            workspace.Context,
            BuildSuccessArgs(outPath),
            writer);

        Assert.Equal(0, exitCode);
        Assert.True(File.Exists(outPath));

        var doc = JsonDocument.Parse(File.ReadAllText(outPath));
        var root = doc.RootElement;
        Assert.Equal("G187", root.GetProperty("execution_unit").GetString());
        Assert.Equal("worker", root.GetProperty("intended_role").GetString());
        Assert.Equal("nested-provider-handoff", root.GetProperty("intended_action").GetString());
        Assert.Equal("J-Tech-Japan/intent-system", root.GetProperty("target_repo").GetString());
        Assert.Equal(".intent-cli/issues/G187/", root.GetProperty("target_path").GetString());
        Assert.Equal("intent-cli safety nested-provider-handoff --execution-unit G187", root.GetProperty("requested_command").GetString());
        Assert.Equal("2026-04-29T12:34:56.789Z", root.GetProperty("generated_at_utc").GetString());
        Assert.False(root.GetProperty("nested_provider_launched").GetBoolean());
        Assert.False(string.IsNullOrEmpty(root.GetProperty("artifact_path").GetString()));
        Assert.Contains(
            SafetyNestedProviderHandoffArtifact.NotStartedMessagePrefix,
            root.GetProperty("summary_line").GetString(),
            StringComparison.Ordinal);

        Assert.Contains(
            $"{SafetyNestedProviderHandoffArtifact.NotStartedMessagePrefix}. Artifact written to",
            writer.ToString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenAllRequiredFlags_ArtifactJsonRoundTripsStably()
    {
        using var workspace = new SafetyHandoffWorkspace();
        SafetyNestedProviderHandoffCommand.TimestampFactory =
            () => new DateTimeOffset(2026, 4, 29, 12, 0, 0, TimeSpan.Zero);

        var outPath = workspace.GetPath("artifact-roundtrip.json");
        using var writer = new StringWriter();
        var exitCode = SafetyNestedProviderHandoffCommand.Execute(
            workspace.Context,
            BuildSuccessArgs(outPath),
            writer);

        Assert.Equal(0, exitCode);

        var roundTripped = JsonSerializer.Deserialize<SafetyNestedProviderHandoffArtifact>(File.ReadAllText(outPath));
        Assert.NotNull(roundTripped);
        Assert.Equal("G187", roundTripped!.ExecutionUnit);
        Assert.Equal("worker", roundTripped.IntendedRole);
        Assert.Equal("nested-provider-handoff", roundTripped.IntendedAction);
        Assert.Equal("J-Tech-Japan/intent-system", roundTripped.TargetRepo);
        Assert.Equal(".intent-cli/issues/G187/", roundTripped.TargetPath);
        Assert.Equal(".intent-cli/issues/G187/packet.yaml", roundTripped.PacketPath);
        Assert.Equal(".intent-cli/issues/G187/review-context.md", roundTripped.ReviewContextPath);
        Assert.Equal(
            "intent-cli safety nested-provider-handoff --execution-unit G187",
            roundTripped.RequestedCommand);
        Assert.False(roundTripped.NestedProviderLaunched);
        Assert.Equal(Path.GetFullPath(outPath), roundTripped.ArtifactPath);
        Assert.Contains(
            SafetyNestedProviderHandoffArtifact.NotStartedMessagePrefix,
            roundTripped.SummaryLine,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenJsonFormat_StdoutMatchesArtifact()
    {
        using var workspace = new SafetyHandoffWorkspace();
        SafetyNestedProviderHandoffCommand.TimestampFactory =
            () => new DateTimeOffset(2026, 4, 29, 1, 2, 3, TimeSpan.Zero);

        var outPath = workspace.GetPath("artifact-json.json");
        var args = BuildSuccessArgs(outPath).Concat(new[] { "--format", "json" }).ToArray();

        using var writer = new StringWriter();
        var exitCode = SafetyNestedProviderHandoffCommand.Execute(workspace.Context, args, writer);

        Assert.Equal(0, exitCode);

        var stdoutArtifact = JsonSerializer.Deserialize<SafetyNestedProviderHandoffArtifact>(writer.ToString());
        var fileArtifact = JsonSerializer.Deserialize<SafetyNestedProviderHandoffArtifact>(File.ReadAllText(outPath));
        Assert.NotNull(stdoutArtifact);
        Assert.NotNull(fileArtifact);
        Assert.Equal(fileArtifact, stdoutArtifact);
    }

    [Fact]
    public void Execute_GivenMissingRequiredFlag_ReturnsNonZero_NoArtifactWritten()
    {
        using var workspace = new SafetyHandoffWorkspace();
        var outPath = workspace.GetPath("artifact-missing.json");

        // Drop --execution-unit by not including it.
        var args = new[]
        {
            "--intended-role", "worker",
            "--intended-action", "nested-provider-handoff",
            "--target-repo", "J-Tech-Japan/intent-system",
            "--target-path", ".intent-cli/issues/G187/",
            "--requested-command", "intent-cli safety nested-provider-handoff",
            "--out", outPath
        };

        using var writer = new StringWriter();
        var exitCode = SafetyNestedProviderHandoffCommand.Execute(workspace.Context, args, writer);

        Assert.Equal(1, exitCode);
        Assert.False(File.Exists(outPath));
        Assert.Contains("--execution-unit", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenBlankRequiredFlagValue_ReturnsNonZero_NoArtifactWritten()
    {
        using var workspace = new SafetyHandoffWorkspace();
        var outPath = workspace.GetPath("artifact-blank.json");

        var args = new[]
        {
            "--execution-unit", "",
            "--intended-role", "worker",
            "--intended-action", "nested-provider-handoff",
            "--target-repo", "J-Tech-Japan/intent-system",
            "--target-path", ".intent-cli/issues/G187/",
            "--requested-command", "intent-cli safety nested-provider-handoff",
            "--out", outPath
        };

        using var writer = new StringWriter();
        var exitCode = SafetyNestedProviderHandoffCommand.Execute(workspace.Context, args, writer);

        Assert.Equal(1, exitCode);
        Assert.False(File.Exists(outPath));
        Assert.Contains("--execution-unit", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenUnknownArgument_ReturnsNonZero()
    {
        using var workspace = new SafetyHandoffWorkspace();
        var outPath = workspace.GetPath("artifact-unknown.json");

        var args = BuildSuccessArgs(outPath).Concat(new[] { "--bogus-flag", "value" }).ToArray();

        using var writer = new StringWriter();
        var exitCode = SafetyNestedProviderHandoffCommand.Execute(workspace.Context, args, writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("--bogus-flag", writer.ToString(), StringComparison.Ordinal);
        Assert.Contains("Unknown argument", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_NeverInvokesProcessStart_NoNestedProviderLaunched()
    {
        using var workspace = new SafetyHandoffWorkspace();
        SafetyNestedProviderHandoffCommand.TimestampFactory =
            () => new DateTimeOffset(2026, 4, 29, 0, 0, 0, TimeSpan.Zero);

        var calledFlag = false;
        SafetyNestedProviderHandoffCommand.NestedProviderLauncher = () =>
        {
            calledFlag = true;
            return true;
        };

        var outPath = workspace.GetPath("artifact-sentinel.json");
        using var writer = new StringWriter();
        var exitCode = SafetyNestedProviderHandoffCommand.Execute(
            workspace.Context,
            BuildSuccessArgs(outPath),
            writer);

        Assert.Equal(0, exitCode);
        Assert.False(calledFlag);

        var artifact = JsonSerializer.Deserialize<SafetyNestedProviderHandoffArtifact>(File.ReadAllText(outPath));
        Assert.NotNull(artifact);
        Assert.False(artifact!.NestedProviderLaunched);
    }

    [Fact]
    public void Execute_OptionalPathsOmitted_AreEmittedAsNullInArtifact()
    {
        using var workspace = new SafetyHandoffWorkspace();
        SafetyNestedProviderHandoffCommand.TimestampFactory =
            () => new DateTimeOffset(2026, 4, 29, 0, 0, 0, TimeSpan.Zero);

        var outPath = workspace.GetPath("artifact-no-optionals.json");
        var args = new[]
        {
            "--execution-unit", "G187",
            "--intended-role", "worker",
            "--intended-action", "nested-provider-handoff",
            "--target-repo", "J-Tech-Japan/intent-system",
            "--target-path", ".intent-cli/issues/G187/",
            "--requested-command", "intent-cli safety nested-provider-handoff --execution-unit G187",
            "--out", outPath
        };

        using var writer = new StringWriter();
        var exitCode = SafetyNestedProviderHandoffCommand.Execute(workspace.Context, args, writer);

        Assert.Equal(0, exitCode);

        using var doc = JsonDocument.Parse(File.ReadAllText(outPath));
        var root = doc.RootElement;
        Assert.Equal(JsonValueKind.Null, root.GetProperty("packet_path").ValueKind);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("review_context_path").ValueKind);

        var artifact = JsonSerializer.Deserialize<SafetyNestedProviderHandoffArtifact>(File.ReadAllText(outPath));
        Assert.NotNull(artifact);
        Assert.Null(artifact!.PacketPath);
        Assert.Null(artifact.ReviewContextPath);
    }

    [Fact]
    public void RunImplementAndRunFixSources_DoNotLaunchAnyNestedProviderBinary_ArtifactOnlyInvariant()
    {
        // Source-scan invariance test: the Run implement/fix command paths must
        // not have introduced a Process.Start call against a hardcoded provider
        // binary while this slice is artifact-only. If the resolver cannot
        // locate the source files we skip — the per-command sentinel test
        // covers the new surface deterministically.
        if (!TryResolveCommandsSourceDirectory(out var commandsDir))
        {
            return;
        }

        var runImplement = Path.Combine(commandsDir, "RunImplementCommand.cs");
        var runFix = Path.Combine(commandsDir, "RunFixCommand.cs");
        if (!File.Exists(runImplement) || !File.Exists(runFix))
        {
            return;
        }

        foreach (var path in new[] { runImplement, runFix })
        {
            var text = File.ReadAllText(path);
            Assert.DoesNotContain("Process.Start(", text, StringComparison.Ordinal);
            Assert.DoesNotContain("\"codex\"", text, StringComparison.Ordinal);
            Assert.DoesNotContain("\"claude\"", text, StringComparison.Ordinal);
        }
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

    private static string[] BuildSuccessArgs(string outPath)
    {
        return new[]
        {
            "--execution-unit", "G187",
            "--intended-role", "worker",
            "--intended-action", "nested-provider-handoff",
            "--target-repo", "J-Tech-Japan/intent-system",
            "--target-path", ".intent-cli/issues/G187/",
            "--packet-path", ".intent-cli/issues/G187/packet.yaml",
            "--review-context-path", ".intent-cli/issues/G187/review-context.md",
            "--requested-command", "intent-cli safety nested-provider-handoff --execution-unit G187",
            "--out", outPath
        };
    }

    private sealed class SafetyHandoffWorkspace : IDisposable
    {
        private readonly string rootPath = Directory
            .CreateTempSubdirectory("safety-nested-provider-handoff-tests-")
            .FullName;

        public SafetyHandoffWorkspace()
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

        public void Dispose()
        {
            if (Directory.Exists(rootPath))
            {
                Directory.Delete(rootPath, recursive: true);
            }
        }
    }
}
