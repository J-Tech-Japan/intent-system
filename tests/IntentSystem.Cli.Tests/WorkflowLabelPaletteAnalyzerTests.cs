using IntentSystem.Cli.Commands;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G366: covers the pure palette diff analyzer. Each test feeds a
/// canned <see cref="GitHubLabelMetadata"/> list and asserts the
/// per-entry classification and the aggregate counts.
/// </summary>
public sealed class WorkflowLabelPaletteAnalyzerTests
{
    [Fact]
    public void Canonical_PaletteContainsExactlyEightLabels()
    {
        // G366 acceptance: the canonical palette is exhaustive and stable.
        Assert.Equal(8, WorkflowLabelPaletteContract.Canonical.Count);
        var names = WorkflowLabelPaletteContract.Canonical.Select(e => e.Name).ToHashSet();
        Assert.Contains("intent-target", names);
        Assert.Contains("intent-issue-in-progress", names);
        Assert.Contains("intent-pr-created", names);
        Assert.Contains("intent-pr-reviewing", names);
        Assert.Contains("intent-pr-request-update", names);
        Assert.Contains("intent-pr-update-in-progress", names);
        Assert.Contains("intent-pr-rereview-ready", names);
        Assert.Contains("intent-pr-approved", names);
    }

    [Fact]
    public void Canonical_AllEntriesHaveSixCharHexColorAndNonEmptyDescription()
    {
        // G366: enforce the palette shape so future additions can't
        // silently break audit comparison (colors must be 6 hex digits;
        // descriptions must be non-empty so missing-description drift
        // stays distinguishable from empty-description-is-canonical).
        foreach (var entry in WorkflowLabelPaletteContract.Canonical)
        {
            Assert.Equal(6, entry.Color.Length);
            Assert.Matches("^[0-9A-Fa-f]{6}$", entry.Color);
            Assert.False(string.IsNullOrWhiteSpace(entry.Description),
                $"canonical entry `{entry.Name}` must have a non-empty description");
        }
    }

    [Fact]
    public void Analyze_AllLabelsMissing_ReturnsAllMissing()
    {
        var result = WorkflowLabelPaletteAnalyzer.Analyze("owner/repo", Array.Empty<GitHubLabelMetadata>());

        Assert.Equal(WorkflowLabelPaletteContract.Canonical.Count, result.MissingCount);
        Assert.Equal(0, result.OkCount);
        Assert.Equal(0, result.WrongColorCount);
        Assert.Equal(0, result.WrongDescriptionCount);
        Assert.Equal(WorkflowLabelPaletteContract.Canonical.Count, result.DriftCount);
        Assert.All(result.Entries, e =>
        {
            Assert.Equal(WorkflowLabelPaletteAnalyzer.StatusMissing, e.Status);
            Assert.Null(e.CurrentColor);
            Assert.Null(e.CurrentDescription);
        });
    }

    [Fact]
    public void Analyze_AllCanonical_ReturnsAllOk()
    {
        var observed = WorkflowLabelPaletteContract.Canonical
            .Select(e => new GitHubLabelMetadata
            {
                Name = e.Name,
                Color = e.Color,
                Description = e.Description,
            })
            .ToList();

        var result = WorkflowLabelPaletteAnalyzer.Analyze("owner/repo", observed);

        Assert.Equal(0, result.MissingCount);
        Assert.Equal(0, result.WrongColorCount);
        Assert.Equal(0, result.WrongDescriptionCount);
        Assert.Equal(WorkflowLabelPaletteContract.Canonical.Count, result.OkCount);
        Assert.Equal(0, result.DriftCount);
    }

    [Fact]
    public void Analyze_ColorCaseInsensitive_TreatsLowercaseHexAsCanonical()
    {
        // G366: GitHub returns lowercase hex from `--json color` while
        // intent-cli stores uppercase. Comparison must be case-insensitive.
        var canonical = WorkflowLabelPaletteContract.Canonical[0];
        var observed = new[]
        {
            new GitHubLabelMetadata
            {
                Name = canonical.Name,
                Color = canonical.Color.ToLowerInvariant(),
                Description = canonical.Description,
            },
        };

        var result = WorkflowLabelPaletteAnalyzer.Analyze("owner/repo", observed);
        var entry = result.Entries.First(e => e.Name == canonical.Name);

        Assert.Equal(WorkflowLabelPaletteAnalyzer.StatusOk, entry.Status);
    }

