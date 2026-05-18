using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G366: command-level coverage for
/// <see cref="AutomationLabelPaletteAuditCommand"/> and
/// <see cref="AutomationLabelPaletteSyncCommand"/>. The audit tests
/// pin the JSON / text shape; the sync tests pin the dry-run /
/// write split, the idempotency contract (a second sync against the
/// already-applied state plans zero mutations), and the exact
/// sequence of create / edit calls the planner emits for missing /
/// drifted labels.
/// </summary>
public sealed class AutomationLabelPaletteCommandsTests
{
    [Fact]
    public void Audit_AllInSync_EmitsOkForEveryCanonicalLabel()
    {
        AutomationLabelPaletteAuditCommand.LabelListerFactory = () =>
            new FakeLister(WorkflowLabelPaletteContract.Canonical
                .Select(e => new GitHubLabelMetadata
                {
                    Name = e.Name,
                    Color = e.Color,
                    Description = e.Description,
                })
                .ToList());

        try
        {
            using var writer = new StringWriter();
            var exit = AutomationLabelPaletteAuditCommand.Execute(
                CreateContext(),
                ["--repo", "owner/repo", "--format", "json"],
                writer);

            Assert.Equal(0, exit);
            using var doc = JsonDocument.Parse(writer.ToString());
            var root = doc.RootElement;
            Assert.Equal(0, root.GetProperty("missing_count").GetInt32());
            Assert.Equal(0, root.GetProperty("wrong_color_count").GetInt32());
            Assert.Equal(0, root.GetProperty("wrong_description_count").GetInt32());
            Assert.Equal(0, root.GetProperty("drift_count").GetInt32());
            Assert.Equal(WorkflowLabelPaletteContract.Canonical.Count, root.GetProperty("ok_count").GetInt32());
        }
        finally
        {
            AutomationLabelPaletteAuditCommand.LabelListerFactory = null;
        }
    }

    [Fact]
    public void Audit_DriftedRepo_ReportsMissingAndWrongColorEntries()
    {
        var canonical0 = WorkflowLabelPaletteContract.Canonical[0]; // intent-target
        var canonical1 = WorkflowLabelPaletteContract.Canonical[1]; // intent-issue-in-progress
        AutomationLabelPaletteAuditCommand.LabelListerFactory = () =>
            new FakeLister(new[]
            {
                // canonical0 with a wrong color (typical cross-repo drift signal).
                new GitHubLabelMetadata { Name = canonical0.Name, Color = "FFFFFF", Description = canonical0.Description },
                // canonical1 with correct color but missing description.
                new GitHubLabelMetadata { Name = canonical1.Name, Color = canonical1.Color, Description = string.Empty },
                // The rest of the palette is absent → MissingCount = 6.
            });

        try
        {
            using var writer = new StringWriter();
            var exit = AutomationLabelPaletteAuditCommand.Execute(
                CreateContext(),
                ["--repo", "owner/repo", "--format", "json"],
                writer);

            Assert.Equal(0, exit);
            using var doc = JsonDocument.Parse(writer.ToString());
            var root = doc.RootElement;
            Assert.Equal(WorkflowLabelPaletteContract.Canonical.Count - 2, root.GetProperty("missing_count").GetInt32());
            Assert.Equal(1, root.GetProperty("wrong_color_count").GetInt32());
            Assert.Equal(1, root.GetProperty("wrong_description_count").GetInt32());
            Assert.Equal(WorkflowLabelPaletteContract.Canonical.Count, root.GetProperty("drift_count").GetInt32());
        }
        finally
        {
            AutomationLabelPaletteAuditCommand.LabelListerFactory = null;
        }
    }

