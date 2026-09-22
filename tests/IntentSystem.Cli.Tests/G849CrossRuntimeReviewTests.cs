using System.Text.RegularExpressions;
using IntentSystem.Cli.Commands;

namespace IntentSystem.Cli.Tests;

/// <summary>G849: copilot keeps gh's operator config root while XDG is isolated.</summary>
[Collection(AutomationPrTransitionSharedStateCollection.Name)]
public sealed class G849CrossRuntimeReviewTests : IDisposable
{
    private static readonly string[] EnvironmentNames = ["HOME", "GH_CONFIG_DIR", "XDG_CONFIG_HOME", "AppData"];
    private readonly string root = Directory.CreateTempSubdirectory("g849-review-").FullName;
    private readonly Dictionary<string, string?> savedEnvironment;

    public G849CrossRuntimeReviewTests()
    {
        savedEnvironment = EnvironmentNames.ToDictionary(
            name => name,
            Environment.GetEnvironmentVariable,
            StringComparer.Ordinal);
        CrossRuntimeReviewHomeAccessGuard.IsWindowsOverride = null;
    }

    public void Dispose()
    {
        foreach (var (name, value) in savedEnvironment)
        {
            Environment.SetEnvironmentVariable(name, value);
        }

        CrossRuntimeReviewHomeAccessGuard.IsWindowsOverride = null;
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void CopilotInvocation_UsesGhDocumentedResolutionOrder()
    {
        var home = Path.Combine(root, "home");
        var configured = Path.Combine(root, "configured-gh");
        var xdg = Path.Combine(root, "xdg-config");
        var appData = Path.Combine(root, "app-data");
        var outDir = Path.Combine(root, "out");

        SetEnvironment("HOME", home);
        SetEnvironment("GH_CONFIG_DIR", configured);
        SetEnvironment("XDG_CONFIG_HOME", xdg);
        SetEnvironment("AppData", appData);
        CrossRuntimeReviewHomeAccessGuard.IsWindowsOverride = () => true;
        AssertRenderedGhRoot(configured, outDir);

        SetEnvironment("GH_CONFIG_DIR", null);
        AssertRenderedGhRoot(Path.Combine(xdg, "gh"), outDir);

        SetEnvironment("XDG_CONFIG_HOME", null);
        AssertRenderedGhRoot(Path.Combine(appData, "GitHub CLI"), outDir);

        SetEnvironment("AppData", null);
        AssertRenderedGhRoot(Path.Combine(home, ".config", "gh"), outDir);
    }

    [Fact]
    public void CopilotInvocation_UsesHomeGhRootForHostsYmlWithoutKeychain()
    {
        var home = Path.Combine(root, "scratch-home");
        var ghRoot = Path.Combine(home, ".config", "gh");
        Directory.CreateDirectory(ghRoot);
        File.WriteAllText(
            Path.Combine(ghRoot, "hosts.yml"),
            "github.com:\n    oauth_token: G849-FAKE-TOKEN-NOT-REAL\n    user: probe\n    git_protocol: https\n");

        SetEnvironment("HOME", home);
        SetEnvironment("GH_CONFIG_DIR", null);
        SetEnvironment("XDG_CONFIG_HOME", null);
        SetEnvironment("AppData", null);
        CrossRuntimeReviewHomeAccessGuard.IsWindowsOverride = () => false;

        var outDir = Path.Combine(root, "out");
        var invocation = CrossRuntimeReviewRuntimes.RenderInvocation(
            CrossRuntimeReviewRuntimes.Copilot,
            Path.Combine(root, "clone"),
            outDir,
            "gpt-5.6-sol");

        Assert.Contains(
            $"GH_CONFIG_DIR={CrossRuntimeReviewPaths.ShellQuote(ghRoot)} ",
            invocation,
            StringComparison.Ordinal);
        Assert.Equal(Path.GetFullPath(ghRoot), CrossRuntimeReviewHomeAccessGuard.ResolveGhConfigDir());
        Assert.Contains(ghRoot, CrossRuntimeReviewHomeAccessGuard.EnumerateProtectedHomes(), StringComparer.Ordinal);
        CrossRuntimeReviewInvocationTestHelpers.AssertCopilotEnvironmentAssignments(invocation, outDir, ghRoot);
    }

    [Fact]
    public void CopilotInvocationHelper_RejectsGhConfigDirOutsideFixedSlotOrOutDir()
    {
        var home = Path.Combine(root, "home");
        var outDir = Path.Combine(root, "out");
        var ghRoot = Path.Combine(home, ".config", "gh");
        SetEnvironment("HOME", home);
        SetEnvironment("GH_CONFIG_DIR", ghRoot);
        SetEnvironment("XDG_CONFIG_HOME", null);
        SetEnvironment("AppData", null);
        CrossRuntimeReviewHomeAccessGuard.IsWindowsOverride = () => false;

        var command = CrossRuntimeReviewRuntimes.RenderInvocation(
            CrossRuntimeReviewRuntimes.Copilot,
            Path.Combine(root, "clone"),
            outDir,
            "gpt-5.6-sol");
        CrossRuntimeReviewInvocationTestHelpers.AssertCopilotEnvironmentAssignments(command, outDir, ghRoot);

        var quotedAssignment = $"GH_CONFIG_DIR={CrossRuntimeReviewPaths.ShellQuote(ghRoot)} ";
        var elsewhere = command.Replace(quotedAssignment, string.Empty, StringComparison.Ordinal)
            .Replace(" < ", $" GH_CONFIG_DIR={CrossRuntimeReviewPaths.ShellQuote(ghRoot)} < ", StringComparison.Ordinal);
        Assert.ThrowsAny<Exception>(() =>
            CrossRuntimeReviewInvocationTestHelpers.AssertCopilotEnvironmentAssignments(elsewhere, outDir, ghRoot));

        var repeated = command.Replace(
            " copilot -C ",
            $" GH_CONFIG_DIR={CrossRuntimeReviewPaths.ShellQuote(ghRoot)} copilot -C ",
            StringComparison.Ordinal);
        Assert.ThrowsAny<Exception>(() =>
            CrossRuntimeReviewInvocationTestHelpers.AssertCopilotEnvironmentAssignments(repeated, outDir, ghRoot));

        var inside = command.Replace(
            CrossRuntimeReviewPaths.ShellQuote(ghRoot),
            CrossRuntimeReviewPaths.ShellQuote(Path.Combine(outDir, CrossRuntimeReviewFiles.CopilotXdg)),
            StringComparison.Ordinal);
        Assert.ThrowsAny<Exception>(() =>
            CrossRuntimeReviewInvocationTestHelpers.AssertCopilotEnvironmentAssignments(inside, outDir, ghRoot));
    }

    [Fact]
    public void GuideSoloConductor_CarriesStaticGhConfigPlaceholderAndReason()
    {
        var builder = GuideSoloConductorCommand.BuildBuilder();
        var copilot = Assert.Single(builder.Invocations, invocation => invocation.Runtime == CrossRuntimeReviewRuntimes.Copilot);

        Assert.Equal(G842PinnedContractTexts.CopilotBuilderCommand, copilot.Command);
        Assert.Contains(
            "replace that placeholder with gh's config root found in gh's documented order",
            builder.Contract[0],
            StringComparison.Ordinal);
        Assert.Contains("GH_CONFIG_DIR=<operator-gh-config-root>", copilot.Command, StringComparison.Ordinal);
        Assert.DoesNotContain(root, copilot.Command, StringComparison.Ordinal);
    }

    [Fact]
    public void Docs_ExplainGhConfigRootForReviewerAndBuilder()
    {
        var repoRoot = RepoVersionPolicySource.RepoRoot();
        var english = File.ReadAllText(Path.Combine(repoRoot, "docs", "en", "12-agent-message-orchestration.md"));
        var japanese = File.ReadAllText(Path.Combine(repoRoot, "docs", "ja", "12-agent-message-orchestration.md"));

        Assert.Contains("G849", english, StringComparison.Ordinal);
        Assert.Contains("GH_CONFIG_DIR=<gh-config-root>", english, StringComparison.Ordinal);
        Assert.Contains("%AppData%/GitHub CLI", english, StringComparison.Ordinal);
        Assert.Contains("GH_CONFIG_DIR=<operator-gh-config-root>", english, StringComparison.Ordinal);
        Assert.Contains("G849", japanese, StringComparison.Ordinal);
        Assert.Contains("GH_CONFIG_DIR=<gh-config-root>", japanese, StringComparison.Ordinal);
        Assert.Contains("%AppData%/GitHub CLI", japanese, StringComparison.Ordinal);
        Assert.Contains("GH_CONFIG_DIR=<operator-gh-config-root>", japanese, StringComparison.Ordinal);

        var g613 = new Regex(@"[A-Za-z]+(?:し|します|した|して|され|せず|しない)");
        Assert.DoesNotMatch(g613, japanese);
    }

    private void AssertRenderedGhRoot(string expected, string outDir)
    {
        var invocation = CrossRuntimeReviewRuntimes.RenderInvocation(
            CrossRuntimeReviewRuntimes.Copilot,
            Path.Combine(root, "clone"),
            outDir,
            "gpt-5.6-sol");
        var absoluteExpected = Path.GetFullPath(expected);
        Assert.Contains(
            $"GH_CONFIG_DIR={CrossRuntimeReviewPaths.ShellQuote(absoluteExpected)} ",
            invocation,
            StringComparison.Ordinal);
        Assert.Equal(absoluteExpected, CrossRuntimeReviewHomeAccessGuard.ResolveGhConfigDir());
        Assert.Contains(absoluteExpected, CrossRuntimeReviewHomeAccessGuard.EnumerateProtectedHomes(), StringComparer.Ordinal);
        CrossRuntimeReviewInvocationTestHelpers.AssertCopilotEnvironmentAssignments(invocation, outDir, absoluteExpected);
    }

    private void SetEnvironment(string name, string? value) => Environment.SetEnvironmentVariable(name, value);
}
