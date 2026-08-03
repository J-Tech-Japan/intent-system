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
        Assert.Contains(SessionLayerSwitchChecklist.Heading, checklist, StringComparison.Ordinal);
        Assert.DoesNotContain("ships in G571", checklist, StringComparison.OrdinalIgnoreCase);
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
        if (instruction.Equals("Codex bridge", StringComparison.OrdinalIgnoreCase))
        {
            // G571 requires this one non-operative contrast while preserving
            // the G570 ban on every actual bridge instruction.
            body = body.Replace(
                "agmsg Codex bridge's headless auto-decline",
                string.Empty,
                StringComparison.OrdinalIgnoreCase);
        }

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
            // G582's mode-independent switch checklist intentionally names
            // BOTH transports in both modes. It is the handover boundary,
            // not an instruction to operate agmsg in herdr-only.
            if (property.Name == SessionLayerSwitchChecklist.JsonProperty)
            {
                continue;
            }
            CollectStringValues(property.Value, path: property.Name, values);
        }

        var body = string.Join("\n", values);
        if (instruction.Equals("Codex bridge", StringComparison.OrdinalIgnoreCase))
        {
            body = body.Replace(
                "agmsg Codex bridge's headless auto-decline",
                string.Empty,
                StringComparison.OrdinalIgnoreCase);
        }

        Assert.DoesNotContain(instruction, body, StringComparison.OrdinalIgnoreCase);
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

        Assert.Contains(SessionLayerSections.DescriptiveAgmsgContextPrefix, output, StringComparison.Ordinal);

        var lines = output.Split('\n').Select(line => line.TrimEnd('\r')).ToArray();
        var labelIndexes = Enumerable.Range(0, lines.Length)
            .Where(i => lines[i].StartsWith(SessionLayerSections.DescriptiveAgmsgContextPrefix, StringComparison.Ordinal))
            .ToArray();
        Assert.NotEmpty(labelIndexes);

        // G570 eleventh repair: the previous version extracted the quote FROM the
        // label and then asserted the quote appeared in the output — necessarily
        // true, since the label is part of the output. It would have passed with
        // the described sentence deleted entirely. The label text is removed
        // before any target is located, so the target has to exist on its own.
        var body = lines.Where((_, i) => !labelIndexes.Contains(i)).ToArray();

        // G570 twelfth repair: OCCURRENCES, not distinct values, and a BIJECTION
        // between labels and targets. Counting distinct strings let a duplicated
        // target line collapse back to one and pass; counting per-label without
        // consuming the target let a duplicated label pass too, because each
        // iteration independently found the same target.
        var quotes = labelIndexes.Select(i => QuotedClause(lines[i])).ToArray();

        foreach (var quote in quotes.Distinct(StringComparer.Ordinal))
        {
            var labelOccurrences = quotes.Count(q => string.Equals(q, quote, StringComparison.Ordinal));
            var targetOccurrences = body.Count(line => line.Contains(quote, StringComparison.Ordinal));

            Assert.True(
                targetOccurrences == 1,
                $"a labelled sentence must be rendered exactly once outside the labels; found {targetOccurrences}: {quote}");
            Assert.True(
                labelOccurrences == 1,
                $"a sentence must carry exactly one label; found {labelOccurrences}: {quote}");
        }

        // The mapping is total in both directions: every label consumes one
        // target and every labelled target is consumed by one label.
        Assert.Equal(labelIndexes.Length, quotes.Distinct(StringComparer.Ordinal).Count());

        foreach (var index in labelIndexes)
        {
            var label = lines[index];
            var quote = QuotedClause(label);
            Assert.Contains("agmsg", quote, StringComparison.OrdinalIgnoreCase);

            var target = body.Single(line => line.Contains(quote, StringComparison.Ordinal));

            // The position claim must be true. A label deferred out of a table
            // says so; an inline one does not, and its target follows it.
            if (label.EndsWith(SessionLayerSections.DescriptiveAgmsgContextDeferredNote, StringComparison.Ordinal))
            {
                var targetIndex = Array.FindIndex(lines, l => l == target);
                Assert.True(
                    targetIndex >= 0 && targetIndex < index,
                    "a label claiming its sentence is in the table ABOVE must be emitted after it: " + quote);
                Assert.StartsWith("|", target.TrimStart(), StringComparison.Ordinal);
            }
            else
            {
                var targetIndex = Array.FindIndex(lines, index, l => l == target);
                Assert.True(targetIndex > index, "an inline label must precede the sentence it names: " + quote);
            }
        }

        // G570 twelfth repair: the semantic check uses a TEST-OWNED fixture, not
        // the production FragmentType. Asking the declaration table whether its
        // own classification is right is the classifier-as-its-own-oracle
        // problem again — a correlated misclassification would agree with
        // itself. These sentences are adjudicated here, in the test, by reading
        // them.
        foreach (var quote in quotes)
        {
            Assert.True(
                KnownDescriptiveAgmsgSentences.Any(known => quote.Contains(known, StringComparison.Ordinal)),
                "a label quotes a sentence this test has not adjudicated as descriptive: " + quote);
            Assert.DoesNotContain(
                KnownOperativeSentences,
                operative => quote.Contains(operative, StringComparison.Ordinal));
        }

        // And every sentence this test knows to be descriptive agmsg
        // illustration, and that the document still renders, carries a label.
        foreach (var known in KnownDescriptiveAgmsgSentences)
        {
            if (body.Any(line => line.Contains(known, StringComparison.Ordinal)))
            {
                Assert.True(
                    quotes.Any(q => q.Contains(known, StringComparison.Ordinal)),
                    "a rendered descriptive agmsg sentence carries no label: " + known);
            }
        }
    }

    private static string QuotedClause(string label) =>
        label[SessionLayerSections.DescriptiveAgmsgContextPrefix.Length..]
            .Split(SessionLayerSections.DescriptiveAgmsgContextSuffix)[0]
            .Trim('"');

    /// <summary>
    /// Sentences this TEST adjudicates as descriptive agmsg illustration:
    /// substrate identity and mechanism, stating what something IS. Owned here
    /// so the semantic claim does not consult the production classification it
    /// is meant to check.
    /// </summary>
    private static readonly IReadOnlyList<string> KnownDescriptiveAgmsgSentences =
    [
        "agmsg run directory (`~/.agents/skills/agmsg/run`)",
        "agmsg `(team, role)` file naming",
        "agmsg run-directory state files are named per `(team, role)`",
        "8 dispatches addressed to `review` were silently lost",
        "agmsg is a message/progress/completion signal layer only",
        "coordinate over agmsg",
        "agmsg carries natural-language delegation",
        "agmsg identity and the codex bridge are folder-scoped",
        "the agmsg run directory",
    ];

    /// <summary>
    /// Sentences this TEST adjudicates as instructions — they bind, so a
    /// descriptive label must never quote one. Negative fixture for the same
    /// reason: the claim is the test's, not the table's.
    /// </summary>
    private static readonly IReadOnlyList<string> KnownOperativeSentences =
    [
        "Touch only files whose team segment is yours",
        "never replaces semantic review or authorizes a merge",
        "Verify the process's cwd before stopping an app-server",
        "Write only through the canonical commands for your own domain",
        "never restart, reconfigure, or kill the shared server",
        "READ the pane first",
        "Never delete another team's workspace",
        "every label transition goes through intent-cli",
    ];

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
            // G570 seventh repair: the fragments to check come from the
            // hand-authored DECLARATION table, not from a classifier the
            // production renderer also consults. The renderer and this guard no
            // longer share a decision procedure — they share a decision.
            var declaredKeep = SessionLayerFragments.Declarations
                .Where(d => d.Section == heading
                    && d.Type != SessionLayerSections.FragmentType.TransportOperative)
                .Select(d => SessionLayerFragments.Expand(BareValues, d.Text))
                .ToArray();
            var agmsgSection = SectionText(underAgmsg, heading);
            var agmsgFragments = declaredKeep
                .Where(text => agmsgSection.Contains(text, StringComparison.Ordinal))
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

    /// <summary>
    /// The values <see cref="ModeWorkspace.RenderOrchestratorGuide"/> renders with.
    /// Declarations are stored in placeholder form, so the guards expand them
    /// the same way the renderer does before comparing.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> BareValues =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["<domain>"] = ModeWorkspace.Domain,
            ["<owner/repo>"] = ModeWorkspace.Repo,
            ["<agent>"] = "claude",
        };

    /// <summary>The setup-ready invocation's values, which render a different
    /// intake — supplied inputs instead of a missing-input list.</summary>
    private static readonly IReadOnlyDictionary<string, string> SetupReadyValues =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["<domain>"] = ModeWorkspace.Domain,
            ["<owner/repo>"] = ModeWorkspace.Repo,
            ["<agent>"] = "claude",
            ["<team>"] = "demo-team",
            ["<orchestrator-path>"] = "/w/orchestrator",
            ["<implementation-path>"] = "/w/impl",
            ["<review-path>"] = "/w/review",
            ["<orchestrator-agent>"] = "claude",
            ["<implementer-agent>"] = "claude",
            ["<reviewer-agent>"] = "codex",
            ["<delivery-mode>"] = "monitor",
        };

    /// <summary>Every string value under a rendered JSON node.</summary>
    private static IEnumerable<string> RenderedStrings(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var child in element.EnumerateObject())
                {
                    if (!child.Name.StartsWith("session_layer", StringComparison.Ordinal))
                    {
                        foreach (var text in RenderedStrings(child.Value))
                        {
                            yield return text;
                        }
                    }
                }

                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    foreach (var text in RenderedStrings(item))
                    {
                        yield return text;
                    }
                }

                break;
            case JsonValueKind.String:
                yield return element.GetString()!;
                break;
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

    /// <summary>
    /// G570 seventh repair — the INDEPENDENT exhaustiveness proof. Review
    /// rejected the sixth repair because the guards asked the production
    /// classifier what a fragment was, so they could only confirm it agreed with
    /// itself, and a sentence phrased outside its cue vocabulary silently
    /// classified as description.
    ///
    /// This guard never calls the classifier. It re-derives the rendered
    /// fragments from the agmsg-mode OUTPUT with its own markdown reading, and
    /// requires each one to consume exactly one declaration — matched by
    /// verbatim text, which is a fact rather than a judgement. A newly added or
    /// reworded sentence therefore fails HERE, before it can reach herdr-only
    /// output untyped.
    /// </summary>
    [Fact]
    public void EveryRenderedFragmentInAMixedSectionConsumesExactlyOneDeclaration_G570()
    {
        // Both invocation shapes: inputs missing and inputs supplied. They
        // render different intake fragments, so proving either one alone would
        // leave the other's fragments untyped.
        var consumed = new List<(string Section, string Text)>();
        var undeclared = new List<string>();

        var shapes = new List<(string Rendered, IReadOnlyDictionary<string, string> Values)>
        {
            (workspace.RenderOrchestratorGuide(), BareValues),
            (workspace.RenderSetupReadyMarkdownWithoutModeRecord(), SetupReadyValues),
        };
        foreach (var policy in new[] { "none", "will-stop", "keep" })
        {
            shapes.Add((workspace.RenderSetupReadyMarkdown(policy), SetupReadyValues));
        }

        // A RECORDED agmsg selection, which states the mode differently from the
        // default, and the herdr-only renderings. Declaring only what the
        // default happens to render would leave the other states untyped.
        using var agmsgWorkspace = new ModeWorkspace();
        Assert.Equal(0, agmsgWorkspace.RunSet(SessionLayerMode.Agmsg, write: true).ExitCode);
        shapes.Add((agmsgWorkspace.RenderOrchestratorGuide(), BareValues));
        shapes.Add((agmsgWorkspace.RenderSetupReadyMarkdown(), SetupReadyValues));

        using var herdrWorkspace = new ModeWorkspace();
        Assert.Equal(0, herdrWorkspace.RunSet(SessionLayerMode.HerdrOnly, write: true).ExitCode);
        shapes.Add((herdrWorkspace.RenderOrchestratorGuide(), BareValues));
        shapes.Add((herdrWorkspace.RenderSetupReadyMarkdown(), SetupReadyValues));

        foreach (var (rendered, values) in shapes)
        {
            string? section = null;
            var inFence = false;

            foreach (var raw in rendered.Split('\n'))
            {
                var line = raw.TrimEnd('\r');
                var trimmed = line.Trim();

                if (trimmed.StartsWith("```", StringComparison.Ordinal))
                {
                    inFence = !inFence;
                    continue;
                }

                if (!inFence && line.StartsWith("## ", StringComparison.Ordinal))
                {
                    section = SessionLayerFragments.IsDeclaredSection(line) ? line : null;
                    continue;
                }

                if (section is null || trimmed.Length == 0)
                {
                    continue;
                }

                // This guard's OWN reading of what carries no semantics. It is
                // deliberately re-implemented rather than borrowed, so a bug in the
                // production notion of "structural" cannot hide a fragment here.
                if (trimmed.StartsWith("#", StringComparison.Ordinal)
                    || (trimmed.StartsWith("|", StringComparison.Ordinal)
                        && trimmed.Trim('|', '-', ':', ' ').Length == 0))
                {
                    continue;
                }

                if (trimmed.Contains(SessionLayerSections.MechanicPointer, StringComparison.Ordinal)
                    || trimmed.Contains("descriptive, not an instruction", StringComparison.Ordinal))
                {
                    // The pointer and the descriptive label are what routing
                    // PRODUCES, not fragments the document declares.
                    continue;
                }

                var matches = SessionLayerFragments.Declarations
                    .Where(d => d.Section == section
                        && SessionLayerFragments.Expand(values, d.Text) == trimmed)
                    .ToArray();

                if (matches.Length == 0)
                {
                    undeclared.Add($"[{section}] {trimmed}");
                    continue;
                }

                Assert.True(
                    matches.Length == 1,
                    $"fragment matches {matches.Length} declarations in `{section}`: {trimmed}");
                consumed.Add((section, trimmed));
            }
        }

        Assert.True(
            undeclared.Count == 0,
            "rendered fragments with no declared type — each must be typed by hand in "
            + "SessionLayerFragments.Declarations before it can be routed or retained on purpose:\n"
            + string.Join("\n", undeclared));

        // And no declaration is decorative: every row names a fragment that is
        // actually rendered, so the table cannot drift ahead of the document.
        var unconsumedRows = SessionLayerFragments.Declarations
            .Where(d => !shapes.Any(shape =>
                consumed.Contains((d.Section, SessionLayerFragments.Expand(shape.Values, d.Text)))))
            .Select(d => $"[{d.Section}] {d.Text}")
            .ToArray();
        Assert.True(
            unconsumedRows.Length == 0,
            "declared fragments that no longer appear in the rendered guide:\n" + string.Join("\n", unconsumedRows));
    }

    /// <summary>
    /// The same proof for the JSON surface, with its own independent walk of the
    /// document. Markdown and JSON declare their fragments separately, so
    /// neither surface can be proved exhaustive by the other.
    /// </summary>
    [Fact]
    public void EveryRenderedJsonValueInAMixedPropertyConsumesExactlyOneDeclaration_G570()
    {
        var shapes = new List<(JsonElement Root, IReadOnlyDictionary<string, string> Values)>
        {
            (workspace.RenderOrchestratorGuideJson(), BareValues),
            (workspace.RenderSetupReadyJsonWithoutModeRecord(), SetupReadyValues),
        };
        foreach (var policy in new[] { "none", "will-stop", "keep" })
        {
            shapes.Add((workspace.RenderSetupReadyJson(policy), SetupReadyValues));
        }

        using var agmsgWorkspace = new ModeWorkspace();
        Assert.Equal(0, agmsgWorkspace.RunSet(SessionLayerMode.Agmsg, write: true).ExitCode);
        shapes.Add((agmsgWorkspace.RenderOrchestratorGuideJson(), BareValues));
        shapes.Add((agmsgWorkspace.RenderSetupReadyJson(), SetupReadyValues));

        using var herdrWorkspace = new ModeWorkspace();
        Assert.Equal(0, herdrWorkspace.RunSet(SessionLayerMode.HerdrOnly, write: true).ExitCode);
        shapes.Add((herdrWorkspace.RenderOrchestratorGuideJson(), BareValues));
        shapes.Add((herdrWorkspace.RenderSetupReadyJson(), SetupReadyValues));

        var undeclared = new List<string>();
        var consumed = new HashSet<(string, string)>();
        IReadOnlyDictionary<string, string> values = BareValues;

        void Walk(string property, JsonElement element)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    foreach (var child in element.EnumerateObject())
                    {
                        if (!child.Name.StartsWith("session_layer", StringComparison.Ordinal))
                        {
                            Walk(property, child.Value);
                        }
                    }

                    break;
                case JsonValueKind.Array:
                    foreach (var item in element.EnumerateArray())
                    {
                        Walk(property, item);
                    }

                    break;
                case JsonValueKind.String:
                    var text = element.GetString()!;
                    if (text.Contains(SessionLayerSections.MechanicPointer, StringComparison.Ordinal)
                        || text.Contains("descriptive, not an instruction", StringComparison.Ordinal))
                    {
                        break;
                    }

                    var matches = SessionLayerFragments.JsonDeclarations
                        .Where(d => d.Section == property
                            && SessionLayerFragments.Expand(values, d.Text) == text)
                        .ToArray();
                    if (matches.Length == 0)
                    {
                        undeclared.Add($"[{property}] {text}");
                    }
                    else
                    {
                        consumed.Add((property, text));
                    }

                    break;
            }
        }

        foreach (var shape in shapes)
        {
            values = shape.Values;
            foreach (var property in SessionLayerSections.MixedJsonProperties)
            {
                if (shape.Root.TryGetProperty(property, out var value))
                {
                    Walk(property, value);
                }
            }
        }

        Assert.True(
            undeclared.Count == 0,
            "rendered JSON values with no declared type:\n" + string.Join("\n", undeclared));

        var unconsumedRows = SessionLayerFragments.JsonDeclarations
            .Where(d => !shapes.Any(shape =>
                consumed.Contains((d.Section, SessionLayerFragments.Expand(shape.Values, d.Text)))))
            .Select(d => $"[{d.Section}] {d.Text}")
            .ToArray();
        Assert.True(
            unconsumedRows.Length == 0,
            "declared JSON fragments no longer rendered:\n" + string.Join("\n", unconsumedRows));
    }

    /// <summary>
    /// The reviewer's named survivors from the sixth repair. Each is asserted by
    /// its own words, so this stays a statement about the DOCUMENT rather than
    /// about the mechanism that produced it.
    /// </summary>
    [Theory]
    [InlineData("re-runs readiness plus the ping test")]
    [InlineData("real-time message monitor")]
    [InlineData("Catch inbound agmsg traffic as it arrives")]
    [InlineData("supervision schedulers are session-scoped")]
    [InlineData("re-arm delivery, or restart the session")]
    public void UnderHerdrOnly_TheSixthRepairSurvivorsAreRoutedAway_G570(string survivor)
    {
        var underAgmsg = workspace.RenderOrchestratorGuide();
        Assert.Contains(survivor, underAgmsg, StringComparison.Ordinal);

        Assert.Equal(0, workspace.RunSet(SessionLayerMode.HerdrOnly, write: true).ExitCode);
        Assert.DoesNotContain(survivor, workspace.RenderOrchestratorGuide(), StringComparison.Ordinal);
        Assert.DoesNotContain(survivor, workspace.RenderOrchestratorGuideJson().GetRawText(), StringComparison.Ordinal);
    }

    /// <summary>
    /// Run-directory and history canon stays byte-identical inside the
    /// descriptive label — routing the mechanics must not cost the reader the
    /// substrate identity they need to reason about a shared machine.
    /// </summary>
    [Theory]
    [InlineData("agmsg run directory")]
    [InlineData("agmsg `(team, role)` file naming")]
    [InlineData("8 dispatches addressed to `review` were silently lost")]
    public void UnderHerdrOnly_RunDirectoryAndHistoryCanonSurvivesByteIdentically_G570(string canon)
    {
        Assert.Contains(canon, workspace.RenderOrchestratorGuide(), StringComparison.Ordinal);
        Assert.Equal(0, workspace.RunSet(SessionLayerMode.HerdrOnly, write: true).ExitCode);
        Assert.Contains(canon, workspace.RenderOrchestratorGuide(), StringComparison.Ordinal);
    }

    /// <summary>
    /// G570 seventh repair: the title is ONE identity with a rendering per mode,
    /// and BOTH renderings are proved consumed by an actual rendered title.
    ///
    /// The sixth repair declared two independent title rows, which is why review
    /// could point out that nothing tied them to the same thing. This asserts
    /// the identity survives in both renderings, that each rendering is the
    /// literal first line of the document in its own mode, and that no rendering
    /// is declared without a mode that produces it.
    /// </summary>
    [Fact]
    public void TheTitleIsOneIdentityWithARenderingPerMode_BothConsumed_G570()
    {
        var agmsgTitle = workspace.RenderOrchestratorGuide().Split('\n')[0].TrimEnd('\r');
        Assert.Equal(0, workspace.RunSet(SessionLayerMode.HerdrOnly, write: true).ExitCode);
        var herdrTitle = workspace.RenderOrchestratorGuide().Split('\n')[0].TrimEnd('\r');

        Assert.Equal(SessionLayerSections.DocumentTitle.Agmsg, agmsgTitle);
        Assert.Equal(SessionLayerSections.DocumentTitle.HerdrOnly, herdrTitle);

        // The identity is what makes them the same document, so neither
        // rendering may drop it.
        foreach (var rendering in SessionLayerSections.DocumentTitle.Renderings)
        {
            foreach (var word in SessionLayerSections.DocumentTitle.Identity.Split(' '))
            {
                Assert.Contains(word, rendering, StringComparison.Ordinal);
            }
        }

        // Every declared rendering is consumed by a mode that actually produces
        // it — a rendering no mode emits would be a decorative declaration.
        var actual = new[] { agmsgTitle, herdrTitle };
        Assert.Equal(
            SessionLayerSections.DocumentTitle.Renderings.OrderBy(r => r, StringComparer.Ordinal),
            actual.OrderBy(r => r, StringComparer.Ordinal));
    }

    /// <summary>
    /// G570 eighth repair, guard 1: known CROSS-MODE IMPERATIVES are typed
    /// operative, not description.
    ///
    /// The seventh repair declared every fragment explicitly but assigned the
    /// types by construction — anything naming no transport mechanic became
    /// CanonDescriptive. That produced 454 descriptive rows against 14
    /// mode-independent-operative ones and filed binding duties ("READ the pane
    /// first", "never delete another team's workspace", "every label transition
    /// goes through intent-cli") as prose. These are named by their own words so
    /// the assertion is about the DOCUMENT, not about the classifier.
    /// </summary>
    [Theory]
    [InlineData("READ the pane first")]
    [InlineData("Confirm the role is still held by that session before concluding anything")]
    [InlineData("answer only what the MAY list allows")]
    [InlineData("may answer a dialog ONLY after it has actually read")]
    [InlineData("Never delete another team's workspace")]
    [InlineData("if you cannot positively establish ownership, the object is READ-ONLY to you")]
    [InlineData("Never reuse, repurpose, or borrow another team's workspace")]
    [InlineData("every label transition goes through intent-cli worker/automation")]
    [InlineData("No hand-editing queue-state")]
    [InlineData("Never ask intent-cli to launch Claude/Codex/Copilot")]
    [InlineData("never hand-write the artifact")]
    [InlineData("Remove a worktree only with `git worktree remove`")]
    [InlineData("STOP and surface it; do not delete user work")]
    [InlineData("Never raw `gh issue create`")]
    [InlineData("Do not launch implement/review recurring timers")]
    // Inside a MIXED row: the clause must be typed operative even though the
    // row also carries descriptive substrate identity. Collapsing the split
    // fails here, because a whole-row descriptive type cannot satisfy it.
    [InlineData("Touch only files whose team segment is yours")]
    [InlineData("never restart, reconfigure, or kill the shared server")]
    [InlineData("Verify the process\u0027s cwd before stopping an app-server")]
    [InlineData("Write only through the canonical commands for your own domain")]
    public void KnownCrossModeImperativesAreTypedOperative_G570(string imperative)
    {
        var matches = SessionLayerFragments.Declarations
            .Concat(SessionLayerFragments.JsonDeclarations)
            .Where(d => d.Text.Contains(imperative, StringComparison.Ordinal))
            .ToArray();

        Assert.True(matches.Length > 0, $"the guide no longer states this duty: {imperative}");

        foreach (var declaration in matches)
        {
            var operative = declaration.Clauses is null
                ? declaration.Type
                : declaration.Clauses
                    .First(c => c.Text.Contains(imperative, StringComparison.Ordinal)).Type;

            Assert.True(
                operative is SessionLayerSections.FragmentType.ModeIndependentOperative
                    or SessionLayerSections.FragmentType.TransportOperative,
                $"a binding duty is declared {operative}, which files an instruction as prose: {imperative}");
        }
    }

    /// <summary>
    /// G570 eighth repair, guard 2: NO operative fragment is covered by a
    /// descriptive-only label, on either surface.
    ///
    /// Markdown: the agmsg-example banner applies until the next heading or the
    /// next operative fragment, so this walks the rendered document and asserts
    /// nothing operative falls inside a labelled run. JSON: the descriptive
    /// context names values, and none of them may be operative.
    /// </summary>
    [Fact]
    public void NoOperativeFragmentIsCoveredByADescriptiveLabel_G570()
    {
        Assert.Equal(0, workspace.RunSet(SessionLayerMode.HerdrOnly, write: true).ExitCode);

        var lines = workspace.RenderOrchestratorGuide().Split('\n').Select(l => l.TrimEnd('\r')).ToArray();
        string? section = null;
        var labelledClauses = new List<(string Section, string Clause)>();

        foreach (var line in lines)
        {
            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                section = SessionLayerFragments.IsDeclaredSection(line) ? line : null;
                continue;
            }

            if (section is not null
                && line.StartsWith(SessionLayerSections.DescriptiveAgmsgContextPrefix, StringComparison.Ordinal))
            {
                // The label NAMES its clause, so its coverage is that clause —
                // not the line, and never the instructions beside it.
                var quoted = line[SessionLayerSections.DescriptiveAgmsgContextPrefix.Length..]
                    .Split(SessionLayerSections.DescriptiveAgmsgContextSuffix)[0].Trim('"');
                labelledClauses.Add((section, quoted));
            }
        }

        Assert.NotEmpty(labelledClauses);

        // Every clause a label names is declared descriptive. A label can no
        // longer reach an instruction, because it does not reach past its quote.
        var offenders = new List<string>();
        foreach (var (heading, clause) in labelledClauses)
        {
            var declared = SessionLayerFragments.Declarations
                .Where(d => d.Section == heading)
                .SelectMany(d => d.Clauses!)
                .Where(c => SessionLayerFragments.Expand(BareValues, c.Text).Trim() == clause)
                .ToArray();

            if (declared.Length == 0
                || declared.Any(c => c.Type != SessionLayerSections.FragmentType.CanonDescriptive))
            {
                offenders.Add($"[{heading}] {clause}");
            }
        }

        Assert.True(
            offenders.Count == 0,
            "clauses labelled \"descriptive, not an instruction\" that are not declared descriptive:\n"
            + string.Join("\n", offenders));

        // Total statement: no operative CLAUSE anywhere is eligible for the
        // label, whether or not one is rendered today.
        var eligible = SessionLayerFragments.Declarations
            .Concat(SessionLayerFragments.JsonDeclarations)
            .SelectMany(d => d.Clauses!.Select(c => (d.Section, Clause: c)))
            .Where(x => x.Clause.Type != SessionLayerSections.FragmentType.CanonDescriptive
                && SessionLayerFragments.IsAgmsgIllustration(x.Clause))
            .Select(x => $"[{x.Section}] {x.Clause.Text}")
            .ToArray();
        Assert.True(
            eligible.Length == 0,
            "operative clauses the renderer would label descriptive:\n" + string.Join("\n", eligible));
    }

    /// <summary>
    /// G570 ninth repair — the TOTAL guard over the clause/sentence inventory.
    ///
    /// Review found the eighth repair typed whole fragments and split only five
    /// table rows, so multi-sentence fragments that mixed mechanism with a
    /// binding duty still carried one verdict, and a substring fixture over 19
    /// imperatives could not prove otherwise. This states the contract over the
    /// WHOLE inventory: every declaration is a clause list, every clause is one
    /// sentence or the scaffolding between sentences, and the clauses
    /// reconstruct the fragment exactly.
    ///
    /// The sentence split is re-derived here independently of the production
    /// tables, so a clause that silently swallowed a second sentence — the exact
    /// defect under repair — fails.
    /// </summary>
    [Fact]
    public void EveryDeclaredFragmentIsTypedAtSentenceGranularity_G570()
    {
        var offenders = new List<string>();

        foreach (var declaration in SessionLayerFragments.Declarations
                     .Concat(SessionLayerFragments.JsonDeclarations))
        {
            Assert.NotNull(declaration.Clauses);
            Assert.Equal(declaration.Text, string.Concat(declaration.Clauses!.Select(c => c.Text)));

            foreach (var clause in declaration.Clauses!)
            {
                if (clause.Type == SessionLayerSections.FragmentType.Structural)
                {
                    continue;
                }

                // A content clause must be ONE sentence. Independently counted:
                // a sentence ends at .!? followed by whitespace and an opener.
                var sentenceBreaks = System.Text.RegularExpressions.Regex.Matches(
                    clause.Text,
                    @"(?<=[.!?])\s+(?=[A-Z`\""'(\u201C])").Count;
                if (sentenceBreaks > 0)
                {
                    offenders.Add($"[{declaration.Section}] {clause.Text}");
                }
            }
        }

        Assert.True(
            offenders.Count == 0,
            "clauses spanning more than one sentence — each sentence must carry its own type:\n"
            + string.Join("\n", offenders));
    }

    /// <summary>
    /// The reviewer's named cases: a row/value that mixes descriptive substrate
    /// identity with a binding rule must type them apart, and the review/merge
    /// prohibition is an instruction, not description.
    /// </summary>
    [Fact]
    public void MixedSemanticsFragmentsAreDeclaredAsSeparatelyTypedClauses_G570()
    {
        var all = SessionLayerFragments.Declarations.Concat(SessionLayerFragments.JsonDeclarations).ToArray();

        var runDirectory = all.First(d =>
            d.Text.Contains("agmsg run directory (`~/.agents/skills/agmsg/run`)", StringComparison.Ordinal));
        Assert.Contains(
            runDirectory.Clauses!,
            c => c.Text.Contains("agmsg run directory", StringComparison.Ordinal)
                && c.Type == SessionLayerSections.FragmentType.CanonDescriptive);
        Assert.Contains(
            runDirectory.Clauses!,
            c => c.Text.Contains("Touch only files whose team segment is yours", StringComparison.Ordinal)
                && c.Type == SessionLayerSections.FragmentType.ModeIndependentOperative);

        // The review/merge prohibition the ninth review named: a normative rule,
        // never "descriptive, not an instruction".
        foreach (var declaration in all.Where(d =>
                     d.Text.Contains("never replaces semantic review or authorizes a merge", StringComparison.Ordinal)))
        {
            var clause = declaration.Clauses!.First(c =>
                c.Text.Contains("never replaces semantic review or authorizes a merge", StringComparison.Ordinal));
            Assert.True(
                clause.Type is SessionLayerSections.FragmentType.ModeIndependentOperative
                    or SessionLayerSections.FragmentType.TransportOperative,
                "the review/merge prohibition is declared " + clause.Type + ", which files a normative rule as prose");
        }

        // Supervision mechanism and the duty inside the same sentence run are
        // typed apart rather than sharing the fragment's verdict.
        var supervision = all.First(d =>
            d.Text.Contains("It answers a blocking dialog only inside an explicit boundary", StringComparison.Ordinal));
        Assert.Contains(
            supervision.Clauses!,
            c => c.Text.Contains("It answers a blocking dialog only inside an explicit boundary", StringComparison.Ordinal)
                && c.Type == SessionLayerSections.FragmentType.ModeIndependentOperative);
    }

    /// <summary>
    /// G570 ninth repair: the render guard the review asked for — each declared
    /// descriptive agmsg CLAUSE that survives into herdr-only output is actually
    /// covered by the context, one for one. The eighth repair only proved the
    /// converse (that covered values were descriptive), which a renderer that
    /// labelled nothing at all would also satisfy.
    /// </summary>
    [Fact]
    public void EveryDescriptiveAgmsgClauseIsContextCovered_G570()
    {
        Assert.Equal(0, workspace.RunSet(SessionLayerMode.HerdrOnly, write: true).ExitCode);
        var context = workspace.RenderOrchestratorGuideJson()
            .GetProperty("herdr_only_descriptive_agmsg_context");

        var covered = context.EnumerateObject()
            .SelectMany(e => e.Value.GetProperty("descriptive_values").EnumerateArray())
            .Select(v => v.GetString()!)
            .ToHashSet(StringComparer.Ordinal);

        // Expected coverage: every declared descriptive agmsg CLAUSE whose text
        // still appears in the herdr-only rendering. Derived from the
        // declarations and confirmed against the rendered document, so a clause
        // this mode never emits is not demanded, and one that does survive
        // cannot go uncovered.
        var herdrJson = workspace.RenderOrchestratorGuideJson().GetRawText();
        var expected = SessionLayerFragments.JsonDeclarations
            .Where(d => SessionLayerSections.MixedJsonProperties.Contains(d.Section, StringComparer.Ordinal))
            .SelectMany(d => d.Clauses!)
            .Where(SessionLayerFragments.IsAgmsgIllustration)
            .Select(c => SessionLayerFragments.Expand(BareValues, c.Text))
            .Distinct(StringComparer.Ordinal)
            .Where(text => herdrJson.Contains(
                System.Text.Json.JsonEncodedText.Encode(text).ToString(),
                StringComparison.Ordinal))
            .ToList();

        Assert.NotEmpty(expected);
        var missing = expected.Where(text => !covered.Contains(text)).ToArray();
        Assert.True(
            missing.Length == 0,
            "descriptive agmsg clauses that survive without their context:\n" + string.Join("\n", missing));

        // And nothing covered is operative: every listed value matches a clause
        // declared CanonDescriptive under that property.
        foreach (var entry in context.EnumerateObject())
        {
            foreach (var value in entry.Value.GetProperty("descriptive_values").EnumerateArray())
            {
                var text = value.GetString()!;
                var declared = SessionLayerFragments.JsonDeclarations
                    .Where(d => d.Section == entry.Name)
                    .SelectMany(d => d.Clauses!)
                    .Where(c => SessionLayerFragments.Expand(BareValues, c.Text) == text)
                    .ToArray();

                Assert.True(declared.Length > 0, $"context lists an undeclared clause under `{entry.Name}`: {text}");
                Assert.All(declared, c => Assert.Equal(
                    SessionLayerSections.FragmentType.CanonDescriptive,
                    c.Type));
            }
        }
    }

    /// <summary>
    /// G570 tenth repair: every declared table row stays inside ONE contiguous
    /// GFM table.
    ///
    /// The one-for-one label was emitted as a blockquote plus a blank line
    /// between two rows, which terminates the table — the rows after it stopped
    /// being part of it. The existing shape guard only checked that a
    /// pointer-bearing row still ends with `|`, so it could not see this.
    ///
    /// This reads the rendered document and requires each table's rows to be
    /// contiguous, with no interruption between the first and last row.
    /// </summary>
    [Fact]
    public void UnderHerdrOnly_DeclaredTableRowsStayInOneContiguousTable_G570()
    {
        foreach (var mode in new[] { SessionLayerMode.Agmsg, SessionLayerMode.HerdrOnly })
        {
            Assert.Equal(0, workspace.RunSet(mode, write: true).ExitCode);
            var lines = workspace.RenderOrchestratorGuide().Split('\n').Select(l => l.TrimEnd('\r')).ToArray();

            var rowIndexes = Enumerable.Range(0, lines.Length)
                .Where(i => lines[i].TrimStart().StartsWith("|", StringComparison.Ordinal))
                .ToArray();
            Assert.NotEmpty(rowIndexes);

            // Group the rows into tables by adjacency, then require every group
            // to be a run with nothing between its first and last row.
            var groups = new List<List<int>>();
            foreach (var index in rowIndexes)
            {
                if (groups.Count > 0 && index == groups[^1][^1] + 1)
                {
                    groups[^1].Add(index);
                }
                else
                {
                    groups.Add([index]);
                }
            }

            foreach (var group in groups)
            {
                for (var i = group[0]; i <= group[^1]; i++)
                {
                    Assert.True(
                        lines[i].TrimStart().StartsWith("|", StringComparison.Ordinal),
                        $"[{mode}] a non-row line interrupts a markdown table at line {i + 1}: {lines[i]}");
                }
            }

            // And the isolation table specifically keeps every declared row —
            // the case the review found broken.
            var isolationRows = SessionLayerFragments.Declarations
                .Where(d => d.Section.Contains("Cross-project isolation", StringComparison.Ordinal))
                .Select(d => SessionLayerFragments.Expand(BareValues, d.Text))
                .Where(t => t.StartsWith("|", StringComparison.Ordinal))
                .ToArray();
            Assert.NotEmpty(isolationRows);

            var present = isolationRows
                .Select(row => Array.FindIndex(lines, l => l.TrimEnd() == row))
                .Where(i => i >= 0)
                .ToArray();
            Assert.NotEmpty(present);

            var table = groups.Single(g => g.Contains(present[0]));
            foreach (var index in present)
            {
                Assert.True(
                    table.Contains(index),
                    $"[{mode}] a declared isolation-table row fell out of the table: {lines[index]}");
            }
        }
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

        // G570 eighth repair: the context names the descriptive VALUES it
        // covers, not the properties that hold them. A property-level mark told
        // a consumer that binding duties in a mixed property were illustration.
        var context = root.GetProperty("herdr_only_descriptive_agmsg_context");
        Assert.True(context.EnumerateObject().Any(), "retained descriptive agmsg illustration must carry its context");

        foreach (var entry in context.EnumerateObject())
        {
            Assert.Contains(
                "descriptive, not an instruction",
                entry.Value.GetProperty("note").GetString()!,
                StringComparison.Ordinal);

            var covered = entry.Value.GetProperty("descriptive_values").EnumerateArray()
                .Select(v => v.GetString()!).ToArray();
            Assert.NotEmpty(covered);

            foreach (var value in covered)
            {
                // Every covered value is one descriptive agmsg CLAUSE — never an
                // instruction that still binds.
                Assert.Contains("agmsg", value, StringComparison.OrdinalIgnoreCase);
                var declared = SessionLayerFragments.JsonDeclarations
                    .Where(d => d.Section == entry.Name)
                    .SelectMany(d => d.Clauses!)
                    .Where(c => SessionLayerFragments.Expand(BareValues, c.Text) == value)
                    .ToArray();
                Assert.NotEmpty(declared);
                Assert.All(declared, c => Assert.Equal(
                    SessionLayerSections.FragmentType.CanonDescriptive,
                    c.Type));
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
                    || line.StartsWith(SessionLayerSections.ReplacementHeading, StringComparison.Ordinal)
                    || line.StartsWith(SessionLayerSwitchChecklist.Heading, StringComparison.Ordinal);
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
        private static string[] SetupReadyArgs(string format, string existingLoopPolicy = "none") =>
        [
            "guide", "orchestrator-thread",
            "--domain", Domain, "--target-repo", Repo, "--agent", "claude",
            "--team", "demo-team",
            "--orchestrator-path", "/w/orchestrator", "--implementation-path", "/w/impl", "--review-path", "/w/review",
            "--orchestrator-agent", "claude", "--implementer-agent", "claude", "--reviewer-agent", "codex",
            "--delivery-mode", "monitor", "--existing-loop-policy", existingLoopPolicy,
            "--format", format,
        ];

        public string RenderSetupReadyMarkdown(string existingLoopPolicy = "none")
        {
            EnsureSetupTeamRecord();
            return Render(SetupReadyArgs("markdown", existingLoopPolicy));
        }

        public string RenderSetupReadyMarkdownWithoutModeRecord(string existingLoopPolicy = "none") =>
            Render(SetupReadyArgs("markdown", existingLoopPolicy));

        public JsonElement RenderSetupReadyJson(string existingLoopPolicy = "none")
        {
            EnsureSetupTeamRecord();
            return JsonDocument.Parse(Render(SetupReadyArgs("json", existingLoopPolicy))).RootElement.Clone();
        }

        public JsonElement RenderSetupReadyJsonWithoutModeRecord(string existingLoopPolicy = "none") =>
            JsonDocument.Parse(Render(SetupReadyArgs("json", existingLoopPolicy))).RootElement.Clone();

        private void EnsureSetupTeamRecord()
        {
            var mode = SessionLayerModeStore.Resolve(RootPath, Domain, team: null).Mode;
            var result = RunSet(mode, write: true, team: "demo-team");
            Assert.Equal(0, result.ExitCode);
            if (string.Equals(mode, SessionLayerMode.HerdrOnly, StringComparison.Ordinal))
            {
                WriteSetupTopology();
            }
        }

        private void WriteSetupTopology()
        {
            var path = NotifyRoleTopologyStore.ResolvePath(RootPath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(new
            {
                team = "demo-team",
                workspace_id = "w-demo",
                roles = new Dictionary<string, object>
                {
                    ["orchestration"] = new
                    {
                        resident = "herdr",
                        workspace_id = "w-demo",
                        pane_id = "w-demo:p1",
                    },
                    ["implementation"] = new
                    {
                        resident = "herdr",
                        workspace_id = "w-demo",
                        pane_id = "w-demo:p2",
                    },
                },
            }));
        }

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
