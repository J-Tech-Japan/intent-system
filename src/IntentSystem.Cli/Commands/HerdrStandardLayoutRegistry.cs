using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G701: the versioned, enumerable source for the standard herdr team layout.
/// The guide renders this model; it never queries or mutates herdr state.
/// </summary>
internal static class HerdrStandardLayoutRegistry
{
    public const string RegistryId = "herdr-standard-layout";
    public const string RegistryVersion = "herdr-standard-layout/v1";

    public static IReadOnlyList<HerdrStandardLayoutPane> Panes { get; } =
    [
        new HerdrStandardLayoutPane
        {
            Role = "orchestration",
            Position = "left",
            Label = "orchestration",
            Cwd = "<host-repo>",
            CreateCommand = "herdr workspace create --cwd <host-repo> --label \"<team> · herdr-only\" --no-focus",
            RenameCommand = "herdr pane rename <root-pane-id> orchestration",
        },
        new HerdrStandardLayoutPane
        {
            Role = "implementation",
            Position = "right-top",
            Label = "implementation",
            Cwd = "<implementation-repo>",
            CreateCommand = "herdr pane split --pane <orchestration-pane-id> --direction right --cwd <implementation-repo> --no-focus",
            RenameCommand = "herdr pane rename <implementation-pane-id> implementation",
        },
        new HerdrStandardLayoutPane
        {
            Role = "review",
            Position = "right-bottom",
            Label = "review",
            Cwd = "<review-cwd>",
            CreateCommand = "herdr pane split --pane <implementation-pane-id> --direction down --cwd <review-cwd> --no-focus",
            RenameCommand = "herdr pane rename <review-pane-id> review",
        },
    ];

    public static HerdrStandardLayoutGuide Create() => new()
    {
        RegistryId = RegistryId,
        RegistryVersion = RegistryVersion,
        Layout = "one-team-tab-three-pane",
        TeamTabCount = 1,
        TeamTabLabel = "<team>",
        WorkspaceLabel = "<team> · herdr-only",
        PaneCount = Panes.Count,
        Panes = Panes,
        TopologySummary = "One workspace per team, one tab named after the team, one pane per role, each pane opened with that role's folder as its cwd.",
        OperatorVisibility = "This keeps all roles visible to the operator at once and keeps the G550 supervision pane scan from being hidden behind an inactive tab.",
        Creation = new HerdrStandardLayoutCreation
        {
            Workspace = "herdr workspace create --cwd <host-repo> --label \"<team> · herdr-only\" --no-focus",
            Tab = "Use the returned tab as the one team tab; if its label needs repair: herdr tab rename <tab-id> <team>",
            RootPane = "Assign the returned root_pane to orchestration, then run herdr pane rename <root-pane-id> orchestration",
            DefaultPaneSplit = "herdr pane split --pane <pane-id> --direction right|down --cwd <role-cwd> --no-focus",
            ImplementationPane = "herdr pane split --pane <orchestration-pane-id> --direction right --cwd <implementation-repo> --no-focus",
            ReviewPane = "herdr pane split --pane <implementation-pane-id> --direction down --cwd <review-cwd> --no-focus",
        },
        Repair = new HerdrStandardLayoutRepair
        {
            TemporaryTab = "herdr tab create --workspace <workspace-id> --label g701-layout-repair --no-focus",
            MoveRight = "herdr pane move <pane-id> --tab <tab-id> --split right --target-pane <target-pane> --no-focus",
            MoveDown = "herdr pane move <pane-id> --tab <tab-id> --split down --target-pane <target-pane> --no-focus",
            Rename = "herdr pane rename <pane-id> <role-label>",
            MeasuredShape = "herdr pane move --tab --split right|down --target-pane, followed by herdr pane rename",
            SafeOrder = "Preview and verify explicit workspace/tab/pane ids, run the measured move on a scratch tab first, then rename each pane and re-check every agent; the operator runs commands, not intent-cli.",
        },
        SetupCheck = new HerdrLayoutSetupCheck
        {
            Id = "layout-and-labels",
            Name = "layout + labels",
            Condition = "Exactly one team-named tab has orchestration on the left and implementation above review on the right, with the three role labels.",
            IncompleteOutcome = "visible-incompleteness",
            ReadyBlocking = false,
            ReadOnly = true,
            NeverExecutesHerdr = true,
        },
        AuthorityBoundary = "Guide output is a read-only operator plan. It does not enforce layout programmatically, query herdr, create panes, rename panes, move panes, or answer dialogs.",
    };

