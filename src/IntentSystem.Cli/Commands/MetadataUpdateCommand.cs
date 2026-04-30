using System.Text;
using System.Text.Json;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G208: <c>intent-cli metadata update --root &lt;path&gt;
/// --execution-unit &lt;ID&gt; --mode &lt;mode&gt; [transition-specific flags]
/// [--format text|json]</c>. Writes a single explicit metadata transition
/// for one execution unit after pre-validating with the G207
/// <see cref="MetadataValidateAnalyzer"/>.
///
/// No-mutation invariants (verified by tests):
/// - never invokes <c>NestedProviderLauncher</c>;
/// - never writes outside the bounded set
///   (<c>.intent-cli/issues/&lt;execution-unit&gt;/publish.yaml</c>,
///   <c>.intent-cli/queue-state.json</c>, <c>.intent-cli/runs.jsonl</c>) —
///   asserted by a whole-workspace byte-snapshot diff;
/// - source-scan asserts the writer + result files contain no
///   <c>Process.Start(</c>, no <c>gh issue edit</c>/<c>gh pr edit</c>/
///   <c>gh pr merge</c>/<c>gh pr close</c>/<c>gh pr reopen</c>/
///   <c>gh pr comment</c>/<c>gh pr review</c> literals in executable code,
///   and no <c>resolveReviewThread</c> mutation literal.
///
/// First-and-only supported mode: <c>completed-closeout</c>. Adds a
/// <c>linked_pr</c> object on the selected queue item, sets its
/// <c>state</c> to <c>completed</c>, appends a <c>pr:</c> block to
/// <c>publish.yaml</c> when one is not yet present, and appends a single
/// <c>metadata-update.completed-closeout</c> event to <c>runs.jsonl</c>.
/// Refuses to act when validation surfaces any hard error other than
/// <c>consistency.queue.completed.missing_closure</c> (which this
/// transition exists to fix).
/// </summary>
internal static class MetadataUpdateCommand
{
    private const string FormatText = "text";
    private const string FormatJson = "json";

    /// <summary>
    /// Test sentinel: must NEVER be invoked. Tests assert it remains
    /// uninvoked across all command paths.
    /// </summary>
    public static Func<bool>? NestedProviderLauncher { get; set; }

