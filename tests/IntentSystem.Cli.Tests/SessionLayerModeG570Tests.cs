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
    };

    [Theory]
    [MemberData(nameof(OperativeAgmsgInstructions))]
    public void UnderHerdrOnly_NoOperativeAgmsgInstructionSurvivesInMarkdown_G570(string instruction)
    {
        Assert.Equal(0, workspace.RunSet(SessionLayerMode.HerdrOnly, write: true).ExitCode);
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
        CollectStringValues(document.RootElement.Clone(), path: string.Empty, values);

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
