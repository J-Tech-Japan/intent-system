using System.Text.Json;
using IntentSystem.Cli.Commands;

namespace IntentSystem.Cli.Tests;

[Collection(AutomationPrTransitionSharedStateCollection.Name)]
public sealed class G842MutationGapTests
{
    private static readonly string[] EnvironmentNames =
    [
        "HOME",
        "COPILOT_HOME",
        "XDG_CONFIG_HOME",
        "XDG_DATA_HOME",
        "XDG_STATE_HOME",
        "XDG_CACHE_HOME",
        "OPENCODE_CONFIG_DIR",
        "GH_CONFIG_DIR",
    ];

    [Fact]
    public void ProtectedRootCheck_CoversEveryPlannedPath_G842()
    {
        var root = Directory.CreateTempSubdirectory("g842-mutation-s-").FullName;
        var saved = SaveEnvironment();
        try
        {
            ConfigureEnvironment(root, Path.Combine(root, "out", CrossRuntimeReviewFiles.CopilotHome));
            var outDir = Path.Combine(root, "out");
            var workspace = Path.Combine(root, "workspace");
            var protectedPlannedPath = Path.Combine(outDir, CrossRuntimeReviewFiles.CopilotHome);

            Assert.Equal(protectedPlannedPath, Environment.GetEnvironmentVariable("COPILOT_HOME"));
            Assert.Contains(protectedPlannedPath, CrossRuntimeReviewHomeAccessGuard.EnumerateProtectedHomes(), StringComparer.Ordinal);
            Assert.True(CrossRuntimeReviewHomeAccessGuard.IsProtectedOperatorPath(protectedPlannedPath));

            var refused = CrossRuntimeReviewHomeAccessGuard.TryRefuseRequestPaths(
                outDir,
                CrossRuntimeReviewRuntimes.Copilot,
                CrossRuntimeReviewRecord.KindImplementation,
                hasClone: true,
                workspace,
                providerConfigPath: null,
                out var detail);

            Assert.True(refused);
            Assert.Contains("protected home", detail, StringComparison.Ordinal);
            Assert.Contains(CrossRuntimeReviewFiles.CopilotHome, detail, StringComparison.Ordinal);
        }
        finally
        {
            RestoreEnvironment(saved);
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void PlannedPaths_MustStayOutOfReviewWorkspace_G842()
    {
        var root = Directory.CreateTempSubdirectory("g842-mutation-x-").FullName;
        var saved = SaveEnvironment();
        try
        {
            ConfigureEnvironment(root, copilotHome: null);
            var outDir = Path.Combine(root, "out");

            var refused = CrossRuntimeReviewHomeAccessGuard.TryRefuseRequestPaths(
                outDir,
                CrossRuntimeReviewRuntimes.Copilot,
                CrossRuntimeReviewRecord.KindDesign,
                hasClone: false,
                workspace: outDir,
                providerConfigPath: null,
                out var detail);

            Assert.True(refused);
            Assert.Contains("review workspace", detail, StringComparison.Ordinal);
            Assert.Contains("overlaps", detail, StringComparison.Ordinal);
        }
        finally
        {
            RestoreEnvironment(saved);
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void CopilotEffort_RejectsABadSecondMainCheckpoint_G842()
    {
        var content = string.Join(
            '\n',
            Checkpoint("low"),
            Checkpoint("high"),
            string.Empty);

        var accepted = CrossRuntimeReviewJsonlVerdict.TryParseCopilotEffort(
            content,
            "gpt-5.6-sol",
            "low",
            out var error);

        Assert.False(accepted);
        Assert.Equal("observed reasoning_effort does not match --effort 'low'.", error);
    }

    private static string Checkpoint(string effort) =>
        JsonSerializer.Serialize(new
        {
            type = "session.usage_checkpoint",
            data = new
            {
                promptCacheBreakState = new[]
                {
                    new
                    {
                        conversation = "main",
                        models = new Dictionary<string, object>
                        {
                            ["gpt-5.6-sol"] = new { reasoning_effort = effort },
                        },
                    },
                },
            },
        });

    private static Dictionary<string, string?> SaveEnvironment() =>
        EnvironmentNames.ToDictionary(name => name, Environment.GetEnvironmentVariable, StringComparer.Ordinal);

    private static void ConfigureEnvironment(string root, string? copilotHome)
    {
        Environment.SetEnvironmentVariable("HOME", Path.Combine(root, "home"));
        Environment.SetEnvironmentVariable("COPILOT_HOME", copilotHome);
        Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", Path.Combine(root, "xdg-config"));
        Environment.SetEnvironmentVariable("XDG_DATA_HOME", Path.Combine(root, "xdg-data"));
        Environment.SetEnvironmentVariable("XDG_STATE_HOME", Path.Combine(root, "xdg-state"));
        Environment.SetEnvironmentVariable("XDG_CACHE_HOME", Path.Combine(root, "xdg-cache"));
        Environment.SetEnvironmentVariable("OPENCODE_CONFIG_DIR", Path.Combine(root, "opencode-config-dir"));
        Environment.SetEnvironmentVariable("GH_CONFIG_DIR", Path.Combine(root, "gh"));
    }

    private static void RestoreEnvironment(IReadOnlyDictionary<string, string?> saved)
    {
        foreach (var (name, value) in saved)
        {
            Environment.SetEnvironmentVariable(name, value);
        }
    }
}
