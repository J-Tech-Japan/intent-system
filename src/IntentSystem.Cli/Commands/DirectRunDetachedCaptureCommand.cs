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
    private static readonly TimeSpan StandardInputProgressObservationWindow = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan StandardInputProgressPollInterval = TimeSpan.FromMilliseconds(100);

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
        StreamWriter? preservedStandardInput = null;
        Task? stdoutPump = null;
        Task? stderrPump = null;
        var providerSessionId = string.Empty;
        var exitedEarly = false;

        try
        {
            var startInfo = CreateProviderStartInfo(options);
            var closePreservedStandardInputAfterLaunch = ShouldClosePreservedStandardInputAfterLaunch(
                startInfo.FileName,
                options);
            var delayClosingPreservedStandardInputAfterLaunch = ShouldDelayClosingPreservedStandardInputAfterLaunch(
                startInfo.FileName,
                options);

            process = Process.Start(startInfo)
                ?? throw new InvalidOperationException($"Failed to start detached direct run process '{options.Command}'.");
            if (startInfo.RedirectStandardInput)
            {
                preservedStandardInput = process.StandardInput;
                preservedStandardInput.AutoFlush = true;
                if (closePreservedStandardInputAfterLaunch)
                {
                    // macOS `script` can keep the wrapper session alive after the real provider exits
                    // while this helper-owned stdin pipe remains open, which suppresses terminal events.
                    preservedStandardInput.Dispose();
                    preservedStandardInput = null;
                }
                else if (delayClosingPreservedStandardInputAfterLaunch)
                {
                }
            }
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

            if (delayClosingPreservedStandardInputAfterLaunch
                && preservedStandardInput is not null)
            {
                if (string.Equals(options.EntryKind, "implement", StringComparison.Ordinal))
                {
                    ScheduleImplementStandardInputClosure(
                        preservedStandardInput,
                        options.ProviderEventLogPath,
                        providerSessionId,
                        options.LaunchedAt);
                }
                else
                {
                    ScheduleFixStandardInputClosure(
                        preservedStandardInput,
                        options.ProviderEventLogPath,
                        providerSessionId,
                        options.LaunchedAt);
                }
            }

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

            preservedStandardInput?.Dispose();
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
            DirectRunTerminalArtifactUpdater.PersistTerminalRunStatusIfCurrent(
                options.ProviderEventLogPath,
                providerSessionId,
                options.LaunchedAt,
                exitCode);
            return;
        }

        writer.Append(DirectRunProviderEventFactory.CreateBackendExitEvent(
            DateTimeOffset.UtcNow,
            options.ExecutionUnit,
            options.EntryKind,
            options.Provider,
            providerSessionId,
            exitCode));
        DirectRunTerminalArtifactUpdater.PersistTerminalRunStatusIfCurrent(
            options.ProviderEventLogPath,
            providerSessionId,
            options.LaunchedAt,
            exitCode);
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
        var providerInvocation = ResolveProviderInvocation(options);
        var preserveStandardInput = ShouldPreserveProviderStandardInput(options);
        var startInfo = new ProcessStartInfo
        {
            WorkingDirectory = options.WorkingDirectory,
            UseShellExecute = false,
            RedirectStandardInput = preserveStandardInput,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        startInfo.FileName = providerInvocation.FileName;

        foreach (var argument in providerInvocation.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return startInfo;
    }

    private static ResolvedProviderInvocation ResolveProviderInvocation(DirectRunDetachedCaptureOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var scriptExecutable = TryResolveScriptExecutable(options);
        if (string.IsNullOrWhiteSpace(scriptExecutable))
        {
            return new ResolvedProviderInvocation(options.Command, options.Arguments);
        }

        if (OperatingSystem.IsMacOS())
        {
            return new ResolvedProviderInvocation(
                scriptExecutable,
                ["-q", "/dev/null", options.Command, .. options.Arguments]);
        }

        return new ResolvedProviderInvocation(
            scriptExecutable,
            ["-q", "-e", "-c", CreateShellCommand(options.Command, options.Arguments), "/dev/null"]);
    }

    private static bool ShouldPreserveProviderStandardInput(DirectRunDetachedCaptureOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (string.Equals(options.EntryKind, "review", StringComparison.Ordinal))
        {
            return false;
        }

        return string.Equals(options.Provider, "codex", StringComparison.OrdinalIgnoreCase)
            || IsCodexLikeCommand(options.Command);
    }

    private static bool ShouldClosePreservedStandardInputAfterLaunch(
        string providerExecutablePath,
        DirectRunDetachedCaptureOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerExecutablePath);
        ArgumentNullException.ThrowIfNull(options);

        if (string.Equals(options.EntryKind, "review", StringComparison.Ordinal))
        {
            return false;
        }

        if (string.Equals(options.EntryKind, "fix", StringComparison.Ordinal))
        {
            return false;
        }

        if (string.Equals(options.EntryKind, "implement", StringComparison.Ordinal))
        {
            return false;
        }

        var executableName = Path.GetFileNameWithoutExtension(providerExecutablePath.Trim());
        return string.Equals(executableName, "script", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ShouldDelayClosingPreservedStandardInputAfterLaunch(
        string providerExecutablePath,
        DirectRunDetachedCaptureOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerExecutablePath);
        ArgumentNullException.ThrowIfNull(options);

        if (!string.Equals(options.EntryKind, "fix", StringComparison.Ordinal)
            && !string.Equals(options.EntryKind, "implement", StringComparison.Ordinal))
        {
            return false;
        }

        var executableName = Path.GetFileNameWithoutExtension(providerExecutablePath.Trim());
        return string.Equals(executableName, "script", StringComparison.OrdinalIgnoreCase);
    }

    private static void ScheduleImplementStandardInputClosure(
        StreamWriter preservedStandardInput,
        string providerEventLogPath,
        string providerSessionId,
        DateTimeOffset launchedAt)
    {
        ArgumentNullException.ThrowIfNull(preservedStandardInput);
        ArgumentException.ThrowIfNullOrWhiteSpace(providerEventLogPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(providerSessionId);

        _ = Task.Run(async () =>
        {
            var deadline = DateTimeOffset.UtcNow + StandardInputProgressObservationWindow;
            try
            {
                while (DateTimeOffset.UtcNow < deadline)
                {
                    if (HasImplementProgressSignal(providerEventLogPath, providerSessionId, launchedAt)
                        || DirectRunSessionBoundary.HasBackendExitEvent(providerEventLogPath, providerSessionId, launchedAt))
                    {
                        break;
                    }

                    await Task.Delay(StandardInputProgressPollInterval).ConfigureAwait(false);
                }

                preservedStandardInput.Dispose();
            }
            catch (ObjectDisposedException)
            {
            }
            catch (InvalidOperationException)
            {
            }
        });
    }

    private static void ScheduleFixStandardInputClosure(
        StreamWriter preservedStandardInput,
        string providerEventLogPath,
        string providerSessionId,
        DateTimeOffset launchedAt)
    {
        ArgumentNullException.ThrowIfNull(preservedStandardInput);
        ArgumentException.ThrowIfNullOrWhiteSpace(providerEventLogPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(providerSessionId);

        _ = Task.Run(async () =>
        {
            var deadline = DateTimeOffset.UtcNow + StandardInputProgressObservationWindow;
            try
            {
                while (DateTimeOffset.UtcNow < deadline)
                {
                    if (HasFixPlanningProgressSignal(providerEventLogPath, providerSessionId, launchedAt)
                        || DirectRunSessionBoundary.HasBackendExitEvent(providerEventLogPath, providerSessionId, launchedAt))
                    {
                        break;
                    }

                    await Task.Delay(StandardInputProgressPollInterval).ConfigureAwait(false);
                }

                preservedStandardInput.Dispose();
            }
            catch (ObjectDisposedException)
            {
            }
            catch (InvalidOperationException)
            {
            }
        });
    }

    private static bool HasImplementProgressSignal(
        string providerEventLogPath,
        string providerSessionId,
        DateTimeOffset launchedAt)
    {
        if (!File.Exists(providerEventLogPath))
        {
            return false;
        }

        try
        {
            var providerEvents = DirectRunProviderEventJsonl.DeserializeAll(File.ReadAllText(providerEventLogPath));
            var currentProviderEvents = DirectRunSessionBoundary.SelectEvents(providerEvents, providerSessionId, launchedAt);
            return DirectRunFixOutcomeSupport.HasBoundedProgressSignal(currentProviderEvents);
        }
        catch (Exception exception) when (
            exception is IOException
            or InvalidOperationException
            or ArgumentException
            or System.Text.Json.JsonException)
        {
            return false;
        }
    }

    private static bool HasFixPlanningProgressSignal(
        string providerEventLogPath,
        string providerSessionId,
        DateTimeOffset launchedAt)
    {
        if (!File.Exists(providerEventLogPath))
        {
            return false;
        }

        try
        {
            var providerEvents = DirectRunProviderEventJsonl.DeserializeAll(File.ReadAllText(providerEventLogPath));
            var currentProviderEvents = DirectRunSessionBoundary.SelectEvents(providerEvents, providerSessionId, launchedAt);
            return DirectRunFixOutcomeSupport.HasPlanningProgressSignalBeyondInitialInventory(currentProviderEvents);
        }
        catch (Exception exception) when (
            exception is IOException
            or InvalidOperationException
            or ArgumentException
            or System.Text.Json.JsonException)
        {
            return false;
        }
    }

    private static string? TryResolveScriptExecutable(DirectRunDetachedCaptureOptions options)
    {
        if (OperatingSystem.IsWindows()
            || string.Equals(options.EntryKind, "review", StringComparison.Ordinal)
            || (!string.Equals(options.Provider, "codex", StringComparison.OrdinalIgnoreCase)
                && !IsCodexLikeCommand(options.Command)))
        {
            return null;
        }

        const string macOsScriptPath = "/usr/bin/script";
        if (OperatingSystem.IsMacOS())
        {
            return File.Exists(macOsScriptPath) ? macOsScriptPath : null;
        }

        return File.Exists(macOsScriptPath) ? macOsScriptPath : "script";
    }

    private static bool IsCodexLikeCommand(string command)
    {
        var fileName = Path.GetFileName(command.Trim());
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return false;
        }

        var commandStem = Path.GetFileNameWithoutExtension(fileName);
        return commandStem.StartsWith("codex", StringComparison.OrdinalIgnoreCase);
    }

    private static string CreateShellCommand(string command, IReadOnlyList<string> arguments)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command);
        ArgumentNullException.ThrowIfNull(arguments);

        return string.Join(
            " ",
            new[] { command }.Concat(arguments).Select(QuoteShellArgument));
    }

    private static string QuoteShellArgument(string argument)
    {
        ArgumentNullException.ThrowIfNull(argument);
        return "'" + argument.Replace("'", "'\"'\"'", StringComparison.Ordinal) + "'";
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

    private sealed record ResolvedProviderInvocation(
        string FileName,
        IReadOnlyList<string> Arguments);
}
