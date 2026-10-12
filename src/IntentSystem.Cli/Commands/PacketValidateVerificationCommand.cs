using System.Text.Json;

namespace IntentSystem.Cli.Commands;

/// <summary>Read-only structural validation of an explicitly authored packet verification contract.</summary>
internal static class PacketValidateVerificationCommand
{
    private const string Usage = "Usage: intent-cli packet validate-verification --execution-unit <unit> --format json|markdown";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    internal static int Execute(CliContext context, string[] args, TextWriter writer) => Execute(context.RepoRoot, args, writer);

    internal static int Execute(string repoRoot, string[] args, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);
        if (args.Length == 1 && args[0] == "--help")
        {
            writer.WriteLine(Usage);
            writer.WriteLine("Reports only structural coverage in an explicitly authored verification contract. It never runs commands or verifies evidence, publication, or authority.");
            return 0;
        }

        var supportedFormat = FindSupportedFormat(args);
        var firstSuppliedUnit = FindFirstSuppliedUnit(args) ?? string.Empty;
        string? unit = null;
        string? format = null;
        string? parseError = null;
        for (var index = 0; index < args.Length; index++)
        {
            var option = args[index];
            if (option is not ("--execution-unit" or "--format"))
            {
                parseError = $"Unknown argument '{option}'.";
                break;
            }
            if (index + 1 >= args.Length || args[index + 1].StartsWith("--", StringComparison.Ordinal))
            {
                parseError = $"Missing value for '{option}'.";
                break;
            }

            var value = args[++index];
            if (option == "--execution-unit")
            {
                if (unit is not null) { parseError = "Duplicate argument '--execution-unit'."; break; }
                unit = value;
            }
            else
            {
                if (format is not null) { parseError = "Duplicate argument '--format'."; break; }
                format = value;
            }
        }

        var outputFormat = format is "json" or "markdown" ? format : supportedFormat;
        if (outputFormat is not ("json" or "markdown"))
        {
            writer.WriteLine(parseError ?? "Format must be json or markdown.");
            writer.WriteLine(Usage);
            return 1;
        }
        if (parseError is not null || unit is null)
        {
            var detail = parseError ?? "--execution-unit is required.";
            Emit(writer, outputFormat, PacketVerificationContract.Invalid(firstSuppliedUnit, null, null, "invalid-arguments", detail));
            return 1;
        }
        if (!RulingArtifact.TryIdentifier(unit, out var identityError))
        {
            Emit(writer, outputFormat, PacketVerificationContract.Invalid(unit, null, null, "invalid-identity", identityError));
            return 1;
        }

        var relativePacket = $".intent-cli/issues/{unit}/packet.yaml";
        var absoluteRoot = Path.GetFullPath(repoRoot);
        var directories = new[]
        {
            (Absolute: absoluteRoot, Relative: "."),
            (Absolute: Path.Combine(absoluteRoot, ".intent-cli"), Relative: ".intent-cli"),
            (Absolute: Path.Combine(absoluteRoot, ".intent-cli", "issues"), Relative: ".intent-cli/issues"),
            (Absolute: Path.Combine(absoluteRoot, ".intent-cli", "issues", unit), Relative: $".intent-cli/issues/{unit}"),
        };
        foreach (var directory in directories)
        {
            if (TryCheckDirectory(directory.Absolute, out var failure, out var detail)) continue;
            var unavailable = PacketVerificationContract.Unavailable(unit, relativePacket, null, "packet-read-failure",
                $"{directory.Relative}: {failure}: {detail}");
            Emit(writer, outputFormat, unavailable);
            return 1;
        }

