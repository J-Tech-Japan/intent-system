using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G637: read-only workspace-layout convention guidance.
///
/// The command consumes operator-supplied workspace/pane identifiers and an
/// observed shape. It renders the herdr commands an operator may run; it never
/// starts herdr, asks herdr for state, or mutates the recorded topology. This
/// keeps layout repair in the session layer while leaving identity, delivery,
/// and settle semantics unchanged.
///
/// The surface is deliberately preview-through-1.x: it was added after the
/// v0.12.0 compatibility freeze and its command/prose shape may evolve.
/// </summary>
internal static class GuideWorkspaceLayoutCommand
{
    internal const string CommandName = "workspace-layout";

    private const string FormatMarkdown = "markdown";
    private const string FormatJson = "json";
    private const string ShapeCanonical = "canonical";
    private const string ShapeThreeColumn = "three-column";
    private const string ShapeMirrored = "mirrored";
    private const string ShapeUnknown = "unknown";
    private const string SeatReview = "review";
    private const string SeatDesign = "design";
    private const decimal TargetLeftRatio = 0.4m;
    private const decimal TargetRightSplitRatio = 0.5m;
    private const decimal RatioTolerance = 0.0005m;

    private const string UsageLine =
        "Usage: intent-cli guide workspace-layout [--workspace-id <id>] [--tab-id <id>] "
        + "[--shape canonical|three-column|mirrored|unknown] "
        + "[--orchestration-pane <id>] [--implementation-pane <id>] [--review-pane <id>] "
        + "[--orchestration-label <label>] [--implementation-label <label>] [--review-label <label>] "
        + "[--third-seat-role review|design] [--temporary-tab-id <id>] "
        + "[--round-trip-pane <id>] [--target-pane <id>] "
        + "[--actual-left-ratio <0..1>] [--actual-top-right-ratio <0..1>] "
        + "[--format markdown|json]";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static int Execute(CliContext context, string[] args, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);

        if (args.Length == 1 && string.Equals(args[0], "--help", StringComparison.Ordinal))
        {
            WriteHelp(writer);
            return 0;
        }

        if (!TryParseArguments(args, out var request, out var error))
        {
            writer.WriteLine(error);
            writer.WriteLine(UsageLine);
            return 1;
        }

        var plan = Build(request);
        if (string.Equals(request.Format, FormatJson, StringComparison.Ordinal))
        {
            writer.WriteLine(JsonSerializer.Serialize(plan, JsonOptions));
            return 0;
        }

