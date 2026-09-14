using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G826: issue publish-flow used to look only for a packet.yaml line starting
/// with "title:", which the scaffold never writes, so every canonically
/// scaffolded packet published as "&lt;id&gt; (untitled)". These tests start
/// from the real scaffold output rather than a hand-written packet.yaml,
/// because the earlier G290 fixture used a shape the scaffold never produces.
/// </summary>
public sealed class G826PublishFlowIssueTitleTests : IDisposable
{
    private const string Unit = "G900";
    private const string AuthoredTitle = "G900: an authored title with a colon — and an em dash";

    private readonly string root;
    private readonly CliContext context;

    public G826PublishFlowIssueTitleTests()
    {
        root = Directory.CreateTempSubdirectory("g826-host-").FullName;
        Directory.CreateDirectory(Path.Combine(root, "intents", "intent-cli", "automation"));
        File.WriteAllText(Path.Combine(root, "intents", "intent-cli", "automation", "bindings.md"), "---\nexecution_unit_regex: '.*'\n---\n");
        context = new CliContext
        {
            RepoRoot = root,
            Config = new CliConfig
            {
                Project = new ProjectConfig { Domain = "intent-cli", ArtifactRoot = ".intent-cli", WorktreeRoot = ".intent-cli/worktrees" }
            }
        };
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ScaffoldedYamlPacket_PublishesWithItsAuthoredTitle()
    {
        ScaffoldPacket();
        SetScaffoldedIssueTitle(AuthoredTitle);
        WriteBodyWithoutH1();

        var result = PublishFlowDryRun();

        Assert.Equal(AuthoredTitle, result.GetProperty("title").GetString());
        Assert.Equal(IssuePublishFlowCommand.TitleSourcePacketYaml, result.GetProperty("title_source").GetString());
        Assert.DoesNotContain(result.GetProperty("warnings").EnumerateArray(), w => w.GetString() == "title-fallback");
    }

    [Fact]
    public void JsonFormPacket_PublishesWithItsIssueTitle()
    {
        ScaffoldPacket();
        File.WriteAllText(PacketYamlPath, JsonSerializer.Serialize(new
        {
            implementation_issue_packet = new
            {
                issue_title = AuthoredTitle,
                source_execution_unit = Unit,
                domain = "intent-cli",
                target_repo = "J-Tech-Japan/intent-system",
            }
        }, new JsonSerializerOptions { WriteIndented = true }));
        WriteBodyWithoutH1();

        var result = PublishFlowDryRun();

        Assert.Equal(AuthoredTitle, result.GetProperty("title").GetString());
        Assert.Equal(IssuePublishFlowCommand.TitleSourcePacketYaml, result.GetProperty("title_source").GetString());
    }

    [Fact]
    public void QueueSeedAndPublishFlow_DeriveTheSameTitle_FromTheScaffoldedPacket()
    {
        ScaffoldPacket();
        SetScaffoldedIssueTitle(AuthoredTitle);
        WriteBodyWithoutH1();

        Assert.True(PacketYamlDocument.TryParse(File.ReadAllText(PacketYamlPath), out var document, out var error), error);
        var seeded = AutomationQueueSeedFromPacketCommand.BuildSeedItem(
            Unit, document!, "intents/intent-cli/clarifications/open.md", "builder", "reviewer", "high");

        Assert.Equal(seeded.Title, PublishFlowDryRun().GetProperty("title").GetString());
        Assert.Equal(AuthoredTitle, seeded.Title);
    }

    [Fact]
    public void NoTitleKeyAndNoH1_StillFallsBackWithWarning()
    {
        ScaffoldPacket();
        File.WriteAllText(PacketYamlPath, "implementation_issue_packet:\n  source_execution_unit: G900\n");
        WriteBodyWithoutH1();

        var result = PublishFlowDryRun();

        Assert.Equal($"{Unit} (untitled)", result.GetProperty("title").GetString());
        Assert.Equal(IssuePublishFlowCommand.TitleSourceFallbackUntitled, result.GetProperty("title_source").GetString());
        Assert.Contains(result.GetProperty("warnings").EnumerateArray(), w => w.GetString() == "title-fallback");
    }

    private string PacketDirectory => Path.Combine(root, ".intent-cli", "issues", Unit);

    private string PacketYamlPath => Path.Combine(PacketDirectory, "packet.yaml");

    private void ScaffoldPacket()
    {
        Assert.Equal(0, PacketDraftCommand.Execute(
            context, ["--execution-unit", Unit, "--target-repo", "J-Tech-Japan/intent-system"], TextWriter.Null));
        Assert.True(File.Exists(PacketYamlPath), "packet draft did not scaffold packet.yaml");
    }

    /// <summary>Replace only the value of the scaffold's own issue_title line.</summary>
    private void SetScaffoldedIssueTitle(string title)
    {
        var yaml = File.ReadAllText(PacketYamlPath);
        var pattern = new Regex(@"^(\s*issue_title:\s*).*$", RegexOptions.Multiline);
        Assert.Matches(pattern, yaml);
        File.WriteAllText(PacketYamlPath, pattern.Replace(yaml, m => m.Groups[1].Value + "\"" + title + "\"", 1));
    }

    private void WriteBodyWithoutH1()
    {
        var body = new StringBuilder();
        foreach (var heading in IssueValidateBodyValidator.RequiredHeadings)
        {
            body.Append("## ").Append(heading).Append("\n\n");
            body.Append(heading switch
            {
                "Target Repo / Path / Part" => "Repository: `J-Tech-Japan/intent-system`\n\n- Target paths: `src/IntentSystem.Cli/Commands/Example.cs`\n",
                "Related Links" => "- https://github.com/J-Tech-Japan/intent-system/issues/1792\n",
                _ => $"Content for {heading}.\n",
            });
            body.Append('\n');
        }
        File.WriteAllText(Path.Combine(PacketDirectory, "github-body.md"), body.ToString());
    }

    private JsonElement PublishFlowDryRun()
    {
        using var writer = new StringWriter();
        IssuePublishFlowCommand.Execute(context, [Unit, "--repo", "J-Tech-Japan/intent-system", "--format", "json"], writer);
        using var document = JsonDocument.Parse(writer.ToString());
        return document.RootElement.Clone();
    }
}
