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

    [Fact]
    public void UnderHerdrOnly_TheAgmsgOperationalSectionsBecomePointers_G570()
    {
        Assert.Equal(0, workspace.RunSet(SessionLayerMode.HerdrOnly, write: true).ExitCode);
        var output = workspace.RenderOrchestratorGuide();

        Assert.Contains("HERDR-ONLY MODE", output, StringComparison.Ordinal);
        Assert.Contains("ships in", output, StringComparison.Ordinal);
        Assert.Contains("G571", output, StringComparison.Ordinal);

        // The wholly agmsg-specific OPERATIONAL sections carry the pointer
        // instead of steps an agent could follow — following them would
        // register a transport this team is not running.
        using var writer = new StringWriter();
        Assert.Equal(0, CommandRouter.Execute(
            ["guide", "orchestrator-thread", "--domain", ModeWorkspace.Domain, "--target-repo", ModeWorkspace.Repo, "--agent", "claude", "--format", "json"],
            workspace.Context,
            writer));
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        foreach (var path in new[] { "setup", "receiver_readiness", "monitor_tool_distinction" })
        {
            var section = root.GetProperty(path).GetProperty("summary").GetString()!;
            Assert.Contains("HERDR-ONLY MODE", section, StringComparison.Ordinal);
        }
        Assert.Empty(root.GetProperty("monitor_recovery").EnumerateArray());
        Assert.Contains("HERDR-ONLY MODE", root.GetProperty("design_receiver").GetProperty("setup").EnumerateArray().First().GetString()!, StringComparison.Ordinal);

        // Honest about the boundary: MIXED sections still quote agmsg mechanics
        // (removing them would remove mode-independent canon too), and the
        // rendering says how to read them until G571 restructures them.
        Assert.Contains("treat every agmsg MECHANIC in those sections as not applicable", output, StringComparison.Ordinal);
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

        public JsonElement RenderOrchestratorGuideJson()
        {
            var output = Render(["guide", "orchestrator-thread", "--domain", Domain, "--target-repo", Repo, "--agent", "claude", "--format", "json"]);
            return JsonDocument.Parse(output).RootElement.Clone();
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
