using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

public sealed class IntentSearchCommandTests
{
    [Fact]
    public void Execute_GivenMatchInPacketAndDomainTree_EmitsBothHits()
    {
        using var workspace = new IntentSearchWorkspace();
        workspace.WriteFile(
            ".intent-cli/issues/G241/github-body.md",
            """
            ## Goal
            Add intent-cli intent status command.

            ## Scope
            Read-only domain status.
            """);
        workspace.WriteFile(
            "intents/intent-cli/specs/05-intent-cli-surface.md",
            """
            # intent-cli surface
            Includes: intent status, intent search, intent explain.
            """);

        using var writer = new StringWriter();
        var exitCode = IntentSearchCommand.Execute(
            workspace.Context,
            ["--query", "intent status"],
            writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("# Intent search — intent-cli", output, StringComparison.Ordinal);
        Assert.Contains("query: `intent status`", output, StringComparison.Ordinal);
        Assert.Contains("github-body.md:2", output, StringComparison.Ordinal);
        Assert.Contains("05-intent-cli-surface.md:2", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenNoMatch_EmitsZeroMatches()
    {
        using var workspace = new IntentSearchWorkspace();
        workspace.WriteFile("intents/intent-cli/README.md", "Nothing relevant here.");

        using var writer = new StringWriter();
        var exitCode = IntentSearchCommand.Execute(
            workspace.Context,
            ["--query", "absent-keyword"],
            writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("matches: 0", output, StringComparison.Ordinal);
        Assert.Contains("No matches.", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenJsonFormat_EmitsStructuredHits()
    {
        using var workspace = new IntentSearchWorkspace();
        workspace.WriteFile(
            ".intent-cli/issues/G242/packet.yaml",
            """
            execution_unit: G242
            title: intent search and explain
            """);

        using var writer = new StringWriter();
        var exitCode = IntentSearchCommand.Execute(
            workspace.Context,
            ["--query", "G242", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.Equal("intent-cli", root.GetProperty("domain").GetString());
        Assert.Equal("G242", root.GetProperty("query").GetString());
        var hits = root.GetProperty("hits");
        Assert.True(hits.GetArrayLength() >= 1);
        var first = hits[0];
        Assert.Contains("G242", first.GetProperty("path").GetString()!, StringComparison.Ordinal);
        Assert.True(first.GetProperty("line").GetInt32() >= 1);
    }

    [Fact]
    public void Execute_GivenDomainOverride_SearchesThatDomainTreeOnly()
    {
        using var workspace = new IntentSearchWorkspace();
        workspace.WriteFile("intents/intent-cli/README.md", "shared keyword");
        workspace.WriteFile("intents/other-domain/README.md", "shared keyword in other-domain");

        using var writer = new StringWriter();
        var exitCode = IntentSearchCommand.Execute(
            workspace.Context,
            ["--query", "shared keyword", "--domain", "other-domain"],
            writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("# Intent search — other-domain", output, StringComparison.Ordinal);
        Assert.Contains("intents/other-domain/README.md", output, StringComparison.Ordinal);
        Assert.DoesNotContain("intents/intent-cli/README.md", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_MissingQueryAndFacet_ReturnsUsageError()
    {
        using var workspace = new IntentSearchWorkspace();
        using var writer = new StringWriter();

        var exitCode = IntentSearchCommand.Execute(
            workspace.Context,
            ["--domain", "intent-cli"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("requires --query and/or --facet", writer.ToString(), StringComparison.Ordinal);
    }

    // ──────────────────────────────────────────────
    // Facet filter (G529)
    // ──────────────────────────────────────────────

    [Fact]
    public void Execute_FacetFilterAlone_ReturnsOnlyNodesCarryingThatFacet()
    {
        using var workspace = new IntentSearchWorkspace();
        workspace.WriteFile(
            "intents/intent-cli/features/auth/overview.md",
            "---\nfacets: [invariant]\n---\n# Auth overview\n");
        workspace.WriteFile(
            "intents/intent-cli/features/auth/decisions.md",
            "---\nfacets: [decider]\n---\n# Auth decisions\n");
        workspace.WriteFile(
            "intents/intent-cli/README.md",
            "No facets here.");

        using var writer = new StringWriter();
        var exitCode = IntentSearchCommand.Execute(
            workspace.Context,
            ["--domain", "intent-cli", "--facet", "invariant", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var hits = document.RootElement.GetProperty("hits");
        var paths = hits.EnumerateArray().Select(h => h.GetProperty("path").GetString()).ToArray();
        Assert.Contains(paths, p => p!.EndsWith("overview.md", StringComparison.Ordinal));
        Assert.DoesNotContain(paths, p => p!.EndsWith("decisions.md", StringComparison.Ordinal));
        Assert.DoesNotContain(paths, p => p!.EndsWith("README.md", StringComparison.Ordinal));
    }

    [Fact]
    public void Execute_FacetFilter_MatchesBlockFormFrontmatter()
    {
        using var workspace = new IntentSearchWorkspace();
        workspace.WriteFile(
            "intents/intent-cli/features/auth/overview.md",
            "---\nfacets:\n  - vocabulary\n  - invariant\n---\n# Auth overview\n");

        using var writer = new StringWriter();
        var exitCode = IntentSearchCommand.Execute(
            workspace.Context,
            ["--domain", "intent-cli", "--facet", "invariant", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var hits = document.RootElement.GetProperty("hits");
        Assert.Equal(1, hits.GetArrayLength());
    }

    [Fact]
    public void Execute_FacetFilter_MalformedFacetsNode_IsExcludedNotErrored()
    {
        using var workspace = new IntentSearchWorkspace();
        workspace.WriteFile(
            "intents/intent-cli/features/auth/broken.md",
            "---\nfacets: invariant\n---\n# Broken facets declaration\n");
        workspace.WriteFile(
            "intents/intent-cli/features/auth/overview.md",
            "---\nfacets: [invariant]\n---\n# Auth overview\n");

        using var writer = new StringWriter();
        var exitCode = IntentSearchCommand.Execute(
            workspace.Context,
            ["--domain", "intent-cli", "--facet", "invariant", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var paths = document.RootElement.GetProperty("hits").EnumerateArray()
            .Select(h => h.GetProperty("path").GetString()).ToArray();
        Assert.Contains(paths, p => p!.EndsWith("overview.md", StringComparison.Ordinal));
        Assert.DoesNotContain(paths, p => p!.EndsWith("broken.md", StringComparison.Ordinal));
    }

    [Fact]
    public void Execute_FacetFilterCombinedWithQuery_RestrictsToBothConditions()
    {
        using var workspace = new IntentSearchWorkspace();
        workspace.WriteFile(
            "intents/intent-cli/features/auth/overview.md",
            "---\nfacets: [invariant]\n---\n# Auth overview\nDescribes the login invariant.\n");
        workspace.WriteFile(
            "intents/intent-cli/features/auth/requirements.md",
            "---\nfacets: [invariant]\n---\n# Auth requirements\nNo matching keyword here.\n");

        using var writer = new StringWriter();
        var exitCode = IntentSearchCommand.Execute(
            workspace.Context,
            ["--domain", "intent-cli", "--query", "login invariant", "--facet", "invariant", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var hits = document.RootElement.GetProperty("hits");
        var paths = hits.EnumerateArray().Select(h => h.GetProperty("path").GetString()).ToArray();
        Assert.Contains(paths, p => p!.EndsWith("overview.md", StringComparison.Ordinal));
        Assert.DoesNotContain(paths, p => p!.EndsWith("requirements.md", StringComparison.Ordinal));
    }

    [Fact]
    public void Execute_UnknownFacetValue_ReturnsUsageError()
    {
        using var workspace = new IntentSearchWorkspace();
        using var writer = new StringWriter();

        var exitCode = IntentSearchCommand.Execute(
            workspace.Context,
            ["--domain", "intent-cli", "--facet", "projection"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("--facet must be one of", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_UnsupportedFormat_ReturnsUsageError()
    {
        using var workspace = new IntentSearchWorkspace();
        using var writer = new StringWriter();

        var exitCode = IntentSearchCommand.Execute(
            workspace.Context,
            ["--query", "x", "--format", "yaml"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("--format must be 'markdown' or 'json'", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_HelpFlag_PrintsUsage()
    {
        using var workspace = new IntentSearchWorkspace();
        using var writer = new StringWriter();

        var exitCode = IntentSearchCommand.Execute(
            workspace.Context,
            ["--help"],
            writer);

        Assert.Equal(0, exitCode);
        Assert.Contains("intent search", writer.ToString(), StringComparison.Ordinal);
        Assert.Contains("--query", writer.ToString(), StringComparison.Ordinal);
    }

    private sealed class IntentSearchWorkspace : IDisposable
    {
        private readonly string rootPath = Directory
            .CreateTempSubdirectory("intent-search-tests-")
            .FullName;

        public IntentSearchWorkspace()
        {
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

        public void WriteFile(string relativePath, string content)
        {
            var full = Path.Combine(rootPath, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, content);
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