    public static JsonObject CreateJson() =>
        (JsonSerializer.SerializeToNode(Create(), JsonOptions) as JsonObject)
        ?? throw new InvalidOperationException("The herdr standard layout registry did not serialize as an object.");

    public static string RenderMarkdown()
    {
        var layout = Create();
        using var writer = new StringWriter();
        writer.WriteLine("## Herdr standard layout registry (G701)");
        writer.WriteLine();
        writer.WriteLine($"- registry: `{layout.RegistryId}`");
        writer.WriteLine($"- version: `{layout.RegistryVersion}`");
        writer.WriteLine($"- layout: `{layout.Layout}` — exactly {layout.TeamTabCount} team tab and {layout.PaneCount} panes");
        writer.WriteLine($"- team tab label: `{layout.TeamTabLabel}`");
        writer.WriteLine($"- workspace label: `{layout.WorkspaceLabel}` is a human-facing, non-authoritative display only; `session-layer-mode.json` remains the source of truth");
        writer.WriteLine($"- topology: {layout.TopologySummary}");
        writer.WriteLine($"- operator visibility: {layout.OperatorVisibility}");
        writer.WriteLine();
        writer.WriteLine("### Enumerable panes");
        writer.WriteLine();
        foreach (var pane in layout.Panes)
        {
            writer.WriteLine($"- role `{pane.Role}` — position `{pane.Position}`; label `{pane.Label}`; cwd `{pane.Cwd}`");
            writer.WriteLine($"  - create: `{pane.CreateCommand}`");
            writer.WriteLine($"  - rename: `{pane.RenameCommand}`");
        }

        writer.WriteLine();
        writer.WriteLine("### Exact creation sequence");
        writer.WriteLine();
        writer.WriteLine($"1. `{layout.Creation.Workspace}`");
        writer.WriteLine($"2. {layout.Creation.Tab}");
        writer.WriteLine($"3. {layout.Creation.RootPane}");
        writer.WriteLine($"4. default pane split shape: `{layout.Creation.DefaultPaneSplit}`");
        writer.WriteLine($"5. `{layout.Creation.ImplementationPane}`");
        writer.WriteLine($"6. `{layout.Creation.ReviewPane}`");
        writer.WriteLine("- measured workspace result fields: `workspace_created`, `workspace.workspace_id`, `tab.tab_id`, `root_pane.pane_id`, `root_pane.cwd`");

        writer.WriteLine();
        writer.WriteLine("### Measured repair sequence for a wrongly-created layout");
        writer.WriteLine();
        writer.WriteLine($"1. `{layout.Repair.TemporaryTab}`");
        writer.WriteLine($"2. `{layout.Repair.MoveRight}` or `{layout.Repair.MoveDown}`, choosing the measured split and explicit target pane");
        writer.WriteLine($"3. `{layout.Repair.Rename}` for `orchestration`, `implementation`, and `review`");
        writer.WriteLine($"- measured command shape: `{layout.Repair.MeasuredShape}`");
        writer.WriteLine("- measured same-tab boundary: Same-tab `herdr pane move` is unsupported; use the temporary-tab move and explicit target-pane repair above.");
        writer.WriteLine($"- safe order: {layout.Repair.SafeOrder}");

        writer.WriteLine();
        writer.WriteLine($"### Named setup check: `{layout.SetupCheck.Id}`");
        writer.WriteLine();
        writer.WriteLine($"- name: **{layout.SetupCheck.Name}**");
        writer.WriteLine($"- check: {layout.SetupCheck.Condition}");
        writer.WriteLine($"- incomplete result: `{layout.SetupCheck.IncompleteOutcome}`");
        writer.WriteLine($"- READY blocking: **{layout.SetupCheck.ReadyBlocking.ToString().ToLowerInvariant()}** — an incomplete layout is visible and must be repaired, but this named check never hard-blocks READY");
        writer.WriteLine($"- read-only / executes herdr: `{layout.SetupCheck.ReadOnly}` / `{(!layout.SetupCheck.NeverExecutesHerdr).ToString().ToLowerInvariant()}`");
        writer.WriteLine();
        writer.WriteLine($"- authority boundary: {layout.AuthorityBoundary}");
        return writer.ToString().TrimEnd();
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
    };
}

