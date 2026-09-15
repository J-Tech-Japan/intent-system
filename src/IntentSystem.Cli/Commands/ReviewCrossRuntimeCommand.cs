using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G834: <c>intent-cli review cross-runtime request|record|status</c> — one
/// review-group handler that dispatches its three subcommands itself.
/// <list type="bullet">
/// <item><c>request</c> renders <c>prompt.md</c>, <c>verdict.schema.json</c>, and
///   the pinned <c>invocation.txt</c> the seat runs; it reads only the packet
///   files and never executes a process.</item>
/// <item><c>record</c> validates a reviewer verdict bound to the exact head and,
///   with <c>--write</c>, stores a create-new record plus a raw copy.</item>
/// <item><c>status</c> reports resolution, declaration, records, and the gate
///   decision the approved transition applies.</item>
/// </list>
/// intent-cli never starts, launches, or manages a reviewer: the seat runs the
/// rendered invocation and posts the rendered comment with <c>gh</c>.
/// </summary>
internal static class ReviewCrossRuntimeCommand
{
    internal const string CommandName = "review cross-runtime";
    private const string FormatJson = "json";
    private const string FormatMarkdown = "markdown";

    internal const string RequestUsage =
        "Usage: intent-cli review cross-runtime request --repo <owner/repo> --pr <n> --head-sha <40-hex> --execution-unit <unit> --runtime codex|claude|cursor --clone <read-only-clone-path> --out-dir <dir> [--format json|markdown]";

    internal const string RecordUsage =
        "Usage: intent-cli review cross-runtime record --repo <owner/repo> --pr <n> --head-sha <40-hex> --execution-unit <unit> --kind implementation --runtime codex|claude|cursor --runtime-version <text> --verdict-file <path> [--comment-out <path>] [--write] [--format json|markdown]";

    internal const string StatusUsage =
        "Usage: intent-cli review cross-runtime status --repo <owner/repo> --pr <n> --head-sha <40-hex> --execution-unit <unit> [--format json|markdown]";

    internal const string NoExecutionBoundary =
        "intent-cli renders text and records evidence only; the seat runs the reviewer and posts with gh. intent-cli does not start, launch, or manage any agent or provider process.";

    internal const string TermsNotice =
        "Confirming each vendor's automation terms for headless reviewer runs is the operator's responsibility; intent-cli does not assert that any use is permitted.";

    /// <summary>
    /// Test seam mirroring the G187 <c>NestedProviderLauncher</c> sentinel. None of
    /// the three subcommands ever invokes it.
    /// </summary>
    public static Func<bool>? NestedProviderLauncher { get; set; }

    /// <summary>
    /// Process-runner seam in the <c>NotifyCommand.ProcessRunnerFactory</c> shape.
    /// The request, record, and status paths never construct a process runner;
    /// tests install a throwing factory to prove it.
    /// </summary>
    internal static Func<INotifyProcessRunner>? ProcessRunnerFactory { get; set; }

    /// <summary>Test seam for <c>recorded_at</c>.</summary>
    internal static Func<DateTimeOffset>? Clock { get; set; }

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

        if (args.Length == 0 || (args.Length == 1 && args[0] is "--help" or "help"))
        {
            WriteHelp(writer);
            return args.Length == 0 ? 1 : 0;
        }

        if (!CommandRouter.ReviewCrossRuntimeSubcommands.TryGetValue(args[0], out var handler))
        {
            writer.WriteLine($"Unknown subcommand '{args[0]}' for '{CommandName}'.");
            WriteHelp(writer);
            return 1;
        }

