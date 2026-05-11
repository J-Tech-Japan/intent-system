using System.Text.Json;
using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G322: read-only <c>intent-cli guide automation lint</c>. Validates a
/// generated automation setup contract (markdown or plain text) against
/// the safety clauses the rest of the system relies on
/// (<see cref="GuideAutomationContractLinter"/>). Mutates nothing;
/// never launches a provider; never reads queue-state. Tests inject
/// inline text; production callers read the contract from a file.
/// </summary>
internal static class GuideAutomationLintCommand
{
    private const string FormatJson = "json";
    private const string FormatMarkdown = "markdown";

    private const string UsageLine =
        "Usage: intent-cli guide automation lint [--from-file <path> | --text <inline>] [--format markdown|json]";

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

        if (!TryParseArguments(args, out var fromFile, out var inlineText, out var format, out var error))
        {
            writer.WriteLine(error);
            writer.WriteLine(UsageLine);
            return 1;
        }

        if (string.IsNullOrEmpty(fromFile) && inlineText is null)
        {
            writer.WriteLine("guide automation lint requires either --from-file <path> or --text <inline>.");
            writer.WriteLine(UsageLine);
            return 1;
        }
        if (!string.IsNullOrEmpty(fromFile) && inlineText is not null)
        {
            writer.WriteLine("guide automation lint accepts --from-file or --text, not both.");
            writer.WriteLine(UsageLine);
            return 1;
        }

        string contract;
        if (!string.IsNullOrEmpty(fromFile))
        {
            try
            {
                contract = File.ReadAllText(fromFile!);
            }
            catch (Exception exception) when (
                exception is FileNotFoundException
                or DirectoryNotFoundException
                or UnauthorizedAccessException
                or IOException)
            {
                writer.WriteLine($"failed to read --from-file '{fromFile}': {exception.Message}");
                return 1;
            }
        }
        else
        {
            contract = inlineText!;
        }

        var result = GuideAutomationContractLinter.Lint(contract);

        if (string.Equals(format, FormatJson, StringComparison.Ordinal))
        {
            writer.Write(JsonSerializer.Serialize(result, JsonOptions));
            writer.WriteLine();
        }
        else
        {
            WriteMarkdown(writer, result);
        }

        // G322: command exit is 0 even when status==fail so callers can
        // pipe / inspect structured output. CI / scripts looking for a
        // gate should test the `status` field, not the exit code.
        return 0;
    }

    private static void WriteMarkdown(TextWriter writer, GuideAutomationLintResult result)
    {
        writer.WriteLine($"# guide automation lint — {result.Status}");
        writer.WriteLine();
        if (result.MissingClauses.Count == 0)
        {
            writer.WriteLine("All required safety clauses present.");
        }
        else
        {
            writer.WriteLine("## Missing clauses");
            foreach (var miss in result.MissingClauses)
            {
                writer.WriteLine($"- `{miss.Id}` — {miss.Description}");
                if (miss.RequiredAny.Count > 0)
                {
                    writer.WriteLine($"  expected ANY of: {string.Join(" | ", miss.RequiredAny.Select(p => $"\"{p}\""))}");
                }
            }
        }
        if (result.FoundClauses.Count > 0)
        {
            writer.WriteLine();
            writer.WriteLine("## Found clauses");
            foreach (var found in result.FoundClauses)
            {
                writer.WriteLine($"- `{found}`");
            }
        }
        if (!string.IsNullOrEmpty(result.RecommendedRegenerationCommand))
        {
            writer.WriteLine();
            writer.WriteLine("## Recommended regeneration");
            writer.WriteLine();
            writer.WriteLine($"```");
            writer.WriteLine(result.RecommendedRegenerationCommand);
            writer.WriteLine($"```");
        }
    }

    private static bool TryParseArguments(
        string[] args,
        out string? fromFile,
        out string? inlineText,
        out string format,
        out string error)
    {
        fromFile = null;
        inlineText = null;
        format = FormatJson; // G322: default controller-friendly
        error = string.Empty;

        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];
            switch (argument)
            {
                case "--from-file":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--from-file requires a path.";
                        return false;
                    }
                    fromFile = args[index + 1];
                    index++;
                    break;

                case "--text":
                    if (index + 1 >= args.Length)
                    {
                        error = "--text requires a value.";
                        return false;
                    }
                    inlineText = args[index + 1];
                    index++;
                    break;

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
        writer.WriteLine("guide automation lint");
        writer.WriteLine(UsageLine);
        writer.WriteLine("Validates a generated automation setup contract against the required safety clauses.");
        writer.WriteLine("Pass --from-file to read a saved contract (paste-ready markdown), or --text for inline content.");
        writer.WriteLine("Exit code is always 0; gate on the `status` field of the JSON output.");
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}

