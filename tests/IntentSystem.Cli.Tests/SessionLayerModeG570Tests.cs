using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G570: the session-layer mode exists, persists, and routes.
///
/// The operator ruling (2026-08-01, host node 08) is that the session layer is
/// a CHOICE rather than a migration, so the properties that matter are the ones
/// a choice needs: it defaults to the practiced transport, it survives, it can
/// be taken back, and the guidance an agent reads changes with it — without the
/// primary path changing at all for teams that never choose.
/// </summary>
public sealed class SessionLayerModeG570Tests : IDisposable
{
    private static readonly DateTimeOffset FixedNow = new(2026, 8, 1, 12, 0, 0, TimeSpan.Zero);

    private readonly ModeWorkspace workspace = new();

    public SessionLayerModeG570Tests()
    {
        SessionLayerCommand.UtcNowFactory = () => FixedNow;
    }

    public void Dispose()
    {
        SessionLayerCommand.UtcNowFactory = null;
        workspace.Dispose();
    }

    // ------------------------------------------------------------- the surface

    [Fact]
    public void WithNoRecord_TheModeIsAgmsg_AndSaysSo_G570()
    {
        var (exitCode, result) = workspace.RunShow();

        Assert.Equal(0, exitCode);
        Assert.Equal(SessionLayerMode.Agmsg, result.GetProperty("mode").GetString());
        Assert.Equal("default", result.GetProperty("source").GetString());
        Assert.False(File.Exists(workspace.RecordPath), "show must not create the record");
    }

    [Fact]
    public void SetThenShow_RoundTrips_G570()
    {
        Assert.Equal(0, workspace.RunSet(SessionLayerMode.HerdrOnly, write: true).ExitCode);

        var (exitCode, result) = workspace.RunShow();
        Assert.Equal(0, exitCode);
        Assert.Equal(SessionLayerMode.HerdrOnly, result.GetProperty("mode").GetString());
        Assert.Equal("recorded", result.GetProperty("source").GetString());
    }

    [Fact]
    public void SetWithoutWrite_PlansOnly_G570()
    {
        var (exitCode, result) = workspace.RunSet(SessionLayerMode.HerdrOnly, write: false);

        Assert.Equal(0, exitCode);
        Assert.Equal("dry-run", result.GetProperty("command_mode").GetString());
        Assert.False(result.GetProperty("applied").GetBoolean());
        Assert.False(File.Exists(workspace.RecordPath));
    }

    [Fact]
    public void SetIsIdempotent_AndRecordsNoSecondTransition_G570()
    {
        Assert.Equal(0, workspace.RunSet(SessionLayerMode.HerdrOnly, write: true).ExitCode);
        var afterFirst = File.ReadAllBytes(workspace.RecordPath);

        var (exitCode, result) = workspace.RunSet(SessionLayerMode.HerdrOnly, write: true);

        Assert.Equal(0, exitCode);
        Assert.True(result.GetProperty("already_recorded").GetBoolean());
        Assert.False(result.GetProperty("applied").GetBoolean());
        // Byte-identical: a re-assertion must not accumulate trail noise, or the
        // trail stops being a record of decisions and becomes a record of runs.
        Assert.Equal(afterFirst, File.ReadAllBytes(workspace.RecordPath));
    }

    [Fact]
    public void TheTrailRecordsBothDirections_G570()
    {
        // Reversibility is the point of the ruling, so the round trip is the
        // fixture — not just the switch away from the default.
        Assert.Equal(0, workspace.RunSet(SessionLayerMode.HerdrOnly, write: true).ExitCode);
        Assert.Equal(0, workspace.RunSet(SessionLayerMode.Agmsg, write: true).ExitCode);

        var (_, result) = workspace.RunShow();
        Assert.Equal(SessionLayerMode.Agmsg, result.GetProperty("mode").GetString());

        var transitions = result.GetProperty("transitions").EnumerateArray().ToArray();
        Assert.Equal(2, transitions.Length);
        Assert.Equal(SessionLayerMode.Agmsg, transitions[0].GetProperty("from").GetString());
        Assert.Equal(SessionLayerMode.HerdrOnly, transitions[0].GetProperty("to").GetString());
        Assert.Equal(SessionLayerMode.HerdrOnly, transitions[1].GetProperty("from").GetString());
        Assert.Equal(SessionLayerMode.Agmsg, transitions[1].GetProperty("to").GetString());
    }

    [Fact]
    public void ATeamScopedRecord_WinsOverTheDomainWideOne_G570()
    {
        Assert.Equal(0, workspace.RunSet(SessionLayerMode.Agmsg, write: true).ExitCode);
        Assert.Equal(0, workspace.RunSet(SessionLayerMode.HerdrOnly, write: true, team: "alpha").ExitCode);

        Assert.Equal(SessionLayerMode.HerdrOnly, workspace.RunShow(team: "alpha").Result.GetProperty("mode").GetString());
        // A different team, and the domain as a whole, are untouched by it.
        Assert.Equal(SessionLayerMode.Agmsg, workspace.RunShow(team: "beta").Result.GetProperty("mode").GetString());
        Assert.Equal(SessionLayerMode.Agmsg, workspace.RunShow().Result.GetProperty("mode").GetString());
    }

    [Fact]
    public void AnUnknownMode_IsRefused_NotRecorded_G570()
    {
        using var writer = new StringWriter();
        var exitCode = SessionLayerCommand.ExecuteSet(
            workspace.Context, ["--domain", ModeWorkspace.Domain, "--mode", "carrier-pigeon", "--write"], writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("is not a session layer", writer.ToString(), StringComparison.Ordinal);
        Assert.False(File.Exists(workspace.RecordPath));
    }

    [Fact]
    public void AnUnreadableRecord_IsRefused_RatherThanOverwritten_G570()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(workspace.RecordPath)!);
        File.WriteAllText(workspace.RecordPath, "{ not json");

        using var writer = new StringWriter();
        var exitCode = SessionLayerCommand.ExecuteSet(
            workspace.Context, ["--domain", ModeWorkspace.Domain, "--mode", SessionLayerMode.HerdrOnly, "--write"], writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("Refusing to overwrite", writer.ToString(), StringComparison.Ordinal);
        Assert.Equal("{ not json", File.ReadAllText(workspace.RecordPath));
    }

