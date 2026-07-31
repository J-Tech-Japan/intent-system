using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G563: pre-release guide↔tree coherence. Five defects the pre-v0.7.0 audit
/// found, each pinned here so the release cannot ship guides that contradict
/// its own headline feature:
///
/// <list type="number">
///   <item>every local-skill prohibition carries the dispatcher-skill
///         carve-out, so an agent that arrived through the shipped skill is
///         not told to stop using it;</item>
///   <item>`guide skill-pack` is a pointer, so exactly one artifact named
///         `intent-cli` is distributed (see GuideSkillPackCommandTests);</item>
///   <item>`guide commands list` knows the `skill` group exists;</item>
///   <item>the paste-ready 5-minute fallback prompts carry the per-receiver
///         delegation cap, not the superseded at-most-one-message cap;</item>
///   <item>the provisioning Authority-boundary sentence enumerates the same
///         four MAY-answer classes the supervision section grants.</item>
/// </list>
/// </summary>
public sealed class GuideCoherenceG563Tests
{
    /// <summary>
    /// Every guide surface that renders a blanket local-skill prohibition.
    /// A new surface that forbids local skills belongs in this table; the
    /// carve-out test is what stops it shipping without the exemption.
    /// </summary>
    private static readonly (string Surface, string[] Args)[] ProhibitionSurfaces = new[]
    {
        ("guide worker issue-to-pr", new[] { "guide", "worker", "issue-to-pr" }),
        ("guide worker pr-comment-fix", new[] { "guide", "worker", "pr-comment-fix" }),
        ("guide onboarding", new[] { "guide", "onboarding" }),
        ("guide intent-work setup --kind domain-organize", new[] { "guide", "intent-work", "setup", "--kind", "domain-organize", "--domain", "intent-cli", "--target-repo", "owner/repo" }),
        ("guide intent-work setup --kind next-slice", new[] { "guide", "intent-work", "setup", "--kind", "next-slice", "--domain", "intent-cli", "--target-repo", "owner/repo" }),
        ("guide intent-work setup --kind packet-preload", new[] { "guide", "intent-work", "setup", "--kind", "packet-preload", "--domain", "intent-cli", "--target-repo", "owner/repo" }),
        ("guide intent-work setup --kind clarification", new[] { "guide", "intent-work", "setup", "--kind", "clarification", "--domain", "intent-cli", "--target-repo", "owner/repo" }),
        ("guide intent-work setup --kind intent-shape", new[] { "guide", "intent-work", "setup", "--kind", "intent-shape", "--domain", "intent-cli", "--target-repo", "owner/repo" }),
        ("guide intent-work setup --kind tree-layout", new[] { "guide", "intent-work", "setup", "--kind", "tree-layout", "--domain", "intent-cli", "--target-repo", "owner/repo" }),
        ("guide intent-work setup --kind restructure", new[] { "guide", "intent-work", "setup", "--kind", "restructure", "--domain", "intent-cli", "--target-repo", "owner/repo" }),
        ("guide intent-work next-slice-execution", new[] { "guide", "intent-work", "next-slice-execution", "--domain", "intent-cli", "--target-repo", "owner/repo" }),
        ("guide intent-work audit", new[] { "guide", "intent-work", "audit" }),
        ("guide closeout run", new[] { "guide", "closeout", "run", "--domain", "intent-cli", "--repo", "owner/repo" }),
        ("guide automation --kind child-implement-update", new[] { "guide", "automation", "--kind", "child-implement-update", "--domain", "intent-cli", "--repo", "J-Tech-Japan/intent-system" }),
        ("guide automation local-loop", new[] { "guide", "automation", "local-loop", "--agent", "claude", "--repo", "owner/repo" }),
        ("guide automation setup --kind child-implement", new[] { "guide", "automation", "setup", "--kind", "child-implement", "--domain", "intent-cli", "--target-repo", "owner/repo" }),
        ("guide automation setup --kind host-review-next-slice", new[] { "guide", "automation", "setup", "--kind", "host-review-next-slice", "--domain", "intent-cli", "--target-repo", "owner/repo" }),
        ("guide prompt-matrix", new[] { "guide", "prompt-matrix" }),
        ("guide workflow task packet-draft", new[] { "guide", "workflow", "task", "packet-draft" }),
        ("guide help", new[] { "guide", "help" }),
        // G563 repair: the four families the first review found missing.
        ("guide prompt-template", new[] { "guide", "prompt-template" }),
        ("guide workflow task intent-interview", new[] { "guide", "workflow", "task", "intent-interview" }),
        ("guide workflow task issue-publish", new[] { "guide", "workflow", "task", "issue-publish" }),
        ("guide workflow task bug-to-intent-repair", new[] { "guide", "workflow", "task", "bug-to-intent-repair" }),
        ("guide collaborate --kind feature-intake", new[] { "guide", "collaborate", "--kind", "feature-intake" }),
    };