        return handler(context, args[1..], writer);
    }

    // ── request ────────────────────────────────────────────────────────

    internal static int ExecuteRequest(CliContext context, string[] args, TextWriter writer)
    {
        if (IsHelp(args))
        {
            writer.WriteLine(RequestUsage);
            return 0;
        }

        if (!TryParse(args, RequestFlags, writer, RequestUsage, out var options, out var format))
        {
            return 1;
        }

        if (!RequireHostRoot(context, writer, format, "request"))
        {
            return 1;
        }

        if (!TryCommonArguments(options, writer, format, "request", out var repo, out var pr, out var head, out var unit)
            || !TryRuntime(options, writer, format, "request", out var runtime))
        {
            return 1;
        }

        if (!TryRequired(options, "--clone", writer, format, "request", out var cloneArgument)
            || !TryRequired(options, "--out-dir", writer, format, "request", out var outDirArgument))
        {
            return 1;
        }

        foreach (var (flag, value) in new[] { ("--clone", cloneArgument), ("--out-dir", outDirArgument) })
        {
            if (!CrossRuntimeReviewPaths.IsRenderablePath(value))
            {
                return Refuse(writer, format, "request", CrossRuntimeReviewCauses.PathInvalid,
                    $"{flag} contains a newline or NUL and cannot be rendered as one shell argument.",
                    $"pass a {flag} path without newline or NUL characters.");
            }
        }

        var clone = ResolvePath(context, cloneArgument);
        var outDir = ResolvePath(context, outDirArgument);
        var packetDirectory = CrossRuntimeReviewPaths.PacketDirectory(context.RepoRoot, unit);
        var body = Path.Combine(packetDirectory, "github-body.md");
        var reviewContext = Path.Combine(packetDirectory, "review-context.md");
        var implementation = Path.Combine(packetDirectory, "implementation.md");
        var missingPacketFiles = new[] { body, reviewContext, implementation }.Where(path => !File.Exists(path)).ToArray();
        if (missingPacketFiles.Length > 0)
        {
            return Refuse(writer, format, "request", CrossRuntimeReviewCauses.PacketMissing,
                $"packet review input(s) do not exist: {string.Join(", ", missingPacketFiles)}; a reviewer cannot review against an incomplete packet.",
                $"run from the host root that holds `.intent-cli/issues/{unit}/` with github-body.md, review-context.md, and implementation.md.");
        }

        if (File.Exists(outDir))
        {
            return Refuse(writer, format, "request", CrossRuntimeReviewCauses.PathInvalid,
                $"--out-dir '{outDir}' is a file.", "pass a new or empty directory.");
        }

        if (Directory.Exists(outDir))
        {
            var foreign = Directory.EnumerateFileSystemEntries(outDir)
                .Select(Path.GetFileName)
                .Where(name => !CrossRuntimeReviewFiles.Rendered.Contains(name, StringComparer.Ordinal))
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();
            if (foreign.Length > 0)
            {
                return Refuse(writer, format, "request", CrossRuntimeReviewCauses.OutDirNotEmpty,
                    $"--out-dir '{outDir}' contains other entries: {string.Join(", ", foreign)}.",
                    "pass a new or empty directory so a stale verdict can never be mixed with this request.");
            }
        }

        var prompt = RenderPrompt(repo, pr, head, unit, clone, body, reviewContext, implementation);
        var invocation = CrossRuntimeReviewRuntimes.InvocationLabel(runtime) + "\n"
            + CrossRuntimeReviewRuntimes.RenderInvocation(runtime, clone, outDir) + "\n";

        var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        Directory.CreateDirectory(outDir);
        File.WriteAllText(Path.Combine(outDir, CrossRuntimeReviewFiles.Prompt), prompt, utf8);
        File.WriteAllText(Path.Combine(outDir, CrossRuntimeReviewFiles.Schema), CrossRuntimeReviewVerdict.SchemaJson, utf8);
        File.WriteAllText(Path.Combine(outDir, CrossRuntimeReviewFiles.Invocation), invocation, utf8);

        var result = new CrossRuntimeReviewRequestResult
        {
            Command = $"{CommandName} request",
            Outcome = "rendered",
            Repo = repo,
            Pr = pr,
            HeadSha = head,
            ExecutionUnit = unit,
            Runtime = runtime,
            OutDir = outDir,
            Files = CrossRuntimeReviewFiles.Rendered.Select(name => Path.Combine(outDir, name)).ToArray(),
            Invocation = invocation.TrimEnd('\n'),
            RunBy = "seat",
            RawVerdictFile = Path.Combine(outDir, CrossRuntimeReviewFiles.RawVerdict),
            ReadOnlyEnforcement = CrossRuntimeReviewRuntimes.ReadOnlyEnforcement[runtime],
            NoExecutionBoundary = NoExecutionBoundary,
            Terms = TermsNotice,
        };

        if (format == FormatJson)
        {
            writer.WriteLine(JsonSerializer.Serialize(result, JsonOptions));
        }
        else
        {
            writer.WriteLine("# Cross-runtime review request (G834)");
            writer.WriteLine();
            writer.WriteLine($"- repo: {result.Repo}");
            writer.WriteLine($"- pr: {result.Pr.ToString(CultureInfo.InvariantCulture)}");
            writer.WriteLine($"- head sha: {result.HeadSha}");
            writer.WriteLine($"- execution unit: {result.ExecutionUnit}");
            writer.WriteLine($"- runtime: {result.Runtime}");
            foreach (var file in result.Files)
            {
                writer.WriteLine($"- rendered: {file}");
            }

            writer.WriteLine();
            writer.WriteLine("Run by the seat:");
            writer.WriteLine();
            writer.WriteLine("```sh");
            writer.WriteLine(result.Invocation);
            writer.WriteLine("```");
            writer.WriteLine();
            writer.WriteLine($"Read-only enforcement: {result.ReadOnlyEnforcement}");
            writer.WriteLine(result.NoExecutionBoundary);
            writer.WriteLine(result.Terms);
        }

        return 0;
    }

    internal static string RenderPrompt(
        string repo,
        int pr,
        string head,
        string unit,
        string clone,
        string body,
        string reviewContext,
        string implementation)
    {
        var q = CrossRuntimeReviewPaths.ShellQuote;
        var prText = pr.ToString(CultureInfo.InvariantCulture);
        var builder = new StringBuilder();
        builder.Append($"# Implementation review: {repo} PR #{prText} ({unit})\n\n");
        builder.Append("You are an independent reviewer on a different runtime from the author. ");
        builder.Append($"Review pull request #{prText} in {repo} at head {head}.\n\n");
        builder.Append("## Inputs\n\n");
        builder.Append($"- Issue contract (packet github-body.md): {q(body)}\n");
        builder.Append($"- Review context (packet review-context.md): {q(reviewContext)}\n");
        builder.Append($"- Implementation notes (packet implementation.md): {q(implementation)}\n");
        builder.Append($"- Pull request: {repo}#{prText}\n");
        builder.Append($"- Head SHA: {head}\n");
        builder.Append($"- Read-only clone checked out at that head: {q(clone)}\n\n");
        builder.Append("## Rules\n\n");
        builder.Append("- Work read-only in the clone. Do not edit, create, or delete files; do not commit, push, or post anything.\n");
        builder.Append($"- If you can run commands, first confirm that `git -C {q(clone)} rev-parse HEAD` prints {head}; if it does not, return request-changes with one finding that names the mismatch. If your runtime cannot run commands, say so in notes.\n");
        builder.Append("- The change under review is the commits on HEAD that are not on the base branch named in the issue contract. If you cannot run git, review the target paths the issue contract names and say in notes that you could not list the commits.\n");
        builder.Append("- Judge the change against the issue contract and the review context: correctness, contract conformance, tests, and every blocking item the review context names.\n");
        builder.Append("- A blocking finding names the file, the line, and the concrete scenario that fails. Put non-blocking observations in notes.\n\n");
        builder.Append("## Output\n\n");
        builder.Append("Return only one JSON object that matches the schema below, with no text before or after it.\n\n");
        builder.Append($"- \"head_sha\": echo the head SHA you reviewed ({head}).\n");
        builder.Append("- \"verdict\": \"approve\" only when there is no blocking finding; \"blocking_findings\" must then be [].\n");
        builder.Append("- \"verdict\": \"request-changes\" when there is at least one blocking finding.\n");
        builder.Append("- \"notes\": non-blocking observations; may be [].\n\n");
        builder.Append("```json\n");
        builder.Append(CrossRuntimeReviewVerdict.SchemaJson);
        builder.Append("```\n");
        return builder.ToString();
    }

    // ── record ─────────────────────────────────────────────────────────

    internal static int ExecuteRecord(CliContext context, string[] args, TextWriter writer)
    {
        if (IsHelp(args))
        {
            writer.WriteLine(RecordUsage);
            return 0;
        }

        if (!TryParse(args, RecordFlags, writer, RecordUsage, out var options, out var format))
        {
            return 1;
        }

        if (!RequireHostRoot(context, writer, format, "record"))
        {
            return 1;
        }

        if (!TryCommonArguments(options, writer, format, "record", out var repo, out var pr, out var head, out var unit)
            || !TryRuntime(options, writer, format, "record", out var runtime)
            || !TryRequired(options, "--kind", writer, format, "record", out var kind)
            || !TryRequired(options, "--runtime-version", writer, format, "record", out var runtimeVersion)
            || !TryRequired(options, "--verdict-file", writer, format, "record", out var verdictFileArgument))
        {
            return 1;
        }

        if (!string.Equals(kind, CrossRuntimeReviewRecord.KindImplementation, StringComparison.Ordinal))
        {
            return Refuse(writer, format, "record", CrossRuntimeReviewCauses.ArgumentInvalid,
                $"--kind must be '{CrossRuntimeReviewRecord.KindImplementation}' (got '{kind}'); design review is G835.",
                $"pass --kind {CrossRuntimeReviewRecord.KindImplementation}.");
        }

        var write = options.ContainsKey("--write");
        var resolution = CrossRuntimeReviewTeamResolver.Resolve(
            context.RepoRoot, repo, pr, unit, "pass the unit the host queue links to this PR with --execution-unit");
        if (!resolution.Resolved)
        {
            return RefuseResolution(writer, format, "record", resolution);
        }

        if (!context.Config.CrossRuntimeReview.TryGetDeclared(resolution.Domain, resolution.Team, out var declaration))
        {
            return Refuse(writer, format, "record", CrossRuntimeReviewCauses.NotDeclared,
                $"team '{resolution.Domain}/{resolution.Team}' is not declared in [[cross_runtime_review.teams]]; its PRs need only the independent same-runtime subagent review.",
                "declare the team in `.intent-cli/config.toml` under [[cross_runtime_review.teams]] if cross-runtime review is required.");
        }

        var verdictFile = ResolvePath(context, verdictFileArgument);
        byte[] raw;
        try
        {
            raw = File.ReadAllBytes(verdictFile);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Refuse(writer, format, "record", CrossRuntimeReviewCauses.VerdictInvalid,
                $"verdict file '{verdictFile}' could not be read: {exception.Message}",
                "pass the file the rendered invocation wrote (verdict.raw.json).");
        }

        string content;
        try
        {
            content = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(raw);
        }
        catch (DecoderFallbackException exception)
        {
            return Refuse(writer, format, "record", CrossRuntimeReviewCauses.VerdictInvalid,
                $"verdict file '{verdictFile}' is not UTF-8: {exception.Message}", "pass the file the rendered invocation wrote.");
        }

        if (!CrossRuntimeReviewVerdict.TryParse(runtime, content, out var verdict, out var verdictError))
        {
            return Refuse(writer, format, "record", CrossRuntimeReviewCauses.VerdictInvalid,
                $"verdict file '{verdictFile}' is invalid for runtime '{runtime}': {verdictError}",
                "re-run the reviewer with the pinned invocation; never hand-write a verdict.");
        }

        if (!string.Equals(verdict.HeadSha, head, StringComparison.OrdinalIgnoreCase))
        {
            return Refuse(writer, format, "record", CrossRuntimeReviewCauses.HeadMismatch,
                $"the verdict echoes head '{verdict.HeadSha}' but --head-sha is '{head}'.",
                "re-run the review against a clone checked out at the gated head.");
        }

        var recordedAt = (Clock ?? (() => DateTimeOffset.UtcNow))().ToUniversalTime();
        var draft = new CrossRuntimeReviewRecord
        {
            ArtifactKind = CrossRuntimeReviewRecord.ArtifactKindValue,
            Repo = repo,
            Pr = pr,
            HeadSha = head.ToLowerInvariant(),
            ExecutionUnit = resolution.ExecutionUnit!,
            Domain = resolution.Domain!,
            Team = resolution.Team!,
            Kind = kind,
            Runtime = runtime,
            RuntimeVersion = runtimeVersion,
            ConductorRuntime = declaration.ConductorRuntime,
            Relation = CrossRuntimeReviewRecord.RelationFor(runtime, declaration.ConductorRuntime),
            Verdict = verdict.Verdict,
            BlockingFindings = verdict.BlockingFindings,
            Notes = verdict.Notes,
            RecordedAt = recordedAt,
            RawVerdictFile = string.Empty,
            RawVerdictSha256 = CrossRuntimeReviewStore.Sha256Hex(raw),
        };
        var record = draft with { RawVerdictFile = CrossRuntimeReviewStore.RawRelativePath(draft) };
        var recordRelative = CrossRuntimeReviewStore.RecordRelativePath(record);
        var commentBody = RenderCommentBody(record, recordRelative);

        string? commentOut = null;
        if (write)
        {
            var stored = CrossRuntimeReviewStore.Write(context.RepoRoot, record, raw);
            if (!stored.Written)
            {
                return Refuse(writer, format, "record",
                    stored.Error?.StartsWith(CrossRuntimeReviewCauses.RecordCollision, StringComparison.Ordinal) == true
                        ? CrossRuntimeReviewCauses.RecordCollision
                        : CrossRuntimeReviewCauses.ArgumentInvalid,
                    stored.Error ?? "record could not be written.",
                    "re-run `record --write`; an existing record is never overwritten.");
            }

            if (options.TryGetValue("--comment-out", out var commentOutArgument))
            {
                commentOut = ResolvePath(context, commentOutArgument);
                var commentDirectory = Path.GetDirectoryName(commentOut);
                if (!string.IsNullOrEmpty(commentDirectory))
                {
                    Directory.CreateDirectory(commentDirectory);
                }

                File.WriteAllText(commentOut, commentBody, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            }
        }

        var result = new CrossRuntimeReviewRecordResult
        {
            Command = $"{CommandName} record",
            Mode = write ? WorkerClaimCompleteConstants.Modes.Write : WorkerClaimCompleteConstants.Modes.DryRun,
            Outcome = write ? "recorded" : "would-record",
            Resolution = resolution,
            DeclarationSource = Models.CrossRuntimeReviewConfig.Source,
            Record = record,
            RecordFile = recordRelative,
            RawVerdictCopy = record.RawVerdictFile,
            CommentBody = commentBody,
            CommentOut = commentOut,
            PostCommand = $"gh pr review {pr.ToString(CultureInfo.InvariantCulture)} --repo {repo} --comment --body-file {(commentOut is null ? "<comment-body-file>" : CrossRuntimeReviewPaths.ShellQuote(commentOut))}",
            Durability = write
                ? $"The record exists only in this checkout until it is committed and pushed: commit `{recordRelative}` and `{record.RawVerdictFile}` in the host and push."
                : "Dry run: nothing was written. Re-run with --write to store the record.",
        };

        if (format == FormatJson)
        {
            writer.WriteLine(JsonSerializer.Serialize(result, JsonOptions));
        }
        else
        {
            writer.WriteLine("# Cross-runtime review record (G834)");
            writer.WriteLine();
            writer.WriteLine($"- mode: {result.Mode}");
            writer.WriteLine($"- outcome: {result.Outcome}");
            writer.WriteLine($"- record file: {result.RecordFile}");
            writer.WriteLine($"- raw verdict copy: {result.RawVerdictCopy}");
            writer.WriteLine($"- relation: {record.Relation} (conductor runtime {record.ConductorRuntime})");
            writer.WriteLine($"- verdict: {record.Verdict}");
            writer.WriteLine($"- post: `{result.PostCommand}`");
            writer.WriteLine($"- durability: {result.Durability}");
            writer.WriteLine();
            writer.WriteLine(commentBody);
        }

        return 0;
    }

    internal static string RenderCommentBody(CrossRuntimeReviewRecord record, string recordRelativePath)
    {
        var reviewer = record.Relation == CrossRuntimeReviewRecord.RelationCrossRuntime
            ? "cross-runtime review"
            : "independent same-runtime subagent review";
        var builder = new StringBuilder();
        builder.Append($"## {char.ToUpperInvariant(reviewer[0])}{reviewer[1..]}: {record.Verdict}\n\n");
        builder.Append($"- reviewer: {reviewer}\n");
        builder.Append($"- runtime: {record.Runtime}\n");
        builder.Append($"- runtime version: {record.RuntimeVersion}\n");
        builder.Append($"- conductor runtime: {record.ConductorRuntime}\n");
        builder.Append($"- head SHA: {record.HeadSha}\n");
        builder.Append($"- kind: {record.Kind}\n");
        builder.Append($"- execution unit: {record.ExecutionUnit}\n");
        builder.Append($"- verdict: {record.Verdict}\n\n");
        builder.Append("### Blocking findings\n\n");
        if (record.BlockingFindings.Count == 0)
        {
            builder.Append("- none\n");
        }
        else
        {
            foreach (var finding in record.BlockingFindings)
            {
                builder.Append($"- `{finding.File}:{finding.Line.ToString(CultureInfo.InvariantCulture)}` {finding.Scenario}\n");
            }
        }

        builder.Append("\n### Notes\n\n");
        if (record.Notes.Count == 0)
        {
            builder.Append("- none\n");
        }
        else
        {
            foreach (var note in record.Notes)
            {
                builder.Append($"- {note}\n");
            }
        }

        builder.Append($"\nRecorded as `{recordRelativePath}` by `intent-cli review cross-runtime record`.\n");
        return builder.ToString();
    }

    // ── status ─────────────────────────────────────────────────────────

    internal static int ExecuteStatus(CliContext context, string[] args, TextWriter writer)
    {
        if (IsHelp(args))
        {
            writer.WriteLine(StatusUsage);
            return 0;
        }

        if (!TryParse(args, StatusFlags, writer, StatusUsage, out var options, out var format))
        {
            return 1;
        }

        if (!RequireHostRoot(context, writer, format, "status"))
        {
            return 1;
        }

        if (!TryCommonArguments(options, writer, format, "status", out var repo, out var pr, out var head, out var unit))
        {
            return 1;
        }

        var resolution = CrossRuntimeReviewTeamResolver.Resolve(
            context.RepoRoot, repo, pr, unit, "pass the unit the host queue links to this PR with --execution-unit");
        if (!resolution.Resolved)
        {
            return RefuseResolution(writer, format, "status", resolution);
        }

        var declared = context.Config.CrossRuntimeReview.TryGetDeclared(resolution.Domain, resolution.Team, out var declaration);
        var read = CrossRuntimeReviewStore.Read(context.RepoRoot, repo, pr);
        var gate = CrossRuntimeReviewGate.Evaluate(declared ? declaration : null, resolution, head, read);
        var result = new CrossRuntimeReviewStatusResult
        {
            Command = $"{CommandName} status",
            Repo = repo,
            Pr = pr,
            HeadSha = head,
            Resolution = resolution,
            Declared = declared,
            DeclarationSource = declared ? Models.CrossRuntimeReviewConfig.Source : null,
            ConductorRuntime = declared ? declaration.ConductorRuntime : null,
            RecordFiles = read.Records.Select(stored => stored.RelativePath).ToArray(),
            Gate = gate,
        };

        if (format == FormatJson)
        {
            writer.WriteLine(JsonSerializer.Serialize(result, JsonOptions));
        }
        else
        {
            writer.WriteLine("# Cross-runtime review status (G834)");
            writer.WriteLine();
            writer.WriteLine($"- repo: {repo} pr: {pr.ToString(CultureInfo.InvariantCulture)} head: {head}");
            writer.WriteLine($"- execution unit: {resolution.ExecutionUnit} ({resolution.ExecutionUnitSource})");
            writer.WriteLine($"- domain: {resolution.Domain} ({resolution.DomainSource})");
            writer.WriteLine($"- team: {resolution.Team} ({resolution.TeamSource})");
            writer.WriteLine(declared
                ? $"- declared: yes ({result.DeclarationSource}), conductor runtime {result.ConductorRuntime}"
                : "- declared: no");
            writer.WriteLine($"- decision: {gate.Decision}");
            foreach (var reason in gate.Reasons)
            {
                writer.WriteLine($"  - {reason.Cause}: {reason.Detail}");
            }

            writer.WriteLine();
            writer.WriteLine("## Records");
            writer.WriteLine();
            if (gate.Records.Count == 0 && gate.Unreadable.Count == 0)
            {
                writer.WriteLine(read.Records.Count == 0 ? "- none" : $"- {read.Records.Count} record(s); not evaluated for an undeclared team");
            }

            foreach (var entry in gate.Records)
            {
                writer.WriteLine($"- [{entry.Status}] {entry.Runtime} ({entry.Relation}) {entry.Verdict} on {entry.HeadSha} at {entry.RecordedAt:O}: {entry.File}");
            }

            foreach (var unreadable in gate.Unreadable)
            {
                writer.WriteLine($"- [unreadable] {unreadable.RelativePath}: {unreadable.Error}");
            }
        }

        return 0;
    }

    // ── shared ─────────────────────────────────────────────────────────

    private static readonly IReadOnlyList<string> CommonValueFlags = ["--repo", "--pr", "--head-sha", "--execution-unit", "--format"];

    private static readonly FlagSet RequestFlags = new(
        [.. CommonValueFlags, "--runtime", "--clone", "--out-dir"], []);

    private static readonly FlagSet RecordFlags = new(
        [.. CommonValueFlags, "--kind", "--runtime", "--runtime-version", "--verdict-file", "--comment-out"], ["--write", "--dry-run"]);

    private static readonly FlagSet StatusFlags = new(CommonValueFlags, []);

    private sealed record FlagSet(IReadOnlyList<string> ValueFlags, IReadOnlyList<string> SwitchFlags);

    private static bool IsHelp(string[] args) =>
        args.Length == 1 && args[0] is "--help" or "help";

    private static bool TryParse(
        string[] args,
        FlagSet flags,
        TextWriter writer,
        string usage,
        out Dictionary<string, string> options,
        out string format)
    {
        options = new Dictionary<string, string>(StringComparer.Ordinal);
        format = FormatMarkdown;
        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];
            if (flags.SwitchFlags.Contains(argument, StringComparer.Ordinal))
            {
                if (argument == "--dry-run")
                {
                    options.Remove("--write");
                }
                else
                {
                    options[argument] = "true";
                }

                continue;
            }

            if (!flags.ValueFlags.Contains(argument, StringComparer.Ordinal))
            {
                writer.WriteLine($"Unknown argument '{argument}'.");
                writer.WriteLine(usage);
                return false;
            }

            if (index + 1 >= args.Length || string.IsNullOrEmpty(args[index + 1]))
            {
                writer.WriteLine($"{argument} requires a value.");
                writer.WriteLine(usage);
                return false;
            }

            options[argument] = args[++index];
        }

        if (options.TryGetValue("--format", out var requested))
        {
            if (requested is not FormatJson and not FormatMarkdown)
            {
                writer.WriteLine("--format must be json or markdown.");
                writer.WriteLine(usage);
                return false;
            }

            format = requested;
        }

        return true;
    }

    private static bool RequireHostRoot(CliContext context, TextWriter writer, string format, string subcommand)
    {
        if (File.Exists(context.GetConfigPath()))
        {
            return true;
        }

        Refuse(writer, format, subcommand, CrossRuntimeReviewCauses.HostRootRequired,
            $"no host config is loaded ('{context.GetConfigPath()}' does not exist); cross-runtime review reads the host config, queue-state, packets, and claims.",
            "run this command from the host root that owns `.intent-cli/config.toml`.");
        return false;
    }

    private static bool TryCommonArguments(
        Dictionary<string, string> options,
        TextWriter writer,
        string format,
        string subcommand,
        out string repo,
        out int pr,
        out string head,
        out string unit)
    {
        pr = 0;
        head = string.Empty;
        unit = string.Empty;
        if (!TryRequired(options, "--repo", writer, format, subcommand, out repo))
        {
            return false;
        }

        if (!CrossRuntimeReviewPaths.IsRepositoryName(repo))
        {
            Refuse(writer, format, subcommand, CrossRuntimeReviewCauses.ArgumentInvalid, $"--repo '{repo}' must be '<owner>/<repo>'.", "pass --repo <owner/repo>.");
            return false;
        }

        if (!TryRequired(options, "--pr", writer, format, subcommand, out var prText))
        {
            return false;
        }

        if (!int.TryParse(prText, NumberStyles.None, CultureInfo.InvariantCulture, out pr) || pr <= 0)
        {
            Refuse(writer, format, subcommand, CrossRuntimeReviewCauses.ArgumentInvalid, $"--pr '{prText}' must be a positive integer.", "pass --pr <n>.");
            return false;
        }

        if (!TryRequired(options, "--head-sha", writer, format, subcommand, out head))
        {
            return false;
        }

        if (!CrossRuntimeReviewPaths.IsFullHeadSha(head))
        {
            Refuse(writer, format, subcommand, CrossRuntimeReviewCauses.ArgumentInvalid, $"--head-sha '{head}' must be a 40-character hexadecimal SHA.", "pass the PR's full head SHA.");
            return false;
        }

        if (!TryRequired(options, "--execution-unit", writer, format, subcommand, out unit))
        {
            return false;
        }

        if (!KnowledgeWriteBackRecord.TryValidateExecutionUnit(unit, out var unitError))
        {
            Refuse(writer, format, subcommand, CrossRuntimeReviewCauses.ArgumentInvalid, $"--execution-unit is invalid: {unitError}", "pass the canonical execution unit id.");
            return false;
        }

        return true;
    }

    private static bool TryRuntime(Dictionary<string, string> options, TextWriter writer, string format, string subcommand, out string runtime)
    {
        if (!TryRequired(options, "--runtime", writer, format, subcommand, out runtime))
        {
            return false;
        }

        if (!CrossRuntimeReviewRuntimes.IsSupported(runtime))
        {
            Refuse(writer, format, subcommand, CrossRuntimeReviewCauses.RuntimeInvalid,
                $"--runtime '{runtime}' is not supported ({CrossRuntimeReviewRuntimes.Describe()}).",
                "pass --runtime codex, claude, or cursor.");
            return false;
        }

        return true;
    }

    private static bool TryRequired(Dictionary<string, string> options, string flag, TextWriter writer, string format, string subcommand, out string value)
    {
        if (options.TryGetValue(flag, out value!) && !string.IsNullOrWhiteSpace(value))
        {
            value = flag is "--clone" or "--out-dir" or "--verdict-file" or "--comment-out" ? value : value.Trim();
            return true;
        }

        Refuse(writer, format, subcommand, CrossRuntimeReviewCauses.ArgumentInvalid, $"{flag} is required.", $"pass {flag}.");
        value = string.Empty;
        return false;
    }

    private static string ResolvePath(CliContext context, string value) =>
        Path.GetFullPath(Path.IsPathRooted(value) ? value : Path.Combine(context.RepoRoot, value));

    private static int RefuseResolution(TextWriter writer, string format, string subcommand, CrossRuntimeReviewResolution resolution) =>
        Refuse(writer, format, subcommand, resolution.Cause!, resolution.Detail ?? string.Empty, resolution.Fix ?? string.Empty, resolution);

    private static int Refuse(
        TextWriter writer,
        string format,
        string subcommand,
        string cause,
        string detail,
        string fix,
        CrossRuntimeReviewResolution? resolution = null)
    {
        var refusal = new CrossRuntimeReviewRefusal
        {
            Command = $"{CommandName} {subcommand}",
            Outcome = "refused",
            Cause = cause,
            Detail = detail,
            Fix = fix,
            Resolution = resolution,
        };

        if (format == FormatJson)
        {
            writer.WriteLine(JsonSerializer.Serialize(refusal, new JsonSerializerOptions
            {
                WriteIndented = true,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            }));
        }
        else
        {
            writer.WriteLine($"{refusal.Command}: refused ({cause})");
            writer.WriteLine($"- detail: {detail}");
            writer.WriteLine($"- fix: {fix}");
            if (resolution?.Missing is not null)
            {
                writer.WriteLine($"- missing: {resolution.Missing}");
            }
        }

        return 1;
    }

    private static void WriteHelp(TextWriter writer)
    {
        writer.WriteLine("review cross-runtime (G834)");
        writer.WriteLine(RequestUsage);
        writer.WriteLine(RecordUsage);
        writer.WriteLine(StatusUsage);
        writer.WriteLine(NoExecutionBoundary);
        writer.WriteLine(TermsNotice);
    }
}

