using System.Text.Json;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G374: <c>intent-cli worker signal &lt;blocker|follow-up|scope-warning&gt;</c>.
/// A child implementation worker raises a structured signal back to host
/// review/design automation by posting a marker-wrapped comment on the
/// assigned Issue/PR and adding <c>intent-signal-sent</c>. GitHub-only by
/// data flow — it uses the comment gateway + label mutator seams and
/// never reads or writes host queue-state / packet / intent metadata.
///
/// Targets by kind:
/// - <c>blocker</c>     → <c>--issue &lt;n&gt;</c> (decline before implementation)
/// - <c>follow-up</c>   → <c>--pr &lt;n&gt;</c> (defect/design gap found mid-PR)
/// - <c>scope-warning</c> → <c>--issue &lt;n&gt;</c> or <c>--pr &lt;n&gt;</c>
///
/// Dry-run by default: no comment is posted and no label is applied
/// unless <c>--write</c> is passed.
/// </summary>
internal static class WorkerSignalCommand
{
    private const string FormatText = "text";
    private const string FormatJson = "json";

    /// <summary>Test seam: inject a fake comment gateway so no GitHub call is made.</summary>
    public static Func<IGitHubSignalGateway>? GatewayFactory { get; set; }

    /// <summary>Test seam: inject a fake label mutator so no GitHub call is made.</summary>
    public static Func<IGitHubLabelMutator>? MutatorFactory { get; set; }

    /// <summary>
    /// Test sentinel: must NEVER be invoked. Locks the "no nested
    /// provider launch" guarantee for the signal surface.
    /// </summary>
    public static Func<bool>? NestedProviderLauncher { get; set; }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    public static int Execute(CliContext context, string[] args, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);

        if (args.Length == 0
            || string.Equals(args[0], "--help", StringComparison.Ordinal))
        {
            WriteHelp(writer);
            return args.Length == 0 ? 1 : 0;
        }

        var signalKind = args[0];
        if (!WorkerSignalContract.IsKnownKind(signalKind))
        {
            writer.WriteLine(
                $"Unknown signal kind '{signalKind}'. Supported: {string.Join(", ", WorkerSignalContract.AllKinds)}.");
            return 1;
        }

        if (!TryParseArguments(
                args[1..],
                out var repo,
                out var target,
                out var number,
                out var bodyPathArg,
                out var mode,
                out var githubOnly,
                out var format,
                out var error))
        {
            writer.WriteLine(error);
            return 1;
        }

        if (!WorkerSignalContract.IsTargetAllowed(signalKind!, target!))
        {
            writer.WriteLine(
                $"Signal kind '{signalKind}' cannot target a {target}. Allowed: {string.Join(", ", WorkerSignalContract.AllowedTargets(signalKind!))}.");
            return 1;
        }