    [Fact]
    public void EveryProhibitionSurface_CarriesTheDispatcherSkillCarveOut_G563()
    {
        foreach (var (surface, args) in ProhibitionSurfaces)
        {
            var output = Render(args);

            Assert.True(
                output.Contains(DispatcherSkillCarveOut.Sentence, StringComparison.Ordinal)
                || output.Contains(DispatcherSkillCarveOut.BoundaryClause, StringComparison.Ordinal)
                || output.Contains(DispatcherSkillCarveOut.ForbiddenSourceItem, StringComparison.Ordinal)
                || output.Contains(DispatcherSkillCarveOut.ForbiddenSourceItemWithExamples, StringComparison.Ordinal),
                $"`{surface}` forbids local skills without the dispatcher-skill carve-out. An agent that reached this "
                + "guide through the skill installed by `intent-cli skill install` is being told not to use the thing "
                + "that brought it here. Add DispatcherSkillCarveOut.Sentence (or the matching list/boundary form) to "
                + "this surface.");
        }
    }

    /// <summary>
    /// G563 repair: the curated <see cref="ProhibitionSurfaces"/> table proves
    /// the carve-out actually RENDERS, but a curated list can only guard the
    /// surfaces someone remembered to add — the first review of this slice
    /// found four command families the table omitted while the contradiction
    /// survived in their output.
    ///
    /// So the table is no longer the completeness guard. This test discovers
    /// every command source that writes a blanket local-skill prohibition and
    /// requires it to reference the shared carve-out. A NEW prohibition
    /// surface fails here the moment it is added, with no table to update.
    /// </summary>
    [Fact]
    public void EveryCommandSourceThatForbidsLocalSkills_ReferencesTheSharedCarveOut_G563()
    {
        var commandsDirectory = Path.Combine(FindRepoRoot(), "src", "IntentSystem.Cli", "Commands");
        Assert.True(Directory.Exists(commandsDirectory), $"Command source directory not found: {commandsDirectory}");

        // Substrings that constitute a blanket prohibition in rendered output.
        var prohibitionMarkers = new[] { "local skill file", "local skills", "local skill/" };

        var uncovered = new List<string>();
        foreach (var file in Directory.EnumerateFiles(commandsDirectory, "*.cs").OrderBy(f => f, StringComparer.Ordinal))
        {
            // The carve-out's own definition states the prohibition it exempts.
            if (string.Equals(Path.GetFileName(file), "DispatcherSkillCarveOut.cs", StringComparison.Ordinal))
            {
                continue;
            }

            // `//` and `///` lines never reach a caller, so a prohibition
            // described in a doc comment is not a rendered contradiction.
            var body = string.Join(
                '\n',
                File.ReadAllLines(file).Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal)));

            if (!prohibitionMarkers.Any(marker => body.Contains(marker, StringComparison.Ordinal)))
            {
                continue;
            }

