using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G834/G835: <c>intent-cli review cross-runtime request|record|status</c> — one
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
        "Usage: intent-cli review cross-runtime request (--repo <owner/repo> --pr <n> --head-sha <40-hex> --execution-unit <unit> --runtime codex|claude|cursor|copilot|opencode --clone <read-only-clone-path> --out-dir <dir> | --kind design --execution-unit <unit> --runtime codex|claude|cursor|copilot|opencode --out-dir <dir> [--clone <read-only-clone>]) [--model <name>] [--effort <level>] [--opencode-provider-config <file>] [--format json|markdown]";

    internal const string RecordUsage =
        "Usage: intent-cli review cross-runtime record (--repo <owner/repo> --pr <n> --head-sha <40-hex> --execution-unit <unit> --kind implementation | --kind design --execution-unit <unit> --packet-digest <sha256>) --runtime codex|claude|cursor|copilot|opencode --runtime-version <text> --verdict-file <path> [--model <name>] [--effort <level>] [--comment-out <path>] [--write] [--format json|markdown]";

    internal const string StatusUsage =
        "Usage: intent-cli review cross-runtime status (--repo <owner/repo> --pr <n> --head-sha <40-hex> --execution-unit <unit> | --kind design --execution-unit <unit>) [--format json|markdown]";

    internal const string NoExecutionBoundary =
        "intent-cli renders text and records evidence only; the seat runs the reviewer and posts with gh. intent-cli does not start, launch, or manage any agent or provider process.";

    internal const string TermsNotice =
        "Confirming each vendor's automation terms for headless reviewer runs is the operator's responsibility; intent-cli does not assert that any use is permitted.";

    /// <summary>
    /// Test-only seam. None of the three subcommands invokes this delegate.
    /// </summary>
    public static Func<bool>? NestedProviderLauncher { get; set; }

    /// <summary>
    /// Test-only runner seam. The request, record, and status paths do not use it.
    /// </summary>
    internal static Func<INotifyProcessRunner>? ProcessRunnerFactory { get; set; }

    /// <summary>Test seam for <c>recorded_at</c>.</summary>
    internal static Func<DateTimeOffset>? Clock { get; set; }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    private static readonly JsonSerializerOptions RequestJsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
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

        var kind = ResolveKind(options);
        if (string.Equals(kind, CrossRuntimeReviewRecord.KindDesign, StringComparison.Ordinal))
        {
            return ExecuteDesignRequest(context, options, writer, format);
        }

        return ExecuteImplementationRequest(context, options, writer, format);
    }

    private static int ExecuteImplementationRequest(
        CliContext context,
        Dictionary<string, string> options,
        TextWriter writer,
        string format)
    {
        if (options.TryGetValue("--kind", out var explicitKind)
            && !string.Equals(explicitKind, CrossRuntimeReviewRecord.KindImplementation, StringComparison.Ordinal))
        {
            return Refuse(writer, format, "request", CrossRuntimeReviewCauses.ArgumentInvalid,
                $"--kind must be '{CrossRuntimeReviewRecord.KindImplementation}' when PR arguments are given (got '{explicitKind}').",
                $"pass --kind {CrossRuntimeReviewRecord.KindImplementation} or omit --kind.");
        }

        if (!TryCommonArguments(options, writer, format, "request", out var repo, out var pr, out var head, out var unit)
            || !TryRuntime(options, writer, format, "request", out var runtime)
            || !TryModelAndEffort(options, writer, format, "request", runtime, out var model, out var effort))
        {
            return 1;
        }

        string? opencodeProviderConfig = null;
        if (options.ContainsKey("--opencode-provider-config"))
        {
            if (runtime != CrossRuntimeReviewRuntimes.Opencode)
            {
                return Refuse(writer, format, "request", CrossRuntimeReviewCauses.ArgumentInvalid,
                    "--opencode-provider-config is accepted only for runtime opencode.",
                    "omit --opencode-provider-config for other runtimes.");
            }

            opencodeProviderConfig = ResolvePath(context, options["--opencode-provider-config"]);
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

        if (!CrossRuntimeReviewRequestSupport.TryValidateOutDir(
                outDir,
                runtime,
                CrossRuntimeReviewRecord.KindImplementation,
                true,
                clone,
                opencodeProviderConfig,
                writer,
                format,
                out _))
        {
            return 1;
        }

        if (opencodeProviderConfig is not null
            && !CrossRuntimeReviewOpencodeConfig.TryValidateProviderConfigFile(opencodeProviderConfig, out _, out var providerError))
        {
            return Refuse(writer, format, "request", CrossRuntimeReviewCauses.OpencodeProviderConfigInvalid,
                providerError,
                "pass a UTF-8 JSON file whose root object has exactly one key 'provider' with an object value.");
        }

        byte[] bodyBytes = [];
        byte[] reviewContextBytes = [];
        byte[] implementationBytes = [];
        if (runtime is CrossRuntimeReviewRuntimes.Copilot or CrossRuntimeReviewRuntimes.Opencode)
        {
            try
            {
                CrossRuntimeReviewHomeAccessGuard.GuardPath(body);
                CrossRuntimeReviewHomeAccessGuard.GuardPath(reviewContext);
                CrossRuntimeReviewHomeAccessGuard.GuardPath(implementation);

                bodyBytes = File.ReadAllBytes(body);
                reviewContextBytes = File.ReadAllBytes(reviewContext);
                implementationBytes = File.ReadAllBytes(implementation);
            }
            catch (InvalidOperationException exception)
            {
                return Refuse(writer, format, "request", CrossRuntimeReviewCauses.ArgumentInvalid, exception.Message, "do not read operator Copilot or OpenCode config directories.");
            }
        }

        string prompt;
        try
        {
            prompt = RenderPrompt(
                runtime,
                context.RepoRoot,
                repo,
                pr,
                head,
                unit,
                clone,
                bodyBytes,
                reviewContextBytes,
                implementationBytes);
        }
        catch (DecoderFallbackException exception)
        {
            return Refuse(writer, format, "request", CrossRuntimeReviewCauses.PacketInvalid,
                $"packet file bytes are not valid UTF-8: {exception.Message}",
                "repair the packet files under `.intent-cli/issues/` so every file is UTF-8 text.");
        }

        var invocation = CrossRuntimeReviewRuntimes.InvocationLabel(runtime) + "\n"
            + CrossRuntimeReviewRuntimes.RenderInvocation(runtime, clone, outDir, model, effort) + "\n";
        IReadOnlyList<string> files;
        try
        {
            files = CrossRuntimeReviewRequestSupport.WriteRequestFiles(
                runtime,
                CrossRuntimeReviewRecord.KindImplementation,
                true,
                clone,
                outDir,
                prompt,
                CrossRuntimeReviewVerdict.SchemaJson,
                invocation,
                opencodeProviderConfig);
        }
        catch (InvalidOperationException exception)
        {
            return Refuse(writer, format, "request", CrossRuntimeReviewCauses.PathInvalid,
                exception.Message,
                "do not read operator Copilot or OpenCode config directories.");
        }

        var result = new CrossRuntimeReviewImplementationRequestResult
        {
            Command = $"{CommandName} request",
            Outcome = "rendered",
            Repo = repo,
            Pr = pr,
            HeadSha = head,
            ExecutionUnit = unit,
            Runtime = runtime,
            Model = model,
            Effort = effort,
            OutDir = outDir,
            OpencodeProviderConfig = opencodeProviderConfig,
            Files = files,
            Invocation = invocation.TrimEnd('\n'),
            RunBy = "seat",
            RawVerdictFile = Path.Combine(outDir, CrossRuntimeReviewFiles.RawVerdict),
            ReadOnlyEnforcement = CrossRuntimeReviewRuntimes.ReadOnlyEnforcement[runtime],
            NoExecutionBoundary = NoExecutionBoundary,
            Terms = TermsNotice,
        };

        WriteImplementationRequestResult(writer, format, result);
        return 0;
    }

    private static int ExecuteDesignRequest(
        CliContext context,
        Dictionary<string, string> options,
        TextWriter writer,
        string format)
    {
        foreach (var forbidden in new[] { "--repo", "--pr", "--head-sha" })
        {
            if (options.ContainsKey(forbidden))
            {
                return Refuse(writer, format, "request", CrossRuntimeReviewCauses.ArgumentInvalid,
                    $"design review refuses {forbidden}; pass --kind design with --execution-unit, --runtime, and --out-dir only.",
                    "omit PR arguments for design review request.");
            }
        }

        if (!TryRequired(options, "--execution-unit", writer, format, "request", out var unit)
            || !TryRuntime(options, writer, format, "request", out var runtime)
            || !TryRequired(options, "--out-dir", writer, format, "request", out var outDirArgument)
            || !TryModelAndEffort(options, writer, format, "request", runtime, out var model, out var effort))
        {
            return 1;
        }

        string? opencodeProviderConfig = null;
        if (options.ContainsKey("--opencode-provider-config"))
        {
            if (runtime != CrossRuntimeReviewRuntimes.Opencode)
            {
                return Refuse(writer, format, "request", CrossRuntimeReviewCauses.ArgumentInvalid,
                    "--opencode-provider-config is accepted only for runtime opencode.",
                    "omit --opencode-provider-config for other runtimes.");
            }

            opencodeProviderConfig = ResolvePath(context, options["--opencode-provider-config"]);
        }

        if (!KnowledgeWriteBackRecord.TryValidateExecutionUnit(unit, out var unitError))
        {
            return Refuse(writer, format, "request", CrossRuntimeReviewCauses.ArgumentInvalid, $"--execution-unit is invalid: {unitError}", "pass the canonical execution unit id.");
        }

        options.TryGetValue("--clone", out var cloneArgument);
        foreach (var (flag, value) in new[] { ("--clone", cloneArgument), ("--out-dir", outDirArgument) }
                     .Where(pair => !string.IsNullOrWhiteSpace(pair.Item2)))
        {
            if (!CrossRuntimeReviewPaths.IsRenderablePath(value!))
            {
                return Refuse(writer, format, "request", CrossRuntimeReviewCauses.PathInvalid,
                    $"{flag} contains a newline or NUL and cannot be rendered as one shell argument.",
                    $"pass a {flag} path without newline or NUL characters.");
            }
        }

        var outDir = ResolvePath(context, outDirArgument);
        var hasClone = !string.IsNullOrWhiteSpace(cloneArgument);
        var workspace = hasClone
            ? ResolvePath(context, cloneArgument!)
            : runtime is CrossRuntimeReviewRuntimes.Copilot or CrossRuntimeReviewRuntimes.Opencode
                ? Path.Combine(outDir, CrossRuntimeReviewFiles.Workspace)
                : outDir;
        var packetDirectory = CrossRuntimeReviewPaths.PacketDirectory(context.RepoRoot, unit);
        if (runtime is CrossRuntimeReviewRuntimes.Copilot or CrossRuntimeReviewRuntimes.Opencode
            && TryRefuseProtectedPacketDirectory(writer, format, "request", packetDirectory, out var packetRefusal))
        {
            return packetRefusal;
        }

        if (!CrossRuntimeDesignReviewDigest.TryReadFromDirectory(packetDirectory, out var packet, out var missingPath))
        {
            return Refuse(writer, format, "request", CrossRuntimeReviewCauses.PacketMissing,
                $"packet file is missing: {missingPath}; a design reviewer cannot review against an incomplete packet.",
                $"run from the host root that holds `.intent-cli/issues/{unit}/` with packet.yaml, github-body.md, review-context.md, and implementation.md.");
        }

        var digest = CrossRuntimeDesignReviewDigest.Compute(packet);
        if (File.Exists(outDir))
        {
            return Refuse(writer, format, "request", CrossRuntimeReviewCauses.PathInvalid,
                $"--out-dir '{outDir}' is a file.", "pass a new or empty directory.");
        }

        if (!CrossRuntimeReviewRequestSupport.TryValidateOutDir(
                outDir,
                runtime,
                CrossRuntimeReviewRecord.KindDesign,
                hasClone,
                workspace,
                opencodeProviderConfig,
                writer,
                format,
                out _))
        {
            return 1;
        }

        if (opencodeProviderConfig is not null
            && !CrossRuntimeReviewOpencodeConfig.TryValidateProviderConfigFile(opencodeProviderConfig, out _, out var providerError))
        {
            return Refuse(writer, format, "request", CrossRuntimeReviewCauses.OpencodeProviderConfigInvalid,
                providerError,
                "pass a UTF-8 JSON file whose root object has exactly one key 'provider' with an object value.");
        }

        string prompt;
        try
        {
            prompt = RenderDesignPrompt(unit, digest, packet, !string.IsNullOrWhiteSpace(cloneArgument));
        }
        catch (DecoderFallbackException exception)
        {
            return Refuse(writer, format, "request", CrossRuntimeReviewCauses.PacketInvalid,
                $"packet file bytes are not valid UTF-8: {exception.Message}",
                "repair the packet files under `.intent-cli/issues/` so every file is UTF-8 text.");
        }

        var invocation = CrossRuntimeReviewRuntimes.InvocationLabel(runtime) + "\n"
            + CrossRuntimeReviewRuntimes.RenderInvocation(runtime, workspace, outDir, model, effort) + "\n";
        IReadOnlyList<string> files;
        try
        {
            files = CrossRuntimeReviewRequestSupport.WriteRequestFiles(
                runtime,
                CrossRuntimeReviewRecord.KindDesign,
                hasClone,
                workspace,
                outDir,
                prompt,
                CrossRuntimeReviewVerdict.DesignSchemaJson,
                invocation,
                opencodeProviderConfig);
        }
        catch (InvalidOperationException exception)
        {
            return Refuse(writer, format, "request", CrossRuntimeReviewCauses.PathInvalid,
                exception.Message,
                "do not read operator Copilot or OpenCode config directories.");
        }

        var result = new CrossRuntimeReviewRequestResult
        {
            Command = $"{CommandName} request",
            Outcome = "rendered",
            Kind = CrossRuntimeReviewRecord.KindDesign,
            ExecutionUnit = unit,
            PacketDigest = digest,
            Runtime = runtime,
            Model = model,
            Effort = effort,
            OutDir = outDir,
            Workspace = workspace,
            OpencodeProviderConfig = opencodeProviderConfig,
            Files = files,
            Invocation = invocation.TrimEnd('\n'),
            RunBy = "seat",
            RawVerdictFile = Path.Combine(outDir, CrossRuntimeReviewFiles.RawVerdict),
            ReadOnlyEnforcement = CrossRuntimeReviewRuntimes.ReadOnlyEnforcement[runtime],
            NoExecutionBoundary = NoExecutionBoundary,
            Terms = TermsNotice,
        };

        WriteDesignRequestResult(writer, format, result);
        return 0;
    }

    private static void WriteImplementationRequestResult(TextWriter writer, string format, CrossRuntimeReviewImplementationRequestResult result)
    {
        if (format == FormatJson)
        {
            writer.WriteLine(JsonSerializer.Serialize(result, JsonOptions));
            return;
        }

        WriteImplementationRequestMarkdown(writer, result);
    }

    private static void WriteDesignRequestResult(TextWriter writer, string format, CrossRuntimeReviewRequestResult result)
    {
        if (format == FormatJson)
        {
            writer.WriteLine(JsonSerializer.Serialize(result, RequestJsonOptions));
        }
        else
        {
            writer.WriteLine("# Cross-runtime review request (G835 design)");
            writer.WriteLine();
            if (result.Repo is not null)
            {
                writer.WriteLine($"- repo: {result.Repo}");
                writer.WriteLine($"- pr: {result.Pr!.Value.ToString(CultureInfo.InvariantCulture)}");
                writer.WriteLine($"- head sha: {result.HeadSha}");
            }

            if (result.PacketDigest is not null)
            {
                writer.WriteLine($"- packet digest: {result.PacketDigest}");
            }

            writer.WriteLine($"- execution unit: {result.ExecutionUnit}");
            writer.WriteLine($"- runtime: {result.Runtime}");
            if (result.Model is not null)
            {
                writer.WriteLine($"- model: {result.Model}");
            }

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
    }

    private static void WriteImplementationRequestMarkdown(TextWriter writer, CrossRuntimeReviewImplementationRequestResult result)
    {
        writer.WriteLine("# Cross-runtime review request (G834)");
        writer.WriteLine();
        writer.WriteLine($"- repo: {result.Repo}");
        writer.WriteLine($"- pr: {result.Pr.ToString(CultureInfo.InvariantCulture)}");
        writer.WriteLine($"- head sha: {result.HeadSha}");
        writer.WriteLine($"- execution unit: {result.ExecutionUnit}");
        writer.WriteLine($"- runtime: {result.Runtime}");
        if (result.Model is not null)
        {
            writer.WriteLine($"- model: {result.Model}");
        }

        if (result.Effort is not null)
        {
            writer.WriteLine($"- effort: {result.Effort}");
        }

        if (result.OpencodeProviderConfig is not null)
        {
            writer.WriteLine($"- opencode provider config: {result.OpencodeProviderConfig}");
        }

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

    internal static string RenderPrompt(
        string runtime,
        string repoRoot,
        string repo,
        int pr,
        string head,
        string unit,
        string clone,
        byte[] bodyBytes,
        byte[] reviewContextBytes,
        byte[] implementationBytes)
    {
        var q = CrossRuntimeReviewPaths.ShellQuote;
        var prText = pr.ToString(CultureInfo.InvariantCulture);
        var builder = new StringBuilder();
        builder.Append($"# Implementation review: {repo} PR #{prText} ({unit})\n\n");
        builder.Append("You are an independent reviewer on a different runtime from the author. ");
        builder.Append($"Review pull request #{prText} in {repo} at head {head}.\n\n");
        builder.Append("## Inputs\n\n");
        if (runtime is CrossRuntimeReviewRuntimes.Copilot or CrossRuntimeReviewRuntimes.Opencode)
        {
            AppendEmbeddedFile(builder, "github-body.md", bodyBytes);
            AppendEmbeddedFile(builder, "review-context.md", reviewContextBytes);
            AppendEmbeddedFile(builder, "implementation.md", implementationBytes);
        }
        else
        {
            var packetDirectory = CrossRuntimeReviewPaths.PacketDirectory(repoRoot, unit);
            builder.Append($"- Issue contract (packet github-body.md): {q(Path.Combine(packetDirectory, "github-body.md"))}\n");
            builder.Append($"- Review context (packet review-context.md): {q(Path.Combine(packetDirectory, "review-context.md"))}\n");
            builder.Append($"- Implementation notes (packet implementation.md): {q(Path.Combine(packetDirectory, "implementation.md"))}\n");
        }

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

    internal static string RenderDesignPrompt(
        string unit,
        string packetDigest,
        CrossRuntimeDesignReviewDigest.PacketBytes packet,
        bool cloneGiven)
    {
        var builder = new StringBuilder();
        builder.Append($"# Design review: {unit}\n\n");
        builder.Append("You are an independent design reviewer. Judge the packet against the operator decisions it names, ");
        builder.Append("the truth of the \"Current Observed State\" claims");
        if (cloneGiven)
        {
            builder.Append(" (check these in the read-only clone when you can run commands)");
        }

        builder.Append(", gate soundness, testability, and the no-launch rule.\n\n");
        builder.Append($"## Packet digest\n\n`{packetDigest}`\n\n");
        builder.Append("Echo this exact digest in your verdict as `packet_digest`.\n\n");
        builder.Append("## Packet files\n\n");
        AppendEmbeddedFile(builder, CrossRuntimeDesignReviewDigest.PacketFileNames[0], packet.PacketYaml);
        AppendEmbeddedFile(builder, CrossRuntimeDesignReviewDigest.PacketFileNames[1], packet.GithubBody);
        AppendEmbeddedFile(builder, CrossRuntimeDesignReviewDigest.PacketFileNames[2], packet.ReviewContext);
        AppendEmbeddedFile(builder, CrossRuntimeDesignReviewDigest.PacketFileNames[3], packet.Implementation);
        builder.Append("## Output\n\n");
        builder.Append("Return only one JSON object that matches the schema below, with no text before or after it.\n\n");
        builder.Append($"- \"packet_digest\": echo the packet digest you reviewed ({packetDigest}).\n");
        builder.Append("- \"verdict\": \"approve\" only when there is no blocking finding; \"blocking_findings\" must then be [].\n");
        builder.Append("- \"verdict\": \"request-changes\" when there is at least one blocking finding.\n");
        builder.Append("- \"notes\": non-blocking observations; may be [].\n\n");
        builder.Append("```json\n");
        builder.Append(CrossRuntimeReviewVerdict.DesignSchemaJson);
        builder.Append("```\n");
        return builder.ToString();
    }

    private static void AppendEmbeddedFile(StringBuilder builder, string fileName, byte[] bytes)
    {
        var content = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(bytes);
        // A fence longer than any backtick run in the file keeps the embedded
        // bytes verbatim and the boundary unambiguous (CommonMark).
        var fence = new string('`', Math.Max(3, LongestBacktickRun(content) + 1));
        builder.Append($"### {fileName}\n\n");
        builder.Append(fence);
        builder.Append(fileName);
        builder.Append('\n');
        builder.Append(content);
        if (bytes.Length == 0 || bytes[^1] != (byte)'\n')
        {
            builder.Append('\n');
        }

        builder.Append(fence);
        builder.Append("\n\n");
    }

    internal static int LongestBacktickRun(string content)
    {
        var longest = 0;
        var current = 0;
        foreach (var character in content)
        {
            current = character == '`' ? current + 1 : 0;
            longest = Math.Max(longest, current);
        }

        return longest;
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

        if (!TryRequired(options, "--kind", writer, format, "record", out var kind))
        {
            return 1;
        }

        if (string.Equals(kind, CrossRuntimeReviewRecord.KindDesign, StringComparison.Ordinal))
        {
            return ExecuteDesignRecord(context, options, writer, format);
        }

        return ExecuteImplementationRecord(context, options, writer, format);
    }

    private static int ExecuteImplementationRecord(
        CliContext context,
        Dictionary<string, string> options,
        TextWriter writer,
        string format)
    {
        if (options.TryGetValue("--kind", out var explicitKind)
            && !string.Equals(explicitKind, CrossRuntimeReviewRecord.KindImplementation, StringComparison.Ordinal))
        {
            return Refuse(writer, format, "record", CrossRuntimeReviewCauses.ArgumentInvalid,
                $"--kind must be '{CrossRuntimeReviewRecord.KindImplementation}' when PR arguments are given (got '{explicitKind}').",
                $"pass --kind {CrossRuntimeReviewRecord.KindImplementation} or omit --kind.");
        }

        if (!TryCommonArguments(options, writer, format, "record", out var repo, out var pr, out var head, out var unit)
            || !TryRuntime(options, writer, format, "record", out var runtime)
            || !TryRequired(options, "--runtime-version", writer, format, "record", out var runtimeVersion)
            || !TryRequired(options, "--verdict-file", writer, format, "record", out var verdictFileArgument)
            || !TryModelAndEffort(options, writer, format, "record", runtime, out var model, out var effort))
        {
            return 1;
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
        if (runtime == CrossRuntimeReviewRuntimes.Opencode
            && !CrossRuntimeReviewJsonlVerdict.TryValidateOpencodeExitStatus(verdictFile, out var exitCause, out var exitDetail))
        {
            return Refuse(writer, format, "record", exitCause, exitDetail,
                "re-run the reviewer with the pinned invocation so opencode-exit.txt contains exactly 0\\n.");
        }

        byte[] raw;
        if (runtime is CrossRuntimeReviewRuntimes.Copilot or CrossRuntimeReviewRuntimes.Opencode)
        {
            if (!CrossRuntimeReviewFileMode.TryReadRegularFileBytes(
                    verdictFile,
                    out raw,
                    out var verdictReadFailure,
                    out var verdictReadError))
            {
                var verdictDetail = verdictReadFailure == CrossRuntimeReviewFileReadFailure.Empty
                    ? $"verdict file '{verdictFile}' is invalid for runtime '{runtime}': verdict-invalid: file is empty."
                    : $"verdict file '{verdictFile}' could not be read: {verdictReadError}";
                return Refuse(writer, format, "record", CrossRuntimeReviewCauses.VerdictInvalid,
                    verdictDetail,
                    "pass the file the rendered invocation wrote (verdict.raw.json).");
            }
        }
        else
        {
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

        if (!CrossRuntimeReviewVerdict.TryParse(runtime, content, out var verdict, out var observedModel, out var verdictError))
        {
            return Refuse(writer, format, "record", CrossRuntimeReviewCauses.VerdictInvalid,
                $"verdict file '{verdictFile}' is invalid for runtime '{runtime}': {verdictError}",
                "re-run the reviewer with the pinned invocation; never hand-write a verdict.");
        }

        if (runtime == CrossRuntimeReviewRuntimes.Copilot
            && (observedModel is null || !string.Equals(observedModel, model, StringComparison.Ordinal)))
        {
            return Refuse(writer, format, "record", CrossRuntimeReviewCauses.ModelMismatch,
                observedModel is null
                    ? $"copilot envelope data.model is missing but --model is '{model}'."
                    : $"copilot envelope data.model is '{observedModel}' but --model is '{model}'.",
                "re-run the reviewer with the same --model value.");
        }

        if (runtime == CrossRuntimeReviewRuntimes.Copilot
            && effort is not null
            && !CrossRuntimeReviewJsonlVerdict.TryParseCopilotEffort(content, observedModel!, effort, out var effortError))
        {
            return Refuse(writer, format, "record", CrossRuntimeReviewCauses.EffortMismatch,
                effortError,
                "re-run the reviewer with the same --effort value.");
        }

        if (!string.Equals(verdict.HeadSha, head, StringComparison.OrdinalIgnoreCase))
        {
            return Refuse(writer, format, "record", CrossRuntimeReviewCauses.HeadMismatch,
                $"the verdict echoes head '{verdict.HeadSha}' but --head-sha is '{head}'.",
                "re-run the review against a clone checked out at the gated head.");
        }

        // G834 review: a verdict for a head the PR has already moved past is
        // refused, so a slow run on an older head can never be recorded after a
        // newer head's block and clear that block's re-review requirement.
        var existing = CrossRuntimeReviewStore.Read(context.RepoRoot, repo, pr).Records;
        if (existing.Count > 0)
        {
            var newestHead = existing[^1].Record.HeadSha;
            var headSeenBefore = existing.Any(item => string.Equals(item.Record.HeadSha, head, StringComparison.OrdinalIgnoreCase));
            if (headSeenBefore && !string.Equals(newestHead, head, StringComparison.OrdinalIgnoreCase))
            {
                return Refuse(writer, format, "record", CrossRuntimeReviewCauses.HeadSuperseded,
                    $"head '{head}' already has records on this PR, and a newer head '{newestHead}' was recorded after them; a verdict for a superseded head is not recorded.",
                    "review the current PR head and record that verdict instead.");
            }
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
            Kind = CrossRuntimeReviewRecord.KindImplementation,
            Runtime = runtime,
            RuntimeVersion = runtimeVersion,
            ConductorRuntime = declaration.ConductorRuntime,
            Relation = CrossRuntimeReviewRecord.RelationFor(runtime, declaration.ConductorRuntime),
            Model = model,
            Effort = effort,
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

    private static int ExecuteDesignRecord(
        CliContext context,
        Dictionary<string, string> options,
        TextWriter writer,
        string format)
    {
        foreach (var forbidden in new[] { "--repo", "--pr", "--head-sha" })
        {
            if (options.ContainsKey(forbidden))
            {
                return Refuse(writer, format, "record", CrossRuntimeReviewCauses.ArgumentInvalid,
                    $"design review refuses {forbidden}; pass --kind design with --execution-unit and --packet-digest.",
                    "omit PR arguments for design review record.");
            }
        }

        if (!TryRequired(options, "--execution-unit", writer, format, "record", out var unit)
            || !TryRequired(options, "--packet-digest", writer, format, "record", out var packetDigestArgument)
            || !TryRuntime(options, writer, format, "record", out var runtime)
            || !TryRequired(options, "--runtime-version", writer, format, "record", out var runtimeVersion)
            || !TryRequired(options, "--verdict-file", writer, format, "record", out var verdictFileArgument)
            || !TryModelAndEffort(options, writer, format, "record", runtime, out var model, out var effort))
        {
            return 1;
        }

        if (!KnowledgeWriteBackRecord.TryValidateExecutionUnit(unit, out var unitError))
        {
            return Refuse(writer, format, "record", CrossRuntimeReviewCauses.ArgumentInvalid, $"--execution-unit is invalid: {unitError}", "pass the canonical execution unit id.");
        }

        var designResolution = CrossRuntimeReviewDesignTeamResolver.Resolve(context.RepoRoot, unit);
        if (!designResolution.Resolved)
        {
            return RefuseDesignResolution(writer, format, "record", designResolution);
        }

        if (!context.Config.CrossRuntimeReview.TryGetDeclared(designResolution.Domain!, designResolution.Team!, out var declaration))
        {
            return Refuse(writer, format, "record", CrossRuntimeReviewCauses.NotDeclared,
                $"team '{designResolution.Domain}/{designResolution.Team}' is not declared in [[cross_runtime_review.teams]]; design review is not required.",
                "declare the team in `.intent-cli/config.toml` under [[cross_runtime_review.teams]] if cross-runtime design review is required.");
        }

        var packetDirectory = CrossRuntimeReviewPaths.PacketDirectory(context.RepoRoot, unit);
        if (runtime is CrossRuntimeReviewRuntimes.Copilot or CrossRuntimeReviewRuntimes.Opencode
            && TryRefuseProtectedPacketDirectory(writer, format, "record", packetDirectory, out var packetRefusal))
        {
            return packetRefusal;
        }

        if (!CrossRuntimeDesignReviewDigest.TryReadFromDirectory(packetDirectory, out var packet, out var missingPath))
        {
            return Refuse(writer, format, "record", CrossRuntimeReviewCauses.PacketMissing,
                $"packet file is missing: {missingPath}.",
                $"complete `.intent-cli/issues/{unit}/` with all four packet files.");
        }

        var currentDigest = CrossRuntimeDesignReviewDigest.Compute(packet);
        if (!string.Equals(packetDigestArgument, currentDigest, StringComparison.OrdinalIgnoreCase))
        {
            return Refuse(writer, format, "record", CrossRuntimeReviewCauses.DigestStale,
                $"--packet-digest '{packetDigestArgument}' does not match the current packet digest '{currentDigest}'.",
                "re-run design review against the current packet bytes and pass the current digest.");
        }

        var verdictFile = ResolvePath(context, verdictFileArgument);
        if (runtime == CrossRuntimeReviewRuntimes.Opencode
            && !CrossRuntimeReviewJsonlVerdict.TryValidateOpencodeExitStatus(verdictFile, out var exitCause, out var exitDetail))
        {
            return Refuse(writer, format, "record", exitCause, exitDetail,
                "re-run the reviewer with the pinned invocation so opencode-exit.txt contains exactly 0\\n.");
        }

        byte[] raw;
        if (runtime is CrossRuntimeReviewRuntimes.Copilot or CrossRuntimeReviewRuntimes.Opencode)
        {
            if (!CrossRuntimeReviewFileMode.TryReadRegularFileBytes(
                    verdictFile,
                    out raw,
                    out var verdictReadFailure,
                    out var verdictReadError))
            {
                var verdictDetail = verdictReadFailure == CrossRuntimeReviewFileReadFailure.Empty
                    ? $"verdict file '{verdictFile}' is invalid for runtime '{runtime}': verdict-invalid: file is empty."
                    : $"verdict file '{verdictFile}' could not be read: {verdictReadError}";
                return Refuse(writer, format, "record", CrossRuntimeReviewCauses.VerdictInvalid,
                    verdictDetail,
                    "pass the file the rendered invocation wrote (verdict.raw.json).");
            }
        }
        else
        {
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

        if (!CrossRuntimeReviewVerdict.TryParseDesign(runtime, content, out var verdict, out var verdictError))
        {
            return Refuse(writer, format, "record", CrossRuntimeReviewCauses.VerdictInvalid,
                $"verdict file '{verdictFile}' is invalid for runtime '{runtime}': {verdictError}",
                "re-run the reviewer with the pinned invocation; never hand-write a verdict.");
        }

        CrossRuntimeReviewJsonlVerdict.TryReadCopilotObservedModel(content, out var observedModel, out _);
        if (runtime == CrossRuntimeReviewRuntimes.Copilot
            && (observedModel is null || !string.Equals(observedModel, model, StringComparison.Ordinal)))
        {
            return Refuse(writer, format, "record", CrossRuntimeReviewCauses.ModelMismatch,
                observedModel is null
                    ? $"copilot envelope data.model is missing but --model is '{model}'."
                    : $"copilot envelope data.model is '{observedModel}' but --model is '{model}'.",
                "re-run the reviewer with the same --model value.");
        }

        if (runtime == CrossRuntimeReviewRuntimes.Copilot
            && effort is not null
            && !CrossRuntimeReviewJsonlVerdict.TryParseCopilotEffort(content, observedModel!, effort, out var effortError))
        {
            return Refuse(writer, format, "record", CrossRuntimeReviewCauses.EffortMismatch,
                effortError,
                "re-run the reviewer with the same --effort value.");
        }

        if (!string.Equals(verdict.PacketDigest, currentDigest, StringComparison.OrdinalIgnoreCase))
        {
            return Refuse(writer, format, "record", CrossRuntimeReviewCauses.DigestMismatch,
                $"the verdict echoes packet_digest '{verdict.PacketDigest}' but the current digest is '{currentDigest}'.",
                "re-run the design review against the current packet bytes.");
        }

        var write = options.ContainsKey("--write");
        var recordedAt = (Clock ?? (() => DateTimeOffset.UtcNow))().ToUniversalTime();
        var targetRepo = designResolution.TargetRepo ?? string.Empty;
        var draft = new CrossRuntimeDesignReviewRecord
        {
            ArtifactKind = CrossRuntimeDesignReviewRecord.ArtifactKindValue,
            PacketDigest = currentDigest,
            TargetRepo = targetRepo,
            ExecutionUnit = unit,
            Domain = designResolution.Domain!,
            Team = designResolution.Team!,
            Kind = CrossRuntimeReviewRecord.KindDesign,
            Runtime = runtime,
            RuntimeVersion = runtimeVersion,
            ConductorRuntime = declaration.ConductorRuntime,
            Relation = CrossRuntimeReviewRecord.RelationFor(runtime, declaration.ConductorRuntime),
            Model = model,
            Effort = effort,
            Verdict = verdict.Verdict,
            BlockingFindings = verdict.BlockingFindings,
            Notes = verdict.Notes,
            RecordedAt = recordedAt,
            RawVerdictFile = string.Empty,
            RawVerdictSha256 = CrossRuntimeReviewStore.Sha256Hex(raw),
        };
        var record = draft with { RawVerdictFile = CrossRuntimeDesignReviewStore.RawRelativePath(draft) };
        var recordRelative = CrossRuntimeDesignReviewStore.RecordRelativePath(record);
        var commentBody = RenderDesignCommentBody(record, recordRelative);

        string? commentOut = null;
        if (write)
        {
            var stored = CrossRuntimeDesignReviewStore.Write(context.RepoRoot, record, raw);
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

        var resolution = CrossRuntimeReviewDesignTeamResolver.ToCrossRuntimeResolution(designResolution);
        var result = new CrossRuntimeDesignReviewRecordResult
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
            Durability = write
                ? $"The record exists only in this checkout until it is committed and pushed: commit `{recordRelative}` and `{record.RawVerdictFile}` in the host and push."
                : "Dry run: nothing was written. Re-run with --write to store the record.",
        };

        if (format == FormatJson)
        {
            writer.WriteLine(JsonSerializer.Serialize(result, RequestJsonOptions));
        }
        else
        {
            writer.WriteLine("# Cross-runtime design review record (G835)");
            writer.WriteLine();
            writer.WriteLine($"- mode: {result.Mode}");
            writer.WriteLine($"- outcome: {result.Outcome}");
            writer.WriteLine($"- record file: {result.RecordFile}");
            writer.WriteLine($"- raw verdict copy: {result.RawVerdictCopy}");
            writer.WriteLine($"- relation: {record.Relation} (conductor runtime {record.ConductorRuntime})");
            writer.WriteLine($"- verdict: {record.Verdict}");
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
        if (record.Model is not null)
        {
            builder.Append($"- model: {record.Model}\n");
        }

        if (record.Effort is not null)
        {
            builder.Append($"- effort: {record.Effort}\n");
        }

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

    internal static string RenderDesignCommentBody(CrossRuntimeDesignReviewRecord record, string recordRelativePath)
    {
        var reviewer = record.Relation == CrossRuntimeReviewRecord.RelationCrossRuntime
            ? "cross-runtime design review"
            : "independent same-runtime subagent design review";
        var builder = new StringBuilder();
        builder.Append($"## {char.ToUpperInvariant(reviewer[0])}{reviewer[1..]}: {record.Verdict}\n\n");
        builder.Append($"- reviewer: {reviewer}\n");
        builder.Append($"- runtime: {record.Runtime}\n");
        builder.Append($"- runtime version: {record.RuntimeVersion}\n");
        if (record.Model is not null)
        {
            builder.Append($"- model: {record.Model}\n");
        }

        if (record.Effort is not null)
        {
            builder.Append($"- effort: {record.Effort}\n");
        }

        builder.Append($"- conductor runtime: {record.ConductorRuntime}\n");
        builder.Append($"- packet digest: {record.PacketDigest}\n");
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

        builder.Append($"\nRecorded as `{recordRelativePath}` by `intent-cli review cross-runtime record --kind design`.\n");
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

        var kind = ResolveKind(options);
        if (string.Equals(kind, CrossRuntimeReviewRecord.KindDesign, StringComparison.Ordinal))
        {
            return ExecuteDesignStatus(context, options, writer, format);
        }

        return ExecuteImplementationStatus(context, options, writer, format);
    }

    private static int ExecuteImplementationStatus(
        CliContext context,
        Dictionary<string, string> options,
        TextWriter writer,
        string format)
    {
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
        var result = new CrossRuntimeReviewImplementationStatusResult
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

        WriteImplementationStatusResult(writer, format, result, gate, read.Records.Count, entry => entry.HeadSha);
        return 0;
    }

    private static int ExecuteDesignStatus(
        CliContext context,
        Dictionary<string, string> options,
        TextWriter writer,
        string format)
    {
        foreach (var forbidden in new[] { "--repo", "--pr", "--head-sha" })
        {
            if (options.ContainsKey(forbidden))
            {
                return Refuse(writer, format, "status", CrossRuntimeReviewCauses.ArgumentInvalid,
                    $"design review status refuses {forbidden}; pass --kind design --execution-unit <unit> only.",
                    "omit PR arguments for design review status.");
            }
        }

        if (!TryRequired(options, "--execution-unit", writer, format, "status", out var unit))
        {
            return 1;
        }

        if (!KnowledgeWriteBackRecord.TryValidateExecutionUnit(unit, out var unitError))
        {
            return Refuse(writer, format, "status", CrossRuntimeReviewCauses.ArgumentInvalid, $"--execution-unit is invalid: {unitError}", "pass the canonical execution unit id.");
        }

        var designResolution = CrossRuntimeReviewDesignTeamResolver.Resolve(context.RepoRoot, unit);
        if (!designResolution.Resolved)
        {
            return RefuseDesignResolution(writer, format, "status", designResolution);
        }

        var resolution = CrossRuntimeReviewDesignTeamResolver.ToCrossRuntimeResolution(designResolution);
        var declared = context.Config.CrossRuntimeReview.TryGetDeclared(resolution.Domain, resolution.Team, out var declaration);
        var packetDirectory = CrossRuntimeReviewPaths.PacketDirectory(context.RepoRoot, unit);
        string? digest = null;
        if (CrossRuntimeDesignReviewDigest.TryReadFromDirectory(packetDirectory, out var packet, out _))
        {
            digest = CrossRuntimeDesignReviewDigest.Compute(packet);
        }

        var read = CrossRuntimeDesignReviewStore.Read(context.RepoRoot, unit);
        var gate = digest is null
            ? new CrossRuntimeReviewGateResult
            {
                Decision = CrossRuntimeReviewGate.DecisionNotRequired,
                Reasons = [],
                Records = [],
                Unreadable = read.Unreadable,
            }
            : CrossRuntimeReviewGate.EvaluateDesign(declared ? declaration : null, resolution, digest, read);
        var result = new CrossRuntimeReviewStatusResult
        {
            Command = $"{CommandName} status",
            Kind = CrossRuntimeReviewRecord.KindDesign,
            ExecutionUnit = unit,
            PacketDigest = digest,
            Resolution = resolution,
            Declared = declared,
            DeclarationSource = declared ? Models.CrossRuntimeReviewConfig.Source : null,
            ConductorRuntime = declared ? declaration.ConductorRuntime : null,
            RecordFiles = read.Records.Select(stored => stored.RelativePath).ToArray(),
            Gate = gate,
        };

        WriteDesignStatusResult(writer, format, result, gate, read.Records.Count, entry => entry.PacketDigest);
        return 0;
    }

    private static void WriteImplementationStatusResult(
        TextWriter writer,
        string format,
        CrossRuntimeReviewImplementationStatusResult result,
        CrossRuntimeReviewGateResult gate,
        int totalRecords,
        Func<CrossRuntimeReviewGateRecordEntry, string?> keySelector)
    {
        if (format == FormatJson)
        {
            writer.WriteLine(JsonSerializer.Serialize(result, JsonOptions));
            return;
        }

        WriteStatusMarkdown(writer, "Cross-runtime review status (G834)", result.Repo, result.Pr, result.HeadSha, null, result.Resolution, result.Declared, result.DeclarationSource, result.ConductorRuntime, gate, totalRecords, keySelector);
    }

    private static void WriteDesignStatusResult(
        TextWriter writer,
        string format,
        CrossRuntimeReviewStatusResult result,
        CrossRuntimeReviewGateResult gate,
        int totalRecords,
        Func<CrossRuntimeReviewGateRecordEntry, string?> keySelector)
    {
        if (format == FormatJson)
        {
            writer.WriteLine(JsonSerializer.Serialize(result, RequestJsonOptions));
            return;
        }

        WriteStatusMarkdown(writer, "Cross-runtime design review status (G835)", result.Repo, result.Pr, result.HeadSha, result.PacketDigest, result.Resolution, result.Declared, result.DeclarationSource, result.ConductorRuntime, gate, totalRecords, keySelector);
    }

    private static void WriteStatusMarkdown(
        TextWriter writer,
        string label,
        string? repo,
        int? pr,
        string? headSha,
        string? packetDigest,
        CrossRuntimeReviewResolution resolution,
        bool declared,
        string? declarationSource,
        string? conductorRuntime,
        CrossRuntimeReviewGateResult gate,
        int totalRecords,
        Func<CrossRuntimeReviewGateRecordEntry, string?> keySelector)
    {
        writer.WriteLine($"# {label}");
        writer.WriteLine();
        if (repo is not null)
        {
            writer.WriteLine($"- repo: {repo} pr: {pr!.Value.ToString(CultureInfo.InvariantCulture)} head: {headSha}");
        }

        if (packetDigest is not null)
        {
            writer.WriteLine($"- packet digest: {packetDigest}");
        }

        writer.WriteLine($"- execution unit: {resolution.ExecutionUnit} ({resolution.ExecutionUnitSource})");
        writer.WriteLine($"- domain: {resolution.Domain} ({resolution.DomainSource})");
        writer.WriteLine($"- team: {resolution.Team} ({resolution.TeamSource})");
        writer.WriteLine(declared
            ? $"- declared: yes ({declarationSource}), conductor runtime {conductorRuntime}"
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
            writer.WriteLine(totalRecords == 0 ? "- none" : $"- {totalRecords} record(s); not evaluated for an undeclared team");
        }

        foreach (var entry in gate.Records)
        {
            var key = keySelector(entry);
            writer.WriteLine($"- [{entry.Status}] {entry.Runtime} ({entry.Relation}) {entry.Verdict} on {key} at {entry.RecordedAt:O}: {entry.File}");
        }

        foreach (var unreadable in gate.Unreadable)
        {
            writer.WriteLine($"- [unreadable] {unreadable.RelativePath}: {unreadable.Error}");
        }
    }

    // ── shared ─────────────────────────────────────────────────────────

    private static readonly IReadOnlyList<string> CommonValueFlags = ["--repo", "--pr", "--head-sha", "--execution-unit", "--format"];

    private static readonly FlagSet RequestFlags = new(
        [.. CommonValueFlags, "--kind", "--runtime", "--clone", "--out-dir", "--model", "--effort", "--opencode-provider-config"], []);

    private static readonly FlagSet RecordFlags = new(
        [.. CommonValueFlags, "--kind", "--runtime", "--runtime-version", "--verdict-file", "--packet-digest", "--comment-out", "--model", "--effort"], ["--write", "--dry-run"]);

    private static readonly FlagSet StatusFlags = new([.. CommonValueFlags, "--kind"], []);

    private static string ResolveKind(Dictionary<string, string> options) =>
        options.TryGetValue("--kind", out var kind)
            ? kind
            : CrossRuntimeReviewRecord.KindImplementation;

    private static bool TryModelAndEffort(
        Dictionary<string, string> options,
        TextWriter writer,
        string format,
        string subcommand,
        string runtime,
        out string? model,
        out string? effort)
    {
        model = null;
        effort = null;
        options.TryGetValue("--effort", out effort);

        if (!CrossRuntimeReviewRuntimes.AcceptsEffort(runtime))
        {
            if (!TryOptionalModel(options, writer, format, subcommand, out model))
            {
                return false;
            }

            if (effort is not null)
            {
                Refuse(writer, format, subcommand,
                    CrossRuntimeReviewCauses.ArgumentInvalid,
                    $"--effort is accepted only for runtimes {CrossRuntimeReviewRuntimes.Copilot} and {CrossRuntimeReviewRuntimes.Opencode}.",
                    "omit --effort for this runtime.");
                return false;
            }

            return true;
        }

        options.TryGetValue("--model", out model);

        if (!CrossRuntimeReviewRuntimes.TryValidateModel(runtime, model, out var modelError))
        {
            var cause = model is null && CrossRuntimeReviewRuntimes.RequiresModel(runtime)
                ? CrossRuntimeReviewCauses.ModelRequired
                : CrossRuntimeReviewCauses.ModelInvalid;
            Refuse(writer, format, subcommand, cause, modelError, "pass a valid --model value for the runtime.");
            return false;
        }

        if (!CrossRuntimeReviewRuntimes.TryValidateEffort(runtime, effort, out var effortError))
        {
            Refuse(writer, format, subcommand,
                effort is not null && effort.Length == 0
                    || effortError.Contains("must not", StringComparison.Ordinal)
                    || effortError.Contains("must be one of", StringComparison.Ordinal)
                    || effortError.Contains("Unicode", StringComparison.Ordinal)
                    ? CrossRuntimeReviewCauses.EffortInvalid
                    : CrossRuntimeReviewCauses.ArgumentInvalid,
                effortError,
                runtime is CrossRuntimeReviewRuntimes.Copilot or CrossRuntimeReviewRuntimes.Opencode
                    ? "pass a valid --effort value for the runtime."
                    : "omit --effort for this runtime.");
            return false;
        }

        return true;
    }

    private static bool TryOptionalModel(
        Dictionary<string, string> options,
        TextWriter writer,
        string format,
        string subcommand,
        out string? model)
    {
        model = null;
        if (!options.TryGetValue("--model", out var value))
        {
            return true;
        }

        if (!CrossRuntimeReviewRuntimes.TryValidateModelCharacters(value, out var error))
        {
            Refuse(writer, format, subcommand, CrossRuntimeReviewCauses.ModelInvalid, error, "pass a non-empty model name without leading '-' or control characters.");
            return false;
        }

        model = value;
        return true;
    }

    private static int RefuseDesignResolution(
        TextWriter writer,
        string format,
        string subcommand,
        CrossRuntimeReviewDesignTeamResolver.DesignResolution resolution) =>
        Refuse(writer, format, subcommand, resolution.Cause!, resolution.Detail ?? string.Empty, resolution.Fix ?? string.Empty,
            CrossRuntimeReviewDesignTeamResolver.ToCrossRuntimeResolution(resolution));

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

            if (index + 1 >= args.Length)
            {
                writer.WriteLine($"{argument} requires a value.");
                writer.WriteLine(usage);
                return false;
            }

            var value = args[++index];
            if (argument != "--model" && string.IsNullOrEmpty(value))
            {
                writer.WriteLine($"{argument} requires a value.");
                writer.WriteLine(usage);
                return false;
            }

            options[argument] = value;
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
                "pass --runtime codex, claude, cursor, copilot, or opencode.");
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

    private static bool TryRefuseProtectedPacketDirectory(
        TextWriter writer,
        string format,
        string subcommand,
        string packetDirectory,
        out int refusal)
    {
        foreach (var packetFile in CrossRuntimeDesignReviewDigest.PacketFileNames)
        {
            var packetPath = Path.Combine(packetDirectory, packetFile);
            if (!CrossRuntimeReviewHomeAccessGuard.TryRefuseProtectedPath(packetPath, out var detail))
            {
                continue;
            }

            refusal = Refuse(writer, format, subcommand, CrossRuntimeReviewCauses.PathInvalid, detail,
                "do not read operator Copilot or OpenCode config directories.");
            return true;
        }

        refusal = 0;
        return false;
    }

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
        writer.WriteLine("review cross-runtime (G834/G835)");
        writer.WriteLine(RequestUsage);
        writer.WriteLine(RecordUsage);
        writer.WriteLine(StatusUsage);
        writer.WriteLine("Use --kind design for packet-digest-keyed design review (G835); --kind implementation or PR arguments select implementation review (G834).");
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

internal sealed record CrossRuntimeReviewImplementationRequestResult
{
    [JsonPropertyName("command")] public required string Command { get; init; }
    [JsonPropertyName("outcome")] public required string Outcome { get; init; }
    [JsonPropertyName("repo")] public required string Repo { get; init; }
    [JsonPropertyName("pr")] public required int Pr { get; init; }
    [JsonPropertyName("head_sha")] public required string HeadSha { get; init; }
    [JsonPropertyName("execution_unit")] public required string ExecutionUnit { get; init; }
    [JsonPropertyName("runtime")] public required string Runtime { get; init; }
    [JsonPropertyName("model")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Model { get; init; }
    [JsonPropertyName("effort")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Effort { get; init; }
    [JsonPropertyName("opencode_provider_config")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? OpencodeProviderConfig { get; init; }
    [JsonPropertyName("out_dir")] public required string OutDir { get; init; }
    [JsonPropertyName("files")] public required IReadOnlyList<string> Files { get; init; }
    [JsonPropertyName("invocation")] public required string Invocation { get; init; }
    [JsonPropertyName("run_by")] public required string RunBy { get; init; }
    [JsonPropertyName("raw_verdict_file")] public required string RawVerdictFile { get; init; }
    [JsonPropertyName("read_only_enforcement")] public required string ReadOnlyEnforcement { get; init; }
    [JsonPropertyName("no_execution_boundary")] public required string NoExecutionBoundary { get; init; }
    [JsonPropertyName("terms")] public required string Terms { get; init; }
}

internal sealed record CrossRuntimeReviewRequestResult
{
    [JsonPropertyName("command")] public required string Command { get; init; }
    [JsonPropertyName("outcome")] public required string Outcome { get; init; }
    [JsonPropertyName("kind")] public required string Kind { get; init; }
    [JsonPropertyName("repo")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Repo { get; init; }
    [JsonPropertyName("pr")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Pr { get; init; }
    [JsonPropertyName("head_sha")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? HeadSha { get; init; }
    [JsonPropertyName("packet_digest")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PacketDigest { get; init; }
    [JsonPropertyName("execution_unit")] public required string ExecutionUnit { get; init; }
    [JsonPropertyName("runtime")] public required string Runtime { get; init; }
    [JsonPropertyName("model")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Model { get; init; }
    [JsonPropertyName("effort")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Effort { get; init; }
    [JsonPropertyName("opencode_provider_config")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? OpencodeProviderConfig { get; init; }
    [JsonPropertyName("out_dir")] public required string OutDir { get; init; }
    [JsonPropertyName("workspace")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Workspace { get; init; }
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

internal sealed record CrossRuntimeReviewImplementationStatusResult
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

internal sealed record CrossRuntimeReviewStatusResult
{
    [JsonPropertyName("command")] public required string Command { get; init; }
    [JsonPropertyName("kind")] public required string Kind { get; init; }
    [JsonPropertyName("repo")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Repo { get; init; }
    [JsonPropertyName("pr")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Pr { get; init; }
    [JsonPropertyName("head_sha")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? HeadSha { get; init; }
    [JsonPropertyName("packet_digest")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PacketDigest { get; init; }
    [JsonPropertyName("execution_unit")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ExecutionUnit { get; init; }
    [JsonPropertyName("resolution")] public required CrossRuntimeReviewResolution Resolution { get; init; }
    [JsonPropertyName("declared")] public required bool Declared { get; init; }
    [JsonPropertyName("declaration_source")] public string? DeclarationSource { get; init; }
    [JsonPropertyName("conductor_runtime")] public string? ConductorRuntime { get; init; }
    [JsonPropertyName("record_files")] public required IReadOnlyList<string> RecordFiles { get; init; }
    [JsonPropertyName("gate")] public required CrossRuntimeReviewGateResult Gate { get; init; }
}

internal sealed record CrossRuntimeDesignReviewRecordResult
{
    [JsonPropertyName("command")] public required string Command { get; init; }
    [JsonPropertyName("mode")] public required string Mode { get; init; }
    [JsonPropertyName("outcome")] public required string Outcome { get; init; }
    [JsonPropertyName("resolution")] public required CrossRuntimeReviewResolution Resolution { get; init; }
    [JsonPropertyName("declaration_source")] public required string DeclarationSource { get; init; }
    [JsonPropertyName("record")] public required CrossRuntimeDesignReviewRecord Record { get; init; }
    [JsonPropertyName("record_file")] public required string RecordFile { get; init; }
    [JsonPropertyName("raw_verdict_copy")] public required string RawVerdictCopy { get; init; }
    [JsonPropertyName("comment_body")] public required string CommentBody { get; init; }
    [JsonPropertyName("comment_out")] public string? CommentOut { get; init; }
    [JsonPropertyName("durability")] public required string Durability { get; init; }
}
