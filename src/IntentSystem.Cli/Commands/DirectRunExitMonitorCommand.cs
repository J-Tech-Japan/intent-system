using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

namespace IntentSystem.Cli.Commands;

internal static class DirectRunExitMonitorCommand
{
    private const string CommandName = "__direct-run-exit-monitor";
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan ExitGracePeriod = TimeSpan.FromMilliseconds(250);

    public static bool TryExecute(string[] args, out int exitCode)
    {
        ArgumentNullException.ThrowIfNull(args);

        exitCode = 0;
        if (args.Length == 0 || !string.Equals(args[0], CommandName, StringComparison.Ordinal))
        {
            return false;
        }

        if (!TryParseArguments(args, out var options))
        {
            Console.Error.WriteLine(
                $"Usage: {CommandName} <pid> <provider_log_path> <execution_unit> <entry_kind> <provider> <session_id> <launched_at>");
            exitCode = 1;
            return true;
        }

        exitCode = Execute(options);
        return true;
    }

    public static ProcessStartInfo CreateDetachedStartInfo(
        int processId,
        string providerEventLogPath,
        string executionUnit,
        string entryKind,
        string provider,
        string providerSessionId,
        DateTimeOffset launchedAt)
    {
        if (processId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(processId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(providerEventLogPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(executionUnit);
        ArgumentException.ThrowIfNullOrWhiteSpace(entryKind);
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(providerSessionId);

        var executablePath = DirectRunHelperHostResolver.ResolveExecutablePath();
        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        if (string.Equals(Path.GetFileNameWithoutExtension(executablePath), "dotnet", StringComparison.OrdinalIgnoreCase))
        {
            startInfo.ArgumentList.Add(System.Reflection.Assembly.GetExecutingAssembly().Location);
        }

        startInfo.ArgumentList.Add(CommandName);
        startInfo.ArgumentList.Add(processId.ToString(CultureInfo.InvariantCulture));
        startInfo.ArgumentList.Add(providerEventLogPath);
        startInfo.ArgumentList.Add(executionUnit);
        startInfo.ArgumentList.Add(entryKind);
        startInfo.ArgumentList.Add(provider);
        startInfo.ArgumentList.Add(providerSessionId);
        startInfo.ArgumentList.Add(launchedAt.ToString("O"));

        return startInfo;
    }

    private static int Execute(DirectRunExitMonitorOptions options)
    {
        WaitForProcessExit(options.ProcessId);
        Thread.Sleep(ExitGracePeriod);

        if (DirectRunSessionBoundary.HasBackendExitEvent(options.ProviderEventLogPath, options.ProviderSessionId, options.LaunchedAt))
        {
            DirectRunTerminalArtifactUpdater.PersistTerminalRunStatusIfCurrent(
                options.ProviderEventLogPath,
                options.ProviderSessionId,
                options.LaunchedAt,
                exitCode: 1);
            return 0;
        }

        var eventWriter = new DirectRunProviderEventWriter(options.ProviderEventLogPath);
        eventWriter.Append(CreateBackendExitEvent(
            DateTimeOffset.UtcNow,
            options.ExecutionUnit,
            options.EntryKind,
            options.Provider,
            options.ProviderSessionId,
            1));
        DirectRunTerminalArtifactUpdater.PersistTerminalRunStatusIfCurrent(
            options.ProviderEventLogPath,
            options.ProviderSessionId,
            options.LaunchedAt,
            exitCode: 1);

        return 0;
    }

    private static void WaitForProcessExit(int processId)
    {
        while (IsProcessAlive(processId))
        {
            Thread.Sleep(PollInterval);
        }
    }

    private static bool IsProcessAlive(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            if (process.HasExited)
            {
                return false;
            }
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }

        if (OperatingSystem.IsWindows())
        {
            return true;
        }

        var processState = TryReadUnixProcessState(processId);
        if (string.IsNullOrWhiteSpace(processState))
        {
            return false;
        }

        return processState.IndexOf('Z') < 0;
    }

    private static string? TryReadUnixProcessState(int processId)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "/bin/ps",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                ArgumentList =
                {
                    "-o",
                    "stat=",
                    "-p",
                    processId.ToString(CultureInfo.InvariantCulture)
                }
            });

            if (process is null)
            {
                return null;
            }

            using (process)
            {
                var output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();
                return output.Trim();
            }
        }
        catch (Exception exception) when (
            exception is Win32Exception
            or InvalidOperationException)
        {
            return null;
        }
    }

    private static bool TryParseArguments(string[] args, out DirectRunExitMonitorOptions options)
    {
        options = null!;
        if (args.Length != 8 || !int.TryParse(args[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var processId))
        {
            return false;
        }

        if (processId <= 0
            || string.IsNullOrWhiteSpace(args[2])
            || string.IsNullOrWhiteSpace(args[3])
            || string.IsNullOrWhiteSpace(args[4])
            || string.IsNullOrWhiteSpace(args[5])
            || string.IsNullOrWhiteSpace(args[6])
            || !DirectRunSessionBoundary.TryParseLaunchedAt(args[7], out var launchedAt))
        {
            return false;
        }

        options = new DirectRunExitMonitorOptions(
            processId,
            args[2],
            args[3],
            args[4],
            args[5],
            args[6],
            launchedAt);
        return true;
    }

    private static DirectRunProviderEvent CreateBackendExitEvent(
        DateTimeOffset timestamp,
        string executionUnit,
        string entryKind,
        string provider,
        string providerSessionId,
        int exitCode)
    {
        return new DirectRunProviderEvent
        {
            Timestamp = timestamp.ToString("O"),
            ExecutionUnit = executionUnit,
            Provider = provider,
            EntryKind = entryKind,
            SessionId = providerSessionId,
            Kind = "provider-event",
            Payload = JsonSerializer.SerializeToElement(new
            {
                type = "backend-exit",
                exit_code = exitCode
            })
        };
    }

    private sealed record DirectRunExitMonitorOptions(
        int ProcessId,
        string ProviderEventLogPath,
        string ExecutionUnit,
        string EntryKind,
        string Provider,
        string ProviderSessionId,
        DateTimeOffset LaunchedAt);
}