internal sealed record CrossRuntimeReviewRefusal
{
    [JsonPropertyName("command")] public required string Command { get; init; }
    [JsonPropertyName("outcome")] public required string Outcome { get; init; }
    [JsonPropertyName("cause")] public required string Cause { get; init; }
    [JsonPropertyName("detail")] public required string Detail { get; init; }
    [JsonPropertyName("fix")] public required string Fix { get; init; }
    [JsonPropertyName("resolution")] public CrossRuntimeReviewResolution? Resolution { get; init; }
}

internal sealed record CrossRuntimeReviewRequestResult
{
    [JsonPropertyName("command")] public required string Command { get; init; }
    [JsonPropertyName("outcome")] public required string Outcome { get; init; }
    [JsonPropertyName("repo")] public required string Repo { get; init; }
    [JsonPropertyName("pr")] public required int Pr { get; init; }
    [JsonPropertyName("head_sha")] public required string HeadSha { get; init; }
    [JsonPropertyName("execution_unit")] public required string ExecutionUnit { get; init; }
    [JsonPropertyName("runtime")] public required string Runtime { get; init; }
    [JsonPropertyName("out_dir")] public required string OutDir { get; init; }
    [JsonPropertyName("files")] public required IReadOnlyList<string> Files { get; init; }
    [JsonPropertyName("invocation")] public required string Invocation { get; init; }
    [JsonPropertyName("run_by")] public required string RunBy { get; init; }
    [JsonPropertyName("raw_verdict_file")] public required string RawVerdictFile { get; init; }
    [JsonPropertyName("read_only_enforcement")] public required string ReadOnlyEnforcement { get; init; }
    [JsonPropertyName("no_execution_boundary")] public required string NoExecutionBoundary { get; init; }
    [JsonPropertyName("terms")] public required string Terms { get; init; }
}

