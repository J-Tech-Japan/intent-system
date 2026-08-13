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

    [JsonPropertyName("host")]
    public required string Host { get; init; }

    [JsonPropertyName("date")]
    public required string Date { get; init; }

    [JsonPropertyName("version")]
    public required string Version { get; init; }

    [JsonPropertyName("platform")]
    public required string Platform { get; init; }
}

/// <summary>
/// G683: one exact, reviewable dialog class attached to the agent kind recipe
/// that produced it. Matching is literal-only; the answer is a bounded herdr
/// key sequence and is never inferred from terminal text.
/// </summary>
internal sealed record AgentPromptClassRecipe
{
    [JsonPropertyName("prompt_class")]
    public required string PromptClass { get; init; }

    [JsonPropertyName("literal_text_fragments")]
    public required IReadOnlyList<string> LiteralTextFragments { get; init; }

    [JsonPropertyName("exact_answer_scope")]
    public required string ExactAnswerScope { get; init; }

    [JsonPropertyName("answer_keys")]
    public required IReadOnlyList<string> AnswerKeys { get; init; }

    [JsonPropertyName("provenance")]
    public required string Provenance { get; init; }
}

internal sealed record AgentPromptClassObservation
{
    public required string AgentKind { get; init; }
    public required string PromptClass { get; init; }
    public required string ObservedText { get; init; }
    public AgentPromptClassRecipe? Recipe { get; init; }
    public bool Known => Recipe is not null;
}

