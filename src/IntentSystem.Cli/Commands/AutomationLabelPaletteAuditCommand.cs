using System.Text.Json;
using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G366: <c>intent-cli automation label-palette-audit --repo
/// &lt;owner/repo&gt; [--format text|json]</c> — read-only audit of
/// the workflow label palette against
/// <see cref="WorkflowLabelPaletteContract.Canonical"/>. Lists each
/// canonical label as <c>ok</c>, <c>missing</c>, <c>wrong-color</c>,
/// <c>wrong-description</c>, or <c>wrong-color-and-description</c> so
/// the operator can decide whether to run the matching
/// <c>label-palette-sync --write</c> command. Never mutates GitHub.
/// </summary>
internal static class AutomationLabelPaletteAuditCommand
{
    private const string FormatJson = "json";
    private const string FormatText = "text";

    /// <summary>
    /// G366: testability seam for the GitHub label list source.
    /// Production uses <see cref="GhCliGitHubLabelLister"/>; tests
    /// inject a fake so they can model missing labels, drifted
    /// colors, and drifted descriptions deterministically without
    /// invoking <c>gh</c>.
    /// </summary>
    public static Func<IGitHubLabelLister>? LabelListerFactory { get; set; }

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

        if (!TryParseArguments(args, out var repo, out var format, out var error))
        {
            writer.WriteLine(error);
            return 1;
        }

        IGitHubLabelLister lister;
        try
        {
            lister = LabelListerFactory?.Invoke() ?? new GhCliGitHubLabelLister();
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or IOException)
        {
            writer.WriteLine($"failed to initialize GitHub label lister: {exception.Message}");
            return 1;
        }

        IReadOnlyList<GitHubLabelMetadata> labels;
        try
        {
            labels = lister.ListLabels(repo!);
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or IOException)
        {
            writer.WriteLine($"failed to list labels in {repo}: {exception.Message}");
            return 1;
        }

        var result = WorkflowLabelPaletteAnalyzer.Analyze(repo!, labels);
        Emit(writer, result, format);
        return 0;
    }

    internal static void Emit(TextWriter writer, WorkflowLabelPaletteAuditResult result, string format)
    {
        if (string.Equals(format, FormatJson, StringComparison.Ordinal))
        {
            writer.WriteLine(JsonSerializer.Serialize(result, JsonOptions));
        }
        else
        {
            WriteText(writer, result);
        }
    }

    private static void WriteText(TextWriter writer, WorkflowLabelPaletteAuditResult result)
    {
        writer.WriteLine($"# Workflow label palette audit — {result.Repo}");
        writer.WriteLine($"- canonical_entries: {WorkflowLabelPaletteContract.Canonical.Count}");
        writer.WriteLine($"- ok: {result.OkCount}");
        writer.WriteLine($"- missing: {result.MissingCount}");
        writer.WriteLine($"- wrong_color: {result.WrongColorCount}");
        writer.WriteLine($"- wrong_description: {result.WrongDescriptionCount}");
        writer.WriteLine($"- drift_count: {result.DriftCount}");
        writer.WriteLine();
        writer.WriteLine("## Entries");
        foreach (var entry in result.Entries)
        {
            writer.WriteLine($"- {entry.Name}: {entry.Status}");
            writer.WriteLine($"  - canonical: color={entry.CanonicalColor}; description={entry.CanonicalDescription}");
            if (!string.Equals(entry.Status, WorkflowLabelPaletteAnalyzer.StatusMissing, StringComparison.Ordinal))
            {
                writer.WriteLine($"  - current: color={entry.CurrentColor}; description={entry.CurrentDescription}");
            }
        }
    }

    private static bool TryParseArguments(string[] args, out string? repo, out string format, out string error)
    {
        repo = null;
        format = FormatText;
        error = string.Empty;

        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];
            switch (argument)
            {
                case "--repo":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--repo requires a value (e.g. owner/repo)."; return false;
                    }
                    repo = args[++index].Trim();
                    break;
                case "--format":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--format requires a value (text or json)."; return false;
                    }
                    var requested = args[++index].Trim();
                    if (!string.Equals(requested, FormatText, StringComparison.Ordinal)
                        && !string.Equals(requested, FormatJson, StringComparison.Ordinal))
                    {
                        error = $"--format must be 'text' or 'json' (got '{requested}')."; return false;
                    }
                    format = requested;
                    break;
                default:
                    error = $"Unknown argument '{argument}'."; return false;
            }
        }

        if (string.IsNullOrWhiteSpace(repo))
        {
            error = "automation label-palette-audit requires '--repo <owner/repo>'.";
            return false;
        }

        return true;
    }
}
