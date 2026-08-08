using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// A fact observed while measuring an unattended launch. The registry keeps
/// the observation, its provenance, and the platform together so a kind switch
/// cannot accidentally turn an unmeasured guess into an operational recipe.
/// </summary>
internal sealed record AgentLaunchRecipeMeasurement
{
    [JsonPropertyName("status")]
    public required string Status { get; init; }

    [JsonPropertyName("fact")]
    public required string Fact { get; init; }

    [JsonPropertyName("observation")]
    public required string Observation { get; init; }

    [JsonPropertyName("version")]
    public required string Version { get; init; }

    [JsonPropertyName("platform")]
    public required string Platform { get; init; }
}

/// <summary>
/// The operator-facing recipe for one agent kind. This is guidance and
/// evidence, not a launcher: intent-cli never starts an agent or evaluates a
/// permission prompt on the operator's behalf.
/// </summary>
internal sealed record AgentLaunchRecipe
{
    [JsonPropertyName("kind")]
    public required string Kind { get; init; }

    [JsonPropertyName("invocation")]
    public required string Invocation { get; init; }

    [JsonPropertyName("role_derived_roots")]
    public required string RoleDerivedRoots { get; init; }

    [JsonPropertyName("continuation_bound")]
    public required string ContinuationBound { get; init; }

    [JsonPropertyName("startup_gates")]
    public required string StartupGates { get; init; }

    [JsonPropertyName("denial_semantics")]
    public required string DenialSemantics { get; init; }

    [JsonPropertyName("recovery")]
    public required string Recovery { get; init; }

    [JsonPropertyName("measurements")]
    public required IReadOnlyList<AgentLaunchRecipeMeasurement> Measurements { get; init; }

    [JsonPropertyName("post_start_interaction")]
    public string? PostStartInteraction { get; init; }
}

/// <summary>
/// The machine-readable result attached to a topology kind change. An absent
/// recipe is deliberately represented as a positive registry gap rather than
/// silently falling back to a different kind's flags.
/// </summary>
internal sealed record AgentLaunchRecipeResolution
{
    [JsonPropertyName("kind")]
    public required string Kind { get; init; }

    [JsonPropertyName("recorded")]
    public required bool Recorded { get; init; }

    [JsonPropertyName("status")]
    public required string Status { get; init; }

    [JsonPropertyName("summary")]
    public required string Summary { get; init; }

    [JsonPropertyName("recipe")]
    public AgentLaunchRecipe? Recipe { get; init; }
}

/// <summary>
/// G647: per-kind launch recipes measured by this host. Do not add a kind here
/// until its invocation, envelope, and recovery behavior have been measured;
/// an unknown kind is safer when surfaced as an explicit gap at switch time.
/// </summary>
internal static class AgentLaunchRecipeRegistry
{
    private const string Measured = "measured";
    private const string MeasuredPlatform = "macOS";