        var packetFile = Path.Combine(absoluteRoot, ".intent-cli", "issues", unit, "packet.yaml");
        if (!CrossRuntimeReviewFileMode.TryReadRegularFileBytes(packetFile, PacketVerificationContract.MaximumBytes,
                out var bytes, out var readFailure, out var readError))
        {
            var diagnosticCause = readFailure == CrossRuntimeReviewFileReadFailure.TooLarge ? "resource-limit" : "packet-read-failure";
            var unavailable = PacketVerificationContract.Unavailable(unit, relativePacket, null, diagnosticCause,
                $"{readFailure}: {readError}");
            Emit(writer, outputFormat, unavailable);
            return 1;
        }

        var result = PacketVerificationContract.Evaluate(unit, relativePacket, bytes);
        Emit(writer, outputFormat, result);
        return result.State is "not-declared" or "complete" ? 0 : 1;
    }

    private static bool TryCheckDirectory(string path, out string failure, out string detail)
    {
        failure = "missing";
        detail = "directory does not exist";
        try
        {
            var attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                failure = "unsafe-path";
                detail = "directory is a symlink or reparse point";
                return false;
            }
            if ((attributes & FileAttributes.Directory) == 0)
            {
                failure = "not-directory";
                detail = "path is not a directory";
                return false;
            }
            // Checking only the directory bit lets search-only directories pass
            // even though their contents cannot be safely inspected. Probe a
            // single entry so this remains bounded and performs no packet read.
            using (var entries = Directory.EnumerateFileSystemEntries(path).GetEnumerator())
                _ = entries.MoveNext();
            return true;
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            return false;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            failure = "read-error";
            detail = exception.Message;
            return false;
        }
    }

    private static string? FindSupportedFormat(string[] args)
    {
        for (var index = 0; index + 1 < args.Length; index++)
            if (args[index] == "--format" && args[index + 1] is "json" or "markdown") return args[index + 1];
        return null;
    }

    private static string? FindFirstSuppliedUnit(string[] args)
    {
        for (var index = 0; index + 1 < args.Length; index++)
        {
            if (args[index] == "--execution-unit" && !args[index + 1].StartsWith("--", StringComparison.Ordinal))
                return args[index + 1];
        }
        return null;
    }

    internal static void Emit(TextWriter writer, string format, PacketVerificationContract.Result result)
    {
        var json = JsonSerializer.Serialize(result, JsonOptions);
        if (format == "json")
        {
            writer.WriteLine(json);
            return;
        }

        if (RulingArtifact.TryIdentifier(result.ExecutionUnit, out _))
            writer.WriteLine($"# Packet verification contract: {result.ExecutionUnit}");
        else
        {
            writer.WriteLine("# Packet verification contract");
            writer.WriteLine();
            writer.WriteLine("- Requested execution unit:");
            WriteFencedValue(writer, "text", result.ExecutionUnit);
        }
        writer.WriteLine();
        writer.WriteLine($"- State: {result.State}");
        writer.WriteLine($"- Cause: {result.Cause}");
        writer.WriteLine("- Detail:");
        WriteFencedValue(writer, "text", result.Detail);
        writer.WriteLine($"- Packet: {result.PacketPath ?? "not resolved"}");
        writer.WriteLine($"- SHA-256: {result.PacketSha256 ?? "not read"}");
        writer.WriteLine($"- Execution: {result.Execution}");
        writer.WriteLine();
        writer.WriteLine("Structural coverage only: this report does not observe command execution or verify evidence, publication, or authority.");
        writer.WriteLine();
        writer.WriteLine("## Structured result (same facts as JSON)");
        writer.WriteLine();
        WriteFencedValue(writer, "json", json);
    }

    private static void WriteFencedValue(TextWriter writer, string language, string value)
    {
        var fence = new string('`', Math.Max(3, LongestBacktickRun(value) + 1));
        writer.WriteLine(fence + language);
        writer.WriteLine(value);
        writer.WriteLine(fence);
    }

    private static int LongestBacktickRun(string value)
    {
        var longest = 0;
        var current = 0;
        foreach (var character in value)
        {
            if (character == '`') { current++; longest = Math.Max(longest, current); }
            else current = 0;
        }
        return longest;
    }

}
