using System.Text.Json;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// The foreground process list is the independent corroboration used before
/// treating an absent herdr registration as a lost recipient.  Keeping this
/// reader shared makes status, delivery, and supervision use the same
/// fail-closed process observation.
/// </summary>
internal sealed record NotifyPaneProcess(
    long Pid,
    string? Cwd,
    string? Name,
    string? Argv0 = null,
    IReadOnlyList<string>? Argv = null,
    string? CommandLine = null);

internal sealed record NotifyPaneProcessInfoResult
{
    public required bool Resolved { get; init; }
    public required IReadOnlyList<NotifyPaneProcess> Processes { get; init; }
    public string? Cause { get; init; }
    public required string Summary { get; init; }
}

internal static class NotifyPaneProcessReader
{
    public static NotifyPaneProcessInfoResult Read(
        INotifyProcessRunner runner,
        string executable,
        string paneId)
    {
        NotifyProcessResult response;
        try
        {
            response = runner.Run(executable, ["pane", "process-info", "--pane", paneId]);
        }
        catch (InvalidOperationException exception)
        {
            return Failure("process-corroboration-unavailable", exception.Message);
        }

        if (response.ExitCode != 0)
        {
            return Failure(
                "process-corroboration-unavailable",
                $"herdr pane process-info failed for pane '{paneId}': {OneLine(response.StandardError, response.StandardOutput)}");
        }

        try
        {
            using var document = JsonDocument.Parse(response.StandardOutput);
            var processInfo = document.RootElement.GetProperty("result").GetProperty("process_info");
            if (!processInfo.TryGetProperty("foreground_processes", out var foreground)
                || foreground.ValueKind != JsonValueKind.Array)
            {
                return Failure(
                    "process-corroboration-unavailable",
                    $"herdr pane process-info did not report a foreground_processes array for pane '{paneId}'; refusing to infer registration loss.");
            }

            var processes = new List<NotifyPaneProcess>();
            foreach (var process in foreground.EnumerateArray())
            {
                if (!process.TryGetProperty("pid", out var pidElement)
                    || !pidElement.TryGetInt64(out var pid)
                    || pid <= 0)
                {
                    return Failure(
                        "process-corroboration-unavailable",
                        $"herdr pane process-info returned a foreground process without a valid pid for pane '{paneId}'; refusing to infer registration loss.");
                }

                var cwd = process.TryGetProperty("cwd", out var cwdElement)
                    && cwdElement.ValueKind == JsonValueKind.String
                    ? cwdElement.GetString()
                    : null;
                var name = process.TryGetProperty("name", out var nameElement)
                    && nameElement.ValueKind == JsonValueKind.String
                    ? nameElement.GetString()
                    : null;
                var argv0 = process.TryGetProperty("argv0", out var argv0Element)
                    && argv0Element.ValueKind == JsonValueKind.String
                    ? argv0Element.GetString()
                    : null;
                var argv = process.TryGetProperty("argv", out var argvElement)
                    && argvElement.ValueKind == JsonValueKind.Array
                    ? argvElement.EnumerateArray()
                        .Where(item => item.ValueKind == JsonValueKind.String)
                        .Select(item => item.GetString()!)
                        .ToArray()
                    : null;
                var commandLine = process.TryGetProperty("cmdline", out var commandLineElement)
                    && commandLineElement.ValueKind == JsonValueKind.String
                    ? commandLineElement.GetString()
                    : null;
                processes.Add(new NotifyPaneProcess(pid, cwd, name, argv0, argv, commandLine));
            }

            return new NotifyPaneProcessInfoResult
            {
                Resolved = true,
                Processes = processes,
                Summary = $"Read foreground process corroboration for recorded pane '{paneId}'.",
            };
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return Failure(
                "process-corroboration-unavailable",
                $"herdr pane process-info returned an unreadable process shape for pane '{paneId}': {exception.Message}");
        }
    }

    private static NotifyPaneProcessInfoResult Failure(string cause, string summary) => new()
    {
        Resolved = false,
        Processes = [],
        Cause = cause,
        Summary = summary,
    };

    private static string OneLine(params string[] values)
    {
        var value = values.FirstOrDefault(item => !string.IsNullOrWhiteSpace(item)) ?? "no detail";
        return string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }
}
