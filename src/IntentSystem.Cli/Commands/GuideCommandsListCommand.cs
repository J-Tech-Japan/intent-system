using System.Text.Json;
using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G257: Read-only <c>intent-cli guide commands list</c>. Returns each
/// top-level command group with a lifecycle classification (primary,
/// support, advanced, experimental), short purpose, mutability hint
/// (read-only / write), and recommended caller, so AI agents can pick
/// the right surface without misreading legacy groups (e.g. <c>run</c>)
/// as the primary product path. Never mutates state. Never launches an
/// AI provider.
/// </summary>
internal static class GuideCommandsListCommand
{
    private const string FormatJson = "json";
    private const string FormatMarkdown = "markdown";

    private const string ClassificationPrimary = "primary";
    private const string ClassificationSupport = "support";
    private const string ClassificationAdvanced = "advanced";
    private const string ClassificationExperimental = "experimental";

    private const string MutabilityReadOnly = "read-only";
    private const string MutabilityMixed = "mixed";

    private const string CallerChatAgent = "chat-agent";
    private const string CallerImplementationLoop = "implementation-loop";
    private const string CallerHostLoop = "host-loop";
    private const string CallerAdvancedRuntime = "advanced-runtime";
    private const string CallerOperator = "operator";

    private const string UsageLine =
        "Usage: intent-cli guide commands list [--format markdown|json]";

    internal static readonly IReadOnlyList<CommandGroupEntry> Groups = new[]
    {
        new CommandGroupEntry
        {
            Name = "guide",
            Classification = ClassificationPrimary,
            Mutability = MutabilityReadOnly,
            RecommendedCaller = CallerChatAgent,
            Purpose = "Operator-facing guidance: collaboration model, rules-by-topic, workflow suggestion, one-shot/automation/review prompts."
        },
        new CommandGroupEntry
        {
            Name = "intent",
            Classification = ClassificationPrimary,
            Mutability = MutabilityMixed,
            RecommendedCaller = CallerChatAgent,
            Purpose = "Read-only domain status / search / explain / next-slice planning, plus draft-from-interview (write requires --write)."
        },
        new CommandGroupEntry
        {
            Name = "interview",
            Classification = ClassificationPrimary,
            Mutability = MutabilityMixed,
            RecommendedCaller = CallerChatAgent,
            Purpose = "Durable per-domain Q/A artifact: next-question/record-answer/compile (older start/answer/resume retained)."
        },
        new CommandGroupEntry
        {
            Name = "packet",
            Classification = ClassificationPrimary,
            Mutability = MutabilityMixed,
            RecommendedCaller = CallerChatAgent,
            Purpose = "Scaffold the canonical packet directory (packet.yaml / implementation.md / review-context.md / github-body.md); read-only without --write."
        },
        new CommandGroupEntry
        {
            Name = "worker",
            Classification = ClassificationPrimary,
            Mutability = MutabilityMixed,
            RecommendedCaller = CallerImplementationLoop,
            Purpose = "Child implementation loop selector: next-action / claim / complete / result-summary."
        },
        new CommandGroupEntry
        {
            Name = "automation",
            Classification = ClassificationSupport,
            Mutability = MutabilityMixed,
            RecommendedCaller = CallerHostLoop,
            Purpose = "Host-side label transitions and capability JSON: summary / doctor / host-review-preflight / issue-publish / pr-transition / check / complete / clarification-stop."
        },
        new CommandGroupEntry
        {
            Name = "metadata",
            Classification = ClassificationSupport,
            Mutability = MutabilityMixed,
            RecommendedCaller = CallerHostLoop,
            Purpose = "Read-only metadata validate plus the bounded controlled writer (`metadata update --mode completed-closeout`)."
        },
        new CommandGroupEntry
        {
            Name = "review",
            Classification = ClassificationSupport,
            Mutability = MutabilityMixed,
            RecommendedCaller = CallerHostLoop,
            Purpose = "Review-side surfaces: closeout-plan (read-only), run / comment / accept (older review surface)."
        },
        new CommandGroupEntry
        {
            Name = "closeout",
            Classification = ClassificationSupport,
            Mutability = MutabilityMixed,
            RecommendedCaller = CallerHostLoop,
            Purpose = "PR closeout: queue completion + runs append + continuation hint (`closeout pr --pr <n> --write`)."
        },
        new CommandGroupEntry
        {
            Name = "issue",
            Classification = ClassificationSupport,
            Mutability = MutabilityMixed,
            RecommendedCaller = CallerOperator,
            Purpose = "GitHub issue surfaces incl. publish-flow (validate packet → create issue → durable-state next-step), draft / create / status / validate-body / prepare / publish-reviewed / plan-candidate."
        },
        new CommandGroupEntry
        {
            Name = "queue",
            Classification = ClassificationSupport,
            Mutability = MutabilityMixed,
            RecommendedCaller = CallerOperator,
            Purpose = "Queue-state inspection and bounded transitions: list / show / next / enqueue / dispatch / transition."
        },
        new CommandGroupEntry
        {
            Name = "status",
            Classification = ClassificationSupport,
            Mutability = MutabilityReadOnly,
            RecommendedCaller = CallerChatAgent,
            Purpose = "Read-only status brief used by AI tasking threads (G179); see `intent status` for the canonical domain summary."
        },
        new CommandGroupEntry
        {
            Name = "context",
            Classification = ClassificationSupport,
            Mutability = MutabilityReadOnly,
            RecommendedCaller = CallerChatAgent,
            Purpose = "Read-only context surfaces consumed by AI tasking threads."
        },
        new CommandGroupEntry
        {
            Name = "clarify",
            Classification = ClassificationSupport,
            Mutability = MutabilityMixed,
            RecommendedCaller = CallerOperator,
            Purpose = "Clarification artifact CRUD: open / list / answer / draft / record. Operator-driven; not the chat-agent's first call."
        },
        new CommandGroupEntry
        {
            Name = "next-slice",
            Classification = ClassificationSupport,
            Mutability = MutabilityReadOnly,
            RecommendedCaller = CallerOperator,
            Purpose = "Older next-slice planning surface; prefer `intent next-slice --dry-run` for collaborative shaping."
        },
        new CommandGroupEntry
        {
            Name = "tasking",
            Classification = ClassificationExperimental,
            Mutability = MutabilityMixed,
            RecommendedCaller = CallerOperator,
            Purpose = "Cross-thread tasking handoff bundles and task-packet helpers; experimental compared to the chat-first flow."
        },
        new CommandGroupEntry
        {
            Name = "intake",
            Classification = ClassificationExperimental,
            Mutability = MutabilityMixed,
            RecommendedCaller = CallerOperator,
            Purpose = "Older concept-intake surface (init / concept / interview / compile / foldin / patch / apply / execution / advance / activate / issue / enqueue / autostart / launch / start). Prefer `guide collaborate` + `interview record-answer` for new flows."
        },
        new CommandGroupEntry
        {
            Name = "bug",
            Classification = ClassificationExperimental,
            Mutability = MutabilityMixed,
            RecommendedCaller = CallerOperator,
            Purpose = "Bug-intent CRUD (report / triage / start / repair). Not the primary collaboration path."
        },
        new CommandGroupEntry
        {
            Name = "projection",
            Classification = ClassificationExperimental,
            Mutability = MutabilityMixed,
            RecommendedCaller = CallerOperator,
            Purpose = "Projection generate / regenerate. Internal to specific tooling; not part of the chat-first product path."
        },
        new CommandGroupEntry
        {
            Name = "project",
            Classification = ClassificationExperimental,
            Mutability = MutabilityReadOnly,
            RecommendedCaller = CallerOperator,
            Purpose = "Project status. Older surface predating `intent status`; retained for compatibility."
        },
        new CommandGroupEntry
        {
            Name = "safety",
            Classification = ClassificationExperimental,
            Mutability = MutabilityReadOnly,
            RecommendedCaller = CallerOperator,
            Purpose = "Safety helpers (e.g. nested-provider-handoff). Used by advanced runtime, not the chat-first flow."
        },
        new CommandGroupEntry
        {
            Name = "run",
            Classification = ClassificationAdvanced,
            Mutability = MutabilityMixed,
            RecommendedCaller = CallerAdvancedRuntime,
            Purpose = "Integration smoke / deterministic replay / local dogfooding. Explicitly not the primary chat-first path; do not use from collaborative loops."
        }
    };

