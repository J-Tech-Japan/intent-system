using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

public sealed class GuideDesignThreadCommandTests
{
    [Fact]
    public void DesignThread_RenderedGuideNamesExternalRoleScopedReceiveWithCallerCursorAndBoundedWait()
    {
        var output = RenderDesignMarkdown();
        var section = SectionFrom(output, "## External-resident design receive");

        Assert.Contains(
            "intent-cli notify collect --domain <domain> --team <team> --role design --since <cursor> --wait --timeout-ms <timeout-ms> --format json",
            section,
            StringComparison.Ordinal);
        Assert.Contains("caller", section, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("opaque cursor", section, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("returned in each result", section, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("bounded", section, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("required", section, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("--task-id", section, StringComparison.Ordinal);
        Assert.DoesNotContain("claude", section, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("codex", section, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("copilot", section, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Bootstrap_RenderedExternalBranchPointsToDesignThreadReceiveGuidance()
    {
        var root = Directory.CreateTempSubdirectory("g762-bootstrap-").FullName;
        try
        {
            using var writer = new StringWriter();
            Assert.Equal(
                0,
                GuideBootstrapCommand.Execute(
                    CreateContext(root),
                    [
                        "--domain", "intent-cli", "--team", "intent-cli-dev", "--target-repo", "owner/repo",
                        "--routing-root", root, "--format", "json",
                    ],
                    writer));

            using var document = JsonDocument.Parse(writer.ToString());
            var externalBranch = document.RootElement.GetProperty("steps")[4];
            var instruction = externalBranch.GetProperty("instruction").GetString()!;
            Assert.Contains("human-selected external branch", instruction, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("intent-cli guide design-thread", instruction, StringComparison.Ordinal);
            Assert.Contains("notify collect", instruction, StringComparison.Ordinal);
            Assert.Contains("role-scoped", instruction, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("bounded", instruction, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void DesignThread_RenderedDeploymentRuleRemainsByteIdentical()
    {
        using var document = JsonDocument.Parse(RenderDesignJson("/routing-root"));
        Assert.Equal(
            "A design seat whose agent kind has no inbound app monitor must be a recorded resident herdr seat with cwd `/routing-root`, where persistent AGENTS rules apply. A kind with an inbound app monitor may use that external reader. This is a deployment rule, not a recommendation.",
            document.RootElement.GetProperty("monitoring").GetProperty("deployment_rule").GetString());
    }

    [Fact]
    public void OrchestratorThread_RenderedOutputRemainsByteIdentical()
    {
        var root = Directory.CreateTempSubdirectory("g762-orchestrator-").FullName;
        try
        {
            Directory.CreateDirectory(Path.Combine(root, ".intent-cli"));
            var context = CreateContext(root);
            using var writer = new StringWriter();
            Assert.Equal(
                0,
                CommandRouter.Execute(
                    [
                        "guide", "orchestrator-thread", "--domain", "intent-cli",
                        "--target-repo", "J-Tech-Japan/intent-system", "--agent", "codex",
                        "--team", "intent-cli-dev", "--format", "markdown",
                    ],
                    context,
                    writer));

            var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(writer.ToString())));
            Assert.Equal("cb18bd2786f4eb295f5c603f9d6ab7d2564724f3d5ea554ab2820bff80ffb008", hash);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static string RenderDesignMarkdown()
    {
        using var writer = new StringWriter();
        Assert.Equal(0, GuideDesignThreadCommand.Execute(CreateContext(Path.GetTempPath()), [], writer));
        return writer.ToString();
    }

    private static string RenderDesignJson(string routingRoot)
    {
        using var writer = new StringWriter();
        Assert.Equal(
            0,
            GuideDesignThreadCommand.Execute(
                CreateContext(routingRoot),
                ["--routing-root", routingRoot, "--format", "json"],
                writer));
        return writer.ToString();
    }

    private static string SectionFrom(string output, string heading)
    {
        var start = output.IndexOf(heading, StringComparison.Ordinal);
        Assert.True(start >= 0, $"missing section heading: {heading}");
        var next = output.IndexOf("\n## ", start + heading.Length, StringComparison.Ordinal);
        return next < 0 ? output[start..] : output[start..next];
    }

    private static CliContext CreateContext(string root) => new()
    {
        RepoRoot = root,
        Config = new CliConfig
        {
            Project = new ProjectConfig
            {
                Domain = "intent-cli",
                ArtifactRoot = ".intent-cli",
                WorktreeRoot = ".intent-cli/worktrees",
            },
        },
    };
}
