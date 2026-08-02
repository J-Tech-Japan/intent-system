using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G582: the mode-independent handover checklist. It is deliberately one
/// model rendered into both markdown and JSON so <c>session-layer show</c>
/// always points at a section present in the current mode.
/// </summary>
internal static class SessionLayerSwitchChecklist
{
    public const string Heading = "## Session-layer switch checklist";
    public const string JsonProperty = "session_layer_switch_checklist";

    public static OrchestratorSessionLayerSwitchChecklist Create() => new()
    {
        Exclusivity =
            "One team runs exactly one session-layer mode at a time. Simultaneous agmsg and herdr-only delivery "
            + "is a mixed-delivery CONTRACT VIOLATION.",
        AgmsgToHerdrOnly =
        [
            "Drain or explicitly park every in-flight delegation; record artifacts and unresolved blockers.",
            "Gracefully drop outgoing agmsg roles and stop their watchers/bridges. Turn off or remove the outgoing "
            + "transport's per-project agmsg hook configuration AND delivery mode, then verify no agmsg receiver can "
            + "still deliver for this team. This teardown is operationally required: leftover hook configuration "
            + "caused the observed next-launch hook-trust screen to block the next Codex launch.",
            "Provision the herdr workspace, role cwds, typed agents, and logical-role-to-pane mapping from the "
            + "herdr-only operating procedures; validate approvals and the events path.",
            "Pass the self-contained G556 settle-and-re-check READY gate for every incoming role and verify bounded "
            + "dispatch/marker/artifact detection.",
            "As the FINAL canonical step, run `intent-cli session-layer set --domain <domain> --team <team> "
            + "--mode herdr-only --write`.",
        ],
        HerdrOnlyToAgmsg =
        [
            "Drain or explicitly park every in-flight delegation; append any final design-relevant event and record "
            + "artifacts/blockers.",
            "Gracefully stop agents or retain/close the outgoing herdr workspace according to the operator's "
            + "workspace policy; ensure it cannot keep delivering tasks for this team.",
            "Provision agmsg roles, transport configuration, and any approved watcher/bridge; do not reuse a stale "
            + "role hold. Keep `events.jsonl` as the mode-independent design boundary, not as an agmsg bus.",
            "Pass the applicable self-contained G556 settle-and-re-check READY gate and end-to-end delivery ack for "
            + "every incoming role.",
            "As the FINAL canonical step, run `intent-cli session-layer set --domain <domain> --team <team> "
            + "--mode agmsg --write`.",
        ],
    };

    public static void WriteMarkdown(TextWriter writer, OrchestratorSessionLayerSwitchChecklist checklist)
    {
        writer.WriteLine(Heading);
        writer.WriteLine();
        writer.WriteLine(checklist.Exclusivity);
        writer.WriteLine();
        writer.WriteLine("**agmsg → herdr-only**");
        writer.WriteLine();
        WriteSteps(writer, checklist.AgmsgToHerdrOnly);
        writer.WriteLine("**herdr-only → agmsg**");
        writer.WriteLine();
        WriteSteps(writer, checklist.HerdrOnlyToAgmsg);
    }

    private static void WriteSteps(TextWriter writer, IReadOnlyList<string> steps)
    {
        for (var index = 0; index < steps.Count; index++)
        {
            writer.WriteLine($"{index + 1}. {steps[index]}");
        }
        writer.WriteLine();
    }
}

internal sealed record OrchestratorSessionLayerSwitchChecklist
{
    [JsonPropertyName("exclusivity")]
    public required string Exclusivity { get; init; }

    [JsonPropertyName("agmsg_to_herdr_only")]
    public required IReadOnlyList<string> AgmsgToHerdrOnly { get; init; }

    [JsonPropertyName("herdr_only_to_agmsg")]
    public required IReadOnlyList<string> HerdrOnlyToAgmsg { get; init; }
}
