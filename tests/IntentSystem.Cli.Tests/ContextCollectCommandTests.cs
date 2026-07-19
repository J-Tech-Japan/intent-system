using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

public sealed class ContextCollectCommandTests
{
    [Fact]
    public void Execute_GivenNormalWorkspace_EmitsMarkdownWithExpectedSections()
    {
        // Required scenario 1 (G180): normal context collection. Real queue-state with
        // a Review unit, a clarification with no open blockers, automation bindings,
        // and packet files for the focus unit. Output must include all packet sections
        // and surface the focus unit so the AI tasking thread can read context without
        // opening five files.
        using var workspace = new ContextCollectWorkspace();
        workspace.WriteQueueState(NormalQueueStateJson);
        workspace.WriteClarificationOpen(NoBlockerClarification);
        workspace.WriteAutomationBindings(NormalAutomationBindings);
        workspace.WritePacketFile("G180", "implementation.md", "# Implementation packet for G180\n");
        workspace.WritePacketFile("G180", "review-context.md", "# Review context for G180\n");
        workspace.WritePacketFile("G180", "packet.yaml", "execution_unit: G180\n");
        workspace.WriteRunLog(
            """
            {"ts":"2026-04-29T00:00:00Z","execution_unit":"G179","event":"completed","by":"reviewer"}
            {"ts":"2026-04-29T00:01:00Z","execution_unit":"G180","event":"queued","by":"system"}
            """);

        using var writer = new StringWriter();
        var exitCode = ContextCollectCommand.Execute(workspace.Context, [], writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("# Context packet: intent-cli", output, StringComparison.Ordinal);
        Assert.Contains("## Queue state", output, StringComparison.Ordinal);
        Assert.Contains("## Focus", output, StringComparison.Ordinal);
        Assert.Contains("## Clarification", output, StringComparison.Ordinal);
        Assert.Contains("## Automation bindings", output, StringComparison.Ordinal);
        Assert.Contains("## Recent events", output, StringComparison.Ordinal);
        Assert.Contains("Unit: G180", output, StringComparison.Ordinal);
        Assert.Contains("Open blocker: no", output, StringComparison.Ordinal);
        Assert.Contains("G179", output, StringComparison.Ordinal); // recent event mention
    }

    [Fact]
    public void Execute_GivenMissingOptionalArtifacts_RecordsDegradedNotesWithoutThrowing()
    {
        // Required scenario 2: missing optional artifacts. No queue-state, no
        // clarification, no automation bindings, no runs.jsonl. Must succeed with
        // explicit notes for each missing source.
        using var workspace = new ContextCollectWorkspace();

        using var writer = new StringWriter();
        var exitCode = ContextCollectCommand.Execute(workspace.Context, [], writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("## Notes", output, StringComparison.Ordinal);
        Assert.Contains("no queue-state file", output, StringComparison.Ordinal);
        Assert.Contains("no clarification file", output, StringComparison.Ordinal);
        Assert.Contains("no automation bindings file", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenMalformedQueueState_RecordsParseNoteAndKeepsCommandReadOnly()
    {
        // Required scenario 3: malformed queue or run state. Must not throw; must
        // record a deterministic degraded note and continue with the rest of the
        // packet so the AI thread can still see clarification / runs / bindings.
        using var workspace = new ContextCollectWorkspace();
        workspace.WriteQueueState("{ this is intentionally not valid JSON");
        workspace.WriteRunLog("not a real json line\n");

        using var writer = new StringWriter();
        var exitCode = ContextCollectCommand.Execute(
            workspace.Context,
            ["--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.True(root.GetProperty("queue_state_present").GetBoolean());
        Assert.False(root.GetProperty("queue_state_readable").GetBoolean());
        var notes = root.GetProperty("notes").EnumerateArray().Select(n => n.GetString()).ToArray();
        Assert.Contains(notes, note => note is not null && note.Contains("queue-state", StringComparison.Ordinal));
        Assert.Contains(notes, note => note is not null && note.Contains("runs.jsonl", StringComparison.Ordinal));
    }

    [Fact]
    public void Execute_GivenDomainOverride_UsesOverrideForResolvedPaths()
    {
        // Required scenario 4: domain override. The packet's domain field and
        // resolved clarification / automation paths must reflect the --domain value,
        // not the workspace default.
        using var workspace = new ContextCollectWorkspace();

        using var writer = new StringWriter();
        var exitCode = ContextCollectCommand.Execute(
            workspace.Context,
            ["--domain", "alt-domain", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.Equal("alt-domain", root.GetProperty("domain").GetString());
        var clarificationPath = root.GetProperty("clarification_open_path").GetString();
        Assert.NotNull(clarificationPath);
        Assert.Contains(
            Path.Combine("intents", "alt-domain", "clarifications", "open.md"),
            clarificationPath!,
            StringComparison.Ordinal);
        var bindingsPath = root.GetProperty("automation_bindings_path").GetString();
        Assert.NotNull(bindingsPath);
        Assert.Contains(
            Path.Combine("intents", "alt-domain", "automation", "bindings.md"),
            bindingsPath!,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenJsonFormat_EmitsParsableSnakeCaseFields()
    {
        using var workspace = new ContextCollectWorkspace();
        workspace.WriteQueueState(NormalQueueStateJson);
        workspace.WriteClarificationOpen(NoBlockerClarification);
        workspace.WriteAutomationBindings(NormalAutomationBindings);

        using var writer = new StringWriter();
        var exitCode = ContextCollectCommand.Execute(
            workspace.Context,
            ["--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.Equal("intent-cli", root.GetProperty("domain").GetString());
        Assert.True(root.GetProperty("queue_state_readable").GetBoolean());
        Assert.True(root.GetProperty("automation_bindings_present").GetBoolean());
        Assert.False(root.GetProperty("clarification_open").GetBoolean());
        Assert.Equal("G180", root.GetProperty("focus_unit").GetString());
        Assert.True(root.TryGetProperty("focus_packet", out var focusPacket));
        Assert.Equal(JsonValueKind.Object, focusPacket.ValueKind);
    }

    [Fact]
    public void Execute_GivenOpenBlockerInClarificationFile_ReportsClarificationOpenTrue()
    {
        // Aligns with G179 semantics: structured "## Current Open Blockers" with a
        // real entry must surface as clarification_open=true and provide the
        // excerpt to the AI thread.
        using var workspace = new ContextCollectWorkspace();
        workspace.WriteClarificationOpen(
            """
            # intent-cli clarifications

            ## Current Open Blockers

            - Should `context collect` include parent automation bindings by default?
            """);

        using var writer = new StringWriter();
        var exitCode = ContextCollectCommand.Execute(
            workspace.Context,
            ["--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.True(root.GetProperty("clarification_open").GetBoolean());
        var excerpt = root.GetProperty("clarification_excerpt").GetString();
        Assert.NotNull(excerpt);
        Assert.Contains("Current Open Blockers", excerpt!, StringComparison.Ordinal);
    }

    // ── G530: facet context section ─────────────────────────────────────

    [Fact]
    public void Execute_DomainWithFacetNodes_RendersFacetSectionAheadOfQueueStateInCanonicalOrder()
    {
        using var workspace = new ContextCollectWorkspace();
        workspace.WriteFacetNode("identity/mission.md", ["vocabulary"], "Mission");
        workspace.WriteFacetNode("decisions/adr-1.md", ["decider"], "Decision One");

        using var writer = new StringWriter();
        var exitCode = ContextCollectCommand.Execute(workspace.Context, [], writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("## Facet context", output, StringComparison.Ordinal);
        var facetIndex = output.IndexOf("## Facet context", StringComparison.Ordinal);
        var queueIndex = output.IndexOf("## Queue state", StringComparison.Ordinal);
        Assert.True(facetIndex >= 0 && queueIndex > facetIndex, "Facet context must render ahead of Queue state.");
        var vocabularyIndex = output.IndexOf("### vocabulary", StringComparison.Ordinal);
        var deciderIndex = output.IndexOf("### decider", StringComparison.Ordinal);
        Assert.True(vocabularyIndex >= 0 && deciderIndex > vocabularyIndex, "vocabulary must render before decider.");
        Assert.Contains("identity/mission", output, StringComparison.Ordinal);
        Assert.Contains("Mission", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_FacetsFilter_RestrictsSectionToRequestedFacetsOnly()
    {
        using var workspace = new ContextCollectWorkspace();
        workspace.WriteFacetNode("identity/mission.md", ["vocabulary"], "Mission");
        workspace.WriteFacetNode("decisions/adr-1.md", ["decider"], "Decision One");

        using var writer = new StringWriter();
        var exitCode = ContextCollectCommand.Execute(
            workspace.Context, ["--facets", "decider"], writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("### decider", output, StringComparison.Ordinal);
        Assert.DoesNotContain("### vocabulary", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_FacetsFilterUnknownValue_ReturnsErrorExitCode()
    {
        using var workspace = new ContextCollectWorkspace();
        using var writer = new StringWriter();

        var exitCode = ContextCollectCommand.Execute(
            workspace.Context, ["--facets", "not-a-real-facet"], writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("--facets", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_ScopeHint_NarrowsFacetSectionToOverlappingNodesOnly()
    {
        using var workspace = new ContextCollectWorkspace();
        workspace.WriteFacetNode("identity/mission.md", ["vocabulary"], "Mission");
        workspace.WriteFacetNode("decisions/adr-1.md", ["vocabulary"], "Decision One");

        using var writer = new StringWriter();
        var exitCode = ContextCollectCommand.Execute(
            workspace.Context, ["--scope", "intents/intent-cli/identity"], writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("identity/mission", output, StringComparison.Ordinal);
        Assert.DoesNotContain("decisions/adr-1", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_NoFacetAnnotatedNodesInDomain_EmitsGracefulDegradationNoteNeverAnError()
    {
        using var workspace = new ContextCollectWorkspace();

        using var writer = new StringWriter();
        var exitCode = ContextCollectCommand.Execute(workspace.Context, [], writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("## Facet context", output, StringComparison.Ordinal);
        Assert.Contains("no facet-annotated nodes found", output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Execute_JsonFormat_FacetContextShapeIsStableSnakeCase()
    {
        using var workspace = new ContextCollectWorkspace();
        workspace.WriteFacetNode("identity/mission.md", ["vocabulary", "invariant"], "Mission");

        using var writer = new StringWriter();
        var exitCode = ContextCollectCommand.Execute(
            workspace.Context, ["--format", "json"], writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        var facetContext = root.GetProperty("facet_context").EnumerateArray().ToArray();
        Assert.Equal(4, facetContext.Length);
        Assert.Equal("vocabulary", facetContext[0].GetProperty("facet").GetString());
        var vocabularyNodes = facetContext[0].GetProperty("nodes").EnumerateArray().ToArray();
        var node = Assert.Single(vocabularyNodes);
        Assert.Equal("identity/mission", node.GetProperty("id").GetString());
        Assert.Equal("intents/intent-cli/identity/mission.md", node.GetProperty("path").GetString());
        Assert.Equal("Mission", node.GetProperty("summary").GetString());
        var facets = node.GetProperty("facets").EnumerateArray().Select(f => f.GetString()).ToArray();
        Assert.Equal(new[] { "vocabulary", "invariant" }, facets);
        Assert.True(root.TryGetProperty("facet_context_note", out var note));
        Assert.Equal(JsonValueKind.Null, note.ValueKind);
    }

    // ── Review repair: strict comma-list validation ─────────────────────

    [Fact]
    public void Execute_ScopeValueIsBareComma_ReturnsErrorRatherThanDisablingScope()
    {
        using var workspace = new ContextCollectWorkspace();
        using var writer = new StringWriter();

        var exitCode = ContextCollectCommand.Execute(workspace.Context, ["--scope", ","], writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("--scope", writer.ToString(), StringComparison.Ordinal);
        Assert.Contains("empty element", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_FacetsValueHasEmptyMiddleElement_ReturnsErrorRatherThanDiscardingIt()
    {
        using var workspace = new ContextCollectWorkspace();
        using var writer = new StringWriter();

        var exitCode = ContextCollectCommand.Execute(
            workspace.Context, ["--facets", "vocabulary,,decider"], writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("--facets", writer.ToString(), StringComparison.Ordinal);
        Assert.Contains("empty element", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_FacetsDuplicateElements_DedupedFirstSeenOrder_StillValidatesAndFilters()
    {
        using var workspace = new ContextCollectWorkspace();
        workspace.WriteFacetNode("identity/mission.md", ["vocabulary"], "Mission");
        workspace.WriteFacetNode("decisions/adr-1.md", ["decider"], "Decision One");

        using var writer = new StringWriter();
        var exitCode = ContextCollectCommand.Execute(
            workspace.Context, ["--facets", "decider,vocabulary,decider"], writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("### vocabulary", output, StringComparison.Ordinal);
        Assert.Contains("### decider", output, StringComparison.Ordinal);
        Assert.DoesNotContain("### invariant", output, StringComparison.Ordinal);
    }

    // ── Review repair: malformed/unknown-facet warning visibility ───────

    [Fact]
    public void Execute_MalformedFacetsNode_SurfacesWarningInMarkdown_NeverSilent()
    {
        using var workspace = new ContextCollectWorkspace();
        workspace.WriteRawIntentFile("identity/mission.md", "---\nfacets: not-a-list\n---\n# Mission\n");

        using var writer = new StringWriter();
        var exitCode = ContextCollectCommand.Execute(workspace.Context, [], writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("no facet-annotated nodes found", output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Warnings", output, StringComparison.Ordinal);
        Assert.Contains("intents/intent-cli/identity/mission.md", output, StringComparison.Ordinal);
        Assert.Contains("malformed", output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Execute_JsonFormat_FacetContextWarningsShapeIsStableSnakeCase()
    {
        using var workspace = new ContextCollectWorkspace();
        workspace.WriteFacetNode("identity/mission.md", ["vocabulary", "projection"], "Mission");

        using var writer = new StringWriter();
        var exitCode = ContextCollectCommand.Execute(
            workspace.Context, ["--format", "json"], writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        var warnings = root.GetProperty("facet_context_warnings").EnumerateArray().ToArray();
        var warning = Assert.Single(warnings);
        Assert.Equal("intents/intent-cli/identity/mission.md", warning.GetProperty("path").GetString());
        Assert.Contains("projection", warning.GetProperty("reason").GetString(), StringComparison.Ordinal);
        // The node still appears under its own valid facet despite the warning.
        var vocabularyNodes = root.GetProperty("facet_context")[0].GetProperty("nodes").EnumerateArray().ToArray();
        Assert.Single(vocabularyNodes);
    }

    // ── Review repair: rejected --scope hints are never silent ──────────

    [Fact]
    public void Execute_ScopeHintOutsideDomainRoot_SurfacesScopeWarningInMarkdown()
    {
        using var workspace = new ContextCollectWorkspace();
        workspace.WriteFacetNode("identity/mission.md", ["vocabulary"], "Mission");
        var outsideHint = Path.Combine(Path.GetTempPath(), "definitely-outside", "file.md");

        using var writer = new StringWriter();
        var exitCode = ContextCollectCommand.Execute(
            workspace.Context, ["--scope", outsideHint], writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("Scope warnings", output, StringComparison.Ordinal);
        Assert.Contains("ALL requested --scope hints were rejected", output, StringComparison.Ordinal);
        Assert.Contains(outsideHint, output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_JsonFormat_FacetContextScopeWarningsShapeIsStableSnakeCase()
    {
        using var workspace = new ContextCollectWorkspace();
        workspace.WriteFacetNode("identity/mission.md", ["vocabulary"], "Mission");
        var outsideHint = Path.Combine(Path.GetTempPath(), "definitely-outside", "file.md");

        using var writer = new StringWriter();
        var exitCode = ContextCollectCommand.Execute(
            workspace.Context, ["--scope", outsideHint, "--format", "json"], writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        var scopeWarnings = root.GetProperty("facet_context_scope_warnings").EnumerateArray().ToArray();
        var warning = Assert.Single(scopeWarnings);
        Assert.Equal(outsideHint, warning.GetProperty("hint").GetString());
        Assert.Contains("outside", warning.GetProperty("reason").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.True(root.GetProperty("facet_context_all_scope_hints_rejected").GetBoolean());
        // Rejected, so the node never appears — never silently "shown anyway".
        var vocabularyNodes = root.GetProperty("facet_context")[0].GetProperty("nodes").EnumerateArray().ToArray();
        Assert.Empty(vocabularyNodes);
    }

    [Fact]
    public void Execute_MixedValidAndInvalidScopeHints_NotAllRejected_ValidHintStillApplied()
    {
        using var workspace = new ContextCollectWorkspace();
        workspace.WriteFacetNode("identity/mission.md", ["vocabulary"], "Mission");
        var outsideHint = Path.Combine(Path.GetTempPath(), "definitely-outside", "file.md");

        using var writer = new StringWriter();
        var exitCode = ContextCollectCommand.Execute(
            workspace.Context,
            ["--scope", $"intents/intent-cli/identity,{outsideHint}", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.False(root.GetProperty("facet_context_all_scope_hints_rejected").GetBoolean());
        var scopeWarnings = root.GetProperty("facet_context_scope_warnings").EnumerateArray().ToArray();
        Assert.Single(scopeWarnings);
        var vocabularyNodes = root.GetProperty("facet_context")[0].GetProperty("nodes").EnumerateArray().ToArray();
        Assert.Single(vocabularyNodes);
    }

    [Fact]
    public void Execute_GivenUnknownArgument_ReturnsErrorExitCode()
    {
        using var workspace = new ContextCollectWorkspace();
        using var writer = new StringWriter();

        var exitCode = ContextCollectCommand.Execute(
            workspace.Context,
            ["--bogus"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("--bogus", writer.ToString(), StringComparison.Ordinal);
    }

    private const string NormalQueueStateJson =
        """
        {
          "schema_version": "1",
          "updated_at": "2026-04-29T00:00:00Z",
          "items": [
            {
              "execution_unit": "G179",
              "title": "status brief",
              "state": "completed",
              "dependencies": [],
              "blocked_by": [],
              "clarification_return_path": "intents/intent-cli/clarifications/open.md",
              "packet_paths": {
                "implementation": ".intent-cli/issues/G179/implementation.md",
                "review_context": ".intent-cli/issues/G179/review-context.md",
                "yaml": ".intent-cli/issues/G179/packet.yaml"
              },
              "worker_role": "coder",
              "review_role": "reviewer",
              "priority": "high"
            },
            {
              "execution_unit": "G180",
              "title": "context collect",
              "state": "review",
              "dependencies": ["G179"],
              "blocked_by": [],
              "clarification_return_path": "intents/intent-cli/clarifications/open.md",
              "packet_paths": {
                "implementation": ".intent-cli/issues/G180/implementation.md",
                "review_context": ".intent-cli/issues/G180/review-context.md",
                "yaml": ".intent-cli/issues/G180/packet.yaml"
              },
              "worker_role": "coder",
              "review_role": "reviewer",
              "priority": "high"
            }
          ]
        }
        """;

    private const string NoBlockerClarification =
        """
        # intent-cli clarifications

        durable prose

        ## Current Open Blockers

        - 現時点で child issue cut を要する root blocker はない。
        """;

    private const string NormalAutomationBindings =
        """
        # intent-cli automation bindings

        - timer-implement-loop: every 10m
        - timer-review-loop: every 5m
        - status-brief-recommendation-mapping: review-closeout, clarification-required, ...
        """;

    private sealed class ContextCollectWorkspace : IDisposable
    {
        private readonly string rootPath = Directory
            .CreateTempSubdirectory("context-collect-tests-")
            .FullName;

        public ContextCollectWorkspace()
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

        public void WriteQueueState(string content)
        {
            File.WriteAllText(Context.GetQueueStatePath(), content);
        }

        public void WriteRunLog(string content)
        {
            File.WriteAllText(Context.GetRunLogPath(), content);
        }

        public void WriteClarificationOpen(string content)
        {
            var path = Path.Combine(rootPath, "intents", Context.Config.Project.Domain, "clarifications");
            Directory.CreateDirectory(path);
            File.WriteAllText(Path.Combine(path, "open.md"), content);
        }

        public void WriteAutomationBindings(string content)
        {
            var path = Path.Combine(rootPath, "intents", Context.Config.Project.Domain, "automation");
            Directory.CreateDirectory(path);
            File.WriteAllText(Path.Combine(path, "bindings.md"), content);
        }

        /// <summary>G530: writes a facet-annotated intent-tree node under `intents/&lt;domain&gt;/&lt;relativePath&gt;`.</summary>
        public void WriteFacetNode(string relativePath, IReadOnlyList<string> facets, string title)
        {
            var fullPath = Path.Combine(
                rootPath, "intents", Context.Config.Project.Domain, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            File.WriteAllText(fullPath, $"---\nfacets: [{string.Join(", ", facets)}]\n---\n# {title}\n");
        }

        /// <summary>G530 review repair: writes arbitrary raw content under `intents/&lt;domain&gt;/&lt;relativePath&gt;` — used to pin a malformed `facets:` declaration.</summary>
        public void WriteRawIntentFile(string relativePath, string content)
        {
            var fullPath = Path.Combine(
                rootPath, "intents", Context.Config.Project.Domain, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            File.WriteAllText(fullPath, content);
        }

        public void WritePacketFile(string executionUnit, string fileName, string content)
        {
            var path = Path.Combine(rootPath, ".intent-cli", "issues", executionUnit);
            Directory.CreateDirectory(path);
            File.WriteAllText(Path.Combine(path, fileName), content);
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