    /// <summary>
    /// Test seam: tests inject a deterministic timestamp so the runs.jsonl
    /// event is comparable byte-for-byte. Production callers leave this
    /// null and the event uses <see cref="DateTime.UtcNow"/>.
    /// </summary>
    public static Func<DateTime>? UtcNowProvider { get; set; }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never,
    };

    public static int Execute(CliContext context, string[] args, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);

        if (!TryParseArguments(args,
                out var root,
                out var executionUnit,
                out var mode,
                out var linkedPrNumber,
                out var linkedPrRepo,
                out var linkedPrUrl,
                out var headSha,
                out var mergeCommit,
                out var format,
                out var error))
        {
            writer.WriteLine(error);
            return 1;
        }

        // G208 follow-up per #522 review: --root is required for the
        // metadata writer because this is a state-mutating parent-host
        // command. Falling back to the process context would let
        // automation accidentally write whichever repo happens to be
        // the current directory.
        var rootPath = root!;

        // Mode-specific argument coherence.
        if (string.Equals(mode, MetadataUpdateConstants.Modes.CompletedCloseout, StringComparison.Ordinal))
        {
            if (linkedPrNumber is null)
            {
                writer.WriteLine($"--linked-pr is required for mode '{mode}' (the PR number to attach as closeout evidence).");
                return 1;
            }
        }

        // Pre-validate with the G207 analyzer.
        MetadataValidateInputs validateInputs;
        try
        {
            validateInputs = LoadValidateInputs(rootPath, executionUnit!);
        }
        catch (Exception exception) when (
            exception is IOException
            or UnauthorizedAccessException)
        {
            writer.WriteLine($"failed to load metadata for {executionUnit} under {rootPath}: {exception.Message}");
            return 1;
        }

        var validation = MetadataValidateAnalyzer.Analyze(validateInputs);
        var hardErrors = FilterHardErrors(validation.Errors, mode!);
        if (hardErrors.Count > 0)
        {
            var rejection = new MetadataUpdateResult
            {
                Valid = false,
                ExecutionUnit = executionUnit!,
                Mode = mode!,
                UpdatedFiles = Array.Empty<string>(),
                EventAppended = null,
                Errors = new List<MetadataValidateFinding>(hardErrors)
                {
                    new()
                    {
                        Code = MetadataUpdateConstants.Codes.ValidationRejected,
                        Message = "metadata update refused — pre-validation surfaced hard errors that the requested mode cannot resolve.",
                        Path = string.Empty,
                    }
                },
                Warnings = validation.Warnings,
            };
            EmitResult(writer, rejection, format);
            return 1;
        }

        // Apply the transition. Each branch is responsible for both the
        // file mutations and surfacing any mode-specific transition errors
        // (e.g. publish.yaml already has a `pr:` block).
        MetadataUpdateResult result;
        try
        {
            result = mode switch
            {
                MetadataUpdateConstants.Modes.CompletedCloseout =>
                    ApplyCompletedCloseout(
                        rootPath,
                        executionUnit!,
                        linkedPrNumber!.Value,
                        linkedPrRepo,
                        linkedPrUrl,
                        headSha,
                        mergeCommit,
                        validation.Warnings),
                _ => throw new InvalidOperationException($"unsupported mode '{mode}'"),
            };
        }
        catch (IOException exception)
        {
            writer.WriteLine($"failed to write metadata update for {executionUnit}: {exception.Message}");
            return 1;
        }
        catch (InvalidOperationException exception)
        {
            writer.WriteLine($"metadata update refused: {exception.Message}");
            return 1;
        }

        EmitResult(writer, result, format);
        return result.Valid ? 0 : 1;
    }

    private static IReadOnlyList<MetadataValidateFinding> FilterHardErrors(
        IReadOnlyList<MetadataValidateFinding> errors,
        string mode)
    {
        // Errors the requested mode is intended to resolve are NOT hard
        // for that mode. completed-closeout exists to fix
        // CompletedMissingClosure, so we accept that error pre-write.
        var fixable = mode switch
        {
            MetadataUpdateConstants.Modes.CompletedCloseout =>
                new[] { MetadataValidateConstants.Codes.CompletedMissingClosure },
            _ => Array.Empty<string>(),
        };

        return errors
            .Where(e => !fixable.Contains(e.Code, StringComparer.Ordinal))
            .ToArray();
    }

    private static MetadataValidateInputs LoadValidateInputs(string rootPath, string executionUnit)
    {
        var unitDir = Path.Combine(rootPath, ".intent-cli", "issues", executionUnit);
        var queueStatePath = Path.Combine(rootPath, ".intent-cli", "queue-state.json");
        return new MetadataValidateInputs
        {
            ExecutionUnit = executionUnit,
            PacketYaml = ReadIfExists(Path.Combine(unitDir, "packet.yaml")),
            GithubBodyMarkdown = ReadIfExists(Path.Combine(unitDir, "github-body.md")),
            ReviewContextMarkdown = ReadIfExists(Path.Combine(unitDir, "review-context.md")),
            ImplementationMarkdown = ReadIfExists(Path.Combine(unitDir, "implementation.md")),
            PublishYaml = ReadIfExists(Path.Combine(unitDir, "publish.yaml")),
            QueueStateJson = ReadIfExists(queueStatePath),
        };
    }

    private static string? ReadIfExists(string path) =>
        File.Exists(path) ? File.ReadAllText(path) : null;

    /// <summary>
    /// G208 completed-closeout transition. Performs three writes:
    /// 1. queue-state.json: set <c>state="completed"</c> on the matching
    ///    item; add object-shaped <c>linked_pr</c>.
    /// 2. publish.yaml: append a <c>pr:</c> block when none is present.
    ///    Refuse with <see cref="MetadataUpdateConstants.Codes.PublishAlreadyHasPr"/>
    ///    if a pr block is already present (no clobber).
    /// 3. runs.jsonl: append one structured event line.
    /// </summary>
    private static MetadataUpdateResult ApplyCompletedCloseout(
        string rootPath,
        string executionUnit,
        int linkedPrNumber,
        string? linkedPrRepo,
        string? linkedPrUrl,
        string? headSha,
        string? mergeCommit,
        IReadOnlyList<MetadataValidateFinding> warnings)
    {
        var unitDir = Path.Combine(rootPath, ".intent-cli", "issues", executionUnit);
        var queueStatePath = Path.Combine(rootPath, ".intent-cli", "queue-state.json");
        var publishPath = Path.Combine(unitDir, "publish.yaml");
        var runsPath = Path.Combine(rootPath, ".intent-cli", "runs.jsonl");

        var updatedFiles = new List<string>();
        var errors = new List<MetadataValidateFinding>();

        // ---- queue-state.json ---------------------------------------------
        if (!File.Exists(queueStatePath))
        {
            errors.Add(new MetadataValidateFinding
            {
                Code = MetadataValidateConstants.Codes.QueueStateMissing,
                Message = $"required queue-state file not found: {queueStatePath}",
                Path = ".intent-cli/queue-state.json",
            });
            return Failure(executionUnit, errors, warnings);
        }

        var queueStateRaw = File.ReadAllText(queueStatePath);
        using var queueDoc = JsonDocument.Parse(queueStateRaw);
        var queueRoot = queueDoc.RootElement;

        var (newQueueText, alreadyCompleted) = ApplyCompletedCloseoutToQueueState(
            queueRoot,
            executionUnit,
            linkedPrNumber,
            linkedPrRepo,
            linkedPrUrl,
            headSha,
            mergeCommit);

        if (alreadyCompleted)
        {
            errors.Add(new MetadataValidateFinding
            {
                Code = MetadataUpdateConstants.Codes.AlreadyCompleted,
                Message = $"queue-state entry '{executionUnit}' is already marked completed; refusing to clobber.",
                Path = ".intent-cli/queue-state.json",
            });
            return Failure(executionUnit, errors, warnings);
        }

        // ---- publish.yaml --------------------------------------------------
        var publishRaw = ReadIfExists(publishPath) ?? string.Empty;
        if (PublishHasPrBlock(publishRaw))
        {
            errors.Add(new MetadataValidateFinding
            {
                Code = MetadataUpdateConstants.Codes.PublishAlreadyHasPr,
                Message = $"publish.yaml already contains a 'pr:' block; refusing to clobber.",
                Path = $".intent-cli/issues/{executionUnit}/publish.yaml",
            });
            return Failure(executionUnit, errors, warnings);
        }

        // No queue/publish failure: write the updates atomically (per file).
        File.WriteAllText(queueStatePath, newQueueText);
        updatedFiles.Add(".intent-cli/queue-state.json");

        var newPublishText = AppendPrBlockToPublishYaml(
            publishRaw,
            linkedPrNumber,
            linkedPrRepo,
            linkedPrUrl,
            headSha,
            mergeCommit);
        File.WriteAllText(publishPath, newPublishText);
        updatedFiles.Add($".intent-cli/issues/{executionUnit}/publish.yaml");

        // ---- runs.jsonl ----------------------------------------------------
        var ts = (UtcNowProvider?.Invoke() ?? DateTime.UtcNow)
            .ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.CultureInfo.InvariantCulture);
        var eventDoc = new Dictionary<string, object?>
        {
            ["event"] = MetadataUpdateConstants.EventNames.CompletedCloseout,
            ["execution_unit"] = executionUnit,
            ["linked_pr_number"] = linkedPrNumber,
            ["timestamp"] = ts,
        };
        if (!string.IsNullOrEmpty(linkedPrRepo)) eventDoc["linked_pr_repo"] = linkedPrRepo;
        if (!string.IsNullOrEmpty(linkedPrUrl)) eventDoc["linked_pr_url"] = linkedPrUrl;
        if (!string.IsNullOrEmpty(headSha)) eventDoc["head_sha"] = headSha;
        if (!string.IsNullOrEmpty(mergeCommit)) eventDoc["merge_commit"] = mergeCommit;

        var eventLine = JsonSerializer.Serialize(eventDoc) + "\n";
        File.AppendAllText(runsPath, eventLine);
        updatedFiles.Add(".intent-cli/runs.jsonl");

        return new MetadataUpdateResult
        {
            Valid = true,
            ExecutionUnit = executionUnit,
            Mode = MetadataUpdateConstants.Modes.CompletedCloseout,
            UpdatedFiles = updatedFiles,
            EventAppended = MetadataUpdateConstants.EventNames.CompletedCloseout,
            Errors = errors,
            Warnings = warnings,
        };
    }

    private static MetadataUpdateResult Failure(
        string executionUnit,
        IReadOnlyList<MetadataValidateFinding> errors,
        IReadOnlyList<MetadataValidateFinding> warnings) =>
        new()
        {
            Valid = false,
            ExecutionUnit = executionUnit,
            Mode = MetadataUpdateConstants.Modes.CompletedCloseout,
            UpdatedFiles = Array.Empty<string>(),
            EventAppended = null,
            Errors = errors,
            Warnings = warnings,
        };

    /// <summary>
    /// Apply the completed-closeout transition to the queue-state JSON.
    /// Walks the items array (or root array) and rewrites the matching
    /// entry. Returns the new JSON text and a flag indicating the entry
    /// was already completed (refuse-to-clobber signal).
    ///
    /// G208 follow-up per #521 review: head_sha and merge_commit are
    /// closeout evidence the host-side automation reads from
    /// queue-state.json (not just publish.yaml / runs.jsonl). When
    /// supplied, they are written onto the matching entry alongside the
    /// state and linked_pr update.
    /// </summary>
    internal static (string Json, bool AlreadyCompleted) ApplyCompletedCloseoutToQueueState(
        JsonElement queueRoot,
        string executionUnit,
        int linkedPrNumber,
        string? linkedPrRepo,
        string? linkedPrUrl,
        string? headSha,
        string? mergeCommit)
    {
        // Convert root to a mutable in-memory model.
        var rootDict = JsonElementToDictionary(queueRoot);
        if (rootDict is null)
        {
            // Root is an array of items.
            var itemsArr = JsonElementToList(queueRoot);
            if (itemsArr is null)
            {
                throw new InvalidOperationException(
                    "queue-state.json has unexpected shape; expected object with items[] or array.");
            }
            var (changedArr, alreadyArr) = MutateItemsArray(
                itemsArr, executionUnit, linkedPrNumber, linkedPrRepo, linkedPrUrl, headSha, mergeCommit);
            return (Serialize(changedArr), alreadyArr);
        }

        // Object form: items array nested under "items" or "entries".
        string itemsKey = rootDict.ContainsKey("items") ? "items"
            : rootDict.ContainsKey("entries") ? "entries"
            : throw new InvalidOperationException(
                "queue-state.json object form must have 'items' or 'entries' array.");
        var items = (List<object?>?)rootDict[itemsKey]
            ?? throw new InvalidOperationException(
                $"queue-state.json {itemsKey} key is not an array.");
        var (changed, alreadyCompleted) = MutateItemsArray(
            items, executionUnit, linkedPrNumber, linkedPrRepo, linkedPrUrl, headSha, mergeCommit);
        rootDict[itemsKey] = changed;
        return (Serialize(rootDict), alreadyCompleted);
    }

    private static (List<object?>, bool) MutateItemsArray(
        List<object?> items,
        string executionUnit,
        int linkedPrNumber,
        string? linkedPrRepo,
        string? linkedPrUrl,
        string? headSha,
        string? mergeCommit)
    {
        for (int i = 0; i < items.Count; i++)
        {
            if (items[i] is Dictionary<string, object?> entry
                && entry.TryGetValue("execution_unit", out var unit)
                && unit is string unitStr
                && string.Equals(unitStr, executionUnit, StringComparison.Ordinal))
            {
                var currentState = entry.TryGetValue("state", out var s) && s is string str
                    ? str
                    : null;
                if (string.Equals(currentState, "completed", StringComparison.OrdinalIgnoreCase))
                {
                    return (items, true);
                }
                entry["state"] = "completed";
                var prObj = new Dictionary<string, object?>
                {
                    ["number"] = linkedPrNumber,
                };
                if (!string.IsNullOrEmpty(linkedPrRepo)) prObj["repo"] = linkedPrRepo;
                if (!string.IsNullOrEmpty(linkedPrUrl)) prObj["url"] = linkedPrUrl;
                entry["linked_pr"] = prObj;

                // G208 follow-up: record closeout evidence on the queue
                // entry too. Host-side automation and historical closeout
                // checks read these from queue-state.json.
                if (!string.IsNullOrEmpty(headSha))
                {
                    entry["head_sha"] = headSha;
                }
                if (!string.IsNullOrEmpty(mergeCommit))
                {
                    entry["merge_commit"] = mergeCommit;
                }

                return (items, false);
            }
        }
        throw new InvalidOperationException(
            $"queue-state.json has no entry for execution unit '{executionUnit}' to update.");
    }

    private static Dictionary<string, object?>? JsonElementToDictionary(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }
        var dict = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var prop in element.EnumerateObject())
        {
            dict[prop.Name] = JsonElementToObject(prop.Value);
        }
        return dict;
    }

    private static List<object?>? JsonElementToList(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Array)
        {
            return null;
        }
        var list = new List<object?>(element.GetArrayLength());
        foreach (var item in element.EnumerateArray())
        {
            list.Add(JsonElementToObject(item));
        }
        return list;
    }

    private static object? JsonElementToObject(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Object => JsonElementToDictionary(element),
            JsonValueKind.Array => JsonElementToList(element),
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.TryGetInt64(out var i)
                ? (object)i
                : element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            _ => null,
        };
    }

    private static string Serialize(object? value) =>
        JsonSerializer.Serialize(value, JsonOptions);

    private static bool PublishHasPrBlock(string yaml)
    {
        // Top-level `pr:` line. We only care about the LHS being literally
        // `pr` at indent 0 (or 0 leading whitespace), so a tight regex.
        return System.Text.RegularExpressions.Regex.IsMatch(
            yaml,
            @"^\s*pr\s*:",
            System.Text.RegularExpressions.RegexOptions.Multiline);
    }

    private static string AppendPrBlockToPublishYaml(
        string existing,
        int prNumber,
        string? prRepo,
        string? prUrl,
        string? headSha,
        string? mergeCommit)
    {
        var sb = new StringBuilder(existing);
        if (existing.Length > 0 && !existing.EndsWith("\n", StringComparison.Ordinal))
        {
            sb.Append('\n');
        }
        sb.Append("pr:\n");
        sb.Append("  number: ").Append(prNumber).Append('\n');
        if (!string.IsNullOrEmpty(prRepo))
        {
            sb.Append("  repo: ").Append(prRepo).Append('\n');
        }
        if (!string.IsNullOrEmpty(prUrl))
        {
            sb.Append("  url: ").Append(prUrl).Append('\n');
        }
        sb.Append("  status: completed\n");
        if (!string.IsNullOrEmpty(headSha))
        {
            sb.Append("  head_sha: ").Append(headSha).Append('\n');
        }
        if (!string.IsNullOrEmpty(mergeCommit))
        {
            sb.Append("  merge_commit: ").Append(mergeCommit).Append('\n');
        }
        return sb.ToString();
    }

    private static void EmitResult(TextWriter writer, MetadataUpdateResult result, string format)
    {
        if (string.Equals(format, FormatJson, StringComparison.Ordinal))
        {
            writer.WriteLine(JsonSerializer.Serialize(result, JsonOptions));
            return;
        }

        writer.WriteLine($"# Metadata update for {result.ExecutionUnit}");
        writer.WriteLine();
        writer.WriteLine($"- valid: {result.Valid.ToString().ToLowerInvariant()}");
        writer.WriteLine($"- mode: {result.Mode}");
        writer.WriteLine($"- updated_files: {result.UpdatedFiles.Count}");
        if (!string.IsNullOrEmpty(result.EventAppended))
        {
            writer.WriteLine($"- event_appended: {result.EventAppended}");
        }
        writer.WriteLine();

        if (result.UpdatedFiles.Count > 0)
        {
            writer.WriteLine("## Updated files");
            foreach (var file in result.UpdatedFiles)
            {
                writer.WriteLine($"- {file}");
            }
            writer.WriteLine();
        }

        if (result.Errors.Count > 0)
        {
            writer.WriteLine("## Errors");
            foreach (var f in result.Errors)
            {
                writer.WriteLine($"- [{f.Code}] {f.Message} ({f.Path})");
            }
        }
    }

    private static bool TryParseArguments(
        string[] args,
        out string? root,
        out string? executionUnit,
        out string? mode,
        out int? linkedPrNumber,
        out string? linkedPrRepo,
        out string? linkedPrUrl,
        out string? headSha,
        out string? mergeCommit,
        out string format,
        out string error)
    {
        root = null;
        executionUnit = null;
        mode = null;
        linkedPrNumber = null;
        linkedPrRepo = null;
        linkedPrUrl = null;
        headSha = null;
        mergeCommit = null;
        format = FormatText;
        error = string.Empty;

        for (var i = 0; i < args.Length; i++)
        {
            var a = args[i];
            switch (a)
            {
                case "--root":
                    if (!Next(args, i, out root, out error, "--root"))
                        return false;
                    i++;
                    break;
                case "--execution-unit":
                    if (!Next(args, i, out executionUnit, out error, "--execution-unit"))
                        return false;
                    i++;
                    break;
                case "--mode":
                    if (!Next(args, i, out mode, out error, "--mode"))
                        return false;
                    i++;
                    break;
                case "--linked-pr":
                    if (!Next(args, i, out var prRaw, out error, "--linked-pr"))
                        return false;
                    if (!int.TryParse(prRaw,
                            System.Globalization.NumberStyles.Integer,
                            System.Globalization.CultureInfo.InvariantCulture,
                            out var prInt)
                        || prInt <= 0)
                    {
                        error = $"--linked-pr must be a positive integer (got '{prRaw}').";
                        return false;
                    }
                    linkedPrNumber = prInt;
                    i++;
                    break;
                case "--linked-pr-repo":
                    if (!Next(args, i, out linkedPrRepo, out error, "--linked-pr-repo"))
                        return false;
                    i++;
                    break;
                case "--linked-pr-url":
                    if (!Next(args, i, out linkedPrUrl, out error, "--linked-pr-url"))
                        return false;
                    i++;
                    break;
                case "--head-sha":
                    if (!Next(args, i, out headSha, out error, "--head-sha"))
                        return false;
                    i++;
                    break;
                case "--merge-commit":
                    if (!Next(args, i, out mergeCommit, out error, "--merge-commit"))
                        return false;
                    i++;
                    break;
                case "--format":
                    if (!Next(args, i, out var fmt, out error, "--format"))
                        return false;
                    if (!string.Equals(fmt, FormatText, StringComparison.Ordinal)
                        && !string.Equals(fmt, FormatJson, StringComparison.Ordinal))
                    {
                        error = $"--format must be 'text' or 'json' (got '{fmt}').";
                        return false;
                    }
                    format = fmt!;
                    i++;
                    break;
                default:
                    error = $"Unknown argument '{a}'. Supported: --root <path> --execution-unit <ID> --mode <mode> [--linked-pr <n>] [--linked-pr-repo <repo>] [--linked-pr-url <url>] [--head-sha <sha>] [--merge-commit <sha>] [--format text|json].";
                    return false;
            }
        }

        // G208 follow-up per #522 review: --root is required for this
        // state-mutating parent-host writer. No falling back to process
        // context — automation must name the target packet root
        // explicitly so it cannot accidentally write a different repo.
        if (string.IsNullOrWhiteSpace(root))
        {
            error = "--root is required (e.g. --root /path/to/parent-host-repo).";
            return false;
        }
        if (string.IsNullOrWhiteSpace(executionUnit))
        {
            error = "--execution-unit is required (e.g. --execution-unit G208).";
            return false;
        }
        if (string.IsNullOrWhiteSpace(mode))
        {
            error = $"--mode is required (e.g. --mode {MetadataUpdateConstants.Modes.CompletedCloseout}).";
            return false;
        }
        if (!string.Equals(mode, MetadataUpdateConstants.Modes.CompletedCloseout, StringComparison.Ordinal))
        {
            error = $"unsupported --mode '{mode}'. Supported: {MetadataUpdateConstants.Modes.CompletedCloseout}.";
            return false;
        }

        return true;
    }

    private static bool Next(string[] args, int i, out string? value, out string error, string name)
    {
        if (i + 1 >= args.Length || string.IsNullOrWhiteSpace(args[i + 1]))
        {
            value = null;
            error = $"{name} requires a value.";
            return false;
        }
        value = args[i + 1];
        error = string.Empty;
        return true;
    }
}