/// <summary>
/// The operator-facing recipe for one agent kind. This is evidence, not a
/// launcher: intent-cli never starts an agent. G683 consumes only the exact
/// prompt classes and bounded answers reviewed in this same recipe.
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

    [JsonPropertyName("inline_payload_warning_profile")]
    public required string InlinePayloadWarningProfile { get; init; }

    [JsonPropertyName("delivery_method")]
    public required string DeliveryMethod { get; init; }

    [JsonPropertyName("post_start_interaction")]
    public required OrchestratorPostStartInteraction PostStartInteraction { get; init; }

    [JsonPropertyName("prompt_classes")]
    public required IReadOnlyList<AgentPromptClassRecipe> PromptClasses { get; init; }

    [JsonPropertyName("startup_gates")]
    public required string StartupGates { get; init; }

    [JsonPropertyName("prohibited_blanket")]
    public required string ProhibitedBlanket { get; init; }

    [JsonPropertyName("denial_semantics")]
    public required string DenialSemantics { get; init; }

    [JsonPropertyName("recovery")]
    public required string Recovery { get; init; }

    [JsonPropertyName("measurements")]
    public required IReadOnlyList<AgentLaunchRecipeMeasurement> Measurements { get; init; }

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
    private const string MeasuredHost = "MyIntentHost";
    private const string MeasuredDate = "2026-08-07";
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
                    "Use one bounded `--add-dir <role-work-root>` for the role's checkout/worktree. A review "
                    + "role additionally receives `--add-dir <host-routing-root>` because `intent-cli notify report` "
                    + "is its canonical reporting surface. Do not add unrelated developer-machine roots. Before "
                    + "delegation, the orchestrator compares workspace prerequisites with this recorded write "
                    + "envelope and prepares anything outside it under orchestrator authority (G655).",
                ContinuationBound =
                    "Keep `--max-autopilot-continues 10` explicit; changing the bound is an operator decision "
                    + "recorded with the recipe, not an agent default.",
                InlinePayloadWarningProfile =
                    "Profile `copilot-autopilot-observed-paste-risk` declares `inline_payload_warning_chars: 4096`. "
                    + "It is ADVISORY only: a payload above it is likely pasted rather than typed, while a payload "
                    + "below it is not promised safe because the real limit is terminal- and agent-dependent. "
                    + "Reference-first dispatch keeps repeated review substance in committed `review-context.md`, but a "
                    + "minimal canonical `notify delegate` envelope still measures 842 characters over 14 lines and can "
                    + "itself be pasted: it reduces duplication, not a paste-sensitive wedge. G619 owns the transport-layer remedy.",
                DeliveryMethod =
                    "Declare `delivery_method: file-backed` for a paste-sensitive herdr seat. `notify` writes the "
                    + "unchanged envelope to durable host `.intent-cli/tasks/<domain>/<team>/<task-id>-<nonce>.md` before "
                    + "sending one line, `Read task envelope: <path>`. Declare `inline` to opt in explicitly; an absent "
                    + "declaration preserves existing inline delivery.",
                PostStartInteraction = new OrchestratorPostStartInteraction
                {
                    Status = "measured",
                    Observed = true,
                    Prompt =
                        "At the first task, Copilot 1.0.78 presents `1. Enable all permissions (recommended)` / "
                        + "`2. Continue with limited permissions` / `3. Cancel`, with the cursor on option 1.",
                    Answer =
                        "Choose `Continue with limited permissions` to preserve the bounded `--add-dir` envelope; "
                        + "the default `Enable all permissions` answer is unsafe.",
                    DefaultIsSafe = false,
                    AbsenceReason = null,
                },
                PromptClasses =
                [
                    new AgentPromptClassRecipe
                    {
                        PromptClass = "launch-limited-permissions",
                        LiteralTextFragments =
                        [
                            "Enable all permissions (recommended)",
                            "Continue with limited permissions",
                            "Cancel",
                        ],
                        ExactAnswerScope =
                            "Select only 'Continue with limited permissions' in the measured Copilot first-task permission dialog.",
                        AnswerKeys = ["2", "enter"],
                        Provenance =
                            "G636 measured Copilot 1.0.78 on MyIntentHost/macOS on 2026-08-07; this registry entry consumes that recorded post-start interaction.",
                    },
                ],
                StartupGates =
                    "Folder trust and autopilot-enable are operator provisioning gates; neither is bypassed by "
                    + "launch flags. The autopilot-enable dialog appears at the FIRST TASK even when `--mode autopilot` "
                    + "was passed at launch. With `--allow-all-tools` plus bounded roots, choose `Continue with limited "
                    + "permissions`; NEVER choose `Enable all permissions`, which discards the boundary.",
                ProhibitedBlanket =
                    "For unattended developer-machine seats, `--yolo` and `--allow-all-paths` are PROHIBITED. "
                    + "They discard the role-derived boundary; bounded `--add-dir` roots are the required alternative.",
                DenialSemantics =
                    "An unattended out-of-scope action is silently auto-denied; READY must capture that denial and "
                    + "must not treat liveness or an allowed action alone as proof.",
                Recovery =
                    "If the bounded envelope is lost, stop and re-provision with the recorded roots; never widen "
                    + "the recipe through --yolo or --allow-all-paths.",
                Measurements =
                [
                    new AgentLaunchRecipeMeasurement
                    {
                        Status = Measured,
                        Fact = "post-start permission interaction",
                        Observation = "The first task presents a permission choice whose default would discard the bounded envelope.",
                        Host = MeasuredHost,
                        Date = MeasuredDate,
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
                    + "routing root only for a role whose canonical report surface needs it. Before delegation, the "
                    + "orchestrator compares workspace prerequisites with this recorded write envelope and prepares "
                    + "anything outside it under orchestrator authority (G655).",
                ContinuationBound =
                    "No product-wide continuation bound is inferred; keep any role-specific bound explicit in the "
                    + "operator's measured launch record.",
                InlinePayloadWarningProfile =
                    "Codex inline-payload risk was not measured in G647; do not infer a safe threshold. Use the "
                    + "reference-first/file-backed task-envelope guidance when the operator records it.",
                DeliveryMethod =
                    "Declare `delivery_method: file-backed` for a paste-sensitive herdr seat. `notify` writes the "
                    + "unchanged envelope before sending one line; an absent declaration preserves existing inline delivery.",
                PostStartInteraction = new OrchestratorPostStartInteraction
                {
                    Status = "unmeasured",
                    Observed = false,
                    Prompt = null,
                    Answer = null,
                    DefaultIsSafe = null,
                    AbsenceReason =
                        "No Codex post-start interaction was observed on MyIntentHost on 2026-08-07; "
                        + "do not infer a prompt, answer, or default safety from the measured launch facts.",
                },
                PromptClasses =
                [
                    new AgentPromptClassRecipe
                    {
                        PromptClass = "github-comment-post",
                        LiteralTextFragments = ["Allow GitHub to add a comment to a pull request?"],
                        ExactAnswerScope =
                            "Select the recorded always-allow choice only for GitHub pull-request comment posting.",
                        AnswerKeys = ["2", "enter"],
                        Provenance =
                            "Operator-filed #1469 measured this exact Codex approval text wedging the review seat three times on 2026-08-11; the operator had explicitly authorized its always-allow choice.",
                    },
                    new AgentPromptClassRecipe
                    {
                        PromptClass = "launch-hook-trust",
                        LiteralTextFragments = ["Do you trust the authors of the files in this folder?"],
                        ExactAnswerScope =
                            "Accept only the already-recorded next-launch hook trust dialog for a hook installed by this team.",
                        AnswerKeys = ["enter"],
                        Provenance =
                            "G582/G636 recorded the next-launch hook-trust screen as a launch gate; this literal class makes that existing recipe fact reviewable rather than guessed.",
                    },
                ],
                StartupGates =
                    "The operator supplies the mapped pane and role roots. --sandbox workspace-write and "
                    + "--ask-for-approval never are part of the measured bounded invocation; do not broaden them by "
                    + "guessing flags for another environment.",
                ProhibitedBlanket =
                    "Do not add unmeasured blanket permissions or broaden the role-derived roots; in particular, "
                    + "do not replace the bounded invocation with a guessed --yolo or --allow-all-paths equivalent.",
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
                        Host = MeasuredHost,
                        Date = MeasuredDate,
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
                        Host = MeasuredHost,
                        Date = MeasuredDate,
                        Version = "Codex v0.144.1",
                        Platform = MeasuredPlatform,
                    },
                    new AgentLaunchRecipeMeasurement
                    {
                        Status = Measured,
                        Fact = "envelope asymmetry",
                        Observation =
                            "Writes outside declared roots were denied while reads outside declared roots were not.",
                        Host = MeasuredHost,
                        Date = MeasuredDate,
                        Version = "Codex v0.144.1",
                        Platform = MeasuredPlatform,
                    },
                ],
            },
        };

    public static IReadOnlyCollection<string> RecordedKinds => Recipes.Keys.ToArray();

    public static AgentLaunchRecipe? Find(string kind) =>
        Recipes.TryGetValue(kind, out var recipe) ? recipe : null;

    public static IReadOnlyList<string> KnownPairs => Recipes.Values
        .SelectMany(recipe => recipe.PromptClasses.Select(prompt => $"{recipe.Kind}:{prompt.PromptClass}"))
        .OrderBy(value => value, StringComparer.Ordinal)
        .ToArray();

    public static bool HasPromptClassProducer(string kind) =>
        Find(kind)?.PromptClasses.Count > 0;

    public static bool TryFindPromptClass(
        string kind,
        string promptClass,
        out AgentPromptClassRecipe? prompt)
    {
        prompt = Find(kind)?.PromptClasses.FirstOrDefault(candidate =>
            string.Equals(candidate.PromptClass, promptClass, StringComparison.OrdinalIgnoreCase));
        return prompt is not null;
    }

    public static AgentPromptClassObservation Classify(string kind, string observedText)
    {
        var matches = Find(kind)?.PromptClasses
            .Where(candidate => candidate.LiteralTextFragments.Count > 0
                && candidate.LiteralTextFragments.All(fragment =>
                    observedText.Contains(fragment, StringComparison.Ordinal)))
            .ToArray() ?? [];
        var match = matches.Length == 1 ? matches[0] : null;
        return new AgentPromptClassObservation
        {
            AgentKind = kind,
            PromptClass = match?.PromptClass ?? "unknown",
            ObservedText = observedText,
            Recipe = match,
        };
    }

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
