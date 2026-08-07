using System.Text.Json;
using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// Records and clears the durable CI wait obligation. This command never
/// polls GitHub and never starts a background process.
/// </summary>
internal static class AutomationCiWaitCommand
{
    private const string FormatJson = "json";
    private const string FormatMarkdown = "markdown";
    private const string UsageLine =
        "Usage: intent-cli automation ci-wait record --domain <d> --repo <owner/repo> --pr <n> --head <sha> "
        + "--transition <transition> [--dry-run|--write] [--format json|markdown]\n"
        + "       intent-cli automation ci-wait clear --repo <owner/repo> --pr <n> --transition <transition> "
        + "[--dry-run|--write] [--format json|markdown]\n"
        + "       intent-cli automation ci-wait show [--domain <d>] [--repo <owner/repo>] [--format json|markdown]";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static int Execute(CliContext context, string[] args, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);

        if (args.Length == 1 && string.Equals(args[0], "--help", StringComparison.Ordinal))
        {
            writer.WriteLine(UsageLine);
            return 0;
        }

        if (args.Length == 0)
        {
            writer.WriteLine(UsageLine);
            return 1;
        }

        var operation = args[0];
        if (!string.Equals(operation, "record", StringComparison.Ordinal)
            && !string.Equals(operation, "clear", StringComparison.Ordinal)
            && !string.Equals(operation, "show", StringComparison.Ordinal))
        {
            writer.WriteLine($"Unknown ci-wait operation '{operation}'.");
            writer.WriteLine(UsageLine);
            return 1;
        }

        if (!TryParse(args[1..], operation, out var domain, out var repo, out var pr, out var head,
                out var transition, out var write, out var format, out var error))
        {
            writer.WriteLine(error);
            writer.WriteLine(UsageLine);
            return 1;
        }

        if (string.Equals(operation, "record", StringComparison.Ordinal))
        {
            var record = new CiWaitRecord
            {
                Domain = domain!,
                Repo = repo!,
                Pr = pr!.Value,
                ObservedHead = head!,
                OwedTransition = transition!,
                RecordedAt = DateTimeOffset.UtcNow,
            };
            var writeResult = CiWaitStore.Record(context.RepoRoot, record, write);
            return Emit(writer, format, new CiWaitCommandResult
            {
                Operation = operation,
                Mode = write ? "write" : "dry-run",
                Applied = writeResult.Applied,
                AlreadyConverged = writeResult.AlreadyConverged,
                Path = writeResult.Path,
                Records = [record],
                Error = writeResult.Error,
                Summary = writeResult.Error is null
                    ? (writeResult.AlreadyConverged
                        ? $"CI wait for PR #{record.Pr} is already recorded at head {record.ObservedHead}; no change made."
                        : write
                            ? $"Recorded durable CI wait for PR #{record.Pr} at head {record.ObservedHead}; owed transition is '{record.OwedTransition}'."
                            : $"Dry-run: would record durable CI wait for PR #{record.Pr} at head {record.ObservedHead}; owed transition is '{record.OwedTransition}'.")
                    : $"Could not record CI wait for PR #{record.Pr}: {writeResult.Error}",
            });
        }

        if (string.Equals(operation, "clear", StringComparison.Ordinal))
        {
            var writeResult = CiWaitStore.ClearForTransition(context.RepoRoot, repo!, pr!.Value, transition!, write);
            var read = CiWaitStore.ReadOpen(context.RepoRoot, repo: repo);
            return Emit(writer, format, new CiWaitCommandResult
            {
                Operation = operation,
                Mode = write ? "write" : "dry-run",
                Applied = writeResult.Applied,
                AlreadyConverged = writeResult.AlreadyConverged,
                Path = writeResult.Path,
                Records = read.Records,
                Error = writeResult.Error ?? read.Error,
                Summary = writeResult.Error is null
                    ? (writeResult.AlreadyConverged
                        ? $"No open CI wait for PR #{pr} owed transition '{transition}' was found."
                        : write
                            ? $"Cleared durable CI wait for PR #{pr} after transition '{transition}'."
                            : $"Dry-run: would clear durable CI wait for PR #{pr} after transition '{transition}'.")
                    : $"Could not clear CI wait for PR #{pr}: {writeResult.Error}",
            });
        }