    [Fact]
    public void TheSwitchOutput_NamesTheGuideSwitchChecklist_G570()
    {
        var (_, result) = workspace.RunSet(SessionLayerMode.HerdrOnly, write: true);

        var checklist = result.GetProperty("switch_checklist").GetString()!;
        Assert.Contains("guide orchestrator-thread", checklist, StringComparison.Ordinal);
        Assert.Contains("Session-layer switch checklist", checklist, StringComparison.Ordinal);
        // The pointer is honest about where the content lives: G571 ships it.
        Assert.Contains("G571", checklist, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------- the routing

    [Fact]
    public void UnderAgmsg_TheOrchestratorGuideIsByteIdenticalApartFromTheAddedSection_G570()
    {
        // The practiced path must not shift because a mode concept now exists.
        // The ONE permitted difference is the added session-layer section, which
        // reports honestly whether the mode was chosen or defaulted — so the
        // comparison is made with that section removed.
        var withoutRecord = workspace.RenderOrchestratorGuide();
        Assert.Equal(0, workspace.RunSet(SessionLayerMode.Agmsg, write: true).ExitCode);
        var withAgmsgRecord = workspace.RenderOrchestratorGuide();

        Assert.NotEqual(withoutRecord, withAgmsgRecord); // the added section differs, and only it
        Assert.Equal(WithoutSessionLayerSection(withoutRecord), WithoutSessionLayerSection(withAgmsgRecord));
    }

    /// <summary>
    /// Everything G570 ADDED, removed: the `## Session layer` section and the
    /// setup-intake line that names the mode. What remains is the document as it
    /// rendered before this slice, so comparing two renderings of it is the real
    /// "the practiced path did not move" assertion.
    /// </summary>
    private static string WithoutSessionLayerSection(string output)
    {
        var start = output.IndexOf("## Session layer", StringComparison.Ordinal);
        Assert.True(start >= 0, "every rendering carries the session-layer section");
        var end = output.IndexOf("\n## ", start + 1, StringComparison.Ordinal);
        Assert.True(end > start, "the session-layer section must be followed by another section");
        var withoutSection = output[..start] + output[end..];

        return string.Join(
            '\n',
            withoutSection.Split('\n').Where(line => !line.StartsWith("- session layer: ", StringComparison.Ordinal)));
    }

    [Fact]
    public void UnderAgmsg_TheAgmsgOperationalSectionsRenderUnchanged_G570()
    {
        Assert.Equal(0, workspace.RunSet(SessionLayerMode.Agmsg, write: true).ExitCode);
        var output = workspace.RenderOrchestratorGuide();

        Assert.Contains("join.sh", output, StringComparison.Ordinal);
        Assert.DoesNotContain("HERDR-ONLY MODE", output, StringComparison.Ordinal);
        Assert.Contains("Session layer: agmsg (PRIMARY)", output, StringComparison.Ordinal);
    }

    /// <summary>
    /// G570 rereview repair: the completeness guard, rewritten to assert the
    /// PROPERTY rather than the mechanism.
    ///
    /// The previous version compared the output against the production token
    /// list, so it could only ever prove the list was applied — never that it
    /// was complete. These strings are chosen INDEPENDENTLY: they are the
    /// agmsg instructions a reader would actually act on, written out here by
    /// hand, including the bare-prose forms ("wait for an agmsg delegation")
    /// that carry no mechanic token at all and which the substring approach
    /// could not have caught.
    /// </summary>
    public static TheoryData<string> OperativeAgmsgInstructions() => new()
    {
        "join.sh",
        "delivery.sh",
        "team.sh",
        "inbox.sh",
        "actas",
        "ping/ack",
        "agmsg delegation",
        "agmsg replies",
        "agmsg reply",
        "assume the agmsg role",
        "Register the design role",
        "register a role / join the team",
        "Codex bridge",
        "delivery mode",
        // The exact imperative fragments the fifth rereview found surviving
        // behind a section-level descriptive label.
        "before sending ANY agmsg message",
        "Verify the recipient id against the team roster",
        "agmsg team.sh",
    };

    [Theory]
    [MemberData(nameof(OperativeAgmsgInstructions))]
    public void UnderHerdrOnly_NoOperativeAgmsgInstructionSurvivesInMarkdown_G570(string instruction)
    {
        Assert.Equal(0, workspace.RunSet(SessionLayerMode.HerdrOnly, write: true).ExitCode);
        // G570 sixth repair: NO wholesale descriptive exclusion. Excluding
        // labelled sections is exactly what hid imperative agmsg steps behind a
        // label — the guard now looks at every fragment outside the
        // session-layer metadata, and fragment typing is what keeps descriptive
        // identities from being flagged.
        var body = WithoutSessionLayerExemptions(workspace.RenderOrchestratorGuide());

        Assert.DoesNotContain(instruction, body, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [MemberData(nameof(OperativeAgmsgInstructions))]
    public void UnderHerdrOnly_NoOperativeAgmsgInstructionSurvivesInJson_G570(string instruction)
    {
        Assert.Equal(0, workspace.RunSet(SessionLayerMode.HerdrOnly, write: true).ExitCode);

        using var writer = new StringWriter();
        Assert.Equal(0, CommandRouter.Execute(
            ["guide", "orchestrator-thread", "--domain", ModeWorkspace.Domain, "--target-repo", ModeWorkspace.Repo, "--agent", "claude", "--format", "json"],
            workspace.Context,
            writer));

        using var document = JsonDocument.Parse(writer.ToString());
        var values = new List<string>();
        foreach (var property in document.RootElement.Clone().EnumerateObject())
        {
            CollectStringValues(property.Value, path: property.Name, values);
        }

        Assert.DoesNotContain(instruction, string.Join("\n", values), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The OVER-stripping direction, asserted from the declared
    /// mode-independent list rather than from whatever the selection happens to
    /// keep: every mode-independent section must still be there. An earlier
    /// draft deleted the timer-loop canon because it mentioned agmsg, and only
    /// a positive assertion like this catches that.
    /// </summary>
    [Fact]
    public void UnderHerdrOnly_EveryModeIndependentSectionSurvives_G570()
    {
        Assert.Equal(0, workspace.RunSet(SessionLayerMode.HerdrOnly, write: true).ExitCode);
        var output = workspace.RenderOrchestratorGuide();

        var missing = SessionLayerSections.ModeIndependentHeadings
            .Where(heading => heading.StartsWith("## ", StringComparison.Ordinal))
            .Where(heading => !output.Contains(heading, StringComparison.Ordinal))
            .ToArray();
        Assert.True(
            missing.Length == 0,
            "herdr-only routing removed mode-independent sections: " + string.Join(", ", missing));
    }

    [Fact]
    public void UnderHerdrOnly_EveryAgmsgOnlySectionIsReplaced_G570()
    {
        Assert.Equal(0, workspace.RunSet(SessionLayerMode.HerdrOnly, write: true).ExitCode);
        var output = workspace.RenderOrchestratorGuide();

        var surviving = SessionLayerSections.AgmsgOnlyHeadings
            .Where(heading => output.Contains(heading, StringComparison.Ordinal))
            .ToArray();
        Assert.True(
            surviving.Length == 0,
            "agmsg-only sections still rendered under herdr-only: " + string.Join(", ", surviving));

        Assert.Contains(SessionLayerSections.ReplacementHeading, output, StringComparison.Ordinal);
        Assert.Contains("G571", output, StringComparison.Ordinal);
    }

    [Fact]
    public void UnderAgmsg_EveryAgmsgOnlySectionStillRenders_G570()
    {
        // The other direction: the practiced path keeps everything.
        var output = workspace.RenderOrchestratorGuide();

        var missing = SessionLayerSections.AgmsgOnlyHeadings
            .Where(heading => !output.Contains(heading, StringComparison.Ordinal))
            .ToArray();
        Assert.True(missing.Length == 0, "agmsg mode lost sections: " + string.Join(", ", missing));
        Assert.DoesNotContain(SessionLayerSections.ReplacementHeading, output, StringComparison.Ordinal);
    }

    /// <summary>
    /// End-to-end: a herdr-only setup must reach a usable outcome WITHOUT the
    /// agmsg-only inputs. Requiring `--team` and `--delivery-mode` from a team
    /// that runs neither is the structural version of handing them agmsg
    /// instructions.
    /// </summary>
    [Fact]
    public void UnderHerdrOnly_SetupSucceedsWithoutAgmsgOnlyInputs_G570()
    {
        Assert.Equal(0, workspace.RunSet(SessionLayerMode.HerdrOnly, write: true).ExitCode);

        using var writer = new StringWriter();
        var exitCode = CommandRouter.Execute(
            [
                "guide", "orchestrator-thread",
                "--domain", ModeWorkspace.Domain, "--target-repo", ModeWorkspace.Repo, "--agent", "claude",
                "--orchestrator-path", "/w/orchestrator", "--implementation-path", "/w/impl", "--review-path", "/w/review",
                "--orchestrator-agent", "claude", "--implementer-agent", "claude", "--reviewer-agent", "codex",
                "--existing-loop-policy", "none",
                "--format", "json",
            ],
            workspace.Context,
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var intake = document.RootElement.GetProperty("setup_intake");

        var missing = intake.GetProperty("missing_fields").EnumerateArray().Select(f => f.GetString()).ToArray();
        Assert.True(
            missing.Length == 0,
            "a herdr-only setup was told it is missing agmsg-only inputs: " + string.Join(", ", missing!));
        Assert.Equal(SessionLayerMode.HerdrOnly, intake.GetProperty("session_layer_mode").GetString());
    }

    [Fact]
    public void UnderHerdrOnly_TheModeIndependentCanonStillRenders_G570()
    {
        Assert.Equal(0, workspace.RunSet(SessionLayerMode.HerdrOnly, write: true).ExitCode);
        var output = workspace.RenderOrchestratorGuide();

        // These are properties of the four-thread MODEL, not of the transport,
        // so a transport choice must never remove them.
        Assert.Contains("DESIGN↔ORCHESTRATOR DOUBLE-CHECK", output, StringComparison.Ordinal);
        Assert.Contains("AT MOST ONE DELEGATION PER RECEIVER", output, StringComparison.Ordinal);
        Assert.Contains("intent-cli and GitHub remain authoritative", output, StringComparison.Ordinal);
        Assert.Contains("## Mode separation", output, StringComparison.Ordinal);
    }

    [Fact]
    public void TheSetupIntake_RecordsTheModeInBothModes_G570()
    {
        var beforeAnyRecord = workspace.RenderOrchestratorGuideJson();
        Assert.Equal(
            SessionLayerMode.Agmsg,
            beforeAnyRecord.GetProperty("setup_intake").GetProperty("session_layer_mode").GetString());

        Assert.Equal(0, workspace.RunSet(SessionLayerMode.HerdrOnly, write: true).ExitCode);
        var afterSwitch = workspace.RenderOrchestratorGuideJson();
        Assert.Equal(
            SessionLayerMode.HerdrOnly,
            afterSwitch.GetProperty("setup_intake").GetProperty("session_layer_mode").GetString());
        Assert.Contains(
            "reversible in both directions",
            afterSwitch.GetProperty("setup_intake").GetProperty("session_layer_note").GetString()!,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// G570 third repair: the cross-render guards. Written from the surfaces
    /// the review named and the phrases it found leaking — not from any
    /// production list — so they can still fail if the declarations drift.
    /// </summary>
    [Fact]
    public void UnderHerdrOnly_TheSetupObjectIsModeSpecific_NotTokenReplaced_G570()
    {
        Assert.Equal(0, workspace.RunSet(SessionLayerMode.HerdrOnly, write: true).ExitCode);
        var intake = workspace.RenderSetupReadyJson().GetProperty("setup_intake");

        Assert.Equal("setup-ready", intake.GetProperty("status").GetString());
        // No agmsg-only FIELDS at all — not fields holding pointer values.
        Assert.False(intake.TryGetProperty("agmsg_commands", out _), "herdr-only setup still carries agmsg_commands");
        Assert.False(intake.TryGetProperty("role_prompts", out _), "herdr-only setup still carries role_prompts");

        var inputs = intake.GetProperty("inputs");
        Assert.False(inputs.TryGetProperty("team", out _), "herdr-only setup still carries the agmsg team input");
        Assert.False(inputs.TryGetProperty("delivery_mode", out _), "herdr-only setup still carries delivery_mode");

        // And the headline does not instruct an agmsg registration.
        var headline = intake.GetProperty("headline").GetString()!;
        Assert.DoesNotContain("agmsg commands", headline, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("register the three roles", headline, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UnderAgmsg_TheSetupObjectKeepsItsAgmsgFields_G570()
    {
        var intake = workspace.RenderSetupReadyJson().GetProperty("setup_intake");

        Assert.True(intake.TryGetProperty("agmsg_commands", out _));
        Assert.True(intake.TryGetProperty("role_prompts", out _));
        Assert.True(intake.GetProperty("inputs").TryGetProperty("team", out _));
        Assert.True(intake.GetProperty("inputs").TryGetProperty("delivery_mode", out _));
    }

    /// <summary>
    /// Imperative prose the review found surviving. None of these carries a
    /// script name, so a mechanic list alone would not have caught them.
    /// </summary>
    [Theory]
    [InlineData("delegate implementation over agmsg")]
    [InlineData("over agmsg")]
    [InlineData("via agmsg")]
    [InlineData("through agmsg")]
    public void UnderHerdrOnly_NoImperativeAgmsgProseSurvivesOutsideLabelledContext_G570(string phrase)
    {
        Assert.Equal(0, workspace.RunSet(SessionLayerMode.HerdrOnly, write: true).ExitCode);
        var body = WithoutDescriptiveContext(WithoutSessionLayerExemptions(workspace.RenderOrchestratorGuide()));

        Assert.DoesNotContain(phrase, body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UnderHerdrOnly_DescriptiveAgmsgContentIsExplicitlyLabelled_G570()
    {
        Assert.Equal(0, workspace.RunSet(SessionLayerMode.HerdrOnly, write: true).ExitCode);
        var output = workspace.RenderOrchestratorGuide();

        Assert.Contains(SessionLayerSections.DescriptiveAgmsgContextLabel, output, StringComparison.Ordinal);
        // The label sits immediately under the heading it qualifies, so a
        // reader meets it before the description.
        var heading = output.IndexOf("## Mode separation", StringComparison.Ordinal);
        var label = output.IndexOf(SessionLayerSections.DescriptiveAgmsgContextLabel, StringComparison.Ordinal);
        Assert.True(label > heading && label - heading < 200, "the agmsg-example label must directly follow its heading");
    }

    [Fact]
    public void UnderHerdrOnly_TheAuthorityCanonSurvivesInBothRenderings_G570()
    {
        // The over-stripping case the review named: this sentence was being
        // destroyed because it mentioned agmsg.
        Assert.Equal(0, workspace.RunSet(SessionLayerMode.HerdrOnly, write: true).ExitCode);

        Assert.Contains(
            "intent-cli and GitHub remain authoritative",
            workspace.RenderOrchestratorGuide(),
            StringComparison.Ordinal);
        Assert.Contains(
            "intent-cli and GitHub remain authoritative",
            workspace.RenderOrchestratorGuideJson().GetProperty("summary").GetString()!,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// G570 fourth repair: the guard that actually proves "one row drives every
    /// surface". The previous version only checked the declarations against
    /// themselves, so a rendered heading or JSON property that no row mentioned
    /// went unnoticed — which is exactly how `summary` and the synthetic
    /// replacement metadata escaped the table.
    ///
    /// This enumerates the RENDERED surfaces in both modes and requires each to
    /// be declared.
    /// </summary>
    [Fact]
    public void EveryRenderedSurfaceIsDeclared_InBothModes_G570()
    {
        foreach (var mode in new[] { SessionLayerMode.Agmsg, SessionLayerMode.HerdrOnly })
        {
            Assert.Equal(0, workspace.RunSet(mode, write: true).ExitCode);

            var declaredHeadings = SessionLayerSections.Declarations
                .Select(d => d.Heading)
                .ToHashSet(StringComparer.Ordinal);
            // Headings inside fenced blocks are quoted content (artifact
            // templates the guide shows), not sections of this document.
            var renderedHeadings = OutsideFencedBlocks(workspace.RenderOrchestratorGuide())
                // G570 sixth repair: the TITLE ('# ') as well as sections
                // ('## '). The title row matched neither actual title and went
                // undetected while only sections were enumerated. Deeper
                // headings are sub-parts OF a declared section, and the ruling
                // puts granularity at section-plus-fragment, so they are not
                // separately declared surfaces.
                .Where(line => (line.StartsWith("# ", StringComparison.Ordinal)
                    || line.StartsWith("## ", StringComparison.Ordinal))
                    && !line.StartsWith("### ", StringComparison.Ordinal))
                .Select(line => line.TrimEnd('\r'))
                .ToArray();

            var undeclaredHeadings = renderedHeadings.Where(h => !declaredHeadings.Contains(h)).Distinct().ToArray();
            Assert.True(
                undeclaredHeadings.Length == 0,
                $"[{mode}] rendered markdown sections that no declaration row covers: "
                + string.Join(", ", undeclaredHeadings));

            var declaredProperties = SessionLayerSections.Declarations
                .Where(d => d.JsonProperty is not null)
                .Select(d => d.JsonProperty!)
                .ToHashSet(StringComparer.Ordinal);
            using var document = JsonDocument.Parse(
                workspace.Render(["guide", "orchestrator-thread", "--domain", ModeWorkspace.Domain, "--target-repo", ModeWorkspace.Repo, "--agent", "claude", "--format", "json"]));
            var renderedProperties = document.RootElement.EnumerateObject().Select(p => p.Name).ToArray();

            // G570 fifth repair: no exemptions. Every rendered property,
            // synthetic ones included, must have a declaration row — an
            // exemption is precisely how a surface escapes the table.
            var undeclaredProperties = renderedProperties
                .Where(name => !declaredProperties.Contains(name))
                .ToArray();
            Assert.True(
                undeclaredProperties.Length == 0,
                $"[{mode}] rendered JSON properties that no declaration row covers: "
                + string.Join(", ", undeclaredProperties));
        }
    }

    /// <summary>
    /// G570 fifth repair: a row that names nothing real, or names the same
    /// surface twice, is as bad as a surface with no row — `session_layer` was
    /// declared twice and the synthetic rows were decorative. The table is
    /// authoritative only if every row is consumed exactly once.
    /// </summary>
    [Fact]
    public void EveryDeclarationRowIsUniqueAndConsumed_G570()
    {
        var headings = SessionLayerSections.Declarations.Select(d => d.Heading).ToArray();
        Assert.Equal(headings.Length, headings.Distinct(StringComparer.Ordinal).Count());

        var properties = SessionLayerSections.Declarations
            .Where(d => d.JsonProperty is not null).Select(d => d.JsonProperty!).ToArray();
        var duplicated = properties.GroupBy(p => p, StringComparer.Ordinal)
            .Where(g => g.Count() > 1).Select(g => g.Key).ToArray();
        Assert.True(duplicated.Length == 0, "declaration rows duplicate a JSON property: " + string.Join(", ", duplicated));

        // Every declared surface is actually rendered in at least one mode.
        var rendered = new HashSet<string>(StringComparer.Ordinal);
        foreach (var mode in new[] { SessionLayerMode.Agmsg, SessionLayerMode.HerdrOnly })
        {
            Assert.Equal(0, workspace.RunSet(mode, write: true).ExitCode);
            using var document = JsonDocument.Parse(workspace.Render(
                ["guide", "orchestrator-thread", "--domain", ModeWorkspace.Domain, "--target-repo", ModeWorkspace.Repo, "--agent", "claude", "--format", "json"]));
            foreach (var property in document.RootElement.EnumerateObject())
            {
                rendered.Add(property.Name);
            }

            foreach (var heading in OutsideFencedBlocks(workspace.RenderOrchestratorGuide())
                         .Where(line => line.StartsWith("#", StringComparison.Ordinal)))
            {
                rendered.Add(heading.TrimEnd('\r'));
            }
        }

        var unconsumed = SessionLayerSections.Declarations
            .Where(d => (d.JsonProperty is null || !rendered.Contains(d.JsonProperty))
                && !rendered.Contains(d.Heading))
            .Select(d => d.Heading)
            .ToArray();
        Assert.True(unconsumed.Length == 0, "declaration rows that name no rendered surface: " + string.Join(", ", unconsumed));
    }

    /// <summary>
    /// G570 fifth repair: descriptive agmsg content is mechanism/history — a
    /// shared substrate identity, not an instruction — so inside its explicit
    /// label it stays BYTE-IDENTICAL. The isolation table's "agmsg run
    /// directory" row was being over-stripped.
    /// </summary>
    /// <summary>
    /// G570 sixth repair: byte-identity is now a property of the descriptive
    /// FRAGMENT, not of the whole labelled section — a section holding both
    /// kinds must keep one and route the other, which is the ambiguity the
    /// fifth rereview exposed.
    /// </summary>
    [Fact]
    public void UnderHerdrOnly_DescriptiveFragmentsAreByteIdentical_G570()
    {
        var underAgmsg = workspace.RenderOrchestratorGuide();
        Assert.Equal(0, workspace.RunSet(SessionLayerMode.HerdrOnly, write: true).ExitCode);
        var underHerdr = workspace.RenderOrchestratorGuide();

        foreach (var heading in SessionLayerSections.DescriptiveAgmsgContextHeadings
                     .Where(h => h.StartsWith("## ", StringComparison.Ordinal)))
        {
            var agmsgFragments = SectionText(underAgmsg, heading).Split('\n')
                .Where(line => SessionLayerSections.ClassifyFragment(line) == SessionLayerSections.FragmentType.CanonDescriptive)
                .ToArray();
            var herdrText = SectionText(underHerdr, heading);

            foreach (var fragment in agmsgFragments)
            {
                Assert.True(
                    herdrText.Contains(fragment, StringComparison.Ordinal),
                    $"descriptive fragment lost from `{heading}` under herdr-only: {fragment.Trim()}");
            }
        }
    }

    private static string SectionText(string markdown, string heading)
    {
        var lines = markdown.Split('\n');
        var start = Array.FindIndex(lines, line => line.TrimEnd('\r') == heading);
        Assert.True(start >= 0, $"missing section: {heading}");
        var end = Array.FindIndex(lines, start + 1, line => line.StartsWith("## ", StringComparison.Ordinal));
        return string.Join('\n', lines[(start + 1)..(end < 0 ? lines.Length : end)]);
    }

    /// <summary>
    /// G570 sixth repair: the two clauses of the ruling now hold in the SAME
    /// section. A descriptive identity inside a labelled section survives
    /// byte-identically while an imperative fragment beside it is routed away —
    /// which a whole-section flag could not express in either direction.
    /// </summary>
    [Fact]
    public void UnderHerdrOnly_DescriptiveFragmentsSurviveBesideRoutedImperativeOnes_G570()
    {
        Assert.Equal(0, workspace.RunSet(SessionLayerMode.HerdrOnly, write: true).ExitCode);
        var output = workspace.RenderOrchestratorGuide();

        // Descriptive substrate identity — kept.
        Assert.Contains("agmsg run directory", output, StringComparison.Ordinal);
        // Imperative instruction from a labelled section — routed away.
        Assert.DoesNotContain("agmsg team.sh", output, StringComparison.Ordinal);
        Assert.DoesNotContain("before sending ANY agmsg message", output, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("| agmsg run directory | one per team |", SessionLayerSections.FragmentType.CanonDescriptive)]
    [InlineData("|---|---|", SessionLayerSections.FragmentType.Structural)]
    [InlineData("## Safety boundaries", SessionLayerSections.FragmentType.Structural)]
    [InlineData("- Verify the recipient id against the team roster (`agmsg team.sh`) before every send.", SessionLayerSections.FragmentType.TransportOperative)]
    [InlineData("- The orchestrator stays authoritative for closeout.", SessionLayerSections.FragmentType.CanonDescriptive)]
    internal void FragmentTypingSeparatesDescriptionFromInstruction_G570(string fragment, SessionLayerSections.FragmentType expected)
    {
        Assert.Equal(expected, SessionLayerSections.ClassifyFragment(fragment));
    }

    [Fact]
    public void UnderHerdrOnly_TheTitleDoesNotClaimAgmsg_G570()
    {
        Assert.Equal(0, workspace.RunSet(SessionLayerMode.HerdrOnly, write: true).ExitCode);
        var first = workspace.RenderOrchestratorGuide().Split('\n')[0];

        Assert.DoesNotContain("agmsg-backed", first, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("herdr-only", first, StringComparison.Ordinal);
    }

    [Fact]
    public void UnderHerdrOnly_DescriptiveContextIsPresentInJsonToo_G570()
    {
        Assert.Equal(0, workspace.RunSet(SessionLayerMode.HerdrOnly, write: true).ExitCode);
        var root = workspace.RenderOrchestratorGuideJson();

        var context = root.GetProperty("herdr_only_descriptive_agmsg_context");
        foreach (var property in SessionLayerSections.DescriptiveAgmsgContextJsonProperties)
        {
            if (root.TryGetProperty(property, out _))
            {
                Assert.True(
                    context.TryGetProperty(property, out var label),
                    $"retained descriptive property `{property}` has no explicit agmsg-example context in JSON");
                Assert.Contains("descriptive, not an instruction", label.GetString()!, StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void UnderHerdrOnly_MarkdownTablesKeepTheirShape_G570()
    {
        // A table row replaced by a bare pointer line breaks every row after
        // it. Pointing away happens inside the cells instead.
        Assert.Equal(0, workspace.RunSet(SessionLayerMode.HerdrOnly, write: true).ExitCode);

        foreach (var line in workspace.RenderOrchestratorGuide().Split('\n'))
        {
            if (line.Contains(SessionLayerSections.MechanicPointer, StringComparison.Ordinal)
                && line.TrimStart().StartsWith("|", StringComparison.Ordinal))
            {
                Assert.EndsWith("|", line.TrimEnd());
            }
        }
    }

    [Fact]
    public void UnderHerdrOnly_OrderedListsKeepTheirNumbering_G570()
    {
        // A replaced step must stay a numbered step, or a playbook reads
        // 1, 2, 3, 5 and the reader cannot tell missing from inapplicable.
        Assert.Equal(0, workspace.RunSet(SessionLayerMode.HerdrOnly, write: true).ExitCode);
        var output = workspace.RenderOrchestratorGuide();

        Assert.Matches(@"(?m)^\d+\. \(herdr-only:", output);
    }

    private static IEnumerable<string> OutsideFencedBlocks(string markdown)
    {
        var inFence = false;
        foreach (var line in markdown.Split('\n'))
        {
            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                inFence = !inFence;
                continue;
            }

            if (!inFence)
            {
                yield return line;
            }
        }
    }

    private static string WithoutDescriptiveContext(string markdown)
    {
        var lines = markdown.Split('\n');
        var kept = new List<string>(lines.Length);
        var inDescriptive = false;
        foreach (var line in lines)
        {
            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                inDescriptive = SessionLayerSections.DescriptiveAgmsgContextHeadings.Contains(line, StringComparer.Ordinal);
            }

            if (!inDescriptive)
            {
                kept.Add(line);
            }
        }

        return string.Join('\n', kept);
    }

    // ------------------------------------------------- fail-closed mode state

    /// <summary>
    /// G570 review repair. The first version caught an unreadable record and
    /// returned agmsg so guidance would always render — which meant a corrupted
    /// or hand-edited record silently routed every guide and setup surface
    /// through the wrong transport, and the reader had no way to know the
    /// record was the reason. An invalid PRESENT record is not absence.
    /// </summary>
    public static TheoryData<string, string> InvalidRecords() => new()
    {
        { "malformed json", "{ not json" },
        {
            // G570 third repair: a VALID mode with an empty trail, so this case
            // exercises the empty-trail rule itself. The previous fixture
            // carried an unknown mode and therefore failed for that reason
            // first — it never reached the trail check it was named for.
            "valid mode, empty trail",
            """
            { "schema_version": "1", "entries": [ { "domain": "intent-cli", "mode": "herdr-only",
              "updated_at": "2026-08-01T12:00:00+00:00", "transitions": [] } ] }
            """
        },
        {
            "valid mode, invalid initial transition",
            """
            { "schema_version": "1", "entries": [ { "domain": "intent-cli", "mode": "herdr-only",
              "updated_at": "2026-08-01T12:00:00+00:00", "transitions": [
                { "from": "herdr-only", "to": "herdr-only", "at": "2026-08-01T12:00:00+00:00" } ] } ] }
            """
        },
        {
            // The envelope, not just the entries: a schema version the writer
            // never emits is command-impossible state.
            "schema version the writer never emits",
            """
            { "schema_version": "not-command-produced", "entries": [ { "domain": "intent-cli", "mode": "herdr-only",
              "updated_at": "2026-08-01T12:00:00+00:00", "transitions": [
                { "from": "agmsg", "to": "herdr-only", "at": "2026-08-01T12:00:00+00:00" } ] } ] }
            """
        },
        {
            // G570 fourth repair: the writer keeps exactly ONE record per
            // scope, so duplicates are command-impossible — and "first one
            // wins" would silently pick a mode nobody chose.
            "duplicate scope records",
            """
            { "schema_version": "1", "entries": [
              { "domain": "intent-cli", "mode": "herdr-only", "updated_at": "2026-08-01T12:00:00+00:00",
                "transitions": [ { "from": "agmsg", "to": "herdr-only", "at": "2026-08-01T12:00:00+00:00" } ] },
              { "domain": "intent-cli", "mode": "agmsg", "updated_at": "2026-08-01T13:00:00+00:00",
                "transitions": [ { "from": "agmsg", "to": "agmsg", "at": "2026-08-01T13:00:00+00:00" } ] } ] }
            """
        },
        {
            "entries not in writer order",
            """
            { "schema_version": "1", "entries": [
              { "domain": "zzz", "mode": "herdr-only", "updated_at": "2026-08-01T12:00:00+00:00",
                "transitions": [ { "from": "agmsg", "to": "herdr-only", "at": "2026-08-01T12:00:00+00:00" } ] },
              { "domain": "intent-cli", "mode": "herdr-only", "updated_at": "2026-08-01T12:00:00+00:00",
                "transitions": [ { "from": "agmsg", "to": "herdr-only", "at": "2026-08-01T12:00:00+00:00" } ] } ] }
            """
        },
        {
            "updated_at disagrees with the last transition",
            """
            { "schema_version": "1", "entries": [ { "domain": "intent-cli", "mode": "herdr-only",
              "updated_at": "2026-08-02T09:00:00+00:00", "transitions": [
                { "from": "agmsg", "to": "herdr-only", "at": "2026-08-01T12:00:00+00:00" } ] } ] }
            """
        },
        {
            // G570 fifth repair: `set` is a no-op when the mode is already
            // recorded, so it never appends a same-mode step to an existing
            // record — the reviewer's reproduction routed herdr-only and then
            // let a later `set` append to it.
            "same-mode transition after creation",
            """
            { "schema_version": "1", "entries": [ { "domain": "intent-cli", "mode": "herdr-only",
              "updated_at": "2026-08-01T13:00:00+00:00", "transitions": [
                { "from": "agmsg", "to": "herdr-only", "at": "2026-08-01T12:00:00+00:00" },
                { "from": "herdr-only", "to": "herdr-only", "at": "2026-08-01T13:00:00+00:00" } ] } ] }
            """
        },
        {
            "unknown mode",
            """
            { "schema_version": "1", "entries": [ { "domain": "intent-cli", "mode": "carrier-pigeon",
              "updated_at": "2026-08-01T12:00:00+00:00", "transitions": [] } ] }
            """
        },
        {
            "hand edit: mode disagrees with the trail",
            """
            { "schema_version": "1", "entries": [ { "domain": "intent-cli", "mode": "herdr-only",
              "updated_at": "2026-08-01T12:00:00+00:00", "transitions": [
                { "from": "agmsg", "to": "agmsg", "at": "2026-08-01T12:00:00+00:00" } ] } ] }
            """
        },
        {
            "hand edit: broken transition chain",
            """
            { "schema_version": "1", "entries": [ { "domain": "intent-cli", "mode": "agmsg",
              "updated_at": "2026-08-01T12:00:00+00:00", "transitions": [
                { "from": "agmsg", "to": "herdr-only", "at": "2026-08-01T12:00:00+00:00" },
                { "from": "agmsg", "to": "agmsg", "at": "2026-08-01T13:00:00+00:00" } ] } ] }
            """
        },
    };

    [Theory]
    [MemberData(nameof(InvalidRecords))]
    public void AnInvalidRecord_RoutesNoGuideOutput_G570(string description, string content)
    {
        workspace.WriteRawRecord(content);

        using var writer = new StringWriter();
        var exitCode = CommandRouter.Execute(
            ["guide", "orchestrator-thread", "--domain", ModeWorkspace.Domain, "--target-repo", ModeWorkspace.Repo, "--agent", "claude"],
            workspace.Context,
            writer);
        var output = writer.ToString();

        Assert.True(exitCode != 0, $"{description}: the guide rendered instead of failing closed");
        Assert.Contains("session-layer-mode-unreadable", output, StringComparison.Ordinal);
        // No guidance at all — not the default transport's guidance.
        Assert.DoesNotContain("# Guide — agmsg-backed orchestrator thread", output, StringComparison.Ordinal);
        Assert.DoesNotContain("## Mode separation", output, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(InvalidRecords))]
    public void AnInvalidRecord_IsNeverOverwritten_G570(string description, string content)
    {
        workspace.WriteRawRecord(content);
        var before = File.ReadAllBytes(workspace.RecordPath);

        using var writer = new StringWriter();
        var exitCode = SessionLayerCommand.ExecuteSet(
            workspace.Context, ["--domain", ModeWorkspace.Domain, "--mode", SessionLayerMode.HerdrOnly, "--write"], writer);

        Assert.True(exitCode != 0, $"{description}: set succeeded against an unreadable record");
        Assert.Equal(before, File.ReadAllBytes(workspace.RecordPath));
    }

    [Theory]
    [MemberData(nameof(InvalidRecords))]
    public void AnInvalidRecord_MakesShowFailClosed_G570(string description, string content)
    {
        workspace.WriteRawRecord(content);

        using var writer = new StringWriter();
        var exitCode = SessionLayerCommand.ExecuteShow(
            workspace.Context, ["--domain", ModeWorkspace.Domain], writer);

        Assert.True(exitCode != 0, $"{description}: show reported a mode it could not verify");
        Assert.Contains("session-layer-mode-unreadable", writer.ToString(), StringComparison.Ordinal);
        // Specifically NOT "the default is agmsg" — that is the silent
        // mis-routing this repair exists to stop.
        Assert.DoesNotContain("no session layer recorded", writer.ToString(), StringComparison.Ordinal);
    }

    // ------------------------------------------------------- the G540 boundary

    [Fact]
    public void EveryPreviewQualifier_CarriesItsScopingSentence_G570()
    {
        // The qualifier is only safe while it says what it qualifies. These are
        // the three surfaces that state it.
        var model = workspace.Render(["guide", "model"]);
        var onboarding = workspace.Render(["guide", "onboarding"]);
        Assert.Equal(0, workspace.RunSet(SessionLayerMode.HerdrOnly, write: true).ExitCode);
        var orchestrator = workspace.RenderOrchestratorGuide();

        foreach (var (surface, output) in new[]
                 {
                     ("guide model", model),
                     ("guide onboarding", onboarding),
                     ("guide orchestrator-thread", orchestrator),
                 })
        {
            Assert.True(
                output.Contains(SessionLayerMode.PreviewScopingSentence, StringComparison.Ordinal),
                $"`{surface}` names the preview qualifier without the sentence scoping it to the session transport — "
                + "which is exactly the reading G540 ruled out for the four-thread model.");
        }
    }

    [Fact]
    public void BothModesAreDescribed_WithAgmsgPrimary_G570()
    {
        var model = workspace.Render(["guide", "model"]);

        Assert.Contains("## Session layer (transport for the four threads)", model, StringComparison.Ordinal);
        Assert.Contains("agmsg (PRIMARY)", model, StringComparison.Ordinal);
        Assert.Contains("herdr-only (PREVIEW)", model, StringComparison.Ordinal);
        // The positioning paragraph the packet asks for: when herdr-only is the
        // right call.
        Assert.Contains("herdr-resident on ONE machine", model, StringComparison.Ordinal);
        Assert.Contains(SessionLayerMode.ExclusivitySentence, model, StringComparison.Ordinal);
    }

    [Fact]
    public void OnboardingTeachesTheModeBeforeTransportSpecificSteps_G570()
    {
        var onboarding = workspace.Render(["guide", "onboarding"]);

        Assert.Contains("intent-cli session-layer show", onboarding, StringComparison.Ordinal);
        var sessionLayerStep = onboarding.IndexOf("intent-cli session-layer show", StringComparison.Ordinal);
        var rulesStep = onboarding.IndexOf("intent-cli guide rules list", StringComparison.Ordinal);
        Assert.True(sessionLayerStep < rulesStep, "the session-layer step must come before the later reference steps");
    }

    [Fact]
    public void TheCommandCatalog_KnowsTheGroup_G570()
    {
        using var writer = new StringWriter();
        Assert.Equal(0, CommandRouter.Execute(["guide", "commands", "list", "--format", "json"], workspace.Context, writer));

        using var document = JsonDocument.Parse(writer.ToString());
        var group = document.RootElement.GetProperty("groups").EnumerateArray()
            .Single(g => g.GetProperty("name").GetString() == "session-layer");
        var purpose = group.GetProperty("purpose").GetString()!;
        Assert.Contains("session-layer show", purpose, StringComparison.Ordinal);
        Assert.Contains("agmsg|herdr-only", purpose, StringComparison.Ordinal);
        Assert.Contains("never to the four-thread model", purpose, StringComparison.Ordinal);
    }

    /// <summary>
    /// Every string VALUE in the document, skipping the deliberately
    /// agmsg-naming session-layer fields.
    /// </summary>
    private static void CollectStringValues(JsonElement element, string path, List<string> values)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    if (property.Name == "session_layer" || property.Name.StartsWith("session_layer", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    CollectStringValues(property.Value, $"{path}.{property.Name}", values);
                }
                break;

            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    CollectStringValues(item, path, values);
                }
                break;

            case JsonValueKind.String:
                values.Add(element.GetString() ?? string.Empty);
                break;
        }
    }

    private static string WithoutSessionLayerExemptions(string markdown)
    {
        var lines = markdown.Split('\n');
        var kept = new List<string>(lines.Length);
        var inSessionLayerSection = false;
        foreach (var line in lines)
        {
            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                inSessionLayerSection =
                    line.StartsWith("## Session layer", StringComparison.Ordinal)
                    || line.StartsWith(SessionLayerSections.ReplacementHeading, StringComparison.Ordinal);
            }

            // The switch-checklist section is exempt for the same reason as the
            // session-layer block: it exists to NAME what no longer applies,
            // which is routing metadata, not an instruction to use agmsg.
            if (inSessionLayerSection || line.StartsWith("- session layer: ", StringComparison.Ordinal))
            {
                continue;
            }

            kept.Add(line);
        }

        return string.Join('\n', kept);
    }

    private sealed class ModeWorkspace : IDisposable
    {
        public const string Domain = "intent-cli";
        public const string Repo = "J-Tech-Japan/intent-system";

        public ModeWorkspace()
        {
            RootPath = Directory.CreateTempSubdirectory("session-layer-g570-").FullName;
            Directory.CreateDirectory(Path.Combine(RootPath, ".intent-cli"));
            Context = new CliContext
            {
                RepoRoot = RootPath,
                Config = new CliConfig
                {
                    Project = new ProjectConfig
                    {
                        Domain = Domain,
                        ArtifactRoot = ".intent-cli",
                        WorktreeRoot = ".intent-cli/worktrees",
                    },
                },
            };
        }

        public string RootPath { get; }

        public CliContext Context { get; }

        public string RecordPath => SessionLayerModeStore.ResolvePath(RootPath);

        public (int ExitCode, JsonElement Result) RunShow(string? team = null)
        {
            using var writer = new StringWriter();
            var args = new List<string> { "--domain", Domain, "--format", "json" };
            if (team is not null)
            {
                args.AddRange(["--team", team]);
            }

            var exitCode = SessionLayerCommand.ExecuteShow(Context, args.ToArray(), writer);
            return (exitCode, JsonDocument.Parse(writer.ToString()).RootElement.Clone());
        }

        public (int ExitCode, JsonElement Result) RunSet(string mode, bool write, string? team = null)
        {
            using var writer = new StringWriter();
            var args = new List<string> { "--domain", Domain, "--mode", mode, "--format", "json" };
            if (team is not null)
            {
                args.AddRange(["--team", team]);
            }
            if (write)
            {
                args.Add("--write");
            }

            var exitCode = SessionLayerCommand.ExecuteSet(Context, args.ToArray(), writer);
            return (exitCode, JsonDocument.Parse(writer.ToString()).RootElement.Clone());
        }

        public string Render(string[] args)
        {
            using var writer = new StringWriter();
            var exitCode = CommandRouter.Execute(args, Context, writer);
            Assert.True(exitCode == 0, $"`intent-cli {string.Join(' ', args)}` exited {exitCode}: {writer}");
            return writer.ToString();
        }

        public string RenderOrchestratorGuide() =>
            Render(["guide", "orchestrator-thread", "--domain", Domain, "--target-repo", Repo, "--agent", "claude"]);

        /// <summary>A setup-ready invocation: every intake input supplied.</summary>
        private static string[] SetupReadyArgs(string format) =>
        [
            "guide", "orchestrator-thread",
            "--domain", Domain, "--target-repo", Repo, "--agent", "claude",
            "--team", "demo-team",
            "--orchestrator-path", "/w/orchestrator", "--implementation-path", "/w/impl", "--review-path", "/w/review",
            "--orchestrator-agent", "claude", "--implementer-agent", "claude", "--reviewer-agent", "codex",
            "--delivery-mode", "monitor", "--existing-loop-policy", "none",
            "--format", format,
        ];

        public string RenderSetupReadyMarkdown() => Render(SetupReadyArgs("markdown"));

        public JsonElement RenderSetupReadyJson() =>
            JsonDocument.Parse(Render(SetupReadyArgs("json"))).RootElement.Clone();

        public JsonElement RenderOrchestratorGuideJson()
        {
            var output = Render(["guide", "orchestrator-thread", "--domain", Domain, "--target-repo", Repo, "--agent", "claude", "--format", "json"]);
            return JsonDocument.Parse(output).RootElement.Clone();
        }

        public void WriteRawRecord(string content)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(RecordPath)!);
            File.WriteAllText(RecordPath, content);
        }

        public void Dispose()
        {
            if (Directory.Exists(RootPath))
            {
                Directory.Delete(RootPath, recursive: true);
            }
        }
    }
}
