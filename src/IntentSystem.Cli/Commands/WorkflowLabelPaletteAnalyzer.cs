using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G366: pure analyzer that diffs an observed set of GitHub labels
/// (from <see cref="IGitHubLabelLister.ListLabels"/>) against the
/// canonical <see cref="WorkflowLabelPaletteContract.Canonical"/>
/// palette and produces a structured audit. Used by both
/// <see cref="AutomationLabelPaletteAuditCommand"/> (read-only,
/// always emits the audit verbatim) and
/// <see cref="AutomationLabelPaletteSyncCommand"/> (uses the same
/// classification to plan create / edit mutations).
///
/// Pure data in / pure data out: no I/O, no <c>gh</c> calls.
/// Comparison is case-insensitive on the 6-character hex color so
/// <c>0E8A16</c> and <c>0e8a16</c> are treated as equal (GitHub
/// returns lowercase from <c>--json color</c> but accepts either on
/// write). Description comparison uses ordinal equality with an
/// empty-vs-null tolerance: a GitHub label whose description is
/// missing reads as <c>""</c>, which is treated as equivalent to a
/// canonical entry of <c>""</c> only.
/// </summary>
internal static class WorkflowLabelPaletteAnalyzer
{
    public const string StatusOk = "ok";
    public const string StatusMissing = "missing";
    public const string StatusWrongColor = "wrong-color";
    public const string StatusWrongDescription = "wrong-description";
    public const string StatusWrongColorAndDescription = "wrong-color-and-description";

    public static WorkflowLabelPaletteAuditResult Analyze(
        string repo,
        IReadOnlyList<GitHubLabelMetadata> observed)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repo);
        ArgumentNullException.ThrowIfNull(observed);

        var observedByName = new Dictionary<string, GitHubLabelMetadata>(StringComparer.Ordinal);
        foreach (var label in observed)
        {
            if (!string.IsNullOrWhiteSpace(label.Name))
            {
                observedByName[label.Name] = label;
            }
        }

        var entries = new List<WorkflowLabelPaletteAuditEntry>(WorkflowLabelPaletteContract.Canonical.Count);
        var missing = 0;
        var wrongColor = 0;
        var wrongDescription = 0;
        var ok = 0;

        foreach (var canonical in WorkflowLabelPaletteContract.Canonical)
        {
            if (!observedByName.TryGetValue(canonical.Name, out var current))
            {
                entries.Add(new WorkflowLabelPaletteAuditEntry
                {
                    Name = canonical.Name,
                    Status = StatusMissing,
                    CanonicalColor = canonical.Color,
                    CanonicalDescription = canonical.Description,
                    CurrentColor = null,
                    CurrentDescription = null,
                });
                missing++;
                continue;
            }

            var colorMismatch = !string.Equals(
                current.Color?.Trim() ?? string.Empty,
                canonical.Color,
                StringComparison.OrdinalIgnoreCase);
            var descriptionMismatch = !string.Equals(
                current.Description ?? string.Empty,
                canonical.Description,
                StringComparison.Ordinal);

            string status;
            if (colorMismatch && descriptionMismatch)
            {
                status = StatusWrongColorAndDescription;
                wrongColor++;
                wrongDescription++;
            }
            else if (colorMismatch)
            {
                status = StatusWrongColor;
                wrongColor++;
            }
            else if (descriptionMismatch)
            {
                status = StatusWrongDescription;
                wrongDescription++;
            }
            else
            {
                status = StatusOk;
                ok++;
            }

            entries.Add(new WorkflowLabelPaletteAuditEntry
            {
                Name = canonical.Name,
                Status = status,
                CanonicalColor = canonical.Color,
                CanonicalDescription = canonical.Description,
                CurrentColor = current.Color ?? string.Empty,
                CurrentDescription = current.Description ?? string.Empty,
            });
        }

        // DriftCount must count each non-ok entry exactly once even
        // when both axes mismatch (wrong-color-and-description). The
        // per-axis counters above still record both axes for dashboard
        // per-axis totals; the total entry-level drift is simply
        // canonical_count - ok_count.
        return new WorkflowLabelPaletteAuditResult
        {
            Repo = repo,
            Entries = entries,
            MissingCount = missing,
            WrongColorCount = wrongColor,
            WrongDescriptionCount = wrongDescription,
            OkCount = ok,
            DriftCount = entries.Count - ok,
        };
    }
}

/// <summary>
/// G366: audit result for one repository. Counts are derived so
/// downstream consumers can decide whether to invoke
/// <c>label-palette sync --write</c> based on a single
/// <see cref="DriftCount"/> field rather than walking the entries.
/// </summary>
internal sealed record WorkflowLabelPaletteAuditResult
{
    [JsonPropertyName("repo")]
    public required string Repo { get; init; }

    [JsonPropertyName("entries")]
    public required IReadOnlyList<WorkflowLabelPaletteAuditEntry> Entries { get; init; }

    [JsonPropertyName("missing_count")]
    public required int MissingCount { get; init; }

    [JsonPropertyName("wrong_color_count")]
    public required int WrongColorCount { get; init; }

    [JsonPropertyName("wrong_description_count")]
    public required int WrongDescriptionCount { get; init; }

    [JsonPropertyName("ok_count")]
    public required int OkCount { get; init; }

    /// <summary>
    /// G366: total entries that would change on
    /// <c>label-palette sync --write</c> — equals
    /// <c>missing + wrong-color + wrong-description</c> with the
    /// combined-mismatch case (<see cref="WorkflowLabelPaletteAnalyzer.StatusWrongColorAndDescription"/>)
    /// counted once. Zero means the palette is in sync and sync is
    /// a no-op (idempotency contract).
    /// </summary>
    [JsonPropertyName("drift_count")]
    public required int DriftCount { get; init; }
}

/// <summary>
/// G366: a single canonical label entry plus its observed state and
/// the audit classification. <see cref="CurrentColor"/> and
/// <see cref="CurrentDescription"/> are <c>null</c> when the label is
/// missing from the repository; otherwise both echo whatever GitHub
/// returned, including the empty string for an unset description.
/// </summary>
internal sealed record WorkflowLabelPaletteAuditEntry
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("status")]
    public required string Status { get; init; }

    [JsonPropertyName("canonical_color")]
    public required string CanonicalColor { get; init; }

    [JsonPropertyName("canonical_description")]
    public required string CanonicalDescription { get; init; }

    [JsonPropertyName("current_color")]
    public string? CurrentColor { get; init; }

    [JsonPropertyName("current_description")]
    public string? CurrentDescription { get; init; }
}
