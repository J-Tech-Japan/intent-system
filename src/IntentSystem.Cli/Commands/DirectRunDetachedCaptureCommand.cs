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
        try
        {
            var startInfo = CreateProviderStartInfo(options);

            using var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException($"Failed to start detached direct run process '{options.Command}'.");
            var providerSessionId = $"pid:{Environment.ProcessId}";
            writer.Append(DirectRunProviderEventFactory.CreateSessionMetadataEvent(
                options.LaunchedAt,
                options.ExecutionUnit,
                options.EntryKind,
                options.Provider,
                providerSessionId,
                options.Model,
                options.Transport,
                options.Command));

            process.OutputDataReceived += (_, eventArgs) =>
            {
                if (!string.IsNullOrEmpty(eventArgs.Data))
                {
                    writer.Append(DirectRunProviderEventFactory.CreateProviderEvent(
                        DateTimeOffset.UtcNow,
                        options.ExecutionUnit,
                        options.EntryKind,
                        options.Provider,
                        providerSessionId,
                        NormalizeCapturedLine(eventArgs.Data)));
                }
            };
            process.ErrorDataReceived += (_, eventArgs) =>
            {
                if (!string.IsNullOrEmpty(eventArgs.Data))
                {
                    writer.Append(DirectRunProviderEventFactory.CreateProviderEvent(
                        DateTimeOffset.UtcNow,
                        options.ExecutionUnit,
                        options.EntryKind,
                        options.Provider,
                        providerSessionId,
                        NormalizeCapturedLine(eventArgs.Data)));
                }
            };
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            Console.Out.WriteLine(providerSessionId);
            Console.Out.Flush();

            var exitedEarly = process.WaitForExit((int)StartupSuccessWindow.TotalMilliseconds);
            process.WaitForExit();
            AppendBackendExitIfMissing(writer, options, providerSessionId, process.ExitCode);

            return exitedEarly && process.ExitCode != 0
                ? process.ExitCode
                : 0;
        }
        catch (Win32Exception exception)
        {
            TryWriteStartupError(exception.Message);
            return 1;
        }
        catch (InvalidOperationException exception)
        {
            TryWriteStartupError(exception.Message);
            return 1;
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

    private static void TryWriteStartupError(string message)
    {
        try
        {
            Console.Out.WriteLine($"error:{message}");
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

        if (ShouldWrapWithPseudoTerminal(options.Provider, options.Command))
        {
            startInfo.FileName = "/usr/bin/script";
            startInfo.ArgumentList.Add("-q");
            startInfo.ArgumentList.Add("/dev/null");
            startInfo.ArgumentList.Add(options.Command);
        }
        else
        {
            startInfo.FileName = options.Command;
        }

        foreach (var argument in options.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return startInfo;
    }

    private static bool ShouldWrapWithPseudoTerminal(string provider, string command)
    {
        if (OperatingSystem.IsWindows() || !File.Exists("/usr/bin/script"))
        {
            return false;
        }

        return string.Equals(provider, "codex", StringComparison.OrdinalIgnoreCase)
            || IsCodexLikeCommand(command);
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