        string body;
        try
        {
            var bodyPath = ResolveBodyPath(context.RepoRoot, bodyPathArg!);
            if (!File.Exists(bodyPath))
            {
                writer.WriteLine($"Signal body file was not found at {bodyPath}");
                return 1;
            }
            body = File.ReadAllText(bodyPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            writer.WriteLine($"failed to read signal body file: {exception.Message}");
            return 1;
        }

        if (string.IsNullOrWhiteSpace(body))
        {
            writer.WriteLine("Signal body file must not be empty.");
            return 1;
        }

        IGitHubLabelMutator mutator;
        try
        {
            mutator = MutatorFactory?.Invoke() ?? new GhCliGitHubLabelMutator();
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException)
        {
            writer.WriteLine($"failed to initialize GitHub mutator: {exception.Message}");
            return 1;
        }

        IReadOnlyList<GitHubAutomationLabel> currentLabels;
        try
        {
            currentLabels = mutator.ReadLabels(repo!, target!, number);
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException)
        {
            writer.WriteLine(
                $"failed to read current labels for {target} #{number} in {repo}: {exception.Message}");
            return 1;
        }

        var currentNames = LabelNames(currentLabels);
        var plan = WorkerSignalContract.PlanSentTransition(currentNames);
        var marker = WorkerSignalContract.BuildMarker(signalKind!, target!, number);
        var commentBody = WorkerSignalContract.BuildCommentBody(signalKind!, target!, number, body);

        var isWrite = string.Equals(mode, WorkerClaimCompleteConstants.Modes.Write, StringComparison.Ordinal);
        var posted = false;
        var applied = false;
        string? commentRef = null;
        var warnings = new List<string>();

        if (currentNames.Contains(WorkerSignalContract.Labels.SignalSent, StringComparer.Ordinal))
        {
            warnings.Add(
                $"{target} #{number} already carries '{WorkerSignalContract.Labels.SignalSent}'; this posts an additional signal comment without a duplicate label add.");
        }

        if (isWrite)
        {
            IGitHubSignalGateway gateway;
            try
            {
                gateway = GatewayFactory?.Invoke() ?? new GhCliGitHubSignalGateway();
            }
            catch (Exception exception) when (exception is InvalidOperationException or IOException)
            {
                writer.WriteLine($"failed to initialize GitHub signal gateway: {exception.Message}");
                return 1;
            }

            try
            {
                commentRef = gateway.PostComment(repo!, target!, number, commentBody);
                posted = true;
            }
            catch (Exception exception) when (exception is InvalidOperationException or IOException)
            {
                writer.WriteLine(
                    $"failed to post signal comment on {target} #{number} in {repo}: {exception.Message}");
                return 1;
            }

            if (plan.HasChanges)
            {
                try
                {
                    mutator.ApplyLabelTransitions(repo!, target!, number, plan.AddLabels, plan.RemoveLabels);
                    applied = true;
                }
                catch (Exception exception) when (exception is InvalidOperationException or IOException)
                {
                    writer.WriteLine(
                        $"posted signal comment {commentRef} but failed to apply '{WorkerSignalContract.Labels.SignalSent}' on {target} #{number} in {repo}: {exception.Message}");
                    return 1;
                }
            }
        }

        var summary = isWrite
            ? $"Posted {signalKind} signal on {target} #{number} and marked it {WorkerSignalContract.Labels.SignalSent}."
            : $"Dry run: would post {signalKind} signal on {target} #{number} and add {WorkerSignalContract.Labels.SignalSent}.";

        var result = new WorkerSignalResult
        {
            SignalKind = signalKind!,
            Repo = repo!,
            Target = target!,
            Number = number,
            Mode = mode,
            Proceed = true,
            Posted = posted,
            Applied = applied,
            CommentRef = commentRef,
            Marker = marker,
            AddLabels = plan.AddLabels,
            RemoveLabels = plan.RemoveLabels,
            CurrentLabels = currentNames,
            Summary = summary,
            Warnings = warnings,
            GithubOnly = githubOnly ? true : null,
        };

        if (string.Equals(format, FormatJson, StringComparison.Ordinal))
        {
            writer.WriteLine(JsonSerializer.Serialize(result, JsonOptions));
        }
        else
        {
            WriteText(writer, result);
        }

        return 0;
    }

    private static IReadOnlyList<string> LabelNames(IReadOnlyList<GitHubAutomationLabel>? labels)
    {
        if (labels is null || labels.Count == 0)
        {
            return Array.Empty<string>();
        }

        var names = new List<string>(labels.Count);
        foreach (var label in labels)
        {
            if (!string.IsNullOrEmpty(label.Name))
            {
                names.Add(label.Name);
            }
        }
        return names;
    }