    private static readonly IReadOnlyDictionary<string, AgentLaunchRecipe> Recipes =
        new Dictionary<string, AgentLaunchRecipe>(StringComparer.OrdinalIgnoreCase)
        {
            ["copilot"] = new AgentLaunchRecipe
            {
                Kind = "copilot",
                Invocation =
                    "herdr agent start <logical-role> --kind copilot --pane <pane-id> -- --model claude-opus-5 "
                    + "--mode autopilot --allow-all-tools --add-dir <role-work-root> "
                    + "[--add-dir <host-routing-root>] --max-autopilot-continues 10",
                RoleDerivedRoots =
                    "Use one bounded --add-dir <role-work-root> for the role checkout/worktree; review also "
                    + "receives --add-dir <host-routing-root> for the canonical intent-cli notify report surface.",
                ContinuationBound = "--max-autopilot-continues 10 is explicit and operator-controlled.",
                StartupGates =
                    "Folder trust and autopilot-enable remain attended operator gates; choose Continue with limited "
                    + "permissions at the first task to preserve the declared roots.",
                DenialSemantics =
                    "An unattended out-of-scope action is silently auto-denied; READY must capture that denial and "
                    + "must not treat liveness or an allowed action alone as proof.",
                Recovery =
                    "If the bounded envelope is lost, stop and re-provision with the recorded roots; never widen "
                    + "the recipe through --yolo or --allow-all-paths.",
                PostStartInteraction =
                    "Copilot 1.0.78 presents Enable all permissions / Continue with limited permissions / Cancel; "
                    + "choose Continue with limited permissions (default_is_safe: false).",
                Measurements =
                [
                    new AgentLaunchRecipeMeasurement
                    {
                        Status = Measured,
                        Fact = "post-start permission interaction",
                        Observation = "The first task presents a permission choice whose default would discard the bounded envelope.",
                        Version = "Copilot 1.0.78",
                        Platform = MeasuredPlatform,
                    },
                ],
            },
            ["codex"] = new AgentLaunchRecipe
            {
                Kind = "codex",
                Invocation =
                    "herdr agent start <logical-role> --kind codex --pane <pane-id> -- --sandbox workspace-write "
                    + "--ask-for-approval never --add-dir <role-work-root> "
                    + "[--add-dir <host-routing-root>]",
                RoleDerivedRoots =
                    "Use one bounded --add-dir <role-work-root> for the role checkout/worktree; add the host "
                    + "routing root only for a role whose canonical report surface needs it.",
                ContinuationBound =
                    "No product-wide continuation bound is inferred; keep any role-specific bound explicit in the "
                    + "operator's measured launch record.",
                StartupGates =
                    "The operator supplies the mapped pane and role roots. --sandbox workspace-write and "
                    + "--ask-for-approval never are part of the measured bounded invocation; do not broaden them by "
                    + "guessing flags for another environment.",
                DenialSemantics =
                    "Measured envelope asymmetry: writes outside declared roots are denied, while reads outside "
                    + "declared roots are not denied. Treat the read asymmetry as an explicit security fact, not as "
                    + "a permission guarantee.",
                Recovery =
                    "Codex may self-update, print 'Please restart Codex', and exit to the pane's shell. Restart the "
                    + "agent in the recorded pane and re-run the READY/ping checks; classify this as a restart "
                    + "condition, not a wedge, and never widen the envelope to bypass it.",
                Measurements =
                [
                    new AgentLaunchRecipeMeasurement
                    {
                        Status = Measured,
                        Fact = "bounded invocation",
                        Observation =
                            "workspace-write sandbox, never-ask approval, and per-role --add-dir roots were used "
                            + "for the first Codex seat.",
                        Version = "Codex v0.144.1",
                        Platform = MeasuredPlatform,
                    },
                    new AgentLaunchRecipeMeasurement
                    {
                        Status = Measured,
                        Fact = "self-update exit",
                        Observation =
                            "The CLI printed 'Please restart Codex' and exited to a shell; restarting the agent "
                            + "restored the seat.",
                        Version = "Codex v0.144.1",
                        Platform = MeasuredPlatform,
                    },
                    new AgentLaunchRecipeMeasurement
                    {
                        Status = Measured,
                        Fact = "envelope asymmetry",
                        Observation =
                            "Writes outside declared roots were denied while reads outside declared roots were not.",
                        Version = "Codex v0.144.1",
                        Platform = MeasuredPlatform,
                    },
                ],
            },
        };

    public static IReadOnlyCollection<string> RecordedKinds => Recipes.Keys.ToArray();

    public static AgentLaunchRecipe? Find(string kind) =>
        Recipes.TryGetValue(kind, out var recipe) ? recipe : null;

    public static AgentLaunchRecipeResolution Describe(string kind)
    {
        var recipe = Find(kind);
        if (recipe is not null)
        {
            return new AgentLaunchRecipeResolution
            {
                Kind = kind,
                Recorded = true,
                Status = "recorded",
                Summary = $"A measured launch recipe for agent kind '{kind}' is recorded and surfaced with the change.",
                Recipe = recipe,
            };
        }

        return new AgentLaunchRecipeResolution
        {
            Kind = kind,
            Recorded = false,
            Status = "absent",
            Summary =
                $"No launch recipe is recorded for agent kind '{kind}'. This registry gap is explicit; do not "
                + "invent launch flags. Measure and record a recipe before unattended launch or ask the operator "
                + "to choose a kind with a recorded recipe.",
        };
    }
}
