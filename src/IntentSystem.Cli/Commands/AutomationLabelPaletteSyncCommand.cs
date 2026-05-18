using System.Text.Json;
using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G366: <c>intent-cli automation label-palette-sync --repo
/// &lt;owner/repo&gt; [--write] [--format text|json]</c> — applies
/// the canonical workflow label palette to a repository. Without
/// <c>--write</c> the command is a dry-run: it lists every planned
/// create / edit action without invoking <c>gh label create</c> or
/// <c>gh label edit</c>. With <c>--write</c>, mutations are applied
/// in palette order; the emitted report records each applied / skipped
/// entry plus an <c>applied_count</c> total.
///
/// Idempotency contract (G366 acceptance): running sync twice against
/// the same repo state MUST report <c>applied_count = 0</c> on the
/// second run. The first run brings the labels to the canonical
/// palette; a second run sees zero drift in the audit and therefore
/// plans zero mutations.
///
/// Out of scope: this command never touches label assignments on
/// existing issues or PRs. It only manages label definitions
/// themselves (name / color / description).
/// </summary>
internal static class AutomationLabelPaletteSyncCommand
{
    private const string FormatJson = "json";
    private const string FormatText = "text";

    /// <summary>
    /// G366: testability seam for the GitHub label list source.
    /// Production uses <see cref="GhCliGitHubLabelLister"/>; tests
    /// inject a fake.
    /// </summary>
    public static Func<IGitHubLabelLister>? LabelListerFactory { get; set; }

    /// <summary>
    /// G366: testability seam for the create / edit mutator.
    /// Production uses <see cref="GhCliGitHubLabelPaletteMutator"/>;
    /// tests inject a fake that records calls so assertions can
    /// verify the exact sequence of create / edit operations.
    /// </summary>
    public static Func<IGitHubLabelPaletteMutator>? LabelPaletteMutatorFactory { get; set; }

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

        if (!TryParseArguments(args, out var repo, out var write, out var format, out var error))
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

        var audit = WorkflowLabelPaletteAnalyzer.Analyze(repo!, labels);
        var plannedActions = new List<WorkflowLabelPaletteSyncAction>();
        foreach (var entry in audit.Entries)
        {
            switch (entry.Status)
            {
                case WorkflowLabelPaletteAnalyzer.StatusMissing:
                    plannedActions.Add(new WorkflowLabelPaletteSyncAction
                    {
                        Name = entry.Name,
                        Action = "create",
                        FromColor = null,
                        FromDescription = null,
                        ToColor = entry.CanonicalColor,
                        ToDescription = entry.CanonicalDescription,
                    });
                    break;
                case WorkflowLabelPaletteAnalyzer.StatusWrongColor:
                case WorkflowLabelPaletteAnalyzer.StatusWrongDescription:
                case WorkflowLabelPaletteAnalyzer.StatusWrongColorAndDescription:
                    plannedActions.Add(new WorkflowLabelPaletteSyncAction
                    {
                        Name = entry.Name,
                        Action = "edit",
                        FromColor = entry.CurrentColor,
                        FromDescription = entry.CurrentDescription,
                        ToColor = entry.CanonicalColor,
                        ToDescription = entry.CanonicalDescription,
                    });
                    break;
            }
        }

        var appliedActions = new List<WorkflowLabelPaletteSyncAction>();
        if (write && plannedActions.Count > 0)
        {
            IGitHubLabelPaletteMutator mutator;
            try
            {
                mutator = LabelPaletteMutatorFactory?.Invoke()
                    ?? new GhCliGitHubLabelPaletteMutator();
            }
            catch (Exception exception) when (
                exception is InvalidOperationException or IOException)
            {
                writer.WriteLine($"failed to initialize label palette mutator: {exception.Message}");
                return 1;
            }

            foreach (var action in plannedActions)
            {
                try
                {
                    if (string.Equals(action.Action, "create", StringComparison.Ordinal))
                    {
                        mutator.CreateLabel(repo!, action.Name, action.ToColor, action.ToDescription);
                    }
                    else
                    {
                        mutator.EditLabel(repo!, action.Name, action.ToColor, action.ToDescription);
                    }
                    appliedActions.Add(action);
                }
                catch (Exception exception) when (
                    exception is InvalidOperationException or IOException)
                {
                    writer.WriteLine(
                        $"failed to {action.Action} label `{action.Name}` in {repo}: {exception.Message}");
                    return 1;
                }
            }
        }

