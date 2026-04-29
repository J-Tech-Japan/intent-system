using System.Text.Json;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G184: <c>intent-cli issue publish-reviewed --from-file &lt;packet.json&gt;
/// --repo &lt;owner/repo&gt;</c>. Reads a reviewed publish packet emitted by
/// <c>issue prepare</c>, refuses to mutate GitHub if the packet is missing,
/// invalid, unreviewed, already published, or points to a body that no longer
/// passes validation, and otherwise creates exactly one GitHub issue via
/// <c>gh issue create</c> (no <c>--label</c>) and records the issue number and
/// URL back into the packet.
/// </summary>
internal static class IssuePublishReviewedCommand
{
    private static readonly JsonSerializerOptions PacketSerializerOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never
    };

    public static Func<IGhIssueCreator> GhIssueCreatorFactory { get; set; } = () => new GhIssueCreator();

    public static Func<DateTimeOffset> TimestampFactory { get; set; } = () => DateTimeOffset.UtcNow;

    public static int Execute(CliContext context, string[] args, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);

        if (!TryParseArguments(args, out var fromFile, out var repo, out var error))
        {
            writer.WriteLine(error);
            return 1;
        }

        if (!File.Exists(fromFile))
        {
            writer.WriteLine($"--from-file path '{fromFile}' does not exist.");
            return 1;
        }

        IssueReviewedPublishPacket? packet;
        try
        {
            packet = JsonSerializer.Deserialize<IssueReviewedPublishPacket>(File.ReadAllText(fromFile), PacketSerializerOptions);
        }
        catch (JsonException exception)
        {
            writer.WriteLine($"Failed to parse reviewed publish packet: {exception.Message}");
            return 1;
        }

        if (packet is null)
        {
            writer.WriteLine("Reviewed publish packet is empty.");
            return 1;
        }

        if (!packet.Reviewed)
        {
            writer.WriteLine("missing reviewed marker");
            return 1;
        }

        if (!string.Equals(packet.ValidationStatus, "valid", StringComparison.Ordinal))
        {
            writer.WriteLine("validation status is not valid");
            return 1;
        }

        if (packet.Published)
        {
            writer.WriteLine("packet is already published");
            return 1;
        }

        if (!File.Exists(packet.SourcePath))
        {
            writer.WriteLine("source body no longer passes validation");
            return 1;
        }

        var sourceContent = File.ReadAllText(packet.SourcePath);
        var validation = IssueValidateBodyValidator.Validate(packet.SourcePath, sourceContent);
        if (!validation.IsValid)
        {
            writer.WriteLine("source body no longer passes validation");
            return 1;
        }

        var creator = GhIssueCreatorFactory();
        string rawUrl;
        try
        {
            rawUrl = creator.CreateIssue(repo, packet.Title, packet.SourcePath);
        }
        catch (Exception exception) when (exception is InvalidOperationException || exception is IOException)
        {
            writer.WriteLine($"gh issue create failed: {exception.Message}");
            return 1;
        }

        var issueUrl = rawUrl.Trim();
        if (!TryExtractIssueNumber(issueUrl, out var issueNumber))
        {
            writer.WriteLine($"Could not parse issue number from gh output: '{issueUrl}'");
            return 1;
        }

        var updated = packet with
        {
            Published = true,
            IssueNumber = issueNumber,
            IssueUrl = issueUrl,
            PublishedAtUtc = IssuePrepareCommand.FormatUtcTimestamp(TimestampFactory())
        };

        File.WriteAllText(fromFile, JsonSerializer.Serialize(updated, PacketSerializerOptions));
        writer.WriteLine($"Published {updated.ExecutionUnit} as {issueUrl}");
        return 0;
    }

    private static bool TryParseArguments(
        string[] args,
        out string fromFile,
        out string repo,
        out string error)
    {
        fromFile = string.Empty;
        repo = string.Empty;
        error = string.Empty;

        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];
            switch (argument)
            {
                case "--from-file":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--from-file requires a non-empty value.";
                        return false;
                    }

                    fromFile = args[index + 1];
                    index++;
                    break;

                case "--repo":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--repo requires a non-empty value.";
                        return false;
                    }

                    repo = args[index + 1];
                    index++;
                    break;

                default:
                    error = $"Unknown argument '{argument}'. Supported: --from-file, --repo.";
                    return false;
            }
        }

        if (string.IsNullOrWhiteSpace(fromFile))
        {
            error = "--from-file is required.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(repo))
        {
            error = "--repo is required.";
            return false;
        }

        return true;
    }

    private static bool TryExtractIssueNumber(string url, out int issueNumber)
    {
        issueNumber = 0;
        if (string.IsNullOrWhiteSpace(url))
        {
            return false;
        }

        var trimmed = url.TrimEnd('/');
        var slashIndex = trimmed.LastIndexOf('/');
        if (slashIndex < 0 || slashIndex == trimmed.Length - 1)
        {
            return false;
        }

        var tail = trimmed[(slashIndex + 1)..];
        return int.TryParse(tail, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out issueNumber)
            && issueNumber > 0;
    }
}