        WriteMarkdown(writer, plan);
        return 0;
    }

    /// <summary>
    /// Builds a deterministic plan from explicit operator input. This method
    /// has no filesystem, process, network, or host-state access so tests can
    /// prove that the guide is render-only.
    /// </summary>
    internal static WorkspaceLayoutPlan Build(WorkspaceLayoutRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var expectedReviewLabel = string.Equals(request.ThirdSeatRole, SeatDesign, StringComparison.Ordinal)
            ? SeatDesign
            : SeatReview;
        var shapeDiffers = !string.Equals(request.Shape, ShapeCanonical, StringComparison.Ordinal);

        var scratchCommands = new List<string>();
        if (shapeDiffers)
        {
            // A temporary tab is intentional: herdr's same-tab move is a
            // measured no-op. The second move targets the pane that should be
            // above the moved pane, preserving the running process.
            scratchCommands.Add(
                $"herdr tab create --workspace {request.WorkspaceId} --label g637-layout-scratch --no-focus");
            scratchCommands.Add(
                $"herdr pane move {request.RoundTripPaneId} --tab {request.TemporaryTabId} --no-focus");
            scratchCommands.Add(
                $"herdr pane move {request.RoundTripPaneId} --tab {request.SourceTabId} "
                + $"--target-pane {request.TargetPaneId} --split down --ratio {TargetRightSplitRatio.ToString(CultureInfo.InvariantCulture)} --no-focus");
        }

        var renameCommands = new List<string>();
        AddRenameIfNeeded(
            renameCommands,
            request.OrchestrationPaneId,
            request.OrchestrationLabel,
            "orchestration");
        AddRenameIfNeeded(
            renameCommands,
            request.ImplementationPaneId,
            request.ImplementationLabel,
            "implementation");
        AddRenameIfNeeded(
            renameCommands,
            request.ReviewPaneId,
            request.ReviewLabel,
            expectedReviewLabel);

        var resizeCommands = new List<string>();
        AddResizeCommand(
            resizeCommands,
            request.OrchestrationPaneId,
            request.ActualLeftRatio,
            TargetLeftRatio,
            vertical: false);
        AddResizeCommand(
            resizeCommands,
            request.ImplementationPaneId,
            request.ActualTopRightRatio,
            TargetRightSplitRatio,
            vertical: true);

        // When a caller only knows that the shape is non-conforming, retain a
        // runnable correction rather than silently omitting the two canonical
        // resize calls. Supplying actual ratios turns these into minimal
        // directional deltas; otherwise the operator can inspect the emitted
        // layout first and use the documented target amounts.
        if (shapeDiffers && request.ActualLeftRatio is null)
        {
            resizeCommands.Add(
                $"herdr pane resize --pane {request.OrchestrationPaneId} --direction right "
                + $"--amount {TargetLeftRatio.ToString(CultureInfo.InvariantCulture)}");
        }

        if (shapeDiffers && request.ActualTopRightRatio is null)
        {
            resizeCommands.Add(
                $"herdr pane resize --pane {request.ImplementationPaneId} --direction down "
                + $"--amount {TargetRightSplitRatio.ToString(CultureInfo.InvariantCulture)}");
        }

        var commands = scratchCommands
            .Concat(renameCommands)
            .Concat(resizeCommands)
            .ToArray();

        return new WorkspaceLayoutPlan
        {
            Preview = true,
            WorkspaceId = request.WorkspaceId,
            SourceTabId = request.SourceTabId,
            ObservedShape = request.Shape,
            StructureDiffers = shapeDiffers,
            Convention = new WorkspaceLayoutConvention
            {
                LeftRole = "orchestration",
                LeftWidth = 0.4m,
                TopRightRole = "implementation",
                BottomRightRole = expectedReviewLabel,
                RightWidth = 0.6m,
                RightSplit = 0.5m,
                LabelVocabulary = expectedReviewLabel == SeatDesign
                    ? new[] { "orchestration", "implementation", "design" }
                    : new[] { "orchestration", "implementation", "review" },
            },
            ScratchTabCommands = scratchCommands,
            RenameCommands = renameCommands,
            ResizeCommands = resizeCommands,
            Commands = commands,
            MeasuredFacts = MeasuredFacts,
            SafeOrder = SafeOrder,
            Boundaries = Boundaries,
        };
    }

    private static readonly IReadOnlyList<string> MeasuredFacts =
    [
        "On herdr 0.8.0 on macOS, `herdr pane move` within the same tab returned `changed: false`; that measured no-op is not a claim about other herdr versions or platforms.",
        "On herdr 0.8.0 on macOS, moving a pane to a temporary tab and back while targeting its destination pane reparented the pane rather than recreating it; all seventeen running agent processes survived the measured round trip.",
    ];

    private static readonly IReadOnlyList<string> SafeOrder =
    [
        "Run the temporary-tab round trip on a scratch tab first and inspect the returned pane/tab identifiers.",
        "Before applying the plan to a workspace holding working agents, confirm the scratch process and every expected agent are still present.",
        "Only then run the rendered rename and resize commands, resolving explicit identifiers immediately before each command.",
        "After the layout change, confirm every agent is still present; intent-cli does not perform this check for you.",
    ];

    private static readonly IReadOnlyList<string> Boundaries =
    [
        "This command prints text only; it never executes herdr or any other process.",
        "Seat identity, topology records, delivery semantics, and the settle contract are unchanged.",
        "The convention does not prescribe an arrangement for a single-pane workspace.",
        "The measured facts are scoped to herdr 0.8.0 on macOS; do not generalize them to an unmeasured version or platform.",
    ];

    private static void AddRenameIfNeeded(
        ICollection<string> commands,
        string paneId,
        string currentLabel,
        string expectedLabel)
    {
        if (!string.Equals(currentLabel, expectedLabel, StringComparison.Ordinal))
        {
            commands.Add($"herdr pane rename {paneId} {expectedLabel}");
        }
    }

    private static void AddResizeCommand(
        ICollection<string> commands,
        string paneId,
        decimal? actual,
        decimal target,
        bool vertical)
    {
        if (actual is null || Math.Abs(actual.Value - target) <= RatioTolerance)
        {
            return;
        }

        var delta = target - actual.Value;
        var direction = vertical
            ? delta > 0 ? "down" : "up"
            : delta > 0 ? "right" : "left";

        commands.Add(
            $"herdr pane resize --pane {paneId} --direction {direction} "
            + $"--amount {Math.Abs(delta).ToString(CultureInfo.InvariantCulture)}");
    }

    private static bool TryParseArguments(
        string[] args,
        out WorkspaceLayoutRequest request,
        out string error)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var format = FormatMarkdown;
        var shape = ShapeCanonical;
        var thirdSeatRole = SeatReview;
        decimal? actualLeftRatio = null;
        decimal? actualTopRightRatio = null;

        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];
            if (string.Equals(argument, "--conforming", StringComparison.Ordinal))
            {
                shape = ShapeCanonical;
                continue;
            }

            if (string.Equals(argument, "--nonconforming", StringComparison.Ordinal))
            {
                shape = ShapeUnknown;
                continue;
            }

            if (!RequiresValue(argument))
            {
                request = new WorkspaceLayoutRequest();
                error = $"Unknown argument '{argument}'.";
                return false;
            }

            if (++index >= args.Length)
            {
                request = new WorkspaceLayoutRequest();
                error = $"{argument} requires a value.";
                return false;
            }

            var value = args[index];
            switch (argument)
            {
                case "--format":
                    format = value;
                    break;
                case "--shape":
                case "--actual-shape":
                    shape = NormalizeShape(value);
                    break;
                case "--third-seat-role":
                    thirdSeatRole = value;
                    break;
                case "--actual-left-ratio":
                    if (!TryParseRatio(value, out actualLeftRatio, out error))
                    {
                        request = new WorkspaceLayoutRequest();
                        return false;
                    }

                    break;
                case "--actual-top-right-ratio":
                    if (!TryParseRatio(value, out actualTopRightRatio, out error))
                    {
                        request = new WorkspaceLayoutRequest();
                        return false;
                    }

                    break;
                default:
                    values[argument] = value;
                    break;
            }
        }

        if (!string.Equals(format, FormatMarkdown, StringComparison.Ordinal)
            && !string.Equals(format, FormatJson, StringComparison.Ordinal))
        {
            request = new WorkspaceLayoutRequest();
            error = $"Unknown --format '{format}'. Supported: markdown, json.";
            return false;
        }

        if (!string.Equals(thirdSeatRole, SeatReview, StringComparison.Ordinal)
            && !string.Equals(thirdSeatRole, SeatDesign, StringComparison.Ordinal))
        {
            request = new WorkspaceLayoutRequest();
            error = $"Unsupported --third-seat-role '{thirdSeatRole}'. Supported: review, design.";
            return false;
        }

        var orchestrationPaneId = Get(values, "--orchestration-pane", "<orchestration-pane>");
        var implementationPaneId = Get(values, "--implementation-pane", "<implementation-pane>");
        var reviewPaneId = Get(values, "--review-pane", "<review-pane>");
        request = new WorkspaceLayoutRequest
        {
            Format = format,
            Shape = shape,
            ThirdSeatRole = thirdSeatRole,
            WorkspaceId = Get(values, "--workspace-id", "--workspace", "<workspace-id>"),
            SourceTabId = Get(values, "--tab-id", "--source-tab-id", "<tab-id>"),
            TemporaryTabId = Get(values, "--temporary-tab-id", "<temporary-tab-id>"),
            OrchestrationPaneId = orchestrationPaneId,
            ImplementationPaneId = implementationPaneId,
            ReviewPaneId = reviewPaneId,
            OrchestrationLabel = Get(values, "--orchestration-label", "orchestration"),
            ImplementationLabel = Get(values, "--implementation-label", "implementation"),
            ReviewLabel = Get(values, "--review-label", thirdSeatRole == SeatDesign ? "design" : "review"),
            RoundTripPaneId = values.TryGetValue("--round-trip-pane", out var roundTripPane)
                ? roundTripPane
                : values.TryGetValue("--move-pane", out var movePane) ? movePane : reviewPaneId,
            TargetPaneId = values.TryGetValue("--target-pane", out var targetPane)
                ? targetPane
                : implementationPaneId,
            ActualLeftRatio = actualLeftRatio,
            ActualTopRightRatio = actualTopRightRatio,
        };
        error = string.Empty;
        return true;
    }

    private static string Get(
        IReadOnlyDictionary<string, string> values,
        string key,
        string fallback) => values.TryGetValue(key, out var value) ? value : fallback;

    private static string Get(
        IReadOnlyDictionary<string, string> values,
        string key,
        string alias,
        string fallback) => values.TryGetValue(key, out var value)
        ? value
        : values.TryGetValue(alias, out var aliasValue) ? aliasValue : fallback;

    private static bool TryParseRatio(string value, out decimal? ratio, out string error)
    {
        if (!decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            || parsed < 0m
            || parsed > 1m)
        {
            ratio = null;
            error = $"Ratio '{value}' must be a number between 0 and 1.";
            return false;
        }

        ratio = parsed;
        error = string.Empty;
        return true;
    }

    private static string NormalizeShape(string value) => value switch
    {
        "canonical" => ShapeCanonical,
        "left-40-right-split" => ShapeCanonical,
        "left-60-right-split" => ShapeThreeColumn,
        "left-split" => ShapeMirrored,
        "three-columns" => ShapeThreeColumn,
        "three-column" => ShapeThreeColumn,
        "mirrored" => ShapeMirrored,
        "unknown" => ShapeUnknown,
        _ => value,
    };

    private static bool RequiresValue(string argument) =>
        argument is "--format"
            or "--shape"
            or "--actual-shape"
            or "--third-seat-role"
            or "--actual-left-ratio"
            or "--actual-top-right-ratio"
            or "--workspace-id"
            or "--workspace"
            or "--tab-id"
            or "--source-tab-id"
            or "--temporary-tab-id"
            or "--orchestration-pane"
            or "--implementation-pane"
            or "--review-pane"
            or "--orchestration-label"
            or "--implementation-label"
            or "--review-label"
            or "--round-trip-pane"
            or "--move-pane"
            or "--target-pane";

    private static void WriteMarkdown(TextWriter writer, WorkspaceLayoutPlan plan)
    {
        writer.WriteLine("# Team workspace layout guide (G637 — preview-through-1.x)");
        writer.WriteLine();
        writer.WriteLine("This is a render-only plan. `intent-cli` does not execute any command below or query a live workspace.");
        writer.WriteLine();
        writer.WriteLine($"- Workspace: `{plan.WorkspaceId}`");
        writer.WriteLine($"- Observed shape: `{plan.ObservedShape}`");
        writer.WriteLine($"- Structure differs: `{plan.StructureDiffers.ToString().ToLowerInvariant()}`");
        writer.WriteLine();
        writer.WriteLine("## Convention");
        writer.WriteLine();
        writer.WriteLine("| slot | role label | width |");
        writer.WriteLine("| --- | --- | --- |");
        writer.WriteLine($"| left, full height | `{plan.Convention.LeftRole}` | 40% |");
        writer.WriteLine($"| top-right | `{plan.Convention.TopRightRole}` | 30% |");
        writer.WriteLine($"| bottom-right | `{plan.Convention.BottomRightRole}` | 30% |");
        writer.WriteLine();
        writer.WriteLine("The right column is 60% of the workspace and is split evenly between the two right-hand seats.");
        writer.WriteLine();
        writer.WriteLine("The label is the recorded topology role. A genuinely design third seat uses `design`; the slot convention does not rename the seat's identity.");
        writer.WriteLine();
        writer.WriteLine("## Commands (run in the listed order after the safe-order checks)");
        writer.WriteLine();
        if (plan.Commands.Count == 0)
        {
            writer.WriteLine("The supplied workspace is already canonical and its labels match; no layout command is needed.");
        }
        else
        {
            for (var index = 0; index < plan.Commands.Count; index++)
            {
                writer.WriteLine($"{index + 1}. `{plan.Commands[index]}`");
            }
        }

        WriteListSection(writer, "## Safe order", plan.SafeOrder);
        WriteListSection(writer, "## Measured facts (herdr 0.8.0 on macOS)", plan.MeasuredFacts);
        WriteListSection(writer, "## Boundaries", plan.Boundaries);
    }

    private static void WriteListSection(TextWriter writer, string heading, IEnumerable<string> entries)
    {
        writer.WriteLine();
        writer.WriteLine(heading);
        writer.WriteLine();
        foreach (var entry in entries)
        {
            writer.WriteLine($"- {entry}");
        }
    }

    private static void WriteHelp(TextWriter writer)
    {
        writer.WriteLine("guide workspace-layout (G637 — preview-through-1.x)");
        writer.WriteLine(UsageLine);
        writer.WriteLine();
        writer.WriteLine("Supply the operator-observed shape and explicit workspace/tab/pane identifiers.");
        writer.WriteLine("`canonical` emits little or nothing when labels and ratios already match.");
        writer.WriteLine("Non-canonical shapes emit a temporary-tab round trip before rename/resize commands.");
        writer.WriteLine("The command is read-only and never invokes herdr.");
    }
}

