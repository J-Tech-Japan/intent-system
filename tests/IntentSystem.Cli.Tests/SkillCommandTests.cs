using System.Text;
using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G559: the cross-platform agent skill install surface.
///
/// The tests exercise the installer's actual writes rather than its wording:
/// what matters is that each platform's own location receives the embedded
/// file, that an edited copy is never replaced silently, and that a scope a
/// platform does not define is refused instead of written somewhere that
/// platform will never read.
/// </summary>
public sealed class SkillCommandTests
{
    [Fact]
    public void EmbeddedSkill_IsTheRepositoryFile_SoThereIsOneSource_G559()
    {
        var repoFile = Path.Combine(RepoRoot(), "skills", "intent-cli", "SKILL.md");
        Assert.True(File.Exists(repoFile), $"expected the single skill source at {repoFile}");

        var skill = SkillAssets.Find("intent-cli");
        Assert.NotNull(skill);

        // Reading the embedded resource is the proof that the build actually
        // packaged it: a missing asset throws here rather than shipping an
        // installer that writes nothing.
        var embedded = SkillAssets.ReadBody(skill!);
        Assert.Equal(Normalize(File.ReadAllText(repoFile)), Normalize(embedded));
    }

    [Fact]
    public void Skill_IsADispatcher_AndDefersToInstalledGuideOutput_G559()
    {
        var body = SkillAssets.ReadBody(SkillAssets.Find("intent-cli")!);

        // The whole point of the thin dispatcher is that it does not restate
        // the workflow: it must say so, and it must route to guide surfaces.
        Assert.Contains("dispatcher", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("intent-cli guide model", body, StringComparison.Ordinal);
        Assert.Contains("intent-cli guide workflow suggest", body, StringComparison.Ordinal);
        Assert.Contains("intent-cli guide worker issue-to-pr", body, StringComparison.Ordinal);
        Assert.Contains("intent-cli guide review", body, StringComparison.Ordinal);

        // "installed guide output wins" is the rule that keeps a stale copy
        // from overriding the tool the agent is actually running.
        Assert.Contains("guide output", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("intent-cli skill install", body, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("claude", null, new[] { ".claude", "skills", "intent-cli", "SKILL.md" }, false)]
    [InlineData("claude", "repo", new[] { ".claude", "skills", "intent-cli", "SKILL.md" }, false)]
    [InlineData("claude", "user", new[] { ".claude", "skills", "intent-cli", "SKILL.md" }, true)]
    [InlineData("codex", null, new[] { ".codex", "skills", "intent-cli", "SKILL.md" }, true)]
    [InlineData("codex", "user", new[] { ".codex", "skills", "intent-cli", "SKILL.md" }, true)]
    [InlineData("copilot", null, new[] { ".github", "skills", "intent-cli", "SKILL.md" }, false)]
    [InlineData("copilot", "repo", new[] { ".github", "skills", "intent-cli", "SKILL.md" }, false)]
    public void Install_WritesTheEmbeddedSkill_IntoEachPlatformsOwnLocation_G559(
        string target, string? scope, string[] relativePath, bool underUserHome)
    {
        using var workspace = new SkillWorkspace();

        var args = scope is null ? new[] { "--target", target } : ["--target", target, "--scope", scope];
        var exit = workspace.Install(args, out var output);

        Assert.Equal(0, exit);
        Assert.Contains("installed", output, StringComparison.Ordinal);

        var expected = Path.Combine(
            [underUserHome ? workspace.UserHome : workspace.RepoRoot, .. relativePath]);
        Assert.True(File.Exists(expected), $"expected the skill at {expected}. Output:\n{output}");
        Assert.Equal(
            Normalize(SkillAssets.ReadBody(SkillAssets.Find("intent-cli")!)),
            Normalize(File.ReadAllText(expected)));
    }

    [Fact]
    public void InstallTargetAll_ReachesEveryPlatform_InOnePass_G559()
    {
        using var workspace = new SkillWorkspace();

        var exit = workspace.Install(["--target", "all"], out var output);

        Assert.Equal(0, exit);
        Assert.True(File.Exists(Path.Combine(workspace.RepoRoot, ".claude", "skills", "intent-cli", "SKILL.md")), output);
        Assert.True(File.Exists(Path.Combine(workspace.UserHome, ".codex", "skills", "intent-cli", "SKILL.md")), output);
        Assert.True(File.Exists(Path.Combine(workspace.RepoRoot, ".github", "skills", "intent-cli", "SKILL.md")), output);
    }

    [Fact]
    public void ReinstallingAnUnchangedCopy_WritesNothing_G559()
    {
        using var workspace = new SkillWorkspace();
        Assert.Equal(0, workspace.Install(["--target", "copilot"], out _));

        var path = Path.Combine(workspace.RepoRoot, ".github", "skills", "intent-cli", "SKILL.md");
        var stamp = File.GetLastWriteTimeUtc(path);

        var exit = workspace.Install(["--target", "copilot"], out var output);

        Assert.Equal(0, exit);
        Assert.Contains("already-current", output, StringComparison.Ordinal);
        Assert.Equal(stamp, File.GetLastWriteTimeUtc(path));
    }

    [Fact]
    public void AnEditedCopy_IsRefused_AndLeftExactlyAsTheOperatorLeftIt_G559()
    {
        using var workspace = new SkillWorkspace();
        Assert.Equal(0, workspace.Install(["--target", "copilot"], out _));

        var path = Path.Combine(workspace.RepoRoot, ".github", "skills", "intent-cli", "SKILL.md");
        var edited = File.ReadAllText(path) + "\nLocal note the operator added.\n";
        File.WriteAllText(path, edited);

        var exit = workspace.Install(["--target", "copilot"], out var output);

        // Refusing is only useful if it is observable to a script AND the file
        // is genuinely untouched.
        Assert.Equal(1, exit);
        Assert.Contains("refused-drifted", output, StringComparison.Ordinal);
        Assert.Equal(edited, File.ReadAllText(path));
    }

    [Fact]
    public void TargetAll_WithADriftedDestinationAnywhere_WritesNothingAtAll_G559()
    {
        using var workspace = new SkillWorkspace();

        // The plan order is claude → codex → copilot, so this is the shape the
        // per-item loop got wrong: an EARLIER missing target, a LATER drifted
        // one, and another missing target after it. Inspecting and writing in
        // one pass installed claude, refused codex, installed copilot, and then
        // exited 1 — a partial install behind an exit code that claims nothing
        // happened.
        var claude = Path.Combine(workspace.RepoRoot, ".claude", "skills", "intent-cli", "SKILL.md");
        var codex = Path.Combine(workspace.UserHome, ".codex", "skills", "intent-cli", "SKILL.md");
        var copilot = Path.Combine(workspace.RepoRoot, ".github", "skills", "intent-cli", "SKILL.md");

        var edited = "an operator's own copy, deliberately different\n";
        Directory.CreateDirectory(Path.GetDirectoryName(codex)!);
        File.WriteAllText(codex, edited);

        var exit = workspace.Install(["--target", "all"], out var output);

        Assert.Equal(1, exit);
        Assert.Contains("refused-drifted", output, StringComparison.Ordinal);
        Assert.Contains("skipped-plan-aborted", output, StringComparison.Ordinal);

        // The drifted file is byte-identical, and neither missing destination
        // was created — not even its directory.
        Assert.Equal(edited, File.ReadAllText(codex));
        Assert.False(File.Exists(claude), $"claude must not have been written: {output}");
        Assert.False(File.Exists(copilot), $"copilot must not have been written: {output}");
        Assert.False(Directory.Exists(Path.GetDirectoryName(claude)!), output);
        Assert.False(Directory.Exists(Path.GetDirectoryName(copilot)!), output);
    }

    [Fact]
    public void TargetAllWithForce_InstallsEveryDestination_IncludingTheDriftedOne_G559()
    {
        using var workspace = new SkillWorkspace();

        var codex = Path.Combine(workspace.UserHome, ".codex", "skills", "intent-cli", "SKILL.md");
        Directory.CreateDirectory(Path.GetDirectoryName(codex)!);
        File.WriteAllText(codex, "an operator's own copy, deliberately different\n");

        var exit = workspace.Install(["--target", "all", "--force"], out var output);

        // --force is the opt-in that makes the same plan succeed end to end:
        // the abort must not survive as a --force regression.
        Assert.Equal(0, exit);
        Assert.Contains("overwritten", output, StringComparison.Ordinal);
        Assert.DoesNotContain("refused-drifted", output, StringComparison.Ordinal);
        Assert.DoesNotContain("skipped-plan-aborted", output, StringComparison.Ordinal);

        var embedded = Normalize(SkillAssets.ReadBody(SkillAssets.Find("intent-cli")!));
        foreach (var path in new[]
                 {
                     Path.Combine(workspace.RepoRoot, ".claude", "skills", "intent-cli", "SKILL.md"),
                     codex,
                     Path.Combine(workspace.RepoRoot, ".github", "skills", "intent-cli", "SKILL.md"),
                 })
        {
            Assert.True(File.Exists(path), $"expected {path}. Output:\n{output}");
            Assert.Equal(embedded, Normalize(File.ReadAllText(path)));
        }
    }

    [Fact]
    public void Force_ReplacesAnEditedCopy_G559()
    {
        using var workspace = new SkillWorkspace();
        Assert.Equal(0, workspace.Install(["--target", "copilot"], out _));

        var path = Path.Combine(workspace.RepoRoot, ".github", "skills", "intent-cli", "SKILL.md");
        File.WriteAllText(path, "replaced by hand\n");

        var exit = workspace.Install(["--target", "copilot", "--force"], out var output);

        Assert.Equal(0, exit);
        Assert.Contains("overwritten", output, StringComparison.Ordinal);
        Assert.Equal(
            Normalize(SkillAssets.ReadBody(SkillAssets.Find("intent-cli")!)),
            Normalize(File.ReadAllText(path)));
    }

    [Fact]
    public void LineEndingDifferencesAreNotDrift_G559()
    {
        using var workspace = new SkillWorkspace();
        Assert.Equal(0, workspace.Install(["--target", "copilot"], out _));

        var path = Path.Combine(workspace.RepoRoot, ".github", "skills", "intent-cli", "SKILL.md");
        File.WriteAllText(path, File.ReadAllText(path).Replace("\n", "\r\n", StringComparison.Ordinal));

        var exit = workspace.Install(["--target", "copilot"], out var output);

        // A Windows checkout must not report every install as edited.
        Assert.Equal(0, exit);
        Assert.Contains("already-current", output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("codex", "repo")]
    [InlineData("copilot", "user")]
    public void AScopeThePlatformDoesNotDefine_IsRefused_WithoutWritingAnything_G559(string target, string scope)
    {
        using var workspace = new SkillWorkspace();

        var exit = workspace.Install(["--target", target, "--scope", scope], out var output);

        Assert.Equal(1, exit);
        Assert.Contains($"does not support scope '{scope}'", output, StringComparison.Ordinal);
        Assert.Empty(FindSkillFiles(workspace.RepoRoot));
        Assert.Empty(FindSkillFiles(workspace.UserHome));
    }

    [Fact]
    public void AnUnsupportedScopeUnderTargetAll_FailsTheWholeRun_RatherThanPartiallyInstalling_G559()
    {
        using var workspace = new SkillWorkspace();

        // `--scope repo` is valid for claude and copilot but not for codex.
        // Skipping codex silently would report success for an install the
        // operator never got, so the plan is validated before any write.
        var exit = workspace.Install(["--target", "all", "--scope", "repo"], out var output);

        Assert.Equal(1, exit);
        Assert.Contains("codex", output, StringComparison.Ordinal);
        Assert.Empty(FindSkillFiles(workspace.RepoRoot));
        Assert.Empty(FindSkillFiles(workspace.UserHome));
    }

    [Fact]
    public void UnknownTarget_IsRejected_G559()
    {
        using var workspace = new SkillWorkspace();

        var exit = workspace.Install(["--target", "vscode"], out var output);

        Assert.Equal(1, exit);
        Assert.Contains("unknown target 'vscode'", output, StringComparison.Ordinal);
    }

    [Fact]
    public void InstallWithoutATarget_IsRejected_G559()
    {
        using var workspace = new SkillWorkspace();

        var exit = workspace.Install([], out var output);

        Assert.Equal(1, exit);
        Assert.Contains("--target is required", output, StringComparison.Ordinal);
    }

    [Fact]
    public void List_ReportsEveryScopeAPlatformDefines_AndItsInstalledState_G559()
    {
        using var workspace = new SkillWorkspace();
        Assert.Equal(0, workspace.Install(["--target", "copilot"], out _));

        var exit = workspace.Run(SkillCommand.ExecuteList, ["--format", "json"], out var output);
        Assert.Equal(0, exit);

        using var document = JsonDocument.Parse(output);
        var installations = document.RootElement
            .GetProperty("skills")[0]
            .GetProperty("installations")
            .EnumerateArray()
            .Select(element => (
                Target: element.GetProperty("target").GetString(),
                Scope: element.GetProperty("scope").GetString(),
                State: element.GetProperty("state").GetString()))
            .ToList();

        // Claude reads both a repo-scoped and a user-scoped directory; listing
        // only one would hide an install the agent is actually loading.
        Assert.Contains(("claude", "repo", "not-installed"), installations);
        Assert.Contains(("claude", "user", "not-installed"), installations);
        Assert.Contains(("codex", "user", "not-installed"), installations);
        Assert.Contains(("copilot", "repo", "current"), installations);
    }

    [Fact]
    public void Diff_ShowsWhatDiffers_WhenACopyHasDrifted_G559()
    {
        using var workspace = new SkillWorkspace();
        Assert.Equal(0, workspace.Install(["--target", "copilot"], out _));

        var path = Path.Combine(workspace.RepoRoot, ".github", "skills", "intent-cli", "SKILL.md");
        File.WriteAllText(path, File.ReadAllText(path) + "\nOperator addition worth seeing.\n");

        var exit = workspace.Run(SkillCommand.ExecuteDiff, ["--target", "copilot"], out var output);

        Assert.Equal(0, exit);
        Assert.Contains("locally-modified", output, StringComparison.Ordinal);
        Assert.Contains("locally modified copy → current embedded version", output, StringComparison.Ordinal);
        Assert.Contains("Operator addition worth seeing.", output, StringComparison.Ordinal);
    }

    private static IReadOnlyList<string> FindSkillFiles(string root) =>
        Directory.Exists(root)
            ? Directory.GetFiles(root, "SKILL.md", SearchOption.AllDirectories)
            : [];

    private static string Normalize(string content) =>
        content.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd('\n');

    private static string RepoRoot() => RepoVersionPolicySource.RepoRoot();

    /// <summary>
    /// A throwaway repo root plus a throwaway user home. User-scoped installs
    /// write under a real home directory in production, so the tests redirect
    /// that seam rather than touching the developer's own skills.
    /// </summary>
    private sealed class SkillWorkspace : IDisposable
    {
        private readonly string _root;
        private readonly Func<string>? _previousUserHome;

        public SkillWorkspace()
        {
            _root = Path.Combine(Path.GetTempPath(), "intent-cli-skill-tests", Guid.NewGuid().ToString("n"));
            RepoRoot = Path.Combine(_root, "repo");
            UserHome = Path.Combine(_root, "home");
            Directory.CreateDirectory(RepoRoot);
            Directory.CreateDirectory(UserHome);

            _previousUserHome = SkillTargets.UserHomeFactory;
            SkillTargets.UserHomeFactory = () => UserHome;
        }

        public string RepoRoot { get; }

        public string UserHome { get; }

        public int Install(string[] args, out string output) =>
            Run(SkillCommand.ExecuteInstall, args, out output);

        public int Run(Func<CliContext, string[], TextWriter, int> command, string[] args, out string output)
        {
            var builder = new StringBuilder();
            using var writer = new StringWriter(builder);
            var exit = command(BuildContext(), args, writer);
            output = builder.ToString();
            return exit;
        }

        public void Dispose()
        {
            SkillTargets.UserHomeFactory = _previousUserHome;

            try
            {
                Directory.Delete(_root, recursive: true);
            }
            catch (IOException)
            {
                // A leftover temp directory is not worth failing a test over.
            }
        }

        private CliContext BuildContext() => new()
        {
            RepoRoot = RepoRoot,
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
}