internal sealed record CrossRuntimeReviewRecordResult
{
    [JsonPropertyName("command")] public required string Command { get; init; }
    [JsonPropertyName("mode")] public required string Mode { get; init; }
    [JsonPropertyName("outcome")] public required string Outcome { get; init; }
    [JsonPropertyName("resolution")] public required CrossRuntimeReviewResolution Resolution { get; init; }
    [JsonPropertyName("declaration_source")] public required string DeclarationSource { get; init; }
    [JsonPropertyName("record")] public required CrossRuntimeReviewRecord Record { get; init; }
    [JsonPropertyName("record_file")] public required string RecordFile { get; init; }
    [JsonPropertyName("raw_verdict_copy")] public required string RawVerdictCopy { get; init; }
    [JsonPropertyName("comment_body")] public required string CommentBody { get; init; }
    [JsonPropertyName("comment_out")] public string? CommentOut { get; init; }
    [JsonPropertyName("post_command")] public required string PostCommand { get; init; }
    [JsonPropertyName("durability")] public required string Durability { get; init; }
}

internal sealed record CrossRuntimeReviewStatusResult
{
    [JsonPropertyName("command")] public required string Command { get; init; }
    [JsonPropertyName("repo")] public required string Repo { get; init; }
    [JsonPropertyName("pr")] public required int Pr { get; init; }
    [JsonPropertyName("head_sha")] public required string HeadSha { get; init; }
    [JsonPropertyName("resolution")] public required CrossRuntimeReviewResolution Resolution { get; init; }
    [JsonPropertyName("declared")] public required bool Declared { get; init; }
    [JsonPropertyName("declaration_source")] public string? DeclarationSource { get; init; }
    [JsonPropertyName("conductor_runtime")] public string? ConductorRuntime { get; init; }
    [JsonPropertyName("record_files")] public required IReadOnlyList<string> RecordFiles { get; init; }
    [JsonPropertyName("gate")] public required CrossRuntimeReviewGateResult Gate { get; init; }
}