internal sealed record WorkspaceLayoutRequest
{
    public string Format { get; init; } = "markdown";
    public string Shape { get; init; } = "canonical";
    public string ThirdSeatRole { get; init; } = "review";
    public string WorkspaceId { get; init; } = "<workspace-id>";
    public string SourceTabId { get; init; } = "<tab-id>";
    public string TemporaryTabId { get; init; } = "<temporary-tab-id>";
    public string OrchestrationPaneId { get; init; } = "<orchestration-pane>";
    public string ImplementationPaneId { get; init; } = "<implementation-pane>";
    public string ReviewPaneId { get; init; } = "<review-pane>";
    public string OrchestrationLabel { get; init; } = "orchestration";
    public string ImplementationLabel { get; init; } = "implementation";
    public string ReviewLabel { get; init; } = "review";
    public string RoundTripPaneId { get; init; } = "<review-pane>";
    public string TargetPaneId { get; init; } = "<implementation-pane>";
    public decimal? ActualLeftRatio { get; init; }
    public decimal? ActualTopRightRatio { get; init; }
}

internal sealed record WorkspaceLayoutPlan
{
    [JsonPropertyName("preview")]
    public required bool Preview { get; init; }

    [JsonPropertyName("workspace_id")]
    public required string WorkspaceId { get; init; }

