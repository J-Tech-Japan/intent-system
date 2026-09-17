using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

[Collection(AutomationPrTransitionSharedStateCollection.Name)]
public sealed class G841CrossRuntimeReviewTests : IDisposable
{
    private const string Domain = "intent-cli";
    private const string Team = "intent-cli-dev";
    private const string Repo = "J-Tech-Japan/intent-system";
    private const string Unit = "G841";

    private readonly string root = Directory.CreateTempSubdirectory("g841-host-").FullName;

    public G841CrossRuntimeReviewTests()
    {
        CrossRuntimeReviewTeamResolver.ClaimReader = (_, scope) =>
        {
            var unit = scope["execution-unit:".Length..];
            return new ClaimOwnershipVerification(
                false,
                ClaimOwnershipVerification.StatusTeamRequired,
                scope,
                true,
                null,
                "design",
                Team,
                "held");
        };
        Directory.CreateDirectory(Path.Combine(root, ".intent-cli"));
        File.WriteAllText(Path.Combine(root, ".intent-cli", "config.toml"), "default_domain = \"intent-cli\"\nartifact_root = \".intent-cli\"\n");
        WriteClaim(Unit);
        WriteValidPacket();
    }

    public void Dispose()
    {
        CrossRuntimeReviewTeamResolver.ClaimReader = null;
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Record_Design_UnparseablePacket_RefusesPacketInvalidWithMissing()
    {
        WritePacketYaml("implementation_issue_packet:\n  domain: intent-cli\n  broken: [\n");
        var (exit, output) = Route(["review", "cross-runtime", "status", "--kind", "design", "--execution-unit", Unit, "--format", "json"]);
        Assert.Equal(1, exit);
        using var json = JsonDocument.Parse(output);
        Assert.Equal(CrossRuntimeReviewCauses.PacketInvalid, json.RootElement.GetProperty("cause").GetString());
        Assert.Equal("packet-invalid", json.RootElement.GetProperty("resolution").GetProperty("missing").GetString());
        Assert.Contains("at line", json.RootElement.GetProperty("detail").GetString(), StringComparison.Ordinal);
        Assert.DoesNotContain("is not valid YAML", json.RootElement.GetProperty("detail").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Record_Design_QuotedHashPacket_DoesNotRefusePacketInvalid()
    {
        WritePacketYaml(
            """
            implementation_issue_packet:
              issue_title: "G841 title"
              domain: intent-cli
              target_repo: J-Tech-Japan/intent-system
              source_artifact: "review of PR #1823"
            """);
        var (exit, output) = Route(["review", "cross-runtime", "status", "--kind", "design", "--execution-unit", Unit, "--format", "json"]);
        Assert.Equal(0, exit);
        using var json = JsonDocument.Parse(output);
        if (json.RootElement.TryGetProperty("cause", out var cause))
        {
            Assert.NotEqual(CrossRuntimeReviewCauses.PacketInvalid, cause.GetString());
        }
    }

    [Fact]
    public void Record_Design_UnreadablePacket_RefusesPacketUnreadable()
    {
        var packetPath = PacketPath();
        var injected = false;
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(packetPath, UnixFileMode.None);
        }

        if (OperatingSystem.IsWindows() || CanRead(packetPath))
        {
            PacketFileReader.ReadAllText = _ => throw new UnauthorizedAccessException("denied");
            injected = true;
        }

        try
        {
            var (exit, output) = Route(["review", "cross-runtime", "status", "--kind", "design", "--execution-unit", Unit, "--format", "json"]);
            Assert.Equal(1, exit);
            using var json = JsonDocument.Parse(output);
            Assert.Equal(CrossRuntimeReviewCauses.PacketUnreadable, json.RootElement.GetProperty("cause").GetString());
            Assert.Equal("packet-unreadable", json.RootElement.GetProperty("resolution").GetProperty("missing").GetString());
        }
        finally
        {
            if (injected)
            {
                PacketFileReader.ReadAllText = File.ReadAllText;
            }

            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(packetPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
        }
    }

    private static bool CanRead(string path)
    {
        try
        {
            using var _ = File.OpenRead(path);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private (int ExitCode, string Output) Route(string[] args)
    {
        using var writer = new StringWriter();
        var exit = CommandRouter.Execute(args, Context(), writer);
        return (exit, writer.ToString());
    }

    private CliContext Context() => new()
    {
        RepoRoot = root,
        Config = new CliConfig
        {
            Project = new ProjectConfig { Domain = Domain, ArtifactRoot = ".intent-cli", WorktreeRoot = ".intent-cli/worktrees" },
            CrossRuntimeReview = new CrossRuntimeReviewConfig
            {
                Teams =
                [
                    new CrossRuntimeReviewTeamDeclaration
                    {
                        Team = $"{Domain}/{Team}",
                        ConductorRuntime = "codex",
                        Repos = [Repo],
                    },
                ],
            },
        },
    };

    private string PacketDir() => Path.Combine(root, ".intent-cli", "issues", Unit);

    private string PacketPath() => Path.Combine(PacketDir(), "packet.yaml");

    private void WriteValidPacket()
    {
        Directory.CreateDirectory(PacketDir());
        WritePacketYaml(
            """
            implementation_issue_packet:
              issue_title: "G841 title"
              domain: intent-cli
              target_repo: J-Tech-Japan/intent-system
            """);
        File.WriteAllText(Path.Combine(PacketDir(), "github-body.md"), "# G841 title\n");
        File.WriteAllText(Path.Combine(PacketDir(), "review-context.md"), "# review\n");
        File.WriteAllText(Path.Combine(PacketDir(), "implementation.md"), "# notes\n");
    }

    private void WritePacketYaml(string yaml) => File.WriteAllText(PacketPath(), yaml);

    private void WriteClaim(string unit = Unit)
    {
        var claimPath = Path.Combine(root, ClaimCommand.ClaimPath($"execution-unit:{unit}").Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(claimPath)!);
        File.WriteAllText(claimPath, JsonSerializer.Serialize(new
        {
            schema_version = "1",
            scope = $"execution-unit:{unit}",
            actor = "design",
            team = Team,
            claimed_at = DateTimeOffset.UtcNow,
            base_commit = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
        }));
    }
}
