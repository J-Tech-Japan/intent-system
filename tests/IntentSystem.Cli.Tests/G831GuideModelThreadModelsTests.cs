using System.Text.Json;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G831: <c>guide model</c> names both supported thread shapes — the
/// four-thread Architect/Orchestrator/Builder/Reviewer model (legacy aliases
/// design/orchestration/implementation/review) and the five-thread model with
/// an optional Steward relay seat — over the recorded session layer, bound to
/// the role normalizer so the guide cannot drift from accepted roles.
/// </summary>
public sealed class G831GuideModelThreadModelsTests
{
    [Fact]
    public void Json_ThreadModelsNameBothShapesWithExactRolesAndAliases()
    {
        var model = RenderJson().GetProperty("execution_orchestration_model");
        var threadModels = model.GetProperty("thread_models").EnumerateArray().ToArray();

        // G833 adds the third shape, solo-conductor, with the four judgment roles.
        Assert.Equal(["four-thread", "five-thread", "solo-conductor"], threadModels.Select(item => item.GetProperty("name").GetString()));
        Assert.Equal(["architect", "orchestrator", "builder", "reviewer"], Roles(threadModels[0]));
        Assert.Equal(["architect", "orchestrator", "builder", "reviewer", "steward"], Roles(threadModels[1]));
        Assert.Equal(["architect", "orchestrator", "builder", "reviewer"], Roles(threadModels[2]));

        var expectedAliases = new Dictionary<string, string>
        {
            ["design"] = "architect",
            ["orchestration"] = "orchestrator",
            ["implementation"] = "builder",
            ["review"] = "reviewer",
        };
        foreach (var threadModel in threadModels)
        {
            Assert.Equal(expectedAliases.OrderBy(pair => pair.Key), Aliases(threadModel).OrderBy(pair => pair.Key));
        }

        // Existing keys stay.
        foreach (var key in new[] { "summary", "roles", "message_driven_steady_state", "alternative" })
        {
            Assert.True(model.TryGetProperty(key, out _), key);
        }
    }

    [Fact]
    public void ThreadModels_AreBoundToTheRoleNormalizer()
    {
        var fiveThread = GuideModelCommand.BuildThreadModels().Single(item => item.Name == "five-thread");
        Assert.Equal(LogicalRoleNormalizer.CanonicalRoles, fiveThread.Roles);
        Assert.Equal(
            LogicalRoleNormalizer.Aliases.OrderBy(pair => pair.Key, StringComparer.Ordinal),
            fiveThread.Aliases.OrderBy(pair => pair.Key, StringComparer.Ordinal));

        foreach (var threadModel in GuideModelCommand.BuildThreadModels())
        {
            foreach (var role in threadModel.Roles.Concat(threadModel.Aliases.Keys))
            {
                Assert.True(LogicalRoleNormalizer.TryNormalize(role, out _, out var error), error);
            }
        }
    }

    [Fact]
    public void Markdown_SummaryNamesBothModels_OverTheSessionLayer_WithoutAgmsgBinding()
    {
        using var writer = new StringWriter();
        Assert.Equal(0, GuideModelCommand.Execute(CreateContext(), [], writer));
        var output = writer.ToString();
        var summary = RenderJson().GetProperty("execution_orchestration_model").GetProperty("summary").GetString()!;

        foreach (var phrase in new[] { "PRIMARY", "four-thread", "five-thread", "Architect", "Orchestrator", "Builder", "Reviewer", "Steward", "recorded session layer", "herdr-only preferred", "agmsg + herdr deprecated" })
        {
            Assert.Contains(phrase, summary, StringComparison.Ordinal);
        }

        foreach (var alias in new[] { "design", "orchestration", "implementation", "review" })
        {
            Assert.Contains(alias, summary, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("over agmsg", output, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("agmsg replies", output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("- **four-thread** — roles: `architect` / `orchestrator` / `builder` / `reviewer`", output, StringComparison.Ordinal);
        Assert.Contains("- **five-thread** — roles: `architect` / `orchestrator` / `builder` / `reviewer` / `steward`", output, StringComparison.Ordinal);
        Assert.Contains("not a judgment seat", output, StringComparison.Ordinal);
        Assert.Contains(SessionLayerMode.AgmsgDeprecationNotice, output, StringComparison.Ordinal);
    }

    [Fact]
    public void RoleEntries_ReferenceOnlyRolesTheListDefines()
    {
        var roles = RenderJson().GetProperty("execution_orchestration_model").GetProperty("roles")
            .EnumerateArray().Select(item => item.GetString()!).ToArray();
        var defined = roles.Select(entry => entry.Split(' ')[0]).ToArray();

        Assert.Equal(LogicalRoleNormalizer.CanonicalRoles, defined);
        foreach (var entry in roles)
        {
            foreach (var mentioned in new[] { "the architect", "the reviewer", "the orchestrator", "the builder" })
            {
                if (entry.Contains(mentioned, StringComparison.Ordinal))
                {
                    Assert.Contains(mentioned["the ".Length..], defined);
                }
            }
        }
    }

    private static string[] Roles(JsonElement threadModel) =>
        threadModel.GetProperty("roles").EnumerateArray().Select(item => item.GetString()!).ToArray();

    private static Dictionary<string, string> Aliases(JsonElement threadModel) =>
        threadModel.GetProperty("aliases").EnumerateObject().ToDictionary(item => item.Name, item => item.Value.GetString()!);

    private static JsonElement RenderJson()
    {
        using var writer = new StringWriter();
        Assert.Equal(0, GuideModelCommand.Execute(CreateContext(), ["--format", "json"], writer));
        return JsonDocument.Parse(writer.ToString()).RootElement.Clone();
    }

    private static CliContext CreateContext() => new()
    {
        RepoRoot = Path.GetTempPath(),
        Config = new CliConfig
        {
            Project = new ProjectConfig { Domain = "intent-cli", ArtifactRoot = ".intent-cli", WorktreeRoot = ".intent-cli/worktrees" },
        },
    };
}