    [JsonPropertyName("source_tab_id")]
    public required string SourceTabId { get; init; }

    [JsonPropertyName("observed_shape")]
    public required string ObservedShape { get; init; }

    [JsonPropertyName("structure_differs")]
    public required bool StructureDiffers { get; init; }

    [JsonPropertyName("convention")]
    public required WorkspaceLayoutConvention Convention { get; init; }

    [JsonPropertyName("scratch_tab_commands")]
    public required IReadOnlyList<string> ScratchTabCommands { get; init; }

    [JsonPropertyName("rename_commands")]
    public required IReadOnlyList<string> RenameCommands { get; init; }

    [JsonPropertyName("resize_commands")]
    public required IReadOnlyList<string> ResizeCommands { get; init; }

    [JsonPropertyName("commands")]
    public required IReadOnlyList<string> Commands { get; init; }

    [JsonPropertyName("measured_facts")]
    public required IReadOnlyList<string> MeasuredFacts { get; init; }

    [JsonPropertyName("safe_order")]
    public required IReadOnlyList<string> SafeOrder { get; init; }

    [JsonPropertyName("boundaries")]
    public required IReadOnlyList<string> Boundaries { get; init; }
}

internal sealed record WorkspaceLayoutConvention
{
    [JsonPropertyName("left_role")]
    public required string LeftRole { get; init; }

    [JsonPropertyName("left_width")]
    public required decimal LeftWidth { get; init; }

    [JsonPropertyName("top_right_role")]
    public required string TopRightRole { get; init; }

    [JsonPropertyName("bottom_right_role")]
    public required string BottomRightRole { get; init; }

    [JsonPropertyName("right_width")]
    public required decimal RightWidth { get; init; }

    [JsonPropertyName("right_split")]
    public required decimal RightSplit { get; init; }

    [JsonPropertyName("label_vocabulary")]
    public required IReadOnlyList<string> LabelVocabulary { get; init; }
}
