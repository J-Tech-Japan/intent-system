using System.Text.Json;
using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G563: retired pointer. G488 introduced this command to render a portable
/// agent skill body an operator copied out by hand — the command wrote no
/// files and told the reader to paste the rendered body into their agent's
/// skill location themselves.
///
/// G559 shipped the real thing: one embedded <c>intent-cli</c> SKILL.md that
/// <c>intent-cli skill install</c> writes to each platform's skill location,
/// with <c>skill diff</c> for drift detection. Keeping G488's renderer alive
/// meant two different artifacts both named <c>intent-cli</c> giving different
/// advice, and the older one still instructing the copy-out workflow the new
/// command replaced. This surface is therefore reduced to a deprecation
/// pointer at the <c>skill</c> command group; it renders no skill body and no
/// install advice of its own.
///
/// Still read-only, still writes no files, still launches no provider.
/// </summary>
internal static class GuideSkillPackCommand
{
    private const string FormatJson = "json";
    private const string FormatMarkdown = "markdown";

    private const string UsageLine =
        "Usage: intent-cli guide skill-pack [--domain <name>] [--target-repo <owner/repo>] [--format markdown|json]";

    /// <summary>Command group that owns the shipped skill after G559.</summary>
    internal const string SupersededBy = "skill";

    internal const string DeprecationSummary =
        "`intent-cli guide skill-pack` is DEPRECATED (G563) and renders no skill body. The `intent-cli` agent skill "
        + "is shipped by this CLI and installed by `intent-cli skill install` — there is exactly one artifact named "
        + "`intent-cli`, and this command is not it.";

    internal const string DeprecationReason =
        "G488 rendered a skill body for the operator to copy out by hand. G559 replaced that workflow with an "
        + "embedded SKILL.md and a real installer, so the rendered body became a second, drifting artifact under the "
        + "same name. Use the `skill` command group instead.";

    internal static readonly IReadOnlyList<string> UseInstead = new[]
    {
        "intent-cli skill list — what this CLI ships and where each platform installs it.",
        "intent-cli skill install --target claude|codex|copilot|all [--scope user|repo] [--force] — write the skill (no copy-out step).",
        "intent-cli skill diff — detect drift between the installed file and what this CLI ships.",
    };

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

        // --domain / --target-repo are still accepted so existing invocations
        // reach the pointer instead of an argument error, but they no longer
        // select any content: the pointer is the whole output.
        if (!TryParseArguments(args, out var format, out var error))
        {
            writer.WriteLine(error);
            writer.WriteLine(UsageLine);
            return 1;
        }

        if (string.Equals(format, FormatJson, StringComparison.Ordinal))
        {
            var payload = new GuideSkillPackDeprecation
            {
                Status = "deprecated",
                SupersededBy = SupersededBy,
                Summary = DeprecationSummary,
                Reason = DeprecationReason,
                UseInstead = UseInstead,
            };
            writer.Write(JsonSerializer.Serialize(payload, JsonOptions));
            writer.WriteLine();
            return 0;
        }

        WriteMarkdown(writer);
        return 0;
    }

    private static bool TryParseArguments(string[] args, out string format, out string error)
    {
        format = FormatMarkdown;
        error = string.Empty;

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (!RequiresValue(arg))
            {
                error = $"Unknown argument '{arg}'.";
                return false;
            }

            if (i + 1 >= args.Length)
            {
                error = $"{arg} requires a value.";
                return false;
            }

            var value = args[++i];
            if (string.Equals(arg, "--format", StringComparison.Ordinal))
            {
                format = value;
            }
        }

        if (!string.Equals(format, FormatMarkdown, StringComparison.Ordinal)
            && !string.Equals(format, FormatJson, StringComparison.Ordinal))
        {
            error = $"Unknown --format '{format}'. Supported: markdown, json.";
            return false;
        }

        return true;
    }

    private static bool RequiresValue(string arg) =>
        string.Equals(arg, "--format", StringComparison.Ordinal)
        || string.Equals(arg, "--domain", StringComparison.Ordinal)
        || string.Equals(arg, "--target-repo", StringComparison.Ordinal);

    private static void WriteMarkdown(TextWriter writer)
    {
        writer.WriteLine("# `guide skill-pack` — deprecated (G563)");
        writer.WriteLine();
        writer.WriteLine(DeprecationSummary);
        writer.WriteLine();
        writer.WriteLine("## Use instead");
        writer.WriteLine();
        foreach (var command in UseInstead)
        {
            writer.WriteLine($"- {command}");
        }
        writer.WriteLine();
        writer.WriteLine("## Why");
        writer.WriteLine();
        writer.WriteLine(DeprecationReason);
    }

    private static void WriteHelp(TextWriter writer)
    {
        writer.WriteLine("guide skill-pack");
        writer.WriteLine(UsageLine);
        writer.WriteLine();
        writer.WriteLine("DEPRECATED (G563): renders a pointer at the `skill` command group and nothing else.");
        writer.WriteLine("The shipped `intent-cli` agent skill is installed by `intent-cli skill install`;");
        writer.WriteLine("`intent-cli skill list` / `diff` show what ships and whether an install has drifted.");
    }
}

internal sealed record GuideSkillPackDeprecation
{
    [JsonPropertyName("status")]
    public required string Status { get; init; }

    [JsonPropertyName("superseded_by")]
    public required string SupersededBy { get; init; }

    [JsonPropertyName("summary")]
    public required string Summary { get; init; }

    [JsonPropertyName("reason")]
    public required string Reason { get; init; }

    [JsonPropertyName("use_instead")]
    public required IReadOnlyList<string> UseInstead { get; init; }
}
