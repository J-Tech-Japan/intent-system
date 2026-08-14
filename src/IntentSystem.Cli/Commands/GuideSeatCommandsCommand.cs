using System.Text.Json;
using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G696: read-only per-kind seat command-form guidance. It renders the
/// versioned registry without consulting host metadata, changing settings,
/// probing a permission surface, or approving a command.
/// </summary>
internal static class GuideSeatCommandsCommand
{
    private const string FormatMarkdown = "markdown";
    private const string FormatJson = "json";

    private const string UsageLine =
        "Usage: intent-cli guide seat-commands --kind claude|codex [--format markdown|json]";

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
            WriteHelp(writer);
            return 0;
        }

        if (!TryParseArguments(args, out var kind, out var format, out var error))
        {
            writer.WriteLine(error);
            writer.WriteLine(UsageLine);
            return 1;
        }

        var guidance = SeatCommandGuidanceRegistry.ForKind(kind!);
        if (string.Equals(format, FormatJson, StringComparison.Ordinal))
        {
            writer.Write(JsonSerializer.Serialize(guidance, JsonOptions));
            writer.WriteLine();
        }
        else
        {
            WriteMarkdown(writer, guidance);
        }

        return 0;
    }

    private static void WriteMarkdown(TextWriter writer, SeatCommandGuidance guidance)
    {
        writer.WriteLine($"# Seat command guidance — {guidance.Kind}");
        writer.WriteLine();
        writer.WriteLine($"- registry version: `{guidance.RegistryVersion}`");
        writer.WriteLine("- read-only: yes");
        writer.WriteLine("- settings or approvals: never");
        writer.WriteLine();

        writer.WriteLine("## Sanctioned forms");
        foreach (var form in guidance.SanctionedForms)
        {
            writer.WriteLine($"- **{form.Name}**: `{form.Command}`");
            writer.WriteLine($"  - {form.Guidance}");
        }
        writer.WriteLine();

        writer.WriteLine("## Prefix-matching breakers");
        foreach (var breaker in guidance.Breakers)
        {
            writer.WriteLine($"- **{breaker.Name}** — {breaker.Form}");
            writer.WriteLine($"  - {breaker.Guidance}");
        }
        writer.WriteLine();

        writer.WriteLine("## Sanctioned alternatives");
        foreach (var alternative in guidance.Alternatives)
        {
            writer.WriteLine($"- denied: `{alternative.DeniedForm}`");
            writer.WriteLine($"  - sanctioned: `{alternative.SanctionedForm}`");
            writer.WriteLine("  - commands:");
            foreach (var command in alternative.Commands)
            {
                writer.WriteLine($"    - `{command}`");
            }
            writer.WriteLine($"  - {alternative.Guidance}");
        }
        writer.WriteLine();

        writer.WriteLine("## Authority boundary");
        writer.WriteLine(guidance.AuthorityBoundary);
        writer.WriteLine("Rules describe forms; the operator's allowlist remains authoritative.");
    }

    private static bool TryParseArguments(
        string[] args,
        out string? kind,
        out string format,
        out string error)
    {
        kind = null;
        format = FormatMarkdown;
        error = string.Empty;

        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--kind":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--kind requires a value (claude or codex).";
                        return false;
                    }

                    kind = args[++index].Trim();
                    if (!SeatCommandGuidanceRegistry.SupportedKinds.Contains(kind, StringComparer.Ordinal))
                    {
                        error = $"--kind must be one of {string.Join(" | ", SeatCommandGuidanceRegistry.SupportedKinds)} (got '{kind}').";
                        return false;
                    }
                    break;

                case "--format":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--format requires a value (markdown or json).";
                        return false;
                    }

                    format = args[++index].Trim();
                    if (format is not (FormatMarkdown or FormatJson))
                    {
                        error = $"--format must be 'markdown' or 'json' (got '{format}').";
                        return false;
                    }
                    break;

                default:
                    error = $"Unknown argument '{args[index]}'.";
                    return false;
            }
        }

        if (kind is null)
        {
            error = "--kind is required.";
            return false;
        }

        return true;
    }

    private static void WriteHelp(TextWriter writer)
    {
        writer.WriteLine("guide seat-commands");
        writer.WriteLine(UsageLine);
        writer.WriteLine("Render the versioned, read-only per-kind seat registry: sanctioned forms, prefix-matching breakers, and measured alternatives.");
    }
}
