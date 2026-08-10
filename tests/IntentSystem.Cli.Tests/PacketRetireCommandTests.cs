using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

public sealed class PacketRetireCommandTests
{
    [Fact]
    public void Execute_AbsorbedWrite_WritesSidecarAndRunEvent()
    {
        using var workspace = new PacketRetireWorkspace();
        workspace.CreatePacket("G344");

        using var writer = new StringWriter();
        var exitCode = PacketRetireCommand.Execute(
            workspace.Context,
            ["--execution-unit", "G344", "--absorbed-by", "G343", "--reason", "fully absorbed into G343", "--write", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.Equal("write", root.GetProperty("mode").GetString());
        Assert.Equal("retired", root.GetProperty("status").GetString());
        Assert.Equal("absorbed", root.GetProperty("lifecycle").GetString());
        Assert.Equal("G343", root.GetProperty("absorbed_by").GetString());

        var sidecarPath = Path.Combine(workspace.PacketDirectory("G344"), "lifecycle.yaml");
        Assert.True(File.Exists(sidecarPath));
        var sidecar = File.ReadAllText(sidecarPath);
        Assert.Contains("lifecycle: absorbed", sidecar, StringComparison.Ordinal);
        Assert.Contains("absorbed_by: G343", sidecar, StringComparison.Ordinal);

        // The packet content file is preserved (design history not deleted).
        Assert.True(File.Exists(Path.Combine(workspace.PacketDirectory("G344"), "github-body.md")));

        var runLog = File.ReadAllText(workspace.Context.GetRunLogPath());
        Assert.Contains("packet-retired", runLog, StringComparison.Ordinal);
        Assert.Single(CountOccurrences(runLog, "packet-retired"));
    }

    [Fact]
    public void Execute_Idempotent_DoesNotDuplicateRunEvents()
    {
        using var workspace = new PacketRetireWorkspace();
        workspace.CreatePacket("G344");

        string[] args =
            ["--execution-unit", "G344", "--absorbed-by", "G343", "--reason", "fully absorbed into G343", "--write", "--format", "json"];

        using (var first = new StringWriter())
        {
            Assert.Equal(0, PacketRetireCommand.Execute(workspace.Context, args, first));
        }

        using var second = new StringWriter();
        Assert.Equal(0, PacketRetireCommand.Execute(workspace.Context, args, second));
        using var document = JsonDocument.Parse(second.ToString());
        Assert.Equal("already-retired", document.RootElement.GetProperty("status").GetString());

        var runLog = File.ReadAllText(workspace.Context.GetRunLogPath());
        Assert.Single(CountOccurrences(runLog, "packet-retired"));
    }

    [Fact]
    public void Execute_DryRunByDefault_DoesNotWriteSidecar()
    {
        using var workspace = new PacketRetireWorkspace();
        workspace.CreatePacket("G344");

        using var writer = new StringWriter();
        var exitCode = PacketRetireCommand.Execute(
            workspace.Context,
            ["--execution-unit", "G344", "--retired", "--reason", "operator retired", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        Assert.Equal("dry-run", document.RootElement.GetProperty("mode").GetString());
        Assert.Equal("planned", document.RootElement.GetProperty("status").GetString());
        Assert.False(File.Exists(Path.Combine(workspace.PacketDirectory("G344"), "lifecycle.yaml")));
        Assert.False(File.Exists(workspace.Context.GetRunLogPath()));
    }

    [Fact]
    public void Execute_RequiresExactlyOneRelationship()
    {
        using var workspace = new PacketRetireWorkspace();
        workspace.CreatePacket("G344");

        using var writer = new StringWriter();
        var exitCode = PacketRetireCommand.Execute(
            workspace.Context,
            ["--execution-unit", "G344", "--reason", "missing relationship", "--write"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("Exactly one of --absorbed-by", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_RequiresReason()
    {
        using var workspace = new PacketRetireWorkspace();
        workspace.CreatePacket("G344");

        using var writer = new StringWriter();
        var exitCode = PacketRetireCommand.Execute(
            workspace.Context,
            ["--execution-unit", "G344", "--absorbed-by", "G343", "--write"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("--reason is required", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_ReactivateRequiresAndRecordsEvidence_G661()
    {
        using var workspace = new PacketRetireWorkspace();
        workspace.CreatePacket("RH-030A");
        Assert.Equal(0, PacketRetireCommand.Execute(
            workspace.Context,
            ["--execution-unit", "RH-030A", "--retired", "--reason", "empty scaffold", "--write", "--format", "json"],
            TextWriter.Null));

        using var missingEvidence = new StringWriter();
        Assert.Equal(1, PacketRetireCommand.Execute(
            workspace.Context,
            ["--execution-unit", "RH-030A", "--reactivate", "--write"],
            missingEvidence));
        Assert.Contains("--evidence", missingEvidence.ToString(), StringComparison.Ordinal);

        using var writer = new StringWriter();
        Assert.Equal(0, PacketRetireCommand.Execute(
            workspace.Context,
            ["--execution-unit", "RH-030A", "--reactivate", "--evidence", "real defect shipped in PR #98", "--write", "--format", "json"],
            writer));
        using var result = JsonDocument.Parse(writer.ToString());
        Assert.Equal("reactivated", result.RootElement.GetProperty("status").GetString());
        Assert.Equal("ready", result.RootElement.GetProperty("lifecycle").GetString());
        Assert.Equal("retired", result.RootElement.GetProperty("reactivated_from").GetString());
        Assert.Equal("real defect shipped in PR #98", result.RootElement.GetProperty("reactivation_evidence").GetString());

        var sidecar = File.ReadAllText(Path.Combine(workspace.PacketDirectory("RH-030A"), "lifecycle.yaml"));
        Assert.Contains("lifecycle: ready", sidecar, StringComparison.Ordinal);
        Assert.Contains("reactivation_evidence: \"real defect shipped in PR #98\"", sidecar, StringComparison.Ordinal);
        Assert.Contains("packet-reactivated", File.ReadAllText(workspace.Context.GetRunLogPath()), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_MissingPacketDirectory_Fails()
    {
        using var workspace = new PacketRetireWorkspace();

        using var writer = new StringWriter();
        var exitCode = PacketRetireCommand.Execute(
            workspace.Context,
            ["--execution-unit", "G999", "--retired", "--reason", "x", "--write"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("Packet directory not found", writer.ToString(), StringComparison.Ordinal);
    }

    private static IEnumerable<int> CountOccurrences(string haystack, string needle)
    {
        var index = 0;
        while ((index = haystack.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            yield return index;
            index += needle.Length;
        }
    }

    private sealed class PacketRetireWorkspace : IDisposable
    {
        private readonly string rootPath = Directory
            .CreateTempSubdirectory("packet-retire-tests-")
            .FullName;

        public PacketRetireWorkspace()
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

        public string PacketDirectory(string executionUnit)
            => Path.Combine(rootPath, ".intent-cli", "issues", executionUnit);

        public void CreatePacket(string executionUnit)
        {
            var dir = PacketDirectory(executionUnit);
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "github-body.md"), "# packet body\n");
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
