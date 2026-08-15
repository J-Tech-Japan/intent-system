using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G701: one structured dialog-answering rule rendered by setup and the
/// design-thread guide. It describes authority; it never answers a dialog.
/// </summary>
internal static class DialogAnsweringRuleGuidance
{
    public const string RuleVersion = "dialog-answering/v1";

    public static DialogAnsweringRuleGuide Create() => new()
    {
        RuleVersion = RuleVersion,
        Tiers =
        [
            new DialogAnsweringTier
            {
                Id = "self-provisioned-gate",
                Name = "self-provisioned gates",
                DecisionActor = "provisioner",
                Executor = "provisioner",
                Rule = "A gate over state the provisioner itself created is the provisioner's duty to answer as the final step of provisioning.",
                Grounds = "The provisioner's own creation record and the exact gate it caused.",
                OnMismatch = "Do not hand the gate to another role merely because it appeared during setup.",
            },
            new DialogAnsweringTier
            {
                Id = "human-approved-execution",
                Name = "human-approved execution",
                DecisionActor = "human operator",
                Executor = "design through the session layer",
                Rule = "An action the human already approved in conversation may be answered mechanically only after the dialog text exactly matches that approved action.",
                Grounds = "The recorded conversation approval is the grounds; the human remains the decision actor.",
                OnMismatch = "A per-action approval never generalizes to a class, and design never answers a different action.",
            },
            new DialogAnsweringTier
            {
                Id = "unapproved-or-uncertain",
                Name = "unapproved or uncertain dialogs",
                DecisionActor = "human operator",
                Executor = "none until the human decides",
                Rule = "An unapproved, unknown-origin, uncertain, or approval-mismatching dialog escalates through the design thread to the human.",
                Grounds = "State the grounds: what was observed, which approval is absent or mismatched, and the minimal decision needed.",
                OnMismatch = "Do not answer, generalize, infer provenance, or send keys while the grounds are unresolved.",
            },
        ],
        G690Distinction = "G690 solo adjudication is narrower: its non-overridable hard risk floor bounds what design may decide alone. It does not block execution through the session layer of a human decision already recorded in conversation.",
        AuthorityBoundary = "The guide is read-only. The session layer is the mechanical executor only after the exact dialog/action match and the applicable authority boundary; no provider launch, terminal mutation, or direct key relay is added here.",
    };

    public static JsonObject CreateJson() =>
        (JsonSerializer.SerializeToNode(Create(), JsonOptions) as JsonObject)
        ?? throw new InvalidOperationException("The dialog-answering rule did not serialize as an object.");

    public static string RenderMarkdown()
    {
        var rule = Create();
        using var writer = new StringWriter();
        writer.WriteLine("## Three-tier dialog-answering rule (G701)");
        writer.WriteLine();
        writer.WriteLine($"- version: `{rule.RuleVersion}`");
        writer.WriteLine();
        for (var index = 0; index < rule.Tiers.Count; index++)
        {
            var tier = rule.Tiers[index];
            writer.WriteLine($"### Tier {index + 1}: {tier.Name}");
            writer.WriteLine();
            writer.WriteLine($"- decision actor: **{tier.DecisionActor}**");
            writer.WriteLine($"- mechanical executor: **{tier.Executor}**");
            writer.WriteLine($"- rule: {tier.Rule}");
            writer.WriteLine($"- grounds: {tier.Grounds}");
            writer.WriteLine($"- boundary: {tier.OnMismatch}");
            writer.WriteLine();
        }

        writer.WriteLine($"- **G690 distinction:** {rule.G690Distinction}");
        writer.WriteLine($"- **authority boundary:** {rule.AuthorityBoundary}");
        return writer.ToString().TrimEnd();
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
    };
}

internal sealed record DialogAnsweringRuleGuide
{
    [JsonPropertyName("rule_version")]
    public required string RuleVersion { get; init; }

    [JsonPropertyName("tiers")]
    public required IReadOnlyList<DialogAnsweringTier> Tiers { get; init; }

    [JsonPropertyName("g690_distinction")]
    public required string G690Distinction { get; init; }

    [JsonPropertyName("authority_boundary")]
    public required string AuthorityBoundary { get; init; }
}

internal sealed record DialogAnsweringTier
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("decision_actor")]
    public required string DecisionActor { get; init; }

    [JsonPropertyName("executor")]
    public required string Executor { get; init; }

    [JsonPropertyName("rule")]
    public required string Rule { get; init; }

    [JsonPropertyName("grounds")]
    public required string Grounds { get; init; }

    [JsonPropertyName("on_mismatch")]
    public required string OnMismatch { get; init; }
}
