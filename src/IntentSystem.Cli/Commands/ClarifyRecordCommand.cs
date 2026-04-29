namespace IntentSystem.Cli.Commands;

/// <summary>
/// G182: <c>intent-cli clarify record --domain &lt;name&gt; --from-file &lt;path&gt;
/// [--dry-run]</c>. Records an owner-approved clarification decision into
/// <c>intents/&lt;domain&gt;/clarifications/open.md</c> under <c>## Recently
/// Resolved</c>. Validates required fields before mutation; <c>--dry-run</c>
/// prints the intended update and does not write. Never picks or rewrites the
/// owner answer; never touches files outside the resolved clarification return
/// path.
/// </summary>
internal static class ClarifyRecordCommand
{
    public static int Execute(CliContext context, string[] args, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);

        if (!TryParseArguments(args, out var domainOverride, out var fromFile, out var dryRun, out var error))
        {
            writer.WriteLine(error);
            return 1;
        }

        var domain = string.IsNullOrWhiteSpace(domainOverride)
            ? context.Config.Project.Domain
            : domainOverride;

        if (string.IsNullOrWhiteSpace(domain))
        {
            writer.WriteLine("Domain could not be resolved. Provide --domain or set Project.Domain in config.");
            return 1;
        }

        if (!File.Exists(fromFile))
        {
            writer.WriteLine($"--from-file path '{fromFile}' does not exist.");
            return 1;
        }

        var decisionContent = File.ReadAllText(fromFile);
        if (!ClarifyRecordDecisionParser.TryParse(decisionContent, out var decision, out var parseError))
        {
            writer.WriteLine(parseError);
            return 1;
        }

        var clarificationPath = ResolveClarificationPath(context, domain);
        if (clarificationPath is null)
        {
            writer.WriteLine("Clarification return path could not be resolved.");
            return 1;
        }

        if (!File.Exists(clarificationPath))
        {
            writer.WriteLine($"Clarification return path '{clarificationPath}' does not exist.");
            return 1;
        }

        var existing = File.ReadAllText(clarificationPath);
        var timestamp = DateTimeOffset.UtcNow;
        var entry = ClarifyRecordWriter.FormatEntry(decision!, timestamp);
        var updated = ClarifyRecordWriter.InsertDecision(existing, decision!, timestamp);

        if (dryRun)
        {
            writer.WriteLine($"Would record decision into '{clarificationPath}':");
            writer.WriteLine(entry);
            return 0;
        }

        File.WriteAllText(clarificationPath, updated);

        writer.WriteLine($"Recorded decision into '{clarificationPath}':");
        writer.WriteLine(entry);
        return 0;
    }

    private static string? ResolveClarificationPath(CliContext context, string domain)
    {
        if (string.IsNullOrWhiteSpace(domain))
        {
            return null;
        }

        var parentRoot = context.ResolveParentIntentRepoRootPath();
        var baseRoot = string.IsNullOrWhiteSpace(parentRoot)
            ? context.RepoRoot
            : parentRoot;

        return Path.Combine(baseRoot!, "intents", domain, "clarifications", "open.md");
    }

    private static bool TryParseArguments(
        string[] args,
        out string? domainOverride,
        out string fromFile,
        out bool dryRun,
        out string error)
    {
        domainOverride = null;
        fromFile = string.Empty;
        dryRun = false;
        error = string.Empty;

        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];
            switch (argument)
            {
                case "--domain":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--domain requires a value.";
                        return false;
                    }

                    domainOverride = args[index + 1];
                    index++;
                    break;

                case "--from-file":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--from-file requires a path.";
                        return false;
                    }

                    fromFile = args[index + 1];
                    index++;
                    break;

                case "--dry-run":
                    dryRun = true;
                    break;

                default:
                    error = $"Unknown argument '{argument}'. Supported: --domain <name> --from-file <path> [--dry-run].";
                    return false;
            }
        }

        if (string.IsNullOrWhiteSpace(fromFile))
        {
            error = "--from-file is required.";
            return false;
        }

        return true;
    }
}