        var result = new WorkflowLabelPaletteSyncResult
        {
            Repo = repo!,
            Mode = write ? "write" : "dry-run",
            Audit = audit,
            PlannedActions = plannedActions,
            AppliedActions = appliedActions,
            PlannedCount = plannedActions.Count,
            AppliedCount = appliedActions.Count,
        };
        Emit(writer, result, format);
        return 0;
    }

    internal static void Emit(TextWriter writer, WorkflowLabelPaletteSyncResult result, string format)
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

    private static void WriteText(TextWriter writer, WorkflowLabelPaletteSyncResult result)
    {
        writer.WriteLine($"# Workflow label palette sync — {result.Repo} ({result.Mode})");
        writer.WriteLine($"- canonical_entries: {WorkflowLabelPaletteContract.Canonical.Count}");
        writer.WriteLine($"- planned_count: {result.PlannedCount}");
        writer.WriteLine($"- applied_count: {result.AppliedCount}");
        if (result.PlannedActions.Count == 0)
        {
            writer.WriteLine();
            writer.WriteLine("All canonical workflow labels are already in sync; nothing to do (idempotent).");
            return;
        }
        writer.WriteLine();
        writer.WriteLine("## Planned actions");
        foreach (var action in result.PlannedActions)
        {
            writer.WriteLine($"- {action.Action} {action.Name}");
            if (string.Equals(action.Action, "edit", StringComparison.Ordinal))
            {
                writer.WriteLine($"  - from: color={action.FromColor}; description={action.FromDescription}");
            }
            writer.WriteLine($"  - to: color={action.ToColor}; description={action.ToDescription}");
        }
    }

    private static bool TryParseArguments(
        string[] args,
        out string? repo,
        out bool write,
        out string format,
        out string error)
    {
        repo = null;
        write = false;
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
                case "--write":
                    write = true;
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
            error = "automation label-palette-sync requires '--repo <owner/repo>'.";
            return false;
        }

        return true;
    }
}

/// <summary>
/// G366: structured result emitted by <c>label-palette-sync</c>. The
/// <see cref="Audit"/> field embeds the same per-entry classification
/// the read-only audit command returns so dashboards can render the
/// drift table from either surface uniformly. The
/// <see cref="AppliedActions"/> list is always a prefix of
/// <see cref="PlannedActions"/> — equal in <c>write</c> mode (modulo
/// errors mid-sync) and empty in <c>dry-run</c> mode.
/// </summary>
internal sealed record WorkflowLabelPaletteSyncResult
{
    [JsonPropertyName("repo")]
    public required string Repo { get; init; }

    [JsonPropertyName("mode")]
    public required string Mode { get; init; }

    [JsonPropertyName("audit")]
    public required WorkflowLabelPaletteAuditResult Audit { get; init; }

    [JsonPropertyName("planned_actions")]
    public required IReadOnlyList<WorkflowLabelPaletteSyncAction> PlannedActions { get; init; }

    [JsonPropertyName("applied_actions")]
    public required IReadOnlyList<WorkflowLabelPaletteSyncAction> AppliedActions { get; init; }

    [JsonPropertyName("planned_count")]
    public required int PlannedCount { get; init; }

    [JsonPropertyName("applied_count")]
    public required int AppliedCount { get; init; }
}

/// <summary>
/// G366: a single planned (or applied) palette mutation. The
/// <see cref="Action"/> field is either <c>create</c> (canonical
/// label absent in the repo) or <c>edit</c> (label exists but its
/// color or description does not match the canonical palette).
/// <see cref="FromColor"/> / <see cref="FromDescription"/> are
/// <c>null</c> for <c>create</c> actions.
/// </summary>
internal sealed record WorkflowLabelPaletteSyncAction
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("action")]
    public required string Action { get; init; }

    [JsonPropertyName("from_color")]
    public string? FromColor { get; init; }

    [JsonPropertyName("from_description")]
    public string? FromDescription { get; init; }

    [JsonPropertyName("to_color")]
    public required string ToColor { get; init; }

    [JsonPropertyName("to_description")]
    public required string ToDescription { get; init; }
}
