using System.Text.Json;
using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// Read-only host automation command freshness preflight. The command reports
/// the installed binary surfaces required by host PR label transitions without
/// mutating GitHub, queue state, parent files, or child repo files.
/// </summary>
internal static class AutomationDoctorCommand
{
    private const string FormatText = "text";
    private const string FormatJson = "json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    public static int Execute(CliContext context, string[] args, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);

        if (!TryParseArguments(args, out var format, out var error))
        {
            writer.WriteLine(error);
            return 1;
        }

        var result = BuildResult();
        if (string.Equals(format, FormatJson, StringComparison.Ordinal))
        {
            writer.WriteLine(JsonSerializer.Serialize(result, JsonOptions));
        }
        else
        {
            WriteText(writer, result);
        }

        return 0;
    }

    private static AutomationDoctorResult BuildResult()
    {
        var requiredCommands = new[]
        {
            new AutomationDoctorRequiredCommand
            {
                Command = "intent-cli automation pr-transition",
                Transition = "review-start",
                Usage = "intent-cli automation pr-transition --transition review-start --write",
                Purpose = "Host review-start transition: add intent-target and intent-pr-reviewing, remove intent-pr-rereview-ready and legacy rereview-ready",
                Available = true,
            },
            new AutomationDoctorRequiredCommand
            {
                Command = "intent-cli automation pr-transition",
                Transition = "request-update",
                Usage = "intent-cli automation pr-transition --transition request-update --write",
                Purpose = "Host request-update transition: remove intent-pr-reviewing and add intent-pr-request-update",
                Available = true,
            },
            new AutomationDoctorRequiredCommand
            {
                Command = "intent-cli automation pr-transition",
                Transition = "approved",
                Usage = "intent-cli automation pr-transition --transition approved --write",
                Purpose = "Host approved transition: remove intent-pr-reviewing and add intent-pr-approved",
                Available = true,
            },
        };

        return new AutomationDoctorResult
        {
            Status = "ok",
            ReadOnly = true,
            RequiredCommands = requiredCommands,
            Summary = "Host automation command preflight passed: required automation pr-transition commands are available.",
        };
    }

    private static bool TryParseArguments(
        string[] args,
        out string format,
        out string error)
    {
        format = FormatText;
        error = string.Empty;

        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];
            switch (argument)
            {
                case "--format":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--format requires a value (text or json).";
                        return false;
                    }

                    var requestedFormat = args[index + 1];
                    if (!string.Equals(requestedFormat, FormatText, StringComparison.Ordinal)
                        && !string.Equals(requestedFormat, FormatJson, StringComparison.Ordinal))
                    {
                        error = $"--format must be 'text' or 'json' (got '{requestedFormat}').";
                        return false;
                    }

                    format = requestedFormat;
                    index++;
                    break;

                default:
                    error = $"Unknown argument '{argument}'. Supported: --format text|json.";
                    return false;
            }
        }

        return true;
    }

    private static void WriteText(TextWriter writer, AutomationDoctorResult result)
    {
        writer.WriteLine("# Automation doctor");
        writer.WriteLine($"status: {result.Status}");
        writer.WriteLine($"read_only: {result.ReadOnly.ToString().ToLowerInvariant()}");
        writer.WriteLine(result.Summary);
        writer.WriteLine();
        writer.WriteLine("## Required host PR transition commands");
        foreach (var command in result.RequiredCommands)
        {
            writer.WriteLine($"- {command.Usage}");
            writer.WriteLine($"  transition: {command.Transition}");
            writer.WriteLine($"  available: {command.Available.ToString().ToLowerInvariant()}");
            writer.WriteLine($"  purpose: {command.Purpose}");
        }
    }
}

internal sealed record AutomationDoctorResult
{
    [JsonPropertyName("status")]
    public required string Status { get; init; }

    [JsonPropertyName("read_only")]
    public required bool ReadOnly { get; init; }

    [JsonPropertyName("readOnly")]
    public bool ReadOnlyCamel => ReadOnly;

    [JsonPropertyName("required_commands")]
    public required IReadOnlyList<AutomationDoctorRequiredCommand> RequiredCommands { get; init; }

    [JsonPropertyName("requiredCommands")]
    public IReadOnlyList<AutomationDoctorRequiredCommand> RequiredCommandsCamel => RequiredCommands;

    [JsonPropertyName("summary")]
    public required string Summary { get; init; }
}

internal sealed record AutomationDoctorRequiredCommand
{
    [JsonPropertyName("command")]
    public required string Command { get; init; }

    [JsonPropertyName("transition")]
    public required string Transition { get; init; }

    [JsonPropertyName("usage")]
    public required string Usage { get; init; }

    [JsonPropertyName("purpose")]
    public required string Purpose { get; init; }

    [JsonPropertyName("available")]
    public required bool Available { get; init; }
}
