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

        var result = BuildResult(context);
        if (string.Equals(format, FormatJson, StringComparison.Ordinal))
        {
            writer.WriteLine(JsonSerializer.Serialize(result, JsonOptions));
        }
        else
        {
            WriteText(writer, result);
        }

        return string.Equals(result.Status, "ok", StringComparison.Ordinal) ? 0 : 1;
    }

    private static AutomationDoctorResult BuildResult(CliContext context)
    {
        var surfaceReport = AutomationInstalledCliSurfaceProbe.Check(context);
        var requiredCommands = surfaceReport.Checks
            .Select(check => new AutomationDoctorRequiredCommand
            {
                Command = check.Command,
                Transition = check.Transition,
                Usage = BuildUsage(check.Command, check.Transition),
                Purpose = BuildPurpose(check.Command, check.Transition),
                Available = check.Available,
                Reason = check.Reason,
            })
            .ToArray();

        var missing = requiredCommands
            .Where(command => !command.Available)
            .ToArray();

        return new AutomationDoctorResult
        {
            Status = surfaceReport.Available ? "ok" : "stale-host-cli",
            ReadOnly = true,
            InstalledCliPath = surfaceReport.InstalledCliPath,
            RequiredCommands = requiredCommands,
            Summary = surfaceReport.Available
                ? "Host automation command preflight passed: required installed automation command surfaces are available."
                : $"Host automation command preflight failed: installed CLI at {surfaceReport.InstalledCliPath} is missing or stale for {string.Join(", ", missing.Select(command => command.Usage))}. Abort before label transitions; refresh the installed CLI instead of falling back to raw gh label mutation.",
        };
    }

    private static string BuildUsage(string command, string? transition) =>
        command switch
        {
            "intent-cli automation summary" => "intent-cli automation summary --format json",
            "intent-cli automation host-review-preflight" => "intent-cli automation host-review-preflight --format json",
            "intent-cli automation issue-publish" => "intent-cli automation issue-publish --issue <n> --write --format json",
            "intent-cli automation pr-transition" => $"intent-cli automation pr-transition --transition {transition} --write --format json",
            _ => command,
        };

    private static string BuildPurpose(string command, string? transition) =>
        command switch
        {
            "intent-cli automation summary" => "Read-only installed command contract summary used by host runbooks.",
            "intent-cli automation host-review-preflight" => "Read-only host review preflight before any host-owned PR label transition.",
            "intent-cli automation issue-publish" => "Host issue-publish transition: add intent-target to a child issue.",
            "intent-cli automation pr-transition" when string.Equals(transition, "review-start", StringComparison.Ordinal) =>
                "Host review-start transition: add intent-target and intent-pr-reviewing, remove intent-pr-rereview-ready and legacy rereview-ready",
            "intent-cli automation pr-transition" when string.Equals(transition, "request-update", StringComparison.Ordinal) =>
                "Host request-update transition: remove intent-pr-reviewing and add intent-pr-request-update",
            "intent-cli automation pr-transition" when string.Equals(transition, "approved", StringComparison.Ordinal) =>
                "Host approved transition: remove intent-pr-reviewing and add intent-pr-approved",
            _ => "Required host automation command surface.",
        };

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
        writer.WriteLine($"installed_cli_path: {result.InstalledCliPath}");
        writer.WriteLine(result.Summary);
        writer.WriteLine();
        writer.WriteLine("## Required installed automation command surfaces");
        foreach (var command in result.RequiredCommands)
        {
            writer.WriteLine($"- {command.Usage}");
            writer.WriteLine($"  transition: {command.Transition}");
            writer.WriteLine($"  available: {command.Available.ToString().ToLowerInvariant()}");
            if (!string.IsNullOrEmpty(command.Reason))
            {
                writer.WriteLine($"  reason: {command.Reason}");
            }
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

    [JsonPropertyName("installed_cli_path")]
    public required string InstalledCliPath { get; init; }

    [JsonPropertyName("installedCliPath")]
    public string InstalledCliPathCamel => InstalledCliPath;

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
    public required string? Transition { get; init; }

    [JsonPropertyName("usage")]
    public required string Usage { get; init; }

    [JsonPropertyName("purpose")]
    public required string Purpose { get; init; }

    [JsonPropertyName("available")]
    public required bool Available { get; init; }

    [JsonPropertyName("reason")]
    public required string? Reason { get; init; }
}