    [Fact]
    public void Analyze_WrongColorOnly_ReturnsWrongColorEntry()
    {
        var canonical = WorkflowLabelPaletteContract.Canonical[0];
        var observed = new[]
        {
            new GitHubLabelMetadata
            {
                Name = canonical.Name,
                Color = "FFFFFF",
                Description = canonical.Description,
            },
        };

        var result = WorkflowLabelPaletteAnalyzer.Analyze("owner/repo", observed);
        var entry = result.Entries.First(e => e.Name == canonical.Name);

        Assert.Equal(WorkflowLabelPaletteAnalyzer.StatusWrongColor, entry.Status);
        Assert.Equal("FFFFFF", entry.CurrentColor);
        Assert.Equal(canonical.Description, entry.CurrentDescription);
        Assert.Equal(1, result.WrongColorCount);
        Assert.Equal(0, result.WrongDescriptionCount);
    }

    [Fact]
    public void Analyze_WrongDescriptionOnly_ReturnsWrongDescriptionEntry()
    {
        var canonical = WorkflowLabelPaletteContract.Canonical[0];
        var observed = new[]
        {
            new GitHubLabelMetadata
            {
                Name = canonical.Name,
                Color = canonical.Color,
                Description = "out of date description",
            },
        };

        var result = WorkflowLabelPaletteAnalyzer.Analyze("owner/repo", observed);
        var entry = result.Entries.First(e => e.Name == canonical.Name);

        Assert.Equal(WorkflowLabelPaletteAnalyzer.StatusWrongDescription, entry.Status);
        Assert.Equal(0, result.WrongColorCount);
        Assert.Equal(1, result.WrongDescriptionCount);
    }

    [Fact]
    public void Analyze_WrongColorAndDescription_ReturnsCombinedStatus()
    {
        // Build a fully-populated observed set: all canonical entries
        // match except canonical[0], which has BOTH wrong color and
        // wrong description. Counters on both axes increment; the
        // entry contributes exactly 1 to drift_count (single-entry
        // de-duplication contract).
        var observed = WorkflowLabelPaletteContract.Canonical
            .Select((entry, index) => index == 0
                ? new GitHubLabelMetadata { Name = entry.Name, Color = "FFFFFF", Description = "out of date" }
                : new GitHubLabelMetadata { Name = entry.Name, Color = entry.Color, Description = entry.Description })
            .ToList();

        var result = WorkflowLabelPaletteAnalyzer.Analyze("owner/repo", observed);
        var entry = result.Entries.First(e => e.Name == WorkflowLabelPaletteContract.Canonical[0].Name);

        Assert.Equal(WorkflowLabelPaletteAnalyzer.StatusWrongColorAndDescription, entry.Status);
        Assert.Equal(1, result.WrongColorCount);
        Assert.Equal(1, result.WrongDescriptionCount);
        // Single combined entry contributes exactly 1 to drift_count;
        // the 7 other canonical labels are already in sync.
        Assert.Equal(1, result.DriftCount);
    }

    [Fact]
    public void Analyze_NonCanonicalLabelsAreIgnored()
    {
        // G366 out-of-scope: only canonical workflow labels are reported.
        // Non-intent labels must not surface in the audit.
        var observed = new[]
        {
            new GitHubLabelMetadata { Name = "bug", Color = "EE0701", Description = "A bug" },
            new GitHubLabelMetadata { Name = "documentation", Color = "0075CA", Description = "Docs" },
        };

        var result = WorkflowLabelPaletteAnalyzer.Analyze("owner/repo", observed);

        Assert.Equal(WorkflowLabelPaletteContract.Canonical.Count, result.MissingCount);
        Assert.DoesNotContain(result.Entries, e => e.Name == "bug" || e.Name == "documentation");
    }
}
