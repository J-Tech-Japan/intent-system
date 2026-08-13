using System.Text.Json;
using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// Read-only inspection of the prompt-class vocabulary. The command exposes
/// the registry and shipped shell scopes without recording policy or deciding
/// an answer.
/// </summary>
internal static class PromptClassCommand
{
    private const string FormatJson = "json";
    private const string FormatMarkdown = "markdown";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static int ExecuteList(CliContext context, string[] args, TextWriter writer) =>
        Execute(context, args, writer, "list");

    public static int ExecuteDescribe(CliContext context, string[] args, TextWriter writer) =>
        Execute(context, args, writer, "describe");

    private static int Execute(CliContext context, string[] args, TextWriter writer, string operation)
    {
        _ = context;
        if (args is ["--help"])
        {
            writer.WriteLine($"Usage: intent-cli prompt-class {operation} ...");
            return 0;
        }

        var format = FormatMarkdown;
        string? agentKind = null;
        string? promptClass = null;
        var positional = new List<string>();
        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--format":
                    if (index + 1 >= args.Length)
                    {
                        writer.WriteLine("invalid-prompt-class: --format requires a value.");
                        return 1;
                    }
                    format = args[++index];
                    break;
                case "--agent-kind":
                    if (index + 1 >= args.Length)
                    {
                        writer.WriteLine("invalid-prompt-class: --agent-kind requires a value.");
                        return 1;
                    }
                    agentKind = args[++index];
                    break;
                case "--prompt-class":
                    if (index + 1 >= args.Length)
                    {
                        writer.WriteLine("invalid-prompt-class: --prompt-class requires a value.");
                        return 1;
                    }
                    promptClass = args[++index];
                    break;
                default:
                    positional.Add(args[index]);
                    break;
            }
        }

        if (format is not (FormatMarkdown or FormatJson))
        {
            writer.WriteLine("invalid-prompt-class: --format must be markdown or json.");
            return 1;
        }

        if (operation == "list")
        {
            if (positional.Count > 0 || promptClass is not null)
            {
                writer.WriteLine("invalid-prompt-class: list accepts only --agent-kind and --format.");
                return 1;
            }

            if (agentKind is not null && AgentLaunchRecipeRegistry.Find(agentKind) is null)
            {
                writer.WriteLine($"unknown-agent-kind: no recorded prompt classes exist for '{agentKind}'.");
                return 1;
            }

            var classes = AgentLaunchRecipeRegistry.RecordedKinds
                .Where(kind => agentKind is null || string.Equals(kind, agentKind, StringComparison.OrdinalIgnoreCase))
                .SelectMany(kind => AgentLaunchRecipeRegistry.Find(kind)!.PromptClasses.Select(recipe =>
                    new PromptClassDescriptor
                    {
                        AgentKind = kind,
                        PromptClass = recipe.PromptClass,
                        PayloadKind = recipe.PayloadKind,
                        ScopedPolicyRequired = recipe.ScopedPolicyRequired,
                        AnswerableBy = recipe.AnswerableBy,
                        RiskTags = recipe.RiskTags,
                        ExactAnswerScope = recipe.ExactAnswerScope,
                        LiteralTextFragments = recipe.LiteralTextFragments,
                        Provenance = recipe.Provenance,
                        Scopes = recipe.ScopedPolicyRequired
                            ? ShellCommandPolicyRegistry.Scopes.Select(scope => scope.Name).ToArray()
                            : [],
                    }))
                .OrderBy(value => value.AgentKind, StringComparer.Ordinal)
                .ThenBy(value => value.PromptClass, StringComparer.Ordinal)
                .ToArray();

            if (format == FormatJson)
            {
                writer.WriteLine(JsonSerializer.Serialize(new { command = "prompt-class list", classes }, JsonOptions));
            }
            else
            {
                writer.WriteLine("# Prompt classes");
                writer.WriteLine();
                writer.WriteLine("| agent kind | prompt class | payload | answerable by | risk floor | scoped policy | scopes |");
                writer.WriteLine("| --- | --- | --- | --- | --- | --- | --- |");
                foreach (var descriptor in classes)
                {
                    writer.WriteLine($"| `{descriptor.AgentKind}` | `{descriptor.PromptClass}` | `{descriptor.PayloadKind}` | `{descriptor.AnswerableBy}` | {string.Join(", ", descriptor.RiskTags.Select(tag => $"`{tag}`"))} | `{descriptor.ScopedPolicyRequired}` | {string.Join(", ", descriptor.Scopes.Select(scope => $"`{scope}`"))} |");
                }
            }
            return 0;
        }

        if (positional.Count == 1 && (agentKind is null || promptClass is null))
        {
            var pair = positional[0].Split(':', 2, StringSplitOptions.None);
            if (pair.Length == 2)
            {
                agentKind ??= pair[0];
                promptClass ??= pair[1];
            }
        }

        if (positional.Count > 1 || string.IsNullOrWhiteSpace(agentKind) || string.IsNullOrWhiteSpace(promptClass))
        {
            writer.WriteLine("invalid-prompt-class: describe requires <agent-kind>:<prompt-class> or both --agent-kind and --prompt-class.");
            return 1;
        }

        if (!AgentLaunchRecipeRegistry.TryFindPromptClass(agentKind, promptClass, out var recipe))
        {
            writer.WriteLine($"unknown-prompt-class: no recorded class exists for '{agentKind}:{promptClass}'.");
            return 1;
        }

        var result = new PromptClassDescriptor
        {
            AgentKind = agentKind,
            PromptClass = recipe!.PromptClass,
            PayloadKind = recipe.PayloadKind,
            ScopedPolicyRequired = recipe.ScopedPolicyRequired,
            AnswerableBy = recipe.AnswerableBy,
            RiskTags = recipe.RiskTags,
            ExactAnswerScope = recipe.ExactAnswerScope,
            LiteralTextFragments = recipe.LiteralTextFragments,
            Provenance = recipe.Provenance,
            Scopes = recipe.ScopedPolicyRequired
                ? ShellCommandPolicyRegistry.Scopes.Select(scope => scope.Name).ToArray()
                : [],
        };
        if (format == FormatJson)
        {
            writer.WriteLine(JsonSerializer.Serialize(result, JsonOptions));
        }
        else
        {
            writer.WriteLine($"# {result.AgentKind}:{result.PromptClass}");
            writer.WriteLine();
            writer.WriteLine($"- payload kind: `{result.PayloadKind}`");
            writer.WriteLine($"- answerable by: `{result.AnswerableBy}`");
            writer.WriteLine($"- risk tags: {string.Join(", ", result.RiskTags.Select(tag => $"`{tag}`"))}");
            writer.WriteLine($"- scoped policy required: `{result.ScopedPolicyRequired}`");
            writer.WriteLine($"- literal fragments: {string.Join(", ", result.LiteralTextFragments.Select(fragment => $"`{fragment}`"))}");
            writer.WriteLine($"- scopes: {string.Join(", ", result.Scopes.Select(scope => $"`{scope}`"))}");
            writer.WriteLine($"- answer scope: {result.ExactAnswerScope}");
            writer.WriteLine($"- provenance: {result.Provenance}");
        }
        return 0;
    }

    private sealed record PromptClassDescriptor
    {
        [JsonPropertyName("agent_kind")] public required string AgentKind { get; init; }
        [JsonPropertyName("prompt_class")] public required string PromptClass { get; init; }
        [JsonPropertyName("payload_kind")] public required string PayloadKind { get; init; }
        [JsonPropertyName("scoped_policy_required")] public bool ScopedPolicyRequired { get; init; }
        [JsonPropertyName("answerable_by")] public required string AnswerableBy { get; init; }
        [JsonPropertyName("risk_tags")] public required IReadOnlyList<string> RiskTags { get; init; }
        [JsonPropertyName("literal_text_fragments")] public required IReadOnlyList<string> LiteralTextFragments { get; init; }
        [JsonPropertyName("exact_answer_scope")] public required string ExactAnswerScope { get; init; }
        [JsonPropertyName("provenance")] public required string Provenance { get; init; }
        [JsonPropertyName("scopes")] public required IReadOnlyList<string> Scopes { get; init; }
    }
}