    private static string ResolveBodyPath(string repoRoot, string rawPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rawPath);
        if (Path.IsPathRooted(rawPath))
        {
            return Path.GetFullPath(rawPath);
        }
        var baseDir = string.IsNullOrWhiteSpace(repoRoot) ? Directory.GetCurrentDirectory() : repoRoot;
        return Path.GetFullPath(Path.Combine(baseDir, rawPath));
    }

    private static void WriteText(TextWriter writer, WorkerSignalResult result)
    {
        writer.WriteLine($"# Worker signal: {result.SignalKind} on {result.Target} #{result.Number} ({result.Repo})");
        writer.WriteLine();
        writer.WriteLine($"- mode: {result.Mode}");
        writer.WriteLine($"- posted: {result.Posted}");
        writer.WriteLine($"- applied: {result.Applied}");
        if (!string.IsNullOrWhiteSpace(result.CommentRef))
        {
            writer.WriteLine($"- comment: {result.CommentRef}");
        }
        writer.WriteLine($"- add: {(result.AddLabels.Count == 0 ? "(none)" : string.Join(", ", result.AddLabels))}");
        writer.WriteLine($"- remove: {(result.RemoveLabels.Count == 0 ? "(none)" : string.Join(", ", result.RemoveLabels))}");
        writer.WriteLine($"- summary: {result.Summary}");
        writer.WriteLine();
        writer.WriteLine("## Warnings");
        if (result.Warnings.Count == 0)
        {
            writer.WriteLine("- (none)");
        }
        else
        {
            foreach (var item in result.Warnings)
            {
                writer.WriteLine($"- {item}");
            }
        }
    }

    private static bool TryParseArguments(
        string[] args,
        out string? repo,
        out string? target,
        out int number,
        out string? bodyPath,
        out string mode,
        out bool githubOnly,
        out string format,
        out string error)
    {
        repo = null;
        target = null;
        number = 0;
        bodyPath = null;
        mode = WorkerClaimCompleteConstants.Modes.DryRun;
        githubOnly = false;
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
                        error = "--repo requires a value (e.g. owner/repo).";
                        return false;
                    }
                    repo = args[index + 1];
                    index++;
                    break;

                case "--issue":
                    if (!TryParseTarget(args, ref index, WorkerSignalContract.Targets.Issue, ref target, ref number, out error))
                    {
                        return false;
                    }
                    break;

                case "--pr":
                    if (!TryParseTarget(args, ref index, WorkerSignalContract.Targets.Pr, ref target, ref number, out error))
                    {
                        return false;
                    }
                    break;

                case "--from-file":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--from-file requires a path.";
                        return false;
                    }
                    bodyPath = args[index + 1];
                    index++;
                    break;

                case "--write":
                    mode = WorkerClaimCompleteConstants.Modes.Write;
                    break;

                case "--dry-run":
                    mode = WorkerClaimCompleteConstants.Modes.DryRun;
                    break;

                case "--github-only":
                    githubOnly = true;
                    break;

                case "--format":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--format requires a value (text or json).";
                        return false;
                    }
                    var requestedFormat = args[index + 1];
                    if (!string.Equals(requestedFormat, FormatText, StringComparison.Ordinal)
                        && !string.Equals(requestedFormat, FormatJson, StringComparison.Ordinal))
                    {
                        error = $"--format must be 'text' or 'json' (got '{requestedFormat}').";
                        return false;
                    }
                    format = requestedFormat;
                    index++;
                    break;

                default:
                    error =
                        $"Unknown argument '{argument}'. Supported: --repo <owner/repo> (--issue <n> | --pr <n>) --from-file <path> [--write] [--dry-run] [--github-only] [--format text|json].";
                    return false;
            }
        }

        if (string.IsNullOrWhiteSpace(repo))
        {
            error = "--repo is required (e.g. --repo owner/repo).";
            return false;
        }
        if (string.IsNullOrWhiteSpace(target) || number <= 0)
        {
            error = "exactly one of --issue <n> or --pr <n> is required.";
            return false;
        }
        if (string.IsNullOrWhiteSpace(bodyPath))
        {
            error = "--from-file <path> is required.";
            return false;
        }

        return true;
    }

    private static bool TryParseTarget(
        string[] args,
        ref int index,
        string targetKind,
        ref string? target,
        ref int number,
        out string error)
    {
        error = string.Empty;
        if (target is not null)
        {
            error = "specify exactly one of --issue or --pr, not both.";
            return false;
        }
        if (index + 1 >= args.Length
            || !int.TryParse(args[index + 1], System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out number)
            || number <= 0)
        {
            error = $"--{targetKind} requires a positive integer.";
            return false;
        }
        target = targetKind;
        index++;
        return true;
    }

    private static void WriteHelp(TextWriter writer)
    {
        writer.WriteLine("intent-cli worker signal <blocker|follow-up|scope-warning>");
        writer.WriteLine(
            "Usage: intent-cli worker signal <kind> --repo <owner/repo> (--issue <n> | --pr <n>) --from-file <path> [--write] [--github-only] [--format text|json]");
        writer.WriteLine("Kinds:");
        writer.WriteLine("- blocker      (--issue <n>): decline before implementation.");
        writer.WriteLine("- follow-up    (--pr <n>): follow-up defect / design gap found while a PR is open.");
        writer.WriteLine("- scope-warning (--issue <n> | --pr <n>): finding belongs to host metadata or widens scope.");
        writer.WriteLine("Default is dry-run; pass --write to post the comment and add intent-signal-sent.");
        writer.WriteLine("See `intent-cli guide worker signal` for paste-ready comment templates.");
    }
}
