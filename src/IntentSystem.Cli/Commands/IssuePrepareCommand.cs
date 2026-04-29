using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G184: <c>intent-cli issue prepare --from-file &lt;body.md&gt;
/// --execution-unit &lt;id&gt; --title &lt;title&gt; --out &lt;packet.json&gt;</c>.
/// Validates a standalone Markdown child issue body via the G183 validator and
/// writes a deterministic reviewed publish packet on success. Refuses to write
/// any packet on failure. Never invokes AI providers; never mutates GitHub or
/// parent queue state.
/// </summary>
internal static class IssuePrepareCommand
{
    private static readonly JsonSerializerOptions PacketSerializerOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never
    };

    public static Func<DateTimeOffset> TimestampFactory { get; set; } = () => DateTimeOffset.UtcNow;

    public static int Execute(CliContext context, string[] args, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);

        if (!TryParseArguments(args, out var fromFile, out var executionUnit, out var title, out var outPath, out var error))
        {
            writer.WriteLine(error);
            return 1;
        }

        if (!File.Exists(fromFile))
        {
            writer.WriteLine($"--from-file path '{fromFile}' does not exist.");
            return 1;
        }

        var content = File.ReadAllText(fromFile);
        var validation = IssueValidateBodyValidator.Validate(fromFile, content);
        if (!validation.IsValid)
        {
            writer.WriteLine("Issue body failed validation; refusing to write packet.");
            if (validation.MissingHeadings.Count > 0)
            {
                writer.WriteLine("Missing required headings:");
                foreach (var heading in validation.MissingHeadings)
                {
                    writer.WriteLine($"- {heading}");
                }
            }

            if (validation.RelatedLinksInvalid)
            {
                writer.WriteLine($"Related Links: {validation.RelatedLinksReason ?? "invalid."}");
            }

            return 1;
        }

        var fileBytes = File.ReadAllBytes(fromFile);
        var sha256 = ComputeSha256Hex(fileBytes);
        var packet = new IssueReviewedPublishPacket
        {
            ExecutionUnit = executionUnit,
            Title = title,
            SourcePath = fromFile,
            SourceBodySha256 = sha256,
            ValidationStatus = "valid",
            Reviewed = true,
            Published = false,
            IssueNumber = null,
            IssueUrl = null,
            PreparedAtUtc = FormatUtcTimestamp(TimestampFactory()),
            PublishedAtUtc = null
        };

        var serialized = JsonSerializer.Serialize(packet, PacketSerializerOptions);
        var outDirectory = Path.GetDirectoryName(outPath);
        if (!string.IsNullOrEmpty(outDirectory))
        {
            Directory.CreateDirectory(outDirectory);
        }

        File.WriteAllText(outPath, serialized);

        writer.WriteLine($"Prepared reviewed publish packet for {executionUnit} at {outPath}");
        return 0;
    }

    private static bool TryParseArguments(
        string[] args,
        out string fromFile,
        out string executionUnit,
        out string title,
        out string outPath,
        out string error)
    {
        fromFile = string.Empty;
        executionUnit = string.Empty;
        title = string.Empty;
        outPath = string.Empty;
        error = string.Empty;

        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];
            switch (argument)
            {
                case "--from-file":
                    if (!TryReadValue(args, ref index, "--from-file", out var fromFileValue, out error))
                    {
                        return false;
                    }

                    fromFile = fromFileValue;
                    break;

                case "--execution-unit":
                    if (!TryReadValue(args, ref index, "--execution-unit", out var executionUnitValue, out error))
                    {
                        return false;
                    }

                    executionUnit = executionUnitValue;
                    break;

                case "--title":
                    if (!TryReadValue(args, ref index, "--title", out var titleValue, out error))
                    {
                        return false;
                    }

                    title = titleValue;
                    break;

                case "--out":
                    if (!TryReadValue(args, ref index, "--out", out var outValue, out error))
                    {
                        return false;
                    }

                    outPath = outValue;
                    break;

                default:
                    error = $"Unknown argument '{argument}'. Supported: --from-file, --execution-unit, --title, --out.";
                    return false;
            }
        }

        if (string.IsNullOrWhiteSpace(fromFile))
        {
            error = "--from-file is required.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(executionUnit))
        {
            error = "--execution-unit is required.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(title))
        {
            error = "--title is required.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(outPath))
        {
            error = "--out is required.";
            return false;
        }

        return true;
    }

    private static bool TryReadValue(string[] args, ref int index, string flag, out string value, out string error)
    {
        value = string.Empty;
        error = string.Empty;
        if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
        {
            error = $"{flag} requires a non-empty value.";
            return false;
        }

        value = args[index + 1];
        index++;
        return true;
    }

    private static string ComputeSha256Hex(byte[] bytes)
    {
        var hash = SHA256.HashData(bytes);
        var builder = new StringBuilder(hash.Length * 2);
        foreach (var b in hash)
        {
            builder.Append(b.ToString("x2", CultureInfo.InvariantCulture));
        }

        return builder.ToString();
    }

    internal static string FormatUtcTimestamp(DateTimeOffset timestamp)
    {
        return timestamp.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);
    }
}