            if (!body.Contains("DispatcherSkillCarveOut.", StringComparison.Ordinal))
            {
                uncovered.Add(Path.GetFileName(file));
            }
        }

        Assert.True(
            uncovered.Count == 0,
            "These command sources forbid local skills in rendered output but never reference the shared carve-out, so "
            + "an agent that arrived through the skill installed by `intent-cli skill install` is told to stop using "
            + "it: "
            + string.Join(", ", uncovered)
            + ". Add DispatcherSkillCarveOut.Sentence (prose/rule lists) or .BoundaryClause (G300 / G330 / G333 lines) "
            + "or the .ForbiddenSourceItem forms (structured forbidden-source lists).");
    }

    [Fact]
    public void CarveOut_NamesItsThreeConditions_AndKeepsWorkflowRestatingSkillsForbidden_G563()
    {
        // The carve-out is narrow on purpose: it is the conditions that make
        // the exemption safe, not the name of the skill. If a future edit
        // drops a condition the exemption stops being justified.
        Assert.Contains("restates no workflow", DispatcherSkillCarveOut.Sentence, StringComparison.Ordinal);
        Assert.Contains("single-sourced from this CLI", DispatcherSkillCarveOut.Sentence, StringComparison.Ordinal);
        Assert.Contains("`intent-cli skill diff` drift detection", DispatcherSkillCarveOut.Sentence, StringComparison.Ordinal);
        Assert.Contains("distributed only by `intent-cli skill install`", DispatcherSkillCarveOut.Sentence, StringComparison.Ordinal);

        // And it must not read as a general amnesty for local skills.
        Assert.Contains(
            "Local skill files that restate workflow (`gh-issue-to-pr`, `gh-fix-pr-comment`, copied runbooks) remain forbidden.",
            DispatcherSkillCarveOut.Sentence,
            StringComparison.Ordinal);
    }

    [Fact]
    public void GuidanceProhibitionCatalog_SkillFallbackEntry_CarriesTheCarveOut_G563()
    {
        // The catalog is the single source for setup contracts and task
        // planners, so the carve-out has to live on the entry itself rather
        // than on each consumer.
        var entry = Assert.Single(
            GuidanceProhibitionCatalog.All.Where(p =>
                string.Equals(p.Id, GuidanceProhibitionCatalog.SkillFallbackForbidden, StringComparison.Ordinal)));

        Assert.Contains(DispatcherSkillCarveOut.Sentence, entry.Description, StringComparison.Ordinal);
    }

    [Fact]
    public void GuideCommandsList_IncludesTheSkillGroup_ConsistentWithTheRouter_G563()
    {
        // The shipped SKILL.md routes agents to this catalog and then names
        // `skill list/diff/install`. A catalog that omits the group sends the
        // agent looking for a surface the catalog says does not exist.
        using var writer = new StringWriter();
        var exit = CommandRouter.Execute(["guide", "commands", "list", "--format", "json"], CreateContext(), writer);
        Assert.Equal(0, exit);

        using var document = JsonDocument.Parse(writer.ToString());
        var skill = Assert.Single(
            document.RootElement.GetProperty("groups")
                .EnumerateArray()
                .Where(g => string.Equals(g.GetProperty("name").GetString(), "skill", StringComparison.Ordinal)));

        var purpose = skill.GetProperty("purpose").GetString()!;
        Assert.Contains("intent-cli skill list", purpose, StringComparison.Ordinal);
        Assert.Contains("install --target", purpose, StringComparison.Ordinal);
        Assert.Contains("diff", purpose, StringComparison.Ordinal);
        Assert.Contains("embedded SKILL.md", purpose, StringComparison.Ordinal);
        Assert.Equal("mixed", skill.GetProperty("mutability").GetString());
    }

    [Fact]
    public void GuideCommandsList_MarkdownRow_ForSkill_DoesNotBreakTheTable_G563()
    {
        // The skill row is the first purpose containing `|` (the --target
        // alternatives). Unescaped, it silently splits into extra columns.
        var output = Render(["guide", "commands", "list"]);
        var row = Assert.Single(output
            .Split('\n')
            .Where(line => line.StartsWith("| skill ", StringComparison.Ordinal)));

        // A well-formed 6-column row is `|`+6 cells+`|`, so splitting on
        // UNESCAPED pipes yields 8 parts (two empties at the ends). Before the
        // escape this row produced 12 — four phantom columns.
        var unescapedPipes = System.Text.RegularExpressions.Regex.Split(row, @"(?<!\\)\|");
        Assert.Equal(8, unescapedPipes.Length);
        Assert.Contains("claude\\|codex\\|copilot\\|all", row, StringComparison.Ordinal);
    }

    [Fact]
    public void OrchestratorFallbackPrompts_CarryThePerReceiverCap_NotAtMostOneMessage_G563()
    {
        var output = Render(["guide", "orchestrator-thread", "--domain", "intent-cli", "--target-repo", "owner/repo", "--agent", "claude"]);

        // The wake contract states the per-receiver cap three times; the
        // paste-ready prompts used to install the superseded rule an operator
        // would then follow verbatim.
        Assert.DoesNotContain("AT MOST ONE message", output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("AT MOST ONE DELEGATION PER RECEIVER", output, StringComparison.Ordinal);
    }

    [Fact]
    public void OrchestratorFallbackPrompts_BothFiveMinuteBlocks_StateTheCorrectedCap_G563()
    {
        using var writer = new StringWriter();
        var exit = CommandRouter.Execute(
            ["guide", "orchestrator-thread", "--domain", "intent-cli", "--target-repo", "owner/repo", "--agent", "claude", "--format", "json"],
            CreateContext(),
            writer);
        Assert.Equal(0, exit);

        using var document = JsonDocument.Parse(writer.ToString());
        var json = document.RootElement.GetRawText();

        // Both paste-ready blocks are structured fields, so assert on them
        // rather than on the rendered prose that surrounds them.
        foreach (var field in new[] { "codex_setup_prompt", "claude_loop_setup_prompt" })
        {
            var prompt = FindStringProperty(document.RootElement, field);
            Assert.False(
                string.IsNullOrWhiteSpace(prompt),
                $"`{field}` is missing from the orchestrator-thread JSON — the paste-ready fallback prompt is the surface the cap fix targets.");
            Assert.Contains("AT MOST ONE DELEGATION PER RECEIVER", prompt!, StringComparison.Ordinal);
            Assert.DoesNotContain("AT MOST ONE message", prompt!, StringComparison.OrdinalIgnoreCase);
        }

        Assert.DoesNotContain("AT MOST ONE message", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ProvisioningAuthorityBoundary_EnumeratesTheSameFourMayAnswerClasses_G563()
    {
        var output = Render(["guide", "orchestrator-thread", "--domain", "intent-cli", "--target-repo", "owner/repo", "--agent", "claude"]);

        // Verbatim agreement is the point: the boundary sentence and the MAY
        // list are composed from the same constants, so a future edit to one
        // cannot narrow the other by accident.
        foreach (var mayAnswerClass in new[]
                 {
                     SupervisionMayAnswerClasses.RequestedConfirmations,
                     SupervisionMayAnswerClasses.VerifiedReadOnlyCommandApprovals,
                     SupervisionMayAnswerClasses.OwnHookTrustScreens,
                     SupervisionMayAnswerClasses.PreauthorizedModeChanges,
                 })
        {
            // Once in the supervision MAY list, once in the boundary sentence.
            Assert.True(
                CountOccurrences(output, mayAnswerClass) >= 2,
                $"MAY-answer class \"{mayAnswerClass}\" appears fewer than twice in the rendered guide: the supervision "
                + "list and the provisioning Authority-boundary sentence must both name it, or the boundary is "
                + "narrower than the grant.");
        }

        Assert.Contains(SupervisionMayAnswerClasses.InlineList, output, StringComparison.Ordinal);

        // Widening is NOT what this fix does: credential / security /
        // permission prompts stay unanswerable with or without authorization.
        Assert.Contains("CREDENTIAL, SECURITY, and PERMISSION prompts are NEVER answerable", output, StringComparison.Ordinal);
        Assert.Contains("no authorization makes them answerable", output, StringComparison.Ordinal);
    }

    [Fact]
    public void NoGuideSurface_StillRendersTheRetiredSkillPackBody_G563()
    {
        // Exactly one artifact named `intent-cli` is distributed: the embedded
        // SKILL.md. The retired renderer must not reappear anywhere.
        foreach (var (surface, args) in ProhibitionSurfaces.Append(("guide skill-pack", ["guide", "skill-pack"])))
        {
            var output = Render(args);
            Assert.False(
                output.Contains("Agent skill pack — `intent-cli`", StringComparison.Ordinal),
                $"`{surface}` renders the retired G488 skill body. `intent-cli skill install` ships the one artifact by that name.");
            Assert.False(
                output.Contains("copy the rendered body", StringComparison.Ordinal),
                $"`{surface}` still instructs the copy-out workflow `intent-cli skill install` replaced.");
        }
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        var count = 0;
        var index = haystack.IndexOf(needle, StringComparison.Ordinal);
        while (index >= 0)
        {
            count++;
            index = haystack.IndexOf(needle, index + needle.Length, StringComparison.Ordinal);
        }

        return count;
    }

    private static string? FindStringProperty(JsonElement element, string name)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    if (string.Equals(property.Name, name, StringComparison.Ordinal)
                        && property.Value.ValueKind == JsonValueKind.String)
                    {
                        return property.Value.GetString();
                    }

                    var nested = FindStringProperty(property.Value, name);
                    if (nested is not null)
                    {
                        return nested;
                    }
                }

                return null;

            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    var nested = FindStringProperty(item, name);
                    if (nested is not null)
                    {
                        return nested;
                    }
                }

                return null;

            default:
                return null;
        }
    }

    private static string FindRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !Directory.Exists(Path.Combine(dir, "src")))
        {
            dir = Path.GetDirectoryName(dir);
        }

        Assert.NotNull(dir);
        return dir!;
    }

    private static string Render(string[] args)
    {
        using var writer = new StringWriter();
        var exit = CommandRouter.Execute(args, CreateContext(), writer);
        Assert.True(
            exit == 0,
            $"`intent-cli {string.Join(' ', args)}` exited {exit}: {writer}");
        return writer.ToString();
    }

    private static CliContext CreateContext()
    {
        return new CliContext
        {
            RepoRoot = Path.GetTempPath(),
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
}
