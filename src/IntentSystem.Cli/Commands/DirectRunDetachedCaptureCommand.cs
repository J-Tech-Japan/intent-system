using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Text;

namespace IntentSystem.Cli.Commands;

internal static class DirectRunDetachedCaptureCommand
{
    internal const string CommandName = "__direct-run-detached-capture";
    private static readonly TimeSpan StartupSuccessWindow = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan ExitPollInterval = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan OutputDrainGracePeriod = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan ExitCodeResolutionGracePeriod = TimeSpan.FromSeconds(3);

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
                $"Usage: {CommandName} <provider_log_path> <execution_unit> <entry_kind> <provider> <model> <transport> <launched_at> <working_directory> <command> [args...]");
            exitCode = 1;
            return true;
        }

        exitCode = Execute(options);
        return true;
    }

    public static ProcessStartInfo CreateStartInfo(
        string providerEventLogPath,
        string executionUnit,
        string entryKind,
        string provider,
        string model,
        string transport,
        DateTimeOffset launchedAt,
        string workingDirectory,
        string command,
        IReadOnlyList<string> arguments)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerEventLogPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(executionUnit);
        ArgumentException.ThrowIfNullOrWhiteSpace(entryKind);
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        ArgumentException.ThrowIfNullOrWhiteSpace(transport);
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(command);
        ArgumentNullException.ThrowIfNull(arguments);

        var executablePath = DirectRunHelperHostResolver.ResolveExecutablePath();
        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardInput = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        if (string.Equals(Path.GetFileNameWithoutExtension(executablePath), "dotnet", StringComparison.OrdinalIgnoreCase))
        {
            startInfo.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
        }

        startInfo.ArgumentList.Add(CommandName);
        startInfo.ArgumentList.Add(providerEventLogPath);
        startInfo.ArgumentList.Add(executionUnit);
        startInfo.ArgumentList.Add(entryKind);
        startInfo.ArgumentList.Add(provider);
        startInfo.ArgumentList.Add(model);
        startInfo.ArgumentList.Add(transport);
        startInfo.ArgumentList.Add(launchedAt.ToString("O"));
        startInfo.ArgumentList.Add(workingDirectory);
        startInfo.ArgumentList.Add(command);
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return startInfo;
    }

    private static int Execute(DirectRunDetachedCaptureOptions options)
    {
        var writer = new DirectRunProviderEventWriter(options.ProviderEventLogPath);
        Process? process = null;
        Task? stdoutPump = null;
        Task? stderrPump = null;
        var providerSessionId = string.Empty;
        var exitedEarly = false;

        try
        {
            var startInfo = CreateProviderStartInfo(options);

            process = Process.Start(startInfo)
                ?? throw new InvalidOperationException($"Failed to start detached direct run process '{options.Command}'.");
            providerSessionId = $"pid:{process.Id}";
            TryWriteSessionId(providerSessionId);
            Console.SetOut(TextWriter.Null);
            Console.SetError(TextWriter.Null);
            writer.Append(DirectRunProviderEventFactory.CreateSessionMetadataEvent(
                options.LaunchedAt,
                options.ExecutionUnit,
                options.EntryKind,
                options.Provider,
                providerSessionId,
                options.Model,
                options.Transport,
                options.Command));
            StartProviderExitMonitorIfPossible(
                process.Id,
                options.ProviderEventLogPath,
                options.ExecutionUnit,
                options.EntryKind,
                options.Provider,
                providerSessionId,
                options.LaunchedAt);

            stdoutPump = StartOutputPump(
                process.StandardOutput,
                writer,
                options,
                providerSessionId);
            stderrPump = StartOutputPump(
                process.StandardError,
                writer,
                options,
                providerSessionId);

            exitedEarly = process.WaitForExit((int)StartupSuccessWindow.TotalMilliseconds);
            WaitForProcessExit(process);
            var exitCode = ResolveExitCode(process, exitedEarly);
            AppendBackendExitIfMissing(writer, options, providerSessionId, exitCode);
            TryCompleteOutputPumps(stdoutPump, stderrPump);

            return exitedEarly && exitCode != 0
                ? exitCode
                : 0;
        }
        catch (Win32Exception)
        {
            return 1;
        }
        catch (InvalidOperationException)
        {
            return 1;
        }
        finally
        {
            if (process is not null)
            {
                try
                {
                    if (!string.IsNullOrWhiteSpace(providerSessionId))
                    {
                        AppendBackendExitIfMissing(
                            writer,
                            options,
                            providerSessionId,
                            ResolveExitCode(process, exitedEarly));
                    }
                }
                catch (InvalidOperationException)
                {
                }

                if (stdoutPump is not null && stderrPump is not null)
                {
                    TryCompleteOutputPumps(stdoutPump, stderrPump);
                }

                process.Dispose();
            }
        }
    }

    private static void AppendBackendExitIfMissing(
        DirectRunProviderEventWriter writer,
        DirectRunDetachedCaptureOptions options,
        string providerSessionId,
        int exitCode)
    {
        if (DirectRunSessionBoundary.HasBackendExitEvent(options.ProviderEventLogPath, providerSessionId, options.LaunchedAt))
        {
            return;
        }

        writer.Append(DirectRunProviderEventFactory.CreateBackendExitEvent(
            DateTimeOffset.UtcNow,
            options.ExecutionUnit,
            options.EntryKind,
            options.Provider,
            providerSessionId,
            exitCode));
    }

    private static void StartProviderExitMonitorIfPossible(
        int processId,
        string providerEventLogPath,
        string executionUnit,
        string entryKind,
        string provider,
        string providerSessionId,
        DateTimeOffset launchedAt)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var monitorStartInfo = DirectRunExitMonitorCommand.CreateDetachedStartInfo(
            processId,
            providerEventLogPath,
            executionUnit,
            entryKind,
            provider,
            providerSessionId,
            launchedAt);

        var launcherStartInfo = new ProcessStartInfo
        {
            FileName = "/bin/sh",
            UseShellExecute = false,
            RedirectStandardOutput = false,
            RedirectStandardError = false
        };
        launcherStartInfo.ArgumentList.Add("-c");
        launcherStartInfo.ArgumentList.Add(
            """
            if command -v nohup >/dev/null 2>&1; then
                nohup "$@" >/dev/null 2>&1 </dev/null &
            else
                "$@" >/dev/null 2>&1 </dev/null &
            fi
            """);
        launcherStartInfo.ArgumentList.Add("direct-run-exit-monitor-launcher");
        launcherStartInfo.ArgumentList.Add(monitorStartInfo.FileName);
        foreach (var argument in monitorStartInfo.ArgumentList)
        {
            launcherStartInfo.ArgumentList.Add(argument);
        }

        using var launcher = Process.Start(launcherStartInfo);
    }

    private static void WaitForProcessExit(Process process)
    {
        while (IsProcessAlive(process))
        {
            Thread.Sleep(ExitPollInterval);
        }
    }

    private static int ResolveExitCode(Process process, bool exitedEarly)
    {
        try
        {
            process.WaitForExit((int)ExitCodeResolutionGracePeriod.TotalMilliseconds);
            process.Refresh();
            return process.ExitCode;
        }
        catch (InvalidOperationException)
        {
            return exitedEarly ? 1 : 0;
        }
    }

    private static Task StartOutputPump(
        StreamReader reader,
        DirectRunProviderEventWriter writer,
        DirectRunDetachedCaptureOptions options,
        string providerSessionId)
    {
        return Task.Run(() =>
        {
            while (true)
            {
                string? line;
                try
                {
                    line = reader.ReadLine();
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (InvalidOperationException)
                {
                    break;
                }

                if (line is null)
                {
                    break;
                }

                var normalizedLine = NormalizeCapturedLine(line);
                if (string.IsNullOrEmpty(normalizedLine))
                {
                    continue;
                }

                writer.Append(DirectRunProviderEventFactory.CreateProviderEvent(
                    DateTimeOffset.UtcNow,
                    options.ExecutionUnit,
                    options.EntryKind,
                    options.Provider,
                    providerSessionId,
                    normalizedLine));
            }
        });
    }

    private static void TryCompleteOutputPumps(params Task[] pumps)
    {
        try
        {
            Task.WaitAll(pumps, OutputDrainGracePeriod);
        }
        catch (AggregateException)
        {
        }
    }

    private static bool IsProcessAlive(Process process)
    {
        try
        {
            process.Refresh();
            if (process.HasExited)
            {
                return false;
            }
        }
        catch (InvalidOperationException)
        {
            return false;
        }

        if (OperatingSystem.IsWindows())
        {
            return true;
        }

        var processState = TryReadUnixProcessState(process.Id);
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

    private static void TryWriteSessionId(string providerSessionId)
    {
        try
        {
            Console.Out.WriteLine(providerSessionId);
            Console.Out.Flush();
        }
        catch
        {
        }
    }

    private static ProcessStartInfo CreateProviderStartInfo(DirectRunDetachedCaptureOptions options)
    {
        var startInfo = new ProcessStartInfo
        {
            WorkingDirectory = options.WorkingDirectory,
            UseShellExecute = false,
            RedirectStandardInput = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        startInfo.FileName = options.Command;

        foreach (var argument in options.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return startInfo;
    }

    private static string NormalizeCapturedLine(string line)
    {
        var builder = new StringBuilder(line.Length);
        foreach (var character in line)
        {
            if (character == '\t' || character >= ' ')
            {
                builder.Append(character);
            }
        }

        var normalized = builder.ToString();
        while (normalized.StartsWith("^D", StringComparison.Ordinal))
        {
            normalized = normalized[2..];
        }

        return normalized;
    }

    private static bool TryParseArguments(string[] args, out DirectRunDetachedCaptureOptions options)
    {
        options = null!;
        if (args.Length < 10
            || string.IsNullOrWhiteSpace(args[1])
            || string.IsNullOrWhiteSpace(args[2])
            || string.IsNullOrWhiteSpace(args[3])
            || string.IsNullOrWhiteSpace(args[4])
            || string.IsNullOrWhiteSpace(args[5])
            || string.IsNullOrWhiteSpace(args[6])
            || string.IsNullOrWhiteSpace(args[7])
            || string.IsNullOrWhiteSpace(args[8])
            || string.IsNullOrWhiteSpace(args[9])
            || !DateTimeOffset.TryParse(args[7], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var launchedAt))
        {
            return false;
        }

        options = new DirectRunDetachedCaptureOptions(
            args[1],
            args[2],
            args[3],
            args[4],
            args[5],
            args[6],
            launchedAt,
            args[8],
            args[9],
            args.Length == 10 ? [] : args[10..]);
        return true;
    }

    private sealed record DirectRunDetachedCaptureOptions(
        string ProviderEventLogPath,
        string ExecutionUnit,
        string EntryKind,
        string Provider,
        string Model,
        string Transport,
        DateTimeOffset LaunchedAt,
        string WorkingDirectory,
        string Command,
        IReadOnlyList<string> Arguments);
}