        var result = CiWaitStore.ReadOpen(context.RepoRoot, domain, repo);
        return Emit(writer, format, new CiWaitCommandResult
        {
            Operation = operation,
            Mode = "read",
            Applied = false,
            AlreadyConverged = false,
            Path = result.Path,
            Records = result.Records,
            Error = result.Error,
            Summary = result.Error is null
                ? $"Found {result.Records.Count} open durable CI wait obligation(s)."
                : $"Could not read durable CI waits: {result.Error}",
        });
    }

    private static int Emit(TextWriter writer, string format, CiWaitCommandResult result)
    {
        if (string.Equals(format, FormatJson, StringComparison.Ordinal))
        {
            writer.WriteLine(JsonSerializer.Serialize(result, JsonOptions));
        }
        else
        {
            writer.WriteLine($"# automation ci-wait {result.Operation}");
            writer.WriteLine();
            writer.WriteLine($"- mode: {result.Mode}");
            writer.WriteLine($"- applied: {result.Applied.ToString().ToLowerInvariant()}");
            writer.WriteLine($"- already_converged: {result.AlreadyConverged.ToString().ToLowerInvariant()}");
            writer.WriteLine($"- path: {result.Path}");
            writer.WriteLine($"- records: {result.Records.Count}");
            writer.WriteLine($"- summary: {result.Summary}");
            if (result.Error is not null)
            {
                writer.WriteLine($"- error: {result.Error}");
            }
        }

        return result.Error is null ? 0 : 1;
    }

    private static bool TryParse(
        string[] args,
        string operation,
        out string? domain,
        out string? repo,
        out int? pr,
        out string? head,
        out string? transition,
        out bool write,
        out string format,
        out string error)
    {
        domain = null;
        repo = null;
        pr = null;
        head = null;
        transition = null;
        write = false;
        format = FormatMarkdown;
        error = string.Empty;

        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];
            if (argument is "--domain" or "--repo" or "--pr" or "--head" or "--observed-head" or "--transition" or "--format")
            {
                if (index + 1 >= args.Length)
                {
                    error = $"{argument} requires a value.";
                    return false;
                }

                var value = args[++index];
                switch (argument)
                {
                    case "--domain": domain = value; break;
                    case "--repo": repo = value; break;
                    case "--head":
                    case "--observed-head": head = value; break;
                    case "--transition": transition = value; break;
                    case "--pr":
                        if (!int.TryParse(value, out var parsedPr) || parsedPr <= 0)
                        {
                            error = $"--pr must be a positive integer (got '{value}').";
                            return false;
                        }
                        pr = parsedPr;
                        break;
                    case "--format":
                        format = value;
                        if (format is not FormatJson and not FormatMarkdown)
                        {
                            error = $"Unsupported --format '{format}'. Use json or markdown.";
                            return false;
                        }
                        break;
                }
                continue;
            }

            if (argument == "--write")
            {
                write = true;
                continue;
            }
            if (argument == "--dry-run")
            {
                write = false;
                continue;
            }

            error = $"Unknown argument '{argument}'.";
            return false;
        }

        if (operation == "record" && string.IsNullOrWhiteSpace(domain))
        {
            error = "record requires --domain <name>.";
            return false;
        }
        if (operation is "record" or "clear" && string.IsNullOrWhiteSpace(repo))
        {
            error = $"{operation} requires --repo <owner/repo>.";
            return false;
        }
        if (operation is "record" or "clear" && pr is null)
        {
            error = $"{operation} requires --pr <number>.";
            return false;
        }
        if (operation == "record" && string.IsNullOrWhiteSpace(head))
        {
            error = "record requires --head <exact-head-sha>.";
            return false;
        }
        if (operation is "record" or "clear" && string.IsNullOrWhiteSpace(transition))
        {
            error = $"{operation} requires --transition <transition>.";
            return false;
        }
        if (operation is "record" or "clear")
        {
            if (!IsSafeToken(repo!) || (head is not null && !IsSafeToken(head)) || !IsSafeToken(transition!))
            {
                error = "repo, head, and transition must be non-empty safe tokens without whitespace or path traversal.";
                return false;
            }
        }

        return true;
    }

    private static bool IsSafeToken(string value) =>
        !string.IsNullOrWhiteSpace(value)
        && !value.Contains("..", StringComparison.Ordinal)
        && value.All(character => char.IsLetterOrDigit(character) || character is '-' or '_' or '.' or '/' or ':');
}

internal sealed record CiWaitCommandResult
{
    [JsonPropertyName("operation")] public required string Operation { get; init; }
    [JsonPropertyName("mode")] public required string Mode { get; init; }
    [JsonPropertyName("applied")] public required bool Applied { get; init; }
    [JsonPropertyName("already_converged")] public required bool AlreadyConverged { get; init; }
    [JsonPropertyName("path")] public required string Path { get; init; }
    [JsonPropertyName("records")] public required IReadOnlyList<CiWaitRecord> Records { get; init; }
    [JsonPropertyName("summary")] public required string Summary { get; init; }
    [JsonPropertyName("error")] [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Error { get; init; }
}