    public static int Execute(CliContext context, string[] args, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);

        if (args.Length == 1 && string.Equals(args[0], "--help", StringComparison.Ordinal))
        {
            WriteHelp(writer);
            return 0;
        }

        if (!TryParseArguments(args, out var format, out var error))
        {
            writer.WriteLine(error);
            writer.WriteLine(UsageLine);
            return 1;
        }

        if (string.Equals(format, FormatJson, StringComparison.Ordinal))
        {
            var payload = new GuideCommandsListResult { Groups = Groups };
            writer.Write(JsonSerializer.Serialize(payload, JsonOptions));
            writer.WriteLine();
        }
        else
        {
            WriteMarkdown(writer);
        }

        return 0;
    }

    private static void WriteMarkdown(TextWriter writer)
    {
        writer.WriteLine("# Guide commands — top-level groups");
        writer.WriteLine();
        writer.WriteLine("Lifecycle classification: `primary` (chat-agent's first calls), `support` (used inside the same flow), `advanced` (optional runtime, not the chat-first path), `experimental` (older surfaces retained for compatibility).");
        writer.WriteLine();
        writer.WriteLine("| group | classification | mutability | caller | purpose |");
        writer.WriteLine("|-------|----------------|------------|--------|---------|");
        foreach (var group in Groups)
        {
            writer.WriteLine($"| {group.Name} | {group.Classification} | {group.Mutability} | {group.RecommendedCaller} | {group.Purpose} |");
        }
    }

    private static bool TryParseArguments(string[] args, out string format, out string error)
    {
        format = FormatMarkdown;
        error = string.Empty;

        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];
            switch (argument)
            {
                case "--format":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--format requires a value (markdown or json).";
                        return false;
                    }

                    var requested = args[index + 1];
                    if (!string.Equals(requested, FormatMarkdown, StringComparison.Ordinal)
                        && !string.Equals(requested, FormatJson, StringComparison.Ordinal))
                    {
                        error = $"--format must be 'markdown' or 'json' (got '{requested}').";
                        return false;
                    }

                    format = requested;
                    index++;
                    break;

                default:
                    error = $"Unknown argument '{argument}'.";
                    return false;
            }
        }

        return true;
    }

    private static void WriteHelp(TextWriter writer)
    {
        writer.WriteLine("guide commands list");
        writer.WriteLine(UsageLine);
        writer.WriteLine("Read-only listing of intent-cli command groups with primary/support/advanced/experimental classification.");
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true
    };
}

internal sealed record CommandGroupEntry
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("classification")]
    public required string Classification { get; init; }

    [JsonPropertyName("mutability")]
    public required string Mutability { get; init; }

    [JsonPropertyName("recommended_caller")]
    public required string RecommendedCaller { get; init; }

    [JsonPropertyName("purpose")]
    public required string Purpose { get; init; }
}

internal sealed record GuideCommandsListResult
{
    [JsonPropertyName("groups")]
    public required IReadOnlyList<CommandGroupEntry> Groups { get; init; }
}