internal sealed record HerdrStandardLayoutGuide
{
    [JsonPropertyName("registry_id")]
    public required string RegistryId { get; init; }

    [JsonPropertyName("registry_version")]
    public required string RegistryVersion { get; init; }

    [JsonPropertyName("layout")]
    public required string Layout { get; init; }

    [JsonPropertyName("team_tab_count")]
    public required int TeamTabCount { get; init; }

    [JsonPropertyName("team_tab_label")]
    public required string TeamTabLabel { get; init; }

    [JsonPropertyName("workspace_label")]
    public required string WorkspaceLabel { get; init; }

    [JsonPropertyName("pane_count")]
    public required int PaneCount { get; init; }

    [JsonPropertyName("topology_summary")]
    public required string TopologySummary { get; init; }

    [JsonPropertyName("operator_visibility")]
    public required string OperatorVisibility { get; init; }

    [JsonPropertyName("panes")]
    public required IReadOnlyList<HerdrStandardLayoutPane> Panes { get; init; }

    [JsonPropertyName("creation")]
    public required HerdrStandardLayoutCreation Creation { get; init; }

    [JsonPropertyName("repair")]
    public required HerdrStandardLayoutRepair Repair { get; init; }

    [JsonPropertyName("setup_check")]
    public required HerdrLayoutSetupCheck SetupCheck { get; init; }

    [JsonPropertyName("authority_boundary")]
    public required string AuthorityBoundary { get; init; }
}

internal sealed record HerdrStandardLayoutPane
{
    [JsonPropertyName("role")]
    public required string Role { get; init; }

    [JsonPropertyName("position")]
    public required string Position { get; init; }

    [JsonPropertyName("label")]
    public required string Label { get; init; }

    [JsonPropertyName("cwd")]
    public required string Cwd { get; init; }

    [JsonPropertyName("create_command")]
    public required string CreateCommand { get; init; }

    [JsonPropertyName("rename_command")]
    public required string RenameCommand { get; init; }
}

internal sealed record HerdrStandardLayoutCreation
{
    [JsonPropertyName("workspace")]
    public required string Workspace { get; init; }

    [JsonPropertyName("tab")]
    public required string Tab { get; init; }

    [JsonPropertyName("root_pane")]
    public required string RootPane { get; init; }

    [JsonPropertyName("default_pane_split")]
    public required string DefaultPaneSplit { get; init; }

    [JsonPropertyName("implementation_pane")]
    public required string ImplementationPane { get; init; }

    [JsonPropertyName("review_pane")]
    public required string ReviewPane { get; init; }
}

internal sealed record HerdrStandardLayoutRepair
{
    [JsonPropertyName("temporary_tab")]
    public required string TemporaryTab { get; init; }

    [JsonPropertyName("move_right")]
    public required string MoveRight { get; init; }

    [JsonPropertyName("move_down")]
    public required string MoveDown { get; init; }

    [JsonPropertyName("rename")]
    public required string Rename { get; init; }

    [JsonPropertyName("measured_shape")]
    public required string MeasuredShape { get; init; }

    [JsonPropertyName("safe_order")]
    public required string SafeOrder { get; init; }
}

internal sealed record HerdrLayoutSetupCheck
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("condition")]
    public required string Condition { get; init; }

    [JsonPropertyName("incomplete_outcome")]
    public required string IncompleteOutcome { get; init; }

    [JsonPropertyName("ready_blocking")]
    public required bool ReadyBlocking { get; init; }

    [JsonPropertyName("read_only")]
    public required bool ReadOnly { get; init; }

    [JsonPropertyName("never_executes_herdr")]
    public required bool NeverExecutesHerdr { get; init; }
}