    [Fact]
    public void Audit_MissingRepo_ReturnsValidationError()
    {
        using var writer = new StringWriter();
        var exit = AutomationLabelPaletteAuditCommand.Execute(
            CreateContext(),
            ["--format", "json"],
            writer);
        Assert.Equal(1, exit);
        Assert.Contains("--repo", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Sync_DryRun_PlansAllMutationsAndAppliesNone()
    {
        // Repo with NO intent labels: every canonical entry must be planned
        // as a `create` action, and dry-run must not touch the mutator.
        AutomationLabelPaletteSyncCommand.LabelListerFactory = () =>
            new FakeLister(Array.Empty<GitHubLabelMetadata>());
        var mutator = new RecordingMutator();
        AutomationLabelPaletteSyncCommand.LabelPaletteMutatorFactory = () => mutator;

        try
        {
            using var writer = new StringWriter();
            var exit = AutomationLabelPaletteSyncCommand.Execute(
                CreateContext(),
                ["--repo", "owner/repo", "--format", "json"],
                writer);

            Assert.Equal(0, exit);
            using var doc = JsonDocument.Parse(writer.ToString());
            var root = doc.RootElement;
            Assert.Equal("dry-run", root.GetProperty("mode").GetString());
            Assert.Equal(WorkflowLabelPaletteContract.Canonical.Count, root.GetProperty("planned_count").GetInt32());
            Assert.Equal(0, root.GetProperty("applied_count").GetInt32());
            Assert.Empty(mutator.Creates);
            Assert.Empty(mutator.Edits);
            // Every planned action for a missing label is a `create`.
            var planned = root.GetProperty("planned_actions").EnumerateArray().ToArray();
            Assert.All(planned, a => Assert.Equal("create", a.GetProperty("action").GetString()));
        }
        finally
        {
            AutomationLabelPaletteSyncCommand.LabelListerFactory = null;
            AutomationLabelPaletteSyncCommand.LabelPaletteMutatorFactory = null;
        }
    }

    [Fact]
    public void Sync_Write_AppliesCreateForMissingAndEditForDrifted()
    {
        var canonical0 = WorkflowLabelPaletteContract.Canonical[0];
        var canonical1 = WorkflowLabelPaletteContract.Canonical[1];
        AutomationLabelPaletteSyncCommand.LabelListerFactory = () =>
            new FakeLister(new[]
            {
                new GitHubLabelMetadata { Name = canonical0.Name, Color = "FFFFFF", Description = canonical0.Description },
                new GitHubLabelMetadata { Name = canonical1.Name, Color = canonical1.Color, Description = canonical1.Description },
                // canonical[2..7] missing → create.
            });
        var mutator = new RecordingMutator();
        AutomationLabelPaletteSyncCommand.LabelPaletteMutatorFactory = () => mutator;

        try
        {
            using var writer = new StringWriter();
            var exit = AutomationLabelPaletteSyncCommand.Execute(
                CreateContext(),
                ["--repo", "owner/repo", "--write", "--format", "json"],
                writer);

            Assert.Equal(0, exit);
            // canonical0 → edit (wrong color); canonical[2..7] → create.
            Assert.Equal(6, mutator.Creates.Count);
            Assert.Single(mutator.Edits);
            Assert.Equal(canonical0.Name, mutator.Edits[0].name);
            Assert.Equal(canonical0.Color, mutator.Edits[0].color);
            // Every create matches a canonical palette entry by name + color + description.
            foreach (var (name, color, description) in mutator.Creates)
            {
                var canonical = WorkflowLabelPaletteContract.Canonical.Single(e => e.Name == name);
                Assert.Equal(canonical.Color, color);
                Assert.Equal(canonical.Description, description);
            }

            using var doc = JsonDocument.Parse(writer.ToString());
            var root = doc.RootElement;
            Assert.Equal("write", root.GetProperty("mode").GetString());
            Assert.Equal(7, root.GetProperty("applied_count").GetInt32());
        }
        finally
        {
            AutomationLabelPaletteSyncCommand.LabelListerFactory = null;
            AutomationLabelPaletteSyncCommand.LabelPaletteMutatorFactory = null;
        }
    }

    [Fact]
    public void Sync_Idempotent_SecondRunPlansZeroActions()
    {
        // G366 acceptance: sync MUST be idempotent. A second sync after the
        // palette has been applied plans zero actions and applies zero
        // mutations.
        AutomationLabelPaletteSyncCommand.LabelListerFactory = () =>
            new FakeLister(WorkflowLabelPaletteContract.Canonical
                .Select(e => new GitHubLabelMetadata
                {
                    Name = e.Name,
                    Color = e.Color,
                    Description = e.Description,
                })
                .ToList());
        var mutator = new RecordingMutator();
        AutomationLabelPaletteSyncCommand.LabelPaletteMutatorFactory = () => mutator;

        try
        {
            using var writer = new StringWriter();
            var exit = AutomationLabelPaletteSyncCommand.Execute(
                CreateContext(),
                ["--repo", "owner/repo", "--write", "--format", "json"],
                writer);

            Assert.Equal(0, exit);
            Assert.Empty(mutator.Creates);
            Assert.Empty(mutator.Edits);
            using var doc = JsonDocument.Parse(writer.ToString());
            var root = doc.RootElement;
            Assert.Equal(0, root.GetProperty("planned_count").GetInt32());
            Assert.Equal(0, root.GetProperty("applied_count").GetInt32());
        }
        finally
        {
            AutomationLabelPaletteSyncCommand.LabelListerFactory = null;
            AutomationLabelPaletteSyncCommand.LabelPaletteMutatorFactory = null;
        }
    }

    private static CliContext CreateContext()
    {
        return new CliContext
        {
            RepoRoot = Path.GetTempPath(),
            Config = new CliConfig
            {
                Project = new ProjectConfig
                {
                    Domain = "intent-cli",
                    ArtifactRoot = ".intent-cli",
                    WorktreeRoot = ".intent-cli/worktrees",
                }
            }
        };
    }

    private sealed class FakeLister : IGitHubLabelLister
    {
        private readonly IReadOnlyList<GitHubLabelMetadata> _labels;
        public FakeLister(IReadOnlyList<GitHubLabelMetadata> labels) { _labels = labels; }
        public IReadOnlyList<GitHubLabelMetadata> ListLabels(string repo) => _labels;
    }

    private sealed class RecordingMutator : IGitHubLabelPaletteMutator
    {
        public List<(string name, string color, string description)> Creates { get; } = new();
        public List<(string name, string color, string description)> Edits { get; } = new();

        public void CreateLabel(string repo, string name, string color, string description) =>
            Creates.Add((name, color, description));

        public void EditLabel(string repo, string name, string color, string description) =>
            Edits.Add((name, color, description));
    }
}
