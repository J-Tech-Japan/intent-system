using System.Text.Json;
using System.Text.Json.Nodes;

namespace IntentSystem.Cli.Commands;

internal static class CompletionChannelGuidance
{
    internal const string Summary =
        "G811 completion channel: a completed Herdr task is written to the sender outbox and the recorded external Steward reader. "
        + "Steward first runs the bounded task-scoped `intent-cli notify collect --domain <domain> --team <team> --role steward --since <cursor> --write --format json` "
        + "and then records `intent-cli notify acknowledge --domain <domain> --team <team> --from steward --task-id <task-id> --result-nonce <nonce> --artifact <artifact> --receipt-cursor <cursor> --write --format json`. "
        + "The acknowledgement requires the exact completion identity and consumption receipt; event append is not consumption. "
        + "The originating resident consumes its bound acknowledgement on its next canonical turn with `intent-cli notify collect --domain <domain> --team <team> --task-id <task-id> --role <resident-role> --write --format json`; no pane/model wake is sent. "
        + "Orchestration runs `intent-cli notify reconcile --domain <domain> --team <team> --task-id <task-id> --routing-root <host-root> --report-root <role-work-root> --write --format json` and reads completion-channel health from status/supervise."
        + " Available-but-idle is consumption-pending, not consumed; all operations are identity-bound and replay-safe.";

    internal static readonly CompletionChannelGuideBlock Block = new()
    {
        Summary = Summary,
        StewardCollectCommand = "intent-cli notify collect --domain <domain> --team <team> --role steward --since <cursor> --write --format json",
        StewardAcknowledgeCommand = "intent-cli notify acknowledge --domain <domain> --team <team> --from steward --task-id <task-id> --result-nonce <nonce> --artifact <artifact> --receipt-cursor <cursor> --write --format json",
        ResidentCollectCommand = "intent-cli notify collect --domain <domain> --team <team> --task-id <task-id> --role <resident-role> --write --format json",
        ReconcileCommand = "intent-cli notify reconcile --domain <domain> --team <team> --task-id <task-id> --routing-root <host-root> --report-root <role-work-root> --write --format json",
        HealthCommand = "intent-cli notify status --domain <domain> --team <team> --task-id <task-id> --format json",
        BoundRule = "B=ceil(F+J+P)+1 from measured complete sweeps; absent/corrupt/stale evidence is unknown or degraded, never healthy.",
        Boundary = "No provider, pane, focus, key, resend, or immediate-wake operation is performed by acknowledgement publication or read-only status.",
    };

    internal static string AppendMarkdown(string markdown) =>
        markdown + Environment.NewLine + Environment.NewLine + $"- **G811 completion channel:** {Summary}" + Environment.NewLine;

    internal static string AppendJson(string json)
    {
        var node = JsonNode.Parse(json);
        if (node is JsonObject obj)
        {
            obj["completion_channel"] = JsonSerializer.SerializeToNode(Block);
        }
        else if (node is JsonArray array)
        {
            foreach (var item in array.OfType<JsonObject>()) item["completion_channel"] = JsonSerializer.SerializeToNode(Block);
        }
        return node?.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) ?? json;
    }
}

internal sealed record CompletionChannelGuideBlock
{
    public required string Summary { get; init; }
    public required string StewardCollectCommand { get; init; }
    public required string StewardAcknowledgeCommand { get; init; }
    public required string ResidentCollectCommand { get; init; }
    public required string ReconcileCommand { get; init; }
    public required string HealthCommand { get; init; }
    public required string BoundRule { get; init; }
    public required string Boundary { get; init; }
}