/// <summary>
/// G322: pure contract linter. Each clause has a stable identifier and
/// a list of substring patterns. ANY substring match (case-insensitive)
/// counts as a clause hit; missing all patterns surfaces the clause as a
/// failure with the full required-any list so the caller knows what to
/// add. Splitting the safety surface into small clauses keeps failures
/// precise — "missing same-thread scheduling" is actionable, "contract
/// looks wrong" is not.
/// </summary>
internal static class GuideAutomationContractLinter
{
    public static GuideAutomationLintResult Lint(string contract)
    {
        ArgumentNullException.ThrowIfNull(contract);

        var found = new List<string>();
        var missing = new List<GuideAutomationLintMiss>();
        foreach (var clause in Clauses)
        {
            var hit = false;
            foreach (var pattern in clause.Patterns)
            {
                if (contract.IndexOf(pattern, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    hit = true;
                    break;
                }
            }

            if (hit)
            {
                found.Add(clause.Id);
            }
            else
            {
                missing.Add(new GuideAutomationLintMiss
                {
                    Id = clause.Id,
                    Description = clause.Description,
                    RequiredAny = clause.Patterns
                });
            }
        }

        return new GuideAutomationLintResult
        {
            Status = missing.Count == 0 ? "pass" : "fail",
            FoundClauses = found,
            MissingClauses = missing,
            RecommendedRegenerationCommand = missing.Count == 0
                ? null
                : "intent-cli guide automation setup --purpose <child-implement|host-review-next-slice> --agent <claude|codex> --domain <DOMAIN> --target-repo <owner/repo> --frequency <NNm> --format markdown"
        };
    }

    private sealed record Clause(string Id, string Description, IReadOnlyList<string> Patterns);

    // Required safety clauses. Each clause's Patterns list is OR-matched
    // (case-insensitive substring); contracts must contain at least one
    // pattern from each list.
    private static readonly IReadOnlyList<Clause> Clauses = new[]
    {
        new Clause(
            "same-thread-scheduling",
            "Contract names a same-thread / local-automation scheduling mechanism (G314).",
            new[]
            {
                "same-thread",
                "same thread",
                "current-thread",
                "current thread",
                "local automation",
                "heartbeat",
                "/loop"
            }),
        new Clause(
            "installed-cli-doctor-check",
            "Contract references `intent-cli automation doctor` / installed CLI surface checks.",
            new[]
            {
                "automation doctor",
                "installed CLI",
                "installed-cli",
                "stale-host-cli"
            }),
        new Clause(
            "stale-cli-abort",
            "Contract requires aborting the wake on stale CLI rather than falling back.",
            new[]
            {
                "stale-host-cli",
                "stale CLI",
                "abort the wake",
                "refresh the installed CLI",
                "refresh or reinstall"
            }),
        new Clause(
            "no-local-rules-or-skills",
            "Contract forbids reading intents/rules/** and copied local skill prompts.",
            new[]
            {
                "intents/rules/**",
                "local skill files",
                "copied prompt"
            }),
        new Clause(
            "no-dotnet-run-fallback",
            "Contract explicitly forbids falling back to `dotnet run` instead of the installed `intent-cli`.",
            new[]
            {
                // G322: only the explicit negative-form prohibition counts.
                // A bare `dotnet run` mention is NOT a safety guarantee —
                // a contract that says "use dotnet run instead" must fail
                // this clause, not silently pass.
                "Do not run `dotnet run`",
                "do not run dotnet run",
                "do NOT fall back to direct DLL",
                "do not fall back to direct dll",
                "never `dotnet run`",
                "never dotnet run"
            }),
        new Clause(
            "no-raw-label-mutation",
            "Contract explicitly forbids raw `gh label` / manual workflow label edits.",
            new[]
            {
                // G322: negative-form prohibition only.
                "no manual `gh ... edit",
                "no manual gh ... edit",
                "no raw `gh` label",
                "no raw gh label",
                "manual `gh ... edit --add-label` / `--remove-label` fallback",
                "no manual gh.*edit.*label"
            }),
        new Clause(
            "no-intent-cli-run",
            "Contract explicitly forbids `intent-cli run` from the chat-first loop (advanced-only).",
            new[]
            {
                // G322: only negative-form prohibition counts. A contract
                // that says "you may use `intent-cli run`" must fail.
                "do not call `intent-cli run`",
                "do not call intent-cli run",
                "never `intent-cli run`",
                "never intent-cli run",
                "advanced runtime"
            }),
        new Clause(
            "no-provider-launch",
            "Contract explicitly forbids `intent-cli` launching Claude/Codex or any AI provider.",
            new[]
            {
                // G322: negative-form prohibition only.
                "do not ask `intent-cli` to launch",
                "do not ask intent-cli to launch",
                "never launch claude",
                "never launch codex",
                "do not launch AI provider"
            }),
        new Clause(
            "canonical-worker-commands",
            "Contract references canonical worker commands (next-action / claim / complete).",
            new[]
            {
                "worker next-action",
                "worker claim",
                "worker complete"
            }),
        new Clause(
            "wip-cap-and-idle-handling",
            "Contract addresses WIP cap / Hard Clarification (host) or `action: none → idle` (child).",
            new[]
            {
                // Host-loop vocabulary.
                "WIP cap",
                "wip-cap",
                "wip cap",
                "Hard Clarification",
                "hard-clarification",
                "true-idle",
                "true idle",
                "no-actionable-item",
                // Child-loop idle vocabulary: `worker next-action` returns
                // `action: none` and the loop stops with idle.
                "stop with `idle`",
                "stop with idle"
            })
    };
}

internal sealed record GuideAutomationLintResult
{
    [JsonPropertyName("status")]
    public required string Status { get; init; }

    [JsonPropertyName("found_clauses")]
    public required IReadOnlyList<string> FoundClauses { get; init; }

    [JsonPropertyName("missing_clauses")]
    public required IReadOnlyList<GuideAutomationLintMiss> MissingClauses { get; init; }

    [JsonPropertyName("recommended_regeneration_command")]
    public string? RecommendedRegenerationCommand { get; init; }
}

internal sealed record GuideAutomationLintMiss
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("description")]
    public required string Description { get; init; }

    [JsonPropertyName("required_any")]
    public required IReadOnlyList<string> RequiredAny { get; init; }
}
